using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown;

/// <summary>
/// B站直播流解析与录制。直播流是无限流：录制持续写入直到用户取消（Ctrl+C）或主播下播。
/// </summary>
public static class LiveStreamUtil
{
    /// <summary>
    /// 解析直播间信息与一条可录制的 flv 直播流地址。
    /// </summary>
    public static async Task<(string Url, string Title, string Uname, string RoomId)> ResolveAsync(string roomId, CancellationToken token = default)
    {
        if (!long.TryParse(roomId, out _))
            throw new ArgumentException($"直播间 ID 必须是数字，当前值: '{roomId}'");

        string infoApi = $"https://api.live.bilibili.com/room/v1/Room/get_info?room_id={roomId}";
        string infoJson = await HTTPUtil.GetWebSourceAsync(infoApi, token: token);
        using var infoDoc = JsonDocument.Parse(infoJson);
        var info = infoDoc.RootElement.GetPropertySafe("data");
        string title = info.GetValueAsStringSafe("title");
        if (title == "") title = $"直播间{roomId}";
        string uname = info.GetValueAsStringSafe("uname");
        if (info.GetInt32Safe("live_status") != 1)
            throw new InvalidOperationException($"直播间 {roomId} 当前未在直播");

        string playApi = "https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo" +
            $"?room_id={roomId}&protocol=0,1&format=0,1,2&codec=0,1&qn=10000&platform=web";
        string playJson = await HTTPUtil.GetWebSourceAsync(playApi, token: token);
        using var playDoc = JsonDocument.Parse(playJson);
        var data = playDoc.RootElement.GetPropertySafe("data");
        var streams = data.GetPropertySafe("playurl_info").GetPropertySafe("playurl").EnumerateArraySafe("stream");
        foreach (var stream in streams)
        {
            foreach (var format in stream.EnumerateArraySafe("format"))
            {
                if (format.GetValueAsStringSafe("format_name") != "flv") continue;
                foreach (var codec in format.EnumerateArraySafe("codec"))
                {
                    var baseUrl = codec.GetValueAsStringSafe("base_url");
                    if (baseUrl == "") continue;
                    foreach (var urlInfo in codec.EnumerateArraySafe("url_info"))
                    {
                        var host = urlInfo.GetValueAsStringSafe("host");
                        var extra = urlInfo.GetValueAsStringSafe("extra");
                        if (host == "") continue;
                        return (host + baseUrl + extra, title, uname, roomId);
                    }
                }
            }
        }
        throw new InvalidOperationException($"无法获取直播间 {roomId} 的可录制流地址");
    }

    /// <summary>
    /// 把直播流持续写入本地文件，直到流结束、取消或重连耗尽。
    /// 写入 <c>path.part</c> 临时文件，成功/取消时原子改名为最终路径：
    /// B 站直播流地址带时效参数，长时间录制中过期是常态，网络瞬断或地址过期时
    /// 重新解析流地址续录（最多 <see cref="ReconnectLimit"/> 次）。
    /// 全部重连失败时保留 .part 中已录制的内容并抛错。
    /// 未收到任何字节（流立即断开/下播前无数据）时返回 false 且不生成空文件，
    /// 调用方据此避免把"什么都没录到"报告为成功。
    /// </summary>
    public static async Task<bool> DownloadToFileAsync(string roomId, string path, Action<long>? onProgress, CancellationToken token = default)
    {
        const int ReconnectLimit = 3;
        // 每段独立文件：重连后的新 FLV 流含新的 FLV 头与重置时间戳，
        // 直接追加到旧流末尾的原始字节拼接不保证可播放。记录在独立分段里，
        // 录制结束后用 FFmpeg concat/remux 合成最终文件。
        var segDir = path + ".segs";
        if (Directory.Exists(segDir)) Directory.Delete(segDir, true);
        Directory.CreateDirectory(segDir);

        long total = 0;
        int reconnect = 0;
        int segIndex = 0;
        var segmentFiles = new List<string>();

        // 重连逻辑：重连计数递增，超限则保留已录分段并抛原异常。
        // 等待采用指数退避：零进展 EOF 也计入重试预算（见下），避免高速轮询。
        async Task ReconnectOrThrow(Exception ex)
        {
            reconnect++;
            if (reconnect > ReconnectLimit)
            {
                Logger.LogWarn($"直播流中断且 {ReconnectLimit} 次重连失败，已录制内容保留在 {segDir}");
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
            int backoffMs = Math.Min(3000 * reconnect, 15000); // 3s → 6s → 9s → 15s
            Logger.LogWarn($"直播流中断（{ex.Message}），{backoffMs / 1000} 秒后重连（{reconnect}/{ReconnectLimit}）...");
            await Task.Delay(backoffMs, token);
        }

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var (url, _, _, _) = await ResolveAsync(roomId, token);
                    var segPath = Path.Combine(segDir, $"seg-{segIndex++:000}.flv");
                    long segBytes = await StreamToFileAsync(url, segPath, onProgress, token);
                    // 本段收到任何字节都算有效传输，重置重连预算
                    if (segBytes > 0)
                    {
                        segmentFiles.Add(segPath);
                        total += segBytes;
                        reconnect = 0;
                    }
                    else
                    {
                        // 零字节 EOF：连接刚建立就结束。若直播仍进行，这是连接到期/CDN 切换，
                        // 计入重试预算并退避，避免高速轮询（此前直接 continue 不延迟）。
                        File.Delete(segPath);
                        try
                        {
                            _ = await ResolveAsync(roomId, token);
                            Logger.LogWarn("直播流连接立即结束但直播间仍在直播，正在退避后重连...");
                            await ReconnectOrThrow(new IOException("直播流零字节 EOF"));
                            continue;
                        }
                        catch (InvalidOperationException ex) when (ex.Message.Contains("当前未在直播"))
                        {
                            break; // 确认下播：结束录制
                        }
                        catch (Exception ex) when (ex is HttpRequestException or JsonException)
                        {
                            await ReconnectOrThrow(ex);
                            continue;
                        }
                    }
                    // EOF 后重新查询直播状态：仍在直播则重新解析地址续录，确认下播才结束。
                    try
                    {
                        _ = await ResolveAsync(roomId, token);
                        Logger.LogWarn("直播流连接结束但直播间仍在直播，正在重新解析流地址续录...");
                        continue;
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("当前未在直播"))
                    {
                        break; // 确认下播：结束录制
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException)
                    {
                        await ReconnectOrThrow(ex);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break; // 用户取消：保留已录分段，退出后合成保存
                }
                catch (OperationCanceledException ex)
                {
                    // HttpClient 2 分钟超时抛出的 TaskCanceledException（token 未取消）：按瞬态故障重连
                    await ReconnectOrThrow(ex);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException)
                {
                    // 直播间下播（"当前未在直播"）是终结态而非可恢复故障：正常结束，走合成保存
                    if (ex is InvalidOperationException && ex.Message.Contains("当前未在直播"))
                        break;
                    await ReconnectOrThrow(ex);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 取消发生在重连等待 Task.Delay 或 while 顶部检查（try 外）：先走到循环后的合成保存再退出
        }

        // 未收到任何字节：不生成空文件，返回 false 让调用方明确失败
        if (total == 0)
        {
            try { if (Directory.Exists(segDir)) Directory.Delete(segDir, true); } catch (IOException) { }
            return false;
        }

        // 多段 → FFmpeg concat 合成最终文件；单段直接改名。
        if (segmentFiles.Count == 1)
        {
            File.Move(segmentFiles[0], path, true);
        }
        else
        {
            if (!await ConcatSegmentsAsync(segmentFiles, path, token))
            {
                Logger.LogWarn($"直播分段合成失败，已录制分段保留在 {segDir}");
                return false;
            }
        }
        try { if (Directory.Exists(segDir)) Directory.Delete(segDir, true); } catch (IOException) { }
        return true;
    }

    /// <summary>用 FFmpeg concat demuxer 把多个 FLV 分段合成一个文件。返回是否成功。</summary>
    private static async Task<bool> ConcatSegmentsAsync(List<string> segmentFiles, string outPath, CancellationToken token)
    {
        // concat demuxer 需要逐行列出文件，FFmpeg 通过 ArgumentList 传参无法直接传换行
        // 列表——这里写一个临时 concat 列表文件。路径含特殊字符时 concat 列表需要转义，
        // 用 file '...' 单引号包裹（路径中单引号已由 SanitizeFileName 在文件生成阶段处理）。
        var listPath = outPath + ".concat.txt";
        try
        {
            await File.WriteAllLinesAsync(listPath, segmentFiles.Select(f => $"file '{f.Replace("'", "'\\''")}'"), token);
            var args = new List<string>
            {
                "-loglevel", "warning", "-y",
                "-f", "concat", "-safe", "0",
                "-i", listPath,
                "-c", "copy",
                outPath,
            };
            // 复用统一外部进程执行器（与混流一致）：支持超时/取消时 Kill 整棵进程树
            var spec = new ExternalProcessSpec
            {
                FileName = "ffmpeg",
                Arguments = args,
                TimeoutMs = Core.Config.Current.MuxerTimeoutMinutes * 60_000,
                ToolDisplayName = "ffmpeg",
            };
            int code = await BBDownMuxer.ProcessRunner.RunAsync(spec, token);
            return code == 0 && File.Exists(outPath) && new FileInfo(outPath).Length > 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Logger.LogDebug("直播分段合成失败: {0}", ex.Message);
            return false;
        }
        finally
        {
            try { if (File.Exists(listPath)) File.Delete(listPath); } catch (IOException) { }
        }
    }

    /// <summary>把一条直播流写到独立分段文件，返回本段写入的字节数。</summary>
    private static async Task<long> StreamToFileAsync(string url, string segPath, Action<long>? onProgress, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", HTTPUtil.UserAgent);
        req.Headers.TryAddWithoutValidation("Referer", "https://live.bilibili.com/");
        using var response = (await HTTPUtil.AppHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token)).EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        await using var fs = new FileStream(segPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        long written = 0;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (read == 0) break; // 流结束（主播下播）
                await fs.WriteAsync(buffer.AsMemory(0, read), token);
                written += read;
                onProgress?.Invoke(written);
            }
            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // 跨平台文件名安全：Path.GetInvalidFileNameChars 在 Unix 上只含 NUL 与 '/',
    // 但下载文件可能被复制到 Windows，因此把 Windows 的非法字符一并替换
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars()
        .Union(@"\/:*?""<>|".ToCharArray())
        .Distinct()
        .ToArray();

    /// <summary>把非法文件名字符替换为下划线，返回安全的文件名。</summary>
    public static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(InvalidFileNameChars.Contains(ch) ? '_' : ch);
        var s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "直播" : s;
    }
}

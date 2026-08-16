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
        int infoCode = infoDoc.RootElement.GetInt32Safe("code");
        if (infoCode != 0)
            throw new InvalidOperationException($"获取直播间信息失败(code={infoCode}): {infoDoc.RootElement.GetValueAsStringSafe("message")}");
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
        int playCode = playDoc.RootElement.GetInt32Safe("code");
        if (playCode != 0)
            throw new InvalidOperationException($"获取直播流信息失败(code={playCode}): {playDoc.RootElement.GetValueAsStringSafe("message")}");
        var playData = playDoc.RootElement.GetPropertySafe("data").GetPropertySafe("playurl_info").GetPropertySafe("playurl");
        var url = SelectFlvUrl(playData, out var availableFormats);
        if (url is null)
        {
            // 不列出格式时用户无法区分"不支持"与"没有流"：把接口实际提供的
            // format_name（flv/ts/fmp4）一并报出，明确说明当前仅支持 flv。
            throw new InvalidOperationException(
                $"无法获取直播间 {roomId} 的可录制流地址" +
                (availableFormats.Length > 0
                    ? $"（接口可用格式: {string.Join(", ", availableFormats)}；当前仅支持 flv，HLS/ts/fmp4 暂不支持）"
                    : "（接口未返回任何流）"));
        }
        return (url, title, uname, roomId);
    }

    /// <summary>
    /// 从 getRoomPlayInfo 的 <c>playurl_info.playurl</c> 数据中挑选一条 FLV 直播流地址。
    /// 纯函数（不发起网络请求），供单测注入内联 JSON 覆盖选流逻辑。
    /// 返回第一个可用的 FLV url；<paramref name="availableFormats"/> 收集接口实际提供的
    /// format_name（含被跳过的 ts/fmp4），供调用方在无 FLV 时报出可操作的错误信息。
    /// </summary>
    internal static string? SelectFlvUrl(JsonElement playData, out string[] availableFormats)
    {
        var available = new HashSet<string>();
        string? picked = null;
        foreach (var stream in playData.EnumerateArraySafe("stream"))
        {
            foreach (var format in stream.EnumerateArraySafe("format"))
            {
                var formatName = format.GetValueAsStringSafe("format_name");
                if (formatName != "") available.Add(formatName);
                if (formatName != "flv") continue;
                foreach (var codec in format.EnumerateArraySafe("codec"))
                {
                    var baseUrl = codec.GetValueAsStringSafe("base_url");
                    if (baseUrl == "") continue;
                    foreach (var urlInfo in codec.EnumerateArraySafe("url_info"))
                    {
                        var host = urlInfo.GetValueAsStringSafe("host");
                        var extra = urlInfo.GetValueAsStringSafe("extra");
                        if (host == "") continue;
                        picked ??= host + baseUrl + extra;
                    }
                }
            }
        }
        availableFormats = available.Count == 0 ? [] : available.ToArray();
        return picked;
    }

    /// <summary>
    /// 把直播流持续写入本地文件，直到流结束、取消或重连耗尽。
    /// 写入 <c>path.part</c> 临时文件，成功/取消时原子改名为最终路径：
    /// B 站直播流地址带时效参数，长时间录制中过期是常态，网络瞬断或地址过期时
    /// 重新解析流地址续录（最多 <see cref="ReconnectLimit"/> 次）。
    /// 全部重连失败时保留 .part 中已录制的内容并抛错。
    public enum LiveRecordResult
    {
        Success,
        NoData,
        ConcatFailedWithSegmentsSaved
    }

    /// <summary>
    /// 录制直播流到文件。如果发生断流且房间仍在直播，会自动重连并生成多个分段，
    /// 录制结束后将所有分段合并为最终文件。
    /// </summary>
    public static async Task<LiveRecordResult> DownloadToFileAsync(string roomId, string path, Action<long>? onProgress, CancellationToken token = default)
    {
        const int ReconnectLimit = 3;
        // 每段独立文件：重连后的新 FLV 流含新的 FLV 头与重置时间戳，
        // 直接追加到旧流末尾的原始字节拼接不保证可播放。记录在独立分段里，
        // 录制结束后用 FFmpeg concat/remux 合成最终文件。
        // 分段根目录用绝对路径：自定义 --output 目录（如 --output E:/录制/xxx.flv）时
        // 相对路径会让 FFmpeg concat 列表里的 file '...' 相对 CWD 解析失败。
        var segRoot = Path.GetFullPath(path) + ".segs";

        // 每次录制用带时间戳的独立会话子目录：合并失败保留的分段是用户的可恢复资产，
        // 若下一次启动直接递归删除整个 .segs（旧实现正是如此），保留内容即丢失。
        // 这里只隔离旧会话（提示保留路径），不自动删除非空会话；当前会话完成后清理自己的目录。
        ReportStaleSessions(segRoot);
        var segDir = Path.Combine(segRoot, $"session-{DateTime.Now:yyyyMMdd_HHmmss}-{Guid.NewGuid():N}");
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
                    // progressBase = 已录完分段的累计字节（total），进度回调上报累计值，
                    // 重连后新分段从 total 起报而不是从 0 倒退。
                    // 本段收到任何字节都算有效传输：重置重连预算（读中断返回的已写字节同样计入，
                    // 使 URL 到期/瞬断这类"有数据"的连接中断不消耗预算，不会误终止长录制）
                    var (segBytes, readInterrupted) = await StreamToFileAsync(url, segPath, total, onProgress, token);
                    if (readInterrupted) Logger.LogDebug("直播流连接在读取时中断，已写入 {0} 字节", segBytes);
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
                    // 非用户取消的取消异常：直播流读取已改用无超时的 StreamingHttpClient，
                    // 不会再因 HttpClient.Timeout 抛 TaskCanceledException；此处是
                    // ResolveAsync（仍走全局 2 分钟超时客户端）或其它内部调用超时/取消，
                    // 按瞬态故障重连。
                    await ReconnectOrThrow(ex);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException)
                {
                    // 直播间下播（"当前未在直播"）是终结态而非可恢复故障：正常结束，走合成保存
                    if (ex is InvalidOperationException && ex.Message.Contains("当前未在直播"))
                        break;
                    // 连接中断：B 站直播流地址带时效参数，到期断开是常态而非故障。先确认
                    // 直播间仍在直播——在播则立即续录（不等退避，避免每次 URL 到期丢失
                    // 3 秒内容）。立即续录同样计入重试预算（预算在"收到数据"的分段后被
                    // 重置，正常到期续录不受限）；持续失败（API 可达但流连不上、磁盘满等）
                    // 会在 ReconnectLimit 次后放弃，不构成无界热循环。重新解析失败
                    //（网络/JSON/无法取到流地址）才按常规退避重连。
                    try
                    {
                        _ = await ResolveAsync(roomId, token);
                    }
                    catch (InvalidOperationException ex2) when (ex2.Message.Contains("当前未在直播"))
                    {
                        break; // 确认下播：结束录制
                    }
                    catch (Exception ex2) when (ex2 is HttpRequestException or IOException or JsonException or InvalidOperationException)
                    {
                        await ReconnectOrThrow(ex2);
                        continue;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break; // 用户取消：保留已录分段，退出后合成保存
                    }
                    catch (OperationCanceledException ex2)
                    {
                        // 重解析超时（AppHttpClient 2 分钟超时）：按瞬态故障退避重连
                        await ReconnectOrThrow(ex2);
                        continue;
                    }
                    // 走到这里说明 ResolveAsync 成功（直播间在播）：立即续录，不等退避。
                    // 计入重试预算——预算在"收到数据"的分段后被重置，正常到期续录不受限；
                    // 持续失败（API 可达但流不可达、磁盘满等）ReconnectLimit 次后放弃。
                    reconnect++;
                    if (reconnect > ReconnectLimit)
                    {
                        Logger.LogWarn($"直播流中断且 {ReconnectLimit} 次重连失败，已录制内容保留在 {segDir}");
                        ExceptionDispatchInfo.Capture(ex).Throw();
                    }
                    Logger.LogWarn("直播流连接中断但直播间仍在直播，正在立即重新解析流地址续录...");
                    continue;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 取消发生在重连等待 Task.Delay 或 while 顶部检查（try 外）：先走到循环后的合成保存再退出
        }

        // 未收到任何字节：不生成空文件，返回 NoData 让调用方明确失败
        if (total == 0 || segmentFiles.Count == 0)
        {
            try { if (Directory.Exists(segDir)) Directory.Delete(segDir, true); } catch (IOException) { }
            return LiveRecordResult.NoData;
        }

        // 多段 → FFmpeg concat 合成最终文件；单段直接改名。
        if (segmentFiles.Count == 1)
        {
            File.Move(segmentFiles[0], path, true);
        }
        else
        {
            // 用户取消录制（Ctrl+C）时 token 已取消，但已录分段仍需合成保存——用
            // 已取消的令牌调 ConcatSegmentsAsync 会让 FFmpeg 进程立即被取消，留下
            // 半截产物。这里用独立的合并令牌：录制停止后给合成阶段一个有限的
            // 收尾窗口（MuxerTimeoutMinutes 以内），超时仍失败则保留分段供重录。
            using var finalizeCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            string tempOutPath = path + $".concat-{Guid.NewGuid():N}.tmp.flv";
            bool concatOk = false;
            try
            {
                concatOk = await ConcatSegmentsAsync(segmentFiles, tempOutPath, finalizeCts.Token);
                if (concatOk && File.Exists(tempOutPath))
                {
                    File.Move(tempOutPath, path, true);
                }
            }
            finally
            {
                try { if (File.Exists(tempOutPath)) File.Delete(tempOutPath); } catch (IOException) { }
            }

            if (!concatOk)
            {
                // 合并失败：保留分段目录——那是用户可恢复的资产，且不破坏可能已存在的旧输出文件。
                Logger.LogWarn($"直播分段合成失败，已保留分段在 {segDir}");
                return LiveRecordResult.ConcatFailedWithSegmentsSaved;
            }
        }
        // 合成成功：清理本次会话的分段目录（仅本会话，不动其它会话/旧会话）
        try { if (Directory.Exists(segDir)) Directory.Delete(segDir, true); } catch (IOException) { }
        return LiveRecordResult.Success;
    }

    /// <summary>
    /// 扫描分段根目录下的旧会话子目录并提示保留位置，但**不删除任何非空会话**。
    /// 旧实现启动时递归删除整个 .segs 目录，把上次合并失败保留的可恢复分段丢掉了。
    /// internal 供测试验证"非空会话不被删除"。
    /// </summary>
    internal static void ReportStaleSessions(string segRoot)
    {
        if (!Directory.Exists(segRoot)) return;
        foreach (var stale in Directory.GetDirectories(segRoot))
        {
            // 非空旧会话：提示用户保留位置，供手动恢复/重合并
            if (Directory.EnumerateFiles(stale).Any())
                Logger.LogWarn($"发现上次直播录制未完成的分段，保留在: {stale}（可用 ffmpeg 手动 concat 恢复）");
        }
    }

    /// <summary>用 FFmpeg concat demuxer 把多个 FLV 分段合成一个文件。返回是否成功。
    /// internal 供测试注入假执行器捕获参数。</summary>
    internal static async Task<bool> ConcatSegmentsAsync(List<string> segmentFiles, string outPath, CancellationToken token)
    {
        // concat demuxer 需要逐行列出文件，FFmpeg 通过 ArgumentList 传参无法直接传换行
        // 列表——这里写一个临时 concat 列表文件。路径含特殊字符时 concat 列表需要转义，
        // 用 file '...' 单引号包裹（路径中单引号已由 SanitizeFileName 在文件生成阶段处理）。
        // 列表文件与输出都用绝对路径：自定义 --output 目录时若 CWD 与目标目录不同，
        // 相对路径的 file '...' 条目会在 concat demuxer 读取时解析失败。
        var listPath = Path.GetFullPath(outPath) + ".concat.txt";
        var absoluteOutPath = Path.GetFullPath(outPath);
        try
        {
            long totalInputBytes = 0;
            foreach (var seg in segmentFiles)
            {
                if (!File.Exists(seg))
                {
                    Logger.LogWarn($"直播分段不存在: {seg}");
                    return false;
                }
                totalInputBytes += new FileInfo(seg).Length;
            }

            if (totalInputBytes == 0) return false;

            await File.WriteAllLinesAsync(listPath, segmentFiles.Select(f => $"file '{f.Replace("'", "'\\''")}'"), token);
            var args = new List<string>
            {
                "-loglevel", "warning", "-y",
                "-f", "concat", "-safe", "0",
                "-i", listPath,
                "-c", "copy",
                absoluteOutPath,
            };
            // 复用统一外部进程执行器（与混流一致）：支持超时/取消时 Kill 整棵进程树。
            // 用 BBDownMuxer.FFMPEG：FindBinaries 会把用户 --ffmpeg-path 或 PATH 探测的
            // 路径写入该静态字段，此前硬编码 "ffmpeg" 会绕过用户的显式指定。
            var spec = new ExternalProcessSpec
            {
                FileName = BBDownMuxer.FFMPEG,
                Arguments = args,
                TimeoutMs = Core.Config.Current.MuxerTimeoutMinutes * 60_000,
                ToolDisplayName = "ffmpeg",
            };
            int code = await BBDownMuxer.ProcessRunner.RunAsync(spec, token);
            if (code != 0 || !File.Exists(absoluteOutPath))
                return false;

            long outLen = new FileInfo(absoluteOutPath).Length;
            if (outLen == 0) return false;

            // FLV concat copy 时，产物大小应与所有分段总和相当（FLV header 占 9~13 字节，多段合并时略有减少）。
            // 若某个分段遇到坏段/HTML导致 demux 提前终止，ffmpeg exit=0 但输出大小会显著小于全部输入总大小（如只生成了坏段之前的几KB）。
            // 当分段数大于1时，输出大小若低于输入总大小的 80% 且差异超过 64KB，判定为截断坏产物。
            if (segmentFiles.Count > 1)
            {
                long minExpected = (long)(totalInputBytes * 0.8);
                if (outLen < minExpected && (totalInputBytes - outLen) > 64 * 1024)
                {
                    Logger.LogWarn($"直播分段合成产物大小异常(输出: {outLen} 字节, 预期总输入: {totalInputBytes} 字节)，判定为合成截断失败");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Logger.LogDebug("直播分段合成失败: {0}", ex.Message);
            return false;
        }
        finally
        {
            try { if (File.Exists(listPath)) File.Delete(listPath); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 把一条直播流写到独立分段文件，返回 (本段写入的字节数, 是否以读中断结束)。
    /// <paramref name="progressBase"/> 为已录完分段的累计字节数：进度回调上报
    /// progressBase + 当前分段写入量，避免重连后进度从零跳回（此前每段从 0 起报，
    /// 用户看到的进度会在重连时倒退）。
    /// </summary>
    private static async Task<(long Written, bool ReadInterrupted)> StreamToFileAsync(string url, string segPath, long progressBase, Action<long>? onProgress, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", HTTPUtil.GetUserAgent(null));
        req.Headers.TryAddWithoutValidation("Referer", "https://live.bilibili.com/");
        // 直播流是无限连接：用专用的无超时客户端（StreamingHttpClient），而非全局
        // AppHttpClient（Timeout=2min）。实测 HttpClient.Timeout 对 ResponseHeadersRead
        // 之后的流读取不生效，但无限流主体不应携带任何客户端超时，见 HTTPUtil.StreamingHttpClient。
        // 响应头阶段单独给有限超时：TCP+TLS 已建立但服务器永不返回响应头（黑洞）时
        // SendAsync 会永久挂起。headerCts 只覆盖"发请求→收响应头"，取到响应头后立即释放
        // （Dispose 幂等），不影响下方主体流读取（读循环用 token）。
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        headerCts.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = (await HTTPUtil.StreamingHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headerCts.Token)).EnsureSuccessStatusCode();
        headerCts.Dispose(); // 响应头已到达：释放仅覆盖头部阶段的超时
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        await using var fs = new FileStream(segPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        long written = 0;
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException)
                {
                    // 网络读中断（URL 到期 RST/瞬断）：已写字节照常返回。调用方据此把
                    // 本段计入 total 并重置重连预算——否则连接以异常结束时已写字节丢失，
                    // 预算持续累积，连续几次 URL 到期就会误终止整场录制（URL 到期是
                    // 常态而非故障）。写盘失败（IOException 来自 fs.WriteAsync）不在此
                    // 分支，会以零进展累计预算并最终有界终止。
                    return (written, ReadInterrupted: true);
                }
                if (read == 0) break; // 流结束（主播下播/正常 EOF）
                await fs.WriteAsync(buffer.AsMemory(0, read), token);
                written += read;
                onProgress?.Invoke(progressBase + written);
            }
            return (written, ReadInterrupted: false);
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

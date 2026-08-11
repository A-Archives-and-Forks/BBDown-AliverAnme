using System;
using System.Buffers;
using BBDown.Core.Util;
using BBDown.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;

namespace BBDown;

internal static class BBDownDownloadUtil
{
    public class DownloadConfig
    {
        public bool UseAria2c { get; set; } = false;
        public string Aria2cArgs { get; set; } = string.Empty;
        public bool ForceHttp { get; set; } = false;
        public bool MultiThread { get; set; } = false;
        public DownloadTask? RelatedTask { get; set; } = null;
    }

    private static async Task<long> RangeDownloadToTmpAsync(int id, string url, string tmpName, long fromPosition, long? toPosition, Action<int, long, long> onProgress, bool failOnRangeNotSupported = false, CancellationToken token = default)
    {
        using var fileStream = new FileStream(tmpName, FileMode.OpenOrCreate);
        long clipLength = toPosition is > 0 ? toPosition.Value - fromPosition + 1 : long.MaxValue;

        // 超长旧分片：上次中断可能留下超出目标分片范围的尾部（内容异常变大）。
        // 此时既有字节不可信——它可能是另一版本/另一资源在相同偏移留下的残留。
        // 此前把尾部截断后仅凭长度判完成，若远端内容已变化但长度恰好相同，
        // 会拼出"长度正确但内容损坏"的文件，且下次重试仍看到同一超长分片。
        // 正确做法是清空并完整重下本分片，不信任截断后"恰好吻合"的长度。
        if (fileStream.Length > clipLength)
        {
            fileStream.SetLength(0);
            fileStream.Seek(0, SeekOrigin.Begin);
        }
        else
        {
            fileStream.Seek(0, SeekOrigin.End);
        }

        if (toPosition > 0 && fileStream.Length == clipLength)
        {
            // 已下载完成 直接汇报进度并跳过下载
            onProgress(id, clipLength, clipLength);
            return fileStream.Length;
        }
        var downloadedBytes = fromPosition + fileStream.Position;

        using var httpRequestMessage = new HttpRequestMessage();
        if (!url.Contains("platform=android_tv_yst") && !url.Contains("platform=android"))
            httpRequestMessage.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
        httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        httpRequestMessage.Headers.TryAddWithoutValidation("Cookie", Core.Config.Current.Cookie);
        // 只发 Range：续传正确性由 Range: bytes=N- 保证，服务器支持则回 206、不支持则回 200
        // （下方 200 分支已做降级处理）。不发送 If-Range——此前用本地临时文件的
        // LastWriteTimeUtc 当 If-Range，它不是服务器的 Last-Modified，符合协议的服务器
        // 会因不匹配返回完整 200，导致续传被误判为"服务器不支持多线程"。
        httpRequestMessage.Headers.Range = new(downloadedBytes, toPosition);
        httpRequestMessage.RequestUri = new(url);

        using var response = (await HTTPUtil.AppHttpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, token)).EnsureSuccessStatusCode();

        if (response.StatusCode == HttpStatusCode.OK) // server doesn't response a partial content
        {
            if (failOnRangeNotSupported && (downloadedBytes > 0 || toPosition != null)) throw new NotSupportedException("Range request is not supported.");
            downloadedBytes = 0;
            // 完整重下必须清空旧内容：只 Seek(0) 而没 SetLength(0) 会留下旧文件尾部，
            // 与新的短内容拼接成损坏文件。
            fileStream.SetLength(0);
            fileStream.Seek(0, SeekOrigin.Begin);
        }
        else if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            // 严格校验 Content-Range 的起始偏移：服务器必须以我们请求的字节偏移响应。
            // 若返回的起始字节与请求不符（远端资源变化导致偏移语义错位、或 CDN 行为异常），
            // 或 206 响应缺失 Content-Range（协议异常），**当前响应体对应的区间不可信**——
            // 它可能是错误偏移的字节，也可能与本地既有前缀不连续。
            // 关键：不能清空本地文件后继续把这段错误区间的字节写到偏移 0（旧实现正是如此，
            // 把 Content-Range: bytes 50-999 的内容写到本地偏移 0，随后因长度恰好匹配而被
            // 当成完整下载）。必须丢弃本地内容并立即抛可重试的 IOException，
            // 让上层重试从正确起点（偏移 0）重新发起完整请求。
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange is not { HasRange: true } || contentRange.From != downloadedBytes)
            {
                // 直接抛错，由 using 释放连接；不读取响应体——若服务器忽略 Range
                // 返回整文件 206，读入缓冲会浪费巨量内存。
                fileStream.SetLength(0);
                fileStream.Seek(0, SeekOrigin.Begin);
                throw new IOException(
                    $"Content-Range 起始偏移({contentRange?.From?.ToString() ?? "none"})与请求偏移({downloadedBytes})不符，" +
                    "远端内容已变化或服务器行为异常，已丢弃本地内容并重新下载");
            }
        }

        using var stream = await response.Content.ReadAsStreamAsync(token);
        long? declaredLength = response.Content.Headers.ContentLength;
        // 服务器声明了 Content-Length 时按声明校验完整性；未声明时读到 EOF 即为结束
        var totalBytes = downloadedBytes + (declaredLength ?? long.MaxValue - downloadedBytes);
        long writeStartPosition = fileStream.Position;

        const int blockSize = 1048576 / 4;
        // 256KB 超过 85000 字节的大对象堆阈值，直接 new 会让每个分片、每次重试
        // 都在 LOH 上留下一块并触发 Gen2 回收（Gen2 会暂停全部线程）。
        // Rent 返回的数组可能大于请求值，因此读写都必须显式限定长度。
        var buffer = ArrayPool<byte>.Shared.Rent(blockSize);
        try
        {
            while (downloadedBytes < totalBytes)
            {
                var recevied = await stream.ReadAsync(buffer.AsMemory(0, blockSize), token);
                if (recevied == 0)
                {
                    // 提前 EOF：仅当服务器声明了 Content-Length 且未读够时是截断——
                    // 说明连接中断，必须抛错触发重试，不能把截断当成功。
                    // 未声明 Content-Length 时读到 EOF 是正常结束，静默跳出。
                    if (declaredLength is not null)
                        throw new IOException("下载中断：响应提前结束，已触发重试");
                    break;
                }
                // 依赖 FileStream 自身缓冲，不逐块 FlushAsync：每 256KB 一次刷盘会
                // 把异步写放大成同步 syscall，大文件下载产生海量无必要的刷新调用。
                // 分片结束时统一 flush 一次，保证数据落盘后调用方（合并）可读到完整内容。
                await fileStream.WriteAsync(buffer.AsMemory(0, recevied), token);
                downloadedBytes += recevied;
                onProgress(id, downloadedBytes - fromPosition, totalBytes);
            }
            // 分片写完后落盘：合并/删除等后续操作依赖磁盘上已有完整数据
            await fileStream.FlushAsync(token);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (declaredLength != null)
        {
            long written = fileStream.Position - writeStartPosition;
            if (written != declaredLength.Value)
                throw new InvalidOperationException("写入大小与HTTP响应声明不符，触发重试");
        }
        return fileStream.Length;
    }

    private sealed class PathLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Waiters;
    }

    private static readonly Dictionary<string, PathLock> _downloadLocks = new();
    private static readonly object _lockFactory = new();

    /// <summary>当前登记的路径锁数量。空闲时应为 0，用于验证锁不会累积。</summary>
    internal static int ActivePathLockCount
    {
        get { lock (_lockFactory) { return _downloadLocks.Count; } }
    }

    /// <summary>供测试使用：以路径锁执行一段逻辑。</summary>
    internal static Task RunWithPathLockAsync(string path, Func<Task> action, CancellationToken token = default)
        => WithPathLockAsync(path, action, token);

    /// <summary>
    /// 带返回值的路径锁版本：以目标路径的独占锁执行 <paramref name="action"/> 并返回其结果。
    /// 用于"判定目标文件是否已存在 → 生产（如混流写最终路径）→ 清理"这类必须原子化的临界区：
    /// 若不持有锁，serve 下两个同标题任务可能同时通过判定、同时写同一个最终路径，后写者覆盖先写者。
    /// </summary>
    internal static Task<T> RunWithPathLockAsync<T>(string path, Func<Task<T>> action, CancellationToken token = default)
        => WithPathLockAsync(path, action, token);

    /// <summary>
    /// 取得某个目标路径的独占锁并登记一个使用者。
    /// 必须与 <see cref="UnregisterDownloadLock"/> 成对使用，否则字典会持续膨胀 ——
    /// serve 模式是长驻进程，每个下载过的路径都留下一个 SemaphoreSlim 就是内存泄漏。
    /// </summary>
    /// <summary>
    /// 规范化为锁键：Path.GetFullPath 展开相对路径/“..”段，Windows 下统一小写
    /// （NTFS 不区分大小写，大小写不同的同一路径应共享一把锁）。
    /// 规范化必须同时用于 Acquire 与 Unregister，否则字典键不匹配导致锁泄漏。
    /// </summary>
    private static string NormalizeLockKey(string path)
    {
        // 空/空白路径在 GetFullPath 下会抛异常；用固定占位键避免锁机制自身抛错
        if (string.IsNullOrWhiteSpace(path)) return "<empty>";
        string full = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }

    private static PathLock AcquireDownloadLock(string path)
    {
        var key = NormalizeLockKey(path);
        lock (_lockFactory)
        {
            if (!_downloadLocks.TryGetValue(key, out var pathLock))
            {
                pathLock = new PathLock();
                _downloadLocks[key] = pathLock;
            }
            pathLock.Waiters++;
            return pathLock;
        }
    }

    private static void UnregisterDownloadLock(string path, PathLock pathLock)
    {
        var key = NormalizeLockKey(path);
        lock (_lockFactory)
        {
            // 仅在没有其他使用者时移除，避免正在等待的线程拿到已被弃用的信号量
            if (--pathLock.Waiters == 0 && _downloadLocks.TryGetValue(key, out var current) && ReferenceEquals(current, pathLock))
            {
                _downloadLocks.Remove(key);
                pathLock.Semaphore.Dispose();
            }
        }
    }

    /// <summary>
    /// 以目标路径的独占锁执行 <paramref name="action"/>。
    /// 单独记录 acquired 状态：等待被取消时既不能漏减引用计数，
    /// 也不能对一个从未获取到的信号量调用 Release。
    /// </summary>
    private static async Task WithPathLockAsync(string path, Func<Task> action, CancellationToken token)
    {
        var pathLock = AcquireDownloadLock(path);
        var acquired = false;
        try
        {
            await pathLock.Semaphore.WaitAsync(token);
            acquired = true;
            await action();
        }
        finally
        {
            if (acquired) pathLock.Semaphore.Release();
            UnregisterDownloadLock(path, pathLock);
        }
    }

    private static async Task<T> WithPathLockAsync<T>(string path, Func<Task<T>> action, CancellationToken token)
    {
        var pathLock = AcquireDownloadLock(path);
        var acquired = false;
        try
        {
            await pathLock.Semaphore.WaitAsync(token);
            acquired = true;
            return await action();
        }
        finally
        {
            if (acquired) pathLock.Semaphore.Release();
            UnregisterDownloadLock(path, pathLock);
        }
    }

    public static async Task DownloadFileAsync(string url, string path, DownloadConfig config, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(url)) return;
        await WithPathLockAsync(path, () => DownloadFileCoreAsync(url, path, config, token), token);
    }

    /// <summary>
    /// 单线程下载的实际逻辑。不获取 <see cref="AcquireDownloadLock"/>，
    /// 以便多线程模式降级时可以在已持锁的状态下复用（SemaphoreSlim 不可重入）。
    /// </summary>
    private static async Task DownloadFileCoreAsync(string url, string path, DownloadConfig config, CancellationToken token = default)
    {
        if (config.ForceHttp) url = ReplaceUrl(url);
        Logger.LogDebug("Start downloading: {0}", url);
        string desDir = Path.GetDirectoryName(path)!;
        if (!string.IsNullOrEmpty(desDir) && !Directory.Exists(desDir)) Directory.CreateDirectory(desDir);
        if (config.UseAria2c)
        {
            await BBDownAria2c.DownloadFileByAria2cAsync(url, path, config.Aria2cArgs, token);
            if (File.Exists(path + ".aria2") || !File.Exists(path))
                throw new InvalidOperationException("aria2下载可能存在错误");
            Console.WriteLine();
            return;
        }
        int retry = 0;
        // 临时文件保留目标扩展名（path + ".tmp"）：视频 xxx.mp4 与音频 xxx.m4a 路径只差
        // 扩展名，此前用 GetFileNameWithoutExtension 会让两者共用同一 .tmp——视频中断
        // 留下的 1MB 视频数据会被下次音频下载当成音频前缀续传（长度正确但内容损坏）。
        // 保留扩展名即隔离音视频的临时文件，且与多线程分片（.vclip/.aclip）的隔离一致。
        string tmpName = path + ".tmp";
        var (fileSize, probeHeaders, probeContentHeaders) = await GetFileSizeAndHeadersAsync(url, token);
        // 必须要求 fileSize > 0：服务器未返回 Content-Length 时 fileSize 为 0，
        // 此时若 path 恰好是上次失败留下的空文件，会被误判成"已下载完成"
        if (fileSize > 0 && File.Exists(path) && new FileInfo(path).Length == fileSize)
        {
            Logger.LogDebug("文件已下载过, 跳过下载");
            return;
        }
        if (fileSize > 0 && File.Exists(tmpName) && new FileInfo(tmpName).Length == fileSize)
        {
            // 长度相等不等于内容可信：同一输出路径可能被 1080P→720P / AVC→HEVC 的
            // 另一个资源复用（长度恰好相同）。只有续传清单确认资源身份一致时才直接采用，
            // 否则删除完整重下——杜绝"长度正确但内容损坏"的假成功。
            if (CanResumeFrom(tmpName, url, fileSize, out var resumeReason))
            {
                Logger.LogDebug("断点续传: 检测到已完整下载的临时文件且资源身份一致, 直接移动");
                File.Move(tmpName, path, true);
                DeleteResumeManifest(tmpName);
                return;
            }
            Logger.LogDebug("断点续传: 临时文件长度与远端一致但资源身份不可信（{0}），删除后完整重下", resumeReason ?? "未知原因");
            File.Delete(tmpName);
            DeleteResumeManifest(tmpName);
        }
        // 部分下载的临时文件直接续传：RangeDownloadToTmpAsync 会从现有长度处
        // 发起 Range: bytes=N- 续传。此前这里删除不完整临时文件导致大文件/弱网
        // 每次中断都从头重下，实际无法断点续传。
        if (File.Exists(tmpName) && new FileInfo(tmpName).Length > 0)
        {
            // 续传同样要求资源身份一致：清单缺失/不符时删除 .tmp 完整重下，
            // 否则旧前缀可能与新响应拼接（长度仍正确但内容损坏）。
            if (CanResumeFrom(tmpName, url, fileSize, out var reason))
            {
                Logger.LogDebug("断点续传: 从现有临时文件 {0} 字节处继续（资源身份一致）", new FileInfo(tmpName).Length);
            }
            else
            {
                Logger.LogDebug("断点续传: 现有临时文件资源身份不可信（{0}），删除后完整重下", reason ?? "未知原因");
                File.Delete(tmpName);
                DeleteResumeManifest(tmpName);
            }
        }
        // 临时文件比远端更大：既非"完整匹配"也非"可续传的前缀"（续传只会追加，
        // 不可能让已有内容变短）。它只可能是远端资源变化（同长度语义错位）或上次
        // 中断写入的越界尾部。若继续从现有长度处发 Range: bytes=N-，服务器要么 416、
        // 要么返回偏移错位的片段，拼出损坏文件。这里删除让单线程下载完整重下。
        if (fileSize > 0 && File.Exists(tmpName) && new FileInfo(tmpName).Length > fileSize)
        {
            Logger.LogDebug("断点续传: 临时文件({0} 字节)大于远端文件({1} 字节)，内容不可信，删除后完整重下",
                new FileInfo(tmpName).Length, fileSize);
            File.Delete(tmpName);
            DeleteResumeManifest(tmpName);
        }
        int maxRetry = Config.Current.MaxRetryCount;
        while (retry < maxRetry)
        {
            try
            {
                using var progress = new ProgressBar(config.RelatedTask);
                long written = await RangeDownloadToTmpAsync(0, url, tmpName, 0, null, (_, downloaded, total) => progress.Report((double)downloaded / total, downloaded), token: token);
                // 下载完成后写入/更新续传清单：记录本次资源的身份（URL + 总长 + 服务器头），
                // 供下次运行的断点续传校验。放在移动最终路径前——若本次中断，.tmp 与清单
                // 都保留，下次续传能用清单确认身份。
                WriteResumeManifest(tmpName, url, fileSize, probeHeaders, probeContentHeaders);
                // 移动最终路径前验证总长度：探测到的远端大小 > 0 时，临时文件必须与之一致。
                // 若响应 Content-Range 错位被上方拒绝重试后仍拿到错误内容，长度校验能拦住
                // 假成功——此前直接 File.Move 把未校验内容当作成品。
                if (fileSize > 0 && written != fileSize)
                    throw new IOException($"下载产物长度({written})与服务器声明({fileSize})不符，触发重试");
                File.Move(tmpName, path, true);
                DeleteResumeManifest(tmpName);
                break;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                throw; // non-retryable: bad input, unsupported feature, logic error
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                // 退避基数用 retry + 1：否则首次重试的等待时间为 0，
                // 面对限流的服务器会立刻再打一次
                int backoffMs = (retry + 1) * Config.Current.RetryDelayMs;
                Logger.LogDebug("下载失败(第{0}次重试, {1}ms后): {2}", retry + 1, backoffMs, ex.Message);
                await Task.Delay(backoffMs, token);
                if (++retry == maxRetry) throw;
            }
        }
    }

    /// <summary>
    /// 多线程下载。返回本次实际产生的分片文件列表（按 index 升序，与
    /// <see cref="GetAllClips"/> 的切片一一对应）。调用方应只合并/清理该列表：
    /// 扫描目录里全部 *.?clip 会把上一次取消、其它分P、其它轨道留下的分片混进来，
    /// 造成拼串味文件与误删。
    /// </summary>
    public static async Task<List<string>> MultiThreadDownloadFileAsync(string url, string path, DownloadConfig config, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(url)) return [];
        List<string>? clips = null;
        await WithPathLockAsync(path, async () => clips = await MultiThreadDownloadCoreAsync(url, path, config, token), token);
        return clips ?? [];
    }

    /// <summary>
    /// 多线程下载 + 合并 + 清理，在目标路径的独占锁内完成整个分片生命周期。
    /// 调用方（Display）不再在锁外合并/删除分片：相同目标路径的第二个任务会等第一个
    /// 任务"下载→合并→清理"全部结束后再进入，要么看到文件已完整而跳过，要么正常下载，
    /// 不会复用/误删第一个任务的分片。
    /// 合并结果先写到临时文件再原子替换到目标路径：中途失败不会留下半截成品。
    /// </summary>
    public static async Task MultiThreadDownloadAndMergeAsync(string url, string path, DownloadConfig config, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(url)) return;
        await WithPathLockAsync(path, async () =>
        {
            // 已完整下载过：直接跳过（不再产生分片，也不做任何合并/清理）
            long fileSize = (await GetFileSizeAndHeadersAsync(url, token)).size;
            if (fileSize > 0 && File.Exists(path) && new FileInfo(path).Length == fileSize)
            {
                Logger.LogDebug("文件已下载过, 跳过下载");
                return;
            }
            var clips = await MultiThreadDownloadCoreAsync(url, path, config, token);
            if (clips.Count == 0) return; // 单线程降级或 aria2 路径：成品已直接写到目标路径
            // 在锁内合并：合并到临时文件后原子替换，避免锁内写目标路径时被读取方读到半截
            string tmpMerged = path + ".merging";
            BBDownUtil.CombineMultipleFilesIntoSingleFile(clips.ToArray(), tmpMerged);
            // 完整性闭环：合并产物必须与服务器声明的总长度一致，否则删除半截成品并抛错，
            // 触发上层重试。合并时若任一来源分片不完整/缺失，产物长度会小于预期。
            if (fileSize > 0)
            {
                long mergedLength = File.Exists(tmpMerged) ? new FileInfo(tmpMerged).Length : 0;
                if (mergedLength != fileSize)
                {
                    try { File.Delete(tmpMerged); } catch (IOException) { }
                    throw new InvalidOperationException(
                        $"分片合并产物长度 ({mergedLength} 字节) 与服务器声明总长 ({fileSize} 字节) 不符，已触发重试");
                }
            }
            File.Move(tmpMerged, path, true);
            // 清理分片
            foreach (var clip in clips)
            {
                try { File.Delete(clip); }
                catch (IOException) { /* 清理失败不影响主流程 */ }
            }
        }, token);
    }

    /// <summary>本次多线程下载实际产生的分片文件列表。无分片（单线程降级/已存在跳过）时返回空列表。</summary>
    private static async Task<List<string>> MultiThreadDownloadCoreAsync(string url, string path, DownloadConfig config, CancellationToken token)
    {
        if (config.ForceHttp) url = ReplaceUrl(url);
        Logger.LogDebug("Start downloading: {0}", url);
        if (config.UseAria2c)
        {
            await BBDownAria2c.DownloadFileByAria2cAsync(url, path, config.Aria2cArgs, token);
            if (File.Exists(path + ".aria2") || !File.Exists(path))
                throw new InvalidOperationException("aria2下载可能存在错误");
            Console.WriteLine();
            return [];
        }
        long fileSize = (await GetFileSizeAndHeadersAsync(url, token)).size;
        Logger.LogDebug("文件大小：{0} bytes", fileSize);
        // 分片必须依赖已知的文件大小：拿不到 Content-Length 时 GetAllClips 会返回空列表，
        // 于是既不下载也不报错，最终在混流阶段才以"找不到文件"的形式暴露出来。
        // 单线程读到 EOF 为止，不依赖文件大小，因此降级而非失败。
        if (fileSize <= 0)
        {
            Logger.LogWarn("服务器未返回文件大小, 已降级为单线程下载");
            await DownloadFileCoreAsync(url, path, config, token);
            return [];
        }
        //已下载过, 跳过下载
        if (File.Exists(path) && new FileInfo(path).Length == fileSize)
        {
            Logger.LogDebug("文件已下载过, 跳过下载");
            // 目标文件已完整：清理上一次中断遗留的该路径分片。否则调用方（Display）
            // 在下载返回后仍会无条件重合并目录里的 .vclip，用残缺分片截断覆盖这份完整成品。
            CleanStaleClipsFor(path);
            return [];
        }
        List<Clip> allClips = GetAllClips(url, fileSize);
        int total = allClips.Count;
        Logger.LogDebug("分段数量：{0}", total);
        // 分片进度按下标存放并维护一个原子累计值。
        // 此前每次回调都要对 ConcurrentDictionary.Values 求两次和，
        // 而 Values 每次访问都会复制出一份快照 —— 回调频率是每分片每 256KB 一次，
        // 10GB 的下载会触发约 4 万次 O(分片数) 的遍历。
        var clipProgress = new long[total];
        long downloadedTotal = 0;

        using var progress = new ProgressBar(config.RelatedTask);
        progress.Report(0);
        int maxRetry = Config.Current.MaxRetryCount;
        // 显式限制单文件分片并发：不设上限时 Parallel.ForEachAsync 用 CPU 核数，
        // 高核数机器 × serve 并发任务会把出站连接数放大到远超需要的量级。
        // 每文件封顶 8 路并发分片，下载带宽通常先于并发数饱和，足够。
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Math.Min(8, Math.Max(1, Environment.ProcessorCount)),
        };
        await Parallel.ForEachAsync(allClips, parallelOptions, async (clip, _) =>
        {
            int retry = 0;
            string tmp = Path.Combine(Path.GetDirectoryName(path)!, clip.index.ToString("00000") + "_" + Path.GetFileNameWithoutExtension(path) + (Path.GetExtension(path).EndsWith(".mp4") ? ".vclip" : ".aclip"));
            while (retry < maxRetry)
            {
                try
                {
                    await RangeDownloadToTmpAsync(clip.index, url, tmp, clip.from, clip.to == -1 ? null : clip.to, (index, downloaded, _) =>
                    {
                        // 同一分片的回调只在它自己的任务里串行发生，
                        // 因此这里只需保证跨分片累加的原子性
                        var previous = Interlocked.Exchange(ref clipProgress[index], downloaded);
                        var current = Interlocked.Add(ref downloadedTotal, downloaded - previous);
                        progress.Report(fileSize > 0 ? (double)current / fileSize : 0, current);
                    }, true, _);
                    break;
                }
                catch (NotSupportedException)
                {
                    // 次数与其他分支统一用 maxRetry，原先硬编码的 3 会随配置变化而偏多或偏少
                    if (++retry == maxRetry) throw new NotSupportedException("服务器可能并不支持多线程下载, 请使用 --multi-thread false 关闭多线程");
                    await Task.Delay(retry * Config.Current.RetryDelayMs, _);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    throw; // non-retryable
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                {
                    int backoffMs = (retry + 1) * Config.Current.RetryDelayMs;
                    Logger.LogDebug("分段下载失败(第{0}次重试, {1}ms后): {2}", retry + 1, backoffMs, ex.Message);
                    await Task.Delay(backoffMs, _);
                    if (++retry == maxRetry) throw new IOException($"分段 {clip.index} 下载失败，请检查网络或关闭多线程重试", ex);
                }
            }
        });
        // 返回本次产生的精确分片列表：与 allClips 的 index 一一对应。
        // 合并/清理调用方据此操作，不扫描目录（避免混入其它任务的残留分片）。
        string dir = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string clipExt = Path.GetExtension(path).EndsWith(".mp4") ? ".vclip" : ".aclip";
        return allClips
            .Select(c => Path.Combine(dir, c.index.ToString("00000") + "_" + stem + clipExt))
            .OrderBy(p => p)
            .ToList();
    }

    /// <summary>
    /// 删除某个目标路径对应的历史分片文件（上次中断遗留）。
    /// 视频与音频的 stem 相同（xxx.mp4 / xxx.m4a），必须按各自扩展名匹配
    /// （.vclip / .aclip），否则清理视频残留会把音频轨的可续传分片一起删掉。
    /// </summary>
    internal static void CleanStaleClipsFor(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        string prefix = Path.GetFileNameWithoutExtension(path);
        string clipExt = Path.GetExtension(path).EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? ".vclip" : ".aclip";
        foreach (var clip in new DirectoryInfo(dir).EnumerateFiles("*_" + prefix + clipExt))
        {
            try { clip.Delete(); }
            catch (IOException) { /* 并发占用时跳过，下次运行再清理 */ }
        }
    }

    //此函数主要是切片下载逻辑
    internal static List<Clip> GetAllClips(string url, long fileSize)
    {
        List<Clip> clips = [];
        int index = 0;
        long counter = 0;
        long perSize = Config.Current.ThreadSegmentSizeMb * 1024L * 1024;
        while (fileSize > 0)
        {
            long segmentSize = Math.Min(perSize, fileSize);
            // to 必须始终指向段末（而非末段用 -1 表示"到 EOF"）：
            // RangeDownloadToTmpAsync 的"已下载完成跳过"检查以 toPosition > 0 为前提，
            // 末段 to=-1 会被调用处映射为 null 而跳过该检查；断点续传时完整末段会发送
            // Range: bytes=<fileSize>-（起始即 EOF），服务器回 416，重试同请求直至永久失败。
            Clip c = new()
            {
                index = index,
                from = counter,
                to = counter + segmentSize - 1
            };
            clips.Add(c);
            fileSize -= segmentSize;
            counter += segmentSize;
            index++;
        }
        return clips;
    }

    private static async Task<(long size, HttpResponseHeaders? headers, HttpContentHeaders? contentHeaders)> GetFileSizeAndHeadersAsync(string url, CancellationToken token = default)
    {
        using var httpRequestMessage = new HttpRequestMessage();
        if (!url.Contains("platform=android_tv_yst") && !url.Contains("platform=android"))
            httpRequestMessage.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
        httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        httpRequestMessage.Headers.TryAddWithoutValidation("Cookie", Core.Config.Current.Cookie);
        httpRequestMessage.RequestUri = new(url);
        using var response = (await HTTPUtil.AppHttpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, token))
            .EnsureSuccessStatusCode();
        long totalSizeBytes = response.Content.Headers.ContentLength ?? 0;

        return (totalSizeBytes, response.Headers, response.Content.Headers);
    }

    /// <summary>
    /// 断点续传的资源身份清单：记录某份 .tmp 临时文件对应的远端资源身份，防止
    /// "长度相同但内容来自另一资源"被静默续传/采用（同一 aid/cid 从 1080P 切 720P、
    /// AVC 切 HEVC 时路径不变但内容不同）。仅当清单与当前请求的资源身份一致时，
    /// 已有的 .tmp 前缀才是可信的续传素材。
    /// </summary>
    internal sealed record ResumeManifest(string Url, long TotalLength, string? LastModified, string? ETag);

    private static string ResumeManifestPath(string tmpName) => tmpName + ".manifest.json";

    /// <summary>把本次下载的资源身份写入清单（.tmp.manifest.json 旁车文件）。
    /// LastModified 来自内容头（HttpContentHeaders），ETag 来自响应头。</summary>
    private static void WriteResumeManifest(string tmpName, string url, long totalLength, HttpResponseHeaders? headers, HttpContentHeaders? contentHeaders)
    {
        try
        {
            var m = new ResumeManifest(
                url,
                totalLength,
                contentHeaders?.LastModified?.ToString("R"),
                headers?.ETag?.Tag);
            File.WriteAllText(ResumeManifestPath(tmpName),
                System.Text.Json.JsonSerializer.Serialize(m, DownloadManifestJsonContext.Default.ResumeManifest));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清单写入失败只降级为日志：资源身份校验退化为"仅长度"，与旧行为一致。
            // 不阻断下载（清单是信任增强，不是必需）。
            Logger.LogDebug("写入续传清单失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 校验某份 .tmp 是否可用于续传当前资源。返回 true 表示身份一致可续传；
    /// 返回 false 表示身份不符/清单缺失/长度不符，调用方应删除 .tmp 完整重下。
    /// internal 供测试验证"等长但资源身份不同"的 .tmp 被拒绝。
    /// </summary>
    internal static bool CanResumeFrom(string tmpName, string url, long totalLength, out string? reason)
    {
        reason = null;
        try
        {
            if (!File.Exists(ResumeManifestPath(tmpName)))
            {
                reason = "缺少续传清单（无法确认 .tmp 内容属于当前资源）";
                return false;
            }
            var m = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(ResumeManifestPath(tmpName)), DownloadManifestJsonContext.Default.ResumeManifest);
            if (m is null)
            {
                reason = "续传清单为空";
                return false;
            }
            if (m.Url != url)
            {
                reason = $"续传清单资源与当前资源不一致";
                return false;
            }
            if (m.TotalLength != totalLength)
            {
                reason = $"续传清单总长({m.TotalLength})与当前探测({totalLength})不一致";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            reason = $"续传清单读取失败: {ex.Message}";
            return false;
        }
    }

    /// <summary>清理 .tmp 的续传清单（下载完成后随 .tmp 一起移除）。</summary>
    private static void DeleteResumeManifest(string tmpName)
    {
        try { if (File.Exists(ResumeManifestPath(tmpName))) File.Delete(ResumeManifestPath(tmpName)); }
        catch (IOException) { /* 清理失败不影响主流程 */ }
    }

    /// <summary>
    /// 将下载地址强制转换为HTTP
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    private static string ReplaceUrl(string url)
    {
        if (url.Contains(".mcdn.bilivideo.cn:"))
        {
            Logger.LogDebug("对[*.mcdn.bilivideo.cn:xxx]域名不做处理");
            return url;
        }

        Logger.LogDebug("将https更改为http");
        return url.Replace("https:", "http:");
    }
}

/// <summary>AOT 裁剪安全的续传清单 JSON 序列化上下文。</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(BBDownDownloadUtil.ResumeManifest))]
internal partial class DownloadManifestJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

using System;
using BBDown.Core.Util;
using BBDown.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using System.Collections.Concurrent;

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

    private static async Task RangeDownloadToTmpAsync(int id, string url, string tmpName, long fromPosition, long? toPosition, Action<int, long, long> onProgress, bool failOnRangeNotSupported = false, CancellationToken token = default)
    {
        DateTimeOffset? lastTime = File.Exists(tmpName) ? new FileInfo(tmpName).LastWriteTimeUtc : null;
        using var fileStream = new FileStream(tmpName, FileMode.OpenOrCreate);
        fileStream.Seek(0, SeekOrigin.End);
        if (toPosition > 0 && fileStream.Position == toPosition - fromPosition + 1)
        {
            // 已下载完成 直接汇报进度并跳过下载
            onProgress(id, fileStream.Position, fileStream.Position);
            return;
        }
        var downloadedBytes = fromPosition + fileStream.Position;

        using var httpRequestMessage = new HttpRequestMessage();
        if (!url.Contains("platform=android_tv_yst") && !url.Contains("platform=android"))
            httpRequestMessage.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
        httpRequestMessage.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        httpRequestMessage.Headers.TryAddWithoutValidation("Cookie", Core.Config.Current.Cookie);
        httpRequestMessage.Headers.Range = new(downloadedBytes, toPosition);
        httpRequestMessage.Headers.IfRange = lastTime != null ? new(lastTime.Value) : null;
        httpRequestMessage.RequestUri = new(url);

        using var response = (await HTTPUtil.AppHttpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, token)).EnsureSuccessStatusCode();

        if (response.StatusCode == HttpStatusCode.OK) // server doesn't response a partial content
        {
            if (failOnRangeNotSupported && (downloadedBytes > 0 || toPosition != null)) throw new NotSupportedException("Range request is not supported.");
            downloadedBytes = 0;
            fileStream.Seek(0, SeekOrigin.Begin);
        }

        using var stream = await response.Content.ReadAsStreamAsync(token);
        var totalBytes = downloadedBytes + (response.Content.Headers.ContentLength ?? long.MaxValue - downloadedBytes);
        long writeStartPosition = fileStream.Position;

        const int blockSize = 1048576 / 4;
        var buffer = new byte[blockSize];

        while (downloadedBytes < totalBytes)
        {
            var recevied = await stream.ReadAsync(buffer, token);
            if (recevied == 0) break;
            await fileStream.WriteAsync(buffer.AsMemory(0, recevied), token);
            await fileStream.FlushAsync(token);
            downloadedBytes += recevied;
            onProgress(id, downloadedBytes - fromPosition, totalBytes);
        }

        if (response.Content.Headers.ContentLength != null)
        {
            long written = fileStream.Position - writeStartPosition;
            if (written != response.Content.Headers.ContentLength.Value)
                throw new InvalidOperationException("写入大小与HTTP响应声明不符，触发重试");
        }
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
    /// 取得某个目标路径的独占锁并登记一个使用者。
    /// 必须与 <see cref="UnregisterDownloadLock"/> 成对使用，否则字典会持续膨胀 ——
    /// serve 模式是长驻进程，每个下载过的路径都留下一个 SemaphoreSlim 就是内存泄漏。
    /// </summary>
    private static PathLock AcquireDownloadLock(string path)
    {
        lock (_lockFactory)
        {
            if (!_downloadLocks.TryGetValue(path, out var pathLock))
            {
                pathLock = new PathLock();
                _downloadLocks[path] = pathLock;
            }
            pathLock.Waiters++;
            return pathLock;
        }
    }

    private static void UnregisterDownloadLock(string path, PathLock pathLock)
    {
        lock (_lockFactory)
        {
            // 仅在没有其他使用者时移除，避免正在等待的线程拿到已被弃用的信号量
            if (--pathLock.Waiters == 0 && _downloadLocks.TryGetValue(path, out var current) && ReferenceEquals(current, pathLock))
            {
                _downloadLocks.Remove(path);
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
            await BBDownAria2c.DownloadFileByAria2cAsync(url, path, config.Aria2cArgs);
            if (File.Exists(path + ".aria2") || !File.Exists(path))
                throw new InvalidOperationException("aria2下载可能存在错误");
            Console.WriteLine();
            return;
        }
        int retry = 0;
        string tmpName = Path.Combine(desDir, Path.GetFileNameWithoutExtension(path) + ".tmp");
        long fileSize = await GetFileSizeAsync(url, token);
        // 必须要求 fileSize > 0：服务器未返回 Content-Length 时 fileSize 为 0，
        // 此时若 path 恰好是上次失败留下的空文件，会被误判成"已下载完成"
        if (fileSize > 0 && File.Exists(path) && new FileInfo(path).Length == fileSize)
        {
            Logger.LogDebug("文件已下载过, 跳过下载");
            return;
        }
        if (fileSize > 0 && File.Exists(tmpName) && new FileInfo(tmpName).Length == fileSize)
        {
            Logger.LogDebug("断点续传: 检测到已完整下载的临时文件, 直接移动");
            File.Move(tmpName, path, true);
            return;
        }
        if (File.Exists(tmpName))
        {
            Logger.LogDebug("断点续传: 临时文件大小不匹配, 删除残留: {0}", tmpName);
            File.Delete(tmpName);
        }
        int maxRetry = Config.Current.MaxRetryCount;
        while (retry < maxRetry)
        {
        try
        {
            using var progress = new ProgressBar(config.RelatedTask);
            await RangeDownloadToTmpAsync(0, url, tmpName, 0, null, (_, downloaded, total) => progress.Report((double)downloaded / total, downloaded), token: token);
            File.Move(tmpName, path, true);
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

    public static async Task MultiThreadDownloadFileAsync(string url, string path, DownloadConfig config, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(url)) return;
        await WithPathLockAsync(path, () => MultiThreadDownloadCoreAsync(url, path, config, token), token);
    }

    private static async Task MultiThreadDownloadCoreAsync(string url, string path, DownloadConfig config, CancellationToken token)
    {
        if (config.ForceHttp) url = ReplaceUrl(url);
        Logger.LogDebug("Start downloading: {0}", url);
        if (config.UseAria2c)
        {
            await BBDownAria2c.DownloadFileByAria2cAsync(url, path, config.Aria2cArgs);
            if (File.Exists(path + ".aria2") || !File.Exists(path))
                throw new InvalidOperationException("aria2下载可能存在错误");
            Console.WriteLine();
            return;
        }
        long fileSize = await GetFileSizeAsync(url, token);
        Logger.LogDebug("文件大小：{0} bytes", fileSize);
        // 分片必须依赖已知的文件大小：拿不到 Content-Length 时 GetAllClips 会返回空列表，
        // 于是既不下载也不报错，最终在混流阶段才以"找不到文件"的形式暴露出来。
        // 单线程读到 EOF 为止，不依赖文件大小，因此降级而非失败。
        if (fileSize <= 0)
        {
            Logger.LogWarn("服务器未返回文件大小, 已降级为单线程下载");
            await DownloadFileCoreAsync(url, path, config, token);
            return;
        }
        //已下载过, 跳过下载
        if (File.Exists(path) && new FileInfo(path).Length == fileSize)
        {
            Logger.LogDebug("文件已下载过, 跳过下载");
            return;
        }
        List<Clip> allClips = GetAllClips(url, fileSize);
        int total = allClips.Count;
        Logger.LogDebug("分段数量：{0}", total);
        ConcurrentDictionary<int, long> clipProgress = new();
        foreach (var i in allClips) clipProgress[i.index] = 0;

        using var progress = new ProgressBar(config.RelatedTask);
        progress.Report(0);
        int maxRetry = Config.Current.MaxRetryCount;
        await Parallel.ForEachAsync(allClips, token, async (clip, _) =>
        {
            int retry = 0;
            string tmp = Path.Combine(Path.GetDirectoryName(path)!, clip.index.ToString("00000") + "_" + Path.GetFileNameWithoutExtension(path) + (Path.GetExtension(path).EndsWith(".mp4") ? ".vclip" : ".aclip"));
            while (retry < maxRetry)
            {
            try
            {
                await RangeDownloadToTmpAsync(clip.index, url, tmp, clip.from, clip.to == -1 ? null : clip.to, (index, downloaded, _) =>
                {
                    clipProgress[index] = downloaded;
                    progress.Report(fileSize > 0 ? (double)clipProgress.Values.Sum() / fileSize : 0, clipProgress.Values.Sum());
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
    }

    //此函数主要是切片下载逻辑
    private static List<Clip> GetAllClips(string url, long fileSize)
    {
        List<Clip> clips = [];
        int index = 0;
        long counter = 0;
        long perSize = Config.Current.ThreadSegmentSizeMb * 1024L * 1024;
        while (fileSize > 0)
        {
            long segmentSize = Math.Min(perSize, fileSize);
            Clip c = new()
            {
                index = index,
                from = counter,
                to = fileSize > perSize ? counter + segmentSize - 1 : -1
            };
            clips.Add(c);
            fileSize -= segmentSize;
            counter += segmentSize;
            index++;
        }
        return clips;
    }

    private static async Task<long> GetFileSizeAsync(string url, CancellationToken token = default)
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

        return totalSizeBytes;
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
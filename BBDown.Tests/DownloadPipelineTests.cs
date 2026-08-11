using System.Net;
using BBDown;
using BBDown.Core;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

/// <summary>
/// 本类通过 <see cref="BBDownDownloadUtil.ActivePathLockCount"/> 断言全局路径锁字典
/// 会被清理（0 个空闲锁），而该字典是进程级静态状态——其它并行测试类若同时登记
/// 路径锁会让计数非 0，导致断言误失败。串行化本类与既有
/// <see cref="MuxerProcessRunnerCollection"/> 同一模式，保证计数断言稳定。
/// </summary>
[Collection("PathLockCollection")]
public class DownloadPipelineTests
{
    [Fact]
    public void GetAllClips_LastSegment_HasExplicitEndInsteadOfMinusOne()
    {
        var original = Config.Current.ThreadSegmentSizeMb;
        try
        {
            Config.Apply(Config.Current with { ThreadSegmentSizeMb = 1 }); // 1MB 分片
            long per = 1024L * 1024;
            var clips = BBDownDownloadUtil.GetAllClips("http://x", per + 500); // 完整一段 + 500 字节末段
            Assert.Equal(2, clips.Count);
            // 末段不再用 -1：指向文件真实末尾，断点续传的完整性检查（toPosition > 0）才能命中，
            // 否则完整末段会发 Range: bytes=<fileSize>- 触发 416 永久失败
            Assert.Equal(per + 500 - 1, clips[^1].to);
            Assert.All(clips, c => Assert.True(c.to >= c.from));
        }
        finally
        {
            Config.Apply(Config.Current with { ThreadSegmentSizeMb = original });
        }
    }

    [Fact]
    public void CleanStaleClips_RemovesOnlyMatchingPathClips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mineA = Path.Combine(dir, "00000_video.vclip");
            var mineB = Path.Combine(dir, "00001_video.vclip");
            var other = Path.Combine(dir, "00000_other.vclip");
            var audioClip = Path.Combine(dir, "00000_video.aclip"); // 同 stem 的音频轨分片
            var unrelated = Path.Combine(dir, "notes.txt");
            File.WriteAllText(mineA, "a");
            File.WriteAllText(mineB, "b");
            File.WriteAllText(other, "c");
            File.WriteAllText(audioClip, "e");
            File.WriteAllText(unrelated, "d");

            BBDownDownloadUtil.CleanStaleClipsFor(Path.Combine(dir, "video.mp4"));

            Assert.False(File.Exists(mineA));
            Assert.False(File.Exists(mineB));
            Assert.True(File.Exists(other));      // 其他任务的 clip 保留
            Assert.True(File.Exists(audioClip));  // 音频轨分片必须保留
            Assert.True(File.Exists(unrelated));  // 非 clip 文件保留
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CleanStaleClips_Audio_DoesNotDeleteVideoClips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var videoClip = Path.Combine(dir, "00000_video.vclip");
            var audioClip = Path.Combine(dir, "00000_video.aclip");
            File.WriteAllText(videoClip, "v");
            File.WriteAllText(audioClip, "a");

            BBDownDownloadUtil.CleanStaleClipsFor(Path.Combine(dir, "video.m4a"));

            Assert.True(File.Exists(videoClip)); // 视频轨分片保留
            Assert.False(File.Exists(audioClip));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(2, 3, 2)]
    [InlineData(5, 3, 2)]   // 越界 → 钳到末位
    [InlineData(-1, 3, 0)]
    [InlineData(3, 1, 0)]
    [InlineData(0, 0, -1)]  // 无音频 → 标记跳过
    public void ClampRoleAudioIndex_HandlesOutOfRange(int aIndex, int count, int expected)
        => Assert.Equal(expected, Program.ClampRoleAudioIndex(aIndex, count));

    [Fact]
    public async Task MultiThreadDownloadAndMerge_MergesAndCleansClips_UnderLock()
    {
        // 用本地 HTTP 服务提供一段小文件，验证多线程下载在锁内完成"下载→合并→清理"：
        // 目标文件完整、分片全部清除、且路径锁已被释放（不泄漏）。
        using var server = new LocalByteServer(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "video.mp4");
        try
        {
            var config = new BBDownDownloadUtil.DownloadConfig { MultiThread = true };
            var original = Config.Current.ThreadSegmentSizeMb;
            try
            {
                Config.Apply(Config.Current with { ThreadSegmentSizeMb = 1 }); // 1MB 分片 → 1 个分片
                await BBDownDownloadUtil.MultiThreadDownloadAndMergeAsync(
                    $"http://127.0.0.1:{server.Port}/file", target, config, CancellationToken.None);
            }
            finally
            {
                Config.Apply(Config.Current with { ThreadSegmentSizeMb = original });
            }

            // 目标文件已合并且内容完整（与服务端字节一致）
            Assert.True(File.Exists(target), "目标文件应已合并生成");
            Assert.Equal(256 * 1024, new FileInfo(target).Length);
            // 分片已清理：目录里不应残留 .vclip
            Assert.Empty(Directory.GetFiles(dir, "*.vclip"));
            // 锁已释放
            Assert.Equal(0, BBDownDownloadUtil.ActivePathLockCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>本地 HTTP 服务，返回固定长度的字节流（用于多线程下载测试）。</summary>
    private sealed class LocalByteServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly byte[] _payload;
        private readonly Task _loop;
        public int Port { get; }

        public LocalByteServer(int size)
        {
            _payload = new byte[size];
            new Random(42).NextBytes(_payload);
            Port = 24000 + (Environment.ProcessId % 2000);
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        var ctx = await _listener.GetContextAsync();
                        try
                        {
                            var resp = ctx.Response;
                            // 支持 Range 请求：响应 206 分段
                            var rangeHeader = ctx.Request.Headers["Range"];
                            if (string.IsNullOrEmpty(rangeHeader))
                            {
                                resp.StatusCode = 200;
                                resp.ContentLength64 = _payload.Length;
                                await resp.OutputStream.WriteAsync(_payload, _cts.Token);
                            }
                            else
                            {
                                // 解析 "bytes=from-to"（-1 表示到末尾）
                                var range = rangeHeader.Replace("bytes=", "").Split('-');
                                var from = int.Parse(range[0]);
                                var to = range.Length > 1 && range[1] != "" ? int.Parse(range[1]) : _payload.Length - 1;
                                var count = to - from + 1;
                                resp.StatusCode = 206;
                                resp.ContentLength64 = count;
                                resp.AddHeader("Content-Range", $"bytes {from}-{to}/{_payload.Length}");
                                await resp.OutputStream.WriteAsync(_payload.AsMemory(from, count), _cts.Token);
                            }
                            resp.Close();
                        }
                        catch
                        {
                            // 客户端中止：忽略
                        }
                    }
                }
                catch (HttpListenerException)
                {
                    // 服务停止
                }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task RunWithPathLockAsync_SamePath_SerializesProducers()
    {
        // 模拟 serve 下两个同标题任务写同一个最终路径：路径锁必须把"生产最终文件"
        // 串行化，避免后写者覆盖先写者。两个生产者各自写入后再整体校验文件内容。
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "same-title.mp4");
        try
        {
            // 两个生产者并发进入，但同一路径的锁会让它们串行执行
            var tasks = new[]
            {
                BBDownDownloadUtil.RunWithPathLockAsync(target, async () =>
                {
                    await Task.Delay(30);
                    await File.WriteAllTextAsync(target, "producer-A");
                    return true;
                }),
                BBDownDownloadUtil.RunWithPathLockAsync(target, async () =>
                {
                    await Task.Delay(10);
                    await File.WriteAllTextAsync(target, "producer-B");
                    return true;
                }),
            };
            await Task.WhenAll(tasks);
            // 最后写入者的内容完整保留（没有被并发截断/交错）
            var content = await File.ReadAllTextAsync(target);
            Assert.True(content is "producer-A" or "producer-B", $"最终内容应为某个生产者完整写入，实际: {content}");
            // 锁字典应已清理：serve 长驻进程不能因路径锁累积内存
            Assert.Equal(0, BBDownDownloadUtil.ActivePathLockCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task RunWithPathLockAsync_DifferentPaths_RunConcurrently()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var tasks = new[]
            {
                BBDownDownloadUtil.RunWithPathLockAsync(Path.Combine(dir, "a.mp4"), async () =>
                {
                    await Task.Delay(100);
                    return true;
                }),
                BBDownDownloadUtil.RunWithPathLockAsync(Path.Combine(dir, "b.mp4"), async () =>
                {
                    await Task.Delay(100);
                    return true;
                }),
            };
            await Task.WhenAll(tasks);
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - start;
            // 不同路径不互斥：两任务应并行完成（显著小于 200ms 串行时间）
            Assert.True(elapsed < 180, $"不同路径应并行执行，实际耗时 {elapsed}ms");
            Assert.Equal(0, BBDownDownloadUtil.ActivePathLockCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

[CollectionDefinition("PathLockCollection", DisableParallelization = true)]
public class PathLockCollection
{
}

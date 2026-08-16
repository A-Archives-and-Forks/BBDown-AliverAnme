using System.Net;
using System.Security.Cryptography;
using BBDown;
using BBDown.Core;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

/// <summary>计算字节数组的 SHA-256 十六进制摘要，用于下载产物内容一致性断言。</summary>
internal static class TestHash
{
    public static string ComputeSha256Hex(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }
}

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
            // 内容哈希一致：仅断言长度无法发现"长度正确但内容损坏"（错误内容也能通过）。
            // 必须与服务端的真实载荷逐字节一致，断点续传/分片拼接的任何错位都会反映在哈希上。
            Assert.Equal(server.PayloadHash, TestHash.ComputeSha256Hex(await File.ReadAllBytesAsync(target)));
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

    [Fact]
    public async Task MultiThreadDownloadAndMerge_OversizedStaleClip_IsTruncatedNotMerged()
    {
        // 回归：旧分片若比目标分片更长（上次中断留下的超长尾部），不能把截断后"恰好吻合"
        // 的长度当成内容可信——远端内容可能已变化但长度相同，会拼出损坏文件。
        // 正确行为是丢弃既有内容完整重下，产物必须与服务端载荷逐字节一致（哈希校验）。
        using var server = new LocalByteServer(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "video.mp4");
        try
        {
            // 预置超长分片：stem=video、扩展名 .mp4 → 分片名 00000_video.vclip。
            // 用与服务端完全不同的随机内容（不是服务端载荷的前缀）——若实现错误地
            // 沿用旧分片尾部，哈希必然失配，直接暴露。
            var clipPath = Path.Combine(dir, "00000_video.vclip");
            var oversized = new byte[512 * 1024]; // 目标 256KB，旧分片 512KB
            new Random(1).NextBytes(oversized);
            await File.WriteAllBytesAsync(clipPath, oversized);

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

            // 产物长度等于服务器总长：超长旧分片尾部未被带入
            Assert.True(File.Exists(target), "目标文件应已合并生成");
            Assert.Equal(256 * 1024, new FileInfo(target).Length);
            // 内容哈希与服务端载荷一致：即便旧分片被截断到相同长度，也不允许把旧内容
            // 当成已下载内容（否则会拼出长度正确但内容损坏的文件）
            Assert.Equal(server.PayloadHash, TestHash.ComputeSha256Hex(await File.ReadAllBytesAsync(target)));
            // 分片已清理
            Assert.Empty(Directory.GetFiles(dir, "*.vclip"));
            Assert.Equal(0, BBDownDownloadUtil.ActivePathLockCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 回归：206 响应的 Content-Range 起始偏移与请求偏移不符时，必须丢弃本地内容并
    /// 抛可重试的 IOException，绝不能把错误区间的字节写到本地偏移 0（旧实现如此会
    /// 拼出"长度正确但内容损坏"的文件）。这里预置一个续传偏移，服务器却从错误的
    /// 起始偏移返回内容，验证下载以异常终止且不产生错误内容。
    /// </summary>
    [Fact]
    public async Task MultiThreadDownloadAndMerge_ContentRangeMismatch_ThrowsAndDoesNotProduceCorruptFile()
    {
        using var server = new MisleadingRangeServer(payloadSize: 128 * 1024, wrongOffset: 50 * 1024);
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
                // 服务器始终从错误偏移返回（即使请求从 0 开始）：下载必须失败，而非产出损坏文件
                await Assert.ThrowsAsync<IOException>(() =>
                    BBDownDownloadUtil.MultiThreadDownloadAndMergeAsync(
                        $"http://127.0.0.1:{server.Port}/file", target, config, CancellationToken.None));
            }
            finally
            {
                Config.Apply(Config.Current with { ThreadSegmentSizeMb = original });
            }

            // 失败后不得留下被当成成品的错误内容：目标文件要么不存在，要么是合法的空占位
            if (File.Exists(target))
                Assert.True(new FileInfo(target).Length == 0, "Content-Range 错位失败后不应产出错误内容文件");
            // 锁应已释放
            Assert.Equal(0, BBDownDownloadUtil.ActivePathLockCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 回归：多线程分片身份检查必须命中。预置资源 A 的旧分片（00000_video.vclip）及其
    /// manifest，再下载等长资源 B——若身份检查不命中（通配符错误/首分片缺失绕过），
    /// 旧分片会被按长度拼入新资源，最终哈希 ≠ B 的哈希。此测试用真实 LocalByteServer
    /// 走完整下载，验证产物哈希必须等于服务端 B 载荷。
    /// </summary>
    [Fact]
    public async Task MultiThreadDownloadAndMerge_StaleSegmentFromOtherResource_IsReplacedWithFreshContent()
    {
        using var server = new LocalByteServer(256 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "video.mp4");
        try
        {
            // 预置资源 A 的旧分片（00000_video.vclip，与服务端 B 等长但内容不同）及其清单
            var clip = Path.Combine(dir, "00000_video.vclip");
            var staleBytes = new byte[256 * 1024];
            new Random(3).NextBytes(staleBytes);
            await File.WriteAllBytesAsync(clip, staleBytes);
            var staleManifest = new BBDownDownloadUtil.ResumeManifest(
                BBDownDownloadUtil.StableResourceIdentity("https://cdn.example.com/resourceA.mp4"), 256 * 1024, null, null);
            await File.WriteAllTextAsync(clip + ".manifest.json",
                System.Text.Json.JsonSerializer.Serialize(staleManifest, DownloadManifestJsonContext.Default.ResumeManifest));

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

            // 产物哈希必须等于服务端 B：若旧分片被复用（身份检查不命中），哈希会失配
            Assert.True(File.Exists(target));
            Assert.Equal(server.PayloadHash, TestHash.ComputeSha256Hex(await File.ReadAllBytesAsync(target)));
            Assert.Equal(0, BBDownDownloadUtil.ActivePathLockCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 回归：视频与音频的临时文件必须隔离。旧实现用 GetFileNameWithoutExtension 生成
    /// .tmp，视频 xxx.mp4 与音频 xxx.m4a 共用 video.tmp——视频中断留下的数据会被音频
    /// 下载当成前缀续传（长度正确但内容损坏）。新实现保留扩展名（video.mp4.tmp /
    /// video.m4a.tmp）。此测试预置一个旧命名的共享 video.tmp 残留，下载音频并验证产物
    /// 与服务端字节一致：若音频误用共享残留作前缀，哈希必然失配。
    /// </summary>
    [Fact]
    public async Task SingleThreadDownload_AudioDoesNotReuseStaleSharedTempFromVideo()
    {
        using var server = new LocalByteServer(64 * 1024);
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var videoPath = Path.Combine(dir, "video.mp4");
        var audioPath = Path.Combine(dir, "video.m4a"); // 同 stem，仅扩展名不同
        try
        {
            // 预置旧命名下的共享 .tmp 残留（模拟视频中断留下的数据）
            var staleShared = Path.Combine(dir, "video.tmp");
            var staleBytes = new byte[32 * 1024]; // 音频 64KB 的一半
            new Random(9).NextBytes(staleBytes);
            await File.WriteAllBytesAsync(staleShared, staleBytes);

            // 下载音频（单线程）：新实现用 video.m4a.tmp，忽略共享 video.tmp 残留
            var config = new BBDownDownloadUtil.DownloadConfig();
            await BBDownDownloadUtil.DownloadFileAsync(
                $"http://127.0.0.1:{server.Port}/file", audioPath, config, CancellationToken.None);

            // 音频产物必须与服务端字节一致：若旧实现把视频残留当音频前缀续传，
            // 输出 = 32KB 视频随机 + 32KB 服务端尾部，哈希必然失配
            Assert.True(File.Exists(audioPath));
            Assert.Equal(server.PayloadHash, TestHash.ComputeSha256Hex(await File.ReadAllBytesAsync(audioPath)));
            Assert.Equal(0, BBDownDownloadUtil.ActivePathLockCount);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 回归：断点续传的 .tmp 必须通过资源身份清单校验。等长但 URL 不同的 .tmp
    /// （同一输出路径被另一清晰度/编码资源复用）必须被拒绝，不能仅凭长度采用。
    /// </summary>
    [Fact]
    public void CanResumeFrom_SameLengthButDifferentUrl_RejectsResume()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tmp = Path.Combine(dir, "video.mp4.tmp");
            File.WriteAllText(tmp, "fake-prefix-content-of-any-length");
            // 写入清单：URL A 与总长，但当前请求是 URL B（同长度不同资源）
            var manifest = new BBDownDownloadUtil.ResumeManifest("https://cdn.example.com/1080p.mp4", 12345, null, null);
            File.WriteAllText(tmp + ".manifest.json",
                System.Text.Json.JsonSerializer.Serialize(manifest, DownloadManifestJsonContext.Default.ResumeManifest));

            // 清单 URL 与当前请求 URL 不同 → 拒绝续传
            Assert.False(BBDownDownloadUtil.CanResumeFrom(tmp, "https://cdn.example.com/720p.mp4", 12345, out var reason));
            Assert.NotNull(reason);
            Assert.Contains("不一致", reason);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 稳定资源身份必须剥离会刷新的签名 query 参数（deadline/sign/w_rid/ts 等）：
    /// 媒体 URL 的签名每次请求刷新，若用完整 URL 相等，同一资源永远无法跨进程续传。
    /// </summary>
    [Fact]
    public void StableResourceIdentity_StripsSignatureParams_AndKeepsStableOnes()
    {
        var a = "https://upos.example.com/video.mp4?mid=1&deadline=1700000000&sign=abc&wts=1700000000&qn=80";
        var b = "https://upos.example.com/video.mp4?mid=1&deadline=1700000300&sign=def&wts=1700000300&qn=80";
        // 同一资源、刷新签名 → 稳定身份必须相同
        Assert.Equal(BBDownDownloadUtil.StableResourceIdentity(a), BBDownDownloadUtil.StableResourceIdentity(b));
        // 稳定参数（mid/qn）保留，签名参数剥离
        var stable = BBDownDownloadUtil.StableResourceIdentity(a);
        Assert.Contains("mid=1", stable);
        Assert.Contains("qn=80", stable);
        Assert.DoesNotContain("sign=", stable);
        Assert.DoesNotContain("deadline=", stable);
        // 不同资源（不同路径）→ 稳定身份不同
        Assert.NotEqual(
            BBDownDownloadUtil.StableResourceIdentity("https://upos.example.com/other.mp4?mid=1&deadline=1&sign=x"),
            stable);
    }

    /// <summary>
    /// 回归：单线程续传的 .tmp 清单在下载前就已写入（真正中断也带清单可续传）。
    /// 验证 CanResumeFrom 用稳定身份匹配——签名刷新后的同一资源仍可续传。
    /// </summary>
    [Fact]
    public void CanResumeFrom_RefreshedSignature_SameResourceStillResumable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tmp = Path.Combine(dir, "video.mp4.tmp");
            File.WriteAllText(tmp, "prefix");
            // 清单记录旧签名的 URL（Identity 用稳定身份）
            var manifest = new BBDownDownloadUtil.ResumeManifest(
                BBDownDownloadUtil.StableResourceIdentity("https://cdn.example.com/1080p.mp4?deadline=100&sign=old&qn=80"),
                12345, null, null);
            File.WriteAllText(tmp + ".manifest.json",
                System.Text.Json.JsonSerializer.Serialize(manifest, DownloadManifestJsonContext.Default.ResumeManifest));

            // 当前请求是签名刷新的同一资源 → 稳定身份一致 → 可续传
            Assert.True(BBDownDownloadUtil.CanResumeFrom(tmp,
                "https://cdn.example.com/1080p.mp4?deadline=999&sign=new&qn=80", 12345, out _));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 回归：完整 .tmp 即便身份与长度都匹配，若服务器 ETag 已变化（同路径内容变化但长度
    /// 不变），也必须拒绝——否则旧 .tmp 被直接采用，产出"长度正确但内容损坏"的文件。
    /// </summary>
    [Fact]
    public void CanResumeFrom_SameLengthAndIdentity_ButChangedETag_RejectsResume()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tmp = Path.Combine(dir, "video.mp4.tmp");
            File.WriteAllText(tmp, "complete-content");
            // 清单记录旧 ETag：同一资源同一长度，但服务器已返回新 ETag（内容已变）。
            // 清单身份用稳定身份（与 URL 剥离签名参数后的结果一致）。
            var manifest = new BBDownDownloadUtil.ResumeManifest(
                BBDownDownloadUtil.StableResourceIdentity("https://cdn.example.com/video.mp4?deadline=1&sign=old"),
                12345, null, "W/old-etag");
            File.WriteAllText(tmp + ".manifest.json",
                System.Text.Json.JsonSerializer.Serialize(manifest, DownloadManifestJsonContext.Default.ResumeManifest));

            Assert.False(BBDownDownloadUtil.CanResumeFrom(tmp, "https://cdn.example.com/video.mp4?deadline=2&sign=new", 12345, out var reason,
                currentETag: "W/new-etag"));
            Assert.Contains("ETag", reason);
            // 校验器一致时仍可续传
            Assert.True(BBDownDownloadUtil.CanResumeFrom(tmp, "https://cdn.example.com/video.mp4?deadline=2&sign=new", 12345, out _,
                currentETag: "W/old-etag"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 回归（aria2c --continue=true）：跨资源且长度恰相等的残留 partial 必须被删除重下，
    /// 不得被"已完整下载"跳过——否则残缺文件会直接作为成品进入混流。此前纯 length-only
    /// 跳过先于身份校验执行，等长跨资源残留被误跳过。
    /// </summary>
    [Fact]
    public void PrepareAria2cTarget_CrossResourceEqualLengthPartial_DeletesAndRedownloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "video.mp4");
            // 残留 partial 恰好 10 字节，与"新资源"的 fileSize 相等（等长跨资源）
            File.WriteAllText(path, "1234567890");
            var control = path + ".aria2";
            File.WriteAllText(control, "ctrl");
            // 清单记录旧资源（1080P）身份；当前请求是另一资源（720P）
            var manifest = new BBDownDownloadUtil.ResumeManifest(
                BBDownDownloadUtil.StableResourceIdentity("https://cdn.example.com/1080p.mp4?qn=80"),
                100, null, null);
            File.WriteAllText(path + ".manifest.json",
                System.Text.Json.JsonSerializer.Serialize(manifest, DownloadManifestJsonContext.Default.ResumeManifest));

            bool skip = BBDownDownloadUtil.PrepareAria2cTarget(
                "https://cdn.example.com/720p.mp4?qn=64", path, fileSize: 10, headers: null, contentHeaders: null);

            Assert.False(skip, "等长跨资源残留不得跳过 aria2c");
            Assert.False(File.Exists(path), "跨资源残留 partial 应被删除");
            Assert.False(File.Exists(control), "残留 .aria2 控制文件应被删除");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>同资源中断（清单身份匹配）：partial 保留供 --continue=true 续传，返回 false。</summary>
    [Fact]
    public void PrepareAria2cTarget_SameResourceInterruptedPartial_KeptForResume()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "video.mp4");
            File.WriteAllText(path, "partial-prefix");
            var control = path + ".aria2";
            File.WriteAllText(control, "ctrl");
            var manifest = new BBDownDownloadUtil.ResumeManifest(
                BBDownDownloadUtil.StableResourceIdentity("https://cdn.example.com/1080p.mp4?deadline=1&sign=old&qn=80"),
                1000, null, null);
            File.WriteAllText(path + ".manifest.json",
                System.Text.Json.JsonSerializer.Serialize(manifest, DownloadManifestJsonContext.Default.ResumeManifest));

            // 签名刷新后的同一资源（稳定身份一致）→ 保留续传
            bool skip = BBDownDownloadUtil.PrepareAria2cTarget(
                "https://cdn.example.com/1080p.mp4?deadline=999&sign=new&qn=80", path, fileSize: 1000, headers: null, contentHeaders: null);

            Assert.False(skip);
            Assert.True(File.Exists(path), "同资源中断的 partial 应保留续传");
            Assert.True(File.Exists(control), "同资源 .aria2 控制文件应保留");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>全新下载（无既有文件）：写入本次身份清单并返回 false（需调 aria2c）。</summary>
    [Fact]
    public void PrepareAria2cTarget_NoPartial_WritesManifestAndReturnsFalse()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "video.mp4");
            bool skip = BBDownDownloadUtil.PrepareAria2cTarget(
                "https://cdn.example.com/1080p.mp4?qn=80", path, fileSize: 1000, headers: null, contentHeaders: null);
            Assert.False(skip);
            Assert.True(File.Exists(path + ".manifest.json"), "首次下载前应写入身份清单");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>同资源完整文件：返回 true（跳过 aria2c），残留控制文件被清理、身份清单保留。</summary>
    [Fact]
    public void PrepareAria2cTarget_CompleteSameResource_SkipsAria2c()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "video.mp4");
            File.WriteAllText(path, "complete-10-byte"); // 16 字节
            var control = path + ".aria2";
            File.WriteAllText(control, "ctrl");
            var manifest = new BBDownDownloadUtil.ResumeManifest(
                BBDownDownloadUtil.StableResourceIdentity("https://cdn.example.com/1080p.mp4?qn=80"),
                16, null, null);
            File.WriteAllText(path + ".manifest.json",
                System.Text.Json.JsonSerializer.Serialize(manifest, DownloadManifestJsonContext.Default.ResumeManifest));

            bool skip = BBDownDownloadUtil.PrepareAria2cTarget(
                "https://cdn.example.com/1080p.mp4?qn=80", path, fileSize: 16, headers: null, contentHeaders: null);

            Assert.True(skip, "同资源完整文件应跳过 aria2c");
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(control), "残留 .aria2 控制文件应被清理");
            // 身份清单保留为"完成证书"，供下次重跑经 CanResumeFrom 确认身份后跳过
            Assert.True(File.Exists(path + ".manifest.json"), "完成下载后身份清单应保留");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 回归：已完整下载但缺身份清单（旧版残留/清单丢失）的文件，不得被纯长度跳过——否则
    /// 无法确认其内容属于当前资源。保守删除重下（安全但浪费，与 CanResumeFrom 缺清单一致）。
    /// </summary>
    [Fact]
    public void PrepareAria2cTarget_CompleteButNoManifest_PurgesAndRedownloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "video.mp4");
            File.WriteAllText(path, "complete-16-bytes"); // 长度与 fileSize 相等
            var control = path + ".aria2";
            File.WriteAllText(control, "ctrl");
            // 不写清单：模拟旧版下载完成/清单丢失

            bool skip = BBDownDownloadUtil.PrepareAria2cTarget(
                "https://cdn.example.com/1080p.mp4?qn=80", path, fileSize: 17, headers: null, contentHeaders: null);

            Assert.False(skip, "缺清单的完整文件不得跳过（无法确认身份）");
            Assert.False(File.Exists(path), "缺清单的既有文件应被删除重下");
            Assert.False(File.Exists(control), "残留控制文件应被删除");
            Assert.True(File.Exists(path + ".manifest.json"), "删除后应写入当前资源的新清单");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>
    /// 身份可信但超长的残留（长度超过远端总长）：内容不可信（越界尾部/资源变化），
    /// 必须删除重下——否则 aria2c --continue 从超出 EOF 的偏移续传可能 416 死循环。
    /// </summary>
    [Fact]
    public void PrepareAria2cTarget_OversizedTrustedPartial_PurgesAndRedownloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "video.mp4");
            File.WriteAllText(path, "oversized-content-longer-than-remote");
            var control = path + ".aria2";
            File.WriteAllText(control, "ctrl");
            // 身份可信（同资源稳定身份 + 总长匹配），但本地文件长度 > fileSize
            var manifest = new BBDownDownloadUtil.ResumeManifest(
                BBDownDownloadUtil.StableResourceIdentity("https://cdn.example.com/1080p.mp4?qn=80"),
                10, null, null);
            File.WriteAllText(path + ".manifest.json",
                System.Text.Json.JsonSerializer.Serialize(manifest, DownloadManifestJsonContext.Default.ResumeManifest));

            bool skip = BBDownDownloadUtil.PrepareAria2cTarget(
                "https://cdn.example.com/1080p.mp4?qn=80", path, fileSize: 10, headers: null, contentHeaders: null);

            Assert.False(skip);
            Assert.False(File.Exists(path), "超长残留应被删除重下");
            Assert.False(File.Exists(control), "残留控制文件应被删除");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>返回错误 Content-Range 起始偏移的本地服务：验证下载必须拒绝而非接受错位区间。</summary>
    private sealed class MisleadingRangeServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly byte[] _payload;
        private readonly long _wrongOffset;
        private readonly Task _loop;
        public int Port { get; }

        public MisleadingRangeServer(int payloadSize, long wrongOffset)
        {
            _payload = new byte[payloadSize];
            new Random(7).NextBytes(_payload);
            _wrongOffset = wrongOffset;
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
                            var rangeHeader = ctx.Request.Headers["Range"];
                            if (string.IsNullOrEmpty(rangeHeader))
                            {
                                resp.StatusCode = 200;
                                resp.ContentLength64 = _payload.Length;
                                await resp.OutputStream.WriteAsync(_payload, _cts.Token);
                            }
                            else
                            {
                                // 故意返回与请求不符的起始偏移：声明 bytes {wrongOffset}- ，
                                // 但实际写入的内容从错误位置开始——正常客户端必须拒绝此响应
                                long count = _payload.Length - _wrongOffset;
                                resp.StatusCode = 206;
                                resp.ContentLength64 = count;
                                resp.AddHeader("Content-Range", $"bytes {_wrongOffset}-{_payload.Length - 1}/{_payload.Length}");
                                await resp.OutputStream.WriteAsync(_payload.AsMemory((int)_wrongOffset, (int)count), _cts.Token);
                            }
                            resp.Close();
                        }
                        catch { /* 客户端中止：忽略 */ }
                    }
                }
                catch (HttpListenerException) { /* 服务停止 */ }
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

    /// <summary>本地 HTTP 服务，返回固定长度的字节流（用于多线程下载测试）。</summary>
    private sealed class LocalByteServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly byte[] _payload;
        private readonly Task _loop;
        public int Port { get; }

        /// <summary>服务端载荷的 SHA-256 十六进制串。测试用它校验下载产物内容一致（而非仅长度）。</summary>
        public string PayloadHash { get; }

        public LocalByteServer(int size)
        {
            _payload = new byte[size];
            new Random(42).NextBytes(_payload);
            PayloadHash = TestHash.ComputeSha256Hex(_payload);
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

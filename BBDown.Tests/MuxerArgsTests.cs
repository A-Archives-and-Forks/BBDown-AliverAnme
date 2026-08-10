using System.Diagnostics;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

/// <summary>
/// BBDownMuxer 的参数构造与取消传播测试：
/// 用假进程执行器捕获 MuxAV 生成的 ffmpeg 参数，验证章节 meta 文件下标与 -map 序列的正确性、
/// 取消令牌的透传；并用真实 ffmpeg（可用时）做一次最小混流冒烟验证。
/// </summary>
/// <remarks>
/// 本类会替换静态的 <see cref="BBDownMuxer.ProcessRunner"/>（结束后恢复），
/// 与 <see cref="ExternalProcessRunnerTests"/> 使用同一 xunit Collection 串行执行，
/// 避免并行测试类互相替换/恢复该静态值造成串扰。
/// </remarks>
[Collection("MuxerProcessRunnerCollection")]
public class MuxerArgsTests
{
    /// <summary>记录收到的调用与取消令牌的假执行器。</summary>
    private sealed class FakeProcessRunner : IExternalProcessRunner
    {
        private readonly int _exitCode;
        public List<ExternalProcessSpec> Specs { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public FakeProcessRunner(int exitCode) => _exitCode = exitCode;

        public Task<int> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Specs.Add(spec);
            Tokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_exitCode);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"muxer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { /* 清理失败不影响测试结论 */ }
    }

    /// <summary>
    /// 章节下标快照测试：points 场景下 meta 文件作为最后一个输入加入，
    /// -map_chapters 的下标应是递增前的 inputCount（即 meta 文件自身下标），
    /// 且 -map 序列不得包含该下标（meta 文件只供取章节，不应被 map 进输出）。
    /// </summary>
    [Fact]
    public async Task MuxAV_WithPoints_ChapterIndexPointsToMetaInput()
    {
        var fake = new FakeProcessRunner(exitCode: 0);
        var original = BBDownMuxer.ProcessRunner;
        var tempDir = NewTempDir();
        try
        {
            BBDownMuxer.ProcessRunner = fake;

            var videoPath = Path.Combine(tempDir, "video.mp4");
            var audioPath = Path.Combine(tempDir, "audio.m4a");
            var sub1 = Path.Combine(tempDir, "sub1.srt");
            var sub2 = Path.Combine(tempDir, "sub2.srt");
            File.WriteAllText(videoPath, "v");
            File.WriteAllText(audioPath, "a");
            // 字幕需为非空文件才会被计入输入
            File.WriteAllText(sub1, "1\n00:00:00,000 --> 00:00:01,000\nhello");
            File.WriteAllText(sub2, "1\n00:00:00,000 --> 00:00:01,000\nworld");

            var outPath = Path.Combine(tempDir, "out.mp4");
            var subs = new List<Subtitle>
            {
                new() { lan = "zh-Hans", url = "x", path = sub1 },
                new() { lan = "en", url = "y", path = sub2 },
            };
            // ViewPoint 为 required 字段：title/start/end 都要给全
            var points = new List<ViewPoint>
            {
                new() { title = "开场", start = 0, end = 10 },
                new() { title = "高潮", start = 10, end = 20 },
            };

            await BBDownMuxer.MuxAV(false, "BVtest", videoPath, audioPath, [], outPath, subs: subs, points: points);

            // 输入排列：video(0) / audio(1) / sub1(2) / sub2(3) / meta(4)
            var args = fake.Specs.Single(s => s.FileName == "ffmpeg").Arguments;
            // 定位 meta 文件作为输入的位置：points 块是最后一个追加输入的块，
            // 因此 meta 文件的 -i 是全部 -i 中的最后一个，且其后的文件名以 "chapters-" 开头。
            int metaPosition = -1;
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i] == "-i" && args[i + 1].Contains("chapters-"))
                    metaPosition = i;
            }
            Assert.True(metaPosition >= 0, $"应找到章节 meta 输入，args={string.Join(" ", args)}");
            int metaIndex = CountOf("-i", args, 0, metaPosition);
            Assert.Equal(4, metaIndex);

            // -map_chapters 后紧跟的下标 == meta 文件下标
            int mapChaptersIdx = args.IndexOf("-map_chapters");
            Assert.True(mapChaptersIdx >= 0, "应包含 -map_chapters");
            Assert.Equal(metaIndex.ToString(), args[mapChaptersIdx + 1]);

            // -map 序列只覆盖真实媒体输入（0..metaIndex-1），不得包含 meta 文件下标
            var maps = new List<int>();
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i] == "-map" && int.TryParse(args[i + 1], out var m))
                    maps.Add(m);
            }
            Assert.Equal(new[] { 0, 1, 2, 3 }, maps);
            Assert.DoesNotContain(metaIndex, maps);
        }
        finally
        {
            BBDownMuxer.ProcessRunner = original;
            CleanupDir(tempDir);
        }
    }

    /// <summary>
    /// 取消传播测试：传入已取消的令牌，MuxAV 应将令牌透传给执行器，
    /// 执行器对已取消令牌抛出 OperationCanceledException，取消据此正常向上传播。
    /// </summary>
    [Fact]
    public async Task MuxAV_CancelledToken_ThrowsOperationCanceledAndPropagatesToken()
    {
        var fake = new FakeProcessRunner(exitCode: 0);
        var original = BBDownMuxer.ProcessRunner;
        var tempDir = NewTempDir();
        try
        {
            BBDownMuxer.ProcessRunner = fake;

            var videoPath = Path.Combine(tempDir, "video.mp4");
            var audioPath = Path.Combine(tempDir, "audio.m4a");
            File.WriteAllText(videoPath, "v");
            File.WriteAllText(audioPath, "a");
            var outPath = Path.Combine(tempDir, "out.mp4");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                BBDownMuxer.MuxAV(false, "BVtest", videoPath, audioPath, [], outPath, cancellationToken: cts.Token));

            var spec = fake.Specs.Single(s => s.FileName == "ffmpeg");
            Assert.True(spec.Arguments.Count > 0, "应捕获到 ffmpeg 参数");
            var token = fake.Tokens.Single();
            Assert.True(token.IsCancellationRequested, "执行器收到的令牌应处于已取消状态");
        }
        finally
        {
            BBDownMuxer.ProcessRunner = original;
            CleanupDir(tempDir);
        }
    }

    /// <summary>
    /// 字幕下标快照测试：跳过空/缺失字幕文件后，-metadata:s:s:N 的 N 应连续递增
    /// （第一条有效字幕为 s:0、第二条为 s:1），不得沿用原列表下标——首项被跳过时
    /// 沿用 i 会把元数据写到 s:1/s:2，与真实字幕流错位。
    /// 同时验证 -map 输入下标仍用 inputCount（递增逻辑不变），与字幕流下标解耦。
    /// </summary>
    [Fact]
    public async Task MuxAV_WithSkippedFirstSubtitle_SubtitleStreamIndexIsConsecutive()
    {
        var fake = new FakeProcessRunner(exitCode: 0);
        var original = BBDownMuxer.ProcessRunner;
        var tempDir = NewTempDir();
        try
        {
            BBDownMuxer.ProcessRunner = fake;

            var videoPath = Path.Combine(tempDir, "video.mp4");
            var audioPath = Path.Combine(tempDir, "audio.m4a");
            var emptySub = Path.Combine(tempDir, "sub-empty.srt");   // 跳过：文件存在但为空
            var missingSub = Path.Combine(tempDir, "sub-missing.srt"); // 跳过：文件不存在
            var sub1 = Path.Combine(tempDir, "sub1.srt");
            var sub2 = Path.Combine(tempDir, "sub2.srt");
            File.WriteAllText(videoPath, "v");
            File.WriteAllText(audioPath, "a");
            File.WriteAllText(emptySub, "");
            // missingSub 故意不创建 → File.Exists 为 false
            File.WriteAllText(sub1, "1\n00:00:00,000 --> 00:00:01,000\nhello");
            File.WriteAllText(sub2, "1\n00:00:00,000 --> 00:00:01,000\nworld");

            var outPath = Path.Combine(tempDir, "out.mp4");
            var subs = new List<Subtitle>
            {
                new() { lan = "ja", url = "x", path = emptySub },
                new() { lan = "en", url = "y", path = missingSub },
                new() { lan = "zh-Hans", url = "z", path = sub1 },
                new() { lan = "ko", url = "w", path = sub2 },
            };

            await BBDownMuxer.MuxAV(false, "BVtest", videoPath, audioPath, [], outPath, subs: subs);

            var args = fake.Specs.Single(s => s.FileName == "ffmpeg").Arguments;

            // 只有 sub1、sub2 实际加入输入；输入排列：video(0) / audio(1) / sub1(2) / sub2(3)。
            // -map 输入下标沿用 inputCount（不受跳过影响）。
            var maps = new List<int>();
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i] == "-map" && int.TryParse(args[i + 1], out var m))
                    maps.Add(m);
            }
            Assert.Equal(new[] { 0, 1, 2, 3 }, maps);

            // 字幕元数据流下标连续递增：sub1 的 title/language 均为 s:0，sub2 均为 s:1。
            var subMetaIndexes = new List<int>();
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].StartsWith("-metadata:s:s:"))
                    subMetaIndexes.Add(int.Parse(args[i]["-metadata:s:s:".Length..]));
            }
            Assert.Equal(new[] { 0, 0, 1, 1 }, subMetaIndexes);
        }
        finally
        {
            BBDownMuxer.ProcessRunner = original;
            CleanupDir(tempDir);
        }
    }

    /// <summary>
    /// 真实 ffmpeg 集成冒烟测试：本机 ffmpeg 可用时，用假源生成 1 秒 H.264 + AAC，
    /// 走 MuxAV 的 ffmpeg 分支做 -c copy 混流，断言产物存在且非空。
    /// 不可用时直接返回（CI 约定 --filter "Category!=Integration" 跳过本类）。
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task MuxAV_RealFfmpeg_MuxesVideoAndAudio()
    {
        if (!TryLocateFfmpeg(out var ffmpeg)) return;

        var tempDir = NewTempDir();
        try
        {
            var videoPath = Path.Combine(tempDir, "src-video.mp4");
            var audioPath = Path.Combine(tempDir, "src-audio.m4a");
            var outPath = Path.Combine(tempDir, "out.mp4");

            // 1 秒 128x96 H.264 测试视频
            var videoCode = await RunFfmpeg(ffmpeg, "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=128x96:rate=10",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", videoPath);
            Assert.Equal(0, videoCode);
            Assert.True(File.Exists(videoPath) && new FileInfo(videoPath).Length > 0, "应生成非空测试视频");

            // 1 秒 AAC 测试音频
            var audioCode = await RunFfmpeg(ffmpeg, "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
                "-c:a", "aac", audioPath);
            Assert.Equal(0, audioCode);
            Assert.True(File.Exists(audioPath) && new FileInfo(audioPath).Length > 0, "应生成非空测试音频");

            var code = await BBDownMuxer.MuxAV(false, "BVtest", videoPath, audioPath, [], outPath);
            Assert.Equal(0, code);
            Assert.True(File.Exists(outPath) && new FileInfo(outPath).Length > 0, "混流产物应存在且非空");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    /// <summary>
    /// 真实 ffmpeg 集成测试：空字幕文件被跳过、仅一条有效字幕实际加入输入时，
    /// -metadata:s:s:N 必须用连续递增的字幕流下标（s:0）。若沿用原列表下标会写成
    /// s:1 指向不存在的字幕流，ffmpeg 以 "Invalid stream specifier" 报错退出——
    /// 这里断言退出码 0 且产物生成成功，即回归校验字幕索引错位缺陷。
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task MuxAV_RealFfmpeg_SkipsEmptySubtitleAndMuxesValidSubtitle()
    {
        if (!TryLocateFfmpeg(out var ffmpeg)) return;

        var tempDir = NewTempDir();
        try
        {
            var videoPath = Path.Combine(tempDir, "src-video.mp4");
            var audioPath = Path.Combine(tempDir, "src-audio.m4a");
            var emptySub = Path.Combine(tempDir, "sub-empty.srt");
            var validSub = Path.Combine(tempDir, "sub-valid.srt");
            var outPath = Path.Combine(tempDir, "out.mp4");

            var videoCode = await RunFfmpeg(ffmpeg, "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=128x96:rate=10",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", videoPath);
            Assert.Equal(0, videoCode);

            var audioCode = await RunFfmpeg(ffmpeg, "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
                "-c:a", "aac", audioPath);
            Assert.Equal(0, audioCode);

            // 空字幕文件会被跳过；有效字幕文件实际加入输入
            File.WriteAllText(emptySub, "");
            File.WriteAllText(validSub, "1\n00:00:00,000 --> 00:00:01,000\nhello");

            var subs = new List<Subtitle>
            {
                new() { lan = "ja", url = "x", path = emptySub },
                new() { lan = "en", url = "y", path = validSub },
            };

            var code = await BBDownMuxer.MuxAV(false, "BVtest", videoPath, audioPath, [], outPath, subs: subs);
            Assert.Equal(0, code);
            Assert.True(File.Exists(outPath) && new FileInfo(outPath).Length > 0, "混流产物应存在且非空");
        }
        finally
        {
            CleanupDir(tempDir);
        }
    }

    /// <summary>在 PATH 中查找 ffmpeg 可执行文件。</summary>
    private static bool TryLocateFfmpeg(out string path)
    {
        var candidates = new[] { "ffmpeg", "ffmpeg.exe" };
        foreach (var name in candidates)
        {
            var full = FindOnPath(name);
            if (full != null)
            {
                path = full;
                return true;
            }
        }
        path = "ffmpeg";
        return false;
    }

    private static string? FindOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>启动 ffmpeg 并返回退出码（stdout/stderr 不重定向，直接透传给测试输出）。</summary>
    private static async Task<int> RunFfmpeg(string ffmpeg, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    /// <summary>统计 [start, end) 范围内 -i 出现的次数（用于推算 meta 输入下标）。</summary>
    private static int CountOf(string token, List<string> args, int start, int end)
    {
        int n = 0;
        for (int i = start; i < end; i++)
            if (args[i] == token) n++;
        return n;
    }
}

/// <summary>
/// 串行执行会替换/恢复静态 <see cref="BBDownMuxer.ProcessRunner"/> 的测试类
/// （<see cref="MuxerArgsTests"/> 与 <see cref="ExternalProcessRunnerTests"/>）。
/// 两类的个别测试都对该静态可变属性做"替换 + finally 恢复"，默认并行执行会互相串扰；
/// 放进同一 Collection 后它们在同一串行上下文中依次执行。
/// </summary>
[CollectionDefinition("MuxerProcessRunnerCollection", DisableParallelization = true)]
public class MuxerProcessRunnerCollection
{
}

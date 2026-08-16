using System.Text.Json;
using BBDown;
using BBDown.Core.Util;

namespace BBDown.Tests;

[Collection("MuxerProcessRunnerCollection")]
public class LiveStreamUtilTests
{
    [Theory]
    [InlineData("正常标题", "正常标题")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("", "直播")]
    [InlineData("   ", "直播")]
    public void SanitizeFileName_StripsInvalidChars(string input, string expected)
        => Assert.Equal(expected, LiveStreamUtil.SanitizeFileName(input));

    /// <summary>
    /// concat 合成必须使用 BBDownMuxer.FFMPEG（用户 --ffmpeg-path / PATH 探测的路径），
    /// 而非硬编码 "ffmpeg"。此前硬编码会让用户的显式指定失效，且 PATH 未配置时
    /// 静默失败。
    /// </summary>
    [Fact]
    public async Task ConcatSegments_UsesBBDownMuxerFfmpegPath()
    {
        var fake = new FakeProcessRunner(exitCode: 0);
        var original = BBDownMuxer.ProcessRunner;
        var originalFfmpeg = BBDownMuxer.FFMPEG;
        var dir = Path.Combine(Path.GetTempPath(), "live-segs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            BBDownMuxer.ProcessRunner = fake;
            // 模拟用户显式指定 ffmpeg：FindBinaries 会把这个路径写入 BBDownMuxer.FFMPEG
            BBDownMuxer.FFMPEG = "/opt/custom/ffmpeg";

            var seg1 = Path.Combine(dir, "seg-000.flv");
            var seg2 = Path.Combine(dir, "seg-001.flv");
            File.WriteAllText(seg1, "a");
            File.WriteAllText(seg2, "b");
            var outPath = Path.Combine(dir, "out.flv");

            var ok = await LiveStreamUtil.ConcatSegmentsAsync([seg1, seg2], outPath, CancellationToken.None);

            Assert.True(ok);
            var spec = fake.Specs.Single();
            Assert.Equal("/opt/custom/ffmpeg", spec.FileName); // 不是硬编码 "ffmpeg"
            Assert.Contains("-f", spec.Arguments);
            Assert.Contains("concat", spec.Arguments);
        }
        finally
        {
            BBDownMuxer.ProcessRunner = original;
            BBDownMuxer.FFMPEG = originalFfmpeg;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// concat 列表文件与输出路径必须用绝对路径：自定义 --output 目录时若 CWD 与
    /// 目标目录不同，相对路径的 file '...' 条目会在 concat demuxer 读取时解析失败。
    /// </summary>
    [Fact]
    public async Task ConcatSegments_UsesAbsolutePathsForListAndOutput()
    {
        var fake = new FakeProcessRunner(exitCode: 0);
        var original = BBDownMuxer.ProcessRunner;
        var originalFfmpeg = BBDownMuxer.FFMPEG;
        var dir = Path.Combine(Path.GetTempPath(), "live-segs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            BBDownMuxer.ProcessRunner = fake;
            BBDownMuxer.FFMPEG = "ffmpeg";

            var outPath = Path.Combine(dir, "out.flv");
            var seg1 = Path.Combine(dir, "seg-000.flv");
            File.WriteAllText(seg1, "a");

            await LiveStreamUtil.ConcatSegmentsAsync([seg1], outPath, CancellationToken.None);

            var spec = fake.Specs.Single();
            var args = spec.Arguments;
            // -i 后紧跟的 concat 列表路径必须是绝对路径
            int iIdx = args.IndexOf("-i");
            Assert.True(iIdx >= 0, $"应包含 -i，args={string.Join(" ", args)}");
            Assert.True(Path.IsPathRooted(args[iIdx + 1]),
                $"concat 列表路径应为绝对路径，实际: {args[iIdx + 1]}");
            // 最后一个参数是输出路径，也必须是绝对路径
            Assert.True(Path.IsPathRooted(args[^1]),
                $"输出路径应为绝对路径，实际: {args[^1]}");
            // concat 列表内容必须包含绝对分段路径：假执行器在 finally 删除列表前
            // 捕获其内容（否则方法返回后列表已被清理，无从校验）
            Assert.NotNull(fake.CapturedInput);
            Assert.Contains(Path.GetFullPath(seg1), fake.CapturedInput);
        }
        finally
        {
            BBDownMuxer.ProcessRunner = original;
            BBDownMuxer.FFMPEG = originalFfmpeg;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// 上次录制合并失败保留的分段会话目录，在下次录制启动时**不得被删除**。
    /// 旧实现启动时递归删除整个 .segs 目录，把可恢复资产丢掉（可恢复数据丢失）。
    /// ReportStaleSessions 只提示保留位置，不删除任何非空会话。
    /// </summary>
    [Fact]
    public void ReportStaleSessions_PreservesNonEmptySessionDirectories()
    {
        var dir = Path.Combine(Path.GetTempPath(), "live-segs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 模拟上次失败保留的会话：根目录下有一个带分段的会话子目录
            var segRoot = Path.Combine(dir, "output.flv.segs");
            var staleSession = Path.Combine(segRoot, "session-20260101_000000");
            Directory.CreateDirectory(staleSession);
            File.WriteAllText(Path.Combine(staleSession, "seg-000.flv"), "recoverable-data");

            LiveStreamUtil.ReportStaleSessions(segRoot);

            // 非空旧会话必须原样保留（文件仍存在）
            Assert.True(File.Exists(Path.Combine(staleSession, "seg-000.flv")),
                "上次录制保留的分段不应在下次启动时被删除");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// SelectFlvUrl 的选流纯函数测试（内联 JSON，无网络）：FLV 被优先选中，
    /// availableFormats 收集接口实际提供的全部格式（含被跳过的 ts/fmp4）。
    /// </summary>
    [Fact]
    public void SelectFlvUrl_PicksFlv_AndReportsAllFormats()
    {
        const string playUrl = """
        {
          "stream": [
            {
              "protocol_name": "http_stream",
              "format": [
                { "format_name": "flv", "codec": [
                  { "codec_name": "avc", "base_url": "/live/room/1.flv", "url_info": [
                    { "host": "https://example.com", "extra": "?token=1" } ] }
                ] }
              ]
            },
            {
              "protocol_name": "http_hls",
              "format": [
                { "format_name": "ts", "codec": [
                  { "codec_name": "avc", "base_url": "/live/room/1.m3u8", "url_info": [
                    { "host": "https://hls.example.com", "extra": "?token=2" } ] }
                ] }
              ]
            }
          ]
        }
        """;
        using var doc = JsonDocument.Parse(playUrl);

        var url = LiveStreamUtil.SelectFlvUrl(doc.RootElement, out var formats);

        Assert.Equal("https://example.com/live/room/1.flv?token=1", url);
        Assert.Contains("flv", formats);
        Assert.Contains("ts", formats);
    }

    [Fact]
    public void SelectFlvUrl_OnlyHls_ReturnsNull_AndReportsTs()
    {
        const string playUrl = """
        {
          "stream": [
            {
              "protocol_name": "http_hls",
              "format": [
                { "format_name": "ts", "codec": [
                  { "codec_name": "avc", "base_url": "/live/room/2.m3u8", "url_info": [
                    { "host": "https://hls.example.com", "extra": "?token=2" } ] }
                ] }
              ]
            }
          ]
        }
        """;
        using var doc = JsonDocument.Parse(playUrl);

        var url = LiveStreamUtil.SelectFlvUrl(doc.RootElement, out var formats);

        Assert.Null(url); // HLS 暂不支持：无 FLV 时返回 null，由调用方报可操作错误
        Assert.Equal(new[] { "ts" }, formats);
    }

    [Fact]
    public void SelectFlvUrl_EmptyPlayUrl_ReturnsNull_AndNoFormats()
    {
        using var doc = JsonDocument.Parse("""{"stream": []}""");

        var url = LiveStreamUtil.SelectFlvUrl(doc.RootElement, out var formats);

        Assert.Null(url);
        Assert.Empty(formats);
    }

    /// <summary>记录收到的调用与取消令牌的假执行器。能捕获外部进程的 stdin 输入。
    /// 模拟真实 concat 产物：在 args 最后一个参数（输出路径）生成非空文件，使
    /// ConcatSegmentsAsync 的"产物存在且非空"校验通过。</summary>
    private sealed class FakeProcessRunner : IExternalProcessRunner
    {
        private readonly int _exitCode;
        public List<ExternalProcessSpec> Specs { get; } = [];
        public string? CapturedInput { get; private set; }

        public FakeProcessRunner(int exitCode) => _exitCode = exitCode;

        public Task<int> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Specs.Add(spec);
            // concat 列表通过文件传给 ffmpeg，而非 stdin——但假执行器不真正启动
            // 进程，列表文件在方法 finally 里被删除。这里在删除前读取列表内容，
            // 供断言验证 file '...' 条目使用绝对路径。
            var listArg = spec.Arguments[spec.Arguments.IndexOf("-i") + 1];
            if (File.Exists(listArg))
                CapturedInput = File.ReadAllText(listArg);
            // 模拟 concat 产出：写出非空产物，满足"产物存在且非空"校验
            var outArg = spec.Arguments[^1];
            File.WriteAllText(outArg, "merged");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_exitCode);
        }
    }
}

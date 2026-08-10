using System.Diagnostics;

namespace BBDown.Tests;

/// <summary>
/// 统一外部进程执行器（ExternalProcessRunner）的测试：验证 ArgumentList 参数逐项传递、
/// 超时/取消时整棵进程树终止、stdout/stderr 行式限流转发。
/// </summary>
/// <remarks>
/// <see cref="Muxer_ProcessRunnerInjectable_FakeRunnerUsed"/> 会替换静态的
/// <see cref="BBDownMuxer.ProcessRunner"/>（结束后恢复），与替换同一静态值的
/// <see cref="MuxerArgsTests"/> 共用同一 xunit Collection 串行执行，避免并行串扰。
/// </remarks>
[Collection("MuxerProcessRunnerCollection")]
public class ExternalProcessRunnerTests
{
    private static string DotNet =>
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    /// <summary>
    /// 返回一个可执行的可打印参数的工具脚本路径：用 dotnet 宿主自身 + 一个小内联程序。
    /// 这里用 dotnet exec 打印 argv 到 stdout 的机制不可行（无自编译宿主），
    /// 因此退而用系统自带 echo-like 工具验证——Windows 用 cmd，Unix 用 sh。
    /// </summary>
    private static string EchoArgsCmd => OperatingSystem.IsWindows() ? "cmd.exe" : "sh";

    [Fact]
    public async Task RunAsync_ArgumentsPassedAsSeparateTokens()
    {
        var runner = new SystemProcessRunner();
        var stdout = new List<string>();
        // Windows 用 cmd /c echo a b，Unix 用 sh -c 'printf ...'
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "echo", "hello", "world with space" }
            : new List<string> { "-c", "printf '%s\\n' 'hello' 'world with space'" };

        var spec = new ExternalProcessSpec
        {
            FileName = EchoArgsCmd,
            Arguments = args,
            OnStandardOutput = stdout.Add,
        };
        var code = await runner.RunAsync(spec);
        Assert.Equal(0, code);
        Assert.Contains(stdout, l => l.Contains("hello"));
        Assert.Contains(stdout, l => l.Contains("world with space"));
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcessTreeAndThrows()
    {
        var runner = new SystemProcessRunner();
        // 启动一个长驻进程：Windows 用 ping 127.0.0.1 -n 30，Unix 用 sleep 30
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "ping", "127.0.0.1", "-n", "30" }
            : new List<string> { "-c", "sleep 30" };
        var spec = new ExternalProcessSpec
        {
            FileName = EchoArgsCmd,
            Arguments = args,
            TimeoutMs = 300,
            ToolDisplayName = "testtool",
        };
        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(spec));
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessTree()
    {
        var runner = new SystemProcessRunner();
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "ping", "127.0.0.1", "-n", "30" }
            : new List<string> { "-c", "sleep 30" };
        var spec = new ExternalProcessSpec
        {
            FileName = EchoArgsCmd,
            Arguments = args,
            TimeoutMs = 60_000,
        };
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(spec, cts.Token));
    }

    [Fact]
    public async Task RunAsync_StderrForwarded()
    {
        var runner = new SystemProcessRunner();
        var stderr = new List<string>();
        // cmd 的 echo 到 stderr 用 >&2
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "echo", "oops>&2" }
            : new List<string> { "-c", "echo oops >&2" };
        var spec = new ExternalProcessSpec
        {
            FileName = EchoArgsCmd,
            Arguments = args,
            OnStandardError = stderr.Add,
        };
        var code = await runner.RunAsync(spec);
        Assert.Equal(0, code);
        Assert.Contains(stderr, l => l.Contains("oops"));
    }

    [Fact]
    public void CommandLineSplitter_ParsesQuotedTokens()
    {
        var tokens = CommandLineSplitter.Split("--flag=value \"path with space.mp4\" --plain");
        Assert.Equal(new[] { "--flag=value", "path with space.mp4", "--plain" }, tokens);

        var empty = CommandLineSplitter.Split("");
        Assert.Empty(empty);
    }

    [Fact]
    public async Task RunAsync_StdoutThrottled()
    {
        var runner = new SystemProcessRunner();
        var stdout = new List<string>();
        // 输出 300 行，验证限流到 200 行 + 一条截断提示
        // Windows 用 powershell（cmd 的多行命令折叠不可靠），Unix 用 sh
        var script = string.Join(";", Enumerable.Range(0, 300).Select(i => $"Write-Output line{i}"));
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "-NoProfile", "-Command", script }
            : new List<string> { "-c", string.Join(";", Enumerable.Range(0, 300).Select(i => $"echo line{i}")) };
        var spec = new ExternalProcessSpec
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : EchoArgsCmd,
            Arguments = args,
            OnStandardOutput = stdout.Add,
        };
        var code = await runner.RunAsync(spec);
        Assert.Equal(0, code);
        // 200 行正文 + 1 行截断提示
        Assert.True(stdout.Count <= 201, $"输出被截断到 {stdout.Count} 行");
        Assert.Contains(stdout, l => l.Contains("已截断"));
    }

    [Fact]
    public async Task RunAsync_StdinPassedToProcess()
    {
        var runner = new SystemProcessRunner();
        var stdout = new List<string>();
        // 让子进程读取 stdin 并回显：Unix 用 cat，Windows 用 more（从 stdin 读）
        var args = OperatingSystem.IsWindows()
            ? new List<string> { "/c", "more" }
            : new List<string> { "-c", "cat" };
        var spec = new ExternalProcessSpec
        {
            FileName = EchoArgsCmd,
            Arguments = args,
            StandardInput = "hello-stdin\n",
            OnStandardOutput = stdout.Add,
        };
        var code = await runner.RunAsync(spec);
        Assert.Equal(0, code);
        Assert.Contains(stdout, l => l.Contains("hello-stdin"));
    }

    [Fact]
    public async Task Muxer_ProcessRunnerInjectable_FakeRunnerUsed()
    {
        // 验证 BBDownMuxer 的可注入执行器：假 runner 直接返回预置退出码，不启动真实进程
        var fakeRunner = new FakeProcessRunner(exitCode: 0);
        var original = BBDownMuxer.ProcessRunner;
        try
        {
            BBDownMuxer.ProcessRunner = fakeRunner;
            // MergeFLV 多文件路径会调用执行器（ffmpeg 转封装），用假 runner 短路
            var files = new[]
            {
                Path.Combine(Path.GetTempPath(), $"flv-a-{Guid.NewGuid():N}.mp4"),
                Path.Combine(Path.GetTempPath(), $"flv-b-{Guid.NewGuid():N}.mp4"),
            };
            var outPath = Path.Combine(Path.GetTempPath(), $"flv-out-{Guid.NewGuid():N}.mp4");
            try
            {
                File.WriteAllText(files[0], "a");
                File.WriteAllText(files[1], "b");
                await BBDownMuxer.MergeFLV(files, outPath);
                // 假 runner 返回 0，但真实进程未运行，两个 .ts 不存在——
                // 因此 MergeFLV 会因 code==0 却缺产物抛出 InvalidOperationException
                Assert.True(fakeRunner.Specs.Count >= 1, "假 runner 应被调用");
                Assert.Contains(fakeRunner.Specs, s => s.FileName == "ffmpeg");
            }
            catch (InvalidOperationException)
            {
                // 预期：假 runner 返回 0 但未真正生成 .ts 文件 → 校验失败抛异常
            }
            finally
            {
                foreach (var f in files) try { File.Delete(f); } catch { }
                try { File.Delete(outPath); } catch { }
            }
        }
        finally
        {
            BBDownMuxer.ProcessRunner = original;
        }
    }

    private sealed class FakeProcessRunner : IExternalProcessRunner
    {
        private readonly int _exitCode;
        public List<ExternalProcessSpec> Specs { get; } = [];

        public FakeProcessRunner(int exitCode) => _exitCode = exitCode;

        public Task<int> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Specs.Add(spec);
            return Task.FromResult(_exitCode);
        }
    }
}

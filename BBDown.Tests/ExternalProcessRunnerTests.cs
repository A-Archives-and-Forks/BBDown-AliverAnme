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
        // 启动一个长驻进程树：根进程派生一个持续写哨兵文件的子进程，验证超时后
        // 是“整棵进程树”被终止而非只杀根进程。若退化为只杀根，孤儿子进程会继续
        // 写文件（哨兵尺寸持续增长），此断言即失败——旧测试只断言异常类型，
        // “杀根不杀子”时仍通过，证据为零（F6）。
        // Unix: 外层 sh 派生后台子 shell 写哨兵（两层树），自身 sleep 60。
        // 哨兵写入间隔 20ms：杀树后 600ms 内不再增长可被可靠检测。
        var sentinel = Path.Combine(Path.GetTempPath(), "bbdown-killtree-" + Guid.NewGuid().ToString("N") + ".log");
        var (fileName, args) = OperatingSystem.IsWindows()
            // Windows: cmd 派生 ping（写哨兵文件），自身等待——cmd + ping 两层树
            ? ("cmd.exe", new List<string> { "/c", "ping", "-n", "300", "127.0.0.1", ">", sentinel })
            : ("sh", new List<string> { "-c", $"(while true; do echo x >> '{sentinel}'; sleep 0.02; done) & sleep 60" });
        var spec = new ExternalProcessSpec
        {
            FileName = fileName,
            Arguments = args,
            TimeoutMs = 1000,
            ToolDisplayName = "testtool",
        };
        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(spec));

        // 树被杀后哨兵必须停止增长：先等进程终止彻底落定（管道关闭/句柄释放），
        // 再采样两次尺寸验证不再增长。
        await Task.Delay(500);
        var size1 = new FileInfo(sentinel).Length;
        await Task.Delay(600);
        var size2 = new FileInfo(sentinel).Length;
        Assert.True(size2 == size1,
            $"进程树应被整棵终止（哨兵文件不应再增长），实际 0.6s 内从 {size1} 增至 {size2} 字节");
        try { File.Delete(sentinel); } catch { }
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessTree()
    {
        var runner = new SystemProcessRunner();
        // 与 Timeout 测试同样的进程树哨兵：取消必须终止整棵进程树（含写哨兵的
        // 子进程），而非只杀根。
        var sentinel = Path.Combine(Path.GetTempPath(), "bbdown-killtree-" + Guid.NewGuid().ToString("N") + ".log");
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new List<string> { "/c", "ping", "-n", "300", "127.0.0.1", ">", sentinel })
            : ("sh", new List<string> { "-c", $"(while true; do echo x >> '{sentinel}'; sleep 0.02; done) & sleep 60" });
        var spec = new ExternalProcessSpec
        {
            FileName = fileName,
            Arguments = args,
            TimeoutMs = 60_000,
        };
        using var cts = new CancellationTokenSource(1000);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(spec, cts.Token));

        await Task.Delay(500);
        var size1 = new FileInfo(sentinel).Length;
        await Task.Delay(600);
        var size2 = new FileInfo(sentinel).Length;
        Assert.True(size2 == size1,
            $"取消应终止整棵进程树（哨兵文件不应再增长），实际 0.6s 内从 {size1} 增至 {size2} 字节");
        try { File.Delete(sentinel); } catch { }
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
                // 确定性断言：假 runner 返回 0 但未生成 .ts 产物，MergeFLV 必须抛
                // InvalidOperationException（“code==0 却缺产物”的失败保护契约）。
                // 此前 try/catch(InvalidOperationException) 把异常吞掉，抛/不抛两种结果
                // 都通过——失败保护契约从未被真正验证（F7）。
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    BBDownMuxer.MergeFLV(files, outPath));
                // 失败保护契约应说明保留源分段以便重试（Assert.Contains 不带消息变体，
                // 用 True+Contains 提供失败诊断）
                Assert.True(ex.Message.Contains("保留源分段", StringComparison.Ordinal),
                    $"失败保护契约应说明保留源分段以便重试，实际消息: {ex.Message}");
                // 假 runner 应被调用且确以 ffmpeg 执行转封装
                Assert.True(fakeRunner.Specs.Count >= 1, "假 runner 应被调用");
                Assert.Contains(fakeRunner.Specs, s => s.FileName == "ffmpeg");
                // 源分段保留：ffmpeg 失败（缺产物）时不能被删除，否则整批不可恢复重试
                Assert.True(File.Exists(files[0]), "ffmpeg 失败时源分段 0 必须保留");
                Assert.True(File.Exists(files[1]), "ffmpeg 失败时源分段 1 必须保留");
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

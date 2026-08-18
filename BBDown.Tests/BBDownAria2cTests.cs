using BBDown;

namespace BBDown.Tests;

/// <summary>
/// aria2c 外部调用测试：退出码必须被校验（此前被完全忽略，非零退出但产物存在的
/// 场景会被当作成功进入混流），且须启用 --continue=true 断点续传（此前中断后整文件重下）。
/// 替换静态 <see cref="BBDownAria2c.ProcessRunner"/>，测试结束恢复。
/// </summary>
public class BBDownAria2cTests
{
    /// <summary>捕获调用参数并返回预设退出码的假执行器。</summary>
    private sealed class FakeAria2cRunner : IExternalProcessRunner
    {
        private readonly int _exitCode;
        public List<ExternalProcessSpec> Specs { get; } = [];

        public FakeAria2cRunner(int exitCode) => _exitCode = exitCode;

        public Task<int> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Specs.Add(spec);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_exitCode);
        }
    }

    [Fact]
    public async Task DownloadFileByAria2cAsync_NonZeroExit_Throws()
    {
        var fake = new FakeAria2cRunner(exitCode: 4); // 达到最大重试次数
        var original = BBDownAria2c.ProcessRunner;
        try
        {
            BBDownAria2c.ProcessRunner = fake;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BBDownAria2c.DownloadFileByAria2cAsync("http://example.com/a.mp4", "out/a.mp4", ""));

            // 错误信息应包含可读的退出码说明
            Assert.Contains("4", ex.Message);
            Assert.Contains("达到最大重试次数", ex.Message);
        }
        finally
        {
            BBDownAria2c.ProcessRunner = original;
        }
    }

    [Fact]
    public async Task DownloadFileByAria2cAsync_ZeroExit_DoesNotThrow_AndEnablesResume()
    {
        var fake = new FakeAria2cRunner(exitCode: 0);
        var original = BBDownAria2c.ProcessRunner;
        try
        {
            BBDownAria2c.ProcessRunner = fake;

            await BBDownAria2c.DownloadFileByAria2cAsync("http://example.com/a.mp4", "out/a.mp4", "");

            var spec = fake.Specs.Single();
            // 断点续传已启用：中断留下的 .aria2 控制文件可续传，而非整文件重下
            Assert.Contains(spec.Arguments, a => a.Contains("--continue=true"));
            // 凭据走 stdin 而非命令行参数
            Assert.NotNull(spec.StandardInput);
        }
        finally
        {
            BBDownAria2c.ProcessRunner = original;
        }
    }

    /// <summary>
    /// 取消必须原样传播为 OperationCanceledException，不能被转成"aria2c 下载失败"的
    /// InvalidOperationException——否则用户 Ctrl+C 会被误报为下载失败。
    /// </summary>
    [Fact]
    public async Task DownloadFileByAria2cAsync_UserCancellation_PropagatesOperationCanceled()
    {
        var fake = new FakeAria2cRunner(exitCode: 0);
        var original = BBDownAria2c.ProcessRunner;
        try
        {
            BBDownAria2c.ProcessRunner = fake;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                BBDownAria2c.DownloadFileByAria2cAsync("http://example.com/a.mp4", "out/a.mp4", "", cts.Token));
        }
        finally
        {
            BBDownAria2c.ProcessRunner = original;
        }
    }
}

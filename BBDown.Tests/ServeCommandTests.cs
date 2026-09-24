using BBDown;
using BBDown.Commands;
using BBDown.Core;
using BBDown.Core.Util;
using Spectre.Console.Cli;
using Xunit;

namespace BBDown.Tests;

[Collection("ServeApiCollection")]
public class ServeCommandTests
{
    [Fact]
    public void ResolveServeToken_BothNull_ReturnsNull()
    {
        var token = ServeCommand.ResolveServeToken(null, null);
        Assert.Null(token);
    }

    [Fact]
    public void ResolveServeToken_CliOnly_ReturnsCliToken()
    {
        var token = ServeCommand.ResolveServeToken("my-cli-token", null);
        Assert.Equal("my-cli-token", token);
    }

    [Fact]
    public void ResolveServeToken_EnvOnly_ReturnsEnvToken()
    {
        var token = ServeCommand.ResolveServeToken(null, "my-env-token");
        Assert.Equal("my-env-token", token);
    }

    [Fact]
    public void ResolveServeToken_BothSetDifferent_EnvTakesPrecedence()
    {
        // 核心安全/运维契约：环境变量优先于 CLI 参数，且不静默覆盖
        var token = ServeCommand.ResolveServeToken("old-cli-token", "new-env-token");
        Assert.Equal("new-env-token", token);
    }

    [Fact]
    public void ResolveServeToken_BothSetSame_ReturnsToken()
    {
        var token = ServeCommand.ResolveServeToken("same-token", "same-token");
        Assert.Equal("same-token", token);
    }

    [Fact]
    public void ResolveServeToken_EmptyEnvFallsBackToCli()
    {
        var token = ServeCommand.ResolveServeToken("my-cli-token", "");
        Assert.Equal("my-cli-token", token);
    }

    [Fact]
    public void ResolveServeToken_EmptyCliUsesEnv()
    {
        var token = ServeCommand.ResolveServeToken("", "my-env-token");
        Assert.Equal("my-env-token", token);
    }

    [Theory]
    [InlineData("bilibili.com", true)]
    [InlineData("api.bilibili.com", true)]
    [InlineData("data.api.bilibili.com", true)]
    [InlineData("b23.tv", true)]
    [InlineData("bilivideo.com", true)]
    [InlineData("upos-sz-mirrorcoso1.bilivideo.com", true)]
    [InlineData("hdslb.com", true)]
    [InlineData("i0.hdslb.com", true)]
    [InlineData("biliapi.net", true)]
    [InlineData("grpc.biliapi.net", true)]
    [InlineData("biliapi.com", true)]
    [InlineData("bilibili.tv", true)]
    [InlineData("biliintl.com", true)]
    [InlineData("aisee.tv", true)]
    [InlineData("snm0516.aisee.tv", true)]
    [InlineData("evil.com", false)]
    [InlineData("notbilibili.com", false)]
    [InlineData("bilibili.com.evil.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HTTPUtil_IsOfficialBilibiliHost_ValidatesCorrectly(string? host, bool expected)
    {
        Assert.Equal(expected, HTTPUtil.IsOfficialBilibiliHost(host));
    }

    /// <summary>
    /// 用户主动关停（根 token 已取消）→ 返回 0（RF-38）：经 StartServerAsync →
    /// WebApplication.RunAsync 的真实关停路径，钉住"取消 → 0"主干不被重构破坏。
    /// 注意（对抗审查指出）：预取消 token 下修复前的无守卫 catch 同样返回 0，
    /// 本用例不判别 when 守卫本身——"未取消 OCE → 1"的判别性回归需向
    /// StartServerAsync 注入非根 token 的 OCE，命令层无此注入缝，以代码走查为证。
    /// ServeCommand.ExecuteAsync 为 protected，子类暴露直调。与 ServeApiCollection
    /// 串行：StartServerAsync 会触碰进程级 Logger.LogFilePath，
    /// try/finally 快照恢复，避免与其它用例并发互扰。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_UserCanceled_ReturnsZero()
    {
        var originalLogPath = Logger.LogFilePath;
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var command = new ExposedServeCommand();
            Assert.Equal(0, await command.ExecutePublicAsync(null!, new ServeSettings(), cts.Token));
        }
        finally
        {
            Logger.LogFilePath = originalLogPath;
        }
    }

    /// <summary>ServeCommand.ExecuteAsync 是 protected：子类暴露以供测试直调。</summary>
    private sealed class ExposedServeCommand : ServeCommand
    {
        public Task<int> ExecutePublicAsync(CommandContext context, ServeSettings settings, CancellationToken cancellationToken)
            => ExecuteAsync(context, settings, cancellationToken);
    }
}

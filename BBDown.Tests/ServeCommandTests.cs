using BBDown.Commands;
using BBDown.Core.Util;
using Xunit;

namespace BBDown.Tests;

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
}

using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// B2：UA 按异步流隔离。HTTPUtil.GetUserAgent 的优先级：
/// 显式参数 → Config.Current.UserAgent（当前流）→ 进程级随机默认值。
/// </summary>
public class HttpUtilUserAgentTests
{
    [Fact]
    public void GetUserAgent_ExplicitParam_Wins()
    {
        Assert.Equal("custom-ua", HTTPUtil.GetUserAgent("custom-ua"));
    }

    [Fact]
    public void GetUserAgent_FlowConfig_UsedWhenNoExplicit()
    {
        var original = Config.Current.UserAgent;
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { UserAgent = "flow-ua" });
            Assert.Equal("flow-ua", HTTPUtil.GetUserAgent(null));
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { UserAgent = original });
        }
    }

    [Fact]
    public void GetUserAgent_FallsBackToRandomDefault()
    {
        var original = Config.Current.UserAgent;
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { UserAgent = "" });
            var ua = HTTPUtil.GetUserAgent(null);
            Assert.False(string.IsNullOrEmpty(ua));
            Assert.Contains("Mozilla/5.0", ua);
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { UserAgent = original });
        }
    }

    [Fact]
    public void GetUserAgent_ExplicitOverridesFlowConfig()
    {
        var original = Config.Current.UserAgent;
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { UserAgent = "flow-ua" });
            Assert.Equal("custom-ua", HTTPUtil.GetUserAgent("custom-ua"));
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { UserAgent = original });
        }
    }
}
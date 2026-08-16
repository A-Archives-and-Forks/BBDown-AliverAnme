using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// A1：SSL 校验策略与连接池解耦。AppHttpClient 按当前异步流配置路由到
/// 校验池/不安全池，两者各自持有独立的 SocketsHttpHandler 连接池——
/// --insecure 任务建立的未验证连接不会被其它任务复用。
/// </summary>
public class HttpUtilSslPolicyTests
{
    [Fact]
    public void AppHttpClient_RoutesToVerifiedPool_WhenFlowDoesNotSkipSslCheck()
    {
        var original = Config.Current.SkipSslCheck;
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { SkipSslCheck = false });
            // 未跳过校验 → AppHttpClient 与始终校验的共享实例是同一个对象（同一连接池）
            Assert.Same(HTTPUtil.VerifiedAppHttpClient, HTTPUtil.AppHttpClient);
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { SkipSslCheck = original });
        }
    }

    [Fact]
    public void AppHttpClient_RoutesToInsecurePool_WhenFlowSkipsSslCheck()
    {
        var original = Config.Current.SkipSslCheck;
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { SkipSslCheck = true });
            // 跳过校验 → 路由到独立的不安全池：与校验池不是同一实例（连接不复用）
            Assert.NotSame(HTTPUtil.VerifiedAppHttpClient, HTTPUtil.AppHttpClient);
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { SkipSslCheck = original });
        }
    }

    [Fact]
    public void VerifiedAppHttpClient_AlwaysVerified_RegardlessOfFlowConfig()
    {
        var original = Config.Current.SkipSslCheck;
        try
        {
            // 即使当前流跳过了校验，VerifiedAppHttpClient 仍指向始终校验的池：
            // WidevineCdm 许可证请求（携带内容密钥）强制走此池
            Config.ApplyToCurrentAsyncFlow(Config.Current with { SkipSslCheck = true });
            var verified = HTTPUtil.VerifiedAppHttpClient;
            Config.ApplyToCurrentAsyncFlow(Config.Current with { SkipSslCheck = false });
            Assert.Same(verified, HTTPUtil.VerifiedAppHttpClient);
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { SkipSslCheck = original });
        }
    }
}
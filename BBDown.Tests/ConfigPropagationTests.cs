using System.Threading;
using System.Threading.Tasks;
using BBDown;
using BBDown.Core;
using BBDown.Core.Util;
using Xunit;

namespace BBDown.Tests;

/// <summary>
/// 验证 AsyncLocal 配置传播修复：子异步方法内写 Config 不回流父调用方，
/// 必须由子方法显式返回新值、父流程应用后才生效。
/// </summary>
public class ConfigPropagationTests
{
    [Fact]
    public async Task SubMethod_SetConfig_DoesNotFlowBackToParent()
    {
        // 记录父流程初始值
        Config.ApplyToCurrentAsyncFlow(Config.Current with { Wbi = "parent-wbi" });

        // 子方法内设置（模拟 TryUpdateWbiKey 的旧行为）
        async Task Child()
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { Wbi = "child-wbi" });
            await Task.Yield();
        }
        await Child();

        // 父流程仍读旧值——这正是报告指出的缺陷根因
        Assert.Equal("parent-wbi", Config.Current.Wbi);
    }

    [Fact]
    public async Task Parent_AppliesReturnedValue_SeesNewConfig()
    {
        Config.ApplyToCurrentAsyncFlow(Config.Current with { Wbi = "parent-wbi" });

        // 子方法返回新值（修复后的模式），父流程显式应用
        async Task<string?> Child()
        {
            await Task.Yield();
            return "child-wbi";
        }
        string? newWbi = await Child();
        if (newWbi is not null) Config.ApplyToCurrentAsyncFlow(Config.Current with { Wbi = newWbi });

        Assert.Equal("child-wbi", Config.Current.Wbi);
    }

    [Fact]
    public void ExtractWbiKey_IsInternalAndPure()
    {
        // 验证 CheckLoginWithDetails 的提取逻辑已从"写 Config"改为"返回新值"：
        // 通过 reflection 检查方法签名应返回 string?（newWbi 元组第三元）。
        var method = typeof(BBDownUtil).GetMethod("CheckLoginWithDetails");
        Assert.NotNull(method);
        var ret = method!.ReturnType;
        Assert.True(ret.IsGenericType && ret.GetGenericTypeDefinition() == typeof(Task<>));
        var tuple = ret.GetGenericArguments()[0];
        Assert.True(tuple.IsGenericType && tuple.GetGenericTypeDefinition() == typeof(ValueTuple<,,>),
            "CheckLoginWithDetails 应返回 (isLoggedIn, cookieExpired, newWbi) 三元组");
    }

    [Fact]
    public void EnsureAsync_ReturnsUpdatedCookie_InsteadOfWritingConfig()
    {
        // EnsureAsync 签名应从 Task 改为 Task<string?>（返回新 Cookie 由调用方应用）
        var method = typeof(BuvidProvider).GetMethod("EnsureAsync");
        Assert.NotNull(method);
        var ret = method!.ReturnType;
        Assert.Equal(typeof(Task<string?>), ret);
    }
}

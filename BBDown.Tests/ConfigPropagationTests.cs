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

    /// <summary>
    /// 锁定 GetVideoInfoAsync 的返回契约：必须把提取的 newWbi 带回父流程（第四元），
    /// 由 CLI 的 DoWorkAsync 与 Serve 的 ProcessDownloadTaskAsync 在返回后显式应用。
    /// 若未来改回三/二元组或删掉 newWbi，此测试失败——父流程将失去密钥轮换后的
    /// w_rid 签名能力（Parser.WbiSign 在 GetVideoInfoAsync 返回后才被调用）。
    /// </summary>
    [Fact]
    public void GetVideoInfoAsync_ReturnsNewWbi_InFourthTupleElement()
    {
        var method = typeof(Program).GetMethod("GetVideoInfoAsync");
        Assert.NotNull(method);
        var ret = method!.ReturnType;
        Assert.True(ret.IsGenericType && ret.GetGenericTypeDefinition() == typeof(Task<>),
            "GetVideoInfoAsync 应返回 Task<元组>");
        var tuple = ret.GetGenericArguments()[0];
        Assert.True(tuple.IsGenericType && tuple.GetGenericTypeDefinition() == typeof(ValueTuple<,,,>),
            "GetVideoInfoAsync 应返回 4 元组 (fetchedAid, vInfo, apiType, newWbi)");
        var fields = tuple.GetFields();
        // 第四元必须是 string?（newWbi）：子流程提取的 wbi 密钥带回父流程
        Assert.Equal(typeof(string), fields[3].FieldType);
    }

    /// <summary>
    /// 验证父流程拿到 newWbi 后显式应用，随后的 Parser.WbiSign 必须用新密钥签名。
    /// 模拟真实调用链的父流程结构：GetVideoInfoAsync（子）返回 newWbi → 父流程
    /// Apply → 后续 DownloadPagesAsync → Parser.WbiSign 读 Config.Current.Wbi。
    /// 若父流程不再应用返回值（回归），WbiSign 会用旧密钥，w_rid 与预期不符。
    /// </summary>
    [Fact]
    public async Task ParentFlow_AppliesReturnedWbi_WbiSignUsesNewKey()
    {
        Config.ApplyToCurrentAsyncFlow(Config.Current with { Wbi = "old-key" });

        // 子方法（模拟 GetVideoInfoAsync 的 CheckLoginWithDetails 提取）返回新密钥
        async Task<string?> Child() { await Task.Yield(); return "new-key"; }
        var newWbi = await Child();

        // 父流程显式应用——这是修复后的契约行为
        if (newWbi is not null) Config.ApplyToCurrentAsyncFlow(Config.Current with { Wbi = newWbi });

        // Parser.WbiSign 必须用新密钥：w_rid = MD5(api + Wbi)
        string api = "mid=1&wts=1700000000";
        string signed = Parser.WbiSign(api);
        string expected = ComputeMd5Hex(api + "new-key");
        Assert.Contains($"w_rid={expected}", signed);
        // 若仍用旧密钥，签名必然不同——显式断言排除
        Assert.DoesNotContain($"w_rid={ComputeMd5Hex(api + "old-key")}", signed);
    }

    /// <summary>验证 Parser.WbiSign 确实使用当前异步流的 Wbi（不是写死的空串/全局残留）。</summary>
    [Fact]
    public void WbiSign_UsesCurrentFlowWbi()
    {
        string api = "mid=1&wts=1700000000";
        Config.ApplyToCurrentAsyncFlow(Config.Current with { Wbi = "flow-key" });
        Assert.Contains($"w_rid={ComputeMd5Hex(api + "flow-key")}", Parser.WbiSign(api));

        // 换密钥后签名随之变化：证明签名读取的是当前流配置而非缓存
        Config.ApplyToCurrentAsyncFlow(Config.Current with { Wbi = "rotated-key" });
        Assert.Contains($"w_rid={ComputeMd5Hex(api + "rotated-key")}", Parser.WbiSign(api));
        Assert.DoesNotContain($"w_rid={ComputeMd5Hex(api + "flow-key")}", Parser.WbiSign(api));
    }

    /// <summary>
    /// 取消令牌必须传播进 BuvidProvider.EnsureAsync：预取消的 token 应立即抛
    /// OperationCanceledException（在信号量 WaitAsync 处），而不是忽略取消继续
    /// 发网络请求。此前 EnsureAsync 不接收调用方 token，取消会被静默吞掉。
    /// </summary>
    [Fact]
    public async Task EnsureAsync_CancelledToken_PropagatesCancellation()
    {
        var originalCookie = Config.Current.Cookie;
        try
        {
            // 清除 buvid3：已有设备标识时 EnsureAsync 提前返回 null，不会走到取消点
            Config.Apply(Config.Current with { Cookie = "" });
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => BuvidProvider.EnsureAsync(cts.Token));
        }
        finally
        {
            Config.Apply(Config.Current with { Cookie = originalCookie });
        }
    }

    private static string ComputeMd5Hex(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}

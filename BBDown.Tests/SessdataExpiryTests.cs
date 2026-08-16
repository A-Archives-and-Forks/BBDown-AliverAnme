using BBDown;

namespace BBDown.Tests;

/// <summary>
/// 3.3：Cookie 无自动刷新（2026 新版 Set-Cookie 登录不落盘 refresh_token，协议层面
/// 不可自动轮转），务实方案是"即将过期"提前警告。EstimateSessdataExpiryDays 纯本地
/// 解析 SESSDATA（base64 JSON）估算剩余天数，解析必须 fail-open（失败返回 null 不误报）。
/// </summary>
public class SessdataExpiryTests
{
    private static string BuildSessdata(long expiryUnix)
    {
        var json = $"{{\"id\":1,\"expires\":{expiryUnix}}}";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        // B 站 SESSDATA 用逗号连接字段、URL 编码（%2C）
        return $"{b64}%2Csecond%2Cthird";
    }

    [Fact]
    public void Estimate_ValidSessdata_ReturnsApproxDays()
    {
        var cookie = $"SESSDATA={BuildSessdata(DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds())}";
        var days = BBDownUtil.EstimateSessdataExpiryDays(cookie);
        Assert.NotNull(days);
        Assert.InRange(days!.Value, 9, 10); // 10 天后过期，容差 1 天
    }

    [Fact]
    public void Estimate_ExpiredSessdata_ReturnsNonPositive()
    {
        var cookie = $"SESSDATA={BuildSessdata(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds())}";
        var days = BBDownUtil.EstimateSessdataExpiryDays(cookie);
        Assert.NotNull(days);
        Assert.True(days!.Value <= 0);
    }

    [Fact]
    public void Estimate_NoSessdata_ReturnsNull()
    {
        Assert.Null(BBDownUtil.EstimateSessdataExpiryDays("bili_jct=abc; DedeUserID=123"));
    }

    [Fact]
    public void Estimate_GarbageBase64_ReturnsNull()
    {
        // 无法解析必须 fail-open：不抛、返回 null（不触发警告）
        var cookie = "SESSDATA=!!!not-base64!!!%2Cb%2Cc";
        Assert.Null(BBDownUtil.EstimateSessdataExpiryDays(cookie));
    }

    [Fact]
    public void Estimate_JsonWithoutExpires_ReturnsNull()
    {
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"id\":1}"));
        var cookie = $"SESSDATA={b64}%2Cb%2Cc";
        Assert.Null(BBDownUtil.EstimateSessdataExpiryDays(cookie));
    }

    [Fact]
    public void Estimate_EmptyCookie_ReturnsNull()
    {
        Assert.Null(BBDownUtil.EstimateSessdataExpiryDays(""));
        Assert.Null(BBDownUtil.EstimateSessdataExpiryDays("SESSDATA="));
    }
}

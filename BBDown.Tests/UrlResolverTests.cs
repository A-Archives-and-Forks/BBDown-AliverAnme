using System.Threading.Tasks;
using Xunit;

namespace BBDown.Tests;

/// <summary>
/// URL 解析测试。
/// 标记为 "NetworkIntegration" 的测试需要真实网络访问 Bilibili API。
/// CI 的本地/单元 job 用 --filter "Category!=Integration & Category!=NetworkIntegration
/// & Category!=LocalIntegration" 排除，网络集成 job 单独运行。
/// </summary>
public class UrlResolverTests
{
    // ── 需要网络的集成测试 ──

    [Theory]
    [Trait("Category", "NetworkIntegration")]
    [InlineData("https://www.bilibili.com/video/av170001")]
    [InlineData("https://www.bilibili.com/video/Av170001")]
    public async Task ResolveAsync_AvVideoUrl_ReturnsAvId(string url)
    {
        var result = await UrlResolver.ResolveAsync(url);
        Assert.True(long.TryParse(result, out var aid));
        Assert.Equal(170001, aid);
    }

    [Theory]
    [Trait("Category", "NetworkIntegration")]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD")]
    [InlineData("https://www.bilibili.com/video/bv1xx411c7mD")]
    public async Task ResolveAsync_BvVideoUrl_ReturnsAvId(string url)
    {
        var result = await UrlResolver.ResolveAsync(url);
        Assert.True(long.TryParse(result, out var aid));
        Assert.True(aid > 0);
    }

    [Theory]
    [Trait("Category", "NetworkIntegration")]
    [InlineData("av170001")]
    [InlineData("AV170001")]
    public async Task ResolveAsync_AvId_ReturnsAvId(string input)
    {
        var result = await UrlResolver.ResolveAsync(input);
        Assert.Equal("170001", result);
    }

    [Theory]
    [Trait("Category", "NetworkIntegration")]
    [InlineData("BV1xx411c7mD")]
    [InlineData("bv1xx411c7mD")]
    public async Task ResolveAsync_BvId_ReturnsAvId(string input)
    {
        var result = await UrlResolver.ResolveAsync(input);
        Assert.True(long.TryParse(result, out var aid));
        Assert.True(aid > 0);
    }

    [Fact]
    [Trait("Category", "NetworkIntegration")]
    public async Task ResolveAsync_EpUrl_ReturnsEpId()
    {
        var result = await UrlResolver.ResolveAsync("https://www.bilibili.com/bangumi/play/ep12345");
        Assert.Equal("ep:12345", result);
    }

    [Fact]
    [Trait("Category", "NetworkIntegration")]
    public async Task ResolveAsync_SsUrl_ReturnsEpFormat()
    {
        // SS ID requires a network call; use a currently valid public season.
        var result = await UrlResolver.ResolveAsync("https://www.bilibili.com/bangumi/play/ss41410");
        Assert.StartsWith("ep:", result);
    }

    // ── 纯本地解析（无需网络） ──

    [Fact]
    public async Task ResolveAsync_CheeseUrl_ReturnsCheeseFormat()
    {
        var result = await UrlResolver.ResolveAsync("cheese/ep123");
        Assert.Equal("cheese:123", result);
    }

    [Fact]
    public async Task ResolveAsync_MidUrl_ReturnsMidFormat()
    {
        var result = await UrlResolver.ResolveAsync("https://space.bilibili.com/12345");
        Assert.Equal("mid:12345", result);
    }

    [Fact]
    public async Task ResolveAsync_InvalidInput_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => UrlResolver.ResolveAsync("invalid_input"));
    }

    [Fact]
    public async Task ResolveAsync_InvalidBv_ThrowsReadableError()
    {
        // 畸形 BV 输入（如裸 "bv"）此前会因字符串切片越界崩溃；
        // 现在应返回可读错误而非底层异常
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => UrlResolver.ResolveAsync("bv"));
        Assert.Contains("无法识别", ex.Message);
    }

    // ── 额外本地解析测试 ──

    [Theory]
    [Trait("Category", "NetworkIntegration")] // av 前缀会经 FixAvidAsync 发起真实网络跳转检查，不能算纯本地解析
    [InlineData("av12345", "12345")]
    [InlineData("AV99999", "99999")]
    public async Task ResolveAsync_RawAvId_LocalParsing(string input, string expected)
    {
        // 注意：AV ID 会经过 FixAvidAsync 做网络跳转检查，但返回值仍是纯数字
        var result = await UrlResolver.ResolveAsync(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("mid:123", "mid:123")]
    [InlineData("favId:1:2", "favId:1:2")]
    [InlineData("listBizId:123", "listBizId:123")]
    [InlineData("seriesBizId:123", "seriesBizId:123")]
    [InlineData("ep:123", "ep:123")]
    [InlineData("ep123", "ep:123")]
    public async Task ResolveAsync_PrefixTargets_PassThrough(string input, string expected)
    {
        // sub add 承诺支持 mid:/favId:/listBizId:/seriesBizId: 裸前缀；ep: 与 ep 两种写法应归一
        var result = await UrlResolver.ResolveAsync(input);
        Assert.Equal(expected, result);
    }

    // ── 泛抓取域名白名单（SSRF/凭据外发防护） ──

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD")]
    [InlineData("https://www.bilibili.com/bangumi/play/ep12345")]
    [InlineData("https://b23.tv/abc123")]
    [InlineData("https://api.bilibili.com/x/web-interface/view")]
    [InlineData("https://upos-sz-mirrorcoso1.bilivideo.com/upgcx")]
    [InlineData("http://hdslb.com/")]
    [InlineData("https://www.bilibili.tv/intl/video")]
    [InlineData("https://api.snm0516.aisee.tv/")]
    public void IsTrustedBilibiliUrl_OfficialHosts_Allowed(string url)
        => Assert.True(UrlResolver.IsTrustedBilibiliUrl(url));

    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("https://evilb23.tv/")]            // 后缀冒充：host 非精确 b23.tv
    [InlineData("https://b23.tv.evil.com/")]       // 真 bilibili.com 域名陷阱
    [InlineData("https://bilibili.com.evil.com/")]
    [InlineData("http://127.0.0.1:23333/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("https://api.bilibili.com@evil.com/")]  // userinfo 混淆
    [InlineData("not a url")]
    [InlineData("ftp://bilibili.com/")]
    public void IsTrustedBilibiliUrl_UntrustedHosts_Rejected(string url)
        => Assert.False(UrlResolver.IsTrustedBilibiliUrl(url));

    [Fact]
    public void IsTrustedBilibiliUrl_PathSuffixConfusion_Rejected()
    {
        // 斜杠/路径混淆：纯字符串后缀匹配会放行 "evil.com/.bilibili.com"，
        // Uri.Host 规范化后必须拒绝
        Assert.False(UrlResolver.IsTrustedBilibiliUrl("http://evil.com/.bilibili.com"));
    }

    [Fact]
    public async Task ResolveAsync_UntrustedHttpUrl_RejectedBeforeNetwork()
    {
        // 泛抓取分支会向用户输入的 URL 主机发请求：攻击者域名必须在发起任何
        // 网络请求之前被域名白名单拦截（否则携带操作者 Cookie 的请求会外发凭据）。
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => UrlResolver.ResolveAsync("https://evil.example.com/"));
        Assert.Contains("仅支持解析 B 站域名", ex.Message);
    }
}

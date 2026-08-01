using System.Threading.Tasks;
using Xunit;

namespace BBDown.Tests;

/// <summary>
/// URL 解析测试。
/// 标记为 "Integration" 的测试需要网络访问 Bilibili API。
/// CI 环境建议用 dotnet test --filter "Category!=Integration" 排除。
/// </summary>
public class UrlResolverTests
{
    // ── 需要网络的集成测试 ──

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("https://www.bilibili.com/video/av170001")]
    [InlineData("https://www.bilibili.com/video/Av170001")]
    public async Task ResolveAsync_AvVideoUrl_ReturnsAvId(string url)
    {
        var result = await UrlResolver.ResolveAsync(url);
        Assert.True(long.TryParse(result, out var aid));
        Assert.Equal(170001, aid);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD")]
    [InlineData("https://www.bilibili.com/video/bv1xx411c7mD")]
    public async Task ResolveAsync_BvVideoUrl_ReturnsAvId(string url)
    {
        var result = await UrlResolver.ResolveAsync(url);
        Assert.True(long.TryParse(result, out var aid));
        Assert.True(aid > 0);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("av170001")]
    [InlineData("AV170001")]
    public async Task ResolveAsync_AvId_ReturnsAvId(string input)
    {
        var result = await UrlResolver.ResolveAsync(input);
        Assert.Equal("170001", result);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("BV1xx411c7mD")]
    [InlineData("bv1xx411c7mD")]
    public async Task ResolveAsync_BvId_ReturnsAvId(string input)
    {
        var result = await UrlResolver.ResolveAsync(input);
        Assert.True(long.TryParse(result, out var aid));
        Assert.True(aid > 0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveAsync_EpUrl_ReturnsEpId()
    {
        var result = await UrlResolver.ResolveAsync("https://www.bilibili.com/bangumi/play/ep12345");
        Assert.Equal("ep:12345", result);
    }

    [Fact]
    [Trait("Category", "Integration")]
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
    [InlineData("av12345", "12345")]
    [InlineData("AV99999", "99999")]
    public async Task ResolveAsync_RawAvId_LocalParsing(string input, string expected)
    {
        // 注意：AV ID 会经过 FixAvidAsync 做网络跳转检查，但返回值仍是纯数字
        var result = await UrlResolver.ResolveAsync(input);
        Assert.Equal(expected, result);
    }
}

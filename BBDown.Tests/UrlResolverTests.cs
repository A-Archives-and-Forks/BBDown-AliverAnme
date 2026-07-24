using System.Threading.Tasks;
using Xunit;

namespace BBDown.Tests;

public class UrlResolverTests
{
    [Theory]
    [InlineData("https://www.bilibili.com/video/av170001")]
    [InlineData("https://www.bilibili.com/video/Av170001")]
    public async Task ResolveAsync_AvVideoUrl_ReturnsAvId(string url)
    {
        var result = await UrlResolver.ResolveAsync(url);
        Assert.True(long.TryParse(result, out var aid));
        Assert.Equal(170001, aid);
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD")]
    [InlineData("https://www.bilibili.com/video/bv1xx411c7mD")]
    public async Task ResolveAsync_BvVideoUrl_ReturnsAvId(string url)
    {
        var result = await UrlResolver.ResolveAsync(url);
        Assert.True(long.TryParse(result, out var aid));
        Assert.True(aid > 0);
    }

    [Theory]
    [InlineData("av170001")]
    [InlineData("AV170001")]
    public async Task ResolveAsync_AvId_ReturnsAvId(string input)
    {
        var result = await UrlResolver.ResolveAsync(input);
        Assert.Equal("170001", result);
    }

    [Theory]
    [InlineData("BV1xx411c7mD")]
    [InlineData("bv1xx411c7mD")]
    public async Task ResolveAsync_BvId_ReturnsAvId(string input)
    {
        var result = await UrlResolver.ResolveAsync(input);
        Assert.True(long.TryParse(result, out var aid));
        Assert.True(aid > 0);
    }

    [Fact]
    public async Task ResolveAsync_EpUrl_ReturnsEpId()
    {
        var result = await UrlResolver.ResolveAsync("https://www.bilibili.com/bangumi/play/ep12345");
        Assert.Equal("ep:12345", result);
    }

    [Fact]
    public async Task ResolveAsync_SsUrl_ReturnsEpFormat()
    {
        // SS ID requires a network call; use a currently valid public season.
        var result = await UrlResolver.ResolveAsync("https://www.bilibili.com/bangumi/play/ss41410");
        Assert.StartsWith("ep:", result);
    }

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
}

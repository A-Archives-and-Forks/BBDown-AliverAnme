using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// buvid3 只应在缺失时补充，绝不能覆盖用户自带的 Cookie。
/// </summary>
public class BuvidProviderTests
{
    [Theory]
    [InlineData("buvid3=ABC123", true)]
    [InlineData("SESSDATA=x;buvid3=ABC123", true)]
    [InlineData("buvid3=ABC123;SESSDATA=x", true)]
    [InlineData("BUVID3=ABC123", true)]          // B 站自身大小写并不统一
    [InlineData("SESSDATA=x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasBuvid3_DetectsPresenceRegardlessOfPosition(string? cookie, bool expected)
    {
        Assert.Equal(expected, BuvidProvider.HasBuvid3(cookie));
    }

    [Fact]
    public void HasBuvid3_DoesNotMatchSimilarKeys()
    {
        // buvid4 / _buvid3 之类不应被误判成已具备 buvid3
        Assert.False(BuvidProvider.HasBuvid3("buvid4=ABC"));
        Assert.False(BuvidProvider.HasBuvid3("buvid=ABC"));
    }
}

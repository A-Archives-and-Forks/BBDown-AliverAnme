using BBDown.Core;

namespace BBDown.Tests;

public class DanmakuFilterTests
{
    // B站弹幕 p 属性: 进度,模式,字号,颜色,ctime,弹幕池,midHash,rowID → midHash 在 index 6
    private static DanmakuUtil.DanmakuItem Make(string content, string midHash = "10001")
        => new(["1", "1", "25", "16777215", "1600000000", "1", midHash], content);

    [Fact]
    public void Filter_Keyword_RemovesMatchingDanmaku()
    {
        var items = new[] { Make("广告：加群领福利"), Make("正常弹幕"), Make("XX广告") };
        var result = DanmakuUtil.Filter(items, "广告", null);
        Assert.Single(result);
        Assert.Equal("正常弹幕", result[0].Content);
    }

    [Fact]
    public void Filter_MidHash_RemovesThatSendersDanmaku()
    {
        var items = new[] { Make("弹幕A", "111"), Make("弹幕B", "222"), Make("弹幕C", "111") };
        var result = DanmakuUtil.Filter(items, null, "111");
        Assert.Single(result);
        Assert.Equal("弹幕B", result[0].Content);
    }

    [Fact]
    public void Filter_NoConditions_ReturnsAll()
    {
        var items = new[] { Make("a"), Make("b") };
        Assert.Equal(2, DanmakuUtil.Filter(items, null, null).Length);
    }

    [Fact]
    public void Filter_MultipleKeywords_AnyMatchRemoves()
    {
        var items = new[] { Make("QQ群"), Make("正常"), Make("加微信") };
        var result = DanmakuUtil.Filter(items, "QQ群,微信", null);
        Assert.Single(result);
        Assert.Equal("正常", result[0].Content);
    }

    [Fact]
    public void Filter_KeywordAndMidHash_Combine()
    {
        var items = new[] { Make("广告", "111"), Make("正常", "111"), Make("正常", "222") };
        var result = DanmakuUtil.Filter(items, "广告", "111");
        Assert.Single(result);
        Assert.Equal("正常", result[0].Content);
        Assert.Equal("222", result[0].MidHash);
    }
}

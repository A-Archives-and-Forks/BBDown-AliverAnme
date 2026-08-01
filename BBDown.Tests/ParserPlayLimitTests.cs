using System.Text.Json;
using BBDown.Core;

namespace BBDown.Tests;

public class ParserPlayLimitTests
{
    [Fact]
    public void ThrowIfPlayLimited_AreaLimit_ThrowsClearMessage()
    {
        const string json = """
        {"code":0,"message":"success","result":{"play_check":{"limit_play_reason":"AREA_LIMIT","play_detail":"PLAY_NONE"},"play_view_business_info":{"episode_info":{"aid":0,"cid":0,"delivery_business_fragment_video":false,"delivery_fragment_video":false,"ep_id":4448895,"ep_status":0},"user_status":{"follow_info":{"follow":0,"follow_status":2},"is_login":1,"vip_info":{"due_date":1786723200000,"real_vip":true,"status":1,"type":2}}}}}
        """;

        using var doc = JsonDocument.Parse(json);
        var ex = Assert.Throws<InvalidOperationException>(() => Parser.ThrowIfPlayLimited(doc.RootElement));

        Assert.Contains("区域限制", ex.Message);
        Assert.Contains("limit_play_reason=AREA_LIMIT", ex.Message);
        Assert.Contains("play_detail=PLAY_NONE", ex.Message);
    }

    [Fact]
    public void ThrowIfBizError_NonZeroCode_ThrowsReadableMessage()
    {
        const string json = """{"code":-86038,"message":"抱歉，您所在地区暂时无法观看","data":{}}""";
        using var doc = JsonDocument.Parse(json);
        var ex = Assert.Throws<InvalidOperationException>(() => Parser.ThrowIfBizError(doc.RootElement));
        Assert.Contains("86038", ex.Message);
        Assert.Contains("无法观看", ex.Message);
    }

    [Theory]
    [InlineData("""{"code":0,"message":"success","data":{}}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"code":"-412","data":{}}""")]  // code 非数字
    [InlineData("""[1,2,3]""")]                    // 根节点非对象
    public void ThrowIfBizError_ZeroOrMissingOrNonNumericCode_DoesNotThrow(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Parser.ThrowIfBizError(doc.RootElement); // 不应抛异常
    }
}

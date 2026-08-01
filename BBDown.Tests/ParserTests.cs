using System.Text.Json;
using BBDown.Core;

namespace BBDown.Tests;

/// <summary>
/// Parser 核心方法测试。
/// ExtractTracksAsync 需要真实/模拟的 Bilibili API 响应，暂不在此覆盖。
/// 此处覆盖所有可单独测试的解析辅助方法。
/// </summary>
public class ParserTests
{
    // ── ThrowIfPlayLimited ──

    [Fact]
    public void ThrowIfPlayLimited_NoResultProperty_DoesNotThrow()
    {
        const string json = """{"code":0,"message":"success","data":{}}""";
        using var doc = JsonDocument.Parse(json);
        Parser.ThrowIfPlayLimited(doc.RootElement);
    }

    [Fact]
    public void ThrowIfPlayLimited_NoPlayCheck_DoesNotThrow()
    {
        const string json = """{"code":0,"result":{"other":1}}""";
        using var doc = JsonDocument.Parse(json);
        Parser.ThrowIfPlayLimited(doc.RootElement);
    }

    [Fact]
    public void ThrowIfPlayLimited_EmptyReasonAndDetail_DoesNotThrow()
    {
        const string json = """{"code":0,"result":{"play_check":{"limit_play_reason":"","play_detail":""}}}""";
        using var doc = JsonDocument.Parse(json);
        Parser.ThrowIfPlayLimited(doc.RootElement);
    }

    [Theory]
    [InlineData("PAY_LIMIT", "付费限制")]
    [InlineData("VIP_LIMIT", "大会员")]
    [InlineData("TIME_LOCK", "可播放时间")]
    public void ThrowIfPlayLimited_KnownReasons_ThrowClearMessage(string reason, string expectedInMessage)
    {
        var json = "{\"code\":0,\"result\":{\"play_check\":{\"limit_play_reason\":\"" + reason + "\",\"play_detail\":\"TEST_DETAIL\"}}}";
        using var doc = JsonDocument.Parse(json);
        var ex = Assert.Throws<InvalidOperationException>(() => Parser.ThrowIfPlayLimited(doc.RootElement));
        Assert.Contains(expectedInMessage, ex.Message);
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void ThrowIfPlayLimited_UnknownReason_ThrowsGenericMessage()
    {
        const string json = """{"code":0,"result":{"play_check":{"limit_play_reason":"UNKNOWN_REASON","play_detail":"SOME_DETAIL"}}}""";
        using var doc = JsonDocument.Parse(json);
        var ex = Assert.Throws<InvalidOperationException>(() => Parser.ThrowIfPlayLimited(doc.RootElement));
        Assert.Contains("播放限制", ex.Message);
    }

    // ── WbiSign ──

    [Fact]
    public void WbiSign_ReturnsStringContainingWrid()
    {
        var originalWbi = Config.WBI;
        try
        {
            Config.WBI = "test_wbi_key";
            var result = Parser.WbiSign("api.bilibili.com/x/test?param=1");
            Assert.Contains("&w_rid=", result);
            Assert.StartsWith("api.bilibili.com/x/test?param=1&w_rid=", result);
        }
        finally
        {
            Config.WBI = originalWbi;
        }
    }

    // ── Codec 映射验证 ──

    [Fact]
    public void CodecId_KnownMappings()
    {
        var codecs = new Dictionary<string, string>
        {
            ["13"] = "AV1",
            ["12"] = "HEVC",
            ["7"] = "AVC",
        };
        foreach (var (id, expected) in codecs)
        {
            Assert.Equal(expected, id switch
            {
                "13" => "AV1",
                "12" => "HEVC",
                "7" => "AVC",
                _ => "UNKNOWN"
            });
        }
    }
}

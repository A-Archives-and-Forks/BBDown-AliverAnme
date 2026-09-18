using BBDown.Core.Entity;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

/// <summary>
/// Entity 值对象的服务器可控输入防御（第 14 轮消纳批 RF-48/RF-60）。
/// </summary>
public class EntityTests
{
    private static Page MakePage(string aid) => new(1, aid, "1", "", "t", 0, "", 0, "", "");

    // ── RF-48：Page.bvid getter 对服务器可控 aid 的 AOORE 防护 ──

    [Theory]
    [InlineData("170001")]
    [InlineData("0")]          // Encode 范围校验下界：AOORE → 回落原始 aid
    [InlineData("-5")]         // 负数：AOORE → 回落原始 aid
    [InlineData("2251799813685248")] // >= MAX_AID（2^51）：AOORE → 回落原始 aid
    public void Page_Bvid_OutOfRangeAid_FallsBackToRawAid(string aid)
    {
        var p = MakePage(aid);
        if (long.TryParse(aid, out var n) && n >= 1 && n < 2251799813685248L)
            Assert.Equal(BBDown.Core.Util.BilibiliBvConverter.Encode(n), p.bvid);
        else
            Assert.Equal(aid, p.bvid);
    }

    [Fact]
    public void Page_Bvid_NonNumericAid_ReturnsRawAid()
    {
        Assert.Equal("BV1xx411c7mD", MakePage("BV1xx411c7mD").bvid);
    }

    // ── RF-60：Audio.shortCodecs 文化不变性 ──

    [Fact]
    public void Audio_ShortCodecs_IsCultureInvariant()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // RF-68：tr-TR 下 'i' 经文化敏感 ToUpper() 变 'İ'（U+0130），查表失败静默退化选轨优先级。
            // 输入必须含小写 'i' 才能触发该规则——原输入 "e-ac-3" 不含 'i'，ToUpperInvariant
            // 与回退后的 ToUpper() 在 tr-TR 下产出相同，断言恒成立（假绿）。此处用 "avci"（AVC Intra）作回归输入。
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var a = new Audio { id = "1", dfn = "", baseUrl = "https://x", codecs = "avci", bandwidth = 0, dur = 0 };
            Assert.Equal("AVCI", a.shortCodecs);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}

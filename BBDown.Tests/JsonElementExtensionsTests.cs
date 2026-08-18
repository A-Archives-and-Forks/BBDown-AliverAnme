using System.Text.Json;
using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// JsonElementExtensions 的取值语义。重点锁定 GetStringSafe 与 GetValueAsStringSafe
/// 在字段为数字时的差异——B 站接口的 code 等字段常是 JSON 数字，
/// 用错方法会静默得到空串（logintv 轮询曾因此永远误判成功）。
/// </summary>
public class JsonElementExtensionsTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void GetStringSafe_ReturnsEmptyForNumericField()
    {
        // 这正是 bug 的根源：数字字段用 GetStringSafe 读出空串
        var e = Parse("""{"code": 86038}""");
        Assert.Equal("", e.GetStringSafe("code"));
    }

    [Fact]
    public void GetValueAsStringSafe_StringifiesNumericField()
    {
        // 修复所依赖的契约：数字被 ToString 成其字面量
        var e = Parse("""{"code": 86038}""");
        Assert.Equal("86038", e.GetValueAsStringSafe("code"));
    }

    [Fact]
    public void GetValueAsStringSafe_ReadsStringFieldVerbatim()
    {
        var e = Parse("""{"code": "86039"}""");
        Assert.Equal("86039", e.GetValueAsStringSafe("code"));
    }

    [Theory]
    [InlineData("""{"other": 1}""")]   // 字段缺失
    [InlineData("""{"code": null}""")]  // 值为 null
    public void GetValueAsStringSafe_ReturnsDefaultWhenMissingOrNull(string json)
    {
        Assert.Equal("", Parse(json).GetValueAsStringSafe("code"));
    }

    [Fact]
    public void GetValueAsStringSafe_OnNonObject_ReturnsDefault()
    {
        Assert.Equal("", Parse("""[1,2,3]""").GetValueAsStringSafe("code"));
    }

    [Fact]
    public void GetStringSafe_ReadsStringFieldVerbatim()
    {
        var e = Parse("""{"name": "abc"}""");
        Assert.Equal("abc", e.GetStringSafe("name"));
    }

    [Theory]
    [InlineData("""{"code": 12345}""", 12345)]
    [InlineData("""{"code": "12345"}""", 12345)]
    [InlineData("""{"code": -101}""", -101)]
    [InlineData("""{"code": "-101"}""", -101)]
    [InlineData("""{"code": "invalid"}""", 0)]
    [InlineData("""{"code": null}""", 0)]
    [InlineData("""{"other": 1}""", 0)]
    public void GetInt32Safe_HandlesNumbersAndStringNumbers(string json, int expected)
    {
        Assert.Equal(expected, Parse(json).GetInt32Safe("code"));
    }

    [Theory]
    [InlineData("""{"code": 9876543210}""", 9876543210L)]
    [InlineData("""{"code": "9876543210"}""", 9876543210L)]
    [InlineData("""{"code": -101}""", -101L)]
    [InlineData("""{"code": "-101"}""", -101L)]
    [InlineData("""{"code": "invalid"}""", 0L)]
    [InlineData("""{"code": null}""", 0L)]
    public void GetInt64Safe_HandlesNumbersAndStringNumbers(string json, long expected)
    {
        Assert.Equal(expected, Parse(json).GetInt64Safe("code"));
    }

    [Theory]
    [InlineData("""{"val": 12.34}""", 12.34)]
    [InlineData("""{"val": "12.34"}""", 12.34)]
    [InlineData("""{"val": "invalid"}""", 0.0)]
    public void GetDoubleSafe_HandlesNumbersAndStringNumbers(string json, double expected)
    {
        Assert.Equal(expected, Parse(json).GetDoubleSafe("val"), precision: 2);
    }

    [Fact]
    public void StringNumberParsing_IsCultureInvariant()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var e = Parse("""{"i": "12345", "l": "9876543210", "d": "12.34"}""");
            Assert.Equal(12345, e.GetInt32Safe("i"));
            Assert.Equal(9876543210L, e.GetInt64Safe("l"));
            Assert.Equal(12.34, e.GetDoubleSafe("d"), precision: 2);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}

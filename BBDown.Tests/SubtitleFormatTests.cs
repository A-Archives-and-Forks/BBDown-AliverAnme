using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// SRT 时间轴与正文的格式约束。
/// 时间轴用 hh 格式符会丢掉超过 24 小时的天数；
/// 正文里的空行会被 SRT 解析器当作块分隔，令其后所有字幕错位。
/// </summary>
public class SubtitleFormatTests
{
    [Theory]
    [InlineData(0, "00:00:00,000")]
    [InlineData(64.13, "00:01:04,130")]
    [InlineData(3661, "01:01:01,000")]
    [InlineData(86399, "23:59:59,000")]
    public void FormatTime_MatchesSrtLayout(double seconds, string expected)
    {
        Assert.Equal(expected, SubUtil.FormatTime(seconds));
    }

    [Theory]
    // 旧实现用 hh（取 TimeSpan.Hours，0-23），这两个值分别退化为
    // 00:00:00 与 01:00:00，超长视频的字幕会整体跳回开头
    [InlineData(86400, "24:00:00,000")]
    [InlineData(90000, "25:00:00,000")]
    [InlineData(360000, "100:00:00,000")]
    public void FormatTime_KeepsHoursBeyondOneDay(double seconds, string expected)
    {
        Assert.Equal(expected, SubUtil.FormatTime(seconds));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-3600)]
    [InlineData(double.NaN)]
    public void FormatTime_ClampsInvalidInputToZero(double seconds)
    {
        // 旧实现把 -1 秒格式化成 00:00:01，符号被静默丢弃
        Assert.Equal("00:00:00,000", SubUtil.FormatTime(seconds));
    }

    [Fact]
    public void SanitizeSrtContent_RemovesBlankLinesThatWouldSplitTheCue()
    {
        var sanitized = SubUtil.SanitizeSrtContent("第一行\n\n第二行");

        Assert.Equal("第一行\n第二行", sanitized);
        Assert.DoesNotContain("\n\n", sanitized);
    }

    [Fact]
    public void SanitizeSrtContent_PreservesLegitimateMultilineText()
    {
        // 多行字幕本身是合法的，不能一并压成单行
        Assert.Equal("上句\n下句", SubUtil.SanitizeSrtContent("上句\n下句"));
    }

    [Theory]
    [InlineData("行一\r\n行二", "行一\n行二")]
    [InlineData("行一\r行二", "行一\n行二")]
    [InlineData("行一   \n   行二", "行一\n   行二")]
    public void SanitizeSrtContent_NormalizesLineEndings(string input, string expected)
    {
        Assert.Equal(expected, SubUtil.SanitizeSrtContent(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("   \n  \n ")]
    public void SanitizeSrtContent_WhitespaceOnly_BecomesEmpty(string input)
    {
        Assert.Equal(string.Empty, SubUtil.SanitizeSrtContent(input));
    }

    [Fact]
    public void SanitizeSrtContent_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SubUtil.SanitizeSrtContent(null));
    }

    [Fact]
    public void SanitizeSrtContent_PlainText_IsUnchanged()
    {
        const string plain = "普通字幕 with English 123";
        Assert.Equal(plain, SubUtil.SanitizeSrtContent(plain));
    }
}

using BBDown;

namespace BBDown.Tests;

public class LiveStreamUtilTests
{
    [Theory]
    [InlineData("正常标题", "正常标题")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("", "直播")]
    [InlineData("   ", "直播")]
    public void SanitizeFileName_StripsInvalidChars(string input, string expected)
        => Assert.Equal(expected, LiveStreamUtil.SanitizeFileName(input));
}

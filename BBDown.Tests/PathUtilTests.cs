using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// PathUtil.GetValidFileName 的路径安全测试：非法字符过滤、保留名、长路径截断。
/// 长路径截断是 Windows MAX_PATH 260 的防线（单组件 255 上限），阈值式生效：
/// 仅超长输入被截断，短文件名行为必须零变化。
/// </summary>
public class PathUtilTests
{
    [Theory]
    [InlineData("正常标题", "正常标题")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("NUL.foo", "_NUL.foo")]
    public void GetValidFileName_ShortInput_UnchangedOrSanitized(string input, string expected)
        => Assert.Equal(expected, PathUtil.GetValidFileName(input));

    // ── 长路径截断 ──

    [Fact]
    public void GetValidFileName_TruncatesLongBaseName()
    {
        var input = new string('长', 300);
        var result = PathUtil.GetValidFileName(input);
        Assert.Equal(100, result.Length);
    }

    [Fact]
    public void GetValidFileName_TruncatesLongBaseName_AndKeepsExtension()
    {
        var input = new string('视', 250) + ".mp4";
        var result = PathUtil.GetValidFileName(input);
        // 基名截断到 96 + 保留 ".mp4"（4 字符）＝ 100
        Assert.Equal(100, result.Length);
        Assert.EndsWith(".mp4", result);
        Assert.Equal(96, result[..^4].Length);
    }

    [Fact]
    public void GetValidFileName_AtLimit_Unchanged()
    {
        var input = new string('x', 100);
        Assert.Equal(input, PathUtil.GetValidFileName(input));
    }

    [Fact]
    public void GetValidFileName_BaseNameWithinLimit_ExtensionNotClipped()
    {
        // 基名 99 + ".mp4" = 总长 103 > 100，但基名未超限（单组件 < 255 无 PathTooLong
        // 风险）：不得截断导致扩展名被截掉（旧逻辑会把 ".mp4" 尾巴截断成残缺文件名）。
        var input = new string('x', 99) + ".mp4";
        Assert.Equal(input, PathUtil.GetValidFileName(input));
    }

    [Fact]
    public void GetValidFileName_OneOverLimit_Truncated()
    {
        var input = new string('x', 101);
        Assert.Equal(new string('x', 100), PathUtil.GetValidFileName(input));
    }

    [Fact]
    public void GetValidFileName_LongInput_NotMisjudgedAsReservedName()
    {
        // 保留名判定是完全匹配（CON/PRN/...），超长名（"CON" + 200 个 x）截断后
        // 不是恰好为保留名，不应加 _ 前缀——截断只缩长度，不改变保留名语义。
        var input = "CON" + new string('x', 200);
        var result = PathUtil.GetValidFileName(input);
        Assert.Equal(100, result.Length);
        Assert.False(result.StartsWith("_")); // 截断后非恰好保留名：不加前缀
        Assert.StartsWith("CON", result);     // 原内容保留
    }

    [Fact]
    public void GetValidFileName_LongWithFilterSlash_SanitizedAndTruncated()
    {
        var input = new string('a', 80) + "/" + new string('b', 80);
        var result = PathUtil.GetValidFileName(input, filterSlash: true);
        Assert.Equal(100, result.Length);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\\", result);
    }

    [Fact]
    public void GetValidFileName_CustomLimit_Respected()
    {
        var input = new string('x', 200);
        var result = PathUtil.GetValidFileName(input, maxBaseNameLength: 60);
        Assert.Equal(60, result.Length);
    }
}

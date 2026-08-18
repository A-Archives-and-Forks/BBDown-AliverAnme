namespace BBDown.Tests;

/// <summary>
/// -p 分P选择表达式的解析。
/// 表达式直接来自用户输入，解析结果决定下载哪些分P，
/// 因此"选不中任何分P"必须是显式错误而不是静默返回空。
/// </summary>
public class PageSelectionTests
{
    [Theory]
    [InlineData("1", new[] { "1" })]
    [InlineData("1,2,10", new[] { "1", "2", "10" })]
    [InlineData("1-3", new[] { "1", "2", "3" })]
    [InlineData("5-5", new[] { "5" })]
    public void ParsesPlainListsAndRanges(string input, string[] expected)
    {
        Assert.Equal(expected, Program.ParsePageSelection(input));
    }

    [Theory]
    [InlineData("1-3,7", new[] { "1", "2", "3", "7" })]
    [InlineData("2,4-6", new[] { "2", "4", "5", "6" })]
    [InlineData("1-2,5,8-9", new[] { "1", "2", "5", "8", "9" })]
    public void ParsesMixedRangeAndListSyntax(string input, string[] expected)
    {
        // 旧实现只要出现 '-' 就整体按范围切分，"1-3,7" 会被切成 "1" / "3,7"
        // 并在 int.Parse("3,7") 处失败，导致整条参数被判为无效
        Assert.Equal(expected, Program.ParsePageSelection(input));
    }

    [Fact]
    public void ReversedRange_ThrowsInsteadOfSelectingNothing()
    {
        // 旧实现的 for (i = 10; i <= 1; i++) 一次都不执行，
        // 于是既不下载也不报错
        var ex = Assert.Throws<ArgumentException>(() => Program.ParsePageSelection("10-1"));
        Assert.Contains("起始值大于结束值", ex.Message);
    }

    [Fact]
    public void HugeRange_IsRejectedBeforeExpansion()
    {
        var ex = Assert.Throws<ArgumentException>(() => Program.ParsePageSelection("1-99999999"));
        Assert.Contains("展开后超过", ex.Message);
    }

    [Theory]
    [InlineData("abc-def")]
    [InlineData("1-")]
    [InlineData("1-x")]
    public void UnparsableRange_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => Program.ParsePageSelection(input));
    }

    [Theory]
    [InlineData(",")]
    [InlineData(",,")]
    [InlineData("  ")]
    public void SelectionThatMatchesNothing_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => Program.ParsePageSelection(input));
    }

    [Fact]
    public void WhitespaceAroundSegments_IsTolerated()
    {
        Assert.Equal(new[] { "1", "2", "3", "9" }, Program.ParsePageSelection(" 1 - 3 , 9 "));
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("-1-5")]
    [InlineData("0-5")]
    public void NonPositiveSegment_Throws(string input)
    {
        // 负数与 0 不是合法的分P编号：提前报错拦截，防止静默少下
        Assert.Throws<ArgumentException>(() => Program.ParsePageSelection(input));
    }

    [Theory]
    [InlineData("3,abc")]
    [InlineData("abc")]
    [InlineData("1,2,x")]
    public void NonNumericSegment_Throws(string input)
    {
        // 非数字段（拼写错误/别名展开残留）若被静默放行，上层会当作不存在的分P
        // 无声丢弃——-p 3,5EST 只下 P3 不报错。解析层必须显式抛错。
        Assert.Throws<ArgumentException>(() => Program.ParsePageSelection(input));
    }

    [Theory]
    [InlineData("LATEST", 5, "5")]
    [InlineData("LAST", 5, "5")]
    [InlineData("NEW", 5, "5")]
    [InlineData("1,LATEST", 5, "1,5")]
    [InlineData("LATEST,3", 5, "5,3")]
    [InlineData("1-2,LATEST", 5, "1-2,5")]
    [InlineData("1-LATEST", 5, "1-5")]
    [InlineData("1-LAST", 5, "1-5")]
    [InlineData("1-NEW", 5, "1-5")]
    [InlineData("2 - LATEST", 5, "2-5")]
    [InlineData("1-2, 4-LATEST", 6, "1-2,4-6")]
    [InlineData(" latest ", 5, "5")]   // 大小写与空白容忍
    [InlineData("1,LATEST,3", 2, "1,2,3")]
    public void ExpandPageAliases_WholeSegmentMatching(string input, int pageCount, string expected)
    {
        // 别名必须全词匹配："LAST" 是 "LATEST" 的前缀，子串替换会把 LATEST
        // 变成 "5EST"（旧实现的 bug）。展开结果应保留其余段原样。
        Assert.Equal(expected, Program.ExpandPageAliases(input, pageCount));
    }

    [Fact]
    public void ExpandPageAliases_RangeAliases_ParsesCorrectly()
    {
        var expanded = Program.ExpandPageAliases("1-LATEST", 5);
        var pages = Program.ParsePageSelection(expanded);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, pages);
    }

    [Fact]
    public void ExpandPageAliases_NonAliasSegments_Untouched()
    {
        // 含别名字样的普通段不能被误替换（如分P 恰好叫 "LAST" 之外的内容）
        Assert.Equal("3,LASTING,2", Program.ExpandPageAliases("3,LASTING,2", 7));
        Assert.Equal("8,PLASTER", Program.ExpandPageAliases("8,PLASTER", 9));
        Assert.Equal("1-PLASTER", Program.ExpandPageAliases("1-PLASTER", 9));
    }
}

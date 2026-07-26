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

    [Fact]
    public void LeadingDash_IsNotTreatedAsRange()
    {
        // "-5" 应当作为一个（后续会被过滤掉的）字面项，而不是空起点的范围
        Assert.Equal(new[] { "-5" }, Program.ParsePageSelection("-5"));
    }
}

namespace BBDown.Tests;

public class ProgramArgumentTests
{
    [Theory]
    [InlineData("-help", "--help")]
    [InlineData("-?", "--help")]
    [InlineData("-version", "--version")]
    public void NormalizeCliArgs_MapsSingleDashAliases(string input, string expected)
    {
        var result = Program.NormalizeCliArgs([input]);

        Assert.Equal([expected], result);
    }

    [Fact]
    public void NormalizeCliArgs_LeavesRegularShortOptionsUnchanged()
    {
        var result = Program.NormalizeCliArgs(["-e", "hevc,avc", "-p", "1"]);

        Assert.Equal(["-e", "hevc,avc", "-p", "1"], result);
    }
}

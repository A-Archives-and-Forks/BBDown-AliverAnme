namespace BBDown.Tests;

/// <summary>
/// 这些数值一路流入下载循环与混流超时，非法值不会当场报错，
/// 而是表现为"没下载任何东西却返回成功""分片切分不收敛"等难以定位的故障。
/// </summary>
public class NumericOptionValidationTests
{
    private static MyOption Valid() => new() { Url = "https://www.bilibili.com/video/BV1qt4y1X7TW" };

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(35792)]     // *60000 后溢出为负数，WaitForExit 会抛异常
    [InlineData(int.MaxValue)]
    public void MuxerTimeout_OutOfRange_Throws(int value)
    {
        var option = Valid();
        option.MuxerTimeout = value;

        var ex = Assert.Throws<ArgumentException>(() => Program.ValidateNumericOptionsForTest(option));
        Assert.Contains("--muxer-timeout", ex.Message);
    }

    [Theory]
    [InlineData(0)]         // while (retry < 0) 一次都不执行，文件从未下载
    [InlineData(-5)]
    public void RetryCount_BelowOne_Throws(int value)
    {
        var option = Valid();
        option.RetryCount = value;

        var ex = Assert.Throws<ArgumentException>(() => Program.ValidateNumericOptionsForTest(option));
        Assert.Contains("--retry-count", ex.Message);
    }

    [Theory]
    [InlineData(0)]         // perSize 为 0 时 fileSize -= 0 永不推进，GetAllClips 不收敛
    [InlineData(-20)]
    public void ThreadSegmentSize_BelowOne_Throws(int value)
    {
        var option = Valid();
        option.ThreadSegmentSize = value;

        var ex = Assert.Throws<ArgumentException>(() => Program.ValidateNumericOptionsForTest(option));
        Assert.Contains("--thread-segment-size", ex.Message);
    }

    [Fact]
    public void RetryDelay_Negative_Throws()
    {
        var option = Valid();
        option.RetryDelay = -1;

        var ex = Assert.Throws<ArgumentException>(() => Program.ValidateNumericOptionsForTest(option));
        Assert.Contains("--retry-delay", ex.Message);
    }

    [Fact]
    public void DelayPerPage_Negative_Throws()
    {
        var option = Valid();
        option.DelayPerPage = -1;

        var ex = Assert.Throws<ArgumentException>(() => Program.ValidateNumericOptionsForTest(option));
        Assert.Contains("--delay-per-page", ex.Message);
    }

    [Fact]
    public void Defaults_AreAccepted()
    {
        Assert.Null(Record.Exception(() => Program.ValidateNumericOptionsForTest(Valid())));
    }

    [Fact]
    public void BoundaryValues_AreAccepted()
    {
        var option = Valid();
        option.MuxerTimeout = 1;
        option.RetryCount = 1;
        option.RetryDelay = 0;
        option.ThreadSegmentSize = 1;
        option.DelayPerPage = 0;

        Assert.Null(Record.Exception(() => Program.ValidateNumericOptionsForTest(option)));
    }
}

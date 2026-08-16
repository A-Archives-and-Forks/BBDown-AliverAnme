using BBDown.Core.Util;

namespace BBDown.Tests;

public class BilibiliBvConverterTests
{
    [Theory]
    [InlineData(170001, "BV17x411w7KC")]
    [InlineData(455017605, "BV1Q541167Qg")]
    [InlineData(882584971, "BV1mK4y1C7Bz")]
    public void Encode_ReturnsExpectedBv(long aid, string expectedBv)
    {
        var result = BilibiliBvConverter.Encode(aid);
        Assert.Equal(expectedBv, result);
    }

    [Theory]
    [InlineData("7x411w7KC", 170001)]
    [InlineData("Q541167Qg", 455017605)]
    [InlineData("mK4y1C7Bz", 882584971)]
    public void Decode_ReturnsExpectedAid(string bvSuffix, long expectedAid)
    {
        var result = BilibiliBvConverter.Decode(bvSuffix);
        Assert.Equal(expectedAid, result);
    }

    [Fact]
    public void Encode_TooSmallAid_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BilibiliBvConverter.Encode(0));
    }

    [Fact]
    public void Decode_WrongLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => BilibiliBvConverter.Decode("short"));
    }

    [Fact]
    public void Decode_InvalidChar_ThrowsArgumentException()
    {
        // '0', 'l', 'I', 'O' 等字符不在 Base58 字母表中
        Assert.Throws<ArgumentException>(() => BilibiliBvConverter.Decode("0x411w7KC"));
    }
}

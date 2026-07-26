using System.Security.Cryptography;
using BBDown.Core.DRM;

namespace BBDown.Tests;

/// <summary>
/// AES-CMAC 是自行实现的密码学原语，Widevine 的会话密钥派生完全建立在它之上：
/// 一旦有偏差，派生出的 encKey / macKey 全错，DRM 解密只会以难以定位的方式失败。
/// 这里用 RFC 4493 附录的官方测试向量锁定行为。
/// </summary>
public class WidevineCryptoTests
{
    // RFC 4493 全部示例共用的密钥
    private static readonly byte[] Key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");

    [Theory]
    // RFC 4493 Example 1: len = 0
    [InlineData("", "bb1d6929e95937287fa37d129b756746")]
    // Example 2: len = 16（恰好一个完整块，走 K1 分支）
    [InlineData("6bc1bee22e409f96e93d7e117393172a", "070a16b46b4d4144f79bdd9dd04a287c")]
    // Example 3: len = 40（末块不完整，走 K2 + 0x80 填充分支）
    [InlineData("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411",
                "dfa66747de9ae63030ca32611497c827")]
    // Example 4: len = 64（多块且末块完整）
    [InlineData("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710",
                "51f0bebf7e3b9d92fc49741779363cfe")]
    public void AesCmac_MatchesRfc4493Vectors(string messageHex, string expectedHex)
    {
        var message = messageHex.Length == 0 ? [] : Convert.FromHexString(messageHex);

        var mac = WidevineCrypto.AesCmac(Key, message);

        Assert.Equal(expectedHex, Convert.ToHexStringLower(mac));
    }

    [Fact]
    public void AesEcbEncrypt_MatchesRfc4493SubkeyBase()
    {
        // RFC 4493 子密钥推导的第一步：AES-128(K, 0^128)
        var l = WidevineCrypto.AesEcbEncrypt(new byte[16], Key);

        Assert.Equal("7df76b0c1ab899b33e42f047b91b546f", Convert.ToHexStringLower(l));
    }

    [Fact]
    public void AesEcbEncrypt_RejectsNonBlockSizedInput()
    {
        Assert.Throws<ArgumentException>(() => WidevineCrypto.AesEcbEncrypt(new byte[15], Key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    public void Pkcs7_RoundTrips(int length)
    {
        var data = new byte[length];
        RandomNumberGenerator.Fill(data);

        var padded = WidevineCrypto.Pkcs7Pad(data, 16);

        // 满块也必须补一整块，否则解填充无法区分数据与填充
        Assert.Equal(0, padded.Length % 16);
        Assert.True(padded.Length > length);
        Assert.Equal(data, WidevineCrypto.Pkcs7Unpad(padded));
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x02, 0x03 })]      // 填充长度与实际不符
    [InlineData(new byte[] { 0x00 })]                   // 填充长度为 0
    public void Pkcs7Unpad_RejectsMalformedPadding(byte[] data)
    {
        Assert.Throws<InvalidDataException>(() => WidevineCrypto.Pkcs7Unpad(data));
    }

    [Fact]
    public void Pkcs7Unpad_RejectsEmptyInput()
    {
        Assert.Throws<InvalidDataException>(() => WidevineCrypto.Pkcs7Unpad([]));
    }

    [Fact]
    public void DeriveContext_EncodesKeySizeInBitsAsBigEndian()
    {
        var (encContext, macContext) = WidevineCrypto.DeriveContext([0xAA, 0xBB]);

        // ENCRYPTION\0 + message + 128（16 字节密钥）
        Assert.Equal("ENCRYPTION\0"u8.ToArray(), encContext[..11]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x80 }, encContext[^4..]);

        // AUTHENTICATION\0 + message + 512（两段 32 字节 MAC 密钥）
        Assert.Equal("AUTHENTICATION\0"u8.ToArray(), macContext[..15]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x02, 0x00 }, macContext[^4..]);
    }

    [Fact]
    public void DeriveKeys_ProducesDistinctKeysOfExpectedLength()
    {
        var sessionKey = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        var (encContext, macContext) = WidevineCrypto.DeriveContext([0x01, 0x02, 0x03]);

        var (encKey, macServer, macClient) = WidevineCrypto.DeriveKeys(sessionKey, encContext, macContext);

        Assert.Equal(16, encKey.Length);
        Assert.Equal(32, macServer.Length);   // counter 1 || counter 2
        Assert.Equal(32, macClient.Length);   // counter 3 || counter 4
        Assert.NotEqual(macServer, macClient);
    }

    [Fact]
    public void DeriveKeys_IsDeterministic()
    {
        var sessionKey = Convert.FromHexString("0f0e0d0c0b0a09080706050403020100");
        var (enc, mac) = WidevineCrypto.DeriveContext([0x42]);

        var first = WidevineCrypto.DeriveKeys(sessionKey, enc, mac);
        var second = WidevineCrypto.DeriveKeys(sessionKey, enc, mac);

        Assert.Equal(first.encKey, second.encKey);
        Assert.Equal(first.macKeyServer, second.macKeyServer);
        Assert.Equal(first.macKeyClient, second.macKeyClient);
    }
}

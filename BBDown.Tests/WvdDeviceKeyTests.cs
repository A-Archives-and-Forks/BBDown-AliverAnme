using System.Security.Cryptography;
using BBDown.Core.DRM;

namespace BBDown.Tests;

/// <summary>
/// .wvd 设备文件里的 RSA 私钥格式。规范的 pywidevine device 把私钥存为
/// PKCS#1 DER 二进制，原实现只认 PEM 文本，导致所有标准 .wvd 加载失败。
/// 这里用真实生成的密钥覆盖三种编码，确保导入逻辑都能吃下。
/// </summary>
public class WvdDeviceKeyTests
{
    /// <summary>导入后应能拿到与原密钥一致的公钥参数，以此确认导入成功且正确。</summary>
    private static void AssertImports(byte[] keyBytes, RSAParameters expectedPublic)
    {
        using var rsa = RSA.Create();
        WvdDevice.ImportPrivateKey(rsa, keyBytes);
        var got = rsa.ExportParameters(false);
        Assert.Equal(expectedPublic.Modulus, got.Modulus);
        Assert.Equal(expectedPublic.Exponent, got.Exponent);
    }

    [Fact]
    public void ImportsPkcs1DerKey()
    {
        // pywidevine Device.dumps() 用的正是 PKCS#1 DER
        using var key = RSA.Create(2048);
        var expected = key.ExportParameters(false);
        AssertImports(key.ExportRSAPrivateKey(), expected);
    }

    [Fact]
    public void ImportsPkcs8DerKey()
    {
        using var key = RSA.Create(2048);
        var expected = key.ExportParameters(false);
        AssertImports(key.ExportPkcs8PrivateKey(), expected);
    }

    [Fact]
    public void ImportsPemKey()
    {
        // 向后兼容：仓库内置的非规范 device.wvd 存的是 PEM 文本
        using var key = RSA.Create(2048);
        var expected = key.ExportParameters(false);
        var pem = System.Text.Encoding.ASCII.GetBytes(key.ExportRSAPrivateKeyPem());
        AssertImports(pem, expected);
    }

    [Fact]
    public void ImportsHeaderlessPemBody()
    {
        // 缺头尾的 PEM 主体（原实现的兜底路径）仍需可用
        using var key = RSA.Create(2048);
        var expected = key.ExportParameters(false);
        var pem = key.ExportRSAPrivateKeyPem();
        var body = pem
            .Replace("-----BEGIN RSA PRIVATE KEY-----", "")
            .Replace("-----END RSA PRIVATE KEY-----", "")
            .Trim();
        AssertImports(System.Text.Encoding.ASCII.GetBytes(body), expected);
    }

    [Fact]
    public void ImportPrivateKey_Garbage_Throws()
    {
        // 垃圾输入必须抛异常，触发上层 Create 的 catch-dispose 释放 RSA 句柄
        using var rsa = RSA.Create();
        Assert.ThrowsAny<Exception>(() => WvdDevice.ImportPrivateKey(rsa, new byte[] { 0x01, 0x02, 0x03 }));
    }

    [Fact]
    public void Load_CorruptWvd_ThrowsInsteadOfLeaking()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-bad-{Guid.NewGuid():N}.wvd");
        File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });
        try
        {
            Assert.ThrowsAny<Exception>(() => WvdDevice.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 无 WVD magic 的标准 v2 .wvd（首字节 = version = 2）必须能加载。格式探测此前
    /// 只认首字节 == 1，而 ParseWvd 明明支持 v2，无 magic 的 v2 文件会被误抛
    /// "无法识别的 WVD 文件格式 (首字节: 2)"。
    /// </summary>
    [Fact]
    public void Load_NoMagicV2Wvd_Loads()
    {
        using var key = RSA.Create(2048);
        var privateKey = key.ExportRSAPrivateKey(); // PKCS#1 DER
        // 合法 protobuf（field 1 = type = DrmDeviceCertificate），Create 也能兼容原始字节
        var clientId = new byte[] { 0x08, 0x01 };
        var wvd = new List<byte> { 2, 1, 3, 0 }; // version=2, type, security_level, flags=0(未加密)
        wvd.Add((byte)(privateKey.Length >> 8));
        wvd.Add((byte)(privateKey.Length & 0xFF));
        wvd.AddRange(privateKey);
        wvd.Add((byte)(clientId.Length >> 8));
        wvd.Add((byte)(clientId.Length & 0xFF));
        wvd.AddRange(clientId);

        var path = Path.Combine(Path.GetTempPath(), $"bbdown-v2-{Guid.NewGuid():N}.wvd");
        File.WriteAllBytes(path, wvd.ToArray());
        try
        {
            using var device = WvdDevice.Load(path);
            Assert.NotNull(device.ClientIdentification);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 加密 v2（flags 第 0 位 = 1，私钥被 AES 加密）必须被 ParseWvd 显式拒绝，
    /// 探测放宽到首字节 2 后不得把加密 v2 误放行。
    /// </summary>
    [Fact]
    public void Load_EncryptedV2Wvd_ThrowsNotSupported()
    {
        using var key = RSA.Create(2048);
        var privateKey = key.ExportRSAPrivateKey();
        var clientId = new byte[] { 0x08, 0x01 };
        var wvd = new List<byte> { 2, 1, 3, 0x01 }; // flags bit0=1 → 私钥加密
        wvd.Add((byte)(privateKey.Length >> 8));
        wvd.Add((byte)(privateKey.Length & 0xFF));
        wvd.AddRange(privateKey);
        wvd.Add((byte)(clientId.Length >> 8));
        wvd.Add((byte)(clientId.Length & 0xFF));
        wvd.AddRange(clientId);

        var path = Path.Combine(Path.GetTempPath(), $"bbdown-v2-enc-{Guid.NewGuid():N}.wvd");
        File.WriteAllBytes(path, wvd.ToArray());
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => WvdDevice.Load(path));
            Assert.Contains("Encrypted", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_TruncatedWvd_ThrowsInvalidDataException()
    {
        // 头部声明私钥长度 100 字节，但实际数据只有 10 字节截断数据
        var truncated = new byte[] { 0x57, 0x56, 0x44, 0x01, 0x01, 0x03, 0x00, 0x00, 0x64, 0x01, 0x02 };
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-trunc-{Guid.NewGuid():N}.wvd");
        File.WriteAllBytes(path, truncated);
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => WvdDevice.Load(path));
            Assert.Contains("超出数据范围", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

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
}

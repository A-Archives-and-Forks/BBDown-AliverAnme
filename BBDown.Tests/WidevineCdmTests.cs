using System.Security.Cryptography;
using BBDown.Core.DRM;

namespace BBDown.Tests;

/// <summary>
/// WidevineCdm 许可证流程测试。
/// 由于完整的 CDM 流程依赖真实的 device.wvd 和 Bilibili DRM 服务器，
/// 此测试侧重解析逻辑的正确性和错误路径的优雅降级。
/// </summary>
public class WidevineCdmTests
{
    [Fact]
    public async Task GetKeysAsync_InvalidWvdPath_ReturnsNullAndLogs()
    {
        // 不存在的 wvd 文件 → 应返回 null 而非抛异常
        var result = await WidevineCdm.GetKeysAsync("CAESEDE=", "/nonexistent/path/device.wvd");
        Assert.Null(result);
    }

    /// <summary>
    /// 构造一个格式合法的最小 v1 .wvd 文件（随机 RSA 私钥 + 占位 client_id）。
    /// 让非法 PSSH 用例真正触达 ParsePsshBox，而不是在 WvdDevice.Load 处提前返回。
    /// </summary>
    private static string CreateValidWvdPath()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportRSAPrivateKey(); // PKCS#1 DER
        var clientId = new byte[] { 0x01, 0x02, 0x03 }; // 非 protobuf，Create 会构建最小 ClientIdentification

        // 格式: version(1) type(0) securityLevel(1) flags(0) keyLen(2) key... clientIdLen(2) clientId...
        var wvd = new List<byte> { 0x01, 0x00, 0x01, 0x00 };
        wvd.Add((byte)(privateKey.Length >> 8));
        wvd.Add((byte)(privateKey.Length & 0xFF));
        wvd.AddRange(privateKey);
        wvd.Add((byte)(clientId.Length >> 8));
        wvd.Add((byte)(clientId.Length & 0xFF));
        wvd.AddRange(clientId);

        var path = Path.Combine(Path.GetTempPath(), $"bbdown-wvd-{Guid.NewGuid():N}.wvd");
        File.WriteAllBytes(path, wvd.ToArray());
        return path;
    }

    [Theory]
    [InlineData("")]                           // 空字符串
    [InlineData("invalid!!!")]                 // 非法的 base64
    [InlineData("AAAA")]                       // 太短，不足 28 字节
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // 恰好 32 字节但系统 ID 不对
    public async Task GetKeysAsync_InvalidPssh_ReturnsNull(string pssh)
    {
        // 所有非法 PSSH 应优雅返回 null。wvd 必须合法：
        // 此前用不存在的路径，测试在 WvdDevice.Load 就返回 null，
        // 永远走不到 ParsePsshBox，无论解析逻辑多坏都测不出来。
        var wvdPath = CreateValidWvdPath();
        // 先独立验证 wvd 可加载，确保走到 ParsePsshBox 而非在 Load 处提前返回；
        // 若 CreateValidWvdPath 被改坏，这里抛异常使测试失败而非静默通过。
        using (var _ = WvdDevice.Load(wvdPath)) { }
        try
        {
            var result = await WidevineCdm.GetKeysAsync(pssh, wvdPath);
            Assert.Null(result);
        }
        finally
        {
            File.Delete(wvdPath);
        }
    }
}

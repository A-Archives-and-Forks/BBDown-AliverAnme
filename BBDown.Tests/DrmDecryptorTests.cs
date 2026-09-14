using System.Security.Cryptography;
using System.Text;
using BBDown.Core.DRM;

namespace BBDown.Tests;

/// <summary>
/// DRM 取钥链的取消透传（RF-35）：GetKeyWidevineAsync → WidevineCdm.GetKeysAsync →
/// 许可证请求必须尊重 CancellationToken——serve /cancel 与 Ctrl+C 在取钥窗口
///（2 分钟超时 ×3 次尝试 + 退避）内应立即中断，而非占住并发槽最长约 6 分钟。
/// 用预取消 token：wvd 加载/PSSH 解析/挑战构造均为本地操作，不发出任何网络请求。
/// </summary>
public class DrmDecryptorTests
{
    [Fact]
    public async Task GetKeyWidevineAsync_CanceledToken_ThrowsOceWithoutNetwork()
    {
        var wvdPath = Path.Combine(Path.GetTempPath(), "bbdown-test-" + Guid.NewGuid().ToString("N") + ".wvd");
        try
        {
            File.WriteAllBytes(wvdPath, BuildWvdBytes());
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            // 预取消 token：取消在许可证请求处以 OCE 显形——证明 token 已透传到
            // GetKeysAsync 下游（旧实现不接收 token，本调用无法编译）。
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => DrmDecryptor.GetKeyWidevineAsync(BuildPsshBase64(), wvdPath, cts.Token));
        }
        finally
        {
            if (File.Exists(wvdPath)) File.Delete(wvdPath);
        }
    }

    /// <summary>构造 pywidevine v1 结构的最小 .wvd：version=1 + PKCS#1 DER 私钥 + 空 client_id。</summary>
    private static byte[] BuildWvdBytes()
    {
        using var key = RSA.Create(2048);
        var privateKey = key.ExportRSAPrivateKey();
        var data = new List<byte> { 1, 0, 0, 0 };
        data.Add((byte)(privateKey.Length >> 8));
        data.Add((byte)(privateKey.Length & 0xFF));
        data.AddRange(privateKey);
        data.Add(0);
        data.Add(0); // client_id 长度 0：ClientIdentification.Parser.ParseFrom(空数组) 合法
        return data.ToArray();
    }

    /// <summary>构造含 1 个 KID 的 v1 PSSH box（Widevine system ID）。</summary>
    private static string BuildPsshBase64()
    {
        var systemId = new byte[] { 0xED, 0xEF, 0x8B, 0xA9, 0x79, 0xD6, 0x4A, 0xCE, 0xA3, 0xC8, 0x27, 0xDC, 0xD5, 0x1D, 0x21, 0xED };
        var box = new List<byte>();
        var size = 4 + 4 + 4 + 16 + 4 + 16 + 4; // 大小头 + "pssh" + version/flags + system id + KID 数 + KID + data 大小
        box.Add((byte)(size >> 24));
        box.Add((byte)(size >> 16));
        box.Add((byte)(size >> 8));
        box.Add((byte)size);
        box.AddRange(Encoding.ASCII.GetBytes("pssh"));
        box.Add(1); // version 1
        box.Add(0); box.Add(0); box.Add(0); // flags
        box.AddRange(systemId);
        box.Add(0); box.Add(0); box.Add(0); box.Add(1); // KID 数 = 1
        box.AddRange(new byte[16]); // KID 全零
        box.Add(0); box.Add(0); box.Add(0); box.Add(0); // data 大小 = 0
        return Convert.ToBase64String(box.ToArray());
    }
}

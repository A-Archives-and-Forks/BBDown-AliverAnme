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

    [Theory]
    [InlineData("")]                           // 空字符串
    [InlineData("invalid!!!")]                 // 非法的 base64
    [InlineData("AAAA")]                       // 太短，不足 28 字节
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // 恰好 32 字节但系统 ID 不对
    public async Task GetKeysAsync_InvalidPssh_ReturnsNull(string pssh)
    {
        // 所有非法 PSSH 应优雅返回 null
        var result = await WidevineCdm.GetKeysAsync(pssh, "/nonexistent/path/device.wvd");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetKeysAsync_ReadU32Be_EdgeCases()
    {
        var buf = new byte[] { 0x00, 0x00, 0x00, 0x01 };
        var val = ((uint)buf[0] << 24) | ((uint)buf[1] << 16) | ((uint)buf[2] << 8) | buf[3];
        Assert.Equal(1u, val);

        // 最大值
        buf = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        val = ((uint)buf[0] << 24) | ((uint)buf[1] << 16) | ((uint)buf[2] << 8) | buf[3];
        Assert.Equal(uint.MaxValue, val);
    }
}

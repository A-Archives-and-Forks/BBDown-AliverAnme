using System.IO.Compression;
using BBDown.Core;

namespace BBDown.Tests;

/// <summary>
/// gRPC 帧解析健壮性测试（B3-S1/S2）：帧首字节校验防御畸形响应、gzip 解压上限防解压炸弹。
/// </summary>
public class AppHelperMessageTests
{
    [Fact]
    public void ReadMessage_ValidUncompressed_RoundTrips()
    {
        // 未压缩帧：首字节 0 + 4 字节大端长度 + 载荷
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var frame = new byte[payload.Length + 5];
        frame[0] = 0;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length);
        payload.CopyTo(frame, 5);

        var result = AppHelper.ReadMessage(frame);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void ReadMessage_GzipCompressed_RoundTrips()
    {
        // gzip 帧：首字节 1 + gzip(bytes)
        var payload = new byte[1000];
        new Random(42).NextBytes(payload);
        var frame = AppHelper.PackMessage(payload); // PackMessage 即 gzip 压缩 + 帧封装

        var result = AppHelper.ReadMessage(frame);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void ReadMessage_InvalidCompressionFlag_Throws()
    {
        // B3-S2：gRPC 帧首字节合法值只有 0/1；2 及以上是畸形帧（或被破坏的响应），
        // 必须显式报错而非静默当作未压缩解析（后者会产出误导性的反序列化错误）。
        var frame = new byte[10]; // 首字节 0x02，后随任意字节
        frame[0] = 0x02;
        var ex = Assert.Throws<InvalidDataException>(() => AppHelper.ReadMessage(frame));
        Assert.Contains("compression flag", ex.Message);
    }

    [Fact]
    public void ReadMessage_TooShort_Throws()
    {
        Assert.Throws<InvalidDataException>(() => AppHelper.ReadMessage(new byte[4])); // 帧头 5 字节
    }

    [Fact]
    public void GzipDecompress_ExceedingLimit_Rejected()
    {
        // B3-S1：小压缩包膨胀为超大输出的解压炸弹必须被上限拦截。
        // 构造约 60MB 的重复数据压缩后放行（远大于 ReadMessage 隐含载荷，走上限路径）。
        // ReadMessage 中 gzip 分支的输出上限 48MB：压缩 64MB 全零数据 → 解压后必然超限。
        var big = new byte[64 * 1024 * 1024];
        var frame = AppHelper.PackMessage(big);
        // PackMessage 内部先压缩：确认压缩帧确实远小于原始（否则走不到解压上限即内存已爆）
        Assert.True(frame.Length < big.Length / 10, $"gzip 应显著压缩可压缩数据: {frame.Length} vs {big.Length}");

        var ex = Assert.Throws<InvalidDataException>(() => AppHelper.ReadMessage(frame));
        Assert.Contains("解压", ex.Message);
    }
}

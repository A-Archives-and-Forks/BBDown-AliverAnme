using System.Threading;
namespace BBDown.Core.DRM;

public static class DrmDecryptor
{
    /// <param name="token">取消令牌，全程透传至许可证请求链路（RF-35）：原实现在此断链，
    /// Ctrl+C / serve 的 /cancel 在取钥窗口（2 分钟超时 ×3 次尝试 + 退避）内不可中断，
    /// 最长约 6 分钟占住并发槽。下游 GetKeysAsync 的取消分类（OCE 重抛）由此可达。</param>
    public static async Task<(string kid, string keyHex)?> GetKeyWidevineAsync(string psshB64, string wvdPath, CancellationToken token = default)
    {
        if (!File.Exists(wvdPath))
        {
            Logger.LogWarn($"device.wvd 未找到: {wvdPath}");
            return null;
        }

        var keys = await WidevineCdm.GetKeysAsync(psshB64, wvdPath, token);
        if (keys == null || keys.Length == 0)
            return null;

        var (kid, key) = keys[0];
        // 密钥是内容解密凭据：只记 kid 与长度，绝不落盘 key 材料——原实现截取前 8 个
        // 十六进制字符会把 AES-128 密钥的 25% 材料写进持久日志文件，泄露即密钥失守。
        Logger.LogDebug("Widevine key: kid={0}, keyLength={1} hex chars", kid, key.Length);
        return (kid, key);
    }
}

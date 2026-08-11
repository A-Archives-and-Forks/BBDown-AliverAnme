using System.Text.Json;

namespace BBDown.Core.Util;

/// <summary>
/// 负责为请求补上 buvid3 设备标识。
/// </summary>
public static class BuvidProvider
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 确保 Cookie 中带有 buvid3。返回注入后的完整 Cookie；无需注入/获取失败返回 null。
    /// 注意：不在此处写 Config——AsyncLocal 写入发生在子方法内不会回流父调用方
    /// （父流程的 ExecutionContext 快照在 await 前已捕获）。由调用方拿到返回值后
    /// 在自身流程内显式应用（覆盖其自身请求的凭据读取）。
    /// </summary>
    public static async Task<string?> EnsureAsync(CancellationToken token = default)
    {
        if (HasBuvid3(Config.Current.Cookie)) return null;

        await _gate.WaitAsync(token);
        try
        {
            // 并发进入时，先到者可能已经补好了
            if (HasBuvid3(Config.Current.Cookie)) return null;

            var source = await HTTPUtil.GetWebSourceAsync("https://api.bilibili.com/x/frontend/finger/spi", token: token);
            using var doc = JsonDocument.Parse(source);
            var buvid3 = doc.RootElement.GetPropertySafe("data").GetValueAsStringSafe("b_3");
            if (string.IsNullOrEmpty(buvid3))
            {
                Logger.LogDebug("spi 接口未返回 b_3，跳过 buvid3 注入");
                return null;
            }

            var cookie = Config.Current.Cookie;
            string updated = string.IsNullOrEmpty(cookie)
                ? $"buvid3={buvid3}"
                : $"{cookie.TrimEnd(';')};buvid3={buvid3}";
            Logger.LogDebug("已获取 buvid3: {0}", buvid3);
            return updated;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Logger.LogDebug("获取 buvid3 失败: {0}", ex.Message);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static bool HasBuvid3(string? cookie)
        => !string.IsNullOrEmpty(cookie)
           && cookie.Contains("buvid3=", StringComparison.OrdinalIgnoreCase);
}

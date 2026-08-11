using System.Text.Json;

namespace BBDown.Core.Util;

/// <summary>
/// 负责为请求补上 buvid3 设备标识。
/// </summary>
public static class BuvidProvider
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 确保 Cookie 中带有 buvid3。
    /// 部分接口（如 UP 主投稿列表 <c>x/space/wbi/arc/search</c>）会对缺少 buvid3 的请求
    /// 直接返回 412，即便 wbi 签名完全正确——未登录时 Cookie 为空，必然触发。
    /// 已存在 buvid3 时不做改动，避免覆盖用户自带的 Cookie。
    /// 取不到也不视为错误：绝大多数接口并不要求 buvid3。
    /// </summary>
    public static async Task EnsureAsync(CancellationToken token = default)
    {
        if (HasBuvid3(Config.Current.Cookie)) return;

        await _gate.WaitAsync(token);
        try
        {
            // 并发进入时，先到者可能已经补好了
            if (HasBuvid3(Config.Current.Cookie)) return;

            var source = await HTTPUtil.GetWebSourceAsync("https://api.bilibili.com/x/frontend/finger/spi", token: token);
            using var doc = JsonDocument.Parse(source);
            var buvid3 = doc.RootElement.GetPropertySafe("data").GetValueAsStringSafe("b_3");
            if (string.IsNullOrEmpty(buvid3))
            {
                Logger.LogDebug("spi 接口未返回 b_3，跳过 buvid3 注入");
                return;
            }

            var cookie = Config.Current.Cookie;
            // 只更新当前异步流的 Cookie：serve 并发任务下写全局会被后写者覆盖，
            // 使其它任务读到被污染后的凭据（跨账号串号）。flow-scoped 改动只对本任务生效。
            Config.COOKIE_FLOW = string.IsNullOrEmpty(cookie)
                ? $"buvid3={buvid3}"
                : $"{cookie.TrimEnd(';')};buvid3={buvid3}";
            Logger.LogDebug("已获取 buvid3: {0}", buvid3);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Logger.LogDebug("获取 buvid3 失败: {0}", ex.Message);
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

using System.Net.Http.Headers;
using System.Text;

namespace BBDown.Core.Util;

/// <summary>
/// 对日志中出现的凭据做脱敏处理。
/// 保留首尾各 4 个字符便于用户核对是哪一份凭据，中间部分不可恢复。
/// </summary>
public static class SensitiveDataMasker
{
    /// <summary>URL query 与 Cookie 中需要脱敏的键名（不区分大小写）。</summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "access_key", "access_token", "refresh_token", "token",
        "SESSDATA", "bili_jct", "DedeUserID__ckMd5", "sid",
    };

    /// <summary>值为 <c>k=v; k=v</c> 结构、需要逐项脱敏的请求头。</summary>
    private static readonly HashSet<string> CookieHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cookie", "Set-Cookie",
    };

    /// <summary>值本身即凭据、需要整体脱敏的请求头。</summary>
    private static readonly HashSet<string> OpaqueSecretHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "X-Bili-Access-Key",
    };

    private const string Redacted = "***";

    /// <summary>
    /// 脱敏单个凭据值。空值原样返回；不超过 8 个字符的值整体隐藏，避免短 token 被反推。
    /// </summary>
    public static string MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (value.Length <= 8) return Redacted;
        return string.Concat(value.AsSpan(0, 4), Redacted, value.AsSpan(value.Length - 4));
    }

    /// <summary>
    /// 脱敏 URL 中的敏感 query 参数，保留路径与其余参数以便排查问题。
    /// 输入不是合法 URL 时按原样返回，避免掩盖调试信息。
    /// </summary>
    public static string MaskUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url ?? string.Empty;

        var queryStart = url.IndexOf('?');
        if (queryStart < 0) return url;

        var prefix = url[..(queryStart + 1)];
        var query = url[(queryStart + 1)..];

        // 片段标识符不属于 query，需要原样保留
        var fragment = string.Empty;
        var fragmentStart = query.IndexOf('#');
        if (fragmentStart >= 0)
        {
            fragment = query[fragmentStart..];
            query = query[..fragmentStart];
        }

        var parts = query.Split('&');
        for (var i = 0; i < parts.Length; i++)
        {
            var separator = parts[i].IndexOf('=');
            if (separator <= 0) continue;

            var key = parts[i][..separator];
            if (SensitiveKeys.Contains(key))
            {
                parts[i] = string.Concat(key, "=", MaskValue(parts[i][(separator + 1)..]));
            }
        }

        return string.Concat(prefix, string.Join('&', parts), fragment);
    }

    /// <summary>
    /// 脱敏 Cookie 串中的敏感项，形如 <c>SESSDATA=xxx; bili_jct=yyy</c>。
    /// 逐项处理而非整体隐藏，保留非敏感 Cookie 以便排查。
    /// </summary>
    public static string MaskCookie(string? cookie)
    {
        if (string.IsNullOrEmpty(cookie)) return cookie ?? string.Empty;

        var items = cookie.Split(';');
        for (var i = 0; i < items.Length; i++)
        {
            var separator = items[i].IndexOf('=');
            if (separator <= 0) continue;

            var key = items[i][..separator].Trim();
            if (SensitiveKeys.Contains(key))
            {
                var leading = items[i][..(items[i].Length - items[i].TrimStart().Length)];
                items[i] = string.Concat(leading, key, "=", MaskValue(items[i][(separator + 1)..]));
            }
        }

        return string.Join(';', items);
    }

    /// <summary>
    /// 把请求头渲染成脱敏后的字符串，供 <see cref="Logger.LogDebug"/> 使用。
    /// 直接记录 <see cref="HttpHeaders"/> 会把 Cookie 原文写进日志文件。
    /// </summary>
    public static string MaskHeaders(HttpHeaders? headers)
    {
        if (headers is null) return string.Empty;

        var builder = new StringBuilder();
        foreach (var (key, values) in headers)
        {
            var joined = string.Join(", ", values);
            builder.Append(key).Append(": ");
            builder.Append(
                CookieHeaders.Contains(key) ? MaskCookie(joined)
                : OpaqueSecretHeaders.Contains(key) ? MaskValue(joined)
                : joined);
            builder.AppendLine();
        }
        return builder.ToString();
    }
}

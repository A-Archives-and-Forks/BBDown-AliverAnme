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
        "SESSDATA", "bili_jct", "DedeUserID", "DedeUserID__ckMd5", "sid",
        // 带签名媒体 URL（playurl/CDN）的临时授权参数：sign/x_sign/w_rid 是 CDN 下载票据、
        // deadline 是其时效戳。这些 URL 经 AppHelper/Parser 解析后流转到下载器，下载器日志
        // 若明文落盘会绕过前两者的脱敏承诺——DebugLog 下日志含用户可用的临时下载权
        //（含付费/大会员内容），统一纳入脱敏（B3-F1）。marlinToken 是 DRM 内容许可令牌。
        "sign", "x_sign", "w_rid", "deadline", "marlin_token", "marlintoken",
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
        // gRPC 请求头：x-bili-metadata-bin 的 protobuf 里携带 access_token（可逆 base64），
        // 其余 bin 头含 buvid 等设备信息，同样不写入日志。
        "x-bili-metadata-bin", "x-bili-device-bin", "x-bili-network-bin",
        "x-bili-locale-bin", "x-bili-restriction-bin", "x-bili-fawkes-req-bin",
        "x-bili-exps-bin",
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
    /// 脱敏 <c>&lt;scheme&gt; &lt;credentials&gt;</c> 形式的认证头，保留方案名。
    /// 方案名（如 <c>identify_v1</c>、<c>Bearer</c>）本身不是秘密，
    /// 保留它才能在排查时看出用的是哪种鉴权。
    /// </summary>
    private static string MaskAuthorization(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

        var separator = value.IndexOf(' ');
        if (separator <= 0) return MaskValue(value);

        return string.Concat(value[..(separator + 1)], MaskValue(value[(separator + 1)..]));
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
                : OpaqueSecretHeaders.Contains(key) ? MaskAuthorization(joined)
                : joined);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    /// <summary>
    /// 按同一套规则脱敏以字典形式手工组装的请求头。
    /// gRPC 侧的 header 不经由 <see cref="HttpHeaders"/>，
    /// 但同样带有 authorization 之类的凭据。
    /// </summary>
    public static Dictionary<string, string> MaskHeaderMap(IReadOnlyDictionary<string, string>? headers)
    {
        var masked = new Dictionary<string, string>();
        if (headers is null) return masked;

        foreach (var (key, value) in headers)
        {
            masked[key] =
                CookieHeaders.Contains(key) ? MaskCookie(value)
                : OpaqueSecretHeaders.Contains(key) ? MaskAuthorization(value)
                : value;
        }
        return masked;
    }
}

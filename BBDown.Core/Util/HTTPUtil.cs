using System.Net;
using System.Net.Http.Headers;

namespace BBDown.Core.Util;

public static class HTTPUtil
{

    private static HttpClient CreateAppHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = CreateSslOptions(),
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
    }

    // 并发下载与 serve 模式会从多个线程首次访问，Lazy 保证只创建一个 HttpClient，
    // 否则多余实例各自持有一份 SocketsHttpHandler 连接池，造成 socket 泄漏。
    private static readonly Lazy<HttpClient> _appHttpClient =
        new(CreateAppHttpClient, LazyThreadSafetyMode.ExecutionAndPublication);

    public static HttpClient AppHttpClient => _appHttpClient.Value;

    /// <summary>
    /// 禁自动跳转的共享客户端：供逐跳校验重定向（GetWebLocationCheckedAsync /
    /// GetWebSourceAnonymousCheckedAsync）复用。手动逐跳跟随需要 AllowAutoRedirect=false，
    /// 每次跳转新建 HttpClient 会重复建连接池；共享实例避免 socket 泄漏与握手开销。
    /// 与 AppHttpClient 相同的 SSL 校验策略（读取当前流配置的 SkipSslCheck）。
    /// </summary>
    private static readonly Lazy<HttpClient> _noRedirectClient = new(() =>
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = CreateSslOptions(),
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(1) };
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private static System.Net.Security.SslClientAuthenticationOptions CreateSslOptions()
    {
        if (Config.Current.SkipSslCheck)
            Logger.LogDebug("SSL 证书验证已禁用");
        return new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
            {
                if (Config.Current.SkipSslCheck)
                {
                    if (errors != System.Net.Security.SslPolicyErrors.None)
                        Logger.LogDebug("SSL 证书验证被跳过，证书错误: {0}", errors);
                    return true;
                }
                return errors == System.Net.Security.SslPolicyErrors.None;
            },
        };
    }

    private static readonly string[] platforms = { "Windows NT 10.0; Win64", "Macintosh; Intel Mac OS X 10_15", "X11; Linux x86_64" };

    private static string RandomVersion(int min, int max)
    {
        double version = Random.Shared.NextDouble() * (max - min) + min;
        return version.ToString("F3");
    }

    private static string GetRandomUserAgent()
    {
        string[] browsers = { $"AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{RandomVersion(80, 110)} Safari/537.36", $"Gecko/20100101 Firefox/{RandomVersion(80, 110)}" };
        return $"Mozilla/5.0 ({platforms[Random.Shared.Next(platforms.Length)]}) {browsers[Random.Shared.Next(browsers.Length)]}";
    }

    public static string UserAgent { get; set; } = GetRandomUserAgent();

    public static async Task<string> GetWebSourceAsync(string url, string? userAgent = null, CancellationToken token = default)
        => await GetWebSourceCoreAsync(url, sendCookie: true, userAgent: userAgent, token: token);

    /// <summary>
    /// 获取网页内容并透出 Set-Cookie 响应头。仅用于需要登录凭据的接口：
    /// B 站新版扫码登录（2026）将 SESSDATA/bili_jct/DedeUserID 等凭证经
    /// poll 响应的 Set-Cookie（HttpOnly）下发，data.url 仅剩 crossDomain 跳转参数，
    /// 只返回 body 会永久丢失登录凭证。
    /// </summary>
    public static async Task<(string Body, List<string> SetCookies)> GetWebSourceWithSetCookiesAsync(
        string url, string? userAgent = null, CancellationToken token = default)
    {
        using var webRequest = new HttpRequestMessage(HttpMethod.Get, url);
        webRequest.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? UserAgent);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        if (!string.IsNullOrEmpty(Config.Current.Cookie))
            webRequest.Headers.TryAddWithoutValidation("Cookie", Config.Current.Cookie);
        webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        webRequest.Headers.Connection.Clear();

        Logger.LogDebug("获取网页内容: Url: {0}", SensitiveDataMasker.MaskUrl(url));
        using var webResponse = (await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, token)).EnsureSuccessStatusCode();

        string htmlCode = await webResponse.Content.ReadAsStringAsync(token);
        Logger.LogDebug("Response: {0}", htmlCode.Length > 1024 ? htmlCode[..1024] + $"…[截断, 共 {htmlCode.Length} 字符]" : htmlCode);
        List<string> setCookies = webResponse.Headers.TryGetValues("Set-Cookie", out var vals) ? vals.ToList() : [];
        return (htmlCode, setCookies);
    }

    /// <summary>
    /// 匿名抓取网页内容：不携带登录 Cookie。仅用于解析阶段抓取尚未确认可信的
    /// 用户输入 URL（如 ResolveAsync 的泛抓取分支）——此时目标域名可能由攻击者控制，
    /// 附带操作者的 B 站凭据会把它外发到攻击者服务器（SSRF + 凭据泄露）。
    /// 已验证可信的 B 站 API 调用仍走 <see cref="GetWebSourceAsync"/>（携带 Cookie）。
    /// </summary>
    public static async Task<string> GetWebSourceAnonymousAsync(string url, string? userAgent = null, CancellationToken token = default)
        => await GetWebSourceCoreAsync(url, sendCookie: false, userAgent: userAgent, token: token);

    /// <summary>
    /// 逐跳校验重定向的匿名抓取：不携带 Cookie，且每一跳的 Location 都在发起下一跳
    /// 请求前交给 <paramref name="validateNextHop"/> 校验。与
    /// <see cref="GetWebSourceAnonymousAsync"/>（走自动跳转的共享客户端）不同，
    /// 初始域名可信不代表重定向目标可信——可信域名的开放重定向仍可把请求导向内网。
    /// </summary>
    public static async Task<string> GetWebSourceAnonymousCheckedAsync(string url,
        Func<Uri, bool> validateNextHop, string? userAgent = null, int maxHops = 10, CancellationToken token = default)
    {
        string current = url;
        for (int hop = 0; hop < maxHops; hop++)
        {
            using var webRequest = new HttpRequestMessage(HttpMethod.Get, current);
            webRequest.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? UserAgent);
            webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
            webRequest.Headers.Connection.Clear();

            using var response = await _noRedirectClient.Value.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null) return current;
                var next = location.IsAbsoluteUri ? location : new Uri(new Uri(current), location);
                // 发起下一跳前校验目标：非可信主机 / 私网地址在此被拦截，不会真正访问
                if (!validateNextHop(next))
                {
                    Logger.LogWarn($"重定向目标未通过校验，已中止: {SensitiveDataMasker.MaskUrl(next.ToString())}");
                    return current;
                }
                current = next.ToString();
                continue;
            }
            string htmlCode = await response.Content.ReadAsStringAsync(token);
            Logger.LogDebug("Response: {0}", htmlCode.Length > 1024 ? htmlCode[..1024] + $"…[截断, 共 {htmlCode.Length} 字符]" : htmlCode);
            return htmlCode;
        }
        return current;
    }

    private static async Task<string> GetWebSourceCoreAsync(string url, bool sendCookie, string? userAgent, CancellationToken token)
    {
        using var webRequest = new HttpRequestMessage(HttpMethod.Get, url);
        webRequest.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? UserAgent);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        if (sendCookie)
            webRequest.Headers.TryAddWithoutValidation("Cookie", (url.Contains("/ep") || url.Contains("/ss")) ? Config.Current.Cookie + ";CURRENT_FNVAL=4048;" : Config.Current.Cookie);
        if (url.Contains("api.bilibili.com"))
            webRequest.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com/");
        if (url.Contains("api.bilibili.tv"))
            webRequest.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        webRequest.Headers.Connection.Clear();

        Logger.LogDebug("获取网页内容: Url: {0}, Headers: {1}",
            SensitiveDataMasker.MaskUrl(url), SensitiveDataMasker.MaskHeaders(webRequest.Headers));
        using var webResponse = (await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, token)).EnsureSuccessStatusCode();

        string htmlCode = await webResponse.Content.ReadAsStringAsync(token);
        // 响应体可达数 MB（如 intl 回退抓取的整张 HTML 页面），翻页类 fetcher 会放大几十倍，
        // 全部落盘会把日志文件灌满；截断到前 1KB 即可排查问题。
        Logger.LogDebug("Response: {0}", htmlCode.Length > 1024 ? htmlCode[..1024] + $"…[截断, 共 {htmlCode.Length} 字符]" : htmlCode);
        return htmlCode;
    }

    // 重写重定向处理, 自动跟随多次重定向
    public static async Task<string> GetWebLocationAsync(string url, CancellationToken token = default)
    {
        // 先尝试 HEAD，部分服务器不支持则 fallback 到 GET
        foreach (var method in new[] { HttpMethod.Head, HttpMethod.Get })
        {
            try
            {
                using var webRequest = new HttpRequestMessage(method, url);
                webRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
                webRequest.Headers.Connection.Clear();

                Logger.LogDebug("获取网页重定向地址(method={1}): Url: {0}", SensitiveDataMasker.MaskUrl(url), method);
                using var webResponse = (await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, token)).EnsureSuccessStatusCode();
                string location = webResponse.RequestMessage?.RequestUri?.AbsoluteUri ?? url;
                Logger.LogDebug("Location: {0}", SensitiveDataMasker.MaskUrl(location));
                return location;
            }
            catch (HttpRequestException) when (method == HttpMethod.Head)
            {
                // HEAD 不被支持，回退到 GET
                Logger.LogDebug("HEAD 请求失败，尝试 GET");
            }
        }
        return url; // fallback: return original URL
    }

    /// <summary>
    /// 逐跳校验重定向的地址解析。共享 <see cref="AppHttpClient"/> 启用了自动跳转
    /// （AllowAutoRedirect=true），请求会先跟随到最终主机才返回——若可信域名存在开放
    /// 重定向，内部地址或非可信目标仍会被先访问到。此方法用禁用自动跳转的专用客户端
    /// 手动逐跳跟随：每一跳的 Location 都在**发起下一跳请求之前**交给
    /// <paramref name="validateNextHop"/> 校验，拒绝即中止（不访问非可信/私网目标）。
    /// 最多跟随 <paramref name="maxHops"/> 次，防重定向环无限跳转。
    /// 每跳先 HEAD，不支持 HEAD 的服务器（405/HttpRequestException）回退到 GET 请求同一 URL。
    /// 返回最终 URL；未被重定向时返回原 URL。
    /// </summary>
    public static async Task<string> GetWebLocationCheckedAsync(string url,
        Func<Uri, bool> validateNextHop, int maxHops = 10, CancellationToken token = default)
    {
        // 复用共享的禁自动跳转客户端：每次跳转新建 HttpClient 会重复建连接池
        string current = url;
        for (int hop = 0; hop < maxHops; hop++)
        {
            var method = HttpMethod.Head;
            bool retryAsGet = false;
            try
            {
                using var webRequest = new HttpRequestMessage(method, current);
                webRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
                webRequest.Headers.Connection.Clear();

                using var response = await _noRedirectClient.Value.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        // 3xx 无 Location：无法继续，按原地址返回
                        return current;
                    }
                    var next = location.IsAbsoluteUri ? location : new Uri(new Uri(current), location);
                    // 发起下一跳前校验目标：非可信主机 / 私网地址在此被拦截，不会真正访问
                    if (!validateNextHop(next))
                    {
                        Logger.LogWarn($"重定向目标未通过校验，已中止: {SensitiveDataMasker.MaskUrl(next.ToString())}");
                        return current;
                    }
                    current = next.ToString();
                    continue;
                }
                if (response.IsSuccessStatusCode)
                {
                    return current;
                }
                // 非 3xx 且非成功（如 HEAD 返回 405/404）：部分服务器不支持 HEAD，回退 GET
                retryAsGet = true;
            }
            catch (HttpRequestException) when (method == HttpMethod.Head)
            {
                // HEAD 不被支持，回退到 GET
                retryAsGet = true;
            }

            if (retryAsGet)
            {
                try
                {
                    using var getRequest = new HttpRequestMessage(HttpMethod.Get, current);
                    getRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    getRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
                    getRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
                    getRequest.Headers.Connection.Clear();

                    using var getResponse = await _noRedirectClient.Value.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, token);
                    if ((int)getResponse.StatusCode is >= 300 and < 400)
                    {
                        var location = getResponse.Headers.Location;
                        if (location is null)
                        {
                            return current;
                        }
                        var next = location.IsAbsoluteUri ? location : new Uri(new Uri(current), location);
                        if (!validateNextHop(next))
                        {
                            Logger.LogWarn($"重定向目标未通过校验，已中止: {SensitiveDataMasker.MaskUrl(next.ToString())}");
                            return current;
                        }
                        current = next.ToString();
                        continue;
                    }
                    return current;
                }
                catch (HttpRequestException)
                {
                    // GET 也失败：按不可达处理，返回当前地址
                    return current;
                }
            }
            return current;
        }
        // 到达跳数上限仍未结束：返回当前地址（调用方据此判断）
        return current;
    }

    public static async Task<byte[]> GetPostResponseAsync(string Url, byte[] postData, Dictionary<string, string>? headers = null, CancellationToken token = default)
    {
        // postData 是 grpc 请求体，可能携带 access_key，只记录长度而非内容
        Logger.LogDebug("Post to: {0}, data: {1} bytes", SensitiveDataMasker.MaskUrl(Url), postData.Length);

        using ByteArrayContent content = new(postData);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/grpc");

        using HttpRequestMessage request = new()
        {
            RequestUri = new Uri(Url),
            Method = HttpMethod.Post,
            Content = content,
            //Version = HttpVersion.Version20
        };

        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        else
        {
            request.Headers.TryAddWithoutValidation("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 6.0.1; oneplus a5010 Build/V417IR) 6.10.0 os/android model/oneplus a5010 mobi_app/android build/6100500 channel/bili innerVer/6100500 osVer/6.0.1 network/2");
            request.Headers.TryAddWithoutValidation("grpc-encoding", "gzip");
        }

        // 不检查状态码会把 4xx/5xx 的错误页当成 grpc 响应体返回，
        // 下游只能报出难以定位的反序列化错误
        using HttpResponseMessage response = (await AppHttpClient.SendAsync(request, token)).EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(token);

        return bytes;
    }
}

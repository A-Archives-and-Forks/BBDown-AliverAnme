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
        };
        if (Config.Current.SkipSslCheck)
        {
            handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (errors != System.Net.Security.SslPolicyErrors.None)
                        Logger.LogDebug("SSL 证书验证被跳过，证书错误: {0}", errors);
                    return true;
                },
            };
            Logger.LogDebug("SSL 证书验证已禁用");
        }
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
    }

    // 并发下载与 serve 模式会从多个线程首次访问，Lazy 保证只创建一个 HttpClient，
    // 否则多余实例各自持有一份 SocketsHttpHandler 连接池，造成 socket 泄漏。
    private static readonly Lazy<HttpClient> _appHttpClient =
        new(CreateAppHttpClient, LazyThreadSafetyMode.ExecutionAndPublication);

    public static HttpClient AppHttpClient => _appHttpClient.Value;

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
    {
        using var webRequest = new HttpRequestMessage(HttpMethod.Get, url);
        webRequest.Headers.TryAddWithoutValidation("User-Agent", userAgent ?? UserAgent);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
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
        Logger.LogDebug("Response: {0}", htmlCode);
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
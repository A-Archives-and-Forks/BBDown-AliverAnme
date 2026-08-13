using QRCoder;
using BBDown.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using BBDown.Core.Util;

namespace BBDown;

internal static class BBDownLoginUtil
{
    /// <summary>
    /// 轮询扫码登录状态，并透出 poll 响应的 Set-Cookie 头。
    /// B 站新版登录（2026）将 SESSDATA 等凭证经 Set-Cookie 下发，必须保留响应头。
    /// </summary>
    public static async Task<(string Body, List<string> SetCookies)> GetLoginStatusAsync(string qrcodeKey, CancellationToken cancellationToken = default)
    {
        string queryUrl = $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={qrcodeKey}&source=main-fe-header";
        return await HTTPUtil.GetWebSourceWithSetCookiesAsync(queryUrl, token: cancellationToken);
    }

    /// <summary>Set-Cookie 头中应丢弃的 cookie 属性名（不是实际 cookie 字段）。</summary>
    private static readonly HashSet<string> CookieAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Path", "Domain", "Expires", "Max-Age", "Secure", "HttpOnly", "SameSite", "Priority", "Partitioned", "Size",
    };

    /// <summary>
    /// 合并扫码登录凭证：url query（旧协议，SESSDATA 等直接放参数）+
    /// Set-Cookie 字段（新协议，HttpOnly 下发）。返回以 ; 连接的 cookie 串。
    /// Set-Cookie 条目取 name=value 部分，丢弃 Path/Domain/Expires 等属性。
    /// </summary>
    internal static string MergeLoginCookies(string urlQuery, IEnumerable<string> setCookies)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in urlQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0) fields[pair[..eq]] = pair[(eq + 1)..];
        }
        foreach (var setCookie in setCookies)
        {
            var kv = setCookie.Split(';')[0].Trim();
            var eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            var name = kv[..eq].Trim();
            // Path=/、Expires=... 等是 cookie 属性而非凭证字段，混入会污染凭据文件
            if (CookieAttributeNames.Contains(name)) continue;
            fields[name] = kv[(eq + 1)..].Trim();
        }
        return string.Join(";", fields.Select(kv => $"{kv.Key}={kv.Value.Replace(",", "%2C")}"));
    }

    /// <summary>
    /// 从 ; 连接的 cookie 串中按名取第一个值（大小写不敏感），无则返回空串。
    /// </summary>
    internal static string GetCookieValue(string cookieString, string name)
    {
        var prefix = name + "=";
        return cookieString.Split(';')
            .FirstOrDefault(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?.Substring(prefix.Length) ?? "";
    }

    public static async Task<bool> LoginWEB(CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.Log("获取登录地址...");
            cancellationToken.ThrowIfCancellationRequested();
            string loginUrl = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate?source=main-fe-header";
            using var loginDoc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(loginUrl, token: cancellationToken));
            string url = loginDoc.RootElement.GetPropertySafe("data").GetStringSafe("url")!;
            string qrcodeKey = BBDownUtil.GetQueryString("qrcode_key", url);
            //Logger.Log(oauthKey);
            //Logger.Log(url);
            bool flag = false;
            Logger.Log("生成二维码...");
            QRCodeGenerator qrGenerator = new();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            PngByteQRCode pngByteCode = new(qrCodeData);
            await File.WriteAllBytesAsync("qrcode.png", pngByteCode.GetGraphic(7));
            Logger.Log("生成二维码成功: qrcode.png, 请打开并扫描, 或扫描打印的二维码");
            var consoleQRCode = new ConsoleQRCode(qrCodeData);
            consoleQRCode.GetGraphic();

            while (true)
            {
                await Task.Delay(1000, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var (w, setCookies) = await GetLoginStatusAsync(qrcodeKey, cancellationToken);
                using var pollDoc = JsonDocument.Parse(w);
                int code = pollDoc.RootElement.GetPropertySafe("data").GetInt32Safe("code");
                if (code == 86038)
                {
                    Logger.LogColor("二维码已过期, 请重新执行登录指令.");
                    return false;
                }
                else if (code == 86101) //等待扫码
                {
                    continue;
                }
                else if (code == 86090) //等待确认
                {
                    if (!flag)
                    {
                        Logger.Log("扫码成功, 请确认...");
                        flag = !flag;
                    }
                }
                else
                {
                    using var successDoc = JsonDocument.Parse(w);
                    string cc = successDoc.RootElement.GetPropertySafe("data").GetStringSafe("url")!;
                    // 导出cookie, 转义英文逗号 否则部分场景会出问题
                    // URL 不含 ? 时 IndexOf 返回 -1，会误把整条 URL 当 cookie 写入凭据文件
                    var queryIdx = cc.IndexOf('?');
                    var cookieQuery = queryIdx >= 0 ? cc[(queryIdx + 1)..] : "";
                    // B 站新版扫码登录：SESSDATA/bili_jct/DedeUserID 等凭证经 poll 响应头
                    // Set-Cookie（HttpOnly）下发，data.url 仅剩 crossDomain 跳转参数。
                    // 合并两类来源，保证新老协议都能写入完整登录态。
                    var merged = MergeLoginCookies(cookieQuery, setCookies);
                    var sessdata = GetCookieValue(merged, "SESSDATA");
                    Logger.Log("登录成功: SESSDATA=" + SensitiveDataMasker.MaskValue(sessdata));
                    if (merged == "" || sessdata == "")
                    {
                        // 防御性检查：url query 总是含 ticket/gourl 等跳转参数，merged 非空
                        // 不代表拿到了有效凭证。B 站新版登录的 SESSDATA 经 Set-Cookie 下发，
                        // 若被中间层剥离/风控拦截，此处应拒绝写入而不是落盘无效 cookie。
                        Logger.LogError("登录成功但未取得有效凭证（SESSDATA 缺失），登录结果未保存；请重试或检查网络环境");
                        return false;
                    }
                    var cookiePath = Path.Combine(Program.APP_DIR, "BBDown.data");
                    // 创建文件时即以 owner 读写权限打开（Unix 上避免先以 umask 默认权限落盘再收紧的两步窗口）
                    var opts = new FileStreamOptions
                    {
                        Mode = FileMode.Create,
                        Access = FileAccess.Write,
                    };
                    if (!OperatingSystem.IsWindows())
                        opts.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    await using (var fs = new FileStream(cookiePath, opts))
                    using (var writer = new StreamWriter(fs))
                        await writer.WriteAsync(merged);
                    SetOwnerOnlyPermission(cookiePath);
                    return true;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            Logger.LogError($"WEB 登录失败: {e.Message}");
            Logger.LogError("请检查网络连接；若二维码已过期请重新执行 BBDown login。");
            return false;
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarn("WEB 登录已取消。");
            return false;
        }
        finally
        {
            CleanUpQrCodeFile();
        }
    }

    public static async Task<bool> LoginTV(CancellationToken cancellationToken = default)
    {
        try
        {
            string loginUrl = "https://passport.snm0516.aisee.tv/x/passport-tv-login/qrcode/auth_code";
            string pollUrl = "https://passport.bilibili.com/x/passport-tv-login/qrcode/poll";
            var parameters = BBDownUtil.GetTVLoginParms();
            Logger.Log("获取登录地址...");
            cancellationToken.ThrowIfCancellationRequested();
            byte[] responseArray = await (await HTTPUtil.AppHttpClient.PostAsync(loginUrl, new FormUrlEncodedContent(parameters.ToDictionary()), cancellationToken)).Content.ReadAsByteArrayAsync(cancellationToken);
            string web = Encoding.UTF8.GetString(responseArray);
            using var authDoc = JsonDocument.Parse(web);
            string url = authDoc.RootElement.GetPropertySafe("data").GetStringSafe("url")!;
            string authCode = authDoc.RootElement.GetPropertySafe("data").GetStringSafe("auth_code")!;
            Logger.Log("生成二维码...");
            QRCodeGenerator qrGenerator = new();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            PngByteQRCode pngByteCode = new(qrCodeData);
            await File.WriteAllBytesAsync("qrcode.png", pngByteCode.GetGraphic(7));
            Logger.Log("生成二维码成功: qrcode.png, 请打开并扫描, 或扫描打印的二维码");
            var consoleQRCode = new ConsoleQRCode(qrCodeData);
            consoleQRCode.GetGraphic();
            parameters.Set("auth_code", authCode);
            parameters.Set("ts", BBDownUtil.GetTimeStamp(true));
            parameters.Remove("sign");
            parameters.Add("sign", BBDownUtil.GetSign(BBDownUtil.ToQueryString(parameters)));
            while (true)
            {
                await Task.Delay(1000, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                responseArray = await (await HTTPUtil.AppHttpClient.PostAsync(pollUrl, new FormUrlEncodedContent(parameters.ToDictionary()), cancellationToken)).Content.ReadAsByteArrayAsync(cancellationToken);
                web = Encoding.UTF8.GetString(responseArray);
                using var pollDoc2 = JsonDocument.Parse(web);
                // 该轮询接口的 code 是 JSON 数字，而 GetStringSafe 只接受字符串类型、
                // 对数字一律返回空串，导致 code 恒为 "" 永远匹配不到 86038/86039，
                // 每次轮询都误入成功分支。GetValueAsStringSafe 用 ToString() 兼容数字与字符串。
                string code = pollDoc2.RootElement.GetValueAsStringSafe("code");
                if (code == "86038")
                {
                    Logger.LogColor("二维码已过期, 请重新执行登录指令.");
                    return false;
                }
                else if (code == "86039") //等待扫码
                {
                    continue;
                }
                else
                {
                    using var successDoc2 = JsonDocument.Parse(web);
                    string cc = successDoc2.RootElement.GetPropertySafe("data").GetStringSafe("access_token")!;
                    Logger.Log("登录成功: AccessToken=" + SensitiveDataMasker.MaskValue(cc));
                    //导出cookie
                    var tvTokenPath = Path.Combine(Program.APP_DIR, "BBDownTV.data");
                    // 与 WEB 登录一致：创建文件时即以 owner 读写权限打开，
                    // 避免先以 umask 默认权限落盘再收紧的两步窗口
                    var tvOpts = new FileStreamOptions
                    {
                        Mode = FileMode.Create,
                        Access = FileAccess.Write,
                    };
                    if (!OperatingSystem.IsWindows())
                        tvOpts.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    await using (var tvFs = new FileStream(tvTokenPath, tvOpts))
                    using (var tvWriter = new StreamWriter(tvFs))
                        await tvWriter.WriteAsync("access_token=" + cc);
                    SetOwnerOnlyPermission(tvTokenPath);
                    return true;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            Logger.LogError($"TV 登录失败: {e.Message}");
            Logger.LogError("请检查网络连接；若二维码已过期请重新执行 BBDown logintv。");
            return false;
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarn("TV 登录已取消。");
            return false;
        }
        finally
        {
            CleanUpQrCodeFile();
        }
    }

    /// <summary>
    /// 删除临时二维码文件。二维码过期、扫码中断或请求异常时同样需要清理，
    /// 否则含登录地址的图片会一直留在工作目录。
    /// </summary>
    private static void CleanUpQrCodeFile()
    {
        try { File.Delete("qrcode.png"); }
        catch (IOException) { /* 文件可能正被看图程序占用 */ }
        catch (UnauthorizedAccessException) { /* 权限不足，留待用户手动清理 */ }
    }

    private static void SetOwnerOnlyPermission(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Unix/macOS: chmod 600 (owner read/write only)
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

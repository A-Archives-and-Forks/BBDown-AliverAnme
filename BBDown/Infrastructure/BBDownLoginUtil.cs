using QRCoder;
using BBDown.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using BBDown.Core.Util;

namespace BBDown;

internal static class BBDownLoginUtil
{
    public static async Task<string> GetLoginStatusAsync(string qrcodeKey)
    {
        string queryUrl = $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={qrcodeKey}&source=main-fe-header";
        return await HTTPUtil.GetWebSourceAsync(queryUrl);
    }

    public static async Task LoginWEB()
    {
        try
        {
            Logger.Log("获取登录地址...");
            string loginUrl = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate?source=main-fe-header";
            using var loginDoc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(loginUrl));
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
                await Task.Delay(1000);
                string w = await GetLoginStatusAsync(qrcodeKey);
                using var pollDoc = JsonDocument.Parse(w);
                int code = pollDoc.RootElement.GetPropertySafe("data").GetInt32Safe("code");
                if (code == 86038)
                {
                    Logger.LogColor("二维码已过期, 请重新执行登录指令.");
                    break;
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
                    Logger.Log("登录成功: SESSDATA=" + SensitiveDataMasker.MaskValue(BBDownUtil.GetQueryString("SESSDATA", cc)));
                    //导出cookie, 转义英文逗号 否则部分场景会出问题
                    var cookiePath = Path.Combine(Program.APP_DIR, "BBDown.data");
                    await File.WriteAllTextAsync(cookiePath, cc[(cc.IndexOf('?') + 1)..].Replace("&", ";").Replace(",", "%2C"));
                    SetOwnerOnlyPermission(cookiePath);
                    break;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            Logger.LogError(e.Message);
        }
        finally
        {
            CleanUpQrCodeFile();
        }
    }

    public static async Task LoginTV()
    {
        try
        {
            string loginUrl = "https://passport.snm0516.aisee.tv/x/passport-tv-login/qrcode/auth_code";
            string pollUrl = "https://passport.bilibili.com/x/passport-tv-login/qrcode/poll";
            var parameters = BBDownUtil.GetTVLoginParms();
            Logger.Log("获取登录地址...");
            byte[] responseArray = await (await HTTPUtil.AppHttpClient.PostAsync(loginUrl, new FormUrlEncodedContent(parameters.ToDictionary()))).Content.ReadAsByteArrayAsync();
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
                await Task.Delay(1000);
                responseArray = await (await HTTPUtil.AppHttpClient.PostAsync(pollUrl, new FormUrlEncodedContent(parameters.ToDictionary()))).Content.ReadAsByteArrayAsync();
                web = Encoding.UTF8.GetString(responseArray);
                using var pollDoc2 = JsonDocument.Parse(web);
                string code = pollDoc2.RootElement.GetStringSafe("code")!;
                if (code == "86038")
                {
                    Logger.LogColor("二维码已过期, 请重新执行登录指令.");
                    break;
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
                    await File.WriteAllTextAsync(tvTokenPath, "access_token=" + cc);
                    SetOwnerOnlyPermission(tvTokenPath);
                    break;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            Logger.LogError(e.Message);
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
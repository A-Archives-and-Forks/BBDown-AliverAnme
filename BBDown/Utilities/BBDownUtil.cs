using System;
using BBDown.Core.Util;
using BBDown.Core;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using static BBDown.Core.Entity.Entity;

namespace BBDown;

public static partial class BBDownUtil
{
    public static async Task CheckUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
            string nowVer = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            string redirectUrl = await HTTPUtil.GetWebLocationAsync("https://github.com/AliverAnme/BBDown/releases/latest", token: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            string latestVer = redirectUrl.Replace("https://github.com/AliverAnme/BBDown/releases/tag/", "");
            if (!nowVer.Equals(latestVer, StringComparison.OrdinalIgnoreCase) && !latestVer.StartsWith("https"))
            {
                Console.Title = $"发现新版本：{latestVer}";
                Logger.LogColor($"发现新版本：{latestVer}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarn($"检查更新失败: {ex.Message}");
            Logger.LogDebug($"检查更新失败详情: {ex}");
        }
    }
    public static Task<string> GetAvIdAsync(string input) => UrlResolver.ResolveAsync(input);


    public static string FormatFileSize(double fileSize)
    {
        return fileSize switch
        {
            < 0 => throw new ArgumentOutOfRangeException(nameof(fileSize)),
            >= 1024 * 1024 * 1024 => $"{fileSize / (1024 * 1024 * 1024):########0.00} GB",
            >= 1024 * 1024 => $"{fileSize / (1024 * 1024):####0.00} MB",
            >= 1024 => $"{fileSize / 1024:####0.00} KB",
            _ => $"{fileSize} bytes"
        };
    }

    public static string FormatTime(int time, bool absolute = false)
    {
        var ts = TimeSpan.FromSeconds(time);
        var totalHours = (int)ts.TotalHours;
        var minutes = ts.Minutes;
        var seconds = ts.Seconds;

        if (absolute)
        {
            return $"{totalHours:D2}:{minutes:D2}:{seconds:D2}";
        }

        return totalHours == 0 ? $"{minutes:D2}m{seconds:D2}s" : $"{totalHours}h{minutes:D2}m{seconds:D2}s";
    }

    public static void CombineMultipleFilesIntoSingleFile(string[] files, string outputFilePath)
    {
        if (!files.Any()) return;
        if (files.Length == 1)
        {
            FileInfo fi = new(files[0]);
            fi.MoveTo(outputFilePath, true);
            return;
        }

        if (!Directory.Exists(Path.GetDirectoryName(outputFilePath)))
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

        string[] inputFilePaths = files;
        using var outputStream = File.Create(outputFilePath);
        foreach (var inputFilePath in inputFilePaths)
        {
            if (inputFilePath == "")
                continue;
            using var inputStream = File.OpenRead(inputFilePath);
            // Buffer size can be passed as the second argument.
            inputStream.CopyTo(outputStream);
            //Console.WriteLine("The file {0} has been processed.", inputFilePath);
        }
        //Global.ExplorerFile(outputFilePath);
    }

    /// <summary>
    /// 寻找指定目录下指定后缀的文件的详细路径 如".txt"
    /// </summary>
    /// <param name="dir"></param>
    /// <param name="ext"></param>
    /// <returns></returns>
    public static string[] GetFiles(string dir, string ext)
    {
        List<string> al = [];
        DirectoryInfo d = new(dir);
        foreach (FileInfo fi in d.GetFiles())
        {
            if (fi.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
            {
                al.Add(fi.FullName);
            }
        }
        string[] res = al.ToArray();
        Array.Sort(res); //排序
        return res;
    }

    public static string GetValidFileName(string input, string re = "_", bool filterSlash = false)
        => BBDown.Core.Util.PathUtil.GetValidFileName(input, re, filterSlash);


    /// <summary>
    /// 获取url字符串参数, 返回参数值字符串
    /// </summary>
    /// <param name="name">参数名称</param>
    /// <param name="url">url字符串</param>
    /// <returns></returns>
    public static string GetQueryString(string name, string url)
    {
        Regex re = QueryRegex();
        MatchCollection mc = re.Matches(url);
        foreach (Match m in mc.Cast<Match>())
        {
            if (m.Result("$2").Equals(name))
            {
                return m.Result("$3");
            }
        }
        return "";
    }

    public static string GetSign(string parameters)
    {
        string toEncode = parameters + "59b43e04ad6965f34319062b478f83dd";
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(toEncode)));
    }

    public static string GetTimeStamp(bool bflag)
    {
        DateTimeOffset ts = DateTimeOffset.Now;
        return (bflag ? ts.ToUnixTimeSeconds() : ts.ToUnixTimeMilliseconds()).ToString();
    }

    //https://stackoverflow.com/questions/1344221/how-can-i-generate-random-alphanumeric-strings
    public static string GetRandomString(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }

    //https://stackoverflow.com/a/45088333
    public static string ToQueryString(NameValueCollection nameValueCollection)
    {
        NameValueCollection httpValueCollection = HttpUtility.ParseQueryString(string.Empty);
        httpValueCollection.Add(nameValueCollection);
        return httpValueCollection.ToString()!;
    }

    public static Dictionary<string, string> ToDictionary(this NameValueCollection nameValueCollection)
    {
        var dict = new Dictionary<string, string>();
        foreach (var key in nameValueCollection.AllKeys)
        {
            dict[key!] = nameValueCollection[key]!;
        }
        return dict;
    }

    public static NameValueCollection GetTVLoginParms()
    {
        NameValueCollection sb = new();
        DateTime now = DateTime.Now;
        string deviceId = GetRandomString(20);
        string buvid = GetRandomString(37);
        string fingerprint = $"{now:yyyyMMddHHmmssfff}{GetRandomString(45)}";
        sb.Add("appkey", "4409e2ce8ffd12b8");
        sb.Add("auth_code", "");
        sb.Add("bili_local_id", deviceId);
        sb.Add("build", "102801");
        sb.Add("buvid", buvid);
        sb.Add("channel", "master");
        sb.Add("device", "OnePlus");
        sb.Add($"device_id", deviceId);
        sb.Add("device_name", "OnePlus7TPro");
        sb.Add("device_platform", "Android10OnePlusHD1910");
        sb.Add($"fingerprint", fingerprint);
        sb.Add($"guid", buvid);
        sb.Add($"local_fingerprint", fingerprint);
        sb.Add($"local_id", buvid);
        sb.Add("mobi_app", "android_tv_yst");
        sb.Add("networkstate", "wifi");
        sb.Add("platform", "android");
        sb.Add("sys_ver", "29");
        sb.Add($"ts", GetTimeStamp(true));
        sb.Add($"sign", GetSign(ToQueryString(sb)));

        return sb;
    }



    /// <summary>
    /// 获取章节信息
    /// </summary>
    /// <param name="cid"></param>
    /// <param name="aid"></param>
    /// <returns></returns>
    public static async Task<List<ViewPoint>> FetchPointsAsync(string cid, string aid, CancellationToken token = default)
    {
        var points = new List<ViewPoint>();
        try
        {
            string api = $"https://api.bilibili.com/x/player/wbi/v2?cid={cid}&aid={aid}";
            string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
            using var infoJson = JsonDocument.Parse(json);
            if (infoJson.RootElement.TryGetPropertySafe("data")?.TryGetProperty("view_points", out JsonElement vPoint) == true)
            {
                foreach (var point in vPoint.EnumerateArray())
                {
                    points.Add(new ViewPoint()
                    {
                        title = point.GetStringSafe("content"),
                        start = point.GetInt32Safe("from"),
                        end = point.GetInt32Safe("to")
                    });
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
        {
            Logger.LogWarn($"获取章节信息失败 (cid={cid}, aid={aid}): {ex.Message}");
        }
        return points;
    }

    /// <summary>
    /// 生成metadata文件, 用于ffmpeg混流章节信息
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    public static string GetFFmpegMetaString(List<ViewPoint> points)
    {
        StringBuilder sb = new();
        sb.AppendLine(";FFMETADATA");
        foreach (var p in points)
        {
            var time = 1000; //固定 1000
            sb.AppendLine("[CHAPTER]");
            sb.AppendLine($"TIMEBASE=1/{time}");
            sb.AppendLine($"START={p.start * time}");
            sb.AppendLine($"END={p.end * time}");
            // 标题含换行可伪造额外章节行（如 "1\n[CHAPTER]..."），来源是 B 站内容，需净化
            sb.AppendLine($"title={p.title.Replace('\n', ' ').Replace('\r', ' ')}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// 生成metadata文件, 用于mp4box混流章节信息
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    public static string GetMp4boxMetaString(List<ViewPoint> points)
    {
        StringBuilder sb = new();
        foreach (var p in points)
        {
            // 标题含换行可注入伪造章节行，来源是 B 站内容，需净化（同 GetFFmpegMetaString）
            sb.AppendLine($"{FormatTime(p.start, true)} {p.title.Replace('\n', ' ').Replace('\r', ' ')}");
        }
        return sb.ToString();
    }



    public static string RSubString(string sub)
    {
        sub = sub[(sub.LastIndexOf('/') + 1)..];
        var lastDot = sub.LastIndexOf('.');
        return lastDot >= 0 ? sub[..lastDot] : sub;
    }

    private static string GetMixinKey(string orig)
    {
        byte[] mixinKeyEncTab =
        [
            46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13
        ];

        var tmp = new StringBuilder(32);
        foreach (var index in mixinKeyEncTab)
        {
            tmp.Append(orig[index]);
        }
        return tmp.ToString();
    }

    public static async Task<(bool isLoggedIn, bool cookieExpired)> CheckLoginWithDetails(string cookie, CancellationToken token = default)
    {
        try
        {
            var api = "https://api.bilibili.com/x/web-interface/nav";
            var source = await HTTPUtil.GetWebSourceAsync(api, token: token);
            using var navDoc = JsonDocument.Parse(source);
            var json = navDoc.RootElement;
            int code = json.GetPropertySafe("code").GetInt32();

            // wbi 密钥必须在判断登录状态之前提取：nav 接口在未登录（code=-101）时
            // 依然会返回 wbi_img，而此前的提前 return 会跳过这一步，
            // 使未登录用户的 Config.WBI 恒为空串、w_rid 恒为无效签名。
            TryUpdateWbiKey(json);

            if (code == -101)
            {
                // nav 对"从未登录"和"cookie 已失效"都返回 -101，接口本身无法区分。
                // 以本地是否真的持有 cookie 来判断，否则从未登录过的新用户
                // 会被告知"Cookie 已过期，请重新扫码"——而调用方那条
                // "你尚未登录"的分支将永远无法触发。
                bool hasCookie = !string.IsNullOrEmpty(cookie);
                Logger.LogDebug(hasCookie
                    ? "Cookie 已过期或无效 (code=-101)"
                    : "尚未登录 (code=-101，本地无 Cookie)");
                return (false, hasCookie);
            }
            var is_login = json.GetPropertySafe("data").GetPropertySafe("isLogin").GetBoolean();
            return (is_login, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Logger.LogDebug("检测登录状态失败: {0}", ex.Message);
            return (false, false);
        }
    }

    /// <summary>
    /// 从 nav 响应中提取 wbi 混淆密钥。缺失时保持原值并记录，
    /// 不因此让登录状态检测失败。
    /// </summary>
    private static void TryUpdateWbiKey(JsonElement navRoot)
    {
        try
        {
            var wbiImg = navRoot.GetPropertySafe("data").GetPropertySafe("wbi_img");
            var imgUrl = wbiImg.GetPropertySafe("img_url").GetString();
            var subUrl = wbiImg.GetPropertySafe("sub_url").GetString();
            if (string.IsNullOrEmpty(imgUrl) || string.IsNullOrEmpty(subUrl))
            {
                Logger.LogDebug("nav 响应中缺少 wbi_img，跳过 wbi 密钥更新");
                return;
            }
            Core.Config.WBI = GetMixinKey(RSubString(imgUrl) + RSubString(subUrl));
            Logger.LogDebug("wbi: {0}", Core.Config.WBI);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            Logger.LogDebug("提取 wbi 密钥失败: {0}", ex.Message);
        }
    }

    public static async Task<bool> CheckLogin(string cookie)
    {
        var (isLoggedIn, _) = await CheckLoginWithDetails(cookie);
        return isLoggedIn;
    }
    [GeneratedRegex("(^|&)?(\\w+)=([^&]+)(&|$)?", RegexOptions.Compiled)]
    private static partial Regex QueryRegex();
    [GeneratedRegex("libavutil\\s+(\\d+)\\. +(\\d+)\\.")]
    internal static partial Regex LibavutilRegex();
}

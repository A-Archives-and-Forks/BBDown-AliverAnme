using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BBDown.Core.Util;

namespace BBDown;

public static partial class UrlResolver
{
    /// <summary>
    /// 解析用户输入（URL、BV号、AV号、EP号等）为统一格式的 avid 标识符。
    /// </summary>
    public static async Task<string> ResolveAsync(string input)
    {
        var avid = input;
        if (input.StartsWith("http"))
        {
            var lowerInput = input.ToLowerInvariant();
            if (input.Contains("b23.tv"))
            {
                string tmp = await HTTPUtil.GetWebLocationAsync(input);
                if (tmp == input) throw new InvalidOperationException("无限重定向");
                input = tmp;
                lowerInput = input.ToLowerInvariant();
            }
            if (lowerInput.Contains("video/av"))
            {
                avid = AvRegex().Match(input).Groups[1].Value;
            }
            else if (lowerInput.Contains("video/bv"))
            {
                avid = DecodeBv(BVRegex().Match(input).Groups[1].Value);
            }
            else if (input.Contains("/cheese/"))
            {
                string epId = "";
                if (input.Contains("/ep"))
                {
                    epId = EpRegex().Match(input).Groups[1].Value;
                }
                else if (input.Contains("/ss"))
                {
                    epId = await GetEpidBySSIdAsync(SsRegex().Match(input).Groups[1].Value);
                }
                avid = $"cheese:{epId}";
            }
            else if (input.Contains("/ep"))
            {
                string epId = EpRegex().Match(input).Groups[1].Value;
                avid = $"ep:{epId}";
            }
            else if (input.Contains("/ss"))
            {
                string epId = await GetEpIdByBangumiSSIdAsync(SsRegex().Match(input).Groups[1].Value);
                avid = $"ep:{epId}";
            }
            else if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_collection")) // 列表类型是合集
            {
                string bizId = BBDownUtil.GetQueryString("business_id", input);
                avid = $"listBizId:{bizId}";
            }
            else if (input.Contains("/medialist/") && input.Contains("business_id=") && input.Contains("business=space_series")) // 列表类型是系列
            {
                string bizId = BBDownUtil.GetQueryString("business_id", input);
                avid = $"seriesBizId:{bizId}";
            }
            else if (input.Contains("/channel/collectiondetail?sid="))
            {
                string bizId = BBDownUtil.GetQueryString("sid", input);
                avid = $"listBizId:{bizId}";
            }
            else if (input.Contains("/channel/seriesdetail?sid="))
            {
                string bizId = BBDownUtil.GetQueryString("sid", input);
                avid = $"seriesBizId:{bizId}";
            }
            else if (input.Contains("/space.bilibili.com/") && input.Contains("/lists/"))
            {
                var type = BBDownUtil.GetQueryString("type", input).ToLower();
                var path = input.Split('?', '#')[0];
                var sidPart = path[(path.LastIndexOf('/') + 1)..];

                if (type == "series")
                {
                    avid = $"seriesBizId:{sidPart}";
                }
                else
                {
                    avid = $"listBizId:{sidPart}";
                }
            }
            else if (input.Contains("/space.bilibili.com/") && input.Contains("/favlist"))
            {
                string mid = UidRegex().Match(input).Groups[1].Value;
                string fid = BBDownUtil.GetQueryString("fid", input);
                avid = $"favId:{fid}:{mid}";
            }
            else if (input.Contains("/space.bilibili.com/"))
            {
                string mid = UidRegex().Match(input).Groups[1].Value;
                avid = $"mid:{mid}";
            }
            else if (input.Contains("ep_id="))
            {
                string epId = BBDownUtil.GetQueryString("ep_id", input);
                avid = $"ep:{epId}";
            }
            else if (GlobalEpRegex().Match(input).Success)
            {
                string epId = GlobalEpRegex().Match(input).Groups[1].Value;
                avid = $"ep:{epId}";
            }
            else if (BangumiMdRegex().Match(input).Success)
            {
                string mdId = BangumiMdRegex().Match(input).Groups[1].Value;
                string epId = await GetEpIdByMDAsync(mdId);
                avid = $"ep:{epId}";
            }
            else
            {
                string web = await HTTPUtil.GetWebSourceAsync(input);
                Regex regex = StateRegex();
                string json = regex.Match(web).Groups[1].Value;
                using var jDoc = JsonDocument.Parse(json);
                var epList = jDoc.RootElement.EnumerateArraySafe("epList");
                var firstEp = epList.FirstOrDefault();
                if (firstEp.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                    throw new InvalidOperationException("未找到任何分P信息");
                string epId = firstEp.GetValueAsStringSafe("id");
                avid = $"ep:{epId}";
            }
        }
        else if (input.ToLowerInvariant().StartsWith("bv"))
        {
            avid = DecodeBv(input[3..]);
        }
        else if (input.ToLowerInvariant().StartsWith("av"))
        {
            avid = input.ToLowerInvariant()[2..];
        }
        else if (input.StartsWith("cheese/"))
        {
            string epId = "";
            if (input.Contains("/ep"))
            {
                epId = EpRegex().Match(input).Groups[1].Value;
            }
            else if (input.Contains("/ss"))
            {
                epId = await GetEpidBySSIdAsync(SsRegex().Match(input).Groups[1].Value);
            }
            avid = $"cheese:{epId}";
        }
        else if (input.StartsWith("ep"))
        {
            string epId = input[2..];
            avid = $"ep:{epId}";
        }
        else if (input.StartsWith("ss"))
        {
            try
            {
                string epId = await GetEpIdByBangumiSSIdAsync(input[2..]);
                avid = $"ep:{epId}";
            }
            catch
            {
                string epId = await GetEpidBySSIdAsync(input[2..]);
                avid = $"cheese:{epId}";
            }
        }
        else if (input.StartsWith("md"))
        {
            string mdId = MdRegex().Match(input).Groups[1].Value;
            string epId = await GetEpIdByMDAsync(mdId);
            avid = $"ep:{epId}";
        }
        else
        {
            throw new ArgumentException("输入有误：无法识别的视频 URL 或 ID");
        }
        return await FixAvidAsync(avid);
    }

    private static async Task<string> FixAvidAsync(string avid)
    {
        if (!avid.All(char.IsDigit))
            return avid;
        try
        {
            string api = $"https://www.bilibili.com/video/av{avid}/";
            string location = await HTTPUtil.GetWebLocationAsync(api);
            return location.Contains("/ep") ? $"ep:{EpRegex().Match(location).Groups[1].Value}" : avid;
        }
        catch (Exception ex) when (ex is HttpRequestException)
        {
            Core.Logger.LogDebug("FixAvidAsync HEAD 请求失败: {0}", ex.Message);
            return avid;
        }
    }

    private static string DecodeBv(string bv)
    {
        return BilibiliBvConverter.Decode(bv).ToString();
    }

    private static async Task<string> GetEpidBySSIdAsync(string ssid)
    {
        string api = $"https://api.bilibili.com/pugv/view/web/season?season_id={ssid}";
        string json = await HTTPUtil.GetWebSourceAsync(api);
        using var jDoc = JsonDocument.Parse(json);
        var episodes = jDoc.RootElement.GetPropertySafe("data").EnumerateArraySafe("episodes");
        var firstEp = episodes.FirstOrDefault();
        if (firstEp.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new InvalidOperationException("未找到课程分P信息");
        return firstEp.GetValueAsStringSafe("id");
    }

    private static async Task<string> GetEpIdByBangumiSSIdAsync(string ssId)
    {
        string api = $"https://{Core.Config.Current.EpHost}/pgc/view/web/season?season_id={ssId}";
        string json = await HTTPUtil.GetWebSourceAsync(api);
        using var jDoc = JsonDocument.Parse(json);
        var episodes = jDoc.RootElement.GetPropertySafe("result").EnumerateArraySafe("episodes");
        var firstEp = episodes.FirstOrDefault();
        if (firstEp.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new InvalidOperationException("未找到番剧分P信息");
        return firstEp.GetValueAsStringSafe("id");
    }

    private static async Task<string> GetEpIdByMDAsync(string mdId)
    {
        string api = $"https://api.bilibili.com/pgc/review/user?media_id={mdId}";
        string json = await HTTPUtil.GetWebSourceAsync(api);
        using var jDoc = JsonDocument.Parse(json);
        return jDoc.RootElement.GetPropertySafe("result").GetPropertySafe("media").GetPropertySafe("new_ep").GetValueAsStringSafe("id");
    }

    [GeneratedRegex("[Aa][Vv](\\d+)")]
    private static partial Regex AvRegex();

    [GeneratedRegex("[Bb][Vv]1(\\w+)")]
    private static partial Regex BVRegex();

    [GeneratedRegex("/ep(\\d+)")]
    private static partial Regex EpRegex();

    [GeneratedRegex("/ss(\\d+)")]
    private static partial Regex SsRegex();

    [GeneratedRegex(@"space\.bilibili\.com/(\d+)")]
    private static partial Regex UidRegex();

    [GeneratedRegex(@"\.bilibili\.tv\/\w+\/play\/\d+\/(\d+)")]
    private static partial Regex GlobalEpRegex();

    [GeneratedRegex("bangumi/media/(md\\d+)")]
    private static partial Regex BangumiMdRegex();

    [GeneratedRegex(@"window\.__INITIAL_STATE__=([\s\S].*?);\(function\(\)")]
    private static partial Regex StateRegex();

    [GeneratedRegex("md(\\d+)")]
    private static partial Regex MdRegex();
}

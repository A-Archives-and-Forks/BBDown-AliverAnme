using System;
using System.IO;
using System.Linq;
using static BBDown.Core.Entity.Entity;
using System.Text.RegularExpressions;

using BBDown.Core.Util;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    private static string FormatSavePath(string savePathFormat, string title, Video? videoTrack, Audio? audioTrack, Page p, int pagesCount, string apiType, long pubTime)
    {
        var result = savePathFormat.Replace('\\', '/');
        var regex = InfoRegex();
        foreach (Match m in regex.Matches(result).Cast<Match>())
        {
            var key = m.Groups[1].Value;

            //解析自定义日期格式
            var defaultDateFormat = "yyyy-MM-dd_HH-mm-ss";
            string[] prefixes = ["publishDate:", "videoDate:"];
            foreach (var prefix in prefixes)
            {
                if (key.StartsWith(prefix))
                {
                    defaultDateFormat = key[(key.IndexOf(':') + 1)..];
                    key = prefix.Replace(":", "");
                    break;
                }
            }

            var v = key switch
            {
                "videoTitle" => BBDownUtil.GetValidFileName(title, filterSlash: true).Trim().TrimEnd('.').Trim(),
                "pageNumber" => p.index.ToString(),
                "pageNumberWithZero" => p.index.ToString().PadLeft(pagesCount.ToString().Length, '0'),
                "pageTitle" => BBDownUtil.GetValidFileName(p.title, filterSlash: true).Trim().TrimEnd('.').Trim(),
                "bvid" => p.bvid,
                "aid" => p.aid,
                "cid" => p.cid,
                "ownerName" => p.ownerName == null ? "" : BBDownUtil.GetValidFileName(p.ownerName, filterSlash: true).Trim().TrimEnd('.').Trim(),
                "ownerMid" => p.ownerMid ?? "",
                "dfn" => videoTrack == null ? "" : videoTrack.dfn,
                "res" => videoTrack == null ? "" : videoTrack.res,
                "fps" => videoTrack == null ? "" : videoTrack.fps,
                "videoCodecs" => videoTrack == null ? "" : videoTrack.codecs,
                "videoBandwidth" => videoTrack == null ? "" : videoTrack.bandwidth.ToString(),
                "audioCodecs" => audioTrack == null ? "" : audioTrack.codecs,
                "audioBandwidth" => audioTrack == null ? "" : audioTrack.bandwidth.ToString(),
                "publishDate" => FormatTimeStamp(pubTime, defaultDateFormat),
                "videoDate" => FormatTimeStamp(p.pubTime, defaultDateFormat),
                "apiType" => apiType,
                _ => $"<{key}>"
            };
            result = result.Replace(m.Value, v);
        }
        // 大小写不敏感判断：用户模板可能产出 ".MP4"/".Mp4"（例如占位符替换自大写扩展名），
        // 大小写敏感会让 ".MP4" 再被追加一次 ".mp4" 变成 ".MP4.mp4"。
        if (!result.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) { result += ".mp4"; }
        return result;
    }

    [GeneratedRegex("<([\\w:\\-.]+?)>")]
    private static partial Regex InfoRegex();

}

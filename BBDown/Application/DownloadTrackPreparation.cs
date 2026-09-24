using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Entity.Entity;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Util;

namespace BBDown;

internal partial class Program
{
    private static (bool IsDash, bool? EarlyResult) PrepareDashTracks(
        ParsedResult parsedResult,
        MyOption options,
        Page page,
        Dictionary<string, byte> encodingPriority,
        Dictionary<string, int> dfnPriority)
    {
        bool isDash = (parsedResult.VideoTracks.Any() || parsedResult.AudioTracks.Any()) && !parsedResult.Clips.Any();
        if (!isDash) return (false, null);

        if (parsedResult.VideoTracks.Count == 0)
        {
            Logger.LogWarn("没有找到符合要求的视频流");
            if (options.VideoOnly) return (true, false);
        }
        if (parsedResult.AudioTracks.Count == 0)
        {
            Logger.LogWarn("没有找到符合要求的音频流");
            if (options.AudioOnly) return (true, false);
        }

        if (options.AudioOnly)
            parsedResult.VideoTracks.Clear();
        if (options.VideoOnly)
        {
            parsedResult.AudioTracks.Clear();
            parsedResult.BackgroundAudioTracks.Clear();
            parsedResult.RoleAudioList.Clear();
        }

        parsedResult.VideoTracks = SortTracks(
            parsedResult.VideoTracks, dfnPriority, encodingPriority, options.VideoAscending);
        parsedResult.AudioTracks = SortTracks(
            parsedResult.AudioTracks, encodingPriority, options.AudioAscending);
        parsedResult.BackgroundAudioTracks = SortTracks(
            parsedResult.BackgroundAudioTracks, encodingPriority, options.AudioAscending);
        foreach (var role in parsedResult.RoleAudioList)
            role.audio = SortTracks(role.audio, encodingPriority, options.AudioAscending);

        if (!options.HideStreams)
            PrintAllTracksInfo(parsedResult, page.dur, options.OnlyShowInfo);

        return (true, options.OnlyShowInfo ? true : null);
    }

    private static async Task<(ParsedResult ParsedResult, List<string> Clips, int VideoIndex, bool Selected, bool? EarlyResult)> PrepareFlvTracksAsync(
        ParsedResult parsedResult,
        Page page,
        MyOption options,
        string aidOri,
        string? firstEncoding,
        Dictionary<string, byte> encodingPriority,
        Dictionary<string, int> dfnPriority,
        bool selected,
        CancellationToken cancellationToken)
    {
        if (options.DecryptDrm)
        {
            Logger.LogError("此视频需要大会员登录才能获取完整DRM内容。");
            Logger.LogError("请先运行: BBDown login  或使用 --cookie 参数");
            return (parsedResult, parsedResult.Clips, 0, selected, false);
        }

        var clips = parsedResult.Clips;
        var dfns = parsedResult.Dfns;
        int videoIndex = 0;
        if (options.Interactive && !selected)
        {
            int index = 0;
            dfns.ForEach(key => Logger.LogColor($"{index++}.{AppSettings.QualityMap.GetValueOrDefault(key, $"未知({key})")}"));
            Logger.Log("请选择最想要的清晰度(输入序号): ", false);
            Console.ForegroundColor = ConsoleColor.Cyan;
            videoIndex = ReadIntSafe();
            // 索引等于 count 也越界；非法输入回退到首档。
            if (videoIndex >= dfns.Count || videoIndex < 0) videoIndex = 0;
            Console.ResetColor();

            parsedResult = await Parser.ExtractTracksAsync(
                aidOri, page.aid, page.cid, page.epid,
                options.UseTvApi, options.UseIntlApi, options.UseAppApi,
                firstEncoding!, options.DecryptDrm, dfns[videoIndex], cancellationToken);
            if (!page.points.Any()) page.points = parsedResult.ExtraPoints;
            selected = true;
            videoIndex = 0;
        }

        parsedResult.VideoTracks = SortTracks(
            parsedResult.VideoTracks, dfnPriority, encodingPriority, options.VideoAscending);
        Logger.Log($"共计{parsedResult.VideoTracks.Count}条流(共有{clips.Count}个分段).");
        int displayIndex = 0;
        foreach (var video in parsedResult.VideoTracks)
        {
            var kbps = video.dur > 0 ? video.size / 1024 / video.dur * 8 : 0;
            Logger.LogColor($"{displayIndex++}. [{video.dfn}] [{video.res}] [{video.codecs}] [{video.fps}] [~{kbps:00} kbps] [{BBDownUtil.FormatFileSize(video.size)}]".Replace("[] ", ""), false);
            if (options.OnlyShowInfo) clips.ForEach(Console.WriteLine);
        }

        return (parsedResult, clips, videoIndex, selected, options.OnlyShowInfo ? true : null);
    }
}

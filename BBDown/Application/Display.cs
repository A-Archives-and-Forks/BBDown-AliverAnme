using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using System.Linq;
using BBDown.Core;
using BBDown.Core.Entity;

using BBDown.Core.Util;
using System.Text.Json;
namespace BBDown;

internal partial class Program
{
    private static void PrintAllTracksInfo(ParsedResult parsedResult, int pageDur, bool onlyShowInfo)
    {
        if (parsedResult.BackgroundAudioTracks.Any() && parsedResult.RoleAudioList.Any())
        {
            Logger.Log($"共计{parsedResult.BackgroundAudioTracks.Count}条背景音频流.");
            int index = 0;
            foreach (var a in parsedResult.BackgroundAudioTracks)
            {
                int pDur = pageDur == 0 ? a.dur : pageDur;
                Logger.LogColor($"{index++}. [{a.codecs}] [{a.bandwidth} kbps] [~{BBDownUtil.FormatFileSize(pDur * a.bandwidth * 1024 / 8)}]", false);
            }
            var firstRoleAudio = parsedResult.RoleAudioList[0].audio;
            if (firstRoleAudio != null && firstRoleAudio.Any())
            {
                Logger.Log($"共计{parsedResult.RoleAudioList.Count}条配音, 每条包含{firstRoleAudio.Count}条配音流.");
                index = 0;
                foreach (var a in firstRoleAudio)
                {
                    int pDur = pageDur == 0 ? a.dur : pageDur;
                    Logger.LogColor($"{index++}. [{a.codecs}] [{a.bandwidth} kbps] [~{BBDownUtil.FormatFileSize(pDur * a.bandwidth * 1024 / 8)}]", false);
                }
            }
        }
        //展示所有的音视频流信息
        if (parsedResult.VideoTracks.Any())
        {
            Logger.Log($"共计{parsedResult.VideoTracks.Count}条视频流.");
            int index = 0;
            foreach (var v in parsedResult.VideoTracks)
            {
                int pDur = pageDur == 0 ? v.dur : pageDur;
                var size = v.size > 0 ? v.size : pDur * v.bandwidth * 1024 / 8;
                Logger.LogColor($"{index++}. [{v.dfn}] [{v.res}] [{v.codecs}] [{v.fps}] [{v.bandwidth} kbps] [~{BBDownUtil.FormatFileSize(size)}]".Replace("[] ", ""), false);
                if (onlyShowInfo) Console.WriteLine(v.baseUrl);
            }
        }
        if (parsedResult.AudioTracks.Any())
        {
            Logger.Log($"共计{parsedResult.AudioTracks.Count}条音频流.");
            int index = 0;
            foreach (var a in parsedResult.AudioTracks)
            {
                int pDur = pageDur == 0 ? a.dur : pageDur;
                Logger.LogColor($"{index++}. [{a.codecs}] [{a.bandwidth} kbps] [~{BBDownUtil.FormatFileSize(pDur * a.bandwidth * 1024 / 8)}]", false);
                if (onlyShowInfo) Console.WriteLine(a.baseUrl);
            }
        }
    }

    private static void PrintSelectedTrackInfo(Video? selectedVideo, Audio? selectedAudio, int pageDur)
    {
        if (selectedVideo != null)
        {
            int pDur = pageDur == 0 ? selectedVideo.dur : pageDur;
            var size = selectedVideo.size > 0 ? selectedVideo.size : pDur * selectedVideo.bandwidth * 1024 / 8;
            Logger.LogColor($"[视频] [{selectedVideo.dfn}] [{selectedVideo.res}] [{selectedVideo.codecs}] [{selectedVideo.fps}] [{selectedVideo.bandwidth} kbps] [~{BBDownUtil.FormatFileSize(size)}]".Replace("[] ", ""), false);
        }
        if (selectedAudio != null)
        {
            int pDur = pageDur == 0 ? selectedAudio.dur : pageDur;
            Logger.LogColor($"[音频] [{selectedAudio.codecs}] [{selectedAudio.bandwidth} kbps] [~{BBDownUtil.FormatFileSize(pDur * selectedAudio.bandwidth * 1024 / 8)}]", false);
        }
    }

    /// <summary>
    /// 引导用户进行手动选择轨道
    /// </summary>
    /// <param name="parsedResult"></param>
    /// <param name="vIndex"></param>
    /// <param name="aIndex"></param>
    private static int ReadIntSafe()
    {
        if (!int.TryParse(Console.ReadLine(), out var val))
            return 0;
        return val;
    }

    private static void SelectTrackManually(ParsedResult parsedResult, ref int vIndex, ref int aIndex)
    {
        if (parsedResult.VideoTracks.Any())
        {
            Logger.Log("请选择一条视频流(输入序号): ", false);
            Console.ForegroundColor = ConsoleColor.Cyan;
            vIndex = ReadIntSafe();
            // 合法下标是 0..Count-1；用 > 会放过 vIndex==Count，下游 ElementAtOrDefault
            // 取到 null 静默跳过该轨道，产出缺流文件。必须用 >=。
            if (vIndex >= parsedResult.VideoTracks.Count || vIndex < 0) vIndex = 0;
            Console.ResetColor();
        }
        if (parsedResult.AudioTracks.Any())
        {
            Logger.Log("请选择一条音频流(输入序号): ", false);
            Console.ForegroundColor = ConsoleColor.Cyan;
            aIndex = ReadIntSafe();
            if (aIndex >= parsedResult.AudioTracks.Count || aIndex < 0) aIndex = 0;
            Console.ResetColor();
        }
    }

    /// <summary>
    /// 下载轨道
    /// </summary>
    /// <returns></returns>
    private static async Task DownloadTrackAsync(string url, string destPath, BBDownDownloadUtil.DownloadConfig downloadConfig, bool video, CancellationToken token = default)
    {
        if (downloadConfig.MultiThread && !url.Contains("-cmcc-"))
        {
            // 下载→合并→清理在目标路径的独占锁内完成（MultiThreadDownloadAndMergeAsync），
            // 调用方不再在锁外合并/删除分片：相同目标路径的第二个任务会等第一个任务
            // 完全结束后再进入，避免复用/误删上一个任务的分片或读到半截成品。
            await BBDownDownloadUtil.MultiThreadDownloadAndMergeAsync(url, destPath, downloadConfig, token);
        }
        else
        {
            if (downloadConfig.MultiThread && url.Contains("-cmcc-"))
            {
                Logger.LogWarn("检测到cmcc域名cdn, 已经禁用多线程");
                downloadConfig.ForceHttp = false;
            }
            await BBDownDownloadUtil.DownloadFileAsync(url, destPath, downloadConfig, token);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using BBDown.Core;
using BBDown.Core.Entity;
using System.Text.Json;

using BBDown.Core.Util;
namespace BBDown;

internal partial class Program
{
    public static async Task DownloadPagesAsync(MyOption myOption, VInfo vInfo, Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority,
        string? firstEncoding, bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, int delay, string apiType, DownloadTask? relatedTask = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        List<Page> pagesInfo = vInfo.PagesInfo;
        bool bangumi = vInfo.IsBangumi;
        bool cheese = vInfo.IsCheese;
        //获取已选择的分P列表
        List<string>? selectedPages = GetSelectedPages(myOption, vInfo, input);

        Logger.Log($"共计 {pagesInfo.Count} 个分P, 已选择：" + (selectedPages == null ? "ALL" : string.Join(",", selectedPages)));
        var pagesCount = pagesInfo.Count;

        //过滤不需要的分P
        if (selectedPages != null)
        {
            pagesInfo = pagesInfo.Where(p => selectedPages.Contains(p.index.ToString())).ToList();
        }

        // 根据p数选择存储路径
        savePathFormat = string.IsNullOrEmpty(myOption.FilePattern) ? SinglePageDefaultSavePath : myOption.FilePattern;
        // 1. 多P; 2. 只有1P, 但是是番剧, 尚未完结时 按照多P处理
        if (pagesCount > 1 || (bangumi && !vInfo.IsBangumiEnd))
        {
            savePathFormat = string.IsNullOrEmpty(myOption.MultiFilePattern) ? MultiPageDefaultSavePath : myOption.MultiFilePattern;
        }

        var failedPages = new List<int>();

        // 存档以 aid 为粒度，而一个多P稿件的所有分P共享同一个 aid。
        // 若下完第一个分P就写入存档，同稿件余下的分P会在下一次循环里被
        // CheckAidFromFile 判定为"已下载"而全部跳过——收藏夹、合集、UP 主投稿
        // 这类含多P稿件的列表尤其容易踩到。因此必须等一个 aid 的分P全部成功
        // 之后再写入。
        var remainingPagesByAid = pagesInfo
            .GroupBy(p => p.aid)
            .ToDictionary(g => g.Key, g => g.Count());
        var failedAids = new HashSet<string>();

        foreach (Page p in pagesInfo)
        {
            if (pagesInfo.Count > 1 && delay > 0)
            {
                Logger.Log($"停顿{delay}秒...");
                await Task.Delay(delay * 1000, cancellationToken);
            }
            Logger.Log($"开始解析P{p.index}: {p.aid}... ({pagesInfo.IndexOf(p) + 1} of {pagesInfo.Count})");

            if (myOption.SaveArchivesToFile && CheckAidFromFile(p.aid))
            {
                Logger.Log($"aid: {p.aid}已下载过, 跳过下载...");
                remainingPagesByAid[p.aid]--;
                continue;
            }

            var succeeded = await DownloadPageAsync(p, myOption, vInfo, pagesInfo, encodingPriority, dfnPriority, firstEncoding,
                downloadDanmaku, downloadDanmakuFormats, input, savePathFormat, lang, aidOri, apiType, relatedTask, cancellationToken);

            if (myOption.SaveArchivesToFile)
            {
                remainingPagesByAid[p.aid]--;
                // 只要该稿件有任何一个分P失败就不入档，否则下次运行会跳过尚未下全的稿件
                if (!succeeded) failedAids.Add(p.aid);
                if (remainingPagesByAid[p.aid] == 0 && !failedAids.Contains(p.aid))
                {
                    SaveAidToFile(p.aid);
                }
            }

            if (!succeeded)
            {
                // 记录后继续处理其余分P
                failedPages.Add(p.index);
            }
        }

        if (failedPages.Count > 0)
        {
            // 必须抛出：调用方据此决定退出码，serve 模式据此把任务标记为失败。
            // 此前这里只打印"任务完成"，失败对脚本与 API 客户端完全不可见。
            throw new InvalidOperationException(
                $"共 {failedPages.Count} 个分P下载失败：P{string.Join(", P", failedPages)}");
        }

        Logger.Log("任务完成");
    }

    /// <summary>
    /// 下载单个分P。返回 false 表示该分P最终失败——调用方据此避免把它记为已完成，
    /// 并让整体任务状态反映失败，而不是照常报告"任务完成"。
    /// </summary>
    private static async Task<bool> DownloadPageAsync(Page p, MyOption myOption, VInfo vInfo, List<Page> selectedPagesInfo, Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority,
        string? firstEncoding, bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, string apiType, DownloadTask? relatedTask = null, CancellationToken cancellationToken = default)
    {
        string desc = string.IsNullOrEmpty(p.desc) ? vInfo.Desc : p.desc;
        bool bangumi = vInfo.IsBangumi;
        var pagesCount = selectedPagesInfo.Count;
        List<Subtitle> subtitleInfo = [];
        string title = vInfo.Title;
        string pic = vInfo.Pic;
        long pubTime = vInfo.PubTime;
        bool selected = false; //用户是否已经手动选择过了轨道
        int retryCount = 0;
        while (retryCount < 3)
        {
        try
        {
            Logger.LogDebug("尝试获取章节信息...");
            p.points = await BBDownUtil.FetchPointsAsync(p.cid, p.aid);

            string videoPath = $"{p.aid}/{p.aid}.P{p.index}.{p.cid}.mp4";
            string audioPath = $"{p.aid}/{p.aid}.P{p.index}.{p.cid}.m4a";
            var coverPath = $"{p.aid}/{p.aid}.jpg";

            //处理文件夹以.结尾导致的异常情况
            if (title.EndsWith('.')) title += "_fix";
            //处理文件夹以.开头导致的异常情况
            if (title.StartsWith('.')) title = "_" + title;

            //处理封面&&字幕
            if (!myOption.OnlyShowInfo)
            {
                if (!Directory.Exists(p.aid))
                {
                    Directory.CreateDirectory(p.aid);
                }
                if (!myOption.SkipCover && !myOption.SubOnly && !File.Exists(coverPath) && !myOption.DanmakuOnly && !myOption.CoverOnly)
                {
                    await BBDownDownloadUtil.DownloadFileAsync(pic == "" ? p.cover! : pic, coverPath, new BBDownDownloadUtil.DownloadConfig(), cancellationToken);
                }

                if (!myOption.SkipSubtitle && !myOption.DanmakuOnly && !myOption.CoverOnly)
                {
                    Logger.LogDebug("获取字幕...");
                    subtitleInfo = await SubUtil.GetSubtitlesAsync(p.aid, p.cid, p.epid, p.index, myOption.UseIntlApi);
                    if (myOption.SkipAi && subtitleInfo.Any())
                    {
                        Logger.Log($"跳过下载AI字幕");
                        subtitleInfo = subtitleInfo.Where(s => !s.lan.StartsWith("ai-")).ToList();
                    }
                    foreach (Subtitle s in subtitleInfo)
                    {
                        Logger.Log($"下载字幕 {s.lan} => {SubUtil.GetSubtitleCode(s.lan).Item2}...");
                        Logger.LogDebug("下载：{0}", s.url);
                        await SubUtil.SaveSubtitleAsync(s.url, s.path);
                        if (myOption.SubOnly && File.Exists(s.path) && File.ReadAllText(s.path) != "")
                        {
                            var _outSubPath = FormatSavePath(savePathFormat, title, null, null, p, pagesCount, apiType, pubTime);
                            var dir = Path.GetDirectoryName(_outSubPath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);
                            _outSubPath = Path.ChangeExtension(_outSubPath, $".{s.lan}.srt");
                            File.Move(s.path, _outSubPath, true);
                        }
                    }
                }

                if (myOption.SubOnly)
                {
                    if (Directory.Exists(p.aid) && Directory.GetFiles(p.aid).Length == 0) Directory.Delete(p.aid, true);
                    return true;
                }
            }

            //调用解析
            ParsedResult parsedResult = await Parser.ExtractTracksAsync(aidOri, p.aid, p.cid, p.epid, myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, firstEncoding!, myOption.DecryptDrm);
            List<AudioMaterial> audioMaterial = [];
            if (!p.points.Any())
            {
                p.points = parsedResult.ExtraPoints;
            }

            if (Config.Current.DebugLog)
            {
                var debugFile = $"debug_{DateTime.Now:yyyyMMddHHmmssfff}.json";
                File.WriteAllText(debugFile, parsedResult.WebJsonString);
                // 限制 debug 文件数量，保留最近 20 个
                var debugFiles = Directory.GetFiles(".", "debug_*.json").Order().ToArray();
                for (int i = 0; i < debugFiles.Length - 20; i++)
                    File.Delete(debugFiles[i]);
            }

            var savePath = "";

            var downloadConfig = new BBDownDownloadUtil.DownloadConfig()
            {
                UseAria2c = myOption.UseAria2c,
                Aria2cArgs = myOption.Aria2cArgs,
                ForceHttp = myOption.ForceHttp,
                MultiThread = myOption.MultiThread,
                RelatedTask = relatedTask,
            };

            //此处代码简直灾难, 后续优化吧
            if ((parsedResult.VideoTracks.Any() || parsedResult.AudioTracks.Any()) && !parsedResult.Clips.Any())   //dash
            {
                if (parsedResult.VideoTracks.Count == 0)
                {
                    Logger.LogWarn("没有找到符合要求的视频流");
                    if (myOption.VideoOnly) return true;
                }
                if (parsedResult.AudioTracks.Count == 0)
                {
                    Logger.LogWarn("没有找到符合要求的音频流");
                    if (myOption.AudioOnly) return true;
                }

                if (myOption.AudioOnly)
                {
                    parsedResult.VideoTracks.Clear();
                }
                if (myOption.VideoOnly)
                {
                    parsedResult.AudioTracks.Clear();
                    parsedResult.BackgroundAudioTracks.Clear();
                    parsedResult.RoleAudioList.Clear();
                }

                //排序
                parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, dfnPriority, encodingPriority, myOption.VideoAscending);
                parsedResult.AudioTracks = SortTracks(parsedResult.AudioTracks, encodingPriority, myOption.AudioAscending);
                parsedResult.BackgroundAudioTracks = SortTracks(parsedResult.BackgroundAudioTracks, encodingPriority, myOption.AudioAscending);
                foreach (var role in parsedResult.RoleAudioList)
                {
                    role.audio = SortTracks(role.audio, encodingPriority, myOption.AudioAscending);
                }

                //打印轨道信息
                if (!myOption.HideStreams)
                {
                    PrintAllTracksInfo(parsedResult, p.dur, myOption.OnlyShowInfo);
                }

                //仅展示 跳过下载
                if (myOption.OnlyShowInfo)
                {
                    return true;
                }

                int vIndex = 0; //用户手动选择的视频序号
                int aIndex = 0; //用户手动选择的音频序号

                //选择轨道
                if (myOption.Interactive && !selected)
                {
                    SelectTrackManually(parsedResult, ref vIndex, ref aIndex);
                    selected = true;
                }

                Video? selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(vIndex);
                Audio? selectedAudio = parsedResult.AudioTracks.ElementAtOrDefault(aIndex);
                Audio? selectedBackgroundAudio = parsedResult.BackgroundAudioTracks.ElementAtOrDefault(aIndex);

                Logger.LogDebug("Format Before: " + savePathFormat);
                savePath = FormatSavePath(savePathFormat, title, selectedVideo, selectedAudio, p, pagesCount, apiType, pubTime);
                Logger.LogDebug("Format After: " + savePath);

                if (downloadDanmaku)
                {
                    var danmakuXmlPath = Path.ChangeExtension(savePath, ".xml");
                    var danmakuAssPath = Path.ChangeExtension(savePath, ".ass");
                    Logger.Log("正在下载弹幕Xml文件");
                    var danmakuUrl = $"https://comment.bilibili.com/{p.cid}.xml";
                    await BBDownDownloadUtil.DownloadFileAsync(danmakuUrl, danmakuXmlPath, downloadConfig, cancellationToken);
                    var danmakus = DanmakuUtil.ParseXml(danmakuXmlPath);
                    if (danmakus == null)
                    {
                        Logger.Log("弹幕Xml解析失败, 删除Xml...");
                        File.Delete(danmakuXmlPath);
                    }
                    else if (danmakus.Length == 0)
                    {
                        Logger.Log("当前视频没有弹幕, 删除Xml...");
                        File.Delete(danmakuXmlPath);
                    }
                    else if (downloadDanmakuFormats.Contains(BBDownDanmakuFormat.Ass))
                    {
                        Logger.Log("正在保存弹幕Ass文件...");
                        await DanmakuUtil.SaveAsAssAsync(danmakus, danmakuAssPath);
                    }

                    // delete xml if possible
                    if (!downloadDanmakuFormats.Contains(BBDownDanmakuFormat.Xml) && File.Exists(danmakuXmlPath)) 
                    {
                        File.Delete(danmakuXmlPath);
                    }

                    if (myOption.DanmakuOnly)
                    {
                        if (Directory.Exists(p.aid))
                        {
                            Directory.Delete(p.aid);
                        }
                        return true;
                    }
                }

                if (myOption.CoverOnly)
                {
                    var coverUrl = pic == "" ? p.cover! : pic;
                    var newCoverPath = Path.ChangeExtension(savePath, Path.GetExtension(coverUrl));
                    await BBDownDownloadUtil.DownloadFileAsync(coverUrl, newCoverPath, downloadConfig, cancellationToken);
                    if (Directory.Exists(p.aid) && Directory.GetFiles(p.aid).Length == 0) Directory.Delete(p.aid, true);
                    relatedTask?.SavePaths.Add(newCoverPath);
                }

                Logger.Log($"已选择的流:");
                PrintSelectedTrackInfo(selectedVideo, selectedAudio, p.dur);

                //用户开启了强制替换
                if (myOption.ForceReplaceHost && string.IsNullOrEmpty(myOption.UposHost))
                {
                    myOption.UposHost = BACKUP_HOST;
                }

                //处理PCDN
                HandlePcdn(myOption, selectedVideo, selectedAudio);

                if (!myOption.OnlyShowInfo && File.Exists(savePath) && new FileInfo(savePath).Length != 0)
                {
                    Logger.Log($"{savePath}已存在, 跳过下载...");
                    relatedTask?.SavePaths.Add(savePath);
                    File.Delete(coverPath);
                    if (Directory.Exists(p.aid) && Directory.GetFiles(p.aid).Length == 0)
                    {
                        Directory.Delete(p.aid, true);
                    }
                    return true;
                }

                if (selectedVideo != null)
                {
                    //杜比视界, 若ffmpeg版本小于5.0, 使用mp4box封装
                    if (selectedVideo.dfn == AppSettings.QualityMap["126"] && !myOption.UseMP4box && !ExternalToolHelper.CheckFFmpegDOVI())
                    {
                        Logger.LogWarn($"检测到杜比视界清晰度且您的ffmpeg版本小于5.0,将使用mp4box混流...");
                        myOption.UseMP4box = true;
                    }
                    Logger.Log($"开始下载P{p.index}视频...");
                    await DownloadTrackAsync(selectedVideo.baseUrl, videoPath, downloadConfig, video: true);
                }

                if (selectedAudio != null)
                {
                    Logger.Log($"开始下载P{p.index}音频...");
                    await DownloadTrackAsync(selectedAudio.baseUrl, audioPath, downloadConfig, video: false);
                }

                if (selectedBackgroundAudio != null)
                {
                    var backgroundPath = $"{p.aid}/{p.aid}.{p.cid}.P{p.index}.back_ground.m4a";
                    Logger.Log($"开始下载P{p.index}背景配音...");
                    await DownloadTrackAsync(selectedBackgroundAudio.baseUrl, backgroundPath, downloadConfig, video: false);
                    audioMaterial.Add(new AudioMaterial("背景音频", "", backgroundPath));
                }

                if (parsedResult.RoleAudioList.Any())
                {
                    foreach (var role in parsedResult.RoleAudioList)
                    {
                        Logger.Log($"开始下载P{p.index}配音[{role.title}]...");
                        await DownloadTrackAsync(role.audio[aIndex].baseUrl, role.path, downloadConfig, video: false);
                        audioMaterial.Add(new AudioMaterial(role));
                    }
                }

                Logger.Log($"下载P{p.index}完毕");

                if (parsedResult.IsDrm && myOption.DecryptDrm && (!string.IsNullOrEmpty(parsedResult.KidHex) || !string.IsNullOrEmpty(parsedResult.PsshBase64)))
                {
                    await DecryptDrmAsync(parsedResult, videoPath, audioPath, myOption);
                }

                if (!parsedResult.VideoTracks.Any()) videoPath = "";
                if (!parsedResult.AudioTracks.Any()) audioPath = "";
                if (myOption.SkipMux) return true;
                Logger.Log($"开始合并音视频{(subtitleInfo.Any() ? "和字幕" : "")}...");
                if (myOption.AudioOnly)
                    savePath = savePath[..^4] + ".m4a";

                var isHevc = selectedVideo?.codecs == "HEVC";
                int code = BBDownMuxer.MuxAV(myOption.UseMP4box, p.bvid, videoPath, audioPath, audioMaterial, savePath,
                    desc,
                    title,
                    p.ownerName ?? "",
                    (pagesCount > 1 || (bangumi && !vInfo.IsBangumiEnd)) ? p.title : "",
                    File.Exists(coverPath) ? coverPath : "",
                    lang,
                    subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.SimplyMux, isHevc);
                if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
                {
                    Logger.LogError("合并失败"); return false;
                }
                Logger.Log("清理临时文件...");
                await Task.Delay(200, cancellationToken);
                if (parsedResult.VideoTracks.Any()) File.Delete(videoPath);
                if (parsedResult.AudioTracks.Any()) File.Delete(audioPath);
                if (p.points.Any()) File.Delete(Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters"));
                foreach (var s in subtitleInfo) File.Delete(s.path);
                foreach (var a in audioMaterial) File.Delete(a.path);
                if (selectedPagesInfo.Count == 1 || p.index == selectedPagesInfo.Last().index || p.aid != selectedPagesInfo.Last().aid)
                    File.Delete(coverPath);
                if (Directory.Exists(p.aid) && Directory.GetFiles(p.aid).Length == 0) Directory.Delete(p.aid, true);
            }
            else if (parsedResult.Clips.Any() && parsedResult.Dfns.Any())   //flv
            {
                if (myOption.DecryptDrm)
                {
                    Logger.LogError("此视频需要大会员登录才能获取完整DRM内容。");
                    Logger.LogError($"请先运行: BBDown login  或使用 --cookie 参数");
                    return false;
                }
                var clips = parsedResult.Clips;
                var dfns = parsedResult.Dfns;

                int vIndex = 0;
                if (myOption.Interactive && !selected)
                {
                    int i = 0;
                    dfns.ForEach(key => Logger.LogColor($"{i++}.{AppSettings.QualityMap[key]}"));
                    Logger.Log("请选择最想要的清晰度(输入序号): ", false);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    vIndex = ReadIntSafe();
                    if (vIndex > dfns.Count || vIndex < 0) vIndex = 0;
                    Console.ResetColor();
                    //重新解析
                    parsedResult = await Parser.ExtractTracksAsync(aidOri, p.aid, p.cid, p.epid, myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, firstEncoding!, myOption.DecryptDrm, dfns[vIndex]);
                    if (!p.points.Any()) p.points = parsedResult.ExtraPoints;
                    selected = true;
                    vIndex = 0; // 重新解析后第一个轨道即为所选清晰度
                }
                //排序
                parsedResult.VideoTracks = SortTracks(parsedResult.VideoTracks, dfnPriority, encodingPriority, myOption.VideoAscending);

                Logger.Log($"共计{parsedResult.VideoTracks.Count}条流(共有{clips.Count}个分段).");
                int index = 0;
                foreach (var v in parsedResult.VideoTracks)
                {
                    var kbps = v.dur > 0 ? v.size / 1024 / v.dur * 8 : 0;
                    Logger.LogColor($"{index++}. [{v.dfn}] [{v.res}] [{v.codecs}] [{v.fps}] [~{kbps:00} kbps] [{BBDownUtil.FormatFileSize(v.size)}]".Replace("[] ", ""), false);
                    if (myOption.OnlyShowInfo)
                    {
                        clips.ForEach(Console.WriteLine);
                    }
                }
                if (myOption.OnlyShowInfo) return true;
                savePath = FormatSavePath(savePathFormat, title, parsedResult.VideoTracks.ElementAtOrDefault(vIndex), null, p, pagesCount, apiType, pubTime);
                if (File.Exists(savePath) && new FileInfo(savePath).Length != 0)
                {
                    Logger.Log($"{savePath}已存在, 跳过下载...");
                    relatedTask?.SavePaths.Add(savePath);
                    if (selectedPagesInfo.Count == 1 && Directory.Exists(p.aid))
                    {
                        Directory.Delete(p.aid, true);
                    }
                    return true;
                }
                var pad = string.Empty.PadRight(clips.Count.ToString().Length, '0');
                for (int i = 0; i < clips.Count; i++)
                {
                    var link = clips[i];
                    videoPath = $"{p.aid}/{p.aid}.P{p.index}.{p.cid}.{i.ToString(pad)}.mp4";
                    Logger.Log($"开始下载P{p.index}视频, 片段({(i + 1).ToString(pad)}/{clips.Count})...");
                    await DownloadTrackAsync(link, videoPath, downloadConfig, video: true);
                }
                Logger.Log($"下载P{p.index}完毕");
                Logger.Log("开始合并分段...");
                var files = BBDownUtil.GetFiles(Path.GetDirectoryName(videoPath)!, ".mp4");
                videoPath = $"{p.aid}/{p.aid}.P{p.index}.{p.cid}.mp4";
                BBDownMuxer.MergeFLV(files, videoPath);
                if (myOption.SkipMux) return true;
                Logger.Log($"开始混流视频{(subtitleInfo.Any() ? "和字幕" : "")}...");
                if (myOption.AudioOnly)
                    savePath = savePath[..^4] + ".m4a";
                int code = BBDownMuxer.MuxAV(false, p.bvid, videoPath, "", audioMaterial, savePath,
                    desc,
                    title,
                    p.ownerName ?? "",
                    (pagesCount > 1 || (bangumi && !vInfo.IsBangumiEnd)) ? p.title : "",
                    File.Exists(coverPath) ? coverPath : "",
                    lang,
                    subtitleInfo, myOption.AudioOnly, myOption.VideoOnly, p.points, p.pubTime, myOption.SimplyMux);
                if (code != 0 || !File.Exists(savePath) || new FileInfo(savePath).Length == 0)
                {
                    Logger.LogError("合并失败"); return false;
                }
                Logger.Log("清理临时文件...");
                await Task.Delay(200, cancellationToken);
                if (parsedResult.VideoTracks.Count != 0) File.Delete(videoPath);
                foreach (var s in subtitleInfo) File.Delete(s.path);
                foreach (var a in audioMaterial) File.Delete(a.path);
                if (p.points.Any()) File.Delete(Path.Combine(Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath)!, "chapters"));
                if (selectedPagesInfo.Count == 1 || p.index == selectedPagesInfo.Last().index || p.aid != selectedPagesInfo.Last().aid)
                    File.Delete(coverPath);
                if (Directory.Exists(p.aid) && Directory.GetFiles(p.aid).Length == 0) Directory.Delete(p.aid, true);
            }
            else
            {
                if (myOption.DecryptDrm)
                {
                    Logger.LogError("此视频需要大会员登录才能获取完整DRM内容。");
                    Logger.LogError("请先运行: BBDown login  或使用 --cookie 参数");
                }
                else
                {
                    Logger.LogError("解析此分P失败(建议--debug查看详细信息)");
                }
                if (parsedResult.WebJsonString.Length < 100)
                {
                    Logger.LogError(parsedResult.WebJsonString);
                }
                Logger.LogDebug("{0}", parsedResult.WebJsonString);
            }

            if (!string.IsNullOrWhiteSpace(savePath)) {
                relatedTask?.SavePaths.Add(savePath);
            }
            return true; // success, exit retry loop
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidOperationException)
        {
            retryCount++;
            if (retryCount >= 3) throw;
            Logger.LogError(ex.Message);
            Logger.LogWarn("下载出现异常, 3秒后将进行自动重试...");
            await Task.Delay(3000, cancellationToken);
        }
        }
        return false;
    }

}

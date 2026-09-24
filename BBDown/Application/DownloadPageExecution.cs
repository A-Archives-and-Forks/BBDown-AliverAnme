using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Entity.Entity;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Util;

namespace BBDown;

internal sealed class PageExecutionContext
{
    internal required Page Page { get; init; }
    internal required MyOption Options { get; init; }
    internal required VInfo VideoInfo { get; init; }
    internal required List<Page> SelectedPagesInfo { get; init; }
    internal required ParsedResult ParsedResult { get; set; }
    internal required string Description { get; init; }
    internal required string Title { get; init; }
    internal required string Pic { get; init; }
    internal required string CoverPath { get; init; }
    internal required string Lang { get; init; }
    internal required List<Subtitle> SubtitleInfo { get; init; }
    internal required List<AudioMaterial> AudioMaterial { get; init; }
    internal required BBDownDownloadUtil.DownloadConfig DownloadConfig { get; init; }
    internal required DownloadFinalizer Finalizer { get; init; }
    internal required bool DownloadDanmaku { get; init; }
    internal required BBDownDanmakuFormat[] DownloadDanmakuFormats { get; init; }
    internal required string SavePathFormat { get; init; }
    internal required int PagesCount { get; init; }
    internal required string ApiType { get; init; }
    internal required long PubTime { get; init; }
    internal required DownloadTask? RelatedTask { get; init; }
    internal required CancellationToken CancellationToken { get; init; }
}

internal sealed class DownloadPageExecutionServices
{
    internal required string BackupHost { get; init; }
    internal required Func<string, string, Video?, Audio?, Page, int, string, long, string> FormatSavePath { get; init; }
    internal required Func<string, Page, MyOption, BBDownDownloadUtil.DownloadConfig, BBDownDanmakuFormat[], DownloadTask?, CancellationToken, Task<bool?>> DownloadDanmakuAsync { get; init; }
    internal required Func<string, string, Page, BBDownDownloadUtil.DownloadConfig, DownloadTask?, CancellationToken, Task<bool>> DownloadCoverOnlyAsync { get; init; }
    internal required Func<string, string, string, List<Subtitle>, DownloadTask?, bool, bool> TrySkipExistingOutput { get; init; }
    internal required Action<Video?, Audio?, int> PrintSelectedTrackInfo { get; init; }
    internal required Action<MyOption, Video?, Audio?> HandlePcdn { get; init; }
    internal required Func<string, string, BBDownDownloadUtil.DownloadConfig, bool, CancellationToken, Task> DownloadTrackAsync { get; init; }
    internal required Func<ParsedResult, string, string, MyOption, CancellationToken, Task> DecryptDrmAsync { get; init; }
}

internal sealed class DownloadPageExecutor
{
    private readonly DownloadPageExecutionServices _services;

    internal DownloadPageExecutor(DownloadPageExecutionServices services)
    {
        _services = services;
    }

    internal async Task<bool> DownloadDashPageAsync(
        PageExecutionContext context,
        int videoIndex,
        int audioIndex)
    {
        var page = context.Page;
        var options = context.Options;
        var parsedResult = context.ParsedResult;
        var selectedVideo = parsedResult.VideoTracks.ElementAtOrDefault(videoIndex);
        var selectedAudio = parsedResult.AudioTracks.ElementAtOrDefault(audioIndex);
        var selectedBackgroundAudio = parsedResult.BackgroundAudioTracks.ElementAtOrDefault(audioIndex);

        Logger.LogDebug("Format Before: " + context.SavePathFormat);
        var savePath = PathUtil.ResolveWorkPath(_services.FormatSavePath(
            context.SavePathFormat, context.Title, selectedVideo, selectedAudio,
            page, context.PagesCount, context.ApiType, context.PubTime));
        Logger.LogDebug("Format After: " + savePath);

        if (context.DownloadDanmaku)
        {
            bool? danmakuOnlyResult = await _services.DownloadDanmakuAsync(
                savePath, page, options, context.DownloadConfig, context.DownloadDanmakuFormats,
                context.RelatedTask, context.CancellationToken);
            if (danmakuOnlyResult.HasValue) return danmakuOnlyResult.Value;
        }

        if (options.CoverOnly)
        {
            // 封面成功后立即结束，避免继续下载视频和音频轨道。
            return await _services.DownloadCoverOnlyAsync(
                savePath, context.Pic, page, context.DownloadConfig,
                context.RelatedTask, context.CancellationToken);
        }

        Logger.Log("已选择的流:");
        _services.PrintSelectedTrackInfo(selectedVideo, selectedAudio, page.dur);

        if (options.ForceReplaceHost && string.IsNullOrEmpty(options.UposHost))
            options.UposHost = _services.BackupHost;

        _services.HandlePcdn(options, selectedVideo, selectedAudio);

        if (!options.OnlyShowInfo && _services.TrySkipExistingOutput(
            savePath, page.aid, context.CoverPath, context.SubtitleInfo,
            context.RelatedTask, true))
            return true;

        var videoPath = PathUtil.ResolveWorkPath($"{page.aid}/{page.aid}.P{page.index}.{page.cid}.mp4");
        var audioPath = PathUtil.ResolveWorkPath($"{page.aid}/{page.aid}.P{page.index}.{page.cid}.m4a");
        if (selectedVideo is not null)
        {
            // SkipMux 无需探测 ffmpeg 版本；FindBinaries 也可能因该选项跳过二进制解析。
            if (selectedVideo.dfn == AppSettings.QualityMap["126"] && !options.UseMP4box && !options.SkipMux &&
                !await ExternalToolHelper.CheckFFmpegDOVIAsync())
            {
                Logger.LogWarn("检测到杜比视界清晰度且您的ffmpeg版本小于5.0,将使用mp4box混流...");
                options.UseMP4box = true;
            }
            Logger.Log($"开始下载P{page.index}视频...");
            await _services.DownloadTrackAsync(
                selectedVideo.baseUrl, videoPath, context.DownloadConfig,
                true, context.CancellationToken);
        }

        if (selectedAudio is not null)
        {
            Logger.Log($"开始下载P{page.index}音频...");
            await _services.DownloadTrackAsync(
                selectedAudio.baseUrl, audioPath, context.DownloadConfig,
                false, context.CancellationToken);
        }

        var audioMaterial = context.AudioMaterial;
        if (selectedBackgroundAudio is not null)
        {
            var backgroundPath = PathUtil.ResolveWorkPath(
                $"{page.aid}/{page.aid}.{page.cid}.P{page.index}.back_ground.m4a");
            Logger.Log($"开始下载P{page.index}背景配音...");
            await _services.DownloadTrackAsync(
                selectedBackgroundAudio.baseUrl, backgroundPath, context.DownloadConfig,
                false, context.CancellationToken);
            audioMaterial.Add(new AudioMaterial("背景音频", "", backgroundPath));
        }

        if (parsedResult.RoleAudioList.Any())
        {
            foreach (var role in parsedResult.RoleAudioList)
            {
                int roleIndex = ClampRoleAudioIndex(audioIndex, role.audio.Count);
                if (roleIndex < 0) continue;
                var roleAudio = role.audio[roleIndex];
                Logger.Log($"开始下载P{page.index}配音[{role.title}]...");
                await _services.DownloadTrackAsync(
                    roleAudio.baseUrl, role.path, context.DownloadConfig,
                    false, context.CancellationToken);
                audioMaterial.Add(new AudioMaterial(role));
            }
        }

        Logger.Log($"下载P{page.index}完毕");

        if (options.DownloadComments && page.index == 1 && long.TryParse(page.aid, out var commentAid))
        {
            // 评论属于附加功能；失败时只告警，不能让页面重试重复下载已经完成的轨道。
            try
            {
                var commentsPath = Path.ChangeExtension(savePath, ".comments.json");
                Logger.Log("正在下载评论...");
                var commentPage = await CommentUtil.FetchAsync(commentAid, token: context.CancellationToken);
                await CommentUtil.SaveToJsonAsync(commentPage.Items, commentsPath);
                Logger.Log($"评论已保存: {commentsPath} ({commentPage.Items.Count} 条)");
                if (commentPage.Truncated)
                    Logger.LogWarn($"评论数量达到抓取上限（{commentPage.Items.Count} 条），可能还有更多评论未导出");
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                                        or IOException or TaskCanceledException or KeyNotFoundException or FormatException
                                        or TimeoutException or AggregateException or UnauthorizedAccessException)
            {
                Logger.LogWarn($"评论下载失败（已跳过）: {ex.Message}");
            }
        }

        if (parsedResult.IsDrm && options.DecryptDrm &&
            (!string.IsNullOrEmpty(parsedResult.KidHex) || !string.IsNullOrEmpty(parsedResult.PsshBase64)))
        {
            await _services.DecryptDrmAsync(parsedResult, videoPath, audioPath, options, context.CancellationToken);
        }

        if (!parsedResult.VideoTracks.Any()) videoPath = "";
        if (!parsedResult.AudioTracks.Any()) audioPath = "";
        if (options.SkipMux)
        {
            if (File.Exists(videoPath)) context.RelatedTask?.AddSavePath(videoPath);
            if (File.Exists(audioPath)) context.RelatedTask?.AddSavePath(audioPath);
            foreach (var audio in audioMaterial)
            {
                if (File.Exists(audio.path)) context.RelatedTask?.AddSavePath(audio.path);
            }
            return true;
        }

        Logger.Log($"开始合并音视频{(context.SubtitleInfo.Any() ? "和字幕" : "")}...");
        if (options.AudioOnly) savePath = Path.ChangeExtension(savePath, ".m4a");

        var isHevc = selectedVideo?.codecs == "HEVC";
        var muxOutcome = await BBDownDownloadUtil.RunWithPathLockAsync(
            savePath,
            () => context.Finalizer.RunAsync(new DownloadFinalizationRequest(
                UseMp4box: options.UseMP4box,
                Options: options,
                Page: page,
                VideoInfo: context.VideoInfo,
                SelectedPagesInfo: context.SelectedPagesInfo,
                ParsedResult: parsedResult,
                Description: context.Description,
                Title: context.Title,
                CoverPath: context.CoverPath,
                Lang: context.Lang,
                SubtitleInfo: context.SubtitleInfo,
                AudioMaterial: audioMaterial,
                VideoPath: videoPath,
                AudioPath: audioPath,
                SavePath: savePath,
                IsHevc: isHevc,
                VideoOnly: false,
                AudioOnly: options.AudioOnly,
                Bangumi: context.VideoInfo.IsBangumi,
                FastSkipChecked: true,
                RelatedTask: context.RelatedTask), context.CancellationToken),
            context.CancellationToken);
        if (muxOutcome == MuxOutcome.Failed)
        {
            Logger.LogError("合并失败");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(savePath)) context.RelatedTask?.AddSavePath(savePath);
        return true;
    }

    internal async Task<bool> DownloadFlvPageAsync(
        PageExecutionContext context,
        List<string> clips,
        int videoIndex)
    {
        var page = context.Page;
        var options = context.Options;
        var parsedResult = context.ParsedResult;
        var savePath = PathUtil.ResolveWorkPath(_services.FormatSavePath(
            context.SavePathFormat, context.Title,
            parsedResult.VideoTracks.ElementAtOrDefault(videoIndex), null,
            page, context.PagesCount, context.ApiType, context.PubTime));

        if (context.DownloadDanmaku)
        {
            bool? danmakuOnlyResult = await _services.DownloadDanmakuAsync(
                savePath, page, options, context.DownloadConfig, context.DownloadDanmakuFormats,
                context.RelatedTask, context.CancellationToken);
            if (danmakuOnlyResult.HasValue) return danmakuOnlyResult.Value;
        }

        if (options.CoverOnly)
        {
            return await _services.DownloadCoverOnlyAsync(
                savePath, context.Pic, page, context.DownloadConfig,
                context.RelatedTask, context.CancellationToken);
        }

        if (_services.TrySkipExistingOutput(
            savePath, page.aid, context.CoverPath, context.SubtitleInfo,
            context.RelatedTask, context.SelectedPagesInfo.Count == 1))
            return true;

        var pad = string.Empty.PadRight(clips.Count.ToString().Length, '0');
        var segmentFiles = new List<string>();
        string videoPath = "";
        for (int i = 0; i < clips.Count; i++)
        {
            videoPath = PathUtil.ResolveWorkPath(
                $"{page.aid}/{page.aid}.P{page.index}.{page.cid}.{i.ToString(pad)}.mp4");
            Logger.Log($"开始下载P{page.index}视频, 片段({(i + 1).ToString(pad)}/{clips.Count})...");
            await _services.DownloadTrackAsync(
                clips[i], videoPath, context.DownloadConfig,
                true, context.CancellationToken);
            segmentFiles.Add(videoPath);
        }

        Logger.Log($"下载P{page.index}完毕");
        Logger.Log("开始合并分段...");
        // 仅合并本次分P下载的分段，避免混入 aid 工作目录中其它分P的视频。
        videoPath = PathUtil.ResolveWorkPath($"{page.aid}/{page.aid}.P{page.index}.{page.cid}.mp4");
        await BBDownMuxer.MergeFLV(segmentFiles.ToArray(), videoPath, context.CancellationToken);
        if (options.SkipMux)
        {
            if (File.Exists(videoPath)) context.RelatedTask?.AddSavePath(videoPath);
            return true;
        }

        Logger.Log($"开始混流视频{(context.SubtitleInfo.Any() ? "和字幕" : "")}...");
        if (options.AudioOnly) savePath = Path.ChangeExtension(savePath, ".m4a");
        var muxOutcome = await BBDownDownloadUtil.RunWithPathLockAsync(
            savePath,
            () => context.Finalizer.RunAsync(new DownloadFinalizationRequest(
                UseMp4box: false,
                Options: options,
                Page: page,
                VideoInfo: context.VideoInfo,
                SelectedPagesInfo: context.SelectedPagesInfo,
                ParsedResult: parsedResult,
                Description: context.Description,
                Title: context.Title,
                CoverPath: context.CoverPath,
                Lang: context.Lang,
                SubtitleInfo: context.SubtitleInfo,
                AudioMaterial: context.AudioMaterial,
                VideoPath: videoPath,
                AudioPath: "",
                SavePath: savePath,
                IsHevc: false,
                VideoOnly: options.VideoOnly,
                AudioOnly: options.AudioOnly,
                Bangumi: context.VideoInfo.IsBangumi,
                FastSkipChecked: true,
                RelatedTask: context.RelatedTask), context.CancellationToken),
            context.CancellationToken);
        if (muxOutcome == MuxOutcome.Failed)
        {
            Logger.LogError("合并失败");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(savePath)) context.RelatedTask?.AddSavePath(savePath);
        return true;
    }

    internal static int ClampRoleAudioIndex(int audioIndex, int audioCount)
        => audioCount <= 0 ? -1 : Math.Min(Math.Max(audioIndex, 0), audioCount - 1);
}

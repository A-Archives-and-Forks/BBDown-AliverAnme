using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Entity.Entity;

using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown;

internal partial class Program
{
    private static async Task<(List<Subtitle> SubtitleInfo, bool? EarlyResult)> PreparePageAssetsAsync(
        Page page,
        MyOption options,
        string title,
        string pic,
        string savePathFormat,
        int pagesCount,
        long pubTime,
        string apiType,
        DownloadTask? relatedTask,
        CancellationToken cancellationToken)
    {
        List<Subtitle> subtitleInfo = [];
        if (options.OnlyShowInfo) return (subtitleInfo, null);

        var workAidDir = PathUtil.ResolveWorkPath(page.aid);
        if (!Directory.Exists(workAidDir)) Directory.CreateDirectory(workAidDir);

        var coverPath = PathUtil.ResolveWorkPath($"{page.aid}/{page.aid}.jpg");
        if (!options.SkipCover && !options.SubOnly && !File.Exists(coverPath) && !options.DanmakuOnly && !options.CoverOnly)
        {
            // 封面是装饰性资源：下载失败只降级为警告，不应进入页面重试循环。
            try
            {
                await BBDownDownloadUtil.DownloadFileAsync(pic == "" ? page.cover! : pic, coverPath, new BBDownDownloadUtil.DownloadConfig(), cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                Logger.LogWarn($"封面下载失败（已跳过）: {ex.Message}");
            }
        }

        bool anyProductProduced = false;
        if (!options.SkipSubtitle && !options.DanmakuOnly && !options.CoverOnly)
        {
            Logger.LogDebug("获取字幕...");
            subtitleInfo = await SubUtil.GetSubtitlesAsync(
                page.aid, page.cid, page.epid, page.index, options.UseIntlApi, cancellationToken);
            if (options.SkipAi && subtitleInfo.Any())
            {
                Logger.Log("跳过下载AI字幕");
                subtitleInfo = subtitleInfo.Where(s => !s.lan.StartsWith("ai-")).ToList();
            }

            var downloadedSubtitles = new List<Subtitle>();
            foreach (Subtitle subtitle in subtitleInfo)
            {
                Logger.Log($"下载字幕 {subtitle.lan} => {SubUtil.GetSubtitleCode(subtitle.lan).Item2}...");
                Logger.LogDebug("下载：{0}", subtitle.url);
                if (!await TryDownloadSubtitleAsync(subtitle, cancellationToken, degradeOnFailure: !options.SubOnly))
                    continue;

                downloadedSubtitles.Add(subtitle);
                if (options.SubOnly && File.Exists(subtitle.path) && await BBDownUtil.HasTextContentAsync(subtitle.path!, cancellationToken))
                {
                    var outputSubtitlePath = PathUtil.ResolveWorkPath(
                        FormatSavePath(savePathFormat, title, null, null, page, pagesCount, apiType, pubTime));
                    var directory = Path.GetDirectoryName(outputSubtitlePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    // ASS 保留 .ass 扩展名，JSON 字幕转成 SRT；语言标签来自服务器，需净化文件名。
                    var subtitleExtension = Path.GetExtension(subtitle.path)
                        .Equals(".ass", StringComparison.OrdinalIgnoreCase) ? "ass" : "srt";
                    outputSubtitlePath = Path.ChangeExtension(
                        outputSubtitlePath,
                        $".{PathUtil.GetValidFileName(subtitle.lan)}.{subtitleExtension}");
                    File.Move(subtitle.path, outputSubtitlePath, true);
                    relatedTask?.AddSavePath(outputSubtitlePath);
                    anyProductProduced = true;
                }
            }

            // 混流只接收成功落盘的字幕，避免嵌入或清理不存在的文件。
            subtitleInfo = downloadedSubtitles;
        }

        if (!options.SubOnly) return (subtitleInfo, null);

        var aidDirectory = PathUtil.ResolveWorkPath(page.aid);
        if (Directory.Exists(aidDirectory) && Directory.GetFiles(aidDirectory).Length == 0)
        {
            try { Directory.Delete(aidDirectory, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        if (!anyProductProduced)
        {
            Logger.LogWarn("SubOnly 模式未生成任何字幕文件");
            return (subtitleInfo, false);
        }

        return (subtitleInfo, true);
    }

    /// <summary>
    /// 下载并处理弹幕。返回 null 表示继续常规视频下载；DanmakuOnly 模式返回产物是否成功。
    /// DASH 与 FLV 共用此路径，避免两处弹幕清理与产物登记逻辑发生漂移。
    /// </summary>
    private static async Task<bool?> DownloadDanmakuAsync(
        string savePath,
        Page page,
        MyOption options,
        BBDownDownloadUtil.DownloadConfig downloadConfig,
        BBDownDanmakuFormat[] downloadDanmakuFormats,
        DownloadTask? relatedTask,
        CancellationToken cancellationToken)
    {
        var danmakuXmlPath = Path.ChangeExtension(savePath, ".xml");
        var danmakuAssPath = Path.ChangeExtension(savePath, ".ass");
        Logger.Log("正在下载弹幕Xml文件");
        var danmakuUrl = $"https://comment.bilibili.com/{page.cid}.xml";
        await BBDownDownloadUtil.DownloadFileAsync(danmakuUrl, danmakuXmlPath, downloadConfig, cancellationToken);
        var danmakus = DanmakuUtil.ParseXml(danmakuXmlPath);
        if (danmakus == null)
        {
            Logger.Log("弹幕Xml解析失败, 删除Xml...");
            try { File.Delete(danmakuXmlPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        else if (danmakus.Length == 0)
        {
            Logger.Log("当前视频没有弹幕, 删除Xml...");
            try { File.Delete(danmakuXmlPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        else if (downloadDanmakuFormats.Contains(BBDownDanmakuFormat.Ass))
        {
            var filtered = DanmakuUtil.Filter(danmakus, options.DanmakuFilter, options.DanmakuFilterUser);
            if (filtered.Length == 0)
            {
                Logger.Log("过滤后没有剩余弹幕, 跳过Ass保存");
            }
            else
            {
                Logger.Log($"正在保存弹幕Ass文件{(filtered.Length < danmakus.Length ? $"(过滤掉 {danmakus.Length - filtered.Length} 条)" : "")}...");
                await DanmakuUtil.SaveAsAssAsync(filtered, danmakuAssPath);
            }
        }

        // delete xml if possible
        if (!downloadDanmakuFormats.Contains(BBDownDanmakuFormat.Xml) && File.Exists(danmakuXmlPath))
        {
            try { File.Delete(danmakuXmlPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        if (!options.DanmakuOnly) return null;

        bool danmakuProduced = false;
        if (downloadDanmakuFormats.Contains(BBDownDanmakuFormat.Xml) && File.Exists(danmakuXmlPath))
        {
            relatedTask?.AddSavePath(danmakuXmlPath);
            danmakuProduced = true;
        }
        if (downloadDanmakuFormats.Contains(BBDownDanmakuFormat.Ass) && File.Exists(danmakuAssPath))
        {
            relatedTask?.AddSavePath(danmakuAssPath);
            danmakuProduced = true;
        }

        // aid 目录按稿件共享，仅清理空目录，保留中断的可续传分片和并发任务产物。
        var aidDir = PathUtil.ResolveWorkPath(page.aid);
        if (Directory.Exists(aidDir) && Directory.GetFiles(aidDir).Length == 0)
        {
            try { Directory.Delete(aidDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        if (!danmakuProduced)
        {
            Logger.LogWarn("DanmakuOnly 模式未生成任何弹幕文件");
            return false;
        }

        return true;
    }

    /// <summary>执行仅下载封面的早退路径，并记录实际产物。</summary>
    private static async Task<bool> DownloadCoverOnlyAsync(
        string savePath,
        string pic,
        Page page,
        BBDownDownloadUtil.DownloadConfig downloadConfig,
        DownloadTask? relatedTask,
        CancellationToken cancellationToken)
    {
        var coverUrl = pic == "" ? page.cover! : pic;
        if (string.IsNullOrEmpty(coverUrl))
        {
            Logger.LogWarn("CoverOnly 模式无封面资源可下载");
            return false;
        }

        var coverPath = Path.ChangeExtension(savePath, Path.GetExtension(coverUrl));
        await BBDownDownloadUtil.DownloadFileAsync(coverUrl, coverPath, downloadConfig, cancellationToken);
        var aidDir = PathUtil.ResolveWorkPath(page.aid);
        if (Directory.Exists(aidDir) && Directory.GetFiles(aidDir).Length == 0)
        {
            try { Directory.Delete(aidDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        relatedTask?.AddSavePath(coverPath);
        return true;
    }

    /// <summary>
    /// 若最终文件已存在，记录跳过结果并清理本次获取的装饰性文件。
    /// DASH 与 FLV 保留各自原有的空 aid 目录清理条件。
    /// </summary>
    private static bool TrySkipExistingOutput(
        string savePath,
        string aid,
        string coverPath,
        List<Subtitle> subtitleInfo,
        DownloadTask? relatedTask,
        bool cleanupEmptyAidDir)
    {
        if (!File.Exists(savePath) || new FileInfo(savePath).Length == 0) return false;

        Logger.Log($"{savePath}已存在, 跳过下载...");
        relatedTask?.AddSavePath(savePath);
        // 封面可能刚下载完成，短暂文件占用不应把成功跳过翻成页面重试。
        try { if (File.Exists(coverPath)) File.Delete(coverPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (var subtitle in subtitleInfo)
        {
            try { if (File.Exists(subtitle.path)) File.Delete(subtitle.path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var aidDir = PathUtil.ResolveWorkPath(aid);
        DownloadFileCleanup.DeleteResidualChapterFiles(aidDir);
        if (cleanupEmptyAidDir && Directory.Exists(aidDir) && Directory.GetFiles(aidDir).Length == 0)
        {
            try { Directory.Delete(aidDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        return true;
    }
}

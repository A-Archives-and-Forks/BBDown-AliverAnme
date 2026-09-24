using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Util;
using static BBDown.Core.Entity.Entity;

namespace BBDown;

internal enum MuxOutcome
{
    Skipped,
    Succeeded,
    Failed,
}

internal sealed record DownloadFinalizationRequest(
    bool UseMp4box,
    MyOption Options,
    Page Page,
    VInfo VideoInfo,
    List<Page> SelectedPagesInfo,
    ParsedResult ParsedResult,
    string Description,
    string Title,
    string CoverPath,
    string Lang,
    List<Subtitle> SubtitleInfo,
    List<AudioMaterial> AudioMaterial,
    string VideoPath,
    string AudioPath,
    string SavePath,
    bool IsHevc,
    bool VideoOnly,
    bool AudioOnly,
    bool Bangumi,
    bool FastSkipChecked,
    DownloadTask? RelatedTask);

/// <summary>
/// 混流、输出提交和临时轨道清理。muxer、任务工作路径和日志均由调用方注入，
/// 让该阶段可以独立构造，同时保留最终路径锁内的权威存在性检查。
/// </summary>
internal sealed class DownloadFinalizer
{
    private const int FileHandleReleaseDelayMs = 200;

    private readonly Func<DownloadFinalizationRequest, string, CancellationToken, Task<int>> _mux;
    private readonly Func<string, string> _resolveWorkPath;
    private readonly Action<string> _log;

    public DownloadFinalizer(
        Func<DownloadFinalizationRequest, string, CancellationToken, Task<int>> mux,
        Func<string, string> resolveWorkPath,
        Action<string> log)
    {
        _mux = mux ?? throw new ArgumentNullException(nameof(mux));
        _resolveWorkPath = resolveWorkPath ?? throw new ArgumentNullException(nameof(resolveWorkPath));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<MuxOutcome> RunAsync(
        DownloadFinalizationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.FastSkipChecked && File.Exists(request.SavePath) && new FileInfo(request.SavePath).Length != 0)
        {
            _log($"{request.SavePath}已存在, 跳过下载...");
            request.RelatedTask?.AddSavePath(request.SavePath);
            CleanupSupersededTracks(request);
            DownloadFileCleanup.DeleteResidualChapterFiles(_resolveWorkPath(request.Page.aid));

            var aidDir = _resolveWorkPath(request.Page.aid);
            if (Directory.Exists(aidDir) && !Directory.EnumerateFileSystemEntries(aidDir).Any())
            {
                try { Directory.Delete(aidDir, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            try { if (File.Exists(request.CoverPath)) File.Delete(request.CoverPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return MuxOutcome.Skipped;
        }

        // 写入唯一临时产物并校验成功后再原子替换最终路径，避免失败或取消留下半成品。
        // 保留 .mp4 后缀，供 mp4box 按扩展名确定 ISOM 封装格式。
        string? muxingPath = request.SavePath + $".muxing-{Guid.NewGuid():N}.mp4";
        bool muxSucceeded = false;
        try
        {
            int code = await _mux(request, muxingPath, cancellationToken);
            if (code != 0 || !File.Exists(muxingPath) || new FileInfo(muxingPath).Length == 0)
                return MuxOutcome.Failed;

            File.Move(muxingPath, request.SavePath, true);
            muxingPath = null;
            muxSucceeded = true;
            _log("清理临时文件...");
            await Task.Delay(FileHandleReleaseDelayMs, cancellationToken);
        }
        finally
        {
            if (muxingPath is not null)
            {
                try { if (File.Exists(muxingPath)) File.Delete(muxingPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            CleanupDownloadedTracks(request);
        }

        return muxSucceeded ? MuxOutcome.Succeeded : MuxOutcome.Failed;
    }

    private static void CleanupSupersededTracks(DownloadFinalizationRequest request)
    {
        if (!string.IsNullOrEmpty(request.VideoPath) && File.Exists(request.VideoPath)) try { File.Delete(request.VideoPath); } catch { }
        if (!string.IsNullOrEmpty(request.AudioPath) && File.Exists(request.AudioPath)) try { File.Delete(request.AudioPath); } catch { }
        foreach (var subtitle in request.SubtitleInfo)
            if (File.Exists(subtitle.path)) try { File.Delete(subtitle.path); } catch { }
        foreach (var audio in request.AudioMaterial)
            if (File.Exists(audio.path)) try { File.Delete(audio.path); } catch { }
    }

    private void CleanupDownloadedTracks(DownloadFinalizationRequest request)
    {
        if (request.ParsedResult.VideoTracks.Any() && File.Exists(request.VideoPath))
        {
            try { File.Delete(request.VideoPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        if (!string.IsNullOrEmpty(request.AudioPath) && request.ParsedResult.AudioTracks.Any() && File.Exists(request.AudioPath))
        {
            try { File.Delete(request.AudioPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        if (request.Page.points.Any())
        {
            var dir = Path.GetDirectoryName(string.IsNullOrEmpty(request.VideoPath) ? request.AudioPath : request.VideoPath);
            if (dir is not null) DownloadFileCleanup.DeleteResidualChapterFiles(dir);
        }

        foreach (var subtitle in request.SubtitleInfo)
        {
            try { if (File.Exists(subtitle.path)) File.Delete(subtitle.path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        foreach (var audio in request.AudioMaterial)
        {
            try { if (File.Exists(audio.path)) File.Delete(audio.path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        if (request.SelectedPagesInfo.Count == 1 ||
            request.Page.index == request.SelectedPagesInfo.Last().index ||
            request.Page.aid != request.SelectedPagesInfo.Last().aid)
        {
            try { if (File.Exists(request.CoverPath)) File.Delete(request.CoverPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var aidDir = _resolveWorkPath(request.Page.aid);
        if (Directory.Exists(aidDir))
        {
            try { if (Directory.GetFiles(aidDir).Length == 0) Directory.Delete(aidDir, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BBDown.Core.Entity;
using static BBDown.Core.Entity.Entity;

namespace BBDown;

/// <summary>一个视频下载任务所需的页面级配置。</summary>
internal sealed record DownloadPagesRequest(
    MyOption Options,
    VInfo VideoInfo,
    Dictionary<string, byte> EncodingPriority,
    Dictionary<string, int> DfnPriority,
    string? FirstEncoding,
    bool DownloadDanmaku,
    BBDownDanmakuFormat[] DownloadDanmakuFormats,
    string Input,
    string Lang,
    string AidOri,
    int Delay,
    string ApiType,
    DownloadTask? RelatedTask);

/// <summary>传给单页执行器的上下文，按任务持有筛选后的页面列表和保存模板。</summary>
internal sealed record PageDownloadRequest(
    DownloadPagesRequest Job,
    Page Page,
    List<Page> SelectedPagesInfo,
    string SavePathFormat);

/// <summary>
/// 协调分P选择、顺序下载、归档和完成通知。所有 I/O 与页面执行均由构造函数注入，
/// 因此每个任务可单独构造，不依赖 Program 静态状态。
/// </summary>
internal sealed class DownloadOrchestrator
{
    private readonly Func<MyOption, VInfo, string, List<string>?> _selectPages;
    private readonly Func<string, string, int, bool, string> _resolveSavePathFormat;
    private readonly Func<string, CancellationToken, Task<bool>> _checkArchived;
    private readonly Func<string, CancellationToken, Task> _saveArchived;
    private readonly Func<PageDownloadRequest, CancellationToken, Task<bool>> _downloadPage;
    private readonly Func<string, VInfo, bool, CancellationToken, Task> _notifyCompletion;
    private readonly Action<string> _log;
    private readonly Action<string> _logError;

    public DownloadOrchestrator(
        Func<MyOption, VInfo, string, List<string>?> selectPages,
        Func<string, string, int, bool, string> resolveSavePathFormat,
        Func<string, CancellationToken, Task<bool>> checkArchived,
        Func<string, CancellationToken, Task> saveArchived,
        Func<PageDownloadRequest, CancellationToken, Task<bool>> downloadPage,
        Func<string, VInfo, bool, CancellationToken, Task> notifyCompletion,
        Action<string> log,
        Action<string> logError)
    {
        _selectPages = selectPages ?? throw new ArgumentNullException(nameof(selectPages));
        _resolveSavePathFormat = resolveSavePathFormat ?? throw new ArgumentNullException(nameof(resolveSavePathFormat));
        _checkArchived = checkArchived ?? throw new ArgumentNullException(nameof(checkArchived));
        _saveArchived = saveArchived ?? throw new ArgumentNullException(nameof(saveArchived));
        _downloadPage = downloadPage ?? throw new ArgumentNullException(nameof(downloadPage));
        _notifyCompletion = notifyCompletion ?? throw new ArgumentNullException(nameof(notifyCompletion));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _logError = logError ?? throw new ArgumentNullException(nameof(logError));
    }

    public async Task RunAsync(DownloadPagesRequest job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<Page> pagesInfo = job.VideoInfo.PagesInfo;
        bool bangumi = job.VideoInfo.IsBangumi;
        List<string>? selectedPages = _selectPages(job.Options, job.VideoInfo, job.Input);

        // RF-81：selectedPages 最多可达 MaxExpandedPages（100k）项，直接 Join 会产生近 MB 级日志行
        //（serve 下同时写控制台与无轮转的 bbdown-api.log）。只记前 20 项 + 计数。
        _log($"共计 {pagesInfo.Count} 个分P, 已选择：" + (selectedPages == null ? "ALL" : $"{selectedPages.Count} 项 [{string.Join(",", selectedPages.Take(20))}{(selectedPages.Count > 20 ? ",…" : "")}]"));
        int pagesCount = pagesInfo.Count;

        // 分P选择最多可展开到 100k 项。HashSet 保持 List.Contains 的 ordinal 字符串语义，
        // 同时避免“分P数 × 选择数”的平方级比较；Where 仍按原视频顺序输出。
        if (selectedPages != null)
        {
            var selectedPageSet = selectedPages.ToHashSet(StringComparer.Ordinal);
            pagesInfo = pagesInfo.Where(page => selectedPageSet.Contains(page.index.ToString())).ToList();
        }

        if (pagesInfo.Count == 0)
        {
            throw new InvalidOperationException(
                $"所选分P不存在: {(selectedPages is null ? "ALL" : string.Join(",", selectedPages))}，视频共有 {pagesCount} 个分P");
        }

        // 保存路径模板按实际下载的分P数决策；番剧未完结时固定按多P处理。
        string savePathFormat = _resolveSavePathFormat(
            job.Options.FilePattern,
            job.Options.MultiFilePattern,
            pagesInfo.Count,
            bangumi && !job.VideoInfo.IsBangumiEnd);

        var failedPages = new List<int>();
        var archiveTracker = new ArchiveTracker(pagesInfo.Select(page => page.aid));
        int pageOrdinal = 0;
        foreach (Page page in pagesInfo)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageOrdinal++;
            if (pagesInfo.Count > 1 && job.Delay > 0)
            {
                _log($"停顿{job.Delay}秒...");
                await Task.Delay(job.Delay * 1000, cancellationToken);
            }

            _log($"开始解析P{page.index}: {page.aid}... ({pageOrdinal} of {pagesInfo.Count})");
            if (job.Options.SaveArchivesToFile && await _checkArchived(page.aid, cancellationToken))
            {
                _log($"aid: {page.aid}已下载过, 跳过下载...");
                archiveTracker.OnSkipped(page.aid);
                continue;
            }

            bool succeeded;
            try
            {
                succeeded = await _downloadPage(
                    new PageDownloadRequest(job, page, pagesInfo, savePathFormat), cancellationToken);
            }
            // 单 P 的网络、解析、文件系统和媒体数据异常记为失败后继续处理整批。
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or TaskCanceledException or AggregateException or FormatException or OverflowException or InvalidDataException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                _logError($"P{page.index} 下载失败: [{ex.GetType().Name}] {ex.Message}");
                failedPages.Add(page.index);
                continue;
            }

            if (job.Options.SaveArchivesToFile && archiveTracker.OnProcessed(page.aid, succeeded))
            {
                await _saveArchived(page.aid, cancellationToken);
            }

            if (!succeeded) failedPages.Add(page.index);
        }

        if (!string.IsNullOrEmpty(job.Options.NotifyWebhook))
        {
            await _notifyCompletion(job.Options.NotifyWebhook, job.VideoInfo, failedPages.Count == 0, cancellationToken);
        }

        if (failedPages.Count > 0)
        {
            throw new InvalidOperationException(
                $"共 {failedPages.Count} 个分P下载失败：P{string.Join(", P", failedPages)}");
        }

        _log("任务完成");
    }
}

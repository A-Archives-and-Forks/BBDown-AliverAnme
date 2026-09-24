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
using System.Text;
using System.Text.Json;

using BBDown.Core.Util;
namespace BBDown;

internal partial class Program
{
    /// <summary>调试日志中 JSON 响应摘要的最大字符数（防巨响应刷屏/耗内存）。</summary>
    private const int LogJsonSummaryMaxChars = 1024;

    public static Task DownloadPagesAsync(MyOption myOption, VInfo vInfo, Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority,
        string? firstEncoding, bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string lang, string aidOri, int delay, string apiType, DownloadTask? relatedTask = null, CancellationToken cancellationToken = default)
    {
        var job = new DownloadPagesRequest(
            myOption,
            vInfo,
            encodingPriority,
            dfnPriority,
            firstEncoding,
            downloadDanmaku,
            downloadDanmakuFormats,
            input,
            lang,
            aidOri,
            delay,
            apiType,
            relatedTask);

        var orchestrator = new DownloadOrchestrator(
            GetSelectedPages,
            ResolveSavePathFormat,
            CheckAidFromFileAsync,
            SaveAidToFileAsync,
            DownloadPageForOrchestratorAsync,
            NotifyCompletionAsync,
            message => Logger.Log(message),
            message => Logger.LogError(message));
        return orchestrator.RunAsync(job, cancellationToken);
    }

    private static Task<bool> DownloadPageForOrchestratorAsync(
        PageDownloadRequest request, CancellationToken cancellationToken)
    {
        var job = request.Job;
        return DownloadPageAsync(
            request.Page,
            job.Options,
            job.VideoInfo,
            request.SelectedPagesInfo,
            job.EncodingPriority,
            job.DfnPriority,
            job.FirstEncoding,
            job.DownloadDanmaku,
            job.DownloadDanmakuFormats,
            job.Input,
            request.SavePathFormat,
            job.Lang,
            job.AidOri,
            job.ApiType,
            job.RelatedTask,
            cancellationToken);
    }

    /// <summary>
    /// 下载任务完成回调：向用户配置的 webhook POST 任务结果。
    /// 失败只降级为日志，不影响下载流程本身。
    /// </summary>
    private static async Task NotifyCompletionAsync(string webhook, VInfo vInfo, bool success, CancellationToken token)
    {
        try
        {
            var payload = new NotifyPayload(
                vInfo.Title,
                vInfo.PagesInfo.Count,
                success ? "completed" : "completed-with-failures",
                DateTimeOffset.Now.ToUnixTimeSeconds());
            var json = JsonSerializer.Serialize(payload, MyOptionJsonContext.Default.NotifyPayload);
            using var req = new HttpRequestMessage(HttpMethod.Post, webhook)
            {
                Content = new StringContent(json, Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")),
            };
            using var resp = await HTTPUtil.AppHttpClient.SendAsync(req, token);
            if (!resp.IsSuccessStatusCode)
                Logger.LogWarn($"通知回调返回 HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            // 回调必须不影响下载结果：URL 畸形（UriFormatException）或内容构造失败都不能把成功任务变失败。
            // 但真正的用户取消必须向上传播：HttpClient 超时抛的 TaskCanceledException 其 token 未取消，
            // 按失败降级；token 已取消时吞掉会让"任务完成"在 Ctrl+C 后仍被打印，取消信号丢失。
            if (token.IsCancellationRequested) throw;
            Logger.LogDebug("通知回调失败: {0}", ex.Message);
        }
    }

    private static DownloadFinalizer CreateDownloadFinalizer()
        => new(MuxForFinalizerAsync, PathUtil.ResolveWorkPath, message => Logger.Log(message));

    private static DownloadPageExecutor CreateDownloadPageExecutor()
        => new(new DownloadPageExecutionServices
        {
            BackupHost = BACKUP_HOST,
            FormatSavePath = FormatSavePath,
            DownloadDanmakuAsync = DownloadDanmakuAsync,
            DownloadCoverOnlyAsync = DownloadCoverOnlyAsync,
            TrySkipExistingOutput = TrySkipExistingOutput,
            PrintSelectedTrackInfo = PrintSelectedTrackInfo,
            HandlePcdn = HandlePcdn,
            DownloadTrackAsync = DownloadTrackAsync,
            DecryptDrmAsync = DecryptDrmAsync,
        });

    private static Task<int> MuxForFinalizerAsync(
        DownloadFinalizationRequest request, string outputPath, CancellationToken cancellationToken)
    {
        var page = request.Page;
        string episodeId = request.SelectedPagesInfo.Count > 1 ||
            (request.Bangumi && !request.VideoInfo.IsBangumiEnd) ? page.title : "";
        string coverPath = File.Exists(request.CoverPath) ? request.CoverPath : "";
        return BBDownMuxer.MuxAV(
            request.UseMp4box,
            page.bvid,
            request.VideoPath,
            request.AudioPath,
            request.AudioMaterial,
            outputPath,
            request.Description,
            request.Title,
            page.ownerName ?? "",
            episodeId,
            coverPath,
            request.Lang,
            request.SubtitleInfo,
            request.AudioOnly,
            request.VideoOnly,
            page.points,
            page.pubTime,
            request.Options.SimplyMux,
            request.IsHevc,
            cancellationToken);
    }

    /// <summary>
    /// 下载单个分P。返回 false 表示该分P最终失败，供调用方避免将其记为已完成。
    /// </summary>
    private static async Task<bool> DownloadPageAsync(Page p, MyOption myOption, VInfo vInfo, List<Page> selectedPagesInfo, Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority,
        string? firstEncoding, bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, string apiType, DownloadTask? relatedTask = null, CancellationToken cancellationToken = default)
    {
        string desc = string.IsNullOrEmpty(p.desc) ? vInfo.Desc : p.desc;
        // 补零宽度用"全部分P总数"而非筛选后的数量：单独下载 P1（-p 1）与稍后下载
        // 全部分P（-p all）时，<pageNumberWithZero> 应产生相同宽度的文件名，
        // 否则同一视频因筛选方式不同会得到不同路径（P01 vs P1）。
        var pagesCount = vInfo.PagesInfo.Count;
        List<Subtitle> subtitleInfo = [];
        string title = vInfo.Title;
        string pic = vInfo.Pic;
        long pubTime = vInfo.PubTime;
        bool selected = false; //用户是否已经手动选择过了轨道
        int retryCount = 0;
        // 页面级重试次数与间隔尊重 --retry-count / --retry-delay（Options.cs 已校验
        // 1~100 / 0~600000）：此前硬编码 3 与 3000ms，用户配置完全被无视。
        int maxRetry = myOption.RetryCount;
        var pageExecutor = CreateDownloadPageExecutor();
        try
        {
            while (retryCount < maxRetry)
            {
                try
                {
                    Logger.LogDebug("尝试获取章节信息...");
                    p.points = await BBDownUtil.FetchPointsAsync(p.cid, p.aid, cancellationToken);

                    // 工作区路径（分P 的 aid 目录）统一基于任务流工作目录解析为绝对路径：
                    // serve 下不写进程 CWD，相对路径必须经 PathUtil.ResolveWorkPath 落到
                    // Config.Current.WorkDir，否则并发任务各自 --work-dir 的文件会互相错位。
                    var coverPath = PathUtil.ResolveWorkPath($"{p.aid}/{p.aid}.jpg");

                    //处理文件夹以.结尾导致的异常情况
                    if (title.EndsWith('.')) title += "_fix";
                    //处理文件夹以.开头导致的异常情况
                    if (title.StartsWith('.')) title = "_" + title;

                    var pageAssets = await PreparePageAssetsAsync(
                        p, myOption, title, pic, savePathFormat, pagesCount, pubTime, apiType, relatedTask, cancellationToken);
                    subtitleInfo = pageAssets.SubtitleInfo;
                    if (pageAssets.EarlyResult.HasValue) return pageAssets.EarlyResult.Value;
                    //调用解析
                    ParsedResult parsedResult = await Parser.ExtractTracksAsync(aidOri, p.aid, p.cid, p.epid, myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi, firstEncoding!, myOption.DecryptDrm, token: cancellationToken);
                    List<AudioMaterial> audioMaterial = [];
                    if (!p.points.Any())
                    {
                        p.points = parsedResult.ExtraPoints;
                    }

                    var previewPolicy = ApplyPreviewPolicy(vInfo, p, parsedResult, myOption, title);
                    title = previewPolicy.Title;
                    if (!previewPolicy.ShouldContinue) return false;

                    await WriteDebugParseResponseAsync(parsedResult, cancellationToken);

                    var downloadConfig = new BBDownDownloadUtil.DownloadConfig()
                    {
                        UseAria2c = myOption.UseAria2c,
                        Aria2cArgs = myOption.Aria2cArgs,
                        ForceHttp = myOption.ForceHttp,
                        MultiThread = myOption.MultiThread,
                        RelatedTask = relatedTask,
                    };

                    var executionContext = new PageExecutionContext
                    {
                        Page = p,
                        Options = myOption,
                        VideoInfo = vInfo,
                        SelectedPagesInfo = selectedPagesInfo,
                        ParsedResult = parsedResult,
                        Description = desc,
                        Title = title,
                        Pic = pic,
                        CoverPath = coverPath,
                        Lang = lang,
                        SubtitleInfo = subtitleInfo,
                        AudioMaterial = audioMaterial,
                        DownloadConfig = downloadConfig,
                        Finalizer = CreateDownloadFinalizer(),
                        DownloadDanmaku = downloadDanmaku,
                        DownloadDanmakuFormats = downloadDanmakuFormats,
                        SavePathFormat = savePathFormat,
                        PagesCount = pagesCount,
                        ApiType = apiType,
                        PubTime = pubTime,
                        RelatedTask = relatedTask,
                        CancellationToken = cancellationToken,
                    };
                    var dashPreparation = PrepareDashTracks(parsedResult, myOption, p, encodingPriority, dfnPriority);
                    if (dashPreparation.EarlyResult.HasValue) return dashPreparation.EarlyResult.Value;

                    if (dashPreparation.IsDash)
                    {
                        int videoIndex = 0;
                        int audioIndex = 0;
                        if (myOption.Interactive && !selected)
                        {
                            SelectTrackManually(parsedResult, ref videoIndex, ref audioIndex);
                            selected = true;
                        }

                        return await pageExecutor.DownloadDashPageAsync(executionContext, videoIndex, audioIndex);
                    }
                    else if (parsedResult.Clips.Any() && parsedResult.Dfns.Any())
                    {
                        var flvPreparation = await PrepareFlvTracksAsync(
                            parsedResult, p, myOption, aidOri, firstEncoding, encodingPriority, dfnPriority, selected, cancellationToken);
                        parsedResult = flvPreparation.ParsedResult;
                        executionContext.ParsedResult = parsedResult;
                        selected = flvPreparation.Selected;
                        if (flvPreparation.EarlyResult.HasValue) return flvPreparation.EarlyResult.Value;

                        return await pageExecutor.DownloadFlvPageAsync(
                            executionContext, flvPreparation.Clips, flvPreparation.VideoIndex);
                    }
                    else
                    {
                        // 无可用轨道（既非 DASH 也非 FLV）→ 解析失败，不能报告假成功。
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
                        // 完整播放 JSON 可能含带签名的媒体地址；只记录长度和摘要，避免临时 URL 泄漏。
                        var webJson = parsedResult.WebJsonString;
                        if (Config.Current.DebugLog)
                            Logger.LogDebug("WebJson {0} chars: {1}",
                                webJson.Length,
                                webJson.Length > LogJsonSummaryMaxChars ? webJson[..LogJsonSummaryMaxChars] + "…" : webJson);
                        return false;
                    }
                }
                // FormatException/OverflowException（RF-31）：SortTracks 的服务器可控 id
                // 解析（已改 TryParse 兜底）与各类 Convert 调用可能抛出——按"单 P 失败"
                // 隔离重试，不让异常逃逸中止整批。
                // UnauthorizedAccessException（RF-44）：与页面级过滤器同步扩充（只读属性/
                // 受控文件夹访问/ACL 拒写等本地权限错误）。
                // InvalidDataException（RF-72）：RF-28/RF-51 的 64MB 上限与 gRPC 帧校验抛型，
                // 不在白名单内会穿透中止整批——补入后按单 P 失败重试。
                catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or AggregateException or FormatException or OverflowException or InvalidDataException
                                  || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    // 风控页（200+HTML 的 RiskControlResponseException，继承 JsonException）也参与
                    // 页面级重试：B 站 windmill/风控页常为瞬时故障（数秒到一分钟内自动解除），
                    // 重试可在解除后恢复，且次数受 --retry-count 约束。装饰性资源（字幕/封面/
                    // 评论）的抓取失败已在各自调用点降级为警告，不会进入此 catch 触发整页重下。
                    // 超时（HttpClient 超时抛的 TaskCanceledException 其 token 未取消）同样参与重试；
                    // 真正的用户取消（token 已取消）不重试，直接向上传播。
                    retryCount++;
                    if (retryCount >= maxRetry)
                    {
                        Logger.LogError($"下载尝试 {retryCount} 次后仍失败，最后错误: [{ex.GetType().Name}] {ex.Message}");
                        throw;
                    }
                    // 与轨道级重试一致：退避基数 retryCount * RetryDelayMs 线性放大
                    //（默认 3000ms → 首次失败 3s、二次 6s...），符合 --retry-delay 的"基础毫秒数"语义
                    int backoffMs = retryCount * myOption.RetryDelay;
                    Logger.LogError($"[{ex.GetType().Name}] {ex.Message}");
                    Logger.LogWarn($"下载出现异常, {backoffMs / 1000.0:0.#} 秒后将进行自动重试...");
                    await Task.Delay(backoffMs, cancellationToken);
                }
            }
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户取消/服务关停：清理当前分P工作目录中"确定非续传资产"的残留
            //（空 aid 目录 + 无清单死 .tmp），保留 .vclip/.aclip 与带有效清单的
            // .tmp——它们是跨进程断点续传资产，无脑删除会让中断的文件无法续传。
            // 清理后原样向上传播取消。
            CleanNonResumableWorkArtifacts(p.aid);
            throw;
        }
    }

    /// <summary>
    /// 取消路径的定位清理：只删"确定不是续传资产"的残留。
    /// - aid 目录为空 → 删目录（镜像成功路径的删空目录语义）；
    /// - 无对应 *.manifest.json 的 *.tmp → 删（此类必被 CanResumeFrom 拒绝、下次运行
    ///   也会删，是纯死重）；
    /// 绝不删 .vclip/.aclip 与带有效清单的 .tmp。aid 目录跨分P共享，取消某 P 不能清
    /// 其它 P 的续传资产，故按文件粒度而非整体删目录。
    /// </summary>
    private static void CleanNonResumableWorkArtifacts(string aid)
    {
        try
        {
            var dir = PathUtil.ResolveWorkPath(aid);
            if (!Directory.Exists(dir)) return;
            foreach (var tmp in Directory.GetFiles(dir, "*.tmp"))
            {
                // 清单伴生文件是 xxx.tmp.manifest.json；无清单的 .tmp 是 WriteResumeManifest
                // 失败窗口或取消竞态留下的死文件，续传必被拒，清理掉避免累积。
                if (!File.Exists(tmp + ".manifest.json"))
                {
                    try { File.Delete(tmp); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 占用时跳过，下次再清 */ }
                }
            }
            if (Directory.GetFiles(dir).Length == 0)
            {
                try { Directory.Delete(dir, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 目录被占用时跳过 */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogDebug("取消清理工作目录失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 下载单条字幕文件。默认（非 SubOnly）字幕是装饰性资源：任何失败（含过期签名 URL
    /// 返回 200+HTML 风控页的 <see cref="RiskControlResponseException"/>，继承 JsonException）
    /// 都只降级为警告并返回 false，由调用方跳过该条字幕——绝不进入页面级重试或中止整批。
    /// SubOnly 模式下字幕是唯一产物（<paramref name="degradeOnFailure"/> = false），失败应
    /// 抛出交由页面级重试恢复。真正的用户取消（token 已取消）向上传播。
    /// </summary>
    internal static async Task<bool> TryDownloadSubtitleAsync(Subtitle s, CancellationToken token, bool degradeOnFailure = true)
    {
        try
        {
            await SubUtil.SaveSubtitleAsync(s.url, s.path, token);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException or InvalidOperationException or TimeoutException)
        {
            // HttpClient 超时抛的 TaskCanceledException 其 token 未取消，仍按字幕降级处理；
            // HTTPUtil 重试耗尽后抛的 TimeoutException（内部包裹 OCE）同属瞬时故障：字幕是装饰性
            // 资源，持续超时也应降级为"无字幕"而非穿透到页面级重试击沉主下载（E1）。
            // 真正的取消必须向上传播中止下载，不能吞掉后继续执行无可取消的网络调用。
            if (token.IsCancellationRequested) throw;
            if (!degradeOnFailure) throw;
            Logger.LogWarn($"字幕 {s.lan} 下载失败（已跳过）: {ex.Message}");
            return false;
        }
    }

}

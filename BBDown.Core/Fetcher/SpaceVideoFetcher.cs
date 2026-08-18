using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;
using System.Xml;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Fetcher;

/// <summary>
/// UP 主投稿解析
/// https://space.bilibili.com/{mid}
///
/// 投稿列表接口不返回 cid，而 Page 必须要有，因此需要逐个请求视频详情来展开分P。
/// 这意味着一次解析会发出「投稿数」量级的请求，风控与瞬时故障都必须认真处理。
/// </summary>
public class SpaceVideoFetcher : IFetcher
{
    private const int PageSize = 50;

    /// <summary>
    /// 连续失败到该次数即判定为系统性故障（风控、Cookie 失效、网络中断）而非个别稿件失效。
    /// 继续跑下去只会加重风控，且会把大面积失败伪装成"部分成功"。
    /// </summary>
    private const int ConsecutiveFailureLimit = 5;

    /// <summary>逐个取详情之间的最小间隔，避免连续数百次请求触发频率风控。</summary>
    private static readonly TimeSpan DetailRequestInterval = TimeSpan.FromMilliseconds(120);

    public async Task<VInfo> FetchAsync(string id, CancellationToken cancellationToken = default)
    {
        id = id[4..];
        // 该接口按 B 站惯例需要设备标识，未登录时 Cookie 为空。
        // EnsureAsync 返回注入后的新 Cookie，由本方法在其流程内显式应用
        // （AsyncLocal 写入不会自动回流，见 BuvidProvider 说明）——本方法后续请求
        // 读取 Config.Current.Cookie 时才能带上 buvid3。
        var updatedCookie = await BuvidProvider.EnsureAsync(cancellationToken);
        if (updatedCookie is not null) Core.Config.COOKIE_FLOW = updatedCookie;
        // using the live API can bypass w_rid
        string userInfoApi = $"https://api.live.bilibili.com/live_user/v1/Master/info?uid={id}";
        using var userDoc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(userInfoApi, token: cancellationToken));
        string userName = userDoc.RootElement.GetPropertySafe("data").GetPropertySafe("info").GetValueAsStringSafe("uname");
        if (string.IsNullOrWhiteSpace(userName)) userName = $"UP主{id}";

        var entries = await FetchAllEntriesAsync(id, userName, cancellationToken);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"{userName} 没有投稿视频");
        }
        Logger.Log($"共 {entries.Count} 个投稿, 正在获取分P信息...");

        var (pagesInfo, steinGate) = await ExpandEntriesAsync(entries, userName, cancellationToken);

        return new VInfo
        {
            // 只有一个分P时下游会走单P命名模板 <videoTitle>，此时该字段就是最终文件名，
            // 用 UP 主昵称会把真正的视频标题弄丢
            Title = (pagesInfo.Count == 1 ? pagesInfo[0].title : userName).Trim(),
            Desc = $"{userName} 的投稿视频",
            Pic = "",
            PubTime = entries[0].PubTime,
            PagesInfo = pagesInfo,
            IsBangumi = false,
            // 任一投稿是互动视频就要标记，否则 Workflow 中"互动视频不支持 TV API"的
            // 自动降级永远不会触发
            IsSteinGate = steinGate,
        };
    }

    private static async Task<(List<Page> pages, bool steinGate)> ExpandEntriesAsync(
        List<SpaceEntry> entries, string userName, CancellationToken cancellationToken)
    {
        var pagesInfo = new List<Page>();
        var index = 1;
        var failures = new List<(string Aid, string Title, string Reason)>();
        var consecutiveFailures = 0;
        var steinGate = false;
        string? lastSuccessAid = null;
        // 节流进度：逐稿展开可能持续数十分钟（千稿 × 120ms 间隔），期间完全静默会让
        // 用户误以为卡死。每处理 20 个或每 5 秒报一次进度，不逐条刷屏。
        var lastProgressLog = DateTimeOffset.MinValue;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (i > 0) await Task.Delay(DetailRequestInterval, cancellationToken);
            if (i > 0 && (i % 20 == 0 || (DateTimeOffset.UtcNow - lastProgressLog).TotalSeconds >= 5))
            {
                Logger.Log($"已展开 {i}/{entries.Count} 个投稿");
                lastProgressLog = DateTimeOffset.UtcNow;
            }

            try
            {
                var detail = await new NormalInfoFetcher().FetchAsync(entry.Aid, cancellationToken);
                consecutiveFailures = 0;
                steinGate |= detail.IsSteinGate;
                lastSuccessAid = entry.Aid;

                var multiPage = detail.PagesInfo.Count > 1;
                foreach (var page in detail.PagesInfo)
                {
                    pagesInfo.Add(new Page(index++, page)
                    {
                        // 多P稿件把分P标题并进去，避免同一投稿的多个分P重名
                        title = multiPage ? $"{entry.Title}_P{page.index}_{page.title}" : entry.Title,
                        cover = string.IsNullOrEmpty(detail.Pic) ? entry.Cover : detail.Pic,
                        desc = entry.Description,
                    });
                }
            }
            // 真正的用户取消必须向上传播中止整个流程——否则取消被吞成"某个稿件失败"，
            // 批量解析会在取消信号下继续空转（还可能触发连续失败/风控）。
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            // 过滤范围要覆盖所有"单个稿件"级别的故障：稿件失效（InvalidOperationException）、
            // 网络瞬断（HttpRequestException/IOException）、请求超时（TaskCanceledException，
            // HttpClient 超时抛的正是它而非 HttpRequestException）、响应结构异常。
            catch (Exception ex) when (ex is HttpRequestException or JsonException or XmlException
                                          or IOException or KeyNotFoundException
                                          or InvalidOperationException or TaskCanceledException)
            {
                failures.Add((entry.Aid, entry.Title, ex.Message));
                consecutiveFailures++;

                // 连续失败说明问题不在个别稿件上，继续跑既拿不到结果又会加重风控。
                // 异常带上"已成功展开数量 + 最后一个成功 av"上下文：批量解析中途中止后，
                // 用户可据此定位断点（从该 av 附近手动续跑），不必从头重试数千次。
                if (consecutiveFailures >= ConsecutiveFailureLimit)
                {
                    throw new InvalidOperationException(
                        $"连续 {consecutiveFailures} 个投稿解析失败，判定为风控或登录状态失效，已中止。" +
                        $"\n已成功展开 {pagesInfo.Count}/{entries.Count} 个投稿" +
                        (lastSuccessAid is null ? "" : $"，最后一个成功 av{lastSuccessAid}") +
                        $"。\n最后一次失败：av{entry.Aid} - {ex.Message}", ex);
                }
            }
        }

        ReportFailures(failures, entries.Count, userName);

        if (pagesInfo.Count == 0)
        {
            throw new InvalidOperationException($"{userName} 的投稿均无法解析，请检查登录状态或稍后重试");
        }

        return (pagesInfo, steinGate);
    }

    /// <summary>
    /// 汇报被跳过的投稿。失败详情必须走 LogWarn 而非 LogDebug：
    /// --debug 只能事先指定，事后无从补救，用户会拿不到任何可定位的信息。
    /// </summary>
    private static void ReportFailures(
        List<(string Aid, string Title, string Reason)> failures, int total, string userName)
    {
        if (failures.Count == 0) return;

        Logger.LogWarn($"{userName} 的 {total} 个投稿中有 {failures.Count} 个无法解析，已跳过：");
        const int shown = 10;
        foreach (var f in failures.Take(shown))
        {
            Logger.LogWarn($"  av{f.Aid} {f.Title} —— {f.Reason}", time: false);
        }
        if (failures.Count > shown)
        {
            Logger.LogWarn($"  ...另有 {failures.Count - shown} 个，完整列表见 --debug 日志", time: false);
        }
        foreach (var f in failures.Skip(shown))
        {
            Logger.LogDebug("跳过投稿 av{0}（{1}）: {2}", f.Aid, f.Title, f.Reason);
        }
    }

    private readonly record struct SpaceEntry(string Aid, string Title, string Cover, string Description, long PubTime);

    private static async Task<List<SpaceEntry>> FetchAllEntriesAsync(string mid, string userName, CancellationToken cancellationToken)
    {
        var entries = new List<SpaceEntry>();
        // 分页是按偏移量取的，翻页期间 UP 主新增投稿会让边界条目在相邻两页各出现一次。
        // FavListFetcher/MediaListFetcher/SeriesListFetcher 用 HashSet 对翻页去重做同样的防护。
        var seen = new HashSet<string>();
        var pageNumber = 1;

        var (first, totalCount) = await FetchPageAsync(pageNumber, mid, cancellationToken);
        AddNew(entries, seen, first);

        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"未获取到 {userName} 的任何投稿视频");
        }

        var totalPage = (int)Math.Ceiling((double)totalCount / PageSize);
        while (pageNumber < totalPage)
        {
            pageNumber++;
            // 超大 UP 主可能数十页：逐页提示推进，避免翻页阶段看似卡死
            Logger.Log($"正在获取第 {pageNumber}/{totalPage} 页投稿列表...");
            var (more, _) = await FetchPageAsync(pageNumber, mid, cancellationToken);
            // 空页意味着接口提前结束（翻页上限、风控降级、条目在翻页间被删减）。
            // 继续把剩余页请求完既拿不到数据，又会平白加重风控。
            if (more.Count == 0)
            {
                Logger.LogWarn($"第 {pageNumber} 页未返回任何投稿，停止翻页（已取到 {entries.Count}/{totalCount} 个）");
                break;
            }
            AddNew(entries, seen, more);
        }

        // 声称的总数与实际取到的数量不符时必须出声，否则用户会把残缺列表当成完整结果
        if (totalCount > 0 && entries.Count < totalCount)
        {
            Logger.LogWarn($"接口声称共 {totalCount} 个投稿，实际只取到 {entries.Count} 个");
        }

        return entries;
    }

    private static void AddNew(List<SpaceEntry> target, HashSet<string> seen, List<SpaceEntry> incoming)
    {
        foreach (var e in incoming)
        {
            if (seen.Add(e.Aid)) target.Add(e);
        }
    }

    private static async Task<(List<SpaceEntry> entries, int totalCount)> FetchPageAsync(int pageNumber, string mid, CancellationToken cancellationToken)
    {
        var api = Parser.WbiSign($"mid={mid}&order=pubdate&pn={pageNumber}&ps={PageSize}&tid=0&wts={ServerClock.NowUnixSeconds()}");
        api = $"https://api.bilibili.com/x/space/wbi/arc/search?{api}";
        var json = await FetchSpaceListAsync(api, cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetPropertySafe("data");
        var entries = new List<SpaceEntry>();
        foreach (var item in data.GetPropertySafe("list").EnumerateArraySafe("vlist"))
        {
            entries.Add(new SpaceEntry(
                item.GetValueAsStringSafe("aid"),
                item.GetValueAsStringSafe("title"),
                item.GetValueAsStringSafe("pic"),
                item.GetValueAsStringSafe("description"),
                item.GetInt64Safe("created")));
        }

        var pageProp = data.TryGetPropertySafe("page");
        var totalCount = pageProp?.GetInt32Safe("count", -1) ?? -1;
        if (totalCount < 0)
        {
            throw new InvalidOperationException("获取 UP 主投稿列表失败: 响应中缺少有效的 page.count 字段");
        }
        return (entries, totalCount);
    }

    /// <summary>
    /// 拉取投稿列表。该接口要求登录，未登录时的失败有两种表现形式：
    /// 直接返回 HTTP 412，或返回 200 但 body 为 <c>{"code":-352,"data":{"v_voucher":...}}</c>。
    /// 两者的原始报错（"412 Precondition Failed" 加长串堆栈 / "JSON property not found: 'list'"）
    /// 对用户都没有可操作信息，这里统一转成明确提示。
    /// </summary>
    private static async Task<string> FetchSpaceListAsync(string api, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await HTTPUtil.GetWebSourceAsync(api, token: cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            throw new InvalidOperationException(BlockedMessage("HTTP 412"), ex);
        }

        using var doc = JsonDocument.Parse(json);
        var code = doc.RootElement.GetPropertySafe("code").GetInt32();
        if (code != 0)
        {
            var message = doc.RootElement.GetValueAsStringSafe("message");
            // -352 风控校验失败 / -403 访问权限不足。实测未登录必然触发，
            // 但已登录时也可能因请求过频或 UP 主设置了投稿不公开而出现，
            // 因此两种可能都要给出。
            throw new InvalidOperationException(code is -352 or -403
                ? BlockedMessage($"code={code}")
                : $"获取 UP 主投稿列表失败(code={code}): {message}");
        }

        return json;
    }

    private static string BlockedMessage(string detail) =>
        $"获取 UP 主投稿列表失败（{detail}）。该接口要求登录后访问：" +
        "若尚未登录，请先运行 BBDown login 扫码登录，或通过 --cookie 传入已登录的 Cookie；" +
        "若已登录，通常是请求过于频繁触发风控，或该 UP 主设置了投稿不公开，请稍后再试。";
}

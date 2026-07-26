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

    public async Task<VInfo> FetchAsync(string id)
    {
        id = id[4..];
        // 该接口按 B 站惯例需要设备标识，未登录时 Cookie 为空
        await BuvidProvider.EnsureAsync();
        // using the live API can bypass w_rid
        string userInfoApi = $"https://api.live.bilibili.com/live_user/v1/Master/info?uid={id}";
        using var userDoc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(userInfoApi));
        string userName = userDoc.RootElement.GetPropertySafe("data").GetPropertySafe("info").GetValueAsStringSafe("uname");
        if (string.IsNullOrWhiteSpace(userName)) userName = $"UP主{id}";

        var entries = await FetchAllEntriesAsync(id, userName);
        Logger.Log($"共 {entries.Count} 个投稿, 正在获取分P信息...");

        var (pagesInfo, steinGate) = await ExpandEntriesAsync(entries, userName);

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
        List<SpaceEntry> entries, string userName)
    {
        var pagesInfo = new List<Page>();
        var index = 1;
        var failures = new List<(string Aid, string Title, string Reason)>();
        var consecutiveFailures = 0;
        var steinGate = false;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (i > 0) await Task.Delay(DetailRequestInterval);

            try
            {
                var detail = await new NormalInfoFetcher().FetchAsync(entry.Aid);
                consecutiveFailures = 0;
                steinGate |= detail.IsSteinGate;

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
            // 过滤范围要覆盖所有"单个稿件"级别的故障：稿件失效（InvalidOperationException）、
            // 网络瞬断（HttpRequestException/IOException）、请求超时（TaskCanceledException，
            // HttpClient 超时抛的正是它而非 HttpRequestException）、响应结构异常。
            catch (Exception ex) when (ex is HttpRequestException or JsonException or XmlException
                                          or IOException or KeyNotFoundException
                                          or InvalidOperationException or TaskCanceledException)
            {
                failures.Add((entry.Aid, entry.Title, ex.Message));
                consecutiveFailures++;

                // 连续失败说明问题不在个别稿件上，继续跑既拿不到结果又会加重风控
                if (consecutiveFailures >= ConsecutiveFailureLimit)
                {
                    throw new InvalidOperationException(
                        $"连续 {consecutiveFailures} 个投稿解析失败，判定为风控或登录状态失效，已中止。" +
                        $"最后一次失败：av{entry.Aid} - {ex.Message}", ex);
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

    private static async Task<List<SpaceEntry>> FetchAllEntriesAsync(string mid, string userName)
    {
        var entries = new List<SpaceEntry>();
        // 分页是按偏移量取的，翻页期间 UP 主新增投稿会让边界条目在相邻两页各出现一次。
        // FavListFetcher 用 pagesInfo.Contains 做同样的防护。
        var seen = new HashSet<string>();
        var pageNumber = 1;

        var (first, totalCount) = await FetchPageAsync(pageNumber, mid);
        AddNew(entries, seen, first);

        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"未获取到 {userName} 的任何投稿视频");
        }

        var totalPage = (int)Math.Ceiling((double)totalCount / PageSize);
        while (pageNumber < totalPage)
        {
            pageNumber++;
            var (more, _) = await FetchPageAsync(pageNumber, mid);
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

    private static async Task<(List<SpaceEntry> entries, int totalCount)> FetchPageAsync(int pageNumber, string mid)
    {
        var api = Parser.WbiSign($"mid={mid}&order=pubdate&pn={pageNumber}&ps={PageSize}&tid=0&wts={DateTimeOffset.Now.ToUnixTimeSeconds()}");
        api = $"https://api.bilibili.com/x/space/wbi/arc/search?{api}";
        var json = await FetchSpaceListAsync(api);

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

        // 用 GetInt32() 而非 GetInt32Safe()：总数控制着翻页上界，
        // 静默退化为 0 会让循环直接结束，只拿到第一页却毫无征兆。
        // FavListFetcher 对同类字段也是这么做的。
        var totalCount = data.GetPropertySafe("page").GetPropertySafe("count").GetInt32();
        return (entries, totalCount);
    }

    /// <summary>
    /// 拉取投稿列表。该接口要求登录，未登录时的失败有两种表现形式：
    /// 直接返回 HTTP 412，或返回 200 但 body 为 <c>{"code":-352,"data":{"v_voucher":...}}</c>。
    /// 两者的原始报错（"412 Precondition Failed" 加长串堆栈 / "JSON property not found: 'list'"）
    /// 对用户都没有可操作信息，这里统一转成明确提示。
    /// </summary>
    private static async Task<string> FetchSpaceListAsync(string api)
    {
        string json;
        try
        {
            json = await HTTPUtil.GetWebSourceAsync(api);
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

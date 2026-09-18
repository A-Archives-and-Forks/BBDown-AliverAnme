using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 列表解析
/// https://space.bilibili.com/23630128/channel/seriesdetail?sid=340933
/// </summary>
public class SeriesListFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id, CancellationToken cancellationToken = default)
    {
        //套用BBDownMediaListFetcher.cs的代码
        //只修改id = id.Substring(12);以及api地址的type=5
        id = id[12..];
        var api = $"https://api.bilibili.com/x/v1/medialist/info?type=5&biz_id={id}&tid=0";
        var json = await HTTPUtil.GetWebSourceAsync(api, token: cancellationToken);
        using var infoJson = JsonDocument.Parse(json);
        var infoRoot = infoJson.RootElement;
        // RF-52：先查 code 再取 data——错误响应（{"code":-400,...} 无 data 键）经 GetPropertySafe
        // 抛英文裸 KeyNotFoundException，精心编写的 code 诊断不可达。与 NormalInfoFetcher 等
        // "先查 code"的正确序对齐。
        if (!(infoRoot.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object))
        {
            var code = infoRoot.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            var message = infoRoot.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString() : "未知错误";
            throw new InvalidOperationException($"获取系列信息失败(code={code}): {JsonElementExtensions.SanitizeServerText(message)}");
        }
        var listTitle = data.GetValueAsStringSafe("title");
        var intro = data.GetValueAsStringSafe("intro");
        long pubTime = data.GetInt64Safe("ctime");

        List<Page> pagesInfo = new();
        // 翻页去重集合：Contains 是 O(n)，翻页几十页时 O(n²) 拖慢解析；Page 已实现
        // Equals/GetHashCode（按 aid+cid+epid），HashSet 去重与 Contains 语义一致。
        HashSet<Page> seenPages = new();
        bool hasMore = true;
        var oid = "";
        int index = 1;
        while (hasMore)
        {
            var listApi = $"https://api.bilibili.com/x/v2/medialist/resource/list?type=5&oid={oid}&otype=2&biz_id={id}&bvid=&with_current=true&mobi_app=web&ps=20&direction=false&sort_field=1&tid=0&desc=true";
            json = await HTTPUtil.GetWebSourceAsync(listApi, token: cancellationToken);
            using var listJson = JsonDocument.Parse(json);
            var listRoot = listJson.RootElement;
            // RF-52：先查 code 再取 data（与首屏一致，错误响应不再抛裸 KeyNotFoundException）。
            if (!(listRoot.TryGetProperty("data", out var listData) && listData.ValueKind == JsonValueKind.Object))
            {
                var code = listRoot.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                var message = listRoot.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString() : "未知错误";
                throw new InvalidOperationException($"获取系列分页列表失败(code={code}): {JsonElementExtensions.SanitizeServerText(message)}");
            }
            data = listData;
            hasMore = data.GetBooleanSafe("has_more");
            // 游标必须记录本页最后一条 id，无论是否被 attr 过滤；否则整页失效时
            // oid 不推进，重复请求同一页造成死循环。
            var previousOid = oid;
            foreach (var m in data.EnumerateArraySafe("media_list"))
            {
                oid = m.GetValueAsStringSafe("id");

                // 只处理未失效的视频条目（与收藏夹解析逻辑保持一致）
                if (m.GetInt32Safe("attr") != 0)
                    continue;

                var pageCount = m.GetInt32Safe("page");
                var desc = m.GetValueAsStringSafe("intro");
                var upperElem = m.TryGetPropertySafe("upper");
                var ownerName = upperElem?.GetValueAsStringSafe("name") ?? "";
                var ownerMid = upperElem?.GetValueAsStringSafe("mid") ?? "";
                foreach (var page in m.EnumerateArraySafe("pages"))
                {
                    Page p = new(index++,
                        m.GetValueAsStringSafe("id"),
                        page.GetValueAsStringSafe("id"),
                        "", //epid
                        pageCount == 1 ? m.GetValueAsStringSafe("title") : $"{m.GetValueAsStringSafe("title")}_P{page.GetValueAsStringSafe("page")}_{page.GetValueAsStringSafe("title")}", //单P使用外层标题 多P则拼接内层子标题
                        page.GetInt32Safe("duration"),
                        page.TryGetProperty("dimension", out var dim) && dim.TryGetProperty("width", out var w) && dim.TryGetProperty("height", out var h) ? $"{w}x{h}" : "",
                        m.GetInt64Safe("pubtime"),
                        m.GetValueAsStringSafe("cover"),
                        desc,
                        ownerName,
                        ownerMid);
                    if (seenPages.Add(p)) pagesInfo.Add(p);
                    else index--;
                }
            }

            if (hasMore && oid == previousOid)
            {
                Logger.LogDebug("系列翻页游标未推进（oid={0}），停止翻页", oid);
                break;
            }
        }

        var info = new VInfo
        {
            Title = listTitle.Trim(),
            Desc = intro.Trim(),
            Pic = "",
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = false
        };

        return info;
    }
}

using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Fetcher;

/// <summary>
/// 合集解析
/// https://space.bilibili.com/23630128/channel/collectiondetail?sid=2045
/// https://www.bilibili.com/medialist/play/23630128?business=space_collection&business_id=2045 (无法从该链接打开合集)
/// </summary>
public class MediaListFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id, CancellationToken cancellationToken = default)
    {
        id = id[10..];
        var api = $"https://api.bilibili.com/x/v1/medialist/info?type=8&biz_id={id}&tid=0";
        var json = await HTTPUtil.GetWebSourceAsync(api, token: cancellationToken);
        using var infoJson = JsonDocument.Parse(json);
        var root = infoJson.RootElement;
        var data = root.GetPropertySafe("data");
        if (data.ValueKind != JsonValueKind.Object)
        {
            // 部分情况下（合集被删除、设为私密或无权访问）data 会是 null
            // 也有可能是“系列”却被误识别为合集，这里优先尝试按系列解析
            try
            {
                return await new SeriesListFetcher().FetchAsync($"seriesBizId:{id}", cancellationToken);
            }
            catch (Exception fallbackEx) when (fallbackEx is HttpRequestException or InvalidOperationException)
            {
                Logger.LogDebug("MediaList fallback to SeriesList failed: {0}", fallbackEx.Message);
                var code = root.TryGetProperty("code", out var codeElem) && codeElem.ValueKind == JsonValueKind.Number
                    ? codeElem.GetInt32()
                    : 0;
                var message = root.TryGetProperty("message", out var msgElem) && msgElem.ValueKind == JsonValueKind.String
                    ? msgElem.GetString()
                    : "未知错误";
                throw new InvalidOperationException($"获取合集信息失败(code={code}): {message}");
            }
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
            var listApi = $"https://api.bilibili.com/x/v2/medialist/resource/list?type=8&oid={oid}&otype=2&biz_id={id}&with_current=true&mobi_app=web&ps=20&direction=false&sort_field=1&tid=0&desc=false";
            json = await HTTPUtil.GetWebSourceAsync(listApi, token: cancellationToken);
            using var listJson = JsonDocument.Parse(json);
            var listRoot = listJson.RootElement;
            data = listRoot.GetPropertySafe("data");
            if (data.ValueKind != JsonValueKind.Object)
            {
                var code = listRoot.TryGetProperty("code", out var codeElem) && codeElem.ValueKind == JsonValueKind.Number
                    ? codeElem.GetInt32()
                    : 0;
                var message = listRoot.TryGetProperty("message", out var msgElem) && msgElem.ValueKind == JsonValueKind.String
                    ? msgElem.GetString()
                    : "未知错误";
                throw new InvalidOperationException($"获取合集视频列表失败(code={code}): {message}");
            }
            hasMore = data.GetBooleanSafe("has_more");
            // 游标必须记录本页最后一条的 id，无论它是否被 attr 过滤跳过。
            // 否则整页都是失效条目时 oid 不会推进，下一轮请求同一页 → 死循环。
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

            // 兜底：接口报 has_more 却没让游标前进（空页 / with_current 只回显游标本身），
            // 再请求也是同一页，直接停止，避免请求洪泛
            if (hasMore && oid == previousOid)
            {
                Logger.LogDebug("合集翻页游标未推进（oid={0}），停止翻页", oid);
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

using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;


namespace BBDown.Core.Fetcher;

/// <summary>
/// 收藏夹解析
/// https://space.bilibili.com/3/favlist
///
/// </summary>
public class FavListFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id, CancellationToken cancellationToken = default)
    {
        id = id[6..];
        var parts = id.Split(':', 2);
        var favId = parts[0];
        // UrlResolver 产出的形式是 favId:{fid}:{mid}，此处已剥掉 "favId:" 前缀
        var mid = parts.Length > 1 ? parts[1] : throw new ArgumentException("收藏夹ID格式错误，期望 favId:收藏夹ID:用户ID");
        //查找默认收藏夹
        if (favId == "")
        {
            var favListApi = $"https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid={mid}";
            using var favDoc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(favListApi, token: cancellationToken));
            var list = favDoc.RootElement.GetPropertySafe("data").EnumerateArraySafe("list");
            var firstFav = list.FirstOrDefault();
            if (firstFav.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                throw new InvalidOperationException("该用户没有创建收藏夹");
            favId = firstFav.GetValueAsStringSafe("id");
        }

        int pageSize = 20;
        int index = 1;
        List<Page> pagesInfo = new();
        // 翻页去重集合：Contains 是 O(n)，翻页几十页时 O(n²) 拖慢解析；Page 已实现
        // Equals/GetHashCode（按 aid+cid+epid），HashSet 去重与 Contains 语义一致。
        HashSet<Page> seenPages = new();

        var api = $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={favId}&pn=1&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
        var json = await HTTPUtil.GetWebSourceAsync(api, token: cancellationToken);
        using var infoJson = JsonDocument.Parse(json);
        var data = infoJson.RootElement.GetPropertySafe("data");
        var favInfo = data.GetPropertySafe("info");
        int totalCount = favInfo.GetInt32Safe("media_count");
        int totalPage = (int)Math.Ceiling((double)totalCount / pageSize);
        var title = favInfo.GetValueAsStringSafe("title");
        var intro = favInfo.GetValueAsStringSafe("intro");
        long pubTime = favInfo.GetInt64Safe("ctime");

        var failures = new List<string>();

        // 就地处理一页的 medias。必须在其 JsonDocument 仍存活时调用——
        // JsonElement 只是对文档的引用，原实现把分页结果 ToList 攒到循环外，
        // 而每次迭代的 using var jsonDoc 已在迭代末尾释放，随后访问会抛
        // ObjectDisposedException（收藏夹超过一页即触发）。
        async Task ProcessPageAsync(JsonElement pageData)
        {
            // 用 EnumerateArraySafe：空收藏夹的 medias 为 null，EnumerateArray 会抛异常
            foreach (var m in pageData.EnumerateArraySafe("medias"))
            {
                //只处理未失效视频
                if (m.GetInt32Safe("attr") != 0) continue;

                var pageCount = m.GetInt32Safe("page");
                if (pageCount > 1)
                {
                    try
                    {
                        var tmpInfo = await new NormalInfoFetcher().FetchAsync(m.GetValueAsStringSafe("id"), cancellationToken);
                        foreach (var item in tmpInfo.PagesInfo)
                        {
                            Page p = new(index++, item)
                            {
                                title = m.GetValueAsStringSafe("title") + $"_P{item.index}_{item.title}",
                                cover = tmpInfo.Pic,
                                desc = m.GetValueAsStringSafe("intro")
                            };
                            // 翻页边界条目重复出现时 index 已自增：命中重复需回退一位，
                            // 否则 Page.index 出现空洞（与 MediaListFetcher/SeriesListFetcher 一致）
                            if (!seenPages.Add(p))
                            {
                                index--;
                                continue;
                            }
                            pagesInfo.Add(p);
                        }
                    }
                    // 单个多P稿件失效（删除/私密/风控）不应中断整个收藏夹解析，
                    // 但真正的用户取消必须向上传播中止整个流程——否则取消被吞成
                    // "某个稿件解析失败"，批量下载会在取消信号下继续空转。
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                                  or InvalidOperationException or TaskCanceledException)
                    {
                        failures.Add($"av{m.GetValueAsStringSafe("id")} {m.GetValueAsStringSafe("title")} —— {ex.Message}");
                    }
                }
                else
                {
                    var upperElem = m.TryGetPropertySafe("upper");
                    var ugcElem = m.TryGetPropertySafe("ugc");
                    Page p = new(index++,
                        m.GetValueAsStringSafe("id"),
                        ugcElem?.GetValueAsStringSafe("first_cid") ?? "",
                        "", //epid
                        m.GetValueAsStringSafe("title"),
                        m.GetInt32Safe("duration"),
                        "",
                        m.GetInt64Safe("pubtime"),
                        m.GetValueAsStringSafe("cover"),
                        m.GetValueAsStringSafe("intro"),
                        upperElem?.GetValueAsStringSafe("name") ?? "",
                        upperElem?.GetValueAsStringSafe("mid") ?? "");
                    if (!seenPages.Add(p)) { index--; continue; }
                    pagesInfo.Add(p);
                }
            }
        }

        await ProcessPageAsync(data);
        for (int page = 2; page <= totalPage; page++)
        {
            api = $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={favId}&pn={page}&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
            json = await HTTPUtil.GetWebSourceAsync(api, token: cancellationToken);
            using var jsonDoc = JsonDocument.Parse(json);
            await ProcessPageAsync(jsonDoc.RootElement.GetPropertySafe("data"));
        }

        if (failures.Count > 0)
        {
            Logger.LogWarn($"收藏夹中有 {failures.Count} 个稿件无法解析，已跳过：");
            foreach (var f in failures.Take(10)) Logger.LogWarn("  " + f, time: false);
            if (failures.Count > 10) Logger.LogWarn($"  ...另有 {failures.Count - 10} 个", time: false);
        }

        var info = new VInfo
        {
            Title = title.Trim(),
            Desc = intro.Trim(),
            Pic = "",
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = false
        };

        return info;
    }
}

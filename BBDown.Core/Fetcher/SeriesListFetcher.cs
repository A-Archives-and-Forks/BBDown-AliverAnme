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
        var data = infoRoot.GetPropertySafe("data");
        // data 为 null 说明系列不存在/私密/无权访问。必须抛出而非静默返回空 VInfo，
        // 否则 MediaListFetcher 的回退会吞掉真正的错误，用户只看到"无视频"。
        if (data.ValueKind != JsonValueKind.Object)
        {
            var code = infoRoot.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            var message = infoRoot.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString() : "未知错误";
            throw new InvalidOperationException($"获取系列信息失败(code={code}): {message}");
        }
        var listTitle = data.GetStringSafe("title")!;
        var intro = data.GetStringSafe("intro")!;
        long pubTime = data.GetInt64Safe("ctime");

        List<Page> pagesInfo = new();
        bool hasMore = true;
        var oid = "";
        int index = 1;
        while (hasMore)
        {
            var listApi = $"https://api.bilibili.com/x/v2/medialist/resource/list?type=5&oid={oid}&otype=2&biz_id={id}&bvid=&with_current=true&mobi_app=web&ps=20&direction=false&sort_field=1&tid=0&desc=true";
            json = await HTTPUtil.GetWebSourceAsync(listApi, token: cancellationToken);
            using var listJson = JsonDocument.Parse(json);
            data = listJson.RootElement.GetPropertySafe("data");
            // 分页接口返回业务错误（data 为 null / code != 0，如系列中途被删除、风控）时，
            // GetBooleanSafe 会静默返回 false 无声结束循环，用户拿到残缺 VInfo。
            // 与 MediaListFetcher 的同一场景保持一致：显式抛出可读错误。
            if (data.ValueKind != JsonValueKind.Object)
            {
                var listRoot = listJson.RootElement;
                var code = listRoot.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                var message = listRoot.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString() : "未知错误";
                throw new InvalidOperationException($"获取系列分页列表失败(code={code}): {message}");
            }
            hasMore = data.GetBooleanSafe("has_more");
            // 游标必须记录本页最后一条 id，无论是否被 attr 过滤；否则整页失效时
            // oid 不推进，重复请求同一页造成死循环。
            var previousOid = oid;
            foreach (var m in data.EnumerateArraySafe("media_list"))
            {
                oid = m.GetValueAsStringSafe("id");

                // 只处理未失效的视频条目（与收藏夹解析逻辑保持一致）
                if (m.TryGetProperty("attr", out var attrElem) && attrElem.GetInt32() != 0)
                    continue;

                var pageCount = m.GetInt32Safe("page");
                var desc = m.GetStringSafe("intro")!;
                var ownerName = m.GetPropertySafe("upper").GetValueAsStringSafe("name");
                var ownerMid = m.GetPropertySafe("upper").GetValueAsStringSafe("mid");
                foreach (var page in m.EnumerateArraySafe("pages"))
                {
                    Page p = new(index++,
                        m.GetValueAsStringSafe("id"),
                        page.GetValueAsStringSafe("id"),
                        "", //epid
                        pageCount == 1 ? m.GetValueAsStringSafe("title") : $"{m.GetValueAsStringSafe("title")}_P{page.GetValueAsStringSafe("page")}_{page.GetValueAsStringSafe("title")}", //单P使用外层标题 多P则拼接内层子标题
                        page.TryGetProperty("duration", out var dur) ? dur.GetInt32() : 0,
                        page.TryGetProperty("dimension", out var dim) && dim.TryGetProperty("width", out var w) && dim.TryGetProperty("height", out var h) ? $"{w}x{h}" : "",
                        m.GetInt64Safe("pubtime"),
                        m.GetValueAsStringSafe("cover"),
                        desc,
                        ownerName,
                        ownerMid);
                    if (!pagesInfo.Contains(p)) pagesInfo.Add(p);
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

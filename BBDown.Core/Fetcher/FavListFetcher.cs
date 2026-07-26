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
    public async Task<VInfo> FetchAsync(string id)
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
            using var favDoc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(favListApi));
            var list = favDoc.RootElement.GetPropertySafe("data").EnumerateArraySafe("list");
            var firstFav = list.FirstOrDefault();
            if (firstFav.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                throw new InvalidOperationException("该用户没有创建收藏夹");
            favId = firstFav.GetValueAsStringSafe("id");
        }

        int pageSize = 20;
        int index = 1;
        List<Page> pagesInfo = new();

        var api = $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={favId}&pn=1&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
        var json = await HTTPUtil.GetWebSourceAsync(api);
        using var infoJson = JsonDocument.Parse(json);
        var data = infoJson.RootElement.GetPropertySafe("data");
        int totalCount = data.GetPropertySafe("info").GetPropertySafe("media_count").GetInt32();
        int totalPage = (int)Math.Ceiling((double)totalCount / pageSize);
        var title = data.GetPropertySafe("info").GetPropertySafe("title").GetString()!;
        var intro = data.GetPropertySafe("info").GetPropertySafe("intro").GetString()!;
        long pubTime = data.GetPropertySafe("info").GetPropertySafe("ctime").GetInt64();
        var userName = data.GetPropertySafe("info").GetPropertySafe("upper").GetPropertySafe("name").ToString();
        var medias = data.GetPropertySafe("medias").EnumerateArray().ToList();

        for (int page = 2; page <= totalPage; page++)
        {
            api = $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={favId}&pn={page}&ps={pageSize}&order=mtime&type=2&tid=0&platform=web";
            json = await HTTPUtil.GetWebSourceAsync(api);
            using var jsonDoc = JsonDocument.Parse(json);
            data = jsonDoc.RootElement.GetPropertySafe("data");
            medias.AddRange(data.GetPropertySafe("medias").EnumerateArray().ToList());
        }

        foreach (var m in medias)
        {
            //只处理视频类型(可以直接在query param上指定type=2)
            // if (m.GetProperty("type").GetInt32() != 2) continue;
            //只处理未失效视频
            if (m.GetInt32Safe("attr") != 0) continue;

            var pageCount = m.GetInt32Safe("page");
            if (pageCount > 1)
            {
                var tmpInfo = await new NormalInfoFetcher().FetchAsync(m.GetValueAsStringSafe("id"));
                foreach (var item in tmpInfo.PagesInfo)
                {
                    Page p = new(index++, item)
                    {
                        title = m.GetValueAsStringSafe("title") + $"_P{item.index}_{item.title}",
                        cover = tmpInfo.Pic,
                        desc = m.GetValueAsStringSafe("intro")
                    };
                    if (!pagesInfo.Contains(p)) pagesInfo.Add(p);
                }
            }
            else
            {
                Page p = new(index++,
                    m.GetValueAsStringSafe("id"),
                    m.GetPropertySafe("ugc").GetValueAsStringSafe("first_cid"),
                    "", //epid
                    m.GetValueAsStringSafe("title"),
                    m.GetInt32Safe("duration"),
                    "",
                    m.GetInt64Safe("pubtime"),
                    m.GetValueAsStringSafe("cover"),
                    m.GetValueAsStringSafe("intro"),
                    m.GetPropertySafe("upper").GetValueAsStringSafe("name"),
                    m.GetPropertySafe("upper").GetValueAsStringSafe("mid"));
                if (!pagesInfo.Contains(p)) pagesInfo.Add(p);
            }
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
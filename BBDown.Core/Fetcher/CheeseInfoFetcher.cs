using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Fetcher;

public class CheeseInfoFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id, CancellationToken cancellationToken = default)
    {
        id = id[7..];
        string index = "";
        string api = $"https://api.bilibili.com/pugv/view/web/season?ep_id={id}";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: cancellationToken);
        using var infoJson = JsonDocument.Parse(json);
        var data = infoJson.RootElement.GetPropertySafe("data");
        string cover = data.GetValueAsStringSafe("cover");
        string title = data.GetValueAsStringSafe("title");
        string desc = data.GetValueAsStringSafe("subtitle");
        string ownerName = data.GetPropertySafe("up_info").GetValueAsStringSafe("uname");
        string ownerMid = data.GetPropertySafe("up_info").GetValueAsStringSafe("mid");
        var pages = data.EnumerateArraySafe("episodes");
        List<Page> pagesInfo = new();
        foreach (var page in pages)
        {
            Page p = new(page.GetInt32Safe("index"),
                page.GetValueAsStringSafe("aid"),
                page.GetValueAsStringSafe("cid"),
                page.GetValueAsStringSafe("id"),
                page.GetValueAsStringSafe("title").Trim(),
                page.GetInt32Safe("duration"),
                "",
                page.GetInt64Safe("release_date"),
                "",
                "",
                ownerName,
                ownerMid);
            if (p.epid == id) index = p.index.ToString();
            pagesInfo.Add(p);
        }
        long pubTime = pagesInfo.Any() ? pagesInfo[0].pubTime : 0;

        var info = new VInfo
        {
            Title = title.Trim(),
            Desc = desc.Trim(),
            Pic = cover,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = true,
            IsCheese = true,
            Index = index
        };

        return info;
    }
}

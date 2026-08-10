using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Fetcher;

public class BangumiInfoFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id, CancellationToken cancellationToken = default)
    {
        id = id[3..];
        string index = "";
        string api = $"https://{Config.Current.EpHost}/pgc/view/web/season?ep_id={id}";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: cancellationToken);
        using var infoJson = JsonDocument.Parse(json);
        if (!infoJson.RootElement.TryGetProperty("result", out var result))
            throw new KeyNotFoundException("Bangumi API response missing 'result' node");
        string cover = result.GetValueAsStringSafe("cover");
        string title = result.GetValueAsStringSafe("title");
        string desc = result.GetValueAsStringSafe("evaluate");
        string pubTimeStr = result.GetPropertySafe("publish").GetValueAsStringSafe("pub_time");
        long pubTime = string.IsNullOrEmpty(pubTimeStr) ? 0 : DateTimeOffset.ParseExact(pubTimeStr, "yyyy-MM-dd HH:mm:ss", null).ToUnixTimeSeconds();
        var pages = result.EnumerateArraySafe("episodes");
        List<Page> pagesInfo = new();
        int i = 1;

        //episodes为空; 或者未包含对应epid，番外/花絮什么的
        bool foundEp = false;
        foreach (var ep in pages)
        {
            if (ep.TryGetProperty("id", out var eid) && eid.ToString() == id)
            {
                foundEp = true;
                break;
            }
        }
        if (!foundEp)
        {
            if (result.TryGetProperty("section", out JsonElement sections))
            {
                foreach (var section in sections.EnumerateArray())
                {
                    bool inSection = false;
                    foreach (var ep in section.EnumerateArraySafe("episodes"))
                    {
                        if (ep.TryGetProperty("id", out var eid) && eid.ToString() == id)
                        {
                            inSection = true;
                            break;
                        }
                    }
                    if (inSection)
                    {
                        if (section.TryGetProperty("title", out var secTitle))
                            title += "[" + secTitle.ToString() + "]";
                        if (section.TryGetProperty("episodes", out var secEps))
                            pages = secEps.EnumerateArray();
                        break;
                    }
                }
            }
        }

        foreach (var page in pages)
        {
            //跳过预告
            if (page.TryGetProperty("badge", out JsonElement badge) && badge.ToString() == "预告") continue;
            string res = "";
            if (page.TryGetProperty("dimension", out var dim) &&
                dim.TryGetProperty("width", out var w) &&
                dim.TryGetProperty("height", out var h))
            {
                res = $"{w}x{h}";
            }
            string _title = page.GetValueAsStringSafe("title");
            if (page.TryGetProperty("long_title", out var lt) && lt.ValueKind != JsonValueKind.Null)
                _title += " " + lt.ToString();
            _title = _title.Trim();
            Page p = new(i++,
                page.GetValueAsStringSafe("aid"),
                page.GetValueAsStringSafe("cid"),
                page.GetValueAsStringSafe("id"),
                _title,
                0, res,
                page.GetInt64Safe("pub_time"));
            if (p.epid == id) index = p.index.ToString();
            pagesInfo.Add(p);
        }


        var info = new VInfo
        {
            Title = title.Trim(),
            Desc = desc.Trim(),
            Pic = cover,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = true,
            IsCheese = false,
            Index = index
        };

        return info;
    }
}

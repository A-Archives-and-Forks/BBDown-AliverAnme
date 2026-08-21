using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Globalization;
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
        // 丢弃 API 顶层 code/message 会把区域限制/账号失效/风控误诊为"响应缺 result 节点"。
        // 与 Cheese/Normal fetcher 一致：非 0 code 给可读诊断。
        long rootCode = infoJson.RootElement.GetInt64Safe("code");
        if (rootCode != 0)
        {
            var msg = infoJson.RootElement.GetValueAsStringSafe("message");
            throw new InvalidOperationException($"番剧接口返回错误: {msg} (code={rootCode})");
        }
        if (!infoJson.RootElement.TryGetProperty("result", out var result))
            throw new KeyNotFoundException("Bangumi API response missing 'result' node");
        string cover = result.GetValueAsStringSafe("cover");
        string title = result.GetValueAsStringSafe("title");
        string desc = result.GetValueAsStringSafe("evaluate");
        string pubTimeStr = result.TryGetPropertySafe("publish")?.GetValueAsStringSafe("pub_time") ?? "";
        // InvariantCulture：pub_time 形如 "2021-07-15 11:00:00"，用 CurrentCulture 解析在
        // 非公历默认历法（fa-IR/ar-SA 等）locale 下会错乱或失败（pubTime 静默归 0）。
        long pubTime = !string.IsNullOrEmpty(pubTimeStr) && DateTimeOffset.TryParse(pubTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto) ? dto.ToUnixTimeSeconds() : 0;
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
            string pageId = page.GetValueAsStringSafe("id");
            // 跳过非用户显式请求的预告（若用户指定了该 epId 则保留，防止 Index 变空导致整季被静默下载）
            if (page.TryGetProperty("badge", out JsonElement badge) && badge.ToString() == "预告" && (string.IsNullOrEmpty(id) || pageId != id))
                continue;
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
                pageId,
                _title,
                0, res,
                page.GetInt64Safe("pub_time"));
            if (p.epid == id) index = p.index.ToString();
            pagesInfo.Add(p);
        }

        if (!string.IsNullOrEmpty(id) && string.IsNullOrEmpty(index))
            throw new KeyNotFoundException($"未找到指定的剧集分P (ep_id={id})");
        if (pagesInfo.Count == 0)
            throw new KeyNotFoundException("未找到剧集分P信息");

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

using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Fetcher;

public partial class NormalInfoFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id, CancellationToken cancellationToken = default)
    {
        string api = $"https://api.bilibili.com/x/web-interface/view?aid={id}";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: cancellationToken);
        using var infoJson = JsonDocument.Parse(json);
        int code = infoJson.RootElement.GetInt32Safe("code");
        if (code != 0)
        {
            string msg = infoJson.RootElement.GetStringSafe("message");
            throw new InvalidOperationException($"获取视频信息失败 (code={code}): {msg}");
        }
        var data = infoJson.RootElement.GetPropertySafe("data");
        string title = data.GetStringSafe("title");
        string desc = data.GetStringSafe("desc");
        string pic = data.GetStringSafe("pic");
        var owner = data.GetPropertySafe("owner");
        // owner.mid 是 JSON 数字，GetStringSafe 只认字符串会返回空串，
        // 导致 <ownerMid> 文件名占位符恒为空；用 GetValueAsStringSafe 兼容数字。
        string ownerMid = owner.GetValueAsStringSafe("mid");
        string ownerName = owner.GetStringSafe("name");
        long pubTime = data.GetInt64Safe("pubdate");
        bool bangumi = false;
        var bvid = data.GetStringSafe("bvid");
        var cid = data.GetInt64Safe("cid");

        // 互动视频 1:是 0:否
        var isSteinGate = data.TryGetProperty("rights", out var rights) && rights.TryGetProperty("is_stein_gate", out var sg) ? sg.GetInt16() : (short)0;

        // UP主充电专属视频。未充电时 playurl 依然返回 code=0，
        // 只是把完整流换成试看片段，因此必须靠这里的权限字段判断，
        // 否则会把几分钟的试看片段当作完整视频下载完毕。
        bool isUpowerExclusive = data.GetBooleanSafe("is_upower_exclusive");
        bool isUpowerPreview = data.GetBooleanSafe("is_upower_preview");
        bool isUpowerPlay = data.GetBooleanSafe("is_upower_play");

        // 分p信息
        List<Page> pagesInfo = new();
        var pages = data.EnumerateArraySafe("pages").ToList();
        foreach (var page in pages)
        {
            Page p = new(page.GetInt32Safe("page"),
                id,
                page.GetValueAsStringSafe("cid"),
                "", //epid
                page.GetValueAsStringSafe("part").Trim(),
                page.GetInt32Safe("duration"),
                page.TryGetProperty("dimension", out var dim) && dim.TryGetProperty("width", out var w) && dim.TryGetProperty("height", out var h) ? $"{w}x{h}" : "",
                pubTime, //分p视频没有发布时间
                "",
                "",
                ownerName,
                ownerMid
            );
            pagesInfo.Add(p);
        }

        if (isSteinGate == 1) // 互动视频获取分P信息
        {
            var playerSoApi = $"https://api.bilibili.com/x/player.so?bvid={bvid}&id=cid:{cid}";
            var playerSoText = await HTTPUtil.GetWebSourceAsync(playerSoApi, token: cancellationToken);
            var playerSoXml = new XmlDocument();
            playerSoXml.LoadXml($"<root>{playerSoText}</root>");

            var interactionNode = playerSoXml.SelectSingleNode("//interaction");

            if (interactionNode is { InnerText.Length: > 0 })
            {
                using var graphDoc = JsonDocument.Parse(interactionNode.InnerText);
                var graphVersion = graphDoc.RootElement.GetInt64Safe("graph_version");
                var edgeInfoApi = $"https://api.bilibili.com/x/stein/edgeinfo_v2?graph_version={graphVersion}&bvid={bvid}";
                var edgeInfoJson = await HTTPUtil.GetWebSourceAsync(edgeInfoApi, token: cancellationToken);
                using var edgeDoc = JsonDocument.Parse(edgeInfoJson);
                var edgeInfoData = edgeDoc.RootElement.GetPropertySafe("data");
                var questions = edgeInfoData.GetPropertySafe("edges").EnumerateArraySafe("questions")
                    .ToList();
                var index = 2; // 互动视频分P索引从2开始
                foreach (var question in questions)
                {
                    var choices = question.EnumerateArraySafe("choices").ToList();
                    foreach (var page in choices)
                    {
                        Page p = new(index++,
                            id,
                            page.GetValueAsStringSafe("cid"),
                            "", //epid
                            page.GetValueAsStringSafe("option").Trim(),
                            0,
                            "",
                            pubTime, //分p视频没有发布时间
                            "",
                            "",
                            ownerName,
                            ownerMid
                        );
                        pagesInfo.Add(p);
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("互动视频获取分P信息失败");
            }
        }

        if (data.TryGetProperty("redirect_url", out var redirectUrl) && redirectUrl.ToString().Contains("bangumi"))
        {
            bangumi = true;
            string epId = EpIdRegex().Match(redirectUrl.ToString()).Groups[1].Value;
            //番剧内容通常不会有分P，如果有分P则不需要epId参数
            if (pages.Count == 1)
            {
                pagesInfo.ForEach(p => p.epid = epId);
            }
        }

        var info = new VInfo
        {
            Title = title.Trim(),
            Desc = desc.Trim(),
            Pic = pic,
            PubTime = pubTime,
            PagesInfo = pagesInfo,
            IsBangumi = bangumi,
            IsSteinGate = isSteinGate == 1,
            IsUpowerExclusive = isUpowerExclusive,
            IsUpowerPreview = isUpowerPreview,
            IsUpowerPlay = isUpowerPlay
        };

        return info;
    }

    [GeneratedRegex("ep(\\d+)")]
    private static partial Regex EpIdRegex();
}

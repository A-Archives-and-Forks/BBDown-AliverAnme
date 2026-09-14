using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

/// <summary>
/// 轨道排序的清晰度 id 解析（RF-31）：id 是服务器可控字符串（dash/intl/flv 各分支的
/// GetValueAsStringSafe，缺失/非字符串时为空串），裸 Convert.ToInt32 的
/// FormatException/OverflowException 不在页面级/批级 catch 过滤器白名单内——
/// 单个畸形节点会穿透两级失败隔离中止整批多 P 下载。SortTracks 提为 internal 供回归。
/// </summary>
public class TrackSortTests
{
    private static List<Video> MakeTracks(params (string id, string codecs, string dfn, int bandwidth)[] specs)
        => specs.Select(s => new Video { baseUrl = "", id = s.id, codecs = s.codecs, dfn = s.dfn, bandwidth = s.bandwidth }).ToList();

    [Fact]
    public void SortTracks_MissingOrMalformedId_FallsBackToZeroInsteadOfThrowing()
    {
        var tracks = MakeTracks(
            ("", "avc", "1080P 高码率", 100),
            ("not-a-number", "avc", "1080P 高码率", 200),
            ("999999999999", "avc", "1080P 高码率", 50), // 超 int32：原实现抛 OverflowException
            ("80", "avc", "1080P 高码率", 10));
        var dfnPriority = new Dictionary<string, int> { ["1080P 高码率"] = 1 };
        var encPriority = new Dictionary<string, byte> { ["avc"] = 1 };

        var sorted = Program.SortTracks(tracks, dfnPriority, encPriority, videoAscending: false);

        // 不抛异常即核心断言；降级 0 的轨道仍按带宽参与排序
        Assert.Equal(4, sorted.Count);
        Assert.Equal("80", sorted[0].id); // 唯一可解析的 id 值最大，tie-break 排最前
        // 其余三个 id 均降级 0（并列），按带宽降序（videoAscending:false → -bandwidth 升序）
        Assert.Equal("not-a-number", sorted[1].id);
        Assert.Equal("", sorted[2].id);
        Assert.Equal("999999999999", sorted[3].id);
    }
}

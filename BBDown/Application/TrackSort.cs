using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static BBDown.Core.Entity.Entity;

using BBDown.Core.Util;
using static BBDown.BBDownUtil;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    // id 是服务器可控字符串（dash/intl/flv 各分支经 GetValueAsStringSafe 取值，缺失/非
    // 字符串时为空串）：裸 Convert.ToInt32 的 FormatException/OverflowException 不在
    // 页面级/批级 catch 过滤器白名单内——单个畸形节点会穿透两级失败隔离、中止整批
    // 多 P 下载（RF-31）。id 仅作编码+清晰度并列时的 tie-break，解析失败降级 0
    // 不影响首选结果。
    internal static List<Video> SortTracks(List<Video> videoTracks, Dictionary<string, int> dfnPriority, Dictionary<string, byte> encodingPriority, bool videoAscending)
    {
        // 编码优先：先按编码排序，再按清晰度排序；清晰度优先时使用 --dfn-priority 即可
        return videoTracks
            .OrderBy(v => encodingPriority.GetValueOrDefault(v.codecs, (byte)100))
            .ThenBy(v => dfnPriority.GetValueOrDefault(v.dfn, 100))
            .ThenByDescending(v => int.TryParse(v.id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality) ? quality : 0)
            .ThenBy(v => videoAscending ? v.bandwidth : -v.bandwidth)
            .ToList();
    }

    private static List<Audio> SortTracks(List<Audio> audioTracks, Dictionary<string, byte> encodingPriority, bool audioAscending)
    {
        return audioTracks
            .OrderBy(a => encodingPriority.GetValueOrDefault(a.shortCodecs, (byte)100))
            .ThenBy(a => audioAscending ? a.bandwidth : -a.bandwidth)
            .ToList();
    }

}

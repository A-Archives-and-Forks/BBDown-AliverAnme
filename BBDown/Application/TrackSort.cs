using System.Collections.Generic;
using System.Linq;
using static BBDown.Core.Entity.Entity;

using BBDown.Core.Util;
using static BBDown.BBDownUtil;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    private static List<Video> SortTracks(List<Video> videoTracks, Dictionary<string, int> dfnPriority, Dictionary<string, byte> encodingPriority, bool videoAscending)
    {
        // 编码优先：先按编码排序，再按清晰度排序；清晰度优先时使用 --dfn-priority 即可
        return videoTracks
            .OrderBy(v => encodingPriority.GetValueOrDefault(v.codecs, (byte)100))
            .ThenBy(v => dfnPriority.GetValueOrDefault(v.dfn, 100))
            .ThenByDescending(v => Convert.ToInt32(v.id))
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

using System.Collections.Generic;
using System.Linq;

namespace BBDown;

/// <summary>
/// 按 aid 聚合多P归档状态。所有分P均已成功或被跳过后才允许把 aid 写入归档文件。
/// </summary>
internal sealed class ArchiveTracker
{
    private readonly Dictionary<string, int> _remaining;
    private readonly HashSet<string> _failed = new();

    public ArchiveTracker(IEnumerable<string> pageAids)
        => _remaining = pageAids.GroupBy(aid => aid).ToDictionary(group => group.Key, group => group.Count());

    /// <summary>已入档的 aid（按全部成功完成的顺序）。</summary>
    public List<string> Archived { get; } = new();

    /// <summary>某个 aid 的分P 被跳过（已下载过）时调用：该分P 不计入失败。</summary>
    public void OnSkipped(string aid) => _remaining[aid]--;

    /// <summary>某个 aid 的分P 处理完毕时调用，返回本次是否触发入档。</summary>
    public bool OnProcessed(string aid, bool succeeded)
    {
        _remaining[aid]--;
        if (!succeeded) _failed.Add(aid);
        if (_remaining[aid] == 0 && !_failed.Contains(aid))
        {
            Archived.Add(aid);
            return true;
        }

        return false;
    }
}

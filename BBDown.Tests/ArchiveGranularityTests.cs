namespace BBDown.Tests;

/// <summary>
/// --save-archives-to-file 的存档以 aid 为粒度，而多P稿件的所有分P共享同一个 aid。
/// 若下完第一个分P就入档，同稿件余下的分P会在下次运行时被判定为"已下载"而跳过。
/// 这里复刻 DownloadPagesAsync 的计数规则，锁定"整个 aid 全部成功才入档"。
/// </summary>
public class ArchiveGranularityTests
{
    /// <summary>与 Download.cs 中的入档判定保持一致。</summary>
    private sealed class ArchiveTracker(IEnumerable<string> pageAids)
    {
        private readonly Dictionary<string, int> _remaining = pageAids
            .GroupBy(a => a)
            .ToDictionary(g => g.Key, g => g.Count());
        private readonly HashSet<string> _failed = [];

        public List<string> Archived { get; } = [];

        public void OnSkipped(string aid) => _remaining[aid]--;

        public void OnProcessed(string aid, bool succeeded)
        {
            _remaining[aid]--;
            if (!succeeded) _failed.Add(aid);
            if (_remaining[aid] == 0 && !_failed.Contains(aid)) Archived.Add(aid);
        }
    }

    [Fact]
    public void MultiPageVideo_IsArchivedOnlyAfterEveryPageSucceeds()
    {
        // 一个 3P 稿件
        var t = new ArchiveTracker(["100", "100", "100"]);

        t.OnProcessed("100", true);
        Assert.Empty(t.Archived);            // 第 1 个分P后不能入档

        t.OnProcessed("100", true);
        Assert.Empty(t.Archived);            // 第 2 个分P后仍不能

        t.OnProcessed("100", true);
        Assert.Equal(["100"], t.Archived);   // 全部完成才入档
    }

    [Fact]
    public void MultiPageVideo_IsNotArchivedWhenAnyPageFails()
    {
        var t = new ArchiveTracker(["100", "100", "100"]);

        t.OnProcessed("100", true);
        t.OnProcessed("100", false);         // 中间一个分P失败
        t.OnProcessed("100", true);

        Assert.Empty(t.Archived);
    }

    [Fact]
    public void FailureOnLastPage_AlsoPreventsArchiving()
    {
        var t = new ArchiveTracker(["100", "100"]);

        t.OnProcessed("100", true);
        t.OnProcessed("100", false);

        Assert.Empty(t.Archived);
    }

    [Fact]
    public void SinglePageVideo_IsArchivedImmediately()
    {
        var t = new ArchiveTracker(["100"]);

        t.OnProcessed("100", true);

        Assert.Equal(["100"], t.Archived);
    }

    [Fact]
    public void MixedList_ArchivesOnlyFullySucceededVideos()
    {
        // 投稿列表典型形态：单P + 多P 混合，其中一个多P稿件有分P失败
        var t = new ArchiveTracker(["a", "b", "b", "c", "c", "c"]);

        t.OnProcessed("a", true);
        t.OnProcessed("b", true);
        t.OnProcessed("b", true);
        t.OnProcessed("c", true);
        t.OnProcessed("c", false);
        t.OnProcessed("c", true);

        Assert.Equal(["a", "b"], t.Archived);
        Assert.DoesNotContain("c", t.Archived);
    }

    [Fact]
    public void PreviouslyArchivedVideo_KeepsCountConsistent()
    {
        // 已入档的稿件会被逐个分P跳过；计数必须同步递减，
        // 否则后续同 aid 的判定会错乱
        var t = new ArchiveTracker(["100", "100"]);

        t.OnSkipped("100");
        t.OnSkipped("100");

        // 跳过不重复入档
        Assert.Empty(t.Archived);
    }
}

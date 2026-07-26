namespace BBDown.Tests;

/// <summary>
/// 多线程下载的进度累计逻辑。
/// 回调传入的是「该分片已下载的累计字节数」而非增量，
/// 因此聚合必须用「新值 - 上次值」推进总量；分片重试时该值会回退到 0，
/// 总量也必须随之回退，否则进度条会越过 100%。
/// </summary>
public class DownloadProgressAggregationTests
{
    /// <summary>与 MultiThreadDownloadCoreAsync 中的聚合方式保持一致。</summary>
    private sealed class ProgressAggregator(int clipCount)
    {
        private readonly long[] _perClip = new long[clipCount];
        private long _total;

        public long Report(int index, long cumulativeForClip)
        {
            var previous = Interlocked.Exchange(ref _perClip[index], cumulativeForClip);
            return Interlocked.Add(ref _total, cumulativeForClip - previous);
        }

        public long Total => Interlocked.Read(ref _total);
    }

    [Fact]
    public void Total_MatchesSumOfPerClipProgress_WhenClipsAdvanceInterleaved()
    {
        const int clips = 8, steps = 10, block = 262144;
        var aggregator = new ProgressAggregator(clips);
        var reference = new long[clips];

        for (var step = 0; step < steps; step++)
        {
            for (var c = 0; c < clips; c++)
            {
                var cumulative = (long)(step + 1) * block;
                reference[c] = cumulative;

                Assert.Equal(reference.Sum(), aggregator.Report(c, cumulative));
            }
        }

        Assert.Equal((long)clips * steps * block, aggregator.Total);
    }

    [Fact]
    public void Total_IsExact_UnderConcurrentClipUpdates()
    {
        const int clips = 64, steps = 500, block = 262144;
        var aggregator = new ProgressAggregator(clips);

        Parallel.For(0, clips, c =>
        {
            for (var step = 0; step < steps; step++)
            {
                aggregator.Report(c, (long)(step + 1) * block);
            }
        });

        Assert.Equal((long)clips * steps * block, aggregator.Total);
    }

    [Fact]
    public void Total_RollsBack_WhenClipRestartsAfterRetry()
    {
        const int block = 262144;
        var aggregator = new ProgressAggregator(3);

        aggregator.Report(0, 10 * block);
        aggregator.Report(1, 10 * block);
        Assert.Equal(20L * block, aggregator.Total);

        // 分片 1 下载失败并重试，从头开始
        aggregator.Report(1, 0);
        Assert.Equal(10L * block, aggregator.Total);

        aggregator.Report(1, 3 * block);
        Assert.Equal(13L * block, aggregator.Total);
    }

    [Fact]
    public void Total_NeverExceedsFileSize_AcrossRetries()
    {
        const int clips = 4, block = 262144;
        var clipSize = 10L * block;
        var fileSize = clips * clipSize;
        var aggregator = new ProgressAggregator(clips);

        for (var c = 0; c < clips; c++)
        {
            aggregator.Report(c, clipSize);
            if (c == 2)
            {
                aggregator.Report(c, 0);          // 重试
                aggregator.Report(c, clipSize);
            }
            Assert.True(aggregator.Total <= fileSize,
                $"分片 {c} 后累计 {aggregator.Total} 超过文件大小 {fileSize}");
        }

        Assert.Equal(fileSize, aggregator.Total);
    }
}

namespace BBDown.Tests;

/// <summary>
/// 覆盖下载路径锁的生命周期。serve 模式是长驻进程，
/// 锁表一旦只增不减就是持续增长的内存占用。
/// </summary>
public class DownloadPathLockTests
{
    [Fact]
    public async Task PathLock_IsRemovedAfterUse()
    {
        var before = BBDownDownloadUtil.ActivePathLockCount;

        await BBDownDownloadUtil.RunWithPathLockAsync("/tmp/bbdown-test-a.mp4", () => Task.CompletedTask);

        Assert.Equal(before, BBDownDownloadUtil.ActivePathLockCount);
    }

    [Fact]
    public async Task PathLock_IsRemovedWhenActionThrows()
    {
        var before = BBDownDownloadUtil.ActivePathLockCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BBDownDownloadUtil.RunWithPathLockAsync("/tmp/bbdown-test-b.mp4",
                () => throw new InvalidOperationException("boom")));

        Assert.Equal(before, BBDownDownloadUtil.ActivePathLockCount);
    }

    [Fact]
    public async Task PathLock_IsRemovedWhenWaitIsCancelled()
    {
        var before = BBDownDownloadUtil.ActivePathLockCount;
        const string path = "/tmp/bbdown-test-c.mp4";

        // 先占住这把锁，让第二个调用停在 WaitAsync 上
        var holderEntered = new TaskCompletionSource();
        var releaseHolder = new TaskCompletionSource();
        var holder = BBDownDownloadUtil.RunWithPathLockAsync(path, async () =>
        {
            holderEntered.SetResult();
            await releaseHolder.Task;
        });
        await holderEntered.Task;

        using var cts = new CancellationTokenSource();
        var blocked = BBDownDownloadUtil.RunWithPathLockAsync(path, () => Task.CompletedTask, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);

        releaseHolder.SetResult();
        await holder;

        // 取消的等待者不能漏减引用计数，否则该路径的锁永远留在表里
        Assert.Equal(before, BBDownDownloadUtil.ActivePathLockCount);
    }

    [Fact]
    public async Task PathLock_SerializesConcurrentAccessToSamePath()
    {
        const string path = "/tmp/bbdown-test-d.mp4";
        var concurrent = 0;
        var maxObserved = 0;
        var gate = new object();

        async Task Body()
        {
            lock (gate) { maxObserved = Math.Max(maxObserved, ++concurrent); }
            await Task.Delay(20);
            lock (gate) { concurrent--; }
        }

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => BBDownDownloadUtil.RunWithPathLockAsync(path, Body)));

        Assert.Equal(1, maxObserved);
        Assert.Equal(0, BBDownDownloadUtil.ActivePathLockCount);
    }

    [Fact]
    public async Task PathLock_AllowsParallelAccessToDifferentPaths()
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = BBDownDownloadUtil.RunWithPathLockAsync("/tmp/bbdown-test-e1.mp4", async () =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;

        // 不同路径不应互相阻塞
        var second = BBDownDownloadUtil.RunWithPathLockAsync("/tmp/bbdown-test-e2.mp4", () => Task.CompletedTask);
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        release.SetResult();
        await first;
    }
}

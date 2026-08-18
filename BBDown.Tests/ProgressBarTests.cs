using BBDown;

namespace BBDown.Tests;

/// <summary>
/// 进度条结算测试：速度定时器每秒才累加一次下载字节（服务端任务字段），
/// 短下载/下载末尾的最后 <1 秒的增量在 Dispose 时一次性结算——否则短下载
/// 统计为 0、平均速度失真（F12 未覆盖分支）。
/// </summary>
public class ProgressBarTests
{
    [Fact]
    public void Dispose_SettlesPendingBytesIntoTask()
    {
        var task = new DownloadTask("170001", "https://x/v", 0);
        var pb = new ProgressBar(task);
        // Report(value, bytesCount)：立即记录已下载字节，但不触发速度定时器（每秒一次）。
        // 若 Dispose 不做最终结算，TotalDownloadedBytes 会保持 0（短下载失真）。
        pb.Report(1.0, 5000);
        pb.Dispose();
        // 无论速度定时器是否在 Dispose 前恰巧触发（其逻辑会累加后清掉 lastDownloadedBytes），
        // 最终总下载字节都必须等于 5000——这是"结算不漏"的契约。
        Assert.Equal(5000, task.TotalDownloadedBytes);
        Assert.Equal(5000, task.DownloadSpeed);
    }

    [Fact]
    public void Dispose_WithNoReport_LeavesTaskZero()
    {
        // 无任何 Report：dispose 结算 delta 为 0，不得给任务累计虚假字节。
        var task = new DownloadTask("170001", "https://x/v", 0);
        using var pb = new ProgressBar(task);
        pb.Dispose();
        Assert.Equal(0, task.TotalDownloadedBytes);
    }
}

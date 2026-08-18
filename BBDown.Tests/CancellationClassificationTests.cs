namespace BBDown.Tests;

/// <summary>
/// B3：OperationCanceledException 的取消/超时分类。
/// HttpClient 超时抛的 TaskCanceledException 其 token 未取消，必须归为 Failed
/// 而非 Cancelled——否则任务被误标"已取消"，掩盖真实失败原因。
/// </summary>
public class CancellationClassificationTests
{
    [Fact]
    public void ClassifyCancellation_GenuineCancel_IsCancelled()
    {
        var (status, message) = BBDownApiServer.ClassifyCancellation(
            cancellationRequested: true, failureMessage: "解析请求超时或被中断");
        Assert.Equal(DownloadTaskStatus.Cancelled, status);
        Assert.Equal("已取消", message);
    }

    [Fact]
    public void ClassifyCancellation_TimeoutWithoutTokenCancel_IsFailedNotCancelled()
    {
        var (status, message) = BBDownApiServer.ClassifyCancellation(
            cancellationRequested: false, failureMessage: "解析请求超时或被中断");
        Assert.Equal(DownloadTaskStatus.Failed, status);
        Assert.Equal("解析请求超时或被中断", message);
    }
}

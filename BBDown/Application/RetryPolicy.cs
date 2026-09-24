namespace BBDown;

/// <summary>
/// Central retry bounds for CLI and serve inputs. CLI values are rejected outside
/// the supported range, while untrusted serve values are clamped to tighter limits.
/// </summary>
internal static class RetryPolicy
{
    internal const int MinRetryCount = 1;
    internal const int MaxCliRetryCount = 100;
    internal const int MaxServeRetryCount = 3;
    internal const int MinRetryDelayMs = 0;
    internal const int MaxCliRetryDelayMs = 600_000;
    internal const int MaxServeRetryDelayMs = 5_000;

    internal static (int RetryCount, int RetryDelayMs) NormalizeForCli(int retryCount, int retryDelayMs)
    {
        if (retryCount < MinRetryCount || retryCount > MaxCliRetryCount)
        {
            throw new ArgumentException(
                $"参数有误：--retry-count 需在 {MinRetryCount} ~ {MaxCliRetryCount} 之间，当前为 {retryCount}（设为 0 将不会发起任何下载，过大则无限重试拖垮任务）");
        }

        if (retryDelayMs < MinRetryDelayMs || retryDelayMs > MaxCliRetryDelayMs)
        {
            // 过大值会让退避等待持续数小时，且重试次数与延迟的乘积可能溢出 int。
            throw new ArgumentException(
                $"参数有误：--retry-delay 需在 {MinRetryDelayMs} ~ {MaxCliRetryDelayMs} ms 之间，当前为 {retryDelayMs}");
        }

        return (retryCount, retryDelayMs);
    }

    internal static (int RetryCount, int RetryDelayMs) NormalizeForServe(int retryCount, int retryDelayMs)
        => (
            Math.Clamp(retryCount, MinRetryCount, MaxServeRetryCount),
            Math.Clamp(retryDelayMs, MinRetryDelayMs, MaxServeRetryDelayMs));
}

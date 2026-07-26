namespace BBDown.Core.Util;

/// <summary>
/// UP主充电专属视频的权限与试看片段判定。
///
/// 背景：充电专属稿件在当前身份无权观看时，playurl 接口<b>不会</b>返回错误码，
/// 而是照常返回 code=0，并且 timelength 字段仍然声称完整时长，
/// 只有实际下发的分段是几分钟的试看片段。若不做交叉校验，
/// 下载器会把残片当作完整视频"下载成功"，用户毫无察觉。
/// </summary>
public static class UpowerGuard
{
    /// <summary>
    /// 实际时长低于稿件声称时长的该比例时，判定为试看片段。
    /// 取 0.9 是为了容忍 dash/durl 与稿件元数据之间常见的数秒级误差。
    /// </summary>
    private const double PreviewRatioThreshold = 0.9;

    /// <summary>
    /// 触发判定所需的最小绝对差(秒)，避免短视频因几秒误差被误报。
    /// </summary>
    private const int MinAbsoluteGapSec = 30;

    /// <param name="IsPreview">本次解析到的是否为试看片段</param>
    /// <param name="Reason">面向用户的说明，仅在 IsPreview 为 true 时有意义</param>
    public readonly record struct PreviewVerdict(bool IsPreview, string Reason);

    /// <summary>
    /// 判定本次解析结果是否为充电专属视频的试看片段。
    /// </summary>
    /// <param name="isUpowerExclusive">稿件是否为充电专属</param>
    /// <param name="isUpowerPlay">当前身份是否有完整播放权限</param>
    /// <param name="declaredDurationSec">稿件元数据声称的时长(秒)</param>
    /// <param name="actualDurationSec">本次实际拿到的媒体时长(秒)</param>
    public static PreviewVerdict Inspect(
        bool isUpowerExclusive,
        bool isUpowerPlay,
        int declaredDurationSec,
        int actualDurationSec)
    {
        // 时长交叉校验独立于权限字段：接口字段可能缺失或改名，
        // 但"拿到的内容比稿件短一大截"这个事实无法伪装。
        bool durationMismatch = declaredDurationSec > 0
                                && actualDurationSec > 0
                                && declaredDurationSec - actualDurationSec >= MinAbsoluteGapSec
                                && actualDurationSec < declaredDurationSec * PreviewRatioThreshold;

        if (isUpowerExclusive && !isUpowerPlay)
        {
            var reason = durationMismatch
                ? $"当前账号没有该UP主的充电权限，接口只返回了 {FormatSec(actualDurationSec)} 的试看片段（完整视频 {FormatSec(declaredDurationSec)}）"
                : "当前账号没有该UP主的充电权限，接口返回的可能只是试看片段";
            return new PreviewVerdict(true, reason);
        }

        if (durationMismatch)
        {
            return new PreviewVerdict(true,
                $"实际解析到的时长 {FormatSec(actualDurationSec)} 明显短于稿件时长 {FormatSec(declaredDurationSec)}，很可能只是试看片段");
        }

        return new PreviewVerdict(false, "");
    }

    private static string FormatSec(int seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
}

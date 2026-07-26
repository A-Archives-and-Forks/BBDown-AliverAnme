using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// 充电专属视频的试看片段判定。
///
/// 真实样本来自 BV1FZNZ67EaP（食贫道《世 纪 辐 射【下】》）：
/// view 接口 duration=8628、is_upower_exclusive=true、is_upower_play=false，
/// 而 playurl 返回 code=0、message 为空、timelength 谎报 8627 秒，
/// 实际 durl 只有一段 389995ms。整条链路没有任何错误信号，
/// 因此判定必须依赖权限字段与时长交叉校验，而不是接口错误码。
/// </summary>
public class UpowerGuardTests
{
    [Fact]
    public void Inspect_FlagsPreview_WhenExclusiveAndNoPlayPermission()
    {
        // BV1FZNZ67EaP 未充电时的真实数据
        var verdict = UpowerGuard.Inspect(
            isUpowerExclusive: true,
            isUpowerPlay: false,
            declaredDurationSec: 8628,
            actualDurationSec: 389);

        Assert.True(verdict.IsPreview);
        Assert.Contains("充电权限", verdict.Reason);
        // 两个时长都要出现在提示里，用户才能一眼看出差距
        Assert.Contains("00:06:29", verdict.Reason);
        Assert.Contains("02:23:48", verdict.Reason);
    }

    [Fact]
    public void Inspect_AllowsDownload_WhenExclusiveButPermissionGranted()
    {
        // 已充电：拿到完整时长，不应误报
        var verdict = UpowerGuard.Inspect(
            isUpowerExclusive: true,
            isUpowerPlay: true,
            declaredDurationSec: 8628,
            actualDurationSec: 8628);

        Assert.False(verdict.IsPreview);
    }

    [Fact]
    public void Inspect_FlagsPreview_OnDurationGapEvenWithoutUpowerFlags()
    {
        // 权限字段缺失或改名时，时长差距仍须独立兜底
        var verdict = UpowerGuard.Inspect(
            isUpowerExclusive: false,
            isUpowerPlay: false,
            declaredDurationSec: 8628,
            actualDurationSec: 389);

        Assert.True(verdict.IsPreview);
        Assert.Contains("试看片段", verdict.Reason);
    }

    [Fact]
    public void Inspect_ToleratesSmallDurationDrift()
    {
        // dash duration 与稿件元数据常有数秒误差，不能误报
        var verdict = UpowerGuard.Inspect(
            isUpowerExclusive: false,
            isUpowerPlay: false,
            declaredDurationSec: 600,
            actualDurationSec: 597);

        Assert.False(verdict.IsPreview);
    }

    [Fact]
    public void Inspect_IgnoresShortClipsWithinAbsoluteGuard()
    {
        // 30 秒以内的绝对差不触发，避免短视频误报
        var verdict = UpowerGuard.Inspect(
            isUpowerExclusive: false,
            isUpowerPlay: false,
            declaredDurationSec: 100,
            actualDurationSec: 75);

        Assert.False(verdict.IsPreview);
    }

    [Fact]
    public void Inspect_SkipsCrossCheck_WhenDurationUnknown()
    {
        // 部分接口拿不到时长，缺数据时不得凭空报错
        var verdict = UpowerGuard.Inspect(
            isUpowerExclusive: false,
            isUpowerPlay: false,
            declaredDurationSec: 0,
            actualDurationSec: 0);

        Assert.False(verdict.IsPreview);
    }

    [Fact]
    public void Inspect_StillWarns_WhenExclusiveAndDurationUnavailable()
    {
        // 无法交叉校验时，权限字段本身仍足以给出警告
        var verdict = UpowerGuard.Inspect(
            isUpowerExclusive: true,
            isUpowerPlay: false,
            declaredDurationSec: 0,
            actualDurationSec: 0);

        Assert.True(verdict.IsPreview);
        Assert.Contains("充电权限", verdict.Reason);
    }
}

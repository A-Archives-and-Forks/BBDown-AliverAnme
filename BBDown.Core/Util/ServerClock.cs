namespace BBDown.Core.Util;

/// <summary>
/// 经服务器时钟偏移校准的 UTC 时间源。WBI 签名（wts/ts）与部分请求参数基于系统时钟，
/// 本地时钟偏差超过 B 站 ~60s 时效窗口即被拒绝签名（虚拟机时钟不同步、双系统时间未
/// 同步、未启用 NTP 的容器等）。偏移由 HTTPUtil.CalibrateClock 从响应头 Date 校准写入
/// Config.Current.ServerClockOffsetSeconds；此处在偏移基础上返回校准时间。
/// offset=0 时 <see cref="Now"/> 即 UTC 当前时间，与未校准行为完全一致（零回归）。
/// </summary>
public static class ServerClock
{
    /// <summary>校准后的当前 UTC 时间。所有参与签名/时效的时间戳都应经此取时。</summary>
    public static DateTimeOffset Now => DateTimeOffset.UtcNow.AddSeconds(Config.Current.ServerClockOffsetSeconds);

    /// <summary>校准后的 Unix 秒时间戳（等价于 <c>Now.ToUnixTimeSeconds()</c> 的便捷写法）。</summary>
    public static long NowUnixSeconds() => Now.ToUnixTimeSeconds();
}

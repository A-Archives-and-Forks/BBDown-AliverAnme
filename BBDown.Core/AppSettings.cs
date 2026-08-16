namespace BBDown.Core;

/// <summary>
/// 全局可注入的应用配置选项。
/// 用于替代 <see cref="Config"/> 的静态可写属性，使依赖关系显式化并支持单元测试。
/// </summary>
public record AppSettings(
    string Cookie = "",
    string Token = "",
    bool DebugLog = false,
    string Host = "api.bilibili.com",
    string EpHost = "api.bilibili.com",
    string TvHost = "api.snm0516.aisee.tv",
    string Area = "",
    string Wbi = "",
    bool SkipSslCheck = false,
    int MuxerTimeoutMinutes = 30,
    int MaxRetryCount = 3,
    int RetryDelayMs = 3000,
    // 单次 API 请求的整体超时（毫秒，默认 2 分钟）。HttpClient.Timeout 只约束响应头阶段，
    // 响应体读取在 ResponseHeadersRead 后不再受其约束，因此 GetWebSourceAsync /
    // GetPostResponseAsync 用此值重建整体超时。默认可由测试覆盖以验证超时重试。
    int ApiTimeoutMs = 120000,
    int ThreadSegmentSizeMb = 20,
    string UserAgent = "",
    // 当前任务流的工作目录（--work-dir）。serve 模式下每个 /add-task 经 SetUpWork
    // 把各自目录写入本配置快照（AsyncLocal 隔离），下载管线的相对路径经
    // PathUtil.ResolveWorkPath 基于它解析——不写进程 CWD，避免并发任务互相污染。
    string WorkDir = "",
    // 服务器时钟偏移（秒）：WBI 签名 wts/ts 基于本地系统时钟，时钟偏差超过 ~60s
    // 时效窗口即被 B 站拒绝签名。首次请求从响应头 Date 校准后写入，后续签名时间戳
    // 经 ServerClock.Now 补偿。UTC 偏移是服务器物理属性而非账号凭据，serve 并发任务
    // 间共享无害。
    long ServerClockOffsetSeconds = 0
)
{
    /// <summary>
    /// B站视频清晰度映射表。
    /// </summary>
    public static readonly Dictionary<string, string> QualityMap = new()
    {
        {"127","8K 超高清" }, {"126","杜比视界" }, {"125","HDR 真彩" }, {"120","4K 超清" }, {"116","1080P 高帧率" },
        {"112","1080P 高码率" }, {"100","智能修复" }, {"80","1080P 高清" }, {"74","720P 高帧率" },
        {"64","720P 高清" }, {"48","720P 高清" }, {"32","480P 清晰" }, {"16","360P 流畅" },
        {"5","144P 流畅" }, {"6","240P 流畅" }
    };
}

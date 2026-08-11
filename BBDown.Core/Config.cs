namespace BBDown.Core;

public static class Config
{
    private static AppSettings _settings = new();
    private static readonly object _lock = new();

    /// <summary>
    /// 每个异步流自己的配置快照。Apply 在当前 async 流内写入它，Current 优先读取它。
    /// serve 模式下每个 /add-task 的下载流各自 SetUpWork 覆盖全局 Config，
    /// 若只有全局 _settings，并发任务会互相读到对方刚写入的 Cookie/Token/Host，
    /// 表现为跨账号串号下载。AsyncLocal 随 ExecutionContext 在 async 流与派生子任务间
    /// 自动传递，把"全局单例"收敛为"每任务流单例"。
    /// </summary>
    private static readonly AsyncLocal<AppSettings?> _contextSettings = new();

    public static AppSettings Current
    {
        get
        {
            AppSettings? local = _contextSettings.Value;
            if (local is not null) return local;
            lock (_lock) { return _settings; }
        }
    }

    public static void Apply(AppSettings settings)
    {
        // 同时写上下文与全局：CLI 顶层（无 async 隔离需求）与静态初始化路径保持原行为，
        // serve 并发任务流内则通过上下文隔离
        _contextSettings.Value = settings;
        lock (_lock) { _settings = settings; }
    }

    /// <summary>
    /// 只更新当前异步流的配置快照，不触碰全局 <see cref="_settings"/>。
    /// 用于子方法在下载流程中产生的新配置（如 wbi 密钥、buvid3 cookie）：
    /// 这些更新应只对当前任务的流程生效——同时写全局会被并发 serve 任务的
    /// 后写者覆盖，造成跨任务污染（一个任务拿到的配置被另一个任务的尾部写入破坏）。
    /// 子方法仍返回新值由父流程经 <see cref="Apply"/> 传播到全局的场景，走 Apply。
    /// </summary>
    public static void ApplyToCurrentAsyncFlow(AppSettings settings)
        => _contextSettings.Value = settings;

    public static string COOKIE { get => Current.Cookie; set => Apply(Current with { Cookie = value }); }
    public static string TOKEN { get => Current.Token; set => Apply(Current with { Token = value }); }
    public static bool DEBUG_LOG { get => Current.DebugLog; set => Apply(Current with { DebugLog = value }); }
    public static string HOST { get => Current.Host; set => Apply(Current with { Host = value }); }
    public static string EPHOST { get => Current.EpHost; set => Apply(Current with { EpHost = value }); }
    public static string TVHOST { get => Current.TvHost; set => Apply(Current with { TvHost = value }); }
    public static string AREA { get => Current.Area; set => Apply(Current with { Area = value }); }
    public static string WBI { get => Current.Wbi; set => Apply(Current with { Wbi = value }); }
    public static bool SKIP_SSL_CHECK { get => Current.SkipSslCheck; set => Apply(Current with { SkipSslCheck = value }); }

    /// <summary>
    /// 只更新当前异步流的 Cookie（不写全局）。用于下载流程中注入 buvid3 等设备标识：
    /// serve 并发任务下，把注入结果写进全局会让后写者覆盖先写者的凭据，
    /// 造成跨账号串号。改动只对本任务流程生效，与 <see cref="ApplyToCurrentAsyncFlow"/> 同义。
    /// </summary>
    public static string COOKIE_FLOW { get => Current.Cookie; set => ApplyToCurrentAsyncFlow(Current with { Cookie = value }); }

    /// <summary>
    /// 只更新当前异步流的 Wbi（不写全局）。用于下载流程中提取的 wbi 密钥：
    /// 与 <see cref="ApplyToCurrentAsyncFlow"/> 同义，见其说明——serve 并发任务下
    /// 不应让一个任务的 wbi 覆盖全局后被其它任务读到。
    /// </summary>
    public static string WBI_FLOW { get => Current.Wbi; set => ApplyToCurrentAsyncFlow(Current with { Wbi = value }); }

    public static readonly Dictionary<string, string> qualitys = AppSettings.QualityMap;
}

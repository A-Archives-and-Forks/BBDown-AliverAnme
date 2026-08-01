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

    public static string COOKIE { get => Current.Cookie; set => Apply(Current with { Cookie = value }); }
    public static string TOKEN { get => Current.Token; set => Apply(Current with { Token = value }); }
    public static bool DEBUG_LOG { get => Current.DebugLog; set => Apply(Current with { DebugLog = value }); }
    public static string HOST { get => Current.Host; set => Apply(Current with { Host = value }); }
    public static string EPHOST { get => Current.EpHost; set => Apply(Current with { EpHost = value }); }
    public static string TVHOST { get => Current.TvHost; set => Apply(Current with { TvHost = value }); }
    public static string AREA { get => Current.Area; set => Apply(Current with { Area = value }); }
    public static string WBI { get => Current.Wbi; set => Apply(Current with { Wbi = value }); }
    public static bool SKIP_SSL_CHECK { get => Current.SkipSslCheck; set => Apply(Current with { SkipSslCheck = value }); }

    public static readonly Dictionary<string, string> qualitys = AppSettings.QualityMap;
}

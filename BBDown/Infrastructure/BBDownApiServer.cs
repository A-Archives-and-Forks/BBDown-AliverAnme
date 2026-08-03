using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using BBDown.Core;
using BBDown.Core.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
namespace BBDown;

public class BBDownApiServer
{
    private WebApplication? app;
    private readonly object _taskLock = new();
    private readonly List<DownloadTask> runningTasks = [];
    private readonly List<DownloadTask> finishedTasks = [];
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly string? _serveToken;

    /// <summary>
    /// 下载任务的生命周期 token。必须独立于 HTTP 请求：
    /// Minimal API 注入的 CancellationToken 是 HttpContext.RequestAborted，
    /// 它在响应写完后即失效，会让后台下载在客户端拿到 200 的瞬间被取消。
    /// 该 token 只在服务器关停时触发。
    /// </summary>
    private readonly CancellationTokenSource _serverLifetimeCts = new();
    // 已完成任务持久化：serve 是长驻进程，任务记录只留在内存会在重启后丢失
    private static readonly string _taskFile = Path.Combine(Environment.CurrentDirectory, "bbdown-tasks.json");

    // webhook 回调专用客户端：关闭自动重定向。共享的 AppHttpClient（AllowAutoRedirect=true）
    // 会在回调时跟随攻击者可控的 Location 重定向到内网/元数据地址，绕过 IsSafeCallbackUrl
    // 的地址校验。专用 handler 让回调只连 IsSafeCallbackUrl 校验过的原始地址。
    private static readonly HttpClient _callbackClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    { Timeout = TimeSpan.FromMinutes(2) };

    public BBDownApiServer(int maxConcurrent = 3, string? serveToken = null)
    {
        // 防御：maxConcurrent <= 0 会让 SemaphoreSlim 构造抛 ArgumentOutOfRangeException，
        // serve 作为长驻进程应以可读错误退出而非崩溃
        _concurrencyLimiter = new SemaphoreSlim(Math.Max(1, maxConcurrent), Math.Max(1, maxConcurrent));
        _serveToken = serveToken;
    }

    public void SetUpServer()
    {
        if (app is not null) return;
        LoadFinishedTasks();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.ConfigureHttpJsonOptions((options) =>
        {
            options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(options.SerializerOptions.TypeInfoResolver, AppJsonSerializerContext.Default);
        });
        builder.Services.AddCors((options) =>
        {
            options.AddPolicy("AllowAnyOrigin",
                policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
        });
        app = builder.Build();
        // 服务器关停时取消仍在进行的下载，避免进程挂在未完成的任务上
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            if (!_serverLifetimeCts.IsCancellationRequested) _serverLifetimeCts.Cancel();
        });
        app.UseCors("AllowAnyOrigin");
        // 可选 token 认证：serve 默认监听 0.0.0.0 且 CORS 全放开，
        // 配置了 --serve-token 后所有任务/查询端点要求 X-Serve-Token 匹配，否则 401。
        // 未配置 token 时保持向后兼容（仅本地信任环境）。
        if (!string.IsNullOrEmpty(_serveToken))
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path;
                bool isApi = path.StartsWithSegments("/get-tasks")
                    || path.StartsWithSegments("/add-task")
                    || path.StartsWithSegments("/remove-finished");
                if (isApi && context.Request.Headers["X-Serve-Token"] != _serveToken)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next();
            });
        }
        var taskStatusApi = app.MapGroup("/get-tasks");
        taskStatusApi.MapGet("/", handler: () =>
        {
            // Results.Json 的序列化发生在锁外（响应流式化阶段），必须在此快照集合，
            // 否则锁外遍历 runningTasks/finishedTasks 与并发 Add/RemoveAll 竞争，
            // 轻则读到不完整状态，重则抛集合修改异常。
            // 元素也需深拷贝：DownloadTask 被下载线程持续修改（SavePaths.Add、进度字段），
            // 共享对象在锁外序列化时仍会与写者竞争。
            List<DownloadTask> running, finished;
            lock (_taskLock)
            {
                running = runningTasks.Select(t => t.Snapshot()).ToList();
                finished = finishedTasks.Select(t => t.Snapshot()).ToList();
            }
            return Results.Json(new DownloadTaskCollection(running, finished), AppJsonSerializerContext.Default.DownloadTaskCollection);
        });
        taskStatusApi.MapGet("/running", handler: () =>
        {
            List<DownloadTask> snapshot;
            lock (_taskLock) { snapshot = runningTasks.Select(t => t.Snapshot()).ToList(); }
            return Results.Json(snapshot, AppJsonSerializerContext.Default.ListDownloadTask);
        });
        taskStatusApi.MapGet("/finished", handler: () =>
        {
            List<DownloadTask> snapshot;
            lock (_taskLock) { snapshot = finishedTasks.Select(t => t.Snapshot()).ToList(); }
            return Results.Json(snapshot, AppJsonSerializerContext.Default.ListDownloadTask);
        });
        taskStatusApi.MapGet("/{id}", (string id, CancellationToken token) =>
        {
            DownloadTask? task;
            lock (_taskLock)
            {
                task = finishedTasks.FirstOrDefault(a => a.Aid == id)
                    ?? runningTasks.FirstOrDefault(a => a.Aid == id);
            }
            if (task is null)
            {
                return Results.NotFound();
            }
            return Results.Json(task.Snapshot(), AppJsonSerializerContext.Default.DownloadTask);
        });
        app.MapPost("/add-task", (MyOptionBindingResult<ServeRequestOptions> bindingResult) =>
        {
            if (!bindingResult.IsValid)
            {
                return Results.BadRequest("输入有误");
            }
            var req = bindingResult.Result!;
            // 安全边界：网络传入的执行路径/参数/代理一律忽略。
            // Aria2cArgs 会被拼入 aria2c 命令行、Aria2cPath 会覆盖静态进程路径，
            // 允许客户端控制这些字段等价于任意命令/程序执行（RCE）。
            SanitizeUntrustedOptions(req);
            if (!IsSafeCallbackUrl(req.CallBackWebHook))
            {
                return Results.BadRequest("回调地址不合法：仅支持 http/https 且禁止指向回环或链路本地地址");
            }
            // 使用服务器生命周期 token 而非请求 token，否则下载会随响应结束一同被取消。
            // AddDownloadTaskAsync 内部已收敛所有异常，此处兜底避免遗漏变成无人观察的 Task 异常
            _ = AddDownloadTaskAsync(req, req.CallBackWebHook, _serverLifetimeCts.Token)
                .ContinueWith(t => Logger.LogError($"任务异常终止: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            return Results.Ok();
        });
        var finishedRemovalApi = app.MapGroup("remove-finished");
        finishedRemovalApi.MapGet("/", () =>
        {
            lock (_taskLock) { finishedTasks.RemoveAll(t => true); }
            PersistFinishedTasks();
            return Results.Ok();
        });
        finishedRemovalApi.MapGet("/failed", () =>
        {
            lock (_taskLock) { finishedTasks.RemoveAll(t => !t.IsSuccessful); }
            PersistFinishedTasks();
            return Results.Ok();
        });
        finishedRemovalApi.MapGet("/{id}", (string id) =>
        {
            lock (_taskLock) { finishedTasks.RemoveAll(t => t.Aid == id); }
            PersistFinishedTasks();
            return Results.Ok();
        });
    }

    public void Run(string url, CancellationToken cancellationToken = default)
    {
        if (app is null) return;
        bool result = Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult)
            && uriResult.Scheme == Uri.UriSchemeHttp;
        if (!result)
        {
            Logger.LogError($"{url} 不是合法的 http URL，url 示例：http://0.0.0.0:5000");
            Logger.LogWarn("如果您需要 https，请额外配置反向代理");
            // 抛异常而非仅设置 ExitCode：Environment.ExitCode 会被 Main 的返回值覆盖，
            // 导致监听地址无效时进程仍以 0 退出。ServeCommand 捕获后返回非零退出码。
            throw new ArgumentException($"{url} 不是合法的 http URL");
        }
        app.Urls.Add(url);
        try
        {
            Task.Run(() => app.RunAsync(cancellationToken)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // 收到取消信号（如 Ctrl+C），正常退出
        }
    }

    // 串行化任务文件的写入：多任务并发完成时若直接 File.WriteAllText，
    // 后写者会因 FileShare.None 抛 IOException 被吞成日志，丢失刚完成任务的记录
    private static readonly object _persistLock = new();

    /// <summary>
    /// 把已完成任务快照写入磁盘，serve 重启后可恢复。
    /// 写失败只降级为日志，不影响下载流程。
    /// </summary>
    private void PersistFinishedTasks()
    {
        try
        {
            List<DownloadTask> snapshot;
            lock (_taskLock) { snapshot = finishedTasks.Select(t => t.Snapshot()).ToList(); }
            var json = JsonSerializer.Serialize(snapshot, AppJsonSerializerContext.Default.ListDownloadTask);
            lock (_persistLock) { File.WriteAllText(_taskFile, json); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.LogDebug("持久化任务记录失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// serve 启动时恢复上次运行留下的已完成任务记录。文件损坏时静默忽略。
    /// </summary>
    private void LoadFinishedTasks()
    {
        try
        {
            if (!File.Exists(_taskFile)) return;
            var json = File.ReadAllText(_taskFile);
            var loaded = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListDownloadTask);
            if (loaded is null) return;
            lock (_taskLock) { finishedTasks.AddRange(loaded); }
            Logger.LogDebug("已恢复 {0} 条历史任务记录", loaded.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.LogDebug("加载历史任务记录失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 清除网络请求体中可能导致任意命令/程序执行或凭据外泄的字段。
    /// Aria2cArgs 会拼入 aria2c 命令行、Aria2cPath 会覆盖静态进程路径、
    /// Aria2cProxy 会追加进 Aria2cArgs —— 三者都不允许客户端控制。
    /// FFmpegPath/Mp4boxPath/WvdPath/Mp4decryptPath 同属"让服务器执行指定程序"的字段，
    /// 允许客户端控制等价于选择任意已存在的可执行文件；WorkDir 会改动进程级
    /// 工作目录，使并发任务的输出互相错乱——一并忽略。
    /// </summary>
    internal static void SanitizeUntrustedOptions(ServeRequestOptions req)
    {
        req.Aria2cArgs = "";
        req.Aria2cPath = "";
        req.Aria2cProxy = "";
        req.FFmpegPath = "";
        req.Mp4boxPath = "";
        req.WvdPath = "";
        req.Mp4decryptPath = "";
        req.WorkDir = "";
        // Insecure 会全局关闭 TLS 证书校验：serve 默认无 token，任意客户端 POST /add-task
        // 携带 {"insecure":true} 即可让携带操作者 SESSDATA 的请求跳过 TLS 校验被中间人截获。
        // serve 强制启用 TLS 校验，忽略该字段。
        req.Insecure = false;
        // UserAgent 是进程级静态字段：一个任务带自定义 UA 会污染此后所有任务
        // （SetUpWork 中空值不覆盖、非空值永久改写），serve 下统一用默认 UA。
        req.UserAgent = "";
        // NotifyWebhook 是 CLI 功能：serve 的 /add-task 请求体若携带它，会绕过
        // CallBackWebHook 的 SSRF 校验，让服务器向攻击者指定的任意地址 POST 任务数据。
        // serve 下的任务回调统一走经过 IsSafeCallbackUrl 校验的 CallBackWebHook。
        req.NotifyWebhook = "";
        // FilePattern/MultiFilePattern 会被 SetUpWork 当作 savePathFormat 拼进保存路径，
        // FormatSavePath 只替换占位符、字面量里的 ".." 段原样保留，BBDownMuxer 会按 savePath
        // 建目录——攻击者可借此任意创建目录/写入文件（路径穿越面）。serve 任务一律回落默认模板。
        req.FilePattern = "";
        req.MultiFilePattern = "";
        // DrmKeyHex/DrmKidHex 会经 DecryptDrmAsync 写入 mp4decrypt 的 key-file 参与解密，
        // 是客户端可控的密钥注入点。serve 任务一律回落 device.wvd 自动取钥；
        // 需要手动 --key/--kid 的操作者应使用 CLI 而非 API。
        req.DrmKeyHex = "";
        req.DrmKidHex = "";

        // host 字段决定凭据（Cookie/access_token）的发送目标：serve 默认无认证，
        // 若不校验，任意客户端可把请求指向自己的服务器，骗取操作者保存在
        // BBDown.data 的 B 站 Cookie（LoadCredentials 会在任务流中加载）。
        // 仅允许 B 站官方域名；空值/非官方一律回落官方默认值。
        req.Host = (string.IsNullOrWhiteSpace(req.Host) || !IsOfficialHost(req.Host)) ? "api.bilibili.com" : req.Host;
        req.EpHost = (string.IsNullOrWhiteSpace(req.EpHost) || !IsOfficialHost(req.EpHost)) ? "api.bilibili.com" : req.EpHost;
        req.TvHost = (string.IsNullOrWhiteSpace(req.TvHost) || !IsOfficialHost(req.TvHost)) ? "api.snm0516.aisee.tv" : req.TvHost;
        req.UposHost = (string.IsNullOrWhiteSpace(req.UposHost) || !IsOfficialHost(req.UposHost)) ? "" : req.UposHost;
    }

    /// <summary>B 站官方域名后缀白名单（含子域）。</summary>
    private static readonly string[] OfficialHostSuffixes =
        { "bilibili.com", "biliapi.net", "bilibili.tv", "aisee.tv", "bilivideo.com", "hdslb.com" };

    /// <summary>
    /// host 字段是否指向 B 站官方域名。空值视为合法（回落默认）；
    /// 支持 "api.bilibili.com" 与 "https://api.bilibili.com" 两种写法。
    /// 只接受规范化的纯主机名：带路径/斜杠/用户信息/非默认端口等形态一律拒绝，
    /// 防止攻击者用 "evil.com/.bilibili.com" 这类斜杠混淆串在纯后缀匹配下被放行，
    /// 使携带操作者 SESSDATA 的请求发往攻击者主机。
    /// </summary>
    internal static bool IsOfficialHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;

        string hostname = host;
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri))
        {
            // 带 scheme 的写法：必须 http/https、无 userinfo、无路径/query/fragment、默认端口
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false; // userinfo 可伪装信任域
            if (uri.AbsolutePath.Length > 1) return false;         // 非空路径（含 "/" 以外的路径段）
            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            if (!uri.IsDefaultPort) return false;
            hostname = uri.Host;
        }
        else if (host.Contains('/') || host.Contains('\\') || host.Contains('@') ||
                 host.Contains('?') || host.Contains('#') || host.Contains(':') || host.Contains('['))
        {
            // 非绝对 URL 却含协议/路径/用户信息/端口/IPv6 字面量等分隔符：不是合法纯主机名，拒绝
            return false;
        }

        return OfficialHostSuffixes.Any(s =>
            hostname.Equals(s, StringComparison.OrdinalIgnoreCase)
            || hostname.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 回调地址 SSRF 防护：仅允许 http/https 绝对地址，
    /// 且禁止指向回环（127.0.0.1/::1/localhost）、链路本地（169.254.x / fe80::）与
    /// 云元数据面（169.254.0.0/16）。RFC1918 私网段仅在回调 URL 是**字面 IP** 时放行
    /// （操作者直接配置的局域网回调是 serve 正常用法）；经域名解析出的内网地址一律拒绝，
    /// 防止攻击者用解析到内网的域名（DNS 重绑定）把回调打向内网。
    /// dnsResolver 供测试注入（生产用系统 DNS）。
    /// 注意：域名在"add 时校验"与"回调时刻连接"之间存在 DNS 重绑定窗口（短 TTL 域名
    /// 可先解析为公网通过校验、回调时改指 169.254.169.254/内网）。因此 AddDownloadTaskAsync
    /// 在每次回调建立连接前都会再调用一次本方法复查，把窗口压缩到连接前瞬间。
    /// </summary>
    internal static bool IsSafeCallbackUrl(string? url, Func<string, IPAddress[]>? dnsResolver = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return true; // 未配置回调视为合法
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        bool hostIsLiteralIp = IPAddress.TryParse(uri.Host, out var literalIp) && literalIp is not null;

        if (hostIsLiteralIp)
        {
            // IPv4-mapped IPv6（如 [::ffff:169.254.169.254]）会把下方的 169.254 检查绕过
            // （其 AddressFamily 是 InterNetworkV6），统一映射回 IPv4 后再做检查。
            if (literalIp!.IsIPv4MappedToIPv6) literalIp = literalIp.MapToIPv4();
            // 字面 IP 是操作者显式配置的地址：仅拦回环/链路本地/云元数据。
            // RFC1918 内网字面 IP 放行（局域网回调是 serve 正常用法），
            // 因为字面 IP 不涉及 DNS 重绑定，攻击者无法借它打内网——攻击者构造的
            // "域名回调"永远走下方 DNS 解析分支（RFC1918 已拒绝）。若需进一步收紧，
            // 局域网回调用户应配置 --serve-token 或前置反向代理。
            if (IPAddress.IsLoopback(literalIp)) return false;
            if (literalIp.IsIPv6LinkLocal) return false;
            if (literalIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var b = literalIp.GetAddressBytes();
                if (b.Length == 4 && b[0] == 169 && b[1] == 254) return false; // 169.254.0.0/16 云元数据面
                if (b.All(x => x == 0)) return false; // 0.0.0.0：连接时绑定到回环
            }
            else if (literalIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                var b6 = literalIp.GetAddressBytes();
                if (b6.Length == 16 && b6.All(x => x == 0)) return false; // [::]
            }
            return true;
        }

        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        // 域名回调的 DNS 重绑定缺口：攻击者注册解析到 169.254.169.254 / 内网地址的域名
        // （如 metadata.google.internal），仅比对字符串会放行，任务完成时 HttpClient 才解析 DNS。
        // 这里提前解析并校验全部地址；域名解析出的任一地址命中回环/链路本地/169.254/RFC1918/ULA
        // 即拒绝——内网地址只允许"字面 IP"形式的显式配置，域名一律要求公网可达。
        try
        {
            var addresses = (dnsResolver ?? Dns.GetHostAddresses)(host);
            foreach (var addr in addresses)
            {
                var resolvedIp = addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr;
                if (IsBlockedAddress(resolvedIp)) return false;
            }
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            // 域名无法解析：回调必然失败，按不安全处理
            return false;
        }
    }

    /// <summary>
    /// 判定地址是否属于应拒绝的敏感/内网段：回环、链路本地、云元数据（169.254/16）、
    /// RFC1918 私网段（10/8、172.16/12、192.168/16）、CGNAT（100.64/10）与 IPv6 ULA（fc00::/7）。
    /// </summary>
    private static bool IsBlockedAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv6LinkLocal) return true;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            // 169.254.0.0/16 云元数据面
            if (b[0] == 169 && b[1] == 254) return true;
            // RFC1918：10.0.0.0/8、172.16.0.0/12、192.168.0.0/16
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            // CGNAT 100.64.0.0/10
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
            return false;
        }
        // IPv6 ULA fc00::/7
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return b.Length == 16 && (b[0] & 0xfe) == 0xfc;
        }
        return false;
    }

    private async Task<DownloadTask> AddDownloadTaskAsync(MyOption option, string? callBackWebHook = null, CancellationToken cancellationToken = default)
    {
        // 解析 aid 前先把本任务的完整配置写入当前 async 流，用干净的 AppSettings 起步：
        // 1) 避免解析阶段读到全局 _settings 里上一个任务留下的 cookie（跨账号解析）；
        // 2) 避免 Host/EpHost/TvHost/Area 等字段继承上个任务的覆盖值（cookie 发往错误 host）。
        // 用 option 的显式值（MyOption 默认值即官方地址），accessToken 可能被 JSON null 置空。
        Config.Apply(new AppSettings
        {
            Cookie = option.Cookie ?? "",
            Token = (option.AccessToken ?? "").Replace("access_token=", ""),
            DebugLog = option.Debug,
            Host = option.Host,
            EpHost = option.EpHost,
            TvHost = option.TvHost,
            Area = option.Area ?? "",
            SkipSslCheck = option.Insecure,
        });
        string aid;
        try
        {
            aid = await BBDownUtil.GetAvIdAsync(option.Url);
        }
        catch (Exception e)
        {
            // 链接无法解析时客户端已经收到 200，必须留下一条失败记录，
            // 否则用户既等不到结果也查不到原因
            var rejected = new DownloadTask(option.Url, option.Url, DateTimeOffset.Now.ToUnixTimeSeconds())
            {
                ErrorMessage = e.Message,
                TaskFinishTime = DateTimeOffset.Now.ToUnixTimeSeconds(),
            };
            lock (_taskLock) { finishedTasks.Add(rejected); }
            PersistFinishedTasks();
            Logger.LogError($"解析链接失败: {option.Url} - {e.Message}");
            return rejected;
        }

        DownloadTask task = new(aid, option.Url, DateTimeOffset.Now.ToUnixTimeSeconds());
        // 查重与入队必须在同一把锁内完成，否则并发提交同一 aid 时
        // 两个请求都能通过 FirstOrDefault 检查并各自入队，导致重复下载
        lock (_taskLock)
        {
            var runningTask = runningTasks.FirstOrDefault(t => t.Aid == aid);
            if (runningTask is not null)
            {
                return runningTask;
            }
            runningTasks.Add(task);
        }
        try
        {
            await _concurrencyLimiter.WaitAsync(cancellationToken);
            try
            {
                var (encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats, input, savePathFormat, lang, aidOri, delay) = Program.SetUpWork(option);
                var (fetchedAid, vInfo, apiType) = await Program.GetVideoInfoAsync(option, aidOri, input);
                task.Title = vInfo.Title;
                task.Pic = vInfo.Pic;
                task.VideoPubTime = vInfo.PubTime;
                await Program.DownloadPagesAsync(option, vInfo, encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats,
                            input, savePathFormat, lang, fetchedAid, delay, apiType, task, cancellationToken);
                task.IsSuccessful = true;
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        }
        // 捕获所有异常：任何漏网的异常类型都会跳过下方的收尾逻辑，
        // 使任务永久滞留在 runningTasks 中，该 aid 之后再也无法重新下载。
        catch (Exception e)
        {
            bool debugMode = option.Debug || Config.Current.DebugLog;
            var displayMsg = debugMode ? e.ToString() : e.Message;
            task.ErrorMessage = displayMsg;
            Logger.LogError($"{aid} 下载失败: {e.Message}");
            Logger.LogDebug("异常详情: {0}", displayMsg);
        }
        task.TaskFinishTime = DateTimeOffset.Now.ToUnixTimeSeconds();
        if (task.IsSuccessful)
        {
            task.Progress = 1f;
            var elapsed = task.TaskFinishTime - task.TaskCreateTime;
            task.DownloadSpeed = elapsed > 0
                ? (double)(task.TotalDownloadedBytes / elapsed)
                : 0;
        }
        lock (_taskLock)
        {
            runningTasks.Remove(task);
            finishedTasks.Add(task);
        }
        PersistFinishedTasks();

        // Webhook 回调
        if (!string.IsNullOrEmpty(callBackWebHook))
        {
            // 回调时刻复查 SSRF：add 时 IsSafeCallbackUrl 解析的 DNS 在分钟级窗口内可能已被
            // 攻击者改指 169.254.169.254 / 内网（短 TTL 域名重绑定）。此处把窗口压缩到
            // 连接建立前瞬间再解析一次，命中即跳过本次回调；add 时校验仍保留（防手误配置）。
            if (!IsSafeCallbackUrl(callBackWebHook))
            {
                Logger.LogWarn($"回调地址不合法，已跳过本次回调: {callBackWebHook}");
            }
            else
            {
                // 序列化共享对象前用 Snapshot() 深拷贝：与 /get-tasks 查询端点一致，
                // 避免未来任何并发写者在序列化期间修改 SavePaths 引发竞态。
                string? jsonContent = JsonSerializer.Serialize(task.Snapshot(), AppJsonSerializerContext.Default.DownloadTask);
                try
                {
                    await _callbackClient.PostAsync(callBackWebHook, new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json"));
                }
                catch (Exception e) when (e is HttpRequestException)
                {
                    Logger.LogDebug("回调失败: {0}", e.Message);
                }
            }
        }

        return task;
    }
}

public record DownloadTask(string Aid, string Url, long TaskCreateTime)
{
    [JsonInclude]
    public string? Title = null;
    [JsonInclude]
    public string? Pic = null;
    [JsonInclude]
    public long? VideoPubTime = null;
    [JsonInclude]
    public long? TaskFinishTime = null;
    [JsonInclude]
    public double Progress = 0f;
    [JsonInclude]
    public double DownloadSpeed = 0f;
    [JsonInclude]
    public double TotalDownloadedBytes = 0f;
    [JsonInclude]
    public bool IsSuccessful = false;
    [JsonInclude]
    public string? ErrorMessage = null;

    [JsonInclude]
    public List<string> SavePaths = new();

    // 保护 SavePaths 的读写锁：下载线程持续 Add，而 /get-tasks 的 Snapshot 深拷贝会枚举
    // SavePaths，若撞上并发 Add 抛 InvalidOperationException（List 版本变更）。
    // 写者一律经 AddSavePath 走这把锁；_savePathLock 不能是 primary constructor 属性。
    private readonly object _savePathLock = new();

    /// <summary>受控写入口：与 Snapshot 的深拷贝在同一把锁下，避免枚举期间被并发修改。</summary>
    public void AddSavePath(string path)
    {
        lock (_savePathLock) { SavePaths.Add(path); }
    }

    /// <summary>
    /// 深拷贝快照：下载线程会持续修改本对象的 Progress/SavePaths 等字段，
    /// /get-tasks 在锁外序列化共享对象时，SavePaths.Add 撞上枚举会抛
    /// InvalidOperationException。快照的 SavePaths 是独立副本，序列化即安全。
    /// </summary>
    public DownloadTask Snapshot()
    {
        List<string> paths;
        lock (_savePathLock) { paths = new List<string>(SavePaths); }
        return new(Aid, Url, TaskCreateTime)
        {
            Title = Title,
            Pic = Pic,
            VideoPubTime = VideoPubTime,
            TaskFinishTime = TaskFinishTime,
            Progress = Progress,
            DownloadSpeed = DownloadSpeed,
            TotalDownloadedBytes = TotalDownloadedBytes,
            IsSuccessful = IsSuccessful,
            ErrorMessage = ErrorMessage,
            SavePaths = paths,
        };
    }
};
public record DownloadTaskCollection(List<DownloadTask> Running, List<DownloadTask> Finished);

record struct MyOptionBindingResult<T>(T? Result, Exception? Exception)
{
    public bool IsValid => Exception is null;

    public static async ValueTask<MyOptionBindingResult<T>> BindAsync(HttpContext httpContext)
    {
        try
        {
            // 大小写不敏感：客户端可能用 {url:...} 或 {Url:...}，两者都应绑定成功。
            // 用带该选项的 context 生成 TypeInfo（源生成，AOT 安全）。
            var context = new SourceGenerationContext(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            JsonTypeInfo? jsonTypeInfo = context.GetTypeInfo(typeof(T));
            if (jsonTypeInfo is not JsonTypeInfo<T> typedInfo)
            {
                return new(default, new InvalidOperationException($"Cannot find TypeInfo for type {typeof(T)}"));
            }
            var json = await new StreamReader(httpContext.Request.Body).ReadToEndAsync(httpContext.RequestAborted);
            var item = JsonSerializer.Deserialize<T>(json, typedInfo);

            if (item is null) return new(default, new NoNullAllowedException());

            return new((T)item, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new(default, ex);
        }
    }
}

[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(DownloadTask))]
[JsonSerializable(typeof(List<DownloadTask>))]
[JsonSerializable(typeof(DownloadTaskCollection))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{

}

[JsonSerializable(typeof(MyOption))]
[JsonSerializable(typeof(ServeRequestOptions))]
internal partial class SourceGenerationContext : JsonSerializerContext
{

}

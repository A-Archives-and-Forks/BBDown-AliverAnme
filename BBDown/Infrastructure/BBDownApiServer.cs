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
            // 轻则读到不完整状态，重则抛集合修改异常
            List<DownloadTask> running, finished;
            lock (_taskLock)
            {
                running = runningTasks.ToList();
                finished = finishedTasks.ToList();
            }
            return Results.Json(new DownloadTaskCollection(running, finished), AppJsonSerializerContext.Default.DownloadTaskCollection);
        });
        taskStatusApi.MapGet("/running", handler: () =>
        {
            List<DownloadTask> snapshot;
            lock (_taskLock) { snapshot = runningTasks.ToList(); }
            return Results.Json(snapshot, AppJsonSerializerContext.Default.ListDownloadTask);
        });
        taskStatusApi.MapGet("/finished", handler: () =>
        {
            List<DownloadTask> snapshot;
            lock (_taskLock) { snapshot = finishedTasks.ToList(); }
            return Results.Json(snapshot, AppJsonSerializerContext.Default.ListDownloadTask);
        });
        taskStatusApi.MapGet("/{id}", (string id, CancellationToken token) =>
        {
            DownloadTask? task, rtask;
            lock (_taskLock)
            {
                task = finishedTasks.FirstOrDefault(a => a.Aid == id);
                rtask = runningTasks.FirstOrDefault(a => a.Aid == id);
            }
            if (rtask is not null) task = rtask;
            if (task is null)
            {
                return Results.NotFound();
            }
            return Results.Json(task, AppJsonSerializerContext.Default.DownloadTask);
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

    /// <summary>
    /// 把已完成任务快照写入磁盘，serve 重启后可恢复。
    /// 写失败只降级为日志，不影响下载流程。
    /// </summary>
    private void PersistFinishedTasks()
    {
        try
        {
            List<DownloadTask> snapshot;
            lock (_taskLock) { snapshot = finishedTasks.ToList(); }
            var json = JsonSerializer.Serialize(snapshot, AppJsonSerializerContext.Default.ListDownloadTask);
            File.WriteAllText(_taskFile, json);
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
    /// 清除网络请求体中可能导致任意命令/程序执行的字段。
    /// Aria2cArgs 会拼入 aria2c 命令行、Aria2cPath 会覆盖静态进程路径、
    /// Aria2cProxy 会追加进 Aria2cArgs —— 三者都不允许客户端控制。
    /// </summary>
    internal static void SanitizeUntrustedOptions(ServeRequestOptions req)
    {
        req.Aria2cArgs = "";
        req.Aria2cPath = "";
        req.Aria2cProxy = "";
    }

    /// <summary>
    /// 回调地址 SSRF 防护：仅允许 http/https 绝对地址，
    /// 且禁止指向回环（127.0.0.1/::1/localhost）与链路本地（169.254.x / fe80::）。
    /// 保留 RFC1918 私网段：局域网回调是 serve 模式的正常用法。
    /// </summary>
    internal static bool IsSafeCallbackUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true; // 未配置回调视为合法
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            // IPv4-mapped IPv6（如 [::ffff:169.254.169.254]）会把下方的 169.254 检查绕过
            // （其 AddressFamily 是 InterNetworkV6），统一映射回 IPv4 后再做回环/链路本地检查
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

            if (IPAddress.IsLoopback(ip)) return false;
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                if (b.Length == 4 && b[0] == 169 && b[1] == 254) return false; // 169.254.0.0/16 云元数据面
            }
            if (ip.IsIPv6LinkLocal) return false;
            return true;
        }
        var host = uri.Host;
        return !host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DownloadTask> AddDownloadTaskAsync(MyOption option, string? callBackWebHook = null, CancellationToken cancellationToken = default)
    {
        // 解析 aid 前先把本任务的认证配置写入当前 async 流：
        // URL 解析阶段的网络请求（GetAvIdAsync 等）若此时读到全局 _settings，
        // 会拿到上一个任务留下的 cookie，造成跨账号解析（region/VIP 状态错乱）。
        Config.Apply(Config.Current with
        {
            Cookie = option.Cookie,
            Token = option.AccessToken.Replace("access_token=", ""),
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
            string? jsonContent = JsonSerializer.Serialize(task, AppJsonSerializerContext.Default.DownloadTask);
            try
            {
                await HTTPUtil.AppHttpClient.PostAsync(callBackWebHook, new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json"));
            }
            catch (Exception e) when (e is HttpRequestException)
            {
                Logger.LogDebug("回调失败: {0}", e.Message);
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

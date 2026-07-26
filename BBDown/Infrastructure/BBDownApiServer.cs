using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

    /// <summary>
    /// 下载任务的生命周期 token。必须独立于 HTTP 请求：
    /// Minimal API 注入的 CancellationToken 是 HttpContext.RequestAborted，
    /// 它在响应写完后即失效，会让后台下载在客户端拿到 200 的瞬间被取消。
    /// 该 token 只在服务器关停时触发。
    /// </summary>
    private readonly CancellationTokenSource _serverLifetimeCts = new();

    public BBDownApiServer(int maxConcurrent = 3)
    {
        _concurrencyLimiter = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public void SetUpServer()
    {
        if (app is not null) return;
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
        var taskStatusApi = app.MapGroup("/get-tasks");
        taskStatusApi.MapGet("/", handler: () =>
        {
            lock (_taskLock)
            {
                return Results.Json(new DownloadTaskCollection(runningTasks, finishedTasks), AppJsonSerializerContext.Default.DownloadTaskCollection);
            }
        });
        taskStatusApi.MapGet("/running", handler: () =>
        {
            lock (_taskLock)
            {
                return Results.Json(runningTasks, AppJsonSerializerContext.Default.ListDownloadTask);
            }
        });
        taskStatusApi.MapGet("/finished", handler: () =>
        {
            lock (_taskLock)
            {
                return Results.Json(finishedTasks, AppJsonSerializerContext.Default.ListDownloadTask);
            }
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
            return Results.Ok();
        });
        finishedRemovalApi.MapGet("/failed", () =>
        {
            lock (_taskLock) { finishedTasks.RemoveAll(t => !t.IsSuccessful); }
            return Results.Ok();
        });
        finishedRemovalApi.MapGet("/{id}", (string id) =>
        {
            lock (_taskLock) { finishedTasks.RemoveAll(t => t.Aid == id); }
            return Results.Ok();
        });
    }

    public void Run(string url)
    {
        if (app is null) return;
        bool result = Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult)
            && uriResult.Scheme == Uri.UriSchemeHttp;
        if (!result)
        {
            Logger.LogError($"{url} 不是合法的 http URL，url 示例：http://0.0.0.0:5000");
            Logger.LogWarn("如果您需要 https，请额外配置反向代理");
            Environment.ExitCode = 1;
            return;
        }
        app.Run(url);
    }

    private async Task<DownloadTask> AddDownloadTaskAsync(MyOption option, string? callBackWebHook = null, CancellationToken cancellationToken = default)
    {
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
            Logger.LogError($"解析链接失败: {option.Url} - {e.Message}");
            return rejected;
        }

        DownloadTask? runningTask;
        lock (_taskLock) { runningTask = runningTasks.FirstOrDefault(t => t.Aid == aid); }
        if (runningTask is not null)
        {
            return runningTask;
        };
        var task = new DownloadTask(aid, option.Url, DateTimeOffset.Now.ToUnixTimeSeconds());
        lock (_taskLock) { runningTasks.Add(task); }
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
            JsonTypeInfo? jsonTypeInfo = SourceGenerationContext.Default.GetTypeInfo(typeof(T));
            if (jsonTypeInfo is null)
            {
                return new(default, new InvalidOperationException($"Cannot find TypeInfo for type {typeof(T)}"));
            }
            var item = await httpContext.Request.ReadFromJsonAsync(jsonTypeInfo);

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

using System.Net;
using System.Text;
using BBDown;
using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// C4：API 层统一重试 + "200+HTML 风控页"识别 + 超时重试（F5）。
/// GetWebSourceAsync/GetPostResponseAsync 仅对 5xx、传输层失败（HttpRequestException）
/// 与超时（用户 token 未取消的 OperationCanceledException）按 MaxRetryCount 指数退避重试；
/// 4xx 风控/参数错不重试；200+HTML 抛 RiskControlResponseException 替代下游 JsonException/grpc 乱码。
/// </summary>
public class HttpUtilRetryTests
{
    [Fact]
    public async Task GetWebSource_RetriesOn500_ThenSucceeds()
    {
        using var server = new ScriptedServer((500, "boom"), (200, """{"code":0}"""));
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 10 });
            var body = await HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/api", token: CancellationToken.None);
            Assert.Equal("""{"code":0}""", body);
            Assert.Equal(2, server.RequestCount); // 1 次 500 + 1 次成功
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000 });
        }
    }

    [Fact]
    public async Task GetWebSource_500ExhaustsRetries_Throws()
    {
        using var server = new ScriptedServer((500, "e1"), (500, "e2"), (500, "e3"));
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 10 });
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/api", token: CancellationToken.None));
            Assert.Equal(3, server.RequestCount); // 恰好重试到耗尽，没有多余请求
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000 });
        }
    }

    [Fact]
    public async Task GetWebSource_RetryBudget_ClampedToThree()
    {
        // API 层重试有界（min(MaxRetryCount,3)）：--retry-count 可到 100，但 API 层最多 3 次尝试，
        // 避免与页面级重试乘积放大请求数/总等待，也避免总退避超过 WBI 签名 wts 的时效窗口
        using var server = new ScriptedServer((500, "e1"), (500, "e2"), (500, "e3"));
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 10, RetryDelayMs = 10 });
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/api", token: CancellationToken.None));
            Assert.Equal(3, server.RequestCount); // 不随 MaxRetryCount=10 放大
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000 });
        }
    }

    [Fact]
    public async Task GetWebSource_404_DoesNotRetry()
    {
        // 4xx（风控/参数/鉴权错）重试只会加重风控状态：必须一次即抛、不重试
        using var server = new ScriptedServer((404, "not found"));
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 10 });
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/api", token: CancellationToken.None));
            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
            Assert.Equal(1, server.RequestCount);
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000 });
        }
    }

    [Fact]
    public async Task GetWebSource_WindmillHtml_ThrowsRiskControlResponseException_WithoutRetrying()
    {
        // B 站风控/登录墙以 200 返回 HTML：必须明确报"疑似风控页"，且不按 5xx 重试
        using var server = new ScriptedServer((200, "<html><body>风险验证</body></html>"));
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 10 });
            await Assert.ThrowsAsync<RiskControlResponseException>(() =>
                HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/api", token: CancellationToken.None));
            Assert.Equal(1, server.RequestCount);
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000 });
        }
    }

    [Fact]
    public async Task GetWebSource_WindmillHtml_WithLeadingWhitespace_StillDetected()
    {
        // 部分 WAF/风控页在 '<html' 前带换行/空白：识别必须先 TrimStart，否则裸 JsonException 回潮
        using var server = new ScriptedServer((200, "\n  <html><body>风险验证</body></html>"));
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 10 });
            await Assert.ThrowsAsync<RiskControlResponseException>(() =>
                HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/api", token: CancellationToken.None));
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000 });
        }
    }

    [Fact]
    public async Task GetWebSource_RejectHtmlFalse_ReturnsHtml()
    {
        // 番剧页/player.so 等确实抓取 HTML/XML 的调用点传 rejectHtml:false，必须原样返回
        using var server = new ScriptedServer((200, "<html>some page</html>"));
        var html = await HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/page",
            token: CancellationToken.None, rejectHtml: false);
        Assert.Equal("<html>some page</html>", html);
    }

    [Fact]
    public async Task GetPostResponse_WindmillHtml_ThrowsRiskControlResponseException()
    {
        // grpc 帧首字节是压缩标志（0/1），首字节 '<' 说明拿到 HTML 错误/风控页
        using var server = new ScriptedServer((200, "<html>error page</html>"));
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 10 });
            await Assert.ThrowsAsync<RiskControlResponseException>(() =>
                HTTPUtil.GetPostResponseAsync($"http://127.0.0.1:{server.Port}/grpc", new byte[] { 0x00, 0x01 }, token: CancellationToken.None));
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000 });
        }
    }

    [Fact]
    public async Task GetWebSource_Timeout_IsRetriedThenThrows()
    {
        // F5：超时（用户 token 未取消的 OperationCanceledException）是最常见的瞬时传输层故障，
        // 必须与 5xx 同权参与 API 层有界重试，而不是首次即失败。重试耗尽后转为 TimeoutException 抛出。
        using var server = new StallingServer();
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 10, ApiTimeoutMs = 100 });
            await Assert.ThrowsAnyAsync<TimeoutException>(() =>
                HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/api", token: CancellationToken.None));
            Assert.Equal(3, server.RequestCount); // 超时被重试到耗尽，没有多余请求
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000, ApiTimeoutMs = 120000 });
        }
    }

    [Fact]
    public async Task GetPostResponse_Timeout_IsRetriedThenThrows()
    {
        // F5：grpc API 的超时同样参与有界重试（timeoutCts 触发、用户 token 未取消）
        using var server = new StallingServer();
        try
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 10, ApiTimeoutMs = 100 });
            await Assert.ThrowsAnyAsync<TimeoutException>(() =>
                HTTPUtil.GetPostResponseAsync($"http://127.0.0.1:{server.Port}/grpc", new byte[] { 0x00 }, token: CancellationToken.None));
            Assert.Equal(3, server.RequestCount);
        }
        finally
        {
            Config.ApplyToCurrentAsyncFlow(Config.Current with { MaxRetryCount = 3, RetryDelayMs = 3000, ApiTimeoutMs = 120000 });
        }
    }

    [Fact]
    public async Task TryDownloadSubtitle_AssUrlReturningHtml_DegradesAndReturnsFalse()
    {
        // F2：字幕是装饰性资源，URL 返回 200+HTML 风控页时不得中止页面下载，只降级为跳过。
        using var server = new ScriptedServer((200, "<html>风险验证</html>"));
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-sub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sub = new BBDown.Core.Entity.Entity.Subtitle
        {
            url = $"http://127.0.0.1:{server.Port}/sub.ass",
            lan = "zh-Hans",
            path = Path.Combine(dir, "sub.ass"),
        };
        try
        {
            bool ok = await Program.TryDownloadSubtitleAsync(sub, CancellationToken.None);
            Assert.False(ok);
            Assert.False(File.Exists(sub.path)); // 失败不落盘
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task TryDownloadSubtitle_Ass_SuccessWritesFile()
    {
        using var server = new ScriptedServer((200, "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,你好"));
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-sub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sub = new BBDown.Core.Entity.Entity.Subtitle
        {
            url = $"http://127.0.0.1:{server.Port}/sub.ass",
            lan = "zh-Hans",
            path = Path.Combine(dir, "sub.ass"),
        };
        try
        {
            bool ok = await Program.TryDownloadSubtitleAsync(sub, CancellationToken.None);
            Assert.True(ok);
            Assert.Equal("Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,你好", await File.ReadAllTextAsync(sub.path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task TryDownloadSubtitle_SubOnlyMode_RethrowsOnHtml()
    {
        // F2：SubOnly 模式下字幕是唯一产物，失败不降级而抛出（交由页面级重试恢复），
        // 与装饰性（非 SubOnly）降级行为区分。
        using var server = new ScriptedServer((200, "<html>风险验证</html>"));
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-sub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sub = new BBDown.Core.Entity.Entity.Subtitle
        {
            url = $"http://127.0.0.1:{server.Port}/sub.ass",
            lan = "zh-Hans",
            path = Path.Combine(dir, "sub.ass"),
        };
        try
        {
            await Assert.ThrowsAsync<RiskControlResponseException>(() =>
                Program.TryDownloadSubtitleAsync(sub, CancellationToken.None, degradeOnFailure: false));
            Assert.False(File.Exists(sub.path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>按脚本依次返回 (状态码, 响应体) 的本地服务，用于验证重试与风控页识别。</summary>
    private sealed class ScriptedServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Queue<(int Status, string Body)> _responses;
        private readonly Task _loop;
        public int Port { get; }
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        public ScriptedServer(params (int Status, string Body)[] responses)
        {
            _responses = new Queue<(int, string)>(responses);
            Port = TestPort.Allocate();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        var ctx = await _listener.GetContextAsync();
                        try
                        {
                            Interlocked.Increment(ref _requestCount);
                            var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : (500, "no more scripted responses");
                            var bytes = Encoding.UTF8.GetBytes(body);
                            ctx.Response.StatusCode = status;
                            ctx.Response.ContentLength64 = bytes.Length;
                            await ctx.Response.OutputStream.WriteAsync(bytes, _cts.Token);
                            ctx.Response.Close();
                        }
                        catch { /* 客户端中止：忽略 */ }
                    }
                }
                catch (HttpListenerException) { /* 服务停止 */ }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }

    /// <summary>返回响应头后让响应体停滞的本地服务：用于验证超时被纳入 API 层有界重试。</summary>
    private sealed class StallingServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private int _requestCount;
        public int Port { get; }
        public int RequestCount => Volatile.Read(ref _requestCount);

        public StallingServer()
        {
            Port = TestPort.Allocate();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        var ctx = await _listener.GetContextAsync();
                        // 每个请求独立任务处理，避免首个请求的停滞阻塞后续重试请求的受理
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                Interlocked.Increment(ref _requestCount);
                                var resp = ctx.Response;
                                resp.StatusCode = 200;
                                // 声明 100 字节但只写 1 字节，随后停滞：客户端 ReadAsByteArrayAsync/
                                // ReadAsStringAsync 会一直等剩余字节直到 ApiTimeoutMs 超时
                                resp.ContentLength64 = 100;
                                await resp.OutputStream.WriteAsync(new byte[1], _cts.Token);
                                await Task.Delay(Timeout.Infinite, _cts.Token);
                            }
                            catch { /* 客户端中止：忽略 */ }
                        });
                    }
                }
                catch (HttpListenerException) { /* 服务停止 */ }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }
}
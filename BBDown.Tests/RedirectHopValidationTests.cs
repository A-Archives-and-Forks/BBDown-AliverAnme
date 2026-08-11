using System.Net;
using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// 逐跳重定向校验测试：验证 <see cref="HTTPUtil.GetWebLocationCheckedAsync"/> 在
/// 每一跳的 Location 被请求之前就用回调校验，非可信目标会被拦截（不会真正访问）。
/// </summary>
public class RedirectHopValidationTests
{
    /// <summary>起一个本地 HTTP 服务，按路径返回重定向或终态。Dispose 时停掉。</summary>
    private sealed class LocalRedirectServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public int Port { get; }

        public LocalRedirectServer(
            Dictionary<string, (int Status, string Location)> routes,
            int terminalStatus = 200)
        {
            var port = 24000 + (Environment.ProcessId % 2000) + (routes.Count % 100);
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            Port = port;
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        var ctx = await _listener.GetContextAsync();
                        try
                        {
                            var path = ctx.Request.Url!.AbsolutePath;
                            if (routes.TryGetValue(path, out var route))
                            {
                                ctx.Response.StatusCode = route.Status;
                                ctx.Response.RedirectLocation = route.Location;
                            }
                            else
                            {
                                ctx.Response.StatusCode = terminalStatus;
                            }
                            ctx.Response.Close();
                        }
                        catch
                        {
                            // 客户端中止连接等：忽略
                        }
                    }
                }
                catch (HttpListenerException)
                {
                    // 服务停止
                }
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

    [Fact]
    public async Task GetWebLocationCheckedAsync_UntrustedRedirect_StopsBeforeNextHop()
    {
        // 攻击链：可信入口 → 302 指向非可信主机。校验回调必须拦截，绝不访问 evil.com。
        using var server = new LocalRedirectServer(new()
        {
            { "/entry", (302, "http://evil.example.com/final") },
        });
        var baseUrl = $"http://127.0.0.1:{server.Port}";
        var result = await HTTPUtil.GetWebLocationCheckedAsync($"{baseUrl}/entry",
            uri => uri.Host == "127.0.0.1", token: CancellationToken.None);

        // 非可信跳转被拦截：返回原入口地址，而不是跟随到 evil.com
        Assert.Equal($"{baseUrl}/entry", result);
    }

    [Fact]
    public async Task GetWebLocationCheckedAsync_TrustedRedirect_FollowsToFinal()
    {
        // 可信入口 → 302 → 可信终点：应跟随到终点
        using var server = new LocalRedirectServer(new()
        {
            { "/entry", (302, "/final") },
        });
        var baseUrl = $"http://127.0.0.1:{server.Port}";
        var result = await HTTPUtil.GetWebLocationCheckedAsync($"{baseUrl}/entry",
            uri => uri.Host == "127.0.0.1", token: CancellationToken.None);

        Assert.Equal($"{baseUrl}/final", result);
    }

    [Fact]
    public async Task GetWebLocationCheckedAsync_RedirectChain_LimitedHops()
    {
        // 无限重定向环：必须被 maxHops 上限截断，而非无限跟随
        using var server = new LocalRedirectServer(new()
        {
            { "/a", (302, "/b") },
            { "/b", (302, "/a") },
        });
        var baseUrl = $"http://127.0.0.1:{server.Port}";
        var result = await HTTPUtil.GetWebLocationCheckedAsync($"{baseUrl}/a",
            uri => uri.Host == "127.0.0.1", maxHops: 5, token: CancellationToken.None);

        // 到达跳数上限后返回（不悬挂），结果落在环中的某一跳
        Assert.Contains(result, new[] { $"{baseUrl}/a", $"{baseUrl}/b" });
    }

    [Fact]
    public async Task GetWebLocationCheckedAsync_HeadRejected_FallsBackToGet()
    {
        // 回归修复：此前逐跳解析只发 HEAD，遇到不支持 HEAD 的服务器（405）直接放弃，
        // 导致 av 视频链接解析失败。必须回退到 GET 请求同一 URL。
        using var server = new LocalHeadRejectingServer(200);
        var baseUrl = $"http://127.0.0.1:{server.Port}";
        var result = await HTTPUtil.GetWebLocationCheckedAsync($"{baseUrl}/ok",
            uri => uri.Host == "127.0.0.1", token: CancellationToken.None);

        // GET 返回 200：成功解析，返回原 URL（未被重定向）
        Assert.Equal($"{baseUrl}/ok", result);
    }

    /// <summary>对 HEAD 一律返回 405、GET 返回指定状态码的本地服务（模拟不支持 HEAD 的服务器）。</summary>
    private sealed class LocalHeadRejectingServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly int _getStatus;
        private readonly Task _loop;
        public int Port { get; }

        public LocalHeadRejectingServer(int getStatus)
        {
            _getStatus = getStatus;
            Port = 25000 + (Environment.ProcessId % 500);
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
                            if (ctx.Request.HttpMethod == "HEAD")
                            {
                                ctx.Response.StatusCode = 405; // Method Not Allowed
                            }
                            else
                            {
                                ctx.Response.StatusCode = _getStatus;
                            }
                            ctx.Response.Close();
                        }
                        catch { }
                    }
                }
                catch (HttpListenerException) { }
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

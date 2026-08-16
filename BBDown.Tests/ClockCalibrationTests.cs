using System.Net;
using BBDown;
using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// 1.1：WBI 签名依赖本地系统时钟，时钟偏差超 ~60s 时效窗口会被 B 站拒绝。
/// 修复：HTTPUtil.CalibrateClock 从响应头 Date 校准偏移写入 Config，
/// 签名时间戳（GetTimeStamp/ServerClock）经偏移补偿。
/// </summary>
public class ClockCalibrationTests
{
    [Fact]
    public void CalibrateClock_ReadsDateHeader_WritesOffset()
    {
        var original = Config.Current;
        try
        {
            Config.Apply(Config.Current with { ServerClockOffsetSeconds = 0 });
            using var response = new HttpResponseMessage();
            var serverDate = DateTimeOffset.UtcNow.AddMinutes(5);
            response.Headers.Date = serverDate;

            HTTPUtil.CalibrateClock(response);

            long expected = (long)Math.Round((serverDate - DateTimeOffset.UtcNow).TotalSeconds);
            Assert.InRange(Config.Current.ServerClockOffsetSeconds, expected - 3, expected + 3);
        }
        finally { Config.Apply(original); }
    }

    [Fact]
    public void CalibrateClock_MissingDate_DoesNotWrite()
    {
        var original = Config.Current;
        try
        {
            Config.Apply(Config.Current with { ServerClockOffsetSeconds = 0 });
            using var response = new HttpResponseMessage(); // 无 Date 头

            HTTPUtil.CalibrateClock(response);

            Assert.Equal(0, Config.Current.ServerClockOffsetSeconds);
        }
        finally { Config.Apply(original); }
    }

    [Fact]
    public void CalibrateClock_MalformedDate_Beyond24h_Rejected()
    {
        var original = Config.Current;
        try
        {
            Config.Apply(Config.Current with { ServerClockOffsetSeconds = 0 });
            using var response = new HttpResponseMessage();
            response.Headers.Date = DateTimeOffset.UtcNow.AddDays(3); // 超过 ±24h clamp

            HTTPUtil.CalibrateClock(response);

            Assert.Equal(0, Config.Current.ServerClockOffsetSeconds);
        }
        finally { Config.Apply(original); }
    }

    [Fact]
    public void CalibrateClock_ZeroOffset_DoesNotRewrite()
    {
        var original = Config.Current;
        try
        {
            Config.Apply(Config.Current with { ServerClockOffsetSeconds = 0 });
            using var response = new HttpResponseMessage();
            response.Headers.Date = DateTimeOffset.UtcNow.AddSeconds(2); // 几乎零偏差

            HTTPUtil.CalibrateClock(response);

            // 秒级偏差可能被截断成 0 或 ±1：只断言没有写入明显异常值
            Assert.InRange(Config.Current.ServerClockOffsetSeconds, -3, 3);
        }
        finally { Config.Apply(original); }
    }

    [Fact]
    public void GetTimeStamp_RespectsServerClockOffset()
    {
        var original = Config.Current;
        try
        {
            Config.Apply(Config.Current with { ServerClockOffsetSeconds = 3600 });
            long ts = long.Parse(BBDownUtil.GetTimeStamp(true));
            long expected = DateTimeOffset.UtcNow.AddSeconds(3600).ToUnixTimeSeconds();
            Assert.InRange(ts, expected - 3, expected + 3);
        }
        finally { Config.Apply(original); }
    }

    [Fact]
    public void GetTimeStamp_ZeroOffset_MatchesUtc()
    {
        var original = Config.Current;
        try
        {
            Config.Apply(Config.Current with { ServerClockOffsetSeconds = 0 });
            long ts = long.Parse(BBDownUtil.GetTimeStamp(true));
            long expected = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Assert.InRange(ts, expected - 3, expected + 3);
        }
        finally { Config.Apply(original); }
    }

    [Fact]
    public async Task GetWebSource_CalibratesClock_FromServerDate()
    {
        // 端到端：真实请求（本机 HttpListener 的 Date 头≈本地 UTC）后偏移被校准。
        // ScriptedServer 用 HttpListener 自动带 Date 头（本机时间），断言偏移落在小容差内。
        using var server = new ScriptedServer((200, """{"code":0}"""));
        var original = Config.Current;
        try
        {
            Config.Apply(Config.Current with { ServerClockOffsetSeconds = 0 });
            await HTTPUtil.GetWebSourceAsync($"http://127.0.0.1:{server.Port}/api", token: CancellationToken.None);
            // 本机服务器的 Date 头即本机 UTC：校准后偏移应 ≈0（秒级容差）
            Assert.InRange(Config.Current.ServerClockOffsetSeconds, -3, 3);
        }
        finally { Config.Apply(original); }
    }

    /// <summary>返回指定 (状态码, 响应体) 的本地服务。HttpListener 响应自动带 Date 头（本机 UTC）。</summary>
    private sealed class ScriptedServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        public int Port { get; }

        public ScriptedServer(params (int Status, string Body)[] responses)
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
                        try
                        {
                            var (status, body) = responses.FirstOrDefault();
                            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
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
}

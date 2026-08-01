using BBDown.Core;

namespace BBDown.Tests;

public class ConfigIsolationTests
{
    [Fact]
    public async Task Apply_ConcurrentAsyncFlows_AreIsolated()
    {
        // serve 模式下每个 /add-task 的下载流各自 SetUpWork 会 Apply 自己的配置。
        // 若 Config 只有全局快照，两个并发流会互相读到对方刚写入的 Cookie；
        // AsyncLocal 方案要求每个流读到自己的值。
        var original = Config.Current.Cookie;
        try
        {
            var t1 = Task.Run(async () =>
            {
                Config.Apply(Config.Current with { Cookie = "cookie-A" });
                await Task.Delay(50);
                return Config.Current.Cookie;
            });
            var t2 = Task.Run(async () =>
            {
                Config.Apply(Config.Current with { Cookie = "cookie-B" });
                await Task.Delay(50);
                return Config.Current.Cookie;
            });
            var results = await Task.WhenAll(t1, t2);
            Assert.Equal("cookie-A", results[0]);
            Assert.Equal("cookie-B", results[1]);
        }
        finally
        {
            Config.Apply(Config.Current with { Cookie = original });
        }
    }

    [Fact]
    public void Apply_AndReadCurrent_PropagatesWithinFlow()
    {
        var original = Config.Current.Cookie;
        try
        {
            Config.Apply(Config.Current with { Cookie = "flow-cookie" });
            Assert.Equal("flow-cookie", Config.Current.Cookie);
        }
        finally
        {
            Config.Apply(Config.Current with { Cookie = original });
        }
    }
}

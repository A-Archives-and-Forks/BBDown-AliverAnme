namespace BBDown.Tests;

public class ServeApiSecurityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSafeCallbackUrl_Empty_Allowed(string? url)
        => Assert.True(BBDownApiServer.IsSafeCallbackUrl(url));

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("http://192.168.1.10:9000/cb")]   // RFC1918 私网：局域网回调是 serve 的正常用法
    [InlineData("https://10.0.0.5/cb")]
    [InlineData("https://api.bilibili.com/x/")]
    public void IsSafeCallbackUrl_PublicOrPrivateNet_Allowed(string url)
        => Assert.True(BBDownApiServer.IsSafeCallbackUrl(url));

    [Theory]
    [InlineData("ftp://example.com/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    public void IsSafeCallbackUrl_NonHttpOrRelative_Rejected(string url)
        => Assert.False(BBDownApiServer.IsSafeCallbackUrl(url));

    [Theory]
    [InlineData("http://localhost:5000/cb")]
    [InlineData("http://127.0.0.1/cb")]
    [InlineData("http://[::1]/cb")]
    [InlineData("http://169.254.169.254/cb")]   // 云元数据探测面
    [InlineData("http://[fe80::1]/cb")]         // IPv6 链路本地
    public void IsSafeCallbackUrl_LoopbackOrLinkLocal_Rejected(string url)
        => Assert.False(BBDownApiServer.IsSafeCallbackUrl(url));

    [Fact]
    public void SanitizeUntrustedOptions_ClearsExecutionFields()
    {
        var req = new ServeRequestOptions
        {
            Aria2cArgs = "--on-download-complete=\"rm -rf ~\"",
            Aria2cPath = "/tmp/evil",
            Aria2cProxy = "http://evil:8080",
        };
        BBDownApiServer.SanitizeUntrustedOptions(req);
        Assert.Equal("", req.Aria2cArgs);
        Assert.Equal("", req.Aria2cPath);
        Assert.Equal("", req.Aria2cProxy);
    }

    [Fact]
    public void Constructor_NonPositiveMaxConcurrent_DoesNotThrow()
    {
        _ = new BBDownApiServer(0);
    }
}

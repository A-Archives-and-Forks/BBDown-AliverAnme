using System.Net;
using System.Net.Sockets;

namespace BBDown.Tests;

public class ServeApiSecurityTests
{
    // 域名用例需要解析 DNS：注入固定解析器保证测试确定性、不依赖网络
    private static Task<IPAddress[]> ResolvePublic(string _) => Task.FromResult(new IPAddress[] { IPAddress.Parse("93.184.216.34") });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsSafeCallbackUrl_Empty_Allowed(string? url)
        => Assert.True(await BBDownApiServer.IsSafeCallbackUrlAsync(url));

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("http://192.168.1.10:9000/cb")]   // RFC1918 私网：局域网回调是 serve 的正常用法
    [InlineData("https://10.0.0.5/cb")]
    [InlineData("https://api.bilibili.com/x/")]
    public async Task IsSafeCallbackUrl_PublicOrPrivateNet_Allowed(string url)
        => Assert.True(await BBDownApiServer.IsSafeCallbackUrlAsync(url, ResolvePublic));

    [Theory]
    [InlineData("ftp://example.com/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    public async Task IsSafeCallbackUrl_NonHttpOrRelative_Rejected(string url)
        => Assert.False(await BBDownApiServer.IsSafeCallbackUrlAsync(url));

    [Theory]
    [InlineData("http://localhost:5000/cb")]
    [InlineData("http://127.0.0.1/cb")]
    [InlineData("http://[::1]/cb")]
    [InlineData("http://169.254.169.254/cb")]   // 云元数据探测面
    [InlineData("http://[fe80::1]/cb")]         // IPv6 链路本地
    [InlineData("http://[::ffff:169.254.169.254]/cb")] // IPv4-mapped IPv6：映射前是 InterNetworkV6，会绕过下方 169.254 检查
    [InlineData("http://[::ffff:127.0.0.1]/cb")]        // IPv4-mapped IPv6 回环
    [InlineData("http://0.0.0.0/cb")]           // 全零地址连接时绑定回环
    [InlineData("http://[::]/cb")]              // IPv6 全零
    public async Task IsSafeCallbackUrl_LoopbackOrLinkLocal_Rejected(string url)
        => Assert.False(await BBDownApiServer.IsSafeCallbackUrlAsync(url));

    [Fact]
    public async Task IsSafeCallbackUrl_DnsRebindingDomain_Rejected()
    {
        // 攻击者注册一个解析到云元数据地址的域名：字符串比对会放行，
        // 必须解析 DNS 后按地址拒绝
        Task<IPAddress[]> ResolveToMetadata(string _) => Task.FromResult(new IPAddress[] { IPAddress.Parse("169.254.169.254") });
        Assert.False(await BBDownApiServer.IsSafeCallbackUrlAsync("http://metadata.internal/cb", ResolveToMetadata));

        Task<IPAddress[]> ResolveToLoopback(string _) => Task.FromResult(new IPAddress[] { IPAddress.Parse("127.0.0.1") });
        Assert.False(await BBDownApiServer.IsSafeCallbackUrlAsync("http://rebind.test/cb", ResolveToLoopback));

        // 域名解析到 RFC1918 内网也应拒绝：内网地址只允许"字面 IP"显式配置，
        // 域名重绑定打内网是 SSRF 横向面（攻击者注册域名解析到 10.0.0.x）
        Task<IPAddress[]> ResolveToPrivate(string _) => Task.FromResult(new IPAddress[] { IPAddress.Parse("10.0.0.5") });
        Assert.False(await BBDownApiServer.IsSafeCallbackUrlAsync("http://rebind-to-private.test/cb", ResolveToPrivate));

        // 字面 IP 的 RFC1918 仍放行（局域网回调是 serve 的正常用法）
        Assert.True(await BBDownApiServer.IsSafeCallbackUrlAsync("http://10.0.0.5:9000/cb"));
    }

    [Fact]
    public async Task IsSafeCallbackUrl_DnsFailure_Rejected()
    {
        // 域名无法解析：回调必然失败，按不安全处理
        Task<IPAddress[]> Throws(string _) => throw new SocketException((int)SocketError.HostNotFound);
        Assert.False(await BBDownApiServer.IsSafeCallbackUrlAsync("http://unresolvable.test/cb", Throws));
    }

    [Fact]
    public void SanitizeUntrustedOptions_ClearsExecutionFields()
    {
        var req = new ServeRequestOptions
        {
            Aria2cArgs = "--on-download-complete=\"rm -rf ~\"",
            Aria2cPath = "/tmp/evil",
            Aria2cProxy = "http://evil:8080",
            // 同属"让服务器执行指定程序/改动进程环境"的字段：混流二进制路径、DRM 路径、工作目录
            FFmpegPath = "/tmp/evil-ffmpeg",
            Mp4boxPath = "/tmp/evil-mp4box",
            WvdPath = "/tmp/evil.wvd",
            Mp4decryptPath = "/tmp/evil-mp4decrypt",
            WorkDir = "/tmp/evil-dir",
            // 自定义 UA 是进程级静态字段：一个任务设置后污染所有后续任务
            UserAgent = "EvilUA/1.0",
            // NotifyWebhook 会绕过 CallBackWebHook 的 SSRF 校验向任意地址 POST
            NotifyWebhook = "http://evil.example/hook",
            // CallBackWebHook：任务回调改为服务端 allowlist（--notify-webhook），
            // 客户端请求体中的回调字段必须被清零，防止任意客户端驱动本机 POST
            CallBackWebHook = "http://evil.example/client-callback",
            // Insecure 会全局关闭 TLS 校验：serve 下必须忽略，否则任意客户端可让携带
            // 操作者 SESSDATA 的请求跳过证书校验被 MITM 截获
            Insecure = true,
            // FilePattern/MultiFilePattern 会被当作保存路径模板，字面量中的 ".." 段原样保留
            // （路径穿越面），serve 下必须回落默认模板
            FilePattern = "../../../evil/out.mp4",
            MultiFilePattern = "/tmp/evil/multi.mp4",
            // DrmKeyHex/DrmKidHex 会经 mp4decrypt 参与解密，是客户端可控的密钥注入点
            DrmKeyHex = "4141414141414141414141414141414141414141414141414141414141414141",
            DrmKidHex = "42424242424242424242424242424242",
        };
        BBDownApiServer.SanitizeUntrustedOptions(req);
        Assert.Equal("", req.Aria2cArgs);
        Assert.Equal("", req.Aria2cPath);
        Assert.Equal("", req.Aria2cProxy);
        Assert.Equal("", req.FFmpegPath);
        Assert.Equal("", req.Mp4boxPath);
        Assert.Equal("", req.WvdPath);
        Assert.Equal("", req.Mp4decryptPath);
        Assert.Equal("", req.WorkDir);
        Assert.Equal("", req.UserAgent);
        Assert.Equal("", req.NotifyWebhook);
        Assert.Equal("", req.CallBackWebHook);
        Assert.False(req.Insecure);
        Assert.Equal("", req.FilePattern);
        Assert.Equal("", req.MultiFilePattern);
        Assert.Equal("", req.DrmKeyHex);
        Assert.Equal("", req.DrmKidHex);
    }

    [Fact]
    public void SanitizeUntrustedOptions_EmptyHost_FallsBackToOfficial()
    {
        // host 为空/null 时也必须回落官方默认，否则番剧/TV/intl URL 拼成 https:///... 抛 UriFormatException
        var req = new ServeRequestOptions { Host = "", EpHost = "", TvHost = "   ", UposHost = "" };
        BBDownApiServer.SanitizeUntrustedOptions(req);
        Assert.Equal("api.bilibili.com", req.Host);
        Assert.Equal("api.bilibili.com", req.EpHost);
        Assert.Equal("api.snm0516.aisee.tv", req.TvHost);
        Assert.Equal("", req.UposHost);
    }

    [Fact]
    public void IsOfficialHost_SlashConfusion_Rejected()
    {
        // 斜杠协议混淆：纯后缀匹配会放行 "evil.com/.bilibili.com"，规范化后必须拒绝
        Assert.False(BBDownApiServer.IsOfficialHost("evil.com/.bilibili.com"));
        Assert.False(BBDownApiServer.IsOfficialHost("https://evil.com/.bilibili.com"));
        Assert.False(BBDownApiServer.IsOfficialHost("evil.com@bilibili.com"));
        Assert.False(BBDownApiServer.IsOfficialHost("bilibili.com.evil.com"));
        Assert.False(BBDownApiServer.IsOfficialHost("https://bilibili.com.evil.com"));
        // 官方域名（含子域）仍应放行
        Assert.True(BBDownApiServer.IsOfficialHost("api.bilibili.com"));
        Assert.True(BBDownApiServer.IsOfficialHost("https://api.bilibili.com"));
        Assert.True(BBDownApiServer.IsOfficialHost("upos-sz-mirrorcoso1.bilivideo.com"));
        Assert.True(BBDownApiServer.IsOfficialHost("https://grpc.biliapi.net"));
        // 空值视为合法（回落默认）
        Assert.True(BBDownApiServer.IsOfficialHost(null));
        Assert.True(BBDownApiServer.IsOfficialHost(""));
    }

    [Fact]
    public void SanitizeUntrustedOptions_HostWhitelist_FallsBackToOfficial()
    {
        // host 字段决定凭据发送目标：指向攻击者域名的 host 必须被回落为官方默认，
        // 否则操作者的 B 站 Cookie 会被发往攻击者服务器（SSRF + 凭据外泄）
        var req = new ServeRequestOptions
        {
            Host = "https://evil.example",
            EpHost = "evil.example",
            TvHost = "http://attacker.com:8080",
            UposHost = "https://user:pass@bilibili.com", // userinfo 伪装信任域也要拒绝
        };
        BBDownApiServer.SanitizeUntrustedOptions(req);
        Assert.Equal("api.bilibili.com", req.Host);
        Assert.Equal("api.bilibili.com", req.EpHost);
        Assert.Equal("api.snm0516.aisee.tv", req.TvHost);
        Assert.Equal("", req.UposHost);

        // 官方域名（含子域）应保留
        var ok = new ServeRequestOptions
        {
            Host = "https://api.bilibili.com",
            EpHost = "https://grpc.biliapi.net",
            TvHost = "https://api.snm0516.aisee.tv",
            UposHost = "upos-sz-mirrorcoso1.bilivideo.com",
        };
        BBDownApiServer.SanitizeUntrustedOptions(ok);
        Assert.Equal("api.bilibili.com", ok.Host);
        Assert.Equal("grpc.biliapi.net", ok.EpHost);
        Assert.Equal("api.snm0516.aisee.tv", ok.TvHost);
        Assert.Equal("upos-sz-mirrorcoso1.bilivideo.com", ok.UposHost);
    }

    [Fact]
    public void Constructor_NonPositiveMaxConcurrent_DoesNotThrow()
    {
        _ = new BBDownApiServer(0);
    }
}

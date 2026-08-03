using System.Net.Http.Headers;
using BBDown.Core.Util;

namespace BBDown.Tests;

public class SensitiveDataMaskerTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("short", "***")]
    [InlineData("12345678", "***")]
    [InlineData("123456789", "1234***6789")]
    [InlineData("abcdefghijklmnop", "abcd***mnop")]
    public void MaskValue_HidesMiddleAndFullyHidesShortValues(string input, string expected)
    {
        Assert.Equal(expected, SensitiveDataMasker.MaskValue(input));
    }

    [Fact]
    public void MaskValue_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SensitiveDataMasker.MaskValue(null));
    }

    [Fact]
    public void MaskUrl_MasksAccessKeyButKeepsOtherParams()
    {
        var masked = SensitiveDataMasker.MaskUrl(
            "https://api.bilibili.com/x/player?access_key=abcdefghijklmnop&cid=123&qn=80");

        Assert.Equal(
            "https://api.bilibili.com/x/player?access_key=abcd***mnop&cid=123&qn=80", masked);
    }

    [Fact]
    public void MaskUrl_MasksEveryKnownSensitiveKey()
    {
        var masked = SensitiveDataMasker.MaskUrl(
            "https://x.com/a?access_token=aaaaaaaaaaaa&refresh_token=bbbbbbbbbbbb&token=cccccccccccc");

        Assert.DoesNotContain("aaaaaaaaaaaa", masked);
        Assert.DoesNotContain("bbbbbbbbbbbb", masked);
        Assert.DoesNotContain("cccccccccccc", masked);
    }

    [Fact]
    public void MaskUrl_NoQueryString_ReturnsUnchanged()
    {
        const string url = "https://www.bilibili.com/video/BV1qt4y1X7TW";
        Assert.Equal(url, SensitiveDataMasker.MaskUrl(url));
    }

    [Fact]
    public void MaskUrl_PreservesFragment()
    {
        var masked = SensitiveDataMasker.MaskUrl("https://x.com/a?access_key=abcdefghijkl#section");

        Assert.EndsWith("#section", masked);
        Assert.DoesNotContain("abcdefghijkl", masked);
    }

    [Fact]
    public void MaskCookie_MasksSessdataButKeepsHarmlessEntries()
    {
        var masked = SensitiveDataMasker.MaskCookie("SESSDATA=abcdefghijklmnop; buvid3=xyz; bili_jct=1234567890ab");

        Assert.Contains("SESSDATA=abcd***mnop", masked);
        Assert.Contains("buvid3=xyz", masked);
        Assert.DoesNotContain("abcdefghijklmnop", masked);
        Assert.DoesNotContain("1234567890ab", masked);
    }

    [Fact]
    public void MaskCookie_ValueContainingEquals_IsFullyMasked()
    {
        // SESSDATA 的真实值里含有 %2C 之外的 '=' 时，不能因为二次分割而漏出尾部
        var masked = SensitiveDataMasker.MaskCookie("SESSDATA=abcd=efgh=ijklmnop");

        Assert.DoesNotContain("ijklmnop", masked);
    }

    [Fact]
    public void MaskHeaders_MasksCookieHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        request.Headers.TryAddWithoutValidation("Cookie", "SESSDATA=abcdefghijklmnop");
        request.Headers.TryAddWithoutValidation("User-Agent", "BBDown/1.0");

        var rendered = SensitiveDataMasker.MaskHeaders(request.Headers);

        Assert.DoesNotContain("abcdefghijklmnop", rendered);
        Assert.Contains("BBDown/1.0", rendered);
    }

    [Fact]
    public void MaskHeaders_MasksOpaqueAuthorizationHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        request.Headers.TryAddWithoutValidation("Authorization", "identify_v1 5227abcdefgh1");

        var rendered = SensitiveDataMasker.MaskHeaders(request.Headers);

        Assert.DoesNotContain("5227abcdefgh1", rendered);
    }

    [Fact]
    public void MaskHeaders_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SensitiveDataMasker.MaskHeaders(null));
    }

    [Fact]
    public void MaskHeaderMap_MasksMetadataBinHeader()
    {
        // x-bili-metadata-bin 的 protobuf 里携带 access_token（可逆 base64）：
        // 不脱敏会把凭据以可解码形式写进 debug 日志
        var headers = new Dictionary<string, string>
        {
            ["x-bili-metadata-bin"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("secret-access-token-here")),
            ["authorization"] = "identify_v1 secret-app-token-1234",
            ["user-agent"] = "Dalvik/2.1.0",
        };

        var masked = SensitiveDataMasker.MaskHeaderMap(headers);

        Assert.DoesNotContain("secret-access-token-here", masked["x-bili-metadata-bin"]);
        Assert.DoesNotContain("secret-app-token-1234", masked["authorization"]);
        Assert.Equal("Dalvik/2.1.0", masked["user-agent"]);
    }

    [Fact]
    public void MaskHeaders_KeepsAuthorizationScheme()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        request.Headers.TryAddWithoutValidation("Authorization", "identify_v1 SECRETAPPTOKEN9876");

        var rendered = SensitiveDataMasker.MaskHeaders(request.Headers);

        // 方案名不是秘密，保留它才能看出用的是哪种鉴权
        Assert.Contains("identify_v1 ", rendered);
        Assert.DoesNotContain("SECRETAPPTOKEN9876", rendered);
    }

    [Fact]
    public void MaskHeaderMap_MasksGrpcAuthorizationHeader()
    {
        // gRPC 侧的 header 是手工组装的字典，不经过 HttpHeaders
        var headers = new Dictionary<string, string>
        {
            ["Host"] = "grpc.biliapi.net",
            ["authorization"] = "identify_v1 SECRETAPPTOKEN9876",
            ["grpc-encoding"] = "gzip",
        };

        var masked = SensitiveDataMasker.MaskHeaderMap(headers);

        Assert.Equal("identify_v1 SECR***9876", masked["authorization"]);
        Assert.Equal("grpc.biliapi.net", masked["Host"]);
        Assert.Equal("gzip", masked["grpc-encoding"]);
    }

    [Fact]
    public void MaskHeaderMap_IsCaseInsensitiveOnHeaderName()
    {
        // AppHelper 用的是小写 "authorization"
        var masked = SensitiveDataMasker.MaskHeaderMap(
            new Dictionary<string, string> { ["AUTHORIZATION"] = "Bearer abcdefghijklmnop" });

        Assert.DoesNotContain("abcdefghijklmnop", masked["AUTHORIZATION"]);
        Assert.StartsWith("Bearer ", masked["AUTHORIZATION"]);
    }

    [Fact]
    public void MaskHeaderMap_Null_ReturnsEmpty()
    {
        Assert.Empty(SensitiveDataMasker.MaskHeaderMap(null));
    }
}

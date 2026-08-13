namespace BBDown.Tests;

/// <summary>
/// 扫码登录凭证合并逻辑测试：B 站新版登录（2026）把 SESSDATA 等凭证经 poll 响应头
/// Set-Cookie（HttpOnly）下发，data.url 只剩 crossDomain 跳转参数。MergeLoginCookies
/// 需合并 url query（旧协议）与 Set-Cookie（新协议），保证两类来源都不丢失。
/// </summary>
public class BBDownLoginUtilMergeTests
{
    [Fact]
    public void Merge_NewProtocol_SetCookieProvidesCredentials()
    {
        // 新版：url query 只有跳转参数，SESSDATA/bili_jct 等全在 Set-Cookie
        var urlQuery = "ticket=t1&gourl=https%3A%2F%2Fwww.bilibili.com&first_domain=.bilibili.com";
        var setCookies = new[]
        {
            "SESSDATA=sess%2Cvalue; Path=/; Domain=bilibili.com; Expires=Wed, 09 Feb 2027 00:00:00 GMT; HttpOnly; Secure; SameSite=None",
            "bili_jct=bj123; Path=/; Domain=.bilibili.com; HttpOnly",
            "DedeUserID=345960772; Path=/; Domain=.bilibili.com",
        };

        var merged = BBDown.BBDownLoginUtil.MergeLoginCookies(urlQuery, setCookies);

        Assert.Contains("SESSDATA=sess%2Cvalue", merged);
        Assert.Contains("bili_jct=bj123", merged);
        Assert.Contains("DedeUserID=345960772", merged);
        Assert.Contains("ticket=t1", merged);
        // 不得包含 cookie 属性（Path/Domain/Expires/HttpOnly 等）
        Assert.DoesNotContain("Path=", merged);
        Assert.DoesNotContain("Domain=", merged);
        Assert.DoesNotContain("Expires=", merged);
        Assert.DoesNotContain("HttpOnly", merged);
        Assert.DoesNotContain("SameSite", merged);
        // 以 ; 连接
        Assert.Equal(6, merged.Split(';').Length);
    }

    [Fact]
    public void Merge_OldProtocol_UrlQueryProvidesCredentials()
    {
        // 旧版：凭证直接在 url query 中，无 Set-Cookie
        var urlQuery = "SESSDATA=old%2Csess&bili_jct=oldbj&DedeUserID=1";
        var merged = BBDown.BBDownLoginUtil.MergeLoginCookies(urlQuery, []);

        Assert.Equal("SESSDATA=old%2Csess;bili_jct=oldbj;DedeUserID=1", merged);
    }

    [Fact]
    public void Merge_SetCookieOverridesSameNameFromUrlQuery()
    {
        // 同名冲突时 Set-Cookie 优先（新协议凭证更权威）
        var merged = BBDown.BBDownLoginUtil.MergeLoginCookies("SESSDATA=from_url", new[] { "SESSDATA=from_cookie; Path=/; HttpOnly" });

        Assert.Equal("SESSDATA=from_cookie", merged);
    }

    [Fact]
    public void Merge_CommaInValueIsEscaped()
    {
        var merged = BBDown.BBDownLoginUtil.MergeLoginCookies("a=1,2;b=3", []);

        Assert.Equal("a=1%2C2;b=3", merged);
    }

    [Fact]
    public void Merge_EmptyEverything_ReturnsEmpty()
    {
        Assert.Equal("", BBDown.BBDownLoginUtil.MergeLoginCookies("", []));
    }

    [Fact]
    public void Merge_NoQueryNoCookies_ReturnsEmpty()
    {
        // 等价于登录成功但 url 无 query 且无 Set-Cookie 的异常场景
        Assert.Equal("", BBDown.BBDownLoginUtil.MergeLoginCookies("https://www.bilibili.com", []));
    }

    [Theory]
    [InlineData("SESSDATA=abc;bili_jct=def", "SESSDATA", "abc")]
    [InlineData("bili_jct=def;SESSDATA=abc", "SESSDATA", "abc")]
    [InlineData("ticket=t1;SESSDATA=", "SESSDATA", "")]
    [InlineData("ticket=t1", "SESSDATA", "")]
    [InlineData("sessdata=lower;SESSDATA=upper", "SESSDATA", "lower")]
    public void GetCookieValue_ExtractsByNameCaseInsensitive(string cookieString, string name, string expected)
    {
        Assert.Equal(expected, BBDown.BBDownLoginUtil.GetCookieValue(cookieString, name));
    }

    [Fact]
    public void Merge_SkipsSetCookieWithoutEqualsSign()
    {
        // 属性型 Set-Cookie 条目（无 name=value）应被跳过，不产生空字段
        var merged = BBDown.BBDownLoginUtil.MergeLoginCookies("ticket=t1", new[] { "Path=/", "HttpOnly", "SESSDATA=real; Path=/" });

        Assert.Equal("ticket=t1;SESSDATA=real", merged);
    }

    [Fact]
    public void Merge_EmptySessdataValue_IsDetectableAsMissing()
    {
        // 空值 SESSDATA（Set-Cookie 下发空凭证）应能被 GetCookieValue 识别为空，
        // 支撑 LoginWEB 的防御性检查：拒绝写入而非落盘无效 cookie。
        var merged = BBDown.BBDownLoginUtil.MergeLoginCookies("ticket=t1", new[] { "SESSDATA=; Path=/; HttpOnly" });

        Assert.Equal("ticket=t1;SESSDATA=", merged);
        Assert.Equal("", BBDown.BBDownLoginUtil.GetCookieValue(merged, "SESSDATA"));
    }
}

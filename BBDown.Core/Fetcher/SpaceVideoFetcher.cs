using BBDown.Core.Entity;
using BBDown.Core.Util;
using System.Text.Json;

namespace BBDown.Core.Fetcher;

public class SpaceVideoFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id)
    {
        id = id[4..];
        // 该接口按 B 站惯例需要设备标识，未登录时 Cookie 为空
        await BuvidProvider.EnsureAsync();
        // using the live API can bypass w_rid
        string userInfoApi = $"https://api.live.bilibili.com/live_user/v1/Master/info?uid={id}";
        using var userDoc = JsonDocument.Parse(await HTTPUtil.GetWebSourceAsync(userInfoApi));
        string userName = PathUtil.GetValidFileName(userDoc.RootElement.GetPropertySafe("data").GetPropertySafe("info").GetValueAsStringSafe("uname"), filterSlash: true);
        List<string> urls = new();
        int pageSize = 50;
        int pageNumber = 1;
        var api = Parser.WbiSign($"mid={id}&order=pubdate&pn={pageNumber}&ps={pageSize}&tid=0&wts={DateTimeOffset.Now.ToUnixTimeSeconds().ToString()}");
        api = $"https://api.bilibili.com/x/space/wbi/arc/search?{api}";
        string json = await FetchSpaceListAsync(api);
        using var infoJson = JsonDocument.Parse(json);
        var pages = infoJson.RootElement.GetPropertySafe("data").GetPropertySafe("list").EnumerateArraySafe("vlist");
        foreach (var page in pages)
        {
            urls.Add($"https://www.bilibili.com/video/av{page.GetValueAsStringSafe("aid")}");
        }
        int totalCount = infoJson.RootElement.GetPropertySafe("data").GetPropertySafe("page").GetInt32Safe("count");
        int totalPage = (int)Math.Ceiling((double)totalCount / pageSize);
        while (pageNumber < totalPage)
        {
            pageNumber++;
            urls.AddRange(await GetVideosByPageAsync(pageNumber, pageSize, id));
        }
        var listFile = $"{userName}的投稿视频.txt";
        await File.WriteAllTextAsync(listFile, string.Join(Environment.NewLine, urls));
        Logger.Log($"已导出 {urls.Count} 个投稿视频地址到 {listFile}");
        Logger.LogWarn("下载器尚不支持直接下载 UP 主的全部投稿，请借助脚本按行调用本程序：");
        Console.WriteLine();
        Console.WriteLine($"  Windows:  for /f %a in ({listFile}) do BBDown.exe \"%a\"");
        Console.WriteLine($"  Linux/macOS:  while read -r u; do BBDown \"$u\"; done < \"{listFile}\"");
        Console.WriteLine();
        throw new NotSupportedException($"暂不支持直接下载 UP 主的全部投稿，视频地址已导出至 {listFile}");
    }

    /// <summary>
    /// 拉取投稿列表。该接口受 B 站风控保护，且风控有两种表现形式：
    /// 直接返回 HTTP 412，或返回 200 但 body 为 <c>{"code":-352,"data":{"v_voucher":...}}</c>。
    /// 两者的原始报错（"412 Precondition Failed" 加长串堆栈 / "JSON property not found: 'list'"）
    /// 对用户都没有可操作信息，这里统一转成明确提示。
    /// </summary>
    private static async Task<string> FetchSpaceListAsync(string api)
    {
        string json;
        try
        {
            json = await HTTPUtil.GetWebSourceAsync(api);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            throw new InvalidOperationException(RiskControlMessage("HTTP 412"), ex);
        }

        using var doc = JsonDocument.Parse(json);
        var code = doc.RootElement.GetPropertySafe("code").GetInt32();
        if (code != 0)
        {
            var message = doc.RootElement.GetValueAsStringSafe("message");
            // -352 为风控校验失败，其余错误码按原样透出便于排查
            throw new InvalidOperationException(code == -352
                ? RiskControlMessage("code=-352")
                : $"获取 UP 主投稿列表失败(code={code}): {message}");
        }

        return json;
    }

    private static string RiskControlMessage(string detail) =>
        $"获取 UP 主投稿列表被 B 站风控拦截（{detail}）。该接口对未登录请求限制较严，" +
        "可先运行 BBDown login 扫码登录后重试，或稍后再试。";

    static async Task<List<string>> GetVideosByPageAsync(int pageNumber, int pageSize, string mid)
    {
        List<string> urls = new();
        var api = Parser.WbiSign($"mid={mid}&order=pubdate&pn={pageNumber}&ps={pageSize}&tid=0&wts={DateTimeOffset.Now.ToUnixTimeSeconds().ToString()}");
        api = $"https://api.bilibili.com/x/space/wbi/arc/search?{api}";
        string json = await FetchSpaceListAsync(api);
        using var infoJson = JsonDocument.Parse(json);
        var pages = infoJson.RootElement.GetPropertySafe("data").GetPropertySafe("list").EnumerateArraySafe("vlist");
        foreach (var page in pages)
        {
            urls.Add($"https://www.bilibili.com/video/av{page.GetValueAsStringSafe("aid")}");
        }
        return urls;
    }
}
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using BBDown;
using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class WatchLaterSettings : CommandSettings
{
    [CommandOption("--limit")]
    [Description("最多下载前 N 个稍后再看视频(默认 0=全部)")]
    public int Limit { get; set; }

    [CommandOption("-c|--cookie")]
    [Description("Cookie 字符串")]
    public string Cookie { get; set; } = "";

    [CommandOption("--access-token")]
    [Description("access token")]
    public string AccessToken { get; set; } = "";

    [CommandOption("-e|--encoding-priority")]
    [Description("视频编码优先级, 如 hevc,avc,av1")]
    public string? EncodingPriority { get; set; }

    [CommandOption("-q|--dfn-priority")]
    [Description("视频清晰度优先级, 如 8K 4K 1080P 高清 720P 高清")]
    public string? DfnPriority { get; set; }

    [CommandOption("-a|--use-app-api")]
    [Description("使用APP端解析模式")]
    public bool UseAppApi { get; set; }

    [CommandOption("-t|--use-tv-api")]
    [Description("使用TV端解析模式")]
    public bool UseTvApi { get; set; }

    [CommandOption("--use-intl-api")]
    [Description("使用国际版解析模式")]
    public bool UseIntlApi { get; set; }

    [CommandOption("-w|--work-dir")]
    [Description("设置工作目录(所有相对路径的根目录)")]
    public string WorkDir { get; set; } = "";
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class WatchLaterCommand : Command<WatchLaterSettings>
{
    protected override int Execute(CommandContext context, WatchLaterSettings settings, CancellationToken cancellationToken)
    {
        // Task.Run avoids deadlock if called from a thread with a SynchronizationContext
        return Task.Run(async () =>
        {
            try
            {
                // 稍后再看接口需要登录：先加载本地登录凭据（或用户传入的 cookie）
                var bootstrap = new MyOption { Cookie = settings.Cookie, AccessToken = settings.AccessToken, UseTvApi = settings.UseTvApi, UseAppApi = settings.UseAppApi };
                Program.LoadCredentials(bootstrap);

                Logger.Log("正在获取稍后再看列表...");
                var list = await FetchWatchLaterAsync(cancellationToken);
                if (list.Count == 0)
                {
                    Logger.Log("稍后再看列表为空");
                    return 0;
                }

                var targets = settings.Limit > 0 ? list.Take(settings.Limit).ToList() : list;
                Logger.Log($"共 {list.Count} 个稍后再看，开始下载 {targets.Count} 个...");
                foreach (var (aid, title) in targets)
                {
                    Logger.Log($"--- 下载 av{aid} {title} ---");
                    try
                    {
                        var opt = BuildOption($"av{aid}", settings);
                        await Program.DoWorkAsync(opt, cancellationToken);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException)
                    {
                        // 单个视频失败不应中止整批稍后再看
                        Logger.LogWarn($"av{aid} 下载失败（继续下一个）: {ex.Message}");
                    }
                }
                return 0;
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarn("已取消");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.LogError($"稍后再看下载失败: {ex.Message}");
                return 1;
            }
        }).GetAwaiter().GetResult();
    }

    private static async Task<List<(string Aid, string Title)>> FetchWatchLaterAsync(CancellationToken token)
    {
        const string api = "https://api.bilibili.com/x/v2/history/toview";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int code = root.GetPropertySafe("code").GetInt32();
        if (code != 0)
            throw new InvalidOperationException($"获取稍后再看失败(code={code}): {root.GetValueAsStringSafe("message")}。该接口需要登录，请先运行 BBDown login 或传入 --cookie。");

        var list = new List<(string, string)>();
        foreach (var item in root.GetPropertySafe("data").EnumerateArraySafe("list"))
        {
            var aid = item.GetValueAsStringSafe("aid");
            if (aid == "") continue;
            list.Add((aid, item.GetValueAsStringSafe("title")));
        }
        return list;
    }

    private static MyOption BuildOption(string url, WatchLaterSettings s) => new()
    {
        Url = url,
        Cookie = s.Cookie,
        AccessToken = s.AccessToken,
        EncodingPriority = s.EncodingPriority,
        DfnPriority = s.DfnPriority,
        UseAppApi = s.UseAppApi,
        UseTvApi = s.UseTvApi,
        UseIntlApi = s.UseIntlApi,
        WorkDir = s.WorkDir,
    };
}

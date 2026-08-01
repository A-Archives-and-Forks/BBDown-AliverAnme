using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using BBDown;
using BBDown.Core;
using BBDown.Core.Fetcher;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubSettings : CommandSettings
{
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubAddSettings : SubSettings
{
    [CommandArgument(0, "<target>")]
    [Description("订阅目标: 视频 URL / av / bv / ep: / ss: / mid: / 合集 / 收藏夹等")]
    public string Target { get; set; } = "";

    [CommandOption("--name")]
    [Description("订阅显示名称(默认使用目标字符串)")]
    public string? Name { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubListSettings : SubSettings
{
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubRemoveSettings : SubSettings
{
    [CommandArgument(0, "<target>")]
    [Description("要移除的订阅目标")]
    public string Target { get; set; } = "";
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubCheckSettings : SubSettings
{
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
public class SubAddCommand : Command<SubAddSettings>
{
    protected override int Execute(CommandContext context, SubAddSettings settings, CancellationToken cancellationToken)
    {
        SubscriptionStore.Add(settings.Target, settings.Name);
        return 0;
    }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubListCommand : Command<SubListSettings>
{
    protected override int Execute(CommandContext context, SubListSettings settings, CancellationToken cancellationToken)
    {
        var subs = SubscriptionStore.Load();
        if (subs.Count == 0)
        {
            Logger.Log("当前没有订阅，请先用 BBDown sub add <目标> 添加");
            return 0;
        }
        Logger.Log($"共 {subs.Count} 个订阅:");
        foreach (var s in subs.OrderBy(s => s.AddedAt))
        {
            Logger.Log($"  {s.Target}  [{s.Name}]  (添加于 {DateTimeOffset.FromUnixTimeSeconds(s.AddedAt).LocalDateTime:yyyy-MM-dd HH:mm})");
        }
        return 0;
    }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubRemoveCommand : Command<SubRemoveSettings>
{
    protected override int Execute(CommandContext context, SubRemoveSettings settings, CancellationToken cancellationToken)
    {
        SubscriptionStore.Remove(settings.Target);
        return 0;
    }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubCheckCommand : Command<SubCheckSettings>
{
    protected override int Execute(CommandContext context, SubCheckSettings settings, CancellationToken cancellationToken)
    {
        // Task.Run avoids deadlock if called from a thread with a SynchronizationContext。
        // 批量检查期间 Ctrl+C 会取消当前下载；直接退出进程则等效于整体取消。
        return Task.Run(async () =>
        {
            var subs = SubscriptionStore.Load();
            if (subs.Count == 0)
            {
                Logger.LogWarn("当前没有订阅，请先用 BBDown sub add <目标> 添加");
                return 0;
            }

            // 订阅解析与拉取（VIP/登录态内容）需要凭据：先把命令行传入的 --cookie/--access-token
            // 写入当前 async 流，否则 ResolveAsync / FetcherFactory.FetchAsync 会在未登录状态下执行
            if (!string.IsNullOrEmpty(settings.Cookie) || !string.IsNullOrEmpty(settings.AccessToken))
            {
                Config.Apply(Config.Current with
                {
                    Cookie = settings.Cookie,
                    Token = settings.AccessToken.Replace("access_token=", ""),
                });
            }

            foreach (var sub in subs)
            {
                Logger.Log($"检查订阅: {sub.Name} ({sub.Target})");
                try
                {
                    string resolved = await UrlResolver.ResolveAsync(sub.Target);
                    if (string.IsNullOrEmpty(resolved)) continue;

                    var fetcher = FetcherFactory.CreateFetcher(resolved, settings.UseIntlApi);
                    var vInfo = await fetcher.FetchAsync(resolved);

                    var allAids = vInfo.PagesInfo.Select(p => p.aid).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
                    var history = SubscriptionStore.LoadHistory(sub.Target);
                    var newAids = allAids.Where(a => !history.Contains(a)).ToList();

                    if (newAids.Count == 0)
                    {
                        Logger.Log("  没有新增内容");
                        continue;
                    }

                    Logger.Log($"  发现 {newAids.Count} 个新内容: av{string.Join(", av", newAids)}");
                    foreach (var aid in newAids)
                    {
                        var opt = BuildOption($"av{aid}", settings);
                        await Program.DoWorkAsync(opt, cancellationToken);
                        SubscriptionStore.RecordDownloaded(sub.Target, aid);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                            or InvalidOperationException or IOException or ArgumentException)
                {
                    Logger.LogWarn($"  订阅检查失败: {ex.Message}");
                }
            }
            return 0;
        }).GetAwaiter().GetResult();
    }

    private static MyOption BuildOption(string url, SubCheckSettings s) => new()
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

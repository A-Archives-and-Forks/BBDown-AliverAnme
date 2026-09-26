using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using BBDown;
using BBDown.Core;
using BBDown.Core.Fetcher;
using BBDown.Core.Util;

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

    [CommandOption("--per-sub-dir")]
    [Description("每个订阅下载到 <work-dir>/<订阅名>/ 子目录(订阅名取 sub add --name, 缺省为 target, 经路径净化)")]
    public bool PerSubDir { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubAddCommand : AsyncCommand<SubAddSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SubAddSettings settings, CancellationToken cancellationToken)
    {
        // RF-93：显示名同时是 sub check --per-sub-dir 的目录名；缺省回退 target 后净化结果
        // 形如 mid_163637592 / https___space_bilibili_com_...，多订阅时几乎不可辨认。
        // 未指定 --name 时提示一次（不改变既有行为）。
        if (string.IsNullOrWhiteSpace(settings.Name))
            Logger.LogWarn("未指定 --name，显示名将回退为订阅目标（也是 sub check --per-sub-dir 的目录名，建议用 --name 指定易读名称）");
        await SubscriptionStore.AddAsync(settings.Target, settings.Name, cancellationToken);
        return 0;
    }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubListCommand : AsyncCommand<SubListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SubListSettings settings, CancellationToken cancellationToken)
    {
        var subs = await SubscriptionStore.LoadAsync(cancellationToken);
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
public class SubRemoveCommand : AsyncCommand<SubRemoveSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SubRemoveSettings settings, CancellationToken cancellationToken)
    {
        await SubscriptionStore.RemoveAsync(settings.Target, cancellationToken);
        return 0;
    }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class SubCheckCommand : AsyncCommand<SubCheckSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SubCheckSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            // 批量检查期间 Ctrl+C 会取消当前下载；直接退出进程则等效于整体取消。
            var subs = await SubscriptionStore.LoadAsync(cancellationToken);
            if (subs.Count == 0)
            {
                Logger.LogWarn("当前没有订阅，请先用 BBDown sub add <目标> 添加");
                return 0;
            }

            // -w 必须先绝对化且只解析一次（RF-89）：ChangeWorkingDir（CLI 非 serve）会把进程
            // CWD 切到上一个订阅的下载目录，相对 -w 到第二个订阅会基于该目录再拼一层，
            // 产出 <root>/<sub1>/<sub2> 嵌套。
            // 非法 -w 是整批无效的输入错误：明确报错 + 退出码 1。此前解析发生在
            // CheckSubscriptionsAsync 的逐订阅循环之前且不在任何 try 内，抛出的
            // ArgumentException 会逃到 Spectre 命令级处理器，被报成误导性的
            // "请尝试升级到最新版本后重试!" 并静默放弃其余全部订阅。
            if (!Program.TryResolveWorkDir(settings.WorkDir, out string resolvedWorkDir, out string workDirError))
            {
                Logger.LogError($"工作目录无效: {workDirError}");
                return 1;
            }
            settings.WorkDir = resolvedWorkDir;

            // 订阅解析与拉取（VIP/登录态内容）需要凭据：
            // LoadCredentials 会优先应用命令行 --cookie/--access-token，否则加载本地 BBDown.data。
            // 此前只处理显式传参，已登录但未传参时枚举阶段以匿名身份执行，VIP/区域订阅会被误判为空。
            var sessionOption = new MyOption
            {
                Cookie = settings.Cookie,
                AccessToken = settings.AccessToken,
                UseTvApi = settings.UseTvApi,
                UseAppApi = settings.UseAppApi,
                UseIntlApi = settings.UseIntlApi,
            };

            // 统一初始化请求会话：订阅枚举（mid: 空间/收藏夹/合集等）经 SpaceVideoFetcher →
            // Parser.WbiSign 签名，必须先取得 wbi，否则空 wbi 的 w_rid 会被 B 站拒绝。
            // 返回的完整会话（含本地凭据与新 wbi）在父流程（SubCheck 自身异步流）内显式应用——
            // 子方法内 AsyncLocal 写入不会回流，只应用 newWbi 会让本地凭据丢失。
            // 会话初始化纳入 try：Ctrl+C 落在此处按"已取消"处理，而非穿透全局
            // handler 返回 130（RF-32 路径②）。
            var session = await Program.InitializeRequestSessionAsync(sessionOption, cancellationToken);
            if (session is not null) Core.Config.Apply(session);

            int failedSubs = await CheckSubscriptionsAsync(subs, settings, cancellationToken);
            if (failedSubs > 0)
            {
                Logger.LogWarn($"订阅检查完成，{failedSubs} 个订阅失败");
                return 1;
            }
            return 0;
        }
        // 用户取消语义（RF-32）：与 watchlater 及文档契约（CLI-Reference 退出码表）对齐——
        // Ctrl+C / 关停属主动取消返回 0；token 未取消的 OCE（HttpClient 超时已在上层过滤器
        // 按 TCE 分类，到这里的多为内部联动 CTS）是真实失败，返回 1 而非以 0 掩盖。
        catch (OperationCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarn("已取消");
                return 0;
            }
            Logger.LogError($"订阅检查超时或被中断: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 逐订阅检查并增量下载新内容，返回失败的订阅数。用户取消与订阅数据损坏异常原样上抛，
    /// 由 <see cref="SubCheckCommand.ExecuteAsync"/> 分类为退出码（RF-30/RF-32）。
    /// </summary>
    private static async Task<int> CheckSubscriptionsAsync(List<Subscription> subs, SubCheckSettings settings, CancellationToken cancellationToken)
    {
        // -w 已由 ExecuteAsync 经 TryResolveWorkDir 绝对化且只解析一次（RF-89）：本方法内不得
        // 再解析——循环前的解析点不在任何 try 内，抛出的 ArgumentException 会逃出命令级
        // 异常过滤器（ExecuteAsync 只捕获 OperationCanceledException）。
        // --per-sub-dir 的基目录：空 -w 时用检查启动时的 CWD（下载过程中 ChangeWorkingDir
        // 会写进程 CWD，相对路径到第二个订阅会漂移，须先捕获）。
        string baseWorkDir = settings.WorkDir.Length == 0 ? Directory.GetCurrentDirectory() : settings.WorkDir;
        // 名称槽位每个订阅都占用（与是否有新增无关），冲突序号才能跨 run 稳定
        var usedSubDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int failedSubs = 0;
        foreach (var sub in subs)
        {
            string subWorkDir = Path.Combine(baseWorkDir, ResolveSubDirName(sub, usedSubDirs));
            Logger.Log($"检查订阅: {sub.Name} ({sub.Target})"
                + (settings.PerSubDir ? $"  输出目录: {subWorkDir}" : ""));
            try
            {
                string resolved = await UrlResolver.ResolveAsync(sub.Target, cancellationToken);
                if (string.IsNullOrEmpty(resolved)) continue;

                var fetcher = FetcherFactory.CreateFetcher(resolved, settings.UseIntlApi);
                var vInfo = await fetcher.FetchAsync(resolved, cancellationToken);

                var allAids = vInfo.PagesInfo.Select(p => p.aid).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
                var history = await SubscriptionStore.LoadHistoryAsync(sub.Target, cancellationToken);
                var newAids = allAids.Where(a => !history.Contains(a)).ToList();

                if (newAids.Count == 0)
                {
                    Logger.Log("  没有新增内容");
                    continue;
                }

                Logger.Log($"  发现 {newAids.Count} 个新内容: av{string.Join(", av", newAids)}");
                bool anyAidFailed = false;
                foreach (var aid in newAids)
                {
                    try
                    {
                        var opt = BuildOption($"av{aid}", settings,
                            settings.PerSubDir ? subWorkDir : settings.WorkDir);
                        await Program.DoWorkAsync(opt, cancellationToken);
                        await SubscriptionStore.RecordDownloadedAsync(sub.Target, aid, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    // 订阅数据损坏（RecordDownloaded 隔离历史文件后抛出）必须终止整个
                    // sub check（RF-30）：SubscriptionDataCorruptException 继承自
                    // InvalidOperationException，会被下方含基类的过滤器吞掉——历史文件已被
                    // 隔离移走，下一个 aid 的 RecordDownloaded 会静默重建仅含自身的空历史
                    // 并原子写回，全部订阅下载历史就此清零、下次 check 全量重下。
                    catch (SubscriptionDataCorruptException)
                    {
                        throw;
                    }
                    // UnauthorizedAccessException（RF-44）：与下载页过滤器同步扩充——
                    // 只读属性文件/受控文件夹访问等本地权限错误按"单 aid 失败"继续。
                    // InvalidDataException（RF-72）：有界响应体/帧校验抛型，同族。
                    catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                                or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException
                                                or TimeoutException or TaskCanceledException or InvalidDataException)
                    {
                        anyAidFailed = true;
                        Logger.LogWarn($"  av{aid} 下载失败（继续下一个）: {ex.Message}");
                    }
                }
                if (anyAidFailed) failedSubs++;
            }
            catch (SubscriptionDataCorruptException)
            {
                // 订阅持久化数据损坏（历史/清单损坏）：必须终止整个 sub check，不能按
                // 普通单订阅失败继续——否则后续订阅会因历史文件已不存在而把全部内容
                // 当作新增重新下载，并覆盖一份不完整的历史。
                throw;
            }
            // 用户取消（RF-32 路径①）：从 per-aid 重抛上来的取消不能被下方含
            // TaskCanceledException 的过滤器吞成"订阅检查失败"——否则后续每个订阅都在
            // 已取消的 token 上立刻失败，最终以"N 个订阅失败"+退出码 1 掩盖主动取消。
            // 上抛由 ExecuteAsync 分类为 0。
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            // UnauthorizedAccessException（RF-44）：与下载页过滤器同步扩充。
            // InvalidDataException（RF-72）：有界响应体/帧校验抛型，同族。
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                        or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException
                                        or TimeoutException or TaskCanceledException or InvalidDataException)
            {
                // 单个订阅失败不中止其余订阅，但必须计入失败数：
                // 全部失败仍返回 0 会让脚本/CI 无法区分"全部成功"与"全部失败"
                failedSubs++;
                Logger.LogWarn($"  订阅检查失败: {ex.Message}");
            }
        }
        return failedSubs;
    }

    /// <summary>
    /// --per-sub-dir：把订阅显示名净化为安全的目录段（RF-18 同款净化——剔除路径分隔符/
    /// 控制字符/保留名/纯点段，超长截断），并与既有槽位去重（OrdinalIgnoreCase 对齐
    /// Windows 文件系统）：两个订阅净化后同名（如 "a:b"/"a?b" → "a_b"）或显示名重复时
    /// 追加 -2/-3 序号，避免互相覆盖。槽位在循环内逐订阅占用，序号跨 run 稳定。
    /// </summary>
    internal static string ResolveSubDirName(Subscription sub, HashSet<string> usedDirs)
    {
        // 显示名为空/空白时回退 target：判据必须用**净化前**的原始值——SanitizePathSegment
        // 对空/纯空白/纯点输入兜底为 "_"、永不返回空串（GetValidFileName 契约），拿净化结果
        // 判断会让回退永远走不到，文档与 PR 声称的"缺省为 target"形同虚设（RF-91）。
        // 判据与 SubscriptionStore.AddAsync 的 "IsNullOrWhiteSpace(name) ? target : name" 一致。
        string raw = string.IsNullOrWhiteSpace(sub.Name) ? sub.Target : sub.Name;
        string name = PathUtil.SanitizePathSegment(raw);
        string candidate = name;
        int seq = 2;
        while (!usedDirs.Add(candidate))
            candidate = $"{name}-{seq++}";
        return candidate;
    }

    private static MyOption BuildOption(string url, SubCheckSettings s, string workDir) => new()
    {
        Url = url,
        Cookie = s.Cookie,
        AccessToken = s.AccessToken,
        EncodingPriority = s.EncodingPriority,
        DfnPriority = s.DfnPriority,
        UseAppApi = s.UseAppApi,
        UseTvApi = s.UseTvApi,
        UseIntlApi = s.UseIntlApi,
        WorkDir = workDir,
    };
}

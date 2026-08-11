using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using BBDown.Core;
using BBDown.Core.Entity;

using BBDown.Core.Util;
using System.Text.Json;
namespace BBDown;

internal partial class Program
{
    public static (Dictionary<string, byte> encodingPriority, Dictionary<string, int> dfnPriority, string? firstEncoding,
        bool downloadDanmaku, BBDownDanmakuFormat[] downloadDanmakuFormats, string input, string savePathFormat, string lang, string aidOri, int delay)
        SetUpWork(MyOption myOption)
    {
        //处理废弃选项
        HandleDeprecatedOptions(myOption);

        //处理冲突选项
        HandleConflictingOptions(myOption);

        //校验数值选项，避免非法值在下载或混流阶段以离奇的方式失败
        ValidateNumericOptions(myOption);

        //寻找并设置所需的二进制文件路径
        FindBinaries(myOption);

        //切换工作目录
        ChangeWorkingDir(myOption);

        //解析优先级
        var encodingPriority = ParseEncodingPriority(myOption, out var firstEncoding);
        var dfnPriority = ParseDfnPriority(myOption);

        //优先使用用户设置的UA
        HTTPUtil.UserAgent = string.IsNullOrEmpty(myOption.UserAgent) ? HTTPUtil.UserAgent : myOption.UserAgent;

        bool downloadDanmaku = myOption.DownloadDanmaku || myOption.DanmakuOnly;
        BBDownDanmakuFormat[] downloadDanmakuFormats = ParseDownloadDanmakuFormats(myOption);

        string input = myOption.Url;
        string savePathFormat = myOption.FilePattern;
        string lang = myOption.Language;
        string aidOri = ""; //原始aid
        int delay = myOption.DelayPerPage;
        Config.Apply(new AppSettings(
            Cookie: myOption.Cookie,
            // System.Text.Json 对 JSON 中显式 null 会覆盖属性初始化器，serve 请求体
            // {"accessToken": null} 可把 AccessToken 置为 null，这里必须防空
            Token: (myOption.AccessToken ?? "").Replace("access_token=", ""),
            DebugLog: myOption.Debug,
            Host: myOption.Host,
            EpHost: myOption.EpHost,
            TvHost: myOption.TvHost,
            Area: myOption.Area,
            SkipSslCheck: myOption.Insecure,
            MuxerTimeoutMinutes: myOption.MuxerTimeout,
            MaxRetryCount: myOption.RetryCount,
            RetryDelayMs: myOption.RetryDelay,
            ThreadSegmentSizeMb: myOption.ThreadSegmentSize
        ));

        Logger.LogDebug("AppDirectory: {0}", APP_DIR);
        if (Config.Current.DebugLog)
        {
            var savedCookie = myOption.Cookie;
            var savedToken = myOption.AccessToken;
            myOption.Cookie = string.IsNullOrEmpty(savedCookie) ? "" : "***";
            myOption.AccessToken = string.IsNullOrEmpty(savedToken) ? "" : "***";
            Logger.LogDebug("运行参数：{0}", JsonSerializer.Serialize(myOption, MyOptionJsonContext.Default.MyOption));
            myOption.Cookie = savedCookie;
            myOption.AccessToken = savedToken ?? "";
        }
        return (encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats, input, savePathFormat, lang, aidOri, delay);
    }

    public static async Task<(string fetchedAid, VInfo vInfo, string apiType, AppSettings? session)> GetVideoInfoAsync(MyOption myOption, string aidOri, string input, CancellationToken cancellationToken = default)
    {
        // 统一初始化请求会话：加载凭据 + 登录检查 + 提取 wbi。返回完整的会话配置
        // （含本地 BBDown.data 加载出的 Cookie/Token，以及提取的 wbi），由调用方在
        // 自身异步流内 Config.Apply 一次性应用——子方法内的 AsyncLocal 写入不会回流
        // 父流程（见 ConfigPropagationTests），只返回 newWbi 会让本地凭据在返回后丢失。
        AppSettings? session = await InitializeRequestSessionAsync(myOption, cancellationToken);
        if (session is not null) Config.Apply(session);

        Logger.Log("获取aid...");
        aidOri = await UrlResolver.ResolveAsync(input, cancellationToken);
        Logger.Log($"获取aid结束: {aidOri}");

        if (string.IsNullOrEmpty(aidOri))
        {
            throw new ArgumentException("输入有误：无法识别的视频 URL 或 ID");
        }

        Logger.Log("获取视频信息...");
        IFetcher fetcher = FetcherFactory.CreateFetcher(aidOri, myOption.UseIntlApi);
        VInfo? vInfo = null;

        // 只输入 EP/SS 时优先按番剧查找，如果找不到则尝试按课程查找
        try
        {
            vInfo = await fetcher.FetchAsync(aidOri, cancellationToken);
        }
        // 回退只对 ep: 前缀成立：其余输入（mid:、favlist:、listBizId: 等）与课程无关，
        // 此前它们同样会走进这里，打印"未找到此 EP/SS 对应番剧信息"这类无关提示，
        // 再用未经改动的 aidOri 重试一次——既误导用户，也把失败的请求翻倍。
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException
                                  && aidOri.StartsWith("ep:"))
        {
            // B站返回非番剧JSON结构（可能是课程），尝试按课程查找
            Logger.LogWarn("未找到此 EP/SS 对应番剧信息, 正在尝试按课程查找。");

            aidOri = aidOri.Replace("ep", "cheese");
            Logger.Log("新的 aid: " + aidOri);

            if (string.IsNullOrEmpty(aidOri))
            {
                throw new ArgumentException("输入有误：无法获取视频信息");
            }

            Logger.Log("获取视频信息...");
            fetcher = FetcherFactory.CreateFetcher(aidOri, myOption.UseIntlApi);
            vInfo = await fetcher.FetchAsync(aidOri, cancellationToken);
        }

        string title = vInfo.Title;
        long pubTime = vInfo.PubTime;
        Logger.LogColor("视频标题: " + title);
        if (pubTime != 0)
        {
            Logger.Log("发布时间: " + FormatTimeStamp(pubTime, "yyyy-MM-dd HH:mm:ss zzz"));
        }
        var bvid = vInfo.PagesInfo.FirstOrDefault()?.bvid;
        if (!string.IsNullOrEmpty(bvid) && !myOption.UseIntlApi)
        {
            Logger.Log($"视频URL: https://www.bilibili.com/video/{bvid}/");
        }
        var mid = vInfo.PagesInfo.FirstOrDefault(p => !string.IsNullOrEmpty(p.ownerMid))?.ownerMid;
        if (!string.IsNullOrEmpty(mid))
        {
            Logger.Log($"UP主页: https://space.bilibili.com/{mid}");
        }

        if (vInfo.IsSteinGate && myOption.UseTvApi)
        {
            Logger.Log("视频为互动视频，暂时不支持tv下载，修改为默认下载");
            myOption.UseTvApi = false;
        }
        string apiType = myOption.UseTvApi ? "TV" : (myOption.UseAppApi ? "APP" : (myOption.UseIntlApi ? "INTL" : "WEB"));

        //打印分P信息
        List<Page> pagesInfo = vInfo.PagesInfo;
        bool more = false;
        foreach (Page p in pagesInfo)
        {
            if (!myOption.ShowAll)
            {
                if (more && p.index != pagesInfo.Count) continue;
                if (!more && p.index > 5)
                {
                    Logger.Log("......");
                    more = true;
                    continue;
                }
            }

            Logger.Log($"P{p.index}: [{p.cid}] [{p.title}] [{BBDownUtil.FormatTime(p.dur)}]");
        }
        // 返回 session 由父流程在自身流内 Config.Apply：AsyncLocal 写入不会回流父调用方
        // （父流程的 ExecutionContext 快照在 await 前已捕获）。父流程在 GetVideoInfoAsync
        // 返回后继续调用 DownloadPagesAsync → Parser.WbiSign，必须用上这一版的凭据与新密钥，
        // 否则 w_rid 仍用旧密钥签名（密钥轮换后服务器会拒绝），本地凭据也会丢失。
        return (aidOri, vInfo, apiType, session);
    }

    /// <summary>
    /// 统一初始化一次请求会话：加载凭据（含本地 BBDown.data 等文件）→ 登录检查 → 提取 wbi。
    /// 返回完整的 <see cref="AppSettings"/>（含凭据与新 wbi），由调用方在自身异步流内
    /// Config.Apply 一次性应用——本方法不写 Config（AsyncLocal 写入只影响本方法上下文，
    /// 不会回流调用方）。CLI 下载、Serve 任务、订阅检查（SubCheck）、稍后再看（WatchLater）
    /// 都应先调用本方法并应用返回值，否则空间/收藏夹/合集等经 Parser.WbiSign 签名的请求
    /// 会用空 wbi 发出（B 站返回签名错误），本地凭据也会在返回后丢失。
    /// 返回 null 表示无需更新（如 INTL/TV 模式且未加载到新凭据）。
    /// </summary>
    public static async Task<AppSettings?> InitializeRequestSessionAsync(MyOption myOption, CancellationToken cancellationToken = default)
    {
        // 计算加载后的凭据（显式传参优先，否则本地文件），但不 Apply——
        // 由调用方拿返回值在自身流内应用，避免子方法内的 AsyncLocal 写入丢失。
        var (cookie, token) = LoadCredentials(myOption);

        string? newWbi = null;
        // 检测是否登录了账号并提取 wbi。WBI 是元数据 fetcher（SpaceVideoFetcher 的
        // mid: 列表）的签名依赖，不能只根据最终播放 API 模式决定：FetcherFactory 无论
        // TV/INTL/WEB 都把 mid: 路由到 SpaceVideoFetcher，后者无条件用 Parser.WbiSign
        // 签名（x/space/wbi/arc/search 是 WEB 接口）。因此只要可能解析空间类目标就初始化。
        if (Config.Current.Area == "")
        {
            Logger.Log("检测账号登录...");
            // CheckLoginWithDetails 内部经 HTTPUtil 读 Config.Current.Cookie（不消费传入的
            // cookie 参数），而本方法尚未把计算出的本地凭据写入当前流。若不清空旧值，用户
            // 显式传 --cookie 时 HTTPUtil 仍读 Config.Current.Cookie（可能是旧的/空的），
            // 导致登录检测用错误的凭据误报"Cookie 已过期"。这里把凭据应用到当前异步流
            // （只影响本方法上下文，不影响全局/父流），使检测使用正确凭据。
            Core.Config.ApplyToCurrentAsyncFlow(Config.Current with { Cookie = cookie, Token = token });
            var (isLoggedIn, cookieExpired, wbi) = await BBDownUtil.CheckLoginWithDetails(cookie, cancellationToken);
            newWbi = wbi;
            if (!isLoggedIn)
            {
                if (cookieExpired)
                {
                    Logger.LogWarn("========================================");
                    Logger.LogWarn("  Cookie 已过期！");
                    Logger.LogWarn("  请运行 BBDown login 重新扫码登录以获取新 Cookie。");
                    Logger.LogWarn("  或者使用 --use-tv-api 配合 --access-token 下载。");
                    Logger.LogWarn("  （若已执行 BBDown logintv，请加上 --use-tv-api）");
                    Logger.LogWarn("========================================");
                }
                else
                {
                    Logger.LogWarn("========================================");
                    Logger.LogWarn("  你尚未登录B站账号！");
                    Logger.LogWarn("  未登录状态下仅能下载6分钟试看片段。");
                    Logger.LogWarn("  请运行 BBDown login 扫码登录以获取完整视频。");
                    Logger.LogWarn("  （若已执行 BBDown logintv，请在下载命令中加上 --use-tv-api）");
                    Logger.LogWarn("========================================");
                }
            }
        }

        // 构造要应用到当前流的完整会话：含加载出的凭据（可能来自本地文件）与新 wbi。
        // 若与当前流配置无差异则返回 null，避免无谓的 Config.Apply。
        var current = Config.Current;
        var session = current with { Cookie = cookie, Token = token, Wbi = newWbi ?? current.Wbi };
        if (session == current) return null;
        return session;
    }

}

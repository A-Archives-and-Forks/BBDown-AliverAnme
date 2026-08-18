using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace BBDown;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class MyOption : CommandSettings
{
    [CommandArgument(0, "<URL>")]
    [Description("视频地址 或 av|bv|BV|ep|ss")]
    public string Url { get; set; } = "";

    [CommandOption("-t|--use-tv-api")]
    [Description("使用TV端解析模式")]
    public bool UseTvApi { get; set; }

    [CommandOption("-a|--use-app-api")]
    [Description("使用APP端解析模式")]
    public bool UseAppApi { get; set; }

    [CommandOption("--use-intl-api")]
    [Description("使用国际版(东南亚视频)解析模式")]
    public bool UseIntlApi { get; set; }

    [CommandOption("--use-mp4box")]
    [Description("使用MP4Box来混流")]
    public bool UseMP4box { get; set; }

    [CommandOption("-e|--encoding-priority")]
    [Description("视频及音频编码的选择优先级, 用逗号分割 例: \"hevc,av1,avc,flac,eac3,m4a\"")]
    public string? EncodingPriority { get; set; }

    [CommandOption("-q|--dfn-priority")]
    [Description("画质优先级,用逗号分隔 例: \"8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界\"")]
    public string? DfnPriority { get; set; }

    [CommandOption("-I|--only-show-info")]
    [Description("仅解析而不进行下载")]
    public bool OnlyShowInfo { get; set; }

    [CommandOption("--show-all")]
    [Description("展示所有分P标题")]
    public bool ShowAll { get; set; }

    [CommandOption("--use-aria2c")]
    [Description("调用aria2c进行下载(你需要自行准备好二进制可执行文件)")]
    public bool UseAria2c { get; set; }

    [CommandOption("-i|--interactive")]
    [Description("交互式选择清晰度")]
    public bool Interactive { get; set; }

    [CommandOption("--hide-streams")]
    [Description("不要显示所有可用音视频流")]
    public bool HideStreams { get; set; }

    [CommandOption("--multi-thread")]
    [Description("使用多线程下载(默认开启)")]
    // Spectre.Console.Cli 在 flag 未出现于命令行时会把 bool 写回 default(false)，
    // 覆盖属性初始化器；必须用 [DefaultValue] 才能让"默认开启"真正生效。
    [DefaultValue(true)]
    public bool MultiThread { get; set; } = true;

    [CommandOption("--simply-mux")]
    [Description("精简混流，不增加描述、作者等信息")]
    public bool SimplyMux { get; set; } = false;

    [CommandOption("--video-only")]
    [Description("仅下载视频")]
    public bool VideoOnly { get; set; }

    [CommandOption("--audio-only")]
    [Description("仅下载音频")]
    public bool AudioOnly { get; set; }

    [CommandOption("--danmaku-only")]
    [Description("仅下载弹幕")]
    public bool DanmakuOnly { get; set; }

    [CommandOption("--cover-only")]
    [Description("仅下载封面")]
    public bool CoverOnly { get; set; }

    [CommandOption("--sub-only")]
    [Description("仅下载字幕")]
    public bool SubOnly { get; set; }

    [CommandOption("--debug")]
    [Description("输出调试日志")]
    public bool Debug { get; set; }

    [CommandOption("--skip-mux")]
    [Description("跳过混流步骤")]
    public bool SkipMux { get; set; }

    [CommandOption("--insecure")]
    [Description("跳过SSL证书验证(仅用于抓包/代理场景)")]
    public bool Insecure { get; set; }

    [CommandOption("--decrypt-drm")]
    [Description("尝试解密DRM保护视频")]
    public bool DecryptDrm { get; set; }

    [CommandOption("--allow-preview")]
    [Description("允许下载充电专属视频的试看片段(默认遇到试看片段直接中止)")]
    public bool AllowPreview { get; set; }

    [CommandOption("--key")]
    [Description("DRM解密密钥 (hex)")]
    public string? DrmKeyHex { get; set; }

    [CommandOption("--kid")]
    [Description("DRM密钥ID (hex)")]
    public string? DrmKidHex { get; set; }

    [CommandOption("--mp4decrypt-path")]
    [Description("设置mp4decrypt的路径")]
    public string Mp4decryptPath { get; set; } = "";

    [CommandOption("--wvd-path")]
    [Description("设置device.wvd的路径")]
    public string WvdPath { get; set; } = "";

    [CommandOption("--skip-subtitle")]
    [Description("跳过字幕下载")]
    public bool SkipSubtitle { get; set; }

    [CommandOption("--skip-cover")]
    [Description("跳过封面下载")]
    public bool SkipCover { get; set; }

    [CommandOption("--force-http")]
    [Description("下载音视频时强制使用HTTP协议替换HTTPS(默认关闭，使用HTTPS)")]
    [DefaultValue(false)]
    public bool ForceHttp { get; set; } = false;

    [CommandOption("-d|--download-danmaku")]
    [Description("下载弹幕")]
    public bool DownloadDanmaku { get; set; } = false;

    [CommandOption("--download-danmaku-formats")]
    [Description("指定需下载的弹幕格式, 用逗号分隔")]
    public string? DownloadDanmakuFormats { get; set; }

    [CommandOption("--danmaku-filter")]
    [Description("弹幕过滤: 包含任一关键词的弹幕将被丢弃(逗号分隔)")]
    public string? DanmakuFilter { get; set; }

    [CommandOption("--danmaku-filter-user")]
    [Description("弹幕过滤: 指定发送者 midHash 的弹幕将被丢弃(逗号分隔)")]
    public string? DanmakuFilterUser { get; set; }

    [CommandOption("--comments")]
    [Description("同时下载视频评论区, 保存为 JSON")]
    public bool DownloadComments { get; set; }

    [CommandOption("--notify-webhook")]
    [Description("下载完成后向指定 URL 发送 HTTP POST 通知(CLI 版回调)")]
    public string? NotifyWebhook { get; set; }

    [CommandOption("--skip-ai")]
    [Description("跳过AI字幕下载（名称表示真跳过：值为 true 时不下载 AI 字幕；需下载请显式传 --skip-ai false）")]
    [DefaultValue(true)]
    public bool SkipAi { get; set; } = true;

    [CommandOption("--video-ascending")]
    [Description("视频升序(最小体积优先)")]
    public bool VideoAscending { get; set; } = false;

    [CommandOption("--audio-ascending")]
    [Description("音频升序(最小体积优先)")]
    public bool AudioAscending { get; set; } = false;

    [CommandOption("--allow-pcdn")]
    [Description("不替换PCDN域名, 仅在正常情况与--upos-host均无法下载时使用")]
    public bool AllowPcdn { get; set; } = false;

    [CommandOption("-F|--file-pattern")]
    [Description("使用内置变量自定义单P存储文件名")]
    public string FilePattern { get; set; } = "";

    [CommandOption("-M|--multi-file-pattern")]
    [Description("使用内置变量自定义多P存储文件名")]
    public string MultiFilePattern { get; set; } = "";

    [CommandOption("-p|--select-page")]
    [Description("选择指定分p或分p范围: (-p 8 或 -p 1,2 或 -p 3-5 或 -p ALL 或 -p LAST 或 -p 3,5,LATEST)")]
    public string SelectPage { get; set; } = "";

    [CommandOption("--language")]
    [Description("设置混流的音频语言(代码), 如chi, jpn等")]
    public string Language { get; set; } = "";

    [CommandOption("-u|--user-agent")]
    [Description("指定user-agent, 否则使用随机user-agent")]
    public string UserAgent { get; set; } = "";

    [CommandOption("-c|--cookie")]
    [Description("设置字符串cookie用以下载网页接口的会员内容")]
    public string Cookie { get; set; } = "";

    [CommandOption("--access-token")]
    [Description("设置access_token用以下载TV/APP接口的会员内容")]
    public string AccessToken { get; set; } = "";

    [CommandOption("--aria2c-args")]
    [Description("调用aria2c的附加参数")]
    public string Aria2cArgs { get; set; } = "";

    [CommandOption("--work-dir")]
    [Description("设置程序的工作目录")]
    public string WorkDir { get; set; } = "";

    [CommandOption("--ffmpeg-path")]
    [Description("设置ffmpeg的路径")]
    public string FFmpegPath { get; set; } = "";

    [CommandOption("--mp4box-path")]
    [Description("设置mp4box的路径")]
    public string Mp4boxPath { get; set; } = "";

    [CommandOption("--aria2c-path")]
    [Description("设置aria2c的路径")]
    public string Aria2cPath { get; set; } = "";

    [CommandOption("--upos-host")]
    [Description("自定义upos服务器")]
    public string UposHost { get; set; } = "";

    [CommandOption("--force-replace-host")]
    [Description("强制替换下载服务器host(默认开启)")]
    [DefaultValue(true)]
    public bool ForceReplaceHost { get; set; } = true;

    [CommandOption("--save-archives-to-file")]
    [Description("将下载过的视频记录到本地文件中, 用于后续跳过下载同个视频")]
    public bool SaveArchivesToFile { get; set; } = false;

    [CommandOption("--delay-per-page")]
    [Description("设置下载合集分P之间的下载间隔时间(单位: 秒, 默认无间隔)")]
    public int DelayPerPage { get; set; } = 0;

    [CommandOption("--muxer-timeout")]
    [Description("混流超时时长(分钟, 默认30)")]
    public int MuxerTimeout { get; set; } = 30;

    [CommandOption("--retry-count")]
    [Description("网络请求失败后的重试次数(默认3)")]
    public int RetryCount { get; set; } = 3;

    [CommandOption("--retry-delay")]
    [Description("重试间隔基础毫秒数(默认3000)")]
    public int RetryDelay { get; set; } = 3000;

    [CommandOption("--thread-segment-size")]
    [Description("多线程下载时分片大小(MB, 默认20)")]
    public int ThreadSegmentSize { get; set; } = 20;

    [CommandOption("--host")]
    [Description("指定BiliPlus host")]
    public string Host { get; set; } = "api.bilibili.com";

    [CommandOption("--ep-host")]
    [Description("指定BiliPlus EP host")]
    public string EpHost { get; set; } = "api.bilibili.com";

    [CommandOption("--tv-host")]
    [Description("自定义tv端接口请求Host")]
    public string TvHost { get; set; } = "api.snm0516.aisee.tv";

    [CommandOption("--area")]
    [Description("(hk|tw|th) 使用BiliPlus时必选, 指定BiliPlus area")]
    public string Area { get; set; } = "";

    [CommandOption("--config-file")]
    [Description("读取指定的BBDown本地配置文件")]
    public string? ConfigFile { get; set; }

    // 以下仅为兼容旧版本命令行，不建议使用
    [CommandOption("--aria2c-proxy", IsHidden = true)]
    public string Aria2cProxy { get; set; } = "";

    [CommandOption("--only-hevc", IsHidden = true)]
    public bool OnlyHevc { get; set; }

    [CommandOption("--only-avc", IsHidden = true)]
    public bool OnlyAvc { get; set; }

    [CommandOption("--only-av1", IsHidden = true)]
    public bool OnlyAv1 { get; set; }

    [CommandOption("--add-dfn-subfix", IsHidden = true)]
    public bool AddDfnSuffix { get; set; }

    [CommandOption("--no-padding-page-num", IsHidden = true)]
    public bool NoPaddingPageNum { get; set; }

    [CommandOption("--bandwith-ascending", IsHidden = true)]
    public bool BandwidthAscending { get; set; }
}

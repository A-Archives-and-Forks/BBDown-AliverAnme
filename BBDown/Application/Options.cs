using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using static BBDown.BBDownUtil;
using System.Linq;
using System.Text.RegularExpressions;
using BBDown.Core;
using BBDown.Core.Entity;

using BBDown.Core.Util;
using System.Text.Json;
namespace BBDown;

internal partial class Program
{
    /// <summary>
    /// 处理废弃选项。注意：--add-dfn-subfix 与 --no-padding-page-num 的默认路径调整
    /// 必须写入当前任务的 <see cref="MyOption.FilePattern"/>/<see cref="MyOption.MultiFilePattern"/>，
    /// 而不是改静态的 SinglePageDefaultSavePath/MultiPageDefaultSavePath——serve 模式下
    /// 每个任务都会执行一次 SetUpWork，改静态字段会让一个任务的废弃选项永久污染
    /// 后续所有任务的默认路径。
    /// </summary>
    private static void HandleDeprecatedOptions(MyOption myOption)
    {
        if (myOption.AddDfnSuffix)
        {
            Logger.LogWarn("--add-dfn-subfix 已被弃用, 建议使用 --file-pattern/-F 或 --multi-file-pattern/-M 来自定义输出文件名格式");
            if (string.IsNullOrEmpty(myOption.FilePattern) && string.IsNullOrEmpty(myOption.MultiFilePattern))
            {
                // 只在当前任务生效：追加后缀到本任务的实际模板，不碰静态默认值
                if (string.IsNullOrEmpty(myOption.FilePattern))
                    myOption.FilePattern = SinglePageDefaultSavePath + "[<dfn>]";
                if (string.IsNullOrEmpty(myOption.MultiFilePattern))
                    myOption.MultiFilePattern = MultiPageDefaultSavePath + "[<dfn>]";
                Logger.LogWarn($"已切换至 -F \"{myOption.FilePattern}\" -M \"{myOption.MultiFilePattern}\"");
            }
        }
        if (myOption.Aria2cProxy != "")
        {
            Logger.LogWarn("--aria2c-proxy 已被弃用, 请使用 --aria2c-args 来设置aria2c代理, 本次执行已添加该代理");
            myOption.Aria2cArgs += $" --all-proxy=\"{myOption.Aria2cProxy}\"";
        }
        if (myOption.OnlyHevc)
        {
            Logger.LogWarn("--only-hevc/-hevc 已被弃用, 请使用 --encoding-priority 来设置编码优先级, 本次执行已将hevc设置为最高优先级");
            myOption.EncodingPriority = "hevc";
        }
        if (myOption.OnlyAvc)
        {
            Logger.LogWarn("--only-avc/-avc 已被弃用, 请使用 --encoding-priority 来设置编码优先级, 本次执行已将avc设置为最高优先级");
            myOption.EncodingPriority = "avc";
        }
        if (myOption.OnlyAv1)
        {
            Logger.LogWarn("--only-av1/-av1 已被弃用, 请使用 --encoding-priority 来设置编码优先级, 本次执行已将av1设置为最高优先级");
            myOption.EncodingPriority = "av1";
        }
        if (myOption.NoPaddingPageNum)
        {
            Logger.LogWarn("--no-padding-page-num 已被弃用, 建议使用 --file-pattern/-F 或 --multi-file-pattern/-M 来自定义输出文件名格式");
            if (string.IsNullOrEmpty(myOption.FilePattern) && string.IsNullOrEmpty(myOption.MultiFilePattern))
            {
                // 只在当前任务生效：替换本任务多P模板里的补零占位符，不碰静态默认值
                myOption.MultiFilePattern = MultiPageDefaultSavePath.Replace("<pageNumberWithZero>", "<pageNumber>");
                Logger.LogWarn($"已切换至 -M \"{myOption.MultiFilePattern}\"");
            }
        }
        if (myOption.BandwidthAscending)
        {
            Logger.LogWarn("--bandwith-ascending 已被弃用, 建议使用 --video-ascending 与 --audio-ascending 来指定视频或音频是否升序, 本次执行已将视频与音频均设为升序");
            myOption.VideoAscending = true;
            myOption.AudioAscending = true;
        }
    }

    /// <summary>
    /// 解析用户指定的编码优先级
    /// </summary>
    /// <param name="myOption"></param>
    /// <returns></returns>
    private static Dictionary<string, byte> ParseEncodingPriority(MyOption myOption, out string firstEncoding)
    {
        var encodingPriority = new Dictionary<string, byte>();
        firstEncoding = "";
        if (myOption.EncodingPriority != null)
        {
            var encodingPriorityTemp = myOption.EncodingPriority
                .ToUpperInvariant()
                .Replace('，', ',')
                .Replace("-", string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrEmpty(s)).ToList();
            byte index = 0;
            firstEncoding = encodingPriorityTemp.FirstOrDefault() ?? "";
            foreach (string encoding in encodingPriorityTemp)
            {
                if (encodingPriority.ContainsKey(encoding))
                    continue;
                encodingPriority[encoding] = index;
                index++;
            }
        }
        return encodingPriority;
    }

    private static BBDownDanmakuFormat[] ParseDownloadDanmakuFormats(MyOption myOption)
    {
        if (string.IsNullOrEmpty(myOption.DownloadDanmakuFormats)) return BBDownDanmakuFormatInfo.DefaultFormats;

        var formats = myOption.DownloadDanmakuFormats.Replace("，", ",").ToLowerInvariant().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (formats.Any(format => !BBDownDanmakuFormatInfo.AllFormatNames.Contains(format)))
        {
            // RF-54：客户端可控原文进日志前单行化。
            Logger.LogError($"包含不支持的下载弹幕格式：{BBDownApiServer.SanitizeLogString(myOption.DownloadDanmakuFormats)}");
            return BBDownDanmakuFormatInfo.DefaultFormats;
        }

        return formats.Select(BBDownDanmakuFormatInfo.FromFormatName).ToArray();
    }

    /// <summary>
    /// 解析用户输入的清晰度规格优先级
    /// </summary>
    /// <param name="myOption"></param>
    /// <returns></returns>
    private static Dictionary<string, int> ParseDfnPriority(MyOption myOption)
    {
        var dfnPriority = new Dictionary<string, int>();
        if (myOption.DfnPriority != null)
        {
            var dfnPriorityTemp = myOption.DfnPriority.Replace("，", ",").Split(',').Select(s => s.ToUpperInvariant().Trim()).Where(s => !string.IsNullOrEmpty(s));
            int index = 0;
            foreach (string dfn in dfnPriorityTemp)
            {
                if (dfnPriority.ContainsKey(dfn)) { continue; }
                dfnPriority[dfn] = index;
                index++;
            }
        }
        return dfnPriority;
    }

    /// <summary>
    /// 寻找并设置所需的二进制文件
    /// </summary>
    /// <param name="myOption"></param>
    /// <exception cref="Exception"></exception>
    private static void FindBinaries(MyOption myOption)
    {
        if (!string.IsNullOrEmpty(myOption.FFmpegPath) && File.Exists(myOption.FFmpegPath))
        {
            BBDownMuxer.FFMPEG = myOption.FFmpegPath;
        }

        if (!string.IsNullOrEmpty(myOption.Mp4boxPath) && File.Exists(myOption.Mp4boxPath))
        {
            BBDownMuxer.MP4BOX = myOption.Mp4boxPath;
        }

        if (!string.IsNullOrEmpty(myOption.Aria2cPath) && File.Exists(myOption.Aria2cPath))
        {
            BBDownAria2c.ARIA2C = myOption.Aria2cPath;
        }
        //寻找ffmpeg或mp4box
        if (!myOption.SkipMux)
        {
            if (myOption.UseMP4box)
            {
                if (string.IsNullOrEmpty(BBDownMuxer.MP4BOX) || !File.Exists(BBDownMuxer.MP4BOX))
                {
                    var binPath = ExternalToolHelper.FindExecutable("mp4box") ?? ExternalToolHelper.FindExecutable("MP4box");
                    if (string.IsNullOrEmpty(binPath))
                        throw new FileNotFoundException(
                            "找不到可执行的mp4box文件。请安装 MP4Box（GPAC）并确保其已加入 PATH，" +
                            "或使用 --mp4box-path 指定路径（如 --mp4box-path C:/ffmpeg/bin/MP4Box.exe）。");
                    BBDownMuxer.MP4BOX = binPath;
                }
            }
            else if (string.IsNullOrEmpty(BBDownMuxer.FFMPEG) || !File.Exists(BBDownMuxer.FFMPEG))
            {
                var binPath = ExternalToolHelper.FindExecutable("ffmpeg");
                if (string.IsNullOrEmpty(binPath))
                    throw new FileNotFoundException(
                        "找不到可执行的ffmpeg文件。请安装 ffmpeg 并确保其已加入 PATH，" +
                        "或使用 --ffmpeg-path 指定路径（如 --ffmpeg-path C:/ffmpeg/bin/ffmpeg.exe）。");
                BBDownMuxer.FFMPEG = binPath;
            }
        }

        //寻找aria2c
        if (myOption.UseAria2c)
        {
            if (string.IsNullOrEmpty(BBDownAria2c.ARIA2C) || !File.Exists(BBDownAria2c.ARIA2C))
            {
                var binPath = ExternalToolHelper.FindExecutable("aria2c");
                if (string.IsNullOrEmpty(binPath))
                    throw new FileNotFoundException(
                        "找不到可执行的aria2c文件。请安装 aria2 并确保其已加入 PATH，" +
                        "或使用 --aria2c-path 指定路径（如 --aria2c-path C:/aria2/aria2c.exe）。");
                BBDownAria2c.ARIA2C = binPath;
            }

        }
    }

    /// <summary>
    /// 处理有冲突的选项
    /// </summary>
    /// <param name="myOption"></param>
    private static void HandleConflictingOptions(MyOption myOption)
    {
        //手动选择时不能隐藏流
        if (myOption.Interactive)
        {
            myOption.HideStreams = false;
        }
        //audioOnly和videoOnly同时开启则全部忽视
        if (myOption.AudioOnly && myOption.VideoOnly)
        {
            myOption.AudioOnly = false;
            myOption.VideoOnly = false;
        }
        if (myOption.SkipSubtitle)
        {
            myOption.SubOnly = false;
        }
    }

    /// <summary>供测试使用：直接校验数值选项，不触碰其余启动流程。</summary>
    internal static void ValidateNumericOptionsForTest(MyOption myOption) => ValidateNumericOptions(myOption);

    /// <summary>
    /// 校验数值选项的取值范围。
    /// 这些值会一路流入下载循环与混流超时，非法值不会当场报错，
    /// 而是变成"不下载任何数据就成功返回""分片切分不收敛"这类难以定位的故障。
    /// </summary>
    private static void ValidateNumericOptions(MyOption myOption)
    {
        // 上限 35000：MuxerTimeout 会以分钟乘 60000 传给 WaitForExit，
        // 超过约 35791 分钟即溢出为负数并抛 ArgumentOutOfRangeException
        const int maxMuxerTimeoutMinutes = 35000;

        // 统一使用单参数 ArgumentException：本项目启用了 UseSystemResourceKeys，
        // 带 paramName 的重载会在消息尾部拼出 "Arg_ParamName_Name, xxx" 这样的资源键
        if (myOption.MuxerTimeout < 1 || myOption.MuxerTimeout > maxMuxerTimeoutMinutes)
        {
            throw new ArgumentException(
                $"参数有误：--muxer-timeout 需在 1 ~ {maxMuxerTimeoutMinutes} 分钟之间，当前为 {myOption.MuxerTimeout}");
        }
        var retryPolicy = RetryPolicy.NormalizeForCli(myOption.RetryCount, myOption.RetryDelay);
        myOption.RetryCount = retryPolicy.RetryCount;
        myOption.RetryDelay = retryPolicy.RetryDelayMs;
        if (myOption.ThreadSegmentSize < 1 || myOption.ThreadSegmentSize > 1024)
        {
            throw new ArgumentException(
                $"参数有误：--thread-segment-size 需在 1 ~ 1024 MB 之间，当前为 {myOption.ThreadSegmentSize}（设为 0 会导致分片切分无法收敛）");
        }
        if (myOption.DelayPerPage < 0 || myOption.DelayPerPage > 600)
        {
            throw new ArgumentException(
                $"参数有误：--delay-per-page 需在 0 ~ 600 秒之间，当前为 {myOption.DelayPerPage}");
        }
    }

    /// <summary>
    /// 设置用户输入的自定义工作目录。返回解析后的绝对目录（未设置返回空串），
    /// 由调用方并入 <see cref="AppSettings.WorkDir"/> 写入任务流配置——
    /// SetUpWork 后续会整体 Config.Apply 一个新 AppSettings，这里若自行写入配置会被覆盖。
    /// </summary>
    /// <param name="myOption"></param>
    internal static string ChangeWorkingDir(MyOption myOption)
    {
        var dir = ResolveWorkDir(myOption.WorkDir);
        if (dir != "")
        {
            // CLI 单任务模式仍写进程 CWD：ffmpeg/aria2c 等子进程与外部工具按相对路径
            // 解析时依赖进程 CWD，单任务场景无并发污染问题，保留既有行为。
            // serve 模式绝不写进程 CWD——并发任务各自的 --work-dir 不能互相覆盖进程级状态，
            // 相对路径由 PathUtil.ResolveWorkPath 基于各任务流配置里的 WorkDir 解析。
            if (!Config.Current.IsServeMode) Environment.CurrentDirectory = dir;
            Logger.LogDebug("切换工作目录至：{0}", dir);
        }
        return dir;
    }

    /// <summary>
    /// 解析工作目录为绝对目录（展开环境变量、必要时创建目录）；未设置返回空串。
    /// 纯函数：不切换进程 CWD，供 article/live 等不依赖 CWD 的简单命令
    /// （默认输出路径已用绝对目录拼接，见 ArticleCommand/LiveCommand）把默认
    /// 输出落到该目录，避免引入进程级全局状态副作用。
    /// </summary>
    internal static string ResolveWorkDir(string workDir)
    {
        if (string.IsNullOrEmpty(workDir)) return "";
        workDir = Environment.ExpandEnvironmentVariables(workDir);
        var dir = Path.GetFullPath(workDir);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 多任务命令（sub check / watchlater）的 <c>-w/--work-dir</c> 规范化入口：把相对路径
    /// 解析为绝对路径并**只解析一次**，供逐任务循环复用（RF-89/RF-90）。
    /// 多任务命令必须先绝对化：<see cref="ChangeWorkingDir"/> 在 CLI（非 serve）下会写进程
    /// CWD，把相对 -w 留给每个任务各自解析时，第二个任务会基于上一个任务的下载目录再拼一层，
    /// 产出 &lt;root&gt;/&lt;task1&gt;/&lt;task2&gt; 嵌套。
    /// 本方法不抛异常：非法路径（<see cref="Path.GetFullPath(string)"/> 拒绝的纯空白/残留通配符
    /// 等，或目录不可创建）经 <paramref name="error"/> 返回，由调用方报错并决定退出码——路径解析
    /// 属"整批输入无效"，不能让 ArgumentException 逃出命令级异常处理器被报成
    /// "请尝试升级到最新版本后重试!"。空 -w 返回成功且 <paramref name="resolved"/> 为空串
    /// （保持既有语义：不写 WorkDir）。
    /// </summary>
    internal static bool TryResolveWorkDir(string? workDir, out string resolved, out string error)
    {
        resolved = "";
        error = "";
        if (string.IsNullOrEmpty(workDir)) return true;
        try
        {
            resolved = ResolveWorkDir(workDir);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                    or IOException or UnauthorizedAccessException
                                    or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 计算用户最终应使用的凭据（cookie/token）：显式传入优先，否则本地凭据文件。
    /// 纯函数：不写 Config（AsyncLocal 语义下写入不回流父流程），由调用方拿返回值应用。
    /// </summary>
    internal static async Task<(string cookie, string token)> LoadCredentialsAsync(
        MyOption myOption, CancellationToken cancellationToken = default)
    {
        // 用户显式传入的凭据优先于本地文件；否则从 Config.Current / BBDown.data 加载
        string cookie = !string.IsNullOrEmpty(myOption.Cookie) ? myOption.Cookie : Config.Current.Cookie;
        string token = !string.IsNullOrEmpty(myOption.AccessToken)
            ? myOption.AccessToken.Replace("access_token=", "")
            : Config.Current.Token;

        if (string.IsNullOrEmpty(cookie) && File.Exists(Path.Combine(APP_DIR, "BBDown.data")))
        {
            Logger.Log("加载本地cookie...");
            Logger.LogDebug("文件路径：{0}", Path.Combine(APP_DIR, "BBDown.data"));
            cookie = await File.ReadAllTextAsync(Path.Combine(APP_DIR, "BBDown.data"), cancellationToken);
        }
        if (string.IsNullOrEmpty(token) && File.Exists(Path.Combine(APP_DIR, "BBDownTV.data")) && myOption.UseTvApi)
        {
            Logger.Log("加载本地token...");
            Logger.LogDebug("文件路径：{0}", Path.Combine(APP_DIR, "BBDownTV.data"));
            token = (await File.ReadAllTextAsync(Path.Combine(APP_DIR, "BBDownTV.data"), cancellationToken)).Replace("access_token=", "");
        }
        if (string.IsNullOrEmpty(token) && File.Exists(Path.Combine(APP_DIR, "BBDownApp.data")) && myOption.UseAppApi)
        {
            Logger.Log("加载本地token...");
            Logger.LogDebug("文件路径：{0}", Path.Combine(APP_DIR, "BBDownApp.data"));
            token = (await File.ReadAllTextAsync(Path.Combine(APP_DIR, "BBDownApp.data"), cancellationToken)).Replace("access_token=", "");
        }

        return (cookie?.Trim() ?? "", token?.Trim() ?? "");
    }

}

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BBDown.Core.Util;

namespace BBDown;

static class BBDownAria2c
{
    public static string ARIA2C = "aria2c";

    /// <summary>aria2c 外部进程执行器。默认用系统进程实现，测试可注入假进程。</summary>
    public static IExternalProcessRunner ProcessRunner { get; set; } = new SystemProcessRunner();

    public static async Task<int> RunCommandCodeAsync(string command, string args, string? stdinContent = null, CancellationToken token = default)
    {
        // 参数仍以命令行字符串传入（aria2c 的 --aria2c-args 是整串配置），
        // 这里切分成逐项 argv 交给执行器——统一超时、整树终止与输出限流。
        var spec = new ExternalProcessSpec
        {
            FileName = command,
            Arguments = CommandLineSplitter.Split(args),
            StandardInput = stdinContent,
            ToolDisplayName = command,
            // aria2c 的进度输出走 stdout，量很大，这里不转发（保持原行为：不重定向）
            TimeoutMs = null,
        };
        return await ProcessRunner.RunAsync(spec, token);
    }

    public static async Task DownloadFileByAria2cAsync(string url, string path, string extraArgs, CancellationToken token = default)
    {
        // URL、请求头、目标路径都通过 stdin 的 input-file 传入，而非命令行参数。
        // Cookie 里含 SESSDATA 等登录凭据，放进命令行会被本机其他用户从进程列表
        // （ps / /proc）直接读到；走 stdin 则既不进 ps 也不落盘。
        // input-file 格式：URI 单独一行，其后缩进行为该 URI 的专属选项。
        var input = new StringBuilder();
        input.Append(url).Append('\n');
        if (!url.Contains("platform=android_tv_yst") && !url.Contains("platform=android"))
            input.Append("  header=Referer: https://www.bilibili.com\n");
        input.Append($"  header=User-Agent: {HTTPUtil.GetUserAgent(null)}\n");
        if (!string.IsNullOrEmpty(Core.Config.Current.Cookie))
            input.Append("  header=Cookie: ").Append(Core.Config.Current.Cookie).Append('\n');
        input.Append("  dir=").Append(Path.GetDirectoryName(path)).Append('\n');
        input.Append("  out=").Append(Path.GetFileName(path)).Append('\n');

        // 退出码必须校验：非零退出（连接失败/限速中止/磁盘满等）即便产物文件恰好存在，
        // 也可能是残缺内容被误当成成功进入混流。用户取消/超时由执行器抛
        // OperationCanceledException/TimeoutException，这里只处理正常结束后的非零码。
        // --continue=true 断点续传：中断留下的 .aria2 控制文件让下次运行从中断处续传，
        // 而非在 --allow-overwrite 下每次从头重下。
        int code = await RunCommandCodeAsync(ARIA2C,
            $"--auto-file-renaming=false --download-result=hide --allow-overwrite=true --continue=true --console-log-level=warn -x16 -s16 -j16 -k5M {extraArgs} --input-file=-",
            input.ToString(), token);
        if (code != 0)
            throw new InvalidOperationException(
                $"aria2c 下载失败，退出码 {code}（{DescribeAria2cExitCode(code)}），目标: {path}");
    }

    /// <summary>aria2c 常见退出码的中文释义，用于错误信息可读化。</summary>
    private static string DescribeAria2cExitCode(int code) => code switch
    {
        1 => "未知错误",
        2 => "超时",
        3 => "资源不存在（如 HTTP 404）",
        4 => "达到最大重试次数",
        5 => "因下载速度过慢被中止",
        6 => "网络问题",
        7 => "存在未完成的下载",
        8 => "服务器不支持断点续传",
        9 => "磁盘空间不足",
        13 => "文件已存在",
        16 => "无法创建或截断文件",
        17 => "文件 I/O 错误",
        18 => "无法创建目录",
        19 => "域名解析失败",
        22 => "HTTP 响应头异常",
        23 => "重定向过多",
        24 => "HTTP 认证失败",
        28 => "参数错误",
        29 => "服务器过载或维护中",
        _ => "未知错误",
    };
}

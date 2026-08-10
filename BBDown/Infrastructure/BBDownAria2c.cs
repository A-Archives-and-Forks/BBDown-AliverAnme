using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        input.Append("  header=User-Agent: Mozilla/5.0\n");
        if (!string.IsNullOrEmpty(Core.Config.Current.Cookie))
            input.Append("  header=Cookie: ").Append(Core.Config.Current.Cookie).Append('\n');
        input.Append("  dir=").Append(Path.GetDirectoryName(path)).Append('\n');
        input.Append("  out=").Append(Path.GetFileName(path)).Append('\n');

        await RunCommandCodeAsync(ARIA2C,
            $"--auto-file-renaming=false --download-result=hide --allow-overwrite=true --console-log-level=warn -x16 -s16 -j16 -k5M {extraArgs} --input-file=-",
            input.ToString(), token);
    }
}

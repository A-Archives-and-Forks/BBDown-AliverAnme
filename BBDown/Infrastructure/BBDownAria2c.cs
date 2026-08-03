using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown;

static class BBDownAria2c
{
    public static string ARIA2C = "aria2c";

    public static async Task<int> RunCommandCodeAsync(string command, string args, string? stdinContent = null, CancellationToken token = default)
    {
        using Process p = new();
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = false;
        p.StartInfo.RedirectStandardInput = stdinContent != null;
        p.StartInfo.FileName = command;
        p.StartInfo.Arguments = args;
        p.Start();
        if (stdinContent != null)
        {
            await p.StandardInput.WriteAsync(stdinContent);
            p.StandardInput.Close();
        }
        try
        {
            await p.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            // WaitForExitAsync(token) 只取消等待、不结束子进程：
            // 不 Kill 的话 aria2c 会变成孤儿进程继续写文件（serve 关停 / Ctrl+C 场景）
            try { p.Kill(entireProcessTree: true); } catch { /* 进程可能已自行退出 */ }
            throw;
        }
        return p.ExitCode;
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

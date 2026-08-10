using System;
using BBDown.Core;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BBDown;

public static class ExternalToolHelper
{
    /// <summary>
    /// 检测ffmpeg是否识别杜比视界
    /// </summary>
    public static bool CheckFFmpegDOVI()
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = BBDownMuxer.FFMPEG;
            process.StartInfo.Arguments = "-version";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            string info = process.StandardOutput.ReadToEnd() + Environment.NewLine + process.StandardError.ReadToEnd();
            process.WaitForExit();
            var match = BBDownUtil.LibavutilRegex().Match(info);
            if (!match.Success) return false;
            int major = Convert.ToInt32(match.Groups[1].Value);
            int minor = Convert.ToInt32(match.Groups[2].Value);
            if (major > 57 || (major == 57 && minor >= 17))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
        {
            Logger.LogDebug("检测ffmpeg版本失败: {0}", ex.Message);
        }
        return false;
    }

    public static string? FindExecutable(string name)
    {
        var fileExt = OperatingSystem.IsWindows() ? ".exe" : "";
        var searchPath = new[] { Environment.CurrentDirectory, Program.APP_DIR };
        var envPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        return searchPath.Concat(envPath).Select(p => Path.Combine(p, name + fileExt)).FirstOrDefault(File.Exists);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Diagnostics;

using BBDown.Core.Util;
using static BBDown.BBDownUtil;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    private static string? FindTool(string name)
    {
        // 1. 优先搜索系统 PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        var pathDirs = !string.IsNullOrEmpty(pathEnv)
            ? pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

        // 2. 然后搜索程序同目录。RF-57：绝不搜索当前工作目录——用户在不可信目录运行
        // --decrypt-drm 时，预置的伪造 mp4decrypt(.exe) 会被静默执行（可执行文件劫持），
        // 与 ExternalToolHelper.FindExecutable 建立的信任边界（"绝不搜索 CWD"）保持一致；
        // DRM 密钥材料 device.wvd 同理不再从 CWD 读取。--mp4decrypt-path/--wvd-path
        // 显式路径分支不受影响。
        var localDirs = new[] { AppContext.BaseDirectory };

        var allDirs = pathDirs.Concat(localDirs);

        // Windows 下追加 .exe 后缀
        var names = OperatingSystem.IsWindows()
            ? new[] { name, name + ".exe" }
            : new[] { name };

        foreach (var dir in allDirs)
        {
            foreach (var n in names)
            {
                var full = Path.Combine(dir, n);
                if (File.Exists(full)) return full;
            }
        }

        // 3. Unix/macOS 常见安装路径回退
        if (!OperatingSystem.IsWindows())
        {
            foreach (var dir in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" })
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
        }

        return null;
    }
}

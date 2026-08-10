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

        // 2. 然后搜索程序同目录及当前工作目录
        var localDirs = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory };

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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BBDown.Core.Util;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    private static readonly SemaphoreSlim fileLock = new(1, 1);

    public static async Task SaveAidToFileAsync(string aid, CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            string filePath = Path.Combine(APP_DIR, "BBDown.archives");
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            Logger.LogDebug("文件路径：{0}", filePath);
            await File.AppendAllTextAsync(filePath, $"{aid}|", cancellationToken);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task<bool> CheckAidFromFileAsync(string aid, CancellationToken cancellationToken)
    {
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            string filePath = Path.Combine(APP_DIR, "BBDown.archives");
            if (!File.Exists(filePath)) return false;
            Logger.LogDebug("文件路径：{0}", filePath);
            var text = await File.ReadAllTextAsync(filePath, cancellationToken);
            return ContainsArchiveAid(text, aid);
        }
        finally
        {
            fileLock.Release();
        }
    }

    private static bool ContainsArchiveAid(string text, string aid)
    {
        ReadOnlySpan<char> remaining = text;
        ReadOnlySpan<char> target = aid;
        while (true)
        {
            int separator = remaining.IndexOf('|');
            var candidate = separator < 0 ? remaining : remaining[..separator];
            if (candidate.SequenceEqual(target)) return true;
            if (separator < 0) return false;
            remaining = remaining[(separator + 1)..];
        }
    }

    /// <summary>
    /// 获取选中的分P列表
    /// </summary>
    /// <param name="myOption"></param>
    /// <param name="vInfo"></param>
    /// <param name="input"></param>
    /// <returns></returns>
}

using System;
using System.IO;

using BBDown.Core.Util;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    private static object fileLock = new object();
    public static void SaveAidToFile(string aid)
    {
        lock (fileLock)
        {
            string filePath = Path.Combine(APP_DIR, "BBDown.archives");
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            Logger.LogDebug("文件路径：{0}", filePath);
            File.AppendAllText(filePath, $"{aid}|");
        }
    }

    public static bool CheckAidFromFile(string aid)
    {
        lock (fileLock)
        {
            string filePath = Path.Combine(APP_DIR, "BBDown.archives");
            if (!File.Exists(filePath)) return false;
            Logger.LogDebug("文件路径：{0}", filePath);
            var text = File.ReadAllText(filePath);
            return text.Split('|').Any(item => item == aid);
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

using System;
using System.IO;

using BBDown.Core.Util;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    private static object fileLock = new object();

    /// <summary>
    /// --save-archives-to-file 的入档判定（RF-75）：存档以 aid 为粒度，多P 稿件所有分P 共享同一 aid。
    /// 下完第一个分P就入档会让余下分P 在下次运行时被 CheckAidFromFile 全跳过，因此必须等一个 aid 的
    /// 全部分P 处理完且无一失败才入档。抽为生产类型供 DownloadPagesAsync 直接使用，
    /// 同时供单元测试直接驱动（此前测试复刻了本逻辑的一份副本，生产逻辑变异仍全绿）。
    /// </summary>
    internal sealed class ArchiveTracker
    {
        private readonly Dictionary<string, int> _remaining;
        private readonly HashSet<string> _failed = new();

        public ArchiveTracker(IEnumerable<string> pageAids)
            => _remaining = pageAids.GroupBy(a => a).ToDictionary(g => g.Key, g => g.Count());

        /// <summary>已入档的 aid（按全部成功完成的顺序）。</summary>
        public List<string> Archived { get; } = new();

        /// <summary>某个 aid 的分P 被跳过（已下载过）时调用：该分P 不计入失败。</summary>
        public void OnSkipped(string aid) => _remaining[aid]--;

        /// <summary>某个 aid 的分P 处理完毕时调用，返回本次是否触发入档。</summary>
        public bool OnProcessed(string aid, bool succeeded)
        {
            _remaining[aid]--;
            if (!succeeded) _failed.Add(aid);
            if (_remaining[aid] == 0 && !_failed.Contains(aid))
            {
                Archived.Add(aid);
                return true;
            }
            return false;
        }
    }

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

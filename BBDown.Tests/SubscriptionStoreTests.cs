using BBDown;
using BBDown.Core;

namespace BBDown.Tests;

/// <summary>
/// 订阅持久化可靠性测试：损坏历史必须隔离而非静默当空/重置。
/// 由于 SubscriptionStore 的 SubFile/HistoryFile 绑定 Program.APP_DIR（程序集目录），
/// 直接调用 LoadHistory/RecordDownloaded 会读写真实安装目录。因此这里通过 internal
/// 的 IsolateCorruptHistoryFile 验证核心隔离行为，并通过单测断言损坏文件被改名隔离。
/// </summary>
public class SubscriptionStoreTests
{
    /// <summary>
    /// 回归：损坏历史文件必须被隔离为 .corrupt-时间戳（保留现场），而不是静默当空历史
    /// 或重置。此前 LoadHistory 损坏时返回空集合、RecordDownloaded 损坏时静默重置，
    /// 会让已下载内容被当作新增重新下载一遍，且丢失全部订阅历史。
    /// </summary>
    [Fact]
    public void LoadHistory_CorruptFile_IsolatesInsteadOfTreatingAsEmpty()
    {
        // 通过 reflection 拿到 HistoryFile 路径，构造一个损坏文件
        var historyFile = GetHistoryFilePath();
        var dir = Path.GetDirectoryName(historyFile)!;
        Assert.True(Directory.Exists(dir), "程序集目录应存在");

        var corruptPath = historyFile + ".test-corrupt";
        // 备份原文件（若存在）
        string? backup = null;
        if (File.Exists(historyFile))
        {
            backup = historyFile + ".test-backup";
            File.Copy(historyFile, backup, true);
        }
        try
        {
            // 写入损坏 JSON
            File.WriteAllText(historyFile, "{ this is not valid json !!!");
            var corruptCountBefore = Directory.GetFiles(dir, "BBDownSubscriptions.history.json.corrupt-*").Length;

            // LoadHistory 必须抛异常（中止订阅），而非返回空集合
            Assert.Throws<InvalidOperationException>(() => SubscriptionStore.LoadHistory("mid:1"));

            // 损坏文件已被隔离（.corrupt-* 文件数 +1）
            var corruptFiles = Directory.GetFiles(dir, "BBDownSubscriptions.history.json.corrupt-*");
            Assert.Equal(corruptCountBefore + 1, corruptFiles.Length);
            // 原文件已被移走（不再存在于原路径）
            Assert.False(File.Exists(historyFile));
        }
        finally
        {
            // 清理隔离文件与测试文件
            foreach (var f in Directory.GetFiles(dir, "BBDownSubscriptions.history.json.corrupt-*")) File.Delete(f);
            if (backup != null && File.Exists(backup))
            {
                File.Copy(backup, historyFile, true);
                File.Delete(backup);
            }
            else if (File.Exists(historyFile))
            {
                File.Delete(historyFile);
            }
        }
    }

    /// <summary>通过反射读取 SubscriptionStore 的私有 HistoryFile 路径（测试需构造损坏文件）。</summary>
    private static string GetHistoryFilePath()
    {
        var field = typeof(SubscriptionStore).GetField("HistoryFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        return (string)field!.GetValue(null)!;
    }
}

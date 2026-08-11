using BBDown;
using BBDown.Core;

namespace BBDown.Tests;

/// <summary>
/// 订阅持久化可靠性测试：损坏数据（清单/历史）必须隔离而非静默当空/重置。
/// 通过 <see cref="SubscriptionStore.StoreRoot"/> 注入独立临时目录，不污染真实安装目录。
/// </summary>
public class SubscriptionStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _origRoot;

    public SubscriptionStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "bbdown-sub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _origRoot = SubscriptionStore.StoreRoot;
        SubscriptionStore.StoreRoot = _tempRoot;
    }

    public void Dispose()
    {
        SubscriptionStore.StoreRoot = _origRoot;
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
    }

    private string HistoryFile => Path.Combine(_tempRoot, "BBDownSubscriptions.history.json");
    private string SubFile => Path.Combine(_tempRoot, "BBDownSubscriptions.json");

    /// <summary>
    /// 回归：损坏历史文件必须被隔离为 .corrupt-时间戳 并抛专用异常，而不是静默当空历史
    /// 或重置。此前 LoadHistory 损坏时返回空集合、RecordDownloaded 损坏时静默重置，
    /// 会让已下载内容被当作新增重新下载一遍，且丢失全部订阅历史。
    /// </summary>
    [Fact]
    public void LoadHistory_CorruptFile_IsolatesAndThrowsCorruptException()
    {
        // 写入损坏 JSON
        File.WriteAllText(HistoryFile, "{ this is not valid json !!!");

        // LoadHistory 必须抛专用异常（中止整个 sub check），而非返回空集合
        var ex = Assert.Throws<SubscriptionDataCorruptException>(() => SubscriptionStore.LoadHistory("mid:1"));

        // 损坏文件已被隔离（.corrupt-* 存在），原文件被移走
        Assert.Contains(".corrupt-", ex.Message);
        Assert.False(File.Exists(HistoryFile));
        Assert.Single(Directory.GetFiles(_tempRoot, "BBDownSubscriptions.history.json.corrupt-*"));
    }

    /// <summary>
    /// 回归：主订阅清单损坏必须隔离并抛专用异常，不能返回空列表。
    /// 此前 Load 损坏时返回空列表 → sub list/check 显示"没有订阅"并成功，
    /// 下一次 sub add/remove 会用空列表覆盖原文件（与历史文件问题同类）。
    /// </summary>
    [Fact]
    public void Load_CorruptSubFile_IsolatesAndThrowsCorruptException()
    {
        // 先写入合法订阅清单，再改成损坏 JSON
        SubscriptionStore.Add("mid:1", "UP主1");
        Assert.Single(SubscriptionStore.Load());
        File.WriteAllText(SubFile, "{ broken !!!");

        // Load 必须抛专用异常而非返回空列表
        var ex = Assert.Throws<SubscriptionDataCorruptException>(() => SubscriptionStore.Load());
        Assert.Contains(".corrupt-", ex.Message);
        Assert.False(File.Exists(SubFile));
        Assert.Single(Directory.GetFiles(_tempRoot, "BBDownSubscriptions.json.corrupt-*"));
    }

    /// <summary>
    /// 回归：RecordDownloaded 遇到损坏历史也必须抛专用异常（而非静默重置），
    /// 否则已下载内容会在下次检查时被当作新增重新下载，且丢失全部历史。
    /// </summary>
    [Fact]
    public void RecordDownloaded_CorruptHistory_IsolatesAndThrowsCorruptException()
    {
        File.WriteAllText(HistoryFile, "{ broken !!!");

        var ex = Assert.Throws<SubscriptionDataCorruptException>(() => SubscriptionStore.RecordDownloaded("mid:1", "170001"));
        Assert.Contains(".corrupt-", ex.Message);
        Assert.False(File.Exists(HistoryFile));
    }
}

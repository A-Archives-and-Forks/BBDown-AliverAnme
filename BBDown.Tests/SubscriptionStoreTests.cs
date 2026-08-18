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
    /// 回归：目标字段存在但不是数组（如 {"mid:1":"broken"}）必须按损坏处理并抛专用异常，
    /// 不能静默当空历史——否则该订阅全部内容会被当作新增重新下载一遍。
    /// </summary>
    [Fact]
    public void LoadHistory_TargetFieldNotArray_IsolatesAndThrowsCorruptException()
    {
        // 合法 JSON 但结构错误：目标字段是字符串而非数组
        File.WriteAllText(HistoryFile, """{"mid:1":"broken"}""");

        var ex = Assert.Throws<SubscriptionDataCorruptException>(() => SubscriptionStore.LoadHistory("mid:1"));
        Assert.Contains(".corrupt-", ex.Message);
        Assert.False(File.Exists(HistoryFile));
    }

    /// <summary>
    /// 回归：历史数组包含非字符串元素（数字/对象）也必须按损坏处理并抛专用异常。
    /// </summary>
    [Fact]
    public void LoadHistory_ArrayWithNonStringElement_IsolatesAndThrowsCorruptException()
    {
        // 数组内含数字元素：结构不符
        File.WriteAllText(HistoryFile, """{"mid:1":[12345, "67890"]}""");

        var ex = Assert.Throws<SubscriptionDataCorruptException>(() => SubscriptionStore.LoadHistory("mid:1"));
        Assert.Contains(".corrupt-", ex.Message);
        Assert.False(File.Exists(HistoryFile));
    }

    /// <summary>目标字段不存在（该订阅从未下载过）是合法场景，返回空集合并隔离历史文件损坏除外。</summary>
    [Fact]
    public void LoadHistory_TargetNotPresent_ReturnsEmpty()
    {
        File.WriteAllText(HistoryFile, """{"mid:2":["170001"]}""");
        Assert.Empty(SubscriptionStore.LoadHistory("mid:1"));
        Assert.True(File.Exists(HistoryFile)); // 未损坏，不隔离
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

    [Fact]
    public void Add_DuplicateTarget_IsIdempotent()
    {
        // F12：重复 Add 同一 target 不得重复登记——否则 sub list 出现重复项、
        // remove 一次删不干净。
        SubscriptionStore.Add("mid:1", "UP");
        SubscriptionStore.Add("mid:1", "UP");
        SubscriptionStore.Add("mid:1", "UP");
        Assert.Single(SubscriptionStore.Load());
    }

    [Fact]
    public void Remove_NonexistentTarget_DoesNotThrow()
    {
        // F12：Remove 不存在的订阅不得抛异常/留空残余（幂等）
        SubscriptionStore.Remove("mid:404");
        Assert.Empty(SubscriptionStore.Load());
    }

    [Fact]
    public void Remove_ExistentTarget_RemovesOnlyThat()
    {
        // F12：Remove 只删除目标，不影响其它订阅
        SubscriptionStore.Add("mid:1", "UP1");
        SubscriptionStore.Add("mid:2", "UP2");
        SubscriptionStore.Remove("mid:1");
        var subs = SubscriptionStore.Load();
        Assert.Single(subs);
        Assert.Equal("mid:2", subs[0].Target);
    }

    [Fact]
    public void RecordDownloaded_SameAid_IsIdempotent()
    {
        // F12：同 aid 重复 RecordDownloaded 不得历史膨胀/重复——否则每次 sub check
        // 都会把已下载的内容当作新增重新下载一遍。
        SubscriptionStore.RecordDownloaded("mid:1", "170001");
        SubscriptionStore.RecordDownloaded("mid:1", "170001");
        SubscriptionStore.RecordDownloaded("mid:1", "170001");
        Assert.Single(SubscriptionStore.LoadHistory("mid:1"));
    }

    [Fact]
    public void RecordDownloaded_MovesExistingAid_KeepsSingleEntry()
    {
        // F12: RecordDownloaded 对已存在 aid 采用 Remove+Add（最近优先语义）：
        // active list 里同 aid 只能出现一次，且重复记录后仍为单条。
        SubscriptionStore.RecordDownloaded("mid:1", "a1");
        SubscriptionStore.RecordDownloaded("mid:1", "b2");
        SubscriptionStore.RecordDownloaded("mid:1", "a1"); // 同 aid 重复
        var hist = SubscriptionStore.LoadHistory("mid:1");
        Assert.Equal(2, hist.Count); // a1 + b2，不重复
        Assert.Contains("a1", hist);
        Assert.Contains("b2", hist);
    }
}

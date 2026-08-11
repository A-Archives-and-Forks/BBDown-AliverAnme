using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BBDown.Core;

namespace BBDown;

public record Subscription(string Target, string Name, long AddedAt);

/// <summary>
/// 订阅持久化数据损坏（订阅清单或历史文件 JSON 解析失败）时抛出。
/// 与普通 I/O/网络失败不同：数据损坏意味着"哪些已下载过"的信息不可信，
/// 继续按空历史/空清单执行会触发大规模重复下载或覆盖，必须中止整个流程。
/// SubCheck 等调用方应捕获本异常并终止整批，而非按单订阅失败继续。
/// </summary>
public sealed class SubscriptionDataCorruptException : InvalidOperationException
{
    public SubscriptionDataCorruptException(string message, Exception inner) : base(message, inner) { }
}

[JsonSerializable(typeof(List<Subscription>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
internal partial class SubscriptionJsonContext : JsonSerializerContext
{
}

/// <summary>
/// 订阅清单与"已下载"历史的持久化。
/// 订阅文件与历史文件都放在程序目录（APP_DIR）下，与凭据文件同级。
/// </summary>
public static class SubscriptionStore
{
    /// <summary>存储根目录（程序目录）。internal 供测试注入临时目录，避免污染真实安装目录。</summary>
    internal static string StoreRoot = Program.APP_DIR;

    private static string SubFile => Path.Combine(StoreRoot, "BBDownSubscriptions.json");
    private static string HistoryFile => Path.Combine(StoreRoot, "BBDownSubscriptions.history.json");

    /// <summary>
    /// 把损坏的持久化文件隔离为 .corrupt-时间戳 并返回隔离路径；返回 null 表示隔离失败
    /// （文件被占用等，保留原位）。internal 供测试验证"损坏数据不静默当空/重置"。
    /// </summary>
    internal static string? IsolateCorruptFile(string path)
    {
        string corrupt = path + $".corrupt-{DateTimeOffset.Now.ToUnixTimeSeconds()}";
        try
        {
            if (File.Exists(path)) File.Move(path, corrupt, true);
            return corrupt;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // 走 JsonTypeInfo 的序列化：AOT 裁剪安全，且可自定义缩进与不转义非 ASCII
    private static string ToJson<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            JsonSerializer.Serialize(writer, value, typeInfo);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // 单进程内的读-改-写串行化：Add/Remove/RecordDownloaded 在 _ioLock 内完成
    // "读文件→内存修改→整体写回"，避免多写者并发时后写者的快照覆盖先写者的修改（丢失更新）。
    private static readonly object _ioLock = new();

    /// <summary>原子替换写入（temp + rename）：避免进程被杀/磁盘满留下截断 JSON，
    /// 否则下次 Load 会把损坏文件静默当作"无订阅"。
    /// 临时文件名带唯一后缀：固定 .tmp 名会让并发写者互相踩踏（FileShare.None 抛 IOException）。</summary>
    private static void AtomicWrite(string path, string content)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, true);
    }

    private static void WriteSubs(List<Subscription> subs)
    {
        // 写入失败必须向上传播：调用方据此返回非零退出码/失败状态。
        // 此前吞掉异常后调用方仍打印"已添加订阅"，用户以为成功但文件没写入。
        lock (_ioLock)
        {
            AtomicWrite(SubFile, ToJson(subs, SubscriptionJsonContext.Default.ListSubscription));
        }
    }

    public static List<Subscription> Load()
    {
        try
        {
            if (!File.Exists(SubFile)) return [];
            return JsonSerializer.Deserialize(File.ReadAllText(SubFile), SubscriptionJsonContext.Default.ListSubscription) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 订阅清单损坏不能静默当空：否则 sub list/check 显示"没有订阅"并成功，
            // 下一次 sub add/remove 会用空列表覆盖原文件。隔离为 .corrupt-时间戳并抛
            // 专用异常，调用方据此中止流程。
            string? corrupt = IsolateCorruptFile(SubFile);
            Logger.LogError($"订阅清单损坏（{ex.Message}），已隔离为 {corrupt ?? SubFile}，中止操作以避免覆盖订阅");
            throw new SubscriptionDataCorruptException($"订阅清单损坏，已隔离为 {corrupt ?? SubFile}，请检查后恢复", ex);
        }
    }

    public static void Add(string target, string? name)
    {
        lock (_ioLock)
        {
            var subs = Load();
            if (subs.Any(s => s.Target == target))
            {
                Logger.LogWarn($"已存在订阅: {target}");
                return;
            }
            subs.Add(new Subscription(target, string.IsNullOrWhiteSpace(name) ? target : name!,
                DateTimeOffset.Now.ToUnixTimeSeconds()));
            WriteSubs(subs);
            Logger.Log($"已添加订阅: {target}");
        }
    }

    public static void Remove(string target)
    {
        lock (_ioLock)
        {
            var subs = Load();
            var removed = subs.RemoveAll(s => s.Target == target);
            WriteSubs(subs);
            Logger.Log(removed > 0 ? $"已移除订阅: {target}" : $"未找到订阅: {target}");
        }
    }

    /// <summary>某个订阅已成功下载过的 avid 集合。
    /// 历史文件损坏时隔离为 .corrupt-时间戳 并抛异常（调用方应中止该订阅，不能当空历史
    /// 继续——否则已下载内容会被当作新增重新下载一遍）。</summary>
    public static HashSet<string> LoadHistory(string target)
    {
        try
        {
            if (!File.Exists(HistoryFile)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(HistoryFile));
            if (!doc.RootElement.TryGetProperty(target, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];
            return arr.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => s is not null)
                .Select(s => s!)
                .ToHashSet();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 损坏历史隔离而非当空历史/静默重置：保留现场供排查，同时以专用异常中止。
            // 静默当空历史会让已下载内容被当作新增重新下载；静默重置会丢失所有订阅的历史。
            // 调用方必须捕获 SubscriptionDataCorruptException 终止整个 sub check——
            // 若按普通订阅失败继续，后续订阅会因历史文件已不存在而把全部内容当新增重新下载。
            string? corrupt = IsolateCorruptFile(HistoryFile);
            Logger.LogError($"订阅历史文件损坏（{ex.Message}），已隔离为 {corrupt ?? HistoryFile}，中止当前订阅以避免重复下载");
            throw new SubscriptionDataCorruptException($"订阅历史文件损坏，已隔离为 {corrupt ?? HistoryFile}，请检查后恢复", ex);
        }
    }

    public static void RecordDownloaded(string target, string aid)
    {
        lock (_ioLock)
        {
            var hist = new Dictionary<string, List<string>>();
            if (File.Exists(HistoryFile))
            {
                try
                {
                    hist = JsonSerializer.Deserialize(File.ReadAllText(HistoryFile), SubscriptionJsonContext.Default.DictionaryStringListString) ?? new();
                }
                catch (JsonException ex)
                {
                    // 历史文件损坏：隔离而非静默重置。静默重置会让已下载过的内容在下次
                    // 检查时被当作新增重新下载一遍，且丢失全部订阅的历史。
                    string? corrupt = IsolateCorruptFile(HistoryFile);
                    Logger.LogError($"订阅历史文件损坏（{ex.Message}），已隔离为 {corrupt ?? HistoryFile}，中止记录以避免覆盖历史");
                    throw new SubscriptionDataCorruptException($"订阅历史文件损坏，已隔离为 {corrupt ?? HistoryFile}，请检查后恢复", ex);
                }
            }

            if (!hist.TryGetValue(target, out var list)) { list = []; hist[target] = list; }
            if (!list.Contains(aid)) list.Add(aid);
            // 写入失败向上传播：调用方据此让 sub check 返回非零退出码，
            // 否则下次运行会因历史未记录而重复下载已下载内容。
            AtomicWrite(HistoryFile, ToJson(hist, SubscriptionJsonContext.Default.DictionaryStringListString));
        }
    }
}

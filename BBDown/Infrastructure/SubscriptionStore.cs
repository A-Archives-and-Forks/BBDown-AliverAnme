using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BBDown.Core;

namespace BBDown;

public record Subscription(string Target, string Name, long AddedAt);

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
    private static readonly string SubFile = Path.Combine(Program.APP_DIR, "BBDownSubscriptions.json");
    private static readonly string HistoryFile = Path.Combine(Program.APP_DIR, "BBDownSubscriptions.history.json");

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

    public static List<Subscription> Load()
    {
        try
        {
            if (!File.Exists(SubFile)) return [];
            return JsonSerializer.Deserialize(File.ReadAllText(SubFile), SubscriptionJsonContext.Default.ListSubscription) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Logger.LogWarn($"读取订阅文件失败: {ex.Message}");
            return [];
        }
    }

    public static void Add(string target, string? name)
    {
        var subs = Load();
        if (subs.Any(s => s.Target == target))
        {
            Logger.LogWarn($"已存在订阅: {target}");
            return;
        }
        subs.Add(new Subscription(target, string.IsNullOrWhiteSpace(name) ? target : name!,
            DateTimeOffset.Now.ToUnixTimeSeconds()));
        File.WriteAllText(SubFile, ToJson(subs, SubscriptionJsonContext.Default.ListSubscription));
        Logger.Log($"已添加订阅: {target}");
    }

    public static void Remove(string target)
    {
        var subs = Load();
        var removed = subs.RemoveAll(s => s.Target == target);
        File.WriteAllText(SubFile, ToJson(subs, SubscriptionJsonContext.Default.ListSubscription));
        Logger.Log(removed > 0 ? $"已移除订阅: {target}" : $"未找到订阅: {target}");
    }

    /// <summary>某个订阅已成功下载过的 avid 集合。</summary>
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
            return [];
        }
    }

    public static void RecordDownloaded(string target, string aid)
    {
        var hist = new Dictionary<string, List<string>>();
        try
        {
            if (File.Exists(HistoryFile))
                hist = JsonSerializer.Deserialize(File.ReadAllText(HistoryFile), SubscriptionJsonContext.Default.DictionaryStringListString) ?? new();
        }
        catch (JsonException) { /* 文件损坏时重置历史 */ }

        if (!hist.TryGetValue(target, out var list)) { list = []; hist[target] = list; }
        if (!list.Contains(aid)) list.Add(aid);
        File.WriteAllText(HistoryFile, ToJson(hist, SubscriptionJsonContext.Default.DictionaryStringListString));
    }
}

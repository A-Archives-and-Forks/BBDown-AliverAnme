using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BBDown.Core.Util;

namespace BBDown;

public record CommentItem(string User, long Time, long Likes, string Content);

internal sealed record CommentExport(
    [property: JsonPropertyName("user")] string User,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("likes")] long Likes,
    [property: JsonPropertyName("content")] string Content);

[JsonSerializable(typeof(List<CommentExport>))]
internal partial class CommentJsonContext : JsonSerializerContext
{
}

/// <summary>
/// B站视频评论抓取。评论 API 的 oid 用 avid，type=1 表示视频评论。
/// </summary>
public static class CommentUtil
{
    /// <summary>
    /// 分页抓取视频评论，直到某页为空或达到 maxPages 上限。
    /// 返回的 <see cref="CommentPage"/> 携带是否因达到上限而被截断的标志：
    /// 默认每页 20 条、最多 20 页（400 条），若评论总数超过该量级，界面应提示结果不完整。
    /// </summary>
    public static async Task<CommentPage> FetchAsync(long aid, int maxPages = 20, CancellationToken token = default)
    {
        var result = new List<CommentItem>();
        bool truncated = false;
        for (int pn = 1; pn <= maxPages; pn++)
        {
            string api = $"https://api.bilibili.com/x/v2/reply?type=1&oid={aid}&sort=0&ps=20&pn={pn}";
            string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int code = root.GetInt32Safe("code");
            if (code != 0)
                throw new InvalidOperationException($"获取评论失败(code={code}): {root.GetValueAsStringSafe("message")}");
            var dataElem = root.TryGetPropertySafe("data");
            if (dataElem is null) break;
            var replies = dataElem.Value.EnumerateArraySafe("replies");
            if (!replies.Any()) break;
            foreach (var r in replies)
            {
                var member = r.TryGetPropertySafe("member");
                var content = r.TryGetPropertySafe("content")?.GetValueAsStringSafe("message") ?? "";
                result.Add(new CommentItem(
                    member?.GetValueAsStringSafe("uname") ?? "",
                    r.GetInt64Safe("ctime"),
                    r.GetInt64Safe("like"),
                    content.Trim()));
            }
            // 已达到分页上限但当前页仍有回复：说明还有更多评论未抓取
            if (pn == maxPages) truncated = true;
        }
        return new CommentPage(result, truncated);
    }

    /// <summary>评论抓取结果：列表 + 是否因达到分页上限而被截断（存在未抓取的更多评论）。</summary>
    public record CommentPage(List<CommentItem> Items, bool Truncated);

    /// <summary>导出评论为带缩进的 JSON 文件（保留中文原文，AOT 裁剪安全）。</summary>
    /// <remarks>
    /// 输出目录可能尚不存在：评论在混流（MuxAV 内才创建输出目录）之前保存，
    /// 目标父目录尚未创建时 File.WriteAllBytesAsync 抛 DirectoryNotFoundException，
    /// 调用方会把异常降级为警告导致评论静默丢失。这里先确保父目录存在，
    /// 保存函数自包含、调用方无需记忆建目录。
    /// </remarks>
    public static async Task SaveToJsonAsync(List<CommentItem> comments, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var payload = comments.Select(c => new CommentExport(
            c.User,
            DateTimeOffset.FromUnixTimeSeconds(c.Time).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            c.Likes,
            c.Content)).ToList();

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            JsonSerializer.Serialize(writer, payload, CommentJsonContext.Default.ListCommentExport);
        }
        await File.WriteAllBytesAsync(path, ms.ToArray());
    }
}

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
    /// </summary>
    public static async Task<List<CommentItem>> FetchAsync(long aid, int maxPages = 20, CancellationToken token = default)
    {
        var result = new List<CommentItem>();
        for (int pn = 1; pn <= maxPages; pn++)
        {
            string api = $"https://api.bilibili.com/x/v2/reply?type=1&oid={aid}&sort=0&ps=20&pn={pn}";
            string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int code = root.GetPropertySafe("code").GetInt32();
            if (code != 0)
                throw new InvalidOperationException($"获取评论失败(code={code}): {root.GetValueAsStringSafe("message")}");
            var data = root.GetPropertySafe("data");
            var replies = data.EnumerateArraySafe("replies");
            if (!replies.Any()) break;
            foreach (var r in replies)
            {
                var member = r.GetPropertySafe("member");
                var content = r.GetPropertySafe("content").GetValueAsStringSafe("message");
                result.Add(new CommentItem(
                    member.GetValueAsStringSafe("uname"),
                    r.GetInt64Safe("ctime"),
                    r.GetInt64Safe("like"),
                    content.Trim()));
            }
        }
        return result;
    }

    /// <summary>导出评论为带缩进的 JSON 文件（保留中文原文，AOT 裁剪安全）。</summary>
    public static async Task SaveToJsonAsync(List<CommentItem> comments, string path)
    {
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

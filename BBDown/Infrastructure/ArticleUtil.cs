using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BBDown.Core.Util;

namespace BBDown;

public record ArticleInfo(string Title, string Author, long PubTime, string MarkdownContent);

/// <summary>
/// B站专栏文章抓取。输入支持 cv{id} 或完整链接（bilibili.com/read/cv{id}）。
/// </summary>
public static partial class ArticleUtil
{
    /// <summary>从输入中提取 cv 编号：cv123、CV123、https://.../read/cv123 均支持。</summary>
    public static string ExtractCvId(string input)
    {
        var m = CvRegex().Match(input);
        if (!m.Success)
            throw new ArgumentException($"输入有误：无法识别的专栏 ID，当前值: '{input}'");
        return m.Groups[1].Value;
    }

    public static async Task<ArticleInfo> FetchAsync(string cvId, CancellationToken token = default)
    {
        string api = $"https://api.bilibili.com/x/article/view?id={cvId}";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int code = root.GetInt32Safe("code");
        if (code != 0)
            throw new InvalidOperationException($"专栏获取失败(code={code}): {root.GetValueAsStringSafe("message")}");

        var data = root.GetPropertySafe("data");
        string title = data.GetValueAsStringSafe("title");
        if (title == "") title = $"专栏{cvId}";
        string author = data.GetValueAsStringSafe("author_name");
        long pubTime = data.GetInt64Safe("publish_time");
        string content = data.GetValueAsStringSafe("content");
        if (content == "") content = "（正文为空，可能为会员专享或已删除）";
        return new ArticleInfo(title, author, pubTime, content);
    }

    /// <summary>导出为 Markdown：标题头 + 作者/时间 + 正文。</summary>
    public static async Task SaveAsMarkdownAsync(ArticleInfo article, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {article.Title}");
        sb.AppendLine();
        sb.AppendLine($"> 作者: {article.Author}  |  发布时间: {DateTimeOffset.FromUnixTimeSeconds(article.PubTime).LocalDateTime:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine(article.MarkdownContent);
        sb.AppendLine();
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
    }

    [GeneratedRegex("cv(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CvRegex();
}

using System.Globalization;
using BBDown;

namespace BBDown.Tests;

public class ArticleUtilTests
{
    [Theory]
    [InlineData("cv123", "123")]
    [InlineData("CV456", "456")]
    [InlineData("https://www.bilibili.com/read/cv789", "789")]
    public void ExtractCvId_ParsesVariousInputs(string input, string expected)
        => Assert.Equal(expected, ArticleUtil.ExtractCvId(input));

    [Theory]
    [InlineData("")]
    [InlineData("av123")]
    [InlineData("not-a-cv")]
    public void ExtractCvId_Invalid_Throws(string input)
        => Assert.Throws<ArgumentException>(() => ArticleUtil.ExtractCvId(input));

    [Fact]
    public async Task SaveAsMarkdownAsync_WritesHeaderAndContent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-art-{Guid.NewGuid():N}.md");
        try
        {
            var article = new ArticleInfo("测试标题", "作者君", 1700000000, "这是正文。");
            await ArticleUtil.SaveAsMarkdownAsync(article, path);
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("# 测试标题", text);
            Assert.Contains("作者: 作者君", text);
            Assert.Contains("这是正文。", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// 专栏 Markdown 是数据文件导出，发布时间必须文化无关（第 13 轮 Info①）：
    /// fi-FI 下 `:` 被替换为本地时间分隔符，导出产物跨机漂移。
    /// </summary>
    [Fact]
    public async Task SaveAsMarkdownAsync_PublishTimeIsCultureInvariant()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-art-cult-{Guid.NewGuid():N}.md");
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            var article = new ArticleInfo("标题", "作者", 1700000000, "正文");
            await ArticleUtil.SaveAsMarkdownAsync(article, path);
            var text = await File.ReadAllTextAsync(path);
            var m = System.Text.RegularExpressions.Regex.Match(text, @"发布时间: (\S+ \S+)");
            Assert.True(m.Success, "应包含发布时间行");
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(m.Groups[1].Value, @"\d{2}:\d{2}$"),
                $"时间应文化无关（冒号分隔），实际: {m.Groups[1].Value}");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

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
}

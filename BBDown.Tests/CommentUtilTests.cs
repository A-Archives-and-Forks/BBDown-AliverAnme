using BBDown;

namespace BBDown.Tests;

public class CommentUtilTests
{
    [Fact]
    public async Task SaveToJsonAsync_WritesUnicodeContent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-cmt-{Guid.NewGuid():N}.json");
        try
        {
            var comments = new List<CommentItem>
            {
                new("UP主", 1700000000, 42, "很好看的视频😀"),
            };
            await CommentUtil.SaveToJsonAsync(comments, path);
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("很好看的视频", text); // 中文原文保留
            Assert.Contains(@"\uD83D\uDE00", text); // emoji 以合法 JSON 转义保留
            Assert.Contains("\"user\": \"UP主\"", text);
            Assert.Contains("\"likes\": 42", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

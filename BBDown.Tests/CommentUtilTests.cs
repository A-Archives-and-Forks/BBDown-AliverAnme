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

    /// <summary>
    /// 评论保存目录自包含回归：评论在混流（MuxAV 才创建输出目录）之前保存，
    /// 目标父目录尚未创建时 SaveToJsonAsync 必须自行创建目录，
    /// 否则 DirectoryNotFoundException 被调用方降级为警告导致评论静默丢失（Bug B）。
    /// </summary>
    [Fact]
    public async Task SaveToJsonAsync_CreatesMissingParentDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"bbdown-cmt-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(tempRoot, "nested", "out", "comments.json");
        try
        {
            var comments = new List<CommentItem>
            {
                new("UP主", 1700000000, 7, "内容"),
            };
            await CommentUtil.SaveToJsonAsync(comments, path);
            Assert.True(File.Exists(path), "父目录不存在时也应成功保存评论文件");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    /// <summary>
    /// 截断判定回归：末页不满页（如 385 条评论的第 20 页只有 5 条）本身就是末页，
    /// 不得误标 truncated——否则 UI 对完整数据警告"结果不完整"。
    /// </summary>
    [Fact]
    public void IsTruncated_PartialLastPage_IsNotTruncated()
    {
        // 385 条 / 每页 20 条：第 20 页仅 5 条，是自然末页
        Assert.False(CommentUtil.IsTruncated(pageNumber: 20, maxPages: 20, lastPageItemCount: 5, pageSize: 20));
    }

    [Fact]
    public void IsTruncated_FullLastPage_MayHaveMoreComments()
    {
        // 第 20 页仍是满页：无法确认后面没有更多，必须标记截断
        Assert.True(CommentUtil.IsTruncated(pageNumber: 20, maxPages: 20, lastPageItemCount: 20, pageSize: 20));
    }

    [Fact]
    public void IsTruncated_BeforeMaxPage_NeverTruncated()
    {
        Assert.False(CommentUtil.IsTruncated(pageNumber: 7, maxPages: 20, lastPageItemCount: 20, pageSize: 20));
    }
}

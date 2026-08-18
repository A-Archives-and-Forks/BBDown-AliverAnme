namespace BBDown.Tests;

/// <summary>
/// 保存路径模板决策测试（C4 观察点修复）：多P命名模板的判定应基于
/// 【实际下载的分P数】而非视频总P数——-p 单选 1 集时即使视频有多个分P，
/// 也应走单P模板（默认 &lt;videoTitle&gt; 或 -F 自定义），产物不再带 [P##] 前缀。
/// </summary>
public class PathFormatTests
{
    [Theory]
    // 单选 1P、非番剧 → 单P 默认模板（无 [P##]）
    [InlineData("", "", 1, false, "<videoTitle>")]
    // 下载 3P → 多P 默认模板
    [InlineData("", "", 3, false, "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>")]
    // 单选 1P 但番剧未完结 → 强制多P（每P自成文件）
    [InlineData("", "", 1, true, "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>")]
    // -F 自定义单P模板：单选 1P 时生效（C4 原缺陷场景）
    [InlineData("<Fpat>", "", 1, false, "<Fpat>")]
    // -M 自定义多P模板：下载多P时生效
    [InlineData("", "<Mpat>", 3, false, "<Mpat>")]
    // 番剧未完结 + 双模板都给了 → 用多P模板
    [InlineData("<Fpat>", "<Mpat>", 1, true, "<Mpat>")]
    // 单选 1P + 双模板都给了 → 用单P模板
    [InlineData("<Fpat>", "<Mpat>", 1, false, "<Fpat>")]
    public void ResolveSavePathFormat_SelectsTemplateBasedOnActualPageCount(
        string filePattern, string multiFilePattern, int actualPageCount, bool useMultiWhenSingle, string expected)
    {
        Assert.Equal(expected,
            Program.ResolveSavePathFormat(filePattern, multiFilePattern, actualPageCount, useMultiWhenSingle));
    }
}

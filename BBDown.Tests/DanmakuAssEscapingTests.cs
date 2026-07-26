using BBDown.Core;

namespace BBDown.Tests;

/// <summary>
/// 弹幕内容来自其他用户，会被原样写进 ASS 的 Dialogue 行。
/// ASS 用 {...} 表示样式覆盖标签、\N 表示换行，这些字符必须先中和，
/// 否则一条弹幕可以改变其他弹幕的渲染，或直接破坏文件结构。
/// </summary>
public class DanmakuAssEscapingTests
{
    private static DanmakuUtil.DanmakuItem Make(string content, double second = 1.0)
        => new(["" + second, "1", "25", "16777215", "1600000000"], content);

    private static async Task<string> RenderAsync(params DanmakuUtil.DanmakuItem[] items)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-ass-{Guid.NewGuid():N}.ass");
        try
        {
            await DanmakuUtil.SaveAsAssAsync(items, path);
            return await File.ReadAllTextAsync(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task StyleOverrideBraces_AreNeutralized()
    {
        var ass = await RenderAsync(Make(@"{\c&HFF0000&}被注入的红色"));

        var dialogue = ass.Split('\n').Single(l => l.StartsWith("Dialogue:"));
        // 正文部分位于第 10 个逗号之后
        var text = dialogue.Split(',', 10)[9];

        // 正文里出现的样式块只能是 BBDown 自己生成的开头那一段
        var braceBlocks = text.Count(c => c == '{');
        Assert.Equal(1, braceBlocks);
        Assert.DoesNotContain(@"{\c&HFF0000&}", text);
    }

    [Fact]
    public async Task ClosingBraceCannotTerminateGeneratedTagBlock()
    {
        // 以 } 开头可提前闭合 BBDown 生成的标签块，使其后内容被当作标签解析
        var ass = await RenderAsync(Make(@"}{\an5\fs100}放大"));

        var dialogue = ass.Split('\n').Single(l => l.StartsWith("Dialogue:"));
        var text = dialogue.Split(',', 10)[9];

        Assert.Equal(1, text.Count(c => c == '{'));
        Assert.Equal(1, text.Count(c => c == '}'));
    }

    [Fact]
    public async Task NewlinesInContent_DoNotBreakLineStructure()
    {
        var ass = await RenderAsync(Make("第一行\n第二行\r\n第三行"));

        // [Events] 段内除表头外的每一行都必须是一条 Dialogue，
        // 内容里的换行若原样写出，会让后半段变成解析器看不懂的孤立行
        var lines = ass.Replace("\r\n", "\n").Split('\n');
        var eventsStart = Array.IndexOf(lines, "[Events]");
        Assert.True(eventsStart >= 0, "未找到 [Events] 段");

        var strayLines = lines
            .Skip(eventsStart + 1)
            .Where(l => !string.IsNullOrEmpty(l))
            .Where(l => !l.StartsWith("Format:") && !l.StartsWith("Dialogue:"))
            .ToList();

        Assert.True(strayLines.Count == 0,
            $"内容换行泄漏成了独立行: {string.Join(" | ", strayLines)}");
    }

    [Fact]
    public async Task NormalContent_IsPreservedVerbatim()
    {
        const string plain = "普通弹幕 abc 123 哈哈哈";
        var ass = await RenderAsync(Make(plain));

        Assert.Contains(plain, ass);
    }

    [Fact]
    public async Task GeneratedStyleTag_StillAppliesToEachLine()
    {
        var ass = await RenderAsync(Make("内容"));

        var dialogue = ass.Split('\n').Single(l => l.StartsWith("Dialogue:"));
        // 移动弹幕依赖 \move 标签，转义不能把它一并破坏
        Assert.Contains(@"\move(", dialogue);
    }
}

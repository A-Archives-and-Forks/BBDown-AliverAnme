using BBDown.Commands;

namespace BBDown.Tests;

/// <summary>
/// sub check --per-sub-dir 的订阅目录名解析：显示名经 RF-18 同款净化后作为
/// work-dir 下的目录段，净化结果冲突（含大小写差异，对齐 Windows 文件系统）
/// 时追加 -2/-3 序号，保证多订阅不会互相覆盖、序号跨 run 稳定。
/// </summary>
public class SubCheckDirNameTests
{
    private static HashSet<string> NewSlots() => new(StringComparer.OrdinalIgnoreCase);

    private static string Resolve(string name, string target, HashSet<string>? slots = null)
        => SubCheckCommand.ResolveSubDirName(new Subscription(target, name, 0), slots ?? NewSlots());

    [Fact]
    public void PlainName_PassesThrough()
    {
        Assert.Equal("何同学", Resolve("何同学", "mid:163637592"));
    }

    [Fact]
    public void NameWithPathSeparators_IsSanitized()
    {
        // 订阅名可含 '/' '\'（用户 --name 或 target 回退为 URL），必须是单一目录段
        var dir = Resolve("a/b\\c", "mid:1");
        Assert.DoesNotContain('/', dir);
        Assert.DoesNotContain('\\', dir);
    }

    [Fact]
    public void WindowsIllegalChars_AreSanitized()
    {
        // 'mid:163637592' 作订阅名（未指定 --name 时 target 回退）：':' 在 Windows 非法
        var dir = Resolve("mid:163637592", "mid:163637592");
        Assert.DoesNotContain(':', dir);
        Assert.NotEqual(".", dir);
        Assert.NotEqual("..", dir);
    }

    [Fact]
    public void EmptyName_FallsBackToTarget()
    {
        var dir = Resolve("", "mid:163637592");
        Assert.NotEqual("", dir);
        Assert.DoesNotContain(':', dir);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    public void ReservedDeviceName_IsPrefixed(string name)
    {
        // Windows 保留设备名直接建目录会失败
        var dir = Resolve(name, "mid:1");
        Assert.NotEqual(name, dir);
    }

    [Fact]
    public void CollidingNames_GetStableOrdinalSuffixes()
    {
        var slots = NewSlots();
        // 'a:b' 与 'a?b' 净化后同为 'a_b'：第二个必须带序号，不得覆盖
        Assert.Equal("a_b", Resolve("a:b", "mid:1", slots));
        Assert.Equal("a_b-2", Resolve("a?b", "mid:2", slots));
        Assert.Equal("a_b-3", Resolve("a_b", "mid:3", slots));
    }

    [Fact]
    public void CaseInsensitiveCollision_TreatedAsSameSlot()
    {
        // OrdinalIgnoreCase 对齐 Windows：'Up' 与 'up' 是同一目录
        var slots = NewSlots();
        Assert.Equal("Up", Resolve("Up", "mid:1", slots));
        Assert.Equal("up-2", Resolve("up", "mid:2", slots));
    }

    [Fact]
    public void SlotsAreConsumedEvenWithoutNewContent_SoSuffixesStayStable()
    {
        // 调用方对每个订阅（无论有无新增）都占槽：这里锁住"占槽即占号"的契约
        var slots = NewSlots();
        Resolve("同名", "mid:1", slots);
        Resolve("同名", "mid:2", slots);
        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public void PerSubDir_DefaultsToOff()
    {
        // 向后兼容：不传 --per-sub-dir 必须保持既有平铺行为
        Assert.False(new SubCheckSettings().PerSubDir);
    }
}

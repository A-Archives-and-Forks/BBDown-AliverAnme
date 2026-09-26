using BBDown.Commands;
using BBDown.Core.Util;

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
        // 订阅名可含 '/' '\'（用户 --name 或 target 回退为 URL），必须是单一目录段。
        // 断言真正的不变式（RF-91）：净化后拼进 work-dir 仍落在 work-dir 之内——
        // 只查"不含分隔符"弱于该不变式（`..` + 分隔符才构成逃逸）。
        var dir = Resolve("a/b\\c", "mid:1");
        Assert.DoesNotContain('/', dir);
        Assert.DoesNotContain('\\', dir);
        var baseDir = Path.Combine(Path.GetTempPath(), "bbdown-base");
        Assert.StartsWith(Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar,
            Path.GetFullPath(Path.Combine(baseDir, dir)));
    }

    [Fact]
    public void WindowsIllegalChars_AreSanitized()
    {
        // 'mid:163637592' 作订阅名：':' 在 Windows 非法
        var dir = Resolve("mid:163637592", "mid:163637592");
        Assert.DoesNotContain(':', dir);
        Assert.Equal("mid_163637592", dir);
    }

    [Fact]
    public void EmptyName_FallsBackToTarget()
    {
        // RF-91：必须断言等于净化后的 target。原断言（非空 + 不含 ':'）对 "_" 同样成立——
        // 而 SanitizePathSegment 对空输入的兜底正是 "_"，因此原测试在"回退失效"时假绿。
        Assert.Equal("mid_163637592", Resolve("", "mid:163637592"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyishName_FallsBackToTarget(string? name)
    {
        // 前提契约：SanitizePathSegment 对空/纯空白输入兜底返回 "_"，永不返回空串，
        // 因此"是否回退"只能用净化前的原始值判断（RF-91）。该契约一旦变化本测试失败，
        // 提醒复核 ResolveSubDirName 的回退判据。
        Assert.NotEqual("", PathUtil.SanitizePathSegment(name));
        Assert.Equal("mid_163637592", Resolve(name!, "mid:163637592"));
    }

    [Fact]
    public void DegenerateDotOnlyName_StaysSafeAndNonEmpty()
    {
        // 纯点名不是空白，不触发回退（判据与 SubscriptionStore.AddAsync 一致），
        // 但净化后必须是安全非空段：不能变成 "."/".."（Windows 非法、且会改变目录层级）。
        var dir = Resolve("...", "mid:1");
        Assert.NotEqual(".", dir);
        Assert.NotEqual("..", dir);
        Assert.NotEqual("", dir);
        Assert.DoesNotContain('/', dir);
        Assert.DoesNotContain('\\', dir);
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
    public void EachCallOccupiesASlot_SoSuffixesStayStableAcrossRuns()
    {
        // ResolveSubDirName 每次调用都占用一个槽位；调用方（CheckSubscriptionsAsync）
        // 对每个订阅——无论有无新增内容——都在循环开头（continue 之前）解析一次，
        // 因此序号跨 run 稳定。这里锁住"每次调用即占号"的契约。
        var slots = NewSlots();
        Resolve("同名", "mid:1", slots);
        Resolve("同名", "mid:2", slots);
        Assert.Equal(2, slots.Count);
        Assert.Equal("同名-3", Resolve("同名", "mid:3", slots));
    }

    [Fact]
    public void PerSubDir_DefaultsToOff()
    {
        // 向后兼容：不传 --per-sub-dir 必须保持既有平铺行为
        Assert.False(new SubCheckSettings().PerSubDir);
    }
}

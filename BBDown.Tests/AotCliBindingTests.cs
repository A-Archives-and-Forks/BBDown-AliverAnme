using System.Reflection;
using BBDown.Commands;
using Spectre.Console.Cli;

namespace BBDown.Tests;

/// <summary>
/// 本项目以 Native AOT 发布，而 Spectre.Console.Cli 的参数绑定走
/// <c>TypeDescriptor.GetConverter</c> 与 <c>Activator.CreateInstance</c>，
/// 发布时会产生一批 IL2026/IL2070/IL2072/IL2075 裁剪警告。
///
/// 目前这些警告不构成实际风险，唯一的原因是所有命令行参数都用了
/// string / int / bool —— 它们的转换器是内置且静态可达的。
/// 一旦有人添加枚举、数组或自定义类型的参数，AOT 产物会在运行时
/// 绑定该参数时抛异常，而日常的 JIT 开发构建完全不会暴露这一点。
///
/// 这些测试就是那道防线：改动参数类型会在这里失败，而不是等到用户
/// 拿到 release 二进制才发现。
/// </summary>
public class AotCliBindingTests
{
    /// <summary>AOT 下可安全绑定的参数类型。</summary>
    private static readonly HashSet<Type> AotSafeTypes =
    [
        typeof(string), typeof(int), typeof(bool),
    ];

    public static TheoryData<Type> SettingsTypes =>
    [
        typeof(MyOption), typeof(ServeSettings), typeof(LoginSettings),
    ];

    [Theory]
    [MemberData(nameof(SettingsTypes))]
    public void EveryCommandOption_UsesAnAotSafeType(Type settingsType)
    {
        var offenders = settingsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<CommandOptionAttribute>() is not null
                     || p.GetCustomAttribute<CommandArgumentAttribute>() is not null)
            .Select(p => (p.Name, Type: Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
            .Where(x => !AotSafeTypes.Contains(x.Type))
            .Select(x => $"{x.Name}: {x.Type.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{settingsType.Name} 中以下参数的类型在 Native AOT 下依赖动态类型转换器，" +
            $"绑定时可能抛异常：{string.Join(", ", offenders)}。" +
            "若确需该类型，请先验证 AOT 产物能正确解析，再把它加入 AotSafeTypes。");
    }

    [Theory]
    [MemberData(nameof(SettingsTypes))]
    public void SettingsType_ExposesAParameterlessConstructor(Type settingsType)
    {
        // Spectre.Console.Cli 通过 Activator.CreateInstance 构造 settings，
        // AOT 下若无公共无参构造会直接失败
        Assert.NotNull(settingsType.GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void MyOption_HasOptionsDeclared()
    {
        // 防止上面的检查因反射拿不到任何属性而空过
        var count = typeof(MyOption)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.GetCustomAttribute<CommandOptionAttribute>() is not null);

        Assert.True(count > 40, $"只找到 {count} 个 CommandOption，反射可能未按预期工作");
    }
}

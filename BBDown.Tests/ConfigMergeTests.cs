namespace BBDown.Tests;

/// <summary>
/// 配置文件与命令行的合并优先级。命令行显式给出的选项必须压过配置文件，
/// 无论用空格还是等号写法；配置文件里以 '-' 开头的取值也不能被吞掉。
/// </summary>
public class ConfigMergeTests
{
    private static string WriteConfig(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-cfg-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>合并结果里某个规范选项名最终生效的值（Spectre 后者胜出）。</summary>
    private static string? EffectiveValue(List<string> merged, string longName)
    {
        string? val = null;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i] == longName && i + 1 < merged.Count) val = merged[i + 1];
            else if (merged[i].StartsWith(longName + "=")) val = merged[i][(longName.Length + 1)..];
        }
        return val;
    }

    [Fact]
    public void SpaceStyleCliOption_OverridesConfigFile()
    {
        var cfg = WriteConfig("--dfn-priority\n1080P 高清\n");
        var merged = BBDownConfigParser.MergeWithConfig(
            ["--dfn-priority", "720P 高清", "--config-file", cfg, "URL"]);
        File.Delete(cfg);

        Assert.Equal("720P 高清", EffectiveValue(merged, "--dfn-priority"));
    }

    [Fact]
    public void EqualsStyleCliOption_OverridesConfigFile()
    {
        // 旧实现按精确 token 匹配识别"已显式指定"，--dfn-priority=X 匹配不到，
        // 配置文件的值被追加到末尾，按 Spectre 后者胜出反向覆盖了命令行
        var cfg = WriteConfig("--dfn-priority\n1080P 高清\n");
        var merged = BBDownConfigParser.MergeWithConfig(
            ["--dfn-priority=720P 高清", "--config-file", cfg, "URL"]);
        File.Delete(cfg);

        Assert.Equal("720P 高清", EffectiveValue(merged, "--dfn-priority"));
    }

    [Fact]
    public void EqualsStyleConfigFilePath_IsHonored()
    {
        // --config-file=path 形式此前匹配不到，会回落到默认配置路径而忽略用户指定
        var cfg = WriteConfig("--dfn-priority\n1080P 高清\n");
        var merged = BBDownConfigParser.MergeWithConfig(
            [$"--config-file={cfg}", "URL"]);
        File.Delete(cfg);

        Assert.Equal("1080P 高清", EffectiveValue(merged, "--dfn-priority"));
    }

    [Fact]
    public void ConfigOptionNotOnCommandLine_IsApplied()
    {
        var cfg = WriteConfig("--dfn-priority\n1080P 高清\n");
        var merged = BBDownConfigParser.MergeWithConfig(["--config-file", cfg, "URL"]);
        File.Delete(cfg);

        Assert.Equal("1080P 高清", EffectiveValue(merged, "--dfn-priority"));
    }
}

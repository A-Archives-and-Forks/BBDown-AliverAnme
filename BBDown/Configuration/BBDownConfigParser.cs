using System;
using BBDown.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Spectre.Console.Cli;

namespace BBDown;

internal static class BBDownConfigParser
{
    public static List<string> MergeWithConfig(string[] cliArgs)
    {
        var result = new List<string>(cliArgs);

        // 同时支持 "--config-file path" 与 "--config-file=path" 两种写法；
        // 旧实现只认空格写法，等号写法会被忽略而回落到默认配置路径。
        string? configPath = null;
        for (int i = 0; i < cliArgs.Length; i++)
        {
            if (cliArgs[i] == "--config-file")
            {
                configPath = cliArgs.ElementAtOrDefault(i + 1);
                break;
            }
            if (cliArgs[i].StartsWith("--config-file=", StringComparison.Ordinal))
            {
                configPath = cliArgs[i]["--config-file=".Length..];
                break;
            }
        }

        if (string.IsNullOrEmpty(configPath))
            configPath = Path.Combine(Program.APP_DIR, "BBDown.config");

        if (!File.Exists(configPath))
            return result;

        Logger.Log($"加载配置文件: {configPath}");

        var configArgs = File.ReadAllLines(configPath)
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.TrimStart().StartsWith('#'))
            .SelectMany(line =>
            {
                var trim = line.Trim();
                if (trim.StartsWith('-') && trim.Contains(' '))
                {
                    var idx = trim.IndexOf(' ');
                    return new[] { trim[..idx], trim[idx..].Trim().Trim('"') };
                }
                return new[] { trim.Trim('"') };
            })
            .ToList();

        var aliasMap = BuildAliasMap();

        var explicitOptions = new HashSet<string>();
        for (int i = 0; i < cliArgs.Length; i++)
        {
            if (!cliArgs[i].StartsWith('-')) continue;
            // 命令行可写成 "--opt value" 或 "--opt=value"，识别"已显式指定"时
            // 必须剥掉等号后缀，否则等号写法匹配不到别名，会被配置文件反向覆盖。
            var token = cliArgs[i];
            var eq = token.IndexOf('=');
            if (eq > 0) token = token[..eq];
            if (aliasMap.TryGetValue(token, out var canonical))
            {
                explicitOptions.Add(canonical);
            }
        }

        for (int i = 0; i < configArgs.Count;)
        {
            var name = configArgs[i];
            if (!name.StartsWith('-'))
            {
                result.Add(name);
                i++;
                continue;
            }

            if (aliasMap.TryGetValue(name, out var canonical))
            {
                if (!explicitOptions.Contains(canonical))
                {
                    result.Add(name);
                    i++;
                    // 收集该选项的值。仅当"以 - 开头且是已知选项名"时才视为下一个选项终止收集：
                    // 否则配置文件里值本身以 - 开头（如 --access-token -abc、负数参数）会被误当选项丢弃。
                    while (i < configArgs.Count && (!configArgs[i].StartsWith('-') || !aliasMap.ContainsKey(configArgs[i])))
                    {
                        result.Add(configArgs[i]);
                        i++;
                    }
                }
                else
                {
                    i++;
                    // 命令行已显式指定该选项：跳过配置文件里的值，判定规则同上
                    while (i < configArgs.Count && (!configArgs[i].StartsWith('-') || !aliasMap.ContainsKey(configArgs[i]))) i++;
                }
            }
            else
            {
                result.Add(name);
                i++;
            }
        }

        Logger.LogDebug("新的命令行参数: " + string.Join(" ", result));
        return result;
    }

    private static Dictionary<string, string> BuildAliasMap()
    {
        var map = new Dictionary<string, string>();

        void ScanType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<CommandOptionAttribute>();
                if (attr != null)
                {
                    var canonical = prop.Name;
                    foreach (var name in attr.LongNames)
                    {
                        map["--" + name] = canonical;
                    }
                    foreach (var name in attr.ShortNames)
                    {
                        map["-" + name] = canonical;
                    }
                }
            }
        }

        ScanType(typeof(MyOption));
        ScanType(typeof(Commands.ServeSettings));
        return map;
    }
}

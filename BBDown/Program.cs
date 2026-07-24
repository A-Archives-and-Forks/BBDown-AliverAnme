using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BBDown.Core;
using BBDown.Core.Util;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using BBDown.Core.Entity;
using BBDown.Core.DRM;
using System.Diagnostics;
using Spectre.Console.Cli;
using BBDown.Commands;

namespace BBDown;

partial class Program
{
    private static readonly string BACKUP_HOST = "upos-sz-mirrorcoso1.bilivideo.com";
    public static string SinglePageDefaultSavePath { get; set; } = "<videoTitle>";
    public static string MultiPageDefaultSavePath { get; set; } = "<videoTitle>/[P<pageNumberWithZero>]<pageTitle>";

    public static readonly string APP_DIR = Path.GetDirectoryName(Environment.ProcessPath)!;

    private static string FormatTimeStamp(long ts, string format)
    {
        try
        {
            return ts == 0 ? "null" : DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().ToString(format);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException)
        {
            Logger.LogError($"格式化日期出错: {ex.Message}");
            return ts.ToString();
        }
    }

    [JsonSerializable(typeof(MyOption))]
    [JsonSerializable(typeof(ServeRequestOptions))]
    partial class MyOptionJsonContext : JsonSerializerContext { }

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Logger.LogWarn("Force Exit...");
        try
        {
            Console.ResetColor();
            Console.CursorVisible = true;
            if (!OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start("stty", "echo");
        }
        catch { }
        Environment.Exit(0);
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DefaultCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LoginCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LoginTVCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ServeCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MyOption))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LoginSettings))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ServeSettings))]
    public static async Task<int> Main(params string[] args)
    {
        Console.CancelKeyPress += Console_CancelKeyPress;

        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
        Console.Write($"BBDown version {ver.Major}.{ver.Minor}.{ver.Build}, Bilibili Downloader.\r\n");
        Console.ResetColor();
        Console.Write("遇到问题请首先到以下地址查阅有无相关信息：\r\nhttps://github.com/AliverAnme/BBDown/issues\r\n");
        Console.WriteLine();

        var normalizedArgs = NormalizeCliArgs(args);
        var mergedArgs = BBDownConfigParser.MergeWithConfig(normalizedArgs).ToArray();

        if (mergedArgs.Contains("--debug"))
        {
            Config.Apply(Config.Current with { DebugLog = true });
        }

        var services = new ServiceCollection();
        var registrar = new TypeRegistrar(services);
        var app = new CommandApp<DefaultCommand>(registrar);
        app.Configure(config =>
        {
            config.SetApplicationName("BBDown");
            config.SetApplicationVersion($"{ver.Major}.{ver.Minor}.{ver.Build}");
            config.SetExceptionHandler((ex, resolver) =>
            {
                Console.BackgroundColor = ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.White;
                var msg = Config.Current.DebugLog ? ex.ToString() : ex.Message;
                Console.Error.WriteLine(msg);
                Console.Error.WriteLine("请尝试升级到最新版本后重试!");
                Console.ResetColor();
                try { Console.CursorVisible = true; } catch { }
                return 1;
            });

            config.AddCommand<LoginCommand>("login")
                  .WithDescription("通过APP扫描二维码以登录您的WEB账号");
            config.AddCommand<LoginTVCommand>("logintv")
                  .WithDescription("通过APP扫描二维码以登录您的TV账号");
            config.AddCommand<ServeCommand>("serve")
                  .WithDescription("以服务器模式运行");
        });

        return await app.RunAsync(mergedArgs);
    }

    internal static string[] NormalizeCliArgs(string[] args)
    {
        return args.Select(arg => arg switch
        {
            "-help" => "--help",
            "-?" => "--help",
            "-version" => "--version",
            _ => arg
        }).ToArray();
    }

    internal static void StartServer(string? listenUrl, int maxConcurrent = 3)
    {
        var defaultListenUrl = "http://0.0.0.0:23333";
        Logger.LogFilePath = Path.Combine(Directory.GetCurrentDirectory(), "bbdown-api.log");
        var server = new BBDownApiServer(maxConcurrent);
        server.SetUpServer();
        server.Run(string.IsNullOrEmpty(listenUrl) ? defaultListenUrl : listenUrl);
    }

    internal static async Task DoWorkAsync(MyOption myOption, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats,
            input, savePathFormat, lang, aidOri, delay) = SetUpWork(myOption);
        var (fetchedAid, vInfo, apiType) = await GetVideoInfoAsync(myOption, aidOri, input);
        await DownloadPagesAsync(myOption, vInfo, encodingPriority, dfnPriority, firstEncoding, downloadDanmaku, downloadDanmakuFormats,
            input, savePathFormat, lang, fetchedAid, delay, apiType, cancellationToken: cancellationToken);
    }

}

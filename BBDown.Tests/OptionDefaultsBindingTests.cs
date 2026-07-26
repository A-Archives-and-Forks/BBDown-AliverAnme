using Spectre.Console.Cli;

namespace BBDown.Tests;

/// <summary>
/// Spectre.Console.Cli 在 flag（bool）未出现于命令行时会把属性写回 default(false)，
/// 覆盖 C# 的属性初始化器。四个帮助文本标注"默认开启"的选项因此实际默认关闭。
/// 唯一可靠的修复是 [DefaultValue(true)]，这些测试驱动真实的 CommandApp 绑定来锁死它。
/// </summary>
public class OptionDefaultsBindingTests
{
    /// <summary>捕获绑定后的 MyOption，不执行任何真实下载。</summary>
    private sealed class CaptureCommand : Command<MyOption>
    {
        public static MyOption? Captured;
        protected override int Execute(CommandContext context, MyOption settings, CancellationToken cancellationToken)
        {
            Captured = settings;
            return 0;
        }
    }

    private static MyOption Bind(params string[] args)
    {
        CaptureCommand.Captured = null;
        var app = new CommandApp<CaptureCommand>();
        var exit = app.Run(args);
        Assert.Equal(0, exit);
        Assert.NotNull(CaptureCommand.Captured);
        return CaptureCommand.Captured!;
    }

    [Fact]
    public void FlagsDocumentedAsDefaultOn_AreTrueWhenNotSpecified()
    {
        var o = Bind("https://www.bilibili.com/video/BV1qt4y1X7TW");

        Assert.True(o.MultiThread, "--multi-thread 帮助文本称默认开启");
        Assert.True(o.ForceHttp, "--force-http 帮助文本称默认开启");
        Assert.True(o.SkipAi, "--skip-ai 帮助文本称默认开启");
        Assert.True(o.ForceReplaceHost, "--force-replace-host 帮助文本称默认开启");
    }

    [Fact]
    public void DefaultOnFlags_CanStillBeDisabledExplicitly()
    {
        var o = Bind("--multi-thread", "false", "--skip-ai", "false", "url");

        Assert.False(o.MultiThread);
        Assert.False(o.SkipAi);
    }

    [Fact]
    public void FlagsDocumentedAsDefaultOff_RemainFalse()
    {
        var o = Bind("url");

        // 对照组：这些没有 [DefaultValue(true)]，本就该是 false
        Assert.False(o.UseTvApi);
        Assert.False(o.OnlyShowInfo);
        Assert.False(o.AudioOnly);
    }

    [Fact]
    public void NumericDefaults_ArePreserved()
    {
        var o = Bind("url");

        // int 选项不受该 bug 影响（Spectre 未指定时不回写），这里固化该前提
        Assert.Equal(30, o.MuxerTimeout);
        Assert.Equal(3, o.RetryCount);
    }
}

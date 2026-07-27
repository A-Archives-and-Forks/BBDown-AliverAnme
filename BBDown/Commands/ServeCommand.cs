using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class ServeSettings : CommandSettings
{
    [CommandOption("-l|--listen")]
    [Description("服务器监听url")]
    public string ListenUrl { get; set; } = "http://0.0.0.0:23333";

    [CommandOption("--max-concurrent")]
    [Description("最大并发下载数(默认3)")]
    public int MaxConcurrent { get; set; } = 3;
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class ServeCommand : Command<ServeSettings>
{
    protected override int Execute(CommandContext context, ServeSettings settings, CancellationToken cancellationToken)
    {
        _ = BBDownUtil.CheckUpdateAsync();
        Program.StartServer(settings.ListenUrl, settings.MaxConcurrent);
        return 0;
    }
}

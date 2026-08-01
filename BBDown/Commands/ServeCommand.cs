using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BBDown;
using BBDown.Core;

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

    [CommandOption("--serve-token")]
    [Description("可选访问令牌，设置后所有 API 请求需携带 X-Serve-Token 请求头，否则返回 401")]
    public string? ServeToken { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class ServeCommand : Command<ServeSettings>
{
    protected override int Execute(CommandContext context, ServeSettings settings, CancellationToken cancellationToken)
    {
        _ = BBDownUtil.CheckUpdateAsync(cancellationToken);
        try
        {
            if (settings.MaxConcurrent < 1)
            {
                Logger.LogError($"--max-concurrent 至少为 1，当前为 {settings.MaxConcurrent}");
                return 1;
            }
            Program.StartServer(settings.ListenUrl, settings.MaxConcurrent, settings.ServeToken, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception e)
        {
            Logger.LogError($"服务器启动失败: {e.Message}");
            return 1;
        }
    }
}

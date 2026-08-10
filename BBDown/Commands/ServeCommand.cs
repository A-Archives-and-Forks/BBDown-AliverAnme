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
    public string ListenUrl { get; set; } = "http://127.0.0.1:23333";

    [CommandOption("--max-concurrent")]
    [Description("最大并发下载数(默认3)")]
    public int MaxConcurrent { get; set; } = 3;

    [CommandOption("--serve-token")]
    [Description("可选访问令牌，设置后所有 API 请求需携带 X-Serve-Token 请求头，否则返回 401")]
    public string? ServeToken { get; set; }

    [CommandOption("--notify-webhook")]
    [Description("任务完成时向该固定地址发送 HTTP POST 回调(服务端配置, 不接受客户端指定)")]
    public string? NotifyWebhook { get; set; }
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
            // 默认安全边界的前置校验：非回环监听（0.0.0.0 / :: / 具体网卡 IP）会把任务
            // 端点暴露到局域网/公网，必须显式配置 --serve-token 才能启动。
            // 这里给出可读错误；BBDownApiServer.Run 内还有兜底防御（InvalidOperationException）。
            if (!IsLoopbackListenUrl(settings.ListenUrl) && string.IsNullOrEmpty(settings.ServeToken))
            {
                Logger.LogError(
                    $"监听地址 {settings.ListenUrl} 不是回环地址（127.0.0.1/localhost），" +
                    $"非回环监听必须配置 --serve-token 才能启动，否则任意客户端都能提交任务并访问本机文件。");
                return 1;
            }
            Program.StartServer(settings.ListenUrl, settings.MaxConcurrent, settings.ServeToken, settings.NotifyWebhook, cancellationToken);
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

    /// <summary>监听 URL 是否属于本机回环（127.0.0.1 / localhost / [::1] / ::1）。</summary>
    private static bool IsLoopbackListenUrl(string listenUrl)
    {
        // 空值在 Program.StartServer 会回落到默认回环地址，视为回环
        if (string.IsNullOrWhiteSpace(listenUrl)) return true;
        if (Uri.TryCreate(listenUrl, UriKind.Absolute, out var uri))
        {
            // DnsSafeHost 去掉 IPv6 字面量的方括号，IPAddress.TryParse 才能解析 [::1]
            var host = uri.DnsSafeHost;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (System.Net.IPAddress.TryParse(host, out var ip)) return System.Net.IPAddress.IsLoopback(ip);
        }
        return false;
    }
}

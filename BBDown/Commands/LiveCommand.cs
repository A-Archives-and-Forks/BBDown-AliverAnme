using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BBDown;
using BBDown.Core;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class LiveSettings : CommandSettings
{
    [CommandArgument(0, "<room_id>")]
    [Description("直播间 ID，如 12345")]
    public string RoomId { get; set; } = "";

    [CommandOption("-o|--output")]
    [Description("输出文件路径(默认: 直播间标题_直播录制_时间.flv)")]
    public string? Output { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class LiveCommand : Command<LiveSettings>
{
    protected override int Execute(CommandContext context, LiveSettings settings, CancellationToken cancellationToken)
    {
        // Task.Run avoids deadlock if called from a thread with a SynchronizationContext
        return Task.Run(async () =>
        {
            try
            {
                Logger.Log($"正在解析直播间 {settings.RoomId}...");
                var (_, title, uname, _) = await LiveStreamUtil.ResolveAsync(settings.RoomId, cancellationToken);
                Logger.Log($"直播间: {title} (UP: {uname})");
                string path = settings.Output ?? $"{LiveStreamUtil.SanitizeFileName(title)}_直播录制_{DateTime.Now:yyyyMMdd_HHmmss}.flv";
                Logger.Log($"开始录制直播流: {path} (Ctrl+C 停止，断流自动重连)");

                DateTime lastLog = DateTime.MinValue;
                // 传 roomId：断流/地址过期时内部重新解析流地址续录
                await LiveStreamUtil.DownloadToFileAsync(settings.RoomId, path, total =>
                {
                    if (DateTime.Now - lastLog >= TimeSpan.FromSeconds(5))
                    {
                        lastLog = DateTime.Now;
                        Logger.Log($"已录制: {BBDownUtil.FormatFileSize(total)}");
                    }
                }, cancellationToken);

                Logger.Log($"录制已保存: {path}");
                return 0;
            }
            // 仅真正的用户取消返回 0；HttpClient 超时/重连耗尽抛出的
            // TaskCanceledException（token 未取消）必须落到下方失败分支返回 1，
            // 否则录制实际失败却以成功码退出，脚本/CI 拿不到失败信号。
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarn("录制已取消");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.LogError($"直播录制失败: {ex.Message}");
                return 1;
            }
        }).GetAwaiter().GetResult();
    }
}

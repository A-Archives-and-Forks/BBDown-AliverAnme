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
                var (url, title, uname, _) = await LiveStreamUtil.ResolveAsync(settings.RoomId, cancellationToken);
                Logger.Log($"直播间: {title} (UP: {uname})");
                string path = settings.Output ?? $"{LiveStreamUtil.SanitizeFileName(title)}_直播录制_{DateTime.Now:yyyyMMdd_HHmmss}.flv";
                Logger.Log($"开始录制直播流: {path} (Ctrl+C 停止)");

                DateTime lastLog = DateTime.MinValue;
                await LiveStreamUtil.DownloadToFileAsync(url, path, total =>
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
            catch (OperationCanceledException)
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

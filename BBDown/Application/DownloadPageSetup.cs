using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static BBDown.Core.Entity.Entity;

using BBDown.Core;
using BBDown.Core.Entity;
using BBDown.Core.Util;

namespace BBDown;

internal partial class Program
{
    private static (bool ShouldContinue, string Title) ApplyPreviewPolicy(
        VInfo videoInfo,
        Page page,
        ParsedResult parsedResult,
        MyOption options,
        string title)
    {
        var previewVerdict = UpowerGuard.Inspect(
            videoInfo.IsUpowerExclusive, videoInfo.IsUpowerPlay, page.dur, parsedResult.ActualDurationSec);
        if (!previewVerdict.IsPreview) return (true, title);

        Logger.LogWarn("========================================");
        Logger.LogWarn("  充电专属视频");
        Logger.LogWarn($"  {previewVerdict.Reason}");
        if (!options.AllowPreview && !options.OnlyShowInfo)
        {
            Logger.LogWarn("  已跳过。如需下载试看片段，请加 --allow-preview");
            Logger.LogWarn("========================================");
            return (false, title);
        }

        if (options.OnlyShowInfo)
        {
            // 仅解析模式放行，但说明流信息对应的是试看片段。
            Logger.LogWarn("  仅解析模式，以下流信息对应的是试看片段");
            Logger.LogWarn("========================================");
            return (true, title);
        }

        Logger.LogWarn("  已启用 --allow-preview，将下载试看片段");
        Logger.LogWarn("========================================");
        // <videoTitle> 是视频/封面/弹幕共用占位符；不拼接最终路径，保留自定义 file-pattern。
        if (!title.StartsWith("[试看]")) title = $"[试看]{title}";
        return (true, title);
    }

    private static async Task WriteDebugParseResponseAsync(ParsedResult parsedResult, CancellationToken cancellationToken)
    {
        if (!Config.Current.DebugLog) return;

        var debugFile = PathUtil.ResolveWorkPath($"debug_{DateTime.Now:yyyyMMddHHmmssfff}.json");
        await File.WriteAllTextAsync(debugFile, parsedResult.WebJsonString, cancellationToken);
        // 保留最近 20 个调试响应，避免长任务持续占用磁盘。
        var debugFiles = Directory.GetFiles(PathUtil.ResolveWorkPath("."), "debug_*.json").Order().ToArray();
        for (int i = 0; i < debugFiles.Length - 20; i++)
            File.Delete(debugFiles[i]);
    }
}

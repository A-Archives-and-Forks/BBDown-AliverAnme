using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BBDown;
using BBDown.Core;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class ArticleSettings : CommandSettings
{
    [CommandArgument(0, "<cv_id>")]
    [Description("专栏 ID 或链接，如 cv123 或 https://www.bilibili.com/read/cv123")]
    public string CvId { get; set; } = "";

    [CommandOption("-o|--output")]
    [Description("输出 Markdown 文件路径(默认: 专栏标题.md)")]
    public string? Output { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class ArticleCommand : Command<ArticleSettings>
{
    protected override int Execute(CommandContext context, ArticleSettings settings, CancellationToken cancellationToken)
    {
        // Task.Run avoids deadlock if called from a thread with a SynchronizationContext
        return Task.Run(async () =>
        {
            try
            {
                string cvId = ArticleUtil.ExtractCvId(settings.CvId);
                Logger.Log($"正在获取专栏 cv{cvId}...");
                var article = await ArticleUtil.FetchAsync(cvId, cancellationToken);
                string path = settings.Output ?? $"{LiveStreamUtil.SanitizeFileName(article.Title)}.md";
                await ArticleUtil.SaveAsMarkdownAsync(article, path);
                Logger.Log($"专栏已保存: {path}");
                return 0;
            }
            catch (OperationCanceledException ex)
            {
                // 区分主动取消（Ctrl+C，token 已取消）与 HttpClient 超时（token 未取消）：
                // 超时是真实失败，返回非零退出码而非以"已取消"+0 隐藏失败
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarn("已取消");
                    return 0;
                }
                Logger.LogError($"专栏获取超时或被中断: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Logger.LogError($"专栏获取失败: {ex.Message}");
                return 1;
            }
        }).GetAwaiter().GetResult();
    }
}

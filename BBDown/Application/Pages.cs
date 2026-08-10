using System;
using System.Collections.Generic;
using System.Linq;
using static BBDown.Core.Entity.Entity;
using System.Text.RegularExpressions;
using BBDown.Core.Entity;

using BBDown.Core.Util;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    private static List<string>? GetSelectedPages(MyOption myOption, VInfo vInfo, string input)
    {
        List<string>? selectedPages = null;
        List<Page> pagesInfo = vInfo.PagesInfo;
        string selectPage = myOption.SelectPage.ToUpperInvariant().Trim().Trim(',');

        if (string.IsNullOrEmpty(selectPage))
        {
            //如果用户没有选择分P, 根据epid或query param来确定某一集
            if (!string.IsNullOrEmpty(vInfo.Index))
            {
                selectedPages = [vInfo.Index];
                Logger.Log("程序已自动选择你输入的集数, 如果要下载其他集数请自行指定分P(如可使用-p ALL代表全部)");
            }
            else if (!string.IsNullOrEmpty(BBDownUtil.GetQueryString("p", input)))
            {
                selectedPages = [BBDownUtil.GetQueryString("p", input)];
                Logger.Log("程序已自动选择你输入的集数, 如果要下载其他集数请自行指定分P(如可使用-p ALL代表全部)");
            }
        }
        else if (selectPage != "ALL")
        {
            selectedPages = new List<string>();

            //选择最新分P
            string lastPage = pagesInfo.Count.ToString();
            foreach (string key in new[] { "LAST", "NEW", "LATEST" })
            {
                selectPage = selectPage.Replace(key, lastPage);
            }

            try
            {
                selectedPages = ParsePageSelection(selectPage);
            }
            catch (ArgumentException e)
            {
                // 解析失败绝不能回退为 null（null 的语义是下载 ALL）：
                // 用户输入 -p 10-1 / -p abc 时静默下载全部分P，批量场景可能触发数 GB 的非预期下载。
                // 直接中止任务，让调用方以错误退出码暴露问题。
                Logger.LogError($"解析分P参数失败: {e.Message}");
                throw;
            }
        }

        return selectedPages;
    }

    /// <summary>分P 选择表达式允许展开出的最大条目数，防止 <c>-p 1-99999999</c> 撑爆内存。</summary>
    private const int MaxExpandedPages = 100_000;

    /// <summary>
    /// 解析分P选择表达式，支持 <c>1,3,5</c>、<c>1-10</c> 以及两者混合的 <c>1-3,7,9-11</c>。
    /// 解析不出结果时抛 <see cref="ArgumentException"/>，由调用方转成用户可见的错误——
    /// 静默返回空列表会让程序不下载任何分P 却也不报错。
    /// </summary>
    internal static List<string> ParsePageSelection(string selectPage)
    {
        var pages = new List<string>();

        foreach (var raw in selectPage.Split(','))
        {
            var segment = raw.Trim();
            if (segment.Length == 0) continue;

            // 从第 2 个字符起找连字符，这样 "-5" 这种负数不会被误判成范围
            var dash = segment.IndexOf('-', 1);
            if (dash < 0)
            {
                pages.Add(segment);
                continue;
            }

            var startText = segment[..dash];
            var endText = segment[(dash + 1)..];
            if (!int.TryParse(startText, out var start) || !int.TryParse(endText, out var end))
            {
                throw new ArgumentException($"无法识别的分P范围 \"{segment}\"");
            }
            if (start > end)
            {
                throw new ArgumentException($"分P范围 \"{segment}\" 的起始值大于结束值");
            }
            if ((long)end - start + 1 > MaxExpandedPages)
            {
                throw new ArgumentException($"分P范围 \"{segment}\" 展开后超过 {MaxExpandedPages} 项");
            }

            for (var i = start; i <= end; i++)
            {
                pages.Add(i.ToString());
            }
        }

        if (pages.Count == 0)
        {
            throw new ArgumentException($"\"{selectPage}\" 未选中任何分P");
        }

        return pages;
    }

    /// <summary>
    /// 处理CDN域名
    /// </summary>
    /// <param name="myOption"></param>
    /// <param name="video"></param>
    /// <param name="audio"></param>
    private static void HandlePcdn(MyOption myOption, Video? selectedVideo, Audio? selectedAudio)
    {
        if (myOption.UposHost == "")
        {
            //处理PCDN
            if (!myOption.AllowPcdn)
            {
                var pcdnReg = PcdnRegex();
                if (selectedVideo != null && pcdnReg.IsMatch(selectedVideo.baseUrl))
                {
                    Logger.LogWarn($"检测到视频流为PCDN, 尝试强制替换为{BACKUP_HOST}……");
                    selectedVideo.baseUrl = pcdnReg.Replace(selectedVideo.baseUrl, $"://{BACKUP_HOST}/");
                }
                if (selectedAudio != null && pcdnReg.IsMatch(selectedAudio.baseUrl))
                {
                    Logger.LogWarn($"检测到音频流为PCDN, 尝试强制替换为{BACKUP_HOST}……");
                    selectedAudio.baseUrl = pcdnReg.Replace(selectedAudio.baseUrl, $"://{BACKUP_HOST}/");
                }
            }

            var akamReg = AkamRegex();
            if (selectedVideo != null && Config.Current.Area != "" && selectedVideo.baseUrl.Contains("akamaized.net"))
            {
                Logger.LogWarn($"检测到视频流为外国源, 尝试强制替换为{BACKUP_HOST}……");
                selectedVideo.baseUrl = akamReg.Replace(selectedVideo.baseUrl, $"://{BACKUP_HOST}/");
            }
            if (selectedAudio != null && Config.Current.Area != "" && selectedAudio.baseUrl.Contains("akamaized.net"))
            {
                Logger.LogWarn($"检测到音频流为外国源, 尝试强制替换为{BACKUP_HOST}……");
                selectedAudio.baseUrl = akamReg.Replace(selectedAudio.baseUrl, $"://{BACKUP_HOST}/");
            }
        }
        else
        {
            if (selectedVideo != null)
            {
                Logger.LogWarn($"尝试将视频流强制替换为{myOption.UposHost}……");
                selectedVideo.baseUrl = UposRegex().Replace(selectedVideo.baseUrl, $"://{myOption.UposHost}/");
            }
            if (selectedAudio != null)
            {
                Logger.LogWarn($"尝试将音频流强制替换为{myOption.UposHost}……");
                selectedAudio.baseUrl = UposRegex().Replace(selectedAudio.baseUrl, $"://{myOption.UposHost}/");
            }
        }
    }

    /// <summary>
    /// 打印解析到的各个轨道信息
    /// </summary>
    /// <param name="parsedResult"></param>
    /// <param name="pageDur"></param>
}

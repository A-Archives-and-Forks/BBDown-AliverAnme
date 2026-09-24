using System;
using System.IO;

namespace BBDown;

/// <summary>下载产物的容错文件清理。</summary>
internal static class DownloadFileCleanup
{
    /// <summary>
    /// 删除章节元数据残留。muxer 按输出文件名派生唯一名（chapters-{basename}），
    /// 早期版本与部分清理路径使用固定名 chapters；按前缀清理两种形式。
    /// 单文件清理失败不影响下载结果。
    /// </summary>
    internal static void DeleteResidualChapterFiles(string dir)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dir, "chapters*"))
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

using BBDown.Core;

namespace BBDown.Tests;

/// <summary>
/// C1：Logger 持久 writer。每行不再 File.AppendAllText 开关文件，
/// 改为进程内单例 StreamWriter（AutoFlush）。验证行内容落盘与 CloseFile 释放句柄。
/// </summary>
public class LoggerFileTests
{
    [Fact]
    public void FileLogging_WritesAllLines_AndCloseFileReleasesHandle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-log-{Guid.NewGuid():N}.txt");
        var original = Logger.LogFilePath;
        try
        {
            Logger.LogFilePath = path;
            Logger.Log("line-one");
            Logger.LogWarn("line-two");
            Logger.Log("line-three");

            // AutoFlush 保证每行已落盘（不依赖 CloseFile）
            // 读取需 FileShare.ReadWrite：writer 以 FileShare.Read 打开（允许外部读），
            // 而 File.ReadAllText 默认 FileShare.Read 不允许 writer 的写访问，会共享冲突
            var content = ReadAllTextShared(path);
            Assert.Contains("line-one", content);
            Assert.Contains("line-two", content);
            Assert.Contains("line-three", content);

            // 同一路径重设是 no-op（writer 保留），新行仍追加而非覆盖
            Logger.LogFilePath = path;
            Logger.Log("line-four");
            content = ReadAllTextShared(path);
            Assert.Contains("line-four", content);
            Assert.Contains("line-one", content);

            // CloseFile 释放句柄：Windows 上未释放时 File.Delete 会抛 IOException
            Logger.CloseFile();
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Logger.LogFilePath = original;
            Logger.CloseFile();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void FileLogging_ExternalDelete_RecreatesOnNextWrite()
    {
        // FileShare.ReadWrite|Delete：外部删除（Windows 上）成功，且下一次写入经轮转检测
        // 发现文件不存在后重建 writer 并继续追加——日志不会因外部清理而永久停止
        var path = Path.Combine(Path.GetTempPath(), $"bbdown-log-{Guid.NewGuid():N}.txt");
        var original = Logger.LogFilePath;
        try
        {
            Logger.LogFilePath = path;
            Logger.Log("first");
            Assert.True(File.Exists(path));

            File.Delete(path);
            Assert.False(File.Exists(path));

            // 自愈：重建 writer 写入新文件
            Logger.Log("second");
            var content = ReadAllTextShared(path);
            Assert.Contains("second", content);
            Assert.DoesNotContain("first", content);
        }
        finally
        {
            Logger.LogFilePath = original;
            Logger.CloseFile();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>以 FileShare.ReadWrite 读取：兼容 Logger writer（FileAccess.Write + FileShare.ReadWrite|Delete）持有的文件。</summary>
    private static string ReadAllTextShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }
}
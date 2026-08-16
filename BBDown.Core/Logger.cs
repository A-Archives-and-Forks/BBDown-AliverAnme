using System.Text;

namespace BBDown.Core;

public static class Logger
{
    private static string? _logFilePath;
    /// <summary>
    /// 日志文件路径。设置时关闭旧持久 writer（路径变化场景下句柄不残留）。
    /// serve 启动时设为 CWD/bbdown-api.log；未设置时只写控制台。
    /// </summary>
    public static string? LogFilePath
    {
        get => _logFilePath;
        set
        {
            lock (_fileLock)
            {
                if (_logFilePath == value) return;
                _logFilePath = value;
                CloseFileWriterLocked();
            }
        }
    }

    // Console.ForegroundColor 是进程级全局状态：并发下载时多线程交错"设色-写入-复位"
    // 会互相插入颜色区间，导致日志颜色错乱、行内容交错。单条日志的写入需整体加锁。
    private static readonly object _consoleLock = new();

    // 文件写入串行化：与 Console 写入的先后顺序由锁保证（Log/LogColor/LogWarn 先取
    // _consoleLock、释放后再 AppendToFile 取 _fileLock，不构成嵌套锁，不会死锁）。
    private static readonly object _fileLock = new();
    private static StreamWriter? _fileWriter;

    private static void WriteLine(string line)
    {
        Console.WriteLine(line);
        AppendToFile(line);
    }

    private static StreamWriter? CreateFileWriter()
    {
        if (string.IsNullOrEmpty(_logFilePath)) return null;
        try
        {
            // 持久句柄 + AutoFlush：替代每行 File.AppendAllText 的开合文件（高频日志下最大的
            // 同步 IO 开销）。AutoFlush 保证每行立即落盘（崩溃/断电不丢行）。
            // FileShare.ReadWrite|Delete：允许 tail/Get-Content（ReadWrite 共享）外部读取，
            // 也允许 Windows 上外部删除/轮转（File.Delete 需要 FILE_SHARE_DELETE）。
            // 注意：默认 FileShare.Read 的读取器（File.ReadAllText）在 Windows 上仍会因不授予
            // 写共享而失败——这是"持有一个写句柄"的固有属性，读日志请用 tail/Get-Content。
            var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            return new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 路径不可写：降级为仅控制台
            return null;
        }
    }

    private static void AppendToFile(string line)
    {
        if (string.IsNullOrEmpty(_logFilePath)) return;
        try
        {
            lock (_fileLock)
            {
                // 轮转自愈：外部 mv/删除后文件被替换时重开 writer，否则日志会写入已重命名的
                // 旧 inode（Unix 上 FileShare 无效，mv 后新文件永远收不到日志；用长度对比检测）。
                if (_fileWriter is not null && LogFileReplacedLocked())
                    CloseFileWriterLocked();
                _fileWriter ??= CreateFileWriter();
                _fileWriter?.WriteLine(line);
            }
        }
        catch { /* silently ignore file write failures */ }
    }

    /// <summary>检测日志文件是否已被外部替换/截断（轮转自愈用，仅在 <see cref="_fileLock"/> 内调用）。</summary>
    private static bool LogFileReplacedLocked()
    {
        try
        {
            var fi = new FileInfo(_logFilePath!);
            // 文件被删除，或长度小于已写字节数（mv+新建/截断替换）→ 持久句柄已指向旧文件
            return !fi.Exists || fi.Length < _fileWriter!.BaseStream.Position;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>关闭并释放日志 writer（serve 关停时调用，释放文件句柄）。</summary>
    public static void CloseFile()
    {
        lock (_fileLock) CloseFileWriterLocked();
    }

    private static void CloseFileWriterLocked()
    {
        if (_fileWriter is null) return;
        try { _fileWriter.Dispose(); } catch { }
        _fileWriter = null;
    }

    /// <summary>以指定颜色原子地输出一行（设色-写入-复位在锁内完成）。</summary>
    private static void WriteColored(string text, ConsoleColor color)
    {
        lock (_consoleLock)
        {
            Console.ForegroundColor = color;
            try
            {
                Console.Write(text);
            }
            finally
            {
                Console.ResetColor();
            }
        }
    }

    public static void Log(object text, bool enter = true)
    {
        var line = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - " + text;
        // 普通日志同样要加锁：serve 并发下载时多线程交错写会插行/截断，
        // 与 LogColor/LogError 的锁保持一致（这些方法都把 Console 写入锁内）。
        lock (_consoleLock)
        {
            Console.Write(line);
            if (enter) Console.WriteLine();
        }
        AppendToFile(line);
        if (enter) AppendToFile(string.Empty);
    }

    public static void LogError(object text)
    {
        var line = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - " + text;
        lock (_consoleLock)
        {
            Console.Write(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - ");
            WriteColored(text.ToString() ?? "", ConsoleColor.Red);
            Console.WriteLine();
        }
        AppendToFile(line);
    }

    public static void LogColor(object text, bool time = true)
    {
        string line;
        if (time)
        {
            line = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - " + text;
        }
        else
        {
            line = "                             " + text;
        }
        lock (_consoleLock)
        {
            if (time)
                Console.Write(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - ");
            else
                Console.Write("                            ");
            WriteColored(text.ToString() ?? "", ConsoleColor.Cyan);
            Console.WriteLine();
        }
        AppendToFile(line);
    }

    public static void LogWarn(object text, bool time = true)
    {
        string line;
        if (time)
        {
            line = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - " + text;
        }
        else
        {
            line = "                             " + text;
        }
        lock (_consoleLock)
        {
            if (time)
                Console.Write(DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - ");
            else
                Console.Write("                            ");
            WriteColored(text.ToString() ?? "", ConsoleColor.DarkYellow);
            Console.WriteLine();
        }
        AppendToFile(line);
    }

    public static void LogDebug(string toFormat, params object[] args)
    {
        if (!Config.Current.DebugLog) return;
        string message = args.Length > 0 ? string.Format(toFormat, args).Trim() : toFormat;
        var line = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss.fff]") + " - " + message;
        lock (_consoleLock)
        {
            WriteColored(line, ConsoleColor.DarkGray);
            Console.WriteLine();
        }
        AppendToFile(line);
    }
}

namespace BBDown.Core;

public static class Logger
{
    public static string? LogFilePath { get; set; }

    // Console.ForegroundColor 是进程级全局状态：并发下载时多线程交错"设色-写入-复位"
    // 会互相插入颜色区间，导致日志颜色错乱、行内容交错。单条日志的写入需整体加锁。
    private static readonly object _consoleLock = new();

    private static void WriteLine(string line)
    {
        Console.WriteLine(line);
        AppendToFile(line);
    }

    private static void AppendToFile(string line)
    {
        var path = LogFilePath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch { /* silently ignore file write failures */ }
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
        Console.Write(line);
        AppendToFile(line);
        if (enter)
        {
            Console.WriteLine();
            AppendToFile(string.Empty);
        }
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

using System.IO;

namespace BBDown.Core.Util;

public static class PathUtil
{
    private static readonly char[] InvalidChars = "34,60,62,124,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,58,42,63,92,47"
        .Split(',').Select(s => (char)byte.Parse(s)).ToArray();

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string GetValidFileName(string input, string re = "_", bool filterSlash = false)
    {
        string title = input;
        foreach (char invalidChar in InvalidChars)
        {
            title = title.Replace(invalidChar.ToString(), re);
        }
        if (filterSlash)
        {
            title = title.Replace("/", re);
            title = title.Replace("\\", re);
        }
        // Windows 保留名规则：CON/PRN/AUX/NUL/COM1..9/LPT1..9 后跟任意扩展名仍然保留
        // （CON.txt、CON.foo 在 Windows 上同样无法创建）。因此按基名匹配，
        // 而不是只匹配完整名称——否则 "CON.txt" 会漏过校验而在 Windows 上报错。
        var nameWithoutExt = Path.GetFileNameWithoutExtension(title);
        if (ReservedNames.Contains(title) || ReservedNames.Contains(nameWithoutExt))
            title = "_" + title;
        return title;
    }

    /// <summary>
    /// 把下载管线的相对路径解析为绝对路径：优先基于当前任务流的工作目录
    /// （<see cref="Config.Current"/> 的 WorkDir，serve 下每个任务经 SetUpWork 各自写入），
    /// 否则基于进程 CWD。CLI 单任务与 serve 并发的统一入口。
    /// 绝对路径原样透传（Path.Combine 对已根化的第二参数直接返回），因此自定义
    /// --file-pattern 指向绝对目录时不受 WorkDir 影响。
    /// </summary>
    public static string ResolveWorkPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var workDir = Config.Current.WorkDir;
        return string.IsNullOrEmpty(workDir) ? Path.GetFullPath(path) : Path.Combine(workDir, path);
    }
}

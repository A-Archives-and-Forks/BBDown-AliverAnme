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

    /// <summary>
    /// 生成安全的文件名。除过滤非法字符/保留名外，还做基名长度截断：
    /// Windows 单组件上限 255 字符、整路径上限 260（未开启长路径支持的默认环境），
    /// 超长标题（如超长多P视频标题拼入 <videoTitle>/[P##]<pageTitle> 模板）会在
    /// 创建/混流阶段抛 PathTooLongException。截断为阈值式（仅 &gt; maxBaseNameLength 生效），
    /// 短标题零变化；带扩展名时保留扩展名（截断基名部分）。
    /// 目录段与文件名段都经本方法，占位符替换前的模板前缀（如 [P##]）不在输入内，
    /// 逐段独立截断天然不破坏多P目录结构。
    /// </summary>
    public static string GetValidFileName(string input, string re = "_", bool filterSlash = false, int maxBaseNameLength = 100)
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
        // 基名+扩展名截断：只在超长时生效。扩展名（如 .mp4）保留，只截基名部分，
        // 避免混流/播放按扩展名识别类型时被破坏。基名未超限时不截——总长略超
        // maxBaseNameLength 但单组件仍在 Windows 255 上限内，无 PathTooLong 风险。
        if (title.Length > maxBaseNameLength)
        {
            string ext = Path.GetExtension(title);
            if (ext.Length is > 0 and <= 10)
            {
                int baseLen = title.Length - ext.Length;
                if (baseLen > maxBaseNameLength)
                    title = title[..(maxBaseNameLength - ext.Length)] + ext;
            }
            else
            {
                title = title[..maxBaseNameLength];
            }
        }
        // Windows 保留名规则：CON/PRN/AUX/NUL/COM1..9/LPT1..9 后跟任意扩展名仍然保留
        // （CON.txt、CON.foo 在 Windows 上同样无法创建）。因此按基名匹配，
        // 而不是只匹配完整名称——否则 "CON.txt" 会漏过校验而在 Windows 上报错。
        var nameWithoutExt = Path.GetFileNameWithoutExtension(title);
        if (ReservedNames.Contains(title) || ReservedNames.Contains(nameWithoutExt))
            title = "_" + title;
        // Windows 不允许文件/目录名以点或空格结尾（会静默剥离/创建失败）：
        // 标题 "video." 或 "video " 在 Windows 上 File.Create/目录创建直接失败。
        // 仅当去掉尾随点/空格后名称仍非空时裁剪（纯 "." / ".." / "..." 已在上方
        // 无效字符替换中处理，不会到达这里；防御性再兜底）。去掉后若为空（如
        // 输入全为点），前缀下划线保证有合法基名。
        var trimmed = title.TrimEnd('.', ' ');
        if (trimmed.Length == 0)
            title = "_" + title;
        else if (trimmed != title)
            title = trimmed;
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

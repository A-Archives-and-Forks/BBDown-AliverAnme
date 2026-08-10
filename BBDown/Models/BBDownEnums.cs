using System;
using System.Linq;

namespace BBDown;

public enum BBDownDanmakuFormat
{
    Xml,
    Ass,
}

public static class BBDownDanmakuFormatInfo
{
    // 默认
    public static BBDownDanmakuFormat[] DefaultFormats = [BBDownDanmakuFormat.Xml, BBDownDanmakuFormat.Ass];
    public static string[] DefaultFormatsNames = DefaultFormats.Select(f => f.ToString().ToLowerInvariant()).ToArray();
    // 可选项
    public static string[] AllFormatNames = Enum.GetNames(typeof(BBDownDanmakuFormat)).Select(f => f.ToLowerInvariant()).ToArray();

    public static BBDownDanmakuFormat FromFormatName(string formatName)
    {
        return formatName switch
        {
            "xml" => BBDownDanmakuFormat.Xml,
            "ass" => BBDownDanmakuFormat.Ass,
            _ => BBDownDanmakuFormat.Xml,
        };
    }
}

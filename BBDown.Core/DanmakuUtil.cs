using System.Text;
using System.Xml;

namespace BBDown.Core;

public static class DanmakuUtil
{
    private const int MONITOR_WIDTH = 1920;         //渲染字幕时的渲染范围的高度
    private const int MONITOR_HEIGHT = 1080;        //渲染字幕时的渲染范围的高度
    private const int FONT_SIZE = 40;               //字体大小
    private const double MOVE_SPEND_TIME = 8.00;    //单条条滚动弹幕存在时间（控制速度）
    private const double TOP_SPEND_TIME = 4.00;     //单条顶部或底部弹幕存在时间
    private const int PROTECT_LENGTH = 50;          //滚动弹幕屏占百分比
    public static readonly DanmakuComparer comparer = new();

    /*public static async Task DownloadAsync(Page p, string xmlPath, bool aria2c, string aria2cProxy)
    {
        string danmakuUrl = "https://comment.bilibili.com/" + p.cid + ".xml";
        await DownloadFile(danmakuUrl, xmlPath, aria2c, aria2cProxy);
    }*/

    public static DanmakuItem[]? ParseXml(string xmlPath)
    {
        // 解析xml文件
        XmlDocument xmlFile = new();
        XmlReaderSettings settings = new()
        {
            IgnoreComments = true//忽略文档里面的注释
        };
        var danmakus = new List<DanmakuItem>();
        using (var reader = XmlReader.Create(xmlPath, settings))
        {
            try
            {
                xmlFile.Load(reader);
            }
            catch (Exception ex) when (ex is XmlException or IOException)
            {
                Logger.LogDebug("解析字幕xml时出现异常: {0}", ex.ToString());
                return null;
            }
        }

        XmlNode? rootNode = xmlFile.SelectSingleNode("i");
        if (rootNode != null)
        {
            XmlElement rootElement = (XmlElement)rootNode;
            XmlNodeList? dNodeList = rootElement.SelectNodes("d");
            if (dNodeList != null)
            {
                foreach (XmlNode node in dNodeList)
                {
                    XmlElement dElement = (XmlElement)node;
                    string attr = dElement.GetAttribute("p").ToString();
                    if (attr != null)
                    {
                        string[] vs = attr.Split(',');
                        if (vs.Length >= 8)
                        {
                            DanmakuItem danmaku = new(vs, dElement.InnerText);
                            danmakus.Add(danmaku);
                        }
                    }
                }
            }
        }
        return danmakus.ToArray();
    }

    /// <summary>
    /// 按关键词/发送者 midHash 过滤弹幕：任一过滤条件命中即丢弃。
    /// 关键词黑名单用于去掉广告/无关弹幕；B站弹幕 XML 不含用户 UID，
    /// 只有发送者 midHash（p 属性 index 6），按用户过滤即匹配该哈希。
    /// </summary>
    public static DanmakuItem[] Filter(DanmakuItem[] danmakus, string? keywords, string? userIds)
    {
        if (danmakus.Length == 0) return danmakus;
        if (string.IsNullOrWhiteSpace(keywords) && string.IsNullOrWhiteSpace(userIds)) return danmakus;

        var kw = (keywords ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var uids = (userIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return danmakus.Where(d =>
        {
            if (uids.Length > 0 && d.MidHash.Length > 0 && uids.Contains(d.MidHash)) return false;
            if (kw.Length > 0 && kw.Any(k => d.Content.Contains(k, StringComparison.Ordinal))) return false;
            return true;
        }).ToArray();
    }

    /// <summary>
    /// 保存为ASS字幕文件
    /// </summary>
    /// <param name="danmakus">弹幕</param>
    /// <param name="outputPath">保存路径</param>
    /// <returns></returns>
    public static async Task SaveAsAssAsync(DanmakuItem[] danmakus, string outputPath)
    {
        var sb = new StringBuilder();
        // ASS字幕文件头
        sb.AppendLine("[Script Info]");
        sb.AppendLine("Script Updated By: BBDown(https://github.com/AliverAnme/BBDown)");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {MONITOR_WIDTH}");
        sb.AppendLine($"PlayResY: {MONITOR_HEIGHT}");
        sb.AppendLine($"Aspect Ratio: {MONITOR_WIDTH}:{MONITOR_HEIGHT}");
        sb.AppendLine("Collisions: Normal");
        sb.AppendLine("WrapStyle: 2");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine("YCbCr Matrix: TV.601");
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: BBDOWN_Style, 黑体, {FONT_SIZE}, &H00FFFFFF, &H00FFFFFF, &H00000000, &H00000000, 0, 0, 0, 0, 100, 100, 0.00, 0.00, 1, 2, 0, 7, 0, 0, 0, 0");
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        PositionController controller = new();   // 弹幕位置控制器
        Array.Sort(danmakus, comparer);
        foreach (DanmakuItem danmaku in danmakus)
        {
            int height = controller.UpdatePosition(danmaku.DanmakuMode, danmaku.Second, danmaku.Content.Length);
            if (height == -1) continue;
            string effect = "";
            effect += danmaku.DanmakuMode switch
            {
                3 => $"\\an8\\pos({MONITOR_WIDTH / 2}, {MONITOR_HEIGHT - FONT_SIZE - height})",
                2 => $"\\an8\\pos({MONITOR_WIDTH / 2}, {height})",
                _ => $"\\move({MONITOR_WIDTH}, {height}, {-danmaku.Content.Length * FONT_SIZE}, {height})",
            };
            if (danmaku.Color.Length == 6 && danmaku.Color != "FFFFFF")
            {
                // ASS 颜色格式为 &HBBGGRR（BGR），而 B 站弹幕 color 是 #RRGGBB（RGB）。
                // 直接拼接会让红蓝互换（红弹幕渲染成蓝）；反转字节序后才能还原正确颜色。
                // &H 前缀是 ASS 颜色规范的必需部分：缺了它 FF0000 会被当作十进制而非十六进制。
                string bgr = danmaku.Color[4..] + danmaku.Color[2..4] + danmaku.Color[..2];
                effect += $"\\c&H{bgr}&";
            }
            sb.AppendLine($"Dialogue: 2,{danmaku.StartTime},{danmaku.EndTime},BBDOWN_Style,,0000,0000,0000,,{{{effect}}}{EscapeAssText(danmaku.Content)}");
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 中和弹幕正文里的 ASS 控制字符。
    /// 弹幕内容由其他用户提供：花括号在 ASS 中界定样式覆盖标签块，
    /// 一条 <c>{\c&amp;HFF0000&amp;}</c> 就能改掉后续渲染；换行则会让正文
    /// 后半段脱离 Dialogue 行，变成解析器无法识别的孤立行。
    /// 花括号替换为全角字符以保留可读性，换行折叠为 ASS 自身的 \N。
    /// </summary>
    private static string EscapeAssText(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;

        var sb = new StringBuilder(content.Length);
        for (var i = 0; i < content.Length; i++)
        {
            switch (content[i])
            {
                case '{': sb.Append('｛'); break;
                case '}': sb.Append('｝'); break;
                case '\r':
                    // 吃掉 \r\n 中的 \n，避免产生两个 \N
                    if (i + 1 < content.Length && content[i + 1] == '\n') i++;
                    sb.Append("\\N");
                    break;
                case '\n': sb.Append("\\N"); break;
                default: sb.Append(content[i]); break;
            }
        }
        return sb.ToString();
    }

    protected class PositionController
    {
        readonly int maxLine = MONITOR_HEIGHT * PROTECT_LENGTH / FONT_SIZE / 100;    //总行数
        // 三个位置的弹幕队列，记录弹幕结束时间

        readonly List<double> moveQueue = new();
        readonly List<double> topQueue = new();
        readonly List<double> bottomQueue = new();

        public PositionController()
        {
            for (int i = 0; i < maxLine; i++)
            {
                moveQueue.Add(0.00);
                topQueue.Add(0.00);
                bottomQueue.Add(0.00);
            }
        }

        public int UpdatePosition(int type, double time, int length)
        {
            // 获取可用位置
            List<double> vs;
            double displayTime = TOP_SPEND_TIME;
            if (type == POS_BOTTOM)
            {
                vs = bottomQueue;
            }
            else if (type == POS_TOP)
            {
                vs = topQueue;
            }
            else
            {
                vs = moveQueue;
                displayTime = MOVE_SPEND_TIME * (length + 5) * FONT_SIZE / (MONITOR_WIDTH + (length * MOVE_SPEND_TIME));
            }
            for (int i = 0; i < maxLine; i++)
            {
                if (time >= vs[i])
                {   // 此条弹幕已结束，更新该位置信息
                    vs[i] = time + displayTime;
                    return i * FONT_SIZE;
                }
            }
            return -1;
        }
    }

    public class DanmakuItem
    {
        public DanmakuItem(string[] attrs, string content)
        {
            DanmakuMode = attrs[1] switch
            {
                "4" => POS_BOTTOM,
                "5" => POS_TOP,
                _ => POS_MOVE,
            };
            try
            {
                // B站弹幕时间固定为点号小数（如 "12.345"）：在 de-DE 等小数点分隔符为
                // 逗号的区域，CurrentCulture 会把分组符吞掉解析成 12345，时间轴整体错乱
                double second = double.Parse(attrs[0], System.Globalization.CultureInfo.InvariantCulture);
                Second = second;
                StartTime = ComputeTime(second);
                EndTime = ComputeTime(second + (DanmakuMode == 1 ? MOVE_SPEND_TIME : TOP_SPEND_TIME));
            }
            catch (Exception e) when (e is FormatException or OverflowException)
            {
                Logger.LogDebug("弹幕时间解析失败: {0}", e.Message);
            }
            FontSize = attrs[2];
            try
            {
                int colorD = int.Parse(attrs[3], System.Globalization.CultureInfo.InvariantCulture);
                Color = string.Format("{0:X6}", colorD);
            }
            // 与上方时间解析保持一致：超范围的颜色值同样只应降级为默认色，
            // 而不是让整个弹幕文件的生成失败。用 LogDebug 避免大量异常弹幕刷屏。
            catch (Exception e) when (e is FormatException or OverflowException)
            {
                Logger.LogDebug("弹幕颜色解析失败: {0}", e.Message);
            }
            Timestamp = attrs[4];
            // B站弹幕 p 属性: 进度,模式,字号,颜色,ctime,弹幕池,用户midHash,rowID。
            // index 6 才是发送者标识（哈希）；Timestamps 存的是 ctime（发送时间戳），
            // 若用它做"按用户过滤"会永远匹配不上。
            MidHash = attrs.Length > 6 ? attrs[6] : "";
            Content = content;
        }
        private static string ComputeTime(double second)
        {
            int hour = (int)second / 3600;
            int minute = (int)(second - (hour * 3600)) / 60;
            second -= (hour * 3600) + (minute * 60);
            return hour.ToString() + string.Format(":{0:D2}:", minute) + string.Format("{0:00.00}", second);
        }
        public string Content { get; set; } = "";
        // 弹幕内容
        public string StartTime { get; set; } = "";
        // 出现时间
        public double Second { get; set; } = 0.00;
        // 出现时间（秒为单位）
        public string EndTime { get; set; } = "";
        // 消失时间
        public int DanmakuMode { get; set; } = POS_MOVE;
        // 弹幕类型
        public string FontSize { get; set; } = "";
        // 字号
        public string Color { get; set; } = "";
        // 颜色
        public string Timestamp { get; set; } = "";
        // 时间戳
        public string MidHash { get; set; } = "";
        // 发送者标识（弹幕 XML p 属性 index 6）
    }

    public class DanmakuComparer : IComparer<DanmakuItem>
    {
        public int Compare(DanmakuItem? x, DanmakuItem? y)
        {
            if (x == null) return -1;
            if (y == null) return 1;
            return x.Second.CompareTo(y.Second);
        }
    }

    private const int POS_MOVE = 1;     //滚动弹幕
    private const int POS_TOP = 2;      //顶部弹幕
    private const int POS_BOTTOM = 3;   //底部弹幕
}

using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Entity;

public class VInfo
{
    /// <summary>
    /// 视频标题
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// 视频描述
    /// </summary>
    public required string Desc { get; set; }

    /// <summary>
    /// 视频封面
    /// </summary>
    public required string Pic { get; set; }

    /// <summary>
    /// 视频发布时间
    /// </summary>
    public required long PubTime { get; set; }
    public bool IsBangumi { get; set; }
    public bool IsCheese { get; set; }

    /// <summary>
    /// 番剧是否完结
    /// </summary>
    public bool IsBangumiEnd { get; set; }

    /// <summary>
    /// 视频index 用于番剧或课程判断当前选择的是第几集
    /// </summary>
    public string? Index { get; set; }

    /// <summary>
    /// 视频分P信息
    /// </summary>
    public required List<Page> PagesInfo { get; set; }

    /// <summary>
    /// 是否为互动视频
    /// </summary>
    public bool IsSteinGate { get; set; }

    /// <summary>
    /// 是否为UP主充电专属视频。
    /// 对应 view 接口的 is_upower_exclusive 字段。
    /// </summary>
    public bool IsUpowerExclusive { get; set; }

    /// <summary>
    /// 充电专属视频是否对当前身份提供试看片段。
    /// 对应 view 接口的 is_upower_preview 字段。
    /// </summary>
    public bool IsUpowerPreview { get; set; }

    /// <summary>
    /// 当前身份是否具备该充电专属视频的完整播放权限。
    /// 对应 view 接口的 is_upower_play 字段：未充电时为 false，
    /// 此时 playurl 仍会返回 code=0，但只给出试看片段。
    /// </summary>
    public bool IsUpowerPlay { get; set; }
}
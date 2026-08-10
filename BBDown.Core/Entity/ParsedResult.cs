using static BBDown.Core.Entity.Entity;

namespace BBDown.Core.Entity;

public class ParsedResult
{
    public string WebJsonString { get; set; } = "";
    public List<Video> VideoTracks { get; set; } = new();
    public List<Audio> AudioTracks { get; set; } = new();
    public List<Audio> BackgroundAudioTracks { get; set; } = new();
    public List<AudioMaterialInfo> RoleAudioList { get; set; } = new();
    public List<ViewPoint> ExtraPoints { get; set; } = new();
    // ⬇⬇⬇⬇⬇ FOR FLV ⬇⬇⬇⬇⬇
    public List<string> Clips { get; set; } = new();
    public List<string> Dfns { get; set; } = new();
    /// <summary>
    /// 本次解析实际拿到的媒体时长(秒)。
    /// 充电专属视频在无权限时依然返回 code=0，且 timelength 谎报完整时长，
    /// 只有各分段累加出的真实长度会暴露出这是试看片段，故单独记录以便交叉校验。
    /// </summary>
    public int ActualDurationSec { get; set; }

    // ⬇⬇⬇⬇⬇ DRM ⬇⬇⬇⬇⬇
    public bool IsDrm { get; set; }
    public int DrmTechType { get; set; }
    public string DrmType { get; set; } = "";
    public string KidHex { get; set; } = "";
    public string KeyHex { get; set; } = "";
    public string PsshBase64 { get; set; } = "";
}

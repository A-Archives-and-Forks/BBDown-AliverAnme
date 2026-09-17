using BBDown.Core.Util;
using System.Diagnostics.CodeAnalysis;

namespace BBDown.Core.Entity;

public static class Entity
{
    public class Page
    {
        public required int index;
        // RF-73：aid/cid/epid 逐字来自 API 响应（Fetcher 的 GetValueAsStringSafe("id") 等，无数字校验），
        // 又直接拼入工作区路径与 <aid>/<cid> 占位符。属性 setter 统一经 SanitizePathSegment 净化，
        // 单一收口杜绝镜像站/中间人下发 "..\\..\\tmp\\x" 类值导致路径穿越出 --work-dir。
        // 纯数字/ BV 号等合法值为恒等变换，不影响 RF-48 的 bvid 非数字回退。
        private string _aid = "";
        public required string aid
        {
            get => _aid;
            set => _aid = PathUtil.SanitizePathSegment(value);
        }
        private string _cid = "";
        public required string cid
        {
            get => _cid;
            set => _cid = PathUtil.SanitizePathSegment(value);
        }
        private string _epid = "";
        public required string epid
        {
            get => _epid;
            set => _epid = PathUtil.SanitizePathSegment(value);
        }
        public required string title;
        public required int dur;
        public required string res;
        public required long pubTime;
        public string? cover;
        public string? desc;
        public string? ownerName;
        public string? ownerMid;
        public string bvid
        {
            get
            {
                if (long.TryParse(aid, out var aidNum))
                {
                    // RF-48：服务器可控 aid（收藏夹/合集/空间条目 id 可为 "0"/负数/超界大数）
                    // 经 Encode 范围校验抛 ArgumentOutOfRangeException——不在下载页两级 catch
                    // 过滤器内（Download.cs 注释三次明言 AOORE 须逐点防护），会中止整批。
                    // 与下方"非纯数字"分支同语义：编码失败回落原始 aid。
                    try
                    {
                        return BilibiliBvConverter.Encode(aidNum);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return aid;
                    }
                }
                // aid 非纯数字（可能本就是 BV 号或自定义标识）：无法编码，直接返回原始 aid。
                // 注意：此处 fallback 返回的是原始字符串而非真实 BV——调用方不应假设 bvid 恒为
                // 规范化 BV 号（如仅用于展示/匹配时它等价 aid；用于请求 API 时请用 aid 字段）。
                return aid;
            }
        }
        public List<ViewPoint> points = new();

        [SetsRequiredMembers]
        public Page(int index, string aid, string cid, string epid, string title, int dur, string res, long pubTime)
        {
            this.aid = aid;
            this.index = index;
            this.cid = cid;
            this.epid = epid;
            this.title = title;
            this.dur = dur;
            this.res = res;
            this.pubTime = pubTime;
        }

        [SetsRequiredMembers]
        public Page(int index, string aid, string cid, string epid, string title, int dur, string res, long pubTime, string cover)
        {
            this.aid = aid;
            this.index = index;
            this.cid = cid;
            this.epid = epid;
            this.title = title;
            this.dur = dur;
            this.res = res;
            this.pubTime = pubTime;
            this.cover = cover;
        }

        [SetsRequiredMembers]
        public Page(int index, string aid, string cid, string epid, string title, int dur, string res, long pubTime, string cover, string desc)
        {
            this.aid = aid;
            this.index = index;
            this.cid = cid;
            this.epid = epid;
            this.title = title;
            this.dur = dur;
            this.res = res;
            this.pubTime = pubTime;
            this.cover = cover;
            this.desc = desc;
        }

        [SetsRequiredMembers]
        public Page(int index, string aid, string cid, string epid, string title, int dur, string res, long pubTime, string cover, string desc, string ownerName, string ownerMid)
        {
            this.aid = aid;
            this.index = index;
            this.cid = cid;
            this.epid = epid;
            this.title = title;
            this.dur = dur;
            this.res = res;
            this.pubTime = pubTime;
            this.cover = cover;
            this.desc = desc;
            this.ownerName = ownerName;
            this.ownerMid = ownerMid;
        }

        [SetsRequiredMembers]
        public Page(int index, Page page)
        {
            this.index = index;
            this.aid = page.aid;
            this.cid = page.cid;
            this.epid = page.epid;
            this.title = page.title;
            this.dur = page.dur;
            this.res = page.res;
            this.pubTime = page.pubTime;
            this.cover = page.cover;
            this.desc = page.desc;
            this.ownerName = page.ownerName;
            this.ownerMid = page.ownerMid;
            this.points = page.points;
        }

        public override bool Equals(object? obj)
        {
            return obj is Page page &&
                   aid == page.aid &&
                   cid == page.cid &&
                   epid == page.epid;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(aid, cid, epid);
        }
    }

    public class ViewPoint
    {
        public required string title;
        public required int start;
        public required int end;
    }

    public class Video
    {
        public required string id;
        public required string dfn;
        public required string baseUrl;
        public string? res;
        public string? fps;
        public required string codecs;
        public long bandwidth;
        public int dur;
        public double size;

        public override bool Equals(object? obj)
        {
            return obj is Video video &&
                   id == video.id &&
                   dfn == video.dfn &&
                   res == video.res &&
                   fps == video.fps &&
                   codecs == video.codecs &&
                   bandwidth == video.bandwidth &&
                   dur == video.dur;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(id, dfn, res, fps, codecs, bandwidth, dur);
        }
    }

    public class Audio
    {
        public required string id;
        public required string dfn;
        public required string baseUrl;
        public required string codecs;
        public required long bandwidth;
        public required int dur;

        // E-AC-3 => EAC3
        // RF-60：ToUpperInvariant——tr-TR 等区域下含 'i' 的服务器可控 codecs 串经
        // 文化敏感 ToUpper() 变 'İ'（U+0130），选轨优先级查表失败静默退化。
        public string shortCodecs => codecs.ToUpperInvariant().Replace("-", string.Empty);

        public override bool Equals(object? obj)
        {
            return obj is Audio audio &&
                   id == audio.id &&
                   dfn == audio.dfn &&
                   codecs == audio.codecs &&
                   bandwidth == audio.bandwidth &&
                   dur == audio.dur;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(id, dfn, codecs, bandwidth, dur);
        }
    }

    public class Subtitle
    {
        public required string lan;
        public required string url;
        public required string path;
    }

    public class Clip
    {
        public required int index;
        public required long from;
        public required long to;
    }

    public class AudioMaterial
    {
        public required string title;
        public required string personName;
        public required string path;

        [SetsRequiredMembers]
        public AudioMaterial(string title, string personName, string path)
        {
            this.title = title;
            this.personName = personName;
            this.path = path;
        }

        [SetsRequiredMembers]
        public AudioMaterial(AudioMaterialInfo audioMaterialInfo)
        {
            this.title = audioMaterialInfo.title;
            this.personName = audioMaterialInfo.personName;
            this.path = audioMaterialInfo.path;
        }
    }

    public class AudioMaterialInfo
    {
        public required string title;
        public required string personName;
        public required string path;
        public required List<Audio> audio;
    }
}

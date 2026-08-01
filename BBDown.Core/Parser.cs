using System.Text;
using BBDown.Core.Util;
using System.Text.RegularExpressions;
using System.Text.Json;
using static BBDown.Core.Entity.Entity;
using System.Security.Cryptography;
using BBDown.Core.Entity;

namespace BBDown.Core;

public static partial class Parser
{
    public static string WbiSign(string api)
    {
        return $"{api}&w_rid=" + Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(api + Config.Current.Wbi)));
    }

    private static async Task<string> GetPlayJsonAsync(string encoding, string aidOri, string aid, string cid, string epId, bool tvApi, bool intl, bool appApi, bool wantDrm, string qn = "0")
    {
        Logger.LogDebug("aid={0},cid={1},epId={2},tvApi={3},IntlApi={4},appApi={5},qn={6}", aid, cid, epId, tvApi, intl, appApi, qn);

        if (intl) return await GetPlayJsonAsync(aid, cid, epId, qn);


        bool cheese = aidOri.StartsWith("cheese:");
        bool bangumi = cheese || aidOri.StartsWith("ep:");
        Logger.LogDebug("bangumi={0},cheese={1}", bangumi, cheese);

        if (appApi) return await AppHelper.DoReqAsync(aid, cid, epId, qn, bangumi, encoding, Config.Current.Token);

        string prefix = tvApi ? bangumi ? $"{Config.Current.TvHost}/pgc/player/api/playurltv" : $"{Config.Current.TvHost}/x/tv/playurl"
            : bangumi ? $"{Config.Current.Host}/pgc/player/web/v2/playurl" : "api.bilibili.com/x/player/wbi/playurl";
        prefix = $"https://{prefix}?";

        string api;
        if (tvApi)
        {
            StringBuilder apiBuilder = new();
            if (Config.Current.Token != "") apiBuilder.Append($"access_key={Config.Current.Token}&");
            apiBuilder.Append($"appkey=4409e2ce8ffd12b8&build=106500&cid={cid}&device=android");
            if (bangumi) apiBuilder.Append($"&ep_id={epId}&expire=0");
            apiBuilder.Append($"&fnval=4048&fnver=0&fourk=1&mid=0&mobi_app=android_tv_yst");
            apiBuilder.Append($"&object_id={aid}&platform=android&playurl_type=1&qn={qn}&ts={GetTimeStamp(true)}");
            api = $"{prefix}{apiBuilder}&sign={GetSign(apiBuilder.ToString(), false)}";
        }
        else
        {
            // 尝试提高可读性
            StringBuilder apiBuilder = new();
            apiBuilder.Append($"support_multi_audio=true&from_client=BROWSER&avid={aid}&cid={cid}&fnval=4048&fnver=0&fourk=1");
            if (Config.Current.Area != "") apiBuilder.Append($"&access_key={Config.Current.Token}&area={Config.Current.Area}");
            apiBuilder.Append($"&otype=json&qn={qn}");
            if (bangumi) apiBuilder.Append($"&module=bangumi&ep_id={epId}&session=");
            if (Config.Current.Cookie == "" && !wantDrm) apiBuilder.Append("&try_look=1");
            if (wantDrm) apiBuilder.Append("&drm_tech_type=2");
            apiBuilder.Append($"&wts={GetTimeStamp(true)}");
            api = prefix + (bangumi ? apiBuilder.ToString() : WbiSign(apiBuilder.ToString()));
        }

        //课程接口
        if (cheese) api = api.Replace("/pgc/player/web/v2/playurl", "/pugv/player/web/playurl");

        //Console.WriteLine(api);
        string webJson = await HTTPUtil.GetWebSourceAsync(api);
        //以下情况从网页源代码尝试解析
        if (webJson.Contains("\"大会员专享限制\""))
        {
            Logger.Log("此视频需要大会员，您大概率需要登录一个有大会员的账号才可以下载，尝试从网页源码解析");
            // 该回退只对番剧成立：UGC 的 epId 为空，构造 /bangumi/play/ep<空> 会拿到
            // 无效页面并被 PlayerJsonRegex 误替换成空/垃圾内容，导致后续解析失败
            if (!string.IsNullOrEmpty(epId))
            {
                string webUrl = "https://www.bilibili.com/bangumi/play/ep" + epId;
                string webSource = await HTTPUtil.GetWebSourceAsync(webUrl);
                webJson = PlayerJsonRegex().Match(webSource).Groups[1].Value;
            }
        }
        return webJson;
    }

    private static async Task<string> GetPlayJsonAsync(string aid, string cid, string epId, string qn, string code = "0")
    {
        bool isBiliPlus = Config.Current.Host != "api.bilibili.com";
        string api = $"https://{(isBiliPlus ? Config.Current.Host : "api.biliintl.com")}/intl/gateway/v2/ogv/playurl?";

        StringBuilder paramBuilder = new();
        if (Config.Current.Token != "") paramBuilder.Append($"access_key={Config.Current.Token}&");
        paramBuilder.Append($"aid={aid}");
        if (isBiliPlus) paramBuilder.Append($"&appkey=7d089525d3611b1c&area={(Config.Current.Area == "" ? "th" : Config.Current.Area)}");
        paramBuilder.Append($"&cid={cid}&ep_id={epId}&platform=android&prefer_code_type={code}&qn={qn}");
        if (isBiliPlus) paramBuilder.Append($"&ts={GetTimeStamp(true)}");

        paramBuilder.Append("&s_locale=zh_SG");
        string param = paramBuilder.ToString();
        api += (isBiliPlus ? $"{param}&sign={GetSign(param, true)}" : param);

        string webJson = await HTTPUtil.GetWebSourceAsync(api);
        return webJson;
    }

    public static async Task<ParsedResult> ExtractTracksAsync(string aidOri, string aid, string cid, string epId, bool tvApi, bool intlApi, bool appApi, string encoding, bool wantDrm = false, string qn = "0")
    {
        ParsedResult parsedResult = new();

        //调用解析
        parsedResult.WebJsonString = await GetPlayJsonAsync(encoding, aidOri, aid, cid, epId, tvApi, intlApi, appApi, wantDrm, qn);

        Logger.LogDebug(parsedResult.WebJsonString);

        //intl接口需要两次请求(code=0和code=1)
        if (intlApi)
        {
            foreach (var code in new[] { "0", "1" })
            {
                if (code == "1")
                    parsedResult.WebJsonString = await GetPlayJsonAsync(aid, cid, epId, qn, code);

                using var intlJson = JsonDocument.Parse(parsedResult.WebJsonString);
                // intl 接口某次请求可能不返回 video_info / stream_list（code=0 与 code=1 返回结构不同），
                // GetPropertySafe 遇到缺失会抛 KeyNotFoundException 直接中断整次解析，
                // 这里逐级判空后跳过本次迭代，等待下一次请求
                var intlData = intlJson.RootElement.TryGetPropertySafe("data");
                if (intlData is not { ValueKind: JsonValueKind.Object }) continue;
                var videoInfo = intlData.Value.TryGetPropertySafe("video_info");
                if (videoInfo is not { ValueKind: JsonValueKind.Object }) continue;
                var streamList = videoInfo.Value.TryGetPropertySafe("stream_list");
                if (streamList is not { ValueKind: JsonValueKind.Array }) continue;
                int pDur = videoInfo.Value.GetInt32Safe("timelength") / 1000;
                var audioElements = videoInfo.Value.EnumerateArraySafe("dash_audio").ToList();

                foreach (var stream in streamList.Value.EnumerateArray())
                {
                    if (stream.TryGetProperty("dash_video", out JsonElement dashVideo))
                    {
                        if (dashVideo.GetValueAsStringSafe("base_url") != "")
                        {
                            var videoId = stream.GetPropertySafe("stream_info").GetValueAsStringSafe("quality");
                            var urlList = new List<string>() { dashVideo.GetValueAsStringSafe("base_url") };
                            urlList.AddRange(dashVideo.EnumerateArraySafe("backup_url").Select(i => i.ToString()));
                            Video v = new()
                            {
                                dur = pDur,
                                id = videoId,
                                dfn = AppSettings.QualityMap.GetValueOrDefault(videoId, $"未知({videoId})"),
                                bandwidth = dashVideo.GetInt64Safe("bandwidth") / 1000,
                                baseUrl = urlList.FirstOrDefault(i => !BaseUrlRegex().IsMatch(i), urlList.First()),
                                codecs = GetVideoCodec(dashVideo.GetValueAsStringSafe("codecid")),
                                size = dashVideo.GetDoubleSafe("size")
                            };
                            if (!parsedResult.VideoTracks.Contains(v)) parsedResult.VideoTracks.Add(v);
                        }
                    }
                }

                foreach (var node in audioElements)
                {
                    var urlList = new List<string>() { node.GetValueAsStringSafe("base_url") };
                    urlList.AddRange(node.EnumerateArraySafe("backup_url").Select(i => i.ToString()));
                    Audio a = new()
                    {
                        id = node.GetValueAsStringSafe("id"),
                        dfn = node.GetValueAsStringSafe("id"),
                        dur = pDur,
                        bandwidth = node.GetInt64Safe("bandwidth") / 1000,
                        baseUrl = urlList.FirstOrDefault(i => !BaseUrlRegex().IsMatch(i), urlList.First()),
                        codecs = "M4A"
                    };
                    if (!parsedResult.AudioTracks.Contains(a)) parsedResult.AudioTracks.Add(a);
                }
            }
            return parsedResult;
        }

        var respJson = JsonDocument.Parse(parsedResult.WebJsonString);
        var data = respJson.RootElement;
        ThrowIfPlayLimited(data);
        // UGC 的播放限制通过顶层业务 code 表达（区域限制 -86038、风控 -412、视频失效 -404 等），
        // 而 play_check 只在 pgc 响应的 result 节点出现、对 UGC 不可达，这里统一兜底
        ThrowIfBizError(data);
        // 根据API版本自动定位数据节点
        JsonElement root;
        if (data.TryGetProperty("result", out var resultElem) && resultElem.ValueKind == JsonValueKind.Object)
        {
            root = resultElem.TryGetProperty("video_info", out var vi) ? vi : resultElem;
        }
        else if (data.TryGetProperty("data", out var dataElem))
        {
            root = dataElem;
        }
        else
        {
            root = data;
        }

        bool bangumi = aidOri.StartsWith("ep:");

        if (root.TryGetProperty("dash", out _)) //dash
        {
            List<JsonElement>? audio = null;
            List<JsonElement>? video = null;
            List<JsonElement>? backgroundAudio = null;
            List<JsonElement>? roleAudio = null;
            int pDur = 0;

            if (root.TryGetProperty("dash", out var dashElem) && dashElem.TryGetProperty("duration", out var durElem))
                pDur = durElem.GetInt32();
            else if (root.TryGetProperty("timelength", out var tlElem))
                pDur = tlElem.GetInt32() / 1000;

            parsedResult.ActualDurationSec = pDur;

            // DRM metadata
            if (root.TryGetProperty("is_drm", out var isDrmElem))
                parsedResult.IsDrm = isDrmElem.GetBoolean();
            if (root.TryGetProperty("drm_tech_type", out var techElem))
                parsedResult.DrmTechType = techElem.GetInt32();
            if (root.TryGetProperty("drm_type", out var typeElem))
                parsedResult.DrmType = typeElem.GetString() ?? "";
            if (parsedResult.IsDrm) Logger.LogDebug("DRM detected: type={0}, tech={1}", parsedResult.DrmType, parsedResult.DrmTechType);

            //免二压视频需要重新请求
            for (int reparsePass = 0; reparsePass < 2; reparsePass++)
            {
            if (reparsePass == 1)
            {
                if (appApi) break; //只有非APP接口需要免二压
                parsedResult.WebJsonString = await GetPlayJsonAsync(encoding, aidOri, aid, cid, epId, tvApi, intlApi, appApi, wantDrm, GetMaxQn());
                respJson.Dispose();
                respJson = JsonDocument.Parse(parsedResult.WebJsonString);
                var newRoot = respJson.RootElement;
                root = newRoot.TryGetProperty("result", out var rr) && rr.ValueKind == JsonValueKind.Object && rr.TryGetProperty("video_info", out var vvii) ? vvii :
                       newRoot.TryGetProperty("result", out var rr2) && rr2.ValueKind == JsonValueKind.Object ? rr2 :
                       newRoot.TryGetProperty("data", out var dd) ? dd : newRoot;
            }
            if (root.TryGetProperty("dash", out var dash) && dash.TryGetProperty("video", out var vidArr))
                video = vidArr.EnumerateArray().ToList();
            if (root.TryGetProperty("dash", out dash) && dash.TryGetProperty("audio", out var audArr))
                audio = audArr.EnumerateArray().ToList();

            if (appApi && bangumi)
            {
                if (data.TryGetProperty("dubbing_info", out var dub) && dub.TryGetProperty("background_audio", out var bgArr))
                    backgroundAudio = bgArr.EnumerateArray().ToList();
                if (data.TryGetProperty("dubbing_info", out dub) && dub.TryGetProperty("role_audio_list", out var roleArr))
                    roleAudio = roleArr.EnumerateArray().ToList();
            }
            //处理杜比音频
            try
            {
                if (audio != null)
                {
                    if (!tvApi && root.GetPropertySafe("dash").TryGetProperty("dolby", out JsonElement dolby))
                    {
                        if (dolby.TryGetProperty("audio", out JsonElement db))
                        {
                            audio.AddRange(db.EnumerateArray());
                        }
                    }
                }
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException)
            { Logger.LogDebug("杜比音频解析失败: {0}", e.Message); }

            //处理Hi-Res无损
            try
            {
                if (audio != null)
                {
                    if (!tvApi && root.GetPropertySafe("dash").TryGetProperty("flac", out JsonElement hiRes))
                    {
                        if (hiRes.TryGetProperty("audio", out JsonElement db))
                        {
                            if (db.ValueKind != JsonValueKind.Null)
                                audio.Add(db);
                        }
                    }
                }
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException)
            { Logger.LogDebug("Hi-Res音频解析失败: {0}", e.Message); }

            if (video != null)
            {
                foreach (var node in video)
                {
                    var urlList = new List<string>() { node.GetValueAsStringSafe("base_url") };
                    if (node.TryGetProperty("backup_url", out JsonElement element) && element.ValueKind != JsonValueKind.Null)
                    {
                        urlList.AddRange(element.EnumerateArray().Select(i => i.ToString()));
                    }
                    var videoId = node.GetValueAsStringSafe("id");
                    Video v = new()
                    {
                        dur = pDur,
                        id = videoId,
                        dfn = AppSettings.QualityMap.GetValueOrDefault(videoId, $"未知({videoId})"),
                        bandwidth = node.GetInt64Safe("bandwidth") / 1000,
                        baseUrl = urlList.FirstOrDefault(i => !BaseUrlRegex().IsMatch(i), urlList.First()),
                        codecs = GetVideoCodec(node.GetValueAsStringSafe("codecid")),
                        size = node.GetDoubleSafe("size")
                    };
                    if (!tvApi && !appApi)
                    {
                        v.res = node.GetValueAsStringSafe("width") + "x" + node.GetValueAsStringSafe("height");
                        v.fps = node.GetValueAsStringSafe("frame_rate");
                    }
                    if (!parsedResult.VideoTracks.Contains(v)) parsedResult.VideoTracks.Add(v);
                }

                if (parsedResult.IsDrm && string.IsNullOrEmpty(parsedResult.KidHex))
                {
                    try
                    {
                        var firstVideo = video.FirstOrDefault();
                        if (firstVideo.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                            throw new InvalidOperationException("视频轨道为空，无法提取 DRM 信息");
                        if (firstVideo.TryGetProperty("bilidrm_uri", out var drmUri))
                        {
                            var uri = drmUri.GetString() ?? "";
                            var lastSlash = uri.LastIndexOf("//", StringComparison.Ordinal);
                            if (lastSlash >= 0)
                                parsedResult.KidHex = uri[(lastSlash + 2)..];
                        }
                        if (firstVideo.TryGetProperty("widevine_pssh", out var pssh) && pssh.GetString() is string ps && ps.Length > 0)
                            parsedResult.PsshBase64 = ps;
                    }
                    catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
                    { Logger.LogWarn($"DRM license info extraction error: {ex.Message}"); }
                }
            }

            } // end for reparsePass

            if (audio != null)
            {
                foreach (var node in audio)
                {
                    var urlList = new List<string>() { node.GetValueAsStringSafe("base_url") };
                    if (node.TryGetProperty("backup_url", out JsonElement element) && element.ValueKind != JsonValueKind.Null)
                    {
                        urlList.AddRange(element.EnumerateArray().Select(i => i.ToString()));
                    }
                    var audioId = node.GetValueAsStringSafe("id");
                    var codecs = node.GetValueAsStringSafe("codecs");
                    codecs = codecs switch
                    {
                        "mp4a.40.2" => "M4A",
                        "mp4a.40.5" => "M4A",
                        "ec-3" => "E-AC-3",
                        "fLaC" => "FLAC",
                        _ => codecs
                    };

                    parsedResult.AudioTracks.Add(new Audio()
                    {
                        id = audioId,
                        dfn = audioId,
                        dur = pDur,
                        bandwidth = node.GetInt64Safe("bandwidth") / 1000,
                        baseUrl = urlList.FirstOrDefault(i => !BaseUrlRegex().IsMatch(i), urlList.First()),
                        codecs = codecs
                    });
                }
            }

            if (backgroundAudio != null && roleAudio != null)
            {
                foreach (var node in backgroundAudio)
                {
                    var audioId = node.GetValueAsStringSafe("id");
                    var urlList = new List<string> { node.GetValueAsStringSafe("base_url") };
                    urlList.AddRange(node.EnumerateArraySafe("backup_url").Select(i => i.ToString()));
                    parsedResult.BackgroundAudioTracks.Add(new Audio()
                    {
                        id = audioId,
                        dfn = audioId,
                        dur = pDur,
                        bandwidth = node.GetInt64Safe("bandwidth") / 1000,
                        baseUrl = urlList.FirstOrDefault(i => !BaseUrlRegex().IsMatch(i), urlList.First()),
                        codecs = node.GetValueAsStringSafe("codecs")
                    });
                }

                foreach (var role in roleAudio)
                {
                    var roleAudioTracks = new List<Audio>();
                    foreach (var node in role.EnumerateArraySafe("audio"))
                    {
                        var audioId = node.GetValueAsStringSafe("id");
                        var urlList = new List<string> { node.GetValueAsStringSafe("base_url") };
                        urlList.AddRange(node.EnumerateArraySafe("backup_url").Select(i => i.ToString()));
                        roleAudioTracks.Add(new Audio()
                        {
                            id = audioId,
                            dfn = audioId,
                            dur = pDur,
                            bandwidth = node.GetInt64Safe("bandwidth") / 1000,
                            baseUrl = urlList.FirstOrDefault(i => !BaseUrlRegex().IsMatch(i), urlList.First()),
                            codecs = node.GetValueAsStringSafe("codecs")
                        });
                    }
                    parsedResult.RoleAudioList.Add(new AudioMaterialInfo()
                    {
                        title = role.GetValueAsStringSafe("title"),
                        personName = role.GetValueAsStringSafe("person_name"),
                        path = $"{aid}/{aid}.{cid}.{role.GetValueAsStringSafe("audio_id")}.m4a",
                        audio = roleAudioTracks
                    });
                }
            }
        }
        else if (root.TryGetProperty("durl", out _)) //flv
        {
            //默认以最高清晰度解析
            parsedResult.WebJsonString = await GetPlayJsonAsync(encoding, aidOri, aid, cid, epId, tvApi, intlApi, appApi, wantDrm, GetMaxQn());
            respJson.Dispose();
            respJson = JsonDocument.Parse(parsedResult.WebJsonString);
            var newData = respJson.RootElement;
            if (newData.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Object)
                root = r.TryGetProperty("video_info", out var vi) ? vi : r;
            else if (newData.TryGetProperty("data", out var d))
                root = d;
            else
                root = newData;
            string quality = "";
            string videoCodecid = "";
            string url = "";
            double size = 0;
            double length = 0;

            quality = root.GetValueAsStringSafe("quality");
            videoCodecid = root.GetValueAsStringSafe("video_codecid");
            //获取所有分段
            foreach (var node in root.EnumerateArraySafe("durl"))
            {
                parsedResult.Clips.Add(node.GetValueAsStringSafe("url"));
                size += node.GetDoubleSafe("size");
                length += node.GetDoubleSafe("length");
            }
            //TV模式可用清晰度
            if (root.TryGetProperty("qn_extras", out JsonElement qnExtras))
            {
                parsedResult.Dfns.AddRange(qnExtras.EnumerateArray().Select(node => node.GetValueAsStringSafe("qn")));
            }
            else if (root.TryGetProperty("accept_quality", out JsonElement acceptQuality)) //非tv模式可用清晰度
            {
                parsedResult.Dfns.AddRange(acceptQuality.EnumerateArray()
                    .Select(node => node.ToString())
                    .Where(_qn => !string.IsNullOrEmpty(_qn)));
            }

            // 分段累加出的长度才是本次真正能拿到的内容长度；
            // 充电试看片段正是在这里与 timelength 声称的完整时长产生分歧。
            parsedResult.ActualDurationSec = (int)length / 1000;

            Video v = new()
            {
                id = quality,
                dfn = AppSettings.QualityMap.GetValueOrDefault(quality, $"未知({quality})"),
                baseUrl = url,
                codecs = GetVideoCodec(videoCodecid),
                dur = (int)length / 1000,
                size = size
            };
            if (!parsedResult.VideoTracks.Contains(v)) parsedResult.VideoTracks.Add(v);
        }

        // 番剧片头片尾转分段信息, 预计效果: 正片? -> 片头 -> 正片 -> 片尾
        if (bangumi)
        {
            if (root.TryGetProperty("clip_info_list", out JsonElement clipList))
            {
                parsedResult.ExtraPoints.AddRange(clipList.EnumerateArray().Select(clip => new ViewPoint()
                    {
                        title = clip.GetValueAsStringSafe("toastText").Replace("即将跳过", ""),
                        start = clip.GetInt32Safe("start"),
                        end = clip.GetInt32Safe("end")
                    })
                );
                parsedResult.ExtraPoints.Sort((p1, p2) => p1.start.CompareTo(p2.start));
                var newPoints = new List<ViewPoint>();
                int lastEnd = 0;
                foreach (var point in parsedResult.ExtraPoints)
                {
                    if (lastEnd < point.start)
                        newPoints.Add(new ViewPoint() { title = "正片", start = lastEnd, end = point.start });
                    newPoints.Add(point);
                    lastEnd = point.end;
                }
                parsedResult.ExtraPoints = newPoints;
            }

        }

        respJson.Dispose();
        return parsedResult;
    }

    internal static void ThrowIfPlayLimited(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var result))
            return;

        if (!result.TryGetProperty("play_check", out var playCheck))
            return;

        var reason = playCheck.GetValueAsStringSafe("limit_play_reason");
        var detail = playCheck.GetValueAsStringSafe("play_detail");
        if (string.IsNullOrWhiteSpace(reason) && string.IsNullOrWhiteSpace(detail))
            return;

        var message = reason switch
        {
            "AREA_LIMIT" => "当前番剧/视频存在区域限制，接口返回不可播放",
            "PAY_LIMIT" => "当前番剧/视频存在付费限制，接口返回不可播放",
            "VIP_LIMIT" => "当前番剧/视频需要大会员权限，接口返回不可播放",
            "TIME_LOCK" => "当前番剧/视频尚未到可播放时间，接口返回不可播放",
            _ => "当前番剧/视频存在播放限制，接口返回不可播放"
        };

        throw new InvalidOperationException($"{message} (limit_play_reason={reason}, play_detail={detail})");
    }

    /// <summary>
    /// 对 playurl 响应统一兜底：顶层业务 code != 0（如 -86038 区域限制、-412 风控、-404 视频失效）
    /// 时以可读错误抛出不播放限制，避免 UGC 路径静默解析出空轨道后在下载阶段才失败。
    /// </summary>
    internal static void ThrowIfBizError(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number) return;
        if (!code.TryGetInt64(out var codeValue) || codeValue == 0) return;
        var message = root.GetValueAsStringSafe("message", $"接口返回错误码 {codeValue}");
        throw new InvalidOperationException($"接口返回错误: {message} (code={codeValue})");
    }

    /// <summary>
    /// 编码转换
    /// </summary>
    /// <param name="code"></param>
    /// <returns></returns>
    private static string GetVideoCodec(string code)
    {
        return code switch
        {
            "13" => "AV1",
            "12" => "HEVC",
            "7" => "AVC",
            _ => "UNKNOWN"
        };
    }

    private static string GetMaxQn()
    {
        var max = AppSettings.QualityMap.Keys
            .Select(k => int.TryParse(k, out var v) ? v : 0)
            .Max();
        return max.ToString();
    }

    private static string GetTimeStamp(bool bflag)
    {
        DateTimeOffset ts = DateTimeOffset.Now;
        return bflag ? ts.ToUnixTimeSeconds().ToString() : ts.ToUnixTimeMilliseconds().ToString();
    }

    private static string GetSign(string parameters, bool isBiliPlus)
    {
        string toEncode = parameters + (isBiliPlus ? "acd495b248ec528c2eed1e862d393126" : "59b43e04ad6965f34319062b478f83dd");
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(toEncode)));
    }

    [GeneratedRegex("window.__playinfo__=([\\s\\S]*?)<\\/script>")]
    private static partial Regex PlayerJsonRegex();
    [GeneratedRegex("http.*:\\d+")]
    private static partial Regex BaseUrlRegex();
}

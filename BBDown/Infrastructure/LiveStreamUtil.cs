using System.Buffers;
using System.Text;
using System.Text.Json;
using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown;

/// <summary>
/// B站直播流解析与录制。直播流是无限流：录制持续写入直到用户取消（Ctrl+C）或主播下播。
/// </summary>
public static class LiveStreamUtil
{
    /// <summary>
    /// 解析直播间信息与一条可录制的 flv 直播流地址。
    /// </summary>
    public static async Task<(string Url, string Title, string Uname, string RoomId)> ResolveAsync(string roomId, CancellationToken token = default)
    {
        if (!long.TryParse(roomId, out _))
            throw new ArgumentException($"直播间 ID 必须是数字，当前值: '{roomId}'");

        string infoApi = $"https://api.live.bilibili.com/room/v1/Room/get_info?room_id={roomId}";
        string infoJson = await HTTPUtil.GetWebSourceAsync(infoApi, token: token);
        using var infoDoc = JsonDocument.Parse(infoJson);
        var info = infoDoc.RootElement.GetPropertySafe("data");
        string title = info.GetValueAsStringSafe("title");
        if (title == "") title = $"直播间{roomId}";
        string uname = info.GetValueAsStringSafe("uname");
        if (info.GetInt32Safe("live_status") != 1)
            throw new InvalidOperationException($"直播间 {roomId} 当前未在直播");

        string playApi = "https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo" +
            $"?room_id={roomId}&protocol=0,1&format=0,1,2&codec=0,1&qn=10000&platform=web";
        string playJson = await HTTPUtil.GetWebSourceAsync(playApi, token: token);
        using var playDoc = JsonDocument.Parse(playJson);
        var data = playDoc.RootElement.GetPropertySafe("data");
        var streams = data.GetPropertySafe("playurl_info").GetPropertySafe("playurl").EnumerateArraySafe("stream");
        foreach (var stream in streams)
        {
            foreach (var format in stream.EnumerateArraySafe("format"))
            {
                if (format.GetValueAsStringSafe("format_name") != "flv") continue;
                foreach (var codec in format.EnumerateArraySafe("codec"))
                {
                    var baseUrl = codec.GetValueAsStringSafe("base_url");
                    if (baseUrl == "") continue;
                    foreach (var urlInfo in codec.EnumerateArraySafe("url_info"))
                    {
                        var host = urlInfo.GetValueAsStringSafe("host");
                        var extra = urlInfo.GetValueAsStringSafe("extra");
                        if (host == "") continue;
                        return (host + baseUrl + extra, title, uname, roomId);
                    }
                }
            }
        }
        throw new InvalidOperationException($"无法获取直播间 {roomId} 的可录制流地址");
    }

    /// <summary>
    /// 把直播流持续写入本地文件，直到流结束或取消。进度回调收到累计字节数。
    /// </summary>
    public static async Task DownloadToFileAsync(string url, string path, Action<long>? onProgress, CancellationToken token = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", HTTPUtil.UserAgent);
        req.Headers.TryAddWithoutValidation("Referer", "https://live.bilibili.com/");
        using var response = (await HTTPUtil.AppHttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token)).EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            long total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (read == 0) break; // 流结束（主播下播）
                await fs.WriteAsync(buffer.AsMemory(0, read), token);
                total += read;
                onProgress?.Invoke(total);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // 跨平台文件名安全：Path.GetInvalidFileNameChars 在 Unix 上只含 NUL 与 '/',
    // 但下载文件可能被复制到 Windows，因此把 Windows 的非法字符一并替换
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars()
        .Union(@"\/:*?""<>|".ToCharArray())
        .Distinct()
        .ToArray();

    /// <summary>把非法文件名字符替换为下划线，返回安全的文件名。</summary>
    public static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(InvalidFileNameChars.Contains(ch) ? '_' : ch);
        var s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "直播" : s;
    }
}

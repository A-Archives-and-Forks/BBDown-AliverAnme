using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using BBDown.Core.DRM;
using BBDown.Core.Entity;
using System.Diagnostics;

using BBDown.Core.Util;
using static BBDown.BBDownUtil;
using System.Text.Json;
using BBDown.Core;
namespace BBDown;

internal partial class Program
{
    private static async Task DecryptDrmAsync(ParsedResult parsed, string videoPath, string audioPath, MyOption myOption)
    {
        Logger.Log("检测到DRM加密，正在获取解密密钥...");

        parsed.KeyHex = myOption.DrmKeyHex ?? "";
        if (!string.IsNullOrEmpty(myOption.DrmKidHex))
            parsed.KidHex = myOption.DrmKidHex;

        if (!string.IsNullOrEmpty(parsed.KeyHex) && !string.IsNullOrEmpty(parsed.KidHex))
        {
            Logger.Log($"使用手动提供的密钥: KEY={parsed.KeyHex[..Math.Min(8, parsed.KeyHex.Length)]}...");
        }
        else
        {
            try
            {
            if (parsed.DrmTechType == 2)
            {
                if (!string.IsNullOrEmpty(parsed.PsshBase64))
                {
                    var wvd = !string.IsNullOrEmpty(myOption.WvdPath) && File.Exists(myOption.WvdPath)
                        ? myOption.WvdPath
                        : FindTool("device.wvd") ?? Path.Combine(AppContext.BaseDirectory, "device.wvd");
                    if (File.Exists(wvd))
                    {
                        var keyResult = await DrmDecryptor.GetKeyWidevineAsync(parsed.PsshBase64, wvd);
                        if (keyResult != null)
                        {
                            parsed.KeyHex = keyResult.Value.keyHex;
                            parsed.KidHex = keyResult.Value.kid;
                        }
                    }
                    else
                    {
                        Logger.LogWarn("Widevine DRM 需要 device.wvd 文件，请放置到程序目录");
                    }
                }
            }
            else
            {
                Logger.LogWarn("当前DRM类型不支持自动解密，请使用 --key --kid 手动提供密钥");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
        {
            Logger.LogWarn($"自动密钥提取异常: {ex.Message}");
        }

        if (string.IsNullOrEmpty(parsed.KeyHex))
        {
            Logger.LogWarn("============================================");
            Logger.LogWarn("自动密钥提取失败，文件将保持加密状态。");
            Logger.LogWarn("");
            Logger.LogWarn("解决方案：");
            Logger.LogWarn("  1. 确保 device.wvd 文件放置在程序目录下");
            Logger.LogWarn($"  2. 或手动指定: BBDown <url> --key <KEY_HEX> --kid {parsed.KidHex}");
            Logger.LogWarn("============================================");
            return;
        }
        }

        Logger.Log($"密钥获取成功: KEY={parsed.KeyHex[..Math.Min(8, parsed.KeyHex.Length)]}...");

        var mp4decrypt = !string.IsNullOrEmpty(myOption.Mp4decryptPath) && File.Exists(myOption.Mp4decryptPath)
            ? myOption.Mp4decryptPath
            : FindTool("mp4decrypt");
        if (string.IsNullOrEmpty(mp4decrypt))
        {
            Logger.LogError("未找到 mp4decrypt，请安装 Bento4 或通过 --mp4decrypt-path 指定路径");
            return;
        }

        if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
        {
            Logger.Log("解密视频流...");
            var tmpVideo = videoPath + ".dec";
            await RunDecryptAsync(mp4decrypt, parsed.KidHex, parsed.KeyHex, videoPath, tmpVideo);
            if (File.Exists(tmpVideo) && new FileInfo(tmpVideo).Length > 0)
            {
                File.Delete(videoPath);
                File.Move(tmpVideo, videoPath);
                Logger.Log("视频解密完成");
            }
        }

        if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
        {
            Logger.Log("解密音频流...");
            var tmpAudio = audioPath + ".dec";
            await RunDecryptAsync(mp4decrypt, parsed.KidHex, parsed.KeyHex, audioPath, tmpAudio);
            if (File.Exists(tmpAudio) && new FileInfo(tmpAudio).Length > 0)
            {
                File.Delete(audioPath);
                File.Move(tmpAudio, audioPath);
                Logger.Log("音频解密完成");
            }
        }
    }

    private static async Task RunDecryptAsync(string mp4decrypt, string kid, string key, string input, string output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = mp4decrypt,
            Arguments = $"--key {kid}:{key} \"{input}\" \"{output}\"",
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc == null) return;
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            var err = await stderrTask;
            // 失败必须抛出：原实现只 LogError 后继续，主流程会把仍处于加密状态或
            // 半损坏的输出当作解密成功，最终混流出无法播放的文件却报告成功。
            try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { }
            throw new InvalidOperationException($"mp4decrypt 解密失败 (code={proc.ExitCode}): {err}");
        }
    }

}

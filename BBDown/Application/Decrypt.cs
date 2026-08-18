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
    private static async Task DecryptDrmAsync(ParsedResult parsed, string videoPath, string audioPath, MyOption myOption, CancellationToken token = default)
    {
        Logger.Log("检测到DRM加密，正在获取解密密钥...");

        parsed.KeyHex = myOption.DrmKeyHex ?? "";
        if (!string.IsNullOrEmpty(myOption.DrmKidHex))
            parsed.KidHex = myOption.DrmKidHex;

        if (!string.IsNullOrEmpty(parsed.KeyHex) && !string.IsNullOrEmpty(parsed.KidHex))
        {
            // 手动密钥也不回显 key 材料：与 CkcDecryptor 的 A2 修复一致，只记 kid 与长度，
            // 否则 INFO 级日志会把 AES-128 密钥的 25% 写进持久日志文件
            Logger.Log($"使用手动提供的密钥: kid={parsed.KidHex}, key 长度={parsed.KeyHex.Length} hex 字符");
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

            // 取钥必须同时得到 Key 与 Kid：mp4decrypt 的 key-file 行格式是 "kid:key"，
            // Kid 为空会生成 ":key" 无效行导致解密失败或静默产出错误输出。
            // 仅检查 KeyHex 会放过 KidHex 为空的半成品（手动 --key 未配 --kid）。
            if (string.IsNullOrEmpty(parsed.KeyHex) || string.IsNullOrEmpty(parsed.KidHex))
            {
                // 用户显式请求了 DRM 解密（--decrypt-drm / --key / --kid）但取钥失败：
                // 若只是打印警告并 return，任务会继续把"仍是加密的流"当成功产物混流/交付，
                // 用户拿到加密文件却被告知下载成功。这里抛异常让调用方把任务标记为失败，
                // 而不是静默交付加密产物。
                throw new InvalidOperationException(
                    "DRM 解密密钥获取失败（Key 或 Kid 缺失），无法解密。" +
                    "请确保 device.wvd 位于程序目录（--wvd-path 可指定外部 WVD 文件）；" +
                    "若此前可解密而当前突然失败，常见原因是 device.wvd 的设备证书已被 B 站吊销/封禁，" +
                    "请更换新版 device.wvd 后重试，或使用 --key --kid 同时提供密钥。");
            }
        }

        Logger.Log($"密钥获取成功: kid={parsed.KidHex}, key 长度={parsed.KeyHex.Length} hex 字符");

        var mp4decrypt = !string.IsNullOrEmpty(myOption.Mp4decryptPath) && File.Exists(myOption.Mp4decryptPath)
            ? myOption.Mp4decryptPath
            : FindTool("mp4decrypt");
        if (string.IsNullOrEmpty(mp4decrypt))
        {
            // 与取钥失败一致：用户显式请求解密但没有解密器，若只记录错误并 return，
            // 加密流会被当成功产物交付。抛异常让任务标记失败。
            throw new InvalidOperationException(
                "未找到 mp4decrypt，无法解密 DRM 内容。请安装 Bento4 或通过 --mp4decrypt-path 指定路径。");
        }

        if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
        {
            Logger.Log("解密视频流...");
            var tmpVideo = videoPath + ".dec";
            await RunDecryptAsync(mp4decrypt, parsed.KidHex, parsed.KeyHex, videoPath, tmpVideo, token);
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
            await RunDecryptAsync(mp4decrypt, parsed.KidHex, parsed.KeyHex, audioPath, tmpAudio, token);
            if (File.Exists(tmpAudio) && new FileInfo(tmpAudio).Length > 0)
            {
                File.Delete(audioPath);
                File.Move(tmpAudio, audioPath);
                Logger.Log("音频解密完成");
            }
        }
    }

    private static async Task RunDecryptAsync(string mp4decrypt, string kid, string key, string input, string output, CancellationToken token = default)
    {
        // Write key to a temp file to avoid exposing it on the command line
        // (visible via ps aux / /proc/<pid>/cmdline to other local users)
        var keyFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(keyFile, $"{kid}:{key}", token);

            var psi = new ProcessStartInfo
            {
                FileName = mp4decrypt,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--key-file");
            psi.ArgumentList.Add(keyFile);
            psi.ArgumentList.Add(input);
            psi.ArgumentList.Add(output);

            using var proc = Process.Start(psi);
            if (proc is null)
                throw new InvalidOperationException($"mp4decrypt 无法启动: {mp4decrypt}（进程启动失败）");
            var stderrTask = proc.StandardError.ReadToEndAsync();
            try
            {
                // 解密无超时兜底会让进程无限挂起：用混流超时配置作上限（与外部进程执行器一致）。
                // 达到超时同样 Kill 进程树并抛错，不留下孤儿 mp4decrypt。
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, Core.Config.Current.MuxerTimeoutMinutes)));
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 用户取消或超时：进程仍在运行，必须 Kill 掉，避免留下孤儿 mp4decrypt。
                try { proc.Kill(true); } catch { /* 进程可能已自行退出 */ }
                // Kill 后 stderr 管道断裂，ReadToEndAsync 会结束；带超时兜底地等待并观察
                // stderrTask，避免它成为未观察的 faulted Task（旧实现直接 throw 跳过等待，
                // Kill 产生的 IOException 会在终结器/后续 GC 时以 UnobservedTaskException 泄漏）。
                // 清理路径的任何异常都不应掩盖主路径的取消异常，一律忽略。
                try { await stderrTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
                throw;
            }

            if (proc.ExitCode != 0)
            {
                var err = await stderrTask;
                try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { }
                throw new InvalidOperationException($"mp4decrypt 解密失败 (code={proc.ExitCode}): {err}");
            }
            // 进程退出 0 但未产出有效文件：静默忽略会让调用方保留原加密文件、
            // 任务却继续"解密成功"。这里把缺失输出当作失败抛出。
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
            {
                throw new InvalidOperationException("mp4decrypt 退出码为 0 但未产出有效的解密文件");
            }
        }
        finally
        {
            // Securely delete the temp key file
            try
            {
                if (File.Exists(keyFile))
                {
                    // Overwrite before delete to prevent recovery
                    await File.WriteAllTextAsync(keyFile, new string('\0', 64));
                    File.Delete(keyFile);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }

}

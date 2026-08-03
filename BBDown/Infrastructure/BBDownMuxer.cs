using System;
using BBDown.Core.Util;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using static BBDown.Core.Entity.Entity;
using System.IO;
using BBDown.Core;
using System.Runtime.InteropServices;

namespace BBDown;

static partial class BBDownMuxer
{
    public static string FFMPEG = "ffmpeg";
    public static string MP4BOX = "mp4box";

    private static int RunExe(string app, string args)
    {
        using Process p = new();
        p.StartInfo.FileName = app;
        p.StartInfo.Arguments = args;
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.CreateNoWindow = true;
        p.ErrorDataReceived += (_, output) =>
        {
            if (!string.IsNullOrWhiteSpace(output.Data))
                Logger.Log(output.Data);
        };
        p.OutputDataReceived += (_, output) =>
        {
            if (!string.IsNullOrWhiteSpace(output.Data))
                Logger.LogDebug(output.Data);
        };
        p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        int muxTimeoutMinutes = Core.Config.Current.MuxerTimeoutMinutes;
        if (!p.WaitForExit(muxTimeoutMinutes * 60_000))
        {
            try { p.Kill(); } catch { /* ignore kill failures */ }
            throw new TimeoutException($"{app} 混流操作超过 {muxTimeoutMinutes} 分钟未结束，已强制终止。请检查输入文件是否损坏或磁盘空间是否不足。");
        }
        return p.ExitCode;
    }

    private static string EscapeString(string str)
    {
        return string.IsNullOrEmpty(str) ? str : str.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static int MuxByMp4box(string url, string videoPath, string audioPath, string outPath, string desc, string title, string author, string episodeId, string pic, string lang, List<Subtitle>? subs, bool audioOnly, bool videoOnly, List<ViewPoint>? points)
    {
        // 与 ffmpeg 分支的 MuxAV 一致：多P/嵌套路径模板下输出目录可能尚不存在，
        // mp4box 打不开不存在的父目录下的输出文件，返回非零导致"合并失败"。
        // 杜比视界 + ffmpeg<5.0 会自动切到 mp4box，无弹幕的多P下载稳定踩中此缺陷。
        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);

        StringBuilder inputArg = new();
        StringBuilder metaArg = new();
        int nowId = 0;
        inputArg.Append(" -inter 500 -noprog ");
        if (!string.IsNullOrEmpty(videoPath))
        {
            inputArg.Append($" -add \"{videoPath}#trackID={(audioOnly && audioPath == "" ? "2" : "1")}:name=\" ");
            nowId++;
        }
        if (!string.IsNullOrEmpty(audioPath))
        {
            inputArg.Append($" -add \"{audioPath}:lang=\"{ (lang == "" ? "und" : lang) }\"\" ");
            nowId++;
        }
        string? metaFile = null;
        try
        {
            if (points != null && points.Any())
            {
                var meta = BBDownUtil.GetMp4boxMetaString(points);
                var baseDir = Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath);
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = ".";
                // 固定名 "chapters" 会让并发混流互相覆盖（后写者的章节被先写者读到）；
                // 用输出文件派生唯一名，并在结束后清理。
                metaFile = Path.Combine(baseDir, $"chapters-{Path.GetFileNameWithoutExtension(outPath)}");
                File.WriteAllText(metaFile, meta);
                inputArg.Append($" -chap  \"{metaFile}\"  ");
            }
            if (!string.IsNullOrEmpty(pic))
                metaArg.Append($":cover=\"{pic}\"");
            if (!string.IsNullOrEmpty(episodeId))
                metaArg.Append($":album=\"{title}\":title=\"{episodeId}\"");
            else
                metaArg.Append($":title=\"{title}\"");
            metaArg.Append($":sdesc=\"{desc}\"");
            metaArg.Append($":comment=\"{url}\"");
            metaArg.Append($":artist=\"{author}\"");

            if (subs != null)
            {
                for (int i = 0; i < subs.Count; i++)
                {
                    if (File.Exists(subs[i].path) && File.ReadAllText(subs[i].path!) != "")
                    {
                        nowId++;
                        var (subLangCode, subLangName) = SubUtil.GetSubtitleCode(subs[i].lan);
                        inputArg.Append($" -add \"{subs[i].path}#trackID=1:name=\"{EscapeString(subLangName)}\":hdlr=sbtl:lang=\"{EscapeString(subLangCode)}\"\" ");
                        inputArg.Append($" -udta {nowId}:type=name:str=\"{EscapeString(subLangName)}\" ");
                    }
                }
            }

            //----分析完毕
            var arguments = (Config.Current.DebugLog ? " -v " : "") + inputArg + (metaArg.ToString() == "" ? "" : " -itags tool=" + metaArg) + $" -new -- \"{outPath}\"";
            Logger.LogDebug("mp4box命令: {0}", arguments);
            return RunExe(MP4BOX, arguments);
        }
        finally
        {
            if (metaFile != null && File.Exists(metaFile))
            {
                try { File.Delete(metaFile); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logger.LogDebug("清理章节文件失败: {0}", ex.Message);
                }
            }
        }
    }

    public static int MuxAV(bool useMp4box, string bvid, string videoPath, string audioPath, List<AudioMaterial> audioMaterial, string outPath, string desc = "", string title = "", string author = "", string episodeId = "", string pic = "", string lang = "", List<Subtitle>? subs = null, bool audioOnly = false, bool videoOnly = false, List<ViewPoint>? points = null, long pubTime = 0, bool simplyMux = false, bool isHevc = false)
    {
        if (audioOnly && audioPath != "")
            videoPath = "";
        if (videoOnly)
            audioPath = "";
        desc = EscapeString(desc);
        title = EscapeString(title);
        episodeId = EscapeString(episodeId);
        author = EscapeString(author);
        lang = EscapeString(lang);
        var url = $"https://www.bilibili.com/video/{bvid}/";

        if (useMp4box)
        {
            return MuxByMp4box(url, videoPath, audioPath, outPath, desc, title, author, episodeId, pic, lang, subs, audioOnly, videoOnly, points);
        }

        string? metaFile = null;
        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);
        //----分析并生成-i参数
        StringBuilder inputArg = new();
        StringBuilder metaArg = new();
        byte inputCount = 0;
        foreach (string path in new[] { videoPath, audioPath })
        {
            if (!string.IsNullOrEmpty(path))
            {
                inputCount++;
                inputArg.Append($"-i \"{path}\" ");
            }
        }

        if (audioMaterial.Any())
        {
            byte audioCount = 0;
            metaArg.Append("-metadata:s:a:0 title=\"原音频\" ");
            foreach (var audio in audioMaterial)
            {
                inputCount++;
                audioCount++;
                inputArg.Append($"-i \"{audio.path}\" ");
                if (!string.IsNullOrWhiteSpace(audio.title)) metaArg.Append($"-metadata:s:a:{audioCount} title=\"{EscapeString(audio.title)}\" ");
                if (!string.IsNullOrWhiteSpace(audio.personName)) metaArg.Append($"-metadata:s:a:{audioCount} artist=\"{EscapeString(audio.personName)}\" ");
            }
        }

        if (!string.IsNullOrEmpty(pic))
        {
            inputCount++;
            inputArg.Append($"-i \"{pic}\" ");
        }

        if (subs != null)
        {
            for (int i = 0; i < subs.Count; i++)
            {
                if(File.Exists(subs[i].path) && File.ReadAllText(subs[i].path!) != "")
                {
                    inputCount++;
                    inputArg.Append($"-i \"{subs[i].path}\" ");
                    var (subLangCode, subLangName) = SubUtil.GetSubtitleCode(subs[i].lan);
                    metaArg.Append($"-metadata:s:s:{i} title=\"{EscapeString(subLangName)}\" -metadata:s:s:{i} language=\"{EscapeString(subLangCode)}\" ");
                }
            }
        }

        if (!string.IsNullOrEmpty(pic))
            metaArg.Append($"-disposition:v:{(audioOnly ? "0" : "1")} attached_pic ");
        // var inputCount = InputRegex().Matches(inputArg.ToString()).Count;

        if (points != null && points.Any())
        {
            var meta = BBDownUtil.GetFFmpegMetaString(points);
            var baseDir = Path.GetDirectoryName(string.IsNullOrEmpty(videoPath) ? audioPath : videoPath);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = ".";
            // 与 mp4box 分支一致：避免并发混流用固定名互相覆盖章节文件，用后即删
            metaFile = Path.Combine(baseDir, $"chapters-{Path.GetFileNameWithoutExtension(outPath)}");
            File.WriteAllText(metaFile, meta);
            inputArg.Append($"-i \"{metaFile}\" -map_chapters {inputCount} ");
        }

        inputArg.Append(string.Concat(Enumerable.Range(0, inputCount).Select(i => $"-map {i} ")));

        //----分析完毕
        StringBuilder argsBuilder = new StringBuilder();
        argsBuilder.Append($"-loglevel {(Config.Current.DebugLog ? "verbose" : "warning")} -y ");
        argsBuilder.Append(inputArg);
        argsBuilder.Append(metaArg);
        if (!simplyMux) {
            argsBuilder.Append($"-metadata title=\"{(episodeId == "" ? title : episodeId)}\" ");
            argsBuilder.Append($"-metadata comment=\"{url}\" ");
            if (lang != "") argsBuilder.Append($"-metadata:s:a:0 language=\"{lang}\" ");
            if (!string.IsNullOrWhiteSpace(desc)) argsBuilder.Append($"-metadata description=\"{desc}\" ");
            if (!string.IsNullOrEmpty(author)) argsBuilder.Append($"-metadata artist=\"{author}\" ");
            if (episodeId != "") argsBuilder.Append($"-metadata album=\"{title}\" ");
            if (pubTime != 0) argsBuilder.Append($"-metadata creation_time=\"{(DateTimeOffset.FromUnixTimeSeconds(pubTime).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ"))}\" ");
        }
        argsBuilder.Append("-c:v copy -c:a copy ");
        if (audioOnly && audioPath == "") argsBuilder.Append("-vn ");
        if (subs != null) argsBuilder.Append("-c:s mov_text ");
        // fix macOS hev1, see https://discussions.apple.com/thread/253081863?sortBy=rank
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && isHevc) argsBuilder.Append("-tag:v:0 hvc1 ");
        argsBuilder.Append($"-movflags faststart -strict unofficial -strict -2 -f mp4 -- \"{outPath}\"");

        string arguments = argsBuilder.ToString();

        Logger.LogDebug("ffmpeg命令: {0}", arguments);
        try
        {
            return RunExe(FFMPEG, arguments);
        }
        finally
        {
            if (metaFile != null && File.Exists(metaFile))
            {
                try { File.Delete(metaFile); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logger.LogDebug("清理章节文件失败: {0}", ex.Message);
                }
            }
        }
    }

    public static void MergeFLV(string[] files, string outPath)
    {
        if (files.Length == 0) return;
        if (files.Length == 1)
        {
            File.Move(files[0], outPath, true);
        }
        else
        {
            // 只处理本次传入的分段派生出的 .ts，不再扫描整个目录：
            // 目录里可能有同一 aid 其它分P 已完成的 .ts / 成品，扫目录会把它们
            // 一并拼进本P 输出，产出串味的损坏文件。
            var tsFiles = new List<string>();
            foreach (var file in files)
            {
                var tmpFile = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + ".ts");
                var arguments = $"-loglevel warning -y -i \"{file}\" -map 0 -c copy -f mpegts -bsf:v h264_mp4toannexb \"{tmpFile}\"";
                Logger.LogDebug("ffmpeg命令: {0}", arguments);
                int code = RunExe(FFMPEG, arguments);
                // 校验退出码与产物：原实现丢弃退出码却无条件删除源分段，
                // ffmpeg 失败时源已删、.ts 为空，合并出缺段文件且不可恢复。
                if (code != 0 || !File.Exists(tmpFile) || new FileInfo(tmpFile).Length == 0)
                {
                    foreach (var ts in tsFiles) TryDelete(ts);
                    TryDelete(tmpFile);
                    throw new InvalidOperationException(
                        $"FLV 分段转封装失败 (ffmpeg code={code})：{Path.GetFileName(file)}，已保留源分段以便重试");
                }
                tsFiles.Add(tmpFile);
                File.Delete(file);
            }
            BBDownUtil.CombineMultipleFilesIntoSingleFile(tsFiles.ToArray(), outPath);
            foreach (var s in tsFiles) TryDelete(s);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* 清理失败不影响主流程 */ }
    }
}

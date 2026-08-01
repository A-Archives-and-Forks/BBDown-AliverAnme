using BBDown;
using BBDown.Core;
using static BBDown.Core.Entity.Entity;

namespace BBDown.Tests;

public class DownloadPipelineTests
{
    [Fact]
    public void GetAllClips_LastSegment_HasExplicitEndInsteadOfMinusOne()
    {
        var original = Config.Current.ThreadSegmentSizeMb;
        try
        {
            Config.Apply(Config.Current with { ThreadSegmentSizeMb = 1 }); // 1MB 分片
            long per = 1024L * 1024;
            var clips = BBDownDownloadUtil.GetAllClips("http://x", per + 500); // 完整一段 + 500 字节末段
            Assert.Equal(2, clips.Count);
            // 末段不再用 -1：指向文件真实末尾，断点续传的完整性检查（toPosition > 0）才能命中，
            // 否则完整末段会发 Range: bytes=<fileSize>- 触发 416 永久失败
            Assert.Equal(per + 500 - 1, clips[^1].to);
            Assert.All(clips, c => Assert.True(c.to >= c.from));
        }
        finally
        {
            Config.Apply(Config.Current with { ThreadSegmentSizeMb = original });
        }
    }

    [Fact]
    public void CleanStaleClips_RemovesOnlyMatchingPathClips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mineA = Path.Combine(dir, "00000_video.vclip");
            var mineB = Path.Combine(dir, "00001_video.vclip");
            var other = Path.Combine(dir, "00000_other.vclip");
            var audioClip = Path.Combine(dir, "00000_video.aclip"); // 同 stem 的音频轨分片
            var unrelated = Path.Combine(dir, "notes.txt");
            File.WriteAllText(mineA, "a");
            File.WriteAllText(mineB, "b");
            File.WriteAllText(other, "c");
            File.WriteAllText(audioClip, "e");
            File.WriteAllText(unrelated, "d");

            BBDownDownloadUtil.CleanStaleClipsFor(Path.Combine(dir, "video.mp4"));

            Assert.False(File.Exists(mineA));
            Assert.False(File.Exists(mineB));
            Assert.True(File.Exists(other));      // 其他任务的 clip 保留
            Assert.True(File.Exists(audioClip));  // 音频轨分片必须保留
            Assert.True(File.Exists(unrelated));  // 非 clip 文件保留
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CleanStaleClips_Audio_DoesNotDeleteVideoClips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var videoClip = Path.Combine(dir, "00000_video.vclip");
            var audioClip = Path.Combine(dir, "00000_video.aclip");
            File.WriteAllText(videoClip, "v");
            File.WriteAllText(audioClip, "a");

            BBDownDownloadUtil.CleanStaleClipsFor(Path.Combine(dir, "video.m4a"));

            Assert.True(File.Exists(videoClip)); // 视频轨分片保留
            Assert.False(File.Exists(audioClip));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(2, 3, 2)]
    [InlineData(5, 3, 2)]   // 越界 → 钳到末位
    [InlineData(-1, 3, 0)]
    [InlineData(3, 1, 0)]
    [InlineData(0, 0, -1)]  // 无音频 → 标记跳过
    public void ClampRoleAudioIndex_HandlesOutOfRange(int aIndex, int count, int expected)
        => Assert.Equal(expected, Program.ClampRoleAudioIndex(aIndex, count));
}

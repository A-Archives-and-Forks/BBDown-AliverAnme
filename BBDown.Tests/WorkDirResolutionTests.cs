using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Tests;

/// <summary>
/// B1：消灭 serve 模式 CWD 全局写。下载管线的相对路径统一经
/// <see cref="PathUtil.ResolveWorkPath"/> 基于任务流配置的 WorkDir 解析；
/// serve 模式下 ChangeWorkingDir 不再写进程 CWD（并发任务各自的 --work-dir
/// 经 AsyncLocal 配置快照隔离），CLI 单任务仍写 CWD 以兼容子进程相对路径。
/// </summary>
public class WorkDirResolutionTests
{
    [Fact]
    public void ResolveWorkPath_Relative_ResolvesAgainstCwd_WhenNoWorkDir()
    {
        var original = Config.Current.WorkDir;
        try
        {
            Config.Apply(Config.Current with { WorkDir = "" });
            Assert.Equal(Path.GetFullPath("12345/a.mp4"), PathUtil.ResolveWorkPath("12345/a.mp4"));
        }
        finally
        {
            Config.Apply(Config.Current with { WorkDir = original });
        }
    }

    [Fact]
    public void ResolveWorkPath_Relative_ResolvesAgainstWorkDir_WhenSet()
    {
        var original = Config.Current.WorkDir;
        var workDir = Path.Combine(Path.GetTempPath(), "bbdown-wd-" + Guid.NewGuid().ToString("N"));
        try
        {
            Config.Apply(Config.Current with { WorkDir = workDir });
            Assert.Equal(Path.Combine(workDir, "12345/a.mp4"), PathUtil.ResolveWorkPath("12345/a.mp4"));
        }
        finally
        {
            Config.Apply(Config.Current with { WorkDir = original });
        }
    }

    [Fact]
    public void ResolveWorkPath_Absolute_PassesThroughRegardlessOfWorkDir()
    {
        var original = Config.Current.WorkDir;
        var workDir = Path.Combine(Path.GetTempPath(), "bbdown-wd-" + Guid.NewGuid().ToString("N"));
        var abs = Path.Combine(Path.GetTempPath(), "custom-out.mp4");
        try
        {
            Config.Apply(Config.Current with { WorkDir = workDir });
            // 自定义 --file-pattern 指向绝对目录时不受 WorkDir 影响
            Assert.Equal(abs, PathUtil.ResolveWorkPath(abs));
        }
        finally
        {
            Config.Apply(Config.Current with { WorkDir = original });
        }
    }

    [Fact]
    public void ResolveWorkPath_Empty_PassesThrough()
    {
        var original = Config.Current.WorkDir;
        try
        {
            Config.Apply(Config.Current with { WorkDir = Path.GetTempPath() });
            Assert.Equal("", PathUtil.ResolveWorkPath(""));
        }
        finally
        {
            Config.Apply(Config.Current with { WorkDir = original });
        }
    }

    [Fact]
    public async Task ResolveWorkPath_ConcurrentFlows_EachUsesOwnWorkDir()
    {
        // serve 并发任务：每个 /add-task 流各自 SetUpWork 写入自己的 WorkDir。
        // AsyncLocal 隔离要求两个并发流解析相对路径时各用各的目录，互不读对方的。
        var original = Config.Current.WorkDir;
        var wdA = Path.Combine(Path.GetTempPath(), "bbdown-wd-a-" + Guid.NewGuid().ToString("N"));
        var wdB = Path.Combine(Path.GetTempPath(), "bbdown-wd-b-" + Guid.NewGuid().ToString("N"));
        try
        {
            var t1 = Task.Run(() =>
            {
                Config.Apply(Config.Current with { WorkDir = wdA });
                Task.Delay(30).Wait();
                return PathUtil.ResolveWorkPath("12345/a.mp4");
            });
            var t2 = Task.Run(() =>
            {
                Config.Apply(Config.Current with { WorkDir = wdB });
                Task.Delay(30).Wait();
                return PathUtil.ResolveWorkPath("12345/a.mp4");
            });
            var results = await Task.WhenAll(t1, t2);
            Assert.Equal(Path.Combine(wdA, "12345/a.mp4"), results[0]);
            Assert.Equal(Path.Combine(wdB, "12345/a.mp4"), results[1]);
        }
        finally
        {
            Config.Apply(Config.Current with { WorkDir = original });
        }
    }

    [Fact]
    public void ChangeWorkingDir_ServeMode_DoesNotWriteProcessCwd()
    {
        var originalServeMode = Program.IsServeMode;
        var originalCwd = Environment.CurrentDirectory;
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-wd-" + Guid.NewGuid().ToString("N"));
        try
        {
            Program.IsServeMode = true;
            var option = new MyOption { WorkDir = dir };
            var resolved = Program.ChangeWorkingDir(option);
            Assert.Equal(Path.GetFullPath(dir), resolved);
            // serve 模式绝不写进程 CWD：并发任务各自的 --work-dir 不能互相覆盖进程级状态
            Assert.Equal(originalCwd, Environment.CurrentDirectory);
            Assert.True(Directory.Exists(resolved), "serve 下仍应创建目录（供 ResolveWorkPath 使用）");
        }
        finally
        {
            Program.IsServeMode = originalServeMode;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ChangeWorkingDir_NoWorkDir_ReturnsEmpty_AndDoesNotTouchCwd()
    {
        var originalServeMode = Program.IsServeMode;
        var originalCwd = Environment.CurrentDirectory;
        try
        {
            Program.IsServeMode = true;
            Assert.Equal("", Program.ChangeWorkingDir(new MyOption()));
            Assert.Equal(originalCwd, Environment.CurrentDirectory);
        }
        finally
        {
            Program.IsServeMode = originalServeMode;
        }
    }

    [Fact]
    public void SetUpWork_WorkDir_IsAppliedToTaskFlow_WithoutWritingCwd()
    {
        var originalServeMode = Program.IsServeMode;
        var originalConfig = Config.Current;
        var originalCwd = Environment.CurrentDirectory;
        var dir = Path.Combine(Path.GetTempPath(), "bbdown-wd-" + Guid.NewGuid().ToString("N"));
        try
        {
            Program.IsServeMode = true; // serve 语义：SetUpWork 不写进程 CWD
            // SkipMux=true 避免 FindBinaries 要求本机存在 ffmpeg
            var option = new MyOption { WorkDir = dir, SkipMux = true };
            Program.SetUpWork(option);

            // WorkDir 已写入当前任务流配置，供 PathUtil.ResolveWorkPath 解析相对路径
            Assert.Equal(Path.GetFullPath(dir), Config.Current.WorkDir);
            // serve 模式未改动进程 CWD
            Assert.Equal(originalCwd, Environment.CurrentDirectory);
            Assert.True(Directory.Exists(Path.GetFullPath(dir)));
        }
        finally
        {
            Program.IsServeMode = originalServeMode;
            Config.Apply(originalConfig);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

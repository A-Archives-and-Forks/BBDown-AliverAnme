namespace BBDown.Tests;

public class DownloadTaskSnapshotTests
{
    [Fact]
    public void Snapshot_IsIndependentOfLiveTask()
    {
        var task = new DownloadTask("12345", "av12345", 1700000000)
        {
            Title = "标题",
            SavePaths = ["a.mp4"],
        };

        var snap = task.Snapshot();
        Assert.Equal("12345", snap.Aid);
        Assert.Equal("标题", snap.Title);
        Assert.Equal(["a.mp4"], snap.SavePaths);

        // 下载线程继续修改原对象：快照不得受影响（SavePaths 必须深拷贝）
        task.SavePaths.Add("b.mp4");
        task.Progress = 0.5;
        task.Title = "新标题";
        Assert.Equal(["a.mp4"], snap.SavePaths);
        Assert.Equal(0f, snap.Progress);
        Assert.Equal("标题", snap.Title);
    }

    [Fact]
    public void Snapshot_CopiesAllFields()
    {
        var task = new DownloadTask("99", "bv1", 1)
        {
            Pic = "cover.jpg",
            VideoPubTime = 2,
            TaskFinishTime = 3,
            DownloadSpeed = 4.5,
            TotalDownloadedBytes = 1024,
            IsSuccessful = true,
            ErrorMessage = "ok",
        };

        var snap = task.Snapshot();
        Assert.Equal("cover.jpg", snap.Pic);
        Assert.Equal(2, snap.VideoPubTime);
        Assert.Equal(3, snap.TaskFinishTime);
        Assert.Equal(4.5, snap.DownloadSpeed);
        Assert.Equal(1024, snap.TotalDownloadedBytes);
        Assert.True(snap.IsSuccessful);
        Assert.Equal("ok", snap.ErrorMessage);
    }

    [Fact]
    public void AddSavePath_IsVisibleToSnapshot()
    {
        var task = new DownloadTask("1", "av1", 1);
        task.AddSavePath("a.mp4");
        task.AddSavePath("b.mp4");

        var snap = task.Snapshot();
        Assert.Equal(["a.mp4", "b.mp4"], snap.SavePaths);
        // 快照是深拷贝：后续写入不污染已生成的快照
        task.AddSavePath("c.mp4");
        Assert.Equal(["a.mp4", "b.mp4"], snap.SavePaths);
        Assert.Equal(["a.mp4", "b.mp4", "c.mp4"], task.Snapshot().SavePaths);
    }
}

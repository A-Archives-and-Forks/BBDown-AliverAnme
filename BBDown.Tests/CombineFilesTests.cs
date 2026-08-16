namespace BBDown.Tests;

/// <summary>
/// C2：分片合并改异步流式（CopyToAsync）。验证按序合并、单文件直移、取消传播。
/// </summary>
public class CombineFilesTests
{
    [Fact]
    public async Task CombineMultipleFiles_Async_MergesInOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bbdown-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var f1 = Path.Combine(dir, "a.bin");
            var f2 = Path.Combine(dir, "b.bin");
            var outFile = Path.Combine(dir, "out.bin");
            await File.WriteAllBytesAsync(f1, new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(f2, new byte[] { 4, 5, 6, 7 });
            await BBDownUtil.CombineMultipleFilesIntoSingleFileAsync(new[] { f1, f2 }, outFile);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, await File.ReadAllBytesAsync(outFile));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task CombineMultipleFiles_SingleFile_MovesDirectly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bbdown-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "solo.bin");
            var dst = Path.Combine(dir, "solo-out.bin");
            await File.WriteAllBytesAsync(src, new byte[] { 9, 8, 7 });
            await BBDownUtil.CombineMultipleFilesIntoSingleFileAsync(new[] { src }, dst);
            Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(dst));
            Assert.False(File.Exists(src), "单分片走 MoveTo，源文件应消失");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task CombineMultipleFiles_CancelledToken_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bbdown-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var f1 = Path.Combine(dir, "a.bin");
            var f2 = Path.Combine(dir, "b.bin");
            var outFile = Path.Combine(dir, "out.bin");
            await File.WriteAllBytesAsync(f1, new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(f2, new byte[] { 4, 5, 6, 7 });
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => BBDownUtil.CombineMultipleFilesIntoSingleFileAsync(new[] { f1, f2 }, outFile, cts.Token));
            // 预取消 token 在创建输出前抛错，不应留下空文件
            Assert.False(File.Exists(outFile));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task CombineMultipleFiles_FailingInput_RemovesPartialOutput()
    {
        // 中途失败（源文件缺失）必须删除半截产物，不能把损坏文件留在最终路径
        var dir = Path.Combine(Path.GetTempPath(), $"bbdown-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var f1 = Path.Combine(dir, "a.bin");
            var f2 = Path.Combine(dir, "missing.bin");
            var outFile = Path.Combine(dir, "out.bin");
            await File.WriteAllBytesAsync(f1, new byte[] { 1, 2, 3 });
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => BBDownUtil.CombineMultipleFilesIntoSingleFileAsync(new[] { f1, f2 }, outFile));
            Assert.False(File.Exists(outFile), "合并中途失败应删除半截产物");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
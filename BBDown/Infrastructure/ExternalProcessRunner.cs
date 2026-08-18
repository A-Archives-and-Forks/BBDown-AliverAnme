using System.Diagnostics;
using System.Text;

namespace BBDown;

/// <summary>
/// 外部进程启动请求。Arguments 是逐项 argv（ArgumentList 语义），
/// 由执行器负责 OS 层转义，调用方不再手工拼引号/转义符。
/// </summary>
public sealed class ExternalProcessSpec
{
    public required string FileName { get; init; }

    /// <summary>逐项命令行参数。作为独立 argv 传递给目标程序。</summary>
    public List<string> Arguments { get; init; } = [];

    /// <summary>可选标准输入内容（如 aria2c 的 input-file）。</summary>
    public string? StandardInput { get; init; }

    /// <summary>超时毫秒；null = 不设超时。</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>stdout 逐行回调；null = 不重定向 stdout。</summary>
    public Action<string>? OnStandardOutput { get; init; }

    /// <summary>stderr 逐行回调；null = 不重定向 stderr。</summary>
    public Action<string>? OnStandardError { get; init; }

    /// <summary>超时错误消息里的工具名（如 "ffmpeg"），便于定位是哪个程序卡住。</summary>
    public string? ToolDisplayName { get; init; }
}

public interface IExternalProcessRunner
{
    Task<int> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 System.Diagnostics.Process 的执行器，集中处理外部进程的共性逻辑：
/// - ArgumentList 逐项转义：参数以独立 argv 传递，杜绝手工拼引号的注入面；
/// - 超时 / 取消时整棵进程树 Kill：serve 关停或 Ctrl+C 不会留下孤儿 ffmpeg/aria2c；
/// - stdout/stderr 行式转发并限流：单个进程最多记录固定行数，防止刷屏灌满日志。
/// </summary>
public sealed class SystemProcessRunner : IExternalProcessRunner
{
    /// <summary>单个外部进程最多向回调转发多少行；超出后提示截断。</summary>
    private const int MaxLogLinesPerProcess = 200;

    public async Task<int> RunAsync(ExternalProcessSpec spec, CancellationToken cancellationToken = default)
    {
        using var p = new Process();
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.FileName = spec.FileName;
        foreach (var arg in spec.Arguments) p.StartInfo.ArgumentList.Add(arg);
        p.StartInfo.RedirectStandardInput = true;
        p.StartInfo.RedirectStandardOutput = spec.OnStandardOutput != null;
        p.StartInfo.RedirectStandardError = spec.OnStandardError != null;
        if (spec.OnStandardError != null) p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        if (spec.OnStandardOutput != null) p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        p.Start();

        // 先启动 stdout/stderr 读取再等待退出，避免子进程写满管道缓冲区时
        // 双方互相等待（管道已满 → 子进程阻塞 → WaitForExit 永不返回）造成死锁。
        var stdoutTask = spec.OnStandardOutput != null ? ReadLinesThrottled(p.StandardOutput, spec.OnStandardOutput) : null;
        var stderrTask = spec.OnStandardError != null ? ReadLinesThrottled(p.StandardError, spec.OnStandardError) : null;
        Task? stdinTask = null;
        if (spec.StandardInput != null)
        {
            stdinTask = WriteStdinAsync(p.StandardInput, spec.StandardInput, cancellationToken);
        }
        else
        {
            // 未指定标准输入时立即关闭 stdin 管道向子进程发送 EOF，
            // 避免 ffmpeg/aria2c 等外部程序因等待终端交互输入而意外挂起阻塞
            try { p.StandardInput.Close(); } catch { }
        }

        try
        {
            await WaitWithTimeoutAsync(p, spec, cancellationToken);
            // 成功路径同样带超时兜底等管道任务：进程已退出后 stdout/stderr 读取应立即结束，
            // 但极端场景（脱离子进程继承管道句柄等）下读取可能短暂挂起钉住并发槽。
            // 与清理路径不同：管道任务的异常（读取/写入失败）应向上传播而不是吞掉——
            // 外部进程成功退出但 stderr 读取失败的证据不能丢。
            var pipeTasks = new List<Task>();
            if (stdoutTask != null) pipeTasks.Add(stdoutTask);
            if (stderrTask != null) pipeTasks.Add(stderrTask);
            if (stdinTask != null) pipeTasks.Add(stdinTask);
            if (pipeTasks.Count > 0)
                await Task.WhenAll(pipeTasks).WaitAsync(TimeSpan.FromSeconds(5));
            return p.ExitCode;
        }
        catch
        {
            // 取消/超时路径：进程已由 WaitWithTimeoutAsync Kill。管道读取任务可能
            // 因管道断裂正常结束、也可能短暂挂起——这里带超时异步兜底等待，避免
            // 旧实现里 finally 中同步 GetAwaiter().GetResult() 卡线程，也确保 stdinTask
            // 一并被观察（不留未观察异常）。清理完成后原样重抛（保留取消语义）。
            await AwaitPipeTasksAsync(stdoutTask, stderrTask, stdinTask);
            throw;
        }
    }

    /// <summary>
    /// 带 5 秒超时兜底地等待 stdout/stderr/stdin 管道任务全部完成。
    /// 仅在取消/超时（进程已被 Kill）的清理路径使用：Kill 后管道断裂会令
    /// <see cref="ReadLinesThrottled"/>/<see cref="WriteStdinAsync"/> 结束，但极端情况下
    /// 读取可能仍短暂挂起，这里限制等待时间保证清理永不阻塞调用线程。
    /// 清理路径的任何异常都不应掩盖主路径已抛出的取消/超时异常，故一律忽略。
    /// </summary>
    private static async Task AwaitPipeTasksAsync(Task? stdoutTask, Task? stderrTask, Task? stdinTask)
    {
        var pipeTasks = new List<Task>();
        if (stdoutTask != null) pipeTasks.Add(stdoutTask);
        if (stderrTask != null) pipeTasks.Add(stderrTask);
        if (stdinTask != null) pipeTasks.Add(stdinTask);
        if (pipeTasks.Count == 0) return;

        try
        {
            await Task.WhenAll(pipeTasks).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // 忽略：进程对象会在方法退出时释放，打断仍在挂起的管道读取。
        }
    }

    private static async Task WaitWithTimeoutAsync(Process p, ExternalProcessSpec spec, CancellationToken cancellationToken)
    {
        if (spec.TimeoutMs is not > 0)
        {
            try { await p.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                KillProcessTree(p);
                throw;
            }
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(spec.TimeoutMs.Value);
        try
        {
            await p.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消：Kill 整树后原样抛出，让上层走取消路径
            KillProcessTree(p);
            throw;
        }
        catch (OperationCanceledException)
        {
            // 超时触发（非调用方取消）
            KillProcessTree(p);
            throw new TimeoutException(
                $"{spec.ToolDisplayName ?? spec.FileName} 执行超过 {spec.TimeoutMs} 毫秒，已强制终止。");
        }
    }

    private static void KillProcessTree(Process p)
    {
        try { p.Kill(entireProcessTree: true); }
        catch { /* 进程可能已自行退出 */ }
    }

    /// <summary>
    /// 行式读取外部进程输出并转发回调。合并连续重复行（如 ffmpeg 反复刷同一进度行），
    /// 超过 <see cref="MaxLogLinesPerProcess"/> 行后只提示一次截断。
    /// </summary>
    private static async Task ReadLinesThrottled(StreamReader reader, Action<string> onLine)
    {
        int lines = 0;
        string? last = null;
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                lines++; if (lines <= MaxLogLinesPerProcess)
                {
                    if (line == last) continue;
                    last = line;
                    onLine(line);
                }
                else if (lines == MaxLogLinesPerProcess + 1)
                {
                    onLine($"…（外部进程输出过多，已截断，共 {lines} 行）");
                }
            }
        }
        catch (IOException) { /* 进程被强制终止时管道中断，读取异常可忽略 */ }
        catch (ObjectDisposedException) { /* 同上 */ }
    }

    private static async Task WriteStdinAsync(StreamWriter writer, string content, CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
            // 若写入被取消（token 触发），进程已被 Kill，writer 仍持有未释放句柄；
            // 用 try/catch 包一层释放，避免进程对象释放前管道写入端残留。
            try { writer.Close(); } catch (ObjectDisposedException) { }
        }
        catch (IOException) { /* 进程可能已提前退出，stdin 管道断裂 */ }
        catch (OperationCanceledException) { /* 取消路径：进程已被 Kill，无需再写 */ }
    }
}

/// <summary>
/// 把一段"以空格分隔、可含引号"的命令行字符串切分成 argv token。
/// 供把历史遗留的整串参数（如 aria2c 的 --aria2c-args 配置）切进 ArgumentList 使用；
/// 支持双引号包裹含空格的 token，不再支持反斜杠转义（按字面保留）。
/// </summary>
internal static class CommandLineSplitter
{
    public static List<string> Split(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        foreach (var c in input)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (c is ' ' or '\t' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (inQuotes)
        {
            // 未闭合引号：--aria2c-args 这类整串配置里出现畸形引号会把后续整串吞成一个
            // token（inQuotes 永不复位），静默改义难以定位。把未闭合引号内的剩余内容
            // 作为最后一个 token（尽力恢复），而非丢弃整段配置。
            if (current.Length > 0) tokens.Add(current.ToString());
        }
        else if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }
        return tokens;
    }
}

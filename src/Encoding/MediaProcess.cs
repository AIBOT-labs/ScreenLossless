using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ScreenLossless;

internal static class MediaProcess
{
    // Long saves have no absolute time limit. Only lack of progress is considered a stall.
    internal static async Task RunAsync(string exe, IEnumerable<string> arguments, double duration,
        Action<double>? progress, CancellationToken token, TimeSpan? stallTimeout = null, Action<double>? mediaTime = null)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-nostats", "-stats_period", "0.5", "-progress", "pipe:1" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException(L10n.T("无法启动媒体处理程序。"));
        long lastActivity = Stopwatch.GetTimestamp();
        var errors = new StringBuilder();
        var advanced = new Dictionary<string, double>(StringComparer.Ordinal);
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task ReadOutput()
        {
            while (await process.StandardOutput.ReadLineAsync(readCancellation.Token) is { } line)
            {
                int separator = line.IndexOf('=');
                if (separator < 0) continue;
                string key = line[..separator];
                if (key is not ("frame" or "total_size" or "out_time_us") ||
                    !double.TryParse(line[(separator + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                    !double.IsFinite(value) || value < 0) continue;
                // FFmpeg emits heartbeats even when frozen; only advancing work resets the watchdog.
                if (advanced.TryGetValue(key, out double previous) && value <= previous) continue;
                advanced[key] = value;
                Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());
                if (key == "out_time_us")
                {
                    mediaTime?.Invoke(value / 1_000_000d);
                    if (duration > 0) progress?.Invoke(Math.Clamp(value / 1_000_000d / duration, 0, 1));
                }
            }
        }
        async Task ReadError()
        {
            while (await process.StandardError.ReadLineAsync(readCancellation.Token) is { } line)
            {
                errors.AppendLine(line);
                if (errors.Length > 5000) errors.Remove(0, errors.Length - 3000);
            }
        }
        var output = ReadOutput(); var error = ReadError();
        var stall = stallTimeout ?? TimeSpan.FromMinutes(3);
        var exited = process.WaitForExitAsync(token);
        try
        {
            while (!process.HasExited)
            {
                token.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastActivity)) > stall)
                    throw new TimeoutException(L10n.T("媒体处理长时间没有进度，已保留原始素材，可稍后恢复重试。"));
                await Task.WhenAny(exited, Task.Delay(500, token));
                if (output.IsFaulted) await output;
                if (error.IsFaulted) await error;
            }
            await Task.WhenAll(output, error);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException(errors.Length == 0 ? L10n.T("媒体处理失败。") : errors.ToString().Trim());
            progress?.Invoke(1);
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch { } }
            readCancellation.Cancel();
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { } catch (IOException) { }
        }
    }
    internal static async Task<string> ReadTextAsync(string exe, IEnumerable<string> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(exe) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process=Process.Start(start) ?? throw new InvalidOperationException(L10n.T("无法启动媒体信息读取。"));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var output=process.StandardOutput.ReadToEndAsync(timeout.Token); var error=process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token); string result=await output, message=await error;
            if(process.ExitCode!=0)throw new InvalidDataException(message);
            return result;
        }
        finally
        {
            if(!process.HasExited)try{process.Kill(true);}catch{}
            timeout.Cancel();
            try{await Task.WhenAll(output,error);}catch(OperationCanceledException){}catch(IOException){}
        }
    }
    internal static async Task<Bitmap> ReadImageAsync(string exe,IEnumerable<string> arguments,CancellationToken token)
    {
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new InvalidOperationException(L10n.T("无法读取预览画面。"));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var memory=new MemoryStream();
        var output=process.StandardOutput.BaseStream.CopyToAsync(memory,timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);await output;
            if(process.ExitCode!=0)throw new InvalidDataException(await error);
            memory.Position=0;using var image=Image.FromStream(memory);return new Bitmap(image);
        }
        finally
        {
            if(!process.HasExited)try{process.Kill(true);}catch{}
            timeout.Cancel();
            try{await Task.WhenAll(output,error);}catch(OperationCanceledException){}catch(IOException){}
        }
    }
}
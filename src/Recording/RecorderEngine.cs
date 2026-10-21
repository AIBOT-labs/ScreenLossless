using System.Diagnostics;
using System.Globalization;

namespace ScreenLossless;

internal enum RecorderState { Idle, Starting, Recording, Paused, Finalizing }

internal sealed class RecorderEngine : IDisposable
{
    private readonly List<string> _errorLines = new();
    private Process? _process;
    private Task _outputReader = Task.CompletedTask;
    private AudioSession? _audio;
    private string _temporaryDirectory = "";
    private string _target = "";
    private Capabilities _caps = new();
    private RecordPlan? _plan;
    private RecordingManifest _manifest = new();
    private bool _autoCodec;
    private EncoderChoice _fallbackEncoder;
    private bool _disposed;
    private TimeSpan _completedDuration;
    private long _completedFrames;
    private long _segmentFrames;
    private readonly RecordingPerformance _performance = new();
    internal PerformanceSnapshot Performance => _performance.Snapshot;
    private int _automaticParts;
    private TimeSpan _groupStart;
    private long _groupBytes;
    private ContinuousVideoSize? _videoSize;
    internal int SegmentCount => _manifest.Segments.Count + _automaticParts;
    internal double ChunkSeconds { get; set; } = 10;
    internal int? CaptureProcessId => _process?.Id;
    internal TimeSpan SegmentDuration => _running.Elapsed;
    internal bool SegmentDue
    {
        get
        {
            if (State != RecorderState.Recording || _plan is null) return false;
            var elapsed=_running.Elapsed-_groupStart;
            if(OutputOptions.SegmentDue(elapsed,_plan.SegmentMinutes,0,0,0))return true;
            if(_plan.SegmentGigabytes<=0)return false;
            long videoBytes=0;
            try { videoBytes=_videoSize?.ReadBytes()??0; }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            return OutputOptions.SegmentDue(elapsed,0,Math.Max(0,videoBytes-_groupBytes),_plan.SegmentGigabytes,0);
        }
    }
    private readonly Stopwatch _running = new();
    public RecorderState State { get; private set; } = RecorderState.Idle;
    public string OutputFile => _target;
    internal string WorkingDirectory=>_temporaryDirectory;
    internal float SystemLevel=>State==RecorderState.Recording?(_audio?.SystemLevel??0)*(_plan?.SystemVolume??1):0;
    internal float MicrophoneLevel=>State==RecorderState.Recording?(_audio?.MicrophoneLevel??0)*(_plan?.MicrophoneVolume??1):0;
    internal bool SystemAudioAvailable=>_audio?.SystemAvailable==true;
    internal bool MicrophoneAvailable=>_audio?.MicrophoneAvailable==true;
    public long FrameCount => Interlocked.Read(ref _completedFrames) + Interlocked.Read(ref _segmentFrames);
    public TimeSpan Duration => _completedDuration + _running.Elapsed;
    public bool UnexpectedExit => State == RecorderState.Recording && (_process?.HasExited ?? false);
    internal string ExitDetails => _process is { HasExited: true } p ? L10n.F($"退出码 {p.ExitCode}：{GetRecentError()}") : "";
    public RecordPlan? CurrentPlan => _plan;
    public event Action<string>? Message;
    public event Action<long>? FrameUpdated;
    public event Action<RecorderState>? StateUpdated;
    public event Action<double>? FinalizationProgress;

    private void ChangeState(RecorderState state) { State = state; StateUpdated?.Invoke(state); }
    internal string LastStopReason { get; private set; } = "";
    internal void ReportStopReason(string message)
    {
        LastStopReason = message;
        ReportStatus(message);
    }
    private void ReportStatus(string message) => Message?.Invoke(message);

    public async Task StartAsync(RecordPlan plan, Capabilities capabilities, string outputFolder, bool autoCodec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State != RecorderState.Idle) throw new InvalidOperationException(L10n.T("录制已在进行中。"));
        if (!capabilities.Available) throw new InvalidOperationException(L10n.T("请先正确配置 FFmpeg。"));
        // Validate before creating a work folder or opening any capture device.
        FfmpegArguments.Capture(plan, "validation.mkv");
        outputFolder = Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(outputFolder);
        _caps = capabilities;
        _autoCodec = autoCodec;
        _fallbackEncoder = VideoEncoding.CpuFallback(capabilities, plan.IsLossless);
        _plan = plan;
        LastStopReason = "";
        _completedDuration = TimeSpan.Zero;
        Interlocked.Exchange(ref _completedFrames, 0);
        Interlocked.Exchange(ref _segmentFrames, 0);
        _running.Reset();
        _automaticParts = 0; _groupStart = TimeSpan.Zero; _groupBytes = 0;
        string prefix = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string extension = plan.Container == OutputContainer.Mp4 ? ".mp4" : ".mkv";
        _target = Path.Combine(outputFolder, $"ScreenLossless_{prefix}{extension}");
        for (int suffix = 2; File.Exists(_target); suffix++) _target = Path.Combine(outputFolder, $"ScreenLossless_{prefix}_{suffix}{extension}");
        _temporaryDirectory = Path.Combine(outputFolder, ".screenlossless-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDirectory);
        _manifest = new RecordingManifest
        {
            TargetFile = Path.GetFileName(_target), SystemAudio = plan.SystemAudio, Microphone = plan.Microphone,
            MixAudio = plan.MixAudio, SystemVolume = plan.SystemVolume, MicrophoneVolume = plan.MicrophoneVolume
        };
        _manifest.Save(_temporaryDirectory);
        ReportStatus(L10n.F($"会话启动：{plan.Bounds} · {plan.Fps} FPS · {plan.EncoderName} · {plan.Container} · 连续分段 · 自动降帧 {plan.AutoPerformance}"));
        ChangeState(RecorderState.Starting);
        try { await BeginSegmentWithFallbackAsync(); ChangeState(RecorderState.Recording); }
        catch
        {
            ChangeState(RecorderState.Idle);
            // No accepted segments exist on a failed initial start.
            if (_manifest.Segments.Count == 0) { try { Directory.Delete(_temporaryDirectory, true); } catch { } }
            throw;
        }
    }

    private async Task BeginSegmentWithFallbackAsync()
    {
        if (_plan is null) throw new InvalidOperationException(L10n.T("无录制方案。"));
        var candidates = new List<RecordPlan> { _plan };
        // Freeze both backend and codec after the first segment: DXGI/GDI can produce different pixel formats.
        if (_manifest.Segments.Count == 0)
        {
            if (_plan.Backend is CaptureBackend.Dxgi or CaptureBackend.WindowsGraphics) candidates.Add(_plan with { Backend = CaptureBackend.Gdi });
            if (_autoCodec && VideoEncoding.IsHardware(_plan.Encoder) && _fallbackEncoder != EncoderChoice.Auto)
                candidates.Add(_plan with { Encoder = _fallbackEncoder, Backend = CaptureBackend.Gdi });
        }
        Exception? last = null;
        foreach (var candidate in candidates)
        {
            try
            {
                await BeginSingleAttemptAsync(candidate);
                _plan = candidate;
                return;
            }
            catch (Exception e)
            {
                last = e;
                ReportStatus(L10n.F($"{candidate.BackendName} / {candidate.EncoderName} 启动失败：{e.Message}"));
                await DisposeCurrentAttemptAsync();
            }
        }
        throw new InvalidOperationException(L10n.T("无法启动录制。已有片段和恢复清单已保留。") + last?.Message, last);
    }

    private async Task BeginSingleAttemptAsync(RecordPlan candidate)
    {
        // A failed attempt can leave extra chunks behind; never reuse their video or audio names.
        int index = ContinuousMedia.NextSegmentIndex(_temporaryDirectory, _manifest.Segments.Count + 1);
        string videoName = $"video-{index:000}.mkv";
        string video = Path.Combine(_temporaryDirectory, videoName);
        _videoSize=new ContinuousVideoSize(video);
        string? system = candidate.SystemAudio ? $"audio-system-{index:000}.wav" : null;
        string? mic = candidate.Microphone ? $"audio-mic-{index:000}.wav" : null;
        var pending = new RecordingSegment(videoName, system, mic, true);
        _manifest.Segments.Add(pending);
        try
        {
            _manifest.Save(_temporaryDirectory); // Persist before launch so a crash leaves a discoverable segment.
            var psi = new ProcessStartInfo(_caps.Ffmpeg)
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardError = true, RedirectStandardInput = true
            };
            foreach (string argument in ContinuousMedia.Capture(candidate, video, ChunkSeconds)) psi.ArgumentList.Add(argument);
            lock (_errorLines) _errorLines.Clear();
            Interlocked.Exchange(ref _segmentFrames, 0);
            _process = new Process { StartInfo = psi };
            if (!_process.Start()) throw new InvalidOperationException(L10n.T("无法启动 FFmpeg。"));
            _performance.Reset();
            _running.Restart();
            _groupStart = TimeSpan.Zero; _groupBytes = 0;
            _outputReader = ReadProcessOutputAsync(_process);
            _audio = new AudioSession(candidate.SystemAudio, candidate.Microphone, _temporaryDirectory, index, ReportStatus, candidate.SystemDeviceId, candidate.MicrophoneDeviceId, () => _running.Elapsed);
            _audio.Start();
            await Task.Delay(1100);
            if(candidate.Backend==CaptureBackend.WindowsGraphics || VideoEncoding.IsHardware(candidate.Encoder))
            {
                var startup=Stopwatch.StartNew();
                while(!_process.HasExited&&Interlocked.Read(ref _segmentFrames)==0&&startup.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(100);
                if(!_process.HasExited&&Interlocked.Read(ref _segmentFrames)==0)throw new TimeoutException(L10n.T("捕获或硬件编码未收到首帧，尝试兼容方案。"));
            }
            if (_process.HasExited)
            {
                await _outputReader;
                throw new InvalidOperationException(GetRecentError());
            }
            ReportStatus(L10n.F($"开始片段 {index} · {candidate.BackendName} · {candidate.EncoderName}"));
        }
        catch
        {
            _manifest.Segments.Remove(pending);
            try { _manifest.Save(_temporaryDirectory); } catch { }
            throw;
        }
    }

    private async Task ReadProcessOutputAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                _performance.Observe(line);
                if (line.StartsWith("frame=", StringComparison.Ordinal))
                {
                    if (long.TryParse(line[6..], out long frames))
                    {
                        Interlocked.Exchange(ref _segmentFrames, frames);
                        FrameUpdated?.Invoke(FrameCount);
                    }
                }
                else if (!line.Contains('=') || line.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_errorLines)
                    {
                        _errorLines.Add(line);
                        if (_errorLines.Count > 24) _errorLines.RemoveAt(0);
                    }
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch (IOException) { }
    }
    private string GetRecentError() { lock (_errorLines) return string.Join(" | ", _errorLines.TakeLast(5)); }

    private async Task DisposeCurrentAttemptAsync()
    {
        _running.Reset();
        if (_process is { } process)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } catch { }
            await _outputReader;
            process.Dispose(); _process = null;
        }
        if (_audio is not null)
        {
            try { await _audio.StopAsync(); } catch { }
            _audio.Dispose(); _audio = null;
        }
        Interlocked.Exchange(ref _segmentFrames, 0);
    }

    private async Task EndSegmentAsync()
    {
        if (_process is not { } process) return;
        _running.Stop();
        Task audioStopped = _audio?.StopAsync() ?? Task.CompletedTask;
        try
        {
            if (!process.HasExited)
            {
                try { await process.StandardInput.WriteLineAsync("q"); await process.StandardInput.FlushAsync(); }
                catch (IOException) { }
                catch (InvalidOperationException) { }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(16));
                try { await process.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } catch { }
                    throw new TimeoutException(L10n.T("FFmpeg 无法及时结束，将尝试恢复已录内容。"));
                }
            }
            await _outputReader;
            if (process.ExitCode != 0) throw new InvalidOperationException(L10n.F($"FFmpeg 退出码 {process.ExitCode}：{GetRecentError()}"));
        }
        finally
        {
            if (_audio is not null)
            {
                try { await audioStopped; } catch (Exception e) { ReportStatus(L10n.T("音频停止警告：") + e.Message); }
                _audio.Dispose(); _audio = null;
            }
            await _outputReader;
            _completedDuration += _running.Elapsed;
            _running.Reset();
            Interlocked.Add(ref _completedFrames, Interlocked.Exchange(ref _segmentFrames, 0));
            _process.Dispose(); _process = null;
        }
    }
    internal Task RotateSegmentAsync()
    {
        if (State != RecorderState.Recording) return Task.CompletedTask;
        // The segment muxer has already been creating closed independent blocks.
        // Only advance the user-facing group marker; neither capture nor audio restarts.
        _groupBytes=_videoSize?.ReadBytes()??0;
        _groupStart = _running.Elapsed; _automaticParts++;
        ReportStatus(L10n.F($"连续分段 {SegmentCount}：采集进程与音频时间线保持运行，停止时合并。"));
        return Task.CompletedTask;
    }
    internal async Task ChangeFpsAsync(int fps)
    {
        if (State != RecorderState.Recording || _plan is null || fps >= _plan.Fps || fps < 10 || _plan.IsLossless) return;
        ChangeState(RecorderState.Starting);
        try
        {
            int previous = _plan.Fps;
            await EndSegmentAsync(); _plan = _plan with { Fps = fps };
            await BeginSegmentWithFallbackAsync(); ChangeState(RecorderState.Recording);
            ReportStatus(L10n.F($"性能保护：{previous} → {fps} FPS，尺寸与编码器保持不变；重启编码可能有短暂间隔。"));
        }
        catch { ChangeState(RecorderState.Paused); ReportStatus(L10n.T("性能调整失败，已暂停并保留素材。")); throw; }
    }
    public async Task PauseAsync()
    {
        if (State != RecorderState.Recording) return;
        ChangeState(RecorderState.Starting);
        try { await EndSegmentAsync(); ReportStatus(L10n.T("已暂停，可使用浮窗或快捷键继续。")); }
        finally { ChangeState(RecorderState.Paused); }
    }
    public async Task ResumeAsync()
    {
        if (State != RecorderState.Paused) return;
        ReportStatus(L10n.T("请求继续录制。"));
        ChangeState(RecorderState.Starting);
        try { await BeginSegmentWithFallbackAsync(); ChangeState(RecorderState.Recording); ReportStatus(L10n.T("继续录制。")); }
        catch { ChangeState(RecorderState.Paused); throw; }
    }
    public async Task<string> StopAsync(CancellationToken token = default)
    {
        if (State is not (RecorderState.Recording or RecorderState.Paused)) throw new InvalidOperationException(L10n.T("没有正在进行的录制。"));
        ReportStatus(L10n.F($"请求停止保存：状态 {State} · 有效时长 {Duration} · {FrameCount} 帧"));
        ChangeState(RecorderState.Finalizing);
        try
        {
            try { await EndSegmentAsync(); }
            catch (Exception e) { ReportStatus(L10n.T("片段结束异常，继续尝试保留内容：") + e.Message); }
            _target = await RecordingFinalizer.ExportAsync(_caps, _temporaryDirectory, _manifest, ReportStatus,p=>FinalizationProgress?.Invoke(p),token);
            ReportStatus(L10n.T("录制完成：") + _target);
            try { Directory.Delete(_temporaryDirectory, true); } catch { }
            return _target;
        }
        catch (OperationCanceledException) { ReportStatus(L10n.T("已取消保存，原始素材已保留，可通过恢复录制重试。"));throw; }
        catch (Exception e)
        {
            throw new InvalidOperationException(L10n.T("合成失败，请通过「恢复录制」重试。素材保存在 ") + _temporaryDirectory + L10n.T("。原因：") + e.Message, e);
        }
        finally { ChangeState(RecorderState.Idle); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.WriteLine("q");
                    if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            process.Dispose(); _process = null;
        }
        _audio?.Dispose();
        // Keep work folder and manifest when the application cannot finish normally.
    }
}

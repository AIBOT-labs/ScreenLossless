using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace ScreenLossless;

internal interface IAudioCaptureSource : IDisposable
{
    WaveFormat Format { get; }
    event EventHandler<WaveInEventArgs>? Data;
    event EventHandler<StoppedEventArgs>? Stopped;
    void Start();
    void Stop();
}
internal sealed class WasapiSource : IAudioCaptureSource
{
    private readonly WasapiCapture _capture;
    private readonly MMDevice _device;
    private readonly AudioEndpointWatcher? _watcher;
    public WaveFormat Format => _capture.WaveFormat;
    public event EventHandler<WaveInEventArgs>? Data;
    public event EventHandler<StoppedEventArgs>? Stopped;
    internal WasapiSource(bool system, string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var flow = system ? DataFlow.Render : DataFlow.Capture;
            _device = string.IsNullOrEmpty(id) ? enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia) : enumerator.GetDevice(id);
            if (_device.State != DeviceState.Active || _device.DataFlow != flow)
                throw new InvalidOperationException(L10n.T("所选音频设备已断开，等待重新连接。"));
            _capture = system ? new WasapiLoopbackCapture(_device) : new WasapiCapture(_device);
            string activeId = _device.ID;
            _watcher = new AudioEndpointWatcher(change =>
            {
                if (AudioEndpointWatcher.Affects(change, id, activeId, flow))
                    Stopped?.Invoke(this, new StoppedEventArgs(new IOException(L10n.T("音频端点或默认设备发生变化，等待重新连接。"))));
            });
            _capture.DataAvailable += OnData; _capture.RecordingStopped += OnStopped;
        }
        catch { _watcher?.Dispose(); _capture?.Dispose(); _device?.Dispose(); throw; }
    }
    private void OnData(object? sender, WaveInEventArgs e) => Data?.Invoke(this, e);
    private void OnStopped(object? sender, StoppedEventArgs e) => Stopped?.Invoke(this, e);
    public void Start() => _capture.StartRecording();
    public void Stop() => _capture.StopRecording();
    public void Dispose()
    {
        _capture.DataAvailable -= OnData; _capture.RecordingStopped -= OnStopped;
        try { _watcher?.Dispose(); }
        finally { try { _capture.Dispose(); } finally { _device.Dispose(); } }
    }
}

internal sealed class AudioSession : IDisposable
{
    private readonly RecoveringAudioTrack? _system, _mic;
    public string? SystemAudio => _system?.FilePath;
    public string? MicrophoneAudio => _mic?.FilePath;
    internal float SystemLevel => _system?.Level ?? 0;
    internal float MicrophoneLevel => _mic?.Level ?? 0;
    internal bool SystemAvailable => _system?.Available == true;
    internal bool MicrophoneAvailable => _mic?.Available == true;
    public AudioSession(bool system, bool mic, string directory, int index, Action<string> log, string systemDeviceId = "", string microphoneDeviceId = "", Func<TimeSpan>? elapsed = null)
    {
        try
        {
            if (system) _system = new(Path.Combine(directory, $"audio-system-{index:000}.wav"), () => new WasapiSource(true, systemDeviceId), m => log(L10n.T("系统声音：") + m), elapsed: elapsed);
            if (mic) _mic = new(Path.Combine(directory, $"audio-mic-{index:000}.wav"), () => new WasapiSource(false, microphoneDeviceId), m => log(L10n.T("麦克风：") + m), elapsed: elapsed);
        }
        catch { _system?.Dispose(); _mic?.Dispose(); throw; }
    }
    public void Start() { _system?.Start(); _mic?.Start(); }
    public Task StopAsync() => Task.WhenAll(_system?.StopAsync() ?? Task.CompletedTask, _mic?.StopAsync() ?? Task.CompletedTask);
    public void Dispose() { _system?.Dispose(); _mic?.Dispose(); }
}

// A stable 48 kHz stereo float timeline survives device loss and format changes.
// Device operations never hold the writer lock: WASAPI disposal can join its event thread.
internal sealed class RecoveringAudioTrack : IDisposable
{
    internal static readonly WaveFormat TimelineFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private readonly Func<IAudioCaptureSource> _factory;
    private readonly Action<string> _notify;
    private readonly object _writeLock = new();
    private readonly SemaphoreSlim _maintenance = new(1, 1);
    private readonly Stopwatch _clock = new();
    private readonly Func<TimeSpan> _elapsed;
    private readonly CancellationTokenSource _cancel = new();
    private readonly long _chunkLimit;
    private readonly AudioPeak _peak = new();
    private readonly byte[] _silence = new byte[384000];
    private readonly float[] _samples = new float[96000];
    private readonly float[] _aligned = new float[97000];
    private readonly byte[] _converted = new byte[388000];
    private long _correctedFrames;
    private WaveFileWriter _writer;
    private IAudioCaptureSource? _capture;
    private BufferedWaveProvider? _input;
    private ISampleProvider? _converter;
    private Task _loop = Task.CompletedTask;
    private Task? _stopTask;
    private volatile bool _available, _stopping, _disposed, _writeFailed;
    private bool _started, _everConnected;
    private long _completedBytes;
    private int _chunk;
    private TimeSpan _lastFailure = TimeSpan.FromSeconds(-30);
    public string FilePath { get; }
    internal bool Available => _available && !_disposed && !_writeFailed;
    internal float Level => Available ? _peak.Level : 0;
    internal RecoveringAudioTrack(string file, Func<IAudioCaptureSource> factory, Action<string> log,
        long chunkLimit = 256L * 1024 * 1024, Func<TimeSpan>? elapsed = null)
    {
        FilePath = file; _factory = factory; _notify = log;
        _chunkLimit = Math.Max(8, chunkLimit / 8 * 8); _elapsed = elapsed ?? (() => _clock.Elapsed);
        _writer = new WaveFileWriter(file, TimelineFormat);
    }
    internal void Start()
    {
        if (_started || _stopping) return;
        _started = true; _clock.Start();
        // Open and retry away from the UI thread, including a missing device at startup.
        _loop = Task.Run(async () =>
        {
            await RetryAsync();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try { while (await timer.WaitForNextTickAsync(_cancel.Token)) await RetryAsync(); }
            catch (OperationCanceledException) { }
        });
    }
    internal async Task RetryAsync()
    {
        await _maintenance.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping || _disposed || _writeFailed || Available) return;
            CloseCapture();
            lock (_writeLock) { if (!_disposed) PadTo(ExpectedBytes); }
            try
            {
                var source = _factory(); _capture = source;
                _input = new BufferedWaveProvider(source.Format) { ReadFully = false, BufferDuration = TimeSpan.FromSeconds(3) };
                ISampleProvider samples = _input.ToSampleProvider();
                if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
                else if (samples.WaveFormat.Channels != 2)
                {
                    var stereo = new MultiplexingSampleProvider(new[] { samples }, 2);
                    stereo.ConnectInputToOutput(0, 0); stereo.ConnectInputToOutput(1, 1); samples = stereo;
                }
                _converter = samples.WaveFormat.SampleRate == 48000 ? samples : new WdlResamplingSampleProvider(samples, 48000);
                source.Data += DataAvailable; source.Stopped += RecordingStopped;
                _available = true; source.Start();
                if (_available) { _notify(_everConnected ? L10n.T("设备已重新连接，继续录制；断开期间保留静音。") : L10n.T("设备已连接。")); _everConnected = true; }
            }
            catch (Exception e)
            {
                _available = false; CloseCapture();
                if (_elapsed() - _lastFailure >= TimeSpan.FromSeconds(30))
                { _lastFailure = _elapsed(); _notify(L10n.T("设备不可用，每 2 秒重试；当前补静音：") + e.Message); }
            }
        }
        catch (Exception e) { _writeFailed = true; _notify(L10n.T("音频维护失败：") + e.Message); }
        finally { _maintenance.Release(); }
    }
    private void RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _capture)) return;
        _available = false;
        if (!_stopping) _notify(L10n.T("设备断开，等待恢复。") + e.Exception?.Message);
    }
    private long ExpectedBytes => (long)(_elapsed().TotalSeconds * TimelineFormat.AverageBytesPerSecond) / 8 * 8;
    private long Written => _completedBytes + _writer.Length;
    private void Write(byte[] bytes, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            if (_writer.Length >= _chunkLimit)
            {
                _completedBytes += _writer.Length; _writer.Dispose(); _chunk++;
                string file = Path.Combine(Path.GetDirectoryName(FilePath)!, Path.GetFileNameWithoutExtension(FilePath) + $"-part-{_chunk:000000}.wav");
                _writer = new WaveFileWriter(file, TimelineFormat);
            }
            int amount = (int)Math.Min(count - offset, _chunkLimit - _writer.Length);
            _writer.Write(bytes, offset, amount); offset += amount;
        }
    }
    private void PadTo(long expected)
    {
        while (Written < expected) Write(_silence, (int)Math.Min(_silence.Length, expected - Written));
    }
    private void DataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_writeLock)
        {
            if (_disposed || _stopping || _writeFailed || !_available || !ReferenceEquals(sender, _capture) || e.BytesRecorded == 0) return;
            try
            {
                _input!.AddSamples(e.Buffer, 0, e.BytesRecorded);
                int count;
                while ((count = _converter!.Read(_samples, 0, _samples.Length)) > 0)
                {
                    int frames = count / 2;
                    var correction = AudioClockCorrection.Plan(frames, Written / 8, _elapsed() - _input.BufferedDuration);
                    PadTo(Written + correction.SilenceFrames * 8);
                    AudioClockCorrection.ResampleStereo(_samples, frames, _aligned, correction.OutputFrames);
                    int bytes = correction.OutputFrames * 8;
                    _correctedFrames += correction.OutputFrames - frames;
                    Buffer.BlockCopy(_aligned, 0, _converted, 0, bytes);
                    _peak.Observe(_converted, bytes, TimelineFormat); Write(_converted, bytes);
                }
            }
            catch (Exception ex) { _writeFailed = true; _available = false; _notify(L10n.T("音频写入失败：") + ex.Message); }
        }
    }
    private void CloseCapture()
    {
        var capture = _capture; _capture = null; _available = false;
        if (capture is null) return;
        capture.Data -= DataAvailable; capture.Stopped -= RecordingStopped;
        try { capture.Stop(); } catch { }
        try { capture.Dispose(); } catch { }
    }
    internal Task StopAsync()
    {
        lock (_writeLock)
        {
            if (_stopTask is not null) return _stopTask;
            _stopping = true; _cancel.Cancel(); // Fence callbacks before the worker is scheduled.
            return _stopTask = Task.Run(StopCoreAsync);
        }
    }
    private async Task StopCoreAsync()
    {
        _stopping = true; _cancel.Cancel();
        // Run on the worker so disposal never joins a WASAPI callback under the writer lock.
        await _loop.ConfigureAwait(false);
        await _maintenance.WaitAsync().ConfigureAwait(false);
        try
        {
            CloseCapture(); _clock.Stop();
            lock (_writeLock)
            {
                if (_disposed) return;
                try { if (!_writeFailed) PadTo(ExpectedBytes); }
                catch (Exception e) { _writeFailed=true; _notify(L10n.T("结束音频补齐失败，保留已写内容：")+e.Message); }
                finally
                {
                    long totalBytes = Written;
                    try { _writer.Dispose(); } catch (Exception e) { _notify(L10n.T("音频封装失败，保留素材：")+e.Message); }
                    _disposed=true;
                    _notify(L10n.F($"音频时间线结束：{totalBytes / 384000d:0.000} 秒；时钟校正 {_correctedFrames} 帧（48 kHz）；断开缺口补静音。"));
                }
            }
        }
        finally { _maintenance.Release(); }
    }
    public void Dispose()
    {
        // Dispose may be called by synchronous form shutdown on the UI thread.
        Task.Run(StopAsync).GetAwaiter().GetResult();
    }
}

using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.Buffers.Binary;
using System.Diagnostics;

namespace ScreenLossless;

internal sealed class AudioPeak
{
    private float _peak;
    private long _updated;
    internal float Level => Stopwatch.GetElapsedTime(Interlocked.Read(ref _updated)).TotalMilliseconds<450?Volatile.Read(ref _peak):0;
    internal void Observe(byte[] buffer,int count,WaveFormat format)
    {
        float peak=Measure(buffer.AsSpan(0,Math.Min(count,buffer.Length)),format);
        Volatile.Write(ref _peak,peak);Interlocked.Exchange(ref _updated,Stopwatch.GetTimestamp());
    }
    internal static float Measure(ReadOnlySpan<byte> bytes,WaveFormat format)
    {
        bool floating=format.Encoding==WaveFormatEncoding.IeeeFloat||
            format is WaveFormatExtensible ext&&ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
        int step=format.BitsPerSample/8;if(step<1)return 0;
        float peak=0;
        for(int i=0;i+step<=bytes.Length;i+=step)
        {
            float value;
            if(floating&&step==4)value=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes[i..]));
            else if(!floating&&step==1)value=(bytes[i]-128)/128f;
            else if(!floating&&step==2)value=BinaryPrimitives.ReadInt16LittleEndian(bytes[i..])/32768f;
            else if(!floating&&step==3){int n=bytes[i]|bytes[i+1]<<8|bytes[i+2]<<16;value=(n<<8>>8)/8388608f;}
            else if(!floating&&step==4)value=BinaryPrimitives.ReadInt32LittleEndian(bytes[i..])/2147483648f;
            else continue;
            if(float.IsFinite(value))peak=Math.Max(peak,Math.Abs(value));
        }
        return Math.Clamp(peak,0,1);
    }
}
// A preview capture has no writer or output file. It lives only while the idle sound page is visible.
internal sealed class AudioLevelPreview : IDisposable
{
    private readonly List<IAudioCaptureSource> _tracks=new();
    private readonly bool _systemEnabled, _micEnabled;
    internal bool NeedsReconnect => (_systemEnabled && !SystemAvailable) || (_micEnabled && !MicrophoneAvailable);
    private readonly AudioPeak _system=new(),_mic=new();
    internal float SystemLevel=>_system.Level;
    internal float MicrophoneLevel=>_mic.Level;
    private volatile bool _systemAvailable, _microphoneAvailable;
    internal bool SystemAvailable => _systemAvailable;
    internal bool MicrophoneAvailable => _microphoneAvailable;
    internal AudioLevelPreview(bool system,bool mic,string systemId,string micId,Action<string> log,
        Func<bool, string, IAudioCaptureSource>? factory = null)
    {
        _systemEnabled = system; _micEnabled = mic;
        factory ??= (loopback, id) => new WasapiSource(loopback, id);
        if (system) Start(true, systemId, _system, log, factory);
        if (mic) Start(false, micId, _mic, log, factory);
    }
    private void SetAvailable(bool system, bool available)
    { if (system) _systemAvailable = available; else _microphoneAvailable = available; }
    private void Start(bool system,string id,AudioPeak peak,Action<string> log,Func<bool, string, IAudioCaptureSource> factory)
    {
        IAudioCaptureSource? capture = null;
        try
        {
            capture = factory(system, id);
            var source = capture;
            capture.Data += (_, e) => peak.Observe(e.Buffer, e.BytesRecorded, source.Format);
            capture.Stopped += (_, _) => SetAvailable(system, false);
            // A disconnect can be raised inside Start; never overwrite that notification afterwards.
            SetAvailable(system, true);
            capture.Start(); _tracks.Add(capture);
        }
        catch(Exception e)
        {
            SetAvailable(system, false);
            try { capture?.Dispose(); } catch (Exception cleanup) { Trace.WriteLine(cleanup); }
            log((system?L10n.T("系统声音"):L10n.T("麦克风"))+L10n.T("电平预览不可用：")+e.Message);
        }
    }
    public void Dispose()
    {
        foreach(var capture in _tracks)
        {
            try { capture.Stop(); } catch { }
            try { capture.Dispose(); } catch (Exception e) { Trace.WriteLine(e); }
        }
        _tracks.Clear(); _systemAvailable = _microphoneAvailable = false;
    }

}
internal sealed class AudioLevelBar : Control
{
    private float _level;
    private string _status=L10n.T("未启用");
    internal void UpdateLevel(float level,string status)
    {
        level=float.IsFinite(level)?Math.Clamp(level,0,1):0;
        if(_level==level&&_status==status)return;
        _level=level;_status=status;AccessibleName=status;Invalidate();
    }
    public AudioLevelBar(){DoubleBuffered=true;Height=24;Dock=DockStyle.Fill;Margin=Padding.Empty;Font=Ui.Font(8);AccessibleName=_status;}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Ui.ParentColor(this));int w=Math.Max(20,Width-Ui.S(this,165)),h=Ui.S(this,7);
        var r=new Rectangle(0,(Height-h)/2,w,h);using var bg=new SolidBrush(Ui.Border);e.Graphics.FillRectangle(bg,r);
        if(_level>0){using var fill=new SolidBrush(_level>=.95f?Ui.Recording:Ui.Mint);
            e.Graphics.FillRectangle(fill,r.X,r.Y,Math.Max(1,(int)(w*Math.Sqrt(_level))),h);}
        TextRenderer.DrawText(e.Graphics,_status,Font,new Rectangle(w+Ui.S(this,8),0,Width-w-Ui.S(this,8),Height),Ui.Subtle,
            TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
    }
}

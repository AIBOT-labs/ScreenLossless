using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ScreenLossless;

internal sealed record AudioEndpointChange(string Id, DataFlow? Flow = null, Role? Role = null, bool DefaultChanged = false);

// Windows calls these on a COM callback thread. Only mark the stream invalid here;
// reopening and disposing WASAPI stays on RecoveringAudioTrack's worker.
internal sealed class AudioEndpointWatcher : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Action<AudioEndpointChange> _changed;
    private volatile bool _disposed;
    internal AudioEndpointWatcher(Action<AudioEndpointChange> changed)
    {
        _changed = changed;
        try { _enumerator.RegisterEndpointNotificationCallback(this); }
        catch { _enumerator.Dispose(); throw; }
    }
    internal static bool Affects(AudioEndpointChange change, string selectedId, string activeId, DataFlow flow)
        => change.DefaultChanged
            ? string.IsNullOrEmpty(selectedId) && change.Flow == flow && change.Role == Role.Multimedia
                && !string.Equals(change.Id, activeId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(change.Id, activeId, StringComparison.OrdinalIgnoreCase);
    private void Notify(AudioEndpointChange change)
    {
        if (_disposed) return;
        try { _changed(change); } catch { /* Never propagate managed exceptions into Windows callbacks. */ }
    }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { if (newState != DeviceState.Active) Notify(new(deviceId)); }
    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) => Notify(new(deviceId));
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Notify(new(defaultDeviceId, flow, role, true));
    public void OnPropertyValueChanged(string id, PropertyKey key)
    {
        var format = PropertyKeys.PKEY_AudioEngine_DeviceFormat;
        if (key.formatId == format.formatId && key.propertyId == format.propertyId) Notify(new(id));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _enumerator.UnregisterEndpointNotificationCallback(this); }
        finally { _enumerator.Dispose(); }
    }
}

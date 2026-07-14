using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioMixer.Services;

public record DeviceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Enumerates audio endpoints and raises DevicesChanged on hotplug.</summary>
public sealed class DeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly NotificationClient _notifications;

    /// <summary>Raised on an arbitrary thread when any endpoint is added/removed/changed.</summary>
    public event Action? DevicesChanged;

    public DeviceService()
    {
        _notifications = new NotificationClient(() => DevicesChanged?.Invoke());
        _enumerator.RegisterEndpointNotificationCallback(_notifications);
    }

    public List<DeviceInfo> GetOutputs() => Enumerate(DataFlow.Render);
    public List<DeviceInfo> GetInputs() => Enumerate(DataFlow.Capture);

    private List<DeviceInfo> Enumerate(DataFlow flow)
    {
        var list = new List<DeviceInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            try { list.Add(new DeviceInfo(device.ID, device.FriendlyName)); }
            catch { }
            finally { device.Dispose(); }
        }
        return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public MMDevice? GetDevice(string id)
    {
        try { return _enumerator.GetDevice(id); }
        catch { return null; }
    }

    public MMDevice GetDefaultOutput() =>
        _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    /// <summary>Finds the VB-Cable playback endpoint ("CABLE Input"), if installed.</summary>
    public DeviceInfo? FindCableInput() =>
        GetOutputs().FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch { }
        _enumerator.Dispose();
    }

    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly Action _changed;
        public NotificationClient(Action changed) => _changed = changed;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _changed();
        public void OnDeviceAdded(string pwstrDeviceId) => _changed();
        public void OnDeviceRemoved(string deviceId) => _changed();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}

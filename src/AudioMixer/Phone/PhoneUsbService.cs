namespace AudioMixer.Phone;

public enum PhoneUsbState
{
    ToolsMissing,
    NoDevice,
    Unauthorized,   // plugged in, but the "allow USB debugging" prompt not accepted
    Ready,          // plugged in and authorized, not streaming
    Streaming,
}

/// <summary>Polls adb for a connected phone and tracks the active USB audio stream.</summary>
public sealed class PhoneUsbService : IDisposable
{
    private readonly AdbService _adb;
    private readonly System.Timers.Timer _pollTimer;
    private bool _polling;

    public PhoneUsbState State { get; private set; }
    public AdbDevice? Device { get; private set; }
    public AdbPhoneSource? ActiveSource { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Raised (on a worker thread) whenever State/Device/ActiveSource change.</summary>
    public event Action? Changed;

    public PhoneUsbService(AdbService adb)
    {
        _adb = adb;
        State = adb.ToolsAvailable ? PhoneUsbState.NoDevice : PhoneUsbState.ToolsMissing;
        _pollTimer = new System.Timers.Timer(3000) { AutoReset = true };
        _pollTimer.Elapsed += async (_, _) => await PollAsync();
        if (adb.ToolsAvailable) _pollTimer.Start();
    }

    public async Task PollAsync()
    {
        if (_polling || !_adb.ToolsAvailable) return;
        _polling = true;
        try
        {
            var devices = await _adb.GetDevicesAsync().ConfigureAwait(false);
            var device = devices.FirstOrDefault(d => d.State == "device")
                      ?? devices.FirstOrDefault(d => d.State == "unauthorized")
                      ?? devices.FirstOrDefault();

            var newState = State;
            if (ActiveSource is not null)
            {
                // Streaming; if the phone vanished the source's Stopped event handles it.
                newState = PhoneUsbState.Streaming;
            }
            else if (device is null) newState = PhoneUsbState.NoDevice;
            else if (device.State == "unauthorized") newState = PhoneUsbState.Unauthorized;
            else if (device.State == "device") newState = PhoneUsbState.Ready;
            else newState = PhoneUsbState.NoDevice;

            bool changed = newState != State || device?.Serial != Device?.Serial;
            State = newState;
            Device = device;
            if (changed) Changed?.Invoke();
        }
        catch { /* adb hiccup; retry next tick */ }
        finally { _polling = false; }
    }

    /// <summary>Creates (but does not start) the USB audio source. It starts when a route uses it.</summary>
    public AdbPhoneSource? Connect()
    {
        if (State != PhoneUsbState.Ready || Device is null) return null;
        var source = new AdbPhoneSource(_adb, Device.Serial, Device.Model);
        source.Stopped += ex =>
        {
            LastError = ex?.Message;
            if (ActiveSource == source)
            {
                ActiveSource = null;
                State = PhoneUsbState.NoDevice;
                Changed?.Invoke();
            }
        };
        ActiveSource = source;
        State = PhoneUsbState.Streaming;
        LastError = null;
        Changed?.Invoke();
        return source;
    }

    public void Disconnect()
    {
        var source = ActiveSource;
        ActiveSource = null;
        source?.Dispose();
        State = _adb.ToolsAvailable ? PhoneUsbState.NoDevice : PhoneUsbState.ToolsMissing;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        Disconnect();
    }
}

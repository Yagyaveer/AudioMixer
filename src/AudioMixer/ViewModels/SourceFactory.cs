using AudioMixer.Audio;
using AudioMixer.Audio.ProcessLoopback;
using AudioMixer.Phone;
using AudioMixer.Services;

namespace AudioMixer.ViewModels;

public record SourceOption(SourceKind Kind, string Id, string Name, string Badge)
{
    public override string ToString() => Name;
}

/// <summary>Creates a live IAudioSource from a persisted (kind, id) pair.</summary>
public sealed class SourceFactory
{
    private readonly DeviceService _devices;
    private readonly PhoneUsbService _phone;

    public SourceFactory(DeviceService devices, PhoneUsbService phone)
    {
        _devices = devices;
        _phone = phone;
    }

    public (IAudioSource? Source, string? Error) Create(SourceKind kind, string id, string name)
    {
        switch (kind)
        {
            case SourceKind.InputDevice:
            {
                var device = _devices.GetDevice(id);
                if (device is null) return (null, $"Input device not found: {name}");
                return (new DeviceSource(device, loopback: false), null);
            }
            case SourceKind.OutputLoopback:
            {
                var device = _devices.GetDevice(id);
                if (device is null) return (null, $"Output device not found: {name}");
                return (new DeviceSource(device, loopback: true), null);
            }
            case SourceKind.Process:
            {
                var app = AudioSessionHelper.FindByProcessName(id);
                if (app is null) return (null, $"Waiting for {name} to play audio…");
                return (new ProcessLoopbackSource(app.ProcessId, app.DisplayName), null);
            }
            case SourceKind.PhoneUsb:
            {
                if (_phone.ActiveSource is not null) return (_phone.ActiveSource, null);
                var source = _phone.Connect();
                if (source is null) return (null, "Phone not connected — see the Phone page.");
                return (source, null);
            }
            default:
                return (null, "Unknown source.");
        }
    }

    /// <summary>Everything that can currently be picked as a source.</summary>
    public List<SourceOption> BuildOptions(bool includePhone = true)
    {
        var options = new List<SourceOption>();
        foreach (var app in AudioSessionHelper.GetAudioApps())
            options.Add(new SourceOption(SourceKind.Process, app.ProcessName, app.DisplayName, "APP"));
        foreach (var d in _devices.GetOutputs())
            options.Add(new SourceOption(SourceKind.OutputLoopback, d.Id, $"What's playing on {d.Name}", "PLAYBACK"));
        foreach (var d in _devices.GetInputs())
            options.Add(new SourceOption(SourceKind.InputDevice, d.Id, d.Name, "INPUT"));
        if (includePhone)
            options.Add(new SourceOption(SourceKind.PhoneUsb, "phone-usb", "Phone (USB)", "PHONE"));
        return options;
    }
}

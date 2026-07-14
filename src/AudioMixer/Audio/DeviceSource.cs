using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioMixer.Audio;

/// <summary>
/// Captures a physical input device (mic / line-in), or — in loopback mode —
/// whatever an output device is currently playing.
/// </summary>
public sealed class DeviceSource : IAudioSource
{
    private readonly WasapiCapture _capture;
    private bool _started;

    public string Name { get; }
    public WaveFormat WaveFormat => _capture.WaveFormat;

    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;

    public DeviceSource(MMDevice device, bool loopback)
    {
        Name = loopback ? $"What's playing on {device.FriendlyName}" : device.FriendlyName;
        _capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device, true, 20);
        _capture.DataAvailable += (_, e) => DataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
        _capture.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
    }

    public void Start()
    {
        if (_started) return;
        _capture.StartRecording();
        _started = true;
    }

    public void Stop()
    {
        if (!_started) return;
        try { _capture.StopRecording(); } catch { /* device may be gone */ }
        _started = false;
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
    }
}

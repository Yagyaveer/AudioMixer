using NAudio.Wave;

namespace AudioMixer.Audio;

/// <summary>A running audio producer (device capture, loopback, app capture, phone…).</summary>
public interface IAudioSource : IDisposable
{
    string Name { get; }
    WaveFormat WaveFormat { get; }
    /// <summary>Raised from the capture thread with (buffer, byteCount).</summary>
    event Action<byte[], int>? DataAvailable;
    event Action<Exception?>? Stopped;
    void Start();
    void Stop();
}

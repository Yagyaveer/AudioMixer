using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioMixer.Audio;

/// <summary>
/// Plays a stream of source-format bytes on one output device.
/// WasapiOut (shared mode) resamples to the device mix format automatically.
/// </summary>
public sealed class RenderSink : IDisposable
{
    private readonly BufferedWaveProvider _buffer;
    private readonly VolumeSampleProvider _volume;
    private readonly WasapiOut _output;

    public string DeviceId { get; }
    public string DeviceName { get; }

    public RenderSink(MMDevice device, WaveFormat sourceFormat, int latencyMs)
    {
        DeviceId = device.ID;
        DeviceName = device.FriendlyName;

        _buffer = new BufferedWaveProvider(sourceFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
            ReadFully = true, // keep the output alive with silence when the source pauses
        };

        ISampleProvider samples = _buffer.ToSampleProvider();
        if (sourceFormat.Channels == 1)
            samples = new MonoToStereoSampleProvider(samples);
        _volume = new VolumeSampleProvider(samples);

        _output = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs);
        _output.Init(new SampleToWaveProvider(_volume));
        _output.Play();
    }

    public float Volume
    {
        get => _volume.Volume;
        set => _volume.Volume = value;
    }

    public void Write(byte[] data, int count)
    {
        // Clock drift guard: never let latency build past ~250 ms.
        if (_buffer.BufferedDuration.TotalMilliseconds > 250)
            _buffer.ClearBuffer();
        _buffer.AddSamples(data, 0, count);
    }

    public void Dispose()
    {
        try { _output.Stop(); } catch { }
        _output.Dispose();
    }
}

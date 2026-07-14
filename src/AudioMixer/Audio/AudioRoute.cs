using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioMixer.Audio;

/// <summary>One source fanned out to any number of output devices.</summary>
public sealed class AudioRoute : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, RenderSink> _sinks = new();
    private readonly int _latencyMs;
    private float _volume = 1f;
    private bool _muted;
    private bool _running;

    public IAudioSource Source { get; }
    /// <summary>Peak level 0..1 of the most recent buffer (for VU meters).</summary>
    public float Peak { get; private set; }
    public event Action<Exception?>? SourceStopped;

    public AudioRoute(IAudioSource source, int latencyMs)
    {
        Source = source;
        _latencyMs = latencyMs;
        source.DataAvailable += OnData;
        source.Stopped += ex => SourceStopped?.Invoke(ex);
    }

    public float Volume
    {
        get => _volume;
        set { _volume = value; ApplyVolume(); }
    }

    public bool Muted
    {
        get => _muted;
        set { _muted = value; ApplyVolume(); }
    }

    public IReadOnlyCollection<string> OutputIds
    {
        get { lock (_lock) return _sinks.Keys.ToArray(); }
    }

    public void AddOutput(MMDevice device)
    {
        lock (_lock)
        {
            if (_sinks.ContainsKey(device.ID)) return;
            var sink = new RenderSink(device, Source.WaveFormat, _latencyMs)
            {
                Volume = _muted ? 0f : _volume,
            };
            _sinks[device.ID] = sink;
        }
        EnsureRunning();
    }

    public void RemoveOutput(string deviceId)
    {
        RenderSink? sink;
        lock (_lock)
        {
            if (!_sinks.Remove(deviceId, out sink)) return;
        }
        sink?.Dispose();
        if (OutputIds.Count == 0) StopIfIdle();
    }

    private void EnsureRunning()
    {
        if (_running) return;
        Source.Start();
        _running = true;
    }

    private void StopIfIdle()
    {
        if (!_running) return;
        Source.Stop();
        _running = false;
        Peak = 0;
    }

    private void ApplyVolume()
    {
        float v = _muted ? 0f : _volume;
        lock (_lock)
        {
            foreach (var s in _sinks.Values) s.Volume = v;
        }
    }

    private void OnData(byte[] buffer, int count)
    {
        Peak = PeakMeter.Compute(buffer, count, Source.WaveFormat);
        lock (_lock)
        {
            foreach (var s in _sinks.Values) s.Write(buffer, count);
        }
    }

    public void Dispose()
    {
        Source.DataAvailable -= OnData;
        StopIfIdle();
        lock (_lock)
        {
            foreach (var s in _sinks.Values) s.Dispose();
            _sinks.Clear();
        }
        Source.Dispose();
    }
}

public static class PeakMeter
{
    public static float Compute(byte[] buffer, int count, WaveFormat format)
    {
        float peak = 0;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= count; i += 4)
            {
                float v = Math.Abs(BitConverter.ToSingle(buffer, i));
                if (v > peak) peak = v;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= count; i += 2)
            {
                float v = Math.Abs(BitConverter.ToInt16(buffer, i) / 32768f);
                if (v > peak) peak = v;
            }
        }
        return Math.Min(peak, 1f);
    }
}

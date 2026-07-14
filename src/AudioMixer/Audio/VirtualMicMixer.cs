using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioMixer.Audio;

/// <summary>
/// Mixes N sources (real mic + game/browser/phone audio) into the VB-Cable
/// input device. Apps that use "CABLE Output" as their microphone hear the mix.
/// </summary>
public sealed class VirtualMicMixer : IDisposable
{
    private static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private readonly MixingSampleProvider _mixer;
    private readonly WasapiOut _output;
    private readonly object _lock = new();
    private readonly Dictionary<string, MixInput> _inputs = new();

    public float Peak { get; private set; }

    public VirtualMicMixer(MMDevice cableInputDevice, int latencyMs)
    {
        _mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
        _output = new WasapiOut(cableInputDevice, AudioClientShareMode.Shared, true, latencyMs);
        var metered = new MeteringSampleProvider(_mixer, 4800);
        metered.StreamVolume += (_, e) => Peak = Math.Min(1f, e.MaxSampleValues.Max());
        _output.Init(new SampleToWaveProvider(metered));
        _output.Play();
    }

    /// <summary>Adds a source to the mix. The key identifies it for gain updates/removal.</summary>
    public void AddInput(string key, IAudioSource source, float gain)
    {
        lock (_lock)
        {
            if (_inputs.ContainsKey(key)) return;
            var input = new MixInput(source, gain);
            _inputs[key] = input;
            _mixer.AddMixerInput(input.Provider);
            source.Start();
        }
    }

    public void RemoveInput(string key)
    {
        MixInput? input;
        lock (_lock)
        {
            if (!_inputs.Remove(key, out input)) return;
            _mixer.RemoveMixerInput(input.Provider);
        }
        input?.Dispose();
    }

    public void SetGain(string key, float gain)
    {
        lock (_lock)
        {
            if (_inputs.TryGetValue(key, out var input)) input.Volume.Volume = gain;
        }
    }

    public IReadOnlyCollection<string> InputKeys
    {
        get { lock (_lock) return _inputs.Keys.ToArray(); }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var input in _inputs.Values) input.Dispose();
            _inputs.Clear();
        }
        try { _output.Stop(); } catch { }
        _output.Dispose();
    }

    private sealed class MixInput : IDisposable
    {
        private readonly IAudioSource _source;
        private readonly BufferedWaveProvider _buffer;
        public VolumeSampleProvider Volume { get; }
        public ISampleProvider Provider { get; }

        public MixInput(IAudioSource source, float gain)
        {
            _source = source;
            _buffer = new BufferedWaveProvider(source.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2),
                ReadFully = true,
            };
            ISampleProvider samples = _buffer.ToSampleProvider();
            if (source.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
            if (source.WaveFormat.SampleRate != MixFormat.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, MixFormat.SampleRate);
            Volume = new VolumeSampleProvider(samples) { Volume = gain };
            Provider = Volume;
            source.DataAvailable += OnData;
        }

        private void OnData(byte[] data, int count)
        {
            if (_buffer.BufferedDuration.TotalMilliseconds > 250) _buffer.ClearBuffer();
            _buffer.AddSamples(data, 0, count);
        }

        public void Dispose()
        {
            _source.DataAvailable -= OnData;
            _source.Dispose();
        }
    }
}

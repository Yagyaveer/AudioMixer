using System.Runtime.InteropServices;
using NAudio.Wave;

namespace AudioMixer.Audio.ProcessLoopback;

/// <summary>
/// Captures the audio of a single process (and its children) via
/// AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK. Requires Windows 10 2004+.
/// Delivers 32-bit float, 48 kHz, stereo.
/// </summary>
public sealed class ProcessLoopbackSource : IAudioSource
{
    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    private readonly int _processId;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private AutoResetEvent? _event;
    private Thread? _thread;
    private volatile bool _stop;
    private bool _started;

    public string Name { get; }
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;

    public ProcessLoopbackSource(int processId, string displayName)
    {
        _processId = processId;
        Name = displayName;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = $"ProcLoopback:{_processId}", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    private void Run()
    {
        Exception? error = null;
        try
        {
            Activate();
            CaptureLoop();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            Cleanup();
            Stopped?.Invoke(error);
        }
    }

    private void Activate()
    {
        var handler = new ActivationHandler();
        var activationParams = new AudioClientActivationParams
        {
            ActivationType = 1, // process loopback
            TargetProcessId = _processId,
            ProcessLoopbackMode = 0, // include process tree
        };

        int paramsSize = Marshal.SizeOf<AudioClientActivationParams>();
        IntPtr paramsPtr = Marshal.AllocHGlobal(paramsSize);
        IntPtr propVariantPtr = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(activationParams, paramsPtr, false);
            var propVariant = new PropVariantBlob
            {
                vt = NativeMethods.VtBlob,
                cbSize = (uint)paramsSize,
                pBlobData = paramsPtr,
            };
            propVariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
            Marshal.StructureToPtr(propVariant, propVariantPtr, false);

            Guid iid = IID_IAudioClient;
            NativeMethods.ActivateAudioInterfaceAsync(
                NativeMethods.VirtualAudioDeviceProcessLoopback,
                ref iid, propVariantPtr, handler, out _);

            if (!handler.Result.Task.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Audio interface activation timed out.");
            _audioClient = (IAudioClient)handler.Result.Task.Result;
        }
        finally
        {
            if (propVariantPtr != IntPtr.Zero) Marshal.FreeHGlobal(propVariantPtr);
            Marshal.FreeHGlobal(paramsPtr);
        }

        // Initialize with our fixed format; the loopback engine converts for us.
        var fmt = new WaveFormatEx
        {
            wFormatTag = 3, // IEEE float
            nChannels = (ushort)WaveFormat.Channels,
            nSamplesPerSec = (uint)WaveFormat.SampleRate,
            wBitsPerSample = (ushort)WaveFormat.BitsPerSample,
            nBlockAlign = (ushort)WaveFormat.BlockAlign,
            nAvgBytesPerSec = (uint)WaveFormat.AverageBytesPerSecond,
            cbSize = 0,
        };
        IntPtr fmtPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        try
        {
            Marshal.StructureToPtr(fmt, fmtPtr, false);
            int hr = _audioClient.Initialize(
                0, // shared
                NativeMethods.AudclntStreamflagsLoopback | NativeMethods.AudclntStreamflagsEventcallback,
                2_000_000, // 200 ms buffer
                0, fmtPtr, IntPtr.Zero);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.FreeHGlobal(fmtPtr);
        }

        _event = new AutoResetEvent(false);
        Check(_audioClient.SetEventHandle(_event.SafeWaitHandle.DangerousGetHandle()));

        Guid captureIid = IID_IAudioCaptureClient;
        Check(_audioClient.GetService(ref captureIid, out object svc));
        _captureClient = (IAudioCaptureClient)svc;
        Check(_audioClient.Start());
    }

    private void CaptureLoop()
    {
        byte[] managed = new byte[WaveFormat.AverageBytesPerSecond]; // 1 s scratch
        int blockAlign = WaveFormat.BlockAlign;

        while (!_stop)
        {
            if (!_event!.WaitOne(500)) continue;
            if (_stop) break;

            while (true)
            {
                Check(_captureClient!.GetNextPacketSize(out uint packetFrames));
                if (packetFrames == 0) break;

                Check(_captureClient.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                int bytes = (int)frames * blockAlign;
                if (bytes > 0)
                {
                    if (bytes > managed.Length) managed = new byte[bytes];
                    if ((flags & NativeMethods.AudclntBufferflagsSilent) != 0)
                        Array.Clear(managed, 0, bytes);
                    else
                        Marshal.Copy(data, managed, 0, bytes);
                    DataAvailable?.Invoke(managed, bytes);
                }
                Check(_captureClient.ReleaseBuffer(frames));
            }
        }
    }

    private static void Check(int hr)
    {
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }

    public void Stop()
    {
        _stop = true;
        _event?.Set();
        if (_thread is not null && _thread.IsAlive && Thread.CurrentThread != _thread)
            _thread.Join(1000);
        _started = false;
    }

    private void Cleanup()
    {
        try { _audioClient?.Stop(); } catch { }
        if (_captureClient is not null) { Marshal.ReleaseComObject(_captureClient); _captureClient = null; }
        if (_audioClient is not null) { Marshal.ReleaseComObject(_audioClient); _audioClient = null; }
        _event?.Dispose();
        _event = null;
    }

    public void Dispose() => Stop();
}

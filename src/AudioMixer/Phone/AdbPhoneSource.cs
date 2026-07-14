using System.Diagnostics;
using System.Net.Sockets;
using AudioMixer.Audio;
using NAudio.Wave;

namespace AudioMixer.Phone;

/// <summary>
/// Phone audio over USB: reads the raw PCM stream (48 kHz stereo s16le) that
/// scrcpy-server sends over the adb-forwarded socket.
/// </summary>
public sealed class AdbPhoneSource : IAudioSource
{
    private readonly AdbService _adb;
    private readonly string _serial;
    private Process? _serverProcess;
    private TcpClient? _tcp;
    private Thread? _thread;
    private volatile bool _stop;

    public string Name { get; }
    public WaveFormat WaveFormat { get; } = new WaveFormat(48000, 16, 2);

    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception?>? Stopped;

    public AdbPhoneSource(AdbService adb, string serial, string model)
    {
        _adb = adb;
        _serial = serial;
        Name = $"Phone (USB) — {model}";
    }

    public void Start()
    {
        if (_thread is not null) return;
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "AdbPhoneAudio" };
        _thread.Start();
    }

    private void Run()
    {
        Exception? error = null;
        try
        {
            _serverProcess = _adb.StartAudioServerAsync(_serial).GetAwaiter().GetResult();
            _tcp = ConnectWithRetry();
            _tcp.NoDelay = true;
            var stream = _tcp.GetStream();

            byte[] buffer = new byte[9600]; // 50 ms of 48k/16/2
            while (!_stop)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break; // phone unplugged / server stopped
                DataAvailable?.Invoke(buffer, read);
            }
        }
        catch (Exception ex) when (!_stop)
        {
            error = ex;
        }
        catch { /* stopping */ }
        finally
        {
            Cleanup();
            Stopped?.Invoke(error);
        }
    }

    private TcpClient ConnectWithRetry()
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 10 && !_stop; attempt++)
        {
            try
            {
                var client = new TcpClient();
                client.Connect("127.0.0.1", AdbService.AudioPort);
                return client;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(300);
            }
        }
        throw new InvalidOperationException("Could not connect to the phone audio stream.", last);
    }

    public void Stop()
    {
        _stop = true;
        try { _tcp?.Close(); } catch { }
        if (_thread is not null && Thread.CurrentThread != _thread) _thread.Join(2000);
        _thread = null;
    }

    private void Cleanup()
    {
        try { _tcp?.Close(); } catch { }
        _tcp = null;
        try
        {
            if (_serverProcess is { HasExited: false }) _serverProcess.Kill(true);
            _serverProcess?.Dispose();
        }
        catch { }
        _serverProcess = null;
        _ = _adb.RemoveForwardAsync(_serial);
    }

    public void Dispose() => Stop();
}

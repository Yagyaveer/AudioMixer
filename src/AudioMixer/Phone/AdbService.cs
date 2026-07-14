using System.Diagnostics;
using System.IO;

namespace AudioMixer.Phone;

public record AdbDevice(string Serial, string State, string Model);

/// <summary>Wraps the bundled adb.exe: device detection and scrcpy-server audio startup.</summary>
public sealed class AdbService
{
    private const string RemoteServerPath = "/data/local/tmp/audiomixer-scrcpy-server.jar";
    public const int AudioPort = 27196;

    public string? AdbPath { get; }
    public string? ScrcpyServerPath { get; }
    public string? ScrcpyVersion { get; }
    public bool ToolsAvailable => AdbPath is not null && ScrcpyServerPath is not null && ScrcpyVersion is not null;

    public AdbService()
    {
        string toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        string adb = Path.Combine(toolsDir, "platform-tools", "adb.exe");
        string server = Path.Combine(toolsDir, "scrcpy-server");
        string versionFile = Path.Combine(toolsDir, "scrcpy-server-version.txt");
        if (File.Exists(adb)) AdbPath = adb;
        if (File.Exists(server)) ScrcpyServerPath = server;
        if (File.Exists(versionFile)) ScrcpyVersion = File.ReadAllText(versionFile).Trim();
    }

    public async Task<List<AdbDevice>> GetDevicesAsync()
    {
        var result = new List<AdbDevice>();
        if (AdbPath is null) return result;
        string output = await RunAdbAsync("devices -l").ConfigureAwait(false);
        foreach (var line in output.Split('\n').Skip(1))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            string model = parts.FirstOrDefault(p => p.StartsWith("model:"))?[6..].Replace('_', ' ') ?? parts[0];
            result.Add(new AdbDevice(parts[0], parts[1], model));
        }
        return result;
    }

    /// <summary>Pushes scrcpy-server, sets up the forward, and launches the audio-only server.
    /// Returns the running server process; the caller connects to 127.0.0.1:AudioPort.</summary>
    public async Task<Process> StartAudioServerAsync(string serial)
    {
        if (!ToolsAvailable)
            throw new InvalidOperationException("adb/scrcpy tools are missing. Run scripts/fetch-tools.ps1 and rebuild.");

        uint scid = (uint)Random.Shared.Next(1, int.MaxValue);
        string scidHex = scid.ToString("x8");

        await RunAdbAsync($"-s {serial} push \"{ScrcpyServerPath}\" {RemoteServerPath}").ConfigureAwait(false);
        await RunAdbAsync($"-s {serial} forward tcp:{AudioPort} localabstract:scrcpy_{scidHex}").ConfigureAwait(false);

        var psi = new ProcessStartInfo
        {
            FileName = AdbPath!,
            Arguments = $"-s {serial} shell CLASSPATH={RemoteServerPath} app_process / com.genymobile.scrcpy.Server {ScrcpyVersion} " +
                        $"scid={scidHex} log_level=warn video=false audio=true control=false cleanup=true " +
                        $"audio_codec=raw audio_source=output raw_stream=true tunnel_forward=true",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start adb shell.");
        // Give the server a moment to bind its socket.
        await Task.Delay(600).ConfigureAwait(false);
        if (process.HasExited)
        {
            string err = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            string outp = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"scrcpy server exited: {err}\n{outp}".Trim());
        }
        return process;
    }

    public Task RemoveForwardAsync(string serial) =>
        RunAdbAsync($"-s {serial} forward --remove tcp:{AudioPort}").ContinueWith(_ => { });

    private async Task<string> RunAdbAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = AdbPath!,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to run adb.");
        string output = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string error = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await p.WaitForExitAsync().ConfigureAwait(false);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"adb {arguments} failed: {error.Trim()}");
        return output;
    }
}

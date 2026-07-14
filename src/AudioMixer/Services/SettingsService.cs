using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioMixer.Services;

public enum SourceKind
{
    InputDevice,     // mic / line-in / CABLE Output etc.
    OutputLoopback,  // "what's playing on <device>"
    Process,         // a single app
    PhoneUsb,
}

public class RouteConfig
{
    public SourceKind Kind { get; set; }
    public string SourceId { get; set; } = "";   // device id, or process name for Kind=Process
    public string SourceName { get; set; } = "";
    public List<string> OutputIds { get; set; } = new();
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }
}

public class MixSourceConfig
{
    public SourceKind Kind { get; set; }
    public string SourceId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public float Gain { get; set; } = 1f;
}

public class VirtualMicConfig
{
    public bool Enabled { get; set; }
    public string? MicDeviceId { get; set; }
    public float MicGain { get; set; } = 1f;
    public List<MixSourceConfig> Sources { get; set; } = new();
}

public class AppSettings
{
    public List<RouteConfig> Routes { get; set; } = new();
    public VirtualMicConfig VirtualMic { get; set; } = new();
    public string AccentColor { get; set; } = "#7C5CFF";
    public int LatencyMs { get; set; } = 50;
    public bool RunAtStartup { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioMixer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
        }
        catch { Current = new AppSettings(); }
    }

    public void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(Current, Options)); }
        catch { /* non-fatal */ }
    }
}

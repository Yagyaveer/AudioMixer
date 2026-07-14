using System.Diagnostics;
using AudioMixer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioMixer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly DeviceService _devices;
    private bool _loading = true;

    public string[] AccentPresets { get; } =
    {
        "#7C5CFF", "#4F9CFF", "#00C2A8", "#5BD75B", "#FFB454", "#FF6B81", "#C678DD",
    };

    public int[] LatencyOptions { get; } = { 20, 50, 100, 150 };

    [ObservableProperty] private string accentColor;
    [ObservableProperty] private int latencyMs;
    [ObservableProperty] private bool runAtStartup;
    [ObservableProperty] private bool startMinimized;
    [ObservableProperty] private bool minimizeToTray;
    [ObservableProperty] private bool cableInstalled;

    public SettingsViewModel(SettingsService settings, DeviceService devices)
    {
        _settings = settings;
        _devices = devices;
        accentColor = settings.Current.AccentColor;
        latencyMs = settings.Current.LatencyMs;
        runAtStartup = settings.Current.RunAtStartup;
        startMinimized = settings.Current.StartMinimized;
        minimizeToTray = settings.Current.MinimizeToTray;
        RefreshCableState();
        _loading = false;
    }

    public void RefreshCableState() => CableInstalled = _devices.FindCableInput() is not null;

    partial void OnAccentColorChanged(string value)
    {
        _settings.Current.AccentColor = value;
        App.ApplyAccent(value);
        SaveIfLoaded();
    }

    partial void OnLatencyMsChanged(int value)
    {
        _settings.Current.LatencyMs = value;
        SaveIfLoaded();
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        _settings.Current.RunAtStartup = value;
        try { StartupService.SetRunAtStartup(value); } catch { }
        SaveIfLoaded();
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        _settings.Current.StartMinimized = value;
        SaveIfLoaded();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settings.Current.MinimizeToTray = value;
        SaveIfLoaded();
    }

    [RelayCommand]
    private void SetAccent(string color) => AccentColor = color;

    [RelayCommand]
    private void InstallCable()
    {
        Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true });
    }

    private void SaveIfLoaded()
    {
        if (!_loading) _settings.Save();
    }
}

using System.Collections.ObjectModel;
using AudioMixer.Audio;
using AudioMixer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioMixer.ViewModels;

public partial class OutputToggleViewModel : ObservableObject
{
    private readonly Action<OutputToggleViewModel> _onToggled;

    public string Id { get; }
    public string Name { get; }

    [ObservableProperty]
    private bool isSelected;

    public OutputToggleViewModel(string id, string name, bool selected, Action<OutputToggleViewModel> onToggled)
    {
        Id = id;
        Name = name;
        isSelected = selected;
        _onToggled = onToggled;
    }

    partial void OnIsSelectedChanged(bool value) => _onToggled(this);
}

public partial class RouteViewModel : ObservableObject, IDisposable
{
    private readonly RoutesViewModel _owner;
    private AudioRoute? _route;
    private bool _restoring;

    public RouteConfig Config { get; }

    [ObservableProperty] private SourceOption? selectedSource;
    [ObservableProperty] private string status = "Choose a source";
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private double volumePercent = 100;
    [ObservableProperty] private bool isMuted;
    [ObservableProperty] private float peak;

    public ObservableCollection<OutputToggleViewModel> Outputs { get; } = new();

    public bool NeedsSourceRetry =>
        _route is null && (Config.Kind == SourceKind.Process || Config.Kind == SourceKind.PhoneUsb) && Config.SourceId.Length > 0;

    public RouteViewModel(RoutesViewModel owner, RouteConfig config)
    {
        _owner = owner;
        Config = config;
        _restoring = true;
        volumePercent = Math.Clamp(config.Volume * 100, 0, 100);
        isMuted = config.Muted;
        RefreshOutputs();
        if (config.SourceId.Length > 0 || config.Kind == SourceKind.PhoneUsb)
        {
            selectedSource = _owner.SourceOptions.FirstOrDefault(o => o.Kind == config.Kind && o.Id == config.SourceId);
            TryBuild();
        }
        _restoring = false;
    }

    partial void OnSelectedSourceChanged(SourceOption? value)
    {
        if (_restoring || value is null) return;
        bool phoneWasActive = Config.Kind == SourceKind.PhoneUsb;
        Config.Kind = value.Kind;
        Config.SourceId = value.Id;
        Config.SourceName = value.Name;
        TryBuild();
        if (phoneWasActive && value.Kind != SourceKind.PhoneUsb) _owner.ReleasePhoneIfUnused();
        _owner.Save();
    }

    partial void OnVolumePercentChanged(double value)
    {
        Config.Volume = (float)(value / 100.0);
        if (_route is not null) _route.Volume = Config.Volume;
        if (!_restoring) _owner.SaveDebounced();
    }

    partial void OnIsMutedChanged(bool value)
    {
        Config.Muted = value;
        if (_route is not null) _route.Muted = value;
        if (!_restoring) _owner.Save();
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveRoute(this);

    /// <summary>(Re)creates the live audio pipeline from Config. Safe to call repeatedly.</summary>
    public void TryBuild()
    {
        TearDown();
        if (Config.SourceId.Length == 0 && Config.Kind != SourceKind.PhoneUsb)
        {
            Status = "Choose a source";
            return;
        }

        var (source, error) = _owner.Factory.Create(Config.Kind, Config.SourceId, Config.SourceName);
        if (source is null)
        {
            Status = error ?? "Source unavailable";
            IsActive = false;
            return;
        }

        var route = new AudioRoute(source, _owner.LatencyMs)
        {
            Volume = Config.Volume,
            Muted = Config.Muted,
        };
        route.SourceStopped += OnSourceStopped;
        _route = route;

        int attached = 0;
        foreach (var id in Config.OutputIds.ToList())
        {
            var device = _owner.Devices.GetDevice(id);
            if (device is not null)
            {
                try { route.AddOutput(device); attached++; }
                catch { }
            }
        }
        IsActive = attached > 0;
        Status = attached > 0 ? "Routing" : "Pick at least one output";
    }

    private void OnSourceStopped(Exception? ex)
    {
        App.RunOnUi(() =>
        {
            if (_route is null) return;
            TearDown();
            Status = Config.Kind switch
            {
                SourceKind.Process => $"Waiting for {Config.SourceName} to play audio…",
                SourceKind.PhoneUsb => "Phone disconnected",
                _ => ex is null ? "Source stopped" : $"Source error: {ex.Message}",
            };
        });
    }

    private void TearDown()
    {
        var route = _route;
        _route = null;
        if (route is not null)
        {
            route.SourceStopped -= OnSourceStopped;
            bool isPhone = Config.Kind == SourceKind.PhoneUsb;
            if (isPhone)
            {
                // The phone source is owned by PhoneUsbService — detach outputs only.
                foreach (var id in route.OutputIds) route.RemoveOutput(id);
            }
            else
            {
                route.Dispose();
            }
        }
        IsActive = false;
        Peak = 0;
    }

    public void OnOutputToggled(OutputToggleViewModel toggle)
    {
        if (toggle.IsSelected)
        {
            if (!Config.OutputIds.Contains(toggle.Id)) Config.OutputIds.Add(toggle.Id);
            var device = _owner.Devices.GetDevice(toggle.Id);
            if (device is not null && _route is not null)
            {
                try { _route.AddOutput(device); } catch { }
            }
        }
        else
        {
            Config.OutputIds.Remove(toggle.Id);
            _route?.RemoveOutput(toggle.Id);
        }
        IsActive = _route is not null && _route.OutputIds.Count > 0;
        if (_route is not null) Status = IsActive ? "Routing" : "Pick at least one output";
        if (!_restoring) _owner.Save();
    }

    public void RefreshOutputs()
    {
        bool wasRestoring = _restoring;
        _restoring = true;
        Outputs.Clear();
        foreach (var d in _owner.Devices.GetOutputs())
            Outputs.Add(new OutputToggleViewModel(d.Id, d.Name, Config.OutputIds.Contains(d.Id), OnOutputToggled));
        _restoring = wasRestoring;

        // Re-attach outputs that came back after a hotplug.
        if (_route is not null)
        {
            foreach (var id in Config.OutputIds)
            {
                if (_route.OutputIds.Contains(id)) continue;
                var device = _owner.Devices.GetDevice(id);
                if (device is not null)
                {
                    try { _route.AddOutput(device); } catch { }
                }
            }
            IsActive = _route.OutputIds.Count > 0;
        }
    }

    /// <summary>Called on the UI timer to refresh the VU meter.</summary>
    public void Tick()
    {
        float current = _route?.Peak ?? 0;
        Peak = current > Peak ? current : Math.Max(0, Peak - 0.06f); // smooth decay
    }

    public void SyncSelectedOption()
    {
        _restoring = true;
        SelectedSource = _owner.SourceOptions.FirstOrDefault(o => o.Kind == Config.Kind && o.Id == Config.SourceId);
        _restoring = false;
    }

    public void Dispose()
    {
        var isPhone = Config.Kind == SourceKind.PhoneUsb;
        TearDown();
        if (isPhone) _owner.ReleasePhoneIfUnused();
    }
}

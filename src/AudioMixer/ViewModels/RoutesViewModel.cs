using System.Collections.ObjectModel;
using AudioMixer.Phone;
using AudioMixer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioMixer.ViewModels;

public partial class RoutesViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly PhoneUsbService _phone;
    private DateTime _lastSave = DateTime.MinValue;

    public DeviceService Devices { get; }
    public SourceFactory Factory { get; }
    public ObservableCollection<RouteViewModel> Routes { get; } = new();
    public ObservableCollection<SourceOption> SourceOptions { get; } = new();

    public int LatencyMs => _settings.Current.LatencyMs;
    public bool HasNoRoutes => Routes.Count == 0;

    public RoutesViewModel(SettingsService settings, DeviceService devices, SourceFactory factory, PhoneUsbService phone)
    {
        _settings = settings;
        Devices = devices;
        Factory = factory;
        _phone = phone;

        RefreshSourceOptions();
        foreach (var config in settings.Current.Routes)
            Routes.Add(new RouteViewModel(this, config));
        Routes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoRoutes));
    }

    [RelayCommand]
    private void AddRoute()
    {
        var config = new RouteConfig();
        _settings.Current.Routes.Add(config);
        Routes.Add(new RouteViewModel(this, config));
        Save();
    }

    public void RemoveRoute(RouteViewModel route)
    {
        Routes.Remove(route);
        _settings.Current.Routes.Remove(route.Config);
        route.Dispose();
        Save();
    }

    /// <summary>Adds (or reuses) a route whose source is the USB phone, playing to the default output.</summary>
    public RouteViewModel EnsurePhoneRoute()
    {
        var existing = Routes.FirstOrDefault(r => r.Config.Kind == SourceKind.PhoneUsb);
        if (existing is not null)
        {
            existing.TryBuild();
            return existing;
        }
        var config = new RouteConfig
        {
            Kind = SourceKind.PhoneUsb,
            SourceId = "phone-usb",
            SourceName = "Phone (USB)",
        };
        try
        {
            using var def = Devices.GetDefaultOutput();
            config.OutputIds.Add(def.ID);
        }
        catch { }
        _settings.Current.Routes.Add(config);
        var vm = new RouteViewModel(this, config);
        Routes.Add(vm);
        vm.SyncSelectedOption();
        Save();
        return vm;
    }

    public void ReleasePhoneIfUnused()
    {
        if (Routes.All(r => r.Config.Kind != SourceKind.PhoneUsb))
            _phone.Disconnect();
    }

    public void RefreshSourceOptions()
    {
        var options = Factory.BuildOptions();
        SourceOptions.Clear();
        foreach (var o in options) SourceOptions.Add(o);
        foreach (var r in Routes) r.SyncSelectedOption();
    }

    /// <summary>Hotplug: refresh output toggles and try to revive routes.</summary>
    public void OnDevicesChanged()
    {
        RefreshSourceOptions();
        foreach (var r in Routes) r.RefreshOutputs();
    }

    /// <summary>Every few seconds: revive routes waiting for an app/phone.</summary>
    public void RetryPending()
    {
        foreach (var r in Routes)
        {
            if (r.NeedsSourceRetry) r.TryBuild();
        }
    }

    public void Tick()
    {
        foreach (var r in Routes) r.Tick();
    }

    public void Save()
    {
        _settings.Save();
        _lastSave = DateTime.UtcNow;
    }

    /// <summary>For high-frequency changes (volume drag): save at most twice a second.</summary>
    public void SaveDebounced()
    {
        if ((DateTime.UtcNow - _lastSave).TotalMilliseconds > 500) Save();
    }

    public void Dispose()
    {
        foreach (var r in Routes) r.Dispose();
        Routes.Clear();
    }
}

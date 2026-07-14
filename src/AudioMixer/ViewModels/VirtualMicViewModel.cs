using System.Collections.ObjectModel;
using AudioMixer.Audio;
using AudioMixer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioMixer.ViewModels;

public partial class MixSourceViewModel : ObservableObject
{
    private readonly VirtualMicViewModel _owner;
    public MixSourceConfig Config { get; }
    public string Key => $"{Config.Kind}:{Config.SourceId}";
    public string Name => Config.SourceName;

    [ObservableProperty] private double gainPercent;

    public MixSourceViewModel(VirtualMicViewModel owner, MixSourceConfig config)
    {
        _owner = owner;
        Config = config;
        gainPercent = Math.Clamp(config.Gain * 100, 0, 150);
    }

    partial void OnGainPercentChanged(double value)
    {
        Config.Gain = (float)(value / 100.0);
        _owner.OnGainChanged(this);
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveSource(this);
}

public partial class VirtualMicViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly DeviceService _devices;
    private readonly SourceFactory _factory;
    private VirtualMicMixer? _mixer;
    private bool _restoring;

    private VirtualMicConfig Config => _settings.Current.VirtualMic;

    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool cableInstalled;
    [ObservableProperty] private DeviceInfo? selectedMic;
    [ObservableProperty] private double micGainPercent = 100;
    [ObservableProperty] private float peak;
    [ObservableProperty] private SourceOption? selectedAddable;

    public ObservableCollection<DeviceInfo> Mics { get; } = new();
    public ObservableCollection<MixSourceViewModel> ExtraSources { get; } = new();
    public ObservableCollection<SourceOption> AddableSources { get; } = new();

    public VirtualMicViewModel(SettingsService settings, DeviceService devices, SourceFactory factory)
    {
        _settings = settings;
        _devices = devices;
        _factory = factory;

        _restoring = true;
        micGainPercent = Math.Clamp(Config.MicGain * 100, 0, 150);
        foreach (var s in Config.Sources)
            ExtraSources.Add(new MixSourceViewModel(this, s));
        RefreshDevices();
        _restoring = false;

        if (Config.Enabled && CableInstalled) IsEnabled = true; // triggers StartMixer
    }

    public void RefreshDevices()
    {
        CableInstalled = _devices.FindCableInput() is not null;

        var mics = _devices.GetInputs()
            .Where(d => !d.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var previous = SelectedMic?.Id ?? Config.MicDeviceId;
        Mics.Clear();
        foreach (var m in mics) Mics.Add(m);
        bool was = _restoring; _restoring = true;
        SelectedMic = Mics.FirstOrDefault(m => m.Id == previous) ?? Mics.FirstOrDefault();
        _restoring = was;
    }

    public void RefreshAddableSources()
    {
        AddableSources.Clear();
        foreach (var o in _factory.BuildOptions())
        {
            if (o.Kind == SourceKind.InputDevice && o.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)) continue;
            if (ExtraSources.Any(s => s.Config.Kind == o.Kind && s.Config.SourceId == o.Id)) continue;
            AddableSources.Add(o);
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        Config.Enabled = value;
        if (value) StartMixer();
        else StopMixer();
        if (!_restoring) _settings.Save();
    }

    partial void OnSelectedMicChanged(DeviceInfo? value)
    {
        Config.MicDeviceId = value?.Id;
        if (_restoring) return;
        _settings.Save();
        if (_mixer is not null) { StopMixer(); StartMixer(); } // swap mic live
    }

    partial void OnMicGainPercentChanged(double value)
    {
        Config.MicGain = (float)(value / 100.0);
        _mixer?.SetGain("mic", Config.MicGain);
        if (!_restoring) _settings.Save();
    }

    [RelayCommand]
    private void AddSource()
    {
        if (SelectedAddable is null) return;
        var config = new MixSourceConfig
        {
            Kind = SelectedAddable.Kind,
            SourceId = SelectedAddable.Id,
            SourceName = SelectedAddable.Name,
            Gain = 1f,
        };
        Config.Sources.Add(config);
        var vm = new MixSourceViewModel(this, config);
        ExtraSources.Add(vm);
        SelectedAddable = null;
        _settings.Save();
        if (_mixer is not null) AttachSource(vm);
    }

    public void RemoveSource(MixSourceViewModel vm)
    {
        ExtraSources.Remove(vm);
        Config.Sources.Remove(vm.Config);
        _mixer?.RemoveInput(vm.Key);
        _settings.Save();
    }

    public void OnGainChanged(MixSourceViewModel vm)
    {
        _mixer?.SetGain(vm.Key, vm.Config.Gain);
        if (!_restoring) _settings.Save();
    }

    private void StartMixer()
    {
        StopMixer();
        var cable = _devices.FindCableInput();
        if (cable is null)
        {
            Status = "VB-Cable is not installed — install it from the Settings page.";
            CableInstalled = false;
            return;
        }
        var cableDevice = _devices.GetDevice(cable.Id);
        if (cableDevice is null)
        {
            Status = "Could not open VB-Cable.";
            return;
        }

        try
        {
            _mixer = new VirtualMicMixer(cableDevice, _settings.Current.LatencyMs);
        }
        catch (Exception ex)
        {
            Status = $"Could not start: {ex.Message}";
            return;
        }

        // Real mic first.
        if (SelectedMic is not null)
        {
            var (mic, err) = _factory.Create(SourceKind.InputDevice, SelectedMic.Id, SelectedMic.Name);
            if (mic is not null) _mixer.AddInput("mic", mic, Config.MicGain);
            else Status = err ?? "";
        }
        foreach (var vm in ExtraSources) AttachSource(vm);

        Status = "Live — pick “CABLE Output (VB-Audio Virtual Cable)” as your mic in Discord/game chat.";
    }

    private void AttachSource(MixSourceViewModel vm)
    {
        if (_mixer is null) return;
        var (source, _) = _factory.Create(vm.Config.Kind, vm.Config.SourceId, vm.Config.SourceName);
        if (source is not null) _mixer.AddInput(vm.Key, source, vm.Config.Gain);
    }

    private void StopMixer()
    {
        _mixer?.Dispose();
        _mixer = null;
        Peak = 0;
        if (!IsEnabled) Status = "";
    }

    public void Tick()
    {
        float current = _mixer?.Peak ?? 0;
        Peak = current > Peak ? current : Math.Max(0, Peak - 0.06f);
    }

    public void Dispose() => StopMixer();
}

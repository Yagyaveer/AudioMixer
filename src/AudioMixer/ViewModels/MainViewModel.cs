using System.Windows.Threading;
using AudioMixer.Phone;
using AudioMixer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioMixer.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _vuTimer;
    private readonly DispatcherTimer _retryTimer;

    public RoutesViewModel RoutesVm { get; }
    public VirtualMicViewModel VirtualMicVm { get; }
    public PhoneViewModel PhoneVm { get; }
    public SettingsViewModel SettingsVm { get; }

    [ObservableProperty] private object currentPage;
    [ObservableProperty] private string currentPageName = "Routes";

    public MainViewModel(
        SettingsService settings,
        DeviceService devices,
        SourceFactory factory,
        PhoneUsbService phoneUsb,
        BluetoothSinkService bluetooth)
    {
        RoutesVm = new RoutesViewModel(settings, devices, factory, phoneUsb);
        VirtualMicVm = new VirtualMicViewModel(settings, devices, factory);
        PhoneVm = new PhoneViewModel(phoneUsb, bluetooth, RoutesVm);
        SettingsVm = new SettingsViewModel(settings, devices);
        currentPage = RoutesVm;

        devices.DevicesChanged += () => App.RunOnUi(() =>
        {
            RoutesVm.OnDevicesChanged();
            VirtualMicVm.RefreshDevices();
            SettingsVm.RefreshCableState();
        });

        _vuTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(66) };
        _vuTimer.Tick += (_, _) => { RoutesVm.Tick(); VirtualMicVm.Tick(); };
        _vuTimer.Start();

        _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _retryTimer.Tick += (_, _) => RoutesVm.RetryPending();
        _retryTimer.Start();
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        CurrentPageName = page;
        CurrentPage = page switch
        {
            "VirtualMic" => VirtualMicVm,
            "Phone" => PhoneVm,
            "Settings" => SettingsVm,
            _ => RoutesVm,
        };
        if (page == "VirtualMic") VirtualMicVm.RefreshAddableSources();
    }

    public void Dispose()
    {
        _vuTimer.Stop();
        _retryTimer.Stop();
        RoutesVm.Dispose();
        VirtualMicVm.Dispose();
        PhoneVm.Dispose();
    }
}

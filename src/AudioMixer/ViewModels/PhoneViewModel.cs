using System.Collections.ObjectModel;
using AudioMixer.Phone;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioMixer.ViewModels;

public partial class BtPhoneViewModel : ObservableObject
{
    private readonly PhoneViewModel _owner;
    public string Id { get; }
    public string Name { get; }

    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isBusy;

    public BtPhoneViewModel(PhoneViewModel owner, string id, string name)
    {
        _owner = owner;
        Id = id;
        Name = name;
    }

    [RelayCommand]
    private async Task Toggle()
    {
        IsBusy = true;
        try
        {
            if (IsConnected) _owner.DisconnectBluetooth(this);
            else await _owner.ConnectBluetoothAsync(this);
        }
        finally { IsBusy = false; }
    }
}

public partial class PhoneViewModel : ObservableObject, IDisposable
{
    private readonly PhoneUsbService _usb;
    private readonly BluetoothSinkService _bluetooth;
    private readonly RoutesViewModel _routes;

    [ObservableProperty] private string usbStatusTitle = "";
    [ObservableProperty] private string usbStatusDetail = "";
    [ObservableProperty] private bool canConnectUsb;
    [ObservableProperty] private bool isUsbStreaming;
    [ObservableProperty] private bool showSetupHelp;
    [ObservableProperty] private string bluetoothStatus = "";

    public ObservableCollection<BtPhoneViewModel> BluetoothPhones { get; } = new();

    public PhoneViewModel(PhoneUsbService usb, BluetoothSinkService bluetooth, RoutesViewModel routes)
    {
        _usb = usb;
        _bluetooth = bluetooth;
        _routes = routes;
        _usb.Changed += () => App.RunOnUi(UpdateUsbState);
        _bluetooth.StateChanged += () => App.RunOnUi(UpdateBtStates);
        UpdateUsbState();
        _ = RefreshBluetoothAsync();
    }

    private void UpdateUsbState()
    {
        switch (_usb.State)
        {
            case PhoneUsbState.ToolsMissing:
                UsbStatusTitle = "Tools missing";
                UsbStatusDetail = "adb/scrcpy were not bundled with this build. Run scripts/fetch-tools.ps1 and rebuild.";
                break;
            case PhoneUsbState.NoDevice:
                UsbStatusTitle = "No phone detected";
                UsbStatusDetail = _usb.LastError is null
                    ? "Plug your phone in with a USB cable. USB debugging must be enabled (one-time setup below)."
                    : $"Last error: {_usb.LastError}";
                break;
            case PhoneUsbState.Unauthorized:
                UsbStatusTitle = $"Almost there — check your phone";
                UsbStatusDetail = "Accept the “Allow USB debugging?” prompt on the phone screen (tick “Always allow”).";
                break;
            case PhoneUsbState.Ready:
                UsbStatusTitle = $"{_usb.Device?.Model} connected";
                UsbStatusDetail = "Ready to stream. Connecting creates a route you can point at any output.";
                break;
            case PhoneUsbState.Streaming:
                UsbStatusTitle = $"{_usb.Device?.Model ?? "Phone"} — streaming";
                UsbStatusDetail = "Phone audio is live. Change where it plays on the Routes page.";
                break;
        }
        CanConnectUsb = _usb.State == PhoneUsbState.Ready;
        IsUsbStreaming = _usb.State == PhoneUsbState.Streaming;
        ShowSetupHelp = _usb.State is PhoneUsbState.NoDevice or PhoneUsbState.Unauthorized;
    }

    [RelayCommand]
    private void ConnectUsb()
    {
        var route = _routes.EnsurePhoneRoute();
        UpdateUsbState();
        if (route.IsActive) UsbStatusDetail = "Streaming to your default output — adjust it on the Routes page.";
    }

    [RelayCommand]
    private void DisconnectUsb()
    {
        _usb.Disconnect();
        UpdateUsbState();
    }

    [RelayCommand]
    private async Task RefreshBluetoothAsync()
    {
        try
        {
            BluetoothStatus = "Scanning paired devices…";
            var phones = await BluetoothSinkService.ListPairedPhonesAsync();
            BluetoothPhones.Clear();
            foreach (var p in phones)
                BluetoothPhones.Add(new BtPhoneViewModel(this, p.Id, p.Name) { IsConnected = _bluetooth.IsConnected(p.Id) });
            BluetoothStatus = phones.Count == 0
                ? "No paired devices found. Pair your phone with this PC in Windows Bluetooth settings first."
                : "Connect, then play audio on the phone. It arrives on your default output — reroute it with a “What's playing on…” route.";
        }
        catch (Exception ex)
        {
            BluetoothStatus = $"Bluetooth unavailable: {ex.Message}";
        }
    }

    public async Task ConnectBluetoothAsync(BtPhoneViewModel phone)
    {
        BluetoothStatus = $"Connecting to {phone.Name}… start playing audio on the phone.";
        string? error = await _bluetooth.ConnectAsync(phone.Id);
        if (error is null)
        {
            phone.IsConnected = true;
            BluetoothStatus = $"{phone.Name} connected as a Bluetooth speaker. Audio plays on your default output.";
        }
        else
        {
            BluetoothStatus = error;
        }
    }

    public void DisconnectBluetooth(BtPhoneViewModel phone)
    {
        _bluetooth.Disconnect(phone.Id);
        phone.IsConnected = false;
        BluetoothStatus = $"{phone.Name} disconnected.";
    }

    private void UpdateBtStates()
    {
        foreach (var p in BluetoothPhones)
            p.IsConnected = _bluetooth.IsConnected(p.Id);
    }

    public void Dispose() { }
}

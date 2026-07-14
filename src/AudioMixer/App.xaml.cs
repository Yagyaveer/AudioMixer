using System.Windows;
using System.Windows.Media;
using AudioMixer.Phone;
using AudioMixer.Services;
using AudioMixer.ViewModels;

namespace AudioMixer;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    private SettingsService _settings = null!;
    private DeviceService _devices = null!;
    private PhoneUsbService _phoneUsb = null!;
    private BluetoothSinkService _bluetooth = null!;
    private MainViewModel _mainVm = null!;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow? _window;

    public static void RunOnUi(Action action)
    {
        var dispatcher = Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public static void ApplyAccent(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            Current.Resources["AccentBrush"] = new SolidColorBrush(color);
            Current.Resources["AccentDimBrush"] = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
        }
        catch { /* bad hex — keep previous accent */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Diagnostic mode: write devices/apps to a file and exit (used for troubleshooting).
        int dumpIdx = Array.IndexOf(e.Args, "--dump-devices");
        if (dumpIdx >= 0)
        {
            string path = dumpIdx + 1 < e.Args.Length ? e.Args[dumpIdx + 1] : "devices.txt";
            using var devices = new DeviceService();
            var lines = new List<string> { "== OUTPUTS ==" };
            lines.AddRange(devices.GetOutputs().Select(d => $"{d.Name}\t{d.Id}"));
            lines.Add("== INPUTS ==");
            lines.AddRange(devices.GetInputs().Select(d => $"{d.Name}\t{d.Id}"));
            lines.Add("== AUDIO APPS ==");
            lines.AddRange(Audio.AudioSessionHelper.GetAudioApps().Select(a => $"{a.DisplayName}\tpid={a.ProcessId}\t{a.ProcessName}"));
            System.IO.File.WriteAllLines(path, lines);
            Shutdown();
            return;
        }

        // Diagnostic mode: capture an app's audio, play it to the default output for 6 s,
        // and report the peak level seen. Proves the whole pipeline end-to-end.
        int stIdx = Array.IndexOf(e.Args, "--selftest");
        if (stIdx >= 0 && stIdx + 2 < e.Args.Length)
        {
            string procName = e.Args[stIdx + 1];
            string outPath = e.Args[stIdx + 2];
            Task.Run(async () =>
            {
                string result;
                try
                {
                    using var devices = new DeviceService();
                    var app = Audio.AudioSessionHelper.FindByProcessName(procName);
                    if (app is null)
                    {
                        var seen = string.Join(", ", Audio.AudioSessionHelper.GetAudioApps().Select(a => a.ProcessName));
                        result = $"FAIL: no audio session for process '{procName}' (apps seen: {seen})";
                    }
                    else
                    {
                        var source = new Audio.ProcessLoopback.ProcessLoopbackSource(app.ProcessId, app.DisplayName);
                        using var route = new Audio.AudioRoute(source, 50);
                        using var def = devices.GetDefaultOutput();
                        route.AddOutput(def);
                        float max = 0, deviceMax = 0;
                        for (int i = 0; i < 60; i++)
                        {
                            await Task.Delay(100);
                            if (route.Peak > max) max = route.Peak;
                            float dm = def.AudioMeterInformation.MasterPeakValue;
                            if (dm > deviceMax) deviceMax = dm;
                        }
                        result = $"OK sourcePeak={max:F4} devicePeak={deviceMax:F4} app={app.DisplayName} pid={app.ProcessId} out={def.FriendlyName}";
                    }
                }
                catch (Exception ex)
                {
                    result = "FAIL: " + ex;
                }
                System.IO.File.WriteAllText(outPath, result);
                Dispatcher.Invoke(Shutdown);
            });
            return;
        }

        _singleInstanceMutex = new Mutex(true, "AudioMixer-SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("AudioMixer is already running (check the system tray).", "AudioMixer");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            MessageBox.Show(args.Exception.Message, "AudioMixer — unexpected error");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogError(args.ExceptionObject as Exception);

        _settings = new SettingsService();
        _devices = new DeviceService();
        var adb = new AdbService();
        _phoneUsb = new PhoneUsbService(adb);
        _bluetooth = new BluetoothSinkService();
        var factory = new SourceFactory(_devices, _phoneUsb);

        ApplyAccent(_settings.Current.AccentColor);
        _mainVm = new MainViewModel(_settings, _devices, factory, _phoneUsb, _bluetooth);

        _window = new MainWindow { DataContext = _mainVm };
        _window.Closing += (_, args) =>
        {
            if (_settings.Current.MinimizeToTray)
            {
                args.Cancel = true;
                _window.Hide();
            }
            else
            {
                ExitApp();
            }
        };

        SetupTray();

        bool startHidden = _settings.Current.StartMinimized || e.Args.Contains("--minimized");
        if (!startHidden) _window.Show();
    }

    private static void LogError(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioMixer");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }

    private void SetupTray()
    {
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        var icon = System.IO.File.Exists(iconPath)
            ? new System.Drawing.Icon(iconPath)
            : System.Drawing.SystemIcons.Application;
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "AudioMixer",
            Visible = true,
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open AudioMixer", null, (_, _) => ShowWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApp()
    {
        if (_trayIcon is not null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
        _settings?.Save();
        _mainVm?.Dispose();
        _bluetooth?.Dispose();
        _phoneUsb?.Dispose();
        _devices?.Dispose();
        Shutdown();
    }
}

using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace AudioMixer.Phone;

public record BluetoothPhone(string Id, string Name);

/// <summary>
/// Lets the PC act as a Bluetooth speaker (A2DP sink) using the
/// AudioPlaybackConnection WinRT API. Phone audio then arrives on the
/// default output device and can be rerouted with a loopback route.
/// </summary>
public sealed class BluetoothSinkService : IDisposable
{
    private readonly Dictionary<string, AudioPlaybackConnection> _connections = new();

    public event Action? StateChanged;

    public static async Task<List<BluetoothPhone>> ListPairedPhonesAsync()
    {
        var result = new List<BluetoothPhone>();
        var devices = await DeviceInformation.FindAllAsync(AudioPlaybackConnection.GetDeviceSelector());
        foreach (var d in devices)
            result.Add(new BluetoothPhone(d.Id, d.Name));
        return result;
    }

    public bool IsConnected(string id) =>
        _connections.TryGetValue(id, out var c) && c.State == AudioPlaybackConnectionState.Opened;

    public async Task<string?> ConnectAsync(string id)
    {
        if (_connections.ContainsKey(id)) return null;
        var connection = AudioPlaybackConnection.TryCreateFromId(id);
        if (connection is null) return "Could not create a connection for this device.";

        connection.StateChanged += (_, _) => StateChanged?.Invoke();
        _connections[id] = connection;
        try
        {
            await connection.StartAsync();
            var result = await connection.OpenAsync();
            if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                Disconnect(id);
                return result.Status switch
                {
                    AudioPlaybackConnectionOpenResultStatus.RequestTimedOut =>
                        "Timed out — start playing audio on the phone and make sure it's connected to this PC over Bluetooth.",
                    AudioPlaybackConnectionOpenResultStatus.DeniedBySystem => "Denied by the system.",
                    _ => "Could not open the connection.",
                };
            }
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            Disconnect(id);
            return ex.Message;
        }
    }

    public void Disconnect(string id)
    {
        if (_connections.Remove(id, out var connection))
        {
            try { connection.Dispose(); } catch { }
            StateChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        foreach (var c in _connections.Values)
        {
            try { c.Dispose(); } catch { }
        }
        _connections.Clear();
    }
}

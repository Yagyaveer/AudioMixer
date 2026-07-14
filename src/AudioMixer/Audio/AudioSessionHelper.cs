using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace AudioMixer.Audio;

public record AudioApp(int ProcessId, string ProcessName, string DisplayName);

/// <summary>Lists processes that currently have an active audio session on any output device.</summary>
public static class AudioSessionHelper
{
    public static List<AudioApp> GetAudioApps()
    {
        var apps = new Dictionary<int, AudioApp>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    int pid = (int)session.GetProcessID;
                    if (pid == 0 || apps.ContainsKey(pid)) continue;
                    string name;
                    try { name = Process.GetProcessById(pid).ProcessName; }
                    catch { continue; }
                    apps[pid] = new AudioApp(pid, name, Prettify(name));
                }
            }
            catch { /* device may vanish mid-enumeration */ }
            finally { device.Dispose(); }
        }
        return apps.Values.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static AudioApp? FindByProcessName(string processName)
    {
        return GetAudioApps().FirstOrDefault(
            a => string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
    }

    private static string Prettify(string processName) =>
        processName.Length <= 1 ? processName : char.ToUpper(processName[0]) + processName[1..];
}

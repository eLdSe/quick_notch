using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace QuickNotchCs;

public sealed class OutputDevice
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

public sealed class AppVolume
{
    public string Name { get; init; } = "";
    public float Level { get; set; }
    public List<int> ProcessIds { get; init; } = new();
}

/// <summary>Громкость, устройства вывода и громкость приложений (порт winapi.py + pycaw).</summary>
public static class AudioService
{
    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["msedge"] = "Edge", ["chrome"] = "Chrome", ["firefox"] = "Firefox", ["opera"] = "Opera",
        ["opera_gx"] = "Opera GX", ["operagx"] = "Opera GX", ["brave"] = "Brave", ["spotify"] = "Spotify",
        ["vlc"] = "VLC", ["yandexmusic"] = "Яндекс Музыка", ["telegram"] = "Telegram",
        ["discord"] = "Discord", ["browser"] = "Яндекс Браузер", ["yandex"] = "Яндекс Браузер",
        ["wmplayer"] = "Медиаплеер", ["zunemusic"] = "Медиаплеер",
    };

    public static string PrettyAppName(string raw)
    {
        string s = (raw ?? "").Trim();
        if (s.Length == 0) return "";
        if (s.Contains("Telegram", StringComparison.OrdinalIgnoreCase)) return "Telegram";
        if (s.Contains("Opera", StringComparison.OrdinalIgnoreCase))
        {
            if (s.Contains("GX", StringComparison.OrdinalIgnoreCase)) return "Opera GX";
            return "Opera";
        }
        if (s.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (s.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (s.Contains("Edge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (s.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (s.Contains("Yandex", StringComparison.OrdinalIgnoreCase)) return "Яндекс Музыка";
        if (s.Contains("Discord", StringComparison.OrdinalIgnoreCase)) return "Discord";
        if (s.Contains('!')) s = s[(s.LastIndexOf('!') + 1)..];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        if (s.Contains('.')) s = s[(s.LastIndexOf('.') + 1)..];
        if (s.Length == 0) return "";
        return KnownApps.TryGetValue(s, out var nice) ? nice : char.ToUpper(s[0]) + s[1..];
    }

    private static MMDeviceEnumerator? _enumerator;
    private static readonly object _enumLock = new();

    private static MMDeviceEnumerator GetEnumerator()
    {
        lock (_enumLock)
        {
            return _enumerator ??= new MMDeviceEnumerator();
        }
    }

    private static MMDevice DefaultDevice(MMDeviceEnumerator en) =>
        en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    /// <summary>(громкость 0..1, выключен ли звук). Бросает исключение, если звуковых устройств нет.</summary>
    public static (float Level, bool Muted) GetVolume()
    {
        try
        {
            var en = GetEnumerator();
            using var dev = DefaultDevice(en);
            var ep = dev.AudioEndpointVolume;
            return (ep.MasterVolumeLevelScalar, ep.Mute);
        }
        catch
        {
            lock (_enumLock)
            {
                _enumerator?.Dispose();
                _enumerator = null;
            }
            throw;
        }
    }

    public static void SetVolume(float level)
    {
        var en = GetEnumerator();
        using var dev = DefaultDevice(en);
        dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
    }

    public static void SetMute(bool muted)
    {
        var en = GetEnumerator();
        using var dev = DefaultDevice(en);
        dev.AudioEndpointVolume.Mute = muted;
    }

    public static (List<OutputDevice> Devices, string? DefaultId) ListOutputDevices()
    {
        using var en = new MMDeviceEnumerator();
        var list = new List<OutputDevice>();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (d)
            {
                list.Add(new OutputDevice { Id = d.ID, Name = d.FriendlyName });
            }
        }

        string? defaultId = null;
        try
        {
            using var def = DefaultDevice(en);
            defaultId = def.ID;
        }
        catch { }
        return (list, defaultId);
    }

    public static void SetDefaultDevice(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        foreach (int role in new[] { 0, 1, 2 })        // консоль, мультимедиа, связь
        {
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
        }
    }

    /// <summary>Приложения со звуком; процессы с одним именем склеены.</summary>
    public static List<AppVolume> ListAppVolumes()
    {
        using var en = new MMDeviceEnumerator();
        using var dev = DefaultDevice(en);
        var mgr = dev.AudioSessionManager;
        mgr.RefreshSessions();
        var sessions = mgr.Sessions;

        var groups = new Dictionary<string, AppVolume>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            try
            {
                if (s.IsSystemSoundsSession || s.State == AudioSessionState.AudioSessionStateExpired) continue;
                int pid = (int)s.GetProcessID;
                if (pid == 0) continue;

                string name = "";
                try { name = Process.GetProcessById(pid).ProcessName; } catch { }
                if (string.IsNullOrEmpty(name)) name = s.DisplayName ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                if (!groups.TryGetValue(name, out var g))
                {
                    g = new AppVolume { Name = PrettyAppName(name) };
                    groups[name] = g;
                }
                g.ProcessIds.Add(pid);
                g.Level = Math.Max(g.Level, s.SimpleAudioVolume.Volume);
            }
            catch { }
        }
        return groups.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void SetAppVolume(IEnumerable<int> processIds, float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        var wanted = new HashSet<int>(processIds);

        using var en = new MMDeviceEnumerator();
        using var dev = DefaultDevice(en);
        var mgr = dev.AudioSessionManager;
        mgr.RefreshSessions();
        var sessions = mgr.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            try
            {
                var s = sessions[i];
                if (wanted.Contains((int)s.GetProcessID)) s.SimpleAudioVolume.Volume = level;
            }
            catch { }
        }
    }

    // ---- IPolicyConfig: недокументированный интерфейс, так делают все утилиты смены звука ----

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name, int def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string name, int def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string name, int fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string name, int fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}


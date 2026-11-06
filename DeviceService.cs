using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Media.Audio;

namespace QuickNotchCs;

public sealed record WifiNetworkInfo(string Ssid, int SignalPercent, bool IsConnected, bool IsSaved, bool IsSecure);

public sealed record BluetoothDeviceInfo(string Id, string Name, bool IsConnected, string IconGlyph);

/// <summary>Яркость экрана через WMI и Wi-Fi/Bluetooth через Windows Radios API.</summary>
public static class DeviceService
{
    public const string BrightnessUnsupported = "Монитор не поддерживает управление яркостью.";

    private static int? _pendingBrightness;
    private static bool _brightnessWorkerRunning;
    private static readonly object _brightnessLock = new();

    public static int GetBrightness()
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightness");
        using var coll = searcher.Get();
        foreach (ManagementBaseObject o in coll)
        {
            using (o)
            {
                return Convert.ToInt32(o["CurrentBrightness"]);
            }
        }
        throw new InvalidOperationException(BrightnessUnsupported);
    }

    public static void SetBrightness(int level)
    {
        level = Math.Clamp(level, 0, 100);
        lock (_brightnessLock)
        {
            _pendingBrightness = level;
            if (_brightnessWorkerRunning) return;
            _brightnessWorkerRunning = true;
        }

        Task.Run(() =>
        {
            while (true)
            {
                int target;
                lock (_brightnessLock)
                {
                    if (_pendingBrightness == null)
                    {
                        _brightnessWorkerRunning = false;
                        break;
                    }
                    target = _pendingBrightness.Value;
                    _pendingBrightness = null;
                }

                try
                {
                    SetBrightnessDirect(target);
                }
                catch { }
            }
        });
    }

    public static void SetBrightnessDirect(int level)
    {
        level = Math.Clamp(level, 0, 100);
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
        using var coll = searcher.Get();
        bool any = false;
        foreach (ManagementObject o in coll)
        {
            using (o)
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level });
                any = true;
            }
        }
        if (!any) throw new InvalidOperationException(BrightnessUnsupported);
    }

    /// <summary>{"WiFi": true/false/null, "Bluetooth": ...}; null — устройства нет.</summary>
    public static async Task<Dictionary<string, bool?>> ReadRadiosAsync()
    {
        var result = new Dictionary<string, bool?> { ["WiFi"] = null, ["Bluetooth"] = null };
        foreach (var radio in await Radio.GetRadiosAsync())
        {
            if (radio.Kind == RadioKind.WiFi) result["WiFi"] = radio.State == RadioState.On;
            else if (radio.Kind == RadioKind.Bluetooth) result["Bluetooth"] = radio.State == RadioState.On;
        }
        return result;
    }

    public static async Task SetRadioAsync(string kind, bool on)
    {
        if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            throw new UnauthorizedAccessException("Windows не разрешила управление радиомодулями.");

        var wanted = kind == "WiFi" ? RadioKind.WiFi : RadioKind.Bluetooth;
        var radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == wanted)
                    ?? throw new InvalidOperationException("Радиомодуль не найден.");
        var res = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
        if (res != RadioAccessStatus.Allowed)
            throw new UnauthorizedAccessException("Windows не разрешила изменить состояние.");
    }

    private static string RunNetsh(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2500);
            return output;
        }
        catch
        {
            return "";
        }
    }

    public static async Task<List<WifiNetworkInfo>> GetWifiNetworksAsync()
    {
        return await Task.Run(async () =>
        {
            var result = new Dictionary<string, WifiNetworkInfo>(StringComparer.OrdinalIgnoreCase);
            var savedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string connectedSsid = "";
            int connectedSignal = 100;

            // 1. Читаем сохранённые профили через netsh
            string profilesOut = RunNetsh("wlan show profiles");
            foreach (Match m in Regex.Matches(profilesOut, @":\s+(.+)$", RegexOptions.Multiline))
            {
                string prof = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(prof) && !prof.Contains("<"))
                {
                    savedProfiles.Add(prof);
                }
            }

            // 2. Определяем текущую подключённую сеть через netsh wlan show interfaces
            string ifaceOut = RunNetsh("wlan show interfaces");
            foreach (string rawLine in ifaceOut.Split('\n'))
            {
                string line = rawLine.Trim();
                if (Regex.IsMatch(line, @"^SSID\s*:", RegexOptions.IgnoreCase))
                {
                    int idx = line.IndexOf(':');
                    if (idx >= 0) connectedSsid = line[(idx + 1)..].Trim();
                }
                else if (line.Contains('%'))
                {
                    var sm = Regex.Match(line, @"(\d+)\s*%");
                    if (sm.Success && int.TryParse(sm.Groups[1].Value, out int sig))
                    {
                        connectedSignal = sig;
                    }
                }
            }

            // 3. Пробуем получить список сетей через WinRT WiFiAdapter
            try
            {
                var adapters = await WiFiAdapter.FindAllAdaptersAsync();
                if (adapters != null && adapters.Count > 0)
                {
                    var adapter = adapters[0];
                    if (string.IsNullOrEmpty(connectedSsid))
                    {
                        try
                        {
                            var prof = await adapter.NetworkAdapter.GetConnectedProfileAsync();
                            if (prof != null && !string.IsNullOrEmpty(prof.ProfileName))
                            {
                                connectedSsid = prof.ProfileName;
                            }
                        }
                        catch { }
                    }

                    var report = adapter.NetworkReport;
                    if (report?.AvailableNetworks != null)
                    {
                        foreach (var net in report.AvailableNetworks)
                        {
                            string ssid = net.Ssid?.Trim() ?? "";
                            if (string.IsNullOrEmpty(ssid)) continue;
                            int sig = Math.Clamp((int)net.SignalBars * 25, 15, 100);
                            bool isConn = string.Equals(ssid, connectedSsid, StringComparison.OrdinalIgnoreCase);
                            bool isSaved = isConn || savedProfiles.Contains(ssid);
                            bool isSec = net.SecuritySettings.NetworkAuthenticationType != Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211;
                            if (!result.TryGetValue(ssid, out var prev) || prev.SignalPercent < sig || isConn)
                            {
                                result[ssid] = new WifiNetworkInfo(ssid, isConn ? Math.Max(sig, connectedSignal) : sig, isConn, isSaved, isSec);
                            }
                        }
                    }
                }
            }
            catch { }

            // 4. Fallback / дополнение через netsh wlan show networks mode=bssid
            if (result.Count == 0)
            {
                string netsOut = RunNetsh("wlan show networks mode=bssid");
                string curSsid = "";
                bool curSec = true;
                int curSig = 75;

                foreach (string rawLine in netsOut.Split('\n'))
                {
                    string line = rawLine.Trim();
                    var ssidMatch = Regex.Match(line, @"^SSID\s+\d+\s*:\s*(.*)$", RegexOptions.IgnoreCase);
                    if (ssidMatch.Success)
                    {
                        if (!string.IsNullOrEmpty(curSsid))
                        {
                            bool isConn = string.Equals(curSsid, connectedSsid, StringComparison.OrdinalIgnoreCase);
                            result[curSsid] = new WifiNetworkInfo(curSsid, curSig, isConn, isConn || savedProfiles.Contains(curSsid), curSec);
                        }
                        curSsid = ssidMatch.Groups[1].Value.Trim();
                        curSig = 75;
                        curSec = true;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(curSsid) && line.Contains('%'))
                    {
                        var sigMatch = Regex.Match(line, @"(\d+)\s*%");
                        if (sigMatch.Success && int.TryParse(sigMatch.Groups[1].Value, out int s))
                        {
                            curSig = Math.Max(curSig == 75 ? 0 : curSig, s);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(curSsid))
                {
                    bool isConn = string.Equals(curSsid, connectedSsid, StringComparison.OrdinalIgnoreCase);
                    result[curSsid] = new WifiNetworkInfo(curSsid, curSig, isConn, isConn || savedProfiles.Contains(curSsid), curSec);
                }
            }

            if (!string.IsNullOrEmpty(connectedSsid) && !result.ContainsKey(connectedSsid))
            {
                result[connectedSsid] = new WifiNetworkInfo(connectedSsid, connectedSignal, true, true, true);
            }

            // Если радио не отдало список эфира, покажем хотя бы сохранённые профили
            if (result.Count <= 1)
            {
                foreach (string prof in savedProfiles.Take(8))
                {
                    if (!result.ContainsKey(prof))
                    {
                        bool isConn = string.Equals(prof, connectedSsid, StringComparison.OrdinalIgnoreCase);
                        result[prof] = new WifiNetworkInfo(prof, isConn ? connectedSignal : 60, isConn, true, true);
                    }
                }
            }

            return result.Values
                .OrderByDescending(x => x.IsConnected)
                .ThenByDescending(x => x.IsSaved)
                .ThenByDescending(x => x.SignalPercent)
                .ToList();
        });
    }

    public static async Task<bool> ConnectWifiAsync(string ssid, string? password = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(password))
                {
                    string safeSsid = SecurityElement.Escape(ssid) ?? ssid;
                    string safePass = SecurityElement.Escape(password) ?? password;
                    string xml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{safeSsid}</name>
    <SSIDConfig>
        <SSID>
            <name>{safeSsid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{safePass}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";
                    string tmpFile = Path.Combine(Path.GetTempPath(), $"qn_wifi_{Guid.NewGuid():N}.xml");
                    try
                    {
                        File.WriteAllText(tmpFile, xml, Encoding.UTF8);
                        RunNetsh($"wlan add profile filename=\"{tmpFile}\"");
                    }
                    finally
                    {
                        try { File.Delete(tmpFile); } catch { }
                    }
                }

                string outStr = RunNetsh($"wlan connect name=\"{ssid}\" ssid=\"{ssid}\"");
                return !string.IsNullOrWhiteSpace(outStr);
            }
            catch
            {
                return false;
            }
        });
    }

    public static async Task<List<BluetoothDeviceInfo>> GetBluetoothDevicesAsync()
    {
        var list = new Dictionary<string, BluetoothDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string[] props = { "System.Devices.Aep.IsConnected", "System.Devices.Aep.Category" };

            // 1. Классические сопряжённые Bluetooth-устройства (наушники, колонки, геймпады)
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector, props);
            foreach (var d in devices)
            {
                string name = d.Name?.Trim() ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                bool isConnected = false;
                if (d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out object? connVal) && connVal is bool b)
                {
                    isConnected = b;
                }
                else
                {
                    try
                    {
                        using var btDev = await BluetoothDevice.FromIdAsync(d.Id);
                        if (btDev != null)
                        {
                            isConnected = btDev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                        }
                    }
                    catch { }
                }

                string glyph = GuessBluetoothIcon(name);
                if (!list.TryGetValue(name, out var prev) || (!prev.IsConnected && isConnected))
                {
                    list[name] = new BluetoothDeviceInfo(d.Id, name, isConnected, glyph);
                }
            }

            // 2. BLE сопряжённые устройства (мыши, клавиатуры, современные наушники)
            string bleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var bleDevices = await DeviceInformation.FindAllAsync(bleSelector, props);
            foreach (var d in bleDevices)
            {
                string name = d.Name?.Trim() ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                bool isConnected = false;
                if (d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out object? connVal) && connVal is bool b)
                {
                    isConnected = b;
                }

                string glyph = GuessBluetoothIcon(name);
                if (!list.TryGetValue(name, out var prev) || (!prev.IsConnected && isConnected))
                {
                    list[name] = new BluetoothDeviceInfo(d.Id, name, isConnected, glyph);
                }
            }
        }
        catch { }

        return list.Values
            .OrderByDescending(x => x.IsConnected)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static string GuessBluetoothIcon(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("airpods") || n.Contains("buds") || n.Contains("headphone") || n.Contains("wh-") || n.Contains("wf-") || n.Contains("наушн") || n.Contains("jbl") || n.Contains("marshall") || n.Contains("sony") || n.Contains("audio"))
            return "\uE7F6"; // Headphones
        if (n.Contains("mouse") || n.Contains("mx master") || n.Contains("мышь"))
            return "\uE962"; // Mouse
        if (n.Contains("key") || n.Contains("клавиатур"))
            return "\uE765"; // Keyboard
        if (n.Contains("xbox") || n.Contains("controller") || n.Contains("dualshock") || n.Contains("dualsense") || n.Contains("gamepad"))
            return "\uE7FC"; // Gamepad
        if (n.Contains("iphone") || n.Contains("galaxy") || n.Contains("phone") || n.Contains("pixel"))
            return "\uE8EA"; // Phone
        return "\uE702"; // Bluetooth
    }

    public static async Task<bool> ConnectBluetoothDeviceAsync(string deviceId, string deviceName)
    {
        return await Task.Run(async () =>
        {
            try
            {
                // 1. Пробуем активировать аудио-подключение через AudioPlaybackConnection (Windows 10 2004+)
                string audioSelector = AudioPlaybackConnection.GetDeviceSelector();
                var audioDevs = await DeviceInformation.FindAllAsync(audioSelector);
                var match = audioDevs.FirstOrDefault(x =>
                    string.Equals(x.Name, deviceName, StringComparison.OrdinalIgnoreCase) ||
                    x.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    using var conn = AudioPlaybackConnection.TryCreateFromId(match.Id);
                    if (conn != null)
                    {
                        await conn.StartAsync();
                        var res = await conn.OpenAsync();
                        if (res.Status == AudioPlaybackConnectionOpenResultStatus.Success)
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                // 2. Пробуем инициировать SDP-запрос к классическому Bluetooth-устройству (пробуждает соединение)
                using var btDev = await BluetoothDevice.FromIdAsync(deviceId);
                if (btDev != null)
                {
                    var rfcomm = await btDev.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
                    _ = rfcomm?.Services?.Count;
                    return btDev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                }
            }
            catch { }

            return false;
        });
    }
}

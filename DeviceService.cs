using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace QuickNotchCs;

/// <summary>Яркость экрана через WMI и Wi-Fi/Bluetooth через Windows Radios API.</summary>
public static class DeviceService
{
    public const string BrightnessUnsupported = "Монитор не поддерживает управление яркостью.";

    public static int GetBrightness()
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightness");
        foreach (ManagementBaseObject o in searcher.Get())
        {
            return Convert.ToInt32(o["CurrentBrightness"]);
        }
        throw new InvalidOperationException(BrightnessUnsupported);
    }

    public static void SetBrightness(int level)
    {
        level = Math.Clamp(level, 0, 100);
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
        bool any = false;
        foreach (ManagementObject o in searcher.Get())
        {
            o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level });
            any = true;
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
}


using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace QuickNotchCs;

public sealed class ProcessItem
{
    public string Name { get; set; } = "";
    public string Memory { get; set; } = "";
    public double MemoryMb { get; set; }
}

public sealed class SystemSnapshot
{
    public string Cpu { get; set; } = "—";
    public double CpuFraction { get; set; }
    public string Memory { get; set; } = "";
    public string MemoryPercent { get; set; } = "";
    public double MemoryFraction { get; set; }
    public string Disk { get; set; } = "";
    public string DiskPercent { get; set; } = "";
    public double DiskFraction { get; set; }
    public string Uptime { get; set; } = "—";
    public string NetworkName { get; set; } = "Сеть";
    public string NetworkSpeed { get; set; } = "—";
    public double NetworkFraction { get; set; }
    public List<ProcessItem> TopProcesses { get; set; } = new();
}

public sealed class HourlyWeather
{
    public string Time { get; set; } = "";
    public double Temp { get; set; }
    public int Code { get; set; }
    public int Pop { get; set; }
}

public sealed class DailyWeather
{
    public string DayName { get; set; } = "";
    public double TempMin { get; set; }
    public double TempMax { get; set; }
    public int Code { get; set; }
    public int Pop { get; set; }
}

public sealed class WeatherData
{
    public string City { get; set; } = "";
    public double Temp { get; set; }
    public double Apparent { get; set; }
    public int Humidity { get; set; }
    public int Code { get; set; }
    public double WindSpeed { get; set; }       // км/ч
    public double Pressure { get; set; }        // гПа
    public int Pop { get; set; }                // % вероятность осадков
    public string Sunrise { get; set; } = "";
    public string Sunset { get; set; } = "";
    public List<HourlyWeather> Hourly { get; set; } = new();
    public List<DailyWeather> Daily { get; set; } = new();
    public string Description => SystemService.WeatherDescription(Code);
}

/// <summary>Система: CPU/RAM/диск, тема, «не засыпать», автозапуск, погода.</summary>
public static class SystemService
{
    private static readonly HttpClient Http = CreateClient();
    private static long _lastIdle, _lastKernel, _lastUser;
    private static bool _hasCpuBaseline;
    private static DateTime _lastProcCheck = DateTime.MinValue;
    private static List<ProcessItem> _cachedProcs = new();
    private static DateTime _lastNetCheck = DateTime.MinValue;
    private static (string Name, string Speed, double Frac) _cachedNet = ("Сеть", "—", 0.0);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("QuickNotch/1.0");
        return c;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);

    // ---------- состояние системы ----------

    public static SystemSnapshot GetSnapshot()
    {
        var snap = new SystemSnapshot();

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            double gb = 1024.0 * 1024 * 1024;
            snap.Memory = $"{(mem.ullTotalPhys - mem.ullAvailPhys) / gb:F1} / {mem.ullTotalPhys / gb:F1} ГБ";
            snap.MemoryPercent = $"{mem.dwMemoryLoad}%";
            snap.MemoryFraction = mem.dwMemoryLoad / 100.0;
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            double gb = 1024.0 * 1024 * 1024;
            double used = drive.TotalSize - drive.AvailableFreeSpace;
            snap.Disk = $"{used / gb:F0} / {drive.TotalSize / gb:F0} ГБ";
            snap.DiskFraction = used / drive.TotalSize;
            snap.DiskPercent = $"{snap.DiskFraction:P0}";
        }
        catch { }

        if (GetSystemTimes(out long idle, out long kernel, out long user))
        {
            if (_hasCpuBaseline)
            {
                long total = (kernel - _lastKernel) + (user - _lastUser);
                if (total > 0)
                {
                    double frac = Math.Clamp((double)(total - (idle - _lastIdle)) / total, 0, 1);
                    snap.CpuFraction = frac;
                    snap.Cpu = $"{Math.Round(frac * 100)}%";
                }
            }
            (_lastIdle, _lastKernel, _lastUser, _hasCpuBaseline) = (idle, kernel, user, true);
        }

        // Время непрерывной работы (Uptime)
        try
        {
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            snap.Uptime = up.Days > 0
                ? $"{up.Days} д {up.Hours} ч {up.Minutes} мин"
                : $"{up.Hours} ч {up.Minutes} мин";
        }
        catch { }

        // Топ-4 процессов по памяти (кэшируем на 6 сек, чтобы не перегружать WMI/Win32)
        DateTime nowUtc = DateTime.UtcNow;
        if ((nowUtc - _lastProcCheck).TotalSeconds >= 6.0 || _cachedProcs.Count == 0)
        {
            _lastProcCheck = nowUtc;
            try
            {
                int myPid = Environment.ProcessId;
                var all = Process.GetProcesses();
                try
                {
                    _cachedProcs = all
                        .Where(p => p.Id != myPid && p.WorkingSet64 > 35 * 1024 * 1024)
                        .OrderByDescending(p => p.WorkingSet64)
                        .Take(4)
                        .Select(p =>
                        {
                            double mb = p.WorkingSet64 / (1024.0 * 1024.0);
                            string memStr = mb >= 1024 ? $"{mb / 1024.0:F1} ГБ" : $"{mb:F0} МБ";
                            return new ProcessItem { Name = p.ProcessName, Memory = memStr, MemoryMb = mb };
                        })
                        .ToList();
                }
                finally
                {
                    foreach (var proc in all) proc.Dispose();
                }
            }
            catch { }
        }
        snap.TopProcesses = _cachedProcs;

        // Сетевой интерфейс и статистика (кэшируем на 4 сек)
        if ((nowUtc - _lastNetCheck).TotalSeconds >= 4.0 || _cachedNet.Speed == "—")
        {
            _lastNetCheck = nowUtc;
            try
            {
                var activeNic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                                         n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);
                if (activeNic != null)
                {
                    string type = activeNic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
                    string name = $"{type} · {activeNic.Name}";
                    var stats = activeNic.GetIPv4Statistics();
                    long totalBytes = stats.BytesReceived + stats.BytesSent;
                    double mb = totalBytes / (1024.0 * 1024.0);
                    string speed = mb >= 1024 ? $"{mb / 1024.0:F1} ГБ трафика" : $"{mb:F0} МБ трафика";
                    _cachedNet = (name, speed, 0.9);
                }
                else
                {
                    _cachedNet = ("Нет подключения", "Отключено", 0.0);
                }
            }
            catch { }
        }
        snap.NetworkName = _cachedNet.Name;
        snap.NetworkSpeed = _cachedNet.Speed;
        snap.NetworkFraction = _cachedNet.Frac;

        return snap;
    }

    // ---------- переключатели ----------

    /// <summary>Вызывать из главного потока: состояние привязано к потоку.</summary>
    public static void SetKeepAwake(bool on)
    {
        const uint continuous = 0x80000000, system = 0x1, display = 0x2;
        SetThreadExecutionState(continuous | (on ? system | display : 0));
    }

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool GetDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Бросает исключение, если реестр недоступен.</summary>
    public static void SetDarkTheme(bool dark)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, true)
                        ?? throw new IOException("Раздел реестра недоступен.");
        int v = dark ? 0 : 1;
        key.SetValue("AppsUseLightTheme", v, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", v, RegistryValueKind.DWord);
    }

    // ---------- автозапуск ----------

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "QuickNotch";

    public static bool AutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) != null;
        }
        catch { return false; }
    }

    /// <summary>Бросает исключение, если реестр недоступен.</summary>
    public static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunValue, false);
    }

    // ---------- погода (Open-Meteo) ----------

    private static bool IsCyrillic(string s) => s.Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё');

    /// <summary>Бросает исключение с понятным текстом, если город не найден или сети нет.</summary>
    public static async Task<WeatherData> FetchWeatherAsync(string city)
    {
        string langCode = Loc.Lang switch
        {
            "tr" => "tr",
            "en" => "en",
            _ => "ru"
        };
        string geo = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language={langCode}";
        var geoRes = await Http.GetFromJsonAsync<JsonObject>(geo);
        var results = geoRes?["results"]?.AsArray();
        if (results == null || results.Count == 0)
        {
            string fallbackGeo = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1";
            geoRes = await Http.GetFromJsonAsync<JsonObject>(fallbackGeo);
            results = geoRes?["results"]?.AsArray();
        }
        if (results == null || results.Count == 0)
            throw new InvalidOperationException(Loc.T($"Город «{city}» не найден.", $"City '{city}' not found.", $"'{city}' şehri bulunamadı."));

        var place = results[0]!;
        string cityName = place["name"]?.ToString() ?? city;
        if (Loc.Lang == "tr" && place["country_code"]?.ToString() == "TR")
        {
            string? adm1 = place["admin1"]?.ToString();
            string? adm2 = place["admin2"]?.ToString();
            if (!string.IsNullOrWhiteSpace(adm1) && !IsCyrillic(adm1)) cityName = adm1;
            else if (!string.IsNullOrWhiteSpace(adm2) && !IsCyrillic(adm2)) cityName = adm2;
        }
        else if (Loc.Lang != "ru" && IsCyrillic(cityName))
        {
            string? adm = place["admin2"]?.ToString();
            if (!string.IsNullOrWhiteSpace(adm) && !IsCyrillic(adm)) cityName = adm;
            else if (!IsCyrillic(city)) cityName = city;
        }

        string url = "https://api.open-meteo.com/v1/forecast"
                     + $"?latitude={Num(place["latitude"])}&longitude={Num(place["longitude"])}"
                     + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,surface_pressure,precipitation"
                     + "&hourly=temperature_2m,weather_code,precipitation_probability"
                     + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset&timezone=auto";
        var forecast = await Http.GetFromJsonAsync<JsonObject>(url);
        var cur = forecast?["current"]?.AsObject() ?? throw new InvalidOperationException(Loc.T("Не удалось получить погоду.", "Failed to get weather data.", "Hava durumu verisi alınamadı."));

        var data = new WeatherData
        {
            City = cityName,
            Temp = cur["temperature_2m"]?.GetValue<double>() ?? 0,
            Apparent = cur["apparent_temperature"]?.GetValue<double>() ?? 0,
            Humidity = (int)Math.Round(cur["relative_humidity_2m"]?.GetValue<double>() ?? 0),
            Code = (int)Math.Round(cur["weather_code"]?.GetValue<double>() ?? -1),
            WindSpeed = cur["wind_speed_10m"]?.GetValue<double>() ?? 0,
            Pressure = cur["surface_pressure"]?.GetValue<double>() ?? 0,
        };

        // 1. Почасовой прогноз (следующие 7 часов)
        var hTimes = forecast?["hourly"]?["time"]?.AsArray();
        var hTemps = forecast?["hourly"]?["temperature_2m"]?.AsArray();
        var hCodes = forecast?["hourly"]?["weather_code"]?.AsArray();
        var hPops = forecast?["hourly"]?["precipitation_probability"]?.AsArray();

        if (hTimes != null && hTemps != null && hCodes != null)
        {
            var now = DateTime.Now;
            int startIdx = 0;
            for (int i = 0; i < hTimes.Count; i++)
            {
                if (DateTime.TryParse(hTimes[i]?.ToString(), out var dt) && dt >= now.AddMinutes(-35))
                {
                    startIdx = i;
                    break;
                }
            }

            for (int k = 0; k < 7 && (startIdx + k) < hTimes.Count; k++)
            {
                int idx = startIdx + k;
                string timeStr = k == 0 ? Loc.T("Сейчас", "Now", "Şimdi") : "";
                if (k > 0 && DateTime.TryParse(hTimes[idx]?.ToString(), out var dt))
                    timeStr = dt.ToString("HH:mm");

                int popVal = 0;
                if (hPops != null && idx < hPops.Count)
                    popVal = (int)Math.Round(hPops[idx]?.GetValue<double>() ?? 0);

                data.Hourly.Add(new HourlyWeather
                {
                    Time = timeStr,
                    Temp = hTemps[idx]?.GetValue<double>() ?? 0,
                    Code = (int)Math.Round(hCodes[idx]?.GetValue<double>() ?? 0),
                    Pop = popVal
                });
            }
        }

        // 2. Прогноз на 6 дней
        var dTimes = forecast?["daily"]?["time"]?.AsArray();
        var dCodes = forecast?["daily"]?["weather_code"]?.AsArray();
        var dMax = forecast?["daily"]?["temperature_2m_max"]?.AsArray();
        var dMin = forecast?["daily"]?["temperature_2m_min"]?.AsArray();
        var dPops = forecast?["daily"]?["precipitation_probability_max"]?.AsArray();
        var dSunrises = forecast?["daily"]?["sunrise"]?.AsArray();
        var dSunsets = forecast?["daily"]?["sunset"]?.AsArray();

        if (dTimes != null && dCodes != null && dMax != null && dMin != null)
        {
            for (int i = 0; i < Math.Min(dTimes.Count, 6); i++)
            {
                string dayName = i == 0 ? Loc.T("Сегодня", "Today", "Bugün") : "";
                if (i > 0 && DateTime.TryParse(dTimes[i]?.ToString(), out var dt))
                {
                    dayName = dt.ToString("ddd", Loc.Culture);
                    dayName = Ui.Capitalize(dayName.TrimEnd('.'));
                }

                int popVal = 0;
                if (dPops != null && i < dPops.Count)
                    popVal = (int)Math.Round(dPops[i]?.GetValue<double>() ?? 0);

                data.Daily.Add(new DailyWeather
                {
                    DayName = dayName,
                    TempMin = dMin[i]?.GetValue<double>() ?? 0,
                    TempMax = dMax[i]?.GetValue<double>() ?? 0,
                    Code = (int)Math.Round(dCodes[i]?.GetValue<double>() ?? 0),
                    Pop = popVal
                });
            }

            if (dPops != null && dPops.Count > 0)
                data.Pop = (int)Math.Round(dPops[0]?.GetValue<double>() ?? 0);

            if (dSunrises != null && dSunrises.Count > 0 && DateTime.TryParse(dSunrises[0]?.ToString(), out var sr))
                data.Sunrise = sr.ToString("HH:mm");
            if (dSunsets != null && dSunsets.Count > 0 && DateTime.TryParse(dSunsets[0]?.ToString(), out var ss))
                data.Sunset = ss.ToString("HH:mm");
        }

        return data;
    }

    private static string Num(JsonNode? n) =>
        (n?.GetValue<double>() ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string WeatherDescription(int code) => code switch
    {
        0 => Loc.T("Ясно", "Clear", "Açık"),
        1 => Loc.T("Преимущественно ясно", "Mainly clear", "Çoğunlukla açık"),
        2 => Loc.T("Переменная облачность", "Partly cloudy", "Parçalı bulutlu"),
        3 => Loc.T("Пасмурно", "Overcast", "Kapalı"),
        45 or 48 => Loc.T("Туман", "Fog", "Sisli"),
        51 or 53 or 55 or 56 or 57 => Loc.T("Морось", "Drizzle", "Çiseleme"),
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => Loc.T("Дождь", "Rain", "Yağmurlu"),
        71 or 73 or 75 or 77 or 85 or 86 => Loc.T("Снег", "Snow", "Karlı"),
        95 or 96 or 99 => Loc.T("Гроза", "Thunderstorm", "Fırtına"),
        _ => Loc.T("Погода", "Weather", "Hava Durumu"),
    };
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickNotchCs;

public sealed class QuickLink
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("target")] public string Target { get; set; } = "";
}

public sealed class CalendarNote
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("date")] public string Date { get; set; } = ""; // "yyyy-MM-dd"
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("done")] public bool Done { get; set; }
}

public sealed class AeroChatSession
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; } = DateTime.Now;
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.Now;
    [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
}

/// <summary>Настройки. Имена полей совпадают с Python-версией: файл state.json общий.</summary>
public sealed class AppSettings
{
    [JsonPropertyName("accent")] public string Accent { get; set; } = "violet";
    [JsonPropertyName("dim_enabled")] public bool DimEnabled { get; set; } = true;
    [JsonPropertyName("dim_level")] public double DimLevel { get; set; } = 0.65;
    [JsonPropertyName("leave_collapse")] public bool LeaveCollapse { get; set; } = true;
    [JsonPropertyName("leave_delay")] public double LeaveDelay { get; set; } = 1.8;
    [JsonPropertyName("hide_when_covered")] public bool HideWhenCovered { get; set; } = true;
    [JsonPropertyName("keep_awake")] public bool KeepAwake { get; set; } = false;
    [JsonPropertyName("weather_city")] public string? WeatherCity { get; set; }
    [JsonPropertyName("clip_pinned")] public List<string> ClipPinned { get; set; } = new();
    [JsonPropertyName("language")] public string Language { get; set; } = "ru";
    [JsonPropertyName("glass_enabled")] public bool GlassEnabled { get; set; } = true;
    [JsonPropertyName("glass_opacity")] public double GlassOpacity { get; set; } = 0.95;
    [JsonPropertyName("notify_enabled")] public bool NotifyEnabled { get; set; } = true;
    [JsonPropertyName("summon_mode")] public string SummonMode { get; set; } = "notch";
    [JsonPropertyName("theme_mode")] public string ThemeMode { get; set; } = "dark";
    [JsonPropertyName("island_style")] public string IslandStyle { get; set; } = "island";
    [JsonPropertyName("robot_companion")] public bool RobotCompanion { get; set; } = true;
    [JsonPropertyName("robot_roaming")] public bool RobotRoaming { get; set; } = false;
    [JsonPropertyName("robot_roam_x")] public double RobotRoamX { get; set; } = -1;
    [JsonPropertyName("robot_roam_y")] public double RobotRoamY { get; set; } = -1;
    [JsonPropertyName("ai_provider")] public string AiProvider { get; set; } = "gemini";
    [JsonPropertyName("ai_key_gemini")] public string AiKeyGemini { get; set; } = "";
    [JsonPropertyName("ai_key_openai")] public string AiKeyOpenAi { get; set; } = "";
    [JsonPropertyName("ai_key_deepseek")] public string AiKeyDeepSeek { get; set; } = "";
    [JsonPropertyName("ai_mode")] public string AiMode { get; set; } = "fast";
    [JsonPropertyName("ai_model")] public string AiModel { get; set; } = "gemini-3.5-flash-lite";

    public string GetEffectiveAiModel()
    {
        bool isPro = AiMode == "pro";
        return AiProvider switch
        {
            "openai" => isPro ? "gpt-4o" : "gpt-4o-mini",
            "deepseek" => isPro ? "deepseek-reasoner" : "deepseek-chat",
            _ => isPro ? "gemini-3.5-flash" : "gemini-3.5-flash-lite"
        };
    }

    public void Sanitize()
    {
        DimLevel = Math.Clamp(double.IsFinite(DimLevel) ? DimLevel : 0.65, 0.2, 0.9);
        LeaveDelay = Math.Clamp(double.IsFinite(LeaveDelay) ? LeaveDelay : 1.8, 0.5, 8.0);
        GlassOpacity = 0.95;
        if (!Theme.IsAccentKey(Accent)) Accent = "violet";
        if (Language != "en" && Language != "ru" && Language != "tr") Language = "ru";
        if (ThemeMode != "dark" && ThemeMode != "light" && ThemeMode != "auto") ThemeMode = "dark";
        if (IslandStyle != "island" && IslandStyle != "notch" && IslandStyle != "compact") IslandStyle = "island";
        SummonMode = "notch";
        if (AiProvider != "gemini" && AiProvider != "openai" && AiProvider != "deepseek")
            AiProvider = "gemini";
        if (AiMode != "fast" && AiMode != "pro")
            AiMode = "fast";
        if (string.IsNullOrWhiteSpace(AiModel) || AiModel == "gemini-1.5-flash" || AiModel == "gemini-2.5-flash" || AiModel == "gemini-3.8-flash")
            AiModel = GetEffectiveAiModel();
        ClipPinned ??= new();
        ClipPinned.RemoveAll(t => string.IsNullOrWhiteSpace(t));
    }
}

/// <summary>Хранение в %APPDATA%\QuickNotch. Запись атомарная (временный файл и замена).</summary>
public static class StoreService
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickNotch");
    private static readonly string StatePath = Path.Combine(BaseDir, "state.json");
    private static readonly string LinksPath = Path.Combine(BaseDir, "links.json");
    private static readonly string CalendarPath = Path.Combine(BaseDir, "calendar.json");
    private static readonly string AeroChatsPath = Path.Combine(BaseDir, "aero_chats.json");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings LoadSettings()
    {
        AppSettings s;
        try
        {
            s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StatePath)) ?? new AppSettings();
        }
        catch
        {
            s = new AppSettings();
        }
        s.Sanitize();
        return s;
    }

    /// <summary>Бросает IOException, если записать не удалось.</summary>
    public static void SaveSettings(AppSettings s) => WriteAtomic(StatePath, JsonSerializer.Serialize(s, Options));

    /// <summary>Возвращает ссылки; при повреждённом файле — Error, а список пустой.</summary>
    public static (List<QuickLink> Links, string? Error) LoadLinks()
    {
        if (!File.Exists(LinksPath))
        {
            return (new List<QuickLink>
            {
                new() { Label = "YouTube", Target = "https://www.youtube.com" },
                new() { Label = "Google", Target = "https://www.google.com" },
                new() { Label = "Домашняя папка", Target = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
            }, null);
        }

        try
        {
            var raw = JsonSerializer.Deserialize<List<QuickLink>>(File.ReadAllText(LinksPath))
                      ?? throw new JsonException("Файл быстрых ссылок должен содержать список.");
            raw.RemoveAll(l => string.IsNullOrEmpty(l.Label) || l.Target == null);
            return (raw, null);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return (new List<QuickLink>(), $"Не удалось загрузить быстрые ссылки: {e.Message}");
        }
    }

    public static void SaveLinks(List<QuickLink> links) => WriteAtomic(LinksPath, JsonSerializer.Serialize(links, Options));

    public static List<CalendarNote> LoadCalendarNotes()
    {
        if (!File.Exists(CalendarPath)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<CalendarNote>>(File.ReadAllText(CalendarPath)) ?? new();
        }
        catch { return new(); }
    }

    public static void SaveCalendarNotes(List<CalendarNote> notes) =>
        WriteAtomic(CalendarPath, JsonSerializer.Serialize(notes, Options));

    public static List<AeroChatSession> LoadAeroChats()
    {
        if (!File.Exists(AeroChatsPath)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<AeroChatSession>>(File.ReadAllText(AeroChatsPath)) ?? new();
        }
        catch { return new(); }
    }

    public static void SaveAeroChats(List<AeroChatSession> sessions) =>
        WriteAtomic(AeroChatsPath, JsonSerializer.Serialize(sessions, Options));

    private static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(BaseDir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, true);
    }
}

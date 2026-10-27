using System.Globalization;

namespace QuickNotchCs;

/// <summary>Локализация интерфейса: Русский (ru), English (en), Türkçe (tr).</summary>
public static class Loc
{
    public static string Lang { get; set; } = "ru";

    public static bool IsEn => Lang == "en";
    public static bool IsTr => Lang == "tr";
    public static bool IsRu => Lang == "ru";

    public static string T(string ru, string en, string tr = "") => Lang switch
    {
        "ru" => ru,
        "tr" => !string.IsNullOrEmpty(tr) ? tr : en,
        _ => en
    };

    public static CultureInfo Culture => Lang switch
    {
        "tr" => CultureInfo.GetCultureInfo("tr-TR"),
        "ru" => CultureInfo.GetCultureInfo("ru-RU"),
        _ => CultureInfo.GetCultureInfo("en-US")
    };
}


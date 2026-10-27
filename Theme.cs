using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace QuickNotchCs;

/// <summary>Цвета и акценты. Поддержка матового стекла Liquid Glass, бликов и настраиваемой прозрачности.</summary>
public static class Theme
{
    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static bool GlassEnabled { get; set; } = true;
    public const double GlassOpacity = 0.95;

    public static string ThemeMode { get; private set; } = "dark"; // "dark", "light", "auto"
    public static bool IsLight { get; private set; } = false;

    public static Color Bg => IsLight
        ? (GlassEnabled ? Color.FromArgb(244, 246, 248, 252) : C("#F3F4F6"))
        : (GlassEnabled ? Color.FromArgb(242, 12, 13, 18) : C("#0A0A0E"));

    public static Color Surface => IsLight
        ? (GlassEnabled ? Color.FromArgb(215, 255, 255, 255) : C("#FFFFFF"))
        : (GlassEnabled ? Color.FromArgb(195, 20, 22, 29) : C("#121217"));

    public static Color Surface2 => IsLight
        ? (GlassEnabled ? Color.FromArgb(235, 241, 245, 249) : C("#EAECEF"))
        : (GlassEnabled ? Color.FromArgb(220, 28, 31, 40) : C("#1A1A22"));

    public static Color Surface3 => IsLight
        ? (GlassEnabled ? Color.FromArgb(245, 226, 232, 240) : C("#DFE3E8"))
        : (GlassEnabled ? Color.FromArgb(235, 38, 42, 54) : C("#252532"));

    public static Color Border => IsLight
        ? (GlassEnabled ? Color.FromArgb(50, 0, 0, 0) : C("#D1D5DB"))
        : (GlassEnabled ? Color.FromArgb(95, 255, 255, 255) : C("#262633"));

    public static Color BorderSubtle => IsLight
        ? (GlassEnabled ? Color.FromArgb(22, 0, 0, 0) : C("#E5E7EB"))
        : (GlassEnabled ? Color.FromArgb(48, 255, 255, 255) : C("#1A1A24"));

    public static Color Text => IsLight ? C("#0F172A") : C("#FFFFFF");

    public static Color Muted => IsLight
        ? C("#64748B")
        : (GlassEnabled ? C("#A8A8B8") : C("#9494A0"));

    public static Color Disabled => IsLight ? C("#94A3B8") : C("#6C6C78");

    public static readonly Color Green = C("#30D158");
    public static readonly Color Yellow = C("#FFD60A");
    public static readonly Color Orange = C("#FF9F0A");
    public static readonly Color Red = C("#FF453A");

    public static readonly (string Key, string Main, string Hot)[] Accents =
    {
        // Холодные и спокойные тона
        ("violet",   "#8b7bff", "#a79bff"),
        ("indigo",   "#5856d6", "#7a79e0"),
        ("blue",     "#0a84ff", "#409cff"),
        ("cyan",     "#32ade6", "#64c0ee"),
        ("mint",     "#00c7be", "#30d9d2"),
        ("green",    "#30d158", "#5de07f"),
        ("graphite", "#636366", "#8e8e93"),

        // Тёплые, яркие и неоновые тона
        ("lime",     "#a4e035", "#bcf054"),
        ("amber",    "#ffd60a", "#ffe047"),
        ("orange",   "#ff9f0a", "#ffb340"),
        ("coral",    "#ff6b6b", "#ff8a8a"),
        ("red",      "#ff453a", "#ff6961"),
        ("pink",     "#ff375f", "#ff6482"),
        ("purple",   "#af52de", "#c775ea"),
    };

    public static string CurrentAccent { get; private set; } = "violet";

    public static readonly SolidColorBrush BgBrush = new(Bg);
    public static readonly SolidColorBrush SurfaceBrush = new(Surface);
    public static readonly SolidColorBrush Surface2Brush = new(Surface2);
    public static readonly SolidColorBrush Surface3Brush = new(Surface3);
    public static readonly SolidColorBrush TextBrush = new(Text);
    public static readonly SolidColorBrush MutedBrush = new(Muted);
    public static readonly SolidColorBrush GreenBrush = new(Green);
    public static readonly SolidColorBrush YellowBrush = new(Yellow);
    public static readonly SolidColorBrush RedBrush = new(Red);
    public static readonly SolidColorBrush DisabledBrush = new(Disabled);
    public static readonly SolidColorBrush AccentBrush = new(C("#8b7bff"));
    public static readonly SolidColorBrush AccentHotBrush = new(C("#a79bff"));

    public static Color Accent => AccentBrush.Color;
    public static Color AccentHot => AccentHotBrush.Color;

    public static bool IsAccentKey(string? key) => Accents.Any(a => a.Key == key);

    public static void SetAccent(string key)
    {
        foreach (var (k, main, hot) in Accents)
        {
            if (k != key) continue;
            AccentBrush.Color = C(main);
            AccentHotBrush.Color = C(hot);
            CurrentAccent = k;
            return;
        }
    }

    private static Brush? _cachedGlassBgBrush;
    private static Pen? _cachedGlassBorderPen;
    private static Brush? _cachedSheenBrush;

    public static bool SetThemeMode(string mode, bool systemIsDark)
    {
        ThemeMode = mode is "light" or "auto" ? mode : "dark";
        bool wantLight = ThemeMode == "light" || (ThemeMode == "auto" && !systemIsDark);
        bool changed = IsLight != wantLight;
        IsLight = wantLight;
        InvalidateTheme();
        return changed;
    }

    public static bool UpdateSystemTheme(bool systemIsDark)
    {
        if (ThemeMode != "auto") return false;
        bool wantLight = !systemIsDark;
        if (IsLight != wantLight)
        {
            IsLight = wantLight;
            InvalidateTheme();
            return true;
        }
        return false;
    }

    public static void InvalidateTheme()
    {
        _cachedGlassBgBrush = null;
        _cachedGlassBorderPen = null;
        _cachedSheenBrush = null;
        BgBrush.Color = Bg;
        SurfaceBrush.Color = Surface;
        Surface2Brush.Color = Surface2;
        Surface3Brush.Color = Surface3;
        TextBrush.Color = Text;
        MutedBrush.Color = Muted;
        DisabledBrush.Color = Disabled;
    }

    public static void SetGlass(bool enabled, double opacity = GlassOpacity)
    {
        GlassEnabled = enabled;
        InvalidateTheme();
    }

    public static Brush GetGlassGradientBrush(double opacity = GlassOpacity)
    {
        return _cachedGlassBgBrush ??= CreateGlassGradientBrush();
    }

    public static Pen GetGlassBorderPen(double width = 1.0) => _cachedGlassBorderPen ??= CreateGlassBorderPen(width);

    public static Brush GetGlassSheenBrush()
    {
        if (_cachedSheenBrush != null) return _cachedSheenBrush;
        var b = new LinearGradientBrush(
            IsLight ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(20, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            new Point(0.5, 0),
            new Point(0.5, 1));
        b.Freeze();
        _cachedSheenBrush = b;
        return b;
    }

    public static Brush CreateGlassGradientBrush()
    {
        if (IsLight)
        {
            byte aTop = 248;
            byte aBottom = 242;
            var brush = new LinearGradientBrush(
                Color.FromArgb(aTop, 255, 255, 255),
                Color.FromArgb(aBottom, 243, 245, 249),
                new Point(0.5, 0),
                new Point(0.5, 1));
            brush.Freeze();
            return brush;
        }
        else
        {
            byte aTop = 246;
            byte aBottom = 240;
            var brush = new LinearGradientBrush(
                Color.FromArgb(aTop, 14, 15, 20),
                Color.FromArgb(aBottom, 9, 10, 14),
                new Point(0.5, 0),
                new Point(0.5, 1));
            brush.Freeze();
            return brush;
        }
    }

    public static Pen CreateGlassBorderPen(double width = 1.0)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        if (IsLight)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(35, 0, 0, 0), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(55, 0, 0, 0), 1.0));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(135, 255, 255, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(55, 255, 255, 255), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(30, 255, 255, 255), 1.0));
        }
        brush.Freeze();
        var pen = new Pen(brush, width);
        pen.Freeze();
        return pen;
    }

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}

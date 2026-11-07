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
        ? (GlassEnabled ? Color.FromArgb(235, 246, 248, 252) : Color.FromRgb(242, 244, 247))
        : (GlassEnabled ? Color.FromArgb(240, 12, 14, 18) : Color.FromRgb(10, 10, 14));

    public static Color Surface => IsLight
        ? (GlassEnabled ? Color.FromArgb(230, 255, 255, 255) : Color.FromRgb(255, 255, 255))
        : (GlassEnabled ? Color.FromArgb(215, 22, 24, 32) : Color.FromRgb(28, 28, 30));

    public static Color Surface2 => IsLight
        ? (GlassEnabled ? Color.FromArgb(235, 241, 245, 249) : Color.FromRgb(238, 241, 246))
        : (GlassEnabled ? Color.FromArgb(225, 30, 34, 46) : Color.FromRgb(38, 38, 41));

    public static Color Surface3 => IsLight
        ? (GlassEnabled ? Color.FromArgb(240, 228, 234, 242) : Color.FromRgb(226, 230, 236))
        : (GlassEnabled ? Color.FromArgb(235, 38, 42, 56) : Color.FromRgb(50, 50, 54));

    public static Color Border => IsLight
        ? Color.FromArgb(90, 0, 0, 0)
        : (GlassEnabled ? Color.FromArgb(50, 255, 255, 255) : Color.FromRgb(58, 58, 60));

    public static Color BorderSubtle => IsLight
        ? Color.FromArgb(35, 0, 0, 0)
        : (GlassEnabled ? Color.FromArgb(28, 255, 255, 255) : Color.FromRgb(40, 40, 43));

    public static Color Text => IsLight ? Color.FromRgb(15, 23, 42) : Color.FromRgb(255, 255, 255);

    public static Color Muted => IsLight ? Color.FromRgb(100, 116, 139) : (GlassEnabled ? Color.FromRgb(160, 160, 172) : Color.FromRgb(142, 142, 147));

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
            var mainCol = C(main);
            var hotCol = C(hot);
            try
            {
                if (!AccentBrush.IsFrozen) AccentBrush.Color = mainCol;
                if (!AccentHotBrush.IsFrozen) AccentHotBrush.Color = hotCol;
            }
            catch { }
            CurrentAccent = k;

            if (Application.Current != null)
            {
                try
                {
                    Application.Current.Resources["AccentBrush"] = new SolidColorBrush(mainCol);
                    Application.Current.Resources["AccentHotBrush"] = new SolidColorBrush(hotCol);
                }
                catch { }
            }
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

    public static void ApplyToAppResources()
    {
        if (Application.Current == null) return;
        var res = Application.Current.Resources;
        if (res == null) return;

        void UpdateBrush(string key, Color color)
        {
            if (res.Contains(key) && res[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
            }
            else
            {
                res[key] = new SolidColorBrush(color);
            }
        }

        UpdateBrush("SurfaceBrush", Surface);
        UpdateBrush("Surface2Brush", Surface2);
        UpdateBrush("Surface3Brush", Surface3);
        UpdateBrush("SurfaceHoverBrush", IsLight ? Color.FromRgb(226, 232, 240) : (GlassEnabled ? Color.FromArgb(210, 44, 46, 58) : Color.FromRgb(44, 44, 46)));
        UpdateBrush("BorderBrush", Border);
        UpdateBrush("BorderSubtleBrush", BorderSubtle);
        UpdateBrush("TextPrimaryBrush", Text);
        UpdateBrush("TextSecondaryBrush", Muted);
        UpdateBrush("AccentBrush", Accent);
        UpdateBrush("AccentHotBrush", AccentHot);
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
        ApplyToAppResources();
    }

    public static void SetGlass(bool enabled, double opacity = GlassOpacity)
    {
        GlassEnabled = enabled;
        _cachedGlassBgBrush = null;
        _cachedGlassBorderPen = null;
        _cachedSheenBrush = null;
        InvalidateTheme();
    }

    public static Brush GetGlassGradientBrush(double opacity = GlassOpacity)
    {
        return _cachedGlassBgBrush ??= CreateGlassGradientBrush(opacity);
    }

    public static Pen GetGlassBorderPen(double width = 1.0) => _cachedGlassBorderPen ??= CreateGlassBorderPen(width);

    public static Brush GetGlassSheenBrush()
    {
        if (_cachedSheenBrush != null) return _cachedSheenBrush;
        var b = new LinearGradientBrush(
            IsLight ? Color.FromArgb(45, 255, 255, 255) : Color.FromArgb(24, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            new Point(0.5, 0),
            new Point(0.5, 1));
        b.Freeze();
        _cachedSheenBrush = b;
        return b;
    }

    public static Brush CreateGlassGradientBrush(double opacity = GlassOpacity)
    {
        double factor = Math.Clamp(opacity, 0.4, 1.0);
        if (IsLight)
        {
            byte a1 = (byte)Math.Round(235 * factor);
            byte a2 = (byte)Math.Round(248 * factor);
            var brush = new LinearGradientBrush(
                Color.FromArgb(a1, 255, 255, 255),
                Color.FromArgb(a2, 240, 244, 250),
                new Point(0.5, 0),
                new Point(0.5, 1));
            brush.Freeze();
            return brush;
        }
        else
        {
            byte a1 = (byte)Math.Round(238 * factor);
            byte a2 = (byte)Math.Round(250 * factor);
            var brush = new LinearGradientBrush(
                Color.FromArgb(a1, 16, 17, 24),
                Color.FromArgb(a2, 10, 11, 15),
                new Point(0.5, 0),
                new Point(0.5, 1));
            brush.Freeze();
            return brush;
        }
    }

    public static Brush CreateGlassBorderBrush(double width = 1.0)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        if (IsLight)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(180, 255, 255, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(50, 0, 0, 0), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(80, 0, 0, 0), 1.0));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(20, 255, 255, 255), 1.0));
        }
        brush.Freeze();
        return brush;
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

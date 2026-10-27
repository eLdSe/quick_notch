using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace QuickNotchCs;

public enum HAlign { Left, Center, Right }
public enum VAlign { Top, Middle }

public static class Ui
{
    public static readonly string[] Weekdays =
        { "понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье" };
    public static readonly string[] Months =
        { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };

    public static double Clamp(double x, double lo = 0.0, double hi = 1.0) => Math.Max(lo, Math.Min(hi, x));

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static string Trim(string text, int limit)
    {
        text = text.Replace("\n", " ").Trim();
        return text.Length <= limit ? text : text[..(limit - 1)] + "…";
    }

    public static string FmtTime(double seconds)
    {
        int s = Math.Max(0, (int)seconds);
        int h = s / 3600, m = s % 3600 / 60, sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    public static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}

/// <summary>Отметка «закончить блок» без аллокаций в куче (readonly struct на стеке, аналог with в Python).</summary>
public readonly struct Scope : IDisposable
{
    public static readonly Scope Empty = default;

    private enum ScopeKind : byte { None, Pop, Opacity, PanelWithPop, PanelNoPop }
    private readonly ScopeKind _kind;
    private readonly Painter? _painter;
    private readonly double _prevAlpha;
    private readonly string? _hitKey;
    private readonly Rect _hitRect;

    public Scope(Painter painter)
    {
        _kind = ScopeKind.Pop;
        _painter = painter;
        _prevAlpha = 0;
        _hitKey = null;
        _hitRect = default;
    }

    public Scope(Painter painter, double prevAlpha)
    {
        _kind = ScopeKind.Opacity;
        _painter = painter;
        _prevAlpha = prevAlpha;
        _hitKey = null;
        _hitRect = default;
    }

    public Scope(Painter painter, string hitKey, Rect hitRect, bool popped)
    {
        _kind = popped ? ScopeKind.PanelWithPop : ScopeKind.PanelNoPop;
        _painter = painter;
        _prevAlpha = 0;
        _hitKey = hitKey;
        _hitRect = hitRect;
    }

    public void Dispose()
    {
        switch (_kind)
        {
            case ScopeKind.Pop:
                _painter?.Dc.Pop();
                break;
            case ScopeKind.Opacity:
                if (_painter != null)
                {
                    _painter.Dc.Pop();
                    _painter.Alpha = _prevAlpha;
                }
                break;
            case ScopeKind.PanelWithPop:
                if (_painter != null)
                {
                    _painter.Dc.Pop();
                    if (_hitKey != null)
                        _painter.Isl.AddHit(_hitKey, _hitRect);
                }
                break;
            case ScopeKind.PanelNoPop:
                if (_painter != null && _hitKey != null)
                {
                    _painter.Isl.AddHit(_hitKey, _hitRect);
                }
                break;
        }
    }
}

/// <summary>Порт ui.py: примитивы интерфейса поверх DrawingContext.</summary>
public sealed class Painter
{
    public DrawingContext Dc = null!;
    public readonly IslandView Isl;
    public double Alpha = 1.0;
    public double Ppd = 1.0;

    private static readonly FontFamily Family = new("Segoe UI Variable Display, Segoe UI");
    private static readonly Typeface Regular = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldFace = new(Family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly FontFamily MonoFamily = new("Cascadia Code, Consolas, Courier New");
    private static readonly Typeface MonoFace = new(MonoFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly Dictionary<Color, SolidColorBrush> _brushes = new();
    private readonly Dictionary<(Color, double), Pen> _pens = new();
    private Dictionary<(string, double, bool, int, HAlign, bool), FormattedText> _texts = new();
    private Dictionary<(string, double, bool, int, HAlign, bool), FormattedText> _oldTexts = new();
    private Dictionary<(string, double, int), FormattedText> _monoTexts = new();
    private Dictionary<(string, double, int), FormattedText> _oldMonoTexts = new();

    public Painter(IslandView isl) => Isl = isl;

    public Brush B(Color c)
    {
        if (!_brushes.TryGetValue(c, out var b))
        {
            b = new SolidColorBrush(c);
            b.Freeze();
            _brushes[c] = b;
        }
        return b;
    }

    public Pen P(Color c, double w)
    {
        w = Math.Round(w, 2);
        if (!_pens.TryGetValue((c, w), out var pen))
        {
            pen = new Pen(B(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            _pens[(c, w)] = pen;
        }
        return pen;
    }

    // ---------- прозрачность ----------

    public Scope WithOpacity(double a)
    {
        if (a >= 0.999) return Scope.Empty;
        double clamped = Math.Clamp(a, 0.0, 1.0);
        double old = Alpha;
        Dc.PushOpacity(clamped);
        Alpha = old * clamped;
        return new Scope(this, old);
    }

    public Scope WithClip(Geometry g)
    {
        Dc.PushClip(g);
        return new Scope(this);
    }

    public Scope WithClip(Rect r)
    {
        var g = new RectangleGeometry(r);
        g.Freeze();
        return WithClip(g);
    }

    public Scope WithTranslate(double dx, double dy)
    {
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) return Scope.Empty;
        var tt = new TranslateTransform(dx, dy);
        tt.Freeze();
        Dc.PushTransform(tt);
        return new Scope(this);
    }

    public Scope WithScale(double s, Point center)
    {
        if (Math.Abs(s - 1.0) < 0.001) return Scope.Empty;
        var st = new ScaleTransform(s, s, center.X, center.Y);
        st.Freeze();
        Dc.PushTransform(st);
        return new Scope(this);
    }

    public Rect PressRect(IslandView isl, string key, Rect r, double maxShrink = 2.0)
    {
        double pv = isl.Pv(key);
        if (pv <= 0.001) return r;
        double shrink = maxShrink * pv;
        return new Rect(r.Left + shrink, r.Top + shrink, Math.Max(2, r.Width - shrink * 2), Math.Max(2, r.Height - shrink * 2));
    }

    // ---------- базовые фигуры ----------

    public void Card(Rect r, Color fill, double radius = 22, Color? border = null)
    {
        var pen = border.HasValue
            ? P(border.Value, 1)
            : (Theme.GlassEnabled && radius > 6 ? P(Theme.BorderSubtle, 1) : null);
        Dc.DrawRoundedRectangle(B(fill), pen, r, radius, radius);
    }

    public void Circle(Point c, double r, Color fill) => Dc.DrawEllipse(B(fill), null, c, r, r);

    public void Line(Point a, Point b, Color color, double w) => Dc.DrawLine(P(color, w), a, b);

    public void Polygon(Color fill, params Point[] pts)
    {
        if (pts.Length == 0) return;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            for (int i = 1; i < pts.Length; i++)
                ctx.LineTo(pts[i], true, false);
        }
        g.Freeze();
        Dc.DrawGeometry(B(fill), null, g);
    }

    public void Polyline(Color color, double w, params Point[] pts)
    {
        if (pts.Length == 0) return;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Length; i++)
                ctx.LineTo(pts[i], true, false);
        }
        g.Freeze();
        Dc.DrawGeometry(null, P(color, w), g);
    }

    // ---------- текст ----------

    private FormattedText Make(string text, double size, bool bold, Color color, double width, HAlign h, bool wrap)
    {
        int w = (int)Math.Round(Math.Max(1, width));
        var key = (text, size, bold, w, h, wrap);
        if (_texts.TryGetValue(key, out var cached))
        {
            cached.SetForegroundBrush(B(color));
            return cached;
        }
        if (_oldTexts.TryGetValue(key, out cached))
        {
            _texts[key] = cached;
            cached.SetForegroundBrush(B(color));
            return cached;
        }

        if (_texts.Count >= 768)
        {
            _oldTexts = _texts;
            _texts = new Dictionary<(string, double, bool, int, HAlign, bool), FormattedText>(256);
        }

        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            bold ? BoldFace : Regular, size, B(color), Ppd)
        {
            MaxTextWidth = w,
            TextAlignment = h switch { HAlign.Center => TextAlignment.Center, HAlign.Right => TextAlignment.Right, _ => TextAlignment.Left },
        };
        if (!wrap)
        {
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }
        _texts[key] = ft;
        return ft;
    }

    public void Label(string text, Rect r, double size = 12, Color? color = null, bool bold = false,
                      HAlign h = HAlign.Left, VAlign v = VAlign.Middle, bool wrap = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = Make(text, size, bold, color ?? Theme.Text, r.Width, h, wrap);
        double y = v == VAlign.Middle ? r.Y + (r.Height - ft.Height) / 2 : r.Y;
        Dc.DrawText(ft, new Point(r.X, y));
    }

    /// <summary>Текст по базовой линии (для крупных часов).</summary>
    public void TextAtBaseline(string text, double x, double baselineY, double size, bool bold, Color color)
    {
        var ft = Make(text, size, bold, color, 600, HAlign.Left, false);
        Dc.DrawText(ft, new Point(x, baselineY - ft.Baseline));
    }

    public void Note(Rect r, string text, Color? color = null, double size = 10) =>
        Label(text, r, size, color ?? Theme.Muted, false, HAlign.Left, VAlign.Top, true);

    public double TextWidth(string text, double size, bool bold) =>
        Make(text, size, bold, Theme.Text, 2000, HAlign.Left, false).WidthIncludingTrailingWhitespace;

    public double WrappedHeight(string text, double size, double width) =>
        Make(text, size, false, Theme.Text, width, HAlign.Left, true).Height;

    private FormattedText MakeMono(string text, double size, Color color, double width)
    {
        int w = (int)Math.Round(Math.Max(1, width));
        var key = (text, size, w);
        if (_monoTexts.TryGetValue(key, out var cached))
        {
            cached.SetForegroundBrush(B(color));
            return cached;
        }
        if (_oldMonoTexts.TryGetValue(key, out cached))
        {
            _monoTexts[key] = cached;
            cached.SetForegroundBrush(B(color));
            return cached;
        }

        if (_monoTexts.Count >= 384)
        {
            _oldMonoTexts = _monoTexts;
            _monoTexts = new Dictionary<(string, double, int), FormattedText>(128);
        }

        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            MonoFace, size, B(color), Ppd)
        {
            MaxTextWidth = w,
            Trimming = TextTrimming.None
        };
        _monoTexts[key] = ft;
        return ft;
    }

    public void CodeLabel(string text, Rect r, double size = 11, Color? color = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = MakeMono(text, size, color ?? Theme.Text, r.Width);
        Dc.DrawText(ft, new Point(r.X, r.Y));
    }

    public double CodeWrappedHeight(string text, double size, double width)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var ft = MakeMono(text, size, Colors.White, width);
        return ft.Height;
    }

    // ---------- элементы ----------

    public void Bar(Rect r, double frac, Color color)
    {
        frac = Ui.Clamp(frac);
        Card(r, Theme.Surface2, r.Height / 2);
        if (frac > 0)
        {
            var fill = new Rect(r.Left, r.Top, Math.Max(r.Height, r.Width * frac), r.Height);
            Card(fill, color, r.Height / 2);
        }
    }

    public void Switch(Rect r, double v, bool enabled = true)
    {
        Card(r, enabled ? Theme.Mix(Theme.Surface3, Theme.Accent, v) : Theme.Surface3, r.Height / 2);
        double d = r.Height - 4;
        double x = r.Left + 2 + (r.Width - r.Height) * v;
        Dc.DrawEllipse(B(enabled ? Theme.Text : Theme.Disabled), null,
            new Point(x + d / 2, r.Top + 2 + d / 2), d / 2, d / 2);
    }

    /// <summary>Ползунок: полоса, кружок и зона для мыши (ключ должен начинаться с slider:).</summary>
    public void Slider(string key, Rect track, double frac, bool enabled = true)
    {
        frac = Ui.Clamp(frac);
        Bar(track, frac, enabled ? Theme.Accent : Theme.Muted);
        double grow = enabled ? Math.Max(Isl.Hv(key), Isl.Pv(key)) : 0.0;
        double r = 10 * (1 + 0.18 * grow);
        double cx = track.Left + track.Width * frac;
        var center = new Point(cx, track.Top + track.Height / 2);
        Circle(center, r + 2, Theme.Surface);
        Circle(center, r, enabled ? Theme.Text : Theme.Disabled);
        if (enabled && grow > 0.05)
        {
            Dc.DrawEllipse(null, P(Theme.Accent, 1.8), center, r + 2.5, r + 2.5);
        }
        if (enabled)
            Isl.AddHit(key, new Rect(track.Left - 14, track.Top - 16, track.Width + 28, track.Height + 32));
    }

    /// <summary>Слайдер в стиле Apple Control Center: сплошная плашка с идеальной инверсией иконки.</summary>
    public void AppleSlider(string key, Rect track, double frac, string icon, bool enabled = true)
    {
        frac = Ui.Clamp(frac);
        double hot = enabled ? Math.Max(Isl.Hv(key), Isl.Pv(key)) : 0.0;
        double radius = track.Height / 2;

        // Фон дорожки
        var trackBg = Theme.Mix(Theme.Surface2, Theme.Surface3, 0.35 + 0.25 * hot);
        Card(track, trackBg, radius, Theme.BorderSubtle);

        var iconCenter = new Point(track.Left + 22, track.Top + track.Height / 2);

        // 1. Базовая иконка в обычном цвете текста
        Color baseIconColor = enabled ? Theme.Text : Theme.Disabled;
        DrawSliderIcon(icon, iconCenter, track, baseIconColor, frac);

        // 2. Заполненная часть с клипом: внутри неё иконка мгновенно и субпиксельно инвертируется!
        if (frac > 0.005)
        {
            using (WithClip(new Rect(track.Left, track.Top, track.Width * frac, track.Height)))
            {
                var fillColor = enabled
                    ? (Theme.IsLight ? Theme.Accent : Theme.Mix(Theme.Surface3, Theme.Text, 0.72))
                    : Theme.Disabled;
                Card(track, fillColor, radius);

                Color invertedIconColor = Theme.IsLight ? Colors.White : Theme.Bg;
                DrawSliderIcon(icon, iconCenter, track, invertedIconColor, frac);
            }
        }

        if (enabled)
            Isl.AddHit(key, new Rect(track.Left, track.Top - 6, track.Width, track.Height + 12));
    }

    private void DrawSliderIcon(string icon, Point iconCenter, Rect track, Color iconColor, double frac)
    {
        if (icon is "volume" or "mute" or "speaker")
        {
            DrawSpeaker(iconCenter, 9.5, iconColor, icon == "mute", frac);
        }
        else if (icon is "brightness" or "sun")
        {
            DrawSun(iconCenter, 8.5, iconColor);
        }
        else
        {
            var iconRect = new Rect(track.Left + 12, track.Top + (track.Height - 20) / 2, 22, 20);
            Label(icon, iconRect, 13, iconColor, true, HAlign.Center);
        }
    }

    // ---------- Векторные Apple иконки ----------

    public void DrawWifi(Point c, double s, Color color)
    {
        var pen = P(color, Math.Max(1.6, s * 0.17));

        // Точка у основания
        Circle(new Point(c.X, c.Y + s * 0.50), Math.Max(1.5, s * 0.13), color);

        // Дуга 1 (малая)
        var g1 = new StreamGeometry();
        using (var ctx = g1.Open())
        {
            ctx.BeginFigure(new Point(c.X - s * 0.28, c.Y + s * 0.26), false, false);
            ctx.ArcTo(new Point(c.X + s * 0.28, c.Y + s * 0.26), new Size(s * 0.36, s * 0.36), 0, false, SweepDirection.Clockwise, true, false);
        }
        g1.Freeze();
        Dc.DrawGeometry(null, pen, g1);

        // Дуга 2 (средняя)
        var g2 = new StreamGeometry();
        using (var ctx = g2.Open())
        {
            ctx.BeginFigure(new Point(c.X - s * 0.52, c.Y + s * 0.02), false, false);
            ctx.ArcTo(new Point(c.X + s * 0.52, c.Y + s * 0.02), new Size(s * 0.65, s * 0.65), 0, false, SweepDirection.Clockwise, true, false);
        }
        g2.Freeze();
        Dc.DrawGeometry(null, pen, g2);

        // Дуга 3 (внешняя)
        var g3 = new StreamGeometry();
        using (var ctx = g3.Open())
        {
            ctx.BeginFigure(new Point(c.X - s * 0.74, c.Y - s * 0.22), false, false);
            ctx.ArcTo(new Point(c.X + s * 0.74, c.Y - s * 0.22), new Size(s * 0.92, s * 0.92), 0, false, SweepDirection.Clockwise, true, false);
        }
        g3.Freeze();
        Dc.DrawGeometry(null, pen, g3);
    }

    public void DrawBluetooth(Point c, double s, Color color)
    {
        var pen = P(color, Math.Max(1.6, s * 0.18));

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X - s * 0.35, c.Y - s * 0.35), false, false);
            ctx.LineTo(new Point(c.X + s * 0.35, c.Y + s * 0.35), true, false);
            ctx.LineTo(new Point(c.X, c.Y + s * 0.72), true, false);
            ctx.LineTo(new Point(c.X, c.Y - s * 0.72), true, false);
            ctx.LineTo(new Point(c.X + s * 0.35, c.Y - s * 0.35), true, false);
            ctx.LineTo(new Point(c.X - s * 0.35, c.Y + s * 0.35), true, false);
        }
        g.Freeze();
        Dc.DrawGeometry(null, pen, g);
    }

    public void DrawMoon(Point c, double s, Color color)
    {
        // Идеальный полнотелый полумесяц Apple через Exclude
        var main = new EllipseGeometry(new Point(c.X - s * 0.05, c.Y), s * 0.62, s * 0.62);
        var bite = new EllipseGeometry(new Point(c.X + s * 0.32, c.Y - s * 0.22), s * 0.54, s * 0.54);
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude, main, bite);
        moon.Freeze();
        Dc.DrawGeometry(B(color), null, moon);
    }

    public void DrawCoffee(Point c, double s, Color color)
    {
        var pen = P(color, Math.Max(1.5, s * 0.17));

        // Тело чашки
        var cup = new StreamGeometry();
        using (var ctx = cup.Open())
        {
            ctx.BeginFigure(new Point(c.X - s * 0.44, c.Y - s * 0.15), true, false);
            ctx.LineTo(new Point(c.X + s * 0.22, c.Y - s * 0.15), true, false);
            ctx.LineTo(new Point(c.X + s * 0.17, c.Y + s * 0.30), true, false);
            ctx.ArcTo(new Point(c.X - s * 0.39, c.Y + s * 0.30), new Size(s * 0.28, s * 0.28), 0, false, SweepDirection.Clockwise, true, false);
        }
        cup.Freeze();
        Dc.DrawGeometry(null, pen, cup);

        // Ручка чашки
        var handle = new StreamGeometry();
        using (var ctx = handle.Open())
        {
            ctx.BeginFigure(new Point(c.X + s * 0.22, c.Y - s * 0.08), false, false);
            ctx.ArcTo(new Point(c.X + s * 0.20, c.Y + s * 0.22), new Size(s * 0.18, s * 0.18), 0, false, SweepDirection.Clockwise, true, false);
        }
        handle.Freeze();
        Dc.DrawGeometry(null, pen, handle);

        // Блюдце снизу
        Dc.DrawLine(pen, new Point(c.X - s * 0.52, c.Y + s * 0.44), new Point(c.X + s * 0.32, c.Y + s * 0.44));

        // Две волнистые струйки пара
        Dc.DrawLine(pen, new Point(c.X - s * 0.18, c.Y - s * 0.28), new Point(c.X - s * 0.18, c.Y - s * 0.52));
        Dc.DrawLine(pen, new Point(c.X + s * 0.04, c.Y - s * 0.28), new Point(c.X + s * 0.04, c.Y - s * 0.58));
    }

    public void DrawSun(Point c, double s, Color color)
    {
        Circle(c, s * 0.38, color);
        var pen = P(color, Math.Max(1.4, s * 0.18));
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4.0;
            double r1 = s * 0.58, r2 = s * 0.88;
            Dc.DrawLine(pen, new Point(c.X + Math.Cos(angle) * r1, c.Y + Math.Sin(angle) * r1),
                             new Point(c.X + Math.Cos(angle) * r2, c.Y + Math.Sin(angle) * r2));
        }
    }

    public void DrawClock(Point c, double r, Color color)
    {
        var pen = P(color, Math.Max(1.3, r * 0.22));
        Dc.DrawEllipse(null, pen, c, r, r);
        // Стрелки на 10:10
        Dc.DrawLine(pen, c, new Point(c.X - r * 0.42, c.Y - r * 0.35));
        Dc.DrawLine(pen, c, new Point(c.X + r * 0.50, c.Y - r * 0.30));
    }

    public void DrawMusicNote(Point c, double s, Color color)
    {
        // Две соединенные ноты в стиле Apple Music
        var pen = P(color, Math.Max(1.5, s * 0.2));

        double x1 = c.X - s * 0.38, y1 = c.Y + s * 0.36;
        double x2 = c.X + s * 0.38, y2 = c.Y + s * 0.18;
        double topY = c.Y - s * 0.55;

        // Нота 1 (овал + ножка)
        Dc.DrawEllipse(B(color), null, new Point(x1, y1), s * 0.24, s * 0.18);
        Dc.DrawLine(pen, new Point(x1 + s * 0.2, y1), new Point(x1 + s * 0.2, topY));

        // Нота 2 (овал + ножка)
        Dc.DrawEllipse(B(color), null, new Point(x2, y2), s * 0.24, s * 0.18);
        Dc.DrawLine(pen, new Point(x2 + s * 0.2, y2), new Point(x2 + s * 0.2, topY - s * 0.12));

        // Верхний мостик
        var bridgePen = P(color, Math.Max(2.0, s * 0.28));
        Dc.DrawLine(bridgePen, new Point(x1 + s * 0.2, topY), new Point(x2 + s * 0.2, topY - s * 0.12));
    }

    public void DrawSpeaker(Point c, double s, Color color, bool muted = false, double frac = 1.0)
    {
        var pen = P(color, Math.Max(1.4, s * 0.16));

        // Корпус динамика
        var horn = new StreamGeometry();
        using (var ctx = horn.Open())
        {
            ctx.BeginFigure(new Point(c.X - s * 0.45, c.Y - s * 0.22), true, true);
            ctx.LineTo(new Point(c.X - s * 0.22, c.Y - s * 0.22), true, false);
            ctx.LineTo(new Point(c.X + s * 0.06, c.Y - s * 0.52), true, false);
            ctx.LineTo(new Point(c.X + s * 0.06, c.Y + s * 0.52), true, false);
            ctx.LineTo(new Point(c.X - s * 0.22, c.Y + s * 0.22), true, false);
            ctx.LineTo(new Point(c.X - s * 0.45, c.Y + s * 0.22), true, false);
        }
        horn.Freeze();
        Dc.DrawGeometry(B(color), pen, horn);

        if (muted)
        {
            double cx = c.X + s * 0.36;
            Dc.DrawLine(pen, new Point(cx - s * 0.15, c.Y - s * 0.15), new Point(cx + s * 0.15, c.Y + s * 0.15));
            Dc.DrawLine(pen, new Point(cx - s * 0.15, c.Y + s * 0.15), new Point(cx + s * 0.15, c.Y - s * 0.15));
        }
        else
        {
            if (frac > 0.05)
            {
                var w1 = new StreamGeometry();
                using (var ctx = w1.Open())
                {
                    ctx.BeginFigure(new Point(c.X + s * 0.22, c.Y - s * 0.26), false, false);
                    ctx.ArcTo(new Point(c.X + s * 0.22, c.Y + s * 0.26), new Size(s * 0.36, s * 0.36), 0, false, SweepDirection.Clockwise, true, false);
                }
                w1.Freeze();
                Dc.DrawGeometry(null, pen, w1);
            }
            if (frac > 0.45)
            {
                var w2 = new StreamGeometry();
                using (var ctx = w2.Open())
                {
                    ctx.BeginFigure(new Point(c.X + s * 0.40, c.Y - s * 0.48), false, false);
                    ctx.ArcTo(new Point(c.X + s * 0.40, c.Y + s * 0.48), new Size(s * 0.65, s * 0.65), 0, false, SweepDirection.Clockwise, true, false);
                }
                w2.Freeze();
                Dc.DrawGeometry(null, pen, w2);
            }
        }
    }

    /// <summary>Обложка трека со скруглёнными углами или заглушка, если обложки нет.</summary>
    public void Cover(Rect r, double radius = 16)
    {
        var brush = Isl.CoverBrush;
        if (brush == null)
        {
            Card(r, Theme.Surface2, radius, Theme.BorderSubtle);
            MediaIcon("play", new Point(r.Left + r.Width / 2, r.Top + r.Height / 2), r.Width * 0.16, 0.35);
            return;
        }
        Dc.DrawRoundedRectangle(brush, P(Theme.BorderSubtle, 1), r, radius, radius);
    }

    private bool PressableInternal(string key, Rect rect)
    {
        double pv = Isl.Pv(key);
        if (pv <= 0.001) return false;
        double s = 1 - 0.04 * pv;
        var m = Matrix.Identity;
        m.ScaleAt(s, s, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        var mt = new MatrixTransform(m);
        mt.Freeze();
        Dc.PushTransform(mt);
        return true;
    }

    /// <summary>Всё, что нарисовано внутри, слегка уменьшается при нажатии.</summary>
    public Scope Pressable(string key, Rect rect)
    {
        return PressableInternal(key, rect) ? new Scope(this) : Scope.Empty;
    }

    public Scope Panel(string key, Rect rect, double radius = 24, Color? border = null)
    {
        bool popped = PressableInternal(key, rect);
        Card(rect, Theme.Mix(Theme.Surface, Theme.Surface2, Isl.Hv(key)), radius, border ?? Theme.BorderSubtle);
        return new Scope(this, key, rect, popped);
    }

    public void Button(string key, Rect rect, string text, bool accent = false, double size = 12)
    {
        var (baseC, hot) = accent ? (Theme.Accent, Theme.AccentHot) : (Theme.Surface2, Theme.Surface3);
        using (Pressable(key, rect))
        {
            Card(rect, Theme.Mix(baseC, hot, Isl.Hv(key)), rect.Height / 2);
            Label(text, rect, size, Theme.Text, true, HAlign.Center);
        }
        Isl.AddHit(key, rect);
    }

    public Scope RoundButton(string key, Point c, double r, bool accent = false)
    {
        var rect = new Rect(c.X - r, c.Y - r, 2 * r, 2 * r);
        var (baseC, hot) = accent ? (Theme.Accent, Theme.AccentHot) : (Theme.Surface2, Theme.Surface3);
        bool popped = PressableInternal(key, rect);
        double hv = Isl.Hv(key);
        double curR = r * (1 + 0.06 * hv);
        Circle(c, curR, Theme.Mix(baseC, hot, hv));
        if (hv > 0.02)
        {
            Dc.DrawEllipse(null, P(Color.FromArgb((byte)(55 * hv), 255, 255, 255), 1), c, curR, curR);
        }
        return new Scope(this, key, rect, popped);
    }

    public void CloseDot(string key, Point c, double r = 11)
    {
        var rect = new Rect(c.X - r, c.Y - r, 2 * r, 2 * r);
        using (Pressable(key, rect))
        {
            Circle(c, r, Theme.Mix(Theme.Surface3, Theme.C("#34343f"), Isl.Hv(key)));
            double d = 3.5;
            Line(new Point(c.X - d, c.Y - d), new Point(c.X + d, c.Y + d), Theme.Text, 1.6);
            Line(new Point(c.X - d, c.Y + d), new Point(c.X + d, c.Y - d), Theme.Text, 1.6);
        }
        Isl.AddHit(key, rect);
    }

    public void MediaIcon(string kind, Point c, double s, double alpha = 1.0, Color? color = null)
    {
        if (alpha < 0.03) return;
        using var _ = WithOpacity(alpha);
        Color col = color ?? Theme.Text;
        double x = c.X, y = c.Y;

        if (kind == "play")
        {
            // Равнобедренный скругленный треугольник Play со смещенным оптическим центром
            double ox = x + s * 0.12;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(ox - s * 0.45, y - s * 0.72), true, true);
                ctx.LineTo(new Point(ox + s * 0.65, y), true, false);
                ctx.LineTo(new Point(ox - s * 0.45, y + s * 0.72), true, false);
            }
            g.Freeze();
            var pen = P(col, s * 0.28);
            Dc.DrawGeometry(B(col), pen, g);
        }
        else if (kind == "pause")
        {
            // Две аккуратные вертикальные капсулы (pill)
            double w = Math.Max(2.0, s * 0.36);
            double h = s * 1.5;
            double r = w / 2;
            double gap = s * 0.42;
            var leftR = new Rect(x - gap / 2 - w, y - h / 2, w, h);
            var rightR = new Rect(x + gap / 2, y - h / 2, w, h);
            Dc.DrawRoundedRectangle(B(col), null, leftR, r, r);
            Dc.DrawRoundedRectangle(B(col), null, rightR, r, r);
        }
        else
        {
            // prev / next: стрелка + вертикальная черта
            int d = kind == "next" ? 1 : -1;
            double bw = Math.Max(1.8, s * 0.24);
            double bh = s * 1.35;
            double br = bw / 2;
            var barR = new Rect(x + d * s * 0.48 - (d < 0 ? bw : 0), y - bh / 2, bw, bh);
            Dc.DrawRoundedRectangle(B(col), null, barR, br, br);

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x - d * s * 0.65, y - s * 0.65), true, true);
                ctx.LineTo(new Point(x + d * s * 0.18, y), true, false);
                ctx.LineTo(new Point(x - d * s * 0.65, y + s * 0.65), true, false);
            }
            g.Freeze();
            var pen = P(col, s * 0.22);
            Dc.DrawGeometry(B(col), pen, g);
        }
    }

    public void WeatherIcon(int code, Point c, double s, double alpha = 1.0, bool isNight = false)
    {
        if (alpha < 0.03) return;
        using var _ = WithOpacity(alpha);
        double x = c.X, y = c.Y;

        if (code is 0 or 1)
        {
            if (!isNight)
            {
                Circle(c, s * 0.52, Theme.C("#FFAA00"));
                Pen rayPen = P(Theme.C("#FFB81C"), Math.Max(1.4, s * 0.2));
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4.0;
                    double r1 = s * 0.72, r2 = s * 1.05;
                    Dc.DrawLine(rayPen, new Point(x + Math.Cos(angle) * r1, y + Math.Sin(angle) * r1),
                                        new Point(x + Math.Cos(angle) * r2, y + Math.Sin(angle) * r2));
                }
            }
            else
            {
                var g = new PathGeometry();
                var fig = new PathFigure { StartPoint = new Point(x + s * 0.2, y - s * 0.8), IsClosed = true, IsFilled = true };
                fig.Segments.Add(new ArcSegment(new Point(x + s * 0.2, y + s * 0.8), new Size(s * 0.85, s * 0.85), 0, false, SweepDirection.Clockwise, true));
                fig.Segments.Add(new ArcSegment(new Point(x + s * 0.2, y - s * 0.8), new Size(s * 0.55, s * 0.75), 0, false, SweepDirection.Counterclockwise, true));
                g.Figures.Add(fig);
                g.Freeze();
                Dc.DrawGeometry(B(Theme.C("#E2E8F0")), null, g);
            }
        }
        else if (code == 2)
        {
            Circle(new Point(x - s * 0.35, y - s * 0.32), s * 0.42, Theme.C("#FFAA00"));
            DrawCloud(new Point(x + s * 0.1, y + s * 0.15), s * 0.75, Theme.C("#C8D0DC"), Theme.C("#F1F5F9"));
        }
        else if (code == 3)
        {
            DrawCloud(c, s * 0.88, Theme.C("#8E96A0"), Theme.C("#CBD5E1"));
        }
        else if (code is 45 or 48)
        {
            Pen fogPen = P(Theme.C("#94A3B8"), Math.Max(1.8, s * 0.24));
            Dc.DrawLine(fogPen, new Point(x - s * 0.8, y - s * 0.4), new Point(x + s * 0.8, y - s * 0.4));
            Dc.DrawLine(fogPen, new Point(x - s * 0.6, y), new Point(x + s * 0.6, y));
            Dc.DrawLine(fogPen, new Point(x - s * 0.75, y + s * 0.4), new Point(x + s * 0.75, y + s * 0.4));
        }
        else if (code is 51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82)
        {
            DrawCloud(new Point(x, y - s * 0.25), s * 0.78, Theme.C("#64748B"), Theme.C("#94A3B8"));
            Pen rainPen = P(Theme.C("#38BDF8"), Math.Max(1.5, s * 0.2));
            Dc.DrawLine(rainPen, new Point(x - s * 0.45, y + s * 0.32), new Point(x - s * 0.6, y + s * 0.75));
            Dc.DrawLine(rainPen, new Point(x, y + s * 0.32), new Point(x - s * 0.15, y + s * 0.75));
            Dc.DrawLine(rainPen, new Point(x + s * 0.45, y + s * 0.32), new Point(x + s * 0.3, y + s * 0.75));
        }
        else if (code is 71 or 73 or 75 or 77 or 85 or 86)
        {
            DrawCloud(new Point(x, y - s * 0.25), s * 0.78, Theme.C("#7B8B9E"), Theme.C("#CBD5E1"));
            Circle(new Point(x - s * 0.45, y + s * 0.52), Math.Max(1.4, s * 0.18), Theme.C("#FFFFFF"));
            Circle(new Point(x, y + s * 0.62), Math.Max(1.4, s * 0.18), Theme.C("#FFFFFF"));
            Circle(new Point(x + s * 0.45, y + s * 0.52), Math.Max(1.4, s * 0.18), Theme.C("#FFFFFF"));
        }
        else if (code is 95 or 96 or 99)
        {
            DrawCloud(new Point(x, y - s * 0.25), s * 0.78, Theme.C("#475569"), Theme.C("#64748B"));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x + s * 0.08, y + s * 0.05), true, true);
                ctx.LineTo(new Point(x - s * 0.18, y + s * 0.45), true, false);
                ctx.LineTo(new Point(x + s * 0.05, y + s * 0.45), true, false);
                ctx.LineTo(new Point(x - s * 0.15, y + s * 0.9), true, false);
                ctx.LineTo(new Point(x + s * 0.22, y + s * 0.35), true, false);
                ctx.LineTo(new Point(x, y + s * 0.35), true, false);
            }
            g.Freeze();
            Dc.DrawGeometry(B(Theme.C("#FBBF24")), null, g);
        }
        else
        {
            DrawCloud(c, s * 0.85, Theme.C("#8E96A0"), Theme.C("#CBD5E1"));
        }
    }

    private void DrawCloud(Point c, double s, Color backColor, Color frontColor)
    {
        double x = c.X, y = c.Y;
        var baseR = new Rect(x - s * 0.65, y, s * 1.3, s * 0.55);
        Dc.DrawRoundedRectangle(B(frontColor), null, baseR, s * 0.27, s * 0.27);
        Circle(new Point(x - s * 0.32, y + s * 0.08), s * 0.36, frontColor);
        Circle(new Point(x + s * 0.05, y - s * 0.1), s * 0.46, frontColor);
        Circle(new Point(x + s * 0.38, y + s * 0.12), s * 0.3, frontColor);
    }
}


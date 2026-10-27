using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace QuickNotchCs;

public static class Clock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    public static double Now => Watch.Elapsed.TotalSeconds;
}

public abstract class Section
{
    public string Key = "";
    private string _title = "";
    public virtual string Title
    {
        get => Key switch
        {
            "home" => Loc.T("Управление", "Control", "Kontrol"),
            "clipboard" => Loc.T("Буфер", "Clipboard", "Pano"),
            "links" => Loc.T("Ссылки", "Links", "Bağlantılar"),
            "settings" => Loc.T("Настройки", "Settings", "Ayarlar"),
            "media" => Loc.T("Медиа", "Media", "Medya"),
            "sound" => Loc.T("Звук", "Sound", "Ses"),
            "system" => Loc.T("Система", "System", "Sistem"),
            "weather" => Loc.T("Погода", "Weather", "Hava Durumu"),
            "ai" => Loc.T("Аэро", "Aero", "Aero"),
            "calendar" => Loc.T("Календарь", "Calendar", "Takvim"),
            _ => !string.IsNullOrEmpty(_title) ? _title : Key
        };
        set => _title = value;
    }

    public virtual string Icon => Key switch
    {
        "home" => "⊞",
        "clipboard" => "⎘",
        "links" => "↗",
        "settings" => "⚙",
        "media" => "♫",
        "sound" => "🔊",
        "system" => "⚡",
        "weather" => "☼",
        "ai" => "🤖",
        "calendar" => "🗓",
        _ => ""
    };

    private string? _dockLabel;
    private string? _lastDockLang;
    public string DockLabel
    {
        get
        {
            if (_dockLabel == null || _lastDockLang != Loc.Lang)
            {
                _lastDockLang = Loc.Lang;
                _dockLabel = $"{Icon} {Title}";
            }
            return _dockLabel;
        }
    }

    public abstract void Paint(IslandView isl, Painter p, Rect rect);
    public virtual void Click(IslandView isl, string key) { }
    /// <summary>Один раз при старте, когда остров уже создан.</summary>
    public virtual void Attach(IslandView isl) { }
    /// <summary>Вызывается при переходе в раздел.</summary>
    public virtual void Enter(IslandView isl) { }
    /// <summary>Раз в секунду, пока раздел открыт.</summary>
    public virtual void Tick(IslandView isl) { }
    /// <summary>Колёсико над островом (положительное значение — вверх).</summary>
    public virtual void Wheel(IslandView isl, double notches) { }
    /// <summary>Мышь двигается с зажатой кнопкой над ползунком key.</summary>
    public virtual void Drag(IslandView isl, string key, Point pos) { }
    /// <summary>Кнопку отпустили.</summary>
    public virtual void DragEnd(IslandView isl, string key) { }

    protected static Rect R(double x, double y, double w, double h) => new(x, y, Math.Max(0, w), Math.Max(0, h));
}

public static class Sections
{
    public static readonly HomeSection Home = new();
    public static readonly ClipboardSection Clipboard = new();
    public static readonly LinksSection Links = new();
    public static readonly SettingsSection Settings = new();

    public static readonly MediaSection Media = new();
    public static readonly SoundSection Sound = new();
    public static readonly SystemSection System = new();
    public static readonly WeatherSection Weather = new();
    public static readonly AiSection Ai = new();
    public static readonly CalendarSection Calendar = new();

    // Основные вкладки для лаконичной навигации в стиле Apple
    public static readonly Section[] Primary = { Home, Clipboard, Ai, Links, Settings };
    public static readonly Section[] All = { Home, Clipboard, Ai, Links, Settings, Media, Sound, System, Weather, Calendar };
    public static Section ByKey(string key) => All.FirstOrDefault(s => s.Key == key) ?? Home;
}

// ------------------------------------------------------------------ Пункт управления (Apple Control Center)

public sealed class HomeSection : Section
{
    public HomeSection() { Key = "home"; Title = "Управление"; }

    public override void Attach(IslandView isl)
    {
        isl.RunLater(150, () =>
        {
            Sections.Settings.Enter(isl);
            Sections.Sound.RefreshVolume(isl);
            Sections.Weather.Ensure(isl);
        });
    }

    public override void Enter(IslandView isl)
    {
        Sections.Weather.Ensure(isl);
        Sections.Settings.Enter(isl);          // Wi-Fi, Bluetooth, яркость, тема
        Sections.Sound.RefreshVolume(isl);
        isl.RefreshSnapshot();
    }

    private double _nextSnapTime;

    public override void Tick(IslandView isl)
    {
        var sett = Sections.Settings;
        Sections.Weather.Ensure(isl);
        sett.Tick(isl);
        if (Clock.Now >= _nextSnapTime)
        {
            _nextSnapTime = Clock.Now + 2.5; // Снимок системы каждые 2.5 сек вместо 1 сек
            isl.RefreshSnapshot();
        }
        if (!Sections.Sound.Dragging) Sections.Sound.RefreshVolume(isl);    // не мешаем, пока тянут ползунок
        bool dark = SystemService.GetDarkTheme();                           // тему могли сменить снаружи
        if (dark != sett.Dark)
        {
            sett.Dark = dark;
            isl.Nsp("sys:dark").Target = dark ? 1.0 : 0.0;
            Theme.UpdateSystemTheme(dark);
            isl.Kick();
        }
    }

    public override void Paint(IslandView isl, Painter p, Rect rect)
    {
        var sett = Sections.Settings;
        var sound = Sections.Sound;
        const double gap = 16;
        double colW = (rect.Width - gap) / 2;
        double leftX = rect.Left;
        double rightX = rect.Left + colW + gap;
        double y0 = rect.Top;

        (bool On, string Status, bool Enabled) Radio(string kind)
        {
            bool? state = sett.States[kind];
            if (state == null) return (false, sett.Busy ? "…" : Loc.T("Нет", "N/A", "Yok"), false);
            return (state.Value, state.Value ? Loc.T("Вкл", "On", "Açık") : Loc.T("Выкл", "Off", "Kapalı"), true);
        }

        bool awake = isl.S.KeepAwake;
        var wifi = Radio("WiFi");
        var bt = Radio("Bluetooth");

        // Математически точная сетка Apple Bento Grid:
        // Верхний ряд: одинаковая высота для обеих колонок
        double topH = 175;
        var connRect = R(leftX, y0, colW, topH);
        var mediaRect = R(rightX, y0, colW, topH);

        // Нижний ряд: общая верхняя линия и одинаковый нижний край
        double botY = y0 + topH + 14;
        double botH = rect.Bottom - botY;
        var sliderCardRect = R(leftX, botY, colW, botH);
        var glanceRect = R(rightX, botY, colW, botH);

        // Отрисовка 4 модулей Bento Grid
        AppleConnectivityCard(isl, p, connRect, wifi, bt, sett.Dark, awake);
        AppleMediaCard(isl, p, mediaRect);
        AppleSlidersCard(isl, p, sliderCardRect);
        AppleWeatherGlanceCard(isl, p, glanceRect);
    }

    private static void AppleConnectivityCard(IslandView isl, Painter p, Rect r,
        (bool On, string Status, bool Enabled) wifi,
        (bool On, string Status, bool Enabled) bt,
        bool dark, bool awake)
    {
        // Единая карточка связности без вложенных коробок
        p.Card(r, Theme.Surface, 20, Theme.BorderSubtle);

        // Тонкие разделители Apple по центру
        p.Line(new Point(r.Left + r.Width / 2, r.Top + 14), new Point(r.Left + r.Width / 2, r.Bottom - 14), Theme.BorderSubtle, 1);
        p.Line(new Point(r.Left + 14, r.Top + r.Height / 2), new Point(r.Right - 14, r.Top + r.Height / 2), Theme.BorderSubtle, 1);

        double cellW = r.Width / 2;
        double cellH = r.Height / 2;

        var items = new (string Key, string Title, string Status, bool On, bool Enabled, string IconKind)[]
        {
            ("WiFi", "Wi-Fi", wifi.Status, wifi.On, wifi.Enabled, "wifi"),
            ("Bluetooth", "Bluetooth", bt.Status, bt.On, bt.Enabled, "bluetooth"),
            ("dark", Loc.T("Тёмная тема", "Dark Mode", "Karanlık Mod"), dark ? Loc.T("Вкл", "On", "Açık") : Loc.T("Выкл", "Off", "Kapalı"), dark, true, "moon"),
            ("awake", Loc.T("Бодрствование", "Keep Awake", "Uyanık Tut"), awake ? Loc.T("Вкл", "On", "Açık") : Loc.T("Выкл", "Off", "Kapalı"), awake, true, "awake"),
        };

        for (int i = 0; i < items.Length; i++)
        {
            int row = i / 2, col = i % 2;
            var cellRect = new Rect(r.Left + col * cellW, r.Top + row * cellH, cellW, cellH);
            var item = items[i];
            string hitKey = $"tog:{item.Key}";

            double want = item.On ? 1.0 : 0.0;
            var sp = isl.Nsp($"home:{hitKey}", want);
            if (sp.Target != want)
            {
                sp.Target = want;
                isl.StartFrames();
            }
            double hot = isl.Hv(hitKey);

            // Мягкая подсветка квадранта при наведении
            var hoverRect = new Rect(cellRect.Left + 4, cellRect.Top + 4, cellRect.Width - 8, cellRect.Height - 8);
            if (hot > 0.01)
            {
                p.Card(hoverRect, Theme.Mix(Theme.Surface, Theme.Surface2, hot * 0.45), 14);
            }

            using (p.Pressable(hitKey, cellRect))
            {
                var circleCenter = new Point(cellRect.Left + 28, cellRect.Top + cellRect.Height / 2);
                double circleR = 17.5;
                Color circleColor = item.Enabled
                    ? (item.On ? Theme.Accent : Theme.Mix(Theme.Surface2, Theme.Surface3, 0.75 + 0.25 * hot))
                    : Theme.Disabled;

                p.Circle(circleCenter, circleR, circleColor);
                if (item.On && item.Enabled)
                {
                    p.Circle(circleCenter, circleR + 2.0 + 0.8 * hot,
                        Color.FromArgb((byte)(45 + 40 * hot), Theme.Accent.R, Theme.Accent.G, Theme.Accent.B));
                }

                Color iconColor = item.On ? Colors.White : (item.Enabled ? Theme.Text : Theme.Disabled);
                if (item.IconKind == "wifi") p.DrawWifi(circleCenter, 9.5, iconColor);
                else if (item.IconKind == "bluetooth") p.DrawBluetooth(circleCenter, 10.0, iconColor);
                else if (item.IconKind == "moon") p.DrawMoon(circleCenter, 10.0, iconColor);
                else if (item.IconKind == "awake") p.DrawCoffee(circleCenter, 9.5, iconColor);

                double tx = cellRect.Left + 54;
                double tw = cellRect.Width - 58;
                p.Label(item.Title, new Rect(tx, cellRect.Top + cellRect.Height / 2 - 15, tw, 17), 12, item.Enabled ? Theme.Text : Theme.Disabled, true);
                p.Label(item.Status, new Rect(tx, cellRect.Top + cellRect.Height / 2 + 2, tw, 14), 10.5, item.On ? Theme.AccentHot : Theme.Muted);
            }
            isl.AddHit(hitKey, cellRect);
        }
    }

    private static void AppleSlidersCard(IslandView isl, Painter p, Rect r)
    {
        var sett = Sections.Settings;
        var sound = Sections.Sound;
        p.Card(r, Theme.Surface, 20, Theme.BorderSubtle);

        // 1. ЗВУК
        double level = Ui.Clamp(isl.Nsp("volume").X);
        string volText = sound.Muted ? Loc.T("Без звука", "Muted", "Sessiz") : $"{Math.Round(level * 100)}%";
        p.Label(Loc.T("ЗВУК", "SOUND", "SES"), R(r.Left + 18, r.Top + 12, 100, 14), 10, Theme.Muted, true);
        p.Label(volText, R(r.Right - 18 - 80, r.Top + 11, 80, 16), 11.5, sound.Muted ? Theme.Red : Theme.Text, true, HAlign.Right);

        var volTrack = R(r.Left + 16, r.Top + 28, r.Width - 32, 34);
        sound.Tracks["slider:volume"] = volTrack;
        p.AppleSlider("slider:volume", volTrack, level, sound.Muted ? "mute" : "volume", sound.Error == null);

        // Индикатор активного устройства и кнопка Mute
        string activeDev = sound.Devices.FirstOrDefault(d => d.Id == sound.DefaultId)?.Name ?? Loc.T("Динамики / Наушники", "Speakers / Headphones", "Hoparlör / Kulaklık");
        if (activeDev.Length > 24) activeDev = activeDev[..22] + "…";
        var devChip = R(r.Left + 16, r.Top + 68, r.Width - 32 - 105, 24);
        double devHv = isl.Hv("go:sound");
        p.Card(devChip, Theme.Mix(Theme.Surface2, Theme.Surface3, devHv * 0.4), 12, Theme.BorderSubtle);
        p.Label($"{activeDev}  ›", R(devChip.Left + 10, devChip.Top, devChip.Width - 14, devChip.Height), 9.5, Theme.Mix(Theme.Muted, Theme.Text, 0.5 + 0.5 * devHv), true, HAlign.Left);
        isl.AddHit("go:sound", devChip);

        var muteChip = R(r.Right - 16 - 98, r.Top + 68, 98, 24);
        double muteHv = isl.Hv("tog:mute");
        Color muteBg = sound.Muted
            ? Theme.Mix(Theme.Surface2, Theme.Red, 0.35 + 0.2 * muteHv)
            : Theme.Mix(Theme.Surface2, Theme.Surface3, muteHv * 0.4);
        p.Card(muteChip, muteBg, 12, Theme.BorderSubtle);
        p.Label(sound.Muted ? Loc.T("Без звука", "Muted", "Sessiz") : Loc.T("Звук активен", "Audio Active", "Ses Açık"), muteChip, 9.5, sound.Muted ? Theme.Red : Theme.Text, true, HAlign.Center);
        isl.AddHit("tog:mute", muteChip);

        // Тонкая разделительная черта Apple
        p.Line(new Point(r.Left + 16, r.Top + 101), new Point(r.Right - 16, r.Top + 101), Theme.BorderSubtle, 1);

        // 2. ЯРКОСТЬ ДИСПЛЕЯ
        double bright = Ui.Clamp(isl.Nsp("brightness").X);
        p.Label(Loc.T("ЯРКОСТЬ ДИСПЛЕЯ", "DISPLAY", "PARLAKLIK"), R(r.Left + 18, r.Top + 108, 140, 14), 10, Theme.Muted, true);
        p.Label($"{Math.Round(bright * 100)}%", R(r.Right - 18 - 80, r.Top + 107, 80, 16), 11.5, Theme.Text, true, HAlign.Right);

        var brTrack = R(r.Left + 16, r.Top + 125, r.Width - 32, 34);
        sett.Tracks["slider:brightness"] = brTrack;
        p.AppleSlider("slider:brightness", brTrack, bright, "brightness", sett.BrError == null || sett.BrKnown);

        // 3. Ссылка на микшер приложений
        var mixerChip = R(r.Left + 16, r.Top + 170, r.Width - 32, 26);
        double mixHv = isl.Hv("go:sound");
        p.Card(mixerChip, Theme.Mix(Theme.Surface2, Theme.Surface3, mixHv * 0.45), 13, Theme.BorderSubtle);
        p.Label(Loc.T("Микшер приложений и устройств  ›", "App Volume Mixer & Devices  ›", "Uygulama Ses Karıştırıcısı  ›"), mixerChip, 10, Theme.Mix(Theme.Muted, Theme.AccentHot, mixHv), true, HAlign.Center);
        isl.AddHit("go:sound", mixerChip);
    }

    private static void AppleMediaCard(IslandView isl, Painter p, Rect r)
    {
        var m = isl.Media;
        double hot = isl.Hv("go:media");
        var cardBg = Theme.Mix(Theme.Surface, Theme.Surface2, hot * 0.35);
        p.Card(r, cardBg, 20, Theme.BorderSubtle);
        isl.AddHit("go:media", r);

        // Заголовок карточки
        p.Label(Loc.T("СЕЙЧАС ИГРАЕТ", "NOW PLAYING", "ŞU AN ÇALINIYOR"), R(r.Left + 18, r.Top + 14, 140, 16), 10, Theme.Muted, true);

        // Анимированный эквалайзер Apple справа
        double t = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (!string.IsNullOrEmpty(m.Title))
        {
            double eqX = r.Right - 36, eqY = r.Top + 20;
            double[] restingH3 = { 5.0, 11.0, 7.0 };
            for (int i = 0; i < 3; i++)
            {
                double w1 = Math.Sin(t * 4.2 + i * 1.1);
                double w2 = Math.Sin(t * 6.8 - i * 1.4);
                double blend = Math.Clamp(0.5 + 0.32 * w1 + 0.18 * w2, 0.0, 1.0);
                double liveH = 4.5 + 8.0 * Math.Pow(blend, 1.25);
                double hgt = isl.Media.Playing ? liveH : restingH3[i];
                p.Card(R(eqX + i * 5.2, eqY - hgt / 2, 2.6, hgt), Theme.Accent, 1.3);
            }
        }

        if (string.IsNullOrEmpty(m.Title))
        {
            // Apple Music placeholder squircle
            var squircleRect = R(r.Left + 20, r.Top + 44, 52, 52);
            p.Card(squircleRect, Theme.Mix(Theme.Surface2, Theme.Accent, 0.16), 13, Theme.BorderSubtle);
            p.DrawMusicNote(new Point(squircleRect.Left + 26, squircleRect.Top + 26), 18, Theme.AccentHot);

            p.Label(Loc.T("Не воспроизводится", "Not Playing", "Çalınmıyor"), R(r.Left + 84, r.Top + 48, r.Width - 98, 20), 13.5, Theme.Text, true);
            p.Label(Loc.T("Включите Spotify или видео в браузере", "Play media in Spotify or browser", "Spotify veya tarayıcıda medya oynatın"), R(r.Left + 84, r.Top + 70, r.Width - 98, 16), 10.5, Theme.Muted);

            // Неактивная полоса прогресса Apple
            var inactiveTrack = R(r.Left + 18, r.Top + 108, r.Width - 36, 4);
            p.Card(inactiveTrack, Theme.Surface2, 2);
            p.Label("--:--", R(r.Left + 18, r.Top + 116, 50, 14), 9.5, Theme.Muted);
            p.Label("--:--", R(r.Right - 18 - 50, r.Top + 116, 50, 14), 9.5, Theme.Muted, false, HAlign.Right);

            // Неактивные кнопки воспроизведения
            double cy = r.Top + 148;
            var prevC = new Point(r.Left + r.Width / 2 - 46, cy);
            var playC = new Point(r.Left + r.Width / 2, cy);
            var nextC = new Point(r.Left + r.Width / 2 + 46, cy);

            p.Circle(prevC, 14, Theme.Surface2);
            p.MediaIcon("prev", prevC, 8.0, 0.35, Theme.Muted);

            p.Circle(playC, 17, Theme.Surface2);
            p.MediaIcon("play", playC, 9.5, 0.4, Theme.Muted);

            p.Circle(nextC, 14, Theme.Surface2);
            p.MediaIcon("next", nextC, 8.0, 0.35, Theme.Muted);
            return;
        }

        // Обложка альбома
        var coverRect = R(r.Left + 18, r.Top + 36, 58, 58);
        p.Cover(coverRect, 12);

        // Метаданные
        double tx = r.Left + 88, tw = r.Width - 104;
        p.Label(m.Title, R(tx, r.Top + 36, tw, 20), 13.5, Theme.Text, true);
        p.Label(m.Artist, R(tx, r.Top + 56, tw, 16), 11.0, Theme.Muted);

        if (!string.IsNullOrEmpty(m.App))
        {
            double badgeW = p.TextWidth(m.App, 9.5, true) + 14;
            var badgeRect = R(tx, r.Top + 75, Math.Min(badgeW, tw), 18);
            p.Card(badgeRect, Theme.Surface2, 9, Theme.BorderSubtle);
            p.Label(m.App, badgeRect, 9.5, Theme.AccentHot, true, HAlign.Center);
        }

        // Полоса прогресса воспроизведения
        double dur = m.Dur > 0 ? m.Dur : 210.0;
        double pos = isl.MediaPos();
        double frac = dur > 0 ? Ui.Clamp(pos / dur) : 0.0;

        var seekTrack = R(r.Left + 18, r.Top + 106, r.Width - 36, 5);
        Sections.Media.Track = seekTrack;
        p.Bar(seekTrack, frac, Theme.Accent);
        isl.AddHit("slider:seek", new Rect(seekTrack.Left, seekTrack.Top - 6, seekTrack.Width, seekTrack.Height + 12));

        // Таймкоды
        p.Label(Ui.FmtTime(pos), R(r.Left + 18, r.Top + 115, 60, 14), 9.5, Theme.Muted);
        p.Label(Ui.FmtTime(dur), R(r.Right - 18 - 60, r.Top + 115, 60, 14), 9.5, Theme.Muted, false, HAlign.Right);

        // Apple кнопки управления
        double cyActive = r.Top + 148;
        var prevCActive = new Point(r.Left + r.Width / 2 - 46, cyActive);
        var playCActive = new Point(r.Left + r.Width / 2, cyActive);
        var nextCActive = new Point(r.Left + r.Width / 2 + 46, cyActive);

        var prevR = new Rect(prevCActive.X - 15, cyActive - 15, 30, 30);
        var playR = new Rect(playCActive.X - 17, cyActive - 17, 34, 34);
        var nextR = new Rect(nextCActive.X - 15, cyActive - 15, 30, 30);

        using (p.Pressable("media:prev", prevR))
        {
            double hv = isl.Hv("media:prev");
            p.Circle(prevCActive, 14, Theme.Mix(Theme.Surface2, Theme.Surface3, hv));
            p.MediaIcon("prev", prevCActive, 8.0, 0.8 + 0.2 * hv, Theme.Text);
        }
        isl.AddHit("media:prev", prevR);

        using (p.Pressable("media:play", playR))
        {
            double hv = isl.Hv("media:play");
            Color playBg = Theme.Mix(Theme.Accent, Theme.AccentHot, hv);
            p.Circle(playCActive, 17, playBg);
            if (hv > 0.05)
            {
                p.Circle(playCActive, 20, Color.FromArgb((byte)(45 * hv), Theme.Accent.R, Theme.Accent.G, Theme.Accent.B));
            }
            double vPlay = Ui.Clamp(isl.Nsp("play").X);
            p.MediaIcon("play", playCActive, 9.5, 1 - vPlay, Colors.White);
            p.MediaIcon("pause", playCActive, 9.5, vPlay, Colors.White);
        }
        isl.AddHit("media:play", playR);

        using (p.Pressable("media:next", nextR))
        {
            double hv = isl.Hv("media:next");
            p.Circle(nextCActive, 14, Theme.Mix(Theme.Surface2, Theme.Surface3, hv));
            p.MediaIcon("next", nextCActive, 8.0, 0.8 + 0.2 * hv, Theme.Text);
        }
        isl.AddHit("media:next", nextR);
    }

    private static void AppleWeatherGlanceCard(IslandView isl, Painter p, Rect r)
    {
        double gap = 12;
        double halfW = (r.Width - gap) / 2;

        // Левая половинка: Погода
        var wRect = R(r.Left, r.Top, halfW, r.Height);
        double wHot = isl.Hv("go:weather");
        p.Card(wRect, Theme.Mix(Theme.Surface, Theme.Surface2, wHot * 0.35), 20, Theme.BorderSubtle);
        isl.AddHit("go:weather", wRect);

        var w = Sections.Weather;
        if (w.Data != null)
        {
            var d = w.Data;
            p.Label(Loc.T("ПОГОДА", "WEATHER", "HAVA DURUMU"), R(wRect.Left + 16, wRect.Top + 12, 80, 14), 10, Theme.Muted, true);
            if (!string.IsNullOrEmpty(d.City))
                p.Label(d.City, R(wRect.Right - 16 - 85, wRect.Top + 11, 85, 16), 10.5, Theme.Mix(Theme.Muted, Theme.Text, 0.4 + 0.6 * wHot), true, HAlign.Right);

            string sign = d.Temp > 0 ? "+" : "";
            p.Label($"{sign}{Math.Round(d.Temp)}°", R(wRect.Left + 16, wRect.Top + 30, 90, 38), 30, Theme.Text, true);
            p.WeatherIcon(d.Code, new Point(wRect.Right - 32, wRect.Top + 48), 16 + 1.2 * wHot);

            p.Label(d.Description, R(wRect.Left + 16, wRect.Top + 72, wRect.Width - 32, 18), 11.5, Theme.AccentHot, true);
            p.Label(Loc.T($"Ощущ. {Math.Round(d.Apparent)}°  ·  Ветер {d.WindSpeed / 3.6:F1} м/с",
                          $"Feels {Math.Round(d.Apparent)}°  ·  Wind {d.WindSpeed / 3.6:F1} m/s",
                          $"Hissedilen {Math.Round(d.Apparent)}°  ·  Rüzgar {d.WindSpeed / 3.6:F1} m/s"),
                    R(wRect.Left + 16, wRect.Top + 92, wRect.Width - 32, 16), 9.5, Theme.Muted);

            var today = d.Daily.FirstOrDefault();
            if (today != null)
            {
                p.Label(Loc.T($"Мин: {Math.Round(today.TempMin)}°   Макс: {Math.Round(today.TempMax)}°",
                              $"Low: {Math.Round(today.TempMin)}°   High: {Math.Round(today.TempMax)}°",
                              $"Düşük: {Math.Round(today.TempMin)}°   Yüksek: {Math.Round(today.TempMax)}°"),
                        R(wRect.Left + 16, wRect.Top + 112, wRect.Width - 32, 16), 9.5, Theme.Mix(Theme.Muted, Theme.Text, 0.65));
            }
            p.Label(Loc.T($"Влажность: {d.Humidity}%  ·  {Math.Round(d.Pressure * 0.750062)} мм",
                          $"Humidity: {d.Humidity}%  ·  {Math.Round(d.Pressure)} hPa",
                          $"Nem: %{d.Humidity}  ·  {Math.Round(d.Pressure)} hPa"),
                    R(wRect.Left + 16, wRect.Top + 130, wRect.Width - 32, 16), 9.0, Theme.Muted);
            
            p.Label(Loc.T("Прогноз на 6 дней  →", "6-day forecast  →", "6 Günlük Tahmin  →"),
                R(wRect.Left + 16, wRect.Top + 174, wRect.Width - 32, 14), 9.5, Theme.Mix(Theme.Muted, Theme.AccentHot, wHot), wHot > 0.3);
        }
        else
        {
            p.Label(Loc.T("ПОГОДА", "WEATHER", "HAVA DURUMU"), R(wRect.Left + 16, wRect.Top + 12, 80, 14), 10, Theme.Muted, true);
            p.Label(Loc.T("Выберите город", "Choose city", "Şehir seçin"), R(wRect.Left + 16, wRect.Top + 46, wRect.Width - 32, 24), 13.5, Theme.Text, true);
            p.Label(Loc.T("Нажмите, чтобы настроить", "Click to configure", "Ayrıntılar için tıklayın"), R(wRect.Left + 16, wRect.Top + 72, wRect.Width - 32, 16), 10.5, Theme.Muted);
            p.Label(Loc.T("Настроить  →", "Configure  →", "Yapılandır  →"),
                R(wRect.Left + 16, wRect.Top + 174, wRect.Width - 32, 14), 9.5, Theme.Mix(Theme.Muted, Theme.AccentHot, wHot), wHot > 0.3);
        }

        // Правая половинка: Системные датчики (CPU / RAM / SSD)
        var sysRect = R(r.Left + halfW + gap, r.Top, halfW, r.Height);
        double sysHot = isl.Hv("go:system");
        p.Card(sysRect, Theme.Mix(Theme.Surface, Theme.Surface2, sysHot * 0.35), 20, Theme.BorderSubtle);
        isl.AddHit("go:system", sysRect);

        p.Label(Loc.T("СИСТЕМА", "SYSTEM", "SİSTEM"), R(sysRect.Left + 16, sysRect.Top + 12, 100, 14), 10, Theme.Muted, true);

        var snap = isl.Snapshot;
        double cpuFrac = snap?.CpuFraction ?? 0.15;
        string cpuText = snap?.Cpu ?? "—";
        double ramFrac = snap?.MemoryFraction ?? 0.35;
        string ramText = snap?.MemoryPercent ?? "—";
        double diskFrac = snap?.DiskFraction ?? 0.45;
        string diskText = snap?.DiskPercent ?? "—";

        // CPU
        p.Label("CPU", R(sysRect.Left + 16, sysRect.Top + 32, 40, 16), 11, Theme.Text, true);
        p.Label(cpuText, R(sysRect.Right - 16 - 70, sysRect.Top + 32, 70, 16), 11, Theme.Muted, true, HAlign.Right);
        var cpuBar = R(sysRect.Left + 16, sysRect.Top + 49, sysRect.Width - 32, 5);
        p.Bar(cpuBar, cpuFrac, cpuFrac > 0.8 ? Theme.Red : Theme.Accent);

        // RAM
        p.Label("RAM", R(sysRect.Left + 16, sysRect.Top + 65, 40, 16), 11, Theme.Text, true);
        p.Label(ramText, R(sysRect.Right - 16 - 70, sysRect.Top + 65, 70, 16), 11, Theme.Muted, true, HAlign.Right);
        var ramBar = R(sysRect.Left + 16, sysRect.Top + 82, sysRect.Width - 32, 5);
        p.Bar(ramBar, ramFrac, ramFrac > 0.85 ? Theme.Orange : Theme.Mix(Theme.Accent, Theme.Green, 0.4));

        // SSD
        p.Label("SSD", R(sysRect.Left + 16, sysRect.Top + 98, 40, 16), 11, Theme.Text, true);
        p.Label(diskText, R(sysRect.Right - 16 - 70, sysRect.Top + 98, 70, 16), 11, Theme.Muted, true, HAlign.Right);
        var diskBar = R(sysRect.Left + 16, sysRect.Top + 115, sysRect.Width - 32, 5);
        p.Bar(diskBar, diskFrac, Theme.Mix(Theme.Surface3, Theme.Text, 0.5));

        // Аптайм с векторными часами
        if (snap != null && !string.IsNullOrEmpty(snap.Uptime) && snap.Uptime != "—")
        {
            p.DrawClock(new Point(sysRect.Left + 22, sysRect.Top + 140), 5.5, Theme.Muted);
            p.Label(snap.Uptime, R(sysRect.Left + 34, sysRect.Top + 133, sysRect.Width - 48, 16), 9.5, Theme.Muted);
        }

        p.Label(Loc.T("Подробные датчики  →", "System details  →", "Sistem Detayları  →"),
            R(sysRect.Left + 16, sysRect.Top + 174, sysRect.Width - 32, 14), 9.5, Theme.Mix(Theme.Muted, Theme.AccentHot, sysHot), sysHot > 0.3);
    }

    public override void Drag(IslandView isl, string key, Point pos)
    {
        if (key == "slider:volume") Sections.Sound.Drag(isl, key, pos);
        else if (key == "slider:brightness") Sections.Settings.Drag(isl, key, pos);
        else if (key == "slider:seek") Sections.Media.Drag(isl, key, pos);
    }

    public override void DragEnd(IslandView isl, string key)
    {
        if (key == "slider:volume") Sections.Sound.DragEnd(isl, key);
        else if (key == "slider:brightness") Sections.Settings.DragEnd(isl, key);
        else if (key == "slider:seek") Sections.Media.DragEnd(isl, key);
    }

    public override void Click(IslandView isl, string key)
    {
        if (key.StartsWith("go:"))
        {
            isl.Select(key[3..]);
        }
        else if (key is "home:play" or "media:play")
        {
            isl.TogglePlay();
        }
        else if (key is "home:prev" or "media:prev")
        {
            isl.PrevTrack();
        }
        else if (key is "home:next" or "media:next")
        {
            isl.NextTrack();
        }
        else if (key.StartsWith("tog:"))
        {
            string name = key[4..];
            var sett = Sections.Settings;
            switch (name)
            {
                case "WiFi":
                case "Bluetooth": sett.Toggle(isl, name); break;
                case "dark": sett.ToggleDark(isl); break;
                case "awake": isl.SetKeepAwake(!isl.S.KeepAwake); break;
                case "mute": Sections.Sound.Click(isl, "vol:mute"); break;
            }
        }
    }
}

// ------------------------------------------------------------------ Звук

public sealed class SoundSection : Section
{
    private const int MaxApps = 5, MaxDevices = 5;

    public bool Muted;
    public string? Error, DevError, AppError;
    public List<OutputDevice> Devices = new();
    public string? DefaultId;
    public List<AppVolume> Apps = new();
    public bool Dragging;
    public readonly Dictionary<string, Rect> Tracks = new();

    private bool _volBusy, _listBusy, _pushBusy, _appBusy;
    private float? _volPending;
    private (int Index, float Level)? _appPending;

    public SoundSection() { Key = "sound"; Title = "Звук"; }

    private double _nextFullListRefresh;
    private double _nextVolPoll;

    public override void Enter(IslandView isl) => Refresh(isl);

    public override void Tick(IslandView isl)
    {
        if (Dragging) return;
        if (Clock.Now >= _nextVolPoll)
        {
            _nextVolPoll = Clock.Now + 3.0; // Фоновый опрос громкости раз в 3 сек вместо 1 сек
            RefreshVolume(isl);
        }
        if (Clock.Now >= _nextFullListRefresh)
        {
            _nextFullListRefresh = Clock.Now + 6.0;
            RefreshList(isl);
        }
    }

    /// <summary>Читает громкость в фоне: COM-вызов не блокирует интерфейс.</summary>
    public void RefreshVolume(IslandView isl)
    {
        if (_volBusy) return;
        _volBusy = true;
        Bg.Run(AudioService.GetVolume, (res, err) =>
        {
            _volBusy = false;
            if (err != null)
            {
                string message = err.Message;
                bool changedErr = Error != message;
                Error = message;
                if (changedErr) isl.Kick();
                return;
            }
            var sp = isl.Nsp("volume");
            bool changed = Error != null || Muted != res.Muted || Math.Abs(sp.Target - res.Level) > 0.005;
            Error = null;
            Muted = res.Muted;
            if (changed && !Dragging)
            {
                sp.Target = res.Level;               // пружина плавно доедет до значения
                isl.Kick();
            }
        });
    }

    private sealed record ListData(List<OutputDevice>? Devices, string? DefaultId, string? DevError,
                                   List<AppVolume>? Apps, string? AppError);

    public void Refresh(IslandView isl)
    {
        RefreshVolume(isl);
        RefreshList(isl);
    }

    public void RefreshList(IslandView isl)
    {
        if (Error != null || _listBusy) return;
        _listBusy = true;
        Bg.Run(() =>
        {
            List<OutputDevice>? devs = null; string? def = null, devErr = null;
            List<AppVolume>? apps = null; string? appErr = null;
            try { (devs, def) = AudioService.ListOutputDevices(); } catch (Exception e) { devErr = e.Message; }
            try { apps = AudioService.ListAppVolumes(); } catch (Exception e) { appErr = e.Message; }
            return new ListData(devs, def, devErr, apps, appErr);
        }, (res, err) =>
        {
            _listBusy = false;
            if (res == null) return;
            DevError = res.DevError;
            if (res.Devices != null) { Devices = res.Devices; DefaultId = res.DefaultId; }
            AppError = res.AppError;
            if (res.Apps != null && !Dragging) Apps = res.Apps;
            isl.Kick();
        });
    }

    public override void Paint(IslandView isl, Painter p, Rect rect)
    {
        const double gap = 16;
        double colW = (rect.Width - gap) / 2;
        PaintMaster(isl, p, R(rect.Left, rect.Top, colW, rect.Height));
        PaintApps(isl, p, R(rect.Left + colW + gap, rect.Top, colW, rect.Height));
    }

    private void PaintMaster(IslandView isl, Painter p, Rect box)
    {
        p.Card(box, Theme.Surface, 20, Theme.BorderSubtle);
        double level = Ui.Clamp(isl.Nsp("volume").X);

        p.Label(Loc.T("ОБЩАЯ ГРОМКОСТЬ", "MASTER VOLUME", "GENEL SES"), R(box.Left + 20, box.Top + 14, 150, 16), 10, Theme.Muted, true);
        string value = Muted ? Loc.T("Без звука", "Muted", "Sessiz") : $"{Math.Round(level * 100)}%";
        p.Label(value, R(box.Right - 20 - 100, box.Top + 12, 100, 18), 12, Muted ? Theme.Red : Theme.Text, true, HAlign.Right);

        // Apple Slider for master volume
        var track = R(box.Left + 18, box.Top + 34, box.Width - 36, 42);
        Tracks["slider:volume"] = track;
        p.AppleSlider("slider:volume", track, level, Muted ? "🔇" : "🔊", Error == null);

        // Mute button
        var muteR = R(box.Left + 18, box.Top + 84, 110, 26);
        p.Card(muteR, Muted ? Theme.Mix(Theme.Surface2, Theme.Red, 0.35) : Theme.Surface2, 13, Theme.BorderSubtle);
        p.Label(Muted ? Loc.T("🔇 Звук выкл", "🔇 Muted", "🔇 Sessiz") : Loc.T("🔊 Звук вкл", "🔊 Audio On", "🔊 Ses Açık"), muteR, 10, Muted ? Theme.Red : Theme.Text, true, HAlign.Center);
        isl.AddHit("vol:mute", muteR);

        if (Error != null)
        {
            p.Note(R(box.Left + 20, box.Top + 120, box.Width - 40, 80), Error, Theme.Red, 11);
            return;
        }

        // Output Devices Header
        p.Label(Loc.T("УСТРОЙСТВА ВЫВОДА ЗВУКА", "OUTPUT DEVICES", "ÇIKIŞ AYGITLARI"), R(box.Left + 20, box.Top + 124, 300, 16), 10, Theme.Muted, true);
        if (DevError != null)
        {
            p.Note(R(box.Left + 20, box.Top + 148, box.Width - 40, 60), DevError, Theme.Red, 11);
            return;
        }

        for (int i = 0; i < Math.Min(Devices.Count, MaxDevices); i++)
        {
            var dev = Devices[i];
            var r = R(box.Left + 16, box.Top + 148 + i * 44, box.Width - 32, 38);
            bool selected = dev.Id == DefaultId;
            double hot = isl.Hv($"dev:{i}");

            var devBg = selected
                ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.22)
                : Theme.Mix(Theme.Surface2, Theme.Surface3, hot * 0.4);
            var devBorder = selected ? Theme.Accent : Theme.BorderSubtle;

            p.Card(r, devBg, 12, devBorder);

            // Icon (headphone or speaker)
            string icon = dev.Name.ToLowerInvariant().Contains("head") || dev.Name.ToLowerInvariant().Contains("науш") ? "🎧" : "🔊";
            p.Label(icon, R(r.Left + 12, r.Top + (r.Height - 18) / 2, 20, 18), 12, selected ? Theme.AccentHot : Theme.Muted, true, HAlign.Center);

            // Name
            p.Label(dev.Name, R(r.Left + 38, r.Top, r.Width - 76, r.Height), 11.5, selected ? Theme.Text : Theme.Mix(Theme.Muted, Theme.Text, 0.4 + 0.6 * hot), selected);

            // Checkmark indicator
            if (selected)
            {
                p.Circle(new Point(r.Right - 20, r.Top + r.Height / 2), 6, Theme.Accent);
                p.Label("✓", R(r.Right - 26, r.Top + (r.Height - 16) / 2, 12, 16), 10, Colors.White, true, HAlign.Center);
            }

            isl.AddHit($"dev:{i}", r);
        }
    }

    private void PaintApps(IslandView isl, Painter p, Rect box)
    {
        p.Card(box, Theme.Surface, 20, Theme.BorderSubtle);
        p.Label(Loc.T("МИКШЕР ПРИЛОЖЕНИЙ", "APPLICATION MIXER", "UYGULAMA KARIŞTIRICISI"), R(box.Left + 20, box.Top + 14, 200, 16), 10, Theme.Muted, true);

        if (AppError != null)
        {
            p.Note(R(box.Left + 20, box.Top + 48, box.Width - 40, 80), AppError, Theme.Red, 11);
            return;
        }
        if (Apps.Count == 0)
        {
            p.Label(Loc.T("Нет активных приложений со звуком", "No apps currently playing audio", "Aktif ses çalan uygulama yok"), R(box.Left + 20, box.Top + 80, box.Width - 40, 24), 13, Theme.Muted, false, HAlign.Center);
            p.Label(Loc.T("Запустите музыку или видео в браузере", "Play audio in browser, game or player", "Tarayıcı veya oynatıcıda müzik çalın"), R(box.Left + 20, box.Top + 106, box.Width - 40, 20), 11, Theme.Muted, false, HAlign.Center);
            return;
        }

        for (int i = 0; i < Math.Min(Apps.Count, MaxApps); i++)
        {
            var app = Apps[i];
            double y = box.Top + 40 + i * 62;
            var appRowRect = R(box.Left + 16, y, box.Width - 32, 56);
            p.Card(appRowRect, Theme.Mix(Theme.Surface2, Theme.Surface3, 0.25), 14, Theme.BorderSubtle);

            // App name and volume percent
            p.Label($"🎵  {app.Name}", R(appRowRect.Left + 14, y + 6, appRowRect.Width - 80, 18), 12, Theme.Text, true);
            p.Label($"{Math.Round(app.Level * 100)}%", R(appRowRect.Right - 14 - 50, y + 6, 50, 18), 11, Theme.AccentHot, true, HAlign.Right);

            // Slider
            string key = $"slider:app:{i}";
            var track = R(appRowRect.Left + 14, y + 30, appRowRect.Width - 28, 16);
            Tracks[key] = track;
            p.Slider(key, track, app.Level);
        }

        if (Apps.Count > MaxApps)
            p.Label(Loc.T($"Показаны первые {MaxApps} из {Apps.Count}", $"Showing top {MaxApps} of {Apps.Count}", $"{Apps.Count} uygulamadan ilk {MaxApps} gösteriliyor"), R(box.Left + 20, box.Bottom - 26, box.Width - 40, 16), 10, Theme.Muted);
    }

    public override void Drag(IslandView isl, string key, Point pos)
    {
        if (!Tracks.TryGetValue(key, out var track) || track.Width <= 0) return;
        Dragging = true;
        double f = Ui.Clamp((pos.X - track.Left) / track.Width);
        if (key == "slider:volume")
        {
            var sp = isl.Nsp("volume");
            sp.X = sp.Target = f;                 // без пружины: ползунок идёт точно за мышью
            sp.V = 0.0;
            _volPending = (float)f;
            PushVolume(isl);
            Muted = false;
        }
        else if (key.StartsWith("slider:app:"))
        {
            if (!int.TryParse(key[(key.LastIndexOf(':') + 1)..], out int idx) || idx < 0 || idx >= Apps.Count) return;
            Apps[idx].Level = (float)f;
            _appPending = (idx, (float)f);
            PushApp(isl);
        }
        isl.Kick();
    }

    private void PushVolume(IslandView isl)
    {
        if (_pushBusy || _volPending == null) return;
        float value = _volPending.Value;
        _volPending = null;
        _pushBusy = true;
        bool unmute = Muted;
        Bg.Run(() =>
        {
            if (unmute) AudioService.SetMute(false);
            AudioService.SetVolume(value);
            return true;
        }, (_, err) =>
        {
            _pushBusy = false;
            if (err != null)
            {
                if (Error == null) isl.Alert($"Не удалось изменить громкость:\n{err.Message}");
                Error = err.Message;
            }
            else
            {
                Error = null;
            }
            if (_volPending != null) PushVolume(isl);
            isl.Kick();
        });
    }

    private void PushApp(IslandView isl)
    {
        if (_appBusy || _appPending == null) return;
        var (idx, level) = _appPending.Value;
        _appPending = null;
        if (idx >= Apps.Count) return;
        var ids = Apps[idx].ProcessIds.ToList();
        _appBusy = true;
        Bg.Run(() => { AudioService.SetAppVolume(ids, level); return true; }, (_, err) =>
        {
            _appBusy = false;
            AppError = err?.Message;                     // сессия могла закрыться
            if (_appPending != null) PushApp(isl);
            isl.Kick();
        });
    }

    public override void DragEnd(IslandView isl, string key) => Dragging = false;

    public override void Click(IslandView isl, string key)
    {
        if (key == "vol:mute")
        {
            bool want = !Muted;
            Muted = want;                                // сразу показываем, при ошибке вернём
            Bg.Run(() => { AudioService.SetMute(want); return true; }, (_, err) =>
            {
                if (err != null)
                {
                    Muted = !want;
                    isl.Alert($"Не удалось переключить звук:\n{err.Message}");
                }
                isl.Kick();
            });
        }
        else if (key.StartsWith("dev:"))
        {
            if (!int.TryParse(key[4..], out int idx) || idx < 0 || idx >= Devices.Count) return;
            var device = Devices[idx];
            if (device.Id == DefaultId) return;
            Bg.Run(() => { AudioService.SetDefaultDevice(device.Id); return true; }, (_, err) =>
            {
                if (err != null) isl.Alert($"Не удалось переключить устройство:\n{err.Message}");
                else DefaultId = device.Id;
                Refresh(isl);
            });
        }
        isl.Kick();
    }
}

// ------------------------------------------------------------------ Медиа

public sealed class MediaSection : Section
{
    private Rect _track;
    private bool _dragging;
    private double _seekFrac;

    public Rect Track { get => _track; set => _track = value; }
    public bool Dragging => _dragging;
    public double SeekFrac => _seekFrac;

    public MediaSection() { Key = "media"; Title = "Медиа"; }

    public override void Enter(IslandView isl) => isl.RefreshMedia();

    public override void Paint(IslandView isl, Painter p, Rect rect)
    {
        var box = R(rect.Left, rect.Top, rect.Width, rect.Height);
        p.Card(box, Theme.Surface, 20, Theme.BorderSubtle);
        var m = isl.Media;

        p.Cover(R(box.Left + 28, box.Top + 28, 150, 150), 22);

        double x0 = box.Left + 206;
        double wcol = box.Width - 206 - 28;
        p.Label(string.IsNullOrEmpty(m.Title) ? Loc.T("Ничего не играет", "Not Playing", "Çalınmıyor") : m.Title, R(x0, box.Top + 28, wcol, 32), 19, Theme.Text, true);
        string sub = !string.IsNullOrEmpty(m.Title) ? m.Artist : Loc.T("Запустите плеер", "Start a media player", "Oynatıcı başlatın");
        p.Label(sub, R(x0, box.Top + 62, wcol, 22), 13, Theme.Muted);

        if (!string.IsNullOrEmpty(m.App))
        {
            double pw = Math.Min(p.TextWidth(m.App, 10, true) + 26, wcol);
            var pill = R(x0, box.Top + 96, pw, 24);
            p.Card(pill, Theme.Surface2, 12);
            p.Label(m.App, pill, 10, Theme.Muted, true, HAlign.Center);
        }

        // перемотка: всегда отображаем полноценный ползунок с бегунком и таймлайном
        double dur = m.Dur > 0 ? m.Dur : 210.0;
        double pos = isl.MediaPos();
        double frac = _dragging ? _seekFrac : Ui.Clamp(dur > 0 ? pos / dur : 0.0);
        var track = R(x0, box.Top + 146, wcol, 8);
        _track = track;
        p.Slider("slider:seek", track, frac, true);

        double shown = _dragging ? frac * dur : pos;
        p.Label(!string.IsNullOrEmpty(m.Title) ? Ui.FmtTime(shown) : "", R(x0, box.Top + 160, 80, 18), 10, Theme.Muted);
        p.Label(Ui.FmtTime(dur), R(x0 + wcol - 80, box.Top + 160, 80, 18), 10, Theme.Muted, false, HAlign.Right);

        // кнопки управления и быстрой перемотки ±10 сек
        double cx = box.Left + box.Width / 2, cy = box.Top + 214;
        double v = Ui.Clamp(isl.Nsp("play").X);

        var prevC = new Point(cx - 100, cy);
        using (p.RoundButton("media:prev", prevC, 22)) p.MediaIcon("prev", prevC, 9.5);

        var rewC = new Point(cx - 50, cy);
        using (p.RoundButton("media:rew10", rewC, 19))
            p.Label("-10", R(rewC.X - 16, rewC.Y - 10, 32, 20), 10, Theme.Text, true, HAlign.Center);

        var mid = new Point(cx, cy);
        using (p.RoundButton("media:toggle", mid, 28, true))
        {
            p.MediaIcon("play", mid, 12, 1 - v);
            p.MediaIcon("pause", mid, 12, v);
        }

        var fwdC = new Point(cx + 50, cy);
        using (p.RoundButton("media:fwd10", fwdC, 19))
            p.Label("+10", R(fwdC.X - 16, fwdC.Y - 10, 32, 20), 10, Theme.Text, true, HAlign.Center);

        var nextC = new Point(cx + 100, cy);
        using (p.RoundButton("media:next", nextC, 22)) p.MediaIcon("next", nextC, 9.5);

        string hint = Loc.T("Интерактивное управление воспроизведением и системным медиаплеером.", "Interactive playback and media player controls.", "İnteraktif oynatma ve medya denetimleri.");
        p.Note(R(rect.Left + 6, box.Bottom + 10, rect.Width - 12, 20), hint);
    }

    public override void Drag(IslandView isl, string key, Point pos)
    {
        if (key != "slider:seek" || _track.Width <= 0) return;
        _dragging = true;
        _seekFrac = Ui.Clamp((pos.X - _track.Left) / _track.Width);
        isl.Kick();
    }

    public override void DragEnd(IslandView isl, string key)
    {
        if (key != "slider:seek") return;
        _dragging = false;
        if (isl.Media.Dur <= 0) return;
        double target = Math.Clamp(_seekFrac * isl.Media.Dur, 0.0, isl.Media.Dur);
        isl.Media.Pos = target;                      // сразу показываем новую позицию
        isl.Media.Stamp = DateTime.UtcNow;
        Bg.RunAsync(async () => { await MediaService.SeekAsync(target); return true; }, (_, _) =>
        {
            isl.RefreshMedia(500);
        });
        isl.Kick();
    }

    public override void Click(IslandView isl, string key)
    {
        string action = key[(key.IndexOf(':') + 1)..];
        switch (action)
        {
            case "prev": isl.PrevTrack(); break;
            case "next": isl.NextTrack(); break;
            case "toggle": isl.TogglePlay(); break;
            case "rew10":
            case "fwd10":
                double shift = action == "fwd10" ? 10.0 : -10.0;
                double newPos = Math.Clamp(isl.MediaPos() + shift, 0.0, isl.Media.Dur > 0 ? isl.Media.Dur : 3600);
                isl.Media.Pos = newPos;
                isl.Media.Stamp = DateTime.UtcNow;
                Bg.RunAsync(async () => { await MediaService.SeekAsync(newPos); return true; }, (_, _) => isl.RefreshMedia(400));
                break;
        }
        isl.RefreshMedia(900);
    }
}

// ------------------------------------------------------------------ Система

public sealed class SystemSection : Section
{
    public SystemSection() { Key = "system"; Title = "Система"; }

    public override void Enter(IslandView isl) => isl.RefreshSnapshot();
    public override void Tick(IslandView isl) => isl.RefreshSnapshot();

    public override void Paint(IslandView isl, Painter p, Rect rect)
    {
        var snap = isl.Snapshot;
        if (snap == null)
        {
            var emptyBox = rect;
            p.Card(emptyBox, Theme.Surface, 24);
            p.Label(Loc.T("Собираю системные данные…", "Gathering system info…", "Sistem bilgileri toplanıyor…"), R(emptyBox.Left + 28, emptyBox.Top + emptyBox.Height / 2 - 12, emptyBox.Width - 56, 24), 13, Theme.Muted, false, HAlign.Center);
            return;
        }

        const double gap = 12;
        double wLeft = 320;
        double wRight = Math.Max(260, rect.Width - wLeft - gap);

        // ==================== ЛЕВАЯ КОЛОНКА: РЕСУРСЫ СИСТЕМЫ ====================
        var leftBox = R(rect.Left, rect.Top, wLeft, rect.Height);
        p.Card(leftBox, Theme.Surface, 20, Theme.BorderSubtle);
        p.Label(Loc.T("РЕСУРСЫ СИСТЕМЫ", "SYSTEM RESOURCES", "SİSTEM KAYNAKLARI"), R(leftBox.Left + 20, leftBox.Top + 14, wLeft - 40, 16), 10, Theme.Muted, true);

        var rows = new List<(string IconName, string Value, double Frac, Color BarColor)>
        {
            (Loc.T("⚡  ЦП", "⚡  CPU", "⚡  İşlemci"), snap.Cpu, snap.CpuFraction, Theme.Accent),
            (Loc.T("🧠  Память", "🧠  Memory", "🧠  Bellek"), $"{snap.Memory} ({snap.MemoryPercent})", snap.MemoryFraction, Theme.AccentHot),
            (Loc.T("💾  Диск C:", "💾  Disk C:", "💾  Disk C:"), $"{snap.Disk} ({snap.DiskPercent})", snap.DiskFraction, Theme.C("#38BDF8")),
            ($"🌐  {snap.NetworkName}", snap.NetworkSpeed, snap.NetworkFraction, Theme.C("#10B981")),
        };

        double startY = leftBox.Top + 38;
        double stepY = (leftBox.Height - 48) / rows.Count;
        for (int i = 0; i < rows.Count; i++)
        {
            var (name, value, frac, barCol) = rows[i];
            double y = startY + i * stepY;
            p.Label(name, R(leftBox.Left + 20, y, 140, 18), 12, Theme.Text, true);
            p.Label(value, R(leftBox.Right - 20 - 150, y, 150, 18), 11.5, Theme.Muted, false, HAlign.Right);
            p.Bar(R(leftBox.Left + 20, y + 22, leftBox.Width - 40, 5), frac, barCol);
        }

        // ==================== ПРАВАЯ КОЛОНКА ====================
        double rightX = rect.Left + wLeft + gap;

        // 1. Время работы ПК (Uptime)
        var upBox = R(rightX, rect.Top, wRight, 78);
        p.Card(upBox, Theme.Surface, 20, Theme.BorderSubtle);
        p.Label(Loc.T("⏱️  ВРЕМЯ РАБОТЫ (UPTIME)", "⏱️  SYSTEM UPTIME", "⏱️  ÇALIŞMA SÜRESİ"), R(upBox.Left + 18, upBox.Top + 14, wRight - 36, 16), 9.5, Theme.Muted, true);
        p.Label(snap.Uptime, R(upBox.Left + 18, upBox.Top + 34, wRight - 36, 26), 16, Theme.Text, true);

        // 2. Топ ресурсоёмких процессов
        double procBoxY = rect.Top + 90;
        var procBox = R(rightX, procBoxY, wRight, rect.Height - 90);
        p.Card(procBox, Theme.Surface, 20, Theme.BorderSubtle);
        p.Label(Loc.T("📊  ТОП ПРОЦЕССОВ (RAM)", "📊  TOP PROCESSES (RAM)", "📊  EN ÇOK RAM KULLANANLAR"), R(procBox.Left + 18, procBox.Top + 14, wRight - 36, 16), 9.5, Theme.Muted, true);

        if (snap.TopProcesses.Count > 0)
        {
            double pStartY = procBox.Top + 36;
            double pRowH = (procBox.Height - 44) / Math.Max(1, Math.Min(4, snap.TopProcesses.Count));
            for (int i = 0; i < Math.Min(4, snap.TopProcesses.Count); i++)
            {
                var pr = snap.TopProcesses[i];
                double py = pStartY + i * pRowH;

                // Индикатор-точка
                p.Circle(new Point(procBox.Left + 22, py + 10), 3.5, Theme.Accent);

                // Название процесса
                p.Label(pr.Name, R(procBox.Left + 32, py, wRight - 110, 20), 11.5, Theme.Text, true);

                // Бейдж объема памяти
                double badgeW = p.TextWidth(pr.Memory, 10, true) + 12;
                var badgeR = R(procBox.Right - 18 - badgeW, py + 1, badgeW, 18);
                p.Card(badgeR, Theme.Surface2, 6, Theme.BorderSubtle);
                p.Label(pr.Memory, badgeR, 9.5, Theme.AccentHot, true, HAlign.Center);
            }
        }
        else
        {
            p.Label(Loc.T("Сканирование процессов…", "Scanning processes…", "İşlemler taranıyor…"), R(procBox.Left + 18, procBox.Top + 60, wRight - 36, 20), 11, Theme.Muted);
        }
    }
}


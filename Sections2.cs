using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickNotchCs;

// ------------------------------------------------------------------ Настройки

public sealed class SettingsSection : Section
{
    private const double DimMin = 0.2, DimMax = 0.9;
    private const double DelayMin = 0.5, DelayMax = 8.0;

    public readonly Dictionary<string, Rect> Tracks = new();
    public bool Dragging;
    public readonly Dictionary<string, bool?> States = new() { ["WiFi"] = null, ["Bluetooth"] = null };
    public bool Busy;
    public string? Error;
    private double _nextRefresh;
    public bool BrKnown, BrBusy;
    private int? _brPending;
    public string? BrError;
    private double _brNext;
    public bool Dark;
    public string SubTab = "appearance";

    public SettingsSection() { Key = "settings"; Title = "Настройки"; }

    public override void Attach(IslandView isl)
    {
        Dark = SystemService.GetDarkTheme();
        var sp = isl.Nsp("sys:dark");
        sp.X = sp.Target = Dark ? 1.0 : 0.0;
    }

    public override void Enter(IslandView isl)
    {
        Dark = SystemService.GetDarkTheme();
        isl.Nsp("sys:dark").Target = Dark ? 1.0 : 0.0;
        Refresh(isl);
        RefreshBrightness(isl);
    }

    public override void Tick(IslandView isl)
    {
        if (!Busy && Clock.Now >= _nextRefresh) Refresh(isl);
        if (Clock.Now >= _brNext) RefreshBrightness(isl);
    }

    // ---------- Wi-Fi и Bluetooth ----------

    public void Refresh(IslandView isl, (string Kind, bool On)? toggle = null)
    {
        if (Busy) return;
        Busy = true;
        _nextRefresh = Clock.Now + 30;
        Bg.RunAsync(async () =>
        {
            if (toggle != null) await DeviceService.SetRadioAsync(toggle.Value.Kind, toggle.Value.On);
            return await DeviceService.ReadRadiosAsync();
        }, (res, err) =>
        {
            Busy = false;
            if (err != null)
            {
                Error = err.Message;
                if (toggle != null)
                {
                    States[toggle.Value.Kind] = !toggle.Value.On;
                    isl.Alert($"Не удалось переключить {toggle.Value.Kind}:\n{err.Message}");
                }
            }
            else
            {
                Error = null;
                foreach (var kv in res!) States[kv.Key] = kv.Value;
            }
            foreach (var kv in States) isl.Nsp($"radio:{kv.Key}").Target = kv.Value == true ? 1.0 : 0.0;
            isl.Kick();
        });
        isl.Kick();
    }

    public void Toggle(IslandView isl, string kind)
    {
        if (Busy) return;
        bool? state = States[kind];
        if (state == null)
        {
            Refresh(isl);
            return;
        }
        States[kind] = !state.Value;
        isl.Nsp($"radio:{kind}").Target = state.Value ? 0.0 : 1.0;
        isl.Kick();
        Refresh(isl, (kind, !state.Value));
    }

    // ---------- яркость ----------

    public void RefreshBrightness(IslandView isl)
    {
        if (BrBusy || Dragging) return;
        BrBusy = true;
        _brNext = Clock.Now + 90;
        Bg.Run(DeviceService.GetBrightness, (res, err) =>
        {
            BrBusy = false;
            if (err != null)
            {
                BrError = err.Message;
                _brNext = Clock.Now + 300;
            }
            else
            {
                BrError = null;
                BrKnown = true;
                if (!Dragging) isl.Nsp("brightness").Target = res / 100.0;
            }
            if (_brPending != null) PushBrightness(isl);
            isl.Kick();
        });
    }

    private void PushBrightness(IslandView isl)
    {
        if (BrBusy || _brPending == null) return;
        int value = _brPending.Value;
        _brPending = null;
        BrBusy = true;
        Bg.Run(() => { DeviceService.SetBrightness(value); return true; }, (_, err) =>
        {
            BrBusy = false;
            if (err != null)
            {
                if (BrError == null) isl.Alert($"Не удалось изменить яркость:\n{err.Message}");
                BrError = err.Message;
            }
            else
            {
                BrError = null;
            }
            if (_brPending != null) PushBrightness(isl);
            isl.Kick();
        });
    }

    public void ToggleDark(IslandView isl)
    {
        bool want = !Dark;
        try { SystemService.SetDarkTheme(want); }
        catch (Exception e)
        {
            isl.Alert($"Не удалось сменить тему:\n{e.Message}");
            return;
        }
        Dark = want;
        isl.Nsp("sys:dark").Target = want ? 1.0 : 0.0;
        Theme.UpdateSystemTheme(want);
        Bg.Run(() => { WinApi.BroadcastThemeChange(); return true; }, (_, _) => { });
        isl.Kick();
    }

    // ---------- отрисовка ----------

    public override void Paint(IslandView isl, Painter p, Rect body)
    {
        // 0. Сегментированный переключатель подразделов в стиле macOS Settings
        var switchBox = R(body.Left, body.Top, body.Width, 36);
        p.Card(switchBox, Theme.Mix(Theme.Surface, Theme.Surface2, 0.4), 18, Theme.BorderSubtle);

        double tabW = (switchBox.Width - 12) / 3;
        var tApp = R(switchBox.Left + 2, switchBox.Top + 3, tabW, switchBox.Height - 6);
        var tBeh = R(switchBox.Left + 2 + tabW + 4, switchBox.Top + 3, tabW, switchBox.Height - 6);
        var tSys = R(switchBox.Left + 2 + 2 * (tabW + 4), switchBox.Top + 3, tabW, switchBox.Height - 6);

        bool isApp = SubTab == "appearance";
        bool isBeh = SubTab == "behavior";
        bool isSys = SubTab == "system";

        // Таб 1: Оформление
        Color bgApp = isApp ? Theme.Accent : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("settab:appearance") * 0.4);
        Color fgApp = isApp ? Colors.White : Theme.Mix(Theme.Muted, Theme.Text, 0.4 + 0.6 * isl.Hv("settab:appearance"));
        p.Card(tApp, bgApp, 15);
        p.Label(Loc.T("🎨 Оформление", "🎨 Appearance", "🎨 Görünüm"), tApp, 11, fgApp, true, HAlign.Center);
        isl.AddHit("settab:appearance", tApp);

        // Таб 2: Поведение
        Color bgBeh = isBeh ? Theme.Accent : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("settab:behavior") * 0.4);
        Color fgBeh = isBeh ? Colors.White : Theme.Mix(Theme.Muted, Theme.Text, 0.4 + 0.6 * isl.Hv("settab:behavior"));
        p.Card(tBeh, bgBeh, 15);
        p.Label(Loc.T("⚡ Поведение", "⚡ Behavior", "⚡ Davranış"), tBeh, 11, fgBeh, true, HAlign.Center);
        isl.AddHit("settab:behavior", tBeh);

        // Таб 3: Система
        Color bgSys = isSys ? Theme.Accent : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("settab:system") * 0.4);
        Color fgSys = isSys ? Colors.White : Theme.Mix(Theme.Muted, Theme.Text, 0.4 + 0.6 * isl.Hv("settab:system"));
        p.Card(tSys, bgSys, 15);
        p.Label(Loc.T("⚙️ Система", "⚙️ System", "⚙️ Sistem"), tSys, 11, fgSys, true, HAlign.Center);
        isl.AddHit("settab:system", tSys);

        double contentY = body.Top + 48;
        double cw = (body.Width - 12) / 2;

        if (isApp)
        {
            // === РАЗДЕЛ 1: ОФОРМЛЕНИЕ ===

            // 1. Тема оформления острова (слева в Row 1)
            var themeBox = R(body.Left, contentY, cw, 86);
            p.Card(themeBox, Theme.Surface, 20);
            p.Label(Loc.T("Тема острова", "Island Theme", "Ada Teması"), R(themeBox.Left + 16, themeBox.Top + 11, cw - 32, 18), 12, Theme.Text, true);
            p.Label(Loc.T("Светлый, тёмный или авто-режим", "Light, dark or match Windows", "Açık, koyu veya Windows ile eşleş"), R(themeBox.Left + 16, themeBox.Top + 29, cw - 32, 16), 9.5, Theme.Muted);

            string curTheme = isl.S.ThemeMode;
            var themeModes = new (string Key, string Label)[]
            {
                ("dark", Loc.T("🌙 Тёмная", "🌙 Dark", "🌙 Koyu")),
                ("light", Loc.T("☀️ Светлая", "☀️ Light", "☀️ Açık")),
                ("auto", Loc.T("⚡ Авто", "⚡ Auto", "⚡ Otomatik"))
            };
            double tBtnGap = 6;
            double tBtnW = (cw - 32 - (themeModes.Length - 1) * tBtnGap) / themeModes.Length;
            double tBtnH = 28;
            double tBtnY = themeBox.Top + 47;
            for (int i = 0; i < themeModes.Length; i++)
            {
                var (mKey, mLabel) = themeModes[i];
                var mRect = R(themeBox.Left + 16 + i * (tBtnW + tBtnGap), tBtnY, tBtnW, tBtnH);
                bool sel = curTheme == mKey;
                p.Card(mRect, sel ? Theme.Accent : Theme.Surface2, 14);
                p.Label(mLabel, mRect, 10, sel ? Colors.White : Theme.Muted, true, HAlign.Center);
                isl.AddHit($"theme:{mKey}", mRect);
            }

            // 2. Форм-фактор острова (справа в Row 1)
            var styleBox = R(body.Left + cw + 12, contentY, cw, 86);
            p.Card(styleBox, Theme.Surface, 20);
            p.Label(Loc.T("Форм-фактор острова", "Island Form Factor", "Ada Biçimi"), R(styleBox.Left + 16, styleBox.Top + 11, cw - 32, 18), 12, Theme.Text, true);
            p.Label(Loc.T("Стиль в свёрнутом виде", "Collapsed display style", "Daraltılmış görünüm stili"), R(styleBox.Left + 16, styleBox.Top + 29, cw - 32, 16), 9.5, Theme.Muted);

            string curStyle = isl.S.IslandStyle;
            var styleModes = new (string Key, string Label)[]
            {
                ("island", Loc.T("🏝️ Остров", "🏝️ Island", "🏝️ Ada")),
                ("notch", Loc.T("💻 Чёлка", "💻 Notch", "💻 Çentik")),
                ("compact", Loc.T("✨ Компакт", "✨ Compact", "✨ Kompakt"))
            };
            double sBtnGap = 6;
            double sBtnW = (cw - 32 - (styleModes.Length - 1) * sBtnGap) / styleModes.Length;
            double sBtnH = 28;
            double sBtnY = styleBox.Top + 47;
            for (int i = 0; i < styleModes.Length; i++)
            {
                var (sKey, sLabel) = styleModes[i];
                var sRect = R(styleBox.Left + 16 + i * (sBtnW + sBtnGap), sBtnY, sBtnW, sBtnH);
                bool sel = curStyle == sKey;
                p.Card(sRect, sel ? Theme.Accent : Theme.Surface2, 14);
                p.Label(sLabel, sRect, 10, sel ? Colors.White : Theme.Muted, true, HAlign.Center);
                isl.AddHit($"style:{sKey}", sRect);
            }

            // 3. Акцентный цвет (слева в Row 2)
            double row2Y = contentY + 98;
            var accentBox = R(body.Left, row2Y, cw, 104);
            p.Card(accentBox, Theme.Surface, 20);
            p.Label(Loc.T("Акцентный цвет", "Accent Color", "Vurgu Rengi"), R(accentBox.Left + 16, accentBox.Top + 10, cw - 32, 18), 12, Theme.Text, true);
            p.Label(Loc.T("14 цветов подсветки и кнопок", "14 accent tints for interface", "14 vurgulu arayüz rengi"), R(accentBox.Left + 16, accentBox.Top + 28, cw - 32, 16), 9.5, Theme.Muted);

            int cols = 7;
            const double r = 10, gap = 8;
            double totalAccW = cols * 2 * r + (cols - 1) * gap;
            double x0 = accentBox.Left + 16 + (accentBox.Width - 32 - totalAccW) / 2;
            double cy1 = accentBox.Top + 56;
            double cy2 = accentBox.Top + 84;

            for (int i = 0; i < Theme.Accents.Length; i++)
            {
                var (key, main, _) = Theme.Accents[i];
                int col = i % cols;
                int row = i / cols;
                double cx = x0 + r + col * (2 * r + gap);
                double cy = row == 0 ? cy1 : cy2;
                string k = $"accent:{key}";
                var rect = R(cx - r - 4, cy - r - 4, 2 * r + 8, 2 * r + 8);
                using (p.Pressable(k, rect))
                {
                    if (Theme.CurrentAccent == key)
                        p.Dc.DrawEllipse(null, p.P(Theme.Text, 2), new Point(cx, cy), r + 3.5, r + 3.5);
                    double rr = r * (1 + 0.12 * isl.Hv(k));
                    p.Circle(new Point(cx, cy), rr, Theme.C(main));
                }
                isl.AddHit(k, rect);
            }

            // 4. Эффект стекла (справа в Row 2)
            var glassBox = R(body.Left + cw + 12, row2Y, cw, 104);
            using (p.Panel("opt:glass", glassBox, 20))
            {
                p.Label(Loc.T("Эффект стекла", "Glass Effect", "Cam Efekti"), R(glassBox.Left + 16, glassBox.Top + 14, cw - 70, 20), 12, Theme.Text, true);
                p.Label(Loc.T("Матовое стекло Liquid Glass", "Liquid Glass frosted blur", "Buzlu Liquid Glass"), R(glassBox.Left + 16, glassBox.Top + 38, cw - 70, 16), 10, Theme.Muted);
                p.Label(Loc.T("Плотность 95% (Space Black)", "95% opacity (Space Black)", "%95 opaklık"), R(glassBox.Left + 16, glassBox.Top + 60, cw - 70, 16), 9.5, Theme.Muted);
                p.Switch(R(glassBox.Right - 16 - 40, glassBox.Top + (glassBox.Height - 24) / 2, 40, 24), isl.Nsp("opt:glass", isl.S.GlassEnabled ? 1.0 : 0.0).X);
            }

            // 5. Робот-компаньон (Row 3)
            double row3Y = row2Y + 114;
            var botBox = R(body.Left, row3Y, body.Width, 64);
            using (p.Panel("opt:robot_companion", botBox, 20))
            {
                p.Label(Loc.T("🤖 Робот-компаньон Aero", "🤖 Robot Companion Aero", "🤖 Robot Arkadaşı"), R(botBox.Left + 16, botBox.Top + 12, body.Width - 260, 20), 12.5, Theme.Text, true);
                p.Label(Loc.T("Живой питомец: следит взглядом за курсором, моргает и может летать по экрану как Mochi",
                              "Living companion: tracks cursor, blinks, and can roam the desktop like Mochi",
                              "Yaşayan arkadaş: fareyi gözleriyle izler ve Mochi gibi masaüstünde gezebilir"),
                    R(botBox.Left + 16, botBox.Top + 34, body.Width - 260, 16), 10, Theme.Muted);

                if (isl.S.RobotCompanion)
                {
                    string roamLabel = isl.S.RobotRoaming ? ("🏝️ " + Loc.T("В нотч", "To Notch", "Adaya")) : ("🚀 " + Loc.T("На рабочий стол", "To Desktop", "Masaüstüne"));
                    double roamW = p.TextWidth(roamLabel, 9.5, true) + 20;
                    var roamBtn = R(botBox.Right - 16 - 40 - 12 - roamW, botBox.Top + (botBox.Height - 26) / 2, roamW, 26);
                    double roamHv = isl.Hv("opt:toggle_roam");
                    p.Card(roamBtn, Theme.Mix(Theme.Surface2, Theme.Accent, 0.2 + 0.3 * roamHv), 13,
                        isl.S.RobotRoaming ? Theme.AccentHot : Theme.BorderSubtle);
                    p.Label(roamLabel, roamBtn, 9.5, isl.S.RobotRoaming ? Theme.AccentHot : Theme.Text, true, HAlign.Center);
                    isl.AddHit("opt:toggle_roam", roamBtn);
                }

                p.Switch(R(botBox.Right - 16 - 40, botBox.Top + (botBox.Height - 24) / 2, 40, 24), isl.Nsp("opt:robot_companion", isl.S.RobotCompanion ? 1.0 : 0.0).X);
            }
        }
        else if (isBeh)
        {
            // === РАЗДЕЛ 2: ПОВЕДЕНИЕ И ЖЕСТЫ ===

            // 1. Способ вызова поверх окон: Полоска Notch
            var summonBox = R(body.Left, contentY, body.Width, 64);
            p.Card(summonBox, Theme.Surface, 20);
            p.Label(Loc.T("Вызов поверх окон: Полоска Notch", "Summon over Windows: Notch Bar", "Pencerelerin Üzerinde Çağır: Notch"), R(summonBox.Left + 20, summonBox.Top + 12, body.Width - 190, 18), 12.5, Theme.Text, true);
            p.Label(Loc.T("Кликните по аккуратной полоске у верхнего края экрана, чтобы развернуть остров поверх любых окон и игр",
                          "Click the sleek top-edge notch bar to summon the island over full-screen apps and games",
                          "Adayı tam ekran pencerelerin üzerinde açmak için ekranın üstündeki şık çubuğa tıklayın"),
                R(summonBox.Left + 20, summonBox.Top + 34, body.Width - 190, 16), 9.5, Theme.Muted);

            var badgeRect = R(summonBox.Right - 160, summonBox.Top + 18, 140, 28);
            p.Card(badgeRect, Theme.Mix(Theme.Accent, Theme.Surface2, 0.2), 14);
            p.Label(Loc.T("🎯 Полоска Notch", "🎯 Notch Bar", "🎯 Notch Çubuğu"), badgeRect, 10, Theme.Accent, true, HAlign.Center);

            // 2. Сворачивать при уходе мыши и Прятать в полноэкранных окнах
            double row2Y = contentY + 74;
            double delay = isl.S.LeaveDelay;
            ToggleSliderCard(isl, p, R(body.Left, row2Y, cw, 86), Loc.T("Сворачивать при уходе мыши", "Auto-collapse on mouse leave", "Fare ayrılınca daralt"),
                "opt:leave_collapse", "slider:delay", isl.S.LeaveCollapse, Loc.T($"через {delay:F1} с", $"after {delay:F1}s", $"{delay:F1} sn sonra"),
                (delay - DelayMin) / (DelayMax - DelayMin));

            var coverBox = R(body.Left + cw + 12, row2Y, cw, 86);
            using (p.Panel("opt:hide_when_covered", coverBox, 20))
            {
                p.Label(Loc.T("Прятать в окнах", "Hide covered", "Pencerelerde Gizle"), R(coverBox.Left + 16, coverBox.Top + 14, cw - 70, 20), 12, Theme.Text, true);
                p.Label(Loc.T("Прятать при перекрытии", "Hide when overlapped", "Çakışınca gizle"), R(coverBox.Left + 16, coverBox.Top + 36, cw - 70, 16), 10, Theme.Muted);
                p.Label(Loc.T($"Хоткей: {HotkeyInfo.Text}", $"Hotkey: {HotkeyInfo.Text}", $"Kısayol: {HotkeyInfo.Text}"), R(coverBox.Left + 16, coverBox.Top + 54, cw - 70, 16), 9.5, Theme.Accent);
                double on = isl.S.HideWhenCovered ? 1.0 : 0.0;
                p.Switch(R(coverBox.Right - 16 - 40, coverBox.Top + (coverBox.Height - 24) / 2, 40, 24), isl.Nsp("opt:hide_when_covered", on).X);
            }

            // 3. Бледнеть над видео
            double row3Y = row2Y + 94;
            double level = isl.S.DimLevel;
            ToggleSliderCard(isl, p, R(body.Left, row3Y, body.Width, 70), Loc.T("Бледнеть над видео", "Dim over fullscreen", "Videolar üzerinde soluklaş"), "opt:dim_enabled", "slider:dim",
                isl.S.DimEnabled, Loc.T($"затухание {Math.Round(level * 100)}% в полноэкранном режиме видео и игр", $"dim level {Math.Round(level * 100)}% over fullscreen video and games", $"tam ekran video ve oyunlarda %{Math.Round(level * 100)} karartma"), (level - DimMin) / (DimMax - DimMin));
        }
        else
        {
            // === РАЗДЕЛ 3: СИСТЕМА И ОПОВЕЩЕНИЯ ===

            // 1. Уведомления Dynamic Island (слева в Row 1)
            var notifBox = R(body.Left, contentY, cw, 96);
            using (p.Panel("opt:notify", notifBox, 20))
            {
                p.Label(Loc.T("Уведомления острова", "Dynamic Island Alerts", "Ada Bildirimleri"), R(notifBox.Left + 16, notifBox.Top + 13, cw - 120, 20), 12, Theme.Text, true);
                p.Label(Loc.T("Всплывают над окнами на 5 сек", "Pop up over windows for 5s", "5 sn pencerelerin üzerinde görünür"), R(notifBox.Left + 16, notifBox.Top + 35, cw - 120, 16), 10, Theme.Muted);
                p.Label(Loc.T("Клик открывает плеер/остров", "Click opens details", "Tıklama detayları açar"), R(notifBox.Left + 16, notifBox.Top + 53, cw - 120, 14), 9, Theme.Muted);

                var testBtn = R(notifBox.Right - 16 - 40 - 54, notifBox.Top + (notifBox.Height - 24) / 2, 48, 24);
                using (p.Pressable("test:notify", testBtn))
                {
                    double hv = isl.Hv("test:notify");
                    Color bg = Theme.Mix(Theme.Surface2, Theme.Accent, hv * 0.4);
                    p.Card(testBtn, bg, 12, Theme.BorderSubtle);
                    p.Label(Loc.T("Тест", "Test", "Test"), testBtn, 10, Theme.Text, true, HAlign.Center);
                }
                isl.AddHit("test:notify", testBtn);
                p.Switch(R(notifBox.Right - 16 - 40, notifBox.Top + (notifBox.Height - 24) / 2, 40, 24), isl.Nsp("opt:notify", isl.S.NotifyEnabled ? 1.0 : 0.0).X);
            }

            // 2. Автозапуск с Windows (справа в Row 1)
            var autoBox = R(body.Left + cw + 12, contentY, cw, 96);
            using (p.Panel("autostart", autoBox, 20))
            {
                p.Label(Loc.T("Автозапуск с Windows", "Launch at Windows startup", "Windows Başlangıcında Çalıştır"), R(autoBox.Left + 16, autoBox.Top + 13, cw - 70, 20), 12, Theme.Text, true);
                p.Label(Loc.T("Запуск при входе в систему", "Start silently upon login", "Girişte sessizce başlat"), R(autoBox.Left + 16, autoBox.Top + 35, cw - 70, 16), 10, Theme.Muted);
                p.Label(Loc.T("Фоновый режим без задержек", "Fast silent background startup", "Gecikmesiz arka plan modu"), R(autoBox.Left + 16, autoBox.Top + 53, cw - 70, 16), 9.5, Theme.Muted);
                p.Switch(R(autoBox.Right - 16 - 40, autoBox.Top + (autoBox.Height - 24) / 2, 40, 24), isl.Nsp("autostart").X);
            }

            // 3. Язык интерфейса (слева в Row 2)
            double row2Y = contentY + 106;
            var langBox = R(body.Left, row2Y, cw, 96);
            p.Card(langBox, Theme.Surface, 20);
            p.Label(Loc.T("Язык интерфейса", "Interface Language", "Arayüz Dili"), R(langBox.Left + 16, langBox.Top + 12, cw - 32, 18), 12, Theme.Text, true);
            p.Label(Loc.T("Русский / English / Türkçe", "English / Русский / Türkçe", "Türkçe / English / Русский"), R(langBox.Left + 16, langBox.Top + 32, cw - 32, 16), 10, Theme.Muted);

            double pillGap = 6;
            double pillW = (cw - 32 - pillGap * 2) / 3, pillH = 28;
            double pillY = langBox.Top + 54;
            var ruRect = R(langBox.Left + 16, pillY, pillW, pillH);
            var enRect = R(langBox.Left + 16 + pillW + pillGap, pillY, pillW, pillH);
            var trRect = R(langBox.Left + 16 + (pillW + pillGap) * 2, pillY, pillW, pillH);

            bool isRu = Loc.Lang == "ru";
            bool isEn = Loc.Lang == "en";
            bool isTr = Loc.Lang == "tr";

            p.Card(ruRect, isRu ? Theme.Accent : Theme.Surface2, 14);
            p.Label("🇷🇺 Русский", ruRect, 9.0, isRu ? Colors.White : Theme.Muted, true, HAlign.Center);
            isl.AddHit("lang:ru", ruRect);

            p.Card(enRect, isEn ? Theme.Accent : Theme.Surface2, 14);
            p.Label("🇬🇧 English", enRect, 9.0, isEn ? Colors.White : Theme.Muted, true, HAlign.Center);
            isl.AddHit("lang:en", enRect);

            p.Card(trRect, isTr ? Theme.Accent : Theme.Surface2, 14);
            p.Label("🇹🇷 Türkçe", trRect, 9.0, isTr ? Colors.White : Theme.Muted, true, HAlign.Center);
            isl.AddHit("lang:tr", trRect);

            // 4. О программе и хоткеи (справа в Row 2)
            var aboutBox = R(body.Left + cw + 12, row2Y, cw, 96);
            p.Card(aboutBox, Theme.Surface, 20);
            p.Label(Loc.T("О программе", "About QuickNotch", "QuickNotch Hakkında"), R(aboutBox.Left + 16, aboutBox.Top + 12, cw - 32, 18), 12, Theme.Text, true);
            p.Label("QuickNotch Dynamic Island v2.0", R(aboutBox.Left + 16, aboutBox.Top + 33, cw - 32, 16), 10, Theme.Muted);
            p.Label(Loc.T($"Хоткей: {HotkeyInfo.Text} • Esc: свернуть", $"Hotkey: {HotkeyInfo.Text} • Esc: collapse", $"Kısayol: {HotkeyInfo.Text} • Esc: daralt"),
                R(aboutBox.Left + 16, aboutBox.Top + 51, cw - 32, 16), 9.5, Theme.Accent);

            double dotY = aboutBox.Top + 73;
            p.Circle(new Point(aboutBox.Left + 20, dotY + 4), 3.5, Theme.C("#22C55E"));
            p.Label(Loc.T("Direct2D GPU ускорение • 0% CPU в покое", "Direct2D GPU acceleration • 0% idle CPU", "Direct2D GPU hızlandırma • Boşta %0 CPU"),
                R(aboutBox.Left + 28, dotY - 2, cw - 44, 16), 9, Theme.Muted);
        }
    }

    private void ToggleSliderCard(IslandView isl, Painter p, Rect r, string title, string toggleKey, string sliderKey,
                                  bool enabled, string valueText, double frac)
    {
        double hot = isl.Hv(toggleKey);
        p.Card(r, Theme.Mix(Theme.Surface, Theme.Surface2, hot), 20);
        p.Label(title, R(r.Left + 16, r.Top + 8, r.Width - 80, 24), 12, Theme.Text, true);
        double v = isl.Nsp(toggleKey, enabled ? 1.0 : 0.0).X;
        p.Switch(R(r.Right - 16 - 40, r.Top + 9, 40, 24), v);
        isl.AddHit(toggleKey, R(r.Left, r.Top, r.Width, 38));
        p.Label(valueText, R(r.Left + 16, r.Top + 33, r.Width - 32, 18), 10, Theme.Muted);
        var track = R(r.Left + 16, r.Bottom - 18, r.Width - 32, 7);
        Tracks[sliderKey] = track;
        p.Slider(sliderKey, track, frac, enabled);
    }

    // ---------- события ----------

    public override void Drag(IslandView isl, string key, Point pos)
    {
        if (!Tracks.TryGetValue(key, out var track) || track.Width <= 0) return;
        Dragging = true;
        double f = Ui.Clamp((pos.X - track.Left) / track.Width);
        switch (key)
        {
            case "slider:brightness":
                var sp = isl.Nsp("brightness");
                sp.X = sp.Target = f;
                sp.V = 0.0;
                _brPending = (int)Math.Round(f * 100);
                PushBrightness(isl);
                break;
            case "slider:dim":
                isl.S.DimLevel = Math.Round(DimMin + (DimMax - DimMin) * f, 2);
                break;
            case "slider:delay":
                isl.S.LeaveDelay = Math.Round(DelayMin + (DelayMax - DelayMin) * f, 1);
                break;
        }
        isl.Kick();
    }

    public override void DragEnd(IslandView isl, string key)
    {
        Dragging = false;
        if (key is "slider:dim" or "slider:delay") isl.SaveState();
    }

    public override void Click(IslandView isl, string key)
    {
        if (key == "settab:appearance") { SubTab = "appearance"; isl.Kick(); }
        else if (key == "settab:behavior") { SubTab = "behavior"; isl.Kick(); }
        else if (key == "settab:system") { SubTab = "system"; isl.Kick(); }
        else if (key == "sys:dark") ToggleDark(isl);
        else if (key.StartsWith("theme:")) isl.SetThemeMode(key[6..]);
        else if (key.StartsWith("style:")) isl.SetIslandStyle(key[6..]);
        else if (key.StartsWith("accent:")) isl.SetAccent(key[7..]);
        else if (key == "lang:ru") isl.SetLanguage("ru");
        else if (key == "lang:en") isl.SetLanguage("en");
        else if (key == "lang:tr") isl.SetLanguage("tr");
        else if (key == "opt:glass") isl.ToggleGlass();
        else if (key == "opt:notify") isl.ToggleNotify();
        else if (key == "test:notify") { isl.Collapse(); NotificationService.TriggerTest(); }
        else if (key == "opt:toggle_roam")
        {
            if (isl.S.RobotRoaming) isl.DockAero();
            else isl.LaunchAeroToDesktop();
        }
        else if (key.StartsWith("opt:")) isl.ToggleOpt(key[4..]);
        else if (key == "autostart") isl.ToggleAutostart();
        else if (key.StartsWith("summon:")) isl.SetSummonMode(key[7..]);
    }
}

public static class HotkeyInfo
{
    public const string Text = "Ctrl+Alt+N";
}

// ------------------------------------------------------------------ Ссылки и Быстрый Запуск (LaunchDeck)

public sealed class LinksSection : Section
{
    private List<QuickLink> _links;
    private string? _error;
    private string _filter = "all"; // "all", "web", "folder", "app"
    private bool _editMode = false;
    private double _maxScroll = 0;

    private static readonly (string Label, string Url, string Icon, string Type)[] Presets = new[]
    {
        ("GitHub", "https://github.com", "⎇", "web"),
        ("Telegram", "https://web.telegram.org", "✈", "web"),
        ("YouTube", "https://youtube.com", "▶", "web"),
        ("ChatGPT", "https://chatgpt.com", "✦", "web"),
        ("Google", "https://google.com", "G", "web"),
        ("VK", "https://vk.com", "VK", "web"),
        ("Загрузки", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "📥", "folder"),
        ("Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "🖥", "folder"),
    };

    public LinksSection()
    {
        Key = "links";
        Title = "Ссылки";
        (_links, _error) = StoreService.LoadLinks();
    }

    private static (string Icon, string Badge, string Type, Color BgCol, Color FgCol) DetectLinkInfo(string target)
    {
        string t = target.ToLowerInvariant();
        if (t.Contains("github.com")) return ("⎇", "GIT", "web", Theme.C("#24292E"), Theme.C("#F0F6FC"));
        if (t.Contains("youtube.com") || t.Contains("youtu.be")) return ("▶", "PLAY", "web", Theme.C("#3F1414"), Theme.C("#FF4444"));
        if (t.Contains("t.me") || t.Contains("telegram")) return ("✈", "TG", "web", Theme.C("#12314D"), Theme.C("#38BDF8"));
        if (t.Contains("chatgpt") || t.Contains("openai.com")) return ("✦", "AI", "web", Theme.C("#13362B"), Theme.C("#10A37F"));
        if (t.Contains("vk.com")) return ("VK", "SOC", "web", Theme.C("#152C47"), Theme.C("#4C94FF"));
        if (t.Contains("yandex")) return ("Я", "SEARCH", "web", Theme.C("#3B1818"), Theme.C("#FC3F1D"));
        if (t.Contains("google")) return ("G", "SEARCH", "web", Theme.C("#162846"), Theme.C("#4285F4"));
        if (t.Contains("discord")) return ("💬", "CHAT", "web", Theme.C("#23264A"), Theme.C("#5865F2"));
        if (t.Contains("figma")) return ("❖", "DESIGN", "web", Theme.C("#331E2A"), Theme.C("#F24E1E"));
        if (t.Contains("notion")) return ("N", "DOCS", "web", Theme.C("#252528"), Theme.C("#FFFFFF"));
        if (t.Contains("spotify")) return ("♫", "MUSIC", "web", Theme.C("#11331F"), Theme.C("#1DB954"));
        if (t.Contains("steam")) return ("♨", "GAME", "web", Theme.C("#172230"), Theme.C("#66C0F4"));
        if (t.Contains("reddit")) return ("●", "COMM", "web", Theme.C("#381F14"), Theme.C("#FF4500"));
        if (t.Contains("twitch")) return ("▲", "LIVE", "web", Theme.C("#2A1942"), Theme.C("#9146FF"));
        if (t.EndsWith(".exe") || t.EndsWith(".lnk")) return ("⚡", "APP", "app", Theme.C("#331E40"), Theme.AccentHot);
        if (t.Contains(":\\") || t.Contains(":/")) return ("📁", "DIR", "folder", Theme.C("#3B2E15"), Theme.Yellow);
        return ("↗", "WEB", "web", Theme.C("#1E222D"), Theme.Muted);
    }

    private static string GetSubtitle(string target)
    {
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(target);
                return uri.Host.Replace("www.", "");
            }
            catch { }
        }
        if (target.Contains(":\\") || target.Contains(":/"))
        {
            try
            {
                string fn = System.IO.Path.GetFileName(target);
                if (!string.IsNullOrEmpty(fn)) return fn;
                return target.TrimEnd('\\', '/');
            }
            catch { }
        }
        return target;
    }

    private static string DetectDefaultLabel(string target)
    {
        string t = target.ToLowerInvariant();
        if (t.Contains("github.com")) return "GitHub";
        if (t.Contains("youtube.com") || t.Contains("youtu.be")) return "YouTube";
        if (t.Contains("t.me") || t.Contains("telegram")) return "Telegram";
        if (t.Contains("chatgpt") || t.Contains("openai.com")) return "ChatGPT";
        if (t.Contains("vk.com")) return "ВКонтакте";
        if (t.Contains("yandex")) return "Яндекс";
        if (t.Contains("google")) return "Google";
        if (t.Contains("discord")) return "Discord";
        if (t.Contains("figma")) return "Figma";
        if (t.Contains("notion")) return "Notion";
        if (t.Contains("spotify")) return "Spotify";
        if (t.Contains("steam")) return "Steam";
        if (t.Contains("reddit")) return "Reddit";
        if (t.Contains("twitch")) return "Twitch";
        return GetSubtitle(target);
    }

    public override void Wheel(IslandView isl, double notches)
    {
        var sp = isl.Nsp("links_scroll");
        sp.Target = Math.Clamp(sp.Target - notches * 56, 0, _maxScroll);
        isl.StartFrames();
        isl.Kick();
    }

    public override void Paint(IslandView isl, Painter p, Rect rect)
    {
        if (_error != null)
        {
            p.Note(R(rect.Left + 4, rect.Top + 6, rect.Width - 8, 60), _error, Theme.Red, 12);
            return;
        }

        // ==================== 1. ВЕРХНИЙ БАР (HEADER & FILTERS) ====================
        double topY = rect.Top + 2;

        // Заголовок
        p.Label("🚀 " + Loc.T("Быстрый запуск", "Quick Launcher", "Hızlı Başlatıcı"),
            R(rect.Left + 4, topY, 155, 26), 15, Theme.Text, true);

        // Фильтрованные элементы
        var filtered = _links
            .Select((l, idx) => (Link: l, OriginalIndex: idx))
            .Where(x => _filter == "all" || DetectLinkInfo(x.Link.Target).Type == _filter)
            .ToList();

        // Счётчик
        string countBadge = $"{filtered.Count}";
        double countW = p.TextWidth(countBadge, 10, true) + 14;
        var countRect = R(rect.Left + 160, topY + 3, countW, 20);
        p.Card(countRect, Theme.Mix(Theme.Surface, Theme.Surface2, 0.7), 10, Theme.BorderSubtle);
        p.Label(countBadge, countRect, 10, Theme.Muted, true, HAlign.Center);

        // Табы категорий
        double filterX = countRect.Right + 14;
        var filters = new (string Key, string Name)[]
        {
            ("all", Loc.T("Все", "All", "Tümü")),
            ("web", Loc.T("🌐 Веб", "🌐 Web", "🌐 Web")),
            ("folder", Loc.T("📁 Папки", "📁 Folders", "📁 Klasörler")),
            ("app", Loc.T("⚡ Программы", "⚡ Apps", "⚡ Uygulamalar"))
        };

        foreach (var (fKey, fName) in filters)
        {
            bool isSel = _filter == fKey;
            double fw = p.TextWidth(fName, 9.5, isSel) + 14;
            var fRect = R(filterX, topY + 2, fw, 22);
            string hitKey = $"link:filter:{fKey}";
            fRect = p.PressRect(isl, hitKey, fRect, 1.5);
            double fHv = isl.Hv(hitKey);
            var fBg = isSel ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.45) : Theme.Mix(Theme.Surface, Theme.Surface2, 0.3 + 0.3 * fHv);
            p.Card(fRect, fBg, 11, isSel ? Theme.Accent : Theme.BorderSubtle);
            p.Label(fName, fRect, 9.5, isSel ? Colors.White : (fHv > 0.05 ? Theme.Text : Theme.Muted), isSel, HAlign.Center);
            isl.AddHit(hitKey, fRect);
            filterX += fw + 5;
        }

        // Правые кнопки управления: «+ Ссылка», «📁 Файл», «✎ Управление»
        double rightOffset = rect.Right;

        // Кнопка Управление / Режим удаления
        string editLabel = _editMode ? ("✓ " + Loc.T("Готово", "Done", "Tamam")) : ("✎ " + Loc.T("Управление", "Manage", "Yönet"));
        double editW = p.TextWidth(editLabel, 9.5, true) + 16;
        rightOffset -= editW;
        var editRect = R(rightOffset, topY + 1, editW, 24);
        editRect = p.PressRect(isl, "link:toggle_edit", editRect, 1.5);
        double editHv = isl.Hv("link:toggle_edit");
        var editBg = _editMode ? Theme.Mix(Theme.Surface2, Theme.Red, 0.35) : Theme.Mix(Theme.Surface, Theme.Surface2, 0.5 + 0.3 * editHv);
        p.Card(editRect, editBg, 12, _editMode ? Theme.Red : Theme.BorderSubtle);
        p.Label(editLabel, editRect, 9.5, _editMode ? Theme.Red : Theme.Text, true, HAlign.Center);
        isl.AddHit("link:toggle_edit", editRect);
        rightOffset -= 6;

        // Кнопка «📁 Файл / Папка»
        string fileLabel = "📁 " + Loc.T("Файл", "File", "Dosya");
        double fileW = p.TextWidth(fileLabel, 9.5, true) + 16;
        rightOffset -= fileW;
        var fileRect = R(rightOffset, topY + 1, fileW, 24);
        fileRect = p.PressRect(isl, "link:add_file", fileRect, 1.5);
        double fileHv = isl.Hv("link:add_file");
        p.Card(fileRect, Theme.Mix(Theme.Surface, Theme.Surface2, 0.5 + 0.3 * fileHv), 12, Theme.BorderSubtle);
        p.Label(fileLabel, fileRect, 9.5, Theme.Text, true, HAlign.Center);
        isl.AddHit("link:add_file", fileRect);
        rightOffset -= 6;

        // Кнопка «+ Ссылка»
        string urlLabel = "+ " + Loc.T("Ссылка", "Link", "Bağlantı");
        double urlW = p.TextWidth(urlLabel, 9.5, true) + 16;
        rightOffset -= urlW;
        var urlRect = R(rightOffset, topY + 1, urlW, 24);
        urlRect = p.PressRect(isl, "link:add_url", urlRect, 1.5);
        double urlHv = isl.Hv("link:add_url");
        p.Card(urlRect, Theme.Mix(Theme.Surface2, Theme.Accent, 0.45 + 0.25 * urlHv), 12, Theme.Accent);
        p.Label(urlLabel, urlRect, 9.5, Colors.White, true, HAlign.Center);
        isl.AddHit("link:add_url", urlRect);

        // ==================== 2. ОБЛАСТЬ КАРТОЧЕК (С СЕТКОЙ И СКРОЛЛОМ) ====================
        double listTop = rect.Top + 34;
        double listBottom = rect.Bottom - 44;
        double listH = listBottom - listTop;
        var listClip = R(rect.Left, listTop, rect.Width, listH);

        double colGap = 10;
        double rowGap = 8;
        const int cols = 3;
        double colW = (listClip.Width - colGap * (cols - 1)) / cols;
        double rowH = 60;

        int totalRows = (int)Math.Ceiling(filtered.Count / (double)cols);
        double totalContentH = totalRows * (rowH + rowGap);
        _maxScroll = Math.Max(0.0, totalContentH - listH);

        var spScroll = isl.Nsp("links_scroll");
        spScroll.Target = Math.Clamp(spScroll.Target, 0.0, _maxScroll);
        if (!spScroll.Settled) isl.StartFrames();
        double scroll = Math.Clamp(spScroll.X, 0.0, _maxScroll);

        if (filtered.Count == 0)
        {
            var emptyRect = R(rect.Left + 20, listTop + 40, rect.Width - 40, 160);
            p.Card(emptyRect, Theme.Mix(Theme.Surface, Theme.Surface2, 0.4), 16, Theme.BorderSubtle);
            p.Label("🚀", R(emptyRect.Left, emptyRect.Top + 24, emptyRect.Width, 32), 26, Theme.Muted, true, HAlign.Center);
            p.Label(Loc.T("В быстром запуске пока пусто", "Quick Launcher is empty", "Hızlı Başlatıcı boş"),
                R(emptyRect.Left, emptyRect.Top + 64, emptyRect.Width, 22), 13, Theme.Text, true, HAlign.Center);
            p.Label(Loc.T("Добавьте нужные ссылки или выберите популярные сервисы из панели внизу",
                          "Add custom links or pick presets from the bottom bar",
                          "Aşağıdaki panelden bağlantı ekleyin veya popüler siteleri seçin"),
                R(emptyRect.Left, emptyRect.Top + 90, emptyRect.Width, 18), 10.5, Theme.Muted, false, HAlign.Center);
        }
        else
        {
            using (p.WithClip(listClip))
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    var item = filtered[i];
                    int r = i / cols;
                    int c = i % cols;

                    double cardX = listClip.Left + c * (colW + colGap);
                    double cardY = listClip.Top - scroll + r * (rowH + rowGap);
                    var cardRect = R(cardX, cardY, colW, rowH);

                    // Оконное отсечение карточек за пределами вьюпорта
                    if (cardRect.Bottom < listClip.Top - 10 || cardRect.Top > listClip.Bottom + 10) continue;

                    string cardKey = $"link:{item.OriginalIndex}";
                    string delKey = $"linkdel:{item.OriginalIndex}";

                    // Защита от сброса ховера при наведении на крестик удаления!
                    double cardHv = Math.Max(isl.Hv(cardKey), isl.Hv(delKey));
                    double delHv = isl.Hv(delKey);

                    var (icon, badge, type, bgCol, fgCol) = DetectLinkInfo(item.Link.Target);
                    string subtitle = GetSubtitle(item.Link.Target);

                    var cardPress = p.PressRect(isl, cardKey, cardRect, 1.5);
                    var cardBg = Theme.Mix(Theme.Surface, Theme.Surface2, 0.5 + 0.45 * cardHv);
                    var cardBorder = cardHv > 0.05 ? Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.4 + 0.6 * cardHv) : Theme.BorderSubtle;

                    p.Card(cardPress, cardBg, 14, cardBorder);

                    // Иконка-сквиркл слева
                    var iconR = R(cardPress.Left + 8, cardPress.Top + 8, 44, 44);
                    p.Card(iconR, Theme.Mix(bgCol, Theme.Surface, 0.25), 11, Theme.Mix(bgCol, Colors.White, 0.15));
                    p.Label(icon, iconR, 18, fgCol, true, HAlign.Center);

                    // Текстовая колонка
                    double textLeft = cardPress.Left + 58;
                    double textRight = cardPress.Right - 32;
                    double textW = Math.Max(20, textRight - textLeft);

                    p.Label(item.Link.Label, R(textLeft, cardPress.Top + 9, textW, 18), 12, cardHv > 0.05 ? Colors.White : Theme.Text, true);

                    // Вторая строка: бейдж типа + подзаголовок
                    double subLeft = textLeft;
                    double badgeW = p.TextWidth(badge, 8, true) + 8;
                    var badgeR = R(subLeft, cardPress.Top + 32, badgeW, 15);
                    p.Card(badgeR, Theme.Mix(Theme.Surface2, Theme.Surface3, 0.6), 4);
                    p.Label(badge, badgeR, 8, Theme.Muted, true, HAlign.Center);

                    subLeft += badgeW + 6;
                    p.Label(subtitle, R(subLeft, cardPress.Top + 31, Math.Max(20, textRight - subLeft), 16), 9.5, Theme.Muted);

                    // Кнопка удаления (крестик)
                    var delRect = R(cardPress.Right - 28, cardPress.Top + (cardPress.Height - 22) / 2, 22, 22);

                    if (_editMode || cardHv > 0.05)
                    {
                        delRect = p.PressRect(isl, delKey, delRect, 1.5);
                        var delBg = delHv > 0.05
                            ? Theme.Red
                            : (_editMode ? Theme.Mix(Theme.Surface3, Theme.Red, 0.4) : Color.FromArgb(60, 255, 255, 255));
                        p.Circle(new Point(delRect.Left + 11, delRect.Top + 11), 9, delBg);
                        p.Label("✕", delRect, 9.5, (delHv > 0.05 || _editMode) ? Colors.White : Theme.Muted, true, HAlign.Center);
                    }
                    else
                    {
                        // Стрелочка перехода
                        p.Label("↗", delRect, 11, Theme.Muted, false, HAlign.Center);
                    }

                    // ВАЖНО: Сначала регистрируем карточку, а затем поверх неё крестик удаления!
                    // Благодаря обратному обходу в HitAt клик по крестику всегда сработает первым!
                    isl.AddHit(cardKey, cardRect);
                    if (_editMode || cardHv > 0.05)
                    {
                        isl.AddHit(delKey, delRect);
                    }
                }
            }

            // Тонкий Apple скроллбар
            if (_maxScroll > 1)
            {
                double scrollFrac = scroll / _maxScroll;
                double trackH = listClip.Height;
                double thumbH = Math.Max(24, trackH * (trackH / totalContentH));
                double thumbY = listClip.Top + scrollFrac * (trackH - thumbH);
                p.Card(R(listClip.Right - 3, thumbY, 3, thumbH), Color.FromArgb(85, 255, 255, 255), 1.5);
            }
        }

        // ==================== 3. НИЖНИЙ БАР (БЫСТРЫЕ ШАБЛОНЫ 1 КЛИК) ====================
        var botBar = R(rect.Left, rect.Bottom - 36, rect.Width, 36);
        p.Card(botBar, Theme.Mix(Theme.Surface, Theme.Surface2, 0.4), 14, Theme.BorderSubtle);

        double curPx = botBar.Left + 14;
        string quickAddLabel = Loc.T("Быстрый выбор:", "Quick add:", "Hızlı ekle:");
        p.Label(quickAddLabel, R(curPx, botBar.Top + 9, 100, 18), 10, Theme.Muted, true);
        curPx += p.TextWidth(quickAddLabel, 10, true) + 12;

        for (int i = 0; i < Presets.Length; i++)
        {
            var pr = Presets[i];
            string pText = $"+ {pr.Icon} {pr.Label}";
            double pw = p.TextWidth(pText, 9.5, false) + 14;
            if (curPx + pw > botBar.Right - 10) break;
            var chipR = R(curPx, botBar.Top + 6, pw, 24);
            string chipKey = $"quickadd:{i}";
            chipR = p.PressRect(isl, chipKey, chipR, 1.5);
            double chHv = isl.Hv(chipKey);
            var chBg = Theme.Mix(Theme.Surface2, Theme.Accent, 0.12 + 0.3 * chHv);
            p.Card(chipR, chBg, 12, chHv > 0.05 ? Theme.Accent : Theme.BorderSubtle);
            p.Label(pText, chipR, 9.5, chHv > 0.05 ? Theme.AccentHot : Theme.Text, false, HAlign.Center);
            isl.AddHit(chipKey, chipR);
            curPx += pw + 6;
        }
    }

    public override void Click(IslandView isl, string key)
    {
        if (_error != null) return;
        try
        {
            if (key == "link:add_url")
            {
                string? target = isl.Ask(Loc.T("Добавить ссылку", "Add Link", "Bağlantı Ekle"),
                                         Loc.T("Введите адрес сайта (URL):", "Enter website URL:", "Web sitesi URL'sini girin:"));
                if (string.IsNullOrWhiteSpace(target)) return;
                target = target.Trim();
                if (!target.Contains("://") && !target.Contains(":\\") && !target.Contains(":/")) target = "https://" + target;

                string defaultLabel = DetectDefaultLabel(target);
                string? label = isl.Ask(Loc.T("Название ссылки", "Link Name", "Bağlantı Adı"),
                                        Loc.T("Введите отображаемое имя ссылки:", "Enter link display name:", "Bağlantı görünen adını girin:"),
                                        defaultLabel);
                if (string.IsNullOrWhiteSpace(label)) label = defaultLabel;

                _links.Add(new QuickLink { Label = label.Trim(), Target = target });
                StoreService.SaveLinks(_links);
                isl.Kick();
                return;
            }

            if (key == "link:add_file")
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = Loc.T("Выберите приложение или файл", "Select Application or File", "Uygulama veya Dosya Seçin"),
                    Filter = Loc.T("Все файлы (*.*)|*.*|Программы (*.exe;*.lnk)|*.exe;*.lnk", "All Files (*.*)|*.*|Apps (*.exe;*.lnk)|*.exe;*.lnk", "Tüm Dosyalar (*.*)|*.*")
                };
                if (dlg.ShowDialog() == true)
                {
                    string name = Path.GetFileNameWithoutExtension(dlg.FileName);
                    _links.Add(new QuickLink { Label = name, Target = dlg.FileName });
                    StoreService.SaveLinks(_links);
                    isl.Kick();
                }
                return;
            }

            if (key == "link:toggle_edit")
            {
                _editMode = !_editMode;
                isl.Kick();
                return;
            }

            if (key.StartsWith("link:filter:"))
            {
                _filter = key["link:filter:".Length..];
                var sp = isl.Nsp("links_scroll");
                sp.X = 0;
                sp.Target = 0;
                isl.Kick();
                return;
            }

            if (key.StartsWith("quickadd:"))
            {
                int qIdx = int.Parse(key["quickadd:".Length..]);
                if (qIdx >= 0 && qIdx < Presets.Length)
                {
                    var pr = Presets[qIdx];
                    if (_links.Any(l => l.Target.Equals(pr.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        isl.Alert(Loc.T($"«{pr.Label}» уже в списке избранного.", $"\"{pr.Label}\" is already added.", $"\"{pr.Label}\" zaten eklendi."));
                        return;
                    }
                    _links.Add(new QuickLink { Label = pr.Label, Target = pr.Url });
                    StoreService.SaveLinks(_links);
                    isl.Kick();
                }
                return;
            }

            if (key.StartsWith("linkdel:"))
            {
                int delIdx = int.Parse(key["linkdel:".Length..]);
                if (delIdx >= 0 && delIdx < _links.Count)
                {
                    _links.RemoveAt(delIdx);
                    StoreService.SaveLinks(_links);
                    isl.Kick();
                }
                return;
            }

            if (key.StartsWith("link:"))
            {
                int linkIdx = int.Parse(key["link:".Length..]);
                if (linkIdx >= 0 && linkIdx < _links.Count)
                {
                    string target = _links[linkIdx].Target;
                    try
                    {
                        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        isl.Alert(Loc.T($"Не удалось открыть: {ex.Message}", $"Failed to open: {ex.Message}", $"Açılamadı: {ex.Message}"));
                    }
                }
                return;
            }
        }
        catch (Exception e)
        {
            isl.Alert(Loc.T($"Ошибка: {e.Message}", $"Error: {e.Message}", $"Hata: {e.Message}"));
        }
    }
}

// ------------------------------------------------------------------ Буфер

public sealed class ClipItem
{
    public string Text = "";
    public DateTime? Time;
    public bool Pinned;
}

public sealed class ClipboardSection : Section
{
    private const double RowH = 50, Gap = 6, Step = RowH + Gap;
    private const int MaxItems = 40;
    private const int MaxText = 100_000;
    private const string PrivateFormat = "ExcludeClipboardContentFromMonitorProcessing";

    private readonly List<ClipItem> _items = new();
    private string? _own;
    private string? _flashText;
    private double _flashUntil;
    private int _selectedIndex = 0;
    private string _filter = "all";
    private Rect _lastBoxRect;
    private double _previewTextHeight;
    private string? _cachedText;
    private string[] _cachedLines = Array.Empty<string>();
    private double _cachedLineHeight;
    private double _cachedTotalHeight;

    public ClipboardSection() { Key = "clipboard"; Title = "Буфер"; }

    public override void Enter(IslandView isl)
    {
        var sp = isl.Nsp("clip_prev_scroll");
        sp.X = 0;
        sp.Target = 0;
        _cachedText = null;
    }

    public override void Attach(IslandView isl)
    {
        foreach (string text in isl.S.ClipPinned)
            _items.Add(new ClipItem { Text = text, Time = null, Pinned = true });
        isl.RunLater(300, () => Capture(isl, notify: false));
    }

    public void OnClipboardChanged(IslandView isl) => isl.RunLater(50, () => Capture(isl, notify: true, attempt: 0));

    public void Capture(IslandView isl, bool notify = true, int attempt = 0)
    {
        string? text = null;
        bool locked = false;

        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                var data = System.Windows.Clipboard.GetDataObject();
                if (data != null && data.GetDataPresent(PrivateFormat)) return;
                text = System.Windows.Clipboard.GetText();
            }
            else if (System.Windows.Clipboard.ContainsFileDropList())
            {
                var files = System.Windows.Clipboard.GetFileDropList();
                if (files.Count == 1) text = files[0];
                else if (files.Count > 1) text = $"{files.Count} files: {System.IO.Path.GetFileName(files[0])}…";
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or IOException)
        {
            locked = true;
        }
        catch { }

        if (locked && attempt < 5)
        {
            // Не блокируем UI-поток через Thread.Sleep — повторяем попытку асинхронно
            isl.RunLater(25, () => Capture(isl, notify, attempt + 1));
            return;
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxText) return;
        if (text == _own)
        {
            _own = null;
            return;
        }
        _own = null;

        var existing = _items.FirstOrDefault(i => i.Text == text);
        if (existing != null)
        {
            if (!existing.Pinned)
            {
                _items.Remove(existing);
                existing.Time = DateTime.Now;
                _items.Insert(PinnedCount(), existing);
            }
        }
        else
        {
            _items.Insert(PinnedCount(), new ClipItem { Text = text, Time = DateTime.Now, Pinned = false });
            Trim();
        }
        _selectedIndex = 0;
        var spPrv = isl.Nsp("clip_prev_scroll");
        spPrv.X = 0;
        spPrv.Target = 0;
        isl.Kick();

        if (notify)
        {
            string preview = text.Trim().Replace("\r", " ").Replace("\n", " ");
            while (preview.Contains("  ")) preview = preview.Replace("  ", " ");
            if (preview.Length > 52) preview = preview[..50] + "…";

            isl.ShowNotification(new NotificationItem
            {
                App = Loc.T("БУФЕР ОБМЕНА", "CLIPBOARD", "PANO"),
                Title = Loc.T("Скопировано", "Copied", "Kopyalandı"),
                Body = preview,
                IconSymbol = "📋",
                AccentColor = Theme.Accent,
                OnClick = () =>
                {
                    isl.Select("clipboard");
                    isl.Expand();
                }
            }, durationSec: 2.8);
        }
    }

    private int PinnedCount() => _items.Count(i => i.Pinned);

    private void Trim()
    {
        while (_items.Count > MaxItems)
        {
            int idx = _items.FindLastIndex(i => !i.Pinned);
            if (idx < 0) return;
            _items.RemoveAt(idx);
        }
    }

    private void Save(IslandView isl)
    {
        isl.S.ClipPinned = _items.Where(i => i.Pinned).Select(i => i.Text).ToList();
        isl.SaveState();
    }

    private double MaxScroll(int count, double listH) => Math.Max(0.0, count * Step - Gap - listH);

    private void ClampScroll(IslandView isl, int count, double listH)
    {
        var sp = isl.Nsp("clip_scroll");
        sp.Target = Math.Min(sp.Target, MaxScroll(count, listH));
    }

    private static string Ago(DateTime? ts)
    {
        if (ts == null) return Loc.T("закреплено", "pinned", "sabitlendi");
        int s = (int)(DateTime.Now - ts.Value).TotalSeconds;
        if (s < 10) return Loc.T("сейчас", "now", "şimdi");
        if (s < 60) return Loc.T($"{s} с назад", $"{s}s ago", $"{s} sn önce");
        if (s < 3600) return Loc.T($"{s / 60} мин назад", $"{s / 60}m ago", $"{s / 60} dk önce");
        if (s < 86400) return Loc.T($"{s / 3600} ч назад", $"{s / 3600}h ago", $"{s / 3600} sa önce");
        return Loc.T($"{s / 86400} д назад", $"{s / 86400}d ago", $"{s / 86400} gün önce");
    }

    private static (string Tag, Color BgCol, Color FgCol) DetectClipType(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return ("URL", Theme.C("#1E3A5F"), Theme.C("#60A5FA"));
        }
        if (trimmed.StartsWith("#") && (trimmed.Length == 4 || trimmed.Length == 7 || trimmed.Length == 9) &&
            System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9a-fA-F]+$"))
        {
            return ("HEX", Theme.C("#1B3D2B"), Theme.C("#4ADE80"));
        }
        if (trimmed.Contains('{') && trimmed.Contains('}') ||
            trimmed.Contains("();") ||
            trimmed.StartsWith("class ") || trimmed.StartsWith("function ") ||
            trimmed.StartsWith("const ") || trimmed.StartsWith("import ") ||
            trimmed.StartsWith("SELECT ") || trimmed.StartsWith("def "))
        {
            return (Loc.T("КОД", "CODE", "KOD"), Theme.C("#382352"), Theme.C("#C084FC"));
        }
        return (Loc.T("ТЕКСТ", "TEXT", "METİN"), Theme.Surface2, Theme.Muted);
    }

    private bool MatchesFilter(ClipItem item)
    {
        if (_filter == "all") return true;
        var (tag, _, _) = DetectClipType(item.Text);
        if (_filter == "code") return tag == "КОД" || tag == "CODE" || tag == "KOD";
        if (_filter == "url") return tag == "URL";
        if (_filter == "text") return tag == "ТЕКСТ" || tag == "TEXT" || tag == "METİN" || tag == "HEX";
        return true;
    }

    public override void Paint(IslandView isl, Painter p, Rect rect)
    {
        // Двухколоночный Apple Master-Detail интерфейс
        double listW = 310;
        double gapX = 14;
        double prevW = rect.Width - listW - gapX;

        var leftRect = R(rect.Left, rect.Top, listW, rect.Height);
        var prevRect = R(rect.Left + listW + gapX, rect.Top, prevW, rect.Height);

        var filtered = _items.Where(MatchesFilter).ToList();
        if (_selectedIndex >= filtered.Count) _selectedIndex = Math.Max(0, filtered.Count - 1);

        // ================= ЛЕВАЯ КОЛОНКА (Фильтры + Список) =================
        // 1. Фильтры категорий
        var filters = new (string Key, string Label)[]
        {
            ("all", Loc.T("Все", "All", "Tümü")),
            ("text", Loc.T("Текст", "Text", "Metin")),
            ("code", Loc.T("Код", "Code", "Kod")),
            ("url", Loc.T("Ссылки", "URLs", "Bağlantılar")),
        };

        double tabW = (listW - 12) / filters.Length;
        for (int i = 0; i < filters.Length; i++)
        {
            var f = filters[i];
            var tabR = R(leftRect.Left + i * (tabW + 4), leftRect.Top, tabW, 26);
            bool isCur = _filter == f.Key;
            string hitKey = $"clipflt:{f.Key}";
            double hot = isl.Hv(hitKey);

            Color tabBg = isCur
                ? Theme.Accent
                : Theme.Mix(Theme.Surface2, Theme.Surface3, hot * 0.5);
            Color tabFg = isCur ? Colors.White : Theme.Mix(Theme.Muted, Theme.Text, 0.4 + 0.6 * hot);

            p.Card(tabR, tabBg, 13);
            p.Label(f.Label, tabR, 10.5, tabFg, true, HAlign.Center);
            isl.AddHit(hitKey, tabR);
        }

        // 2. Список записей
        double listY = leftRect.Top + 32;
        double listH = leftRect.Height - 64;
        var listArea = R(leftRect.Left, listY, listW, listH);
        double scroll = isl.Nsp("clip_scroll").X;

        if (filtered.Count == 0)
        {
            var emptyR = R(listArea.Left, listArea.Top, listArea.Width, 120);
            p.Card(emptyR, Theme.Surface, 16, Theme.BorderSubtle);
            p.Label(Loc.T("Нет записей в этой категории", "No clips in category", "Bu kategoride kayıt yok"),
                R(emptyR.Left, emptyR.Top + 45, emptyR.Width, 20), 12, Theme.Muted, false, HAlign.Center);
        }
        else
        {
            using (p.WithClip(new RectangleGeometry(listArea)))
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    double y = listArea.Top + i * Step - scroll;
                    if (y + RowH < listArea.Top || y > listArea.Bottom) continue;
                    var rowR = R(listArea.Left, y, listArea.Width, RowH);
                    Row(isl, p, i, filtered[i], rowR, listArea, i == _selectedIndex);
                }
                ScrollBar(p, listArea, scroll, filtered.Count, listH);
            }
        }

        // 3. Нижняя строка статистики и очистки
        double botY = leftRect.Bottom - 26;
        p.Label(Loc.T($"Всего: {_items.Count}", $"Total: {_items.Count}", $"Toplam: {_items.Count}"),
            R(leftRect.Left + 4, botY, 120, 24), 10.5, Theme.Muted);

        var clearBtnRect = R(leftRect.Right - 110, botY - 1, 110, 26);
        double clrHv = isl.Hv("clip:clear");
        p.Card(clearBtnRect, Theme.Mix(Theme.Surface2, Theme.Surface3, clrHv * 0.4), 13, Theme.BorderSubtle);
        p.Label(Loc.T("Очистить все", "Clear All", "Tümünü Temizle"), clearBtnRect, 10,
            Theme.Mix(Theme.Muted, Theme.Red, clrHv * 0.7), true, HAlign.Center);
        isl.AddHit("clip:clear", clearBtnRect);

        // ================= ПРАВАЯ КОЛОНКА (Инспектор и предпросмотр) =================
        p.Card(prevRect, Theme.Surface, 20, Theme.BorderSubtle);

        if (filtered.Count == 0 || _selectedIndex >= filtered.Count)
        {
            // Пустое состояние
            var centerP = new Point(prevRect.Left + prevRect.Width / 2, prevRect.Top + prevRect.Height / 2 - 20);
            p.Circle(centerP, 28, Theme.Surface2);
            p.Label("📋", R(centerP.X - 16, centerP.Y - 16, 32, 32), 18, Theme.Muted, false, HAlign.Center);
            p.Label(Loc.T("Буфер обмена пуст", "Clipboard is empty", "Pano boş"),
                R(prevRect.Left, centerP.Y + 36, prevRect.Width, 22), 14, Theme.Text, true, HAlign.Center);
            p.Label(Loc.T("Скопируйте любой текст (Ctrl+C), и он автоматически появится здесь.",
                          "Copy text (Ctrl+C) and it will appear here automatically.",
                          "Herhangi bir metni kopyalayın (Ctrl+C), burada otomatik olarak görünecektir."),
                R(prevRect.Left + 30, centerP.Y + 60, prevRect.Width - 60, 36), 11, Theme.Muted, false, HAlign.Center, VAlign.Top, true);
            return;
        }

        var curItem = filtered[_selectedIndex];
        var (cTag, cTagBg, cTagFg) = DetectClipType(curItem.Text);

        // 1. Шапка панели предпросмотра
        var tagPill = R(prevRect.Left + 18, prevRect.Top + 14, 46, 20);
        p.Card(tagPill, cTagBg, 6);
        p.Label(cTag, tagPill, 9.5, cTagFg, true, HAlign.Center);

        p.Label(Loc.T($"Скопировано: {Ago(curItem.Time)}", $"Copied: {Ago(curItem.Time)}", $"Kopyalandı: {Ago(curItem.Time)}"),
            R(prevRect.Left + 72, prevRect.Top + 15, 200, 18), 10.5, Theme.Muted);

        // Кнопка закрепить (Pin)
        var pinRect = R(prevRect.Right - 18 - 110, prevRect.Top + 12, 74, 24);
        double pinHv = isl.Hv("clipprev:pin");
        p.Card(pinRect, curItem.Pinned ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.25) : Theme.Mix(Theme.Surface2, Theme.Surface3, pinHv * 0.4), 12,
            curItem.Pinned ? Theme.Accent : Theme.BorderSubtle);
        p.Label(curItem.Pinned ? Loc.T("📌 Закреплено", "📌 Pinned", "📌 Sabitlendi") : Loc.T("📌 Закрепить", "📌 Pin", "📌 Sabitle"),
            pinRect, 9.5, curItem.Pinned ? Theme.AccentHot : Theme.Text, true, HAlign.Center);
        isl.AddHit("clipprev:pin", pinRect);

        // Кнопка удалить (Del)
        var delRect = R(prevRect.Right - 18 - 28, prevRect.Top + 12, 24, 24);
        double delHv = isl.Hv("clipprev:del");
        p.Card(delRect, Theme.Mix(Theme.Surface2, Theme.Red, delHv * 0.35), 12, Theme.BorderSubtle);
        p.Label("✕", delRect, 10, Theme.Mix(Theme.Muted, Theme.Red, delHv), true, HAlign.Center);
        isl.AddHit("clipprev:del", delRect);

        // Тонкий разделитель
        p.Line(new Point(prevRect.Left + 18, prevRect.Top + 44),
               new Point(prevRect.Right - 18, prevRect.Top + 44), Theme.BorderSubtle, 1);

        // 2. Тело предпросмотра (Текст с подсветкой / Цвет / Ссылка со скроллом)
        double boxTop = prevRect.Top + 52;
        double boxH = prevRect.Height - 114;
        var boxRect = R(prevRect.Left + 18, boxTop, prevRect.Width - 36, boxH);
        _lastBoxRect = boxRect;
        p.Card(boxRect, Theme.Mix(Theme.Surface, Theme.Surface2, 0.4), 14, Theme.BorderSubtle);

        double prevScroll = isl.Nsp("clip_prev_scroll").X;

        using (p.WithClip(new RectangleGeometry(new Rect(boxRect.Left + 2, boxRect.Top + 2, boxRect.Width - 4, boxRect.Height - 4))))
        {
            // Если это URL — кнопка быстрого перехода
            if (cTag == "URL")
            {
                var openUrlBtn = R(boxRect.Left + 12, boxRect.Top + 10 - prevScroll, boxRect.Width - 24, 30);
                double openHv = isl.Hv("clipprev:open");
                p.Card(openUrlBtn, Theme.Mix(Theme.Surface2, Theme.Accent, 0.2 + 0.3 * openHv), 12, Theme.Accent);
                p.Label(Loc.T("↗  Открыть ссылку в браузере", "↗  Open URL in browser", "↗  Tarayıcıda Bağlantıyı Aç"), openUrlBtn, 11, Colors.White, true, HAlign.Center);
                isl.AddHit("clipprev:open", openUrlBtn);

                // Текст самого URL ниже кнопки
                double textW = boxRect.Width - 28;
                double textH = p.WrappedHeight(curItem.Text.Trim(), 11.5, textW);
                _previewTextHeight = 48 + textH + 20;
                p.Label(curItem.Text.Trim(), R(boxRect.Left + 14, boxRect.Top + 48 - prevScroll, textW, textH + 10), 11.5, Theme.Text, false, HAlign.Left, VAlign.Top, true);
            }
            else if (cTag == "HEX")
            {
                _previewTextHeight = 80;
                // Цветная плашка
                try
                {
                    var hexCol = (Color)ColorConverter.ConvertFromString(curItem.Text.Trim());
                    var swatch = R(boxRect.Left + 14, boxRect.Top + 14 - prevScroll, 52, 52);
                    p.Card(swatch, hexCol, 12, Theme.BorderSubtle);
                    p.Label(curItem.Text.Trim(), R(boxRect.Left + 78, boxRect.Top + 20 - prevScroll, 200, 22), 16, Theme.Text, true);
                    p.Label($"RGB: {hexCol.R}, {hexCol.G}, {hexCol.B}", R(boxRect.Left + 78, boxRect.Top + 42 - prevScroll, 200, 16), 11, Theme.Muted);
                }
                catch { }
            }
            else
            {
                // Обычный или форматированный текст / код с виртуализацией строк (сверхплавный 60fps скролл)
                bool isCode = cTag == "КОД" || cTag == "CODE" || cTag == "KOD";
                double fontSize = isCode ? 11 : 12;
                double textW = boxRect.Width - 28;

                if (_cachedText != curItem.Text)
                {
                    _cachedText = curItem.Text;
                    _cachedLines = curItem.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    if (_cachedLines.Length > 4000)
                    {
                        var truncated = new string[4001];
                        Array.Copy(_cachedLines, truncated, 4000);
                        truncated[4000] = "…";
                        _cachedLines = truncated;
                    }
                    _cachedLineHeight = Math.Round(fontSize * 1.55);
                    _cachedTotalHeight = _cachedLines.Length * _cachedLineHeight;
                }

                double lineH = _cachedLineHeight;
                _previewTextHeight = _cachedTotalHeight + 24;

                // Виртуализация: рендерим ТОЛЬКО те строки, которые попадают в видимую область карточки
                double visibleTop = prevScroll - 16;
                double visibleBottom = prevScroll + boxRect.Height + 16;
                int startLine = Math.Max(0, (int)Math.Floor(visibleTop / lineH));
                int endLine = Math.Min(_cachedLines.Length - 1, (int)Math.Ceiling(visibleBottom / lineH));

                double codeLeft = isCode ? boxRect.Left + 42 : boxRect.Left + 14;
                double codeW = isCode ? textW - 28 : textW;

                for (int lineIdx = startLine; lineIdx <= endLine; lineIdx++)
                {
                    string line = _cachedLines[lineIdx];
                    double lineY = boxRect.Top + 12 + lineIdx * lineH - prevScroll;

                    // Номера строк для кода в стиле Xcode
                    if (isCode)
                    {
                        var numR = R(boxRect.Left + 8, lineY, 26, lineH);
                        p.Label((lineIdx + 1).ToString(), numR, 9.5, Theme.Mix(Theme.Muted, Theme.Surface3, 0.4), false, HAlign.Right, VAlign.Middle);
                    }

                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.Length > 220) line = line[..220] + "…";

                    var lineR = R(codeLeft, lineY, codeW, lineH);
                    p.Label(line, lineR, fontSize, Theme.Text, false, HAlign.Left, VAlign.Middle, false);
                }
            }

            // Изящный индикатор скролла справа
            double maxScroll = Math.Max(0, _previewTextHeight - (boxRect.Height - 16));
            if (maxScroll > 1)
            {
                double trackH = boxRect.Height - 16;
                double thumbH = Math.Max(24, trackH * (boxRect.Height - 16) / _previewTextHeight);
                double thumbY = boxRect.Top + 8 + (trackH - thumbH) * Ui.Clamp(prevScroll / maxScroll);
                var thumbR = R(boxRect.Right - 7, thumbY, 4, thumbH);
                p.Card(thumbR, Theme.Mix(Theme.Surface3, Theme.Text, 0.45), 2);
            }
        }

        // 3. Подвал панели предпросмотра
        bool isFlashing = curItem.Text == _flashText && Clock.Now < _flashUntil;
        var copyBtnRect = R(prevRect.Left + 18, prevRect.Bottom - 44, 200, 32);
        double cpyHv = isl.Hv("clipprev:copy");
        Color cpyBg = isFlashing
            ? Theme.Green
            : Theme.Mix(Theme.Accent, Theme.AccentHot, cpyHv);
        p.Card(copyBtnRect, cpyBg, 14);
        p.Label(isFlashing ? Loc.T("✓  Скопировано!", "✓  Copied!", "✓  Kopyalandı!") : Loc.T("📋  Скопировать (Enter)", "📋  Copy to Clipboard", "📋  Panoya Kopyala"),
            copyBtnRect, 11, Colors.White, true, HAlign.Center);
        isl.AddHit("clipprev:copy", copyBtnRect);

        // Статистика справа внизу
        int lineCount = curItem.Text.Count(c => c == '\n') + 1;
        p.Label(Loc.T($"{curItem.Text.Length} симв.  ·  {lineCount} стр.",
                      $"{curItem.Text.Length} chars  ·  {lineCount} lines",
                      $"{curItem.Text.Length} krk.  ·  {lineCount} satır"),
            R(prevRect.Right - 18 - 180, prevRect.Bottom - 42, 180, 26), 10.5, Theme.Muted, false, HAlign.Right);
    }

    private void Row(IslandView isl, Painter p, int i, ClipItem item, Rect r, Rect area, bool isSelected)
    {
        string key = $"clip:{i}";
        var visible = Rect.Intersect(r, area);
        if (!visible.IsEmpty) isl.AddHit(key, visible);

        bool flashing = item.Text == _flashText && Clock.Now < _flashUntil;
        using (p.Pressable(key, r))
        {
            var fill = isSelected
                ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.22)
                : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv(key) * 0.45);
            if (flashing) fill = Theme.Mix(Theme.Surface, Theme.Accent, 0.4);

            p.Card(r, fill, 13, isSelected ? Theme.Accent : (item.Pinned ? Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.5) : Theme.BorderSubtle));

            var (tag, tagBg, tagFg) = DetectClipType(item.Text);

            // Бейдж типа контента
            var badgeR = R(r.Left + 10, r.Top + 10, 32, 16);
            p.Card(badgeR, tagBg, 4);
            p.Label(tag, badgeR, 8.0, tagFg, true, HAlign.Center);

            double tx = r.Left + 48, tw = r.Width - 92;
            if (flashing)
            {
                p.Label(Loc.T("✓ Скопировано", "✓ Copied", "✓ Kopyalandı"), R(tx, r.Top, tw, r.Height), 11.5, Theme.AccentHot, true);
            }
            else
            {
                string first = item.Text.ReplaceLineEndings("\n").Trim().Split('\n')[0].Trim();
                if (first.Length == 0) first = "…";
                p.Label(first, R(tx, r.Top + 6, tw, 18), 11.5, isSelected ? Colors.White : Theme.Text, true);

                string when = Ago(item.Time);
                p.Label(Loc.T($"{when}  ·  {item.Text.Length} с.", $"{when}  ·  {item.Text.Length} ch.", $"{when}  ·  {item.Text.Length} krk."), R(tx, r.Top + 24, tw, 15), 9.5, Theme.Muted);
            }

            // Иконка пина
            if (item.Pinned)
            {
                p.Label("📌", R(r.Right - 28, r.Top + r.Height / 2 - 8, 16, 16), 10, Theme.AccentHot, false, HAlign.Center);
            }
        }
    }

    private void ScrollBar(Painter p, Rect area, double scroll, int count, double listH)
    {
        double mx = MaxScroll(count, listH);
        if (mx <= 0) return;
        double thumb = Math.Max(28.0, listH * listH / (listH + mx));
        double y = area.Top + (listH - thumb) * Ui.Clamp(scroll / mx);
        p.Card(R(area.Right - 4, y, 3, thumb), Theme.Mix(Theme.Surface3, Theme.Muted, 0.5), 1.5);
    }

    public override void Click(IslandView isl, string key)
    {
        if (key.StartsWith("clipflt:"))
        {
            _filter = key["clipflt:".Length..];
            _selectedIndex = 0;
            var spPrv = isl.Nsp("clip_prev_scroll");
            spPrv.X = 0;
            spPrv.Target = 0;
            isl.Kick();
            return;
        }

        if (key == "clip:clear")
        {
            _items.RemoveAll(i => !i.Pinned);
            _selectedIndex = 0;
            isl.Kick();
            return;
        }

        if (key == "clipprev:copy")
        {
            var filtered = _items.Where(MatchesFilter).ToList();
            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                Copy(isl, filtered[_selectedIndex]);
            }
            return;
        }

        if (key == "clipprev:open")
        {
            var filtered = _items.Where(MatchesFilter).ToList();
            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                try { Process.Start(new ProcessStartInfo(filtered[_selectedIndex].Text.Trim()) { UseShellExecute = true }); } catch { }
            }
            return;
        }

        if (key == "clipprev:pin")
        {
            var filtered = _items.Where(MatchesFilter).ToList();
            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                var cur = filtered[_selectedIndex];
                cur.Pinned = !cur.Pinned;
                var sorted = _items.OrderBy(i => i.Pinned ? 0 : 1).ToList();
                _items.Clear();
                _items.AddRange(sorted);
                Save(isl);
                isl.Kick();
            }
            return;
        }

        if (key == "clipprev:del")
        {
            var filtered = _items.Where(MatchesFilter).ToList();
            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                var cur = filtered[_selectedIndex];
                _items.Remove(cur);
                if (cur.Pinned) Save(isl);
                if (_selectedIndex >= _items.Count) _selectedIndex = Math.Max(0, _items.Count - 1);
                isl.Kick();
            }
            return;
        }

        string[] parts = key.Split(':');
        if (int.TryParse(parts[^1], out int idx))
        {
            var filtered = _items.Where(MatchesFilter).ToList();
            if (idx >= 0 && idx < filtered.Count)
            {
                _selectedIndex = idx;
                var spPrv = isl.Nsp("clip_prev_scroll");
                spPrv.X = 0;
                spPrv.Target = 0;
                Copy(isl, filtered[idx]);
            }
        }
    }

    private void Copy(IslandView isl, ClipItem item)
    {
        _own = item.Text;
        try { System.Windows.Clipboard.SetText(item.Text); }
        catch
        {
            isl.RunLater(25, () => { try { System.Windows.Clipboard.SetText(item.Text); } catch { } });
        }
        _flashText = item.Text;
        _flashUntil = Clock.Now + 1.1;
        isl.RunLater(1150, isl.Kick);
    }

    public override void Wheel(IslandView isl, double notches)
    {
        Point mousePos = Mouse.GetPosition(isl);
        if (_lastBoxRect.Width > 0 && _lastBoxRect.Contains(mousePos))
        {
            double maxScroll = Math.Max(0, _previewTextHeight - (_lastBoxRect.Height - 16));
            if (maxScroll > 0)
            {
                var spPrv = isl.Nsp("clip_prev_scroll");
                spPrv.Target = Ui.Clamp(spPrv.Target - notches * 68, 0.0, maxScroll);
                isl.Kick();
            }
            return;
        }

        var filtered = _items.Where(MatchesFilter).ToList();
        if (filtered.Count == 0) return;
        var spList = isl.Nsp("clip_scroll");
        double listH = 396 - 64;
        spList.Target = Ui.Clamp(spList.Target - notches * Step, 0.0, MaxScroll(filtered.Count, listH));
        isl.Kick();
    }
}

// ------------------------------------------------------------------ Погода

public sealed class WeatherSection : Section
{
    private const double Ttl = 1800;         // погода считается свежей 30 минут

    public WeatherData? Data;
    public string? Error;
    public bool Loading;
    private double _loadedAt = -1e9;

    public WeatherSection() { Key = "weather"; Title = "Погода"; }

    /// <summary>Выбранный город или null, если пользователь ещё не выбирал.</summary>
    public static string? SelectedCity(IslandView isl) =>
        string.IsNullOrWhiteSpace(isl.S.WeatherCity) ? null : isl.S.WeatherCity.Trim();

    public override void Attach(IslandView isl) => isl.RunLater(150, () => Ensure(isl));
    public override void Enter(IslandView isl) => Ensure(isl);

    public void Invalidate(IslandView isl)
    {
        _loadedAt = -1e9;
        Data = null;
        Ensure(isl);
    }

    /// <summary>Подгружает погоду, если город выбран, а данных нет или они старше 30 минут.</summary>
    public void Ensure(IslandView isl)
    {
        string? city = SelectedCity(isl);
        if (city == null || Loading) return;
        double age = Clock.Now - _loadedAt;
        if (Data == null && Error != null && age < 120) return;       // после ошибки не долбим сеть чаще раза в 2 минуты
        if (Data == null || age > Ttl) Load(isl, city);
    }

    private void Load(IslandView isl, string city)
    {
        if (isl.S.WeatherCity != city)
        {
            isl.S.WeatherCity = city;
            isl.SaveState();
        }
        Error = null;
        Loading = true;
        _loadedAt = Clock.Now;
        Bg.RunAsync(() => SystemService.FetchWeatherAsync(city), (res, err) =>
        {
            Loading = false;
            if (err != null)
            {
                Data = null;
                Error = string.IsNullOrEmpty(err.Message) ? "Не удалось получить погоду." : err.Message;
            }
            else
            {
                Data = res;
            }
            isl.Kick();
        });
        isl.Kick();
    }

    public override void Paint(IslandView isl, Painter p, Rect rect)
    {
        string? city = SelectedCity(isl);
        if (Loading)
        {
            var box = rect;
            p.Card(box, Theme.Surface, 24);
            p.Label(Loc.T("Загружаю прогноз погоды…", "Loading weather forecast…", "Hava durumu tahmini yükleniyor…"), R(box.Left + 28, box.Top + box.Height / 2 - 14, box.Width - 56, 28), 13, Theme.Muted, false, HAlign.Center);
            return;
        }

        if (Error != null)
        {
            var box = rect;
            p.Card(box, Theme.Surface, 24);
            p.Label(city ?? Loc.T("Город", "City", "Şehir"), R(box.Left + 28, box.Top + 24, 300, 26), 16, Theme.Text, true);
            p.Note(R(box.Left + 28, box.Top + 64, box.Width - 56, 54), Error, Theme.Red, 11);
            p.Button("weather:refresh", R(box.Left + 28, box.Top + 130, 130, 36), Loc.T("Повторить", "Retry", "Tekrar dene"), true);
            p.Button("weather:city", R(box.Left + 168, box.Top + 130, 160, 36), Loc.T("Сменить город", "Change City", "Şehri Değiştir"));
            return;
        }

        if (Data == null || string.IsNullOrWhiteSpace(city))
        {
            var box = rect;
            p.Card(box, Theme.Surface, 24);
            p.Label(Loc.T("Город не выбран", "City Not Selected", "Şehir Seçilmedi"), R(box.Left + 28, box.Top + 40, box.Width - 56, 28), 16, Theme.Text, true, HAlign.Center);
            p.Label(Loc.T("Выберите ваш город, чтобы видеть погоду в Dynamic Island", "Select your city to view weather in Dynamic Island", "Dynamic Island'da hava durumunu görmek için şehrinizi seçin"),
                R(box.Left + 28, box.Top + 74, box.Width - 56, 22), 11.5, Theme.Muted, false, HAlign.Center);
            p.Button("weather:city", R(box.Left + (box.Width - 180) / 2, box.Top + 116, 180, 40), Loc.T("Выбрать город", "Select City", "Şehir Seç"), true);
            return;
        }

        var d = Data;
        double gap = 12;
        double wLeft = 360;
        double wRight = Math.Max(280, rect.Width - wLeft - gap);

        // ==================== ЛЕВАЯ КОЛОНКА ====================
        double leftX = rect.Left, topY = rect.Top;

        // 1. Главная карточка текущей погоды (h = 154)
        var curBox = R(leftX, topY, wLeft, 154);
        p.Card(curBox, Theme.Surface, 20, Theme.BorderSubtle);

        // Город и кнопки
        p.Label($"📍 {d.City}", R(curBox.Left + 18, curBox.Top + 14, curBox.Width - 140, 24), 15, Theme.Text, true);
        p.Button("weather:refresh", R(curBox.Right - 18 - 84, curBox.Top + 12, 38, 26), "🔄", false, 11);
        p.Button("weather:city", R(curBox.Right - 18 - 42, curBox.Top + 12, 42, 26), "🏙️", false, 11);

        // Температура и большая векторная иконка
        string sign = d.Temp > 0 ? "+" : "";
        string tempText = $"{sign}{Math.Round(d.Temp)}°";
        p.Label(tempText, R(curBox.Left + 18, curBox.Top + 44, 150, 56), 46, Theme.Text, true);
        p.WeatherIcon(d.Code, new Point(curBox.Right - 54, curBox.Top + 72), 22);

        // Описание и доп инфо
        p.Label(d.Description, R(curBox.Left + 18, curBox.Top + 102, curBox.Width - 36, 22), 13, Theme.AccentHot, true);
        string minMaxStr = "";
        if (d.Daily.Count > 0)
        {
            var day0 = d.Daily[0];
            minMaxStr = $"   ·   ↓{Math.Round(day0.TempMin)}° ↑{Math.Round(day0.TempMax)}°";
        }
        p.Label($"{Loc.T("Ощущается как", "Feels like", "Hissedilen")} {Math.Round(d.Apparent)}°{minMaxStr}",
            R(curBox.Left + 18, curBox.Top + 125, curBox.Width - 36, 18), 10.5, Theme.Muted);

        // 2. Сетка 4 параметров (2x2, h = 134)
        double gridY = topY + 164;
        double cellW = (wLeft - 10) / 2, cellH = 65;

        // Ячейка 1: Ветер
        var cellWind = R(leftX, gridY, cellW, cellH);
        p.Card(cellWind, Theme.Surface, 16, Theme.BorderSubtle);
        p.Label(Loc.T("💨 ВЕТЕР", "💨 WIND", "💨 RÜZGAR"), R(cellWind.Left + 14, cellWind.Top + 10, cellW - 28, 16), 9.5, Theme.Muted, true);
        p.Label($"{d.WindSpeed / 3.6:F1} {Loc.T("м/с", "m/s", "m/sn")}", R(cellWind.Left + 14, cellWind.Top + 30, cellW - 28, 24), 14, Theme.Text, true);

        // Ячейка 2: Влажность
        var cellHum = R(leftX + cellW + 10, gridY, cellW, cellH);
        p.Card(cellHum, Theme.Surface, 16, Theme.BorderSubtle);
        p.Label(Loc.T("💧 ВЛАЖНОСТЬ", "💧 HUMIDITY", "💧 NEM"), R(cellHum.Left + 14, cellHum.Top + 10, cellW - 28, 16), 9.5, Theme.Muted, true);
        p.Label($"{d.Humidity}%", R(cellHum.Left + 14, cellHum.Top + 30, cellW - 28, 24), 14, Theme.Text, true);

        // Ячейка 3: Давление
        var cellPres = R(leftX, gridY + cellH + 8, cellW, cellH);
        p.Card(cellPres, Theme.Surface, 16, Theme.BorderSubtle);
        p.Label(Loc.T("🧭 ДАВЛЕНИЕ", "🧭 PRESSURE", "🧭 BASINÇ"), R(cellPres.Left + 14, cellPres.Top + 10, cellW - 28, 16), 9.5, Theme.Muted, true);
        p.Label($"{Math.Round(d.Pressure * 0.750062)} {Loc.T("мм", "mmHg", "hPa")}", R(cellPres.Left + 14, cellPres.Top + 30, cellW - 28, 24), 14, Theme.Text, true);

        // Ячейка 4: Солнце (Восход / Закат)
        var cellSun = R(leftX + cellW + 10, gridY + cellH + 8, cellW, cellH);
        p.Card(cellSun, Theme.Surface, 16, Theme.BorderSubtle);
        p.Label(Loc.T("🌅 РАССВЕТ / ЗАКАТ", "🌅 SUNRISE / SUNSET", "🌅 GÜN DOĞUMU / BATIŞI"), R(cellSun.Left + 14, cellSun.Top + 10, cellW - 28, 16), 9.5, Theme.Muted, true);
        string sunStr = !string.IsNullOrEmpty(d.Sunrise) ? $"{d.Sunrise} / {d.Sunset}" : "—";
        p.Label(sunStr, R(cellSun.Left + 14, cellSun.Top + 30, cellW - 28, 24), 13.5, Theme.Text, true);

        // ==================== ПРАВАЯ КОЛОНКА ====================
        double rightX = leftX + wLeft + gap;

        // 1. Почасовой прогноз (h = 92)
        var hourBox = R(rightX, topY, wRight, 92);
        p.Card(hourBox, Theme.Surface, 20, Theme.BorderSubtle);
        p.Label(Loc.T("ПОЧАСОВОЙ ПРОГНОЗ", "HOURLY FORECAST", "SAATLİK TAHMİN"), R(hourBox.Left + 16, hourBox.Top + 10, 200, 16), 9.5, Theme.Muted, true);

        if (d.Hourly.Count > 0)
        {
            int hCount = Math.Min(d.Hourly.Count, 7);
            double colW = (hourBox.Width - 28) / hCount;
            for (int i = 0; i < hCount; i++)
            {
                var h = d.Hourly[i];
                double hX = hourBox.Left + 14 + i * colW;
                var hRect = R(hX, hourBox.Top + 28, colW, 56);

                p.Label(h.Time, R(hRect.Left, hRect.Top, hRect.Width, 14), 9.5, Theme.Muted, false, HAlign.Center);
                p.WeatherIcon(h.Code, new Point(hRect.Left + hRect.Width / 2, hRect.Top + 24), 7.5);
                string hSign = h.Temp > 0 ? "+" : "";
                p.Label($"{hSign}{Math.Round(h.Temp)}°", R(hRect.Left, hRect.Top + 36, hRect.Width, 16), 11, Theme.Text, true, HAlign.Center);
            }
        }

        // 2. Прогноз на 6 дней (h = 196)
        double dayBoxY = topY + 102;
        var dayBox = R(rightX, dayBoxY, wRight, 200);
        p.Card(dayBox, Theme.Surface, 20, Theme.BorderSubtle);
        p.Label(Loc.T("ПРОГНОЗ НА 6 ДНЕЙ", "6-DAY FORECAST", "6 GÜNLÜK TAHMİN"), R(dayBox.Left + 16, dayBox.Top + 10, 200, 16), 9.5, Theme.Muted, true);

        if (d.Daily.Count > 0)
        {
            double globalMin = d.Daily.Min(x => x.TempMin);
            double globalMax = d.Daily.Max(x => x.TempMax);
            double span = Math.Max(1.0, globalMax - globalMin);

            double rowH = 27;
            double startRowY = dayBox.Top + 30;

            for (int i = 0; i < d.Daily.Count; i++)
            {
                var day = d.Daily[i];
                double rY = startRowY + i * rowH;

                // Название дня
                p.Label(day.DayName, R(dayBox.Left + 16, rY + 4, 64, 18), 11, i == 0 ? Theme.Text : Theme.Muted, i == 0);

                // Векторная иконка
                p.WeatherIcon(day.Code, new Point(dayBox.Left + 94, rY + 12), 7.5);

                // Осадки (если есть)
                if (day.Pop > 10)
                {
                    p.Label($"{day.Pop}%", R(dayBox.Left + 108, rY + 4, 38, 18), 9.5, Theme.C("#38BDF8"), true);
                }

                // Минимальная температура
                p.Label($"{Math.Round(day.TempMin)}°", R(dayBox.Left + 148, rY + 4, 32, 18), 11, Theme.Muted, false, HAlign.Right);

                // Apple-style температурная полоска диапазона
                double barX = dayBox.Left + 188;
                double barW = Math.Max(60, dayBox.Width - 188 - 56);
                var trackR = R(barX, rY + 10, barW, 5);
                p.Card(trackR, Theme.Surface2, 2.5);

                double fMin = Ui.Clamp((day.TempMin - globalMin) / span);
                double fMax = Ui.Clamp((day.TempMax - globalMin) / span);
                double fillX = barX + fMin * barW;
                double fillW = Math.Max(6.0, (fMax - fMin) * barW);
                var fillR = R(fillX, rY + 10, fillW, 5);
                p.Card(fillR, Theme.Mix(Theme.C("#38BDF8"), Theme.C("#F59E0B"), (fMin + fMax) / 2.0), 2.5);

                // Максимальная температура
                p.Label($"{Math.Round(day.TempMax)}°", R(dayBox.Right - 46, rY + 4, 32, 18), 11, Theme.Text, true, HAlign.Left);
            }
        }
    }

    public override void Click(IslandView isl, string key)
    {
        if (key == "weather:refresh")
        {
            if (Loading) return;
            string? city = SelectedCity(isl);
            if (city != null) Load(isl, city);
            else ChooseCity(isl);
            return;
        }
        if (key != "weather:city" || Loading) return;
        ChooseCity(isl);
    }

    private void ChooseCity(IslandView isl)
    {
        string? city = isl.Ask(Loc.T("Погода", "Weather", "Hava Durumu"), Loc.T("Введите город:", "Enter city:", "Şehir girin:"), SelectedCity(isl) ?? Loc.T("Москва", "Moscow", "İstanbul"));
        if (string.IsNullOrWhiteSpace(city)) return;
        Load(isl, city.Trim());
    }
}

// ------------------------------------------------------------------ Календарь (Apple macOS Calendar)

public sealed class CalendarSection : Section
{
    private DateTime _viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDay = DateTime.Today;
    private List<CalendarNote> _notes = new();

    public CalendarSection()
    {
        Key = "calendar";
        Title = "Календарь";
    }

    public override void Attach(IslandView isl)
    {
        _notes = StoreService.LoadCalendarNotes();
    }

    public override void Enter(IslandView isl)
    {
        _selectedDay = DateTime.Today;
        _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _notes = StoreService.LoadCalendarNotes();
    }

    public override void Paint(IslandView isl, Painter p, Rect rect)
    {
        const double gap = 14;
        double leftW = 420;
        double rightW = rect.Width - leftW - gap;

        var leftRect = R(rect.Left, rect.Top, leftW, rect.Height);
        var rightRect = R(rect.Left + leftW + gap, rect.Top, rightW, rect.Height);

        PaintCalendarGrid(isl, p, leftRect);
        PaintDayAgenda(isl, p, rightRect);
    }

    private void PaintCalendarGrid(IslandView isl, Painter p, Rect r)
    {
        p.Card(r, Theme.Surface, 20, Theme.BorderSubtle);

        // 1. Шапка месяца и навигация
        string monthTitle = _viewMonth.ToString("MMMM yyyy", Loc.Culture);
        monthTitle = char.ToUpper(monthTitle[0]) + monthTitle[1..];
        p.Label(monthTitle, R(r.Left + 18, r.Top + 14, 220, 24), 16, Theme.Text, true);

        // Кнопки навигации справа: [ ‹ ] [ Сегодня ] [ › ]
        double btnY = r.Top + 14;
        var prevBtn = R(r.Right - 18 - 26 - 74 - 26, btnY, 24, 24);
        var todayBtn = R(r.Right - 18 - 26 - 74, btnY, 70, 24);
        var nextBtn = R(r.Right - 18 - 24, btnY, 24, 24);

        double prevHv = isl.Hv("cal:prevmonth");
        p.Card(prevBtn, Theme.Mix(Theme.Surface2, Theme.Surface3, prevHv * 0.5), 12, Theme.BorderSubtle);
        p.Label("‹", prevBtn, 14, Theme.Text, true, HAlign.Center);
        isl.AddHit("cal:prevmonth", prevBtn);

        double tdHv = isl.Hv("cal:today");
        p.Card(todayBtn, Theme.Mix(Theme.Surface2, Theme.Accent, 0.2 + 0.3 * tdHv), 12,
            _selectedDay.Date == DateTime.Today ? Theme.Accent : Theme.BorderSubtle);
        p.Label(Loc.T("Сегодня", "Today", "Bugün"), todayBtn, 10.5, Theme.Text, true, HAlign.Center);
        isl.AddHit("cal:today", todayBtn);

        double nextHv = isl.Hv("cal:nextmonth");
        p.Card(nextBtn, Theme.Mix(Theme.Surface2, Theme.Surface3, nextHv * 0.5), 12, Theme.BorderSubtle);
        p.Label("›", nextBtn, 14, Theme.Text, true, HAlign.Center);
        isl.AddHit("cal:nextmonth", nextBtn);

        // Тонкий разделитель
        p.Line(new Point(r.Left + 16, r.Top + 46), new Point(r.Right - 16, r.Top + 46), Theme.BorderSubtle, 1);

        // 2. Дни недели
        string[] daysOfWeek = Loc.Lang switch
        {
            "ru" => new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" },
            "tr" => new[] { "Pt", "Sa", "Ça", "Pe", "Cu", "Ct", "Pz" },
            _ => new[] { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" }
        };

        double gridLeft = r.Left + 14;
        double gridW = r.Width - 28;
        double colW = gridW / 7;

        double dY = r.Top + 54;
        for (int i = 0; i < 7; i++)
        {
            var dwR = R(gridLeft + i * colW, dY, colW, 18);
            bool isWeekend = i >= 5;
            p.Label(daysOfWeek[i], dwR, 10.5, isWeekend ? Theme.Mix(Theme.Muted, Theme.Red, 0.4) : Theme.Muted, true, HAlign.Center);
        }

        // 3. Сетка дней
        DateTime firstDayOfMonth = new(_viewMonth.Year, _viewMonth.Month, 1);
        int dayOfWeekOffset = ((int)firstDayOfMonth.DayOfWeek + 6) % 7; // Пн = 0 .. Вс = 6
        DateTime startDate = firstDayOfMonth.AddDays(-dayOfWeekOffset);

        double startGridY = r.Top + 76;
        double cellH = 46;

        for (int i = 0; i < 42; i++)
        {
            int row = i / 7;
            int col = i % 7;
            if (row >= 6) break;

            DateTime cellDate = startDate.AddDays(i);
            var cellR = R(gridLeft + col * colW, startGridY + row * cellH, colW, cellH);

            bool isToday = cellDate.Date == DateTime.Today;
            bool isSelected = cellDate.Date == _selectedDay.Date;
            bool isCurMonth = cellDate.Month == _viewMonth.Month;
            bool isWeekend = col >= 5;
            string dateStrKey = cellDate.ToString("yyyy-MM-dd");
            bool hasNotes = _notes.Any(n => n.Date == dateStrKey);

            string hitKey = $"calday:{dateStrKey}";
            double hot = isl.Hv(hitKey);

            var centerP = new Point(cellR.Left + cellR.Width / 2, cellR.Top + cellR.Height / 2 - 2);

            using (p.Pressable(hitKey, cellR))
            {
                if (isToday)
                {
                    // Сегодня: яркий залитый кружок Apple Accent
                    p.Circle(centerP, 16, Theme.Accent);
                    if (isSelected)
                    {
                        p.Circle(centerP, 19, Color.FromArgb(60, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B));
                    }
                }
                else if (isSelected)
                {
                    // Выбранный день: обводка кружка
                    p.Circle(centerP, 16, Theme.Mix(Theme.Surface2, Theme.Surface3, 0.8));
                    p.Dc.DrawEllipse(null, p.P(Theme.Accent, 1.8), centerP, 17, 17);
                }
                else if (hot > 0.05)
                {
                    // Ховер кружок
                    p.Circle(centerP, 16, Theme.Mix(Theme.Surface2, Theme.Surface3, hot * 0.4));
                }

                Color textCol;
                if (isToday) textCol = Colors.White;
                else if (isSelected) textCol = Theme.AccentHot;
                else if (!isCurMonth) textCol = Theme.Mix(Theme.Muted, Theme.Surface3, 0.45);
                else if (isWeekend) textCol = Theme.Mix(Theme.Text, Theme.Red, 0.35);
                else textCol = Theme.Text;

                var textR = R(cellR.Left, centerP.Y - 10, cellR.Width, 20);
                p.Label(cellDate.Day.ToString(), textR, 12, textCol, isToday || isSelected, HAlign.Center);

                // Точка наличия задачи
                if (hasNotes)
                {
                    Color dotCol = isToday ? Colors.White : Theme.AccentHot;
                    p.Circle(new Point(centerP.X, centerP.Y + 11), 2.2, dotCol);
                }
            }
            isl.AddHit(hitKey, cellR);
        }
    }

    private void PaintDayAgenda(IslandView isl, Painter p, Rect r)
    {
        p.Card(r, Theme.Surface, 20, Theme.BorderSubtle);

        // 1. Шапка выбранного дня
        string dayTitle = _selectedDay.ToString(Loc.IsEn ? "dddd, MMMM d" : "d MMMM, dddd", Loc.Culture);
        dayTitle = char.ToUpper(dayTitle[0]) + dayTitle[1..];
        p.Label(dayTitle, R(r.Left + 18, r.Top + 14, r.Width - 140, 20), 14, Theme.Text, true);

        string relativeStatus = _selectedDay.Date == DateTime.Today
            ? Loc.T("Сегодня", "Today", "Bugün")
            : _selectedDay.Date == DateTime.Today.AddDays(1)
                ? Loc.T("Завтра", "Tomorrow", "Yarın")
                : _selectedDay.Date == DateTime.Today.AddDays(-1)
                    ? Loc.T("Вчера", "Yesterday", "Dün")
                    : Loc.T($"{(_selectedDay - DateTime.Today).Days} дн.", $"{(_selectedDay - DateTime.Today).Days}d", $"{(_selectedDay - DateTime.Today).Days} gün");

        p.Label(relativeStatus, R(r.Left + 18, r.Top + 33, 100, 16), 10, Theme.AccentHot, true);

        // Кнопка добавления задачи
        var addBtn = R(r.Right - 18 - 85, r.Top + 14, 85, 26);
        double addHv = isl.Hv("cal:addnote");
        p.Card(addBtn, Theme.Mix(Theme.Surface2, Theme.Accent, 0.2 + 0.3 * addHv), 13, Theme.Accent);
        p.Label(Loc.T("+  Задача", "+  Task", "+  Görev"), addBtn, 10.5, Colors.White, true, HAlign.Center);
        isl.AddHit("cal:addnote", addBtn);

        // Тонкий разделитель
        p.Line(new Point(r.Left + 16, r.Top + 54), new Point(r.Right - 16, r.Top + 54), Theme.BorderSubtle, 1);

        // 2. Список задач на выбранный день
        string dateKey = _selectedDay.ToString("yyyy-MM-dd");
        var dayNotes = _notes.Where(n => n.Date == dateKey).ToList();

        double listTop = r.Top + 62;
        double listH = r.Height - 118;
        var listRect = R(r.Left + 16, listTop, r.Width - 32, listH);

        if (dayNotes.Count == 0)
        {
            var iconP = new Point(listRect.Left + listRect.Width / 2, listRect.Top + 45);
            p.Circle(iconP, 24, Theme.Surface2);
            p.Label("🗓", R(iconP.X - 14, iconP.Y - 14, 28, 28), 16, Theme.Muted, false, HAlign.Center);

            p.Label(Loc.T("На этот день нет задач", "No tasks for this day", "Bu gün için görev yok"),
                R(listRect.Left, iconP.Y + 34, listRect.Width, 20), 12.5, Theme.Text, true, HAlign.Center);
            p.Label(Loc.T("Нажмите «+ Задача», чтобы добавить напоминание.", "Click '+ Task' to add a reminder.", "Hatırlatıcı eklemek için «+ Görev» butonuna tıklayın."),
                R(listRect.Left, iconP.Y + 54, listRect.Width, 18), 10.5, Theme.Muted, false, HAlign.Center);
        }
        else
        {
            using (p.WithClip(new RectangleGeometry(listRect)))
            {
                double rowH = 40;
                for (int i = 0; i < dayNotes.Count; i++)
                {
                    double ny = listRect.Top + i * (rowH + 6);
                    if (ny + rowH > listRect.Bottom) break;
                    var note = dayNotes[i];
                    var itemR = R(listRect.Left, ny, listRect.Width, rowH);

                    string togKey = $"caltoggle:{note.Id}";
                    string delKey = $"caldel:{note.Id}";
                    double itemHv = isl.Hv(togKey);

                    p.Card(itemR, Theme.Mix(Theme.Surface2, Theme.Surface3, 0.4 + 0.3 * itemHv), 12,
                        note.Done ? Theme.BorderSubtle : Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.4));

                    // Чекбокс слева
                    var checkC = new Point(itemR.Left + 20, itemR.Top + itemR.Height / 2);
                    p.Circle(checkC, 9, note.Done ? Theme.Accent : Theme.Surface);
                    if (note.Done)
                    {
                        p.Label("✓", R(checkC.X - 6, checkC.Y - 8, 12, 16), 9.5, Colors.White, true, HAlign.Center);
                    }
                    else
                    {
                        p.Dc.DrawEllipse(null, p.P(Theme.Mix(Theme.Muted, Theme.Text, 0.4), 1.4), checkC, 9, 9);
                    }
                    isl.AddHit(togKey, new Rect(itemR.Left, itemR.Top, itemR.Width - 36, itemR.Height));

                    // Текст задачи
                    double tx = itemR.Left + 38;
                    double tw = itemR.Width - 76;
                    Color textCol = note.Done ? Theme.Muted : Theme.Text;
                    p.Label(note.Text, R(tx, itemR.Top + 10, tw, 20), 11.5, textCol, !note.Done);

                    // Крестик удаления
                    p.CloseDot(delKey, new Point(itemR.Right - 16, itemR.Top + itemR.Height / 2), 8.5);
                }
            }
        }

        // 3. Подвал: статистика года
        p.Line(new Point(r.Left + 16, r.Bottom - 44), new Point(r.Right - 16, r.Bottom - 44), Theme.BorderSubtle, 1);

        int dayOfYear = _selectedDay.DayOfYear;
        int totalDays = DateTime.IsLeapYear(_selectedDay.Year) ? 366 : 365;
        int daysLeft = totalDays - dayOfYear;
        p.Label(Loc.T($"День {dayOfYear} из {totalDays}  ·  До Нового года: {daysLeft} дн.",
                      $"Day {dayOfYear} of {totalDays}  ·  {daysLeft} days to New Year",
                      $"Gün {dayOfYear} / {totalDays}  ·  Yılbaşına: {daysLeft} gün"),
            R(r.Left + 16, r.Bottom - 32, r.Width - 32, 20), 10, Theme.Muted, false, HAlign.Center);
    }

    public override void Click(IslandView isl, string key)
    {
        if (key == "cal:prevmonth")
        {
            _viewMonth = _viewMonth.AddMonths(-1);
            isl.Kick();
            return;
        }
        if (key == "cal:nextmonth")
        {
            _viewMonth = _viewMonth.AddMonths(1);
            isl.Kick();
            return;
        }
        if (key == "cal:today")
        {
            _selectedDay = DateTime.Today;
            _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            isl.Kick();
            return;
        }
        if (key.StartsWith("calday:"))
        {
            string dateStr = key["calday:".Length..];
            if (DateTime.TryParse(dateStr, out var d))
            {
                _selectedDay = d;
                isl.Kick();
            }
            return;
        }
        if (key == "cal:addnote")
        {
            string dateStr = _selectedDay.ToString("d MMMM", Loc.Culture);
            string? text = isl.Ask(Loc.T("Новая задача", "New task", "Yeni Görev"), Loc.T($"Напоминание на {dateStr}:", $"Reminder for {dateStr}:", $"{dateStr} için hatırlatıcı:"));
            if (!string.IsNullOrWhiteSpace(text))
            {
                _notes.Add(new CalendarNote
                {
                    Date = _selectedDay.ToString("yyyy-MM-dd"),
                    Text = text.Trim(),
                    Done = false
                });
                StoreService.SaveCalendarNotes(_notes);
                isl.Kick();
            }
            return;
        }
        if (key.StartsWith("caltoggle:"))
        {
            string id = key["caltoggle:".Length..];
            var n = _notes.FirstOrDefault(x => x.Id == id);
            if (n != null)
            {
                n.Done = !n.Done;
                StoreService.SaveCalendarNotes(_notes);
                isl.Kick();
            }
            return;
        }
        if (key.StartsWith("caldel:"))
        {
            string id = key["caldel:".Length..];
            _notes.RemoveAll(x => x.Id == id);
            StoreService.SaveCalendarNotes(_notes);
            isl.Kick();
            return;
        }
    }
}


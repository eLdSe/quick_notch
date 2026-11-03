using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuickNotchCs;

public partial class SettingsView : UserControl
{
    private IslandView? _island;
    private bool _loading = false;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void Attach(IslandView island)
    {
        _island = island;
        RefreshAll();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _island?.StopLeaveTimer();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _island?.StopLeaveTimer();
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        _island?.StopLeaveTimer();
    }

    public void RefreshAll()
    {
        if (_island == null) return;
        _loading = true;
        try
        {
            var s = _island.S;

            // 1. Тема острова
            if (ThemeDarkRadio != null) ThemeDarkRadio.IsChecked = s.ThemeMode == "dark";
            if (ThemeLightRadio != null) ThemeLightRadio.IsChecked = s.ThemeMode == "light";
            if (ThemeAutoRadio != null) ThemeAutoRadio.IsChecked = s.ThemeMode == "auto";

            // 2. Палитра акцентных цветов
            BuildAccentPalette();

            // 3. Эффект стекла
            if (GlassCheckBox != null) GlassCheckBox.IsChecked = s.GlassEnabled;

            // 4. Робот-компаньон Aero
            if (RobotCompanionCheckBox != null) RobotCompanionCheckBox.IsChecked = s.RobotCompanion;
            if (RoamButton != null) RoamButton.Content = s.RobotRoaming ? "🏠 Вернуть в островок" : "🚀 На рабочий стол";

            // 5. Задержка скрытия
            if (DelaySlider != null) DelaySlider.Value = s.LeaveDelay;
            if (DelayValueText != null) DelayValueText.Text = $"{s.LeaveDelay:F1} {Loc.T("сек", "sec", "sn")}";

            // 6. Автозапуск и язык
            if (AutostartCheckBox != null) AutostartCheckBox.IsChecked = _island.Autostart;
            if (LangRuRadio != null) LangRuRadio.IsChecked = s.Language == "ru";
            if (LangEnRadio != null) LangEnRadio.IsChecked = s.Language == "en";
            if (LangTrRadio != null) LangTrRadio.IsChecked = s.Language == "tr";

            // 7. Фон острова
            RefreshBackgroundCard();

            // 8. Локализация всех надписей
            RefreshLabels();
        }
        finally
        {
            _loading = false;
        }
    }

    public void BuildAccentPalette()
    {
        if (AccentColorsPanel == null) return;
        AccentColorsPanel.Children.Clear();
        for (int i = 0; i < Theme.Accents.Length; i++)
        {
            var (key, mainColorHex, name) = Theme.Accents[i];
            bool isCurrent = Theme.CurrentAccent == key;

            var border = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(4),
                Cursor = Cursors.Hand,
                ToolTip = name,
                Tag = key,
                BorderThickness = new Thickness(isCurrent ? 2 : 0),
                BorderBrush = isCurrent ? Brushes.White : Brushes.Transparent,
                Background = Brushes.Transparent
            };

            var circle = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush(Theme.C(mainColorHex))
            };
            border.Child = circle;

            border.MouseLeftButtonUp += (s, e) =>
            {
                if (s is Border b && b.Tag is string accKey)
                {
                    _island?.SetAccent(accKey);
                    BuildAccentPalette();
                }
            };

            AccentColorsPanel.Children.Add(border);
        }
    }

    private void OnSubTabChanged(object sender, RoutedEventArgs e)
    {
        if (AppearancePanel == null || BehaviorPanel == null || SystemPanel == null || BackgroundPanel == null) return;

        AppearancePanel.Visibility = TabAppearanceRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BackgroundPanel.Visibility = TabBackgroundRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BehaviorPanel.Visibility = TabBehaviorRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = TabSystemRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- задний фон острова ----------

    private readonly System.Windows.Threading.DispatcherTimer _bgLookTimer =
        new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _bgTimerHooked;

    private void RefreshBackgroundCard()
    {
        if (_island == null || BgFileText == null) return;
        var s = _island.S;
        bool has = !string.IsNullOrEmpty(s.BgImage);
        BgFileText.Text = has ? System.IO.Path.GetFileName(s.BgImage) : Loc.T("Фон не выбран", "No background selected", "Arka plan seçilmedi");
        if (BgClearButton != null)
        {
            BgClearButton.IsEnabled = has;
            BgClearButton.Opacity = has ? 1.0 : 0.45;
        }
        if (BgDimSlider == null || BgBlurSlider == null || BgDimValueText == null || BgBlurValueText == null) return;

        bool dragging = _bgLookTimer.IsEnabled
            || (Mouse.LeftButton == MouseButtonState.Pressed && (BgDimSlider.IsMouseOver || BgBlurSlider.IsMouseOver));
        if (dragging) return;
        BgDimSlider.Value = s.BgDim;
        BgBlurSlider.Value = s.BgBlur;
        BgDimValueText.Text = $"{(int)Math.Round(s.BgDim * 100)}%";
        BgBlurValueText.Text = $"{(int)Math.Round(s.BgBlur)}";
    }

    private void OnBgPickClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Выберите картинку для фона острова", "Choose island background", "Ada arka planı seçin"),
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|All files|*.*",
            CheckFileExists = true,
        };
        _island.Busy++;
        bool ok;
        try { ok = dlg.ShowDialog() == true; }
        finally { _island.Busy--; }
        if (!ok) return;

        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickNotch");
            System.IO.Directory.CreateDirectory(dir);
            string dest = System.IO.Path.Combine(dir, "background" + System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant());
            foreach (var old in System.IO.Directory.GetFiles(dir, "background.*"))
            {
                if (!string.Equals(old, dest, StringComparison.OrdinalIgnoreCase))
                    try { System.IO.File.Delete(old); } catch { }
            }
            System.IO.File.Copy(dlg.FileName, dest, true);
            _island.SetBackgroundImage(dest);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "SettingsView.OnBgPickClick");
            _island.SetBackgroundImage(dlg.FileName);
        }
        _loading = true;
        try { RefreshBackgroundCard(); } finally { _loading = false; }
    }

    private void OnBgClearClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        _island.SetBackgroundImage("");
        _loading = true;
        try { RefreshBackgroundCard(); } finally { _loading = false; }
    }

    private void OnBgLookChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _island == null || BgDimSlider == null || BgBlurSlider == null
            || BgDimValueText == null || BgBlurValueText == null) return;

        BgDimValueText.Text = $"{(int)Math.Round(BgDimSlider.Value * 100)}%";
        BgBlurValueText.Text = $"{(int)Math.Round(BgBlurSlider.Value)}";

        if (!_bgTimerHooked)
        {
            _bgTimerHooked = true;
            _bgLookTimer.Tick += (_, _) =>
            {
                _bgLookTimer.Stop();
                _island?.SetBackgroundLook(BgDimSlider.Value, BgBlurSlider.Value);
            };
        }
        _bgLookTimer.Stop();
        _bgLookTimer.Start();
    }

    private void OnThemeDarkClick(object sender, RoutedEventArgs e)
    {
        _island?.SetThemeMode("dark");
        RefreshAll();
    }

    private void OnThemeLightClick(object sender, RoutedEventArgs e)
    {
        _island?.SetThemeMode("light");
        RefreshAll();
    }

    private void OnThemeAutoClick(object sender, RoutedEventArgs e)
    {
        _island?.SetThemeMode("auto");
        RefreshAll();
    }

    private void OnGlassToggleClick(object sender, RoutedEventArgs e)
    {
        _island?.ToggleGlass();
        RefreshAll();
    }

    private void OnRobotCompanionClick(object sender, RoutedEventArgs e)
    {
        _island?.ToggleOpt("robot_companion");
        RefreshAll();
    }

    private void OnRoamClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        if (_island.S.RobotRoaming) _island.DockAero();
        else _island.LaunchAeroToDesktop();
        RefreshAll();
    }

    private void OnDelaySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _island == null || DelaySlider == null || DelayValueText == null) return;
        double val = Math.Round(DelaySlider.Value, 1);
        _island.S.LeaveDelay = val;
        _island.SaveState();
        DelayValueText.Text = $"{val:F1} {Loc.T("сек", "sec", "sn")}";
    }

    private void OnAutostartClick(object sender, RoutedEventArgs e) => _island?.ToggleAutostart();

    private void OnLangRuClick(object sender, RoutedEventArgs e) => _island?.SetLanguage("ru");
    private void OnLangEnClick(object sender, RoutedEventArgs e) => _island?.SetLanguage("en");
    private void OnLangTrClick(object sender, RoutedEventArgs e) => _island?.SetLanguage("tr");

    private void OnTestNotificationClick(object sender, RoutedEventArgs e)
    {
        _island?.Collapse();
        NotificationService.TriggerTest();
    }

    public void RefreshLabels()
    {
        // Вкладки сегментов вверху
        if (TabAppearanceRadio != null) TabAppearanceRadio.Content = Loc.T("Оформление", "Appearance", "Görünüm");
        if (TabBackgroundRadio != null) TabBackgroundRadio.Content = Loc.T("Фон", "Background", "Arka Plan");
        if (TabBehaviorRadio != null) TabBehaviorRadio.Content = Loc.T("Поведение", "Behavior", "Davranış");
        if (TabSystemRadio != null) TabSystemRadio.Content = Loc.T("Система", "System", "Sistem");

        // Вкладка 1: Оформление
        if (ThemeCardTitle != null) ThemeCardTitle.Text = Loc.T("Тема острова", "Island Theme", "Ada Teması");
        if (ThemeCardDesc != null) ThemeCardDesc.Text = Loc.T("Светлый, тёмный или авто-режим", "Light, dark or auto mode", "Açık, koyu veya otomatik mod");
        if (ThemeDarkRadio != null) ThemeDarkRadio.Content = Loc.T("Тёмная", "Dark", "Koyu");
        if (ThemeLightRadio != null) ThemeLightRadio.Content = Loc.T("Светлая", "Light", "Açık");
        if (ThemeAutoRadio != null) ThemeAutoRadio.Content = Loc.T("Авто", "Auto", "Otomatik");

        if (GlassCardTitle != null) GlassCardTitle.Text = Loc.T("Эффект стекла", "Glass Effect", "Cam Efekti");
        if (GlassCardSub != null) GlassCardSub.Text = Loc.T("Матовое стекло Liquid Glass", "Liquid Glass Frosted Surface", "Liquid Glass Mat Cam");
        if (GlassCardDesc != null) GlassCardDesc.Text = Loc.T("Apple Frosted Glass с глубиной", "Apple Frosted Glass with depth", "Derinlikli Apple Mat Cam");

        if (AccentCardTitle != null) AccentCardTitle.Text = Loc.T("Акцентный цвет", "Accent Color", "Vurgu Rengi");
        if (AccentCardDesc != null) AccentCardDesc.Text = Loc.T("Цвета подсветки и активных элементов", "Highlight and active elements colors", "Vurgu ve aktif öğe renkleri");

        if (AeroCardTitle != null) AeroCardTitle.Text = Loc.T("🤖 Робот-компаньон Aero", "🤖 Aero Companion Robot", "🤖 Aero Robot Arkadaşı");
        if (AeroCardDesc != null) AeroCardDesc.Text = Loc.T("Интерактивный питомец: следит за курсором и может летать по рабочему столу", "Interactive pet: follows cursor and roams your desktop", "Etkileşimli evcil hayvan: imleci takip eder ve masaüstünde dolaşır");
        if (RoamButton != null) RoamButton.Content = (_island?.S.RobotRoaming ?? false) 
            ? Loc.T("🏠 Вернуть в островок", "🏠 Return to Island", "🏠 Adaya Geri Dön")
            : Loc.T("🚀 Выпустить на рабочий стол", "🚀 Roam Desktop", "🚀 Masaüstüne Bırak");

        // Вкладка 2: Фон
        if (BgFileCardTitle != null) BgFileCardTitle.Text = Loc.T("Фоновое изображение острова", "Island Background Image", "Ada Arka Plan Resmi");
        if (BgClearButton != null) BgClearButton.Content = Loc.T("Сбросить", "Reset", "Sıfırla");
        if (BgPickButton != null) BgPickButton.Content = Loc.T("📁 Выбрать файл...", "📁 Choose File...", "📁 Dosya Seç...");

        if (BgDimCardTitle != null) BgDimCardTitle.Text = Loc.T("Затемнение фонового рисунка", "Background Dimming", "Arka Plan Karartma");
        if (BgDimCardDesc != null) BgDimCardDesc.Text = Loc.T("Регулировка яркости подложки под карточками", "Adjust brightness under cards", "Kartların altındaki parlaklığı ayarla");

        if (BgBlurCardTitle != null) BgBlurCardTitle.Text = Loc.T("Размытие фона (Gaussian Blur)", "Background Blur (Gaussian Blur)", "Arka Plan Bulanıklığı");
        if (BgBlurCardDesc != null) BgBlurCardDesc.Text = Loc.T("Мягкое матовое размытие обоев острова", "Soft frosted blur for wallpaper", "Duvar kağıdı için yumuşak mat bulanıklık");

        // Вкладка 3: Поведение
        if (SummonCardTitle != null) SummonCardTitle.Text = Loc.T("Способ вызова острова", "Summon Method", "Adayı Açma Yöntemi");
        if (SummonCardDesc != null) SummonCardDesc.Text = Loc.T("Остров раскрывается исключительно по нажатию на чёлку экрана", "Expands strictly when clicking screen notch", "Yalnızca ekran çentiğine tıklandığında açılır");
        if (SummonBadgeText != null) SummonBadgeText.Text = Loc.T("💻 Только чёлка", "💻 Notch Only", "💻 Yalnızca Çentik");

        if (DelayCardTitle != null) DelayCardTitle.Text = Loc.T("Задержка скрытия (Auto-Collapse)", "Auto-Collapse Delay", "Otomatik Kapanma Gecikmesi");
        if (DelayCardDesc != null) DelayCardDesc.Text = Loc.T("Время до сворачивания после ухода курсора", "Time before collapsing after cursor leaves", "İmleç ayrıldıktan sonra kapanma süresi");

        // Вкладка 4: Система
        if (AutostartCardTitle != null) AutostartCardTitle.Text = Loc.T("Запуск с Windows", "Launch on Windows Startup", "Windows ile Başlat");
        if (AutostartCardDesc != null) AutostartCardDesc.Text = Loc.T("Автозагрузка при старте системы", "Auto-start when system boots", "Sistem başladığında otomatik başlat");

        if (LangCardTitle != null) LangCardTitle.Text = Loc.T("Язык интерфейса", "Interface Language", "Arayüz Dili");
        if (LangCardDesc != null) LangCardDesc.Text = Loc.T("Русский, English, Türkçe", "Russian, English, Turkish", "Rusça, İngilizce, Türkçe");

        if (HotkeyCardTitle != null) HotkeyCardTitle.Text = Loc.T("Горячая клавиша активации", "Global Activation Hotkey", "Global Etkinleştirme Kısayolu");
        if (HotkeyCardDesc != null) HotkeyCardDesc.Text = Loc.T("Быстрое открытие и скрытие острова из любого приложения", "Quick toggle island from any application", "Herhangi bir uygulamadan adayı hızlıca aç/kapat");

        if (AboutCardTitle != null) AboutCardTitle.Text = Loc.T("QuickNotch v1.0.4  ·  macOS Dynamic Island для Windows", "QuickNotch v1.0.4  ·  macOS Dynamic Island for Windows", "QuickNotch v1.0.4  ·  Windows için macOS Dynamic Island");
        if (AboutCardDesc != null) AboutCardDesc.Text = Loc.T("Аппаратно-ускоренный движок WPF XAML (0% нагрузки на ЦП)", "Hardware-accelerated WPF XAML engine (0% idle CPU)", "Donanım hızlandırmalı WPF XAML motoru (%0 boşta CPU)");
        if (TestNotifyButton != null) TestNotifyButton.Content = Loc.T("🔔 Тест уведомления", "🔔 Test Notification", "🔔 Bildirim Testi");
    }
}

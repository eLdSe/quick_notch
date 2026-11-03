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
            if (DelayValueText != null) DelayValueText.Text = $"{s.LeaveDelay:F1} сек";

            // 6. Автозапуск и язык
            if (AutostartCheckBox != null) AutostartCheckBox.IsChecked = _island.Autostart;
            if (LangRuRadio != null) LangRuRadio.IsChecked = s.Language == "ru";
            if (LangEnRadio != null) LangEnRadio.IsChecked = s.Language == "en";
            if (LangTrRadio != null) LangTrRadio.IsChecked = s.Language == "tr";

            // 7. Фон острова
            RefreshBackgroundCard();
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
        DelayValueText.Text = $"{val:F1} сек";
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
}

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

    public void RefreshAll()
    {
        if (_island == null) return;
        _loading = true;
        try
        {
            var s = _island.S;

            // 1. Тема острова
            ThemeDarkRadio.IsChecked = s.ThemeMode == "dark";
            ThemeLightRadio.IsChecked = s.ThemeMode == "light";
            ThemeAutoRadio.IsChecked = s.ThemeMode == "auto";

            // 2. Форм-фактор
            StyleIslandRadio.IsChecked = s.IslandStyle == "island";
            StyleNotchRadio.IsChecked = s.IslandStyle == "notch";
            StyleCompactRadio.IsChecked = s.IslandStyle == "compact";

            // 3. Палитра акцентных цветов
            BuildAccentPalette();

            // 4. Эффект стекла
            GlassCheckBox.IsChecked = s.GlassEnabled;

            // 5. Робот-компаньон Aero
            RobotCompanionCheckBox.IsChecked = s.RobotCompanion;
            RoamButton.Content = s.RobotRoaming ? "🏠 Вернуть в островок" : "🚀 На рабочий стол";

            // 6. Способ вызова
            SummonHoverRadio.IsChecked = s.SummonMode == "hover";
            SummonHotkeyRadio.IsChecked = s.SummonMode == "hotkey";
            SummonBothRadio.IsChecked = s.SummonMode == "both";
            SummonNotchRadio.IsChecked = s.SummonMode == "notch";

            // 7. Слайдеры задержки и затемнения
            DelaySlider.Value = s.LeaveDelay;
            DelayValueText.Text = $"{s.LeaveDelay:F1} сек";

            DimSlider.Value = s.DimLevel;
            DimValueText.Text = $"{(int)Math.Round(s.DimLevel * 100)}%";

            // 8. Автозапуск и язык
            AutostartCheckBox.IsChecked = _island.Autostart;
            LangRuRadio.IsChecked = s.Language == "ru";
            LangEnRadio.IsChecked = s.Language == "en";
            LangTrRadio.IsChecked = s.Language == "tr";
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildAccentPalette()
    {
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
        if (AppearancePanel == null || BehaviorPanel == null || SystemPanel == null) return;

        AppearancePanel.Visibility = TabAppearanceRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        BehaviorPanel.Visibility = TabBehaviorRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = TabSystemRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnThemeDarkClick(object sender, RoutedEventArgs e) => _island?.SetThemeMode("dark");
    private void OnThemeLightClick(object sender, RoutedEventArgs e) => _island?.SetThemeMode("light");
    private void OnThemeAutoClick(object sender, RoutedEventArgs e) => _island?.SetThemeMode("auto");

    private void OnStyleIslandClick(object sender, RoutedEventArgs e) => _island?.SetIslandStyle("island");
    private void OnStyleNotchClick(object sender, RoutedEventArgs e) => _island?.SetIslandStyle("notch");
    private void OnStyleCompactClick(object sender, RoutedEventArgs e) => _island?.SetIslandStyle("compact");

    private void OnGlassToggleClick(object sender, RoutedEventArgs e) => _island?.ToggleGlass();

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

    private void OnSummonHoverClick(object sender, RoutedEventArgs e) => _island?.SetSummonMode("hover");
    private void OnSummonHotkeyClick(object sender, RoutedEventArgs e) => _island?.SetSummonMode("hotkey");
    private void OnSummonBothClick(object sender, RoutedEventArgs e) => _island?.SetSummonMode("both");
    private void OnSummonNotchClick(object sender, RoutedEventArgs e) => _island?.SetSummonMode("notch");

    private void OnDelaySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _island == null) return;
        double val = Math.Round(DelaySlider.Value, 1);
        _island.S.LeaveDelay = val;
        _island.SaveState();
        DelayValueText.Text = $"{val:F1} сек";
    }

    private void OnDimSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _island == null) return;
        double val = Math.Round(DimSlider.Value, 2);
        _island.S.DimLevel = val;
        _island.SaveState();
        DimValueText.Text = $"{(int)Math.Round(val * 100)}%";
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


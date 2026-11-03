using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickNotchCs;

public partial class TopBarView : UserControl
{
    private IslandView? _island;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private static readonly SolidColorBrush GreenBrush = new((Color)ColorConverter.ConvertFromString("#30D158"));
    private static readonly SolidColorBrush YellowBrush = new((Color)ColorConverter.ConvertFromString("#FFD60A"));
    private static readonly SolidColorBrush RedBrush = new((Color)ColorConverter.ConvertFromString("#FF453A"));
    private static readonly SolidColorBrush TextPrimaryBrush = Brushes.White;
    private static readonly SolidColorBrush AeroCyanBrush = new((Color)ColorConverter.ConvertFromString("#38BDF8"));
    private static readonly SolidColorBrush RoamOrangeBrush = new((Color)ColorConverter.ConvertFromString("#FF9F0A"));

    static TopBarView()
    {
        GreenBrush.Freeze();
        YellowBrush.Freeze();
        RedBrush.Freeze();
        AeroCyanBrush.Freeze();
        RoamOrangeBrush.Freeze();
    }

    public TopBarView()
    {
        InitializeComponent();
        _clockTimer.Tick += (_, _) => RefreshTimeAndDate();
        _clockTimer.Start();
        RefreshTimeAndDate();
    }

    public void Attach(IslandView island)
    {
        _island = island;
        RefreshAll();
    }

    public void RefreshAll()
    {
        RefreshTimeAndDate();
        RefreshBattery();
        RefreshRobot();
        RefreshBreadcrumb();
    }

    public void RefreshTimeAndDate()
    {
        if (TimeText == null || DateText == null) return;
        var now = DateTime.Now;
        TimeText.Text = now.ToString("HH:mm");

        string dayOfWeek = now.ToString("dddd", Loc.Culture);
        if (!string.IsNullOrEmpty(dayOfWeek))
        {
            dayOfWeek = char.ToUpper(dayOfWeek[0]) + dayOfWeek[1..];
        }
        string datePart = now.ToString("d MMMM", Loc.Culture);
        DateText.Text = $" •  {dayOfWeek}, {datePart}";
    }

    public void RefreshBattery()
    {
        if (_island == null || BatteryChip == null || BatteryFill == null || BatteryPercentText == null) return;

        var (pct, charging) = _island.Battery;
        if (pct == null)
        {
            BatteryPercentText.Text = "—";
            BatteryFill.Width = 0;
            ChargingBolt.Visibility = Visibility.Collapsed;
            return;
        }

        int val = Math.Clamp(pct.Value, 0, 100);
        BatteryPercentText.Text = $"{val}%";
        // Полная ширина 18px (максимум)
        BatteryFill.Width = Math.Max(2, 18.0 * val / 100.0);

        if (charging)
        {
            BatteryFill.Background = GreenBrush;
            ChargingBolt.Visibility = Visibility.Visible;
        }
        else
        {
            ChargingBolt.Visibility = Visibility.Collapsed;
            if (val <= 10) BatteryFill.Background = RedBrush;
            else if (val <= 20) BatteryFill.Background = YellowBrush;
            else BatteryFill.Background = TextPrimaryBrush; // Белый цвет аккумулятора при нормальной работе
        }
    }

    public void RefreshRobot()
    {
        if (_island == null || AeroPillButton == null) return;

        AeroPillButton.Visibility = Visibility.Visible;
        if (_island.S.RobotRoaming)
        {
            if (AeroIconSymbol != null)
            {
                AeroIconSymbol.Text = "🛸";
                AeroIconSymbol.Foreground = RoamOrangeBrush;
            }
            if (AeroNameText != null) AeroNameText.Text = Loc.T("Аэро на столе", "Aero on Desktop", "Aero masada");
            if (AeroArrowSymbol != null) AeroArrowSymbol.Text = "⭡";
            if (AeroOnlineIndicator != null) AeroOnlineIndicator.Fill = (Brush)FindResource("AccentBrush");
            if (AeroIndicatorGlow != null) AeroIndicatorGlow.Color = (Color)ColorConverter.ConvertFromString("#FF453A");
            AeroPillButton.ToolTip = Loc.T("Вернуть Аэро в остров", "Dock Aero back to Island", "Aero'yu geri adaya al");
        }
        else
        {
            if (AeroIconSymbol != null)
            {
                AeroIconSymbol.Text = "🤖";
                AeroIconSymbol.Foreground = AeroCyanBrush;
            }
            if (AeroNameText != null) AeroNameText.Text = "Аэро";
            if (AeroArrowSymbol != null) AeroArrowSymbol.Text = "⭣";
            if (AeroOnlineIndicator != null) AeroOnlineIndicator.Fill = (Brush)FindResource("GreenBrush");
            if (AeroIndicatorGlow != null) AeroIndicatorGlow.Color = (Color)ColorConverter.ConvertFromString("#30D158");
            AeroPillButton.ToolTip = Loc.T("Выпустить Аэро на рабочий стол", "Launch Aero to Desktop", "Aero'yu masaüstüne çıkart");
        }
    }

    private void OnAeroIconClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        if (_island.S.RobotRoaming)
        {
            _island.DockAero();
        }
        else
        {
            _island.LaunchAeroToDesktop();
        }
        RefreshRobot();
    }

    public void RefreshBreadcrumb()
    {
        if (_island == null || BreadcrumbButton == null || BreadcrumbText == null) return;

        string tool = _island.CurrentTool;
        if (tool == "home")
        {
            BreadcrumbButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            var sec = Sections.ByKey(tool);
            BreadcrumbText.Text = $"{Loc.T("Назад", "Back", "Geri")}  •  {sec.Icon} {sec.Title}";
            BreadcrumbButton.Visibility = Visibility.Visible;
        }
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

    private void OnDateClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        _island.Select(_island.CurrentTool == "calendar" ? "home" : "calendar");
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        _island.Select(_island.CurrentTool == "settings" ? "home" : "settings");
    }

    private void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        _island?.Select("home");
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e)
    {
        _island?.Collapse();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}


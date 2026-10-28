using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuickNotchCs;

public partial class SystemView : UserControl
{
    private IslandView? _island;

    public SystemView()
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
        var snap = _island.Snapshot;
        if (snap == null) return;

        // 1. ЦП
        CpuValueText.Text = snap.Cpu;
        CpuProgressBar.Value = Math.Clamp(snap.CpuFraction * 100.0, 0.0, 100.0);

        // 2. Память
        MemoryValueText.Text = string.IsNullOrEmpty(snap.MemoryPercent) ? snap.Memory : $"{snap.Memory} ({snap.MemoryPercent})";
        MemoryProgressBar.Value = Math.Clamp(snap.MemoryFraction * 100.0, 0.0, 100.0);

        // 3. Диск C:
        DiskValueText.Text = string.IsNullOrEmpty(snap.DiskPercent) ? snap.Disk : $"{snap.Disk} ({snap.DiskPercent})";
        DiskProgressBar.Value = Math.Clamp(snap.DiskFraction * 100.0, 0.0, 100.0);

        // 4. Сеть
        NetworkNameText.Text = $"🌐  {snap.NetworkName}";
        NetworkSpeedText.Text = snap.NetworkSpeed;
        NetworkProgressBar.Value = Math.Clamp(snap.NetworkFraction * 100.0, 0.0, 100.0);

        // 5. Uptime
        UptimeText.Text = snap.Uptime;

        // 6. Топ процессов
        ProcessesPanel.Children.Clear();
        int count = Math.Min(4, snap.TopProcesses.Count);
        for (int i = 0; i < count; i++)
        {
            var proc = snap.TopProcesses[i];

            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Индикатор-точка
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = (Brush)FindResource("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(dot, 0);
            rowGrid.Children.Add(dot);

            // Название процесса
            var nameText = new TextBlock
            {
                Text = proc.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 8, 0)
            };
            Grid.SetColumn(nameText, 1);
            rowGrid.Children.Add(nameText);

            // Бейдж объема памяти
            var badge = new Border
            {
                Background = (Brush)FindResource("Surface2Brush"),
                BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            var memText = new TextBlock
            {
                Text = proc.Memory,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("AccentHotBrush")
            };
            badge.Child = memText;
            Grid.SetColumn(badge, 2);
            rowGrid.Children.Add(badge);

            ProcessesPanel.Children.Add(rowGrid);
        }
    }
}


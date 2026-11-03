using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class SoundView : UserControl
{
    private IslandView? _island;
    private bool _updatingMaster = false;
    private bool _isMuted = false;

    public SoundView()
    {
        InitializeComponent();
    }

    public void Attach(IslandView island)
    {
        _island = island;
        RefreshAll();
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _island?.StopLeaveTimer();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _island?.StopLeaveTimer();
    }

    protected override void OnPreviewMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        _island?.StopLeaveTimer();
    }

    public void RefreshAll()
    {
        RefreshMasterVolume();
        RefreshDevices();
        RefreshApps();
    }

    public void RefreshMasterVolume()
    {
        if (_updatingMaster) return;
        _updatingMaster = true;
        try
        {
            var (volLevel, muted) = AudioService.GetVolume();
            int volPct = (int)Math.Round(volLevel * 100);
            MasterVolumeSlider.Value = volPct;
            _isMuted = muted;
            MasterVolumePercentText.Text = muted ? Loc.T("Без звука", "Muted", "Sessiz") : $"{volPct}%";
            MuteIcon.Text = muted ? "\uE74F" : "\uE767";
            MuteButtonText.Text = muted ? Loc.T("Звук выключен", "Muted", "Sessiz") : Loc.T("Звук включен", "Audio On", "Ses Açık");
        }
        catch { }
        finally
        {
            _updatingMaster = false;
        }
    }

    public void ApplyAccent()
    {
        RefreshDevices();
    }

    public void RefreshDevices()
    {
        Bg.Run(AudioService.ListOutputDevices, (res, err) =>
        {
            if (err != null || res.Devices == null) return;
            DevicesPanel.Children.Clear();
            var (devices, defaultId) = res;
            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                bool isSelected = dev.Id == defaultId;

                var btn = new Button
                {
                    Style = (Style)FindResource("DeviceItemButtonStyle"),
                    Tag = dev.Id
                };

                if (isSelected)
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(40, 10, 132, 255));
                    btn.BorderBrush = Theme.AccentBrush;
                }

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

                bool isHeadphones = dev.Name.Contains("head", StringComparison.OrdinalIgnoreCase) ||
                                   dev.Name.Contains("науш", StringComparison.OrdinalIgnoreCase);
                var icon = new TextBlock
                {
                    Text = isHeadphones ? "\uE7F6" : "\uE7F5",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 14,
                    Foreground = isSelected ? Theme.AccentBrush : (Brush)FindResource("TextSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);

                var nameText = new TextBlock
                {
                    Text = dev.Name,
                    FontSize = 12,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isSelected ? (Brush)FindResource("TextPrimaryBrush") : (Brush)FindResource("TextSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameText, 1);
                grid.Children.Add(nameText);

                if (isSelected)
                {
                    var check = new TextBlock
                    {
                        Text = "\uE73E",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = Theme.AccentBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(check, 2);
                    grid.Children.Add(check);
                }

                btn.Content = grid;
                btn.Click += (_, _) =>
                {
                    AudioService.SetDefaultDevice(dev.Id);
                    RefreshDevices();
                };

                DevicesPanel.Children.Add(btn);
            }
        });
    }

    public void RefreshApps()
    {
        Bg.Run(AudioService.ListAppVolumes, (apps, err) =>
        {
            if (err != null || apps == null) return;
            AppsPanel.Children.Clear();
            if (apps.Count == 0)
            {
                NoAppsPanel.Visibility = Visibility.Visible;
                return;
            }

            NoAppsPanel.Visibility = Visibility.Collapsed;
            for (int i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                var appCard = new Border
                {
                    Background = (Brush)FindResource("Surface2Brush"),
                    BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var sp = new StackPanel();

                var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                var nameText = new TextBlock
                {
                    Text = app.Name,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                var pctText = new TextBlock
                {
                    Text = $"{(int)Math.Round(app.Level * 100)}%",
                    FontSize = 11.5,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                headerGrid.Children.Add(nameText);
                headerGrid.Children.Add(pctText);
                sp.Children.Add(headerGrid);

                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = (int)Math.Round(app.Level * 100),
                    IsMoveToPointEnabled = true,
                    Height = 32,
                    Style = (Style)FindResource("AppleSliderStyle"),
                    Tag = app
                };

                slider.ValueChanged += (s, e) =>
                {
                    if (s is Slider sl && sl.Tag is AppVolume targetApp)
                    {
                        float lvl = (float)(sl.Value / 100.0);
                        AudioService.SetAppVolume(targetApp.ProcessIds, lvl);
                        pctText.Text = $"{(int)Math.Round(sl.Value)}%";
                    }
                };
                sp.Children.Add(slider);

                appCard.Child = sp;
                AppsPanel.Children.Add(appCard);
            }
        });
    }

    private void OnMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingMaster || MasterVolumePercentText == null || MasterVolumeSlider == null || _island == null) return;
        float level = (float)(MasterVolumeSlider.Value / 100.0);
        AudioService.SetVolume(level);
        MasterVolumePercentText.Text = $"{(int)Math.Round(MasterVolumeSlider.Value)}%";
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        _isMuted = !_isMuted;
        AudioService.SetMute(_isMuted);
        RefreshMasterVolume();
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuickNotchCs;

public partial class SoundView : UserControl
{
    private IslandView? _island;
    private bool _updatingMaster = false;
    private bool _isMuted = false;
    private bool _volumeSubscribed = false;
    private int _masterAnimGen = 0;
    private DateTime _lastUserMasterVolumeTime = DateTime.MinValue;
    private string _currentDefaultDeviceId = "";

    public SoundView()
    {
        InitializeComponent();
    }

    public void Attach(IslandView island)
    {
        _island = island;
        if (!_volumeSubscribed)
        {
            _volumeSubscribed = true;
            AudioService.EnsureVolumeListener();
            AudioService.VolumeChanged += OnAudioVolumeChanged;
        }
        RefreshAll();
    }

    private void OnAudioVolumeChanged(float level, bool muted)
    {
        if ((DateTime.UtcNow - _lastUserMasterVolumeTime).TotalMilliseconds < 350) return;
        Dispatcher.InvokeAsync(() =>
        {
            if ((DateTime.UtcNow - _lastUserMasterVolumeTime).TotalMilliseconds < 350) return;
            int volPct = (int)Math.Round(level * 100);
            AnimateMasterVolume(volPct, muted);
        });
    }

    private void AnimateMasterVolume(int targetPct, bool muted)
    {
        if (MasterVolumeSlider == null || MasterVolumePercentText == null) return;
        _updatingMaster = true;
        try
        {
            _isMuted = muted;
            MasterVolumePercentText.Text = muted ? Loc.T("Без звука", "Muted", "Sessiz") : $"{targetPct}%";
            if (MuteIcon != null) MuteIcon.Text = muted ? "\uE74F" : "\uE767";
            if (MuteButtonText != null)
                MuteButtonText.Text = muted ? Loc.T("Звук выключен", "Muted", "Sessiz") : Loc.T("Звук включен", "Audio On", "Ses Açık");

            if (Math.Abs(MasterVolumeSlider.Value - targetPct) < 0.5)
            {
                _masterAnimGen++;
                MasterVolumeSlider.BeginAnimation(Slider.ValueProperty, null);
                MasterVolumeSlider.Value = targetPct;
                _updatingMaster = false;
                return;
            }

            int curGen = ++_masterAnimGen;
            var anim = new DoubleAnimation
            {
                To = targetPct,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                if (_masterAnimGen == curGen)
                {
                    MasterVolumeSlider.BeginAnimation(Slider.ValueProperty, null);
                    MasterVolumeSlider.Value = targetPct;
                    _updatingMaster = false;
                }
            };
            MasterVolumeSlider.BeginAnimation(Slider.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }
        catch
        {
            MasterVolumeSlider.Value = targetPct;
            _updatingMaster = false;
        }
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
            MasterVolumeSlider.BeginAnimation(Slider.ValueProperty, null);
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
        UpdateDevicesVisualState(_currentDefaultDeviceId);
    }

    private void SelectDevice(string devId)
    {
        if (_currentDefaultDeviceId == devId) return;
        _currentDefaultDeviceId = devId;

        // Мгновенное тактильное переключение в интерфейсе без задержек и без пересоздания кнопок
        UpdateDevicesVisualState(devId);

        // Переключение аудиоустройства в Windows выполняем в фоновом потоке, не замораживая UI
        Task.Run(() =>
        {
            try
            {
                AudioService.SetDefaultDevice(devId);
                Dispatcher.InvokeAsync(() =>
                {
                    RefreshMasterVolume();
                });
            }
            catch { }
        });
    }

    private void UpdateDevicesVisualState(string? defaultId)
    {
        string targetId = defaultId ?? "";
        foreach (var child in DevicesPanel.Children)
        {
            if (child is Button btn && btn.Tag is string id && btn.Content is Grid grid)
            {
                bool isSelected = id == targetId;
                btn.Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(40, 10, 132, 255))
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
                btn.BorderBrush = isSelected ? Theme.AccentBrush : new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));

                if (grid.Children.Count >= 2 && grid.Children[0] is TextBlock icon && grid.Children[1] is TextBlock nameText)
                {
                    icon.Foreground = isSelected ? Theme.AccentBrush : (Brush)FindResource("TextSecondaryBrush");
                    nameText.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
                    nameText.Foreground = isSelected ? (Brush)FindResource("TextPrimaryBrush") : (Brush)FindResource("TextSecondaryBrush");
                }
                if (grid.Children.Count >= 3 && grid.Children[2] is TextBlock check)
                {
                    check.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                    check.Foreground = Theme.AccentBrush;
                }
            }
        }
    }

    public void RefreshDevices()
    {
        Bg.Run(AudioService.ListOutputDevices, (res, err) =>
        {
            if (err != null || res.Devices == null) return;
            var (devices, defaultId) = res;
            _currentDefaultDeviceId = defaultId ?? "";

            // Если список устройств совпадает — плавно обновляем выделение без рывка и очистки UI
            if (DevicesPanel.Children.Count == devices.Count)
            {
                bool match = true;
                for (int i = 0; i < devices.Count; i++)
                {
                    if (DevicesPanel.Children[i] is not Button btn || (string?)btn.Tag != devices[i].Id)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    UpdateDevicesVisualState(defaultId);
                    return;
                }
            }

            DevicesPanel.Children.Clear();
            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                bool isSelected = dev.Id == defaultId;

                var btn = new Button
                {
                    Style = (Style)FindResource("DeviceItemButtonStyle"),
                    Tag = dev.Id
                };

                btn.Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(40, 10, 132, 255))
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
                btn.BorderBrush = isSelected ? Theme.AccentBrush : new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));

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

                var check = new TextBlock
                {
                    Text = "\uE73E",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.AccentBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
                };
                Grid.SetColumn(check, 2);
                grid.Children.Add(check);

                btn.Content = grid;
                btn.Click += (_, _) => SelectDevice(dev.Id);

                DevicesPanel.Children.Add(btn);
            }
        });
    }

    public void RefreshApps()
    {
        Bg.Run(AudioService.ListAppVolumes, (apps, err) =>
        {
            if (err != null || apps == null) return;
            if (apps.Count == 0)
            {
                AppsPanel.Children.Clear();
                NoAppsPanel.Visibility = Visibility.Visible;
                return;
            }

            NoAppsPanel.Visibility = Visibility.Collapsed;

            // Если список приложений не изменился по количеству и составу — обновляем ползунки на месте
            if (AppsPanel.Children.Count == apps.Count)
            {
                bool match = true;
                for (int i = 0; i < apps.Count; i++)
                {
                    if (AppsPanel.Children[i] is Border card && card.Tag is string name && name == apps[i].Name)
                    {
                        continue;
                    }
                    match = false;
                    break;
                }
                if (match)
                {
                    for (int i = 0; i < apps.Count; i++)
                    {
                        var app = apps[i];
                        if (AppsPanel.Children[i] is Border card && card.Child is StackPanel sp && sp.Children.Count >= 2)
                        {
                            if (sp.Children[0] is Grid hGrid && hGrid.Children.Count >= 2 && hGrid.Children[1] is TextBlock pct)
                            {
                                pct.Text = $"{(int)Math.Round(app.Level * 100)}%";
                            }
                            if (sp.Children[1] is Slider s && !s.IsMouseCaptureWithin)
                            {
                                s.Tag = app;
                                s.Value = (int)Math.Round(app.Level * 100);
                            }
                        }
                    }
                    return;
                }
            }

            AppsPanel.Children.Clear();
            for (int i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                var appCard = new Border
                {
                    Tag = app.Name,
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

    private void OnMasterVolumeSliderPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _lastUserMasterVolumeTime = DateTime.UtcNow;
        _masterAnimGen++;
        _updatingMaster = false;
        MasterVolumeSlider.BeginAnimation(Slider.ValueProperty, null);
    }

    private void OnMasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingMaster || MasterVolumePercentText == null || MasterVolumeSlider == null || _island == null) return;
        _lastUserMasterVolumeTime = DateTime.UtcNow;
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

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuickNotchCs;

public partial class HomeView : UserControl
{
    private IslandView? _island;
    private bool _updatingSliders = false;
    private bool _isDraggingVolume = false;
    private bool _isDraggingBrightness = false;
    private int _lastObservedBrightness = -1;
    private bool _volumeSubscribed = false;
    private bool _pomodoroSubscribed = false;
    private int _volumeAnimGen = 0;
    private int _brightnessAnimGen = 0;
    private DateTime _lastUserVolumeTime = DateTime.MinValue;
    private DateTime _lastUserBrightnessTime = DateTime.MinValue;

    private DispatcherTimer? _mediaProgressTimer;

    public HomeView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) CheckMediaProgressTimer();
            else _mediaProgressTimer?.Stop();
        };
    }

    private void EnsureMediaProgressTimer()
    {
        if (_mediaProgressTimer != null) return;
        _mediaProgressTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _mediaProgressTimer.Tick += (_, _) =>
        {
            if (IsVisible && _island?.Media.Playing == true)
            {
                UpdateMediaProgress();
            }
            else if (_island?.Media.Playing != true)
            {
                _mediaProgressTimer.Stop();
            }
        };
    }

    private void CheckMediaProgressTimer()
    {
        EnsureMediaProgressTimer();
        if (IsVisible && _island?.Media.Playing == true)
        {
            if (!_mediaProgressTimer!.IsEnabled) _mediaProgressTimer.Start();
        }
        else
        {
            _mediaProgressTimer?.Stop();
        }
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
        if (!_pomodoroSubscribed)
        {
            _pomodoroSubscribed = true;
            PomodoroService.OnTick += () => Dispatcher.InvokeAsync(RefreshConnectivity);
            PomodoroService.OnPhaseChanged += () => Dispatcher.InvokeAsync(RefreshConnectivity);
        }
        RefreshAll();
    }

    private void OnAudioVolumeChanged(float level, bool muted)
    {
        if (_isDraggingVolume) return;
        if ((DateTime.UtcNow - _lastUserVolumeTime).TotalMilliseconds < 350) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (_isDraggingVolume) return;
            if ((DateTime.UtcNow - _lastUserVolumeTime).TotalMilliseconds < 350) return;
            int volPct = (int)Math.Round(level * 100);
            AnimateVolume(volPct, muted);
        });
    }

    private void AnimateVolume(int targetPct, bool muted)
    {
        if (VolumeSlider == null || VolumePercentText == null) return;
        _updatingSliders = true;
        try
        {
            VolumePercentText.Text = muted ? Loc.T("Без звука", "Muted", "Sessiz") : $"{targetPct}%";
            if (VolumeIcon != null)
            {
                VolumeIcon.Text = muted || targetPct == 0 ? "\uE74F" : (targetPct < 33 ? "\uE992" : (targetPct < 66 ? "\uE994" : "\uE767"));
            }

            if (Math.Abs(VolumeSlider.Value - targetPct) < 0.5)
            {
                _volumeAnimGen++;
                VolumeSlider.BeginAnimation(Slider.ValueProperty, null);
                VolumeSlider.Value = targetPct;
                _updatingSliders = false;
                return;
            }

            int curGen = ++_volumeAnimGen;
            var anim = new DoubleAnimation
            {
                To = targetPct,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                if (_volumeAnimGen == curGen)
                {
                    VolumeSlider.BeginAnimation(Slider.ValueProperty, null);
                    VolumeSlider.Value = targetPct;
                    _updatingSliders = false;
                }
            };
            VolumeSlider.BeginAnimation(Slider.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }
        catch
        {
            VolumeSlider.Value = targetPct;
            _updatingSliders = false;
        }
    }

    private void AnimateBrightness(int targetPct)
    {
        if (BrightnessSlider == null || BrightnessPercentText == null) return;
        if (Math.Abs(BrightnessSlider.Value - targetPct) < 1) return;
        _updatingSliders = true;
        try
        {
            BrightnessPercentText.Text = $"{targetPct}%";
            int curGen = ++_brightnessAnimGen;
            var anim = new DoubleAnimation
            {
                To = targetPct,
                Duration = TimeSpan.FromMilliseconds(130),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                if (_brightnessAnimGen == curGen)
                {
                    BrightnessSlider.BeginAnimation(Slider.ValueProperty, null);
                    BrightnessSlider.Value = targetPct;
                    _updatingSliders = false;
                }
            };
            BrightnessSlider.BeginAnimation(Slider.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }
        catch
        {
            BrightnessSlider.Value = targetPct;
            _updatingSliders = false;
        }
    }

    private void PollBrightnessIfIdle()
    {
        if (_isDraggingBrightness || _updatingSliders) return;
        Task.Run(() =>
        {
            try
            {
                int cur = DeviceService.GetBrightness();
                if (_lastObservedBrightness != cur)
                {
                    _lastObservedBrightness = cur;
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (!_isDraggingBrightness && !_updatingSliders)
                        {
                            AnimateBrightness(cur);
                        }
                    });
                }
            }
            catch { }
        });
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
        if (_island == null) return;
        RefreshTitles();
        RefreshMedia();
        RefreshConnectivity();
        RefreshSliders();
        RefreshSystemAndWeather();
    }

    private void RefreshTitles()
    {
        QuickTitleText.Text = Loc.T("Быстрые действия", "Quick Actions", "Hızlı İşlemler");
        NowPlayingTitleText.Text = Loc.T("Сейчас играет", "Now Playing", "Şimdi Çalıyor");
        VolumeTitleText.Text = Loc.T("Звук и аудио", "Sound and Audio", "Ses ve Ses Düzeyi");
        VolumeLabelText.Text = Loc.T("Динамики", "Speakers", "Hoparlörler");
        BrightnessLabelText.Text = Loc.T("Яркость дисплея", "Display brightness", "Ekran parlaklığı");
        SystemTitleText.Text = Loc.T("Дисплей и Система", "Display and System", "Ekran ve Sistem");
        if (SnipTitleText != null) SnipTitleText.Text = Loc.T("Снимок", "Snip", "Ekran");
        if (SnipStatusText != null) SnipStatusText.Text = Loc.T("Экран", "Screen", "Kırp");
        if (PomodoroTitleText != null) PomodoroTitleText.Text = Loc.T("Помодоро", "Pomodoro", "Pomodoro");
        if (NotesTitleText != null) NotesTitleText.Text = Loc.T("Заметки", "Notes", "Notlar");
        if (FilesTitleText != null) FilesTitleText.Text = Loc.T("Файлы", "Files", "Dosyalar");
        if (CpuLabelText != null) CpuLabelText.Text = "CPU";
        if (RamLabelText != null) RamLabelText.Text = "RAM";
    }

    public void RefreshMedia()
    {
        if (_island == null) return;
        var m = _island.Media;

        if (string.IsNullOrEmpty(m.Title))
        {
            TrackTitleText.Text = Loc.T("Не воспроизводится", "Not Playing", "Çalınmıyor");
            TrackArtistText.Text = Loc.T("Включите музыку в плеере", "Play media in Spotify or browser", "Spotify veya tarayıcıda medya oynatın");
            MediaAppBadge.Text = "";
            UpdatePlayPauseIcon(false);
            AlbumCoverImage.Source = null;
        }
        else
        {
            TrackTitleText.Text = m.Title;
            TrackArtistText.Text = string.IsNullOrEmpty(m.Artist) ? "—" : m.Artist;
            MediaAppBadge.Text = m.App ?? "";
            UpdatePlayPauseIcon(m.Playing);
            if (_island.CoverBrush is ImageBrush ib)
            {
                AlbumCoverImage.Source = ib.ImageSource;
            }
        }
        UpdateMediaProgress();
        CheckMediaProgressTimer();
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public void UpdateMediaProgress()
    {
        if (_island == null || MediaProgress == null) return;
        var m = _island.Media;
        if (string.IsNullOrEmpty(m.Title) || m.Dur <= 0)
        {
            MediaProgress.Value = 0;
            MediaPosText.Text = "0:00";
            MediaDurText.Text = string.IsNullOrEmpty(m.Title) ? "0:00" : "--:--";
            return;
        }
        double pos = m.CurrentPos();
        MediaProgress.Value = Math.Clamp(pos / m.Dur, 0, 1);
        MediaPosText.Text = FormatTime(pos);
        MediaDurText.Text = FormatTime(m.Dur);
    }

    private static void StyleTile(Border tile, TextBlock icon, bool on)
    {
        tile.BorderBrush = on ? Theme.AccentBrush : Brushes.Transparent;
        tile.Background = on
            ? new SolidColorBrush(Color.FromArgb(0x30, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B))
            : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        tile.Effect = on
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Theme.Accent, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            }
            : null;
        icon.Foreground = on ? Theme.AccentBrush : Brushes.White;
    }

    public void RefreshConnectivity()
    {
        if (_island == null) return;
        var sett = Sections.Settings;

        // Wi-Fi
        bool? wifi = sett.States["WiFi"];
        bool wifiOn = wifi == true;
        StyleTile(WifiIndicator, WifiIcon, wifiOn);
        WifiStatusText.Text = wifi == null ? "—" : (wifiOn ? Loc.T("Вкл", "On", "Açık") : Loc.T("Выкл", "Off", "Kapalı"));

        // Bluetooth
        bool? bt = sett.States["Bluetooth"];
        bool btOn = bt == true;
        StyleTile(BtIndicator, BtIcon, btOn);
        BtStatusText.Text = bt == null ? "—" : (btOn ? Loc.T("Вкл", "On", "Açık") : Loc.T("Выкл", "Off", "Kapalı"));

        // Скриншот
        if (SnipIndicator != null && SnipIcon != null)
        {
            StyleTile(SnipIndicator, SnipIcon, false);
        }

        // Помодоро
        bool pomoActive = PomodoroService.CurrentState == PomodoroState.Running;
        bool pomoPaused = PomodoroService.CurrentState == PomodoroState.Paused;
        if (PomodoroStatusText != null)
        {
            if (pomoActive)
            {
                PomodoroStatusText.Text = PomodoroService.FormattedTime;
            }
            else if (pomoPaused)
            {
                PomodoroStatusText.Text = $"{PomodoroService.FormattedTime} ⏸";
            }
            else
            {
                PomodoroStatusText.Text = $"{(_island?.S.PomodoroFocus ?? 25)}:00";
            }
        }
        StyleTile(PomodoroIndicator, PomodoroIcon, pomoActive || pomoPaused);

        // Заметки / Задачи
        if (NotesIndicator != null && NotesIcon != null && NotesStatusText != null)
        {
            int activeNotes = Sections.Calendar.Notes.Count(n => !n.Done);
            NotesStatusText.Text = activeNotes > 0
                ? $"{activeNotes} {Loc.T("актив.", "active", "aktif")}"
                : Loc.T("Открыть", "Open", "Aç");
            StyleTile(NotesIndicator, NotesIcon, activeNotes > 0);
        }

        // Файлы / Полка
        if (FilesIndicator != null && FilesIcon != null && FilesStatusText != null)
        {
            int fileCount = Sections.Links.Links.Count(l => l.Target.Contains(":\\") || l.Target.Contains(":/"));
            FilesStatusText.Text = fileCount > 0
                ? $"{fileCount} {Loc.T("шт.", "items", "öğe")}"
                : Loc.T("Полка", "Tray", "Raf");
            StyleTile(FilesIndicator, FilesIcon, false);
        }
    }

    public void ApplyAccent()
    {
        if (!_pomodoroSubscribed)
        {
            _pomodoroSubscribed = true;
            PomodoroService.OnTick += () => Dispatcher.InvokeAsync(RefreshConnectivity);
            PomodoroService.OnPhaseChanged += () => Dispatcher.InvokeAsync(RefreshConnectivity);
        }
        RefreshAll();
    }

    public void RefreshSliders()
    {
        if (_island == null || _updatingSliders || _isDraggingVolume || _isDraggingBrightness) return;
        _updatingSliders = true;
        try
        {
            // Громкость
            var (volLevel, muted) = AudioService.GetVolume();
            int volPct = (int)Math.Round(volLevel * 100);
            VolumeSlider.Value = volPct;
            VolumePercentText.Text = muted ? Loc.T("Без звука", "Muted", "Sessiz") : $"{volPct}%";
            if (VolumeIcon != null)
            {
                VolumeIcon.Text = muted || volPct == 0 ? "\uE74F" : (volPct < 33 ? "\uE992" : (volPct < 66 ? "\uE994" : "\uE767"));
            }
            if (MuteToggleIcon != null) MuteToggleIcon.Text = muted ? "\uE74F" : "\uE767";
            if (MuteToggleText != null) MuteToggleText.Text = muted ? Loc.T("Включить звук", "Unmute", "Sesi Aç") : Loc.T("Звук включен", "Audio On", "Ses Açık");

            // Яркость
            if (_lastObservedBrightness >= 0)
            {
                BrightnessSlider.Value = _lastObservedBrightness;
                BrightnessPercentText.Text = $"{_lastObservedBrightness}%";
            }
            else
            {
                Task.Run(() =>
                {
                    try
                    {
                        int br = DeviceService.GetBrightness();
                        _lastObservedBrightness = br;
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (!_isDraggingBrightness && !_updatingSliders)
                            {
                                BrightnessSlider.Value = br;
                                BrightnessPercentText.Text = $"{br}%";
                            }
                        });
                    }
                    catch { }
                });
            }
        }
        catch { }
        finally
        {
            _updatingSliders = false;
        }
    }

    public void RefreshSystemAndWeather()
    {
        if (_island == null) return;

        // Погода
        var w = Sections.Weather;
        if (w.Data != null)
        {
            string sign = w.Data.Temp > 0 ? "+" : "";
            WeatherTempText.Text = $"{sign}{Math.Round(w.Data.Temp)}°";
            WeatherCityText.Text = w.Data.Description;
        }

        // Система (Кольца активности Apple Activity Rings)
        var snap = _island.Snapshot;
        if (snap != null)
        {
            int cpuVal = (int)Math.Round(snap.CpuFraction * 100);
            int ramVal = (int)Math.Round(snap.MemoryFraction * 100);

            if (CpuPercentText != null) CpuPercentText.Text = $"{cpuVal}%";
            if (RamPercentText != null) RamPercentText.Text = $"{ramVal}%";

            if (CpuRingPath != null) CpuRingPath.Data = CreateRingGeometry(32, 3, snap.CpuFraction);
            if (RamRingPath != null) RamRingPath.Data = CreateRingGeometry(32, 3, snap.MemoryFraction);
        }

        PollBrightnessIfIdle();
    }

    private static Geometry CreateRingGeometry(double size, double stroke, double fraction)
    {
        fraction = Math.Clamp(fraction, 0.001, 0.9999);
        double r = (size - stroke) / 2.0;
        double cx = size / 2.0;
        double cy = size / 2.0;
        double angle = fraction * 360.0;
        double rad = (angle - 90.0) * Math.PI / 180.0;

        Point start = new(cx, cy - r);
        Point end = new(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        bool isLargeArc = angle > 180.0;

        var fig = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        fig.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise,
            RotationAngle = 0
        });

        var geo = new PathGeometry();
        geo.Figures.Add(fig);

        // Фиксируем границы PathGeometry точно в [0, 0, size, size]
        // без отрисовки, чтобы WPF не смещал дугу при малых процентах
        var boundsFig = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = false,
            IsFilled = false
        };
        boundsFig.Segments.Add(new LineSegment(new Point(size, size), isStroked: false));
        geo.Figures.Add(boundsFig);

        geo.Freeze();
        return geo;
    }

    private string? _activeQuickSubMenu;

    private void OnWifiClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        Sections.Settings.Toggle(_island, "WiFi");
        RefreshConnectivity();
    }

    private void OnWifiChevronClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OpenQuickSubMenu("wifi");
    }

    private void OnBluetoothClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        Sections.Settings.Toggle(_island, "Bluetooth");
        RefreshConnectivity();
    }

    private void OnBluetoothChevronClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OpenQuickSubMenu("bluetooth");
    }

    private void OnQuickSubMenuBackClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseQuickSubMenu();
    }

    private void OnQuickSubMenuRefreshClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_activeQuickSubMenu is "wifi" or "bluetooth")
        {
            OpenQuickSubMenu(_activeQuickSubMenu);
        }
    }

    private void CloseQuickSubMenu()
    {
        _activeQuickSubMenu = null;
        if (QuickSubMenuHeader != null) QuickSubMenuHeader.Visibility = Visibility.Collapsed;
        if (QuickSubMenuPanel != null) QuickSubMenuPanel.Visibility = Visibility.Collapsed;
        if (QuickDefaultHeader != null) QuickDefaultHeader.Visibility = Visibility.Visible;
        if (QuickGridPanel != null) QuickGridPanel.Visibility = Visibility.Visible;
    }

    private void OpenQuickSubMenu(string mode)
    {
        _activeQuickSubMenu = mode;
        if (QuickDefaultHeader != null) QuickDefaultHeader.Visibility = Visibility.Collapsed;
        if (QuickGridPanel != null) QuickGridPanel.Visibility = Visibility.Collapsed;
        if (QuickSubMenuHeader != null) QuickSubMenuHeader.Visibility = Visibility.Visible;
        if (QuickSubMenuPanel != null) QuickSubMenuPanel.Visibility = Visibility.Visible;
        if (QuickSubMenuBackText != null) QuickSubMenuBackText.Text = Loc.T("Назад", "Back", "Geri");

        if (mode == "wifi")
        {
            if (QuickSubMenuTitleText != null) QuickSubMenuTitleText.Text = Loc.T("Сети Wi-Fi", "Wi-Fi Networks", "Wi-Fi Ağları");
            _ = LoadWifiSubMenuAsync();
        }
        else
        {
            if (QuickSubMenuTitleText != null) QuickSubMenuTitleText.Text = Loc.T("Bluetooth", "Bluetooth", "Bluetooth");
            _ = LoadBluetoothSubMenuAsync();
        }
    }

    private async System.Threading.Tasks.Task LoadWifiSubMenuAsync()
    {
        if (QuickSubMenuItemsPanel == null || QuickSubMenuEmptyText == null) return;
        QuickSubMenuItemsPanel.Children.Clear();
        QuickSubMenuEmptyText.Text = Loc.T("Поиск сетей Wi-Fi...", "Scanning Wi-Fi...", "Wi-Fi taranıyor...");
        QuickSubMenuEmptyText.Visibility = Visibility.Visible;

        var nets = await DeviceService.GetWifiNetworksAsync();
        if (_activeQuickSubMenu != "wifi") return;

        QuickSubMenuItemsPanel.Children.Clear();
        if (nets.Count == 0)
        {
            QuickSubMenuEmptyText.Text = Loc.T("Сети не найдены или Wi-Fi выключен", "No networks found or Wi-Fi is off", "Ağ bulunamadı");
            QuickSubMenuEmptyText.Visibility = Visibility.Visible;
            return;
        }

        QuickSubMenuEmptyText.Visibility = Visibility.Collapsed;
        foreach (var net in nets)
        {
            string sigGlyph = net.SignalPercent >= 75 ? "\uE701" : (net.SignalPercent >= 45 ? "\uEC3F" : "\uEC3E");
            string rightBadge = net.IsConnected
                ? Loc.T("Подключено ✓", "Connected ✓", "Bağlı ✓")
                : (net.IsSaved ? $"{net.SignalPercent}%" : $"🔒 {net.SignalPercent}%");

            var row = CreateSubMenuRow(sigGlyph, net.Ssid, rightBadge, net.IsConnected, async () =>
            {
                if (net.IsConnected) return;
                string? password = null;
                if (!net.IsSaved && net.IsSecure && _island != null)
                {
                    password = AskDialog.Show(
                        _island,
                        "Wi-Fi",
                        Loc.T($"Пароль для «{net.Ssid}»", $"Password for \"{net.Ssid}\"", $"\"{net.Ssid}\" şifresi"),
                        "");
                    if (string.IsNullOrEmpty(password)) return;
                }

                if (QuickSubMenuEmptyText != null)
                {
                    QuickSubMenuItemsPanel.Children.Clear();
                    QuickSubMenuEmptyText.Text = Loc.T($"Подключение к {net.Ssid}...", $"Connecting to {net.Ssid}...", $"Bağlanıyor: {net.Ssid}...");
                    QuickSubMenuEmptyText.Visibility = Visibility.Visible;
                }

                await DeviceService.ConnectWifiAsync(net.Ssid, password);
                await System.Threading.Tasks.Task.Delay(900);
                if (_activeQuickSubMenu == "wifi")
                {
                    await LoadWifiSubMenuAsync();
                }
                RefreshConnectivity();
            });
            QuickSubMenuItemsPanel.Children.Add(row);
        }
    }

    private async System.Threading.Tasks.Task LoadBluetoothSubMenuAsync()
    {
        if (QuickSubMenuItemsPanel == null || QuickSubMenuEmptyText == null) return;
        QuickSubMenuItemsPanel.Children.Clear();
        QuickSubMenuEmptyText.Text = Loc.T("Поиск устройств...", "Scanning devices...", "Cihazlar aranıyor...");
        QuickSubMenuEmptyText.Visibility = Visibility.Visible;

        var devs = await DeviceService.GetBluetoothDevicesAsync();
        if (_activeQuickSubMenu != "bluetooth") return;

        QuickSubMenuItemsPanel.Children.Clear();
        if (devs.Count == 0)
        {
            QuickSubMenuEmptyText.Text = Loc.T("Нет сопряжённых устройств", "No paired devices", "Eşleşmiş cihaz yok");
            QuickSubMenuEmptyText.Visibility = Visibility.Visible;
            return;
        }

        QuickSubMenuEmptyText.Visibility = Visibility.Collapsed;
        foreach (var dev in devs)
        {
            string rightBadge = dev.IsConnected
                ? Loc.T("Подключено ✓", "Connected ✓", "Bağlı ✓")
                : Loc.T("Подключить", "Connect", "Bağlan");

            var row = CreateSubMenuRow(dev.IconGlyph, dev.Name, rightBadge, dev.IsConnected, async () =>
            {
                if (QuickSubMenuEmptyText != null)
                {
                    QuickSubMenuItemsPanel.Children.Clear();
                    QuickSubMenuEmptyText.Text = Loc.T($"Подключение к {dev.Name}...", $"Connecting to {dev.Name}...", $"Bağlanıyor: {dev.Name}...");
                    QuickSubMenuEmptyText.Visibility = Visibility.Visible;
                }

                await DeviceService.ConnectBluetoothDeviceAsync(dev.Id, dev.Name);
                await System.Threading.Tasks.Task.Delay(700);
                if (_activeQuickSubMenu == "bluetooth")
                {
                    await LoadBluetoothSubMenuAsync();
                }
                RefreshConnectivity();
            });
            QuickSubMenuItemsPanel.Children.Add(row);
        }
    }

    private UIElement CreateSubMenuRow(string iconGlyph, string title, string badgeText, bool isActive, Func<System.Threading.Tasks.Task> onClickAsync)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = isActive
                ? new SolidColorBrush(Color.FromArgb(0x2E, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B))
                : new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            BorderBrush = isActive ? Theme.AccentBrush : Brushes.Transparent
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13.5,
            Foreground = isActive ? Theme.AccentBrush : Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(icon, 0);

        var nameBlock = new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(nameBlock, 1);

        var badgeBlock = new TextBlock
        {
            Text = badgeText,
            FontSize = 10,
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isActive
                ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))
                : new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(badgeBlock, 2);

        grid.Children.Add(icon);
        grid.Children.Add(nameBlock);
        grid.Children.Add(badgeBlock);
        border.Child = grid;

        border.MouseEnter += (_, _) =>
        {
            if (!isActive)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            }
        };
        border.MouseLeave += (_, _) =>
        {
            if (!isActive)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
            }
        };
        border.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            await onClickAsync();
        };

        return border;
    }

    private void OnSnipClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        _island.Collapse();
        _island.RunLater(240, () =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
            }
            catch { }
        });
    }

    private void OnPomodoroTileClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        _island.Select("pomodoro");
    }

    private void OnPomodoroTileRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_island == null) return;
        PomodoroService.Initialize(_island);
        PomodoroService.Toggle();
        RefreshConnectivity();
    }

    private void OnNotesTileClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        _island.Select("calendar");
    }

    private void OnFilesTileClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        _island.Select("links");
    }

    private void OnFilesTileRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_island == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Выберите файл для конвертации или закрепления", "Select file to convert or pin", "Dönüştürmek veya sabitlemek için dosya seçin"),
            Multiselect = true,
            Filter = Loc.T("Все файлы (*.*)|*.*|Изображения (*.png;*.jpg;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|Программы (*.exe;*.lnk)|*.exe;*.lnk",
                           "All Files (*.*)|*.*|Images (*.png;*.jpg;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|Programs (*.exe;*.lnk)|*.exe;*.lnk",
                           "Tüm Dosyalar (*.*)|*.*|Görseller (*.png;*.jpg;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|Programlar (*.exe;*.lnk)|*.exe;*.lnk")
        };
        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            OpenFileConvertSubMenu(dlg.FileNames);
        }
    }

    private void OnFilesTileDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (FilesIndicator != null && FilesIcon != null)
            {
                StyleTile(FilesIndicator, FilesIcon, true);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnFilesTileDrop(object sender, DragEventArgs e)
    {
        if (_island == null) return;
        if (FilesIndicator != null && FilesIcon != null)
        {
            StyleTile(FilesIndicator, FilesIcon, false);
        }
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            OpenFileConvertSubMenu(files);
        }
        e.Handled = true;
    }

    private void OpenFileConvertSubMenu(string[] files)
    {
        if (_island == null || QuickSubMenuItemsPanel == null || QuickSubMenuEmptyText == null) return;

        _activeQuickSubMenu = "convert";
        if (QuickDefaultHeader != null) QuickDefaultHeader.Visibility = Visibility.Collapsed;
        if (QuickGridPanel != null) QuickGridPanel.Visibility = Visibility.Collapsed;
        if (QuickSubMenuHeader != null) QuickSubMenuHeader.Visibility = Visibility.Visible;
        if (QuickSubMenuPanel != null) QuickSubMenuPanel.Visibility = Visibility.Visible;
        if (QuickSubMenuBackText != null) QuickSubMenuBackText.Text = Loc.T("Назад", "Back", "Geri");

        string titleLabel = files.Length == 1
            ? System.IO.Path.GetFileName(files[0])
            : $"{files.Length} {Loc.T("файл.", "files", "dosya")}";
        if (QuickSubMenuTitleText != null) QuickSubMenuTitleText.Text = $"⚡ {titleLabel}";

        QuickSubMenuEmptyText.Visibility = Visibility.Collapsed;
        QuickSubMenuItemsPanel.Children.Clear();

        var options = ShiftConvertService.GetOptionsForFiles(files, forWheel: false);
        if (!options.Any(o => o.Key == "links"))
        {
            options.Add(new ShiftConvertOption
            {
                Key = "links",
                Label = Loc.T("В быстрые ссылки", "Pin to Quick Links", "Hızlı Bağlantılara Ekle"),
                SubLabel = Loc.T("Полка", "Tray", "Raf"),
                Icon = "📌",
                Accent = Theme.Accent
            });
        }

        foreach (var opt in options)
        {
            string badge = opt.SubLabel;
            string rowTitle = opt.Key switch
            {
                "links" or "compress" or "clipboard" or "unzip" => opt.Label,
                "ocr" => Loc.T("Распознать текст (OCR)", "Extract Text (OCR)", "Metni Tanı (OCR)"),
                "square" => Loc.T("Обрезать 1:1 (Квадрат)", "Square Crop 1:1", "Kare Kırp 1:1"),
                "grayscale" => Loc.T("Сделать чёрно-белым", "Convert to B&W", "Siyah-Beyaz Yap"),
                "base64" => Loc.T("Скопировать Base64 код", "Copy Base64 Code", "Base64 Kopyala"),
                "sha256" => Loc.T("Скопировать SHA-256 хеш", "Copy SHA-256 Hash", "SHA-256 Kopyala"),
                "zip" => Loc.T("Упаковать в ZIP-архив", "Pack to ZIP Archive", "ZIP Arşivine Ekle"),
                "format_json" => Loc.T("Форматировать JSON", "Format JSON", "JSON Biçimlendir"),
                _ => $"{Loc.T("Конвертировать в", "Convert to", "Dönüştür:")} {opt.Label}"
            };

            var row = CreateSubMenuRow("\uE8AB", $"{opt.Icon}  {rowTitle}", badge, false, () =>
            {
                CloseQuickSubMenu();
                _island.Collapse();
                ShiftWheelWindow.ExecuteConversion(_island, files, opt);
                return System.Threading.Tasks.Task.CompletedTask;
            });
            QuickSubMenuItemsPanel.Children.Add(row);
        }
    }

    private void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders || _island == null || VolumeSlider == null || VolumePercentText == null) return;
        _lastUserVolumeTime = DateTime.UtcNow;
        float level = (float)(VolumeSlider.Value / 100.0);
        AudioService.SetVolume(level);
        int volPct = (int)Math.Round(VolumeSlider.Value);
        VolumePercentText.Text = $"{volPct}%";
        if (VolumeIcon != null)
        {
            VolumeIcon.Text = volPct == 0 ? "\uE74F" : (volPct < 33 ? "\uE992" : (volPct < 66 ? "\uE994" : "\uE767"));
        }
    }

    private void OnBrightnessSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders || _island == null || BrightnessSlider == null || BrightnessPercentText == null) return;
        _lastUserBrightnessTime = DateTime.UtcNow;
        int level = (int)Math.Round(BrightnessSlider.Value);
        _lastObservedBrightness = level;
        _island.Nsp("brightness").Target = level / 100.0;
        DeviceService.SetBrightness(level);
        BrightnessPercentText.Text = $"{level}%";
    }

    private void OnVolumeCapsuleMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            _isDraggingVolume = true;
            _lastUserVolumeTime = DateTime.UtcNow;
            _island?.StopLeaveTimer();
            fe.CaptureMouse();
            UpdateVolumeFromCapsule(fe, e.GetPosition(fe).X);
            e.Handled = true;
        }
    }

    private void OnVolumeCapsuleMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingVolume && sender is FrameworkElement fe && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            _lastUserVolumeTime = DateTime.UtcNow;
            _island?.StopLeaveTimer();
            UpdateVolumeFromCapsule(fe, e.GetPosition(fe).X);
            e.Handled = true;
        }
    }

    private void OnVolumeCapsuleMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingVolume && sender is FrameworkElement fe)
        {
            _lastUserVolumeTime = DateTime.UtcNow;
            fe.ReleaseMouseCapture();
            _isDraggingVolume = false;
            e.Handled = true;
        }
    }

    private void UpdateVolumeFromCapsule(FrameworkElement container, double mouseX)
    {
        double width = container.ActualWidth;
        if (width <= 0) return;
        double fraction = Math.Clamp(mouseX / width, 0.0, 1.0);
        int volPct = (int)Math.Round(fraction * 100);
        _lastUserVolumeTime = DateTime.UtcNow;
        _volumeAnimGen++;
        _updatingSliders = false;
        VolumeSlider.BeginAnimation(Slider.ValueProperty, null);
        VolumeSlider.Value = volPct;
    }

    private void OnBrightnessCapsuleMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            _isDraggingBrightness = true;
            _lastUserBrightnessTime = DateTime.UtcNow;
            _island?.StopLeaveTimer();
            fe.CaptureMouse();
            UpdateBrightnessFromCapsule(fe, e.GetPosition(fe).X);
            e.Handled = true;
        }
    }

    private void OnBrightnessCapsuleMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingBrightness && sender is FrameworkElement fe && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            _lastUserBrightnessTime = DateTime.UtcNow;
            _island?.StopLeaveTimer();
            UpdateBrightnessFromCapsule(fe, e.GetPosition(fe).X);
            e.Handled = true;
        }
    }

    private void OnBrightnessCapsuleMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingBrightness && sender is FrameworkElement fe)
        {
            _lastUserBrightnessTime = DateTime.UtcNow;
            fe.ReleaseMouseCapture();
            _isDraggingBrightness = false;
            e.Handled = true;
        }
    }

    private void UpdateBrightnessFromCapsule(FrameworkElement container, double mouseX)
    {
        double width = container.ActualWidth;
        if (width <= 0) return;
        double fraction = Math.Clamp(mouseX / width, 0.0, 1.0);
        int brPct = (int)Math.Round(fraction * 100);
        _lastUserBrightnessTime = DateTime.UtcNow;
        _brightnessAnimGen++;
        _updatingSliders = false;
        BrightnessSlider.BeginAnimation(Slider.ValueProperty, null);
        BrightnessSlider.Value = brPct;
    }

    private void OnMediaPrevClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _island?.PrevTrack();
    }

    private bool? _lastHomePlaying;

    private void UpdatePlayPauseIcon(bool playing)
    {
        if (HomePauseIcon == null || HomePlayIcon == null) return;
        if (_lastHomePlaying == playing) return;

        bool isFirst = _lastHomePlaying == null;
        _lastHomePlaying = playing;

        if (isFirst)
        {
            HomePauseIcon.Opacity = playing ? 1.0 : 0.0;
            HomePauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            HomePlayIcon.Opacity = playing ? 0.0 : 1.0;
            HomePlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
            if (HomePauseScale != null) { HomePauseScale.ScaleX = 1.0; HomePauseScale.ScaleY = 1.0; }
            if (HomePlayScale != null) { HomePlayScale.ScaleX = 1.0; HomePlayScale.ScaleY = 1.0; }
            return;
        }

        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
        var fadeOut = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
        var scaleUp = new DoubleAnimation(0.72, 1.0, duration) { EasingFunction = ease };
        var scaleDown = new DoubleAnimation(1.0, 0.72, duration) { EasingFunction = ease };

        if (playing)
        {
            HomePauseIcon.Visibility = Visibility.Visible;
            HomePlayIcon.Visibility = Visibility.Visible;

            HomePauseIcon.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            HomePauseScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            HomePauseScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);

            fadeOut.Completed += (_, _) =>
            {
                if (_lastHomePlaying == true) HomePlayIcon.Visibility = Visibility.Collapsed;
            };
            HomePlayIcon.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            HomePlayScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
            HomePlayScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
        }
        else
        {
            HomePauseIcon.Visibility = Visibility.Visible;
            HomePlayIcon.Visibility = Visibility.Visible;

            HomePlayIcon.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            HomePlayScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            HomePlayScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);

            fadeOut.Completed += (_, _) =>
            {
                if (_lastHomePlaying == false) HomePauseIcon.Visibility = Visibility.Collapsed;
            };
            HomePauseIcon.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            HomePauseScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
            HomePauseScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
        }
    }

    private void OnMediaPlayPauseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _island?.TogglePlay();
    }

    private void OnMediaNextClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _island?.NextTrack();
    }

    private void OnMediaCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        _island?.Select("media");
    }

    private void OnWeatherCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        _island?.Select("weather");
    }

    private void OnSystemCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        _island?.Select("system");
    }

    private void OnSoundHeaderClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        _island?.Select("sound");
    }

    private void OnMuteToggleClick(object sender, RoutedEventArgs e)
    {
        var (_, muted) = AudioService.GetVolume();
        AudioService.SetMute(!muted);
        RefreshSliders();
    }
}

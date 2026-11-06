using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickNotchCs;

public partial class MediaView : UserControl
{
    private IslandView? _island;
    private bool _isDraggingSeek = false;
    private bool _isDraggingQuickVol = false;
    private bool _updatingVolume = false;

    private DispatcherTimer? _mediaProgressTimer;

    public MediaView()
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
                UpdatePlaybackProgress();
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

    private void UpdatePlaybackProgress()
    {
        if (_island == null || _isDraggingSeek || SeekSlider == null || TimeElapsedText == null) return;
        var m = _island.Media;
        if (!m.Playing || string.IsNullOrEmpty(m.Title)) return;
        double dur = m.Dur > 0 ? m.Dur : 210.0;
        double pos = _island.MediaPos();
        SeekSlider.Maximum = dur;
        SeekSlider.Value = Math.Clamp(pos, 0.0, dur);
        TimeElapsedText.Text = Ui.FmtTime(pos);
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

    public void ApplyAccent()
    {
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_island == null) return;

        var m = _island.Media;

        // 1. Обложка
        if (_island.CoverBrush != null)
        {
            CoverImageBorder.Background = _island.CoverBrush;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            CoverImageBorder.Background = Brushes.Transparent;
            CoverPlaceholder.Visibility = Visibility.Visible;
        }

        // 2. Название трека и исполнитель
        bool hasTitle = !string.IsNullOrWhiteSpace(m.Title);
        TrackTitleText.Text = hasTitle ? m.Title : Loc.T("Ничего не играет", "Not Playing", "Çalınmıyor");
        ArtistText.Text = hasTitle ? m.Artist : Loc.T("Запустите воспроизведение в любом плеере", "Start playback in any media player", "Herhangi bir oynatıcıda çalın");

        // 3. Бейдж приложения
        if (!string.IsNullOrWhiteSpace(m.App))
        {
            AppBadge.Visibility = Visibility.Visible;
            AppBadgeText.Text = m.App;
        }
        else
        {
            AppBadge.Visibility = Visibility.Collapsed;
        }

        // 4. Кнопка Play / Pause
        if (PlayPauseIcon != null)
        {
            PlayPauseIcon.Text = m.Playing ? "\uE769" : "\uE768";
            PlayPauseIcon.Margin = m.Playing ? new Thickness(0) : new Thickness(2, 0, 0, 0);
        }

        // 5. Позиция и слайдер трека
        if (!_isDraggingSeek)
        {
            double dur = m.Dur > 0 ? m.Dur : 210.0;
            double pos = _island.MediaPos();
            SeekSlider.Maximum = dur;
            SeekSlider.Value = Math.Clamp(pos, 0.0, dur);

            TimeElapsedText.Text = hasTitle ? Ui.FmtTime(pos) : "0:00";
            TimeTotalText.Text = hasTitle ? Ui.FmtTime(dur) : "0:00";
        }

        // 6. Быстрая громкость и устройство
        RefreshQuickVolume();
        CheckMediaProgressTimer();
    }

    private void RefreshQuickVolume()
    {
        if (_updatingVolume) return;
        _updatingVolume = true;
        try
        {
            var (volLevel, muted) = AudioService.GetVolume();
            int volPct = (int)Math.Round(volLevel * 100);
            QuickVolumeSlider.Value = volPct;
            QuickVolumeText.Text = muted ? Loc.T("Mute", "Mute", "Mute") : $"{volPct}%";

            Bg.Run(AudioService.ListOutputDevices, (res, err) =>
            {
                if (err != null || res.Devices == null) return;
                var (devices, defaultId) = res;
                var active = devices.FirstOrDefault(d => d.Id == defaultId);
                if (active != null)
                {
                    string devName = active.Name;
                    if (devName.Length > 28) devName = devName[..26] + "…";
                    ActiveDeviceText.Text = devName;
                }
                else
                {
                    ActiveDeviceText.Text = Loc.T("Динамики", "Speakers", "Hoparlör");
                }
            });
        }
        catch { }
        finally
        {
            _updatingVolume = false;
        }
    }

    private void OnQuickVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolume || _island == null || QuickVolumeText == null || QuickVolumeSlider == null) return;
        float level = (float)(QuickVolumeSlider.Value / 100.0);
        AudioService.SetVolume(level);
        QuickVolumeText.Text = $"{(int)Math.Round(QuickVolumeSlider.Value)}%";
    }

    private void OnQuickVolMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingQuickVol = true;
            _island?.StopLeaveTimer();
            fe.CaptureMouse();
            UpdateQuickVolFromMouse(fe, e.GetPosition(fe).X);
            e.Handled = true;
        }
    }

    private void OnQuickVolMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingQuickVol && sender is FrameworkElement fe && e.LeftButton == MouseButtonState.Pressed)
        {
            _island?.StopLeaveTimer();
            UpdateQuickVolFromMouse(fe, e.GetPosition(fe).X);
            e.Handled = true;
        }
    }

    private void OnQuickVolMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingQuickVol && sender is FrameworkElement fe)
        {
            _isDraggingQuickVol = false;
            fe.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnQuickVolLostCapture(object sender, MouseEventArgs e)
    {
        _isDraggingQuickVol = false;
    }

    private void UpdateQuickVolFromMouse(FrameworkElement fe, double mouseX)
    {
        double width = fe.ActualWidth;
        if (width <= 0 || QuickVolumeSlider == null) return;
        double fraction = Math.Clamp((mouseX - 8.0) / Math.Max(1.0, width - 16.0), 0.0, 1.0);
        _updatingVolume = false;
        QuickVolumeSlider.Value = Math.Round(fraction * 100.0);
    }

    private void OnSeekBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingSeek = true;
            _island?.StopLeaveTimer();
            fe.CaptureMouse();
            UpdateSeekFromMouse(fe, e.GetPosition(fe).X);
            e.Handled = true;
        }
    }

    private void OnSeekBarMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSeek && sender is FrameworkElement fe && e.LeftButton == MouseButtonState.Pressed)
        {
            _island?.StopLeaveTimer();
            UpdateSeekFromMouse(fe, e.GetPosition(fe).X);
            e.Handled = true;
        }
    }

    private void OnSeekBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSeek && sender is FrameworkElement fe)
        {
            UpdateSeekFromMouse(fe, e.GetPosition(fe).X);
            _isDraggingSeek = false;
            fe.ReleaseMouseCapture();
            CommitSeek();
            e.Handled = true;
        }
    }

    private void OnSeekBarLostCapture(object sender, MouseEventArgs e)
    {
        if (_isDraggingSeek)
        {
            _isDraggingSeek = false;
            CommitSeek();
        }
    }

    private void UpdateSeekFromMouse(FrameworkElement fe, double mouseX)
    {
        double width = fe.ActualWidth;
        if (width <= 0 || SeekSlider == null) return;
        double fraction = Math.Clamp((mouseX - 8.0) / Math.Max(1.0, width - 16.0), 0.0, 1.0);
        double dur = SeekSlider.Maximum > 0 ? SeekSlider.Maximum : 210.0;
        double targetSec = fraction * dur;
        SeekSlider.Value = targetSec;
        if (TimeElapsedText != null) TimeElapsedText.Text = Ui.FmtTime(targetSec);
    }

    private void CommitSeek()
    {
        if (_island == null || SeekSlider == null) return;
        _island.SeekTo(SeekSlider.Value);
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _island?.TogglePlay();
    }

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _island?.PrevTrack();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _island?.NextTrack();
    }

    private void OnRewind10Click(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        double current = _island.MediaPos();
        double target = Math.Max(0.0, current - 10.0);
        _island.SeekTo(target);
    }

    private void OnForward10Click(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        double current = _island.MediaPos();
        double dur = _island.Media.Dur > 0 ? _island.Media.Dur : 210.0;
        double target = Math.Min(dur, current + 10.0);
        _island.SeekTo(target);
    }
}


using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class MediaView : UserControl
{
    private IslandView? _island;
    private bool _isDraggingSeek = false;
    private bool _updatingVolume = false;

    public MediaView()
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
        PlayPauseIcon.Text = m.Playing ? "❚❚" : "▶";
        PlayPauseIcon.Margin = m.Playing ? new Thickness(0) : new Thickness(3, 0, 0, 0);

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

            var (devices, defaultId) = AudioService.ListOutputDevices();
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
        }
        catch { }
        finally
        {
            _updatingVolume = false;
        }
    }

    private void OnQuickVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolume) return;
        float level = (float)(QuickVolumeSlider.Value / 100.0);
        AudioService.SetVolume(level);
        QuickVolumeText.Text = $"{(int)Math.Round(QuickVolumeSlider.Value)}%";
    }

    private void OnSeekMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeek = true;
    }

    private void OnSeekMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_island == null) return;
        _isDraggingSeek = false;
        double targetSec = SeekSlider.Value;
        _island.Media.Pos = targetSec;
        _island.Media.Stamp = DateTime.UtcNow;
        _ = MediaService.SeekAsync(targetSec);
    }

    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSeek)
        {
            TimeElapsedText.Text = Ui.FmtTime(SeekSlider.Value);
        }
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        _island?.TogglePlay();
        if (_island != null)
        {
            _island.Media.Playing = !_island.Media.Playing;
            PlayPauseIcon.Text = _island.Media.Playing ? "❚❚" : "▶";
            PlayPauseIcon.Margin = _island.Media.Playing ? new Thickness(0) : new Thickness(3, 0, 0, 0);
        }
    }

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        _island?.PrevTrack();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        _island?.NextTrack();
    }

    private void OnRewind10Click(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        double current = _island.MediaPos();
        double target = Math.Max(0.0, current - 10.0);
        _island.Media.Pos = target;
        _island.Media.Stamp = DateTime.UtcNow;
        _ = MediaService.SeekAsync(target);
        SeekSlider.Value = target;
        TimeElapsedText.Text = Ui.FmtTime(target);
    }

    private void OnForward10Click(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        double current = _island.MediaPos();
        double dur = _island.Media.Dur > 0 ? _island.Media.Dur : 210.0;
        double target = Math.Min(dur, current + 10.0);
        _island.Media.Pos = target;
        _island.Media.Stamp = DateTime.UtcNow;
        _ = MediaService.SeekAsync(target);
        SeekSlider.Value = target;
        TimeElapsedText.Text = Ui.FmtTime(target);
    }
}

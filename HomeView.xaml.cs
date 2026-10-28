using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class HomeView : UserControl
{
    private IslandView? _island;
    private bool _updatingSliders = false;

    public HomeView()
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
        if (_island == null) return;
        RefreshMedia();
        RefreshConnectivity();
        RefreshSliders();
        RefreshSystemAndWeather();
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
            PlayPauseIcon.Text = "▶";
            AlbumCoverImage.Source = null;
        }
        else
        {
            TrackTitleText.Text = m.Title;
            TrackArtistText.Text = string.IsNullOrEmpty(m.Artist) ? "—" : m.Artist;
            MediaAppBadge.Text = m.App ?? "";
            PlayPauseIcon.Text = m.Playing ? "⏸" : "▶";
            if (_island.CoverBrush is ImageBrush ib)
            {
                AlbumCoverImage.Source = ib.ImageSource;
            }
        }
    }

    public void RefreshConnectivity()
    {
        if (_island == null) return;
        var sett = Sections.Settings;

        // Wi-Fi
        bool? wifi = sett.States["WiFi"];
        bool wifiOn = wifi == true;
        WifiIndicator.Background = wifiOn ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("Surface2Brush");
        WifiStatusText.Text = wifi == null ? "—" : (wifiOn ? Loc.T("Вкл", "On", "Açık") : Loc.T("Выкл", "Off", "Kapalı"));

        // Bluetooth
        bool? bt = sett.States["Bluetooth"];
        bool btOn = bt == true;
        BtIndicator.Background = btOn ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("Surface2Brush");
        BtStatusText.Text = bt == null ? "—" : (btOn ? Loc.T("Вкл", "On", "Açık") : Loc.T("Выкл", "Off", "Kapalı"));

        // Тёмная тема
        ThemeStatusText.Text = sett.Dark ? Loc.T("Тёмная", "Dark", "Karanlık") : Loc.T("Светлая", "Light", "Açık");

        // Бодрствование
        bool awake = _island.S.KeepAwake;
        AwakeIndicator.Background = awake ? (Brush)FindResource("AccentHotBrush") : (Brush)FindResource("Surface2Brush");
        AwakeStatusText.Text = awake ? Loc.T("Вкл", "On", "Açık") : Loc.T("Выкл", "Off", "Kapalı");
    }

    public void RefreshSliders()
    {
        if (_island == null || _updatingSliders) return;
        _updatingSliders = true;
        try
        {
            // Громкость
            var (volLevel, muted) = AudioService.GetVolume();
            int volPct = (int)Math.Round(volLevel * 100);
            VolumeSlider.Value = volPct;
            VolumePercentText.Text = muted ? Loc.T("Без звука", "Muted", "Sessiz") : $"{volPct}%";

            // Яркость
            double brVal = Ui.Clamp(_island.Nsp("brightness").Target);
            int bright = (int)Math.Round(brVal * 100);
            BrightnessSlider.Value = bright;
            BrightnessPercentText.Text = $"{bright}%";
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

        // Система
        var snap = _island.Snapshot;
        if (snap != null)
        {
            CpuText.Text = $"CPU: {snap.Cpu}";
            CpuBar.Value = snap.CpuFraction * 100;
            RamText.Text = $"RAM: {snap.MemoryPercent}";
            RamBar.Value = snap.MemoryFraction * 100;
        }
    }

    private void OnWifiClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        Sections.Settings.Toggle(_island, "WiFi");
        RefreshConnectivity();
    }

    private void OnBluetoothClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        Sections.Settings.Toggle(_island, "Bluetooth");
        RefreshConnectivity();
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        Sections.Settings.ToggleDark(_island);
        RefreshConnectivity();
    }

    private void OnKeepAwakeClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        _island.SetKeepAwake(!_island.S.KeepAwake);
        RefreshConnectivity();
    }

    private void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders || _island == null) return;
        float level = (float)(VolumeSlider.Value / 100.0);
        AudioService.SetVolume(level);
        VolumePercentText.Text = $"{(int)Math.Round(VolumeSlider.Value)}%";
    }

    private void OnBrightnessSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSliders || _island == null) return;
        int level = (int)Math.Round(BrightnessSlider.Value);
        _island.Nsp("brightness").Target = level / 100.0;
        Bg.Run(() => { DeviceService.SetBrightness(level); return true; }, (_, _) => { });
        BrightnessPercentText.Text = $"{level}%";
    }

    private void OnMediaPrevClick(object sender, RoutedEventArgs e)
    {
        _island?.PrevTrack();
    }

    private void OnMediaPlayPauseClick(object sender, RoutedEventArgs e)
    {
        _island?.TogglePlay();
    }

    private void OnMediaNextClick(object sender, RoutedEventArgs e)
    {
        _island?.NextTrack();
    }
}


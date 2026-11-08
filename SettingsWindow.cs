using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuickNotchCs;

/// <summary>
/// Автономное окно настроек QuickNotch в эстетике True-Black Obsidian Glass:
/// - Единая глубина #0A0A0E с тонкими разделителями 1px #1E1E26.
/// - Карточки #121217 (CornerRadius 14) с деликатными обводками.
/// - Боковой сайдбар с 6 разделами (Уведомления, Чёлка, Общие, Оформление, ShiftConvert, О программе).
/// - Выбор звука уведомлений с кнопками прослушивания и тестовым тостом.
/// - 0% CPU в режиме простоя (Addy Osmani Production Standards).
/// </summary>
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    public static void ShowOrActivate(IslandView island)
    {
        if (_instance != null && _instance.IsLoaded)
        {
            _instance.Activate();
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Focus();
            return;
        }

        _instance = new SettingsWindow(island);
        _instance.Show();
        _instance.Activate();
    }

    private readonly IslandView _island;
    private int _activeTab;

    // Палитра True-Black Obsidian
    private static readonly SolidColorBrush BgGround = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0E)));
    private static readonly SolidColorBrush BgSidebar = Freeze(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x13)));
    private static readonly SolidColorBrush BgSurface = Freeze(new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x18)));
    private static readonly SolidColorBrush BgSurfaceHover = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x22)));
    private static readonly SolidColorBrush BgNavActive = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x26)));
    private static readonly SolidColorBrush BgRaised = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2C)));
    private static readonly SolidColorBrush BgRaisedHover = Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x38)));
    private static readonly SolidColorBrush RuleHairline = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x24)));
    private static readonly SolidColorBrush BorderOuter = Freeze(new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x30)));
    private static readonly SolidColorBrush BorderInteractive = Freeze(new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x40)));

    private static readonly SolidColorBrush TextPrimary = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly SolidColorBrush TextMuted = Freeze(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xAE)));
    private static readonly SolidColorBrush TextFaint = Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x7C)));

    private static readonly SolidColorBrush AccentViolet = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x7B, 0xFF)));
    private static readonly SolidColorBrush AccentGreen = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)));
    private static readonly SolidColorBrush DangerCoral = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x56, 0x6F)));
    private static readonly SolidColorBrush DangerSoft = Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x1A, 0x20)));

    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    // UI элементы навигации
    private readonly (Border Btn, TextBlock Label)[] _navRows = new (Border, TextBlock)[6];

    // Заголовок текущего таба
    private readonly TextBlock _topTabTitle;
    private readonly TextBlock _topTabDesc;

    // Панели контента табов
    private readonly FrameworkElement[] _tabPanels = new FrameworkElement[6];

    // Динамические элементы таба Уведомлений
    private readonly Border _notifySwitchTrack;
    private readonly Border _notifySwitchKnob;
    private readonly Border _notifySoundSwitchTrack;
    private readonly Border _notifySoundSwitchKnob;
    private readonly StackPanel _soundListPanel;

    // Чёлка и Остров
    private readonly Border _coveredSwitchTrack;
    private readonly Border _coveredSwitchKnob;
    private readonly Border _aeroSwitchTrack;
    private readonly Border _aeroSwitchKnob;
    private readonly TextBlock _aeroRoamBtnLabel;
    private readonly Slider _delaySlider;
    private readonly TextBlock _delayValText;

    // Основные
    private readonly Border _autoSwitchTrack;
    private readonly Border _autoSwitchKnob;
    private readonly (Border Pill, TextBlock Text)[] _langPills = new (Border, TextBlock)[3];

    // Оформление
    private readonly WrapPanel _accentColorsPanel;
    private readonly Border _glassSwitchTrack;
    private readonly Border _glassSwitchKnob;
    private readonly TextBlock _bgFileNameText;
    private readonly Slider _bgDimSlider;
    private readonly TextBlock _bgDimValText;
    private readonly Slider _bgBlurSlider;
    private readonly TextBlock _bgBlurValText;

    // ShiftConvert
    private readonly Border _convShiftSwitchTrack;
    private readonly Border _convShiftSwitchKnob;
    private readonly Border _convClipSwitchTrack;
    private readonly Border _convClipSwitchKnob;
    private readonly Border _convCtxSwitchTrack;
    private readonly Border _convCtxSwitchKnob;
    private readonly Border _convRevealSwitchTrack;
    private readonly Border _convRevealSwitchKnob;

    public SettingsWindow(IslandView island)
    {
        _island = island;

        Title = Loc.T("Параметры QuickNotch", "QuickNotch Settings", "QuickNotch Ayarları");
        Width = 870;
        Height = 590;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.ico"));
        }
        catch { }

        // Внешняя рама с закруглёнными углами
        var outerFrame = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = BgGround,
            BorderBrush = BorderOuter,
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };

        var rootGrid = new Grid();
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ==================== ЛЕВЫЙ САЙДБАР ====================
        var sidebarGrid = new Grid { Background = BgSidebar, Margin = new Thickness(14, 16, 14, 14) };
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Логотип
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Меню
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Статус

        // Логотип QuickNotch в шапке сайдбара
        var logoRow = new Grid { Margin = new Thickness(4, 0, 0, 18), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        logoRow.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        logoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        logoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logoBadge = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(9),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "🏝️",
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(logoBadge, 0);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = "QuickNotch",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });

        var verBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = BgRaised,
            BorderBrush = BorderOuter,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "v2.1.2",
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = AccentViolet
            }
        };
        titleRow.Children.Add(verBadge);
        titleStack.Children.Add(titleRow);

        titleStack.Children.Add(new TextBlock
        {
            Text = Loc.T("Dynamic Island для Windows", "Dynamic Island for Windows", "Windows için Dynamic Island"),
            FontSize = 10.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(titleStack, 1);

        logoRow.Children.Add(logoBadge);
        logoRow.Children.Add(titleStack);
        Grid.SetRow(logoRow, 0);
        sidebarGrid.Children.Add(logoRow);

        // Список вкладок
        var navStack = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        _navRows[0] = CreateNavRow("\uE7E7", Loc.T("Уведомления и Звук", "Notifications & Sound", "Bildirimler ve Ses"), 0);
        _navRows[1] = CreateNavRow("\uE7EE", Loc.T("Чёлка и Остров", "Notch & Island", "Çentik ve Ada"), 1);
        _navRows[2] = CreateNavRow("\uE713", Loc.T("Основные", "General", "Genel"), 2);
        _navRows[3] = CreateNavRow("\uE790", Loc.T("Оформление", "Appearance", "Görünüm"), 3);
        _navRows[4] = CreateNavRow("\uE8B7", Loc.T("ShiftConvert", "ShiftConvert", "ShiftConvert"), 4);
        _navRows[5] = CreateNavRow("\uE946", Loc.T("О программе", "About", "Hakkında"), 5);

        foreach (var (btn, _) in _navRows)
        {
            navStack.Children.Add(btn);
        }
        Grid.SetRow(navStack, 1);
        sidebarGrid.Children.Add(navStack);

        // Нижний блок: статус и кнопка перезапуска
        var bottomSidebar = new StackPanel();

        var statusPill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = BgSurface,
            BorderBrush = RuleHairline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Ellipse
        {
            Width = 6.5,
            Height = 6.5,
            Fill = AccentGreen,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = "0% CPU · Direct2D",
            FontSize = 11,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusRow.Children.Add(dot);
        statusRow.Children.Add(statusText);
        statusPill.Child = statusRow;
        bottomSidebar.Children.Add(statusPill);

        var restartBtn = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(10),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 10, 0),
            Cursor = Cursors.Hand
        };
        var restartRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var restartIcon = new TextBlock
        {
            Text = "\uE777",
            FontFamily = IconFont,
            FontSize = 11,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var restartLabel = new TextBlock
        {
            Text = Loc.T("Перезапустить", "Restart", "Yeniden Başlat"),
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        restartRow.Children.Add(restartIcon);
        restartRow.Children.Add(restartLabel);
        restartBtn.Child = restartRow;
        restartBtn.MouseEnter += (_, _) =>
        {
            restartBtn.Background = BgRaisedHover;
            restartLabel.Foreground = TextPrimary;
            restartIcon.Foreground = TextPrimary;
        };
        restartBtn.MouseLeave += (_, _) =>
        {
            restartBtn.Background = BgRaised;
            restartLabel.Foreground = TextMuted;
            restartIcon.Foreground = TextMuted;
        };
        restartBtn.MouseLeftButtonUp += (_, _) => RestartApplication();
        bottomSidebar.Children.Add(restartBtn);

        Grid.SetRow(bottomSidebar, 2);
        sidebarGrid.Children.Add(bottomSidebar);

        Grid.SetColumn(sidebarGrid, 0);
        rootGrid.Children.Add(sidebarGrid);

        // Вертикальный 1px разделитель
        var vDivider = new Border { Background = RuleHairline };
        Grid.SetColumn(vDivider, 1);
        rootGrid.Children.Add(vDivider);

        // ==================== ПРАВАЯ ОБЛАСТЬ (КОНТЕНТ) ====================
        var contentGrid = new Grid { Background = BgGround };
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) }); // Топ-бар
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) }); // 1px разделитель
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Контентная панель

        // Топ-бар: Заголовок таба + Кнопки Свернуть/Закрыть
        var topBar = new Grid { Margin = new Thickness(22, 0, 16, 0), Background = Brushes.Transparent };
        topBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var topTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _topTabTitle = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        };
        _topTabDesc = new TextBlock
        {
            FontSize = 11.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 2, 0, 0)
        };
        topTextStack.Children.Add(_topTabTitle);
        topTextStack.Children.Add(_topTabDesc);
        Grid.SetColumn(topTextStack, 0);
        topBar.Children.Add(topTextStack);

        var topButtonsRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var minBtn = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 4, 0),
            Child = new TextBlock
            {
                Text = "\uE921",
                FontFamily = IconFont,
                FontSize = 10,
                Foreground = TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        minBtn.MouseEnter += (_, _) => minBtn.Background = BgSurfaceHover;
        minBtn.MouseLeave += (_, _) => minBtn.Background = Brushes.Transparent;
        minBtn.MouseLeftButtonUp += (_, _) => WindowState = WindowState.Minimized;
        topButtonsRow.Children.Add(minBtn);

        var closeBtn = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\uE8BB",
                FontFamily = IconFont,
                FontSize = 10,
                Foreground = TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        closeBtn.MouseEnter += (_, _) =>
        {
            closeBtn.Background = DangerSoft;
            ((TextBlock)closeBtn.Child).Foreground = DangerCoral;
        };
        closeBtn.MouseLeave += (_, _) =>
        {
            closeBtn.Background = Brushes.Transparent;
            ((TextBlock)closeBtn.Child).Foreground = TextMuted;
        };
        closeBtn.MouseLeftButtonUp += (_, _) => Close();
        topButtonsRow.Children.Add(closeBtn);

        Grid.SetColumn(topButtonsRow, 1);
        topBar.Children.Add(topButtonsRow);
        Grid.SetRow(topBar, 0);
        contentGrid.Children.Add(topBar);

        var hDivider = new Border { Background = RuleHairline };
        Grid.SetRow(hDivider, 1);
        contentGrid.Children.Add(hDivider);

        // Хост экранов
        var contentHost = new Grid { Margin = new Thickness(22, 16, 22, 16) };

        // -------------------- ВКЛАДКА 0: УВЕДОМЛЕНИЯ И ЗВУК --------------------
        var tab0Panel = new StackPanel();

        // Карточка 1: Переключатель всплывающих тостов
        var cardNotify = CreateCard();
        var notifyRow = CreateSettingRow(
            Loc.T("Всплывающие уведомления в чёлке", "Island Notification Toasts", "Çentik Bildirimleri"),
            Loc.T("Отображение системных событий (USB, батарея, приложения) в Dynamic Island",
                  "Show system alerts (USB, battery, apps) directly in Dynamic Island",
                  "Sistem uyarılarını (USB, pil, uygulamalar) Dynamic Island içinde göster"),
            out _notifySwitchTrack, out _notifySwitchKnob,
            () =>
            {
                _island.S.NotifyEnabled = !_island.S.NotifyEnabled;
                NotificationService.IsEnabled = _island.S.NotifyEnabled;
                if (_island.S.NotifyEnabled) NotificationService.InitializeAsync();
                _island.SaveState();
                UpdateSwitchVisual(_notifySwitchTrack, _notifySwitchKnob, _island.S.NotifyEnabled);
            });
        cardNotify.Child = notifyRow;
        tab0Panel.Children.Add(cardNotify);

        // Карточка 2: Звук уведомления
        var cardSound = CreateCard();
        var soundInner = new StackPanel();

        var soundToggleRow = CreateSettingRow(
            Loc.T("Звуковой сигнал при уведомлении", "Notification Sound Alerts", "Bildirim Ses Uyarısı"),
            Loc.T("Воспроизводить мелодию при появлении всплывающего тоста в Dynamic Island",
                  "Play a gentle audio tone when notification expands the Dynamic Island",
                  "Dynamic Island bildirimi açıldığında ses çal"),
            out _notifySoundSwitchTrack, out _notifySoundSwitchKnob,
            () =>
            {
                _island.S.NotifySoundEnabled = !_island.S.NotifySoundEnabled;
                _island.SaveState();
                UpdateSwitchVisual(_notifySoundSwitchTrack, _notifySoundSwitchKnob, _island.S.NotifySoundEnabled);
            });
        soundInner.Children.Add(soundToggleRow);

        soundInner.Children.Add(new Border { Height = 1, Background = RuleHairline, Margin = new Thickness(0, 10, 0, 10) });

        var soundListHeader = new TextBlock
        {
            Text = Loc.T("Выберите звук оповещения:", "Select alert sound:", "Uyarı sesini seçin:"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            Margin = new Thickness(0, 0, 0, 8)
        };
        soundInner.Children.Add(soundListHeader);

        _soundListPanel = new StackPanel();
        soundInner.Children.Add(_soundListPanel);

        // Кнопка тестового уведомления
        soundInner.Children.Add(new Border { Height = 1, Background = RuleHairline, Margin = new Thickness(0, 12, 0, 12) });

        var testRow = new Grid();
        testRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        testRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var testDesc = new TextBlock
        {
            Text = Loc.T("Проверьте как выглядит тост и звучит выбранная мелодия в чёлке",
                         "Preview how toast appears and sound plays in the Dynamic Island",
                         "Bildirimin çentikte nasıl göründüğünü ve sesin nasıl çaldığını test edin"),
            FontSize = 11.5,
            Foreground = TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(testDesc, 0);
        testRow.Children.Add(testDesc);

        var testBtn = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = AccentViolet,
            Padding = new Thickness(16, 0, 16, 0),
            Cursor = Cursors.Hand
        };
        var testBtnLabel = new TextBlock
        {
            Text = Loc.T("🔔 Отправить тестовое уведомление", "🔔 Test Notification", "🔔 Bildirim Testi"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = BgGround,
            VerticalAlignment = VerticalAlignment.Center
        };
        testBtn.Child = testBtnLabel;
        testBtn.MouseLeftButtonUp += (_, _) =>
        {
            _island.ShowNotification(new NotificationItem
            {
                App = Loc.T("ТЕСТОВОЕ УВЕДОМЛЕНИЕ", "TEST NOTIFICATION", "TEST BİLDİRİMİ"),
                Title = Loc.T("Звук уведомления QuickNotch", "QuickNotch Notification Sound", "QuickNotch Bildirim Sesi"),
                Body = Loc.T("Мелодия успешно воспроизведена в Dynamic Island!",
                             "Tone successfully played in Dynamic Island!",
                             "Ses Dynamic Island içinde başarıyla çalındı!"),
                IconSymbol = "🔔",
                AccentColor = Theme.Accent
            }, 5.0);
        };
        Grid.SetColumn(testBtn, 1);
        testRow.Children.Add(testBtn);
        soundInner.Children.Add(testRow);

        cardSound.Child = soundInner;
        tab0Panel.Children.Add(cardSound);

        var tab0Scroll = WrapInScroll(tab0Panel);
        _tabPanels[0] = tab0Scroll;
        contentHost.Children.Add(tab0Scroll);

        // -------------------- ВКЛАДКА 1: ЧЁЛКА И ОСТРОВ --------------------
        var tab1Panel = new StackPanel();

        // Карточка 1: 10-секундный таймер ожидания плеера
        var cardTimer = CreateCard();
        var timerGrid = new Grid();
        timerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var timerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        timerText.Children.Add(new TextBlock
        {
            Text = Loc.T("Таймер ожидания медиаплеера при паузе", "Media Player Pause Grace Timer", "Medya Oynatıcı Duraklatma Zamanlayıcısı"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        timerText.Children.Add(new TextBlock
        {
            Text = Loc.T("Плеер и полоска прогресса не исчезают мгновенно при нажатии стоп, а ожидают ровно 10 секунд перед скрытием",
                         "Player and seekbar do not vanish immediately on pause; they wait 10 seconds before auto-collapsing",
                         "Oynatıcı duraklatıldığında hemen kaybolmaz, kapanmadan önce tam 10 saniye bekler"),
            FontSize = 11.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(timerText, 0);
        timerGrid.Children.Add(timerText);

        var timerBadge = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = Loc.T("⏱️ 10 секунд", "⏱️ 10 seconds", "⏱️ 10 saniye"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentViolet
            }
        };
        Grid.SetColumn(timerBadge, 1);
        timerGrid.Children.Add(timerBadge);
        cardTimer.Child = timerGrid;
        tab1Panel.Children.Add(cardTimer);

        // Карточка 2: Поведение поверх окон
        var cardCovered = CreateCard();
        var coveredRow = CreateSettingRow(
            Loc.T("Сворачивать при открытых окнах", "Collapse Under Windows", "Pencerelerin Altında Daralt"),
            Loc.T("Чёлка аккуратно сворачивается в компактный Home Bar (80×5 px), не перекрывая вкладки браузера",
                  "Notch cleanly collapses into a minimal Home Bar (80×5 px) without overlapping tabs",
                  "Çentik sekmeleri kapatmadan zarif bir Home Bar (80×5 px) haline küçülür"),
            out _coveredSwitchTrack, out _coveredSwitchKnob,
            () =>
            {
                _island.S.HideWhenCovered = !_island.S.HideWhenCovered;
                _island.SaveState();
                UpdateSwitchVisual(_coveredSwitchTrack, _coveredSwitchKnob, _island.S.HideWhenCovered);
            });
        cardCovered.Child = coveredRow;
        tab1Panel.Children.Add(cardCovered);

        // Карточка 3: Задержка автосворачивания
        var cardDelay = CreateCard();
        var delayInner = new StackPanel();

        var delayTop = new Grid();
        delayTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        delayTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var delayLabels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        delayLabels.Children.Add(new TextBlock
        {
            Text = Loc.T("Задержка закрытия острова после ухода курсора", "Auto-Collapse Delay", "Kapanma Gecikmesi"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        delayLabels.Children.Add(new TextBlock
        {
            Text = Loc.T("Время ожидания перед тем как раскрытый остров вернётся в режим чёлки",
                         "Time to wait before expanded island collapses back to notch",
                         "Açılan adanın tekrar çentik moduna dönmesi için bekleme süresi"),
            FontSize = 11.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(delayLabels, 0);
        delayTop.Children.Add(delayLabels);

        _delayValText = new TextBlock
        {
            Text = $"{_island.S.LeaveDelay:F1} {Loc.T("сек", "sec", "sn")}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentViolet,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_delayValText, 1);
        delayTop.Children.Add(_delayValText);
        delayInner.Children.Add(delayTop);

        _delaySlider = new Slider
        {
            Minimum = 0.5,
            Maximum = 3.0,
            Value = _island.S.LeaveDelay,
            SmallChange = 0.1,
            LargeChange = 0.5,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _delaySlider.ValueChanged += (_, e) =>
        {
            double val = Math.Round(e.NewValue, 1);
            _island.S.LeaveDelay = val;
            _island.SaveState();
            _delayValText.Text = $"{val:F1} {Loc.T("сек", "sec", "sn")}";
        };
        delayInner.Children.Add(_delaySlider);
        cardDelay.Child = delayInner;
        tab1Panel.Children.Add(cardDelay);

        // Карточка 4: Робот-компаньон Aero
        var cardAero = CreateCard();
        var aeroInner = new StackPanel();

        var aeroRow = CreateSettingRow(
            Loc.T("🤖 Робот-компаньон Aero", "🤖 Aero Companion Robot", "🤖 Aero Robot Arkadaşı"),
            Loc.T("Интерактивный питомец в чёлке: следит за курсором и реагирует на события системы",
                  "Interactive pet living in your notch: watches cursor and reacts to system events",
                  "Çentikte yaşayan interaktif dost: imleci takip eder ve sistem olaylarına tepki verir"),
            out _aeroSwitchTrack, out _aeroSwitchKnob,
            () =>
            {
                _island.ToggleOpt("robot_companion");
                UpdateSwitchVisual(_aeroSwitchTrack, _aeroSwitchKnob, _island.S.RobotCompanion);
            });
        aeroInner.Children.Add(aeroRow);

        aeroInner.Children.Add(new Border { Height = 1, Background = RuleHairline, Margin = new Thickness(0, 10, 0, 10) });

        var roamGrid = new Grid();
        roamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        roamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var roamDesc = new TextBlock
        {
            Text = Loc.T("Выпустить робота свободно летать по рабочему столу Windows",
                         "Release robot to freely roam your Windows desktop",
                         "Robotu Windows masaüstünde serbestçe uçmaya bırakın"),
            FontSize = 11.5,
            Foreground = TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(roamDesc, 0);
        roamGrid.Children.Add(roamDesc);

        var roamBtn = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 0, 14, 0),
            Cursor = Cursors.Hand
        };
        _aeroRoamBtnLabel = new TextBlock
        {
            Text = _island.S.RobotRoaming
                ? Loc.T("🏠 Вернуть в чёлку", "🏠 Return to Notch", "🏠 Çentiğe Dön")
                : Loc.T("🚀 Выпустить на рабочий стол", "🚀 Roam Desktop", "🚀 Masaüstüne Bırak"),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };
        roamBtn.Child = _aeroRoamBtnLabel;
        roamBtn.MouseEnter += (_, _) => roamBtn.Background = BgRaisedHover;
        roamBtn.MouseLeave += (_, _) => roamBtn.Background = BgRaised;
        roamBtn.MouseLeftButtonUp += (_, _) =>
        {
            if (_island.S.RobotRoaming) _island.DockAero();
            else _island.LaunchAeroToDesktop();
            _aeroRoamBtnLabel.Text = _island.S.RobotRoaming
                ? Loc.T("🏠 Вернуть в чёлку", "🏠 Return to Notch", "🏠 Çentiğe Dön")
                : Loc.T("🚀 Выпустить на рабочий стол", "🚀 Roam Desktop", "🚀 Masaüstüne Bırak");
        };
        Grid.SetColumn(roamBtn, 1);
        roamGrid.Children.Add(roamBtn);
        aeroInner.Children.Add(roamGrid);

        cardAero.Child = aeroInner;
        tab1Panel.Children.Add(cardAero);

        var tab1Scroll = WrapInScroll(tab1Panel);
        _tabPanels[1] = tab1Scroll;
        contentHost.Children.Add(tab1Scroll);

        // -------------------- ВКЛАДКА 2: ОСНОВНЫЕ --------------------
        var tab2Panel = new StackPanel();

        // Карточка 1: Автозагрузка
        var cardAuto = CreateCard();
        var autoRow = CreateSettingRow(
            Loc.T("Запуск вместе с Windows", "Launch on Windows Startup", "Windows ile Başlat"),
            Loc.T("QuickNotch автоматически запускается в фоновом режиме при включении компьютера",
                  "QuickNotch launches silently in background when your PC boots",
                  "QuickNotch bilgisayar açıldığında arka planda otomatik olarak başlar"),
            out _autoSwitchTrack, out _autoSwitchKnob,
            () =>
            {
                _island.ToggleAutostart();
                UpdateSwitchVisual(_autoSwitchTrack, _autoSwitchKnob, _island.Autostart);
            });
        cardAuto.Child = autoRow;
        tab2Panel.Children.Add(cardAuto);

        // Карточка 2: Язык интерфейса
        var cardLang = CreateCard();
        var langInner = new StackPanel();

        var langHeader = new Grid();
        langHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        langHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var langTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        langTexts.Children.Add(new TextBlock
        {
            Text = Loc.T("Язык интерфейса", "Interface Language", "Arayüz Dili"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        langTexts.Children.Add(new TextBlock
        {
            Text = Loc.T("Переключение языка надписей во всем приложении",
                         "Switch language for all labels across the app",
                         "Tüm uygulamada etiket dilini değiştirin"),
            FontSize = 11.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(langTexts, 0);
        langHeader.Children.Add(langTexts);

        var langRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _langPills[0] = CreatePillButton("🇷🇺 Русский", () => SetLang("ru"));
        _langPills[1] = CreatePillButton("🇬🇧 English", () => SetLang("en"));
        _langPills[2] = CreatePillButton("🇹🇷 Türkçe", () => SetLang("tr"));

        foreach (var (pill, _) in _langPills)
        {
            langRow.Children.Add(pill);
        }
        Grid.SetColumn(langRow, 1);
        langHeader.Children.Add(langRow);

        langInner.Children.Add(langHeader);
        cardLang.Child = langInner;
        tab2Panel.Children.Add(cardLang);

        // Карточка 3: Горячая клавиша
        var cardHotkey = CreateCard();
        var hotkeyGrid = new Grid();
        hotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var hotkeyTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        hotkeyTexts.Children.Add(new TextBlock
        {
            Text = Loc.T("Глобальная горячая клавиша", "Global Activation Hotkey", "Global Kısayol"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        hotkeyTexts.Children.Add(new TextBlock
        {
            Text = Loc.T("Мгновенный вызов и скрытие острова из любого полноэкранного приложения",
                         "Toggle island instantly from any full-screen game or desktop app",
                         "Herhangi bir tam ekran uygulama veya oyundan adayı anında açın"),
            FontSize = 11.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(hotkeyTexts, 0);
        hotkeyGrid.Children.Add(hotkeyTexts);

        var keyPill = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "Ctrl + Alt + N",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentViolet
            }
        };
        Grid.SetColumn(keyPill, 1);
        hotkeyGrid.Children.Add(keyPill);

        cardHotkey.Child = hotkeyGrid;
        tab2Panel.Children.Add(cardHotkey);

        var tab2Scroll = WrapInScroll(tab2Panel);
        _tabPanels[2] = tab2Scroll;
        contentHost.Children.Add(tab2Scroll);

        // -------------------- ВКЛАДКА 3: ОФОРМЛЕНИЕ --------------------
        var tab3Panel = new StackPanel();

        // Карточка 1: Акцентный цвет
        var cardAccent = CreateCard();
        var accentInner = new StackPanel();
        accentInner.Children.Add(new TextBlock
        {
            Text = Loc.T("Акцентный цвет оформления", "Accent Theme Color", "Vurgu Rengi"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        accentInner.Children.Add(new TextBlock
        {
            Text = Loc.T("Цвет индикаторов, прогресс-бара, эквалайзера и активных кнопок",
                         "Color of indicators, progress seekbars, dancing visualizer and active elements",
                         "Göstergelerin, ilerleme çubuğunun ve aktif öğelerin rengi"),
            FontSize = 11.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 3, 0, 10)
        });

        _accentColorsPanel = new WrapPanel { Margin = new Thickness(-4, 0, 0, 0) };
        accentInner.Children.Add(_accentColorsPanel);
        cardAccent.Child = accentInner;
        tab3Panel.Children.Add(cardAccent);

        // Карточка 2: Эффект стекла Liquid Glass
        var cardGlass = CreateCard();
        var glassRow = CreateSettingRow(
            Loc.T("Матовое стекло Liquid Glass (93% плотность)", "Liquid Glass Frosted Surface (93% Obsidian)", "Liquid Glass Mat Cam"),
            Loc.T("Глубокое обсидиановое стекло с тонкими бликами и мягким преломлением фона",
                  "Deep obsidian glass with subtle light sheens and soft background refraction",
                  "Hafif ışık parıltıları ve yumuşak kırılma ile derin obsidiyen camı"),
            out _glassSwitchTrack, out _glassSwitchKnob,
            () =>
            {
                _island.ToggleGlass();
                UpdateSwitchVisual(_glassSwitchTrack, _glassSwitchKnob, _island.S.GlassEnabled);
            });
        cardGlass.Child = glassRow;
        tab3Panel.Children.Add(cardGlass);

        // Карточка 3: Обои и фон острова
        var cardBg = CreateCard();
        var bgInner = new StackPanel();

        var bgTopRow = new Grid();
        bgTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bgTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bgLabels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bgLabels.Children.Add(new TextBlock
        {
            Text = Loc.T("Фоновое изображение подложки", "Custom Island Background Wallpaper", "Özel Ada Arka Planı"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        _bgFileNameText = new TextBlock
        {
            Text = string.IsNullOrEmpty(_island.S.BgImage) ? Loc.T("Фон по умолчанию", "Default Background", "Varsayılan Arka Plan") : System.IO.Path.GetFileName(_island.S.BgImage),
            FontSize = 11.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 3, 0, 0)
        };
        bgLabels.Children.Add(_bgFileNameText);
        Grid.SetColumn(bgLabels, 0);
        bgTopRow.Children.Add(bgLabels);

        var bgButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var clearBgBtn = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = Loc.T("Сбросить", "Reset", "Sıfırla"),
                FontSize = 11.5,
                Foreground = TextMuted,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        clearBgBtn.MouseEnter += (_, _) => clearBgBtn.Background = DangerSoft;
        clearBgBtn.MouseLeave += (_, _) => clearBgBtn.Background = BgRaised;
        clearBgBtn.MouseLeftButtonUp += (_, _) =>
        {
            _island.SetBackgroundImage("");
            _bgFileNameText.Text = Loc.T("Фон по умолчанию", "Default Background", "Varsayılan Arka Plan");
        };
        bgButtons.Children.Add(clearBgBtn);

        var pickBgBtn = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = TextPrimary,
            Padding = new Thickness(14, 0, 14, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = Loc.T("📁 Выбрать фото...", "📁 Choose image...", "📁 Görsel seç..."),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BgGround,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        pickBgBtn.MouseLeftButtonUp += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Выберите фоновое изображение", "Select background image", "Arka plan görseli seçin"),
                Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickNotch");
                    Directory.CreateDirectory(dir);
                    string dest = System.IO.Path.Combine(dir, "background" + System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant());
                    File.Copy(dlg.FileName, dest, true);
                    _island.SetBackgroundImage(dest);
                    _bgFileNameText.Text = System.IO.Path.GetFileName(dest);
                }
                catch
                {
                    _island.SetBackgroundImage(dlg.FileName);
                    _bgFileNameText.Text = System.IO.Path.GetFileName(dlg.FileName);
                }
            }
        };
        bgButtons.Children.Add(pickBgBtn);

        Grid.SetColumn(bgButtons, 1);
        bgTopRow.Children.Add(bgButtons);
        bgInner.Children.Add(bgTopRow);

        // Слайдеры затемнения и блюра
        bgInner.Children.Add(new Border { Height = 1, Background = RuleHairline, Margin = new Thickness(0, 12, 0, 12) });

        var dimRow = new Grid();
        dimRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dimRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dimRow.Children.Add(new TextBlock
        {
            Text = Loc.T("Затемнение фонового рисунка (Dim)", "Background Wallpaper Dimming", "Arka Plan Karartma"),
            FontSize = 12.5,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        _bgDimValText = new TextBlock
        {
            Text = $"{(int)Math.Round(_island.S.BgDim * 100)}%",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentViolet,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_bgDimValText, 1);
        dimRow.Children.Add(_bgDimValText);
        bgInner.Children.Add(dimRow);

        _bgDimSlider = new Slider
        {
            Minimum = 0.0,
            Maximum = 0.9,
            Value = _island.S.BgDim,
            Margin = new Thickness(0, 6, 0, 12)
        };
        _bgDimSlider.ValueChanged += (_, e) =>
        {
            _island.SetBackgroundLook(e.NewValue, _bgBlurSlider?.Value ?? _island.S.BgBlur);
            _bgDimValText.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
        };
        bgInner.Children.Add(_bgDimSlider);

        var blurRow = new Grid();
        blurRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        blurRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        blurRow.Children.Add(new TextBlock
        {
            Text = Loc.T("Размытие обоев (Gaussian Blur)", "Wallpaper Blur (Gaussian Blur)", "Arka Plan Bulanıklığı"),
            FontSize = 12.5,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });
        _bgBlurValText = new TextBlock
        {
            Text = $"{(int)Math.Round(_island.S.BgBlur)} px",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AccentViolet,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_bgBlurValText, 1);
        blurRow.Children.Add(_bgBlurValText);
        bgInner.Children.Add(blurRow);

        _bgBlurSlider = new Slider
        {
            Minimum = 0.0,
            Maximum = 20.0,
            Value = _island.S.BgBlur,
            Margin = new Thickness(0, 6, 0, 0)
        };
        _bgBlurSlider.ValueChanged += (_, e) =>
        {
            _island.SetBackgroundLook(_bgDimSlider?.Value ?? _island.S.BgDim, e.NewValue);
            _bgBlurValText.Text = $"{(int)Math.Round(e.NewValue)} px";
        };
        bgInner.Children.Add(_bgBlurSlider);

        cardBg.Child = bgInner;
        tab3Panel.Children.Add(cardBg);

        var tab3Scroll = WrapInScroll(tab3Panel);
        _tabPanels[3] = tab3Scroll;
        contentHost.Children.Add(tab3Scroll);

        // -------------------- ВКЛАДКА 4: SHIFTCONVERT --------------------
        var tab4Panel = new StackPanel();

        var cardConvShift = CreateCard();
        var convShiftRow = CreateSettingRow(
            Loc.T("Колесо форматов при зажатой клавише Shift", "Hold-Shift Conversion Radial Wheel", "Shift Tuşu Dönüştürme Çarkı"),
            Loc.T("При перетаскивании файла с зажатой клавишей Shift прямо у курсора открывается круговое меню форматов",
                  "Dragging any file while holding Shift pops up an instant radial format wheel right at cursor",
                  "Herhangi bir dosyayı Shift tuşuyla sürüklerken imleçte dairesel format çarkı açılır"),
            out _convShiftSwitchTrack, out _convShiftSwitchKnob,
            () =>
            {
                _island.S.ShiftConvertEnabled = !_island.S.ShiftConvertEnabled;
                _island.SaveState();
                UpdateSwitchVisual(_convShiftSwitchTrack, _convShiftSwitchKnob, _island.S.ShiftConvertEnabled);
            });
        cardConvShift.Child = convShiftRow;
        tab4Panel.Children.Add(cardConvShift);

        var cardConvClip = CreateCard();
        var convClipRow = CreateSettingRow(
            Loc.T("Автокопирование готового файла в буфер (Ctrl+V)", "Auto-Copy Output to Clipboard", "Panoya Otomatik Kopyala"),
            Loc.T("После завершения конвертации готовый файл автоматически копируется в буфер для мгновенной вставки",
                  "Converted file is immediately ready in clipboard so you can paste (Ctrl+V) anywhere",
                  "Dönüştürülen dosya hemen yapıştırmak (Ctrl+V) üzere panoya kopyalanır"),
            out _convClipSwitchTrack, out _convClipSwitchKnob,
            () =>
            {
                _island.S.ShiftConvertAutoCopy = !_island.S.ShiftConvertAutoCopy;
                _island.SaveState();
                UpdateSwitchVisual(_convClipSwitchTrack, _convClipSwitchKnob, _island.S.ShiftConvertAutoCopy);
            });
        cardConvClip.Child = convClipRow;
        tab4Panel.Children.Add(cardConvClip);

        var cardConvCtx = CreateCard();
        var convCtxRow = CreateSettingRow(
            Loc.T("Меню правой кнопки мыши (ПКМ в Проводнике)", "Windows Explorer Context Menu", "Windows Gezgini Sağ Tık Menüsü"),
            Loc.T("Добавить пункт «⚡ Конвертировать» в контекстное меню файлов Проводника Windows",
                  "Adds quick '⚡ Convert' command directly into Windows Explorer right-click menu",
                  "Windows Gezgini sağ tık menüsüne '⚡ Dönüştür' seçeneğini ekler"),
            out _convCtxSwitchTrack, out _convCtxSwitchKnob,
            () =>
            {
                bool enable = !_island.S.ShiftConvertContextMenu;
                _island.S.ShiftConvertContextMenu = enable;
                _island.SaveState();
                ShiftConvertService.SetContextMenu(enable);
                UpdateSwitchVisual(_convCtxSwitchTrack, _convCtxSwitchKnob, _island.S.ShiftConvertContextMenu);
            });
        cardConvCtx.Child = convCtxRow;
        tab4Panel.Children.Add(cardConvCtx);

        var cardConvReveal = CreateCard();
        var convRevealRow = CreateSettingRow(
            Loc.T("Открывать папку после завершения", "Reveal Converted File in Explorer", "Klasörde Göster"),
            Loc.T("Автоматически открывать Проводник и подсвечивать готовый сконвертированный файл",
                  "Opens the destination folder and highlights the resulting file automatically",
                  "Dönüştürme bittiğinde hedef klasörü otomatik açıp dosyayı seçili gösterir"),
            out _convRevealSwitchTrack, out _convRevealSwitchKnob,
            () =>
            {
                _island.S.ShiftConvertAutoReveal = !_island.S.ShiftConvertAutoReveal;
                _island.SaveState();
                UpdateSwitchVisual(_convRevealSwitchTrack, _convRevealSwitchKnob, _island.S.ShiftConvertAutoReveal);
            });
        cardConvReveal.Child = convRevealRow;
        tab4Panel.Children.Add(cardConvReveal);

        var tab4Scroll = WrapInScroll(tab4Panel);
        _tabPanels[4] = tab4Scroll;
        contentHost.Children.Add(tab4Scroll);

        // -------------------- ВКЛАДКА 5: О ПРОГРАММЕ --------------------
        var tab5Panel = new StackPanel();

        var cardAbout = CreateCard();
        var aboutInner = new StackPanel();

        var aboutHeader = new Grid { Margin = new Thickness(0, 4, 0, 16) };
        aboutHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        aboutHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var aboutIconBorder = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(16),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 16, 0),
            Child = new TextBlock
            {
                Text = "🏝️",
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(aboutIconBorder, 0);
        aboutHeader.Children.Add(aboutIconBorder);

        var aboutTitles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        aboutTitles.Children.Add(new TextBlock
        {
            Text = "QuickNotch v2.1.2",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = TextPrimary
        });
        aboutTitles.Children.Add(new TextBlock
        {
            Text = "macOS Dynamic Island & Smart Notch Engine for Windows 10 / 11",
            FontSize = 12.5,
            Foreground = TextMuted,
            Margin = new Thickness(0, 2, 0, 0)
        });
        aboutTitles.Children.Add(new TextBlock
        {
            Text = Loc.T("Разработчик: Eldar Semenov", "Developer: Eldar Semenov", "Geliştirici: Eldar Semenov"),
            FontSize = 12,
            Foreground = TextFaint,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetColumn(aboutTitles, 1);
        aboutHeader.Children.Add(aboutTitles);
        aboutInner.Children.Add(aboutHeader);

        aboutInner.Children.Add(new Border { Height = 1, Background = RuleHairline, Margin = new Thickness(0, 0, 0, 14) });

        // Метрики
        var metricsGrid = new Grid();
        metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        metricsGrid.Children.Add(CreateStatPill("0.0%", Loc.T("Нагрузка на CPU", "Idle CPU Usage", "CPU Kullanımı"), 0));
        metricsGrid.Children.Add(CreateStatPill("60 FPS", Loc.T("Плавные анимации", "Fluid Animations", "Akıcı Animasyonlar"), 1));
        metricsGrid.Children.Add(CreateStatPill("Single EXE", Loc.T("Без зависимостей", "Self-Contained", "Bağımsız"), 2));
        aboutInner.Children.Add(metricsGrid);

        aboutInner.Children.Add(new Border { Height = 1, Background = RuleHairline, Margin = new Thickness(0, 14, 0, 14) });

        // Кнопка репозитория на GitHub
        var repoRow = new Grid();
        repoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        repoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        repoRow.Children.Add(new TextBlock
        {
            Text = "github.com/eLdSe/quick_notch",
            FontSize = 12.5,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        });

        var ghBtn = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = TextPrimary,
            Padding = new Thickness(16, 0, 16, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = Loc.T("🔗 Открыть на GitHub", "🔗 Open on GitHub", "🔗 GitHub'da Aç"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = BgGround,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        ghBtn.MouseLeftButtonUp += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/eLdSe/quick_notch") { UseShellExecute = true }); } catch { }
        };
        Grid.SetColumn(ghBtn, 1);
        repoRow.Children.Add(ghBtn);
        aboutInner.Children.Add(repoRow);

        cardAbout.Child = aboutInner;
        tab5Panel.Children.Add(cardAbout);

        var tab5Scroll = WrapInScroll(tab5Panel);
        _tabPanels[5] = tab5Scroll;
        contentHost.Children.Add(tab5Scroll);

        Grid.SetRow(contentHost, 2);
        contentGrid.Children.Add(contentHost);

        Grid.SetColumn(contentGrid, 2);
        rootGrid.Children.Add(contentGrid);

        outerFrame.Child = rootGrid;
        Content = outerFrame;

        // Инициализация данных и отображения
        BuildSoundsList();
        BuildAccentPalette();
        RefreshUIState();
        SwitchTab(0);
    }

    private void SwitchTab(int tabIndex)
    {
        _activeTab = tabIndex;
        for (int i = 0; i < 6; i++)
        {
            bool isActive = i == tabIndex;
            _tabPanels[i].Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            _navRows[i].Btn.Background = isActive ? BgNavActive : Brushes.Transparent;
            _navRows[i].Label.Foreground = isActive ? TextPrimary : TextMuted;
            _navRows[i].Label.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }

        switch (tabIndex)
        {
            case 0:
                _topTabTitle.Text = Loc.T("Уведомления и Звук", "Notifications & Sound", "Bildirimler ve Ses");
                _topTabDesc.Text = Loc.T("Настройка всплывающих тостов Dynamic Island и выбор мелодии оповещения",
                                         "Configure Dynamic Island alert toasts and choose your favorite notification chime",
                                         "Dynamic Island uyarı bildirimlerini yapılandırın ve uyarı melodisini seçin");
                break;
            case 1:
                _topTabTitle.Text = Loc.T("Чёлка и Остров", "Notch & Island", "Çentik ve Ada");
                _topTabDesc.Text = Loc.T("Параметры сворачивания, таймер паузы плеера 10 секунд и робот-компаньон",
                                         "Collapse behavior, 10s player pause timeout and Aero robot companion",
                                         "Daralma davranışı, 10 saniye duraklatma süresi ve Aero robot dostu");
                break;
            case 2:
                _topTabTitle.Text = Loc.T("Основные параметры", "General Settings", "Genel Ayarlar");
                _topTabDesc.Text = Loc.T("Автозапуск при старте Windows, язык интерфейса и глобальная горячая клавиша",
                                         "Windows auto-start, interface language and global hotkey toggle",
                                         "Windows başlangıcında çalıştırma, arayüz dili ve global kısayol");
                break;
            case 3:
                _topTabTitle.Text = Loc.T("Оформление и Стекло", "Appearance & Glass", "Görünüm ve Cam");
                _topTabDesc.Text = Loc.T("Плотность Liquid Glass 93%, палитра акцентов и обои подложки",
                                         "Liquid Glass 93% obsidian frosted density, accent colors and wallpaper dimming",
                                         "Liquid Glass mat cam yoğunluğu, vurgu renkleri ve arka plan ayarları");
                break;
            case 4:
                _topTabTitle.Text = Loc.T("Конвертер ShiftConvert", "ShiftConvert Integration", "ShiftConvert Entegrasyonu");
                _topTabDesc.Text = Loc.T("Круговое колесо форматов при зажатой клавише Shift и контекстное меню Проводника",
                                         "Radial conversion wheel on Shift key and Windows Explorer integration",
                                         "Shift tuşunda dairesel format çarkı ve Windows Gezgini entegrasyonu");
                break;
            case 5:
                _topTabTitle.Text = Loc.T("О программе QuickNotch", "About QuickNotch", "QuickNotch Hakkında");
                _topTabDesc.Text = Loc.T("Информация о версии, аппаратном ускорении и разработчике",
                                         "Version info, hardware acceleration metrics and developer credits",
                                         "Sürüm bilgisi, donanım hızlandırma metrikleri ve geliştirici bilgileri");
                break;
        }
    }

    private void BuildSoundsList()
    {
        _soundListPanel.Children.Clear();

        foreach (var sound in NotificationSoundService.Sounds)
        {
            bool isSelected = string.Equals(_island.S.NotifySound, sound.Key, StringComparison.OrdinalIgnoreCase);

            var row = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = isSelected ? BgNavActive : BgSurface,
                BorderBrush = isSelected ? AccentViolet : RuleHairline,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Радио-метка
            var radioCircle = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                BorderBrush = isSelected ? AccentViolet : BorderInteractive,
                BorderThickness = new Thickness(isSelected ? 5 : 1.5),
                Background = isSelected ? TextPrimary : Brushes.Transparent,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(radioCircle, 0);
            grid.Children.Add(radioCircle);

            // Тексты
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = Loc.T(sound.NameRu, sound.NameEn, sound.NameRu),
                FontSize = 13,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground = isSelected ? TextPrimary : TextMuted
            });
            textStack.Children.Add(new TextBlock
            {
                Text = sound.Description,
                FontSize = 11,
                Foreground = TextFaint,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            // Кнопка предпрослушивания (если звук не "none")
            if (!string.Equals(sound.Key, "none", StringComparison.OrdinalIgnoreCase))
            {
                var playBtn = new Border
                {
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = BgRaised,
                    BorderBrush = BorderInteractive,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 0, 10, 0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = Loc.T("Прослушать этот звук прямо сейчас", "Listen to this tone now", "Bu sesi hemen dinleyin")
                };
                var playRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                playRow.Children.Add(new TextBlock
                {
                    Text = "\uE768",
                    FontFamily = IconFont,
                    FontSize = 9.5,
                    Foreground = AccentViolet,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                });
                playRow.Children.Add(new TextBlock
                {
                    Text = Loc.T("Прослушать", "Preview", "Dinle"),
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = TextPrimary,
                    VerticalAlignment = VerticalAlignment.Center
                });
                playBtn.Child = playRow;
                playBtn.MouseEnter += (_, _) => playBtn.Background = BgRaisedHover;
                playBtn.MouseLeave += (_, _) => playBtn.Background = BgRaised;
                playBtn.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    NotificationSoundService.Preview(sound.Key);
                };
                Grid.SetColumn(playBtn, 2);
                grid.Children.Add(playBtn);
            }

            row.Child = grid;
            row.MouseLeftButtonUp += (_, _) =>
            {
                _island.S.NotifySound = sound.Key;
                _island.SaveState();
                BuildSoundsList();
                if (!string.Equals(sound.Key, "none", StringComparison.OrdinalIgnoreCase))
                {
                    NotificationSoundService.Preview(sound.Key);
                }
            };
            _soundListPanel.Children.Add(row);
        }
    }

    private void BuildAccentPalette()
    {
        _accentColorsPanel.Children.Clear();

        for (int i = 0; i < Theme.Accents.Length; i++)
        {
            var (key, mainColorHex, name) = Theme.Accents[i];
            bool isCurrent = Theme.CurrentAccent == key;

            var border = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(5),
                Cursor = Cursors.Hand,
                ToolTip = name,
                BorderThickness = new Thickness(isCurrent ? 2.5 : 1),
                BorderBrush = isCurrent ? TextPrimary : BorderInteractive,
                Background = Brushes.Transparent
            };

            var circle = new Ellipse
            {
                Width = 22,
                Height = 22,
                Fill = new SolidColorBrush(Theme.C(mainColorHex))
            };
            border.Child = circle;

            border.MouseLeftButtonUp += (_, _) =>
            {
                _island.SetAccent(key);
                BuildAccentPalette();
            };

            _accentColorsPanel.Children.Add(border);
        }
    }

    private void RefreshUIState()
    {
        UpdateSwitchVisual(_notifySwitchTrack, _notifySwitchKnob, _island.S.NotifyEnabled);
        UpdateSwitchVisual(_notifySoundSwitchTrack, _notifySoundSwitchKnob, _island.S.NotifySoundEnabled);
        UpdateSwitchVisual(_coveredSwitchTrack, _coveredSwitchKnob, _island.S.HideWhenCovered);
        UpdateSwitchVisual(_aeroSwitchTrack, _aeroSwitchKnob, _island.S.RobotCompanion);
        UpdateSwitchVisual(_autoSwitchTrack, _autoSwitchKnob, _island.Autostart);
        UpdateSwitchVisual(_glassSwitchTrack, _glassSwitchKnob, _island.S.GlassEnabled);

        UpdateSwitchVisual(_convShiftSwitchTrack, _convShiftSwitchKnob, _island.S.ShiftConvertEnabled);
        UpdateSwitchVisual(_convClipSwitchTrack, _convClipSwitchKnob, _island.S.ShiftConvertAutoCopy);
        UpdateSwitchVisual(_convCtxSwitchTrack, _convCtxSwitchKnob, _island.S.ShiftConvertContextMenu);
        UpdateSwitchVisual(_convRevealSwitchTrack, _convRevealSwitchKnob, _island.S.ShiftConvertAutoReveal);

        UpdateLangPills();
    }

    private void SetLang(string lang)
    {
        _island.SetLanguage(lang);
        UpdateLangPills();
        SwitchTab(_activeTab);
        BuildSoundsList();
    }

    private void UpdateLangPills()
    {
        string cur = _island.S.Language;
        for (int i = 0; i < 3; i++)
        {
            bool isCurrent = (i == 0 && cur == "ru") || (i == 1 && cur == "en") || (i == 2 && cur == "tr");
            _langPills[i].Pill.Background = isCurrent ? AccentViolet : BgRaised;
            _langPills[i].Pill.BorderBrush = isCurrent ? AccentViolet : BorderInteractive;
            _langPills[i].Text.Foreground = isCurrent ? BgGround : TextPrimary;
            _langPills[i].Text.FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Medium;
        }
    }

    private (Border Pill, TextBlock Text) CreatePillButton(string text, Action onClick)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        };
        var pill = new Border
        {
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = tb
        };
        pill.MouseLeftButtonUp += (_, _) => onClick();
        return (pill, tb);
    }

    private static Border CreateCard()
    {
        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = BgSurface,
            BorderBrush = BorderOuter,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 14, 18, 14),
            Margin = new Thickness(0, 0, 0, 10)
        };
    }

    private static ScrollViewer WrapInScroll(UIElement content)
    {
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Visibility = Visibility.Collapsed
        };
    }

    private (Border Btn, TextBlock Label) CreateNavRow(string icon, string label, int tabIndex)
    {
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconTb = new TextBlock
        {
            Text = icon,
            FontFamily = IconFont,
            FontSize = 14,
            Foreground = AccentViolet,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(iconTb, 0);
        row.Children.Add(iconTb);

        var labelTb = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            Foreground = TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelTb, 1);
        row.Children.Add(labelTb);

        var btn = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            Child = row
        };

        btn.MouseEnter += (_, _) =>
        {
            if (_activeTab != tabIndex) btn.Background = BgSurfaceHover;
        };
        btn.MouseLeave += (_, _) =>
        {
            if (_activeTab != tabIndex) btn.Background = Brushes.Transparent;
        };
        btn.MouseLeftButtonUp += (_, _) => SwitchTab(tabIndex);

        return (btn, labelTb);
    }

    private static Grid CreateSettingRow(
        string title, string desc,
        out Border track, out Border knob,
        Action onToggle)
    {
        var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        textStack.Children.Add(new TextBlock
        {
            Text = desc,
            FontSize = 11.5,
            Foreground = TextFaint,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(textStack, 0);

        knob = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = BgGround,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        track = new Border
        {
            Width = 42,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = BgRaised,
            Padding = new Thickness(3, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = knob
        };
        Grid.SetColumn(track, 1);

        row.Children.Add(textStack);
        row.Children.Add(track);
        row.MouseLeftButtonUp += (_, _) => onToggle();

        return row;
    }

    private static void UpdateSwitchVisual(Border track, Border knob, bool isOn)
    {
        track.Background = isOn ? AccentViolet : BgRaised;
        knob.Background = isOn ? TextPrimary : TextMuted;
        knob.HorizontalAlignment = isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private static Border CreateStatPill(string val, string lbl, int col)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = BgRaised,
            BorderBrush = BorderInteractive,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 2 ? 0 : 4, 0)
        };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = val,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = AccentViolet,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = lbl,
            FontSize = 10.5,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });
        card.Child = stack;
        Grid.SetColumn(card, col);
        return card;
    }

    private static void RestartApplication()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            Application.Current.Shutdown();
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _instance = null;
        base.OnClosed(e);
    }
}

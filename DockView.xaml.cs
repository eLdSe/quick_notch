using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace QuickNotchCs;

public partial class DockView : UserControl
{
    private IslandView? _island;
    private static readonly SolidColorBrush AccentBrush = new((Color)ColorConverter.ConvertFromString("#0A84FF"));
    private static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;
    private static readonly SolidColorBrush TextWhiteBrush = Brushes.White;
    private static readonly SolidColorBrush TextMutedBrush = new((Color)ColorConverter.ConvertFromString("#8E8E93"));
    private static readonly SolidColorBrush AeroCyanBrush = new((Color)ColorConverter.ConvertFromString("#38BDF8"));
    private static readonly DropShadowEffect ActiveGlowEffect = new()
    {
        Color = (Color)ColorConverter.ConvertFromString("#0A84FF"),
        BlurRadius = 10,
        Opacity = 0.45,
        ShadowDepth = 1
    };

    static DockView()
    {
        AccentBrush.Freeze();
        TextMutedBrush.Freeze();
        AeroCyanBrush.Freeze();
    }

    public DockView()
    {
        InitializeComponent();
        RefreshLabels();
    }

    public void Attach(IslandView island)
    {
        _island = island;
        RefreshAll();
    }

    public void RefreshAll()
    {
        RefreshLabels();
        RefreshSelected(_island?.CurrentTool ?? "home");
    }

    public void RefreshLabels()
    {
        if (HomeText == null) return;
        HomeText.Text = Loc.T("Управление", "Control", "Kontrol");
        ClipboardText.Text = Loc.T("Буфер", "Clipboard", "Pano");
        AiText.Text = Loc.T("Аэро", "Aero", "Aero");
        LinksText.Text = Loc.T("Ссылки", "Links", "Bağlantılar");
        SettingsText.Text = Loc.T("Настройки", "Settings", "Ayarlar");
    }

    public void RefreshSelected(string currentTool)
    {
        if (HomeBorder == null) return;

        SetTabState(HomeBorder, HomeIcon, HomeText, currentTool == "home");
        SetTabState(ClipboardBorder, ClipboardIcon, ClipboardText, currentTool == "clipboard");
        SetTabState(AiBorder, AiIcon, AiText, currentTool == "ai");
        if (currentTool != "ai" && AiIcon != null)
        {
            AiIcon.Foreground = AeroCyanBrush;
        }
        SetTabState(LinksBorder, LinksIcon, LinksText, currentTool == "links");
        SetTabState(SettingsBorder, SettingsIcon, SettingsText, currentTool == "settings");

        bool isSubTool = currentTool is "sound" or "media" or "system" or "weather" or "calendar" or "pomodoro";
        if (BackButton != null)
        {
            BackButton.Visibility = isSubTool ? Visibility.Visible : Visibility.Collapsed;
        }
        if (SubToolBorder != null && SubToolText != null)
        {
            if (isSubTool)
            {
                var sec = Sections.ByKey(currentTool);
                SubToolText.Text = $"{sec.Icon} {sec.Title}";
                SubToolBorder.Visibility = Visibility.Visible;
            }
            else
            {
                SubToolBorder.Visibility = Visibility.Collapsed;
            }
        }
    }

    public void ApplyAccent()
    {
        ActiveGlowEffect.Color = Theme.Accent;
        if (SubToolBorder != null) SubToolBorder.Background = Theme.AccentBrush;
        RefreshSelected(_island?.CurrentTool ?? "home");
    }

    private void SetTabState(Border border, TextBlock icon, TextBlock text, bool selected)
    {
        if (selected)
        {
            border.Background = Theme.AccentBrush;
            icon.Foreground = TextWhiteBrush;
            text.Foreground = TextWhiteBrush;
        }
        else
        {
            border.Background = TransparentBrush;
            icon.Foreground = Theme.MutedBrush;
            text.Foreground = Theme.MutedBrush;
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

    private void OnBackClick(object sender, RoutedEventArgs e) => _island?.Select("home");
    private void OnHomeClick(object sender, RoutedEventArgs e) => _island?.Select("home");
    private void OnClipboardClick(object sender, RoutedEventArgs e) => _island?.Select("clipboard");
    private void OnAiClick(object sender, RoutedEventArgs e) => _island?.Select("ai");
    private void OnLinksClick(object sender, RoutedEventArgs e) => _island?.Select("links");
    private void OnSettingsClick(object sender, RoutedEventArgs e) => _island?.Select("settings");
}


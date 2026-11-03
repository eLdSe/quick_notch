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
    private static readonly Thickness SelectedPadding = new(13, 6, 15, 6);
    private static readonly Thickness UnselectedPadding = new(9, 6, 9, 6);

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
        HomeText.Text = Loc.T("Главная", "Home", "Ana Sayfa");
        if (AiText != null) AiText.Text = Loc.T("Аэро", "Aero", "Aero");
        if (LinksText != null) LinksText.Text = Loc.T("Ссылки", "Links", "Bağlantılar");
        if (ClipboardText != null) ClipboardText.Text = Loc.T("Буфер", "Clipboard", "Pano");
    }

    public void RefreshSelected(string currentTool)
    {
        if (HomeBorder == null) return;

        SetTabState(HomeBorder, HomeIcon, HomeText, currentTool == "home");
        SetTabState(AiBorder, AiIcon, AiText, currentTool == "ai");
        if (currentTool != "ai" && AiIcon != null)
        {
            AiIcon.Foreground = AeroCyanBrush;
        }
        SetTabState(LinksBorder, LinksIcon, LinksText, currentTool == "links");
        SetTabState(ClipboardBorder, ClipboardIcon, ClipboardText, currentTool == "clipboard");

        if (BackButton != null) BackButton.Visibility = Visibility.Collapsed;
        if (SubToolBorder != null) SubToolBorder.Visibility = Visibility.Collapsed;
    }

    public void ApplyAccent()
    {
        RefreshSelected(_island?.CurrentTool ?? "home");
    }

    private void SetTabState(Border border, TextBlock icon, TextBlock text, bool selected)
    {
        if (selected)
        {
            border.Background = Theme.AccentBrush;
            border.Padding = SelectedPadding;
            icon.Foreground = TextWhiteBrush;
            text.Foreground = TextWhiteBrush;
            text.Visibility = Visibility.Visible;
        }
        else
        {
            border.Background = TransparentBrush;
            border.Padding = UnselectedPadding;
            icon.Foreground = Theme.MutedBrush;
            text.Foreground = Theme.MutedBrush;
            text.Visibility = Visibility.Collapsed;
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
    private void OnAiClick(object sender, RoutedEventArgs e) => _island?.Select("ai");
    private void OnLinksClick(object sender, RoutedEventArgs e) => _island?.Select("links");
    private void OnClipboardClick(object sender, RoutedEventArgs e) => _island?.Select("clipboard");
}

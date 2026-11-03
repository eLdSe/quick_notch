using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class ToastView : UserControl
{
    private IslandView? _island;
    private NotificationItem? _currentNotif;

    public ToastView()
    {
        InitializeComponent();
    }

    public void Attach(IslandView island)
    {
        _island = island;
    }

    public void ShowNotification(NotificationItem notif)
    {
        _currentNotif = notif;
        if (TitleText == null || AppNameText == null) return;

        AppNameText.Text = string.IsNullOrEmpty(notif.App) ? Loc.T("УВЕДОМЛЕНИЕ", "NOTIFICATION", "BİLDİRİM") : notif.App.ToUpperInvariant();
        TitleText.Text = notif.Title;

        if (notif.AccentColor != null)
        {
            var brush = new SolidColorBrush(notif.AccentColor.Value);
            AppNameText.Foreground = brush;
        }
        else
        {
            AppNameText.Foreground = Brushes.White;
        }

        if (string.IsNullOrEmpty(notif.Body))
        {
            BodyText.Visibility = Visibility.Collapsed;
        }
        else
        {
            BodyText.Text = notif.Body;
            BodyText.Visibility = Visibility.Visible;
        }

        if (notif.UseCoverArt && _island?.CoverBrush is ImageBrush ib)
        {
            IconImage.Source = ib.ImageSource;
            IconSymbolText.Visibility = Visibility.Collapsed;
        }
        else
        {
            IconImage.Source = null;
            IconSymbolText.Text = notif.IconSymbol ?? (string.IsNullOrEmpty(notif.App) ? "🔔" : notif.App[..1].ToUpperInvariant());
            IconSymbolText.Visibility = Visibility.Visible;
        }

        Visibility = Visibility.Visible;
    }

    public void UpdateMorph(double w, double h, double radius, double sn)
    {
        if (ToastCapsule != null)
        {
            ToastCapsule.Width = w;
            ToastCapsule.Height = h;
            ToastCapsule.CornerRadius = new CornerRadius(0, 0, radius, radius);
        }
        if (ToastContentGrid != null)
        {
            ToastContentGrid.Opacity = Ui.Clamp((sn - 0.22) / 0.78);
        }
    }

    public void HideNotification()
    {
        _currentNotif = null;
        Visibility = Visibility.Collapsed;
    }

    private void OnToastClick(object sender, MouseButtonEventArgs e)
    {
        if (_currentNotif?.OnClick != null)
        {
            var click = _currentNotif.OnClick;
            _island?.DismissNotification();
            click();
        }
        else
        {
            _island?.DismissNotification();
            _island?.Expand();
        }
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        _island?.DismissNotification();
    }
}


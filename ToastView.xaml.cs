using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

        Color accent = notif.AccentColor ?? Colors.White;
        var accentBrush = new SolidColorBrush(accent);
        AppNameText.Foreground = accentBrush;

        if (string.IsNullOrEmpty(notif.Body))
        {
            BodyText.Visibility = Visibility.Collapsed;
        }
        else
        {
            BodyText.Text = notif.Body;
            BodyText.Visibility = Visibility.Visible;
        }

        if (notif.IsLoading)
        {
            IconImage.Source = null;
            IconSymbolText.Visibility = Visibility.Collapsed;
            if (SpinnerArc != null && SpinnerRotate != null)
            {
                SpinnerArc.Stroke = accentBrush;
                SpinnerArc.Visibility = Visibility.Visible;
                var spinAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(650))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
            }
        }
        else
        {
            if (SpinnerArc != null && SpinnerRotate != null)
            {
                SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                SpinnerArc.Visibility = Visibility.Collapsed;
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
        }

        if (CancelProcessBtn != null && DismissBtn != null)
        {
            bool showCancelPill = notif.IsLoading && notif.OnCancel != null;
            CancelProcessBtn.Visibility = showCancelPill ? Visibility.Visible : Visibility.Collapsed;
            DismissBtn.Visibility = showCancelPill ? Visibility.Collapsed : Visibility.Visible;
        }

        if (ProgressTrack != null && ProgressFill != null)
        {
            if (notif.ShowProgressBar)
            {
                ProgressTrack.Visibility = Visibility.Visible;
                ProgressFill.Background = accentBrush;
                if (notif.IsLoading)
                {
                    var loadAnim = new DoubleAnimation(16, 235, TimeSpan.FromMilliseconds(420))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    ProgressFill.BeginAnimation(WidthProperty, loadAnim);
                }
                else
                {
                    double startW = ProgressFill.ActualWidth > 16 ? ProgressFill.ActualWidth : 210;
                    var doneAnim = new DoubleAnimation(startW, 285, TimeSpan.FromMilliseconds(150));
                    ProgressFill.BeginAnimation(WidthProperty, doneAnim);
                }
            }
            else
            {
                ProgressFill.BeginAnimation(WidthProperty, null);
                ProgressTrack.Visibility = Visibility.Collapsed;
            }
        }

        Visibility = Visibility.Visible;
    }

    public void UpdateProgress(double pct, string title)
    {
        if (TitleText != null && !string.IsNullOrEmpty(title))
        {
            TitleText.Text = title;
        }
        if (ProgressFill != null)
        {
            int clamped = Math.Clamp((int)Math.Round(pct), 1, 99);
            ProgressFill.BeginAnimation(WidthProperty, null);
            ProgressFill.Width = Math.Clamp(220.0 * (clamped / 100.0), 16.0, 218.0);
        }
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
        var cancel = _currentNotif?.OnCancel;
        _island?.DismissNotification();
        cancel?.Invoke();
    }
}


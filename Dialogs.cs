using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuickNotchCs;

internal static class DialogStyle
{
    public static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI");

    public static Border PillButton(string text, Color bg, Color hot)
    {
        var brush = new SolidColorBrush(bg);
        var b = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 8, 22, 8),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        b.MouseEnter += (_, _) => brush.Color = hot;
        b.MouseLeave += (_, _) => brush.Color = bg;
        return b;
    }
}

/// <summary>Неблокирующее уведомление под островом. Клик закрывает (порт Toast).</summary>
public sealed class Toast : Window
{
    private const double ToastWidth = 380;
    private readonly IslandView _isl;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double _x0, _y0, _y1;
    private bool _closed;
    private bool _animating = true;

    public Toast(IslandView isl, string title, string message, Color accent, int? timeoutMs = null)
    {
        _isl = isl;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = ToastWidth;
        Cursor = Cursors.Hand;
        FontFamily = DialogStyle.Font;

        string text = message.Contains('\n')
            ? message[..Math.Min(400, message.Length)]
            : Ui.Trim(message, 400);

        var button = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(18),
            Width = 104,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock
            {
                Text = Loc.T("Готово", "Done", "Tamam"),
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Ellipse
        {
            Width = 12, Height = 12, Fill = new SolidColorBrush(accent),
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Bold });

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Theme.C("#ebebf5")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0),
        });
        stack.Children.Add(button);

        Content = new Border
        {
            Background = Theme.SurfaceBrush,
            BorderBrush = Theme.Surface2Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(28),
            Padding = new Thickness(24, 18, 24, 18),
            Child = stack,
        };

        Measure(new Size(ToastWidth, double.PositiveInfinity));
        Height = Math.Ceiling(DesiredSize.Height);

        double offset = isl.Toasts.Sum(t => t.Height + 10);
        _x0 = SystemParameters.PrimaryScreenWidth / 2 - ToastWidth / 2;
        _y1 = IslandView.TopMargin + 64 + offset;
        _y0 = _y1 - 28;
        isl.Toasts.Add(this);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _x0;
        Top = _y0;
        Opacity = 0;
        MouseLeftButtonDown += (_, _) => Dismiss();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            WinApi.SetWindowLong(h, WinApi.GWL_EXSTYLE,
                WinApi.GetWindowLong(h, WinApi.GWL_EXSTYLE) | WinApi.WS_EX_TOOLWINDOW | WinApi.WS_EX_NOACTIVATE);
        };

        Show();
        CompositionTarget.Rendering += Step;
        if (timeoutMs is > 0)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(timeoutMs.Value) };
            timer.Tick += (_, _) => { timer.Stop(); Dismiss(); };
            timer.Start();
        }
    }

    private void Step(object? sender, EventArgs e)
    {
        if (!_animating) return;
        double p = Math.Min(1.0, _clock.Elapsed.TotalSeconds / 0.45);
        Top = _y0 + (_y1 - _y0) * Spring.EaseOutBack(p, 1.4);
        Opacity = Math.Min(1.0, p * 3);
        if (p >= 1)
        {
            _animating = false;
            CompositionTarget.Rendering -= Step;
        }
    }

    public void Dismiss()
    {
        if (_closed) return;
        _closed = true;
        CompositionTarget.Rendering -= Step;
        _isl.Toasts.Remove(this);
        Close();
    }
}

/// <summary>Тёмный модальный диалог ввода текста (порт ask).</summary>
public static class AskDialog
{
    public static string? Show(IslandView isl, string title, string prompt, string initial = "")
    {
        string? result = null;
        var dlg = new Window
        {
            Title = title,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420,
            Background = Theme.SurfaceBrush,
            FontFamily = DialogStyle.Font,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };

        var entry = new TextBox
        {
            Text = initial,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            SelectionBrush = Theme.AccentBrush,
            BorderThickness = new Thickness(0),
            FontSize = 14,
        };
        var entryBorder = new Border
        {
            Background = Theme.Surface2Brush,
            BorderBrush = Theme.Surface3Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 8, 0, 0),
            Child = entry,
        };
        entry.GotKeyboardFocus += (_, _) => entryBorder.BorderBrush = Theme.AccentBrush;
        entry.LostKeyboardFocus += (_, _) => entryBorder.BorderBrush = Theme.Surface3Brush;

        var error = new TextBlock { Foreground = Theme.RedBrush, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), MinHeight = 16 };
        var cancel = DialogStyle.PillButton(Loc.T("Отмена", "Cancel", "İptal"), Theme.Surface2, Theme.Surface3);
        var ok = DialogStyle.PillButton(Loc.T("Готово", "Done", "Tamam"), Theme.Accent, Theme.AccentHot);
        cancel.Margin = new Thickness(0, 0, 8, 0);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(cancel);
        row.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        root.Children.Add(new TextBlock { Text = prompt, Foreground = new SolidColorBrush(Theme.C("#ebebf5")), FontSize = 14 });
        root.Children.Add(entryBorder);
        root.Children.Add(error);
        root.Children.Add(row);
        dlg.Content = root;

        void Submit()
        {
            string value = entry.Text.Trim();
            if (value.Length == 0)
            {
                error.Text = Loc.T("Поле не может быть пустым", "Field cannot be empty", "Alan boş bırakılamaz");
                return;
            }
            result = value;
            dlg.DialogResult = true;
        }

        ok.MouseLeftButtonUp += (_, _) => Submit();
        cancel.MouseLeftButtonUp += (_, _) => dlg.DialogResult = false;
        entry.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Submit();
            else if (e.Key == Key.Escape) dlg.DialogResult = false;
        };
        dlg.SourceInitialized += (_, _) => WinApi.DarkTitlebar(new WindowInteropHelper(dlg).Handle);
        dlg.Loaded += (_, _) =>
        {
            dlg.Left = SystemParameters.PrimaryScreenWidth / 2 - dlg.ActualWidth / 2;
            dlg.Top = SystemParameters.PrimaryScreenHeight / 3 - dlg.ActualHeight / 2;
            entry.Focus();
            entry.SelectAll();
        };

        isl.Busy++;
        try { dlg.ShowDialog(); }
        finally { isl.Busy--; }
        return result;
    }
}


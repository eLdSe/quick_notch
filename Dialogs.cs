using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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

/// <summary>Премиальный Apple Glass модальный диалог ввода текста.</summary>
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
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            FontFamily = DialogStyle.Font,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Opacity = 0
        };

        // Contextual icon and accent tone
        string lower = (title + " " + prompt).ToLowerInvariant();
        string iconGlyph;
        Color iconColor;

        if (lower.Contains("задач") || lower.Contains("напомин") || lower.Contains("task") || lower.Contains("remind") || lower.Contains("план"))
        {
            iconGlyph = "\uE787"; // Calendar
            iconColor = Color.FromRgb(0x60, 0xA5, 0xFA); // Blue
        }
        else if (lower.Contains("город") || lower.Contains("погод") || lower.Contains("city") || lower.Contains("weather"))
        {
            iconGlyph = "\uE707"; // Location Pin
            iconColor = Color.FromRgb(0x2D, 0xD4, 0xBF); // Teal
        }
        else if (lower.Contains("ссылк") || lower.Contains("link") || lower.Contains("url") || lower.Contains("сайт") || lower.Contains("веб"))
        {
            iconGlyph = "\uE71B"; // Link
            iconColor = Color.FromRgb(0xC0, 0x84, 0xFC); // Purple
        }
        else if (lower.Contains("ключ") || lower.Contains("key") || lower.Contains("api") || lower.Contains("токен"))
        {
            iconGlyph = "\uE8D7"; // Key / Permissions
            iconColor = Color.FromRgb(0xFB, 0xBF, 0x24); // Amber
        }
        else
        {
            iconGlyph = "\uE70F"; // Edit
            iconColor = Theme.Accent;
        }

        // --- Card Root Border ---
        var card = new Border
        {
            Width = 430,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(246, 20, 20, 24)),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(90, 255, 255, 255), 0.0), // Top light specular reflection
                    new(Color.FromArgb(30, 255, 255, 255), 0.35),
                    new(Color.FromArgb(14, 255, 255, 255), 1.0)
                },
                new Point(0, 0),
                new Point(0, 1)
            ),
            Effect = new DropShadowEffect
            {
                BlurRadius = 38,
                ShadowDepth = 10,
                Opacity = 0.65,
                Color = Colors.Black
            },
            Padding = new Thickness(22, 18, 22, 20)
        };

        // --- Header ---
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon badge
        var iconBorder = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(35, iconColor.R, iconColor.G, iconColor.B)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, iconColor.R, iconColor.G, iconColor.B)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = iconGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = new SolidColorBrush(iconColor),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconBorder, 0);
        headerGrid.Children.Add(iconBorder);

        // Title
        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(titleBlock, 1);
        headerGrid.Children.Add(titleBlock);

        // Close button
        var closeBtn = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "\uE711", // Segoe MDL2 Assets ChromeClose
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        closeBtn.MouseEnter += (_, _) =>
        {
            closeBtn.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            ((TextBlock)closeBtn.Child).Foreground = Brushes.White;
        };
        closeBtn.MouseLeave += (_, _) =>
        {
            closeBtn.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
            ((TextBlock)closeBtn.Child).Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
        };
        void CloseCancel()
        {
            result = null;
            try { dlg.DialogResult = false; } catch { }
            try { dlg.Close(); } catch { }
        }

        closeBtn.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            CloseCancel();
        };
        closeBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseCancel();
        };
        Grid.SetColumn(closeBtn, 2);
        headerGrid.Children.Add(closeBtn);

        // --- Prompt ---
        var promptBlock = new TextBlock
        {
            Text = prompt,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 8)
        };

        // --- Input Well ---
        var wellGrid = new Grid();
        var placeholder = new TextBlock
        {
            Text = Loc.T("Введите значение...", "Enter value...", "Bir değer girin..."),
            Foreground = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
            IsHitTestVisible = false,
            Visibility = string.IsNullOrEmpty(initial) ? Visibility.Visible : Visibility.Collapsed
        };

        var entry = new TextBox
        {
            Text = initial,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            SelectionBrush = Theme.AccentBrush,
            BorderThickness = new Thickness(0),
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        entry.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(entry.Text) ? Visibility.Visible : Visibility.Collapsed;
        };

        wellGrid.Children.Add(placeholder);
        wellGrid.Children.Add(entry);

        var wellBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 12, 12, 15)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Child = wellGrid
        };

        entry.GotKeyboardFocus += (_, _) =>
        {
            wellBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(180, iconColor.R, iconColor.G, iconColor.B));
            wellBorder.Background = new SolidColorBrush(Color.FromArgb(230, 16, 16, 20));
        };
        entry.LostKeyboardFocus += (_, _) =>
        {
            wellBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            wellBorder.Background = new SolidColorBrush(Color.FromArgb(200, 12, 12, 15));
        };

        // --- Error TextBlock ---
        var errorBlock = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)), // Apple Red
            FontSize = 12,
            Margin = new Thickness(2, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };

        // --- Footer ---
        var footerGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Keyboard hint on left
        var hintBlock = new TextBlock
        {
            Text = "Esc: " + Loc.T("отмена", "cancel", "iptal"),
            Foreground = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(hintBlock, 0);
        footerGrid.Children.Add(hintBlock);

        // Buttons on right
        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal };

        // Cancel button
        var cancelBtn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = Loc.T("Отмена", "Cancel", "İptal"),
                Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        cancelBtn.MouseEnter += (_, _) => cancelBtn.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        cancelBtn.MouseLeave += (_, _) => cancelBtn.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        cancelBtn.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            CloseCancel();
        };
        cancelBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseCancel();
        };
        buttonsRow.Children.Add(cancelBtn);

        // Submit button
        var submitBrush = new SolidColorBrush(Theme.Accent);
        var submitBtn = new Border
        {
            Background = submitBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 7, 20, 7),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = Loc.T("Готово", "Done", "Tamam"),
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        submitBtn.MouseEnter += (_, _) => submitBrush.Color = Theme.AccentHot;
        submitBtn.MouseLeave += (_, _) => submitBrush.Color = Theme.Accent;
        submitBtn.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };
        submitBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };
        buttonsRow.Children.Add(submitBtn);

        Grid.SetColumn(buttonsRow, 1);
        footerGrid.Children.Add(buttonsRow);

        // --- Assemble Stack ---
        var stack = new StackPanel();
        stack.Children.Add(headerGrid);
        stack.Children.Add(promptBlock);
        stack.Children.Add(wellBorder);
        stack.Children.Add(errorBlock);
        stack.Children.Add(footerGrid);

        card.Child = stack;

        // Window dragging on card background only (ignore buttons and input well)
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                if (e.OriginalSource is DependencyObject dep)
                {
                    for (DependencyObject? cur = dep; cur != null && cur != card; cur = VisualTreeHelper.GetParent(cur))
                    {
                        if (cur == cancelBtn || cur == closeBtn || cur == submitBtn || cur == wellBorder || cur is TextBox || cur is Button)
                            return;
                    }
                }
                try { dlg.DragMove(); } catch { }
            }
        };

        // Wrap card in container with margin for DropShadowEffect
        var outerWrapper = new Border
        {
            Margin = new Thickness(24),
            Background = Brushes.Transparent,
            Child = card
        };
        dlg.Content = outerWrapper;

        void Submit()
        {
            string value = entry.Text.Trim();
            if (value.Length == 0)
            {
                errorBlock.Text = Loc.T("Поле не может быть пустым", "Field cannot be empty", "Alan boş bırakılamaz");
                errorBlock.Visibility = Visibility.Visible;
                return;
            }
            result = value;
            try { dlg.DialogResult = true; } catch { }
            try { dlg.Close(); } catch { }
        }

        entry.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Submit(); }
            else if (e.Key == Key.Escape) { e.Handled = true; CloseCancel(); }
        };

        // Escape also on root window
        dlg.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseCancel(); }
        };

        dlg.Loaded += (_, _) =>
        {
            dlg.Left = (SystemParameters.PrimaryScreenWidth - dlg.ActualWidth) / 2;
            dlg.Top = (SystemParameters.PrimaryScreenHeight - dlg.ActualHeight) / 2.6; // Slightly above center

            // Smooth subtle fade-in animation
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            dlg.BeginAnimation(UIElement.OpacityProperty, anim);

            entry.Focus();
            entry.SelectAll();
        };

        isl.Busy++;
        try { dlg.ShowDialog(); }
        finally { isl.Busy--; }
        return result;
    }
}


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class ClipboardView : UserControl
{
    private IslandView? _island;
    private string _filter = "all";
    private int _selectedIndex = 0;
    private ClipItem? _selectedItem;

    public ClipboardView()
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

    private static (string Tag, Color BgCol, Color FgCol) DetectClipType(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return ("URL", Color.FromRgb(0x1E, 0x3A, 0x5F), Color.FromRgb(0x60, 0xA5, 0xFA));
        }
        if (trimmed.StartsWith("#") && (trimmed.Length == 4 || trimmed.Length == 7 || trimmed.Length == 9) &&
            System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9a-fA-F]+$"))
        {
            return ("HEX", Color.FromRgb(0x1B, 0x3D, 0x2B), Color.FromRgb(0x4A, 0xDE, 0x80));
        }
        if (trimmed.Contains('{') && trimmed.Contains('}') ||
            trimmed.Contains("();") ||
            trimmed.StartsWith("class ") || trimmed.StartsWith("function ") ||
            trimmed.StartsWith("const ") || trimmed.StartsWith("import ") ||
            trimmed.StartsWith("SELECT ") || trimmed.StartsWith("def "))
        {
            return ("КОД", Color.FromRgb(0x38, 0x23, 0x52), Color.FromRgb(0xC0, 0x84, 0xFC));
        }
        return ("ТЕКСТ", Color.FromRgb(0x26, 0x26, 0x29), Color.FromRgb(0x8E, 0x8E, 0x93));
    }

    private bool MatchesFilter(ClipItem item)
    {
        if (_filter == "all") return true;
        var (tag, _, _) = DetectClipType(item.Text);
        if (_filter == "code") return tag == "КОД";
        if (_filter == "url") return tag == "URL";
        if (_filter == "text") return tag == "ТЕКСТ" || tag == "HEX";
        return true;
    }

    private static string FormatAgo(DateTime? ts)
    {
        if (ts == null) return "закреплено";
        int s = (int)(DateTime.Now - ts.Value).TotalSeconds;
        if (s < 10) return "сейчас";
        if (s < 60) return $"{s} с назад";
        if (s < 3600) return $"{s / 60} мин назад";
        if (s < 86400) return $"{s / 3600} ч назад";
        return $"{s / 86400} д назад";
    }

    public void RefreshAll()
    {
        if (_island == null) return;

        var allItems = Sections.Clipboard.Items;
        var filtered = allItems.Where(MatchesFilter).ToList();

        if (filtered.Count == 0)
        {
            ClipsPanel.Children.Clear();
            EmptyClipsPanel.Visibility = Visibility.Visible;
            _selectedItem = null;
            UpdatePreview(null);
            return;
        }

        EmptyClipsPanel.Visibility = Visibility.Collapsed;

        if (_selectedIndex >= filtered.Count) _selectedIndex = Math.Max(0, filtered.Count - 1);
        _selectedItem = filtered[_selectedIndex];

        ClipsPanel.Children.Clear();

        for (int i = 0; i < filtered.Count; i++)
        {
            var item = filtered[i];
            bool isSelected = i == _selectedIndex;

            var card = new Border
            {
                Background = isSelected ? new SolidColorBrush(Color.FromArgb(45, 10, 132, 255)) : (Brush)FindResource("Surface2Brush"),
                BorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Tag = i
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Бейдж типа
            var (tag, tagBg, tagFg) = DetectClipType(item.Text);
            var badge = new Border
            {
                Background = new SolidColorBrush(tagBg),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4, 2, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = tag,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(tagFg)
            };
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            // Текст и метаданные
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            string firstLine = item.Text.ReplaceLineEndings("\n").Trim().Split('\n')[0].Trim();
            if (firstLine.Length == 0) firstLine = "…";
            if (firstLine.Length > 45) firstLine = firstLine[..42] + "…";

            var titleText = new TextBlock
            {
                Text = firstLine,
                FontSize = 11.5,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isSelected ? Brushes.White : (Brush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var metaText = new TextBlock
            {
                Text = $"{FormatAgo(item.Time)}  ·  {item.Text.Length} симв.",
                FontSize = 9.5,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            sp.Children.Add(titleText);
            sp.Children.Add(metaText);
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            // Иконка пина
            if (item.Pinned)
            {
                var pin = new TextBlock
                {
                    Text = "📌",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                Grid.SetColumn(pin, 2);
                grid.Children.Add(pin);
            }

            card.Child = grid;

            int clickIdx = i;
            card.MouseLeftButtonUp += (s, e) =>
            {
                _selectedIndex = clickIdx;
                _selectedItem = item;
                if (_island != null) Sections.Clipboard.CopyItem(_island, item);
                RefreshAll();
            };

            ClipsPanel.Children.Add(card);
        }

        UpdatePreview(_selectedItem);
    }

    private void UpdatePreview(ClipItem? item)
    {
        if (item == null)
        {
            UrlPreviewPanel.Visibility = Visibility.Collapsed;
            HexPreviewPanel.Visibility = Visibility.Collapsed;
            TextPreviewScroll.Visibility = Visibility.Collapsed;
            PreviewStatsText.Text = "0 симв.  ·  0 стр.";
            PreviewTypeBadge.Visibility = Visibility.Collapsed;
            PreviewMetaText.Text = "";
            return;
        }

        PreviewTypeBadge.Visibility = Visibility.Visible;
        var (tag, tagBg, tagFg) = DetectClipType(item.Text);
        PreviewTypeBadge.Background = new SolidColorBrush(tagBg);
        PreviewTypeBadgeText.Text = tag;
        PreviewTypeBadgeText.Foreground = new SolidColorBrush(tagFg);
        PreviewMetaText.Text = FormatAgo(item.Time);

        PinButtonText.Text = item.Pinned ? "📌 Открепить" : "📌 Закрепить";

        int lineCount = item.Text.Count(c => c == '\n') + 1;
        PreviewStatsText.Text = $"{item.Text.Length} симв.  ·  {lineCount} стр.";

        if (tag == "URL")
        {
            UrlPreviewPanel.Visibility = Visibility.Visible;
            HexPreviewPanel.Visibility = Visibility.Collapsed;
            TextPreviewScroll.Visibility = Visibility.Collapsed;
            UrlText.Text = item.Text.Trim();
        }
        else if (tag == "HEX")
        {
            UrlPreviewPanel.Visibility = Visibility.Collapsed;
            HexPreviewPanel.Visibility = Visibility.Visible;
            TextPreviewScroll.Visibility = Visibility.Collapsed;
            string hex = item.Text.Trim();
            HexCodeText.Text = hex;
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(hex);
                HexSwatch.Background = new SolidColorBrush(col);
                HexRgbText.Text = $"RGB: {col.R}, {col.G}, {col.B}";
            }
            catch { }
        }
        else
        {
            UrlPreviewPanel.Visibility = Visibility.Collapsed;
            HexPreviewPanel.Visibility = Visibility.Collapsed;
            TextPreviewScroll.Visibility = Visibility.Visible;
            PreviewTextBox.Text = item.Text;
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (FilterAllRadio.IsChecked == true) _filter = "all";
        else if (FilterCodeRadio.IsChecked == true) _filter = "code";
        else if (FilterUrlRadio.IsChecked == true) _filter = "url";
        else if (FilterTextRadio.IsChecked == true) _filter = "text";
        _selectedIndex = 0;
        RefreshAll();
    }

    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        if (_island != null)
        {
            Sections.Clipboard.ClearAll(_island);
            RefreshAll();
        }
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        if (_island != null && _selectedItem != null)
        {
            Sections.Clipboard.TogglePinItem(_island, _selectedItem);
            RefreshAll();
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_island != null && _selectedItem != null)
        {
            Sections.Clipboard.DeleteItem(_island, _selectedItem);
            RefreshAll();
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_island != null && _selectedItem != null)
        {
            Sections.Clipboard.CopyItem(_island, _selectedItem);
        }
    }

    private void OnOpenUrlClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_selectedItem.Text.Trim()) { UseShellExecute = true });
            }
            catch { }
        }
    }
}


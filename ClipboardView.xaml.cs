using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuickNotchCs;

public partial class ClipboardView : UserControl
{
    private IslandView? _island;
    private string _filter = "all";
    private string _searchQuery = "";
    private int _selectedIndex = 0;
    private ClipItem? _selectedItem;
    private int _displayLimit = 35;
    private DispatcherTimer? _copiedFeedbackTimer;

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

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        _island?.StopLeaveTimer();
    }

    private static (string Key, string Tag, Color BgCol, Color FgCol) DetectClipType(ClipItem item)
    {
        if (item.IsImage)
        {
            return ("IMG", Loc.T("ФОТО", "IMG", "FOTO"), Color.FromRgb(0x3D, 0x26, 0x12), Color.FromRgb(0xFB, 0xBF, 0x24));
        }

        string trimmed = item.Text.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return ("URL", "URL", Color.FromRgb(0x1E, 0x3A, 0x5F), Color.FromRgb(0x60, 0xA5, 0xFA));
        }
        if (trimmed.StartsWith("#") && (trimmed.Length == 4 || trimmed.Length == 7 || trimmed.Length == 9) &&
            System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9a-fA-F]+$"))
        {
            return ("HEX", "HEX", Color.FromRgb(0x1B, 0x3D, 0x2B), Color.FromRgb(0x4A, 0xDE, 0x80));
        }
        if (trimmed.Contains('{') && trimmed.Contains('}') ||
            trimmed.Contains("();") ||
            trimmed.StartsWith("class ") || trimmed.StartsWith("function ") ||
            trimmed.StartsWith("const ") || trimmed.StartsWith("import ") ||
            trimmed.StartsWith("SELECT ") || trimmed.StartsWith("def "))
        {
            return ("CODE", Loc.T("КОД", "CODE", "KOD"), Color.FromRgb(0x38, 0x23, 0x52), Color.FromRgb(0xC0, 0x84, 0xFC));
        }
        return ("TEXT", Loc.T("ТЕКСТ", "TEXT", "METİN"), Color.FromRgb(0x26, 0x26, 0x29), Color.FromRgb(0x8E, 0x8E, 0x93));
    }

    private bool MatchesFilter(ClipItem item)
    {
        if (_filter != "all")
        {
            var (key, _, _, _) = DetectClipType(item);
            if (_filter == "img" && key != "IMG") return false;
            if (_filter == "code" && key != "CODE") return false;
            if (_filter == "url" && key != "URL") return false;
            if (_filter == "text" && key != "TEXT" && key != "HEX") return false;
        }

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            string q = _searchQuery.Trim();
            if (item.IsImage)
            {
                string imgTitle = Loc.T(
                    $"Изображение Скриншот Фото {item.ImageWidth}x{item.ImageHeight} {item.ImageWidth}×{item.ImageHeight}",
                    $"Image Screenshot Photo {item.ImageWidth}x{item.ImageHeight} {item.ImageWidth}×{item.ImageHeight}",
                    $"Görsel Foto Ekran {item.ImageWidth}x{item.ImageHeight} {item.ImageWidth}×{item.ImageHeight}");
                if (!imgTitle.Contains(q, StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (!item.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatAgo(DateTime? ts)
    {
        if (ts == null) return Loc.T("закреплено", "pinned", "sabitlendi");
        int s = (int)(DateTime.Now - ts.Value).TotalSeconds;
        if (s < 10) return Loc.T("сейчас", "just now", "şimdi");
        if (s < 60) return Loc.T($"{s} с назад", $"{s}s ago", $"{s} sn önce");
        if (s < 3600) return Loc.T($"{s / 60} мин назад", $"{s / 60}m ago", $"{s / 60} dk önce");
        if (s < 86400) return Loc.T($"{s / 3600} ч назад", $"{s / 3600}h ago", $"{s / 3600} sa önce");
        return Loc.T($"{s / 86400} д назад", $"{s / 86400}d ago", $"{s / 86400} g önce");
    }

    public void RefreshAll()
    {
        if (_island == null) return;

        // Локализация статичных элементов интерфейса
        if (SearchPlaceholderText != null) SearchPlaceholderText.Text = Loc.T("Поиск в буфере…", "Search clipboard…", "Panoda ara…");
        if (FilterAllRadio != null) FilterAllRadio.Content = Loc.T("Все", "All", "Tümü");
        if (FilterImgRadio != null) FilterImgRadio.Content = Loc.T("Фото", "Img", "Foto");
        if (FilterCodeRadio != null) FilterCodeRadio.Content = Loc.T("Код", "Code", "Kod");
        if (FilterUrlRadio != null) FilterUrlRadio.Content = Loc.T("Ссылки", "Links", "Link");
        if (FilterTextRadio != null) FilterTextRadio.Content = Loc.T("Текст", "Text", "Metin");
        if (ClearAllButton != null) ClearAllButton.ToolTip = Loc.T("Очистить незакреплённые", "Clear unpinned", "Sabitlenmemişleri temizle");
        if (EmptyTitleText != null)
        {
            EmptyTitleText.Text = string.IsNullOrWhiteSpace(_searchQuery)
                ? Loc.T("Буфер обмена пуст", "Clipboard is empty", "Pano boş")
                : Loc.T("Ничего не найдено", "No matches found", "Sonuç bulunamadı");
        }
        if (EmptySubText != null)
        {
            EmptySubText.Text = string.IsNullOrWhiteSpace(_searchQuery)
                ? Loc.T("Скопируйте текст, ссылку или скриншот", "Copy text, a link, or a screenshot", "Metin, bağlantı veya ekran görüntüsü kopyalayın")
                : Loc.T("Попробуйте изменить поисковый запрос", "Try a different search query", "Farklı bir arama terimi deneyin");
        }
        if (PinButtonText != null) PinButtonText.Text = Loc.T("Закрепить", "Pin", "Sabitle");
        if (DeleteButtonText != null) DeleteButtonText.Text = Loc.T("Удалить", "Delete", "Sil");
        if (OpenUrlButtonText != null) OpenUrlButtonText.Text = Loc.T("Открыть ссылку в браузере", "Open link in browser", "Bağlantıyı tarayıcıda aç");
        if (SaveImageButtonText != null) SaveImageButtonText.Text = Loc.T("Сохранить PNG", "Save PNG", "PNG Kaydet");
        if (OpenImageFolderButtonText != null) OpenImageFolderButtonText.Text = Loc.T("Показать в папке", "Show in Folder", "Klasörde Göster");
        if (CopyButtonText != null && (_copiedFeedbackTimer == null || !_copiedFeedbackTimer.IsEnabled))
        {
            CopyButtonText.Text = Loc.T("Копировать", "Copy", "Kopyala");
        }
        if (CopiedToastText != null) CopiedToastText.Text = Loc.T("Скопировано в буфер", "Copied to clipboard", "Panoya kopyalandı");

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

        if (_selectedItem != null)
        {
            int found = filtered.IndexOf(_selectedItem);
            if (found >= 0) _selectedIndex = found;
            else _selectedIndex = 0;
        }
        else if (_selectedIndex >= filtered.Count)
        {
            _selectedIndex = Math.Max(0, filtered.Count - 1);
        }
        _selectedItem = filtered[_selectedIndex];

        ClipsPanel.Children.Clear();

        int renderLimit = Math.Min(filtered.Count, _displayLimit);
        for (int i = 0; i < renderLimit; i++)
        {
            var item = filtered[i];
            bool isSelected = i == _selectedIndex;

            var card = new Border
            {
                Background = isSelected ? new SolidColorBrush(Color.FromArgb(45, 10, 132, 255)) : (Brush)FindResource("Surface2Brush"),
                BorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Tag = i
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Бейдж типа
            var (_, tag, tagBg, tagFg) = DetectClipType(item);
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
            string firstLine;
            string metaStr;

            if (item.IsImage)
            {
                firstLine = Loc.T(
                    $"Скриншот ({item.ImageWidth}×{item.ImageHeight})",
                    $"Screenshot ({item.ImageWidth}×{item.ImageHeight})",
                    $"Ekran Görüntüsü ({item.ImageWidth}×{item.ImageHeight})");
                metaStr = $"{FormatAgo(item.Time)}  ·  PNG";
            }
            else
            {
                firstLine = item.Text.ReplaceLineEndings("\n").Trim().Split('\n')[0].Trim();
                if (firstLine.Length == 0) firstLine = "…";
                if (firstLine.Length > 45) firstLine = firstLine[..42] + "…";
                metaStr = Loc.T(
                    $"{FormatAgo(item.Time)}  ·  {item.Text.Length} симв.",
                    $"{FormatAgo(item.Time)}  ·  {item.Text.Length} chars",
                    $"{FormatAgo(item.Time)}  ·  {item.Text.Length} krkt");
            }

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
                Text = metaStr,
                FontSize = 9.5,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            sp.Children.Add(titleText);
            sp.Children.Add(metaText);
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            // Миниатюра для картинок
            if (item.IsImage && item.Image != null)
            {
                var thumb = new Border
                {
                    Width = 34,
                    Height = 26,
                    CornerRadius = new CornerRadius(5),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new ImageBrush(item.Image) { Stretch = Stretch.UniformToFill }
                };
                Grid.SetColumn(thumb, 2);
                grid.Children.Add(thumb);
            }

            // Иконка пина
            if (item.Pinned)
            {
                var pin = new TextBlock
                {
                    Text = "\uE718",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 11,
                    Foreground = Theme.AccentBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                Grid.SetColumn(pin, 3);
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
                ShowCopiedFeedback();
            };

            ClipsPanel.Children.Add(card);
        }

        if (filtered.Count > renderLimit)
        {
            var moreCard = new Border
            {
                Background = (Brush)FindResource("Surface2Brush"),
                BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 4, 0, 8),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            int rem = filtered.Count - renderLimit;
            var moreText = new TextBlock
            {
                Text = Loc.T($"Показать ещё ({rem})", $"Show more ({rem})", $"Daha fazla göster ({rem})"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush")
            };
            moreCard.Child = moreText;
            moreCard.MouseLeftButtonUp += (s, e) =>
            {
                _displayLimit += 35;
                RefreshAll();
            };
            ClipsPanel.Children.Add(moreCard);
        }

        UpdatePreview(_selectedItem);
    }

    private void UpdatePreview(ClipItem? item)
    {
        if (item == null)
        {
            UrlPreviewPanel.Visibility = Visibility.Collapsed;
            HexPreviewPanel.Visibility = Visibility.Collapsed;
            ImagePreviewPanel.Visibility = Visibility.Collapsed;
            TextPreviewScroll.Visibility = Visibility.Collapsed;
            PreviewStatsText.Text = Loc.T("0 симв.  ·  0 стр.", "0 chars  ·  0 lines", "0 krkt  ·  0 satır");
            PreviewTypeBadge.Visibility = Visibility.Collapsed;
            PreviewMetaText.Text = "";
            if (PinButtonText != null) PinButtonText.Text = Loc.T("Закрепить", "Pin", "Sabitle");
            if (PinIcon != null) PinIcon.Text = "\uE718";
            return;
        }

        PreviewTypeBadge.Visibility = Visibility.Visible;
        var (key, tag, tagBg, tagFg) = DetectClipType(item);
        PreviewTypeBadge.Background = new SolidColorBrush(tagBg);
        PreviewTypeBadgeText.Text = tag;
        PreviewTypeBadgeText.Foreground = new SolidColorBrush(tagFg);
        PreviewMetaText.Text = FormatAgo(item.Time);

        PinButtonText.Text = item.Pinned
            ? Loc.T("Открепить", "Unpin", "Kaldır")
            : Loc.T("Закрепить", "Pin", "Sabitle");
        PinIcon.Text = item.Pinned ? "\uE77A" : "\uE718";

        if (item.IsImage && item.Image != null)
        {
            UrlPreviewPanel.Visibility = Visibility.Collapsed;
            HexPreviewPanel.Visibility = Visibility.Collapsed;
            TextPreviewScroll.Visibility = Visibility.Collapsed;
            ImagePreviewPanel.Visibility = Visibility.Visible;
            PreviewImage.Source = item.Image;
            if (OpenImageFolderButton != null)
            {
                OpenImageFolderButton.Visibility = !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            PreviewStatsText.Text = $"{item.ImageWidth} × {item.ImageHeight} px  ·  PNG";
            return;
        }

        ImagePreviewPanel.Visibility = Visibility.Collapsed;

        int lineCount = item.Text.Count(c => c == '\n') + 1;
        PreviewStatsText.Text = Loc.T(
            $"{item.Text.Length} симв.  ·  {lineCount} стр.",
            $"{item.Text.Length} chars  ·  {lineCount} lines",
            $"{item.Text.Length} krkt  ·  {lineCount} satır");

        if (key == "URL")
        {
            UrlPreviewPanel.Visibility = Visibility.Visible;
            HexPreviewPanel.Visibility = Visibility.Collapsed;
            TextPreviewScroll.Visibility = Visibility.Collapsed;
            UrlText.Text = item.Text.Trim();
        }
        else if (key == "HEX")
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

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _island?.StopLeaveTimer();
        _searchQuery = SearchTextBox?.Text ?? "";
        if (SearchPlaceholderText != null)
        {
            SearchPlaceholderText.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ClearSearchButton != null)
        {
            ClearSearchButton.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Collapsed : Visibility.Visible;
        }
        _selectedIndex = 0;
        _selectedItem = null;
        _displayLimit = 35;
        RefreshAll();
    }

    private void OnClearSearchClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (SearchTextBox != null)
        {
            SearchTextBox.Text = "";
            SearchTextBox.Focus();
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (FilterAllRadio?.IsChecked == true) _filter = "all";
        else if (FilterImgRadio?.IsChecked == true) _filter = "img";
        else if (FilterCodeRadio?.IsChecked == true) _filter = "code";
        else if (FilterUrlRadio?.IsChecked == true) _filter = "url";
        else if (FilterTextRadio?.IsChecked == true) _filter = "text";
        _selectedIndex = 0;
        _selectedItem = null;
        _displayLimit = 35;
        RefreshAll();
    }

    private void OnSaveImageClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null || !_selectedItem.IsImage || _selectedItem.Image == null) return;
        _island?.StopLeaveTimer();
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("Сохранить изображение", "Save Image", "Görseli Kaydet"),
                Filter = "PNG Image (*.png)|*.png",
                FileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"
            };
            if (dlg.ShowDialog() == true)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(_selectedItem.Image));
                using var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write);
                encoder.Save(fs);
            }
        }
        catch { }
    }

    private void OnOpenImageFolderClick(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null || string.IsNullOrEmpty(_selectedItem.ImagePath) || !File.Exists(_selectedItem.ImagePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_selectedItem.ImagePath}\"") { UseShellExecute = true });
        }
        catch { }
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
            ShowCopiedFeedback();
        }
    }

    private void ShowCopiedFeedback()
    {
        if (CopyButtonText == null || CopyIcon == null || CopyButton == null) return;

        // 1. Анимация кнопки Copy
        CopyButtonText.Text = Loc.T("Скопировано!", "Copied!", "Kopyalandı!");
        CopyIcon.Text = "\uE73E";
        CopyButton.Background = new SolidColorBrush(Color.FromRgb(0x28, 0xC8, 0x40));

        // 2. Всплывающий тост над предпросмотром
        if (CopiedToastPill != null && CopiedToastText != null)
        {
            CopiedToastText.Text = Loc.T("Скопировано в буфер", "Copied to clipboard", "Panoya kopyalandı");
            CopiedToastPill.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
            CopiedToastPill.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        // 3. Таймер восстановления
        _copiedFeedbackTimer?.Stop();
        _copiedFeedbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1600)
        };
        _copiedFeedbackTimer.Tick += (_, _) =>
        {
            _copiedFeedbackTimer?.Stop();
            CopyButtonText.Text = Loc.T("Копировать", "Copy", "Kopyala");
            CopyIcon.Text = "\uE8C8";
            CopyButton.Background = (Brush)FindResource("AccentBrush");

            if (CopiedToastPill != null)
            {
                var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(220));
                fadeOut.Completed += (_, _) =>
                {
                    CopiedToastPill.Visibility = Visibility.Collapsed;
                };
                CopiedToastPill.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        };
        _copiedFeedbackTimer.Start();
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


using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class LinksView : UserControl
{
    private IslandView? _island;
    private string _filter = "all";
    private bool _isEditMode = false;

    private static readonly (string Label, string Url, string Icon, string Type)[] Presets = new[]
    {
        ("GitHub", "https://github.com", "⎇", "web"),
        ("Telegram", "https://web.telegram.org", "✈", "web"),
        ("YouTube", "https://youtube.com", "▶", "web"),
        ("ChatGPT", "https://chatgpt.com", "✦", "web"),
        ("Загрузки", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "📥", "folder"),
        ("Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "🖥", "folder"),
    };

    public LinksView()
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

    private static (string Icon, string Badge, string Type, Color BgCol, Color FgCol) DetectLinkInfo(string target)
    {
        string t = target.ToLowerInvariant();
        if (t.Contains("github.com")) return ("⎇", "GIT", "web", Color.FromRgb(0x24, 0x29, 0x2E), Color.FromRgb(0xF0, 0xF6, 0xFC));
        if (t.Contains("youtube.com") || t.Contains("youtu.be")) return ("▶", "PLAY", "web", Color.FromRgb(0x3F, 0x14, 0x14), Color.FromRgb(0xFF, 0x44, 0x44));
        if (t.Contains("t.me") || t.Contains("telegram")) return ("✈", "TG", "web", Color.FromRgb(0x12, 0x31, 0x4D), Color.FromRgb(0x38, 0xBD, 0xF8));
        if (t.Contains("chatgpt") || t.Contains("openai.com")) return ("✦", "AI", "web", Color.FromRgb(0x13, 0x36, 0x2B), Color.FromRgb(0x10, 0xA3, 0x7F));
        if (t.Contains("vk.com")) return ("VK", "SOC", "web", Color.FromRgb(0x15, 0x2C, 0x47), Color.FromRgb(0x4C, 0x94, 0xFF));
        if (t.Contains("yandex")) return ("Я", "SEARCH", "web", Color.FromRgb(0x3B, 0x18, 0x18), Color.FromRgb(0xFC, 0x3F, 0x1D));
        if (t.Contains("google")) return ("G", "SEARCH", "web", Color.FromRgb(0x16, 0x28, 0x46), Color.FromRgb(0x42, 0x85, 0xF4));
        if (t.EndsWith(".exe") || t.EndsWith(".lnk")) return ("⚡", "APP", "app", Color.FromRgb(0x33, 0x1E, 0x40), Color.FromRgb(0xFF, 0x37, 0x5F));
        if (t.Contains(":\\") || t.Contains(":/")) return ("📁", "DIR", "folder", Color.FromRgb(0x3B, 0x2E, 0x15), Color.FromRgb(0xFA, 0xCC, 0x15));
        return ("↗", "WEB", "web", Color.FromRgb(0x1E, 0x22, 0x2D), Color.FromRgb(0x8E, 0x8E, 0x93));
    }

    private static string GetSubtitle(string target)
    {
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(target);
                return uri.Host.Replace("www.", "");
            }
            catch { }
        }
        if (target.Contains(":\\") || target.Contains(":/"))
        {
            try
            {
                string fn = Path.GetFileName(target);
                if (!string.IsNullOrEmpty(fn)) return fn;
                return target.TrimEnd('\\', '/');
            }
            catch { }
        }
        return target;
    }

    public void RefreshAll()
    {
        if (_island == null) return;

        var allLinks = Sections.Links.Links;
        var filtered = allLinks.Where(l =>
        {
            if (_filter == "all") return true;
            var info = DetectLinkInfo(l.Target);
            return info.Type == _filter;
        }).ToList();

        LinksGridPanel.Children.Clear();

        for (int i = 0; i < filtered.Count; i++)
        {
            var link = filtered[i];
            var info = DetectLinkInfo(link.Target);

            var card = new Border
            {
                Width = 186,
                Height = 52,
                Background = (Brush)FindResource("Surface2Brush"),
                BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Иконка сервиса
            var iconBorder = new Border
            {
                Background = new SolidColorBrush(info.BgCol),
                CornerRadius = new CornerRadius(8),
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = new TextBlock
            {
                Text = info.Icon,
                FontSize = 13,
                Foreground = new SolidColorBrush(info.FgCol),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // Название и подзаголовок
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
            var labelText = new TextBlock
            {
                Text = link.Label,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var subText = new TextBlock
            {
                Text = GetSubtitle(link.Target),
                FontSize = 9.5,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            };
            sp.Children.Add(labelText);
            sp.Children.Add(subText);
            Grid.SetColumn(sp, 1);
            grid.Children.Add(sp);

            // Кнопка удаления в режиме редактирования
            if (_isEditMode)
            {
                var delBtn = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4),
                    Cursor = Cursors.Hand,
                    Content = new TextBlock { Text = "✕", FontSize = 10, Foreground = (Brush)FindResource("AccentHotBrush"), FontWeight = FontWeights.Bold }
                };
                delBtn.Click += (s, e) =>
                {
                    Sections.Links.RemoveLink(link);
                    RefreshAll();
                };
                Grid.SetColumn(delBtn, 2);
                grid.Children.Add(delBtn);
            }

            card.Child = grid;

            card.MouseLeftButtonUp += (s, e) =>
            {
                if (_isEditMode) return;
                try
                {
                    Process.Start(new ProcessStartInfo(link.Target) { UseShellExecute = true });
                    _island?.Collapse();
                }
                catch (Exception ex)
                {
                    _island?.Alert($"Не удалось открыть ссылку:\n{ex.Message}");
                }
            };

            LinksGridPanel.Children.Add(card);
        }

        RefreshPresets();
    }

    private void RefreshPresets()
    {
        PresetsPanel.Children.Clear();
        var allLinks = Sections.Links.Links;
        for (int i = 0; i < Presets.Length; i++)
        {
            var pr = Presets[i];
            bool alreadyAdded = allLinks.Any(l => l.Target.Equals(pr.Url, StringComparison.OrdinalIgnoreCase));
            if (alreadyAdded) continue;

            var btn = new Button
            {
                Style = (Style)FindResource("HeaderActionButton"),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };

            btn.Content = new TextBlock
            {
                Text = $"+ {pr.Label}",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            };

            btn.Click += (_, _) =>
            {
                Sections.Links.AddLink(new QuickLink { Label = pr.Label, Target = pr.Url });
                RefreshAll();
            };

            PresetsPanel.Children.Add(btn);
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (FilterAllRadio.IsChecked == true) _filter = "all";
        else if (FilterWebRadio.IsChecked == true) _filter = "web";
        else if (FilterFolderRadio.IsChecked == true) _filter = "folder";
        else if (FilterAppRadio.IsChecked == true) _filter = "app";
        RefreshAll();
    }

    private void OnAddUrlClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        string? url = _island.Ask("Новая ссылка", "Введите адрес сайта (URL):");
        if (string.IsNullOrWhiteSpace(url)) return;
        url = url.Trim();
        if (!url.Contains("://") && !url.Contains(":\\")) url = "https://" + url;

        string? label = _island.Ask("Название ссылки", "Введите отображаемое имя:", GetSubtitle(url));
        if (string.IsNullOrWhiteSpace(label)) label = GetSubtitle(url);

        Sections.Links.AddLink(new QuickLink { Label = label.Trim(), Target = url });
        RefreshAll();
    }

    private void OnAddFileClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите приложение или файл",
            Filter = "Все файлы (*.*)|*.*|Программы (*.exe;*.lnk)|*.exe;*.lnk"
        };
        if (dlg.ShowDialog() == true)
        {
            string name = Path.GetFileNameWithoutExtension(dlg.FileName);
            Sections.Links.AddLink(new QuickLink { Label = name, Target = dlg.FileName });
            RefreshAll();
        }
    }

    private void OnToggleEditClick(object sender, RoutedEventArgs e)
    {
        _isEditMode = !_isEditMode;
        EditModeButtonText.Text = _isEditMode ? "✓ Готово" : "✏️ Изменить";
        RefreshAll();
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class AiView : UserControl
{
    private IslandView? _island;

    public AiView()
    {
        InitializeComponent();
        PromptTextBox.TextChanged += (s, e) =>
        {
            PromptPlaceholder.Visibility = string.IsNullOrEmpty(PromptTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        };
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

    public void RefreshAll()
    {
        if (_island == null) return;

        // Обновляем бейдж провайдера
        string prov = _island.S.AiProvider.ToLowerInvariant();
        string badge = prov switch
        {
            "gemini" => "Gemini 2.5 Flash",
            "openai" => "OpenAI GPT-4o Mini",
            "deepseek" => "DeepSeek V3",
            "groq" => "Groq Llama 3.3",
            _ => "AI Online"
        };
        ProviderModelBadge.Text = badge;

        var msgs = Sections.Ai.Messages;
        if (msgs.Count == 0)
        {
            WelcomePanel.Visibility = Visibility.Visible;
            ChatScroll.Visibility = Visibility.Collapsed;
            MessagesPanel.Children.Clear();
            return;
        }

        WelcomePanel.Visibility = Visibility.Collapsed;
        ChatScroll.Visibility = Visibility.Visible;
        MessagesPanel.Children.Clear();

        for (int i = 0; i < msgs.Count; i++)
        {
            var msg = msgs[i];
            bool isUser = msg.Role == "user";

            var bubble = new Border
            {
                Background = isUser ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("Surface2Brush"),
                BorderBrush = isUser ? Brushes.Transparent : (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(isUser ? 0 : 1),
                CornerRadius = isUser ? new CornerRadius(16, 16, 4, 16) : new CornerRadius(16, 16, 16, 4),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = isUser ? 580 : 700
            };

            var sp = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = msg.Text,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            sp.Children.Add(textBlock);

            // Нижняя панель для ответа Аэро с кнопкой копирования
            if (!isUser && !string.IsNullOrWhiteSpace(msg.Text))
            {
                var copyBtn = new Button
                {
                    Style = (Style)FindResource("AiHeaderButton"),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Content = new TextBlock { Text = "📋 Копировать", FontSize = 9.5 }
                };
                string copyContent = msg.Text;
                copyBtn.Click += (_, _) =>
                {
                    try { Clipboard.SetText(copyContent); } catch { }
                };
                sp.Children.Add(copyBtn);
            }

            bubble.Child = sp;
            MessagesPanel.Children.Add(bubble);
        }

        ChatScroll.ScrollToEnd();
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        SubmitPrompt();
    }

    private void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            e.Handled = true;
            SubmitPrompt();
        }
    }

    private void SubmitPrompt()
    {
        if (_island == null) return;
        string text = PromptTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        PromptTextBox.Text = "";
        Sections.Ai.SendPrompt(_island, text);
        RefreshAll();
    }

    private void OnPasteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                PromptTextBox.AppendText(Clipboard.GetText());
                PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
                PromptTextBox.Focus();
            }
        }
        catch { }
    }

    private void OnNewChatClick(object sender, RoutedEventArgs e)
    {
        if (_island != null)
        {
            Sections.Ai.NewChat(_island);
            RefreshAll();
        }
    }

    private void OnToggleHistoryClick(object sender, RoutedEventArgs e)
    {
        bool show = HistoryDrawer.Visibility != Visibility.Visible;
        HistoryDrawer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) RefreshHistoryDrawer();
    }

    private void OnCloseHistoryClick(object sender, RoutedEventArgs e)
    {
        HistoryDrawer.Visibility = Visibility.Collapsed;
    }

    private void RefreshHistoryDrawer()
    {
        HistoryListPanel.Children.Clear();
        var sessions = Sections.Ai.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var ses = sessions[i];
            var btn = new Button
            {
                Style = (Style)FindResource("AiHeaderButton"),
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8, 10, 8)
            };

            var sp = new StackPanel();
            var title = new TextBlock
            {
                Text = ses.Title,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var date = new TextBlock
            {
                Text = ses.UpdatedAt.ToString("d MMMM, HH:mm"),
                FontSize = 9.5,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            sp.Children.Add(title);
            sp.Children.Add(date);
            btn.Content = sp;

            btn.Click += (_, _) =>
            {
                if (_island != null)
                {
                    Sections.Ai.LoadSession(_island, ses);
                    HistoryDrawer.Visibility = Visibility.Collapsed;
                    RefreshAll();
                }
            };

            HistoryListPanel.Children.Add(btn);
        }
    }

    private void OnApiKeyClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        string current = AiService.GetActiveKey(_island.S);
        string prov = _island.S.AiProvider.ToUpperInvariant();
        string? entered = _island.Ask($"API-ключ {prov}", "Вставьте ваш персональный ключ:", current);
        if (entered != null)
        {
            AiService.SetActiveKey(_island.S, entered.Trim());
            _island.SaveState();
            RefreshAll();
        }
    }

    private void OnPromptChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Content is string prompt && _island != null)
        {
            Sections.Ai.SendPrompt(_island, prompt);
            RefreshAll();
        }
    }
}


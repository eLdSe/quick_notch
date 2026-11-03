using System;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Threading;



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



        // Обновляем статус Аэро в левой панели (Штаб Аэро)

        bool roaming = _island.S.RobotRoaming;

        if (AeroStatusText != null && AeroStatusDot != null)

        {

            var greenBrush = (Brush)FindResource("GreenBrush");

            var blueBrush = (Brush)FindResource("AccentBrush");

            AeroStatusText.Text = roaming ? Loc.T("На рабочем столе", "On Desktop", "Masaüstünde") : Loc.T("В острове", "In Island", "Adada");

            AeroStatusText.Foreground = roaming ? blueBrush : greenBrush;

            AeroStatusDot.Fill = roaming ? blueBrush : greenBrush;

        }



        if (ToggleRoamIcon != null && ToggleRoamText != null)

        {

            ToggleRoamIcon.Text = roaming ? "🏠" : "🚀";

            ToggleRoamText.Text = roaming ? Loc.T("Вернуть в остров", "Dock to Island", "Adaya al") : Loc.T("На рабочий стол", "To Desktop", "Masaüstüne");

        }



        // Обновляем бейдж провайдера

        string prov = _island.S.AiProvider.ToLowerInvariant();

        string badge = prov switch

        {

            "gemini" => "⚙️  Gemini 2.5 Flash",

            "openai" => "⚙️  GPT-4o Mini",

            "deepseek" => "⚙️  DeepSeek V3",

            "groq" => "⚙️  Groq Llama 3.3",

            _ => "⚙️  AI Online"

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



    private void OnToggleRoamClick(object sender, RoutedEventArgs e)

    {

        if (_island == null) return;

        if (_island.S.RobotRoaming)

        {

            _island.DockAero();

        }

        else

        {

            _island.LaunchAeroToDesktop();

        }

        RefreshAll();

        _island.TopBarHost?.RefreshRobot();

    }



    private DispatcherTimer? _happyEyesTimer;



    public void TriggerAvatarHappy()

    {

        if (AeroAvatarNormalEyes == null || AeroAvatarHappyEyes == null) return;

        AeroAvatarNormalEyes.Visibility = Visibility.Collapsed;

        AeroAvatarHappyEyes.Visibility = Visibility.Visible;

        if (_happyEyesTimer == null)

        {

            _happyEyesTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };

            _happyEyesTimer.Tick += (_, _) =>

            {

                _happyEyesTimer.Stop();

                if (AeroAvatarNormalEyes != null && AeroAvatarHappyEyes != null)

                {

                    AeroAvatarNormalEyes.Visibility = Visibility.Visible;

                    AeroAvatarHappyEyes.Visibility = Visibility.Collapsed;

                }

            };

        }

        _happyEyesTimer.Stop();

        _happyEyesTimer.Start();

    }



    private void OnAeroAvatarClick(object sender, MouseButtonEventArgs e)

    {

        if (_island == null) return;

        _island.PetRobot();

        TriggerAvatarHappy();

    }



    private void OnPetAeroClick(object sender, RoutedEventArgs e)

    {

        if (_island == null) return;

        _island.PetRobot();

        TriggerAvatarHappy();

        RefreshAll();

    }



    private void OnExplainClipboardClick(object sender, RoutedEventArgs e)

    {

        if (_island == null) return;

        try

        {

            if (Clipboard.ContainsText())

            {

                string text = Clipboard.GetText().Trim();

                if (!string.IsNullOrEmpty(text))

                {

                    string prompt = $"Объясни кратко и понятно следующий текст:\n\n\"{text}\"";

                    Sections.Ai.SendPrompt(_island, prompt);

                    RefreshAll();

                    return;

                }

            }

        }

        catch { }



        PromptTextBox.Text = "Объясни текст: ";

        PromptTextBox.Focus();

        PromptTextBox.CaretIndex = PromptTextBox.Text.Length;

    }



    private void OnFixClipboardClick(object sender, RoutedEventArgs e)

    {

        if (_island == null) return;

        try

        {

            if (Clipboard.ContainsText())

            {

                string text = Clipboard.GetText().Trim();

                if (!string.IsNullOrEmpty(text))

                {

                    string prompt = $"Исправь орфографию, пунктуацию и сделай красивый рерайт текста:\n\n\"{text}\"";

                    Sections.Ai.SendPrompt(_island, prompt);

                    RefreshAll();

                    return;

                }

            }

        }

        catch { }



        PromptTextBox.Text = "Исправь ошибки: ";

        PromptTextBox.Focus();

        PromptTextBox.CaretIndex = PromptTextBox.Text.Length;

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



    private void OnClearAllHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_island != null && Sections.Ai.Sessions.Count > 0)
        {
            Sections.Ai.ClearAllSessions(_island);
            RefreshHistoryDrawer();
            RefreshAll();
        }
    }

    private void RefreshHistoryDrawer()
    {
        HistoryListPanel.Children.Clear();
        var sessions = Sections.Ai.Sessions;

        if (ClearHistoryButton != null)
        {
            ClearHistoryButton.Visibility = sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (sessions.Count == 0)
        {
            var emptyPanel = new StackPanel
            {
                Margin = new Thickness(0, 30, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptyPanel.Children.Add(new TextBlock
            {
                Text = "\uE81C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });
            emptyPanel.Children.Add(new TextBlock
            {
                Text = "История пуста",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            emptyPanel.Children.Add(new TextBlock
            {
                Text = "Здесь появятся ваши беседы",
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            HistoryListPanel.Children.Add(emptyPanel);
            return;
        }

        for (int i = 0; i < sessions.Count; i++)
        {
            var ses = sessions[i];

            var card = new Border
            {
                Background = (Brush)FindResource("Surface2Brush"),
                BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 7, 6, 7)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

            // Левая часть: название и дата (клик открывает беседу)
            var sp = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent };
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
            sp.MouseLeftButtonUp += (_, _) =>
            {
                if (_island != null)
                {
                    Sections.Ai.LoadSession(_island, ses);
                    HistoryDrawer.Visibility = Visibility.Collapsed;
                    RefreshAll();
                }
            };
            Grid.SetColumn(sp, 0);
            grid.Children.Add(sp);

            // Правая часть: кнопка удаления конкретной беседы
            var delBtn = new Button
            {
                Width = 26,
                Height = 26,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ToolTip = "Удалить эту беседу",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var delBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Width = 26,
                Height = 26
            };
            var delIcon = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            delBorder.Child = delIcon;
            delBtn.Content = delBorder;

            delBtn.MouseEnter += (_, _) =>
            {
                delBorder.Background = new SolidColorBrush(Color.FromArgb(40, 255, 69, 58));
                delIcon.Foreground = (Brush)FindResource("AccentHotBrush");
            };
            delBtn.MouseLeave += (_, _) =>
            {
                delBorder.Background = Brushes.Transparent;
                delIcon.Foreground = (Brush)FindResource("TextSecondaryBrush");
            };

            delBtn.Click += (_, _) =>
            {
                if (_island != null)
                {
                    Sections.Ai.DeleteSession(_island, ses);
                    RefreshHistoryDrawer();
                    RefreshAll();
                }
            };
            Grid.SetColumn(delBtn, 1);
            grid.Children.Add(delBtn);

            card.Child = grid;
            HistoryListPanel.Children.Add(card);
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




using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickNotchCs;

public sealed class AeroPetWindow : Window
{
    private readonly IslandView _island;
    private readonly AeroPetCanvas _canvas;
    private bool _isDragging;
    private Point _dragStartCursor;
    private Point _dragStartWindow;
    private bool _wasMoved;

    public AeroPetWindow(IslandView island)
    {
        _island = island;
        Title = "Aero Companion";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 240;
        Height = 220;

        _canvas = new AeroPetCanvas(island, this);
        Content = _canvas;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            int exStyle = WinApi.GetWindowLong(handle, WinApi.GWL_EXSTYLE);
            WinApi.SetWindowLong(handle, WinApi.GWL_EXSTYLE, exStyle | WinApi.WS_EX_TOOLWINDOW);

            RestorePosition();
        }
        catch { }
    }

    public void RestorePosition()
    {
        _isDragging = false;
        _wasMoved = false;
        _canvas.ResetIdleState();

        double screenW = SystemParameters.PrimaryScreenWidth;
        double screenH = SystemParameters.PrimaryScreenHeight;

        double x = _island.S.RobotRoamX;
        double y = _island.S.RobotRoamY;

        if (x < 0 || y < 0 || x > screenW - 60 || y > screenH - 60)
        {
            // По умолчанию сажаем в правый нижний угол над панелью задач
            Left = screenW - Width - 40;
            Top = screenH - Height - 80;
        }
        else
        {
            Left = Math.Max(0, Math.Min(screenW - Width, x));
            Top = Math.Max(0, Math.Min(screenH - Height, y));
        }
    }

    public void DockBack()
    {
        _canvas.StopAnimating();
        _canvas.StopIdleTimer();
        _island.DockAero();
    }

    public void RefreshMusicState()
    {
        if (_island.Media.Playing)
        {
            if (_canvas.IsSleeping) _canvas.WakeUp();
            else _canvas.NotifyMouseMoved();
        }
        _canvas.InvalidateVisual();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == VisibilityProperty)
        {
            if (Visibility == Visibility.Visible)
            {
                _canvas.ResetIdleState();
                _canvas.StartIdleTimer();
                _canvas.StartAnimating();
            }
            else
            {
                _canvas.StopAnimating();
                _canvas.StopIdleTimer();
            }
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _canvas.ResetScratchAnchor(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (WinApi.GetCursorPos(out var pt))
        {
            _isDragging = true;
            _wasMoved = false;
            _dragStartCursor = new Point(pt.X, pt.Y);
            _dragStartWindow = new Point(Left, Top);
            CaptureMouse();
            _canvas.StartAnimating();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging && WinApi.GetCursorPos(out var pt))
        {
            double dx = pt.X - _dragStartCursor.X;
            double dy = pt.Y - _dragStartCursor.Y;
            if (dx * dx + dy * dy > 16.0)
            {
                _wasMoved = true;
                if (_canvas.IsSleeping)
                {
                    _canvas.WakeUp();
                }
            }

            Left = Math.Max(0, Math.Min(SystemParameters.PrimaryScreenWidth - Width, _dragStartWindow.X + dx));
            Top = Math.Max(0, Math.Min(SystemParameters.PrimaryScreenHeight - Height, _dragStartWindow.Y + dy));
            _canvas.InvalidateVisual();
        }
        else if (!_isDragging)
        {
            // Почесать за ушком при быстром движении курсора над роботом
            var localPt = e.GetPosition(this);
            _canvas.HandleScratchMove(localPt);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();

            _island.S.RobotRoamX = Left;
            _island.S.RobotRoamY = Top;
            _island.SaveState();

            if (!_wasMoved)
            {
                // Одиночный клик: если спал — будим, если бодрствует — гладим!
                if (_canvas.IsSleeping)
                {
                    _canvas.WakeUp();
                }
                else
                {
                    _canvas.Pet();
                }
            }
        }
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.ChangedButton == MouseButton.Left)
        {
            DockBack();
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var menu = new ContextMenu();

        if (_canvas.IsSleeping)
        {
            var itemWake = new MenuItem { Header = "⚡ " + Loc.T("Разбудить Аэро", "Wake up Aero", "Aero'yu uyandır") };
            itemWake.Click += (_, _) => _canvas.WakeUp();
            menu.Items.Add(itemWake);
        }
        else
        {
            var itemSleep = new MenuItem { Header = "💤 " + Loc.T("Уложить спать", "Put to sleep", "Uyut") };
            itemSleep.Click += (_, _) => _canvas.FallAsleep();
            menu.Items.Add(itemSleep);
        }

        var itemTricks = new MenuItem { Header = "🎭 " + Loc.T("Трюки Аэро", "Aero Tricks", "Aero Numaraları") };
        var trickWipe = new MenuItem { Header = "✨ " + Loc.T("Протереть визор тряпочкой", "Wipe Visor", "Vizörü sil") };
        trickWipe.Click += (_, _) => _canvas.TriggerQuirk("wipe");
        var trickScan = new MenuItem { Header = "📡 " + Loc.T("Лазерное сканирование экрана", "Laser Scan", "Lazer tara") };
        trickScan.Click += (_, _) => _canvas.TriggerQuirk("scan");
        var trickBubble = new MenuItem { Header = "🫧 " + Loc.T("Надуть пузырь из жвачки", "Blow Bubble", "Baloncuk şişir") };
        trickBubble.Click += (_, _) => _canvas.TriggerQuirk("bubble");
        var trickFlip = new MenuItem { Header = "🌀 " + Loc.T("Сальто 360° в воздухе", "Barrel Roll 360°", "360° Takla") };
        trickFlip.Click += (_, _) => _canvas.TriggerQuirk("flip");
        var trickDj = new MenuItem { Header = "🎧 " + Loc.T("DJ Аэро (бит и наушники)", "DJ Aero Beat", "DJ Aero Müzik") };
        trickDj.Click += (_, _) => _canvas.TriggerQuirk("dj");
        var trickFirefly = new MenuItem { Header = "🦋 " + Loc.T("Цифровой светлячок", "Cyber Firefly", "Siber Ateşböceği") };
        trickFirefly.Click += (_, _) => _canvas.TriggerQuirk("firefly");
        var trickCoffee = new MenuItem { Header = "☕ " + Loc.T("Байт-кофе (подзарядка)", "Byte Coffee", "Bayt Kahvesi") };
        trickCoffee.Click += (_, _) => _canvas.TriggerQuirk("coffee");
        var trickPeekaboo = new MenuItem { Header = "🙈 " + Loc.T("Игра в прятки (Ку-ку!)", "Peek-a-boo!", "Ce-eee!") };
        trickPeekaboo.Click += (_, _) => _canvas.TriggerQuirk("peekaboo");
        var trickScratch = new MenuItem { Header = "🥰 " + Loc.T("Почесать за ушком", "Tickle Aero", "Gıdıkla") };
        trickScratch.Click += (_, _) => _canvas.TriggerScratchJoy();

        itemTricks.Items.Add(trickWipe);
        itemTricks.Items.Add(trickScan);
        itemTricks.Items.Add(trickBubble);
        itemTricks.Items.Add(trickFlip);
        itemTricks.Items.Add(trickDj);
        itemTricks.Items.Add(trickFirefly);
        itemTricks.Items.Add(trickCoffee);
        itemTricks.Items.Add(trickPeekaboo);
        itemTricks.Items.Add(trickScratch);
        menu.Items.Add(itemTricks);

        var itemAsk = new MenuItem { Header = "🧠 " + Loc.T("Спросить Аэро", "Ask Aero", "Aero'ya sor") };
        itemAsk.IsEnabled = !_canvas.IsThinking;
        itemAsk.Click += (_, _) => OpenAskBox();
        menu.Items.Add(itemAsk);

        var itemTalk = new MenuItem { Header = "💬 " + Loc.T("Поговорить с Аэро", "Chat with Aero", "Aero ile konuş") };
        itemTalk.Click += (_, _) => _canvas.Pet();
        menu.Items.Add(itemTalk);

        var itemDock = new MenuItem { Header = "🏝️ " + Loc.T("Вернуть в островок (двойной клик)", "Dock into Notch (double-click)", "Adaya geri döndür") };
        itemDock.Click += (_, _) => DockBack();
        menu.Items.Add(itemDock);

        var itemHide = new MenuItem { Header = "✕ " + Loc.T("Спрятать робота", "Hide Robot", "Robotu gizle") };
        itemHide.Click += (_, _) => _island.ToggleOpt("robot_companion");
        menu.Items.Add(itemHide);

        menu.IsOpen = true;
    }

    // ── «Спросить Аэро»: маленькое поле ввода под роботом ──
    private Window? _askBox;

    public void OpenAskBox()
    {
        if (_canvas.IsThinking) return;
        if (_askBox != null)
        {
            _askBox.Activate();
            return;
        }

        if (_canvas.IsSleeping) _canvas.WakeUp();
        _canvas.SetAsking(true);

        const double boxW = 260, boxH = 40;
        var input = new TextBox
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Text),
            CaretBrush = new SolidColorBrush(Theme.Accent),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 400,
        };
        var hint = new TextBlock
        {
            Text = Loc.T("Спроси у Аэро… (Enter)", "Ask Aero… (Enter)", "Aero'ya sor… (Enter)"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Theme.Muted),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
            IsHitTestVisible = false,
        };
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var grid = new Grid();
        grid.Children.Add(hint);
        grid.Children.Add(input);

        var border = new Border
        {
            Background = new SolidColorBrush(Theme.Mix(Theme.Surface2, Theme.Surface, 0.4)),
            BorderBrush = new SolidColorBrush(Theme.Accent),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 4, 12, 4),
            Child = grid,
        };

        double screenW = SystemParameters.WorkArea.Right;
        double screenH = SystemParameters.WorkArea.Bottom;
        double left = Left + (Width - boxW) / 2;
        double top = Top + Height - 18;
        if (top + boxH > screenH) top = Top - boxH + 10; // нет места снизу — открываем над роботом
        left = Math.Max(0, Math.Min(screenW - boxW, left));

        var box = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Width = boxW,
            Height = boxH,
            Left = left,
            Top = top,
            Content = border,
            Owner = this,
        };

        bool submitted = false;
        void CloseBox(bool submit)
        {
            if (_askBox == null) return;
            submitted = submit;
            _askBox = null;
            box.Close();
        }

        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseBox(false);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                string q = input.Text.Trim();
                if (q.Length == 0) return;
                CloseBox(true);
                _canvas.AskAi(q);
            }
        };
        box.Deactivated += (_, _) => CloseBox(false);
        box.Closed += (_, _) =>
        {
            _askBox = null;
            if (!submitted) _canvas.SetAsking(false);
        };

        _askBox = box;
        box.Show();
        box.Activate();
        input.Focus();
        Keyboard.Focus(input);
    }
}

public sealed class AeroPetCanvas : FrameworkElement
{
    private readonly IslandView _island;
    private readonly AeroPetWindow _window;
    private readonly Painter _painter;

    private readonly Spring _spEyeX = new(0), _spEyeY = new(0), _spHappy = new(0), _spHop = new(0), _spSleepWeight = new(0);
    private double _blinkTimer = 3.0;
    private double _blinkState = 0.0;
    private double _animPhase = 0.0;
    private double _bubbleTime = 0.0;
    private string _bubbleText = "";
    private string _emotion = "normal";
    private double _emotionTimer = 0.0;
    private bool _framing = false;
    private long _lastTs;

    // Сон и таймер неактивности курсора
    private readonly DispatcherTimer _idleCheckTimer;
    private Point _lastCursorPos;
    private double _mouseIdleSeconds = 0.0;
    private bool _hasYawnedThisIdle = false;
    public bool IsSleeping { get; private set; } = false;
    private double _sleepAnimTimer = 0.0;

    // Случайные активности в простое (Idle Quirks)
    private readonly RotateTransform _rotTransform = new();
    private double _quirkInterval = 18.0 + Random.Shared.NextDouble() * 15.0; // от 18 до 33 секунд
    public string ActiveQuirk { get; private set; } = ""; // "wipe", "scan", "bubble", "flip", "dj", "firefly", "coffee", "peekaboo"
    private double _quirkTimer = 0.0;
    private double _quirkPhase = 0.0;

    // "Почесать за ушком" (Scratching / Tickle)
    private Point _lastScratchMouse;
    private double _scratchAccum = 0.0;
    private double _scratchDecayTimer = 0.0;
    public double ScratchJoyTimer { get; private set; } = 0.0;

    // «Спросить Аэро» (ИИ)
    public bool IsAsking { get; private set; } = false;   // открыто поле ввода
    public bool IsThinking { get; private set; } = false; // ждём ответ модели
    private double _aiAnswerHold = 0.0;                   // пока читаешь ответ — не засыпает
    private readonly System.Collections.Generic.List<ChatMessage> _aiHistory = new();
    private const double BubbleTextW = 204.0;             // ширина текста в облачке (w - 16 - 20)
    private const double BubbleMaxTextH = 62.0;           // ~4 строки

    private const string AeroPersona =
        "Ты — Аэро, маленький милый робот-компаньон, который живёт на рабочем столе пользователя. " +
        "Отвечай на языке вопроса, дружелюбно, на «ты», ОЧЕНЬ кратко: максимум 2–3 коротких предложения, до 200 символов. " +
        "Без markdown, без списков, без блоков кода. Можно один эмодзи в конце.";

    public void SetAsking(bool asking)
    {
        IsAsking = asking;
        if (asking)
        {
            _mouseIdleSeconds = 0.0;
            _spSleepWeight.Target = 0.0;
            ActiveQuirk = "";
            _emotion = "normal";
            _emotionTimer = 0.0;
            _spHappy.Target = 0.6;
            _bubbleText = "Слушаю! 👂";
            _bubbleTime = 2.0;
        }
        StartAnimating();
    }

    public async void AskAi(string question)
    {
        if (IsThinking) return;
        IsAsking = false;
        IsThinking = true;
        ActiveQuirk = "";
        _emotion = "normal";
        _emotionTimer = 0.0;
        _spHappy.Target = 0.0;
        _bubbleText = "Думаю.";
        _bubbleTime = 999.0;
        StartAnimating();

        var s = _island.S;
        string answer;
        bool ok;

        if (string.IsNullOrWhiteSpace(AiService.GetActiveKey(s)))
        {
            ok = false;
            answer = "Мне нужен API-ключ 🔑 Добавь его в ИИ-разделе островка!";
        }
        else
        {
            string prompt = AeroPersona + "\n\nВопрос: " + question;
            var res = await AiService.AskAsync(prompt, s, _aiHistory.Count > 0 ? _aiHistory.ToArray() : null);
            ok = res.Success && !string.IsNullOrWhiteSpace(res.Text);
            if (ok)
            {
                answer = CleanAiText(res.Text);
                _aiHistory.Add(new ChatMessage { Role = "user", Text = question });
                _aiHistory.Add(new ChatMessage { Role = "model", Text = answer });
                if (_aiHistory.Count > 8) _aiHistory.RemoveRange(0, _aiHistory.Count - 8);
            }
            else
            {
                answer = "Ой, нет связи с базой 📡 Попробуй ещё раз!";
                Debug.WriteLine($"[Aero AI] {res.Error}");
            }
        }

        IsThinking = false;
        _mouseIdleSeconds = 0.0;
        _bubbleText = FitToBubble(answer);
        _bubbleTime = Math.Clamp(3.0 + _bubbleText.Length * 0.06, 4.0, 16.0);
        _aiAnswerHold = _bubbleTime;

        if (ok)
        {
            _spHop.V = -70.0; // радостный подскок — ответ готов
            _spHappy.Target = 0.8;
            _emotionTimer = 0.0;
        }
        else
        {
            _emotion = "surprised";
            _emotionTimer = 1.5;
        }
        StartAnimating();
        InvalidateVisual();
    }

    private static string CleanAiText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '*' || c == '`' || c == '#' || c == '_') continue;
            sb.Append(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
        }
        string t = sb.ToString().Trim();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t;
    }

    /// <summary>Обрезает ответ так, чтобы он влез в облачко (~4 строки).</summary>
    private string FitToBubble(string text)
    {
        _painter.Ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_painter.WrappedHeight(text, 11, BubbleTextW) <= BubbleMaxTextH) return text;

        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            string cand = text.Substring(0, mid).TrimEnd() + "…";
            if (_painter.WrappedHeight(cand, 11, BubbleTextW) <= BubbleMaxTextH) lo = mid;
            else hi = mid - 1;
        }
        string cut = text.Substring(0, lo);
        int sp = cut.LastIndexOf(' ');
        if (sp > lo * 0.6) cut = cut.Substring(0, sp);
        return cut.TrimEnd(' ', ',', '.', ';', ':') + "…";
    }

    public AeroPetCanvas(IslandView island, AeroPetWindow window)
    {
        _island = island;
        _window = window;
        _painter = new Painter(island);
        Cursor = Cursors.Hand;

        if (WinApi.GetCursorPos(out var pt))
            _lastCursorPos = new Point(pt.X, pt.Y);

        _idleCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(0.5)
        };
        _idleCheckTimer.Tick += OnIdleCheckTick;
    }

    public void ResetScratchAnchor(Point p)
    {
        _lastScratchMouse = p;
    }

    public void HandleScratchMove(Point p)
    {
        if (IsSleeping) return;
        if (_lastScratchMouse.X < 0 && _lastScratchMouse.Y < 0)
        {
            _lastScratchMouse = p;
            return;
        }
        double dx = p.X - _lastScratchMouse.X;
        double dy = p.Y - _lastScratchMouse.Y;
        _lastScratchMouse = p;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > 1.2 && dist < 60.0)
        {
            _scratchAccum += dist;
            _scratchDecayTimer = 0.6; // таймер сброса, если мышь замерла

            if (_scratchAccum > 320.0 && ScratchJoyTimer <= 0)
            {
                TriggerScratchJoy();
            }
        }
    }

    public void TriggerScratchJoy()
    {
        if (IsThinking) return;
        _scratchAccum = 0.0;
        ScratchJoyTimer = 3.5;
        _emotion = "love";
        _emotionTimer = 3.5;
        _spHappy.Target = 1.0;
        _spHop.V = -100.0; // Радостный вибро-подскок

        string[] phrases =
        {
            "Мррр... Как приятно! 🥰",
            "Щекотно! Хи-хи-хи! ✨",
            "Обожаю, когда чешут за ушком! 💙",
            "Бип-буп от счастья! Мяу! 🤖"
        };
        _bubbleText = phrases[Random.Shared.Next(phrases.Length)];
        _bubbleTime = 3.5;

        StartAnimating();
        InvalidateVisual();
    }

    public void TriggerQuirk(string? name = null)
    {
        if (IsSleeping || IsThinking || IsAsking) return;
        if (string.IsNullOrEmpty(name))
        {
            string[] quirks = { "wipe", "scan", "bubble", "flip", "dj", "firefly", "coffee", "peekaboo" };
            name = quirks[Random.Shared.Next(quirks.Length)];
        }

        ActiveQuirk = name;
        _quirkPhase = 0.0;

        if (name == "wipe")
        {
            _quirkTimer = 3.6;
            _bubbleText = "*протирает визор* ✨";
            _bubbleTime = 3.0;
        }
        else if (name == "scan")
        {
            _quirkTimer = 3.8;
            _bubbleText = "Сканирую окружение... 📡";
            _bubbleTime = 3.0;
        }
        else if (name == "bubble")
        {
            _quirkTimer = 3.5;
            _bubbleText = "*надувает жвачку* 🫧";
            _bubbleTime = 3.0;
        }
        else if (name == "flip")
        {
            _quirkTimer = 2.2;
            _bubbleText = "Смотри как умею! 🌀";
            _bubbleTime = 2.2;
        }
        else if (name == "dj")
        {
            _quirkTimer = 4.8;
            _bubbleText = "Ловлю кибер-вайб! 🎧🎶";
            _bubbleTime = 3.2;
        }
        else if (name == "firefly")
        {
            _quirkTimer = 4.6;
            _bubbleText = "Ой, светлячок! ✨";
            _bubbleTime = 2.8;
        }
        else if (name == "coffee")
        {
            _quirkTimer = 4.0;
            _bubbleText = "Время подзарядки! ☕🔋";
            _bubbleTime = 3.2;
        }
        else if (name == "peekaboo")
        {
            _quirkTimer = 3.4;
            _bubbleText = "Куда я пропал?.. 🙈";
            _bubbleTime = 1.8;
        }

        StartAnimating();
        InvalidateVisual();
    }

    public void ResetIdleState()
    {
        _mouseIdleSeconds = 0.0;
        _hasYawnedThisIdle = false;
        _spSleepWeight.Target = 0.0;
        _spSleepWeight.Snap();
        _spHop.Target = 0.0;
        _spHop.Snap();
        _scratchAccum = 0.0;
        _scratchDecayTimer = 0.0;
        ScratchJoyTimer = 0.0;
        ActiveQuirk = "";
        if (IsSleeping)
        {
            IsSleeping = false;
        }
        _emotion = "normal";
        _emotionTimer = 0.0;
        if (WinApi.GetCursorPos(out var pt))
        {
            _lastCursorPos = new Point(pt.X, pt.Y);
        }
        _lastScratchMouse = new Point(-1, -1);
    }

    public void NotifyMouseMoved()
    {
        if (WinApi.GetCursorPos(out var pt))
        {
            _lastCursorPos = new Point(pt.X, pt.Y);
        }
        WakeFromDrowsyIfNeeded();
    }

    private void WakeFromDrowsyIfNeeded()
    {
        if (IsSleeping) return;

        bool wasDrowsy = _mouseIdleSeconds >= 18.0 || _hasYawnedThisIdle;
        _mouseIdleSeconds = 0.0;
        _hasYawnedThisIdle = false;
        _spSleepWeight.Target = 0.0;

        if (wasDrowsy)
        {
            _spSleepWeight.Snap();
            if (_spHop.V > -10.0)
            {
                _spHop.V = -45.0; // Вздрагивает от неожиданности ровно один раз
            }
            _emotion = "surprised";
            _emotionTimer = 1.3;
            if (Random.Shared.NextDouble() < 0.35)
            {
                string[] catchPhrases = { "Ой! Чуть не уснул... 😅", "Я не сплю, я на страже! 👀", "Сенсоры онлайн! ⚡" };
                _bubbleText = catchPhrases[Random.Shared.Next(catchPhrases.Length)];
                _bubbleTime = 2.2;
            }
        }

        if (!IsSleeping && !_framing)
        {
            StartAnimating();
        }
    }

    private void OnIdleCheckTick(object? sender, EventArgs e)
    {
        if (_window.Visibility != Visibility.Visible) return;
        if (!_island.S.RobotRoaming) return;

        // Пока пишешь вопрос, ждёшь, читаешь ответ или играет музыка — Аэро не засыпает
        if (IsAsking || IsThinking || _aiAnswerHold > 0 || _island.Media.Playing)
        {
            if (IsSleeping)
            {
                WakeUp();
            }
            else if (_mouseIdleSeconds > 0 || _spSleepWeight.Target > 0)
            {
                _mouseIdleSeconds = 0.0;
                _hasYawnedThisIdle = false;
                _spSleepWeight.Target = 0.0;
                if (!_framing) StartAnimating();
            }
            if (WinApi.GetCursorPos(out var cp)) _lastCursorPos = new Point(cp.X, cp.Y);
            return;
        }

        if (WinApi.GetCursorPos(out var pt))
        {
            if (Math.Abs(pt.X - _lastCursorPos.X) < 3 && Math.Abs(pt.Y - _lastCursorPos.Y) < 3)
            {
                _mouseIdleSeconds += 0.5;

                // Фаза 1: милый сонный зевок (на ~18 сек)
                if (_mouseIdleSeconds >= 18.0 && !_hasYawnedThisIdle && !IsSleeping)
                {
                    _hasYawnedThisIdle = true;
                    _emotion = "yawn";
                    _emotionTimer = 2.6;
                    _bubbleText = "*Зевает...* 🥱";
                    _bubbleTime = 2.4;
                    _spEyeY.Target = 2.5;
                    StartAnimating();
                }

                // Фаза 2: плавное опускание век и микро-моргания (в OnRendering)
                // Фаза 3: полное погружение в глубокий сон на 30 сек
                if (_mouseIdleSeconds >= 30.0 && !IsSleeping)
                {
                    FallAsleep();
                }
            }
            else
            {
                _lastCursorPos = new Point(pt.X, pt.Y);
                WakeFromDrowsyIfNeeded();
            }
        }
    }

    public void StartAnimating()
    {
        if (_framing) return;
        _framing = true;
        _lastTs = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnRendering;
    }

    public void StartIdleTimer()
    {
        if (!_idleCheckTimer.IsEnabled) _idleCheckTimer.Start();
    }

    public void StopIdleTimer()
    {
        if (_idleCheckTimer.IsEnabled) _idleCheckTimer.Stop();
    }

    public void StopAnimating()
    {
        if (!_framing) return;
        _framing = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    public void FallAsleep()
    {
        if (IsSleeping) return;
        IsSleeping = true;
        _sleepAnimTimer = 0.0;
        _spSleepWeight.Target = 1.0;
        _emotion = "sleeping";
        _emotionTimer = 0.0;
        _bubbleText = "Zzz...";
        _bubbleTime = 2.5;
        _spEyeX.Target = 0.0;
        _spEyeY.Target = 3.0;
        _spHappy.Target = 0.0;
        StartAnimating();
        InvalidateVisual();
    }

    public void WakeUp()
    {
        if (!IsSleeping && _spSleepWeight.X <= 0.05) return;
        IsSleeping = false;
        _spSleepWeight.Target = 0.0;
        _mouseIdleSeconds = 0.0;
        _hasYawnedThisIdle = false;
        _emotion = "surprised";
        _emotionTimer = 3.5;
        _spHop.V = -155.0; // Высокий радостный прыжок
        _spHappy.Target = 1.0;

        string[] wakePhrases =
        {
            "Ой! Я не спал, я просто заряжался! ⚡",
            "Доброе утро! Все системы онлайн! ✨",
            "А?.. Кто здесь? А, привет! 🚀",
            "Зевнул... Батарея 100%, готов помогать! 🔋",
            "Я на связи! Что делаем дальше? 💫",
            "Сенсоры откалиброваны! Рад тебя видеть! 💙"
        };
        _bubbleText = wakePhrases[Random.Shared.Next(wakePhrases.Length)];
        _bubbleTime = Math.Max(3.2, 2.0 + _bubbleText.Length * 0.045);

        StartAnimating();
        InvalidateVisual();
    }

    public void Pet()
    {
        if (IsThinking) return;
        if (IsSleeping)
        {
            WakeUp();
            return;
        }

        var phrase = _island.PickNextBotPhrase();
        _emotion = phrase.Emotion;
        _emotionTimer = 3.5;
        _bubbleText = Loc.T(phrase.Ru, phrase.En, phrase.Tr);
        _bubbleTime = Math.Max(3.2, 2.0 + _bubbleText.Length * 0.045);
        _spHop.V = -135.0; // Высокий упругий радостный прыжок
        _spHappy.Target = 1.0;
        StartAnimating();
        InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_window.Visibility != Visibility.Visible || !_island.S.RobotRoaming)
        {
            StopAnimating();
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double dt = (now - _lastTs) / (double)Stopwatch.Frequency;

        // Плавная частота: 60 FPS для живого парения и засыпания, ~35 FPS только в глубоком сне
        double minDt = (IsSleeping && _spSleepWeight.Settled) ? 0.028 : 0.016;
        if (dt < minDt) return;
        _lastTs = now;
        dt = Math.Min(dt, 0.033);

        _animPhase = (_animPhase + dt * 3.2) % (Math.PI * 2000.0);

        // Плавный расчет веса сна (0.0 = бодрствует, 1.0 = спит)
        double targetSleep = IsSleeping ? 1.0 : (_mouseIdleSeconds > 20.0 ? Math.Min(1.0, (_mouseIdleSeconds - 20.0) / 10.0) : 0.0);
        _spSleepWeight.Target = targetSleep;
        _spSleepWeight.Step(dt, 70, 15); // Очень мягкое, естественное опускание век

        double sleepW = Math.Clamp(_spSleepWeight.X, 0.0, 1.0);

        if (IsSleeping)
        {
            _sleepAnimTimer += dt;
            _spEyeX.Target = 0;
            _spEyeY.Target = 3.0;
            _spHappy.Target = 0;
            _spEyeX.Step(dt, 160, 20);
            _spEyeY.Step(dt, 160, 20);
            _spHappy.Step(dt, 160, 20);
            _spHop.Step(dt, 200, 20);
        }
        else
        {
            // Расчёт направления взгляда на курсор (с учетом сонливости)
            var mouseScreen = WinApi.GetCursorPos(out var pt) ? new Point(pt.X, pt.Y) : new Point(0, 0);
            double cx = _window.Left + _window.Width / 2.0;
            double cy = _window.Top + _window.Height / 2.0;
            double dx = mouseScreen.X - cx;
            double dy = mouseScreen.Y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double maxEye = 6.0;
            double targetX = 0, targetY = 0;
            if (dist > 3.0)
            {
                // При сонливости взгляд становится менее резким, веки тянут вниз
                double factor = Math.Min(dist * 0.009, maxEye) * (1.0 - sleepW * 0.7);
                targetX = (dx / dist) * factor;
                targetY = (dy / dist) * factor + sleepW * 3.0;
            }
            else
            {
                targetY = sleepW * 3.0;
            }

            if (IsThinking)
            {
                // Кибер-анализ: глаза бегают слева направо, пока ждём ответ ИИ
                targetX = Math.Sin(_animPhase * 2.4) * 5.0;
                targetY = -1.5;
            }

            _spEyeX.Target = targetX;
            _spEyeY.Target = targetY;

            double kEye = Ui.Lerp(320.0, 140.0, sleepW);
            double cEye = Ui.Lerp(24.0, 18.0, sleepW);
            _spEyeX.Step(dt, kEye, cEye);
            _spEyeY.Step(dt, kEye, cEye);
            _spHappy.Step(dt, 280, 26);
            _spHop.Step(dt, 340, 22);

            // Таймер моргания
            if (_emotionTimer <= 0)
            {
                _blinkTimer -= dt;
                if (_blinkTimer <= 0)
                {
                    _blinkState = 1.0;
                    _blinkTimer = 2.5 + Random.Shared.NextDouble() * 3.5;
                }
                else if (_blinkState > 0)
                {
                    _blinkState = Math.Max(0.0, _blinkState - dt * 8.0);
                }
            }
            else
            {
                _blinkState = 0.0;
            }
        }

        if (_emotionTimer > 0)
        {
            _emotionTimer -= dt;
            if (_emotionTimer <= 0)
            {
                _emotion = IsSleeping ? "sleeping" : "normal";
                _spHappy.Target = 0.0;
            }
        }

        if (_bubbleTime > 0)
        {
            _bubbleTime -= dt;
            if (_bubbleTime <= 0) _bubbleText = "";
        }

        // Обновление почёсывания за ушком
        if (_scratchDecayTimer > 0)
        {
            _scratchDecayTimer -= dt;
            if (_scratchDecayTimer <= 0) _scratchAccum = 0.0;
        }
        if (ScratchJoyTimer > 0)
        {
            ScratchJoyTimer -= dt;
            if (ScratchJoyTimer <= 0) ScratchJoyTimer = 0.0;
        }

        // «Спросить Аэро»: глаза бегают, пока он думает
        if (_aiAnswerHold > 0) _aiAnswerHold -= dt;
        if (IsThinking)
        {
            int dots = (int)(_animPhase * 0.9) % 3;
            _bubbleText = dots == 0 ? "Думаю." : (dots == 1 ? "Думаю.." : "Думаю...");
            _bubbleTime = 999.0;
        }

        // Обновление случайных микро-активностей
        if (ActiveQuirk != "")
        {
            _quirkPhase += dt;
            _quirkTimer -= dt;

            if (ActiveQuirk == "flip")
            {
                if (_quirkPhase >= 0.35 && _quirkPhase - dt < 0.35)
                {
                    _spHop.V = -135.0; // Высокий акробатический взлёт
                }
            }
            else if (ActiveQuirk == "dj")
            {
                _spHappy.Target = 0.9;
            }
            else if (ActiveQuirk == "peekaboo")
            {
                if (_quirkPhase >= 1.8 && _quirkPhase - dt < 1.8)
                {
                    _spHop.V = -60.0; // Всплеск радости при раскрытии
                    _bubbleText = "Ку-ку! Я тут! 😋✨";
                    _bubbleTime = 2.4;
                }
            }
            else if (ActiveQuirk == "firefly")
            {
                double cx = _window.Left + _window.Width / 2.0;
                double cy = _window.Top + _window.Height / 2.0;
                if (_quirkPhase < 2.2)
                {
                    double theta = _quirkPhase * 3.2;
                    double ffx = cx + Math.Sin(theta) * 46.0;
                    double ffy = cy - 12.0 + Math.Cos(theta * 1.5) * 22.0;
                    _spEyeX.Target = Math.Clamp((ffx - cx) * 0.16, -6.0, 6.0);
                    _spEyeY.Target = Math.Clamp((ffy - cy) * 0.16, -5.0, 5.0);
                }
                else if (_quirkPhase < 3.4)
                {
                    _spEyeX.Target = 0.0;
                    _spEyeY.Target = -5.0; // Глазки вверх на антеннку
                }
            }

            if (_quirkTimer <= 0)
            {
                if (ActiveQuirk == "scan")
                {
                    _bubbleText = "Всё чисто, угроз нет! 🛡️";
                    _bubbleTime = 2.5;
                }
                else if (ActiveQuirk == "wipe")
                {
                    _bubbleText = "Визор сияет! ✧";
                    _bubbleTime = 2.5;
                }
                else if (ActiveQuirk == "flip")
                {
                    _bubbleText = "Вжух! 360 no-scope! 🌀";
                    _bubbleTime = 2.4;
                    _spHop.V = -30.0;
                }
                else if (ActiveQuirk == "dj")
                {
                    _bubbleText = "Трек доиграл, но вайб остался! 🎶";
                    _bubbleTime = 2.4;
                }
                else if (ActiveQuirk == "firefly")
                {
                    _bubbleText = "Пока, светлячок! 🦋✨";
                    _bubbleTime = 2.2;
                }
                else if (ActiveQuirk == "coffee")
                {
                    _bubbleText = "Батарея 100%! Полный заряд! 🔋⚡";
                    _bubbleTime = 2.5;
                }
                else if (ActiveQuirk == "peekaboo")
                {
                    _bubbleText = "Ку-ку! Я тебя вижу! 😋✨";
                    _bubbleTime = 2.4;
                }
                ActiveQuirk = "";
            }
        }
        else if (!IsSleeping && !IsThinking && !IsAsking && _mouseIdleSeconds < 16.0 && ScratchJoyTimer <= 0 && _bubbleTime <= 0)
        {
            _quirkInterval -= dt;
            if (_quirkInterval <= 0)
            {
                TriggerQuirk();
                _quirkInterval = 20.0 + Random.Shared.NextDouble() * 20.0; // каждые 20-40 сек
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _lastScratchMouse = e.GetPosition(this);
        if (IsSleeping)
        {
            _bubbleText = "Zzz... (кликни, чтобы разбудить)";
            _bubbleTime = 3.0;
            StartAnimating();
        }
        else
        {
            _spHappy.Target = 0.8;
            StartAnimating();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _scratchAccum = 0.0;
        _lastScratchMouse = new Point(-1, -1);
        if (_emotionTimer <= 0 && !IsSleeping) _spHappy.Target = 0.0;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = _painter;
        p.Dc = dc;
        p.Ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        p.Alpha = 1.0;

        double w = ActualWidth > 0 ? ActualWidth : 240;
        double h = ActualHeight > 0 ? ActualHeight : 220;

        double sleepW = Math.Clamp(_spSleepWeight.X, 0.0, 1.0);

        double cx = w / 2;
        double baseCy = h / 2 + 28;
        bool isMusic = _island.Media.Playing;
        double musicAwake = (!IsSleeping && sleepW < 0.85) ? (1.0 - sleepW) : 0.0;
        double musicBob = (isMusic && ActiveQuirk != "dj") ? Math.Abs(Math.Sin(_animPhase * 3.0)) * 3.0 * musicAwake : 0.0;
        double djBob = (ActiveQuirk == "dj") ? Math.Max(0.0, Math.Sin(_quirkPhase * 16.0)) * 3.5 : 0.0;
        double scratchPurr = ScratchJoyTimer > 0 ? Math.Sin(_animPhase * 35.0) * 1.5 : 0.0;
        double awakeFloat = Math.Sin(_animPhase) * 3.5 + scratchPurr;
        double sleepFloat = Math.Sin((IsSleeping ? _sleepAnimTimer : _animPhase * 0.5) * 1.5) * 2.0;
        double floatY = Ui.Lerp(awakeFloat, sleepFloat, sleepW) + _spHop.X + sleepW * 2.5;
        double cy = baseCy + floatY + djBob - musicBob;

        // 1. Всплывающее облачко речи (Speech Bubble) над головой Аэро
        if (_bubbleTime > 0 && !string.IsNullOrEmpty(_bubbleText))
        {
            double bubbleA = Ui.Clamp(_bubbleTime * 2.5);
            double maxBubbleW = w - 16;
            double singleW = p.TextWidth(_bubbleText, 11, true) + 26;
            bool wrapBubble = singleW > maxBubbleW; // длинный текст (например, ответ ИИ) — в несколько строк
            double bubbleW = wrapBubble ? maxBubbleW : singleW;
            double bubbleH = wrapBubble
                ? Math.Min(p.WrappedHeight(_bubbleText, 11, bubbleW - 20), BubbleMaxTextH) + 14
                : 32;
            double bubbleX = cx - bubbleW / 2;
            double bubbleY = Math.Max(2, cy - 44 - bubbleH);
            var bRect = new Rect(bubbleX, bubbleY, bubbleW, bubbleH);

            using (p.WithOpacity(bubbleA))
            {
                var bubbleBg = Theme.Mix(Theme.Surface2, Theme.Surface3, 0.96);
                p.Card(bRect, bubbleBg, wrapBubble ? 12 : 16, Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.7));

                // Хвостик облачка (через zero-alloc полигон)
                p.Polygon(bubbleBg, new Point(cx - 6, bRect.Bottom), new Point(cx, bRect.Bottom + 7), new Point(cx + 6, bRect.Bottom));

                if (wrapBubble)
                    p.Label(_bubbleText, new Rect(bRect.X + 10, bRect.Y + 7, bRect.Width - 20, bRect.Height - 14),
                            11, Theme.Text, false, HAlign.Center, VAlign.Top, true);
                else
                    p.Label(_bubbleText, bRect, 11, Theme.Text, true, HAlign.Center);
            }
        }

        // Подготовка поворота для сальто (360° Barrel Roll) или покачивания в такт музыке
        bool pushedRot = false;
        if (ActiveQuirk == "flip")
        {
            double angle = 0.0;
            if (_quirkPhase >= 0.35 && _quirkPhase < 1.75)
            {
                double tSpin = (_quirkPhase - 0.35) / 1.4;
                angle = tSpin * 360.0;
            }
            if (Math.Abs(angle) > 0.01)
            {
                _rotTransform.Angle = angle;
                _rotTransform.CenterX = cx;
                _rotTransform.CenterY = cy;
                dc.PushTransform(_rotTransform);
                pushedRot = true;
            }
        }
        else if (isMusic && musicAwake > 0.05)
        {
            double swayAngle = Math.Sin(_animPhase * 1.5) * 3.2 * musicAwake;
            if (Math.Abs(swayAngle) > 0.05)
            {
                _rotTransform.Angle = swayAngle;
                _rotTransform.CenterX = cx;
                _rotTransform.CenterY = cy;
                dc.PushTransform(_rotTransform);
                pushedRot = true;
            }
        }

        // 2. Джетпак-пламя / антигравитационное свечение снизу под чиби-тельцем
        double awakePulse = 6.5 + Math.Sin(_animPhase * 2.8) * 3.0;
        double sleepPulse = 4.5 + Math.Sin(_sleepAnimTimer * 1.5) * 1.5;
        double flamePulse = Ui.Lerp(awakePulse, sleepPulse, sleepW);
        Color awakeFlame = Color.FromArgb(190, 56, 189, 248);
        Color sleepFlame = Color.FromArgb(140, 100, 180, 255);
        Color flameColor = Theme.Mix(awakeFlame, sleepFlame, sleepW);

        if (ActiveQuirk == "flip" && _quirkPhase >= 0.35 && _quirkPhase < 1.75)
        {
            flamePulse *= 2.0;
            flameColor = Color.FromArgb(240, 250, 204, 21); // ярко-золотой турбо-всплеск
        }

        p.Circle(new Point(cx, cy + 44), flamePulse * 1.5, Color.FromArgb((byte)Ui.Lerp(45, 25, sleepW), 56, 189, 248));
        p.Circle(new Point(cx, cy + 42), flamePulse, flameColor);
        p.Circle(new Point(cx, cy + 40), flamePulse * 0.55, Colors.White);

        // 2.5. Умилительное пухлое чиби-тельце с реактором-сердечком в груди
        Color bodyBg = Theme.IsLight ? Color.FromRgb(242, 245, 252) : Color.FromRgb(26, 30, 42);
        Color bodyBorder = Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.45);
        var bodyRect = new Rect(cx - 19, cy + 21, 38, 20);
        p.Card(bodyRect, bodyBg, 10, bodyBorder);
        // Мягкий блик на животике
        p.Card(new Rect(bodyRect.Left + 6, bodyRect.Top + 2, bodyRect.Width - 12, 4), Color.FromArgb(40, 255, 255, 255), 2);
        // Светящееся сердечко-ядро на груди
        Color coreColor = (_spHappy.X > 0.25 || ScratchJoyTimer > 0 || _emotion == "love")
            ? Color.FromRgb(244, 114, 182)
            : Theme.Mix(Theme.Accent, Theme.AccentHot, (Math.Sin(_animPhase * 2.5) + 1.0) * 0.5);
        p.Label("♥", new Rect(cx - 7, bodyRect.Top + 2, 14, 15), 10.5, coreColor, true, HAlign.Center);

        // 3. Антеннка
        p.Line(new Point(cx, cy - 26), new Point(cx, cy - 38), Theme.BorderSubtle, 2.2);
        Color baseTipColor = ((_spHappy.X > 0.3 || _emotionTimer > 0) ? Theme.AccentHot : Theme.Accent);
        Color sleepTipColor = Theme.Mix(Theme.Accent, Color.FromRgb(140, 180, 255), 0.5);
        Color tipColor = Theme.Mix(baseTipColor, sleepTipColor, sleepW);
        if (IsThinking)
        {
            // Антенна переливается бирюзовым ↔ фиолетовым, пока идёт запрос к ИИ
            double tPulse = (Math.Sin(_animPhase * 3.0) + 1.0) * 0.5;
            tipColor = Theme.Mix(Color.FromRgb(56, 189, 248), Color.FromRgb(168, 85, 247), tPulse);
            p.Circle(new Point(cx, cy - 40), 9.0 + tPulse * 4.0, Color.FromArgb(40, tipColor.R, tipColor.G, tipColor.B));
        }
        p.Circle(new Point(cx, cy - 40), 4.5, tipColor);
        p.Circle(new Point(cx, cy - 40), 7.0, Color.FromArgb((byte)Ui.Lerp(60, 35, sleepW), tipColor.R, tipColor.G, tipColor.B));

        // Лазерное сканирование при квирке "scan"
        if (ActiveQuirk == "scan")
        {
            double sweep = Math.Sin(_quirkPhase * 3.8); // от -1 до +1
            double sweepX = cx + sweep * 52.0;
            double scanY = cy + 46.0;
            Point tipPt = new Point(cx, cy - 40);

            // Полупрозрачный голографический сектор сканера
            Color beamFill = Color.FromArgb(35, 56, 189, 248);
            p.Polygon(beamFill, tipPt, new Point(sweepX - 28, scanY), new Point(sweepX + 28, scanY));

            // Яркая неоновая лазерная линия на поверхности
            Color beamLine = Color.FromArgb(220, 100, 220, 255);
            p.Line(new Point(sweepX - 28, scanY), new Point(sweepX + 28, scanY), beamLine, 2.0);
            p.Circle(new Point(sweepX, scanY), 3.0, Colors.White);
        }

        // Парящие буковки Zzz при сне или погружении в сон
        if (IsSleeping || sleepW > 0.82)
        {
            double sleepT = IsSleeping ? _sleepAnimTimer : (_mouseIdleSeconds - 24.0);
            double partAlphaFactor = IsSleeping ? 1.0 : Math.Clamp((sleepW - 0.82) / 0.18, 0.0, 1.0);
            for (int i = 0; i < 3; i++)
            {
                double zPhase = (sleepT * 0.45 + i * 0.33) % 1.0;
                double zAlpha = Math.Sin(zPhase * Math.PI) * partAlphaFactor; // плавное появление и растворение
                double zX = cx + 14.0 + Math.Sin(zPhase * Math.PI * 2.0) * 8.0 + i * 5.0;
                double zY = cy - 42.0 - zPhase * 38.0;
                double zSize = 9.0 + i * 2.5;
                Color zColor = Color.FromArgb((byte)(zAlpha * 210), 160, 205, 255);
                p.Label("z", new Rect(zX - 10, zY - 10, 20, 20), zSize, zColor, true, HAlign.Center);
            }
        }

        // Парящее сердечко при эмоции Love
        if (_emotion == "love" && _emotionTimer > 0)
        {
            double heartUp = (1.0 - _emotionTimer / 3.0) * 20.0;
            double heartA = Ui.Clamp(_emotionTimer * 2.0);
            using (p.WithOpacity(heartA))
            {
                p.Label("♥", new Rect(cx - 14, cy - 64 - heartUp, 28, 28), 18, Theme.Red, true, HAlign.Center);
            }
        }

        // 4. Наушники / ушки по бокам
        Color earColor = Theme.Mix(Theme.Surface3, Theme.Accent, 0.5);
        if (isMusic || ActiveQuirk == "dj")
        {
            // Дужка студийных наушников поверх головы
            Color bandColor = Theme.Mix(Theme.Surface3, Theme.Accent, 0.75);
            p.Polyline(bandColor, 4.2,
                new Point(cx - 39, cy - 10),
                new Point(cx - 35, cy - 26),
                new Point(cx - 18, cy - 32),
                new Point(cx, cy - 33.5),
                new Point(cx + 18, cy - 32),
                new Point(cx + 35, cy - 26),
                new Point(cx + 39, cy - 10));
            p.Polyline(Theme.AccentHot, 1.6,
                new Point(cx - 34, cy - 25),
                new Point(cx - 18, cy - 31),
                new Point(cx, cy - 32.5),
                new Point(cx + 18, cy - 31),
                new Point(cx + 34, cy - 25));

            // Массивные светящиеся чашки наушников
            double ledPulse = musicAwake > 0.05 ? (Math.Sin(_animPhase * 6.0) + 1.0) * 0.5 : 0.35;
            Color cupBg = Color.FromRgb(22, 25, 36);
            Color cupRim = Theme.Mix(Theme.Accent, Theme.AccentHot, ledPulse);
            p.Card(new Rect(cx - 45, cy - 14, 11, 28), cupBg, 5.5, cupRim);
            p.Card(new Rect(cx + 34, cy - 14, 11, 28), cupBg, 5.5, cupRim);
            // Неоновые полоски на амбушюрах
            p.Card(new Rect(cx - 41.5, cy - 8, 3.0, 16), cupRim, 1.5);
            p.Card(new Rect(cx + 38.5, cy - 8, 3.0, 16), cupRim, 1.5);
        }
        else
        {
            p.Card(new Rect(cx - 42, cy - 12, 8, 24), earColor, 4);
            p.Card(new Rect(cx + 34, cy - 12, 8, 24), earColor, 4);
            p.Line(new Point(cx - 38, cy), new Point(cx - 34, cy), Theme.Accent, 2.0);
            p.Line(new Point(cx + 34, cy), new Point(cx + 38, cy), Theme.Accent, 2.0);
        }

        // DJ Эквалайзер и летающие нотки над наушниками
        if (ActiveQuirk == "dj")
        {
            for (int b = 0; b < 3; b++)
            {
                double bH = 4.0 + Math.Abs(Math.Sin(_quirkPhase * 18.0 + b * 1.4)) * 14.0;
                Color bCol = (b == 0) ? Theme.AccentHot : ((b == 1) ? Color.FromRgb(244, 63, 94) : Color.FromRgb(56, 189, 248));
                p.Card(new Rect(cx - 48 + b * 4.5, cy - 16 - bH, 3.5, bH), bCol, 1.5);
                p.Card(new Rect(cx + 36 + b * 4.5, cy - 16 - bH, 3.5, bH), bCol, 1.5);
            }

            for (int k = 0; k < 4; k++)
            {
                double nP = (_quirkPhase * 0.75 + k * 0.25) % 1.0;
                double nAlpha = Math.Sin(nP * Math.PI);
                double nX = cx + (k % 2 == 0 ? -48.0 : 48.0) + Math.Sin(nP * 6.0) * 12.0;
                double nY = cy - 20.0 - nP * 46.0;
                Color nCol = Color.FromArgb((byte)(nAlpha * 220), 236, 72, 153);
                p.Label((k % 2 == 0) ? "♪" : "♫", new Rect(nX - 10, nY - 10, 20, 20), 16, nCol, true, HAlign.Center);
            }
        }
        else if (isMusic && musicAwake > 0.05)
        {
            // Парящие музыкальные нотки ♪ ♫, когда играет системная музыка
            for (int k = 0; k < 3; k++)
            {
                double nP = (_animPhase * 0.22 + k * 0.33) % 1.0;
                double nAlpha = Math.Sin(nP * Math.PI) * musicAwake;
                double side = (k % 2 == 0) ? -46.0 : 46.0;
                double nX = cx + side + Math.Sin(nP * 5.5 + k) * 9.0;
                double nY = cy - 16.0 - nP * 38.0;
                Color baseCol = (k == 0) ? Theme.AccentHot : ((k == 1) ? Color.FromRgb(236, 72, 153) : Color.FromRgb(56, 189, 248));
                Color nCol = Color.FromArgb((byte)(nAlpha * 215), baseCol.R, baseCol.G, baseCol.B);
                p.Label((k % 2 == 0) ? "♪" : "♫", new Rect(nX - 10, nY - 10, 20, 20), 14.5, nCol, true, HAlign.Center);
            }
        }

        // 5. Корпус робота (гладкая белая премиальная керамика / титан)
        var headRect = new Rect(cx - 35, cy - 26, 70, 52);
        Color headBg = Theme.IsLight ? Color.FromRgb(246, 248, 254) : Color.FromRgb(28, 32, 44);
        Color headBorder = Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.5);
        p.Card(headRect, headBg, 22, headBorder);
        // Глянцевый стеклянный отблеск сверху головы
        p.Card(new Rect(headRect.Left + 10, headRect.Top + 2.5, headRect.Width - 20, 8.0), Color.FromArgb(55, 255, 255, 255), 4.0);

        // 6. Тёмный глянцевый OLED-визор
        var visorRect = new Rect(cx - 26, cy - 17, 52, 33);
        Color visorBg = Color.FromRgb(10, 12, 18);
        Color visorBorder = Color.FromArgb(60, 255, 255, 255);
        p.Card(visorRect, visorBg, 14, visorBorder);
        p.Line(new Point(visorRect.Left + 6, visorRect.Top + 2.5),
               new Point(visorRect.Right - 6, visorRect.Top + 2.5),
               Color.FromArgb(90, 255, 255, 255), 1.5);

        // Сияющая звёздочка после протирки визора салфеткой
        if (ActiveQuirk == "wipe" && _quirkPhase >= 2.5)
        {
            double sparkScale = Math.Sin((_quirkPhase - 2.5) / 1.1 * Math.PI);
            if (sparkScale > 0.01)
            {
                using (p.WithOpacity(sparkScale))
                {
                    p.Label("✧", new Rect(visorRect.Right - 12, visorRect.Top - 8, 20, 20), 16, Colors.White, true, HAlign.Center);
                }
            }
        }

        // Голографический индикатор батареи при питье байт-кофе
        if (ActiveQuirk == "coffee" && _quirkPhase >= 1.8)
        {
            double batAlpha = Math.Sin(Math.Clamp((_quirkPhase - 1.8) / 2.0, 0.0, 1.0) * Math.PI);
            using (p.WithOpacity(batAlpha))
            {
                var batRect = new Rect(cx - 24, cy - 60, 48, 15);
                p.Card(batRect, Color.FromArgb(220, 15, 23, 42), 4, Color.FromRgb(34, 197, 94));
                p.Card(new Rect(batRect.Left + 2, batRect.Top + 2, batRect.Width - 4, batRect.Height - 4), Color.FromRgb(34, 197, 94), 2);
                p.Card(new Rect(batRect.Right, batRect.Top + 4, 3, 7), Color.FromRgb(34, 197, 94), 1);
                p.Label("100% ⚡", batRect, 9, Colors.White, true, HAlign.Center);
            }
        }

        // Цифровой светлячок
        if (ActiveQuirk == "firefly")
        {
            double ffx, ffy;
            if (_quirkPhase < 2.2)
            {
                double theta = _quirkPhase * 3.2;
                ffx = cx + Math.Sin(theta) * 46.0;
                ffy = cy - 12.0 + Math.Cos(theta * 1.5) * 22.0;
            }
            else if (_quirkPhase < 3.4)
            {
                ffx = cx;
                ffy = cy - 42.0; // приземлился на антеннку
            }
            else
            {
                double flyUp = (_quirkPhase - 3.4) / 1.1;
                ffx = cx + Math.Sin(flyUp * 6.0) * 32.0;
                ffy = cy - 42.0 - flyUp * 65.0;
            }

            // Неоновое свечение светлячка
            double fGlow = 4.0 + Math.Sin(_animPhase * 8.0) * 1.5;
            p.Circle(new Point(ffx, ffy), fGlow * 2.2, Color.FromArgb(50, 56, 189, 248));
            p.Circle(new Point(ffx, ffy), fGlow * 1.2, Color.FromArgb(160, 250, 204, 21));
            p.Circle(new Point(ffx, ffy), 2.6, Colors.White);

            // Крошечные трепещущие крылышки
            double wingFlap = Math.Sin(_quirkPhase * 32.0) * 5.5;
            Color wingCol = Color.FromArgb(200, 250, 204, 21);
            p.Polygon(wingCol, new Point(ffx, ffy), new Point(ffx - wingFlap, ffy - 4.5), new Point(ffx - 1, ffy - 6));
            p.Polygon(wingCol, new Point(ffx, ffy), new Point(ffx + wingFlap, ffy - 4.5), new Point(ffx + 1, ffy - 6));

            // Искорки на антеннке во время посадки
            if (_quirkPhase >= 2.2 && _quirkPhase < 3.4)
            {
                for (int s = 0; s < 4; s++)
                {
                    double sAngle = _quirkPhase * 10.0 + s * 1.57;
                    double sDist = 7.0 + Math.Sin(_quirkPhase * 15.0 + s) * 3.0;
                    p.Circle(new Point(ffx + Math.Cos(sAngle) * sDist, ffy + Math.Sin(sAngle) * sDist), 1.5, Colors.White);
                }
            }
        }

        // Надувание голографического пузыря из жвачки
        if (ActiveQuirk == "bubble")
        {
            Point mouthPt = new Point(cx, visorRect.Bottom - 3);

            if (_quirkPhase < 2.5)
            {
                double grow = Ui.Clamp(_quirkPhase / 2.4);
                double bubbleR = 3.0 + grow * 16.0;
                Point bubbleCenter = new Point(cx, mouthPt.Y + bubbleR * 0.45);

                // Ротик, дующий пузырь
                p.Circle(mouthPt, 2.6, Theme.Mix(Theme.Surface3, Theme.Text, 0.4));

                // Голографический пузырь (неоново-розовый с голубым отблеском)
                p.Circle(bubbleCenter, bubbleR, Color.FromArgb(90, 244, 114, 182));
                dc.DrawEllipse(null, p.P(Color.FromArgb(200, 251, 168, 212), 1.6), bubbleCenter, bubbleR, bubbleR);
                // Стеклянный блик
                p.Circle(new Point(bubbleCenter.X - bubbleR * 0.35, bubbleCenter.Y - bubbleR * 0.35), bubbleR * 0.22, Color.FromArgb(210, 255, 255, 255));
            }
            else if (_quirkPhase >= 2.5 && _quirkPhase < 3.1)
            {
                // ЛОПНУЛ! (Хлоп) — капельки разлетаются радиально
                double popT = (_quirkPhase - 2.5) / 0.6;
                double popDist = 12.0 + popT * 18.0;
                byte popAlpha = (byte)((1.0 - popT) * 230);
                Color popColor = Color.FromArgb(popAlpha, 244, 114, 182);

                for (int i = 0; i < 6; i++)
                {
                    double ang = i * Math.PI / 3.0;
                    Point drop = new Point(cx + Math.Cos(ang) * popDist, mouthPt.Y + Math.Sin(ang) * popDist);
                    p.Circle(drop, 2.0, popColor);
                }
            }
        }

        // 7. Глазки и мимика
        double ex = _spEyeX.X;
        double ey = _spEyeY.X;
        double blink = _blinkState;
        double happy = _spHappy.X;
        Color eyeColor = (happy > 0.3 || _emotionTimer > 0) ? Theme.AccentHot : Theme.Mix(Theme.Accent, Theme.AccentHot, 0.75);

        double lx = cx - 12.0 + ex;
        double rx = cx + 12.0 + ex;
        double ly = cy - 0.5 + ey;
        double ry = ly;

        if (ScratchJoyTimer > 0)
        {
            // Жмурится от удовольствия: сердечки в глазах + розовые щечки
            p.Label("♥", new Rect(lx - 10, ly - 12, 20, 24), 20, Theme.Red, true, HAlign.Center);
            p.Label("♥", new Rect(rx - 10, ry - 12, 20, 24), 20, Theme.Red, true, HAlign.Center);
            p.Circle(new Point(lx, ly + 9.0), 3.5, Color.FromArgb(220, 255, 120, 160));
            p.Circle(new Point(rx, ry + 9.0), 3.5, Color.FromArgb(220, 255, 120, 160));
        }
        else if (IsThinking)
        {
            // Сосредоточенные глазки во время размышления (без бликов — «вычисляет»)
            p.Card(new Rect(lx - 4.5, ly - 5.0, 9.0, 10.0), eyeColor, 4.5);
            p.Card(new Rect(rx - 4.5, ry - 5.0, 9.0, 10.0), eyeColor, 4.5);
        }
        else if (ActiveQuirk == "wipe")
        {
            if (_quirkPhase < 2.5)
            {
                // Прищуренный глаз при протирке (> _ <)
                p.Polyline(eyeColor, 3.2, new Point(lx - 6, ly - 3), new Point(lx, ly), new Point(lx - 6, ly + 3));
                p.Polyline(eyeColor, 3.2, new Point(rx + 6, ry - 3), new Point(rx, ry), new Point(rx + 6, ry + 3));
            }
            else
            {
                // Гордый сверкающий взгляд (★ ★)
                p.Label("★", new Rect(lx - 10, ly - 13, 20, 24), 21, Theme.Yellow, true, HAlign.Center);
                p.Label("★", new Rect(rx - 10, ry - 13, 20, 24), 21, Theme.Yellow, true, HAlign.Center);
            }
        }
        else if (ActiveQuirk == "scan")
        {
            // Сосредоточенный кибер-скан с бегающей черточкой
            p.Line(new Point(visorRect.Left + 8, ly), new Point(visorRect.Right - 8, ly), Color.FromArgb(160, 56, 189, 248), 2.0);
            p.Circle(new Point(lx, ly), 4.0, Theme.AccentHot);
            p.Circle(new Point(rx, ry), 4.0, Theme.AccentHot);
        }
        else if (ActiveQuirk == "bubble")
        {
            if (_quirkPhase < 2.5)
            {
                // Сосредоточенные глазки-точки при надувании
                p.Circle(new Point(lx, ly), 3.5, eyeColor);
                p.Circle(new Point(rx, ry), 3.5, eyeColor);
                p.Circle(new Point(lx, ly + 8.5), 2.5, Color.FromArgb(150, 255, 120, 160));
                p.Circle(new Point(rx, ry + 8.5), 2.5, Color.FromArgb(150, 255, 120, 160));
            }
            else
            {
                // Удивлённые круглые глаза при хлопке (O _ O)
                dc.DrawEllipse(null, p.P(Theme.AccentHot, 2.6), new Point(lx, ly), 7.5, 7.5);
                dc.DrawEllipse(null, p.P(Theme.AccentHot, 2.6), new Point(rx, ry), 7.5, 7.5);
                p.Circle(new Point(lx, ly), 3.0, Theme.Text);
                p.Circle(new Point(rx, ry), 3.0, Theme.Text);
                p.Circle(new Point(lx, ly + 8.5), 3.0, Color.FromArgb(190, 255, 120, 160));
                p.Circle(new Point(rx, ry + 8.5), 3.0, Color.FromArgb(190, 255, 120, 160));
            }
        }
        else if (ActiveQuirk == "flip")
        {
            // Радостные глазки-дуги при кувырке
            p.Polyline(eyeColor, 3.2, new Point(lx - 7.0, ly + 2), new Point(lx, ly - 5.0), new Point(lx + 7.0, ly + 2));
            p.Polyline(eyeColor, 3.2, new Point(rx - 7.0, ry + 2), new Point(rx, ry - 5.0), new Point(rx + 7.0, ry + 2));
            p.Circle(new Point(lx, ly + 8.0), 3.0, Color.FromArgb(180, 255, 120, 160));
            p.Circle(new Point(rx, ry + 8.0), 3.0, Color.FromArgb(180, 255, 120, 160));
        }
        else if (ActiveQuirk == "dj")
        {
            // Стильные диджейские кибер-очки на визоре
            var lLens = new Rect(cx - 22, cy - 13, 20, 14);
            var rLens = new Rect(cx + 2, cy - 13, 20, 14);
            p.Card(lLens, Color.FromRgb(15, 15, 22), 3.0, Theme.AccentHot);
            p.Card(rLens, Color.FromRgb(15, 15, 22), 3.0, Theme.AccentHot);
            p.Line(new Point(cx - 2, cy - 6), new Point(cx + 2, cy - 6), Theme.AccentHot, 2.0);
            p.Line(new Point(lLens.Left + 3, lLens.Top + 2), new Point(lLens.Left + 8, lLens.Bottom - 2), Colors.White, 1.5);
            p.Line(new Point(rLens.Left + 3, rLens.Top + 2), new Point(rLens.Left + 8, rLens.Bottom - 2), Colors.White, 1.5);
            p.Circle(new Point(lx, ly + 8.5), 3.0, Color.FromArgb(180, 255, 120, 160));
            p.Circle(new Point(rx, ry + 8.5), 3.0, Color.FromArgb(180, 255, 120, 160));
        }
        else if (ActiveQuirk == "firefly" && _quirkPhase >= 2.2 && _quirkPhase < 3.4)
        {
            // Сведённые к центру глазки, смотрящие вверх на антеннку (@ _ @)
            p.Polyline(eyeColor, 3.0, new Point(lx - 5, ly - 3), new Point(lx + 2, ly - 5), new Point(lx + 4, ly));
            p.Polyline(eyeColor, 3.0, new Point(rx + 5, ry - 3), new Point(rx - 2, ry - 5), new Point(rx - 4, ry));
            p.Circle(new Point(lx + 1.5, ly - 2.5), 2.2, Theme.Text);
            p.Circle(new Point(rx - 1.5, ry - 2.5), 2.2, Theme.Text);
            p.Circle(new Point(lx, ly + 8.5), 3.0, Color.FromArgb(180, 255, 120, 160));
            p.Circle(new Point(rx, ry + 8.5), 3.0, Color.FromArgb(180, 255, 120, 160));
        }
        else if (ActiveQuirk == "coffee" && _quirkPhase > 0.8 && _quirkPhase < 2.8)
        {
            // Жмурится от удовольствия при глотке кофе (^ ‿ ^)
            p.Polyline(eyeColor, 3.2, new Point(lx - 7.0, ly + 2), new Point(lx, ly - 5.0), new Point(lx + 7.0, ly + 2));
            p.Polyline(eyeColor, 3.2, new Point(rx - 7.0, ry + 2), new Point(rx, ry - 5.0), new Point(rx + 7.0, ry + 2));
            p.Circle(new Point(lx, ly + 8.5), 3.2, Color.FromArgb(200, 255, 120, 160));
            p.Circle(new Point(rx, ry + 8.5), 3.2, Color.FromArgb(200, 255, 120, 160));
        }
        else if (ActiveQuirk == "peekaboo" && _quirkPhase >= 1.8)
        {
            // Сияющие звёзды при раскрытии пряток (★ ★)
            p.Label("★", new Rect(lx - 10, ly - 13, 20, 24), 21, Theme.Yellow, true, HAlign.Center);
            p.Label("★", new Rect(rx - 10, ry - 13, 20, 24), 21, Theme.Yellow, true, HAlign.Center);
            p.Circle(new Point(lx, ly + 8.0), 3.2, Color.FromArgb(200, 255, 180, 80));
            p.Circle(new Point(rx, ry + 8.0), 3.2, Color.FromArgb(200, 255, 180, 80));
        }
        else if (_emotion == "love" && _emotionTimer > 0)
        {
            p.Label("♥", new Rect(lx - 10, ly - 12, 20, 24), 20, Theme.Red, true, HAlign.Center);
            p.Label("♥", new Rect(rx - 10, ry - 12, 20, 24), 20, Theme.Red, true, HAlign.Center);
            p.Circle(new Point(lx, ly + 9.0), 3.0, Color.FromArgb(200, 255, 120, 160));
            p.Circle(new Point(rx, ry + 9.0), 3.0, Color.FromArgb(200, 255, 120, 160));
        }
        else if (_emotion == "wink" && _emotionTimer > 0)
        {
            p.Polyline(eyeColor, 3.2, new Point(lx - 7.0, ly + 2), new Point(lx, ly - 5.0), new Point(lx + 7.0, ly + 2));
            p.Label("★", new Rect(rx - 10, ry - 13, 20, 24), 20, Theme.Yellow, true, HAlign.Center);
            p.Circle(new Point(lx, ly + 8.0), 3.0, Color.FromArgb(180, 255, 120, 160));
        }
        else if (_emotion == "stars" && _emotionTimer > 0)
        {
            p.Label("★", new Rect(lx - 10, ly - 13, 20, 24), 21, Theme.Yellow, true, HAlign.Center);
            p.Label("★", new Rect(rx - 10, ry - 13, 20, 24), 21, Theme.Yellow, true, HAlign.Center);
            p.Circle(new Point(lx, ly + 8.0), 3.0, Color.FromArgb(180, 255, 200, 80));
            p.Circle(new Point(rx, ry + 8.0), 3.0, Color.FromArgb(180, 255, 200, 80));
        }
        else if (_emotion == "cool" && _emotionTimer > 0)
        {
            var leftLens = new Rect(cx - 22, cy - 13, 19, 15);
            var rightLens = new Rect(cx + 3, cy - 13, 19, 15);
            p.Card(leftLens, Color.FromRgb(15, 15, 20), 4, Theme.AccentHot);
            p.Card(rightLens, Color.FromRgb(15, 15, 20), 4, Theme.AccentHot);
            p.Line(new Point(cx - 3, cy - 6), new Point(cx + 3, cy - 6), Theme.AccentHot, 2.5);
            p.Line(new Point(leftLens.Left + 3, leftLens.Top + 3), new Point(leftLens.Left + 8, leftLens.Bottom - 3), Colors.White, 1.6);
            p.Line(new Point(rightLens.Left + 3, rightLens.Top + 3), new Point(rightLens.Left + 8, rightLens.Bottom - 3), Colors.White, 1.6);
        }
        else if (_emotion == "surprised" && _emotionTimer > 0)
        {
            dc.DrawEllipse(null, p.P(Theme.AccentHot, 2.6), new Point(lx, ly), 8.0, 8.0);
            dc.DrawEllipse(null, p.P(Theme.AccentHot, 2.6), new Point(rx, ry), 8.0, 8.0);
            p.Circle(new Point(lx, ly), 3.2, Theme.Text);
            p.Circle(new Point(rx, ry), 3.2, Theme.Text);
        }
        else if (_emotion == "yawn" && _emotionTimer > 0)
        {
            // Сонный милый зевок перед сном: глазки-щелочки > < и открытый круглый ротик
            p.Polyline(eyeColor, 2.8, new Point(lx - 6.5, ly - 2.5), new Point(lx, ly + 2.0), new Point(lx + 6.5, ly - 2.5));
            p.Polyline(eyeColor, 2.8, new Point(rx - 6.5, ry - 2.5), new Point(rx, ry + 2.0), new Point(rx + 6.5, ry - 2.5));
            dc.DrawEllipse(null, p.P(eyeColor, 1.8), new Point(cx, cy + 4.0), 3.5, 4.5);
            p.Circle(new Point(lx, ly + 8.5), 3.2, Color.FromArgb(160, 255, 130, 170));
            p.Circle(new Point(rx, ry + 8.5), 3.2, Color.FromArgb(160, 255, 130, 170));
        }
        else if (IsSleeping || sleepW >= 0.88)
        {
            // Закрытые спящие глаза-дуги (мирно спит)
            Color sleepEyeColor = Theme.Mix(eyeColor, Color.FromRgb(140, 170, 235), 0.7);
            p.Polyline(sleepEyeColor, 3.2, new Point(lx - 7.0, ly - 1.5), new Point(lx, ly + 4.5), new Point(lx + 7.0, ly - 1.5));
            p.Polyline(sleepEyeColor, 3.2, new Point(rx - 7.0, ry - 1.5), new Point(rx, ry + 4.5), new Point(rx + 7.0, ry - 1.5));
            // Мягкий сонный румянец на щечках
            p.Circle(new Point(lx, ly + 9.0), 3.2, Color.FromArgb(140, 255, 140, 180));
            p.Circle(new Point(rx, ry + 9.0), 3.2, Color.FromArgb(140, 255, 140, 180));
        }
        else if (happy > 0.3)
        {
            p.Polyline(eyeColor, 3.2, new Point(lx - 7.0, ly + 2), new Point(lx, ly - 5.0), new Point(lx + 7.0, ly + 2));
            p.Polyline(eyeColor, 3.2, new Point(rx - 7.0, ry + 2), new Point(rx, ry - 5.0), new Point(rx + 7.0, ry + 2));
            p.Circle(new Point(lx - 2.0, ly + 8.0), 3.6, Color.FromArgb(195, 255, 120, 165));
            p.Circle(new Point(rx + 2.0, ry + 8.0), 3.6, Color.FromArgb(195, 255, 120, 165));
            // Милая кошачья улыбочка ω
            p.Polyline(eyeColor, 2.0,
                new Point(cx - 4.5, ly + 4.5),
                new Point(cx - 2.2, ly + 6.8),
                new Point(cx, ly + 4.8),
                new Point(cx + 2.2, ly + 6.8),
                new Point(cx + 4.5, ly + 4.5));
        }
        else
        {
            // Крупные умилительные чиби-глазки с двойными аниме-бликами и нежным румянцем
            double eyeH = Math.Max(2.4, 13.5 * (1.0 - sleepW * 0.82));
            double eyeYOff = sleepW * 2.8;

            // Тяжёлое сонное микро-моргание
            double drowsyBlink = 0.0;
            if (sleepW > 0.25)
            {
                double nodCycle = Math.Sin(_animPhase * 0.8);
                if (nodCycle > 0.55)
                    drowsyBlink = (nodCycle - 0.55) / 0.45;
            }
            double currentBlink = Math.Max(blink, drowsyBlink);

            if (currentBlink > 0.25)
            {
                p.Polyline(eyeColor, 3.0, new Point(lx - 6.5, ly + eyeYOff - 1.0), new Point(lx, ly + eyeYOff + 2.0), new Point(lx + 6.5, ly + eyeYOff - 1.0));
                p.Polyline(eyeColor, 3.0, new Point(rx - 6.5, ry + eyeYOff - 1.0), new Point(rx, ry + eyeYOff + 2.0), new Point(rx + 6.5, ry + eyeYOff - 1.0));
            }
            else
            {
                p.Card(new Rect(lx - 5.5, ly - eyeH / 2.0 + eyeYOff, 11.0, eyeH), eyeColor, Math.Min(5.5, eyeH / 2.0));
                p.Card(new Rect(rx - 5.5, ry - eyeH / 2.0 + eyeYOff, 11.0, eyeH), eyeColor, Math.Min(5.5, eyeH / 2.0));

                // Двойные аниме-блики в глазках (большой сверху-слева + маленький снизу-справа)
                if (eyeH > 5.2)
                {
                    double glintAlpha = Math.Clamp((eyeH - 5.2) / 6.8, 0.0, 1.0);
                    Color glintColor = Color.FromArgb((byte)(glintAlpha * 255), 255, 255, 255);
                    Color subGlint = Color.FromArgb((byte)(glintAlpha * 200), 255, 255, 255);
                    p.Circle(new Point(lx - 1.8, ly - 3.2 + eyeYOff), 2.3, glintColor);
                    p.Circle(new Point(rx - 1.8, ry - 3.2 + eyeYOff), 2.3, glintColor);
                    p.Circle(new Point(lx + 2.0, ly + 2.4 + eyeYOff), 1.1, subGlint);
                    p.Circle(new Point(rx + 2.0, ry + 2.4 + eyeYOff), 1.1, subGlint);
                }
            }

            // Постоянный нежный румянец на щёчках
            byte blushA = (byte)Ui.Lerp(115, 165, Math.Max(sleepW, isMusic ? 0.8 : 0.0));
            p.Circle(new Point(lx - 2.5, ly + 8.5), 3.3, Color.FromArgb(blushA, 255, 130, 175));
            p.Circle(new Point(rx + 2.5, ry + 8.5), 3.3, Color.FromArgb(blushA, 255, 130, 175));

            // Маленькая дружелюбная улыбочка посередине визора
            if (sleepW < 0.5 && ActiveQuirk != "bubble")
            {
                p.Polyline(eyeColor, 1.8,
                    new Point(cx - 3.0, ly + 5.0 + ey * 0.4),
                    new Point(cx, ly + 6.8 + ey * 0.4),
                    new Point(cx + 3.0, ly + 5.0 + ey * 0.4));
            }
        }

        // 8. Парящие пухлые лапки-варежки с розовыми подушечками (без палок!)
        Color pawBg = headBg;
        Color pawBorder = Theme.Mix(headBorder, Theme.AccentHot, 0.35);
        Color padCol = Color.FromArgb(215, 244, 114, 182);

        static void DrawCutePaw(Painter p, double px, double py, double w, double h, Color bg, Color border, Color pad, bool showPad = true)
        {
            var r = new Rect(px - w / 2.0, py - h / 2.0, w, h);
            p.Card(r, bg, Math.Min(w, h) / 2.0, border);
            // Глянцевый блик сверху лапки
            p.Circle(new Point(px - w * 0.16, py - h * 0.2), Math.Min(w, h) * 0.14, Color.FromArgb(140, 255, 255, 255));
            if (showPad)
            {
                p.Circle(new Point(px, py + h * 0.1), Math.Min(w, h) * 0.2, pad);
            }
        }

        if (ActiveQuirk == "wipe")
        {
            // Левая пухлая лапка умилительно прижата у щёчки
            DrawCutePaw(p, cx - 32, cy + 16, 13, 15, pawBg, pawBorder, padCol);

            // Правая пухлая лапка протирает визор жёлтой салфеткой
            double wipeSweep = Math.Sin(_quirkPhase * 8.5) * 16.0;
            double clothX = cx + wipeSweep;
            double clothY = cy - 2.0 + Math.Cos(_quirkPhase * 8.5) * 3.0;

            // Жёлтая салфетка из микрофибры + пухлая лапка поверх неё
            p.Card(new Rect(clothX - 9, clothY - 6, 18, 12), Color.FromRgb(254, 215, 102), 3.5, Color.FromRgb(245, 158, 11));
            p.Line(new Point(clothX - 5, clothY - 1), new Point(clothX + 5, clothY - 1), Color.FromRgb(245, 158, 11), 1.2);
            DrawCutePaw(p, clothX + 3, clothY + 3, 13, 13, pawBg, pawBorder, padCol);
        }
        else if (ActiveQuirk == "flip")
        {
            // Радостно раскинутые в полёте пухлые лапки
            DrawCutePaw(p, cx - 44, cy + 10, 15, 13, pawBg, pawBorder, padCol);
            DrawCutePaw(p, cx + 44, cy + 10, 15, 13, pawBg, pawBorder, padCol);
        }
        else if (ActiveQuirk == "dj")
        {
            // Пухлые лапки диджея качают бит
            double djArmL = Math.Sin(_quirkPhase * 18.0) * 5.0;
            double djArmR = Math.Cos(_quirkPhase * 18.0) * 5.0;
            DrawCutePaw(p, cx - 34, cy + 22 + djArmL, 14, 14, pawBg, pawBorder, padCol);
            DrawCutePaw(p, cx + 34, cy + 22 + djArmR, 14, 14, pawBg, pawBorder, padCol);
        }
        else if (ActiveQuirk == "coffee")
        {
            // Левая лапка радостно приподнята
            DrawCutePaw(p, cx - 33, cy + 22, 13, 15, pawBg, pawBorder, padCol);

            // Правая лапка держит мини-кружку с байт-кофе
            double sipLift = (_quirkPhase > 0.8 && _quirkPhase < 2.8) ? 1.0 : (_quirkPhase <= 0.8 ? _quirkPhase / 0.8 : (4.0 - _quirkPhase) / 1.2);
            sipLift = Math.Clamp(sipLift, 0.0, 1.0);
            double mugX = cx + 18.0 - sipLift * 8.0;
            double mugY = cy + 12.0 - sipLift * 15.0;

            var mugRect = new Rect(mugX - 6, mugY, 13, 14);
            p.Card(mugRect, Color.FromRgb(20, 24, 34), 3.0, Theme.Accent);
            p.Polyline(Theme.Accent, 1.8, new Point(mugRect.Right, mugRect.Top + 3), new Point(mugRect.Right + 4, mugRect.Top + 7), new Point(mugRect.Right, mugRect.Bottom - 3));
            p.Label("⚡", new Rect(mugRect.Left - 2, mugRect.Top + 1, mugRect.Width + 4, mugRect.Height - 2), 9, Theme.Yellow, true, HAlign.Center);

            DrawCutePaw(p, mugX - 6, mugY + 8, 12, 12, pawBg, pawBorder, padCol, false);

            // Дымок/пар над кружкой
            for (int st = 0; st < 2; st++)
            {
                double stPhase = (_quirkPhase * 1.5 + st * 0.5) % 1.0;
                double stX = mugX + (st == 0 ? -2 : 2) + Math.Sin(stPhase * Math.PI * 2.0) * 2.5;
                double stY = mugRect.Top - 3 - stPhase * 14.0;
                double stA = Math.Sin(stPhase * Math.PI);
                p.Circle(new Point(stX, stY), 1.5 + stPhase * 1.2, Color.FromArgb((byte)(stA * 160), 200, 220, 255));
            }
        }
        else if (ActiveQuirk == "peekaboo")
        {
            if (_quirkPhase < 1.8)
            {
                // Пухлые лапки закрывают глазки на визоре (прячется)
                DrawCutePaw(p, cx - 11, cy - 1, 15, 16, pawBg, pawBorder, padCol);
                DrawCutePaw(p, cx + 11, cy - 1, 15, 16, pawBg, pawBorder, padCol);
            }
            else
            {
                // Распахнутые в восторге лапки (Ку-ку!)
                DrawCutePaw(p, cx - 42, cy - 2, 14, 15, pawBg, pawBorder, padCol);
                DrawCutePaw(p, cx + 42, cy - 2, 14, 15, pawBg, pawBorder, padCol);
            }
        }
        else
        {
            // Естественное парение пухлых лапок рядом с чиби-тельцем
            double pawBob = IsSleeping ? 0.0 : (isMusic ? Math.Sin(_animPhase * 6.0) * 3.5 : Math.Sin(_animPhase * 1.5 - 0.6) * 2.8);
            double sleepTuckX = sleepW * 5.0; // во сне уютно поджимает лапки ближе к животику
            double sleepTuckY = sleepW * 2.5;
            bool showPads = happy > 0.2 || ScratchJoyTimer > 0 || isMusic;
            DrawCutePaw(p, cx - 31 + sleepTuckX, cy + 27 + pawBob + sleepTuckY, 12.5, 15.0, pawBg, pawBorder, padCol, showPads);
            DrawCutePaw(p, cx + 31 - sleepTuckX, cy + 27 - pawBob + sleepTuckY, 12.5, 15.0, pawBg, pawBorder, padCol, showPads);
        }

        if (pushedRot)
        {
            dc.Pop();
        }
    }
}

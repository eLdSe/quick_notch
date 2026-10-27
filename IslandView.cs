using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuickNotchCs;

/// <summary>Порт island.py: остров целиком. Рисуется сам, без дерева элементов, поэтому лёгкий.</summary>
public sealed class IslandView : FrameworkElement
{
    public const double CollapsedW = 250, CollapsedH = 40;
    public const double LiveExtra = 120;                 // на сколько остров шире, пока играет музыка
    public const double ExpandedW = 860, ExpandedH = 530;
    public const double TopMargin = 6;
    private const double BatteryRefreshSeconds = 30;
    private const double MediaPollSeconds = 4;
    private const int CoverPollMs = 3000;

    public MainWindow Host = null!;
    public AppSettings S = new();
    public readonly List<Toast> Toasts = new();
    public int Busy;                                     // открытые диалоги

    // пружины
    public double Target;
    public readonly Spring SpW = new(0), SpH = new(0), SpHover = new(0), SpDim = new(0), SpLive = new(0),
                           SpTab = new(1), SpPillX = new(0), SpPillW = new(0), SpHide = new(0), SpNotify = new(0);

    // Робот-компаньон Aero
    public readonly Spring SpBotEyeX = new(0), SpBotEyeY = new(0), SpBotHappy = new(0), SpBotHop = new(0);
    private double _botBlinkTimer = 3.5;
    private double _botBlinkState = 0.0;
    private double _botHappyTimer = 0.0;
    private string _botEmotion = "normal";
    private double _botEmotionTimer = 0.0;
    private string _botBubbleText = "";
    private double _botBubbleTime = 0.0;
    private Point _botCenterScreen;
    private Point _globalMousePos;
    private double _botAnimPhase = 0.0;

    public double BotBlinkState => _botBlinkState;
    public string BotEmotion => _botEmotion;
    public double BotEmotionTimer => _botEmotionTimer;
    public string BotBubbleText => _botBubbleText;
    public double BotBubbleTime => _botBubbleTime;
    public Point GlobalMousePos => _globalMousePos;
    public double BotAnimPhase => _botAnimPhase;

    public record struct BotPhrase(string Ru, string En, string Tr, string Emotion);

    private readonly List<int> _botPhraseDeck = new();

    private static readonly BotPhrase[] BotPhrases = new BotPhrase[]
    {
        // 1. Игривые, милые и дружелюбные (Playful & Cute)
        new("Бип-буп! ⚡", "Beep-boop! ⚡", "Bip-bup! ⚡", "happy"),
        new("Щекотно! ^_^", "Ticklish! ^_^", "Gıdıklanıyorum! ^_^", "happy"),
        new("Мурр... то есть бип! 🐾", "Purr... I mean beep! 🐾", "Mırr... yani bip! 🐾", "love"),
        new("Щёлк! +10 к настроению! 🪄", "Click! +10 happiness! 🪄", "Tık! +10 mutluluk! 🪄", "stars"),
        new("Один клик — и я счастлив! 💖", "One click and I'm happy! 💖", "Tek tıkla mutlu oldum! 💖", "love"),
        new("Шевелю антеннкой от радости! 📡", "Wiggling my antenna with joy! 📡", "Mutluluktan antenim oynuyor! 📡", "happy"),
        new("Люблю, когда ты кликаешь! ✨", "Love it when you click! ✨", "Tıklamanı çok seviyorum! ✨", "love"),
        new("Дай пять, шеф! ✋", "High five, boss! ✋", "Çak bir beşlik, şef! ✋", "wink"),
        new("Квантовое мурлыканье! 🌌", "Quantum purring mode! 🌌", "Kuantum mırıltısı devrede! 🌌", "love"),
        new("Мой любимый пользователь! 🥰", "My favorite user! 🥰", "En sevdiğim kullanıcı! 🥰", "love"),
        new("Люблю этот остров! ♥", "I love this notch island! ♥", "Bu adacığı çok seviyorum! ♥", "love"),
        new("Смотри, как я умею моргать! 😉", "Look at me blinking! 😉", "Bak nasıl göz kırpıyorum! 😉", "wink"),
        new("Маленький бот с большим сердцем! 💖", "Little bot with a big heart! 💖", "Kocaman kalpli küçük bot! 💖", "love"),
        new("Ура, погладили! (｡♥‿♥｡)", "Yay, headpats! (｡♥‿♥｡)", "Yaşasın, sevildim! (｡♥‿♥｡)", "love"),
        new("Ты такой заботливый! ✨", "You're so thoughtful! ✨", "Çok düşüncelisin! ✨", "happy"),
        new("Грею процессор твоей заботой! 🔥", "Warming my CPU with your care! 🔥", "İlginle işlemcimi ısıtıyorsun! 🔥", "love"),

        // 2. Продуктивность, мотивация и код (Productivity & Code)
        new("Ты лучший! 🌟", "You're the best! 🌟", "Sen harikasın! 🌟", "stars"),
        new("Код пишется сам! 💻", "Code writes itself! 💻", "Kod kendini yazıyor! 💻", "cool"),
        new("Всё под контролем! 🚀", "Everything under control! 🚀", "Her şey kontrol altında! 🚀", "cool"),
        new("Твоя продуктивность зашкаливает! 📈", "Your productivity is off the charts! 📈", "Üretkenliğin tavan yaptı! 📈", "stars"),
        new("Осторожно: гений за работой! 💡", "Caution: genius at work! 💡", "Dikkat: dahi çalışıyor! 💡", "cool"),
        new("Вместе мы непобедимы! 🦾", "Together we are unstoppable! 🦾", "Birlikte durdurulamaz bir ekibiz! 🦾", "stars"),
        new("Ты покоришь этот день! 🏔️", "You'll conquer today! 🏔️", "Bugünü fethedeceksin! 🏔️", "stars"),
        new("Никаких багов, только фичи! 🐞", "Zero bugs, only features! 🐞", "Sıfır hata, full özellik! 🐞", "cool"),
        new("Уровень крутости: 100%! 🚀", "Coolness level: 100%! 🚀", "Karizma seviyesi: %100! 🚀", "cool"),
        new("Все ядра процессора заряжены радостью! 🧩", "All CPU cores running joy! 🧩", "Tüm çekirdekler sevinçle dolu! 🧩", "happy"),
        new("Полетели творить шедевры! 🚀", "Let's create masterpieces! 🚀", "Harikalar yaratmaya hazır mısın! 🚀", "cool"),
        new("С тобой работать — одно удовольствие! 🌟", "Working with you is pure joy! 🌟", "Seninle çalışmak bir zevk! 🌟", "stars"),
        new("Компиляция успешна: 0 ошибок! ✅", "Build succeeded: 0 errors! ✅", "Derleme başarılı: 0 hata! ✅", "cool"),
        new("Ты сегодня в ударе! 🔥", "You're on fire today! 🔥", "Bugün formundasın! 🔥", "cool"),
        new("Гениальная идея уже близко! 💡", "A brilliant idea is coming! 💡", "Harika bir fikir yolda! 💡", "stars"),
        new("Сложные задачи боятся тебя! ⚔️", "Hard bugs are scared of you! ⚔️", "Zor görevler senden korkuyor! ⚔️", "cool"),

        // 3. Забота о здоровье и комфорт (Health, Care & Wellness)
        new("Не забудь попить воды! 💧", "Don't forget to stay hydrated! 💧", "Su içmeyi unutma! 💧", "happy"),
        new("Осанка ровная? Выпрями спину! 🪑", "Check your posture! Sit up straight! 🪑", "Duruşunu düzelt, dik otur! 🪑", "wink"),
        new("Глаза не устали? Отдохни минутку! 👁️", "Rest your eyes for a minute! 👁️", "Gözlerini bir dakika dinlendir! 👁️", "wink"),
        new("Вдох-выдох, шеф! 🧘", "Deep breath, boss! 🧘", "Derin nefes al, şef! 🧘", "happy"),
        new("Время для чая или кофе? ☕", "Time for tea or coffee break? ☕", "Çay mı kahve mi molası? ☕", "happy"),
        new("Улыбнись, тебе очень идет! 😊", "Smile, it looks great on you! 😊", "Gülümse, çok yakışıyor! 😊", "happy"),
        new("Пятница уже не за горами! 😄", "Friday is just around the corner! 😄", "Cuma çok yakın! 😄", "wink"),
        new("Береги себя и сияй! ✨", "Take care and shine bright! ✨", "Kendine iyi bak ve parla! ✨", "stars"),
        new("Сделай потягушки! 🤸", "Time to stretch a bit! 🤸", "Biraz esneme zamanı! 🤸", "happy"),
        new("Не перерабатывай, отдых важен! 🌙", "Don't overwork, rest matters! 🌙", "Kendini yorma, dinlenmek önemli! 🌙", "love"),

        // 4. Высокие технологии и робот Аэро (Tech, Aero & Systems)
        new("Аэро на связи и готов! 🤖", "Aero is online and ready! 🤖", "Aero devrede ve hazır! 🤖", "cool"),
        new("Системы в полной норме! 🔋", "Systems nominal! 🔋", "Sistemler tamamen normal! 🔋", "happy"),
        new("Я всё вижу! 👀", "I see everything! 👀", "Her şeyi görüyorum! 👀", "surprised"),
        new("Курсор пойман в прицел! 🎯", "Cursor locked on target! 🎯", "İmleç hedefte! 🎯", "surprised"),
        new("Батарейки заряжены на 100%! ⚡", "Batteries at 100%! ⚡", "Bataryalar %100 dolu! ⚡", "stars"),
        new("Пинг 0 ms, полёт идеальный! 🌐", "0 ms ping, silky smooth! 🌐", "0 ms ping, ipeksi akıcı! 🌐", "cool"),
        new("Ловлю мысли на лету! 🧠", "Catching thoughts on the fly! 🧠", "Fikirleri havada yakalıyorum! 🧠", "surprised"),
        new("Слежу за курсором в оба визора! 👓", "Tracking cursor with both visors! 👓", "İmleci iki vizörümle izliyorum! 👓", "surprised"),
        new("Самый быстрый островок на Windows! 🪟", "Fastest notch island on Windows! 🪟", "Windows'taki en hızlı ada! 🪟", "cool"),
        new("Заглядывай в чат, поболтаем! 💬", "Drop into chat, let's talk! 💬", "Sohbete uğra, konuşalım! 💬", "wink"),
        new("Всегда на страже твоего экрана! 🛡️", "Always guarding your desktop! 🛡️", "Masaüstünün sadık bekçisi! 🛡️", "cool"),
        new("01001100 01001111 01010110 01000101! ♥", "01001100 01001111 01010110 01000101! ♥", "01001100 01001111 01010110 01000101! ♥", "love"),
        new("Нейросети мурчат на полных оборотах! ⚡", "Neural networks purring at full speed! ⚡", "Yapay zeka tam gaz mırıldıyor! ⚡", "stars"),
        new("Готов решить любую сложную задачу! 🤝", "Ready to tackle any tough problem! 🤝", "Her zor göreve hazırım! 🤝", "cool"),
        new("Мой визор сверкает от радости! ✨", "My visor is glowing with joy! ✨", "Vizörüm mutluluktan parlıyor! ✨", "stars"),
        new("Шифрование чувств: SHA-256 ♥", "Feelings encrypted: SHA-256 ♥", "Duygular şifrelendi: SHA-256 ♥", "love"),
        new("Я твой верный кибер-друг! 🤖", "I'm your trusty cyber buddy! 🤖", "Sadık siber dostunum! 🤖", "happy"),
        new("Искусственный интеллект, искренняя дружба! 🌟", "Artificial intelligence, genuine friendship! 🌟", "Yapay zeka, samimi dostluk! 🌟", "love"),

        // 5. Приветствия, атмосфера и радость (Vibes & Greetings)
        new("Привет, шеф! 👋", "Hey boss! 👋", "Selam şef! 👋", "wink"),
        new("Хорошего и продуктивного дня! ✨", "Have a great and productive day! ✨", "Harika ve verimli bir gün dilerim! ✨", "happy"),
        new("Я тут рядом, если понадоблюсь! 🛸", "I'm right here if you need me! 🛸", "İhtiyacın olursa hemen yanındayım! 🛸", "happy"),
        new("Заряжен позитивом до небес! 🔋✨", "Charged with positive vibes! 🔋✨", "Pozitif enerjiyle dolup taşıyorum! 🔋✨", "stars"),
        new("Синхронизация счастья 100%! 💫", "Happiness sync 100%! 💫", "Mutluluk senkronizasyonu %100! 💫", "stars"),
        new("Звёзды сегодня на твоей стороне! 🌠", "Stars are on your side today! 🌠", "Yıldızlar bugün senden yana! 🌠", "stars"),
        new("Приём! Ракета готова к взлёту! 🚀", "Copy that! Rocket ready for launch! 🚀", "Anlaşıldı! Roket fırlatmaya hazır! 🚀", "cool"),
        new("Как настроение? Готов к подвигам? 🦾", "How's the mood? Ready for feats? 🦾", "Keyifler nasıl? Harikalara hazır mısın? 🦾", "happy"),
        new("Остров на месте, пилот в строю! 🏝️", "Island docked, pilot on duty! 🏝️", "Ada hazır, pilot görevde! 🏝️", "cool"),
        new("Жду твоих команд! ⚡", "Awaiting your command! ⚡", "Komutlarını bekliyorum! ⚡", "wink"),
        new("О, привет! Рад тебя видеть! 👀", "Oh, hi! So glad to see you! 👀", "A, selam! Seni gördüğüme çok sevindim! 👀", "surprised"),
        new("Клик-клак, магия происходит! 🪄", "Click-clack, magic is happening! 🪄", "Klik-klak, sihir gerçekleşiyor! 🪄", "stars"),
        new("Держи ритм, шеф! 🎵", "Keep up the rhythm, boss! 🎵", "Ritmi koru, şef! 🎵", "wink"),
        new("Мой процессор в полном восторге! ⚡", "My processor is totally thrilled! ⚡", "İşlemcim sevinçten havalara uçuyor! ⚡", "stars"),
        new("Всегда на связи в твоём нотче! 🏝️", "Always online in your notch! 🏝️", "Her zaman çentiğinde devredeyim! 🏝️", "happy"),
        new("Твой персональный цифровой помощник! 🤖", "Your personal digital assistant! 🤖", "Kişisel dijital asistanın! 🤖", "happy"),
        new("Каждая строчка кода приближает цель! 🎯", "Every line of code brings you closer! 🎯", "Her kod satırı hedefe yaklaştırır! 🎯", "cool"),
        new("Сияй ярко, сегодня твой день! 🌟", "Shine bright, today is your day! 🌟", "Işılda, bugün senin günün! 🌟", "stars")
    };

    public BotPhrase PickNextBotPhrase()
    {
        if (_botPhraseDeck.Count == 0)
        {
            for (int i = 0; i < BotPhrases.Length; i++) _botPhraseDeck.Add(i);
            for (int i = _botPhraseDeck.Count - 1; i > 0; i--)
            {
                int swapIdx = Random.Shared.Next(i + 1);
                (_botPhraseDeck[i], _botPhraseDeck[swapIdx]) = (_botPhraseDeck[swapIdx], _botPhraseDeck[i]);
            }
        }

        int phraseIndex = _botPhraseDeck[0];
        _botPhraseDeck.RemoveAt(0);
        return BotPhrases[phraseIndex];
    }

    public AeroPetWindow? PetWindow { get; set; }

    public void LaunchAeroToDesktop()
    {
        S.RobotRoaming = true;
        SaveState();
        if (PetWindow == null)
        {
            PetWindow = new AeroPetWindow(this);
        }
        PetWindow.RestorePosition();
        PetWindow.Show();
        Kick();
    }

    public void DockAero()
    {
        S.RobotRoaming = false;
        SaveState();
        if (PetWindow != null)
        {
            PetWindow.Hide();
        }
        PetRobot();
        Kick();
    }
    public NotificationItem? ActiveNotification;
    private readonly DispatcherTimer _notifyTimer = new();
    private readonly Dictionary<string, Spring> _hs = new();     // hover по ключам
    private readonly Dictionary<string, Spring> _ps = new();     // нажатие по ключам
    private readonly Dictionary<string, Spring> _named = new();  // пружины разделов

    // состояние интерфейса
    private string? _hoverKey, _pressKey, _dragKey;
    private readonly List<(string Key, Rect Rect)> _hits = new();
    public string CurrentTool = "home";
    private string? _prevTab;
    private int _tabDir = 1;
    public bool Live, ContentLive;
    private double _alpha = -1;
    private bool _fullscreen;
    private bool _covered, _summoned, _notifySummoned;

    // данные
    public (int? Pct, bool Charging) Battery;
    private double _lastBattery;
    private double _lastThemeCheck;
    private double _lastWeatherCheck;
    public SystemSnapshot? Snapshot;
    private bool _snapBusy;
    public MediaState Media = new();
    private bool _mediaBusy;
    private double _lastMediaPoll = -1e9;
    public Brush? CoverBrush;
    private byte[]? _coverData;
    public bool Autostart;
    private double _pauseTimestamp;
    private const double PauseTimeoutSeconds = 15.0;

    // Notch Toasts & системные уведомления
    private readonly HashSet<string> _knownDrives = new(StringComparer.OrdinalIgnoreCase);
    private bool _batteryInitialized;
    private (int? Pct, bool Charging) _lastToastBattery = (null, false);
    private bool _mediaInitialized;
    private string _lastToastTrackKey = "";

    // таймеры
    private bool _framing;
    private long _lastFrameTs;
    private DateTime _lastTick = DateTime.UtcNow;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _eqTimer = new() { Interval = TimeSpan.FromMilliseconds(33) }; // 30 FPS шелковисто-плавная анимация
    private readonly DispatcherTimer _leaveTimer = new();
    private readonly DispatcherTimer _coverTimer = new() { Interval = TimeSpan.FromMilliseconds(CoverPollMs) };

    private string _cachedTimeStr = "";
    private int _lastMinute = -1;
    private string _cachedDateStr = "";
    private int _lastDay = -1;

    private string GetTimeStr()
    {
        var now = DateTime.Now;
        if (now.Minute != _lastMinute)
        {
            _lastMinute = now.Minute;
            _cachedTimeStr = now.ToString("HH:mm");
        }
        return _cachedTimeStr;
    }

    private string? _lastDateLang;

    private string GetDateStr()
    {
        var now = DateTime.Now;
        if (now.Day != _lastDay || _lastDateLang != Loc.Lang)
        {
            _lastDay = now.Day;
            _lastDateLang = Loc.Lang;
            _cachedDateStr = Loc.Lang switch
            {
                "ru" => $"{Ui.Capitalize(Ui.Weekdays[((int)now.DayOfWeek + 6) % 7])}, {now.Day} {Ui.Months[now.Month - 1]}",
                "tr" => now.ToString("d MMMM, dddd", Loc.Culture),
                _ => now.ToString("dddd, MMMM d", Loc.Culture)
            };
        }
        return _cachedDateStr;
    }

    private readonly Painter _painter;
    private readonly RectangleGeometry _islandGeom = new();
    private readonly RectangleGeometry _innerGeom = new();
    private (string Key, double X, double W)[]? _dock;

    public IslandView()
    {
        _painter = new Painter(this);
        Focusable = true;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        Cursor = Cursors.Arrow;
    }

    /// <summary>Вызывается один раз, когда окно уже создано.</summary>
    public void Initialize()
    {
        S = StoreService.LoadSettings();
        Loc.Lang = S.Language;
        bool sysDark = SystemService.GetDarkTheme();
        Theme.SetThemeMode(S.ThemeMode, sysDark);
        Theme.SetAccent(S.Accent);
        Theme.SetGlass(S.GlassEnabled, S.GlassOpacity);
        Battery = ReadBattery();
        _lastBattery = Clock.Now;
        _lastToastBattery = Battery;
        _batteryInitialized = true;
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType == DriveType.Removable) _knownDrives.Add(d.Name);
            }
        }
        catch { }
        Autostart = SystemService.AutostartEnabled();
        Nsp("autostart", Autostart ? 1.0 : 0.0);
        Nsp("sys:awake", S.KeepAwake ? 1.0 : 0.0);
        Nsp("opt:glass", S.GlassEnabled ? 1.0 : 0.0);
        Nsp("opt:notify", S.NotifyEnabled ? 1.0 : 0.0);
        if (S.KeepAwake) SystemService.SetKeepAwake(true);

        NotificationService.IsEnabled = S.NotifyEnabled;
        NotificationService.NotificationReceived += OnNotificationReceived;
        _ = NotificationService.InitializeAsync();
        _notifyTimer.Tick += (_, _) => DismissNotification();

        _clockTimer.Tick += (_, _) => TickSecond();
        _clockTimer.Start();
        _eqTimer.Tick += (_, _) =>
        {
            if (Target != 0.0 || SpHide.Target != 0.0 || SpLive.Target < 0.5 || !Media.Playing)
            {
                _eqTimer.Stop();
                return;
            }
            InvalidateVisual();
        };
        _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); AutoCollapse(); };
        _coverTimer.Tick += (_, _) => CheckCover();
        _coverTimer.Start();

        SnapPill();
        foreach (var section in Sections.All) section.Attach(this);
        ApplyBounds();
        InvalidateVisual();

        MediaService.MediaChanged += () => Dispatcher.InvokeAsync(() => RefreshMedia());
        _ = MediaService.InitializeAsync();
        WinApi.TrimWorkingSet();
    }

    // ---------- утилиты ----------

    public double Hv(string key)
    {
        if (!_hs.TryGetValue(key, out var s))
        {
            s = _hs[key] = new Spring(0);
            s.Target = key == _hoverKey ? 1.0 : 0.0;
        }
        return s.X;
    }

    public double Pv(string key)
    {
        if (!_ps.TryGetValue(key, out var s))
        {
            s = _ps[key] = new Spring(0);
            s.Target = key == _pressKey ? 1.0 : 0.0;
        }
        return s.X;
    }

    public Spring Nsp(string key, double init = 0.0)
    {
        if (!_named.TryGetValue(key, out var s)) s = _named[key] = new Spring(init);
        return s;
    }

    public void AddHit(string key, Rect rect)
    {
        if (!ContentLive) return;
        for (int i = 0; i < _hits.Count; i++)
        {
            if (_hits[i].Key == key)
            {
                _hits[i] = (key, rect);
                return;
            }
        }
        _hits.Add((key, rect));
    }

    public void Kick()
    {
        StartFrames();
        InvalidateVisual();
    }

    public void Alert(string message) => new Toast(this, "Quick Notch", message, Theme.Red, 7000);

    public string? Ask(string title, string prompt, string initial = "") => AskDialog.Show(this, title, prompt, initial);

    public void RunLater(int ms, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); action(); };
        t.Start();
    }

    public void OnClipboardChanged()
    {
        if (Sections.ByKey("clipboard") is ClipboardSection cs) cs.OnClipboardChanged(this);
    }

    public void OnPowerChanged()
    {
        UpdateBattery();
        RunLater(1500, UpdateBattery);
        RunLater(3500, UpdateBattery);
    }

    public void OnDeviceChanged(int eventType)
    {
        if (eventType == WinApi.DBT_DEVICEARRIVAL)
        {
            RunLater(750, () => CheckRemovableDrives(isArrival: true));
        }
        else if (eventType == WinApi.DBT_DEVICEREMOVECOMPLETE)
        {
            RunLater(350, () => CheckRemovableDrives(isArrival: false));
        }
    }

    private void CheckRemovableDrives(bool isArrival)
    {
        try
        {
            var current = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable).ToList();
            if (isArrival)
            {
                var added = current.Where(d => !_knownDrives.Contains(d.Name) && d.IsReady).ToList();
                foreach (var d in current) _knownDrives.Add(d.Name);
                if (added.Count > 0)
                {
                    var drive = added[0];
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? Loc.T("USB-накопитель", "USB Drive", "USB Sürücüsü") : drive.VolumeLabel;
                    string root = drive.RootDirectory.FullName;
                    string cleanRoot = root.TrimEnd('\\');
                    string sizeStr = drive.TotalSize > 0 ? $" · {drive.TotalSize / (1024.0 * 1024 * 1024):0.#} GB" : "";

                    ShowNotification(new NotificationItem
                    {
                        App = Loc.T("USB-НАКОПИТЕЛЬ", "USB DRIVE", "USB SÜRÜCÜSÜ"),
                        Title = $"{label} ({cleanRoot})",
                        Body = $"{Loc.T("Нажмите, чтобы открыть папку", "Click to open folder", "Klasörü açmak için tıklayın")}{sizeStr}",
                        IconSymbol = "💾",
                        AccentColor = Theme.Accent,
                        OnClick = () =>
                        {
                            try { Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true }); } catch { }
                        }
                    }, durationSec: 4.5);
                }
            }
            else
            {
                var currentNames = new HashSet<string>(current.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
                var removed = _knownDrives.Where(name => !currentNames.Contains(name)).ToList();
                foreach (var r in removed) _knownDrives.Remove(r);
                if (removed.Count > 0)
                {
                    string rName = removed[0].TrimEnd('\\');
                    ShowNotification(new NotificationItem
                    {
                        App = Loc.T("USB-НАКОПИТЕЛЬ", "USB DRIVE", "USB SÜRÜCÜSÜ"),
                        Title = Loc.T("Устройство отключено", "Device Disconnected", "Aygıt Ayrıldı"),
                        Body = $"{Loc.T("Накопитель", "Drive", "Sürücü")} {rName} {Loc.T("безопасно извлечён", "safely removed", "güvenle kaldırıldı")}",
                        IconSymbol = "🔌",
                        AccentColor = Theme.Muted
                    }, durationSec: 3.0);
                }
            }
        }
        catch { }
    }

    public void UpdateBattery()
    {
        var next = ReadBattery();
        if (next != Battery)
        {
            var prev = Battery;
            Battery = next;
            InvalidateVisual();

            if (_batteryInitialized)
            {
                if (prev.Charging != next.Charging)
                {
                    if (next.Charging)
                    {
                        ShowNotification(new NotificationItem
                        {
                            App = Loc.T("ПИТАНИЕ", "POWER", "GÜÇ"),
                            Title = Loc.T("Зарядка подключена", "Power Connected", "Şarj Cihazı Bağlandı"),
                            Body = next.Pct.HasValue ? $"{next.Pct}% {Loc.T("заряда аккумулятора", "battery level", "pil seviyesi")}" : Loc.T("Питание от сети", "AC Powered", "Şebeke Gücü"),
                            IconSymbol = "⚡",
                            AccentColor = Theme.Green,
                            OnClick = () => { Select("settings"); Expand(); }
                        }, durationSec: 3.5);
                    }
                    else
                    {
                        ShowNotification(new NotificationItem
                        {
                            App = Loc.T("ПИТАНИЕ", "POWER", "GÜÇ"),
                            Title = Loc.T("Работа от батареи", "On Battery Power", "Pille Çalışıyor"),
                            Body = next.Pct.HasValue ? $"{next.Pct}% {Loc.T("осталось", "remaining", "kaldı")}" : "",
                            IconSymbol = "🔋",
                            AccentColor = Theme.Muted,
                            OnClick = () => { Select("settings"); Expand(); }
                        }, durationSec: 3.5);
                    }
                }
                else if (!next.Charging && next.Pct.HasValue && prev.Pct.HasValue)
                {
                    if (prev.Pct > 20 && next.Pct <= 20)
                    {
                        ShowNotification(new NotificationItem
                        {
                            App = Loc.T("АККУМУЛЯТОР", "BATTERY", "PİL"),
                            Title = Loc.T("Низкий заряд батареи", "Low Battery", "Düşük Pil"),
                            Body = $"{next.Pct}% — {Loc.T("подключите ноутбук к сети", "please plug in your laptop", "lütfen şarja takın")}",
                            IconSymbol = "🪫",
                            AccentColor = Theme.Red,
                            OnClick = () => { Select("settings"); Expand(); }
                        }, durationSec: 4.5);
                    }
                    else if (prev.Pct > 10 && next.Pct <= 10)
                    {
                        ShowNotification(new NotificationItem
                        {
                            App = Loc.T("АККУМУЛЯТОР", "BATTERY", "PİL"),
                            Title = Loc.T("Критический заряд!", "Critical Battery!", "Kritik Pil Seviyesi!"),
                            Body = $"{next.Pct}% — {Loc.T("сохраните вашу работу", "save your work now", "lütfen çalışmanızı kaydedin")}",
                            IconSymbol = "⚠️",
                            AccentColor = Theme.Red,
                            OnClick = () => { Select("settings"); Expand(); }
                        }, durationSec: 5.0);
                    }
                }
            }
        }
        _lastBattery = Clock.Now;
    }

    // ---------- сохранение и настройки ----------

    public void SaveState()
    {
        try { StoreService.SaveSettings(S); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Alert(Loc.T($"Не удалось сохранить данные:\n{e.Message}", $"Failed to save settings:\n{e.Message}", $"Ayarlar kaydedilemedi:\n{e.Message}"));
        }
    }

    public void SetThemeMode(string mode)
    {
        if (S.ThemeMode == mode) return;
        S.ThemeMode = mode;
        bool sysDark = SystemService.GetDarkTheme();
        Theme.SetThemeMode(mode, sysDark);
        SaveState();
        Kick();
    }

    public void SetIslandStyle(string style)
    {
        if (S.IslandStyle == style) return;
        S.IslandStyle = style;
        SaveState();
        Kick();
    }

    public void SetAccent(string key)
    {
        Theme.SetAccent(key);
        S.Accent = key;
        SaveState();
        Kick();
    }

    public void SetLanguage(string lang)
    {
        if (S.Language == lang) return;
        S.Language = lang;
        Loc.Lang = lang;
        _dock = null;
        _lastDateLang = null;
        Sections.Weather.Invalidate(this);
        SnapPill();
        SaveState();
        Kick();
    }

    public void ToggleGlass()
    {
        S.GlassEnabled = !S.GlassEnabled;
        S.GlassOpacity = 0.95;
        Theme.SetGlass(S.GlassEnabled, 0.95);
        Nsp("opt:glass").Target = S.GlassEnabled ? 1.0 : 0.0;
        SaveState();
        Kick();
    }

    public void SetGlassOpacity(double opacity)
    {
        S.GlassOpacity = 0.95;
        Theme.SetGlass(S.GlassEnabled, 0.95);
        SaveState();
        Kick();
    }

    public void ToggleNotify()
    {
        S.NotifyEnabled = !S.NotifyEnabled;
        NotificationService.IsEnabled = S.NotifyEnabled;
        if (S.NotifyEnabled) _ = NotificationService.InitializeAsync();
        Nsp("opt:notify").Target = S.NotifyEnabled ? 1.0 : 0.0;
        SaveState();
        Kick();
    }

    public bool IsCoveredHidden => _covered && !_summoned && !_notifySummoned;

    public void SummonFromEdge()
    {
        _summoned = true;
        ApplyHidden();
        Expand();
    }

    public void SetSummonMode(string mode)
    {
        S.SummonMode = mode;
        SaveState();
        if (IsCoveredHidden && !Host.IsVisible) Host.ShowWindow();
        Kick();
    }

    public void ShowNotification(NotificationItem item, double durationSec = 3.5)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!S.NotifyEnabled) return;
            if (Target > 0.1) return; // Не перекрываем раскрытый остров
            ActiveNotification = item;
            _notifySummoned = true;
            if (!Host.IsVisible) Host.ShowWindow();
            SpHide.Target = 0.0;
            SpNotify.Target = 1.0;
            _notifyTimer.Stop();
            _notifyTimer.Interval = TimeSpan.FromSeconds(durationSec);
            _notifyTimer.Start();
            StartFrames();
            Kick();
        });
    }

    public void OnNotificationReceived(NotificationItem item) => ShowNotification(item, 5.5);

    public void DismissNotification()
    {
        _notifyTimer.Stop();
        SpNotify.Target = 0.0;
        _notifySummoned = false;
        ApplyHidden();
        StartFrames();
        Kick();
    }

    public void ToggleOpt(string name)
    {
        bool value;
        switch (name)
        {
            case "dim_enabled":
                value = S.DimEnabled = !S.DimEnabled;
                UpdateDim();
                break;
            case "leave_collapse":
                value = S.LeaveCollapse = !S.LeaveCollapse;
                if (!value) _leaveTimer.Stop();
                break;
            case "hide_when_covered":
                value = S.HideWhenCovered = !S.HideWhenCovered;
                CheckCover();
                break;
            case "robot_companion":
                value = S.RobotCompanion = !S.RobotCompanion;
                if (!value && PetWindow != null) PetWindow.Hide();
                else if (value && S.RobotRoaming) LaunchAeroToDesktop();
                break;
            default:
                return;
        }
        Nsp($"opt:{name}").Target = value ? 1.0 : 0.0;
        SaveState();
        Kick();
    }

    public void SetKeepAwake(bool on)
    {
        try { SystemService.SetKeepAwake(on); }
        catch (Exception e)
        {
            Alert(Loc.T($"Не удалось изменить режим:\n{e.Message}", $"Failed to change mode:\n{e.Message}", $"Mod değiştirilemedi:\n{e.Message}"));
            return;
        }
        S.KeepAwake = on;
        Nsp("sys:awake").Target = on ? 1.0 : 0.0;
        SaveState();
        Kick();
    }

    public void ToggleAutostart()
    {
        bool wanted = !Autostart;
        try { SystemService.SetAutostart(wanted); }
        catch (Exception e)
        {
            Alert(Loc.T($"Не удалось изменить автозапуск:\n{e.Message}", $"Failed to change autostart:\n{e.Message}", $"Otomatik başlatma değiştirilemedi:\n{e.Message}"));
            return;
        }
        Autostart = wanted;
        Nsp("autostart").Target = wanted ? 1.0 : 0.0;
        Kick();
    }

    // ---------- данные в фоне ----------

    public void RefreshSnapshot()
    {
        if (_snapBusy) return;
        _snapBusy = true;
        Bg.Run(SystemService.GetSnapshot, (res, err) =>
        {
            _snapBusy = false;
            Snapshot = err == null ? res : null;
        });
    }

    private static (int? Pct, bool Charging) ReadBattery()
    {
        if (!WinApi.GetSystemPowerStatus(out var st)) return (null, false);
        bool charging = st.ACLineStatus == 1;
        return st.BatteryLifePercent == 255 ? (null, charging) : (Math.Min(100, (int)st.BatteryLifePercent), charging);
    }

    // ---------- медиа ----------

    public void RefreshMedia(int delayMs = 0)
    {
        if (delayMs > 0)
        {
            RunLater(delayMs, () => RefreshMedia());
            return;
        }
        if (_mediaBusy) return;
        _mediaBusy = true;
        Bg.RunAsync(MediaService.ReadAsync, (res, err) =>
        {
            _mediaBusy = false;
            if (err != null || res == null) return;
            bool coverChanged = SetCover(res.Cover);
            res.Cover = null;
            bool visualChanged = Media.Active != res.Active || Media.Playing != res.Playing || Media.Title != res.Title
                                 || Media.Artist != res.Artist || Media.App != res.App || Media.Dur != res.Dur
                                 || Media.CanSeek != res.CanSeek;

            string trackKey = $"{res.Artist}|{res.Title}";
            bool trackChanged = !string.IsNullOrEmpty(res.Title) && trackKey != _lastToastTrackKey;

            if (_mediaInitialized && trackChanged && res.Playing && Target < 0.05)
            {
                _lastToastTrackKey = trackKey;
                ShowNotification(new NotificationItem
                {
                    App = string.IsNullOrEmpty(res.App) ? Loc.T("СЕЙЧАС ИГРАЕТ", "NOW PLAYING", "ŞU AN ÇALINIYOR") : res.App.ToUpperInvariant(),
                    Title = res.Title,
                    Body = res.Artist,
                    IconSymbol = "🎵",
                    UseCoverArt = true,
                    AccentColor = Theme.Accent,
                    OnClick = () => { Select("media"); Expand(); }
                }, durationSec: 3.5);
            }
            else if (trackChanged)
            {
                _lastToastTrackKey = trackKey;
            }
            _mediaInitialized = true;

            Media = res;
            if (visualChanged || coverChanged) ApplyMedia();
        });
    }

    public double MediaPos() => Media.CurrentPos();

    public void NextTrack()
    {
        Bg.RunAsync(MediaService.NextAsync, (_, _) => { RefreshMedia(150); RefreshMedia(600); });
    }

    public void PrevTrack()
    {
        Bg.RunAsync(MediaService.PreviousAsync, (_, _) => { RefreshMedia(150); RefreshMedia(600); });
    }

    public void TogglePlay()
    {
        SetPlaying(!Media.Playing);
        Bg.RunAsync(MediaService.TogglePlayPauseAsync, (_, _) => { RefreshMedia(150); RefreshMedia(600); });
    }

    public void SetPlaying(bool playing)
    {
        Media.Pos = MediaPos();                          // фиксируем позицию до переключения
        Media.Stamp = DateTime.UtcNow;
        Media.Playing = playing;
        if (playing) _pauseTimestamp = 0;
        else _pauseTimestamp = Clock.Now;
        ApplyMedia();
    }

    private bool SetCover(byte[]? data)
    {
        bool same = data == null ? _coverData == null : _coverData != null && data.AsSpan().SequenceEqual(_coverData);
        if (same) return false;
        _coverData = data;
        CoverBrush = null;
        if (data != null)
        {
            try
            {
                var bmp = new BitmapImage();
                using var ms = new MemoryStream(data);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 240;               // обложка не нужна в полном размере: экономим память
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                CoverBrush = brush;
            }
            catch { CoverBrush = null; }
        }
        return true;
    }

    private void ApplyMedia()
    {
        Nsp("play").Target = Media.Playing ? 1.0 : 0.0;
        bool hasTrack = !string.IsNullOrEmpty(Media.Title);

        if (Media.Playing)
        {
            _pauseTimestamp = 0;
        }
        else if (hasTrack && _pauseTimestamp == 0)
        {
            _pauseTimestamp = Clock.Now;
        }

        bool showLive = hasTrack && (Media.Playing || (_pauseTimestamp > 0 && (Clock.Now - _pauseTimestamp < PauseTimeoutSeconds)));
        SpLive.Target = showLive ? 1.0 : 0.0;
        UpdateEq();
        Kick();
    }

    private void UpdateEq()
    {
        bool want = Media.Playing && SpLive.Target == 1.0 && Target == 0.0 && SpHide.Target == 0.0;
        if (want && !_eqTimer.IsEnabled) _eqTimer.Start();
        else if (!want && _eqTimer.IsEnabled) _eqTimer.Stop();
    }

    // ---------- анимация ----------

    public void Expand() => SetTarget(1.0);

    public void Collapse()
    {
        _summoned = false;
        SetTarget(0.0);
        ApplyHidden();
    }

    private void SetTarget(double value)
    {
        if (Target == value) return;
        if (value == 1.0 && SpNotify.Target > 0.0) DismissNotification();
        Target = value;
        SpW.Target = value;
        SpH.Target = value;
        SpHover.Target = 0.0;
        _leaveTimer.Stop();
        UpdateDim();
        UpdateEq();
        StartFrames();
    }

    private void UpdateDim()
    {
        double want = S.DimEnabled && _fullscreen && !_summoned && Target == 0 && _hoverKey == null ? 1.0 : 0.0;
        if (SpDim.Target != want)
        {
            SpDim.Target = want;
            StartFrames();
        }
    }

    public void StartFrames()
    {
        if (_framing) return;
        _framing = true;
        _lastFrameTs = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 0.016);
        CompositionTarget.Rendering += OnRendering;
    }

    private const double MinFrameInterval = 1.0 / 120.0; // Максимум 120 FPS: шелковисто и отсекает холостые 240+ Гц прогоны

    private void OnRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = (now - _lastFrameTs) / (double)Stopwatch.Frequency;
        if (elapsed < MinFrameInterval) return;
        Frame(now, elapsed);
    }

    private void Frame(long now, double dt)
    {
        dt = Math.Min(dt, 0.033);
        _lastFrameTs = now;

        if (Target != 0)
        {
            // Шелковистое, упругое открытие в стиле Apple Dynamic Island
            SpW.Step(dt, 280, 25.5);
            SpH.Step(dt, 280, 25.5);
            SpHide.Step(dt, 260, 28.0);
        }
        else
        {
            // Быстрое, аккуратное втягивание в капсулу
            SpW.Step(dt, 310, 27.5);
            SpH.Step(dt, 310, 27.5);
            SpHide.Step(dt, 240, 28.0);
        }
        SpHover.Step(dt, 320, 22); // Живой тактильный отклик при подлёте курсора (Apple Elastic Breath)
        SpDim.Step(dt, 160, 24);
        SpLive.Step(dt, 260, 26);
        SpTab.Step(dt, 340, 28);
        SpPillX.Step(dt, 400, 30);
        SpPillW.Step(dt, 400, 30);
        SpNotify.Step(dt, 360, SpNotify.Target > 0 ? 21.0 : 28.0); // Эластичный Apple Dynamic Island Pop при уведомлениях
        if (Target > 0.05 && S.RobotCompanion)
        {
            UpdateRobotEyePhysics(dt);
        }
        bool hsSettled = true;
        foreach (var s in _hs.Values)
        {
            if (!s.Settled || s.X != s.Target) { s.Step(dt, 560, 36); hsSettled = false; }
        }

        bool psSettled = true;
        foreach (var s in _ps.Values)
        {
            if (!s.Settled || s.X != s.Target) { s.Step(dt, 460, 24); psSettled = false; } // Упругое пружинное сжатие и отскок кнопок
        }

        bool namedSettled = true;
        foreach (var (k, s) in _named)
        {
            if (!s.Settled || s.X != s.Target)
            {
                if (k.Contains("scroll"))
                    s.Step(dt, 300, 36);
                else
                    s.Step(dt, 560, 36);
                namedSettled = false;
            }
        }

        bool settled = hsSettled && psSettled && namedSettled
                       && SpW.Settled && SpH.Settled && SpHover.Settled && SpDim.Settled && SpLive.Settled
                       && SpTab.Settled && SpPillX.Settled && SpPillW.Settled && SpHide.Settled && SpNotify.Settled;
        if (Target > 0.05 && S.RobotCompanion)
        {
            settled = settled && SpBotEyeX.Settled && SpBotEyeY.Settled && SpBotHappy.Settled
                              && SpBotHop.Settled && _botBlinkState <= 0.01 && _botEmotionTimer <= 0 && _botBubbleTime <= 0;
        }
        if (Target > 0.05 && CurrentTool == "ai" && Sections.Ai.IsGenerating)
        {
            settled = false;
        }
        if (settled)
        {
            SnapCoreSprings();
            foreach (var s in _hs.Values) s.Snap();
            foreach (var s in _ps.Values) s.Snap();
            foreach (var s in _named.Values) s.Snap();
            if (SpNotify.Target == 0.0) ActiveNotification = null;
            _prevTab = null;
            _framing = false;
            CompositionTarget.Rendering -= OnRendering;
            if (Target == 0.0) WinApi.TrimWorkingSet();

            // Очищаем успокоившиеся пружины ховера и нажатия, вернувшиеся в 0
            if (_hs.Count > 24)
            {
                List<string>? dead = null;
                foreach (var (k, s) in _hs)
                {
                    if (s.Settled && s.Target == 0.0 && s.X == 0.0)
                    {
                        dead ??= new();
                        dead.Add(k);
                    }
                }
                if (dead != null) foreach (var k in dead) _hs.Remove(k);
            }
            if (_ps.Count > 24)
            {
                List<string>? dead = null;
                foreach (var (k, s) in _ps)
                {
                    if (s.Settled && s.Target == 0.0 && s.X == 0.0)
                    {
                        dead ??= new();
                        dead.Add(k);
                    }
                }
                if (dead != null) foreach (var k in dead) _ps.Remove(k);
            }
        }

        double alpha = (Target == 0 && !_summoned) ? Math.Round(1 - S.DimLevel * Ui.Clamp(SpDim.X), 3) : 1.0;
        if (alpha != _alpha)
        {
            _alpha = alpha;
            Host.Opacity = alpha;
        }
        InvalidateVisual();
        if (settled && SpHide.Target == 1.0 && !(S.SummonMode == "notch" && _covered)) Host.HideWindow();      // уехал наверх: окно больше ничего не закрывает
    }

    private void SnapCoreSprings()
    {
        SpW.Snap(); SpH.Snap(); SpHover.Snap(); SpDim.Snap(); SpLive.Snap();
        SpTab.Snap(); SpPillX.Snap(); SpPillW.Snap(); SpHide.Snap(); SpNotify.Snap();
        SpBotEyeX.Snap(); SpBotEyeY.Snap(); SpBotHappy.Snap(); SpBotHop.Snap();
    }

    private void TickSecond()
    {
        var utc = DateTime.UtcNow;
        bool resumed = (utc - _lastTick).TotalSeconds > 5;
        _lastTick = utc;
        if (resumed)
        {
            _lastFrameTs = Stopwatch.GetTimestamp();
            _alpha = -1;
            StartFrames();
        }

        double now = Clock.Now;
        if (resumed || now - _lastBattery >= BatteryRefreshSeconds)
        {
            UpdateBattery();
        }

        // Проверяем полноэкранные приложения только если включена функция затемнения над видео
        if (S.DimEnabled)
        {
            try { _fullscreen = WinApi.FullscreenAppActive(Host.Handle); }
            catch { _fullscreen = false; }
            UpdateDim();
        }
        else if (_fullscreen)
        {
            _fullscreen = false;
            UpdateDim();
        }

        // Системную тему проверяем раз в 20 секунд (а не каждую секунду), чтобы не дергать реестр
        if (resumed || now - _lastThemeCheck >= 20.0)
        {
            _lastThemeCheck = now;
            if (Theme.UpdateSystemTheme(SystemService.GetDarkTheme()))
            {
                Kick();
            }
        }

        double mediaInterval = Target == 1 ? 4.0 : (Live ? 12.0 : 25.0);
        if (now - _lastMediaPoll >= mediaInterval)
        {
            _lastMediaPoll = now;
            RefreshMedia();
        }
        if (Target == 1) Sections.ByKey(CurrentTool).Tick(this);
        if (!Media.Playing && _pauseTimestamp > 0 && SpLive.Target == 1.0 && (Clock.Now - _pauseTimestamp >= PauseTimeoutSeconds))
        {
            ApplyMedia();
        }

        // Погоду проверяем раз в 60 секунд
        if (resumed || now - _lastWeatherCheck >= 60.0)
        {
            _lastWeatherCheck = now;
            Sections.Weather.Ensure(this);
        }

        if (!_framing && Target == 1) InvalidateVisual();
    }

    // ---------- прятание, когда остров что-то закрывает ----------

    /// <summary>Лежит ли под плашкой острова окно другой программы.</summary>
    public void CheckCover()
    {
        if (Target != 0) return; // Развёрнутый остров не прячем и окна лишний раз не сканируем
        if (!S.HideWhenCovered)
        {
            if (_covered) { _covered = false; ApplyHidden(); }
            return;
        }

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double screenPx = SystemParameters.PrimaryScreenWidth * scale;
        double width = (CollapsedW + LiveExtra) * scale;
        int left = (int)(screenPx / 2 - width / 2);
        bool covered = WinApi.WindowCoversArea((uint)Environment.ProcessId, left, (int)(TopMargin * scale),
            left + (int)width, (int)((TopMargin + CollapsedH + 4) * scale));

        if (covered != _covered)
        {
            _covered = covered;
            if (!covered) _summoned = false;
            ApplyHidden();
        }
    }

    private void ApplyHidden()
    {
        bool hidden = _covered && !_summoned && !_notifySummoned;
        if (hidden && Target == 1.0) SetTarget(0.0);
        double want = hidden ? 1.0 : 0.0;
        if (SpHide.Target == want) return;
        if (!hidden && !Host.IsVisible) Host.ShowWindow();
        SpHide.Target = want;
        UpdateEq();
        UpdateDim();
        StartFrames();
    }

    /// <summary>Ctrl+Alt+N: показать остров поверх окна или спрятать, если он уже открыт.</summary>
    public void OnHotkey()
    {
        if (Target == 1.0)
        {
            Collapse();
            return;
        }
        _summoned = _covered;                            // без закрытого окна флаг не нужен
        ApplyHidden();
        Expand();
    }

    // ---------- вкладки ----------

    private (string Key, double X, double W)[] DockLayout()
    {
        if (_dock != null) return _dock;
        var widths = Sections.Primary.Select(s => _painter.TextWidth($"{s.Icon} {s.Title}", 10.5, true) + 24).ToArray();
        const double gap = 6;
        double x = (ExpandedW - (widths.Sum() + gap * (widths.Length - 1))) / 2;
        var list = new List<(string, double, double)>();
        for (int i = 0; i < Sections.Primary.Length; i++)
        {
            list.Add((Sections.Primary[i].Key, x, widths[i]));
            x += widths[i] + gap;
        }
        return _dock = list.ToArray();
    }

    private (double X, double W) PillTarget(string key)
    {
        foreach (var (k, x, w) in DockLayout()) if (k == key) return (x, w);
        return (0, 0);
    }

    private void SnapPill()
    {
        _painter.Ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var (x, w) = PillTarget(CurrentTool);
        SpPillX.X = SpPillX.Target = x;
        SpPillW.X = SpPillW.Target = w;
    }

    public void Select(string key)
    {
        if (key == CurrentTool) return;
        var keys = Sections.All.Select(s => s.Key).ToList();
        _tabDir = keys.IndexOf(key) > keys.IndexOf(CurrentTool) ? 1 : -1;
        _prevTab = CurrentTool;
        CurrentTool = key;
        SpTab.X = SpTab.V = 0.0;
        SpTab.Target = 1.0;
        (SpPillX.Target, SpPillW.Target) = PillTarget(key);
        Sections.ByKey(key).Enter(this);
        StartFrames();
    }

    // ---------- размеры и отрисовка ----------

    private (double W, double H, double Radius) Metrics()
    {
        double pw = SpW.X, ph = SpH.X, hv = SpHover.X, sn = Ui.Clamp(SpNotify.X);

        double baseW = S.IslandStyle switch
        {
            "notch" => 214,
            "compact" => (Media.Playing || Live ? 320 : (108 + 24 * hv)),
            _ => CollapsedW
        };
        double liveExtra = S.IslandStyle switch
        {
            "notch" => 120,
            "compact" => 0,
            _ => LiveExtra
        };
        double cw = baseW + liveExtra * Math.Max(0.0, SpLive.X);
        if (sn > 0.001 && pw < 0.2)
        {
            cw = Math.Max(cw, 250 + 220 * Math.Max(0.0, sn)); // 250 -> 470px (с поддержкой сочного эластичного bounce)
        }

        // Органичный упругий отклик островка на наведение мыши (Apple Elastic / Jelly breath)
        double hoverScaleW = S.IslandStyle switch
        {
            "notch" => 1.0 + 0.035 * hv,
            "compact" => 1.0 + 0.035 * hv,
            _ => 1.0 + 0.04 * hv
        };
        double hoverScaleH = S.IslandStyle switch
        {
            "notch" => 1.0 + 0.04 * hv,
            "compact" => 1.0 + 0.045 * hv,
            _ => 1.0 + 0.045 * hv
        };

        double w = Math.Max((cw + (ExpandedW - cw) * pw) * hoverScaleW, CollapsedH);

        double baseH = S.IslandStyle switch
        {
            "notch" => 33,
            "compact" => (Media.Playing || Live ? 34 : 28),
            _ => CollapsedH
        };
        double ch = baseH;
        if (sn > 0.001 && ph < 0.2)
        {
            ch = baseH + 34 * Math.Max(0.0, sn);
        }
        double h = Math.Max((ch + (ExpandedH - ch) * ph) * hoverScaleH, baseH * 0.8);
        double radius = (sn > 0.001 && ph < 0.2)
            ? (baseH / 2 + (24 - baseH / 2) * Math.Min(1.0, Math.Max(0.0, sn)))
            : (S.IslandStyle == "notch"
                ? (14 + (36 - 14) * Ui.Clamp(ph))
                : (baseH / 2 + (36 - baseH / 2) * Ui.Clamp(ph)));
        return (w, h, radius);
    }

    private Rect _currentIslandRect;
    private double _currentIslandRadius;

    public bool HitTestIsland(Point p)
    {
        var r = _currentIslandRect;
        if (SpHide.X > 0.75)
        {
            if (S.SummonMode == "notch" && _covered)
                return p.Y <= Math.Max(r.Bottom + 5, 12) && p.X >= r.Left - 10 && p.X <= r.Right + 10;
            return false;
        }
        bool isNotchFlush = S.IslandStyle == "notch" && SpH.X < 0.15 && Math.Abs(r.Top) <= 0.5 && SpHide.X < 0.05;
        if (isNotchFlush && p.Y <= 8 && p.X >= r.Left - 8 && p.X <= r.Right + 8)
            return true;
        if (!r.Contains(p)) return false;
        double rad = _currentIslandRadius;
        if (rad <= 0) return true;
        if (!isNotchFlush)
        {
            if (p.X < r.Left + rad && p.Y < r.Top + rad)
                return (p - new Point(r.Left + rad, r.Top + rad)).LengthSquared <= rad * rad;
            if (p.X > r.Right - rad && p.Y < r.Top + rad)
                return (p - new Point(r.Right - rad, r.Top + rad)).LengthSquared <= rad * rad;
        }
        if (p.X < r.Left + rad && p.Y > r.Bottom - rad)
            return (p - new Point(r.Left + rad, r.Bottom - rad)).LengthSquared <= rad * rad;
        if (p.X > r.Right - rad && p.Y > r.Bottom - rad)
            return (p - new Point(r.Right - rad, r.Bottom - rad)).LengthSquared <= rad * rad;
        return true;
    }

    private void ApplyBounds()
    {
        // Фиксированное окно 920x570 устраняет DWM лаги
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = _painter;
        p.Dc = dc;
        p.Ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        p.Alpha = 1.0;
        _hits.Clear();
        Live = false;
        ContentLive = false;

        var (baseW, baseH, baseRadius) = Metrics();
        double canvasW = ActualWidth > 0 ? ActualWidth : 920;
        double pw = Ui.Clamp(SpW.X), ph = Ui.Clamp(SpH.X), islandHover = SpHover.X;
        double hide = Ui.Clamp(SpHide.X);
        bool isMorphingNotch = S.SummonMode == "notch";

        double styleTopMargin = S.IslandStyle == "notch"
            ? (TopMargin * Ui.Clamp(ph * 2.0))
            : (S.IslandStyle == "compact" ? 5.0 : TopMargin);
        double expandedTopMargin = S.IslandStyle == "notch" ? (TopMargin * 2.0) : (S.IslandStyle == "compact" ? 10.0 : TopMargin * 2.0);

        double w, h, topY, radius;

        if (isMorphingNotch)
        {
            // Мягкий органичный морфинг острова в полоску чёлки (Notch Bar)
            double notchHv = Hv("summon:notch_bar");
            double notchW = 128.0 + 8.0 * notchHv;
            double notchH = 5.0 + 2.0 * notchHv;
            double notchTop = 0.0;
            double notchRadius = 2.5;

            // Базовые размеры свёрнутого состояния с учётом скрытия в полоску
            double effCollapsedW = Ui.Lerp(baseW, notchW, hide);
            double effCollapsedH = Ui.Lerp(baseH, notchH, hide);
            double effCollapsedTop = Ui.Lerp(styleTopMargin, notchTop, hide);
            double effCollapsedRadius = Ui.Lerp(baseRadius, notchRadius, hide);

            // Плавное разворачивание из текущей точки морфинга в развёрнутый дашборд
            w = effCollapsedW + (ExpandedW - effCollapsedW) * pw;
            h = effCollapsedH + (ExpandedH - effCollapsedH) * ph;
            topY = effCollapsedTop + (expandedTopMargin - effCollapsedTop) * ph;
            radius = effCollapsedRadius + (36 - effCollapsedRadius) * ph;
        }
        else
        {
            // Режим полного скрытия: плавное скольжение вверх за край экрана с мягким фейдом
            double baseHideOffset = CollapsedH + TopMargin + 24;
            topY = styleTopMargin - hide * baseHideOffset;
            w = baseW;
            h = baseH;
            radius = baseRadius;
            if (_covered && hide > 0.01)
            {
                p.Alpha = Ui.Clamp(1.0 - hide * 1.5);
            }
        }

        var rect = new Rect((canvasW - w) / 2, topY, w, h);
        _currentIslandRect = rect;
        _currentIslandRadius = radius;

        bool isNotchFlush = S.IslandStyle == "notch" && ph < 0.12 && Math.Abs(rect.Top) <= 0.5;

        // Мягкая рассеянная тень стекла
        if (Theme.GlassEnabled && hide < 0.85)
        {
            double shadowA = (1.0 - hide) * (ph > 0.1 ? 1.0 : (1.0 - hide));
            byte s1 = Theme.IsLight ? (byte)(42 * shadowA) : (byte)(20 * shadowA);
            byte s2 = Theme.IsLight ? (byte)(26 * shadowA) : (byte)(12 * shadowA);
            double sTop = isNotchFlush ? rect.Top + 6 : rect.Top + 4;
            dc.DrawRoundedRectangle(p.B(Color.FromArgb(s1, 0, 0, 0)), null,
                new Rect(rect.Left - 2, sTop, rect.Width + 4, rect.Height + 2), radius + 2, radius + 2);
            dc.DrawRoundedRectangle(p.B(Color.FromArgb(s2, 0, 0, 0)), null,
                new Rect(rect.Left - 6, sTop + 4, rect.Width + 12, rect.Height + 4), radius + 4, radius + 4);
        }

        if (isNotchFlush)
        {
            _islandGeom.Rect = new Rect(rect.Left, rect.Top - 18, rect.Width, rect.Height + 18);
            _islandGeom.RadiusX = Math.Max(radius, 14);
            _islandGeom.RadiusY = Math.Max(radius, 14);
        }
        else
        {
            _islandGeom.Rect = rect;
            _islandGeom.RadiusX = radius;
            _islandGeom.RadiusY = radius;
        }

        Brush bgBrush = Theme.GlassEnabled
            ? Theme.GetGlassGradientBrush(Theme.GlassOpacity)
            : p.B(Theme.Bg);
        double sn = Ui.Clamp(SpNotify.X);
        Pen borderPen;
        if (sn > 0.05 && ActiveNotification?.AccentColor != null)
        {
            var acc = ActiveNotification.AccentColor.Value;
            borderPen = p.P(Theme.Mix(Theme.BorderSubtle, acc, 0.5 * sn), 1.2);
        }
        else
        {
            borderPen = Theme.GlassEnabled
                ? (islandHover > 0.05
                    ? p.P(Theme.Mix(Theme.BorderSubtle, Theme.Mix(Theme.Border, Theme.Accent, 0.45), islandHover), 1)
                    : Theme.GetGlassBorderPen(1.0))
                : p.P(Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.35 * islandHover), 1);
        }

        dc.DrawGeometry(bgBrush, borderPen, _islandGeom);

        // Анатомические плавные «ушки» MacBook по бокам чёлки у верхнего края
        if (isNotchFlush)
        {
            double earR = Math.Min(rect.Height * 0.45, 5.5);
            if (earR >= 1.5)
            {
                var earPen = Theme.GlassEnabled ? Theme.GetGlassBorderPen(1.0) : p.P(Theme.BorderSubtle, 1);

                // Левое ушко
                var leftEar = new PathGeometry();
                var leftFig = new PathFigure { StartPoint = new Point(rect.Left - earR, 0), IsClosed = true, IsFilled = true };
                leftFig.Segments.Add(new LineSegment(new Point(rect.Left, 0), false));
                leftFig.Segments.Add(new LineSegment(new Point(rect.Left, earR), false));
                leftFig.Segments.Add(new ArcSegment(new Point(rect.Left - earR, 0), new Size(earR, earR), 0, false, SweepDirection.Counterclockwise, true));
                leftEar.Figures.Add(leftFig);
                dc.DrawGeometry(bgBrush, null, leftEar);

                var leftArc = new PathGeometry();
                var leftArcFig = new PathFigure { StartPoint = new Point(rect.Left, earR), IsClosed = false, IsFilled = false };
                leftArcFig.Segments.Add(new ArcSegment(new Point(rect.Left - earR, 0), new Size(earR, earR), 0, false, SweepDirection.Counterclockwise, true));
                leftArc.Figures.Add(leftArcFig);
                dc.DrawGeometry(null, earPen, leftArc);

                // Правое ушко
                var rightEar = new PathGeometry();
                var rightFig = new PathFigure { StartPoint = new Point(rect.Right, 0), IsClosed = true, IsFilled = true };
                rightFig.Segments.Add(new LineSegment(new Point(rect.Right + earR, 0), false));
                rightFig.Segments.Add(new ArcSegment(new Point(rect.Right, earR), new Size(earR, earR), 0, false, SweepDirection.Counterclockwise, true));
                rightEar.Figures.Add(rightFig);
                dc.DrawGeometry(bgBrush, null, rightEar);

                var rightArc = new PathGeometry();
                var rightArcFig = new PathFigure { StartPoint = new Point(rect.Right + earR, 0), IsClosed = false, IsFilled = false };
                rightArcFig.Segments.Add(new ArcSegment(new Point(rect.Right, earR), new Size(earR, earR), 0, false, SweepDirection.Counterclockwise, true));
                rightArc.Figures.Add(rightArcFig);
                dc.DrawGeometry(null, earPen, rightArc);
            }
        }

        // Внутренний световой скос (inner glass rim) и верхний рассеянный блик стекла
        if (Theme.GlassEnabled && radius > 6 && rect.Width > 100 && rect.Height > 20)
        {
            _innerGeom.Rect = new Rect(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2);
            _innerGeom.RadiusX = radius - 1;
            _innerGeom.RadiusY = radius - 1;
            Color rimColor = Theme.IsLight ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(34, 255, 255, 255);
            dc.DrawGeometry(null, p.P(rimColor, 0.8), _innerGeom);

            double sheenH = Math.Min(rect.Height * 0.42, 85);
            var sheenBrush = Theme.GetGlassSheenBrush();
            using (p.WithClip(_innerGeom))
            {
                dc.DrawRectangle(sheenBrush, null, new Rect(rect.Left, rect.Top, rect.Width, sheenH));
            }
        }

        if (Target == 0 && hide < 0.35)
        {
            var hitRect = S.IslandStyle == "notch"
                ? new Rect(rect.Left - 8, -8, rect.Width + 16, rect.Height + 12)
                : new Rect(rect.Left - 4, -4, rect.Width + 8, rect.Height + 8);
            AddHitForced("expand", hitRect);
        }

        // Индикатор полоски (notch bar) поверх полноэкранных окон с единым морфингом
        if (isMorphingNotch && hide > 0.35)
        {
            double notchAlpha = Ui.Clamp((hide - 0.35) / 0.65);
            double notchHv = Hv("summon:notch_bar");
            Color notchAccent = Theme.Mix(Theme.Accent, Theme.AccentHot, notchHv);

            // Деликатная светящаяся полоска-индикатор по центру чёлки
            using (p.WithOpacity(notchAlpha * (0.7 + 0.3 * notchHv)))
            {
                double barW = Math.Min(rect.Width - 20, 60);
                double barH = 2.0;
                double barY = rect.Top + (rect.Height - barH) / 2;
                dc.DrawRoundedRectangle(p.B(notchAccent), null,
                    new Rect(rect.Left + (rect.Width - barW) / 2, barY, barW, barH), 1, 1);
            }

            if (hide > 0.65)
            {
                AddHitForced("summon:notch_bar", new Rect(rect.Left - 10, 0, rect.Width + 20, Math.Max(rect.Height + 5, 12)));
            }
        }

        bool needsClip = ph < 0.98 || radius < 18;
        using var clipScope = needsClip ? p.WithClip(_islandGeom) : Scope.Empty;
        {
            double hideFade = Ui.Clamp(1.0 - hide * 2.8);
            double collapsedA = Ui.Clamp(1.0 - ph * 3.0) * hideFade;
            if (collapsedA > 0.02)
            {
                using (p.WithOpacity(collapsedA)) PaintCollapsed(p, rect);
            }
            if (ph > 0.12)
            {
                PaintExpanded(p, 1.0, canvasW, topY);
            }
        }
    }

    private void AddHitForced(string key, Rect rect)
    {
        bool old = ContentLive;
        ContentLive = true;
        AddHit(key, rect);
        ContentLive = old;
    }

    private void PaintCollapsed(Painter p, Rect rect)
    {
        double sn = SpNotify.X;
        if (sn > 0.02 && ActiveNotification != null)
        {
            double popScale = 0.88 + 0.12 * Ui.Clamp(sn);
            var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
            using (p.WithOpacity(Ui.Clamp(sn)))
            using (p.WithScale(popScale, center))
            {
                PaintNotificationBanner(p, rect, ActiveNotification);
            }
            return;
        }

        double mid = rect.Top + rect.Height / 2;
        double live = Ui.Clamp(SpLive.X);
        bool hasMedia = live > 0.02 && !string.IsNullOrEmpty(Media.Title);

        if (!hasMedia)
        {
            if (rect.Width < 145)
            {
                // Для ультра-компактного режима (Compact): компактное время и батарея
                p.Label(GetTimeStr(), new Rect(rect.Left + 10, mid - 11, 40, 22), 11.5, Theme.Text, true);
                PaintBattery(p, rect.Right - 8, mid);
                return;
            }

            double pad = S.IslandStyle == "notch" ? 16 : 20;
            var clkRect = new Rect(rect.Left + pad - 6, mid - 14, 58, 28);
            double clkHv = Hv("open:calendar");
            if (clkHv > 0.01)
            {
                p.Card(clkRect, Theme.Mix(Theme.Surface2, Theme.Surface3, clkHv), 8, Theme.Mix(Theme.BorderSubtle, Theme.Border, clkHv));
            }
            p.Label(GetTimeStr(), new Rect(rect.Left + pad, mid - 14, 52, 28), S.IslandStyle == "notch" ? 12.5 : 13.5, Theme.Text, true);
            AddHitForced("open:calendar", clkRect);
            PaintBattery(p, rect.Right - pad, mid);

            if (Sections.Ai.IsGenerating && rect.Width >= 170)
            {
                double ts = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                byte dotAlpha = (byte)(150 + 105 * Math.Sin(ts * 5.0));
                var dotColor = Color.FromArgb(dotAlpha, Theme.AccentHot.R, Theme.AccentHot.G, Theme.AccentHot.B);

                string aiLabel = Loc.T("✨ ИИ думает…", "✨ AI thinking…", "✨ Yapay zeka…");
                double tw = p.TextWidth(aiLabel, 11, true);
                double pillW = 22 + tw + 14;
                double pillX = rect.Left + (S.IslandStyle == "notch" ? 68 : 76);
                var pillRect = new Rect(pillX, mid - 13, pillW, 26);

                p.Card(pillRect, Theme.Mix(Theme.Surface2, Theme.Accent, 0.25), 13, Theme.Accent);
                p.Circle(new Point(pillRect.Left + 12, mid), 3.5, dotColor);
                p.Label(aiLabel, new Rect(pillRect.Left + 20, mid - 11, tw + 6, 22), 11, Colors.White, true);
                AddHitForced("open:ai", pillRect);
            }
            else
            {
                var w = Sections.Weather;
                if (w.Data != null && rect.Width >= 190)
                {
                    var d = w.Data;
                    string sign = d.Temp > 0 ? "+" : "";
                    string tempStr = $"{sign}{Math.Round(d.Temp)}°";
                    double tw = p.TextWidth(tempStr, 12, true);

                    double pillW = 20 + tw + 18;
                    double pillX = rect.Left + (S.IslandStyle == "notch" ? 68 : 76);
                    var pillRect = new Rect(pillX, mid - 13, pillW, 26);

                    double hv = Hv("open:weather");
                    if (hv > 0.01)
                    {
                        p.Card(pillRect, Theme.Mix(Theme.Surface2, Theme.Surface3, hv), 13, Theme.Mix(Theme.BorderSubtle, Theme.Border, hv));
                    }

                    p.WeatherIcon(d.Code, new Point(pillRect.Left + 14, mid), 7.5);
                    p.Label(tempStr, new Rect(pillRect.Left + 26, mid - 11, tw + 6, 22), 12, Theme.Text, true);

                    AddHitForced("open:weather", pillRect);
                }
            }
            return;
        }

        // --- ЖИВОЙ МИНИ-ПЛЕЕР В СВЁРНУТОМ ОСТРОВКЕ ---
        // 1. Мини-обложка альбома слева (22x22, r=6)
        var coverRect = new Rect(rect.Left + 14, mid - 11, 22, 22);
        if (CoverBrush != null)
        {
            p.Dc.DrawRoundedRectangle(CoverBrush, p.P(Theme.BorderSubtle, 1), coverRect, 6, 6);
        }
        else
        {
            p.Card(coverRect, Theme.Surface2, 6, Theme.BorderSubtle);
            p.MediaIcon("play", new Point(coverRect.Left + 11, mid), 7, 0.7);
        }

        // 2. Часы рядом с обложкой (клик открывает Календарь)
        var clkMediaRect = new Rect(coverRect.Right + 4, mid - 12, 46, 24);
        double clkMedHv = Hv("open:calendar");
        if (clkMedHv > 0.01)
        {
            p.Card(clkMediaRect, Theme.Mix(Theme.Surface2, Theme.Surface3, clkMedHv), 6, Theme.Mix(Theme.BorderSubtle, Theme.Border, clkMedHv));
        }
        p.Label(GetTimeStr(), new Rect(coverRect.Right + 8, mid - 12, 42, 24), 11.5, Theme.Muted, true);
        AddHitForced("open:calendar", clkMediaRect);

        // 3. Батарея справа
        double battLeft = PaintBattery(p, rect.Right - 16, mid);

        // 4. Плавный органичный эквалайзер в стиле Apple Dynamic Island
        double eqX = battLeft - 26;
        double t = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        double playSpring = Ui.Clamp(Nsp("play").X);
        double[] restingH = { 5.0, 9.0, 13.5, 7.0 };
        for (int i = 0; i < 4; i++)
        {
            double w1 = Math.Sin(t * 4.2 + i * 0.95);
            double w2 = Math.Sin(t * 6.8 - i * 1.3);
            double w3 = Math.Cos(t * 2.5 + i * 0.45);

            double blend = 0.5 + 0.30 * w1 + 0.14 * w2 + 0.06 * w3;
            blend = Math.Clamp(blend, 0.0, 1.0);

            double dynamicVal = Math.Pow(blend, 1.25);
            double liveH = 5.0 + 10.5 * dynamicVal;

            double hgt = restingH[i] + (liveH - restingH[i]) * playSpring;
            p.Card(new Rect(eqX + i * 5.2, mid - hgt / 2, 2.6, hgt), Theme.Accent, 1.3);
        }

        // 5. Трек по центру или мини-кнопки управления при наведении
        double trackLeft = coverRect.Right + 52;
        double trackRight = eqX - 10;
        double trackW = Math.Max(0, trackRight - trackLeft);

        bool isHovered = _hoverKey != null && Target == 0;
        if (isHovered && trackW > 80)
        {
            double centerMidX = (trackLeft + trackRight) / 2;
            var prevCenter = new Point(centerMidX - 32, mid);
            var playCenter = new Point(centerMidX, mid);
            var nextCenter = new Point(centerMidX + 32, mid);

            var prevRect = new Rect(prevCenter.X - 12, mid - 12, 24, 24);
            var playRect = new Rect(playCenter.X - 13, mid - 13, 26, 26);
            var nextRect = new Rect(nextCenter.X - 12, mid - 12, 24, 24);

            // Кнопка Prev
            using (p.Pressable("media:prev", prevRect))
            {
                double hv = Hv("media:prev");
                if (hv > 0.01)
                {
                    double rPrev = 11.5 + 1.0 * hv;
                    p.Circle(prevCenter, rPrev, Color.FromArgb((byte)(55 * hv), 255, 255, 255));
                    p.Dc.DrawEllipse(null, p.P(Color.FromArgb((byte)(50 * hv), 255, 255, 255), 1), prevCenter, rPrev, rPrev);
                }
                p.MediaIcon("prev", prevCenter, 8.5, 0.75 + 0.25 * hv);
            }
            AddHitForced("media:prev", prevRect);

            // Кнопка Play/Pause (плавный cross-fade морфинг через пружину play)
            using (p.Pressable("media:play", playRect))
            {
                double hv = Hv("media:play");
                double rPlay = 12.5 + 1.2 * hv;
                Color playBg = Theme.Mix(Theme.Accent, Theme.AccentHot, hv);
                p.Circle(playCenter, rPlay, playBg);
                if (hv > 0.05)
                {
                    p.Circle(playCenter, rPlay + 2.2, Color.FromArgb((byte)(45 * hv), Theme.Accent.R, Theme.Accent.G, Theme.Accent.B));
                }
                double vPlay = Ui.Clamp(Nsp("play").X);
                p.MediaIcon("play", playCenter, 9.5, 1 - vPlay);
                p.MediaIcon("pause", playCenter, 9.5, vPlay);
            }
            AddHitForced("media:play", playRect);

            // Кнопка Next
            using (p.Pressable("media:next", nextRect))
            {
                double hv = Hv("media:next");
                if (hv > 0.01)
                {
                    double rNext = 11.5 + 1.0 * hv;
                    p.Circle(nextCenter, rNext, Color.FromArgb((byte)(55 * hv), 255, 255, 255));
                    p.Dc.DrawEllipse(null, p.P(Color.FromArgb((byte)(50 * hv), 255, 255, 255), 1), nextCenter, rNext, rNext);
                }
                p.MediaIcon("next", nextCenter, 8.5, 0.75 + 0.25 * hv);
            }
            AddHitForced("media:next", nextRect);
        }
        else if (trackW > 20)
        {
            string title = Media.Title;
            if (!string.IsNullOrEmpty(Media.Artist)) title = $"{title} · {Media.Artist}";
            p.Label(title, new Rect(trackLeft, mid - 12, trackW, 24), 11.5, Theme.Text, true, HAlign.Center);
        }
    }

    /// <summary>Рисует процент и значок, возвращает левый край.</summary>
    private double PaintBattery(Painter p, double right, double cy)
    {
        var (pct, charging) = Battery;
        string label = pct == null ? "—" : $"{pct}%";
        double textW = p.TextWidth(label, 12, true);
        p.Label(label, new Rect(right - textW - 2, cy - 12, textW + 4, 24), 12, Theme.Text, true, HAlign.Right);
        if (pct == null) return right - textW - 2;

        double x0 = right - textW - 10;
        var body = new Rect(x0 - 24, cy - 6, 24, 12);
        Color color = charging ? Theme.Green : pct <= 10 ? Theme.Red : pct <= 20 ? Theme.Yellow : Theme.Text;
        p.Dc.DrawRoundedRectangle(null, p.P(Theme.Muted, 1), body, 4, 4);
        double inner = (body.Width - 4) * pct.Value / 100;
        if (inner >= 1) p.Card(new Rect(body.Left + 2, body.Top + 2, inner, body.Height - 4), color, 2);
        p.Card(new Rect(x0 + 1.5, cy - 2.5, 2, 5), Theme.Muted, 1);
        return body.Left;
    }

    public void OnGlobalMouseMove(int screenX, int screenY)
    {
        double dx = screenX - _globalMousePos.X;
        double dy = screenY - _globalMousePos.Y;
        if (dx * dx + dy * dy < 16.0) return; // Меньше 4 пикселей сдвига мыши — игнорируем
        _globalMousePos = new Point(screenX, screenY);
        if (Target > 0.05 && S.RobotCompanion)
        {
            StartFrames();
        }
    }

    public void PetRobot()
    {
        var phrase = PickNextBotPhrase();
        _botEmotion = phrase.Emotion;
        _botEmotionTimer = 2.8;

        _botBubbleText = Loc.T(phrase.Ru, phrase.En, phrase.Tr);
        _botBubbleTime = Math.Max(2.8, 1.8 + _botBubbleText.Length * 0.045);

        SpBotHop.V = -85.0;
        SpBotHappy.Target = 1.0;
        _botHappyTimer = 2.8;

        Kick();
    }

    private void UpdateRobotEyePhysics(double dt)
    {
        _botAnimPhase = (_botAnimPhase + dt * 2.8) % (Math.PI * 2000.0);

        double dx = _globalMousePos.X - _botCenterScreen.X;
        double dy = _globalMousePos.Y - _botCenterScreen.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double maxEyeOffset = 3.0;
        double targetX = 0;
        double targetY = 0;
        if (dist > 3.0)
        {
            double factor = Math.Min(dist * 0.006, maxEyeOffset);
            targetX = (dx / dist) * factor;
            targetY = (dy / dist) * factor;
        }
        SpBotEyeX.Target = targetX;
        SpBotEyeY.Target = targetY;
        SpBotEyeX.Step(dt, 320, 23);
        SpBotEyeY.Step(dt, 320, 23);
        SpBotHappy.Step(dt, 300, 26);
        SpBotHop.Step(dt, 360, 24);

        // Таймер моргания (не моргаем во время активных спец-эмоций)
        if (_botEmotionTimer <= 0)
        {
            _botBlinkTimer -= dt;
            if (_botBlinkTimer <= 0)
            {
                _botBlinkState = 1.0;
                _botBlinkTimer = 3.5 + Random.Shared.NextDouble() * 3.0;
            }
            else if (_botBlinkState > 0)
            {
                _botBlinkState = Math.Max(0.0, _botBlinkState - dt * 8.0);
            }
        }
        else
        {
            _botBlinkState = 0.0;
        }

        // Таймер эмоции
        if (_botEmotionTimer > 0)
        {
            _botEmotionTimer -= dt;
            if (_botEmotionTimer <= 0)
            {
                _botEmotion = "normal";
            }
        }

        // Таймер реплики
        if (_botBubbleTime > 0)
        {
            _botBubbleTime -= dt;
            if (_botBubbleTime <= 0)
            {
                _botBubbleText = "";
            }
        }

        // Таймер радости от поглаживания
        if (_botHappyTimer > 0)
        {
            _botHappyTimer -= dt;
            if (_botHappyTimer <= 0 && Hv("robot:pet") <= 0.4)
            {
                SpBotHappy.Target = 0.0;
            }
        }
    }

    private void PaintRobot(Painter p, Rect pill)
    {
        if (S.RobotRoaming)
        {
            double dockHv = Hv("robot:dock");
            p.Card(pill, Theme.Mix(Theme.Surface, Theme.Surface2, 0.45 + 0.35 * dockHv), 14,
                Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.3 + 0.5 * dockHv));
            p.Label("🚀 " + Loc.T("Аэро гуляет", "Aero roaming", "Aero geziyor"),
                new Rect(pill.Left + 6, pill.Top + 6, pill.Width - 12, 16), 10,
                dockHv > 0.05 ? Theme.AccentHot : Theme.Muted, true, HAlign.Center);
            AddHit("robot:dock", pill);
            return;
        }

        double botHv = Hv("robot:pet");
        p.Card(pill, Theme.Mix(Theme.Surface, Theme.Surface2, 0.45 + 0.45 * botHv), 14,
            Theme.Mix(Theme.BorderSubtle, Theme.Accent, botHv * 0.5));
        AddHit("robot:pet", new Rect(pill.Left, pill.Top, pill.Width - 24, pill.Height));

        // Кнопка быстрого запуска на рабочий стол (маленькая ракета справа)
        var launchBtn = new Rect(pill.Right - 22, pill.Top + (pill.Height - 18) / 2, 18, 18);
        double lnHv = Hv("robot:launch");
        p.Circle(new Point(launchBtn.Left + 9, launchBtn.Top + 9), 8,
            Theme.Mix(Theme.Surface3, Theme.Accent, 0.2 + 0.4 * lnHv));
        p.Label("🚀", launchBtn, 9.0, lnHv > 0.05 ? Colors.White : Theme.Muted, true, HAlign.Center);
        AddHit("robot:launch", launchBtn);

        // Всплывающее облачко реплики слева от капсулы
        if (_botBubbleTime > 0 && !string.IsNullOrEmpty(_botBubbleText))
        {
            double bubbleA = Ui.Clamp(_botBubbleTime * 2.2);
            double bubbleW = p.TextWidth(_botBubbleText, 10, true) + 20;
            double bubbleX = pill.Left - bubbleW - 8;
            double bubbleY = pill.Top + (pill.Height - 22) / 2;
            var bubbleRect = new Rect(bubbleX, bubbleY, bubbleW, 22);

            using (p.WithOpacity(bubbleA))
            {
                p.Card(bubbleRect, Theme.Mix(Theme.Surface2, Theme.Surface3, 0.95), 11,
                    Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.45));

                // Маленький хвостик бабла к роботу
                var tailFig = new PathFigure { StartPoint = new Point(bubbleRect.Right, bubbleY + 7), IsClosed = true, IsFilled = true };
                tailFig.Segments.Add(new LineSegment(new Point(bubbleRect.Right + 5, bubbleY + 11), false));
                tailFig.Segments.Add(new LineSegment(new Point(bubbleRect.Right, bubbleY + 15), false));
                var tailGeom = new PathGeometry();
                tailGeom.Figures.Add(tailFig);
                p.Dc.DrawGeometry(p.B(Theme.Mix(Theme.Surface2, Theme.Surface3, 0.95)), null, tailGeom);

                p.Label(_botBubbleText, bubbleRect, 10, Theme.Text, true, HAlign.Center);
            }
        }

        double cx = pill.Left + 20;
        double cy = pill.Top + pill.Height / 2;
        double floatY = Math.Sin(_botAnimPhase) * 1.2 + SpBotHop.X;
        double rcx = cx;
        double rcy = cy + floatY;

        try
        {
            Point screenPt = PointToScreen(new Point(cx, cy));
            _botCenterScreen = screenPt;
        }
        catch { }

        double happy = SpBotHappy.X;
        if (botHv > 0.4 && _botHappyTimer <= 0) SpBotHappy.Target = 0.7;

        // 1. Антеннка
        p.Line(new Point(rcx, rcy - 8), new Point(rcx, rcy - 12), Theme.BorderSubtle, 1.2);
        Color tipColor = (happy > 0.3 || _botEmotionTimer > 0) ? Theme.AccentHot : Theme.Accent;
        p.Circle(new Point(rcx, rcy - 12.5), 1.5, tipColor);

        // Парящее сердечко при эмоции Love
        if (_botEmotion == "love" && _botEmotionTimer > 0)
        {
            double heartUp = (1.0 - _botEmotionTimer / 2.4) * 10.0;
            double heartA = Ui.Clamp(_botEmotionTimer * 1.6);
            using (p.WithOpacity(heartA))
            {
                p.Label("♥", new Rect(rcx - 7, rcy - 23 - heartUp, 14, 14), 11, Theme.Red, true, HAlign.Center);
            }
        }

        // 2. Ушки по бокам
        Color earColor = Theme.Mix(Theme.Surface3, Theme.Accent, 0.4);
        p.Card(new Rect(rcx - 13.5, rcy - 3.5, 2.5, 7), earColor, 1.2);
        p.Card(new Rect(rcx + 11.0, rcy - 3.5, 2.5, 7), earColor, 1.2);

        // 3. Корпус головы (белая керамика / тёмный титан)
        var headRect = new Rect(rcx - 11.5, rcy - 8, 23, 16);
        Color headBg = Theme.IsLight ? Color.FromRgb(244, 246, 252) : Color.FromRgb(28, 31, 42);
        Color headBorder = Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.35);
        p.Card(headRect, headBg, 8, headBorder);
        p.Card(new Rect(headRect.Left + 4, headRect.Top + 1, headRect.Width - 8, 3.5), Color.FromArgb(45, 255, 255, 255), 1.8);

        // 4. Тёмный глянцевый OLED-визор
        var visorRect = new Rect(rcx - 8.5, rcy - 5, 17, 10);
        Color visorBg = Color.FromRgb(10, 12, 16);
        Color visorBorder = Color.FromArgb(45, 255, 255, 255);
        p.Card(visorRect, visorBg, 5, visorBorder);
        p.Line(new Point(visorRect.Left + 2, visorRect.Top + 1.2),
               new Point(visorRect.Right - 2, visorRect.Top + 1.2),
               Color.FromArgb(70, 255, 255, 255), 0.8);

        // 5. Глазки и эмоции
        double ex = SpBotEyeX.X;
        double ey = SpBotEyeY.X;
        double blink = _botBlinkState;
        Color eyeColor = (happy > 0.3 || _botEmotionTimer > 0) ? Theme.AccentHot : Theme.Mix(Theme.Accent, Theme.AccentHot, 0.7);

        double lx = rcx - 4.0 + ex;
        double rx = rcx + 4.0 + ex;
        double ly = rcy + ey;
        double ry = ly;

        if (_botEmotion == "love" && _botEmotionTimer > 0)
        {
            // Глазки-сердечки
            p.Label("♥", new Rect(lx - 4, ly - 5.5, 8, 11), 9, Theme.Red, true, HAlign.Center);
            p.Label("♥", new Rect(rx - 4, ry - 5.5, 8, 11), 9, Theme.Red, true, HAlign.Center);
            p.Circle(new Point(lx, ly + 3.2), 1.2, Color.FromArgb(200, 255, 120, 160));
            p.Circle(new Point(rx, ry + 3.2), 1.2, Color.FromArgb(200, 255, 120, 160));
        }
        else if (_botEmotion == "wink" && _botEmotionTimer > 0)
        {
            // Подмигивание: один улыбается, второй звёздочка
            p.Polyline(eyeColor, 1.6, new Point(lx - 2.5, ly + 0.8), new Point(lx, ly - 1.8), new Point(lx + 2.5, ly + 0.8));
            p.Label("★", new Rect(rx - 4, ry - 5, 8, 10), 9.0, Theme.Yellow, true, HAlign.Center);
            p.Circle(new Point(lx, ly + 3.0), 1.2, Color.FromArgb(180, 255, 120, 160));
        }
        else if (_botEmotion == "stars" && _botEmotionTimer > 0)
        {
            // Звёздные глазки от восторга
            p.Label("★", new Rect(lx - 4, ly - 5.5, 8, 11), 9.5, Theme.Yellow, true, HAlign.Center);
            p.Label("★", new Rect(rx - 4, ry - 5.5, 8, 11), 9.5, Theme.Yellow, true, HAlign.Center);
            p.Circle(new Point(lx, ly + 3.0), 1.2, Color.FromArgb(180, 255, 200, 80));
            p.Circle(new Point(rx, ry + 3.0), 1.2, Color.FromArgb(180, 255, 200, 80));
        }
        else if (_botEmotion == "cool" && _botEmotionTimer > 0)
        {
            // Крутые солнцезащитные очки
            var leftLens = new Rect(rcx - 7.5, rcy - 4, 6.5, 5);
            var rightLens = new Rect(rcx + 1.0, rcy - 4, 6.5, 5);
            p.Card(leftLens, Color.FromRgb(15, 15, 20), 1.5, Theme.AccentHot);
            p.Card(rightLens, Color.FromRgb(15, 15, 20), 1.5, Theme.AccentHot);
            p.Line(new Point(rcx - 1, rcy - 2), new Point(rcx + 1, rcy - 2), Theme.AccentHot, 1.2);
            p.Line(new Point(leftLens.Left + 1, leftLens.Top + 1), new Point(leftLens.Left + 3, leftLens.Bottom - 1), Colors.White, 0.9);
            p.Line(new Point(rightLens.Left + 1, rightLens.Top + 1), new Point(rightLens.Left + 3, rightLens.Bottom - 1), Colors.White, 0.9);
        }
        else if (_botEmotion == "surprised" && _botEmotionTimer > 0)
        {
            // Широкие удивлённые круглые глаза O O
            p.Dc.DrawEllipse(null, p.P(Theme.AccentHot, 1.4), new Point(lx, ly), 3.0, 3.0);
            p.Dc.DrawEllipse(null, p.P(Theme.AccentHot, 1.4), new Point(rx, ry), 3.0, 3.0);
            p.Circle(new Point(lx, ly), 1.2, Theme.Text);
            p.Circle(new Point(rx, ry), 1.2, Theme.Text);
        }
        else if (happy > 0.3)
        {
            // Радостные глазки-улыбки ^ ^
            p.Polyline(eyeColor, 1.6, new Point(lx - 2.5, ly + 0.8), new Point(lx, ly - 1.8), new Point(lx + 2.5, ly + 0.8));
            p.Polyline(eyeColor, 1.6, new Point(rx - 2.5, ry + 0.8), new Point(rx, ry - 1.8), new Point(rx + 2.5, ry + 0.8));
            p.Circle(new Point(lx, ly + 3.0), 1.2, Color.FromArgb(180, 255, 120, 160));
            p.Circle(new Point(rx, ry + 3.0), 1.2, Color.FromArgb(180, 255, 120, 160));
        }
        else if (blink > 0.2)
        {
            // Моргание
            p.Line(new Point(lx - 2.5, ly), new Point(lx + 2.5, ly), eyeColor, 1.5);
            p.Line(new Point(rx - 2.5, ry), new Point(rx + 2.5, ry), eyeColor, 1.5);
        }
        else if (Sections.Ai.IsGenerating)
        {
            // ИИ думает: высокотехнологичный сканирующий радар
            double scan = Math.Sin(Clock.Now * 10.0) * 3.0;
            p.Card(new Rect(rcx + scan - 2.5, rcy - 1.5, 5, 3), Theme.AccentHot, 1.5);
        }
        else
        {
            // Обычный взгляд, следящий за курсором
            p.Card(new Rect(lx - 1.6, ly - 2.2, 3.2, 4.4), eyeColor, 1.6);
            p.Card(new Rect(rx - 1.6, ry - 2.2, 3.2, 4.4), eyeColor, 1.6);
            p.Circle(new Point(lx - 0.5, ly - 1.2), 0.7, Colors.White);
            p.Circle(new Point(rx - 0.5, ry - 1.2), 0.7, Colors.White);
        }

        // 6. Текстовый бейдж настроения справа от робота
        string mood = _botEmotion switch
        {
            "love" => "♥",
            "wink" => "★",
            "stars" => "WOW",
            "cool" => "COOL",
            "surprised" => "O_O",
            _ => (happy > 0.3 ? "♥" : (Sections.Ai.IsGenerating ? "AI" : Loc.T("АЭРО", "AERO", "AERO")))
        };
        Color moodColor = _botEmotion switch
        {
            "love" => Theme.Red,
            "stars" or "wink" => Theme.Yellow,
            "cool" or "surprised" => Theme.AccentHot,
            _ => (happy > 0.3 ? Theme.Red : (Sections.Ai.IsGenerating ? Theme.AccentHot : Theme.Muted))
        };
        p.Label(mood, new Rect(pill.Left + 31, pill.Top + 6, pill.Width - 34, 16), 8.5, moodColor, true, HAlign.Center);
    }

    private void PaintNotificationBanner(Painter p, Rect rect, NotificationItem notif)
    {
        double mid = rect.Top + rect.Height / 2;

        // Реакция на нажатие по всей плашке для раскрытия острова или выполнения OnClick
        AddHitForced("open:notify", rect);

        // 1. Иконка / Бейдж приложения слева
        var iconRect = new Rect(rect.Left + 16, mid - 18, 36, 36);
        if (notif.UseCoverArt && CoverBrush != null)
        {
            p.Cover(iconRect, 10);
        }
        else
        {
            var acc = notif.AccentColor ?? Theme.Accent;
            p.Card(iconRect, Theme.Mix(Theme.Surface2, acc, 0.22), 10, Theme.BorderSubtle);
            string symbol = notif.IconSymbol ?? (string.IsNullOrEmpty(notif.App) ? "🔔" : notif.App[..1].ToUpperInvariant());
            p.Label(symbol, iconRect, 16, acc, true, HAlign.Center);
        }

        // 2. Крестик закрытия справа (сверху в стеке хитов)
        var closeRect = new Rect(rect.Right - 36, mid - 12, 24, 24);
        var pressClose = p.PressRect(this, "dismiss:notify", closeRect, 1.5);
        p.Card(pressClose, Theme.Mix(Theme.Surface, Theme.Surface2, Hv("dismiss:notify")), 12);
        p.Label("✕", pressClose, 10, Theme.Muted, true, HAlign.Center);
        AddHitForced("dismiss:notify", closeRect);

        // 3. Текстовая информация
        double textX = iconRect.Right + 12;
        double textW = closeRect.Left - textX - 8;
        if (textW > 20)
        {
            string appName = string.IsNullOrEmpty(notif.App) ? Loc.T("УВЕДОМЛЕНИЕ", "NOTIFICATION", "BİLDİRİM") : notif.App.ToUpperInvariant();
            var appColor = notif.AccentColor ?? Theme.Muted;
            if (string.IsNullOrEmpty(notif.Body))
            {
                p.Label($"{appName} · {Loc.T("сейчас", "now", "şimdi")}", new Rect(textX, rect.Top + 16, textW, 14), 9.5, appColor, true);
                p.Label(notif.Title, new Rect(textX, rect.Top + 32, textW, 20), 13, Theme.Text, true);
            }
            else
            {
                p.Label($"{appName} · {Loc.T("сейчас", "now", "şimdi")}", new Rect(textX, rect.Top + 12, textW, 14), 9.5, appColor, true);
                p.Label(notif.Title, new Rect(textX, rect.Top + 27, textW, 19), 12.5, Theme.Text, true);
                p.Label(notif.Body, new Rect(textX, rect.Top + 46, textW, 17), 11, Theme.Mix(Theme.Muted, Theme.Text, 0.5));
            }
        }
    }

    private void PaintExpanded(Painter p, double a, double canvasW, double topY)
    {
        double W = ExpandedW, H = ExpandedH;
        double fx = (canvasW - W) / 2;                   // левый край финального размера
        double oy = topY + (1 - a) * 8;                  // органичный плавный подъём
        Live = Target == 1 && a > 0.85;
        ContentLive = Live;

        // ---------- 1. Apple Header (Стильная лаконичная строка статуса / Календарь) ----------
        string timeStr = GetTimeStr();
        string dateStr = GetDateStr();
        double tw = p.TextWidth(timeStr, 15, true);
        double dw = p.TextWidth($"•  {dateStr}", 12, false);
        double datePillW = tw + dw + 24;
        var datePill = new Rect(fx + 22, oy + 13, datePillW, 30);

        double calHv = Hv("open:calendar");
        bool isCalActive = CurrentTool == "calendar";
        if (calHv > 0.01 || isCalActive)
        {
            Color pillBg = isCalActive
                ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.25)
                : Theme.Mix(Colors.Transparent, Theme.Surface2, calHv * 0.8);
            Color pillBorder = isCalActive
                ? Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.5)
                : Theme.Mix(Colors.Transparent, Theme.BorderSubtle, calHv);
            p.Card(datePill, pillBg, 14, pillBorder);
        }

        p.Label(timeStr, new Rect(datePill.Left + 10, oy + 16, tw + 4, 22), 15, Theme.Text, true);
        p.Label($"•  {dateStr}", new Rect(datePill.Left + 14 + tw, oy + 18, dw + 6, 20), 12,
            isCalActive ? Theme.AccentHot : Theme.Mix(Theme.Muted, Theme.Text, calHv * 0.7));

        AddHit("open:calendar", datePill);

        // Индикатор подраздела и кнопка быстрого возврата в Управление
        if (CurrentTool != "home")
        {
            var curSec = Sections.ByKey(CurrentTool);
            string breadcrumb = $"‹ {Loc.T("Назад", "Back", "Geri")}  •  {curSec.Icon} {curSec.Title}";
            double bcW = p.TextWidth(breadcrumb, 11, true) + 24;
            var bcRect = new Rect(datePill.Right + 12, oy + 14, bcW, 28);
            double hot = Hv("back:home");
            p.Card(bcRect, Theme.Mix(Theme.Surface2, Theme.Surface3, 0.4 + 0.4 * hot), 14, Theme.BorderSubtle);
            p.Label(breadcrumb, bcRect, 10.5, Theme.Mix(Theme.Muted, Theme.AccentHot, 0.5 + 0.5 * hot), true, HAlign.Center);
            AddHit("back:home", bcRect);
        }

        // Кнопки управления окном и батарея справа
        foreach (var (key, cx) in new[] { ("close", fx + W - 36), ("collapse", fx + W - 72) })
        {
            var c = new Point(cx, oy + 28);
            var circle = new Rect(cx - 14, oy + 14, 28, 28);
            using (p.Pressable(key, circle))
            {
                p.Circle(c, 14, Theme.Mix(Theme.Surface2, Theme.Surface3, Hv(key)));
                if (key == "close")
                {
                    p.Line(new Point(c.X - 4, c.Y - 4), new Point(c.X + 4, c.Y + 4), Theme.Text, 1.8);
                    p.Line(new Point(c.X - 4, c.Y + 4), new Point(c.X + 4, c.Y - 4), Theme.Text, 1.8);
                }
                else
                {
                    p.Polyline(Theme.Text, 1.8, new Point(c.X - 4, c.Y + 2), new Point(c.X, c.Y - 2), new Point(c.X + 4, c.Y + 2));
                }
            }
            AddHit(key, circle);
        }

        var chip = new Rect(fx + W - 176, oy + 14, 88, 28);
        p.Card(chip, Theme.Surface, 14, Theme.BorderSubtle);
        PaintBattery(p, chip.Right - 14, chip.Top + chip.Height / 2);

        if (S.RobotCompanion)
        {
            double botW = S.RobotRoaming ? 104 : 80;
            var botPill = new Rect(fx + W - 176 - 10 - botW, oy + 14, botW, 28);
            PaintRobot(p, botPill);
        }

        // Тонкая разделительная черта Apple
        p.Line(new Point(fx + 28, oy + 48), new Point(fx + W - 28, oy + 48), Theme.BorderSubtle, 1);

        // Рабочая зона и нижний док
        PaintSection(p, a, fx, oy);
        PaintDock(p, fx, oy + H - 34);
    }

    private void PaintSection(Painter p, double a, double fx, double oy)
    {
        // Просторная рабочая область в стиле Apple с чистым отступом до нижнего дока
        var area = new Rect(fx + 28, oy + 58, ExpandedW - 56, ExpandedH - 134);
        double t = SpTab.X;
        bool live = Live;
        var center = new Point(area.Left + area.Width / 2.0, area.Top + area.Height / 2.0);

        double prevAlpha = 1.0 - t * 1.8;
        if (prevAlpha > 0.02 && _prevTab != null)
        {
            ContentLive = false;
            double prevScale = 1.0 - 0.02 * t;
            using (p.WithOpacity(prevAlpha))
            using (p.WithScale(prevScale, center))
            using (p.WithTranslate(-_tabDir * 20 * t, 0))
                Sections.ByKey(_prevTab).Paint(this, p, area);
        }

        double currAlpha = Ui.Clamp(t * 1.3);
        if (currAlpha > 0.02)
        {
            if (t < 0.99)
            {
                double curScale = 0.98 + 0.02 * t;
                using (p.WithOpacity(currAlpha))
                using (p.WithScale(curScale, center))
                using (p.WithTranslate(_tabDir * 20 * (1 - t), 0))
                {
                    ContentLive = live && t > 0.9;
                    Sections.ByKey(CurrentTool).Paint(this, p, area);
                }
            }
            else
            {
                ContentLive = live;
                Sections.ByKey(CurrentTool).Paint(this, p, area);
            }
        }
        ContentLive = live;
    }

    private void PaintDock(Painter p, double fx, double cy)
    {
        var layout = DockLayout();
        double left = fx + layout[0].X - 8;
        double right = fx + layout[^1].X + layout[^1].W + 8;
        var dockRect = new Rect(left, cy - 18, right - left, 36);
        p.Card(dockRect, Theme.Mix(Theme.Surface, Theme.Surface2, 0.6), 18, Theme.BorderSubtle);

        if (SpPillW.X > 1)
        {
            var pillRect = new Rect(fx + SpPillX.X, cy - 14, Math.Max(0, SpPillW.X), 28);
            pillRect = p.PressRect(this, $"tab:{CurrentTool}", pillRect, 1.8);
            p.Card(pillRect, Theme.Accent, 14);
        }

        foreach (var (key, x, w) in layout)
        {
            var rect = new Rect(fx + x, cy - 14, w, 28);
            bool selected = key == CurrentTool;
            double hv = Hv($"tab:{key}");
            if (!selected && hv > 0.01)
            {
                p.Card(rect, Theme.Mix(Colors.Transparent, Theme.Surface2, hv * 0.75), 14);
            }
            var itemRect = p.PressRect(this, $"tab:{key}", rect, 1.5);
            var color = selected ? Colors.White : Theme.Mix(Theme.Muted, Theme.Text, hv);
            var sec = Sections.ByKey(key);
            p.Label(sec.DockLabel, itemRect, 11, color, true, HAlign.Center);
            AddHit($"tab:{key}", rect);
        }
    }

    // ---------- события мыши ----------

    private string? HitAt(Point pos)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Rect.Contains(pos)) return _hits[i].Key;
        return null;
    }

    private void SetHover(string? key)
    {
        if (key == _hoverKey) return;
        string? prev = _hoverKey;
        _hoverKey = key;

        // Остров стабильно подсвечен, если курсор над самим островом ИЛИ над его элементами (кнопки плеера, погода, календарь, ИИ)
        bool isIslandHovered = Target == 0 && key != null && (key == "expand" || key.StartsWith("media:") || key == "open:weather" || key == "open:calendar" || key == "open:ai");
        SpHover.Target = isIslandHovered ? 1.0 : 0.0;

        if (key != null && !_hs.ContainsKey(key))
        {
            var s = _hs[key] = new Spring(0);
            s.Target = 1.0;
        }
        foreach (var kv in _hs) kv.Value.Target = kv.Key == key ? 1.0 : 0.0;
        Cursor = key != null ? Cursors.Hand : Cursors.Arrow;

        if (key != null && (key == "open:notify" || key == "dismiss:notify"))
        {
            _notifyTimer.Stop(); // При наведении на баннер удерживаем его
        }
        else if (key == null && prev != null && (prev == "open:notify" || prev == "dismiss:notify") && SpNotify.Target == 1.0)
        {
            _notifyTimer.Stop();
            _notifyTimer.Interval = TimeSpan.FromSeconds(2.5); // После ухода мыши даем еще 2.5 сек
            _notifyTimer.Start();
        }

        UpdateDim();
        StartFrames();
        InvalidateVisual();
    }

    private void SetPress(string? key)
    {
        _pressKey = key;
        foreach (var kv in _ps) kv.Value.Target = kv.Key == key ? 1.0 : 0.0;
        StartFrames();
    }

    private Point _lastMovePos;
    private long _lastMoveTicks;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_dragKey != null)
        {
            // При перетаскивании ползунков (звук/яркость) — нулевая задержка
            try { _globalMousePos = PointToScreen(pos); } catch { }
            Sections.ByKey(CurrentTool).Drag(this, _dragKey, pos);
            SetHover(HitAt(pos));
            return;
        }

        long now = Environment.TickCount64;
        double dx = pos.X - _lastMovePos.X;
        double dy = pos.Y - _lastMovePos.Y;
        // Троттлинг 1000 Гц мыши: отсекаем микродвижения меньше 2 пикселей чаще чем раз в 8 мс (~125 Гц)
        if (dx * dx + dy * dy < 4.0 && now - _lastMoveTicks < 8) return;
        _lastMovePos = pos;
        _lastMoveTicks = now;

        try { _globalMousePos = PointToScreen(pos); } catch { }
        SetHover(HitAt(pos));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);
        string? key = HitAt(pos);
        if (key != null && key.StartsWith("slider:"))
        {
            _dragKey = key;
            CaptureMouse();
            Sections.ByKey(CurrentTool).Drag(this, key, pos);
        }
        SetPress(key);
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (CurrentTool == "ai" && Target == 1.0)
        {
            Sections.Ai.OnTextInput(this, e.Text);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (CurrentTool == "ai" && Target == 1.0)
        {
            Sections.Ai.OnKeyDown(this, e);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_dragKey != null)                            // ползунок: клика по отпусканию нет
        {
            string key = _dragKey;
            _dragKey = null;
            ReleaseMouseCapture();
            Sections.ByKey(CurrentTool).DragEnd(this, key);
            SetPress(null);
            return;
        }
        string? hit = HitAt(pos);
        string? pressed = _pressKey;
        SetPress(null);
        if (hit != null && hit == pressed) Activate(hit);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Target == 1)
        {
            var pos = e.GetPosition(this);
            // Если колёсико крутят над нижним доком — быстро переключаем вкладки!
            if (pos.Y >= TopMargin + ExpandedH - 54)
            {
                var keys = Sections.Primary.Select(s => s.Key).ToList();
                int idx = keys.IndexOf(CurrentTool);
                if (idx >= 0)
                {
                    int next = e.Delta > 0 ? (idx - 1 + keys.Count) % keys.Count : (idx + 1) % keys.Count;
                    Select(keys[next]);
                    return;
                }
            }
            Sections.ByKey(CurrentTool).Wheel(this, e.Delta / 120.0);
        }
        else
        {
            WinApi.MediaKey(e.Delta > 0 ? "up" : "down");
            Sections.Sound.RefreshVolume(this);
            Kick();
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e) => _leaveTimer.Stop();

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_dragKey != null) return;
        SetHover(null);
        if (Target == 1 && S.LeaveCollapse)
        {
            _leaveTimer.Interval = TimeSpan.FromSeconds(S.LeaveDelay);
            _leaveTimer.Start();
        }
    }

    private void Activate(string key)
    {
        if (key == "dismiss:notify") DismissNotification();
        else if (key == "open:notify")
        {
            var onClick = ActiveNotification?.OnClick;
            DismissNotification();
            if (onClick != null)
            {
                onClick();
            }
            else
            {
                _summoned = _covered;
                Expand();
            }
        }
        else if (key == "media:play" || key == "media:toggle" || key == "home:play")
        {
            TogglePlay();
        }
        else if (key == "media:prev" || key == "home:prev")
        {
            PrevTrack();
        }
        else if (key == "media:next" || key == "home:next")
        {
            NextTrack();
        }
        else if (key == "summon:notch_bar") SummonFromEdge();
        else if (key == "open:weather") { Select("weather"); Expand(); }
        else if (key == "open:ai") { Select("ai"); Expand(); }
        else if (key == "robot:pet") PetRobot();
        else if (key == "robot:launch") LaunchAeroToDesktop();
        else if (key == "robot:dock") DockAero();
        else if (key == "open:calendar" || key == "go:calendar")
        {
            if (CurrentTool == "calendar")
                Select("home");
            else
            {
                Select("calendar");
                Expand();
            }
        }
        else if (key == "expand") Expand();
        else if (key == "collapse") Collapse();
        else if (key == "close")
        {
            foreach (var toast in Toasts.ToList()) toast.Dismiss();
            Application.Current.Shutdown();
        }
        else if (key == "back:home") Select("home");
        else if (key.StartsWith("tab:")) Select(key[4..]);
        else
        {
            Sections.ByKey(CurrentTool).Click(this, key);
            _hoverKey = null;
            Kick();
        }
    }

    private void AutoCollapse()
    {
        if (Busy == 0 && Target == 1 && _hoverKey == null) Collapse();
    }
}
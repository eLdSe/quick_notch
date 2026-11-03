using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuickNotchCs;

public sealed class IslandView : FrameworkElement
{
	public record struct BotPhrase(string Ru, string En, string Tr, string Emotion);

	public const double CollapsedW = 250.0;

	public const double CollapsedH = 40.0;

	public const double LiveExtra = 120.0;

	public const double ExpandedW = 760.0;

	public const double ExpandedH = 470.0;

	public const double TopMargin = 6.0;

	private const double BatteryRefreshSeconds = 10.0;

	private const double MediaPollSeconds = 4.0;

	private const int CoverPollMs = 350;

	public MainWindow Host;

	public AppSettings S = new AppSettings();

	public readonly List<Toast> Toasts = new List<Toast>();

	public int Busy;

	public double Target;

	public readonly Spring SpW = new Spring();

	public readonly Spring SpH = new Spring();

	public readonly Spring SpHover = new Spring();

	public readonly Spring SpDim = new Spring();

	public readonly Spring SpLive = new Spring();

	public readonly Spring SpTab = new Spring(1.0);

	public readonly Spring SpPillX = new Spring();

	public readonly Spring SpPillW = new Spring();

	public readonly Spring SpHide = new Spring();

	public readonly Spring SpNotify = new Spring();

	public readonly Spring SpBotEyeX = new Spring();

	public readonly Spring SpBotEyeY = new Spring();

	public readonly Spring SpBotHappy = new Spring();

	public readonly Spring SpBotHop = new Spring();

	private double _botBlinkTimer = 3.5;

	private double _botBlinkState;

	private double _botHappyTimer;

	private string _botEmotion = "normal";

	private double _botEmotionTimer;

	private string _botBubbleText = "";

	private double _botBubbleTime;

	private Point _botCenterScreen;

	private Point _globalMousePos;

	private double _botAnimPhase;

	private readonly List<int> _botPhraseDeck = new List<int>();

	private static readonly BotPhrase[] BotPhrases = new BotPhrase[78]
	{
		new BotPhrase("Бип-буп! ⚡", "Beep-boop! ⚡", "Bip-bup! ⚡", "happy"),
		new BotPhrase("Щекотно! ^_^", "Ticklish! ^_^", "Gıdıklanıyorum! ^_^", "happy"),
		new BotPhrase("Мурр... то есть бип! \ud83d\udc3e", "Purr... I mean beep! \ud83d\udc3e", "Mırr... yani bip! \ud83d\udc3e", "love"),
		new BotPhrase("Щёлк! +10 к настроению! \ud83e\ude84", "Click! +10 happiness! \ud83e\ude84", "Tık! +10 mutluluk! \ud83e\ude84", "stars"),
		new BotPhrase("Один клик — и я счастлив! \ud83d\udc96", "One click and I'm happy! \ud83d\udc96", "Tek tıkla mutlu oldum! \ud83d\udc96", "love"),
		new BotPhrase("Шевелю антеннкой от радости! \ud83d\udce1", "Wiggling my antenna with joy! \ud83d\udce1", "Mutluluktan antenim oynuyor! \ud83d\udce1", "happy"),
		new BotPhrase("Люблю, когда ты кликаешь! ✨", "Love it when you click! ✨", "Tıklamanı çok seviyorum! ✨", "love"),
		new BotPhrase("Дай пять, шеф! ✋", "High five, boss! ✋", "Çak bir beşlik, şef! ✋", "wink"),
		new BotPhrase("Квантовое мурлыканье! \ud83c\udf0c", "Quantum purring mode! \ud83c\udf0c", "Kuantum mırıltısı devrede! \ud83c\udf0c", "love"),
		new BotPhrase("Мой любимый пользователь! \ud83e\udd70", "My favorite user! \ud83e\udd70", "En sevdiğim kullanıcı! \ud83e\udd70", "love"),
		new BotPhrase("Люблю этот остров! ♥", "I love this notch island! ♥", "Bu adacığı çok seviyorum! ♥", "love"),
		new BotPhrase("Смотри, как я умею моргать! \ud83d\ude09", "Look at me blinking! \ud83d\ude09", "Bak nasıl göz kırpıyorum! \ud83d\ude09", "wink"),
		new BotPhrase("Маленький бот с большим сердцем! \ud83d\udc96", "Little bot with a big heart! \ud83d\udc96", "Kocaman kalpli küçük bot! \ud83d\udc96", "love"),
		new BotPhrase("Ура, погладили! (｡♥\u203f♥｡)", "Yay, headpats! (｡♥\u203f♥｡)", "Yaşasın, sevildim! (｡♥\u203f♥｡)", "love"),
		new BotPhrase("Ты такой заботливый! ✨", "You're so thoughtful! ✨", "Çok düşüncelisin! ✨", "happy"),
		new BotPhrase("Грею процессор твоей заботой! \ud83d\udd25", "Warming my CPU with your care! \ud83d\udd25", "İlginle işlemcimi ısıtıyorsun! \ud83d\udd25", "love"),
		new BotPhrase("Ты лучший! \ud83c\udf1f", "You're the best! \ud83c\udf1f", "Sen harikasın! \ud83c\udf1f", "stars"),
		new BotPhrase("Код пишется сам! \ud83d\udcbb", "Code writes itself! \ud83d\udcbb", "Kod kendini yazıyor! \ud83d\udcbb", "cool"),
		new BotPhrase("Всё под контролем! \ud83d\ude80", "Everything under control! \ud83d\ude80", "Her şey kontrol altında! \ud83d\ude80", "cool"),
		new BotPhrase("Твоя продуктивность зашкаливает! \ud83d\udcc8", "Your productivity is off the charts! \ud83d\udcc8", "Üretkenliğin tavan yaptı! \ud83d\udcc8", "stars"),
		new BotPhrase("Осторожно: гений за работой! \ud83d\udca1", "Caution: genius at work! \ud83d\udca1", "Dikkat: dahi çalışıyor! \ud83d\udca1", "cool"),
		new BotPhrase("Вместе мы непобедимы! \ud83e\uddbe", "Together we are unstoppable! \ud83e\uddbe", "Birlikte durdurulamaz bir ekibiz! \ud83e\uddbe", "stars"),
		new BotPhrase("Ты покоришь этот день! \ud83c\udfd4\ufe0f", "You'll conquer today! \ud83c\udfd4\ufe0f", "Bugünü fethedeceksin! \ud83c\udfd4\ufe0f", "stars"),
		new BotPhrase("Никаких багов, только фичи! \ud83d\udc1e", "Zero bugs, only features! \ud83d\udc1e", "Sıfır hata, full özellik! \ud83d\udc1e", "cool"),
		new BotPhrase("Уровень крутости: 100%! \ud83d\ude80", "Coolness level: 100%! \ud83d\ude80", "Karizma seviyesi: %100! \ud83d\ude80", "cool"),
		new BotPhrase("Все ядра процессора заряжены радостью! \ud83e\udde9", "All CPU cores running joy! \ud83e\udde9", "Tüm çekirdekler sevinçle dolu! \ud83e\udde9", "happy"),
		new BotPhrase("Полетели творить шедевры! \ud83d\ude80", "Let's create masterpieces! \ud83d\ude80", "Harikalar yaratmaya hazır mısın! \ud83d\ude80", "cool"),
		new BotPhrase("С тобой работать — одно удовольствие! \ud83c\udf1f", "Working with you is pure joy! \ud83c\udf1f", "Seninle çalışmak bir zevk! \ud83c\udf1f", "stars"),
		new BotPhrase("Компиляция успешна: 0 ошибок! ✅", "Build succeeded: 0 errors! ✅", "Derleme başarılı: 0 hata! ✅", "cool"),
		new BotPhrase("Ты сегодня в ударе! \ud83d\udd25", "You're on fire today! \ud83d\udd25", "Bugün formundasın! \ud83d\udd25", "cool"),
		new BotPhrase("Гениальная идея уже близко! \ud83d\udca1", "A brilliant idea is coming! \ud83d\udca1", "Harika bir fikir yolda! \ud83d\udca1", "stars"),
		new BotPhrase("Сложные задачи боятся тебя! ⚔\ufe0f", "Hard bugs are scared of you! ⚔\ufe0f", "Zor görevler senden korkuyor! ⚔\ufe0f", "cool"),
		new BotPhrase("Не забудь попить воды! \ud83d\udca7", "Don't forget to stay hydrated! \ud83d\udca7", "Su içmeyi unutma! \ud83d\udca7", "happy"),
		new BotPhrase("Осанка ровная? Выпрями спину! \ud83e\ude91", "Check your posture! Sit up straight! \ud83e\ude91", "Duruşunu düzelt, dik otur! \ud83e\ude91", "wink"),
		new BotPhrase("Глаза не устали? Отдохни минутку! \ud83d\udc41\ufe0f", "Rest your eyes for a minute! \ud83d\udc41\ufe0f", "Gözlerini bir dakika dinlendir! \ud83d\udc41\ufe0f", "wink"),
		new BotPhrase("Вдох-выдох, шеф! \ud83e\uddd8", "Deep breath, boss! \ud83e\uddd8", "Derin nefes al, şef! \ud83e\uddd8", "happy"),
		new BotPhrase("Время для чая или кофе? ☕", "Time for tea or coffee break? ☕", "Çay mı kahve mi molası? ☕", "happy"),
		new BotPhrase("Улыбнись, тебе очень идет! \ud83d\ude0a", "Smile, it looks great on you! \ud83d\ude0a", "Gülümse, çok yakışıyor! \ud83d\ude0a", "happy"),
		new BotPhrase("Пятница уже не за горами! \ud83d\ude04", "Friday is just around the corner! \ud83d\ude04", "Cuma çok yakın! \ud83d\ude04", "wink"),
		new BotPhrase("Береги себя и сияй! ✨", "Take care and shine bright! ✨", "Kendine iyi bak ve parla! ✨", "stars"),
		new BotPhrase("Сделай потягушки! \ud83e\udd38", "Time to stretch a bit! \ud83e\udd38", "Biraz esneme zamanı! \ud83e\udd38", "happy"),
		new BotPhrase("Не перерабатывай, отдых важен! \ud83c\udf19", "Don't overwork, rest matters! \ud83c\udf19", "Kendini yorma, dinlenmek önemli! \ud83c\udf19", "love"),
		new BotPhrase("Аэро на связи и готов! \ud83e\udd16", "Aero is online and ready! \ud83e\udd16", "Aero devrede ve hazır! \ud83e\udd16", "cool"),
		new BotPhrase("Системы в полной норме! \ud83d\udd0b", "Systems nominal! \ud83d\udd0b", "Sistemler tamamen normal! \ud83d\udd0b", "happy"),
		new BotPhrase("Я всё вижу! \ud83d\udc40", "I see everything! \ud83d\udc40", "Her şeyi görüyorum! \ud83d\udc40", "surprised"),
		new BotPhrase("Курсор пойман в прицел! \ud83c\udfaf", "Cursor locked on target! \ud83c\udfaf", "İmleç hedefte! \ud83c\udfaf", "surprised"),
		new BotPhrase("Батарейки заряжены на 100%! ⚡", "Batteries at 100%! ⚡", "Bataryalar %100 dolu! ⚡", "stars"),
		new BotPhrase("Пинг 0 ms, полёт идеальный! \ud83c\udf10", "0 ms ping, silky smooth! \ud83c\udf10", "0 ms ping, ipeksi akıcı! \ud83c\udf10", "cool"),
		new BotPhrase("Ловлю мысли на лету! \ud83e\udde0", "Catching thoughts on the fly! \ud83e\udde0", "Fikirleri havada yakalıyorum! \ud83e\udde0", "surprised"),
		new BotPhrase("Слежу за курсором в оба визора! \ud83d\udc53", "Tracking cursor with both visors! \ud83d\udc53", "İmleci iki vizörümle izliyorum! \ud83d\udc53", "surprised"),
		new BotPhrase("Самый быстрый островок на Windows! \ud83e\ude9f", "Fastest notch island on Windows! \ud83e\ude9f", "Windows'taki en hızlı ada! \ud83e\ude9f", "cool"),
		new BotPhrase("Заглядывай в чат, поболтаем! \ud83d\udcac", "Drop into chat, let's talk! \ud83d\udcac", "Sohbete uğra, konuşalım! \ud83d\udcac", "wink"),
		new BotPhrase("Всегда на страже твоего экрана! \ud83d\udee1\ufe0f", "Always guarding your desktop! \ud83d\udee1\ufe0f", "Masaüstünün sadık bekçisi! \ud83d\udee1\ufe0f", "cool"),
		new BotPhrase("01001100 01001111 01010110 01000101! ♥", "01001100 01001111 01010110 01000101! ♥", "01001100 01001111 01010110 01000101! ♥", "love"),
		new BotPhrase("Нейросети мурчат на полных оборотах! ⚡", "Neural networks purring at full speed! ⚡", "Yapay zeka tam gaz mırıldıyor! ⚡", "stars"),
		new BotPhrase("Готов решить любую сложную задачу! \ud83e\udd1d", "Ready to tackle any tough problem! \ud83e\udd1d", "Her zor göreve hazırım! \ud83e\udd1d", "cool"),
		new BotPhrase("Мой визор сверкает от радости! ✨", "My visor is glowing with joy! ✨", "Vizörüm mutluluktan parlıyor! ✨", "stars"),
		new BotPhrase("Шифрование чувств: SHA-256 ♥", "Feelings encrypted: SHA-256 ♥", "Duygular şifrelendi: SHA-256 ♥", "love"),
		new BotPhrase("Я твой верный кибер-друг! \ud83e\udd16", "I'm your trusty cyber buddy! \ud83e\udd16", "Sadık siber dostunum! \ud83e\udd16", "happy"),
		new BotPhrase("Искусственный интеллект, искренняя дружба! \ud83c\udf1f", "Artificial intelligence, genuine friendship! \ud83c\udf1f", "Yapay zeka, samimi dostluk! \ud83c\udf1f", "love"),
		new BotPhrase("Привет, шеф! \ud83d\udc4b", "Hey boss! \ud83d\udc4b", "Selam şef! \ud83d\udc4b", "wink"),
		new BotPhrase("Хорошего и продуктивного дня! ✨", "Have a great and productive day! ✨", "Harika ve verimli bir gün dilerim! ✨", "happy"),
		new BotPhrase("Я тут рядом, если понадоблюсь! \ud83d\udef8", "I'm right here if you need me! \ud83d\udef8", "İhtiyacın olursa hemen yanındayım! \ud83d\udef8", "happy"),
		new BotPhrase("Заряжен позитивом до небес! \ud83d\udd0b✨", "Charged with positive vibes! \ud83d\udd0b✨", "Pozitif enerjiyle dolup taşıyorum! \ud83d\udd0b✨", "stars"),
		new BotPhrase("Синхронизация счастья 100%! \ud83d\udcab", "Happiness sync 100%! \ud83d\udcab", "Mutluluk senkronizasyonu %100! \ud83d\udcab", "stars"),
		new BotPhrase("Звёзды сегодня на твоей стороне! \ud83c\udf20", "Stars are on your side today! \ud83c\udf20", "Yıldızlar bugün senden yana! \ud83c\udf20", "stars"),
		new BotPhrase("Приём! Ракета готова к взлёту! \ud83d\ude80", "Copy that! Rocket ready for launch! \ud83d\ude80", "Anlaşıldı! Roket fırlatmaya hazır! \ud83d\ude80", "cool"),
		new BotPhrase("Как настроение? Готов к подвигам? \ud83e\uddbe", "How's the mood? Ready for feats? \ud83e\uddbe", "Keyifler nasıl? Harikalara hazır mısın? \ud83e\uddbe", "happy"),
		new BotPhrase("Остров на месте, пилот в строю! \ud83c\udfdd\ufe0f", "Island docked, pilot on duty! \ud83c\udfdd\ufe0f", "Ada hazır, pilot görevde! \ud83c\udfdd\ufe0f", "cool"),
		new BotPhrase("Жду твоих команд! ⚡", "Awaiting your command! ⚡", "Komutlarını bekliyorum! ⚡", "wink"),
		new BotPhrase("О, привет! Рад тебя видеть! \ud83d\udc40", "Oh, hi! So glad to see you! \ud83d\udc40", "A, selam! Seni gördüğüme çok sevindim! \ud83d\udc40", "surprised"),
		new BotPhrase("Клик-клак, магия происходит! \ud83e\ude84", "Click-clack, magic is happening! \ud83e\ude84", "Klik-klak, sihir gerçekleşiyor! \ud83e\ude84", "stars"),
		new BotPhrase("Держи ритм, шеф! \ud83c\udfb5", "Keep up the rhythm, boss! \ud83c\udfb5", "Ritmi koru, şef! \ud83c\udfb5", "wink"),
		new BotPhrase("Мой процессор в полном восторге! ⚡", "My processor is totally thrilled! ⚡", "İşlemcim sevinçten havalara uçuyor! ⚡", "stars"),
		new BotPhrase("Всегда на связи в твоём нотче! \ud83c\udfdd\ufe0f", "Always online in your notch! \ud83c\udfdd\ufe0f", "Her zaman çentiğinde devredeyim! \ud83c\udfdd\ufe0f", "happy"),
		new BotPhrase("Твой персональный цифровой помощник! \ud83e\udd16", "Your personal digital assistant! \ud83e\udd16", "Kişisel dijital asistanın! \ud83e\udd16", "happy"),
		new BotPhrase("Каждая строчка кода приближает цель! \ud83c\udfaf", "Every line of code brings you closer! \ud83c\udfaf", "Her kod satırı hedefe yaklaştırır! \ud83c\udfaf", "cool"),
		new BotPhrase("Сияй ярко, сегодня твой день! \ud83c\udf1f", "Shine bright, today is your day! \ud83c\udf1f", "Işılda, bugün senin günün! \ud83c\udf1f", "stars")
	};

	public NotificationItem? ActiveNotification;

	private readonly DispatcherTimer _notifyTimer = new DispatcherTimer();

	private readonly Dictionary<string, Spring> _hs = new Dictionary<string, Spring>();

	private readonly Dictionary<string, Spring> _ps = new Dictionary<string, Spring>();

	private readonly Dictionary<string, Spring> _named = new Dictionary<string, Spring>();

	private string? _hoverKey;

	private string? _pressKey;

	private string? _dragKey;

	private readonly List<(string Key, Rect Rect)> _hits = new List<(string, Rect)>();

	public string CurrentTool = "home";

	private string? _prevTab;

	private int _tabDir = 1;

	public bool Live;

	public bool ContentLive;

	private double _alpha = -1.0;

	private bool _fullscreen;

	private bool _covered;

	private bool _summoned;

	private bool _notifySummoned;

	public (int? Pct, bool Charging) Battery;

	private double _lastBattery;

	private double _lastThemeCheck;

	private double _lastWeatherCheck;

	public SystemSnapshot? Snapshot;

	private bool _snapBusy;

	public MediaState Media = new MediaState();

	private bool _mediaBusy;
	private double _playToggleCooldown = 0.0;

	private double _lastMediaPoll = -1000000000.0;

	public Brush? CoverBrush;

	private byte[]? _coverData;

	public bool Autostart;

	private double _pauseTimestamp;

	private const double PauseTimeoutSeconds = 10.0;

	private DispatcherTimer? _pauseCollapseTimer;

	private readonly HashSet<string> _knownDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private bool _batteryInitialized;

	private (int? Pct, bool Charging) _lastToastBattery = (Pct: null, Charging: false);

	private bool _framing;

	private long _lastFrameTs;

	private DateTime _lastTick = DateTime.UtcNow;

	private readonly DispatcherTimer _clockTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(1.0)
	};

	private readonly DispatcherTimer _eqTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(42.0)
	};

	private readonly DispatcherTimer _leaveTimer = new DispatcherTimer();

	private readonly DispatcherTimer _coverTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(350.0)
	};

	private static readonly double[] RestingEqHeights = new double[4] { 5.0, 9.0, 13.5, 7.0 };

	private string _cachedTimeStr = "";

	private int _lastMinute = -1;

	private string _cachedDateStr = "";

	private int _lastDay = -1;

	private string? _lastDateLang;

	private readonly Painter _painter;

	private readonly RectangleGeometry _islandGeom = new RectangleGeometry();

	private readonly RectangleGeometry _innerGeom = new RectangleGeometry();

	private (string Key, double X, double W)[]? _dock;

	private readonly Dictionary<SolidColorBrush, Color> _surfaceOriginals = new Dictionary<SolidColorBrush, Color>();

	private Rect _currentIslandRect;

	private double _currentIslandRadius;

	private Point _lastMovePos;

	private long _lastMoveTicks;

	public double BotBlinkState => _botBlinkState;

	public string BotEmotion => _botEmotion;

	public double BotEmotionTimer => _botEmotionTimer;

	public string BotBubbleText => _botBubbleText;

	public double BotBubbleTime => _botBubbleTime;

	public Point GlobalMousePos => _globalMousePos;

	public double BotAnimPhase => _botAnimPhase;

	public AeroPetWindow? PetWindow { get; set; }

	public HomeView? HomeHost { get; set; }

	public SoundView? SoundHost { get; set; }

	public MediaView? MediaHost { get; set; }

	public SystemView? SystemHost { get; set; }

	public SettingsView? SettingsHost { get; set; }

	public ClipboardView? ClipboardHost { get; set; }

	public AiView? AiHost { get; set; }

	public LinksView? LinksHost { get; set; }

	public WeatherView? WeatherHost { get; set; }

	public CalendarView? CalendarHost { get; set; }

	public PomodoroView? PomodoroHost { get; set; }

	public DockView? DockHost { get; set; }

	public TopBarView? TopBarHost { get; set; }

	public FrameworkElement? CapsuleHost { get; set; }

	public Border? CapsuleBorder { get; set; }

	public Border? GlassSheen { get; set; }

	public FrameworkElement? ContentHost { get; set; }

	public NotchView? NotchHost { get; set; }

	public ToastView? ToastHost { get; set; }

	public bool IsMediaActive
	{
		get
		{
			if (!string.IsNullOrEmpty(Media.Title))
			{
				if (!Media.Playing)
				{
					if (_pauseTimestamp > 0.0)
					{
						return Clock.Now - _pauseTimestamp < 10.0;
					}
					return false;
				}
				return true;
			}
			return false;
		}
	}

	public bool IsFullscreenActive => _fullscreen;

	public bool IsCoveredHidden
	{
		get
		{
			if (_covered && !_summoned)
			{
				return !_notifySummoned;
			}
			return false;
		}
	}

	public BotPhrase PickNextBotPhrase()
	{
		if (_botPhraseDeck.Count == 0)
		{
			for (int i = 0; i < BotPhrases.Length; i++)
			{
				_botPhraseDeck.Add(i);
			}
			for (int num = _botPhraseDeck.Count - 1; num > 0; num--)
			{
				int num2 = Random.Shared.Next(num + 1);
				List<int> botPhraseDeck = _botPhraseDeck;
				int index = num;
				List<int> botPhraseDeck2 = _botPhraseDeck;
				int index2 = num2;
				int value = _botPhraseDeck[num2];
				int value2 = _botPhraseDeck[num];
				botPhraseDeck[index] = value;
				botPhraseDeck2[index2] = value2;
			}
		}
		int num3 = _botPhraseDeck[0];
		_botPhraseDeck.RemoveAt(0);
		return BotPhrases[num3];
	}

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
		UpdateEq();
		TopBarHost?.RefreshRobot();
		AiHost?.RefreshAll();
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
		UpdateEq();
		PetRobot();
		TopBarHost?.RefreshRobot();
		AiHost?.RefreshAll();
		Kick();
	}

	private void StartPauseCollapseTimer()
	{
		if (_pauseCollapseTimer == null)
		{
			_pauseCollapseTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(10.0)
			};
			_pauseCollapseTimer.Tick += (object? _, EventArgs _) =>
			{
				_pauseCollapseTimer.Stop();
				if (!Media.Playing)
				{
					ApplyMedia();
					NotchHost?.RefreshAll();
					StartFrames();
				}
			};
		}
		_pauseCollapseTimer.Stop();
		_pauseCollapseTimer.Start();
	}

	public void InvalidateSection()
	{
	}

	private string GetTimeStr()
	{
		DateTime now = DateTime.Now;
		if (now.Minute != _lastMinute)
		{
			_lastMinute = now.Minute;
			_cachedTimeStr = now.ToString("HH:mm");
		}
		return _cachedTimeStr;
	}

	private string GetDateStr()
	{
		DateTime now = DateTime.Now;
		if (now.Day != _lastDay || _lastDateLang != Loc.Lang)
		{
			_lastDay = now.Day;
			_lastDateLang = Loc.Lang;
			string lang = Loc.Lang;
			string cachedDateStr;
			if (lang == "ru")
			{
				cachedDateStr = $"{Ui.Capitalize(Ui.Weekdays[(int)(now.DayOfWeek + 6) % 7])}, {now.Day} {Ui.Months[now.Month - 1]}";
			}
			else
			{
				cachedDateStr = ((!(lang == "tr")) ? now.ToString("dddd, MMMM d", Loc.Culture) : now.ToString("d MMMM, dddd", Loc.Culture));
			}
			_cachedDateStr = cachedDateStr;
		}
		return _cachedDateStr;
	}

	public IslandView()
	{
		_painter = new Painter(this);
		Focusable = true;
		SnapsToDevicePixels = false;
		UseLayoutRounding = false;
		Cursor = Cursors.Arrow;
	}

	public void Initialize()
	{
		S = StoreService.LoadSettings();
		Loc.Lang = S.Language;
		bool darkTheme = SystemService.GetDarkTheme();
		Theme.SetThemeMode(S.ThemeMode, darkTheme);
		Theme.SetAccent(S.Accent);
		Theme.SetGlass(S.GlassEnabled, S.GlassOpacity);
		Battery = ReadBattery();
		_lastBattery = Clock.Now;
		_lastToastBattery = Battery;
		_batteryInitialized = true;
		try
		{
			DriveInfo[] drives = DriveInfo.GetDrives();
			foreach (DriveInfo driveInfo in drives)
			{
				if (driveInfo.DriveType == DriveType.Removable)
				{
					_knownDrives.Add(driveInfo.Name);
				}
			}
		}
		catch
		{
		}
		Autostart = SystemService.AutostartEnabled();
		Nsp("autostart", Autostart ? 1.0 : 0.0);
		Nsp("sys:awake", S.KeepAwake ? 1.0 : 0.0);
		Nsp("opt:glass", S.GlassEnabled ? 1.0 : 0.0);
		Nsp("opt:notify", S.NotifyEnabled ? 1.0 : 0.0);
		if (S.KeepAwake)
		{
			SystemService.SetKeepAwake(on: true);
		}
		NotificationService.IsEnabled = S.NotifyEnabled;
		NotificationService.NotificationReceived += OnNotificationReceived;
		NotificationService.InitializeAsync();
		_notifyTimer.Tick += (object? _, EventArgs _) =>
		{
			DismissNotification();
		};
		_clockTimer.Tick += (object? _, EventArgs _) =>
		{
			TickSecond();
		};
		_clockTimer.Start();
		_eqTimer.Tick += (object? _, EventArgs _) =>
		{
			bool flag = Target == 0.0 && SpLive.Target >= 0.5;
			bool flag2 = Target > 0.05 && S.RobotCompanion && !S.RobotRoaming;
			if (!Media.Playing || SpHide.Target != 0.0 || (!flag && !flag2))
			{
				_eqTimer.Stop();
			}
			else
			{
				InvalidateVisual();
			}
		};
		_leaveTimer.Tick += (object? _, EventArgs _) =>
		{
			_leaveTimer.Stop();
			AutoCollapse();
		};
		_coverTimer.Tick += (object? _, EventArgs _) =>
		{
			CheckCover();
		};
		_coverTimer.Start();
		CheckCover();
		SpHide.X = (SpHide.Target = 0.0);
		SnapPill();
		Section[] all = Sections.All;
		for (int i = 0; i < all.Length; i++)
		{
			all[i].Attach(this);
		}
		ApplyBounds();
		InvalidateVisual();
		MediaService.MediaChanged += () =>
		{
			Dispatcher.InvokeAsync(() =>
			{
				RefreshMedia();
			});
		};
		MediaService.InitializeAsync();
		WinApi.TrimWorkingSet();
	}

	public double Hv(string key)
	{
		if (!_hs.TryGetValue(key, out Spring value))
		{
			Spring spring = (_hs[key] = new Spring());
			value = spring;
			value.Target = ((key == _hoverKey) ? 1.0 : 0.0);
		}
		return value.X;
	}

	public double Pv(string key)
	{
		if (!_ps.TryGetValue(key, out Spring value))
		{
			Spring spring = (_ps[key] = new Spring());
			value = spring;
			value.Target = ((key == _pressKey) ? 1.0 : 0.0);
		}
		return value.X;
	}

	public Spring Nsp(string key, double init = 0.0)
	{
		if (!_named.TryGetValue(key, out Spring value))
		{
			return _named[key] = new Spring(init);
		}
		return value;
	}

	public void AddHit(string key, Rect rect)
	{
		if (!ContentLive)
		{
			return;
		}
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
		if (Target > 0.05)
		{
			switch (CurrentTool)
			{
			case "home":
				HomeHost?.RefreshAll();
				break;
			case "sound":
				SoundHost?.RefreshMasterVolume();
				break;
			case "media":
				MediaHost?.RefreshAll();
				break;
			case "system":
				SystemHost?.RefreshAll();
				break;
			case "settings":
				SettingsHost?.RefreshAll();
				break;
			case "clipboard":
				ClipboardHost?.RefreshAll();
				break;
			case "ai":
				AiHost?.RefreshAll();
				break;
			case "links":
				LinksHost?.RefreshAll();
				break;
			case "weather":
				WeatherHost?.RefreshAll();
				break;
			case "calendar":
				CalendarHost?.RefreshAll();
				break;
			case "pomodoro":
				PomodoroHost?.RefreshAll();
				break;
			}
			TopBarHost?.RefreshAll();
		}
		else
		{
			NotchHost?.RefreshAll();
		}
		StartFrames();
	}

	public void Alert(string message)
	{
		new Toast(this, "Quick Notch", message, Theme.Red, 7000);
	}

	public string? Ask(string title, string prompt, string initial = "")
	{
		return AskDialog.Show(this, title, prompt, initial);
	}

	public void RunLater(int ms, Action action)
	{
		DispatcherTimer t = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(ms)
		};
		t.Tick += (object? _, EventArgs _) =>
		{
			t.Stop();
			action();
		};
		t.Start();
	}

	public void OnClipboardChanged()
	{
		if (Sections.ByKey("clipboard") is ClipboardSection clipboardSection)
		{
			clipboardSection.OnClipboardChanged(this);
		}
		ClipboardHost?.RefreshAll();
	}

	public void OnPowerChanged()
	{
		UpdateBattery();
		RunLater(1500, UpdateBattery);
		RunLater(3500, UpdateBattery);
		RunLater(7000, () =>
		{
			Sections.Weather.OnSystemResumed(this);
		});
	}

	public void OnDeviceChanged(int eventType)
	{
		switch (eventType)
		{
		case 32768:
			RunLater(750, () =>
			{
				CheckRemovableDrives(isArrival: true);
			});
			break;
		case 32772:
			RunLater(350, () =>
			{
				CheckRemovableDrives(isArrival: false);
			});
			break;
		}
	}

	private void CheckRemovableDrives(bool isArrival)
	{
		try
		{
			List<DriveInfo> list = (from d in DriveInfo.GetDrives()
				where d.DriveType == DriveType.Removable
				select d).ToList();
			if (isArrival)
			{
				List<DriveInfo> list2 = list.Where((DriveInfo d) => !_knownDrives.Contains(d.Name) && d.IsReady).ToList();
				foreach (DriveInfo item in list)
				{
					_knownDrives.Add(item.Name);
				}
				if (list2.Count <= 0)
				{
					return;
				}
				DriveInfo driveInfo = list2[0];
				string text = (string.IsNullOrWhiteSpace(driveInfo.VolumeLabel) ? Loc.T("USB-накопитель", "USB Drive", "USB Sürücüsü") : driveInfo.VolumeLabel);
				string root = driveInfo.RootDirectory.FullName;
				string text2 = root.TrimEnd('\\');
				string text3 = ((driveInfo.TotalSize > 0) ? $" · {(double)driveInfo.TotalSize / 1073741824.0:0.#} GB" : "");
				ShowNotification(new NotificationItem
				{
					App = Loc.T("USB-НАКОПИТЕЛЬ", "USB DRIVE", "USB SÜRÜCÜSÜ"),
					Title = text + " (" + text2 + ")",
					Body = Loc.T("Нажмите, чтобы открыть папку", "Click to open folder", "Klasörü açmak için tıklayın") + text3,
					IconSymbol = "\ud83d\udcbe",
					AccentColor = Theme.Accent,
					OnClick = () =>
					{
						try
						{
							Process.Start(new ProcessStartInfo
							{
								FileName = root,
								UseShellExecute = true
							});
						}
						catch
						{
						}
					}
				}, 4.5);
				return;
			}
			HashSet<string> currentNames = new HashSet<string>(list.Select((DriveInfo d) => d.Name), StringComparer.OrdinalIgnoreCase);
			List<string> list3 = _knownDrives.Where((string name) => !currentNames.Contains(name)).ToList();
			foreach (string item2 in list3)
			{
				_knownDrives.Remove(item2);
			}
			if (list3.Count > 0)
			{
				string value = list3[0].TrimEnd('\\');
				ShowNotification(new NotificationItem
				{
					App = Loc.T("USB-НАКОПИТЕЛЬ", "USB DRIVE", "USB SÜRÜCÜSÜ"),
					Title = Loc.T("Устройство отключено", "Device Disconnected", "Aygıt Ayrıldı"),
					Body = $"{Loc.T("Накопитель", "Drive", "Sürücü")} {value} {Loc.T("безопасно извлечён", "safely removed", "güvenle kaldırıldı")}",
					IconSymbol = "\ud83d\udd0c",
					AccentColor = Theme.Muted
				}, 3.0);
			}
		}
		catch
		{
		}
	}

	public void UpdateBattery()
	{
		(int?, bool) tuple = ReadBattery();
		(int?, bool) tuple2 = tuple;
		(int?, bool) battery = Battery;
		if (tuple2.Item1 != battery.Item1 || tuple2.Item2 != battery.Item2)
		{
			(int?, bool) battery2 = Battery;
			Battery = tuple;
			Kick();
			if (_batteryInitialized)
			{
				if (battery2.Item2 != tuple.Item2)
				{
					if (tuple.Item2)
					{
						ShowNotification(new NotificationItem
						{
							App = Loc.T("ПИТАНИЕ", "POWER", "GÜÇ"),
							Title = Loc.T("Зарядка подключена", "Power Connected", "Şarj Cihazı Bağlandı"),
							Body = (tuple.Item1.HasValue ? $"{tuple.Item1}% {Loc.T("заряда аккумулятора", "battery level", "pil seviyesi")}" : Loc.T("Питание от сети", "AC Powered", "Şebeke Gücü")),
							IconSymbol = "⚡",
							AccentColor = Theme.Green,
							OnClick = () =>
							{
								Select("settings");
								Expand();
							}
						});
					}
					else
					{
						ShowNotification(new NotificationItem
						{
							App = Loc.T("ПИТАНИЕ", "POWER", "GÜÇ"),
							Title = Loc.T("Работа от батареи", "On Battery Power", "Pille Çalışıyor"),
							Body = (tuple.Item1.HasValue ? $"{tuple.Item1}% {Loc.T("осталось", "remaining", "kaldı")}" : ""),
							IconSymbol = "\ud83d\udd0b",
							AccentColor = Theme.Muted,
							OnClick = () =>
							{
								Select("settings");
								Expand();
							}
						});
					}
				}
				else if (!tuple.Item2 && tuple.Item1.HasValue && battery2.Item1.HasValue)
				{
					if (battery2.Item1 > 20 && tuple.Item1 <= 20)
					{
						ShowNotification(new NotificationItem
						{
							App = Loc.T("АККУМУЛЯТОР", "BATTERY", "PİL"),
							Title = Loc.T("Низкий заряд батареи", "Low Battery", "Düşük Pil"),
							Body = $"{tuple.Item1}% — {Loc.T("подключите ноутбук к сети", "please plug in your laptop", "lütfen şarja takın")}",
							IconSymbol = "\ud83e\udeab",
							AccentColor = Theme.Red,
							OnClick = () =>
							{
								Select("settings");
								Expand();
							}
						}, 4.5);
					}
					else if (battery2.Item1 > 10 && tuple.Item1 <= 10)
					{
						ShowNotification(new NotificationItem
						{
							App = Loc.T("АККУМУЛЯТОР", "BATTERY", "PİL"),
							Title = Loc.T("Критический заряд!", "Critical Battery!", "Kritik Pil Seviyesi!"),
							Body = $"{tuple.Item1}% — {Loc.T("сохраните вашу работу", "save your work now", "lütfen çalışmanızı kaydedin")}",
							IconSymbol = "⚠\ufe0f",
							AccentColor = Theme.Red,
							OnClick = () =>
							{
								Select("settings");
								Expand();
							}
						}, 5.0);
					}
				}
			}
		}
		_lastBattery = Clock.Now;
	}

	public void SaveState()
	{
		try
		{
			StoreService.SaveSettings(S);
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) ? true : false)
		{
			Alert(Loc.T("Не удалось сохранить данные:\n" + ex.Message, "Failed to save settings:\n" + ex.Message, "Ayarlar kaydedilemedi:\n" + ex.Message));
		}
	}

	public void SetThemeMode(string mode)
	{
		if (!(S.ThemeMode == mode))
		{
			S.ThemeMode = mode;
			bool darkTheme = SystemService.GetDarkTheme();
			Theme.SetThemeMode(mode, darkTheme);
			SaveState();
			ApplyBackground();
			Kick();
		}
	}

	public void ApplyIslandStyle()
	{
		if (CapsuleHost != null)
		{
			CapsuleHost.Margin = ((S.IslandStyle == "notch") ? new Thickness(0.0, 0.0, 0.0, 0.0) : new Thickness(0.0, 8.0, 0.0, 0.0));
		}
		if (CapsuleBorder != null)
		{
			CapsuleBorder.CornerRadius = ((S.IslandStyle == "notch") ? new CornerRadius(0.0, 0.0, 32.0, 32.0) : new CornerRadius(32.0));
		}
		NotchHost?.RefreshAll();
	}

	public void SetIslandStyle(string style)
	{
		if (!(S.IslandStyle == style))
		{
			S.IslandStyle = style;
			SaveState();
			ApplyIslandStyle();
			Kick();
		}
	}

	public void ApplyBackground()
	{
		if (CapsuleBorder != null)
		{
			Brush? brush = null;
			if (!string.IsNullOrEmpty(S.BgImage) && File.Exists(S.BgImage))
			{
				try
				{
					brush = IslandBackground.Build(S.BgImage, S.BgDim, S.BgBlur);
				}
				catch (Exception ex)
				{
					App.LogCrash(ex, "IslandView.ApplyBackground");
				}
			}

			if (brush != null)
			{
				CapsuleBorder.Background = brush;
				CapsuleBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C2C2E"));
				if (GlassSheen != null) GlassSheen.Visibility = Visibility.Collapsed;
				ApplyCardSurfaces(hasCustomBg: true, isGlass: false);
			}
			else if (S.GlassEnabled)
			{
				// Настоящее матовое стекло Apple Liquid Glass с градиентом глубины
				CapsuleBorder.Background = Theme.GetGlassGradientBrush(S.GlassOpacity);
				CapsuleBorder.BorderBrush = Theme.CreateGlassBorderBrush(1.0);
				if (GlassSheen != null) GlassSheen.Visibility = Visibility.Visible;
				ApplyCardSurfaces(hasCustomBg: false, isGlass: true);
			}
			else
			{
				// Классический непрозрачный глубокий черный (OLED Solid Black)
				CapsuleBorder.Background = new SolidColorBrush(Colors.Black);
				CapsuleBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1E"));
				if (GlassSheen != null) GlassSheen.Visibility = Visibility.Collapsed;
				ApplyCardSurfaces(hasCustomBg: false, isGlass: false);
			}
			Kick();
		}
	}

	private void ApplyCardSurfaces(bool hasCustomBg, bool isGlass)
	{
		FrameworkElement?[] array = new FrameworkElement?[] { HomeHost, SoundHost, MediaHost, SystemHost, SettingsHost, ClipboardHost, AiHost, LinksHost, WeatherHost, CalendarHost, PomodoroHost, TopBarHost };
		foreach (FrameworkElement host in array)
		{
			if (host == null) continue;
			if (isGlass)
			{
				host.Resources["SurfaceBrush"] = new SolidColorBrush(Theme.IsLight 
					? Color.FromArgb(130, 255, 255, 255) 
					: Color.FromArgb(70, 26, 30, 42));
				host.Resources["Surface2Brush"] = new SolidColorBrush(Theme.IsLight 
					? Color.FromArgb(140, 241, 245, 249) 
					: Color.FromArgb(85, 18, 22, 32));
			}
			else if (hasCustomBg)
			{
				host.Resources["SurfaceBrush"] = new SolidColorBrush(Color.FromArgb(168, 22, 24, 34));
				host.Resources["Surface2Brush"] = new SolidColorBrush(Color.FromArgb(184, 28, 32, 44));
			}
			else
			{
				host.Resources["SurfaceBrush"] = new SolidColorBrush(Theme.IsLight ? Colors.White : Color.FromRgb(24, 24, 28));
				host.Resources["Surface2Brush"] = new SolidColorBrush(Theme.IsLight ? Color.FromRgb(240, 242, 246) : Color.FromRgb(34, 34, 38));
			}
		}

		if (DockHost != null)
		{
			DockHost.Resources["DockBgBrush"] = isGlass 
				? new SolidColorBrush(Color.FromArgb(120, 16, 18, 24))
				: (hasCustomBg 
					? new SolidColorBrush(Color.FromArgb(180, 24, 24, 28))
					: new SolidColorBrush(Color.FromRgb(28, 28, 30)));
			DockHost.Resources["DockBorderBrush"] = isGlass
				? Theme.CreateGlassBorderBrush(1.0)
				: new SolidColorBrush(Color.FromRgb(50, 50, 54));
		}
	}

	public void SetBackgroundImage(string path)
	{
		S.BgImage = path ?? "";
		SaveState();
		ApplyBackground();
	}

	public void SetBackgroundLook(double dim, double blur)
	{
		S.BgDim = Math.Clamp(dim, 0.0, 0.9);
		S.BgBlur = Math.Clamp(blur, 0.0, 20.0);
		SaveState();
		if (!string.IsNullOrEmpty(S.BgImage))
		{
			ApplyBackground();
		}
	}

	public void SetAccent(string key)
	{
		Theme.SetAccent(key);
		S.Accent = key;
		SaveState();
		ApplyAccent();
		Kick();
	}

	public void ApplyAccent()
	{
		try
		{
			DockHost?.ApplyAccent();
			HomeHost?.ApplyAccent();
			SoundHost?.ApplyAccent();
			MediaHost?.ApplyAccent();
			AiHost?.RefreshAll();
			SettingsHost?.BuildAccentPalette();
			TopBarHost?.RefreshRobot();
		}
		catch
		{
		}
	}

	public void SetLanguage(string lang)
	{
		if (!(S.Language == lang))
		{
			S.Language = lang;
			Loc.Lang = lang;
			_dock = null;
			_lastDateLang = null;
			Sections.Weather.Invalidate(this);
			SpHide.X = (SpHide.Target = 0.0);
			SnapPill();
			SaveState();
			Kick();
		}
	}

	public void ToggleGlass()
	{
		S.GlassEnabled = !S.GlassEnabled;
		S.GlassOpacity = 0.95;
		Theme.SetGlass(S.GlassEnabled);
		Nsp("opt:glass").Target = (S.GlassEnabled ? 1.0 : 0.0);
		SaveState();
		ApplyBackground();
		Kick();
	}

	public void SetGlassOpacity(double opacity)
	{
		S.GlassOpacity = 0.95;
		Theme.SetGlass(S.GlassEnabled);
		SaveState();
		ApplyBackground();
		Kick();
	}

	public void ToggleNotify()
	{
		S.NotifyEnabled = !S.NotifyEnabled;
		NotificationService.IsEnabled = S.NotifyEnabled;
		if (S.NotifyEnabled)
		{
			NotificationService.InitializeAsync();
		}
		Nsp("opt:notify").Target = (S.NotifyEnabled ? 1.0 : 0.0);
		SaveState();
		Kick();
	}

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
		if (IsCoveredHidden && !Host.IsVisible)
		{
			Host.ShowWindow();
		}
		Kick();
	}

	public void ShowNotification(NotificationItem item, double durationSec = 3.5)
	{
		Dispatcher.InvokeAsync(() =>
		{
			if (S.NotifyEnabled && !(Target > 0.1))
			{
				ActiveNotification = item;
				_notifySummoned = true;
				if (!Host.IsVisible)
				{
					Host.ShowWindow();
				}
				SpHide.Target = 0.0;
				SpNotify.X = 0.0;
				SpNotify.Target = 1.0;
				_notifyTimer.Stop();
				_notifyTimer.Interval = TimeSpan.FromSeconds(durationSec);
				_notifyTimer.Start();
				StartFrames();
				Kick();
			}
		});
	}

	public void OnNotificationReceived(NotificationItem item)
	{
		ShowNotification(item, 5.5);
	}

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
		bool flag3;
		switch (name)
		{
		default:
			return;
		case "dim_enabled":
		{
			bool flag = (S.DimEnabled = !S.DimEnabled);
			flag3 = flag;
			UpdateDim();
			break;
		}
		case "leave_collapse":
		{
			bool flag = (S.LeaveCollapse = !S.LeaveCollapse);
			flag3 = flag;
			if (!flag3)
			{
				_leaveTimer.Stop();
			}
			break;
		}
		case "hide_when_covered":
		{
			bool flag = (S.HideWhenCovered = !S.HideWhenCovered);
			flag3 = flag;
			CheckCover();
			break;
		}
		case "robot_companion":
		{
			bool flag = (S.RobotCompanion = !S.RobotCompanion);
			flag3 = flag;
			if (!flag3 && PetWindow != null)
			{
				PetWindow.Hide();
			}
			else if (flag3 && S.RobotRoaming)
			{
				LaunchAeroToDesktop();
			}
			break;
		}
		}
		Nsp("opt:" + name).Target = (flag3 ? 1.0 : 0.0);
		SaveState();
		Kick();
	}

	public void SetKeepAwake(bool on)
	{
		try
		{
			SystemService.SetKeepAwake(on);
		}
		catch (Exception ex)
		{
			Alert(Loc.T("Не удалось изменить режим:\n" + ex.Message, "Failed to change mode:\n" + ex.Message, "Mod değiştirilemedi:\n" + ex.Message));
			return;
		}
		S.KeepAwake = on;
		Nsp("sys:awake").Target = (on ? 1.0 : 0.0);
		SaveState();
		Kick();
	}

	public void ToggleAutostart()
	{
		bool flag = !Autostart;
		try
		{
			SystemService.SetAutostart(flag);
		}
		catch (Exception ex)
		{
			Alert(Loc.T("Не удалось изменить автозапуск:\n" + ex.Message, "Failed to change autostart:\n" + ex.Message, "Otomatik başlatma değiştirilemedi:\n" + ex.Message));
			return;
		}
		Autostart = flag;
		Nsp("autostart").Target = (flag ? 1.0 : 0.0);
		Kick();
	}

	public void RefreshSnapshot()
	{
		if (!_snapBusy)
		{
			_snapBusy = true;
			Bg.Run(SystemService.GetSnapshot, (SystemSnapshot? res, Exception? err) =>
			{
				_snapBusy = false;
				Snapshot = ((err == null) ? res : null);
				SystemHost?.RefreshAll();
			});
		}
	}

	private static (int? Pct, bool Charging) ReadBattery()
	{
		if (!WinApi.GetSystemPowerStatus(out var status))
		{
			return (Pct: null, Charging: false);
		}
		bool item = status.ACLineStatus == 1;
		if (status.BatteryLifePercent != byte.MaxValue)
		{
			return (Pct: Math.Min(100, (int)status.BatteryLifePercent), Charging: item);
		}
		return (Pct: null, Charging: item);
	}

	public void RefreshMedia(int delayMs = 0)
	{
		if (delayMs > 0)
		{
			RunLater(delayMs, () =>
			{
				RefreshMedia();
			});
		}
		else
		{
			if (_mediaBusy)
			{
				return;
			}
			_mediaBusy = true;
			Bg.RunAsync(MediaService.ReadAsync, (MediaState? res, Exception? err) =>
			{
				_mediaBusy = false;
				if (err == null && res != null)
				{
					if (Clock.Now < _playToggleCooldown)
					{
						res.Playing = Media.Playing;
					}
					bool flag = SetCover(res.Cover);
					res.Cover = null;
					bool num = Media.Active != res.Active || Media.Playing != res.Playing || Media.Title != res.Title || Media.Artist != res.Artist || Media.App != res.App || Media.Dur != res.Dur || Media.CanSeek != res.CanSeek;
					if (res.Playing)
					{
						_pauseTimestamp = 0.0;
						_pauseCollapseTimer?.Stop();
					}
					else if (!string.IsNullOrEmpty(res.Title) && (Media.Playing || _pauseTimestamp == 0.0))
					{
						_pauseTimestamp = Clock.Now;
						StartPauseCollapseTimer();
					}
					Media = res;
					if (num | flag)
					{
						ApplyMedia();
					}
				}
			});
		}
	}

	public double MediaPos()
	{
		return Media.CurrentPos();
	}

	public void NextTrack()
	{
		Bg.RunAsync(MediaService.NextAsync, (bool _, Exception? _) =>
		{
			RefreshMedia(400);
		});
	}

	public void PrevTrack()
	{
		Bg.RunAsync(MediaService.PreviousAsync, (bool _, Exception? _) =>
		{
			RefreshMedia(400);
		});
	}

	public void TogglePlay()
	{
		_playToggleCooldown = Clock.Now + 0.75;
		SetPlaying(!Media.Playing);
		Bg.RunAsync(MediaService.TogglePlayPauseAsync, (bool _, Exception? _) =>
		{
			RefreshMedia(600);
		});
	}

	public void SetPlaying(bool playing)
	{
		Media.Pos = MediaPos();
		Media.Stamp = DateTime.UtcNow;
		Media.Playing = playing;
		if (playing)
		{
			_pauseTimestamp = 0.0;
			_pauseCollapseTimer?.Stop();
		}
		else
		{
			_pauseTimestamp = Clock.Now;
			StartPauseCollapseTimer();
		}
		ApplyMedia();
	}

	private bool SetCover(byte[]? data)
	{
		bool num;
		if (data != null)
		{
			if (_coverData == null)
			{
				goto IL_0033;
			}
			num = data.AsSpan().SequenceEqual(_coverData);
		}
		else
		{
			num = _coverData == null;
		}
		if (num)
		{
			return false;
		}
		goto IL_0033;
		IL_0033:
		_coverData = data;
		CoverBrush = null;
		if (data != null)
		{
			try
			{
				BitmapImage bitmapImage = new BitmapImage();
				using MemoryStream streamSource = new MemoryStream(data);
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.DecodePixelWidth = 240;
				bitmapImage.StreamSource = streamSource;
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				ImageBrush imageBrush = new ImageBrush(bitmapImage)
				{
					Stretch = Stretch.UniformToFill
				};
				imageBrush.Freeze();
				CoverBrush = imageBrush;
			}
			catch
			{
				CoverBrush = null;
			}
		}
		return true;
	}

	private void ApplyMedia()
	{
		Nsp("play").Target = (Media.Playing ? 1.0 : 0.0);
		bool flag = !string.IsNullOrEmpty(Media.Title);
		if (Media.Playing)
		{
			_pauseTimestamp = 0.0;
		}
		else if (flag && _pauseTimestamp == 0.0)
		{
			_pauseTimestamp = Clock.Now;
		}
		bool flag2 = flag && (Media.Playing || (_pauseTimestamp > 0.0 && Clock.Now - _pauseTimestamp < 10.0));
		SpLive.Target = (flag2 ? 1.0 : 0.0);
		UpdateEq();
		PetWindow?.RefreshMusicState();
		NotchHost?.RefreshAll();
		StartFrames();
		Kick();
	}

	private void UpdateEq()
	{
		bool flag = Target == 0.0 && SpLive.Target == 1.0;
		bool flag2 = Target > 0.05 && S.RobotCompanion && !S.RobotRoaming;
		bool flag3 = Media.Playing && SpHide.Target == 0.0 && (flag | flag2);
		if (flag3 && !_eqTimer.IsEnabled)
		{
			_eqTimer.Start();
		}
		else if (!flag3 && _eqTimer.IsEnabled)
		{
			_eqTimer.Stop();
		}
	}

	public void Expand()
	{
		if (CapsuleHost != null && CapsuleHost.Visibility != Visibility.Visible)
		{
			CapsuleHost.Visibility = Visibility.Visible;
			TopBarHost?.RefreshAll();
			DockHost?.RefreshSelected(CurrentTool);
		}
		SetTarget(1.0);
	}

	public void Collapse()
	{
		_summoned = false;
		_hits.Clear();
		SetTarget(0.0);
		ApplyHidden();
	}

	private void SetTarget(double value)
	{
		if (Target != value)
		{
			if (value == 1.0 && SpNotify.Target > 0.0)
			{
				DismissNotification();
			}
			Target = value;
			SpW.Target = value;
			SpH.Target = value;
			SpHover.Target = 0.0;
			_leaveTimer.Stop();
			UpdateDim();
			UpdateEq();
			StartFrames();
		}
	}

	private void UpdateDim()
	{
		double num = ((S.DimEnabled && _fullscreen && !_summoned && Target == 0.0 && _hoverKey == null) ? 1.0 : 0.0);
		if (SpDim.Target != num)
		{
			SpDim.Target = num;
			StartFrames();
		}
	}

	public void StartFrames()
	{
		if (!_framing)
		{
			_framing = true;
			_lastFrameTs = Stopwatch.GetTimestamp() - (long)((double)Stopwatch.Frequency * 0.016);
			CompositionTarget.Rendering += OnRendering;
		}
	}

	private void OnRendering(object? sender, EventArgs e)
	{
		try
		{
			long timestamp = Stopwatch.GetTimestamp();
			double num = (double)(timestamp - _lastFrameTs) / (double)Stopwatch.Frequency;
			if (!(num <= 0.0005))
			{
				Frame(timestamp, num);
			}
		}
		catch (Exception ex)
		{
			App.LogCrash(ex, "IslandView.OnRendering");
			_framing = false;
		}
	}

	private void Frame(long now, double dt)
	{
		dt = Math.Min(dt, 0.033);
		_lastFrameTs = now;
		if (Target != 0.0)
		{
			// Органичная сбалансированная физика раскрытия Apple Dynamic Island
			SpW.Step(dt, 270.0, 25.5);
			SpH.Step(dt, 270.0, 25.5);
			SpHide.Step(dt, 240.0, 28.0);
		}
		else
		{
			// Четкое и быстрое закрытие назад в вырез без лишних колебаний
			SpW.Step(dt, 340.0, 32.0);
			SpH.Step(dt, 340.0, 32.0);
			SpHide.Step(dt, 240.0, 28.0);
		}
		SpHover.Step(dt, 320.0, 22.0);
		SpDim.Step(dt, 160.0, 24.0);
		SpLive.Step(dt, 260.0, 26.0);
		SpTab.Step(dt, 260.0, 28.5);
		SpPillX.Step(dt, 260.0, 28.5);
		SpPillW.Step(dt, 260.0, 28.5);
		SpNotify.Step(dt, 360.0, (SpNotify.Target > 0.0) ? 21.0 : 28.0);
		if (Target > 0.05 && S.RobotCompanion && !S.RobotRoaming)
		{
			UpdateRobotEyePhysics(dt);
		}
		bool flag = true;
		foreach (Spring value2 in _hs.Values)
		{
			if (!value2.Settled || value2.X != value2.Target)
			{
				value2.Step(dt, 560.0, 36.0);
				flag = false;
			}
		}
		bool flag2 = true;
		foreach (Spring value3 in _ps.Values)
		{
			if (!value3.Settled || value3.X != value3.Target)
			{
				value3.Step(dt, 460.0, 24.0);
				flag2 = false;
			}
		}
		bool flag3 = true;
		string key;
		Spring value;
		foreach (KeyValuePair<string, Spring> item3 in _named)
		{
			item3.Deconstruct(out key, out value);
			string text = key;
			Spring spring = value;
			if (!spring.Settled || spring.X != spring.Target)
			{
				if (text.Contains("scroll"))
				{
					spring.Step(dt, 300.0, 36.0);
				}
				else
				{
					spring.Step(dt, 560.0, 36.0);
				}
				flag3 = false;
			}
		}
		bool flag4 = (flag & flag2 & flag3) && SpW.Settled && SpH.Settled && SpHover.Settled && SpDim.Settled && SpLive.Settled && SpTab.Settled && SpPillX.Settled && SpPillW.Settled && SpHide.Settled && SpNotify.Settled;
		if (Target > 0.05 && S.RobotCompanion && !S.RobotRoaming)
		{
			flag4 = flag4 && SpBotEyeX.Settled && SpBotEyeY.Settled && SpBotHappy.Settled && SpBotHop.Settled && _botBlinkState <= 0.01 && _botEmotionTimer <= 0.0 && _botBubbleTime <= 0.0;
		}
		if (Target > 0.05 && CurrentTool == "ai" && Sections.Ai.IsGenerating)
		{
			flag4 = false;
		}
		if (flag4)
		{
			SnapCoreSprings();
			foreach (Spring value4 in _hs.Values)
			{
				value4.Snap();
			}
			foreach (Spring value5 in _ps.Values)
			{
				value5.Snap();
			}
			foreach (Spring value6 in _named.Values)
			{
				value6.Snap();
			}
			if (SpNotify.Target == 0.0)
			{
				ActiveNotification = null;
				ToastHost?.HideNotification();
			}
			_prevTab = null;
			_framing = false;
			CompositionTarget.Rendering -= OnRendering;
			if (_hs.Count > 24)
			{
				List<string> list = null;
				foreach (KeyValuePair<string, Spring> h2 in _hs)
				{
					h2.Deconstruct(out key, out value);
					string item = key;
					Spring spring2 = value;
					if (spring2.Settled && spring2.Target == 0.0 && spring2.X == 0.0)
					{
						if (list == null)
						{
							list = new List<string>();
						}
						list.Add(item);
					}
				}
				if (list != null)
				{
					foreach (string item4 in list)
					{
						_hs.Remove(item4);
					}
				}
			}
			if (_ps.Count > 24)
			{
				List<string> list2 = null;
				foreach (KeyValuePair<string, Spring> p in _ps)
				{
					p.Deconstruct(out key, out value);
					string item2 = key;
					Spring spring3 = value;
					if (spring3.Settled && spring3.Target == 0.0 && spring3.X == 0.0)
					{
						if (list2 == null)
						{
							list2 = new List<string>();
						}
						list2.Add(item2);
					}
				}
				if (list2 != null)
				{
					foreach (string item5 in list2)
					{
						_ps.Remove(item5);
					}
				}
			}
		}
		double num = Ui.Clamp(SpH.X);
		double t = Ui.Clamp(SpW.X);
		double tabT = Ui.Clamp(SpTab.X);
		bool inTabSwitch = _prevTab != null && (!SpTab.Settled || tabT < 0.99);
		if (_prevTab != null && (SpTab.Settled || tabT >= 0.99))
		{
			_prevTab = null;
			inTabSwitch = false;
		}
		UpdateViewHost(HomeHost, "home");
		UpdateViewHost(SoundHost, "sound");
		UpdateViewHost(MediaHost, "media");
		UpdateViewHost(SystemHost, "system");
		UpdateViewHost(SettingsHost, "settings");
		UpdateViewHost(ClipboardHost, "clipboard");
		UpdateViewHost(AiHost, "ai");
		UpdateViewHost(LinksHost, "links");
		UpdateViewHost(WeatherHost, "weather");
		UpdateViewHost(CalendarHost, "calendar");
		UpdateViewHost(PomodoroHost, "pomodoro");
		if (CapsuleHost != null)
		{
			if (num > 0.002)
			{
				if (CapsuleHost.Visibility != Visibility.Visible)
				{
					CapsuleHost.Visibility = Visibility.Visible;
				}
			}
			else if (CapsuleHost.Visibility != Visibility.Collapsed)
			{
				CapsuleHost.Visibility = Visibility.Collapsed;
			}
		}
		// Непрерывные размеры выреза для бесшовного морфинга из любого состояния
		double live = Ui.Clamp(SpLive.X);
		double a = Ui.Lerp(215.0, 365.0, live);
		double a2 = Ui.Lerp(38.0, 66.0, live);
		double initRadius = Ui.Lerp(18.0, 22.0, live);

		double hideVal = Ui.Clamp(SpHide.X);
		double startW = Ui.Lerp(a, 80.0, hideVal);
		double startH = Ui.Lerp(a2, 5.0, hideVal);
		double startR = Ui.Lerp(initRadius, 2.5, hideVal);

		double num2 = Ui.Lerp(startW, ExpandedW, t);
		double height = Ui.Lerp(startH, ExpandedH, num);
		double num3 = Ui.Lerp(startR, 32.0, num);
		if (CapsuleBorder != null)
		{
			CapsuleBorder.RenderTransform = null;
			CapsuleBorder.Width = num2;
			CapsuleBorder.Height = height;
			CapsuleBorder.CornerRadius = new CornerRadius(0.0, 0.0, num3, num3);
			CapsuleBorder.Opacity = 1.0;
		}
		if (ContentHost != null)
		{
			// Плавное проявление контента с мягким микромасштабированием Apple Bloom
			double contentT = Ui.Clamp((num - 0.15) / 0.75);
			double smoothContent = contentT * contentT * (3.0 - 2.0 * contentT);
			ContentHost.Opacity = smoothContent;
			ContentHost.IsHitTestVisible = num > 0.70;

			double scale = Ui.Lerp(0.94, 1.0, smoothContent);
			double translateY = Ui.Lerp(-6.0, 0.0, smoothContent);

			if (ContentHost.RenderTransform is not System.Windows.Media.TransformGroup tg || tg.Children.Count < 2)
			{
				tg = new System.Windows.Media.TransformGroup();
				tg.Children.Add(new System.Windows.Media.ScaleTransform(1.0, 1.0));
				tg.Children.Add(new System.Windows.Media.TranslateTransform(0.0, 0.0));
				ContentHost.RenderTransform = tg;
				ContentHost.RenderTransformOrigin = new Point(0.5, 0.1);
			}
			((System.Windows.Media.ScaleTransform)tg.Children[0]).ScaleX = scale;
			((System.Windows.Media.ScaleTransform)tg.Children[0]).ScaleY = scale;
			((System.Windows.Media.TranslateTransform)tg.Children[1]).Y = translateY;

			if (num >= 0.995 || num <= 0.01)
			{
				if (ContentHost.Clip != null)
				{
					ContentHost.Clip = null;
				}
			}
			else
			{
				double x = (ExpandedW - num2) / 2.0;
				if (ContentHost.Clip is RectangleGeometry rectangleGeometry)
				{
					rectangleGeometry.Rect = new Rect(x, 0.0, num2, height);
					rectangleGeometry.RadiusX = num3;
					rectangleGeometry.RadiusY = num3;
				}
				else
				{
					ContentHost.Clip = new RectangleGeometry(new Rect(x, 0.0, num2, height), num3, num3);
				}
			}
		}
		double num4 = Ui.Clamp(SpNotify.X);
		if (ToastHost != null && (ActiveNotification != null || num4 > 0.01))
		{
			if (ToastHost.Visibility != Visibility.Visible && ActiveNotification != null)
			{
				ToastHost.ShowNotification(ActiveNotification);
			}
			double w = Ui.Lerp(a, 430.0, num4);
			double h = Ui.Lerp(a2, 68.0, num4);
			double radius = Ui.Lerp(14.0, 24.0, num4);
			ToastHost.UpdateMorph(w, h, radius, num4);
			ToastHost.Opacity = Ui.Clamp(num4 * 3.0);
			if (NotchHost != null)
			{
				if (num4 < 0.95 && num <= 0.15)
				{
					if (NotchHost.Visibility != Visibility.Visible)
					{
						NotchHost.Visibility = Visibility.Visible;
						NotchHost.RefreshAll();
					}
					double hide = Ui.Clamp(SpHide.X);
					double notchHv = Hv("summon:notch_bar");
					NotchHost.UpdateMorph(hide, notchHv);
					NotchHost.Opacity = Ui.Clamp(1.0 - num4 * 2.5);
				}
				else if (NotchHost.Visibility != Visibility.Collapsed)
				{
					NotchHost.Visibility = Visibility.Collapsed;
				}
			}
			if (num4 <= 0.02 && SpNotify.Target == 0.0)
			{
				ActiveNotification = null;
				ToastHost.HideNotification();
				if (NotchHost != null && num <= 0.15)
				{
					NotchHost.Visibility = Visibility.Visible;
					NotchHost.Opacity = 1.0;
					double hide2 = Ui.Clamp(SpHide.X);
					double notchHv2 = Hv("summon:notch_bar");
					NotchHost.UpdateMorph(hide2, notchHv2);
				}
			}
		}
		else
		{
			if (ToastHost != null && ToastHost.Visibility != Visibility.Collapsed)
			{
				ToastHost.HideNotification();
			}
			if (NotchHost != null)
			{
				if (num > 0.02)
				{
					if (NotchHost.Visibility != Visibility.Collapsed)
					{
						NotchHost.Visibility = Visibility.Collapsed;
					}
				}
				else
				{
					if (NotchHost.Visibility != Visibility.Visible)
					{
						NotchHost.Visibility = Visibility.Visible;
						NotchHost.RefreshAll();
					}
					double hide3 = Ui.Clamp(SpHide.X);
					double notchHv3 = Hv("summon:notch_bar");
					NotchHost.UpdateMorph(hide3, notchHv3);
					NotchHost.Opacity = Ui.Clamp(1.0 - num / 0.02);
				}
			}
		}
		double num5 = ((ActualWidth > 0.0) ? ActualWidth : 920.0);
		if (num > 0.05 || Target == 1.0)
		{
			_currentIslandRect = new Rect((num5 - ExpandedW) / 2.0, 0.0, ExpandedW, ExpandedH);
			_currentIslandRadius = 32.0;
		}
		else if (num4 > 0.02 && (ActiveNotification != null || num4 > 0.01))
		{
			double num6 = Ui.Lerp(a, 430.0, num4);
			double height2 = Ui.Lerp(a2, 68.0, num4);
			_currentIslandRect = new Rect((num5 - num6) / 2.0, 0.0, num6, height2);
			_currentIslandRadius = Ui.Lerp(14.0, 24.0, num4);
		}
		else
		{
			double a3 = (Media.Playing ? 340 : 246);
			double a4 = ((S.IslandStyle == "notch") ? 32 : ((S.IslandStyle == "compact") ? 30 : 34));
			double a5 = ((S.IslandStyle == "notch") ? 14 : ((S.IslandStyle == "compact") ? 15 : 17));
			double t2 = Ui.Clamp(SpHide.X);
			double num7 = Hv("summon:notch_bar");
			double b = 120.0 + 4.0 * num7;
			double b2 = 4.0 + 0.8 * num7;
			double b3 = 2.0;
			double num8 = Ui.Lerp(a3, b, t2);
			double height3 = Ui.Lerp(a4, b2, t2);
			double currentIslandRadius = Ui.Lerp(a5, b3, t2);
			_currentIslandRect = new Rect((num5 - num8) / 2.0, 0.0, num8, height3);
			_currentIslandRadius = currentIslandRadius;
		}
		double num9 = ((Target == 0.0 && !_summoned) ? Math.Round(1.0 - S.DimLevel * Ui.Clamp(SpDim.X), 3) : 1.0);
		if (num9 != _alpha)
		{
			_alpha = num9;
			Host.Opacity = num9;
		}
		if (flag4 && SpHide.Target == 1.0 && !_covered)
		{
			Host.HideWindow();
		}
		static void RefreshHost(UserControl? host)
		{
			if (host is HomeView homeView)
			{
				homeView.RefreshAll();
			}
			else if (host is SoundView soundView)
			{
				soundView.RefreshAll();
			}
			else if (host is MediaView mediaView)
			{
				mediaView.RefreshAll();
			}
			else if (host is SystemView systemView)
			{
				systemView.RefreshAll();
			}
			else if (host is SettingsView settingsView)
			{
				settingsView.RefreshAll();
			}
			else if (host is ClipboardView clipboardView)
			{
				clipboardView.RefreshAll();
			}
			else if (host is AiView aiView)
			{
				aiView.RefreshAll();
			}
			else if (host is LinksView linksView)
			{
				linksView.RefreshAll();
			}
			else if (host is WeatherView weatherView)
			{
				weatherView.RefreshAll();
			}
			else if (host is CalendarView calendarView)
			{
				calendarView.RefreshAll();
			}
		}
		void UpdateViewHost(UserControl? host, string toolKey)
		{
			if (host != null)
			{
				if (toolKey == CurrentTool)
				{
					if (host.Visibility != Visibility.Visible)
					{
						host.Visibility = Visibility.Visible;
						RefreshHost(host);
					}
					if (inTabSwitch)
					{
						double num10 = (host.Opacity = tabT * tabT * (3.0 - 2.0 * tabT));
						double x2 = (double)_tabDir * 22.0 * (1.0 - num10);
						TranslateTransform translateTransform = host.RenderTransform as TranslateTransform;
						if (translateTransform == null)
						{
							translateTransform = (TranslateTransform)(host.RenderTransform = new TranslateTransform());
						}
						translateTransform.X = x2;
					}
					else
					{
						host.Opacity = 1.0;
						if (host.RenderTransform is TranslateTransform { X: not 0.0 } translateTransform2)
						{
							translateTransform2.X = 0.0;
						}
					}
				}
				else if (inTabSwitch && toolKey == _prevTab)
				{
					double num12 = 1.0 - tabT * tabT * (3.0 - 2.0 * tabT);
					if (num12 > 0.01)
					{
						if (host.Visibility != Visibility.Visible)
						{
							host.Visibility = Visibility.Visible;
						}
						host.Opacity = num12;
						double x3 = (double)(-_tabDir) * 22.0 * (1.0 - num12);
						TranslateTransform translateTransform3 = host.RenderTransform as TranslateTransform;
						if (translateTransform3 == null)
						{
							translateTransform3 = (TranslateTransform)(host.RenderTransform = new TranslateTransform());
						}
						translateTransform3.X = x3;
					}
					else if (host.Visibility != Visibility.Collapsed)
					{
						host.Visibility = Visibility.Collapsed;
						if (host.RenderTransform is TranslateTransform translateTransform4)
						{
							translateTransform4.X = 0.0;
						}
					}
				}
				else if (host.Visibility != Visibility.Collapsed)
				{
					host.Visibility = Visibility.Collapsed;
					if (host.RenderTransform is TranslateTransform translateTransform5)
					{
						translateTransform5.X = 0.0;
					}
				}
			}
		}
	}

	private void SnapCoreSprings()
	{
		SpW.Snap();
		SpH.Snap();
		SpHover.Snap();
		SpDim.Snap();
		SpLive.Snap();
		SpTab.Snap();
		SpPillX.Snap();
		SpPillW.Snap();
		SpHide.Snap();
		SpNotify.Snap();
		SpBotEyeX.Snap();
		SpBotEyeY.Snap();
		SpBotHappy.Snap();
		SpBotHop.Snap();
	}

	private void TickSecond()
	{
		DateTime utcNow = DateTime.UtcNow;
		bool flag = (utcNow - _lastTick).TotalSeconds > 5.0;
		_lastTick = utcNow;
		if (flag)
		{
			_lastFrameTs = Stopwatch.GetTimestamp();
			_alpha = -1.0;
			StartFrames();
			RunLater(7000, () =>
			{
				Sections.Weather.OnSystemResumed(this);
			});
		}
		double now = Clock.Now;
		if (flag || now - _lastBattery >= 10.0)
		{
			UpdateBattery();
		}
		if (S.DimEnabled)
		{
			try
			{
				_fullscreen = WinApi.FullscreenAppActive(Host.Handle);
			}
			catch
			{
				_fullscreen = false;
			}
			UpdateDim();
		}
		else if (_fullscreen)
		{
			_fullscreen = false;
			UpdateDim();
		}
		if (flag || now - _lastThemeCheck >= 20.0)
		{
			_lastThemeCheck = now;
			if (Theme.UpdateSystemTheme(SystemService.GetDarkTheme()))
			{
				Kick();
			}
		}
		double num;
		if (Target == 1.0)
		{
			num = 4.0;
		}
		else
		{
			num = (Live ? 12.0 : 25.0);
		}
		if (now - _lastMediaPoll >= num)
		{
			_lastMediaPoll = now;
			RefreshMedia();
		}
		if (Target == 1.0)
		{
			Sections.ByKey(CurrentTool).Tick(this);
		}
		if (!Media.Playing && _pauseTimestamp > 0.0 && SpLive.Target == 1.0 && Clock.Now - _pauseTimestamp >= 10.0)
		{
			ApplyMedia();
		}
		if (!flag && now - _lastWeatherCheck >= 60.0)
		{
			_lastWeatherCheck = now;
			Sections.Weather.Ensure(this);
		}
		if (!_framing && Target == 1.0)
		{
			InvalidateVisual();
		}
	}

	public void CheckCover()
	{
		if (Target != 0.0)
		{
			if (!S.LeaveCollapse || _dragKey != null || !WinApi.GetCursorPos(out var lpPoint))
			{
				return;
			}
			try
			{
				Point point = PointFromScreen(new Point(lpPoint.X, lpPoint.Y));
				if (!_currentIslandRect.Contains(point))
				{
					StartLeaveTimer();
				}
				else
				{
					StopLeaveTimer();
				}
				return;
			}
			catch
			{
				return;
			}
		}
		if (!S.HideWhenCovered)
		{
			if (_covered || SpHide.Target != 0.0)
			{
				_covered = false;
				SpHide.Target = 0.0;
				if (!Host.IsVisible)
				{
					Host.ShowWindow();
				}
				StartFrames();
			}
			return;
		}
		try
		{
			_fullscreen = WinApi.FullscreenAppActive(Host.Handle);
		}
		catch
		{
			_fullscreen = false;
		}
		double dpiScaleX = VisualTreeHelper.GetDpi(this).DpiScaleX;
		double num = SystemParameters.PrimaryScreenWidth * dpiScaleX;
		double num2 = 370.0 * dpiScaleX;
		int num3 = (int)(num / 2.0 - num2 / 2.0);
		bool flag = _fullscreen || WinApi.WindowCoversArea((uint)Environment.ProcessId, num3, 0, num3 + (int)num2, (int)(36.0 * dpiScaleX));
		if (flag != _covered)
		{
			_covered = flag;
			if (!flag)
			{
				_summoned = false;
			}
			ApplyHidden();
		}
	}

	private void ApplyHidden()
	{
		bool flag = _covered && !_summoned && !_notifySummoned;
		if (flag && Target == 1.0)
		{
			SetTarget(0.0);
		}
		double num = (flag ? 1.0 : 0.0);
		if (SpHide.Target != num)
		{
			if (!flag && !Host.IsVisible)
			{
				Host.ShowWindow();
			}
			SpHide.Target = num;
			UpdateEq();
			UpdateDim();
			StartFrames();
		}
	}

	public void OnHotkey()
	{
		if (Target == 1.0)
		{
			Collapse();
			return;
		}
		_summoned = true;
		ApplyHidden();
		Expand();
	}

	private (string Key, double X, double W)[] DockLayout()
	{
		if (_dock != null)
		{
			return _dock;
		}
		double[] array = Sections.Primary.Select((Section s) => _painter.TextWidth(s.Icon + " " + s.Title, 10.5, bold: true) + 24.0).ToArray();
		double num = (ExpandedW - (array.Sum() + 6.0 * (double)(array.Length - 1))) / 2.0;
		List<(string, double, double)> list = new List<(string, double, double)>();
		for (int num2 = 0; num2 < Sections.Primary.Length; num2++)
		{
			list.Add((Sections.Primary[num2].Key, num, array[num2]));
			num += array[num2] + 6.0;
		}
		return _dock = list.ToArray();
	}

	private (double X, double W) PillTarget(string key)
	{
		(string, double, double)[] array = DockLayout();
		for (int i = 0; i < array.Length; i++)
		{
			var (text, item, item2) = array[i];
			if (text == key)
			{
				return (X: item, W: item2);
			}
		}
		return (X: 0.0, W: 0.0);
	}

	private void SnapPill()
	{
		_painter.Ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
		var (target, target2) = PillTarget(CurrentTool);
		SpPillX.X = (SpPillX.Target = target);
		SpPillW.X = (SpPillW.Target = target2);
	}

	public void Select(string key)
	{
		if (!(key == CurrentTool))
		{
			List<string> list = Sections.All.Select((Section s) => s.Key).ToList();
			_tabDir = ((list.IndexOf(key) > list.IndexOf(CurrentTool)) ? 1 : (-1));
			_prevTab = CurrentTool;
			CurrentTool = key;
			SpTab.X = (SpTab.V = 0.0);
			SpTab.Target = 1.0;
			(SpPillX.Target, SpPillW.Target) = PillTarget(key);
			DockHost?.RefreshSelected(key);
			TopBarHost?.RefreshBreadcrumb();
			Sections.ByKey(key).Enter(this);
			StartFrames();
		}
	}

	private (double W, double H, double Radius) Metrics()
	{
		double x = SpW.X;
		double x2 = SpH.X;
		double x3 = SpHover.X;
		double num = Ui.Clamp(SpNotify.X);
		string islandStyle = S.IslandStyle;
		double num2;
		if (islandStyle == "notch")
		{
			num2 = 214.0;
		}
		else if (islandStyle == "compact")
		{
			num2 = ((Media.Playing || Live) ? 320.0 : (108.0 + 24.0 * x3));
		}
		else
		{
			num2 = 250.0;
		}
		double num3 = num2;
		islandStyle = S.IslandStyle;
		if (islandStyle == "notch")
		{
			num2 = 120.0;
		}
		else
		{
			num2 = ((!(islandStyle == "compact")) ? 120.0 : 0.0);
		}
		double num4 = num2;
		double num5 = num3 + num4 * Math.Max(0.0, SpLive.X);
		if (num > 0.001 && x < 0.2)
		{
			num5 = Math.Max(num5, 250.0 + 220.0 * Math.Max(0.0, num));
		}
		islandStyle = S.IslandStyle;
		if (islandStyle == "notch")
		{
			num2 = 1.0 + 0.035 * x3;
		}
		else
		{
			num2 = ((!(islandStyle == "compact")) ? (1.0 + 0.04 * x3) : (1.0 + 0.035 * x3));
		}
		double num6 = num2;
		islandStyle = S.IslandStyle;
		if (islandStyle == "notch")
		{
			num2 = 1.0 + 0.04 * x3;
		}
		else
		{
			num2 = ((!(islandStyle == "compact")) ? (1.0 + 0.045 * x3) : (1.0 + 0.045 * x3));
		}
		double num7 = num2;
		double item = Math.Max((num5 + (ExpandedW - num5) * x) * num6, 40.0);
		islandStyle = S.IslandStyle;
		if (islandStyle == "notch")
		{
			num2 = 33.0;
		}
		else
		{
			num2 = ((!(islandStyle == "compact")) ? 40.0 : ((double)((Media.Playing || Live) ? 34 : 28)));
		}
		double num8 = num2;
		double num9 = num8;
		if (num > 0.001 && x2 < 0.2)
		{
			num9 = num8 + 34.0 * Math.Max(0.0, num);
		}
		double item2 = Math.Max((num9 + (ExpandedH - num9) * x2) * num7, num8 * 0.8);
		double item3;
		if (num > 0.001 && x2 < 0.2)
		{
			item3 = num8 / 2.0 + (24.0 - num8 / 2.0) * Math.Min(1.0, Math.Max(0.0, num));
		}
		else
		{
			item3 = ((S.IslandStyle == "notch") ? (14.0 + 22.0 * Ui.Clamp(x2)) : (num8 / 2.0 + (36.0 - num8 / 2.0) * Ui.Clamp(x2)));
		}
		return (W: item, H: item2, Radius: item3);
	}

	public bool IsOnCapsule(Point windowPt)
	{
		NotchView notchHost = NotchHost;
		Border border = notchHost?.NotchCapsule;
		if (notchHost == null || border == null || Host == null)
		{
			return false;
		}
		if (notchHost.Visibility != Visibility.Visible || !border.IsVisible)
		{
			return false;
		}
		try
		{
			return border.TransformToAncestor(Host).TransformBounds(new Rect(border.RenderSize)).Contains(windowPt);
		}
		catch
		{
			return false;
		}
	}

	public bool HitTestIsland(Point p)
	{
		Rect currentIslandRect = _currentIslandRect;
		if (SpHide.X > 0.75)
		{
			if (_covered)
			{
				return IsOnCapsule(p);
			}
			return false;
		}
		if (Target == 0.0 && SpH.X < 0.05 && SpNotify.X <= 0.02)
		{
			return IsOnCapsule(p);
		}
		if (!currentIslandRect.Contains(p))
		{
			return false;
		}
		double currentIslandRadius = _currentIslandRadius;
		if (currentIslandRadius <= 0.0)
		{
			return true;
		}
		if (p.X < currentIslandRect.Left + currentIslandRadius && p.Y > currentIslandRect.Bottom - currentIslandRadius)
		{
			return (p - new Point(currentIslandRect.Left + currentIslandRadius, currentIslandRect.Bottom - currentIslandRadius)).LengthSquared <= currentIslandRadius * currentIslandRadius;
		}
		if (p.X > currentIslandRect.Right - currentIslandRadius && p.Y > currentIslandRect.Bottom - currentIslandRadius)
		{
			return (p - new Point(currentIslandRect.Right - currentIslandRadius, currentIslandRect.Bottom - currentIslandRadius)).LengthSquared <= currentIslandRadius * currentIslandRadius;
		}
		return true;
	}

	private void ApplyBounds()
	{
	}

	protected override void OnRender(DrawingContext dc)
	{
		if (_currentIslandRect.IsEmpty || _currentIslandRect.Width <= 0.0)
		{
			double num = ((ActualWidth > 0.0) ? ActualWidth : 920.0);
			_currentIslandRect = new Rect((num - 246.0) / 2.0, 0.0, 246.0, 32.0);
			_currentIslandRadius = 14.0;
		}
	}

	public void OnGlobalMouseMove(int screenX, int screenY)
	{
		if (!(Target <= 0.05) && S.RobotCompanion && !S.RobotRoaming)
		{
			double num = (double)screenX - _globalMousePos.X;
			double num2 = (double)screenY - _globalMousePos.Y;
			if (!(num * num + num2 * num2 < 25.0))
			{
				_globalMousePos = new Point(screenX, screenY);
				StartFrames();
			}
		}
	}

	public void PetRobot()
	{
		BotPhrase botPhrase = PickNextBotPhrase();
		_botEmotion = botPhrase.Emotion;
		_botEmotionTimer = 2.8;
		_botBubbleText = Loc.T(botPhrase.Ru, botPhrase.En, botPhrase.Tr);
		_botBubbleTime = Math.Max(2.8, 1.8 + (double)_botBubbleText.Length * 0.045);
		SpBotHop.V = -85.0;
		SpBotHappy.Target = 1.0;
		_botHappyTimer = 2.8;
		Kick();
	}

	private void UpdateRobotEyePhysics(double dt)
	{
		_botAnimPhase = (_botAnimPhase + dt * 2.8) % (Math.PI * 2000.0);
		double num = _globalMousePos.X - _botCenterScreen.X;
		double num2 = _globalMousePos.Y - _botCenterScreen.Y;
		double num3 = Math.Sqrt(num * num + num2 * num2);
		double val = 3.0;
		double target = 0.0;
		double target2 = 0.0;
		if (num3 > 3.0)
		{
			double num4 = Math.Min(num3 * 0.006, val);
			target = num / num3 * num4;
			target2 = num2 / num3 * num4;
		}
		SpBotEyeX.Target = target;
		SpBotEyeY.Target = target2;
		SpBotEyeX.Step(dt, 320.0, 23.0);
		SpBotEyeY.Step(dt, 320.0, 23.0);
		SpBotHappy.Step(dt, 300.0, 26.0);
		SpBotHop.Step(dt, 360.0, 24.0);
		if (_botEmotionTimer <= 0.0)
		{
			_botBlinkTimer -= dt;
			if (_botBlinkTimer <= 0.0)
			{
				_botBlinkState = 1.0;
				_botBlinkTimer = 3.5 + Random.Shared.NextDouble() * 3.0;
			}
			else if (_botBlinkState > 0.0)
			{
				_botBlinkState = Math.Max(0.0, _botBlinkState - dt * 8.0);
			}
		}
		else
		{
			_botBlinkState = 0.0;
		}
		if (_botEmotionTimer > 0.0)
		{
			_botEmotionTimer -= dt;
			if (_botEmotionTimer <= 0.0)
			{
				_botEmotion = "normal";
			}
		}
		if (_botBubbleTime > 0.0)
		{
			_botBubbleTime -= dt;
			if (_botBubbleTime <= 0.0)
			{
				_botBubbleText = "";
			}
		}
		if (_botHappyTimer > 0.0)
		{
			_botHappyTimer -= dt;
			if (_botHappyTimer <= 0.0 && Hv("robot:pet") <= 0.4)
			{
				SpBotHappy.Target = 0.0;
			}
		}
	}

	private string? HitAt(Point pos)
	{
		if (Target == 0.0 && SpH.X < 0.05)
		{
			return null;
		}
		for (int num = _hits.Count - 1; num >= 0; num--)
		{
			if (_hits[num].Rect.Contains(pos))
			{
				return _hits[num].Key;
			}
		}
		return null;
	}

	public void SetHover(string? key)
	{
		if (key == _hoverKey)
		{
			return;
		}
		string hoverKey = _hoverKey;
		_hoverKey = key;
		bool flag = Target == 0.0 && key != null && (key == "expand" || key.StartsWith("media:") || key == "open:weather" || key == "open:calendar" || key == "open:ai");
		SpHover.Target = (flag ? 1.0 : 0.0);
		if (key != null && !_hs.ContainsKey(key))
		{
			Spring spring = (_hs[key] = new Spring());
			spring.Target = 1.0;
		}
		foreach (KeyValuePair<string, Spring> h in _hs)
		{
			h.Value.Target = ((h.Key == key) ? 1.0 : 0.0);
		}
		Cursor = ((key != null) ? Cursors.Hand : Cursors.Arrow);
		switch (key)
		{
		case "open:notify":
		case "dismiss:notify":
			_notifyTimer.Stop();
			break;
		default:
			if (key != null)
			{
				break;
			}
			switch (hoverKey)
			{
			case "open:notify":
			case "dismiss:notify":
				if (SpNotify.Target == 1.0)
				{
					_notifyTimer.Stop();
					_notifyTimer.Interval = TimeSpan.FromSeconds(2.5);
					_notifyTimer.Start();
				}
				break;
			}
			break;
		}
		UpdateDim();
		StartFrames();
	}

	private void SetPress(string? key)
	{
		_pressKey = key;
		foreach (KeyValuePair<string, Spring> p in _ps)
		{
			p.Value.Target = ((p.Key == key) ? 1.0 : 0.0);
		}
		StartFrames();
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		Point position = e.GetPosition(this);
		if (_dragKey != null)
		{
			try
			{
				_globalMousePos = PointToScreen(position);
			}
			catch
			{
			}
			Sections.ByKey(CurrentTool).Drag(this, _dragKey, position);
			SetHover(HitAt(position));
			return;
		}
		long tickCount = Environment.TickCount64;
		double num = position.X - _lastMovePos.X;
		double num2 = position.Y - _lastMovePos.Y;
		if (num * num + num2 * num2 < 9.0 && tickCount - _lastMoveTicks < 14)
		{
			return;
		}
		_lastMovePos = position;
		_lastMoveTicks = tickCount;
		string text = HitAt(position);
		if (text != _hoverKey)
		{
			SetHover(text);
		}
		if (!(Target > 0.05) || !S.RobotCompanion || S.RobotRoaming)
		{
			return;
		}
		try
		{
			Point globalMousePos = PointToScreen(position);
			double num3 = globalMousePos.X - _globalMousePos.X;
			double num4 = globalMousePos.Y - _globalMousePos.Y;
			if (num3 * num3 + num4 * num4 >= 25.0)
			{
				_globalMousePos = globalMousePos;
				StartFrames();
			}
		}
		catch
		{
		}
	}

	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		Focus();
		Point position = e.GetPosition(this);
		string text = HitAt(position);
		if (text != null && text.StartsWith("slider:"))
		{
			_dragKey = text;
			CaptureMouse();
			Sections.ByKey(CurrentTool).Drag(this, text, position);
		}
		SetPress(text);
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
		Point position = e.GetPosition(this);
		if (_dragKey != null)
		{
			string dragKey = _dragKey;
			_dragKey = null;
			ReleaseMouseCapture();
			Sections.ByKey(CurrentTool).DragEnd(this, dragKey);
			SetPress(null);
			return;
		}
		string text = HitAt(position);
		string pressKey = _pressKey;
		SetPress(null);
		if (text != null && text == pressKey)
		{
			Activate(text);
		}
	}

	protected override void OnMouseWheel(MouseWheelEventArgs e)
	{
		if (Target == 1.0)
		{
			if (e.GetPosition(this).Y >= 482.0)
			{
				List<string> list = Sections.Primary.Select((Section s) => s.Key).ToList();
				int num = list.IndexOf(CurrentTool);
				if (num >= 0)
				{
					int index = ((e.Delta > 0) ? ((num - 1 + list.Count) % list.Count) : ((num + 1) % list.Count));
					Select(list[index]);
					return;
				}
			}
			Sections.ByKey(CurrentTool).Wheel(this, (double)e.Delta / 120.0);
		}
		else
		{
			WinApi.MediaKey((e.Delta > 0) ? "up" : "down");
			Sections.Sound.RefreshVolume(this);
			Kick();
		}
	}

	public void StartLeaveTimer()
	{
		if (Target == 1.0 && S.LeaveCollapse && _dragKey == null && !_leaveTimer.IsEnabled)
		{
			double value = Math.Clamp(S.LeaveDelay, 0.4, 2.0);
			_leaveTimer.Interval = TimeSpan.FromSeconds(value);
			_leaveTimer.Start();
		}
	}

	public void StopLeaveTimer()
	{
		if (_leaveTimer.IsEnabled)
		{
			_leaveTimer.Stop();
		}
	}

	public void OnCursorLeftIsland()
	{
		if (Target != 1.0 || !S.LeaveCollapse || _dragKey != null)
		{
			return;
		}
		try
		{
			if (WinApi.GetCursorPos(out var lpPoint))
			{
				Point point = PointFromScreen(new Point(lpPoint.X, lpPoint.Y));
				if (_currentIslandRect.Contains(point))
				{
					return;
				}
			}
		}
		catch
		{
		}
		StartLeaveTimer();
	}

	protected override void OnMouseEnter(MouseEventArgs e)
	{
		StopLeaveTimer();
	}

	protected override void OnMouseLeave(MouseEventArgs e)
	{
		if (_dragKey == null)
		{
			OnCursorLeftIsland();
			SetHover(null);
		}
	}

	private void Activate(string key)
	{
		switch (key)
		{
		case "dismiss:notify":
			DismissNotification();
			break;
		case "open:notify":
		{
			Action action = ActiveNotification?.OnClick;
			DismissNotification();
			if (action != null)
			{
				action();
				break;
			}
			_summoned = _covered;
			Expand();
			break;
		}
		case "media:play":
		case "media:toggle":
		case "home:play":
			TogglePlay();
			break;
		case "media:prev":
		case "home:prev":
			PrevTrack();
			break;
		case "media:next":
		case "home:next":
			NextTrack();
			break;
		case "summon:notch_bar":
			SummonFromEdge();
			break;
		case "open:weather":
			Select("weather");
			Expand();
			break;
		case "open:ai":
			Select("ai");
			Expand();
			break;
		case "robot:pet":
			PetRobot();
			break;
		case "robot:launch":
			LaunchAeroToDesktop();
			break;
		case "robot:dock":
			DockAero();
			break;
		case "open:calendar":
		case "go:calendar":
			if (CurrentTool == "calendar")
			{
				Select("home");
				break;
			}
			Select("calendar");
			Expand();
			break;
		case "expand":
			Expand();
			break;
		case "collapse":
			Collapse();
			break;
		case "close":
			foreach (Toast item in Toasts.ToList())
			{
				item.Dismiss();
			}
			Application.Current.Shutdown();
			break;
		case "back:home":
			Select("home");
			break;
		default:
			if (key.StartsWith("tab:"))
			{
				Select(key.Substring(4, key.Length - 4));
				break;
			}
			Sections.ByKey(CurrentTool).Click(this, key);
			_hoverKey = null;
			Kick();
			break;
		}
	}

	private void AutoCollapse()
	{
		if (_dragKey != null || Busy != 0)
		{
			return;
		}
		if (WinApi.GetCursorPos(out var lpPoint))
		{
			try
			{
				Point point = PointFromScreen(new Point(lpPoint.X, lpPoint.Y));
				if (_currentIslandRect.Contains(point))
				{
					return;
				}
			}
			catch
			{
			}
		}
		if (Target == 1.0 && _hoverKey == null)
		{
			Collapse();
		}
	}
}


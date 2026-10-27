using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace QuickNotchCs;

public sealed class NotificationItem
{
    public uint Id { get; init; }
    public string App { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? IconSymbol { get; init; }
    public Color? AccentColor { get; init; }
    public Action? OnClick { get; init; }
    public bool UseCoverArt { get; init; }
}

public static class NotificationService
{
    private static UserNotificationListener? _listener;
    private static CancellationTokenSource? _cts;
    private static readonly HashSet<uint> _knownIds = new();
    private static bool _initialized;

    public static volatile bool IsEnabled = true;

    public static event Action<NotificationItem>? NotificationReceived;

    // Системный перехват всплывающих окон тостов через WinEventHook (0% CPU + мгновенная реакция)
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const uint WINEVENT_SKIPOWNPROCESS = 2;

    private static IntPtr _winEventHook = IntPtr.Zero;
    private static WinEventDelegate? _winEventDelegate;
    private static readonly SemaphoreSlim _wakeSignal = new(0, 1);

    public static async Task<bool> InitializeAsync()
    {
        if (_initialized) return _listener != null;
        _initialized = true;

        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                return false;
            }

            // Запоминаем текущие тосты, чтобы не спамить старыми при старте программы
            try
            {
                var initial = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                lock (_knownIds)
                {
                    foreach (var n in initial) _knownIds.Add(n.Id);
                }
            }
            catch { }

            try
            {
                _listener.NotificationChanged += Listener_NotificationChanged;
            }
            catch { }

            // Регистрируем WinEventHook для перехвата появления окон уведомлений Windows в реальном времени
            try
            {
                _winEventDelegate = OnWinEvent;
                _winEventHook = SetWinEventHook(
                    EVENT_OBJECT_CREATE,
                    EVENT_OBJECT_SHOW,
                    IntPtr.Zero,
                    _winEventDelegate,
                    0, 0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            }
            catch { }

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_cts.Token));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Shutdown()
    {
        _cts?.Cancel();
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }

    private static void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero) return; // OBJID_WINDOW = 0
        try
        {
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, 64);
            string cls = sb.ToString();
            // Классы системных тостов Windows (UWP CoreWindow баннеров, ToastView, Action Center)
            if (cls.Contains("CoreWindow") || cls.Contains("Toast") || cls.Contains("Notification"))
            {
                if (_wakeSignal.CurrentCount == 0) _wakeSignal.Release();
            }
        }
        catch { }
    }

    public static void TriggerTest()
    {
        var item = new NotificationItem
        {
            Id = (uint)Random.Shared.Next(10000, 99999),
            App = "Quick Notch",
            Title = "Уведомления работают!",
            Body = "Dynamic Island мгновенно перехватывает тосты Windows.",
            Timestamp = DateTime.UtcNow
        };
        NotificationReceived?.Invoke(item);
    }

    private static void Listener_NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (!IsEnabled) return;
        if (_wakeSignal.CurrentCount == 0) _wakeSignal.Release();
    }

    private static NotificationItem? TryParse(UserNotification n)
    {
        bool isNew;
        lock (_knownIds)
        {
            isNew = _knownIds.Add(n.Id);
        }
        if (!isNew) return null;

        try
        {
            var visual = n.Notification?.Visual;
            if (visual == null) return null;

            var binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric)
                          ?? (visual.Bindings != null && visual.Bindings.Count > 0 ? visual.Bindings[0] : null);

            var texts = binding?.GetTextElements();
            string title = texts != null && texts.Count > 0 ? (texts[0].Text ?? "").Trim() : "";
            string body = texts != null && texts.Count > 1 ? (texts[1].Text ?? "").Trim() : "";

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
            if (string.IsNullOrWhiteSpace(app)) app = "Windows";

            return new NotificationItem
            {
                Id = n.Id,
                App = app,
                Title = title,
                Body = body,
                Timestamp = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Ждём системного события тоста (через хук окон или NotificationChanged), таймаут 25 сек для подстраховки
                try { await _wakeSignal.WaitAsync(25000, token); }
                catch (OperationCanceledException) { break; }

                if (!IsEnabled || _listener == null) continue;

                var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                var currentIds = new HashSet<uint>();

                foreach (var n in current)
                {
                    currentIds.Add(n.Id);
                    var item = TryParse(n);
                    if (item != null)
                    {
                        NotificationReceived?.Invoke(item);
                    }
                }

                lock (_knownIds)
                {
                    _knownIds.IntersectWith(currentIds);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }
}

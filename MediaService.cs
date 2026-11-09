using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace QuickNotchCs;

public sealed class MediaState
{
    public bool Active { get; set; }
    public bool Playing { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string App { get; set; } = "";
    public double Pos { get; set; }
    public double Dur { get; set; }
    public bool HasTimeline { get; set; }
    public bool CanSeek { get; set; }
    public DateTime Stamp { get; set; } = DateTime.UtcNow;
    public byte[]? Cover { get; set; }

    /// <summary>Позиция трека с учётом времени, прошедшего с последнего опроса.</summary>
    public double CurrentPos()
    {
        double pos = Pos;
        if (Playing) pos += (DateTime.UtcNow - Stamp).TotalSeconds;
        return Dur > 0 ? Math.Min(pos, Dur) : pos;
    }
}

/// <summary>
/// Системный плеер через GlobalSystemMediaTransportControls.
/// Потокобезопасная архитектура с защитой от сбоев CsWinRT Finalizer и нативного повреждения кучи (0xc0000374).
/// </summary>
public static class MediaService
{
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static GlobalSystemMediaTransportControlsSession? _subscribedSession;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    private static (string Key, byte[]? Data, int Tries) _cover = ("", null, 0);

    public static event Action? MediaChanged;

    private static long _lastChangedTicks;
    private static bool _scheduledChanged;

    public static async Task InitializeAsync()
    {
        try
        {
            await _gate.WaitAsync();
            try
            {
                _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_manager != null)
                {
                    _manager.CurrentSessionChanged += OnManagerSessionChanged;
                    _manager.SessionsChanged += OnManagerSessionsListChanged;
                    var cur = _manager.GetCurrentSession();
                    UpdateSessionSubscriptionLocked(cur);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch { }
    }

    private static void OnManagerSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        ScheduleUpdate();
    }

    private static void OnManagerSessionsListChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        ScheduleUpdate();
    }

    private static void ScheduleUpdate()
    {
        long now = Environment.TickCount64;
        if (_scheduledChanged && now - _lastChangedTicks < 200) return;
        _scheduledChanged = true;
        _lastChangedTicks = now;

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            try
            {
                await _gate.WaitAsync();
                try
                {
                    if (_manager != null)
                    {
                        var cur = _manager.GetCurrentSession();
                        UpdateSessionSubscriptionLocked(cur);
                    }
                }
                finally
                {
                    _scheduledChanged = false;
                    _gate.Release();
                }
                MediaChanged?.Invoke();
            }
            catch
            {
                _scheduledChanged = false;
            }
        });
    }

    /// <summary>
    /// Обновляет подписку на события плеера.
    /// ВАЖНО: вызывается ТОЛЬКО под блокировкой _gate!
    /// Защита от сбоев нативного WinRT/COM: никогда не дёргает отписку на мёртвых сессиях.
    /// </summary>
    private static void UpdateSessionSubscriptionLocked(GlobalSystemMediaTransportControlsSession? newSession)
    {
        // Если объект сессии физически тот же самый — ничего не меняем
        if (ReferenceEquals(_subscribedSession, newSession) && _subscribedSession != null)
        {
            return;
        }

        // Отписываемся от предыдущей сессии ТОЛЬКО если она ещё жива и отвечает
        if (_subscribedSession != null)
        {
            try
            {
                // Проверяем жива ли сессия: если GetPlaybackInfo() бросает ошибку, нативный C++ объект уже уничтожен
                _ = _subscribedSession.GetPlaybackInfo();
                _subscribedSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _subscribedSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            }
            catch
            {
                // Сессия уже закрыта ОС — НЕ вызываем -=, чтобы не повредить нативную кучу Windows.Media.MediaControl.dll!
            }
            finally
            {
                _subscribedSession = null;
            }
        }

        _subscribedSession = newSession;

        // Подписываемся на новую сессию (ТОЛЬКО на PlaybackInfo и MediaProperties, без спама Timeline)
        if (_subscribedSession != null)
        {
            try
            {
                _subscribedSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _subscribedSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            }
            catch
            {
                _subscribedSession = null;
            }
        }
    }

    private static void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => FireMediaChangedThrottled();
    private static void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => FireMediaChangedThrottled();

    private static void FireMediaChangedThrottled()
    {
        long now = Environment.TickCount64;
        if (now - _lastChangedTicks < 200) return;
        _lastChangedTicks = now;
        MediaChanged?.Invoke();
    }

    private static async Task<GlobalSystemMediaTransportControlsSession?> GetSessionAsync()
    {
        try
        {
            await _gate.WaitAsync();
            try
            {
                _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_manager == null) return null;

                // 1. Сначала проверяем GetCurrentSession() — это самый быстрый и стабильный путь!
                GlobalSystemMediaTransportControlsSession? cur = null;
                try { cur = _manager.GetCurrentSession(); } catch { }

                if (cur != null)
                {
                    bool isCurValid = false;
                    try
                    {
                        var info = cur.GetPlaybackInfo();
                        if (info != null)
                        {
                            isCurValid = true;
                            // Если текущая сессия играет — используем её напрямую без дорогого GetSessions()!
                            if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            {
                                UpdateSessionSubscriptionLocked(cur);
                                return cur;
                            }
                        }
                    }
                    catch { }

                    // Если cur валидна, но стоит на паузе — проверяем, нет ли другой реально играющей сессии
                    if (isCurValid)
                    {
                        try
                        {
                            var sessions = _manager.GetSessions();
                            if (sessions != null)
                            {
                                foreach (var s in sessions)
                                {
                                    try
                                    {
                                        var info = s.GetPlaybackInfo();
                                        if (info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                                        {
                                            UpdateSessionSubscriptionLocked(s);
                                            return s;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        UpdateSessionSubscriptionLocked(cur);
                        return cur;
                    }
                }

                // 2. Fallback: если GetCurrentSession() пуст, ищем любую валидную сессию
                try
                {
                    var sessions = _manager.GetSessions();
                    if (sessions != null && sessions.Count > 0)
                    {
                        GlobalSystemMediaTransportControlsSession? fallback = null;
                        foreach (var s in sessions)
                        {
                            try
                            {
                                var info = s.GetPlaybackInfo();
                                if (info != null)
                                {
                                    if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                                    {
                                        UpdateSessionSubscriptionLocked(s);
                                        return s;
                                    }
                                    fallback ??= s;
                                }
                            }
                            catch { }
                        }

                        if (fallback != null)
                        {
                            UpdateSessionSubscriptionLocked(fallback);
                            return fallback;
                        }
                    }
                }
                catch { }

                UpdateSessionSubscriptionLocked(null);
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            return null;
        }
    }

    public static async Task<MediaState> ReadAsync()
    {
        var state = new MediaState();
        var session = await GetSessionAsync();
        if (session == null) return state;

        try
        {
            var info = session.GetPlaybackInfo();
            if (info != null)
            {
                state.Active = true;
                state.Playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
        }
        catch { }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props != null)
            {
                state.Title = props.Title ?? "";
                state.Artist = props.Artist ?? "";
                state.Cover = await ReadCoverAsync(props, $"{state.Title}|{state.Artist}");
            }
        }
        catch { }

        try
        {
            state.App = AudioService.PrettyAppName(session.SourceAppUserModelId);
        }
        catch { }

        try
        {
            var tl = session.GetTimelineProperties();
            if (tl != null)
            {
                double start = tl.StartTime.TotalSeconds;
                state.Dur = Math.Max(0, tl.EndTime.TotalSeconds - start);
                state.Pos = Math.Max(0, tl.Position.TotalSeconds - start);
                if (state.Playing && tl.LastUpdatedTime.Year > 2000)
                {
                    double elapsed = (DateTimeOffset.UtcNow - tl.LastUpdatedTime).TotalSeconds;
                    if (elapsed >= 0 && elapsed < 36000) state.Pos += elapsed;
                }
                if (state.Dur > 0) state.Pos = Math.Min(state.Pos, state.Dur);
            }
        }
        catch { }

        state.HasTimeline = state.Dur > 0;
        if (state.Dur <= 0 && !string.IsNullOrEmpty(state.Title))
        {
            state.Dur = Math.Max(210.0, state.Pos + 30.0);
        }
        state.CanSeek = true;
        state.Stamp = DateTime.UtcNow;
        return state;
    }

    /// <summary>
    /// Читает обложку трека. Потоки потокобезопасно и гарантированно закрываются через using с таймаутом.
    /// </summary>
    private static async Task<byte[]?> ReadCoverAsync(GlobalSystemMediaTransportControlsSessionMediaProperties? props, string key)
    {
        if (props == null) return null;
        if (_cover.Key == key && (_cover.Data != null || _cover.Tries >= 3)) return _cover.Data;
        if (_cover.Key != key) _cover = (key, null, 0);
        _cover.Tries++;

        byte[]? data = null;
        try
        {
            if (props.Thumbnail != null)
            {
                var openTask = props.Thumbnail.OpenReadAsync().AsTask();
                if (await Task.WhenAny(openTask, Task.Delay(1200)) == openTask)
                {
                    using var stream = await openTask;
                    if (stream != null && stream.Size > 0 && stream.Size < 8_000_000)
                    {
                        using var inputStream = stream.GetInputStreamAt(0);
                        using var reader = new DataReader(inputStream);
                        var loadTask = reader.LoadAsync((uint)stream.Size).AsTask();
                        if (await Task.WhenAny(loadTask, Task.Delay(1200)) == loadTask)
                        {
                            await loadTask;
                            data = new byte[stream.Size];
                            reader.ReadBytes(data);
                        }
                    }
                }
            }
        }
        catch { data = null; }

        _cover = (key, data, _cover.Tries);
        return data;
    }

    public static async Task<bool> NextAsync()
    {
        bool handled = false;
        try
        {
            var session = await GetSessionAsync();
            if (session != null)
            {
                var task = session.TrySkipNextAsync().AsTask();
                if (await Task.WhenAny(task, Task.Delay(350)) == task)
                {
                    handled = await task;
                }
            }
        }
        catch { }
        if (!handled)
        {
            WinApi.MediaKey("next");
        }
        return true;
    }

    public static async Task<bool> PreviousAsync()
    {
        bool handled = false;
        try
        {
            var session = await GetSessionAsync();
            if (session != null)
            {
                var task = session.TrySkipPreviousAsync().AsTask();
                if (await Task.WhenAny(task, Task.Delay(350)) == task)
                {
                    handled = await task;
                }
            }
        }
        catch { }
        if (!handled)
        {
            WinApi.MediaKey("previous");
        }
        return true;
    }

    public static async Task<bool> TogglePlayPauseAsync()
    {
        try
        {
            var session = await GetSessionAsync();
            if (session != null)
            {
                var task = session.TryTogglePlayPauseAsync().AsTask();
                if (await Task.WhenAny(task, Task.Delay(350)) == task)
                {
                    bool ok = await task;
                    if (ok) return true;
                }
            }
        }
        catch { }
        WinApi.MediaKey("play_pause");
        return true;
    }

    public static async Task SeekAsync(double seconds)
    {
        try
        {
            var session = await GetSessionAsync();
            if (session != null)
            {
                double start = 0.0;
                try
                {
                    var tl = session.GetTimelineProperties();
                    if (tl != null) start = tl.StartTime.TotalSeconds;
                }
                catch { }

                long ticks = (long)(Math.Max(0, seconds + start) * 10_000_000);
                var task = session.TryChangePlaybackPositionAsync(ticks).AsTask();
                if (await Task.WhenAny(task, Task.Delay(350)) == task)
                {
                    await task;
                }
            }
        }
        catch { }
    }
}

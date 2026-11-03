namespace QuickNotchCs;

using System;
using System.Media;
using System.Windows.Threading;

public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak
}

public enum PomodoroState
{
    Idle,
    Running,
    Paused
}

public static class PomodoroService
{
    private static DispatcherTimer? _timer;
    private static IslandView? _island;

    public static PomodoroPhase CurrentPhase { get; private set; } = PomodoroPhase.Focus;
    public static PomodoroState CurrentState { get; private set; } = PomodoroState.Idle;

    public static int RemainingSeconds { get; private set; } = 25 * 60;
    public static int TotalSeconds { get; private set; } = 25 * 60;
    public static int CurrentRound { get; private set; } = 1;
    public static int CompletedRounds { get; private set; } = 0;

    public static event Action? OnTick;
    public static event Action? OnPhaseChanged;

    public static void Initialize(IslandView island)
    {
        _island = island;
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tick();
        }
        ResetToCurrentPhase();
    }

    public static string FormattedTime => $"{RemainingSeconds / 60:D2}:{RemainingSeconds % 60:D2}";

    public static double Progress => TotalSeconds > 0 ? 1.0 - ((double)RemainingSeconds / TotalSeconds) : 0.0;

    public static string PhaseName => CurrentPhase switch
    {
        PomodoroPhase.Focus => Loc.T("Фокус", "Focus", "Odak"),
        PomodoroPhase.ShortBreak => Loc.T("Короткий отдых", "Short Break", "Kısa Mola"),
        PomodoroPhase.LongBreak => Loc.T("Длинный отдых", "Long Break", "Uzun Mola"),
        _ => "Фокус"
    };

    public static string PhaseIcon => CurrentPhase switch
    {
        PomodoroPhase.Focus => "🎯",
        PomodoroPhase.ShortBreak => "☕",
        PomodoroPhase.LongBreak => "🌴",
        _ => "🎯"
    };

    public static void Start()
    {
        if (CurrentState == PomodoroState.Running) return;
        CurrentState = PomodoroState.Running;
        _timer?.Start();
        OnTick?.Invoke();
    }

    public static void Pause()
    {
        if (CurrentState != PomodoroState.Running) return;
        CurrentState = PomodoroState.Paused;
        _timer?.Stop();
        OnTick?.Invoke();
    }

    public static void Toggle()
    {
        if (CurrentState == PomodoroState.Running)
            Pause();
        else
            Start();
    }

    public static void Reset()
    {
        _timer?.Stop();
        CurrentState = PomodoroState.Idle;
        ResetToCurrentPhase();
        OnTick?.Invoke();
    }

    public static void Skip()
    {
        AdvancePhase();
        Reset();
    }

    public static void SetPhase(PomodoroPhase phase)
    {
        CurrentPhase = phase;
        Reset();
        OnPhaseChanged?.Invoke();
    }

    public static void SetFocusDuration(int minutes)
    {
        if (_island == null) return;
        minutes = Math.Clamp(minutes, 1, 120);
        _island.S.PomodoroFocus = minutes;
        _island.SaveState();
        if (CurrentPhase == PomodoroPhase.Focus && CurrentState == PomodoroState.Idle)
        {
            ResetToCurrentPhase();
        }
        OnTick?.Invoke();
    }

    public static void SetShortBreakDuration(int minutes)
    {
        if (_island == null) return;
        minutes = Math.Clamp(minutes, 1, 60);
        _island.S.PomodoroShort = minutes;
        _island.SaveState();
        if (CurrentPhase == PomodoroPhase.ShortBreak && CurrentState == PomodoroState.Idle)
        {
            ResetToCurrentPhase();
        }
        OnTick?.Invoke();
    }

    public static void SetLongBreakDuration(int minutes)
    {
        if (_island == null) return;
        minutes = Math.Clamp(minutes, 1, 60);
        _island.S.PomodoroLong = minutes;
        _island.SaveState();
        if (CurrentPhase == PomodoroPhase.LongBreak && CurrentState == PomodoroState.Idle)
        {
            ResetToCurrentPhase();
        }
        OnTick?.Invoke();
    }

    public static void SetRounds(int rounds)
    {
        if (_island == null) return;
        rounds = Math.Clamp(rounds, 1, 12);
        _island.S.PomodoroRounds = rounds;
        _island.SaveState();
        OnTick?.Invoke();
    }

    public static void UpdateDurations()
    {
        if (CurrentState == PomodoroState.Idle)
        {
            ResetToCurrentPhase();
            OnTick?.Invoke();
        }
    }

    private static void ResetToCurrentPhase()
    {
        int min = CurrentPhase switch
        {
            PomodoroPhase.Focus => _island?.S.PomodoroFocus ?? 25,
            PomodoroPhase.ShortBreak => _island?.S.PomodoroShort ?? 5,
            PomodoroPhase.LongBreak => _island?.S.PomodoroLong ?? 15,
            _ => 25
        };
        TotalSeconds = Math.Max(1, min * 60);
        RemainingSeconds = TotalSeconds;
    }

    private static void Tick()
    {
        if (RemainingSeconds > 0)
        {
            RemainingSeconds--;
            OnTick?.Invoke();
            if (RemainingSeconds == 0)
            {
                OnPhaseCompleted();
            }
        }
    }

    private static void OnPhaseCompleted()
    {
        _timer?.Stop();
        CurrentState = PomodoroState.Idle;

        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch { }

        string message;
        int maxRounds = _island?.S.PomodoroRounds ?? 4;
        if (CurrentPhase == PomodoroPhase.Focus)
        {
            CompletedRounds++;
            if (CurrentRound >= maxRounds)
            {
                CurrentPhase = PomodoroPhase.LongBreak;
                CurrentRound = 1;
                message = Loc.T(
                    $"Все {maxRounds} раунда завершены! Отдохните {(_island?.S.PomodoroLong ?? 15)} минут.",
                    $"All {maxRounds} rounds completed! Take a {(_island?.S.PomodoroLong ?? 15)} minute long break.",
                    $"Tüm {maxRounds} tur tamamlandı! {(_island?.S.PomodoroLong ?? 15)} dakika mola verin.");
            }
            else
            {
                CurrentPhase = PomodoroPhase.ShortBreak;
                CurrentRound++;
                message = Loc.T(
                    $"Раунд {CurrentRound - 1} завершён! Время отдыха ({(_island?.S.PomodoroShort ?? 5)} мин).",
                    $"Round {CurrentRound - 1} completed! Rest for {(_island?.S.PomodoroShort ?? 5)} min.",
                    $"Tur {CurrentRound - 1} tamamlandı! {(_island?.S.PomodoroShort ?? 5)} dk dinlenin.");
            }
        }
        else
        {
            CurrentPhase = PomodoroPhase.Focus;
            message = Loc.T(
                $"Отдых завершён! Готовы к раунду {CurrentRound}?",
                $"Break finished! Ready for round {CurrentRound}?",
                $"Mola bitti! {CurrentRound}. tur için hazır mısınız?");
        }

        if (_island != null)
        {
            new Toast(_island, "⏱ " + Loc.T("Помодоро", "Pomodoro", "Pomodoro"), message, Theme.Accent, 6000);
        }

        ResetToCurrentPhase();
        OnPhaseChanged?.Invoke();
        OnTick?.Invoke();
    }

    private static void AdvancePhase()
    {
        if (CurrentPhase == PomodoroPhase.Focus)
        {
            int maxRounds = _island?.S.PomodoroRounds ?? 4;
            if (CurrentRound >= maxRounds)
            {
                CurrentPhase = PomodoroPhase.LongBreak;
                CurrentRound = 1;
            }
            else
            {
                CurrentPhase = PomodoroPhase.ShortBreak;
                CurrentRound++;
            }
        }
        else
        {
            CurrentPhase = PomodoroPhase.Focus;
        }
        OnPhaseChanged?.Invoke();
    }
}


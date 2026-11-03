namespace QuickNotchCs;

using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

public partial class PomodoroView : UserControl
{
    private IslandView? _island;

    public PomodoroView()
    {
        InitializeComponent();
        PomodoroService.OnTick += OnServiceTick;
        PomodoroService.OnPhaseChanged += OnServicePhaseChanged;
    }

    public void Attach(IslandView island)
    {
        _island = island;
        PomodoroService.Initialize(island);
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (_island == null) return;

        try
        {
            // 1. Циферблат и прогресс
            if (TimerText != null) TimerText.Text = PomodoroService.FormattedTime;
            double progress = PomodoroService.Progress;
            if (ProgressBarFill != null) ProgressBarFill.Width = Math.Clamp(220.0 * progress, 0.0, 220.0);

            // 2. Кнопка Старт / Пауза
            bool isRunning = PomodoroService.CurrentState == PomodoroState.Running;
            if (PlayPauseIcon != null) PlayPauseIcon.Text = isRunning ? "\uE769" : "\uE768";
            if (PlayPauseText != null) PlayPauseText.Text = isRunning ? Loc.T("Пауза", "Pause", "Duraklat") : Loc.T("Старт", "Start", "Başlat");

            // 3. Фазы (вкладки слева)
            HighlightPhaseButton(FocusPhaseBtn, PomodoroService.CurrentPhase == PomodoroPhase.Focus);
            HighlightPhaseButton(ShortBreakPhaseBtn, PomodoroService.CurrentPhase == PomodoroPhase.ShortBreak);
            HighlightPhaseButton(LongBreakPhaseBtn, PomodoroService.CurrentPhase == PomodoroPhase.LongBreak);

            // 4. Индикатор раундов
            int maxRounds = _island.S.PomodoroRounds;
            int curRound = PomodoroService.CurrentRound;
            var sb = new StringBuilder();
            for (int i = 1; i <= maxRounds; i++)
            {
                sb.Append(i <= curRound ? "● " : "○ ");
            }
            if (RoundDotsText != null) RoundDotsText.Text = sb.ToString().TrimEnd();
            if (RoundInfoText != null) RoundInfoText.Text = $"{Loc.T("Раунд", "Round", "Tur")} {curRound} {Loc.T("из", "of", "/")} {maxRounds}";

            // 5. Настройки в правой колонке
            if (FocusValText != null) FocusValText.Text = $"{_island.S.PomodoroFocus} {Loc.T("мин", "min", "dk")}";
            if (ShortValText != null) ShortValText.Text = $"{_island.S.PomodoroShort} {Loc.T("мин", "min", "dk")}";
            if (LongValText != null) LongValText.Text = $"{_island.S.PomodoroLong} {Loc.T("мин", "min", "dk")}";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "PomodoroView.RefreshAll");
        }
    }

    private void HighlightPhaseButton(Button? btn, bool isSelected)
    {
        if (btn == null) return;
        btn.Background = isSelected ? Theme.AccentBrush : Brushes.Transparent;
        btn.Foreground = isSelected ? Brushes.White : Theme.MutedBrush;
    }

    private void OnServiceTick()
    {
        Dispatcher.InvokeAsync(RefreshAll);
    }

    private void OnServicePhaseChanged()
    {
        Dispatcher.InvokeAsync(RefreshAll);
    }

    // ==================== Управление таймером ====================
    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        PomodoroService.Toggle();
        RefreshAll();
        _island?.HomeHost?.RefreshAll();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        PomodoroService.Reset();
        RefreshAll();
        _island?.HomeHost?.RefreshAll();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        PomodoroService.Skip();
        RefreshAll();
        _island?.HomeHost?.RefreshAll();
    }

    private void OnFocusPhaseClick(object sender, RoutedEventArgs e)
    {
        PomodoroService.SetPhase(PomodoroPhase.Focus);
        RefreshAll();
    }

    private void OnShortBreakPhaseClick(object sender, RoutedEventArgs e)
    {
        PomodoroService.SetPhase(PomodoroPhase.ShortBreak);
        RefreshAll();
    }

    private void OnLongBreakPhaseClick(object sender, RoutedEventArgs e)
    {
        PomodoroService.SetPhase(PomodoroPhase.LongBreak);
        RefreshAll();
    }

    // ==================== Пресеты фокуса ====================
    private void OnFocusPreset15(object sender, RoutedEventArgs e) => SetFocus(15);
    private void OnFocusPreset20(object sender, RoutedEventArgs e) => SetFocus(20);
    private void OnFocusPreset25(object sender, RoutedEventArgs e) => SetFocus(25);
    private void OnFocusPreset30(object sender, RoutedEventArgs e) => SetFocus(30);
    private void OnFocusPreset45(object sender, RoutedEventArgs e) => SetFocus(45);
    private void OnFocusPreset60(object sender, RoutedEventArgs e) => SetFocus(60);
    private void OnFocusMinus1(object sender, RoutedEventArgs e) => SetFocus((_island?.S.PomodoroFocus ?? 25) - 1);
    private void OnFocusPlus1(object sender, RoutedEventArgs e) => SetFocus((_island?.S.PomodoroFocus ?? 25) + 1);

    private void SetFocus(int min)
    {
        PomodoroService.SetFocusDuration(min);
        RefreshAll();
        _island?.HomeHost?.RefreshAll();
    }

    // ==================== Пресеты короткого отдыха ====================
    private void OnShortPreset3(object sender, RoutedEventArgs e) => SetShort(3);
    private void OnShortPreset5(object sender, RoutedEventArgs e) => SetShort(5);
    private void OnShortPreset10(object sender, RoutedEventArgs e) => SetShort(10);
    private void OnShortPreset15(object sender, RoutedEventArgs e) => SetShort(15);
    private void OnShortMinus1(object sender, RoutedEventArgs e) => SetShort((_island?.S.PomodoroShort ?? 5) - 1);
    private void OnShortPlus1(object sender, RoutedEventArgs e) => SetShort((_island?.S.PomodoroShort ?? 5) + 1);

    private void SetShort(int min)
    {
        PomodoroService.SetShortBreakDuration(min);
        RefreshAll();
        _island?.HomeHost?.RefreshAll();
    }

    // ==================== Пресеты длинного отдыха ====================
    private void OnLongPreset10(object sender, RoutedEventArgs e) => SetLong(10);
    private void OnLongPreset15(object sender, RoutedEventArgs e) => SetLong(15);
    private void OnLongPreset20(object sender, RoutedEventArgs e) => SetLong(20);
    private void OnLongPreset30(object sender, RoutedEventArgs e) => SetLong(30);
    private void OnLongMinus1(object sender, RoutedEventArgs e) => SetLong((_island?.S.PomodoroLong ?? 15) - 1);
    private void OnLongPlus1(object sender, RoutedEventArgs e) => SetLong((_island?.S.PomodoroLong ?? 15) + 1);

    private void SetLong(int min)
    {
        PomodoroService.SetLongBreakDuration(min);
        RefreshAll();
        _island?.HomeHost?.RefreshAll();
    }

    // ==================== Пресеты раундов ====================
    private void OnRounds2(object sender, RoutedEventArgs e) => SetRounds(2);
    private void OnRounds3(object sender, RoutedEventArgs e) => SetRounds(3);
    private void OnRounds4(object sender, RoutedEventArgs e) => SetRounds(4);
    private void OnRounds5(object sender, RoutedEventArgs e) => SetRounds(5);

    private void SetRounds(int r)
    {
        PomodoroService.SetRounds(r);
        RefreshAll();
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
}

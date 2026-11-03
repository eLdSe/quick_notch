using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QuickNotchCs;

public partial class CalendarView : UserControl
{
    private IslandView? _island;
    private DateTime _viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDay = DateTime.Today;

    private static readonly SolidColorBrush WeekendBrush = new(Color.FromRgb(0xFF, 0x85, 0x66));
    private static readonly SolidColorBrush SuccessGreenBrush = new(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly SolidColorBrush TodayCellBgBrush = new(Color.FromArgb(34, 10, 132, 255));
    private static readonly SolidColorBrush SelectedCellBgBrush = new(Color.FromArgb(55, 10, 132, 255));
    private static readonly SolidColorBrush TaskDotBrush = new(Color.FromRgb(0xFF, 0x37, 0x5F));

    static CalendarView()
    {
        WeekendBrush.Freeze();
        SuccessGreenBrush.Freeze();
        TodayCellBgBrush.Freeze();
        SelectedCellBgBrush.Freeze();
        TaskDotBrush.Freeze();
    }

    public CalendarView()
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

    public void RefreshAll()
    {
        if (_island == null) return;

        // 1. Шапка месяца
        string monthTitle = _viewMonth.ToString("MMMM yyyy", Loc.Culture);
        monthTitle = char.ToUpper(monthTitle[0]) + monthTitle[1..];
        MonthTitleText.Text = monthTitle;

        // 2. Сетка дней месяца (6 недель x 7 дней = 42 ячейки)
        CalendarDaysGrid.Children.Clear();
        var firstOfMonth = new DateTime(_viewMonth.Year, _viewMonth.Month, 1);
        int dayOfWeekOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7; // Понедельник = 0
        var startDate = firstOfMonth.AddDays(-dayOfWeekOffset);

        var allNotes = Sections.Calendar.Notes;

        for (int i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            bool isCurrentMonth = date.Month == _viewMonth.Month;
            bool isSelected = date.Date == _selectedDay.Date;
            bool isToday = date.Date == DateTime.Today;
            bool isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
            string dateKey = date.ToString("yyyy-MM-dd");
            bool hasNotes = allNotes.Any(n => n.Date == dateKey);

            Brush cellBg;
            Brush cellBorder;
            double borderThickness = 0;

            if (isSelected)
            {
                cellBg = SelectedCellBgBrush;
                cellBorder = (Brush)FindResource("AccentBrush");
                borderThickness = 1.5;
            }
            else if (isToday)
            {
                cellBg = TodayCellBgBrush;
                cellBorder = (Brush)FindResource("AccentBrush");
                borderThickness = 1;
            }
            else if (isCurrentMonth)
            {
                cellBg = (Brush)FindResource("Surface2Brush");
                cellBorder = Brushes.Transparent;
            }
            else
            {
                cellBg = Brushes.Transparent;
                cellBorder = Brushes.Transparent;
            }

            var cell = new Border
            {
                Background = cellBg,
                BorderBrush = cellBorder,
                BorderThickness = new Thickness(borderThickness),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(1),
                Cursor = Cursors.Hand,
                Opacity = isCurrentMonth ? 1.0 : 0.35
            };

            var sp = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Brush numColor;
            if (isToday)
            {
                numColor = (Brush)FindResource("AccentBrush");
            }
            else if (isSelected)
            {
                numColor = (Brush)FindResource("TextPrimaryBrush");
            }
            else if (isWeekend && isCurrentMonth)
            {
                numColor = WeekendBrush;
            }
            else
            {
                numColor = (Brush)FindResource("TextPrimaryBrush");
            }

            var numBlock = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 12,
                FontWeight = (isSelected || isToday) ? FontWeights.Bold : FontWeights.Normal,
                Foreground = numColor,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sp.Children.Add(numBlock);

            // Точка-индикатор наличия задач
            if (hasNotes)
            {
                var dot = new Ellipse
                {
                    Width = 4.5,
                    Height = 4.5,
                    Fill = TaskDotBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                sp.Children.Add(dot);
            }

            cell.Child = sp;

            var targetDate = date;
            cell.MouseLeftButtonUp += (s, e) =>
            {
                _selectedDay = targetDate;
                if (targetDate.Month != _viewMonth.Month)
                {
                    _viewMonth = new DateTime(targetDate.Year, targetDate.Month, 1);
                }
                RefreshAll();
            };

            CalendarDaysGrid.Children.Add(cell);
        }

        // 3. Задачи на выбранный день
        string dayTitle = _selectedDay.ToString("d MMMM, dddd", Loc.Culture);
        dayTitle = char.ToUpper(dayTitle[0]) + dayTitle[1..];
        SelectedDayText.Text = dayTitle;

        string selDateKey = _selectedDay.ToString("yyyy-MM-dd");
        var dayNotes = allNotes.Where(n => n.Date == selDateKey).ToList();

        DayNotesPanel.Children.Clear();
        if (dayNotes.Count == 0)
        {
            NoNotesPanel.Visibility = Visibility.Visible;
        }
        else
        {
            NoNotesPanel.Visibility = Visibility.Collapsed;
            for (int i = 0; i < dayNotes.Count; i++)
            {
                var note = dayNotes[i];

                var card = new Border
                {
                    Background = (Brush)FindResource("Surface2Brush"),
                    BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Чекбокс отметки выполнения
                var checkBtn = new Button
                {
                    Background = note.Done ? SuccessGreenBrush : Brushes.Transparent,
                    BorderBrush = note.Done ? SuccessGreenBrush : (Brush)FindResource("TextSecondaryBrush"),
                    BorderThickness = new Thickness(1.5),
                    Width = 18,
                    Height = 18,
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBtn.Template = CreateRoundButtonTemplate();
                if (note.Done)
                {
                    checkBtn.Content = new TextBlock
                    {
                        Text = "✓",
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }

                checkBtn.Click += (s, e) =>
                {
                    Sections.Calendar.ToggleNote(note.Id);
                    RefreshAll();
                };
                Grid.SetColumn(checkBtn, 0);
                grid.Children.Add(checkBtn);

                // Текст задачи
                var text = new TextBlock
                {
                    Text = note.Text,
                    FontSize = 12,
                    Foreground = note.Done ? (Brush)FindResource("TextSecondaryBrush") : (Brush)FindResource("TextPrimaryBrush"),
                    TextDecorations = note.Done ? TextDecorations.Strikethrough : null,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);

                // Кнопка удаления ✕
                var delBtn = new Button
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4),
                    Cursor = Cursors.Hand,
                    Content = new TextBlock
                    {
                        Text = "✕",
                        FontSize = 10,
                        Foreground = (Brush)FindResource("AccentHotBrush"),
                        FontWeight = FontWeights.Bold
                    }
                };
                delBtn.Click += (s, e) =>
                {
                    Sections.Calendar.DeleteNote(note.Id);
                    RefreshAll();
                };
                Grid.SetColumn(delBtn, 2);
                grid.Children.Add(delBtn);

                card.Child = grid;
                DayNotesPanel.Children.Add(card);
            }
        }

        // 4. Статистика года
        int dayOfYear = _selectedDay.DayOfYear;
        int totalDays = DateTime.IsLeapYear(_selectedDay.Year) ? 366 : 365;
        int daysLeft = totalDays - dayOfYear;
        YearStatsText.Text = $"День {dayOfYear} из {totalDays}  ·  До Нового года: {daysLeft} дн.";
    }

    private static ControlTemplate CreateRoundButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.Name = "border";
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        factory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        factory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        factory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(presenter);

        template.VisualTree = factory;
        return template;
    }

    private void OnPrevMonthClick(object sender, RoutedEventArgs e)
    {
        _viewMonth = _viewMonth.AddMonths(-1);
        RefreshAll();
    }

    private void OnNextMonthClick(object sender, RoutedEventArgs e)
    {
        _viewMonth = _viewMonth.AddMonths(1);
        RefreshAll();
    }

    private void OnTodayClick(object sender, RoutedEventArgs e)
    {
        _selectedDay = DateTime.Today;
        _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        RefreshAll();
    }

    private void OnAddNoteClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        string dateStr = _selectedDay.ToString("d MMMM", Loc.Culture);
        string? text = _island.Ask("Новая задача", $"Напоминание на {dateStr}:");
        if (!string.IsNullOrWhiteSpace(text))
        {
            Sections.Calendar.AddNote(_selectedDay.ToString("yyyy-MM-dd"), text.Trim());
            RefreshAll();
        }
    }

    }

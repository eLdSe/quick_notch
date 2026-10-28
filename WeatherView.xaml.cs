using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace QuickNotchCs;

public partial class WeatherView : UserControl
{
    private IslandView? _island;

    public WeatherView()
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

    public static string GetWeatherEmoji(int code) => code switch
    {
        0 => "☀️",
        1 or 2 => "🌤️",
        3 => "☁️",
        45 or 48 => "🌫️",
        51 or 53 or 55 or 56 or 57 => "🌧️",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "🌧️",
        71 or 73 or 75 or 77 or 85 or 86 => "❄️",
        95 or 96 or 99 => "⛈️",
        _ => "🌤️"
    };

    public void RefreshAll()
    {
        if (_island == null) return;

        var data = Sections.Weather.Data;
        if (data == null)
        {
            Sections.Weather.Ensure(_island);
            string? city = WeatherSection.SelectedCity(_island) ?? "Город не выбран";
            CityNameText.Text = city;
            WeatherDescText.Text = "Загрузка прогноза погоды…";
            return;
        }

        // 1. Город и текущая температура
        CityNameText.Text = data.City;
        string sign = data.Temp > 0 ? "+" : "";
        CurrentTempText.Text = $"{sign}{Math.Round(data.Temp)}°";
        WeatherIconText.Text = GetWeatherEmoji(data.Code);
        WeatherDescText.Text = data.Description;

        string appSign = data.Apparent > 0 ? "+" : "";
        string minMax = "";
        if (data.Daily.Count > 0)
        {
            var today = data.Daily[0];
            string minS = today.TempMin > 0 ? "+" : "";
            string maxS = today.TempMax > 0 ? "+" : "";
            minMax = $"  ·  Мин {minS}{Math.Round(today.TempMin)}°  Макс {maxS}{Math.Round(today.TempMax)}°";
        }
        FeelsLikeText.Text = $"Ощущается как {appSign}{Math.Round(data.Apparent)}°{minMax}";

        // 2. Метрики
        WindSpeedText.Text = $"{data.WindSpeed / 3.6:F1} м/с";
        HumidityText.Text = $"{data.Humidity}%";
        PressureText.Text = $"{Math.Round(data.Pressure * 0.750062)} мм";
        PrecipitationText.Text = $"{data.Pop}%";

        // 3. Восход и закат
        SunriseText.Text = string.IsNullOrEmpty(data.Sunrise) ? "🌅 Восход: —" : $"🌅 Восход: {data.Sunrise}";
        SunsetText.Text = string.IsNullOrEmpty(data.Sunset) ? "🌇 Закат: —" : $"🌇 Закат: {data.Sunset}";

        // 4. Почасовой прогноз
        HourlyForecastPanel.Children.Clear();
        int hourlyCount = Math.Min(12, data.Hourly.Count);
        for (int i = 0; i < hourlyCount; i++)
        {
            var h = data.Hourly[i];
            var col = new StackPanel
            {
                Width = 46,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var timeText = new TextBlock
            {
                Text = h.Time,
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var iconText = new TextBlock
            {
                Text = GetWeatherEmoji(h.Code),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };
            string hSign = h.Temp > 0 ? "+" : "";
            var tempText = new TextBlock
            {
                Text = $"{hSign}{Math.Round(h.Temp)}°",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            col.Children.Add(timeText);
            col.Children.Add(iconText);
            col.Children.Add(tempText);
            HourlyForecastPanel.Children.Add(col);
        }

        // 5. Прогноз на 7 дней
        DailyForecastPanel.Children.Clear();
        for (int i = 0; i < data.Daily.Count; i++)
        {
            var d = data.Daily[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // День недели
            var dayText = new TextBlock
            {
                Text = d.DayName,
                FontSize = 12,
                FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal,
                Foreground = i == 0 ? (Brush)FindResource("TextPrimaryBrush") : (Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dayText, 0);
            row.Children.Add(dayText);

            // Иконка
            var icon = new TextBlock
            {
                Text = GetWeatherEmoji(d.Code),
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 1);
            row.Children.Add(icon);

            // Вероятность осадков
            var pop = new TextBlock
            {
                Text = d.Pop > 10 ? $"{d.Pop}%" : "",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("CyanBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(pop, 2);
            row.Children.Add(pop);

            // Диапазон температур
            string minS = d.TempMin > 0 ? "+" : "";
            string maxS = d.TempMax > 0 ? "+" : "";
            var range = new TextBlock
            {
                Text = $"{minS}{Math.Round(d.TempMin)}° … {maxS}{Math.Round(d.TempMax)}°",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(range, 3);
            row.Children.Add(range);

            DailyForecastPanel.Children.Add(row);
        }
    }

    private void OnChangeCityClick(object sender, RoutedEventArgs e)
    {
        if (_island == null) return;
        string current = _island.S.WeatherCity ?? "Москва";
        string? entered = _island.Ask("Выбор города", "Введите название города (например, Москва, Сочи, Стамбул):", current);
        if (!string.IsNullOrWhiteSpace(entered))
        {
            _island.S.WeatherCity = entered.Trim();
            _island.SaveState();
            Sections.Weather.Invalidate(_island);
            RefreshAll();
        }
    }
}

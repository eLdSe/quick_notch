using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace QuickNotchCs;

public partial class NotchView : UserControl
{
	private IslandView? _island;
	private readonly DispatcherTimer _eqTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(50.0)
	};
	private int _eqPhase;

	private static readonly SolidColorBrush NotchHoverBrush;
	private static readonly SolidColorBrush NotchNormalBrush;
	private static readonly SolidColorBrush WhiteBrush;
	private static readonly SolidColorBrush GreenBrushStatic;
	private static readonly SolidColorBrush LowBatteryBrush;

	static NotchView()
	{
		NotchHoverBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141416"));
		NotchNormalBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000"));
		WhiteBrush = new SolidColorBrush(Colors.White);
		GreenBrushStatic = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30D158"));
		LowBatteryBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF453A"));
		NotchHoverBrush.Freeze();
		NotchNormalBrush.Freeze();
		WhiteBrush.Freeze();
		GreenBrushStatic.Freeze();
		LowBatteryBrush.Freeze();
	}

	public NotchView()
	{
		InitializeComponent();
		_eqTimer.Tick += OnEqTick;
	}

	public void Attach(IslandView island)
	{
		_island = island;
		RefreshAll();
	}

	public void RefreshAll()
	{
		if (_island == null)
		{
			return;
		}
		string islandStyle = _island.S.IslandStyle;
		bool isMediaActive = _island.IsMediaActive;
		double width = (isMediaActive ? 365.0 : 215.0);
		double height = (isMediaActive ? 66.0 : 38.0);
		double radius = (isMediaActive ? 22.0 : 18.0);
		if (islandStyle == "notch")
		{
			if (LeftEar != null)
			{
				LeftEar.Visibility = Visibility.Visible;
			}
			if (RightEar != null)
			{
				RightEar.Visibility = Visibility.Visible;
			}
			if (NotchCapsule != null)
			{
				NotchCapsule.CornerRadius = new CornerRadius(0.0, 0.0, radius, radius);
				NotchCapsule.BorderThickness = new Thickness(1.0, 0.0, 1.0, 1.0);
				NotchCapsule.Height = height;
				NotchCapsule.Width = width;
			}
			Margin = new Thickness(0.0, 0.0, 0.0, 0.0);
		}
		else if (islandStyle == "compact")
		{
			if (LeftEar != null)
			{
				LeftEar.Visibility = Visibility.Collapsed;
			}
			if (RightEar != null)
			{
				RightEar.Visibility = Visibility.Collapsed;
			}
			if (NotchCapsule != null)
			{
				NotchCapsule.CornerRadius = new CornerRadius(radius);
				NotchCapsule.BorderThickness = new Thickness(1.0);
				NotchCapsule.Height = height;
				NotchCapsule.Width = width;
			}
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		}
		else
		{
			if (LeftEar != null)
			{
				LeftEar.Visibility = Visibility.Collapsed;
			}
			if (RightEar != null)
			{
				RightEar.Visibility = Visibility.Collapsed;
			}
			if (NotchCapsule != null)
			{
				NotchCapsule.CornerRadius = new CornerRadius(radius);
				NotchCapsule.BorderThickness = new Thickness(1.0);
				NotchCapsule.Height = height;
				NotchCapsule.Width = width;
			}
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		}

		Brush mediaAccent = (Theme.CurrentAccent == "violet") 
			? new SolidColorBrush(Color.FromRgb(255, 43, 60)) 
			: Theme.AccentBrush;
		if (MiniPlayPauseBtn != null) MiniPlayPauseBtn.Background = mediaAccent;
		if (EqBar1 != null) EqBar1.Background = mediaAccent;
		if (EqBar2 != null) EqBar2.Background = mediaAccent;
		if (EqBar3 != null) EqBar3.Background = mediaAccent;
		if (EqBar4 != null) EqBar4.Background = mediaAccent;
		if (EqBar5 != null) EqBar5.Background = mediaAccent;
		if (EqBar6 != null) EqBar6.Background = mediaAccent;
		if (EqBar7 != null) EqBar7.Background = mediaAccent;

		if (NotchShadow != null)
		{
			NotchShadow.Color = (Theme.CurrentAccent == "red" || Theme.CurrentAccent == "pink" || Theme.CurrentAccent == "coral") 
				? Theme.Accent 
				: Color.FromRgb(255, 43, 60);
		}
		if (NotchTimeText != null)
		{
			NotchTimeText.Text = DateTime.Now.ToString("HH:mm");
		}
		var (num, flag) = _island.Battery;
		if (NotchBatteryText != null && NotchBatteryFill != null)
		{
			if (num.HasValue)
			{
				int num2 = Math.Clamp(num.Value, 0, 100);
				NotchBatteryText.Text = $"{num2}%";
				NotchBatteryFill.Width = Math.Max(2.0, 16.5 * (double)num2 / 100.0);
				if (flag)
				{
					NotchBatteryFill.Background = GreenBrushStatic;
					if (NotchChargingBolt != null)
					{
						NotchChargingBolt.Visibility = Visibility.Visible;
					}
				}
				else if (num2 <= 20)
				{
					NotchBatteryFill.Background = LowBatteryBrush;
					if (NotchChargingBolt != null)
					{
						NotchChargingBolt.Visibility = Visibility.Collapsed;
					}
				}
				else
				{
					NotchBatteryFill.Background = WhiteBrush;
					if (NotchChargingBolt != null)
					{
						NotchChargingBolt.Visibility = Visibility.Collapsed;
					}
				}
			}
			else
			{
				NotchBatteryText.Text = "—";
				NotchBatteryFill.Width = 0.0;
				if (NotchChargingBolt != null)
				{
					NotchChargingBolt.Visibility = Visibility.Collapsed;
				}
			}
		}
		WeatherSection weather = Sections.Weather;
		if (weather.Data != null && NotchTempText != null)
		{
			string value = ((weather.Data.Temp > 0.0) ? "+" : "");
			NotchTempText.Text = $"{value}{Math.Round(weather.Data.Temp)}°";
			if (NotchWeatherIcon != null)
			{
				string text = (weather.Data.Description ?? "").ToLowerInvariant();
				if (text.Contains("ясно") || text.Contains("солн"))
				{
					NotchWeatherIcon.Text = "☼";
					NotchWeatherIcon.Foreground = Brushes.Gold;
				}
				else if (text.Contains("дожд") || text.Contains("лив"))
				{
					NotchWeatherIcon.Text = "🌧";
					NotchWeatherIcon.Foreground = Brushes.LightSkyBlue;
				}
				else if (text.Contains("снег"))
				{
					NotchWeatherIcon.Text = "❄";
					NotchWeatherIcon.Foreground = Brushes.White;
				}
				else if (text.Contains("гроз"))
				{
					NotchWeatherIcon.Text = "⛈";
					NotchWeatherIcon.Foreground = Brushes.LightYellow;
				}
				else
				{
					NotchWeatherIcon.Text = "☁";
					NotchWeatherIcon.Foreground = (Brush)FindResource("WeatherCloudBrush");
				}
			}
			if (NotchWeatherPanel != null)
			{
				NotchWeatherPanel.Visibility = Visibility.Visible;
			}
		}
		else if (NotchWeatherPanel != null)
		{
			NotchWeatherPanel.Visibility = Visibility.Collapsed;
		}
		MediaState media = _island.Media;
		if (_island.IsMediaActive && MediaModeGrid != null && IdleModeGrid != null)
		{
			MediaModeGrid.Visibility = Visibility.Visible;
			IdleModeGrid.Visibility = Visibility.Collapsed;
			MiniTrackTitle.Text = media.Title;
			MiniTrackArtist.Text = (string.IsNullOrEmpty(media.Artist) ? "—" : media.Artist);
			if (MiniPauseIcon != null && MiniPlayIcon != null)
			{
				MiniPauseIcon.Visibility = ((!media.Playing) ? Visibility.Collapsed : Visibility.Visible);
				MiniPlayIcon.Visibility = (media.Playing ? Visibility.Collapsed : Visibility.Visible);
			}
			if (_island.CoverBrush is ImageBrush imageBrush)
			{
				MiniCoverImage.Source = imageBrush.ImageSource;
			}
			else
			{
				MiniCoverImage.Source = null;
			}
			if (media.Playing && !_eqTimer.IsEnabled)
			{
				_eqTimer.Start();
			}
			else if (!media.Playing && _eqTimer.IsEnabled)
			{
				_eqTimer.Stop();
				ResetEqBars();
			}
		}
		else if (MediaModeGrid != null && IdleModeGrid != null)
		{
			MediaModeGrid.Visibility = Visibility.Collapsed;
			IdleModeGrid.Visibility = Visibility.Visible;
			_eqTimer.Stop();
			ResetEqBars();
		}
		UpdateMorph(Ui.Clamp(_island.SpHide.X), _island.Hv("summon:notch_bar"));
	}

	private void OnEqTick(object? sender, EventArgs e)
	{
		if (EqBar1 != null && EqBar2 != null && EqBar3 != null && EqBar4 != null &&
			EqBar5 != null && EqBar6 != null && EqBar7 != null)
		{
			_eqPhase++;
			double num = (double)_eqPhase * 0.35;
			EqBar1.Height = 3.0 + 5.0 * (0.5 + 0.5 * Math.Sin(num * 1.8));
			EqBar2.Height = 4.0 + 11.0 * (0.5 + 0.5 * Math.Sin(num * 2.4 + 1.2));
			EqBar3.Height = 3.0 + 8.0 * (0.5 + 0.5 * Math.Cos(num * 1.6 + 0.7));
			EqBar4.Height = 5.0 + 13.0 * (0.5 + 0.5 * Math.Sin(num * 2.1 + 2.1));
			EqBar5.Height = 4.0 + 10.0 * (0.5 + 0.5 * Math.Cos(num * 2.7 + 0.5));
			EqBar6.Height = 4.0 + 12.0 * (0.5 + 0.5 * Math.Sin(num * 1.9 + 1.8));
			EqBar7.Height = 2.5 + 6.0 * (0.5 + 0.5 * Math.Cos(num * 2.3 + 3.0));
		}
	}

	private void ResetEqBars()
	{
		if (EqBar1 != null && EqBar2 != null && EqBar3 != null && EqBar4 != null &&
			EqBar5 != null && EqBar6 != null && EqBar7 != null)
		{
			EqBar1.Height = 3.0;
			EqBar2.Height = 7.0;
			EqBar3.Height = 5.0;
			EqBar4.Height = 9.0;
			EqBar5.Height = 6.0;
			EqBar6.Height = 8.0;
			EqBar7.Height = 4.0;
		}
	}

	public void UpdateMorph(double hide, double notchHv)
	{
		string text = _island?.S.IslandStyle ?? "notch";
		bool flag = text == "notch";
		bool flag2 = _island?.IsMediaActive ?? false;
		double a = (flag2 ? 365.0 : 215.0);
		double a2 = (flag2 ? 66.0 : (flag ? 38.0 : ((text == "compact") ? 30.0 : 34.0)));
		double a3 = (flag2 ? 22.0 : (flag ? 18.0 : ((text == "compact") ? 15.0 : 17.0)));
		double b = 120.0 + 4.0 * notchHv;
		double b2 = 4.0 + 0.8 * notchHv;
		double b3 = 2.0;
		double num = Ui.Lerp(a, b, hide);
		double height = Ui.Lerp(a2, b2, hide);
		double num2 = Ui.Lerp(a3, b3, hide);
		if (NotchCapsule != null)
		{
			NotchCapsule.Width = num;
			NotchCapsule.Height = height;
			if (flag)
			{
				NotchCapsule.CornerRadius = new CornerRadius(0.0, 0.0, num2, num2);
				NotchCapsule.BorderThickness = new Thickness(1.0, 0.0, 1.0, 1.0);
			}
			else
			{
				NotchCapsule.CornerRadius = new CornerRadius(num2);
				NotchCapsule.BorderThickness = new Thickness(1.0);
			}
			double num3 = Ui.Lerp(12.0, 4.0, hide);
			NotchCapsule.Padding = new Thickness(num3, 0.0, num3, 0.0);
		}
		double num4 = (flag ? Ui.Clamp(1.0 - hide * 2.8) : 0.0);
		if (LeftEar != null)
		{
			LeftEar.Opacity = num4;
			LeftEar.Visibility = ((!(num4 > 0.01)) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (RightEar != null)
		{
			RightEar.Opacity = num4;
			RightEar.Visibility = ((!(num4 > 0.01)) ? Visibility.Collapsed : Visibility.Visible);
		}
		double opacity = Ui.Clamp(1.0 - hide * 2.2);
		if (IdleModeGrid != null)
		{
			IdleModeGrid.Opacity = opacity;
			IdleModeGrid.IsHitTestVisible = hide < 0.25;
		}
		if (MediaModeGrid != null)
		{
			MediaModeGrid.Opacity = opacity;
			MediaModeGrid.IsHitTestVisible = hide < 0.25;
		}
		if (NotchBarIndicator != null)
		{
			if (hide > 0.2)
			{
				double opacity2 = Ui.Clamp((hide - 0.2) / 0.7) * (0.45 + 0.3 * notchHv);
				NotchBarIndicator.Opacity = opacity2;
				NotchBarIndicator.Width = Math.Min(num - 24.0, 38.0 + 6.0 * notchHv);
				NotchBarIndicator.Visibility = Visibility.Visible;
				NotchBarIndicator.Background = Brushes.White;
			}
			else
			{
				NotchBarIndicator.Opacity = 0.0;
				NotchBarIndicator.Visibility = Visibility.Collapsed;
			}
		}
		if (NotchShadow != null)
		{
			NotchShadow.Opacity = Ui.Lerp(0.45, 0.12, hide);
		}
	}

	private void OnNotchClick(object sender, MouseButtonEventArgs e)
	{
		if (_island != null)
		{
			if (_island.IsCoveredHidden || _island.SpHide.X > 0.3)
			{
				_island.SummonFromEdge();
			}
			else
			{
				_island.Expand();
			}
		}
	}

	private void OnMediaNotchClick(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		_island?.Select("media");
		_island?.Expand();
	}

	private void OnMediaPrevClick(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		_island?.PrevTrack();
	}

	private void OnMediaPlayPauseClick(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		_island?.TogglePlay();
	}

	private void OnMediaNextClick(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		_island?.NextTrack();
	}

	private void OnWeatherNotchClick(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		_island?.Select("weather");
		_island?.Expand();
	}

	private void OnTimeNotchClick(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		_island?.Select("calendar");
		_island?.Expand();
	}

	private void OnNotchMouseEnter(object sender, MouseEventArgs e)
	{
		_island?.SetHover("summon:notch_bar");
		if (_island == null || _island.SpHide.X < 0.2)
		{
			NotchCapsule.Background = NotchHoverBrush;
			if (LeftEar != null)
			{
				LeftEar.Fill = NotchHoverBrush;
			}
			if (RightEar != null)
			{
				RightEar.Fill = NotchHoverBrush;
			}
		}
		if (_island != null && (_island.S.SummonMode == "hover" || _island.S.SummonMode == "both") && _island.IsCoveredHidden)
		{
			_island.SummonFromEdge();
		}
	}

	private void OnNotchMouseLeave(object sender, MouseEventArgs e)
	{
		_island?.SetHover(null);
		NotchCapsule.Background = NotchNormalBrush;
		if (LeftEar != null)
		{
			LeftEar.Fill = NotchNormalBrush;
		}
		if (RightEar != null)
		{
			RightEar.Fill = NotchNormalBrush;
		}
	}
}

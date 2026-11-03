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
	private double _eqDecay = 1.0;
	private bool _eqDecaying = false;
	private bool? _lastMiniPlaying;

	private static readonly SolidColorBrush NotchHoverBrush;
	private static readonly SolidColorBrush NotchHoverBorderBrush;
	private static readonly SolidColorBrush NotchNormalBorderBrush;
	private static readonly SolidColorBrush NotchNormalBrush;
	private static readonly SolidColorBrush WhiteBrush;
	private static readonly SolidColorBrush GreenBrushStatic;
	private static readonly SolidColorBrush LowBatteryBrush;

	static NotchView()
	{
		NotchHoverBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141416"));
		NotchNormalBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000"));
		NotchNormalBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1E"));
		NotchHoverBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E2E32"));
		NotchNormalBorderBrush.Freeze();
		NotchHoverBorderBrush.Freeze();
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
		PomodoroService.OnTick += () => Dispatcher.InvokeAsync(RefreshAll);
		PomodoroService.OnPhaseChanged += () => Dispatcher.InvokeAsync(RefreshAll);
		RefreshAll();
	}

	public void RefreshAll()
	{
		if (_island == null)
		{
			return;
		}
		string islandStyle = _island.S.IslandStyle;
		if (islandStyle == "notch")
		{
			if (LeftEar != null) LeftEar.Visibility = Visibility.Visible;
			if (RightEar != null) RightEar.Visibility = Visibility.Visible;
			Margin = new Thickness(0.0, 0.0, 0.0, 0.0);
		}
		else
		{
			if (LeftEar != null) LeftEar.Visibility = Visibility.Collapsed;
			if (RightEar != null) RightEar.Visibility = Visibility.Collapsed;
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		}

		if (MiniPlayPauseBtn != null) MiniPlayPauseBtn.Background = Theme.AccentBrush;
		if (EqBar1 != null) EqBar1.Background = Theme.AccentBrush;
		if (EqBar2 != null) EqBar2.Background = Theme.AccentBrush;
		if (EqBar3 != null) EqBar3.Background = Theme.AccentBrush;
		if (EqBar4 != null) EqBar4.Background = Theme.AccentBrush;
		if (EqBar5 != null) EqBar5.Background = Theme.AccentBrush;
		if (EqBar6 != null) EqBar6.Background = Theme.AccentBrush;
		if (EqBar7 != null) EqBar7.Background = Theme.AccentBrush;

		if (NotchShadow != null)
		{
			NotchShadow.Color = Colors.Black;
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

		bool pomoActive = PomodoroService.CurrentState != PomodoroState.Idle;
		if (pomoActive)
		{
			if (NotchWeatherPanel != null) NotchWeatherPanel.Visibility = Visibility.Collapsed;
			if (NotchPomoPanel != null)
			{
				NotchPomoPanel.Visibility = Visibility.Visible;
				if (NotchPomoIcon != null) NotchPomoIcon.Text = PomodoroService.PhaseIcon;
				if (NotchPomoTimer != null)
				{
					NotchPomoTimer.Text = PomodoroService.FormattedTime;
					NotchPomoTimer.Foreground = PomodoroService.CurrentState == PomodoroState.Running 
						? Theme.AccentBrush 
						: Theme.MutedBrush;
				}
			}
		}
		else if (NotchPomoPanel != null)
		{
			NotchPomoPanel.Visibility = Visibility.Collapsed;
		}
		MediaState media = _island.Media;
		if (MiniTrackTitle != null) MiniTrackTitle.Text = media.Title;
		if (MiniTrackArtist != null) MiniTrackArtist.Text = (string.IsNullOrEmpty(media.Artist) ? "—" : media.Artist);

		AnimateMiniPlayPause(media.Playing);

		if (_island.CoverBrush is ImageBrush imageBrush)
		{
			if (MiniCoverImage != null) MiniCoverImage.Source = imageBrush.ImageSource;
		}
		else
		{
			if (MiniCoverImage != null) MiniCoverImage.Source = null;
		}

		if (media.Playing)
		{
			_eqDecaying = false;
			_eqDecay = 1.0;
			if (!_eqTimer.IsEnabled) _eqTimer.Start();
		}
		else if (_eqTimer.IsEnabled && !_eqDecaying)
		{
			// Плавное мягкое затухание столбиков эквалайзера в ноль при паузе вместо резкого скачка
			_eqDecaying = true;
		}

		UpdateMorph(Ui.Clamp(_island.SpHide.X), _island.Hv("summon:notch_bar"));
	}

	private void AnimateMiniPlayPause(bool playing)
	{
		if (MiniPauseIcon == null || MiniPlayIcon == null) return;
		if (_lastMiniPlaying == playing) return;

		bool isFirst = _lastMiniPlaying == null;
		_lastMiniPlaying = playing;

		if (isFirst)
		{
			MiniPauseIcon.Opacity = playing ? 1.0 : 0.0;
			MiniPauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
			MiniPlayIcon.Opacity = playing ? 0.0 : 1.0;
			MiniPlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
			if (MiniPauseScale != null) { MiniPauseScale.ScaleX = 1.0; MiniPauseScale.ScaleY = 1.0; }
			if (MiniPlayScale != null) { MiniPlayScale.ScaleX = 1.0; MiniPlayScale.ScaleY = 1.0; }
			return;
		}

		var duration = TimeSpan.FromMilliseconds(160);
		var ease = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

		var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };
		var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };
		var scaleUp = new System.Windows.Media.Animation.DoubleAnimation(0.70, 1.0, duration) { EasingFunction = ease };
		var scaleDown = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.70, duration) { EasingFunction = ease };

		if (playing)
		{
			MiniPauseIcon.Visibility = Visibility.Visible;
			MiniPlayIcon.Visibility = Visibility.Visible;

			MiniPauseIcon.BeginAnimation(UIElement.OpacityProperty, fadeIn);
			MiniPauseScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
			MiniPauseScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);

			fadeOut.Completed += (_, _) =>
			{
				if (_lastMiniPlaying == true) MiniPlayIcon.Visibility = Visibility.Collapsed;
			};
			MiniPlayIcon.BeginAnimation(UIElement.OpacityProperty, fadeOut);
			MiniPlayScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
			MiniPlayScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
		}
		else
		{
			MiniPauseIcon.Visibility = Visibility.Visible;
			MiniPlayIcon.Visibility = Visibility.Visible;

			MiniPlayIcon.BeginAnimation(UIElement.OpacityProperty, fadeIn);
			MiniPlayScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
			MiniPlayScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);

			fadeOut.Completed += (_, _) =>
			{
				if (_lastMiniPlaying == false) MiniPauseIcon.Visibility = Visibility.Collapsed;
			};
			MiniPauseIcon.BeginAnimation(UIElement.OpacityProperty, fadeOut);
			MiniPauseScale?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
			MiniPauseScale?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);
		}
	}

	private void OnEqTick(object? sender, EventArgs e)
	{
		if (EqBar1 == null || EqBar2 == null || EqBar3 == null || EqBar4 == null ||
			EqBar5 == null || EqBar6 == null || EqBar7 == null)
		{
			return;
		}

		if (_eqDecaying)
		{
			_eqDecay = Math.Max(0.0, _eqDecay - 0.15);
			if (_eqDecay <= 0.0)
			{
				_eqTimer.Stop();
				_eqDecaying = false;
				ResetEqBars();
				return;
			}
		}

		_eqPhase++;
		double num = (double)_eqPhase * 0.35;
		double d = _eqDecay;
		EqBar1.Height = 3.0 + 5.0 * (0.5 + 0.5 * Math.Sin(num * 1.8)) * d;
		EqBar2.Height = 4.0 + 11.0 * (0.5 + 0.5 * Math.Sin(num * 2.4 + 1.2)) * d;
		EqBar3.Height = 3.0 + 8.0 * (0.5 + 0.5 * Math.Cos(num * 1.6 + 0.7)) * d;
		EqBar4.Height = 5.0 + 13.0 * (0.5 + 0.5 * Math.Sin(num * 2.1 + 2.1)) * d;
		EqBar5.Height = 4.0 + 10.0 * (0.5 + 0.5 * Math.Cos(num * 2.7 + 0.5)) * d;
		EqBar6.Height = 4.0 + 12.0 * (0.5 + 0.5 * Math.Sin(num * 1.9 + 1.8)) * d;
		EqBar7.Height = 2.5 + 6.0 * (0.5 + 0.5 * Math.Cos(num * 2.3 + 3.0)) * d;
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
		double live = Ui.Clamp(_island?.SpLive.X ?? 0.0);

		// Плавный органичный морфинг между режимом ожидания и музыкальным режимом
		double idleW = 215.0;
		double mediaW = 365.0;
		double idleH = (flag ? 38.0 : ((text == "compact") ? 30.0 : 34.0));
		double mediaH = 66.0;
		double idleR = (flag ? 18.0 : ((text == "compact") ? 15.0 : 17.0));
		double mediaR = 22.0;

		double a = Ui.Lerp(idleW, mediaW, live);
		double a2 = Ui.Lerp(idleH, mediaH, live);
		double a3 = Ui.Lerp(idleR, mediaR, live);

		bool isFullscreen = _island?.IsFullscreenActive ?? false;

		// Состояние 3 (над открытыми окнами / при просмотре видео):
		// В покое: супер-компактная тонкая полоска 76 × 6 px (не мешает просмотру видео)
		// При наведении: плавно раскрывается в удобную тактильную капсулу 120 × 16 px для клика
		double restW = 80.0;
		double restH = 5.0;
		double restR = 2.5;

		double hoverW = 90.0;
		double hoverH = 7.0;
		double hoverR = 3.5;

		double b = Ui.Lerp(restW, hoverW, notchHv);
		double b2 = Ui.Lerp(restH, hoverH, notchHv);
		double b3 = Ui.Lerp(restR, hoverR, notchHv);

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
			double num3 = Ui.Lerp(Ui.Lerp(12.0, 14.0, live), 4.0, hide);
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

		double baseOpacity = Ui.Clamp(1.0 - hide * 2.2);
		bool hitTest = hide < 0.25;

		if (live <= 0.001)
		{
			if (MediaModeGrid != null)
			{
				MediaModeGrid.Visibility = Visibility.Collapsed;
				MediaModeGrid.Opacity = 0.0;
			}
			if (IdleModeGrid != null)
			{
				IdleModeGrid.Visibility = Visibility.Visible;
				IdleModeGrid.Opacity = baseOpacity;
				IdleModeGrid.IsHitTestVisible = hitTest;
				if (IdleModeScale != null) { IdleModeScale.ScaleX = 1.0; IdleModeScale.ScaleY = 1.0; }
			}
		}
		else if (live >= 0.999)
		{
			if (IdleModeGrid != null)
			{
				IdleModeGrid.Visibility = Visibility.Collapsed;
				IdleModeGrid.Opacity = 0.0;
			}
			if (MediaModeGrid != null)
			{
				MediaModeGrid.Visibility = Visibility.Visible;
				MediaModeGrid.Opacity = baseOpacity;
				MediaModeGrid.IsHitTestVisible = hitTest;
				if (MediaModeScale != null) { MediaModeScale.ScaleX = 1.0; MediaModeScale.ScaleY = 1.0; }
			}
		}
		else
		{
			// Бесшовный кроссфейд и масштаб между двумя режимами
			if (IdleModeGrid != null)
			{
				IdleModeGrid.Visibility = Visibility.Visible;
				IdleModeGrid.Opacity = (1.0 - live) * baseOpacity;
				IdleModeGrid.IsHitTestVisible = hitTest && live < 0.5;
				if (IdleModeScale != null)
				{
					double s = 0.92 + 0.08 * (1.0 - live);
					IdleModeScale.ScaleX = s;
					IdleModeScale.ScaleY = s;
				}
			}
			if (MediaModeGrid != null)
			{
				MediaModeGrid.Visibility = Visibility.Visible;
				MediaModeGrid.Opacity = live * baseOpacity;
				MediaModeGrid.IsHitTestVisible = hitTest && live >= 0.5;
				if (MediaModeScale != null)
				{
					double s = 0.92 + 0.08 * live;
					MediaModeScale.ScaleX = s;
					MediaModeScale.ScaleY = s;
				}
			}
		}
		if (NotchBarContainer != null)
		{
			if (hide > 0.15)
			{
				double baseContainerOpacity = Ui.Clamp((hide - 0.15) / 0.65);
				double fsMultiplier = isFullscreen ? Ui.Lerp(0.20, 1.0, notchHv) : Ui.Lerp(0.60, 1.0, notchHv);
				NotchBarContainer.Opacity = baseContainerOpacity * fsMultiplier;
				NotchBarContainer.Visibility = Visibility.Visible;

				double barW = Ui.Lerp(34.0, 42.0, notchHv);
				double barH = Ui.Lerp(2.0, 2.5, notchHv);
				double barR = barH / 2.0;

				if (NotchBarIndicator != null)
				{
					NotchBarIndicator.Width = barW;
					NotchBarIndicator.Height = barH;
					NotchBarIndicator.CornerRadius = new CornerRadius(barR);
					// Цвет световой полоски всегда белый
					if (NotchBarBrush != null)
					{
						NotchBarIndicator.Background = NotchBarBrush;
					}
					else
					{
						NotchBarIndicator.Background = Brushes.White;
					}
				}
				if (NotchBarGlow != null)
				{
					NotchBarGlow.Width = barW;
					NotchBarGlow.Height = barH;
					NotchBarGlow.CornerRadius = new CornerRadius(barR);
					NotchBarGlow.Background = Brushes.White;
				}
				if (NotchBarShadow != null)
				{
					NotchBarShadow.Opacity = Ui.Lerp(0.30, 0.55, notchHv);
					NotchBarShadow.BlurRadius = Ui.Lerp(5.0, 8.0, notchHv);
					NotchBarShadow.Color = Colors.White;
				}
			}
			else
			{
				NotchBarContainer.Opacity = 0.0;
				NotchBarContainer.Visibility = Visibility.Collapsed;
			}
		}
		if (NotchShadow != null)
		{
			double shadowOpacity = isFullscreen ? Ui.Lerp(0.05, 0.35, notchHv) : Ui.Lerp(0.30, 0.16, hide);
			NotchShadow.Opacity = shadowOpacity;
			NotchShadow.BlurRadius = Ui.Lerp(18.0, 8.0, hide);
			NotchShadow.ShadowDepth = Ui.Lerp(4.0, 1.5, hide);
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

	private void OnPomoNotchClick(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		_island?.Select("pomodoro");
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
		NotchCapsule.Background = NotchHoverBrush;
		NotchCapsule.BorderBrush = NotchHoverBorderBrush;
		if (LeftEar != null) LeftEar.Fill = NotchHoverBrush;
		if (RightEar != null) RightEar.Fill = NotchHoverBrush;
	}

	private void OnNotchMouseLeave(object sender, MouseEventArgs e)
	{
		_island?.SetHover(null);
		NotchCapsule.Background = NotchNormalBrush;
		NotchCapsule.BorderBrush = NotchNormalBorderBrush;
		if (LeftEar != null) LeftEar.Fill = NotchNormalBrush;
		if (RightEar != null) RightEar.Fill = NotchNormalBrush;
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace QuickNotchCs;

/// <summary>
/// Радиальное колесо быстрой конвертации файлов (Shift-Convert).
/// Вызывается нажатием Shift во время перетаскивания файла в Проводнике/на Рабочем столе.
/// </summary>
public sealed class ShiftWheelWindow : Window
{
    private const double WheelSize = 320.0;
    private const double Center = WheelSize / 2.0;
    private const double InnerRadius = 46.0;
    private const double OuterRadius = 144.0;
    private const double IconRadius = 96.0;

    private static ShiftWheelWindow? _active;
    private static long _lastCloseTicks;

    private readonly IslandView _island;
    private readonly Grid _rootGrid;
    private readonly Canvas _sectorCanvas;
    private readonly ScaleTransform _scaleTransform;
    private readonly TextBlock _centerIconText;
    private readonly TextBlock _centerTitleText;
    private readonly TextBlock _centerSubText;
    private readonly Border _centerHub;

    private readonly DispatcherTimer _probeTimer;
    private readonly DispatcherTimer _safetyTimer;

    private List<ShiftConvertOption> _options = new();
    private readonly List<System.Windows.Shapes.Path> _sectorPaths = new();
    private readonly List<Border> _sectorBadges = new();
    private string[] _draggedFiles = Array.Empty<string>();
    private int _hoveredIndex = -1;
    private bool _confirmedFileDrag;
    private bool _isClickMode;
    private bool _isOneShot;
    private bool _isClosing;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const int VK_LBUTTON = 0x01;
    private const int VK_ESCAPE = 0x1B;

    public static void TryShowAtCursor(IslandView island, int screenPxX, int screenPxY)
    {
        if (!island.S.ShiftConvertEnabled) return;
        if (_active != null) return;
        if (Environment.TickCount64 - _lastCloseTicks < 350) return;

        island.Dispatcher.InvokeAsync(() =>
        {
            if (_active != null) return;
            try
            {
                var win = new ShiftWheelWindow(island, screenPxX, screenPxY, null, false);
                _active = win;
                win.Show();
                // Слегка дёргаем курсор на (0,0), чтобы Windows OLE DoDragDrop мгновенно вызвал DragEnter на нашем окне
                mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0, UIntPtr.Zero);
            }
            catch
            {
                _active = null;
            }
        });
    }

    public static void ShowForFilesAtCursor(IslandView island, string[] files, int screenPxX, int screenPxY, bool isOneShot = false)
    {
        if (files == null || files.Length == 0)
        {
            if (isOneShot) Application.Current?.Shutdown();
            return;
        }

        island.Dispatcher.InvokeAsync(() =>
        {
            try { _active?.CloseWheel(suppressShutdown: true); } catch { }
            try
            {
                var win = new ShiftWheelWindow(island, screenPxX, screenPxY, files, isOneShot);
                _active = win;
                win.Show();
                win.Activate();
            }
            catch
            {
                _active = null;
                if (isOneShot) Application.Current?.Shutdown();
            }
        });
    }

    private ShiftWheelWindow(IslandView island, int screenPxX, int screenPxY, string[]? preloadedFiles, bool isOneShot)
    {
        _island = island;
        _isOneShot = isOneShot;
        _isClickMode = preloadedFiles != null && preloadedFiles.Length > 0;

        Width = WheelSize;
        Height = WheelSize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = _isClickMode;
        ResizeMode = ResizeMode.NoResize;
        AllowDrop = true;

        // Начинаем почти прозрачными (0.01), чтобы проверить через DragEnter, действительно ли тянут файл
        Opacity = _isClickMode ? 1.0 : 0.01;
        if (_isClickMode) Cursor = Cursors.Hand;

        PositionAtScreenPixels(screenPxX, screenPxY);

        _scaleTransform = new ScaleTransform(0.72, 0.72, Center, Center);
        _rootGrid = new Grid
        {
            Width = WheelSize,
            Height = WheelSize,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // Для 100% перехвата OLE HitTest по всей площади
            RenderTransform = _scaleTransform
        };

        // Внешнее свечение/подложка колеса
        var outerDisc = new Ellipse
        {
            Width = OuterRadius * 2 + 12,
            Height = OuterRadius * 2 + 12,
            Fill = new SolidColorBrush(Color.FromArgb(0xDC, 0x0E, 0x0E, 0x12)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 28,
                ShadowDepth = 6,
                Opacity = 0.75,
                RenderingBias = RenderingBias.Performance
            }
        };
        _rootGrid.Children.Add(outerDisc);

        _sectorCanvas = new Canvas
        {
            Width = WheelSize,
            Height = WheelSize,
            IsHitTestVisible = false
        };
        _rootGrid.Children.Add(_sectorCanvas);

        // Центральный хаб (отмена / инфо о выбранном формате)
        _centerHub = new Border
        {
            Width = InnerRadius * 2 - 4,
            Height = InnerRadius * 2 - 4,
            CornerRadius = new CornerRadius(InnerRadius),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x18, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1.2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var centerStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _centerIconText = new TextBlock
        {
            Text = "⚡",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 1)
        };

        _centerTitleText = new TextBlock
        {
            Text = "Shift",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _centerSubText = new TextBlock
        {
            Text = Loc.T("Отмена", "Cancel", "İptal"),
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0xA0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = InnerRadius * 1.75
        };

        centerStack.Children.Add(_centerIconText);
        centerStack.Children.Add(_centerTitleText);
        centerStack.Children.Add(_centerSubText);
        _centerHub.Child = centerStack;
        _rootGrid.Children.Add(_centerHub);

        Content = _rootGrid;

        // Если за 220 мс Windows не прислал DragEnter с файлом — значит пользователь тянет просто текст или окно, тихо закрываемся
        _probeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _probeTimer.Tick += (_, _) =>
        {
            _probeTimer.Stop();
            if (!_confirmedFileDrag && !_isClickMode)
            {
                CloseWheel();
            }
        };

        // Таймер безопасности: если кнопку мыши отпустили вне колеса или нажали Esc
        _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _safetyTimer.Tick += (_, _) =>
        {
            if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
            {
                CloseWheel();
                return;
            }
            if (!_isClickMode && _confirmedFileDrag && (GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
            {
                // Даём 1 тик на срабатывание OnDrop, затем закрываем
                _safetyTimer.Stop();
                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(110);
                    if (!_isClosing) CloseWheel();
                });
            }
        };

        if (_isClickMode && preloadedFiles != null)
        {
            _confirmedFileDrag = true;
            _draggedFiles = preloadedFiles;
            _options = ShiftConvertService.GetOptionsForFiles(preloadedFiles);
            BuildSectors();
            SetHoveredSector(-1);
            RevealWheelAnimation();
            _safetyTimer.Start();

            Deactivated += (_, _) =>
            {
                if (!_isClosing) CloseWheel();
            };
        }
        else
        {
            _probeTimer.Start();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isClickMode)
        {
            UpdateHoverFromPoint(e.GetPosition(_rootGrid));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_isClickMode) return;
        e.Handled = true;

        int chosenIndex = _hoveredIndex;
        var files = _draggedFiles;
        var options = _options;
        bool oneShot = _isOneShot;

        if (chosenIndex >= 0 && chosenIndex < options.Count && files.Length > 0)
        {
            CloseWheel(suppressShutdown: true);
            ExecuteConversion(_island, files, options[chosenIndex], shutdownOnComplete: oneShot);
        }
        else
        {
            CloseWheel();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            CloseWheel();
        }
    }

    private void PositionAtScreenPixels(int pxX, int pxY)
    {
        double dpiX = 1.0, dpiY = 1.0;
        try
        {
            if (_island.Host != null)
            {
                var src = PresentationSource.FromVisual(_island.Host);
                if (src?.CompositionTarget != null)
                {
                    dpiX = src.CompositionTarget.TransformToDevice.M11;
                    dpiY = src.CompositionTarget.TransformToDevice.M22;
                }
            }
        }
        catch { }

        double dipX = pxX / dpiX;
        double dipY = pxY / dpiY;

        Left = dipX - Center;
        Top = dipY - Center;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = WinApi.GetWindowLong(hwnd, WinApi.GWL_EXSTYLE);
            int addStyle = _isClickMode
                ? WinApi.WS_EX_TOOLWINDOW
                : (WinApi.WS_EX_TOOLWINDOW | WinApi.WS_EX_NOACTIVATE);
            WinApi.SetWindowLong(hwnd, WinApi.GWL_EXSTYLE, exStyle | addStyle);
        }
        catch { }
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            _probeTimer.Stop();
            if (!_confirmedFileDrag)
            {
                _confirmedFileDrag = true;
                _draggedFiles = files;
                _options = ShiftConvertService.GetOptionsForFiles(files);
                BuildSectors();
                RevealWheelAnimation();
                _safetyTimer.Start();
            }
            e.Effects = DragDropEffects.Copy;
            UpdateHoverFromPoint(e.GetPosition(_rootGrid));
        }
        else
        {
            e.Effects = DragDropEffects.None;
            CloseWheel();
        }
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (_confirmedFileDrag)
        {
            e.Effects = DragDropEffects.Copy;
            UpdateHoverFromPoint(e.GetPosition(_rootGrid));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        SetHoveredSector(-1);
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        e.Handled = true;

        int chosenIndex = _hoveredIndex;
        var files = _draggedFiles;
        var options = _options;

        CloseWheel();

        if (chosenIndex >= 0 && chosenIndex < options.Count && files.Length > 0)
        {
            var opt = options[chosenIndex];
            ExecuteConversion(_island, files, opt);
        }
    }

    private static CancellationTokenSource? _activeCts;

    public static void ExecuteConversion(IslandView island, IReadOnlyList<string> files, ShiftConvertOption opt, bool shutdownOnComplete = false)
    {
        try { _activeCts?.Cancel(); } catch { }
        try { _activeCts?.Dispose(); } catch { }
        var cts = new CancellationTokenSource();
        _activeCts = cts;

        string targetInfo = files.Count == 1
            ? System.IO.Path.GetFileName(files[0])
            : $"{files.Count} {Loc.T("файл(ов)", "files", "dosya")}";

        if (opt.Key != "vid_frame")
        {
            island.ShowNotification(new NotificationItem
            {
                App = "SHIFT CONVERT",
                Title = Loc.T($"Выполняется: {opt.Label}…", $"Processing: {opt.Label}…", $"İşleniyor: {opt.Label}…"),
                Body = $"{targetInfo} · {Loc.T("Нажмите ✕ для отмены", "Click ✕ to cancel", "İptal için ✕")}",
                AccentColor = opt.Accent,
                IsLoading = true,
                ShowProgressBar = true,
                OnCancel = () =>
                {
                    try { cts.Cancel(); } catch { }
                }
            }, 25.0);
        }

        // Для операций с буфером обмена или быстрыми ссылками выполняем в UI потоке или фоне
        Task.Run(async () =>
        {
            long startTicks = Environment.TickCount64;
            var res = ShiftConvertService.ConvertFiles(island, files, opt.Key, cts.Token, pct =>
            {
                if (cts.IsCancellationRequested) return;
                int clamped = Math.Clamp((int)Math.Round(pct), 1, 99);
                island.Dispatcher.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    string progTitle = Loc.T(
                        $"Выполняется: {opt.Label} ({clamped}%)…",
                        $"Processing: {opt.Label} ({clamped}%)…",
                        $"İşleniyor: {opt.Label} (%{clamped})…");
                    island.ToastHost?.UpdateProgress(clamped, progTitle);
                });
            });
            long elapsed = Environment.TickCount64 - startTicks;
            if (!res.Cancelled && elapsed < 420)
            {
                await Task.Delay((int)(420 - elapsed)).ConfigureAwait(false);
            }
            if (ReferenceEquals(_activeCts, cts))
            {
                _activeCts = null;
            }
            try { cts.Dispose(); } catch { }

            island.Dispatcher.InvokeAsync(() =>
            {
                island.HomeHost?.RefreshConnectivity();
                island.LinksHost?.RefreshAll();
                island.ClipboardHost?.RefreshAll();

                if (res.Success && !res.Cancelled && island.S.ShiftConvertAutoReveal && !string.IsNullOrEmpty(res.OutputPath))
                {
                    try
                    {
                        if (File.Exists(res.OutputPath))
                        {
                            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{res.OutputPath}\"") { UseShellExecute = true });
                        }
                        else if (Directory.Exists(res.OutputPath))
                        {
                            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{res.OutputPath}\"") { UseShellExecute = true });
                        }
                    }
                    catch { }
                }

                island.ShowNotification(new NotificationItem
                {
                    App = "SHIFT CONVERT",
                    Title = res.Title,
                    Body = res.Message,
                    IconSymbol = res.Success ? opt.Icon : (res.Cancelled ? "✕" : "⚠️"),
                    AccentColor = res.Success ? opt.Accent : Theme.Red,
                    IsLoading = false,
                    ShowProgressBar = true,
                    OnClick = () =>
                    {
                        if (!string.IsNullOrEmpty(res.OpenSection))
                        {
                            island.Select(res.OpenSection);
                            island.Expand();
                        }
                        else if (!string.IsNullOrEmpty(res.OutputPath) && File.Exists(res.OutputPath))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{res.OutputPath}\"")
                                {
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        }
                        else if (!string.IsNullOrEmpty(res.OutputPath) && Directory.Exists(res.OutputPath))
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{res.OutputPath}\"")
                                {
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        }
                        else if (opt.Key == "links")
                        {
                            island.Select("links");
                            island.Expand();
                        }
                        else if (opt.Key == "clipboard")
                        {
                            island.Select("clipboard");
                            island.Expand();
                        }
                    }
                }, 3.6);

                if (shutdownOnComplete || App.IsOneShotWheelMode)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(3800).ConfigureAwait(false);
                        Application.Current?.Dispatcher.InvokeAsync(() => Application.Current?.Shutdown());
                    });
                }
            });
        });
    }

    private void RevealWheelAnimation()
    {
        var dur = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140));
        var scaleAnim = new DoubleAnimation(0.76, 1.0, dur) { EasingFunction = ease };

        BeginAnimation(OpacityProperty, fadeAnim);
        _scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        _scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void BuildSectors()
    {
        _sectorCanvas.Children.Clear();
        _sectorPaths.Clear();
        _sectorBadges.Clear();

        int count = _options.Count;
        if (count == 0) return;

        double stepAngle = 360.0 / count;
        double gapDeg = count >= 7 ? 2.5 : 3.2;

        for (int i = 0; i < count; i++)
        {
            var opt = _options[i];
            // Начинаем сверху (-90 градусов)
            double startDeg = -90.0 + i * stepAngle - stepAngle / 2.0 + gapDeg / 2.0;
            double endDeg = startDeg + stepAngle - gapDeg;

            var path = new System.Windows.Shapes.Path
            {
                Data = CreateSectorGeometry(Center, Center, InnerRadius + 3, OuterRadius, startDeg, endDeg),
                Fill = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1.0
            };
            _sectorPaths.Add(path);
            _sectorCanvas.Children.Add(path);

            double midRad = (-90.0 + i * stepAngle) * Math.PI / 180.0;
            double bx = Center + IconRadius * Math.Cos(midRad);
            double by = Center + IconRadius * Math.Sin(midRad);

            var badgeStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            badgeStack.Children.Add(new TextBlock
            {
                Text = opt.Icon,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1)
            });

            badgeStack.Children.Add(new TextBlock
            {
                Text = opt.Label,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var badge = new Border
            {
                Width = 64,
                Height = 44,
                CornerRadius = new CornerRadius(10),
                Child = badgeStack
            };

            Canvas.SetLeft(badge, bx - 32);
            Canvas.SetTop(badge, by - 22);
            _sectorBadges.Add(badge);
            _sectorCanvas.Children.Add(badge);
        }
    }

    private void UpdateHoverFromPoint(Point pt)
    {
        if (_options.Count == 0) return;

        double dx = pt.X - Center;
        double dy = pt.Y - Center;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < InnerRadius + 2 || dist > OuterRadius + 24)
        {
            SetHoveredSector(-1);
            return;
        }

        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI; // -180..180, -90 is top
        double step = 360.0 / _options.Count;
        double shifted = (angle + 90.0 + step / 2.0 + 360.0) % 360.0;
        int idx = (int)(shifted / step) % _options.Count;
        SetHoveredSector(idx);
    }

    private void SetHoveredSector(int idx)
    {
        if (_hoveredIndex == idx) return;
        _hoveredIndex = idx;

        for (int i = 0; i < _sectorPaths.Count; i++)
        {
            bool active = (i == idx);
            var opt = _options[i];
            _sectorPaths[i].Fill = active
                ? new SolidColorBrush(Color.FromArgb(0x6E, opt.Accent.R, opt.Accent.G, opt.Accent.B))
                : new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            _sectorPaths[i].Stroke = active
                ? new SolidColorBrush(opt.Accent)
                : new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
            _sectorPaths[i].StrokeThickness = active ? 1.8 : 1.0;
        }

        if (idx >= 0 && idx < _options.Count)
        {
            var opt = _options[idx];
            _centerIconText.Text = opt.Icon;
            _centerTitleText.Text = opt.Label;
            _centerTitleText.Foreground = new SolidColorBrush(opt.Accent);
            _centerSubText.Text = opt.SubLabel;
            _centerHub.BorderBrush = new SolidColorBrush(opt.Accent);
        }
        else
        {
            var badge = GetPayloadBadge();
            _centerIconText.Text = badge.icon;
            _centerTitleText.Text = badge.title;
            _centerTitleText.Foreground = Brushes.White;
            _centerSubText.Text = badge.sub;
            _centerHub.BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, 0xC6, 0xF2, 0x4E));
        }
    }

    private (string icon, string title, string sub) GetPayloadBadge()
    {
        if (_draggedFiles.Length == 0)
        {
            return ("⚡", "Shift", Loc.T("Конвертер", "Converter", "Dönüştürücü"));
        }

        if (_draggedFiles.Length > 1)
        {
            long totalBytes = 0;
            foreach (var f in _draggedFiles)
            {
                try { if (File.Exists(f)) totalBytes += new FileInfo(f).Length; } catch { }
            }
            string sizeStr = totalBytes > 0 ? ShiftConvertService.FormatBytes(totalBytes) : Loc.T("Пакет", "Batch", "Paket");
            return ("📚", $"{_draggedFiles.Length} {Loc.T("файлов", "files", "dosya")}", sizeStr);
        }

        string single = _draggedFiles[0];
        string ext = System.IO.Path.GetExtension(single).ToUpperInvariant().TrimStart('.');
        if (string.IsNullOrEmpty(ext)) ext = Loc.T("Файл", "File", "Dosya");

        long singleBytes = 0;
        try { if (File.Exists(single)) singleBytes = new FileInfo(single).Length; } catch { }
        string singleSize = singleBytes > 0 ? ShiftConvertService.FormatBytes(singleBytes) : "";

        string icon = "📄";
        if (ShiftConvertService.IsPdfFile(single)) icon = "📕";
        else if (ShiftConvertService.IsImageFile(single)) icon = "🖼️";
        else if (ShiftConvertService.IsAudioFile(single)) icon = "🎵";
        else if (ShiftConvertService.IsVideoFile(single)) icon = "🎬";
        else if (ShiftConvertService.IsZipFile(single)) icon = "📦";

        return (icon, ext, singleSize);
    }

    private static Geometry CreateSectorGeometry(double cx, double cy, double rInner, double rOuter, double startDeg, double endDeg)
    {
        double startRad = startDeg * Math.PI / 180.0;
        double endRad = endDeg * Math.PI / 180.0;

        Point p1 = new(cx + rOuter * Math.Cos(startRad), cy + rOuter * Math.Sin(startRad));
        Point p2 = new(cx + rOuter * Math.Cos(endRad), cy + rOuter * Math.Sin(endRad));
        Point p3 = new(cx + rInner * Math.Cos(endRad), cy + rInner * Math.Sin(endRad));
        Point p4 = new(cx + rInner * Math.Cos(startRad), cy + rInner * Math.Sin(startRad));

        bool largeArc = (endDeg - startDeg) > 180.0;

        var fig = new PathFigure { StartPoint = p1, IsClosed = true, IsFilled = true };
        fig.Segments.Add(new ArcSegment
        {
            Point = p2,
            Size = new Size(rOuter, rOuter),
            IsLargeArc = largeArc,
            SweepDirection = SweepDirection.Clockwise
        });
        fig.Segments.Add(new LineSegment(p3, isStroked: true));
        fig.Segments.Add(new ArcSegment
        {
            Point = p4,
            Size = new Size(rInner, rInner),
            IsLargeArc = largeArc,
            SweepDirection = SweepDirection.Counterclockwise
        });

        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private void CloseWheel(bool suppressShutdown = false)
    {
        if (_isClosing) return;
        _isClosing = true;
        _probeTimer.Stop();
        _safetyTimer.Stop();
        _lastCloseTicks = Environment.TickCount64;
        if (_active == this) _active = null;
        try { Close(); } catch { }
        if ((_isOneShot || App.IsOneShotWheelMode) && !suppressShutdown)
        {
            Application.Current?.Shutdown();
        }
    }
}

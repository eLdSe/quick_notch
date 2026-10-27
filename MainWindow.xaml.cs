using System;
using System.Windows;
using System.Windows.Interop;

namespace QuickNotchCs;

public partial class MainWindow : Window
{
    public IntPtr Handle { get; private set; }
    private uint _wakeMsg;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            Handle = new WindowInteropHelper(this).Handle;

            // Центрируем фиксированное окно вверху экрана
            Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
            Top = 0;

            // WS_EX_TOOLWINDOW: скрывает из Alt+Tab и панели задач
            int exStyle = WinApi.GetWindowLong(Handle, WinApi.GWL_EXSTYLE);
            WinApi.SetWindowLong(Handle, WinApi.GWL_EXSTYLE, exStyle | WinApi.WS_EX_TOOLWINDOW);

            var source = HwndSource.FromHwnd(Handle);
            source?.AddHook(WndProc);

            // Горячая клавиша Ctrl+Alt+N
            WinApi.RegisterHotKey(Handle, WinApi.HOTKEY_ID, WinApi.MOD_CONTROL | WinApi.MOD_ALT | WinApi.MOD_NOREPEAT, WinApi.VK_N);

            // Отслеживание изменений буфера обмена
            WinApi.AddClipboardFormatListener(Handle);

            _wakeMsg = WinApi.RegisterWindowMessage(WinApi.WakeMessageName);

            Island.Host = this;
            Island.Initialize();
            SummonHookService.Initialize(Island);
            if (Island.S.RobotCompanion && Island.S.RobotRoaming)
            {
                Island.LaunchAeroToDesktop();
            }
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WinApi.WM_NCHITTEST)
        {
            // Пропускаем клики сквозь прозрачную зону окна
            int screenX = (short)(lParam.ToInt32() & 0xFFFF);
            int screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            var p = PointFromScreen(new Point(screenX, screenY));
            if (Island.HitTestIsland(p))
            {
                handled = true;
                return new IntPtr(WinApi.HTCLIENT);
            }
            handled = true;
            return new IntPtr(WinApi.HTTRANSPARENT);
        }
        else if (msg == WinApi.WM_HOTKEY && (int)wParam == WinApi.HOTKEY_ID)
        {
            Island.OnHotkey();
            handled = true;
        }
        else if (_wakeMsg != 0 && (uint)msg == _wakeMsg)
        {
            Island.OnHotkey();
            handled = true;
        }
        else if (msg == WinApi.WM_CLIPBOARDUPDATE)
        {
            Island.OnClipboardChanged();
            handled = true;
        }
        else if (msg == WinApi.WM_POWERBROADCAST)
        {
            int pEvent = wParam.ToInt32();
            if (pEvent is WinApi.PBT_APMPOWERSTATUSCHANGE or WinApi.PBT_APMRESUMEAUTOMATIC or WinApi.PBT_APMRESUMESUSPEND)
            {
                Island.OnPowerChanged();
            }
        }
        else if (msg == WinApi.WM_DEVICECHANGE)
        {
            int devEvent = wParam.ToInt32();
            if (devEvent is WinApi.DBT_DEVICEARRIVAL or WinApi.DBT_DEVICEREMOVECOMPLETE)
            {
                Island.OnDeviceChanged(devEvent);
            }
        }
        return IntPtr.Zero;
    }

    public void ApplyBounds(double left, double top, double width, double height)
    {
        // Окно имеет фиксированный размер 920x570, DWM ресайзить не нужно
    }

    public void ShowWindow() => Visibility = Visibility.Visible;

    public void HideWindow() => Visibility = Visibility.Hidden;

    protected override void OnClosed(EventArgs e)
    {
        try { Island.PetWindow?.Close(); } catch { }
        NotificationService.Shutdown();
        if (Handle != IntPtr.Zero)
        {
            SummonHookService.Shutdown();
            WinApi.UnregisterHotKey(Handle, WinApi.HOTKEY_ID);
            WinApi.RemoveClipboardFormatListener(Handle);
        }
        base.OnClosed(e);
    }
}
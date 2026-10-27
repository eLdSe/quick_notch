using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickNotchCs;

/// <summary>Низкоуровневые вызовы Win32 (порт winapi.py): окна, горячая клавиша, питание.</summary>
public static class WinApi
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint VK_N = 0x4E;
    public const int HOTKEY_ID = 9001;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_NCHITTEST = 0x0084;
    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT = 1;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMPOWERSTATUSCHANGE = 0x000A;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int WM_DEVICECHANGE = 0x0219;
    public const int DBT_DEVICEARRIVAL = 0x8000;
    public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    public const string WakeMessageName = "QuickNotch_WakeIsland_Message";

    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    private static readonly string[] ShellClasses =
        { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    [ThreadStatic] private static StringBuilder? _classBuf;
    private static StringBuilder ClassBuf => _classBuf ??= new StringBuilder(64);

    private static bool IsShellClass(StringBuilder sb)
    {
        for (int i = 0; i < ShellClasses.Length; i++)
        {
            string s = ShellClasses[i];
            if (sb.Length == s.Length)
            {
                bool match = true;
                for (int j = 0; j < s.Length; j++)
                {
                    if (sb[j] != s[j]) { match = false; break; }
                }
                if (match) return true;
            }
        }
        return false;
    }

    private struct CoverState
    {
        public uint OwnPid;
        public int Left, Top, Right, Bottom;
        public bool Found;
    }

    [ThreadStatic] private static CoverState _coverState;

    private static readonly EnumWindowsProc _coverProc = (hWnd, _) =>
    {
        try
        {
            // Сначала быстрая проверка координат: отсекает 98% окон без лишних Win32 API вызовов
            if (!GetWindowRect(hWnd, out RECT r)) return true;
            if (r.Left >= _coverState.Right || r.Right <= _coverState.Left || r.Top >= _coverState.Bottom || r.Bottom <= _coverState.Top) return true;

            if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == _coverState.OwnPid) return true;
            if ((GetWindowLong(hWnd, GWL_EXSTYLE) & (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0) return true;
            if (DwmGetWindowAttribute(hWnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            var buf = ClassBuf;
            buf.Clear();
            GetClassName(hWnd, buf, 64);
            if (IsShellClass(buf)) return true;

            _coverState.Found = true;
            return false;
        }
        catch { }
        return true;
    };

    /// <summary>Есть ли окно другой программы под областью (самое верхнее по z-порядку).</summary>
    public static bool WindowCoversArea(uint ownPid, int left, int top, int right, int bottom)
    {
        var buf = ClassBuf;

        // Быстрая проверка: если активное переднее окно закрывает остров, не нужно перебирать все окна
        IntPtr fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            try
            {
                GetWindowThreadProcessId(fg, out uint fgPid);
                if (fgPid != ownPid && IsWindowVisible(fg) && !IsIconic(fg))
                {
                    if (GetWindowRect(fg, out RECT fr) && fr.Left < right && fr.Right > left && fr.Top < bottom && fr.Bottom > top)
                    {
                        buf.Clear();
                        GetClassName(fg, buf, 64);
                        if (!IsShellClass(buf))
                        {
                            if ((GetWindowLong(fg, GWL_EXSTYLE) & (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) == 0)
                            {
                                if (DwmGetWindowAttribute(fg, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        _coverState = new CoverState
        {
            OwnPid = ownPid,
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom,
            Found = false
        };

        EnumWindows(_coverProc, IntPtr.Zero);
        return _coverState.Found;
    }

    /// <summary>Активное окно закрывает весь монитор (видео на весь экран, игра).</summary>
    public static bool FullscreenAppActive(IntPtr ownHwnd)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == ownHwnd) return false;

        var name = ClassBuf;
        name.Clear();
        GetClassName(hwnd, name, 64);
        if (IsShellClass(name)) return false;

        if (!GetWindowRect(hwnd, out RECT rect)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return false;

        bool Covers(RECT a) => rect.Left <= a.Left && rect.Top <= a.Top && rect.Right >= a.Right && rect.Bottom >= a.Bottom;

        var m = info.rcMonitor;
        if (Covers(m)) return true;

        var w = info.rcWork;
        bool workDiffers = w.Left != m.Left || w.Top != m.Top || w.Right != m.Right || w.Bottom != m.Bottom;
        return Covers(w) && workDiffers;
    }

    /// <summary>Мультимедийные клавиши Windows (общая громкость и плеер).</summary>
    public static void MediaKey(string action)
    {
        byte key = action switch
        {
            "mute" => 0xAD,
            "down" => 0xAE,
            "up" => 0xAF,
            "play_pause" => 0xB3,
            "previous" => 0xB1,
            "next" => 0xB0,
            _ => 0
        };
        if (key == 0) return;
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
    }

    /// <summary>Сообщает программам о смене темы. Может ждать зависшие окна: вызывать в потоке.</summary>
    public static void BroadcastThemeChange()
    {
        SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 0x0002, 5000, out _);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void DarkTitlebar(IntPtr hwnd)
    {
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); } catch { }
    }

    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    private static long _lastTrimTicks;

    public static void TrimWorkingSet()
    {
        try
        {
            long now = Environment.TickCount64;
            if (now - _lastTrimTicks < 6000) return; // Защита от частого сброса страниц
            _lastTrimTicks = now;
            GC.Collect(1, GCCollectionMode.Optimized, false);
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
        }
        catch { }
    }
}

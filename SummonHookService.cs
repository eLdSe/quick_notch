using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickNotchCs;

public static class SummonHookService
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;

    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static IntPtr _mouseHookId = IntPtr.Zero;
    private static IntPtr _kbdHookId = IntPtr.Zero;
    private static LowLevelProc? _mouseProc;
    private static LowLevelProc? _kbdProc;
    private static IslandView? _island;
    private static long _lastSummonTicks;

    // Отслеживание перетаскивания файла + нажатия Shift для вызова Shift-Convert колеса
    private static bool _lButtonDown;
    private static bool _shiftHeldAtStart;
    private static bool _dragThresholdMet;
    private static bool _wheelTriggeredThisDrag;
    private static int _dragStartX;
    private static int _dragStartY;
    private static int _lastMouseX;
    private static int _lastMouseY;

    public static void Initialize(IslandView island)
    {
        _island = island;
        if (_mouseHookId != IntPtr.Zero) return;

        _mouseProc = MouseHookCallback;
        _kbdProc = KeyboardHookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        _kbdHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _kbdProc, hMod, 0);
    }

    public static void Shutdown()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        if (_kbdHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_kbdHookId);
            _kbdHookId = IntPtr.Zero;
        }
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _island != null)
        {
            int msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN)
            {
                try
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _lButtonDown = true;
                    _dragStartX = hookStruct.pt.x;
                    _dragStartY = hookStruct.pt.y;
                    _lastMouseX = hookStruct.pt.x;
                    _lastMouseY = hookStruct.pt.y;
                    _dragThresholdMet = false;
                    _wheelTriggeredThisDrag = false;
                    _shiftHeldAtStart = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                    // Для полоски Notch реагируем на левый клик мыши, когда остров свёрнут и скрыт за окнами
                    if (_island.Target == 0.0 && _island.IsCoveredHidden)
                    {
                        CheckTrigger(hookStruct.pt.x, hookStruct.pt.y);
                    }
                }
                catch { }
            }
            else if (msg == WM_LBUTTONUP)
            {
                _lButtonDown = false;
                _dragThresholdMet = false;
                _wheelTriggeredThisDrag = false;
            }
            else if (msg == WM_MOUSEMOVE && _lButtonDown)
            {
                try
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _lastMouseX = hookStruct.pt.x;
                    _lastMouseY = hookStruct.pt.y;

                    if (!_dragThresholdMet)
                    {
                        int dx = _lastMouseX - _dragStartX;
                        int dy = _lastMouseY - _dragStartY;
                        if (dx * dx + dy * dy > 324) // > 18px
                        {
                            _dragThresholdMet = true;
                        }
                    }

                    if (_dragThresholdMet && !_shiftHeldAtStart && !_wheelTriggeredThisDrag)
                    {
                        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
                        {
                            _wheelTriggeredThisDrag = true;
                            ShiftWheelWindow.TryShowAtCursor(_island, _lastMouseX, _lastMouseY);
                        }
                    }
                }
                catch { }
            }
        }
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _island != null && _lButtonDown && _dragThresholdMet && !_shiftHeldAtStart && !_wheelTriggeredThisDrag)
        {
            int msg = (int)wParam;
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                try
                {
                    var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    if (kb.vkCode is VK_SHIFT or VK_LSHIFT or VK_RSHIFT)
                    {
                        if ((GetAsyncKeyState(0x01) & 0x8000) != 0)
                        {
                            _wheelTriggeredThisDrag = true;
                            ShiftWheelWindow.TryShowAtCursor(_island, _lastMouseX, _lastMouseY);
                        }
                    }
                }
                catch { }
            }
        }
        return CallNextHookEx(_kbdHookId, nCode, wParam, lParam);
    }

    private static void CheckTrigger(int x, int y)
    {
        if (_island == null) return;
        if (_island.Target == 1.0 || !_island.IsCoveredHidden) return;

        bool onCapsule;
        try
        {
            var p = _island.Host.PointFromScreen(new Point(x, y));
            onCapsule = _island.IsOnCapsule(p);
        }
        catch { onCapsule = false; }

        if (onCapsule)
        {
            long now = Environment.TickCount64;
            if (now - _lastSummonTicks > 800) // антидребезг 800 мс
            {
                _lastSummonTicks = now;
                _island.Dispatcher.InvokeAsync(() => _island.SummonFromEdge());
            }
        }
    }
}

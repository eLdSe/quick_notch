using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace QuickNotchCs;

public static class SummonHookService
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MOUSEWHEEL = 0x020A;

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

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr _hookId = IntPtr.Zero;
    private static LowLevelMouseProc? _proc;
    private static IslandView? _island;
    private static long _lastSummonTicks;

    public static void Initialize(IslandView island)
    {
        _island = island;
        if (_hookId != IntPtr.Zero) return;

        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);
    }

    public static void Shutdown()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN && _island != null)
        {
            // Для полоски Notch реагируем только на левый клик мыши, когда остров свёрнут и скрыт за окнами
            if (_island.Target == 0.0 && _island.IsCoveredHidden)
            {
                try
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    CheckTrigger(hookStruct.pt.x, hookStruct.pt.y);
                }
                catch { }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void CheckTrigger(int x, int y)
    {
        if (_island == null) return;
        if (_island.Target == 1.0 || !_island.IsCoveredHidden) return;

        double screenW = SystemParameters.PrimaryScreenWidth;
        double cx = screenW / 2;

        // Строго нажатие левой кнопки мыши строго по полоске Notch (ширина 140px: |x - cx| <= 70, y <= 7)
        if (y <= 7 && Math.Abs(x - cx) <= 70)
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


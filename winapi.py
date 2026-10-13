from __future__ import annotations

import ctypes
import os
import shutil
import subprocess
import sys
import winreg
from pathlib import Path
from typing import Any


class SystemPowerStatus(ctypes.Structure):
    _fields_ = [
        ("ACLineStatus", ctypes.c_ubyte),
        ("BatteryFlag", ctypes.c_ubyte),
        ("BatteryLifePercent", ctypes.c_ubyte),
        ("SystemStatusFlag", ctypes.c_ubyte),
        ("BatteryLifeTime", ctypes.c_ulong),
        ("BatteryFullLifeTime", ctypes.c_ulong),
    ]


class MemoryStatus(ctypes.Structure):
    _fields_ = [
        ("dwLength", ctypes.c_ulong),
        ("dwMemoryLoad", ctypes.c_ulong),
        ("ullTotalPhys", ctypes.c_ulonglong),
        ("ullAvailPhys", ctypes.c_ulonglong),
        ("ullTotalPageFile", ctypes.c_ulonglong),
        ("ullAvailPageFile", ctypes.c_ulonglong),
        ("ullTotalVirtual", ctypes.c_ulonglong),
        ("ullAvailVirtual", ctypes.c_ulonglong),
        ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
    ]


class FileTime(ctypes.Structure):
    _fields_ = [("low", ctypes.c_ulong), ("high", ctypes.c_ulong)]


class Rect(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


class MonitorInfo(ctypes.Structure):
    _fields_ = [("cbSize", ctypes.c_ulong), ("rcMonitor", Rect),
                ("rcWork", Rect), ("dwFlags", ctypes.c_ulong)]


_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
_get_power_status = _kernel32.GetSystemPowerStatus
_get_power_status.argtypes = [ctypes.POINTER(SystemPowerStatus)]
_get_power_status.restype = ctypes.c_int
_create_mutex = _kernel32.CreateMutexW
_create_mutex.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_wchar_p]
_create_mutex.restype = ctypes.c_void_p
_global_memory_status = _kernel32.GlobalMemoryStatusEx
_global_memory_status.argtypes = [ctypes.POINTER(MemoryStatus)]
_global_memory_status.restype = ctypes.c_int
_get_system_times = _kernel32.GetSystemTimes
_get_system_times.argtypes = [ctypes.POINTER(FileTime)] * 3
_get_system_times.restype = ctypes.c_int
_set_execution_state = _kernel32.SetThreadExecutionState
_set_execution_state.argtypes = [ctypes.c_uint]
_set_execution_state.restype = ctypes.c_uint
_last_cpu: tuple[int, int, int] | None = None

_user32 = ctypes.WinDLL("user32", use_last_error=True)
_user32.GetForegroundWindow.restype = ctypes.c_void_p
_user32.GetWindowRect.argtypes = [ctypes.c_void_p, ctypes.POINTER(Rect)]
_user32.GetClassNameW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_int]
_user32.MonitorFromWindow.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
_user32.MonitorFromWindow.restype = ctypes.c_void_p
_user32.GetMonitorInfoW.argtypes = [ctypes.c_void_p, ctypes.POINTER(MonitorInfo)]
_user32.SendMessageTimeoutW.argtypes = [
    ctypes.c_void_p, ctypes.c_uint, ctypes.c_size_t, ctypes.c_wchar_p,
    ctypes.c_uint, ctypes.c_uint, ctypes.POINTER(ctypes.c_size_t)]
_user32.SendMessageTimeoutW.restype = ctypes.c_ssize_t

_dwmapi = ctypes.WinDLL("dwmapi")
_dwmapi.DwmSetWindowAttribute.argtypes = [
    ctypes.c_void_p, ctypes.c_ulong, ctypes.c_void_p, ctypes.c_ulong]


# ---------- процесс ----------

def already_running() -> bool:
    _create_mutex(None, False, "QuickNotch_SingleInstance")
    return ctypes.get_last_error() == 183


def set_timer_resolution(enabled: bool) -> None:
    try:
        winmm = ctypes.windll.winmm
        (winmm.timeBeginPeriod if enabled else winmm.timeEndPeriod)(1)
    except (AttributeError, OSError):
        pass


def dark_titlebar(hwnd: int) -> None:
    value = ctypes.c_int(1)
    try:
        _dwmapi.DwmSetWindowAttribute(hwnd, 20, ctypes.byref(value),
                                      ctypes.sizeof(value))
    except OSError:
        pass


# ---------- состояние системы ----------

def battery_info() -> tuple[int | None, bool]:
    status = SystemPowerStatus()
    if not _get_power_status(ctypes.byref(status)):
        return None, False
    charging = status.ACLineStatus == 1
    if status.BatteryLifePercent == 255:
        return None, charging
    return min(100, status.BatteryLifePercent), charging


def _ft(v: FileTime) -> int:
    return (v.high << 32) | v.low


def _cpu_fraction() -> float | None:
    global _last_cpu
    idle, kernel, user = FileTime(), FileTime(), FileTime()
    if not _get_system_times(ctypes.byref(idle), ctypes.byref(kernel),
                             ctypes.byref(user)):
        raise ctypes.WinError(ctypes.get_last_error())
    cur = (_ft(idle), _ft(kernel), _ft(user))
    prev, _last_cpu = _last_cpu, cur
    if prev is None:
        return None
    total = (cur[1] - prev[1]) + (cur[2] - prev[2])
    if total <= 0:
        return None
    return max(0.0, min(1.0, (total - (cur[0] - prev[0])) / total))


def system_snapshot() -> dict[str, Any]:
    st = MemoryStatus()
    st.dwLength = ctypes.sizeof(st)
    if not _global_memory_status(ctypes.byref(st)):
        raise ctypes.WinError(ctypes.get_last_error())
    disk = shutil.disk_usage(Path.home().anchor)
    used_ram = st.ullTotalPhys - st.ullAvailPhys
    used_disk = disk.total - disk.free
    cpu = _cpu_fraction()
    return {
        "cpu": "—" if cpu is None else f"{round(cpu * 100)}%",
        "cpu_f": cpu or 0.0,
        "memory": f"{used_ram / 1024**3:.1f} / {st.ullTotalPhys / 1024**3:.1f} ГБ",
        "memory_percent": f"{st.dwMemoryLoad}%",
        "mem_f": st.dwMemoryLoad / 100,
        "disk": f"{used_disk / 1024**3:.0f} / {disk.total / 1024**3:.0f} ГБ",
        "disk_percent": f"{used_disk / disk.total:.0%}",
        "disk_f": used_disk / disk.total,
    }


def fullscreen_app_active(own_hwnd: int) -> bool:
    """Активное окно закрывает весь монитор (видео на весь экран, игра)."""
    hwnd = _user32.GetForegroundWindow()
    if not hwnd or hwnd == own_hwnd:
        return False
    name = ctypes.create_unicode_buffer(64)
    _user32.GetClassNameW(hwnd, name, 64)
    if name.value in ("Progman", "WorkerW", "Shell_TrayWnd"):
        return False
    rect, info = Rect(), MonitorInfo()
    info.cbSize = ctypes.sizeof(info)
    monitor = _user32.MonitorFromWindow(hwnd, 2)
    if not _user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        return False
    if not _user32.GetMonitorInfoW(monitor, ctypes.byref(info)):
        return False
    m = info.rcMonitor
    return (rect.left <= m.left and rect.top <= m.top
            and rect.right >= m.right and rect.bottom >= m.bottom)


# ---------- действия ----------

def media_key(action: str) -> None:
    codes = {"mute": 0xAD, "down": 0xAE, "up": 0xAF,
             "play_pause": 0xB3, "previous": 0xB1, "next": 0xB0}
    key = codes[action]
    ctypes.windll.user32.keybd_event(key, 0, 0, 0)
    ctypes.windll.user32.keybd_event(key, 0, 2, 0)


def open_browser() -> None:
    shortcut = (
        Path(os.environ.get("APPDATA", "")) / "Microsoft" / "Windows"
        / "Start Menu" / "Programs" / "Браузер Opera GX.lnk"
    )
    os.startfile(shortcut if shortcut.is_file() else "https://www.google.com")


def launch(name: str) -> None:
    if name == "browser":
        open_browser()
    else:
        subprocess.Popen({"explorer": "explorer.exe", "notepad": "notepad.exe",
                          "calculator": "calc.exe"}[name])


def open_windows_settings(page: str) -> None:
    os.startfile(f"ms-settings:{page}")


def start_screen_capture() -> None:
    os.startfile("ms-screenclip:")


# ---------- автозапуск ----------

STARTUP_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
STARTUP_VALUE = "QuickNotch"


def autostart_enabled() -> bool:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, STARTUP_KEY, 0,
                            winreg.KEY_READ) as key:
            winreg.QueryValueEx(key, STARTUP_VALUE)
        return True
    except OSError:
        return False


def _autostart_command() -> str:
    if getattr(sys, "frozen", False):
        return f'"{sys.executable}"'
    pythonw = Path(sys.executable).with_name("pythonw.exe")
    if not pythonw.exists():
        pythonw = Path(sys.executable)
    return f'"{pythonw}" "{Path(sys.argv[0]).resolve()}"'


def set_autostart(enabled: bool) -> None:
    """Бросает OSError, если реестр недоступен."""
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, STARTUP_KEY) as key:
        if enabled:
            winreg.SetValueEx(key, STARTUP_VALUE, 0, winreg.REG_SZ,
                              _autostart_command())
        else:
            try:
                winreg.DeleteValue(key, STARTUP_VALUE)
            except FileNotFoundError:
                pass


# ---------- не засыпать ----------

def set_keep_awake(on: bool) -> None:
    """Пока включено, компьютер и экран не уходят в сон. Вызывать из главного потока:
    состояние привязано к потоку, который его выставил."""
    ES_CONTINUOUS = 0x80000000
    ES_SYSTEM_REQUIRED = 0x00000001
    ES_DISPLAY_REQUIRED = 0x00000002
    flags = ES_CONTINUOUS | ((ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED) if on else 0)
    _set_execution_state(flags)


# ---------- тёмная тема Windows ----------

_PERSONALIZE = r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"


def get_dark_theme() -> bool:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, _PERSONALIZE, 0,
                            winreg.KEY_READ) as key:
            return winreg.QueryValueEx(key, "AppsUseLightTheme")[0] == 0
    except OSError:
        return False


def set_dark_theme(on: bool) -> None:
    """Бросает OSError, если реестр недоступен."""
    value = 0 if on else 1
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, _PERSONALIZE, 0,
                        winreg.KEY_SET_VALUE) as key:
        for name in ("AppsUseLightTheme", "SystemUsesLightTheme"):
            winreg.SetValueEx(key, name, 0, winreg.REG_DWORD, value)


def broadcast_theme_change() -> None:
    """Сообщает программам о смене темы. Может ждать зависшие окна: вызывать в потоке."""
    result = ctypes.c_size_t()
    _user32.SendMessageTimeoutW(0xFFFF, 0x001A, 0, "ImmersiveColorSet",
                                0x0002, 5000, ctypes.byref(result))


# ---------- яркость экрана ----------

BRIGHTNESS_HINT = ("Не установлен screen_brightness_control. "
                   "Выполните: pip install screen_brightness_control")


def _co_init() -> None:
    """WMI в фоновом потоке требует инициализации COM (если есть pywin32)."""
    try:
        import pythoncom
        pythoncom.CoInitialize()
    except Exception:  # noqa: BLE001
        pass


def _sbc():
    try:
        import screen_brightness_control as sbc
    except ImportError as error:
        raise RuntimeError(BRIGHTNESS_HINT) from error
    return sbc


def get_brightness() -> int:
    sbc = _sbc()
    _co_init()
    values = [v for v in sbc.get_brightness() if isinstance(v, (int, float))]
    if not values:
        raise RuntimeError("Монитор не поддерживает управление яркостью.")
    return int(values[0])


def set_brightness(level: int) -> None:
    sbc = _sbc()
    _co_init()
    sbc.set_brightness(max(0, min(100, int(level))))


# ---------- громкость (pycaw) ----------

VOLUME_HINT = "Не установлен pycaw. Выполните: pip install pycaw"
_endpoint = None


def _volume_endpoint():
    global _endpoint
    if _endpoint is None:
        try:
            from comtypes import CLSCTX_ALL
            from pycaw.pycaw import AudioUtilities, IAudioEndpointVolume
        except ImportError as error:
            raise RuntimeError(VOLUME_HINT) from error
        device = AudioUtilities.GetSpeakers()
        if hasattr(device, "EndpointVolume"):        # новые версии pycaw
            _endpoint = device.EndpointVolume
        else:                                        # классический способ
            iface = device.Activate(IAudioEndpointVolume._iid_, CLSCTX_ALL, None)
            _endpoint = ctypes.cast(iface, ctypes.POINTER(IAudioEndpointVolume))
    return _endpoint


def _with_endpoint(fn):
    global _endpoint
    try:
        return fn(_volume_endpoint())
    except RuntimeError:
        raise
    except Exception:  # noqa: BLE001 — устройство могло смениться
        _endpoint = None
        return fn(_volume_endpoint())


def get_volume() -> tuple[float, bool]:
    """(громкость 0..1, выключен ли звук)."""
    return _with_endpoint(
        lambda ep: (float(ep.GetMasterVolumeLevelScalar()), bool(ep.GetMute())))


def set_volume(level: float) -> None:
    level = max(0.0, min(1.0, level))
    _with_endpoint(lambda ep: ep.SetMasterVolumeLevelScalar(level, None))


def set_mute(muted: bool) -> None:
    _with_endpoint(lambda ep: ep.SetMute(1 if muted else 0, None))
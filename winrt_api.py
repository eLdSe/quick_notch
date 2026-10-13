from __future__ import annotations

import asyncio

HINT_RADIO = ("Не установлен winrt. Выполните: pip install winrt-runtime "
              "winrt-Windows.Foundation winrt-Windows.Foundation.Collections "
              "winrt-Windows.Devices.Radios")
HINT_MEDIA = ("Не установлен winrt. Выполните: pip install winrt-runtime "
              "winrt-Windows.Foundation winrt-Windows.Foundation.Collections "
              "winrt-Windows.Media.Control")

try:
    from winrt.windows.devices.radios import (
        Radio, RadioAccessStatus, RadioKind, RadioState)
    RADIOS_OK = True
except ImportError:
    RADIOS_OK = False

try:
    from winrt.windows.media.control import (
        GlobalSystemMediaTransportControlsSessionManager as _Manager,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus as _Status)
    MEDIA_OK = True
except ImportError:
    MEDIA_OK = False


def read_radios() -> dict[str, bool | None]:
    """{'WiFi': True/False/None, 'Bluetooth': ...}; None = устройства нет."""
    if not RADIOS_OK:
        raise RuntimeError(HINT_RADIO)

    async def go() -> dict[str, bool | None]:
        out: dict[str, bool | None] = {"WiFi": None, "Bluetooth": None}
        for radio in await Radio.get_radios_async():
            if radio.kind == RadioKind.WI_FI:
                out["WiFi"] = radio.state == RadioState.ON
            elif radio.kind == RadioKind.BLUETOOTH:
                out["Bluetooth"] = radio.state == RadioState.ON
        return out

    return asyncio.run(go())


def set_radio(kind: str, on: bool) -> None:
    if not RADIOS_OK:
        raise RuntimeError(HINT_RADIO)
    wanted = RadioKind.WI_FI if kind == "WiFi" else RadioKind.BLUETOOTH

    async def go() -> None:
        if await Radio.request_access_async() != RadioAccessStatus.ALLOWED:
            raise PermissionError("Windows не разрешила управление радиомодулями.")
        for radio in await Radio.get_radios_async():
            if radio.kind == wanted:
                result = await radio.set_state_async(
                    RadioState.ON if on else RadioState.OFF)
                if result != RadioAccessStatus.ALLOWED:
                    raise PermissionError("Windows не разрешила изменить состояние.")
                return
        raise RuntimeError("Радиомодуль не найден.")

    asyncio.run(go())


def read_media() -> dict:
    if not MEDIA_OK:
        raise RuntimeError(HINT_MEDIA)

    async def go() -> dict:
        manager = await _Manager.request_async()
        session = manager.get_current_session()
        if session is None:
            return {"active": False, "playing": False, "title": "", "artist": ""}
        playing = session.get_playback_info().playback_status == _Status.PLAYING
        props = await session.try_get_media_properties_async()
        return {
            "active": True,
            "playing": playing,
            "title": (props.title or "") if props else "",
            "artist": (props.artist or "") if props else "",
        }

    return asyncio.run(go())


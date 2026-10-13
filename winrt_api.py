from __future__ import annotations

import asyncio
import time
from datetime import datetime, timezone

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

try:
    from winrt.windows.storage.streams import DataReader
    COVER_OK = True
except ImportError:
    COVER_OK = False


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


# ---------- медиа ----------

_KNOWN_APPS = {
    "msedge": "Edge", "chrome": "Chrome", "firefox": "Firefox", "opera": "Opera",
    "operagx": "Opera GX", "brave": "Brave", "spotify": "Spotify", "vlc": "VLC",
    "zunemusic": "Медиаплеер", "yandexmusic": "Яндекс Музыка",
    "telegram": "Telegram", "discord": "Discord", "yandex": "Яндекс Браузер",
}


def pretty_app_name(raw: str) -> str:
    """'Spotify.exe', 'chrome.exe', 'SpotifyAB.SpotifyMusic_xxx!Spotify' -> красивое имя."""
    s = (raw or "").strip()
    if "!" in s:
        s = s.split("!")[-1]
    if s.lower().endswith(".exe"):
        s = s[:-4]
    if "." in s:
        s = s.split(".")[-1]
    if not s:
        return ""
    return _KNOWN_APPS.get(s.lower(), s[:1].upper() + s[1:])


_cover_state = {"key": None, "data": None, "tries": 0}


def _empty_media() -> dict:
    return {"active": False, "playing": False, "title": "", "artist": "",
            "app": "", "pos": 0.0, "dur": 0.0, "can_seek": False,
            "stamp": time.time(), "cover": None}


async def _read_cover(props, key) -> bytes | None:
    """Обложка читается один раз на трек; если плеер отдал её не сразу, пробуем ещё."""
    if not COVER_OK or props is None:
        return None
    if _cover_state["key"] == key and (
            _cover_state["data"] is not None or _cover_state["tries"] >= 3):
        return _cover_state["data"]
    if _cover_state["key"] != key:
        _cover_state.update(key=key, data=None, tries=0)
    _cover_state["tries"] += 1

    data = None
    thumb = props.thumbnail
    if thumb is not None:
        try:
            stream = await thumb.open_read_async()
            size = int(stream.size)
            if 0 < size < 8_000_000:
                try:
                    source = stream.get_input_stream_at(0)
                except Exception:  # noqa: BLE001
                    source = stream
                reader = DataReader(source)
                await reader.load_async(size)
                buf = bytearray(size)
                reader.read_bytes(buf)
                data = bytes(buf)
            try:
                stream.close()
            except Exception:  # noqa: BLE001
                pass
        except Exception:  # noqa: BLE001 — обложка не критична
            data = None
    _cover_state["data"] = data
    return data


def read_media() -> dict:
    if not MEDIA_OK:
        raise RuntimeError(HINT_MEDIA)

    async def go() -> dict:
        manager = await _Manager.request_async()
        session = manager.get_current_session()
        if session is None:
            return _empty_media()

        info = session.get_playback_info()
        playing = info.playback_status == _Status.PLAYING
        props = await session.try_get_media_properties_async()
        title = (props.title or "") if props else ""
        artist = (props.artist or "") if props else ""
        try:
            app = pretty_app_name(session.source_app_user_model_id)
        except Exception:  # noqa: BLE001
            app = ""

        pos = dur = 0.0
        try:
            tl = session.get_timeline_properties()
            start = tl.start_time.total_seconds()
            dur = max(0.0, tl.end_time.total_seconds() - start)
            pos = max(0.0, tl.position.total_seconds() - start)
            if playing:     # позиция отдана на момент last_updated_time
                upd = tl.last_updated_time
                if upd.tzinfo is None:
                    upd = upd.replace(tzinfo=timezone.utc)
                elapsed = (datetime.now(timezone.utc) - upd).total_seconds()
                if 0 <= elapsed < 36000:
                    pos += elapsed
            if dur:
                pos = min(pos, dur)
        except Exception:  # noqa: BLE001 — не все плееры отдают время
            pass

        can_seek = False
        try:
            can_seek = bool(info.controls.is_playback_position_enabled)
        except Exception:  # noqa: BLE001
            pass

        cover = await _read_cover(props, (title, artist, app))
        return {"active": True, "playing": playing, "title": title,
                "artist": artist, "app": app, "pos": pos, "dur": dur,
                "can_seek": can_seek, "stamp": time.time(), "cover": cover}

    return asyncio.run(go())


def seek_media(seconds: float) -> None:
    if not MEDIA_OK:
        raise RuntimeError(HINT_MEDIA)

    async def go() -> None:
        manager = await _Manager.request_async()
        session = manager.get_current_session()
        if session is None:
            raise RuntimeError("Нет активного плеера.")
        ok = await session.try_change_playback_position_async(
            int(max(0.0, seconds) * 10_000_000))
        if not ok:
            raise RuntimeError("Плеер не поддерживает перемотку.")

    asyncio.run(go())
from __future__ import annotations

import os
import time
from datetime import datetime

from PySide6.QtCore import QPointF, QRectF, Qt, QTimer
from PySide6.QtGui import QColor, QFontMetricsF, QGuiApplication, QPen

import theme
import ui
import winapi
import winrt_api
from bg import run_bg
from store import (LINKS_PATH, fetch_weather, read_links, weather_description,
                   write_links)

TIMER_LABEL = "Таймер 25 мин"
TIMER_SECONDS = 25 * 60

LIST_H = 252          # высота списка буфера: ровно 5 строк
ROW_H = 44
GAP = 8
STEP = ROW_H + GAP
MAX_ITEMS = 30
PRIVATE_FORMAT = "ExcludeClipboardContentFromMonitorProcessing"

DIM_MIN, DIM_MAX = 0.2, 0.9        # диапазон затухания над видео
DELAY_MIN, DELAY_MAX = 0.5, 8.0    # диапазон задержки автосворачивания, секунды
WEATHER_TTL = 1800                 # погода считается свежей 30 минут
MAX_APPS = 4                       # сколько приложений показывать в «Звуке»
MAX_DEVICES = 4


def ago(ts: float | None) -> str:
    if ts is None:
        return ""
    s = int(time.time() - ts)
    if s < 10:
        return "сейчас"
    if s < 60:
        return f"{s} с назад"
    if s < 3600:
        return f"{s // 60} мин назад"
    if s < 86400:
        return f"{s // 3600} ч назад"
    return f"{s // 86400} д назад"


def until(ts: float) -> str:
    secs = ts - time.time()
    if secs < 60:
        return "меньше минуты"
    minutes = int(secs // 60)
    if minutes < 60:
        return f"через {minutes} мин"
    hours, mins = divmod(minutes, 60)
    if hours < 24:
        return f"через {hours} ч {mins} мин" if mins else f"через {hours} ч"
    return f"через {hours // 24} д"


class Section:
    key = ""
    title = ""

    def paint(self, isl, p, rect: QRectF) -> None: ...

    def click(self, isl, key: str) -> None: ...

    def attach(self, isl) -> None:
        """Один раз при старте, когда остров уже создан."""

    def enter(self, isl) -> None:
        """Вызывается при переходе в раздел."""

    def tick(self, isl) -> None:
        """Раз в секунду, пока раздел открыт."""

    def wheel(self, isl, notches: float) -> None:
        """Колёсико над островом (положительное значение = вверх)."""

    def drag(self, isl, key: str, pos) -> None:
        """Мышь двигается с зажатой кнопкой над ползунком key."""

    def drag_end(self, isl, key: str) -> None:
        """Кнопку отпустили."""


def note(isl, p, rect: QRectF, text: str, color=theme.MUTED, size: float = 10) -> None:
    ui.label(isl, p, text, rect, size, color, wrap=True,
             align=Qt.AlignLeft | Qt.AlignTop)


# ------------------------------------------------------------------ Обзор

class Home(Section):
    key, title = "home", "Обзор"

    # ---------- жизненный цикл ----------

    def enter(self, isl) -> None:
        BY_KEY["weather"].ensure(isl)
        BY_KEY["settings"].enter(isl)        # Wi-Fi, Bluetooth, яркость, тема
        BY_KEY["sound"].refresh_volume(isl)

    def tick(self, isl) -> None:
        sett, sound = BY_KEY["settings"], BY_KEY["sound"]
        BY_KEY["weather"].ensure(isl)
        sett.tick(isl)
        if not sound.dragging:               # не мешаем, пока тянут ползунок
            sound.refresh_volume(isl)
        dark = winapi.get_dark_theme()       # тему могли сменить снаружи
        if dark != sett.dark:
            sett.dark = dark
            isl.nsp("sys:dark").target = 1.0 if dark else 0.0
            isl.kick()

    # ---------- таймер ----------

    @staticmethod
    def _timer_due(isl) -> float | None:
        for item in isl.reminders:
            if item["label"] == TIMER_LABEL:
                return item["due"]
        return None

    def _toggle_timer(self, isl) -> None:
        if self._timer_due(isl) is not None:     # второй клик отменяет таймер
            isl.state["reminders"] = [
                r for r in isl.reminders if r["label"] != TIMER_LABEL]
        else:
            isl.reminders.append(
                {"label": TIMER_LABEL, "due": time.time() + TIMER_SECONDS})
            isl.reminders.sort(key=lambda r: r["due"])
        isl.save_state()

    # ---------- отрисовка ----------

    def paint(self, isl, p, rect: QRectF) -> None:
        sett, sound = BY_KEY["settings"], BY_KEY["sound"]
        gap = 12
        x0, y0 = rect.left(), rect.top()

        def radio(kind: str):
            state = sett.states[kind]
            if state is None:
                return False, ("…" if sett.busy else "Нет"), False
            return state, ("Вкл" if state else "Выкл"), True

        awake = isl.state["keep_awake"]
        due = self._timer_due(isl)
        timer_status = ("25 мин" if due is None
                        else f"ещё {int((due - time.time()) // 60) + 1} мин")
        tiles = (
            ("WiFi", "Wi-Fi", *radio("WiFi")),
            ("Bluetooth", "Bluetooth", *radio("Bluetooth")),
            ("dark", "Тёмная тема", sett.dark, "Вкл" if sett.dark else "Выкл", True),
            ("awake", "Не засыпать", awake, "Вкл" if awake else "Выкл", True),
            ("mute", "Без звука", sound.muted, "Вкл" if sound.muted else "Выкл", True),
            ("timer", "Таймер", due is not None, timer_status, True),
        )
        tw = (rect.width() - 5 * gap) / 6
        for i, (key, text, on, status, enabled) in enumerate(tiles):
            self._tile(isl, p, f"tog:{key}", text, status, on, enabled,
                       QRectF(x0 + i * (tw + gap), y0, tw, 64))

        # два ползунка
        sw = (rect.width() - gap) / 2
        level = ui.clamp(isl.nsp("volume").x)
        self._slider_card(
            isl, p, QRectF(x0, y0 + 76, sw, 56), "Громкость", "slider:volume",
            sound.tracks, level,
            "Без звука" if sound.muted else f"{round(level * 100)}%",
            ui.trim(sound.error, 34) if sound.error else None)
        bright = ui.clamp(isl.nsp("brightness").x)
        br_error = (ui.trim(sett.br_error, 34)
                    if sett.br_error and not sett.br_known else None)
        self._slider_card(
            isl, p, QRectF(x0 + sw + gap, y0 + 76, sw, 56), "Яркость",
            "slider:brightness", sett.tracks, bright, f"{round(bright * 100)}%",
            br_error)

        # три карточки
        top = y0 + 144
        cw = (rect.width() - 2 * gap) / 3
        self._reminder_card(isl, p, QRectF(x0, top, cw, 150))
        self._media_card(isl, p, QRectF(x0 + cw + gap, top, cw, 150))
        self._weather_card(isl, p, QRectF(x0 + 2 * (cw + gap), top, cw, 150))

    def _tile(self, isl, p, key: str, text: str, status: str, on: bool,
              enabled: bool, r: QRectF) -> None:
        want = 1.0 if on else 0.0
        sp = isl.nsp(f"home:{key}", want)        # при создании сразу в нужном состоянии
        if sp.target != want:
            sp.target = want
            isl._start_frames()
        v = ui.clamp(sp.x)
        hot = isl.hv(key)
        with ui.pressable(isl, p, key, r):
            off = ui.mix(theme.SURFACE, theme.SURFACE_2, hot)
            onc = ui.mix(theme.ACCENT, theme.ACCENT_HOT, hot)
            ui.card(p, r, ui.mix(off, onc, v), 22)
            ui.label(isl, p, text,
                     QRectF(r.left() + 16, r.top() + 10, r.width() - 28, 22), 12,
                     theme.TEXT if enabled else theme.MUTED, True)
            ui.label(isl, p, status,
                     QRectF(r.left() + 16, r.top() + 34, r.width() - 28, 18), 10,
                     ui.mix(theme.MUTED, theme.TEXT, 0.85 * v))
        isl.add_hit(key, r)

    def _slider_card(self, isl, p, r: QRectF, title: str, key: str,
                     tracks: dict, level: float, value: str,
                     error: str | None) -> None:
        ui.card(p, r, theme.SURFACE, 22)
        ui.label(isl, p, title, QRectF(r.left() + 20, r.top() + 8, 160, 20),
                 11, theme.MUTED, True)
        right = QRectF(r.right() - 20 - 200, r.top() + 8, 200, 20)
        if error:
            ui.label(isl, p, error, right, 10, theme.RED,
                     align=Qt.AlignRight | Qt.AlignVCenter)
        else:
            ui.label(isl, p, value, right, 11, theme.ACCENT_HOT, True,
                     Qt.AlignRight | Qt.AlignVCenter)
        track = QRectF(r.left() + 28, r.top() + 38, r.width() - 56, 6)
        tracks[key] = track          # ползунок на главной управляется кодом раздела
        ui.slider(isl, p, key, track, level, error is None)

    def _reminder_card(self, isl, p, r: QRectF) -> None:
        with ui.panel(isl, p, "go:calendar", r, 24):
            ui.label(isl, p, "Ближайшее напоминание",
                     QRectF(r.left() + 20, r.top() + 16, r.width() - 40, 20),
                     11, theme.MUTED, True)
            items = isl.reminders
            if not items:
                ui.label(isl, p, "Нет напоминаний",
                         QRectF(r.left() + 20, r.top() + 50, r.width() - 40, 28),
                         15, bold=True)
                ui.label(isl, p, "Добавьте в календаре",
                         QRectF(r.left() + 20, r.top() + 82, r.width() - 40, 20),
                         11, theme.MUTED)
                return
            first = items[0]
            ui.label(isl, p, ui.trim(first["label"], 42),
                     QRectF(r.left() + 20, r.top() + 46, r.width() - 40, 52),
                     16, bold=True, wrap=True, align=Qt.AlignLeft | Qt.AlignTop)
            due = datetime.fromtimestamp(first["due"])
            when = due.strftime("%H:%M" if due.date() == datetime.now().date()
                                else "%d.%m %H:%M")
            ui.label(isl, p, f"в {when}  ·  {until(first['due'])}",
                     QRectF(r.left() + 20, r.top() + 104, r.width() - 40, 20),
                     11, theme.ACCENT_HOT)
            if len(items) > 1:
                ui.label(isl, p, f"и ещё {len(items) - 1}",
                         QRectF(r.left() + 20, r.top() + 126, r.width() - 40, 16),
                         10, theme.MUTED)

    def _media_card(self, isl, p, r: QRectF) -> None:
        m = isl.media
        with ui.panel(isl, p, "go:media", r, 24):
            ui.label(isl, p, "Сейчас играет",
                     QRectF(r.left() + 20, r.top() + 16, r.width() - 40, 20),
                     11, theme.MUTED, True)
            if not m["title"]:
                ui.label(isl, p, "Ничего не играет",
                         QRectF(r.left() + 20, r.top() + 50, r.width() - 40, 28),
                         15, bold=True)
                ui.label(isl, p, "Запустите плеер",
                         QRectF(r.left() + 20, r.top() + 82, r.width() - 40, 20),
                         11, theme.MUTED)
                return
            ui.cover(isl, p, QRectF(r.left() + 20, r.top() + 46, 64, 64), 14)
            tx, tw = r.left() + 98, r.width() - 118
            ui.label(isl, p, m["title"], QRectF(tx, r.top() + 48, tw, 22), 13, bold=True)
            ui.label(isl, p, m["artist"], QRectF(tx, r.top() + 72, tw, 18),
                     11, theme.MUTED)
            ui.label(isl, p, m["app"], QRectF(tx, r.top() + 92, tw, 16),
                     10, theme.ACCENT_HOT)
            dur = m["dur"]
            frac = isl.media_pos() / dur if dur > 0 else 0.0
            ui.bar(p, QRectF(r.left() + 20, r.top() + 128, r.width() - 40, 5),
                   frac, theme.ACCENT)

    def _weather_card(self, isl, p, r: QRectF) -> None:
        w = BY_KEY["weather"]
        with ui.panel(isl, p, "go:weather", r, 24):
            ui.label(isl, p, "Погода",
                     QRectF(r.left() + 20, r.top() + 16, r.width() - 40, 20),
                     11, theme.MUTED, True)
            if w.data is not None:
                d = w.data
                ui.label(isl, p, f"{round(d['temperature_2m'])}°",
                         QRectF(r.left() + 20, r.top() + 38, r.width() - 40, 54),
                         38, bold=True)
                ui.label(isl, p, weather_description(int(d.get("weather_code", -1))),
                         QRectF(r.left() + 20, r.top() + 94, r.width() - 40, 20),
                         13, theme.ACCENT_HOT, True)
                ui.label(isl, p,
                         f"{w.city(isl)}  ·  ощущается как {round(d['apparent_temperature'])}°",
                         QRectF(r.left() + 20, r.top() + 120, r.width() - 40, 18),
                         10, theme.MUTED)
            elif w.loading:
                ui.label(isl, p, "Загружаю…",
                         QRectF(r.left() + 20, r.top() + 54, r.width() - 40, 28),
                         14, theme.MUTED)
            elif w.error:
                note(isl, p, QRectF(r.left() + 20, r.top() + 46, r.width() - 40, 80),
                     ui.trim(w.error, 90), theme.RED, 11)
            else:
                ui.label(isl, p, "Выберите город",
                         QRectF(r.left() + 20, r.top() + 50, r.width() - 40, 28),
                         15, bold=True)
                ui.label(isl, p, "Раздел «Погода»",
                         QRectF(r.left() + 20, r.top() + 82, r.width() - 40, 20),
                         11, theme.MUTED)

    # ---------- события ----------

    def drag(self, isl, key: str, pos) -> None:
        if key == "slider:volume":
            BY_KEY["sound"].drag(isl, key, pos)
        elif key == "slider:brightness":
            BY_KEY["settings"].drag(isl, key, pos)

    def drag_end(self, isl, key: str) -> None:
        if key == "slider:volume":
            BY_KEY["sound"].drag_end(isl, key)
        elif key == "slider:brightness":
            BY_KEY["settings"].drag_end(isl, key)

    def click(self, isl, key: str) -> None:
        if key.startswith("go:"):
            isl.select(key.split(":", 1)[1])
        elif key.startswith("tog:"):
            name = key.split(":", 1)[1]
            sett, sound = BY_KEY["settings"], BY_KEY["sound"]
            if name in ("WiFi", "Bluetooth"):
                sett.toggle(isl, name)
            elif name == "dark":
                sett.toggle_dark(isl)
            elif name == "awake":
                isl.set_keep_awake(not isl.state["keep_awake"])
            elif name == "mute":
                sound.click(isl, "vol:mute")
            elif name == "timer":
                self._toggle_timer(isl)


# ------------------------------------------------------------------ Звук

class Sound(Section):
    key, title = "sound", "Звук"

    def __init__(self) -> None:
        self.muted = False
        self.error: str | None = None
        self.dev_error: str | None = None
        self.app_error: str | None = None
        self.devices: list[dict] = []
        self.default_id: str | None = None
        self.apps: list[dict] = []
        self.dragging = False
        self.tracks: dict[str, QRectF] = {}

    def enter(self, isl) -> None:
        self.refresh(isl)

    def tick(self, isl) -> None:
        if not self.dragging:       # не мешаем, пока человек тянет ползунок
            self.refresh(isl)

    def refresh_volume(self, isl) -> None:
        try:
            level, self.muted = winapi.get_volume()
        except Exception as error:  # noqa: BLE001 — показываем причину в интерфейсе
            self.error = str(error)
            return
        self.error = None
        isl.nsp("volume").target = level      # пружина плавно доедет до значения
        isl.kick()

    def refresh(self, isl) -> None:
        self.refresh_volume(isl)
        if self.error:
            return
        try:
            self.devices, self.default_id = winapi.list_output_devices()
            self.dev_error = None
        except Exception as error:  # noqa: BLE001
            self.dev_error = str(error)
        try:
            self.apps = winapi.list_app_volumes()
            self.app_error = None
        except Exception as error:  # noqa: BLE001
            self.app_error = str(error)
        isl.kick()

    def paint(self, isl, p, rect: QRectF) -> None:
        self._paint_master(isl, p, QRectF(rect.left(), rect.top(), 380, rect.height()))
        self._paint_apps(isl, p, QRectF(rect.left() + 396, rect.top(),
                                        rect.width() - 396, rect.height()))

    def _paint_master(self, isl, p, box: QRectF) -> None:
        ui.card(p, box, theme.SURFACE, 26)
        level = ui.clamp(isl.nsp("volume").x)

        ui.label(isl, p, "Громкость",
                 QRectF(box.left() + 24, box.top() + 16, 120, 32), 15, bold=True)
        value = "Без звука" if self.muted else f"{round(level * 100)}%"
        ui.label(isl, p, value, QRectF(box.left() + 138, box.top() + 16, 100, 32),
                 15, theme.MUTED if self.muted else theme.ACCENT_HOT, True)
        ui.button(isl, p, "vol:mute",
                  QRectF(box.right() - 24 - 108, box.top() + 16, 108, 32),
                  "Включить" if self.muted else "Без звука", size=11)

        track = QRectF(box.left() + 34, box.top() + 78, box.width() - 68, 8)
        self.tracks["slider:volume"] = track
        ui.slider(isl, p, "slider:volume", track, level, not self.error)

        if self.error:
            note(isl, p, QRectF(box.left() + 24, box.top() + 108, box.width() - 48, 80),
                 self.error, theme.RED, 11)
            return

        ui.label(isl, p, "УСТРОЙСТВО ВЫВОДА",
                 QRectF(box.left() + 24, box.top() + 106, 300, 16), 10, theme.MUTED, True)
        if self.dev_error:
            note(isl, p, QRectF(box.left() + 24, box.top() + 130, box.width() - 48, 60),
                 self.dev_error, theme.RED, 11)
            return
        for i, dev in enumerate(self.devices[:MAX_DEVICES]):
            r = QRectF(box.left() + 16, box.top() + 130 + i * 44, box.width() - 32, 38)
            selected = dev["id"] == self.default_id
            with ui.panel(isl, p, f"dev:{i}", r, 19):
                ui.label(isl, p, dev["name"],
                         QRectF(r.left() + 18, r.top(), r.width() - 56, r.height()),
                         11, theme.TEXT if selected else theme.MUTED, selected)
                if selected:
                    p.setPen(Qt.NoPen)
                    p.setBrush(theme.ACCENT)
                    p.drawEllipse(QPointF(r.right() - 22, r.center().y()), 5, 5)

    def _paint_apps(self, isl, p, box: QRectF) -> None:
        ui.card(p, box, theme.SURFACE, 26)
        ui.label(isl, p, "Приложения",
                 QRectF(box.left() + 24, box.top() + 16, 240, 32), 15, bold=True)
        if self.app_error:
            note(isl, p, QRectF(box.left() + 24, box.top() + 60, box.width() - 48, 80),
                 self.app_error, theme.RED, 11)
            return
        if not self.apps:
            ui.label(isl, p, "Нет приложений со звуком",
                     QRectF(box.left(), box.top() + 60, box.width(), 200),
                     12, theme.MUTED, align=Qt.AlignCenter)
            return
        for i, app in enumerate(self.apps[:MAX_APPS]):
            y = box.top() + 62 + i * 58
            ui.label(isl, p, app["name"],
                     QRectF(box.left() + 24, y, box.width() - 110, 22), 12, bold=True)
            ui.label(isl, p, f"{round(app['level'] * 100)}%",
                     QRectF(box.right() - 24 - 56, y, 56, 22), 11, theme.MUTED,
                     align=Qt.AlignRight | Qt.AlignVCenter)
            key = f"slider:app:{i}"
            track = QRectF(box.left() + 34, y + 36, box.width() - 68, 6)
            self.tracks[key] = track
            ui.slider(isl, p, key, track, app["level"])
        if len(self.apps) > MAX_APPS:
            ui.label(isl, p, f"Показаны первые {MAX_APPS} из {len(self.apps)}",
                     QRectF(box.left() + 24, box.bottom() - 28, box.width() - 48, 18),
                     10, theme.MUTED)

    def drag(self, isl, key: str, pos) -> None:
        track = self.tracks.get(key)
        if track is None or track.width() <= 0:
            return
        self.dragging = True
        f = ui.clamp((pos.x() - track.left()) / track.width())
        if key == "slider:volume":
            sp = isl.nsp("volume")
            sp.x = sp.target = f            # без пружины: ползунок идёт точно за мышью
            sp.v = 0.0
            try:
                if self.muted:
                    winapi.set_mute(False)
                    self.muted = False
                winapi.set_volume(f)
                self.error = None
            except Exception as error:  # noqa: BLE001
                if self.error is None:
                    isl.alert(f"Не удалось изменить громкость:\n{error}")
                self.error = str(error)
        elif key.startswith("slider:app:"):
            try:
                app = self.apps[int(key.rsplit(":", 1)[1])]
            except (ValueError, IndexError):
                return
            app["level"] = f
            try:
                winapi.set_app_volume(app["vols"], f)
                self.app_error = None
            except Exception as error:  # noqa: BLE001 — сессия могла закрыться
                self.app_error = str(error)
        isl.kick()

    def drag_end(self, isl, key: str) -> None:
        self.dragging = False

    def click(self, isl, key: str) -> None:
        if key == "vol:mute":
            try:
                winapi.set_mute(not self.muted)
                self.muted = not self.muted
            except Exception as error:  # noqa: BLE001
                isl.alert(f"Не удалось переключить звук:\n{error}")
        elif key.startswith("dev:"):
            try:
                device = self.devices[int(key.split(":", 1)[1])]
            except (ValueError, IndexError):
                return
            if device["id"] != self.default_id:
                try:
                    winapi.set_default_device(device["id"])
                    self.default_id = device["id"]
                    self.refresh(isl)
                except Exception as error:  # noqa: BLE001
                    isl.alert(f"Не удалось переключить устройство:\n{error}")
        isl.kick()


# ------------------------------------------------------------------ Медиа

class Media(Section):
    key, title = "media", "Медиа"

    def __init__(self) -> None:
        self.track = QRectF()
        self.dragging = False
        self.seek_frac = 0.0

    def enter(self, isl) -> None:
        isl.refresh_media()

    def paint(self, isl, p, rect: QRectF) -> None:
        box = QRectF(rect.left(), rect.top(), rect.width(), 262)
        ui.card(p, box, theme.SURFACE, 26)
        m = isl.media

        ui.cover(isl, p, QRectF(box.left() + 28, box.top() + 28, 150, 150), 22)

        x0 = box.left() + 206
        wcol = box.width() - 206 - 28
        ui.label(isl, p, m["title"] or "Ничего не играет",
                 QRectF(x0, box.top() + 28, wcol, 32), 19, bold=True)
        if m["title"]:
            sub = m["artist"]
        elif winrt_api.MEDIA_OK:
            sub = "Запустите плеер"
        else:
            sub = "Название трека недоступно: не установлен winrt"
        ui.label(isl, p, sub, QRectF(x0, box.top() + 62, wcol, 22), 13, theme.MUTED)

        if m["app"]:
            fm = QFontMetricsF(isl._font(10, True))
            pill = QRectF(x0, box.top() + 96, min(fm.horizontalAdvance(m["app"]) + 26, wcol), 24)
            ui.card(p, pill, theme.SURFACE_2, 12)
            ui.label(isl, p, m["app"], pill, 10, theme.MUTED, True, Qt.AlignCenter)

        # перемотка
        dur = m["dur"]
        can_seek = m["can_seek"] and dur > 0
        pos = isl.media_pos()
        frac = self.seek_frac if self.dragging else (pos / dur if dur > 0 else 0.0)
        track = QRectF(x0, box.top() + 146, wcol, 8)
        self.track = track
        if can_seek:
            ui.slider(isl, p, "slider:seek", track, frac)
        else:
            ui.bar(p, track, frac, theme.MUTED)
        shown = frac * dur if self.dragging else pos
        ui.label(isl, p, ui.fmt_time(shown) if m["title"] else "",
                 QRectF(x0, box.top() + 160, 80, 18), 10, theme.MUTED)
        ui.label(isl, p, ui.fmt_time(dur) if dur > 0 else "",
                 QRectF(x0 + wcol - 80, box.top() + 160, 80, 18), 10, theme.MUTED,
                 align=Qt.AlignRight | Qt.AlignVCenter)

        # кнопки
        cx, cy = box.center().x(), box.top() + 214
        v = ui.clamp(isl.nsp("play").x)
        with ui.round_button(isl, p, "media:prev", QPointF(cx - 90, cy), 24):
            ui.media_icon(p, "prev", QPointF(cx - 90, cy), 10)
        with ui.round_button(isl, p, "media:toggle", QPointF(cx, cy), 30, True):
            ui.media_icon(p, "play", QPointF(cx, cy), 13, 1 - v)
            ui.media_icon(p, "pause", QPointF(cx, cy), 13, v)
        with ui.round_button(isl, p, "media:next", QPointF(cx + 90, cy), 24):
            ui.media_icon(p, "next", QPointF(cx + 90, cy), 10)

        hint = "Команды идут активному плееру через мультимедийные клавиши."
        if m["active"] and dur > 0 and not m["can_seek"]:
            hint = "Этот плеер не поддерживает перемотку."
        note(isl, p, QRectF(rect.left() + 6, box.bottom() + 10, rect.width() - 12, 20), hint)

    def drag(self, isl, key: str, pos) -> None:
        if key != "slider:seek" or self.track.width() <= 0:
            return
        self.dragging = True
        self.seek_frac = ui.clamp((pos.x() - self.track.left()) / self.track.width())
        isl.kick()

    def drag_end(self, isl, key: str) -> None:
        if key != "slider:seek":
            return
        self.dragging = False
        target = self.seek_frac * isl.media["dur"]
        isl.media["pos"] = target                  # сразу показываем новую позицию
        isl.media["stamp"] = time.time()

        def done(_res, err) -> None:
            if err is not None:
                isl.alert(f"Не удалось перемотать:\n{err}")
            isl.refresh_media(delay_ms=500)

        run_bg(lambda: winrt_api.seek_media(target), done)
        isl.kick()

    def click(self, isl, key: str) -> None:
        action = key.split(":", 1)[1]
        if action == "prev":
            winapi.media_key("previous")
        elif action == "next":
            winapi.media_key("next")
        elif action == "toggle":
            winapi.media_key("play_pause")
            isl.set_playing(not isl.media["playing"])
        isl.refresh_media(delay_ms=900)


# ------------------------------------------------------------------ Система

class System(Section):
    key, title = "system", "Система"

    def paint(self, isl, p, rect: QRectF) -> None:
        snap = isl.snapshot
        box = QRectF(rect.left(), rect.top(), rect.width(), rect.height())
        ui.card(p, box, theme.SURFACE, 26)
        if snap is None:
            ui.label(isl, p, "Собираю системные данные…",
                     QRectF(box.left() + 28, box.top() + 22, 400, 24), 12, theme.MUTED)
            return
        pct, charging = isl.battery
        rows = [
            ("Процессор", snap["cpu"], snap["cpu_f"]),
            ("Оперативная память",
             f"{snap['memory']}  ·  {snap['memory_percent']}", snap["mem_f"]),
            ("Системный диск",
             f"{snap['disk']}  ·  {snap['disk_percent']}", snap["disk_f"]),
        ]
        if pct is not None:
            rows.append(("Батарея" + ("  ·  заряжается" if charging else ""),
                         f"{pct}%", pct / 100))
        for i, (name, value, frac) in enumerate(rows):
            y = box.top() + 22 + i * 70
            ui.label(isl, p, name, QRectF(box.left() + 28, y, 300, 20),
                     12, theme.MUTED, True)
            ui.label(isl, p, value,
                     QRectF(box.right() - 28 - 320, y, 320, 22), 13, bold=True,
                     align=Qt.AlignRight | Qt.AlignVCenter)
            color = theme.YELLOW if name.startswith("Батарея") else theme.ACCENT
            ui.bar(p, QRectF(box.left() + 28, y + 30, box.width() - 56, 6), frac, color)


# ------------------------------------------------------------------ Настройки

class Settings(Section):
    key, title = "settings", "Настройки"

    def __init__(self) -> None:
        self.tracks: dict[str, QRectF] = {}
        self.dragging = False
        self.states: dict[str, bool | None] = {"WiFi": None, "Bluetooth": None}
        self.busy = False
        self.error: str | None = None
        self.next_refresh = 0.0
        self.br_known = False
        self.br_busy = False
        self.br_pending: int | None = None
        self.br_error: str | None = None
        self.br_next = 0.0
        self.dark = False

    def attach(self, isl) -> None:
        self.dark = winapi.get_dark_theme()
        sp = isl.nsp("sys:dark")
        sp.x = sp.target = 1.0 if self.dark else 0.0

    def enter(self, isl) -> None:
        self.dark = winapi.get_dark_theme()
        isl.nsp("sys:dark").target = 1.0 if self.dark else 0.0
        self.refresh(isl)
        self.refresh_brightness(isl)

    def tick(self, isl) -> None:
        if not self.busy and time.perf_counter() >= self.next_refresh:
            self.refresh(isl)
        if time.perf_counter() >= self.br_next:
            self.refresh_brightness(isl)

    def refresh(self, isl, toggle: tuple[str, bool] | None = None) -> None:
        if self.busy:
            return
        self.busy = True
        self.next_refresh = time.perf_counter() + 30

        def job():
            if toggle:
                winrt_api.set_radio(*toggle)
            return winrt_api.read_radios()

        run_bg(job, lambda res, err: self._done(isl, toggle, res, err))
        isl.kick()

    def _done(self, isl, toggle, res, err) -> None:
        self.busy = False
        if err is not None:
            self.error = str(err)
            if toggle:
                self.states[toggle[0]] = not toggle[1]
                isl.alert(f"Не удалось переключить {toggle[0]}:\n{err}")
        else:
            self.error = None
            self.states.update(res)
        for kind, state in self.states.items():
            isl.nsp(f"radio:{kind}").target = 1.0 if state else 0.0
        isl.kick()

    def toggle(self, isl, kind: str) -> None:
        if self.busy:
            return
        state = self.states.get(kind)
        if state is None:
            self.refresh(isl)
            return
        self.states[kind] = not state
        isl.nsp(f"radio:{kind}").target = 0.0 if state else 1.0
        isl.kick()
        self.refresh(isl, (kind, not state))

    def refresh_brightness(self, isl) -> None:
        if self.br_busy or self.dragging:
            return
        self.br_busy = True
        self.br_next = time.perf_counter() + 15

        def done(res, err) -> None:
            self.br_busy = False
            if err is not None:
                self.br_error = str(err)
                self.br_next = time.perf_counter() + 60
            else:
                self.br_error = None
                self.br_known = True
                if not self.dragging:
                    isl.nsp("brightness").target = res / 100
            if self.br_pending is not None:
                self._push_brightness(isl)
            isl.kick()

        run_bg(winapi.get_brightness, done)

    def _push_brightness(self, isl) -> None:
        if self.br_busy or self.br_pending is None:
            return
        value, self.br_pending = self.br_pending, None
        self.br_busy = True

        def done(_res, err) -> None:
            self.br_busy = False
            if err is not None:
                if self.br_error is None:
                    isl.alert(f"Не удалось изменить яркость:\n{err}")
                self.br_error = str(err)
            else:
                self.br_error = None
            if self.br_pending is not None:
                self._push_brightness(isl)
            isl.kick()

        run_bg(lambda: winapi.set_brightness(value), done)

    def toggle_dark(self, isl) -> None:
        want = not self.dark
        try:
            winapi.set_dark_theme(want)
        except OSError as error:
            isl.alert(f"Не удалось сменить тему:\n{error}")
            return
        self.dark = want
        isl.nsp("sys:dark").target = 1.0 if want else 0.0
        run_bg(winapi.broadcast_theme_change, lambda _r, _e: None)
        isl.kick()

    def paint(self, isl, p, rect: QRectF) -> None:
        self._paint_island(isl, p, rect)

    def _slider_card(self, isl, p, r: QRectF, title: str, toggle_key: str,
                     slider_key: str, enabled: bool, value_text: str,
                     frac: float) -> None:
        hot = isl.hv(toggle_key)
        ui.card(p, r, ui.mix(theme.SURFACE, theme.SURFACE_2, hot), 24)
        ui.label(isl, p, title, QRectF(r.left() + 20, r.top() + 10, r.width() - 110, 28),
                 13, bold=True)
        v = isl.nsp(toggle_key, 1.0 if enabled else 0.0).x
        ui.switch(p, QRectF(r.right() - 20 - 44, r.top() + 12, 44, 26), v)
        isl.add_hit(toggle_key, QRectF(r.left(), r.top(), r.width(), 48))
        ui.label(isl, p, value_text, QRectF(r.left() + 20, r.top() + 44, 220, 20),
                 11, theme.MUTED)
        track = QRectF(r.left() + 20, r.top() + 76, r.width() - 40, 8)
        self.tracks[slider_key] = track
        ui.slider(isl, p, slider_key, track, frac, enabled)

    def _paint_island(self, isl, p, body: QRectF) -> None:
        # акцентный цвет
        row = QRectF(body.left(), body.top(), body.width(), 52)
        ui.card(p, row, theme.SURFACE, 26)
        ui.label(isl, p, "Акцентный цвет",
                 QRectF(row.left() + 28, row.top(), 300, row.height()), 13, bold=True)
        n, r, gap = len(theme.ACCENTS), 11, 14
        x0 = row.right() - 28 - (n * 2 * r + (n - 1) * gap)
        cy = row.center().y()
        for i, (key, main, _hot) in enumerate(theme.ACCENTS):
            cx = x0 + r + i * (2 * r + gap)
            k = f"accent:{key}"
            rect = QRectF(cx - r - 4, cy - r - 4, 2 * r + 8, 2 * r + 8)
            with ui.pressable(isl, p, k, rect):
                if theme.current_accent == key:
                    p.setPen(QPen(theme.TEXT, 2))
                    p.setBrush(Qt.NoBrush)
                    p.drawEllipse(QPointF(cx, cy), r + 4, r + 4)
                p.setPen(Qt.NoPen)
                p.setBrush(QColor(main))
                rr = r * (1 + 0.12 * isl.hv(k))
                p.drawEllipse(QPointF(cx, cy), rr, rr)
            isl.add_hit(k, rect)

        # затухание над видео и автосворачивание
        cw = (body.width() - 12) / 2
        level = isl.opt("dim_level")
        self._slider_card(
            isl, p, QRectF(body.left(), body.top() + 60, cw, 100),
            "Бледнеть над видео", "opt:dim_enabled", "slider:dim",
            isl.opt("dim_enabled"), f"затухание {round(level * 100)}%",
            (level - DIM_MIN) / (DIM_MAX - DIM_MIN))
        delay = isl.opt("leave_delay")
        self._slider_card(
            isl, p, QRectF(body.left() + cw + 12, body.top() + 60, cw, 100),
            "Сворачивать при уходе мыши", "opt:leave_collapse", "slider:delay",
            isl.opt("leave_collapse"), f"через {delay:.1f} с",
            (delay - DELAY_MIN) / (DELAY_MAX - DELAY_MIN))

        # автозапуск
        box = QRectF(body.left(), body.top() + 168, body.width(), 60)
        with ui.panel(isl, p, "autostart", box, 24):
            ui.label(isl, p, "Запускать вместе с Windows",
                     QRectF(box.left() + 28, box.top() + 8, 400, 24), 13, bold=True)
            ui.label(isl, p, "Quick Notch появится после входа в систему",
                     QRectF(box.left() + 28, box.top() + 34, 400, 18), 10, theme.MUTED)
            ui.switch(p, QRectF(box.right() - 28 - 52, box.center().y() - 15, 52, 30),
                      isl.nsp("autostart").x)

    # ---------- события ----------

    def drag(self, isl, key: str, pos) -> None:
        track = self.tracks.get(key)
        if track is None or track.width() <= 0:
            return
        self.dragging = True
        f = ui.clamp((pos.x() - track.left()) / track.width())
        if key == "slider:brightness":
            sp = isl.nsp("brightness")
            sp.x = sp.target = f
            sp.v = 0.0
            self.br_pending = round(f * 100)
            self._push_brightness(isl)
        elif key == "slider:dim":
            isl.state["dim_level"] = round(DIM_MIN + (DIM_MAX - DIM_MIN) * f, 2)
        elif key == "slider:delay":
            isl.state["leave_delay"] = round(DELAY_MIN + (DELAY_MAX - DELAY_MIN) * f, 1)
        isl.kick()

    def drag_end(self, isl, key: str) -> None:
        self.dragging = False
        if key in ("slider:dim", "slider:delay"):
            isl.save_state()

    def click(self, isl, key: str) -> None:
        if key.startswith("accent:"):
            isl.set_accent(key.split(":", 1)[1])
        elif key.startswith("opt:"):
            isl.toggle_opt(key.split(":", 1)[1])
        elif key == "autostart":
            isl.toggle_autostart()


# ------------------------------------------------------------------ Ссылки

class Links(Section):
    key, title = "links", "Ссылки"

    def __init__(self) -> None:
        try:
            self.links = read_links(LINKS_PATH)
            self.error: str | None = None
        except ValueError as error:
            self.links, self.error = [], str(error)

    def paint(self, isl, p, rect: QRectF) -> None:
        h = rect.height()
        if self.error:
            note(isl, p, QRectF(rect.left() + 4, rect.top() + 6, rect.width() - 8, 60),
                 self.error, theme.RED)
        else:
            bw = (rect.width() - 12) / 2
            for i, item in enumerate(self.links[:8]):
                row, col = divmod(i, 2)
                r = QRectF(rect.left() + col * (bw + 12), rect.top() + row * 52, bw, 44)
                with ui.panel(isl, p, f"link:{i}", r, 22):
                    ui.label(isl, p, item["label"],
                             QRectF(r.left() + 20, r.top(), bw - 60, r.height()), 11)
                ui.close_dot(isl, p, f"linkdel:{i}", QPointF(r.right() - 26, r.center().y()))
            if not self.links:
                ui.label(isl, p, "Ссылок пока нет. Добавьте первую.",
                         QRectF(rect.left() + 6, rect.top() + 8, 400, 24), 12, theme.MUTED)
            if len(self.links) > 8:
                ui.label(isl, p, f"Показаны первые 8 из {len(self.links)}",
                         QRectF(rect.left() + 240, rect.top() + h - 28, 300, 20),
                         10, theme.MUTED)
        ui.button(isl, p, "link:add", QRectF(rect.left(), rect.top() + h - 40, 220, 36),
                  "+  Добавить ссылку", True)

    def click(self, isl, key: str) -> None:
        action, _, arg = key.partition(":")
        try:
            if key == "link:add":
                label = isl.ask("Быстрая ссылка", "Название ссылки:")
                if not label:
                    return
                target = isl.ask("Быстрая ссылка",
                                 "Адрес сайта, путь к папке или программе:")
                if not target:
                    return
                self.links.append({"label": label, "target": target})
                write_links(LINKS_PATH, self.links)
                self.error = None
            elif action == "link":
                os.startfile(self.links[int(arg)]["target"])
            elif action == "linkdel":
                del self.links[int(arg)]
                write_links(LINKS_PATH, self.links)
        except (OSError, ValueError, IndexError) as error:
            isl.alert(f"Не удалось выполнить действие:\n{error}")


# ------------------------------------------------------------------ Буфер

class Clipboard(Section):
    key, title = "clipboard", "Буфер"

    def __init__(self) -> None:
        self.items: list[dict] = []     # {"text", "time", "pinned"}
        self.own: str | None = None     # то, что мы сами положили в буфер
        self.flash_text: str | None = None
        self.flash_until = 0.0

    def attach(self, isl) -> None:
        for text in isl.state.get("clip_pinned", []):
            if isinstance(text, str) and text.strip():
                self.items.append({"text": text, "time": None, "pinned": True})
        QGuiApplication.clipboard().dataChanged.connect(
            lambda: QTimer.singleShot(40, lambda: self.capture(isl)))
        QTimer.singleShot(300, lambda: self.capture(isl))

    def capture(self, isl) -> None:
        board = QGuiApplication.clipboard()
        mime = board.mimeData()
        if mime is None or not mime.hasText():
            return
        if any(PRIVATE_FORMAT in fmt for fmt in mime.formats()):
            return                       # менеджер паролей просит не сохранять
        text = (board.text() or "").strip()[:2000]
        if not text or text == self.own:
            return
        self.own = None
        for item in self.items:
            if item["text"] == text:     # уже есть: поднимаем наверх
                if not item["pinned"]:
                    self.items.remove(item)
                    item["time"] = time.time()
                    self.items.insert(self._pinned_count(), item)
                break
        else:
            self.items.insert(self._pinned_count(),
                              {"text": text, "time": time.time(), "pinned": False})
            self._trim()
        isl.kick()

    def _pinned_count(self) -> int:
        return sum(1 for i in self.items if i["pinned"])

    def _trim(self) -> None:
        while len(self.items) > MAX_ITEMS:
            for idx in range(len(self.items) - 1, -1, -1):
                if not self.items[idx]["pinned"]:
                    del self.items[idx]
                    break
            else:
                return

    def _save(self, isl) -> None:
        isl.state["clip_pinned"] = [i["text"] for i in self.items if i["pinned"]]
        isl.save_state()

    def _max_scroll(self) -> float:
        return max(0.0, len(self.items) * STEP - GAP - LIST_H)

    def _clamp_scroll(self, isl) -> None:
        sp = isl.nsp("clip_scroll")
        sp.target = min(sp.target, self._max_scroll())

    def paint(self, isl, p, rect: QRectF) -> None:
        area = QRectF(rect.left(), rect.top(), rect.width(), LIST_H)
        scroll = isl.nsp("clip_scroll").x

        if not self.items:
            ui.card(p, QRectF(rect.left(), rect.top(), rect.width(), 120),
                    theme.SURFACE, 26)
            ui.label(isl, p, "Скопируйте текст, и он появится здесь.",
                     QRectF(rect.left(), rect.top(), rect.width(), 120),
                     12, theme.MUTED, align=Qt.AlignCenter)
        else:
            p.save()
            p.setClipRect(area, Qt.IntersectClip)   # не вылезаем за список
            for i, item in enumerate(self.items):
                y = area.top() + i * STEP - scroll
                if y + ROW_H < area.top() or y > area.bottom():
                    continue
                self._row(isl, p, i, item,
                          QRectF(area.left(), y, area.width(), ROW_H), area)
            self._scrollbar(p, area, scroll)
            p.restore()

        h = rect.height()
        note(isl, p, QRectF(rect.left() + 6, rect.top() + h - 30, rect.width() - 150, 28),
             "Закреплённые сохраняются на диске. Прокрутка: колёсико мыши.")
        ui.button(isl, p, "clip:clear",
                  QRectF(rect.right() - 120, rect.top() + h - 38, 120, 32),
                  "Очистить", size=10)

    def _row(self, isl, p, i: int, item: dict, r: QRectF, area: QRectF) -> None:
        key = f"clip:{i}"
        isl.add_hit(key, r.intersected(area))   # сначала строка, потом кнопки: они сверху
        flashing = (item["text"] == self.flash_text
                    and time.perf_counter() < self.flash_until)
        with ui.pressable(isl, p, key, r):
            fill = ui.mix(theme.SURFACE, theme.SURFACE_2, isl.hv(key))
            if flashing:
                fill = ui.mix(theme.SURFACE, theme.ACCENT, 0.4)
            ui.card(p, r, fill, 20)

            tx, tw = r.left() + 18, r.width() - 120
            if flashing:
                ui.label(isl, p, "Скопировано",
                         QRectF(tx, r.top(), tw, r.height()), 12, theme.TEXT, True)
            else:
                lines = item["text"].splitlines()
                ui.label(isl, p, lines[0].strip() or "…",
                         QRectF(tx, r.top() + 5, tw, 20), 12)
                parts = []
                if len(lines) > 1:
                    parts.append(f"{len(lines)} стр.")
                parts.append(f"{len(item['text'])} симв.")
                when = ago(item["time"])
                if when:
                    parts.append(when)
                ui.label(isl, p, "  ·  ".join(parts),
                         QRectF(tx, r.top() + 24, tw, 16), 10, theme.MUTED)

            c = r.center().y()
            pin_c = QPointF(r.right() - 66, c)
            with ui.round_button(isl, p, f"clip:pin:{i}", pin_c, 11, item["pinned"]):
                self._pin(p, pin_c, item["pinned"])
            ui.close_dot(isl, p, f"clip:del:{i}", QPointF(r.right() - 28, c))

    @staticmethod
    def _pin(p, c: QPointF, pinned: bool) -> None:
        col = theme.TEXT if pinned else theme.MUTED
        p.setPen(QPen(col, 1.6, Qt.SolidLine, Qt.RoundCap))
        p.drawLine(QPointF(c.x(), c.y() + 0.5), QPointF(c.x(), c.y() + 5))
        p.setPen(Qt.NoPen)
        p.setBrush(col)
        p.drawEllipse(QPointF(c.x(), c.y() - 2.5), 3.2, 3.2)

    def _scrollbar(self, p, area: QRectF, scroll: float) -> None:
        mx = self._max_scroll()
        if mx <= 0:
            return
        thumb = max(28.0, LIST_H * LIST_H / (LIST_H + mx))
        y = area.top() + (LIST_H - thumb) * ui.clamp(scroll / mx)
        ui.card(p, QRectF(area.right() - 6, y, 3, thumb),
                ui.mix(theme.SURFACE_3, theme.MUTED, 0.5), 1.5)

    def click(self, isl, key: str) -> None:
        parts = key.split(":")                  # clip:3 / clip:pin:3 / clip:clear
        if parts[1] == "clear":
            self.items = [i for i in self.items if i["pinned"]]
        else:
            try:
                item = self.items[int(parts[-1])]
            except (ValueError, IndexError):
                return
            action = parts[1] if len(parts) == 3 else "copy"
            if action == "copy":
                self._copy(isl, item)
            elif action == "pin":
                item["pinned"] = not item["pinned"]
                self.items.sort(key=lambda it: not it["pinned"])   # закреплённые наверх
                self._save(isl)
            elif action == "del":
                self.items.remove(item)
                if item["pinned"]:
                    self._save(isl)
        self._clamp_scroll(isl)
        isl.kick()

    def _copy(self, isl, item: dict) -> None:
        self.own = item["text"]                 # чтобы capture не переставил запись
        QGuiApplication.clipboard().setText(item["text"])
        self.flash_text = item["text"]
        self.flash_until = time.perf_counter() + 1.1
        QTimer.singleShot(1150, isl.kick)       # перерисовать, когда подсветка кончится

    def wheel(self, isl, notches: float) -> None:
        if not self.items:
            return
        sp = isl.nsp("clip_scroll")
        sp.target = ui.clamp(sp.target - notches * STEP, 0.0, self._max_scroll())
        isl.kick()


# ------------------------------------------------------------------ Календарь

class Calendar(Section):
    key, title = "calendar", "Календарь"

    def paint(self, isl, p, rect: QRectF) -> None:
        today = datetime.now()
        h = rect.height()
        left = QRectF(rect.left(), rect.top(), 250, h)
        ui.card(p, left, theme.SURFACE, 26)
        ui.label(isl, p, ui.WEEKDAYS[today.weekday()].upper(),
                 QRectF(left.left() + 26, left.top() + 24, 200, 20), 11, theme.RED, True)
        ui.label(isl, p, str(today.day),
                 QRectF(left.left() + 24, left.top() + 44, 200, 96), 72, bold=True)
        ui.label(isl, p, f"{ui.MONTHS_NOM[today.month - 1]} {today.year}",
                 QRectF(left.left() + 28, left.top() + 152, 200, 24), 14, theme.MUTED)

        rx = rect.left() + 266
        rw = rect.width() - 266
        right = QRectF(rx, rect.top(), rw, h)
        ui.card(p, right, theme.SURFACE, 26)
        ui.label(isl, p, "Напоминания", QRectF(rx + 24, rect.top() + 18, 200, 28),
                 14, bold=True)
        ui.button(isl, p, "rem:add", QRectF(rx + rw - 24 - 132, rect.top() + 12, 132, 36),
                  "+  Новое", True, 11)
        reminders = isl.reminders
        if not reminders:
            ui.label(isl, p, "Нет активных напоминаний",
                     QRectF(rx, rect.top() + 150, rw, 40), 12, theme.MUTED,
                     align=Qt.AlignCenter)
        for i, item in enumerate(reminders[:4]):
            y = rect.top() + 64 + i * 58
            due = datetime.fromtimestamp(item["due"])
            when = due.strftime("%H:%M" if due.date() == today.date() else "%d.%m %H:%M")
            ui.card(p, QRectF(rx + 16, y, rw - 32, 50), theme.SURFACE_2, 18)
            ui.label(isl, p, item["label"], QRectF(rx + 34, y + 6, rw - 100, 22),
                     12, bold=True)
            ui.label(isl, p, f"в {when}", QRectF(rx + 34, y + 27, rw - 100, 18),
                     10, theme.MUTED)
            ui.close_dot(isl, p, f"rem:del:{i}", QPointF(rx + rw - 40, y + 25))

    def click(self, isl, key: str) -> None:
        if key == "rem:add":
            isl.add_reminder()
        elif key.startswith("rem:del:"):
            isl.delete_reminder(int(key.rsplit(":", 1)[1]))


# ------------------------------------------------------------------ Погода

class Weather(Section):
    key, title = "weather", "Погода"

    def __init__(self) -> None:
        self.data: dict | None = None
        self.error: str | None = None
        self.loading = False
        self.loaded_at = 0.0

    @staticmethod
    def city(isl) -> str:
        city = isl.state.get("weather_city")
        return city.strip() if isinstance(city, str) and city.strip() else "Москва"

    def attach(self, isl) -> None:
        QTimer.singleShot(1500, lambda: self.ensure(isl))

    def ensure(self, isl) -> None:
        """Подгружает погоду, если город выбран, а данных нет или они старше 30 минут."""
        city = isl.state.get("weather_city")
        if not (isinstance(city, str) and city.strip()) or self.loading:
            return
        age = time.time() - self.loaded_at
        if self.data is None and self.error and age < 120:
            return                       # после ошибки не долбим сеть чаще раза в 2 минуты
        if self.data is None or age > WEATHER_TTL:
            self._load(isl, city.strip())

    def enter(self, isl) -> None:
        self.ensure(isl)

    def _load(self, isl, city: str) -> None:
        if isl.state.get("weather_city") != city:
            isl.state["weather_city"] = city
            isl.save_state()
        self.error = None
        self.loading = True
        self.loaded_at = time.time()

        def done(res, err) -> None:
            self.loading = False
            if err is not None:
                self.data = None
                self.error = str(err) or "Не удалось получить погоду."
            else:
                self.data = res
            isl.kick()

        run_bg(lambda: fetch_weather(city), done)
        isl.kick()

    def paint(self, isl, p, rect: QRectF) -> None:
        box = QRectF(rect.left(), rect.top(), rect.width(), rect.height())
        ui.card(p, box, theme.SURFACE, 26)
        ui.label(isl, p, self.city(isl), QRectF(box.left() + 28, box.top() + 22, 400, 30),
                 17, bold=True)
        button_y = box.top() + 20
        ui.button(isl, p, "weather:refresh",
                  QRectF(box.right() - 28 - 252, button_y, 116, 40),
                  "Обновить", True)
        ui.button(isl, p, "weather:city",
                  QRectF(box.right() - 28 - 128, button_y, 128, 40),
                  "Сменить город")
        x, y = box.left() + 28, box.top()
        if self.loading:
            ui.label(isl, p, "Загружаю прогноз…", QRectF(x, y + 100, 400, 24),
                     12, theme.MUTED)
        elif self.error:
            note(isl, p, QRectF(x, y + 100, box.width() - 56, 60), self.error,
                 theme.RED, 11)
        elif self.data is None:
            ui.label(isl, p, "Нажмите «Сменить город», чтобы выбрать город.",
                     QRectF(x, y + 100, 500, 24), 12, theme.MUTED)
        else:
            d = self.data
            ui.label(isl, p, f"{round(d['temperature_2m'])}°",
                     QRectF(x, y + 78, 300, 84), 64, bold=True)
            ui.label(isl, p, weather_description(int(d.get("weather_code", -1))),
                     QRectF(x + 4, y + 168, 400, 26), 14, theme.ACCENT_HOT, True)
            ui.label(isl, p,
                     f"Ощущается как {round(d['apparent_temperature'])}°   ·   "
                     f"Влажность {d['relative_humidity_2m']}%",
                     QRectF(x + 4, y + 204, box.width() - 56, 22), 11, theme.MUTED)

    def click(self, isl, key: str) -> None:
        if key == "weather:refresh":
            if not self.loading:
                self._load(isl, self.city(isl))
            return
        if key != "weather:city" or self.loading:
            return
        city = isl.ask("Погода", "Введите город:", self.city(isl))
        if not isinstance(city, str) or not city.strip():
            return
        self._load(isl, city.strip())


SECTIONS: list[Section] = [
    Home(), Sound(), Media(), System(), Settings(),
    Links(), Clipboard(), Calendar(), Weather(),
]
BY_KEY = {s.key: s for s in SECTIONS}
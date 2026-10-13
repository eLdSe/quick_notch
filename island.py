from __future__ import annotations

import math
import time
from datetime import datetime

from PySide6.QtCore import QPointF, QRectF, Qt, QTimer
from PySide6.QtGui import (QFont, QFontMetricsF, QGuiApplication, QPainter,
                           QPainterPath, QPen)
from PySide6.QtWidgets import QApplication, QWidget

import theme
import ui
import winrt_api
from bg import run_bg
from dialogs import Toast, ask
from sections import BY_KEY, SECTIONS
from spring import Spring
from store import STATE_PATH, read_state, write_state
from winapi import (autostart_enabled, battery_info, fullscreen_app_active,
                    set_autostart, set_keep_awake, system_snapshot)

COLLAPSED = (250, 40)
LIVE_EXTRA = 120            # на сколько остров шире, пока играет музыка
EXPANDED = (860, 520)
PAD = 44
TOP_MARGIN = 6


def _num(value, lo: float, hi: float, default: float) -> float:
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return min(hi, max(lo, float(value)))
    return default


class Island(QWidget):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool
                            | Qt.WindowDoesNotAcceptFocus)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setMouseTracking(True)
        self.setFocusPolicy(Qt.NoFocus)

        self.top_margin = TOP_MARGIN
        self.resize(EXPANDED[0] + 2 * PAD, EXPANDED[1] + PAD)
        screen = QGuiApplication.primaryScreen().geometry()
        self.move(screen.center().x() - self.width() // 2, TOP_MARGIN)

        # пружины
        self.target = 0.0
        self.sp_w = Spring(0.0)
        self.sp_h = Spring(0.0)
        self.sp_hover = Spring(0.0)
        self.sp_dim = Spring(0.0)
        self.sp_live = Spring(0.0)
        self.sp_tab = Spring(1.0)
        self.sp_pill_x = Spring(0.0)
        self.sp_pill_w = Spring(0.0)
        self.hs: dict[str, Spring] = {}       # hover по ключам
        self.ps: dict[str, Spring] = {}       # нажатие по ключам
        self.named: dict[str, Spring] = {}    # пружины разделов (ползунки и т.п.)

        # состояние интерфейса
        self.hover_key: str | None = None
        self.press_key: str | None = None
        self.drag_key: str | None = None      # ползунок, который сейчас тянут
        self.hits: dict[str, QRectF] = {}
        self.current_tool = "home"
        self.prev_tab: str | None = None
        self.tab_dir = 1
        self.live = False
        self.content_live = False
        self.busy = 0                    # открытые диалоги
        self.toasts: list[Toast] = []
        self._alpha = 1.0
        self.fullscreen = False

        # данные
        self.battery = battery_info()
        self.snapshot = None
        self.snap_busy = False
        self.media = {"active": False, "playing": False, "title": "", "artist": ""}
        self.media_busy = False
        self.tick_n = 0
        self.state = read_state(STATE_PATH)
        self._sanitize_state()
        theme.set_accent(self.state["accent"])
        self.autostart = autostart_enabled()
        self.nsp("autostart", 1.0 if self.autostart else 0.0)
        self.nsp("sys:awake", 1.0 if self.state["keep_awake"] else 0.0)
        if self.state["keep_awake"]:
            set_keep_awake(True)

        # таймеры
        self.frame_timer = QTimer(self)
        self.frame_timer.setTimerType(Qt.PreciseTimer)
        self.frame_timer.setInterval(8)
        self.frame_timer.timeout.connect(self._frame)
        self.last_frame = time.perf_counter()

        self.clock_timer = QTimer(self)
        self.clock_timer.setInterval(1000)
        self.clock_timer.timeout.connect(self._tick)
        self.clock_timer.start()

        self.eq_timer = QTimer(self)      # анимация эквалайзера
        self.eq_timer.setInterval(40)
        self.eq_timer.timeout.connect(self.update)

        self.leave_timer = QTimer(self)
        self.leave_timer.setSingleShot(True)
        self.leave_timer.timeout.connect(self._auto_collapse)

        self._snap_pill()
        self._refresh_snapshot()
        self.refresh_media()
        for section in SECTIONS:
            section.attach(self)

    # ---------- утилиты ----------

    def _font(self, size: float, bold: bool = False) -> QFont:
        font = QFont()
        font.setFamilies(["Segoe UI Variable Display", "Segoe UI"])
        font.setPixelSize(round(size))
        font.setBold(bold)
        return font

    def hv(self, key: str) -> float:
        s = self.hs.get(key)
        if s is None:
            s = self.hs[key] = Spring(0.0)
            s.target = 1.0 if key == self.hover_key else 0.0
        return s.x

    def pv(self, key: str) -> float:
        s = self.ps.get(key)
        if s is None:
            s = self.ps[key] = Spring(0.0)
            s.target = 1.0 if key == self.press_key else 0.0
        return s.x

    def nsp(self, key: str, init: float = 0.0) -> Spring:
        s = self.named.get(key)
        if s is None:
            s = self.named[key] = Spring(init)
        return s

    def add_hit(self, key: str, rect: QRectF) -> None:
        if self.content_live:
            self.hits[key] = rect

    def kick(self) -> None:
        self._start_frames()
        self.update()

    def alert(self, message: str) -> None:
        Toast(self, "Quick Notch", message, theme.RED, 7000)

    def ask(self, *args, **kwargs):
        return ask(self, *args, **kwargs)

    # ---------- сохранение и настройки ----------

    def _sanitize_state(self) -> None:
        s = self.state
        raw = s.get("reminders")
        clean = []
        if isinstance(raw, list):
            for item in raw:
                if (isinstance(item, dict) and isinstance(item.get("label"), str)
                        and isinstance(item.get("due"), (int, float))):
                    clean.append({"label": item["label"], "due": float(item["due"])})
        clean.sort(key=lambda r: r["due"])
        s["reminders"] = clean

        s["dim_enabled"] = s.get("dim_enabled") is not False
        s["leave_collapse"] = s.get("leave_collapse") is not False
        s["keep_awake"] = s.get("keep_awake") is True
        s["dim_level"] = _num(s.get("dim_level"), 0.2, 0.9, 0.65)
        s["leave_delay"] = _num(s.get("leave_delay"), 0.5, 8.0, 1.8)
        if s.get("accent") not in theme.ACCENT_KEYS:
            s["accent"] = "violet"

    def opt(self, name: str):
        return self.state[name]

    def save_state(self) -> None:
        try:
            write_state(STATE_PATH, self.state)
        except OSError as error:
            self.alert(f"Не удалось сохранить данные:\n{error}")

    def set_accent(self, key: str) -> None:
        theme.set_accent(key)
        self.state["accent"] = key
        self.save_state()
        self.kick()

    def toggle_opt(self, name: str) -> None:
        self.state[name] = not self.state[name]
        self.nsp(f"opt:{name}").target = 1.0 if self.state[name] else 0.0
        if name == "dim_enabled":
            self._update_dim()
        elif name == "leave_collapse" and not self.state[name]:
            self.leave_timer.stop()
        self.save_state()
        self.kick()

    def set_keep_awake(self, on: bool) -> None:
        try:
            set_keep_awake(on)
        except OSError as error:
            self.alert(f"Не удалось изменить режим:\n{error}")
            return
        self.state["keep_awake"] = on
        self.nsp("sys:awake").target = 1.0 if on else 0.0
        self.save_state()
        self.kick()

    # ---------- напоминания ----------

    @property
    def reminders(self) -> list[dict]:
        return self.state["reminders"]

    def add_reminder(self) -> None:
        minutes = self.ask("Напоминание", "Через сколько минут напомнить?", "15",
                           number=True, minv=1, maxv=1440)
        if minutes is None:
            return
        label = self.ask("Напоминание", "О чём напомнить?")
        if not label:
            return
        self.reminders.append({"label": label, "due": time.time() + minutes * 60})
        self.reminders.sort(key=lambda r: r["due"])
        self.save_state()

    def delete_reminder(self, index: int) -> None:
        if 0 <= index < len(self.reminders):
            del self.reminders[index]
            self.save_state()

    def _check_reminders(self) -> None:
        now = time.time()
        due = [r for r in self.reminders if r["due"] <= now]
        if not due:
            return
        self.state["reminders"] = [r for r in self.reminders if r["due"] > now]
        self.save_state()
        for item in due:
            Toast(self, "Напоминание", item["label"], theme.ORANGE)

    # ---------- автозапуск ----------

    def toggle_autostart(self) -> None:
        wanted = not self.autostart
        try:
            set_autostart(wanted)
        except OSError as error:
            self.alert(f"Не удалось изменить автозапуск:\n{error}")
            return
        self.autostart = wanted
        self.nsp("autostart").target = 1.0 if wanted else 0.0
        self.kick()

    # ---------- данные в фоне ----------

    def _refresh_snapshot(self) -> None:
        if self.snap_busy:
            return
        self.snap_busy = True

        def done(res, err) -> None:
            self.snap_busy = False
            self.snapshot = res if err is None else None

        run_bg(system_snapshot, done)

    def refresh_media(self, delay_ms: int = 0) -> None:
        if delay_ms:
            QTimer.singleShot(delay_ms, self.refresh_media)
            return
        if self.media_busy or not winrt_api.MEDIA_OK:
            return
        self.media_busy = True

        def done(res, err) -> None:
            self.media_busy = False
            if err is None and res is not None:
                self.media = res
                self._apply_media()

        run_bg(winrt_api.read_media, done)

    def set_playing(self, playing: bool) -> None:
        self.media["playing"] = playing
        self._apply_media()

    def _apply_media(self) -> None:
        self.nsp("play").target = 1.0 if self.media["playing"] else 0.0
        live = self.media["playing"] and bool(self.media["title"])
        self.sp_live.target = 1.0 if live else 0.0
        self._update_eq()
        self.kick()

    def _update_eq(self) -> None:
        want = self.sp_live.target == 1.0 and self.target == 0.0
        if want and not self.eq_timer.isActive():
            self.eq_timer.start()
        elif not want and self.eq_timer.isActive():
            self.eq_timer.stop()

    # ---------- анимация ----------

    def expand(self) -> None:
        self._set_target(1.0)

    def collapse(self) -> None:
        self._set_target(0.0)

    def _set_target(self, value: float) -> None:
        if self.target == value:
            return
        self.target = value
        self.sp_w.target = value
        self.sp_h.target = value
        self.sp_hover.target = 0.0
        self.leave_timer.stop()
        self._update_dim()
        self._update_eq()
        self._start_frames()

    def _update_dim(self) -> None:
        want = 1.0 if (self.opt("dim_enabled") and self.fullscreen
                       and self.target == 0 and self.hover_key is None) else 0.0
        if self.sp_dim.target != want:
            self.sp_dim.target = want
            self._start_frames()

    def _start_frames(self) -> None:
        if not self.frame_timer.isActive():
            self.last_frame = time.perf_counter() - 0.008
            self.frame_timer.start()

    def _frame(self) -> None:
        now = time.perf_counter()
        dt = min(now - self.last_frame, 0.05)
        self.last_frame = now

        if self.target:
            self.sp_w.step(dt, 210, 21)
            self.sp_h.step(dt, 165, 19)
        else:
            self.sp_w.step(dt, 320, 34)
            self.sp_h.step(dt, 300, 33)
        self.sp_hover.step(dt, 420, 32)
        self.sp_dim.step(dt, 120, 22)
        self.sp_live.step(dt, 200, 22)
        self.sp_tab.step(dt, 260, 26)
        self.sp_pill_x.step(dt, 380, 28)
        self.sp_pill_w.step(dt, 380, 28)
        for s in (*self.hs.values(), *self.ps.values(), *self.named.values()):
            s.step(dt, 520, 34)

        springs = (
            self.sp_w, self.sp_h, self.sp_hover, self.sp_dim, self.sp_live,
            self.sp_tab, self.sp_pill_x, self.sp_pill_w,
            *self.hs.values(), *self.ps.values(), *self.named.values(),
        )
        if all(s.settled for s in springs):
            for s in springs:
                s.snap()
            self.prev_tab = None
            self.frame_timer.stop()

        alpha = round(1 - self.opt("dim_level") * ui.clamp(self.sp_dim.x), 3)
        if alpha != self._alpha:
            self._alpha = alpha
            self.setWindowOpacity(alpha)
        self.update()

    def _tick(self) -> None:
        self.battery = battery_info()
        self._refresh_snapshot()
        try:
            self.fullscreen = fullscreen_app_active(int(self.winId()))
        except Exception:  # noqa: BLE001
            self.fullscreen = False
        self._update_dim()
        if self.tick_n % 2 == 0:
            self.refresh_media()
        self.tick_n += 1
        self._check_reminders()
        if self.target == 1:
            BY_KEY[self.current_tool].tick(self)
        if not self.frame_timer.isActive():
            self.update()

    # ---------- вкладки ----------

    def dock_layout(self) -> list[tuple[str, float, float]]:
        fm = QFontMetricsF(self._font(12, True))
        widths = [fm.horizontalAdvance(s.title) + 28 for s in SECTIONS]
        gap = 4
        x = (EXPANDED[0] - (sum(widths) + gap * (len(widths) - 1))) / 2
        out = []
        for s, w in zip(SECTIONS, widths):
            out.append((s.key, x, w))
            x += w + gap
        return out

    def _pill_target(self, key: str) -> tuple[float, float]:
        for k, x, w in self.dock_layout():
            if k == key:
                return x, w
        return 0.0, 0.0

    def _snap_pill(self) -> None:
        x, w = self._pill_target(self.current_tool)
        self.sp_pill_x.x = self.sp_pill_x.target = x
        self.sp_pill_w.x = self.sp_pill_w.target = w

    def select(self, key: str) -> None:
        if key == self.current_tool:
            return
        keys = [s.key for s in SECTIONS]
        self.tab_dir = 1 if keys.index(key) > keys.index(self.current_tool) else -1
        self.prev_tab, self.current_tool = self.current_tool, key
        self.sp_tab.x = self.sp_tab.v = 0.0
        self.sp_tab.target = 1.0
        self.sp_pill_x.target, self.sp_pill_w.target = self._pill_target(key)
        BY_KEY[key].enter(self)
        self._start_frames()

    # ---------- отрисовка ----------

    def paintEvent(self, _event) -> None:
        p = QPainter(self)
        p.setRenderHints(QPainter.Antialiasing | QPainter.TextAntialiasing)
        self.hits.clear()
        self.live = False
        self.content_live = False

        pw, ph, hv = self.sp_w.x, self.sp_h.x, self.sp_hover.x
        cw, ch = COLLAPSED
        cw += LIVE_EXTRA * max(0.0, self.sp_live.x)
        ew, eh = EXPANDED
        w = max((cw + (ew - cw) * pw) * (1 + 0.05 * hv), ch)
        h = max((ch + (eh - ch) * ph) * (1 + 0.12 * hv), ch * 0.8)
        rect = QRectF((self.width() - w) / 2, 1, w, h)
        radius = ch / 2 + (36 - ch / 2) * ui.clamp(ph)

        path = QPainterPath()
        path.addRoundedRect(rect, radius, radius)
        p.setPen(QPen(theme.SURFACE, 1))
        p.setBrush(theme.BG)
        p.drawPath(path)

        if self.target == 0:
            self.hits["expand"] = rect

        p.setClipPath(path)
        collapsed_a = ui.clamp(1 - ph * 3.2)
        if collapsed_a > 0:
            p.setOpacity(collapsed_a)
            self._paint_collapsed(p, rect)
        expanded_a = ui.clamp((ph - 0.5) / 0.45)
        if expanded_a > 0:
            p.setOpacity(expanded_a)
            self._paint_expanded(p, expanded_a)

    def _paint_collapsed(self, p: QPainter, rect: QRectF) -> None:
        base = p.opacity()
        mid = rect.center().y()
        live = ui.clamp(self.sp_live.x)

        p.setPen(theme.TEXT)
        p.setFont(self._font(14, True))
        p.drawText(QRectF(rect.left() + 22, mid - 15, 80, 30),
                   Qt.AlignLeft | Qt.AlignVCenter, datetime.now().strftime("%H:%M"))
        batt_left = self._paint_battery(p, rect.right() - 22, mid)

        if live > 0.02 and self.media["title"]:
            p.setOpacity(base * live)
            eq_x = rect.left() + 22 + 58
            t = time.perf_counter()
            p.setPen(Qt.NoPen)
            p.setBrush(theme.ACCENT)
            for i in range(4):
                hgt = 3 + 11 * (0.5 + 0.5 * math.sin(t * (5 + i * 1.7) + i * 1.3))
                p.drawRoundedRect(QRectF(eq_x + i * 5, mid - hgt / 2, 3, hgt), 1.5, 1.5)
            text_x = eq_x + 26
            width = batt_left - 12 - text_x
            if width > 30:
                title = self.media["title"]
                if self.media["artist"]:
                    title = f"{title} · {self.media['artist']}"
                ui.label(self, p, title, QRectF(text_x, mid - 12, width, 24),
                         11, theme.MUTED)
            p.setOpacity(base)
        if live < 0.5 and self.reminders:
            p.setOpacity(base * (1 - live * 2))
            p.setPen(Qt.NoPen)
            p.setBrush(theme.ORANGE)
            p.drawEllipse(QPointF(rect.center().x(), mid), 3.5, 3.5)
            p.setOpacity(base)

    def _paint_battery(self, p: QPainter, right: float, cy: float) -> float:
        """Рисует процент и значок, возвращает левый край."""
        pct, charging = self.battery
        p.setFont(self._font(12, True))
        p.setPen(theme.TEXT)
        label = "—" if pct is None else f"{pct}%"
        text_w = p.fontMetrics().horizontalAdvance(label)
        p.drawText(QRectF(right - text_w - 2, cy - 12, text_w + 4, 24),
                   Qt.AlignRight | Qt.AlignVCenter, label)
        if pct is None:
            return right - text_w - 2
        x0 = right - text_w - 10
        body = QRectF(x0 - 24, cy - 6, 24, 12)
        if charging:
            color = theme.GREEN
        elif pct <= 10:
            color = theme.RED
        elif pct <= 20:
            color = theme.YELLOW
        else:
            color = theme.TEXT
        p.setPen(QPen(theme.MUTED, 1))
        p.setBrush(Qt.NoBrush)
        p.drawRoundedRect(body, 4, 4)
        inner = (body.width() - 4) * pct / 100
        if inner >= 1:
            p.setPen(Qt.NoPen)
            p.setBrush(color)
            p.drawRoundedRect(
                QRectF(body.left() + 2, body.top() + 2, inner, body.height() - 4), 2, 2)
        p.setPen(Qt.NoPen)
        p.setBrush(theme.MUTED)
        p.drawRoundedRect(QRectF(x0 + 1.5, cy - 2.5, 2, 5), 1, 1)
        return body.left()

    def _paint_expanded(self, p: QPainter, a: float) -> None:
        now = datetime.now()
        W, H = EXPANDED
        fx = (self.width() - W) / 2          # левый край финального размера
        oy = 1 + (1 - a) * 14                # лёгкий подъём при появлении
        self.live = self.target == 1 and a > 0.9
        self.content_live = self.live

        p.setPen(theme.TEXT)
        p.setFont(self._font(34, True))
        p.drawText(QPointF(fx + 28, oy + 52), now.strftime("%H:%M"))
        ui.label(
            self, p,
            f"{ui.WEEKDAYS[now.weekday()].capitalize()}, {now.day} {ui.MONTHS[now.month - 1]}",
            QRectF(fx + 31, oy + 62, 400, 20), 12, theme.MUTED,
        )
        for key, cx in (("close", fx + W - 40), ("collapse", fx + W - 82)):
            c = QPointF(cx, oy + 38)
            circle = QRectF(cx - 16, oy + 22, 32, 32)
            with ui.pressable(self, p, key, circle):
                p.setPen(Qt.NoPen)
                p.setBrush(ui.mix(theme.SURFACE_2, theme.SURFACE_3, self.hv(key)))
                p.drawEllipse(c, 16, 16)
                p.setPen(QPen(theme.TEXT, 2, Qt.SolidLine, Qt.RoundCap, Qt.RoundJoin))
                if key == "close":
                    p.drawLine(c + QPointF(-4.5, -4.5), c + QPointF(4.5, 4.5))
                    p.drawLine(c + QPointF(-4.5, 4.5), c + QPointF(4.5, -4.5))
                else:
                    p.drawPolyline([c + QPointF(-4.5, 2), c + QPointF(0, -2),
                                    c + QPointF(4.5, 2)])
            self.add_hit(key, circle)

        chip = QRectF(fx + W - 112 - 112, oy + 22, 96, 32)
        ui.card(p, chip, theme.SURFACE, 16)
        self._paint_battery(p, chip.right() - 16, chip.center().y())

        ui.label(self, p, BY_KEY[self.current_tool].title.upper(),
                 QRectF(fx + 30, oy + 84, 300, 18), 10, theme.MUTED, True)
        self._paint_section(p, a, fx, oy)
        self._paint_dock(p, fx, oy + H - 44)

    def _paint_section(self, p: QPainter, a: float, fx: float, oy: float) -> None:
        area = QRectF(fx + 28, oy + 118, EXPANDED[0] - 56, 306)
        t = self.sp_tab.x
        live = self.live

        if t < 0.999 and self.prev_tab is not None:
            self.content_live = False
            p.save()
            p.setOpacity(a * max(0.0, 1 - t * 1.8))
            p.translate(-self.tab_dir * 18 * t, 0)
            BY_KEY[self.prev_tab].paint(self, p, area)
            p.restore()

        p.save()
        p.setOpacity(a * ui.clamp(t * 1.3))
        p.translate(self.tab_dir * 18 * (1 - t), 0)
        self.content_live = live and t > 0.9
        BY_KEY[self.current_tool].paint(self, p, area)
        p.restore()
        self.content_live = live

    def _paint_dock(self, p: QPainter, fx: float, cy: float) -> None:
        layout = self.dock_layout()
        left = fx + layout[0][1] - 8
        right = fx + layout[-1][1] + layout[-1][2] + 8
        ui.card(p, QRectF(left, cy - 22, right - left, 44), theme.SURFACE, 22)
        ui.card(p, QRectF(fx + self.sp_pill_x.x, cy - 17, self.sp_pill_w.x, 34),
                theme.ACCENT, 17)
        for key, x, w in layout:
            rect = QRectF(fx + x, cy - 17, w, 34)
            selected = key == self.current_tool
            color = theme.TEXT if selected else ui.mix(
                theme.MUTED, theme.TEXT, self.hv(f"tab:{key}"))
            ui.label(self, p, BY_KEY[key].title, rect, 12, color, True, Qt.AlignCenter)
            self.add_hit(f"tab:{key}", rect)

    # ---------- события ----------

    def _hit(self, pos: QPointF) -> str | None:
        for key, rect in reversed(list(self.hits.items())):
            if rect.contains(pos):
                return key
        return None

    def _set_hover(self, key: str | None) -> None:
        if key == self.hover_key:
            return
        self.hover_key = key
        self.sp_hover.target = 1.0 if (key == "expand" and self.target == 0) else 0.0
        for k, s in self.hs.items():
            s.target = 1.0 if k == key else 0.0
        self.setCursor(Qt.PointingHandCursor if key else Qt.ArrowCursor)
        self._update_dim()
        self._start_frames()
        self.update()

    def _set_press(self, key: str | None) -> None:
        self.press_key = key
        for k, s in self.ps.items():
            s.target = 1.0 if k == key else 0.0
        self._start_frames()

    def mouseMoveEvent(self, event) -> None:
        if self.drag_key:
            BY_KEY[self.current_tool].drag(self, self.drag_key, event.position())
        self._set_hover(self._hit(event.position()))

    def mousePressEvent(self, event) -> None:
        if event.button() != Qt.LeftButton:
            return
        key = self._hit(event.position())
        if key and key.startswith("slider:"):
            self.drag_key = key
            BY_KEY[self.current_tool].drag(self, key, event.position())
        self._set_press(key)

    def mouseReleaseEvent(self, event) -> None:
        if event.button() != Qt.LeftButton:
            return
        if self.drag_key:                      # ползунок: клика по отпусканию нет
            BY_KEY[self.current_tool].drag_end(self, self.drag_key)
            self.drag_key = None
            self._set_press(None)
            return
        key = self._hit(event.position())
        pressed = self.press_key
        self._set_press(None)
        if key is not None and key == pressed:
            self._activate(key)

    def wheelEvent(self, event) -> None:
        if self.target == 1:
            BY_KEY[self.current_tool].wheel(self, event.angleDelta().y() / 120)

    def _activate(self, key: str) -> None:
        if key == "expand":
            self.expand()
        elif key == "collapse":
            self.collapse()
        elif key == "close":
            for toast in list(self.toasts):
                toast.dismiss()
            QApplication.instance().quit()
        elif key.startswith("tab:"):
            self.select(key[4:])
        else:
            BY_KEY[self.current_tool].click(self, key)
            self.hover_key = None
            self.kick()

    def enterEvent(self, _event) -> None:
        self.leave_timer.stop()

    def leaveEvent(self, _event) -> None:
        self._set_hover(None)
        if self.target == 1 and self.opt("leave_collapse"):
            self.leave_timer.setInterval(round(self.opt("leave_delay") * 1000))
            self.leave_timer.start()

    def _auto_collapse(self) -> None:
        if self.busy == 0 and self.target == 1 and self.hover_key is None:
            self.collapse()

    def keyPressEvent(self, event) -> None:
        if event.key() == Qt.Key_Escape:
            self.collapse()
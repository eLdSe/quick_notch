from __future__ import annotations

import math
import time
from typing import Any

from PySide6.QtCore import QRectF, Qt, QTimer
from PySide6.QtGui import QColor, QGuiApplication, QPainter, QPen
from PySide6.QtWidgets import (QDialog, QHBoxLayout, QLabel, QLineEdit,
                               QPushButton, QVBoxLayout, QWidget)

import theme
import ui
from spring import ease_out_back
from winapi import dark_titlebar, set_timer_resolution


class Toast(QWidget):
    """Неблокирующее уведомление под островом. Клик закрывает."""

    WIDTH = 380

    def __init__(self, app, title: str, message: str,
                 accent: QColor = theme.ACCENT, timeout_ms: int | None = None) -> None:
        super().__init__()
        self.app, self.title, self.accent = app, title, accent
        self.message = ui.trim(message, 400) if "\n" not in message else message[:400]
        self.closed = False
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint
                            | Qt.Tool | Qt.WindowDoesNotAcceptFocus)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setAttribute(Qt.WA_ShowWithoutActivating)
        self.setCursor(Qt.PointingHandCursor)

        self.msg_h = ui.wrapped_height(app._font(12), self.message, self.WIDTH - 48)
        h = math.ceil(52 + self.msg_h + 12 + 36 + 18)
        self.resize(self.WIDTH, h)

        offset = sum(t.height() + 10 for t in app.toasts)
        screen = QGuiApplication.primaryScreen().geometry()
        self.x0 = screen.center().x() - self.WIDTH // 2
        self.y1 = app.top_margin + 64 + offset
        self.y0 = self.y1 - 28
        app.toasts.append(self)

        self.move(self.x0, self.y0)
        self.setWindowOpacity(0.0)
        self.show()
        self.t0 = time.perf_counter()
        self.anim = QTimer(self)
        self.anim.setInterval(14)
        self.anim.timeout.connect(self._step)
        self._timer_resolution_owner = object()
        set_timer_resolution(True, self._timer_resolution_owner)
        self.anim.start()
        if timeout_ms:
            QTimer.singleShot(timeout_ms, self.dismiss)

    def _step(self) -> None:
        p = min(1.0, (time.perf_counter() - self.t0) / 0.45)
        y = round(self.y0 + (self.y1 - self.y0) * ease_out_back(p, 1.4))
        self.move(self.x0, y)
        self.setWindowOpacity(min(1.0, p * 3))
        if p >= 1:
            self.anim.stop()
            set_timer_resolution(False, self._timer_resolution_owner)

    def paintEvent(self, _event) -> None:
        p = QPainter(self)
        p.setRenderHints(QPainter.Antialiasing | QPainter.TextAntialiasing)
        w, h = self.width(), self.height()
        p.setPen(QPen(theme.SURFACE_2, 1))
        p.setBrush(theme.SURFACE)
        p.drawRoundedRect(QRectF(1, 1, w - 2, h - 2), 28, 28)
        p.setPen(Qt.NoPen)
        p.setBrush(self.accent)
        p.drawEllipse(QRectF(24, 24, 12, 12))
        ui.label(self.app, p, self.title, QRectF(46, 18, w - 70, 24), 13, bold=True)
        ui.label(self.app, p, self.message, QRectF(24, 52, w - 48, self.msg_h + 4),
                 12, QColor("#ebebf5"), align=Qt.AlignLeft | Qt.AlignTop, wrap=True)
        btn = QRectF(w - 24 - 104, h - 18 - 36, 104, 36)
        ui.card(p, btn, self.accent, 18)
        ui.label(self.app, p, "Готово", btn, 11, bold=True, align=Qt.AlignCenter)

    def mousePressEvent(self, _event) -> None:
        self.dismiss()

    def dismiss(self) -> None:
        if self.closed:
            return
        self.closed = True
        self.anim.stop()
        set_timer_resolution(False, self._timer_resolution_owner)
        if self in self.app.toasts:
            self.app.toasts.remove(self)
        self.close()


_STYLE = """
QDialog { background: #14141a; }
QLabel { color: #ebebf5; font-size: 14px; }
QLabel#error { color: #ff453a; font-size: 12px; }
QLineEdit { background: #1f1f28; color: #ffffff; border: 1px solid #2a2a36;
            border-radius: 8px; padding: 8px 10px; font-size: 14px;
            selection-background-color: #8b7bff; }
QLineEdit:focus { border-color: #8b7bff; }
QPushButton { background: #1f1f28; color: #ffffff; border: none;
              border-radius: 16px; padding: 8px 22px; font-weight: bold;
              font-size: 14px; }
QPushButton:hover { background: #2a2a36; }
QPushButton#ok { background: #8b7bff; }
QPushButton#ok:hover { background: #a79bff; }
"""


def ask(app, title: str, prompt: str, initial: str = "", *,
        number: bool = False, minv: int = 1, maxv: int = 1440) -> Any:
    """Тёмный модальный диалог. Возвращает строку/число или None."""
    result: list[Any] = []
    dlg = QDialog(app)
    dlg.setWindowTitle(title)
    dlg.setWindowFlag(Qt.WindowStaysOnTopHint, True)
    dlg.setStyleSheet(_STYLE)
    dlg.setMinimumWidth(420)

    layout = QVBoxLayout(dlg)
    layout.setContentsMargins(24, 22, 24, 20)
    layout.setSpacing(8)
    layout.addWidget(QLabel(prompt))
    entry = QLineEdit(initial)
    layout.addWidget(entry)
    error = QLabel("")
    error.setObjectName("error")
    layout.addWidget(error)
    row = QHBoxLayout()
    row.addStretch(1)
    cancel, ok = QPushButton("Отмена"), QPushButton("Готово")
    ok.setObjectName("ok")
    row.addWidget(cancel)
    row.addWidget(ok)
    layout.addLayout(row)

    def submit() -> None:
        value = entry.text().strip()
        if number:
            try:
                parsed = int(value)
            except ValueError:
                parsed = None
            if parsed is None or not minv <= parsed <= maxv:
                error.setText(f"Введите число от {minv} до {maxv}")
                return
            result.append(parsed)
        else:
            if not value:
                error.setText("Поле не может быть пустым")
                return
            result.append(value)
        dlg.accept()

    ok.clicked.connect(submit)
    entry.returnPressed.connect(submit)
    cancel.clicked.connect(dlg.reject)

    dark_titlebar(int(dlg.winId()))
    dlg.adjustSize()
    screen = QGuiApplication.primaryScreen().geometry()
    dlg.move(screen.center().x() - dlg.width() // 2,
             screen.height() // 3 - dlg.height() // 2)
    entry.setFocus()
    entry.selectAll()

    app.busy += 1
    try:
        dlg.exec()
    finally:
        app.busy -= 1
    return result[0] if result else None

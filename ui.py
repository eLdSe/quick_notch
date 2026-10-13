from __future__ import annotations

from contextlib import contextmanager

from PySide6.QtCore import QPointF, QRectF, Qt
from PySide6.QtGui import (QColor, QFont, QFontMetricsF, QPainter, QPen,
                           QPolygonF, QTextLayout, QTextOption)

import theme

WEEKDAYS = ("понедельник", "вторник", "среда", "четверг",
            "пятница", "суббота", "воскресенье")
MONTHS = ("января", "февраля", "марта", "апреля", "мая", "июня",
          "июля", "августа", "сентября", "октября", "ноября", "декабря")
MONTHS_NOM = ("Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
              "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь")


def clamp(x: float, lo: float = 0.0, hi: float = 1.0) -> float:
    return max(lo, min(hi, x))


def trim(text: str, limit: int) -> str:
    text = text.replace("\n", " ").strip()
    return text if len(text) <= limit else text[: limit - 1] + "…"


def mix(a: QColor, b: QColor, t: float) -> QColor:
    t = clamp(t)
    return QColor(
        round(a.red() + (b.red() - a.red()) * t),
        round(a.green() + (b.green() - a.green()) * t),
        round(a.blue() + (b.blue() - a.blue()) * t),
    )


def card(p: QPainter, rect: QRectF, fill: QColor, r: float = 22) -> None:
    p.setPen(Qt.NoPen)
    p.setBrush(fill)
    p.drawRoundedRect(rect, r, r)


def label(
    isl, p: QPainter, text: str, rect: QRectF, size: float = 12,
    color: QColor = theme.TEXT, bold: bool = False,
    align=Qt.AlignLeft | Qt.AlignVCenter, wrap: bool = False,
) -> None:
    font = isl._font(size, bold)
    if not wrap:
        text = QFontMetricsF(font).elidedText(text, Qt.ElideRight, rect.width())
    option = QTextOption(align)
    option.setWrapMode(QTextOption.WordWrap if wrap else QTextOption.NoWrap)
    p.setPen(color)
    p.setFont(font)
    p.drawText(rect, text, option)


def wrapped_height(font: QFont, text: str, width: float) -> float:
    layout = QTextLayout(text, font)
    option = QTextOption()
    option.setWrapMode(QTextOption.WordWrap)
    layout.setTextOption(option)
    layout.beginLayout()
    height = 0.0
    while True:
        line = layout.createLine()
        if not line.isValid():
            break
        line.setLineWidth(width)
        line.setPosition(QPointF(0, height))
        height += line.height()
    layout.endLayout()
    return height


def bar(p: QPainter, rect: QRectF, frac: float, color: QColor) -> None:
    frac = clamp(frac)
    card(p, rect, theme.SURFACE_2, rect.height() / 2)
    if frac > 0:
        fill = QRectF(rect.left(), rect.top(),
                      max(rect.height(), rect.width() * frac), rect.height())
        card(p, fill, color, rect.height() / 2)


def switch(p: QPainter, rect: QRectF, v: float, enabled: bool = True) -> None:
    track = mix(theme.SURFACE_3, theme.ACCENT, v) if enabled else theme.SURFACE_3
    card(p, rect, track, rect.height() / 2)
    d = rect.height() - 4
    x = rect.left() + 2 + (rect.width() - rect.height()) * v
    p.setPen(Qt.NoPen)
    p.setBrush(theme.TEXT if enabled else QColor("#6c6c78"))
    p.drawEllipse(QRectF(x, rect.top() + 2, d, d))


def slider(isl, p: QPainter, key: str, track: QRectF, frac: float,
           enabled: bool = True) -> None:
    """Ползунок: полоса, кружок и зона для мыши (ключ должен начинаться с slider:)."""
    frac = clamp(frac)
    bar(p, track, frac, theme.ACCENT if enabled else theme.MUTED)
    grow = max(isl.hv(key), isl.pv(key)) if enabled else 0.0
    r = 11 * (1 + 0.15 * grow)
    cx = track.left() + track.width() * frac
    p.setPen(Qt.NoPen)
    p.setBrush(theme.TEXT if enabled else QColor("#6c6c78"))
    p.drawEllipse(QPointF(cx, track.center().y()), r, r)
    if enabled:
        isl.add_hit(key, track.adjusted(-14, -16, 14, 16))


def segmented(isl, p: QPainter, rect: QRectF, items, v: float, prefix: str) -> None:
    """Переключатель вкладок. v: позиция «пилюли» в сегментах (0..n-1)."""
    card(p, rect, theme.SURFACE, rect.height() / 2)
    seg = (rect.width() - 6) / len(items)
    pill = QRectF(rect.left() + 3 + seg * v, rect.top() + 3, seg, rect.height() - 6)
    card(p, pill, theme.ACCENT, pill.height() / 2)
    for i, (key, text) in enumerate(items):
        r = QRectF(rect.left() + 3 + seg * i, rect.top() + 3, seg, rect.height() - 6)
        near = 1 - clamp(abs(v - i))
        k = f"{prefix}:{key}"
        color = mix(theme.MUTED, theme.TEXT, max(near, isl.hv(k)))
        label(isl, p, text, r, 12, color, True, Qt.AlignCenter)
        isl.add_hit(k, r)


@contextmanager
def pressable(isl, p: QPainter, key: str, rect: QRectF):
    """Всё, что нарисовано внутри, слегка уменьшается при нажатии."""
    s = 1 - 0.04 * isl.pv(key)
    c = rect.center()
    p.save()
    p.translate(c)
    p.scale(s, s)
    p.translate(-c.x(), -c.y())
    try:
        yield
    finally:
        p.restore()


@contextmanager
def panel(isl, p: QPainter, key: str, rect: QRectF, r: float = 24):
    with pressable(isl, p, key, rect):
        card(p, rect, mix(theme.SURFACE, theme.SURFACE_2, isl.hv(key)), r)
        yield
    isl.add_hit(key, rect)


def button(isl, p: QPainter, key: str, rect: QRectF, text: str,
           accent: bool = False, size: float = 12) -> None:
    base, hot = ((theme.ACCENT, theme.ACCENT_HOT) if accent
                 else (theme.SURFACE_2, theme.SURFACE_3))
    with pressable(isl, p, key, rect):
        card(p, rect, mix(base, hot, isl.hv(key)), rect.height() / 2)
        label(isl, p, text, rect, size, bold=True, align=Qt.AlignCenter)
    isl.add_hit(key, rect)


@contextmanager
def round_button(isl, p: QPainter, key: str, c: QPointF, r: float,
                 accent: bool = False):
    rect = QRectF(c.x() - r, c.y() - r, 2 * r, 2 * r)
    base, hot = ((theme.ACCENT, theme.ACCENT_HOT) if accent
                 else (theme.SURFACE_2, theme.SURFACE_3))
    with pressable(isl, p, key, rect):
        p.setPen(Qt.NoPen)
        p.setBrush(mix(base, hot, isl.hv(key)))
        p.drawEllipse(c, r, r)
        yield
    isl.add_hit(key, rect)


def close_dot(isl, p: QPainter, key: str, c: QPointF, r: float = 11) -> None:
    rect = QRectF(c.x() - r, c.y() - r, 2 * r, 2 * r)
    with pressable(isl, p, key, rect):
        p.setPen(Qt.NoPen)
        p.setBrush(mix(theme.SURFACE_3, QColor("#34343f"), isl.hv(key)))
        p.drawEllipse(c, r, r)
        p.setPen(QPen(theme.TEXT, 1.6, Qt.SolidLine, Qt.RoundCap))
        d = 3.5
        p.drawLine(c + QPointF(-d, -d), c + QPointF(d, d))
        p.drawLine(c + QPointF(-d, d), c + QPointF(d, -d))
    isl.add_hit(key, rect)


def media_icon(p: QPainter, kind: str, c: QPointF, s: float, alpha: float = 1.0) -> None:
    if alpha < 0.03:
        return
    p.save()
    p.setOpacity(p.opacity() * alpha)
    p.setPen(Qt.NoPen)
    p.setBrush(theme.TEXT)
    x, y = c.x(), c.y()
    if kind == "play":
        p.drawPolygon(QPolygonF([QPointF(x - s * 0.45, y - s),
                                 QPointF(x - s * 0.45, y + s),
                                 QPointF(x + s * 0.95, y)]))
    elif kind == "pause":
        for dx in (-0.45, 0.45):
            p.drawRoundedRect(
                QRectF(x + dx * s - s * 0.22, y - s * 0.9, s * 0.44, s * 1.8), 1.5, 1.5)
    else:
        d = 1 if kind == "next" else -1
        p.drawPolygon(QPolygonF([QPointF(x - d * s * 0.85, y - s * 0.8),
                                 QPointF(x - d * s * 0.85, y + s * 0.8),
                                 QPointF(x + d * s * 0.45, y)]))
        p.drawRoundedRect(
            QRectF(x + d * s * 0.6 - s * 0.15, y - s * 0.8, s * 0.3, s * 1.6), 1, 1)
    p.restore()
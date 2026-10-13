from __future__ import annotations

import threading
from typing import Any, Callable

from PySide6.QtCore import QObject, Signal


class _Bridge(QObject):
    done = Signal(object, object, object)

    def __init__(self) -> None:
        super().__init__()
        self.done.connect(self._dispatch)

    def _dispatch(self, callback, result, error) -> None:
        callback(result, error)


_bridge: _Bridge | None = None


def init() -> None:
    """Вызвать один раз в главном потоке, после QApplication."""
    global _bridge
    _bridge = _Bridge()


def run_bg(fn: Callable[[], Any], callback: Callable[[Any, Exception | None], None]) -> None:
    """fn выполняется в потоке, callback(result, error) в главном."""
    def work() -> None:
        try:
            result, error = fn(), None
        except Exception as exc:  # noqa: BLE001 — ошибка уходит в callback
            result, error = None, exc
        _bridge.done.emit(callback, result, error)

    threading.Thread(target=work, daemon=True).start()

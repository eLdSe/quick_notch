from PySide6.QtGui import QColor

BG = QColor("#000000")
SURFACE = QColor("#14141a")
SURFACE_2 = QColor("#1f1f28")
SURFACE_3 = QColor("#2a2a36")
TEXT = QColor("#ffffff")
MUTED = QColor("#8e8e93")
GREEN = QColor("#30d158")
YELLOW = QColor("#ffd60a")
ORANGE = QColor("#ff9f0a")
RED = QColor("#ff453a")

# (ключ, основной цвет, цвет при наведении)
ACCENTS = (
    ("violet", "#8b7bff", "#a79bff"),
    ("blue", "#0a84ff", "#409cff"),
    ("cyan", "#32ade6", "#64c0ee"),
    ("green", "#30d158", "#5de07f"),
    ("orange", "#ff9f0a", "#ffb340"),
    ("pink", "#ff375f", "#ff6482"),
    ("graphite", "#636366", "#8e8e93"),
)
ACCENT_KEYS = tuple(a[0] for a in ACCENTS)

ACCENT = QColor("#8b7bff")
ACCENT_HOT = QColor("#a79bff")
current_accent = "violet"


def set_accent(key: str) -> None:
    """Меняет акцент. Все, кто читает theme.ACCENT при отрисовке, подхватят сразу."""
    global ACCENT, ACCENT_HOT, current_accent
    for k, main, hot in ACCENTS:
        if k == key:
            ACCENT, ACCENT_HOT, current_accent = QColor(main), QColor(hot), k
            return
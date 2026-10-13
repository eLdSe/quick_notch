import sys

from PySide6.QtWidgets import QApplication

import bg
from island import Island
from winapi import already_running, clear_timer_resolution


def main() -> None:
    if already_running():
        return
    app = QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)
    bg.init()
    island = Island()
    island.show()
    try:
        code = app.exec()
    finally:
        clear_timer_resolution()
    sys.exit(code)


if __name__ == "__main__":
    main()
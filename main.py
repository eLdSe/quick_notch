import sys

from PySide6.QtWidgets import QApplication

import bg
from island import Island
from winapi import already_running, set_timer_resolution


def main() -> None:
    if already_running():
        return
    set_timer_resolution(True)
    app = QApplication(sys.argv)
    app.setQuitOnLastWindowClosed(False)
    bg.init()
    island = Island()
    island.show()
    code = app.exec()
    set_timer_resolution(False)
    sys.exit(code)


if __name__ == "__main__":
    main()
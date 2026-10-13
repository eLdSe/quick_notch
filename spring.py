import math


class Spring:
    __slots__ = ("x", "v", "target")

    def __init__(self, x: float = 0.0) -> None:
        self.x = x
        self.v = 0.0
        self.target = x

    def step(self, dt: float, k: float, c: float) -> None:
        n = max(1, math.ceil(dt / 0.004))
        h = dt / n
        for _ in range(n):
            self.v += (k * (self.target - self.x) - c * self.v) * h
            self.x += self.v * h

    @property
    def settled(self) -> bool:
        return abs(self.target - self.x) < 5e-4 and abs(self.v) < 5e-3

    def snap(self) -> None:
        self.x, self.v = self.target, 0.0


def ease_out_back(x: float, s: float = 1.2) -> float:
    c3 = s + 1
    return 1 + c3 * (x - 1) ** 3 + s * (x - 1) ** 2

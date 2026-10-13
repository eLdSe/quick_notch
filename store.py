from __future__ import annotations

import json
import os
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

BASE = Path(os.environ.get("APPDATA", Path.home())) / "QuickNotch"
LINKS_PATH = BASE / "links.json"
STATE_PATH = BASE / "state.json"


def read_links(path: Path) -> list[dict[str, str]]:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return [
            {"label": "YouTube", "target": "https://www.youtube.com"},
            {"label": "Google", "target": "https://www.google.com"},
            {"label": "Домашняя папка", "target": str(Path.home())},
        ]
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"Не удалось загрузить быстрые ссылки: {error}") from error
    if not isinstance(payload, list):
        raise ValueError("Файл быстрых ссылок должен содержать список.")
    return [
        {"label": i["label"], "target": i["target"]}
        for i in payload
        if isinstance(i, dict) and isinstance(i.get("label"), str)
        and isinstance(i.get("target"), str)
    ]


def write_links(path: Path, links: list[dict[str, str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(links, ensure_ascii=False, indent=2), encoding="utf-8")


def read_state(path: Path) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    return data if isinstance(data, dict) else {}


def write_state(path: Path, data: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def weather_description(code: int) -> str:
    """Описание по коду погоды WMO."""
    if code == 0:
        return "Ясно"
    if code == 1:
        return "Преимущественно ясно"
    if code == 2:
        return "Переменная облачность"
    if code == 3:
        return "Пасмурно"
    if code in (45, 48):
        return "Туман"
    if code in (51, 53, 55, 56, 57):
        return "Морось"
    if code in (61, 63, 65, 66, 67, 80, 81, 82):
        return "Дождь"
    if code in (71, 73, 75, 77, 85, 86):
        return "Снег"
    if code in (95, 96, 99):
        return "Гроза"
    return "Погода"


def _get_json(url: str) -> dict[str, Any]:
    request = urllib.request.Request(url, headers={"User-Agent": "QuickNotch/1.0"})
    with urllib.request.urlopen(request, timeout=8) as response:
        return json.load(response)


def fetch_weather(city: str) -> dict[str, Any]:
    query = urllib.parse.urlencode({"name": city, "count": 1, "language": "ru"})
    results = _get_json(
        f"https://geocoding-api.open-meteo.com/v1/search?{query}").get("results", [])
    if not results:
        raise ValueError(f"Город «{city}» не найден.")
    place = results[0]
    query = urllib.parse.urlencode({
        "latitude": place["latitude"],
        "longitude": place["longitude"],
        "current": "temperature_2m,apparent_temperature,relative_humidity_2m,weather_code",
        "timezone": "auto",
    })
    forecast = _get_json(f"https://api.open-meteo.com/v1/forecast?{query}")
    return {"city": place["name"], **forecast["current"]}

"""Galaxy Music REST client for uploading Essentia audio profiles."""

from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent
CONFIG_PATH = ROOT / "galaxy.json"


class GalaxyError(RuntimeError):
    pass


def load_config(path: Path | None = None) -> dict:
    config_path = path or CONFIG_PATH
    data = {}
    if config_path.is_file():
        data = json.loads(config_path.read_text(encoding="utf-8"))
    url = os.environ.get("GALAXY_API_URL", data.get("url") or "").rstrip("/")
    key = os.environ.get("GALAXY_API_KEY", data.get("apiKey") or data.get("api_key") or "")
    return {"url": url, "apiKey": key, "path": str(config_path)}


def save_config(url: str, api_key: str, path: Path | None = None) -> None:
    config_path = path or CONFIG_PATH
    existing = {}
    if config_path.is_file():
        existing = json.loads(config_path.read_text(encoding="utf-8"))
    existing["url"] = url.rstrip("/")
    if api_key:
        existing["apiKey"] = api_key
    config_path.write_text(json.dumps(existing, indent=2) + "\n", encoding="utf-8")


class GalaxyClient:
    def __init__(self, url: str, api_key: str):
        self.url = url.rstrip("/")
        self.api_key = api_key.strip()
        if not self.url:
            raise GalaxyError("Set a Galaxy Music URL (e.g. http://localhost:5107).")
        if not self.api_key:
            raise GalaxyError("Set a write API key from Galaxy Music Settings.")

    def _request(self, method: str, path: str, body: dict | None = None):
        data = None
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "Accept": "application/json",
        }
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(self.url + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=60) as res:
                raw = res.read()
                return json.loads(raw.decode("utf-8")) if raw else {}
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise GalaxyError(f"{method} {path} failed ({exc.code}): {detail}") from exc
        except urllib.error.URLError as exc:
            raise GalaxyError(f"Could not reach {self.url}: {exc.reason}") from exc

    def pending(self, take: int = 25) -> list[dict]:
        payload = self._request("GET", f"/api/v1/tracks/pending-audio?take={take}")
        return list(payload.get("items") or [])

    def put_profile(self, track_id: int, profile: dict) -> dict:
        return self._request("PUT", f"/api/v1/tracks/{track_id}/audio-profile", profile)

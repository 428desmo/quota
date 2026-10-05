"""Serve the Unity WebGL export and the Unity lobby.

Port 8080 is the Unity client. Port 8000 stays the separate Python HTML table.
"""

from __future__ import annotations

import argparse
import json
import secrets
import threading
import time
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DIR = ROOT / "unity" / "Builds" / "WebGL"
PORT = 8080

TYPES = {
    ".html": "text/html; charset=utf-8",
    ".js": "application/javascript",
    ".css": "text/css; charset=utf-8",
    ".wasm": "application/wasm",
    ".data": "application/octet-stream",
    ".unityweb": "application/octet-stream",
    ".json": "application/json",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".svg": "image/svg+xml",
    ".ico": "image/x-icon",
}


class Seat:
    def __init__(self, client: str, name: str, cpu: bool = False) -> None:
        self.client = client
        self.name = name
        self.cpu = cpu


class UnityTable:
    def __init__(self, leader: str, name: str, players: int) -> None:
        self.id = secrets.token_hex(3)
        self.players = players if players in (3, 4) else 3
        self.phase = "recruiting"
        self.idle_at = time.monotonic()
        self.humans = [Seat(leader, name or "あなた")]

    def touch(self) -> None:
        self.idle_at = time.monotonic()

    def seated(self) -> int:
        return len(self.humans)

    def leader_name(self) -> str:
        return self.humans[0].name if self.humans else ""

    def leader_id(self) -> str:
        return self.humans[0].client if self.humans else ""

    def has(self, client: str) -> bool:
        return any(seat.client == client for seat in self.humans)

    def summary(self) -> dict:
        return {
            "id": self.id,
            "leader": self.leader_name(),
            "players": self.players,
            "seated": self.seated(),
            "status": "募集中" if self.phase == "recruiting" else "対局中",
            "observers": 0,
        }

    def recruiting(self, client: str) -> dict:
        return {
            "phase": "recruiting",
            "table_id": self.id,
            "event_n": 1,
            "players": self.players,
            "seats": [
                {"name": seat.name, "leader": seat.client == self.leader_id()}
                for seat in self.humans
            ],
            "cpus": [f"CPU{n + 1}" for n in range(max(0, self.players - self.seated()))],
            "you": {
                "seat": next((i for i, seat in enumerate(self.humans) if seat.client == client), None),
                "observer": False,
                "joined": self.has(client),
                "leader": client == self.leader_id(),
            },
        }


class UnityHall:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.tables: dict[str, UnityTable] = {}
        self.where: dict[str, str] = {}

    def sweep(self) -> None:
        now = time.monotonic()
        dead = [tid for tid, table in self.tables.items() if now - table.idle_at >= 180]
        for tid in dead:
            self.tables.pop(tid, None)
            for client, loc in list(self.where.items()):
                if loc == tid:
                    del self.where[client]

    def snapshot(self, client: str) -> dict:
        self.sweep()
        table = self.tables.get(self.where.get(client, ""))
        if table is None:
            self.where.pop(client, None)
            return {"phase": "hall", "tables": [item.summary() for item in self.tables.values()]}
        table.touch()
        if table.phase == "recruiting":
            return table.recruiting(client)
        return {"phase": "playing", "table_id": table.id}

    def create(self, body: dict, client: str) -> dict:
        self.leave(client)
        players = int(body.get("players") or 3)
        table = UnityTable(client, str(body.get("name") or "あなた"), players)
        self.tables[table.id] = table
        self.where[client] = table.id
        return table.recruiting(client)

    def join(self, body: dict, client: str) -> dict:
        self.sweep()
        table = self.tables.get(str(body.get("table") or ""))
        if table is None or table.phase != "recruiting":
            raise ValueError("その卓はありません")
        if table.seated() >= table.players:
            raise ValueError("席がありません")
        if not table.has(client):
            table.humans.append(Seat(client, str(body.get("name") or "あなた")))
        self.where[client] = table.id
        table.touch()
        return table.recruiting(client)

    def leave(self, client: str) -> dict:
        tid = self.where.pop(client, "")
        table = self.tables.get(tid)
        if table is None:
            return self.snapshot(client)
        table.humans = [seat for seat in table.humans if seat.client != client]
        if not table.humans:
            self.tables.pop(tid, None)
        else:
            table.touch()
        return self.snapshot(client)

    def set_players(self, body: dict, client: str) -> dict:
        table = self._require(client)
        if table.leader_id() != client:
            raise ValueError("リーダーだけが変えられます")
        players = int(body.get("players") or table.players)
        if players not in (3, 4):
            raise ValueError("人数は3か4です")
        if players < table.seated():
            raise ValueError("参加者より少なくはできません")
        table.players = players
        table.touch()
        return table.recruiting(client)

    def begin(self, client: str) -> dict:
        table = self._require(client)
        if table.leader_id() != client:
            raise ValueError("リーダーだけが開始できます")
        table.phase = "playing"
        table.touch()
        return {"phase": "playing", "table_id": table.id}

    def _require(self, client: str) -> UnityTable:
        table = self.tables.get(self.where.get(client, ""))
        if table is None:
            raise ValueError("卓に入っていません")
        return table


HALL = UnityHall()
DIAGNOSTICS: list[dict] = []


def content_encoding(path: Path) -> str:
    if path.suffix == ".br":
        return "br"
    if path.suffix == ".gz":
        return "gzip"
    if path.name.endswith(".unityweb") and path.read_bytes()[:2] == b"\x1f\x8b":
        return "gzip"
    return ""


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, directory: str, **kwargs):
        super().__init__(*args, directory=directory, **kwargs)

    def do_GET(self) -> None:
        if self.path.startswith("/api/"):
            self.api()
            return
        super().do_GET()

    def do_POST(self) -> None:
        if self.path.startswith("/api/"):
            self.api()
            return
        self.send_error(404)

    def api(self) -> None:
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length else b"{}"
        client = self.headers.get("X-Quota-Client", "")
        try:
            body = json.loads(raw.decode() or "{}")
            with HALL.lock:
                if self.command == "GET" and self.path == "/api/diagnostic":
                    payload = {"events": DIAGNOSTICS[-100:]}
                elif self.command == "POST" and self.path == "/api/diagnostic":
                    event = {
                        "at": time.time(),
                        "client": self.client_address[0],
                        "user_agent": self.headers.get("User-Agent", ""),
                        **body,
                    }
                    DIAGNOSTICS.append(event)
                    print("WebGL diagnostic:", json.dumps(event, ensure_ascii=False), flush=True)
                    payload = {"ok": True}
                elif self.command == "GET" and self.path == "/api/state":
                    payload = HALL.snapshot(client)
                elif self.path == "/api/table":
                    payload = HALL.create(body, client)
                elif self.path == "/api/join":
                    payload = HALL.join(body, client)
                elif self.path == "/api/leave":
                    payload = HALL.leave(client)
                elif self.path == "/api/players":
                    payload = HALL.set_players(body, client)
                elif self.path == "/api/start":
                    payload = HALL.begin(client)
                elif self.path == "/api/shuffle":
                    payload = HALL.snapshot(client)
                else:
                    self.send_error(404)
                    return
            self.json(payload)
        except (ValueError, KeyError, json.JSONDecodeError) as error:
            self.json({"error": str(error)}, status=400)

    def json(self, payload: dict, status: int = 200) -> None:
        data = json.dumps(payload, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def end_headers(self) -> None:
        path = Path(self.translate_path(self.path.split("?", 1)[0]))
        if path.is_file():
            encoding = content_encoding(path)
            if encoding:
                self.send_header("Content-Encoding", encoding)
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def guess_type(self, path: str) -> str:
        name = Path(path)
        if name.suffix in (".gz", ".br", ".unityweb"):
            name = Path(name.stem)
        if name.suffix == ".wasm":
            return "application/wasm"
        if name.suffix == ".js":
            return "application/javascript"
        return TYPES.get(name.suffix, "application/octet-stream")

    def log_message(self, fmt: str, *args) -> None:
        return


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=PORT)
    parser.add_argument("--dir", type=Path, default=DEFAULT_DIR)
    args = parser.parse_args()
    folder = args.dir.resolve()
    if not folder.is_dir():
        raise SystemExit(f"WebGL export is not built yet: {folder}")
    server = ThreadingHTTPServer(("0.0.0.0", args.port), lambda *a, **k: Handler(*a, directory=str(folder), **k))
    print(f"http://127.0.0.1:{args.port}  （Unity WebGL）")
    server.serve_forever()


if __name__ == "__main__":
    main()

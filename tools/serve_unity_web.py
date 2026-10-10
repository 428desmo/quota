"""Serve the Unity WebGL export and the Unity lobby.

Port 8080 serves Unity and its authoritative multiplayer action history.
"""

from __future__ import annotations

import argparse
from collections import Counter, OrderedDict, deque
import json
import os
import resource
import secrets
import threading
import time
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DIR = ROOT / "unity" / "Builds" / "WebGL"
PORT = 8080
LATENCY_BUCKETS_MS = (1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 5000)


class ServerMetrics:
    """Bounded, process-local counters; no client names or request bodies."""

    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.started_at = time.time()
        self.routes: dict[str, dict] = {}
        self.cpu_decisions = 0
        self.cpu_decision_ms = 0.0
        self.cpu_decision_max_ms = 0.0
        self.inflight = 0
        self.peak_inflight = 0

    def request_started(self) -> None:
        with self.lock:
            self.inflight += 1
            self.peak_inflight = max(self.peak_inflight, self.inflight)

    def request_finished(self) -> None:
        with self.lock:
            self.inflight -= 1

    def observe(self, route: str, status: int, duration_ms: float,
                lock_wait_ms: float, lock_held_ms: float,
                serialization_ms: float, response_bytes: int) -> None:
        with self.lock:
            row = self.routes.setdefault(route, {
                "requests": 0, "statuses": Counter(), "duration_ms_total": 0.0,
                "duration_ms_max": 0.0, "duration_ms_buckets": [0] * (len(LATENCY_BUCKETS_MS) + 1),
                "lock_wait_ms_total": 0.0, "lock_wait_ms_max": 0.0,
                "lock_wait_ms_buckets": [0] * (len(LATENCY_BUCKETS_MS) + 1),
                "lock_held_ms_total": 0.0, "lock_held_ms_max": 0.0,
                "serialization_ms_total": 0.0, "response_bytes": 0,
            })
            row["requests"] += 1
            row["statuses"][str(status)] += 1
            row["duration_ms_total"] += duration_ms
            row["duration_ms_max"] = max(row["duration_ms_max"], duration_ms)
            bucket = next((i for i, bound in enumerate(LATENCY_BUCKETS_MS) if duration_ms <= bound), len(LATENCY_BUCKETS_MS))
            row["duration_ms_buckets"][bucket] += 1
            wait_bucket = next((i for i, bound in enumerate(LATENCY_BUCKETS_MS) if lock_wait_ms <= bound), len(LATENCY_BUCKETS_MS))
            row["lock_wait_ms_buckets"][wait_bucket] += 1
            for key, value in (("lock_wait", lock_wait_ms), ("lock_held", lock_held_ms)):
                row[f"{key}_ms_total"] += value
                row[f"{key}_ms_max"] = max(row[f"{key}_ms_max"], value)
            row["serialization_ms_total"] += serialization_ms
            row["response_bytes"] += response_bytes

    def observe_cpu(self, duration_ms: float) -> None:
        with self.lock:
            self.cpu_decisions += 1
            self.cpu_decision_ms += duration_ms
            self.cpu_decision_max_ms = max(self.cpu_decision_max_ms, duration_ms)

    def snapshot(self) -> dict:
        with self.lock:
            return {
                "started_at": self.started_at,
                "latency_buckets_ms": LATENCY_BUCKETS_MS,
                "routes": {key: {**row, "statuses": dict(row["statuses"]),
                                 "duration_ms_buckets": row["duration_ms_buckets"][:],
                                 "lock_wait_ms_buckets": row["lock_wait_ms_buckets"][:]} for key, row in self.routes.items()},
                "cpu": {"decisions": self.cpu_decisions, "decision_ms_total": self.cpu_decision_ms,
                        "decision_ms_max": self.cpu_decision_max_ms},
                "inflight_api": self.inflight,
                "peak_inflight_api": self.peak_inflight,
            }


METRICS = ServerMetrics()


class TimedLock:
    def __init__(self, lock: threading.Lock, result: dict) -> None:
        self.lock = lock
        self.result = result

    def __enter__(self) -> None:
        waiting_at = time.perf_counter()
        self.lock.acquire()
        self.entered_at = time.perf_counter()
        self.result["wait_ms"] = (self.entered_at - waiting_at) * 1000

    def __exit__(self, *_error) -> None:
        self.result["held_ms"] = (time.perf_counter() - self.entered_at) * 1000
        self.lock.release()

sys.path.insert(0, str(ROOT))
from quota.characters import pick_cast, display_name, seat_cast, mind_for, bind_replacement
from quota.engine import Game, GameConfig, TakeQuota, Collect, Abandon, Pass

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
        self.departed = False
        self.resume_after = None
        self.last_seen = time.monotonic()


class UnityTable:
    def __init__(self, leader: str, name: str, players: int) -> None:
        self.id = secrets.token_hex(3)
        self.players = players if players in (3, 4) else 3
        self.phase = "recruiting"
        self.idle_at = time.monotonic()
        self.host = Seat(leader, name or "あなた")
        self.members = {leader: self.host}
        self.humans = [self.host]
        self.cpu_cast = pick_cast(self.players)
        self.seed = secrets.randbelow(2147483647)
        self.options = {}
        self.game = None
        self.actions = []
        self.cpu_at = 0.0
        self.turn_key = None
        self.turn_deadline = 0.0
        self.round_at = None

    def touch(self) -> None:
        self.idle_at = time.monotonic()

    def seated(self) -> int:
        return len(self.humans)

    def participating_humans(self) -> int:
        # CPU substitution is temporary; only EXIT removes a participant.
        return sum(seat.client in self.members and (not seat.departed or seat.resume_after is not None) for seat in self.humans)

    def leader_name(self) -> str:
        return self.host.name

    def leader_id(self) -> str:
        return self.host.client

    def has(self, client: str) -> bool:
        return client in self.members

    def summary(self, client="") -> dict:
        active_humans = {seat.client for seat in self.humans if not seat.cpu}
        result = {
            "id": self.id,
            "leader": self.leader_name(),
            "players": self.players,
            "seated": len(active_humans),
            "status": "募集中" if self.phase == "recruiting" else "対局中",
            "observers": len(set(self.members) - active_humans),
        }

        if any(seat.client == client and seat.departed for seat in self.humans) and self.game and not self.game.finished:
            result["rejoin"] = True
        return result

    def recruiting(self, client: str) -> dict:
        return {
            "phase": self.phase,
            "awaiting_next_round": bool(self.game and self.game.awaiting_next_round),
            "finished": bool(self.game and self.game.finished),
            "current_seat": self.game.current if self.game else -1,
            "seed": self.seed,
            "options": self.options,
            "actions": self.actions,
            "cpu_cast": self.cpu_cast[:max(0, self.players - self.seated())],
            "table_id": self.id,
            "event_n": 1,
            "turn_timeout_active": bool(self.game and not self.game.finished and not self.game.awaiting_next_round and self.game.players[self.game.current].is_human),
            "turn_remaining": max(0.0, self.turn_deadline - time.monotonic()),
            "players": self.players,
            "seats": [
                {"name": seat.name, "leader": seat.client == self.leader_id(), "cpu": seat.cpu, "departed": seat.departed}
                for seat in self.humans
            ],
            "cpus": [display_name(cid) for cid in self.cpu_cast[:max(0, self.players - self.seated())]],
            "you": {
                "seat": next((i for i, seat in enumerate(self.humans) if seat.client == client), -1),
                "observer": not any(seat.client == client and not seat.cpu for seat in self.humans),
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
            return {"phase": "hall", "tables": [item.summary(client) for item in self.tables.values()]}
        table.touch()
        if client in table.members:
            table.members[client].last_seen = time.monotonic()
        if table.phase == "playing":
            self.pump_cpu(table)
        return table.recruiting(client)

    def create(self, body: dict, client: str) -> dict:
        self.leave(client)
        players = int(body.get("players") or 3)
        table = UnityTable(client, str(body.get("name") or "あなた"), players)
        table.options = {key: body.get(key, default) for key, default in {"simple": False, "sequence": True, "title": True, "special": True, "ok_timeout": 5, "turn_timeout": 30, "round_mode": 1}.items()}
        if table.options["round_mode"] not in (0, 1, 2):
            raise ValueError("ラウンド数の指定が不正です")
        table.options.update(simple=False, sequence=True, title=True, special=True)
        if body.get("sit_out"):
            table.humans.clear()
        self.tables[table.id] = table
        self.where[client] = table.id
        return table.recruiting(client)

    def join(self, body: dict, client: str) -> dict:
        self.sweep()
        table = self.tables.get(str(body.get("table") or ""))
        if table is None or table.phase not in ("recruiting", "playing"):
            raise ValueError("その卓はありません")
        if not table.has(client):
            seat = next((seat for seat in table.humans if seat.client == client and seat.departed), None)
            if seat is not None and table.game and not table.game.finished:
                seat.resume_after = (table.game.round_index, table.game.turn_number, table.game.current)
            else:
                seat = Seat(client, str(body.get("name") or "あなた"))
            seat.last_seen = time.monotonic()
            table.members[client] = seat
            if table.phase == "recruiting" and table.seated() < table.players:
                table.humans.append(seat)
        self.where[client] = table.id
        table.touch()
        if table.game is not None:
            self.update_deadline(table)
        return table.recruiting(client)

    def leave(self, client: str) -> dict:
        tid = self.where.pop(client, "")
        table = self.tables.get(tid)
        if table is None:
            return self.snapshot(client)
        if table.phase == "playing":
            for index, seat in enumerate(table.humans):
                if seat.client == client:
                    seat.departed = True
                    seat.resume_after = None
                    table.actions.append(f"away:{index}")
                    self.replace_human(table, index)
            table.members.pop(client, None)
            if table.host.client == client and table.members:
                table.host = next(iter(table.members.values()))
            table.touch()
            self.update_deadline(table)
            return self.snapshot(client)
        table.members.pop(client, None)
        table.humans = [seat for seat in table.humans if seat.client != client]
        if not table.members:
            self.tables.pop(tid, None)
        else:
            if table.host.client == client: table.host = next(iter(table.members.values()))
            table.touch()
        return self.snapshot(client)

    def participation(self, body: dict, client: str) -> dict:
        table = self._require(client)
        if table.phase != "recruiting":
            raise ValueError("募集中だけ変更できます")
        seated = any(seat.client == client for seat in table.humans)
        table.options.update(simple=False, sequence=True, title=True, special=True)
        if body.get("sit_out"):
            table.humans = [seat for seat in table.humans if seat.client != client]
        elif not seated:
            if table.seated() >= table.players:
                raise ValueError("席がありません")
            seat = table.members[client]
            if client == table.leader_id(): table.humans.insert(0, seat)
            else: table.humans.append(seat)
        table.touch()
        return table.recruiting(client)

    def set_players(self, body: dict, client: str) -> dict:
        table = self._require(client)
        if table.leader_id() != client:
            raise ValueError("リーダーだけが変えられます")
        players = int(body.get("players") or table.players)
        if players not in (3, 4):
            raise ValueError("人数は3か4です")
        if players < table.seated():
            raise ValueError("参加者より少なくはできません")
        if table.phase != "recruiting":
            raise ValueError("対局中は人数を変更できません")
        table.players = players
        if len(table.cpu_cast) < players:
            table.cpu_cast.extend(pick_cast(players - len(table.cpu_cast), used=table.cpu_cast))
        table.touch()
        return table.recruiting(client)

    def settings(self, body: dict, client: str) -> dict:
        table = self._require(client)
        if table.phase != "recruiting" or table.leader_id() != client:
            raise ValueError("募集中のリーダーだけが変更できます")
        import math
        values = {}
        for key, minimum in (("ok_timeout", 0), ("turn_timeout", 1)):
            value = float(body.get(key, table.options[key]))
            if not math.isfinite(value) or value < minimum:
                raise ValueError("タイムアウトの値が不正です")
            values[key] = value
        mode = body.get("round_mode", table.options["round_mode"])
        if mode not in (0, 1, 2):
            raise ValueError("ラウンド数の指定が不正です")
        values["round_mode"] = mode
        table.options.update(values)
        table.touch()
        return table.recruiting(client)

    def begin(self, client: str, body: dict | None = None) -> dict:
        table = self._require(client)
        if table.leader_id() != client:
            raise ValueError("リーダーだけが開始できます")
        if table.phase != "recruiting":
            return table.recruiting(client)
        if body:
            if body.get("round_mode", table.options["round_mode"]) not in (0, 1, 2):
                raise ValueError("ラウンド数の指定が不正です")
            table.options.update({k: body[k] for k in table.options if k in body})
        table.options.update(simple=False, sequence=True, title=True, special=True)
        options = table.options
        table.game = Game.start(GameConfig(
            num_players=table.players, seed=table.seed,
            names=[seat.name for seat in table.humans] + [display_name(cid) for cid in table.cpu_cast[:table.players-table.seated()]],
            human_seats=list(range(table.seated())),
            sequence_rule=options["sequence"], title_rule=options["title"],
            special_actions_rule=options["special"], rounds=(1 if options["round_mode"] == 0 else table.players * (2 if options["round_mode"] == 2 else 1)),
        ))
        seat_cast(table.game, table.cpu_cast)
        table.cpu_at = time.monotonic() + 0.7
        table.phase = "playing"
        self.update_deadline(table)
        table.touch()
        return table.recruiting(client)

    def action(self, body: dict, client: str) -> dict:
        table = self._require(client)
        game = table.game
        if table.phase != "playing" or game is None:
            raise ValueError("対局は始まっていません")
        if body.get("revision") != len(table.actions):
            raise ValueError("盤面が更新されています")
        key = str(body.get("key") or "")
        if key == "next_round":
            if client != table.leader_id():
                raise ValueError("リーダーだけがラウンドを進められます")
        elif game.finished or game.awaiting_next_round or game.current >= table.seated() or not game.players[game.current].is_human or table.humans[game.current].client != client:
            raise ValueError("あなたの手番ではありません")
        self.apply_action(table, key)
        table.cpu_at = time.monotonic() + 0.7
        self.update_deadline(table)
        return table.recruiting(client)

    @staticmethod
    def apply_action(table, key):
        game = table.game
        if key == "double": game.declare_double()
        elif key == "reshuffle": game.declare_reshuffle()
        elif key == "cancel_double": game.cancel_double()
        elif key == "next_round": game.begin_next_round()
        elif key == "pass": game.step(Pass())
        elif key == "abandon": game.step(Abandon())
        elif key.startswith("take:"): game.step(TakeQuota(int(key[5:])))
        elif key.startswith("collect:"): game.step(Collect(tuple(int(n) for n in key[8:].split(","))))
        else: raise ValueError("不明な行動です")
        table.actions.append(key)

    @staticmethod
    def update_deadline(table):
        game = table.game
        key = (game.round_index, game.turn_number, game.current)
        if key != table.turn_key:
            table.turn_key = key
            table.turn_deadline = time.monotonic() + max(1, float(table.options["turn_timeout"]))
            if game.current < len(table.humans):
                seat = table.humans[game.current]
                if seat.resume_after is not None and key != seat.resume_after and seat.client in table.members and time.monotonic() - seat.last_seen <= 5:
                    seat.cpu = False
                    seat.departed = False
                    seat.resume_after = None
                    game.players[game.current].is_human = True
                    table.actions.append(f"human:{game.current}")

    @staticmethod
    def replace_human(table, index):
        player = table.game.players[index]
        if not player.is_human:
            return
        player.is_human = False
        table.humans[index].cpu = True
        bind_replacement(player)
        table.actions.append(f"cpu:{index}")
        table.cpu_at = time.monotonic() + 0.7

    def pump_cpu(self, table):
        game = table.game
        if game is None or game.finished:
            return
        if game.awaiting_next_round:
            # Continue even when the original human leader has left the table.
            if table.round_at is None:
                table.round_at = time.monotonic()
            if table.host.client not in self.where:
                if time.monotonic() >= table.round_at + max(0, float(table.options["ok_timeout"])):
                    self.apply_action(table, "next_round")
                    table.round_at = None
                    self.update_deadline(table)
            return
        table.round_at = None
        self.update_deadline(table)
        if game.players[game.current].is_human and time.monotonic() >= table.turn_deadline:
            table.actions.append(f"timeout:{game.current}")
            self.replace_human(table, game.current)
            table.humans[game.current].resume_after = table.turn_key

        game = table.game
        if game is None or game.finished or game.awaiting_next_round or game.players[game.current].is_human or time.monotonic() < table.cpu_at:
            return
        plan = game.plan
        thinking_at = time.perf_counter()
        try:
            action = mind_for(game.players[game.current]).choose(game)
        finally:
            METRICS.observe_cpu((time.perf_counter() - thinking_at) * 1000)
        if game.plan != plan:
            table.actions.append(game.plan)
        if isinstance(action, TakeQuota): key = "take:" + str(action.card_id)
        elif isinstance(action, Collect): key = "collect:" + ",".join(map(str, action.card_ids))
        elif isinstance(action, Abandon): key = "abandon"
        else: key = "pass"
        self.apply_action(table, key)
        table.cpu_at = time.monotonic() + 0.7

    def _require(self, client: str) -> UnityTable:
        table = self.tables.get(self.where.get(client, ""))
        if table is None:
            raise ValueError("卓に入っていません")
        return table


HALL = UnityHall()
DIAGNOSTICS: deque[dict] = deque(maxlen=200)
CLIENT_STATES: OrderedDict[str, tuple] = OrderedDict()


def diagnostic_signature(payload: dict) -> tuple:
    """Ignore the changing countdown and avoid retaining complete game states."""
    return (payload.get("phase"), payload.get("table_id"),
            len(payload.get("actions", ())),
            tuple((item.get("id"), item.get("status"), item.get("seated"))
                  for item in payload.get("tables", ())),
            tuple((item.get("name"), item.get("cpu"), item.get("departed"))
                  for item in payload.get("seats", ())),
            str(payload.get("options", {})))


def current_rss_bytes() -> int | None:
    try:
        with open("/proc/self/statm", encoding="ascii") as statm:
            return int(statm.read().split()[1]) * os.sysconf("SC_PAGE_SIZE")
    except (OSError, IndexError, ValueError):
        return None


def linux_listen_counters() -> dict | None:
    try:
        with open("/proc/net/netstat", encoding="ascii") as netstat:
            lines = netstat.readlines()
        for header, values in zip(lines, lines[1:]):
            if header.startswith("TcpExt:") and values.startswith("TcpExt:"):
                counters = dict(zip(header.split()[1:], values.split()[1:]))
                return {"listen_overflows": int(counters["ListenOverflows"]),
                        "listen_drops": int(counters["ListenDrops"])}
    except (OSError, KeyError, ValueError):
        pass
    return None


def metrics_snapshot() -> dict:
    result = METRICS.snapshot()
    with HALL.lock:
        tables = list(HALL.tables.values())
        result["hall"] = {
            "tables": len(tables),
            "playing_tables": sum(table.phase == "playing" for table in tables),
            "connected_clients": len(HALL.where),
            "action_history_entries": sum(len(table.actions) for table in tables),
            "diagnostic_entries": len(DIAGNOSTICS),
            "diagnostic_clients": len(CLIENT_STATES),
        }
    usage = resource.getrusage(resource.RUSAGE_SELF)
    result["process"] = {
        "rss_bytes": current_rss_bytes(),
        "peak_rss_bytes": usage.ru_maxrss * (1024 if sys.platform.startswith("linux") else 1),
        "cpu_user_seconds": usage.ru_utime,
        "cpu_system_seconds": usage.ru_stime,
        "threads": threading.active_count(),
    }
    result["kernel_tcp"] = linux_listen_counters()
    return result


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
        if self.path.split("?", 1)[0].startswith("/StreamingAssets/"):
            event = {
                "at": time.time(),
                "client": self.client_address[0],
                "user_agent": self.headers.get("User-Agent", ""),
                "id": "",
                "stage": "static-get",
                "detail": self.path,
            }
            DIAGNOSTICS.append(event)
            print("WebGL diagnostic:", json.dumps(event, ensure_ascii=False), flush=True)
        super().do_GET()

    def do_POST(self) -> None:
        if self.path.startswith("/api/"):
            self.api()
            return
        self.send_error(404)

    def api(self) -> None:
        started_at = time.perf_counter()
        status = 200
        response_bytes = 0
        serialization_ms = 0.0
        lock_timing = {"wait_ms": 0.0, "held_ms": 0.0}
        route = self.path if self.path in {
            "/api/state", "/api/table", "/api/join", "/api/leave", "/api/participation",
            "/api/players", "/api/settings", "/api/action", "/api/start", "/api/shuffle",
            "/api/diagnostic", "/api/metrics",
        } else "/api/unknown"
        if self.path != "/api/metrics":
            METRICS.request_started()
        try:
            if self.path == "/api/metrics":
                # Nginx overrides X-Real-IP for forwarded requests; only local reads are allowed.
                forwarded = self.headers.get("X-Real-IP")
                if self.command != "GET" or (forwarded and forwarded not in ("127.0.0.1", "::1")) or self.client_address[0] not in ("127.0.0.1", "::1"):
                    status = 403
                    response_bytes, serialization_ms = self.json({"error": "local access only"}, status)
                else:
                    response_bytes, serialization_ms = self.json(metrics_snapshot())
                return
            length = int(self.headers.get("Content-Length") or 0)
            raw = self.rfile.read(length) if length else b"{}"
            client = self.headers.get("X-Quota-Client", "")
            body = json.loads(raw.decode() or "{}")
            with TimedLock(HALL.lock, lock_timing):
                if self.command == "GET" and self.path == "/api/diagnostic":
                    payload = {"events": list(DIAGNOSTICS)[-100:]}
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
                elif self.path == "/api/participation":
                    payload = HALL.participation(body, client)
                elif self.path == "/api/players":
                    payload = HALL.set_players(body, client)
                elif self.path == "/api/settings":
                    payload = HALL.settings(body, client)
                elif self.path == "/api/action":
                    payload = HALL.action(body, client)
                elif self.path == "/api/start":
                    payload = HALL.begin(client, body)
                elif self.path == "/api/shuffle":
                    table = HALL._require(client)
                    if table.phase != "recruiting" or table.leader_id() != client:
                        raise ValueError("募集中のリーダーだけが変更できます")
                    table.cpu_cast = pick_cast(table.players)
                    payload = HALL.snapshot(client)
                else:
                    status = 404
                    self.send_error(404)
                    return
                if self.path != "/api/diagnostic":
                    signature = diagnostic_signature(payload)
                    changed = CLIENT_STATES.get(client) != signature
                    if self.command == "POST" or changed:
                        CLIENT_STATES[client] = signature
                        CLIENT_STATES.move_to_end(client)
                        if len(CLIENT_STATES) > 2048:
                            CLIENT_STATES.popitem(last=False)
                        event = {
                            "at": time.time(),
                            "client": self.client_address[0],
                            "user_agent": self.headers.get("User-Agent", ""),
                            "id": client,
                            "stage": f"api-{self.command.lower()} {self.path}",
                            "detail": {"phase": payload.get("phase"), "table_id": payload.get("table_id"),
                                       "actions": len(payload.get("actions", ()))},
                        }
                        DIAGNOSTICS.append(event)
                        if self.command == "POST":
                            print("WebGL diagnostic:", json.dumps(event, ensure_ascii=False), flush=True)
            response_bytes, serialization_ms = self.json(payload)
        except (ValueError, KeyError, json.JSONDecodeError) as error:
            status = 400
            response_bytes, serialization_ms = self.json({"error": str(error)}, status)
        except Exception:
            status = 500
            raise
        finally:
            if self.path != "/api/metrics":
                METRICS.observe(f"{self.command} {route}", status,
                                (time.perf_counter() - started_at) * 1000,
                                lock_timing["wait_ms"], lock_timing["held_ms"], serialization_ms, response_bytes)
                METRICS.request_finished()

    def json(self, payload: dict, status: int = 200) -> tuple[int, float]:
        serialization_at = time.perf_counter()
        data = json.dumps(payload, ensure_ascii=False).encode()
        serialization_ms = (time.perf_counter() - serialization_at) * 1000
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)
        return len(data), serialization_ms

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


class QuotaHTTPServer(ThreadingHTTPServer):
    # Nginx opens a fresh upstream connection for each poll; the default queue of
    # five can overflow during simultaneous table polling even when CPU is idle.
    request_queue_size = 128


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0", help="Listen address (use 127.0.0.1 behind Nginx)")
    parser.add_argument("--port", type=int, default=PORT)
    parser.add_argument("--dir", type=Path, default=DEFAULT_DIR)
    args = parser.parse_args()
    folder = args.dir.resolve()
    if not folder.is_dir():
        raise SystemExit(f"WebGL export is not built yet: {folder}")
    server = QuotaHTTPServer((args.host, args.port), lambda *a, **k: Handler(*a, directory=str(folder), **k))
    print(f"http://127.0.0.1:{args.port}  （Unity WebGL）")
    server.serve_forever()


if __name__ == "__main__":
    main()

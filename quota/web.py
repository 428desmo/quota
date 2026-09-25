"""LAN web table. One shared game, any browser on the network can act."""

from __future__ import annotations

import json
import secrets
import threading
import time
from urllib.parse import quote
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from quota.ai import choose_action
from quota.engine import Abandon, Collect, Game, GameConfig, Pass, TakeQuota
from quota.items import catalog, resolve_item_set

ROOT = Path(__file__).resolve().parent.parent / "web"


class Table:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.game: Game | None = None
        self.phase = "lobby"
        self.event_n = 0
        self.event: dict | None = None
        self.hold_until = 0.0
        self.settling_seat: int | None = None
        self.last_options: dict | None = None
        self.seat_owner: dict[int, str] = {}
        self.seat_acked: set[int] = set()
        self.notice_at: float | None = None
        self.gate_released = False
        self.ok_timeout = 3.0
        self.left_handed = False
        self.refresh_hold: list | None = None
        self.refresh_acked: set[int] = set()
        self.refresh_notice_at: float | None = None
        self.refresh_released = False
        self.observers: list[dict] = []
        self.roster: list[dict] = []
        self.capacity = 3
        self.turn_timeout = 30.0
        self.turn_deadline: float | None = None
        self.cover: dict | None = None
        self.cover_n = 0
        self.idle_at = time.monotonic()
        self.table_id = ""
        self.seed = None
        self.item_set_id = "trade"

    def touch(self) -> None:
        self.idle_at = time.monotonic()

    def open(self, body: dict, client_id: str) -> None:
        players = int(body.get("players", 3))
        if players not in (3, 4):
            raise ValueError("人数は3か4です")
        if not client_id:
            raise ValueError("参加できません")
        self.capacity = players
        self.ok_timeout = _seconds(body.get("ok_timeout"), 3, minimum=0)
        self.turn_timeout = _seconds(body.get("turn_timeout"), 30, minimum=1)
        self.left_handed = bool(body.get("left_handed"))
        self.seed = body.get("seed")
        theme = resolve_item_set(str(body.get("item_set") or "trade"))
        self.item_set_id = theme.id
        self.last_options = {
            "players": players,
            "sequence": bool(body.get("sequence")),
            "title": bool(body.get("title")),
            "item_set": theme.id,
            "ok_timeout": self.ok_timeout,
            "turn_timeout": self.turn_timeout,
            "left_handed": self.left_handed,
        }
        self.phase = "recruiting"
        self.game = None
        self.roster = [{
            "client": client_id,
            "name": _clean_name(body.get("name"), "あなた"),
            "joined_at": time.monotonic(),
        }]
        self.observers = []
        self.seat_owner = {}
        self.touch()

    def begin(self, client_id: str) -> None:
        if self.phase != "recruiting":
            raise ValueError("募集中ではありません")
        if self.leader_id() != client_id:
            raise ValueError("リーダーだけが開始できます")
        if not self.roster:
            raise ValueError("参加者がいません")
        players = self.capacity
        humans = len(self.roster)
        names = [member["name"] for member in self.roster]
        names += [f"CPU{i + 1}" for i in range(players - humans)]
        theme = resolve_item_set(self.item_set_id)
        seed = self.seed
        self.seed = None
        self.game = Game.start(
            GameConfig(
                num_players=players,
                seed=None if seed in (None, "") else int(seed),
                names=names,
                human_seats=list(range(humans)),
                sequence_rule=bool(self.last_options and self.last_options.get("sequence")),
                title_rule=bool(self.last_options and self.last_options.get("title")),
                item_set=theme.id,
            )
        )
        self.phase = "playing"
        self.event = None
        self.event_n += 1
        self.hold_until = 0.0
        self.settling_seat = None
        self.seat_owner = {i: self.roster[i]["client"] for i in range(humans)}
        self.turn_deadline = None
        self.touch()
        self.seat_acked = set()
        self.notice_at = None
        self.gate_released = False
        self.refresh_hold = None
        self.refresh_acked = set()
        self.refresh_notice_at = None
        self.refresh_released = False

    def again(self, client_id: str) -> None:
        if self.phase != "finished":
            raise ValueError("対局はまだ終わっていません")
        if not any(member["client"] == client_id for member in self.roster):
            raise ValueError("参加者ではありません")
        self.game = None
        self.phase = "recruiting"
        self.event = None
        self.event_n += 1
        self.hold_until = 0.0
        self.settling_seat = None
        self.seat_owner = {}
        self.seat_acked = set()
        self.notice_at = None
        self.gate_released = False
        self.refresh_hold = None
        self.refresh_acked = set()
        self.refresh_notice_at = None
        self.refresh_released = False
        self.turn_deadline = None
        self.touch()

    def admit(self, client_id: str, name: str) -> None:
        if not client_id:
            raise ValueError("参加できません")
        name = _clean_name(name, "あなた")
        for member in self.roster:
            if member["client"] == client_id:
                member["name"] = name
                self.touch()
                return
        for obs in self.observers:
            if obs["client"] == client_id:
                obs["name"] = name
                self.touch()
                return
        if self.phase == "recruiting" and len(self.roster) < self.capacity:
            self.roster.append({"client": client_id, "name": name, "joined_at": time.monotonic()})
        else:
            self.observers.append({"client": client_id, "name": name})
        self.touch()

    def leave(self, client_id: str) -> None:
        self.observers = [obs for obs in self.observers if obs["client"] != client_id]
        member = next((item for item in self.roster if item["client"] == client_id), None)
        if member is None:
            self.touch()
            return
        self.roster = [item for item in self.roster if item["client"] != client_id]
        seat = next((i for i, owner in self.seat_owner.items() if owner == client_id), None)
        self.seat_owner = {i: owner for i, owner in self.seat_owner.items() if owner != client_id}
        game = self.game
        if game is not None and seat is not None and self.phase in ("playing", "finished"):
            player = game.players[seat]
            player.is_human = False
            self._announce(f"{player.name}が抜けたので、CPUが代わりにプレイしました")
            if self._only_one_human():
                self.turn_deadline = None
            self._touch_gate()
            self._touch_refresh()
            if (
                self.phase == "playing"
                and not game.finished
                and game.current == seat
                and not self._settling()
                and not self._refresh_waiting()
            ):
                self.turn_deadline = None
                self._apply(choose_action(game))
        self.touch()

    def leader_id(self) -> str | None:
        if not self.roster:
            return None
        return min(self.roster, key=lambda member: member["joined_at"])["client"]

    def act(self, body: dict, client_id: str = "") -> None:
        if self._refresh_waiting():
            raise ValueError("場札の入れ替えを待っています")
        if self._settling():
            raise ValueError("実績へ移しています")
        game = self._require_playing()
        player = game.players[game.current]
        if not player.is_human:
            raise ValueError("CPU の手番です")
        kind = body.get("kind")
        if kind == "take":
            action = TakeQuota(int(body["card_id"]))
        elif kind == "collect":
            action = Collect(tuple(int(n) for n in body.get("card_ids", [])))
        elif kind == "abandon":
            action = Abandon()
        elif kind == "pass":
            action = Pass()
        else:
            raise ValueError("unknown action")
        if self.seat_owner.get(game.current) != client_id:
            raise ValueError("あなたの手番ではありません")
        self.turn_deadline = None
        self.touch()
        self._apply(action)

    def ack(self, client_id: str) -> None:
        if self._refresh_waiting():
            self._touch_refresh()
            acked = self.refresh_acked
        else:
            self._touch_gate()
            if not self._gate_waiting():
                raise ValueError("確認中ではありません")
            acked = self.seat_acked
        mine = [seat for seat, owner in self.seat_owner.items() if owner == client_id and seat not in acked]
        if not client_id or not mine:
            raise ValueError("参加者ではありません")
        acked.add(mine[0])
        self.touch()
        if self._refresh_waiting():
            self._touch_refresh()
        else:
            self._touch_gate()

    def step_cpu(self) -> None:
        game = self.game
        if self.phase != "playing" or game is None or game.finished or self._settling() or self._refresh_waiting():
            return
        if game.players[game.current].is_human:
            return
        self.turn_deadline = None
        self._apply(choose_action(game))

    def step_timeout(self) -> None:
        game = self.game
        if self.phase != "playing" or game is None or game.finished or self._settling() or self._refresh_waiting():
            self.turn_deadline = None
            return
        player = game.players[game.current]
        if not player.is_human or self._only_one_human():
            self.turn_deadline = None
            return
        now = time.monotonic()
        if self.turn_deadline is None:
            self.turn_deadline = now + self.turn_timeout
            return
        if now < self.turn_deadline:
            return
        self.turn_deadline = None
        self._announce(f"{player.name}が応答しないので、CPUが代わりにプレイしました")
        self.touch()
        self._apply(choose_action(game))

    def snapshot(self, client_id: str = "") -> dict:
        if self.phase == "recruiting" or self.game is None:
            return self._recruiting_view(client_id)
        game = self.game
        theme = resolve_item_set(game.config.item_set)
        view = {
            "phase": self.phase,
            "event_n": self.event_n,
            "event": self.event,
            "deck_count": len(game.deck),
            "turn_number": game.turn_number,
            "turn_gain": game.turn_gain,
            "player_count": len(game.players),
            "no_gain_streak": game.no_gain_streak,
            "stall_flag": game.stall_flag,
            "stall_count": 1 if game.stall_flag else 0,
            "settling": self._settling(),
            "settling_seat": self.settling_seat if self._settling() else None,
            "finished": game.finished,
            "end_reason": game.end_reason,
            "sequence_rule": game.config.sequence_rule,
            "title_rule": game.config.title_rule,
            "left_handed": self.left_handed,
            "item_set": {"id": theme.id, "name": theme.name},
            "current": game.current,
            "current_human": game.players[game.current].is_human and not game.finished and not self._settling() and not self._refresh_waiting(),
            "turn_left": self._turn_left(),
            "your_turn": self._your_turn(client_id),
            "you": self._you(client_id),
            "observers": [obs["name"] for obs in self.observers],
            "cover": self._cover_view(),
            "table_id": self.table_id,
            "ranking": game.ranking() if game.finished else [],
            "score_gate": self._score_gate(client_id),
            "refresh_gate": self._refresh_gate(client_id),
            "market": self.refresh_hold if self._refresh_waiting() else [_card(c, theme) for c in game.market],
            "players": [
                {
                    "name": p.name,
                    "human": p.is_human,
                    "score": game.final_score(p),
                    "delivery_score": p.score,
                    "sequence_bonus": game.sequence_points(p),
                    "titles": [{"name": name, "points": points} for name, points in game.title_awards(p)],
                    "achieve_count": p.achieve_count,
                    "max_single_score": p.max_single_score,
                    "quota": None if p.quota is None else _card(p.quota, theme),
                    "need": None
                    if p.quota is None or p.quota.rank is None
                    else p.quota.rank - 1 - len(p.collection),
                    "collection": [_card(c, theme) for c in p.collection],
                    "achieved": [_card(c, theme) for c in p.achieved],
                }
                for p in game.players
            ],
        }
        return view

    def _settling(self) -> bool:
        return time.monotonic() < self.hold_until

    def _require_playing(self) -> Game:
        if self.phase != "playing" or self.game is None or self.game.finished:
            raise ValueError("ゲーム中ではありません")
        return self.game

    def _apply(self, action) -> None:
        assert self.game is not None
        game = self.game
        seat = game.current
        before = {c.id: _card(c, resolve_item_set(game.config.item_set)) for c in game.market}
        if isinstance(action, TakeQuota):
            cards = [before[action.card_id]]
            kind = "take"
        elif isinstance(action, Collect):
            cards = [before[card_id] for card_id in action.card_ids]
            kind = "collect"
        elif isinstance(action, Abandon):
            kind = "abandon"
            cards = []
        else:
            kind = "pass"
            cards = []
        turn_before = game.turn_number
        reshuffles = game.reshuffle_count
        market_before = [_card(c, resolve_item_set(game.config.item_set)) for c in game.market]
        game.step(action)
        if game.reshuffle_count > reshuffles and not game.finished:
            self.refresh_hold = market_before
            self.refresh_acked = set()
            self.refresh_notice_at = time.monotonic()
            self.refresh_released = False
        self.event_n += 1
        self.event = {"n": self.event_n, "kind": kind, "seat": seat, "cards": cards}
        ended = game.turn_number != turn_before or game.finished
        achieved = {c.id for c in game.players[seat].achieved}
        filed = any(card["id"] in achieved for card in cards)
        delay = 0.0
        if filed:
            delay = 1.7 if kind == "take" else 2.1
        elif ended and cards:
            delay = 0.7
        if delay:
            self.settling_seat = seat
            self.hold_until = time.monotonic() + delay
        if game.finished:
            self.phase = "finished"
            if game.end_reason == "DECK" and self.notice_at is None:
                self.notice_at = time.monotonic()
            self._touch_gate()

    def _gate_waiting(self) -> bool:
        game = self.game
        return game is not None and game.finished and game.end_reason == "DECK" and not self.gate_released

    def _touch_gate(self) -> None:
        game = self.game
        if game is None or not game.finished or game.end_reason != "DECK" or self.gate_released:
            return
        if self.notice_at is None:
            self.notice_at = time.monotonic()
        if self._confirm_ready(self.notice_at, self.seat_acked):
            self.gate_released = True

    def _refresh_waiting(self) -> bool:
        return self.refresh_hold is not None and not self.refresh_released

    def _touch_refresh(self) -> None:
        if not self._refresh_waiting() or self.refresh_notice_at is None:
            return
        if self._confirm_ready(self.refresh_notice_at, self.refresh_acked):
            self.refresh_released = True
            self.refresh_hold = None

    def _confirm_ready(self, notice_at: float, acked: set[int]) -> bool:
        game = self.game
        if game is None:
            return False
        humans = [i for i, p in enumerate(game.players) if p.is_human]
        cpus = [i for i, p in enumerate(game.players) if not p.is_human]
        cpu_ready = not cpus or time.monotonic() >= notice_at + 0.5
        humans_ready = time.monotonic() >= notice_at + self.ok_timeout or all(i in acked for i in humans)
        return cpu_ready and humans_ready

    def _refresh_gate(self, client_id: str) -> dict | None:
        if self.refresh_hold is None and not self.refresh_released:
            return None
        self._touch_refresh()
        if self.refresh_hold is None:
            return {"released": True, "you_can_ack": False, "waiting": []}
        game = self.game
        assert game is not None
        waiting = [game.players[i].name for i in range(len(game.players)) if game.players[i].is_human and i not in self.refresh_acked]
        mine = [seat for seat, owner in self.seat_owner.items() if owner == client_id and seat not in self.refresh_acked]
        return {
            "released": False,
            "you_can_ack": bool(client_id) and bool(mine),
            "waiting": waiting,
        }

    def _you(self, client_id: str) -> dict:
        seat = next((i for i, owner in self.seat_owner.items() if owner == client_id), None)
        observer = any(obs["client"] == client_id for obs in self.observers)
        return {
            "seat": seat,
            "observer": observer,
            "joined": seat is not None or observer,
            "leader": client_id == self.leader_id() and seat is not None,
        }

    def _only_one_human(self) -> bool:
        game = self.game
        return game is not None and sum(player.is_human for player in game.players) <= 1

    def _turn_left(self) -> float | None:
        game = self.game
        if self.phase != "playing" or game is None or game.finished or self._settling() or self._refresh_waiting():
            return None
        if not game.players[game.current].is_human or self._only_one_human():
            return None
        if self.turn_deadline is None:
            return self.turn_timeout
        return max(0.0, self.turn_deadline - time.monotonic())

    def _your_turn(self, client_id: str) -> bool:
        game = self.game
        if game is None or game.finished or self._settling() or self._refresh_waiting():
            return False
        if not game.players[game.current].is_human:
            return False
        return self.seat_owner.get(game.current) == client_id

    def _score_gate(self, client_id: str) -> dict | None:
        game = self.game
        if game is None or not game.finished or game.end_reason != "DECK":
            return None
        self._touch_gate()
        waiting = [game.players[i].name for i in range(len(game.players)) if game.players[i].is_human and i not in self.seat_acked]
        mine = [seat for seat, owner in self.seat_owner.items() if owner == client_id and seat not in self.seat_acked]
        return {
            "released": self.gate_released,
            "you_can_ack": bool(client_id) and bool(mine) and not self.gate_released,
            "waiting": waiting,
        }


    def _announce(self, text: str) -> None:
        self.cover_n += 1
        self.cover = {"n": self.cover_n, "text": text, "until": time.monotonic() + 1.5}

    def _cover_view(self) -> dict | None:
        if self.cover is None or time.monotonic() >= self.cover["until"]:
            return None
        return {"n": self.cover["n"], "text": self.cover["text"]}

    def _recruiting_view(self, client_id: str) -> dict:
        leader = self.leader_id()
        seat = next((i for i, member in enumerate(self.roster) if member["client"] == client_id), None)
        observer = any(obs["client"] == client_id for obs in self.observers)
        return {
            "phase": "recruiting",
            "table_id": self.table_id,
            "event_n": self.event_n,
            "players": self.capacity,
            "seats": [
                {"name": member["name"], "leader": member["client"] == leader}
                for member in self.roster
            ],
            "observers": [obs["name"] for obs in self.observers],
            "you": {
                "seat": seat,
                "observer": observer,
                "joined": True,
                "leader": client_id == leader and seat is not None,
            },
            "item_sets": _item_set_choices(),
            "sequence_rule": bool(self.last_options and self.last_options.get("sequence")),
            "title_rule": bool(self.last_options and self.last_options.get("title")),
            "left_handed": self.left_handed,
            "ok_timeout": self.ok_timeout,
            "turn_timeout": self.turn_timeout,
        }

    def summary(self) -> dict:
        if self.phase == "recruiting" or self.game is None:
            seated = len(self.roster)
        else:
            seated = sum(1 for player in self.game.players if player.is_human)
        leader = next((member["name"] for member in self.roster if member["client"] == self.leader_id()), "")
        return {
            "id": self.table_id,
            "leader": leader,
            "players": self.capacity,
            "seated": seated,
            "status": "募集中" if self.phase == "recruiting" else "対局中",
            "observers": len(self.observers),
        }


def _clean_name(raw, fallback: str) -> str:
    text = " ".join(str(raw or "").split())[:24]
    return text or fallback


def _seconds(raw, default: float, minimum: float) -> float:
    try:
        value = float(default if raw in (None, "") else raw)
    except (TypeError, ValueError):
        raise ValueError("秒数で指定してください") from None
    if value < minimum:
        raise ValueError("秒数で指定してください")
    return value


def _item_set_choices() -> list[dict]:
    return [
        {"id": item.id, "name": item.name, "description": item.description, "default": item.default}
        for item in catalog()
    ]


def _card(card, theme) -> dict:
    face = theme.face_for(card)
    return {
        "id": card.id,
        "emoji": face.emoji,
        "face": theme.rank_label(card),
        "rank": card.rank,
        "goods": face.name,
        "color": face.color,
        "joker": card.suit == "JOKER",
    }


def _options_cookie() -> str | None:
    options = HALL.last_options
    if not options:
        return None
    raw = quote(json.dumps(options, separators=(",", ":")))
    return f"quota_options={raw}; Path=/; Max-Age=31536000; SameSite=Lax"


class Hall:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.tables: dict[str, Table] = {}
        self.where: dict[str, str] = {}
        self.last_options: dict | None = None

    def sweep(self) -> None:
        now = time.monotonic()
        dead = [tid for tid, table in self.tables.items() if now - table.idle_at >= 180]
        for tid in dead:
            self.tables.pop(tid, None)
            for client, loc in list(self.where.items()):
                if loc == tid:
                    del self.where[client]

    def _table(self, client: str) -> Table | None:
        self.sweep()
        tid = self.where.get(client)
        table = self.tables.get(tid) if tid else None
        if tid and table is None:
            self.where.pop(client, None)
        return table

    def _require(self, client: str) -> Table:
        table = self._table(client)
        if table is None:
            raise ValueError("卓に入っていません")
        return table

    def _detach(self, client: str) -> None:
        current = self.tables.get(self.where.get(client, ""))
        if current is not None:
            current.leave(client)
        self.where.pop(client, None)

    def snapshot(self, client: str) -> dict:
        table = self._table(client)
        if table is None:
            return {
                "phase": "hall",
                "item_sets": _item_set_choices(),
                "tables": [item.summary() for item in self.tables.values()],
            }
        return table.snapshot(client)

    def create(self, body: dict, client: str) -> None:
        if not client:
            raise ValueError("参加できません")
        self._detach(client)
        table = Table()
        table.table_id = secrets.token_hex(3)
        table.open(body, client)
        self.tables[table.table_id] = table
        self.where[client] = table.table_id
        self.last_options = dict(table.last_options or {})

    def join(self, body: dict, client: str) -> None:
        if not client:
            raise ValueError("参加できません")
        self.sweep()
        table = self.tables.get(str(body.get("table") or ""))
        if table is None:
            raise ValueError("その卓はありません")
        self._detach(client)
        table.admit(client, body.get("name"))
        self.where[client] = table.table_id

    def leave(self, client: str) -> None:
        table = self._table(client)
        if table is None:
            return
        table.leave(client)
        self.where.pop(client, None)

    def begin(self, client: str) -> None:
        self._require(client).begin(client)

    def act(self, body: dict, client: str) -> None:
        self._require(client).act(body, client)

    def ack(self, client: str) -> None:
        self._require(client).ack(client)

    def again(self, client: str) -> None:
        self._require(client).again(client)

    def step(self) -> None:
        self.sweep()
        for table in list(self.tables.values()):
            table.step_cpu()
            table.step_timeout()


HALL = Hall()


def _cpu_loop() -> None:
    while True:
        time.sleep(0.7)
        with HALL.lock:
            HALL.step()


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/api/state":
            with HALL.lock:
                payload = HALL.snapshot(self.headers.get("X-Quota-Client", ""))
            self._json(payload)
            return
        rel = "index.html" if self.path in ("/", "") else self.path.lstrip("/")
        path = (ROOT / rel).resolve()
        if not str(path).startswith(str(ROOT.resolve())) or not path.is_file():
            self.send_error(404)
            return
        kind = "text/html; charset=utf-8"
        if path.suffix == ".js":
            kind = "text/javascript; charset=utf-8"
        elif path.suffix == ".css":
            kind = "text/css; charset=utf-8"
        data = path.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self) -> None:
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            body = json.loads(raw.decode() or "{}")
            client = self.headers.get("X-Quota-Client", "")
            with HALL.lock:
                if self.path == "/api/table":
                    HALL.create(body, client)
                elif self.path == "/api/start":
                    HALL.begin(client)
                elif self.path == "/api/action":
                    HALL.act(body, client)
                elif self.path == "/api/join":
                    HALL.join(body, client)
                elif self.path == "/api/leave":
                    HALL.leave(client)
                elif self.path == "/api/ack":
                    HALL.ack(client)
                elif self.path == "/api/again":
                    HALL.again(client)
                else:
                    self.send_error(404)
                    return
                payload = HALL.snapshot(client)
            self._json(payload)
        except (ValueError, KeyError, json.JSONDecodeError) as exc:
            self._json({"error": str(exc)}, status=400)

    def _json(self, payload: dict, status: int = 200) -> None:
        data = json.dumps(payload, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        cookie = _options_cookie()
        if cookie:
            self.send_header("Set-Cookie", cookie)
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, fmt: str, *args) -> None:
        return


def main() -> None:
    threading.Thread(target=_cpu_loop, daemon=True).start()
    server = ThreadingHTTPServer(("0.0.0.0", 8000), Handler)
    print("http://127.0.0.1:8000  （同じネットワークの他の端末からも開けます）")
    server.serve_forever()


if __name__ == "__main__":
    main()

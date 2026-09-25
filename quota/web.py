"""LAN web table. One shared game, any browser on the network can act."""

from __future__ import annotations

import json
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

    def start(self, body: dict) -> None:
        players = int(body.get("players", 3))
        humans = int(body.get("humans", players))
        if players not in (3, 4) or not 0 <= humans <= players:
            raise ValueError("players must be 3 or 4, and humans within that")
        seed = body.get("seed")
        theme = resolve_item_set(str(body.get("item_set") or "trade"))
        names = [f"席{i + 1}" if i < humans else f"CPU{i - humans + 1}" for i in range(players)]
        if humans == 1:
            names[0] = "あなた"
        self.game = Game.start(
            GameConfig(
                num_players=players,
                seed=None if seed in (None, "") else int(seed),
                names=names,
                human_seats=list(range(humans)),
                sequence_rule=bool(body.get("sequence")),
                item_set=theme.id,
            )
        )
        self.phase = "playing"
        self.event = None
        self.event_n += 1
        self.hold_until = 0.0
        self.settling_seat = None
        self.last_options = {
            "players": players,
            "humans": humans,
            "sequence": bool(body.get("sequence")),
            "item_set": theme.id,
        }

    def reset(self) -> None:
        self.game = None
        self.phase = "lobby"
        self.event = None
        self.event_n += 1
        self.hold_until = 0.0
        self.settling_seat = None

    def act(self, body: dict) -> None:
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
        self._apply(action)

    def step_cpu(self) -> None:
        game = self.game
        if self.phase != "playing" or game is None or game.finished or self._settling():
            return
        if game.players[game.current].is_human:
            return
        self._apply(choose_action(game))

    def snapshot(self) -> dict:
        if self.game is None:
            return {
                "phase": "lobby",
                "event_n": self.event_n,
                "event": self.event,
                "item_sets": _item_set_choices(),
            }
        game = self.game
        theme = resolve_item_set(game.config.item_set)
        view = {
            "phase": self.phase,
            "event_n": self.event_n,
            "event": self.event,
            "deck_count": len(game.deck),
            "turn_number": game.turn_number,
            "player_count": len(game.players),
            "no_gain_streak": game.no_gain_streak,
            "stall_flag": game.stall_flag,
            "stall_count": 1 if game.stall_flag else 0,
            "settling": self._settling(),
            "settling_seat": self.settling_seat if self._settling() else None,
            "finished": game.finished,
            "end_reason": game.end_reason,
            "sequence_rule": game.config.sequence_rule,
            "item_set": {"id": theme.id, "name": theme.name},
            "current": game.current,
            "current_human": game.players[game.current].is_human and not game.finished and not self._settling(),
            "market": [_card(c, theme) for c in game.market],
            "ranking": game.ranking() if game.finished else [],
            "players": [
                {
                    "name": p.name,
                    "human": p.is_human,
                    "score": game.final_score(p),
                    "delivery_score": p.score,
                    "sequence_bonus": game.sequence_points(p),
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
        game.step(action)
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
    options = TABLE.last_options
    if not options:
        return None
    raw = quote(json.dumps(options, separators=(",", ":")))
    return f"quota_options={raw}; Path=/; Max-Age=31536000; SameSite=Lax"


TABLE = Table()


def _cpu_loop() -> None:
    while True:
        time.sleep(0.7)
        with TABLE.lock:
            TABLE.step_cpu()


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/api/state":
            with TABLE.lock:
                payload = TABLE.snapshot()
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
            with TABLE.lock:
                if self.path == "/api/start":
                    TABLE.start(body)
                elif self.path == "/api/action":
                    TABLE.act(body)
                elif self.path == "/api/reset":
                    TABLE.reset()
                else:
                    self.send_error(404)
                    return
                payload = TABLE.snapshot()
            self._json(payload)
        except (ValueError, KeyError, json.JSONDecodeError) as exc:
            self._json({"error": str(exc)}, status=400)

    def _json(self, payload: dict, status: int = 200) -> None:
        data = json.dumps(payload, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
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

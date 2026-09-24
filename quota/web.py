"""LAN web table. One shared game, any browser on the network can act."""

from __future__ import annotations

import json
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from quota.ai import choose_action
from quota.cards import SUIT_NAME
from quota.engine import Abandon, Collect, Game, GameConfig, Pass, TakeQuota

ROOT = Path(__file__).resolve().parent.parent / "web"
EMOJI = {"S": "🌶️", "H": "🎀", "D": "💎", "C": "🍵", "JOKER": "🪙"}


class Table:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.game: Game | None = None
        self.phase = "lobby"
        self.event_n = 0
        self.event: dict | None = None

    def start(self, body: dict) -> None:
        players = int(body.get("players", 3))
        humans = int(body.get("humans", players))
        if players not in (3, 4) or not 0 <= humans <= players:
            raise ValueError("players must be 3 or 4, and humans within that")
        seed = body.get("seed")
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
            )
        )
        self.phase = "playing"
        self.event = None
        self.event_n += 1

    def reset(self) -> None:
        self.game = None
        self.phase = "lobby"
        self.event = None
        self.event_n += 1

    def act(self, body: dict) -> None:
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
        if self.phase != "playing" or game is None or game.finished:
            return
        if game.players[game.current].is_human:
            return
        self._apply(choose_action(game))

    def snapshot(self) -> dict:
        if self.game is None:
            return {"phase": "lobby", "event_n": self.event_n, "event": self.event}
        game = self.game
        view = {
            "phase": self.phase,
            "event_n": self.event_n,
            "event": self.event,
            "deck_count": len(game.deck),
            "turn_number": game.turn_number,
            "no_gain_streak": game.no_gain_streak,
            "stall_flag": game.stall_flag,
            "finished": game.finished,
            "end_reason": game.end_reason,
            "sequence_rule": game.config.sequence_rule,
            "current": game.current,
            "current_human": game.players[game.current].is_human and not game.finished,
            "market": [_card(c) for c in game.market],
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
                    "quota": None if p.quota is None else _card(p.quota),
                    "need": None
                    if p.quota is None or p.quota.rank is None
                    else p.quota.rank - 1 - len(p.collection),
                    "collection": [_card(c) for c in p.collection],
                    "achieved": [_card(c) for c in p.achieved],
                }
                for p in game.players
            ],
        }
        return view

    def _require_playing(self) -> Game:
        if self.phase != "playing" or self.game is None or self.game.finished:
            raise ValueError("ゲーム中ではありません")
        return self.game

    def _apply(self, action) -> None:
        assert self.game is not None
        game = self.game
        seat = game.current
        before = {c.id: _card(c) for c in game.market}
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
        game.step(action)
        self.event_n += 1
        self.event = {"n": self.event_n, "kind": kind, "seat": seat, "cards": cards}
        if game.finished:
            self.phase = "finished"


def _card(card) -> dict:
    face = "＊" if card.rank is None else str(card.rank)
    return {
        "id": card.id,
        "emoji": EMOJI[card.suit],
        "face": face,
        "goods": SUIT_NAME[card.suit],
        "joker": card.suit == "JOKER",
    }


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

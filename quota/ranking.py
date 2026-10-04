"""CPU character ranking.

The evaluation is keyed by the strategy dimensions, not by the display name and
not by the packed character id, so the table survives a change to the naming
words or to how the id is packed. Adding a strategy keeps every stored key
meaningful as long as the existing numbers keep their meaning.

A rating is Elo over random matches. Each match applies one pairwise update per
seat pair, so the updates are zero sum and the mean rating stays at the start
value. A combination that has never played is given that mean.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

from quota.characters import (
    DENIAL_COUNT,
    STRATEGY_COUNT,
    TITLE_STANCE_COUNT,
    TRIGGER_COUNT,
    decode,
)

FORMAT = "quota-cpu-ranking"
VERSION = 1
RANKING_PATH = Path(__file__).resolve().parent.parent / "cpu_ranking_v1.0.json"

DIMENSIONS = ("before", "trigger", "after", "stance", "denial")

START_RATING = 1500.0
K_FACTOR = 24.0
SPREAD = 400.0


def dimension_sizes() -> tuple[int, ...]:
    return (STRATEGY_COUNT, TRIGGER_COUNT, STRATEGY_COUNT, TITLE_STANCE_COUNT, DENIAL_COUNT)


def key_of(dims) -> str:
    return "-".join(str(value) for value in dims)


def dims_of(key: str) -> tuple[int, ...]:
    return tuple(int(part) for part in key.split("-"))


def key_for(character_id: int) -> str:
    return key_of(decode(character_id))


@dataclass
class Entry:
    rating: float
    games: int = 0
    points: float = 0.0
    rank: int = 0

    @property
    def share(self) -> float:
        """Win share, counting a tie as a half win."""
        return self.points / self.games if self.games else 0.0


@dataclass
class Table:
    start: float = START_RATING
    k: float = K_FACTOR
    spread: float = SPREAD
    matches: int = 0
    runs: list[dict] = field(default_factory=list)
    entries: dict[str, Entry] = field(default_factory=dict)

    def mean(self) -> float:
        if not self.entries:
            return self.start
        return sum(entry.rating for entry in self.entries.values()) / len(self.entries)

    def entry_for(self, key: str) -> Entry:
        entry = self.entries.get(key)
        if entry is None:
            entry = Entry(rating=self.mean())
            self.entries[key] = entry
        return entry

    def record(self, keys, scores) -> None:
        """Fold one finished match into the table."""
        count = len(keys)
        if count < 2 or count != len(scores):
            raise ValueError("a match needs a score for two or more seats")
        seats = [self.entry_for(key) for key in keys]
        ratings = [entry.rating for entry in seats]
        deltas = [0.0] * count
        shares = [0.0] * count
        for left in range(count):
            for right in range(left + 1, count):
                expected = 1.0 / (1.0 + 10.0 ** ((ratings[right] - ratings[left]) / self.spread))
                if scores[left] > scores[right]:
                    actual = 1.0
                elif scores[left] < scores[right]:
                    actual = 0.0
                else:
                    actual = 0.5
                step = self.k * (actual - expected) / (count - 1)
                deltas[left] += step
                deltas[right] -= step
                shares[left] += actual
                shares[right] += 1.0 - actual
        for entry, delta, share in zip(seats, deltas, shares):
            entry.rating += delta
            entry.points += share / (count - 1)
            entry.games += 1
        self.matches += 1

    def rerank(self) -> None:
        order = sorted(self.entries.items(), key=lambda pair: (-pair[1].rating, dims_of(pair[0])))
        place = 0
        held = None
        for index, (_key, entry) in enumerate(order, start=1):
            if held is None or entry.rating != held:
                place = index
                held = entry.rating
            entry.rank = place

    def provisional_rank(self) -> int | None:
        """The place a never-played combination takes with the mean rating."""
        if not self.entries:
            return None
        middle = self.mean()
        return 1 + sum(1 for entry in self.entries.values() if entry.rating > middle)

    def rank_for(self, character_id: int) -> int | None:
        entry = self.entries.get(key_for(character_id))
        if entry is not None and entry.rank:
            return entry.rank
        return self.provisional_rank()

    def to_json(self) -> dict:
        self.rerank()
        rows = sorted(self.entries.items(), key=lambda pair: (pair[1].rank, dims_of(pair[0])))
        return {
            "format": FORMAT,
            "version": VERSION,
            "system": "elo",
            "dimensions": list(DIMENSIONS),
            "sizes": list(dimension_sizes()),
            "start": self.start,
            "k": self.k,
            "spread": self.spread,
            "mean": round(self.mean(), 2),
            "provisional": self.provisional_rank() or 0,
            "matches": self.matches,
            "count": len(self.entries),
            "runs": self.runs,
            "players": [
                {
                    "id": list(dims_of(key)),
                    "rating": round(entry.rating, 1),
                    "games": entry.games,
                    "points": round(entry.points, 1),
                    "rank": entry.rank,
                }
                for key, entry in rows
            ],
        }


def from_json(data: dict) -> Table:
    if data.get("format") != FORMAT:
        raise ValueError("not a CPU ranking file")
    if data.get("version") != VERSION:
        raise ValueError(f"unsupported ranking version {data.get('version')}")
    if list(data.get("dimensions", ())) != list(DIMENSIONS):
        raise ValueError("the ranking file names different dimensions")
    table = Table(
        start=float(data.get("start", START_RATING)),
        k=float(data.get("k", K_FACTOR)),
        spread=float(data.get("spread", SPREAD)),
        matches=int(data.get("matches", 0)),
        runs=list(data.get("runs", [])),
    )
    for row in data.get("players", []):
        table.entries[key_of(row["id"])] = Entry(
            rating=float(row["rating"]),
            games=int(row.get("games", 0)),
            points=float(row.get("points", 0.0)),
            rank=int(row.get("rank", 0)),
        )
    return table


def dumps(table: Table) -> str:
    """One line per player so a rerun shows a readable diff."""
    data = table.to_json()
    players = data.pop("players")
    head = ",\n".join(
        f"  {json.dumps(name, ensure_ascii=False)}: {json.dumps(value, ensure_ascii=False)}"
        for name, value in data.items()
    )
    rows = ",\n".join(
        "    " + json.dumps(player, ensure_ascii=False, separators=(", ", ": "))
        for player in players
    )
    return "{\n" + head + ',\n  "players": [\n' + rows + "\n  ]\n}\n"


def save(table: Table, path: Path = RANKING_PATH) -> None:
    path.write_text(dumps(table), encoding="utf-8")


def load(path: Path = RANKING_PATH) -> Table:
    return from_json(json.loads(path.read_text(encoding="utf-8")))


_table: Table | None = None
_tried = False


def table() -> Table | None:
    """The shipped table, read once. None when the file is missing."""
    global _table, _tried
    if not _tried:
        _tried = True
        try:
            _table = load()
        except (OSError, ValueError):
            _table = None
    return _table


def forget() -> None:
    global _table, _tried
    _table = None
    _tried = False


def rank_for(character_id: int) -> int | None:
    held = table()
    return held.rank_for(character_id) if held is not None else None

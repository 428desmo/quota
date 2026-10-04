"""CPU character ranking.

The evaluation is keyed by the strategy dimensions, not by the display name and
not by the packed character id, so the table survives a change to the naming
words or to how the id is packed. Adding a strategy keeps every stored key
meaningful as long as the existing numbers keep their meaning.

A match hands out place points: 3 to the winner, 2 to the runner up, nothing
below. Seats that tie share the points of the places they fill, so one match
always hands out the same total. The evaluation value is the points a
combination has taken divided by the matches it has played, which makes the
average over all combinations a constant. A combination that has never played
is given that average.
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
VERSION = 2
RANKING_PATH = Path(__file__).resolve().parent.parent / "cpu_ranking_v1.1.json"

DIMENSIONS = ("before", "trigger", "after", "stance", "denial")

AWARD = (3.0, 2.0)
SEATS = 4


def dimension_sizes() -> tuple[int, ...]:
    return (STRATEGY_COUNT, TRIGGER_COUNT, STRATEGY_COUNT, TITLE_STANCE_COUNT, DENIAL_COUNT)


def award_slots(seats: int) -> list[float]:
    """The points each place takes, longest first, padded out with zeroes."""
    return [AWARD[place] if place < len(AWARD) else 0.0 for place in range(seats)]


def mean_value(seats: int = SEATS) -> float:
    """The average value, which the award rule holds constant."""
    return sum(award_slots(seats)) / seats


def key_of(dims) -> str:
    return "-".join(str(value) for value in dims)


def dims_of(key: str) -> tuple[int, ...]:
    return tuple(int(part) for part in key.split("-"))


def key_for(character_id: int) -> str:
    return key_of(decode(character_id))


@dataclass
class Entry:
    value: float
    games: int = 0
    points: float = 0.0
    rank: int = 0


@dataclass
class Table:
    seats: int = SEATS
    matches: int = 0
    runs: list[dict] = field(default_factory=list)
    entries: dict[str, Entry] = field(default_factory=dict)

    def mean(self) -> float:
        return mean_value(self.seats)

    def entry_for(self, key: str) -> Entry:
        entry = self.entries.get(key)
        if entry is None:
            entry = Entry(value=self.mean())
            self.entries[key] = entry
        return entry

    def awards(self, scores) -> list[float]:
        """Place points for one match, with a tie sharing the places it fills."""
        count = len(scores)
        slots = award_slots(count)
        order = sorted(range(count), key=lambda seat: -scores[seat])
        given = [0.0] * count
        head = 0
        while head < count:
            tail = head
            while tail + 1 < count and scores[order[tail + 1]] == scores[order[head]]:
                tail += 1
            pot = sum(slots[head:tail + 1]) / (tail - head + 1)
            for place in range(head, tail + 1):
                given[order[place]] = pot
            head = tail + 1
        return given

    def record(self, keys, scores) -> None:
        """Fold one finished match into the table."""
        count = len(keys)
        if count < 2 or count != len(scores):
            raise ValueError("a match needs a score for two or more seats")
        seats = [self.entry_for(key) for key in keys]
        for entry, given in zip(seats, self.awards(scores)):
            entry.points += given
            entry.games += 1
            entry.value = entry.points / entry.games
        self.matches += 1

    def rerank(self) -> None:
        order = sorted(self.entries.items(), key=lambda pair: (-pair[1].value, dims_of(pair[0])))
        place = 0
        held = None
        for index, (_key, entry) in enumerate(order, start=1):
            if held is None or entry.value != held:
                place = index
                held = entry.value
            entry.rank = place

    def provisional_rank(self) -> int | None:
        """The place a never-played combination takes with the average value."""
        if not self.entries:
            return None
        middle = self.mean()
        return 1 + sum(1 for entry in self.entries.values() if entry.value > middle)

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
            "system": "place-points",
            "dimensions": list(DIMENSIONS),
            "sizes": list(dimension_sizes()),
            "award": list(AWARD),
            "seats": self.seats,
            "mean": round(self.mean(), 4),
            "provisional": self.provisional_rank() or 0,
            "matches": self.matches,
            "count": len(self.entries),
            "runs": self.runs,
            "players": [
                {
                    "id": list(dims_of(key)),
                    "value": round(entry.value, 4),
                    "games": entry.games,
                    "points": round(entry.points, 3),
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
    if list(data.get("award", AWARD)) != list(AWARD):
        raise ValueError("the ranking file was scored by another award rule")
    table = Table(
        seats=int(data.get("seats", SEATS)),
        matches=int(data.get("matches", 0)),
        runs=list(data.get("runs", [])),
    )
    for row in data.get("players", []):
        table.entries[key_of(row["id"])] = Entry(
            value=float(row["value"]),
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

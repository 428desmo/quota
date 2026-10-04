"""Rate CPU characters against each other and write the ranking file.

Every character cannot be tried against every other, so seats are drawn
uniformly at random. One run adds matches to the table that is already on
disk, so the ranking gets steadier each time this is run.

    python3 tools/rate_cpu.py --matches 30000 --seed 1

The framework below is fixed so that runs stay comparable. Four seats over
four rounds means every seat leads once, which keeps the first-player
advantage out of the rating.
"""

from __future__ import annotations

import argparse
import datetime
import json
import random
import sys
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from quota.ai import choose_action  # noqa: E402
from quota.characters import Mind, bind, character_count, character_name  # noqa: E402
from quota.engine import Game, GameConfig  # noqa: E402
from quota.ranking import RANKING_PATH, Table, dumps, key_for, load, save  # noqa: E402

FRAMEWORK = {
    "players": 4,
    "rounds": 4,
    "sequence_rule": True,
    "title_rule": True,
    "special_actions_rule": True,
}

STREAMING = ROOT / "unity" / "Assets" / "StreamingAssets"
GUARD = 20000


def main() -> None:
    parser = argparse.ArgumentParser(description="rate CPU characters by random matches")
    parser.add_argument("--matches", type=int, default=30000, help="matches to add in this run")
    parser.add_argument("--seed", type=int, default=1, help="seed for the draw and the deals")
    parser.add_argument("--out", type=Path, default=None, help="ranking file to update")
    parser.add_argument("--fresh", action="store_true", help="start from an empty table")
    args = parser.parse_args()

    path = args.out or RANKING_PATH
    table = Table() if args.fresh or not path.exists() else load(path)
    before = dict((key, entry.rating) for key, entry in table.entries.items())

    rng = random.Random(args.seed)
    for index in range(args.matches):
        play(table, rng)
        if (index + 1) % 2000 == 0:
            print(f"{index + 1}/{args.matches} matches", flush=True)

    table.runs.append({
        "matches": args.matches,
        "seed": args.seed,
        "at": datetime.date.today().isoformat(),
        **FRAMEWORK,
    })
    save(table, path)
    mirror(path)
    report(table, before)


def play(table: Table, rng: random.Random) -> None:
    seats = FRAMEWORK["players"]
    characters = rng.sample(range(character_count()), seats)
    game = Game.start(GameConfig(
        num_players=seats,
        seed=rng.randrange(1 << 30),
        human_seats=[],
        names=[character_name(character) for character in characters],
        rounds=FRAMEWORK["rounds"],
        sequence_rule=FRAMEWORK["sequence_rule"],
        title_rule=FRAMEWORK["title_rule"],
        special_actions_rule=FRAMEWORK["special_actions_rule"],
    ))
    for player, character in zip(game.players, characters):
        bind(player, Mind(character))
    guard = 0
    while not game.finished:
        if game.awaiting_next_round:
            game.begin_next_round()
            continue
        game.step(choose_action(game))
        guard += 1
        if guard > GUARD:
            raise RuntimeError("a match did not finish")
    scores = [game.final_score(player) for player in game.players]
    table.record([key_for(character) for character in characters], scores)


def mirror(path: Path) -> None:
    """Unity reads the same file out of StreamingAssets."""
    if path != RANKING_PATH or not STREAMING.is_dir():
        return
    copy = STREAMING / path.name
    copy.write_text(path.read_text(encoding="utf-8"), encoding="utf-8")
    meta = STREAMING / f"{path.name}.meta"
    if meta.exists():
        return
    meta.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {uuid.uuid4().hex}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n",
        encoding="utf-8",
    )


def report(table: Table, before: dict) -> None:
    data = json.loads(dumps(table))
    players = data["players"]
    played = [entry.games for entry in table.entries.values()]
    moved = 0.0
    for key, entry in table.entries.items():
        moved = max(moved, abs(entry.rating - before.get(key, table.start)))
    print(f"matches {table.matches}, combinations {len(players)}")
    print(f"games per combination {min(played)}-{max(played)}, biggest move this run {moved:.1f}")
    for label, rows in (("top", players[:10]), ("bottom", players[-10:])):
        print(label)
        for row in rows:
            print(f"  {row['rank']:>5} {row['rating']:>7.1f} {tuple(row['id'])} games {row['games']}")


if __name__ == "__main__":
    main()

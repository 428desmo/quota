"""Seat-rotated round-robin league for six independent experimental CPUs."""

from __future__ import annotations

import argparse
import itertools
import json
import random
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tools.cpu_character_policy import LEVELS, STYLES  # noqa: E402
from tools.cpu_character_simulator import run_match  # noqa: E402
from tools.tactic_simulator import confidence_interval, mean  # noqa: E402


def evaluate(*, roster: tuple[dict, ...], players: int, rounds: int,
             deals_per_cast: int, seed: int, guard_actions: int = 20_000) -> dict:
    if players not in (3, 4) or rounds < 1 or deals_per_cast < 1 or len(roster) < players:
        raise ValueError("invalid league setup")
    ids = [entry["id"] for entry in roster]
    if len(set(ids)) != len(ids) or any(
        entry["style"] not in STYLES or entry["inference"] not in LEVELS for entry in roster
    ):
        raise ValueError("roster ids must be unique; styles and levels must be valid")
    rng = random.Random(seed)
    blocks = defaultdict(lambda: defaultdict(list))
    totals = defaultdict(Counter)
    titles = defaultdict(Counter)
    trials = []
    for cast in itertools.combinations(roster, players):
        for repeat in range(deals_per_cast):
            deal_seed = rng.randrange(1 << 30)
            order = list(cast)
            rng.shuffle(order)
            block = defaultdict(lambda: defaultdict(list))
            for rotation in range(players):
                placed = order[rotation:] + order[:rotation]
                specs = {
                    seat: (entry["style"], entry["inference"])
                    for seat, entry in enumerate(placed)
                }
                match = run_match(deal_seed, tuple(0 for _ in range(players)), specs,
                                  rounds, guard_actions)
                for seat, entry in enumerate(placed):
                    key = entry["id"]
                    block[key]["score"].append(match["scores"][seat])
                    block[key]["win_share"].append(match["win_shares"][seat])
                    block[key]["margin"].append(match["margins"][seat])
                    totals[key].update(match["policy_stats"][str(seat)])
                    titles[key].update(match["title_counts"][seat])
                trials.append({
                    "cast": [entry["id"] for entry in cast], "repeat": repeat,
                    "seed": deal_seed, "rotation": rotation,
                    "seats": [entry["id"] for entry in placed],
                    "scores": match["scores"], "win_shares": match["win_shares"],
                })
            for key, fields in block.items():
                for field, values in fields.items():
                    blocks[key][field].append(mean(values))
    summary = {}
    for entry in roster:
        key = entry["id"]
        score = blocks[key]["score"]
        summary[key] = {
            "style": entry["style"], "inference": entry["inference"],
            "independent_casts": len(score),
            "games": len(score) * players,
            "mean_score": mean(score),
            "score_95ci_by_cast": confidence_interval(score),
            "mean_win_share": mean(blocks[key]["win_share"]),
            "win_share_95ci_by_cast": confidence_interval(blocks[key]["win_share"]),
            "mean_margin": mean(blocks[key]["margin"]),
            "contested_quota_rate": (
                totals[key]["contested_quota"] / sum(
                    totals[key][f"quota_{band}"] for band in ("1", "2_6", "7_9", "10_13")
                ) if totals[key]["quota_rank_total"] else 0.0
            ),
            "mono_quota_count": totals[key]["mono_quota"],
            "gold_collected": totals[key]["gold_collected"],
            "title_counts": dict(titles[key]),
            "policy_stats": dict(totals[key]),
        }
    return {
        "config": {
            "players": players, "rounds": rounds,
            "deals_per_cast": deals_per_cast, "seed": seed,
            "roster": roster, "guard_actions": guard_actions,
        },
        "summary": summary, "trials": trials,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--roster", type=Path, required=True,
                        help="JSON list of {id, style, inference} entries")
    parser.add_argument("--players", type=int, choices=(3, 4), default=3)
    parser.add_argument("--rounds", type=int, default=None)
    parser.add_argument("--deals-per-cast", type=int, default=10)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--guard-actions", type=int, default=20_000)
    parser.add_argument("--json-out", type=Path)
    args = parser.parse_args()
    roster = tuple(json.loads(args.roster.read_text(encoding="utf-8")))
    result = evaluate(
        roster=roster, players=args.players, rounds=args.rounds or args.players,
        deals_per_cast=args.deals_per_cast, seed=args.seed,
        guard_actions=args.guard_actions,
    )
    print(f"{len(result['trials'])} games; {args.players} seats × {len(roster)} characters")
    for key, row in sorted(result["summary"].items(),
                           key=lambda item: item[1]["mean_win_share"], reverse=True):
        print(f"{key:16} score={row['mean_score']:.1f} "
              f"win_share={row['mean_win_share']:.3f} "
              f"contested={row['contested_quota_rate']:.1%} "
              f"CI={row['win_share_95ci_by_cast']}")
    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                                 encoding="utf-8")
        print(f"wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

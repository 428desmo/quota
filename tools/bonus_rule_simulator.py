"""Compare proposed bonus rules with six experimental CPUs in paired matches.

Each cast, initial seed and seat rotation is replayed under every rule. CPU
policies see the tested bonus rules and may change their choices; the live game
engine, roster and old ranking remain untouched.
"""

from __future__ import annotations

import argparse
import itertools
import json
import random
import sys
from collections import Counter, defaultdict
from dataclasses import asdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tools.bonus_rules import BonusRules  # noqa: E402
from tools.cpu_character_simulator import run_match  # noqa: E402
from tools.tactic_simulator import confidence_interval, mean  # noqa: E402


RULES = {
    "current": BonusRules(),
    "variety_3pt": BonusRules(variety_points=3),
    "variety_twice": BonusRules(variety_copies=2),
    "variety_thrice": BonusRules(variety_copies=3),
    "variety_8cards": BonusRules(variety_min_cards=8),
    "variety_10cards": BonusRules(variety_min_cards=10),
    "variety_15cards": BonusRules(variety_min_cards=15),
    "variety_20cards": BonusRules(variety_min_cards=20),
    "achievement_from_6": BonusRules(achievement_min=6),
    "achievement_from_5": BonusRules(achievement_min=5),
    "variety_twice_and_from_6": BonusRules(variety_copies=2, achievement_min=6),
}


def evaluate(*, roster: tuple[dict, ...], players: int, rounds: int,
             deals_per_cast: int, seed: int,
             rules: dict[str, BonusRules] = RULES) -> dict:
    if players not in (3, 4) or rounds < 1 or deals_per_cast < 1 or len(roster) < players:
        raise ValueError("invalid experiment setup")
    if not rules or "current" not in rules or rules["current"] != BonusRules():
        raise ValueError("a current-rule control is required")
    rng = random.Random(seed)
    totals = defaultdict(lambda: defaultdict(Counter))
    blocks = defaultdict(lambda: defaultdict(lambda: defaultdict(list)))
    trials = 0
    for cast in itertools.combinations(roster, players):
        for _repeat in range(deals_per_cast):
            deal_seed = rng.randrange(1 << 30)
            order = list(cast)
            rng.shuffle(order)
            rotation_rows = defaultdict(lambda: defaultdict(lambda: defaultdict(list)))
            for rotation in range(players):
                placed = order[rotation:] + order[:rotation]
                specs = {seat: (entry["style"], entry["inference"])
                         for seat, entry in enumerate(placed)}
                results = {
                    name: run_match(deal_seed, (0,) * players, specs, rounds,
                                    bonus_rules=variant)
                    for name, variant in rules.items()
                }
                control = results["current"]
                for name, result in results.items():
                    for seat, entry in enumerate(placed):
                        key = entry["id"]
                        data = totals[name][key]
                        data["games"] += 1
                        data["score"] += result["scores"][seat]
                        data["win_share"] += result["win_shares"][seat]
                        data["variety"] += result["title_counts"][seat].get("五種の品揃え", 0)
                        data["mono"] += result["title_counts"][seat].get("単色達成", 0)
                        data["purist"] += result["title_counts"][seat].get("生粋の買い付け", 0)
                        stats = result["policy_stats"][str(seat)]
                        data["quota_count"] += sum(stats.get(f"quota_{band}", 0)
                                                   for band in ("1", "2_6", "7_9", "10_13"))
                        data["quota_rank_total"] += stats.get("quota_rank_total", 0)
                        data["quota_10_13"] += stats.get("quota_10_13", 0)
                        rotation_rows[name][key]["score_delta"].append(
                            result["scores"][seat] - control["scores"][seat])
                        rotation_rows[name][key]["win_share_delta"].append(
                            result["win_shares"][seat] - control["win_shares"][seat])
                trials += 1
            for name, characters in rotation_rows.items():
                for key, fields in characters.items():
                    for metric, values in fields.items():
                        blocks[name][key][metric].append(mean(values))
    summary = {}
    for name, characters in totals.items():
        per_character = {}
        for key, data in characters.items():
            games = data["games"]
            block = blocks[name][key]
            per_character[key] = {
                "games": games,
                "mean_score": data["score"] / games,
                "mean_win_share": data["win_share"] / games,
                "variety_per_100_player_rounds": 100 * data["variety"] / (games * rounds),
                "mono_per_100_player_rounds": 100 * data["mono"] / (games * rounds),
                "purist_per_100_player_rounds": 100 * data["purist"] / (games * rounds),
                "mean_quota_rank": data["quota_rank_total"] / data["quota_count"],
                "high_quota_rate": data["quota_10_13"] / data["quota_count"],
                "paired_score_delta": mean(block["score_delta"]),
                "paired_score_delta_95ci": confidence_interval(block["score_delta"]),
                "paired_win_share_delta": mean(block["win_share_delta"]),
                "paired_win_share_delta_95ci": confidence_interval(block["win_share_delta"]),
            }
        summary[name] = {
            "rules": asdict(rules[name]),
            "mean_score_across_characters": mean([row["mean_score"] for row in per_character.values()]),
            "variety_per_100_player_rounds": mean([
                row["variety_per_100_player_rounds"] for row in per_character.values()]),
            "characters": per_character,
        }
    return {
        "config": {"players": players, "rounds": rounds,
                   "deals_per_cast": deals_per_cast, "seed": seed,
                   "casts": len(list(itertools.combinations(roster, players))),
                   "matches_per_variant": trials, "roster": roster},
        "summary": summary,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--roster", type=Path, default=Path("docs/cpu_characters_experimental.json"))
    parser.add_argument("--players", type=int, choices=(3, 4), default=3)
    parser.add_argument("--rounds", type=int)
    parser.add_argument("--deals-per-cast", type=int, default=8)
    parser.add_argument("--seed", type=int, default=20261011)
    parser.add_argument("--json-out", type=Path)
    args = parser.parse_args()
    roster = tuple(json.loads(args.roster.read_text(encoding="utf-8")))
    result = evaluate(roster=roster, players=args.players,
                      rounds=args.rounds or args.players,
                      deals_per_cast=args.deals_per_cast, seed=args.seed)
    print(f"{result['config']['matches_per_variant']} matches × {len(RULES)} rule sets")
    for name, row in result["summary"].items():
        print(f"{name:26} score={row['mean_score_across_characters']:.2f} "
              f"variety={row['variety_per_100_player_rounds']:.2f}/100 player-rounds")
    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                                 encoding="utf-8")
        print(f"wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

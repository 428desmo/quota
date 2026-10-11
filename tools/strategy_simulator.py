"""Paired, seat-balanced ablation of strategic priority-change triggers.

Run from the repository root, for example:

    python3 tools/strategy_simulator.py --deals 20 --players 3 --rounds 3 \
        --seed 42 --json-out /tmp/quota-strategy.json

This is an offline experiment. It never updates the CPU ranking or live AI.
"""

from __future__ import annotations

import argparse
import json
import random
import sys
from collections import Counter
from dataclasses import asdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from quota.characters import character_count  # noqa: E402
from tools.strategy_policies import MODES, PROFILES, StrategyPolicy  # noqa: E402
from tools.tactic_simulator import confidence_interval, mean, run_game  # noqa: E402


def evaluate(*, deals: int, seed: int, players: int, rounds: int,
             profile: str = "balanced",
             guard_actions: int = 20_000) -> dict:
    if deals < 1 or players not in (3, 4) or rounds < 1:
        raise ValueError("deals must be positive, players must be 3 or 4, rounds must be positive")
    if profile not in PROFILES:
        raise ValueError(f"unknown profile {profile!r}")
    rng = random.Random(seed)
    trials = []
    blocks = {mode: {"score": [], "win_share": [], "margin": []} for mode in MODES}
    aggregates = {mode: Counter() for mode in MODES}
    reasons = {mode: Counter() for mode in MODES}
    specials = {mode: Counter() for mode in MODES}
    for deal in range(deals):
        deal_seed = rng.randrange(1 << 30)
        characters = tuple(rng.sample(range(character_count()), players))
        deal_deltas = {mode: {"score": [], "win_share": [], "margin": []} for mode in MODES}
        for focal in range(players):
            control = run_game(deal_seed, characters, focal, rounds, None, guard_actions)
            variants = {}
            for mode in MODES:
                policy = StrategyPolicy(mode, profile)
                result = run_game(
                    deal_seed, characters, focal, rounds, lambda policy=policy: policy, guard_actions
                )
                report = policy.report()
                variants[mode] = {
                    "result": asdict(result),
                    "audit": report,
                    "score_delta": result.score - control.score,
                    "win_share_delta": result.win_share - control.win_share,
                    "margin_delta": result.margin - control.margin,
                }
                for field in ("score", "win_share", "margin"):
                    deal_deltas[mode][field].append(variants[mode][f"{field}_delta"])
                aggregates[mode].update({
                    "decisions": report["decisions"],
                    "switches": report["switches"],
                    "changed": report["changed"],
                    "applicable": result.counters.applicable,
                })
                reasons[mode].update(report["reason_counts"])
                specials[mode].update(report["manual_specials"])
            trials.append({
                "deal": deal, "seed": deal_seed, "characters": characters,
                "focal": focal, "control": asdict(control), "variants": variants,
            })
        for mode in MODES:
            for field in ("score", "win_share", "margin"):
                blocks[mode][field].append(mean(deal_deltas[mode][field]))

    summary = {}
    for mode in MODES:
        rows = [trial["variants"][mode] for trial in trials]
        summary[mode] = {
            "trials": len(rows),
            "mean_score_delta": mean([row["score_delta"] for row in rows]),
            "score_delta_95ci_by_deal": confidence_interval(blocks[mode]["score"]),
            "mean_win_share_delta": mean([row["win_share_delta"] for row in rows]),
            "win_share_delta_95ci_by_deal": confidence_interval(blocks[mode]["win_share"]),
            "mean_margin_delta": mean([row["margin_delta"] for row in rows]),
            "margin_delta_95ci_by_deal": confidence_interval(blocks[mode]["margin"]),
            "mean_achieve_count_delta": mean([
                row["result"]["achieve_count"] - trial["control"]["achieve_count"]
                for trial, row in zip(trials, rows)
            ]),
            "decisions": aggregates[mode]["decisions"],
            "applicable": aggregates[mode]["applicable"],
            "changed": aggregates[mode]["changed"],
            "switches": aggregates[mode]["switches"],
            "trigger_counts": dict(reasons[mode]),
            "manual_specials": dict(specials[mode]),
        }

    # Factorial contrasts estimate whether both trigger families interact.
    interaction = {}
    for field in ("score", "win_share", "margin"):
        by_deal = [
            blocks["combined"][field][i] - blocks["macro"][field][i]
            - blocks["tactical"][field][i] + blocks["fixed"][field][i]
            for i in range(deals)
        ]
        interaction[field] = {
            "mean": mean(by_deal), "ci_95_by_deal": confidence_interval(by_deal)
        }
    return {
        "config": {
            "deals": deals, "seed": seed, "players": players, "rounds": rounds,
            "profile": profile,
            "guard_actions": guard_actions, "modes": MODES,
            "sequence_rule": True, "title_rule": True, "special_actions_rule": True,
        },
        "summary": summary, "interaction": interaction, "trials": trials,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--deals", type=int, default=20)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--players", type=int, choices=(3, 4), default=3)
    parser.add_argument("--rounds", type=int, default=None, help="defaults to player count")
    parser.add_argument("--profile", choices=tuple(PROFILES), default="balanced")
    parser.add_argument("--guard-actions", type=int, default=20_000)
    parser.add_argument("--json-out", type=Path)
    args = parser.parse_args()
    result = evaluate(
        deals=args.deals, seed=args.seed, players=args.players,
        rounds=args.rounds or args.players, profile=args.profile,
        guard_actions=args.guard_actions,
    )
    print(f"{args.deals} deals × {args.players} seats; paired control and four trigger modes")
    for mode, row in result["summary"].items():
        print(
            f"{mode:9} score={row['mean_score_delta']:+.3f} "
            f"CI={row['score_delta_95ci_by_deal']} "
            f"win_share={row['mean_win_share_delta']:+.4f} "
            f"changed={row['changed']} switches={row['switches']}"
        )
    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

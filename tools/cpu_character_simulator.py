"""Screen independent CPU styles and deck-inference levels against legacy CPUs.

    python3 tools/cpu_character_simulator.py --deals 80 --players 3 --rounds 3 \
        --seed 42 --json-out /tmp/quota-character-screen.json
"""

from __future__ import annotations

import argparse
import json
import random
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from quota.ai import choose_action  # noqa: E402
from quota.characters import Mind, bind, character_count  # noqa: E402
from quota.engine import Game, GameConfig  # noqa: E402
from tools.cpu_character_policy import LEVELS, STYLES, CharacterPolicy  # noqa: E402
from tools.bonus_rules import BonusGame, BonusGameConfig, BonusRules  # noqa: E402
from tools.tactic_simulator import confidence_interval, mean, observe  # noqa: E402


def run_match(seed: int, legacy: tuple[int, ...],
              candidates: dict[int, tuple[str, int]], rounds: int,
              guard_actions: int = 20_000,
              bonus_rules: BonusRules | None = None) -> dict:
    config_type = BonusGameConfig if bonus_rules is not None else GameConfig
    game_type = BonusGame if bonus_rules is not None else Game
    config_options = {"bonus_rules": bonus_rules} if bonus_rules is not None else {}
    game = game_type.start(config_type(
        num_players=len(legacy), seed=seed, human_seats=[],
        names=[f"P{i + 1}" for i in range(len(legacy))],
        rounds=rounds, sequence_rule=True, title_rule=True,
        special_actions_rule=True,
        **config_options,
    ))
    policies = {seat: CharacterPolicy(*spec, bonus_rules=bonus_rules)
                for seat, spec in candidates.items()}
    for seat, character in enumerate(legacy):
        if seat not in policies:
            bind(game.players[seat], Mind(character))
    actions = 0
    title_counts = [Counter() for _ in game.players]

    def record_titles() -> None:
        for seat, player in enumerate(game.players):
            title_counts[seat].update(name for name, _ in game.title_awards(player))

    while not game.finished:
        if game.awaiting_next_round:
            record_titles()
            before = observe(game)
            game.begin_next_round()
            after = observe(game)
            for policy in policies.values():
                policy.on_transition(before, after, None, "new_round")
            continue
        if actions >= guard_actions:
            raise RuntimeError(f"game exceeded {guard_actions} actions (seed={seed})")
        before = observe(game)
        policy = policies.get(game.current)
        if policy is not None:
            special = policy.special(before)
            if special == "double":
                game.declare_double()
            elif special == "reshuffle":
                game.declare_reshuffle()
            elif special is not None:
                raise ValueError(f"unknown special: {special!r}")
            action = policy.choose(observe(game))
        else:
            action = choose_action(game)
        after_choice = observe(game)
        if after_choice != before:
            for watcher in policies.values():
                watcher.on_transition(before, after_choice, None, "declaration")
        if not game.is_legal(action):
            raise ValueError(f"illegal action from seat {game.current}: {action!r}")
        game.step(action)
        after = observe(game)
        for watcher in policies.values():
            watcher.on_transition(after_choice, after, action, "action")
        actions += 1

    record_titles()
    scores = [game.final_score(player) for player in game.players]
    top = max(scores)
    tied = scores.count(top)
    return {
        "scores": scores,
        "margins": [score - max(other for j, other in enumerate(scores) if j != i)
                    for i, score in enumerate(scores)],
        "win_shares": [(1 / tied if score == top else 0.0) for score in scores],
        "achieve_counts": [player.achieve_count for player in game.players],
        "actions": actions,
        "policy_stats": {str(seat): dict(policy.stats) for seat, policy in policies.items()},
        "title_counts": [dict(counts) for counts in title_counts],
    }


def evaluate(*, deals: int, seed: int, players: int, rounds: int,
             styles: tuple[str, ...] = STYLES,
             levels: tuple[int, ...] = LEVELS,
             guard_actions: int = 20_000) -> dict:
    if deals < 1 or players not in (3, 4) or rounds < 1:
        raise ValueError("deals must be positive, players must be 3 or 4, rounds must be positive")
    if not styles or not levels or any(style not in STYLES for style in styles) or any(
        level not in LEVELS for level in levels
    ):
        raise ValueError("styles and levels must be nonempty and valid")
    rng = random.Random(seed)
    specs = [(style, level) for style in styles for level in levels]
    trials = []
    blocks = {f"{style}:{level}": {"score": [], "win_share": [], "margin": []}
              for style, level in specs}
    for deal in range(deals):
        deal_seed = rng.randrange(1 << 30)
        legacy = tuple(rng.sample(range(character_count()), players))
        controls = run_match(deal_seed, legacy, {}, rounds, guard_actions)
        deal_results = {key: {field: [] for field in ("score", "win_share", "margin")}
                        for key in blocks}
        for focal in range(players):
            control = {
                "score": controls["scores"][focal],
                "win_share": controls["win_shares"][focal],
                "margin": controls["margins"][focal],
                "achieve_count": controls["achieve_counts"][focal],
            }
            for style, level in specs:
                key = f"{style}:{level}"
                result = run_match(
                    deal_seed, legacy, {focal: (style, level)}, rounds, guard_actions
                )
                trial = {
                    "deal": deal, "seed": deal_seed, "legacy": legacy,
                    "focal": focal, "style": style, "inference": level,
                    "control": control,
                    "candidate": {
                        "score": result["scores"][focal],
                        "win_share": result["win_shares"][focal],
                        "margin": result["margins"][focal],
                        "achieve_count": result["achieve_counts"][focal],
                    },
                    "policy_stats": result["policy_stats"][str(focal)],
                }
                for field in ("score", "win_share", "margin"):
                    delta = trial["candidate"][field] - control[field]
                    trial[f"{field}_delta"] = delta
                    deal_results[key][field].append(delta)
                trials.append(trial)
        for key in blocks:
            for field in blocks[key]:
                blocks[key][field].append(mean(deal_results[key][field]))

    summary = {}
    for style, level in specs:
        key = f"{style}:{level}"
        rows = [trial for trial in trials
                if trial["style"] == style and trial["inference"] == level]
        stats = Counter()
        for row in rows:
            stats.update(row["policy_stats"])
        quotas = sum(stats[f"quota_{band}"] for band in ("1", "2_6", "7_9", "10_13"))
        summary[key] = {
            "trials": len(rows),
            "mean_score_delta": mean([row["score_delta"] for row in rows]),
            "score_delta_95ci_by_deal": confidence_interval(blocks[key]["score"]),
            "mean_win_share_delta": mean([row["win_share_delta"] for row in rows]),
            "win_share_delta_95ci_by_deal": confidence_interval(blocks[key]["win_share"]),
            "mean_margin_delta": mean([row["margin_delta"] for row in rows]),
            "mean_achieve_count_delta": mean([
                row["candidate"]["achieve_count"] - row["control"]["achieve_count"]
                for row in rows
            ]),
            "mean_quota_rank": stats["quota_rank_total"] / quotas if quotas else None,
            "high_quota_rate": stats["quota_10_13"] / quotas if quotas else None,
            "stats": dict(stats),
        }
    return {
        "config": {
            "deals": deals, "seed": seed, "players": players, "rounds": rounds,
            "styles": styles, "levels": levels, "guard_actions": guard_actions,
        },
        "summary": summary, "trials": trials,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--deals", type=int, default=50)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--players", type=int, choices=(3, 4), default=3)
    parser.add_argument("--rounds", type=int, default=None)
    parser.add_argument("--styles", nargs="+", choices=STYLES, default=STYLES)
    parser.add_argument("--levels", nargs="+", type=int, choices=LEVELS, default=LEVELS)
    parser.add_argument("--guard-actions", type=int, default=20_000)
    parser.add_argument("--json-out", type=Path)
    args = parser.parse_args()
    result = evaluate(
        deals=args.deals, seed=args.seed, players=args.players,
        rounds=args.rounds or args.players,
        styles=tuple(args.styles), levels=tuple(args.levels),
        guard_actions=args.guard_actions,
    )
    print(f"{args.deals} deals × {args.players} seats × {len(result['summary'])} variants")
    for key, row in result["summary"].items():
        print(f"{key:14} score={row['mean_score_delta']:+.2f} "
              f"win_share={row['mean_win_share_delta']:+.3f} "
              f"quota={row['mean_quota_rank']:.1f} "
              f"high={row['high_quota_rate']:.1%}")
    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                                 encoding="utf-8")
        print(f"wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

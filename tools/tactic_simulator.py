"""Paired, seat-balanced experiments for one CPU tactic at a time.

The existing ranking file is never read or written. Every candidate game has a
control game with the same initial seed, CPU characters, and focal seat. Each
deal is replayed with the candidate in every seat, so seat order is balanced.

    python3 tools/tactic_simulator.py --policy P11 --deals 100 --seed 42 \
        --json-out /tmp/quota-p11.json
"""

from __future__ import annotations

import argparse
import importlib
import json
import math
import random
import statistics
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Callable

ROOT = Path(__file__).resolve().parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from quota.ai import choose_action  # noqa: E402
from quota.cards import Card  # noqa: E402
from quota.characters import Mind, bind, character_count, mind_for  # noqa: E402
from quota.engine import Abandon, Action, Game, GameConfig  # noqa: E402
from tools.tactic_policies import Decision, Policy, policy as builtin_policy  # noqa: E402


@dataclass(frozen=True)
class SeatObservation:
    quota: Card | None
    collection: tuple[Card, ...]
    achieved: tuple[Card, ...]
    score: int
    achieve_count: int
    reshuffle_take_left: int
    double_action_left: int


@dataclass(frozen=True)
class Observation:
    """Only information a player can see. No deck order, removed cards, or RNG."""

    market: tuple[Card | None, ...]
    discard: tuple[Card, ...]
    players: tuple[SeatObservation, ...]
    current: int
    deck_count: int
    no_gain_streak: int
    stall_flag: bool
    turn_order: tuple[int, ...]
    round_index: int
    round_count: int
    turn_number: int
    plan: str
    double_stage: int
    legal_actions: tuple[Action, ...]
    turn_gain: bool = False


def observe(game: Game) -> Observation:
    return Observation(
        market=tuple(game.market),
        discard=tuple(game.discard),
        players=tuple(
            SeatObservation(
                quota=player.quota,
                collection=tuple(player.collection),
                achieved=tuple(player.achieved),
                score=game.final_score(player),
                achieve_count=player.achieve_count,
                reshuffle_take_left=player.reshuffle_take_left,
                double_action_left=player.double_action_left,
            )
            for player in game.players
        ),
        current=game.current,
        deck_count=len(game.deck),
        no_gain_streak=game.no_gain_streak,
        stall_flag=game.stall_flag,
        turn_order=tuple(game.turn_order),
        round_index=game.round_index,
        round_count=game.round_count,
        turn_number=game.turn_number,
        plan=game.plan,
        double_stage=game.double_stage,
        legal_actions=tuple(game.legal_actions()),
        turn_gain=game.turn_gain,
    )


@dataclass
class Counters:
    decisions: int = 0
    applicable: int = 0
    changed: int = 0
    special_offered: int = 0
    special_blocked: int = 0


@dataclass(frozen=True)
class Result:
    score: int
    margin: int
    win_share: float
    achieve_count: int
    actions: int
    counters: Counters


def run_game(
    seed: int,
    characters: tuple[int, ...],
    focal: int,
    rounds: int,
    candidate_factory: Callable[[], Policy] | None,
    guard_actions: int = 20_000,
) -> Result:
    seats = len(characters)
    game = Game.start(GameConfig(
        num_players=seats,
        seed=seed,
        human_seats=[],
        names=[f"P{index + 1}" for index in range(seats)],
        rounds=rounds,
        sequence_rule=True,
        title_rule=True,
        special_actions_rule=True,
    ))
    for player, character_id in zip(game.players, characters):
        bind(player, Mind(character_id))
    candidate = candidate_factory() if candidate_factory else None
    counters = Counters()
    actions = 0

    while not game.finished:
        if game.awaiting_next_round:
            before = observe(game) if candidate is not None else None
            game.begin_next_round()
            if candidate is not None:
                candidate.on_transition(before, observe(game), None, "new_round")
            continue
        if actions >= guard_actions:
            raise RuntimeError(f"game exceeded {guard_actions} actions (seed={seed}, focal={focal})")
        before_choice = observe(game) if candidate is not None else None
        if candidate is not None and game.current == focal:
            mind = mind_for(game.players[focal])
            mind_action_state = (
                (mind.abandoned, mind.block_seat, mind.block_suit, mind.block_turn)
                if mind is not None else None
            )
            declaration = candidate.special_declaration(before_choice)
            if declaration not in ("baseline", "none", "double", "reshuffle"):
                raise ValueError(f"unknown special declaration {declaration!r}")
            if declaration in ("double", "reshuffle"):
                me = before_choice.players[focal]
                if (before_choice.plan != "normal" or game.turn_gain or
                    (declaration == "double" and me.double_action_left < 1) or
                    (declaration == "reshuffle" and me.reshuffle_take_left < 1)):
                    raise ValueError(f"illegal special declaration {declaration!r}")
                if declaration == "double":
                    game.declare_double()
                else:
                    game.declare_reshuffle()

            def special_guard(kind: str) -> bool:
                counters.special_offered += 1
                allowed = declaration == "baseline" and candidate.allow_special(observe(game), kind)
                if not allowed:
                    counters.special_blocked += 1
                return allowed

            game.experimental_special_guard = special_guard
            try:
                baseline = choose_action(game)
            finally:
                del game.experimental_special_guard
            after_choice = observe(game)
            if after_choice != before_choice:
                candidate.on_transition(before_choice, after_choice, None, "declaration")
            decision = candidate.decide(after_choice, baseline)
            if not isinstance(decision, Decision):
                raise TypeError("policy.decide must return a Decision")
            action = decision.action
            counters.decisions += 1
            counters.applicable += decision.applicable
            counters.changed += action != baseline
            if not game.is_legal(action):
                raise ValueError(f"candidate returned illegal action {action!r}")
            if action != baseline and mind is not None and mind_action_state is not None:
                (mind.abandoned, mind.block_seat,
                 mind.block_suit, mind.block_turn) = mind_action_state
                if isinstance(action, Abandon):
                    mind.abandoned = True
        else:
            action = choose_action(game)
            if candidate is not None:
                after_choice = observe(game)
                if after_choice != before_choice:
                    candidate.on_transition(before_choice, after_choice, None, "declaration")
        game.step(action)
        if candidate is not None:
            candidate.on_transition(after_choice, observe(game), action, "action")
        actions += 1

    scores = [game.final_score(player) for player in game.players]
    top = max(scores)
    winners = scores.count(top)
    return Result(
        score=scores[focal],
        margin=scores[focal] - max(value for index, value in enumerate(scores) if index != focal),
        win_share=(1 / winners if scores[focal] == top else 0.0),
        achieve_count=game.players[focal].achieve_count,
        actions=actions,
        counters=counters,
    )


def policy_factory(spec: str, deck_threshold: int) -> Callable[[], Policy]:
    if ":" not in spec:
        return lambda: builtin_policy(spec, deck_threshold)
    module_name, factory_name = spec.split(":", 1)
    factory = getattr(importlib.import_module(module_name), factory_name)
    if not callable(factory):
        raise TypeError(f"{spec} is not a callable factory")
    return factory


def mean(values: list[float]) -> float:
    return statistics.fmean(values)


def confidence_interval(blocks: list[float]) -> tuple[float, float] | None:
    """Approximate 95% CI using independent deal-level blocks, not seat trials."""
    if len(blocks) < 2:
        return None
    center = mean(blocks)
    half_width = 1.96 * statistics.stdev(blocks) / math.sqrt(len(blocks))
    return (center - half_width, center + half_width)


def evaluate(
    *,
    deals: int,
    seed: int,
    players: int,
    rounds: int,
    candidate_factory: Callable[[], Policy],
    guard_actions: int = 20_000,
) -> dict:
    if deals < 1 or players not in (3, 4) or rounds < 1:
        raise ValueError("deals must be positive, players must be 3 or 4, rounds must be positive")
    rng = random.Random(seed)
    trials = []
    score_blocks = []
    win_blocks = []
    for deal in range(deals):
        deal_seed = rng.randrange(1 << 30)
        characters = tuple(rng.sample(range(character_count()), players))
        score_deltas = []
        win_deltas = []
        for focal in range(players):
            control = run_game(deal_seed, characters, focal, rounds, None, guard_actions)
            treatment = run_game(deal_seed, characters, focal, rounds, candidate_factory, guard_actions)
            score_delta = treatment.score - control.score
            win_delta = treatment.win_share - control.win_share
            score_deltas.append(score_delta)
            win_deltas.append(win_delta)
            trials.append({
                "deal": deal,
                "seed": deal_seed,
                "characters": characters,
                "focal": focal,
                "control": asdict(control),
                "candidate": asdict(treatment),
                "score_delta": score_delta,
                "win_share_delta": win_delta,
                "margin_delta": treatment.margin - control.margin,
            })
        score_blocks.append(mean(score_deltas))
        win_blocks.append(mean(win_deltas))

    controls = [trial["control"] for trial in trials]
    candidates = [trial["candidate"] for trial in trials]
    return {
        "config": {
            "deals": deals,
            "seed": seed,
            "players": players,
            "rounds": rounds,
            "sequence_rule": True,
            "title_rule": True,
            "special_actions_rule": True,
            "guard_actions": guard_actions,
        },
        "summary": {
            "trials": len(trials),
            "control_mean_score": mean([row["score"] for row in controls]),
            "candidate_mean_score": mean([row["score"] for row in candidates]),
            "mean_score_delta": mean([row["score_delta"] for row in trials]),
            "score_delta_95ci_by_deal": confidence_interval(score_blocks),
            "control_win_share": mean([row["win_share"] for row in controls]),
            "candidate_win_share": mean([row["win_share"] for row in candidates]),
            "mean_win_share_delta": mean([row["win_share_delta"] for row in trials]),
            "win_share_delta_95ci_by_deal": confidence_interval(win_blocks),
            "mean_margin_delta": mean([row["margin_delta"] for row in trials]),
            "mean_achieve_count_delta": mean([
                candidate["achieve_count"] - control["achieve_count"]
                for control, candidate in zip(controls, candidates)
            ]),
            "candidate_decisions": sum(row["counters"]["decisions"] for row in candidates),
            "candidate_applicable": sum(row["counters"]["applicable"] for row in candidates),
            "candidate_changed": sum(row["counters"]["changed"] for row in candidates),
            "candidate_special_offered": sum(row["counters"]["special_offered"] for row in candidates),
            "candidate_special_blocked": sum(row["counters"]["special_blocked"] for row in candidates),
        },
        "trials": trials,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--policy", default="baseline", help="baseline, P11, A15, or module:factory")
    parser.add_argument("--deals", type=int, default=20, help="independent deal seeds; every seat is tested")
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--players", type=int, choices=(3, 4), default=3)
    parser.add_argument("--rounds", type=int, default=None, help="defaults to the player count")
    parser.add_argument("--deck-threshold", type=int, default=30, help="A15 example cutoff")
    parser.add_argument("--guard-actions", type=int, default=20_000)
    parser.add_argument("--json-out", type=Path)
    args = parser.parse_args()
    factory = policy_factory(args.policy, args.deck_threshold)
    result = evaluate(
        deals=args.deals,
        seed=args.seed,
        players=args.players,
        rounds=args.rounds or args.players,
        candidate_factory=factory,
        guard_actions=args.guard_actions,
    )
    result["config"]["policy"] = args.policy
    result["config"]["deck_threshold"] = args.deck_threshold
    summary = result["summary"]
    print(f"{summary['trials']} paired seat trials ({args.deals} deals × {args.players} seats)")
    print(f"score delta candidate-control: {summary['mean_score_delta']:+.3f} "
          f"95% CI by deal: {summary['score_delta_95ci_by_deal']}")
    print(f"win-share delta: {summary['mean_win_share_delta']:+.4f} "
          f"95% CI by deal: {summary['win_share_delta_95ci_by_deal']}")
    print(f"applicable={summary['candidate_applicable']} changed={summary['candidate_changed']} "
          f"special_blocked={summary['candidate_special_blocked']}")
    if args.json_out:
        args.json_out.parent.mkdir(parents=True, exist_ok=True)
        args.json_out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

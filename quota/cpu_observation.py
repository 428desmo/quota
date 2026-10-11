"""Public game state exposed to CPU policies."""

from __future__ import annotations

from dataclasses import dataclass
from quota.cards import Card
from quota.engine import Action, Game

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

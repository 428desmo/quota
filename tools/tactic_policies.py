"""Public-observation policies for paired CPU tactic experiments.

These are examples, not changes to live CPU play. Add policies here or load a
module exposing a ``policy()`` factory through tactic_simulator --policy.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

from quota.cards import Card, sequence_at
from quota.engine import Action, Collect, Pass, TakeQuota

if TYPE_CHECKING:
    from tools.tactic_simulator import Observation


@dataclass(frozen=True)
class Decision:
    action: Action
    applicable: bool = False


class Policy:
    name = "baseline"

    def special_declaration(self, observation: Observation) -> str:
        """'baseline', 'none', 'double', or 'reshuffle' before CPU action selection."""
        return "baseline"

    def on_transition(
        self, before: Observation, after: Observation, action: Action | None, phase: str
    ) -> None:
        """Observe every public state change, including other players' turns."""
        return None

    def allow_special(self, observation: Observation, kind: str) -> bool:
        return True

    def decide(self, observation: Observation, baseline: Action) -> Decision:
        return Decision(baseline)


class DenySequence(Policy):
    """P11 prototype: use a finishing card that also spoils a rival's next pair."""

    name = "P11-deny-sequence"

    def decide(self, observation: Observation, baseline: Action) -> Decision:
        me = observation.players[observation.current]
        if me.quota is None or me.quota.rank is None or not isinstance(baseline, Collect):
            return Decision(baseline)
        if me.quota.rank - 1 - len(me.collection) != 1:
            return Decision(baseline)
        rivals = [
            rival for index, rival in enumerate(observation.players)
            if index != observation.current and rival.quota is not None
            and rival.quota.suit == me.quota.suit
            and rival.quota.rank is not None
        ]
        if not rivals:
            return Decision(baseline)
        choices = [
            card for card in observation.market if card is not None
            and (card.suit == me.quota.suit or card.suit == "JOKER")
        ]
        if len(choices) < 2:
            return Decision(baseline)

        own_line = [*me.achieved, me.quota, *me.collection]
        baseline_id = baseline.card_ids[0]
        base_card = next((card for card in choices if card.id == baseline_id), None)
        if base_card is None:
            return Decision(baseline)

        def rival_pair(other, card: Card) -> int:
            line = [*other.achieved, other.quota, *other.collection, card]
            return sequence_at(line, len(line) - 1)

        def value(card: Card) -> int:
            own = sequence_at(own_line + [card], len(own_line))
            denial = max(
                max(rival_pair(other, choice) for choice in choices)
                - max(rival_pair(other, choice) for choice in choices if choice.id != card.id)
                for other in rivals
            )
            return own * 2 + denial

        best = max(choices, key=lambda card: (value(card), card.id == baseline_id))
        if value(best) <= value(base_card):
            return Decision(baseline, applicable=True)
        return Decision(Collect((best.id,)), applicable=True)


class ReserveSpecials(Policy):
    """A15 prototype: reserve both one-use actions until the deck is low."""

    name = "A15-reserve-specials"

    def __init__(self, deck_threshold: int = 30):
        self.deck_threshold = deck_threshold

    def allow_special(self, observation: Observation, kind: str) -> bool:
        return observation.deck_count <= self.deck_threshold


class BlockWithImpossibleQuota(Policy):
    """P12 prototype: take an unwinnable quota instead of passing late."""

    name = "P12-impossible-block"

    def decide(self, observation: Observation, baseline: Action) -> Decision:
        me = observation.players[observation.current]
        if me.quota is not None or observation.deck_count > 24:
            return Decision(baseline)

        def can_finish(card: Card) -> bool:
            unavailable = [*observation.discard]
            for seat in observation.players:
                unavailable.extend(seat.achieved)
                if seat.quota is not None:
                    unavailable.append(seat.quota)
                unavailable.extend(seat.collection)
            same = sum(item.suit == card.suit for item in unavailable)
            wild = sum(item.suit == "JOKER" for item in unavailable)
            # An upper bound: hidden removed cards may reduce this further.
            return card.rank - 1 <= (26 - same - 1) + (4 - wild)

        by_id = {card.id: card for card in observation.market if card is not None}
        if isinstance(baseline, TakeQuota) and can_finish(by_id[baseline.card_id]):
            return Decision(baseline)
        if not isinstance(baseline, (TakeQuota, Pass)):
            return Decision(baseline)

        choices = []
        for card in by_id.values():
            if card.suit == "JOKER" or card.rank is None or card.rank == 1 or can_finish(card):
                continue
            rivals = [
                seat for index, seat in enumerate(observation.players)
                if index != observation.current and seat.quota is not None
                and seat.quota.suit == card.suit
            ]
            if not rivals:
                continue
            def denial(seat) -> int:
                line = [*seat.achieved, seat.quota, *seat.collection, card]
                pair = sequence_at(line, len(line) - 1)
                need = seat.quota.rank - 1 - len(seat.collection)
                return pair + (2 if need == 1 else 0)
            choices.append((max(denial(seat) for seat in rivals), card))
        if not choices:
            return Decision(baseline)
        best = max(choices, key=lambda entry: (entry[0], -entry[1].id))[1]
        return Decision(TakeQuota(best.id), applicable=True)


POLICIES = {
    "baseline": Policy,
    "P11": DenySequence,
    "A15": ReserveSpecials,
    "P12": BlockWithImpossibleQuota,
}


def policy(name: str, deck_threshold: int = 30) -> Policy:
    if name not in POLICIES:
        raise ValueError(f"unknown policy {name!r}; choose from {', '.join(POLICIES)}")
    if name == "A15":
        return ReserveSpecials(deck_threshold)
    return POLICIES[name]()

"""Experimental strategy switching on public observations, never live CPU play.

The heuristics below are intentionally small proxies, not calibrated card odds.
They let the paired simulator isolate the *reason* for changing priorities.
"""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from typing import TYPE_CHECKING

from quota.cards import Card, score_for, sequence_at
from quota.engine import Abandon, Action, Collect, TakeQuota
from tools.tactic_policies import Decision, Policy

if TYPE_CHECKING:
    from tools.tactic_simulator import Observation


MODES = ("fixed", "macro", "tactical", "combined")


@dataclass(frozen=True)
class Profile:
    failure_cost: float = 2.0
    turn_cost: float = 0.22
    comeback_weight: float = 3.0
    denial_weight: float = 1.0
    title_weight: float = 1.0


PROFILES = {
    "balanced": Profile(),
    "efficient": Profile(turn_cost=0.40, denial_weight=0.6, title_weight=0.5),
    "safe": Profile(failure_cost=5.0, turn_cost=0.32, comeback_weight=1.0),
    "comeback": Profile(failure_cost=1.0, comeback_weight=6.0),
    "denial": Profile(denial_weight=2.3, title_weight=0.7),
    "title": Profile(title_weight=2.5, denial_weight=0.6),
}


def bundles(cards: tuple[Card, ...]) -> list[tuple[str, bool]]:
    result = []
    offset = 0
    while offset < len(cards):
        first = cards[offset]
        if first.rank is None or offset + first.rank > len(cards):
            break
        group = cards[offset:offset + first.rank]
        result.append((first.suit, any(card.suit == "JOKER" for card in group)))
        offset += first.rank
    return result


def visible_counts(observation: Observation) -> Counter[str]:
    cards = [*observation.discard, *(card for card in observation.market if card is not None)]
    for seat in observation.players:
        cards.extend(seat.achieved)
        cards.extend(seat.collection)
        if seat.quota is not None:
            cards.append(seat.quota)
    return Counter(card.suit for card in cards)


def quota_outlook(observation: Observation, card: Card) -> float:
    """Conservative public-card proxy; deliberately not an exact probability."""
    if card.rank == 1:
        return 1.0
    if card.rank is None:
        return 0.0
    need = card.rank - 1
    market = sum(
        other is not None and other.id != card.id
        and other.suit in (card.suit, "JOKER")
        for other in observation.market
    )
    counts = visible_counts(observation)
    unknown = max(0, 26 - counts[card.suit]) + max(0, 4 - counts["JOKER"])
    if market + unknown < need:
        return 0.0
    future = min(observation.deck_count, unknown) * observation.deck_count / max(
        observation.deck_count + 8, 1
    )
    supply = market + future * 0.35
    return min(1.0, supply / need) ** 1.5


@dataclass(frozen=True)
class Signals:
    macro: tuple[str, ...]
    tactical: tuple[str, ...]


class StrategyPolicy(Policy):
    """A four-way trigger ablation using the same legal candidates and scorer."""

    def __init__(self, mode: str, profile: str = "balanced"):
        if mode not in MODES:
            raise ValueError(f"unknown mode {mode!r}")
        if profile not in PROFILES:
            raise ValueError(f"unknown profile {profile!r}")
        self.mode = mode
        self.profile = profile
        self.weights = PROFILES[profile]
        self.reason_counts: Counter[str] = Counter()
        self.switches = 0
        self.decisions = 0
        self.changed = 0
        self.manual_specials: Counter[str] = Counter()
        self.last_active = False
        self.mono_intent: Counter[int] = Counter()

    def on_transition(
        self, before: Observation, after: Observation, action: Action | None, phase: str
    ) -> None:
        if phase == "new_round":
            self.mono_intent.clear()
        if phase != "action" or not isinstance(action, TakeQuota):
            return
        actor = before.current
        prior = bundles(before.players[actor].achieved)
        if not prior or len({suit for suit, _ in prior}) != 1:
            return
        chosen = next((card for card in before.market if card and card.id == action.card_id), None)
        if chosen is None or chosen.suit != prior[0][0]:
            return
        if any(card and card.suit not in ("JOKER", chosen.suit) for card in before.market):
            self.mono_intent[actor] += 1

    def signals(self, observation: Observation) -> Signals:
        me = observation.players[observation.current]
        rivals = [
            (index, seat) for index, seat in enumerate(observation.players)
            if index != observation.current
        ]
        gap = max(seat.score for _, seat in rivals) - me.score
        rounds_left = observation.round_count - observation.round_index
        macro = []
        if gap >= 8 and (rounds_left <= 1 or gap >= 16):
            macro.append("score_deficit")
        if -gap >= 10 and rounds_left <= 1:
            macro.append("protect_lead")

        tactical = []
        available = [card for card in observation.market if card is not None]
        if any(
            seat.quota is not None and seat.quota.rank is not None
            and seat.quota.rank - 1 - len(seat.collection) == 1
            and any(card.suit in (seat.quota.suit, "JOKER") for card in available)
            for _, seat in rivals
        ):
            tactical.append("rival_finishing")
        if any(
            len(groups := bundles(seat.achieved)) == 2
            and len({suit for suit, _ in groups}) == 1
            and (seat.quota is None or seat.quota.suit == groups[0][0])
            and (self.mono_intent[index] > 0 or seat.achieve_count >= 2)
            for index, seat in rivals
        ):
            tactical.append("rival_mono_title")
        if me.quota is None and any(
            card.suit != "JOKER" and card.rank == 13
            and quota_outlook(observation, card) >= 0.45
            for card in available
        ):
            tactical.append("large_quota_opening")
        if me.quota is not None and me.quota.rank is not None:
            need = me.quota.rank - 1 - len(me.collection)
            counts = visible_counts(observation)
            remaining = 26 - counts[me.quota.suit] + 4 - counts["JOKER"]
            market = sum(card.suit in (me.quota.suit, "JOKER") for card in available)
            if need > market + remaining:
                tactical.append("quota_impossible")
        return Signals(tuple(macro), tuple(tactical))

    def active_reasons(self, observation: Observation) -> tuple[str, ...]:
        signals = self.signals(observation)
        reasons = []
        if self.mode in ("macro", "combined"):
            reasons.extend(signals.macro)
        if self.mode in ("tactical", "combined"):
            reasons.extend(signals.tactical)
        return tuple(reasons)

    def special_declaration(self, observation: Observation) -> str:
        if (observation.plan != "normal" or observation.turn_gain
            or not self.active_reasons(observation)):
            return "baseline"
        me = observation.players[observation.current]
        signals = self.signals(observation)
        if ("rival_finishing" in signals.tactical and self.mode in ("tactical", "combined")
            and me.reshuffle_take_left and me.quota is None
            and not any(card and card.rank == 1 for card in observation.market)):
            self.manual_specials["reshuffle"] += 1
            return "reshuffle"
        if ("score_deficit" in signals.macro and self.mode in ("macro", "combined")
            and me.double_action_left and observation.deck_count > 0
            and any(card and card.rank == 1 for card in observation.market)):
            self.manual_specials["double"] += 1
            return "double"
        return "baseline"

    def decide(self, observation: Observation, baseline: Action) -> Decision:
        self.decisions += 1
        reasons = self.active_reasons(observation)
        active = bool(reasons)
        if active != self.last_active:
            self.switches += 1
        self.last_active = active
        self.reason_counts.update(reasons)
        if not active:
            return Decision(baseline)

        me = observation.players[observation.current]
        by_id = {card.id: card for card in observation.market if card is not None}
        signals = self.signals(observation)
        if me.quota is None:
            takes = [action for action in observation.legal_actions if isinstance(action, TakeQuota)]
            if not takes:
                return Decision(baseline, applicable=True)

            def value(action: TakeQuota) -> float:
                card = by_id[action.card_id]
                assert card.rank is not None
                chance = quota_outlook(observation, card)
                score = (
                    score_for(card.rank) * chance
                    - (1 - chance) * self.weights.failure_cost
                    - card.rank * self.weights.turn_cost
                )
                if card.rank == 1:
                    score += 1
                if "score_deficit" in reasons and card.rank >= 10:
                    score += self.weights.comeback_weight * chance
                if "protect_lead" in reasons:
                    score -= card.rank * (1 - chance) * 0.45
                if "large_quota_opening" in reasons and card.rank >= 10:
                    score += 1.5 * chance
                if "rival_finishing" in reasons:
                    score += 2.5 * self.weights.denial_weight * sum(
                        seat.quota is not None and seat.quota.suit == card.suit
                        and seat.quota.rank is not None
                        and seat.quota.rank - 1 - len(seat.collection) == 1
                        for index, seat in enumerate(observation.players)
                        if index != observation.current
                    )
                if "rival_mono_title" in reasons:
                    score += 0.8 * self.weights.denial_weight * sum(
                        len(groups := bundles(seat.achieved)) >= 2
                        and groups[0][0] == card.suit
                        for index, seat in enumerate(observation.players)
                        if index != observation.current
                    )
                own_groups = bundles(me.achieved)
                if self.weights.title_weight and own_groups:
                    if (len({suit for suit, _ in own_groups}) == 1
                        and card.suit == own_groups[0][0]
                        and len(own_groups) < 3):
                        score += 1.0 * self.weights.title_weight * chance
                    if card.suit not in {item.suit for item in me.achieved}:
                        score += 0.35 * self.weights.title_weight * chance
                return score + (0.25 if action == baseline else 0)

            chosen = max(takes, key=value)
        elif "quota_impossible" in reasons and Abandon() in observation.legal_actions:
            chosen = Abandon()
        elif isinstance(baseline, Collect) and me.quota.rank is not None:
            need = me.quota.rank - 1 - len(me.collection)
            if need != 1:
                return Decision(baseline, applicable=True)
            eligible = [
                card for card in by_id.values()
                if card.suit in (me.quota.suit, "JOKER")
            ]
            if not eligible:
                return Decision(baseline, applicable=True)
            own_line = [*me.achieved, me.quota, *me.collection]

            def value(card: Card) -> float:
                own = sequence_at(own_line + [card], len(own_line))
                denial = 0
                if "rival_finishing" in reasons:
                    for index, seat in enumerate(observation.players):
                        if index == observation.current or seat.quota is None:
                            continue
                        if seat.quota.suit == card.suit and seat.quota.rank is not None:
                            if seat.quota.rank - 1 - len(seat.collection) == 1:
                                denial += 2
                return (own + denial * self.weights.denial_weight
                        + (0.25 if card.id in baseline.card_ids else 0))

            chosen = Collect((max(eligible, key=value).id,))
        else:
            return Decision(baseline, applicable=True)
        if chosen != baseline:
            self.changed += 1
        return Decision(chosen, applicable=True)

    def report(self) -> dict:
        return {
            "decisions": self.decisions,
            "switches": self.switches,
            "reason_counts": dict(self.reason_counts),
            "manual_specials": dict(self.manual_specials),
            "changed": self.changed,
            "profile": self.profile,
        }

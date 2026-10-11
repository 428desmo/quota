"""Independent experimental CPU. It sees only public observations.

Style and inference level are separate axes. Nothing here is installed in the
live Python or Unity CPU. Hidden deck contents and the removed eight cards are
never passed to this policy.
"""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from typing import TYPE_CHECKING

from quota.cards import Card, score_for, sequence_at
from quota.engine import Abandon, Action, Collect, Pass, TakeQuota

if TYPE_CHECKING:
    from tools.tactic_simulator import Observation

GOODS = ("S", "H", "D", "C")
STYLES = ("balanced", "efficient", "safe", "comeback", "denial", "title")
LEVELS = (0, 1, 2)


@dataclass(frozen=True)
class Style:
    risk: float
    time: float
    upside: float
    denial: float
    title: float


STYLE = {
    "balanced": Style(2.0, 0.48, 1.0, 0.4, 0.6),
    "efficient": Style(2.0, 0.90, 0.85, 0.2, 0.3),
    "safe": Style(5.0, 0.58, 0.70, 0.2, 0.5),
    "comeback": Style(1.2, 0.40, 1.55, 0.2, 0.5),
    "denial": Style(2.3, 0.54, 0.85, 2.0, 0.4),
    "title": Style(2.4, 0.50, 0.95, 0.2, 2.8),
}


def visible_cards(observation: Observation) -> list[Card]:
    cards = [*observation.discard, *(card for card in observation.market if card)]
    for seat in observation.players:
        if seat.quota:
            cards.append(seat.quota)
        cards.extend(seat.collection)
        cards.extend(seat.achieved)
    return cards


def bundles(cards: tuple[Card, ...]) -> list[tuple[str, bool]]:
    found = []
    offset = 0
    while offset < len(cards):
        head = cards[offset]
        if head.rank is None or offset + head.rank > len(cards):
            break
        group = cards[offset:offset + head.rank]
        found.append((head.suit, any(card.suit == "JOKER" for card in group)))
        offset += head.rank
    return found


class CharacterPolicy:
    def __init__(self, style: str, inference: int):
        if style not in STYLE or inference not in LEVELS:
            raise ValueError(f"invalid style/inference: {style}, {inference}")
        self.style_name = style
        self.style = STYLE[style]
        self.inference = inference
        self.returned: Counter[str] = Counter()
        self.stats: Counter[str] = Counter()

    def on_transition(self, before: Observation, after: Observation,
                      action: Action | None, phase: str) -> None:
        if phase == "new_round":
            self.returned.clear()
            return
        if self.inference < 2 or before.round_index != after.round_index:
            return
        reshuffled = (
            before.market != after.market
            and (phase == "declaration" or isinstance(action, Pass))
        )
        if reshuffled:
            returning = [card for card in before.market if card]
            self.returned.update(card.suit for card in returning)
            pool = before.deck_count + len(returning)
            drawn = sum(card is not None for card in after.market)
            survival = max(0.0, 1.0 - drawn / pool) if pool else 0.0
            for suit in list(self.returned):
                self.returned[suit] *= survival
            return
        draws = max(0, before.deck_count - after.deck_count)
        if draws and before.deck_count:
            survival = max(0.0, 1.0 - draws / before.deck_count)
            for suit in list(self.returned):
                self.returned[suit] *= survival

    def expected_deck(self, observation: Observation, suit: str) -> float:
        """Expected suit count in deck; all inputs are public or memorized."""
        deck = observation.deck_count
        if deck <= 0:
            return 0.0
        if self.inference == 0:
            return deck * (4 / 108 if suit == "JOKER" else 26 / 108)
        visible = Counter(card.suit for card in visible_cards(observation))
        total = 4 if suit == "JOKER" else 26
        if self.inference == 1:
            unknown = max(0, total - visible[suit])
            return min(deck, unknown * deck / (deck + 8))
        # The returned market is known to have re-entered the deck. Cards drawn
        # since then reduce that known mass in expectation, not by physical ID.
        returned = {kind: min(max(0.0, self.returned[kind]), deck) for kind in (*GOODS, "JOKER")}
        returned_total = min(deck, sum(returned.values()))
        unknown = {
            kind: max(0.0, (4 if kind == "JOKER" else 26) - visible[kind] - returned[kind])
            for kind in (*GOODS, "JOKER")
        }
        unknown_total = sum(unknown.values())
        remainder = deck - returned_total
        return returned[suit] + (remainder * unknown[suit] / unknown_total if unknown_total else 0.0)

    def completion(self, observation: Observation, suit: str, need: int,
                   *, excluded_id: int | None = None) -> float:
        if need <= 0:
            return 1.0
        market = sum(
            card is not None and card.id != excluded_id and card.suit in (suit, "JOKER")
            for card in observation.market
        )
        future = self.expected_deck(observation, suit) + self.expected_deck(observation, "JOKER")
        # Opponents compete for cards and the round can end before the deck does.
        reach = 0.32 if len(observation.players) == 3 else 0.27
        usable = market + future * reach
        if self.inference > 0:
            visible = Counter(card.suit for card in visible_cards(observation))
            upper = 26 - visible[suit] + 4 - visible["JOKER"] + market
            if upper < need:
                return 0.0
        return min(1.0, usable / need) ** 1.7

    def title_value(self, observation: Observation, card: Card, chance: float) -> float:
        mine = observation.players[observation.current]
        groups = bundles(mine.achieved)
        value = 0.0
        if groups and len(groups) < 3 and len({kind for kind, _ in groups}) == 1:
            if card.suit == groups[0][0]:
                value += 3.0 * chance
        if card.suit not in {item.suit for item in mine.achieved}:
            value += 0.5 * chance
        return value * self.style.title

    def denial_value(self, observation: Observation, card: Card) -> float:
        impact = 0.0
        for index, rival in enumerate(observation.players):
            if index == observation.current or rival.quota is None:
                continue
            if rival.quota.suit != card.suit:
                continue
            need = rival.quota.rank - 1 - len(rival.collection)
            if need <= 0:
                continue
            impact = max(impact, 2.0 if need == 1 else 0.5)
        return impact * self.style.denial

    def special(self, observation: Observation) -> str | None:
        mine = observation.players[observation.current]
        if observation.plan != "normal" or observation.turn_gain:
            return None
        eligible = [
            card for card in observation.market
            if card and (mine.quota is None and card.suit != "JOKER"
                         or mine.quota is not None and card.suit in (mine.quota.suit, "JOKER"))
        ]
        if mine.reshuffle_take_left and not eligible and observation.deck_count > 0:
            self.stats["reshuffle"] += 1
            return "reshuffle"
        if mine.double_action_left and observation.deck_count > 1:
            if mine.quota is not None and mine.quota.rank is not None:
                need = mine.quota.rank - 1 - len(mine.collection)
                if 0 < need <= len(eligible) and self.style_name != "safe":
                    self.stats["double"] += 1
                    return "double"
            elif any(card.rank == 1 for card in eligible) and self.style_name in (
                "efficient", "comeback", "balanced"
            ):
                self.stats["double"] += 1
                return "double"
        return None

    def choose(self, observation: Observation) -> Action:
        self.stats["decisions"] += 1
        mine = observation.players[observation.current]
        market = {card.id: card for card in observation.market if card}
        if mine.quota is None:
            choices = [action for action in observation.legal_actions if isinstance(action, TakeQuota)]
            if not choices:
                self.stats["pass"] += 1
                return Pass()

            lead = max(seat.score for index, seat in enumerate(observation.players)
                       if index != observation.current) - mine.score
            remaining_rounds = observation.round_count - observation.round_index

            def utility(action: TakeQuota) -> float:
                card = market[action.card_id]
                assert card.rank is not None
                chance = self.completion(observation, card.suit, card.rank - 1,
                                         excluded_id=card.id)
                upside = self.style.upside
                if lead >= 8 and remaining_rounds <= 1:
                    upside *= 1.4
                if lead <= -10 and remaining_rounds <= 1:
                    upside *= 0.8
                score = (score_for(card.rank) * chance * upside
                         - self.style.risk * (1 - chance)
                         - self.style.time * card.rank * 0.65)
                score += self.title_value(observation, card, chance)
                score += self.denial_value(observation, card)
                return score

            chosen = max(choices, key=lambda action: (utility(action), -action.card_id))
            card = market[chosen.card_id]
            rank = card.rank
            self.stats["quota_1" if rank == 1 else "quota_2_6" if rank <= 6
                       else "quota_7_9" if rank <= 9 else "quota_10_13"] += 1
            self.stats["quota_rank_total"] += rank
            if any(
                index != observation.current and rival.quota is not None
                and rival.quota.suit == card.suit
                for index, rival in enumerate(observation.players)
            ):
                self.stats["contested_quota"] += 1
            groups = bundles(mine.achieved)
            if (groups and len(groups) < 3 and len({kind for kind, _ in groups}) == 1
                and card.suit == groups[0][0]):
                self.stats["mono_quota"] += 1
            return chosen

        assert mine.quota.rank is not None
        need = mine.quota.rank - 1 - len(mine.collection)
        eligible = [card for card in market.values()
                    if card.suit in (mine.quota.suit, "JOKER")]
        if not eligible:
            chance = self.completion(observation, mine.quota.suit, need)
            if (observation.plan == "normal" and not observation.turn_gain
                and chance < (0.18 if self.style_name == "comeback" else 0.32)):
                self.stats["abandon"] += 1
                return Abandon()
            self.stats["pass"] += 1
            return Pass()
        suits = [card for card in eligible if card.suit != "JOKER"]
        wilds = [card for card in eligible if card.suit == "JOKER"]
        purist = (self.style_name == "title" and len(bundles(mine.achieved)) < 3
                  and not any(wild for _, wild in bundles(mine.achieved)))
        if purist and suits and len(suits) < need:
            chance_without_wild = self.completion(observation, mine.quota.suit,
                                                  need - len(suits))
            if chance_without_wild > 0.60:
                wilds = []
        available = suits + wilds
        line = [*mine.achieved, mine.quota, *mine.collection]
        chosen = []
        for _ in range(min(need, len(available))):
            card = max(available, key=lambda item: (
                sequence_at(line + [item], len(line))
                + self.denial_value(observation, item),
                item.suit != "JOKER", -item.id,
            ))
            available.remove(card)
            chosen.append(card)
            line.append(card)
        if not chosen:
            self.stats["pass"] += 1
            return Pass()
        self.stats["collect"] += 1
        self.stats["gold_collected"] += sum(card.suit == "JOKER" for card in chosen)
        return Collect(tuple(card.id for card in chosen))

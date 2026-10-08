"""Cards. Suit ids are the game logic; names come from an item set."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

Suit = Literal["S", "H", "D", "C", "JOKER"]

SUITS: tuple[Suit, ...] = ("S", "H", "D", "C")


def bonus(rank: int) -> int:
    if rank <= 6:
        return 0
    if rank <= 9:
        return 1
    if rank <= 12:
        return 3
    if rank == 13:
        return 6
    raise ValueError(f"rank out of range: {rank}")


def score_for(rank: int) -> int:
    return rank + bonus(rank)


def sequence_at(cards: list[Card], index: int, same: int = 2, adjacent: int = 1) -> int:
    """Bonus placed on this card, including consecutive wildcards."""
    if index < 1:
        return 0
    left, right = cards[index - 1], cards[index]
    if right.suit == "JOKER":
        count = 0
        cursor = index - 1
        while cursor >= 0 and cards[cursor].suit == "JOKER":
            count += 1
            cursor -= 1
        return count
    if left.suit == "JOKER":
        return 0
    if left.rank == right.rank:
        return same
    if left.rank is not None and right.rank is not None and abs(left.rank - right.rank) == 1:
        return adjacent
    return 0


def sequence_bonus(cards: list[Card], same: int = 2, adjacent: int = 1) -> int:
    return sum(sequence_at(cards, i, same, adjacent) for i in range(1, len(cards)))


@dataclass(frozen=True, slots=True)
class Card:
    id: int
    suit: Suit
    rank: int | None

    def label(self, item_set=None) -> str:
        from quota.items import default_item_set

        theme = default_item_set() if item_set is None else item_set
        return theme.label(self)


def make_deck(num_decks: int = 2) -> list[Card]:
    cards: list[Card] = []
    next_id = 0
    for _ in range(num_decks):
        for suit in SUITS:
            for rank in range(1, 14):
                cards.append(Card(next_id, suit, rank))
                next_id += 1
        for _j in range(2):
            cards.append(Card(next_id, "JOKER", None))
            next_id += 1
    return cards

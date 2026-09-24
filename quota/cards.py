"""Cards and display labels. Theme names sit beside suit marks."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

Suit = Literal["S", "H", "D", "C", "JOKER"]

SUITS: tuple[Suit, ...] = ("S", "H", "D", "C")

SUIT_MARK = {"S": "♠", "H": "♥", "D": "♦", "C": "♣", "JOKER": "Joker"}
SUIT_NAME = {"S": "香辛料", "H": "絹", "D": "宝石", "C": "茶", "JOKER": "銀貨"}

RANK_LABEL = {
    1: "A",
    11: "J",
    12: "Q",
    13: "K",
}


def bonus(rank: int) -> int:
    if rank <= 6:
        return 0
    if rank <= 9:
        return 2
    if rank <= 12:
        return 5
    if rank == 13:
        return 10
    raise ValueError(f"rank out of range: {rank}")


def score_for(rank: int) -> int:
    return rank + bonus(rank)


@dataclass(frozen=True, slots=True)
class Card:
    id: int
    suit: Suit
    rank: int | None

    def label(self) -> str:
        goods = SUIT_NAME[self.suit]
        if self.suit == "JOKER":
            return f"Joker 銀貨 #{self.id}"
        assert self.rank is not None
        face = RANK_LABEL.get(self.rank, str(self.rank))
        return f"{SUIT_MARK[self.suit]}{face} {goods} #{self.id}"


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

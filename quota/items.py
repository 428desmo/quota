"""Trade-goods catalog. Display only; game logic stays on suit ids."""

from __future__ import annotations

import json
import random
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path

from quota.cards import Card

GOODS_PATH = Path(__file__).resolve().parent.parent / "quota_goods_v1.0.json"

# Deck order is S, H, D, C. The four dealt goods follow S, H, C, D.
KIND_OF_SUIT = {"S": "K1", "H": "K2", "C": "K3", "D": "K4", "JOKER": "WILD"}
SLOT_KIND_IDS = ("K1", "K2", "K3", "K4")
SLOT_COLORS = ("#A0522D", "#7B3FA0", "#2E7D32", "#1E5AA8")
WILD_COLOR = "#C4A035"
RANK_LABELS = tuple(str(n) for n in range(1, 14))


@dataclass(frozen=True, slots=True)
class Good:
    number: int
    name: str
    file: str


@dataclass(frozen=True, slots=True)
class ItemFace:
    kind_id: str
    name: str
    emoji: str
    color: str
    file: str


@dataclass(frozen=True, slots=True)
class ItemSet:
    id: str
    name: str
    description: str
    default: bool
    faces: dict[str, ItemFace]
    rank_labels: tuple[str, ...]
    wild_rank_label: str

    def face_for(self, card: Card) -> ItemFace:
        return self.faces[KIND_OF_SUIT[card.suit]]

    def rank_label(self, card: Card) -> str:
        if card.rank is None:
            return self.wild_rank_label
        return self.rank_labels[card.rank - 1]

    def label(self, card: Card) -> str:
        face = self.face_for(card)
        return f"{face.emoji}{self.rank_label(card)} {face.name} #{card.id}"


@lru_cache(maxsize=1)
def _catalog() -> tuple[tuple[Good, ...], Good]:
    data = json.loads(GOODS_PATH.read_text(encoding="utf-8"))
    goods = tuple(Good(item["number"], item["name"], item["file"]) for item in data["goods"])
    wild = data["wild"]
    if [item.number for item in goods] != list(range(1, 28)):
        raise ValueError("trade goods must be numbered 1 through 27")
    if len({item.file for item in goods}) != len(goods):
        raise ValueError("trade good files must be unique")
    return goods, Good(wild["number"], wild["name"], wild["file"])


def goods_pool() -> tuple[Good, ...]:
    return _catalog()[0]


def wild_good() -> Good:
    return _catalog()[1]


def theme_for(indices: tuple[int, ...]) -> ItemSet:
    pool = goods_pool()
    if len(indices) != 4 or len(set(indices)) != 4:
        raise ValueError("a game needs four distinct goods")
    faces: dict[str, ItemFace] = {}
    for kind_id, color, index in zip(SLOT_KIND_IDS, SLOT_COLORS, indices):
        if index < 0 or index >= len(pool):
            raise ValueError("goods index is outside the pool")
        good = pool[index]
        faces[kind_id] = ItemFace(kind_id, good.name, "", color, good.file)
    wild = wild_good()
    faces["WILD"] = ItemFace("WILD", wild.name, "", WILD_COLOR, wild.file)
    return ItemSet(
        id="trade",
        name="交易品",
        description="交易品。対局ごとに4品目。",
        default=True,
        faces=faces,
        rank_labels=RANK_LABELS,
        wild_rank_label="＊",
    )


def deal_goods(rng: random.Random) -> tuple[int, int, int, int]:
    order = list(range(len(goods_pool())))
    rng.shuffle(order)
    picked = tuple(order[:4])
    return picked  # type: ignore[return-value]


def default_item_set() -> ItemSet:
    return theme_for((0, 1, 2, 3))


def resolve_item_set(key: str) -> ItemSet:
    needle = key.strip()
    if needle in ("", "trade", "交易品"):
        return default_item_set()
    raise ValueError("アイテムセットは交易品だけです")

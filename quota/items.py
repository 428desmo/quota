"""Item-set catalog. Display only; game logic stays on suit ids."""

from __future__ import annotations

import json
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path

from quota.cards import Card

CATALOG_PATH = Path(__file__).resolve().parent.parent / "quota_item_sets_v1.0.json"

# Deck order is S, H, D, C. Kinds follow the trade-goods correspondence.
KIND_OF_SUIT = {"S": "K1", "H": "K2", "C": "K3", "D": "K4", "JOKER": "WILD"}


@dataclass(frozen=True, slots=True)
class ItemFace:
    kind_id: str
    name: str
    emoji: str
    color: str


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


def _parse(raw: dict) -> ItemSet:
    faces: dict[str, ItemFace] = {}
    for kind in raw["kinds"]:
        faces[kind["id"]] = ItemFace(kind["id"], kind["name"], kind["emoji"], kind["color"])
    wild = raw["wild"]
    faces[wild["id"]] = ItemFace(wild["id"], wild["name"], wild["emoji"], wild["color"])
    labels = tuple(raw["rank_labels"])
    if len(labels) != 13:
        raise ValueError(f"{raw['id']}: rank_labels must have 13 entries")
    expected = {"K1", "K2", "K3", "K4", "WILD"}
    if set(faces) != expected:
        raise ValueError(f"{raw['id']}: kinds must be K1..K4 and WILD")
    return ItemSet(
        id=raw["id"],
        name=raw["name"],
        description=raw["description"],
        default=bool(raw.get("default")),
        faces=faces,
        rank_labels=labels,
        wild_rank_label=raw["wild_rank_label"],
    )


@lru_cache(maxsize=1)
def catalog() -> tuple[ItemSet, ...]:
    data = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))
    sets = tuple(_parse(raw) for raw in data["item_sets"])
    defaults = [item for item in sets if item.default]
    if len(defaults) != 1:
        raise ValueError("item set catalog must have exactly one default")
    if len({item.id for item in sets}) != len(sets):
        raise ValueError("item set ids must be unique")
    return sets


def default_item_set() -> ItemSet:
    return next(item for item in catalog() if item.default)


def resolve_item_set(key: str) -> ItemSet:
    needle = key.strip()
    for item in catalog():
        if item.id == needle or item.name == needle:
            return item
    names = "、".join(f"{item.id}（{item.name}）" for item in catalog())
    raise ValueError(f"未知のアイテムセットです: {key}。選べるのは {names}")

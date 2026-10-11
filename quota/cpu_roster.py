"""The six playable CPU characters and their independent deck-reading level."""

from __future__ import annotations

import random

# Order is stable: these IDs are sent to Unity clients in table snapshots.
CHARACTERS = (
    ("慎重な帳簿係", "safe"),
    ("港の仲買人", "balanced"),
    ("早足の商人", "efficient"),
    ("勝負師の船長", "comeback"),
    ("目ざとい競売人", "denial"),
    ("品揃えの収集家", "title"),
)


def display_name(character_id: int) -> str:
    return CHARACTERS[character_id][0]


def pick_cast(count: int, rng: random.Random | None = None, used=()) -> list[int]:
    available = [index for index in range(len(CHARACTERS)) if index not in used]
    if count < 0 or count > len(available):
        raise ValueError("CPU character count exceeds distinct roster")
    return (rng or random.Random()).sample(available, count)


def validate_cast(cast, count: int) -> list[int]:
    if not isinstance(cast, list) or len(cast) != count or any(
        type(item) is not int or item < 0 or item >= len(CHARACTERS) for item in cast
    ) or len(set(cast)) != count:
        raise ValueError("CPUプレイヤーの指名が不正です")
    return cast[:]


def validate_strength(value) -> int:
    if type(value) is not int or value not in (0, 1, 2):
        raise ValueError("CPUプレイヤーの強さが不正です")
    return value

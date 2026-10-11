"""Experiment-only bonus rules; production Game and GameConfig stay unchanged."""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass

from quota.cards import Card, bonus
from quota.engine import Game, GameConfig, Player


@dataclass(frozen=True)
class BonusRules:
    variety_points: int = 5
    variety_copies: int = 1
    variety_min_cards: int = 5
    achievement_min: int = 7

    def __post_init__(self) -> None:
        if self.variety_points < 0 or self.variety_copies not in (1, 2, 3):
            raise ValueError("invalid variety bonus")
        if self.variety_min_cards < 5:
            raise ValueError("variety_min_cards must be at least 5")
        if self.achievement_min not in (5, 6, 7):
            raise ValueError("achievement_min must be 5, 6, or 7")

    def achievement_bonus(self, rank: int) -> int:
        if rank < self.achievement_min:
            return 0
        return 1 if rank < 7 else bonus(rank)

    def score_for(self, rank: int) -> int:
        return rank + self.achievement_bonus(rank)

    def has_variety(self, achieved: list[Card]) -> bool:
        counts = Counter(card.suit for card in achieved)
        return len(achieved) >= self.variety_min_cards and all(counts[kind] >= self.variety_copies
                   for kind in ("S", "H", "D", "C", "JOKER"))


@dataclass
class BonusGameConfig(GameConfig):
    bonus_rules: BonusRules = BonusRules()


class BonusGame(Game):
    """Run alternate rules without changing the live scoring implementation."""

    config: BonusGameConfig

    def _achieve(self, player: Player, cards: list[Card], rank: int) -> None:
        super()._achieve(player, cards, rank)
        delta = self.config.bonus_rules.achievement_bonus(rank) - bonus(rank)
        player.score += delta
        player.max_single_score = max(player.max_single_score, len(cards) + self.config.bonus_rules.achievement_bonus(rank))

    def title_awards(self, player: Player) -> list[tuple[str, int]]:
        awards = [award for award in super().title_awards(player)
                  if award[0] != "五種の品揃え"]
        rules = self.config.bonus_rules
        if self.config.title_rule and rules.variety_points and rules.has_variety(player.achieved):
            awards.insert(0, ("五種の品揃え", rules.variety_points))
        return awards

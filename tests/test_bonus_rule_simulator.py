import json
from pathlib import Path

from quota.cards import Card, bonus
from quota.engine import Game, GameConfig
from tools.bonus_rules import BonusGame, BonusGameConfig, BonusRules
from tools.bonus_rule_simulator import evaluate
from tools.cpu_character_policy import CharacterPolicy
from tools.cpu_character_simulator import run_match


def test_current_rule_experiment_matches_live_scoring():
    for seed in (3, 5, 9):
        baseline = run_match(seed, (0, 0, 0), {
            0: ("balanced", 1), 1: ("title", 1), 2: ("efficient", 2)
        }, 2)
        experiment = run_match(seed, (0, 0, 0), {
            0: ("balanced", 1), 1: ("title", 1), 2: ("efficient", 2)
        }, 2, bonus_rules=BonusRules())
        assert experiment["scores"] == baseline["scores"]
        assert experiment["title_counts"] == baseline["title_counts"]


def test_alternate_achievement_awards_start_at_five_or_six():
    for threshold in (5, 6, 7):
        rules = BonusRules(achievement_min=threshold)
        for rank in range(1, 14):
            expected = 1 if threshold <= rank < 7 else bonus(rank)
            if rank < threshold:
                expected = 0
            assert rules.achievement_bonus(rank) == expected


def test_variety_requires_each_goods_and_gold_copy_count():
    achieved = [Card(i, kind, 1 if kind != "JOKER" else None)
                for i, kind in enumerate(("S", "H", "D", "C", "JOKER"))]
    assert BonusRules().has_variety(achieved)
    assert not BonusRules(variety_copies=2).has_variety(achieved)
    assert not BonusRules(variety_min_cards=8).has_variety(achieved)
    doubled = achieved + [Card(i + 10, card.suit, card.rank)
                           for i, card in enumerate(achieved)]
    assert BonusRules(variety_copies=2).has_variety(doubled)
    assert BonusRules(variety_min_cards=10).has_variety(doubled)
    assert not BonusRules(variety_copies=3).has_variety(doubled)


def test_experimental_game_awards_points_without_changing_production_game():
    current = Game.start(GameConfig(seed=1, human_seats=[], title_rule=True))
    alternate = BonusGame.start(BonusGameConfig(
        seed=1, human_seats=[], title_rule=True,
        bonus_rules=BonusRules(achievement_min=6, variety_copies=2),
    ))
    cards = [Card(i, "S", i + 1) for i in range(6)]
    current._achieve(current.players[0], cards, 6)
    alternate._achieve(alternate.players[0], cards, 6)
    assert alternate.players[0].score == current.players[0].score + 1
    assert GameConfig().title_variety_bonus == 5


def test_policy_values_new_achievement_threshold():
    plain = CharacterPolicy("balanced", 1)
    altered = CharacterPolicy("balanced", 1, BonusRules(achievement_min=6))
    assert altered.score_for(6) == plain.score_for(6) + 1
    assert altered.score_for(7) == plain.score_for(7)


def test_bonus_experiment_pairs_same_casts_and_seats():
    roster = tuple(json.loads(Path("docs/cpu_characters_experimental.json").read_text()))
    result = evaluate(roster=roster, players=3, rounds=1, deals_per_cast=1,
                      seed=42, rules={
                          "current": BonusRules(),
                          "variety_twice": BonusRules(variety_copies=2),
                      })
    assert result["config"]["matches_per_variant"] == 60
    for row in result["summary"]["current"]["characters"].values():
        assert row["paired_score_delta"] == 0
        assert row["paired_win_share_delta"] == 0
    assert all(row["games"] == 30 for row in result["summary"]["current"]["characters"].values())

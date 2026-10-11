from dataclasses import replace
import math

from quota.cards import Card
from quota.engine import Pass, TakeQuota
from tools.cpu_character_policy import CharacterPolicy
from tools.cpu_character_simulator import evaluate, run_match
from tools.cpu_character_league import evaluate as evaluate_league
from tools.tactic_simulator import Observation, SeatObservation


def seat(*, quota=None, collection=(), achieved=(), score=0):
    return SeatObservation(quota, tuple(collection), tuple(achieved), score, 0, 1, 1)


def view(market, players=None, deck_count=30):
    if players is None:
        players = (seat(), seat(), seat())
    return Observation(
        tuple(market), (), tuple(players), 0, deck_count, 0, False,
        tuple(range(len(players))), 1, 3, 1, "normal", 0,
        tuple(TakeQuota(card.id) for card in market if card and card.suit != "JOKER")
        + (Pass(),),
    )


def test_independent_policy_uses_public_observation_and_returns_legal_actions():
    observed = view([Card(1, "S", 13), Card(2, "H", 4), Card(3, "H", 1)])
    for style in ("balanced", "efficient", "safe", "comeback", "denial", "title"):
        for level in (0, 1, 2):
            policy = CharacterPolicy(style, level)
            action = policy.choose(observed)
            assert action in observed.legal_actions
            assert not hasattr(observed, "deck")
            assert not hasattr(observed, "removed")


def test_counting_inference_reacts_to_publicly_removed_cards():
    observation = view([Card(1, "S", 8)])
    basic = CharacterPolicy("balanced", 0)
    counting = CharacterPolicy("balanced", 1)
    after = replace(observation, discard=tuple(Card(100 + i, "S", i + 1) for i in range(12)))
    assert basic.expected_deck(observation, "S") == basic.expected_deck(after, "S")
    assert counting.expected_deck(after, "S") < counting.expected_deck(observation, "S")


def test_advanced_inference_remembers_reshuffled_market_and_resets_next_round():
    policy = CharacterPolicy("balanced", 2)
    before = view([Card(1, "S", 3), Card(2, "H", 4)])
    after = replace(before, market=(Card(3, "D", 5), Card(4, "C", 6)))
    policy.on_transition(before, after, None, "declaration")
    assert math.isclose(policy.returned["S"], 1 - 2 / 32)
    policy.on_transition(after, before, None, "new_round")
    assert not policy.returned


def test_all_new_players_and_legacy_comparator_finish():
    legacy = (0, 1, 2)
    result = run_match(42, legacy, {
        0: ("balanced", 0), 1: ("denial", 1), 2: ("title", 2),
    }, 2)
    assert len(result["scores"]) == 3
    assert sum(result["win_shares"]) == 1
    assert len(result["policy_stats"]) == 3
    assert all(score >= 0 for score in result["scores"])


def test_paired_screen_is_reproducible_and_seat_balanced():
    result = evaluate(
        deals=2, seed=42, players=3, rounds=1,
        styles=("balanced", "safe"), levels=(0, 2),
    )
    assert len(result["trials"]) == 2 * 3 * 2 * 2
    assert set(result["summary"]) == {"balanced:0", "balanced:2", "safe:0", "safe:2"}
    assert result == evaluate(
        deals=2, seed=42, players=3, rounds=1,
        styles=("balanced", "safe"), levels=(0, 2),
    )


def test_league_rotates_every_cast_through_all_seats():
    roster = (
        {"id": "A", "style": "balanced", "inference": 0},
        {"id": "B", "style": "safe", "inference": 1},
        {"id": "C", "style": "comeback", "inference": 2},
    )
    result = evaluate_league(
        roster=roster, players=3, rounds=1, deals_per_cast=2, seed=42
    )
    assert len(result["trials"]) == 6
    assert all(row["games"] == 6 for row in result["summary"].values())
    assert sum(row["mean_win_share"] for row in result["summary"].values()) == 1

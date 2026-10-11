from dataclasses import replace

from quota.cards import Card
from quota.engine import TakeQuota
from tools.strategy_policies import StrategyPolicy, bundles, quota_outlook
from tools.strategy_simulator import evaluate
from tools.tactic_policies import Policy
from tools.tactic_simulator import Observation, SeatObservation, run_game


def seat(*, quota=None, collection=(), achieved=(), score=0, achieve_count=0):
    return SeatObservation(
        quota, tuple(collection), tuple(achieved), score, achieve_count, 1, 1
    )


def view(*, market=(), players=None, current=0, round_index=1, round_count=1,
         deck_count=30):
    if players is None:
        players = (seat(), seat(), seat())
    return Observation(
        tuple(market), (), tuple(players), current, deck_count, 0, False,
        tuple(range(len(players))), round_index, round_count, 1,
        "normal", 0, tuple(TakeQuota(card.id) for card in market if card),
    )


def test_macro_and_tactical_reasons_are_separable():
    card = Card(1, "S", 13)
    observation = view(
        market=(card, Card(2, "S", 2), Card(3, "S", 3), Card(4, "S", 4)),
        players=(seat(score=0), seat(score=18), seat(score=5)),
    )
    assert "score_deficit" in StrategyPolicy("macro").active_reasons(observation)
    assert "large_quota_opening" in StrategyPolicy("tactical").active_reasons(observation)
    assert StrategyPolicy("fixed").active_reasons(observation) == ()


def test_public_title_intent_requires_an_actual_choice():
    prior = (Card(10, "S", 1),)
    same = Card(11, "S", 3)
    alternative = Card(12, "H", 3)
    before = view(market=(same, alternative), players=(seat(achieved=prior), seat(), seat()))
    after = replace(before, market=(None, alternative))
    policy = StrategyPolicy("tactical")
    policy.on_transition(before, after, TakeQuota(same.id), "action")
    assert policy.mono_intent[0] == 1
    policy.on_transition(before, after, None, "new_round")
    assert policy.mono_intent[0] == 0


def test_mono_title_threat_stops_after_title_is_secured():
    two = (Card(10, "S", 1), Card(11, "S", 1))
    three = two + (Card(12, "S", 1),)
    policy = StrategyPolicy("tactical")
    threatened = view(players=(seat(), seat(achieved=two, achieve_count=2), seat()))
    secured = view(players=(seat(), seat(achieved=three, achieve_count=3), seat()))
    assert "rival_mono_title" in policy.active_reasons(threatened)
    assert "rival_mono_title" not in policy.active_reasons(secured)


def test_quota_proxy_and_bundle_parser_use_visible_cards_only():
    cards = (Card(1, "S", 2), Card(2, "S", 4), Card(3, "H", 1))
    assert bundles(cards) == [("S", False), ("H", False)]
    observation = view(market=(Card(4, "S", 1),))
    assert quota_outlook(observation, observation.market[0]) == 1
    assert not hasattr(observation, "deck")
    assert not hasattr(observation, "removed")


def test_simulator_can_propose_a_special_declaration():
    class DeclareDouble(Policy):
        def __init__(self):
            self.saw_double = False

        def special_declaration(self, observation):
            if observation.plan == "normal" and observation.players[observation.current].double_action_left:
                return "double"
            return "baseline"

        def on_transition(self, before, after, action, phase):
            if phase == "declaration" and after.plan == "double":
                self.saw_double = True

    policy = DeclareDouble()
    run_game(11, (0, 1, 2), 0, 1, lambda: policy)
    assert policy.saw_double


def test_four_mode_ablation_replays_and_reports_reasons():
    result = evaluate(deals=2, seed=42, players=3, rounds=1)
    assert result["summary"]["fixed"]["mean_score_delta"] == 0
    assert result["summary"]["fixed"]["changed"] == 0
    assert result["summary"]["fixed"]["switches"] == 0
    assert result["summary"]["macro"]["trigger_counts"]
    assert result["summary"]["tactical"]["trigger_counts"]
    assert len(result["trials"]) == 6
    assert set(result["interaction"]) == {"score", "win_share", "margin"}
    assert result == evaluate(deals=2, seed=42, players=3, rounds=1)

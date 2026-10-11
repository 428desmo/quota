from quota.ai import _maybe_special
from quota.cards import Card, make_deck
from quota.engine import Collect, Game, GameConfig, Pass, TakeQuota
from tools.tactic_policies import BlockWithImpossibleQuota, DenySequence, Policy
from tools.tactic_simulator import (
    Observation,
    SeatObservation,
    evaluate,
    observe,
    run_game,
)


def seat(quota=None, collection=(), achieved=()):
    return SeatObservation(quota, tuple(collection), tuple(achieved), 0, 0, 1, 1)


def view(market, players, discard=(), deck_count=10):
    return Observation(
        tuple(market), tuple(discard), tuple(players), 0, deck_count,
        0, False, tuple(range(len(players))), 1, 1, 10, "normal", 0, (),
    )


def test_noop_policy_replays_identical_games_in_every_seat():
    result = evaluate(
        deals=3, seed=42, players=3, rounds=2,
        candidate_factory=Policy,
    )
    assert result["summary"]["trials"] == 9
    assert result["summary"]["mean_score_delta"] == 0
    assert result["summary"]["mean_win_share_delta"] == 0
    assert all(trial["control"]["score"] == trial["candidate"]["score"] for trial in result["trials"])
    assert sorted({trial["focal"] for trial in result["trials"]}) == [0, 1, 2]


def test_policy_observes_public_transitions_across_rounds():
    class Recorder(Policy):
        def __init__(self):
            self.phases = []

        def on_transition(self, before, after, action, phase):
            assert not hasattr(after, "deck")
            assert not hasattr(after, "removed")
            self.phases.append(phase)

    recorder = Recorder()
    run_game(7, (0, 1, 2), 0, 2, lambda: recorder)
    assert "action" in recorder.phases
    assert "new_round" in recorder.phases


def test_sequence_denial_considers_rivals_numeric_bonus():
    own_quota = Card(1, "S", 4)
    rival_quota = Card(2, "S", 3)
    rival_collected = Card(3, "S", 7)
    preferred_denial = Card(4, "S", 7)
    other = Card(5, "S", 8)
    observation = view(
        [preferred_denial, other],
        [
            seat(own_quota, [Card(6, "S", 10), Card(7, "S", 11)]),
            seat(rival_quota, [rival_collected]),
            seat(),
        ],
    )
    decision = DenySequence().decide(observation, Collect((other.id,)))
    assert decision.applicable
    assert decision.action == Collect((preferred_denial.id,))


def test_impossible_quota_block_uses_only_publicly_unavailable_cards():
    cards = make_deck()
    stock = [card for card in cards if card.suit == "S"]
    jokers = [card for card in cards if card.suit == "JOKER"]
    target = next(card for card in stock if card.rank == 13)
    rival_quota = next(card for card in stock if card.rank == 5)
    rival_collected = next(card for card in stock if card.rank == 7)
    discarded = [card for card in stock if card.id not in (target.id, rival_quota.id, rival_collected.id)][:16]
    observation = view(
        [target],
        [seat(), seat(rival_quota, [rival_collected]), seat()],
        discarded + jokers,
    )
    decision = BlockWithImpossibleQuota().decide(observation, Pass())
    assert decision.applicable
    assert decision.action == TakeQuota(target.id)


def test_special_guard_vetoes_only_experimental_decisions():
    game = Game.start(GameConfig(num_players=3, seed=11, human_seats=[], special_actions_rule=True))
    game.market = [card for card in game.deck if card.suit == "JOKER"][:2]
    player = game.players[game.current]
    before = player.reshuffle_take_left
    game.experimental_special_guard = lambda kind: False
    _maybe_special(game)
    assert player.reshuffle_take_left == before
    assert game.plan == "normal"
    del game.experimental_special_guard
    _maybe_special(game)
    assert player.reshuffle_take_left == before - 1
    assert game.plan == "reshuffle"


def test_public_observation_excludes_hidden_deck_and_removed_cards():
    game = Game.start(GameConfig(num_players=3, seed=1, human_seats=[]))
    seen = observe(game)
    assert seen.deck_count == len(game.deck)
    assert not hasattr(seen, "deck")
    assert not hasattr(seen, "removed")

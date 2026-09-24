import random

import pytest

from quota.ai import choose_action
from quota.cards import bonus, make_deck, score_for
from quota.engine import Collect, Game, GameConfig, Pass, TakeQuota


def test_deck_has_108_unique_cards():
    deck = make_deck(2)
    assert len(deck) == 108
    assert len({c.id for c in deck}) == 108
    assert sum(c.suit == "JOKER" for c in deck) == 4


@pytest.mark.parametrize("rank,pts", [(1, 1), (6, 6), (7, 8), (9, 10), (10, 13), (12, 15), (13, 19)])
def test_score_table(rank, pts):
    assert score_for(rank) == pts
    assert bonus(rank) == pts - rank


def test_same_seed_same_opening():
    a = Game.start(GameConfig(seed=7, num_players=3))
    b = Game.start(GameConfig(seed=7, num_players=3))
    assert [c.id for c in a.market] == [c.id for c in b.market]
    assert a.current == b.current
    assert [c.id for c in a.removed] == [c.id for c in b.removed]


def test_ace_scores_immediately():
    game = Game.start(GameConfig(seed=1, num_players=3))
    ace = next(c for c in game.market if c.rank == 1)
    seat = game.current
    game.step(TakeQuota(ace.id))
    player = game.players[seat]
    assert player.quota is None
    assert player.score == 1
    assert player.achieve_count == 1
    assert game.market_size() == len(game.market)


def test_invariants_hold_for_random_games():
    rng = random.Random(0)
    for n, players in enumerate((3, 4)):
        for k in range(30):
            game = Game.start(GameConfig(num_players=players, seed=n * 1000 + k))
            removed = [c.id for c in game.removed]
            while not game.finished:
                game.step(rng.choice(game.legal_actions()))
                _assert_invariants(game, removed)
            assert game.end_reason in {"DECK", "STALL"}
            assert len(game.all_card_ids()) == 108


def test_replay_matches():
    seed = 42
    rng = random.Random(1)
    game = Game.start(GameConfig(num_players=3, seed=seed))
    script = []
    while not game.finished:
        action = rng.choice(game.legal_actions())
        script.append(action.key())
        game.step(action)
    replay = Game.start(GameConfig(num_players=3, seed=seed))
    for key in script:
        action = next(a for a in replay.legal_actions() if a.key() == key)
        replay.step(action)
    assert [p.score for p in replay.players] == [p.score for p in game.players]
    assert replay.end_reason == game.end_reason
    assert [c.id for c in replay.market] == [c.id for c in game.market]


def test_cpu_game_finishes():
    game = Game.start(GameConfig(num_players=3, seed=3, human_seats=[]))
    guard = 0
    while not game.finished:
        game.step(choose_action(game))
        guard += 1
        assert guard < 5000
    assert sum(p.score for p in game.players) > 0


def test_joker_cannot_be_quota():
    game = Game.start(GameConfig(seed=1, num_players=3))
    game.market = [c for c in game.deck if c.suit == "JOKER"][:2]
    kinds = {type(a) for a in game.legal_actions()}
    assert kinds == {Pass}


def test_collect_matching_suit_only():
    game = Game.start(GameConfig(seed=2, num_players=3))
    card = next(c for c in game.market if c.rank is not None and c.rank >= 2)
    game.step(TakeQuota(card.id))
    # move to that player again by passing others if needed — just inspect next legal when current has quota
    # force seat back
    owner = next(i for i, p in enumerate(game.players) if p.quota is not None)
    game.current = owner
    for action in game.legal_actions():
        if isinstance(action, Collect):
            quota = game.players[owner].quota
            assert quota is not None and quota.rank is not None
            need = quota.rank - 1 - len(game.players[owner].collection)
            assert 1 <= len(action.card_ids) <= need
            for card_id in action.card_ids:
                picked = next(c for c in game.market if c.id == card_id)
                assert picked.suit == quota.suit or picked.suit == "JOKER"


def test_collect_several_then_refill_on_next_turn():
    game = Game.start(GameConfig(seed=4, num_players=3))
    quota = next(c for c in game.market if c.rank is not None and c.rank >= 3)
    game.step(TakeQuota(quota.id))
    owner = next(i for i, p in enumerate(game.players) if p.quota is not None)
    game.current = owner
    suit = game.players[owner].quota.suit  # type: ignore[union-attr]
    extras = []
    for card in list(game.deck):
        if card.suit == suit:
            game.deck.remove(card)
            extras.append(card)
        if len(extras) == 2:
            break
    assert len(extras) == 2
    game.deck.extend(game.market[:2])
    game.market = extras + game.market[2:]
    before = len(game.deck)
    game.step(Collect(tuple(c.id for c in extras)))
    assert game.players[owner].quota is not None
    assert len(game.players[owner].collection) == 2
    assert len(game.market) == game.market_size()
    assert len(game.deck) == before - 2


def test_deck_ends_at_the_start_of_the_next_turn():
    game = Game.start(GameConfig(seed=5, num_players=3))
    card = next(c for c in game.market if c.rank is not None and c.rank >= 2)
    game.deck.clear()
    game.step(TakeQuota(card.id))
    assert game.finished
    assert game.end_reason == "DECK"
    owner = next(p for p in game.players if p.quota is not None)
    assert owner.quota is not None
    assert len(game.market) == game.market_size() - 1


def _assert_invariants(game: Game, removed_ids: list[int]) -> None:
    ids = game.all_card_ids()
    assert len(ids) == 108
    assert len(set(ids)) == 108
    assert [c.id for c in game.removed] == removed_ids
    assert len(game.removed) == 8
    if not game.finished:
        assert len(game.market) == game.market_size()
        assert 0 <= game.no_gain_streak < game.config.resolved_stall_threshold()
    for p in game.players:
        if p.quota is None:
            assert p.collection == []
        else:
            assert p.quota.suit != "JOKER"
            assert p.quota.rank is not None
            assert 1 + len(p.collection) < p.quota.rank
            for card in p.collection:
                assert card.suit == p.quota.suit or card.suit == "JOKER"

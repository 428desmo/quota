import random

import pytest

from quota.ai import choose_action
from quota.cards import Card, bonus, make_deck, score_for, sequence_bonus
from quota.engine import Bundle, Collect, Game, GameConfig, Pass, TakeQuota


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
    market_len = len(game.market)
    game.step(Collect((extras[0].id,)))
    assert game.current == owner
    assert len(game.players[owner].collection) == 1
    assert len(game.market) == market_len - 1
    game.step(Collect((extras[1].id,)))
    assert game.players[owner].quota is not None
    assert len(game.players[owner].collection) == 2
    assert len(game.market) == market_len - 2
    assert len(game.deck) == before
    game.step(Pass())
    assert game.current != owner
    assert len(game.market) == game.market_size()
    assert len(game.deck) == before - 2


def test_taking_the_last_eligible_card_ends_the_turn():
    game = Game.start(GameConfig(seed=4, num_players=3))
    quota = next(c for c in game.market if c.rank is not None and c.rank >= 4)
    game.step(TakeQuota(quota.id))
    owner = next(i for i, p in enumerate(game.players) if p.quota is not None)
    game.current = owner
    suit = game.players[owner].quota.suit  # type: ignore[union-attr]
    extra = next(c for c in game.deck if c.suit == suit)
    game.deck.remove(extra)
    others = [c for c in game.market if c.suit != suit and c.suit != "JOKER"]
    game.market = [extra, *others[: game.market_size() - 1]]
    game.step(Collect((extra.id,)))
    assert game.current != owner
    assert len(game.players[owner].collection) == 1
    assert len(game.market) == game.market_size()


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


def test_title_bonus_needs_three_achieves_and_scores_each_award():
    game = Game.start(GameConfig(seed=1, num_players=3, title_rule=True))
    player = game.players[0]
    player.bundles = [Bundle("S", False), Bundle("S", True)]
    assert game.title_awards(player) == []
    player.bundles.append(Bundle("S", False))
    assert game.title_awards(player) == [("単色達成", 15)]
    player.bundles = [Bundle("S", False), Bundle("H", False), Bundle("C", False)]
    assert game.title_awards(player) == [("生粋の買い付け", 5)]
    player.bundles = [Bundle("S", False), Bundle("S", False), Bundle("S", False)]
    assert game.title_points(player) == 20
    assert game.final_score(player) == player.score + 20


def test_sequence_bonus_examples():
    def card(suit, rank, n):
        return Card(n, suit, rank)

    joker = Card(99, "JOKER", None)
    assert sequence_bonus([card("H", 5, 1), card("H", 1, 2), card("C", 1, 3), card("C", 1, 4), card("D", 3, 5)]) == 4
    assert sequence_bonus([card("H", 3, 1), card("H", 4, 2), card("C", 3, 3), card("C", 2, 4), card("C", 5, 5), card("D", 3, 6)]) == 3
    assert sequence_bonus([card("S", 6, 1), joker, card("S", 7, 2)]) == 0
    assert sequence_bonus([card("S", 13, 1), card("H", 1, 2)]) == 0
    assert sequence_bonus([card("S", 4, 1), card("H", 4, 2), card("D", 4, 3)]) == 4


def test_collect_order_is_kept_and_scores_only_when_enabled():
    game = Game.start(GameConfig(seed=8, num_players=3, sequence_rule=True))
    quota = next(c for c in game.market if c.rank == 3)
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
    game.deck.extend(game.market[:2])
    game.market = extras + game.market[2:]
    ordered = (extras[1].id, extras[0].id)
    game.step(Collect(ordered))
    assert [c.id for c in game.players[owner].achieved] == [quota.id, extras[1].id, extras[0].id]
    assert game.sequence_points(game.players[owner]) == sequence_bonus(game.players[owner].achieved)
    plain = Game.start(GameConfig(seed=8, num_players=3))
    assert plain.sequence_points(plain.players[0]) == 0


def test_special_actions_start_unused_and_stay_off_by_default():
    plain = Game.start(GameConfig(seed=1, num_players=3))
    assert all(p.reshuffle_take_left == 0 and p.double_action_left == 0 for p in plain.players)
    with pytest.raises(ValueError):
        plain.declare_double()


def test_reshuffle_and_take_replaces_the_market_without_touching_stall():
    game = Game.start(GameConfig(seed=5, num_players=3, special_actions_rule=True))
    seat = game.current
    before = [c.id for c in game.market]
    streak = game.no_gain_streak
    game.declare_reshuffle()
    assert game.players[seat].reshuffle_take_left == 0
    assert game.plan == "reshuffle"
    assert [c.id for c in game.market] != before
    assert len(game.market) == game.market_size()
    assert game.no_gain_streak == streak
    assert game.stall_flag is False
    assert game.reshuffle_count == 0
    with pytest.raises(ValueError):
        game.declare_double()
    game.step(Pass())
    assert game.current != seat
    assert game.no_gain_streak == 1
    assert game.plan == "normal"


def test_double_action_takes_two_actions_and_counts_one_miss():
    game = Game.start(GameConfig(seed=6, num_players=3, special_actions_rule=True))
    seat = game.current
    turn = game.turn_number
    game.declare_double()
    assert game.players[seat].double_action_left == 0
    game.step(Pass())
    assert game.current == seat
    assert game.turn_number == turn
    assert game.double_stage == 2
    assert len(game.market) == game.market_size()
    game.step(Pass())
    assert game.current != seat
    assert game.turn_number == turn + 1
    assert game.no_gain_streak == 1
    assert game.plan == "normal"
    assert game.double_stage == 0


def test_double_action_ends_when_the_refill_empties_the_deck():
    game = Game.start(GameConfig(seed=7, num_players=3, special_actions_rule=True))
    seat = game.current
    take = next(action for action in game.legal_actions() if isinstance(action, TakeQuota))
    game.declare_double()
    game.deck.clear()
    game.step(take)
    assert game.finished
    assert game.end_reason == "DECK"
    assert game.current == seat
    player = game.players[seat]
    assert player.quota is not None or player.achieve_count == 1


def test_cpu_with_special_actions_finishes():
    game = Game.start(GameConfig(num_players=3, seed=9, human_seats=[], special_actions_rule=True))
    guard = 0
    while not game.finished:
        game.step(choose_action(game))
        _assert_invariants(game, [c.id for c in game.removed])
        guard += 1
        assert guard < 5000
    assert all(p.reshuffle_take_left <= 1 and p.double_action_left <= 1 for p in game.players)


def _assert_invariants(game: Game, removed_ids: list[int]) -> None:
    ids = game.all_card_ids()
    assert len(ids) == 108
    assert len(set(ids)) == 108
    assert [c.id for c in game.removed] == removed_ids
    assert len(game.removed) == 8
    if not game.finished:
        if not game.turn_gain:
            assert len(game.market) == game.market_size()
        assert 0 <= game.no_gain_streak < game.config.resolved_stall_threshold()
    take_cap = game.config.reshuffle_take_uses if game.config.special_actions_rule else 0
    double_cap = game.config.double_action_uses if game.config.special_actions_rule else 0
    for p in game.players:
        assert 0 <= p.reshuffle_take_left <= take_cap
        assert 0 <= p.double_action_left <= double_cap
        if p.quota is None:
            assert p.collection == []
        else:
            assert p.quota.suit != "JOKER"
            assert p.quota.rank is not None
            assert 1 + len(p.collection) < p.quota.rank
            for card in p.collection:
                assert card.suit == p.quota.suit or card.suit == "JOKER"

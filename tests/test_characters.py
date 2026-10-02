import random

from quota.ai import choose_action
from quota.characters import (
    ADJECTIVES,
    CONSISTENT_CHANCE,
    NOUNS,
    STRATEGY_COUNT,
    TITLE_STANCE_COUNT,
    TRIGGER_COUNT,
    Mind,
    assign_seats,
    character_count,
    character_name,
    decode,
    encode,
    pick_character,
)
from quota.engine import Abandon, Bundle, Collect, Game, GameConfig, Pass, TakeQuota


def test_every_character_has_its_own_name():
    names = [character_name(character_id) for character_id in range(character_count())]
    assert len(names) == STRATEGY_COUNT * TRIGGER_COUNT * STRATEGY_COUNT * TITLE_STANCE_COUNT
    assert len(set(names)) == len(names)
    pool = {adjective + noun for adjective in ADJECTIVES for noun in NOUNS}
    assert set(names) <= pool
    assert len(ADJECTIVES) * len(NOUNS) >= character_count()


def test_names_do_not_describe_the_strategy():
    blocked = ("粘", "放棄", "ノルマ", "エース", "見切", "戦略", "柔軟", "かぶり")
    for name in (character_name(character_id) for character_id in range(character_count())):
        assert not any(word in name for word in blocked)


def test_consistent_characters_are_chosen_more_often():
    rng = random.Random(0)
    picks = [pick_character(rng) for _ in range(4000)]
    consistent = 0
    for character_id in picks:
        before, _trigger, after, _stance = decode(character_id)
        consistent += before == after
    rate = consistent / len(picks)
    assert abs(rate - CONSISTENT_CHANCE) < 0.03
    assert rate > 0.5


def test_same_strategy_ignores_the_trigger():
    game = Game.start(GameConfig(num_players=3, seed=2, human_seats=[]))
    game.current = 0
    game.players[1].score = 30
    steady = Mind(encode(8, 0, 8))
    other_trigger = Mind(encode(8, 5, 8))
    assert steady.choose(game) == other_trigger.choose(game)
    assert steady.switched is False


def test_a_trigger_can_change_the_quota_it_takes():
    game = Game.start(GameConfig(num_players=3, seed=4, human_seats=[]))
    game.current = 0
    game.players[0].quota = None
    game.players[0].collection = []
    ace = next(card for card in game.deck if card.rank == 1)
    big = next(card for card in game.deck if card.rank == 9)
    game.market = [ace, big, None, None, None, None, None]
    game.players[1].score = 30
    mind = Mind(encode(8, 0, 5))
    action = mind.choose(game)
    assert isinstance(action, TakeQuota)
    assert action.card_id == big.id
    assert mind.switched is True


def test_dead_quota_strategy_abandons_an_impossible_set():
    game = Game.start(GameConfig(num_players=3, seed=5, human_seats=[]))
    game.current = 0
    quota = next(card for card in game.deck if card.rank == 13 and card.suit == "S")
    game.players[0].quota = quota
    game.players[0].collection = []
    taken = [card for card in game.deck if card.suit == "S" and card is not quota]
    game.players[1].achieved = taken
    game.market = [card for card in game.deck if card.suit == "H"][:7]
    mind = Mind(encode(1, 0, 1))
    assert isinstance(mind.choose(game), Abandon)


def _market_pair(game, suit, rank, other_suit, other_rank):
    wanted = next(card for card in game.deck if card.suit == suit and card.rank == rank)
    other = next(card for card in game.deck if card.suit == other_suit and card.rank == other_rank)
    game.market = [other, wanted, None, None, None, None, None]
    return wanted, other


def test_title_hunger_keeps_the_single_suit_and_skips_wilds():
    game = Game.start(GameConfig(num_players=3, seed=7, human_seats=[], title_rule=True))
    game.current = 0
    player = game.players[0]
    player.quota = None
    player.collection = []
    player.bundles = [Bundle("S", False)]
    wanted, other = _market_pair(game, "S", 6, "H", 1)
    plain = Mind(encode(8, 0, 8, 0))
    hungry = Mind(encode(8, 0, 8, 2))
    plain_take = plain.choose(game)
    hungry_take = hungry.choose(game)
    assert isinstance(plain_take, TakeQuota)
    assert isinstance(hungry_take, TakeQuota)
    assert plain_take.card_id == other.id
    assert hungry_take.card_id == wanted.id

    quota = next(card for card in game.deck if card.suit == "S" and card.rank == 3)
    suited = next(card for card in game.deck if card.suit == "S" and card is not quota)
    joker = next(card for card in game.deck if card.suit == "JOKER")
    player.quota = quota
    player.bundles = [Bundle("S", False)]
    game.market = [joker, suited, None, None, None, None, None]
    kept = hungry.choose(game)
    spoiled = plain.choose(game)
    assert isinstance(kept, Collect)
    assert kept.card_ids == (suited.id,)
    assert isinstance(spoiled, Collect)
    assert joker.id in spoiled.card_ids


def test_title_comeback_only_matters_while_the_points_can_catch_the_leader():
    game = Game.start(GameConfig(num_players=3, seed=8, human_seats=[], title_rule=True))
    game.current = 0
    player = game.players[0]
    player.quota = None
    player.collection = []
    player.bundles = [Bundle("S", False)]
    wanted, other = _market_pair(game, "S", 6, "H", 1)
    mind = Mind(encode(8, 0, 8, 1))
    game.players[1].score = 30
    far = mind.choose(game)
    assert isinstance(far, TakeQuota)
    assert far.card_id == other.id
    game.players[1].score = 10
    close = mind.choose(game)
    assert isinstance(close, TakeQuota)
    assert close.card_id == wanted.id


def test_titles_off_ignores_the_stance():
    game = Game.start(GameConfig(num_players=3, seed=9, human_seats=[], title_rule=False))
    game.current = 0
    player = game.players[0]
    player.quota = None
    player.collection = []
    player.bundles = [Bundle("S", False)]
    _wanted, other = _market_pair(game, "S", 6, "H", 1)
    hungry = Mind(encode(8, 0, 8, 2))
    action = hungry.choose(game)
    assert isinstance(action, TakeQuota)
    assert action.card_id == other.id
    assert not isinstance(action, Pass)


def test_assigned_cpus_finish_a_game_under_their_names():
    game = Game.start(GameConfig(
        num_players=3,
        seed=6,
        human_seats=[0],
        sequence_rule=True,
        title_rule=True,
        special_actions_rule=True,
    ))
    assign_seats(game, random.Random(6))
    assert game.players[0].name != character_name(0) or game.players[0].is_human
    assert game.players[1].name != game.players[2].name
    assert "CPU" not in game.players[1].name
    guard = 0
    while not game.finished:
        action = choose_action(game)
        assert not isinstance(action, type(None))
        game.step(action)
        guard += 1
        assert guard < 5000
    assert game.end_reason in ("DECK", "STALL")

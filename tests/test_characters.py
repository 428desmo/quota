import random

from quota.ai import choose_action
from quota.characters import (
    ADJECTIVES,
    CONSISTENT_CHANCE,
    NOUNS,
    STRATEGY_COUNT,
    TRIGGER_COUNT,
    Mind,
    assign_seats,
    character_count,
    character_name,
    decode,
    encode,
    pick_character,
)
from quota.engine import Abandon, Game, GameConfig, TakeQuota


def test_every_character_has_its_own_name():
    names = [character_name(character_id) for character_id in range(character_count())]
    assert len(names) == STRATEGY_COUNT * TRIGGER_COUNT * STRATEGY_COUNT
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
        before, _trigger, after = decode(character_id)
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

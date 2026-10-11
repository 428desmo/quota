from quota.characters import mind_for
from quota.cpu_observation import observe
from quota.cpu_roster import CHARACTERS, pick_cast
from quota.engine import Game, GameConfig
from quota.live_cpu import bind_cast, observe_transition
from tools.serve_unity_web import UnityHall


def test_cast_nomination_is_ordered_unique_and_leader_only():
    hall = UnityHall()
    opened = hall.create({"players": 3, "name": "Host", "cpu_strength": 2}, "host")
    assert len(set(opened["cpu_cast"])) == 3
    assert all(0 <= cid < len(CHARACTERS) for cid in opened["cpu_cast"])
    for bad in ([0, 0, 1], [0, 1], [0, 1, 7], [0, 1, "2"]):
        try:
            hall.nominate({"cpu_cast": bad}, "host")
        except ValueError:
            pass
        else:
            raise AssertionError(f"accepted invalid cast {bad!r}")
    hall.join({"table": opened["table_id"], "name": "Guest"}, "guest")
    try:
        hall.nominate({"cpu_cast": [5, 2, 0]}, "guest")
    except ValueError:
        pass
    else:
        raise AssertionError("guest nominated CPUs")
    hall.nominate({"cpu_cast": [5, 2, 0]}, "host")
    started = hall.begin("host")
    assert started["cpus"] == ["好奇心旺盛なソフィア"]
    mind = mind_for(hall.tables[opened["table_id"]].game.players[2])
    assert mind.character_id == 5
    assert mind.policy.inference == 2


def test_each_strength_plays_a_complete_cpu_match():
    for strength in range(3):
        game = Game.start(GameConfig(num_players=3, seed=11, names=["A", "B", "C"],
            human_seats=[], rounds=1, sequence_rule=True, title_rule=True, special_actions_rule=True))
        bind_cast(game, [0, 1, 2], strength)
        for _ in range(1000):
            if game.finished:
                break
            before = observe(game)
            action = mind_for(game.players[game.current]).choose(game)
            game.step(action)
            observe_transition(game, before, observe(game), action, "action")
        assert game.finished
        assert [mind_for(player).policy.inference for player in game.players] == [strength] * 3


def test_cast_picker_never_duplicates():
    assert len(set(pick_cast(4))) == 4
    assert not set(pick_cast(2, used=(0, 1, 2, 3))) & {0, 1, 2, 3}

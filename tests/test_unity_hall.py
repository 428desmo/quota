import sys
import pytest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from tools.serve_unity_web import UnityHall


def test_editor_and_browser_see_the_same_recruiting_table():
    hall = UnityHall()
    created = hall.create({"players": 4, "name": "竜二郎"}, "unity")
    assert created["phase"] == "recruiting"
    public = hall.snapshot("safari")
    assert public["phase"] == "hall"
    assert public["tables"] == [{
        "id": created["table_id"],
        "leader": "竜二郎",
        "players": 4,
        "seated": 1,
        "status": "募集中",
        "observers": 0,
    }]
    joined = hall.join({"table": created["table_id"], "name": "ともだち"}, "safari")
    assert [seat["name"] for seat in joined["seats"]] == ["竜二郎", "ともだち"]
    assert hall.snapshot("unity")["seats"][1]["name"] == "ともだち"


def test_start_preserves_roster_cast_rules_and_seed():
    hall = UnityHall()
    opened = hall.create({"players": 3, "name": "A", "simple": False, "sequence": True}, "a")
    assert all(not name.startswith("CPU") for name in opened["cpus"])
    hall.join({"table": opened["table_id"], "name": "B"}, "b")
    started = hall.begin("a")
    assert [s["name"] for s in started["seats"]] == ["A", "B"]
    assert started["cpus"] == opened["cpus"][:1]
    assert started["seed"] == opened["seed"]
    assert started["options"]["sequence"]
    other = hall.snapshot("b")
    assert other["seed"] == started["seed"]
    assert other["you"]["seat"] == 1
    assert [p.name for p in hall.tables[started["table_id"]].game.players][:2] == ["A", "B"]
    assert hall.begin("a")["actions"] == started["actions"]


def test_only_owner_can_act_and_stale_revision_cannot_repeat():
    import pytest
    hall = UnityHall()
    opened = hall.create({"players": 3, "name": "A"}, "a")
    hall.join({"table": opened["table_id"], "name": "B"}, "b")
    hall.begin("a")
    table = hall.tables[opened["table_id"]]
    table.game.current = 1
    with pytest.raises(ValueError, match="あなたの手番"):
        hall.action({"key": "pass", "revision": 0}, "a")
    result = hall.action({"key": "pass", "revision": 0}, "b")
    assert result["actions"] == ["pass"]
    with pytest.raises(ValueError, match="盤面が更新"):
        hall.action({"key": "pass", "revision": 0}, "b")
    assert hall.snapshot("a")["actions"] == ["pass"]


@pytest.mark.parametrize("advanced", [False, True])
def test_cpu_moves_are_shared_and_game_finishes(advanced):
    from quota.engine import Game, GameConfig, TakeQuota, Collect, Abandon, Pass
    hall = UnityHall()
    opened = hall.create({"players": 3, "name": "A", "simple": not advanced, "sequence": advanced, "title": advanced, "special": advanced}, "a")
    hall.join({"table": opened["table_id"], "name": "B"}, "b")
    hall.begin("a")
    table = hall.tables[opened["table_id"]]
    replay = Game.start(GameConfig(num_players=3, seed=table.seed, names=[p.name for p in table.game.players], human_seats=[0, 1], sequence_rule=advanced, title_rule=advanced, special_actions_rule=advanced, rounds=3 if advanced else 1))
    for _ in range(1500):
        if table.game.finished: break
        if table.game.awaiting_next_round:
            hall.action({"key": "next_round", "revision": len(table.actions)}, "a")
        elif table.game.players[table.game.current].is_human:
            hall.action({"key": "pass", "revision": len(table.actions)}, table.humans[table.game.current].client)
        else:
            table.cpu_at = 0
            hall.snapshot("b")
    assert table.game.finished
    for key in table.actions:
        if key == "double": replay.declare_double()
        elif key == "reshuffle": replay.declare_reshuffle()
        elif key == "next_round": replay.begin_next_round()
        elif key == "pass": replay.step(Pass())
        elif key == "abandon": replay.step(Abandon())
        elif key.startswith("take:"): replay.step(TakeQuota(int(key[5:])))
        elif key.startswith("collect:"): replay.step(Collect(tuple(map(int, key[8:].split(',')))))
    assert replay.finished
    assert [p.score for p in replay.players] == [p.score for p in table.game.players]


def test_host_can_watch_without_losing_leadership_or_cpu_cast():
    hall = UnityHall()
    opened = hall.create({"players": 3, "name": "A"}, "a")
    watched = hall.participation({"sit_out": True}, "a")
    assert watched["you"]["observer"]
    assert watched["you"]["leader"]
    assert watched["you"]["seat"] == -1
    assert watched["seats"] == []
    assert len(watched["cpus"]) == 3
    restored = hall.participation({"sit_out": False}, "a")
    assert restored["you"]["seat"] == 0
    assert restored["cpu_cast"] == opened["cpu_cast"]
    hall.participation({"sit_out": True}, "a")
    started = hall.begin("a")
    assert started["phase"] == "playing"
    assert not any(p.is_human for p in hall.tables[opened["table_id"]].game.players)

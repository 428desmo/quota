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


def playing_pair():
    hall = UnityHall()
    opened = hall.create({"name": "A", "players": 3, "turn_timeout": 1}, "a")
    hall.join({"table": opened["table_id"], "name": "B"}, "b")
    hall.begin("a")
    return hall, hall.tables[opened["table_id"]]


@pytest.mark.parametrize('client,seat', [('a', 0), ('b', 1)])
def test_leaving_preserves_other_players_and_replaces_named_seat(client, seat):
    hall, table = playing_pair()
    remaining = 'b' if client == 'a' else 'a'
    names = [p.name for p in table.game.players]
    assert hall.leave(client)['phase'] == 'hall'
    assert hall.snapshot(remaining)['phase'] == 'playing'
    assert [p.name for p in table.game.players] == names
    assert not table.game.players[seat].is_human
    assert table.actions == [f'away:{seat}', f'cpu:{seat}']
    assert hall.snapshot(remaining)['you']['leader']
    assert hall.snapshot(remaining)['you']['seat'] == (1 if remaining == 'b' else 0)
    table.game.current = seat
    table.cpu_at = 0
    hall.snapshot(remaining)
    assert len(table.actions) > 1


def test_timeout_replaces_human_without_renaming_or_resetting_board():
    hall, table = playing_pair()
    table.game.current = 1
    hall.update_deadline(table)
    table.turn_deadline = 0
    state = hall.snapshot('b')
    assert state['actions'][:2] == ['timeout:1', 'cpu:1']
    assert not table.game.players[1].is_human
    assert table.game.players[1].name == 'B'
    assert state['you']['observer']
    assert state['seats'][1]['cpu']
    with pytest.raises(ValueError, match='あなたの手番'):
        hall.action({'key': 'pass', 'revision': len(table.actions)}, 'b')
    table.cpu_at = 0
    hall.snapshot('a')
    assert len(table.actions) >= 2


def test_abandon_keeps_same_turn_and_can_pass_without_matching_card():
    from quota.cards import Card
    hall, table = playing_pair()
    game = table.game
    game.current = 1
    game.order_cursor = game.turn_order.index(1)
    game.players[1].quota = Card(99999, 'S', 13)
    turn = game.turn_number
    hall.action({'key': 'abandon', 'revision': 0}, 'b')
    assert game.players[1].quota is None
    assert game.current == 1 and game.turn_number == turn
    game.market = [None] * len(game.market)
    hall.action({'key': 'pass', 'revision': 1}, 'b')
    assert game.current != 1 or game.finished


def test_abandon_does_not_extend_turn_deadline():
    from quota.cards import Card
    hall, table = playing_pair()
    table.game.current = 0
    table.game.order_cursor = table.game.turn_order.index(0)
    hall.update_deadline(table)
    table.game.players[0].quota = Card(99999, 'S', 13)
    deadline = table.turn_deadline
    hall.action({'key': 'abandon', 'revision': 0}, 'a')
    assert table.turn_deadline == deadline


def test_all_humans_replaced_can_advance_remaining_rounds():
    hall, table = playing_pair()
    table.game.round_count = 3
    table.game.awaiting_next_round = True
    table.options['ok_timeout'] = 0
    hall.replace_human(table, 0)
    hall.replace_human(table, 1)
    hall.snapshot('a')
    assert not table.game.awaiting_next_round
    assert table.actions[-1] == 'next_round'
    assert all(not p.is_human for p in table.game.players)


@pytest.mark.parametrize('started', [False, True])
def test_table_list_can_join_full_or_playing_table_as_observer(started):
    hall, table = playing_pair()
    if not started:
        table.phase = 'recruiting'
        hall.join({'table': table.id, 'name': 'C'}, 'c')
    seats = [s.name for s in table.humans]
    result = hall.join({'table': table.id, 'name': 'Watcher'}, 'watcher')
    assert result['you']['observer'] and result['you']['seat'] == -1
    assert result['you']['joined']
    assert [s.name for s in table.humans] == seats
    hall.leave('watcher')
    assert hall.snapshot('a')['phase'] == ('playing' if started else 'recruiting')


def test_countdown_is_authoritative_and_disabled_with_only_one_human():
    hall = UnityHall()
    opened = hall.create({"players": 3, "name": "A", "turn_timeout": 30}, "a")
    hall.join({"table": opened["table_id"], "name": "B"}, "b")
    hall.begin("a")
    table = hall.tables[opened["table_id"]]
    table.game.current = 0
    hall.update_deadline(table)
    state = table.recruiting("b")
    assert state["turn_timeout_active"]
    assert 29 < state["turn_remaining"] <= 30
    hall.leave("b")
    assert not table.recruiting("a")["turn_timeout_active"]
    table.game.current = 1
    assert not table.recruiting("a")["turn_timeout_active"]


def test_departed_player_rejoins_original_seat_on_next_turn_only():
    hall, table = playing_pair()
    game = table.game
    game.current = 0
    game.order_cursor = game.turn_order.index(0)
    hall.update_deadline(table)
    hall.leave('a')
    assert table.humans[0].departed
    assert hall.snapshot('a')['tables'][0]['rejoin']
    assert 'rejoin' not in hall.snapshot('stranger')['tables'][0]
    state = hall.join({'table': table.id, 'name': '別の名前'}, 'a')
    assert state['you']['observer']
    assert len(table.humans) == 2
    assert game.players[0].name == 'A'
    hall.update_deadline(table)
    assert not game.players[0].is_human
    hall.apply_action(table, 'pass')
    for _ in range(3):
        hall.update_deadline(table)
        if game.current == 0: break
        hall.apply_action(table, 'pass')
    assert game.current == 0
    assert game.players[0].is_human
    assert not table.humans[0].departed
    assert not table.recruiting('a')['you']['observer']
    assert table.actions[-1] == 'human:0'


def test_last_departed_player_can_rejoin_and_cancel_pending_return():
    hall, table = playing_pair()
    hall.leave('a')
    hall.leave('b')
    assert table.id in hall.tables
    assert hall.snapshot('a')['tables'][0]['rejoin']
    hall.join({'table': table.id}, 'a')
    hall.leave('a')
    assert table.humans[0].resume_after is None
    assert table.humans[0].departed


@pytest.mark.parametrize("connected", [True, False])
def test_timeout_proxy_returns_to_connected_human_on_next_turn(connected):
    import time
    hall, table = playing_pair()
    game = table.game
    game.current = 1
    game.order_cursor = game.turn_order.index(1)
    hall.update_deadline(table)
    table.turn_deadline = 0
    hall.snapshot('b')
    assert not game.players[1].is_human
    assert not table.humans[1].departed
    if not connected: table.humans[1].last_seen = time.monotonic() - 10
    hall.apply_action(table, 'pass')
    for _ in range(3):
        hall.update_deadline(table)
        if game.current == 1: break
        hall.apply_action(table, 'pass')
    assert game.current == 1
    assert game.players[1].is_human == connected
    if connected:
        assert table.actions[-1] == 'human:1'
        assert not table.recruiting('b')['you']['observer']
    else:
        assert table.humans[1].resume_after is not None


def test_other_human_timer_survives_temporary_cpu_substitution():
    hall, table = playing_pair()
    game = table.game
    game.current = 0
    game.order_cursor = game.turn_order.index(0)
    hall.update_deadline(table)
    table.turn_deadline = 0
    hall.snapshot('a')
    assert not game.players[0].is_human
    assert table.participating_humans() == 2
    hall.apply_action(table, 'pass')
    for _ in range(3):
        hall.update_deadline(table)
        if game.current == 1: break
        hall.apply_action(table, 'pass')
    assert game.current == 1
    state = table.recruiting('b')
    assert state['turn_timeout_active']
    assert 0 < state['turn_remaining'] <= 1
    table.turn_deadline = 0
    state = hall.snapshot('b')
    assert 'timeout:1' in state['actions']
    assert not game.players[1].is_human
    assert table.participating_humans() == 2


def test_second_returning_participant_starts_timer_for_existing_turn():
    import time
    hall, table = playing_pair()
    table.options['turn_timeout'] = 30
    hall.leave('a')
    hall.leave('b')
    hall.join({'table': table.id}, 'a')
    assert table.participating_humans() == 1
    game = table.game
    game.current = 0
    game.turn_number += 1
    hall.update_deadline(table)
    assert game.players[0].is_human
    assert not table.recruiting('a')['turn_timeout_active']
    table.turn_deadline = time.monotonic() - 60
    key = table.turn_key
    state = hall.join({'table': table.id}, 'b')
    assert state['you']['observer']  # B still waits for the next own turn.
    assert not game.players[1].is_human
    assert table.turn_key == key  # A's turn does not change.
    assert table.participating_humans() == 2
    state = hall.snapshot('a')
    assert state['turn_timeout_active']
    assert 29 < state['turn_remaining'] <= 30
    deadline = table.turn_deadline
    hall.join({'table': table.id}, 'b')
    hall.snapshot('a')
    assert table.turn_deadline == deadline  # Polling/repeated join cannot reset it.
    table.turn_deadline = 0
    assert 'timeout:0' in hall.snapshot('a')['actions']


def test_spectator_join_does_not_enable_a_single_humans_timer():
    hall, table = playing_pair()
    hall.leave('b')
    table.game.current = 0
    hall.update_deadline(table)
    hall.join({'table': table.id, 'name': '観戦者'}, 'spectator')
    assert table.participating_humans() == 1
    assert not table.recruiting('a')['turn_timeout_active']

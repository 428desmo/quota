import time

import pytest

from quota.web import Table, _table_expired


def test_cpu_waits_one_second_after_the_opening_deal():
    table = None
    for seed in range(40):
        candidate = Table()
        candidate.open(
            {"players": 3, "name": "a", "seed": seed, "sequence": False, "title": False, "special": False},
            "human",
        )
        candidate.begin("human")
        if not candidate.game.players[candidate.game.current].is_human:
            table = candidate
            break
    assert table is not None
    game = table.game
    before = (game.turn_number, game.current, len(game.log))
    table.step_cpu()
    assert (game.turn_number, game.current, len(game.log)) == before
    assert table.cpu_after > time.monotonic()
    table.cpu_after = 0
    table.step_cpu()
    assert len(game.log) > before[2]


def test_turn_timeout_is_two_minutes_and_skips_a_lone_human():
    table = Table()
    table.open({"players": 3, "name": "a", "seed": 1, "simple": True}, "human")
    assert table.turn_timeout == 120
    table.begin("human")
    table.step_timeout()
    assert table.turn_deadline is None


def test_two_humans_still_get_a_turn_clock():
    found = False
    for seed in range(40):
        table = Table()
        table.open({"players": 3, "name": "a", "seed": seed, "simple": True}, "h1")
        table.admit("h2", "bee")
        table.begin("h1")
        if not table.game.players[table.game.current].is_human:
            continue
        table.step_timeout()
        assert table.turn_deadline is not None
        found = True
        break
    assert found


def test_exhibition_waits_and_the_watcher_advances_the_round():
    table = Table()
    table.open_exhibition({"players": 3, "name": "観戦", "seed": 1, "simple": False}, "obs")
    assert table.display_name == "CPU模擬戦"
    assert all(not player.is_human for player in table.game.players)
    table.game.awaiting_next_round = True
    table.cpu_after = 0
    table.step_cpu()
    assert table.game.awaiting_next_round
    table.next_round("obs")
    assert table.game.round_index == 2
    assert not table.game.awaiting_next_round


def test_observer_cannot_advance_while_a_human_is_seated():
    table = Table()
    table.open({"players": 3, "name": "a", "seed": 1, "simple": False}, "human")
    table.begin("human")
    table.admit("obs", "見")
    table.game.awaiting_next_round = True
    with pytest.raises(ValueError):
        table.next_round("obs")
    table.next_round("human")
    assert table.game.round_index == 2


def test_finished_exhibition_is_labeled_on_the_hall():
    from quota.web import Hall

    hall = Hall()
    hall.create({"cpu_match": True, "players": 3, "name": "観戦", "seed": 1, "simple": True}, "obs")
    table = next(iter(hall.tables.values()))
    table.phase = "finished"
    table.finished_at = time.monotonic()
    summary = table.summary()
    assert summary["status"] == "ゲーム終了"
    assert summary["leader"] == "CPU模擬戦"
    assert not _table_expired(table, table.finished_at + 10)


def test_finished_table_stays_on_the_hall_for_three_minutes():
    table = Table()
    table.phase = "finished"
    table.finished_at = 1000
    table.idle_at = 1000
    assert not _table_expired(table, 1179)
    assert _table_expired(table, 1180)
    table.phase = "playing"
    table.finished_at = None
    assert not _table_expired(table, 1179)
    assert _table_expired(table, 1180)

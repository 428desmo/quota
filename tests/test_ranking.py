import json

from quota.characters import character_count, character_name, decode, display_name, encode
from quota.ranking import (
    AWARD,
    DIMENSIONS,
    Table,
    award_slots,
    dimension_sizes,
    dims_of,
    dumps,
    from_json,
    key_for,
    key_of,
    load,
    mean_value,
    table,
)


def test_the_key_is_the_strategy_dimensions():
    character_id = encode(11, 4, 5, 2, 1)
    assert key_for(character_id) == "11-4-5-2-1"
    assert dims_of(key_for(character_id)) == decode(character_id)
    assert key_of(decode(character_id)) == key_for(character_id)


def test_the_winner_takes_three_and_the_runner_up_two():
    sheet = Table()
    keys = ["0-0-0-0-0", "1-0-1-0-0", "2-0-2-0-0", "3-0-3-0-0"]
    sheet.record(keys, [50, 40, 30, 20])
    points = [sheet.entries[key].points for key in keys]
    assert points == [3.0, 2.0, 0.0, 0.0]
    assert [sheet.entries[key].value for key in keys] == [3.0, 2.0, 0.0, 0.0]
    assert sheet.matches == 1
    for key in keys:
        assert sheet.entries[key].games == 1


def test_a_tie_shares_the_places_it_fills():
    sheet = Table()
    keys = ["0-0-0-0-0", "1-0-1-0-0", "2-0-2-0-0", "3-0-3-0-0"]
    sheet.record(keys, [40, 40, 30, 20])
    assert sheet.entries[keys[0]].points == sheet.entries[keys[1]].points == 2.5
    assert sheet.entries[keys[2]].points == 0.0
    second = Table()
    second.record(keys, [40, 30, 30, 20])
    assert second.entries[keys[1]].points == second.entries[keys[2]].points == 1.0


def test_one_match_always_hands_out_the_same_total():
    for scores in ([50, 40, 30, 20], [40, 40, 30, 20], [30, 30, 30, 30], [50, 40, 40, 40]):
        sheet = Table()
        sheet.record(["0-0-0-0-0", "1-0-1-0-0", "2-0-2-0-0", "3-0-3-0-0"], scores)
        given = sum(entry.points for entry in sheet.entries.values())
        assert abs(given - sum(AWARD)) < 1e-9


def test_the_award_rule_holds_the_average_still():
    assert award_slots(4) == [3.0, 2.0, 0.0, 0.0]
    assert mean_value(4) == 1.25
    sheet = Table()
    sheet.record(["0-0-0-0-0", "1-0-1-0-0", "2-0-2-0-0", "3-0-3-0-0"], [50, 40, 30, 20])
    sheet.record(["0-0-0-0-0", "4-0-4-0-0", "5-0-5-0-0", "6-0-6-0-0"], [10, 40, 30, 20])
    played = sum(entry.games for entry in sheet.entries.values())
    taken = sum(entry.points for entry in sheet.entries.values())
    assert abs(taken / played - sheet.mean()) < 1e-9


def test_an_unplayed_combination_starts_at_the_average():
    sheet = Table()
    sheet.record(["0-0-0-0-0", "1-0-1-0-0"], [40, 20])
    fresh = sheet.entry_for("2-0-2-0-0")
    assert fresh.value == sheet.mean() == 1.25
    assert fresh.games == 0


def test_places_follow_the_value():
    sheet = Table()
    sheet.record(["0-0-0-0-0", "1-0-1-0-0", "2-0-2-0-0"], [30, 20, 10])
    sheet.rerank()
    places = [sheet.entries[key].rank for key in ("0-0-0-0-0", "1-0-1-0-0", "2-0-2-0-0")]
    assert places == [1, 2, 3]


def test_a_tie_shares_a_place():
    sheet = Table()
    sheet.record(["0-0-0-0-0", "1-0-1-0-0", "2-0-2-0-0"], [20, 20, 10])
    sheet.rerank()
    assert sheet.entries["0-0-0-0-0"].rank == sheet.entries["1-0-1-0-0"].rank == 1
    assert sheet.entries["2-0-2-0-0"].rank == 3


def test_a_run_can_be_added_to_a_saved_table():
    sheet = Table()
    sheet.record(["0-0-0-0-0", "1-0-1-0-0"], [30, 10])
    again = from_json(json.loads(dumps(sheet)))
    assert again.matches == sheet.matches
    assert again.seats == sheet.seats
    assert set(again.entries) == set(sheet.entries)
    assert again.entries["0-0-0-0-0"].points == sheet.entries["0-0-0-0-0"].points
    assert again.entries["0-0-0-0-0"].rank == 1
    again.record(["0-0-0-0-0", "2-0-2-0-0"], [10, 30])
    assert again.matches == 2
    assert again.entries["2-0-2-0-0"].games == 1
    assert again.entries["0-0-0-0-0"].value == 2.5


def test_a_file_scored_by_another_rule_is_refused():
    sheet = Table()
    sheet.record(["0-0-0-0-0", "1-0-1-0-0"], [30, 10])
    data = json.loads(dumps(sheet))
    data["award"] = [1.0]
    try:
        from_json(data)
    except ValueError as error:
        assert "award" in str(error)
    else:
        raise AssertionError("another award rule was accepted")


def test_the_shipped_file_covers_the_current_dimensions():
    sheet = load()
    data = json.loads(dumps(sheet))
    assert data["format"] == "quota-cpu-ranking"
    assert data["system"] == "place-points"
    assert data["dimensions"] == list(DIMENSIONS)
    assert data["sizes"] == list(dimension_sizes())
    assert data["award"] == list(AWARD)
    assert data["mean"] == 1.25
    assert data["matches"] > 0
    assert len(data["players"]) == len(sheet.entries)
    places = [row["rank"] for row in data["players"]]
    assert places[0] == 1
    assert places == sorted(places)
    values = [row["value"] for row in data["players"]]
    assert values == sorted(values, reverse=True)
    for row in data["players"]:
        assert len(row["id"]) == len(DIMENSIONS)
        for value, size in zip(row["id"], dimension_sizes()):
            assert 0 <= value < size


def test_every_character_gets_a_place():
    sheet = load()
    for character_id in (0, 1, 1234, character_count() - 1):
        place = sheet.rank_for(character_id)
        assert place is not None
        assert 1 <= place <= len(sheet.entries)


def test_a_combination_outside_the_file_takes_the_middle_place():
    sheet = load()
    held = sheet.entries.pop(key_for(7), None)
    try:
        assert sheet.rank_for(7) == sheet.provisional_rank()
    finally:
        if held is not None:
            sheet.entries[key_for(7)] = held


def test_the_display_name_carries_the_place():
    sheet = table()
    assert sheet is not None
    name = display_name(7)
    assert name == f"{character_name(7)}({sheet.rank_for(7)})"
    assert name.endswith(")")

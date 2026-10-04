import json

from quota.characters import character_count, character_name, decode, display_name, encode
from quota.ranking import (
    DIMENSIONS,
    START_RATING,
    Table,
    dimension_sizes,
    dims_of,
    dumps,
    from_json,
    key_for,
    key_of,
    load,
    table,
)


def test_the_key_is_the_strategy_dimensions():
    character_id = encode(11, 4, 5, 2, 1)
    assert key_for(character_id) == "11-4-5-2-1"
    assert dims_of(key_for(character_id)) == decode(character_id)
    assert key_of(decode(character_id)) == key_for(character_id)


def test_an_unplayed_combination_starts_at_the_mean():
    sheet = Table()
    assert sheet.mean() == START_RATING
    sheet.record(["0-0-0-0-0", "1-0-1-0-0"], [40, 20])
    fresh = sheet.entry_for("2-0-2-0-0")
    assert fresh.rating == sheet.mean()
    assert fresh.games == 0


def test_a_match_moves_the_winner_up_and_keeps_the_total():
    sheet = Table()
    keys = ["0-0-0-0-0", "1-0-1-0-0", "2-0-2-0-0", "3-0-3-0-0"]
    sheet.record(keys, [50, 40, 30, 30])
    ratings = [sheet.entries[key].rating for key in keys]
    assert ratings[0] > ratings[1] > ratings[2]
    assert ratings[2] == ratings[3]
    assert abs(sum(ratings) - START_RATING * len(keys)) < 1e-9
    assert sheet.matches == 1
    for key in keys:
        assert sheet.entries[key].games == 1
    assert abs(sheet.entries[keys[0]].points - 1.0) < 1e-9
    assert abs(sheet.entries[keys[2]].points - 1.0 / 6.0) < 1e-9


def test_places_follow_the_rating():
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
    assert set(again.entries) == set(sheet.entries)
    assert abs(again.entries["0-0-0-0-0"].rating - sheet.entries["0-0-0-0-0"].rating) < 0.05
    assert again.entries["0-0-0-0-0"].rank == 1
    again.record(["0-0-0-0-0", "2-0-2-0-0"], [10, 30])
    assert again.matches == 2
    assert again.entries["2-0-2-0-0"].games == 1


def test_the_shipped_file_covers_the_current_dimensions():
    sheet = load()
    data = json.loads(dumps(sheet))
    assert data["format"] == "quota-cpu-ranking"
    assert data["dimensions"] == list(DIMENSIONS)
    assert data["sizes"] == list(dimension_sizes())
    assert data["matches"] > 0
    assert len(data["players"]) == len(sheet.entries)
    places = [row["rank"] for row in data["players"]]
    assert places[0] == 1
    assert places == sorted(places)
    ratings = [row["rating"] for row in data["players"]]
    assert ratings == sorted(ratings, reverse=True)
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
    held = sheet.entries.pop(key_for(7))
    try:
        assert sheet.rank_for(7) == sheet.provisional_rank()
    finally:
        sheet.entries[key_for(7)] = held


def test_the_display_name_carries_the_place():
    sheet = table()
    assert sheet is not None
    name = display_name(7)
    assert name == f"{character_name(7)}({sheet.rank_for(7)})"
    assert name.endswith(")")

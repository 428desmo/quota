"""The proposed room flow keeps invitations and start decisions server-side."""

import pytest

from tools.serve_unity_web import UnityHall


def create_room(hall, **options):
    body = {"name": "リーダー", "players": 3, "prepare": True,
            "visibility": "private", "lobby_duration": 60}
    body.update(options)
    return hall.create(body, "leader")


def test_private_preparing_room_is_hidden_but_invitee_can_join():
    hall = UnityHall()
    room = create_room(hall)
    assert room["phase"] == "preparing"
    assert len(room["invite_code"]) >= 22
    assert hall.snapshot("outsider")["tables"] == []
    with pytest.raises(ValueError, match="そのルームはありません"):
        hall.join({"table": room["table_id"], "name": "侵入者"}, "outsider")
    with pytest.raises(ValueError, match="そのルームはありません"):
        hall.lookup({"code": room["table_id"]})
    assert hall.lookup({"code": room["invite_code"]})["leader"] == "リーダー"
    joined = hall.join({"table": room["invite_code"], "name": "友人", "role": "player"}, "friend")
    assert joined["you"]["seat"] == 1
    assert joined["invite_code"] == ""
    assert hall.snapshot("outsider")["tables"] == []


def test_private_room_can_open_then_be_published_without_restarting_clock():
    hall = UnityHall()
    room = create_room(hall)
    assert hall.snapshot("viewer")["tables"] == []
    opened = hall.open_lobby("leader")
    assert opened["phase"] == "recruiting"
    assert 0 < opened["lobby_remaining"] <= 60
    deadline = hall.tables[room["table_id"]].lobby_deadline
    hall.set_visibility({"visibility": "public"}, "leader")
    assert hall.tables[room["table_id"]].lobby_deadline == deadline
    assert hall.snapshot("viewer")["tables"][0]["id"] == room["table_id"]


def test_early_start_needs_every_human_ready_and_currently_connected():
    hall = UnityHall()
    room = create_room(hall)
    hall.join({"table": room["invite_code"], "name": "友人", "role": "player"}, "friend")
    hall.open_lobby("leader")
    with pytest.raises(ValueError, match="開始可能"):
        hall.begin("leader")
    hall.set_ready({"ready": True}, "leader")
    with pytest.raises(ValueError, match="開始可能"):
        hall.begin("leader")
    hall.set_ready({"ready": True}, "friend")
    assert hall.begin("leader")["phase"] == "playing"


def test_deadline_auto_starts_two_humans_without_ready_declarations():
    hall = UnityHall()
    room = create_room(hall)
    hall.join({"table": room["invite_code"], "name": "友人", "role": "player"}, "friend")
    hall.open_lobby("leader")
    hall.tables[room["table_id"]].lobby_deadline = 0
    assert hall.snapshot("leader")["phase"] == "playing"
    assert hall.snapshot("friend")["phase"] == "playing"


def test_solo_deadline_requires_choice_and_allows_only_one_extension():
    hall = UnityHall()
    room = create_room(hall, visibility="public")
    hall.open_lobby("leader")
    hall.tables[room["table_id"]].lobby_deadline = 0
    expired = hall.snapshot("leader")
    assert expired["phase"] == "recruiting" and expired["lobby_decision"]
    with pytest.raises(ValueError, match="参加受付"):
        hall.join({"table": room["table_id"], "name": "遅刻"}, "late")
    extended = hall.extend_lobby("leader")
    assert extended["lobby_extended"] and 0 < extended["lobby_remaining"] <= 60
    hall.tables[room["table_id"]].lobby_deadline = 0
    assert hall.snapshot("leader")["lobby_decision"]
    with pytest.raises(ValueError, match="もう一度"):
        hall.extend_lobby("leader")
    assert hall.begin("leader")["phase"] == "playing"


def test_new_human_and_changed_cpu_cast_reset_early_start_readiness():
    hall = UnityHall()
    room = create_room(hall)
    hall.join({"table": room["invite_code"], "name": "友人", "role": "player"}, "friend")
    hall.open_lobby("leader")
    hall.set_ready({"ready": True}, "leader")
    hall.set_ready({"ready": True}, "friend")
    hall.set_players({"players": 4}, "leader")
    assert hall.snapshot("leader")["lobby_ready"] == [False, False]
    hall.set_ready({"ready": True}, "leader")
    hall.set_ready({"ready": True}, "friend")
    hall.nominate({"cpu_cast": [0, 1, 2, 3]}, "leader")
    assert hall.snapshot("leader")["lobby_ready"] == [False, False]


def test_pre_game_leave_has_no_guard_and_leader_can_cancel_room():
    hall = UnityHall()
    room = create_room(hall)
    hall.join({"table": room["invite_code"], "name": "友人", "role": "player"}, "friend")
    assert hall.leave("friend")["phase"] == "hall"
    assert hall.join({"table": room["invite_code"], "name": "友人", "role": "player"}, "friend")["phase"] == "preparing"
    hall.cancel_lobby("leader")
    assert hall.snapshot("friend")["phase"] == "hall"
    assert room["table_id"] not in hall.tables


def test_explicit_exit_blocks_new_play_but_allows_observation():
    hall = UnityHall()
    first = hall.create({"name": "A", "players": 3}, "a")
    hall.join({"table": first["table_id"], "name": "B"}, "b")
    hall.begin("a")
    hall.leave("a")
    assert 0 < hall.snapshot("a")["guard_remaining"] <= 60
    with pytest.raises(ValueError, match="参加待機時間"):
        hall.create({"name": "A", "players": 3}, "a")
    with pytest.raises(ValueError, match="参加待機時間"):
        hall.join({"table": first["table_id"]}, "a")
    watching = hall.join({"table": first["table_id"], "role": "observer"}, "a")
    assert watching["you"]["observer"]
    hall.leave("a")
    second = hall.create({"name": "別のリーダー", "players": 3}, "other")
    hall.join({"table": second["table_id"], "role": "observer"}, "a")
    with pytest.raises(ValueError, match="参加待機時間"):
        hall.participation({"sit_out": False}, "a")
    hall.exit_guard_until["a"] = 0
    hall.join({"table": first["table_id"]}, "a")
    assert hall.tables[first["table_id"]].members["a"] is hall.tables[first["table_id"]].humans[0]


def test_invitee_can_join_before_publication_and_promote_from_observer():
    hall = UnityHall()
    room = create_room(hall)
    assert hall.lookup({"code": room["invite_code"]})["status"] == "準備中"
    hall.join({"table": room["invite_code"], "role": "observer"}, "friend")
    assert hall.snapshot("friend")["you"]["observer"]
    promoted = hall.join({"table": room["invite_code"], "role": "player"}, "friend")
    assert promoted["you"]["seat"] == 1


def test_expired_lobby_waits_when_other_human_has_disconnected():
    hall = UnityHall()
    room = create_room(hall)
    hall.join({"table": room["invite_code"], "role": "player"}, "friend")
    hall.open_lobby("leader")
    hall.tables[room["table_id"]].members["friend"].last_seen = 0
    hall.tables[room["table_id"]].lobby_deadline = 0
    assert hall.snapshot("leader")["lobby_decision"]


def test_normal_result_dismissal_does_not_trigger_exit_guard():
    hall = UnityHall()
    room = hall.create({"name": "A", "players": 3}, "a")
    hall.begin("a")
    hall.tables[room["table_id"]].game.finished = True
    hall.leave("a", explicit_exit=False)
    assert hall.snapshot("a")["guard_remaining"] == 0
    hall.create({"name": "A", "players": 3}, "a")


def test_false_normal_dismissal_during_play_still_counts_as_exit():
    hall = UnityHall()
    room = hall.create({"name": "A", "players": 3}, "a")
    hall.begin("a")
    hall.leave("a", explicit_exit=False)
    assert hall.snapshot("a")["guard_remaining"] > 0

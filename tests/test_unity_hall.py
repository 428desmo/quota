import sys
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

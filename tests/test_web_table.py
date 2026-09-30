import time

from quota.web import Table


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

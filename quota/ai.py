"""Greedy CPU. Prefers finishing, then mid-size quotas, and takes up to the need."""

from __future__ import annotations

from quota.cards import score_for
from quota.engine import Abandon, Action, Collect, Game, Pass, TakeQuota


def choose_action(game: Game) -> Action:
    actions = game.legal_actions()
    player = game.players[game.current]
    takes = [a for a in actions if isinstance(a, TakeQuota)]
    if takes:
        def take_key(action: TakeQuota) -> tuple[int, int, int]:
            card = next(c for c in game.market if c.id == action.card_id)
            assert card.rank is not None
            sweet = -abs(card.rank - 6)
            return (score_for(card.rank) if card.rank == 1 else 0, sweet, -card.rank)

        takes.sort(key=take_key, reverse=True)
        best = takes[0]
        card = next(c for c in game.market if c.id == best.card_id)
        assert card.rank is not None
        if card.rank >= 11 and any(
            next(c for c in game.market if c.id == a.card_id).rank == 1 for a in takes
        ):
            return next(
                a
                for a in takes
                if next(c for c in game.market if c.id == a.card_id).rank == 1
            )
        return best

    if player.quota is None:
        return Pass()

    assert player.quota.rank is not None
    need = player.quota.rank - 1 - len(player.collection)
    eligible = [
        c
        for c in game.market
        if c.suit == player.quota.suit or c.suit == "JOKER"
    ]
    suits = [c for c in eligible if c.suit != "JOKER"]
    jokers = [c for c in eligible if c.suit == "JOKER"]
    if not eligible or need <= 0:
        if need >= 6:
            return Abandon()
        return Pass()
    if need >= 6 and not suits:
        return Abandon()
    chosen_ids = {c.id for c in (suits + jokers)[:need]}
    return Collect(tuple(c.id for c in eligible if c.id in chosen_ids))

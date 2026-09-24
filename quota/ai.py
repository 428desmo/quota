"""Greedy CPU. Prefers finishing, then jokers, then mid-size quotas."""

from __future__ import annotations

from quota.cards import score_for
from quota.engine import Abandon, Action, Collect, Game, Pass, TakeQuota


def choose_action(game: Game) -> Action:
    actions = game.legal_actions()
    player = game.players[game.current]
    takes = [a for a in actions if isinstance(a, TakeQuota)]
    collects = [a for a in actions if isinstance(a, Collect)]
    if takes:
        def take_key(action: TakeQuota) -> tuple[int, int]:
            card = next(c for c in game.market if c.id == action.card_id)
            assert card.rank is not None
            # Aces are safe points. Otherwise prefer 4–8, which finish often.
            sweet = -abs(card.rank - 6)
            return (score_for(card.rank) if card.rank == 1 else 0, sweet, -card.rank)

        takes.sort(key=take_key, reverse=True)
        best = takes[0]
        card = next(c for c in game.market if c.id == best.card_id)
        assert card.rank is not None
        if card.rank >= 11 and any(
            next(c for c in game.market if c.id == a.card_id).rank == 1 for a in takes
        ):
            ace = next(
                a
                for a in takes
                if next(c for c in game.market if c.id == a.card_id).rank == 1
            )
            return ace
        return best
    if collects:
        assert player.quota is not None and player.quota.rank is not None
        need = player.quota.rank - 1 - len(player.collection)

        def collect_key(action: Collect) -> tuple[int, int]:
            card = next(c for c in game.market if c.id == action.card_id)
            finishes = 1 if need == 1 else 0
            joker_later = 0 if card.suit == "JOKER" else 1
            return (finishes, joker_later, card.id)

        collects.sort(key=collect_key, reverse=True)
        if need > 8 and not any(
            next(c for c in game.market if c.id == a.card_id).suit != "JOKER"
            for a in collects
        ):
            return Abandon()
        return collects[0]
    if any(isinstance(a, Abandon) for a in actions) and player.quota is not None:
        assert player.quota.rank is not None
        if player.quota.rank - 1 - len(player.collection) >= 6:
            return Abandon()
    return Pass()

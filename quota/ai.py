"""Greedy CPU. Prefers finishing, then mid-size quotas, and takes up to the need."""

from __future__ import annotations

from quota.cards import score_for
from quota.engine import Abandon, Action, Collect, Game, Pass, TakeQuota


def choose_action(game: Game) -> Action:
    _maybe_special(game)
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
    chosen = suits + jokers
    if game.config.sequence_rule:
        chosen = _order_for_sequence(player, chosen)[:need]
    else:
        chosen = chosen[:need]
    return Collect(tuple(c.id for c in chosen))


def _maybe_special(game: Game) -> None:
    if not game.config.special_actions_rule or game.finished or game.plan != "normal" or game.turn_gain:
        return
    player = game.players[game.current]
    if player.reshuffle_take_left > 0 and not _market_helps(game, player):
        game.declare_reshuffle()
        return
    if player.double_action_left > 0 and _worth_double(game, player):
        game.declare_double()


def _market_helps(game: Game, player) -> bool:
    if player.quota is None:
        return any(card.suit != "JOKER" for card in game.market)
    return any(card.suit == player.quota.suit or card.suit == "JOKER" for card in game.market)


def _worth_double(game: Game, player) -> bool:
    if player.quota is None:
        return any(card.rank == 1 or (card.rank is not None and card.rank >= 7) for card in game.market)
    assert player.quota.rank is not None
    need = player.quota.rank - 1 - len(player.collection)
    eligible = [
        card
        for card in game.market
        if card.suit == player.quota.suit or card.suit == "JOKER"
    ]
    return 0 < need <= 4 and len(eligible) > 0


def _order_for_sequence(player, cards):
    """Greedy order that continues the shipment list."""
    tail = player.quota.rank if player.quota is not None else None
    if player.collection:
        last = player.collection[-1]
        tail = None if last.suit == "JOKER" else last.rank
    remaining = list(cards)
    ordered = []
    while remaining:
        def value(card, current=tail):
            if card.suit == "JOKER" or current is None or card.rank is None:
                return 0
            if card.rank == current:
                return 2
            if abs(card.rank - current) == 1:
                return 1
            return 0

        remaining.sort(key=value, reverse=True)
        picked = remaining.pop(0)
        ordered.append(picked)
        tail = None if picked.suit == "JOKER" else picked.rank
    return ordered

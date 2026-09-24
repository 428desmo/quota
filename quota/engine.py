"""Quota rules engine. Section 2 of the spec wins over the overview."""

from __future__ import annotations

import itertools
import random
from dataclasses import dataclass, field
from typing import Literal

from quota.cards import Card, bonus, make_deck, score_for

EndReason = Literal["DECK", "STALL"]


@dataclass(frozen=True, slots=True)
class TakeQuota:
    card_id: int

    def key(self) -> tuple[str, int]:
        return ("take", self.card_id)


@dataclass(frozen=True, slots=True)
class Collect:
    card_ids: tuple[int, ...]

    def key(self) -> tuple[str, tuple[int, ...]]:
        return ("collect", self.card_ids)


@dataclass(frozen=True, slots=True)
class Abandon:
    def key(self) -> tuple[str, int]:
        return ("abandon", -1)


@dataclass(frozen=True, slots=True)
class Pass:
    def key(self) -> tuple[str, int]:
        return ("pass", -1)


Action = TakeQuota | Collect | Abandon | Pass


@dataclass
class Player:
    name: str
    quota: Card | None = None
    collection: list[Card] = field(default_factory=list)
    achieved: list[Card] = field(default_factory=list)
    score: int = 0
    achieve_count: int = 0
    max_single_score: int = 0
    is_human: bool = True


@dataclass
class GameConfig:
    num_players: int = 3
    num_decks: int = 2
    market_size: int | None = None
    removed_count: int = 8
    stall_threshold: int | None = None
    stall_end_count: int = 2
    seed: int | None = None
    names: list[str] | None = None
    human_seats: list[int] | None = None

    def resolved_market_size(self) -> int:
        if self.market_size is not None:
            return self.market_size
        if self.num_players == 4:
            return 6
        return 7

    def resolved_stall_threshold(self) -> int:
        if self.stall_threshold is not None:
            return self.stall_threshold
        return self.num_players


@dataclass
class Game:
    config: GameConfig
    deck: list[Card]
    removed: list[Card]
    market: list[Card]
    discard: list[Card]
    players: list[Player]
    current: int
    no_gain_streak: int
    stall_flag: bool
    finished: bool
    end_reason: EndReason | None
    turn_number: int
    log: list[str]
    rng: random.Random
    reshuffle_count: int = 0

    @classmethod
    def start(cls, config: GameConfig | None = None) -> Game:
        cfg = config or GameConfig()
        if cfg.num_players < 2:
            raise ValueError("num_players must be at least 2")
        rng = random.Random(cfg.seed)
        deck = make_deck(cfg.num_decks)
        rng.shuffle(deck)
        removed_count = cfg.removed_count
        market_size = cfg.resolved_market_size()
        if len(deck) < removed_count + market_size:
            raise ValueError("deck is smaller than removed cards plus market")
        removed = [deck.pop() for _ in range(removed_count)]
        market = [deck.pop() for _ in range(market_size)]
        names = cfg.names or [f"P{i + 1}" for i in range(cfg.num_players)]
        if len(names) != cfg.num_players:
            raise ValueError("names length must match num_players")
        human = set(cfg.human_seats if cfg.human_seats is not None else range(cfg.num_players))
        players = [
            Player(name=names[i], is_human=i in human) for i in range(cfg.num_players)
        ]
        first = rng.randrange(cfg.num_players)
        game = cls(
            config=cfg,
            deck=deck,
            removed=removed,
            market=market,
            discard=[],
            players=players,
            current=first,
            no_gain_streak=0,
            stall_flag=False,
            finished=False,
            end_reason=None,
            turn_number=1,
            log=[],
            rng=rng,
        )
        game.log.append(f"先手: {players[first].name}")
        return game

    def market_size(self) -> int:
        return self.config.resolved_market_size()

    def legal_actions(self, seat: int | None = None) -> list[Action]:
        p = self.players[self.current if seat is None else seat]
        if p.quota is None:
            acts: list[Action] = [
                TakeQuota(c.id) for c in self.market if c.suit != "JOKER"
            ]
            return acts + [Pass()]
        assert p.quota.rank is not None
        eligible = [
            c.id
            for c in self.market
            if c.suit == p.quota.suit or c.suit == "JOKER"
        ]
        need = p.quota.rank - 1 - len(p.collection)
        acts: list[Action] = [
            Collect(ids)
            for size in range(1, need + 1)
            for ids in itertools.combinations(eligible, size)
        ]
        return acts + [Abandon(), Pass()]

    def step(self, action: Action) -> None:
        if self.finished:
            raise RuntimeError("game is already finished")
        action = self._canonicalize(action)
        legal = {a.key(): a for a in self.legal_actions()}
        if action.key() not in legal:
            raise ValueError(f"illegal action: {action}")
        p = self.players[self.current]
        gained = False
        if isinstance(action, TakeQuota):
            card = self._take_market(action.card_id)
            gained = True
            if card.rank == 1:
                self._achieve(p, [card], 1)
                self.log.append(f"{p.name} が {card.label()} の注文を請け負い、即納品（1点）")
            else:
                p.quota = card
                p.collection = []
                self.log.append(
                    f"{p.name} が {card.label()} の注文を請け負った（{card.rank}枚、{score_for(card.rank)}点）"
                )
        elif isinstance(action, Collect):
            assert p.quota is not None and p.quota.rank is not None
            taken = [self._take_market(card_id) for card_id in action.card_ids]
            gained = True
            p.collection.extend(taken)
            labels = "、".join(card.label() for card in taken)
            self.log.append(f"{p.name} が {labels} を買い付け")
            if 1 + len(p.collection) == p.quota.rank:
                rank = p.quota.rank
                cards = [p.quota, *p.collection]
                self._achieve(p, cards, rank)
                p.quota = None
                p.collection = []
                self.log.append(f"{p.name} が納品（{score_for(rank)}点）")
        elif isinstance(action, Abandon):
            assert p.quota is not None
            self.discard.extend([p.quota, *p.collection])
            self.log.append(f"{p.name} が注文を取り消した")
            p.quota = None
            p.collection = []
        elif isinstance(action, Pass):
            self.log.append(f"{p.name} はパス")
        else:
            raise TypeError(action)

        if gained:
            self.no_gain_streak = 0
            self.stall_flag = False
        else:
            self.no_gain_streak += 1
            if self.no_gain_streak == self.config.resolved_stall_threshold():
                if self.stall_flag:
                    self.finished = True
                    self.end_reason = "STALL"
                    self.log.append("交易の途絶（膠着の連続）")
                    return
                self.deck.extend(self.market)
                self.market = []
                self.rng.shuffle(self.deck)
                self.market = [self.deck.pop() for _ in range(self.market_size())]
                self.no_gain_streak = 0
                self.stall_flag = True
                self.reshuffle_count += 1
                self.log.append("新しい船団が入港した")

        self.current = (self.current + 1) % len(self.players)
        self.turn_number += 1
        self.begin_turn()

    def begin_turn(self) -> bool:
        """Refill the market at the start of a turn. False means the game ended."""
        while len(self.market) < self.market_size():
            if not self.deck:
                self.finished = True
                self.end_reason = "DECK"
                self.log.append("季節風の終わり（山札切れ）")
                return False
            self.market.append(self.deck.pop())
        return True

    def ranking(self) -> list[list[int]]:
        """Seats grouped best-first. Ties share a group."""
        seats = list(range(len(self.players)))
        seats.sort(
            key=lambda i: (
                self.players[i].score,
                self.players[i].achieve_count,
                self.players[i].max_single_score,
            ),
            reverse=True,
        )
        groups: list[list[int]] = []
        for seat in seats:
            if not groups or not self._tied(groups[-1][0], seat):
                groups.append([seat])
            else:
                groups[-1].append(seat)
        return groups

    def public_view(self) -> dict:
        """Observation without deck order or removed cards."""
        return {
            "market": [c.label() for c in self.market],
            "discard": [c.label() for c in self.discard],
            "deck_count": len(self.deck),
            "current": self.current,
            "no_gain_streak": self.no_gain_streak,
            "stall_flag": self.stall_flag,
            "finished": self.finished,
            "end_reason": self.end_reason,
            "turn_number": self.turn_number,
            "players": [
                {
                    "name": p.name,
                    "quota": None if p.quota is None else p.quota.label(),
                    "collection": [c.label() for c in p.collection],
                    "score": p.score,
                    "achieve_count": p.achieve_count,
                    "max_single_score": p.max_single_score,
                }
                for p in self.players
            ],
        }

    def all_card_ids(self) -> list[int]:
        ids: list[int] = []
        ids.extend(c.id for c in self.deck)
        ids.extend(c.id for c in self.removed)
        ids.extend(c.id for c in self.market)
        ids.extend(c.id for c in self.discard)
        for p in self.players:
            if p.quota is not None:
                ids.append(p.quota.id)
            ids.extend(c.id for c in p.collection)
            ids.extend(c.id for c in p.achieved)
        return ids

    def _canonicalize(self, action: Action) -> Action:
        if not isinstance(action, Collect):
            return action
        order = {card.id: index for index, card in enumerate(self.market)}
        return Collect(tuple(sorted(action.card_ids, key=lambda card_id: order.get(card_id, 10**9))))

    def _take_market(self, card_id: int) -> Card:
        for i, card in enumerate(self.market):
            if card.id == card_id:
                return self.market.pop(i)
        raise ValueError(f"card {card_id} is not in the market")

    def _achieve(self, player: Player, cards: list[Card], rank: int) -> None:
        player.achieved.extend(cards)
        pts = len(cards) + bonus(rank)
        player.score += pts
        player.achieve_count += 1
        player.max_single_score = max(player.max_single_score, pts)

    def _tied(self, a: int, b: int) -> bool:
        pa, pb = self.players[a], self.players[b]
        return (
            pa.score == pb.score
            and pa.achieve_count == pb.achieve_count
            and pa.max_single_score == pb.max_single_score
        )

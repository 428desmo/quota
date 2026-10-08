"""Quota rules engine. Section 2 of the spec wins over the overview."""

from __future__ import annotations

import itertools
import random
from dataclasses import dataclass, field
from typing import Literal

from quota.cards import Card, bonus, make_deck, score_for, sequence_bonus

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


@dataclass(frozen=True)
class Bundle:
    kind: str
    has_wild: bool


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
    bundles: list[Bundle] = field(default_factory=list)
    reshuffle_take_left: int = 0
    double_action_left: int = 0


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
    sequence_rule: bool = False
    title_rule: bool = False
    title_min_achieves: int = 3
    title_mono_bonus: int = 15
    title_purist_bonus: int = 5
    title_variety_bonus: int = 5
    special_actions_rule: bool = False
    reshuffle_take_uses: int = 1
    double_action_uses: int = 1
    rounds: int | None = None
    item_set: str = "trade"

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

    def resolved_rounds(self) -> int:
        count = 1 if self.rounds is None else self.rounds
        if count < 1:
            raise ValueError("rounds must be at least 1")
        return count


@dataclass
class Game:
    config: GameConfig
    deck: list[Card]
    removed: list[Card]
    market: list[Card | None]
    discard: list[Card]
    players: list[Player]
    current: int
    no_gain_streak: int
    stall_flag: bool
    finished: bool
    end_reason: EndReason | None
    turn_number: int
    log: list[str]
    goods: tuple[int, int, int, int]
    rng: random.Random
    reshuffle_count: int = 0
    turn_gain: bool = False
    plan: Literal["normal", "reshuffle", "double"] = "normal"
    double_stage: int = 0
    double_gained: bool = False
    turn_order: list[int] = field(default_factory=list)
    order_cursor: int = 0
    round_index: int = 1
    round_count: int = 1
    awaiting_next_round: bool = False
    round_end_reason: EndReason | None = None

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
        take_left = cfg.reshuffle_take_uses if cfg.special_actions_rule else 0
        double_left = cfg.double_action_uses if cfg.special_actions_rule else 0
        players = [
            Player(
                name=names[i],
                is_human=i in human,
                reshuffle_take_left=take_left,
                double_action_left=double_left,
            )
            for i in range(cfg.num_players)
        ]
        # One draw here keeps the goods deal on the same stream as earlier seeds.
        rng.randrange(cfg.num_players)
        from quota.items import deal_goods

        goods = deal_goods(rng)
        turn_order = list(range(cfg.num_players))
        rng.shuffle(turn_order)
        game = cls(
            config=cfg,
            deck=deck,
            removed=removed,
            market=market,
            discard=[],
            players=players,
            current=turn_order[0],
            no_gain_streak=0,
            stall_flag=False,
            finished=False,
            end_reason=None,
            turn_number=1,
            log=[],
            goods=goods,
            rng=rng,
            turn_gain=False,
            turn_order=turn_order,
            order_cursor=0,
            round_index=1,
            round_count=cfg.resolved_rounds(),
        )
        game.log.append(f"第1ラウンド 先手: {players[turn_order[0]].name}")
        return game

    def _can_collect_more(self, player) -> bool:
        assert player.quota is not None and player.quota.rank is not None
        if player.quota.rank - 1 - len(player.collection) <= 0:
            return False
        return any(c is not None and (c.suit == player.quota.suit or c.suit == "JOKER") for c in self.market)

    def market_size(self) -> int:
        return self.config.resolved_market_size()

    def legal_actions(self, seat: int | None = None) -> list[Action]:
        if self.finished or self.awaiting_next_round:
            return []
        p = self.players[self.current if seat is None else seat]
        if p.quota is None:
            acts: list[Action] = [
                TakeQuota(c.id) for c in self.market if c is not None and c.suit != "JOKER"
            ]
            return acts + [Pass()]
        assert p.quota.rank is not None
        eligible = [
            c.id
            for c in self.market
            if c is not None and (c.suit == p.quota.suit or c.suit == "JOKER")
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
        if self.awaiting_next_round:
            raise RuntimeError("round is waiting to advance")
        if not self.is_legal(action):
            raise ValueError(f"illegal action: {action}")
        p = self.players[self.current]
        gained = False
        theme = self.theme()
        if isinstance(action, TakeQuota):
            card = self._take_market(action.card_id)
            gained = True
            if card.rank == 1:
                self._achieve(p, [card], 1)
                self.log.append(f"{p.name} が {card.label(theme)} をノルマ札にし、即達成（1点）")
            else:
                p.quota = card
                p.collection = []
                self.log.append(
                    f"{p.name} が {card.label(theme)} をノルマ札にした（{card.rank}枚、{score_for(card.rank)}点）"
                )
        elif isinstance(action, Collect):
            assert p.quota is not None and p.quota.rank is not None
            taken = [self._take_market(card_id) for card_id in action.card_ids]
            gained = True
            p.collection.extend(taken)
            labels = "、".join(card.label(theme) for card in taken)
            self.log.append(f"{p.name} が {labels} を収集")
            self.turn_gain = True
            if 1 + len(p.collection) == p.quota.rank:
                rank = p.quota.rank
                cards = [p.quota, *p.collection]
                self._achieve(p, cards, rank)
                p.quota = None
                p.collection = []
                self.log.append(f"{p.name} がノルマ達成（{score_for(rank)}点）")
            elif p.collection and not self._can_collect_more(p):
                self.log.append(f"{p.name} は取れる札を取り切った")
            else:
                return
        elif isinstance(action, Abandon):
            assert p.quota is not None
            self.discard.extend([p.quota, *p.collection])
            self.log.append(f"{p.name} がノルマを放棄した")
            p.quota = None
            p.collection = []
            return
        elif isinstance(action, Pass):
            self.log.append(f"{p.name} はパス")
        else:
            raise TypeError(action)

        self._close_action(gained)

    def declare_reshuffle(self) -> None:
        """15.3.2. Replace the market, then the player takes one normal action."""
        player = self.players[self.current]
        self._declare("reshuffle")
        self._self_reshuffle()
        self.log.append(f"{player.name} が配り直し＆取得を宣言し、場を配り直した")

    def declare_double(self) -> None:
        """15.3.3. The next two actions belong to this turn, with a refill between them."""
        player = self.players[self.current]
        self._declare("double")
        self.double_stage = 1
        self.double_gained = False
        self.log.append(f"{player.name} がダブルアクションを宣言した")

    def cancel_double(self) -> None:
        """Undo a double declaration before any card has been touched."""
        if self.finished:
            raise RuntimeError("game is already finished")
        if self.plan != "double" or self.double_stage != 1 or self.turn_gain:
            raise ValueError("ダブルアクションは取り消せません")
        player = self.players[self.current]
        player.double_action_left += 1
        self.plan = "normal"
        self.double_stage = 0
        self.double_gained = False
        self.log.append(f"{player.name} がダブルアクションの宣言を取り消した")

    def _declare(self, kind: str) -> None:
        if self.finished:
            raise RuntimeError("game is already finished")
        if self.awaiting_next_round:
            raise RuntimeError("round is waiting to advance")
        if not self.config.special_actions_rule:
            raise ValueError("特殊アクションは採用されていません")
        if self.plan != "normal" or self.turn_gain:
            raise ValueError("この手番では特殊アクションを宣言できません")
        player = self.players[self.current]
        if kind == "reshuffle":
            if player.reshuffle_take_left < 1:
                raise ValueError("配り直し＆取得は使い切っています")
            player.reshuffle_take_left -= 1
            self.plan = "reshuffle"
            return
        if kind == "double":
            if player.double_action_left < 1:
                raise ValueError("ダブルアクションは使い切っています")
            player.double_action_left -= 1
            self.plan = "double"
            return
        raise ValueError(kind)

    def _self_reshuffle(self) -> None:
        self.deck.extend(card for card in self.market if card is not None)
        self.market = []
        self.rng.shuffle(self.deck)
        self.market = [self.deck.pop() for _ in range(self.market_size())]

    def _close_action(self, gained: bool) -> None:
        gained = gained or self.turn_gain
        if self.plan == "double" and self.double_stage == 1:
            self.double_gained = gained
            self.turn_gain = False
            if not self.begin_turn():
                return
            self.double_stage = 2
            return
        self._end_turn(gained or self.double_gained)

    def _end_turn(self, gained: bool) -> None:
        if gained:
            self.no_gain_streak = 0
            self.stall_flag = False
        else:
            self.no_gain_streak += 1
            if self.no_gain_streak == self.config.resolved_stall_threshold():
                if self.stall_flag:
                    self._end_round("STALL")
                    return
                self._self_reshuffle()
                self.no_gain_streak = 0
                self.stall_flag = True
                self.reshuffle_count += 1
                self.log.append("場を配り直した")

        self.turn_gain = False
        self.plan = "normal"
        self.double_stage = 0
        self.double_gained = False
        self.order_cursor = (self.order_cursor + 1) % len(self.turn_order)
        self.current = self.turn_order[self.order_cursor]
        self.turn_number += 1
        self.begin_turn()

    def begin_turn(self) -> bool:
        """Refill empty market slots from the left. False means the game ended."""
        while len(self.market) < self.market_size():
            self.market.append(None)
        for index, card in enumerate(self.market):
            if card is not None:
                continue
            if not self.deck:
                self._end_round("DECK")
                return False
            self.market[index] = self.deck.pop()
        return True

    def previous_seat(self) -> int:
        count = len(self.turn_order)
        return self.turn_order[(self.order_cursor - 1) % count]

    def begin_next_round(self) -> None:
        if self.finished or not self.awaiting_next_round:
            raise RuntimeError("次のラウンドはありません")
        self._bank_round()
        self.round_index += 1
        self.awaiting_next_round = False
        self.round_end_reason = None
        self.cpu_takes = {}
        from quota.items import deal_goods

        self.goods = deal_goods(self.rng)
        self._open_round()

    def _end_round(self, reason: EndReason) -> None:
        self.round_end_reason = reason
        label = "山札切れ" if reason == "DECK" else "膠着の連続"
        self.log.append(label)
        if self.round_index >= self.round_count:
            self.finished = True
            self.end_reason = reason
            self.awaiting_next_round = False
            if self.round_count > 1:
                self.log.append("ゲーム終了")
            return
        self.awaiting_next_round = True
        self.log.append(f"第{self.round_index}ラウンド終了")

    def _bank_round(self) -> None:
        for player in self.players:
            player.score += self.sequence_points(player) + self.title_points(player)
            player.achieved.clear()
            player.bundles.clear()
            player.quota = None
            player.collection.clear()

    def _open_round(self) -> None:
        cfg = self.config
        deck = make_deck(cfg.num_decks)
        self.rng.shuffle(deck)
        self.removed = [deck.pop() for _ in range(cfg.removed_count)]
        self.market = [deck.pop() for _ in range(cfg.resolved_market_size())]
        self.deck = deck
        self.discard = []
        take_left = cfg.reshuffle_take_uses if cfg.special_actions_rule else 0
        double_left = cfg.double_action_uses if cfg.special_actions_rule else 0
        for player in self.players:
            player.reshuffle_take_left = take_left
            player.double_action_left = double_left
        self.no_gain_streak = 0
        self.stall_flag = False
        self.turn_gain = False
        self.plan = "normal"
        self.double_stage = 0
        self.double_gained = False
        self.turn_number = 1
        self.reshuffle_count = 0
        self.order_cursor = (self.round_index - 1) % len(self.turn_order)
        self.current = self.turn_order[self.order_cursor]
        self.log.append(f"第{self.round_index}ラウンド 先手: {self.players[self.current].name}")

    def ranking(self) -> list[list[int]]:
        """Seats grouped best-first. Ties share a group."""
        seats = list(range(len(self.players)))
        seats.sort(
            key=lambda i: (
                self.final_score(self.players[i]),
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

    def theme(self):
        from quota.items import theme_for

        return theme_for(self.goods)

    def public_view(self) -> dict:
        """Observation without deck order or removed cards."""
        theme = self.theme()
        return {
            "item_set": theme.id,
            "market": [None if c is None else c.label(theme) for c in self.market],
            "discard": [c.label(theme) for c in self.discard],
            "deck_count": len(self.deck),
            "current": self.current,
            "no_gain_streak": self.no_gain_streak,
            "stall_flag": self.stall_flag,
            "finished": self.finished,
            "end_reason": self.end_reason,
            "turn_number": self.turn_number,
            "round_index": self.round_index,
            "round_count": self.round_count,
            "awaiting_next_round": self.awaiting_next_round,
            "round_end_reason": self.round_end_reason,
            "turn_order": list(self.turn_order),
            "sequence_rule": self.config.sequence_rule,
            "special_actions_rule": self.config.special_actions_rule,
            "plan": self.plan,
            "double_stage": self.double_stage,
            "players": [
                {
                    "name": p.name,
                    "quota": None if p.quota is None else p.quota.label(theme),
                    "collection": [c.label(theme) for c in p.collection],
                    "score": self.final_score(p),
                    "delivery_score": p.score,
                    "sequence_bonus": self.sequence_points(p),
                    "achieve_count": p.achieve_count,
                    "max_single_score": p.max_single_score,
                    "reshuffle_take_left": p.reshuffle_take_left,
                    "double_action_left": p.double_action_left,
                }
                for p in self.players
            ],
        }

    def all_card_ids(self) -> list[int]:
        ids: list[int] = []
        ids.extend(c.id for c in self.deck)
        ids.extend(c.id for c in self.removed)
        ids.extend(c.id for c in self.market if c is not None)
        ids.extend(c.id for c in self.discard)
        for p in self.players:
            if p.quota is not None:
                ids.append(p.quota.id)
            ids.extend(c.id for c in p.collection)
            ids.extend(c.id for c in p.achieved)
        return ids

    def final_score(self, player: Player) -> int:
        return player.score + self.sequence_points(player) + self.title_points(player)

    def title_awards(self, player: Player) -> list[tuple[str, int]]:
        if not self.config.title_rule:
            return []
        awards: list[tuple[str, int]] = []
        if {"S", "H", "D", "C", "JOKER"}.issubset({card.suit for card in player.achieved}):
            awards.append(("五種の品揃え", self.config.title_variety_bonus))
        bundles = player.bundles
        if len(bundles) < self.config.title_min_achieves:
            return awards
        if len({bundle.kind for bundle in bundles}) == 1:
            awards.append(("単色達成", self.config.title_mono_bonus))
        if not any(bundle.has_wild for bundle in bundles):
            awards.append(("生粋の買い付け", self.config.title_purist_bonus))
        return awards

    def title_points(self, player: Player) -> int:
        return sum(points for _, points in self.title_awards(player))

    def sequence_points(self, player: Player) -> int:
        if not self.config.sequence_rule:
            return 0
        return sequence_bonus(player.achieved)

    def is_legal(self, action: Action) -> bool:
        if isinstance(action, Collect):
            player = self.players[self.current]
            if player.quota is None or player.quota.rank is None:
                return False
            ids = action.card_ids
            if len(ids) != len(set(ids)) or len(ids) < 1:
                return False
            need = player.quota.rank - 1 - len(player.collection)
            if len(ids) > need:
                return False
            eligible = {
                card.id
                for card in self.market
                if card is not None and (card.suit == player.quota.suit or card.suit == "JOKER")
            }
            return set(ids) <= eligible
        return action.key() in {item.key() for item in self.legal_actions()}

    def _canonicalize(self, action: Action) -> Action:
        if not isinstance(action, Collect):
            return action
        order = {card.id: index for index, card in enumerate(self.market) if card is not None}
        return Collect(tuple(sorted(action.card_ids, key=lambda card_id: order.get(card_id, 10**9))))

    def _take_market(self, card_id: int) -> Card:
        for i, card in enumerate(self.market):
            if card is not None and card.id == card_id:
                self.market[i] = None
                return card
        raise ValueError(f"card {card_id} is not in the market")

    def _achieve(self, player: Player, cards: list[Card], rank: int) -> None:
        player.achieved.extend(cards)
        player.bundles.append(Bundle(cards[0].suit, any(card.suit == "JOKER" for card in cards)))
        pts = len(cards) + bonus(rank)
        player.score += pts
        player.achieve_count += 1
        player.max_single_score = max(player.max_single_score, pts)

    def _tied(self, a: int, b: int) -> bool:
        pa, pb = self.players[a], self.players[b]
        return (
            self.final_score(pa) == self.final_score(pb)
            and pa.achieve_count == pb.achieve_count
            and pa.max_single_score == pb.max_single_score
        )

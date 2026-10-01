"""CPU characters. A character is a strategy, a switch trigger, and a later strategy.

When the two strategies are the same, the trigger does not change play.
Names are adjective + noun and do not describe the strategy.
"""

from __future__ import annotations

import random
from dataclasses import dataclass

from quota.ai import _maybe_special, _order_for_sequence, choose_stock
from quota.cards import score_for
from quota.engine import Abandon, Action, Collect, Game, Pass, TakeQuota

# Taking style, then when it gives up a quota.
# 0 六粘り: near 6, never
# 1 六・達成不能: near 6, abandon when the quota cannot be finished
# 2 六・直前かぶり: near 6, abandon when the previous player just took the same suit
# 3 六・交換: near 6, swap an untouched quota for one at least 2 closer to 6
# 4 六・柔軟: near 6, any of those three reasons
# 5 大粘り: ranks 7-10, never
# 6 大・かぶり: ranks 7-10, abandon when a rival has also just started that suit
# 7 小回り: ranks 3-5, drop an untouched 8+ if a 5 or less is showing
# 8 エース: aces, else the lowest rank, never
# 9 標準: the original CPU
STRATEGY_COUNT = 10

# 0 behind by 8 or more
# 1 another seat holds the same suit
# 2 this character has abandoned once
# 3 deck has 36 cards or fewer
# 4 an opponent has finished a rank of 7 or more
# 5 turn 18 or later
TRIGGER_COUNT = 6

CONSISTENT_CHANCE = 2 / 3

ADJECTIVES = (
    "放浪する",
    "ご機嫌な",
    "心配性の",
    "まぶしい",
    "午後の",
    "逆さまの",
    "古びた",
    "遠回りな",
    "ひなたの",
    "夜更かしの",
    "まるい",
    "斜めの",
    "潮風の",
    "まばゆい",
    "陽気な",
    "ひんやりした",
    "とろける",
    "ささやく",
    "まどろむ",
    "きらめく",
    "風向きの",
    "忘れ物の",
    "とけない",
    "うたたねする",
    "こっそりした",
    "そわそわした",
    "のんびりした",
    "くすぐったい",
    "まばたきする",
    "星を見る",
)

NOUNS = (
    "ロボット",
    "冷蔵庫",
    "秋刀魚",
    "急須",
    "気球",
    "鉛筆",
    "灯台",
    "饅頭",
    "鍵盤",
    "帆船",
    "温度計",
    "風鈴",
    "地球儀",
    "金魚",
    "ラジオ",
    "梯子",
    "石鹸",
    "蒲鉾",
    "湯のみ",
    "靴べら",
    "郵便箱",
    "蝶番",
    "時計塔",
    "雲",
)

_NAME_STEP = 409


def character_count() -> int:
    return STRATEGY_COUNT * TRIGGER_COUNT * STRATEGY_COUNT


def encode(before: int, trigger: int, after: int) -> int:
    return (before * TRIGGER_COUNT + trigger) * STRATEGY_COUNT + after


def decode(character_id: int) -> tuple[int, int, int]:
    after = character_id % STRATEGY_COUNT
    rest = character_id // STRATEGY_COUNT
    trigger = rest % TRIGGER_COUNT
    before = rest // TRIGGER_COUNT
    return before, trigger, after


def character_name(character_id: int) -> str:
    space = len(ADJECTIVES) * len(NOUNS)
    index = (character_id * _NAME_STEP) % space
    return ADJECTIVES[index // len(NOUNS)] + NOUNS[index % len(NOUNS)]


def pick_character(rng: random.Random) -> int:
    """Consistent characters are more likely than ones that switch."""
    if rng.random() < CONSISTENT_CHANCE:
        strategy = rng.randrange(STRATEGY_COUNT)
        trigger = rng.randrange(TRIGGER_COUNT)
        return encode(strategy, trigger, strategy)
    before = rng.randrange(STRATEGY_COUNT)
    after = rng.randrange(STRATEGY_COUNT - 1)
    if after >= before:
        after += 1
    return encode(before, rng.randrange(TRIGGER_COUNT), after)


def assign_seats(game: Game, rng: random.Random | None = None) -> None:
    """Name each CPU seat with a character and remember how it plays."""
    picker = rng or random.Random()
    used: set[int] = set()
    for player in game.players:
        if player.is_human:
            continue
        character_id = _fresh(picker, used)
        player.name = character_name(character_id)
        bind(player, Mind(character_id))


def bind_replacement(player, rng: random.Random | None = None) -> None:
    """A seat that became a CPU keeps its name and gains a character."""
    if mind_for(player) is not None:
        return
    bind(player, Mind(pick_character(rng or random.Random())))


@dataclass
class Mind:
    character_id: int
    switched: bool = False
    abandoned: bool = False

    def choose(self, game: Game) -> Action:
        before, trigger, after = decode(self.character_id)
        if before != after and not self.switched and _trigger(trigger, game, self):
            self.switched = True
        strategy = after if self.switched else before
        action = choose_stock(game) if strategy == 9 else _choose_styled(game, strategy)
        if isinstance(action, Abandon):
            self.abandoned = True
        return action


def bind(player, mind: Mind) -> None:
    player.cpu_mind = mind


def mind_for(player) -> Mind | None:
    return getattr(player, "cpu_mind", None)


def _fresh(rng: random.Random, used: set[int]) -> int:
    for _ in range(64):
        character_id = pick_character(rng)
        if character_id not in used:
            used.add(character_id)
            return character_id
    character_id = pick_character(rng)
    used.add(character_id)
    return character_id


def _choose_styled(game: Game, strategy: int) -> Action:
    if _wants_abandon(strategy, game):
        return Abandon()
    _maybe_special(game)
    player = game.players[game.current]
    if player.quota is None:
        return _take(game, strategy)
    return _collect(game)


def _take(game: Game, strategy: int) -> Action:
    cards = [card for card in game.market if card is not None and card.suit != "JOKER" and card.rank]
    if not cards:
        return Pass()
    card = _PICK[strategy](cards)
    return TakeQuota(card.id)


def _collect(game: Game) -> Action:
    player = game.players[game.current]
    if player.quota is None or player.quota.rank is None:
        return Pass()
    need = player.quota.rank - 1 - len(player.collection)
    if need <= 0:
        return Pass()
    eligible = [
        card
        for card in game.market
        if card is not None and (card.suit == player.quota.suit or card.suit == "JOKER")
    ]
    if not eligible:
        return Pass()
    suits = [card for card in eligible if card.suit != "JOKER"]
    jokers = [card for card in eligible if card.suit == "JOKER"]
    chosen = _order_for_sequence(player, suits + jokers)[:need]
    return Collect(tuple(card.id for card in chosen))


def _pick_six(cards):
    best = min(cards, key=lambda card: (abs(card.rank - 6), -card.rank))
    if best.rank >= 11:
        for card in cards:
            if card.rank == 1:
                return card
    return best


def _pick_big(cards):
    band = [card for card in cards if 7 <= card.rank <= 10]
    if band:
        return max(band, key=lambda card: card.rank)
    huge = [card for card in cards if card.rank >= 11]
    if huge:
        return min(huge, key=lambda card: card.rank)
    return _pick_six(cards)


def _pick_small(cards):
    def key(card):
        if card.rank in (3, 4, 5):
            return (0, abs(card.rank - 4), card.rank)
        if card.rank == 1:
            return (2, 0, 0)
        return (1, abs(card.rank - 4), card.rank)

    return min(cards, key=key)


def _pick_ace(cards):
    for card in cards:
        if card.rank == 1:
            return card
    return min(cards, key=lambda card: card.rank)


_PICK = {
    0: _pick_six,
    1: _pick_six,
    2: _pick_six,
    3: _pick_six,
    4: _pick_six,
    5: _pick_big,
    6: _pick_big,
    7: _pick_small,
    8: _pick_ace,
}


def _wants_abandon(strategy: int, game: Game) -> bool:
    player = game.players[game.current]
    if player.quota is None or game.turn_gain or game.plan != "normal" or game.double_stage:
        return False
    if strategy == 1:
        return _keep_outlook(game) < 1
    if strategy == 2:
        return _previous_clash(game)
    if strategy == 3:
        return _swap_ready(game)
    if strategy == 4:
        return _keep_outlook(game) < 1 or _previous_clash(game) or _swap_ready(game)
    if strategy == 6:
        return _big_clash(game)
    if strategy == 7:
        return _small_escape(game)
    return False


def _trigger(trigger: int, game: Game, mind: Mind) -> bool:
    if trigger == 0:
        return _behind(game)
    if trigger == 1:
        return _suit_shared(game)
    if trigger == 2:
        return mind.abandoned
    if trigger == 3:
        return len(game.deck) <= 36
    if trigger == 4:
        return _opponent_large(game)
    if trigger == 5:
        return game.turn_number >= 18
    return False


def _behind(game: Game) -> bool:
    seat = game.current
    mine = game.final_score(game.players[seat])
    others = [game.final_score(player) for index, player in enumerate(game.players) if index != seat]
    return max(others) >= mine + 8


def _suit_shared(game: Game) -> bool:
    me = game.players[game.current]
    if me.quota is None:
        return False
    return any(
        player is not me and player.quota is not None and player.quota.suit == me.quota.suit
        for player in game.players
    )


def _opponent_large(game: Game) -> bool:
    for index, player in enumerate(game.players):
        if index == game.current:
            continue
        if _large_bundle(player):
            return True
    return False


def _large_bundle(player) -> bool:
    index = 0
    while index < len(player.achieved):
        rank = player.achieved[index].rank
        if rank is None:
            break
        if rank >= 7:
            return True
        index += rank
    return False


def _visible_counts(game: Game):
    suits = {suit: 0 for suit in "SHDC"}
    jokers = 0
    for card in (*game.market, *game.discard):
        if card is None:
            continue
        if card.suit == "JOKER":
            jokers += 1
        else:
            suits[card.suit] += 1
    for player in game.players:
        for card in (player.quota, *player.collection, *player.achieved):
            if card is None:
                continue
            if card.suit == "JOKER":
                jokers += 1
            else:
                suits[card.suit] += 1
    return suits, jokers


def _keep_outlook(game: Game) -> float:
    player = game.players[game.current]
    if player.quota is None or player.quota.rank is None:
        return 0.0
    rank = player.quota.rank
    need = rank - 1 - len(player.collection)
    if need <= 0:
        return float(score_for(rank))
    market_same = sum(1 for card in game.market if card is not None and card.suit == player.quota.suit)
    market_jokers = sum(1 for card in game.market if card is not None and card.suit == "JOKER")
    left = need - min(need, market_same + market_jokers)
    if left <= 0:
        return float(score_for(rank))
    suits, jokers = _visible_counts(game)
    hidden_suits = 26 - suits[player.quota.suit]
    hidden_jokers = 4 - jokers
    if hidden_suits + hidden_jokers < left:
        return 0.0
    future = (hidden_suits + hidden_jokers) * len(game.deck) / max(len(game.deck) + 8, 1)
    if future < left:
        return score_for(rank) * 0.05
    chance = min(1.0, (future / (left * 3.0)) ** 1.4)
    return score_for(rank) * chance


def _previous_clash(game: Game) -> bool:
    me = game.players[game.current]
    if me.quota is None or me.quota.rank is None:
        return False
    gathered = 1 + len(me.collection)
    if me.quota.rank - gathered < 5 or gathered * 2 > me.quota.rank:
        return False
    suit = _previous_take(game)
    if suit != me.quota.suit:
        return False
    prev = (game.current - 1) % len(game.players)
    other = game.players[prev]
    if other.quota is None or other.quota.suit != me.quota.suit or other.quota.rank is None:
        return False
    return other.quota.rank - 1 - len(other.collection) >= 3


def _swap_ready(game: Game) -> bool:
    me = game.players[game.current]
    if me.quota is None or me.quota.rank is None or me.collection:
        return False
    rank = me.quota.rank
    candidates = [
        card
        for card in game.market
        if card is not None and card.suit not in ("JOKER", me.quota.suit) and card.rank
    ]
    if not candidates:
        return False
    if rank >= 11 and any(card.rank == 1 for card in candidates):
        return True
    best = min(candidates, key=lambda card: (abs(card.rank - 6), -card.rank))
    return abs(rank - 6) - abs(best.rank - 6) >= 2


def _big_clash(game: Game) -> bool:
    me = game.players[game.current]
    if me.quota is None or me.quota.rank is None or me.quota.rank < 7 or len(me.collection) > 2:
        return False
    return any(
        player is not me
        and player.quota is not None
        and player.quota.rank is not None
        and player.quota.suit == me.quota.suit
        and player.quota.rank >= 7
        and len(player.collection) <= 2
        for player in game.players
    )


def _small_escape(game: Game) -> bool:
    me = game.players[game.current]
    if me.quota is None or me.quota.rank is None or me.quota.rank < 8 or me.collection:
        return False
    return any(
        card is not None and card.suit not in ("JOKER", me.quota.suit) and card.rank and card.rank <= 5
        for card in game.market
    )


_installed = False


def _previous_take(game: Game):
    record = getattr(game, "cpu_takes", None)
    if not record or "last" not in record:
        return None
    seat, suit = record["last"]
    if seat != game.previous_seat():
        return None
    return suit


def _install_take_tracker() -> None:
    global _installed
    if _installed:
        return
    _installed = True
    original = Game.step

    def step(self, action):
        took = None
        if isinstance(action, TakeQuota):
            for card in self.market:
                if card is not None and card.id == action.card_id:
                    took = card.suit
                    break
        seat = self.current
        turn = self.turn_number
        original(self, action)
        record = getattr(self, "cpu_takes", None)
        if record is None:
            record = {}
            self.cpu_takes = record
        progress = record.get("progress")
        if progress is None or progress[0] != seat or progress[1] != turn:
            progress = [seat, turn, None]
            record["progress"] = progress
        if took is not None:
            progress[2] = took
        if self.current != seat or self.turn_number != turn or self.finished:
            record["last"] = (seat, progress[2])

    Game.step = step


_install_take_tracker()

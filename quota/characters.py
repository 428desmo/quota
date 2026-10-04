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
# 10 単色一徹: the suit it already owns, swap an untouched quota of another suit for it
# 11 山読み: the card with the best finish outlook, swap when a clearly better one shows
STRATEGY_COUNT = 12

# 0 behind by 8 or more
# 1 another seat holds the same suit
# 2 this character has abandoned once
# 3 deck has 36 cards or fewer
# 4 an opponent has finished a rank of 7 or more
# 5 turn 18 or later
TRIGGER_COUNT = 6

# How much a character cares about title bonuses.
# 0 none
# 1 only when the points could catch the leader
# 2 always
TITLE_STANCE_COUNT = 3

# Whether it takes a quota just to keep a card away from a rival.
# 0 none
# 1 when the market holds nothing its own style wants
DENIAL_COUNT = 2

CONSISTENT_CHANCE = 2 / 3
DENIAL_CHANCE = 1 / 3

# A rival quota worth this much or more is worth blocking.
BLOCK_FLOOR = 4

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
    "朝焼けの",
    "雨上がりの",
    "ひょっこりの",
    "よれよれの",
    "ほっこりの",
    "さらさらの",
    "ぽかぽかの",
    "ねむたげな",
    "ひらひらの",
    "ゆらゆらの",
    "ほのぼのした",
    "ざわざわした",
    "てかてかの",
    "ふわふわの",
    "きょとんとした",
    "たそがれの",
    "寄り道の",
    "石畳の",
    "波止場の",
    "霧の",
    "真昼の",
    "沖合の",
    "内緒の",
    "おぼろげな",
    "のどかな",
    "真夜中の",
    "旅支度の",
    "こぼれ落ちる",
    "はしゃぐ",
    "遠雷の",
    "ひそやかな",
    "たっぷりの",
    "ちぐはぐな",
    "そっけない",
    "おせっかいな",
    "気まぐれな",
    "まっさらの",
    "水玉の",
    "うららかな",
    "しとやかな",
    "ひたむきな",
    "寝ぼけた",
    "大あくびの",
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
    "団扇",
    "提灯",
    "箒",
    "算盤",
    "徳利",
    "煙突",
    "車輪",
    "看板",
    "植木鉢",
    "やかん",
    "下駄",
    "火鉢",
    "行灯",
    "扇子",
    "硯",
    "竹籠",
    "手鏡",
    "布団",
    "羅針盤",
    "砂時計",
    "麦藁帽",
    "桟橋",
    "浮き輪",
    "封蝋",
    "天秤",
    "樽",
    "麻袋",
    "帆布",
    "綱",
    "舵輪",
    "書棚",
    "木箱",
    "便箋",
    "切符",
    "風見鶏",
    "糸車",
    "蓄音機",
    "望遠鏡",
    "万年筆",
    "水差し",
    "茶筒",
    "風呂敷",
    "巾着",
    "帳面",
    "竹馬",
    "紙風船",
    "椅子",
    "階段",
)

_NAME_STEP = 409


def character_count() -> int:
    return STRATEGY_COUNT * TRIGGER_COUNT * STRATEGY_COUNT * TITLE_STANCE_COUNT * DENIAL_COUNT


def encode(before: int, trigger: int, after: int, stance: int = 0, denial: int = 0) -> int:
    base = (before * TRIGGER_COUNT + trigger) * STRATEGY_COUNT + after
    return (base * TITLE_STANCE_COUNT + stance) * DENIAL_COUNT + denial


def decode(character_id: int) -> tuple[int, int, int, int, int]:
    denial = character_id % DENIAL_COUNT
    rest = character_id // DENIAL_COUNT
    stance = rest % TITLE_STANCE_COUNT
    rest //= TITLE_STANCE_COUNT
    after = rest % STRATEGY_COUNT
    rest //= STRATEGY_COUNT
    trigger = rest % TRIGGER_COUNT
    before = rest // TRIGGER_COUNT
    return before, trigger, after, stance, denial


def character_name(character_id: int) -> str:
    space = len(ADJECTIVES) * len(NOUNS)
    index = (character_id * _NAME_STEP) % space
    return ADJECTIVES[index // len(NOUNS)] + NOUNS[index % len(NOUNS)]


def pick_character(rng: random.Random) -> int:
    """Consistent characters are more likely than ones that switch."""
    stance = rng.randrange(TITLE_STANCE_COUNT)
    denial = 1 if rng.random() < DENIAL_CHANCE else 0
    if rng.random() < CONSISTENT_CHANCE:
        strategy = rng.randrange(STRATEGY_COUNT)
        trigger = rng.randrange(TRIGGER_COUNT)
        return encode(strategy, trigger, strategy, stance, denial)
    before = rng.randrange(STRATEGY_COUNT)
    after = rng.randrange(STRATEGY_COUNT - 1)
    if after >= before:
        after += 1
    return encode(before, rng.randrange(TRIGGER_COUNT), after, stance, denial)


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
    block_seat: int | None = None
    block_suit: str | None = None
    block_turn: int = -1

    def choose(self, game: Game) -> Action:
        before, trigger, after, stance, denial = decode(self.character_id)
        if before != after and not self.switched and _trigger(trigger, game, self):
            self.switched = True
        strategy = after if self.switched else before
        action = _choose_for(game, strategy, stance, denial, self)
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


def _choose_for(game: Game, strategy: int, stance: int, denial: int = 0, mind: Mind | None = None) -> Action:
    if mind is not None and mind.block_suit is not None:
        settled = _block_settle(game, mind)
        if settled is not None:
            return settled
    if _title_chase(game, stance):
        override = _title_override(game, strategy)
        if override is not None:
            return override
    if denial and mind is not None:
        block = _block_take(game, strategy, mind)
        if block is not None:
            return block
    action = choose_stock(game) if strategy == 9 else _choose_styled(game, strategy)
    if _title_chase(game, stance) and isinstance(action, Collect):
        return _without_wilds(game, action)
    return action


def _choose_styled(game: Game, strategy: int) -> Action:
    if _wants_abandon(strategy, game):
        return Abandon()
    _maybe_special(game)
    player = game.players[game.current]
    if player.quota is None:
        return _take(game, strategy)
    return _collect(game)


def _take(game: Game, strategy: int) -> Action:
    cards = _takeable(game)
    if not cards:
        return Pass()
    return TakeQuota(_pick_card(game, strategy, cards).id)


def _takeable(game: Game):
    return [card for card in game.market if card is not None and card.suit != "JOKER" and card.rank]


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


def _pick_mono(game: Game, cards):
    kind = _mono_kind(game.players[game.current])
    if kind is not None:
        same = [card for card in cards if card.suit == kind]
        if same:
            return _pick_six(same)
    return _pick_six(cards)


def _pick_reader(game: Game, cards):
    return max(cards, key=lambda card: (_prospect(game, card), card.rank))


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


def _pick_card(game: Game, strategy: int, cards):
    if strategy == 9:
        return _stock_pick(cards)
    if strategy == 10:
        return _pick_mono(game, cards)
    if strategy == 11:
        return _pick_reader(game, cards)
    return _PICK[strategy](cards)


def _title_chase(game: Game, stance: int) -> bool:
    if stance == 0 or not game.config.title_rule:
        return False
    if stance == 2:
        return True
    player = game.players[game.current]
    mine = game.final_score(player)
    others = [game.final_score(other) for index, other in enumerate(game.players) if index != game.current]
    if not others:
        return False
    gap = max(others) - mine
    return gap > 0 and _title_potential(game, player) >= gap


def _title_potential(game: Game, player) -> int:
    bundles = player.bundles
    kinds = {bundle.kind for bundle in bundles}
    wild = any(bundle.has_wild for bundle in bundles)
    earned = len(bundles) >= game.config.title_min_achieves
    points = 0
    if not (earned and len(kinds) == 1) and len(kinds) <= 1:
        points += game.config.title_mono_bonus
    if not (earned and not wild) and not wild:
        points += game.config.title_purist_bonus
    return points


def _mono_kind(player):
    kinds = {bundle.kind for bundle in player.bundles}
    if len(kinds) != 1:
        return None
    return next(iter(kinds))


def _title_override(game: Game, strategy: int):
    player = game.players[game.current]
    kind = _mono_kind(player)
    if kind is None:
        return None
    same = [card for card in game.market if card is not None and card.suit == kind and card.rank]
    if player.quota is not None:
        if player.quota.suit == kind or player.collection or game.turn_gain or game.plan != "normal" or game.double_stage:
            return None
        return Abandon() if same else None
    if not same:
        return None
    return TakeQuota(_pick_card(game, strategy, same).id)


def _forget_block(mind: Mind) -> None:
    mind.block_seat = None
    mind.block_suit = None
    mind.block_turn = -1


def _block_settle(game: Game, mind: Mind):
    """Drop a blocking quota on the next turn and go back to the real plan."""
    player = game.players[game.current]
    if player.quota is None or player.quota.suit != mind.block_suit or player.collection:
        _forget_block(mind)
        return None
    if game.turn_number == mind.block_turn:
        return None
    if game.turn_gain or game.plan != "normal" or game.double_stage:
        return None
    seat = mind.block_seat
    rival = game.players[seat] if seat is not None and 0 <= seat < len(game.players) else None
    still_held = (
        rival is not None
        and rival is not player
        and rival.quota is not None
        and rival.quota.suit == mind.block_suit
    )
    _forget_block(mind)
    return Abandon() if still_held else None


def _rival_pressure(game: Game):
    """The suit each rival most wants, keyed by suit, worth blocking."""
    pressure: dict[str, tuple[int, int, int]] = {}
    for seat, rival in enumerate(game.players):
        if seat == game.current or rival.quota is None or rival.quota.rank is None:
            continue
        need = rival.quota.rank - 1 - len(rival.collection)
        if need < 1 or score_for(rival.quota.rank) < BLOCK_FLOOR:
            continue
        weight = (score_for(rival.quota.rank), -need, seat)
        held = pressure.get(rival.quota.suit)
        if held is None or weight[:2] > held[:2]:
            pressure[rival.quota.suit] = weight
    return pressure


def _block_take(game: Game, strategy: int, mind: Mind):
    player = game.players[game.current]
    if player.quota is not None:
        return None
    cards = _takeable(game)
    if not cards:
        return None
    if _appealing(game, strategy, _pick_card(game, strategy, cards)):
        return None
    pressure = _rival_pressure(game)
    wanted = [card for card in cards if card.rank > 1 and card.suit in pressure]
    if not wanted:
        return None
    suits = {card.suit for card in wanted}
    target = max(suits, key=lambda suit: pressure[suit][:2])
    options = [card for card in wanted if card.suit == target]
    card = _pick_card(game, strategy, options)
    mind.block_seat = pressure[target][2]
    mind.block_suit = target
    mind.block_turn = game.turn_number
    return TakeQuota(card.id)


def _appealing(game: Game, strategy: int, card) -> bool:
    """Whether the best card on offer already suits this character."""
    rank = card.rank
    if strategy in (5, 6):
        return 7 <= rank <= 10
    if strategy == 7:
        return 3 <= rank <= 5
    if strategy == 8:
        return rank == 1
    if strategy == 10:
        kind = _mono_kind(game.players[game.current])
        if kind is not None:
            return card.suit == kind
    if strategy == 11:
        return _prospect(game, card) >= 3
    return abs(rank - 6) <= 1 or rank == 1


def _stock_pick(cards):
    def key(card):
        sweet = -abs(card.rank - 6)
        return (score_for(card.rank) if card.rank == 1 else 0, sweet, -card.rank)

    best = max(cards, key=key)
    if best.rank >= 11 and any(card.rank == 1 for card in cards):
        return next(card for card in cards if card.rank == 1)
    return best


def _without_wilds(game: Game, action: Collect) -> Action:
    player = game.players[game.current]
    if any(bundle.has_wild for bundle in player.bundles):
        return action
    by_id = {card.id: card for card in game.market if card is not None}
    kept = [card_id for card_id in action.card_ids if by_id[card_id].suit != "JOKER"]
    if kept:
        return Collect(tuple(kept))
    return Pass()


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
    if strategy == 10:
        return _mono_swap(game)
    if strategy == 11:
        return _reader_swap(game)
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


def _prospect(game: Game, card) -> float:
    """The outlook of a market card, as if it were already this seat's quota."""
    rank = card.rank
    need = rank - 1
    if need <= 0:
        return float(score_for(rank))
    market_same = sum(
        1 for other in game.market if other is not None and other is not card and other.suit == card.suit
    )
    market_jokers = sum(1 for other in game.market if other is not None and other.suit == "JOKER")
    left = need - min(need, market_same + market_jokers)
    if left <= 0:
        return float(score_for(rank))
    suits, jokers = _visible_counts(game)
    hidden = (26 - suits[card.suit]) + (4 - jokers)
    if hidden < left:
        return 0.0
    future = hidden * len(game.deck) / max(len(game.deck) + 8, 1)
    if future < left:
        return score_for(rank) * 0.05
    chance = min(1.0, (future / (left * 3.0)) ** 1.4)
    return score_for(rank) * chance


def _mono_swap(game: Game) -> bool:
    me = game.players[game.current]
    if me.quota is None or me.collection:
        return False
    kind = _mono_kind(me)
    if kind is None or me.quota.suit == kind:
        return False
    return any(card is not None and card.suit == kind and card.rank for card in game.market)


def _reader_swap(game: Game) -> bool:
    me = game.players[game.current]
    if me.quota is None or me.quota.rank is None:
        return False
    outlook = _keep_outlook(game)
    if outlook < 1:
        return True
    if me.collection:
        return False
    best = 0.0
    for card in game.market:
        if card is None or card.suit == "JOKER" or card.rank is None or card.suit == me.quota.suit:
            continue
        best = max(best, _prospect(game, card))
    return best - outlook >= 3


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

import struct
from pathlib import Path

from quota.cards import Card
from quota.engine import Game, GameConfig
from quota.items import default_item_set, goods_pool, resolve_item_set, wild_good

ROOT = Path(__file__).resolve().parent.parent
NAMES = (
    "胡椒",
    "シナモン",
    "クローブ",
    "ナツメグ",
    "サフラン",
    "生姜",
    "絹",
    "綿花",
    "ルビー",
    "サファイア",
    "エメラルド",
    "真珠",
    "トルコ石",
    "瑪瑙",
    "明礬",
    "染料",
    "藍",
    "香木",
    "蜜蝋",
    "毛皮",
    "琥珀",
    "穀物",
    "木材",
    "象牙",
    "陶磁器",
    "ソーダ灰",
    "皮革",
)


def test_trade_goods_are_the_only_set():
    pool = goods_pool()
    assert [item.name for item in pool] == list(NAMES)
    assert wild_good().name == "金貨"
    assert wild_good().file == "0_ducato"
    theme = default_item_set()
    assert theme.id == "trade"
    assert theme.name == "交易品"
    spice = Card(0, "S", 1)
    cinnamon = Card(1, "H", 11)
    clove = Card(2, "C", 13)
    nutmeg = Card(3, "D", 4)
    wild = Card(4, "JOKER", None)
    assert theme.label(spice) == "1 胡椒 #0"
    assert theme.label(cinnamon) == "11 シナモン #1"
    assert theme.label(clove) == "13 クローブ #2"
    assert theme.label(nutmeg) == "4 ナツメグ #3"
    assert theme.label(wild) == "＊ 金貨 #4"
    assert resolve_item_set("交易品").label(wild) == "＊ 金貨 #4"


def test_each_game_deals_four_goods_from_the_pool():
    game = Game.start(GameConfig(seed=7, num_players=3))
    again = Game.start(GameConfig(seed=7, num_players=3))
    other = Game.start(GameConfig(seed=8, num_players=3))
    assert game.goods == again.goods == (17, 10, 16, 1)
    assert len(set(game.goods)) == 4
    assert game.goods != other.goods
    names = [goods_pool()[index].name for index in game.goods]
    assert names == [game.theme().faces[kind].name for kind in ("K1", "K2", "K3", "K4")]
    assert game.theme().faces["WILD"].name == "金貨"


def test_goods_icons_fit_in_72_pixels():
    for good in (*goods_pool(), wild_good()):
        data = (ROOT / "web" / "goods" / f"{good.file}.png").read_bytes()
        assert data.startswith(b"\x89PNG\r\n\x1a\n")
        width, height = struct.unpack(">II", data[16:24])
        assert (width, height, data[25]) == (72, 72, 6)


def test_web_cards_point_at_the_icon():
    from quota.web import _card

    game = Game.start(GameConfig(seed=7, num_players=3))
    card = next(c for c in game.market if c is not None)
    payload = _card(card, game.theme())
    assert payload["image"].endswith(".png")
    assert (ROOT / "web" / payload["image"]).is_file()
    assert payload["goods"] in NAMES or payload["goods"] == "金貨"

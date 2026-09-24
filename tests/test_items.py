from quota.cards import Card
from quota.items import catalog, default_item_set, resolve_item_set


def test_catalog_has_one_default():
    sets = catalog()
    assert len(sets) >= 3
    assert sum(item.default for item in sets) == 1
    assert default_item_set().id == "trade"
    assert default_item_set().name == "交易品"


def test_labels_follow_the_selected_set():
    spice = Card(0, "S", 1)
    silk = Card(1, "H", 11)
    tea = Card(2, "C", 13)
    gem = Card(3, "D", 4)
    wild = Card(4, "JOKER", None)
    trade = resolve_item_set("交易品")
    assert trade.label(spice) == "🌶️1 香辛料 #0"
    assert trade.label(silk) == "🎀11 絹 #1"
    assert trade.label(tea) == "🫖13 茶 #2"
    assert trade.label(gem) == "💎4 宝石 #3"
    assert trade.label(wild) == "🪙＊ 銀貨 #4"
    cards = resolve_item_set("playing_cards")
    assert cards.label(spice) == "♤A スペード #0"
    assert cards.label(tea) == "♧K クラブ #2"
    assert cards.label(gem) == "♦︎4 ダイヤ #3"
    assert resolve_item_set("sweets").label(spice) == "🍰① ケーキ #0"
    assert resolve_item_set("edo_market").label(silk) == "🍶十一 酒 #1"

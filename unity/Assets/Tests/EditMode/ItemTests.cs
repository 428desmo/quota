using NUnit.Framework;

namespace Quota.Tests
{
    public class ItemTests
    {
        [Test]
        public void CatalogHasOneDefault()
        {
            var sets = ItemCatalog.Sets;
            Assert.GreaterOrEqual(sets.Count, 3);
            var defaults = 0;
            foreach (var item in sets)
                if (item.IsDefault) defaults++;
            Assert.AreEqual(1, defaults);
            Assert.AreEqual("trade", ItemCatalog.Default().Id);
            Assert.AreEqual("交易品", ItemCatalog.Default().Name);
        }

        [Test]
        public void LabelsFollowTheSelectedSet()
        {
            var spice = new Card(0, Suit.S, 1);
            var silk = new Card(1, Suit.H, 11);
            var tea = new Card(2, Suit.C, 13);
            var gem = new Card(3, Suit.D, 4);
            var wild = new Card(4, Suit.Joker, null);
            var trade = ItemCatalog.Resolve("交易品");
            Assert.AreEqual("🌶️1 香辛料 #0", trade.Label(spice));
            Assert.AreEqual("🎀11 絹 #1", trade.Label(silk));
            Assert.AreEqual("🫖13 茶 #2", trade.Label(tea));
            Assert.AreEqual("💎4 宝石 #3", trade.Label(gem));
            Assert.AreEqual("🪙＊ 銀貨 #4", trade.Label(wild));
            var cards = ItemCatalog.Resolve("playing_cards");
            Assert.AreEqual("♤A スペード #0", cards.Label(spice));
            Assert.AreEqual("♧K クラブ #2", cards.Label(tea));
            Assert.AreEqual("♦︎4 ダイヤ #3", cards.Label(gem));
            Assert.AreEqual("🍰① ケーキ #0", ItemCatalog.Resolve("sweets").Label(spice));
            Assert.AreEqual("🍶十一 酒 #1", ItemCatalog.Resolve("edo_market").Label(silk));
        }
    }
}

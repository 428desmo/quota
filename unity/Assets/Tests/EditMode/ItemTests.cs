using System.Collections.Generic;
using NUnit.Framework;

namespace Quota.Tests
{
    public class ItemTests
    {
        static readonly string[] Names =
        {
            "胡椒", "シナモン", "クローブ", "ナツメグ", "サフラン", "生姜", "絹",
            "綿花", "ルビー", "サファイア", "エメラルド", "真珠", "トルコ石", "瑪瑙",
            "明礬", "染料", "藍", "香木", "蜜蝋", "毛皮", "琥珀", "穀物", "木材",
            "象牙", "陶磁器", "ソーダ灰", "皮革",
        };

        [Test]
        public void TradeGoodsAreTheOnlySet()
        {
            Assert.AreEqual(27, ItemCatalog.Count);
            for (var i = 0; i < Names.Length; i++)
                Assert.AreEqual(Names[i], ItemCatalog.NameAt(i));
            var trade = ItemCatalog.Resolve("交易品");
            Assert.AreEqual("trade", trade.Id);
            Assert.AreEqual("交易品", trade.Name);
            Assert.AreEqual("1 胡椒 #0", trade.Label(new Card(0, Suit.S, 1)));
            Assert.AreEqual("11 シナモン #1", trade.Label(new Card(1, Suit.H, 11)));
            Assert.AreEqual("13 クローブ #2", trade.Label(new Card(2, Suit.C, 13)));
            Assert.AreEqual("4 ナツメグ #3", trade.Label(new Card(3, Suit.D, 4)));
            Assert.AreEqual("＊ 金貨 #4", trade.Label(new Card(4, Suit.Joker, null)));
        }

        [Test]
        public void EachGameDealsFourGoodsFromThePool()
        {
            var game = Game.Start(new GameConfig { Seed = 7, NumPlayers = 3 });
            var again = Game.Start(new GameConfig { Seed = 7, NumPlayers = 3 });
            var other = Game.Start(new GameConfig { Seed = 8, NumPlayers = 3 });
            CollectionAssert.AreEqual(new[] { 17, 10, 16, 1 }, game.Goods);
            CollectionAssert.AreEqual(game.Goods, again.Goods);
            Assert.AreEqual(4, new HashSet<int>(game.Goods).Count);
            CollectionAssert.AreNotEqual(game.Goods, other.Goods);
            var theme = game.Theme();
            Assert.AreEqual(ItemCatalog.NameAt(game.Goods[0]), theme.FaceFor(new Card(0, Suit.S, 1)).Name);
            Assert.AreEqual(ItemCatalog.NameAt(game.Goods[1]), theme.FaceFor(new Card(1, Suit.H, 1)).Name);
            Assert.AreEqual(ItemCatalog.NameAt(game.Goods[2]), theme.FaceFor(new Card(2, Suit.C, 1)).Name);
            Assert.AreEqual(ItemCatalog.NameAt(game.Goods[3]), theme.FaceFor(new Card(3, Suit.D, 1)).Name);
            Assert.AreEqual("金貨", theme.FaceFor(new Card(4, Suit.Joker, null)).Name);
        }
    }
}

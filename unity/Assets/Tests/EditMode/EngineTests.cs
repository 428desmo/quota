using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Quota.Tests
{
    public class EngineTests
    {
        [Test]
        public void PythonRandomMatchesCPython()
        {
            var rng = new PythonRandom(7);
            CollectionAssert.AreEqual(new[] { 5, 2, 6, 0, 1, 8, 1, 5 }, Enumerable.Range(0, 8).Select(_ => rng.RandBelow(10)).ToArray());
            rng = new PythonRandom(7);
            var deck = Enumerable.Range(0, 108).ToList();
            rng.Shuffle(deck);
            CollectionAssert.AreEqual(new[] { 92, 32, 57, 22, 0, 99, 67, 90, 42, 14, 59, 78 }, deck.Take(12).ToArray());
            Assert.AreEqual(2, rng.RandBelow(3));
        }

        [Test]
        public void DeckHas108UniqueCards()
        {
            var deck = Cards.MakeDeck(2);
            Assert.AreEqual(108, deck.Count);
            Assert.AreEqual(108, deck.Select(card => card.Id).Distinct().Count());
            Assert.AreEqual(4, deck.Count(card => card.Suit == Suit.Joker));
        }

        [TestCase(1, 1)]
        [TestCase(6, 6)]
        [TestCase(7, 8)]
        [TestCase(9, 10)]
        [TestCase(10, 13)]
        [TestCase(12, 15)]
        [TestCase(13, 19)]
        public void ScoreTable(int rank, int points)
        {
            Assert.AreEqual(points, Cards.ScoreFor(rank));
            Assert.AreEqual(points - rank, Cards.Bonus(rank));
        }

        [Test]
        public void SameSeedSameOpening()
        {
            var a = Game.Start(Config(7));
            var b = Game.Start(Config(7));
            CollectionAssert.AreEqual(Ids(a.Market), Ids(b.Market));
            Assert.AreEqual(a.Current, b.Current);
            CollectionAssert.AreEqual(Ids(a.Removed), Ids(b.Removed));
        }

        [Test]
        public void AceScoresImmediately()
        {
            var game = Game.Start(Config(1));
            var ace = game.Market.First(card => card.Rank == 1);
            var seat = game.Current;
            game.Step(new TakeQuota(ace.Id));
            var player = game.Players[seat];
            Assert.IsNull(player.Quota);
            Assert.AreEqual(1, player.Score);
            Assert.AreEqual(1, player.AchieveCount);
            Assert.AreEqual(game.MarketSize(), game.Market.Count);
        }

        [Test]
        public void InvariantsHoldForRandomGames()
        {
            var rng = new PythonRandom(0);
            for (var n = 0; n < 2; n++)
            {
                var players = n == 0 ? 3 : 4;
                for (var k = 0; k < 30; k++)
                {
                    var game = Game.Start(Config(n * 1000 + k, players));
                    var removed = Ids(game.Removed);
                    while (!game.Finished)
                    {
                        game.Step(rng.Choice(game.LegalActions()));
                        AssertInvariants(game, removed);
                    }
                    Assert.That(game.EndReason, Is.EqualTo("DECK").Or.EqualTo("STALL"));
                    Assert.AreEqual(108, game.AllCardIds().Count);
                }
            }
        }

        [Test]
        public void ReplayMatches()
        {
            const int seed = 42;
            var rng = new PythonRandom(1);
            var game = Game.Start(Config(seed));
            var script = new List<string>();
            while (!game.Finished)
            {
                var action = rng.Choice(game.LegalActions());
                script.Add(action.Key);
                game.Step(action);
            }
            var replay = Game.Start(Config(seed));
            foreach (var key in script)
            {
                var action = replay.LegalActions().First(item => item.Key == key);
                replay.Step(action);
            }
            CollectionAssert.AreEqual(game.Players.Select(player => player.Score).ToArray(), replay.Players.Select(player => player.Score).ToArray());
            Assert.AreEqual(game.EndReason, replay.EndReason);
            CollectionAssert.AreEqual(Ids(game.Market), Ids(replay.Market));
        }

        [Test]
        public void CpuGameFinishes()
        {
            var game = Game.Start(new GameConfig { NumPlayers = 3, Seed = 3, HumanSeats = new List<int>() });
            var guard = 0;
            while (!game.Finished)
            {
                game.Step(Cpu.ChooseAction(game));
                guard++;
                Assert.Less(guard, 5000);
            }
            Assert.Greater(game.Players.Sum(player => player.Score), 0);
        }

        [Test]
        public void JokerCannotBeQuota()
        {
            var game = Game.Start(Config(1));
            game.Market.Clear();
            game.Market.AddRange(game.Deck.Where(card => card.Suit == Suit.Joker).Take(2));
            var kinds = new HashSet<System.Type>(game.LegalActions().Select(action => action.GetType()));
            CollectionAssert.AreEquivalent(new[] { typeof(Pass) }, kinds);
        }

        [Test]
        public void CollectMatchingSuitOnly()
        {
            var game = Game.Start(Config(2));
            var card = game.Market.First(item => item.Rank != null && item.Rank >= 2);
            game.Step(new TakeQuota(card.Id));
            var owner = Owner(game);
            game.Current = owner;
            foreach (var action in game.LegalActions().OfType<Collect>())
            {
                var quota = game.Players[owner].Quota;
                var need = quota.Rank.Value - 1 - game.Players[owner].Collection.Count;
                Assert.GreaterOrEqual(action.CardIds.Length, 1);
                Assert.LessOrEqual(action.CardIds.Length, need);
                foreach (var cardId in action.CardIds)
                {
                    var picked = game.Market.First(item => item.Id == cardId);
                    Assert.That(picked.Suit == quota.Suit || picked.Suit == Suit.Joker);
                }
            }
        }

        [Test]
        public void CollectSeveralThenRefillOnNextTurn()
        {
            var game = Game.Start(Config(4));
            var quota = game.Market.First(card => card.Rank != null && card.Rank >= 3);
            game.Step(new TakeQuota(quota.Id));
            var owner = Owner(game);
            game.Current = owner;
            var suit = game.Players[owner].Quota.Suit;
            var extras = TakeFromDeck(game, suit, 2);
            game.Deck.AddRange(game.Market.Take(2));
            game.Market.RemoveRange(0, 2);
            game.Market.InsertRange(0, extras);
            var before = game.Deck.Count;
            var marketLen = game.Market.Count;
            game.Step(new Collect(new[] { extras[0].Id }));
            Assert.AreEqual(owner, game.Current);
            Assert.AreEqual(1, game.Players[owner].Collection.Count);
            Assert.AreEqual(marketLen - 1, game.Market.Count);
            game.Step(new Collect(new[] { extras[1].Id }));
            Assert.IsNotNull(game.Players[owner].Quota);
            Assert.AreEqual(2, game.Players[owner].Collection.Count);
            Assert.AreEqual(marketLen - 2, game.Market.Count);
            Assert.AreEqual(before, game.Deck.Count);
            game.Step(new Pass());
            Assert.AreNotEqual(owner, game.Current);
            Assert.AreEqual(game.MarketSize(), game.Market.Count);
            Assert.AreEqual(before - 2, game.Deck.Count);
        }

        [Test]
        public void TakingTheLastEligibleCardEndsTheTurn()
        {
            var game = Game.Start(Config(4));
            var quota = game.Market.First(card => card.Rank != null && card.Rank >= 4);
            game.Step(new TakeQuota(quota.Id));
            var owner = Owner(game);
            game.Current = owner;
            var suit = game.Players[owner].Quota.Suit;
            var extra = game.Deck.First(card => card.Suit == suit);
            game.Deck.Remove(extra);
            var others = game.Market.Where(card => card.Suit != suit && card.Suit != Suit.Joker).Take(game.MarketSize() - 1).ToList();
            game.Market.Clear();
            game.Market.Add(extra);
            game.Market.AddRange(others);
            game.Step(new Collect(new[] { extra.Id }));
            Assert.AreNotEqual(owner, game.Current);
            Assert.AreEqual(1, game.Players[owner].Collection.Count);
            Assert.AreEqual(game.MarketSize(), game.Market.Count);
        }

        [Test]
        public void DeckEndsAtTheStartOfTheNextTurn()
        {
            var game = Game.Start(Config(5));
            var card = game.Market.First(item => item.Rank != null && item.Rank >= 2);
            game.Deck.Clear();
            game.Step(new TakeQuota(card.Id));
            Assert.IsTrue(game.Finished);
            Assert.AreEqual("DECK", game.EndReason);
            var owner = game.Players.First(player => player.Quota != null);
            Assert.IsNotNull(owner.Quota);
            Assert.AreEqual(game.MarketSize() - 1, game.Market.Count);
        }

        [Test]
        public void TitleBonusNeedsThreeAchievesAndScoresEachAward()
        {
            var game = Game.Start(new GameConfig { Seed = 1, NumPlayers = 3, TitleRule = true });
            var player = game.Players[0];
            player.Bundles.Add(new Bundle("S", false));
            player.Bundles.Add(new Bundle("S", true));
            Assert.AreEqual(0, game.TitleAwards(player).Count);
            player.Bundles.Add(new Bundle("S", false));
            CollectionAssert.AreEqual(new[] { ("単色達成", 15) }, game.TitleAwards(player));
            player.Bundles.Clear();
            player.Bundles.Add(new Bundle("S", false));
            player.Bundles.Add(new Bundle("H", false));
            player.Bundles.Add(new Bundle("C", false));
            CollectionAssert.AreEqual(new[] { ("生粋の買い付け", 5) }, game.TitleAwards(player));
            player.Bundles.Clear();
            player.Bundles.Add(new Bundle("S", false));
            player.Bundles.Add(new Bundle("S", false));
            player.Bundles.Add(new Bundle("S", false));
            Assert.AreEqual(20, game.TitlePoints(player));
            Assert.AreEqual(player.Score + 20, game.FinalScore(player));
        }

        [Test]
        public void SequenceBonusExamples()
        {
            Card C(Suit suit, int rank, int id) => new Card(id, suit, rank);
            var joker = new Card(99, Suit.Joker, null);
            Assert.AreEqual(4, Cards.SequenceBonus(new List<Card> { C(Suit.H, 5, 1), C(Suit.H, 1, 2), C(Suit.C, 1, 3), C(Suit.C, 1, 4), C(Suit.D, 3, 5) }));
            Assert.AreEqual(3, Cards.SequenceBonus(new List<Card> { C(Suit.H, 3, 1), C(Suit.H, 4, 2), C(Suit.C, 3, 3), C(Suit.C, 2, 4), C(Suit.C, 5, 5), C(Suit.D, 3, 6) }));
            Assert.AreEqual(0, Cards.SequenceBonus(new List<Card> { C(Suit.S, 6, 1), joker, C(Suit.S, 7, 2) }));
            Assert.AreEqual(0, Cards.SequenceBonus(new List<Card> { C(Suit.S, 13, 1), C(Suit.H, 1, 2) }));
            Assert.AreEqual(4, Cards.SequenceBonus(new List<Card> { C(Suit.S, 4, 1), C(Suit.H, 4, 2), C(Suit.D, 4, 3) }));
        }

        [Test]
        public void CollectOrderIsKeptAndScoresOnlyWhenEnabled()
        {
            var game = Game.Start(new GameConfig { Seed = 8, NumPlayers = 3, SequenceRule = true });
            var quota = game.Market.First(card => card.Rank == 3);
            game.Step(new TakeQuota(quota.Id));
            var owner = Owner(game);
            game.Current = owner;
            var suit = game.Players[owner].Quota.Suit;
            var extras = TakeFromDeck(game, suit, 2);
            game.Deck.AddRange(game.Market.Take(2));
            game.Market.RemoveRange(0, 2);
            game.Market.InsertRange(0, extras);
            game.Step(new Collect(new[] { extras[1].Id, extras[0].Id }));
            CollectionAssert.AreEqual(new[] { quota.Id, extras[1].Id, extras[0].Id }, Ids(game.Players[owner].Achieved));
            Assert.AreEqual(Cards.SequenceBonus(game.Players[owner].Achieved), game.SequencePoints(game.Players[owner]));
            var plain = Game.Start(Config(8));
            Assert.AreEqual(0, plain.SequencePoints(plain.Players[0]));
        }

        [Test]
        public void SpecialActionsStartUnusedAndStayOffByDefault()
        {
            var plain = Game.Start(Config(1));
            Assert.IsTrue(plain.Players.TrueForAll(player => player.ReshuffleTakeLeft == 0 && player.DoubleActionLeft == 0));
            Assert.Throws<System.ArgumentException>(() => plain.DeclareDouble());
        }

        [Test]
        public void ReshuffleAndTakeReplacesTheMarketWithoutTouchingStall()
        {
            var game = Game.Start(new GameConfig { Seed = 5, NumPlayers = 3, SpecialActionsRule = true });
            var seat = game.Current;
            var before = Ids(game.Market);
            var streak = game.NoGainStreak;
            game.DeclareReshuffle();
            Assert.AreEqual(0, game.Players[seat].ReshuffleTakeLeft);
            Assert.AreEqual("reshuffle", game.Plan);
            CollectionAssert.AreNotEqual(before, Ids(game.Market));
            Assert.AreEqual(game.MarketSize(), game.Market.Count);
            Assert.AreEqual(streak, game.NoGainStreak);
            Assert.IsFalse(game.StallFlag);
            Assert.AreEqual(0, game.ReshuffleCount);
            Assert.Throws<System.ArgumentException>(() => game.DeclareDouble());
            game.Step(new Pass());
            Assert.AreNotEqual(seat, game.Current);
            Assert.AreEqual(1, game.NoGainStreak);
            Assert.AreEqual("normal", game.Plan);
        }

        [Test]
        public void DoubleActionTakesTwoActionsAndCountsOneMiss()
        {
            var game = Game.Start(new GameConfig { Seed = 6, NumPlayers = 3, SpecialActionsRule = true });
            var seat = game.Current;
            var turn = game.TurnNumber;
            game.DeclareDouble();
            Assert.AreEqual(0, game.Players[seat].DoubleActionLeft);
            game.Step(new Pass());
            Assert.AreEqual(seat, game.Current);
            Assert.AreEqual(turn, game.TurnNumber);
            Assert.AreEqual(2, game.DoubleStage);
            Assert.AreEqual(game.MarketSize(), game.Market.Count);
            game.Step(new Pass());
            Assert.AreNotEqual(seat, game.Current);
            Assert.AreEqual(turn + 1, game.TurnNumber);
            Assert.AreEqual(1, game.NoGainStreak);
            Assert.AreEqual("normal", game.Plan);
            Assert.AreEqual(0, game.DoubleStage);
        }

        [Test]
        public void DoubleActionEndsWhenTheRefillEmptiesTheDeck()
        {
            var game = Game.Start(new GameConfig { Seed = 7, NumPlayers = 3, SpecialActionsRule = true });
            var seat = game.Current;
            var take = game.LegalActions().OfType<TakeQuota>().First();
            game.DeclareDouble();
            game.Deck.Clear();
            game.Step(take);
            Assert.IsTrue(game.Finished);
            Assert.AreEqual("DECK", game.EndReason);
            Assert.AreEqual(seat, game.Current);
            var player = game.Players[seat];
            Assert.IsTrue(player.Quota != null || player.AchieveCount == 1);
        }

        [Test]
        public void CpuWithSpecialActionsFinishes()
        {
            var game = Game.Start(new GameConfig
            {
                NumPlayers = 3,
                Seed = 9,
                HumanSeats = new List<int>(),
                SpecialActionsRule = true,
            });
            var guard = 0;
            while (!game.Finished)
            {
                game.Step(Cpu.ChooseAction(game));
                AssertInvariants(game, Ids(game.Removed));
                guard++;
                Assert.Less(guard, 5000);
            }
            Assert.IsTrue(game.Players.TrueForAll(player => player.ReshuffleTakeLeft <= 1 && player.DoubleActionLeft <= 1));
        }

        static GameConfig Config(int seed, int players = 3)
        {
            return new GameConfig { Seed = seed, NumPlayers = players };
        }

        static List<int> Ids(IEnumerable<Card> cards)
        {
            return cards.Select(card => card.Id).ToList();
        }

        static int Owner(Game game)
        {
            return game.Players.FindIndex(player => player.Quota != null);
        }

        static List<Card> TakeFromDeck(Game game, Suit suit, int count)
        {
            var extras = new List<Card>();
            foreach (var card in game.Deck.ToList())
            {
                if (card.Suit != suit) continue;
                game.Deck.Remove(card);
                extras.Add(card);
                if (extras.Count == count) break;
            }
            Assert.AreEqual(count, extras.Count);
            return extras;
        }

        static void AssertInvariants(Game game, List<int> removedIds)
        {
            var ids = game.AllCardIds();
            Assert.AreEqual(108, ids.Count);
            Assert.AreEqual(108, ids.Distinct().Count());
            CollectionAssert.AreEqual(removedIds, Ids(game.Removed));
            Assert.AreEqual(8, game.Removed.Count);
            if (!game.Finished)
            {
                if (!game.TurnGain) Assert.AreEqual(game.MarketSize(), game.Market.Count);
                Assert.GreaterOrEqual(game.NoGainStreak, 0);
                Assert.Less(game.NoGainStreak, game.Config.ResolvedStallThreshold());
            }
            var takeCap = game.Config.SpecialActionsRule ? game.Config.ReshuffleTakeUses : 0;
            var doubleCap = game.Config.SpecialActionsRule ? game.Config.DoubleActionUses : 0;
            foreach (var player in game.Players)
            {
                Assert.GreaterOrEqual(player.ReshuffleTakeLeft, 0);
                Assert.LessOrEqual(player.ReshuffleTakeLeft, takeCap);
                Assert.GreaterOrEqual(player.DoubleActionLeft, 0);
                Assert.LessOrEqual(player.DoubleActionLeft, doubleCap);
                if (player.Quota == null)
                {
                    Assert.AreEqual(0, player.Collection.Count);
                }
                else
                {
                    Assert.AreNotEqual(Suit.Joker, player.Quota.Suit);
                    Assert.IsNotNull(player.Quota.Rank);
                    Assert.Less(1 + player.Collection.Count, player.Quota.Rank.Value);
                    foreach (var card in player.Collection)
                        Assert.That(card.Suit == player.Quota.Suit || card.Suit == Suit.Joker);
                }
            }
        }
    }
}

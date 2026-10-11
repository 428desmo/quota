using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Quota.Tests
{
    public class CpuProfilesTests
    {
        [Test]
        public void SixProfilesAreDistinctAndNominatableWithoutDuplication()
        {
            Assert.AreEqual(6, CpuProfiles.Names.Length);
            var used = new HashSet<int>();
            var rng = new Random(14);
            for (var i = 0; i < 4; i++)
            {
                var id = CpuProfiles.PickFresh(rng, used);
                Assert.That(id, Is.InRange(0, 5));
                Assert.IsFalse(string.IsNullOrEmpty(CpuProfiles.Names[id]));
            }
            Assert.AreEqual(4, used.Count);
        }

        [TestCase(3)]
        [TestCase(4)]
        public void StrongAbandonsContestedLargeQuotaWhileMiddleCollects(int characterId)
        {
            var game = Game.Start(new GameConfig { NumPlayers = 3, Seed = 4 });
            var cards = Cards.MakeDeck();
            var ownQuota = cards.First(card => card.Suit == Suit.S && card.Rank == 13);
            var rivalQuota = cards.First(card => card.Suit == Suit.S && card.Rank == 12);
            var matching = cards.First(card => card.Suit == Suit.S && card.Rank == 5);
            var alternative = cards.First(card => card.Suit == Suit.D && card.Rank == 4);
            var selected = new HashSet<int> { ownQuota.Id, rivalQuota.Id, matching.Id, alternative.Id };
            var remaining = cards.Where(card => !selected.Contains(card.Id))
                .OrderBy(card => card.Suit == Suit.S ? 0 : 1).ThenBy(card => card.Id).ToList();
            game.Market.Clear();
            game.Market.AddRange(new Card[] { matching, alternative, null, null, null, null, null });
            game.Discard.Clear();
            game.Discard.AddRange(remaining.Take(61));
            game.Deck.Clear();
            game.Deck.AddRange(remaining.Skip(61).Take(35));
            game.Removed.Clear();
            game.Removed.AddRange(remaining.Skip(96));
            game.Current = 0;
            game.Players[0].Quota = ownQuota;
            game.Players[1].Quota = rivalQuota;

            var middle = new ProfileMind(characterId, 1).Choose(game);
            var strong = new ProfileMind(characterId, 2).Choose(game);
            Assert.IsInstanceOf<Collect>(middle);
            Assert.IsInstanceOf<Abandon>(strong);
            Assert.IsTrue(game.IsLegal(strong));
        }
    }
}

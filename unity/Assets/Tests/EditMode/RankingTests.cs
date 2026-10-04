using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Quota.Tests
{
    public class RankingTests
    {
        [TearDown]
        public void Restore()
        {
            Ranking.Forget();
        }

        [Test]
        public void TheKeyIsTheStrategyDimensions()
        {
            Assert.AreEqual("11-4-5-2-1", Characters.KeyOf(Characters.Encode(11, 4, 5, 2, 1)));
            Assert.AreEqual("0-0-0-0-0", Characters.KeyOf(Characters.Encode(0, 0, 0, 0, 0)));
            Assert.AreEqual(Characters.KeyOf(Characters.Encode(3, 1, 4, 1, 0)), Characters.KeyOf(new[] { 3, 1, 4, 1, 0 }));
        }

        [Test]
        public void APlaceIsReadForEachCombinationAndTheRestTakeTheMiddle()
        {
            var listed = Characters.Encode(1, 0, 1, 0, 0);
            var missing = Characters.Encode(2, 0, 2, 0, 0);
            Ranking.LoadJson(
                "{\"format\":\"quota-cpu-ranking\",\"version\":2," +
                "\"dimensions\":[\"before\",\"trigger\",\"after\",\"stance\",\"denial\"]," +
                "\"award\":[3.0,2.0],\"seats\":4,\"mean\":1.25," +
                "\"provisional\":7,\"players\":[" +
                "{\"id\":[1,0,1,0,0],\"value\":1.75,\"games\":4,\"points\":7.0,\"rank\":2}]}");
            Assert.IsTrue(Ranking.IsLoaded);
            Assert.AreEqual(2, Ranking.PlaceOf(listed));
            Assert.AreEqual(7, Ranking.PlaceOf(missing));
            Assert.AreEqual($"{Characters.NameOf(listed)}(2)", Ranking.DisplayName(listed));
        }

        [Test]
        public void AnotherFormatIsIgnoredSoTheNameStaysPlain()
        {
            Ranking.LoadJson("{\"format\":\"something-else\",\"version\":2}");
            Assert.IsFalse(Ranking.IsLoaded);
            Ranking.LoadJson("{\"format\":\"quota-cpu-ranking\",\"version\":1}");
            Assert.IsFalse(Ranking.IsLoaded);
            Assert.AreEqual(0, Ranking.PlaceOf(0));
            Assert.AreEqual(Characters.NameOf(0), Ranking.DisplayName(0));
        }

        [Test]
        public void TheShippedFileGivesEveryCharacterAPlace()
        {
            if (!File.Exists(Ranking.Path))
            {
                Assert.Ignore("the ranking file is not in StreamingAssets");
                return;
            }
            Ranking.LoadJson(File.ReadAllText(Ranking.Path));
            Assert.IsTrue(Ranking.IsLoaded);
            foreach (var characterId in new[] { 0, 1, 1234, Characters.Count - 1 })
            {
                var place = Ranking.PlaceOf(characterId);
                Assert.Greater(place, 0);
                Assert.LessOrEqual(place, Characters.Count);
                StringAssert.EndsWith($"({place})", Ranking.DisplayName(characterId));
            }
        }
    }
}

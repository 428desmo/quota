using NUnit.Framework;

namespace Quota.Tests
{
    public class OfflineMatchTests
    {
        [Test]
        public void SoloGameReachesAResult()
        {
            var match = new OfflineMatch();
            match.Begin(new GameConfig
            {
                NumPlayers = 3,
                Seed = 11,
                HumanSeats = new System.Collections.Generic.List<int> { 0 },
                Names = new System.Collections.Generic.List<string> { "あなた", "CPU1", "CPU2" },
            });
            var guard = 0;
            while (!match.Game.Finished)
            {
                Assert.IsTrue(match.IsHumanTurn);
                var actions = match.Game.LegalActions();
                var pick = actions[0] is Pass && actions.Count > 1 ? actions[1] : actions[0];
                match.Act(pick);
                guard++;
                Assert.Less(guard, 5000);
            }
            Assert.That(match.Game.EndReason, Is.EqualTo("DECK").Or.EqualTo("STALL"));
            Assert.AreEqual(108, match.Game.AllCardIds().Count);
        }
    }
}

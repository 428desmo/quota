// Test-only adapters for Unity presentation dependencies. The engine and CPU
// under test are the actual, unmodified Unity C# sources.
using System;
using System.Linq;
using System.Collections.Generic;
namespace Quota {
    public sealed class ItemSet { public string Label(Card card) { return card.Id.ToString(); } }
    public static class ItemCatalog {
        public static int Count => 27;
        public static ItemSet Theme(int[] goods) { return new ItemSet(); }
        public static ItemSet Default() { return new ItemSet(); }
    }
    public static class Characters { public static void Forget(Game game) {} }
    public static class Reference {
        static string Ids(IEnumerable<Card> cards) { return string.Join(",", cards.Select(c => c == null ? -1 : c.Id)); }
        static void Print(Game g, string action) {
            var parts = new List<string> { action, g.RoundIndex.ToString(), g.Current.ToString(), g.TurnNumber.ToString(),
                g.Plan, g.DoubleStage.ToString(), g.TurnGain ? "1":"0", g.DoubleGained ? "1":"0",
                g.NoGainStreak.ToString(), g.StallFlag ? "1":"0", g.ReshuffleCount.ToString(),
                g.Finished ? "1":"0", g.AwaitingNextRound ? "1":"0", g.RoundEndReason ?? "",
                string.Join(",",g.Goods), string.Join(",",g.TurnOrder), Ids(g.Deck), Ids(g.Removed), Ids(g.Market), Ids(g.Discard) };
            foreach(var p in g.Players) parts.Add(string.Join(";", new[] {
                p.Quota == null ? "-1" : p.Quota.Id.ToString(), Ids(p.Collection), Ids(p.Achieved),
                p.Score.ToString(), g.FinalScore(p).ToString(), p.AchieveCount.ToString(), p.MaxSingleScore.ToString(),
                p.ReshuffleTakeLeft.ToString(), p.DoubleActionLeft.ToString(),
                string.Join(",",p.Bundles.Select(b => b.Kind + (b.HasWild ? "1":"0"))) }));
            Console.WriteLine(string.Join("|", parts));
        }
        public static void Main(string[] args) {
            var seed=int.Parse(args[0]); var count=int.Parse(args[1]); var strength=int.Parse(args[2]); var offset=int.Parse(args[3]);
            var rounds=args.Length>4?int.Parse(args[4]):count;
            var g=Game.Start(new GameConfig { Seed=seed, NumPlayers=count, Rounds=rounds,
                HumanSeats=new List<int>(), SequenceRule=true, TitleRule=true, SpecialActionsRule=true });
            CpuProfiles.BindInOrder(g, Enumerable.Range(0,count).Select(i=>(i+offset)%6).ToList(),strength);
            Print(g,"start");
            int guard=0;
            while (!g.Finished && guard++ < 10000) {
                if(g.AwaitingNextRound) {
                    g.BeginNextRound(); CpuProfiles.ObserveTransition(g,new List<Card>(),0,false,true); Print(g,"next"); continue;
                }
                var plan=g.Plan;
                var a=CpuProfiles.MindFor(g.Players[g.Current]).Choose(g);
                var declaration=plan==g.Plan ? "" : g.Plan + "/";
                var market=g.Market.ToList(); var deck=g.Deck.Count;
                g.Step(a);
                CpuProfiles.ObserveTransition(g,market,deck,a is Pass && !market.SequenceEqual(g.Market),false);
                Print(g,declaration+a.Key);
            }
            if (!g.Finished) throw new Exception("Reference match did not finish");
        }
    }
}

using System.Collections.Generic;
using System.Linq;

namespace Quota
{
    public sealed class OfflineMatch
    {
        public Game Game { get; private set; }

        public bool IsHumanTurn =>
            Game != null && !Game.Finished && !Game.AwaitingNextRound && Game.Players[Game.Current].IsHuman;

        public void Begin(GameConfig config, bool pumpCpus = true)
        {
            if (config.HumanSeats == null)
                config.HumanSeats = new List<int> { 0 };
            Game = Game.Start(config);
            if (pumpCpus) PumpCpus();
        }

        public void Clear()
        {
            Game = null;
        }

        public void Act(GameAction action)
        {
            var seat = Game.Current;
            var turn = Game.TurnNumber;
            var market = new List<Card>(Game.Market);
            var deck = Game.Deck.Count;
            Characters.Observe(Game, action);
            Game.Step(action);
            CpuProfiles.ObserveTransition(Game, market, deck, action is Pass && !market.SequenceEqual(Game.Market), false);
            Characters.CommitIfTurnEnded(Game, seat, turn);
            PumpCpus();
        }

        public bool StepOneCpu(System.Action<string, string, bool> notify = null)
        {
            if (Game.Finished || Game.AwaitingNextRound || Game.Players[Game.Current].IsHuman) return false;
            var player = Game.Players[Game.Current];
            var plan = Game.Plan;
            var action = Cpu.ChooseAction(Game);
            if (Game.Plan != plan && (Game.Plan == "double" || Game.Plan == "reshuffle"))
                notify?.Invoke(player.Name, Game.Plan, Game.TurnGain);
            notify?.Invoke(player.Name, action.Key, Game.TurnGain || Game.DoubleGained);
            var seat = Game.Current;
            var turn = Game.TurnNumber;
            var market = new List<Card>(Game.Market);
            var deck = Game.Deck.Count;
            Characters.Observe(Game, action);
            Game.Step(action);
            CpuProfiles.ObserveTransition(Game, market, deck, action is Pass && !market.SequenceEqual(Game.Market), false);
            Characters.CommitIfTurnEnded(Game, seat, turn);
            return true;
        }

        void PumpCpus()
        {
            var guard = 0;
            while (StepOneCpu())
            {
                guard++;
                if (guard > 5000) throw new System.InvalidOperationException("CPU did not finish a turn");
            }
        }
    }
}

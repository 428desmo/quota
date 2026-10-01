using System.Collections.Generic;

namespace Quota
{
    public sealed class OfflineMatch
    {
        public Game Game { get; private set; }

        public bool IsHumanTurn =>
            Game != null && !Game.Finished && !Game.AwaitingNextRound && Game.Players[Game.Current].IsHuman;

        public void Begin(GameConfig config, bool pumpCpus = true)
        {
            if (config.HumanSeats == null || config.HumanSeats.Count == 0)
                config.HumanSeats = new List<int> { 0 };
            Game = Game.Start(config);
            if (pumpCpus) PumpCpus();
        }

        public void Act(GameAction action)
        {
            var seat = Game.Current;
            var turn = Game.TurnNumber;
            Characters.Observe(Game, action);
            Game.Step(action);
            Characters.CommitIfTurnEnded(Game, seat, turn);
            PumpCpus();
        }

        public bool StepOneCpu()
        {
            if (Game.Finished || Game.AwaitingNextRound || Game.Players[Game.Current].IsHuman) return false;
            var action = Cpu.ChooseAction(Game);
            var seat = Game.Current;
            var turn = Game.TurnNumber;
            Characters.Observe(Game, action);
            Game.Step(action);
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

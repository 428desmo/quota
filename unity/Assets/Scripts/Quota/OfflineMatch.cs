using System.Collections.Generic;

namespace Quota
{
    public sealed class OfflineMatch
    {
        public Game Game { get; private set; }

        public bool IsHumanTurn =>
            Game != null && !Game.Finished && Game.Players[Game.Current].IsHuman;

        public void Begin(GameConfig config)
        {
            if (config.HumanSeats == null || config.HumanSeats.Count == 0)
                config.HumanSeats = new List<int> { 0 };
            Game = Game.Start(config);
            PumpCpus();
        }

        public void Act(GameAction action)
        {
            Game.Step(action);
            PumpCpus();
        }

        public bool StepOneCpu()
        {
            if (Game.Finished || Game.Players[Game.Current].IsHuman) return false;
            Game.Step(Cpu.ChooseAction(Game));
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

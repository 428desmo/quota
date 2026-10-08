using System.Collections.Generic;

namespace Quota
{
    public enum CoinKind
    {
        Green,
        Purple,
        Blue,
    }

    public readonly struct BonusCoin
    {
        public readonly CoinKind Kind;
        public readonly int CardId;
        public readonly bool InTray;
        public readonly int Serial;
        public readonly int Index;

        public BonusCoin(CoinKind kind, int cardId, bool inTray, int serial, int index)
        {
            Kind = kind;
            CardId = cardId;
            InTray = inTray;
            Serial = serial;
            Index = index;
        }

        public string Key => CardId + "-" + (int)Kind + "-" + Index;
    }

    public static class BonusCoins
    {
        public static List<BonusCoin> Plan(IReadOnlyList<Card> achieved, IReadOnlyList<Card> quotaLine, bool sequence, int titleCoins)
        {
            var coins = new List<BonusCoin>();
            var serial = 0;
            AddGreens(achieved, true);
            AddGreens(quotaLine, false);
            if (sequence)
            {
                var line = new List<Card>(achieved);
                line.AddRange(quotaLine);
                for (var i = 1; i < line.Count; i++)
                    Add(line, i, CoinKind.Purple, Cards.SequenceAt(line, i), i < achieved.Count);
            }
            for (var i = 0; i < titleCoins; i++) coins.Add(new BonusCoin(CoinKind.Blue, -1, true, serial++, i));
            return coins;

            void AddGreens(IReadOnlyList<Card> cards, bool bank)
            {
                var index = 0;
                while (index < cards.Count)
                {
                    var rank = cards[index].Rank;
                    if (rank == null) break;
                    var size = rank.Value;
                    Add(cards, index, CoinKind.Green, Cards.Bonus(size), bank);
                    index += size;
                }
            }

            void Add(IReadOnlyList<Card> cards, int index, CoinKind kind, int count, bool bank)
            {
                if (index < 0 || index >= cards.Count || count <= 0) return;
                var cardId = cards[index].Id;
                for (var i = 0; i < count; i++) coins.Add(new BonusCoin(kind, cardId, bank, serial++, i));
            }


        }
    }
}

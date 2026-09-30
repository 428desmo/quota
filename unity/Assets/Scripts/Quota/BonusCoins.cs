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
        public readonly int Serial;

        public BonusCoin(CoinKind kind, int cardId, int serial)
        {
            Kind = kind;
            CardId = cardId;
            Serial = serial;
        }
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
                for (var i = 1; i < achieved.Count; i++) AddPair(achieved[i - 1], achieved[i], true);
                if (achieved.Count > 0 && quotaLine.Count > 0) AddPair(achieved[achieved.Count - 1], quotaLine[0], false);
                for (var i = 1; i < quotaLine.Count; i++) AddPair(quotaLine[i - 1], quotaLine[i], false);
            }
            for (var i = 0; i < titleCoins; i++) coins.Add(new BonusCoin(CoinKind.Blue, -1, serial++));
            return coins;

            void AddGreens(IReadOnlyList<Card> cards, bool bank)
            {
                var index = 0;
                while (index < cards.Count)
                {
                    var rank = cards[index].Rank;
                    if (rank == null) break;
                    var size = rank.Value;
                    if (size >= 7) Add(cards, index + 6, CoinKind.Green, 1, bank);
                    if (size >= 10) Add(cards, index + 9, CoinKind.Green, 2, bank);
                    if (size == 13) Add(cards, index + 12, CoinKind.Green, 3, bank);
                    index += size;
                }
            }

            void Add(IReadOnlyList<Card> cards, int index, CoinKind kind, int count, bool bank)
            {
                if (index < 0 || index >= cards.Count || count <= 0) return;
                var cardId = bank ? -1 : cards[index].Id;
                for (var i = 0; i < count; i++) coins.Add(new BonusCoin(kind, cardId, serial++));
            }

            void AddPair(Card left, Card right, bool bank)
            {
                if (left.Suit == Suit.Joker || right.Suit == Suit.Joker) return;
                if (left.Rank == null || right.Rank == null) return;
                var count = left.Rank == right.Rank ? 2 : (System.Math.Abs(left.Rank.Value - right.Rank.Value) == 1 ? 1 : 0);
                if (count == 0) return;
                var cardId = bank ? -1 : right.Id;
                for (var i = 0; i < count; i++) coins.Add(new BonusCoin(CoinKind.Purple, cardId, serial++));
            }
        }
    }
}

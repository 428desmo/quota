using System;
using System.Collections.Generic;

namespace Quota
{
    public enum Suit
    {
        S,
        H,
        D,
        C,
        Joker,
    }

    public sealed class Card
    {
        public readonly int Id;
        public readonly Suit Suit;
        public readonly int? Rank;

        public Card(int id, Suit suit, int? rank)
        {
            Id = id;
            Suit = suit;
            Rank = rank;
        }

        public string Label(ItemSet itemSet = null)
        {
            return (itemSet ?? ItemCatalog.Default()).Label(this);
        }
    }

    public static class Cards
    {
        public static readonly Suit[] KindSuits = { Suit.S, Suit.H, Suit.D, Suit.C };

        public static int Bonus(int rank)
        {
            if (rank <= 6) return 0;
            if (rank <= 9) return 1;
            if (rank <= 12) return 3;
            if (rank == 13) return 6;
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "rank out of range");
        }

        public static int ScoreFor(int rank)
        {
            return rank + Bonus(rank);
        }

        public static int SequenceBonus(IReadOnlyList<Card> cards, int same = 2, int adjacent = 1)
        {
            var total = 0;
            for (var i = 1; i < cards.Count; i++)
            {
                var left = cards[i - 1];
                var right = cards[i];
                if (left.Suit == Suit.Joker || right.Suit == Suit.Joker) continue;
                if (left.Rank == right.Rank) total += same;
                else if (left.Rank != null && right.Rank != null && Math.Abs(left.Rank.Value - right.Rank.Value) == 1)
                    total += adjacent;
            }
            return total;
        }

        public static List<Card> MakeDeck(int numDecks = 2)
        {
            var cards = new List<Card>();
            var nextId = 0;
            for (var deck = 0; deck < numDecks; deck++)
            {
                foreach (var suit in KindSuits)
                {
                    for (var rank = 1; rank <= 13; rank++)
                        cards.Add(new Card(nextId++, suit, rank));
                }
                cards.Add(new Card(nextId++, Suit.Joker, null));
                cards.Add(new Card(nextId++, Suit.Joker, null));
            }
            return cards;
        }
    }
}

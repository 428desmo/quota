using System.Collections.Generic;
using System.Linq;

namespace Quota
{
    public static class Cpu
    {
        public static GameAction ChooseAction(Game game)
        {
            MaybeSpecial(game);
            var actions = game.LegalActions();
            var player = game.Players[game.Current];
            var takes = actions.OfType<TakeQuota>().ToList();
            if (takes.Count > 0)
            {
                takes = takes.OrderByDescending(action =>
                {
                    var card = CardInMarket(game, action.CardId);
                    var ace = card.Rank == 1 ? Cards.ScoreFor(card.Rank.Value) : 0;
                    var sweet = -Abs(card.Rank.Value - 6);
                    return (ace, sweet, -card.Rank.Value);
                }).ToList();
                var best = takes[0];
                var chosen = CardInMarket(game, best.CardId);
                if (chosen.Rank >= 11 && takes.Any(action => CardInMarket(game, action.CardId).Rank == 1))
                    return takes.First(action => CardInMarket(game, action.CardId).Rank == 1);
                return best;
            }
            if (player.Quota == null) return new Pass();
            var need = player.Quota.Rank.Value - 1 - player.Collection.Count;
            var eligible = game.Market.Where(card => card.Suit == player.Quota.Suit || card.Suit == Suit.Joker).ToList();
            var suits = eligible.Where(card => card.Suit != Suit.Joker).ToList();
            var jokers = eligible.Where(card => card.Suit == Suit.Joker).ToList();
            if (eligible.Count == 0 || need <= 0)
                return need >= 6 ? (GameAction)new Abandon() : new Pass();
            if (need >= 6 && suits.Count == 0) return new Abandon();
            var picked = suits.Concat(jokers).ToList();
            if (game.Config.SequenceRule) picked = OrderForSequence(player, picked);
            if (picked.Count > need) picked = picked.GetRange(0, need);
            return new Collect(picked.ConvertAll(card => card.Id));
        }

        static void MaybeSpecial(Game game)
        {
            if (!game.Config.SpecialActionsRule || game.Finished || game.Plan != "normal" || game.TurnGain) return;
            var player = game.Players[game.Current];
            if (player.ReshuffleTakeLeft > 0 && !MarketHelps(game, player))
            {
                game.DeclareReshuffle();
                return;
            }
            if (player.DoubleActionLeft > 0 && WorthDouble(game, player)) game.DeclareDouble();
        }

        static bool MarketHelps(Game game, Player player)
        {
            if (player.Quota == null) return game.Market.Any(card => card.Suit != Suit.Joker);
            return game.Market.Any(card => card.Suit == player.Quota.Suit || card.Suit == Suit.Joker);
        }

        static bool WorthDouble(Game game, Player player)
        {
            if (player.Quota == null)
                return game.Market.Any(card => card.Rank == 1 || (card.Rank != null && card.Rank >= 7));
            var need = player.Quota.Rank.Value - 1 - player.Collection.Count;
            var eligible = game.Market.Count(card => card.Suit == player.Quota.Suit || card.Suit == Suit.Joker);
            return need > 0 && need <= 4 && eligible > 0;
        }

        static List<Card> OrderForSequence(Player player, List<Card> cards)
        {
            int? tail = player.Quota != null ? player.Quota.Rank : null;
            if (player.Collection.Count > 0)
            {
                var last = player.Collection[player.Collection.Count - 1];
                tail = last.Suit == Suit.Joker ? (int?)null : last.Rank;
            }
            var remaining = new List<Card>(cards);
            var ordered = new List<Card>();
            while (remaining.Count > 0)
            {
                var current = tail;
                remaining = remaining.OrderByDescending(card => SequenceValue(card, current)).ToList();
                var pick = remaining[0];
                remaining.RemoveAt(0);
                ordered.Add(pick);
                tail = pick.Suit == Suit.Joker ? (int?)null : pick.Rank;
            }
            return ordered;
        }

        static int SequenceValue(Card card, int? current)
        {
            if (card.Suit == Suit.Joker || current == null || card.Rank == null) return 0;
            if (card.Rank == current) return 2;
            if (Abs(card.Rank.Value - current.Value) == 1) return 1;
            return 0;
        }

        static Card CardInMarket(Game game, int cardId)
        {
            foreach (var card in game.Market)
                if (card.Id == cardId) return card;
            return null;
        }

        static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Quota
{
    // Stable IDs shared with quota/cpu_roster.py.
    public static class CpuProfiles
    {
        public static readonly string[] Names = { "慎重なピエトロ", "柔軟なマルコ", "手際のよいルチア", "大胆なロレンツォ", "抜け目ないイザベラ", "好奇心旺盛なソフィア" };
        static readonly Dictionary<Player, ProfileMind> Minds = new Dictionary<Player, ProfileMind>();

        public static int PickFresh(Random rng, HashSet<int> used)
        {
            var available = Enumerable.Range(0, Names.Length).Where(id => !used.Contains(id)).ToList();
            if (available.Count == 0) throw new InvalidOperationException("No unused CPU");
            var id = available[rng.Next(available.Count)];
            used.Add(id);
            return id;
        }
        public static void BindInOrder(Game game, IList<int> ids, int strength)
        {
            var index = 0;
            foreach (var player in game.Players)
                if (!player.IsHuman && index < ids.Count)
                    Minds[player] = new ProfileMind(ids[index++], strength);
        }
        public static ProfileMind MindFor(Player player)
        {
            ProfileMind mind;
            return Minds.TryGetValue(player, out mind) ? mind : null;
        }
        public static void ObserveTransition(Game game, List<Card> market, int deckCount, bool reshuffled, bool newRound)
        {
            foreach (var player in game.Players)
            {
                var mind = MindFor(player);
                if (mind != null) mind.Observe(game, market, deckCount, reshuffled, newRound);
            }
        }
    }

    public sealed class ProfileMind
    {
        readonly int id;
        readonly int strength;
        readonly Dictionary<Suit, double> returned = new Dictionary<Suit, double>();
        public ProfileMind(int characterId, int inference)
        {
            id = characterId;
            strength = Math.Max(0, Math.Min(2, inference));
        }
        double Known(Suit suit) { double value; return returned.TryGetValue(suit, out value) ? value : 0; }
        public void Observe(Game game, List<Card> beforeMarket, int beforeDeck, bool reshuffled, bool newRound)
        {
            if (newRound) { returned.Clear(); return; }
            if (strength < 2) return;
            if (reshuffled)
            {
                foreach (var card in beforeMarket.Where(card => card != null))
                    returned[card.Suit] = Known(card.Suit) + 1;
                var pool = beforeDeck + beforeMarket.Count(card => card != null);
                var survival = pool > 0 ? Math.Max(0, 1.0 - game.Market.Count(card => card != null) / (double)pool) : 0;
                foreach (var suit in returned.Keys.ToList()) returned[suit] *= survival;
            }
            else if (beforeDeck > game.Deck.Count && beforeDeck > 0)
            {
                var survival = Math.Max(0, 1.0 - (beforeDeck - game.Deck.Count) / (double)beforeDeck);
                foreach (var suit in returned.Keys.ToList()) returned[suit] *= survival;
            }
        }
        List<Card> Visible(Game game)
        {
            var cards = game.Market.Where(card => card != null).Concat(game.Discard).ToList();
            foreach (var player in game.Players)
            {
                if (player.Quota != null) cards.Add(player.Quota);
                cards.AddRange(player.Collection);
                cards.AddRange(player.Achieved);
            }
            return cards;
        }
        double Expected(Game game, Suit suit)
        {
            var deck = game.Deck.Count;
            if (deck == 0) return 0;
            var total = suit == Suit.Joker ? 4 : 26;
            if (strength == 0) return deck * total / 108.0;
            var visible = Visible(game);
            if (strength == 1) return Math.Min(deck, Math.Max(0, total - visible.Count(card => card.Suit == suit)) * deck / (double)(deck + 8));
            var knownTotal = Enum.GetValues(typeof(Suit)).Cast<Suit>().Sum(item => Math.Min(deck, Known(item)));
            var unknown = Math.Max(0, total - visible.Count(card => card.Suit == suit) - Known(suit));
            var unknownTotal = Enum.GetValues(typeof(Suit)).Cast<Suit>().Sum(item =>
                Math.Max(0, (item == Suit.Joker ? 4 : 26) - visible.Count(card => card.Suit == item) - Known(item)));
            return Math.Min(deck, Known(suit)) + (unknownTotal > 0 ? Math.Max(0, deck - knownTotal) * unknown / unknownTotal : 0);
        }
        double Completion(Game game, Suit suit, int need, int excludedId = -1)
        {
            if (need <= 0) return 1;
            var market = game.Market.Count(card => card != null && card.Id != excludedId && (card.Suit == suit || card.Suit == Suit.Joker));
            if (strength > 0)
            {
                var visible = Visible(game);
                if (26 - visible.Count(card => card.Suit == suit) + 4 - visible.Count(card => card.Suit == Suit.Joker) + market < need) return 0;
            }
            var reach = game.Players.Count == 3 ? 0.32 : 0.27;
            return Math.Pow(Math.Min(1, (market + (Expected(game, suit) + Expected(game, Suit.Joker)) * reach) / need), 1.7);
        }
        double Denial(Game game, Card card)
        {
            var impact = game.Players.Where((rival, index) => index != game.Current && rival.Quota != null && rival.Quota.Suit == card.Suit)
                .Select(rival => rival.Quota.Rank.Value - 1 - rival.Collection.Count == 1 ? 2.0 : 0.5).DefaultIfEmpty(0).Max();
            return impact * (id == 4 ? 2.0 : id == 1 ? 0.4 : 0.2);
        }
        double TitleValue(Player player, Card card, double chance)
        {
            var value = 0.0;
            var kinds = player.Bundles.Select(bundle => bundle.Kind).Distinct().ToList();
            if (player.Bundles.Count > 0 && player.Bundles.Count < 3 && kinds.Count == 1 &&
                kinds[0] == card.Suit.ToString()) value += 3.0 * chance;
            if (!player.Achieved.Any(item => item.Suit == card.Suit)) value += 0.5 * chance;
            return value * (id == 5 ? 2.8 : id == 0 || id == 3 ? 0.5 : id == 1 ? 0.6 : id == 4 ? 0.4 : 0.3);
        }
        public GameAction Choose(Game game)
        {
            var player = game.Players[game.Current];
            if (game.Config.SpecialActionsRule && game.Plan == "normal" && !game.TurnGain)
            {
                var eligible = game.Market.Where(card => card != null && (player.Quota == null ? card.Suit != Suit.Joker : card.Suit == player.Quota.Suit || card.Suit == Suit.Joker)).ToList();
                if (player.ReshuffleTakeLeft > 0 && eligible.Count == 0 && game.Deck.Count > 0)
                {
                    var market = game.Market.ToList();
                    var deck = game.Deck.Count;
                    game.DeclareReshuffle();
                    CpuProfiles.ObserveTransition(game, market, deck, true, false);
                }
                else if (player.DoubleActionLeft > 0 && game.Deck.Count > 1 &&
                    (player.Quota != null && id != 0 && player.Quota.Rank.Value - 1 - player.Collection.Count > 0 &&
                     player.Quota.Rank.Value - 1 - player.Collection.Count <= eligible.Count ||
                     player.Quota == null && (id == 1 || id == 2 || id == 3) && eligible.Any(card => card.Rank == 1)))
                    game.DeclareDouble();
            }
            var takes = game.LegalActions().OfType<TakeQuota>().ToList();
            if (player.Quota == null)
            {
                if (takes.Count == 0) return new Pass();
                var lead = game.Players.Where((other, index) => index != game.Current).Max(other => game.FinalScore(other)) - game.FinalScore(player);
                var remaining = game.RoundCount - game.RoundIndex;
                var risk = new[] { 5.0, 2.0, 2.0, 1.2, 2.3, 2.4 }[id];
                var time = new[] { 0.58, 0.48, 0.90, 0.40, 0.54, 0.50 }[id];
                var upside = new[] { 0.70, 1.0, 0.85, 1.55, 0.85, 0.95 }[id];
                if (lead >= 8 && remaining <= 1) upside *= 1.4;
                if (lead <= -10 && remaining <= 1) upside *= 0.8;
                return takes.OrderByDescending(action =>
                {
                    var card = game.Market.First(item => item != null && item.Id == action.CardId);
                    var rank = card.Rank.Value;
                    var chance = Completion(game, card.Suit, rank - 1, card.Id);
                    var title = TitleValue(player, card, chance);
                    return Cards.ScoreFor(rank) * chance * upside - risk * (1 - chance) - time * rank * 0.65 + title + Denial(game, card);
                }).ThenBy(action => action.CardId).First();
            }
            var need = player.Quota.Rank.Value - 1 - player.Collection.Count;
            var cards = game.Market.Where(card => card != null && (card.Suit == player.Quota.Suit || card.Suit == Suit.Joker)).ToList();
            if (cards.Count == 0)
                return game.Plan == "normal" && !game.TurnGain && Completion(game, player.Quota.Suit, need) < (id == 3 ? 0.18 : 0.32)
                    ? (GameAction)new Abandon() : new Pass();
            var available = cards.Where(card => card.Suit != Suit.Joker).Concat(cards.Where(card => card.Suit == Suit.Joker)).ToList();
            if (id == 5 && player.Bundles.Count < 3 && !player.Bundles.Any(bundle => bundle.HasWild))
            {
                var suits = available.Count(card => card.Suit != Suit.Joker);
                if (suits > 0 && suits < need && Completion(game, player.Quota.Suit, need - suits) > 0.60)
                    available.RemoveAll(card => card.Suit == Suit.Joker);
            }
            var chosen = new List<Card>();
            var line = new List<Card>(player.Achieved) { player.Quota };
            line.AddRange(player.Collection);
            while (available.Count > 0 && chosen.Count < need)
            {
                var card = available.OrderByDescending(item => Cards.SequenceAt(line.Concat(new[] { item }).ToList(), line.Count) + Denial(game, item))
                    .ThenBy(item => item.Suit == Suit.Joker ? 1 : 0).ThenBy(item => item.Id).First();
                available.Remove(card); chosen.Add(card); line.Add(card);
            }
            return chosen.Count == 0 ? (GameAction)new Pass() : new Collect(chosen.Select(card => card.Id));
        }
    }
}

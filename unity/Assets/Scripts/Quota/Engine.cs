using System;
using System.Collections.Generic;
using System.Linq;

namespace Quota
{
    public abstract class GameAction
    {
        public abstract string Key { get; }
    }

    public sealed class TakeQuota : GameAction
    {
        public readonly int CardId;
        public TakeQuota(int cardId) { CardId = cardId; }
        public override string Key => "take:" + CardId;
    }

    public sealed class Collect : GameAction
    {
        public readonly int[] CardIds;
        public Collect(IEnumerable<int> cardIds) { CardIds = cardIds.ToArray(); }
        public override string Key => "collect:" + string.Join(",", CardIds);
    }

    public sealed class Abandon : GameAction
    {
        public override string Key => "abandon";
    }

    public sealed class Pass : GameAction
    {
        public override string Key => "pass";
    }

    public sealed class Bundle
    {
        public readonly string Kind;
        public readonly bool HasWild;
        public Bundle(string kind, bool hasWild)
        {
            Kind = kind;
            HasWild = hasWild;
        }
    }

    public sealed class Player
    {
        public readonly string Name;
        public Card Quota;
        public readonly List<Card> Collection = new List<Card>();
        public readonly List<Card> Achieved = new List<Card>();
        public int Score;
        public int AchieveCount;
        public int MaxSingleScore;
        public readonly bool IsHuman;
        public readonly List<Bundle> Bundles = new List<Bundle>();
        public int ReshuffleTakeLeft;
        public int DoubleActionLeft;

        public Player(string name, bool isHuman)
        {
            Name = name;
            IsHuman = isHuman;
        }
    }

    public sealed class GameConfig
    {
        public int NumPlayers = 3;
        public int NumDecks = 2;
        public int? MarketSize;
        public int RemovedCount = 8;
        public int? StallThreshold;
        public int StallEndCount = 2;
        public int? Seed;
        public List<string> Names;
        public List<int> HumanSeats;
        public bool SequenceRule;
        public bool TitleRule;
        public int TitleMinAchieves = 3;
        public int TitleMonoBonus = 15;
        public int TitlePuristBonus = 5;
        public bool SpecialActionsRule;
        public int ReshuffleTakeUses = 1;
        public int DoubleActionUses = 1;
        public string ItemSet = "trade";

        public int ResolvedMarketSize()
        {
            if (MarketSize != null) return MarketSize.Value;
            return NumPlayers == 4 ? 6 : 7;
        }

        public int ResolvedStallThreshold()
        {
            if (StallThreshold != null) return StallThreshold.Value;
            return NumPlayers;
        }
    }

    public sealed class Game
    {
        public readonly GameConfig Config;
        public readonly List<Card> Deck;
        public readonly List<Card> Removed;
        public readonly List<Card> Market;
        public readonly List<Card> Discard = new List<Card>();
        public readonly List<Player> Players;
        public int Current;
        public int NoGainStreak;
        public bool StallFlag;
        public bool Finished;
        public string EndReason;
        public int TurnNumber;
        public readonly List<string> Log = new List<string>();
        public readonly PythonRandom Rng;
        public int ReshuffleCount;
        public bool TurnGain;
        public string Plan = "normal";
        public int DoubleStage;
        public bool DoubleGained;

        Game(GameConfig config, List<Card> deck, List<Card> removed, List<Card> market, List<Player> players, int current, PythonRandom rng)
        {
            Config = config;
            Deck = deck;
            Removed = removed;
            Market = market;
            Players = players;
            Current = current;
            Rng = rng;
            TurnNumber = 1;
        }

        public static Game Start(GameConfig config = null)
        {
            var cfg = config ?? new GameConfig();
            if (cfg.NumPlayers < 2) throw new ArgumentException("num_players must be at least 2");
            PythonRandom rng;
            if (cfg.Seed == null) rng = new PythonRandom(Environment.TickCount);
            else rng = new PythonRandom(cfg.Seed.Value);
            var deck = Cards.MakeDeck(cfg.NumDecks);
            rng.Shuffle(deck);
            var removedCount = cfg.RemovedCount;
            var marketSize = cfg.ResolvedMarketSize();
            if (deck.Count < removedCount + marketSize)
                throw new ArgumentException("deck is smaller than removed cards plus market");
            var removed = PopMany(deck, removedCount);
            var market = PopMany(deck, marketSize);
            var names = cfg.Names ?? new List<string>();
            if (cfg.Names == null)
            {
                for (var i = 0; i < cfg.NumPlayers; i++) names.Add($"P{i + 1}");
            }
            if (names.Count != cfg.NumPlayers) throw new ArgumentException("names length must match num_players");
            var human = new HashSet<int>(cfg.HumanSeats ?? Enumerable.Range(0, cfg.NumPlayers));
            var takeLeft = cfg.SpecialActionsRule ? cfg.ReshuffleTakeUses : 0;
            var doubleLeft = cfg.SpecialActionsRule ? cfg.DoubleActionUses : 0;
            var players = new List<Player>();
            for (var i = 0; i < cfg.NumPlayers; i++)
            {
                var player = new Player(names[i], human.Contains(i));
                player.ReshuffleTakeLeft = takeLeft;
                player.DoubleActionLeft = doubleLeft;
                players.Add(player);
            }
            var first = rng.RandBelow(cfg.NumPlayers);
            var game = new Game(cfg, deck, removed, market, players, first, rng);
            game.Log.Add($"先手: {players[first].Name}");
            return game;
        }

        public int MarketSize()
        {
            return Config.ResolvedMarketSize();
        }

        public List<GameAction> LegalActions(int? seat = null)
        {
            var player = Players[seat ?? Current];
            var actions = new List<GameAction>();
            if (player.Quota == null)
            {
                foreach (var card in Market)
                    if (card.Suit != Suit.Joker) actions.Add(new TakeQuota(card.Id));
                actions.Add(new Pass());
                return actions;
            }
            var eligible = new List<int>();
            foreach (var card in Market)
                if (card.Suit == player.Quota.Suit || card.Suit == Suit.Joker) eligible.Add(card.Id);
            var need = player.Quota.Rank.Value - 1 - player.Collection.Count;
            for (var size = 1; size <= need; size++)
            {
                foreach (var ids in Combinations(eligible, size))
                    actions.Add(new Collect(ids));
            }
            actions.Add(new Abandon());
            actions.Add(new Pass());
            return actions;
        }

        public void Step(GameAction action)
        {
            if (Finished) throw new InvalidOperationException("game is already finished");
            if (!IsLegal(action)) throw new ArgumentException($"illegal action: {action}");
            var player = Players[Current];
            var gained = false;
            if (action is TakeQuota take)
            {
                var card = TakeMarket(take.CardId);
                gained = true;
                if (card.Rank == 1)
                {
                    Achieve(player, new List<Card> { card }, 1);
                    Log.Add($"{player.Name} が {card.Label()} をノルマ札にし、即達成（1点）");
                }
                else
                {
                    player.Quota = card;
                    player.Collection.Clear();
                    Log.Add($"{player.Name} が {card.Label()} をノルマ札にした（{card.Rank}枚、{Cards.ScoreFor(card.Rank.Value)}点）");
                }
            }
            else if (action is Collect collect)
            {
                var taken = new List<Card>();
                foreach (var cardId in collect.CardIds) taken.Add(TakeMarket(cardId));
                gained = true;
                player.Collection.AddRange(taken);
                Log.Add($"{player.Name} が {string.Join("、", taken.ConvertAll(card => card.Label()))} を収集");
                TurnGain = true;
                if (1 + player.Collection.Count == player.Quota.Rank)
                {
                    var rank = player.Quota.Rank.Value;
                    var cards = new List<Card> { player.Quota };
                    cards.AddRange(player.Collection);
                    Achieve(player, cards, rank);
                    player.Quota = null;
                    player.Collection.Clear();
                    Log.Add($"{player.Name} がノルマ達成（{Cards.ScoreFor(rank)}点）");
                }
                else if (player.Collection.Count > 0 && !CanCollectMore(player))
                {
                    Log.Add($"{player.Name} は取れる札を取り切った");
                }
                else
                {
                    return;
                }
            }
            else if (action is Abandon)
            {
                Discard.Add(player.Quota);
                Discard.AddRange(player.Collection);
                Log.Add($"{player.Name} がノルマを放棄した");
                player.Quota = null;
                player.Collection.Clear();
            }
            else if (action is Pass)
            {
                Log.Add($"{player.Name} はパス");
            }
            else
            {
                throw new ArgumentException("unknown action");
            }

            CloseAction(gained);
        }

        public void DeclareReshuffle()
        {
            var player = Players[Current];
            Declare("reshuffle");
            SelfReshuffle();
            Log.Add($"{player.Name} が配り直し＆取得を宣言し、場を配り直した");
        }

        public void DeclareDouble()
        {
            var player = Players[Current];
            Declare("double");
            DoubleStage = 1;
            DoubleGained = false;
            Log.Add($"{player.Name} がダブルアクションを宣言した");
        }

        public void CancelDouble()
        {
            if (Finished) throw new InvalidOperationException("game is already finished");
            if (Plan != "double" || DoubleStage != 1 || TurnGain)
                throw new ArgumentException("ダブルアクションは取り消せません");
            var player = Players[Current];
            player.DoubleActionLeft += 1;
            Plan = "normal";
            DoubleStage = 0;
            DoubleGained = false;
            Log.Add($"{player.Name} がダブルアクションの宣言を取り消した");
        }

        void Declare(string kind)
        {
            if (Finished) throw new InvalidOperationException("game is already finished");
            if (!Config.SpecialActionsRule) throw new ArgumentException("特殊アクションは採用されていません");
            if (Plan != "normal" || TurnGain) throw new ArgumentException("この手番では特殊アクションを宣言できません");
            var player = Players[Current];
            if (kind == "reshuffle")
            {
                if (player.ReshuffleTakeLeft < 1) throw new ArgumentException("配り直し＆取得は使い切っています");
                player.ReshuffleTakeLeft -= 1;
                Plan = "reshuffle";
                return;
            }
            if (kind == "double")
            {
                if (player.DoubleActionLeft < 1) throw new ArgumentException("ダブルアクションは使い切っています");
                player.DoubleActionLeft -= 1;
                Plan = "double";
                return;
            }
            throw new ArgumentException(kind);
        }

        void SelfReshuffle()
        {
            Deck.AddRange(Market);
            Market.Clear();
            Rng.Shuffle(Deck);
            Market.AddRange(PopMany(Deck, MarketSize()));
        }

        void CloseAction(bool gained)
        {
            gained = gained || TurnGain;
            if (Plan == "double" && DoubleStage == 1)
            {
                DoubleGained = gained;
                TurnGain = false;
                if (!BeginTurn()) return;
                DoubleStage = 2;
                return;
            }
            EndTurn(gained || DoubleGained);
        }

        void EndTurn(bool gained)
        {
            if (gained)
            {
                NoGainStreak = 0;
                StallFlag = false;
            }
            else
            {
                NoGainStreak++;
                if (NoGainStreak == Config.ResolvedStallThreshold())
                {
                    if (StallFlag)
                    {
                        Finished = true;
                        EndReason = "STALL";
                        Log.Add("膠着の連続");
                        return;
                    }
                    SelfReshuffle();
                    NoGainStreak = 0;
                    StallFlag = true;
                    ReshuffleCount++;
                    Log.Add("場を配り直した");
                }
            }

            TurnGain = false;
            Plan = "normal";
            DoubleStage = 0;
            DoubleGained = false;
            Current = (Current + 1) % Players.Count;
            TurnNumber++;
            BeginTurn();
        }

        public bool BeginTurn()
        {
            while (Market.Count < MarketSize())
            {
                if (Deck.Count == 0)
                {
                    Finished = true;
                    EndReason = "DECK";
                    Log.Add("山札切れ");
                    return false;
                }
                Market.Add(Pop(Deck));
            }
            return true;
        }

        public List<List<int>> Ranking()
        {
            var seats = Enumerable.Range(0, Players.Count)
                .OrderByDescending(i => FinalScore(Players[i]))
                .ThenByDescending(i => Players[i].AchieveCount)
                .ThenByDescending(i => Players[i].MaxSingleScore)
                .ToList();
            var groups = new List<List<int>>();
            foreach (var seat in seats)
            {
                if (groups.Count == 0 || !Tied(groups[groups.Count - 1][0], seat))
                    groups.Add(new List<int>());
                groups[groups.Count - 1].Add(seat);
            }
            return groups;
        }

        public List<int> AllCardIds()
        {
            var ids = new List<int>();
            ids.AddRange(Deck.ConvertAll(card => card.Id));
            ids.AddRange(Removed.ConvertAll(card => card.Id));
            ids.AddRange(Market.ConvertAll(card => card.Id));
            ids.AddRange(Discard.ConvertAll(card => card.Id));
            foreach (var player in Players)
            {
                if (player.Quota != null) ids.Add(player.Quota.Id);
                ids.AddRange(player.Collection.ConvertAll(card => card.Id));
                ids.AddRange(player.Achieved.ConvertAll(card => card.Id));
            }
            return ids;
        }

        public int FinalScore(Player player)
        {
            return player.Score + SequencePoints(player) + TitlePoints(player);
        }

        public List<(string name, int points)> TitleAwards(Player player)
        {
            var awards = new List<(string, int)>();
            if (!Config.TitleRule) return awards;
            if (player.Bundles.Count < Config.TitleMinAchieves) return awards;
            var kinds = new HashSet<string>();
            var wild = false;
            foreach (var bundle in player.Bundles)
            {
                kinds.Add(bundle.Kind);
                if (bundle.HasWild) wild = true;
            }
            if (kinds.Count == 1) awards.Add(("単色達成", Config.TitleMonoBonus));
            if (!wild) awards.Add(("生粋の買い付け", Config.TitlePuristBonus));
            return awards;
        }

        public int TitlePoints(Player player)
        {
            var total = 0;
            foreach (var award in TitleAwards(player)) total += award.points;
            return total;
        }

        public int SequencePoints(Player player)
        {
            if (!Config.SequenceRule) return 0;
            return Cards.SequenceBonus(player.Achieved);
        }

        public bool IsLegal(GameAction action)
        {
            if (action is Collect collect)
            {
                var player = Players[Current];
                if (player.Quota == null || player.Quota.Rank == null) return false;
                var ids = collect.CardIds;
                if (ids.Length < 1 || ids.Distinct().Count() != ids.Length) return false;
                var need = player.Quota.Rank.Value - 1 - player.Collection.Count;
                if (ids.Length > need) return false;
                var eligible = new HashSet<int>();
                foreach (var card in Market)
                    if (card.Suit == player.Quota.Suit || card.Suit == Suit.Joker) eligible.Add(card.Id);
                foreach (var id in ids)
                    if (!eligible.Contains(id)) return false;
                return true;
            }
            var key = action.Key;
            foreach (var item in LegalActions())
                if (item.Key == key) return true;
            return false;
        }

        bool CanCollectMore(Player player)
        {
            if (player.Quota.Rank.Value - 1 - player.Collection.Count <= 0) return false;
            foreach (var card in Market)
                if (card.Suit == player.Quota.Suit || card.Suit == Suit.Joker) return true;
            return false;
        }

        Card TakeMarket(int cardId)
        {
            for (var i = 0; i < Market.Count; i++)
            {
                if (Market[i].Id != cardId) continue;
                var card = Market[i];
                Market.RemoveAt(i);
                return card;
            }
            throw new ArgumentException($"card {cardId} is not in the market");
        }

        void Achieve(Player player, List<Card> cards, int rank)
        {
            player.Achieved.AddRange(cards);
            var wild = false;
            foreach (var card in cards)
                if (card.Suit == Suit.Joker) wild = true;
            player.Bundles.Add(new Bundle(cards[0].Suit.ToString() == "Joker" ? "JOKER" : cards[0].Suit.ToString(), wild));
            var points = cards.Count + Cards.Bonus(rank);
            player.Score += points;
            player.AchieveCount++;
            if (points > player.MaxSingleScore) player.MaxSingleScore = points;
        }

        bool Tied(int a, int b)
        {
            var left = Players[a];
            var right = Players[b];
            return FinalScore(left) == FinalScore(right)
                && left.AchieveCount == right.AchieveCount
                && left.MaxSingleScore == right.MaxSingleScore;
        }

        static List<Card> PopMany(List<Card> deck, int count)
        {
            var taken = new List<Card>(count);
            for (var i = 0; i < count; i++) taken.Add(Pop(deck));
            return taken;
        }

        static Card Pop(List<Card> deck)
        {
            var card = deck[deck.Count - 1];
            deck.RemoveAt(deck.Count - 1);
            return card;
        }

        static IEnumerable<int[]> Combinations(IReadOnlyList<int> items, int size)
        {
            if (size <= 0 || size > items.Count) yield break;
            var index = new int[size];
            for (var i = 0; i < size; i++) index[i] = i;
            while (true)
            {
                var pick = new int[size];
                for (var i = 0; i < size; i++) pick[i] = items[index[i]];
                yield return pick;
                var cursor = size - 1;
                while (cursor >= 0 && index[cursor] == items.Count - size + cursor) cursor--;
                if (cursor < 0) yield break;
                index[cursor]++;
                for (var i = cursor + 1; i < size; i++) index[i] = index[i - 1] + 1;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace Quota
{
    public sealed class CpuMind
    {
        public readonly int CharacterId;
        public bool Switched;
        public bool Abandoned;

        public CpuMind(int characterId)
        {
            CharacterId = characterId;
        }

        public GameAction Choose(Game game)
        {
            Characters.Decode(CharacterId, out var before, out var trigger, out var after, out var stance);
            if (before != after && !Switched && Characters.Trigger(trigger, game, this)) Switched = true;
            var strategy = Switched ? after : before;
            var action = Characters.ChooseFor(game, strategy, stance);
            if (action is Abandon) Abandoned = true;
            return action;
        }
    }

    public static class Characters
    {
        public const int StrategyCount = 10;
        public const int TriggerCount = 6;
        public const int TitleStanceCount = 3;
        public const double ConsistentChance = 2.0 / 3.0;

        static readonly string[] Adjectives =
        {
            "放浪する", "ご機嫌な", "心配性の", "まぶしい", "午後の", "逆さまの", "古びた", "遠回りな", "ひなたの", "夜更かしの",
            "まるい", "斜めの", "潮風の", "まばゆい", "陽気な", "ひんやりした", "とろける", "ささやく", "まどろむ", "きらめく",
            "風向きの", "忘れ物の", "とけない", "うたたねする", "こっそりした", "そわそわした", "のんびりした", "くすぐったい", "まばたきする", "星を見る",
            "朝焼けの", "雨上がりの", "ひょっこりの", "よれよれの", "ほっこりの", "さらさらの", "ぽかぽかの", "ねむたげな", "ひらひらの", "ゆらゆらの",
            "ほのぼのした", "ざわざわした", "てかてかの", "ふわふわの", "きょとんとした",
        };

        static readonly string[] Nouns =
        {
            "ロボット", "冷蔵庫", "秋刀魚", "急須", "気球", "鉛筆", "灯台", "饅頭", "鍵盤", "帆船",
            "温度計", "風鈴", "地球儀", "金魚", "ラジオ", "梯子", "石鹸", "蒲鉾", "湯のみ", "靴べら",
            "郵便箱", "蝶番", "時計塔", "雲",
            "団扇", "提灯", "箒", "算盤", "徳利", "煙突", "車輪", "看板", "植木鉢",
            "やかん", "下駄", "火鉢", "行灯", "扇子", "硯", "竹籠", "手鏡", "布団",
        };

        const int NameStep = 409;

        static readonly Dictionary<Player, CpuMind> Minds = new Dictionary<Player, CpuMind>();

        class TakeNote
        {
            public int Seat;
            public int Turn = -1;
            public Suit? Suit;
            public bool HasLast;
            public int LastSeat;
            public Suit? LastSuit;
        }

        static readonly Dictionary<Game, TakeNote> Takes = new Dictionary<Game, TakeNote>();

        public static int Count => StrategyCount * TriggerCount * StrategyCount * TitleStanceCount;

        public static int Encode(int before, int trigger, int after, int stance = 0)
        {
            var basis = (before * TriggerCount + trigger) * StrategyCount + after;
            return basis * TitleStanceCount + stance;
        }

        public static void Decode(int characterId, out int before, out int trigger, out int after, out int stance)
        {
            stance = characterId % TitleStanceCount;
            var rest = characterId / TitleStanceCount;
            after = rest % StrategyCount;
            rest /= StrategyCount;
            trigger = rest % TriggerCount;
            before = rest / TriggerCount;
        }

        public static string NameOf(int characterId)
        {
            var space = Adjectives.Length * Nouns.Length;
            var index = (characterId * NameStep) % space;
            if (index < 0) index += space;
            return Adjectives[index / Nouns.Length] + Nouns[index % Nouns.Length];
        }

        public static int Pick(Random rng)
        {
            var stance = rng.Next(TitleStanceCount);
            if (rng.NextDouble() < ConsistentChance)
            {
                var strategy = rng.Next(StrategyCount);
                return Encode(strategy, rng.Next(TriggerCount), strategy, stance);
            }
            var before = rng.Next(StrategyCount);
            var after = rng.Next(StrategyCount - 1);
            if (after >= before) after += 1;
            return Encode(before, rng.Next(TriggerCount), after, stance);
        }

        public static int PickFresh(Random rng, HashSet<int> used)
        {
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var characterId = Pick(rng);
                if (used.Add(characterId)) return characterId;
            }
            var fallback = Pick(rng);
            used.Add(fallback);
            return fallback;
        }

        public static void BindInOrder(Game game, IList<int> characterIds)
        {
            var index = 0;
            foreach (var player in game.Players)
            {
                if (player.IsHuman) continue;
                if (index >= characterIds.Count) break;
                Minds[player] = new CpuMind(characterIds[index]);
                index++;
            }
        }

        public static CpuMind MindFor(Player player)
        {
            CpuMind mind;
            return Minds.TryGetValue(player, out mind) ? mind : null;
        }

        public static void Observe(Game game, GameAction action)
        {
            Suit? took = null;
            var take = action as TakeQuota;
            if (take != null)
            {
                foreach (var card in game.Market)
                {
                    if (card != null && card.Id == take.CardId)
                    {
                        took = card.Suit;
                        break;
                    }
                }
            }
            var seat = game.Current;
            var turn = game.TurnNumber;
            TakeNote note;
            if (!Takes.TryGetValue(game, out note))
            {
                note = new TakeNote();
                Takes[game] = note;
            }
            if (note.Seat != seat || note.Turn != turn)
            {
                note.Seat = seat;
                note.Turn = turn;
                note.Suit = null;
            }
            if (took != null) note.Suit = took;
        }

        public static void Forget(Game game)
        {
            Takes.Remove(game);
        }

        public static void CommitIfTurnEnded(Game game, int seat, int turn)
        {
            TakeNote note;
            if (!Takes.TryGetValue(game, out note)) return;
            if (note.Seat != seat || note.Turn != turn) return;
            if (game.Current != seat || game.TurnNumber != turn || game.Finished)
            {
                note.HasLast = true;
                note.LastSeat = seat;
                note.LastSuit = note.Suit;
            }
        }

        public static GameAction ChooseFor(Game game, int strategy, int stance)
        {
            if (TitleChase(game, stance))
            {
                var titleAction = TitleOverride(game, strategy);
                if (titleAction != null) return titleAction;
            }
            var action = strategy == 9 ? Cpu.ChooseStock(game) : ChooseStyled(game, strategy);
            if (TitleChase(game, stance) && action is Collect) return WithoutWilds(game, (Collect)action);
            return action;
        }

        public static GameAction ChooseStyled(Game game, int strategy)
        {
            if (WantsAbandon(game, strategy)) return new Abandon();
            Cpu.ConsiderSpecials(game);
            var player = game.Players[game.Current];
            if (player.Quota == null) return Take(game, strategy);
            return Collect(game);
        }

        static GameAction Take(Game game, int strategy)
        {
            var cards = new List<Card>();
            foreach (var card in game.Market)
                if (card != null && card.Suit != Suit.Joker && card.Rank != null) cards.Add(card);
            if (cards.Count == 0) return new Pass();
            return new TakeQuota(PickCard(strategy, cards).Id);
        }

        static GameAction Collect(Game game)
        {
            var player = game.Players[game.Current];
            if (player.Quota == null || player.Quota.Rank == null) return new Pass();
            var need = player.Quota.Rank.Value - 1 - player.Collection.Count;
            if (need <= 0) return new Pass();
            var suits = new List<Card>();
            var jokers = new List<Card>();
            foreach (var card in game.Market)
            {
                if (card == null) continue;
                if (card.Suit == player.Quota.Suit) suits.Add(card);
                else if (card.Suit == Suit.Joker) jokers.Add(card);
            }
            if (suits.Count == 0 && jokers.Count == 0) return new Pass();
            suits.AddRange(jokers);
            var chosen = game.Config.SequenceRule ? Cpu.OrderForSequence(player, suits) : suits;
            if (chosen.Count > need) chosen = chosen.GetRange(0, need);
            return new Collect(chosen.ConvertAll(card => card.Id));
        }

        static Card PickCard(int strategy, List<Card> cards)
        {
            if (strategy == 5 || strategy == 6) return PickBig(cards);
            if (strategy == 7) return PickSmall(cards);
            if (strategy == 8) return PickAce(cards);
            return PickSix(cards);
        }

        static Card PickSix(List<Card> cards)
        {
            var best = cards[0];
            foreach (var card in cards)
                if (RankDistance(card, 6) < RankDistance(best, 6) || (RankDistance(card, 6) == RankDistance(best, 6) && card.Rank > best.Rank))
                    best = card;
            if (best.Rank >= 11)
                foreach (var card in cards)
                    if (card.Rank == 1) return card;
            return best;
        }

        static Card PickBig(List<Card> cards)
        {
            Card band = null;
            Card huge = null;
            foreach (var card in cards)
            {
                if (card.Rank >= 7 && card.Rank <= 10 && (band == null || card.Rank > band.Rank)) band = card;
                if (card.Rank >= 11 && (huge == null || card.Rank < huge.Rank)) huge = card;
            }
            if (band != null) return band;
            if (huge != null) return huge;
            return PickSix(cards);
        }

        static Card PickSmall(List<Card> cards)
        {
            var best = cards[0];
            foreach (var card in cards)
                if (SmallKey(card) < SmallKey(best)) best = card;
            return best;
        }

        static int SmallKey(Card card)
        {
            var rank = card.Rank.Value;
            if (rank >= 3 && rank <= 5) return rank == 4 ? 0 : 1 + Math.Abs(rank - 4);
            if (rank == 1) return 100;
            return 50 + Math.Abs(rank - 4);
        }

        static Card PickAce(List<Card> cards)
        {
            foreach (var card in cards)
                if (card.Rank == 1) return card;
            var best = cards[0];
            foreach (var card in cards)
                if (card.Rank < best.Rank) best = card;
            return best;
        }

        static int RankDistance(Card card, int target)
        {
            return Math.Abs(card.Rank.Value - target);
        }

        static bool WantsAbandon(Game game, int strategy)
        {
            var player = game.Players[game.Current];
            if (player.Quota == null || game.TurnGain || game.Plan != "normal" || game.DoubleStage != 0) return false;
            if (strategy == 1) return KeepOutlook(game) < 1;
            if (strategy == 2) return PreviousClash(game);
            if (strategy == 3) return SwapReady(game);
            if (strategy == 4) return KeepOutlook(game) < 1 || PreviousClash(game) || SwapReady(game);
            if (strategy == 6) return BigClash(game);
            if (strategy == 7) return SmallEscape(game);
            return false;
        }

        static bool TitleChase(Game game, int stance)
        {
            if (stance == 0 || !game.Config.TitleRule) return false;
            if (stance == 2) return true;
            var mine = game.FinalScore(game.Players[game.Current]);
            var best = int.MinValue;
            for (var i = 0; i < game.Players.Count; i++)
            {
                if (i == game.Current) continue;
                var score = game.FinalScore(game.Players[i]);
                if (score > best) best = score;
            }
            var gap = best - mine;
            return gap > 0 && TitlePotential(game, game.Players[game.Current]) >= gap;
        }

        static int TitlePotential(Game game, Player player)
        {
            var kinds = new HashSet<string>();
            var wild = false;
            foreach (var bundle in player.Bundles)
            {
                kinds.Add(bundle.Kind);
                if (bundle.HasWild) wild = true;
            }
            var earned = player.Bundles.Count >= game.Config.TitleMinAchieves;
            var points = 0;
            if (!(earned && kinds.Count == 1) && kinds.Count <= 1) points += game.Config.TitleMonoBonus;
            if (!(earned && !wild) && !wild) points += game.Config.TitlePuristBonus;
            return points;
        }

        static string MonoKind(Player player)
        {
            string kind = null;
            foreach (var bundle in player.Bundles)
            {
                if (kind == null) kind = bundle.Kind;
                else if (kind != bundle.Kind) return null;
            }
            if (player.Bundles.Count == 0) return null;
            return kind;
        }

        static GameAction TitleOverride(Game game, int strategy)
        {
            var player = game.Players[game.Current];
            var kind = MonoKind(player);
            if (kind == null) return null;
            var same = new List<Card>();
            foreach (var card in game.Market)
                if (card != null && card.Rank != null && card.Suit.ToString() == kind) same.Add(card);
            if (player.Quota != null)
            {
                if (player.Quota.Suit.ToString() == kind || player.Collection.Count > 0 || game.TurnGain || game.Plan != "normal" || game.DoubleStage != 0)
                    return null;
                return same.Count > 0 ? (GameAction)new Abandon() : null;
            }
            if (same.Count == 0) return null;
            var picked = strategy == 9 ? StockPick(same) : PickCard(strategy, same);
            return new TakeQuota(picked.Id);
        }

        static Card StockPick(List<Card> cards)
        {
            var best = cards[0];
            foreach (var card in cards)
            {
                if (StockKey(card).CompareTo(StockKey(best)) > 0) best = card;
            }
            if (best.Rank >= 11)
                foreach (var card in cards)
                    if (card.Rank == 1) return card;
            return best;
        }

        static (int ace, int sweet, int rank) StockKey(Card card)
        {
            var ace = card.Rank == 1 ? Cards.ScoreFor(card.Rank.Value) : 0;
            return (ace, -Math.Abs(card.Rank.Value - 6), -card.Rank.Value);
        }

        static GameAction WithoutWilds(Game game, Collect action)
        {
            var player = game.Players[game.Current];
            foreach (var bundle in player.Bundles)
                if (bundle.HasWild) return action;
            var kept = new List<int>();
            foreach (var cardId in action.CardIds)
            {
                foreach (var card in game.Market)
                {
                    if (card == null || card.Id != cardId) continue;
                    if (card.Suit != Suit.Joker) kept.Add(cardId);
                    break;
                }
            }
            if (kept.Count > 0) return new Collect(kept);
            return new Pass();
        }

        public static bool Trigger(int trigger, Game game, CpuMind mind)
        {
            if (trigger == 0) return Behind(game);
            if (trigger == 1) return SuitShared(game);
            if (trigger == 2) return mind.Abandoned;
            if (trigger == 3) return game.Deck.Count <= 36;
            if (trigger == 4) return OpponentLarge(game);
            if (trigger == 5) return game.TurnNumber >= 18;
            return false;
        }

        static bool Behind(Game game)
        {
            var mine = game.FinalScore(game.Players[game.Current]);
            var best = int.MinValue;
            for (var i = 0; i < game.Players.Count; i++)
            {
                if (i == game.Current) continue;
                var score = game.FinalScore(game.Players[i]);
                if (score > best) best = score;
            }
            return best >= mine + 8;
        }

        static bool SuitShared(Game game)
        {
            var me = game.Players[game.Current];
            if (me.Quota == null) return false;
            foreach (var player in game.Players)
                if (player != me && player.Quota != null && player.Quota.Suit == me.Quota.Suit) return true;
            return false;
        }

        static bool OpponentLarge(Game game)
        {
            for (var i = 0; i < game.Players.Count; i++)
            {
                if (i == game.Current) continue;
                if (LargeBundle(game.Players[i])) return true;
            }
            return false;
        }

        static bool LargeBundle(Player player)
        {
            var index = 0;
            while (index < player.Achieved.Count)
            {
                var rank = player.Achieved[index].Rank;
                if (rank == null) break;
                if (rank >= 7) return true;
                index += rank.Value;
            }
            return false;
        }

        static double KeepOutlook(Game game)
        {
            var player = game.Players[game.Current];
            if (player.Quota == null || player.Quota.Rank == null) return 0;
            var rank = player.Quota.Rank.Value;
            var need = rank - 1 - player.Collection.Count;
            if (need <= 0) return Cards.ScoreFor(rank);
            var marketSame = 0;
            var marketJokers = 0;
            foreach (var card in game.Market)
            {
                if (card == null) continue;
                if (card.Suit == player.Quota.Suit) marketSame++;
                else if (card.Suit == Suit.Joker) marketJokers++;
            }
            var left = need - Math.Min(need, marketSame + marketJokers);
            if (left <= 0) return Cards.ScoreFor(rank);
            var suits = new Dictionary<Suit, int>
            {
                { Suit.S, 0 }, { Suit.H, 0 }, { Suit.D, 0 }, { Suit.C, 0 },
            };
            var jokers = 0;
            void Add(Card card)
            {
                if (card == null) return;
                if (card.Suit == Suit.Joker) jokers++;
                else suits[card.Suit] = suits[card.Suit] + 1;
            }
            foreach (var card in game.Market) Add(card);
            foreach (var card in game.Discard) Add(card);
            foreach (var owner in game.Players)
            {
                Add(owner.Quota);
                foreach (var card in owner.Collection) Add(card);
                foreach (var card in owner.Achieved) Add(card);
            }
            var hiddenSuits = 26 - suits[player.Quota.Suit];
            var hiddenJokers = 4 - jokers;
            if (hiddenSuits + hiddenJokers < left) return 0;
            var future = (hiddenSuits + hiddenJokers) * (double)game.Deck.Count / Math.Max(game.Deck.Count + 8, 1);
            if (future < left) return Cards.ScoreFor(rank) * 0.05;
            var chance = Math.Min(1.0, Math.Pow(future / (left * 3.0), 1.4));
            return Cards.ScoreFor(rank) * chance;
        }

        static bool PreviousClash(Game game)
        {
            var me = game.Players[game.Current];
            if (me.Quota == null || me.Quota.Rank == null) return false;
            var gathered = 1 + me.Collection.Count;
            if (me.Quota.Rank.Value - gathered < 5 || gathered * 2 > me.Quota.Rank.Value) return false;
            TakeNote note;
            if (!Takes.TryGetValue(game, out note) || !note.HasLast) return false;
            var prev = game.PreviousSeat();
            if (note.LastSeat != prev || note.LastSuit != me.Quota.Suit) return false;
            var other = game.Players[prev];
            if (other.Quota == null || other.Quota.Rank == null || other.Quota.Suit != me.Quota.Suit) return false;
            return other.Quota.Rank.Value - 1 - other.Collection.Count >= 3;
        }

        static bool SwapReady(Game game)
        {
            var me = game.Players[game.Current];
            if (me.Quota == null || me.Quota.Rank == null || me.Collection.Count > 0) return false;
            var rank = me.Quota.Rank.Value;
            Card best = null;
            var ace = false;
            foreach (var card in game.Market)
            {
                if (card == null || card.Suit == Suit.Joker || card.Suit == me.Quota.Suit || card.Rank == null) continue;
                if (card.Rank == 1) ace = true;
                if (best == null || RankDistance(card, 6) < RankDistance(best, 6) || (RankDistance(card, 6) == RankDistance(best, 6) && card.Rank > best.Rank))
                    best = card;
            }
            if (best == null) return false;
            if (rank >= 11 && ace) return true;
            return Math.Abs(rank - 6) - RankDistance(best, 6) >= 2;
        }

        static bool BigClash(Game game)
        {
            var me = game.Players[game.Current];
            if (me.Quota == null || me.Quota.Rank == null || me.Quota.Rank < 7 || me.Collection.Count > 2) return false;
            foreach (var player in game.Players)
            {
                if (player == me || player.Quota == null || player.Quota.Rank == null) continue;
                if (player.Quota.Suit == me.Quota.Suit && player.Quota.Rank >= 7 && player.Collection.Count <= 2) return true;
            }
            return false;
        }

        static bool SmallEscape(Game game)
        {
            var me = game.Players[game.Current];
            if (me.Quota == null || me.Quota.Rank == null || me.Quota.Rank < 8 || me.Collection.Count > 0) return false;
            foreach (var card in game.Market)
                if (card != null && card.Suit != Suit.Joker && card.Suit != me.Quota.Suit && card.Rank != null && card.Rank <= 5)
                    return true;
            return false;
        }
    }
}

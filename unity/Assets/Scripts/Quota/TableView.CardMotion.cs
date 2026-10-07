using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public sealed partial class TableView
    {
        // Presentation snapshots never change the authoritative game or its action log.
        sealed class BoardFrame
        {
            public List<Card> Market;
            public Player[] Players;
            public int Deck;
            public int Round;
            public int Actor;
            public int Shuffles;
            public static BoardFrame Capture(Game game)
            {
                var state = new BoardFrame { Market = new List<Card>(game.Market), Deck = game.Deck.Count,
                    Round = game.RoundIndex, Actor = game.Current, Shuffles = game.ReshuffleCount, Players = new Player[game.Players.Count] };
                for (var i = 0; i < state.Players.Length; i++) state.Players[i] = CopyPlayer(game.Players[i]);
                return state;
            }
            public BoardFrame Copy()
            {
                var copy = new BoardFrame { Market = new List<Card>(Market), Deck = Deck, Round = Round, Actor = Actor, Shuffles = Shuffles, Players = new Player[Players.Length] };
                for (var i = 0; i < Players.Length; i++) copy.Players[i] = CopyPlayer(Players[i]);
                return copy;
            }
        }
        static Player CopyPlayer(Player p)
        {
            var copy = new Player(p.Name, p.IsHuman) { Quota = p.Quota, Score = p.Score, AchieveCount = p.AchieveCount,
                MaxSingleScore = p.MaxSingleScore, DoubleActionLeft = p.DoubleActionLeft, ReshuffleTakeLeft = p.ReshuffleTakeLeft };
            copy.Collection.AddRange(p.Collection); copy.Achieved.AddRange(p.Achieved); copy.Bundles.AddRange(p.Bundles);
            return copy;
        }
        sealed class BoardChange { public BoardFrame Before, After; }
        readonly Queue<BoardChange> cardChanges = new Queue<BoardChange>();
        readonly HashSet<int> hiddenCards = new HashSet<int>();
        BoardFrame presentedBoard;
        Coroutine cardMotion;
        int cardTicket;
        bool deckCardInFlight;
        bool catchingUpCards;
        Sprite cardBack;
        bool CardsAnimating => cardMotion != null;
        List<Card> ShownMarket(Game game) => presentedBoard != null ? presentedBoard.Market : game.Market;
        Player ShownPlayer(Game game, int seat) => presentedBoard != null ? presentedBoard.Players[seat] : game.Players[seat];
        int ShownDeck(Game game) => presentedBoard != null ? presentedBoard.Deck : game.Deck.Count;

        void ResetCardMotion()
        {
            cardTicket++;
            if (cardMotion != null) StopCoroutine(cardMotion);
            cardMotion = null; presentedBoard = null; deckCardInFlight = false;
            cardChanges.Clear(); hiddenCards.Clear();
            for (var i = transform.childCount - 1; i >= 0; i--)
                if (transform.GetChild(i).name == "card-flyer") DestroyImmediate(transform.GetChild(i).gameObject);
        }
        void BeginCardPresentation()
        {
            ResetCardMotion();
            if (!Application.isPlaying || catchingUpCards) return;
            var after = BoardFrame.Capture(match.Game);
            var before = after.Copy();
            before.Deck += before.Market.Count;
            before.Market.Clear();
            presentedBoard = before;
            EnqueueCards(before, after);
        }
        void TrackCardChange(BoardFrame before)
        {
            if (!Application.isPlaying || catchingUpCards || match.Game == null) return;
            EnqueueCards(before, BoardFrame.Capture(match.Game));
        }
        void EnqueueCards(BoardFrame before, BoardFrame after)
        {
            cardChanges.Enqueue(new BoardChange { Before = before, After = after });
            if (cardMotion == null)
            {
                // Keep the pre-action board visible before the coroutine's first yield.
                presentedBoard = before.Copy();
                cardMotion = StartCoroutine(RunCardChanges(cardTicket));
            }
        }
        IEnumerator RunCardChanges(int ticket)
        {
            yield return null;
            while (ticket == cardTicket && match.Game != null && cardChanges.Count > 0)
                yield return PresentCardChange(cardChanges.Dequeue(), ticket);
            if (ticket != cardTicket || match.Game == null) yield break;
            presentedBoard = null;
            cardMotion = null;
            ShowTable();
        }
        static bool ContainsCard(IReadOnlyList<Card> cards, int id)
        {
            foreach (var card in cards) if (card != null && card.Id == id) return true;
            return false;
        }
        static List<Card> IncomingCards(BoardFrame before, BoardFrame after)
        {
            var cards = new List<Card>();
            var reshuffled = after.Shuffles != before.Shuffles;
            for (var i = 0; i < after.Players.Length; i++)
                if (after.Players[i].ReshuffleTakeLeft < before.Players[i].ReshuffleTakeLeft) reshuffled = true;
            foreach (var card in after.Market)
                if (card != null && (before.Round != after.Round || reshuffled || !ContainsCard(before.Market, card.Id))) cards.Add(card);
            return cards;
        }
        static BoardFrame CollectionStage(BoardFrame before, BoardFrame after, int seat)
        {
            var stage = before.Copy();
            var target = CopyPlayer(after.Players[seat]);
            if (target.Achieved.Count > before.Players[seat].Achieved.Count)
            {
                var bundle = target.Achieved.GetRange(before.Players[seat].Achieved.Count,
                    target.Achieved.Count - before.Players[seat].Achieved.Count);
                target.Quota = bundle[0];
                target.Collection.Clear(); target.Collection.AddRange(bundle.GetRange(1, bundle.Count - 1));
                target.Achieved.Clear(); target.Achieved.AddRange(before.Players[seat].Achieved);
                target.Score = before.Players[seat].Score;
                target.Bundles.Clear(); target.Bundles.AddRange(before.Players[seat].Bundles);
            }
            stage.Players[seat] = target;
            return stage;
        }
        IEnumerator PresentCardChange(BoardChange change, int ticket)
        {
            var before = change.Before; var after = change.After;
            presentedBoard = before.Copy();
            ShowTable();
            var seat = before.Actor;
            var acquired = new List<Card>();
            if (before.Round == after.Round)
            {
                var target = after.Players[seat];
                foreach (var card in before.Market)
                    if (card != null && !ContainsCard(after.Market, card.Id) &&
                        (target.Quota != null && target.Quota.Id == card.Id || ContainsCard(target.Collection, card.Id) || ContainsCard(target.Achieved, card.Id))) acquired.Add(card);
            }
            if (acquired.Count > 0)
            {
                var origins = CardPositions(acquired, -1, "");
                presentedBoard = CollectionStage(before, after, seat);
                foreach (var card in acquired)
                {
                    for (var i = 0; i < presentedBoard.Market.Count; i++)
                        if (presentedBoard.Market[i] != null && presentedBoard.Market[i].Id == card.Id) presentedBoard.Market[i] = null;
                    hiddenCards.Add(card.Id);
                }
                ShowTable();
                yield return MoveCards(acquired, origins, seat, "quota-cards", MarketScale, 1f, ticket);
                if (ticket != cardTicket) yield break;
                foreach (var card in acquired) hiddenCards.Remove(card.Id);
                ShowTable();
                if (after.Players[seat].Achieved.Count > before.Players[seat].Achieved.Count)
                {
                    // All completed quotas, including rank 1, visibly reach n/n first.
                    yield return new WaitForSeconds(0.3f);
                    if (ticket != cardTicket) yield break;
                    var bundle = new List<Card> { presentedBoard.Players[seat].Quota };
                    bundle.AddRange(presentedBoard.Players[seat].Collection);
                    origins = CardPositions(bundle, seat, "quota-cards");
                    foreach (var card in bundle) hiddenCards.Add(card.Id);
                    presentedBoard.Players[seat] = CopyPlayer(after.Players[seat]);
                    ShowTable();
                    yield return MoveCards(bundle, origins, seat, "achieved-cards", 1f, WideScreen() ? 0.62f : 1f, ticket);
                    if (ticket != cardTicket) yield break;
                    foreach (var card in bundle) hiddenCards.Remove(card.Id);
                }
            }
            var incoming = IncomingCards(before, after);
            presentedBoard = after.Copy();
            presentedBoard.Deck += incoming.Count;
            foreach (var card in incoming) hiddenCards.Add(card.Id);
            ShowTable();
            foreach (var card in incoming)
            {
                yield return DealCard(card, ticket);
                if (ticket != cardTicket) yield break;
            }
            presentedBoard = after.Copy();
            ShowTable();
        }
        Dictionary<int, Vector3> CardPositions(List<Card> cards, int seat, string area)
        {
            var positions = new Dictionary<int, Vector3>();
            Canvas.ForceUpdateCanvases();
            foreach (var card in cards)
            {
                var node = CardNode(card.Id, seat, area);
                if (node != null) positions[card.Id] = node.position;
            }
            return positions;
        }
        RectTransform CardNode(int id, int seat, string area) => frame.Find(seat < 0 ? "card" + id : "seat" + seat + "/" + area + "/card" + id) as RectTransform;
        RectTransform FlyingCard(Card card)
        {
            DrawCard(transform, match.Game.Theme(), card, 0, 0, 1f, null, false);
            var node = transform.Find("card" + card.Id) as RectTransform;
            node.name = "card-flyer";
            node.GetComponent<CanvasGroup>().alpha = 1f;
            return node;
        }
        IEnumerator MoveCards(List<Card> cards, Dictionary<int, Vector3> origins, int seat, string area, float fromScale, float toScale, int ticket)
        {
            var flyers = new List<RectTransform>();
            foreach (var card in cards) flyers.Add(FlyingCard(card));
            var elapsed = 0f;
            while (elapsed < 0.16f)
            {
                if (ticket != cardTicket) yield break;
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / 0.16f));
                for (var i = 0; i < cards.Count; i++)
                {
                    var target = CardNode(cards[i].Id, seat, area);
                    if (target == null) continue;
                    var from = origins.TryGetValue(cards[i].Id, out var origin) ? origin : target.position;
                    flyers[i].position = Vector3.Lerp(from, target.position, t);
                    flyers[i].localScale = frame.localScale * Mathf.Lerp(fromScale, toScale, t);
                }
                yield return null;
            }
            foreach (var flyer in flyers) DestroyImmediate(flyer.gameObject);
        }
        IEnumerator DealCard(Card card, int ticket)
        {
            deckCardInFlight = true;
            ShowTable();
            var deck = frame.Find("deck") as RectTransform;
            var back = DrawCardBack(transform, "card-flyer", 0, 0, 144f, 200f);
            back.localScale = frame.localScale;
            var start = deck.position;
            back.position = start;
            var elapsed = 0f;
            while (elapsed < 0.08f)
            {
                if (ticket != cardTicket) yield break;
                elapsed += Time.deltaTime;
                back.position = start + Vector3.up * (280f * frame.localScale.x * Mathf.Clamp01(elapsed / 0.08f));
                yield return null;
            }
            DestroyImmediate(back.gameObject);
            // Restore the reusable back off screen; the last card leaves no lower back.
            presentedBoard.Deck = Mathf.Max(0, presentedBoard.Deck - 1);
            deckCardInFlight = false;
            ShowTable();
            var flyer = FlyingCard(card);
            flyer.localScale = frame.localScale * MarketScale;
            var target = CardNode(card.Id, -1, "");
            var corners = new Vector3[4]; frame.GetWorldCorners(corners);
            start = new Vector3(target.position.x, corners[1].y + 220f * frame.localScale.x, target.position.z);
            elapsed = 0f;
            while (elapsed < 0.12f)
            {
                if (ticket != cardTicket) yield break;
                elapsed += Time.deltaTime;
                target = CardNode(card.Id, -1, "");
                if (target != null) flyer.position = Vector3.Lerp(start, target.position, Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / 0.12f)));
                yield return null;
            }
            DestroyImmediate(flyer.gameObject);
            hiddenCards.Remove(card.Id);
            ShowTable();
        }
        Sprite CardBackSprite()
        {
            if (cardBack == null) cardBack = Application.platform == RuntimePlatform.WebGLPlayer ? LoadBundledSprite("card_back") : ReadSprite("card_back.png");
            return cardBack;
        }
        RectTransform DrawCardBack(Transform parent, string name, float x, float y, float width, float height)
        {
            var root = Portrait.Rect(parent, name, x, y, width, height);
            var image = root.gameObject.AddComponent<Image>();
            image.sprite = CardBackSprite(); image.preserveAspect = true; image.raycastTarget = false;
            return root;
        }
        void DrawDeck(Game game)
        {
            var count = ShownDeck(game);
            var x = (WideScreen() ? LandWidth : ScreenWidth) - 24f - 144f;
            var deck = Portrait.Rect(frame, "deck", x, -68f, 144f, 200f);
            if (count > 1) DrawCardBack(deck, "lower", -3f, 3f, 144f, 200f);
            if (count > 0 && !deckCardInFlight) DrawCardBack(deck, "upper", 0, 0, 144f, 200f);
            Shade(TextAt(deck, "山札", 8f, 80f, 128f, 40f, 30, Cream, nameFont, TextAnchor.MiddleCenter));
            var figure = TextAt(deck, count.ToString(), 8f, 120f, 128f, 64f, 52, Cream, nameFont, TextAnchor.MiddleCenter);
            figure.gameObject.name = "remaining"; Shade(figure);
        }
        static string QuotaProgress(Player player) => player.Quota == null ? "" :
            $"{1 + player.Collection.Count}/{player.Quota.Rank}";
        void DrawQuotaProgress(Transform parent, Player player, float x, float y, float width, float height, int font)
        {
            var label = TextAt(parent, player.Quota == null ? "" : $"{1 + player.Collection.Count}<size={21}>/{player.Quota.Rank}</size>", x, y, width, height, font, Ink, nameFont, TextAnchor.MiddleLeft);
            label.fontStyle = FontStyle.Bold;
            label.gameObject.name = "quota-progress";
            label.supportRichText = true;
        }
    }
}

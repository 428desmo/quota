using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public sealed class TableView : MonoBehaviour
    {
        readonly OfflineMatch match = new OfflineMatch();
        Font font;
        RectTransform root;
        RectTransform content;
        bool busy;
        bool confirmPass;
        int setIndex;
        int playerCount = 3;
        bool sequenceRule;
        bool titleRule;
        string seedText = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<TableView>() != null) return;
            var host = new GameObject("Quota");
            host.AddComponent<TableView>();
        }

        void Start()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Yu Gothic" }, 16);
            var camera = Camera.main;
            if (camera != null) camera.backgroundColor = Hex("#f4f1ea");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(980, 800);
            gameObject.AddComponent<GraphicRaycaster>();
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<UnityEngine.EventSystems.EventSystem>();
                events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
            var panel = new GameObject("Root", typeof(RectTransform));
            panel.transform.SetParent(transform, false);
            root = panel.GetComponent<RectTransform>();
            Stretch(root);
            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scroll.transform.SetParent(root, false);
            var scrollRect = scroll.GetComponent<RectTransform>();
            Stretch(scrollRect);
            scrollRect.offsetMin = new Vector2(12, 12);
            scrollRect.offsetMax = new Vector2(-12, -12);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scroll.transform, false);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var body = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            body.transform.SetParent(viewport.transform, false);
            content = body.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            var layout = body.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroller = scroll.GetComponent<ScrollRect>();
            scroller.viewport = viewport.GetComponent<RectTransform>();
            scroller.content = content;
            scroller.horizontal = false;
            var sets = ItemCatalog.Sets;
            for (var i = 0; i < sets.Count; i++)
                if (sets[i].IsDefault) setIndex = i;
            ShowSetup();
        }

        void ShowSetup()
        {
            Clear();
            var sets = ItemCatalog.Sets;
            Title(content, "QUOTA");
            Note(content, "揃えて、達成。");
            Button(content, $"アイテムセット  {sets[setIndex].Name}", () =>
            {
                setIndex = (setIndex + 1) % sets.Count;
                ShowSetup();
            });
            Button(content, $"人数  {playerCount}", () =>
            {
                playerCount = playerCount == 3 ? 4 : 3;
                ShowSetup();
            });
            var seed = Field(content, "シード（空ならランダム）", seedText);
            seed.onValueChanged.AddListener(value => seedText = value);
            Button(content, $"並び順ボーナス  {(sequenceRule ? "オン" : "オフ")}", () =>
            {
                sequenceRule = !sequenceRule;
                ShowSetup();
            });
            Button(content, $"称号ボーナス  {(titleRule ? "オン" : "オフ")}", () =>
            {
                titleRule = !titleRule;
                ShowSetup();
            });
            Button(content, "対局開始", () =>
            {
                int? parsed = null;
                if (int.TryParse(seedText, out var number)) parsed = number;
                var names = new List<string> { "あなた" };
                for (var i = 1; i < playerCount; i++) names.Add($"CPU{i}");
                match.Begin(new GameConfig
                {
                    NumPlayers = playerCount,
                    Seed = parsed,
                    Names = names,
                    HumanSeats = new List<int> { 0 },
                    SequenceRule = sequenceRule,
                    TitleRule = titleRule,
                    ItemSet = sets[setIndex].Id,
                });
                confirmPass = false;
                ShowTable();
            });
        }

        void ShowTable()
        {
            Clear();
            var game = match.Game;
            var theme = ItemCatalog.Resolve(game.Config.ItemSet);
            Title(content, "QUOTA  揃えて、達成。");
            Note(content, $"手番 {game.TurnNumber} / 山札 {game.Deck.Count} / 膠着 {(game.StallFlag ? 1 : 0)} / 連続パス {game.NoGainStreak}/{game.Players.Count}");
            Note(content, "場札");
            var market = Row(content, "market");
            var me = game.Players[game.Current];
            foreach (var card in game.Market)
            {
                var playable = match.IsHumanTurn && !busy && CanPlay(card, me);
                var face = theme.FaceFor(card);
                Button(market, $"{theme.RankLabel(card)}\n{face.Name}", () => Play(new TakeQuota(card.Id)), playable, Hex(face.Color));
            }
            if (match.IsHumanTurn && !busy)
            {
                var controls = Row(content, "controls");
                if (me.Quota != null) Button(controls, "放棄", () => Play(new Abandon()));
                var passLabel = me.Quota == null ? "パス" : game.TurnGain ? "次へ" : "パス";
                Button(controls, passLabel, () =>
                {
                    if (me.Quota == null)
                    {
                        confirmPass = true;
                        ShowTable();
                        return;
                    }
                    Play(new Pass());
                });
            }
            else if (!game.Finished)
            {
                Note(content, $"{me.Name} が考えています");
            }
            for (var i = 0; i < game.Players.Count; i++)
            {
                var player = game.Players[i];
                var seat = Column(content, "seat" + i);
                var mark = i == game.Current && !game.Finished ? " ▶" : "";
                Note(seat, $"{player.Name}{mark}  {game.FinalScore(player)}点");
                Note(seat, "ノルマ  " + Line(theme, player.Quota, player.Collection));
                Note(seat, "実績  " + (player.Achieved.Count == 0 ? "なし" : Line(theme, null, player.Achieved)));
            }
            if (confirmPass) Confirm(content);
            if (game.Finished) Result(content, game);
            else Button(content, "最初の画面に戻る", ShowSetup);
        }

        void Confirm(RectTransform parent)
        {
            var box = Column(parent, "confirm");
            Note(box, "本当にパスしますか？");
            var row = Row(box, "confirm-buttons");
            Button(row, "パスする", () =>
            {
                confirmPass = false;
                Play(new Pass());
            });
            Button(row, "キャンセル", () =>
            {
                confirmPass = false;
                ShowTable();
            });
        }

        void Result(RectTransform parent, Game game)
        {
            var box = Column(parent, "result");
            Title(box, game.EndReason == "DECK" ? "ゲーム終了" : "膠着の連続");
            var place = 1;
            foreach (var group in game.Ranking())
            {
                foreach (var seat in group)
                {
                    var player = game.Players[seat];
                    Note(box, $"{place}位 {player.Name} {game.FinalScore(player)}点（達成{player.AchieveCount} / 最高{player.MaxSingleScore}）");
                    foreach (var line in Perks(game, player)) Note(box, line);
                }
                place += group.Count;
            }
            Button(box, "もう一局", ShowSetup);
        }

        void Play(GameAction action)
        {
            if (busy || !match.IsHumanTurn) return;
            confirmPass = false;
            match.Game.Step(action);
            StartCoroutine(RunCpus());
        }

        IEnumerator RunCpus()
        {
            busy = true;
            ShowTable();
            while (match.StepOneCpu())
            {
                ShowTable();
                yield return new WaitForSeconds(0.35f);
            }
            busy = false;
            ShowTable();
        }

        static bool CanPlay(Card card, Player player)
        {
            if (player.Quota == null) return card.Suit != Suit.Joker;
            return card.Suit == Suit.Joker || card.Suit == player.Quota.Suit;
        }

        static string Line(ItemSet theme, Card quota, IReadOnlyList<Card> cards)
        {
            var parts = new List<string>();
            if (quota != null) parts.Add(theme.Label(quota));
            foreach (var card in cards) parts.Add(theme.Label(card));
            return parts.Count == 0 ? "なし" : string.Join("  ", parts);
        }

        static IEnumerable<string> Perks(Game game, Player player)
        {
            var delivery = player.Score - BaseScore(player);
            if (delivery > 0) yield return $"{player.Name}の達成ボーナス +{delivery}";
            var sequence = game.SequencePoints(player);
            if (sequence > 0) yield return $"{player.Name}の並び順ボーナス +{sequence}";
            foreach (var award in game.TitleAwards(player))
                yield return $"{player.Name}が『{award.name}』を達成したので+{award.points}のボーナス獲得";
        }

        static int BaseScore(Player player)
        {
            var total = 0;
            var index = 0;
            while (index < player.Achieved.Count)
            {
                var rank = player.Achieved[index].Rank;
                if (rank == null) break;
                total += rank.Value;
                index += rank.Value;
            }
            return total;
        }

        void Clear()
        {
            for (var i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        }

        RectTransform Column(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        RectTransform Row(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            go.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        void Title(RectTransform parent, string text)
        {
            Label(parent, text, 22, FontStyle.Bold);
        }

        void Note(RectTransform parent, string text)
        {
            Label(parent, text, 14, FontStyle.Normal);
        }

        void Label(RectTransform parent, string text, int size, FontStyle style)
        {
            var go = new GameObject("label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = Hex("#222222");
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<LayoutElement>().preferredHeight = size + 8;
        }

        InputField Field(RectTransform parent, string caption, string value)
        {
            Note(parent, caption);
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.white;
            go.GetComponent<LayoutElement>().preferredHeight = 32;
            var textGo = new GameObject("text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>());
            var text = textGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 16;
            text.color = Hex("#222222");
            text.supportRichText = false;
            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.text = value;
            return field;
        }

        Text NewText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = 16;
            text.color = Hex("#222222");
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        void Button(RectTransform parent, string text, UnityEngine.Events.UnityAction action, bool enabled = true, Color? color = null)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color ?? Hex("#111111");
            var layout = go.GetComponent<LayoutElement>();
            layout.preferredHeight = 36;
            layout.preferredWidth = Mathf.Max(72, text.Length * 16);
            var label = NewText(go.transform, "caption");
            label.text = text;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color == null ? Color.white : Hex("#222222");
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.interactable = enabled;
            button.onClick.AddListener(action);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16, 16);
            rect.offsetMax = new Vector2(-16, -16);
        }

        static Color Hex(string html)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(html, out color) ? color : Color.white;
        }
    }
}

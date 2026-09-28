using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Quota
{
    public sealed class TableView : MonoBehaviour
    {
        const float ScreenWidth = 1080f;
        const float ScreenHeight = 1920f;
        const float SeatTop = 400f;
        const float SeatHeight = 380f;
        const float ActionStride = 74f;
        const float MarketScale = 1.35f;
        const float CardWidth = 95f;
        const float CardHeight = 132f;
        const float GoodsNameSize = 14f;

        static readonly Color[] IndicatorColors =
        {
            Hex("#000000"),
            Hex("#ff0000"),
            Hex("#00ff00"),
            Hex("#00ffff"),
            Hex("#ff00ff"),
        };

        readonly OfflineMatch match = new OfflineMatch();
        Font nameFont;
        Font roundFont;
        RectTransform frame;
        Image backdrop;
        Sprite verticalBackground;
        Sprite horizontalBackground;
        readonly Dictionary<string, Sprite> goodsSprites = new Dictionary<string, Sprite>();
        readonly List<RectTransform> seatFrames = new List<RectTransform>();
        bool busy;
        string confirm;
        int playerCount = 3;
        bool sequenceRule;
        bool titleRule;
        bool specialRule;
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
            nameFont = LoadFont(new[] { "Hiragino Kaku Gothic ProN W6", "HiraginoSans-W6", "Hiragino Kaku Gothic ProN", "Hiragino Sans", "Yu Gothic" });
            roundFont = LoadFont(new[] { "FOT-TsukuBRdGothic Std B", "FOT-筑紫B丸ゴシック Std B", "Hiragino Maru Gothic ProN", "Hiragino Kaku Gothic ProN", "Hiragino Sans" });
            var camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            gameObject.AddComponent<GraphicRaycaster>();
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<UnityEngine.EventSystems.EventSystem>();
                events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
            verticalBackground = LoadBackground("vertical_base.jpg");
            horizontalBackground = LoadBackground("horizontal_base.jpg");
            var root = Portrait.Rect(transform, "Root", 0f, 0f, ScreenWidth, ScreenHeight);
            Stretch(root);
            var backdropRect = Portrait.Rect(root, "Backdrop", 0f, 0f, ScreenWidth, ScreenHeight);
            backdropRect.anchorMin = backdropRect.anchorMax = new Vector2(0.5f, 0.5f);
            backdropRect.pivot = new Vector2(0.5f, 0.5f);
            backdropRect.anchoredPosition = Vector2.zero;
            backdrop = backdropRect.gameObject.AddComponent<Image>();
            backdrop.color = Color.white;
            backdrop.raycastTarget = false;
            frame = Portrait.Rect(root, "Frame", 0f, 0f, ScreenWidth, ScreenHeight);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            Fit();
            ShowSetup();
        }

        void Update()
        {
            Fit();
        }

        void Fit()
        {
            if (frame == null || Screen.width <= 0 || Screen.height <= 0) return;
            var portrait = Screen.height >= Screen.width;
            var sprite = portrait ? verticalBackground : horizontalBackground;
            if (sprite == null) sprite = verticalBackground != null ? verticalBackground : horizontalBackground;
            if (backdrop != null && sprite != null)
            {
                backdrop.sprite = sprite;
                backdrop.color = Color.white;
                var cover = Mathf.Max(Screen.width / sprite.rect.width, Screen.height / sprite.rect.height);
                backdrop.rectTransform.sizeDelta = new Vector2(sprite.rect.width * cover, sprite.rect.height * cover);
            }
            var scale = Mathf.Min(Screen.width / ScreenWidth, Screen.height / ScreenHeight);
            frame.localScale = new Vector3(scale, scale, 1f);
        }

        static Sprite LoadBackground(string fileName)
        {
            var path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!texture.LoadImage(File.ReadAllBytes(path))) return null;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        Sprite GoodsSprite(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (goodsSprites.TryGetValue(file, out var cached)) return cached;
            var path = Path.Combine(Application.streamingAssetsPath, "goods", file + ".png");
            Sprite sprite = null;
            if (File.Exists(path))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(File.ReadAllBytes(path)))
                {
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }
            goodsSprites[file] = sprite;
            return sprite;
        }

        void ShowSetup()
        {
            Clear();
            TextAt(frame, "QUOTA", 48f, 36f, 700f, 72f, 64, Color.white, nameFont, TextAnchor.MiddleLeft);
            TextAt(frame, "揃えて、達成。", 48f, 112f, 700f, 36f, 28, Color.white, nameFont, TextAnchor.MiddleLeft);
            var column = Portrait.Rect(frame, "setup", 48f, 180f, 984f, 1600f);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            SetupButton(column, $"人数  {playerCount}", () =>
            {
                playerCount = playerCount == 3 ? 4 : 3;
                ShowSetup();
            });
            var seed = Field(column, "シード（空ならランダム）", seedText);
            seed.onValueChanged.AddListener(value => seedText = value);
            SetupButton(column, $"並び順ボーナス  {(sequenceRule ? "オン" : "オフ")}", () =>
            {
                sequenceRule = !sequenceRule;
                ShowSetup();
            });
            SetupButton(column, $"称号ボーナス  {(titleRule ? "オン" : "オフ")}", () =>
            {
                titleRule = !titleRule;
                ShowSetup();
            });
            SetupButton(column, $"特殊アクション  {(specialRule ? "オン" : "オフ")}", () =>
            {
                specialRule = !specialRule;
                ShowSetup();
            });
            SetupButton(column, "対局開始", () =>
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
                    SpecialActionsRule = specialRule,
                });
                confirm = null;
                ShowTable();
            });
        }

        void ShowTable()
        {
            Clear();
            seatFrames.Clear();
            var game = match.Game;
            var theme = game.Theme();
            for (var i = 0; i < game.Players.Count; i++)
                DrawPlayer(game, theme, i);
            Portrait.Box(frame, "market-tray", 20f, 170f, 1040f, 210f, 7f, 0f, new Color(1f, 1f, 1f, 0.5f), Color.white, false);
            DrawMarket(game, theme);
            DrawTitle(game);
            if (confirm == null && match.IsHumanTurn && !busy && !game.Finished) DrawControls(game);
            else if (confirm == null && !game.Finished) TextAt(frame, $"{game.Players[game.Current].Name} が考えています", 28f, 108f, 700f, 32f, 22, Color.white, nameFont, TextAnchor.MiddleLeft);
            if (!game.Finished) LeaveButton();
            if (game.Finished) Result(game);
            else if (confirm != null) Confirm();
        }

        void DrawTitle(Game game)
        {
            TextAt(frame, "QUOTA", 28f, 16f, 640f, 68f, 56, Color.white, nameFont, TextAnchor.MiddleLeft);
            TextAt(frame, "揃えて、達成。", 28f, 84f, 640f, 32f, 24, Color.white, nameFont, TextAnchor.MiddleLeft);
            if (game.DoubleStage == 1) TextAt(frame, "ダブル：1回目の行動です。", 300f, 28f, 460f, 36f, 22, Color.white, nameFont, TextAnchor.MiddleRight);
            else if (game.DoubleStage == 2) TextAt(frame, "ダブル：2回目の行動です。", 300f, 28f, 460f, 36f, 22, Color.white, nameFont, TextAnchor.MiddleRight);
            else if (game.Plan == "reshuffle") TextAt(frame, "配り直しました。行動を選んでください。", 280f, 28f, 480f, 36f, 22, Color.white, nameFont, TextAnchor.MiddleRight);
            var me = game.Players[game.Current];
            var hint = $"手番 {game.TurnNumber}  山札 {game.Deck.Count}  膠着 {(game.StallFlag ? 1 : 0)}/{game.Players.Count}";
            if (me.Quota != null && me.Quota.Rank != null)
            {
                var need = me.Quota.Rank.Value - 1 - me.Collection.Count;
                if (need > 0) hint = $"あと{need}枚   " + hint;
            }
            TextAt(frame, hint, 28f, 116f, 1020f, 28f, 20, Color.white, nameFont, TextAnchor.MiddleLeft);
        }

        void DrawMarket(Game game, ItemSet theme)
        {
            var slots = Mathf.Max(game.MarketSize(), game.Market.Count);
            if (slots == 0) return;
            var cardWidth = CardWidth * MarketScale;
            var cardHeight = CardHeight * MarketScale;
            var gap = 16f;
            var group = slots * cardWidth + (slots - 1) * gap;
            var origin = 20f + (1040f - group) * 0.5f;
            var y = 170f + (210f - cardHeight) * 0.5f;
            var me = game.Players[game.Current];
            var yours = match.IsHumanTurn && !busy && !game.Finished;
            for (var i = 0; i < game.Market.Count; i++)
            {
                var card = game.Market[i];
                if (card == null) continue;
                var playable = yours && CanPlay(card, me);
                var cardId = card.Id;
                var takingQuota = me.Quota == null;
                var x = origin + i * (cardWidth + gap);
                DrawCard(frame, theme, card, x, y, MarketScale, playable ? () =>
                {
                    if (takingQuota) Play(new TakeQuota(cardId));
                    else Play(new Collect(new[] { cardId }));
                } : null, yours && !playable);
            }
        }

        void DrawPlayer(Game game, ItemSet theme, int index)
        {
            var player = game.Players[index];
            var top = SeatTop + index * SeatHeight;
            var seat = Portrait.Rect(frame, "seat" + index, 0f, top, ScreenWidth, SeatHeight);
            while (seatFrames.Count <= index) seatFrames.Add(null);
            seatFrames[index] = seat;
            Portrait.Box(seat, "plate", 25f, 25f, 1030f, 340f, 7f, 1f, new Color(1f, 1f, 1f, 0.7f), Color.black, false);
            Portrait.Box(seat, "nameplate", 0f, 10f, 300f, 50f, 4.5f, 1f, Color.white, Color.black, true);
            var name = TextAt(seat, player.Name, 12f, 10f, 276f, 50f, 36, Color.black, nameFont, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            var quotaTop = 75f;
            TextAt(seat, "ノルマ", 0f, quotaTop, 122f, 40f, 24, Color.black, nameFont, TextAnchor.UpperRight);
            var quotaCards = Portrait.Rect(seat, "quota-cards", 130f, quotaTop, 768f, 145f);
            quotaCards.gameObject.AddComponent<RectMask2D>();
            var strip = new List<Card>();
            if (player.Quota != null) strip.Add(player.Quota);
            strip.AddRange(player.Collection);
            LayCards(quotaCards, theme, strip, 6.5f, 55f);
            TextAt(seat, "実績", 0f, 220f, 122f, 40f, 24, Color.black, nameFont, TextAnchor.UpperRight);
            var achieved = Portrait.Rect(seat, "achieved-cards", 130f, 220f, 405f, 145f);
            achieved.gameObject.AddComponent<RectMask2D>();
            LayCards(achieved, theme, player.Achieved, 6.5f, 3f);
            TextAt(seat, "ボーナス", 535f, 220f, 212f, 40f, 24, Color.black, nameFont, TextAnchor.UpperRight);
            Portrait.Box(seat, "chip-tray", 775f, 240f, 220f, 105f, 7f, 1f, Color.white, Color.black, false);
            var side = $"{game.FinalScore(player)}点";
            if (game.Config.SpecialActionsRule)
                side += $"\nダブル {(player.DoubleActionLeft > 0 ? "残1" : "済")}\n配り直し {(player.ReshuffleTakeLeft > 0 ? "残1" : "済")}";
            if (index == game.Current && !game.Finished) side = "▶ " + side;
            TextAt(seat, side, 898f, quotaTop, 170f, 140f, 20, Color.black, nameFont, TextAnchor.UpperLeft);
        }

        void LayCards(RectTransform area, ItemSet theme, IReadOnlyList<Card> cards, float padding, float stride)
        {
            var x = padding;
            var y = padding;
            foreach (var card in cards)
            {
                DrawCard(area, theme, card, x, y, 1f, null, false);
                x += stride;
            }
        }

        void DrawCard(Transform parent, ItemSet theme, Card card, float x, float y, float scale, UnityAction onClick, bool dim)
        {
            var width = CardWidth * scale;
            var height = CardHeight * scale;
            var host = Portrait.Rect(parent, "card" + card.Id, x, y, width, height);
            var group = host.gameObject.AddComponent<CanvasGroup>();
            group.alpha = dim ? 0.35f : 1f;
            group.blocksRaycasts = onClick != null;
            Portrait.Box(host, "face", 0f, 0f, width, height, 4.5f * scale, Mathf.Max(1f, scale), Color.white, Color.black, false);
            var kind = KindIndex(card);
            var ink = IndicatorColors[kind];
            Portrait.Solid(host, "mark", 0f, (66f + kind * 10f) * scale, 4f * scale, 10f * scale, ink);
            var face = theme.FaceFor(card);
            Baseline(host, theme.RankLabel(card), 20f * scale, 30f * scale, 24f * scale, Hex(face.Color), roundFont, 70f * scale);
            var diameter = 72f * scale;
            var iconX = 47.5f * scale - diameter * 0.5f;
            var iconY = 66f * scale - diameter * 0.5f;
            var sprite = GoodsSprite(face.File);
            if (sprite != null)
            {
                var icon = Portrait.Rect(host, "suit", iconX, iconY, diameter, diameter);
                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            else
            {
                Portrait.Circle(host, "suit", iconX, iconY, diameter, ink);
            }
            Baseline(host, face.Name, 47.5f * scale, 118f * scale, GoodsNameSize * scale, Color.black, roundFont, width - 8f);
            if (onClick == null) return;
            var hit = host.gameObject.AddComponent<Image>();
            hit.sprite = Portrait.White;
            hit.color = new Color(1f, 1f, 1f, 0f);
            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(onClick);
        }

        void DrawControls(Game game)
        {
            var me = game.Players[game.Current];
            var top = SeatTop + game.Current * SeatHeight;
            var controls = Portrait.Rect(frame, "controls", 0f, top, ScreenWidth, SeatHeight);
            var entries = new List<KeyValuePair<string, UnityAction>>();
            var canDeclare = game.Plan == "normal" && !game.TurnGain && game.DoubleStage == 0;
            if (canDeclare && me.DoubleActionLeft > 0) entries.Add(Item("ダブル", () =>
            {
                confirm = null;
                game.DeclareDouble();
                ShowTable();
            }));
            else if (game.Plan == "double" && game.DoubleStage == 1 && !game.TurnGain) entries.Add(Item("キャンセル", () =>
            {
                confirm = null;
                game.CancelDouble();
                ShowTable();
            }));
            if (canDeclare && me.ReshuffleTakeLeft > 0) entries.Add(Item("配り直し", () =>
            {
                confirm = null;
                game.DeclareReshuffle();
                ShowTable();
            }));
            if (me.Quota != null) entries.Add(Item("放棄", () => Ask("abandon")));
            var passLabel = me.Quota != null && game.TurnGain ? "次へ" : "パス";
            entries.Add(Item(passLabel, () =>
            {
                if (!HasTakeable(game, me)) Play(new Pass());
                else Ask(passLabel == "次へ" ? "next" : "pass");
            }));
            for (var i = 0; i < entries.Count; i++)
            {
                var width = entries[i].Key.Length * 48f + 30f;
                Pill(controls, entries[i].Key, ScreenWidth - width, i * ActionStride, width, 72f, 48, entries[i].Value);
            }
        }

        void LeaveButton()
        {
            const string caption = "ゲームから抜ける";
            var width = caption.Length * 28f + 8f;
            var host = Portrait.Rect(frame, caption, ScreenWidth - 20f - width, 18f, width, 40f);
            var hit = host.gameObject.AddComponent<Image>();
            hit.sprite = Portrait.White;
            hit.color = new Color(1f, 1f, 1f, 0f);
            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() => Ask("leave"));
            var label = TextAt(host, caption, 0f, 0f, width, 40f, 28, Color.black, nameFont, TextAnchor.MiddleRight);
            label.raycastTarget = false;
        }

        void Ask(string kind)
        {
            confirm = kind;
            ShowTable();
        }

        void Confirm()
        {
            string message;
            string yes;
            UnityAction run;
            if (confirm == "abandon")
            {
                message = "本当に放棄しますか？";
                yes = "放棄する";
                run = () => Play(new Abandon());
            }
            else if (confirm == "leave")
            {
                message = "本当にゲームから抜けますか？";
                yes = "抜ける";
                run = () =>
                {
                    confirm = null;
                    ShowSetup();
                };
            }
            else if (confirm == "next")
            {
                message = "本当に次へ進みますか？";
                yes = "次へ進む";
                run = () => Play(new Pass());
            }
            else
            {
                message = "本当にパスしますか？";
                yes = "パスする";
                run = () => Play(new Pass());
            }
            var game = match.Game;
            var seatIndex = confirm == "leave" ? HumanSeat(game) : game.Current;
            var seat = seatFrames[seatIndex];
            const float panelWidth = 700f;
            const float panelHeight = 280f;
            var panel = Portrait.Box(seat, "confirm", 25f + (1030f - panelWidth) * 0.5f, 25f + (340f - panelHeight) * 0.5f, panelWidth, panelHeight, 7f, 1f, Color.white, Color.black, false);
            TextAt(panel, message, 24f, 28f, 652f, 80f, 32, Color.black, nameFont, TextAnchor.MiddleCenter);
            var yesWidth = yes.Length * 32f + 30f;
            Pill(panel, yes, 40f, 150f, yesWidth, 72f, 32, () =>
            {
                confirm = null;
                run();
            });
            Pill(panel, "キャンセル", 700f - 40f - 224f, 150f, 224f, 72f, 32, () =>
            {
                confirm = null;
                ShowTable();
            });
        }

        void Result(Game game)
        {
            var panel = Portrait.Box(frame, "result", 90f, 430f, 900f, 1100f, 7f, 1f, Color.white, Color.black, false);
            TextAt(panel, game.EndReason == "DECK" ? "ゲーム終了" : "膠着の連続", 32f, 24f, 836f, 56f, 36, Color.black, nameFont, TextAnchor.MiddleLeft);
            var place = 1;
            var y = 96f;
            foreach (var group in game.Ranking())
            {
                foreach (var seat in group)
                {
                    var player = game.Players[seat];
                    TextAt(panel, $"{place}位 {player.Name} {game.FinalScore(player)}点（達成{player.AchieveCount} / 最高{player.MaxSingleScore}）", 32f, y, 836f, 36f, 22, Color.black, nameFont, TextAnchor.MiddleLeft);
                    y += 36f;
                    foreach (var line in Perks(game, player))
                    {
                        TextAt(panel, line, 48f, y, 820f, 32f, 18, Color.black, nameFont, TextAnchor.MiddleLeft);
                        y += 30f;
                    }
                }
                place += group.Count;
            }
            Pill(panel, "もう一局", 32f, 1000f, 280f, 72f, 32, ShowSetup);
        }

        void Play(GameAction action)
        {
            if (busy || !match.IsHumanTurn || !match.Game.IsLegal(action)) return;
            confirm = null;
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

        static int HumanSeat(Game game)
        {
            for (var i = 0; i < game.Players.Count; i++)
                if (game.Players[i].IsHuman) return i;
            return game.Current;
        }

        static bool HasTakeable(Game game, Player player)
        {
            foreach (var card in game.Market)
                if (card != null && CanPlay(card, player)) return true;
            return false;
        }

        static bool CanPlay(Card card, Player player)
        {
            if (player.Quota == null) return card.Suit != Suit.Joker;
            return card.Suit == Suit.Joker || card.Suit == player.Quota.Suit;
        }

        static int KindIndex(Card card)
        {
            switch (card.Suit)
            {
                case Suit.Joker: return 0;
                case Suit.S: return 1;
                case Suit.H: return 2;
                case Suit.C: return 3;
                case Suit.D: return 4;
                default: return 0;
            }
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
            for (var i = frame.childCount - 1; i >= 0; i--)
            {
                var child = frame.GetChild(i).gameObject;
                child.name = "retired";
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        Text TextAt(Transform parent, string text, float x, float y, float width, float height, int size, Color color, Font font, TextAnchor anchor)
        {
            var host = Portrait.Rect(parent, "label", x, y, width, height);
            var label = host.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        void Baseline(Transform parent, string text, float centerX, float baseline, float size, Color color, Font font, float width)
        {
            var height = size;
            var host = Portrait.Rect(parent, "glyph", centerX - width * 0.5f, baseline - height, width, height);
            var label = host.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            label.color = color;
            label.alignment = TextAnchor.LowerCenter;
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
        }

        void Pill(Transform parent, string caption, float x, float y, float width, float height, int size, UnityAction action)
        {
            var host = Portrait.Box(parent, caption, x, y, width, height, 7f, 1f, Color.white, Color.black, false);
            var hit = host.GetComponent<Image>();
            hit.raycastTarget = true;
            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(action);
            TextAt(host, caption, 0f, 0f, width, height, size, Color.black, nameFont, TextAnchor.MiddleCenter);
        }

        void SetupButton(RectTransform parent, string caption, UnityAction action)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = Portrait.SlicedRound;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var layout = go.GetComponent<LayoutElement>();
            layout.preferredHeight = 72f;
            layout.minHeight = 72f;
            layout.preferredWidth = 700f;
            var label = new GameObject("caption", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            Stretch(label.GetComponent<RectTransform>(), 16f, 8f);
            var text = label.GetComponent<Text>();
            text.font = nameFont;
            text.fontSize = 32;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = caption;
            text.raycastTarget = false;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
        }

        InputField Field(RectTransform parent, string caption, string value)
        {
            var block = new GameObject(caption, typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            block.transform.SetParent(parent, false);
            block.GetComponent<LayoutElement>().preferredHeight = 96f;
            var layout = block.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var note = new GameObject("label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            note.transform.SetParent(block.transform, false);
            note.GetComponent<LayoutElement>().preferredHeight = 28f;
            var noteText = note.GetComponent<Text>();
            noteText.font = nameFont;
            noteText.fontSize = 22;
            noteText.color = Color.white;
            noteText.alignment = TextAnchor.MiddleLeft;
            noteText.text = caption;
            var go = new GameObject("field", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(block.transform, false);
            go.GetComponent<Image>().sprite = Portrait.White;
            go.GetComponent<Image>().color = Color.white;
            go.GetComponent<LayoutElement>().preferredHeight = 56f;
            var textGo = new GameObject("text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>(), 12f, 8f);
            var text = textGo.GetComponent<Text>();
            text.font = nameFont;
            text.fontSize = 28;
            text.color = Color.black;
            text.supportRichText = false;
            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.text = value;
            return field;
        }

        static KeyValuePair<string, UnityAction> Item(string caption, UnityAction action)
        {
            return new KeyValuePair<string, UnityAction>(caption, action);
        }

        static Font LoadFont(string[] names)
        {
            try
            {
                var font = Font.CreateDynamicFontFromOSFont(names, 32);
                if (font != null) return font;
            }
            catch (System.Exception)
            {
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static void Stretch(RectTransform rect, float padX = 0f, float padY = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padX, padY);
            rect.offsetMax = new Vector2(-padX, -padY);
        }

        static Color Hex(string html)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(html, out color) ? color : Color.white;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Quota.Tests
{
    public class TableViewTests
    {
        GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
            var events = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (events != null) Object.DestroyImmediate(events.gameObject);
        }

        [Test]
        public void SetupTextStaysInsideThePanelAndIsTallEnoughToRead()
        {
            host = new GameObject("Quota");
            var view = host.AddComponent<TableView>();
            typeof(TableView).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, null);

            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            host.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var canvasRect = host.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(980, 800);
            canvasRect.localScale = Vector3.one;

            var frame = host.transform.Find("Root/Frame") as RectTransform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(frame);
            Canvas.ForceUpdateCanvases();

            Text splash = null;
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text.StartsWith("港で働く仲買人のあなた。")) splash = label;
            Assert.IsNotNull(splash);
            Assert.AreEqual("港で働く仲買人のあなた。\n大口顧客のために、舶来の交易品を買い集めよう。\n買い付けノルマは、自分で決める。", splash.text);
            Assert.IsNull(host.transform.Find("Root/Frame/setup"));
            var hold = (float)typeof(TableView).GetField("SplashSeconds", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.AreEqual(3f, hold);
            typeof(TableView).GetMethod("DismissSplash", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            LayoutRebuilder.ForceRebuildLayoutImmediate(frame);
            Canvas.ForceUpdateCanvases();

            Assert.AreEqual(1080f, frame.sizeDelta.x, 0.01f);
            Assert.AreEqual(1920f, frame.sizeDelta.y, 0.01f);
            var backdrop = host.transform.Find("Root/Backdrop") as RectTransform;
            var photo = backdrop.GetComponent<Image>().sprite;
            Assert.IsNotNull(photo);
            if (Screen.width > 0 && Screen.height > 0)
            {
                var cover = Mathf.Max(Screen.width / photo.rect.width, Screen.height / photo.rect.height);
                Assert.AreEqual(photo.rect.width * cover, backdrop.sizeDelta.x, 1f);
                Assert.AreEqual(photo.rect.height * cover, backdrop.sizeDelta.y, 1f);
                Assert.GreaterOrEqual(backdrop.sizeDelta.x, Screen.width - 1f);
                Assert.GreaterOrEqual(backdrop.sizeDelta.y, Screen.height - 1f);
            }

            var buttons = host.GetComponentsInChildren<Button>();
            Assert.Greater(buttons.Length, 0);
            foreach (var button in buttons)
            {
                var caption = button.GetComponentInChildren<Text>();
                Assert.Greater(caption.rectTransform.rect.height, 16f, caption.text);
                Assert.Greater(caption.rectTransform.rect.width, 40f, caption.text);
                Assert.IsFalse(caption.text.Contains("アイテムセット"), caption.text);
                Assert.IsFalse(caption.text.Contains("並び順ボーナス"), caption.text);
                Assert.IsFalse(caption.text.Contains("称号ボーナス"), caption.text);
                Assert.IsFalse(caption.text.Contains("特殊アクション"), caption.text);
                Assert.IsFalse(caption.text.Contains("シード"), caption.text);
                Assert.IsFalse(caption.text.StartsWith("シンプルモード"), caption.text);
            }
            Assert.IsNotNull(host.transform.Find("Root/Frame/title-mark"));
            Assert.IsNotNull(host.transform.Find("Root/Frame/title-catch"));
            InputField name = null;
            foreach (var field in host.GetComponentsInChildren<InputField>())
                if (field.text == "あなた") name = field;
            Assert.IsNotNull(name);
            Rebuild(host);

            var guide = ButtonNamed("QuickStartガイド").GetComponent<RectTransform>();
            var rules = ButtonNamed("ルール").GetComponent<RectTransform>();
            var hint = ButtonNamed("勝つためのヒント").GetComponent<RectTransform>();
            var settings = ButtonNamed("設定").GetComponent<RectTransform>();
            var start = ButtonNamed("対局開始").GetComponent<RectTransform>();
            var watch = ButtonNamed("CPU模擬戦を観戦").GetComponent<RectTransform>();
            Assert.AreEqual(1080f * 0.40f, guide.rect.width, 2f);
            Assert.AreEqual(guide.rect.width, rules.rect.width, 1f);
            Assert.AreEqual(guide.rect.width, hint.rect.width, 1f);
            Assert.AreEqual(1080f * 0.35f, settings.rect.width, 2f);
            Assert.AreEqual(settings.rect.width, start.rect.width, 1f);
            Assert.AreEqual(settings.rect.width, watch.rect.width, 1f);
            Assert.AreEqual(guide.rect.height * 2f, start.rect.height, 2f);
            Assert.AreEqual(guide.GetComponentInChildren<Text>().fontSize * 2, start.GetComponentInChildren<Text>().fontSize);

            var countLabel = FindText("プレイヤーの数：");
            var nameLabel = FindText("あなたの名前：");
            Assert.IsNotNull(countLabel);
            Assert.IsNotNull(nameLabel);
            var countRow = countLabel.rectTransform.parent as RectTransform;
            var nameRow = nameLabel.rectTransform.parent as RectTransform;
            var countButton = ButtonNamed("3人").GetComponent<RectTransform>();
            var nameBox = name.GetComponent<RectTransform>();
            Assert.AreEqual(1080f * 0.60f, countRow.rect.width, 2f);
            Assert.AreEqual(countRow.rect.width, nameRow.rect.width, 1f);
            Assert.AreEqual(WorldLeft(countLabel.rectTransform), WorldLeft(nameLabel.rectTransform), 1f);
            Assert.AreEqual(WorldLeft(countButton), WorldLeft(nameBox), 1f);
            Assert.AreEqual(countButton.rect.width, nameBox.rect.width, 1f);
            Assert.AreEqual(WorldMidY(countLabel.rectTransform), WorldMidY(countButton), 2f);
            Assert.AreEqual(WorldMidY(nameLabel.rectTransform), WorldMidY(nameBox), 2f);
            var section = guide.rect.height * 2f;
            var scaleY = Mathf.Abs(WorldTop(guide) - WorldBottom(guide)) / guide.rect.height;
            Assert.AreEqual(section, (WorldBottom(hint) - WorldTop(countRow)) / scaleY, 3f);
            Assert.AreEqual(section, (WorldBottom(nameRow) - WorldTop(settings)) / scaleY, 3f);
            Assert.AreEqual(section, (WorldBottom(settings) - WorldTop(start)) / scaleY, 3f);

            Assert.AreEqual(WorldMidX(frame), WorldMidX(guide), 2f);
            Assert.AreEqual(WorldMidX(frame), WorldMidX(settings), 2f);
            Assert.AreEqual(WorldMidX(frame), WorldMidX(start), 2f);
            Assert.AreEqual(WorldMidX(frame), WorldMidX(countRow), 2f);
            Assert.AreEqual(WorldMidX(frame), WorldMidX(nameRow), 2f);

            Assert.AreEqual(Color.white, countLabel.color);
            Assert.AreEqual(Color.white, nameLabel.color);

            Click("設定");
            Assert.IsNotNull(ButtonNamed("シンプルモード　オン"));
            var decide = ButtonNamed("決定").GetComponent<RectTransform>();
            var cancel = ButtonNamed("キャンセル").GetComponent<RectTransform>();
            var dialog = host.transform.Find("Root/Frame/setup-dialog") as RectTransform;
            var pairLeft = Mathf.Min(WorldLeft(decide), WorldLeft(cancel));
            var pairRight = Mathf.Max(WorldRight(decide), WorldRight(cancel));
            Assert.AreEqual(WorldMidX(dialog), (pairLeft + pairRight) * 0.5f, 3f);
            Click("キャンセル");
            Assert.IsNull(FindButton("シンプルモード　オン"));

            Click("QuickStartガイド");
            Assert.IsNotNull(FindText("QuickStartガイド"));
            Text intro = null;
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text.StartsWith("場札から商品のカードを1枚選んで")) intro = label;
            Assert.IsNotNull(intro);
            Assert.AreEqual(36, intro.fontSize);
            var quickScroll = host.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(quickScroll);
            Click("OK");
            Assert.IsNull(host.transform.Find("Root/Frame/setup-dialog"));

            Click("ルール");
            Rebuild(host);
            var rulesScroll = host.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(rulesScroll);
            Assert.Greater(rulesScroll.content.rect.height, rulesScroll.viewport.rect.height);
            Click("OK");
        }

        [Test]
        public void MarketCardsShowTheGoodsPicture()
        {
            host = Open();
            Set("seedText", "7");
            Click("対局開始");
            Image icon = null;
            foreach (var image in host.GetComponentsInChildren<Image>())
            {
                if (image.gameObject.name != "suit" || image.sprite == null) continue;
                icon = image;
                break;
            }
            Assert.IsNotNull(icon);
            Assert.Greater(icon.rectTransform.rect.width, 70f);
            Assert.Less(icon.rectTransform.rect.width, 100f);
        }

        [Test]
        public void PassButtonHasAVisibleSize()
        {
            host = Open();
            Set("seedText", "0");
            Click("対局開始");
            var pass = ButtonNamed("パス");
            Assert.Greater(pass.GetComponent<RectTransform>().rect.width, 40f);
            Assert.Greater(pass.GetComponent<RectTransform>().rect.height, 16f);
            Assert.Greater(pass.GetComponentInChildren<Text>().rectTransform.rect.width, 20f);

            Click("パス");
            var confirm = ButtonNamed("パスする");
            var cancel = ButtonNamed("キャンセル");
            Assert.Greater(confirm.GetComponent<RectTransform>().rect.width, 40f);
            Assert.Greater(cancel.GetComponent<RectTransform>().rect.width, 40f);
        }

        [Test]
        public void SpecialButtonsStackDownTheRightOfTheCurrentSeat()
        {
            host = Open();
            Set("specialRule", true);
            Set("seedText", "0");
            Click("対局開始");

            var seat = host.transform.Find("Root/Frame/seat0");
            Assert.IsNull(seat.Find("wash"));
            var plate = seat.Find("plate") as RectTransform;
            Assert.AreEqual(0, plate.GetSiblingIndex());
            Assert.AreEqual(new Vector2(25f, -25f), plate.anchoredPosition);
            Assert.AreEqual(new Vector2(1030f, 340f), plate.sizeDelta);
            Assert.AreEqual(Color.black, plate.GetComponent<Image>().color);
            var fill = plate.Find("fill") as RectTransform;
            var fillColor = fill.GetComponent<Image>().color;
            Assert.AreEqual(1f, fillColor.r, 0.001f);
            Assert.AreEqual(1f, fillColor.g, 0.001f);
            Assert.AreEqual(1f, fillColor.b, 0.001f);
            Assert.AreEqual(0.7f, fillColor.a, 0.001f);
            Assert.AreEqual(new Vector2(1f, -1f), fill.anchoredPosition);
            Assert.AreEqual(new Vector2(1028f, 338f), fill.sizeDelta);

            var controls = host.transform.Find("Root/Frame/controls") as RectTransform;
            Assert.IsNotNull(controls);
            Assert.AreEqual(new Vector2(0f, -400f), controls.anchoredPosition);
            var labels = new System.Collections.Generic.List<string>();
            for (var i = 0; i < controls.childCount; i++)
            {
                var button = controls.GetChild(i) as RectTransform;
                var caption = button.GetComponentInChildren<Text>();
                labels.Add(caption.text);
                Assert.AreEqual(72f, button.sizeDelta.y, 0.01f);
                Assert.AreEqual(1080f, button.anchoredPosition.x + button.sizeDelta.x, 0.01f);
                Assert.AreEqual(-i * 74f, button.anchoredPosition.y, 0.01f);
            }
            CollectionAssert.AreEqual(new[] { "ダブル", "配り直し", "パス" }, labels);

            Click("ダブル");
            Assert.IsNull(FindButton("パスする"));
            Assert.IsNotNull(FindText("ダブル：1回目の行動です。"));
            Assert.IsNull(host.transform.Find("Root/Frame/controls/ダブル"));
            Click("キャンセル");
            Assert.IsNotNull(ButtonNamed("ダブル"));
            Assert.IsNull(FindButton("キャンセル"));
        }

        [Test]
        public void LandscapeKeepsCardSizeAndMovesTheMarketToTheRight()
        {
            host = Open();
            Set("widePreview", true);
            Set("seedText", "0");
            Click("対局開始");

            var frame = host.transform.Find("Root/Frame") as RectTransform;
            Assert.AreEqual(1920f, frame.sizeDelta.x, 0.01f);
            Assert.AreEqual(1080f, frame.sizeDelta.y, 0.01f);
            var seat = host.transform.Find("Root/Frame/seat0") as RectTransform;
            Assert.AreEqual(new Vector2(0f, -120f), seat.anchoredPosition);
            Assert.AreEqual(new Vector2(1220f, 240f), seat.sizeDelta);
            Assert.IsNotNull(seat.Find("nameplate"));
            Assert.IsNotNull(seat.Find("bonus-box"));
            Assert.IsNotNull(seat.Find("record-box"));
            var quota = seat.Find("quota-cards");
            Assert.IsNotNull(quota);
            if (quota.childCount > 0)
            {
                var card = quota.GetChild(0) as RectTransform;
                Assert.AreEqual(95f, card.sizeDelta.x, 0.01f);
                Assert.AreEqual(132f, card.sizeDelta.y, 0.01f);
            }

            var tray = host.transform.Find("Root/Frame/market-tray") as RectTransform;
            Assert.AreEqual(new Vector2(1245f, -320f), tray.anchoredPosition);
            Assert.AreEqual(new Vector2(650f, 450f), tray.sizeDelta);
            RectTransform marketCard = null;
            for (var i = 0; i < frame.childCount; i++)
            {
                var child = frame.GetChild(i) as RectTransform;
                if (!child.name.StartsWith("card")) continue;
                marketCard = child;
                break;
            }
            Assert.IsNotNull(marketCard);
            Assert.AreEqual(95f, marketCard.sizeDelta.x, 0.01f);
            Assert.AreEqual(132f, marketCard.sizeDelta.y, 0.01f);
            Assert.Greater(marketCard.anchoredPosition.x, 1220f);

            var controls = host.transform.Find("Root/Frame/controls");
            Assert.IsNotNull(controls);
            var button = controls.GetChild(0) as RectTransform;
            Assert.AreEqual(1920f - 28f, button.anchoredPosition.x + button.sizeDelta.x, 0.01f);
            Assert.AreEqual(-(320f + 450f + 16f), button.anchoredPosition.y, 0.01f);
            Assert.AreEqual(72f, button.sizeDelta.y, 0.01f);
            Assert.IsNull(frame.Find("confirm"));

            var view = host.GetComponent<TableView>();
            var match = (OfflineMatch)typeof(TableView).GetField("match", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var pile = match.Game.Players[0].Achieved;
            pile.Add(match.Game.Deck[0]);
            pile.Add(match.Game.Deck[1]);
            pile.Add(match.Game.Deck[2]);
            Show(view);
            var record = host.transform.Find("Root/Frame/seat0/achieved-cards");
            Assert.AreEqual(3, record.childCount);
            for (var i = 1; i < record.childCount; i++)
            {
                var prev = record.GetChild(i - 1) as RectTransform;
                var card = record.GetChild(i) as RectTransform;
                Assert.AreEqual(prev.anchoredPosition.x + 3f, card.anchoredPosition.x, 0.01f);
            }
        }

        [Test]
        public void PendingBonusCoinSitsOnTheQuotaCardUntilTheSetIsAchieved()
        {
            host = Open();
            Set("seedText", "1");
            Click("対局開始");
            var view = host.GetComponent<TableView>();
            var match = (OfflineMatch)typeof(TableView).GetField("match", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var player = match.Game.Players[0];
            player.Quota = new Card(9001, Suit.H, 7);
            player.Collection.Clear();
            player.Collection.Add(new Card(9002, Suit.H, 2));
            player.Collection.Add(new Card(9003, Suit.H, 4));
            player.Collection.Add(new Card(9004, Suit.H, 6));
            player.Collection.Add(new Card(9005, Suit.H, 1));
            player.Collection.Add(new Card(9006, Suit.H, 5));
            player.Collection.Add(new Card(9007, Suit.H, 3));
            Show(view);

            var quota = host.transform.Find("Root/Frame/seat0/quota-cards/card9001");
            Assert.IsNotNull(quota);
            Assert.IsNotNull(quota.Find("coin"));
            Assert.IsNull(host.transform.Find("Root/Frame/seat0/quota-cards/card9007/coin"));
            Assert.IsNull(host.transform.Find("Root/Frame/seat0/chip-tray/coin"));

            player.Achieved.Add(player.Quota);
            player.Achieved.AddRange(player.Collection);
            player.Quota = null;
            player.Collection.Clear();
            Show(view);
            Assert.IsNull(host.transform.Find("Root/Frame/seat0/quota-cards/card9007"));
            Assert.IsNotNull(host.transform.Find("Root/Frame/seat0/chip-tray/coin"));
        }

        [Test]
        public void FinishedTableReviewHidesTrayCoinsAndExitsWithLeave()
        {
            host = Open();
            Set("seedText", "1");
            Click("対局開始");
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            var player = match.Game.Players[0];
            player.Achieved.Add(new Card(9101, Suit.H, 7));
            match.Game.RoundIndex = 2;
            match.Game.RoundEndReason = "DECK";
            var order = new List<int>();
            var scores = new Dictionary<int, int>();
            var previous = new Dictionary<int, int>();
            var rounds = new Dictionary<int, int>();
            for (var i = 0; i < match.Game.Players.Count; i++)
            {
                order.Add(i);
                scores[i] = match.Game.Players.Count - i;
                previous[i] = scores[i] - 1;
                rounds[i] = 1;
            }
            typeof(TableView).GetField("reviewOrder", flags).SetValue(view, order);
            typeof(TableView).GetField("reviewScores", flags).SetValue(view, scores);
            typeof(TableView).GetField("previousScores", flags).SetValue(view, previous);
            typeof(TableView).GetField("roundScores", flags).SetValue(view, rounds);
            typeof(TableView).GetMethod("ShowReview", flags).Invoke(view, null);
            Rebuild(host);

            Assert.IsNull(host.transform.Find("Root/Frame/seat0/chip-tray/coin"));
            Assert.IsNotNull(FindText("第2ラウンド終了"));
            Assert.IsNotNull(FindText("山札切れでラウンド終了。"));
            Assert.IsNotNull(FindText("最終順位"));
            Assert.IsNotNull(FindText("2+1=3"));
            var panel = host.transform.Find("Root/Frame/ceremony") as RectTransform;
            Assert.AreEqual(640f, -panel.anchoredPosition.y, 1f);
            Assert.AreEqual(640f, panel.sizeDelta.y, 1f);
            var leave = panel.Find("抜ける") as RectTransform;
            Assert.IsNotNull(leave);
            Assert.AreEqual((panel.sizeDelta.x - leave.sizeDelta.x) * 0.5f, leave.anchoredPosition.x, 1f);
            Assert.AreEqual(-(panel.sizeDelta.y - 80f), leave.anchoredPosition.y, 1f);
            Assert.IsNull(FindButton("ゲームを終了"));
            Click("抜ける");
            Assert.IsNotNull(ButtonNamed("対局開始"));
        }

        [Test]
        public void AbandonConfirmAttachesToTheLiveSeat()
        {
            host = Open();
            Set("seedText", "0");
            Click("対局開始");
            var view = host.GetComponent<TableView>();
            var frame = host.transform.Find("Root/Frame");
            var live = frame.Find("seat0");
            var stale = new GameObject("seat0", typeof(RectTransform));
            stale.transform.SetParent(frame, false);
            stale.transform.SetAsFirstSibling();
            Set("confirm", "abandon");
            typeof(TableView).GetMethod("Confirm", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);

            Assert.IsNotNull(live.Find("confirm"));
            Assert.IsNull(stale.transform.Find("confirm"));
            Object.DestroyImmediate(stale);
        }

        [Test]
        public void AbandonPassNextAndLeaveAskBeforeActing()
        {
            host = Open();
            Set("seedText", "0");
            Click("対局開始");
            var view = host.GetComponent<TableView>();
            var match = (OfflineMatch)typeof(TableView).GetField("match", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var game = match.Game;
            var card = game.Market.Find(item => item.Suit != Suit.Joker && item.Rank != 1);
            game.Market.Remove(card);
            game.Players[0].Quota = card;
            Show(view);

            Click("放棄");
            AssertConfirmInside("seat0");
            Assert.IsNotNull(FindText("本当に放棄しますか？"));
            Click("キャンセル");
            Assert.IsNull(FindText("本当に放棄しますか？"));

            Click("パス");
            AssertConfirmInside("seat0");
            Assert.IsNotNull(FindText("本当にパスしますか？"));
            Click("キャンセル");

            game.TurnGain = true;
            Show(view);
            Click("次へ");
            AssertConfirmInside("seat0");
            Assert.IsNotNull(FindText("本当に次へ進みますか？"));
            Click("キャンセル");

            game.TurnGain = false;
            for (var i = 0; i < game.Market.Count; i++)
            {
                var item = game.Market[i];
                if (item != null && (item.Suit == game.Players[0].Quota.Suit || item.Suit == Suit.Joker))
                    game.Market[i] = null;
            }
            Show(view);
            Click("パス");
            Assert.IsNull(FindText("本当にパスしますか？"));
            Assert.AreNotEqual(0, game.Current);

            Click("ゲームから抜ける");
            AssertConfirmInside("seat0");
            Assert.IsNotNull(FindText("本当にゲームから抜けますか？"));
            Assert.IsNotNull(ButtonNamed("抜ける"));
            Click("キャンセル");
            Assert.IsNotNull(ButtonNamed("ゲームから抜ける"));
        }

        [Test]
        public void NextRoundClearsTheRoundEndWindow()
        {
            host = Open();
            Set("seedText", "0");
            Click("対局開始");
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            match.Game.RoundCount = 3;
            match.Game.RoundIndex = 2;
            match.Game.AwaitingNextRound = false;
            typeof(TableView).GetField("ceremonyDialog", flags).SetValue(view, true);
            typeof(TableView).GetField("ceremonyHeading", flags).SetValue(view, "第1ラウンド終了（山札切れ）");
            Show(view);
            Assert.IsNull(FindText("第1ラウンド終了（山札切れ）"));
            Assert.IsNotNull(FindText("第2ラウンド / 3"));
        }

        [Test]
        public void RoundEndDialogListsTheReasonAndTitlesInsideANarrowWindow()
        {
            host = Open();
            Set("seedText", "0");
            Click("対局開始");
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            var game = match.Game;
            game.Config.TitleRule = true;
            game.RoundCount = 3;
            game.RoundIndex = 2;
            game.AwaitingNextRound = true;
            game.RoundEndReason = "DECK";
            var leader = game.TurnOrder[(game.RoundIndex - 1) % game.TurnOrder.Count];
            var orderStart = game.TurnOrder.IndexOf(leader);
            var expected = new List<int>();
            for (var i = 0; i < game.TurnOrder.Count; i++) expected.Add(game.TurnOrder[(orderStart + i) % game.TurnOrder.Count]);
            AwardThree(game.Players[expected[0]]);
            AwardThree(game.Players[expected[1]]);
            var broken = game.Players[expected[2]];
            broken.Bundles.Add(new Bundle("S", false));
            broken.Bundles.Add(new Bundle("H", true));
            game.Players[leader].Achieved.Add(new Card(9001, Suit.S, 9));
            typeof(TableView).GetMethod("PrepareCeremony", flags).Invoke(view, new object[] { game });
            var order = (List<int>)typeof(TableView).GetField("dialogOrder", flags).GetValue(view);
            CollectionAssert.AreEqual(expected, order);
            Assert.AreEqual("第2ラウンド終了", typeof(TableView).GetField("ceremonyHeading", flags).GetValue(view));
            Assert.AreEqual("山札切れでラウンド終了。", typeof(TableView).GetField("ceremonyReason", flags).GetValue(view));
            Assert.IsNull(typeof(TableView).GetField("ceremonyPlaces", flags).GetValue(view));
            var scores = (Dictionary<int, int>)typeof(TableView).GetField("scoreOverride", flags).GetValue(view);
            Assert.AreEqual(9, scores[leader]);
            typeof(TableView).GetField("ceremonyDialog", flags).SetValue(view, true);
            typeof(TableView).GetField("ceremonyReasonShown", flags).SetValue(view, true);
            Show(view);
            Assert.IsNotNull(FindText("第2ラウンド終了"));
            Assert.IsNull(FindText("第2ラウンド終了（山札切れ）"));
            Assert.IsNotNull(FindText("山札切れでラウンド終了。"));
            var panel = host.transform.Find("Root/Frame/ceremony") as RectTransform;
            Assert.IsNotNull(panel);
            Assert.AreEqual(460f, panel.sizeDelta.x, 0.1f);
            Assert.AreEqual(189f, panel.sizeDelta.y, 0.1f);
            Assert.AreEqual(180.5f, -panel.anchoredPosition.y, 0.1f);
            Assert.LessOrEqual(panel.anchoredPosition.x + panel.sizeDelta.x, 770f);
            var compactLeft = panel.anchoredPosition.x;
            var compactRight = panel.anchoredPosition.x + panel.sizeDelta.x;
            var tray = host.transform.Find("Root/Frame/seat0/chip-tray") as RectTransform;
            Assert.IsNotNull(tray);
            Assert.GreaterOrEqual(tray.anchoredPosition.x, compactRight);
            Assert.AreEqual(0, CountRows(panel));
            Assert.IsNull(panel.Find("bonus0"));
            AssertHeadingFont(panel, 36);
            for (var i = 0; i < game.Players.Count; i++)
            {
                var chip = host.transform.Find("Root/Frame/seat" + i + "/chip-tray") as RectTransform;
                var names = host.transform.Find("Root/Frame/seat" + i + "/title-names") as RectTransform;
                Assert.IsNotNull(names);
                Assert.IsNull(chip.Find("title-mono"));
                Assert.LessOrEqual(-names.anchoredPosition.y + names.sizeDelta.y, -chip.anchoredPosition.y);
                var mono = names.Find("title-mono").GetComponent<Text>();
                var purist = names.Find("title-purist").GetComponent<Text>();
                Assert.AreEqual("単色達成", mono.text);
                Assert.AreEqual("生粋の買い付け", purist.text);
                var struck = i == expected[2];
                if (struck) Assert.AreEqual(0.541f, mono.color.r, 0.02f);
                else Assert.AreEqual(Color.black, mono.color);
                Assert.AreEqual(mono.color, purist.color);
                Assert.AreEqual(struck, names.Find("title-mono-strike") != null);
                Assert.AreEqual(struck, names.Find("title-purist-strike") != null);
            }
            typeof(TableView).GetField("ceremonyExpand", flags).SetValue(view, 1f);
            typeof(TableView).GetField("ceremonyButton", flags).SetValue(view, "OK");
            Show(view);
            panel = host.transform.Find("Root/Frame/ceremony") as RectTransform;
            Assert.LessOrEqual(panel.anchoredPosition.x, compactLeft);
            Assert.AreEqual(compactRight, panel.anchoredPosition.x + panel.sizeDelta.x, 0.1f);
            Assert.AreEqual(640f, -panel.anchoredPosition.y, 1f);
            Assert.AreEqual(640f, panel.sizeDelta.y, 1f);
            var ok = panel.Find("OK") as RectTransform;
            Assert.IsNotNull(ok);
            Assert.AreEqual((panel.sizeDelta.x - ok.sizeDelta.x) * 0.5f, ok.anchoredPosition.x, 1f);
            Assert.AreEqual(-(panel.sizeDelta.y - 80f), ok.anchoredPosition.y, 1f);
            var rows = new List<RectTransform>();
            for (var i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (child.name.StartsWith("row")) rows.Add((RectTransform)child);
            }
            rows.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));
            Assert.AreEqual(expected.Count, rows.Count);
            for (var i = 0; i < expected.Count; i++) Assert.AreEqual("row" + expected[i], rows[i].name);
            var slots = new List<int>();
            for (var i = 0; i < expected.Count; i++) slots.Add(i + 1);
            typeof(TableView).GetField("ceremonyRankSlots", flags).SetValue(view, slots);
            var swapped = new List<int>(expected);
            swapped.Reverse();
            typeof(TableView).GetField("dialogOrder", flags).SetValue(view, swapped);
            Show(view);
            panel = host.transform.Find("Root/Frame/ceremony") as RectTransform;
            rows.Clear();
            for (var i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (child.name.StartsWith("row")) rows.Add((RectTransform)child);
            }
            rows.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));
            Assert.AreEqual("row" + swapped[0], rows[0].name);
            Assert.AreEqual("1位", rows[0].Find("rank").GetComponent<Text>().text);
            Assert.AreEqual(game.Players[swapped[0]].Name, rows[0].Find("mover").GetComponentInChildren<Text>().text);
            Assert.AreEqual(expected.Count + "位", rows[rows.Count - 1].Find("rank").GetComponent<Text>().text);
            Assert.IsNull(panel.Find("bonus0"));
            AssertHeadingFont(panel, 36);
            game.RoundEndReason = "STALL";
            typeof(TableView).GetMethod("PrepareCeremony", flags).Invoke(view, new object[] { game });
            Assert.AreEqual("第2ラウンド終了", typeof(TableView).GetField("ceremonyHeading", flags).GetValue(view));
            Assert.AreEqual("膠着の連続でラウンド終了。", typeof(TableView).GetField("ceremonyReason", flags).GetValue(view));
        }

        static int CountRows(Transform panel)
        {
            var count = 0;
            for (var i = 0; i < panel.childCount; i++)
                if (panel.GetChild(i).name.StartsWith("row")) count++;
            return count;
        }

        static void AssertHeadingFont(Transform panel, int minimum)
        {
            Text heading = null;
            for (var i = 0; i < panel.childCount; i++)
            {
                var label = panel.GetChild(i).GetComponent<Text>();
                if (label != null && label.text == "第2ラウンド終了") heading = label;
            }
            Assert.IsNotNull(heading);
            Assert.GreaterOrEqual(heading.fontSize, minimum);
        }

        static void AwardThree(Player player)
        {
            player.Bundles.Add(new Bundle("S", false));
            player.Bundles.Add(new Bundle("S", false));
            player.Bundles.Add(new Bundle("S", false));
        }

        [Test]
        public void LeaveReturnsToSetupAndStopsTheCpuLoop()
        {
            host = Open();
            Set("seedText", "0");
            Click("対局開始");
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            Assert.IsTrue(match.Game.Players[0].IsHuman);
            var ticket = (int)typeof(TableView).GetField("cpuRun", flags).GetValue(view);
            Click("ゲームから抜ける");
            AssertConfirmInside("seat0");
            Click("抜ける");
            Assert.IsNull(match.Game);
            Assert.IsNull(FindText("本当にゲームから抜けますか？"));
            Assert.IsNotNull(ButtonNamed("対局開始"));
            var run = (IEnumerator)typeof(TableView).GetMethod("RunCpus", flags).Invoke(view, new object[] { ticket });
            Drive(run);
            Assert.IsNull(match.Game);
            Assert.IsNotNull(ButtonNamed("対局開始"));
        }

        [Test]
        public void CpuOnlyLeaveDialogStaysOnTheTableUntilThePlayerLeaves()
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            match.Begin(new GameConfig
            {
                NumPlayers = 3,
                Seed = 1,
                Names = new List<string> { "北", "東", "南" },
                HumanSeats = new List<int>(),
                Rounds = 1,
            }, pumpCpus: false);
            Set("confirm", "leave");
            Show(view);
            Assert.IsNotNull(host.transform.Find("Root/Frame/confirm"));
            Assert.IsNull(host.transform.Find("Root/Frame/seat0/confirm"));
            match.Game.Current = (match.Game.Current + 1) % match.Game.Players.Count;
            Show(view);
            Assert.IsNotNull(host.transform.Find("Root/Frame/confirm"));
            Assert.IsNull(host.transform.Find("Root/Frame/seat" + match.Game.Current + "/confirm"));
            Click("抜ける");
            Assert.IsNull(match.Game);
            Assert.IsNotNull(ButtonNamed("対局開始"));
        }

        [Test]
        public void PartialCollectKeepsTheTurnAndShowsNext()
        {
            host = Open();
            Set("seedText", "0");
            Click("対局開始");
            var view = host.GetComponent<TableView>();
            var match = (OfflineMatch)typeof(TableView).GetField("match", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var game = match.Game;
            var player = game.Players[game.Current];
            player.Quota = new Card(9001, Suit.H, 7);
            player.Collection.Clear();
            game.Market.Clear();
            game.Market.Add(new Card(9101, Suit.H, 3));
            game.Market.Add(new Card(9102, Suit.H, 4));
            game.Step(new Collect(new[] { 9101 }));
            Assert.IsTrue(match.IsHumanTurn);
            Assert.IsTrue(game.TurnGain);
            Assert.AreEqual(1, player.Collection.Count);

            Set("cpuRun", 1);
            var run = (IEnumerator)typeof(TableView).GetMethod("RunCpus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, new object[] { 1 });
            Drive(run);

            Assert.IsNotNull(FindButton("次へ"));
            Assert.IsNotNull(FindButton("放棄"));
            var second = host.transform.Find("Root/Frame/card9102");
            Assert.IsNotNull(second);
            Assert.IsNotNull(second.GetComponent<Button>());
        }

        static void Drive(IEnumerator enumerator)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(enumerator);
            var guard = 0;
            while (stack.Count > 0)
            {
                if (++guard > 10000) Assert.Fail("coroutine did not finish");
                var current = stack.Peek();
                if (!current.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (current.Current is IEnumerator nested) stack.Push(nested);
            }
        }

        GameObject Open()
        {
            var viewHost = new GameObject("Quota");
            var view = viewHost.AddComponent<TableView>();
            typeof(TableView).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, null);
            typeof(TableView).GetMethod("DismissSplash", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, null);
            var canvas = viewHost.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            viewHost.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var canvasRect = viewHost.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(980, 800);
            canvasRect.localScale = Vector3.one;
            Rebuild(viewHost);
            return viewHost;
        }

        void Click(string caption)
        {
            ButtonNamed(caption).onClick.Invoke();
            Rebuild(host);
        }

        void AssertConfirmInside(string seatName)
        {
            var panel = host.transform.Find("Root/Frame/" + seatName + "/confirm") as RectTransform;
            Assert.IsNotNull(panel, seatName);
            Assert.IsNull(host.transform.Find("Root/Frame/confirm"));
            Assert.GreaterOrEqual(panel.anchoredPosition.x, 25f);
            Assert.LessOrEqual(panel.anchoredPosition.x + panel.sizeDelta.x, 1055f);
            var top = -panel.anchoredPosition.y;
            Assert.GreaterOrEqual(top, 25f);
            Assert.LessOrEqual(top + panel.sizeDelta.y, 365f);
        }

        Button ButtonNamed(string caption)
        {
            var button = FindButton(caption);
            Assert.IsNotNull(button, "missing button " + caption);
            return button;
        }

        Button FindButton(string caption)
        {
            foreach (var button in host.GetComponentsInChildren<Button>())
            {
                var label = button.GetComponentInChildren<Text>();
                if (label != null && label.text == caption) return button;
            }
            return null;
        }

        Text FindText(string caption)
        {
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text == caption) return label;
            return null;
        }

        void Set(string field, object value)
        {
            typeof(TableView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host.GetComponent<TableView>(), value);
        }

        void Show(TableView view)
        {
            typeof(TableView).GetMethod("ShowTable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            Rebuild(host);
        }

        static void Rebuild(GameObject viewHost)
        {
            var frame = viewHost.transform.Find("Root/Frame") as RectTransform;
            if (frame != null) LayoutRebuilder.ForceRebuildLayoutImmediate(frame);
            Canvas.ForceUpdateCanvases();
        }

        static float WorldTop(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
        }

        static float WorldBottom(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
        }

        static float WorldLeft(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
        }

        static float WorldRight(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
        }

        static float WorldMidX(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var left = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            var right = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            return (left + right) * 0.5f;
        }

        static float WorldMidY(RectTransform rect)
        {
            return (WorldTop(rect) + WorldBottom(rect)) * 0.5f;
        }
    }
}

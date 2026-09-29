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
            var sawSimple = false;
            foreach (var button in buttons)
            {
                var caption = button.GetComponentInChildren<Text>();
                Assert.Greater(caption.rectTransform.rect.height, 16f, caption.text);
                Assert.Greater(caption.rectTransform.rect.width, 40f, caption.text);
                Assert.IsFalse(caption.text.Contains("アイテムセット"), caption.text);
                Assert.IsFalse(caption.text.Contains("並び順ボーナス"), caption.text);
                Assert.IsFalse(caption.text.Contains("称号ボーナス"), caption.text);
                Assert.IsFalse(caption.text.Contains("特殊アクション"), caption.text);
                if (caption.text.StartsWith("シンプルモード")) sawSimple = true;
            }
            Assert.IsTrue(sawSimple);

            Text title = null;
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text == "QUOTA") title = label;
            Assert.IsNotNull(title);
            Assert.Greater(title.rectTransform.rect.height, 20f);
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
    }
}

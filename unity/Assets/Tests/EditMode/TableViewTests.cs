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
            }

            Text title = null;
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text == "QUOTA") title = label;
            Assert.IsNotNull(title);
            Assert.Greater(title.rectTransform.rect.height, 20f);
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
        public void SpecialButtonsSitLeftOfPassAndDeclareWithoutAsking()
        {
            host = Open();
            Set("specialRule", true);
            Set("seedText", "0");
            Click("対局開始");

            var controls = host.transform.Find("Root/Frame/controls");
            Assert.IsNotNull(controls);
            var labels = new System.Collections.Generic.List<string>();
            for (var i = 0; i < controls.childCount; i++)
            {
                var caption = controls.GetChild(i).GetComponentInChildren<Text>();
                labels.Add(caption.text);
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
            Assert.IsNotNull(FindText("本当に放棄しますか？"));
            Click("キャンセル");
            Assert.IsNull(FindText("本当に放棄しますか？"));

            Click("パス");
            Assert.IsNotNull(FindText("本当にパスしますか？"));
            Click("キャンセル");

            game.TurnGain = true;
            Show(view);
            Click("次へ");
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

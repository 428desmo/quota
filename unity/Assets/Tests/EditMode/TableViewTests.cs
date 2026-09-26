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

            var content = host.transform.Find("Root/Scroll/Viewport/Content") as RectTransform;
            var viewport = content.parent as RectTransform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();

            Assert.AreEqual(0f, content.sizeDelta.x, 0.01f);
            Assert.AreEqual(viewport.rect.width, content.rect.width, 1f);

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
            var min = viewport.InverseTransformPoint(title.rectTransform.TransformPoint(title.rectTransform.rect.min));
            Assert.GreaterOrEqual(min.x, viewport.rect.xMin - 1f);
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

            var controls = host.transform.Find("Root/Scroll/Viewport/Content/controls");
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
            Assert.IsNull(host.transform.Find("Root/Scroll/Viewport/Content/controls/ダブル"));
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

            Click("最初の画面に戻る");
            Assert.IsNotNull(FindText("本当にゲームから抜けますか？"));
            Assert.IsNotNull(ButtonNamed("抜ける"));
            Click("キャンセル");
            Assert.IsNotNull(ButtonNamed("最初の画面に戻る"));
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
            var content = viewHost.transform.Find("Root/Scroll/Viewport/Content") as RectTransform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();
        }
    }
}

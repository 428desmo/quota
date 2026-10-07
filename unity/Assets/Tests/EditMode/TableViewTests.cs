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
        public void FourCpuNetworkTableStartsWithoutHumanSeats()
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var json = "{\"phase\":\"playing\",\"table_id\":\"test\",\"players\":4,\"seed\":1,\"seats\":[],\"cpus\":[\"A\",\"B\",\"C\",\"D\"],\"cpu_cast\":[0,1,2,3],\"you\":{\"seat\":-1,\"observer\":true,\"leader\":true},\"options\":{\"simple\":true},\"actions\":[]}";
            typeof(TableView).GetMethod("ApplyNetworkState", flags).Invoke(view, new object[] { json });
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            Assert.AreEqual(4, game.Players.Count);
            foreach (var player in game.Players) Assert.IsFalse(player.IsHuman);
            game.AwaitingNextRound = true;
            typeof(TableView).GetMethod("ShowCompletedCeremony", flags).Invoke(view, null);
            Assert.IsNotNull(FindText("OK"));
            Assert.IsFalse((bool)typeof(TableView).GetField("ceremonyRunning", flags).GetValue(view));
            Assert.IsTrue((bool)typeof(TableView).GetField("ceremonyDialog", flags).GetValue(view));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void RoundPanelStartsWithEachNetworkViewersOwnPlayerAndNoScores(int viewer)
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var json = "{\"phase\":\"playing\",\"table_id\":\"test\",\"players\":3,\"seed\":1,\"seats\":[{\"name\":\"A\"},{\"name\":\"B\"}],\"cpus\":[\"CPU\"],\"cpu_cast\":[0],\"you\":{\"seat\":" + viewer + ",\"leader\":false},\"options\":{\"simple\":true},\"actions\":[]}";
            typeof(TableView).GetMethod("ApplyNetworkState", flags).Invoke(view, new object[] { json });
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            typeof(TableView).GetMethod("PrepareCeremony", flags).Invoke(view, new object[] { game });
            var order = (List<int>)typeof(TableView).GetField("dialogOrder", flags).GetValue(view);
            Assert.AreEqual(viewer, order[0]);
            var displayed = (List<int>)typeof(TableView).GetMethod("DisplayRows", flags).Invoke(view, new object[] { game });
            CollectionAssert.AreEqual(displayed, order);
            var figure = typeof(TableView).GetMethod("FigureText", flags);
            foreach (var seat in order) Assert.AreEqual("", figure.Invoke(view, new object[] { seat }));
            Set("ceremonyNamesOnly", false);
            foreach (var seat in order) Assert.AreEqual("0", figure.Invoke(view, new object[] { seat }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayingScoreSitsBelowAchievementsAndSpecialCardsShowTheirUsedSide(bool wide)
        {
            host = Open();
            Set("widePreview", wide);
            Set("seedText", "0");
            Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            game.Config.SpecialActionsRule = true;
            game.Players[0].DoubleActionLeft = 1;
            game.Players[0].ReshuffleTakeLeft = 0;
            Show(view);
            var seat = host.transform.Find("Root/Frame/seat0");
            var score = seat.Find("score").GetComponent<Text>();
            Assert.AreEqual("0", score.text);
            Assert.GreaterOrEqual(score.fontSize, 30);
            Assert.AreEqual("ダブル", seat.Find("double-card").GetComponentInChildren<Text>().text);
            Assert.AreEqual("USED", seat.Find("reshuffle-card").GetComponentInChildren<Text>().text);
            var label = System.Array.Find(seat.GetComponentsInChildren<Text>(), t => t.text == "実績");
            Assert.Less(((RectTransform)score.transform).anchoredPosition.y, ((RectTransform)label.transform).anchoredPosition.y);
            Assert.Less(((RectTransform)score.transform).anchoredPosition.x, wide ? 290f : 130f);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QuotaSpacingAndPreviousRoundScoreFollowTheBoardLayout(bool wide)
        {
            host = Open(); Set("widePreview", wide); Set("seedText", "0"); Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            var player = game.Players[0];
            player.Quota = new Card(9100, Suit.H, 13);
            player.Collection.Clear();
            for (var i = 0; i < 11; i++) player.Collection.Add(new Card(9101 + i, Suit.H, 2));
            player.Achieved.Clear(); player.Bundles.Clear(); player.Score = 87;
            game.RoundIndex = 2; game.RoundCount = 3;
            game.StallFlag = true; game.NoGainStreak = 1;
            Show(view);
            var seat = host.transform.Find("Root/Frame/seat0");
            var area = seat.Find("quota-cards");
            var first = (RectTransform)area.Find("card9100");
            var second = (RectTransform)area.Find("card9101");
            var third = (RectTransform)area.Find("card9102");
            Assert.AreEqual(70f, second.anchoredPosition.x - first.anchoredPosition.x);
            Assert.AreEqual(40f, third.anchoredPosition.x - second.anchoredPosition.x);
            Assert.AreEqual("87+", seat.Find("previous-score").GetComponent<Text>().text);
            Assert.AreEqual("0", seat.Find("score").GetComponent<Text>().text);
            var score = seat.Find("score").GetComponent<Text>();
            var previous = seat.Find("previous-score").GetComponent<Text>();
            var progress = seat.Find("quota-progress").GetComponent<Text>();
            Assert.AreEqual(48, score.fontSize); Assert.AreEqual(21, previous.fontSize);
            Assert.AreEqual(48, progress.fontSize); Assert.IsTrue(progress.text.Contains("<size=21>/13</size>"));
            foreach (var text in new[] { score, previous, progress }) Assert.AreEqual(FontStyle.Bold, text.fontStyle);
            Assert.AreEqual(((RectTransform)score.transform).anchoredPosition.x, ((RectTransform)previous.transform).anchoredPosition.x);
            Assert.AreEqual(wide ? 960f : 540f, ((RectTransform)seat.Find("nameplate")).rect.width);
            var caption = typeof(TableView).GetMethod("PlayerCaption", BindingFlags.Instance | BindingFlags.NonPublic);
            var leader = game.TurnOrder[(game.RoundIndex - 1) % game.TurnOrder.Count];
            Assert.AreEqual("①" + game.Players[leader].Name, caption.Invoke(host.GetComponent<TableView>(), new object[] { game, leader }));
            Assert.IsNotNull(FindText("ラウンド 2/3"));
            var frame = host.transform.Find("Root/Frame");
            Assert.AreEqual(new Color32(255,211,38,255), (Color32)frame.Find("stall-dot-0").GetComponent<Image>().color);
            Assert.AreEqual(new Color32(230,66,53,255), (Color32)frame.Find("stall-dot-3").GetComponent<Image>().color);
            Assert.AreEqual(new Color32(232,232,232,255), (Color32)frame.Find("stall-dot-4").GetComponent<Image>().color);
            Assert.AreEqual(FontStyle.Bold, ButtonNamed("EXIT").GetComponentInChildren<Text>().fontStyle);
        }

        [Test]
        public void LeaveButtonSurvivesCpuRedrawAndDisappearsAfterLeaving()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
            var view = host.GetComponent<TableView>();
            var button = ButtonNamed("EXIT");
            var frame = host.transform.Find("Root/Frame") as RectTransform;
            var corners = new Vector3[4];
            frame.GetWorldCorners(corners);
            var buttonRect = (RectTransform)button.transform;
            var expected = corners[1] + new Vector3(frame.rect.width - 328f, -8f, 0f) * frame.localScale.x;
            Assert.Less(Vector3.Distance(expected, buttonRect.position), 0.1f);
            Show(view);
            Assert.AreSame(button, ButtonNamed("EXIT"));
            Click("EXIT");
            Click("抜ける");
            Assert.IsNull(host.transform.Find("leave-button"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CpuOnlySpectatorLeavesImmediatelyEvenDuringRoundScoring(bool duringScoring)
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            match.Begin(new GameConfig { NumPlayers = 3, Seed = 0, HumanSeats = new List<int>(), Rounds = 3 }, pumpCpus: false);
            if (duringScoring)
            {
                match.Game.AwaitingNextRound = true;
                typeof(TableView).GetMethod("PrepareCeremony", flags).Invoke(view, new object[] { match.Game });
                Set("ceremonyRunning", true);
                Set("ceremonyDialog", true);
            }
            Set("busy", true);
            Show(view);
            var ticket = (int)typeof(TableView).GetField("cpuRun", flags).GetValue(view);
            ButtonNamed("EXIT").onClick.Invoke();
            Assert.IsNull(match.Game);
            Assert.IsNull(typeof(TableView).GetField("confirm", flags).GetValue(view));
            Assert.IsTrue((bool)typeof(TableView).GetField("onSetup", flags).GetValue(view));
            Assert.IsFalse((bool)typeof(TableView).GetField("busy", flags).GetValue(view));
            Assert.IsFalse((bool)typeof(TableView).GetField("ceremonyRunning", flags).GetValue(view));
            Assert.Greater((int)typeof(TableView).GetField("cpuRun", flags).GetValue(view), ticket);
        }

        [TestCase(true, -1, true)]
        [TestCase(false, -1, true)]
        [TestCase(false, 0, false)]
        public void NetworkSpectatingUsesOwnParticipationStatus(bool observer, int seat, bool expected)
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var json = "{\"phase\":\"playing\",\"table_id\":\"test\",\"players\":3,\"seed\":1,\"seats\":[{\"name\":\"A\"}],\"cpus\":[\"CPU1\",\"CPU2\"],\"cpu_cast\":[0,1],\"you\":{\"seat\":" + seat + ",\"observer\":" + (observer ? "true" : "false") + "},\"options\":{\"simple\":true},\"actions\":[]}";
            typeof(TableView).GetMethod("ApplyNetworkState", flags).Invoke(view, new object[] { json });
            Assert.AreEqual(expected, typeof(TableView).GetProperty("IsSpectating", flags).GetValue(view));
        }

        [Test]
        public void PortraitMarketUsesTheSuppliedCardDimensionsAndDeckPosition()
        {
            host = Open(); Set("seedText", "0"); Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            var frame = host.transform.Find("Root/Frame");
            var card = frame.Find("card" + game.Market[0].Id) as RectTransform;
            Assert.AreEqual(144f, card.rect.width, 0.01f);
            Assert.AreEqual(200f, card.rect.height, 0.01f);
            Assert.AreEqual(new Vector2(24f, -158f), card.anchoredPosition);
            Assert.AreEqual(112f, ((RectTransform)card.Find("suit")).rect.width, 0.01f);
            var stroke = card.Find("face").GetComponent<Image>();
            Assert.AreEqual(0f, stroke.sprite.texture.GetPixel(4, 4).a, 0.01f);
            var deck = frame.Find("deck") as RectTransform;
            Assert.AreEqual(new Vector2(912f, 68f), deck.anchoredPosition);
            Assert.IsNotNull(deck.Find("lower")); Assert.IsNotNull(deck.Find("upper").GetComponent<Image>().sprite);
            game.Deck.Clear(); game.Deck.Add(new Card(9999, Suit.H, 2)); Show(view);
            deck = host.transform.Find("Root/Frame/deck") as RectTransform;
            Assert.IsNull(deck.Find("lower")); Assert.IsNotNull(deck.Find("upper"));
            game.Deck.Clear(); Show(view);
            deck = host.transform.Find("Root/Frame/deck") as RectTransform;
            Assert.IsNull(deck.Find("upper")); Assert.AreEqual("0", deck.Find("remaining").GetComponent<Text>().text);
        }

        [TestCase(42, 39, 22, 1, 2, 3)]
        [TestCase(42, 22, 22, 1, 2, 2)]
        [TestCase(42, 42, 22, 1, 1, 3)]
        [TestCase(22, 22, 22, 1, 1, 1)]
        public void CeremonyKeepsEquationAndCompetitionRanks(int a, int b, int c, int ra, int rb, int rc)
        {
            host = Open(); Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Set("dialogOrder", new List<int> { 0, 1, 2 });
            var rounds = new Dictionary<int, int> { [0] = a, [1] = b, [2] = c };
            Set("scoreOverride", rounds);
            Set("roundScores", rounds);
            Set("previousScores", new Dictionary<int, int> { [0] = 26, [1] = 36, [2] = 26 });
            Set("ceremonyNamesOnly", false);
            typeof(TableView).GetMethod("AssignPlaces", flags).Invoke(view, new object[] { (System.Func<int, int>)(seat => rounds[seat]) });
            var places = (Dictionary<int, int>)typeof(TableView).GetField("ceremonyPlaces", flags).GetValue(view);
            CollectionAssert.AreEqual(new[] { ra, rb, rc }, new[] { places[0], places[1], places[2] });
            Set("plusOverride", rounds);
            var figure = typeof(TableView).GetMethod("FigureText", flags);
            Assert.AreEqual("26 + " + a, figure.Invoke(view, new object[] { 0 }));
            Set("scoreOverride", new Dictionary<int, int> { [0] = 26 + a, [1] = 36 + b, [2] = 26 + c });
            Set("ceremonyEquation", true);
            Assert.AreEqual("26 + " + a + " = " + (26 + a), figure.Invoke(view, new object[] { 0 }));
            var box = typeof(TableView).GetMethod("CeremonyFrame", flags);
            var expanded = (Rect)box.Invoke(view, new object[] { 1f });
            var retained = (Rect)box.Invoke(view, new object[] { 0f });
            Assert.AreEqual(expanded.height, retained.height);
            Assert.AreEqual(expanded.position, retained.position);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SixTablesScrollInsideFourRowViewportAndLogosAreWhite(bool wide)
        {
            host = Open();
            Set("widePreview", wide);
            var view = host.GetComponent<TableView>();
            var entries = new List<string>();
            for (var i = 0; i < 6; i++) entries.Add("{\"id\":\"table" + i + "\",\"leader\":\"卓" + i + "\",\"players\":3,\"seated\":1,\"status\":\"募集中\"}");
            typeof(TableView).GetMethod("ApplyNetworkState", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, new object[] { "{\"phase\":\"hall\",\"tables\":[" + string.Join(",", entries) + "]}" });
            Canvas.ForceUpdateCanvases();
            var scroll = host.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(scroll);
            Assert.IsTrue(scroll.vertical);
            Assert.IsFalse(scroll.horizontal);
            Assert.IsNotNull(scroll.viewport.GetComponent<RectMask2D>());
            Assert.AreEqual(6, scroll.content.childCount);
            var frame = host.transform.Find("Root/Frame") as RectTransform;
            Assert.GreaterOrEqual(WorldBottom(FindButton("実装テスト").transform as RectTransform), WorldBottom(frame));
            var first = scroll.content.GetChild(0).GetComponent<RectTransform>();
            Assert.AreEqual(first.rect.height * 4f + 36f, scroll.viewport.rect.height, 1f);
            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            Assert.Greater(scroll.content.anchoredPosition.y, 0f);
            foreach (var logo in new[] { "title-mark", "title-catch" })
            {
                var image = host.transform.Find("Root/Frame/" + logo).GetComponent<Image>();
                Assert.AreEqual(Color.white, image.color);
                Assert.AreEqual("Quota/LogoInk", image.material.shader.name);
            }
        }

        [Test]
        public void RefillCardStaysHiddenDuringAnimationStartupFrame()
        {
            host = Open();
            Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            var type = typeof(TableView).GetNestedType("BoardFrame", BindingFlags.NonPublic);
            var capture = type.GetMethod("Capture", BindingFlags.Static | BindingFlags.Public);
            var before = capture.Invoke(null, new object[] { game });
            var original = game.Market[0];
            var refill = new Card(99123, Suit.H, 7);
            game.Market[0] = refill;
            var after = capture.Invoke(null, new object[] { game });
            typeof(TableView).GetMethod("EnqueueCards", flags).Invoke(view, new[] { before, after });
            Show(view);
            Assert.IsNotNull(host.transform.Find("Root/Frame/card" + original.Id));
            Assert.IsNull(host.transform.Find("Root/Frame/card" + refill.Id));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CompletedQuotaHasAPresentationOnlyFullCollectionStage(bool rankOne)
        {
            var game = Game.Start(new GameConfig { NumPlayers = 3, Seed = 0 });
            var seat = game.Current;
            var player = game.Players[seat];
            var card = new Card(9910, Suit.H, rankOne ? 1 : 4);
            game.Market.Clear(); game.Market.Add(card);
            if (!rankOne) { player.Quota = new Card(9911, Suit.H, 3); player.Collection.Add(new Card(9912, Suit.H, 2)); }
            var type = typeof(TableView).GetNestedType("BoardFrame", BindingFlags.NonPublic);
            var capture = type.GetMethod("Capture", BindingFlags.Static | BindingFlags.Public);
            var before = capture.Invoke(null, new object[] { game });
            game.Step(rankOne ? (GameAction)new TakeQuota(card.Id) : new Collect(new[] { card.Id }));
            var after = capture.Invoke(null, new object[] { game });
            var stage = typeof(TableView).GetMethod("CollectionStage", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { before, after, seat });
            var stagedPlayer = ((Player[])type.GetField("Players").GetValue(stage))[seat];
            var progress = typeof(TableView).GetMethod("QuotaProgress", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.AreEqual(rankOne ? "1/1" : "3/3", progress.Invoke(null, new object[] { stagedPlayer }));
            Assert.AreEqual(0, stagedPlayer.Achieved.Count);
            Assert.AreEqual(rankOne ? 1 : 3, game.Players[seat].Achieved.Count);
            Assert.IsNull(game.Players[seat].Quota, "The stage must not modify the game state.");
        }

        [Test]
        public void ActionNoticeIsOpaqueBorderedAndContainsOnlyTheAction()
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var root = (RectTransform)typeof(TableView).GetMethod("DrawActionNotice", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, new object[] { "ダブル" });
            Assert.AreEqual("ダブル", root.GetComponentInChildren<Text>().text);
            Assert.AreEqual(1f, root.GetComponent<CanvasGroup>().alpha);
            Assert.IsFalse(root.GetComponent<CanvasGroup>().blocksRaycasts);
            Assert.IsNotNull(root.Find("panel"));
        }

        [TestCase("pass", false, "パス")]
        [TestCase("pass", true, null)]
        [TestCase("abandon", false, "放棄")]
        [TestCase("double", false, "ダブル")]
        [TestCase("reshuffle", false, "配り直し")]
        [TestCase("collect:1", false, null)]
        public void OtherPlayerNoticesExcludeCardsAndPassAfterCollecting(string key, bool gained, string expected)
        {
            var method = typeof(TableView).GetMethod("NoticeFor", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.AreEqual(expected, method.Invoke(null, new object[] { key, gained }));
        }

        [TestCase("quota_goods_v1.0", "quota_goods_v1.0")]
        [TestCase("quota_goods_v1.0.json", "quota_goods_v1.0")]
        [TestCase("cpu_ranking_v1.2.json", "cpu_ranking_v1.2")]
        public void BundledTextKeepsTheVersionInItsResourceName(string name, string expected)
        {
            var method = typeof(TableView).GetMethod("BundledTextName", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.AreEqual(expected, method.Invoke(null, new object[] { name }));
        }

        [Test]
        public void NetworkStartKeepsBothHumansAndAppliesSharedActionsOnlyOnce()
        {
            host = new GameObject("Quota");
            var view = host.AddComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(TableView).GetMethod("Start", flags).Invoke(view, null);
            typeof(TableView).GetMethod("DismissSplash", flags).Invoke(view, null);
            var apply = typeof(TableView).GetMethod("ApplyNetworkState", flags);
            var json = "{\"phase\":\"playing\",\"table_id\":\"test\",\"players\":3,\"seed\":1,\"seats\":[{\"name\":\"A\"},{\"name\":\"B\"}],\"cpus\":[\"Test CPU\"],\"cpu_cast\":[0],\"you\":{\"seat\":1,\"leader\":false},\"options\":{\"simple\":true},\"actions\":[]}";
            apply.Invoke(view, new object[] { json });
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            Assert.AreEqual("A", match.Game.Players[0].Name);
            Assert.AreEqual("B", match.Game.Players[1].Name);
            Assert.IsTrue(match.Game.Players[1].IsHuman);
            Assert.IsFalse(match.Game.Players[2].IsHuman);
            var update = json.Replace("\"actions\":[]", "\"actions\":[\"pass\"]");
            apply.Invoke(view, new object[] { update });
            var turn = match.Game.TurnNumber;
            apply.Invoke(view, new object[] { update });
            Assert.AreEqual(turn, match.Game.TurnNumber);
            Assert.AreEqual(1, typeof(TableView).GetField("networkApplied", flags).GetValue(view));
            Set("networkLeaving", true);
            match.Clear();
            apply.Invoke(view, new object[] { update });
            Assert.IsNull(match.Game, "Late state responses must not restore a match while leaving.");
        }

        [Test]
        public void GuestCanConfirmLeavingAfterAcknowledgingARound()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            match.Game.RoundCount = 3;
            match.Game.AwaitingNextRound = true;
            Set("acknowledgedRound", match.Game.RoundIndex);
            Set("confirm", "leave");
            Show(view);
            Assert.IsNotNull(ButtonNamed("抜ける"));
            Assert.IsFalse((bool)typeof(TableView).GetField("ceremonyRunning", flags).GetValue(view));
            ButtonNamed("抜ける").onClick.Invoke();
            Assert.IsNull(match.Game);
        }

        [Test]
        public void GuestLobbyHidesShuffleAndExplainsWaiting()
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var apply = typeof(TableView).GetMethod("ApplyNetworkState", BindingFlags.Instance | BindingFlags.NonPublic);
            apply.Invoke(view, new object[] { "{\"phase\":\"recruiting\",\"table_id\":\"test\",\"players\":3,\"seats\":[{\"name\":\"A\"},{\"name\":\"B\"}],\"cpus\":[\"CPU\"],\"you\":{\"seat\":1,\"leader\":false}}" });
            Assert.IsNull(FindText("CPUプレイヤー入れ替え"));
            Assert.IsNotNull(FindText("リーダーがゲーム開始するのを待っています"));
        }

        [Test]
        public void TranslucentPanelsUseBlurWhileWhiteCardsStayOpaque()
        {
            host = new GameObject("GlassTest", typeof(RectTransform));
            var panel = Portrait.Box(host.transform, "panel", 0, 0, 200, 100, 7, 1,
                new Color(0.953f, 0.929f, 0.894f, 0.8f), Color.black, false);
            var fill = panel.Find("fill").GetComponent<Image>();
            Assert.AreEqual("Quota/Glass", fill.material.shader.name);
            Assert.AreEqual(0.8f, fill.color.a);
            var card = Portrait.Box(host.transform, "card", 0, 0, 100, 140, 7, 1,
                Color.white, Color.black, false);
            Assert.AreNotEqual("Quota/Glass", card.Find("fill").GetComponent<Image>().material.shader.name);
            Assert.IsTrue(Resources.Load<Shader>("Quota/Glass").isSupported);
        }

        [Test]
        public void ChipTestCreatesRenderersForInitialAndAdditionalBatches()
        {
            host = new GameObject("ChipCanvas", typeof(RectTransform), typeof(Canvas));
            host.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var state = new BonusChipLab.State();
            BonusChipLab.Open(host.transform, 1920, 1080, true, false,
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), Color.white, Color.black, state, () => {}, () => {});
            var initial = host.GetComponentInChildren<BonusChipLab>();
            Assert.IsNotNull(initial.GetComponent<CanvasRenderer>());
            // Cross the mesh-batch boundary through the actual UI callback.
            state.Add(400, 176, 148, -10);
            foreach (var button in host.GetComponentsInChildren<Button>())
                if (button.name == "＋1") { button.onClick.Invoke(); break; }
            var batches = host.GetComponentsInChildren<BonusChipLab>();
            Assert.AreEqual(6, batches.Length);
            foreach (var batch in batches)
            {
                Assert.IsNotNull(batch.GetComponent<CanvasRenderer>());
                batch.Rebuild(CanvasUpdate.PreRender);
            }
            foreach (var mask in host.GetComponentsInChildren<RectMask2D>()) mask.PerformClipping();
            Canvas.ForceUpdateCanvases();
        }

        [Test]
        public void NativeChipColorEditorAcceptsKeyboardInputAndScalesPreviews()
        {
            host = new GameObject("ChipCanvas", typeof(RectTransform), typeof(Canvas));
            var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).GetComponent<UnityEngine.EventSystems.EventSystem>();
            typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events,null);
            UnityEngine.EventSystems.EventSystem.current = events;
            var state = new BonusChipLab.State();
            BonusChipLab.Open(host.transform, 1920, 1080, true, false,
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), Color.white, Color.black, state, () => {}, () => {});
            var input = host.GetComponentInChildren<InputField>();
            foreach (var button in host.GetComponentsInChildren<Button>())
                if (button.name == "編集") button.onClick.Invoke();
            typeof(InputField).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input,null);
            Assert.IsTrue(input.isFocused);
            input.text = "";
            input.caretPosition = 0;
            foreach (var c in "#00ff00") input.ProcessEvent(new Event { type=EventType.KeyDown, character=c });
            Assert.AreEqual("#00ff00",input.text);
            input.DeactivateInputField();
            Assert.AreEqual("#00FF00",state.Rgb);
            var panel = host.transform.Find("bonus-chip-test");
            for (var scale=1;scale<=3;scale++)
                Assert.AreEqual(Vector3.one*scale,panel.Find("chip-tray-"+scale).localScale);
        }

        [TestCase(CoinKind.Purple, "FF17E2", 4, 26f, 0.70f)]
        [TestCase(CoinKind.Green, "00E749", 6, 26f, 1f)]
        [TestCase(CoinKind.Blue, "2EA3FF", 32, 22f, 1f)]
        public void GameChipUsesTheApprovedStyle(CoinKind kind, string rgb, int sides, float size, float aspect)
        {
            for (var seed=0;seed<100;seed++)
            {
                var chip=BonusChipLab.GameChip(kind,seed,true);
                Assert.AreEqual(rgb,ColorUtility.ToHtmlStringRGB(chip.Color));
                Assert.AreEqual(sides,chip.Sides);
                Assert.AreEqual(size,chip.Size);
                Assert.AreEqual(4f,chip.Thickness);
                Assert.AreEqual(aspect,chip.Aspect);
                Assert.Less(BonusChipLab.ChipBounds(chip).width,95f/3f-2f);
                Assert.AreEqual(chip.Rotation,BonusChipLab.GameChip(kind,seed,true).Rotation);
            }
        }

        [TestCase(6)]
        [TestCase(8)]
        public void QuotaChipsStayBelowTheNumberAndInsideTheExposedSeventyPixels(int count)
        {
            host = Open();
            Set("seedText", "1"); Begin();
            var view=host.GetComponent<TableView>();
            var match=(OfflineMatch)typeof(TableView).GetField("match",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            match.Game.Players[0].Quota=new Card(9100,Suit.H,13);
            match.Game.Players[0].Collection.Clear();
            match.Game.Players[0].Achieved.Clear();
            if (count == 8)
            {
                match.Game.Config.SequenceRule = true;
                for (var i = 0; i < 13; i++) match.Game.Players[0].Achieved.Add(new Card(9200+i, Suit.H, 13));
            }
            Show(view);
            var card=host.transform.Find("Root/Frame/seat0/quota-cards/card9100");
            var chips=card.GetComponentsInChildren<BonusChipLab>();
            Assert.AreEqual(count,chips.Length);
            foreach(var chip in chips)
            {
                var rect=chip.rectTransform;
                Assert.GreaterOrEqual(-rect.anchoredPosition.y,40f);
                Assert.LessOrEqual(-rect.anchoredPosition.y+rect.rect.height*rect.localScale.y,132f);
                Assert.GreaterOrEqual(rect.anchoredPosition.x,0f);
                Assert.LessOrEqual(rect.anchoredPosition.x+rect.rect.width*rect.localScale.x,70f);
            }
        }

        [Test]
        public void ChipViewShowsTheFrontEdgeFromSlightlyAboveTheTable()
        {
            var top = BonusChipLab.ProjectChipPoint(new Vector3(0, 10, 0));
            Assert.AreEqual(8.660254f, top.y, 0.0001f);
            var raised = BonusChipLab.ProjectChipPoint(new Vector3(0, 0, 4));
            Assert.AreEqual(2f, raised.y);
            Assert.IsTrue(BonusChipLab.FaceVisible(Vector3.down));
            Assert.IsFalse(BonusChipLab.FaceVisible(Vector3.up));
            Assert.IsTrue(BonusChipLab.FaceVisible(Vector3.forward));
        }

        [TestCase("#00ff00", "#00FF00")]
        [TestCase("123abc", "#123ABC")]
        public void ChipRgbAcceptsSixDigitColors(string value, string expected)
        {
            var state = new BonusChipLab.State();
            Assert.IsTrue(state.TrySetRgb(value));
            Assert.AreEqual(expected, state.Rgb);
            var selected = state.ChipColor;
            Assert.IsFalse(state.TrySetRgb("#12345Z"));
            Assert.AreEqual(selected, state.ChipColor);
            Assert.IsNotEmpty(state.ColorError);
        }

        [Test]
        public void ChipTestKeepsEarlierColorsAndAddsTheRequestedCount()
        {
            var state = new BonusChipLab.State { Shape = 2, Sides = 8 };
            state.Add(10, 220, 105, 0);
            var green = state.Chips[0].Color;
            foreach (var chip in state.Chips)
            {
                Assert.AreEqual(green, chip.Color);
                Assert.AreEqual(8, chip.Sides);
                Assert.That(chip.Position.x, Is.InRange(0f, 220f));
                Assert.That(chip.Position.y, Is.InRange(0f, 105f));
            }
            state.TrySetRgb("#0000ff");
            state.Add(50, 220, 105, 1);
            Assert.AreEqual(60, state.Chips.Count);
            Assert.AreEqual(green, state.Chips[0].Color);
            Assert.AreNotEqual(green, state.Chips[10].Color);
            Assert.AreNotEqual(state.Chips[10].Rotation, state.Chips[11].Rotation);
            state.Chips.Clear();
            Assert.AreEqual(0, state.Chips.Count);
            Assert.AreEqual("#0000FF", state.Rgb);
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
                if (label.text.StartsWith("あなたは港で働く仲買人だ。")) splash = label;
            Assert.IsNotNull(splash);
            Assert.AreEqual("あなたは港で働く仲買人だ。\n大口顧客のために、舶来の交易品を買い集めよう。\n買い付けノルマは、自分で決める。", splash.text);
            Assert.IsNotNull(host.transform.Find("Root/Frame/splash/splash-panel"));
            Color ink;
            ColorUtility.TryParseHtmlString("#2C221E", out ink);
            Assert.AreEqual(ink, splash.color);
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
            var start = ButtonNamed("新規ゲーム卓の準備").GetComponent<RectTransform>();
            Assert.AreEqual(1080f * 0.40f, guide.rect.width, 2f);
            Assert.AreEqual(guide.rect.width, rules.rect.width, 1f);
            Assert.AreEqual(guide.rect.width, hint.rect.width, 1f);
            Assert.AreEqual(1080f * 0.60f, start.rect.width, 2f);
            Assert.AreEqual(guide.rect.height * 2f, start.rect.height, 2f);
            Assert.AreEqual(Mathf.RoundToInt(guide.GetComponentInChildren<Text>().fontSize * 1.3f), start.GetComponentInChildren<Text>().fontSize);
            var tables = host.transform.Find("Root/Frame/setup-column/table-list") as RectTransform;
            if (tables == null)
                foreach (var rect in host.GetComponentsInChildren<RectTransform>()) if (rect.name == "table-list") tables = rect;
            Assert.IsNotNull(tables);
            Assert.Less(WorldTop(tables), WorldBottom(start));
            Assert.IsNotNull(FindText("参加・観戦できるゲーム卓"));
            Assert.IsNull(FindButton("設定"));
            Assert.IsNull(FindButton("CPU模擬戦を観戦"));
            Assert.IsNull(FindText("プレイヤーの数："));

            var nameLabel = FindText("あなたの名前：");
            Assert.IsNotNull(nameLabel);
            var nameRow = nameLabel.rectTransform.parent as RectTransform;
            var nameBox = name.GetComponent<RectTransform>();
            Assert.AreEqual(1080f * 0.60f, nameRow.rect.width, 2f);
            var scaleX = Mathf.Abs(WorldRight(nameRow) - WorldLeft(nameRow)) / nameRow.rect.width;
            Assert.AreEqual(nameLabel.fontSize, (WorldLeft(nameLabel.rectTransform) - WorldLeft(nameRow)) / scaleX, 2f);
            Assert.AreEqual(WorldMidY(nameLabel.rectTransform), WorldMidY(nameBox), 2f);
            var section = guide.rect.height * 2f;
            var scaleY = Mathf.Abs(WorldTop(guide) - WorldBottom(guide)) / guide.rect.height;
            Assert.AreEqual(section, (WorldBottom(hint) - WorldTop(nameRow)) / scaleY, 3f);
            Assert.AreEqual(section, (WorldBottom(nameRow) - WorldTop(start)) / scaleY, 3f);

            Assert.AreEqual(WorldMidX(frame), WorldMidX(guide), 2f);
            Assert.AreEqual(WorldMidX(frame), WorldMidX(start), 2f);
            Assert.AreEqual(WorldMidX(frame), WorldMidX(nameRow), 2f);
            var title = host.transform.Find("Root/Frame/title-mark") as RectTransform;
            var catchLine = host.transform.Find("Root/Frame/title-catch") as RectTransform;
            Assert.AreEqual(WorldMidX(frame), WorldMidX(title), 2f);
            Assert.AreEqual(WorldMidX(frame), WorldMidX(catchLine), 2f);
            Assert.GreaterOrEqual((WorldBottom(catchLine) - WorldTop(guide)) / scaleY, guide.rect.height * 2f - 2f);

            Color inkLabel;
            ColorUtility.TryParseHtmlString("#2C221E", out inkLabel);
            Assert.AreEqual(inkLabel, nameLabel.color);
            Color accent;
            ColorUtility.TryParseHtmlString("#8C3D2A", out accent);
            Assert.AreEqual(accent, start.GetComponent<Image>().color);

            Click("QuickStartガイド");
            Assert.IsNotNull(FindText("QuickStartガイド"));
            Text intro = null;
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text.StartsWith("場札から商品のカードを1枚選んで")) intro = label;
            Assert.IsNotNull(intro);
            Assert.AreEqual(36, intro.fontSize);
            var quickScroll = host.transform.Find("Root/Frame/setup-dialog").GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(quickScroll);
            Click("OK");
            Assert.IsNull(host.transform.Find("Root/Frame/setup-dialog"));

            Click("ルール");
            Rebuild(host);
            var rulesScroll = host.transform.Find("Root/Frame/setup-dialog").GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(rulesScroll);
            Assert.Greater(rulesScroll.content.rect.height, rulesScroll.viewport.rect.height);
            Click("OK");
        }

        [Test]
        public void TheLobbyListsTheWaitingCpusAndRunsThemAloneWhenYouSitOut()
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Click("新規ゲーム卓の準備");
            Assert.IsNull(FindButton("新規ゲーム卓の準備"));
            Assert.IsNotNull(FindText("プレイヤーの数："));
            Assert.IsNotNull(FindText("1. あなた"));
            Assert.AreEqual(2, CountText("CPU"));

            Click("戻る");
            Assert.IsNotNull(ButtonNamed("新規ゲーム卓の準備"));
            Assert.IsNull(FindButton("ゲーム開始"));
            Click("新規ゲーム卓の準備");

            Click("3人");
            Assert.IsNotNull(ButtonNamed("4人"));
            Assert.AreEqual(3, CountText("CPU"));
            var cast = (List<int>)typeof(TableView).GetField("lobbyCast", flags).GetValue(view);
            var before = new List<int>(cast);
            Assert.AreEqual(4, before.Count);
            Assert.IsNotNull(FindTextContaining(Ranking.DisplayName(before[0])));
            Assert.IsNotNull(FindText("1. あなた"));
            Assert.IsNotNull(FindTextContaining("2. "));
            Click("CPUプレイヤー入れ替え");
            CollectionAssert.AreNotEqual(before, cast);
            Assert.IsNotNull(FindTextContaining(Ranking.DisplayName(cast[0])));

            Click("その他の設定");
            Assert.IsNotNull(ButtonNamed("シンプルモード　オン"));
            var decide = ButtonNamed("決定").GetComponent<RectTransform>();
            var cancel = ButtonNamed("キャンセル").GetComponent<RectTransform>();
            var dialog = host.transform.Find("Root/Frame/setup-dialog") as RectTransform;
            var pairLeft = Mathf.Min(WorldLeft(decide), WorldLeft(cancel));
            var pairRight = Mathf.Max(WorldRight(decide), WorldRight(cancel));
            Assert.AreEqual(WorldMidX(dialog), (pairLeft + pairRight) * 0.5f, 3f);
            Click("キャンセル");
            Assert.IsNull(FindButton("シンプルモード　オン"));

            Click("自分は参加せずに参戦: NO");
            Assert.IsNotNull(FindText("自分は参加せずに参戦: YES"));
            Assert.IsNull(FindText("1. あなた"));
            Assert.AreEqual(4, CountText("CPU"));
            Assert.IsNotNull(FindTextContaining("1. "));
            Assert.IsNotNull(FindTextContaining("4. "));

            Set("seedText", "0");
            Click("ゲーム開始");
            Assert.IsFalse((bool)typeof(TableView).GetField("lobbyOpen", flags).GetValue(view));
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            Assert.AreEqual(4, match.Game.Players.Count);
            foreach (var player in match.Game.Players) Assert.IsFalse(player.IsHuman);
        }

        [Test]
        public void RoundEndStopsWaitingForAPersonOnceEverySeatIsACpu()
        {
            host = Open();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var noHuman = typeof(TableView).GetMethod("NoHumanSeats", flags);
            Assert.IsFalse((bool)noHuman.Invoke(view, null));
            Set("seedText", "0");
            Begin();
            Assert.IsFalse((bool)noHuman.Invoke(view, null));

            Click("EXIT");
            Click("抜ける");
            Click("新規ゲーム卓の準備");
            Click("自分は参加せずに参戦: NO");
            Click("ゲーム開始");
            Assert.IsTrue((bool)noHuman.Invoke(view, null));
            Assert.AreEqual(5f, (float)typeof(TableView).GetField("okTimeout", flags).GetValue(view));
        }

        [Test]
        public void MarketCardsShowTheGoodsPicture()
        {
            host = Open();
            Set("seedText", "7");
            Begin();
            Image icon = null;
            foreach (var image in host.GetComponentsInChildren<Image>())
            {
                if (image.gameObject.name != "suit" || image.sprite == null) continue;
                icon = image;
                break;
            }
            Assert.IsNotNull(icon);
            Assert.Greater(icon.rectTransform.rect.width, 70f);
            Assert.LessOrEqual(icon.rectTransform.rect.width, 112f);
        }

        [Test]
        public void PassButtonHasAVisibleSize()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
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
            Begin();

            var seat = host.transform.Find("Root/Frame/seat0");
            Assert.IsNull(seat.Find("wash"));
            var plate = seat.Find("plate") as RectTransform;
            Assert.AreEqual(0, plate.GetSiblingIndex());
            Assert.AreEqual(new Vector2(25f, -25f), plate.anchoredPosition);
            Assert.AreEqual(new Vector2(1030f, 340f), plate.sizeDelta);
            Color ink;
            ColorUtility.TryParseHtmlString("#2C221E", out ink);
            Assert.AreEqual(ink, plate.GetComponent<Image>().color);
            var fill = plate.Find("fill") as RectTransform;
            var fillColor = fill.GetComponent<Image>().color;
            Assert.AreEqual(0.953f, fillColor.r, 0.002f);
            Assert.AreEqual(0.929f, fillColor.g, 0.002f);
            Assert.AreEqual(0.894f, fillColor.b, 0.002f);
            Assert.AreEqual(0.8f, fillColor.a, 0.001f);
            Assert.AreEqual("Quota/Glass", fill.GetComponent<Image>().material.shader.name);
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
            Begin();

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
                Assert.AreEqual(95f * 200f / 144f, card.sizeDelta.y, 0.01f);
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
            Assert.AreEqual(144f, marketCard.sizeDelta.x, 0.01f);
            Assert.AreEqual(200f, marketCard.sizeDelta.y, 0.01f);
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
        public void TheCurrentNameplatePulsesFromWhiteToCreamAndTheScoreHasNoMarker()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
            var view = host.GetComponent<TableView>();
            var match = (OfflineMatch)typeof(TableView).GetField("match", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var current = match.Game.Current;
            var plate = host.transform.Find("Root/Frame/seat" + current + "/nameplate/fill").GetComponent<Image>();
            Assert.AreEqual(Color.white, plate.color);
            var pulse = typeof(TableView).GetMethod("TurnPulse", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.AreEqual(Color.white, pulse.Invoke(null, new object[] { 0f }));
            Assert.AreEqual(new Color32(255,230,173,255), (Color32)(Color)pulse.Invoke(null, new object[] { 1f }));
            Assert.AreEqual(Color.white, pulse.Invoke(null, new object[] { 2f }));
            var other = (current + 1) % match.Game.Players.Count;
            var plain = host.transform.Find("Root/Frame/seat" + other + "/nameplate/fill").GetComponent<Image>();
            Assert.AreEqual(Color.white, plain.color);
            var score = host.transform.Find("Root/Frame/seat" + current + "/score").GetComponent<Text>();
            Assert.IsFalse(score.text.Contains("▶"));
        }

        [Test]
        public void PendingBonusCoinSitsOnTheQuotaCardUntilTheSetIsAchieved()
        {
            host = Open();
            Set("seedText", "1");
            Begin();
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
            Begin();
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
            Assert.IsNotNull(FindText("2 + 1 = 3"));
            var panel = host.transform.Find("Root/Frame/ceremony") as RectTransform;
            Assert.AreEqual(56f, -panel.anchoredPosition.y, 1f);
            Assert.GreaterOrEqual(panel.sizeDelta.y, 340f);
            var leave = panel.Find("抜ける") as RectTransform;
            Assert.IsNotNull(leave);
            Assert.AreEqual((panel.sizeDelta.x - leave.sizeDelta.x) * 0.5f, leave.anchoredPosition.x, 1f);
            Assert.AreEqual(-(panel.sizeDelta.y - 80f), leave.anchoredPosition.y, 1f);
            Assert.IsNull(FindButton("ゲームを終了"));
            Click("抜ける");
            Assert.IsNotNull(ButtonNamed("新規ゲーム卓の準備"));
        }

        [Test]
        public void ExitConfirmationIsAvailableDuringScoreCeremony()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
            var view = host.GetComponent<TableView>();
            Set("ceremonyRunning", true);
            Set("ceremonyDialog", true);
            Set("confirm", "leave");
            // Render the confirmation directly: it must not depend on whose turn it is.
            typeof(TableView).GetMethod("Confirm", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            Assert.IsNotNull(ButtonNamed("抜ける"));
            ButtonNamed("抜ける").onClick.Invoke();
            var match = (OfflineMatch)typeof(TableView).GetField("match", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            Assert.IsNull(match.Game);
        }

        [Test]
        public void GuestAbandonAndExitConfirmationsStayAboveOtherPlayers()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            match.Game.Current = 1;
            match.Game.Players[1].IsHuman = true;
            Set("confirm", "abandon");
            Show(view);
            var panel = host.transform.Find("Root/Frame/seat1/confirm");
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.GetComponent<Canvas>().overrideSorting);
            Assert.AreEqual(100, panel.GetComponent<Canvas>().sortingOrder);
            Assert.IsNotNull(panel.GetComponent<UnityEngine.UI.GraphicRaycaster>());
            Assert.IsNotNull(FindText("本当に放棄しますか？"));
        }

        [Test]
        public void AbandonConfirmAttachesToTheLiveSeat()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
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
        public void DepartedCaptionDisappearsWhenSeatReturnsToHumanControl()
        {
            host = Open();
            Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            var apply = typeof(TableView).GetMethod("ApplyNetworkActions", flags);
            apply.Invoke(view, new object[] { new[] { "away:0", "cpu:0" } });
            var caption = typeof(TableView).GetMethod("PlayerCaption", flags);
            Assert.IsTrue(((string)caption.Invoke(view, new object[] { game, 0 })).EndsWith("（退席）"));
            Assert.IsFalse(game.Players[0].IsHuman);
            apply.Invoke(view, new object[] { new[] { "away:0", "cpu:0", "human:0" } });
            Assert.IsFalse(((string)caption.Invoke(view, new object[] { game, 0 })).Contains("（退席）"));
            Assert.IsTrue(game.Players[0].IsHuman);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CountdownIsVisibleTurnsRedAndHidesExpiredControls(bool wide)
        {
            host = Open();
            Set("widePreview", wide);
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var json = "{\"phase\":\"playing\",\"table_id\":\"test\",\"players\":3,\"seed\":0,\"turn_timeout_active\":true,\"turn_remaining\":30,\"seats\":[{\"name\":\"A\"},{\"name\":\"B\"}],\"cpus\":[\"CPU\"],\"cpu_cast\":[0],\"you\":{\"seat\":0,\"leader\":true},\"options\":{\"simple\":true},\"actions\":[]}";
            typeof(TableView).GetMethod("ApplyNetworkState", flags).Invoke(view, new object[] { json });
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            game.Current = 0;
            Show(view);
            var label = host.transform.Find("Root/Frame/seat0/turn-countdown").GetComponent<Text>();
            Assert.AreEqual("30秒", label.text);
            Set("networkTurnDeadline", Time.realtimeSinceStartupAsDouble + 4d);
            typeof(TableView).GetMethod("UpdateTurnCountdown", flags).Invoke(view, null);
            Assert.AreEqual("4秒", label.text);
            Assert.Greater(label.color.r, label.color.g);
            Set("networkTurnDeadline", Time.realtimeSinceStartupAsDouble - 1d);
            typeof(TableView).GetMethod("UpdateTurnCountdown", flags).Invoke(view, null);
            Assert.AreEqual("0秒", label.text);
            Assert.IsFalse(host.transform.Find("Root/Frame/controls").gameObject.activeSelf);
        }

        [Test]
        public void ActionFillIsOpaqueAndDisabledConfirmationPassesImmediately()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
            var button = FindButton("パス");
            var fill = button.transform.Find("fill").GetComponent<Image>().color;
            Assert.AreEqual(214f / 255f, fill.r, 0.001f);
            Assert.AreEqual(185f / 255f, fill.g, 0.001f);
            Assert.AreEqual(140f / 255f, fill.b, 0.001f);
            Assert.AreEqual(1f, fill.a);
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var game = ((OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view)).Game;
            var previous = game.Current;
            Set("confirmActions", false);
            typeof(TableView).GetMethod("Ask", flags).Invoke(view, new object[] { "pass" });
            Assert.IsNull(FindText("本当にパスしますか？"));
            Assert.AreNotEqual(previous, game.Current);
        }

        [Test]
        public void AbandonPassNextAndLeaveAskBeforeActing()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
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
            Click("パス");
            AssertConfirmInside("seat0");
            Assert.IsNotNull(FindText("本当にパスしますか？"));
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

            Click("EXIT");
            AssertConfirmInside("seat0");
            Assert.IsNotNull(FindText("本当にゲームから抜けますか？"));
            Assert.IsNotNull(ButtonNamed("抜ける"));
            Click("キャンセル");
            Assert.IsNotNull(ButtonNamed("EXIT"));
        }

        [Test]
        public void NextRoundClearsTheRoundEndWindow()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
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
            Assert.IsNotNull(FindText("ラウンド 2/3"));
        }

        [Test]
        public void RoundEndDialogKeepsItsTopPositionAndOnlyExpandsDownward()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
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
            var orderStart = game.TurnOrder.IndexOf(0);
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
            Assert.AreEqual(1040f, panel.sizeDelta.x, 0.1f);
            Assert.AreEqual(340f, panel.sizeDelta.y, 0.1f);
            Assert.AreEqual(56f, -panel.anchoredPosition.y, 0.1f);
            Assert.LessOrEqual(panel.anchoredPosition.x + panel.sizeDelta.x, 1060f);
            var compactLeft = panel.anchoredPosition.x;
            var compactRight = panel.anchoredPosition.x + panel.sizeDelta.x;
            var tray = host.transform.Find("Root/Frame/seat0/chip-tray") as RectTransform;
            Assert.IsNotNull(tray);
            Assert.Less(-panel.anchoredPosition.y + panel.sizeDelta.y, 400f);
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
                else
                {
                    Color titleInk;
                    ColorUtility.TryParseHtmlString("#2C221E", out titleInk);
                    Assert.AreEqual(titleInk, mono.color);
                }
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
            Assert.AreEqual(56f, -panel.anchoredPosition.y, 1f);
            Assert.GreaterOrEqual(panel.sizeDelta.y, 340f);
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
            Begin();
            var view = host.GetComponent<TableView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var match = (OfflineMatch)typeof(TableView).GetField("match", flags).GetValue(view);
            Assert.IsTrue(match.Game.Players[0].IsHuman);
            var ticket = (int)typeof(TableView).GetField("cpuRun", flags).GetValue(view);
            Click("EXIT");
            AssertConfirmInside("seat0");
            Click("抜ける");
            Assert.IsNull(match.Game);
            Assert.IsNull(FindText("本当にゲームから抜けますか？"));
            Assert.IsNotNull(ButtonNamed("新規ゲーム卓の準備"));
            Assert.IsNull(host.transform.Find("Root/Frame/seat0"));
            Assert.IsNull(FindButton("パス"));
            Assert.IsTrue((bool)typeof(TableView).GetField("onSetup", flags).GetValue(view));
            typeof(TableView).GetField("laidOutWide", flags).SetValue(view, true);
            typeof(TableView).GetMethod("Update", flags).Invoke(view, null);
            Rebuild(host);
            Assert.IsNotNull(ButtonNamed("新規ゲーム卓の準備"));
            Assert.IsNull(host.transform.Find("Root/Frame/seat0"));
            Assert.IsNull(FindButton("パス"));
            var run = (IEnumerator)typeof(TableView).GetMethod("RunCpus", flags).Invoke(view, new object[] { ticket });
            Drive(run);
            Assert.IsNull(match.Game);
            Assert.IsNotNull(ButtonNamed("新規ゲーム卓の準備"));
            Assert.IsNull(FindButton("パス"));
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
            Assert.IsNotNull(ButtonNamed("新規ゲーム卓の準備"));
        }

        [Test]
        public void PartialCollectKeepsTheTurnAndShowsNext()
        {
            host = Open();
            Set("seedText", "0");
            Begin();
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

            Assert.IsNotNull(FindButton("パス"));
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

        void Begin()
        {
            Click("新規ゲーム卓の準備");
            Click("ゲーム開始");
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

        int CountText(string caption)
        {
            var found = 0;
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text == caption) found++;
            return found;
        }

        Text FindText(string caption)
        {
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text == caption) return label;
            return null;
        }

        Text FindTextContaining(string fragment)
        {
            foreach (var label in host.GetComponentsInChildren<Text>())
                if (label.text != null && label.text.Contains(fragment)) return label;
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

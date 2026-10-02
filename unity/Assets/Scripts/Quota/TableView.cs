using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Quota
{
    public sealed class TableView : MonoBehaviour
    {
        const float ScreenWidth = 1080f;
        const float ScreenHeight = 1920f;
        const float LandWidth = 1920f;
        const float LandHeight = 1080f;
        const float LandHeader = 120f;
        const float LandSeatHeight = 240f;
        const float LandLeft = 1220f;
        const float LandMarketX = 1245f;
        const float LandMarketY = 320f;
        const float LandMarketW = 650f;
        const float LandMarketH = 450f;
        const float LandButtonRight = 28f;
        const float SeatTop = 400f;
        const float SeatHeight = 380f;
        const float ActionStride = 74f;
        const float SplashSeconds = 3f;
        const float SplashFadeSeconds = 0.6f;
        const int AdvancedPromptAfter = 3;
        const string SplashCopy = "港で働く仲買人のあなた。\n大口顧客のために、舶来の交易品を買い集めよう。\n買い付けノルマは、自分で決める。";
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
        bool webAssetsReady = true;
        readonly List<RectTransform> seatFrames = new List<RectTransform>();
        bool busy;
        string confirm;
        int playerCount = 3;
        bool simpleMode = true;
        bool sequenceRule;
        bool titleRule;
        bool specialRule;
        bool finishCounted;
        bool offerStandard;
        Coroutine splashRun;
        string seedText = "";
        string playerName = "あなた";
        string setupPage;
        string draftOk = "5";
        string draftTurn = "120";
        bool draftPro;
        float okTimeout = 5f;
        float turnTimeout = 120f;
        Sprite titleMark;
        Sprite catchMark;
        bool widePreview;
        bool laidOutWide;
        int cpuRun;
        float cpuNotBefore;
        int coinMotion;
        Dictionary<string, Vector3> coinFrom = new Dictionary<string, Vector3>();
        bool ceremonyRunning;
        bool ceremonyOk;
        bool ceremonyDismissed;
        bool ceremonyScoreReady;
        bool reviewMode;
        string ceremonyHeading = "";
        string ceremonyReason = "";
        bool ceremonyReasonShown;
        string ceremonyButton;
        int ceremonyLineCount;
        int ceremonyRoundIndex;
        int roundLeaderSeat;
        bool ceremonyLast;
        bool ceremonyOverall;
        bool ceremonyOverallSlot;
        bool ceremonyBlank;
        bool ceremonyWinner;
        bool ceremonyDialog;
        string ceremonyRankTitle;
        int dotSerial;
        int ceremonyPending;
        readonly List<CeremonyLine> titleLines = new List<CeremonyLine>();
        readonly List<CeremonyDot> ceremonyTray = new List<CeremonyDot>();
        readonly Dictionary<int, RectTransform> bonusMarks = new Dictionary<int, RectTransform>();
        List<int> orderOverride;
        List<int> dialogOrder;
        Dictionary<int, int> ceremonyPlaces;
        bool ceremonyEquation;
        Dictionary<int, int> scoreOverride;
        Dictionary<int, int> plusOverride;
        Dictionary<int, int> roundScores;
        Dictionary<int, int> previousScores;
        float reviewUntil;
        List<int> reviewOrder;
        Dictionary<int, int> reviewScores;

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
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                webAssetsReady = false;
                if (Application.isPlaying) StartCoroutine(LoadWebAssets());
            }
            else
            {
                verticalBackground = LoadBackground("vertical_base.jpg");
                horizontalBackground = LoadBackground("horizontal_base.jpg");
            }
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
            LoadRules();
            ShowSplash();
        }

        void Update()
        {
            Fit();
            if (!Application.isPlaying || frame == null) return;
            var wide = WideScreen();
            if (wide == laidOutWide) return;
            if (frame.Find("splash") != null)
            {
                laidOutWide = wide;
                return;
            }
            if (match.Game != null) ShowTable();
            else ShowSetup();
        }

        bool WideScreen()
        {
            if (!Application.isPlaying) return widePreview;
            return Screen.width > Screen.height && Screen.height > 0;
        }

        void UseFrame()
        {
            var wide = WideScreen();
            frame.sizeDelta = new Vector2(wide ? LandWidth : ScreenWidth, wide ? LandHeight : ScreenHeight);
            laidOutWide = wide;
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
            var wide = WideScreen();
            var designW = wide ? LandWidth : ScreenWidth;
            var designH = wide ? LandHeight : ScreenHeight;
            frame.sizeDelta = new Vector2(designW, designH);
            var scale = Mathf.Min(Screen.width / designW, Screen.height / designH);
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
            if (Application.platform == RuntimePlatform.WebGLPlayer) return null;
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

        void ShowSplash()
        {
            Clear();
            UseFrame();
            var wide = WideScreen();
            var width = wide ? LandWidth : ScreenWidth;
            var height = wide ? LandHeight : ScreenHeight;
            var splash = Portrait.Rect(frame, "splash", 0f, 0f, width, height);
            splash.gameObject.AddComponent<CanvasGroup>();
            var veilY = wide ? (height - 400f) * 0.5f : 760f;
            Portrait.Solid(splash, "veil", 0f, veilY, width, 400f, new Color(0f, 0f, 0f, 0.45f));
            var copy = TextAt(splash, SplashCopy, 48f, wide ? (height - 320f) * 0.5f : 800f, width - 96f, 320f, 34, Color.white, nameFont, TextAnchor.MiddleCenter);
            copy.horizontalOverflow = HorizontalWrapMode.Overflow;
            copy.lineSpacing = 1.15f;
            if (Application.isPlaying) splashRun = StartCoroutine(FadeSplash());
        }

        IEnumerator LoadWebAssets()
        {
            Sprite vertical = null;
            yield return LoadSprite("vertical_base.jpg", sprite => vertical = sprite);
            verticalBackground = vertical;
            Sprite horizontal = null;
            yield return LoadSprite("horizontal_base.jpg", sprite => horizontal = sprite);
            horizontalBackground = horizontal;
            string json = null;
            yield return LoadText("quota_goods_v1.0.json", text => json = text);
            if (!string.IsNullOrEmpty(json)) ItemCatalog.LoadJson(json);
            if (ItemCatalog.IsLoaded)
            {
                foreach (var file in ItemCatalog.PictureFiles())
                {
                    Sprite picture = null;
                    yield return LoadSprite("goods/" + file + ".png", sprite => picture = sprite);
                    if (picture != null) goodsSprites[file] = picture;
                }
            }
            Sprite loadedTitle = null;
            yield return LoadSprite("title1.png", sprite => loadedTitle = sprite);
            if (loadedTitle != null) titleMark = loadedTitle;
            Sprite loadedCatch = null;
            yield return LoadSprite("title2.png", sprite => loadedCatch = sprite);
            if (loadedCatch != null) catchMark = loadedCatch;
            webAssetsReady = true;
            Fit();
        }

        IEnumerator LoadText(string fileName, System.Action<string> done)
        {
            var request = UnityWebRequest.Get(StreamingUrl(fileName));
            yield return request.SendWebRequest();
            var text = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
            request.Dispose();
            done(text);
        }

        IEnumerator LoadSprite(string fileName, System.Action<Sprite> done)
        {
            var request = UnityWebRequestTexture.GetTexture(StreamingUrl(fileName));
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                request.Dispose();
                done(null);
                yield break;
            }
            var texture = DownloadHandlerTexture.GetContent(request);
            request.disposeDownloadHandlerOnDispose = false;
            request.Dispose();
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            done(Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f));
        }

        static string StreamingUrl(string fileName)
        {
            var root = Application.streamingAssetsPath;
            if (!root.EndsWith("/")) root += "/";
            return root + fileName;
        }

        IEnumerator FadeSplash()
        {
            yield return new WaitForSeconds(SplashSeconds);
            var extra = 0f;
            while (!webAssetsReady && extra < 17f)
            {
                extra += Time.deltaTime;
                yield return null;
            }
            var splash = frame.Find("splash");
            var group = splash != null ? splash.GetComponent<CanvasGroup>() : null;
            var elapsed = 0f;
            while (elapsed < SplashFadeSeconds)
            {
                elapsed += Time.deltaTime;
                if (group != null) group.alpha = 1f - Mathf.Clamp01(elapsed / SplashFadeSeconds);
                yield return null;
            }
            splashRun = null;
            ShowSetup();
        }

        void DismissSplash()
        {
            if (splashRun != null) StopCoroutine(splashRun);
            splashRun = null;
            ShowSetup();
        }

        void ApplyMode()
        {
            sequenceRule = !simpleMode;
            titleRule = !simpleMode;
            specialRule = !simpleMode;
        }

        void LoadRules()
        {
            if (!Application.isPlaying) return;
            if (PlayerPrefs.GetInt("quota.saved", 0) == 0)
            {
                simpleMode = true;
                ApplyMode();
                SaveRules();
                return;
            }
            if (PlayerPrefs.HasKey("quota.simple")) simpleMode = PlayerPrefs.GetInt("quota.simple", 1) == 1;
            else simpleMode = PlayerPrefs.GetInt("quota.sequence", 0) == 0 && PlayerPrefs.GetInt("quota.title", 0) == 0 && PlayerPrefs.GetInt("quota.special", 0) == 0;
            playerName = PlayerPrefs.GetString("quota.name", "あなた");
            if (string.IsNullOrWhiteSpace(playerName)) playerName = "あなた";
            okTimeout = PlayerPrefs.GetFloat("quota.okTimeout", 5f);
            turnTimeout = PlayerPrefs.GetFloat("quota.turnTimeout", 120f);
            ApplyMode();
        }

        void SaveRules()
        {
            if (!Application.isPlaying) return;
            PlayerPrefs.SetInt("quota.saved", 1);
            PlayerPrefs.SetInt("quota.simple", simpleMode ? 1 : 0);
            PlayerPrefs.SetInt("quota.sequence", sequenceRule ? 1 : 0);
            PlayerPrefs.SetInt("quota.title", titleRule ? 1 : 0);
            PlayerPrefs.SetInt("quota.special", specialRule ? 1 : 0);
            PlayerPrefs.SetString("quota.name", string.IsNullOrWhiteSpace(playerName) ? "あなた" : playerName.Trim());
            PlayerPrefs.SetFloat("quota.okTimeout", okTimeout);
            PlayerPrefs.SetFloat("quota.turnTimeout", turnTimeout);
            PlayerPrefs.Save();
        }

        void ShowSetup()
        {
            busy = false;
            if (ceremonyRunning)
            {
                cpuRun++;
                ceremonyRunning = false;
            }
            ceremonyScoreReady = false;
            reviewMode = false;
            orderOverride = null;
            dialogOrder = null;
            ceremonyPlaces = null;
            ceremonyEquation = false;
            ceremonyOverall = false;
            ceremonyOverallSlot = false;
            ceremonyBlank = false;
            ceremonyWinner = false;
            ceremonyDialog = false;
            ceremonyRankTitle = null;
            ceremonyReason = "";
            ceremonyReasonShown = false;
            scoreOverride = null;
            plusOverride = null;
            CleanupFlyers();
            Clear();
            UseFrame();
            var wide = WideScreen();
            var x = wide ? (LandWidth - 984f) * 0.5f : 48f;
            var title = TitleSprite(true);
            var catchLine = TitleSprite(false);
            if (title != null) PlaceSprite(frame, "title-mark", title, x, 24f, 760f, 152f);
            else TextAt(frame, "QUOTA", x, 36f, 700f, 72f, 64, Color.white, nameFont, TextAnchor.MiddleLeft);
            if (catchLine != null) PlaceSprite(frame, "title-catch", catchLine, x, 184f, 560f, 56f);
            else TextAt(frame, "ノルマは、自分で決めろ。", x, 184f, 700f, 40f, 28, Color.white, nameFont, TextAnchor.MiddleLeft);
            var column = Portrait.Rect(frame, "setup", x, 260f, 984f, 1500f);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            SetupButton(column, "QuickStartガイド", () => OpenPage("quick"));
            SetupButton(column, "ルール", () => OpenPage("rules"));
            SetupButton(column, "勝つためのヒント", () => OpenPage("hint"));
            SetupButton(column, $"人数  {playerCount}", () =>
            {
                playerCount = playerCount == 3 ? 4 : 3;
                ShowSetup();
            });
            var name = Field(column, "あなたの名前", string.IsNullOrWhiteSpace(playerName) ? "あなた" : playerName);
            name.onValueChanged.AddListener(value => playerName = value);
            SetupButton(column, "設定", OpenSettings);
            SetupButton(column, "対局開始", () => StartMatch(false));
            SetupButton(column, "CPU模擬戦を観戦", () => StartMatch(true));
            if (reviewUntil > Time.realtimeSinceStartup && reviewOrder != null && reviewOrder.Count > 0)
                SetupButton(column, "ゲーム終了の卓を見る", ShowReview);
            if (!string.IsNullOrEmpty(setupPage)) DrawSetupPage(wide);
        }

        void OpenPage(string page)
        {
            setupPage = page;
            ShowSetup();
        }

        void OpenSettings()
        {
            draftPro = !simpleMode;
            draftOk = okTimeout.ToString("0.##");
            draftTurn = turnTimeout.ToString("0.##");
            setupPage = "settings";
            ShowSetup();
        }

        void ApplySettings()
        {
            simpleMode = !draftPro;
            ApplyMode();
            okTimeout = ParseSeconds(draftOk, 5f, 0f);
            turnTimeout = ParseSeconds(draftTurn, 120f, 1f);
            draftOk = okTimeout.ToString("0.##");
            draftTurn = turnTimeout.ToString("0.##");
            SaveRules();
            setupPage = null;
            ShowSetup();
        }

        static float ParseSeconds(string text, float fallback, float minimum)
        {
            if (!float.TryParse(text, out var value)) return fallback;
            return value < minimum ? minimum : value;
        }

        void StartMatch(bool cpuOnly)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer && !ItemCatalog.IsLoaded) return;
            cpuRun++;
            ceremonyRunning = false;
            ceremonyDismissed = false;
            reviewMode = false;
            reviewOrder = null;
            reviewScores = null;
            CleanupFlyers();
            int? parsed = null;
            if (int.TryParse(seedText, out var number)) parsed = number;
            var names = new List<string>();
            var characters = new List<int>();
            var used = new HashSet<int>();
            var rng = new System.Random();
            var firstCpu = cpuOnly ? 0 : 1;
            playerName = HumanName();
            if (Application.isPlaying)
            {
                PlayerPrefs.SetString("quota.name", playerName);
                PlayerPrefs.Save();
            }
            if (!cpuOnly) names.Add(playerName);
            for (var i = firstCpu; i < playerCount; i++)
            {
                var character = Characters.PickFresh(rng, used);
                characters.Add(character);
                names.Add(Characters.NameOf(character));
            }
            orderOverride = null;
            dialogOrder = null;
            ceremonyPlaces = null;
            ceremonyEquation = false;
            scoreOverride = null;
            plusOverride = null;
            ceremonyRunning = false;
            match.Begin(new GameConfig
            {
                NumPlayers = playerCount,
                Seed = parsed,
                Names = names,
                HumanSeats = cpuOnly ? new List<int>() : new List<int> { 0 },
                SequenceRule = sequenceRule,
                TitleRule = titleRule,
                SpecialActionsRule = specialRule,
                Rounds = simpleMode ? 1 : playerCount,
            }, pumpCpus: !Application.isPlaying);
            Characters.BindInOrder(match.Game, characters);
            confirm = null;
            finishCounted = false;
            offerStandard = false;
            cpuNotBefore = Application.isPlaying ? Time.time + 1f : 0f;
            ShowTable();
            if (Application.isPlaying) StartCoroutine(RunCpus(++cpuRun));
        }

        void ShowReview()
        {
            if (match.Game == null || reviewOrder == null || reviewScores == null) return;
            reviewMode = true;
            ceremonyScoreReady = true;
            ceremonyRunning = false;
            ceremonyDismissed = true;
            ceremonyHeading = "ゲーム終了";
            ceremonyReason = "";
            ceremonyReasonShown = false;
            ceremonyButton = "ゲームを終了";
            ceremonyOverall = false;
            ceremonyOverallSlot = false;
            ceremonyBlank = false;
            ceremonyWinner = true;
            ceremonyLineCount = 0;
            titleLines.Clear();
            orderOverride = null;
            dialogOrder = new List<int>(reviewOrder);
            scoreOverride = new Dictionary<int, int>(reviewScores);
            plusOverride = null;
            ceremonyEquation = false;
            AssignPlaces(seat => reviewScores.TryGetValue(seat, out var score) ? score : 0);
            ShowTable();
        }

        void ShowTable()
        {
            coinFrom = SnapshotCardCoins();
            var game = match.Game;
            var ceremonyBreak = !reviewMode && game != null && (game.AwaitingNextRound || (Application.isPlaying && game.Finished && !ceremonyDismissed));
            if (ceremonyBreak && Application.isPlaying && !ceremonyRunning)
            {
                ceremonyRunning = true;
                PrepareCeremony(game);
                StartCoroutine(RunCeremony(++cpuRun));
            }
            Clear();
            seatFrames.Clear();
            UseFrame();
            game = match.Game;
            var theme = game.Theme();
            var wide = WideScreen();
            var rows = DisplayRows(game);
            for (var row = 0; row < rows.Count; row++)
            {
                if (wide) DrawPlayerWide(game, theme, rows[row], row);
                else DrawPlayer(game, theme, rows[row], row);
            }
            var marketWash = new Color(0f, 0f, 0f, 0.5f);
            if (wide) Portrait.Box(frame, "market-tray", LandMarketX, LandMarketY, LandMarketW, LandMarketH, 7f, 0f, marketWash, Color.white, false);
            else Portrait.Box(frame, "market-tray", 20f, 170f, 1040f, 210f, 7f, 0f, marketWash, Color.white, false);
            if (wide) DrawMarketWide(game, theme);
            else DrawMarket(game, theme);
            if (wide) DrawTitleWide(game);
            else DrawTitle(game);
            if (ceremonyRunning) LayoutCeremonyDots();
            if (!reviewMode && !game.AwaitingNextRound && !(Application.isPlaying && game.Finished && !ceremonyDismissed))
                ceremonyDialog = false;
            var showCeremony = reviewMode || ceremonyDialog;
            if (showCeremony) DrawCeremonyPanel();
            else if (ceremonyBreak && game.AwaitingNextRound && !ceremonyRunning) DrawRoundBreak(game);
            else if (confirm == null && match.IsHumanTurn && !busy && !game.Finished) DrawControls(game);
            else if (confirm == null && !game.Finished)
            {
                var note = $"{game.Players[game.Current].Name} が考えています";
                if (wide) TextAt(frame, note, LandMarketX, LandMarketY + LandMarketH + 12f, LandMarketW, 32f, 22, Color.white, nameFont, TextAnchor.MiddleLeft);
                else TextAt(frame, note, 28f, 108f, 700f, 32f, 22, Color.white, nameFont, TextAnchor.MiddleLeft);
            }
            if (!game.Finished) LeaveButton();
            if (!showCeremony && game.Finished && !ceremonyBreak)
            {
                Result(game);
                if (offerStandard) DrawStandardOffer();
            }
            else if (confirm != null && !ceremonyBreak && !reviewMode) Confirm();
        }

        static string RoundLabel(Game game)
        {
            if (game.RoundCount <= 1) return "揃えて、達成。";
            return $"第{game.RoundIndex}ラウンド / {game.RoundCount}";
        }

        sealed class CeremonyLine
        {
            public int Seat;
            public string Text;
            public int Points;
            public int Arrived;
            public bool Gone;
        }

        sealed class CeremonyDot
        {
            public int Seat;
            public CoinKind Kind;
            public int Serial;
        }

        struct TitleJob
        {
            public int Line;
            public int Seat;
        }

        void PrepareCeremony(Game game)
        {
            ceremonyOk = false;
            ceremonyDismissed = false;
            dotSerial = 0;
            ceremonyTray.Clear();
            titleLines.Clear();
            bonusMarks.Clear();
            scoreOverride = new Dictionary<int, int>();
            plusOverride = null;
            roundScores = new Dictionary<int, int>();
            previousScores = new Dictionary<int, int>();
            var count = game.Players.Count;
            orderOverride = null;
            ceremonyPlaces = null;
            ceremonyEquation = false;
            ceremonyOverall = false;
            ceremonyOverallSlot = game.RoundIndex >= 2;
            ceremonyBlank = false;
            ceremonyWinner = false;
            ceremonyDialog = false;
            ceremonyRankTitle = null;
            roundLeaderSeat = game.TurnOrder.Count == 0 ? 0 : game.TurnOrder[(game.RoundIndex - 1) % game.TurnOrder.Count];
            var cycle = game.TurnOrder.Count == count ? game.TurnOrder : new List<int>();
            if (cycle.Count != count)
            {
                cycle = new List<int>();
                for (var i = 0; i < count; i++) cycle.Add(i);
            }
            dialogOrder = Rotate(cycle, roundLeaderSeat);
            for (var seat = 0; seat < count; seat++)
            {
                var player = game.Players[seat];
                var baseScore = BaseScore(player);
                var green = GreenCount(player);
                var purple = game.SequencePoints(player);
                var blue = game.TitlePoints(player);
                scoreOverride[seat] = baseScore;
                roundScores[seat] = baseScore + green + purple + blue;
                previousScores[seat] = player.Score - baseScore - green;
                for (var n = 0; n < green; n++) ceremonyTray.Add(new CeremonyDot { Seat = seat, Kind = CoinKind.Green, Serial = ++dotSerial });
                for (var n = 0; n < purple; n++) ceremonyTray.Add(new CeremonyDot { Seat = seat, Kind = CoinKind.Purple, Serial = ++dotSerial });
            }
            foreach (var seat in Rotate(game.TurnOrder, roundLeaderSeat))
            {
                foreach (var award in game.TitleAwards(game.Players[seat]))
                {
                    if (award.points <= 0) continue;
                    titleLines.Add(new CeremonyLine
                    {
                        Seat = seat,
                        Text = $"{game.Players[seat].Name} : {award.name}ボーナス +{award.points}",
                        Points = award.points,
                    });
                }
            }
            ceremonyLineCount = titleLines.Count;
            ceremonyRoundIndex = game.RoundIndex;
            ceremonyLast = game.Finished || game.RoundIndex >= game.RoundCount;
            ceremonyHeading = $"第{game.RoundIndex}ラウンド終了";
            ceremonyReason = game.RoundEndReason == "DECK" ? "山札切れでラウンド終了。" : game.RoundEndReason == "STALL" ? "膠着の連続でラウンド終了。" : "";
            ceremonyReasonShown = false;
            ceremonyButton = null;
        }

        IEnumerator RunCeremony(int serial)
        {
            yield return null;
            if (serial != cpuRun || match.Game == null) yield break;
            ceremonyDialog = true;
            ceremonyReasonShown = ceremonyReason.Length > 0;
            ceremonyButton = null;
            ShowTable();
            yield return new WaitForSeconds(1f);
            if (serial != cpuRun || match.Game == null) yield break;
            ceremonyReasonShown = false;
            RedrawCeremonyPanel();
            yield return FlyTitles(serial);
            if (serial != cpuRun) yield break;
            dialogOrder = SortBy(scoreOverride);
            AssignPlaces(seat => scoreOverride.TryGetValue(seat, out var score) ? score : 0);
            RedrawCeremonyPanel();
            yield return new WaitForSeconds(0.5f);
            if (serial != cpuRun) yield break;
            yield return FlyScores(serial);
            if (serial != cpuRun) yield break;
            dialogOrder = SortBy(scoreOverride);
            AssignPlaces(seat => scoreOverride.TryGetValue(seat, out var score) ? score : 0);
            ceremonyButton = "OK";
            ceremonyOk = false;
            RedrawCeremonyPanel();
            yield return WaitOr(serial, 2f);
            if (serial != cpuRun) yield break;
            if (ceremonyRoundIndex >= 2)
            {
                ceremonyBlank = true;
                ceremonyButton = "OK";
                ceremonyOk = false;
                RedrawCeremonyPanel();
                yield return WaitOr(serial, 0.8f);
                if (serial != cpuRun) yield break;
                ceremonyBlank = false;
                ceremonyOverall = true;
                ceremonyRankTitle = "暫定順位";
                scoreOverride = new Dictionary<int, int>(previousScores);
                plusOverride = null;
                AssignPlacesByScore(seat => previousScores.TryGetValue(seat, out var previous) ? previous : 0);
                ceremonyEquation = false;
                ceremonyButton = "OK";
                ceremonyOk = false;
                RedrawCeremonyPanel();
                yield return WaitOr(serial, 2f);
                if (serial != cpuRun) yield break;
                ceremonyButton = null;
                RedrawCeremonyPanel();
                plusOverride = new Dictionary<int, int>();
                for (var i = 0; i < dialogOrder.Count; i++)
                {
                    plusOverride[dialogOrder[i]] = roundScores[dialogOrder[i]];
                    RefreshScore(dialogOrder[i]);
                    if (i + 1 >= dialogOrder.Count) continue;
                    yield return new WaitForSeconds(0.2f);
                    if (serial != cpuRun) yield break;
                }
                yield return new WaitForSeconds(1f);
                if (serial != cpuRun) yield break;
                ceremonyEquation = true;
                for (var i = 0; i < dialogOrder.Count; i++)
                {
                    var seat = dialogOrder[i];
                    scoreOverride[seat] = previousScores[seat] + roundScores[seat];
                    RefreshScore(seat);
                    if (i + 1 >= dialogOrder.Count) continue;
                    yield return new WaitForSeconds(0.2f);
                    if (serial != cpuRun) yield break;
                }
                yield return new WaitForSeconds(1f);
                if (serial != cpuRun) yield break;
                dialogOrder = SortBy(scoreOverride);
                plusOverride = null;
                AssignPlaces(seat => scoreOverride.TryGetValue(seat, out var score) ? score : 0);
                ceremonyLast = match.Game.Finished || match.Game.RoundIndex >= match.Game.RoundCount;
                ceremonyRankTitle = ceremonyLast ? "最終順位" : "暫定順位";
                RedrawCeremonyPanel();
            }
            if (serial != cpuRun) yield break;
            ceremonyLast = match.Game.Finished || match.Game.RoundIndex >= match.Game.RoundCount;
            ceremonyOk = false;
            if (ceremonyLast)
            {
                yield return new WaitForSeconds(1f);
                if (serial != cpuRun) yield break;
                reviewOrder = new List<int>(dialogOrder ?? new List<int>());
                reviewScores = new Dictionary<int, int>(scoreOverride ?? new Dictionary<int, int>());
                reviewUntil = Time.realtimeSinceStartup + 180f;
                ceremonyWinner = true;
                ceremonyButton = "ゲームを終了";
            }
            else ceremonyButton = "OK";
            RedrawCeremonyPanel();
            while (!ceremonyOk)
            {
                if (serial != cpuRun) yield break;
                yield return null;
            }
            if (serial != cpuRun) yield break;
            FinishCeremony();
        }

        IEnumerator WaitOr(int serial, float seconds)
        {
            ceremonyOk = false;
            var start = Time.time;
            while (!ceremonyOk && Time.time - start < seconds)
            {
                if (serial != cpuRun) yield break;
                yield return null;
            }
        }

        void AssignPlacesByScore(System.Func<int, int> scoreOf)
        {
            ceremonyPlaces = new Dictionary<int, int>();
            if (dialogOrder == null) return;
            var seats = new List<int>(dialogOrder);
            seats.Sort((a, b) => scoreOf(b).CompareTo(scoreOf(a)));
            var place = 1;
            for (var i = 0; i < seats.Count; i++)
            {
                if (i > 0 && scoreOf(seats[i]) != scoreOf(seats[i - 1])) place = i + 1;
                ceremonyPlaces[seats[i]] = place;
            }
        }

        IEnumerator FlyTitles(int serial)
        {
            var jobs = new List<TitleJob>();
            for (var i = 0; i < titleLines.Count; i++)
                for (var n = 0; n < titleLines[i].Points; n++)
                    jobs.Add(new TitleJob { Line = i, Seat = titleLines[i].Seat });
            if (jobs.Count == 0) yield break;
            ceremonyPending = jobs.Count;
            var launched = 0;
            var started = Time.time;
            while (ceremonyPending > 0)
            {
                if (serial != cpuRun) yield break;
                while (launched < jobs.Count && Time.time >= started + launched * 0.1f)
                {
                    var job = jobs[launched];
                    launched++;
                    var from = BonusPoint(job.Line);
                    var to = TrayPoint(job.Seat);
                    var lineIndex = job.Line;
                    var seat = job.Seat;
                    StartCoroutine(AnimateFly(serial, from, to, Hex("#3c7dff"), () =>
                    {
                        var dot = new CeremonyDot { Seat = seat, Kind = CoinKind.Blue, Serial = ++dotSerial };
                        ceremonyTray.Add(dot);
                        SpawnDot(dot);
                        var line = titleLines[lineIndex];
                        line.Arrived++;
                        if (line.Arrived >= line.Points && !line.Gone)
                        {
                            line.Gone = true;
                            RedrawCeremonyPanel();
                        }
                        ceremonyPending--;
                    }));
                }
                yield return null;
            }
        }

        IEnumerator FlyScores(int serial)
        {
            var jobs = new List<CeremonyDot>();
            foreach (var seat in RoundTurnOrder())
                for (var i = 0; i < ceremonyTray.Count; i++)
                    if (ceremonyTray[i].Seat == seat) jobs.Add(ceremonyTray[i]);
            if (jobs.Count == 0) yield break;
            ceremonyPending = jobs.Count;
            var launched = 0;
            var started = Time.time;
            while (ceremonyPending > 0)
            {
                if (serial != cpuRun) yield break;
                while (launched < jobs.Count && Time.time >= started + launched * 0.1f)
                {
                    var dot = jobs[launched];
                    launched++;
                    var from = TakeDot(dot);
                    var to = ScorePoint(dot.Seat);
                    var seat = dot.Seat;
                    var color = CoinColor(dot.Kind);
                    StartCoroutine(AnimateFly(serial, from, to, color, () =>
                    {
                        if (scoreOverride != null)
                        {
                            if (!scoreOverride.ContainsKey(seat)) scoreOverride[seat] = 0;
                            scoreOverride[seat] += 1;
                            RefreshScore(seat);
                        }
                        ceremonyPending--;
                    }));
                }
                yield return null;
            }
        }

        IEnumerator AnimateFly(int serial, Vector3 from, Vector3 to, Color color, System.Action arrived)
        {
            var ring = Portrait.Circle(transform, "flyer", 0f, 0f, 18f, Color.black);
            var disk = Portrait.Circle(transform, "flyer", 0f, 0f, 16f, color);
            PlaceCenter(ring, from);
            if (disk != null) disk.position = ring.position;
            var start = Time.time;
            while (Time.time - start < 0.3f)
            {
                if (serial != cpuRun)
                {
                    if (ring != null) Destroy(ring.gameObject);
                    if (disk != null) Destroy(disk.gameObject);
                    yield break;
                }
                var along = (Time.time - start) / 0.3f;
                var point = Vector3.Lerp(from, to, along);
                if (ring != null) PlaceCenter(ring, point);
                if (disk != null && ring != null) disk.position = ring.position;
                yield return null;
            }
            if (ring != null) Destroy(ring.gameObject);
            if (disk != null) Destroy(disk.gameObject);
            if (serial == cpuRun && arrived != null) arrived();
        }

        void FinishCeremony()
        {
            var last = ceremonyLast;
            ceremonyRunning = false;
            ceremonyDialog = false;
            ceremonyRankTitle = null;
            ceremonyButton = null;
            plusOverride = null;
            ceremonyTray.Clear();
            titleLines.Clear();
            CleanupFlyers();
            if (last)
            {
                ceremonyDismissed = true;
                orderOverride = null;
                dialogOrder = null;
                scoreOverride = null;
                ShowSetup();
                return;
            }
            orderOverride = null;
            dialogOrder = null;
            scoreOverride = null;
            match.Game.BeginNextRound();
            cpuNotBefore = Time.time + 0.6f;
            StartCoroutine(RunCpus(++cpuRun));
        }

        List<int> RoundTurnOrder()
        {
            var game = match.Game;
            if (game != null && game.TurnOrder.Count == game.Players.Count) return Rotate(game.TurnOrder, roundLeaderSeat);
            var seats = new List<int>();
            var count = game != null ? game.Players.Count : 0;
            for (var i = 0; i < count; i++) seats.Add(i);
            return Rotate(seats, roundLeaderSeat);
        }

        void DrawCeremonyPanel()
        {
            bonusMarks.Clear();
            var wide = WideScreen();
            var panelW = wide ? 520f : 460f;
            const float rowH = 48f;
            const float lineH = 36f;
            const float titleH = 34f;
            const float winnerH = 64f;
            var rows = dialogOrder != null ? dialogOrder.Count : 0;
            var reasonH = ceremonyReasonShown && !string.IsNullOrEmpty(ceremonyReason) ? lineH : 0f;
            var visibleTitles = 0;
            for (var i = 0; i < titleLines.Count; i++) if (!titleLines[i].Gone) visibleTitles++;
            var titlesH = visibleTitles * titleH;
            var overallH = ceremonyOverallSlot ? lineH : 0f;
            var winH = ceremonyWinner ? winnerH : 0f;
            var height = 20f + 44f + reasonH + titlesH + overallH + rows * rowH + winH + 88f;
            var panelX = ((wide ? LandWidth : ScreenWidth) - panelW) * 0.5f;
            var panelY = wide ? 200f : 360f;
            var panel = Portrait.Box(frame, "ceremony", panelX, panelY, panelW, height, 7f, 1f, Color.white, Color.black, false);
            TextAt(panel, ceremonyHeading, 16f, 12f, panelW - 32f, 40f, 24, Color.black, nameFont, TextAnchor.MiddleLeft);
            var y = 56f;
            if (reasonH > 0f)
            {
                TextAt(panel, ceremonyReason, 16f, y, panelW - 32f, lineH, 18, Color.black, nameFont, TextAnchor.MiddleLeft);
                y += lineH;
            }
            for (var i = 0; i < titleLines.Count; i++)
            {
                var line = titleLines[i];
                if (line.Gone) continue;
                TextAt(panel, line.Text, 16f, y, panelW - 48f, titleH, 16, Color.black, nameFont, TextAnchor.MiddleLeft);
                bonusMarks[i] = Portrait.Rect(panel, "bonus" + i, panelW - 36f, y + 7f, 20f, 20f);
                y += titleH;
            }
            if (ceremonyOverallSlot)
            {
                if (!string.IsNullOrEmpty(ceremonyRankTitle)) TextAt(panel, ceremonyRankTitle, 16f, y, panelW - 32f, lineH, 22, Color.black, nameFont, TextAnchor.MiddleLeft);
                y += lineH;
            }
            if (dialogOrder != null && match.Game != null)
            {
                const float figureX = 216f;
                for (var r = 0; r < dialogOrder.Count; r++)
                {
                    var seat = dialogOrder[r];
                    if (!ceremonyBlank)
                    {
                        var row = Portrait.Rect(panel, "row" + seat, 16f, y, panelW - 32f, rowH);
                        var rank = ceremonyPlaces != null && ceremonyPlaces.TryGetValue(seat, out var place) ? $"{place}位" : "";
                        TextAt(row, rank, 0f, 0f, 64f, rowH, 20, Color.black, nameFont, TextAnchor.MiddleLeft);
                        TextAt(row, match.Game.Players[seat].Name, 68f, 0f, 140f, rowH, 20, Color.black, nameFont, TextAnchor.MiddleLeft);
                        var figure = TextAt(row, FigureText(seat), figureX, 0f, panelW - 32f - figureX, rowH, 20, Color.black, nameFont, TextAnchor.MiddleRight);
                        figure.gameObject.name = "figure";
                        figure.supportRichText = true;
                    }
                    y += rowH;
                }
            }
            if (ceremonyWinner)
            {
                var cheer = TextAt(panel, WinnerText(), 16f, y, panelW - 32f, winnerH, 20, Color.black, nameFont, TextAnchor.MiddleLeft);
                cheer.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            if (!string.IsNullOrEmpty(ceremonyButton))
            {
                var caption = ceremonyButton;
                var buttonW = caption.Length * 32f + 48f;
                Pill(panel, caption, 16f, height - 80f, buttonW, 64f, 28, () =>
                {
                    if (reviewMode)
                    {
                        reviewMode = false;
                        ShowSetup();
                        return;
                    }
                    ceremonyOk = true;
                });
            }
            LayoutCeremonyDots();
        }

        void AssignPlaces(System.Func<int, int> scoreOf)
        {
            ceremonyPlaces = new Dictionary<int, int>();
            if (dialogOrder == null) return;
            var place = 1;
            for (var i = 0; i < dialogOrder.Count; i++)
            {
                if (i > 0 && scoreOf(dialogOrder[i]) != scoreOf(dialogOrder[i - 1])) place = i + 1;
                ceremonyPlaces[dialogOrder[i]] = place;
            }
        }

        string WinnerText()
        {
            if (!ceremonyWinner || ceremonyPlaces == null || dialogOrder == null || match.Game == null) return "";
            var names = new List<string>();
            foreach (var seat in dialogOrder)
            {
                if (ceremonyPlaces.TryGetValue(seat, out var place) && place == 1)
                    names.Add(match.Game.Players[seat].Name);
            }
            if (names.Count == 0) return "";
            return string.Join("さん、", names) + "さん、総合優勝おめでとうございます";
        }

        string FigureText(int seat)
        {
            var score = scoreOverride != null && scoreOverride.TryGetValue(seat, out var shown) ? shown : 0;
            var round = roundScores != null && roundScores.TryGetValue(seat, out var gained) ? gained : 0;
            var prev = previousScores != null && previousScores.TryGetValue(seat, out var before) ? before : 0;
            if (ceremonyEquation && score == prev + round) return $"{prev}+{round}={score}";
            if (plusOverride != null && plusOverride.TryGetValue(seat, out var plus))
                return $"{score}点  <color=#c45c26>+{plus}</color>";
            return score + "点";
        }

        void RedrawCeremonyPanel()
        {
            if (frame == null) return;
            var before = new Dictionary<int, float>();
            var existing = frame.Find("ceremony");
            if (existing != null)
            {
                for (var i = 0; i < existing.childCount; i++)
                {
                    var child = existing.GetChild(i);
                    if (!child.name.StartsWith("row")) continue;
                    if (!int.TryParse(child.name.Substring(3), out var seat)) continue;
                    before[seat] = ((RectTransform)child).anchoredPosition.y;
                }
                existing.name = "retired";
                Destroy(existing.gameObject);
            }
            DrawCeremonyPanel();
            if (before.Count > 0 && Application.isPlaying) StartCoroutine(SlideCeremonyRows(before));
        }

        IEnumerator SlideCeremonyRows(Dictionary<int, float> before)
        {
            var panel = frame != null ? frame.Find("ceremony") : null;
            if (panel == null) yield break;
            var rows = new List<RectTransform>();
            var from = new List<float>();
            var to = new List<float>();
            for (var i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (!child.name.StartsWith("row")) continue;
                if (!int.TryParse(child.name.Substring(3), out var seat)) continue;
                if (!before.TryGetValue(seat, out var oldY)) continue;
                var rect = (RectTransform)child;
                if (Mathf.Abs(oldY - rect.anchoredPosition.y) < 1f) continue;
                rows.Add(rect);
                from.Add(oldY);
                to.Add(rect.anchoredPosition.y);
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, oldY);
            }
            var start = Time.time;
            const float duration = 0.45f;
            while (Time.time - start < duration)
            {
                var along = Mathf.SmoothStep(0f, 1f, (Time.time - start) / duration);
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i] == null) continue;
                    rows[i].anchoredPosition = new Vector2(rows[i].anchoredPosition.x, Mathf.Lerp(from[i], to[i], along));
                }
                yield return null;
            }
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                rows[i].anchoredPosition = new Vector2(rows[i].anchoredPosition.x, to[i]);
            }
        }

        string SeatPoints(Game game, int index, Player player)
        {
            if (ceremonyRunning || reviewMode) return BaseScore(player) + "点";
            var points = game.AwaitingNextRound || game.Finished ? game.FinalScore(player) : BaseScore(player);
            return points + "点";
        }

        void RefreshScore(int seat)
        {
            if (frame == null) return;
            var host = frame.Find("ceremony/row" + seat + "/figure");
            if (host == null) return;
            var label = host.GetComponent<Text>();
            if (label == null) return;
            label.supportRichText = true;
            label.text = FigureText(seat);
        }

        List<int> SortBy(Dictionary<int, int> scores)
        {
            var game = match.Game;
            var seats = new List<int>();
            for (var i = 0; i < game.Players.Count; i++) seats.Add(i);
            var turn = game.TurnOrder;
            seats.Sort((a, b) =>
            {
                var diff = (scores.TryGetValue(b, out var right) ? right : 0).CompareTo(scores.TryGetValue(a, out var left) ? left : 0);
                if (diff != 0) return diff;
                return turn.IndexOf(a).CompareTo(turn.IndexOf(b));
            });
            return seats;
        }

        static List<int> Rotate(IReadOnlyList<int> cycle, int seat)
        {
            var rows = new List<int>();
            if (cycle == null) return rows;
            var start = -1;
            for (var i = 0; i < cycle.Count; i++) if (cycle[i] == seat) start = i;
            if (start < 0)
            {
                for (var i = 0; i < cycle.Count; i++) rows.Add(cycle[i]);
                return rows;
            }
            for (var i = 0; i < cycle.Count; i++) rows.Add(cycle[(start + i) % cycle.Count]);
            return rows;
        }

        Vector3 BonusPoint(int line)
        {
            if (bonusMarks.TryGetValue(line, out var mark) && mark != null) return CenterOf(mark);
            var panel = frame != null ? frame.Find("ceremony") as RectTransform : null;
            return panel != null ? CenterOf(panel) : Vector3.zero;
        }

        Vector3 TrayPoint(int seat)
        {
            var tray = TrayOf(seat) as RectTransform;
            return tray != null ? CenterOf(tray) : Vector3.zero;
        }

        Vector3 ScorePoint(int seat)
        {
            var host = frame != null ? frame.Find("ceremony/row" + seat + "/figure") as RectTransform : null;
            if (host == null) return TrayPoint(seat);
            var label = host.GetComponent<Text>();
            if (label == null) return CenterOf(host);
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            host.GetWorldCorners(corners);
            var rightMid = (corners[2] + corners[3]) * 0.5f;
            var leftMid = (corners[0] + corners[1]) * 0.5f;
            var width = host.rect.width;
            var textW = label.preferredWidth;
            if (width <= 1f || textW <= 1f) return rightMid;
            var inset = Mathf.Min(textW, width) * 0.5f / width;
            return Vector3.Lerp(rightMid, leftMid, inset);
        }

        static void PlaceCenter(RectTransform rect, Vector3 center)
        {
            if (rect == null) return;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            rect.position += center - (corners[0] + corners[2]) * 0.5f;
        }

        static Vector3 CenterOf(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        Transform TrayOf(int seat)
        {
            if (frame == null) return null;
            var seatFrame = frame.Find("seat" + seat);
            if (seatFrame == null) return null;
            var chip = seatFrame.Find("chip-tray");
            return chip != null ? chip : seatFrame.Find("bonus-box");
        }

        void SpawnDot(CeremonyDot dot)
        {
            var tray = TrayOf(dot.Seat);
            if (tray == null || tray.Find("cdot-" + dot.Serial) != null) return;
            if (WideScreen()) DrawStoredDot(tray, dot, 8f, 28f, 160f, 82f, 12f);
            else DrawStoredDot(tray, dot, 8f, 8f, 204f, 89f, 14f);
        }

        void LayoutCeremonyDots()
        {
            foreach (var dot in ceremonyTray) SpawnDot(dot);
        }

        Vector3 TakeDot(CeremonyDot dot)
        {
            ceremonyTray.Remove(dot);
            var tray = TrayOf(dot.Seat);
            var host = tray != null ? tray.Find("cdot-" + dot.Serial) as RectTransform : null;
            var from = host != null ? CenterOf(host) : TrayPoint(dot.Seat);
            if (host != null) Destroy(host.gameObject);
            return from;
        }

        void DrawStoredDot(Transform tray, CeremonyDot dot, float x, float y, float width, float height, float diameter)
        {
            var px = x + Centered(dot.Serial * 2 + 1) * Mathf.Max(0f, width - diameter);
            var py = y + Centered(dot.Serial * 2 + 5) * Mathf.Max(0f, height - diameter);
            var holder = Portrait.Rect(tray, "cdot-" + dot.Serial, px, py, diameter, diameter);
            Portrait.Circle(holder, "ring", -1f, -1f, diameter + 2f, Color.black);
            Portrait.Circle(holder, "disk", 0f, 0f, diameter, CoinColor(dot.Kind));
        }

        static Color CoinColor(CoinKind kind)
        {
            if (kind == CoinKind.Purple) return Hex("#a04bff");
            if (kind == CoinKind.Blue) return Hex("#3c7dff");
            return Hex("#3cce3c");
        }

        void CleanupFlyers()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name != "flyer" && !child.name.StartsWith("cdot-")) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        static int GreenCount(Player player)
        {
            var total = 0;
            var index = 0;
            while (index < player.Achieved.Count)
            {
                var rank = player.Achieved[index].Rank;
                if (rank == null) break;
                total += Cards.Bonus(rank.Value);
                index += rank.Value;
            }
            return total;
        }

        void DrawRoundBreak(Game game)
        {
            var reason = game.RoundEndReason == "DECK" ? "山札切れ" : "膠着の連続";
            const float width = 760f;
            var height = 220f + game.Players.Count * 36f;
            var panel = WideScreen()
                ? Portrait.Box(frame, "round-break", (LandWidth - width) * 0.5f, (LandHeight - height) * 0.5f, width, height, 7f, 1f, Color.white, Color.black, false)
                : Portrait.Box(frame, "round-break", (ScreenWidth - width) * 0.5f, 640f, width, height, 7f, 1f, Color.white, Color.black, false);
            TextAt(panel, $"第{game.RoundIndex}ラウンド終了（{reason}）", 32f, 24f, width - 64f, 48f, 32, Color.black, nameFont, TextAnchor.MiddleLeft);
            var y = 84f;
            foreach (var group in game.Ranking())
            {
                foreach (var seat in group)
                {
                    var player = game.Players[seat];
                    TextAt(panel, $"{player.Name}  {game.FinalScore(player)}点", 32f, y, width - 64f, 36f, 24, Color.black, nameFont, TextAnchor.MiddleLeft);
                    y += 36f;
                }
            }
            Pill(panel, "次のラウンド", 32f, height - 96f, 280f, 72f, 32, () =>
            {
                match.Game.BeginNextRound();
                ShowTable();
            });
        }

        void DrawTitle(Game game)
        {
            var mark = TitleSprite(true);
            if (mark != null) PlaceSprite(frame, "title-mark", mark, 28f, 12f, 320f, 64f);
            else TextAt(frame, "QUOTA", 28f, 16f, 640f, 68f, 56, Color.white, nameFont, TextAnchor.MiddleLeft);
            TextAt(frame, RoundLabel(game), 28f, 84f, 640f, 32f, 24, Color.white, nameFont, TextAnchor.MiddleLeft);
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

        void DrawTitleWide(Game game)
        {
            var mark = TitleSprite(true);
            if (mark != null) PlaceSprite(frame, "title-mark", mark, 28f, 24f, 260f, 52f);
            else TextAt(frame, "QUOTA", 28f, 28f, 280f, 64f, 48, Color.white, nameFont, TextAnchor.MiddleLeft);
            TextAt(frame, RoundLabel(game), 300f, 48f, 280f, 36f, 26, Color.white, nameFont, TextAnchor.MiddleLeft);
            var note = "";
            if (game.DoubleStage == 1) note = "ダブル：1回目の行動です。  ";
            else if (game.DoubleStage == 2) note = "ダブル：2回目の行動です。  ";
            else if (game.Plan == "reshuffle") note = "配り直しました。行動を選んでください。  ";
            var me = game.Players[game.Current];
            var hint = $"手番 {game.TurnNumber}  山札 {game.Deck.Count}  膠着 {(game.StallFlag ? 1 : 0)}/{game.Players.Count}";
            if (me.Quota != null && me.Quota.Rank != null)
            {
                var need = me.Quota.Rank.Value - 1 - me.Collection.Count;
                if (need > 0) hint = $"あと{need}枚   " + hint;
            }
            TextAt(frame, note + hint, 600f, 44f, 1000f, 36f, 20, Color.white, nameFont, TextAnchor.MiddleLeft);
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

        void DrawMarketWide(Game game, ItemSet theme)
        {
            const int columns = 4;
            var slots = Mathf.Max(game.MarketSize(), game.Market.Count);
            if (slots == 0) return;
            var gapX = 18f;
            var gapY = 16f;
            var rows = Mathf.CeilToInt(slots / (float)columns);
            var groupW = columns * CardWidth + (columns - 1) * gapX;
            var groupH = rows * CardHeight + (rows - 1) * gapY;
            var originX = LandMarketX + (LandMarketW - groupW) * 0.5f;
            var originY = LandMarketY + (LandMarketH - groupH) * 0.5f;
            var me = game.Players[game.Current];
            var yours = match.IsHumanTurn && !busy && !game.Finished;
            for (var i = 0; i < game.Market.Count; i++)
            {
                var card = game.Market[i];
                if (card == null) continue;
                var playable = yours && CanPlay(card, me);
                var cardId = card.Id;
                var takingQuota = me.Quota == null;
                var x = originX + (i % columns) * (CardWidth + gapX);
                var y = originY + (i / columns) * (CardHeight + gapY);
                DrawCard(frame, theme, card, x, y, 1f, playable ? () =>
                {
                    if (takingQuota) Play(new TakeQuota(cardId));
                    else Play(new Collect(new[] { cardId }));
                } : null, yours && !playable);
            }
        }

        static int NameFontSize(string name, float width, int preferred)
        {
            if (string.IsNullOrEmpty(name)) return preferred;
            var fitted = Mathf.FloorToInt(width * 0.92f / name.Length);
            return Mathf.Clamp(fitted, 18, preferred);
        }

        void DrawPlayer(Game game, ItemSet theme, int index, int row)
        {
            var player = game.Players[index];
            var top = SeatTop + row * SeatHeight;
            var seat = Portrait.Rect(frame, "seat" + index, 0f, top, ScreenWidth, SeatHeight);
            while (seatFrames.Count <= index) seatFrames.Add(null);
            seatFrames[index] = seat;
            Portrait.Box(seat, "plate", 25f, 25f, 1030f, 340f, 7f, 1f, new Color(1f, 1f, 1f, 0.7f), Color.black, false);
            Portrait.Box(seat, "nameplate", 0f, 10f, 300f, 50f, 4.5f, 1f, Color.white, Color.black, true);
            var name = TextAt(seat, player.Name, 12f, 10f, 276f, 50f, NameFontSize(player.Name, 276f, 36), Color.black, nameFont, TextAnchor.MiddleLeft);
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
            var tray = Portrait.Box(seat, "chip-tray", 775f, 240f, 220f, 105f, 7f, 1f, Color.white, Color.black, false);
            PlaceBonus(game, player, quotaCards, tray, 8f, 8f, 204f, 89f, 14f, 1f);
            var side = SeatPoints(game, index, player);
            if (!(ceremonyRunning || reviewMode) && game.Config.SpecialActionsRule)
                side += $"\nダブル {(player.DoubleActionLeft > 0 ? "残1" : "済")}\n配り直し {(player.ReshuffleTakeLeft > 0 ? "残1" : "済")}";
            if (!(ceremonyRunning || reviewMode) && index == game.Current && !game.Finished) side = "▶ " + side;
            var score = TextAt(seat, side, 898f, quotaTop, 170f, 140f, 20, Color.black, nameFont, TextAnchor.UpperLeft);
            score.gameObject.name = "score";
            score.supportRichText = true;
        }

        void DrawPlayerWide(Game game, ItemSet theme, int index, int row)
        {
            var player = game.Players[index];
            var top = LandHeader + row * LandSeatHeight;
            var seat = Portrait.Rect(frame, "seat" + index, 0f, top, LandLeft, LandSeatHeight);
            while (seatFrames.Count <= index) seatFrames.Add(null);
            seatFrames[index] = seat;
            var current = index == game.Current && !game.Finished;
            Portrait.Box(seat, "plate", 16f, 22f, 1188f, 206f, 7f, 1f, new Color(1f, 1f, 1f, 0.7f), Color.black, false);
            Portrait.Box(seat, "nameplate", 16f, 6f, 270f, 46f, 4.5f, 1f, current ? Hex("#ffe56a") : Color.white, Color.black, false);
            var name = TextAt(seat, player.Name, 28f, 6f, 246f, 46f, NameFontSize(player.Name, 246f, 30), Color.black, nameFont, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (game.Config.SpecialActionsRule)
            {
                var uses = $"ダブル {(player.DoubleActionLeft > 0 ? "残1" : "済")}　配り直し {(player.ReshuffleTakeLeft > 0 ? "残1" : "済")}";
                TextAt(seat, uses, 860f, 14f, 320f, 28f, 16, Color.black, nameFont, TextAnchor.MiddleRight);
            }

            const float boxY = 66f;
            const float boxH = 148f;
            Portrait.Box(seat, "bonus-box", 28f, boxY, 176f, boxH, 7f, 1f, Color.white, Color.black, false);
            TextAt(seat, "ボーナス", 36f, boxY + 4f, 120f, 24f, 14, Color.black, nameFont, TextAnchor.MiddleLeft);
            var wideScore = TextAt(seat, SeatPoints(game, index, player), 36f, boxY + boxH - 30f, 152f, 24f, 16, Color.black, nameFont, TextAnchor.MiddleLeft);
            wideScore.gameObject.name = "score";
            wideScore.supportRichText = true;

            Portrait.Box(seat, "record-box", 216f, boxY, 220f, boxH, 7f, 1f, Color.white, Color.black, false);
            TextAt(seat, "実績", 224f, boxY + 4f, 80f, 24f, 14, Color.black, nameFont, TextAnchor.MiddleLeft);
            var achieved = Portrait.Rect(seat, "achieved-cards", 224f, boxY + 30f, 200f, boxH - 38f);
            achieved.gameObject.AddComponent<RectMask2D>();
            LayCards(achieved, theme, player.Achieved, 2f, 3f, 0.62f);

            TextAt(seat, "ノルマ", 452f, boxY, 80f, 24f, 14, Color.black, nameFont, TextAnchor.MiddleLeft);
            var quotaCards = Portrait.Rect(seat, "quota-cards", 452f, boxY + 22f, 730f, CardHeight + 4f);
            quotaCards.gameObject.AddComponent<RectMask2D>();
            var strip = new List<Card>();
            if (player.Quota != null) strip.Add(player.Quota);
            strip.AddRange(player.Collection);
            var stride = CardWidth + 8f;
            if (strip.Count > 1)
            {
                var need = CardWidth + (strip.Count - 1) * stride;
                if (need > 730f) stride = (730f - CardWidth) / (strip.Count - 1);
            }
            LayCards(quotaCards, theme, strip, 0f, stride, 1f);
            var bonusBox = seat.Find("bonus-box");
            PlaceBonus(game, player, quotaCards, bonusBox, 8f, 28f, 160f, 82f, 12f, 1f);
        }

        void PlaceBonus(Game game, Player player, Transform quotaArea, Transform tray, float x, float y, float width, float height, float diameter, float cardScale)
        {
            var line = new List<Card>();
            if (player.Quota != null)
            {
                line.Add(player.Quota);
                line.AddRange(player.Collection);
            }
            var title = game.Finished ? game.TitlePoints(player) : 0;
            var coins = BonusCoins.Plan(player.Achieved, line, game.Config.SequenceRule, title);
            var parked = new Dictionary<int, List<BonusCoin>>();
            var bank = new List<BonusCoin>();
            foreach (var coin in coins)
            {
                if (coin.InTray)
                {
                    bank.Add(coin);
                    continue;
                }
                if (!parked.TryGetValue(coin.CardId, out var pile))
                {
                    pile = new List<BonusCoin>();
                    parked[coin.CardId] = pile;
                }
                pile.Add(coin);
            }
            foreach (var pair in parked)
            {
                var host = quotaArea.Find("card" + pair.Key);
                if (host == null) continue;
                var step = (diameter + 1f) * cardScale;
                for (var i = 0; i < pair.Value.Count; i++)
                    DrawCoin(host, pair.Value[i], 4f * cardScale, (28f * cardScale) + i * step, diameter * cardScale);
            }
            if (ceremonyRunning)
            {
                var seat = -1;
                for (var i = 0; i < game.Players.Count; i++)
                    if (ReferenceEquals(game.Players[i], player)) seat = i;
                for (var i = 0; i < ceremonyTray.Count; i++)
                {
                    var dot = ceremonyTray[i];
                    if (dot.Seat != seat || tray.Find("cdot-" + dot.Serial) != null) continue;
                    DrawStoredDot(tray, dot, x, y, width, height, diameter);
                }
                return;
            }
            var slides = new List<CoinSlide>();
            for (var i = 0; i < bank.Count; i++)
            {
                var coin = bank[i];
                var px = x + Centered(coin.Serial * 2 + 1) * Mathf.Max(0f, width - diameter);
                var py = y + Centered(coin.Serial * 2 + 5) * Mathf.Max(0f, height - diameter);
                var drawn = DrawCoin(tray, coin, px, py, diameter);
                var spot = "spot-" + coin.Key;
                if (!Application.isPlaying || !coinFrom.TryGetValue(spot, out var fromRing)) continue;
                slides.Add(new CoinSlide
                {
                    Ring = drawn.ring,
                    Disk = drawn.disk,
                    ToRing = drawn.ring.position,
                    ToDisk = drawn.disk.position,
                    FromRing = fromRing,
                    FromDisk = drawn.disk.position + (fromRing - drawn.ring.position),
                    FromX = fromRing.x,
                    Serial = coin.Serial,
                });
            }
            if (slides.Count == 0) return;
            slides.Sort((a, b) =>
            {
                var order = a.FromX.CompareTo(b.FromX);
                return order != 0 ? order : a.Serial.CompareTo(b.Serial);
            });
            for (var i = 0; i < slides.Count; i++)
            {
                slides[i].Delay = i * 0.1f;
                slides[i].Ring.SetAsLastSibling();
                slides[i].Disk.SetAsLastSibling();
            }
            coinMotion++;
            StartCoroutine(SlideCoins(slides));
        }

        (RectTransform ring, RectTransform disk) DrawCoin(Transform parent, BonusCoin coin, float x, float y, float diameter)
        {
            var color = coin.Kind == CoinKind.Purple ? Hex("#a04bff") : coin.Kind == CoinKind.Blue ? Hex("#3c7dff") : Hex("#3cce3c");
            var ring = Portrait.Circle(parent, "spot-" + coin.Key, x - 1f, y - 1f, diameter + 2f, Color.black);
            var disk = Portrait.Circle(parent, "coin", x, y, diameter, color);
            return (ring, disk);
        }

        Dictionary<string, Vector3> SnapshotCardCoins()
        {
            var spots = new Dictionary<string, Vector3>();
            if (!Application.isPlaying || frame == null) return spots;
            foreach (var rect in frame.GetComponentsInChildren<RectTransform>(true))
            {
                if (!rect.name.StartsWith("spot-") || rect.parent == null || !rect.parent.name.StartsWith("card")) continue;
                spots[rect.name] = rect.position;
            }
            return spots;
        }

        IEnumerator SlideCoins(List<CoinSlide> slides)
        {
            foreach (var slide in slides)
            {
                if (slide.Ring != null) slide.Ring.position = slide.FromRing;
                if (slide.Disk != null) slide.Disk.position = slide.FromDisk;
            }
            var started = Time.time;
            var span = slides[slides.Count - 1].Delay + 0.3f;
            while (Time.time - started < span)
            {
                var now = Time.time - started;
                var alive = false;
                foreach (var slide in slides)
                {
                    if (slide.Ring == null) continue;
                    alive = true;
                    var along = now <= slide.Delay ? 0f : Mathf.Clamp01((now - slide.Delay) / 0.3f);
                    slide.Ring.position = Vector3.Lerp(slide.FromRing, slide.ToRing, along);
                    if (slide.Disk != null) slide.Disk.position = Vector3.Lerp(slide.FromDisk, slide.ToDisk, along);
                }
                if (!alive) break;
                yield return null;
            }
            foreach (var slide in slides)
            {
                if (slide.Ring != null) slide.Ring.position = slide.ToRing;
                if (slide.Disk != null) slide.Disk.position = slide.ToDisk;
            }
            coinMotion = Mathf.Max(0, coinMotion - 1);
        }

        sealed class CoinSlide
        {
            public RectTransform Ring;
            public RectTransform Disk;
            public Vector3 FromRing;
            public Vector3 ToRing;
            public Vector3 FromDisk;
            public Vector3 ToDisk;
            public float FromX;
            public float Delay;
            public int Serial;
        }

        static float Centered(int seed)
        {
            return (Hash01(seed) + Hash01(unchecked(seed * 747796405 + 13))) * 0.5f;
        }

        static float Hash01(int seed)
        {
            unchecked
            {
                var value = (uint)seed * 747796405u + 2891336453u;
                var shift = (int)(value >> 28) + 4;
                value = ((value >> shift) + 4) ^ value;
                value *= 277803737u;
                return (value >> 8) * (1f / 16777216f);
            }
        }

        void LayCards(RectTransform area, ItemSet theme, IReadOnlyList<Card> cards, float padding, float stride, float scale = 1f)
        {
            var x = padding;
            var y = padding;
            foreach (var card in cards)
            {
                DrawCard(area, theme, card, x, y, scale, null, false);
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
            var wide = WideScreen();
            var controls = wide
                ? Portrait.Rect(frame, "controls", 0f, 0f, LandWidth, LandHeight)
                : Portrait.Rect(frame, "controls", 0f, SeatTop + RowOf(game, game.Current) * SeatHeight, ScreenWidth, SeatHeight);
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
            var edge = wide ? LandWidth - LandButtonRight : ScreenWidth;
            var y = wide ? LandMarketY + LandMarketH + 16f : 0f;
            for (var i = 0; i < entries.Count; i++)
            {
                var width = entries[i].Key.Length * 48f + 30f;
                Pill(controls, entries[i].Key, edge - width, y + i * ActionStride, width, 72f, 48, entries[i].Value);
            }
        }

        void LeaveButton()
        {
            const string caption = "ゲームから抜ける";
            var width = caption.Length * 28f + 8f;
            var x = WideScreen() ? LandWidth - 24f - width : ScreenWidth - 20f - width;
            var y = WideScreen() ? 40f : 18f;
            var host = Portrait.Rect(frame, caption, x, y, width, 40f);
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
                run = LeaveMatch;
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
            var hasHuman = false;
            for (var i = 0; i < game.Players.Count; i++)
                if (game.Players[i].IsHuman) hasHuman = true;
            const float panelWidth = 700f;
            const float panelHeight = 280f;
            RectTransform panel;
            if (WideScreen() || (confirm == "leave" && !hasHuman))
                panel = Portrait.Box(frame, "confirm", ((WideScreen() ? LandWidth : ScreenWidth) - panelWidth) * 0.5f, ((WideScreen() ? LandHeight : ScreenHeight) - panelHeight) * 0.5f, panelWidth, panelHeight, 7f, 1f, Color.white, Color.black, false);
            else
            {
                var seat = seatFrames[seatIndex];
                panel = Portrait.Box(seat, "confirm", 25f + (1030f - panelWidth) * 0.5f, 25f + (340f - panelHeight) * 0.5f, panelWidth, panelHeight, 7f, 1f, Color.white, Color.black, false);
            }
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

        void NoteFinish()
        {
            if (!Application.isPlaying || finishCounted) return;
            finishCounted = true;
            if (PlayerPrefs.GetInt("quota.prompted", 0) == 1) return;
            var games = PlayerPrefs.GetInt("quota.games", 0) + 1;
            PlayerPrefs.SetInt("quota.games", games);
            PlayerPrefs.Save();
            if (games >= AdvancedPromptAfter && simpleMode) offerStandard = true;
        }

        void DrawStandardOffer()
        {
            var veilW = WideScreen() ? LandWidth : ScreenWidth;
            var veilH = WideScreen() ? LandHeight : ScreenHeight;
            var veil = Portrait.Rect(frame, "offer", 0f, 0f, veilW, veilH);
            var shade = veil.gameObject.AddComponent<Image>();
            shade.sprite = Portrait.White;
            shade.color = new Color(0f, 0f, 0f, 0.35f);
            shade.raycastTarget = true;
            var offerX = WideScreen() ? (LandWidth - 800f) * 0.5f : 140f;
            var offerY = WideScreen() ? (LandHeight - 340f) * 0.5f : 760f;
            var panel = Portrait.Box(veil, "offer-card", offerX, offerY, 800f, 340f, 7f, 1f, Color.white, Color.black, false);
            TextAt(panel, "シンプルモードをオフにして標準ルールに戻しますか？", 32f, 36f, 736f, 140f, 28, Color.black, nameFont, TextAnchor.MiddleCenter);
            Pill(panel, "はい", 48f, 210f, 200f, 72f, 32, () =>
            {
                simpleMode = false;
                ApplyMode();
                SaveRules();
                PlayerPrefs.SetInt("quota.prompted", 1);
                PlayerPrefs.Save();
                offerStandard = false;
                ShowTable();
            });
            Pill(panel, "いいえ", 800f - 48f - 240f, 210f, 240f, 72f, 32, () =>
            {
                PlayerPrefs.SetInt("quota.prompted", 1);
                PlayerPrefs.Save();
                offerStandard = false;
                ShowTable();
            });
        }

        void Result(Game game, string button = "もう一局")
        {
            NoteFinish();
            var resultX = WideScreen() ? (LandWidth - 900f) * 0.5f : 90f;
            var resultY = WideScreen() ? 80f : 430f;
            var resultH = WideScreen() ? 920f : 1100f;
            var panel = Portrait.Box(frame, "result", resultX, resultY, 900f, resultH, 7f, 1f, Color.white, Color.black, false);
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
            var width = Mathf.Max(280f, button.Length * 32f + 48f);
            Pill(panel, button, 32f, resultH - 100f, width, 72f, 32, ShowSetup);
        }

        void LeaveMatch()
        {
            confirm = null;
            busy = false;
            cpuRun++;
            ceremonyRunning = false;
            ceremonyDialog = false;
            match.Clear();
            CleanupFlyers();
            ShowSetup();
        }

        void Play(GameAction action)
        {
            if (busy || !match.IsHumanTurn || !match.Game.IsLegal(action)) return;
            confirm = null;
            var seat = match.Game.Current;
            var turn = match.Game.TurnNumber;
            Characters.Observe(match.Game, action);
            match.Game.Step(action);
            Characters.CommitIfTurnEnded(match.Game, seat, turn);
            StartCoroutine(RunCpus(++cpuRun));
        }

        IEnumerator RunCpus(int ticket)
        {
            if (ticket != cpuRun || match.Game == null) yield break;
            busy = true;
            ShowTable();
            yield return WaitForCoins();
            yield return WaitConfirm(ticket);
            if (ticket != cpuRun || match.Game == null) yield break;
            var wait = cpuNotBefore - Time.time;
            if (wait > 0f && match.Game != null && !match.IsHumanTurn && !match.Game.Finished)
                yield return new WaitForSeconds(wait);
            if (ticket != cpuRun || match.Game == null) yield break;
            yield return WaitConfirm(ticket);
            if (ticket != cpuRun || match.Game == null) yield break;
            while (match.Game != null && !match.Game.Finished && !match.IsHumanTurn)
            {
                yield return WaitConfirm(ticket);
                if (ticket != cpuRun || match.Game == null) yield break;
                if (match.Game.Finished || match.IsHumanTurn) break;
                if (!match.StepOneCpu()) break;
                ShowTable();
                yield return WaitForCoins();
                yield return new WaitForSeconds(0.35f);
                if (ticket != cpuRun || match.Game == null) yield break;
            }
            busy = false;
            if (ticket == cpuRun && confirm == null && match.Game != null) ShowTable();
        }

        IEnumerator WaitConfirm(int ticket)
        {
            while (confirm != null)
            {
                if (ticket != cpuRun || match.Game == null) yield break;
                yield return null;
            }
        }

        IEnumerator WaitForCoins()
        {
            while (coinMotion > 0) yield return null;
        }

        List<int> DisplayRows(Game game)
        {
            var count = game.Players.Count;
            var cycle = game.TurnOrder.Count == count ? game.TurnOrder : new List<int>();
            if (cycle.Count != count)
            {
                cycle = new List<int>();
                for (var i = 0; i < count; i++) cycle.Add(i);
            }
            var anchor = cycle[0];
            for (var i = 0; i < count; i++)
            {
                if (!game.Players[i].IsHuman) continue;
                anchor = i;
                break;
            }
            var start = cycle.IndexOf(anchor);
            if (start < 0) start = 0;
            var rows = new List<int>();
            for (var i = 0; i < count; i++) rows.Add(cycle[(start + i) % count]);
            return rows;
        }

        int RowOf(Game game, int seat)
        {
            var row = DisplayRows(game).IndexOf(seat);
            return row < 0 ? seat : row;
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

        void DrawSetupPage(bool wide)
        {
            var screenW = wide ? LandWidth : ScreenWidth;
            var screenH = wide ? LandHeight : ScreenHeight;
            var veil = Portrait.Solid(frame, "setup-veil", 0f, 0f, screenW, screenH, new Color(0f, 0f, 0f, 0.28f));
            veil.GetComponent<Image>().raycastTarget = true;
            if (setupPage == "settings") DrawSettings(screenW, screenH);
            else DrawGuide(screenW, screenH);
        }

        void DrawSettings(float screenW, float screenH)
        {
            const float panelW = 860f;
            const float panelH = 560f;
            var panel = Portrait.Box(frame, "setup-dialog", (screenW - panelW) * 0.5f, (screenH - panelH) * 0.5f, panelW, panelH, 7f, 1f, Color.white, Color.black, false);
            TextAt(panel, "設定", 32f, 24f, panelW - 64f, 48f, 32, Color.black, nameFont, TextAnchor.MiddleLeft);
            Pill(panel, draftPro ? "新プロモード　オン" : "新プロモード　オフ", 32f, 96f, panelW - 64f, 72f, 28, () =>
            {
                draftPro = !draftPro;
                ShowSetup();
            });
            var ok = DialogField(panel, "OKタイムアウト（秒）", draftOk, 32f, 196f, 380f);
            ok.onValueChanged.AddListener(value => draftOk = value);
            var turn = DialogField(panel, "手番タイムアウト（秒）", draftTurn, 440f, 196f, 380f);
            turn.onValueChanged.AddListener(value => draftTurn = value);
            Pill(panel, "設定", 32f, panelH - 112f, 200f, 72f, 28, ApplySettings);
            Pill(panel, "キャンセル", 252f, panelH - 112f, 240f, 72f, 28, () =>
            {
                setupPage = null;
                ShowSetup();
            });
        }

        void DrawGuide(float screenW, float screenH)
        {
            var panelW = Mathf.Min(980f, screenW - 48f);
            var panelH = Mathf.Min(screenH - 80f, screenW > screenH ? 820f : 1400f);
            var panel = Portrait.Box(frame, "setup-dialog", (screenW - panelW) * 0.5f, (screenH - panelH) * 0.5f, panelW, panelH, 7f, 1f, Color.white, Color.black, false);
            TextAt(panel, GuideCopy.Title(setupPage), 32f, 20f, panelW - 64f, 48f, 32, Color.black, nameFont, TextAnchor.MiddleLeft);
            var viewW = panelW - 64f;
            var viewH = panelH - 180f;
            var viewport = Portrait.Rect(panel, "guide-view", 32f, 80f, viewW, viewH);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Portrait.Rect(viewport, "guide-body", 0f, 0f, viewW, viewH);
            var text = TextAt(content, GuideCopy.Body(setupPage), 0f, 0f, viewW, viewH, 24, Color.black, nameFont, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Canvas.ForceUpdateCanvases();
            var bodyH = Mathf.Max(viewH, text.preferredHeight + 16f);
            content.sizeDelta = new Vector2(viewW, bodyH);
            text.rectTransform.sizeDelta = new Vector2(viewW, bodyH);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            Pill(panel, "OK", 32f, panelH - 84f, 160f, 64f, 28, () =>
            {
                setupPage = null;
                ShowSetup();
            });
        }

        InputField DialogField(Transform parent, string caption, string value, float x, float y, float width)
        {
            TextAt(parent, caption, x, y, width, 28f, 22, Color.black, nameFont, TextAnchor.MiddleLeft);
            var host = Portrait.Box(parent, caption, x, y + 32f, width, 56f, 4f, 1f, Color.white, Color.black, false);
            host.GetComponent<Image>().raycastTarget = true;
            var textGo = new GameObject("text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(host, false);
            Stretch(textGo.GetComponent<RectTransform>(), 12f, 8f);
            var text = textGo.GetComponent<Text>();
            text.font = nameFont;
            text.fontSize = 28;
            text.color = Color.black;
            text.supportRichText = false;
            var field = host.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.text = value;
            return field;
        }

        string HumanName()
        {
            var human = playerName == null ? "" : playerName.Trim();
            if (human.Length > 24) human = human.Substring(0, 24);
            return human.Length == 0 ? "あなた" : human;
        }

        Sprite TitleSprite(bool mark)
        {
            if (mark)
            {
                if (titleMark == null) titleMark = ReadSprite("title1.png");
                return titleMark;
            }
            if (catchMark == null) catchMark = ReadSprite("title2.png");
            return catchMark;
        }

        static Sprite ReadSprite(string fileName)
        {
            var path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path))) return null;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        static void PlaceSprite(Transform parent, string name, Sprite sprite, float x, float y, float width, float height)
        {
            var host = Portrait.Rect(parent, name, x, y, width, height);
            var image = host.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
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
            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(names, 32);
                    if (font != null) return font;
                }
                catch (System.Exception)
                {
                }
            }
            var embedded = Resources.Load<Font>("NotoSansJP-Regular");
            if (embedded != null) return embedded;
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

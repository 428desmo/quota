using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        const string BuildStamp = "UNITY-WEBGL splash-harbor";
        const float MarketScale = 1.35f;
        const float CardWidth = 95f;
        const float CardHeight = 132f;
        const float GoodsNameSize = 14f;

        static readonly Color Ink = Hex("#2C221E");
        static readonly Color Cream = Hex("#F6F1E8");
        static readonly Color Ecru = Hex("#F3EDE4");
        static readonly Color Accent = Hex("#8C3D2A");
        static readonly Color Plate = new Color(0.953f, 0.929f, 0.894f, 1f);
        static readonly Color Paper = new Color(0.965f, 0.945f, 0.910f, 1f);
        static readonly Color Field = Hex("#FFF8F0");
        static readonly Color DimTint = new Color(0.78f, 0.74f, 0.68f, 1f);

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
        bool backdropDim;
        SpriteRenderer backdropStage;
        VolumeProfile gradeProfile;
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
        bool lobbyOpen;
        bool sitOut;
        bool onSetup = true;
        readonly List<int> lobbyCast = new List<int>();
        readonly System.Random lobbyRng = new System.Random();
        NetworkSnapshot networkState;
        readonly List<NetworkTable> networkTables = new List<NetworkTable>();
        string networkSignature = "";
        bool networkRequest;
        bool networkNavigating;
        string draftOk = "5";
        string draftTurn = "120";
        bool draftSimple;
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
        float ceremonyExpand;
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
        List<int> ceremonyRankSlots;
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
                if (Application.isPlaying) StartCoroutine(BootWeb());
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
            backdrop.color = Hex("#1A1410");
            backdrop.raycastTarget = false;
            frame = Portrait.Rect(root, "Frame", 0f, 0f, ScreenWidth, ScreenHeight);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            EnsureBackdropGrade();
            Fit();
            LoadRules();
            if (Application.isPlaying) StartCoroutine(PollNetworkLobby());
            if (Application.platform != RuntimePlatform.WebGLPlayer) ShowSplash();
        }

        void OnDestroy()
        {
            if (gradeProfile == null) return;
            if (Application.isPlaying) Destroy(gradeProfile);
            else DestroyImmediate(gradeProfile);
        }

        void EnsureBackdropGrade()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(transform, false);
                camera = cameraObject.AddComponent<Camera>();
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("#1A1410");
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 40f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            var extra = camera.GetUniversalAdditionalCameraData();
            // Safari / iOS WebGL loses the context when URP post-process shaders
            // are compiled, so the grade stays on the native builds only.
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                extra.renderPostProcessing = false;
                if (backdropStage != null) backdropStage.enabled = false;
                return;
            }
            extra.renderPostProcessing = true;
            extra.volumeLayerMask = ~0;
            if (backdropStage == null)
            {
                var stage = new GameObject("BackdropStage");
                stage.transform.SetParent(transform, false);
                backdropStage = stage.AddComponent<SpriteRenderer>();
                backdropStage.sortingOrder = -20;
            }
            if (gradeProfile != null) return;
            var volumeObject = new GameObject("BackdropGrade");
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            gradeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            gradeProfile.hideFlags = HideFlags.HideAndDontSave;
            var vignette = gradeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.36f);
            vignette.smoothness.Override(0.48f);
            vignette.color.Override(Hex("#140E0B"));
            vignette.rounded.Override(false);
            var grade = gradeProfile.Add<ColorAdjustments>(true);
            grade.saturation.Override(-16f);
            grade.contrast.Override(10f);
            volume.sharedProfile = gradeProfile;
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
            if (onSetup || match.Game == null) ShowSetup();
            else ShowTable();
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
                var cover = Mathf.Max(Screen.width / sprite.rect.width, Screen.height / sprite.rect.height);
                backdrop.rectTransform.sizeDelta = new Vector2(sprite.rect.width * cover, sprite.rect.height * cover);
                PresentBackdrop();
            }
            var wide = WideScreen();
            var designW = wide ? LandWidth : ScreenWidth;
            var designH = wide ? LandHeight : ScreenHeight;
            frame.sizeDelta = new Vector2(designW, designH);
            var scale = Mathf.Min(Screen.width / designW, Screen.height / designH);
            frame.localScale = new Vector3(scale, scale, 1f);
        }

        void PresentBackdrop()
        {
            if (backdrop == null) return;
            var tint = backdropDim ? DimTint : Color.white;
            var camera = Camera.main;
            if (backdropStage != null && backdrop.sprite != null && camera != null && camera.orthographic)
            {
                backdropStage.sprite = backdrop.sprite;
                backdropStage.color = tint;
                backdropStage.transform.position = new Vector3(camera.transform.position.x, camera.transform.position.y, 0f);
                var worldHeight = camera.orthographicSize * 2f;
                var worldWidth = worldHeight * Mathf.Max(0.01f, camera.aspect);
                var size = backdrop.sprite.bounds.size;
                if (size.x > 0.01f && size.y > 0.01f)
                {
                    var cover = Mathf.Max(worldWidth / size.x, worldHeight / size.y);
                    backdropStage.transform.localScale = new Vector3(cover, cover, 1f);
                }
                backdrop.color = new Color(tint.r, tint.g, tint.b, 0f);
                backdropStage.enabled = true;
                return;
            }
            backdrop.color = tint;
        }

        static Sprite LoadBackground(string fileName)
        {
            var path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!texture.LoadImage(File.ReadAllBytes(path))) return null;
            return MakeSprite(FitTexture(texture));
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
                    sprite = MakeSprite(FitTexture(texture));
            }
            goodsSprites[file] = sprite;
            return sprite;
        }

        void ShowSplash()
        {
            onSetup = true;
            Clear();
            UseFrame();
            var wide = WideScreen();
            var width = wide ? LandWidth : ScreenWidth;
            var height = wide ? LandHeight : ScreenHeight;
            var splash = Portrait.Rect(frame, "splash", 0f, 0f, width, height);
            splash.gameObject.AddComponent<CanvasGroup>();
            var panelW = wide ? 1040f : 880f;
            var panelH = 320f;
            var panelX = (width - panelW) * 0.5f;
            var panelY = wide ? (height - panelH) * 0.5f : 800f;
            SoftPanel(splash, "splash-panel", panelX, panelY, panelW, panelH, Paper);
            var copy = TextAt(splash, SplashCopy, panelX + 40f, panelY + 28f, panelW - 80f, panelH - 56f, 34, Ink, nameFont, TextAnchor.MiddleCenter);
            copy.horizontalOverflow = HorizontalWrapMode.Overflow;
            copy.lineSpacing = 1.15f;
            Shade(TextAt(splash, BuildStamp, 24f, height - 72f, width - 48f, 40f, 28, Cream, nameFont, TextAnchor.MiddleCenter));
            if (Application.isPlaying) splashRun = StartCoroutine(FadeSplash());
        }

        IEnumerator BootWeb()
        {
            ShowSplash();
            Sprite vertical = null;
            yield return LoadSprite("vertical_base.jpg", sprite => vertical = sprite);
            verticalBackground = vertical;
            Sprite horizontal = null;
            yield return LoadSprite("horizontal_base.jpg", sprite => horizontal = sprite);
            horizontalBackground = horizontal;
            Fit();
            string json = null;
            yield return LoadText("quota_goods_v1.0.json", text => json = text);
            if (!string.IsNullOrEmpty(json)) ItemCatalog.LoadJson(json);
            string ranking = null;
            yield return LoadText(Ranking.FileName, text => ranking = text);
            Ranking.LoadJson(ranking);
            Sprite loadedTitle = null;
            yield return LoadSprite("title1.png", sprite => loadedTitle = sprite);
            if (loadedTitle != null) titleMark = loadedTitle;
            Sprite loadedCatch = null;
            yield return LoadSprite("title2.png", sprite => loadedCatch = sprite);
            if (loadedCatch != null) catchMark = loadedCatch;
            webAssetsReady = true;
            Fit();
            if (ItemCatalog.IsLoaded)
            {
                foreach (var file in ItemCatalog.PictureFiles())
                {
                    Sprite picture = null;
                    yield return LoadSprite("goods/" + file + ".png", sprite => picture = sprite);
                    if (picture != null) goodsSprites[file] = picture;
                }
            }
        }

        IEnumerator LoadWebAssets()
        {
            yield return BootWeb();
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
            var request = UnityWebRequest.Get(StreamingUrl(fileName));
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success || request.downloadHandler.data == null)
            {
                request.Dispose();
                done(null);
                yield break;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var ok = texture.LoadImage(request.downloadHandler.data);
            request.Dispose();
            if (!ok)
            {
                if (Application.isPlaying) Destroy(texture);
                else DestroyImmediate(texture);
                done(null);
                yield break;
            }
            done(MakeSprite(FitTexture(texture)));
        }

        static string StreamingUrl(string fileName)
        {
            var root = Application.streamingAssetsPath;
            if (!root.EndsWith("/")) root += "/";
            return root + fileName;
        }

        const int WebTextureCap = 4096;

        static Texture2D FitTexture(Texture2D texture)
        {
            if (texture == null) return null;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            if (Application.platform == RuntimePlatform.WebGLPlayer) return texture;
            var wide = Mathf.Max(texture.width, texture.height);
            if (wide <= WebTextureCap) return texture;
            var scale = (float)WebTextureCap / wide;
            var width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
            var scaled = new Texture2D(width, height, texture.format, false);
            scaled.wrapMode = TextureWrapMode.Clamp;
            scaled.filterMode = FilterMode.Bilinear;
            scaled.hideFlags = HideFlags.HideAndDontSave;
            var from = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, from);
            RenderTexture.active = from;
            scaled.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            scaled.Apply(false, true);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(from);
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
            return scaled;
        }

        static Sprite MakeSprite(Texture2D texture)
        {
            if (texture == null) return null;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
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
            onSetup = true;
            busy = false;
            backdropDim = true;
            PresentBackdrop();
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
            ceremonyRankSlots = null;
            ceremonyEquation = false;
            ceremonyOverall = false;
            ceremonyOverallSlot = false;
            ceremonyBlank = false;
            ceremonyWinner = false;
            ceremonyDialog = false;
            ceremonyRankTitle = null;
            ceremonyReason = "";
            ceremonyReasonShown = false;
            ceremonyExpand = 0f;
            scoreOverride = null;
            plusOverride = null;
            CleanupFlyers();
            Clear();
            UseFrame();
            var wide = WideScreen();
            var screenW = wide ? LandWidth : ScreenWidth;
            var screenH = wide ? LandHeight : ScreenHeight;
            var title = TitleSprite(true);
            var catchLine = TitleSprite(false);
            if (title != null) PlaceSprite(frame, "title-mark", title, (screenW - 760f) * 0.5f, 24f, 760f, 152f);
            else Shade(TextAt(frame, "QUOTA", (screenW - 700f) * 0.5f, 36f, 700f, 72f, 64, Cream, nameFont, TextAnchor.MiddleCenter));
            if (catchLine != null) PlaceSprite(frame, "title-catch", catchLine, (screenW - 560f) * 0.5f, 184f, 560f, 56f);
            else Shade(TextAt(frame, "ノルマは、自分で決めろ。", (screenW - 700f) * 0.5f, 184f, 700f, 40f, 28, Cream, nameFont, TextAnchor.MiddleCenter));
            const float columnTop = 384f;
            var showReview = reviewUntil > Time.realtimeSinceStartup && reviewOrder != null && reviewOrder.Count > 0;
            var available = screenH - columnTop - 24f;
            if (lobbyOpen) DrawLobby(screenW, columnTop, available, showReview);
            else DrawStartMenu(screenW, columnTop, available, showReview);
            if (!string.IsNullOrEmpty(setupPage)) DrawSetupPage(wide);
            Shade(TextAt(frame, BuildStamp, 24f, screenH - 56f, screenW - 48f, 40f, 24, Cream, nameFont, TextAnchor.MiddleCenter));
        }

        RectTransform SetupColumn(float screenW, float columnTop, float height)
        {
            var column = Portrait.Rect(frame, "setup", 0f, columnTop, screenW, height);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return column;
        }

        void DrawStartMenu(float screenW, float columnTop, float available, bool showReview)
        {
            const float innerGap = 16f;
            var tableCount = Mathf.Min(3, networkTables.Count);
            var buttonH = SetupButtonHeight(available, 10f + tableCount + (showReview ? 1f : 0f), 2 + tableCount + (showReview ? 1 : 0));
            var font = Mathf.Max(18, Mathf.RoundToInt(32f * buttonH / 72f));
            var column = SetupColumn(screenW, columnTop, available + 24f);
            var guideW = screenW * 0.40f;
            var rowW = screenW * 0.60f;
            var actionW = screenW * 0.35f;
            var section = buttonH * 2f;
            SetupButton(column, "QuickStartガイド", () => OpenPage("quick"), guideW, buttonH, font);
            SetupGap(column, innerGap);
            SetupButton(column, "ルール", () => OpenPage("rules"), guideW, buttonH, font);
            SetupGap(column, innerGap);
            SetupButton(column, "勝つためのヒント", () => OpenPage("hint"), guideW, buttonH, font);
            SetupGap(column, section);
            var labelW = LabelSlot(font, "あなたの名前：");
            var name = SetupNameRow(column, "あなたの名前：", string.IsNullOrWhiteSpace(playerName) ? "あなた" : playerName, rowW, buttonH, labelW, rowW - labelW - 12f - font, font);
            name.onValueChanged.AddListener(value => playerName = value);
            for (var i = 0; i < tableCount; i++)
            {
                SetupGap(column, innerGap);
                var table = networkTables[i];
                var action = table.status == "募集中" && table.seated < table.players ? "参加" : "観戦";
                SetupButton(column, $"{table.leader}　人間 {table.seated}/{table.players}　{action}", () => JoinNetworkTable(table.id), rowW, buttonH, font);
            }
            SetupGap(column, section);
            SetupButton(column, "対局開始", OpenLobby, actionW, buttonH * 2f, font * 2);
            if (showReview)
            {
                SetupGap(column, innerGap);
                SetupButton(column, "ゲーム終了の卓を見る", ShowReview, actionW, buttonH, font);
            }
        }

        void DrawLobby(float screenW, float columnTop, float available, bool showReview)
        {
            const float innerGap = 16f;
            var seats = playerCount;
            var buttonH = SetupButtonHeight(available, 12f + seats + (showReview ? 1f : 0f), seats + 3 + (showReview ? 1 : 0));
            var font = Mathf.Max(18, Mathf.RoundToInt(32f * buttonH / 72f));
            var column = SetupColumn(screenW, columnTop, available + 24f);
            var rowW = screenW * 0.60f;
            var actionW = screenW * 0.35f;
            var section = buttonH * 2f;
            var labelW = LabelSlot(font, "プレイヤーの数：");
            SetupChoiceRow(column, "プレイヤーの数：", $"{playerCount}人", rowW, buttonH, labelW, rowW - labelW - 12f - font, font, () =>
            {
                var next = playerCount == 3 ? 4 : 3;
                if (NetworkJoined) SetNetworkPlayers(next);
                else
                {
                    playerCount = next;
                    ShowSetup();
                }
            });
            SetupGap(column, innerGap);
            var seatNumber = 1;
            foreach (var seat in LobbySeats())
            {
                SetupSeatRow(column, $"{seatNumber}. {seat.Key}", seat.Value, rowW, buttonH, font);
                seatNumber++;
                SetupGap(column, innerGap);
            }
            SetupButton(column, "シャッフル", NetworkJoined ? (UnityAction)ShuffleNetworkCast : ShuffleCast, actionW, buttonH, font);
            if (!NetworkJoined)
            {
                SetupGap(column, section);
                SetupButton(column, sitOut ? "自分は参加しない　オン" : "自分は参加しない　オフ", () =>
                {
                    sitOut = !sitOut;
                    ShowSetup();
                }, rowW, buttonH, font);
                SetupGap(column, innerGap);
            }
            else SetupGap(column, section);
            SetupButton(column, "設定", OpenSettings, actionW, buttonH, font);
            SetupGap(column, section);
            if (NetworkJoined)
            {
                var leader = networkState.you != null && networkState.you.leader;
                SetupButton(column, leader ? "ゲーム開始" : "リーダーの開始を待っています", leader ? (UnityAction)StartNetworkMatch : () => { }, actionW, buttonH * 2f, leader ? font * 2 : font);
            }
            else SetupButton(column, "ゲーム開始", () => StartMatch(sitOut), actionW, buttonH * 2f, font * 2);
            SetupGap(column, innerGap);
            SetupButton(column, "戻る", NetworkJoined ? (UnityAction)LeaveNetworkTable : CloseLobby, actionW, buttonH, font);
            if (showReview)
            {
                SetupGap(column, innerGap);
                SetupButton(column, "ゲーム終了の卓を見る", ShowReview, actionW, buttonH, font);
            }
        }

        List<KeyValuePair<string, bool>> LobbySeats()
        {
            if (NetworkJoined)
            {
                var networkSeats = new List<KeyValuePair<string, bool>>();
                if (networkState.seats != null)
                    foreach (var seat in networkState.seats)
                        networkSeats.Add(new KeyValuePair<string, bool>(seat.name, false));
                if (networkState.cpus != null)
                    foreach (var cpu in networkState.cpus)
                        networkSeats.Add(new KeyValuePair<string, bool>(cpu, true));
                return networkSeats;
            }
            EnsureCast();
            var seats = new List<KeyValuePair<string, bool>>();
            if (!sitOut) seats.Add(new KeyValuePair<string, bool>(HumanName(), false));
            for (var i = 0; seats.Count < playerCount && i < lobbyCast.Count; i++)
                seats.Add(new KeyValuePair<string, bool>(Ranking.DisplayName(lobbyCast[i]), true));
            return seats;
        }

        void EnsureCast()
        {
            var used = new HashSet<int>(lobbyCast);
            while (lobbyCast.Count < 4) lobbyCast.Add(Characters.PickFresh(lobbyRng, used));
        }

        void ShuffleCast()
        {
            lobbyCast.Clear();
            EnsureCast();
            ShowSetup();
        }

        void OpenLobby()
        {
            lobbyOpen = true;
            EnsureCast();
            ShowSetup();
            if (SharedNetwork && !NetworkJoined) StartCoroutine(CreateNetworkTable());
        }

        void CloseLobby()
        {
            lobbyOpen = false;
            ShowSetup();
        }

        bool SharedNetwork => Application.isPlaying;
        bool NetworkJoined => networkState != null && networkState.phase == "recruiting" && !string.IsNullOrEmpty(networkState.table_id);

        IEnumerator PollNetworkLobby()
        {
            yield return new WaitForSeconds(SplashSeconds + SplashFadeSeconds);
            while (SharedNetwork)
            {
                if (!networkRequest) yield return NetworkGet();
                yield return new WaitForSeconds(onSetup ? 0.4f : 0.7f);
            }
        }

        IEnumerator NetworkGet()
        {
            yield return NetworkSend("/api/state", null);
        }

        IEnumerator NetworkPost(string path, string json)
        {
            yield return NetworkSend(path, json);
        }

        IEnumerator NetworkSend(string path, string json)
        {
            while (networkRequest) yield return null;
            networkRequest = true;
            var posted = json != null;
            var body = posted ? System.Text.Encoding.UTF8.GetBytes(json) : null;
            foreach (var root in NetworkRoots())
            {
                var request = posted
                    ? new UnityWebRequest(root + path, UnityWebRequest.kHttpVerbPOST)
                    : UnityWebRequest.Get(root + path);
                if (posted)
                {
                    request.uploadHandler = new UploadHandlerRaw(body);
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("X-Quota-Client", NetworkClient());
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    ApplyNetworkState(request.downloadHandler.text);
                    break;
                }
            }
            networkRequest = false;
        }

        void ApplyNetworkState(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var next = JsonUtility.FromJson<NetworkSnapshot>(json);
            if (next == null || !string.IsNullOrEmpty(next.error)) return;
            var wasRecruiting = networkState != null && networkState.phase == "recruiting";
            networkState = next;
            if (next.phase == "hall")
            {
                ReadHallTables(json, next);
                if (wasRecruiting) lobbyOpen = false;
            }
            else if (next.phase == "recruiting")
            {
                networkTables.Clear();
                lobbyOpen = true;
                playerCount = next.players;
            }
            else if (next.phase == "playing" && !networkNavigating)
            {
                networkNavigating = true;
                if (onSetup) StartMatch(false);
                return;
            }
            var signature = next.phase + ":" + (next.table_id ?? "") + ":" + HallSignature();
            if (signature == networkSignature) return;
            networkSignature = signature;
            if (onSetup && frame != null && frame.Find("splash") == null) ShowSetup();
        }

        void ReadHallTables(string json, NetworkSnapshot next)
        {
            networkTables.Clear();
            if (next.tables != null && next.tables.Length > 0)
            {
                networkTables.AddRange(next.tables);
                return;
            }
            var wrapped = JsonUtility.FromJson<NetworkSnapshot>("{\"tables\":" + SliceJsonArray(json, "tables") + "}");
            if (wrapped != null && wrapped.tables != null) networkTables.AddRange(wrapped.tables);
        }

        string HallSignature()
        {
            var parts = new List<string>();
            for (var i = 0; i < networkTables.Count; i++)
            {
                var table = networkTables[i];
                parts.Add($"{table.id}:{table.leader}:{table.seated}/{table.players}:{table.status}");
            }
            return string.Join("|", parts);
        }

        static string SliceJsonArray(string json, string name)
        {
            var key = "\"" + name + "\"";
            var at = json.IndexOf(key, System.StringComparison.Ordinal);
            if (at < 0) return "[]";
            var start = json.IndexOf('[', at);
            if (start < 0) return "[]";
            var depth = 0;
            for (var i = start; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']')
                {
                    depth--;
                    if (depth == 0) return json.Substring(start, i - start + 1);
                }
            }
            return "[]";
        }

        IEnumerator CreateNetworkTable()
        {
            SaveRules();
            var body = new NetworkCreate
            {
                players = playerCount,
                name = HumanName(),
                simple = simpleMode,
                sequence = sequenceRule,
                title = titleRule,
                special = specialRule,
                ok_timeout = okTimeout,
                ok_timeout_set = true,
                turn_timeout = turnTimeout,
            };
            yield return NetworkPost("/api/table", JsonUtility.ToJson(body));
        }

        void JoinNetworkTable(string table)
        {
            if (!SharedNetwork || string.IsNullOrEmpty(table)) return;
            StartCoroutine(NetworkPost("/api/join", JsonUtility.ToJson(new NetworkJoin { table = table, name = HumanName() })));
        }

        void SetNetworkPlayers(int players)
        {
            StartCoroutine(NetworkPost("/api/players", JsonUtility.ToJson(new NetworkPlayers { players = players })));
        }

        void ShuffleNetworkCast()
        {
            StartCoroutine(NetworkPost("/api/shuffle", "{}"));
        }

        void StartNetworkMatch()
        {
            StartCoroutine(NetworkPost("/api/start", "{}"));
        }

        void LeaveNetworkTable()
        {
            StartCoroutine(NetworkPost("/api/leave", "{}"));
        }

        IEnumerable<string> NetworkRoots()
        {
            var seen = new HashSet<string>();
            System.Uri uri;
            if (System.Uri.TryCreate(Application.absoluteURL, System.UriKind.Absolute, out uri)
                && (uri.Scheme == "http" || uri.Scheme == "https"))
                seen.Add(uri.GetLeftPart(System.UriPartial.Authority));
            seen.Add("http://127.0.0.1:8080");
            return seen;
        }

        string NetworkClient()
        {
            var id = PlayerPrefs.GetString("quota.network.client", "");
            if (!string.IsNullOrEmpty(id)) return id;
            id = System.Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString("quota.network.client", id);
            PlayerPrefs.Save();
            return id;
        }

        static float SetupButtonHeight(float available, float units, int inners)
        {
            return Mathf.Clamp((available - inners * 16f) / units, 40f, 72f);
        }

        void OpenPage(string page)
        {
            setupPage = page;
            ShowSetup();
        }

        void OpenSettings()
        {
            draftSimple = simpleMode;
            draftOk = okTimeout.ToString("0.##");
            draftTurn = turnTimeout.ToString("0.##");
            setupPage = "settings";
            ShowSetup();
        }

        void ApplySettings()
        {
            simpleMode = draftSimple;
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
            lobbyOpen = false;
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
            var firstCpu = cpuOnly ? 0 : 1;
            playerName = HumanName();
            if (Application.isPlaying)
            {
                PlayerPrefs.SetString("quota.name", playerName);
                PlayerPrefs.Save();
            }
            if (!cpuOnly) names.Add(playerName);
            EnsureCast();
            var spare = 0;
            for (var i = firstCpu; i < playerCount; i++)
            {
                var character = spare < lobbyCast.Count ? lobbyCast[spare++] : Characters.PickFresh(lobbyRng, new HashSet<int>(lobbyCast));
                characters.Add(character);
                names.Add(Ranking.DisplayName(character));
            }
            orderOverride = null;
            dialogOrder = null;
            ceremonyPlaces = null;
            ceremonyRankSlots = null;
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
            var game = match.Game;
            var lastRound = game.RoundIndex >= 2;
            reviewMode = true;
            ceremonyScoreReady = true;
            ceremonyRunning = false;
            ceremonyDismissed = true;
            ceremonyExpand = 1f;
            ceremonyHeading = $"第{game.RoundIndex}ラウンド終了";
            ceremonyReason = game.RoundEndReason == "DECK" ? "山札切れでラウンド終了。" : game.RoundEndReason == "STALL" ? "膠着の連続でラウンド終了。" : "";
            ceremonyReasonShown = ceremonyReason.Length > 0;
            ceremonyButton = "抜ける";
            ceremonyOverall = lastRound;
            ceremonyOverallSlot = lastRound;
            ceremonyRankTitle = lastRound ? "最終順位" : null;
            ceremonyBlank = false;
            ceremonyWinner = true;
            ceremonyLineCount = 0;
            titleLines.Clear();
            orderOverride = null;
            dialogOrder = new List<int>(reviewOrder);
            scoreOverride = new Dictionary<int, int>(reviewScores);
            plusOverride = null;
            ceremonyEquation = lastRound;
            AssignPlaces(seat => reviewScores.TryGetValue(seat, out var score) ? score : 0);
            CaptureRankSlots();
            ShowTable();
        }

        void ShowTable()
        {
            onSetup = false;
            backdropDim = false;
            PresentBackdrop();
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
            var marketWash = Plate;
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
                var thinking = wide
                    ? PhotoText(frame, note, LandMarketX, LandMarketY + LandMarketH + 12f, LandMarketW, 32f, 22, TextAnchor.MiddleRight)
                    : PhotoText(frame, note, 520f, 84f, 532f, 32f, 22, TextAnchor.MiddleRight);
                thinking.horizontalOverflow = HorizontalWrapMode.Overflow;
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

        [System.Serializable]
        sealed class NetworkTable
        {
            public string id;
            public string leader;
            public int players;
            public int seated;
            public string status;
        }

        [System.Serializable]
        sealed class NetworkSeat
        {
            public string name;
            public bool leader;
        }

        [System.Serializable]
        sealed class NetworkYou
        {
            public bool leader;
        }

        [System.Serializable]
        sealed class NetworkSnapshot
        {
            public string phase;
            public string table_id;
            public int players;
            public NetworkTable[] tables;
            public NetworkSeat[] seats;
            public string[] cpus;
            public NetworkYou you;
            public string error;
        }

        [System.Serializable]
        sealed class NetworkCreate
        {
            public int players;
            public string name;
            public bool simple;
            public bool sequence;
            public bool title;
            public bool special;
            public float ok_timeout;
            public bool ok_timeout_set;
            public float turn_timeout;
        }

        [System.Serializable]
        sealed class NetworkJoin
        {
            public string table;
            public string name;
        }

        [System.Serializable]
        sealed class NetworkPlayers
        {
            public int players;
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
            ceremonyRankSlots = null;
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
            ceremonyExpand = 0f;
            ceremonyButton = null;
        }

        IEnumerator RunCeremony(int serial)
        {
            yield return null;
            if (serial != cpuRun || match.Game == null) yield break;
            ceremonyDialog = true;
            ceremonyReasonShown = ceremonyReason.Length > 0;
            ceremonyExpand = 0f;
            ceremonyButton = null;
            ShowTable();
            yield return new WaitForSeconds(1f);
            if (serial != cpuRun || match.Game == null) yield break;
            yield return FlyTitles(serial);
            if (serial != cpuRun) yield break;
            yield return ExpandCeremony(serial);
            if (serial != cpuRun) yield break;
            yield return new WaitForSeconds(0.5f);
            if (serial != cpuRun) yield break;
            dialogOrder = SortBy(scoreOverride);
            AssignPlaces(seat => scoreOverride.TryGetValue(seat, out var score) ? score : 0);
            CaptureRankSlots();
            RedrawCeremonyPanel();
            yield return new WaitForSeconds(0.5f);
            if (serial != cpuRun) yield break;
            yield return FlyScores(serial);
            if (serial != cpuRun) yield break;
            dialogOrder = SortBy(scoreOverride);
            AssignPlaces(seat => scoreOverride.TryGetValue(seat, out var score) ? score : 0);
            CaptureRankSlots();
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
                dialogOrder = SortBy(previousScores);
                AssignPlaces(seat => previousScores.TryGetValue(seat, out var previous) ? previous : 0);
                CaptureRankSlots();
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
                CaptureRankSlots();
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
            var patience = !ceremonyLast && NoHumanSeats() ? Time.time + Mathf.Max(0f, okTimeout) : float.MaxValue;
            while (!ceremonyOk)
            {
                if (serial != cpuRun) yield break;
                if (Time.time >= patience) break;
                yield return null;
            }
            if (serial != cpuRun) yield break;
            FinishCeremony();
        }

        bool NoHumanSeats()
        {
            if (match.Game == null) return false;
            foreach (var player in match.Game.Players)
                if (player.IsHuman) return false;
            return true;
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
                    var from = TitleOrigin(job.Line);
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
                        if (line.Arrived >= line.Points) line.Gone = true;
                        ceremonyPending--;
                    }));
                }
                yield return null;
            }
        }

        IEnumerator FlyScores(int serial)
        {
            var piles = new List<List<CeremonyDot>>();
            var max = 0;
            var total = 0;
            foreach (var seat in RoundTurnOrder())
            {
                var pile = new List<CeremonyDot>();
                for (var i = 0; i < ceremonyTray.Count; i++)
                    if (ceremonyTray[i].Seat == seat) pile.Add(ceremonyTray[i]);
                if (pile.Count == 0) continue;
                piles.Add(pile);
                total += pile.Count;
                if (pile.Count > max) max = pile.Count;
            }
            if (total == 0) yield break;
            ceremonyPending = total;
            var wave = 0;
            var started = Time.time;
            while (ceremonyPending > 0)
            {
                if (serial != cpuRun) yield break;
                while (wave < max && Time.time >= started + wave * 0.1f)
                {
                    for (var p = 0; p < piles.Count; p++)
                    {
                        if (wave >= piles[p].Count) continue;
                        var dot = piles[p][wave];
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
                    wave++;
                }
                yield return null;
            }
        }

        IEnumerator AnimateFly(int serial, Vector3 from, Vector3 to, Color color, System.Action arrived)
        {
            var ring = Portrait.Circle(transform, "flyer", 0f, 0f, 18f, Ink);
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

        Rect CeremonyFrame(float expand)
        {
            var wide = WideScreen();
            var screenH = wide ? LandHeight : ScreenHeight;
            var marketX = wide ? LandMarketX : 20f;
            var marketY = wide ? LandMarketY : 170f;
            var marketW = wide ? LandMarketW : 1040f;
            var marketH = wide ? LandMarketH : 210f;
            var compactW = wide ? 520f : 460f;
            var compactH = marketH * 0.9f;
            var compactX = marketX + (marketW - compactW) * 0.5f;
            var compactY = marketY + (marketH - compactH) * 0.5f;
            var right = compactX + compactW;
            var expandedW = wide ? 640f : 700f;
            var limit = Mathf.Max(compactW, right - 12f);
            if (expandedW > limit) expandedW = limit;
            var expandedX = right - expandedW;
            var expandedY = screenH / 3f;
            var expandedH = screenH / 3f;
            var t = Mathf.Clamp01(expand);
            return new Rect(
                Mathf.Lerp(compactX, expandedX, t),
                Mathf.Lerp(compactY, expandedY, t),
                Mathf.Lerp(compactW, expandedW, t),
                Mathf.Lerp(compactH, expandedH, t));
        }

        void ApplyCeremonyFrame()
        {
            var panel = frame != null ? frame.Find("ceremony") as RectTransform : null;
            if (panel == null) return;
            var box = CeremonyFrame(ceremonyExpand);
            panel.anchoredPosition = new Vector2(box.x, -box.y);
            panel.sizeDelta = new Vector2(box.width, box.height);
        }

        IEnumerator ExpandCeremony(int serial)
        {
            var start = Time.time;
            const float span = 0.55f;
            while (Time.time - start < span)
            {
                if (serial != cpuRun) yield break;
                ceremonyExpand = Mathf.Clamp01((Time.time - start) / span);
                ApplyCeremonyFrame();
                yield return null;
            }
            if (serial != cpuRun) yield break;
            ceremonyExpand = 1f;
            RedrawCeremonyPanel();
        }

        void DrawCeremonyPanel()
        {
            bonusMarks.Clear();
            if (reviewMode) ceremonyExpand = 1f;
            var box = CeremonyFrame(ceremonyExpand);
            var panel = Portrait.Box(frame, "ceremony", box.x, box.y, box.width, box.height, 7f, 1f, Paper, Ink, false);
            if (!reviewMode && ceremonyExpand < 1f) DrawCompactCeremony(panel, box.width, box.height);
            else DrawScoreCeremony(panel, box.width, box.height);
            LayoutCeremonyDots();
        }

        void DrawCompactCeremony(RectTransform panel, float panelW, float panelH)
        {
            const float headH = 52f;
            var reasonH = ceremonyReasonShown && !string.IsNullOrEmpty(ceremonyReason) ? 44f : 0f;
            var y = Mathf.Max(12f, (panelH - headH - reasonH) * 0.5f);
            var head = TextAt(panel, ceremonyHeading, 16f, y, panelW - 32f, headH, 40, Ink, nameFont, TextAnchor.MiddleCenter);
            head.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (reasonH <= 0f) return;
            var reason = TextAt(panel, ceremonyReason, 16f, y + headH, panelW - 32f, reasonH, 28, Ink, nameFont, TextAnchor.MiddleCenter);
            reason.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void DrawScoreCeremony(RectTransform panel, float panelW, float panelH)
        {
            const float headH = 56f;
            const float lineH = 48f;
            const float winnerH = 72f;
            var reasonH = ceremonyReasonShown && !string.IsNullOrEmpty(ceremonyReason) ? lineH : 0f;
            var overallH = ceremonyOverallSlot ? lineH : 0f;
            var rows = dialogOrder != null ? dialogOrder.Count : 0;
            var y = 16f;
            var head = TextAt(panel, ceremonyHeading, 16f, y, panelW - 32f, headH, 40, Ink, nameFont, TextAnchor.MiddleLeft);
            head.horizontalOverflow = HorizontalWrapMode.Overflow;
            y += headH;
            if (reasonH > 0f)
            {
                var reason = TextAt(panel, ceremonyReason, 16f, y, panelW - 32f, reasonH, 28, Ink, nameFont, TextAnchor.MiddleLeft);
                reason.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += reasonH;
            }
            if (ceremonyOverallSlot)
            {
                if (!string.IsNullOrEmpty(ceremonyRankTitle))
                    TextAt(panel, ceremonyRankTitle, 16f, y, panelW - 32f, lineH, 28, Ink, nameFont, TextAnchor.MiddleLeft);
                y += lineH;
            }
            var reserved = 96f + (ceremonyWinner ? winnerH : 0f);
            var room = Mathf.Max(48f, panelH - y - reserved);
            var rowH = rows > 0 ? Mathf.Clamp(room / rows, 56f, 88f) : 64f;
            var font = Mathf.Clamp(Mathf.RoundToInt(rowH * 0.42f), 22, 36);
            var rankW = Mathf.Max(72f, font * 2.6f);
            var nameW = Mathf.Max(160f, font * 6f);
            if (dialogOrder != null && match.Game != null)
            {
                for (var r = 0; r < dialogOrder.Count; r++)
                {
                    var seat = dialogOrder[r];
                    if (!ceremonyBlank)
                    {
                        var row = Portrait.Rect(panel, "row" + seat, 16f, y, panelW - 32f, rowH);
                        var rankLabel = TextAt(row, RankSlotText(r), 0f, 0f, rankW, rowH, font, Ink, nameFont, TextAnchor.MiddleLeft);
                        rankLabel.gameObject.name = "rank";
                        var mover = Portrait.Rect(row, "mover", rankW, 0f, panelW - 32f - rankW, rowH);
                        var who = TextAt(mover, match.Game.Players[seat].Name, 0f, 0f, nameW, rowH, font, Ink, nameFont, TextAnchor.MiddleLeft);
                        who.horizontalOverflow = HorizontalWrapMode.Overflow;
                        var figure = TextAt(mover, FigureText(seat), nameW + 8f, 0f, Mathf.Max(80f, panelW - 32f - rankW - nameW - 8f), rowH, font, Ink, nameFont, TextAnchor.MiddleRight);
                        figure.gameObject.name = "figure";
                        figure.supportRichText = true;
                        figure.horizontalOverflow = HorizontalWrapMode.Overflow;
                    }
                    y += rowH;
                }
            }
            if (ceremonyWinner)
            {
                var cheer = TextAt(panel, WinnerText(), 16f, y, panelW - 32f, winnerH, 28, Ink, nameFont, TextAnchor.MiddleLeft);
                cheer.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            if (string.IsNullOrEmpty(ceremonyButton)) return;
            var caption = ceremonyButton;
            var buttonW = caption.Length * 32f + 48f;
            Pill(panel, caption, (panelW - buttonW) * 0.5f, panelH - 80f, buttonW, 64f, 28, () =>
            {
                if (!reviewMode)
                {
                    ceremonyOk = true;
                    return;
                }
                reviewMode = false;
                ShowSetup();
            });
        }

        void CaptureRankSlots()
        {
            ceremonyRankSlots = new List<int>();
            if (dialogOrder == null) return;
            for (var i = 0; i < dialogOrder.Count; i++)
            {
                var place = 0;
                if (ceremonyPlaces != null) ceremonyPlaces.TryGetValue(dialogOrder[i], out place);
                ceremonyRankSlots.Add(place);
            }
        }

        string RankSlotText(int row)
        {
            if (ceremonyRankSlots == null || row < 0 || row >= ceremonyRankSlots.Count || ceremonyRankSlots[row] <= 0) return "";
            return ceremonyRankSlots[row] + "位";
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
            var movers = new List<RectTransform>();
            var from = new List<float>();
            for (var i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                if (!child.name.StartsWith("row")) continue;
                if (!int.TryParse(child.name.Substring(3), out var seat)) continue;
                if (!before.TryGetValue(seat, out var oldY)) continue;
                var mover = child.Find("mover") as RectTransform;
                if (mover == null) continue;
                var delta = oldY - ((RectTransform)child).anchoredPosition.y;
                if (Mathf.Abs(delta) < 1f) continue;
                mover.anchoredPosition = new Vector2(mover.anchoredPosition.x, delta);
                movers.Add(mover);
                from.Add(delta);
            }
            var start = Time.time;
            const float duration = 0.45f;
            while (Time.time - start < duration)
            {
                var along = Mathf.SmoothStep(0f, 1f, (Time.time - start) / duration);
                for (var i = 0; i < movers.Count; i++)
                {
                    if (movers[i] == null) continue;
                    movers[i].anchoredPosition = new Vector2(movers[i].anchoredPosition.x, Mathf.Lerp(from[i], 0f, along));
                }
                yield return null;
            }
            for (var i = 0; i < movers.Count; i++)
            {
                if (movers[i] == null) continue;
                movers[i].anchoredPosition = new Vector2(movers[i].anchoredPosition.x, 0f);
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
            var host = frame.Find("ceremony/row" + seat + "/mover/figure");
            if (host == null) host = frame.Find("ceremony/row" + seat + "/figure");
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

        Vector3 TitleOrigin(int line)
        {
            if (line < 0 || line >= titleLines.Count) return Vector3.zero;
            var which = (titleLines[line].Text ?? "").Contains("単色") ? "title-mono" : "title-purist";
            var seatFrame = frame != null ? frame.Find("seat" + titleLines[line].Seat) : null;
            var mark = seatFrame != null ? seatFrame.Find("title-names/" + which) as RectTransform : null;
            if (mark != null) return CenterOf(mark);
            return TrayPoint(titleLines[line].Seat);
        }

        Vector3 TrayPoint(int seat)
        {
            var tray = TrayOf(seat) as RectTransform;
            if (tray == null) return Vector3.zero;
            var area = tray.Find("coin-area") as RectTransform;
            return area != null ? CenterOf(area) : CenterOf(tray);
        }

        Vector3 ScorePoint(int seat)
        {
            var host = frame != null ? frame.Find("ceremony/row" + seat + "/mover/figure") as RectTransform : null;
            if (host == null && frame != null) host = frame.Find("ceremony/row" + seat + "/figure") as RectTransform;
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
            float x, y, width, height, diameter;
            CoinSpot(tray.name == "bonus-box", out x, out y, out width, out height, out diameter);
            if (tray.name == "bonus-box") height = Mathf.Max(24f, ((RectTransform)tray).sizeDelta.y - y - 34f);
            DrawStoredDot(tray, dot, x, y, width, height, diameter);
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
            Portrait.Circle(holder, "ring", -1f, -1f, diameter + 2f, Ink);
            Portrait.Circle(holder, "disk", 0f, 0f, diameter, CoinColor(dot.Kind));
        }

        static Color CoinColor(CoinKind kind)
        {
            if (kind == CoinKind.Purple) return Hex("#a04bff");
            if (kind == CoinKind.Blue) return Hex("#3c7dff");
            return Hex("#3cce3c");
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
                ? Portrait.Box(frame, "round-break", (LandWidth - width) * 0.5f, (LandHeight - height) * 0.5f, width, height, 7f, 1f, Paper, Ink, false)
                : Portrait.Box(frame, "round-break", (ScreenWidth - width) * 0.5f, 640f, width, height, 7f, 1f, Paper, Ink, false);
            TextAt(panel, $"第{game.RoundIndex}ラウンド終了（{reason}）", 32f, 24f, width - 64f, 48f, 32, Ink, nameFont, TextAnchor.MiddleLeft);
            var y = 84f;
            foreach (var group in game.Ranking())
            {
                foreach (var seat in group)
                {
                    var player = game.Players[seat];
                    TextAt(panel, $"{player.Name}  {game.FinalScore(player)}点", 32f, y, width - 64f, 36f, 24, Ink, nameFont, TextAnchor.MiddleLeft);
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
            else Shade(TextAt(frame, "QUOTA", 28f, 16f, 640f, 68f, 56, Cream, nameFont, TextAnchor.MiddleLeft));
            PhotoText(frame, RoundLabel(game), 20f, 80f, 500f, 36f, 24, TextAnchor.MiddleLeft);
            if (game.DoubleStage == 1) Shade(TextAt(frame, "ダブル：1回目の行動です。", 300f, 28f, 460f, 36f, 22, Cream, nameFont, TextAnchor.MiddleRight));
            else if (game.DoubleStage == 2) Shade(TextAt(frame, "ダブル：2回目の行動です。", 300f, 28f, 460f, 36f, 22, Cream, nameFont, TextAnchor.MiddleRight));
            else if (game.Plan == "reshuffle") Shade(TextAt(frame, "配り直しました。行動を選んでください。", 280f, 28f, 480f, 36f, 22, Cream, nameFont, TextAnchor.MiddleRight));
            var me = game.Players[game.Current];
            var hint = $"手番 {game.TurnNumber}  山札 {game.Deck.Count}  膠着 {(game.StallFlag ? 1 : 0)}/{game.Players.Count}";
            if (me.Quota != null && me.Quota.Rank != null)
            {
                var need = me.Quota.Rank.Value - 1 - me.Collection.Count;
                if (need > 0) hint = $"あと{need}枚   " + hint;
            }
            PhotoText(frame, hint, 20f, 116f, 1040f, 32f, 20, TextAnchor.MiddleLeft);
        }

        void DrawTitleWide(Game game)
        {
            var mark = TitleSprite(true);
            if (mark != null) PlaceSprite(frame, "title-mark", mark, 28f, 24f, 260f, 52f);
            else Shade(TextAt(frame, "QUOTA", 28f, 28f, 280f, 64f, 48, Cream, nameFont, TextAnchor.MiddleLeft));
            PhotoText(frame, RoundLabel(game), 292f, 44f, 296f, 40f, 26, TextAnchor.MiddleLeft);
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
            PhotoText(frame, note + hint, 596f, 44f, 1000f, 40f, 20, TextAnchor.MiddleLeft);
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

        Color NameplateColor(Game game, int index)
        {
            var playing = index == game.Current && !game.Finished && !game.AwaitingNextRound && !reviewMode;
            return playing ? Hex("#fff3d6") : Color.white;
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
            Portrait.Box(seat, "plate", 25f, 25f, 1030f, 340f, 7f, 1f, Plate, Ink, false);
            Portrait.Box(seat, "nameplate", 0f, 10f, 300f, 50f, 4.5f, 1f, NameplateColor(game, index), Ink, true);
            var name = TextAt(seat, player.Name, 12f, 10f, 276f, 50f, NameFontSize(player.Name, 276f, 36), Ink, nameFont, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            var quotaTop = 75f;
            TextAt(seat, "ノルマ", 0f, quotaTop, 122f, 40f, 24, Ink, nameFont, TextAnchor.UpperRight);
            var quotaCards = Portrait.Rect(seat, "quota-cards", 130f, quotaTop, 768f, 145f);
            quotaCards.gameObject.AddComponent<RectMask2D>();
            var strip = new List<Card>();
            if (player.Quota != null) strip.Add(player.Quota);
            strip.AddRange(player.Collection);
            LayCards(quotaCards, theme, strip, 6.5f, 55f);
            TextAt(seat, "実績", 0f, 220f, 122f, 40f, 24, Ink, nameFont, TextAnchor.UpperRight);
            var achieved = Portrait.Rect(seat, "achieved-cards", 130f, 220f, 405f, 145f);
            achieved.gameObject.AddComponent<RectMask2D>();
            LayCards(achieved, theme, player.Achieved, 6.5f, 3f);
            TextAt(seat, "ボーナス", 535f, 220f, 212f, 40f, 24, Ink, nameFont, TextAnchor.UpperRight);
            const float trayX = 775f;
            const float trayY = 240f;
            const float trayW = 220f;
            const float trayH = 105f;
            var tray = Portrait.Box(seat, "chip-tray", trayX, trayY, trayW, trayH, 7f, 1f, Ecru, Ink, false);
            if (game.Config.TitleRule) DrawTrayTitles(seat, player, trayX, trayY - 42f, trayW, 40f, 16);
            float coinX, coinY, coinW, coinH, coinD;
            CoinSpot(false, out coinX, out coinY, out coinW, out coinH, out coinD);
            Portrait.Rect(tray, "coin-area", coinX, coinY, coinW, coinH);
            PlaceBonus(game, player, quotaCards, tray, coinX, coinY, coinW, coinH, coinD, 1f);
            var side = SeatPoints(game, index, player);
            if (!(ceremonyRunning || reviewMode) && game.Config.SpecialActionsRule)
                side += $"\nダブル {(player.DoubleActionLeft > 0 ? "残1" : "済")}\n配り直し {(player.ReshuffleTakeLeft > 0 ? "残1" : "済")}";
            var score = TextAt(seat, side, 898f, quotaTop, 170f, 140f, 20, Ink, nameFont, TextAnchor.UpperLeft);
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
            Portrait.Box(seat, "plate", 16f, 22f, 1188f, 206f, 7f, 1f, Plate, Ink, false);
            Portrait.Box(seat, "nameplate", 16f, 6f, 270f, 46f, 4.5f, 1f, NameplateColor(game, index), Ink, false);
            var name = TextAt(seat, player.Name, 28f, 6f, 246f, 46f, NameFontSize(player.Name, 246f, 30), Ink, nameFont, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (game.Config.SpecialActionsRule)
            {
                var uses = $"ダブル {(player.DoubleActionLeft > 0 ? "残1" : "済")}　配り直し {(player.ReshuffleTakeLeft > 0 ? "残1" : "済")}";
                PhotoText(seat, uses, 848f, 8f, 340f, 32f, 16, TextAnchor.MiddleRight);
            }

            var titled = game.Config.TitleRule;
            const float boxX = 28f;
            const float boxW = 176f;
            var boxY = titled ? 88f : 66f;
            var boxH = titled ? 126f : 148f;
            Portrait.Box(seat, "bonus-box", boxX, boxY, boxW, boxH, 7f, 1f, Ecru, Ink, false);
            if (titled) DrawTrayTitles(seat, player, boxX, boxY - 34f, boxW, 32f, 12);
            TextAt(seat, "ボーナス", 36f, boxY + 4f, 120f, 24f, 14, Ink, nameFont, TextAnchor.MiddleLeft);
            var wideScore = TextAt(seat, SeatPoints(game, index, player), 36f, boxY + boxH - 30f, 152f, 24f, 16, Ink, nameFont, TextAnchor.MiddleLeft);
            wideScore.gameObject.name = "score";
            wideScore.supportRichText = true;

            const float recordY = 66f;
            const float recordH = 148f;
            Portrait.Box(seat, "record-box", 216f, recordY, 220f, recordH, 7f, 1f, Ecru, Ink, false);
            TextAt(seat, "実績", 224f, recordY + 4f, 80f, 24f, 14, Ink, nameFont, TextAnchor.MiddleLeft);
            var achieved = Portrait.Rect(seat, "achieved-cards", 224f, recordY + 30f, 200f, recordH - 38f);
            achieved.gameObject.AddComponent<RectMask2D>();
            LayCards(achieved, theme, player.Achieved, 2f, 3f, 0.62f);

            TextAt(seat, "ノルマ", 452f, recordY, 80f, 24f, 14, Ink, nameFont, TextAnchor.MiddleLeft);
            var quotaCards = Portrait.Rect(seat, "quota-cards", 452f, recordY + 22f, 730f, CardHeight + 4f);
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
            float coinX, coinY, coinW, coinH, coinD;
            CoinSpot(true, out coinX, out coinY, out coinW, out coinH, out coinD);
            coinH = Mathf.Max(24f, boxH - coinY - 34f);
            Portrait.Rect(bonusBox, "coin-area", coinX, coinY, coinW, coinH);
            PlaceBonus(game, player, quotaCards, bonusBox, coinX, coinY, coinW, coinH, coinD, 1f);
        }

        static void CoinSpot(bool wide, out float x, out float y, out float width, out float height, out float diameter)
        {
            x = 8f;
            if (wide)
            {
                diameter = 12f;
                width = 160f;
                y = 28f;
                height = 82f;
                return;
            }
            diameter = 14f;
            width = 204f;
            y = 8f;
            height = 89f;
        }

        void DrawTrayTitles(Transform seat, Player player, float x, float y, float width, float band, int font)
        {
            var host = Portrait.Rect(seat, "title-names", x, y, width, band);
            var lineH = band * 0.5f;
            DrawTitleName(host, "title-mono", "単色達成", 0f, 0f, width, lineH, font, TitleMonoOut(player));
            DrawTitleName(host, "title-purist", "生粋の買い付け", 0f, lineH, width, lineH, font, TitlePuristOut(player));
        }

        static bool TitleMonoOut(Player player)
        {
            var kinds = new HashSet<string>();
            foreach (var bundle in player.Bundles) kinds.Add(bundle.Kind);
            return kinds.Count > 1;
        }

        static bool TitlePuristOut(Player player)
        {
            foreach (var bundle in player.Bundles)
                if (bundle.HasWild) return true;
            return false;
        }

        void DrawTitleName(Transform parent, string name, string text, float x, float y, float width, float height, int font, bool struck)
        {
            var color = struck ? Hex("#8a8a8a") : Ink;
            var label = TextAt(parent, text, x, y, width, height, font, color, nameFont, TextAnchor.MiddleCenter);
            label.gameObject.name = name;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (!struck) return;
            Portrait.Solid(parent, name + "-strike", x, y + height * 0.5f - 1f, width, 2f, color);
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
            if (reviewMode) return;
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
            var ring = Portrait.Circle(parent, "spot-" + coin.Key, x - 1f, y - 1f, diameter + 2f, Ink);
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
            Portrait.Box(host, "face", 0f, 0f, width, height, 4.5f * scale, Mathf.Max(1f, scale), Color.white, Ink, false);
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
            Baseline(host, face.Name, 47.5f * scale, 118f * scale, GoodsNameSize * scale, Ink, roundFont, width - 8f);
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
            var width = caption.Length * 28f + 36f;
            var x = WideScreen() ? LandWidth - 24f - width : ScreenWidth - 20f - width;
            var y = WideScreen() ? 40f : 18f;
            var host = Portrait.Rect(frame, caption, x, y, width, 40f);
            var hit = host.gameObject.AddComponent<Image>();
            hit.sprite = Portrait.SlicedRound;
            hit.type = Image.Type.Sliced;
            hit.color = Ecru;
            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() => Ask("leave"));
            var label = TextAt(host, caption, 12f, 0f, width - 20f, 40f, 28, Ink, nameFont, TextAnchor.MiddleRight);
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
                panel = Portrait.Box(frame, "confirm", ((WideScreen() ? LandWidth : ScreenWidth) - panelWidth) * 0.5f, ((WideScreen() ? LandHeight : ScreenHeight) - panelHeight) * 0.5f, panelWidth, panelHeight, 7f, 1f, Paper, Ink, false);
            else
            {
                var seat = seatFrames[seatIndex];
                panel = Portrait.Box(seat, "confirm", 25f + (1030f - panelWidth) * 0.5f, 25f + (340f - panelHeight) * 0.5f, panelWidth, panelHeight, 7f, 1f, Paper, Ink, false);
            }
            TextAt(panel, message, 24f, 28f, 652f, 80f, 32, Ink, nameFont, TextAnchor.MiddleCenter);
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
            shade.color = new Color(0.10f, 0.07f, 0.05f, 0.5f);
            shade.raycastTarget = true;
            var offerX = WideScreen() ? (LandWidth - 800f) * 0.5f : 140f;
            var offerY = WideScreen() ? (LandHeight - 340f) * 0.5f : 760f;
            var panel = Portrait.Box(veil, "offer-card", offerX, offerY, 800f, 340f, 7f, 1f, Paper, Ink, false);
            TextAt(panel, "シンプルモードをオフにして標準ルールに戻しますか？", 32f, 36f, 736f, 140f, 28, Ink, nameFont, TextAnchor.MiddleCenter);
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
            var panel = Portrait.Box(frame, "result", resultX, resultY, 900f, resultH, 7f, 1f, Paper, Ink, false);
            TextAt(panel, game.EndReason == "DECK" ? "ゲーム終了" : "膠着の連続", 32f, 24f, 836f, 56f, 36, Ink, nameFont, TextAnchor.MiddleLeft);
            var place = 1;
            var y = 96f;
            foreach (var group in game.Ranking())
            {
                foreach (var seat in group)
                {
                    var player = game.Players[seat];
                    TextAt(panel, $"{place}位 {player.Name} {game.FinalScore(player)}点（達成{player.AchieveCount} / 最高{player.MaxSingleScore}）", 32f, y, 836f, 36f, 22, Ink, nameFont, TextAnchor.MiddleLeft);
                    y += 36f;
                    foreach (var line in Perks(game, player))
                    {
                        TextAt(panel, line, 48f, y, 820f, 32f, 18, Ink, nameFont, TextAnchor.MiddleLeft);
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
                DestroyImmediate(frame.GetChild(i).gameObject);
            seatFrames.Clear();
        }

        void CleanupFlyers()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name != "flyer" && !child.name.StartsWith("cdot-")) continue;
                DestroyImmediate(child.gameObject);
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

        void Pill(Transform parent, string caption, float x, float y, float width, float height, int size, UnityAction action, bool accent = false)
        {
            var host = Portrait.Box(parent, caption, x, y, width, height, 7f, 1f, accent ? Accent : Ecru, Ink, false);
            var hit = host.GetComponent<Image>();
            hit.raycastTarget = true;
            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(action);
            TextAt(host, caption, 8f, 0f, width - 16f, height, size, accent ? Cream : Ink, nameFont, TextAnchor.MiddleCenter);
        }

        RectTransform SoftPanel(Transform parent, string name, float x, float y, float width, float height, Color fill)
        {
            var host = Portrait.Rect(parent, name, x, y, width, height);
            var image = host.gameObject.AddComponent<Image>();
            image.sprite = Portrait.SlicedRound;
            image.type = Image.Type.Sliced;
            image.color = fill;
            image.raycastTarget = false;
            return host;
        }

        Text PhotoText(Transform parent, string text, float x, float y, float width, float height, int size, TextAnchor anchor)
        {
            SoftPanel(parent, "chip", x, y, width, height, Plate);
            var label = TextAt(parent, text, x + 12f, y, Mathf.Max(8f, width - 20f), height, size, Ink, nameFont, anchor);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }

        static void Shade(Text label)
        {
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.08f, 0.05f, 0.04f, 0.9f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        void DrawSetupPage(bool wide)
        {
            var screenW = wide ? LandWidth : ScreenWidth;
            var screenH = wide ? LandHeight : ScreenHeight;
            var veil = Portrait.Solid(frame, "setup-veil", 0f, 0f, screenW, screenH, new Color(0.10f, 0.07f, 0.05f, 0.58f));
            veil.GetComponent<Image>().raycastTarget = true;
            if (setupPage == "settings") DrawSettings(screenW, screenH);
            else DrawGuide(screenW, screenH);
        }

        void DrawSettings(float screenW, float screenH)
        {
            const float panelW = 860f;
            const float panelH = 560f;
            var panel = Portrait.Box(frame, "setup-dialog", (screenW - panelW) * 0.5f, (screenH - panelH) * 0.5f, panelW, panelH, 7f, 1f, Paper, Ink, false);
            TextAt(panel, "設定", 32f, 24f, panelW - 64f, 48f, 32, Ink, nameFont, TextAnchor.MiddleLeft);
            Pill(panel, draftSimple ? "シンプルモード　オン" : "シンプルモード　オフ", 32f, 96f, panelW - 64f, 72f, 28, () =>
            {
                draftSimple = !draftSimple;
                ShowSetup();
            });
            var ok = DialogField(panel, "OKタイムアウト（秒）", draftOk, 32f, 196f, 380f);
            ok.onValueChanged.AddListener(value => draftOk = value);
            var turn = DialogField(panel, "手番タイムアウト（秒）", draftTurn, 440f, 196f, 380f);
            turn.onValueChanged.AddListener(value => draftTurn = value);
            var decideW = 200f;
            var cancelW = 240f;
            var buttonGap = 20f;
            var buttonsX = (panelW - decideW - buttonGap - cancelW) * 0.5f;
            Pill(panel, "決定", buttonsX, panelH - 112f, decideW, 72f, 28, ApplySettings, true);
            Pill(panel, "キャンセル", buttonsX + decideW + buttonGap, panelH - 112f, cancelW, 72f, 28, () =>
            {
                setupPage = null;
                ShowSetup();
            });
        }

        void DrawGuide(float screenW, float screenH)
        {
            var panelW = Mathf.Min(980f, screenW - 48f);
            var panelH = Mathf.Min(screenH - 80f, screenW > screenH ? 820f : 1400f);
            var panel = Portrait.Box(frame, "setup-dialog", (screenW - panelW) * 0.5f, (screenH - panelH) * 0.5f, panelW, panelH, 7f, 1f, Paper, Ink, false);
            TextAt(panel, GuideCopy.Title(setupPage), 32f, 16f, panelW - 64f, 56f, 40, Ink, nameFont, TextAnchor.MiddleLeft);
            var viewW = panelW - 64f;
            const float viewTop = 84f;
            var viewH = panelH - viewTop - 100f;
            var viewport = Portrait.Rect(panel, "guide-view", 32f, viewTop, viewW, viewH);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.01f);
            hit.raycastTarget = true;
            var content = Portrait.Rect(viewport, "guide-body", 0f, 0f, viewW, viewH);
            var text = TextAt(content, GuideCopy.Body(setupPage), 0f, 0f, viewW, viewH, 36, Ink, nameFont, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = true;
            Canvas.ForceUpdateCanvases();
            var bodyH = Mathf.Max(viewH, text.preferredHeight + 24f);
            content.sizeDelta = new Vector2(viewW, bodyH);
            text.rectTransform.sizeDelta = new Vector2(viewW, bodyH);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;
            Pill(panel, "OK", 32f, panelH - 84f, 160f, 64f, 28, () =>
            {
                setupPage = null;
                ShowSetup();
            });
        }

        InputField DialogField(Transform parent, string caption, string value, float x, float y, float width)
        {
            TextAt(parent, caption, x, y, width, 28f, 22, Ink, nameFont, TextAnchor.MiddleLeft);
            var host = Portrait.Box(parent, caption, x, y + 32f, width, 56f, 4f, 1f, Field, Ink, false);
            host.GetComponent<Image>().raycastTarget = true;
            var textGo = new GameObject("text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(host, false);
            Stretch(textGo.GetComponent<RectTransform>(), 12f, 8f);
            var text = textGo.GetComponent<Text>();
            text.font = nameFont;
            text.fontSize = 28;
            text.color = Ink;
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
            return MakeSprite(FitTexture(texture));
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

        void SetupButton(RectTransform parent, string caption, UnityAction action, float width, float height, int fontSize)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            var accent = caption == "対局開始" || caption == "ゲーム開始";
            image.sprite = Portrait.SlicedRound;
            image.type = Image.Type.Sliced;
            image.color = accent ? Accent : Ecru;
            SizeElement(go.GetComponent<LayoutElement>(), width, height);
            var label = new GameObject("caption", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            Stretch(label.GetComponent<RectTransform>(), 16f, 8f);
            var text = label.GetComponent<Text>();
            text.font = nameFont;
            text.fontSize = fontSize;
            text.color = accent ? Cream : Ink;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = caption;
            text.raycastTarget = false;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
        }

        void SetupChoiceRow(RectTransform parent, string caption, string value, float rowW, float height, float labelW, float fieldW, int fontSize, UnityAction action)
        {
            var row = FormRow(parent, caption, rowW, height, labelW, fontSize);
            SetupButton(row, value, action, fieldW, height, fontSize);
        }

        void SetupSeatRow(RectTransform parent, string seatName, bool cpu, float rowW, float height, int fontSize)
        {
            var tagFont = Mathf.Max(14, fontSize - 10);
            var tagW = cpu ? LabelSlot(tagFont, "CPU") : 0f;
            var row = FormRow(parent, seatName, rowW, height, rowW - fontSize - tagW - 24f, fontSize);
            if (!cpu) return;
            var go = new GameObject("tag", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(row, false);
            SizeElement(go.GetComponent<LayoutElement>(), tagW, height);
            var text = go.GetComponent<Text>();
            text.font = nameFont;
            text.fontSize = tagFont;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleRight;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = "CPU";
            text.raycastTarget = false;
        }

        InputField SetupNameRow(RectTransform parent, string caption, string value, float rowW, float height, float labelW, float fieldW, int fontSize)
        {
            var row = FormRow(parent, caption, rowW, height, labelW, fontSize);
            var go = new GameObject("field", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(row, false);
            var image = go.GetComponent<Image>();
            image.sprite = Portrait.SlicedRound;
            image.type = Image.Type.Sliced;
            image.color = Field;
            image.raycastTarget = true;
            SizeElement(go.GetComponent<LayoutElement>(), fieldW, height);
            var textGo = new GameObject("text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>(), 12f, 8f);
            var text = textGo.GetComponent<Text>();
            text.font = nameFont;
            text.fontSize = fontSize;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.text = value;
            return field;
        }

        RectTransform FormRow(RectTransform parent, string caption, float rowW, float height, float labelW, int fontSize)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup), typeof(Image));
            go.transform.SetParent(parent, false);
            var plate = go.GetComponent<Image>();
            plate.sprite = Portrait.SlicedRound;
            plate.type = Image.Type.Sliced;
            plate.color = Plate;
            plate.raycastTarget = false;
            SizeElement(go.GetComponent<LayoutElement>(), rowW, height);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(fontSize, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            var label = new GameObject("label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            label.transform.SetParent(go.transform, false);
            SizeElement(label.GetComponent<LayoutElement>(), labelW, height);
            var text = label.GetComponent<Text>();
            text.font = nameFont;
            text.fontSize = fontSize;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = caption;
            text.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        float LabelSlot(int fontSize, params string[] captions)
        {
            var width = 0f;
            foreach (var caption in captions) width = Mathf.Max(width, MeasuredTextWidth(caption, fontSize));
            return width + 20f;
        }

        float MeasuredTextWidth(string value, int fontSize)
        {
            if (nameFont == null || string.IsNullOrEmpty(value)) return fontSize * 8f;
            var settings = new TextGenerationSettings
            {
                font = nameFont,
                color = Color.white,
                fontSize = fontSize,
                lineSpacing = 1f,
                richText = false,
                scaleFactor = 1f,
                fontStyle = FontStyle.Normal,
                textAnchor = TextAnchor.MiddleLeft,
                alignByGeometry = false,
                resizeTextForBestFit = false,
                updateBounds = false,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(4000f, fontSize * 2f),
                pivot = new Vector2(0f, 0.5f),
            };
            var width = new TextGenerator().GetPreferredWidth(value, settings);
            return width > 1f ? width : fontSize * value.Length;
        }

        static void SetupGap(RectTransform parent, float height)
        {
            var go = new GameObject("gap", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            SizeElement(go.GetComponent<LayoutElement>(), 8f, height);
        }

        static void SizeElement(LayoutElement element, float width, float height)
        {
            element.preferredWidth = width;
            element.minWidth = width;
            element.preferredHeight = height;
            element.minHeight = height;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;
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

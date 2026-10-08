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
    public sealed partial class TableView : MonoBehaviour
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
        const float SplashFadeSeconds = 0.5f;
        const string SplashCopy = "あなたは港で働く仲買人だ。\n大口顧客のために、舶来の交易品を買い集めよう。\n買い付けノルマは、自分で決める。";
        const string BuildStamp = "UNITY-WEBGL splash-harbor";
        const float MarketScale = 144f / 95f;
        const float CardWidth = 95f;
        const float CardHeight = 132f;
        const float GoodsNameSize = 21f;

        static readonly Color Ink = Hex("#2C221E");
        static readonly Color Cream = Hex("#F6F1E8");
        static readonly Color Ecru = new Color(0.953f, 0.929f, 0.894f, 0.86f);
        static readonly Color Accent = Hex("#8C3D2A");
        static readonly Color Plate = new Color(0.953f, 0.929f, 0.894f, 0.80f);
        static readonly Color Paper = new Color(0.965f, 0.945f, 0.910f, 0.92f);
        static readonly Color Field = Hex("#FFF8F0");
        static readonly Color DimTint = new Color(0.52f, 0.49f, 0.45f, 1f);

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
        float backdropShade;
        float tableScroll = 1f;
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
        bool simpleMode = false;
        bool sequenceRule = true;
        bool titleRule = true;
        bool specialRule = true;
        Coroutine splashRun;
        string seedText = "";
        string playerName = "あなた";
        string setupPage;
        readonly BonusChipLab.State chipLabState = new BonusChipLab.State();
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
        int networkApplied;
        bool NetworkPlaying => networkState != null && networkState.phase == "playing";
        bool MyHumanTurn => match.IsHumanTurn && (!NetworkPlaying || (networkState.you != null && networkState.you.seat == match.Game.Current));
        bool webNetworkDone;
        string webNetworkResponse;
        double networkTurnDeadline;
        readonly HashSet<int> departedSeats = new HashSet<int>();
        readonly HashSet<int> timedOutSeats = new HashSet<int>();
        readonly HashSet<int> timeoutNoticeSeats = new HashSet<int>();
        bool confirmActions = true;
        float okTimeout = 5f;
        float turnTimeout = 30f;
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
        int acknowledgedRound;
        bool networkLeaving;
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
        int titleVerdictRound;
        bool ceremonyTitles;
        float ceremonyMaxHeight;
        bool ceremonyOverall;
        bool ceremonyOverallSlot;
        bool ceremonyBlank;
        bool ceremonyNamesOnly;
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

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void QuotaMarkReady(string message);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void QuotaFetch(string method, string url, string body, string client, string target);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void QuotaEditName(string value, string target);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void QuotaEditTimeout(string value, string kind, string target);
#endif

        static void MarkWeb(string message)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaMarkReady(message);
#endif
        }

        void Start()
        {
            MarkWeb("Start 開始");
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
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                if (Application.isPlaying) StartCoroutine(BootWeb());
            }
            else ShowSplash();
            MarkWeb("Start 完了");
        }

        void OnDestroy()
        {
            if (logoInk != null)
            {
                if (Application.isPlaying) Destroy(logoInk);
                else DestroyImmediate(logoInk);
            }
            if (gradeProfile == null) return;
            if (Application.isPlaying) Destroy(gradeProfile);
            else DestroyImmediate(gradeProfile);
        }

        Camera EnsureGameCamera()
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
            return camera;
        }

        void EnsureBackdropGrade()
        {
            var camera = EnsureGameCamera();
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                if (backdropStage != null) backdropStage.enabled = false;
                return;
            }
            var extra = camera.GetUniversalAdditionalCameraData();
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

        bool splashDismissed;
        readonly Queue<KeyValuePair<int, string>> actionNotices = new Queue<KeyValuePair<int, string>>();
        Coroutine noticeRun;

        void Update()
        {
            var shade = Mathf.MoveTowards(backdropShade, backdropDim ? 1f : 0f, Time.unscaledDeltaTime / 0.8f);
            if (!Mathf.Approximately(shade, backdropShade))
            {
                backdropShade = shade;
                PresentBackdrop();
            }
            if (frame != null && frame.Find("splash") != null && (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))) DismissSplash();
            if (TouchScreenKeyboard.visible) return;
            foreach (var input in GetComponentsInChildren<InputField>())
                if (input.isFocused) return;
            Fit();
            if (!onSetup && match.Game != null && transform.Find("leave-button") != null) LeaveButton();
            UpdateNameplates();
            UpdateTurnCountdown();
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
            ViewSize(out var width, out var height);
            return width > height && height > 0;
        }

        float viewW;
        float viewH;

        void ViewSize(out float width, out float height)
        {
            width = Screen.width;
            height = Screen.height;
            if (width < 2f || height < 2f)
            {
                width = Display.main.renderingWidth;
                height = Display.main.renderingHeight;
            }
            if (width < 2f || height < 2f)
            {
                width = viewW > 0f ? viewW : 390f;
                height = viewH > 0f ? viewH : 844f;
            }
            viewW = width;
            viewH = height;
        }

        void UseFrame()
        {
            var wide = WideScreen();
            frame.sizeDelta = new Vector2(wide ? LandWidth : ScreenWidth, wide ? LandHeight : ScreenHeight);
            laidOutWide = wide;
        }

        void Fit()
        {
            if (frame == null) return;
            ViewSize(out var viewWidth, out var viewHeight);
            var portrait = viewHeight >= viewWidth;
            var sprite = portrait ? verticalBackground : horizontalBackground;
            if (sprite == null) sprite = verticalBackground != null ? verticalBackground : horizontalBackground;
            if (backdrop != null && sprite != null)
            {
                backdrop.sprite = sprite;
                var cover = Mathf.Max(viewWidth / sprite.rect.width, viewHeight / sprite.rect.height);
                backdrop.rectTransform.sizeDelta = new Vector2(sprite.rect.width * cover, sprite.rect.height * cover);
                PresentBackdrop();
            }
            var wide = WideScreen();
            var designW = wide ? LandWidth : ScreenWidth;
            var designH = wide ? LandHeight : ScreenHeight;
            frame.sizeDelta = new Vector2(designW, designH);
            var scale = Mathf.Min(viewWidth / designW, viewHeight / designH);
            if (scale < 0.01f) scale = 0.01f;
            frame.localScale = new Vector3(scale, scale, 1f);
            // The playing board starts at the top edge so the deck extends off screen.
            frame.anchoredPosition = !onSetup && !wide
                ? new Vector2(0f, (viewHeight - designH * scale) * 0.5f) : Vector2.zero;
        }

        void PresentBackdrop()
        {
            if (backdrop == null) return;
            if (!Application.isPlaying) backdropShade = backdropDim ? 1f : 0f;
            var tint = Color.Lerp(Color.white, DimTint, Mathf.SmoothStep(0f, 1f, backdropShade));
            if (backdrop.sprite != null)
            {
                Shader.SetGlobalTexture("_QuotaBackdrop", backdrop.sprite.texture);
                var texture = backdrop.sprite.texture;
                Shader.SetGlobalVector("_QuotaBackdrop_TexelSize", new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height));
                var corners = new Vector3[4];
                backdrop.rectTransform.GetWorldCorners(corners);
                Shader.SetGlobalVector("_QuotaBackdropRect", new Vector4(corners[0].x / Screen.width, corners[0].y / Screen.height, (corners[2].x - corners[0].x) / Screen.width, (corners[2].y - corners[0].y) / Screen.height));
                Shader.SetGlobalColor("_QuotaBackdropTint", tint);
            }
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
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                var bundled = LoadBundledSprite("goods/" + file);
                if (bundled != null) goodsSprites[file] = bundled;
                return bundled;
            }
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
            splashDismissed = false;
            splash.gameObject.AddComponent<CanvasGroup>().alpha = Application.isPlaying ? 0f : 1f;
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
            MarkWeb("同梱アセット読込開始");
            verticalBackground = LoadBundledSprite("vertical_base");
            horizontalBackground = LoadBundledSprite("horizontal_base");
            titleMark = LoadBundledSprite("title1");
            catchMark = LoadBundledSprite("title2");
            var goods = LoadBundledText("quota_goods_v1.0");
            if (!string.IsNullOrEmpty(goods)) ItemCatalog.LoadJson(goods);
            Ranking.LoadJson(LoadBundledText(Ranking.FileName));
            webAssetsReady = true;
            Fit();
            ShowSplash();
            MarkWeb("スプラッシュ表示");

            if (ItemCatalog.IsLoaded)
            {
                var loaded = 0;
                foreach (var file in ItemCatalog.PictureFiles())
                {
                    GoodsSprite(file);
                    if (++loaded % 8 == 0) yield return null;
                }
            }
            MarkWeb("同梱アセット読込完了");
        }

        static string BundledPath(string name)
        {
            return "QuotaWebGenerated/" + name;
        }

        static string BundledTextName(string name)
        {
            var extension = Path.GetExtension(name);
            if (extension == ".json" || extension == ".txt") name = name.Substring(0, name.Length - extension.Length);
            return name;
        }

        static string LoadBundledText(string name)
        {
            var asset = Resources.Load<TextAsset>(BundledPath(BundledTextName(name)));
            return asset != null ? asset.text : null;
        }

        static Sprite LoadBundledSprite(string name)
        {
            var texture = Resources.Load<Texture2D>(BundledPath(name));
            return MakeSprite(FitTexture(texture));
        }

        const int WebTextureCap = 4096;
        static Texture2D FitTexture(Texture2D texture)
        {
            if (texture == null) return null;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            // These source images are at most 2048 px, which iPhone Safari
            // supports directly. CPU resampling here blocked WebGL startup.
            if (Application.platform == RuntimePlatform.WebGLPlayer) return texture;
            var cap = WebTextureCap;
            var wide = Mathf.Max(texture.width, texture.height);
            if (wide <= cap) return texture;
            var scale = (float)cap / wide;
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
            var group = frame.Find("splash").GetComponent<CanvasGroup>();
            var elapsed = 0f;
            while (elapsed < SplashFadeSeconds && !splashDismissed)
            {
                elapsed += Time.deltaTime;
                group.alpha = Mathf.Clamp01(elapsed / SplashFadeSeconds);
                yield return null;
            }
            elapsed = 0f;
            while (elapsed < SplashSeconds && !splashDismissed)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            var startAlpha = group.alpha;
            elapsed = 0f;
            while (elapsed < SplashFadeSeconds)
            {
                elapsed += Time.deltaTime;
                group.alpha = startAlpha * (1f - Mathf.Clamp01(elapsed / SplashFadeSeconds));
                yield return null;
            }
            splashRun = null;
            ShowSetup();
        }

        void DismissSplash()
        {
            splashDismissed = true;
            if (!Application.isPlaying) ShowSetup();
        }

        internal static string NoticeFor(string key, bool gained)
        {
            if (key.StartsWith("timeout:")) return "CPUによる代理アクション";
            if (key == "pass") return gained ? null : "パス";
            if (key == "abandon") return "放棄";
            if (key == "double") return "ダブル";
            if (key == "reshuffle") return "配り直し";
            if (key == "cancel_double") return "ダブルを取り消し";
            return null;
        }

        void QueueNotice(int seat, string key, bool gained)
        {
            var caption = NoticeFor(key, gained);
            if (caption == null || !Application.isPlaying || catchingUpCards) return;
            actionNotices.Enqueue(new KeyValuePair<int, string>(seat, caption));
            if (noticeRun == null) noticeRun = StartCoroutine(ShowNotices());
        }

        void QueueTimeoutNotice(int seat)
        {
            if (timeoutNoticeSeats.Add(seat)) QueueNotice(seat, "timeout:" + seat, false);
        }

        IEnumerator ShowNotices()
        {
            // Outside Frame: rebuilding the table must not interrupt notifications.
            while (actionNotices.Count > 0)
            {
                var notice = actionNotices.Dequeue();
                var root = DrawActionNotice(notice.Value);
                var elapsed = 0f;
                while (elapsed < 0.5f)
                {
                    // Resolve the rebuilt seat every frame, including orientation changes.
                    if (notice.Key >= 0 && notice.Key < seatFrames.Count && seatFrames[notice.Key] != null)
                    {
                        var seat = seatFrames[notice.Key];
                        root.position = seat.TransformPoint(new Vector3(seat.rect.width - 350f, -100f, 0f));
                        root.localScale = frame.localScale;
                    }
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                Destroy(root.gameObject);
            }
            noticeRun = null;
        }

        RectTransform DrawActionNotice(string caption)
        {
            var root = Portrait.Rect(transform, "action-notice", 0f, 0f, 320f, 70f);
            var fill = Hex("#D6B98C");
            Portrait.Box(root, "panel", 0f, 0f, 320f, 70f, 7f, 1f, fill, Ink, false);
            Bold(TextAt(root, caption, 12f, 8f, 296f, 54f, 26, Ink, nameFont, TextAnchor.MiddleCenter));
            root.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            return root;
        }

        void ApplyMode()
        {
            simpleMode = false;
            sequenceRule = titleRule = specialRule = true;
        }

        void LoadRules()
        {
            if (!Application.isPlaying) return;
            simpleMode = false;
            playerCount = PlayerPrefs.GetInt("quota.players", 3);
            playerName = PlayerPrefs.GetString("quota.name", "あなた");
            if (string.IsNullOrWhiteSpace(playerName)) playerName = "あなた";
            okTimeout = PlayerPrefs.GetFloat("quota.okTimeout", 5f);
            turnTimeout = PlayerPrefs.GetFloat("quota.turnTimeout", 30f);
            confirmActions = PlayerPrefs.GetInt("quota.confirmActions", 1) == 1;
            ApplyMode();
        }

        void SaveRules()
        {
            if (!Application.isPlaying) return;
            PlayerPrefs.SetInt("quota.saved", 1);
            PlayerPrefs.SetInt("quota.sequence", sequenceRule ? 1 : 0);
            PlayerPrefs.SetInt("quota.title", titleRule ? 1 : 0);
            PlayerPrefs.SetInt("quota.special", specialRule ? 1 : 0);
            PlayerPrefs.SetString("quota.name", string.IsNullOrWhiteSpace(playerName) ? "あなた" : playerName.Trim());
            if (!NetworkJoined || (networkState.you != null && networkState.you.leader))
            {
                PlayerPrefs.SetInt("quota.players", playerCount);
                PlayerPrefs.SetFloat("quota.okTimeout", okTimeout);
                PlayerPrefs.SetFloat("quota.turnTimeout", turnTimeout);
            }
            PlayerPrefs.SetInt("quota.confirmActions", confirmActions ? 1 : 0);
            PlayerPrefs.Save();
        }

        void ShowSetup()
        {
            ResetCardMotion();
            pulsingSeat = -1;
            RemoveLeaveButton();
            actionNotices.Clear();
            if (noticeRun != null) StopCoroutine(noticeRun);
            noticeRun = null;
            var notice = transform.Find("action-notice");
            if (notice != null) DestroyImmediate(notice.gameObject);
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
            ceremonyTitles = false;
            titleVerdictRound = 0;
            ceremonyMaxHeight = 0f;
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
            var columnTop = wide ? 264f : 384f;
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
            var tableCount = Mathf.Min(4, networkTables.Count);
            var fixedSpace = 80f + Mathf.Max(1, tableCount) * 12f + (showReview ? 16f : 0f);
            var buttonH = Mathf.Clamp((available - fixedSpace) / (13f + Mathf.Max(1, tableCount) + (showReview ? 1f : 0f)), 28f, 72f);
            var font = Mathf.Max(18, Mathf.RoundToInt(32f * buttonH / 72f));
            var column = SetupColumn(screenW, columnTop, available + 24f);
            var guideW = screenW * 0.40f;
            var rowW = screenW * 0.60f;
            var actionW = screenW * 0.35f;
            var section = buttonH * 2f;
            SetupButton(column, "遊び方", () => OpenPage("play"), guideW, buttonH, font);
            SetupGap(column, innerGap);
            SetupButton(column, "詳細ルール", () => OpenPage("details"), guideW, buttonH, font);
            SetupGap(column, section);
            var labelW = LabelSlot(font, "あなたの名前：");
#if UNITY_WEBGL && !UNITY_EDITOR
            SetupChoiceRow(column, "あなたの名前：", HumanName(), rowW, buttonH, labelW, rowW - labelW - 12f - font, font,
                () => QuotaEditName(HumanName(), gameObject.name));
#else
            var name = SetupNameRow(column, "あなたの名前：", string.IsNullOrWhiteSpace(playerName) ? "あなた" : playerName, rowW, buttonH, labelW, rowW - labelW - 12f - font, font);
            name.onValueChanged.AddListener(value => playerName = value);
#endif
            SetupGap(column, section);
            SetupButton(column, "新規ゲーム卓の準備", OpenLobby, rowW, buttonH * 2f, Mathf.RoundToInt(font * 1.3f));
            SetupGap(column, innerGap);
            var tablePanel = new GameObject("table-list", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(VerticalLayoutGroup));
            tablePanel.transform.SetParent(column, false);
            SizeElement(tablePanel.GetComponent<LayoutElement>(), rowW, buttonH * (Mathf.Max(1, tableCount) + 1) + 32f + Mathf.Max(1, tableCount) * 12f);
            var panelFill = tablePanel.GetComponent<Image>();
            panelFill.sprite = Portrait.SlicedRound;
            panelFill.type = Image.Type.Sliced;
            panelFill.color = Paper;
            panelFill.raycastTarget = false;
            var layout = tablePanel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var tableRoot = tablePanel.GetComponent<RectTransform>();
            SetupNotice(tableRoot, "参加・観戦できるゲーム卓", rowW - 40f, buttonH, font);
            var viewportHeight = Mathf.Max(1, tableCount) * (buttonH + 12f) - 12f;
            var viewportObject = new GameObject("table-viewport", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewportObject.transform.SetParent(tableRoot, false);
            SizeElement(viewportObject.GetComponent<LayoutElement>(), rowW - 40f, viewportHeight);
            viewportObject.GetComponent<Image>().color = Color.clear;
            var viewport = viewportObject.GetComponent<RectTransform>();
            var content = Portrait.Rect(viewport, "table-list-content", 0f, 0f, rowW - 40f, Mathf.Max(1, networkTables.Count) * (buttonH + 12f) - 12f);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, Mathf.Max(1, networkTables.Count) * (buttonH + 12f) - 12f);
            content.anchoredPosition = new Vector2(0f, (1f - tableScroll) * Mathf.Max(0f, content.sizeDelta.y - viewportHeight));
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 12f;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = contentLayout.childForceExpandHeight = false;
            var scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = networkTables.Count > 4;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.onValueChanged.AddListener(value => tableScroll = value.y);
            for (var i = 0; i < networkTables.Count; i++)
            {
                var table = networkTables[i];
                var action = table.rejoin ? "再び参加" : (table.status == "募集中" && table.seated < table.players ? "参加" : "観戦");
                SetupButton(content, $"{table.leader}　人間 {table.seated}/{table.players}　{action}", () => JoinNetworkTable(table.id), rowW - 40f, buttonH, font);
            }
            if (tableCount == 0) SetupNotice(content, "現在、卓はありません", rowW - 40f, buttonH, font);
            SetupGap(column, section);
            SetupButton(column, "実装テスト", () => OpenPage("tests"), actionW, buttonH, font);
            if (showReview)
            {
                SetupGap(column, innerGap);
                SetupButton(column, "ゲーム終了の卓を見る", ShowReview, actionW, buttonH, font);
            }
        }

        void DrawLobby(float screenW, float columnTop, float available, bool showReview)
        {
            const float innerGap = 16f;
            var leader = !NetworkJoined || (networkState.you != null && networkState.you.leader);
            var seats = playerCount;
            var buttonH = Mathf.Clamp((available - 176f - (seats + 9) * 16f) / (seats + 11f), 24f, 60f);
            var font = Mathf.Max(18, Mathf.RoundToInt(32f * buttonH / 72f));
            var viewport = Portrait.Rect(frame, "lobby-viewport", 0f, columnTop, screenW, available + 24f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var column = Portrait.Rect(viewport, "setup", 0f, 0f, screenW, available + 24f);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var rowW = screenW * 0.60f;
            var actionW = screenW * 0.35f;
            var labelW = LabelSlot(font, "プレイヤーの数：");
            SetupNotice(column, "ゲーム設定", rowW, buttonH, font);
            if (leader) SetupChoiceRow(column, "プレイヤーの数：", $"{playerCount}人", rowW, buttonH, labelW, rowW - labelW - 12f - font, font, () =>
            {
                var next = playerCount == 3 ? 4 : 3;
                if (NetworkJoined) SetNetworkPlayers(next);
                else
                {
                    playerCount = next;
                    SaveRules();
                    ShowSetup();
                }
            });
            else SetupNotice(column, $"プレイヤーの数：{playerCount}人", rowW, buttonH, font);
            SetupGap(column, innerGap);
            var seatNumber = 1;
            foreach (var seat in LobbySeats())
            {
                SetupSeatRow(column, $"{seatNumber}. {seat.Key}", seat.Value, rowW, buttonH, font);
                seatNumber++;
                SetupGap(column, innerGap);
            }
            if (!NetworkJoined || (networkState.you != null && networkState.you.leader))
                SetupButton(column, "CPUプレイヤー入れ替え", NetworkJoined ? (UnityAction)ShuffleNetworkCast : ShuffleCast, rowW, buttonH, font);
            SetupGap(column, innerGap);
            SetupButton(column, sitOut ? "自分は参加せずに参戦: YES" : "自分は参加せずに参戦: NO", () =>
            {
                if (NetworkJoined)
                    StartCoroutine(NetworkPost("/api/participation", JsonUtility.ToJson(new NetworkCreate { sit_out = !sitOut })));
                else { sitOut = !sitOut; ShowSetup(); }
            }, rowW, buttonH, font);
            SetupGap(column, innerGap);
            DrawLobbyTimeout(column, "OKタイムアウト（秒）", true, leader, rowW, buttonH, font);
            SetupGap(column, innerGap);
            DrawLobbyTimeout(column, "手番タイムアウト（秒）", false, leader, rowW, buttonH, font);
            SetupGap(column, buttonH);
            SetupNotice(column, "個人設定", rowW, buttonH, font);
            SetupButton(column, "放棄などに確認を求める: " + (confirmActions ? "YES" : "NO"), () =>
            {
                confirmActions = !confirmActions; SaveRules(); ShowSetup();
            }, rowW, buttonH, font);
            SetupGap(column, buttonH);
            if (NetworkJoined)
            {
                if (leader) SetupButton(column, "ゲーム開始", StartNetworkMatch, actionW, buttonH * 2f, font * 2);
                else SetupNotice(column, "ゲーム開始待ち", rowW, buttonH, font);
            }
            else SetupButton(column, "ゲーム開始", () => StartMatch(sitOut), actionW, buttonH * 2f, font * 2);
            SetupGap(column, innerGap);
            SetupButton(column, "戻る", NetworkJoined ? (UnityAction)LeaveNetworkTable : CloseLobby, actionW, buttonH, font);
            if (showReview)
            {
                SetupGap(column, innerGap);
                SetupButton(column, "ゲーム終了の卓を見る", ShowReview, actionW, buttonH, font);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(column);
            var contentH = Mathf.Max(available + 24f, LayoutUtility.GetPreferredHeight(column));
            column.anchorMin = new Vector2(0f, 1f);
            column.anchorMax = new Vector2(1f, 1f);
            column.pivot = new Vector2(0.5f, 1f);
            column.sizeDelta = new Vector2(0f, contentH);
            column.anchoredPosition = Vector2.zero;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = column;
            scroll.horizontal = false;
            scroll.vertical = contentH > available + 24f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;
        }

        List<KeyValuePair<string, bool>> LobbySeats()
        {
            if (NetworkJoined)
            {
                var networkSeats = new List<KeyValuePair<string, bool>>();
                if (networkState.seats != null)
                    foreach (var seat in networkState.seats ?? new NetworkSeat[0])
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
            if (Application.isPlaying)
            {
                playerCount = PlayerPrefs.GetInt("quota.players", 3);
                okTimeout = PlayerPrefs.GetFloat("quota.okTimeout", 5f);
                turnTimeout = PlayerPrefs.GetFloat("quota.turnTimeout", 30f);
            }
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
            yield return null;
            while (SharedNetwork)
            {
                if (!networkRequest) yield return NetworkGet();
                yield return new WaitForSeconds(0.4f);
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
            try
            {
            var posted = json != null;
#if UNITY_WEBGL && !UNITY_EDITOR
            webNetworkDone = false;
            webNetworkResponse = null;
            QuotaFetch(
                posted ? "POST" : "GET",
                CurrentOrigin() + path,
                json ?? "",
                NetworkClient(),
                gameObject.name);
            var waited = 0f;
            while (!webNetworkDone && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (webNetworkDone)
            {
                var response = JsonUtility.FromJson<WebNetworkResult>(webNetworkResponse);
                if (response != null && response.ok) ApplyNetworkState(response.body);
                else MarkWeb("ロビー通信失敗 " + (response != null ? response.error : "invalid response"));
            }
            else MarkWeb("ロビー通信タイムアウト " + path);
            networkRequest = false;
            yield break;
#else
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
#endif
            }
            finally { networkRequest = false; }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void QuotaEditChipColor(string value, string target);
#endif
        void EditChipColor()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            QuotaEditChipColor(chipLabState.Rgb, gameObject.name);
#endif
        }

        public void OnChipColorEdited(string value)
        {
            chipLabState.TrySetRgb(value);
            if (setupPage == "chips") ShowSetup();
        }

        public void OnNameEdited(string value)
        {
            playerName = string.IsNullOrWhiteSpace(value) ? "あなた" : value.Trim();
            PlayerPrefs.SetString("quota.name", playerName);
            PlayerPrefs.Save();
            if (onSetup) ShowSetup();
        }

        public void OnNetworkResponse(string response)
        {
            webNetworkResponse = response;
            webNetworkDone = true;
        }

        void ApplyNetworkState(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var next = JsonUtility.FromJson<NetworkSnapshot>(json);
            if (next == null || !string.IsNullOrEmpty(next.error)) return;
            var wasRecruiting = networkState != null && networkState.phase == "recruiting";
            networkState = next;
            networkTurnDeadline = Time.realtimeSinceStartupAsDouble + next.turn_remaining;
            if (next.phase == "hall")
            {
                networkLeaving = false;
                networkNavigating = false;
                ReadHallTables(json);
                if (wasRecruiting) lobbyOpen = false;
                if (!onSetup && match.Game != null) { match.Clear(); cpuRun++; busy = false; ShowSetup(); }
            }
            else if (next.phase == "recruiting")
            {
                networkTables.Clear();
                lobbyOpen = true;
                playerCount = next.players;
                if (next.you != null) sitOut = next.you.observer;
                if (next.options != null)
                {
                    okTimeout = next.options.ok_timeout;
                    turnTimeout = next.options.turn_timeout;
                }
            }
            else if (next.phase == "playing")
            {
                if (networkLeaving) return;
                catchingUpCards = !networkNavigating && next.actions != null && next.actions.Length > 0;
                if (!networkNavigating)
                {
                    networkNavigating = true;
                    networkApplied = 0;
                    timedOutSeats.Clear();
                    departedSeats.Clear();
                    timeoutNoticeSeats.Clear();
                    if (onSetup)
                    {
                        playerCount = next.players;
                        seedText = next.seed.ToString();
                        if (next.options != null)
                        {
                            simpleMode = false;
                            sequenceRule = next.options.sequence;
                            titleRule = next.options.title;
                            specialRule = next.options.special;
                            okTimeout = next.options.ok_timeout;
                            turnTimeout = next.options.turn_timeout;
                        }
                        StartMatch(false);
                    }
                }
                ApplyNetworkActions(next.actions);
                var restoredEnding = catchingUpCards && match.Game != null && (match.Game.AwaitingNextRound || match.Game.Finished);
                catchingUpCards = false;
                if (restoredEnding) ShowCompletedCeremony();
                return;
            }
            var signature = next.phase + ":" + (next.table_id ?? "") + ":" + HallSignature() + ":" + JsonUtility.ToJson(next);
            if (signature == networkSignature) return;
            foreach (var input in GetComponentsInChildren<InputField>())
                if (input.isFocused) return;
            networkSignature = signature;
            if (onSetup && frame != null && frame.Find("splash") == null) ShowSetup();
        }

        void ReadHallTables(string json)
        {
            networkTables.Clear();
            var array = SliceJsonArray(json, "tables");
            var start = 0;
            while (start < array.Length)
            {
                var open = array.IndexOf('{', start);
                if (open < 0) break;
                var depth = 0;
                var close = -1;
                for (var i = open; i < array.Length; i++)
                {
                    if (array[i] == '{') depth++;
                    else if (array[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            close = i;
                            break;
                        }
                    }
                }
                if (close < 0) break;
                var chunk = array.Substring(open, close - open + 1);
                var table = new NetworkTable
                {
                    id = JsonString(chunk, "id"),
                    leader = JsonString(chunk, "leader"),
                    status = JsonString(chunk, "status"),
                    players = JsonInt(chunk, "players"),
                    seated = JsonInt(chunk, "seated"),
                    rejoin = JsonUtility.FromJson<NetworkTable>(chunk).rejoin,
                };
                if (!string.IsNullOrEmpty(table.id)) networkTables.Add(table);
                start = close + 1;
            }
        }

        static string JsonString(string json, string name)
        {
            var key = "\"" + name + "\"";
            var at = json.IndexOf(key, System.StringComparison.Ordinal);
            if (at < 0) return "";
            var colon = json.IndexOf(':', at + key.Length);
            var first = json.IndexOf('"', colon + 1);
            var last = first < 0 ? -1 : json.IndexOf('"', first + 1);
            return first < 0 || last < 0 ? "" : json.Substring(first + 1, last - first - 1);
        }

        static int JsonInt(string json, string name)
        {
            var key = "\"" + name + "\"";
            var at = json.IndexOf(key, System.StringComparison.Ordinal);
            if (at < 0) return 0;
            var colon = json.IndexOf(':', at + key.Length);
            if (colon < 0) return 0;
            var end = colon + 1;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == ' ')) end++;
            int.TryParse(json.Substring(colon + 1, end - colon - 1).Trim(), out var value);
            return value;
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
                sit_out = sitOut,
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
            playerCount = players; SaveRules();
            StartCoroutine(NetworkPost("/api/players", JsonUtility.ToJson(new NetworkPlayers { players = players })));
        }

        void ShuffleNetworkCast()
        {
            StartCoroutine(NetworkPost("/api/shuffle", "{}"));
        }

        [System.Serializable]
        sealed class NetworkActionRequest { public string key; public int revision; }

        void SendNetworkAction(string key)
        {
            if (key != "next_round" && networkState != null && networkState.turn_timeout_active && networkTurnDeadline <= Time.realtimeSinceStartupAsDouble) return;
            confirm = null;
            StartCoroutine(NetworkPost("/api/action", JsonUtility.ToJson(new NetworkActionRequest { key = key, revision = networkApplied })));
        }

        void ApplyNetworkActions(string[] actions)
        {
            if (actions == null || match.Game == null) return;
            var changed = false;
            while (networkApplied < actions.Length)
            {
                var key = actions[networkApplied];
                var game = match.Game;
                var beforeCards = BoardFrame.Capture(game);
                if (key.StartsWith("timeout:"))
                {
                    var seat = int.Parse(key.Substring(8));
                    timedOutSeats.Add(seat);
                    if (!catchingUpCards) QueueTimeoutNotice(seat);
                }
                else if (!catchingUpCards && (timedOutSeats.Contains(game.Current) || IsSpectating || game.Current != HumanSeat(game)))
                    QueueNotice(game.Current, key, !timedOutSeats.Contains(game.Current) && (game.TurnGain || game.DoubleGained));
                if (key.StartsWith("away:")) departedSeats.Add(int.Parse(key.Substring(5)));
                else if (key.StartsWith("human:"))
                {
                    var seat = int.Parse(key.Substring(6));
                    game.Players[seat].IsHuman = true;
                    departedSeats.Remove(seat);
                    timedOutSeats.Remove(seat);
                    timeoutNoticeSeats.Remove(seat);
                }
                else if (key.StartsWith("timeout:")) { }
                else if (key.StartsWith("cpu:")) game.Players[int.Parse(key.Substring(4))].IsHuman = false;
                else if (key == "double") game.DeclareDouble();
                else if (key == "reshuffle") game.DeclareReshuffle();
                else if (key == "cancel_double") game.CancelDouble();
                else if (key == "next_round") game.BeginNextRound();
                else if (key == "pass") game.Step(new Pass());
                else if (key == "abandon") game.Step(new Abandon());
                else if (key.StartsWith("take:")) game.Step(new TakeQuota(int.Parse(key.Substring(5))));
                else if (key.StartsWith("collect:"))
                {
                    var ids = new List<int>();
                    foreach (var id in key.Substring(8).Split(',')) ids.Add(int.Parse(id));
                    game.Step(new Collect(ids));
                }
                TrackCardChange(beforeCards);
                networkApplied++;
                changed = true;
            }
            if (changed)
            {
                // A remote player acting must not dismiss a local EXIT confirmation.
                if (confirm != "leave") confirm = null;
                busy = false;
                ShowTable();
            }
        }

        void StartNetworkMatch()
        {
            StartCoroutine(NetworkPost("/api/start", JsonUtility.ToJson(new NetworkCreate
            { simple = simpleMode, sequence = sequenceRule, title = titleRule, special = specialRule, ok_timeout = okTimeout, turn_timeout = turnTimeout })));
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

        static string CurrentOrigin()
        {
            System.Uri uri;
            if (System.Uri.TryCreate(Application.absoluteURL, System.UriKind.Absolute, out uri)
                && (uri.Scheme == "http" || uri.Scheme == "https"))
                return uri.GetLeftPart(System.UriPartial.Authority);
            return "http://127.0.0.1:8080";
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

        static float ParseSeconds(string text, float fallback, float minimum)
        {
            if (!float.TryParse(text, out var value) || float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return value < minimum ? minimum : value;
        }

        void StartMatch(bool cpuOnly)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer && !ItemCatalog.IsLoaded)
            {
                MarkWeb("対局開始失敗：品目データ未読込");
                networkNavigating = false;
                return;
            }
            lobbyOpen = false;
            cpuRun++;
            ceremonyRunning = false;
            ceremonyDismissed = false;
            acknowledgedRound = 0;
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
            var shared = networkState != null && networkState.phase == "playing";
            var humanSeats = new List<int>();
            if (shared)
            {
                foreach (var seat in networkState.seats ?? new NetworkSeat[0])
                {
                    humanSeats.Add(names.Count);
                    names.Add(seat.name);
                }
                firstCpu = names.Count;
            }
            else if (!cpuOnly) { names.Add(playerName); humanSeats.Add(0); }
            EnsureCast();
            var spare = 0;
            for (var i = firstCpu; i < playerCount; i++)
            {
                var character = shared && networkState.cpu_cast != null
                    ? networkState.cpu_cast[i - firstCpu]
                    : spare < lobbyCast.Count ? lobbyCast[spare++] : Characters.PickFresh(lobbyRng, new HashSet<int>(lobbyCast));
                characters.Add(character);
                names.Add(shared ? networkState.cpus[i - firstCpu] : Ranking.DisplayName(character));
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
                HumanSeats = humanSeats,
                SequenceRule = sequenceRule,
                TitleRule = titleRule,
                SpecialActionsRule = specialRule,
                Rounds = playerCount,
            }, pumpCpus: !Application.isPlaying && !shared);
            Characters.BindInOrder(match.Game, characters);
            BeginCardPresentation();
            confirm = null;
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
            if (ceremonyBreak && !catchingUpCards && Application.isPlaying && !CardsAnimating && !ceremonyRunning && acknowledgedRound != game.RoundIndex)
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
            // Portrait market cards sit directly on the photo, as in the supplied layout.
            if (wide) DrawMarketWide(game, theme);
            else DrawMarket(game, theme);
            if (wide) DrawTitleWide(game);
            else DrawTitle(game);
            DrawDeck(game);
            if (ceremonyRunning) LayoutCeremonyDots();
            if (!reviewMode && !game.AwaitingNextRound && !(Application.isPlaying && game.Finished && !ceremonyDismissed))
                ceremonyDialog = false;
            var showCeremony = reviewMode || ceremonyDialog;
            if (showCeremony) DrawCeremonyPanel();
            else if (confirm == null && !catchingUpCards && ceremonyBreak && game.AwaitingNextRound && !ceremonyRunning && acknowledgedRound == game.RoundIndex) DrawRoundBreak(game);
            else if (confirm == null && MyHumanTurn && !busy && !CardsAnimating && !game.Finished) DrawControls(game);
            if (!game.Finished) LeaveButton();
            else RemoveLeaveButton();
            if (!showCeremony && game.Finished && !ceremonyBreak && !CardsAnimating)
            {
                Result(game);
            }
            else if (confirm != null && (confirm == "leave" || (!ceremonyRunning && !showCeremony && !reviewMode))) Confirm();
        }

        static string RoundLabel(Game game)
        {
            return $"ラウンド {game.RoundIndex}/{game.RoundCount}";
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
            public bool rejoin;
            public string status;
        }

        [System.Serializable]
        sealed class NetworkSeat
        {
            public string name;
            public bool leader;
            public bool cpu;
        }

        [System.Serializable]
        sealed class NetworkYou
        {
            public int seat;
            public bool observer;
            public bool leader;
        }

        [System.Serializable]
        sealed class NetworkSnapshot
        {
            public string phase;
            public string table_id;
            public bool turn_timeout_active;
            public float turn_remaining;
            public int players;
            public int seed;
            public string[] actions;
            public int[] cpu_cast;
            public NetworkCreate options;
            public NetworkTable[] tables;
            public NetworkSeat[] seats;
            public string[] cpus;
            public NetworkYou you;
            public string error;
        }

        [System.Serializable]
        sealed class WebNetworkResult
        {
            public bool ok;
            public int status;
            public string body;
            public string error;
        }

        [System.Serializable]
        sealed class NetworkCreate
        {
            public int players;
            public string name;
            public bool sit_out;
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
            ceremonyTitles = false;
            titleVerdictRound = 0;
            ceremonyMaxHeight = 0f;
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
            dialogOrder = DisplayRows(game);
            ceremonyNamesOnly = true;
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
            foreach (var seat in dialogOrder)
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

        // A newly joined viewer sees the completed result, never the historical animation.
        void ShowCompletedCeremony()
        {
            cpuRun++;
            PrepareCeremony(match.Game);
            ceremonyRunning = false;
            titleVerdictRound = match.Game.RoundIndex;
            acknowledgedRound = match.Game.RoundIndex;
            ceremonyDialog = true;
            ceremonyNamesOnly = false;
            ceremonyExpand = 1f;
            ceremonyOverall = match.Game.RoundIndex >= 2;
            ceremonyEquation = ceremonyOverall;
            scoreOverride = new Dictionary<int, int>();
            foreach (var seat in dialogOrder)
                scoreOverride[seat] = roundScores[seat] + (ceremonyOverall ? previousScores[seat] : 0);
            dialogOrder.Sort((a, b) => scoreOverride[b].CompareTo(scoreOverride[a]));
            AssignPlaces(seat => scoreOverride[seat]);
            CaptureRankSlots();
            ceremonyTray.Clear();
            ceremonyWinner = ceremonyLast;
            ceremonyButton = "OK";
            ShowTable();
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
            ceremonyReasonShown = false;
            titleVerdictRound = match.Game.RoundIndex;
            ShowTable();
            ceremonyTitles = titleLines.Count > 0;
            yield return ExpandCeremony(serial);
            if (serial != cpuRun) yield break;
            if (ceremonyTitles)
            {
                yield return FlyTitles(serial);
                if (serial != cpuRun) yield break;
                yield return new WaitForSeconds(0.8f);
            }
            ceremonyTitles = false;
            ceremonyNamesOnly = false;
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
                ceremonyOverall = true;
                ceremonyRankTitle = null;
                ceremonyEquation = false;
                ceremonyButton = null;
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
                    StartCoroutine(AnimateFly(serial, from, to, CoinKind.Blue, () =>
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
                        StartCoroutine(AnimateFly(serial, from, to, dot.Kind, () =>
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

        IEnumerator AnimateFly(int serial, Vector3 from, Vector3 to, CoinKind kind, System.Action arrived)
        {
            var chip = BonusChipLab.GameChip(kind, serial + dotSerial);
            var bounds = BonusChipLab.ChipBounds(chip);
            var scale = frame != null ? frame.lossyScale.x : 1f;
            var ring = Portrait.Rect(transform, "flyer", 0f, 0f, bounds.width * scale, bounds.height * scale);
            var disk = BonusChipLab.DrawGameChip(transform, "flyer", chip, 0f, 0f, scale);
            var chipLayer = ring.gameObject.AddComponent<Canvas>();
            chipLayer.overrideSorting = true; chipLayer.sortingOrder = 95;
            if (disk != null)
            {
                var diskLayer = disk.gameObject.AddComponent<Canvas>();
                diskLayer.overrideSorting = true; diskLayer.sortingOrder = 95;
            }
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
            acknowledgedRound = match.Game.RoundIndex;
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
                if (NetworkPlaying) { networkLeaving = true; StartCoroutine(NetworkPost("/api/leave", "{}")); }
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
            if (NetworkPlaying)
            {
                if (networkState.you != null && networkState.you.leader) SendNetworkAction("next_round");
                else ShowTable();
                return;
            }
            var beforeCards = BoardFrame.Capture(match.Game);
            match.Game.BeginNextRound();
            TrackCardChange(beforeCards);
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
            const float width = 1040f;
            var x = wide ? LandMarketX - 390f : 20f;
            const float y = 56f;
            const float compactH = 340f;
            var required = ceremonyTitles ? titleLines.Count * 56f + 136f : (dialogOrder?.Count ?? 0) * 64f + 232f + (ceremonyReasonShown ? 48f : 0f);
            ceremonyMaxHeight = Mathf.Max(ceremonyMaxHeight, Mathf.Lerp(compactH, Mathf.Max(compactH, required), Mathf.Clamp01(expand)));
            return new Rect(x, y, width, Mathf.Max(compactH, ceremonyMaxHeight));
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
            var panel = Portrait.Box(frame, "ceremony", box.x, box.y, box.width, box.height, 18f, 1f, new Color(0.45f, 0.23f, 0.18f, 0.88f), Ink, false);
            var layer = panel.gameObject.AddComponent<Canvas>();
            layer.overrideSorting = true; layer.sortingOrder = 90;
            panel.gameObject.AddComponent<GraphicRaycaster>();
            if (!reviewMode && ceremonyExpand < 1f) DrawCompactCeremony(panel, box.width, box.height);
            else DrawScoreCeremony(panel, box.width, box.height);
            LayoutCeremonyDots();
        }

        void DrawCompactCeremony(RectTransform panel, float panelW, float panelH)
        {
            const float headH = 52f;
            var reasonH = ceremonyReasonShown && !string.IsNullOrEmpty(ceremonyReason) ? 44f : 0f;
            var y = Mathf.Max(12f, (panelH - headH - reasonH) * 0.5f);
            var head = TextAt(panel, ceremonyHeading, 16f, y, panelW - 32f, headH, 40, Color.white, nameFont, TextAnchor.MiddleCenter);
            head.fontStyle = FontStyle.Bold;
            head.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (reasonH <= 0f) return;
            var reason = TextAt(panel, ceremonyReason, 16f, y + headH, panelW - 32f, reasonH, 28, Color.white, nameFont, TextAnchor.MiddleCenter);
            reason.fontStyle = FontStyle.Bold;
            reason.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void DrawScoreCeremony(RectTransform panel, float panelW, float panelH)
        {
            if (ceremonyTitles)
            {
                Bold(TextAt(panel, ceremonyHeading, 24f, 16f, panelW - 48f, 56f, 40, Color.white, nameFont, TextAnchor.MiddleCenter));
                for (var i = 0; i < titleLines.Count; i++)
                {
                    var line = titleLines[i];
                    var text = line.Text;
                    var colon = text.IndexOf(" : ");
                    var who = match.Game.Players[line.Seat].Name;
                    var award = colon >= 0 ? text.Substring(colon + 3) : text;
                    var suffix = award.IndexOf("ボーナス");
                    if (suffix >= 0) award = award.Substring(0, suffix);
                    Bold(TextAt(panel, who, 64f, 88f + i * 56f, 520f, 56f, NameFontSize(who, 520f, 32), Color.white, nameFont, TextAnchor.MiddleLeft));
                    var tag = Bold(TextAt(panel, "「" + award + "」", 600f, 88f + i * 56f, panelW - 624f, 56f, 30, Color.white, nameFont, TextAnchor.MiddleLeft));
                    tag.gameObject.name = "award" + i;
                }
                return;
            }
            const float headH = 56f;
            const float lineH = 48f;
            const float winnerH = 72f;
            var reasonH = ceremonyReasonShown && !string.IsNullOrEmpty(ceremonyReason) ? lineH : 0f;
            var rows = dialogOrder != null ? dialogOrder.Count : 0;
            var y = 16f;
            var head = TextAt(panel, ceremonyHeading, 16f, y, panelW - 32f, headH, 40, Color.white, nameFont, TextAnchor.MiddleCenter);
            head.fontStyle = FontStyle.Bold;
            head.horizontalOverflow = HorizontalWrapMode.Overflow;
            y += headH;
            if (reasonH > 0f)
            {
                var reason = TextAt(panel, ceremonyReason, 16f, y, panelW - 32f, reasonH, 28, Color.white, nameFont, TextAnchor.MiddleLeft);
                reason.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += reasonH;
            }
            var reserved = 96f + (ceremonyWinner ? winnerH : 0f);
            var room = Mathf.Max(48f, panelH - y - reserved);
            var rowH = rows > 0 ? Mathf.Clamp(room / rows, 56f, 64f) : 64f;
            var font = 32;
            var rankW = Mathf.Max(72f, font * 2.6f);
            var nameW = panelW * 0.46f;
            if (dialogOrder != null && match.Game != null)
            {
                for (var r = 0; r < dialogOrder.Count; r++)
                {
                    var seat = dialogOrder[r];
                    if (!ceremonyBlank)
                    {
                        var row = Portrait.Rect(panel, "row" + seat, 16f, y, panelW - 32f, rowH);
                        var rankLabel = TextAt(row, RankSlotText(r), 0f, 0f, rankW, rowH, font, Color.white, nameFont, TextAnchor.MiddleLeft);
                        rankLabel.gameObject.name = "rank";
                        var mover = Portrait.Rect(row, "mover", rankW, 0f, panelW - 32f - rankW, rowH);
                        var who = TextAt(mover, match.Game.Players[seat].Name, 0f, 0f, nameW, rowH, NameFontSize(match.Game.Players[seat].Name, nameW, font), Color.white, nameFont, TextAnchor.MiddleLeft);
                        who.horizontalOverflow = HorizontalWrapMode.Overflow;
                        var figure = TextAt(mover, FigureText(seat), nameW + 8f, 0f, Mathf.Max(80f, panelW - 32f - rankW - nameW - 8f), rowH, font, Color.white, nameFont, TextAnchor.MiddleRight);
                        figure.gameObject.name = "figure";
                        figure.supportRichText = true;
                        figure.horizontalOverflow = HorizontalWrapMode.Overflow;
                    }
                    y += rowH;
                }
            }
            if (ceremonyWinner)
            {
                var cheer = TextAt(panel, WinnerText(), 16f, y, panelW - 32f, winnerH, 28, Color.white, nameFont, TextAnchor.MiddleLeft);
                cheer.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            foreach (var text in panel.GetComponentsInChildren<Text>()) text.fontStyle = FontStyle.Bold;
            if (string.IsNullOrEmpty(ceremonyButton)) return;
            var caption = ceremonyButton;
            var buttonW = caption.Length * 32f + 48f;
            Pill(panel, caption, (panelW - buttonW) * 0.5f, panelH - 80f, buttonW, 64f, 28, () =>
            {
                if (!reviewMode)
                {
                    ceremonyOk = true;
                    if (!ceremonyRunning) FinishCeremony();
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
            if (ceremonyNamesOnly && !reviewMode) return "";
            var score = scoreOverride != null && scoreOverride.TryGetValue(seat, out var shown) ? shown : 0;
            var round = roundScores != null && roundScores.TryGetValue(seat, out var gained) ? gained : 0;
            var prev = previousScores != null && previousScores.TryGetValue(seat, out var before) ? before : 0;
            if (ceremonyEquation && score == prev + round) return $"{prev} + {round} = {score}";
            if (plusOverride != null && plusOverride.TryGetValue(seat, out var plus))
                return $"{prev} + {round}";
            return score.ToString();
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
            return BaseScore(player).ToString();
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
            var award = frame != null ? frame.Find("ceremony/award" + line) as RectTransform : null;
            if (award != null) return CenterOf(award);
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
            var chip = BonusChipLab.GameChip(dot.Kind, dot.Serial);
            var bounds = BonusChipLab.ChipBounds(chip);
            var px = x + Centered(dot.Serial * 2 + 1) * Mathf.Max(0f, width - bounds.width);
            var py = y + Centered(dot.Serial * 2 + 5) * Mathf.Max(0f, height - bounds.height);
            var holder = Portrait.Rect(tray, "cdot-" + dot.Serial, px, py, bounds.width, bounds.height);
            BonusChipLab.DrawGameChip(holder, "disk", chip, 0f, 0f);
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
            ShowCompletedCeremony();
        }

        Material logoInk;

        void DrawTitle(Game game)
        {
            var mark = TitleSprite(true);
            if (mark != null)
            {
                PlaceSprite(frame, "title-mark", mark, 24f, 28f, 640f - Mathf.Max(0, game.Config.ResolvedStallThreshold() * 2 - 6) * 22f, 116f);
                var image = frame.Find("title-mark").GetComponent<Image>();
                image.color = Color.white;
                if (logoInk == null) logoInk = new Material(Resources.Load<Shader>("Quota/LogoInk")) { hideFlags = HideFlags.HideAndDontSave };
                image.material = logoInk;
            }
            else Shade(TextAt(frame, "QUOTA", 24f, 28f, 680f, 116f, 76, Cream, nameFont, TextAnchor.MiddleLeft));
            var note = game.DoubleStage == 1 ? "ダブル：1回目の行動です。" : game.DoubleStage == 2 ? "ダブル：2回目の行動です。" : game.Plan == "reshuffle" ? "配り直しました。行動を選んでください。" : "";
            if (note.Length > 0) Shade(TextAt(frame, note, 24f, 134f, 630f, 24f, 16, Cream, nameFont, TextAnchor.MiddleRight));
            DrawBoardStatus(game);
        }

        void DrawTitleWide(Game game)
        {
            var mark = TitleSprite(true);
            if (mark != null) PlaceSprite(frame, "title-mark", mark, 28f, 12f, 250f, 64f);
            else Shade(TextAt(frame, "QUOTA", 28f, 16f, 250f, 68f, 56, Cream, nameFont, TextAnchor.MiddleLeft));
            DrawBoardStatus(game);
        }

        void DrawBoardStatus(Game game)
        {
            var threshold = game.Config.ResolvedStallThreshold();
            var x = (WideScreen() ? LandWidth : ScreenWidth) - 412f - Mathf.Max(0, threshold * 2 - 6) * 22f;
            TextAt(frame, RoundLabel(game), x + 8f, 72f, 236f, 30f, 22, Ink, nameFont, TextAnchor.MiddleLeft);
            TextAt(frame, "膠着:", x + 8f, 104f, 70f, 28f, 20, Ink, nameFont, TextAnchor.MiddleLeft);
            var filled = Mathf.Clamp((game.StallFlag ? threshold : 0) + game.NoGainStreak, 0, threshold * 2);
            for (var i = 0; i < threshold * 2; i++)
                Portrait.Circle(frame, "stall-dot-" + i, x + 80f + i * 22f, 107f, 19f,
                    i >= filled ? Hex("#E8E8E8") : i < threshold ? Hex("#FFD326") : Hex("#E64235"));
        }

        void DrawMarket(Game game, ItemSet theme)
        {
            var market = ShownMarket(game);
            var slots = Mathf.Max(game.MarketSize(), market.Count);
            if (slots == 0) return;
            var cardWidth = CardWidth * MarketScale;
            var gap = 4f;
            var group = slots * cardWidth + (slots - 1) * gap;
            var origin = (1080f - group) * 0.5f;
            var y = 158f;
            var me = game.Players[game.Current];
            var yours = MyHumanTurn && !game.Finished && confirm == null;
            for (var i = 0; i < market.Count; i++)
            {
                var card = market[i];
                if (card == null) continue;
                var legal = CanPlay(card, me);
                var playable = yours && !busy && !CardsAnimating && legal;
                var cardId = card.Id;
                var takingQuota = me.Quota == null;
                var x = origin + i * (cardWidth + gap);
                DrawCard(frame, theme, card, x, y, MarketScale, playable ? () =>
                {
                    if (takingQuota) Play(new TakeQuota(cardId));
                    else Play(new Collect(new[] { cardId }));
                } : null, yours && !legal);
            }
        }

        void DrawMarketWide(Game game, ItemSet theme)
        {
            const int columns = 4;
            var market = ShownMarket(game);
            var slots = Mathf.Max(game.MarketSize(), market.Count);
            if (slots == 0) return;
            var gapX = 10f;
            var gapY = 12f;
            var rows = Mathf.CeilToInt(slots / (float)columns);
            var groupW = columns * CardWidth * MarketScale + (columns - 1) * gapX;
            var groupH = rows * 200f + (rows - 1) * gapY;
            var originX = LandMarketX + (LandMarketW - groupW) * 0.5f;
            var originY = LandMarketY + (LandMarketH - groupH) * 0.5f;
            var me = game.Players[game.Current];
            var yours = MyHumanTurn && !game.Finished && confirm == null;
            for (var i = 0; i < market.Count; i++)
            {
                var card = market[i];
                if (card == null) continue;
                var legal = CanPlay(card, me);
                var playable = yours && !busy && !CardsAnimating && legal;
                var cardId = card.Id;
                var takingQuota = me.Quota == null;
                var x = originX + (i % columns) * (CardWidth * MarketScale + gapX);
                var y = originY + (i / columns) * (200f + gapY);
                DrawCard(frame, theme, card, x, y, MarketScale, playable ? () =>
                {
                    if (takingQuota) Play(new TakeQuota(cardId));
                    else Play(new Collect(new[] { cardId }));
                } : null, yours && !legal);
            }
        }

        int pulsingSeat = -1;
        int pulsingRound;
        float pulseStarted;

        Color NameplateColor(Game game, int index)
        {
            if (game.Current != pulsingSeat || game.RoundIndex != pulsingRound)
            {
                pulsingSeat = game.Current; pulsingRound = game.RoundIndex; pulseStarted = Time.time;
            }
            var playing = index == game.Current && !game.Finished && !game.AwaitingNextRound && !reviewMode;
            return playing ? TurnPulse(Time.time - pulseStarted) : Color.white;
        }

        static Color TurnPulse(float elapsed) => Color.Lerp(Color.white, Hex("#FFE6AD"), Mathf.PingPong(elapsed, 1f));

        void UpdateNameplates()
        {
            if (onSetup || frame == null || match.Game == null) return;
            for (var i = 0; i < seatFrames.Count; i++)
            {
                if (seatFrames[i] == null) continue;
                var plate = seatFrames[i].Find("nameplate/fill");
                if (plate != null) plate.GetComponent<Image>().color = NameplateColor(match.Game, i);
            }
        }

        string PlayerCaption(Game game, int seat)
        {
            var count = game.TurnOrder.Count;
            var position = count == 0 ? seat : (game.TurnOrder.IndexOf(seat) - (game.RoundIndex - 1) % count + count) % count;
            return ((char)('①' + position)).ToString() + game.Players[seat].Name + (departedSeats.Contains(seat) ? "（退席）" : "");
        }

        static int NameFontSize(string name, float width, int preferred)
        {
            if (string.IsNullOrEmpty(name)) return preferred;
            var fitted = Mathf.FloorToInt(width * 0.92f / name.Length);
            return Mathf.Clamp(fitted, 18, preferred);
        }

        void DrawPlayer(Game game, ItemSet theme, int index, int row)
        {
            var player = ShownPlayer(game, index);
            var top = SeatTop + row * SeatHeight;
            var seat = Portrait.Rect(frame, "seat" + index, 0f, top, ScreenWidth, SeatHeight);
            while (seatFrames.Count <= index) seatFrames.Add(null);
            seatFrames[index] = seat;
            Portrait.Box(seat, "plate", 25f, 25f, 1030f, 340f, 7f, 1f, Plate, Ink, false);
            Portrait.Box(seat, "nameplate", 0f, 10f, ScreenWidth * 0.5f, 50f, 4.5f, 1f, NameplateColor(game, index), Ink, true);
            var name = TextAt(seat, PlayerCaption(game, index), 12f, 10f, ScreenWidth * 0.5f - 24f, 50f, NameFontSize(PlayerCaption(game, index), ScreenWidth * 0.5f - 24f, 36), Ink, nameFont, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.fontStyle = FontStyle.Bold;
            var quotaTop = 75f;
            Bold(TextAt(seat, "ノルマ", 32f, quotaTop, 142f, 40f, 24, Ink, nameFont, TextAnchor.MiddleLeft));
            DrawQuotaProgress(seat, player, 32f, 120f, 142f, 62f, 48);
            var quotaCards = Portrait.Rect(seat, "quota-cards", 130f, quotaTop, 768f, 145f);
            quotaCards.gameObject.AddComponent<RectMask2D>();
            var strip = new List<Card>();
            if (player.Quota != null) strip.Add(player.Quota);
            strip.AddRange(player.Collection);
            LayQuotaCards(quotaCards, theme, strip, 6.5f);
            Bold(TextAt(seat, "実績", 32f, 220f, 96f, 40f, 24, Ink, nameFont, TextAnchor.UpperLeft));
            var achieved = Portrait.Rect(seat, "achieved-cards", 130f, 220f, 405f, 145f);
            achieved.gameObject.AddComponent<RectMask2D>();
            LayCards(achieved, theme, player.Achieved, 6.5f, 3f);
            Bold(TextAt(seat, "ボーナス", 535f, 220f, 212f, 40f, 24, Ink, nameFont, TextAnchor.UpperRight));
            const float trayX = 775f;
            const float trayY = 240f;
            const float trayW = 220f;
            const float trayH = 105f;
            var tray = Portrait.Box(seat, "chip-tray", trayX, trayY, trayW, trayH, 7f, 1f, Ecru, Ink, false);
            if (game.Config.TitleRule) DrawTrayTitles(seat, player, trayX, trayY - 62f, trayW, 60f, 16);
            float coinX, coinY, coinW, coinH, coinD;
            CoinSpot(false, out coinX, out coinY, out coinW, out coinH, out coinD);
            Portrait.Rect(tray, "coin-area", coinX, coinY, coinW, coinH);
            PlaceBonus(game, player, quotaCards, tray, coinX, coinY, coinW, coinH, coinD, 1f);
            var score = TextAt(seat, SeatPoints(game, index, player), 32f, 262f, 96f, 64f, 48, Ink, nameFont, TextAnchor.UpperLeft);
            score.gameObject.name = "score";
            score.fontStyle = FontStyle.Bold;
            if (game.RoundIndex >= 2) ((RectTransform)score.transform).anchoredPosition += new Vector2(0f, -30f);
            DrawPreviousScore(seat, game, player, false);
            DrawTurnCountdown(seat, index, false);
            if (game.Config.SpecialActionsRule)
            {
                DrawSpecialCard(seat, "double-card", "ダブル", player.DoubleActionLeft > 0, 900f, 75f, 148f, 38f, Hex("#cfe9f5"));
                DrawSpecialCard(seat, "reshuffle-card", "配り直し", player.ReshuffleTakeLeft > 0, 900f, 121f, 148f, 38f, Hex("#d9edcf"));
            }
        }

        void DrawPlayerWide(Game game, ItemSet theme, int index, int row)
        {
            var player = ShownPlayer(game, index);
            var top = LandHeader + row * LandSeatHeight;
            var seat = Portrait.Rect(frame, "seat" + index, 0f, top, LandLeft, LandSeatHeight);
            while (seatFrames.Count <= index) seatFrames.Add(null);
            seatFrames[index] = seat;
            Portrait.Box(seat, "plate", 16f, 22f, 1188f, 206f, 7f, 1f, Plate, Ink, false);
            Portrait.Box(seat, "nameplate", 16f, 6f, LandWidth * 0.5f, 46f, 4.5f, 1f, NameplateColor(game, index), Ink, false);
            var name = TextAt(seat, PlayerCaption(game, index), 28f, 6f, 648f, 46f, NameFontSize(PlayerCaption(game, index), 648f, 30), Ink, nameFont, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.fontStyle = FontStyle.Bold;
            if (game.Config.SpecialActionsRule)
            {
                DrawSpecialCard(seat, "double-card", "ダブル", player.DoubleActionLeft > 0, 976f, 8f, 110f, 34f, Hex("#cfe9f5"));
                DrawSpecialCard(seat, "reshuffle-card", "配り直し", player.ReshuffleTakeLeft > 0, 1094f, 8f, 110f, 34f, Hex("#d9edcf"));
            }

            DrawTurnCountdown(seat, index, true);
            var titled = game.Config.TitleRule;
            const float boxX = 28f;
            const float boxW = 176f;
            var boxY = titled ? 104f : 66f;
            var boxH = titled ? 110f : 148f;
            Portrait.Box(seat, "bonus-box", boxX, boxY, boxW, boxH, 7f, 1f, Ecru, Ink, false);
            if (titled) DrawTrayTitles(seat, player, boxX, boxY - 50f, boxW, 48f, 12);
            Bold(TextAt(seat, "ボーナス", 36f, boxY + 4f, 120f, 24f, 14, Ink, nameFont, TextAnchor.MiddleLeft));


            const float recordY = 66f;
            const float recordH = 148f;
            Portrait.Box(seat, "record-box", 216f, recordY, 220f, recordH, 7f, 1f, Ecru, Ink, false);
            Bold(TextAt(seat, "実績", 224f, recordY + 4f, 96f, 30f, 24, Ink, nameFont, TextAnchor.MiddleLeft));
            var score = TextAt(seat, SeatPoints(game, index, player), 224f, recordY + 34f, 96f, 64f, 48, Ink, nameFont, TextAnchor.UpperLeft);
            score.gameObject.name = "score";
            score.fontStyle = FontStyle.Bold;
            if (game.RoundIndex >= 2) ((RectTransform)score.transform).anchoredPosition += new Vector2(0f, -28f);
            DrawPreviousScore(seat, game, player, true);
            var achieved = Portrait.Rect(seat, "achieved-cards", 330f, recordY + 30f, 94f, recordH - 38f);
            achieved.gameObject.AddComponent<RectMask2D>();
            LayCards(achieved, theme, player.Achieved, 2f, 3f, 0.62f);

            Bold(TextAt(seat, "ノルマ", 452f, recordY, 104f, 30f, 24, Ink, nameFont, TextAnchor.MiddleLeft));
            DrawQuotaProgress(seat, player, 452f, recordY + 34f, 104f, 62f, 48);
            var quotaCards = Portrait.Rect(seat, "quota-cards", 564f, recordY + 22f, 618f, CardHeight + 4f);
            quotaCards.gameObject.AddComponent<RectMask2D>();
            var strip = new List<Card>();
            if (player.Quota != null) strip.Add(player.Quota);
            strip.AddRange(player.Collection);
            LayQuotaCards(quotaCards, theme, strip, 0f);
            var bonusBox = seat.Find("bonus-box");
            float coinX, coinY, coinW, coinH, coinD;
            CoinSpot(true, out coinX, out coinY, out coinW, out coinH, out coinD);
            coinH = Mathf.Max(24f, boxH - coinY - 34f);
            Portrait.Rect(bonusBox, "coin-area", coinX, coinY, coinW, coinH);
            PlaceBonus(game, player, quotaCards, bonusBox, coinX, coinY, coinW, coinH, coinD, 1f);
        }

        void DrawSpecialCard(Transform parent, string name, string caption, bool available, float x, float y, float width, float height, Color face)
        {
            var card = Portrait.Box(parent, name, x, y, width, height, 3f, 1f, available ? face : Hex("#ababab"), Ink, false);
            TextAt(card, available ? caption : "USED", 4f, 0f, width - 8f, height, 20, Ink, nameFont, TextAnchor.MiddleCenter);
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
            var lineH = band / 3f;
            var finalized = reviewMode || titleVerdictRound == match.Game.RoundIndex;
            bool Missing(string name)
            {
                foreach (var award in match.Game.TitleAwards(player))
                    if (award.name == name) return false;
                return true;
            }
            DrawTitleName(host, "title-mono", "単色達成", 0f, 0f, width, lineH, font, finalized ? Missing("単色達成") : TitleMonoOut(player));
            DrawTitleName(host, "title-purist", "生粋の買い付け", 0f, lineH, width, lineH, font, finalized ? Missing("生粋の買い付け") : TitlePuristOut(player));
            DrawTitleName(host, "title-variety", "五種の品揃え", 0f, lineH * 2f, width, lineH, font, finalized && Missing("五種の品揃え"));
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
                var count = pair.Value.Count;
                var maxHeight = 0f;
                foreach (var bonus in pair.Value)
                    maxHeight = Mathf.Max(maxHeight, BonusChipLab.ChipBounds(BonusChipLab.GameChip(bonus.Kind, ChipSeed(bonus), true)).height);
                var columns = count > 4 && player.Quota != null && pair.Key == player.Quota.Id ? 2 : 1;
                var rows = Mathf.CeilToInt(count / (float)columns);
                var pileScale = columns > 1 ? Mathf.Min(1f, (CardHeight - 42f - (rows - 1) * 3f) / (rows * maxHeight)) : 1f;
                var step = rows > 1 ? Mathf.Min(28f, (CardHeight - 42f - maxHeight * pileScale) / (rows - 1)) : 0f;
                for (var i = 0; i < count; i++)
                {
                    var bonus=pair.Value[i];
                    var bounds=BonusChipLab.ChipBounds(BonusChipLab.GameChip(bonus.Kind,ChipSeed(bonus),true));
                    var px=1f+Centered(ChipSeed(bonus)+11)*Mathf.Max(0,CardWidth/3f-bounds.width-2f);
                    px += (i % columns) * 35f;
                    var py=40f+(i / columns)*step+(columns > 1 ? 0f : Centered(ChipSeed(bonus)+17)*2f);
                    DrawCoin(host,bonus,px*cardScale,py*cardScale,cardScale*pileScale,true);
                }
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
            if (reviewMode || (acknowledgedRound == game.RoundIndex && (game.AwaitingNextRound || game.Finished))) return;
            var slides = new List<CoinSlide>();
            for (var i = 0; i < bank.Count; i++)
            {
                var coin = bank[i];
                var bounds = BonusChipLab.ChipBounds(BonusChipLab.GameChip(coin.Kind,ChipSeed(coin)));
                var px = x + Centered(coin.Serial * 2 + 1) * Mathf.Max(0f, width - bounds.width);
                var py = y + Centered(coin.Serial * 2 + 5) * Mathf.Max(0f, height - bounds.height);
                var drawn = DrawCoin(tray, coin, px, py);
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

        static int ChipSeed(BonusCoin coin) => unchecked(coin.CardId * 397 ^ (int)coin.Kind * 37 ^ coin.Index * 101);

        (RectTransform ring, RectTransform disk) DrawCoin(Transform parent, BonusCoin coin, float x, float y, float scale = 1f, bool onCard = false)
        {
            var chip=BonusChipLab.GameChip(coin.Kind,ChipSeed(coin),onCard);
            var bounds=BonusChipLab.ChipBounds(chip);
            var ring=Portrait.Rect(parent,"spot-"+coin.Key,x,y,bounds.width*scale,bounds.height*scale);
            var disk=BonusChipLab.DrawGameChip(parent,"coin",chip,x,y,scale);
            return (ring,disk);
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

        static Text Bold(Text label) { label.fontStyle = FontStyle.Bold; return label; }

        void LayQuotaCards(RectTransform area, ItemSet theme, IReadOnlyList<Card> cards, float padding)
        {
            for (var i = 0; i < cards.Count; i++)
                DrawCard(area, theme, cards[i], padding + (i == 0 ? 0f : 70f + (i - 1) * 40f), padding, 1f, null, false);
        }

        void DrawPreviousScore(Transform seat, Game game, Player player, bool wide)
        {
            if (game.RoundIndex < 2) return;
            var previous = player.Score - BaseScore(player) - GreenCount(player);
            var label = TextAt(seat, previous + "+", wide ? 224f : 32f, wide ? 98f : 260f,
                96f, 30f, 21, Ink, nameFont, TextAnchor.UpperLeft);
            label.gameObject.name = "previous-score";
            label.fontStyle = FontStyle.Bold;
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
            var height = width * 200f / 144f;
            var designScale = width / 144f;
            var host = Portrait.Rect(parent, "card" + card.Id, x, y, width, height);
            var group = host.gameObject.AddComponent<CanvasGroup>();
            group.alpha = hiddenCards.Contains(card.Id) && parent != transform ? 0f : dim ? 0.75f : 1f;
            group.blocksRaycasts = onClick != null;
            Portrait.Box(host, "face", 0f, 0f, width, height, 20f * designScale, Mathf.Max(1f, designScale), Color.white, Ink, false);
            var kind = KindIndex(card);
            var ink = IndicatorColors[kind];
            Portrait.Solid(host, "mark", 0f, (120f + kind * 10f) * designScale, 6f * designScale, 15f * designScale, ink);
            var face = theme.FaceFor(card);
            TextAt(host, theme.RankLabel(card), 10f * designScale, 9f * designScale, 112f * designScale, 42f * designScale, Mathf.RoundToInt(36f * designScale), Hex(face.Color), roundFont, TextAnchor.UpperLeft);
            var diameter = 112f * designScale;
            var iconX = 16f * designScale;
            var iconY = 44f * designScale;
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
            Baseline(host, face.Name, width * 0.5f, 180f * designScale, GoodsNameSize * designScale, Ink, roundFont, width - 8f * designScale);
            if (onClick == null) return;
            var hit = host.gameObject.AddComponent<Image>();
            hit.sprite = Portrait.White;
            hit.color = new Color(1f, 1f, 1f, 0f);
            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(onClick);
        }

        void DrawTurnCountdown(Transform seat, int index, bool wide)
        {
            var label = TextAt(seat, "", wide ? 700f : 620f, wide ? 8f : 25f, 200f, 40f, 26, Ink, nameFont, TextAnchor.MiddleRight);
            label.gameObject.name = "turn-countdown";
            label.fontStyle = FontStyle.Bold;
            UpdateTurnCountdown();
        }

        void UpdateTurnCountdown()
        {
            var game = match.Game;
            for (var i = 0; i < seatFrames.Count; i++)
            {
                if (seatFrames[i] == null) continue;
                var host = seatFrames[i].Find("turn-countdown");
                if (host == null) continue;
                var active = !onSetup && NetworkPlaying && networkState.turn_timeout_active && game != null && !game.Finished && game.Current == i;
                var remaining = networkTurnDeadline - Time.realtimeSinceStartupAsDouble;
                var label = host.GetComponent<Text>();
                label.text = active ? (int)System.Math.Ceiling(System.Math.Max(0d, remaining)) + "秒" : "";
                label.color = active && remaining <= 5f ? Hex("#D00000") : Ink;
            }
            if (frame != null && NetworkPlaying && networkState.turn_timeout_active && networkTurnDeadline <= Time.realtimeSinceStartupAsDouble)
            {
                if (game != null && !game.Finished) QueueTimeoutNotice(game.Current);
                var controls = frame.Find("controls");
                if (controls != null) controls.gameObject.SetActive(false);
                if (confirm != null && confirm != "leave")
                {
                    var dialog = frame.Find("confirm");
                    if (dialog != null) dialog.gameObject.SetActive(false);
                    foreach (var seat in seatFrames)
                        if (seat != null && seat.Find("confirm") != null) seat.Find("confirm").gameObject.SetActive(false);
                }
            }
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
                if (NetworkPlaying) { SendNetworkAction("double"); return; }
                game.DeclareDouble();
                ShowTable();
            }));
            else if (game.Plan == "double" && game.DoubleStage == 1 && !game.TurnGain) entries.Add(Item("キャンセル", () =>
            {
                confirm = null;
                if (NetworkPlaying) { SendNetworkAction("cancel_double"); return; }
                game.CancelDouble();
                ShowTable();
            }));
            if (canDeclare && me.ReshuffleTakeLeft > 0) entries.Add(Item("配り直し", () =>
            {
                confirm = null;
                if (NetworkPlaying) { SendNetworkAction("reshuffle"); return; }
                var beforeCards = BoardFrame.Capture(game);
                game.DeclareReshuffle();
                TrackCardChange(beforeCards);
                ShowTable();
            }));
            if (me.Quota != null) entries.Add(Item("放棄", () => Ask("abandon")));
            const string passLabel = "パス";
            entries.Add(Item(passLabel, () =>
            {
                if (!HasTakeable(game, me)) Play(new Pass());
                else Ask("pass");
            }));
            var edge = wide ? LandWidth - LandButtonRight : ScreenWidth;
            var y = wide ? LandMarketY + LandMarketH + 16f : 0f;
            for (var i = 0; i < entries.Count; i++)
            {
                var width = entries[i].Key.Length * 48f + 30f;
                Pill(controls, entries[i].Key, edge - width, y + i * ActionStride, width, 72f, 40, entries[i].Value, fill: Hex("#D6B98C"));
                var actionButton = controls.Find(entries[i].Key);
                foreach (var label in actionButton.GetComponentsInChildren<Text>()) label.fontStyle = FontStyle.Bold;
            }
        }

        void LeaveButton()
        {
            const string caption = "EXIT";
            const float width = 144f;
            var x = (WideScreen() ? LandWidth : ScreenWidth) - 328f;
            const float y = 8f;
            var host = transform.Find("leave-button") as RectTransform;
            if (host != null)
            {
                host.position = frame.TransformPoint(new Vector3(x - frame.rect.width * frame.pivot.x, frame.rect.height * (1f - frame.pivot.y) - y, 0f));
                host.localScale = frame.localScale;
                return;
            }
            host = Portrait.Rect(transform, "leave-button", 0f, 0f, width, 40f);
            host.position = frame.TransformPoint(new Vector3(x - frame.rect.width * frame.pivot.x, frame.rect.height * (1f - frame.pivot.y) - y, 0f));
            host.localScale = frame.localScale;
            var hit = host.gameObject.AddComponent<Image>();
            hit.sprite = Portrait.SlicedRound;
            hit.type = Image.Type.Sliced;
            hit.color = Hex("#00C900");
            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() => Ask("leave"));
            var label = TextAt(host, caption, 8f, 0f, width - 16f, 40f, 28, Color.white, nameFont, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
        }

        void RemoveLeaveButton()
        {
            var button = transform.Find("leave-button");
            if (button != null) DestroyImmediate(button.gameObject);
        }

        bool IsSpectating => NetworkPlaying && networkState.you != null
            ? networkState.you.observer || networkState.you.seat < 0
            : NoHumanSeats();

        void Ask(string kind)
        {
            if (kind == "leave" && IsSpectating)
            {
                LeaveMatch();
                return;
            }
            if (!confirmActions && (kind == "abandon" || kind == "pass"))
            {
                Play(kind == "abandon" ? (GameAction)new Abandon() : new Pass());
                return;
            }
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
            const float panelWidth = 1000f;
            const float panelHeight = 330f;
            RectTransform panel;
            if (WideScreen() || (confirm == "leave" && !hasHuman))
                panel = Portrait.Box(frame, "confirm", ((WideScreen() ? LandWidth : ScreenWidth) - panelWidth) * 0.5f, ((WideScreen() ? LandHeight : ScreenHeight) - panelHeight) * 0.5f, panelWidth, panelHeight, 7f, 1f, Hex("#FFD078"), Ink, false);
            else
            {
                var seat = seatFrames[seatIndex];
                panel = Portrait.Box(seat, "confirm", 25f + (1030f - panelWidth) * 0.5f, 25f + (340f - panelHeight) * 0.5f, panelWidth, panelHeight, 7f, 1f, Hex("#FFD078"), Ink, false);
            }
            // Seat panels are drawn in display order; keep confirmations above every seat.
            var modalCanvas = panel.gameObject.AddComponent<Canvas>();
            modalCanvas.overrideSorting = true;
            modalCanvas.sortingOrder = 100;
            panel.gameObject.AddComponent<GraphicRaycaster>();
            TextAt(panel, message, 24f, 16f, panelWidth - 48f, 80f, 34, Ink, nameFont, TextAnchor.MiddleCenter);
            Pill(panel, yes, 20f, 110f, panelWidth - 40f, 84f, 34, () =>
            {
                confirm = null;
                run();
            });
            Pill(panel, "キャンセル", 20f, 210f, panelWidth - 40f, 84f, 34, () =>
            {
                confirm = null;
                ShowTable();
            });
            foreach (var label in panel.GetComponentsInChildren<Text>()) label.fontStyle = FontStyle.Bold;
        }

        void Result(Game game, string button = "もう一局")
        {
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
            if (NetworkPlaying) { networkLeaving = true; StartCoroutine(NetworkPost("/api/leave", "{}")); }
            CleanupFlyers();
            ShowSetup();
        }

        void Play(GameAction action)
        {
            if (confirm != null || busy || CardsAnimating || !MyHumanTurn || !match.Game.IsLegal(action)) return;
            if (NetworkPlaying) { SendNetworkAction(action.Key); return; }
            confirm = null;
            var seat = match.Game.Current;
            var turn = match.Game.TurnNumber;
            var beforeCards = BoardFrame.Capture(match.Game);
            Characters.Observe(match.Game, action);
            match.Game.Step(action);
            TrackCardChange(beforeCards);
            Characters.CommitIfTurnEnded(match.Game, seat, turn);
            StartCoroutine(RunCpus(++cpuRun));
        }

        IEnumerator RunCpus(int ticket)
        {
            if (NetworkPlaying) { busy = false; ShowTable(); yield break; }
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
                var beforeCards = BoardFrame.Capture(match.Game);
                if (!match.StepOneCpu((name, key, gained) =>
                {
                    QueueNotice(match.Game.Current, key, gained);
                    if (key == "reshuffle" || key == "double")
                    {
                        TrackCardChange(beforeCards);
                        beforeCards = BoardFrame.Capture(match.Game);
                    }
                })) break;
                TrackCardChange(beforeCards);
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
            while (coinMotion > 0 || CardsAnimating) yield return null;
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
            if (NetworkPlaying && networkState.you != null) anchor = networkState.you.seat;
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

        int HumanSeat(Game game)
        {
            if (NetworkPlaying && networkState.you != null && networkState.you.seat >= 0) return networkState.you.seat;
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
            if (splashRun != null) StopCoroutine(splashRun);
            splashRun = null;
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

        void Pill(Transform parent, string caption, float x, float y, float width, float height, int size, UnityAction action, bool accent = false, Color? fill = null)
        {
            var host = Portrait.Box(parent, caption, x, y, width, height, 7f, 1f, fill ?? (accent ? Accent : Ecru), Ink, false);
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
            Portrait.ApplyGlass(image);
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
            if (setupPage == "chips")
            {
                BonusChipLab.Open(frame, screenW, screenH, wide, titleRule, nameFont, Ecru, Ink, chipLabState, () => { setupPage = null; ShowSetup(); }, EditChipColor);
                return;
            }
            if (setupPage == "tests")
            {
                var panel = Portrait.Box(frame, "implementation-tests", (screenW-800)/2, (screenH-300)/2, 800, 300, 7, 1, Paper, Ink, false);
                TextAt(panel, "実装テスト", 24, 16, 752, 60, 32, Ink, nameFont, TextAnchor.MiddleCenter);
                Pill(panel, "ボーナスチップ", 40, 100, 720, 72, 32, () => OpenPage("chips"));
                Pill(panel, "終了", 40, 200, 720, 72, 32, () => { setupPage = null; ShowSetup(); });
                return;
            }
            if (setupPage == "settings") DrawSettings(screenW, screenH);
            else DrawGuide(screenW, screenH);
        }

        void DrawSettings(float screenW, float screenH) { setupPage = null; }

        void DrawLobbyTimeout(RectTransform column, string caption, bool ok, bool leader, float width, float height, int font)
        {
            var value = (ok ? okTimeout : turnTimeout).ToString("0.##");
            if (!leader) { SetupNotice(column, caption + ": " + value, width, height, font); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
            SetupButton(column, caption + ": " + value + "　✎", () => QuotaEditTimeout(value, ok ? "ok" : "turn", gameObject.name), width, height, font);
#else
            var row = Portrait.Rect(column, caption + "-row", 0, 0, width, 88f);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width; layout.preferredHeight = 88f;
            var field = DialogField(row, caption, value, 0, 0, width);
            field.onEndEdit.AddListener(text => { if (ok) OnOkTimeoutEdited(text); else OnTurnTimeoutEdited(text); });
#endif
        }

        void SaveLobbyTimeouts()
        {
            if (NetworkJoined && (networkState.you == null || !networkState.you.leader)) return;
            SaveRules();
            if (NetworkJoined) StartCoroutine(NetworkPost("/api/settings", JsonUtility.ToJson(new NetworkCreate { ok_timeout = okTimeout, turn_timeout = turnTimeout })));
            ShowSetup();
        }

        public void OnOkTimeoutEdited(string value)
        {
            if (NetworkJoined && (networkState.you == null || !networkState.you.leader)) return;
            okTimeout = ParseSeconds(value, okTimeout, 0f);
            SaveLobbyTimeouts();
        }

        public void OnTurnTimeoutEdited(string value)
        {
            if (NetworkJoined && (networkState.you == null || !networkState.you.leader)) return;
            turnTimeout = ParseSeconds(value, turnTimeout, 1f);
            SaveLobbyTimeouts();
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
            text.raycastTarget = false;
            field.targetGraphic = host.GetComponent<Image>();
            field.contentType = InputField.ContentType.DecimalNumber;
            field.customCaretColor = true;
            field.caretColor = Ink;
            field.caretWidth = 4;
            field.caretBlinkRate = 1.2f;
            field.selectionColor = new Color(0.2f, 0.55f, 1f, 0.45f);
            var colors = field.colors;
            colors.selectedColor = new Color(0.75f, 0.9f, 1f);
            colors.highlightedColor = new Color(0.88f, 0.96f, 1f);
            field.colors = colors;
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

        void PlaceSprite(Transform parent, string name, Sprite sprite, float x, float y, float width, float height)
        {
            var host = Portrait.Rect(parent, name, x, y, width, height);
            var image = host.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
            if (name == "title-mark" || name == "title-catch")
            {
                if (logoInk == null) logoInk = new Material(Resources.Load<Shader>("Quota/LogoInk")) { hideFlags = HideFlags.HideAndDontSave };
                image.material = logoInk;
            }
        }

        void SetupButton(RectTransform parent, string caption, UnityAction action, float width, float height, int fontSize)
        {
            var go = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            var accent = caption == "新規ゲーム卓の準備" || caption == "ゲーム開始" || (parent.name == "table-list" || parent.name == "table-list-content");
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
            var fill = row.GetComponent<Image>() ?? row.gameObject.AddComponent<Image>();
            fill.sprite = Portrait.SlicedRound;
            fill.type = Image.Type.Sliced;
            fill.color = Hex("#FFF0C2");
            fill.raycastTarget = false;
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

        void SetupNotice(RectTransform parent, string caption, float width, float height, int fontSize)
        {
            var go = new GameObject("notice", typeof(RectTransform), typeof(LayoutElement), typeof(Text));
            go.transform.SetParent(parent, false);
            SizeElement(go.GetComponent<LayoutElement>(), width, height);
            var text = go.GetComponent<Text>();
            text.text = caption;
            text.font = nameFont;
            text.fontSize = fontSize;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleCenter;
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
            field.shouldHideMobileInput = false;
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

using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public sealed partial class TableView
    {
        bool settingsOpen;

        void SetupVolumeRow(RectTransform column, string caption, bool bgm, float rowW, float height, float labelW, int font)
        {
            var row = FormRow(column, caption, rowW, height, labelW, font);
            row.GetComponent<Image>().color = new Color(0.19f, 0.12f, 0.09f, 0.7f);
            var heading = row.GetComponentInChildren<Text>();
            heading.color = Color.white;
            heading.fontStyle = FontStyle.Bold;
            Shade(heading);
            var width = rowW - labelW - font - 12f;
            var field = new GameObject("volume", typeof(RectTransform), typeof(LayoutElement));
            field.transform.SetParent(row, false);
            SizeElement(field.GetComponent<LayoutElement>(), width, height);
            DrawVolumeSlider(field.GetComponent<RectTransform>(), width, height, bgm, font);
        }

        void DrawVolumeSlider(RectTransform parent, float width, float height, bool bgm, int font)
        {
            var sliderHost = Portrait.Rect(parent, bgm ? "bgm-slider" : "se-slider", 0f, 0f, width, height);
            var trackWidth = Mathf.Max(80f, width - 104f);
            var track = Portrait.Rect(sliderHost, "track", 8f, height * 0.5f - 5f, trackWidth, 10f);
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.sprite = Portrait.SlicedRound;
            trackImage.type = Image.Type.Sliced;
            trackImage.color = Hex("#E4D7C5");
            var fill = Portrait.Rect(track, "fill", 0f, 0f, trackWidth, 10f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = Accent;
            fillImage.raycastTarget = false;
            var handle = Portrait.Rect(track, "handle", 0f, -9f, 24f, 28f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = Portrait.SlicedRound;
            handleImage.type = Image.Type.Sliced;
            handleImage.color = Hex("#FFF6DD");
            var valueLabel = TextAt(sliderHost, VolumeCaption(bgm ? bgmLevel : seLevel), width - 88f, 0f, 84f, height,
                Mathf.Max(16, font - 2), Ink, nameFont, TextAnchor.MiddleCenter);
            valueLabel.fontStyle = FontStyle.Bold;
            var slider = sliderHost.gameObject.AddComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.minValue = 0f;
            slider.maxValue = 5f;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(bgm ? bgmLevel : seLevel);
            slider.onValueChanged.AddListener(value =>
            {
                var level = Mathf.RoundToInt(value);
                if (bgm)
                {
                    bgmLevel = level;
                    if (bgmActive && !bgmEnding) SetRoundBgmGain(BgmGain);
                    SetEndBgmGain(BgmGain);
                }
                else seLevel = level; // Reserved for future sound effects.
                valueLabel.text = VolumeCaption(level);
                SaveRules();
            });
        }

        static string VolumeCaption(int level) => level == 0 ? "0 (OFF)" : level.ToString();

        void SettingsButton()
        {
            const float width = 144f;
            const float y = 8f;
            var x = (WideScreen() ? LandWidth : ScreenWidth) - 484f;
            var host = transform.Find("settings-button") as RectTransform;
            if (host == null)
            {
                host = Portrait.Rect(transform, "settings-button", 0f, 0f, width, 40f);
                var image = host.gameObject.AddComponent<Image>();
                image.sprite = Portrait.SlicedRound;
                image.type = Image.Type.Sliced;
                image.color = new Color(1f, 1f, 1f, 0.9f);
                var button = host.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => { settingsOpen = true; ShowTable(); });
                var label = TextAt(host, "設定", 8f, 0f, width - 16f, 40f, 28, Color.black, nameFont, TextAnchor.MiddleCenter);
                label.fontStyle = FontStyle.Bold;
            }
            host.position = frame.TransformPoint(new Vector3(x - frame.rect.width * frame.pivot.x,
                frame.rect.height * (1f - frame.pivot.y) - y, 0f));
            host.localScale = frame.localScale;
        }

        void RemoveSettingsButton()
        {
            var button = transform.Find("settings-button");
            if (button != null) DestroyImmediate(button.gameObject);
        }

        void DrawPlayingSettings()
        {
            var screenW = WideScreen() ? LandWidth : ScreenWidth;
            var screenH = WideScreen() ? LandHeight : ScreenHeight;
            var overlay = Portrait.Rect(frame, "personal-settings", 0f, 0f, screenW, screenH);
            var wash = overlay.gameObject.AddComponent<Image>();
            wash.color = new Color(0f, 0f, 0f, 0.38f);
            var canvas = overlay.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 150;
            overlay.gameObject.AddComponent<GraphicRaycaster>();
            var outside = overlay.gameObject.AddComponent<Button>();
            outside.targetGraphic = wash;
            outside.onClick.AddListener(() => { settingsOpen = false; ShowTable(); });
            const float panelW = 740f;
            const float panelH = 470f;
            var panel = Portrait.Box(overlay, "settings-window", (screenW - panelW) * 0.5f,
                (screenH - panelH) * 0.5f, panelW, panelH, 12f, 1f, Paper, Ink, false);
            panel.GetComponent<Image>().raycastTarget = true;
            Bold(TextAt(panel, "個人設定", 24f, 16f, panelW - 48f, 52f, 34, Ink, nameFont, TextAnchor.MiddleCenter));
            Bold(TextAt(panel, "放棄などに確認を求める：", 32f, 84f, 500f, 62f, 26, Ink, nameFont, TextAnchor.MiddleLeft));
            Pill(panel, confirmActions ? "YES" : "NO", 548f, 88f, 160f, 54f, 28, () =>
            {
                confirmActions = !confirmActions;
                SaveRules();
                ShowTable();
            });
            Bold(TextAt(panel, "BGM音量：", 32f, 164f, 260f, 60f, 28, Ink, nameFont, TextAnchor.MiddleLeft));
            var bgmField = Portrait.Rect(panel, "bgm-volume", 294f, 164f, 414f, 60f);
            DrawVolumeSlider(bgmField, 414f, 60f, true, 26);
            Bold(TextAt(panel, "SE音量：", 32f, 244f, 260f, 60f, 28, Ink, nameFont, TextAnchor.MiddleLeft));
            var seField = Portrait.Rect(panel, "se-volume", 294f, 244f, 414f, 60f);
            DrawVolumeSlider(seField, 414f, 60f, false, 26);
            Pill(panel, "OK", 20f, 366f, panelW - 40f, 78f, 32, () => { settingsOpen = false; ShowTable(); });
        }
    }
}

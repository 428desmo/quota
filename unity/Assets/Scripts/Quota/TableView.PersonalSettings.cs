using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public sealed partial class TableView
    {
        bool settingsOpen;

        void DrawPersonalSettingsRows(RectTransform rows, float rowW, float height, int font)
        {
            var labelW = LabelSlot(font, "放棄などに確認を求める：");
            SetupSegmentedRow(rows, "放棄などに確認を求める：", new[] { "YES", "NO" },
                confirmActions ? "YES" : "NO", rowW, height, labelW, font, true, SetPersonalConfirmation);
            SetupGap(rows, 16f);
            var volumes = new[] { "OFF", "1", "2", "3", "4", "5" };
            SetupSegmentedRow(rows, "BGM音量：", volumes, bgmLevel == 0 ? "OFF" : bgmLevel.ToString(),
                rowW, height, labelW, font, true, value => SetPersonalVolume(true, value));
            SetupGap(rows, 16f);
            SetupSegmentedRow(rows, "SE音量：", volumes, seLevel == 0 ? "OFF" : seLevel.ToString(),
                rowW, height, labelW, font, true, value => SetPersonalVolume(false, value));
        }

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
            const float panelW = 840f;
            const float panelH = 450f;
            var panel = Portrait.Box(overlay, "settings-window", (screenW - panelW) * 0.5f,
                (screenH - panelH) * 0.5f, panelW, panelH, 12f, 1f, Paper, Ink, false);
            panel.GetComponent<Image>().raycastTarget = true;
            Bold(TextAt(panel, "個人設定", 24f, 16f, panelW - 48f, 52f, 34, Ink, nameFont, TextAnchor.MiddleCenter));
            var rows = Portrait.Rect(panel, "settings-list", 24f, 84f, panelW - 48f, 250f);
            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            DrawPersonalSettingsRows(rows, panelW - 48f, 64f, 26);
            Pill(panel, "OK", 24f, panelH - 90f, panelW - 48f, 66f, 28,
                () => { settingsOpen = false; ShowTable(); });
        }
    }
}

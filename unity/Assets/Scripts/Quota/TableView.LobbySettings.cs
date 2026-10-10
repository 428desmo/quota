using UnityEngine;
using UnityEngine.UI;

namespace Quota
{
    public sealed partial class TableView
    {
        void OpenLobbySettings(string page)
        {
            lobbySettingsPage = page;
            ShowSetup();
        }

        void CloseLobbySettings()
        {
            lobbySettingsPage = null;
            ShowSetup();
        }

        void DrawLobbySettings(float screenW, float screenH)
        {
            var personal = lobbySettingsPage == "personal";
            if (!personal && gameMode == GameMode.Offline) return;
            var overlay = Portrait.Rect(frame, "lobby-settings", 0f, 0f, screenW, screenH);
            var wash = overlay.gameObject.AddComponent<Image>();
            wash.color = new Color(0f, 0f, 0f, 0.55f);
            var canvas = overlay.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 140;
            overlay.gameObject.AddComponent<GraphicRaycaster>();
            var outside = overlay.gameObject.AddComponent<Button>();
            outside.targetGraphic = wash;
            outside.onClick.AddListener(CloseLobbySettings);

            var panelW = Mathf.Min(screenW - 40f, 840f);
            var panelH = personal ? 450f : 360f;
            var rowW = panelW - 48f;
            var panel = Portrait.Box(overlay, "settings-window", (screenW - panelW) * 0.5f,
                (screenH - panelH) * 0.5f, panelW, panelH, 12f, 1f, Paper, Ink, false);
            panel.GetComponent<Image>().raycastTarget = true;
            Bold(TextAt(panel, personal ? "個人設定" : "対戦設定", 24f, 16f, rowW, 52f,
                34, Ink, nameFont, TextAnchor.MiddleCenter));
            var rows = Portrait.Rect(panel, "settings-list", 24f, 84f, rowW, personal ? 250f : 165f);
            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            if (personal) DrawPersonalSettingsRows(rows, rowW, 64f, 26);
            else
            {
                var leader = !NetworkJoined || (networkState.you != null && networkState.you.leader);
                var labelW = LabelSlot(26, "手番タイムアウト（秒）：", "OKタイムアウト（秒）：");
                SetupSegmentedRow(rows, "OKタイムアウト（秒）：", new[] { "1", "2", "3", "4", "5" },
                    okTimeout.ToString("0"), rowW, 64f, labelW, 26, leader,
                    value => ChooseInlineLobbyChoice("ok", value));
                SetupGap(rows, 16f);
                SetupSegmentedRow(rows, "手番タイムアウト（秒）：", new[] { "5", "10", "15", "20", "25", "30" },
                    turnTimeout.ToString("0"), rowW, 64f, labelW, 26, leader,
                    value => ChooseInlineLobbyChoice("turn", value));
            }
            Pill(panel, "OK", 24f, panelH - 90f, rowW, 66f, 28, CloseLobbySettings);
        }
    }
}

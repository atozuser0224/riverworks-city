using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Session-only resident AI controls, built inside the existing HUD canvas.</summary>
    public sealed class ResidentAiPanel : MonoBehaviour
    {
        static readonly Color Navy = Hex("101820");
        static readonly Color Navy2 = Hex("253442");
        static readonly Color Paper = Hex("1B2731");
        static readonly Color Cream = Hex("DCE6EB");
        static readonly Color Muted = Hex("8FA1AC");
        static readonly Color Teal = Hex("4A9F98");
        static readonly Color Coral = Hex("C89B55");

        GameController controller;
        ResidentAiClient client;
        GameObject overlay;
        Text statusText;
        Text metricsText;
        Text explanationText;
        Text enableLabel;
        Text pauseLabel;
        InputField addressInput;
        InputField gatewayTokenInput;
        Toggle enableToggle;
        Button pauseButton;
        Font font;
        float refreshAt;
        float resumeSpeed = 1f;
        bool open;

        public bool IsOpen => open;

        public void Initialize(GameController game, ResidentAiClient residentAi, Transform hudCanvas)
        {
            controller = game;
            client = residentAi;
            if (controller == null || client == null || hudCanvas == null) return;
            font = GameFont.Load();
            Build(hudCanvas);
            client.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (client != null) client.Changed -= Refresh;
            if (open && controller != null) controller.ModalOpen = false;
        }

        void Update()
        {
            if (!open || controller == null || client == null) return;
            if (!controller.ModalOpen)
            {
                open = false;
                if (overlay != null) overlay.SetActive(false);
                ClearTokenField();
                return;
            }
            if (Time.unscaledTime >= refreshAt)
            {
                refreshAt = Time.unscaledTime + .25f;
                Refresh();
            }
        }

        public void Show()
        {
            if (overlay == null || controller == null) return;
            if (controller.HelpOpen) controller.ToggleHelp();
            if (controller.ResearchOpen) controller.ToggleResearch();
            if (controller.CityHud != null) controller.CityHud.CloseTransientPanels();
            open = true;
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling();
            controller.ModalOpen = true;
            addressInput.text = client.GatewayUrl;
            ClearTokenField();
            refreshAt = 0f;
            Refresh();
        }

        public void Hide()
        {
            if (!open) return;
            open = false;
            if (overlay != null) overlay.SetActive(false);
            ClearTokenField();
            if (controller != null) controller.ModalOpen = false;
        }

        void Build(Transform parent)
        {
            overlay = Surface("ResidentAiOverlay", parent, new Color(0f, 0f, 0f, .62f));
            Stretch((RectTransform)overlay.transform, Vector2.zero, Vector2.zero);

            GameObject card = Surface("ResidentAiCard", overlay.transform, Paper);
            RectTransform cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(.5f, .5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(680f, 420f);
            FeelUiFeedback.AttachPanel(card);

            Text title = Label("주민 AI", card.transform, 20, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(title.rectTransform, new Vector2(24f, -12f), new Vector2(540f, 44f));
            Button close = MakeButton("×", card.transform, new Vector2(612f, -12f), new Vector2(44f, 44f), Navy2, Hide, 22);
            close.name = "Button_ResidentAiClose";

            statusText = Label("", card.transform, 14, Cream, FontStyle.Bold, TextAnchor.UpperLeft);
            statusText.supportRichText = false;
            Place(statusText.rectTransform, new Vector2(24f, -60f), new Vector2(632f, 48f));

            metricsText = Label("", card.transform, 12, Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            metricsText.supportRichText = false;
            Place(metricsText.rectTransform, new Vector2(24f, -112f), new Vector2(632f, 66f));

            explanationText = Label("주민 이름·활동·도시 자원은 허구의 게임 데이터입니다. 생각과 기억은 저장 파일에 남지 않으며 이 실행 세션에서만 사용됩니다.",
                card.transform, 11, Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            explanationText.supportRichText = false;
            Place(explanationText.rectTransform, new Vector2(24f, -180f), new Vector2(632f, 34f));

            Text addressLabel = Label("게이트웨이", card.transform, 12, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(addressLabel.rectTransform, new Vector2(24f, -218f), new Vector2(110f, 42f));
            addressInput = MakeInput("ResidentAiGatewayAddress", card.transform, new Vector2(136f, -218f), new Vector2(520f, 42f),
                "Windows 로컬 기본값: http://127.0.0.1:47841", false, 300);

            Text tokenLabel = Label("접근 토큰", card.transform, 12, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(tokenLabel.rectTransform, new Vector2(24f, -266f), new Vector2(110f, 42f));
            gatewayTokenInput = MakeInput("ResidentAiGatewayToken", card.transform, new Vector2(136f, -266f), new Vector2(520f, 42f),
                "원격 게이트웨이에 필요할 때만 입력 · 저장하지 않음", true, 512);

            enableToggle = MakeToggle(card.transform, new Vector2(24f, -316f), new Vector2(156f, 44f));
            enableToggle.name = "Toggle_ResidentAiEnabled";
            enableToggle.onValueChanged.AddListener(client.SetEnabled);

            Button apply = MakeButton("연결 설정", card.transform, new Vector2(188f, -316f), new Vector2(108f, 44f), Navy2, ApplySettings, 12);
            apply.name = "Button_ResidentAiConnect";
            Button test = MakeButton("연결 테스트", card.transform, new Vector2(304f, -316f), new Vector2(112f, 44f), Teal, TestConnection, 12);
            test.name = "Button_ResidentAiTest";
            pauseButton = MakeButton("", card.transform, new Vector2(424f, -316f), new Vector2(112f, 44f), Coral, ToggleGamePause, 12);
            pauseButton.name = "Button_ResidentAiPause";
            pauseLabel = pauseButton.GetComponentInChildren<Text>();
            Button clear = MakeButton("세션 기억 지우기", card.transform, new Vector2(544f, -316f), new Vector2(112f, 44f), Navy2, ClearMemory, 11);
            clear.name = "Button_ResidentAiClearMemory";

            Text footer = Label("클라우드 제공자 키는 게이트웨이 서버에만 보관됩니다.", card.transform, 11, Muted, FontStyle.Normal, TextAnchor.MiddleLeft);
            footer.supportRichText = false;
            Place(footer.rectTransform, new Vector2(24f, -368f), new Vector2(632f, 34f));

            overlay.SetActive(false);
        }

        void ApplySettings()
        {
            string reason;
            if (!client.TrySetGatewayUrl(addressInput.text, out reason)) { Refresh(); return; }
            if (!string.IsNullOrEmpty(gatewayTokenInput.text) && !client.SetGatewayAccessToken(gatewayTokenInput.text)) { ClearTokenField(); Refresh(); return; }
            ClearTokenField();
            Refresh();
        }

        void TestConnection()
        {
            string reason;
            if (!client.TrySetGatewayUrl(addressInput.text, out reason)) { Refresh(); return; }
            if (!string.IsNullOrEmpty(gatewayTokenInput.text) && !client.SetGatewayAccessToken(gatewayTokenInput.text)) { ClearTokenField(); Refresh(); return; }
            ClearTokenField();
            client.RequestHealthNow();
            Refresh();
        }

        void ToggleGamePause()
        {
            if (controller.GameSpeed > 0f)
            {
                resumeSpeed = controller.GameSpeed;
                controller.SetSpeed(0f);
            }
            else controller.SetSpeed(resumeSpeed <= 0f ? 1f : resumeSpeed);
            Refresh();
        }

        void ClearMemory()
        {
            client.ClearSessionMemory();
            Refresh();
        }

        void Refresh()
        {
            if (client == null) return;
            string source = client.CurrentNetworkMode == ResidentAiNetworkMode.LLM ? "LLM"
                : client.CurrentNetworkMode == ResidentAiNetworkMode.CouldNotConnect ? "연결 안 됨" : "규칙 기반";
            string readiness = client.GatewayReady ? "준비됨" : "준비 안 됨";
            if (statusText != null) statusText.text = source + " · " + client.Status + "\n" + client.StatusReason;

            string model = string.IsNullOrEmpty(client.Model) ? "확인 전" : client.Model;
            string lastSource = string.IsNullOrEmpty(client.LastResponseSource) ? "없음" : client.LastResponseSource;
            string lastRequest = string.IsNullOrEmpty(client.LastResponseRequestId) ? "없음" : client.LastResponseRequestId;
            string latency = client.LastResponseLatencyMilliseconds <= 0f ? "없음" : client.LastResponseLatencyMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms";
            string usage = string.IsNullOrEmpty(client.LastResponseSource) ? "없음" :
                client.LastPromptTokens + "/" + client.LastCompletionTokens + " 토큰 · 예상 $" +
                client.LastEstimatedUsd.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            if (metricsText != null)
                metricsText.text = "게이트웨이 " + readiness + " · 모델 " + model + "\n최근/누적 적용 " + client.LastAppliedDecisionCount + "/" + client.AppliedDecisionCount +
                    " · 실패 요청 " + client.FailedRequestCount + " · 마지막 출처 " + lastSource + " · 지연 " + latency +
                    "\n마지막 사용량 " + usage + "\n마지막 요청 ID " + lastRequest;

            if (enableToggle != null)
            {
                enableToggle.SetIsOnWithoutNotify(client.EnabledForSession);
                if (enableLabel != null) enableLabel.text = client.EnabledForSession ? "주민 AI 켜짐" : "주민 AI 꺼짐";
            }
            if (pauseLabel != null && controller != null) pauseLabel.text = controller.GameSpeed > 0f ? "게임 일시정지" : "게임 계속";
            if (addressInput != null && !addressInput.isFocused) addressInput.text = client.GatewayUrl;
        }

        void ClearTokenField()
        {
            if (gatewayTokenInput != null) gatewayTokenInput.text = "";
        }

        Toggle MakeToggle(Transform parent, Vector2 position, Vector2 size)
        {
            GameObject root = Surface("ResidentAiEnabledToggle", parent, Navy2);
            Place((RectTransform)root.transform, position, size);
            Toggle toggle = root.AddComponent<Toggle>();
            Image background = root.GetComponent<Image>();
            background.sprite = HudAssets.Button;
            background.type = Image.Type.Sliced;
            toggle.targetGraphic = background;

            GameObject mark = Surface("Checkmark", root.transform, Teal);
            RectTransform markRect = (RectTransform)mark.transform;
            markRect.anchorMin = markRect.anchorMax = markRect.pivot = new Vector2(0f, .5f);
            markRect.anchoredPosition = new Vector2(10f, 0f);
            markRect.sizeDelta = new Vector2(24f, 24f);
            toggle.graphic = mark.GetComponent<Image>();

            enableLabel = Label("주민 AI 켜짐", root.transform, 12, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
            RectTransform labelRect = enableLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(44f, 0f);
            labelRect.offsetMax = new Vector2(-6f, 0f);
            return toggle;
        }

        InputField MakeInput(string name, Transform parent, Vector2 position, Vector2 size, string hint, bool password, int limit)
        {
            GameObject root = Surface(name, parent, Cream);
            Place((RectTransform)root.transform, position, size);
            Image image = root.GetComponent<Image>();
            image.sprite = HudAssets.Button;
            image.type = Image.Type.Sliced;

            Text value = Label("", root.transform, 12, Navy, FontStyle.Normal, TextAnchor.MiddleLeft);
            value.supportRichText = false;
            RectTransform valueRect = value.rectTransform;
            valueRect.anchorMin = Vector2.zero;
            valueRect.anchorMax = Vector2.one;
            valueRect.offsetMin = new Vector2(12f, 4f);
            valueRect.offsetMax = new Vector2(-12f, -4f);

            Text placeholder = Label(hint, root.transform, 11, new Color(Navy.r, Navy.g, Navy.b, .58f), FontStyle.Italic, TextAnchor.MiddleLeft);
            placeholder.supportRichText = false;
            RectTransform placeholderRect = placeholder.rectTransform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(12f, 4f);
            placeholderRect.offsetMax = new Vector2(-12f, -4f);

            InputField input = root.AddComponent<InputField>();
            input.textComponent = value;
            input.placeholder = placeholder;
            input.targetGraphic = image;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = limit;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            input.asteriskChar = '•';
            return input;
        }

        Button MakeButton(string value, Transform parent, Vector2 position, Vector2 size, Color color, UnityEngine.Events.UnityAction action, int textSize)
        {
            GameObject root = Surface("Button_" + value, parent, color);
            Place((RectTransform)root.transform, position, size);
            Image image = root.GetComponent<Image>();
            image.sprite = HudAssets.Button;
            image.type = Image.Type.Sliced;
            Button button = root.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(.82f, .82f, .82f, 1f);
            colors.disabledColor = new Color(.56f, .56f, .56f, .7f);
            button.colors = colors;
            button.onClick.AddListener(action);
            Text label = Label(value, root.transform, textSize, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 2f);
            labelRect.offsetMax = new Vector2(-4f, -2f);
            FeelUiFeedback.AttachButton(button);
            return button;
        }

        Text Label(string value, Transform parent, int size, Color color, FontStyle style, TextAnchor alignment)
        {
            GameObject root = new GameObject("Text", typeof(RectTransform), typeof(Text));
            root.transform.SetParent(parent, false);
            Text text = root.GetComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        static GameObject Surface(string name, Transform parent, Color color)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            Image image = root.GetComponent<Image>();
            image.sprite = HudAssets.Panel;
            image.type = Image.Type.Sliced;
            image.color = color;
            return root;
        }

        static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = minimum;
            rect.offsetMax = maximum;
        }

        static Color Hex(string value)
        {
            Color color;
            ColorUtility.TryParseHtmlString("#" + value, out color);
            return color;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Session-only resident AI controls, built inside the existing HUD canvas.</summary>
    public sealed class ResidentAiPanel : MonoBehaviour
    {
        const float CardWidth = 680f;
        const float CardHeight = 520f;
        const float CardMargin = 24f;

        GameController controller;
        ResidentAiClient client;
        GameObject overlay;
        RectTransform cardRect;
        RectTransform contentRect;
        ScrollRect contentScroll;
        readonly RectTransform[] actionRects = new RectTransform[5];
        RectTransform footerRect;
        Text statusText;
        Text metricsText;
        Text explanationText;
        Text enableLabel;
        Text pauseLabel;
        InputField addressInput;
        InputField gatewayTokenInput;
        Toggle enableToggle;
        Image enableBackground;
        Button pauseButton;
        Font font;
        float refreshAt;
        float resumeSpeed = 1f;
        bool open;
        Vector2 lastOverlaySize = new Vector2(-1f, -1f);

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

            ClampCardToOverlay();
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
            ClampCardToOverlay(true);
            if (contentScroll != null) contentScroll.verticalNormalizedPosition = 1f;
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
            overlay = Surface("ResidentAiOverlay", parent, HudStyle.Dim);
            Stretch((RectTransform)overlay.transform, Vector2.zero, Vector2.zero);

            GameObject card = Surface("ResidentAiCard", overlay.transform, HudStyle.Surface);
            cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(.5f, .5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            FeelUiFeedback.AttachPanel(card);

            Text title = Label("주민 AI", card.transform, HudStyle.TitleSize, HudStyle.Text, TextAnchor.MiddleLeft);
            PlaceHorizontal(title.rectTransform, 24f, 80f, -12f, HudStyle.TouchSize);
            Button close = MakeIconButton(card.transform, new Vector2(-24f, -12f), HudAssets.CloseIcon, Hide);
            close.name = "Button_ResidentAiClose";

            GameObject viewport = Surface("ResidentAiContentViewport", card.transform, Color.clear);
            RectTransform viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(0f, 12f);
            viewportRect.offsetMax = new Vector2(0f, -64f);
            viewport.AddComponent<RectMask2D>();
            contentScroll = viewport.AddComponent<ScrollRect>();
            contentScroll.viewport = viewportRect;
            contentScroll.horizontal = false;
            contentScroll.vertical = true;
            contentScroll.movementType = ScrollRect.MovementType.Clamped;
            contentScroll.scrollSensitivity = 22f;

            GameObject content = new GameObject("ResidentAiContent", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 440f);
            contentScroll.content = contentRect;

            statusText = Label("", content.transform, HudStyle.BodySize, HudStyle.Text, TextAnchor.UpperLeft);
            statusText.supportRichText = false;
            PlaceHorizontal(statusText.rectTransform, 24f, 24f, -4f, 44f);

            metricsText = Label("", content.transform, HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.UpperLeft);
            metricsText.supportRichText = false;
            PlaceHorizontal(metricsText.rectTransform, 24f, 24f, -52f, 88f);

            explanationText = Label(
                "주민 이름·활동·도시 자원은 허구의 게임 데이터입니다. 생각과 기억은 저장 파일에 남지 않으며 이 실행 세션에서만 사용됩니다.",
                content.transform, HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.UpperLeft);
            explanationText.supportRichText = false;
            PlaceHorizontal(explanationText.rectTransform, 24f, 24f, -144f, 34f);

            Text addressLabel = Label("게이트웨이", content.transform, HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.MiddleLeft);
            PlaceHorizontal(addressLabel.rectTransform, 24f, 24f, -182f, 22f);
            addressInput = MakeInput(
                "ResidentAiGatewayAddress", content.transform, 24f, 24f, -208f,
                "Windows 로컬 기본값: http://127.0.0.1:47841", false, 300);

            Text tokenLabel = Label("접근 토큰", content.transform, HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.MiddleLeft);
            PlaceHorizontal(tokenLabel.rectTransform, 24f, 24f, -260f, 22f);
            gatewayTokenInput = MakeInput(
                "ResidentAiGatewayToken", content.transform, 24f, 24f, -286f,
                "원격 게이트웨이에 필요할 때만 입력 · 저장하지 않음", true, 512);

            enableToggle = MakeToggle(content.transform, 0, 5, -342f);
            enableToggle.name = "Toggle_ResidentAiEnabled";
            enableToggle.onValueChanged.AddListener(client.SetEnabled);
            actionRects[0] = (RectTransform)enableToggle.transform;

            Button apply = MakeButton("연결 설정", content.transform, 1, 5, -342f, HudStyle.SurfaceRaised, ApplySettings);
            apply.name = "Button_ResidentAiConnect";
            actionRects[1] = (RectTransform)apply.transform;
            Button test = MakeButton("연결 테스트", content.transform, 2, 5, -342f, HudStyle.Positive, TestConnection);
            test.name = "Button_ResidentAiTest";
            actionRects[2] = (RectTransform)test.transform;
            pauseButton = MakeButton("", content.transform, 3, 5, -342f, HudStyle.SurfaceRaised, ToggleGamePause);
            pauseButton.name = "Button_ResidentAiPause";
            actionRects[3] = (RectTransform)pauseButton.transform;
            pauseLabel = pauseButton.GetComponentInChildren<Text>();
            Button clear = MakeButton("세션 기억 지우기", content.transform, 4, 5, -342f, HudStyle.SurfaceRaised, ClearMemory);
            clear.name = "Button_ResidentAiClearMemory";
            actionRects[4] = (RectTransform)clear.transform;

            Text footer = Label("클라우드 제공자 키는 게이트웨이 서버에만 보관됩니다.", content.transform,
                HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.MiddleLeft);
            footer.supportRichText = false;
            footerRect = footer.rectTransform;
            PlaceHorizontal(footerRect, 24f, 24f, -394f, 34f);

            ClampCardToOverlay(true);
            overlay.SetActive(false);
        }

        void ApplySettings()
        {
            string reason;
            if (!client.TrySetGatewayUrl(addressInput.text, out reason)) { Refresh(); return; }
            if (!string.IsNullOrEmpty(gatewayTokenInput.text) && !client.SetGatewayAccessToken(gatewayTokenInput.text))
            {
                ClearTokenField();
                Refresh();
                return;
            }
            ClearTokenField();
            Refresh();
        }

        void TestConnection()
        {
            string reason;
            if (!client.TrySetGatewayUrl(addressInput.text, out reason)) { Refresh(); return; }
            if (!string.IsNullOrEmpty(gatewayTokenInput.text) && !client.SetGatewayAccessToken(gatewayTokenInput.text))
            {
                ClearTokenField();
                Refresh();
                return;
            }
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
            if (statusText != null)
            {
                string status = client.Status ?? "";
                string summary = status.IndexOf(source, StringComparison.OrdinalIgnoreCase) >= 0
                    ? status
                    : string.IsNullOrEmpty(status) ? source : source + " · " + status;
                statusText.text = string.IsNullOrEmpty(client.StatusReason)
                    ? summary
                    : summary + "\n" + client.StatusReason;
            }

            string model = string.IsNullOrEmpty(client.Model) ? "확인 전" : client.Model;
            string lastSource = string.IsNullOrEmpty(client.LastResponseSource) ? "없음" : client.LastResponseSource;
            string lastRequest = string.IsNullOrEmpty(client.LastResponseRequestId) ? "없음" : client.LastResponseRequestId;
            string latency = client.LastResponseLatencyMilliseconds <= 0f ? "없음" :
                client.LastResponseLatencyMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms";
            string usage = string.IsNullOrEmpty(client.LastResponseSource) ? "없음" :
                client.LastPromptTokens + "/" + client.LastCompletionTokens + " 토큰 · 예상 $" +
                client.LastEstimatedUsd.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            if (metricsText != null)
                metricsText.text = "게이트웨이 " + readiness + " · 모델 " + model +
                    "\n최근/누적 적용 " + client.LastAppliedDecisionCount + "/" + client.AppliedDecisionCount +
                    " · 실패 요청 " + client.FailedRequestCount + " · 마지막 출처 " + lastSource + " · 지연 " + latency +
                    "\n마지막 사용량 " + usage + "\n마지막 요청 ID " + lastRequest;

            if (enableToggle != null)
            {
                enableToggle.SetIsOnWithoutNotify(client.EnabledForSession);
                if (enableLabel != null) enableLabel.text = client.EnabledForSession ? "주민 AI 켜짐" : "주민 AI 꺼짐";
                if (enableBackground != null)
                    enableBackground.color = client.EnabledForSession ? HudStyle.Positive : HudStyle.SurfaceRaised;
            }
            if (pauseLabel != null && controller != null)
                pauseLabel.text = controller.GameSpeed > 0f ? "게임 일시정지" : "게임 계속";
            if (addressInput != null && !addressInput.isFocused) addressInput.text = client.GatewayUrl;
        }

        void ClearTokenField()
        {
            if (gatewayTokenInput != null) gatewayTokenInput.text = "";
        }

        void ClampCardToOverlay(bool force = false)
        {
            if (overlay == null || cardRect == null) return;
            RectTransform overlayRect = (RectTransform)overlay.transform;
            Vector2 available = overlayRect.rect.size;
            if (available.x <= 0f || available.y <= 0f || (!force && available == lastOverlaySize)) return;
            lastOverlaySize = available;
            cardRect.sizeDelta = new Vector2(
                Mathf.Max(0f, Mathf.Min(CardWidth, available.x - CardMargin * 2f)),
                Mathf.Max(0f, Mathf.Min(CardHeight, available.y - CardMargin * 2f)));
            cardRect.anchoredPosition = Vector2.zero;
            ConfigureContentLayout(cardRect.sizeDelta.x);
        }

        void ConfigureContentLayout(float cardWidth)
        {
            if (contentRect == null || actionRects[0] == null || footerRect == null) return;
            if (cardWidth >= 600f)
            {
                for (int i = 0; i < actionRects.Length; i++) PlaceInRow(actionRects[i], i, 5, -342f);
                PlaceHorizontal(footerRect, 24f, 24f, -394f, 34f);
                contentRect.sizeDelta = new Vector2(0f, 440f);
                return;
            }

            PlaceInRow(actionRects[0], 0, 2, -342f);
            PlaceInRow(actionRects[1], 1, 2, -342f);
            PlaceInRow(actionRects[2], 0, 2, -394f);
            PlaceInRow(actionRects[3], 1, 2, -394f);
            PlaceHorizontal(actionRects[4], 24f, 24f, -446f, HudStyle.TouchSize);
            PlaceHorizontal(footerRect, 24f, 24f, -498f, 34f);
            contentRect.sizeDelta = new Vector2(0f, 544f);
        }

        Toggle MakeToggle(Transform parent, int index, int count, float top)
        {
            GameObject root = Surface("ResidentAiEnabledToggle", parent, HudStyle.SurfaceRaised);
            PlaceInRow((RectTransform)root.transform, index, count, top);
            Toggle toggle = root.AddComponent<Toggle>();
            enableBackground = root.GetComponent<Image>();
            enableBackground.sprite = HudAssets.Button;
            enableBackground.type = Image.Type.Sliced;
            toggle.targetGraphic = enableBackground;

            GameObject mark = Surface("Checkmark", root.transform, HudStyle.Foreground(HudStyle.Positive));
            RectTransform markRect = (RectTransform)mark.transform;
            markRect.anchorMin = markRect.anchorMax = markRect.pivot = new Vector2(0f, .5f);
            markRect.anchoredPosition = new Vector2(10f, 0f);
            markRect.sizeDelta = new Vector2(24f, 24f);
            toggle.graphic = mark.GetComponent<Image>();

            enableLabel = Label("주민 AI 켜짐", root.transform, HudStyle.BodySize,
                HudStyle.Foreground(HudStyle.SurfaceRaised), TextAnchor.MiddleLeft);
            RectTransform labelRect = enableLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(42f, 0f);
            labelRect.offsetMax = new Vector2(-4f, 0f);
            return toggle;
        }

        InputField MakeInput(string name, Transform parent, float left, float right, float top, string hint, bool password, int limit)
        {
            GameObject root = Surface(name, parent, HudStyle.Text);
            PlaceHorizontal((RectTransform)root.transform, left, right, top, HudStyle.TouchSize);
            Image image = root.GetComponent<Image>();
            image.sprite = HudAssets.Button;
            image.type = Image.Type.Sliced;

            Color foreground = HudStyle.Foreground(HudStyle.Text);
            Text value = Label("", root.transform, HudStyle.BodySize, foreground, TextAnchor.MiddleLeft);
            value.supportRichText = false;
            StretchText(value.rectTransform, 12f);

            Color placeholderColor = new Color(foreground.r, foreground.g, foreground.b, .58f);
            Text placeholder = Label(hint, root.transform, HudStyle.BodySize, placeholderColor, TextAnchor.MiddleLeft);
            placeholder.supportRichText = false;
            StretchText(placeholder.rectTransform, 12f);

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

        Button MakeButton(string value, Transform parent, int index, int count, float top, Color color,
            UnityEngine.Events.UnityAction action)
        {
            GameObject root = Surface("Button_" + value, parent, color);
            PlaceInRow((RectTransform)root.transform, index, count, top);
            Button button = ConfigureButton(root, action);
            Text label = Label(value, root.transform, HudStyle.BodySize, HudStyle.Foreground(color), TextAnchor.MiddleCenter);
            StretchText(label.rectTransform, 4f);
            FeelUiFeedback.AttachButton(button);
            return button;
        }

        Button MakeIconButton(Transform parent, Vector2 position, string iconName, UnityEngine.Events.UnityAction action)
        {
            GameObject root = Surface("Button_Icon", parent, HudStyle.SurfaceRaised);
            RectTransform rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(HudStyle.TouchSize, HudStyle.TouchSize);
            root.GetComponent<Image>().sprite = HudAssets.IconButton;
            Button button = ConfigureButton(root, action);

            GameObject iconRoot = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconRoot.transform.SetParent(root.transform, false);
            RectTransform iconRect = (RectTransform)iconRoot.transform;
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(.5f, .5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(22f, 22f);
            Image icon = iconRoot.GetComponent<Image>();
            icon.sprite = HudAssets.Icon(iconName);
            icon.color = HudStyle.Foreground(HudStyle.SurfaceRaised);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            FeelUiFeedback.AttachButton(button);
            return button;
        }

        static Button ConfigureButton(GameObject root, UnityEngine.Events.UnityAction action)
        {
            Image image = root.GetComponent<Image>();
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
            return button;
        }

        Text Label(string value, Transform parent, int size, Color color, TextAnchor alignment)
        {
            GameObject root = new GameObject("Text", typeof(RectTransform), typeof(Text));
            root.transform.SetParent(parent, false);
            Text text = root.GetComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = FontStyle.Normal;
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

        static void PlaceHorizontal(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2((left - right) * .5f, top);
            rect.sizeDelta = new Vector2(-(left + right), height);
        }

        static void PlaceInRow(RectTransform rect, int index, int count, float top)
        {
            const float left = 24f;
            const float right = 24f;
            const float gap = 8f;
            float start = (float)index / count;
            float end = (float)(index + 1) / count;
            rect.anchorMin = new Vector2(start, 1f);
            rect.anchorMax = new Vector2(end, 1f);
            rect.pivot = new Vector2(.5f, .5f);
            float totalGaps = gap * (count - 1);
            float minimum = left * (1f - start) - right * start - totalGaps * start + gap * index;
            float maximum = left * (1f - end) - right * end - totalGaps * end + gap * index;
            rect.offsetMin = new Vector2(minimum, top - HudStyle.TouchSize);
            rect.offsetMax = new Vector2(maximum, top);
        }

        static void StretchText(RectTransform rect, float horizontalInset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalInset, 2f);
            rect.offsetMax = new Vector2(-horizontalInset, -2f);
        }

        static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = minimum;
            rect.offsetMax = maximum;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Four-slot, data-only editor for conditional factory automation.</summary>
    public sealed class AutomationRulePanel : MonoBehaviour
    {
        const int SlotCount = 4;
        const float WideBreakpoint = 720f;

        sealed class RuleDraft
        {
            public int Id;
            public int SourceEntityId;
            public Resource Resource = Resource.Timber;
            public AutomationComparison Comparison = AutomationComparison.AtLeast;
            public AutomationAction Action = AutomationAction.AllowWhenTrue;
            public int Threshold;
            public bool Enabled = true;
        }

        GameController game;
        Action closed;
        RectTransform window;
        RectTransform viewport;
        RectTransform content;
        ScrollRect scroll;
        GameObject pickerOverlay;
        Text pickerTitle;
        RectTransform pickerViewport;
        RectTransform pickerContent;
        Text title;
        Text targetSummary;
        Text notice;
        FactoryState boundFactory;
        int targetId;
        bool initialized;
        bool stale;
        bool layoutRebuildPending;
        bool rulesReloadPending;
        readonly bool[] dirtySlots = new bool[SlotCount];
        int lastRulesFingerprint;
        Vector2 lastRootSize = new Vector2(-1f, -1f);
        readonly List<RuleDraft> drafts = new List<RuleDraft>(SlotCount);

        public bool IsOpen => initialized && gameObject.activeSelf;

        public void Initialize(GameController controller, Action onClosed = null)
        {
            if (initialized) throw new InvalidOperationException("Automation rule panel is already initialized.");
            game = controller ?? throw new ArgumentNullException(nameof(controller));
            closed = onClosed;
            initialized = true;
            Build();
            gameObject.SetActive(false);
        }

        public void Open(int selectedTargetId)
        {
            if (!initialized) return;
            targetId = selectedTargetId;
            boundFactory = game.State == null ? null : game.State.Factory;
            stale = false;
            layoutRebuildPending = false;
            rulesReloadPending = false;
            Array.Clear(dirtySlots, 0, dirtySlots.Length);
            notice.text = "";
            game.ModalOpen = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            LoadDrafts();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            if (!initialized) return;
            gameObject.SetActive(false);
            boundFactory = null;
            targetId = 0;
            drafts.Clear();
            layoutRebuildPending = false;
            rulesReloadPending = false;
            Array.Clear(dirtySlots, 0, dirtySlots.Length);
            lastRulesFingerprint = 0;
            if (game != null) game.ModalOpen = false;
            closed?.Invoke();
        }

        void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (pickerOverlay != null && pickerOverlay.activeSelf) ClosePicker();
                else Close();
                return;
            }
            if (game == null || !game.ModalOpen)
            {
                gameObject.SetActive(false);
                boundFactory = null;
                drafts.Clear();
                return;
            }

            RectTransform root = transform as RectTransform;
            if (root != null && root.rect.size != lastRootSize)
            {
                ApplyLayout();
                if (TextInputFocused()) layoutRebuildPending = true;
                else { layoutRebuildPending = false; RebuildSlots(); }
            }
            if (!stale && !HasCurrentTarget()) MarkStale();
            if (!TextInputFocused())
            {
                if (rulesReloadPending && !HasUnsavedDraftChanges)
                {
                    rulesReloadPending = false;
                    LoadDrafts();
                }
                else if (layoutRebuildPending)
                {
                    layoutRebuildPending = false;
                    RebuildSlots();
                }
            }
        }

        public void Refresh()
        {
            if (!initialized || !IsOpen) return;
            if (!HasCurrentTarget()) { MarkStale(); return; }
            stale = false;
            UpdateTargetSummary();
            int fingerprint = RulesFingerprint();
            if (fingerprint == lastRulesFingerprint) return;
            if (TextInputFocused() || HasUnsavedDraftChanges) rulesReloadPending = true;
            else LoadDrafts();
        }

        void Build()
        {
            RectTransform root = transform as RectTransform;
            if (root == null) throw new InvalidOperationException("AutomationRulePanel requires a RectTransform host.");
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            Image dim = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            dim.color = HudStyle.Dim;
            dim.raycastTarget = true;

            window = ResearchUi.Panel("AutomationRuleWindow", root, HudStyle.SurfaceRaised);
            title = ResearchUi.Label("AutomationRuleTitle", window, "조건부 자동화", HudStyle.TitleSize,
                HudStyle.Text, TextAnchor.MiddleLeft);
            Button close = IconButton("Button_AutomationRuleClose", window, HudAssets.CloseIcon, Close);
            targetSummary = ResearchUi.Label("AutomationTargetSummary", window, "", HudStyle.BodySize,
                HudStyle.TextMuted, TextAnchor.UpperLeft);
            notice = ResearchUi.Label("AutomationNotice", window, "", HudStyle.BodySize,
                HudStyle.TextMuted, TextAnchor.MiddleLeft);

            viewport = ResearchUi.Panel("AutomationRuleViewport", window, Color.clear);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = ResearchUi.Rect("AutomationRuleContent", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;
            BuildPicker();
            ApplyLayout();
        }

        void ApplyLayout()
        {
            if (window == null) return;
            RectTransform root = transform as RectTransform;
            float width = Mathf.Max(0f, Mathf.Min(980f, root.rect.width - 32f));
            float height = Mathf.Max(0f, Mathf.Min(760f, root.rect.height - 32f));
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero;
            window.sizeDelta = new Vector2(width, height);
            ResearchUi.Place(title.rectTransform, 24f, 12f, Mathf.Max(120f, width - 104f), HudStyle.TouchSize);
            ResearchUi.Place(window.Find("Button_AutomationRuleClose") as RectTransform,
                width - 68f, 12f, HudStyle.TouchSize, HudStyle.TouchSize);
            ResearchUi.Place(targetSummary.rectTransform, 24f, 62f, width - 48f, 46f);
            ResearchUi.Place(notice.rectTransform, 24f, 108f, width - 48f, 34f);
            ResearchUi.Place(viewport, 24f, 146f, width - 48f, Mathf.Max(44f, height - 166f));
            if (pickerOverlay != null)
            {
                RectTransform pickerRect = pickerOverlay.transform as RectTransform;
                ResearchUi.Place(pickerRect, 16f, 64f, width - 32f, height - 80f);
                ResearchUi.Place(pickerTitle.rectTransform, 16f, 8f, Mathf.Max(80f, width - 112f), HudStyle.TouchSize);
                ResearchUi.Place(pickerRect.Find("Button_AutomationPickerClose") as RectTransform,
                    Mathf.Max(16f, width - 92f), 8f, HudStyle.TouchSize, HudStyle.TouchSize);
                ResearchUi.Place(pickerViewport, 16f, 60f, width - 64f, Mathf.Max(44f, height - 156f));
            }
            lastRootSize = root.rect.size;
        }

        void BuildPicker()
        {
            RectTransform picker = ResearchUi.Panel("AutomationPicker", window, HudStyle.SurfaceRaised);
            pickerOverlay = picker.gameObject;
            pickerTitle = ResearchUi.Label("AutomationPickerTitle", picker, "선택", HudStyle.TitleSize,
                HudStyle.Text, TextAnchor.MiddleLeft);
            IconButton("Button_AutomationPickerClose", picker, HudAssets.CloseIcon, ClosePicker);
            pickerViewport = ResearchUi.Panel("AutomationPickerViewport", picker, Color.clear);
            pickerViewport.gameObject.AddComponent<RectMask2D>();
            pickerContent = ResearchUi.Rect("AutomationPickerContent", pickerViewport);
            pickerContent.anchorMin = new Vector2(0f, 1f);
            pickerContent.anchorMax = new Vector2(1f, 1f);
            pickerContent.pivot = new Vector2(.5f, 1f);
            pickerContent.anchoredPosition = Vector2.zero;
            ScrollRect pickerScroll = pickerViewport.gameObject.AddComponent<ScrollRect>();
            pickerScroll.viewport = pickerViewport;
            pickerScroll.content = pickerContent;
            pickerScroll.horizontal = false;
            pickerScroll.vertical = true;
            pickerScroll.movementType = ScrollRect.MovementType.Clamped;
            pickerScroll.scrollSensitivity = 34f;
            pickerOverlay.SetActive(false);
        }

        void LoadDrafts()
        {
            drafts.Clear();
            FactoryState state = boundFactory;
            if (state != null && state.AutomationRules != null)
            {
                foreach (AutomationRule rule in state.AutomationRules
                    .Where(rule => rule != null && rule.TargetEntityId == targetId)
                    .OrderBy(rule => rule.Id).Take(SlotCount))
                {
                    drafts.Add(new RuleDraft
                    {
                        Id = rule.Id,
                        SourceEntityId = rule.SourceEntityId,
                        Resource = rule.Resource,
                        Comparison = rule.Comparison,
                        Action = rule.Action,
                        Threshold = rule.Threshold,
                        Enabled = rule.Enabled
                    });
                }
            }
            while (drafts.Count < SlotCount) drafts.Add(NewDraft());
            lastRulesFingerprint = RulesFingerprint();
            rulesReloadPending = false;
            layoutRebuildPending = false;
            Array.Clear(dirtySlots, 0, dirtySlots.Length);
            RebuildSlots();
        }

        RuleDraft NewDraft()
        {
            List<Resource> resources = ResourcesFor(0);
            return new RuleDraft { Resource = resources.Count == 0 ? Resource.Timber : resources[0] };
        }

        void RebuildSlots()
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            if (stale)
            {
                targetSummary.text = "대상 설비 또는 도시 상태가 바뀌었습니다.";
                AddStaleMessage();
                return;
            }
            FactoryEntity target = CurrentTarget();
            if (target == null)
            {
                targetSummary.text = "대상 설비를 찾을 수 없습니다.";
                AddStaleMessage();
                return;
            }
            UpdateTargetSummary();

            bool wide = window.rect.width >= WideBreakpoint;
            float y = 0f;
            for (int i = 0; i < SlotCount; i++)
            {
                float height = wide ? 154f : 432f;
                BuildSlot(i, y, height, wide);
                y += height + 10f;
            }
            content.sizeDelta = new Vector2(0f, Mathf.Max(1f, y));
        }

        void BuildSlot(int slotIndex, float top, float height, bool wide)
        {
            RuleDraft draft = drafts[slotIndex];
            RectTransform card = ResearchUi.Panel("AutomationRuleSlot_" + slotIndex, content, HudStyle.Surface);
            ResearchUi.Place(card, 0f, top, 100f, height);
            StretchWidth(card, 0f, 0f);

            Toggle enabled = MakeToggle("Toggle_AutomationRuleEnabled_" + slotIndex, card,
                draft.Enabled, draft.Id == 0 ? "슬롯 " + (slotIndex + 1) + " · 새 규칙 활성" :
                    "슬롯 " + (slotIndex + 1) + " · 규칙 #" + draft.Id + " 활성",
                value => { dirtySlots[slotIndex] = true; draft.Enabled = value; });
            Button save = ResearchUi.Button("Button_AutomationRuleSave_" + slotIndex, card, "저장",
                () => SaveSlot(slotIndex), HudStyle.Positive);
            Button remove = ResearchUi.Button("Button_AutomationRuleDelete_" + slotIndex, card,
                draft.Id == 0 ? "초기화" : "삭제", () => RemoveSlot(slotIndex),
                draft.Id == 0 ? HudStyle.SurfaceRaised : HudStyle.Danger);

            if (wide)
            {
                ResearchUi.Place(enabled.transform as RectTransform, 12f, 8f, 180f, HudStyle.TouchSize);
                AnchorRight(remove.transform as RectTransform, 12f, 8f, 82f, HudStyle.TouchSize);
                AnchorRight(save.transform as RectTransform, 102f, 8f, 82f, HudStyle.TouchSize);
                BuildFieldRow(slotIndex, card, 12f, 58f, card.rect.width - 24f, true);
            }
            else
            {
                ResearchUi.Place(enabled.transform as RectTransform, 12f, 8f, Mathf.Max(44f, card.rect.width - 24f), HudStyle.TouchSize);
                float half = Mathf.Max(44f, (card.rect.width - 32f) * .5f);
                ResearchUi.Place(save.transform as RectTransform, 12f, 60f, half, HudStyle.TouchSize);
                ResearchUi.Place(remove.transform as RectTransform, 20f + half, 60f, half, HudStyle.TouchSize);
                BuildFieldRow(slotIndex, card, 12f, 112f, card.rect.width - 24f, false);
            }
        }

        void BuildFieldRow(int slotIndex, RectTransform card, float left, float top, float width, bool wide)
        {
            RuleDraft draft = drafts[slotIndex];
            List<FactoryEntity> sources = SourceEntities();
            List<Resource> resources = ResourcesFor(draft.SourceEntityId);

            if (wide)
            {
                float gap = 8f;
                float sourceW = width * .29f;
                float resourceW = width * .21f;
                float compareW = 74f;
                float thresholdW = 104f;
                float actionW = Mathf.Max(104f, width - sourceW - resourceW - compareW - thresholdW - gap * 4f);
                AddField("출처", SourceText(draft.SourceEntityId), "Button_AutomationRuleSource_" + slotIndex,
                    card, left, top, sourceW, () => OpenSourcePicker(slotIndex, sources));
                left += sourceW + gap;
                AddField("자원", ResourceText(draft.Resource), "Button_AutomationRuleResource_" + slotIndex,
                    card, left, top, resourceW, () => OpenResourcePicker(slotIndex, resources));
                left += resourceW + gap;
                AddField("비교", ComparisonText(draft.Comparison), "Button_AutomationRuleComparison_" + slotIndex,
                    card, left, top, compareW, () => ToggleComparison(slotIndex));
                left += compareW + gap;
                AddThreshold(slotIndex, card, left, top, thresholdW);
                left += thresholdW + gap;
                AddField("참일 때", ActionText(draft.Action), "Button_AutomationRuleAction_" + slotIndex,
                    card, left, top, actionW, () => ToggleAction(slotIndex));
                return;
            }

            AddField("출처", SourceText(draft.SourceEntityId), "Button_AutomationRuleSource_" + slotIndex,
                card, left, top, width, () => OpenSourcePicker(slotIndex, sources));
            AddField("자원", ResourceText(draft.Resource), "Button_AutomationRuleResource_" + slotIndex,
                card, left, top + 62f, width, () => OpenResourcePicker(slotIndex, resources));
            AddField("비교", ComparisonText(draft.Comparison), "Button_AutomationRuleComparison_" + slotIndex,
                card, left, top + 124f, width, () => ToggleComparison(slotIndex));
            AddThreshold(slotIndex, card, left, top + 186f, width);
            AddField("참일 때", ActionText(draft.Action), "Button_AutomationRuleAction_" + slotIndex,
                card, left, top + 248f, width, () => ToggleAction(slotIndex));
        }

        void AddField(string labelText, string value, string buttonName, RectTransform parent,
            float x, float top, float width, Action click)
        {
            Text label = ResearchUi.Label(buttonName + "Label", parent, labelText, HudStyle.BodySize,
                HudStyle.TextMuted, TextAnchor.MiddleLeft);
            ResearchUi.Place(label.rectTransform, x, top, width, 18f);
            Button button = ResearchUi.Button(buttonName, parent, value + "  ▾", click, HudStyle.SurfaceRaised);
            ResearchUi.Place(button.transform as RectTransform, x, top + 20f, width, HudStyle.TouchSize);
        }

        void AddThreshold(int slotIndex, RectTransform parent, float x, float top, float width)
        {
            Text label = ResearchUi.Label("AutomationRuleThresholdLabel_" + slotIndex, parent, "기준값 (정수)",
                HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.MiddleLeft);
            ResearchUi.Place(label.rectTransform, x, top, width, 18f);
            InputField input = MakeIntegerInput("Input_AutomationRuleThreshold_" + slotIndex, parent,
                drafts[slotIndex].Threshold.ToString(), value =>
                {
                    dirtySlots[slotIndex] = true;
                    if (string.IsNullOrEmpty(value)) drafts[slotIndex].Threshold = 0;
                    else if (int.TryParse(value, out int parsed)) drafts[slotIndex].Threshold = Mathf.Clamp(parsed, 0, 1000000);
                });
            ResearchUi.Place(input.transform as RectTransform, x, top + 20f, width, HudStyle.TouchSize);
        }

        void OpenSourcePicker(int slotIndex, List<FactoryEntity> sources)
        {
            OpenPicker("슬롯 " + (slotIndex + 1) + " · 측정 출처", new[] { 0 }.Concat(sources.Select(entity => entity.Id)),
                SourceText, sourceId =>
                {
                    RuleDraft draft = drafts[slotIndex];
                    dirtySlots[slotIndex] = true;
                    draft.SourceEntityId = sourceId;
                    List<Resource> resources = ResourcesFor(sourceId);
                    if (!resources.Contains(draft.Resource) && resources.Count > 0) draft.Resource = resources[0];
                    ClosePicker();
                    RebuildSlots();
                });
        }

        void OpenResourcePicker(int slotIndex, List<Resource> resources)
        {
            if (resources.Count == 0)
            {
                notice.color = HudStyle.Danger;
                notice.text = "선택한 출처에서 측정할 수 있는 자원이 없습니다.";
                return;
            }
            OpenPicker("슬롯 " + (slotIndex + 1) + " · 측정 자원", resources, ResourceText, resource =>
            {
                dirtySlots[slotIndex] = true;
                drafts[slotIndex].Resource = resource;
                ClosePicker();
                RebuildSlots();
            });
        }

        void OpenPicker<T>(string heading, IEnumerable<T> values, Func<T, string> label, Action<T> selected)
        {
            if (pickerOverlay == null || pickerContent == null) return;
            for (int i = pickerContent.childCount - 1; i >= 0; i--) Destroy(pickerContent.GetChild(i).gameObject);
            pickerTitle.text = heading;
            float y = 0f;
            int index = 0;
            foreach (T item in values)
            {
                T captured = item;
                Button choice = ResearchUi.Button("Button_AutomationPickerChoice_" + index++, pickerContent,
                    label(captured), () => selected(captured), HudStyle.Surface);
                ResearchUi.Place(choice.transform as RectTransform, 0f, y, 100f, HudStyle.TouchSize);
                StretchWidth(choice.transform as RectTransform, 0f, 0f);
                y += HudStyle.TouchSize + 8f;
            }
            pickerContent.sizeDelta = new Vector2(0f, Mathf.Max(1f, y));
            pickerOverlay.SetActive(true);
            pickerOverlay.transform.SetAsLastSibling();
        }

        void ClosePicker()
        {
            if (pickerOverlay != null) pickerOverlay.SetActive(false);
        }

        void ToggleComparison(int slotIndex)
        {
            RuleDraft draft = drafts[slotIndex];
            dirtySlots[slotIndex] = true;
            draft.Comparison = draft.Comparison == AutomationComparison.AtLeast
                ? AutomationComparison.AtMost : AutomationComparison.AtLeast;
            RebuildSlots();
        }

        void ToggleAction(int slotIndex)
        {
            RuleDraft draft = drafts[slotIndex];
            dirtySlots[slotIndex] = true;
            draft.Action = draft.Action == AutomationAction.AllowWhenTrue
                ? AutomationAction.StopWhenTrue : AutomationAction.AllowWhenTrue;
            RebuildSlots();
        }

        void SaveSlot(int slotIndex)
        {
            if (!HasCurrentTarget()) { MarkStale(); return; }
            RuleDraft draft = drafts[slotIndex];
            HashSet<int> idsBefore = boundFactory.AutomationRules == null ? new HashSet<int>() :
                new HashSet<int>(boundFactory.AutomationRules.Where(value => value != null).Select(value => value.Id));
            draft.Threshold = Mathf.Clamp(draft.Threshold, 0, 1000000);
            var rule = new AutomationRule
            {
                Id = draft.Id,
                SourceEntityId = draft.SourceEntityId,
                TargetEntityId = targetId,
                Resource = draft.Resource,
                Comparison = draft.Comparison,
                Action = draft.Action,
                Threshold = draft.Threshold,
                Enabled = draft.Enabled
            };
            string reason = "공장 제어기를 사용할 수 없습니다.";
            bool okay = game.Factory != null && game.Factory.SaveAutomationRule(rule, out reason);
            notice.color = okay ? HudStyle.Positive : HudStyle.Danger;
            notice.text = okay ? "자동화 규칙을 저장했습니다." : reason;
            if (okay)
            {
                AutomationRule saved = boundFactory.AutomationRules.FirstOrDefault(value => value != null &&
                    value.TargetEntityId == targetId && (rule.Id != 0 ? value.Id == rule.Id : !idsBefore.Contains(value.Id)));
                if (saved != null) CopyRuleToDraft(saved, draft);
                dirtySlots[slotIndex] = false;
                lastRulesFingerprint = RulesFingerprint();
                rulesReloadPending = false;
                RebuildSlots();
            }
        }

        void RemoveSlot(int slotIndex)
        {
            RuleDraft draft = drafts[slotIndex];
            if (draft.Id == 0)
            {
                drafts[slotIndex] = NewDraft();
                dirtySlots[slotIndex] = false;
                notice.color = HudStyle.TextMuted;
                notice.text = "새 규칙 입력을 초기화했습니다.";
                RebuildSlots();
                return;
            }
            if (!HasCurrentTarget()) { MarkStale(); return; }
            string reason = "공장 제어기를 사용할 수 없습니다.";
            bool okay = game.Factory != null && game.Factory.RemoveAutomationRule(draft.Id, out reason);
            notice.color = okay ? HudStyle.Positive : HudStyle.Danger;
            notice.text = okay ? "자동화 규칙을 삭제했습니다. 설치된 제어기는 설비에 남습니다." : reason;
            if (okay)
            {
                drafts[slotIndex] = NewDraft();
                dirtySlots[slotIndex] = false;
                lastRulesFingerprint = RulesFingerprint();
                rulesReloadPending = false;
                RebuildSlots();
            }
        }

        static void CopyRuleToDraft(AutomationRule rule, RuleDraft draft)
        {
            draft.Id = rule.Id;
            draft.SourceEntityId = rule.SourceEntityId;
            draft.Resource = rule.Resource;
            draft.Comparison = rule.Comparison;
            draft.Action = rule.Action;
            draft.Threshold = rule.Threshold;
            draft.Enabled = rule.Enabled;
        }

        bool HasCurrentTarget()
        {
            return game != null && game.State != null && ReferenceEquals(boundFactory, game.State.Factory) &&
                CurrentTarget() != null;
        }

        FactoryEntity CurrentTarget()
        {
            return boundFactory == null || boundFactory.Entities == null ? null :
                boundFactory.Entities.FirstOrDefault(entity => entity != null && entity.Id == targetId &&
                    entity.Kind != FactoryKind.None && FactoryCatalog.Get(entity.Kind) != null &&
                    ((entity.Kind != FactoryKind.ItemLift && entity.Kind != FactoryKind.FluidRiser) || entity.IsLinkSender));
        }

        void UpdateTargetSummary()
        {
            FactoryEntity target = CurrentTarget();
            if (target == null || targetSummary == null) return;
            FactorySpec spec = FactoryCatalog.Get(target.Kind);
            string targetName = spec == null ? target.Kind.ToString() : spec.Name;
            string running = target.Paused ? "수동 일시정지 중 · 자동화보다 우선"
                : target.AutomationBlocked ? "자동 조건 대기 중" : "가동 허용";
            string controller = target.ControllerInstalled ? "제어기 설치됨" : ControllerGateText();
            targetSummary.text = targetName + " #" + target.Id + " · " + (target.Floor + 1) + "층 · " + running +
                "\n" + controller + " · 수동 일시정지는 모든 자동 규칙보다 항상 우선합니다.";
        }

        int RulesFingerprint()
        {
            if (boundFactory == null || boundFactory.AutomationRules == null) return 0;
            unchecked
            {
                int hash = 17;
                foreach (AutomationRule rule in boundFactory.AutomationRules
                    .Where(value => value != null && value.TargetEntityId == targetId).OrderBy(value => value.Id))
                {
                    hash = hash * 31 + rule.Id;
                    hash = hash * 31 + rule.SourceEntityId;
                    hash = hash * 31 + rule.TargetEntityId;
                    hash = hash * 31 + (int)rule.Resource;
                    hash = hash * 31 + (int)rule.Comparison;
                    hash = hash * 31 + (int)rule.Action;
                    hash = hash * 31 + rule.Threshold;
                    hash = hash * 31 + (rule.Enabled ? 1 : 0);
                }
                return hash;
            }
        }

        bool TextInputFocused()
        {
            return game != null && game.UiTextInputFocused;
        }

        bool HasUnsavedDraftChanges => dirtySlots.Any(value => value);

        void MarkStale()
        {
            stale = true;
            notice.color = HudStyle.Danger;
            notice.text = "대상 설비 또는 도시 상태가 바뀌었습니다. 이 창을 닫고 현재 설비에서 다시 여세요.";
            RebuildSlots();
        }

        void AddStaleMessage()
        {
            Text message = ResearchUi.Label("AutomationRuleStale", content,
                "이 편집기는 이전 대상 상태를 저장하지 않습니다. 창을 닫고 설비를 다시 선택하세요.",
                HudStyle.BodySize, HudStyle.Danger, TextAnchor.MiddleCenter);
            ResearchUi.Place(message.rectTransform, 12f, 12f, 100f, 88f);
            StretchWidth(message.rectTransform, 12f, 12f);
            content.sizeDelta = new Vector2(0f, 112f);
        }

        List<FactoryEntity> SourceEntities()
        {
            return boundFactory == null || boundFactory.Entities == null ? new List<FactoryEntity>() :
                boundFactory.Entities.Where(entity => entity != null).OrderBy(entity => entity.Id).ToList();
        }

        List<Resource> ResourcesFor(int sourceId)
        {
            IEnumerable<ResourceSpec> resources = ResourceCatalog.All.Where(spec => spec.Id != Resource.Coins);
            if (sourceId == 0) return resources.Select(spec => spec.Id).ToList();
            FactoryEntity source = boundFactory == null ? null : boundFactory.Entities.FirstOrDefault(entity => entity != null && entity.Id == sourceId);
            if (source == null) return new List<Resource>();
            bool fluid = FactoryCatalog.IsFluidTransport(source.Kind) || source.Kind == FactoryKind.WaterPump ||
                source.Kind == FactoryKind.OilPump || source.Kind == FactoryKind.Refinery || source.Kind == FactoryKind.ChemicalPlant;
            bool solid = !FactoryCatalog.IsFluidTransport(source.Kind) && source.Kind != FactoryKind.WaterPump && source.Kind != FactoryKind.OilPump;
            return resources.Where(spec => (spec.IsFluid && fluid) || (!spec.IsFluid && solid)).Select(spec => spec.Id).ToList();
        }

        string SourceText(int sourceId)
        {
            if (sourceId == 0) return "도시 재고";
            FactoryEntity source = boundFactory == null ? null : boundFactory.Entities.FirstOrDefault(entity => entity != null && entity.Id == sourceId);
            if (source == null) return "누락된 설비 #" + sourceId;
            FactorySpec spec = FactoryCatalog.Get(source.Kind);
            return (spec == null ? source.Kind.ToString() : spec.Name) + " #" + source.Id + " · " + (source.Floor + 1) + "층";
        }

        string ControllerGateText()
        {
            bool tech = TechCatalog.Has(game.State, TechId.IndustrialControl);
            int units = game.State.Stock != null && game.State.Stock.Count > (int)Resource.ControlUnit
                ? Mathf.FloorToInt(game.State.Stock[(int)Resource.ControlUnit]) : 0;
            return "첫 규칙: 제어 장치 1개 (보유 " + units + ") · 산업 제어 " + (tech ? "연구됨" : "연구 필요");
        }

        static string ResourceText(Resource resource)
        {
            ResourceSpec spec = ResourceCatalog.Get(resource);
            return spec == null ? resource.ToString() : spec.Name + " (" + (spec.IsFluid ? "유체" : "고체") + ")";
        }

        static string ComparisonText(AutomationComparison comparison) =>
            comparison == AutomationComparison.AtLeast ? "≥ 이상" : "≤ 이하";

        static string ActionText(AutomationAction action) =>
            action == AutomationAction.AllowWhenTrue ? "조건 참이면 허용" : "조건 참이면 정지";

        static Toggle MakeToggle(string name, Transform parent, bool value, string text, Action<bool> changed)
        {
            RectTransform root = ResearchUi.Panel(name, parent, value ? HudStyle.Positive : HudStyle.SurfaceRaised);
            Toggle toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = root.GetComponent<Image>();
            RectTransform mark = ResearchUi.Panel("Checkmark", root, HudStyle.Foreground(HudStyle.Positive));
            ResearchUi.Place(mark, 10f, 10f, 24f, 24f);
            toggle.graphic = mark.GetComponent<Image>();
            Text label = ResearchUi.Label("Label", root, text, HudStyle.BodySize,
                HudStyle.Foreground(value ? HudStyle.Positive : HudStyle.SurfaceRaised), TextAnchor.MiddleLeft);
            ResearchUi.Stretch(label.rectTransform, 42f, 2f, 6f, 2f);
            toggle.SetIsOnWithoutNotify(value);
            toggle.onValueChanged.AddListener(next =>
            {
                Color background = next ? HudStyle.Positive : HudStyle.SurfaceRaised;
                root.GetComponent<Image>().color = background;
                label.color = HudStyle.Foreground(background);
                changed?.Invoke(next);
            });
            return toggle;
        }

        static InputField MakeIntegerInput(string name, Transform parent, string value, Action<string> changed)
        {
            RectTransform root = ResearchUi.Panel(name, parent, HudStyle.SurfaceRaised);
            InputField input = root.gameObject.AddComponent<InputField>();
            Text text = ResearchUi.Label("Text", root, value, HudStyle.BodySize, HudStyle.Text, TextAnchor.MiddleLeft);
            ResearchUi.Stretch(text.rectTransform, 12f, 2f, 12f, 2f);
            Text placeholder = ResearchUi.Label("Placeholder", root, "0–1,000,000", HudStyle.BodySize,
                HudStyle.TextMuted, TextAnchor.MiddleLeft);
            ResearchUi.Stretch(placeholder.rectTransform, 12f, 2f, 12f, 2f);
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = value;
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = 7;
            input.lineType = InputField.LineType.SingleLine;
            input.onValueChanged.AddListener(next => changed?.Invoke(next));
            return input;
        }

        static Button IconButton(string name, Transform parent, string iconName, Action click)
        {
            RectTransform root = ResearchUi.Panel(name, parent, HudStyle.Surface);
            root.GetComponent<Image>().sprite = HudAssets.IconButton;
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.onClick.AddListener(() => click?.Invoke());
            RectTransform icon = ResearchUi.Panel("Icon", root, HudStyle.Foreground(HudStyle.Surface));
            icon.GetComponent<Image>().sprite = HudAssets.Icon(iconName);
            icon.GetComponent<Image>().preserveAspect = true;
            icon.GetComponent<Image>().raycastTarget = false;
            ResearchUi.Place(icon, 11f, 11f, 22f, 22f);
            FeelUiFeedback.AttachButton(button);
            return button;
        }

        static void AnchorRight(RectTransform rect, float right, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-right, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        static void StretchWidth(RectTransform rect, float left, float right)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2((left - right) * .5f, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(-(left + right), rect.sizeDelta.y);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>
    /// Runtime-built, non-modal dialogue for village clerk Danwoo.
    /// It lives on the existing city HUD canvas and only raycasts inside its own cards.
    /// </summary>
    public sealed class TutorialDialogue : MonoBehaviour
    {
        const float CharactersPerSecond = 28f;
        const float MaximumBlipsPerSecond = 18f;
        const int MaximumPageElements = 110;
        const int PreferredBreakFloor = 72;
        static readonly float[] VoicePitches = { .92f, .98f, 1.04f, 1.09f, .96f };

        readonly List<int> textElementEnds = new List<int>(256);
        readonly List<string> textElements = new List<string>(256);
        readonly List<string> pages = new List<string>(8);
        readonly List<RectTransform> topicButtons = new List<RectTransform>(5);
        readonly List<RectTransform> lessonButtons = new List<RectTransform>(24);
        readonly TextGenerator pageLayoutGenerator = new TextGenerator();

        TutorialDirector director;
        Transform visualParent;
        GameObject expandedRoot;
        GameObject collapsedRoot;
        GameObject introSkipRoot;
        GameObject topicRow;
        GameObject lessonPicker;
        GameObject morePopup;
        RectTransform bodyHitRect;
        RectTransform expandedRect;
        RectTransform topicRect;
        RectTransform lessonPickerRect;
        RectTransform lessonContentRect;
        RectTransform morePopupRect;
        RectTransform goalRect;
        RectTransform advanceRect;
        RectTransform moreRect;
        Text speakerText;
        Text titleText;
        Text bodyText;
        Text goalText;
        Text advanceText;
        Text pageIndicatorText;
        Text collapsedText;
        Button advanceButton;
        Button skipButton;
        Button restartButton;
        Button guidanceLessonsButton;
        Button nextLessonButton;
        Button followGuideButton;
        Text guidanceToggleText;
        Font font;
        AudioSource voiceSource;
        AudioClip voiceClip;

        string fullText = "";
        string contentSignature = "";
        string displayedText = "";
        int pageIndex;
        int revealedCharacters;
        float nextCharacterDelay;
        float lastBlipAt = float.NegativeInfinity;
        int blipSequence;
        bool typing;
        bool lastObscured;
        bool lessonPickerOpen;
        bool morePopupOpen;
        bool lastConstructionBarVisible;
        bool lastAdviceMode;
        float lastConstructionBarHeight = -1f;
        int lastScreenWidth = -1;
        int lastScreenHeight = -1;
        Rect lastSafeArea;
        Vector2 lastSafeSize;

        public bool Typing => typing;
        public bool Visible =>
            (expandedRoot != null && expandedRoot.activeInHierarchy) ||
            (collapsedRoot != null && collapsedRoot.activeInHierarchy);
        public string DisplayedText => displayedText;
        public string FullText => fullText;
        public string CurrentPageText => pages.Count == 0 ? "" : pages[pageIndex];
        public int RevealedCharacters => revealedCharacters;
        public bool CurrentPageFits => PageFits(CurrentPageText);
        public bool ActualPageFits => PageFits(CurrentPageText);
        /// <summary>Zero-based index of the page currently presented.</summary>
        public int PageIndex => pageIndex;
        public int PageCount => pages.Count;
        /// <summary>True while the compact overflow menu is visible. Used by smoke diagnostics.</summary>
        public bool DialogMoreVisible => morePopup != null && morePopup.activeInHierarchy;
        /// <summary>True while the guidance lesson picker is visible. Used by smoke diagnostics.</summary>
        public bool DialogLessonPickerVisible => lessonPicker != null && lessonPicker.activeInHierarchy;

        /// <summary>Safe to call more than once; rebuilds only when the HUD parent changes.</summary>
        public void Initialize(TutorialDirector value, Transform parent)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            Transform safeArea = parent.Find("CitySafeArea");
            Transform resolvedParent = safeArea != null ? safeArea : parent;

            if (director != null) director.Changed -= OnDirectorChanged;
            director = value;

            if (expandedRoot == null || visualParent != resolvedParent)
            {
                DestroyVisuals();
                visualParent = resolvedParent;
                BuildInterface();
            }

            director.Changed += OnDirectorChanged;
            Refresh(true);
        }

        void Update()
        {
            if (director == null) return;

            ApplyResponsiveLayout(false);

            bool obscured = IsObscured();
            if (obscured != lastObscured) Refresh(false);
            if (obscured) return;
            if (expandedRoot == null || !expandedRoot.activeInHierarchy) return;

            if (typing) AdvanceTyping(Time.unscaledDeltaTime);
            HandleKeyboardShortcut();
        }

        void OnDestroy()
        {
            if (director != null) director.Changed -= OnDirectorChanged;
            StopVoice();
            DestroyVisuals();
            if (voiceClip != null)
            {
                if (Application.isPlaying) Destroy(voiceClip);
                else DestroyImmediate(voiceClip);
            }
            DestroyOwnedObject(voiceSource);
        }

        void OnDisable()
        {
            StopVoice();
        }

        void OnDirectorChanged()
        {
            Refresh(false);
        }

        void BuildInterface()
        {
            font = GameFont.Load();
            EnsureVoice();
            topicButtons.Clear();
            lessonButtons.Clear();

            expandedRoot = Surface("TutorialDialogue", visualParent, HudStyle.Surface, new Vector2(.5f, 0f),
                new Vector2(0f, 68f), new Vector2(680f, 174f));
            expandedRect = expandedRoot.GetComponent<RectTransform>();
            FeelUiFeedback.AttachPanel(expandedRoot);

            speakerText = Label("단우", expandedRoot.transform, HudStyle.BodySize, HudStyle.Accent, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Vector2(14f, -8f), new Vector2(64f, 28f));

            titleText = Label("", expandedRoot.transform, HudStyle.TitleSize, HudStyle.Text, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Vector2(84f, -8f), new Vector2(470f, 28f));
            pageIndicatorText = Label("", expandedRoot.transform, HudStyle.BodySize, HudStyle.TextMuted, FontStyle.Normal,
                TextAnchor.MiddleRight, new Vector2(570f, -8f), new Vector2(92f, 28f));

            GameObject bodyHit = Surface("TutorialDialogueBody", expandedRoot.transform,
                HudStyle.SurfaceRaised, new Vector2(0f, 1f),
                new Vector2(14f, -42f), new Vector2(652f, 76f));
            bodyHitRect = bodyHit.GetComponent<RectTransform>();
            Button bodyButton = bodyHit.AddComponent<Button>();
            bodyButton.targetGraphic = bodyHit.GetComponent<Image>();
            bodyButton.transition = Selectable.Transition.None;
            bodyButton.onClick.AddListener(OnAdvance);
            FeelUiFeedback.AttachButton(bodyButton);
            bodyText = Label("", bodyHit.transform, HudStyle.BodySize, HudStyle.Text, FontStyle.Normal,
                TextAnchor.UpperLeft, new Vector2(10f, -5f), new Vector2(632f, 66f));
            bodyText.resizeTextForBestFit = false;

            goalText = Label("", expandedRoot.transform, HudStyle.BodySize, HudStyle.TextMuted, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Vector2(144f, -124f), new Vector2(282f, 44f));
            goalRect = goalText.rectTransform;

            Button collapse = MakeButton("접기", expandedRoot.transform, new Vector2(14f, -124f),
                new Vector2(58f, HudStyle.TouchSize), HudStyle.SurfaceRaised, OnCollapse);
            collapse.name = "Button_TutorialCollapse";

            Button followGuide = MakeButton("단우 보기", expandedRoot.transform, new Vector2(78f, -124f),
                new Vector2(60f, HudStyle.TouchSize), HudStyle.SurfaceRaised, OnFocusGuide);
            followGuide.name = "Button_TutorialFollowGuide";
            followGuideButton = followGuide;

            advanceButton = MakeButton("계속", expandedRoot.transform, new Vector2(480f, -124f),
                new Vector2(112f, HudStyle.TouchSize), HudStyle.Accent, OnAdvance);
            advanceButton.name = "Button_TutorialAdvance";
            advanceText = advanceButton.GetComponentInChildren<Text>();
            advanceRect = advanceButton.GetComponent<RectTransform>();

            Button more = MakeButton("...", expandedRoot.transform, new Vector2(598f, -124f),
                new Vector2(68f, HudStyle.TouchSize), HudStyle.SurfaceRaised, OnToggleMorePopup);
            more.name = "Button_TutorialMore";
            moreRect = more.GetComponent<RectTransform>();

            morePopup = Surface("TutorialMorePopup", expandedRoot.transform, HudStyle.SurfaceRaised,
                new Vector2(1f, 0f), new Vector2(-14f, 52f), new Vector2(224f, 244f));
            morePopupRect = morePopup.GetComponent<RectTransform>();

            Button skip = MakeButton("건너뛰기", morePopup.transform, new Vector2(4f, -4f),
                new Vector2(216f, HudStyle.TouchSize), HudStyle.Surface, OnSkip);
            skip.name = "Button_TutorialSkip";
            skipButton = skip;

            restartButton = MakeButton("처음부터", morePopup.transform, new Vector2(4f, -4f),
                new Vector2(216f, HudStyle.TouchSize), HudStyle.Surface, OnRestart);
            restartButton.name = "Button_TutorialRestart";

            guidanceLessonsButton = MakeButton("기능 목록", morePopup.transform, new Vector2(4f, -52f),
                new Vector2(216f, HudStyle.TouchSize), HudStyle.Surface, OnToggleLessonPicker);
            guidanceLessonsButton.name = "Button_GuidanceLessons";
            nextLessonButton = MakeButton("다음 기능", morePopup.transform, new Vector2(4f, -100f),
                new Vector2(216f, HudStyle.TouchSize), HudStyle.Surface, OnNextLesson);
            nextLessonButton.name = "Button_GuidanceNext";
            Button guidanceToggle = MakeButton("기능 팁 끄기", morePopup.transform, new Vector2(4f, -148f),
                new Vector2(216f, HudStyle.TouchSize), HudStyle.Surface, OnToggleGuidance);
            guidanceToggle.name = "Button_GuidanceToggle";
            guidanceToggleText = guidanceToggle.GetComponentInChildren<Text>();
            morePopup.SetActive(false);

            topicRow = Surface("TutorialAdviceTopics", expandedRoot.transform, HudStyle.SurfaceRaised, new Vector2(0f, 1f),
                new Vector2(10f, -178f), new Vector2(660f, 48f));
            topicRect = topicRow.GetComponent<RectTransform>();
            BuildTopicButton("도시", "Button_Advice_City", AdvisorTopic.City, 4f);
            BuildTopicButton("생산", "Button_Advice_Production", AdvisorTopic.Production, 134f);
            BuildTopicButton("연구", "Button_Advice_Research", AdvisorTopic.Research, 264f);
            BuildTopicButton("설비", "Button_Advice_Factory", AdvisorTopic.Factory, 394f);
            BuildTopicButton("영토", "Button_Advice_Territory", AdvisorTopic.Territory, 524f);
            topicRow.SetActive(false);

            BuildLessonPicker();

            collapsedRoot = Surface("TutorialDialogueHint", visualParent, HudStyle.Surface,
                 new Vector2(0f, 1f), new Vector2(8f, -110f), new Vector2(292f, 44f));
            Surface("TutorialDialogueHintAccent", collapsedRoot.transform, HudStyle.Accent,
                new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 44f), null);
            Button reopen = collapsedRoot.AddComponent<Button>();
            reopen.targetGraphic = collapsedRoot.GetComponent<Image>();
            reopen.onClick.AddListener(OnReopen);
            reopen.name = "Button_TutorialReopen";
            collapsedRoot.name = "Button_TutorialReopen";
            FeelUiFeedback.AttachButton(reopen);
            collapsedText = Label("단우 길잡이", collapsedRoot.transform, HudStyle.BodySize, HudStyle.Text, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Vector2(12f, 0f), new Vector2(244f, 44f));
            Label("›", collapsedRoot.transform, HudStyle.TitleSize, HudStyle.Accent, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Vector2(258f, 0f), new Vector2(26f, 44f));

            introSkipRoot = Surface("Button_IntroSkip", visualParent, HudStyle.Surface,
                new Vector2(1f, 1f), new Vector2(-8f, -60f), new Vector2(128f, 44f), HudAssets.Button);
            Button introSkip = introSkipRoot.AddComponent<Button>();
            introSkip.targetGraphic = introSkipRoot.GetComponent<Image>();
            introSkip.onClick.AddListener(OnSkipIntro);
            introSkip.name = "Button_IntroSkip";
            FeelUiFeedback.AttachButton(introSkip);
            Label("도착 연출 건너뛰기", introSkipRoot.transform, HudStyle.BodySize, HudStyle.Text, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Vector2(4f, 0f), new Vector2(120f, 44f));
            introSkipRoot.SetActive(false);
            ApplyResponsiveLayout(true);
        }

        void BuildLessonPicker()
        {
            lessonPicker = Surface("GuidanceLessonPicker", expandedRoot.transform, HudStyle.SurfaceRaised,
                new Vector2(.5f, 1f), new Vector2(0f, 8f), new Vector2(640f, 378f));
            lessonPickerRect = lessonPicker.GetComponent<RectTransform>();
            lessonPickerRect.pivot = new Vector2(.5f, 0f);

            Label("기능 목록", lessonPicker.transform, HudStyle.TitleSize, HudStyle.Text, FontStyle.Normal,
                TextAnchor.MiddleLeft, new Vector2(10f, -4f), new Vector2(560f, HudStyle.TouchSize));
            Button close = MakeButton("X", lessonPicker.transform, new Vector2(592f, -4f),
                new Vector2(HudStyle.TouchSize, HudStyle.TouchSize), HudStyle.Surface, OnCloseLessonPicker);
            close.name = "Button_GuidanceLessonsClose";

            GameObject viewport = new GameObject("GuidanceLessonViewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(lessonPicker.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(4f, 4f);
            viewportRect.offsetMax = new Vector2(-4f, -(HudStyle.TouchSize + 8f));

            GameObject content = new GameObject("GuidanceLessonContent", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            lessonContentRect = content.GetComponent<RectTransform>();
            lessonContentRect.anchorMin = new Vector2(0f, 1f);
            lessonContentRect.anchorMax = new Vector2(1f, 1f);
            lessonContentRect.pivot = new Vector2(.5f, 1f);
            lessonContentRect.anchoredPosition = Vector2.zero;

            int count = 0;
            foreach (GuidanceLessonDefinition ignored in GuidanceLessonCatalog.All) count++;
            int rows = Mathf.CeilToInt(count / 3f);
            lessonContentRect.sizeDelta = new Vector2(0f, rows * 46f + 2f);

            int index = 0;
            foreach (GuidanceLessonDefinition lesson in GuidanceLessonCatalog.All)
            {
                GuidanceLessonId lessonId = lesson.Id;
                int row = index / 3;
                int column = index % 3;
                Button button = MakeButton((index + 1) + ". " + lesson.Title, content.transform,
                    new Vector2(2f + column * 210f, -2f - row * 46f),
                    new Vector2(204f, HudStyle.TouchSize), HudStyle.Surface,
                    () => OnLessonSelected(lessonId));
                button.name = "Button_GuidanceLesson_" + lesson.StableId;
                lessonButtons.Add(button.GetComponent<RectTransform>());
                index++;
            }

            ScrollRect scroll = lessonPicker.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = lessonContentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 32f;
            lessonPicker.SetActive(false);
        }

        void BuildTopicButton(string label, string objectName, AdvisorTopic topic, float x)
        {
            Button button = MakeButton(label, topicRow.transform, new Vector2(x, -2f),
                new Vector2(126f, HudStyle.TouchSize), HudStyle.Surface, () => OnAdvice(topic));
            button.name = objectName;
            topicButtons.Add(button.GetComponent<RectTransform>());
        }

        void Refresh(bool force)
        {
            if (director == null || expandedRoot == null || collapsedRoot == null) return;

            bool obscured = IsObscured();
            lastObscured = obscured;
            bool intro = director.IntroPlaying;
            bool hasOpenContent = director.IsRunning || director.IsAdviceMode || director.IsLessonMode;
            bool expanded = !obscured && !intro && hasOpenContent && !director.IsCollapsed;
            bool persistentChip = director.IsSkipped || director.IsCompleted;
            bool collapsed = !obscured && !intro && (hasOpenContent ? director.IsCollapsed : persistentChip);

            SetActive(expandedRoot, expanded);
            SetActive(collapsedRoot, collapsed);
            SetActive(introSkipRoot, !obscured && intro);
            if (obscured || intro)
            {
                StopVoice();
                CloseTransientMenus();
                return;
            }

            bool advice = director.IsAdviceMode;
            if (!advice) lessonPickerOpen = false;
            speakerText.text = string.IsNullOrEmpty(director.SpeakerName) ? "단우" : director.SpeakerName;
            titleText.text = string.IsNullOrEmpty(director.Title) ? (advice ? "단우의 마을 장부" : "첫걸음 안내") : director.Title;
            goalText.text = GoalLabel(director.GoalText, director.GoalSatisfied, advice);
            goalText.color = director.GoalSatisfied ? HudStyle.Positive : HudStyle.TextMuted;

            topicRow.SetActive(advice);
            lessonPicker.SetActive(advice && lessonPickerOpen);
            skipButton.gameObject.SetActive(!advice && !director.IsLessonMode && director.IsRunning);
            restartButton.gameObject.SetActive(advice && director.CanRestart);
            guidanceLessonsButton.gameObject.SetActive(advice);
            nextLessonButton.gameObject.SetActive(advice);
            guidanceToggleText.transform.parent.gameObject.SetActive(advice);
            if (!HasMoreActions()) morePopupOpen = false;
            LayoutMoreActions();
            SetActive(morePopup, expanded && morePopupOpen && HasMoreActions());
            nextLessonButton.interactable = director.HasEligibleLesson;
            followGuideButton.gameObject.SetActive(director.GuideAvailable);
            guidanceToggleText.text = director.GuidanceEnabled ? "기능 팁 끄기" : "기능 팁 켜기";
            ApplyResponsiveLayout(false);

            if (advice)
            {
                advanceText.text = director.CanResume ? "이어하기" : "닫기";
                collapsedText.text = "단우에게 다시 묻기";
            }
            else if (director.IsLessonMode)
            {
                collapsedText.text = "단우 · " + TrimForChip(director.Title);
            }
            else if (director.IsRunning)
            {
                advanceText.text = typing ? "바로 보기" : (director.GoalSatisfied ? "계속" : "해 보기");
                collapsedText.text = "단우 · " + TrimForChip(director.Title);
            }
            else
            {
                collapsedText.text = director.IsCompleted ? "단우에게 조언 묻기" : "단우 튜토리얼 이어하기";
            }

            string incoming = director.Text ?? "";
            string signature = ContentSignature(incoming, director.PageTexts, advice, director.IsLessonMode, director.Title);
            if (force || !string.Equals(signature, contentSignature, StringComparison.Ordinal))
                StartContent(incoming, director.PageTexts, signature);
            UpdateAdvanceLabel();
        }

        void StartContent(string value, IReadOnlyList<string> authoredPages, string signature)
        {
            StopVoice();
            contentSignature = signature ?? "";
            fullText = value ?? "";
            pages.Clear();
            if (authoredPages != null && authoredPages.Count > 0)
            {
                if (string.IsNullOrEmpty(fullText)) fullText = JoinPages(authoredPages);
                for (int i = 0; i < authoredPages.Count; i++) AppendFittedPages(authoredPages[i] ?? "");
            }
            else
            {
                AppendFittedPages(fullText);
            }
            if (pages.Count == 0) pages.Add("");
            StartPage(0);
        }

        void StartPage(int index)
        {
            StopVoice();
            pageIndex = Mathf.Clamp(index, 0, pages.Count - 1);
            string pageText = pages[pageIndex];
            displayedText = "";
            revealedCharacters = 0;
            nextCharacterDelay = 0f;
            textElementEnds.Clear();
            textElements.Clear();

            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(pageText);
            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();
                textElements.Add(element);
                textElementEnds.Add(enumerator.ElementIndex + element.Length);
            }

            typing = textElements.Count > 0 && !FeelUiFeedback.ReducedMotion;
            if (typing)
            {
                bodyText.text = "";
            }
            else
            {
                RevealAll();
            }
            UpdatePageIndicator();
            UpdateAdvanceLabel();
        }

        void AdvanceTyping(float elapsed)
        {
            if (!typing || elapsed <= 0f) return;
            nextCharacterDelay -= elapsed;
            int safety = 0;
            while (typing && nextCharacterDelay <= 0f && safety++ < 32)
            {
                string element = textElements[revealedCharacters];
                revealedCharacters++;
                displayedText = pages[pageIndex].Substring(0, textElementEnds[revealedCharacters - 1]);
                bodyText.text = displayedText;
                PlayBlip(element);
                nextCharacterDelay += 1f / CharactersPerSecond + PunctuationPause(element);
                if (revealedCharacters >= textElements.Count)
                {
                    typing = false;
                    UpdateAdvanceLabel();
                }
            }
        }

        public void RevealAll()
        {
            typing = false;
            revealedCharacters = textElements.Count;
            displayedText = pages.Count == 0 ? "" : pages[pageIndex];
            if (bodyText != null) bodyText.text = displayedText;
            StopVoice();
            UpdateAdvanceLabel();
        }

        void OnAdvance()
        {
            CloseTransientMenus();
            if (typing)
            {
                RevealAll();
                return;
            }

            if (pageIndex + 1 < pages.Count)
            {
                StartPage(pageIndex + 1);
                return;
            }

            StopVoice();
            if (director.IsAdviceMode)
            {
                if (director.CanResume) director.Resume();
                else director.Collapse();
            }
            else
            {
                director.Continue();
            }
        }

        void OnCollapse()
        {
            StopVoice();
            CloseTransientMenus();
            director.Collapse();
        }

        void OnSkip()
        {
            StopVoice();
            CloseTransientMenus();
            director.Skip();
        }

        void OnRestart()
        {
            StopVoice();
            CloseTransientMenus();
            director.Restart();
        }

        void OnSkipIntro()
        {
            StopVoice();
            director.SkipIntro();
        }

        void OnFocusGuide()
        {
            director.FocusGuide();
        }

        void OnNextLesson()
        {
            StopVoice();
            CloseTransientMenus();
            director.OpenNextLesson();
        }

        void OnToggleMorePopup()
        {
            if (!HasMoreActions()) return;
            lessonPickerOpen = false;
            morePopupOpen = !morePopupOpen;
            SetActive(lessonPicker, false);
            SetActive(morePopup, morePopupOpen);
            LayoutMoreActions();
            ApplyResponsiveLayout(false);
        }

        void OnToggleLessonPicker()
        {
            if (!director.IsAdviceMode) return;
            lessonPickerOpen = !lessonPickerOpen;
            morePopupOpen = false;
            SetActive(morePopup, false);
            lessonPicker.SetActive(lessonPickerOpen);
            ApplyResponsiveLayout(false);
        }

        void OnCloseLessonPicker()
        {
            lessonPickerOpen = false;
            SetActive(lessonPicker, false);
        }

        void OnLessonSelected(GuidanceLessonId lessonId)
        {
            StopVoice();
            CloseTransientMenus();
            director.OpenLesson(lessonId);
        }

        void OnToggleGuidance()
        {
            CloseTransientMenus();
            director.SetGuidanceEnabled(!director.GuidanceEnabled);
        }

        void OnReopen()
        {
            CloseTransientMenus();
            if (director.IsRunning || director.IsLessonMode) director.Reopen();
            else director.OpenGuide();
        }

        void OnAdvice(AdvisorTopic topic)
        {
            StopVoice();
            CloseTransientMenus();
            director.AskAdvice(topic);
        }

        void CloseTransientMenus()
        {
            lessonPickerOpen = false;
            morePopupOpen = false;
            SetActive(lessonPicker, false);
            SetActive(morePopup, false);
        }

        bool HasMoreActions()
        {
            return (skipButton != null && skipButton.gameObject.activeSelf) ||
                   (restartButton != null && restartButton.gameObject.activeSelf) ||
                   (guidanceLessonsButton != null && guidanceLessonsButton.gameObject.activeSelf);
        }

        void HandleKeyboardShortcut()
        {
            if (director.IntroPlaying || expandedRoot == null || !expandedRoot.activeInHierarchy ||
                IsObscured() || IsInputFieldFocused()) return;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                OnAdvance();
        }

        bool IsObscured()
        {
            GameController game = director == null ? null : director.Game;
            return game != null && (game.HelpOpen || game.ResearchOpen || game.ModalOpen);
        }

        static bool IsInputFieldFocused()
        {
            GameObject selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            return selected != null && selected.GetComponentInParent<InputField>() != null;
        }

        void UpdateAdvanceLabel()
        {
            if (advanceText == null || director == null) return;
            if (typing) advanceText.text = "바로 보기";
            else if (pageIndex + 1 < pages.Count) advanceText.text = "다음 쪽";
            else if (director.IsLessonMode) advanceText.text = "확인";
            else if (director.IsAdviceMode) advanceText.text = director.CanResume ? "이어하기" : "닫기";
            else advanceText.text = director.GoalSatisfied ? "계속" : "해 보기";
        }

        void UpdatePageIndicator()
        {
            if (pageIndicatorText == null) return;
            pageIndicatorText.text = pages.Count > 1 ? (pageIndex + 1) + "/" + pages.Count : "";
        }

        void EnsureVoice()
        {
            if (voiceSource == null) voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.loop = false;
            voiceSource.spatialBlend = 0f;
            voiceSource.volume = .16f;
            voiceSource.ignoreListenerVolume = false;
            voiceSource.ignoreListenerPause = false;

            if (voiceClip != null) return;
            const int sampleRate = 24000;
            const int sampleCount = 720;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * i / (sampleCount - 1f));
                float tone = Mathf.Sin(Mathf.PI * 2f * 390f * t) * .7f + Mathf.Sin(Mathf.PI * 2f * 585f * t) * .3f;
                samples[i] = tone * envelope * .22f;
            }
            voiceClip = AudioClip.Create("Danwoo procedural speech blip", sampleCount, 1, sampleRate, false);
            voiceClip.SetData(samples, 0);
        }

        void PlayBlip(string element)
        {
            if (voiceSource == null || voiceClip == null || string.IsNullOrWhiteSpace(element) || AudioListener.volume <= 0f) return;
            if (!char.IsLetterOrDigit(element, 0)) return;
            float now = Time.unscaledTime;
            if (now - lastBlipAt < 1f / MaximumBlipsPerSecond) return;
            voiceSource.pitch = VoicePitches[blipSequence++ % VoicePitches.Length];
            voiceSource.PlayOneShot(voiceClip, .34f);
            lastBlipAt = now;
        }

        void StopVoice()
        {
            if (voiceSource != null) voiceSource.Stop();
        }

        static float PunctuationPause(string element)
        {
            if (string.IsNullOrEmpty(element)) return 0f;
            char last = element[element.Length - 1];
            if (last == '.' || last == '!' || last == '?' || last == '。' || last == '！' || last == '？') return .18f;
            if (last == ',' || last == ';' || last == ':' || last == '…' || last == '、') return .12f;
            return 0f;
        }

        static string ContentSignature(string text, IReadOnlyList<string> authoredPages, bool advice, bool lesson, string title)
        {
            var signature = new StringBuilder((text == null ? 0 : text.Length) + 64);
            signature.Append(advice ? 'A' : lesson ? 'L' : 'T').Append('\u001f');
            signature.Append(title ?? "").Append('\u001f').Append(text ?? "");
            if (authoredPages == null) return signature.ToString();
            signature.Append('\u001e').Append(authoredPages.Count);
            for (int i = 0; i < authoredPages.Count; i++)
            {
                string page = authoredPages[i] ?? "";
                signature.Append('\u001f').Append(page.Length).Append(':').Append(page);
            }
            return signature.ToString();
        }

        static string JoinPages(IReadOnlyList<string> authoredPages)
        {
            var joined = new StringBuilder();
            for (int i = 0; i < authoredPages.Count; i++)
            {
                string page = authoredPages[i];
                if (string.IsNullOrWhiteSpace(page)) continue;
                if (joined.Length > 0) joined.Append("\n\n");
                joined.Append(page.Trim());
            }
            return joined.ToString();
        }

        void AppendFittedPages(string value)
        {
            string source = (value ?? "").Trim();
            if (source.Length == 0) return;

            var elements = new List<string>();
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(source);
            while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());

            int start = 0;
            while (start < elements.Count)
            {
                int end = Mathf.Min(start + MaximumPageElements, elements.Count);
                string candidate = Slice(elements, start, end).Trim();
                while (end > start + 1 && !PageFits(candidate))
                {
                    end--;
                    candidate = Slice(elements, start, end).Trim();
                }

                if (end < elements.Count)
                {
                    int naturalBreak = FindNaturalBreak(elements, start, end);
                    if (naturalBreak > start)
                    {
                        end = naturalBreak;
                        candidate = Slice(elements, start, end).Trim();
                    }
                }

                if (candidate.Length > 0) pages.Add(candidate);
                start = Mathf.Max(end, start + 1);
                while (start < elements.Count && string.IsNullOrWhiteSpace(elements[start])) start++;
            }
        }

        bool PageFits(string candidate)
        {
            if (string.IsNullOrEmpty(candidate) || bodyText == null || font == null) return true;
            try
            {
                Rect adjustedRect = bodyText.GetPixelAdjustedRect();
                float availableWidth = adjustedRect.width > 0f ? adjustedRect.width : bodyText.rectTransform.rect.width;
                float availableHeight = bodyText.rectTransform.rect.height;
                TextGenerationSettings settings = bodyText.GetGenerationSettings(new Vector2(availableWidth, 0f));
                settings.resizeTextForBestFit = false;
                settings.horizontalOverflow = HorizontalWrapMode.Wrap;
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                settings.generateOutOfBounds = false;
                float pixelsPerUnit = Mathf.Max(.0001f, bodyText.pixelsPerUnit);
                float preferredHeight = pageLayoutGenerator.GetPreferredHeight(candidate, settings) / pixelsPerUnit;
                return preferredHeight > 0f && preferredHeight <= availableHeight + .01f;
            }
            catch (Exception)
            {
                // Dynamic-font layout can be unavailable for one frame on a few platforms.
                // Three conservative estimated lines prevent a false fit from clipping text.
                return FitsThreeEstimatedLines(candidate);
            }
        }

        static bool FitsThreeEstimatedLines(string candidate)
        {
            const float conservativeColumns = 28f;
            int lines = 1;
            float columns = 0f;
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(candidate);
            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();
                if (element == "\r") continue;
                if (element == "\n")
                {
                    lines++;
                    columns = 0f;
                    if (lines > 3) return false;
                    continue;
                }

                char first = element[0];
                float width = char.IsWhiteSpace(first) ? .5f : first <= 0x7f ? .65f : 1f;
                if (columns > 0f && columns + width > conservativeColumns)
                {
                    lines++;
                    columns = 0f;
                    if (lines > 3) return false;
                }
                columns += width;
            }
            return lines <= 3;
        }

        static int FindNaturalBreak(IReadOnlyList<string> elements, int start, int end)
        {
            int floor = start + Mathf.Min(PreferredBreakFloor, Mathf.Max(1, end - start - 12));
            int preferred = FindBreak(elements, end - 1, floor);
            if (preferred > start) return preferred;
            int anyWordBoundary = FindBreak(elements, floor - 1, start + 1);
            return anyWordBoundary > start ? anyWordBoundary : end;
        }

        static int FindBreak(IReadOnlyList<string> elements, int from, int through)
        {
            for (int i = from; i >= through; i--)
            {
                string element = elements[i];
                if (string.IsNullOrWhiteSpace(element)) return i;
                if (EndsSentence(element)) return i + 1;
            }
            return -1;
        }

        static bool EndsSentence(string element)
        {
            if (string.IsNullOrEmpty(element)) return false;
            char value = element[element.Length - 1];
            return value == '.' || value == '!' || value == '?' || value == '。' || value == '！' || value == '？';
        }

        static string Slice(IReadOnlyList<string> elements, int start, int end)
        {
            var value = new StringBuilder();
            for (int i = start; i < end; i++) value.Append(elements[i]);
            return value.ToString();
        }

        static string GoalLabel(string value, bool satisfied, bool advice)
        {
            if (advice || string.IsNullOrWhiteSpace(value)) return advice ? "장부에서 궁금한 주제를 골라 보세요." : "";
            return (satisfied ? "✓ " : "◆ ") + value;
        }

        static string TrimForChip(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "현재 안내";
            return value.Length <= 17 ? value : value.Substring(0, 16) + "…";
        }

        void LayoutMoreActions()
        {
            if (morePopupRect == null) return;
            float y = -4f;
            PositionVisibleMenuButton(skipButton, ref y);
            PositionVisibleMenuButton(restartButton, ref y);
            PositionVisibleMenuButton(guidanceLessonsButton, ref y);
            PositionVisibleMenuButton(nextLessonButton, ref y);
            Button guidanceToggle = guidanceToggleText == null ? null : guidanceToggleText.GetComponentInParent<Button>();
            PositionVisibleMenuButton(guidanceToggle, ref y);
            morePopupRect.sizeDelta = new Vector2(224f, Mathf.Max(HudStyle.TouchSize + 8f, -y + 4f));
            PositionMorePopup();
        }

        void PositionMorePopup()
        {
            if (morePopupRect == null || moreRect == null || expandedRect == null) return;
            morePopupRect.anchoredPosition = new Vector2(-14f,
                expandedRect.rect.height + moreRect.anchoredPosition.y + 8f);
        }

        static void PositionVisibleMenuButton(Button button, ref float y)
        {
            if (button == null || !button.gameObject.activeSelf) return;
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(4f, y);
            y -= HudStyle.TouchSize + 4f;
        }

        void ApplyResponsiveLayout(bool force)
        {
            if (expandedRect == null || visualParent == null) return;
            RectTransform parentRect = visualParent as RectTransform;
            float safeWidth = parentRect == null || parentRect.rect.width <= 0f
                ? Mathf.Max(720f, Screen.safeArea.width)
                : parentRect.rect.width;
            float safeHeight = parentRect == null || parentRect.rect.height <= 0f
                ? Mathf.Max(720f, Screen.safeArea.height)
                : parentRect.rect.height;
            float width = Mathf.Clamp(safeWidth - 16f, 480f, 680f);
            Hud hud = visualParent.GetComponentInParent<Hud>();
            bool constructionVisible = hud != null && hud.ConstructionBarVisible;
            float constructionHeight = constructionVisible ? Mathf.Max(0f, hud.ConstructionBarHeight) : 0f;
            bool advice = director != null && director.IsAdviceMode;
            bool textGeometryChanged = force || Mathf.Abs(expandedRect.sizeDelta.x - width) > .01f;
            bool geometryChanged = force || lastScreenWidth != Screen.width || lastScreenHeight != Screen.height ||
                                   lastSafeArea != Screen.safeArea || Mathf.Abs(expandedRect.sizeDelta.x - width) > .01f ||
                                   lastSafeSize != new Vector2(safeWidth, safeHeight) ||
                                   advice != lastAdviceMode ||
                                   constructionVisible != lastConstructionBarVisible ||
                                   Mathf.Abs(constructionHeight - lastConstructionBarHeight) > .01f;
            if (!geometryChanged) return;

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastSafeArea = Screen.safeArea;
            lastSafeSize = new Vector2(safeWidth, safeHeight);
            lastAdviceMode = advice;
            lastConstructionBarVisible = constructionVisible;
            lastConstructionBarHeight = constructionHeight;

            expandedRect.sizeDelta = new Vector2(width, advice ? 230f : 174f);
            float constructionOffset = constructionVisible ? constructionHeight + 8f : 0f;
            expandedRect.anchoredPosition = new Vector2(0f, 68f + constructionOffset);

            SetWidth(bodyHitRect, width - 28f);
            SetWidth(bodyText == null ? null : bodyText.rectTransform, width - 48f);
            SetPositionAndWidth(titleText == null ? null : titleText.rectTransform, 84f, width - 210f);
            SetPositionAndWidth(pageIndicatorText == null ? null : pageIndicatorText.rectTransform, width - 110f, 92f);
            SetPositionAndWidth(goalRect, 144f, Mathf.Max(120f, width - 398f));
            SetX(advanceRect, width - 200f);
            SetX(moreRect, width - 82f);
            PositionMorePopup();

            if (topicRect != null)
            {
                topicRect.sizeDelta = new Vector2(width - 20f, 48f);
                float buttonWidth = (width - 44f) / 5f;
                for (int i = 0; i < topicButtons.Count; i++)
                {
                    RectTransform rect = topicButtons[i];
                    rect.anchoredPosition = new Vector2(4f + i * buttonWidth, -2f);
                    SetButtonSize(rect, buttonWidth - 4f, HudStyle.TouchSize);
                }
            }

            if (lessonPickerRect != null)
            {
                float pickerWidth = Mathf.Min(640f, width - 20f);
                float dialogueTop = expandedRect.anchoredPosition.y + expandedRect.sizeDelta.y;
                float availableAbove = Mathf.Max(0f, safeHeight - dialogueTop - 8f);
                float pickerHeight = Mathf.Min(Mathf.Max(100f, availableAbove), Mathf.Min(378f, safeHeight - 16f));
                lessonPickerRect.sizeDelta = new Vector2(pickerWidth, pickerHeight);
                float pickerBottom = Mathf.Min(dialogueTop + 8f, safeHeight - pickerHeight - 8f);
                lessonPickerRect.anchoredPosition = new Vector2(0f, pickerBottom - dialogueTop);
                float cellWidth = (pickerWidth - 8f) / 3f;
                Button close = lessonPickerRect.Find("Button_GuidanceLessonsClose") == null
                    ? null
                    : lessonPickerRect.Find("Button_GuidanceLessonsClose").GetComponent<Button>();
                if (close != null) close.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(pickerWidth - HudStyle.TouchSize - 4f, -4f);
                Text pickerTitle = lessonPickerRect.GetComponentInChildren<Text>();
                if (pickerTitle != null && pickerTitle.transform.parent == lessonPickerRect)
                    pickerTitle.rectTransform.sizeDelta = new Vector2(
                        Mathf.Max(1f, pickerWidth - HudStyle.TouchSize - 22f), HudStyle.TouchSize);
                for (int i = 0; i < lessonButtons.Count; i++)
                {
                    int row = i / 3;
                    int column = i % 3;
                    RectTransform rect = lessonButtons[i];
                    rect.anchoredPosition = new Vector2(2f + column * cellWidth, -2f - row * 46f);
                    SetButtonSize(rect, cellWidth - 6f, HudStyle.TouchSize);
                }
            }

            if (!force && textGeometryChanged && director != null && pages.Count > 0)
                StartContent(director.Text ?? "", director.PageTexts, contentSignature);
        }

        static void SetWidth(RectTransform rect, float width)
        {
            if (rect == null) return;
            rect.sizeDelta = new Vector2(Mathf.Max(1f, width), rect.sizeDelta.y);
        }

        static void SetPositionAndWidth(RectTransform rect, float x, float width)
        {
            if (rect == null) return;
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            SetWidth(rect, width);
        }

        static void SetX(RectTransform rect, float x)
        {
            if (rect != null) rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
        }

        static void SetButtonSize(RectTransform rect, float width, float height)
        {
            if (rect == null) return;
            rect.sizeDelta = new Vector2(Mathf.Max(1f, width), height);
            Text label = rect.GetComponentInChildren<Text>();
            if (label != null) label.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, width - 8f), height);
        }

        Button MakeButton(string value, Transform parent, Vector2 position, Vector2 size, Color color,
            UnityEngine.Events.UnityAction click)
        {
            GameObject go = Surface("Button_" + value, parent, color, new Vector2(0f, 1f), position, size, HudAssets.Button);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(.82f, .82f, .82f, 1f);
            colors.disabledColor = new Color(.55f, .55f, .55f, .7f);
            button.colors = colors;
            button.onClick.AddListener(click);
            Label(value, go.transform, HudStyle.BodySize, HudStyle.Foreground(color), FontStyle.Normal, TextAnchor.MiddleCenter,
                new Vector2(4f, 0f), new Vector2(size.x - 8f, size.y));
            FeelUiFeedback.AttachButton(button);
            return button;
        }

        GameObject Surface(string name, Transform parent, Color color, Vector2 pivot, Vector2 position,
            Vector2 size, Sprite sprite = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = pivot;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : HudAssets.Panel;
            image.type = Image.Type.Sliced;
            image.color = color;
            return go;
        }

        Text Label(string value, Transform parent, int size, Color color, FontStyle style, TextAnchor alignment,
            Vector2 position, Vector2 dimensions)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            Text text = go.GetComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value) target.SetActive(value);
        }

        void DestroyVisuals()
        {
            DestroyOwnedObject(expandedRoot);
            DestroyOwnedObject(collapsedRoot);
            DestroyOwnedObject(introSkipRoot);
            expandedRoot = null;
            collapsedRoot = null;
            introSkipRoot = null;
            topicRow = null;
            lessonPicker = null;
            expandedRect = null;
        }

        static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

    }
}

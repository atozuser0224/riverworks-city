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

        static readonly Color Navy = Hex("172437");
        static readonly Color Navy2 = Hex("24384F");
        static readonly Color Brass = Hex("D5A84B");
        static readonly Color Cream = Hex("F4ECD8");
        static readonly Color Muted = Hex("B8B5A8");
        static readonly Color Teal = Hex("4EBFAF");

        readonly List<int> textElementEnds = new List<int>(256);
        readonly List<string> textElements = new List<string>(256);
        readonly List<string> pages = new List<string>(8);
        readonly TextGenerator pageLayoutGenerator = new TextGenerator();

        TutorialDirector director;
        Transform visualParent;
        GameObject expandedRoot;
        GameObject collapsedRoot;
        GameObject introSkipRoot;
        GameObject topicRow;
        GameObject lessonPicker;
        RectTransform expandedRect;
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

        public bool Typing => typing;
        public bool Visible =>
            (expandedRoot != null && expandedRoot.activeInHierarchy) ||
            (collapsedRoot != null && collapsedRoot.activeInHierarchy);
        public string DisplayedText => displayedText;
        public string FullText => fullText;
        public string CurrentPageText => pages.Count == 0 ? "" : pages[pageIndex];
        public int RevealedCharacters => revealedCharacters;
        public bool CurrentPageFits => PageFits(CurrentPageText);
        /// <summary>Zero-based index of the page currently presented.</summary>
        public int PageIndex => pageIndex;
        public int PageCount => pages.Count;

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

            expandedRoot = Surface("TutorialDialogue", visualParent, Navy, new Vector2(.5f, 0f),
                new Vector2(0f, 68f), new Vector2(660f, 156f));
            expandedRect = expandedRoot.GetComponent<RectTransform>();
            FeelUiFeedback.AttachPanel(expandedRoot);

            speakerText = Label("단우", expandedRoot.transform, 12, Brass, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Vector2(14f, -8f), new Vector2(64f, 24f));

            titleText = Label("", expandedRoot.transform, 15, Brass, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Vector2(84f, -8f), new Vector2(360f, 24f));
            pageIndicatorText = Label("", expandedRoot.transform, 11, Muted, FontStyle.Bold,
                TextAnchor.MiddleRight, new Vector2(452f, -8f), new Vector2(82f, 24f));

            GameObject bodyHit = Surface("TutorialDialogueBody", expandedRoot.transform,
                new Color(Navy2.r, Navy2.g, Navy2.b, .58f), new Vector2(0f, 1f),
                new Vector2(14f, -35f), new Vector2(522f, 79f));
            Button bodyButton = bodyHit.AddComponent<Button>();
            bodyButton.targetGraphic = bodyHit.GetComponent<Image>();
            bodyButton.transition = Selectable.Transition.None;
            bodyButton.onClick.AddListener(OnAdvance);
            FeelUiFeedback.AttachButton(bodyButton);
            bodyText = Label("", bodyHit.transform, 16, Cream, FontStyle.Normal,
                TextAnchor.UpperLeft, new Vector2(10f, -5f), new Vector2(502f, 69f));
            bodyText.resizeTextForBestFit = false;

            goalText = Label("", expandedRoot.transform, 12, Muted, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Vector2(14f, -119f), new Vector2(410f, 28f));

            Button followGuide = MakeButton("단우 보기", expandedRoot.transform, new Vector2(432f, -112f),
                new Vector2(104f, 44f), Navy2, OnFocusGuide, 11);
            followGuide.name = "Button_TutorialFollowGuide";
            followGuideButton = followGuide;

            Button collapse = MakeButton("접기", expandedRoot.transform, new Vector2(546f, -6f),
                new Vector2(50f, 44f), Navy2, OnCollapse, 11);
            collapse.name = "Button_TutorialCollapse";
            Button skip = MakeButton("건너뛰기", expandedRoot.transform, new Vector2(602f, -6f),
                new Vector2(50f, 44f), new Color(.42f, .27f, .27f, 1f), OnSkip, 9);
            skip.name = "Button_TutorialSkip";
            skipButton = skip;

            restartButton = MakeButton("처음부터", expandedRoot.transform, new Vector2(602f, -6f),
                new Vector2(50f, 44f), new Color(.42f, .31f, .20f, 1f), OnRestart, 9);
            restartButton.name = "Button_TutorialRestart";

            advanceButton = MakeButton("계속", expandedRoot.transform, new Vector2(546f, -56f),
                new Vector2(106f, 92f), Brass, OnAdvance, 14);
            advanceButton.name = "Button_TutorialAdvance";
            advanceText = advanceButton.GetComponentInChildren<Text>();

            topicRow = Surface("TutorialAdviceTopics", expandedRoot.transform, Navy2, new Vector2(0f, 1f),
                new Vector2(10f, -162f), new Vector2(640f, 48f));
            BuildTopicButton("도시", "Button_Advice_City", AdvisorTopic.City, 4f);
            BuildTopicButton("생산", "Button_Advice_Production", AdvisorTopic.Production, 66f);
            BuildTopicButton("연구", "Button_Advice_Research", AdvisorTopic.Research, 128f);
            BuildTopicButton("설비", "Button_Advice_Factory", AdvisorTopic.Factory, 190f);
            BuildTopicButton("영토", "Button_Advice_Territory", AdvisorTopic.Territory, 252f);
            Button lessonList = MakeButton("기능 목록", topicRow.transform, new Vector2(314f, -2f),
                new Vector2(104f, 44f), Navy, OnToggleLessonPicker, 10);
            lessonList.name = "Button_GuidanceLessons";
            nextLessonButton = MakeButton("다음 기능", topicRow.transform, new Vector2(422f, -2f),
                new Vector2(104f, 44f), Brass, OnNextLesson, 10);
            nextLessonButton.name = "Button_GuidanceNext";
            Button guidanceToggle = MakeButton("기능 팁 끄기", topicRow.transform, new Vector2(530f, -2f),
                new Vector2(106f, 44f), Navy, OnToggleGuidance, 10);
            guidanceToggle.name = "Button_GuidanceToggle";
            guidanceToggleText = guidanceToggle.GetComponentInChildren<Text>();
            topicRow.SetActive(false);

            BuildLessonPicker();

            collapsedRoot = Surface("TutorialDialogueHint", visualParent, new Color(Navy.r, Navy.g, Navy.b, .97f),
                new Vector2(0f, 1f), new Vector2(8f, -110f), new Vector2(292f, 44f));
            Button reopen = collapsedRoot.AddComponent<Button>();
            reopen.targetGraphic = collapsedRoot.GetComponent<Image>();
            reopen.onClick.AddListener(OnReopen);
            reopen.name = "Button_TutorialReopen";
            collapsedRoot.name = "Button_TutorialReopen";
            FeelUiFeedback.AttachButton(reopen);
            collapsedText = Label("단우 길잡이", collapsedRoot.transform, 12, Cream, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Vector2(12f, 0f), new Vector2(244f, 44f));
            Label("›", collapsedRoot.transform, 18, Brass, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Vector2(258f, 0f), new Vector2(26f, 44f));

            introSkipRoot = Surface("Button_IntroSkip", visualParent, new Color(Navy.r, Navy.g, Navy.b, .97f),
                new Vector2(1f, 1f), new Vector2(-8f, -60f), new Vector2(128f, 44f), HudAssets.Button);
            Button introSkip = introSkipRoot.AddComponent<Button>();
            introSkip.targetGraphic = introSkipRoot.GetComponent<Image>();
            introSkip.onClick.AddListener(OnSkipIntro);
            introSkip.name = "Button_IntroSkip";
            FeelUiFeedback.AttachButton(introSkip);
            Label("도착 연출 건너뛰기", introSkipRoot.transform, 10, Cream, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Vector2(4f, 0f), new Vector2(120f, 44f));
            introSkipRoot.SetActive(false);
        }

        void BuildLessonPicker()
        {
            lessonPicker = Surface("GuidanceLessonPicker", expandedRoot.transform, Navy2, new Vector2(0f, 1f),
                new Vector2(10f, -216f), new Vector2(640f, 142f));

            GameObject viewport = new GameObject("GuidanceLessonViewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(lessonPicker.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(4f, 4f);
            viewportRect.offsetMax = new Vector2(-4f, -4f);

            GameObject content = new GameObject("GuidanceLessonContent", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;

            int count = 0;
            foreach (GuidanceLessonDefinition ignored in GuidanceLessonCatalog.All) count++;
            int rows = Mathf.CeilToInt(count / 3f);
            contentRect.sizeDelta = new Vector2(0f, rows * 46f + 2f);

            int index = 0;
            foreach (GuidanceLessonDefinition lesson in GuidanceLessonCatalog.All)
            {
                GuidanceLessonId lessonId = lesson.Id;
                int row = index / 3;
                int column = index % 3;
                Button button = MakeButton((index + 1) + ". " + lesson.Title, content.transform,
                    new Vector2(2f + column * 210f, -2f - row * 46f), new Vector2(204f, 44f), Navy,
                    () => OnLessonSelected(lessonId), 10);
                button.name = "Button_GuidanceLesson_" + lesson.StableId;
                index++;
            }

            ScrollRect scroll = lessonPicker.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 32f;
            lessonPicker.SetActive(false);
        }

        void BuildTopicButton(string label, string objectName, AdvisorTopic topic, float x)
        {
            Button button = MakeButton(label, topicRow.transform, new Vector2(x, -2f), new Vector2(58f, 44f),
                Navy, () => OnAdvice(topic), 12);
            button.name = objectName;
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
                return;
            }

            bool advice = director.IsAdviceMode;
            if (!advice) lessonPickerOpen = false;
            speakerText.text = string.IsNullOrEmpty(director.SpeakerName) ? "단우" : director.SpeakerName;
            titleText.text = string.IsNullOrEmpty(director.Title) ? (advice ? "단우의 마을 장부" : "첫걸음 안내") : director.Title;
            goalText.text = GoalLabel(director.GoalText, director.GoalSatisfied, advice);
            goalText.color = director.GoalSatisfied ? Teal : Muted;

            topicRow.SetActive(advice);
            lessonPicker.SetActive(advice && lessonPickerOpen);
            expandedRect.sizeDelta = new Vector2(660f, advice ? (lessonPickerOpen ? 364f : 216f) : 156f);
            skipButton.gameObject.SetActive(!advice && !director.IsLessonMode && director.IsRunning);
            restartButton.gameObject.SetActive(advice && director.CanRestart);
            nextLessonButton.interactable = director.HasEligibleLesson;
            followGuideButton.gameObject.SetActive(director.GuideAvailable);
            guidanceToggleText.text = director.GuidanceEnabled ? "기능 팁 끄기" : "기능 팁 켜기";

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
            director.Collapse();
        }

        void OnSkip()
        {
            StopVoice();
            director.Skip();
        }

        void OnRestart()
        {
            StopVoice();
            lessonPickerOpen = false;
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
            lessonPickerOpen = false;
            director.OpenNextLesson();
        }

        void OnToggleLessonPicker()
        {
            if (!director.IsAdviceMode) return;
            lessonPickerOpen = !lessonPickerOpen;
            lessonPicker.SetActive(lessonPickerOpen);
            expandedRect.sizeDelta = new Vector2(660f, lessonPickerOpen ? 364f : 216f);
        }

        void OnLessonSelected(GuidanceLessonId lessonId)
        {
            StopVoice();
            lessonPickerOpen = false;
            director.OpenLesson(lessonId);
        }

        void OnToggleGuidance()
        {
            director.SetGuidanceEnabled(!director.GuidanceEnabled);
        }

        void OnReopen()
        {
            if (director.IsRunning || director.IsLessonMode) director.Reopen();
            else director.OpenGuide();
        }

        void OnAdvice(AdvisorTopic topic)
        {
            StopVoice();
            lessonPickerOpen = false;
            director.AskAdvice(topic);
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

        Button MakeButton(string value, Transform parent, Vector2 position, Vector2 size, Color color,
            UnityEngine.Events.UnityAction click, int textSize)
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
            Label(value, go.transform, textSize, Cream, FontStyle.Bold, TextAnchor.MiddleCenter,
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

        static Color Hex(string value)
        {
            Color color;
            ColorUtility.TryParseHtmlString("#" + value, out color);
            return color;
        }
    }
}

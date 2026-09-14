using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riverworks
{
    /// <summary>
    /// Bridges the pure tutorial rules to live player actions. This component never performs a
    /// goal on the player's behalf; it only observes the city, owns tutorial presentation state,
    /// and persists guarded progress transitions.
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        const string ExplicitSmokeArgument = "-riverworks-tutorial-smoke";
        public const float MinimumLessonIntervalSeconds = 10f;
        static readonly string[] NoPages = Array.Empty<string>();

        GameController game;
        GameState observedState;
        TutorialStepId observedStep;
        bool dialogueAcknowledged;
        bool saveSucceeded;
        int successfulSaveVersion = -1;
        bool adviceMode;
        AdvisorTopic adviceTopic;
        string adviceText = "";
        bool lessonMode;
        bool lessonCollapsed;
        GuidanceLessonDefinition currentLesson;
        string lessonText = "";
        float nextLessonEligibleAt = float.PositiveInfinity;
        bool automaticUiSuppressed;
        bool explicitlyOpened;
        bool handlingGameChange;
        bool applyingTutorialSpeed;
        bool ownsPause;
        float ownedPauseResumeSpeed = 1f;
        bool ownsLessonPause;
        float lessonResumeSpeed = 1f;
        float observedSpeed = 1f;
        bool publishedGoalSatisfied;
        LineRenderer targetOutline;
        Material targetMaterial;
        TutorialDialogue dialogue;
        TownHallIntro townHallIntro;
        bool bindingState;
        bool stopFollowingWhenGuideArrives;
        float stopFollowingDeadline;

        public event Action Changed;

        public GameController Game => game;
        public TutorialStepId Step => Progress != null ? Progress.Step : observedStep;
        public int AdvanceCount { get; private set; }
        public bool PersistResult { get; private set; }
        public string PersistError { get; private set; } = "";
        public int PersistAttemptCount { get; private set; }
        public bool IsAdviceMode => adviceMode;
        public AdvisorTopic AdviceTopic => adviceTopic;
        public bool IntroPlaying => townHallIntro != null && townHallIntro.IsPlaying;
        public TownHallIntro Intro => townHallIntro;
        public bool IsLessonMode => lessonMode;
        public GuidanceLessonId CurrentLessonId => currentLesson != null ? currentLesson.Id : default;
        public GuidanceLessonDefinition CurrentLessonDefinition => currentLesson;
        public IReadOnlyList<string> PageTexts => lessonMode && currentLesson?.Pages != null ? currentLesson.Pages : NoPages;
        public int LessonOpenCount { get; private set; }
        public int LessonAcknowledgeCount { get; private set; }
        public float LessonCooldownRemaining => Mathf.Max(0f, nextLessonEligibleAt - Time.unscaledTime);
        public bool GuideAvailable => game != null && game.Citizens != null && game.Citizens.GuideAvailable;
        public bool GuideFollowing => game != null && game.CameraRig != null &&
                                      game.CameraRig.Cinematics != null && game.CameraRig.Cinematics.IsFollowing;
        public bool GuideWalking => game != null && game.Citizens != null &&
                                    game.Citizens.Simulation != null && game.Citizens.Simulation.GuideWalking;
        public int GuideFocusCount { get; private set; }
        public int GuideRouteCount { get; private set; }
        public bool GuidanceEnabled => Progress != null && Progress.GuidanceEnabled;
        public bool HasEligibleLesson => GuidanceLessonCatalog.GetNextUnseenUnlocked(observedState) != null;
        public bool IsCompleted => Progress != null && Progress.Completed;
        public bool IsSkipped => Progress != null && Progress.Skipped;
        public bool CanResume => IsSkipped;
        public bool CanRestart => observedState != null;
        public bool IsRunning => !IntroPlaying && CanPresentAutomatically && Progress != null && Progress.Enabled &&
                                 !Progress.Completed && !Progress.Skipped;
        public bool IsCollapsed => lessonMode ? lessonCollapsed : !adviceMode && IsRunning && Progress.Collapsed;
        public bool ConsumesAdvanceShortcut => IntroPlaying || adviceMode || lessonMode || (IsRunning && !IsCollapsed);
        public bool GoalSatisfied => lessonMode || (IsRunning && TutorialCatalog.EvaluateGoal(observedState, BuildFacts()));
        public string SpeakerName => TutorialAdvisor.Name;
        public string Title => lessonMode ? currentLesson?.Title ?? "기능 안내" :
            adviceMode ? AdviceTitle(adviceTopic) : CurrentDefinition?.Title ?? "길잡이";
        public string Text => lessonMode ? lessonText : adviceMode ? adviceText : CurrentDefinition?.Dialogue ?? "";
        public string GoalText => lessonMode ? currentLesson?.Goal ?? "기능 안내를 끝까지 읽어 보세요." :
            adviceMode ? "궁금한 주제를 골라 단우에게 다시 물어볼 수 있어요." : CurrentDefinition?.Goal ?? "";

        TutorialProgress Progress => observedState?.Tutorial;
        TutorialStepDefinition CurrentDefinition => TutorialCatalog.Current(observedState);
        bool CanPresentAutomatically => !automaticUiSuppressed || explicitlyOpened;

        public void Initialize(GameController controller)
        {
            if (ReferenceEquals(game, controller) && observedState != null) return;
            if (game != null) game.Changed -= OnGameChanged;

            game = controller;
            automaticUiSuppressed = game != null && game.SmokeMode && !HasCommandLineArgument(ExplicitSmokeArgument);
            explicitlyOpened = false;
            EnsureTargetOutline();
            EnsureTownHallIntro();
            EnsureDialogue();

            if (game == null)
            {
                BindState(null, false);
                return;
            }

            observedSpeed = game.GameSpeed;
            BindState(game.State, true);
            game.Changed += OnGameChanged;
        }

        void Update()
        {
            UpdateGuideFollowForTask();
            if (game == null || observedState == null || IntroPlaying || lessonMode || adviceMode) return;
            if (Time.unscaledTime < nextLessonEligibleAt || !CanPresentAutomatically || !IsIdleForGuidance()) return;
            TryOpenNextLesson(false);
        }

        /// <summary>Called only after the dialogue UI has revealed the complete current line.</summary>
        public void Continue()
        {
            if (IntroPlaying) return;
            if (lessonMode)
            {
                AcknowledgeLesson();
                return;
            }
            if (adviceMode)
            {
                adviceMode = false;
                DelayGuidanceAfterManualPanel();
                SyncGuideControl(false, false);
                RefreshPresentation();
                return;
            }
            if (!IsRunning) return;
            if (Progress.Collapsed)
            {
                Reopen();
                return;
            }

            dialogueAcknowledged = true;
            if (!TryAdvance())
            {
                Progress.SetCollapsed(true);
                PrepareTaskView();
                RefreshPresentation();
            }
        }

        public void Collapse()
        {
            if (lessonMode)
            {
                lessonCollapsed = true;
                ReleaseLessonPause();
                StopFollowingGuide();
                RefreshPresentation();
                return;
            }
            if (adviceMode)
            {
                adviceMode = false;
                DelayGuidanceAfterManualPanel();
                SyncGuideControl(false, false);
            }
            if (IsRunning)
            {
                Progress.SetCollapsed(true);
                PrepareTaskView();
            }
            RefreshPresentation();
        }

        public void Reopen()
        {
            if (lessonMode)
            {
                lessonCollapsed = false;
                AcquireLessonPause();
                SyncGuideControl(false, false);
                RefreshPresentation();
                return;
            }
            if (!IsRunning) return;
            adviceMode = false;
            Progress.SetCollapsed(false);
            SyncGuideControl(false, false);
            RefreshPresentation();
        }

        public void Skip()
        {
            if (Progress == null || Progress.Completed) return;
            adviceMode = false;
            CloseLesson(false);
            dialogueAcknowledged = false;
            ClearSaveLatch();
            Progress.Skip();
            Progress.SetGuidanceEnabled(false);
            ReleaseOwnedPause();
            SyncGuideControl(false, false);
            PersistProgress();
            RefreshPresentation();
        }

        public void Resume()
        {
            if (Progress == null || Progress.Completed || !Progress.Skipped) return;
            explicitlyOpened = true;
            adviceMode = false;
            CloseLesson(false);
            dialogueAcknowledged = false;
            ClearSaveLatch();
            Progress.Resume();
            Progress.SetGuidanceEnabled(true);
            Progress.SetCollapsed(false);
            observedStep = Progress.Step;
            SyncGuideControl(true, false);
            PersistProgress();
            RefreshPresentation();
        }

        public void Restart()
        {
            if (observedState == null) return;
            explicitlyOpened = true;
            adviceMode = false;
            CloseLesson(false);
            dialogueAcknowledged = false;
            ClearSaveLatch();
            observedState.Tutorial = TutorialProgress.Replay(observedState);
            observedStep = observedState.Tutorial.Step;
            AcquirePause();
            SyncGuideControl(true, false);
            PersistProgress();
            RefreshPresentation();
        }

        /// <summary>Entry point used by the persistent guide chip and the menu button.</summary>
        public void OpenGuide()
        {
            if (observedState == null) return;
            explicitlyOpened = true;
            if (lessonMode)
            {
                Reopen();
                return;
            }
            if (Progress == null)
            {
                observedState.Tutorial = TutorialProgress.Create(observedState);
                observedStep = observedState.Tutorial.Step;
                dialogueAcknowledged = false;
                ClearSaveLatch();
                adviceMode = false;
                AcquirePause();
                SyncGuideControl(true, false);
                PersistProgress();
                RefreshPresentation();
                return;
            }
            if (Progress.Completed)
            {
                AskAdvice(AdvisorTopic.City);
                return;
            }
            if (Progress.Skipped)
            {
                Resume();
                return;
            }
            Reopen();
        }

        public void AskAdvice(AdvisorTopic topic)
        {
            if (observedState == null || !Enum.IsDefined(typeof(AdvisorTopic), topic)) return;
            explicitlyOpened = true;
            CloseLesson(false);
            adviceTopic = topic;
            adviceText = TutorialAdvisor.Get(topic, observedState);
            adviceMode = true;
            SyncGuideControl(true, false);
            RefreshPresentation();
        }

        /// <summary>Opens the earliest currently unlocked lesson without waiting for auto timing.</summary>
        public bool OpenNextLesson() => TryOpenNextLesson(true);

        /// <summary>
        /// Opens any authored feature lesson selected from Danwoo's manual guide. Manual reading is
        /// intentionally available regardless of unlock, completion, seen, or automatic-tip state.
        /// </summary>
        public bool OpenLesson(GuidanceLessonId id)
        {
            GuidanceLessonDefinition lesson = GuidanceLessonCatalog.Get(id);
            if (observedState == null || IntroPlaying || lesson == null) return false;
            explicitlyOpened = true;
            return OpenLessonDefinition(lesson);
        }

        public void SetGuidanceEnabled(bool enabled)
        {
            if (Progress == null) return;
            Progress.SetGuidanceEnabled(enabled);
            if (!enabled)
            {
                CloseLesson(false);
                nextLessonEligibleAt = float.PositiveInfinity;
                SyncGuideControl(false, false);
            }
            else
            {
                explicitlyOpened = true;
                ScheduleNextLesson();
            }
            PersistProgress();
            RefreshPresentation();
        }

        public void DisableGuidance() => SetGuidanceEnabled(false);
        public void EnableGuidance() => SetGuidanceEnabled(true);

        public void SkipIntro()
        {
            if (townHallIntro != null && townHallIntro.IsPlaying) townHallIntro.Skip();
        }

        /// <summary>
        /// Receives only the result of an explicit player save. Tutorial-internal quiet saves must
        /// call Game.TrySaveGame directly and must never pass through this method.
        /// </summary>
        public void NotifySave(bool success)
        {
            if (!IsRunning || Step != TutorialStepId.SaveCity) return;
            saveSucceeded = success;
            successfulSaveVersion = success && observedState != null ? observedState.Version : -1;
            bool oldGoal = publishedGoalSatisfied;
            if (!TryAdvance() && oldGoal != GoalSatisfied) RefreshPresentation();
        }

        void OnGameChanged()
        {
            if (handlingGameChange || game == null) return;
            handlingGameChange = true;
            try
            {
                if (!ReferenceEquals(observedState, game.State))
                {
                    BindState(game.State, true);
                    return;
                }

                ObserveManualSpeedChange();
                bool oldGoal = publishedGoalSatisfied;
                if (!TryAdvance() && oldGoal != GoalSatisfied) RefreshPresentation();
            }
            finally
            {
                handlingGameChange = false;
            }
        }

        bool TryAdvance()
        {
            if (!IsRunning || !dialogueAcknowledged) return false;
            TutorialStepId previous = Progress.Step;
            if (!TutorialCatalog.TryAdvance(observedState, BuildFacts())) return false;

            AdvanceCount++;
            observedStep = Progress.Step;
            dialogueAcknowledged = false;
            ClearSaveLatch();
            adviceMode = false;

            if (Progress.Completed)
            {
                ReleaseOwnedPause();
                ScheduleNextLesson();
                SyncGuideControl(false, false);
            }
            else
            {
                EnterStep(previous, Progress.Step);
                SyncGuideControl(true, true);
            }

            PersistProgress();
            RefreshPresentation();
            return true;
        }

        TutorialFacts BuildFacts()
        {
            Cell selected = game != null ? game.SelectedCell : null;
            return new TutorialFacts
            {
                DialogueAcknowledged = dialogueAcknowledged,
                HasSelectedCell = selected != null,
                SelectedX = selected != null ? selected.X : -1,
                SelectedZ = selected != null ? selected.Z : -1,
                SelectedBuilding = selected != null ? selected.Building : BuildingKind.None,
                SaveSucceeded = saveSucceeded,
                SuccessfulSaveVersion = successfulSaveVersion
            };
        }

        void BindState(GameState state, bool allowInitialPause)
        {
            bindingState = true;
            ReleaseGuideControl();
            StopTownHallIntro();
            observedState = state;
            dialogueAcknowledged = false;
            ClearSaveLatch();
            adviceMode = false;
            CloseLesson(false);
            ownsPause = false;
            ownsLessonPause = false;
            observedSpeed = game != null ? game.GameSpeed : 1f;
            nextLessonEligibleAt = float.PositiveInfinity;

            if (Progress != null)
            {
                observedStep = Progress.Step;
                if (allowInitialPause && CanPresentAutomatically && Progress.Enabled && !Progress.Completed &&
                    !Progress.Skipped && Progress.Step != TutorialStepId.CompleteCropRotation)
                    AcquirePause();
                else if (Progress.Step == TutorialStepId.CompleteCropRotation)
                    ReleaseTutorialPauseForResearch();

                if (Progress.Completed && Progress.GuidanceEnabled) ScheduleNextLesson();
                TryStartFreshTownHallIntro();
            }
            bindingState = false;
            SyncGuideControl(!IntroPlaying, false);
            RefreshPresentation();
        }

        void EnterStep(TutorialStepId previous, TutorialStepId current)
        {
            if (previous == current) return;
            if (current == TutorialStepId.CompleteCropRotation) ReleaseTutorialPauseForResearch();
        }

        void AcquirePause()
        {
            if (game == null || Mathf.Approximately(game.GameSpeed, 0f))
            {
                // If already paused by the player, the tutorial does not own that pause.
                observedSpeed = game != null ? game.GameSpeed : 0f;
                return;
            }
            applyingTutorialSpeed = true;
            ownsPause = true;
            ownedPauseResumeSpeed = game.GameSpeed;
            game.SetSpeed(0f);
            observedSpeed = game.GameSpeed;
            applyingTutorialSpeed = false;
        }

        void ReleaseTutorialPauseForResearch()
        {
            if (!ownsPause) return;
            applyingTutorialSpeed = true;
            ownsPause = false;
            if (game != null && Mathf.Approximately(game.GameSpeed, 0f)) game.SetSpeed(ownedPauseResumeSpeed);
            observedSpeed = game != null ? game.GameSpeed : 1f;
            applyingTutorialSpeed = false;
        }

        void ReleaseOwnedPause()
        {
            if (!ownsPause) return;
            applyingTutorialSpeed = true;
            ownsPause = false;
            if (game != null && Mathf.Approximately(game.GameSpeed, 0f)) game.SetSpeed(ownedPauseResumeSpeed);
            observedSpeed = game != null ? game.GameSpeed : 1f;
            applyingTutorialSpeed = false;
        }

        void ObserveManualSpeedChange()
        {
            if (game == null) return;
            float speed = game.GameSpeed;
            if (!applyingTutorialSpeed && !Mathf.Approximately(speed, observedSpeed) && ownsPause)
                ownsPause = false;
            if (!applyingTutorialSpeed && !Mathf.Approximately(speed, observedSpeed) && ownsLessonPause)
                ownsLessonPause = false;
            observedSpeed = speed;
        }

        void ClearSaveLatch()
        {
            saveSucceeded = false;
            successfulSaveVersion = -1;
        }

        bool TryOpenNextLesson(bool manual)
        {
            if (observedState == null || Progress == null || !Progress.Completed || !Progress.GuidanceEnabled ||
                IntroPlaying || lessonMode) return false;
            if (!manual && (!CanPresentAutomatically || Time.unscaledTime < nextLessonEligibleAt || !IsIdleForGuidance()))
                return false;

            GuidanceLessonDefinition lesson = GuidanceLessonCatalog.GetNextUnseenUnlocked(observedState);
            if (lesson == null) return false;

            return OpenLessonDefinition(lesson);
        }

        bool OpenLessonDefinition(GuidanceLessonDefinition lesson)
        {
            if (lesson == null) return false;
            CloseLesson(false);
            adviceMode = false;
            currentLesson = lesson;
            lessonText = lesson.Pages == null ? "" : string.Join("\n\n", lesson.Pages);
            lessonMode = true;
            lessonCollapsed = false;
            LessonOpenCount++;
            AcquireLessonPause();
            SyncGuideControl(true, false);
            RefreshPresentation();
            return true;
        }

        void AcknowledgeLesson()
        {
            if (!lessonMode || currentLesson == null || observedState == null) return;
            GuidanceLessonId acknowledged = currentLesson.Id;
            bool marked = GuidanceLessonCatalog.MarkSeen(observedState, acknowledged);
            CloseLesson(false);
            if (marked)
            {
                LessonAcknowledgeCount++;
                PersistProgress();
            }
            ScheduleNextLesson();
            SyncGuideControl(false, false);
            RefreshPresentation();
        }

        void CloseLesson(bool scheduleNext)
        {
            if (!lessonMode && currentLesson == null) return;
            lessonMode = false;
            lessonCollapsed = false;
            currentLesson = null;
            lessonText = "";
            ReleaseLessonPause();
            if (scheduleNext) ScheduleNextLesson();
        }

        void AcquireLessonPause()
        {
            if (game == null || ownsLessonPause || Mathf.Approximately(game.GameSpeed, 0f))
            {
                observedSpeed = game != null ? game.GameSpeed : 0f;
                return;
            }
            lessonResumeSpeed = game.GameSpeed;
            applyingTutorialSpeed = true;
            ownsLessonPause = true;
            game.SetSpeed(0f);
            observedSpeed = game.GameSpeed;
            applyingTutorialSpeed = false;
        }

        void ReleaseLessonPause()
        {
            if (!ownsLessonPause) return;
            applyingTutorialSpeed = true;
            ownsLessonPause = false;
            if (game != null && Mathf.Approximately(game.GameSpeed, 0f)) game.SetSpeed(lessonResumeSpeed);
            observedSpeed = game != null ? game.GameSpeed : lessonResumeSpeed;
            applyingTutorialSpeed = false;
        }

        void ScheduleNextLesson()
        {
            nextLessonEligibleAt = Time.unscaledTime + MinimumLessonIntervalSeconds;
        }

        void DelayGuidanceAfterManualPanel()
        {
            if (Progress != null && Progress.Completed && Progress.GuidanceEnabled) ScheduleNextLesson();
        }

        /// <summary>Explicit UI action that finds and follows the in-world guide without teleporting.</summary>
        public bool FocusGuide()
        {
            if (!GuideNarrationActive || !EnsureGuideControl()) return false;
            Transform target = game.Citizens.GuideTransform;
            if (target == null)
            {
                TutorialStepDefinition definition = IsRunning ? CurrentDefinition : null;
                if (definition != null && definition.PreferredX >= 0 && definition.PreferredZ >= 0)
                    RouteGuideNear(definition.PreferredX, definition.PreferredZ);
                else if (TryFindTownHall(out int hallX, out int hallZ))
                    RouteGuideNear(hallX, hallZ);
                target = game.Citizens.GuideTransform;
            }
            if (target == null || game.CameraRig == null || game.CameraRig.Cinematics == null) return false;
            stopFollowingWhenGuideArrives = false;
            game.CameraRig.Cinematics.FollowTarget(target, 2.8f);
            bool following = game.CameraRig.Cinematics.IsFollowing &&
                             game.CameraRig.Cinematics.FollowTargetTransform == target;
            if (following) GuideFocusCount++;
            return following;
        }

        bool GuideNarrationActive => !IntroPlaying && (IsRunning || adviceMode || lessonMode);

        void SyncGuideControl(bool requestFocus, bool routeToCurrentStep)
        {
            if (!GuideNarrationActive)
            {
                ReleaseGuideControl();
                return;
            }
            if (!EnsureGuideControl())
            {
                ReleaseGuideControl();
                return;
            }

            stopFollowingWhenGuideArrives = false;
            if (routeToCurrentStep && IsRunning)
            {
                TutorialStepDefinition definition = CurrentDefinition;
                if (definition != null && definition.PreferredX >= 0 && definition.PreferredZ >= 0)
                    RouteGuideNear(definition.PreferredX, definition.PreferredZ);
            }
            // A released normal resident may currently be indoors. For a newly opened narrative
            // with no spatial lesson target, bring Danwoo out along real roads near the Town Hall.
            if (requestFocus && game.Citizens.GuideTransform == null && TryFindTownHall(out int hallX, out int hallZ))
                RouteGuideNear(hallX, hallZ);
            if (requestFocus) FocusGuide();
        }

        bool RouteGuideNear(int x, int z)
        {
            if (game == null || game.Citizens == null || !game.Citizens.GuideToNear(x, z)) return false;
            GuideRouteCount++;
            return true;
        }

        bool TryFindTownHall(out int x, out int z)
        {
            x = z = -1;
            if (observedState?.Cells == null) return false;
            foreach (Cell cell in observedState.Cells)
            {
                if (cell == null || cell.Building != BuildingKind.TownHall) continue;
                x = cell.X;
                z = cell.Z;
                return true;
            }
            return false;
        }

        bool EnsureGuideControl()
        {
            if (game == null || game.Citizens == null || !game.Citizens.EnsureGuide()) return false;
            bool accepted = game.Citizens.SetGuideControl(true);
            return accepted || (game.Citizens.Simulation != null && game.Citizens.Simulation.GuideControlled);
        }

        void PrepareTaskView()
        {
            // Let the camera accompany Danwoo to the task, then leave the player's chosen view
            // anchored there. Manual pan/zoom may cancel the follow sooner and is never restarted.
            stopFollowingWhenGuideArrives = true;
            stopFollowingDeadline = Time.unscaledTime + 12f;
            UpdateGuideFollowForTask();
        }

        void UpdateGuideFollowForTask()
        {
            if (!stopFollowingWhenGuideArrives) return;
            if (!GuideFollowing)
            {
                stopFollowingWhenGuideArrives = false;
                return;
            }
            if (GuideWalking && GuideAvailable && Time.unscaledTime < stopFollowingDeadline) return;
            StopFollowingGuide();
        }

        void StopFollowingGuide()
        {
            stopFollowingWhenGuideArrives = false;
            if (game != null && game.CameraRig != null && game.CameraRig.Cinematics != null)
                game.CameraRig.Cinematics.StopFollowing(false);
        }

        void ReleaseGuideControl()
        {
            StopFollowingGuide();
            if (game != null && game.Citizens != null) game.Citizens.SetGuideControl(false);
        }

        bool IsIdleForGuidance()
        {
            if (game == null || game.HelpOpen || game.ResearchOpen || game.ModalOpen) return false;
            if (game.SelectedTool != BuildingKind.None || game.DemolitionMode || game.FactoryToolActive) return false;
            // Resident AI setup owns ModalOpen, so it is covered by the same native modal gate.
            return true;
        }

        void EnsureTownHallIntro()
        {
            if (game == null) return;
            if (townHallIntro == null) townHallIntro = gameObject.GetComponent<TownHallIntro>();
            if (townHallIntro == null) townHallIntro = gameObject.AddComponent<TownHallIntro>();
            townHallIntro.Finished -= OnTownHallIntroFinished;
            townHallIntro.Initialize(game);
            townHallIntro.Finished += OnTownHallIntroFinished;
        }

        void TryStartFreshTownHallIntro()
        {
            if (townHallIntro == null || Progress == null || !CanPresentAutomatically || !Progress.Enabled ||
                Progress.Completed || Progress.Skipped || Progress.Step != TutorialStepId.Welcome ||
                observedState.Day != 0 || Progress.TownHallIntroPlayed) return;
            if (!Progress.MarkTownHallIntroPlayed()) return;

            // Commit the one-shot flag before presentation begins. A failed quiet save is surfaced
            // through PersistResult/PersistError without replaying the effect in this session.
            PersistProgress();
            StartCoroutine(townHallIntro.PlayArrival());
        }

        void StopTownHallIntro()
        {
            if (townHallIntro != null && townHallIntro.IsPlaying) townHallIntro.Skip();
        }

        void OnTownHallIntroFinished()
        {
            if (bindingState || game == null || !ReferenceEquals(observedState, game.State)) return;
            // Retry the already-committed flag after completion/skip as well. SaveStore avoids a
            // physical rewrite when the pre-play save is still current, while a transient failure
            // cannot make a finished one-shot replay on the next launch.
            PersistProgress();
            SyncGuideControl(true, false);
            RefreshPresentation();
        }

        void PersistProgress()
        {
            PersistAttemptCount++;
            if (game == null)
            {
                PersistResult = false;
                PersistError = "게임이 준비되지 않았습니다.";
                return;
            }
            PersistResult = game.TrySaveGame(out string error);
            PersistError = PersistResult ? "" : error ?? "";
        }

        void RefreshPresentation()
        {
            publishedGoalSatisfied = GoalSatisfied;
            RefreshTargetOutline();
            Changed?.Invoke();
        }

        void EnsureDialogue()
        {
            if (game == null || game.CityHud == null || dialogue != null) return;
            var host = new GameObject("Danwoo tutorial dialogue");
            host.transform.SetParent(game.CityHud.transform, false);
            dialogue = host.AddComponent<TutorialDialogue>();
            dialogue.Initialize(this, game.CityHud.transform);
        }

        void EnsureTargetOutline()
        {
            if (targetOutline != null) return;
            var marker = new GameObject("Tutorial target outline");
            marker.transform.SetParent(transform, false);
            targetOutline = marker.AddComponent<LineRenderer>();
            targetOutline.useWorldSpace = true;
            targetOutline.loop = true;
            targetOutline.positionCount = 4;
            targetOutline.startWidth = .075f;
            targetOutline.endWidth = .075f;
            targetOutline.numCornerVertices = 2;
            targetOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            targetOutline.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                targetMaterial = new Material(shader) { name = "Tutorial target material" };
                targetMaterial.color = new Color(.20f, .95f, .80f, .95f);
                targetOutline.sharedMaterial = targetMaterial;
            }
            targetOutline.enabled = false;
        }

        void RefreshTargetOutline()
        {
            if (targetOutline == null) return;
            TutorialStepDefinition definition = CurrentDefinition;
            // Keep the world cue visible after the dialogue collapses so the player can carry
            // out the requested action with an unobstructed view of the city.
            bool show = IsRunning && !adviceMode && !lessonMode && definition != null &&
                        definition.PreferredX >= 0 && definition.PreferredZ >= 0;
            targetOutline.enabled = show;
            if (!show) return;

            Vector3 center = BoardView.Position(definition.PreferredX, definition.PreferredZ) + Vector3.up * .15f;
            const float half = .48f;
            targetOutline.SetPosition(0, center + new Vector3(-half, 0f, -half));
            targetOutline.SetPosition(1, center + new Vector3(-half, 0f, half));
            targetOutline.SetPosition(2, center + new Vector3(half, 0f, half));
            targetOutline.SetPosition(3, center + new Vector3(half, 0f, -half));
        }

        static string AdviceTitle(AdvisorTopic topic)
        {
            switch (topic)
            {
                case AdvisorTopic.City: return "도시 운영 조언";
                case AdvisorTopic.Production: return "생산 조언";
                case AdvisorTopic.Research: return "연구 조언";
                case AdvisorTopic.Factory: return "산업 설비 조언";
                case AdvisorTopic.Territory: return "영토 조언";
                default: return "단우의 조언";
            }
        }

        static bool HasCommandLineArgument(string value) =>
            Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;

        void OnDestroy()
        {
            if (game != null) game.Changed -= OnGameChanged;
            if (townHallIntro != null)
            {
                townHallIntro.Finished -= OnTownHallIntroFinished;
                townHallIntro.Skip();
            }
            ReleaseGuideControl();
            ReleaseLessonPause();
            if (targetMaterial != null) Destroy(targetMaterial);
        }
    }
}

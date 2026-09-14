using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>
    /// Opt-in native runtime journey for the complete Danwoo tutorial. The fixture starts from the
    /// ordinary GameState.CreateNew economy and performs every player action through live HUD and
    /// world-pointer paths. It never targets the player's save file.
    /// </summary>
    public sealed class TutorialSmokeTest : MonoBehaviour
    {
        const string SmokeArgument = "-riverworks-tutorial-smoke";
        const int MaximumErrors = 80;
        const int MaximumErrorCharacters = 4096;
        const int TypingFrameCount = 24;

        static readonly string[] ExpectedStableIds =
        {
            "welcome",
            "select-town-hall",
            "extend-connected-road",
            "build-connected-house",
            "build-connected-lumberyard",
            "build-connected-study-house",
            "start-crop-rotation",
            "complete-crop-rotation",
            "mechanical-power-advice",
            "save-city",
            "finish"
        };

        GameController game;
        TutorialDirector director;
        TutorialDialogue dialogue;
        Hud hud;
        string output;
        bool finished;
        bool originalReducedMotion;
        float originalTimeScale = 1f;
        int suppressedErrors;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();

        sealed class IntroMotionSample
        {
            public float MaximumVerticalDelta;
            public bool FeedbackObserved;
        }

        public void Initialize(GameController controller)
        {
            game = controller;
            originalReducedMotion = FeelUiFeedback.ReducedMotion;
            originalTimeScale = Time.timeScale;
            string[] arguments = Environment.GetCommandLineArgs();
            int outputAt = Array.IndexOf(arguments, "-riverworks-output");
            output = outputAt >= 0 && outputAt + 1 < arguments.Length
                ? arguments[outputAt + 1]
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "TutorialSmoke"));
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
            StartCoroutine(GuardedRun());
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = originalTimeScale;
            FeelUiFeedback.ReducedMotion = originalReducedMotion;
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                RecordError(message + "\n" + trace);
        }

        IEnumerator GuardedRun()
        {
            yield return SmokeExecution.Run(RunSteps(), exception => RecordError(exception.ToString()));
            Finish();
        }

        IEnumerator RunSteps()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            Require(Array.IndexOf(arguments, SmokeArgument) >= 0,
                "tutorial runtime smoke only runs behind its explicit command flag");
            Require(game != null && game.Sim != null, "GameController supplied a live simulation");
            Require(game.SmokeMode, "tutorial runtime smoke uses smoke mode");
            Require(!string.Equals(Path.GetFileName(game.SavePath), "city-v1.json", StringComparison.OrdinalIgnoreCase),
                "tutorial runtime smoke never targets the player's city-v1.json");
            Require(Path.GetFullPath(game.SavePath).StartsWith(Path.GetFullPath(Application.persistentDataPath),
                    StringComparison.OrdinalIgnoreCase),
                "tutorial save remains inside the application persistent-data directory");

            Time.timeScale = 1f;
            FeelUiFeedback.ReducedMotion = false;
            hud = game.CityHud;
            director = game.Tutorial;
            dialogue = hud == null ? null : hud.GetComponentInChildren<TutorialDialogue>(true);

            Require(hud != null && hud.gameObject.activeInHierarchy, "the live city HUD is active");
            Require(director != null && director.Game == game, "the tutorial director observes the live controller");
            Require(dialogue != null, "the live Danwoo dialogue is attached to the existing HUD");
            dialogue.Initialize(director, hud.transform);
            Require(EventSystem.current != null, "an EventSystem is available for native pointer events");
            Require(game.Feel != null && !FeelUiFeedback.ReducedMotion,
                "normal Feel feedback remains enabled during tutorial smoke");
            Require(game.ResidentAi != null && !game.ResidentAi.RootTestEndpoint &&
                    game.ResidentAi.CurrentNetworkMode == ResidentAiNetworkMode.Rule,
                "unrelated resident AI remains in network-off rule mode during tutorial smoke");
            Require(FindObjectsByType<Canvas>().Length == 1,
                "the tutorial reuses the single existing Canvas");
            VerifyCatalogAndFreshCity();

            yield return VerifyTownHallIntro();
            yield return VerifyInWorldGuide();

            yield return SetResolution(1600, 900);
            Require(director.IsRunning && director.Step == TutorialStepId.Welcome,
                "a fresh city automatically opens the welcome step");
            Require(dialogue.Visible, "the welcome dialogue is rendered");
            Require(Mathf.Approximately(game.GameSpeed, 0f), "the welcome step owns an initial game-speed pause");
            VerifyDialogueGeometry();

            int beforeTyping = dialogue.RevealedCharacters;
            yield return new WaitForSecondsRealtime(.18f);
            yield return new WaitForEndOfFrame();
            Require(dialogue.Typing && dialogue.RevealedCharacters > beforeTyping,
                "typewriter text advances while game speed is paused");
            Require(dialogue.RevealedCharacters < TextElementCount(dialogue.CurrentPageText) &&
                    dialogue.DisplayedText.Length < dialogue.CurrentPageText.Length,
                "the welcome line has a measurable partial-text state");
            Require(dialogue.CurrentPageText.StartsWith(dialogue.DisplayedText, StringComparison.Ordinal),
                "partial dialogue is always a prefix of the full Korean line");
            Require(dialogue.CurrentPageFits,
                "the Korean pixel-font welcome page fits its rendered dialogue body");

            yield return CaptureAtCurrentResolution("1600x900-01-intro-typing.png");
            yield return SetResolution(1280, 720);
            Require(dialogue.Typing, "welcome remains partially typed after the 1280 layout settles");
            yield return CaptureAtCurrentResolution("1280x720-01-intro-typing.png");
            yield return RecordTypingFrames();
            Require(dialogue.Typing && dialogue.RevealedCharacters < TextElementCount(dialogue.CurrentPageText),
                "24 captured typewriter frames remain an in-progress sequence");

            int advanceBeforeReveal = director.AdvanceCount;
            int pageBeforeReveal = dialogue.PageIndex;
            yield return ClickAndSettle("Button_TutorialAdvance");
            Require(!dialogue.Typing && dialogue.DisplayedText == dialogue.CurrentPageText,
                "the first dialogue tap reveals only the complete current page");
            Require(dialogue.PageIndex == pageBeforeReveal,
                "the reveal-all tap does not move to another dialogue page");
            Require(director.Step == TutorialStepId.Welcome && director.AdvanceCount == advanceBeforeReveal,
                "the reveal-all tap does not advance the tutorial step");
            yield return CapturePair("02-intro-full");

            int welcomeRouteBefore = director.GuideRouteCount;
            int welcomeFocusBefore = director.GuideFocusCount;
            yield return FinishDialogueAndContinue(TutorialStepId.Welcome);
            Require(director.Step == TutorialStepId.SelectTownHall,
                "the second welcome tap advances to the first guarded task");
            Require(director.GuideRouteCount == welcomeRouteBefore + 1 &&
                    director.GuideFocusCount == welcomeFocusBefore + 1 && director.GuideFollowing &&
                    game.CameraRig.Cinematics.FollowTargetTransform == game.Citizens.GuideTransform,
                "the guarded spatial step routes and follows the actual Danwoo resident exactly once");
            Require(!game.State.Tutorial.Completed, "pending progress is not prematurely complete");
            VerifyPersistedStep(TutorialStepId.SelectTownHall, false, "welcome transition");

            game.LoadGame();
            yield return WaitForPresentation();
            Require(director.Step == TutorialStepId.SelectTownHall && director.IsRunning,
                "loading the quiet progress save resumes the pending step");
            Require(Mathf.Approximately(game.GameSpeed, 0f),
                "loading a pending tutorial reacquires its owned pause");

            yield return ClickAndSettle("Button_TutorialSkip");
            Require(director.IsSkipped && !director.IsRunning && game.State.Tutorial.Step == TutorialStepId.SelectTownHall,
                "skip preserves the current pending step");
            Require(Mathf.Approximately(game.GameSpeed, 1f), "skip releases the tutorial-owned pause");
            VerifyPersistedStep(TutorialStepId.SelectTownHall, false, "skip", false);
            yield return ClickAndSettle("Button_TutorialReopen");
            Require(director.IsRunning && !director.IsSkipped && director.Step == TutorialStepId.SelectTownHall,
                "the persistent guide chip explicitly resumes a skipped tutorial");
            game.SetSpeed(0f);
            yield return new WaitForSecondsRealtime(.14f);
            Require(dialogue.Typing && dialogue.RevealedCharacters > 0,
                "resumed dialogue also types with the simulation paused");

            yield return AcknowledgeIncompleteTask(TutorialStepId.SelectTownHall, "town-hall selection");
            yield return CapturePair("03-task-collapsed");
            yield return WorldClick(10, 10, "select the actual town hall");
            Require(game.SelectedCell != null && game.SelectedCell.Building == BuildingKind.TownHall,
                "world pointer selection owns the real town-hall cell");
            Require(director.Step == TutorialStepId.ExtendRoad,
                "town-hall selection satisfies its guarded goal and advances");
            VerifyPersistedStep(TutorialStepId.ExtendRoad, false, "town-hall selection");

            yield return BuildThroughHud(TutorialStepId.ExtendRoad, "Button_도시", BuildingKind.Road, 10, 7);
            yield return BuildThroughHud(TutorialStepId.BuildHouse, "Button_주거", BuildingKind.House, 9, 7);
            yield return BuildThroughHud(TutorialStepId.BuildLumberyard, "Button_생산", BuildingKind.Lumberyard, 7, 10);
            yield return BuildThroughHud(TutorialStepId.BuildStudyHouse, "Button_도시", BuildingKind.StudyHouse, 11, 8);
            Require(director.Step == TutorialStepId.StartCropRotation,
                "the four real connected construction goals reach research");

            yield return AcknowledgeIncompleteTask(TutorialStepId.StartCropRotation, "crop-rotation start");
            Require(game.State.ActiveResearch == TechId.None && !TechCatalog.Has(game.State, TechId.CropRotation),
                "crop rotation is neither active nor completed before its rendered research action");
            yield return ClickAndSettle("Button_기술 연구");
            Require(game.ResearchOpen && !dialogue.Visible,
                "the research modal obscures the non-modal dialogue");
            yield return ClickAndSettle("Button_연구 시작_CropRotation", .35f);
            Require(game.State.ActiveResearch == TechId.CropRotation,
                "the live research button starts crop rotation");
            Require(director.Step == TutorialStepId.CompleteCropRotation,
                "only an actually active crop-rotation project clears the start goal");
            VerifyPersistedStep(TutorialStepId.CompleteCropRotation, false, "crop-rotation start");
            Require(!TechCatalog.Has(game.State, TechId.CropRotation),
                "starting research does not satisfy the completion step");
            Require(!director.GoalSatisfied, "the completion goal stays guarded while research is active");

            game.SetSpeed(1f);
            int researchStartDay = game.State.Day;
            Require(Mathf.Approximately(game.GameSpeed, 1f), "research proceeds at the legitimate 1x game speed");
            float researchDeadline = Time.realtimeSinceStartup + GameController.SecondsPerDay *
                TechCatalog.Get(TechId.CropRotation).DurationDays + 2.5f;
            while (!TechCatalog.Has(game.State, TechId.CropRotation) && Time.realtimeSinceStartup < researchDeadline)
                yield return null;
            yield return new WaitForEndOfFrame();
            Require(TechCatalog.Has(game.State, TechId.CropRotation),
                "crop rotation completes through real-time GameController day ticks");
            Require(game.State.Day >= researchStartDay + TechCatalog.Get(TechId.CropRotation).DurationDays,
                "the required research days elapsed rather than being injected");
            Require(director.Step == TutorialStepId.CompleteCropRotation && director.GoalSatisfied,
                "completed research only satisfies the current step and waits for acknowledgement");

            yield return RevealAndContinue(TutorialStepId.CompleteCropRotation);
            Require(director.Step == TutorialStepId.MechanicalPowerAdvice,
                "completed crop rotation reaches the manual mechanical-power advice");
            VerifyPersistedStep(TutorialStepId.MechanicalPowerAdvice, false, "crop-rotation completion");
            Require(director.Text.Contains("기계 동력") && director.Text.Contains("42×42"),
                "Danwoo's rendered mechanical-power advice names the next technology and factory grid");
            yield return CapturePair("04-mechanical-power-advice");
            yield return RevealAndContinue(TutorialStepId.MechanicalPowerAdvice);
            Require(director.Step == TutorialStepId.SaveCity, "acknowledging advice reaches manual save");
            VerifyPersistedStep(TutorialStepId.SaveCity, false, "mechanical-power advice");

            yield return AcknowledgeIncompleteTask(TutorialStepId.SaveCity, "manual save");
            int validStockCount = game.State.Stock.Count;
            string economyBeforeInvalidSave = EconomySignature(game.State);
            int advanceBeforeManualSave = director.AdvanceCount;
            game.State.Stock.Add(0f);
            yield return ClickAndSettle("Button_Menu");
            Require(game.ModalOpen, "the rendered menu opens for the manual-save gate");
            yield return ClickAndSettle("Button_저장", .3f);
            Require(director.Step == TutorialStepId.SaveCity && !director.GoalSatisfied,
                "a failed manual save cannot fulfill the save goal");
            Require(director.AdvanceCount == advanceBeforeManualSave,
                "the failed manual-save callback does not increment progress");
            Require(game.Notice.StartsWith("저장 실패:", StringComparison.Ordinal),
                "the intentionally invalid isolated save reports failure to the player");
            game.State.Stock.RemoveAt(game.State.Stock.Count - 1);
            Require(game.State.Stock.Count == validStockCount,
                "the intentionally invalid stock buffer is restored after the isolated failure test");
            Require(EconomySignature(game.State) == economyBeforeInvalidSave,
                "the failed-save fixture leaves every economy value unchanged after restoration");

            yield return ClickAndSettle("Button_저장", .35f);
            Require(director.Step == TutorialStepId.Finish && !director.IsCompleted,
                "a successful rendered manual save reaches Finish without completing it early");
            Require(director.AdvanceCount == advanceBeforeManualSave + 1,
                "the successful manual-save callback advances exactly once");
            Require(File.Exists(game.SavePath), "manual save creates the isolated tutorial save file");
            yield return ClickAndSettle("Button_MenuClose");
            Require(!game.ModalOpen && dialogue.Visible, "closing the menu restores the Finish dialogue");
            VerifyPersistedStep(TutorialStepId.Finish, false, "successful manual save");

            yield return RevealAndContinue(TutorialStepId.Finish);
            Require(director.IsCompleted && !director.IsRunning,
                "the last Danwoo acknowledgement completes the tutorial");
            Require(game.State.Tutorial.Completed && !game.State.Tutorial.Enabled && !game.State.Tutorial.Skipped,
                "completed progress has consistent durable terminal flags");
            VerifyPersistedStep(TutorialStepId.Finish, true, "completion");
            yield return CapturePair("05-completion");

            yield return ClickAndSettle("Button_TutorialReopen");
            Require(director.IsAdviceMode && director.AdviceTopic == AdvisorTopic.City,
                "the completed guide chip opens Danwoo's local city advice");
            yield return VerifyAdviceTopics();
            yield return CapturePair("06-advice");

            GuidanceLessonDefinition postBasicLesson = GuidanceLessonCatalog.GetNextUnseenUnlocked(game.State);
            Require(postBasicLesson != null && postBasicLesson.Id == GuidanceLessonId.OptionalResearch,
                "completed basic play unlocks the first relevant unseen lesson without forcing a campaign");
            yield return VerifyGuidanceLessons();

            string economyBeforeReplay = EconomySignature(game.State);
            int roadsBeforeReplay = ConnectedCount(game.State, BuildingKind.Road);
            int housesBeforeReplay = ConnectedCount(game.State, BuildingKind.House);
            int lumberBeforeReplay = ConnectedCount(game.State, BuildingKind.Lumberyard);
            int studiesBeforeReplay = ConnectedCount(game.State, BuildingKind.StudyHouse);
            yield return ClickAndSettle("Button_TutorialRestart");
            Require(director.Step == TutorialStepId.Welcome && director.IsRunning,
                "the advice panel's rendered restart button begins a replay");
            Require(EconomySignature(game.State) == economyBeforeReplay,
                "replay leaves the existing city's economy and buildings untouched");
            Require(game.State.Tutorial.RoadBaseline == roadsBeforeReplay &&
                    game.State.Tutorial.HouseBaseline == housesBeforeReplay &&
                    game.State.Tutorial.LumberyardBaseline == lumberBeforeReplay &&
                    game.State.Tutorial.StudyHouseBaseline == studiesBeforeReplay,
                "replay snapshots all current connected-building baselines");
            TutorialStepId replayStep = game.State.Tutorial.Step;
            game.State.Tutorial.Step = TutorialStepId.ExtendRoad;
            Require(!TutorialCatalog.EvaluateGoal(game.State, new TutorialFacts()),
                "replay baselines cannot instantly clear a future construction goal");
            game.State.Tutorial.Step = replayStep;

            yield return VerifyLegacyNullFlow();
            Check(results.Count(line => line.StartsWith("PASS ", StringComparison.Ordinal)) >= 80,
                "runtime journey records at least 80 independently named checks");
            game.SetSpeed(0f);
        }

        IEnumerator VerifyTownHallIntro()
        {
            TownHallIntro intro = director.Intro;
            Require(intro != null && director.IntroPlaying && intro.IsPlaying,
                "a fresh city starts the one-time Town Hall arrival before basic dialogue");
            Require(IsActive("Button_IntroSkip") && !dialogue.Visible,
                "the intro exposes only its rendered 44-pixel skip control");
            Collider[] groundColliders = GroundColliders();
            Require(groundColliders.Length == game.State.Size * game.State.Size,
                "the intro begins over all 441 live ground colliders");
            Transform skippedTarget = intro.Target;
            Vector3 skippedBasePosition = intro.BaseLocalPosition;
            Vector3 skippedBaseScale = intro.BaseLocalScale;
            int skippedPlayCount = intro.PlayCount;
            director.SkipIntro();
            yield return null;
            Require(!director.IntroPlaying && !intro.IsPlaying,
                "the startup intro can be cleared through its API before the first graphics render");
            Require(skippedTarget != null && Approximately(skippedTarget.localPosition, skippedBasePosition) &&
                    Approximately(skippedTarget.localScale, skippedBaseScale),
                "startup API skip restores the Town Hall position and scale exactly");
            Require(game.State.Tutorial.TownHallIntroPlayed,
                "the skipped one-shot intro remains marked as played");
            VerifyIntroPersisted("startup API intro skip");

            yield return SetResolution(1600, 900);
            yield return new WaitForSecondsRealtime(.2f);
            game.NewGame();
            yield return new WaitForEndOfFrame();
            intro = director.Intro;
            Require(director.IntroPlaying && intro.IsPlaying && intro.PlayCount == skippedPlayCount + 1,
                "a warm-window NewGame starts the rendered skip-button case");
            Transform nativeSkipTarget = intro.Target;
            Vector3 nativeSkipBasePosition = intro.BaseLocalPosition;
            Vector3 nativeSkipBaseScale = intro.BaseLocalScale;
            yield return ClickAndSettle("Button_IntroSkip", .12f);
            Require(!director.IntroPlaying && !intro.IsPlaying,
                "the rendered native intro skip ends the warm-window arrival");
            Require(nativeSkipTarget != null && Approximately(nativeSkipTarget.localPosition, nativeSkipBasePosition) &&
                    Approximately(nativeSkipTarget.localScale, nativeSkipBaseScale),
                "rendered intro skip restores the Town Hall transform");
            VerifyIntroPersisted("rendered intro skip");

            int nativeSkipPlayCount = intro.PlayCount;
            int arrivalCueBefore = game.Feel.CountFor(FeelCue.TownHallArrival);
            game.NewGame();
            yield return null;
            intro = director.Intro;
            Require(director.IntroPlaying && intro.IsPlaying && intro.PlayCount == nativeSkipPlayCount + 1,
                "a smoke-isolated NewGame starts a warm-window arrival run");
            int introAdvanceCount = director.AdvanceCount;
            director.Continue();
            Require(director.IntroPlaying && director.Step == TutorialStepId.Welcome &&
                    director.AdvanceCount == introAdvanceCount,
                "basic dialogue acknowledgement is guarded until the arrival finishes");
            Require(intro.Target != null && intro.TargetName.Contains("Details 10 10") &&
                    game.Board.ModelAt(10, 10) == intro.Target,
                "the arrival targets the rendered Town Hall model");
            Require(intro.FeedbackPlaying && intro.GetComponentsInChildren<MMF_Player>(true)
                    .Any(player => player != null && player.name == "Town Hall arrival MMF player" && player.IsPlaying),
                "the Town Hall arrival is driven by a live native MMF_Player");

            Transform normalTarget = intro.Target;
            Vector3 normalBasePosition = intro.BaseLocalPosition;
            Vector3 normalBaseScale = intro.BaseLocalScale;
            var normalSample = new IntroMotionSample();
            yield return RecordIntroFrames(intro, normalSample);
            Require(normalSample.FeedbackObserved,
                "the 24-frame intro sequence observes MMF feedback in flight");
            Require(normalSample.MaximumVerticalDelta > .15f,
                "a mid-intro Town Hall frame is meaningfully above its authored base Y");
            yield return WaitForIntroToFinish(intro, 3f);
            Require(!director.IntroPlaying && game.State.Tutorial.TownHallIntroPlayed,
                "the normal arrival completes and commits its one-shot flag");
            Require(game.Feel.CountFor(FeelCue.TownHallArrival) == arrivalCueBefore + 1,
                "normal landing emits exactly one TownHallArrival Feel cue");
            Require(normalTarget != null && Approximately(normalTarget.localPosition, normalBasePosition) &&
                    Approximately(normalTarget.localScale, normalBaseScale),
                "normal arrival completion restores the Town Hall transform");
            Require(groundColliders.SequenceEqual(GroundColliders()),
                "normal arrival leaves every ground collider instance unchanged");
            VerifyIntroPersisted("normal intro completion");

            int oneShotPlayCount = intro.PlayCount;
            game.LoadGame();
            yield return WaitForPresentation();
            Require(!director.IntroPlaying && director.Intro.PlayCount == oneShotPlayCount,
                "loading a city with TownHallIntroPlayed does not replay the arrival");
            Require(game.State.Tutorial.TownHallIntroPlayed,
                "the loaded city retains the one-time intro flag");

            FeelUiFeedback.ReducedMotion = true;
            int reducedCueBefore = game.Feel.CountFor(FeelCue.TownHallArrival);
            game.NewGame();
            yield return null;
            TownHallIntro reducedIntro = director.Intro;
            Require(reducedIntro.IsPlaying && reducedIntro.FeedbackPlaying,
                "reduced-motion NewGame still presents a short native arrival cue");
            Transform reducedTarget = reducedIntro.Target;
            Vector3 reducedBasePosition = reducedIntro.BaseLocalPosition;
            Vector3 reducedBaseScale = reducedIntro.BaseLocalScale;
            float maximumReducedDrop = Mathf.Abs(reducedIntro.CurrentLocalY - reducedIntro.BaseLocalY);
            float reducedDeadline = Time.realtimeSinceStartup + 2f;
            while (reducedIntro.IsPlaying && Time.realtimeSinceStartup < reducedDeadline)
            {
                maximumReducedDrop = Mathf.Max(maximumReducedDrop,
                    Mathf.Abs(reducedIntro.CurrentLocalY - reducedIntro.BaseLocalY));
                yield return null;
            }
            Require(!reducedIntro.IsPlaying, "the reduced-motion arrival finishes within its short bound");
            Require(game.Feel.CountFor(FeelCue.TownHallArrival) == reducedCueBefore + 1,
                "reduced arrival keeps one meaningful landing cue");
            Require(maximumReducedDrop <= .01f,
                "reduced motion avoids a hard vertical Town Hall drop");
            Require(reducedTarget != null && Approximately(reducedTarget.localPosition, reducedBasePosition) &&
                    Approximately(reducedTarget.localScale, reducedBaseScale),
                "reduced-motion completion restores the Town Hall transform");
            Require(groundColliders.SequenceEqual(GroundColliders()),
                "skip, normal, load, and reduced arrivals never replace ground colliders");
            VerifyIntroPersisted("reduced-motion intro completion");

            FeelUiFeedback.ReducedMotion = false;
            dialogue.Initialize(director, hud.transform);
            yield return new WaitForEndOfFrame();
            Require(director.IsRunning && director.Step == TutorialStepId.Welcome && dialogue.Visible,
                "basic welcome dialogue starts only after the arrival has finished");
            Require(game.State.Day == 0 && game.State.Coins == 1100 &&
                    game.State.Stock[(int)Resource.Timber] == 90 &&
                    game.State.Stock[(int)Resource.Stone] == 65 && game.State.ResearchPoints == 10,
                "all intro variants leave the normal CreateNew economy untouched");
        }

        IEnumerator RecordIntroFrames(TownHallIntro intro, IntroMotionSample sample)
        {
            string directory = Path.Combine(output, "intro-frames");
            Directory.CreateDirectory(directory);
            for (int index = 0; index < TypingFrameCount; index++)
            {
                sample.MaximumVerticalDelta = Mathf.Max(sample.MaximumVerticalDelta,
                    Mathf.Abs(intro.CurrentLocalY - intro.BaseLocalY));
                sample.FeedbackObserved |= intro.FeedbackPlaying;
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null) RecordError("Intro frame returned no texture at index " + index);
                else
                {
                    try
                    {
                        File.WriteAllBytes(Path.Combine(directory, "frame-" + index.ToString("D3") + ".png"),
                            frame.EncodeToPNG());
                    }
                    catch (Exception exception)
                    {
                        RecordError("Could not write intro frame " + index + ": " + exception.Message);
                    }
                    Destroy(frame);
                }
            }
            results.Add("CAPTURE 24 Town Hall arrival frames at " + Screen.width + "x" + Screen.height);
        }

        IEnumerator VerifyInWorldGuide()
        {
            CitizenView citizens = game.Citizens;
            Require(citizens != null && citizens.Simulation != null && citizens.GuideAvailable &&
                    director.GuideAvailable,
                "completed arrival creates the in-world Danwoo guide from the live citizen simulation");
            CitizenSimulation citizenSimulation = citizens.Simulation;
            Citizen guide = citizenSimulation.GuideCitizen;
            int populationBefore = game.State.Population;
            Require(guide != null && guide.IsGuide && guide.Id == 1 && guide.Name == "단우" &&
                    guide.Persona == "친절한 서기",
                "the existing first resident is styled and named as clerk Danwoo");
            Require(citizenSimulation.ResidentCount == populationBefore &&
                    citizenSimulation.Residents.Count(resident => resident.IsGuide) == 1 &&
                    citizenSimulation.Residents.Contains(guide),
                "Danwoo remains the same resident without changing population");
            Transform guideTransform = citizens.GuideTransform;
            CitizenHandle guideHandle = guideTransform == null ? null : guideTransform.GetComponent<CitizenHandle>();
            Require(guideTransform != null && guideTransform.gameObject.activeInHierarchy &&
                    guideHandle != null && guideHandle.ResidentId == guide.Id,
                "the guide API resolves the rendered citizen carrying the same resident id");

            Require(Root("Portrait_Danwoo") == null,
                "dialogue uses the actual in-world Danwoo and has no duplicate portrait");
            Font pixelFont = GameFont.LoadBundledPixelFont();
            Text[] dialogueTexts = Root("TutorialDialogue").GetComponentsInChildren<Text>(true);
            Require(pixelFont != null && GameFont.Load() == pixelFont && dialogueTexts.Length >= 6 &&
                    dialogueTexts.All(text => text.font == pixelFont),
                "all tutorial dialogue text uses the bundled Korean pixel font");
            Require(pixelFont.material != null && pixelFont.material.mainTexture != null &&
                    pixelFont.material.mainTexture.filterMode == FilterMode.Point && ContainsKorean(director.Text),
                "the pixel font keeps point filtering while rendering Korean narration");

            Require(citizenSimulation.GuideControlled,
                "tutorial narration owns only Danwoo's presentation movement");
            CitizenDecisionBatch controlledSnapshot = citizenSimulation.BuildDecisionSnapshot(new[] { guide.Id });
            Require(controlledSnapshot.residents.Count == 0,
                "resident AI excludes Danwoo only while tutorial guide control is active");
            CitizenDecisionBatch peerSnapshot = citizenSimulation.BuildDecisionSnapshot(
                citizenSimulation.Residents.Select(resident => resident.Id));
            Require(peerSnapshot.residents.Count > 0 &&
                    peerSnapshot.residents.All(facts => facts.id != guide.Id.ToString()),
                "guide control still leaves ordinary residents available to the AI client");
            Require(citizens.SetGuideControl(false), "guide control can be released without removing Danwoo");
            CitizenDecisionBatch releasedSnapshot = citizenSimulation.BuildDecisionSnapshot(new[] { guide.Id });
            Require(releasedSnapshot.residents.Count == 1 && releasedSnapshot.residents[0].id == guide.Id.ToString(),
                "the same resident becomes AI-eligible again when guide control is released");
            Require(citizens.SetGuideControl(true), "tutorial guide control can reclaim the same resident");

            Require(citizens.GuideToNear(10, 7) && citizenSimulation.GuideWalking,
                "Danwoo starts or queues a real route toward the first road-building lesson");
            IReadOnlyList<int> route = guide.Path;
            Require(route.Count >= 2 && route.Skip(1).All(index =>
                    game.State.Cells[index].Building == BuildingKind.Road ||
                    game.State.Cells[index].Building == BuildingKind.TownHall),
                "Danwoo's movement path follows the live connected road network");
            for (int index = 1; index < route.Count; index++)
            {
                int previous = route[index - 1], current = route[index];
                int distance = Math.Abs(previous % game.State.Size - current % game.State.Size) +
                               Math.Abs(previous / game.State.Size - current / game.State.Size);
                Check(distance == 1, "Danwoo route segment " + index + " is orthogonally adjacent");
            }

            CameraCinematics cinematics = game.CameraRig.Cinematics;
            int focusBefore = director.GuideFocusCount;
            yield return ClickAndSettle("Button_TutorialFollowGuide", .12f);
            Require(director.GuideFollowing && cinematics.IsFollowing &&
                    cinematics.FollowTargetTransform == guideTransform && director.GuideFocusCount == focusBefore + 1,
                "the rendered Danwoo button follows the actual moving guide transform");
            Vector3 guideStart = guideTransform.position;
            Vector3 cameraStart = cinematics.Focus;
            Require(Mathf.Approximately(game.GameSpeed, 0f),
                "the city remains paused while Danwoo is under guide control");
            yield return new WaitForSecondsRealtime(.72f);
            yield return new WaitForEndOfFrame();
            Require(Vector3.Distance(guideTransform.position, guideStart) > .08f,
                "Danwoo advances with unscaled time while the world simulation is paused");
            Require(Vector3.Distance(cinematics.Focus, cameraStart) > .04f &&
                    Vector2.Distance(new Vector2(cinematics.Focus.x, cinematics.Focus.z),
                        new Vector2(guideTransform.position.x, guideTransform.position.z)) < .8f,
                "camera focus follows Danwoo's actual changing world position");
            Require(game.State.Population == populationBefore && citizenSimulation.ResidentCount == populationBefore,
                "guide movement never creates or removes a citizen");

            int focusBeforeTakeover = director.GuideFocusCount;
            game.CameraRig.PanScreen(new Vector2(72f, 0f));
            yield return new WaitForEndOfFrame();
            Require(!cinematics.IsFollowing && !director.GuideFollowing,
                "manual camera input takes control away from guide follow");
            director.Reopen();
            game.NotifyWorldSelection();
            yield return new WaitForSecondsRealtime(.24f);
            Require(!cinematics.IsFollowing && director.GuideFocusCount == focusBeforeTakeover,
                "routine refresh and reopen do not steal the camera back after manual takeover");

            yield return new WaitForSecondsRealtime(3.05f);
            dialogue.Initialize(director, hud.transform);
            yield return new WaitForEndOfFrame();
            Require(dialogue.Typing && dialogue.PageIndex == 0,
                "guide verification resets the welcome page for the typewriter journey");
        }

        IEnumerator WaitForIntroToFinish(TownHallIntro intro, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (intro != null && intro.IsPlaying && Time.realtimeSinceStartup < deadline) yield return null;
            if (intro != null && intro.IsPlaying)
                throw new InvalidOperationException("Town Hall intro exceeded its " + timeout + " second bound");
            yield return new WaitForEndOfFrame();
        }

        void VerifyCatalogAndFreshCity()
        {
            TutorialStepDefinition[] steps = TutorialCatalog.All.ToArray();
            Require(steps.Length == 11, "the real tutorial catalog contains exactly 11 steps");
            Require(TutorialAdvisor.Name == "단우", "the friendly village clerk is named 단우");
            Require(game.State.Tutorial != null && game.State.Tutorial.Enabled &&
                    game.State.Tutorial.Step == TutorialStepId.Welcome,
                "GameState.CreateNew owns enabled welcome progress");
            Require(game.State.Coins == 1100 && game.State.Stock[(int)Resource.Timber] == 90 &&
                    game.State.Stock[(int)Resource.Stone] == 65 && game.State.ResearchPoints == 10,
                "the journey starts with only the normal CreateNew economy");
            Require(game.State.Tutorial.RoadBaseline == 6 && game.State.Tutorial.HouseBaseline == 2 &&
                    game.State.Tutorial.LumberyardBaseline == 0 && game.State.Tutorial.StudyHouseBaseline == 0,
                "fresh connected-building baselines match the authored city");
            Check(game.State.Tutorial.GuidanceEnabled && game.State.Tutorial.GuidanceSeenMask == 0,
                "fresh progress enables feature guidance with an empty seen mask");

            GameState authoredFresh = GameState.CreateNew();
            Check(!authoredFresh.Tutorial.TownHallIntroPlayed && authoredFresh.Tutorial.GuidanceEnabled &&
                  authoredFresh.Tutorial.GuidanceSeenMask == 0,
                "the pure fresh-city DTO starts before its one-time intro and feature lessons");

            for (int index = 0; index < steps.Length; index++)
            {
                TutorialStepDefinition step = steps[index];
                Check((int)step.Id == index, "catalog step " + index + " preserves its persisted enum order");
                Check(step.StableId == ExpectedStableIds[index],
                    "catalog step " + index + " preserves stable id " + ExpectedStableIds[index]);
                Check(!string.IsNullOrWhiteSpace(step.Title) && !string.IsNullOrWhiteSpace(step.Dialogue) &&
                      !string.IsNullOrWhiteSpace(step.Goal),
                    "catalog step " + index + " has title, dialogue, and goal copy");
                Check(step.Speaker == "단우", "catalog step " + index + " is spoken by 단우");
                Check(ContainsKorean(step.Title + step.Dialogue + step.Goal),
                    "catalog step " + index + " contains authored Korean text");
            }

            Check(Target(TutorialStepId.SelectTownHall, BuildingKind.TownHall, 10, 10),
                "town-hall step targets TownHall 10,10");
            Check(Target(TutorialStepId.ExtendRoad, BuildingKind.Road, 10, 7),
                "road step targets Road 10,7");
            Check(Target(TutorialStepId.BuildHouse, BuildingKind.House, 9, 7),
                "house step targets House 9,7");
            Check(Target(TutorialStepId.BuildLumberyard, BuildingKind.Lumberyard, 7, 10),
                "lumberyard step targets Lumberyard 7,10");
            Check(Target(TutorialStepId.BuildStudyHouse, BuildingKind.StudyHouse, 11, 8),
                "study-house step targets StudyHouse 11,8");

            GuidanceLessonDefinition[] lessons = GuidanceLessonCatalog.All.ToArray();
            Check(GuidanceLessonCatalog.Count == 19 && lessons.Length == 19,
                "the feature guidance catalog contains exactly 19 current-system lessons");
            Check(lessons.Select(lesson => lesson.StableId).Distinct().Count() == 19,
                "all feature guidance lessons have unique stable ids");
            Check(GuidanceLessonCatalog.KnownSeenMask == (1 << 19) - 1 &&
                  GuidanceLessonCatalog.IsValidSeenMask(GuidanceLessonCatalog.KnownSeenMask),
                "the persisted guidance seen mask owns exactly 19 known bits");
            for (int index = 0; index < lessons.Length; index++)
            {
                GuidanceLessonDefinition lesson = lessons[index];
                Check((int)lesson.Id == index, "feature lesson " + index + " preserves its seen-bit index");
                Check(!string.IsNullOrWhiteSpace(lesson.StableId) && !string.IsNullOrWhiteSpace(lesson.Title) &&
                      !string.IsNullOrWhiteSpace(lesson.Goal),
                    "feature lesson " + index + " has stable authored metadata");
                Check(lesson.Pages != null && lesson.Pages.Length >= 2 && lesson.Pages.Length <= 3 &&
                      lesson.Pages.All(page => ContainsKorean(page) && !string.IsNullOrWhiteSpace(page)),
                    "feature lesson " + index + " has two or three Korean teaching pages");
            }
            Check(GuidanceLessonCatalog.GetNextUnseenUnlocked(authoredFresh) == null,
                "feature lessons never interrupt the unfinished basic tutorial");
        }

        void VerifyDialogueGeometry()
        {
            RectTransform root = Root("TutorialDialogue") as RectTransform;
            Require(root != null && Mathf.Abs(root.rect.width - 660f) <= 1f &&
                    Mathf.Abs(root.rect.height - 156f) <= 1f,
                "the owned tutorial dialogue is exactly 660x156 design pixels");
            Check(root != null && root.parent == hud.transform,
                "the tutorial dialogue is owned directly by the existing HUD Canvas");
            string[] touchTargets =
            {
                "TutorialDialogueBody", "Button_TutorialCollapse", "Button_TutorialSkip",
                "Button_TutorialAdvance", "Button_TutorialReopen", "Button_TutorialRestart", "Button_IntroSkip",
                "Button_GuidanceLessons", "Button_GuidanceNext", "Button_GuidanceToggle", "Button_Advice_City",
                "Button_Advice_Production", "Button_Advice_Research", "Button_Advice_Factory",
                "Button_Advice_Territory"
            };
            foreach (string name in touchTargets)
            {
                RectTransform rect = Root(name) as RectTransform;
                Check(rect != null && rect.rect.width >= 44f && rect.rect.height >= 44f,
                    name + " keeps a 44-pixel minimum touch target");
            }
            foreach (GuidanceLessonDefinition lesson in GuidanceLessonCatalog.All)
            {
                RectTransform rect = Root("Button_GuidanceLesson_" + lesson.StableId) as RectTransform;
                Check(rect != null && rect.rect.width >= 44f && rect.rect.height >= 44f,
                    lesson.StableId + " picker button keeps a 44-pixel minimum touch target");
            }
        }

        IEnumerator AcknowledgeIncompleteTask(TutorialStepId expected, string label)
        {
            Require(director.Step == expected, label + " begins on its expected step");
            Require(!director.GoalSatisfied, label + " goal is incomplete before the player action");
            int advance = director.AdvanceCount;
            yield return RevealAllPages(expected);
            yield return ClickAndSettle("Button_TutorialAdvance");
            Require(director.Step == expected && director.AdvanceCount == advance,
                label + " cannot advance while its real goal is incomplete");
            Require(director.IsCollapsed && !Root("TutorialDialogue").gameObject.activeInHierarchy,
                label + " acknowledgement collapses the dialogue for unobstructed work");
        }

        IEnumerator RevealAndContinue(TutorialStepId expected)
        {
            Require(director.Step == expected, expected + " is current before acknowledgement");
            yield return FinishDialogueAndContinue(expected);
        }

        IEnumerator FinishDialogueAndContinue(TutorialStepId expected)
        {
            yield return RevealAllPages(expected);
            yield return ClickAndSettle("Button_TutorialAdvance");
        }

        IEnumerator RevealAllPages(TutorialStepId expected)
        {
            int guard = 0;
            while (guard++ < 32)
            {
                Require(director.Step == expected, expected + " remains current while paging its dialogue");
                Require(dialogue.PageCount >= 1 && dialogue.PageIndex >= 0 && dialogue.PageIndex < dialogue.PageCount,
                    expected + " exposes a valid dialogue page index");
                Require(dialogue.CurrentPageFits,
                    expected + " current Korean pixel-font page fits the rendered body");
                if (dialogue.Typing)
                {
                    int page = dialogue.PageIndex;
                    yield return ClickAndSettle("Button_TutorialAdvance");
                    Require(director.Step == expected && dialogue.PageIndex == page && !dialogue.Typing &&
                            dialogue.DisplayedText == dialogue.CurrentPageText,
                        expected + " reveal tap completes only page " + (page + 1));
                }
                if (dialogue.PageIndex >= dialogue.PageCount - 1) yield break;

                int previousPage = dialogue.PageIndex;
                yield return ClickAndSettle("Button_TutorialAdvance");
                Require(director.Step == expected && dialogue.PageIndex == previousPage + 1,
                    expected + " advances from page " + (previousPage + 1) + " to page " + (previousPage + 2) +
                    " without acknowledging the director");
            }
            throw new InvalidOperationException(expected + " dialogue pagination exceeded its 32-page safety bound");
        }

        IEnumerator BuildThroughHud(TutorialStepId expected, string categoryButton,
            BuildingKind kind, int x, int z)
        {
            yield return AcknowledgeIncompleteTask(expected, kind + " construction");
            Require(game.State.Cells[z * game.State.Size + x].Building == BuildingKind.None,
                kind + " target " + x + "," + z + " starts empty");
            string toolButton = "Button_" + Catalog.Get(kind).Name;
            if (game.SelectedTool != kind)
            {
                if (IsButtonFirstHit(toolButton))
                {
                    Check(true, toolButton + " is already visible and raycastable without toggling its category");
                }
                else
                {
                    BuildingKind previousTool = game.SelectedTool;
                    yield return ClickAndSettle(categoryButton);
                    if (previousTool != BuildingKind.None && previousTool != kind)
                        Require(game.SelectedTool == BuildingKind.None,
                            categoryButton + " directly clears the previous " + previousTool + " tool");
                }
                Require(IsActive("BuildChoices") && IsButtonFirstHit(toolButton),
                    kind + " target tool is visible in the rendered build tray");
                yield return ClickAndSettle(toolButton);
            }
            Require(game.SelectedTool == kind, toolButton + " selects the actual city tool");
            Require(director.IsCollapsed, kind + " keeps the dialogue collapsed during placement");
            yield return WorldClick(x, z, "build " + kind + " at " + x + "," + z);
            Cell built = game.State.Cells[z * game.State.Size + x];
            Require(built.Building == kind && built.Connected,
                kind + " is really built and connected at " + x + "," + z);
            Require(director.Step == (TutorialStepId)((int)expected + 1),
                kind + " clears only its matching tutorial goal");
            VerifyPersistedStep((TutorialStepId)((int)expected + 1), false, kind + " construction");
        }

        IEnumerator WorldClick(int x, int z, string label)
        {
            yield return new WaitForEndOfFrame();
            bool found = TryFindClearWorldPoint(x, z, out Vector2 position);
            if (!found)
            {
                game.CameraRig.Overview();
                yield return new WaitForSecondsRealtime(.4f);
                yield return new WaitForEndOfFrame();
                found = TryFindClearWorldPoint(x, z, out position);
            }
            Require(found && VisibleScreenPoint(position), label + " has a visible camera-to-tile screen point");
            Require(!game.IsScreenPointOverUI(position), label + " is not covered by HUD raycasts");

            Ray ray = game.CameraRig.Camera.ScreenPointToRay(position);
            Require(Physics.Raycast(ray, out RaycastHit hit, 200f, 1 << 8),
                label + " reaches the board collider through the camera");
            TileHandle tile = hit.collider == null ? null : hit.collider.GetComponent<TileHandle>();
            Require(tile != null && tile.X == x && tile.Z == z,
                label + " resolves to the intended TileHandle");

            var pointer = new PointerEventData(EventSystem.current)
            {
                position = position,
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
            var uiHits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, uiHits);
            Require(uiHits.Count == 0, label + " has a clear EventSystem world-pointer path");
            Require(game.InteractScreenPoint(position), label + " is accepted by the live world input path");
            yield return new WaitForSecondsRealtime(.32f);
            yield return new WaitForEndOfFrame();
        }

        IEnumerator VerifyAdviceTopics()
        {
            var topics = new[]
            {
                (AdvisorTopic.City, "Button_Advice_City"),
                (AdvisorTopic.Production, "Button_Advice_Production"),
                (AdvisorTopic.Research, "Button_Advice_Research"),
                (AdvisorTopic.Factory, "Button_Advice_Factory"),
                (AdvisorTopic.Territory, "Button_Advice_Territory")
            };
            foreach (var entry in topics)
            {
                yield return ClickAndSettle(entry.Item2, .18f);
                string expected = TutorialAdvisor.Get(entry.Item1, game.State);
                Check(director.IsAdviceMode && director.AdviceTopic == entry.Item1,
                    entry.Item1 + " topic remains in local advice mode");
                Check(director.Text == expected && dialogue.FullText == expected,
                    entry.Item1 + " button renders the actual deterministic advisor response");
                Check(ContainsKorean(expected) && expected.Contains("\n\n"),
                    entry.Item1 + " advice contains Korean text in two readable paragraphs");
            }
            if (dialogue.Typing)
            {
                dialogue.RevealAll();
                yield return new WaitForEndOfFrame();
                Check(!dialogue.Typing && dialogue.DisplayedText == dialogue.CurrentPageText,
                    "public RevealAll exposes the complete dynamic advice text");
            }
        }

        IEnumerator VerifyGuidanceLessons()
        {
            GameState low = GameState.CreateNew();
            SetCompletedTutorial(low, true);
            Require(GuidanceLessonCatalog.MarkSeen(low, GuidanceLessonId.SaveBackupAndReducedEffects),
                "the low-growth fixture pre-marks only its always-unlocked lesson");
            int alwaysSeenMask = 1 << (int)GuidanceLessonId.SaveBackupAndReducedEffects;
            Require(low.Tutorial.GuidanceSeenMask == alwaysSeenMask &&
                    GuidanceLessonCatalog.GetNextUnseenUnlocked(low) == null,
                "known seen and locked lessons leave no automatic low-growth candidate");
            Require(SaveStore.TrySave(game.SavePath, low, out string lowSaveError),
                "the low-growth guidance fixture saves to the isolated slot: " + lowSaveError);
            game.LoadGame();
            yield return WaitForPresentation();
            Require(game.State.Tutorial.Completed && game.State.Tutorial.TownHallIntroPlayed &&
                    game.State.Tutorial.GuidanceEnabled && !director.IntroPlaying,
                "the staged completed city keeps intro and guidance persistence independent");
            Require(!director.HasEligibleLesson && !director.OpenNextLesson(),
                "automatic and manual-next paths reject seen or currently locked lessons");

            yield return ClickAndSettle("Button_TutorialReopen");
            Require(director.IsAdviceMode, "the completed guide opens its feature controls");
            yield return ClickAndSettle("Button_GuidanceLessons");
            Require(IsActive("GuidanceLessonPicker"), "the rendered feature lesson picker opens");
            int lessonButtons = hud.GetComponentsInChildren<Button>(true).Count(button =>
                button.name.StartsWith("Button_GuidanceLesson_", StringComparison.Ordinal));
            Require(lessonButtons == GuidanceLessonCatalog.Count,
                "the manual picker exposes all 19 current feature lessons");

            GuidanceLessonDefinition lockedLesson = GuidanceLessonCatalog.Get(GuidanceLessonId.FoodProductionChain);
            Require(!GuidanceLessonCatalog.IsUnlocked(game.State, lockedLesson.Id),
                "food-chain guidance is still growth-locked in the low fixture");
            int maskBeforeManualLesson = game.State.Tutorial.GuidanceSeenMask;
            yield return ClickAndSettle("Button_GuidanceLesson_" + lockedLesson.StableId);
            Require(director.IsLessonMode && director.CurrentLessonId == lockedLesson.Id &&
                    director.CurrentLessonDefinition == lockedLesson,
                "the manual picker can open a requested locked lesson without calling it automatically eligible");
            Require(dialogue.PageCount >= 2 && director.PageTexts.Count == lockedLesson.Pages.Length,
                "manual feature guidance renders its authored paginated lesson");
            int lessonOpenCount = director.LessonOpenCount;
            int lessonAcknowledgements = director.LessonAcknowledgeCount;
            yield return RevealAllPages(TutorialStepId.Finish);
            Require(game.State.Tutorial.GuidanceSeenMask == maskBeforeManualLesson &&
                    !GuidanceLessonCatalog.IsSeen(game.State, lockedLesson.Id),
                "revealing every page does not mark a feature lesson seen");
            yield return CapturePair("07-feature-lesson");
            yield return ClickAndSettle("Button_TutorialAdvance");
            Require(!director.IsLessonMode && GuidanceLessonCatalog.IsSeen(game.State, lockedLesson.Id),
                "the user's final-page acknowledgement alone marks the lesson seen");
            int expectedMask = maskBeforeManualLesson | 1 << (int)lockedLesson.Id;
            Require(game.State.Tutorial.GuidanceSeenMask == expectedMask &&
                    director.LessonAcknowledgeCount == lessonAcknowledgements + 1,
                "lesson acknowledgement adds exactly its stable bit once");
            float cascadeDeadline = Time.realtimeSinceStartup + .65f;
            while (Time.realtimeSinceStartup < cascadeDeadline) yield return null;
            Require(!director.IsLessonMode && director.LessonOpenCount == lessonOpenCount &&
                    director.LessonCooldownRemaining > 8f,
                "acknowledgement schedules a gap instead of immediately cascading another popup");
            VerifyGuidancePersisted(expectedMask, true, "manual lesson acknowledgement");

            game.State.Technologies.Add(TechId.Stonecraft);
            game.Sim.Recalculate();
            game.NotifyWorldSelection();
            GuidanceLessonDefinition later = GuidanceLessonCatalog.GetNextUnseenUnlocked(game.State);
            Require(later != null && later.Id == GuidanceLessonId.BuildingUpgrades &&
                    GuidanceLessonCatalog.IsUnlocked(game.State, later.Id) &&
                    !GuidanceLessonCatalog.IsSeen(game.State, later.Id),
                "a later technology unlock exposes the earliest relevant unseen lesson");
            yield return ClickAndSettle("Button_TutorialReopen");
            yield return ClickAndSettle("Button_GuidanceNext");
            Require(director.IsLessonMode && director.CurrentLessonId == GuidanceLessonId.BuildingUpgrades,
                "the rendered next-feature button opens only the known unseen unlocked lesson");
            int beforeDisableMask = game.State.Tutorial.GuidanceSeenMask;
            director.DisableGuidance();
            yield return new WaitForEndOfFrame();
            Require(!director.IsLessonMode && !director.GuidanceEnabled &&
                    game.State.Tutorial.GuidanceSeenMask == beforeDisableMask,
                "disabling guidance closes an unacknowledged lesson without marking it seen");
            VerifyGuidancePersisted(beforeDisableMask, false, "guidance disable");

            GameState advanced = SharedCityScenario.Create();
            advanced.Population = 30;
            SetCompletedTutorial(advanced, false);
            advanced.Tutorial.GuidanceSeenMask = beforeDisableMask;
            Require(GuidanceLessonCatalog.All.All(lesson => GuidanceLessonCatalog.IsUnlocked(advanced, lesson.Id)),
                "the advanced shared-city fixture makes all 19 current feature lessons eligible by state");
            Require(SaveStore.TrySave(game.SavePath, advanced, out string advancedSaveError),
                "the advanced guidance fixture saves to the isolated slot: " + advancedSaveError);
            game.LoadGame();
            yield return WaitForPresentation();
            Require(!director.GuidanceEnabled && !director.IsLessonMode && !director.OpenNextLesson(),
                "disabled guidance blocks automatic and manual-next lessons even in an advanced city");
            VerifyGuidancePersisted(beforeDisableMask, false, "advanced fixture load");

            yield return ClickAndSettle("Button_TutorialReopen");
            Require(director.IsAdviceMode, "advanced completed city opens the feature control panel");
            yield return ClickAndSettle("Button_GuidanceToggle");
            Require(director.GuidanceEnabled && !director.IsLessonMode,
                "the rendered guidance toggle enables future lessons without an immediate popup");
            yield return ClickAndSettle("Button_GuidanceToggle");
            Require(!director.GuidanceEnabled && game.State.Tutorial.GuidanceSeenMask == beforeDisableMask,
                "the rendered guidance toggle disables tips without changing seen history");
            VerifyGuidancePersisted(beforeDisableMask, false, "advanced guidance toggle");
        }

        IEnumerator VerifyLegacyNullFlow()
        {
            var legacy = GameState.CreateNew();
            legacy.Tutorial = null;
            Require(SaveStore.TrySave(game.SavePath, legacy, out string saveError),
                "legacy Tutorial=null fixture saves to the isolated slot: " + saveError);
            game.LoadGame();
            yield return WaitForPresentation();
            Require(game.State.Tutorial == null && !director.IsRunning && !dialogue.Visible,
                "legacy Tutorial=null does not auto-launch tutorial UI");
            director.OpenGuide();
            yield return WaitForPresentation();
            Require(game.State.Tutorial != null && director.IsRunning &&
                    director.Step == TutorialStepId.Welcome,
                "explicit OpenGuide creates welcome progress for a legacy city");
            Require(dialogue.Visible && Mathf.Approximately(game.GameSpeed, 0f),
                "explicit legacy guide opening renders and pauses safely");
            VerifyPersistedStep(TutorialStepId.Welcome, false, "legacy explicit guide open");
        }

        IEnumerator ClickAndSettle(string name, float seconds = .24f)
        {
            Button button = null;
            PointerEventData pointer = null;
            var hits = new List<RaycastResult>();
            bool ready = false;
            string lastReason = "button not created";
            float raycastDeadline = Time.realtimeSinceStartup + .3f;
            while (!ready && Time.realtimeSinceStartup <= raycastDeadline)
            {
                button = FindButton(name);
                if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                {
                    lastReason = "button unavailable";
                    yield return null;
                    continue;
                }
                Canvas.ForceUpdateCanvases();
                bool pointerReady = true;
                try { pointer = PointerAtButtonCenter(button); }
                catch (Exception exception)
                {
                    lastReason = exception.Message;
                    pointerReady = false;
                }
                if (!pointerReady) { yield return null; continue; }
                hits.Clear();
                EventSystem.current.RaycastAll(pointer, hits);
                ready = hits.Count > 0 && IsButtonHit(button, hits[0].gameObject);
                if (!ready)
                    lastReason = hits.Count == 0 ? "no EventSystem hits" : "first hit " + hits[0].gameObject.name;
                if (!ready) yield return null;
            }
            if (!ready)
                throw new InvalidOperationException("UI button did not become the first EventSystem raycast target " +
                    "within 0.3 seconds: " + name + " (" + lastReason + ")");

            GameObject target = hits[0].gameObject;
            FeelUiFeedback feedback = button.GetComponent<FeelUiFeedback>();
            int feedbackBefore = FeelUiFeedback.PlaysRequested;
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler);
            Check(true, "EventSystem pointer enter/down/up/click activates " + name);
            Check(feedback != null && feedback.Role == FeelUiFeedback.FeedbackRole.Button,
                name + " owns live Feel button feedback");
            Check(FeelUiFeedback.PlaysRequested > feedbackBefore,
                name + " requests actual Feel animation from pointer events");
            yield return new WaitForSecondsRealtime(seconds);
            yield return new WaitForEndOfFrame();
        }

        IEnumerator WaitForPresentation()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            yield return new WaitForEndOfFrame();
            hud = game.CityHud;
            director = game.Tutorial;
            dialogue = hud == null ? null : hud.GetComponentInChildren<TutorialDialogue>(true);
            Require(director != null && dialogue != null, "tutorial presentation survives the state replacement");
        }

        IEnumerator SetResolution(int width, int height)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            float deadline = Time.realtimeSinceStartup + 2f;
            while ((Screen.width != width || Screen.height != height) && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return new WaitForSecondsRealtime(.12f);
            yield return new WaitForEndOfFrame();
            Require(Screen.width == width && Screen.height == height,
                "render target settles at " + width + "x" + height);
        }

        IEnumerator CapturePair(string stem)
        {
            yield return SetResolution(1600, 900);
            yield return CaptureAtCurrentResolution("1600x900-" + stem + ".png");
            yield return SetResolution(1280, 720);
            yield return CaptureAtCurrentResolution("1280x720-" + stem + ".png");
        }

        IEnumerator RecordTypingFrames()
        {
            string directory = Path.Combine(output, "typing-frames");
            Directory.CreateDirectory(directory);
            for (int index = 0; index < TypingFrameCount; index++)
            {
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null) RecordError("Typing frame returned no texture at index " + index);
                else
                {
                    try
                    {
                        File.WriteAllBytes(Path.Combine(directory, "frame-" + index.ToString("D3") + ".png"),
                            frame.EncodeToPNG());
                    }
                    catch (Exception exception)
                    {
                        RecordError("Could not write typing frame " + index + ": " + exception.Message);
                    }
                    Destroy(frame);
                }
                yield return new WaitForSecondsRealtime(.02f);
            }
            results.Add("CAPTURE 24 dialogue typing frames at " + Screen.width + "x" + Screen.height);
        }

        IEnumerator CaptureAtCurrentResolution(string name)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            string lastReason = "no frame captured";
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                Texture2D texture = null;
                bool captured = false;
                try
                {
                    texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (!VisibleFrame(texture, out lastReason)) continue;
                    File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                    results.Add("CAPTURE " + name + " " + texture.width + "x" + texture.height);
                    captured = true;
                }
                catch (Exception exception) { lastReason = exception.Message; }
                finally { if (texture != null) Destroy(texture); }
                if (captured) yield break;
            }
            RecordError("Capture failed within five seconds: " + name + " - " + lastReason);
        }

        void VerifyPersistedStep(TutorialStepId expected, bool completed, string phase, bool? expectedEnabled = null)
        {
            Check(director.PersistResult && string.IsNullOrEmpty(director.PersistError),
                phase + " transition reports a successful quiet progress save");
            Check(SaveStore.TryLoad(game.SavePath, out GameState loaded, out string error),
                phase + " progress loads from the isolated save: " + error);
            Check(loaded != null && loaded.Tutorial != null, phase + " save contains tutorial progress");
            if (loaded?.Tutorial == null) return;
            Check(loaded.Tutorial.Step == expected, phase + " save resumes " + expected);
            Check(loaded.Tutorial.Completed == completed, phase + " completion flag is " + completed);
            bool enabled = expectedEnabled ?? !completed;
            Check(loaded.Tutorial.Enabled == enabled, phase + " enabled flag is " + enabled);
        }

        void VerifyIntroPersisted(string phase)
        {
            Check(director.PersistResult && string.IsNullOrEmpty(director.PersistError),
                phase + " reports successful intro persistence");
            Check(SaveStore.TryLoad(game.SavePath, out GameState loaded, out string error),
                phase + " reloads from the isolated save: " + error);
            Check(loaded?.Tutorial != null && loaded.Tutorial.TownHallIntroPlayed,
                phase + " persists TownHallIntroPlayed");
        }

        void VerifyGuidancePersisted(int expectedMask, bool enabled, string phase)
        {
            Check(director.PersistResult && string.IsNullOrEmpty(director.PersistError),
                phase + " reports successful guidance persistence");
            Check(SaveStore.TryLoad(game.SavePath, out GameState loaded, out string error),
                phase + " reloads from the isolated save: " + error);
            Check(loaded?.Tutorial != null && loaded.Tutorial.Completed && loaded.Tutorial.TownHallIntroPlayed,
                phase + " retains completed basics and the one-time intro flag");
            Check(loaded?.Tutorial != null && loaded.Tutorial.GuidanceEnabled == enabled &&
                  loaded.Tutorial.GuidanceSeenMask == expectedMask,
                phase + " retains guidance enabled state and exact seen mask");
        }

        static void SetCompletedTutorial(GameState state, bool guidanceEnabled)
        {
            if (state.Tutorial == null) state.Tutorial = TutorialProgress.Create(state);
            state.Tutorial.Enabled = false;
            state.Tutorial.Completed = true;
            state.Tutorial.Skipped = false;
            state.Tutorial.Collapsed = false;
            state.Tutorial.Step = TutorialStepId.Finish;
            state.Tutorial.TownHallIntroPlayed = true;
            state.Tutorial.GuidanceEnabled = guidanceEnabled;
            state.Tutorial.GuidanceSeenMask = 0;
        }

        static Collider[] GroundColliders() => FindObjectsByType<TileHandle>()
            .Where(tile => tile != null && tile.GetComponent<Collider>() != null && tile.GetComponent<Collider>().enabled)
            .OrderBy(tile => tile.Z)
            .ThenBy(tile => tile.X)
            .Select(tile => tile.GetComponent<Collider>())
            .ToArray();

        static bool Approximately(Vector3 first, Vector3 second) =>
            Vector3.SqrMagnitude(first - second) <= .000001f;

        bool Target(TutorialStepId id, BuildingKind kind, int x, int z)
        {
            TutorialStepDefinition step = TutorialCatalog.Get(id);
            return step != null && step.HighlightBuilding == kind && step.PreferredX == x && step.PreferredZ == z;
        }

        bool TryFindClearWorldPoint(int x, int z, out Vector2 position)
        {
            Vector3[] offsets =
            {
                new Vector3(0f, .1f, 0f), new Vector3(.28f, .1f, .28f),
                new Vector3(-.28f, .1f, .28f), new Vector3(.28f, .1f, -.28f),
                new Vector3(-.28f, .1f, -.28f)
            };
            foreach (Vector3 offset in offsets)
            {
                Vector3 screen = game.CameraRig.Camera.WorldToScreenPoint(BoardView.Position(x, z) + offset);
                var candidate = new Vector2(screen.x, screen.y);
                if (screen.z <= 0f || !VisibleScreenPoint(candidate) || game.IsScreenPointOverUI(candidate)) continue;
                Ray ray = game.CameraRig.Camera.ScreenPointToRay(candidate);
                if (!Physics.Raycast(ray, out RaycastHit boardHit, 200f, 1 << 8)) continue;
                TileHandle tile = boardHit.collider == null ? null : boardHit.collider.GetComponent<TileHandle>();
                if (tile == null || tile.X != x || tile.Z != z) continue;
                if (game.SelectedTool == BuildingKind.None && !game.DemolitionMode &&
                    Physics.Raycast(ray, out _, 200f, 1 << 9)) continue;
                position = candidate;
                return true;
            }
            position = default;
            return false;
        }

        bool VisibleScreenPoint(Vector2 point) => point.x >= 1f && point.y >= 1f &&
            point.x < Screen.width - 1f && point.y < Screen.height - 1f;

        Button FindButton(string name) => hud == null ? null :
            hud.GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == name);

        Transform Root(string name) => hud == null ? null :
            hud.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);

        bool IsActive(string name)
        {
            Transform root = Root(name);
            return root != null && root.gameObject.activeInHierarchy;
        }

        static bool IsButtonHit(Button button, GameObject hit) =>
            hit == button.gameObject || hit.transform.IsChildOf(button.transform);

        bool IsButtonFirstHit(string name)
        {
            Button button = FindButton(name);
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable ||
                EventSystem.current == null) return false;
            Canvas.ForceUpdateCanvases();
            PointerEventData pointer;
            try { pointer = PointerAtButtonCenter(button); }
            catch { return false; }
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            return hits.Count > 0 && IsButtonHit(button, hits[0].gameObject);
        }

        static PointerEventData PointerAtButtonCenter(Button button)
        {
            if (EventSystem.current == null) throw new InvalidOperationException("EventSystem unavailable");
            RectTransform rect = button.transform as RectTransform;
            if (rect == null) throw new InvalidOperationException("Button has no RectTransform: " + button.name);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 center = new Vector2((corners[0].x + corners[2].x) * .5f,
                (corners[0].y + corners[2].y) * .5f);
            if (corners[2].x - corners[0].x < 1f || corners[2].y - corners[0].y < 1f ||
                center.x < 0f || center.x > Screen.width || center.y < 0f || center.y > Screen.height)
                throw new InvalidOperationException("Button has no visible screen rect: " + button.name);
            return new PointerEventData(EventSystem.current)
            {
                position = center,
                button = PointerEventData.InputButton.Left,
                clickCount = 1
            };
        }

        static bool VisibleFrame(Texture2D texture, out string reason)
        {
            if (texture == null || texture.width < 320 || texture.height < 180)
            {
                reason = "missing or undersized texture";
                return false;
            }
            Color32[] pixels = texture.GetPixels32();
            int stride = Mathf.Max(1, pixels.Length / 4096);
            int samples = 0, lit = 0;
            var colors = new HashSet<int>();
            for (int index = 0; index < pixels.Length; index += stride)
            {
                Color32 pixel = pixels[index];
                samples++;
                if (pixel.r + pixel.g + pixel.b >= 24) lit++;
                colors.Add((pixel.r >> 4) << 8 | (pixel.g >> 4) << 4 | (pixel.b >> 4));
            }
            if (lit < samples / 20) { reason = "fewer than five percent of sampled pixels are visible"; return false; }
            if (colors.Count < 16) { reason = "fewer than 16 quantized sampled colors"; return false; }
            reason = "";
            return true;
        }

        static bool ContainsKorean(string value) => !string.IsNullOrEmpty(value) &&
            value.Any(character => character >= '\uAC00' && character <= '\uD7A3');

        static int TextElementCount(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return new System.Globalization.StringInfo(value).LengthInTextElements;
        }

        static int ConnectedCount(GameState state, BuildingKind kind) => state.Cells.Count(cell =>
            cell != null && cell.Connected && cell.Building == kind);

        static string EconomySignature(GameState state) =>
            state.Coins + "|" + state.Day + "|" + state.Population + "|" + state.ResearchPoints + "|" +
            string.Join(",", state.Stock) + "|" + string.Join(",", state.Technologies) + "|" +
            string.Join(",", state.Cells.Select(cell => (int)cell.Building + ":" + cell.Level));

        void Check(bool okay, string message)
        {
            if (okay)
            {
                results.Add("PASS " + message);
                Debug.Log("TUTORIAL PASS: " + message);
            }
            else RecordError("FAIL " + message);
        }

        void Require(bool okay, string message)
        {
            if (!okay) throw new InvalidOperationException(message);
            Check(true, message);
        }

        void RecordError(string message)
        {
            if (errors.Count >= MaximumErrors)
            {
                suppressedErrors++;
                return;
            }
            message ??= "Unknown error";
            if (message.Length > MaximumErrorCharacters)
                message = message.Substring(0, MaximumErrorCharacters) + "...[truncated]";
            errors.Add(message);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            game?.SetSpeed(0f);
            Application.logMessageReceived -= OnLog;
            int exitCode = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILD " + Application.buildGUID +
                " BUILDGUID " + Application.buildGUID + " UNITY " + Application.unityVersion);
            results.Add("ERRORS " + errors.Count);
            results.Add("SUPPRESSED_ERRORS " + suppressedErrors);
            results.AddRange(errors);
            results.Add("EXIT " + exitCode);
            try { File.WriteAllLines(Path.Combine(output, "tutorial-results.txt"), results); }
            catch (Exception exception)
            {
                Debug.LogError("Could not write tutorial-results.txt: " + exception.Message);
                exitCode = 1;
            }
            Debug.Log(exitCode == 0 ? "RIVERWORKS_TUTORIAL_SMOKE_PASS" : "RIVERWORKS_TUTORIAL_SMOKE_FAIL");
            Application.Quit(exitCode);
        }
    }
}

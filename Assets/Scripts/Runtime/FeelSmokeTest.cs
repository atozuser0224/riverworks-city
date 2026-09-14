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
    /// <summary>Opt-in executable verification for Riverworks' Feel-backed world and HUD feedback.</summary>
    public sealed class FeelSmokeTest : MonoBehaviour
    {
        const int MaximumErrors = 64;
        const int MaximumErrorCharacters = 4096;
        const int SequenceFrames = 12;

        GameController game;
        FeelDirector feel;
        Hud hud;
        string output;
        bool finished;
        bool originalReducedMotion;
        float originalTimeScale = 1f;
        bool originalCameraPoseCaptured;
        Vector3 originalCameraFocus;
        float originalCameraSize = 7f;
        int suppressedErrors;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();
        readonly Dictionary<TileHandle, TransformSnapshot> tileSnapshots = new Dictionary<TileHandle, TransformSnapshot>();

        struct TransformSnapshot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        public void Initialize(GameController controller)
        {
            game = controller;
            originalReducedMotion = FeelDirector.ReducedMotion;
            originalTimeScale = Time.timeScale;
            if (controller != null && controller.CameraRig != null)
            {
                originalCameraFocus = controller.CameraRig.AuthoredFocus;
                originalCameraSize = controller.CameraRig.AuthoredSize;
                originalCameraPoseCaptured = true;
            }
            string[] arguments = Environment.GetCommandLineArgs();
            int outputAt = Array.IndexOf(arguments, "-riverworks-output");
            output = outputAt >= 0 && outputAt + 1 < arguments.Length
                ? arguments[outputAt + 1]
                : Path.Combine(Application.persistentDataPath, "FeelSmoke");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
            StartCoroutine(GuardedRun());
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = originalTimeScale;
            FeelUiFeedback.ReducedMotion = originalReducedMotion;
            RestoreOriginalCameraPose();
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
            Require(game != null, "GameController.Initialize supplied a controller");
            string[] arguments = Environment.GetCommandLineArgs();
            Require(Array.IndexOf(arguments, "-riverworks-feel-smoke") >= 0,
                "Feel smoke only runs behind its explicit command flag");
            Require(game.SmokeMode, "Feel smoke uses the controller's isolated smoke save slot");
            Require(!string.Equals(Path.GetFileName(game.SavePath), "city-v1.json", StringComparison.OrdinalIgnoreCase),
                "Feel smoke never targets the player's city save");

            Time.timeScale = 1f;
            FeelUiFeedback.ReducedMotion = false;
            game.SetSpeed(0);
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.35f);
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.45f);
            yield return new WaitForEndOfFrame();

            GameState fixture = SharedCityScenario.Create();
            Require(fixture.Technologies.Remove(TechId.Education),
                "shared city fixture can stage one real research-completion edge");
            fixture.ActiveResearch = TechId.Education;
            fixture.ResearchDaysRemaining = 1;
            Require(SaveStore.TrySave(game.SavePath, fixture, out string fixtureSaveError),
                "shared city Feel fixture saves before loading: " + fixtureSaveError);

            game.LoadGame();
            yield return null;
            game.SetSpeed(0);
            feel = game.Feel;
            hud = game.CityHud;
            Require(feel != null, "GameController exposes its initialized FeelDirector");
            Require(hud != null && hud.gameObject.activeInHierarchy, "the active city HUD is available");
            Require(EventSystem.current != null, "an EventSystem is available for native UI input");
            Require(game.State.ActiveResearch == TechId.Education && !TechCatalog.Has(game.State, TechId.Education),
                "loaded scenario retains the staged incomplete research baseline");
            Check(feel.PlayedCount == 0 && feel.ActiveWorldEffects == 0 &&
                  feel.CountFor(FeelCue.ResearchComplete) == 0 &&
                  feel.CountFor(FeelCue.EraAdvance) == 0 && feel.CountFor(FeelCue.GoalComplete) == 0,
                "loading a rich save establishes a baseline without fake celebration feedback");
            Require(FindObjectsByType<MMF_Player>(FindObjectsInactive.Include).Length > 0,
                "Feel creates real native MMF_Player components in the running scene");

            SnapshotGameplayColliders();
            game.CameraRig.Overview();
            yield return new WaitForSecondsRealtime(.35f);
            yield return Capture("00-whole-city-1600x900.png");

            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            game.CameraRig.Home();
            game.CameraRig.SetZoom(5.2f);
            yield return new WaitForSecondsRealtime(.45f);
            yield return Capture("01-city-closeup-before-1280x720.png");

            int buildX, buildZ;
            FindBuildSite(out buildX, out buildZ);
            game.SelectTool(BuildingKind.House);
            yield return WaitForFeedbackToSettle(.3f);
            int buildCueBefore = feel.CountFor(FeelCue.Build);
            int buildNativeBefore = NativePlayCount();
            int housesBefore = game.State.Cells.Count(cell => cell.Building == BuildingKind.House);
            game.InteractCell(buildX, buildZ);
            Require(game.Sim.GetCell(buildX, buildZ).Building == BuildingKind.House &&
                    game.State.Cells.Count(cell => cell.Building == BuildingKind.House) == housesBefore + 1,
                "a real successful city build creates one house");
            Transform buildTarget = game.Board.ModelAt(buildX, buildZ);
            Require(buildTarget != null, "BoardView exposes the newly built model as the feedback target");
            yield return ObserveWorldMotion(
                FeelCue.Build,
                buildCueBefore,
                buildNativeBefore,
                buildTarget,
                BoardView.Position(buildX, buildZ),
                Vector3.one,
                Path.Combine("build-landing-frames"),
                SequenceFrames,
                "successful city build");
            yield return Capture("02-build-closeup-settled-1280x720.png");

            game.SelectTool(BuildingKind.None);
            game.InteractCell(buildX, buildZ);
            yield return WaitForFeedbackToSettle(.25f);
            int upgradeCueBefore = feel.CountFor(FeelCue.Upgrade);
            int upgradeNativeBefore = NativePlayCount();
            int levelBefore = game.Sim.GetCell(buildX, buildZ).Level;
            game.UpgradeSelected();
            Require(game.Sim.GetCell(buildX, buildZ).Level == levelBefore + 1,
                "a real successful upgrade advances the selected building by one level");
            Transform upgradeTarget = game.Board.ModelAt(buildX, buildZ);
            Require(upgradeTarget != null, "BoardView exposes the rebuilt upgraded model as the feedback target");
            yield return ObserveWorldMotion(
                FeelCue.Upgrade,
                upgradeCueBefore,
                upgradeNativeBefore,
                upgradeTarget,
                BoardView.Position(buildX, buildZ),
                Vector3.one,
                null,
                14,
                "successful city upgrade");

            game.Factory.SelectTool(FactoryKind.Belt);
            yield return WaitForFeedbackToSettle(.25f);
            int factoryX, factoryZ;
            FindFactorySite(out factoryX, out factoryZ);
            int factoryCueBefore = feel.CountFor(FeelCue.FactoryBuild);
            int factoryNativeBefore = NativePlayCount();
            int entitiesBefore = game.Factory.State.Entities.Count;
            game.Factory.PlaceAt(factoryX, factoryZ);
            FactoryEntity placed = game.Factory.Sim.GetAt(factoryX, factoryZ);
            Require(placed != null && placed.Kind == FactoryKind.Belt &&
                    game.Factory.State.Entities.Count == entitiesBefore + 1,
                "a real successful factory placement creates one belt");
            Transform factoryTarget = game.Factory.View.ModelFor(placed.Id);
            Require(factoryTarget != null, "FactoryView exposes the newly placed machine as the feedback target");
            Vector3 factoryBasePosition = game.Factory.View.transform.InverseTransformPoint(
                game.Factory.View.WorldPosition(factoryX, factoryZ));
            Vector3 factoryBaseScale = Vector3.one * (BoardView.Spacing * .5f);
            yield return ObserveWorldMotion(
                FeelCue.FactoryBuild,
                factoryCueBefore,
                factoryNativeBefore,
                factoryTarget,
                factoryBasePosition,
                factoryBaseScale,
                null,
                12,
                "successful factory placement");

            game.Factory.CancelTools();
            game.SelectTool(BuildingKind.House);
            yield return WaitForFeedbackToSettle(.25f);
            float deniedCoins = game.State.Coins;
            float[] deniedStock = game.State.Stock.ToArray();
            int deniedEntities = game.State.Factory.Entities.Count;
            int deniedBuildings = game.State.Cells.Count(cell => cell.Building != BuildingKind.None);
            int deniedCueBefore = feel.CountFor(FeelCue.Denied);
            int deniedNativeBefore = NativePlayCount();
            game.InteractCell(buildX, buildZ);
            yield return null;
            Check(feel.CountFor(FeelCue.Denied) == deniedCueBefore + 1 && NativePlayCount() > deniedNativeBefore,
                "a rejected occupied-lot build plays one real native Denied feedback");
            Check(Approximately(game.State.Coins, deniedCoins) && SequenceEqual(game.State.Stock, deniedStock) &&
                  game.State.Factory.Entities.Count == deniedEntities &&
                  game.State.Cells.Count(cell => cell.Building != BuildingKind.None) == deniedBuildings,
                "a denied action changes no city resources, buildings, or factory entities");
            yield return WaitForFeedbackToSettle(.6f);

            yield return VerifyRapidRetriggerAndVisualPurity(buildX, buildZ);
            yield return VerifyUiAtPausedTimescale();
            yield return VerifyReducedMotion(buildX, buildZ);
            yield return VerifyResearchCompletionEdge();
            yield return VerifyCameraCinematics();

            VerifyGameplayColliders();
            Check(errors.Count == 0, "Feel runtime emitted no error, exception, or assertion logs");
            game.SetSpeed(0);
        }

        IEnumerator ObserveWorldMotion(
            FeelCue cue,
            int cueBefore,
            int nativeBefore,
            Transform target,
            Vector3 baseLocalPosition,
            Vector3 baseLocalScale,
            string captureDirectory,
            int frames,
            string label)
        {
            if (!string.IsNullOrEmpty(captureDirectory))
                Directory.CreateDirectory(Path.Combine(output, captureDirectory));
            bool sawMotion = false;
            bool sawNativePlaying = false;
            for (int index = 0; index < frames; index++)
            {
                yield return new WaitForEndOfFrame();
                Require(target != null, label + " target survives its animation");
                sawMotion |= TransformChanged(target, baseLocalPosition, baseLocalScale, .002f);
                sawNativePlaying |= FindObjectsByType<MMF_Player>(FindObjectsInactive.Include)
                    .Any(player => player != null && player.IsPlaying);
                if (!string.IsNullOrEmpty(captureDirectory))
                    WriteFrame(captureDirectory, index);
            }
            Check(feel.CountFor(cue) == cueBefore + 1, label + " increments its semantic cue exactly once");
            Check(NativePlayCount() > nativeBefore && sawNativePlaying,
                label + " starts an actual MMF_Player rather than only incrementing a counter");
            Check(sawMotion, label + " visibly changes target scale or position mid-animation");
            yield return new WaitForSecondsRealtime(1.25f);
            Check(TransformRestored(target, baseLocalPosition, baseLocalScale),
                label + " restores the target's exact base transform after settling");
        }

        IEnumerator VerifyRapidRetriggerAndVisualPurity(int buildX, int buildZ)
        {
            Transform target = game.Board.ModelAt(buildX, buildZ);
            Require(target != null, "rapid-retrigger target exists");
            Vector3 basePosition = BoardView.Position(buildX, buildZ);
            Vector3 baseScale = Vector3.one;
            TileHandle tile = FindObjectsByType<TileHandle>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.X == buildX && item.Z == buildZ);
            Require(tile != null && tile.GetComponent<Collider>() != null,
                "the target lot retains its real physics picking tile");
            TransformSnapshot tileBefore = Snapshot(tile.transform);

            string stateBefore = JsonUtility.ToJson(game.State);
            byte[] saveBefore = File.Exists(game.SavePath) ? File.ReadAllBytes(game.SavePath) : Array.Empty<byte>();
            int poolBefore = feel.PoolSize;
            int nativeBefore = NativePlayCount();
            for (int index = 0; index < 32; index++)
                feel.Play(FeelCue.Denied, target.position, target);
            yield return null;
            Check(feel.PoolSize == poolBefore && feel.ActiveWorldEffects <= feel.PoolSize,
                "32 rapid retriggers stay inside the director's fixed world-effect pool");
            Check(NativePlayCount() > nativeBefore,
                "rapid retriggers execute pooled native MMF_Player instances");
            Check(Matches(tile.transform, tileBefore),
                "ground-tile picking geometry never animates with a target feedback cue");

            yield return new WaitForSecondsRealtime(2f);
            Check(feel.ActiveWorldEffects == 0 && feel.CameraOffset.localPosition.sqrMagnitude < .000001f,
                "the pool is inactive and the camera offset is zero two seconds after rapid retriggers");
            Check(TransformRestored(target, basePosition, baseScale),
                "rapid target retriggers produce no cumulative scale or position drift");
            Check(Matches(tile.transform, tileBefore),
                "the physical ground tile remains unchanged after feedback cleanup");
            Check(string.Equals(JsonUtility.ToJson(game.State), stateBefore, StringComparison.Ordinal) &&
                  ByteArraysEqual(saveBefore, File.Exists(game.SavePath) ? File.ReadAllBytes(game.SavePath) : Array.Empty<byte>()),
                "pure visual Play calls change neither core state nor the isolated save bytes");
        }

        IEnumerator VerifyUiAtPausedTimescale()
        {
            FeelUiFeedback.ResetTestCounters();
            game.SetSpeed(0);
            Time.timeScale = 0f;
            Button menuButton = FindButton("Button_Menu");
            Require(menuButton != null && menuButton.gameObject.activeInHierarchy,
                "the menu button is available for native EventSystem input");
            Vector3 buttonBaseScale = menuButton.transform.localScale;
            int nativeBefore = NativePlayCount();
            PointerEventData pointer;
            GameObject hit;
            PreparePointer(menuButton, out pointer, out hit);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.045f);
            Check(Vector3.Distance(menuButton.transform.localScale, buttonBaseScale) > .002f,
                "native pointer-down produces a visible Feel button press");
            Check(NativePlayCount() > nativeBefore && menuButton.GetComponentsInChildren<MMF_Player>(true).Any(player => player.IsPlaying),
                "the pressed menu button runs its real unscaled MMF_Player");

            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerExitHandler);
            Transform menuOverlay = Root("MenuOverlay");
            Require(menuOverlay != null && menuOverlay.gameObject.activeInHierarchy && game.ModalOpen,
                "native EventSystem click opens the menu as a modal");
            FeelUiFeedback panelFeedback = menuOverlay.GetComponentsInChildren<FeelUiFeedback>(true)
                .FirstOrDefault(candidate => candidate.Role == FeelUiFeedback.FeedbackRole.Panel);
            Require(panelFeedback != null, "the opened menu owns a Feel panel feedback component");
            CanvasGroup panelGroup = panelFeedback.GetComponent<CanvasGroup>();
            Vector3 panelBaseScale = Vector3.one;
            bool sawPanelFade = false;
            bool sawPanelMotion = false;
            bool sawPanelPlayer = false;
            string sequence = "ui-open-frames";
            Directory.CreateDirectory(Path.Combine(output, sequence));
            for (int index = 0; index < SequenceFrames; index++)
            {
                yield return new WaitForEndOfFrame();
                panelGroup = panelFeedback.GetComponent<CanvasGroup>();
                sawPanelFade |= panelGroup != null && panelGroup.alpha < .985f;
                sawPanelMotion |= Vector3.Distance(panelFeedback.transform.localScale, panelBaseScale) > .002f;
                sawPanelPlayer |= panelFeedback.GetComponentsInChildren<MMF_Player>(true).Any(player => player.IsPlaying);
                WriteFrame(sequence, index);
            }
            Check(Mathf.Approximately(Time.timeScale, 0f) && FeelUiFeedback.PlaysRequested > 0 &&
                  FeelUiFeedback.PanelEntrancesPlayed == 1,
                "button and panel feedback keep running while Unity timeScale is zero");
            Check(sawPanelFade && sawPanelMotion && sawPanelPlayer,
                "menu opening has real MMF_Player fade and pop states between closed and settled");
            yield return new WaitForSecondsRealtime(.35f);
            panelGroup = panelFeedback.GetComponent<CanvasGroup>();
            Check(panelGroup != null && panelGroup.alpha > .995f &&
                  Vector3.Distance(panelFeedback.transform.localScale, panelBaseScale) < .001f,
                "menu panel settles at full alpha and its base scale");
            Check(RectInsideScreen(panelFeedback.transform as RectTransform, 1f),
                "settled menu card remains within the 1280x720 window");
            Check(game.IsScreenPointOverUI(new Vector2(Screen.width * .5f, Screen.height * .5f)),
                "the open menu blocks central world input");
            yield return Capture("03-menu-open-settled-1280x720.png");

            Click("Button_ReducedMotion");
            yield return null;
            Check(FeelUiFeedback.ReducedMotion,
                "the rendered reduced-motion menu toggle updates the shared Feel profile");
            Click("Button_MenuClose");
            yield return null;
            Check(!game.ModalOpen && (menuOverlay == null || !menuOverlay.gameObject.activeInHierarchy),
                "the menu closes through its native rendered close button");
            Time.timeScale = 1f;
            Check(Vector3.Distance(menuButton.transform.localScale, buttonBaseScale) < .001f,
                "the menu button restores its base scale after pointer feedback");
        }

        IEnumerator VerifyReducedMotion(int buildX, int buildZ)
        {
            Require(FeelUiFeedback.ReducedMotion && FeelDirector.ReducedMotion,
                "reduced motion selected in the menu is shared by world and UI feedback");
            Transform target = game.Board.ModelAt(buildX, buildZ);
            Require(target != null, "reduced-motion world target exists");
            Vector3 basePosition = BoardView.Position(buildX, buildZ);
            Vector3 baseScale = Vector3.one;
            float maximumScaleDelta = 0f;
            float maximumCameraDelta = 0f;
            int nativeBefore = NativePlayCount();
            feel.Play(FeelCue.Build, target.position, target);
            for (int frame = 0; frame < 12; frame++)
            {
                yield return new WaitForEndOfFrame();
                maximumScaleDelta = Mathf.Max(maximumScaleDelta, Vector3.Distance(target.localScale, baseScale));
                maximumCameraDelta = Mathf.Max(maximumCameraDelta,
                    feel.CameraOffset.localPosition.magnitude + Quaternion.Angle(feel.CameraOffset.localRotation, Quaternion.identity) * .01f);
            }
            Check(NativePlayCount() > nativeBefore,
                "reduced-motion feedback still uses a real MMF_Player");
            Check(maximumCameraDelta < .001f && maximumScaleDelta < .08f,
                "reduced motion disables camera shake and large target scaling");
            yield return new WaitForSecondsRealtime(2f);
            Check(feel.ActiveWorldEffects == 0 && feel.CameraOffset.localPosition.sqrMagnitude < .000001f &&
                  Quaternion.Angle(feel.CameraOffset.localRotation, Quaternion.identity) < .01f,
                "reduced-motion effects leave an inactive pool and zero camera offset after two seconds");
            Check(TransformRestored(target, basePosition, baseScale),
                "reduced-motion target returns to its exact base transform");
            FeelUiFeedback.ReducedMotion = false;
        }

        IEnumerator VerifyResearchCompletionEdge()
        {
            Require(game.State.ActiveResearch == TechId.Education && game.State.ResearchDaysRemaining == 1 &&
                    !TechCatalog.Has(game.State, TechId.Education),
                "research completion starts from the staged 17-technology edge");
            game.NotifyWorldSelection();
            yield return null;
            int before = feel.CountFor(FeelCue.ResearchComplete);
            int nativeBefore = NativePlayCount();
            game.Sim.Tick();
            game.RefreshWorld(false);
            game.NotifyWorldSelection();
            yield return null;
            Check(TechCatalog.Has(game.State, TechId.Education) && game.State.ActiveResearch == TechId.None,
                "the real simulation tick completes the staged research");
            Check(feel.CountFor(FeelCue.ResearchComplete) == before + 1 && NativePlayCount() > nativeBefore,
                "research completion edge plays one real native feedback");
            for (int index = 0; index < 4; index++)
            {
                game.RefreshWorld(false);
                game.NotifyWorldSelection();
                yield return null;
            }
            Check(feel.CountFor(FeelCue.ResearchComplete) == before + 1,
                "repeated refreshes do not replay an already observed research completion");
            yield return new WaitForSecondsRealtime(2f);
            Check(feel.ActiveWorldEffects == 0 && feel.CameraOffset.localPosition.sqrMagnitude < .000001f,
                "celebration feedback also returns the world pool and camera offset to rest");
        }

        IEnumerator VerifyCameraCinematics()
        {
            OrbitCamera orbit = game.CameraRig;
            Require(orbit != null && orbit.Cinematics != null && orbit.Camera != null && orbit.Camera.orthographic,
                "the orthographic OrbitCamera exposes its CameraCinematics controller");
            CameraCinematics cinematics = orbit.Cinematics;
            MMF_Player cameraPlayer = cinematics.GetComponentsInChildren<MMF_Player>(true)
                .FirstOrDefault(player => player != null && player.gameObject.name == "Camera cinematics MMF player");
            Require(cameraPlayer != null,
                "CameraCinematics owns a dedicated native MMF_Player");

            Vector3 restoreFocus = orbit.AuthoredFocus;
            float restoreSize = orbit.AuthoredSize;
            bool restoreReducedMotion = FeelDirector.ReducedMotion;
            string coreBefore = JsonUtility.ToJson(game.State);
            byte[] saveBefore = File.Exists(game.SavePath) ? File.ReadAllBytes(game.SavePath) : Array.Empty<byte>();

            FeelUiFeedback.ReducedMotion = false;
            game.SetSpeed(0);
            orbit.Home();
            yield return new WaitForSecondsRealtime(.8f);
            Check(Vector3.Distance(orbit.AuthoredFocus, Vector3.zero) < .001f &&
                  Mathf.Abs(orbit.AuthoredSize - 7f) < .001f &&
                  Mathf.Abs(orbit.DisplayedSize - 7f) < .02f,
                "Town Hall cinematic starts from the authored 7-unit home view");

            int townHallCountBefore = cinematics.PlayCount;
            int townHallNativeBefore = cameraPlayer.PlayCount;
            cinematics.PlayTownHall(BoardView.Position(10, 10));
            Require(cinematics.IsActive && cinematics.PlayCount == townHallCountBefore + 1,
                "PlayTownHall starts one camera cinematic sequence");
            float minimumDesiredSize = cinematics.Size;
            float minimumDisplayedSize = orbit.DisplayedSize;
            bool sawTownHallNativePlaying = false;
            const int zoomFrames = 8;
            const string zoomDirectory = "camera-zoom-frames";
            Directory.CreateDirectory(Path.Combine(output, zoomDirectory));
            for (int frame = 0; frame < zoomFrames; frame++)
            {
                yield return new WaitForSecondsRealtime(.06f);
                yield return new WaitForEndOfFrame();
                minimumDesiredSize = Mathf.Min(minimumDesiredSize, cinematics.Size);
                minimumDisplayedSize = Mathf.Min(minimumDisplayedSize, orbit.DisplayedSize);
                sawTownHallNativePlaying |= cameraPlayer.IsPlaying;
                WriteFrame(zoomDirectory, frame);
            }
            results.Add("CAPTURE " + zoomFrames + " actual Town Hall zoom frames in " + zoomDirectory +
                " at " + Screen.width + "x" + Screen.height);
            Check(cameraPlayer.PlayCount > townHallNativeBefore && sawTownHallNativePlaying,
                "Town Hall zoom is driven by the dedicated native MMF_Player");
            Check(minimumDesiredSize >= 3.65f && minimumDesiredSize <= 4.05f && minimumDisplayedSize < 5.5f,
                "Town Hall view reaches the authored approximately 3.8-unit close-up");
            yield return new WaitForSecondsRealtime(1.25f);
            Check(!cinematics.IsActive &&
                  Vector3.Distance(orbit.AuthoredFocus, Vector3.zero) < .001f &&
                  Mathf.Abs(orbit.AuthoredSize - 7f) < .001f,
                "Town Hall cinematic releases ownership without changing the authored home pose");
            yield return new WaitForSecondsRealtime(.75f);
            Check(Vector3.Distance(orbit.DisplayedFocus, Vector3.zero) < .02f &&
                  Mathf.Abs(orbit.DisplayedSize - 7f) < .02f,
                "Town Hall close-up returns the displayed camera to the 7-unit home view");

            Vector3 teachingTarget = new Vector3(2.4f, 0f, -1.8f);
            int teachingCountBefore = cinematics.PlayCount;
            int teachingNativeBefore = cameraPlayer.PlayCount;
            Vector3 teachingStart = orbit.DisplayedFocus;
            cinematics.FocusTeaching(teachingTarget, 4.8f);
            Require(cinematics.IsActive && cinematics.PlayCount == teachingCountBefore + 1,
                "FocusTeaching starts one camera cinematic sequence");
            yield return new WaitForSecondsRealtime(.24f);
            Check(Vector3.Distance(cinematics.Focus, teachingTarget) < Vector3.Distance(teachingStart, teachingTarget) &&
                  cinematics.Size < 7f && cameraPlayer.IsPlaying,
                "FocusTeaching moves its Feel pose toward the teaching target mid-animation");
            yield return new WaitForSecondsRealtime(.35f);
            Check(!cinematics.IsActive && cameraPlayer.PlayCount > teachingNativeBefore &&
                  Vector3.Distance(orbit.AuthoredFocus, teachingTarget) < .001f &&
                  Mathf.Abs(orbit.AuthoredSize - 4.8f) < .001f,
                "completed teaching focus commits its target and zoom to OrbitCamera");
            yield return new WaitForSecondsRealtime(.75f);
            Check(Vector3.Distance(orbit.DisplayedFocus, teachingTarget) < .02f &&
                  Mathf.Abs(orbit.DisplayedSize - 4.8f) < .02f,
                "the displayed camera settles on the committed teaching pose");

            int panPulseBefore = cinematics.PlayCount;
            cinematics.Pulse(FeelCue.EraAdvance);
            Require(cinematics.IsActive && cinematics.PlayCount == panPulseBefore + 1,
                "a major Feel cue starts a camera zoom pulse before manual-pan cancellation");
            yield return new WaitForSecondsRealtime(.18f);
            Vector3 displayedBeforePan = orbit.DisplayedFocus;
            float sizeBeforePan = orbit.DisplayedSize;
            orbit.PanScreen(new Vector2(36f, -18f));
            Check(!cinematics.IsActive && Mathf.Abs(orbit.AuthoredSize - sizeBeforePan) < .01f &&
                  Vector3.Distance(orbit.DisplayedFocus, displayedBeforePan) < .002f &&
                  Mathf.Abs(orbit.DisplayedSize - sizeBeforePan) < .002f,
                "manual PanScreen aborts the pulse and preserves the actually displayed camera pose without a jump");
            int suppressedAfterPan = cinematics.PlayCount;
            cinematics.Pulse(FeelCue.EraAdvance);
            Check(!cinematics.IsActive && cinematics.PlayCount == suppressedAfterPan,
                "manual pan suppresses an immediate camera pulse");
            yield return new WaitForSecondsRealtime(2.75f);
            cinematics.Pulse(FeelCue.EraAdvance);
            Check(!cinematics.IsActive && cinematics.PlayCount == suppressedAfterPan,
                "manual-input pulse suppression remains active shortly before three seconds");
            yield return new WaitForSecondsRealtime(.4f);
            cinematics.Pulse(FeelCue.EraAdvance);
            Require(cinematics.IsActive && cinematics.PlayCount == suppressedAfterPan + 1,
                "camera pulses resume after the three-second manual-input suppression window");

            yield return new WaitForSecondsRealtime(.18f);
            Vector3 displayedBeforeZoom = orbit.DisplayedFocus;
            float displayedSizeBeforeZoom = orbit.DisplayedSize;
            orbit.Zoom(.90f);
            Check(!cinematics.IsActive &&
                  Vector3.Distance(orbit.AuthoredFocus, displayedBeforeZoom) < .01f &&
                  Vector3.Distance(orbit.DisplayedFocus, displayedBeforeZoom) < .002f &&
                  Mathf.Abs(orbit.DisplayedSize - displayedSizeBeforeZoom) < .002f,
                "manual Zoom aborts the pulse, commits its displayed focus, and keeps the visible pose continuous");
            int suppressedAfterZoom = cinematics.PlayCount;
            cinematics.Pulse(FeelCue.ResearchComplete);
            Check(!cinematics.IsActive && cinematics.PlayCount == suppressedAfterZoom,
                "manual zoom also suppresses an immediate camera pulse");
            yield return new WaitForSecondsRealtime(3.15f);
            cinematics.Pulse(FeelCue.ResearchComplete);
            Require(cinematics.IsActive && cinematics.PlayCount == suppressedAfterZoom + 1,
                "camera pulse suppression expires after approximately three seconds");
            yield return new WaitForSecondsRealtime(.8f);

            cinematics.Pulse(FeelCue.GoalComplete);
            Require(cinematics.IsActive, "a camera pulse is active before programmatic Home reset");
            orbit.Home();
            Check(!cinematics.IsActive && Vector3.Distance(orbit.AuthoredFocus, Vector3.zero) < .001f &&
                  Mathf.Abs(orbit.AuthoredSize - 7f) < .001f,
                "programmatic Home resets cinematic ownership and authored pose");
            cinematics.Pulse(FeelCue.GoalComplete);
            Require(cinematics.IsActive, "a camera pulse is active before programmatic SetZoom reset");
            orbit.SetZoom(6.1f);
            Check(!cinematics.IsActive && Mathf.Abs(orbit.AuthoredSize - 6.1f) < .001f,
                "programmatic SetZoom resets cinematic ownership and commits its requested size");
            yield return new WaitForSecondsRealtime(.8f);

            cinematics.Reset();
            yield return new WaitForSecondsRealtime(.8f);
            orbit.SetZoom(1.7f);
            yield return new WaitForSecondsRealtime(.8f);
            Check(Mathf.Abs(orbit.DisplayedSize-1.7f)<.015f,
                "close zoom settles below the previous cinematic minimum");
            orbit.PanScreen(new Vector2(12f,-6f));
            Check(orbit.AuthoredSize<1.8f && orbit.AuthoredSize>=OrbitCamera.MinimumZoom,
                "manual pan preserves close zoom instead of forcing a zoom-out");
            yield return new WaitForSecondsRealtime(.25f);
            orbit.Zoom(.96f);
            Check(orbit.AuthoredSize<1.7f && orbit.AuthoredSize>=OrbitCamera.MinimumZoom,
                "repeated manual zoom shares the same close-zoom limit as cinematics");
            yield return new WaitForSecondsRealtime(3.15f);
            float closeBase=orbit.AuthoredSize;
            cinematics.Pulse(FeelCue.ResearchComplete);
            Require(cinematics.IsActive,"close zoom can play a normal Feel pulse after manual suppression expires");
            yield return new WaitForSecondsRealtime(.15f);
            Check(cinematics.Size<1.8f && cinematics.Size>=OrbitCamera.MinimumZoom,
                "a Feel pulse stays inside the current close-zoom range");
            yield return new WaitForSecondsRealtime(.9f);
            Check(Mathf.Abs(orbit.AuthoredSize-closeBase)<.015f && orbit.DisplayedSize<1.8f,
                "close zoom remains intact after the pulse finishes");
            orbit.SetZoom(6.1f);
            yield return new WaitForSecondsRealtime(.8f);

            Vector3 reducedFocus = orbit.AuthoredFocus;
            float reducedSize = orbit.AuthoredSize;
            Vector3 reducedDisplayedFocus = orbit.DisplayedFocus;
            float reducedDisplayedSize = orbit.DisplayedSize;
            int reducedPlayCount = cinematics.PlayCount;
            FeelUiFeedback.ReducedMotion = true;
            cinematics.PlayTownHall(new Vector3(-3f, 0f, 3f));
            cinematics.FocusTeaching(new Vector3(3f, 0f, -3f), 4.8f);
            cinematics.Pulse(FeelCue.EraAdvance);
            yield return new WaitForSecondsRealtime(.25f);
            Check(!cinematics.IsActive && cinematics.PlayCount == reducedPlayCount &&
                  Vector3.Distance(orbit.AuthoredFocus, reducedFocus) < .001f &&
                  Mathf.Abs(orbit.AuthoredSize - reducedSize) < .001f,
                "reduced motion starts no Town Hall, teaching, or pulse camera animation");
            Check(Vector3.Distance(orbit.DisplayedFocus, reducedDisplayedFocus) < .02f &&
                  Mathf.Abs(orbit.DisplayedSize - reducedDisplayedSize) < .02f,
                "reduced motion performs no automatic pan or zoom");

            FeelUiFeedback.ReducedMotion = restoreReducedMotion;
            cinematics.Reset();
            orbit.CommitFocus(restoreFocus, restoreSize);
            yield return new WaitForSecondsRealtime(.85f);
            Check(Vector3.Distance(orbit.AuthoredFocus, restoreFocus) < .001f &&
                  Mathf.Abs(orbit.AuthoredSize - restoreSize) < .001f &&
                  Vector3.Distance(orbit.DisplayedFocus, restoreFocus) < .03f &&
                  Mathf.Abs(orbit.DisplayedSize - restoreSize) < .03f,
                "camera cinematic smoke restores the pre-test authored and displayed pose");
            Check(string.Equals(JsonUtility.ToJson(game.State), coreBefore, StringComparison.Ordinal) &&
                  ByteArraysEqual(saveBefore, File.Exists(game.SavePath) ? File.ReadAllBytes(game.SavePath) : Array.Empty<byte>()),
                "camera-only cinematics change no economy, simulation, or isolated save data");
        }

        void FindBuildSite(out int x, out int z)
        {
            Cell candidate = game.State.Cells
                .Where(cell => cell.Building == BuildingKind.None && game.Sim.CanBuild(BuildingKind.House, cell.X, cell.Z, out _))
                .OrderBy(cell => Mathf.Abs(cell.X - 10) + Mathf.Abs(cell.Z - 10))
                .FirstOrDefault();
            Require(candidate != null, "shared city has an affordable legal house site near the camera focus");
            x = candidate.X;
            z = candidate.Z;
        }

        void FindFactorySite(out int x, out int z)
        {
            for (int radius = 0; radius < 42; radius++)
            for (int zCandidate = 0; zCandidate < game.Factory.State.Height; zCandidate++)
            for (int xCandidate = 0; xCandidate < game.Factory.State.Width; xCandidate++)
            {
                if (Mathf.Abs(xCandidate - 21) + Mathf.Abs(zCandidate - 21) != radius) continue;
                if (!game.Factory.CanPlaceAt(xCandidate, zCandidate, out _)) continue;
                x = xCandidate;
                z = zCandidate;
                return;
            }
            throw new InvalidOperationException("Shared city has no legal affordable belt site.");
        }

        void SnapshotGameplayColliders()
        {
            tileSnapshots.Clear();
            bool allHaveColliders = true;
            foreach (TileHandle tile in FindObjectsByType<TileHandle>(FindObjectsInactive.Include))
            {
                allHaveColliders &= tile.GetComponent<Collider>() != null;
                tileSnapshots[tile] = Snapshot(tile.transform);
            }
            Require(allHaveColliders, "each gameplay tile keeps a picking collider");
            Require(tileSnapshots.Count == game.State.Size * game.State.Size,
                "all 21x21 gameplay picking colliders are present before feedback tests");
        }

        void VerifyGameplayColliders()
        {
            Check(tileSnapshots.All(pair => pair.Key != null && pair.Key.GetComponent<Collider>() != null &&
                                             Matches(pair.Key.transform, pair.Value)),
                "visual feedback never changes gameplay tile collider position, rotation, or scale");
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                Require(VisibleFrame(texture), "capture is visible and non-black: " + name);
                File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                results.Add("CAPTURE " + name + " " + texture.width + "x" + texture.height);
            }
            finally
            {
                if (texture != null) Destroy(texture);
            }
        }

        void WriteFrame(string directory, int index)
        {
            Texture2D frame = null;
            try
            {
                frame = ScreenCapture.CaptureScreenshotAsTexture();
                Require(VisibleFrame(frame), "captured animation frame is visible: " + directory + "/" + index);
                File.WriteAllBytes(
                    Path.Combine(output, directory, "frame-" + index.ToString("D3") + ".png"),
                    frame.EncodeToPNG());
            }
            finally
            {
                if (frame != null) Destroy(frame);
            }
            if (index == SequenceFrames - 1)
                results.Add("CAPTURE " + SequenceFrames + " actual frames in " + directory + " at " + Screen.width + "x" + Screen.height);
        }

        static bool VisibleFrame(Texture2D texture)
        {
            if (texture == null || texture.width < 320 || texture.height < 180) return false;
            Color32[] pixels = texture.GetPixels32();
            int stride = Mathf.Max(1, pixels.Length / 4096);
            int samples = 0;
            int lit = 0;
            var colors = new HashSet<int>();
            for (int index = 0; index < pixels.Length; index += stride)
            {
                Color32 pixel = pixels[index];
                samples++;
                if (pixel.r + pixel.g + pixel.b >= 24) lit++;
                colors.Add((pixel.r >> 4) << 8 | (pixel.g >> 4) << 4 | (pixel.b >> 4));
            }
            return lit >= samples / 20 && colors.Count >= 16;
        }

        void PreparePointer(Button button, out PointerEventData pointer, out GameObject hit)
        {
            if (EventSystem.current == null) throw new InvalidOperationException("EventSystem unavailable");
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                throw new InvalidOperationException("UI button unavailable: " + (button == null ? "null" : button.name));
            Canvas.ForceUpdateCanvases();
            RectTransform rect = button.transform as RectTransform;
            if (rect == null) throw new InvalidOperationException("Button has no RectTransform: " + button.name);
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 center = new Vector2((corners[0].x + corners[2].x) * .5f, (corners[0].y + corners[2].y) * .5f);
            pointer = new PointerEventData(EventSystem.current)
            {
                position = center,
                button = PointerEventData.InputButton.Left
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || !IsButtonHit(button, hits[0].gameObject))
                throw new InvalidOperationException("Button is not the first native EventSystem target: " + button.name);
            hit = hits[0].gameObject;
        }

        void Click(string name)
        {
            Button button = FindButton(name);
            PreparePointer(button, out PointerEventData pointer, out GameObject hit);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerExitHandler);
            Check(true, "EventSystem raycast clicks " + name);
        }

        Button FindButton(string name) => hud == null ? null :
            hud.GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == name);

        Transform Root(string name) => hud == null ? null :
            hud.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);

        static bool IsButtonHit(Button button, GameObject hit) =>
            hit == button.gameObject || hit.transform.IsChildOf(button.transform);

        static bool RectInsideScreen(RectTransform rect, float tolerance)
        {
            if (rect == null) return false;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[0].x >= -tolerance && corners[0].y >= -tolerance &&
                   corners[2].x <= Screen.width + tolerance && corners[2].y <= Screen.height + tolerance &&
                   corners[2].x - corners[0].x > 1f && corners[2].y - corners[0].y > 1f;
        }

        IEnumerator WaitForFeedbackToSettle(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            yield return null;
        }

        static int NativePlayCount() => FindObjectsByType<MMF_Player>(FindObjectsInactive.Include)
            .Where(player => player != null)
            .Sum(player => player.PlayCount);

        static TransformSnapshot Snapshot(Transform target) => new TransformSnapshot
        {
            Position = target.position,
            Rotation = target.rotation,
            Scale = target.localScale
        };

        static bool Matches(Transform target, TransformSnapshot expected) =>
            Vector3.Distance(target.position, expected.Position) < .0001f &&
            Quaternion.Angle(target.rotation, expected.Rotation) < .01f &&
            Vector3.Distance(target.localScale, expected.Scale) < .0001f;

        static bool TransformChanged(Transform target, Vector3 baseLocalPosition, Vector3 baseLocalScale, float epsilon) =>
            target != null && (Vector3.Distance(target.localPosition, baseLocalPosition) > epsilon ||
                               Vector3.Distance(target.localScale, baseLocalScale) > epsilon);

        static bool TransformRestored(Transform target, Vector3 baseLocalPosition, Vector3 baseLocalScale) =>
            target != null && Vector3.Distance(target.localPosition, baseLocalPosition) < .001f &&
            Vector3.Distance(target.localScale, baseLocalScale) < .001f;

        static bool SequenceEqual(IList<float> actual, IList<float> expected)
        {
            if (actual == null || expected == null || actual.Count != expected.Count) return false;
            for (int index = 0; index < actual.Count; index++)
                if (!Approximately(actual[index], expected[index])) return false;
            return true;
        }

        static bool ByteArraysEqual(byte[] first, byte[] second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null || first.Length != second.Length) return false;
            for (int index = 0; index < first.Length; index++)
                if (first[index] != second[index]) return false;
            return true;
        }

        static bool Approximately(float first, float second) => Mathf.Abs(first - second) <= .001f;

        void RestoreOriginalCameraPose()
        {
            if (!originalCameraPoseCaptured || game == null || game.CameraRig == null) return;
            game.CameraRig.Cinematics?.Reset();
            game.CameraRig.CommitFocus(originalCameraFocus, originalCameraSize);
        }

        void Check(bool okay, string message)
        {
            if (okay)
            {
                results.Add("PASS " + message);
                Debug.Log("FEEL PASS: " + message);
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
            Time.timeScale = originalTimeScale;
            FeelUiFeedback.ReducedMotion = originalReducedMotion;
            RestoreOriginalCameraPose();
            game?.SetSpeed(0);
            Application.logMessageReceived -= OnLog;
            int exitCode = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILD " + Application.buildGUID +
                " BUILDGUID " + Application.buildGUID + " UNITY " + Application.unityVersion);
            results.Add("ERRORS " + errors.Count);
            results.Add("SUPPRESSED_ERRORS " + suppressedErrors);
            results.AddRange(errors);
            results.Add("EXIT " + exitCode);
            try { File.WriteAllLines(Path.Combine(output, "feel-results.txt"), results); }
            catch (Exception exception)
            {
                Debug.LogError("Could not write feel-results.txt: " + exception.Message);
                exitCode = 1;
            }
            Debug.Log(exitCode == 0 ? "RIVERWORKS_FEEL_SMOKE_PASS" : "RIVERWORKS_FEEL_SMOKE_FAIL");
            Application.Quit(exitCode);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Opt-in executable verification for the compact, world-first city HUD.</summary>
    public sealed class CompactHudSmokeTest : MonoBehaviour
    {
        const int CoverageColumns = 40;
        const int CoverageRows = 24;
        const int MaximumErrors = 64;
        const int MaximumErrorCharacters = 4096;

        GameController game;
        Hud hud;
        string output;
        bool finished;
        bool baselineCaptureOnly;
        int baselineCaptureCount;
        UiSmokeViewport smokeViewport;
        int suppressedErrors;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();

        public void Initialize(GameController controller)
        {
            game = controller;
            string[] arguments = Environment.GetCommandLineArgs();
            baselineCaptureOnly = Array.IndexOf(arguments, "-riverworks-ui-baseline") >= 0;
            int outputAt = Array.IndexOf(arguments, "-riverworks-output");
            output = outputAt >= 0 && outputAt + 1 < arguments.Length
                ? arguments[outputAt + 1]
                : Path.Combine(Application.persistentDataPath, "CompactUiSmoke");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
            StartCoroutine(GuardedRun());
        }

        void OnDestroy()
        {
            smokeViewport?.Dispose();
            smokeViewport = null;
            Application.logMessageReceived -= OnLog;
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
            Require(Array.IndexOf(arguments, "-riverworks-compact-ui-smoke") >= 0,
                "compact HUD smoke only runs behind its explicit command flag");
            Require(game.SmokeMode, "compact HUD smoke uses the controller's isolated smoke save slot");
            Require(!string.Equals(Path.GetFileName(game.SavePath), "city-v1.json", StringComparison.OrdinalIgnoreCase),
                "compact HUD smoke never targets the player's city save");

            game.SetSpeed(0);
            // Hidden Windows launches need a resize before their first usable rendered frame.
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.35f);
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.45f);
            yield return new WaitForEndOfFrame();

            GameState fixture = SharedCityScenario.Create();
            Require(fixture.Cells.Any(cell => cell.Building == BuildingKind.House) &&
                    fixture.Factory.Entities.Count > 0 && fixture.Technologies.Count == 18,
                "shared fixture contains a rich city, factory equipment, and all 18 technologies");
            Require(SaveStore.TrySave(game.SavePath, fixture, out string saveError),
                "shared city fixture saves before UI verification: " + saveError);
            game.LoadGame();
            game.CameraRig.Home();
            game.CameraRig.SetZoom(6.3f);
            game.RefreshWorld(true);
            game.SetSpeed(1);
            float peopleDeadline = Time.realtimeSinceStartup + 8f;
            while (game.PeopleOnStreet == 0 && Time.realtimeSinceStartup < peopleDeadline) yield return null;
            Require(game.PeopleOnStreet > 0, "shared-city residents become visible before compact HUD captures");
            game.SetSpeed(0);
            // Let the load notice finish fading so the idle screenshots show the persistent HUD only.
            yield return new WaitForSecondsRealtime(4.6f);
            yield return new WaitForEndOfFrame();

            hud = game.CityHud;
            Require(hud != null && hud.gameObject.activeInHierarchy, "one active compact city HUD is available");
            Require(EventSystem.current != null, "an EventSystem is available for real UI raycasts");

            if (baselineCaptureOnly)
            {
                results.Add("MODE BASELINE_CAPTURE_ONLY");
                results.Add("VALIDATION NOT_RUN - screenshots are BEFORE-reference evidence only");
                results.Add("CAPTURE_MODE OFFSCREEN_RENDER_TARGET");
                results.Add("DEVICE_VALIDATION NOT_RUN - captures do not prove a real window or Android layout");
                yield return CaptureBaselineStates();
                yield break;
            }

            VerifyHudAssets();

            int[,] resolutions = { { 1600, 900 }, { 1280, 720 } };
            for (int resolution = 0; resolution < resolutions.GetLength(0); resolution++)
            {
                int width = resolutions[resolution, 0];
                int height = resolutions[resolution, 1];
                string size = width + "x" + height;
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(.45f);
                yield return new WaitForEndOfFrame();

                VerifyCompactChrome(size);
                VerifyIdleState(size);
                MeasureCoverage("idle-" + size, .20f);
                yield return Capture(size + "-00-idle.png");

                VerifyCentralWorldClick(size);
                yield return new WaitForEndOfFrame();
                CloseInspectorThroughUi("central world click at " + size);

                SelectFixtureHouse(size);
                yield return new WaitForEndOfFrame();
                Require(IsActive("Inspector"), "building selection alone opens the inspector at " + size);
                MeasureCoverage("selected-" + size, .30f);
                yield return Capture(size + "-01-building-selection.png");
                CloseInspectorThroughUi("building selection at " + size);

                Click("Button_주거");
                yield return new WaitForEndOfFrame();
                Require(IsActive("BuildChoices"), "requesting the housing category expands build choices at " + size);
                Require(IsButtonFirstHit("Button_주택"), "the housing choice is visible and raycastable at " + size);
                yield return Capture(size + "-02-build-tray.png");
                Click("Button_BuildCollapse");
                yield return new WaitForEndOfFrame();
                Require(!IsActive("BuildChoices"), "the collapse button closes the build tray at " + size);

                Click("Button_주거");
                Click("Button_주택");
                yield return new WaitForEndOfFrame();
                Require(game.SelectedTool == BuildingKind.House, "a raycast housing choice selects the house tool at " + size);
                Require(!IsActive("BuildChoices"), "selecting a city tool collapses the build tray at " + size);
                Require(!game.IsScreenPointOverUI(ScreenCenter), "a selected city tool leaves the central world clear at " + size);
                Click("Button_ToolCancel");
                Require(game.SelectedTool == BuildingKind.None && !game.DemolitionMode,
                    "the compact tool cancel clears the city tool at " + size);

                yield return VerifyFactoryRotateAndCancel(size);

                yield return ExerciseObjectives(size);
                yield return ExerciseModal("Menu", "Button_Menu", "MenuOverlay", "Button_MenuClose", size);
                yield return ExerciseModal("Territory", "Button_Territory", "TerritoryOverlay", "Button_TerritoryClose", size);
                yield return ExerciseModal("Trade", "Button_Trade", "TradeOverlay", "Button_TradeClose", size);
                yield return ExerciseModal("Overview", "Button_Overview", "OverviewOverlay", "Button_OverviewClose", size);

                if (resolution == 0)
                {
                    yield return VerifyNewCityCancel();
                }

                SelectFixtureFactory(size);
                yield return new WaitForEndOfFrame();
                Require(IsActive("Inspector") && IsButtonFirstHit("Button_FactoryConfigure"),
                    "factory selection exposes its inspector configuration action at " + size);
                Click("Button_FactoryConfigure");
                yield return new WaitForEndOfFrame();
                Require(game.ModalOpen && IsActive("FactoryConfigureOverlay"),
                    "factory configuration opens as a modal at " + size);
                Require(game.IsScreenPointOverUI(ScreenCenter),
                    "factory configuration blocks the central world at " + size);
                yield return Capture(size + "-08-factory-configure.png");
                Click("Button_FactoryConfigureClose");
                yield return new WaitForEndOfFrame();
                Require(!game.ModalOpen && !IsActive("FactoryConfigureOverlay"),
                    "factory configuration closes and clears ModalOpen at " + size);
                CloseInspectorThroughUi("factory selection at " + size);

                Click("Button_기술 연구");
                yield return new WaitForEndOfFrame();
                Require(game.ResearchOpen && IsActive("ResearchOverlay"),
                    "research opens through its compact top-bar button at " + size);
                Require(game.IsScreenPointOverUI(ScreenCenter), "research blocks the central world at " + size);
                VerifyAllResearchAccessible(size);
                yield return Capture(size + "-09-research.png");
                Click("Button_ResearchClose");
                yield return new WaitForEndOfFrame();
                Require(!game.ResearchOpen && !IsActive("ResearchOverlay"),
                    "research closes through its rendered close button at " + size);
                VerifyIdleState(size + " after panel exercises");
            }

            if (Array.IndexOf(arguments, "-riverworks-compact-ui-moving-frames") >= 0)
                yield return RecordMovingFrames();

            Check(errors.Count == 0, "compact HUD runtime emitted no error, exception, or assertion logs");
            game.SetSpeed(0);
        }

        IEnumerator CaptureBaselineStates()
        {
            int[,] resolutions = { { 1600, 900 }, { 1280, 720 }, { 1024, 768 }, { 2048, 1536 } };
            for (int resolution = 0; resolution < resolutions.GetLength(0); resolution++)
            {
                int width = resolutions[resolution, 0];
                int height = resolutions[resolution, 1];
                string size = width + "x" + height;
                smokeViewport?.Dispose();
                smokeViewport = new UiSmokeViewport(game.CameraRig.Camera, hud.GetComponent<Canvas>(), width, height);
                yield return null;
                Canvas.ForceUpdateCanvases();
                results.Add("VIEWPORT " + size + " CANVAS " +
                    Mathf.RoundToInt(hud.GetComponent<Canvas>().pixelRect.width) + "x" +
                    Mathf.RoundToInt(hud.GetComponent<Canvas>().pixelRect.height) +
                    " RENDER_TEXTURE " + smokeViewport.Width + "x" + smokeViewport.Height);

                ResetBaselineState();
                yield return Capture(size + "-00-idle.png");

                game.InteractCell(9, 7);
                yield return Capture(size + "-01-building-selection.png");
                game.ClearCitySelection();
                game.NotifyWorldSelection();

                InvokeButton("Button_주거");
                yield return Capture(size + "-02-build-tray.png");
                InvokeButton("Button_BuildCollapse");

                InvokeButton("Button_Objectives");
                yield return Capture(size + "-03-objectives.png");
                InvokeButton("Button_ObjectivesClose");

                yield return CaptureBaselineModal(size, "04-menu", "Button_Menu", "Button_MenuClose");
                yield return CaptureBaselineModal(size, "05-overview", "Button_Overview", "Button_OverviewClose");
                yield return CaptureBaselineModal(size, "06-territory", "Button_Territory", "Button_TerritoryClose");
                yield return CaptureBaselineModal(size, "07-trade", "Button_Trade", "Button_TradeClose");

                game.Factory.SelectAt(SharedCityScenario.ProcessorMicroX, SharedCityScenario.ProcessorMicroZ);
                game.NotifyWorldSelection();
                yield return new WaitForEndOfFrame();
                InvokeButton("Button_FactoryConfigure");
                yield return Capture(size + "-08-factory-configure.png");
                InvokeButton("Button_FactoryConfigureClose");
                game.Factory.ClearSelection();
                game.NotifyWorldSelection();

                InvokeButton("Button_기술 연구");
                yield return Capture(size + "-09-research.png");
                InvokeButton("Button_ResearchClose");
                ResetBaselineState();
            }
            results.Add("CAPTURES " + baselineCaptureCount + "/40");
        }

        IEnumerator CaptureBaselineModal(string size, string state, string openButton, string closeButton)
        {
            InvokeButton(openButton);
            yield return Capture(size + "-" + state + ".png");
            InvokeButton(closeButton);
        }

        void ResetBaselineState()
        {
            if (game.ResearchOpen) game.ToggleResearch();
            if (game.HelpOpen) game.ToggleHelp();
            hud.CloseTransientPanels();
            if (IsActive("Objectives")) InvokeButton("Button_ObjectivesClose");
            if (IsActive("BuildChoices")) InvokeButton("Button_BuildCollapse");
            if (game.Factory != null && game.Factory.IsOpen) game.Factory.Close();
            game.ClearConstructionTools();
            game.ClearCitySelection();
            game.NotifyWorldSelection();
        }

        void InvokeButton(string name)
        {
            Button button = FindButton(name);
            if (button == null) throw new InvalidOperationException("Baseline capture button unavailable: " + name);
            button.onClick.Invoke();
            results.Add("BASELINE_ACTION direct Button.onClick " + name);
        }

        IEnumerator ExerciseObjectives(string size)
        {
            Click("Button_Objectives");
            yield return new WaitForEndOfFrame();
            Require(IsActive("Objectives"), "objectives appear only after their button is requested at " + size);
            Require(!game.ModalOpen, "objectives remain a non-modal compact card at " + size);
            Require(!game.IsScreenPointOverUI(ScreenCenter), "objectives leave the central world clear at " + size);
            yield return Capture(size + "-03-objectives.png");
            Click("Button_ObjectivesClose");
            yield return new WaitForEndOfFrame();
            Require(!IsActive("Objectives"), "objectives close through their rendered close button at " + size);
        }

        IEnumerator ExerciseModal(string label, string openButton, string rootName, string closeButton, string size)
        {
            Click(openButton);
            yield return new WaitForEndOfFrame();
            Require(game.ModalOpen && IsActive(rootName), label + " appears only after its button is requested at " + size);
            Require(game.IsScreenPointOverUI(ScreenCenter), label + " blocks the central world at " + size);
            yield return Capture(size + "-04-utility-" + label.ToLowerInvariant() + ".png");
            Click(closeButton);
            yield return new WaitForEndOfFrame();
            Require(!game.ModalOpen && !IsActive(rootName),
                label + " closes through its rendered button and clears ModalOpen at " + size);
        }

        void VerifyCompactChrome(string size)
        {
            RectTransform top = Root("TopBar") as RectTransform;
            RectTransform bottom = Root("BuildBar") as RectTransform;
            Require(top != null && bottom != null, "compact top and bottom bars exist at " + size);
            Check(Mathf.Abs(top.rect.height - 52f) <= 1f,
                "TopBar is 52 design pixels high at " + size + " (actual " + top.rect.height.ToString("0.##") + ")");
            Check(bottom.rect.height <= 60.5f && bottom.offsetMin.y >= -.5f && bottom.offsetMax.y <= 60.5f,
                "BuildBar stays within the bottom 60-design-pixel envelope at " + size +
                " (height " + bottom.rect.height.ToString("0.##") + ", top " + bottom.offsetMax.y.ToString("0.##") + ")");
        }

        void VerifyHudAssets()
        {
            Sprite[] icons =
            {
                HudAssets.Icon(HudAssets.MenuIcon), HudAssets.Icon(HudAssets.CloseIcon),
                HudAssets.Icon(HudAssets.ArrowLeftIcon), HudAssets.Icon(HudAssets.ArrowRightIcon),
                HudAssets.Icon(HudAssets.RotateIcon), HudAssets.Icon(HudAssets.PlayIcon),
                HudAssets.Icon(HudAssets.PauseIcon), HudAssets.Icon(HudAssets.SettingsIcon),
                HudAssets.Icon(HudAssets.InfoIcon), HudAssets.Icon(HudAssets.HomeIcon),
                HudAssets.Icon(HudAssets.MapIcon), HudAssets.Icon(HudAssets.ConfirmIcon)
            };
            Require(HudAssets.Panel != null && HudAssets.Button != null && HudAssets.IconButton != null &&
                    icons.All(icon => icon != null),
                "HUD panel, button, icon-button, and all 12 semantic icon assets load from Resources");
            Image[] activeImages = hud.GetComponentsInChildren<Image>(false);
            int assetBacked = activeImages.Count(image => image.sprite != null &&
                image.sprite.name.StartsWith("HudAssets/", StringComparison.Ordinal));
            Require(assetBacked > 0,
                "active HUD images use loaded HudAssets sprites (" + assetBacked + " active images)");
        }

        void VerifyIdleState(string phase)
        {
            string[] hidden =
            {
                "BuildChoices", "Inspector", "Objectives", "MenuOverlay", "OverviewOverlay",
                "TerritoryOverlay", "TradeOverlay", "ResearchOverlay", "HelpOverlay",
                "NewGameConfirm", "FactoryConfigureOverlay"
            };
            foreach (string rootName in hidden)
                Check(!IsActive(rootName), rootName + " is hidden while idle at " + phase);
            Check(!game.ModalOpen && !game.ResearchOpen && !game.HelpOpen,
                "idle HUD has no modal controller state at " + phase);
            Check(game.SelectedCell == null && game.SelectedFactory == null && string.IsNullOrEmpty(game.ResidentDetails),
                "idle HUD has no inspector selection at " + phase);
        }

        void VerifyCentralWorldClick(string size)
        {
            Vector2 center = ScreenCenter;
            Require(!game.IsScreenPointOverUI(center), "central world point is not covered by idle UI at " + size);
            Ray ray = game.CameraRig.Camera.ScreenPointToRay(center);
            Require(Physics.Raycast(ray, out RaycastHit hit, 200f, 1 << 8) && hit.collider.GetComponent<TileHandle>() != null,
                "central screen point resolves to a real world tile at " + size);
            Require(game.InteractScreenPoint(center), "central screen point is clickable through the controller at " + size);
            Require(IsActive("Inspector"), "central world click produces a visible inspector selection at " + size);
        }

        void SelectFixtureHouse(string size)
        {
            Vector2Int[] houses =
            {
                new Vector2Int(9, 7), new Vector2Int(10, 7),
                new Vector2Int(7, 9), new Vector2Int(7, 11)
            };
            foreach (Vector2Int cell in houses)
            {
                Vector3 screen = game.CameraRig.Camera.WorldToScreenPoint(BoardView.Position(cell.x, cell.y) + Vector3.up * .15f);
                if (!VisibleWorldPoint(screen) || game.IsScreenPointOverUI(screen)) continue;
                if (!game.InteractScreenPoint(screen)) continue;
                if (game.SelectedCell != null && game.SelectedCell.Building == BuildingKind.House)
                {
                    Check(true, "a rendered fixture house is selected through its screen point at " + size);
                    return;
                }
            }
            throw new InvalidOperationException("No visible fixture house could be selected at " + size);
        }

        void SelectFixtureFactory(string size)
        {
            Vector2Int[] candidates =
            {
                new Vector2Int(SharedCityScenario.ProcessorMicroX, SharedCityScenario.ProcessorMicroZ),
                new Vector2Int(SharedCityScenario.DrillMicroX, SharedCityScenario.DrillMicroZ),
                new Vector2Int(SharedCityScenario.ConnectedInletMicroX, SharedCityScenario.ConnectedInletMicroZ)
            };
            foreach (Vector2Int cell in candidates)
            {
                Vector3 screen = game.CameraRig.Camera.WorldToScreenPoint(game.Factory.View.WorldPosition(cell.x, cell.y) + Vector3.up * .18f);
                if (!VisibleWorldPoint(screen) || game.IsScreenPointOverUI(screen)) continue;
                if (!game.InteractScreenPoint(screen)) continue;
                if (game.SelectedFactory != null)
                {
                    Check(true, "a rendered fixture factory is selected through its screen point at " + size);
                    return;
                }
            }
            throw new InvalidOperationException("No visible fixture factory could be selected at " + size);
        }

        IEnumerator VerifyFactoryRotateAndCancel(string size)
        {
            Click("Button_Factory");
            yield return new WaitForEndOfFrame();
            Require(game.Factory.IsOpen && IsActive("BuildChoices"),
                "the factory button requests the factory build tray at " + size);
            string factoryToolButton = "Button_" + FactoryCatalog.Get(FactoryKind.Drill).Name;
            Require(IsButtonFirstHit(factoryToolButton), "the factory tray exposes a raycastable drill choice at " + size);
            Click(factoryToolButton);
            yield return new WaitForEndOfFrame();
            Require(game.Factory.SelectedTool == FactoryKind.Drill && !IsActive("BuildChoices"),
                "selecting a factory tool collapses its tray at " + size);
            int direction = game.Factory.Direction;
            Click("Button_FactoryRotate");
            yield return new WaitForEndOfFrame();
            Require(game.Factory.Direction == (direction + 1) % 4,
                "the compact rotate button rotates the active factory tool at " + size);
            Click("Button_ToolCancel");
            yield return new WaitForEndOfFrame();
            Require(game.Factory.SelectedTool == FactoryKind.None && !game.Factory.RemovalMode,
                "the compact tool cancel clears the factory tool at " + size);
            Click("Button_Factory");
            yield return new WaitForEndOfFrame();
            Require(game.Factory.IsOpen && IsActive("BuildChoices"),
                "the persistent factory category reopens its tray at " + size);
            Click("Button_Factory");
            yield return new WaitForEndOfFrame();
            Require(game.Factory.IsOpen && !IsActive("BuildChoices"),
                "a repeated factory category click collapses its tray at " + size);
            game.Factory.Close();
            yield return new WaitForEndOfFrame();
            Require(!game.Factory.IsOpen, "factory palette cleanup restores the idle city state at " + size);
        }

        IEnumerator VerifyNewCityCancel()
        {
            Click("Button_Menu");
            yield return new WaitForEndOfFrame();
            Require(game.ModalOpen && IsActive("MenuOverlay"), "menu opens before new-city confirmation verification");
            Click("Button_새 도시");
            yield return new WaitForEndOfFrame();
            Require(game.ModalOpen && IsActive("NewGameConfirm"), "new-city confirmation replaces the menu through a rendered button");
            Click("Button_취소");
            yield return new WaitForEndOfFrame();
            Require(!game.ModalOpen && !IsActive("NewGameConfirm"),
                "new-city cancellation closes the confirmation and clears ModalOpen");
        }

        void VerifyAllResearchAccessible(string size)
        {
            TechSpec[] specs = TechCatalog.All.Where(spec => spec != null).ToArray();
            Require(specs.Length == 18, "research catalog still contains exactly 18 technologies at " + size);
            int accessible = 0;
            foreach (TechSpec spec in specs)
            {
                Button button = FindButton("Button_연구 시작_" + spec.Id);
                if (button == null)
                {
                    RecordError("Missing research button for " + spec.Id + " at " + size);
                    continue;
                }
                ScrollRect scroll = button.GetComponentInParent<ScrollRect>();
                bool reached = false;
                if (scroll != null)
                {
                    for (int step = 0; step <= 32 && !reached; step++)
                    {
                        scroll.StopMovement();
                        scroll.verticalNormalizedPosition = 1f - step / 32f;
                        Canvas.ForceUpdateCanvases();
                        reached = IsFirstRaycastTarget(button);
                    }
                }
                else reached = IsFirstRaycastTarget(button);
                if (reached) accessible++;
                else RecordError("Research button cannot be reached by scrolling and raycast: " + spec.Id + " at " + size);
            }
            Require(accessible == 18, "all 18 research cards are reachable and raycastable at " + size + " (18/18)");
        }

        void CloseInspectorThroughUi(string phase)
        {
            Require(IsActive("Inspector"), "inspector is open before its close test after " + phase);
            Click("Button_InspectorClose");
            Require(!IsActive("Inspector"), "inspector closes through its rendered close button after " + phase);
            Require(game.SelectedCell == null && game.SelectedFactory == null && string.IsNullOrEmpty(game.ResidentDetails),
                "inspector close clears city, factory, and resident selections after " + phase);
        }

        void MeasureCoverage(string label, float maximum)
        {
            int blocked = 0;
            int total = CoverageColumns * CoverageRows;
            for (int row = 0; row < CoverageRows; row++)
            for (int column = 0; column < CoverageColumns; column++)
            {
                Vector2 sample = new Vector2(
                    (column + .5f) * Screen.width / CoverageColumns,
                    (row + .5f) * Screen.height / CoverageRows);
                if (game.IsScreenPointOverUI(sample)) blocked++;
            }
            float fraction = blocked / (float)total;
            string count = blocked + "/" + total + " samples (" + (fraction * 100f).ToString("0.00") + "%)";
            Check(fraction <= maximum,
                "COVERAGE " + label + " blocks " + count + ", limit " + (maximum * 100f).ToString("0") + "%");
        }

        void Click(string name)
        {
            Button button = FindButton(name);
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                throw new InvalidOperationException("UI button unavailable: " + name);
            Canvas.ForceUpdateCanvases();
            PointerEventData pointer = PointerAtButtonCenter(button);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || !IsButtonHit(button, hits[0].gameObject))
                throw new InvalidOperationException("UI button is not the first EventSystem raycast target: " + name +
                    (hits.Count == 0 ? " (no hits)" : " (hit " + hits[0].gameObject.name + ")"));
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Check(true, "EventSystem raycast clicks " + name);
        }

        bool IsButtonFirstHit(string name)
        {
            Button button = FindButton(name);
            return button != null && button.gameObject.activeInHierarchy && IsFirstRaycastTarget(button);
        }

        bool IsFirstRaycastTarget(Button button)
        {
            if (button == null || !button.gameObject.activeInHierarchy || EventSystem.current == null) return false;
            Canvas.ForceUpdateCanvases();
            PointerEventData pointer;
            try { pointer = PointerAtButtonCenter(button); }
            catch { return false; }
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            return hits.Count > 0 && IsButtonHit(button, hits[0].gameObject);
        }

        static bool IsButtonHit(Button button, GameObject hit) =>
            hit == button.gameObject || hit.transform.IsChildOf(button.transform);

        static PointerEventData PointerAtButtonCenter(Button button)
        {
            if (EventSystem.current == null) throw new InvalidOperationException("EventSystem unavailable");
            RectTransform rect = button.transform as RectTransform;
            if (rect == null) throw new InvalidOperationException("Button has no RectTransform: " + button.name);
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 center = new Vector2((corners[0].x + corners[2].x) * .5f, (corners[0].y + corners[2].y) * .5f);
            if (corners[2].x - corners[0].x < 1 || corners[2].y - corners[0].y < 1 ||
                center.x < 0 || center.x > Screen.width || center.y < 0 || center.y > Screen.height)
                throw new InvalidOperationException("Button has no visible screen rect: " + button.name);
            return new PointerEventData(EventSystem.current) { position = center, button = PointerEventData.InputButton.Left };
        }

        Button FindButton(string name) => hud == null ? null :
            hud.GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == name);

        Transform Root(string name) => hud == null ? null :
            hud.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);

        bool IsActive(string name)
        {
            Transform root = Root(name);
            return root != null && root.gameObject.activeInHierarchy;
        }

        bool VisibleWorldPoint(Vector3 point) => point.z > 0 && point.x >= 1 && point.y >= 1 &&
            point.x < Screen.width - 1 && point.y < Screen.height - 1;

        Vector2 ScreenCenter => new Vector2(Screen.width * .5f, Screen.height * .5f);

        IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.3f);
            if (baselineCaptureOnly && smokeViewport == null)
            {
                RecordError("Offscreen baseline viewport is unavailable before capture: " + name);
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 5f;
            string lastReason = "no frame captured";
            while (Time.realtimeSinceStartup < deadline)
            {
                if (smokeViewport == null) yield return new WaitForEndOfFrame();
                else yield return null;
                Texture2D texture = null;
                bool captured = false;
                bool fatalSizeMismatch = false;
                try
                {
                    texture = smokeViewport == null
                        ? ScreenCapture.CaptureScreenshotAsTexture()
                        : smokeViewport.Capture();
                    if (baselineCaptureOnly && (texture == null || texture.width != smokeViewport.Width ||
                        texture.height != smokeViewport.Height))
                    {
                        string actual = texture == null ? "null" : texture.width + "x" + texture.height;
                        lastReason = "offscreen texture size " + actual + " does not match requested " +
                            smokeViewport.Width + "x" + smokeViewport.Height;
                        fatalSizeMismatch = true;
                    }
                    else if (VisibleFrame(texture, out lastReason))
                    {
                        File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                        results.Add("CAPTURE " + name + " " + texture.width + "x" + texture.height);
                        if (baselineCaptureOnly) baselineCaptureCount++;
                        captured = true;
                    }
                }
                catch (Exception exception) { lastReason = exception.Message; }
                finally { if (texture != null) Destroy(texture); }
                if (fatalSizeMismatch)
                {
                    RecordError("Capture failed exact-size check: " + name + " - " + lastReason);
                    yield break;
                }
                if (captured) yield break;
            }
            RecordError("Capture failed within five seconds: " + name + " - " + lastReason);
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

        IEnumerator RecordMovingFrames()
        {
            string directory = Path.Combine(output, "moving-ui-frames");
            Directory.CreateDirectory(directory);
            game.SetSpeed(1);
            for (int index = 0; index < 24; index++)
            {
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null) RecordError("Moving UI frame returned no texture at index " + index);
                else
                {
                    try { File.WriteAllBytes(Path.Combine(directory, "frame-" + index.ToString("D3") + ".png"), frame.EncodeToPNG()); }
                    catch (Exception exception) { RecordError("Could not write moving UI frame " + index + ": " + exception.Message); }
                    Destroy(frame);
                }
                yield return new WaitForSecondsRealtime(.1f);
            }
            game.SetSpeed(0);
            results.Add("CAPTURE 24 optional moving compact-HUD frames at " + Screen.width + "x" + Screen.height);
        }

        void Check(bool okay, string message)
        {
            if (okay)
            {
                results.Add("PASS " + message);
                Debug.Log("COMPACT UI PASS: " + message);
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
            game?.SetSpeed(0);
            smokeViewport?.Dispose();
            smokeViewport = null;
            Application.logMessageReceived -= OnLog;
            if (baselineCaptureOnly && baselineCaptureCount != 40)
                RecordError("Baseline capture count was " + baselineCaptureCount + ", expected 40");
            int exitCode = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILD " + Application.buildGUID +
                " BUILDGUID " + Application.buildGUID + " UNITY " + Application.unityVersion);
            results.Add("ERRORS " + errors.Count);
            results.Add("SUPPRESSED_ERRORS " + suppressedErrors);
            results.AddRange(errors);
            results.Add("EXIT " + exitCode);
            string resultFile = baselineCaptureOnly ? "baseline-capture-results.txt" : "compact-ui-results.txt";
            try { File.WriteAllLines(Path.Combine(output, resultFile), results); }
            catch (Exception exception)
            {
                Debug.LogError("Could not write " + resultFile + ": " + exception.Message);
                exitCode = 1;
            }
            if (baselineCaptureOnly)
                Debug.Log(exitCode == 0 ? "UI_BASELINE_CAPTURE_DONE" : "UI_BASELINE_CAPTURE_FAIL");
            else Debug.Log(exitCode == 0 ? "COMPACT_UI_RUNTIME_PASS" : "COMPACT_UI_RUNTIME_FAIL");
            Application.Quit(exitCode);
        }
    }
}

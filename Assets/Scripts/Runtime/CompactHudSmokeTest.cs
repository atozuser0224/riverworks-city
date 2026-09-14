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
            results.Add("CAPTURE_MODE OFFSCREEN_RENDER_TARGET");
            results.Add("DEVICE_VALIDATION NOT_RUN - virtual renders do not prove a native window or Android layout");

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

                VerifyPolishedHudContracts(size);
                if (resolution == 0) yield return VerifyEarlyStateContracts(size);
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
                VerifyInspectorPowerRows(false, size + " house");
                MeasureCoverage("selected-" + size, .30f);
                yield return Capture(size + "-01-building-selection.png");
                CloseInspectorThroughUi("building selection at " + size);

                game.InteractCell(SharedCityScenario.RockX, 9);
                yield return new WaitForEndOfFrame();
                VerifyInspectorPowerRows(true, size + " windmill");
                CloseInspectorThroughUi("powered building selection at " + size);

                Click("Button_주거");
                yield return new WaitForEndOfFrame();
                Require(IsActive("BuildChoices"), "requesting the housing category expands build choices at " + size);
                Require(IsButtonFirstHit("Button_주택"), "the housing choice is visible and raycastable at " + size);
                yield return VerifyBuildTooltip(size);
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
                VerifyFactoryConfigureLayout(size);
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
                VerifyResearchWindowBounds(size);
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
            if (label == "Trade") VerifyTradeState(size);
            else if (label == "Territory") VerifyTerritoryReasons(size);
            yield return Capture(size + "-04-utility-" + label.ToLowerInvariant() + ".png");
            Click(closeButton);
            yield return new WaitForEndOfFrame();
            Require(!game.ModalOpen && !IsActive(rootName),
                label + " closes through its rendered button and clears ModalOpen at " + size);
        }

        void VerifyPolishedHudContracts(string size)
        {
            Canvas canvas = hud.GetComponent<Canvas>();
            CanvasScaler scaler = hud.GetComponent<CanvasScaler>();
            int expectedScale = HudStyle.IntegerScale(FrameWidth, FrameHeight, Screen.dpi, Application.isMobilePlatform);
            Require(canvas != null && scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize &&
                    Mathf.Approximately(scaler.scaleFactor, expectedScale),
                "HUD uses the expected integer ConstantPixelSize scale at " + size + " (" + expectedScale + "x)");

            Text[] labels = hud.GetComponentsInChildren<Text>(true);
            Require(labels.Length > 40 && labels.All(label =>
                    (label.fontSize == HudStyle.BodySize || label.fontSize == HudStyle.TitleSize) &&
                    label.fontStyle == FontStyle.Normal),
                "all " + labels.Length + " HUD labels use 11/22 pixel normal type at " + size);

            Button[] activeButtons = hud.GetComponentsInChildren<Button>(false);
            Require(activeButtons.Length >= 15 && activeButtons.All(button =>
            {
                RectTransform rect = button.transform as RectTransform;
                return rect != null && rect.rect.width >= HudStyle.TouchSize - .5f &&
                       rect.rect.height >= HudStyle.TouchSize - .5f;
            }), "all " + activeButtons.Length + " active HUD buttons meet the 44 pixel touch target at " + size);

            ScrollRect[] scrolls = hud.GetComponentsInChildren<ScrollRect>(true);
            Require(scrolls.Length >= 8 && scrolls.All(scroll => scroll.viewport != null &&
                    scroll.viewport.GetComponent<RectMask2D>() != null),
                "all " + scrolls.Length + " HUD scroll regions have explicit masked viewports at " + size);

            Button selectedCategory = FindButton("Button_주거");
            Button territory = FindButton("Button_Territory");
            Button removal = FindButton("Button_철거");
            Require(ButtonColorIs(selectedCategory, HudStyle.Accent) && ButtonColorIs(territory, HudStyle.SurfaceRaised) &&
                    ButtonColorIs(removal, HudStyle.SurfaceRaised),
                "palette color distinguishes the selected category without persistent utility or removal emphasis at " + size);

            Button research = FindButton("Button_기술 연구");
            Text[] researchLines = research == null ? Array.Empty<Text>() : research.GetComponentsInChildren<Text>(false);
            Require(research != null && researchLines.Length == 2 && researchLines.All(text =>
                    TextRenderFits(text, research.transform as RectTransform, true, out _)),
                "both top-bar research lines have visible rendered width and height inside their button at " + size);

            Require(hud.VerifyLayout(out string reason), "HUD VerifyLayout passes at " + size + ": " + reason);
        }

        IEnumerator VerifyEarlyStateContracts(string size)
        {
            GameState early = GameState.CreateNew();
            early.Technologies.Clear();
            early.Stock[(int)Resource.Ore] = 0;
            early.Stock[(int)Resource.Steel] = 0;
            early.Stock[(int)Resource.Tools] = 0;
            Require(SaveStore.TrySave(game.SavePath, early, out string earlySaveError),
                "early-state UI fixture saves: " + earlySaveError);
            game.LoadGame();
            yield return null;
            Canvas.ForceUpdateCanvases();

            Require(!IsActive("Ore") && !IsActive("Steel") && !IsActive("Tools"),
                "advanced resource chips start hidden in an early city at " + size);

            TechId oreUnlock = TechCatalog.RequiredTechnology(BuildingKind.Mine);
            game.State.Technologies.Add(oreUnlock);
            game.NotifyWorldSelection();
            yield return null;
            Require(IsActive("Ore"), "unlocking the mine reveals the ore resource chip at " + size);
            game.State.Technologies.Remove(oreUnlock);
            game.NotifyWorldSelection();
            yield return null;
            Require(IsActive("Ore"), "an advanced resource chip remains sticky after first reveal at " + size);

            game.ToggleResearch();
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (TechId id in new[] { TechId.Stonecraft, TechId.CropRotation })
            {
                Button button = FindButton("Button_연구 시작_" + id);
                RectTransform card = Root("TechCard_" + id) as RectTransform;
                ScrollRect scroll = card == null ? null : card.GetComponentInParent<ScrollRect>();
                if (scroll != null) scroll.verticalNormalizedPosition = 1f;
                Canvas.ForceUpdateCanvases();
                Text effect = Root("TechEffect_" + id) == null ? null : Root("TechEffect_" + id).GetComponent<Text>();
                Require(button != null && button.gameObject.activeInHierarchy && button.interactable &&
                        card != null && scroll != null && ContainsRect(scroll.viewport, card, 1f) &&
                        effect != null && effect.gameObject.activeInHierarchy &&
                        card.rect.height >= 183.5f && card.rect.height + .5f >= effect.preferredHeight + 84f &&
                        TextRenderFits(effect, card, false, out _),
                    id + " is an available, initially visible research action in the early-state fixture at " + size);
            }
            game.ToggleResearch();

            Require(SaveStore.TrySave(game.SavePath, game.State, out string resetSaveError),
                "early-state resource reset fixture saves: " + resetSaveError);
            game.LoadGame();
            yield return null;
            Require(!IsActive("Ore"), "loading a new state reference resets sticky advanced-resource visibility at " + size);

            GameState rich = SharedCityScenario.Create();
            Require(SaveStore.TrySave(game.SavePath, rich, out string richSaveError),
                "shared city is restored after early-state UI verification: " + richSaveError);
            game.LoadGame();
            game.CameraRig.Home();
            game.CameraRig.SetZoom(6.3f);
            game.RefreshWorld(true);
            yield return new WaitForSecondsRealtime(.35f);
            Canvas.ForceUpdateCanvases();
        }

        IEnumerator VerifyBuildTooltip(string size)
        {
            Button house = FindButton("Button_주택");
            HudTooltipTrigger trigger = house == null ? null : house.GetComponent<HudTooltipTrigger>();
            Require(trigger != null && !string.IsNullOrEmpty(trigger.TooltipText),
                "housing choice exposes a tooltip trigger and descriptive content at " + size);

            var mouse = new PointerEventData(EventSystem.current) { pointerId = -1 };
            trigger.OnPointerEnter(mouse);
            Require(IsActive("BuildTooltip"), "mouse hover reveals the construction tooltip at " + size);
            trigger.OnPointerExit(mouse);
            Require(!IsActive("BuildTooltip"), "pointer exit hides the construction tooltip at " + size);

            game.SelectTool(BuildingKind.None);
            var touch = new PointerEventData(EventSystem.current)
                { pointerId = 0, button = PointerEventData.InputButton.Left };
            trigger.OnPointerDown(touch);
            yield return new WaitForSecondsRealtime(HudTooltipTrigger.LongPressSeconds + .08f);
            Require(trigger.ConsumeClick && IsActive("BuildTooltip"),
                "unscaled long press reveals the construction tooltip and consumes its click at " + size);
            house.onClick.Invoke();
            Require(game.SelectedTool == BuildingKind.None,
                "consumed long press does not accidentally select a construction tool at " + size);
            trigger.OnPointerUp(touch);
            trigger.OnPointerClick(touch);
        }

        void VerifyInspectorPowerRows(bool expected, string phase)
        {
            Transform rows = Root("InspectorRows");
            RectTransform rowsRect = rows as RectTransform;
            RectTransform viewport = rows == null ? null : rows.parent as RectTransform;
            bool shown = rows != null && rows.GetComponentsInChildren<Text>(false).Any(text => text.text == "전력");
            Require(shown == expected, "inspector power row is " + (expected ? "shown" : "omitted") + " for " + phase);
            Require(rowsRect != null && viewport != null && rowsRect.rect.width <= viewport.rect.width + 1f &&
                    Mathf.Abs(rowsRect.sizeDelta.x) <= .5f,
                "inspector row content uses the viewport width without horizontal mask overflow for " + phase);
            RectTransform[] activeRows = rows == null ? Array.Empty<RectTransform>() : rows.Cast<Transform>()
                .Where(row => row.gameObject.activeInHierarchy).Select(row => row as RectTransform).ToArray();
            Require(activeRows.Length > 0, "inspector exposes structured rows for " + phase);
            foreach (RectTransform row in activeRows)
            {
                Text[] cells = row.GetComponentsInChildren<Text>(false);
                string labelReason = "missing", valueReason = "missing";
                bool labelFits = cells.Length == 2 && TextRenderFits(cells[0], row, true, out labelReason);
                bool valueFits = cells.Length == 2 && TextRenderFits(cells[1], row, false, out valueReason);
                Require(labelFits && valueFits &&
                        NonOverlappingHorizontalCells(row, cells[0].rectTransform, cells[1].rectTransform) &&
                        HorizontallyContained(viewport, row, .75f) &&
                        HorizontallyContained(viewport, cells[0].rectTransform, .75f),
                    "inspector label/value render inside their row for " + phase + " " + row.name +
                    " (label " + labelReason + ", value " + valueReason + ")");
            }
        }

        void VerifyTradeState(string size)
        {
            Transform root = Root("TradeOverlay");
            Require(root != null && root.GetComponentsInChildren<Text>(false).Any(text => text.text.StartsWith("보유 코인")),
                "trade panel shows current coin holdings at " + size);
            RectTransform card = Root("TradeCard") as RectTransform;
            Text holdings = Root("TradeHoldingsHeader") == null ? null : Root("TradeHoldingsHeader").GetComponent<Text>();
            string headerReason = "missing";
            bool headerFits = holdings != null && TextRenderFits(holdings, card, true, out headerReason);
            Require(card != null && holdings != null && holdings.transform.parent == card && holdings.gameObject.activeInHierarchy &&
                    headerFits,
                "fixed trade holdings header remains visible inside its card at " + size + " (" + headerReason + ")");
            Resource[] resources = { Resource.Timber, Resource.Stone, Resource.Grain, Resource.Flour,
                Resource.Bread, Resource.Ore, Resource.Steel, Resource.Tools };
            foreach (Resource resource in resources)
            {
                Button buy = FindButton("Button_MobileBuy_" + resource);
                Button sell = FindButton("Button_MobileSell_" + resource);
                bool canBuy = game.State.Coins >= GameController.TradePrice(resource) * 10;
                bool canSell = game.Sim.Get(resource) >= 10;
                Require(buy != null && sell != null && buy.interactable == canBuy && sell.interactable == canSell,
                    "trade buy/sell availability follows coin and " + resource + " holdings at " + size);
            }
        }

        void VerifyTerritoryReasons(string size)
        {
            Button[] regions = Enumerable.Range(1, 9).Select(index => FindButton("Button_Region_" + index)).ToArray();
            Require(regions.All(button => button != null), "all nine territory actions exist at " + size);
            foreach (Button button in regions.Where(button => !button.interactable))
            {
                string label = button.GetComponentInChildren<Text>().text;
                Require(label.Contains("보유") || label.Contains("인접 필요") || label.Contains("코인 부족"),
                    button.name + " explains why it is unavailable at " + size);
            }
        }

        void VerifyFactoryConfigureLayout(string size)
        {
            RectTransform window = Root("FactoryConfigureCard") as RectTransform;
            Transform recipe = Root("FactoryRecipeSection");
            Require(window != null && recipe != null && recipe.gameObject.activeInHierarchy &&
                    recipe.GetComponent<HorizontalLayoutGroup>() != null,
                "assembler configuration uses its horizontal recipe layout at " + size);
            Canvas.ForceUpdateCanvases();
            Button[] recipes = recipe.GetComponentsInChildren<Button>(false).OrderBy(button =>
                ((RectTransform)button.transform).anchoredPosition.x).ToArray();
            Require(recipes.Length == 3 && AdjacentWithoutHole(recipe as RectTransform, recipes),
                "assembler recipe row contains three compatible choices without a blank slot at " + size);
            float recipeHeight = window.rect.height;

            Require(game.Factory.SelectAt(18, 16), "fixture inserter is selectable for configuration layout at " + size);
            Canvas.ForceUpdateCanvases();
            Transform filter = Root("FactoryFilterSection");
            float filterHeight = window.rect.height;
            Require(filter != null && filter.gameObject.activeInHierarchy && filter.GetComponent<GridLayoutGroup>() != null &&
                    filter.GetComponentsInChildren<Button>(false).Length == 9,
                "inserter configuration uses a dense nine-filter grid at " + size);

            Require(game.Factory.SelectAt(22, 24), "fixture storage is selectable for configuration layout at " + size);
            Canvas.ForceUpdateCanvases();
            Transform feed = Root("FactoryFeedSection");
            float feedHeight = window.rect.height;
            Require(feed != null && feed.gameObject.activeInHierarchy && feed.GetComponent<GridLayoutGroup>() != null &&
                    feed.GetComponentsInChildren<Button>(false).Length == 8 && filterHeight > feedHeight && feedHeight > recipeHeight,
                "factory configuration height follows filter, feed, and recipe content at " + size);

            Require(game.Factory.SelectAt(SharedCityScenario.ProcessorMicroX, SharedCityScenario.ProcessorMicroZ),
                "fixture assembler selection is restored after configuration layout checks at " + size);
            Canvas.ForceUpdateCanvases();
        }

        void VerifyResearchWindowBounds(string size)
        {
            RectTransform canvas = hud.transform as RectTransform;
            RectTransform window = Root("ResearchCard") as RectTransform;
            Require(canvas != null && window != null && ContainsRect(canvas, window, 1f),
                "research window remains inside the HUD canvas at " + size);
            Vector3[] corners = new Vector3[4];
            window.GetWorldCorners(corners);
            float minimum = float.MaxValue, maximum = float.MinValue;
            foreach (Vector3 corner in corners)
            {
                float y = canvas.InverseTransformPoint(corner).y;
                minimum = Mathf.Min(minimum, y); maximum = Mathf.Max(maximum, y);
            }
            Require(minimum >= canvas.rect.yMin + 68f - 1f && maximum <= canvas.rect.yMax - 60f + 1f,
                "research window respects the top 60 and bottom 68 HUD reservations at " + size);
        }

        static bool ButtonColorIs(Button button, Color expected) => button != null && button.targetGraphic != null &&
            ColorDistance(button.targetGraphic.color, expected) <= .01f;

        static float ColorDistance(Color first, Color second) => Mathf.Max(Mathf.Abs(first.r - second.r),
            Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b));

        static bool TextRenderFits(Text text, RectTransform container, bool requireSingleLine, out string reason)
        {
            if (text == null || container == null)
            {
                reason = "missing text or container";
                return false;
            }
            RectTransform rect = text.rectTransform;
            float renderedWidth = rect.rect.width;
            float renderedHeight = rect.rect.height;
            float preferredWidth = text.preferredWidth;
            float preferredHeight = text.preferredHeight;
            bool positiveArea = renderedWidth > .5f && renderedHeight > .5f;
            bool heightFits = preferredHeight <= renderedHeight + .75f;
            bool widthFits = !requireSingleLine || preferredWidth <= renderedWidth + .75f;
            bool oneLine = !requireSingleLine || !text.text.Contains("\n");
            bool contained = ContainsRect(container, rect, .75f);
            reason = "rect " + renderedWidth.ToString("0.#") + "x" + renderedHeight.ToString("0.#") +
                ", preferred " + preferredWidth.ToString("0.#") + "x" + preferredHeight.ToString("0.#");
            return positiveArea && heightFits && widthFits && oneLine && contained;
        }

        static bool NonOverlappingHorizontalCells(RectTransform row, RectTransform label, RectTransform value)
        {
            if (row == null || label == null || value == null) return false;
            Vector3[] labelCorners = new Vector3[4], valueCorners = new Vector3[4];
            label.GetWorldCorners(labelCorners); value.GetWorldCorners(valueCorners);
            float labelMaximum = labelCorners.Max(corner => row.InverseTransformPoint(corner).x);
            float valueMinimum = valueCorners.Min(corner => row.InverseTransformPoint(corner).x);
            return labelMaximum <= valueMinimum + .75f;
        }

        static bool HorizontallyContained(RectTransform parent, RectTransform child, float tolerance)
        {
            if (parent == null || child == null) return false;
            Vector3[] corners = new Vector3[4]; child.GetWorldCorners(corners);
            float minimum = corners.Min(corner => parent.InverseTransformPoint(corner).x);
            float maximum = corners.Max(corner => parent.InverseTransformPoint(corner).x);
            return minimum >= parent.rect.xMin - tolerance && maximum <= parent.rect.xMax + tolerance;
        }

        static bool ContainsRect(RectTransform parent, RectTransform child, float tolerance)
        {
            if (parent == null || child == null) return false;
            Rect rect = parent.rect;
            Vector3[] corners = new Vector3[4]; child.GetWorldCorners(corners);
            return corners.All(corner =>
            {
                Vector3 point = parent.InverseTransformPoint(corner);
                return point.x >= rect.xMin - tolerance && point.x <= rect.xMax + tolerance &&
                       point.y >= rect.yMin - tolerance && point.y <= rect.yMax + tolerance;
            });
        }

        static bool AdjacentWithoutHole(RectTransform parent, IList<Button> buttons)
        {
            if (parent == null || buttons == null || buttons.Count == 0) return false;
            float previousMax = float.MinValue;
            foreach (Button button in buttons)
            {
                Vector3[] corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                float minimum = corners.Min(corner => parent.InverseTransformPoint(corner).x);
                float maximum = corners.Max(corner => parent.InverseTransformPoint(corner).x);
                if (previousMax > float.MinValue && (minimum < previousMax - 1f || minimum - previousMax > 16f)) return false;
                previousMax = maximum;
            }
            return true;
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
                RectTransform card = Root("TechCard_" + spec.Id) as RectTransform;
                if (button == null || card == null)
                {
                    RecordError("Missing research card or stable button for " + spec.Id + " at " + size);
                    continue;
                }
                ScrollRect scroll = card.GetComponentInParent<ScrollRect>();
                bool reached = false;
                if (scroll != null)
                {
                    for (int step = 0; step <= 32 && !reached; step++)
                    {
                        scroll.StopMovement();
                        scroll.verticalNormalizedPosition = 1f - step / 32f;
                        Canvas.ForceUpdateCanvases();
                        reached = ContainsRect(scroll.viewport, card, 1.5f);
                    }
                }
                bool completed = TechCatalog.Has(game.State, spec.Id);
                if (completed)
                {
                    Text effect = Root("TechEffect_" + spec.Id) == null ? null : Root("TechEffect_" + spec.Id).GetComponent<Text>();
                    Check(!button.gameObject.activeSelf && card.rect.height <= 82.5f && effect != null &&
                          effect.gameObject.activeInHierarchy && TextRenderFits(effect, card, true, out _) &&
                          scroll != null && ContainsRect(scroll.viewport, effect.rectTransform, 1.5f),
                        "completed research card is compact and keeps one unclipped effect line: " + spec.Id + " at " + size);
                }
                else
                {
                    RectTransform rect = button.transform as RectTransform;
                    Check(button.gameObject.activeSelf && rect != null && rect.rect.width >= 200f &&
                          rect.rect.height >= HudStyle.TouchSize - .5f,
                        "available or locked research card retains a full-width action: " + spec.Id + " at " + size);
                }
                if (reached) accessible++;
                else RecordError("Research card cannot be reached by its internal scroll viewport: " + spec.Id + " at " + size);
            }
            Require(accessible == 18, "all 18 research cards are reachable through their internal scroll views at " + size + " (18/18)");
            Transform research = Root("ResearchOverlay");
            if (research != null)
            {
                foreach (ScrollRect scroll in research.GetComponentsInChildren<ScrollRect>(true))
                {
                    scroll.StopMovement();
                    if (scroll.vertical) scroll.verticalNormalizedPosition = 1f;
                    if (scroll.horizontal) scroll.horizontalNormalizedPosition = 0f;
                }
                Canvas.ForceUpdateCanvases();
            }
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
                    (column + .5f) * FrameWidth / CoverageColumns,
                    (row + .5f) * FrameHeight / CoverageRows);
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
            Canvas canvas = button.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector3 worldCenter = (corners[0] + corners[2]) * .5f;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCenter);
            Vector2 minimum = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
            Vector2 maximum = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[2]);
            Vector2 dimensions = canvas == null ? new Vector2(Screen.width, Screen.height) : HudStyle.PixelDimensions(canvas);
            if (maximum.x - minimum.x < 1 || maximum.y - minimum.y < 1 ||
                center.x < 0 || center.x > dimensions.x || center.y < 0 || center.y > dimensions.y)
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
            point.x < FrameWidth - 1 && point.y < FrameHeight - 1;

        int FrameWidth => smokeViewport == null ? Screen.width : smokeViewport.Width;
        int FrameHeight => smokeViewport == null ? Screen.height : smokeViewport.Height;
        Vector2 ScreenCenter => smokeViewport == null
            ? new Vector2(Screen.width * .5f, Screen.height * .5f)
            : smokeViewport.Center;

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
                    if (smokeViewport != null && (texture == null || texture.width != smokeViewport.Width ||
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
                if (smokeViewport == null) yield return new WaitForEndOfFrame();
                else yield return null;
                Texture2D frame = smokeViewport == null
                    ? ScreenCapture.CaptureScreenshotAsTexture()
                    : smokeViewport.Capture();
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
            results.Add("CAPTURE 24 optional moving compact-HUD frames at " + FrameWidth + "x" + FrameHeight);
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

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
    /// <summary>Opt-in native proof for factory floors, conditional automation, and city projects.</summary>
    public sealed class ExpansionSmokeTest : MonoBehaviour
    {
        const string Flag = "-riverworks-expansion-smoke";
        const string ResultFile = "expansion-results.txt";
        static readonly Vector2Int[] Viewports =
        {
            new Vector2Int(1600,900), new Vector2Int(1280,720),
            new Vector2Int(1024,768), new Vector2Int(2048,1536)
        };

        GameController game;
        FactoryController factory;
        Canvas canvas;
        UiSmokeViewport viewport;
        string output;
        bool finished;
        int captures, suppressedErrors;
        int oldWidth, oldHeight, oldTargetFrameRate, oldVsync;
        float oldTimeScale;
        FullScreenMode oldFullScreenMode;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();

        public void Initialize(GameController controller)
        {
            game = controller; factory = game == null ? null : game.Factory;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-riverworks-output");
            output = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "ExpansionSmoke");
            Directory.CreateDirectory(output);
            oldWidth = Screen.width; oldHeight = Screen.height; oldFullScreenMode = Screen.fullScreenMode;
            oldTargetFrameRate = Application.targetFrameRate; oldVsync = QualitySettings.vSyncCount; oldTimeScale = Time.timeScale;
            Application.logMessageReceived += OnLog;
            StartCoroutine(Guarded());
        }

        void OnDestroy() { DisposeViewport(); RestoreGlobals(); Application.logMessageReceived -= OnLog; }
        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) RecordError((message ?? "") + "\n" + (trace ?? ""));
        }
        IEnumerator Guarded()
        {
            yield return SmokeExecution.Run(Run(), exception => RecordError(exception.ToString()), 300f);
            Finish();
        }

        IEnumerator Run()
        {
            Require(Environment.GetCommandLineArgs().Contains(Flag), "expansion smoke runs only behind its explicit command flag");
            Require(game != null && game.SmokeMode && factory != null && game.CityHud != null && game.CameraRig != null,
                "controller, factory, HUD, and camera initialized in isolated smoke mode");
            Require(!string.Equals(Path.GetFileName(game.SavePath), "city-v1.json", StringComparison.OrdinalIgnoreCase),
                "expansion fixture cannot overwrite the player's city save");
            canvas = game.CityHud.GetComponent<Canvas>();
            Require(canvas != null && EventSystem.current != null, "native HUD canvas and EventSystem are available");
            Image projectHost=game.CityHud.Projects.GetComponent<Image>();
            Require(projectHost!=null&&!projectHost.raycastTarget&&projectHost.color.a==0,
                "closed project host is transparent and never blocks world input");
            game.ResidentAi?.SetEnabled(false); game.Tutorial?.SkipIntro();
            Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0;
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(.35f);

            GameState fixture = ExpansionScenario.Create();
            Require(SaveStore.TrySave(game.SavePath, fixture, out string saveError),
                "three-floor fixture saves only to the isolated smoke slot: " + saveError);
            game.LoadGame(); factory = game.Factory; factory.OpenReal(); game.ModalOpen = false;
            results.Add("FIXTURE_SCOPE raw Grain is physical food input; Water and project parts are explicit prepared fixture material");

            VerifyStructureAndConservation();
            yield return WaitForFactoryGoals();
            yield return VerifyFloorUiAndPicking();
            yield return VerifyAutomationUi();
            yield return VerifyProjectUiAndCompletion();
            Require(captures >= 11, "native run captured three floors and four viewports for both expansion panels");
        }

        void VerifyStructureAndConservation()
        {
            FactoryState state = factory.State;
            Require(state.Platforms != null && state.Platforms.Any(p => p.Floor == 1) && state.Platforms.Any(p => p.Floor == 2),
                "fixture has real supporting platforms on floors one and two");
            FactoryEntity[] links = state.Entities.Where(e => e.Kind == FactoryKind.ItemLift || e.Kind == FactoryKind.FluidRiser).ToArray();
            Require(links.Length >= 8 && links.All(e => e.LinkId > 0 && links.Any(other => other.Id == e.LinkId &&
                    other.LinkId == e.Id && other.Kind == e.Kind && other.X == e.X && other.Z == e.Z &&
                    Math.Abs(other.Floor - e.Floor) == 1 && other.IsLinkSender != e.IsLinkSender)),
                "every vertical transport endpoint has one reciprocal adjacent-floor partner");
            Require(state.Entities.All(e => e.Input.All(v => v >= 0) && e.Output.All(v => v >= 0)),
                "all initial expansion inventories are nonnegative");
            Require(state.Entities.Where(e => e.Kind == FactoryKind.ItemLift).All(e => e.Filter == Resource.Coins || ResourceCatalog.IsSolid(e.Filter)) &&
                    state.Entities.Where(e => e.Kind == FactoryKind.FluidRiser).All(e => e.Filter == Resource.Coins || ResourceCatalog.IsFluid(e.Filter)),
                "vertical item and fluid links preserve their phase filters");
        }

        IEnumerator WaitForFactoryGoals()
        {
            FactoryEntity bread = ExpansionScenario.FindBreadMachine(game.State);
            FactoryEntity topTank = ExpansionScenario.FindTopTank(game.State);
            Require(bread != null && topTank != null && bread.Floor == 2 && topTank.Floor == 2,
                "scenario exposes its top-floor bread machine and water tank");
            FactoryEntity groundTank = factory.State.Entities.FirstOrDefault(e => e.Kind == FactoryKind.FluidTank && e.Floor == 0);
            AutomationRule breadRule = factory.State.AutomationRules.FirstOrDefault(r => r.TargetEntityId == bread.Id);
            AutomationRule tankRule = groundTank == null ? null : factory.State.AutomationRules.FirstOrDefault(r => r.TargetEntityId == groundTank.Id);
            Require(breadRule != null && breadRule.SourceEntityId == 0 && breadRule.Resource == Resource.Bread &&
                    breadRule.Comparison == AutomationComparison.AtMost && breadRule.Threshold == 0 &&
                    breadRule.Action == AutomationAction.AllowWhenTrue,
                "bread controller allows production only while city bread stock is zero");
            Require(groundTank != null && tankRule != null && tankRule.SourceEntityId == topTank.Id &&
                    tankRule.Resource == Resource.Water && tankRule.Comparison == AutomationComparison.AtLeast &&
                    tankRule.Threshold == 8 && tankRule.Action == AutomationAction.StopWhenTrue,
                "ground tank controller stops when the top tank reaches eight liters of water");
            game.SetSpeed(3f); float deadline = Time.realtimeSinceStartup + 90f;
            while ((factory.State.Exported[(int)Resource.Bread] <= 0 || Amount(topTank.Input, Resource.Water) <= 0 || !bread.AutomationBlocked) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            game.SetSpeed(0f);
            Require(factory.State.Exported[(int)Resource.Bread] > 0,
                "raw grain crosses three floors and reaches the real ground export through FactoryController.Update");
            Require(Amount(topTank.Input, Resource.Water) > 0,
                "prepared water crosses two powered fluid risers and reaches the top tank through actual updates");
            Require(bread.AutomationBlocked && !bread.Paused,
                "the installed bread rule automatically blocks after export without changing manual pause");
            if (Amount(topTank.Input, Resource.Water) >= 8)
                Require(groundTank.AutomationBlocked, "top-tank threshold blocks the ground tank through its installed rule");
            Require(factory.State.Entities.All(e => e.Input.All(v => v >= 0) && e.Output.All(v => v >= 0)) &&
                    factory.State.Produced[(int)Resource.Flour] > 0 && factory.State.Produced[(int)Resource.Bread] > 0,
                "food production remains nonnegative and records both flour and bread stages");
            results.Add("PRIMARY_PROOF actual FactoryController.Update; no forced factory simulation ticks");
        }

        IEnumerator VerifyFloorUiAndPicking()
        {
            ClosePanels(); factory.OpenReal(); game.SetSpeed(0f);
            game.CameraRig.FrameRegion(5); game.CameraRig.SetZoom(6.5f);
            yield return new WaitForSecondsRealtime(.35f);
            int probeX = ExpansionScenario.BreadMachineX, probeZ = ExpansionScenario.BreadMachineZ;
            Vector3 ground = factory.View.WorldPosition(probeX, probeZ, 0), upper = factory.View.WorldPosition(probeX, probeZ, 2);
            Require(upper.y > ground.y + FactoryView.FloorHeight * 1.9f,
                "same factory coordinate resolves to distinct world heights on floors zero and two");
            for (int floor = 0; floor <= FactoryLayers.MaxFloor; floor++)
            {
                Button button = FindButton("Button_FactoryFloor_" + floor);
                Require(button != null && Height(button) == 44, "floor " + floor + " action has a 44 pixel target");
                Click(button); yield return null;
                Require(factory.ActiveFloor == floor, "floor " + floor + " button changes the actual controller floor");
                Vector2 screen = game.CameraRig.Camera.WorldToScreenPoint(factory.View.WorldPosition(probeX, probeZ, floor));
                Require(factory.View.TryGetFloorPoint(screen, floor, out Vector3 picked) &&
                        Mathf.Abs(picked.y - floor * FactoryView.FloorHeight) < .02f,
                    "floor " + floor + " uses its actual bounded pick plane");
                factory.View.Refresh(true);
                yield return Capture("expansion-floor-" + floor + ".png", 1600, 900);
            }
            Click(FindButton("Button_FactoryFloor_1")); yield return null;
            Button foundation = FindButton("Button_FactoryFoundation");
            Require(foundation != null && Height(foundation) == 44, "foundation build action has a 44 pixel target");
            Click(foundation); Require(factory.FoundationMode && factory.ActiveFloor > 0,
                "foundation button enters real upper-floor platform placement mode");
            Vector2Int foundationSite = FindFoundationSite(1);
            Require(foundationSite.x >= 0, "fixture has one valid empty floor-one foundation site");
            float coinsBefore = game.State.Coins;
            float beamsBefore = game.State.Stock[(int)Resource.SteelBeam];
            float framesBefore = game.State.Stock[(int)Resource.ModularFrame];
            int cityX = foundationSite.x / CityLogistics.Resolution, cityZ = foundationSite.y / CityLogistics.Resolution;
            game.CameraRig.FrameRegion((cityZ / 7) * 3 + cityX / 7); game.CameraRig.SetZoom(6.5f);
            yield return new WaitForSecondsRealtime(.35f);
            Vector2 foundationPixel = game.CameraRig.Camera.WorldToScreenPoint(
                factory.View.WorldPosition(foundationSite.x, foundationSite.y, 1));
            Require(factory.InteractScreenPoint(foundationPixel),
                "upper-floor pick plane routes one real UI-selected foundation placement");
            Require(FactoryLayers.HasPlatform(factory.State, foundationSite.x, foundationSite.y, 1) &&
                    Mathf.Approximately(game.State.Coins, coinsBefore - 30f) &&
                    Mathf.Approximately(game.State.Stock[(int)Resource.SteelBeam], beamsBefore - 2f) &&
                    Mathf.Approximately(game.State.Stock[(int)Resource.ModularFrame], framesBefore - 1f),
                "native foundation placement charges exactly 30G, two steel beams, and one modular frame");
            factory.SelectTool(FactoryKind.None);
        }

        IEnumerator VerifyAutomationUi()
        {
            FactoryEntity bread = ExpansionScenario.FindBreadMachine(game.State);
            Require(bread != null && factory.SelectAt(bread.X, bread.Z, bread.Floor),
                "top bread machine selects on its actual floor");
            yield return null; Click(FindButton("Button_FactoryConfigure")); yield return null;
            Click(FindButton("Button_FactoryAutomation")); yield return null;
            AutomationRulePanel panel = game.CityHud.Automation;
            Require(panel != null && panel.IsOpen, "configuration automation action opens the real rule panel");
            Require(game.CityHud.GetComponentsInChildren<RectTransform>(true).Count(r =>
                    r.name.StartsWith("AutomationRuleSlot_", StringComparison.Ordinal) && r.gameObject.activeInHierarchy) == 4,
                "automation editor renders exactly four rule slots");
            foreach (Vector2Int size in Viewports) yield return CapturePanel("expansion-rules-" + size.x + "x" + size.y + ".png", size);

            AutomationRule rule = factory.State.AutomationRules.FirstOrDefault(r => r.TargetEntityId == bread.Id);
            Require(rule != null && bread.ControllerInstalled, "fixture rule targets installed bread controller hardware");
            float controlBefore = game.State.Stock[(int)Resource.ControlUnit];
            InputField threshold = game.CityHud.GetComponentsInChildren<InputField>(true)
                .FirstOrDefault(input => input.name == "Input_AutomationRuleThreshold_0");
            Require(threshold != null, "first rule exposes its integer threshold editor");
            int changedThreshold = rule.Threshold == 0 ? 1 : rule.Threshold - 1;
            threshold.text = changedThreshold.ToString();
            InputField otherThreshold = game.CityHud.GetComponentsInChildren<InputField>(true)
                .FirstOrDefault(input => input.name == "Input_AutomationRuleThreshold_1" && input.gameObject.activeInHierarchy);
            Require(otherThreshold != null, "second slot exposes an independent unsaved threshold draft");
            otherThreshold.text = "123";
            yield return new WaitForSecondsRealtime(.75f);
            threshold = game.CityHud.GetComponentsInChildren<InputField>(true)
                .FirstOrDefault(input => input.name == "Input_AutomationRuleThreshold_0" && input.gameObject.activeInHierarchy);
            Require(threshold!=null&&threshold.text==changedThreshold.ToString(),
                "periodic HUD refresh preserves the unsaved automation threshold draft");
            Click(FindButton("Button_AutomationRuleSave_0")); yield return null;
            rule = factory.State.AutomationRules.First(r => r.Id == rule.Id);
            Require(rule.Threshold == changedThreshold && Mathf.Approximately(game.State.Stock[(int)Resource.ControlUnit], controlBefore),
                "editing an existing threshold saves through the controller without charging hardware again");
            otherThreshold = game.CityHud.GetComponentsInChildren<InputField>(true)
                .FirstOrDefault(input => input.name == "Input_AutomationRuleThreshold_1" && input.gameObject.activeInHierarchy);
            Require(otherThreshold != null && otherThreshold.text == "123",
                "saving the first rule preserves the second slot's unsaved draft");
            Click(FindButton("Button_AutomationRuleClose"));
            factory.SetPaused(true); game.SetSpeed(3f); yield return new WaitForSecondsRealtime(.25f); game.SetSpeed(0f);
            Require(bread.Paused && bread.IsStopped,
                "manual pause remains authoritative while automation rules are installed");
            factory.SetPaused(false);
        }

        IEnumerator VerifyProjectUiAndCompletion()
        {
            ClosePanels(); game.CityHud.OpenMenu(); yield return null;
            Click(FindButton("Button_CityProjects")); yield return null;
            CityProjectPanel panel = game.CityHud.Projects;
            Require(panel != null && panel.IsOpen, "menu project action opens the real city-project panel");
            Click(FindButton("Button_CityProjectKind_" + CityProjectKind.ResearchCampus)); yield return null;
            foreach (Vector2Int size in Viewports) yield return CapturePanel("expansion-projects-" + size.x + "x" + size.y + ".png", size);

            Vector2Int site = FindProjectSite(CityProjectKind.ResearchCampus);
            Require(site.x >= 0, "fixture contains a connected valid Research Campus site");
            Click(FindButton("Button_CityProjectSite"));
            Require(game.ProjectSiteTool == CityProjectKind.ResearchCampus,
                "site button enters the real GameController project selection mode");
            game.CameraRig.FrameRegion((site.y / 7) * 3 + site.x / 7); game.CameraRig.SetZoom(6.5f);
            yield return new WaitForSecondsRealtime(.4f);
            Vector2 pixel = game.CameraRig.Camera.WorldToScreenPoint(BoardView.Position(site.x, site.y));
            Require(game.InteractScreenPoint(pixel), "actual world-tile screen interaction starts the selected project");
            CityProjectState project = CityProjects.Find(game.State, CityProjectKind.ResearchCampus);
            Require(project != null && project.X == site.x && project.Z == site.y && game.ProjectView != null,
                "Research Campus occupies the selected real city footprint and has a project view");

            float researchBefore = game.Sim.ResearchPerDay;
            CityProjectSpec spec = CityProjects.Get(CityProjectKind.ResearchCampus);
            for (int expectedStage = 0; expectedStage < spec.Stages.Length; expectedStage++)
            {
                if (!panel.IsOpen) game.CityHud.OpenCityProjects(); yield return null;
                Button deliver = FindButton("Button_CityProjectDeliver");
                Require(deliver != null && deliver.gameObject.activeInHierarchy && deliver.interactable,
                    "stage " + (expectedStage + 1) + " exposes its real material-delivery action");
                Click(deliver); yield return null;
                Require(project.DaysRemaining == spec.Stages[expectedStage].Days,
                    "stage " + (expectedStage + 1) + " delivery starts its catalog construction duration");
                panel.Close(); game.SetSpeed(3f);
                float deadline = Time.realtimeSinceStartup + 40f;
                while (project.Stage == expectedStage && Time.realtimeSinceStartup < deadline) yield return null;
                game.SetSpeed(0f);
                Require(project.Stage == expectedStage + 1,
                    "actual GameController day updates advance project stage " + (expectedStage + 1));
                game.CameraRig.CommitFocus(BoardView.Position(site.x,site.y)+new Vector3(BoardView.Spacing*.5f,0,BoardView.Spacing*.5f),4.5f);
                yield return Capture("expansion-campus-stage-"+project.Stage+".png",1600,900);
            }
            Require(project.CompletedDay >= project.StartedDay && CityProjects.IsConnected(game.State, project),
                "Research Campus completes after all three real timed stages on a connected site");
            Require(Mathf.Approximately(game.Sim.ResearchPerDay, researchBefore + 10f),
                "completed connected Research Campus adds exactly 10 research per day");
            Require(game.ProjectView.GetComponentsInChildren<Renderer>(true).Length > 0,
                "completed project is represented by native world geometry");
        }

        Vector2Int FindProjectSite(CityProjectKind kind)
        {
            if (kind == CityProjectKind.ResearchCampus &&
                CityProjects.CanStart(game.State, kind, ExpansionScenario.CampusX, ExpansionScenario.CampusZ, out _) &&
                CityProjects.IsConnected(game.State, new CityProjectState
                    { Kind = kind, X = ExpansionScenario.CampusX, Z = ExpansionScenario.CampusZ }))
                return new Vector2Int(ExpansionScenario.CampusX, ExpansionScenario.CampusZ);
            CityProjectSpec spec = CityProjects.Get(kind);
            for (int z = 0; z <= game.State.Size - spec.Height; z++)
            for (int x = 0; x <= game.State.Size - spec.Width; x++)
                if (CityProjects.CanStart(game.State, kind, x, z, out _) &&
                    CityProjects.IsConnected(game.State,new CityProjectState{Kind=kind,X=x,Z=z})) return new Vector2Int(x, z);
            return new Vector2Int(-1, -1);
        }

        Vector2Int FindFoundationSite(int floor)
        {
            for (int z = 0; z <= factory.State.Height - FactoryLayers.PlatformSize; z += FactoryLayers.PlatformSize)
            for (int x = 0; x <= factory.State.Width - FactoryLayers.PlatformSize; x += FactoryLayers.PlatformSize)
                if (FactoryLayers.CanPlacePlatform(game.State, x, z, floor, out _)) return new Vector2Int(x, z);
            return new Vector2Int(-1, -1);
        }

        IEnumerator CapturePanel(string name, Vector2Int size)
        {
            yield return Capture(name, size.x, size.y);
            Component owner = game.CityHud.Automation != null && game.CityHud.Automation.IsOpen
                ? (Component)game.CityHud.Automation : game.CityHud.Projects;
            Require(owner != null && owner.GetComponentsInChildren<Button>(true).Where(b => b.gameObject.activeInHierarchy)
                    .All(b => ((RectTransform)b.transform).rect.height >= 44f - .5f),
                "active expansion panel actions remain at least 44 pixels high at " + size.x + "x" + size.y);
        }

        IEnumerator Capture(string name, int width, int height)
        {
            DisposeViewport(); viewport = new UiSmokeViewport(game.CameraRig.Camera, canvas, width, height);
            yield return new WaitForSecondsRealtime(.3f); yield return null;
            Texture2D texture = null;
            try
            {
                texture = viewport.Capture();
                Require(texture != null && texture.width == width && texture.height == height,
                    name + " renders at its exact requested viewport size");
                Require(texture.GetPixels32().Any(p => p.r > 8 || p.g > 8 || p.b > 8), name + " is not a black frame");
                File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                results.Add("CAPTURE " + name + " " + width + "x" + height); captures++;
            }
            finally { if (texture != null) Destroy(texture); DisposeViewport(); }
        }

        void ClosePanels()
        {
            game.SetSpeed(0f);
            game.CityHud.Automation?.Close(); game.CityHud.Projects?.Close(); game.CityHud.Industry?.Close();
            game.CityHud.CloseTransientPanels(); game.ModalOpen = false;
            if (game.HelpOpen) game.ToggleHelp(); if (game.ResearchOpen) game.ToggleResearch();
        }

        Button FindButton(string name) => game.CityHud.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);
        void Click(Button button)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                throw new InvalidOperationException("UI button unavailable: " + (button == null ? "null" : button.name));
            ScrollIntoView(button.transform as RectTransform); Canvas.ForceUpdateCanvases();
            PointerEventData pointer = PointerAt(button); var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && IsButtonHit(button, hits[0].gameObject), button.name + " is the first EventSystem raycast target");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        static void ScrollIntoView(RectTransform target)
        {
            ScrollRect scroll = target == null ? null : target.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            Canvas.ForceUpdateCanvases(); Vector3[] a = new Vector3[4], b = new Vector3[4]; target.GetWorldCorners(a); scroll.viewport.GetWorldCorners(b);
            float dy = a[1].y > b[1].y ? b[1].y - a[1].y : a[0].y < b[0].y ? b[0].y - a[0].y : 0;
            scroll.content.position += new Vector3(0, dy, 0); Canvas.ForceUpdateCanvases();
        }
        static PointerEventData PointerAt(Button button)
        {
            RectTransform rect = button.transform as RectTransform; Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Canvas owner = button.GetComponentInParent<Canvas>(); Camera camera = owner != null && owner.renderMode != RenderMode.ScreenSpaceOverlay ? owner.worldCamera : null;
            return new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(camera, (corners[0] + corners[2]) * .5f),
                button = PointerEventData.InputButton.Left, pointerId = -1 };
        }
        static bool IsButtonHit(Button button, GameObject hit) => hit == button.gameObject || hit.transform.IsChildOf(button.transform);
        static int Height(Button button) => Mathf.RoundToInt(((RectTransform)button.transform).rect.height);
        static int Amount(IList<int> inventory, Resource resource) => inventory != null && (int)resource < inventory.Count ? inventory[(int)resource] : 0;
        void Require(bool okay, string message)
        {
            if (!okay) throw new InvalidOperationException(message);
            results.Add("PASS " + message); Debug.Log("EXPANSION PASS: " + message);
        }
        void RecordError(string message)
        {
            if (errors.Count >= 64) { suppressedErrors++; return; }
            message = string.IsNullOrEmpty(message) ? "Unknown error" : message;
            if (message.Length > 4096) message = message.Substring(0, 4096) + "...[truncated]";
            errors.Add(message);
        }
        void DisposeViewport() { viewport?.Dispose(); viewport = null; }
        void RestoreGlobals()
        {
            Time.timeScale = oldTimeScale; QualitySettings.vSyncCount = oldVsync; Application.targetFrameRate = oldTargetFrameRate;
            if (oldWidth > 0 && oldHeight > 0) Screen.SetResolution(oldWidth, oldHeight, oldFullScreenMode);
        }
        void Finish()
        {
            if (finished) return; finished = true; game?.SetSpeed(0f); DisposeViewport(); RestoreGlobals(); Application.logMessageReceived -= OnLog;
            int exit = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILDGUID " + Application.buildGUID + " UNITY " + Application.unityVersion);
            results.Add("ERRORS " + errors.Count); results.Add("SUPPRESSED_ERRORS " + suppressedErrors); results.AddRange(errors); results.Add("EXIT " + exit);
            try { File.WriteAllLines(Path.Combine(output, ResultFile), results); }
            catch (Exception exception) { Debug.LogError("Could not write " + ResultFile + ": " + exception.Message); exit = 1; }
            Debug.Log(exit == 0 ? "RIVERWORKS_EXPANSION_SMOKE_PASS" : "RIVERWORKS_EXPANSION_SMOKE_FAIL"); Application.Quit(exit);
        }
    }
}

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
    /// <summary>
    /// Opt-in native proof for the industrial scenarios. Production proof deliberately waits for
    /// FactoryController.Update; direct simulation ticks are not used to make the target exports.
    /// </summary>
    public sealed class IndustrySmokeTest : MonoBehaviour
    {
        const string SmokeArgument = "-riverworks-industry-smoke";
        const string ResultFile = "industry-results.txt";
        const int MaximumErrors = 64;
        const int MaximumErrorCharacters = 4096;
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
        int captures;
        int suppressedErrors;
        int oldWidth, oldHeight, oldTargetFrameRate, oldVsync;
        float oldTimeScale;
        FullScreenMode oldFullScreenMode;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();

        public void Initialize(GameController controller)
        {
            game = controller;
            factory = controller == null ? null : controller.Factory;
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-riverworks-output");
            output = at >= 0 && at + 1 < args.Length ? args[at + 1]
                : Path.Combine(Application.persistentDataPath, "IndustrySmoke");
            Directory.CreateDirectory(output);
            oldWidth = Screen.width; oldHeight = Screen.height; oldFullScreenMode = Screen.fullScreenMode;
            oldTargetFrameRate = Application.targetFrameRate; oldVsync = QualitySettings.vSyncCount;
            oldTimeScale = Time.timeScale;
            Application.logMessageReceived += OnLog;
            StartCoroutine(GuardedRun());
        }

        void OnDestroy()
        {
            DisposeViewport(); RestoreGlobals(); Application.logMessageReceived -= OnLog;
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                RecordError((message ?? "") + "\n" + (trace ?? ""));
        }

        IEnumerator GuardedRun()
        {
            yield return SmokeExecution.Run(RunSteps(), exception => RecordError(exception.ToString()), 300f);
            Finish();
        }

        IEnumerator RunSteps()
        {
            Require(Environment.GetCommandLineArgs().Contains(SmokeArgument),
                "industry smoke runs only behind its explicit command flag");
            Require(game != null && game.SmokeMode && factory != null && game.CityHud != null && game.CameraRig != null,
                "controller, factory, HUD, and camera initialized in isolated smoke mode");
            Require(!string.Equals(Path.GetFileName(game.SavePath), "city-v1.json", StringComparison.OrdinalIgnoreCase),
                "industrial fixtures cannot overwrite the player's city save");
            canvas = game.CityHud.GetComponent<Canvas>();
            Require(canvas != null && EventSystem.current != null, "native HUD canvas and EventSystem are available");

            game.ResidentAi?.SetEnabled(false);
            game.Tutorial?.SkipIntro();
            Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0;
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.35f);

            VerifyCatalogContracts();
            yield return RunScenario(IndustryScenarioKind.OilChemistry, Resource.Plastic, VerifyOilChemistry);
            yield return RunScenario(IndustryScenarioKind.AluminumRecycling, Resource.Aluminum, VerifyAluminumRecycling);
            yield return RunScenario(IndustryScenarioKind.ControlAssembly, Resource.ControlUnit, VerifyControlAssembly);
            VerifyTradeGuard();
            yield return VerifyFactoryConfigurationUi();
            yield return VerifyIndustryCodex();
            Require(captures >= 11, "native run captured four configuration views, four codex views, and three industrial worlds");
        }

        IEnumerator RunScenario(IndustryScenarioKind kind, Resource target, Action verify)
        {
            game.SetSpeed(0f); CloseUi();
            GameState fixture = IndustrialScenario.Create(kind);
            if (kind == IndustryScenarioKind.ControlAssembly) VerifyPreparedControlFixture(fixture);
            Require(SaveStore.TrySave(game.SavePath, fixture, out string error),
                kind + " fixture saves to the isolated smoke slot: " + error);
            game.LoadGame(); factory = game.Factory; factory.OpenReal(); game.ModalOpen = false;
            game.SetSpeed(3f);
            float deadline = Time.realtimeSinceStartup + 90f;
            while (factory.State.Exported[(int)target] <= 0 && Time.realtimeSinceStartup < deadline)
                yield return null;
            game.SetSpeed(0f);
            Require(factory.State.Exported[(int)target] > 0,
                kind + " exports " + target + " through actual FactoryController.Update within 90 seconds");
            verify();
            factory.View.Refresh(true); FocusIndustry(kind);
            yield return Capture("industry-world-" + kind + ".png");
        }

        void VerifyCatalogContracts()
        {
            FactorySpec[] machines = FactoryCatalog.All.ToArray();
            RecipeSpec[] recipes = FactoryCatalog.Recipes.Where(r => r != null && r.Id != FactoryRecipe.None).ToArray();
            Require(machines.Length == 23 && machines.All(s => s != null && !string.IsNullOrWhiteSpace(s.Name) &&
                    !string.IsNullOrWhiteSpace(s.Description) && s.Width > 0 && s.Height > 0),
                "all 23 machinery kinds expose complete catalog metadata");
            Require(recipes.Length == 37 && recipes.Select(r => r.Id).Distinct().Count() == 37 &&
                    recipes.All(r => r.Machines.Length > 0 && r.Outputs.Length > 0 && r.Duration > 0),
                "all 37 recipes expose machines, outputs, and duration");
            Require(ResourceCatalog.Count == 38 && ResourceCatalog.All.Count == 38 &&
                    ResourceCatalog.All.All(r => r != null && !string.IsNullOrWhiteSpace(r.Name) &&
                    ColorUtility.TryParseHtmlString(r.ColorHex, out _)),
                "all 38 resources expose valid names and render colors");
            Require(ResourceCatalog.IsFluid(Resource.Water) && ResourceCatalog.IsFluid(Resource.CrudeOil) &&
                    !ResourceCatalog.IsFluid(Resource.Plastic) && !ResourceCatalog.IsTradeable(Resource.ControlUnit),
                "resource phase and advanced-product trade restrictions come from the shared catalog");
        }

        void VerifyOilChemistry()
        {
            FactoryEntity oil = IndustrialScenario.FindMachine(game.State, FactoryRecipe.OilExtraction);
            FactoryEntity water = IndustrialScenario.FindMachine(game.State, FactoryRecipe.WaterExtraction);
            FactoryEntity refinery = IndustrialScenario.FindMachine(game.State, FactoryRecipe.OilRefining);
            FactoryEntity chemical = IndustrialScenario.FindMachine(game.State, FactoryRecipe.PlasticPolymerization);
            FactoryEntity tank = factory.State.Entities.FirstOrDefault(e => e.Kind == FactoryKind.FluidTank);
            Require(oil != null && water != null && factory.State.Produced[(int)Resource.CrudeOil] > 0 &&
                    factory.State.Produced[(int)Resource.Water] > 0,
                "oil and water extraction machines actually produced fluid");
            Require(refinery != null && chemical != null &&
                    factory.State.Produced[(int)Resource.PetroleumGas] > 0 &&
                    factory.State.Produced[(int)Resource.Plastic] > 0,
                "oil refining feeds real plastic polymerization");
            Require(tank != null && Amount(tank.Input, Resource.HeavyOil) > 0 &&
                    FluidPorts.For(refinery).Any(p => !p.IsInput && p.Resource == Resource.HeavyOil),
                "heavy-oil byproduct drains from its real refinery output into the fluid tank");
            VerifyRenderedFluidAssets();
        }

        void VerifyAluminumRecycling()
        {
            FactoryEntity alumina = IndustrialScenario.FindMachine(game.State, FactoryRecipe.AluminaRefining);
            FactoryEntity scrap = IndustrialScenario.FindMachine(game.State, FactoryRecipe.ScrapRefining);
            FactoryEntity foundry = IndustrialScenario.FindMachine(game.State, FactoryRecipe.AluminumSmelting);
            Require(alumina != null && factory.State.Produced[(int)Resource.AluminaSolution] > 0 &&
                    scrap != null && factory.State.Produced[(int)Resource.AluminumScrap] > 0 &&
                    foundry != null && factory.State.Produced[(int)Resource.Aluminum] > 0,
                "alumina, scrap, and aluminum stages all produced through their physical paths");
            Require(factory.State.Produced[(int)Resource.Water] > 0 &&
                    FluidPorts.For(scrap).Any(p => !p.IsInput && p.Resource == Resource.Water),
                "scrap refining exposes water as a real byproduct port");
            FactoryEntity[] returnPath = factory.State.Entities.Where(e => FactoryCatalog.IsFluidTransport(e.Kind) &&
                Amount(e.Input, Resource.Water) > 0).ToArray();
            Require(returnPath.Length > 0 && returnPath.Any(e => FactoryFluidSimulation.Volume(e) > 0),
                "recovered water is visible at positive volume on the return-loop transport path");
        }

        void VerifyPreparedControlFixture(GameState fixture)
        {
            Resource[] prepared = { Resource.Computer, Resource.Motor, Resource.Battery,
                Resource.ModularFrame, Resource.AluminumCasing };
            FactoryEntity manufacturer = IndustrialScenario.FindMachine(fixture, FactoryRecipe.ControlUnitAssembly);
            Require(manufacturer != null && prepared.All(resource => fixture.Factory.Entities.Any(e =>
                    e.Kind == FactoryKind.Storage && Amount(e.Input, resource) > 0)),
                "control scenario explicitly starts with five prepared components for final-stage assembly");
            results.Add("FIXTURE_SCOPE ControlAssembly is prepared-components-only final-stage proof, not raw-resource E2E");
        }

        void VerifyControlAssembly()
        {
            FactoryEntity manufacturer = IndustrialScenario.FindMachine(game.State, FactoryRecipe.ControlUnitAssembly);
            Require(manufacturer != null &&
                    factory.State.Produced[(int)Resource.ControlUnit] > 0,
                "prepared components enter the manufacturer and produce a control unit");
        }

        void VerifyTradeGuard()
        {
            string before = JsonUtility.ToJson(game.State);
            game.Trade(Resource.ControlUnit, true);
            Require(JsonUtility.ToJson(game.State) == before,
                "GameController.Trade rejects ControlUnit purchase without changing any resources");
            game.Trade(Resource.ControlUnit, false);
            Require(JsonUtility.ToJson(game.State) == before,
                "GameController.Trade rejects ControlUnit sale without changing any resources");
        }

        IEnumerator VerifyFactoryConfigurationUi()
        {
            game.SetSpeed(0f); CloseUi(); factory.OpenReal();
            FactoryEntity machine = IndustrialScenario.FindMachine(game.State, FactoryRecipe.ControlUnitAssembly);
            Require(machine != null && factory.SelectAt(machine.X, machine.Z), "known scenario machine selects through FactoryController.SelectAt");
            yield return null;
            Click(FindButton("Button_FactoryConfigure")); yield return null;
            Require(game.ModalOpen, "real configure action opens the selected-machine modal");
            Button pause = FindButton("Button_FactoryPause");
            Require(pause != null && Height(pause) == 44, "pause action has the required 44 pixel target");
            float progress = machine.Progress;
            Click(pause); Require(machine.Paused && Mathf.Approximately(machine.Progress, progress),
                "pause button preserves machine progress while setting the paused flag");
            Click(pause); Require(!machine.Paused && Mathf.Approximately(machine.Progress, progress),
                "pause button resumes without resetting machine progress");

            var powers = new List<float>();
            foreach (int clock in new[] { 50, 100, 150, 200 })
            {
                Button button = FindButton("Button_FactoryClock_" + clock);
                Require(button != null && Height(button) == 44 && button.interactable,
                    clock + "% clock action is enabled and 44 pixels high");
                Click(button); powers.Add(factory.Sim.PowerUsed);
                Require(machine.ClockPercent == clock, clock + "% clock reaches the actual selected machine");
            }
            Require(powers[0] < powers[1] && powers[1] < powers[2] && powers[2] < powers[3] &&
                    powers[3] - powers[0] > FactoryCatalog.Get(machine.Kind).PowerDemand,
                "clock controls change actual power use with the catalog's quadratic scaling");

            Button[] recipeButtons = game.CityHud.GetComponentsInChildren<Button>(true)
                .Where(b => b.name.StartsWith("Button_FactoryRecipe_", StringComparison.Ordinal)).ToArray();
            Require(recipeButtons.Length == FactoryCatalog.Recipes.Count && recipeButtons.All(b =>
            {
                FactoryRecipe id; return Enum.TryParse(b.name.Substring("Button_FactoryRecipe_".Length), out id) &&
                    b.gameObject.activeSelf == FactoryCatalog.IsRecipeCompatible(machine.Kind, id);
            }), "configuration grid exposes exactly the selected machine's compatible recipes");

            Click(FindButton("Button_FactoryConfigureClose"));
            game.State.Technologies.Remove(TechId.IndustrialControl);
            game.NotifyWorldSelection();
            Click(FindButton("Button_FactoryConfigure"));
            Button lockedRecipe = FindButton("Button_FactoryRecipe_" + FactoryRecipe.ControlUnitAssembly);
            Require(lockedRecipe != null && lockedRecipe.gameObject.activeSelf && !lockedRecipe.interactable &&
                    lockedRecipe.GetComponentInChildren<Text>().text.Contains(TechCatalog.Get(TechId.IndustrialControl).Name),
                "compatible locked recipe remains visible with its catalog-backed research reason");
            Click(FindButton("Button_FactoryConfigureClose"));
            game.State.Technologies.Add(TechId.IndustrialControl);
            game.NotifyWorldSelection();
            Click(FindButton("Button_FactoryConfigure"));

            yield return CaptureConfigurationViewports();
            Click(FindButton("Button_FactoryConfigureClose"));

            GameState transportFixture = IndustrialScenario.Create(IndustryScenarioKind.OilChemistry);
            transportFixture.Stock[(int)Resource.HeavyOil] = 20;
            Require(SaveStore.TrySave(game.SavePath, transportFixture, out string transportSaveError),
                "filter UI fixture saves to the isolated smoke slot: " + transportSaveError);
            game.LoadGame(); factory = game.Factory; factory.OpenReal(); game.SetSpeed(0f);
            yield return null;
            yield return VerifyFiltersFor(FactoryKind.Inserter, false);
            yield return VerifyFiltersFor(FactoryKind.Pipe, true);
            yield return VerifyFiltersFor(FactoryKind.FluidTank, true);
            yield return VerifyFiltersFor(FactoryKind.Belt, null);
        }

        IEnumerator VerifyFiltersFor(FactoryKind kind, bool? fluid)
        {
            FactoryEntity entity = factory.State.Entities.FirstOrDefault(e => e.Kind == kind);
            Require(entity != null && factory.SelectAt(entity.X, entity.Z), kind + " fixture entity selects");
            game.NotifyWorldSelection();
            // LoadGame, selection, and the inspector's dynamic layout all settle through separate
            // Unity callbacks. Probe the real raycast only after those callbacks have rendered once.
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            Click(FindButton("Button_FactoryConfigure"));
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            Button[] filters = game.CityHud.GetComponentsInChildren<Button>(true)
                .Where(b => b.name.StartsWith("Button_FactoryFilter_", StringComparison.Ordinal)).ToArray();
            int visible = filters.Count(b => b.gameObject.activeSelf);
            if (fluid == true) Require(visible == ResourceCatalog.FluidResources.Count + 1 && filters.Where(b => b.gameObject.activeSelf)
                    .All(b => b.name.EndsWith("Coins") || ResourceCatalog.IsFluid(ParseResource(b.name))),
                kind + " offers only fluid filters plus the clear-filter action");
            else if (fluid == false) Require(filters.Where(b => b.gameObject.activeSelf)
                    .All(b => b.name.EndsWith("Coins") || ResourceCatalog.IsSolid(ParseResource(b.name))),
                kind + " offers only solid filters plus the clear-filter action");
            else Require(visible == 0, "belt exposes no fluid or item filter controls");

            if (kind == FactoryKind.FluidTank)
            {
                RectTransform filterSection = game.CityHud.GetComponentsInChildren<RectTransform>(true)
                    .FirstOrDefault(rect => rect.name == "FactoryFilterSection");
                RectTransform feedSection = game.CityHud.GetComponentsInChildren<RectTransform>(true)
                    .FirstOrDefault(rect => rect.name == "FactoryFeedSection");
                Require(filterSection != null && feedSection != null && filterSection.gameObject.activeInHierarchy &&
                        feedSection.gameObject.activeInHierarchy && !WorldRect(filterSection).Overlaps(WorldRect(feedSection)),
                    "tank filter and manual-feed sections occupy separate vertical regions");
                Button heavyFilter = FindButton("Button_FactoryFilter_" + Resource.HeavyOil);
                Require(heavyFilter != null && heavyFilter.gameObject.activeInHierarchy && heavyFilter.interactable,
                    "tank exposes an interactive heavy-oil filter action");
                Click(heavyFilter);
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return new WaitForEndOfFrame();
                Button heavyFeed = FindButton("Button_FactoryFeed_" + Resource.HeavyOil);
                Require(heavyFeed != null && heavyFeed.gameObject.activeInHierarchy && heavyFeed.interactable,
                    "tank exposes an interactive heavy-oil manual-feed action backed by city stock");
                int before = Amount(entity.Input, Resource.HeavyOil);
                Click(heavyFeed);
                Require(Amount(entity.Input, Resource.HeavyOil) >= before + 10,
                    "EventSystem click on the tank feed action transfers 10L into the selected tank");
            }
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            Click(FindButton("Button_FactoryConfigureClose"));
            yield return null;
        }

        IEnumerator CaptureConfigurationViewports()
        {
            foreach (Vector2Int size in Viewports)
            {
                yield return PrepareViewport(size.x, size.y);
                yield return Capture("industry-config-" + size.x + "x" + size.y + ".png");
                Text[] labels = game.CityHud.GetComponentsInChildren<Text>(true);
                Require(labels.Where(t => t.gameObject.activeInHierarchy && t.GetComponentInParent<Button>() != null)
                    .All(t => t.cachedTextGenerator.lineCount <= 5),
                    "configuration text remains bounded at " + size.x + "x" + size.y);
                DisposeViewport();
            }
        }

        IEnumerator VerifyIndustryCodex()
        {
            game.SetSpeed(0f); CloseUi();
            game.CityHud.OpenMenu(); yield return SettleUi();
            Click(FindButton("Button_IndustryCodex")); yield return SettleUi();
            IndustryPanel panel = game.CityHud.Industry;
            Require(panel != null && panel.IsOpen, "menu industry button opens the public IndustryPanel");
            InputField search = game.CityHud.GetComponentsInChildren<InputField>(true)
                .FirstOrDefault(i => i.name == "IndustrySearch");
            Require(search != null, "codex exposes the stable IndustrySearch input");
            Require(game.CityHud.GetComponentsInChildren<Button>(true).Count(b =>
                    b.name.StartsWith("Button_IndustryRecipe_", StringComparison.Ordinal)) == 37,
                "all 37 catalog recipes are reachable in the recipe codex");
            search.text = FactoryCatalog.GetRecipe(FactoryRecipe.PlasticPolymerization).Name;
            yield return SettleUi();
            Require(VisibleNamed("Button_IndustryRecipe_") == 1, "recipe search filters to a catalog-backed match");
            search.text = "";

            foreach (Vector2Int size in Viewports)
            {
                yield return PrepareViewport(size.x, size.y);
                yield return Capture("industry-codex-" + size.x + "x" + size.y + ".png");
                Require(game.CityHud.GetComponentsInChildren<Text>(true).Where(t => t.gameObject.activeInHierarchy)
                    .All(t => t.fontSize >= 11 && t.fontSize <= 22),
                    "codex typography stays in the 11 to 22 pixel system at " + size.x + "x" + size.y);
                DisposeViewport();
            }

            Click(FindButton("Button_IndustryResources")); yield return SettleUi();
            Require(game.CityHud.GetComponentsInChildren<Transform>(true).Count(t =>
                    t.name.StartsWith("IndustryResource_", StringComparison.Ordinal)) == 38,
                "resource page renders all 38 catalog resources");
            search = game.CityHud.GetComponentsInChildren<InputField>(true)
                .FirstOrDefault(i => i.name == "IndustrySearch");
            search.text = ResourceCatalog.Get(Resource.Water).Name; yield return SettleUi();
            Require(game.CityHud.GetComponentsInChildren<Transform>(true).Count(t => t.gameObject.activeInHierarchy &&
                    t.name.StartsWith("IndustryResource_", StringComparison.Ordinal)) >= 1,
                "resource search dynamically rebuilds its catalog-backed result count");
            Click(FindButton("Button_IndustryProduction")); yield return SettleUi();
            Require(panel.IsOpen && FindButton("Button_IndustryClose") != null,
                "production tab and close action remain reachable");
            Click(FindButton("Button_IndustryClose")); Require(!panel.IsOpen, "industry close action dismisses the codex");

            FactoryEntity codexSource = factory.State.Entities.FirstOrDefault(e => FactoryCatalog.IsProduction(e.Kind));
            Require(codexSource != null && factory.SelectAt(codexSource.X, codexSource.Z),
                "an existing fixture machine selects for the configuration codex entry point");
            yield return SettleUi();
            Click(FindButton("Button_FactoryConfigure")); yield return SettleUi();
            Click(FindButton("Button_FactoryIndustryCodex")); yield return SettleUi();
            Require(panel.IsOpen, "factory configuration's industry button opens the same codex");
            Click(FindButton("Button_IndustryClose"));
        }

        IEnumerator PrepareViewport(int width, int height)
        {
            DisposeViewport(); Canvas.ForceUpdateCanvases();
            viewport = new UiSmokeViewport(game.CameraRig.Camera, canvas, width, height);
            yield return new WaitForSecondsRealtime(.3f); yield return null;
        }

        IEnumerator SettleUi()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            Canvas.ForceUpdateCanvases();
        }

        IEnumerator Capture(string name)
        {
            if (viewport == null) viewport = new UiSmokeViewport(game.CameraRig.Camera, canvas, 1600, 900);
            yield return new WaitForSecondsRealtime(.3f); yield return null;
            Texture2D texture = null;
            try
            {
                texture = viewport.Capture();
                Require(texture != null && texture.width == viewport.Width && texture.height == viewport.Height,
                    name + " renders at the exact requested dimensions");
                Color32[] pixels = texture.GetPixels32();
                Require(pixels.Any(p => p.r > 8 || p.g > 8 || p.b > 8), name + " is not a black frame");
                File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                results.Add("CAPTURE " + name + " " + texture.width + "x" + texture.height); captures++;
            }
            finally { if (texture != null) Destroy(texture); }
            if (name.StartsWith("industry-world-", StringComparison.Ordinal)) DisposeViewport();
        }

        void FocusIndustry(IndustryScenarioKind kind)
        {
            CloseUi(); factory.OpenReal();
            int region = kind == IndustryScenarioKind.AluminumRecycling ? 8 : 2;
            game.CameraRig.FrameRegion(region); game.CameraRig.SetZoom(6.5f);
            game.RefreshWorld(true); factory.View.Refresh(true);
        }

        void VerifyRenderedFluidAssets()
        {
            foreach (FactoryKind kind in new[] { FactoryKind.Pipe, FactoryKind.FluidTank })
            {
                FactoryEntity entity = factory.State.Entities.First(e => e.Kind == kind);
                Transform model = factory.View.ModelFor(entity.Id);
                Require(model != null && model.GetComponentsInChildren<Collider>(true).All(c => !c.enabled),
                    kind + " renders a visual model whose decorative parts cannot intercept world input");
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Require(renderers.Length > 0 && renderers.Any(r => r.sharedMaterial != null &&
                        r.sharedMaterial.color != Color.white), kind + " uses authored industrial material colors");
            }
        }

        void CloseUi()
        {
            game?.CityHud?.Industry?.Close();
            game?.CityHud?.CloseTransientPanels();
            if (game != null)
            {
                game.ModalOpen = false;
                if (game.HelpOpen) game.ToggleHelp();
                if (game.ResearchOpen) game.ToggleResearch();
            }
        }

        Button FindButton(string name) => game?.CityHud == null ? null : game.CityHud.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(button => button.name == name);

        void Click(Button button)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                throw new InvalidOperationException("UI button unavailable: " + (button == null ? "null" : button.name));
            ScrollIntoView(button.transform as RectTransform);
            Canvas.ForceUpdateCanvases();
            PointerEventData pointer = PointerAtButtonCenter(button);
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            string hitDiagnostic = hits.Count == 0 ? "none" : string.Join(" > ", hits.Take(4).Select(hit =>
                hit.gameObject == null ? "null" : hit.gameObject.name));
            RectTransform targetRect = button.transform as RectTransform;
            Graphic targetGraphic = button.targetGraphic;
            Canvas ownerCanvas = button.GetComponentInParent<Canvas>();
            string targetDiagnostic = " rect=" + (targetRect == null ? "none" : targetRect.rect.ToString()) +
                " screen=" + pointer.position + " cull=" + (targetGraphic != null && targetGraphic.canvasRenderer.cull) +
                " canvas=" + (ownerCanvas == null ? "none" : ownerCanvas.renderMode.ToString());
            Require(hits.Count > 0 && IsButtonHit(button, hits[0].gameObject),
                button.name + " is the first EventSystem raycast target (top hits: " + hitDiagnostic + targetDiagnostic + ")");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        static void ScrollIntoView(RectTransform target)
        {
            ScrollRect scroll = target == null ? null : target.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            Canvas.ForceUpdateCanvases();
            Vector3[] targetCorners = new Vector3[4], viewCorners = new Vector3[4];
            target.GetWorldCorners(targetCorners); scroll.viewport.GetWorldCorners(viewCorners);
            float delta = targetCorners[1].y > viewCorners[1].y ? viewCorners[1].y - targetCorners[1].y :
                targetCorners[0].y < viewCorners[0].y ? viewCorners[0].y - targetCorners[0].y : 0f;
            if (Mathf.Abs(delta) > .1f) scroll.content.position += new Vector3(0, delta, 0);
            Canvas.ForceUpdateCanvases();
        }

        static PointerEventData PointerAtButtonCenter(Button button)
        {
            RectTransform rect = button.transform as RectTransform; Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Canvas owner = button.GetComponentInParent<Canvas>();
            Camera eventCamera = owner != null && owner.renderMode != RenderMode.ScreenSpaceOverlay ? owner.worldCamera : null;
            return new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(eventCamera,
                (corners[0] + corners[2]) * .5f), button = PointerEventData.InputButton.Left, pointerId = -1 };
        }

        static bool IsButtonHit(Button button, GameObject hit) => hit == button.gameObject || hit.transform.IsChildOf(button.transform);
        static Rect WorldRect(RectTransform transform)
        {
            Vector3[] corners = new Vector3[4]; transform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
        static int Height(Button button) => Mathf.RoundToInt(((RectTransform)button.transform).rect.height);
        static int Amount(IList<int> values, Resource resource) => values != null && (int)resource < values.Count ? values[(int)resource] : 0;
        static Resource ParseResource(string buttonName) => (Resource)Enum.Parse(typeof(Resource), buttonName.Substring("Button_FactoryFilter_".Length));
        int VisibleNamed(string prefix) => game.CityHud.GetComponentsInChildren<Transform>(true)
            .Count(t => t.gameObject.activeInHierarchy && t.name.StartsWith(prefix, StringComparison.Ordinal));

        void Require(bool okay, string message)
        {
            if (!okay) throw new InvalidOperationException(message);
            results.Add("PASS " + message); Debug.Log("INDUSTRY PASS: " + message);
        }

        void RecordError(string message)
        {
            if (errors.Count >= MaximumErrors) { suppressedErrors++; return; }
            message = string.IsNullOrEmpty(message) ? "Unknown error" : message;
            if (message.Length > MaximumErrorCharacters) message = message.Substring(0, MaximumErrorCharacters) + "...[truncated]";
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
            if (finished) return; finished = true;
            game?.SetSpeed(0f); DisposeViewport(); RestoreGlobals(); Application.logMessageReceived -= OnLog;
            int exitCode = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILDGUID " + Application.buildGUID +
                " UNITY " + Application.unityVersion);
            results.Add("PRIMARY_PROOF actual FactoryController.Update; no forced simulation ticks");
            results.Add("ERRORS " + errors.Count); results.Add("SUPPRESSED_ERRORS " + suppressedErrors);
            results.AddRange(errors); results.Add("EXIT " + exitCode);
            try { File.WriteAllLines(Path.Combine(output, ResultFile), results); }
            catch (Exception exception) { Debug.LogError("Could not write " + ResultFile + ": " + exception.Message); exitCode = 1; }
            Debug.Log(exitCode == 0 ? "RIVERWORKS_INDUSTRY_SMOKE_PASS" : "RIVERWORKS_INDUSTRY_SMOKE_FAIL");
            Application.Quit(exitCode);
        }
    }
}

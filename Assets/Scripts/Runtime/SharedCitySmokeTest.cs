using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Opt-in executable verification for the single-board city and factory runtime.</summary>
    public sealed class SharedCitySmokeTest : MonoBehaviour
    {
        const int MaximumErrors = 64;
        const int MaximumErrorCharacters = 4096;
        GameController game;
        FactoryController factory;
        string output;
        bool finished;
        int suppressedErrors;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();

        public void Initialize(GameController controller)
        {
            game = controller;
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-riverworks-output");
            output = at >= 0 && at + 1 < args.Length
                ? args[at + 1]
                : Path.Combine(Application.persistentDataPath, "SharedCitySmoke");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
            StartCoroutine(GuardedRun());
        }

        void OnDestroy() { Application.logMessageReceived -= OnLog; }

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
            string[] commandLine = Environment.GetCommandLineArgs();
            Require(Array.IndexOf(commandLine, "-riverworks-shared-city-smoke") >= 0 ||
                    Array.IndexOf(commandLine, "-riverworks-factory-smoke") >= 0,
                "shared-city smoke only runs behind its explicit flag or legacy factory-smoke alias");
            game.SetSpeed(0);
            // Hidden Windows launches need a resize before their first usable rendered frame.
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.35f);
            Screen.SetResolution(1600,900,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.45f);
            yield return new WaitForEndOfFrame();

            GameState fixture = SharedCityScenario.Create();
            Check(fixture.Version == 4 && fixture.Factory.Width == 42 && fixture.Factory.Height == 42,
                "fixture uses the v4 city save and shared 42x42 factory grid");
            Check(fixture.Factory.Produced.Sum() == 0 && fixture.Factory.Exported.Sum() == 0,
                "fixture does not seed production or export counters");
            Require(SaveStore.TrySave(game.SavePath, fixture, out string fixtureSaveError),
                "integrated fixture saves before loading: " + fixtureSaveError);
            game.LoadGame();
            yield return null;
            game.SetSpeed(0);
            factory = game.Factory;
            Require(factory != null && factory.Sim != null && ReferenceEquals(factory.State, game.State.Factory),
                "FactoryController binds the loaded city's real factory state");

            VerifySingleSceneOwnership();
            game.OpenFactory();
            yield return null;
            VerifyOpenKeepsCityAlive();
            VerifySharedCoordinates();
            VerifyPlacementRules();

            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            game.CameraRig.Overview();
            yield return new WaitForSecondsRealtime(.35f);
            yield return VerifyLayerEightPlacement();
            VerifyRoadRequirements();
            VerifyProductionAndHandoff();
            yield return VerifySaveLoad();

            game.OpenFactory();
            game.CameraRig.Home();game.CameraRig.SetZoom(6.3f);
            game.RefreshWorld(true);
            factory.View.Refresh(true);
            PrimeMovingCargo();
            game.SetSpeed(1);
            float walkingDeadline=Time.realtimeSinceStartup+8;
            while(game.PeopleOnStreet==0&&Time.realtimeSinceStartup<walkingDeadline)yield return null;
            yield return new WaitForSecondsRealtime(.8f);
            Check(game.PeopleOnStreet > 0, "people are rendered in the shared industrial city");
            VerifyCaptureComposition();
            yield return CaptureCityMap("01-shared-city-1600x900.png", true);

            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.45f);
            yield return new WaitForEndOfFrame();
            VerifyCaptureComposition();
            yield return CaptureCityMap("02-shared-city-1280x720.png", false);
            yield return RecordMovingCityFrames();

            factory.Close();
            yield return null;
            Check(!factory.IsOpen, "closing factory tools clears only the tool-palette state");
            VerifyCityRootsActive("after factory tools close");
            Check(FindMainHud() != null && FindMainHud().gameObject.activeInHierarchy,
                "main HUD remains active after factory tools close");
            Check(errors.Count == 0, "shared-city runtime emitted no error, exception, or assertion logs");
            game.SetSpeed(0);
        }

        void VerifySingleSceneOwnership()
        {
            Require(game.Board != null && game.CameraRig != null && game.Citizens != null && factory.View != null,
                "city board, camera, citizens, and factory visuals are initialized");
            Hud hud = FindMainHud();
            Require(hud != null, "one main city HUD is present");
            Check(FindObjectsByType<Hud>(FindObjectsInactive.Include).Length == 1,
                "the root owns one main HUD");
            Check(!FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
                    .Any(component => component != null && component.GetType().Name == "FactoryHud"),
                "no separate FactoryHud instance exists");
            Check(!FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
                    .Any(component => component != null && component.GetType().Name == "FactoryTileHandle"),
                "no separate factory-floor tile handles exist");
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
            Check(cameras.Length == 1 && ReferenceEquals(cameras[0], game.CameraRig.Camera),
                "the city camera is the only scene camera");
            Check(ReferenceEquals(factory.View.Camera, game.CameraRig.Camera),
                "FactoryView delegates to the city camera");
            VerifyCityRootsActive("before factory tools open");
        }

        void VerifyOpenKeepsCityAlive()
        {
            Check(factory.IsOpen && !factory.IsPractice, "OpenFactory activates real shared-city tools");
            VerifyCityRootsActive("while factory tools are open");
            Hud hud = FindMainHud();
            Button factoryTool = ButtonNamed("Button_" + FactoryCatalog.Get(FactoryKind.Drill).Name);
            Check(hud != null && factoryTool != null && factoryTool.transform.IsChildOf(hud.transform) && factoryTool.gameObject.activeInHierarchy,
                "OpenFactory exposes the factory tool palette inside the main HUD");
            Check(FindObjectsByType<Camera>().Count(camera => camera.isActiveAndEnabled) == 1,
                "opening factory tools does not activate another camera");
            Check(factory.View.gameObject.activeInHierarchy,
                "factory models remain part of the city world while tools are open");
        }

        void VerifyCityRootsActive(string phase)
        {
            Check(game.Board.gameObject.activeInHierarchy, "city Board stays active " + phase);
            Check(game.CameraRig.gameObject.activeInHierarchy && game.CameraRig.Camera.isActiveAndEnabled,
                "main city camera stays active " + phase);
            Check(game.Citizens.gameObject.activeInHierarchy, "city Citizens stay active " + phase);
            Hud hud = FindMainHud();
            Check(hud != null && hud.gameObject.activeInHierarchy, "main city HUD stays active " + phase);
        }

        void VerifySharedCoordinates()
        {
            Check(factory.State.Width == game.State.Size * CityLogistics.Resolution &&
                  factory.State.Height == game.State.Size * CityLogistics.Resolution,
                "factory dimensions are exactly two microcells per city lot");
            Vector3 actual = factory.View.WorldPosition(SharedCityScenario.ProcessorMicroX, SharedCityScenario.ProcessorMicroZ);
            Vector3 expected = BoardView.Position(0, 0) + new Vector3(
                (SharedCityScenario.ProcessorMicroX * .5f - .25f) * BoardView.Spacing,
                0,
                (SharedCityScenario.ProcessorMicroZ * .5f - .25f) * BoardView.Spacing);
            Check(Vector3.Distance(actual, expected) < .001f,
                "FactoryView microcoordinates resolve on the city Board coordinate system");
            Check(factory.Sim.GetAt(SharedCityScenario.ProcessorMicroX, SharedCityScenario.ProcessorMicroZ)?.Kind == FactoryKind.Assembler,
                "the integrated processor occupies its shared-grid city lot");
            Check(game.Sim.GetCell(SharedCityScenario.RockX, SharedCityScenario.RockZ).Terrain == TerrainKind.Rock &&
                  factory.Sim.GetAt(SharedCityScenario.DrillMicroX, SharedCityScenario.DrillMicroZ)?.Kind == FactoryKind.Drill,
                "the drill is attached to actual visible city rock");
        }

        void VerifyPlacementRules()
        {
            var environment = new CityLogistics(game.State);
            Check(!factory.Sim.CanPlace(FactoryKind.Belt, 0, 0, 0, out _),
                "factory placement rejects unowned city land");
            Check(!factory.Sim.CanPlace(FactoryKind.Belt, 14, 14, 0, out _),
                "light transport rejects owned water without a city road bridge");
            Check(!factory.Sim.CanPlace(FactoryKind.Furnace, 18, 14, 0, out _),
                "factory machines reject an existing city house footprint");
            Check(!game.Sim.CanBuild(BuildingKind.House, 13, 13, out _),
                "city buildings reject an existing factory storage footprint");
            Check(environment.HasOre(SharedCityScenario.DrillMicroX, SharedCityScenario.DrillMicroZ),
                "CityLogistics reads ore from the city terrain");

            Require(game.Sim.Build(BuildingKind.Road, 7, 7, out string bridgeReason),
                "city road bridge fixture builds on water: " + bridgeReason);
            factory.Sim.InvalidateEnvironment();
            Check(factory.Sim.CanPlace(FactoryKind.Belt, 14, 14, 0, out _),
                "light transport may coexist with a city road bridge");
        }

        IEnumerator VerifyLayerEightPlacement()
        {
            int x = SharedCityScenario.RoadBeltMicroX;
            int z = SharedCityScenario.RoadBeltMicroZ;
            Check(game.Sim.GetCell(x / 2, z / 2).Building == BuildingKind.Road,
                "layer-8 placement target is an actual city road");
            Require(factory.Sim.GetAt(x, z) == null, "layer-8 placement target starts empty");
            factory.SelectTool(FactoryKind.Belt);
            yield return new WaitForEndOfFrame();
            Vector3 pixel = game.CameraRig.Camera.WorldToScreenPoint(factory.View.WorldPosition(x, z));
            Require(pixel.z > 0 && pixel.x >= 0 && pixel.x < Screen.width && pixel.y >= 0 && pixel.y < Screen.height,
                "shared-grid target projects inside the city camera");
            Require(Physics.Raycast(game.CameraRig.Camera.ScreenPointToRay(pixel), out RaycastHit hit, 200f, 1 << 8),
                "shared-grid pointer reaches layer-8 city terrain");
            TileHandle tile = hit.collider.GetComponent<TileHandle>();
            Check(tile != null && tile.X == x / 2 && tile.Z == z / 2,
                "the layer-8 raycast resolves the expected city lot");
            Check(game.InteractScreenPoint(pixel), "GameController routes a city-terrain click to factory placement");
            Check(factory.Sim.GetAt(x, z)?.Kind == FactoryKind.Belt,
                "FactoryController.PlaceAt retains the exact clicked microcoordinates");
            Check(game.FactoryToolActive, "root reports an active factory placement tool");
            factory.SelectTool(FactoryKind.None);
            Check(game.Sim.GetCell(x / 2, z / 2).Building == BuildingKind.Road,
                "placing a belt preserves the co-located city road");
        }

        void VerifyRoadRequirements()
        {
            var environment = new CityLogistics(game.State);
            FactoryEntity connected = factory.Sim.GetAt(SharedCityScenario.ConnectedInletMicroX, SharedCityScenario.ConnectedInletMicroZ);
            FactoryEntity dock = factory.Sim.GetAt(SharedCityScenario.DisconnectedDockMicroX, SharedCityScenario.DisconnectedDockMicroZ);
            FactoryEntity inlet = factory.Sim.GetAt(SharedCityScenario.DisconnectedInletMicroX, SharedCityScenario.DisconnectedInletMicroZ);
            Require(connected != null && dock != null && inlet != null,
                "power and dock road fixtures exist");
            Check(environment.CanSupplyPower(connected),
                "power inlet beside the connected city road can supply factory power");
            Check(!environment.CanExport(dock), "export dock without an adjacent connected city road cannot export");
            Check(!environment.CanSupplyPower(inlet), "power inlet without an adjacent connected city road cannot supply power");

            Require(game.Sim.Build(BuildingKind.Road, 13, 11, out string dockRoadReason),
                "dock access road builds through the city API: " + dockRoadReason);
            Require(game.Sim.Build(BuildingKind.Road, 13, 12, out string inletRoadReason),
                "inlet access road builds through the city API: " + inletRoadReason);
            game.RefreshWorld(true);
            factory.Sim.InvalidateEnvironment();
            environment = new CityLogistics(game.State);
            Check(environment.CanExport(dock), "connected city access road enables export dock handoff");
            Check(environment.CanSupplyPower(inlet), "connected city access road enables power inlet supply");

            game.State.Factory.PowerBudget = Math.Max(0, game.Sim.PowerCapacity - game.Sim.PowerUsed);
            factory.Sim.InvalidateEnvironment();
            factory.Sim.Recalculate();
            FactoryEntity processor = factory.Sim.GetAt(SharedCityScenario.ProcessorMicroX, SharedCityScenario.ProcessorMicroZ);
            Check(processor != null && processor.Powered,
                "city road power inlet and real city power budget energize the factory processor");
        }

        void VerifyProductionAndHandoff()
        {
            Cell farm = game.Sim.GetCell(SharedCityScenario.FarmX, SharedCityScenario.FarmZ);
            Cell lumber = game.Sim.GetCell(SharedCityScenario.LumberyardX, SharedCityScenario.LumberyardZ);
            Cell warehouse = game.Sim.GetCell(SharedCityScenario.WarehouseX, SharedCityScenario.WarehouseZ);
            Cell market = game.Sim.GetCell(SharedCityScenario.MarketX, SharedCityScenario.MarketZ);
            Require(farm.Building == BuildingKind.Farm && lumber.Building == BuildingKind.Lumberyard &&
                    warehouse.Building == BuildingKind.Warehouse && market.Building == BuildingKind.Market,
                "city farm, lumberyard, warehouse, and market are physical fixture buildings");
            Check(farm.Connected && lumber.Connected && warehouse.Connected && market.Connected,
                "all city producer and port buildings use the connected town road network");
            Check(CityLogistics.HasAutomatedOutput(game.State, farm) && CityLogistics.HasAutomatedOutput(game.State, lumber),
                "city farm and lumberyard detect their physical inserter attachments");

            float grainStockBefore = game.State.Stock[(int)Resource.Grain];
            float timberStockBefore = game.State.Stock[(int)Resource.Timber];
            float flourStockBefore = game.State.Stock[(int)Resource.Flour];
            int flourExportsBefore = game.State.Factory.Exported[(int)Resource.Flour];
            game.Sim.Tick();
            float farmBuffered = farm.LogisticsOutput[(int)Resource.Grain];
            float lumberBuffered = lumber.LogisticsOutput[(int)Resource.Timber];
            Check(farmBuffered > 0 && lumberBuffered > 0,
                "real city tick writes farm and lumber production into city logistics buffers");
            Check(Approximately(game.State.Stock[(int)Resource.Grain], grainStockBefore) &&
                  Approximately(game.State.Stock[(int)Resource.Timber], timberStockBefore),
                "automated city producers do not also credit their output to global stock");

            bool sawMoving = false;
            bool sawFarmHandoff = false;
            FactoryEntity lumberStorage = factory.Sim.GetAt(22, 24);
            Require(lumberStorage != null && lumberStorage.Kind == FactoryKind.Storage,
                "lumber attachment terminates at physical factory storage");
            for (int i = 0; i < 12000; i++)
            {
                factory.Sim.Tick(.1f);
                sawMoving |= factory.Sim.MovingItems > 0;
                sawFarmHandoff |= farm.LogisticsOutput[(int)Resource.Grain] + .001f < farmBuffered;
                if (game.State.Stock[(int)Resource.Flour] > flourStockBefore &&
                    lumberStorage.Input[(int)Resource.Timber] > 0 &&
                    game.State.Factory.Produced[(int)Resource.Ore] > 0) break;
            }

            int flourExportDelta = game.State.Factory.Exported[(int)Resource.Flour] - flourExportsBefore;
            float flourStockDelta = game.State.Stock[(int)Resource.Flour] - flourStockBefore;
            Check(sawMoving, "actual factory cargo entered an in-flight belt or inserter state");
            Check(sawFarmHandoff, "an inserter took actual grain from the city farm output buffer");
            Check(game.State.Factory.Produced[(int)Resource.Flour] > 0,
                "the factory processor produced flour from actual city grain");
            Check(lumberStorage.Input[(int)Resource.Timber] > 0,
                "actual city lumber travelled through inserter and belts into factory storage");
            Check(game.State.Factory.Produced[(int)Resource.Ore] > 0,
                "the powered drill produced ore from natural city rock");
            Check(flourExportDelta > 0 && Approximately(flourStockDelta, flourExportDelta),
                "warehouse city port credits each physically delivered flour item exactly once");
            Check(Approximately(game.State.Stock[(int)Resource.Grain], grainStockBefore) &&
                  Approximately(game.State.Stock[(int)Resource.Timber], timberStockBefore),
                "buffer handoff creates no duplicate global grain or timber credit");
        }

        IEnumerator VerifySaveLoad()
        {
            float flour = game.State.Stock[(int)Resource.Flour];
            int exports = game.State.Factory.Exported[(int)Resource.Flour];
            int entities = game.State.Factory.Entities.Count;
            float farmGrain = game.Sim.GetCell(SharedCityScenario.FarmX, SharedCityScenario.FarmZ)
                .LogisticsOutput[(int)Resource.Grain];
            Require(game.TrySaveGame(out string saveError), "shared-city state saves: " + saveError);
            game.State.Stock[(int)Resource.Flour] = 0;
            game.State.Factory.Entities.Clear();
            game.LoadGame();
            yield return null;
            game.SetSpeed(0);
            factory = game.Factory;
            Check(game.State.Version == 4 && game.State.Factory.Width == 42 && game.State.Factory.Height == 42,
                "load preserves the shared-city save version and geometry");
            Check(Approximately(game.State.Stock[(int)Resource.Flour], flour) &&
                  game.State.Factory.Exported[(int)Resource.Flour] == exports,
                "load preserves real city-port stock and factory export accounting");
            Check(game.State.Factory.Entities.Count == entities &&
                  factory.Sim.GetAt(SharedCityScenario.ProcessorMicroX, SharedCityScenario.ProcessorMicroZ)?.Recipe == FactoryRecipe.Flour,
                "load preserves the integrated factory layout and processor recipe");
            Check(Approximately(game.Sim.GetCell(SharedCityScenario.FarmX, SharedCityScenario.FarmZ)
                    .LogisticsOutput[(int)Resource.Grain], farmGrain),
                "load preserves city producer buffers");
            VerifyCityRootsActive("after shared-city load");
        }

        void PrimeMovingCargo()
        {
            game.Sim.Tick();
            for (int i = 0; i < 500 && factory.Sim.MovingItems == 0; i++) factory.Sim.Tick(.1f);
            Check(factory.Sim.MovingItems > 0,
                "moving-item preview starts with actual simulated cargo");
            factory.View.Refresh(false);
        }

        void VerifyCaptureComposition()
        {
            VerifyCityRootsActive("in the capture frame");
            Check(factory.View.gameObject.activeInHierarchy && ReferenceEquals(factory.View.Camera, game.CameraRig.Camera),
                "factory machines render through the active city camera in the capture frame");
            Check(game.State.Cells.Count(cell => cell.Building == BuildingKind.House) >= 4 &&
                  game.State.Cells.Any(cell => cell.Building == BuildingKind.Farm) &&
                  game.State.Cells.Any(cell => cell.Building == BuildingKind.Lumberyard) &&
                  game.State.Cells.Any(cell => cell.Building == BuildingKind.Warehouse) &&
                  game.State.Cells.Any(cell => cell.Building == BuildingKind.Market),
                "capture state contains houses, city producers, warehouse, and market together");
            Check(game.State.Factory.Entities.Any(entity => entity.Kind == FactoryKind.Drill) &&
                  game.State.Factory.Entities.Any(entity => entity.Kind == FactoryKind.Assembler) &&
                  game.State.Factory.Entities.Any(entity => entity.Kind == FactoryKind.Belt) &&
                  game.State.Factory.Entities.Any(entity => entity.Kind == FactoryKind.Inserter),
                "capture state contains machines and logistics together");
            Check(game.PeopleOnStreet > 0, "capture frame contains rendered people");
        }

        IEnumerator CaptureCityMap(string name, bool writePreview)
        {
            yield return new WaitForEndOfFrame();
            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null)
            {
                RecordError("City map capture returned no texture: " + name);
                yield break;
            }
            byte[] png = texture.EncodeToPNG();
            Check(CameraRegionVisible(texture), "cityMap sample is visible and non-black in " + name);
            File.WriteAllBytes(Path.Combine(output, name), png);
            if (writePreview)
            {
                File.WriteAllBytes(Path.Combine(output, "preview.png"), png);
                results.Add("SAMPLE cityMap preview.png");
            }
            Destroy(texture);
            results.Add("CAPTURE " + name);
        }

        IEnumerator RecordMovingCityFrames()
        {
            string directory = Path.Combine(output, "moving-city-frames");
            Directory.CreateDirectory(directory);
            ulong firstHash = 0;
            bool differentFrame = false;
            bool sawMoving = false;
            bool visible = true;
            for (int i = 0; i < 24; i++)
            {
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null)
                {
                    RecordError("Moving city frame returned no texture at index " + i);
                    continue;
                }
                visible &= CameraRegionVisible(frame);
                sawMoving |= factory.Sim.MovingItems > 0;
                ulong hash = FrameHash(frame);
                if (i == 0) firstHash = hash;
                else differentFrame |= hash != firstHash;
                File.WriteAllBytes(Path.Combine(directory, "frame-" + i.ToString("D3") + ".png"), frame.EncodeToPNG());
                Destroy(frame);
                yield return new WaitForSecondsRealtime(.1f);
            }
            Check(visible, "all moving-item city preview frames contain visible city-map pixels");
            Check(sawMoving, "moving-item city preview records actual in-flight cargo");
            Check(differentFrame, "shared city preview changes while cargo and people move");
            results.Add("CAPTURE 24 shared city moving-item frames at 1280x720");
        }

        bool CameraRegionVisible(Texture2D texture)
        {
            if (texture == null || game?.CameraRig?.Camera == null) return false;
            Rect rect = game.CameraRig.Camera.pixelRect;
            int left = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, texture.width - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), left + 1, texture.width);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, texture.height - 1);
            int top = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), bottom + 1, texture.height);
            int stepX = Mathf.Max(1, (right - left) / 32);
            int stepY = Mathf.Max(1, (top - bottom) / 20);
            int minimum = 255, maximum = 0, lit = 0, samples = 0;
            for (int y = bottom; y < top; y += stepY)
            for (int x = left; x < right; x += stepX)
            {
                Color32 pixel = texture.GetPixel(x, y);
                int light = (pixel.r + pixel.g + pixel.b) / 3;
                minimum = Math.Min(minimum, light);
                maximum = Math.Max(maximum, light);
                if (light > 10) lit++;
                samples++;
            }
            return samples > 0 && lit * 20 >= samples && maximum - minimum >= 6;
        }

        static ulong FrameHash(Texture2D texture)
        {
            ulong hash = 1469598103934665603UL;
            int stepX = Mathf.Max(1, texture.width / 40);
            int stepY = Mathf.Max(1, texture.height / 24);
            for (int y = 0; y < texture.height; y += stepY)
            for (int x = 0; x < texture.width; x += stepX)
            {
                Color32 pixel = texture.GetPixel(x, y);
                hash = (hash ^ pixel.r) * 1099511628211UL;
                hash = (hash ^ pixel.g) * 1099511628211UL;
                hash = (hash ^ pixel.b) * 1099511628211UL;
            }
            return hash;
        }

        Hud FindMainHud()
        {
            Hud[] all = FindObjectsByType<Hud>(FindObjectsInactive.Include);
            return all.FirstOrDefault(candidate => candidate.gameObject.name == "City interface") ?? all.FirstOrDefault();
        }

        static Button ButtonNamed(string name) => FindObjectsByType<Button>(FindObjectsInactive.Include)
            .FirstOrDefault(button => button.name == name);

        void Check(bool okay, string message)
        {
            if (okay)
            {
                results.Add("PASS " + message);
                Debug.Log("SHARED CITY PASS: " + message);
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
            if (message.Length > MaximumErrorCharacters) message = message.Substring(0, MaximumErrorCharacters) + "...[truncated]";
            errors.Add(message);
        }

        static bool Approximately(float a, float b) => Math.Abs(a - b) <= .001f;

        void Finish()
        {
            if (finished) return;
            finished = true;
            game?.SetSpeed(0);
            Application.logMessageReceived -= OnLog;
            int exitCode = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILD " + Application.buildGUID);
            var lines = results.Concat(new[]
            {
                "ERRORS " + errors.Count,
                "SUPPRESSED_ERRORS " + suppressedErrors
            }).Concat(errors).Concat(new[] { "EXIT " + exitCode });
            try { File.WriteAllLines(Path.Combine(output, "shared-city-results.txt"), lines); }
            catch (Exception exception) { Debug.LogError("Could not write shared-city-results.txt: " + exception.Message); exitCode = 1; }
            Debug.Log(exitCode == 0 ? "SHARED_CITY_RUNTIME_PASS" : "SHARED_CITY_RUNTIME_FAIL");
            Application.Quit(exitCode);
        }
    }
}

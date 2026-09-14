using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Riverworks
{
    /// <summary>Opt-in native verification for imported residents, worksite presentation and animation.</summary>
    public sealed class ResidentActivitySmokeTest : MonoBehaviour
    {
        const string SmokeArgument = "-riverworks-resident-activity-smoke";
        const int MaximumErrors = 64;
        const int MaximumErrorCharacters = 4096;
        const int FixturePopulation = 200;
        const int MovingFrameCount = 24;

        static readonly BuildingKind[] WorkingKinds =
        {
            BuildingKind.Lumberyard, BuildingKind.Quarry, BuildingKind.Farm, BuildingKind.Mill,
            BuildingKind.Bakery, BuildingKind.Mine, BuildingKind.Smelter, BuildingKind.Workshop,
            BuildingKind.Windmill, BuildingKind.Warehouse, BuildingKind.StudyHouse,
            BuildingKind.Academy, BuildingKind.SteamPlant
        };

        static readonly BuildingKind[] DwellKinds =
        {
            BuildingKind.Lumberyard, BuildingKind.Quarry, BuildingKind.Farm, BuildingKind.Mill,
            BuildingKind.Bakery, BuildingKind.Mine, BuildingKind.Smelter, BuildingKind.Workshop,
            BuildingKind.Windmill, BuildingKind.Warehouse, BuildingKind.StudyHouse,
            BuildingKind.Academy, BuildingKind.SteamPlant, BuildingKind.Market,
            BuildingKind.Park, BuildingKind.TownHall
        };

        static readonly ResidentAction[] NativeActions =
        {
            ResidentAction.Idle, ResidentAction.Walk, ResidentAction.Carry, ResidentAction.Chop,
            ResidentAction.Dig, ResidentAction.Farm, ResidentAction.Talk, ResidentAction.Sit
        };

        GameController game;
        ResidentVisualLibrary library;
        string output;
        bool finished;
        int suppressedErrors;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();
        readonly Dictionary<BuildingKind, int> buildingIndices = new Dictionary<BuildingKind, int>();

        public void Initialize(GameController controller)
        {
            game = controller;
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-riverworks-output");
            output = at >= 0 && at + 1 < args.Length
                ? args[at + 1]
                : Path.Combine(Application.persistentDataPath, "ResidentActivitySmoke");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += OnLog;
            StartCoroutine(GuardedRun());
        }

        void OnDestroy() { Application.logMessageReceived -= OnLog; }

        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                RecordError((message ?? "") + "\n" + (trace ?? ""));
        }

        IEnumerator GuardedRun()
        {
            yield return SmokeExecution.Run(RunSteps(), exception => RecordError(exception.ToString()), 180f);
            Finish();
        }

        IEnumerator RunSteps()
        {
            string[] args = Environment.GetCommandLineArgs();
            Require(Array.IndexOf(args, SmokeArgument) >= 0,
                "resident activity smoke only runs behind its explicit command flag");
            Require(game != null && game.SmokeMode && game.Citizens != null && game.Board != null && game.CameraRig != null,
                "controller uses the isolated smoke slot with resident, board and camera presentation initialized");
            Check(string.Equals(Path.GetFileName(game.SavePath), "smoke-test.json", StringComparison.Ordinal),
                "resident activity fixture cannot overwrite the ordinary city save");
            game.SetSpeed(0f);
            game.ResidentAi?.SetEnabled(false);
            game.Tutorial?.SkipIntro();

            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.30f);
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.45f);
            yield return new WaitForEndOfFrame();

            library = Resources.Load<ResidentVisualLibrary>("Residents/ResidentVisualLibrary");
            VerifyLibrary();
            VerifyWorksiteCatalog();
            VerifyControlledSimulation();

            GameState fixture = CreateFixture();
            Require(SaveStore.TrySave(game.SavePath, fixture, out string saveError),
                "controlled resident fixture saves only to the isolated smoke slot: " + saveError);
            game.LoadGame();
            game.SetSpeed(0f);
            yield return null;
            Require(game.State.Population == FixturePopulation && game.Citizens.ResidentCount == FixturePopulation,
                "real controller and CitizenSimulation retain all controlled fixture residents");

            float coins = game.State.Coins;
            int population = game.State.Population;
            int day = game.State.Day;
            ConfigurePresentationStates();
            game.Citizens.Refresh();
            yield return null;

            VerifyPresentationBudgets();
            VerifyResidentAssignments();
            yield return VerifyAnimationRuntime();
            yield return VerifySelectionAndIdentity();
            yield return VerifyAnchorInvalidation();
            Check(game.State.Population == population && Mathf.Approximately(game.State.Coins, coins) && game.State.Day == day,
                "presentation checks change neither population, economy nor day state");

            yield return CaptureOverview();
            yield return CaptureCloseup(BuildingKind.Farm, "02-farm-closeup.png");
            yield return CaptureCloseup(BuildingKind.Lumberyard, "03-lumberyard-closeup.png");
            yield return CaptureCloseup(BuildingKind.Bakery, "04-bakery-closeup.png");
            yield return CaptureCloseup(BuildingKind.Smelter, "05-smelter-closeup.png");
            yield return CaptureCloseup(BuildingKind.StudyHouse, "06-study-closeup.png");
            yield return CaptureCloseup(BuildingKind.Warehouse, "07-warehouse-closeup.png");
            yield return CaptureMovingFrames();

            Check(errors.Count == 0, "resident activity runtime emitted no error, exception or assertion logs");
            game.SetSpeed(0f);
        }

        void VerifyLibrary()
        {
            Require(library != null, "Residents/ResidentVisualLibrary loads from Resources");
            Require(library.Models != null && library.Models.Length >= 4 && library.Models.Take(4).All(model => model != null),
                "resident library contains at least four imported low-poly models");
            Require(library.GuideModel != null, "resident library contains the separate guide model");
            Check(library.ModelHeights != null && library.ModelHeights.Length >= 4 &&
                  library.ModelHeights.Take(4).All(height => height > .01f),
                "resident source heights are recorded for normalization to 0.36 world units");
            Check(library.GuideHeight > .01f, "guide source height is recorded for normalization");

            var prefabs = library.Models.Take(4).Concat(new[] { library.GuideModel }).ToArray();
            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject prefab = prefabs[i];
                Animator animator = prefab.GetComponentInChildren<Animator>(true);
                SkinnedMeshRenderer[] skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Check(animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman,
                    prefab.name + " has a valid humanoid avatar");
                Check(skins.Length == 1 && skins[0].sharedMesh != null && skins[0].sharedMesh.subMeshCount == 1,
                    prefab.name + " has one skinned renderer and one submesh");
                if (skins.Length == 1 && skins[0].sharedMesh != null)
                {
                    Mesh mesh = skins[0].sharedMesh;
                    Check(mesh.vertexCount > 0 && mesh.bounds.size.y > .01f,
                        prefab.name + " has a non-empty bounded skinned mesh");
                    Check(mesh.colors32 != null && mesh.colors32.Length == mesh.vertexCount,
                        prefab.name + " preserves per-vertex color data");
                }
            }

            foreach (ResidentAction action in NativeActions)
            {
                AnimationClip clip = ClipFor(action);
                Check(clip != null && clip.isHumanMotion && clip.length > .01f,
                    action + " is backed by a native humanoid clip");
            }
            Check(library.Farm != null && library.Chop != null && library.Walk != null,
                "Farm, Chop and Walk resolve to explicit native clips");
            Check(library.Knead == null && library.Hammer == null && library.Read == null && library.Operate == null,
                "Knead, Hammer, Read and Operate intentionally use procedural poses over Idle");
        }

        void VerifyWorksiteCatalog()
        {
            var actions = new HashSet<ResidentAction>();
            foreach (BuildingKind kind in DwellKinds)
            {
                Require(CitizenWorksites.TryGetPose(kind, 0, out WorksitePose first),
                    kind + " exposes its first readable worksite pose");
                Require(CitizenWorksites.TryGetPose(kind, 1, out WorksitePose second),
                    kind + " exposes its second readable worksite pose");
                Check(!string.IsNullOrWhiteSpace(first.Label) && !string.IsNullOrWhiteSpace(second.Label),
                    kind + " worksite poses expose an immediate action label");
                Check(!CitizenWorksites.TryGetPose(kind, 2, out _), kind + " rejects a third worksite slot");
                actions.Add(first.Action);
            }
            Check(DwellKinds.Length == 16 && CitizenWorksites.MaximumSlotsPerBuilding == 2,
                "all 16 work/leisure building kinds map to exactly two presentation slots");
            Check(actions.Contains(ResidentAction.Farm) && actions.Contains(ResidentAction.Chop) &&
                  actions.Contains(ResidentAction.Knead) && actions.Contains(ResidentAction.Hammer) &&
                  actions.Contains(ResidentAction.Read) && actions.Contains(ResidentAction.Carry),
                "worksite map differentiates the requested farm, logging, bakery, forge, study and warehouse actions");
        }

        void VerifyControlledSimulation()
        {
            GameState controlled = CreateFixture(48);
            var simulation = new CitizenSimulation(controlled);
            int[] ids = simulation.Residents.Select(resident => resident.Id).ToArray();
            float coins = controlled.Coins;
            int population = controlled.Population;
            bool sawCommute = false, sawWorking = false;
            for (int step = 0; step < 900 && !sawWorking; step++)
            {
                simulation.Advance(.1f);
                sawCommute |= simulation.Residents.Any(resident => resident.Activity == CitizenActivity.GoingToWork && !resident.Indoors);
                sawWorking |= simulation.Residents.Any(resident => resident.Activity == CitizenActivity.Working && resident.Indoors &&
                    resident.CurrentIndex == resident.JobIndex && CitizenWorksites.IsWorksiteDwell(resident, controlled));
            }
            Check(sawCommute && sawWorking,
                "CONTROLLED real CitizenSimulation.Advance observes a commute and arrival at an assigned worksite");
            Check(ids.SequenceEqual(simulation.Residents.Select(resident => resident.Id)) &&
                  controlled.Population == population && Mathf.Approximately(controlled.Coins, coins),
                "CONTROLLED resident advance preserves IDs, population and economy");
        }

        GameState CreateFixture(int requestedPopulation = FixturePopulation)
        {
            GameState state = GameState.CreateNew();
            state.Era = Era.Industrial;
            state.Day = 18;
            state.DayProgressSeconds = 0f;
            state.Coins = 54321f;
            state.OwnedRegions = Enumerable.Range(0, 9).ToList();
            state.Technologies = TechCatalog.All.Select(spec => spec.Id).Distinct().ToList();
            foreach (Cell cell in state.Cells)
            {
                cell.Terrain = TerrainKind.Grass;
                cell.Building = BuildingKind.Road;
                cell.Level = 1;
                cell.Progress = 0f;
                cell.Status = "";
                cell.LogisticsInput = Cell.NewLogisticsBuffer();
                cell.LogisticsOutput = Cell.NewLogisticsBuffer();
            }

            Set(state, 10, 10, BuildingKind.TownHall);
            var sites = new[]
            {
                new Site(BuildingKind.Lumberyard,3,3), new Site(BuildingKind.Quarry,6,3),
                new Site(BuildingKind.Farm,9,3), new Site(BuildingKind.Mill,12,3),
                new Site(BuildingKind.Bakery,15,3), new Site(BuildingKind.Mine,18,3),
                new Site(BuildingKind.Smelter,3,7), new Site(BuildingKind.Workshop,6,7),
                new Site(BuildingKind.Windmill,9,7), new Site(BuildingKind.Warehouse,12,7),
                new Site(BuildingKind.StudyHouse,15,7), new Site(BuildingKind.Academy,18,7),
                new Site(BuildingKind.SteamPlant,3,12), new Site(BuildingKind.Market,6,12),
                new Site(BuildingKind.Park,9,12)
            };
            buildingIndices.Clear();
            foreach (Site site in sites)
            {
                Set(state, site.X, site.Z, site.Kind);
                buildingIndices[site.Kind] = site.Z * state.Size + site.X;
            }
            buildingIndices[BuildingKind.TownHall] = 10 * state.Size + 10;

            int houses = Mathf.CeilToInt(requestedPopulation / 6f);
            int placed = 0;
            for (int z = 15; z <= 19 && placed < houses; z++)
            for (int x = 1; x <= 19 && placed < houses; x += 2)
            {
                Set(state, x, z, BuildingKind.House);
                placed++;
            }
            for (int z = 1; z <= 13 && placed < houses; z += 2)
            for (int x = 1; x <= 19 && placed < houses; x += 2)
            {
                int index = z * state.Size + x;
                if (state.Cells[index].Building != BuildingKind.Road) continue;
                Set(state, x, z, BuildingKind.House);
                placed++;
            }
            state.Population = requestedPopulation;
            state.Stock[(int)Resource.Coins] = state.Coins;
            new Simulation(state).Recalculate();
            state.Population = Math.Min(requestedPopulation,
                state.Cells.Where(cell => cell.Building == BuildingKind.House).Sum(cell => cell.Level * 6));
            return state;
        }

        static void Set(GameState state, int x, int z, BuildingKind kind)
        {
            Cell cell = state.Cells[z * state.Size + x];
            cell.Building = kind;
            cell.Level = 1;
        }

        void ConfigurePresentationStates()
        {
            IReadOnlyList<Citizen> residents = game.Citizens.Simulation.Residents;
            for (int i = 0; i < residents.Count; i++)
            {
                Citizen resident = residents[i];
                resident.LastDecisionSource = "routine";
                resident.LastThought = "";
                resident.LastSourceModel = "";
                resident.path.Clear();
                resident.pathStep = 0;
                resident.segment = 0f;
                if (i < 40)
                {
                    BuildingKind kind = i < 32 ? DwellKinds[i / 2] : BuildingKind.Farm;
                    int index = buildingIndices[kind];
                    resident.CurrentIndex = index;
                    resident.DestinationIndex = index;
                    if (kind == BuildingKind.Market || kind == BuildingKind.Park || kind == BuildingKind.TownHall)
                    {
                        resident.LeisureIndex = index;
                        resident.Activity = CitizenActivity.Leisure;
                    }
                    else
                    {
                        resident.JobIndex = index;
                        resident.Activity = CitizenActivity.Working;
                    }
                    resident.Indoors = true;
                }
                else
                {
                    int road = (1 + (i - 40) % 19) + ((1 + ((i - 40) / 19) % 13) * game.State.Size);
                    resident.CurrentIndex = road;
                    resident.DestinationIndex = road;
                    resident.Activity = CitizenActivity.GoingHome;
                    resident.Indoors = false;
                    resident.X = road % game.State.Size;
                    resident.Z = road / game.State.Size;
                }
            }
        }

        void VerifyPresentationBudgets()
        {
            Check(CitizenView.MaximumWorksiteResidents == 32 && CitizenSimulation.StreetCapacity == 128,
                "desktop presentation declares 32 worksite and 128 street resident limits");
            Check(game.Citizens.ActiveWorksiteCount == 32 && game.Citizens.VisibleCount == 128 &&
                  game.Citizens.PresentedCount == 160,
                "over-budget fixture renders exactly 32 workers plus 128 street residents");
            Check(game.Citizens.ImportedModelCount == game.Citizens.PresentedCount,
                "every presented resident uses an imported humanoid model");
            Check(game.Citizens.CachedFigureCount <= CitizenSimulation.StreetCapacity +
                  CitizenView.MaximumWorksiteResidents + 1,
                "inactive figure cache stays within the 160 presented residents plus guide allowance");
        }

        void VerifyResidentAssignments()
        {
            IReadOnlyList<Citizen> residents = game.Citizens.Simulation.Residents;
            for (int i = 0; i < 32; i++)
            {
                Citizen resident = residents[i];
                Require(game.Citizens.TryGetResidentPresentation(resident.Id, out ResidentActor actor,
                    out bool atWorksite, out int buildingIndex), "working resident " + resident.Id + " has a presentation");
                bool leisure = resident.Activity == CitizenActivity.Leisure;
                int assigned = leisure ? resident.LeisureIndex : resident.JobIndex;
                Check(atWorksite && buildingIndex == assigned && resident.CurrentIndex == assigned && resident.Indoors,
                    "resident " + resident.Id + " stays at its real assigned dwell index without changing simulation semantics");
                Require(CitizenWorksites.TryGetPose(resident, game.State, i % 2, out WorksitePose pose),
                    "working resident " + resident.Id + " resolves its actual building worksite pose");
                Check(actor.CurrentAction == pose.Action && actor.VisibleToolCount == ExpectedToolCount(pose.Action),
                    "working resident " + resident.Id + " visually communicates " + pose.Action);
            }
            Citizen hiddenAtHome = residents[0];
            hiddenAtHome.Activity = CitizenActivity.Home;
            hiddenAtHome.CurrentIndex = hiddenAtHome.HomeIndex;
            hiddenAtHome.DestinationIndex = hiddenAtHome.HomeIndex;
            hiddenAtHome.Indoors = true;
            game.Citizens.Refresh();
            Check(!game.Citizens.TryGetResidentPresentation(hiddenAtHome.Id, out _, out _, out _),
                "resident dwelling at home remains hidden");
            hiddenAtHome.JobIndex = buildingIndices[BuildingKind.Lumberyard];
            hiddenAtHome.CurrentIndex = hiddenAtHome.JobIndex;
            hiddenAtHome.DestinationIndex = hiddenAtHome.JobIndex;
            hiddenAtHome.Activity = CitizenActivity.Working;
            game.Citizens.Refresh();
        }

        IEnumerator VerifyAnimationRuntime()
        {
            Citizen resident = game.Citizens.Simulation.Residents[4];
            Require(game.Citizens.TryGetResidentPresentation(resident.Id, out ResidentActor actor, out _, out _),
                "farm animation test resident is visible");
            Check(actor.HasImportedModel && actor.Animator != null && actor.Model != null,
                "resident actor exposes imported model and humanoid animator diagnostics");
            GameObject actorRoot = actor.gameObject;
            GameObject originalModel = actor.Model;
            actor.SetAppearance(false, 3);
            Check(ReferenceEquals(actorRoot, actor.gameObject) && !ReferenceEquals(originalModel, actor.Model),
                "appearance replacement changes the model while preserving the citizen root identity");
            Check(RenderedHeight(actor.Model) > .25f && RenderedHeight(actor.Model) < .48f,
                "replacement low-poly model is normalized near the 0.36 world-unit target");
            actor.SetAppearance(false, Math.Abs(resident.Id) % 3);
            actor.Tick(ResidentAction.Farm, 0f, false, false, "", game.CameraRig.Camera);
            Check(actor.CurrentAction == ResidentAction.Farm && actor.ActiveClipName == library.Farm.name,
                "farm work selects the native Farm clip");

            Transform bone = actor.Animator.GetBoneTransform(HumanBodyBones.RightHand) ??
                             actor.Animator.GetBoneTransform(HumanBodyBones.Hips);
            Require(bone != null, "animation graph exposes a measured humanoid bone");
            Quaternion before = bone.localRotation;
            Vector3 rootBefore = actor.transform.position;
            Vector3 modelBefore = actor.Model.transform.localPosition;
            float timeBefore = actor.AnimationTime;
            actor.Tick(ResidentAction.Farm, .18f, false, false, "", game.CameraRig.Camera);
            Quaternion moving = bone.localRotation;
            Check(actor.AnimationTime > timeBefore && Quaternion.Angle(before, moving) > .01f,
                "actual playable graph advances time and changes a humanoid bone away from T-pose");
            Check(Vector3.Distance(actor.transform.position, rootBefore) < .00001f &&
                  Vector3.Distance(actor.Model.transform.localPosition, modelBefore) < .00001f &&
                  !actor.Animator.applyRootMotion,
                "native animation produces no resident or model-root drift");
            float frozenTime = actor.AnimationTime;
            Quaternion frozenBone = bone.localRotation;
            actor.Tick(ResidentAction.Farm, 0f, false, false, "", game.CameraRig.Camera);
            Check(Mathf.Approximately(actor.AnimationTime, frozenTime) && Quaternion.Angle(frozenBone, bone.localRotation) < .001f,
                "dt zero freezes clip time and measured bone pose");

            actor.Tick(ResidentAction.Knead, .16f, false, false, "", game.CameraRig.Camera);
            Check(actor.CurrentAction == ResidentAction.Knead && actor.ActiveClipName.StartsWith(library.Idle.name, StringComparison.Ordinal) && actor.VisibleToolCount == 1,
                "missing Knead clip falls back to Idle while its procedural pose and tool remain active");
            yield return null;
        }

        IEnumerator VerifySelectionAndIdentity()
        {
            Citizen target = game.Citizens.Simulation.Residents[45];
            target.LastDecisionSource = "model";
            target.LastSourceModel = "fixture-model";
            target.LastThought = "<script>alert(1)</script> useful thought";
            game.Citizens.Refresh();
            Require(game.Citizens.TryGetVisibleResident(target.Id, out Vector3 world),
                "street resident exposes a visible world position for selection");
            game.CameraRig.CommitFocus(world, 3.4f);
            yield return new WaitForSecondsRealtime(.45f);
            Vector3 pixel = game.CameraRig.Camera.WorldToScreenPoint(world + Vector3.up * .18f);
            bool selected = pixel.z > 0f && game.Citizens.TrySelect(pixel);
            Check(selected, "camera raycast selects a real imported resident collider");
            Check(selected && game.ResidentDetails.Contains("fixture-model") &&
                  !game.ResidentDetails.Contains("<") && !game.ResidentDetails.Contains(">"),
                "selection details retain model source and thought while stripping markup characters");
            Check(target.LastDecisionSource == "model" && target.LastThought.Contains("<script>"),
                "presentation sanitization does not mutate resident AI source or thought state");

            ResidentActor citizenActor;
            Require(game.Citizens.TryGetResidentPresentation(target.Id, out citizenActor, out _, out _),
                "selected resident actor remains available");
            Require(game.Citizens.EnsureGuide(), "existing resident 1 becomes the tutorial guide without adding population");
            game.Citizens.Refresh();
            Transform guideRoot = game.Citizens.GuideTransform;
            Require(guideRoot != null, "guide uses a visible imported guide root");
            ResidentActor guideActor = guideRoot.GetComponent<ResidentActor>();
            Check(guideActor != null && guideActor.HasImportedModel && guideActor.Animator != null,
                "guide root owns the imported humanoid guide model");

            GameObject citizenRoot = citizenActor.gameObject;
            GameObject guideIdentity = guideRoot.gameObject;
            game.State.Era = Era.Renaissance;
            game.RefreshWorld(true);
            yield return null;
            Require(game.Citizens.TryGetResidentPresentation(target.Id, out ResidentActor afterEra, out _, out _),
                "resident presentation survives an era refresh");
            Check(ReferenceEquals(citizenRoot, afterEra.gameObject) && ReferenceEquals(guideIdentity, game.Citizens.GuideTransform.gameObject),
                "citizen and guide root identities remain stable across visual-era changes");
            game.State.Era = Era.Industrial;
            game.RefreshWorld(true);
            yield return null;
        }

        IEnumerator VerifyAnchorInvalidation()
        {
            Citizen resident = game.Citizens.Simulation.Residents[0];
            int index = buildingIndices[BuildingKind.Lumberyard];
            resident.JobIndex = index;
            resident.CurrentIndex = index;
            resident.DestinationIndex = index;
            resident.Activity = CitizenActivity.Working;
            resident.Indoors = true;
            game.Citizens.Refresh();
            Require(game.Board.TryGetBuildingTransform(index, out Transform oldBuilding),
                "lumberyard has a current rendered building anchor");
            Require(game.Citizens.TryGetResidentPresentation(resident.Id, out ResidentActor oldActor,
                    out bool oldAtWorksite, out int oldIndex) && oldAtWorksite && oldIndex == index,
                "worker begins on the current lumberyard anchor");
            Transform oldStation = oldBuilding.Find("Resident Worksite Station 0");

            Cell changed = game.State.Cells[index];
            changed.Building = BuildingKind.Farm;
            resident.JobIndex = index;
            Cell south = game.State.Cells[(changed.Z + 1) * game.State.Size + changed.X];
            south.Building = BuildingKind.None;
            game.Sim.Recalculate();
            game.RefreshWorld(true);
            yield return null;
            // Topology synchronization correctly reassigns routines. Re-pin this one
            // controlled presentation subject before checking the replacement anchor.
            resident.JobIndex = index;
            resident.CurrentIndex = index;
            resident.DestinationIndex = index;
            resident.Activity = CitizenActivity.Working;
            resident.Indoors = true;
            resident.path.Clear();
            game.Citizens.Refresh();
            Require(game.Board.TryGetBuildingTransform(index, out Transform newBuilding),
                "replacement building has a current rendered anchor");
            Require(game.Citizens.TryGetResidentPresentation(resident.Id, out ResidentActor newActor,
                    out bool newAtWorksite, out int newIndex) && newAtWorksite,
                "worker rebinds after building kind and facing change");
            Check(!ReferenceEquals(oldBuilding, newBuilding) && ReferenceEquals(oldActor, newActor) && newIndex == index &&
                  newActor.CurrentAction == ResidentAction.Farm,
                "building replacement discards the stale anchor while preserving resident identity and selecting Farm");
            yield return null;
            Check(oldBuilding == null && oldStation == null,
                "destroyed building and worksite station leave no stale anchor reference");

            GameObject oldResidentRoot = newActor.gameObject;
            GameState replacement = CreateFixture();
            Require(SaveStore.TrySave(game.SavePath, replacement, out string saveError),
                "replacement fixture saves to isolated slot: " + saveError);
            game.LoadGame();
            game.SetSpeed(0f);
            yield return null;
            ConfigurePresentationStates();
            game.Citizens.Refresh();
            yield return null;
            Require(game.Citizens.TryGetResidentPresentation(game.Citizens.Simulation.Residents[0].Id,
                out ResidentActor replacementActor, out _, out _), "replacement state creates resident presentation");
            Check(!ReferenceEquals(oldResidentRoot, replacementActor.gameObject),
                "whole-state replacement discards stale resident roots and creates identities bound to the new simulation");
        }

        IEnumerator CaptureOverview()
        {
            game.CameraRig.Overview();
            yield return new WaitForSecondsRealtime(.45f);
            yield return Capture("01-resident-activity-overview-1600x900.png");
        }

        IEnumerator CaptureCloseup(BuildingKind kind, string name)
        {
            Require(buildingIndices.TryGetValue(kind, out int index), kind + " has a fixture index");
            Require(game.Board.TryGetBuildingTransform(index, out Transform building),
                kind + " close-up target exists in the rendered board");
            game.CameraRig.CommitFocus(building.position + new Vector3(0f, .12f, -.05f), 1.9f);
            Citizen resident = game.Citizens.Simulation.Residents.FirstOrDefault(value =>
                value.CurrentIndex == index && value.Indoors);
            Require(resident != null, kind + " close-up has a resident assigned to the worksite");
            Require(game.Citizens.TryGetResidentPresentation(resident.Id,
                out ResidentActor actor, out bool atWorksite, out _) && atWorksite,
                kind + " close-up uses an actual resident actor at that worksite");
            Require(CitizenWorksites.TryGetPose(kind, 0, out WorksitePose pose),
                kind + " close-up resolves its authored action pose");
            for (int sample = 0; sample < 8; sample++)
            {
                actor.Tick(pose.Action, .08f, false, true, pose.Label, game.CameraRig.Camera);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.18f);
            yield return Capture(name);
        }

        IEnumerator CaptureMovingFrames()
        {
            int index = buildingIndices[BuildingKind.Farm];
            Require(game.Board.TryGetBuildingTransform(index, out Transform farm), "farm exists for moving-frame capture");
            Citizen resident = game.Citizens.Simulation.Residents.First(value => value.JobIndex == index && value.Indoors);
            Require(game.Citizens.TryGetResidentPresentation(resident.Id, out ResidentActor actor, out bool atWorksite, out _) && atWorksite,
                "moving-frame capture uses an actual active farm resident");
            Transform bone = actor.Animator.GetBoneTransform(HumanBodyBones.RightHand) ?? actor.Animator.GetBoneTransform(HumanBodyBones.Hips);
            Require(bone != null, "moving-frame capture has a measurable animated bone");
            game.CameraRig.CommitFocus(farm.position + new Vector3(0f,.12f,-.05f), 1.9f);
            yield return new WaitForSecondsRealtime(.4f);
            string directory = Path.Combine(output, "resident-animation-frames");
            Directory.CreateDirectory(directory);
            Quaternion first = bone.localRotation;
            float maximumBoneChange = 0f;
            Vector3 root = actor.transform.position;
            for (int frameIndex = 0; frameIndex < MovingFrameCount; frameIndex++)
            {
                actor.Tick(ResidentAction.Farm, .08f, false, true, "밭 경작하기", game.CameraRig.Camera);
                maximumBoneChange = Mathf.Max(maximumBoneChange, Quaternion.Angle(first, bone.localRotation));
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame == null) RecordError("Moving resident frame returned no texture at index " + frameIndex);
                else
                {
                    try { File.WriteAllBytes(Path.Combine(directory, "frame-" + frameIndex.ToString("D3") + ".png"), frame.EncodeToPNG()); }
                    catch (Exception exception) { RecordError("Could not write moving resident frame " + frameIndex + ": " + exception.Message); }
                    Destroy(frame);
                }
                yield return new WaitForSecondsRealtime(.04f);
            }
            Check(maximumBoneChange > .05f && Vector3.Distance(root, actor.transform.position) < .00001f,
                "24 captured frames measure active bone motion without root-motion drift");
            results.Add("CAPTURE " + MovingFrameCount + " resident animation frames at " + Screen.width + "x" + Screen.height);
        }

        IEnumerator Capture(string name)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            string reason = "no frame captured";
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                Texture2D texture = null;
                bool captured = false;
                try
                {
                    texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (!VisibleFrame(texture, out reason)) continue;
                    File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                    results.Add("CAPTURE " + name + " " + texture.width + "x" + texture.height);
                    captured = true;
                }
                catch (Exception exception) { reason = exception.Message; }
                finally { if (texture != null) Destroy(texture); }
                if (captured) yield break;
            }
            RecordError("Capture failed within five seconds: " + name + " - " + reason);
        }

        static bool VisibleFrame(Texture2D texture, out string reason)
        {
            if (texture == null || texture.width != 1600 || texture.height != 900)
            {
                reason = "missing or not 1600x900";
                return false;
            }
            Color32[] pixels = texture.GetPixels32();
            int stride = Mathf.Max(1, pixels.Length / 4096);
            int lit = 0, samples = 0;
            var colors = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i += stride)
            {
                Color32 pixel = pixels[i];
                samples++;
                if (pixel.r + pixel.g + pixel.b >= 24) lit++;
                colors.Add((pixel.r >> 4) << 8 | (pixel.g >> 4) << 4 | (pixel.b >> 4));
            }
            if (lit < samples / 20) { reason = "fewer than five percent of sampled pixels are visible"; return false; }
            if (colors.Count < 16) { reason = "fewer than 16 quantized colors"; return false; }
            reason = "";
            return true;
        }

        AnimationClip ClipFor(ResidentAction action)
        {
            switch (action)
            {
                case ResidentAction.Walk: return library.Walk;
                case ResidentAction.Carry: return library.Carry;
                case ResidentAction.Chop: return library.Chop;
                case ResidentAction.Dig: return library.Dig;
                case ResidentAction.Farm: return library.Farm;
                case ResidentAction.Talk: return library.Talk;
                case ResidentAction.Sit: return library.Sit;
                default: return library.Idle;
            }
        }

        static int ExpectedToolCount(ResidentAction action)
        {
            switch (action)
            {
                case ResidentAction.Talk: case ResidentAction.Idle: case ResidentAction.Walk: case ResidentAction.Sit: return 0;
                default: return 1;
            }
        }

        static float RenderedHeight(GameObject root)
        {
            if (root == null) return 0f;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 0f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds.size.y;
        }

        void Check(bool okay, string message)
        {
            if (okay)
            {
                results.Add("PASS " + message);
                Debug.Log("RESIDENT ACTIVITY PASS: " + message);
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
            if (errors.Count >= MaximumErrors) { suppressedErrors++; return; }
            message = string.IsNullOrEmpty(message) ? "Unknown error" : message;
            if (message.Length > MaximumErrorCharacters)
                message = message.Substring(0, MaximumErrorCharacters) + "...[truncated]";
            errors.Add(message);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            game?.SetSpeed(0f);
            game?.ResidentAi?.SetEnabled(false);
            Application.logMessageReceived -= OnLog;
            int exitCode = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILDGUID " + Application.buildGUID +
                " UNITY " + Application.unityVersion);
            results.Add("ERRORS " + errors.Count);
            results.Add("SUPPRESSED_ERRORS " + suppressedErrors);
            results.AddRange(errors);
            results.Add("EXIT " + exitCode);
            try { File.WriteAllLines(Path.Combine(output, "resident-activity-results.txt"), results); }
            catch (Exception exception)
            {
                Debug.LogError("Could not write resident-activity-results.txt: " + exception.Message);
                exitCode = 1;
            }
            Debug.Log(exitCode == 0 ? "RIVERWORKS_RESIDENT_ACTIVITY_SMOKE_PASS" : "RIVERWORKS_RESIDENT_ACTIVITY_SMOKE_FAIL");
            Application.Quit(exitCode);
        }

        readonly struct Site
        {
            public readonly BuildingKind Kind;
            public readonly int X, Z;
            public Site(BuildingKind kind, int x, int z) { Kind = kind; X = x; Z = z; }
        }
    }
}

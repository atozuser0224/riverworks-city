using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riverworks
{
    public sealed class CitizenHandle : MonoBehaviour { public int ResidentId; }

    /// <summary>Shows real residents walking and working without changing their simulation state.</summary>
    public sealed class CitizenView : MonoBehaviour
    {
        public const int MaximumWorksiteResidents = 32;
        sealed class Figure
        {
            public Citizen citizen;
            public GameObject root;
            public ResidentActor actor;
            public bool guideStyle, worksite;
            public int worksiteIndex = -1, worksiteSlot = -1;
            public int modelIndex;
            public Transform stationBuilding;
            public ResidentAction action;
            public string workLabel = "";
        }
        readonly Dictionary<int, Figure> figures = new Dictionary<int, Figure>();
        readonly HashSet<int> activeIds = new HashSet<int>();
        readonly List<int> retiredIds = new List<int>();
        readonly int[] worksiteSlots = new int[441];
        GameController controller;
        CitizenSimulation simulation;
        GameState boundState;
        ResidentVisualLibrary visualLibrary;
        Material selected;
        Citizen selectedCitizen;
        GameObject selectionRing;
        Era drawnEra;

        // Street count retains its original meaning; working residents have a separate budget.
        public int VisibleCount { get; private set; }
        public int ActiveWorksiteCount { get; private set; }
        public int PresentedCount => VisibleCount + ActiveWorksiteCount;
        public int ImportedModelCount { get; private set; }
        public int CachedFigureCount => figures.Count;
        public int ResidentCount => simulation == null ? 0 : simulation.ResidentCount;
        public string SelectedDetails => selectedCitizen == null ? "" : Details(selectedCitizen);
        public bool IsGuideSelected => selectedCitizen != null && selectedCitizen.IsGuide;
        public CitizenSimulation Simulation => simulation;
        public bool GuideAvailable => simulation != null && simulation.GuideCitizen != null;
        public Transform GuideTransform
        {
            get
            {
                if (!GuideAvailable || !figures.TryGetValue(simulation.GuideCitizen.Id, out var figure) || !figure.root.activeSelf) return null;
                return figure.root.transform;
            }
        }
        public Vector3 GuideWorldPosition => GuideTransform != null ? GuideTransform.position : default;

        public void Initialize(GameController game)
        {
            controller = game;
            boundState = game.State;
            simulation = new CitizenSimulation(boundState);
            drawnEra = boundState.Era;
            visualLibrary = Resources.Load<ResidentVisualLibrary>("Residents/ResidentVisualLibrary");
            if (visualLibrary == null) Debug.LogError("Resident visual library is missing. Run ResidentAssetBuild.Prepare before building.");
            selected = new Material(Shader.Find("Standard")) { name = "Selected resident", color = new Color(1f, .78f, .18f) };
            selected.SetFloat("_Glossiness", .1f);
            selectionRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            selectionRing.name = "Selected resident marker";
            selectionRing.transform.SetParent(transform, false);
            selectionRing.transform.localScale = new Vector3(.16f, .004f, .16f);
            selectionRing.GetComponent<Renderer>().sharedMaterial = selected;
            selectionRing.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Destroy(selectionRing.GetComponent<Collider>());
            selectionRing.SetActive(false);
            Refresh();
        }

        public void Refresh()
        {
            if (controller == null) return;
            if (!ReferenceEquals(boundState, controller.State))
            {
                boundState = controller.State; simulation.Reset(boundState); ClearSelection(); ClearFigures();
            }
            else simulation.Synchronize();
            if (drawnEra != boundState.Era)
            {
                drawnEra = boundState.Era;
                // Keep the root followed by the tutorial camera stable across visual changes.
                foreach (var pair in figures) pair.Value.stationBuilding = null;
            }
            retiredIds.Clear();
            foreach (var pair in figures) if (!ContainsResident(pair.Key)) retiredIds.Add(pair.Key);
            foreach (int id in retiredIds)
            {
                figures[id].root.SetActive(false); Destroy(figures[id].root); figures.Remove(id);
            }
            if (selectedCitizen != null && !ContainsResident(selectedCitizen.Id)) ClearSelection();
            RenderFigures(0f, 0f);
        }

        void Update()
        {
            if (controller == null || simulation == null) return;
            float dt = controller.HelpOpen || controller.ModalOpen || controller.ResearchOpen
                ? 0f : Time.unscaledDeltaTime * controller.GameSpeed;
            if (dt > 0f) simulation.Advance(dt);
            float guideDt = simulation.GuideControlled ? Time.unscaledDeltaTime : dt;
            if (simulation.GuideControlled) simulation.AdvanceGuide(guideDt);
            RenderFigures(dt, guideDt);
        }

        public bool EnsureGuide()
        {
            if (simulation == null || !simulation.EnsureGuide()) return false;
            RenderFigures(0f, 0f); return true;
        }
        public bool SetGuideControl(bool enabled)
        {
            if (simulation == null || !simulation.SetGuideControl(enabled)) return false;
            RenderFigures(0f, 0f); return true;
        }
        public bool GuideToNear(int x, int z)
        {
            if (simulation == null || !simulation.GuideToNear(x, z)) return false;
            RenderFigures(0f, 0f); return true;
        }
        public bool TrySelect(Vector2 screenPosition)
        {
            if (controller == null || controller.CameraRig == null) return false;
            if (!Physics.Raycast(controller.CameraRig.Camera.ScreenPointToRay(screenPosition), out var hit, 200f, 1 << 9)) return false;
            var handle = hit.collider.GetComponent<CitizenHandle>();
            if (handle == null || !TryResident(handle.ResidentId, out selectedCitizen)) return false;
            controller.SetNotice(SelectedDetails); RenderSelection(); return true;
        }
        public void ClearSelection() { selectedCitizen = null; if (selectionRing != null) selectionRing.SetActive(false); }
        public bool TryGetVisibleResident(int id, out Vector3 worldPosition)
        {
            if (figures.TryGetValue(id, out var figure) && figure.root.activeSelf)
            {
                worldPosition = figure.root.transform.position; return true;
            }
            worldPosition = default; return false;
        }
        public bool TryGetResidentPresentation(int id, out ResidentActor actor, out bool atWorksite, out int buildingIndex)
        {
            if (figures.TryGetValue(id, out var figure) && figure.root.activeSelf)
            {
                actor = figure.actor; atWorksite = figure.worksite; buildingIndex = figure.worksiteIndex; return true;
            }
            actor = null; atWorksite = false; buildingIndex = -1; return false;
        }

        void RenderFigures(float dt, float guideDt)
        {
            activeIds.Clear(); Array.Clear(worksiteSlots, 0, worksiteSlots.Length);
            VisibleCount = 0; ActiveWorksiteCount = 0; ImportedModelCount = 0;
            int streetBudget = Application.isMobilePlatform ? 64 : CitizenSimulation.StreetCapacity;
            int workBudget = Application.isMobilePlatform ? 16 : MaximumWorksiteResidents;
            Camera camera = controller.CameraRig != null ? controller.CameraRig.Camera : null;
            var residents = simulation.Residents;
            for (int i = 0; i < residents.Count; i++)
            {
                Citizen citizen = residents[i];
                bool guiding = citizen.IsGuide && simulation.GuideControlled;
                bool worksite = false;
                WorksitePose pose = default;
                Transform building = null;
                int slot = -1;
                if (citizen.Indoors)
                {
                    if (guiding || ActiveWorksiteCount >= workBudget || !CitizenWorksites.IsWorksiteDwell(citizen, boundState)) continue;
                    slot = worksiteSlots[citizen.CurrentIndex];
                    if (!CitizenWorksites.TryGetPose(citizen, boundState, slot, out pose) ||
                        !controller.Board.TryGetBuildingTransform(citizen.CurrentIndex, out building)) continue;
                    worksiteSlots[citizen.CurrentIndex]++; ActiveWorksiteCount++; worksite = true;
                }
                else
                {
                    if (VisibleCount >= streetBudget) continue;
                    VisibleCount++;
                }
                activeIds.Add(citizen.Id);
                if (!figures.TryGetValue(citizen.Id, out var figure))
                {
                    figure = CreateFigure(citizen); figures.Add(citizen.Id, figure);
                }
                figure.citizen = citizen;
                int modelIndex = ModelIndex(citizen);
                if (figure.guideStyle != citizen.IsGuide || figure.modelIndex != modelIndex)
                {
                    figure.guideStyle = citizen.IsGuide;
                    figure.modelIndex = modelIndex;
                    figure.actor.SetAppearance(citizen.IsGuide, modelIndex);
                }
                if (!figure.root.activeSelf) figure.root.SetActive(true);
                figure.worksite = worksite;
                if (worksite)
                {
                    if (figure.stationBuilding != building || figure.worksiteSlot != slot)
                    {
                        CitizenWorksites.CreateStation(building, boundState.Cells[citizen.CurrentIndex].Building, slot);
                        figure.stationBuilding = building;
                    }
                    figure.worksiteIndex = citizen.CurrentIndex; figure.worksiteSlot = slot;
                    figure.root.transform.position = building.TransformPoint(pose.LocalPosition);
                    figure.root.transform.rotation = building.rotation * Quaternion.Euler(0f, pose.LocalYaw, 0f);
                    figure.action = pose.Action; figure.workLabel = pose.Label;
                }
                else
                {
                    figure.worksiteIndex = -1; figure.worksiteSlot = -1; figure.workLabel = "";
                    PositionOnStreet(figure);
                    figure.action = citizen.Path.Count > 1 ? ResidentAction.Walk
                        : guiding && controller.Tutorial != null && !controller.Tutorial.IsCollapsed ? ResidentAction.Talk : ResidentAction.Idle;
                }
                bool isSelected = ReferenceEquals(citizen, selectedCitizen);
                bool selectedAtThisSite = selectedCitizen != null && selectedCitizen.Indoors &&
                    selectedCitizen.CurrentIndex == citizen.CurrentIndex;
                bool showLabel = worksite && camera != null && (isSelected ||
                    (!selectedAtThisSite && slot == 0 && camera.orthographicSize <= 4.5f));
                figure.actor.Tick(figure.action, guiding ? guideDt : dt, isSelected, showLabel, figure.workLabel, camera);
                if (figure.actor.HasImportedModel) ImportedModelCount++;
            }
            foreach (var pair in figures)
                if (!activeIds.Contains(pair.Key) && pair.Value.root.activeSelf) pair.Value.root.SetActive(false);
            TrimInactiveFigures(streetBudget + workBudget + 1);
            RenderSelection();
        }

        void TrimInactiveFigures(int cacheBudget)
        {
            if (figures.Count <= cacheBudget) return;
            retiredIds.Clear();
            foreach (var pair in figures)
            {
                // Keep the guide root stable even between narration sessions.
                if (!pair.Value.root.activeSelf && !pair.Value.citizen.IsGuide) retiredIds.Add(pair.Key);
                if (figures.Count - retiredIds.Count <= cacheBudget) break;
            }
            foreach (int id in retiredIds) { Destroy(figures[id].root); figures.Remove(id); }
        }

        void PositionOnStreet(Figure figure)
        {
            Citizen citizen = figure.citizen;
            int x = Mathf.Clamp(Mathf.RoundToInt(citizen.X), 0, boundState.Size - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(citizen.Z), 0, boundState.Size - 1);
            bool bridge = boundState.Cells[z * boundState.Size + x].Terrain == TerrainKind.Water;
            Vector3 position = BoardView.Position(0, 0) + new Vector3(citizen.X * BoardView.Spacing,
                bridge ? .11f : .047f, citizen.Z * BoardView.Spacing);
            if (citizen.Path.Count > 1)
            {
                int best = Mathf.Clamp(citizen.pathStep, 0, citizen.Path.Count - 2);
                int a = citizen.Path[best], b = citizen.Path[best + 1];
                Vector2 direction = new Vector2(b % boundState.Size - a % boundState.Size, b / boundState.Size - a / boundState.Size).normalized;
                position += new Vector3(-direction.y, 0f, direction.x) * (.20f + citizen.Id % 3 * .025f);
                figure.root.transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y), Vector3.up);
            }
            figure.root.transform.position = position;
        }
        Figure CreateFigure(Citizen citizen)
        {
            var root = new GameObject("Resident " + citizen.Id); root.transform.SetParent(transform, false); root.layer = 9;
            root.AddComponent<CitizenHandle>().ResidentId = citizen.Id;
            var collider = root.AddComponent<CapsuleCollider>(); collider.height = .40f; collider.radius = .085f; collider.center = Vector3.up * .20f;
            var actor = root.AddComponent<ResidentActor>();
            int modelIndex = ModelIndex(citizen);
            actor.Initialize(citizen, citizen.IsGuide, visualLibrary, modelIndex);
            return new Figure { citizen = citizen, root = root, actor = actor, guideStyle = citizen.IsGuide, modelIndex = modelIndex };
        }
        int ModelIndex(Citizen citizen) => citizen.JobIndex >= 0 && citizen.JobIndex < boundState.Cells.Count &&
            boundState.Cells[citizen.JobIndex].Building == BuildingKind.Bakery ? 3 : Math.Abs(citizen.Id) % 3;
        void RenderSelection()
        {
            if (selectedCitizen == null || !figures.TryGetValue(selectedCitizen.Id, out var figure) || !figure.root.activeSelf)
            {
                if (selectionRing != null) selectionRing.SetActive(false); return;
            }
            selectionRing.SetActive(true);
            selectionRing.transform.position = figure.root.transform.position + Vector3.up * .004f;
        }
        string Details(Citizen citizen)
        {
            string job = citizen.JobIndex >= 0 ? Catalog.Get(boundState.Cells[citizen.JobIndex].Building).Name : "일자리 없음";
            string destination = citizen.DestinationIndex >= 0 ? Catalog.Get(boundState.Cells[citizen.DestinationIndex].Building).Name : "집";
            bool guiding = citizen.IsGuide && simulation.GuideControlled;
            string action;
            if (guiding) action = "안내 중";
            else if (CitizenWorksites.TryGetPose(citizen, boundState, 0, out var pose)) action = pose.Label;
            else switch (citizen.Activity)
            {
                case CitizenActivity.GoingToWork: action = "출근 중"; break;
                case CitizenActivity.Working: action = "일하는 중"; break;
                case CitizenActivity.GoingToLeisure: action = "장 보러 가는 중"; break;
                case CitizenActivity.Leisure: action = "쉬는 중"; break;
                case CitizenActivity.GoingHome: action = "귀가 중"; break;
                default: action = "집에서 쉬는 중"; break;
            }
            string source = guiding ? "역할: 안내 중" : citizen.LastDecisionSource == "model"
                ? "판단: LLM · " + PlainText(citizen.LastSourceModel, 32) : "판단: 기본 일과";
            string thought = guiding || string.IsNullOrWhiteSpace(citizen.LastThought) ? "" : "\n생각: " + PlainText(citizen.LastThought, 42);
            return citizen.Name + " · " + job + " · " + action + " · 목적지 " + destination + "\n" + source + thought;
        }
        static string PlainText(string text, int maximumLength)
        {
            string plain = (text ?? "").Replace("<", "‹").Replace(">", "›");
            return plain.Length > maximumLength ? plain.Substring(0, maximumLength) + "…" : plain;
        }
        bool ContainsResident(int id) { Citizen ignored; return TryResident(id, out ignored); }
        bool TryResident(int id, out Citizen result)
        {
            foreach (var citizen in simulation.Residents) if (citizen.Id == id) { result = citizen; return true; }
            result = null; return false;
        }
        void ClearFigures()
        {
            foreach (var pair in figures) { pair.Value.root.SetActive(false); Destroy(pair.Value.root); }
            figures.Clear();
        }
        void OnDestroy() { if (selected) Destroy(selected); }
    }
}

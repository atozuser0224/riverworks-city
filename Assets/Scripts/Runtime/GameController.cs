using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Riverworks
{
    public sealed class GameController : MonoBehaviour
    {
        [NonSerialized] public Simulation Sim;
        public GameState State => Sim.State;
        public BuildingKind SelectedTool { get; private set; }
        public Cell SelectedCell { get; private set; }
        public bool DemolitionMode { get; private set; }
        public float GameSpeed { get; private set; } = 1;
        public bool HelpOpen { get; private set; }
        public bool ResearchOpen { get; private set; }
        public bool ModalOpen { get; set; }
        public string Notice { get; private set; } = "중세 마을의 첫 아침입니다. 곡물로 주민을 먹이고, T 키로 기술을 연구하세요.";
        public event Action Changed;
        public int NoticeVersion { get; private set; }
        public BoardView Board { get; private set; }
        public OrbitCamera CameraRig { get; private set; }
        public CitizenView Citizens { get; private set; }
        public FactoryController Factory { get; private set; }
        public CityProjectView ProjectView { get; private set; }
        public CityProjectKind ProjectSiteTool { get; private set; }
        public bool ProjectToolActive => ProjectSiteTool!=CityProjectKind.None;
        public FeelDirector Feel { get; private set; }
        public TutorialDirector Tutorial { get; private set; }
        public ResidentAiClient ResidentAi { get; private set; }
        Hud cityHud;
        public Hud CityHud => cityHud;
        public FactoryEntity SelectedFactory => Factory!=null?Factory.SelectedEntity:null;
        public bool FactoryToolActive => Factory!=null&&(Factory.SelectedTool!=FactoryKind.None||Factory.RemovalMode||Factory.FoundationMode);
        public string PowerUsageText => (Sim.PowerUsed+(Factory!=null?Factory.Sim.PowerUsed:0)).ToString("0.#");
        public string ResidentDetails => Citizens != null ? Citizens.SelectedDetails : "";
        public int PeopleOnStreet => Citizens != null ? Citizens.VisibleCount : 0;
        public bool SmokeMode { get; private set; }
        public string SavePath { get; private set; }
        private float clock, autoSaveClock, noticeClock, uiClock;
        private int lastPaint = -1;
        private int lastFactoryPaint=-1;
        private Vector3 rightDown;
        private bool leftWorldStroke, rightWorldGesture, middleWorldGesture, rightHasDragged;
        private bool allowAutoSave = true;
        private PointerEventData pointer;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        public bool CanPanWithRight => rightWorldGesture && rightHasDragged && Input.GetMouseButton(1);
        public bool CanPanWithMiddle => middleWorldGesture && Input.GetMouseButton(2);
        public bool UiTextInputFocused
        {
            get
            {
                GameObject selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                UnityEngine.UI.InputField input = selected == null ? null : selected.GetComponent<UnityEngine.UI.InputField>();
                return input != null && input.isFocused;
            }
        }
        private Soundscape sounds;
        public const float SecondsPerDay = 5f;

        void Awake()
        {
            Application.targetFrameRate = Application.isMobilePlatform?30:60;
            bool feelSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-feel-smoke")>=0;
            bool tutorialSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-tutorial-smoke")>=0;
            bool residentAiSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-resident-ai-smoke")>=0;
            bool residentActivitySmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-resident-activity-smoke")>=0;
            bool compactUiSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-compact-ui-smoke")>=0;
            bool researchTreeSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-research-tree-smoke")>=0;
            bool industrySmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-industry-smoke")>=0;
            bool expansionSmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-expansion-smoke")>=0;
            bool factorySmoke=Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-factory-smoke")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(),"-riverworks-shared-city-smoke")>=0;
            SmokeMode = expansionSmoke || industrySmoke || researchTreeSmoke || residentActivitySmoke || residentAiSmoke || tutorialSmoke || feelSmoke || compactUiSmoke || factorySmoke || Array.IndexOf(Environment.GetCommandLineArgs(), "-riverworks-smoke") >= 0;
            SavePath = SmokeMode ? Path.Combine(Application.persistentDataPath, "smoke-test.json") : Path.Combine(Application.persistentDataPath, "city-v1.json");
            var initial = GameState.CreateNew();
            if (!SmokeMode && (File.Exists(SavePath) || File.Exists(SavePath + ".bak")))
            {
                if (SaveStore.TryLoad(SavePath, out var loaded, out var error)) { initial = loaded; Notice = "저장된 도시를 이어서 시작합니다."; }
                else
                {
                    string quarantineId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
                    if (SaveStore.TryLoad(SavePath + ".bak", out var recovered, out _))
                    {
                        initial = recovered;
                        Notice = "이전 정상 백업에서 도시를 복구했습니다.";
                        if (!TryQuarantine(SavePath, quarantineId))
                        {
                            allowAutoSave = false;
                            Notice += " 손상된 주 저장을 별도로 보관하지 못해 자동 저장을 중지했습니다. 직접 저장할 수 있습니다.";
                        }
                    }
                    else
                    {
                        // Neither candidate is trustworthy. Keep the fallback city in memory only
                        // until the player explicitly chooses Save or New Game.
                        allowAutoSave = false;
                        bool primaryQuarantined = TryQuarantine(SavePath, quarantineId);
                        bool backupQuarantined = TryQuarantine(SavePath + ".bak", quarantineId);
                        Notice = primaryQuarantined && backupQuarantined
                            ? "저장 파일과 백업을 불러올 수 없어 새 도시를 열었습니다. 존재하는 손상 파일을 각각 별도로 보관했으며 자동 저장을 중지했습니다. 직접 저장하거나 새 도시를 시작할 수 있습니다."
                            : "저장 파일과 백업이 손상되어 새 도시를 열었습니다. 손상 파일 일부를 별도로 보관하지 못해 자동 저장을 중지했습니다. 직접 저장하거나 새 도시를 시작할 수 있습니다.";
                    }
                }
            }
            SetSimulation(initial);
            Board = new GameObject("Miniature board").AddComponent<BoardView>();
            Board.Initialize(this);
            CameraRig = new GameObject("Isometric camera").AddComponent<OrbitCamera>();
            CameraRig.Initialize(this);
            sounds = gameObject.AddComponent<Soundscape>();
            Citizens = new GameObject("Residents of Riverworks").AddComponent<CitizenView>();
            Citizens.Initialize(this);
            Feel=new GameObject("City feedback").AddComponent<FeelDirector>();
            Feel.transform.SetParent(transform,false);Feel.Initialize(this);
            cityHud=new GameObject("City interface").AddComponent<Hud>();cityHud.Initialize(this);
            Factory=new GameObject("Physical factory simulation").AddComponent<FactoryController>();Factory.Initialize(this);
            ProjectView=new GameObject("City construction projects").AddComponent<CityProjectView>();ProjectView.Initialize(this);
            gameObject.AddComponent<MobileInput>().Initialize(this);
            Tutorial=gameObject.AddComponent<TutorialDirector>();Tutorial.Initialize(this);
            ResidentAi=gameObject.AddComponent<ResidentAiClient>();ResidentAi.Initialize(this);
            if(SmokeMode)AudioListener.volume=0f;
            if(expansionSmoke)gameObject.AddComponent<ExpansionSmokeTest>().Initialize(this);
            else if(industrySmoke)gameObject.AddComponent<IndustrySmokeTest>().Initialize(this);
            else if(researchTreeSmoke)gameObject.AddComponent<ResearchTreeSmokeTest>().Initialize(this);
            else if(residentActivitySmoke)gameObject.AddComponent<ResidentActivitySmokeTest>().Initialize(this);
            else if(residentAiSmoke)gameObject.AddComponent<ResidentAiSmokeTest>().Initialize(this);
            else if(tutorialSmoke)gameObject.AddComponent<TutorialSmokeTest>().Initialize(this);
            else if(feelSmoke)gameObject.AddComponent<FeelSmokeTest>().Initialize(this);
            else if(compactUiSmoke)gameObject.AddComponent<CompactHudSmokeTest>().Initialize(this);
            else if(factorySmoke)gameObject.AddComponent<SharedCitySmokeTest>().Initialize(this);
            else if (SmokeMode) gameObject.AddComponent<RuntimeSmokeTest>().Initialize(this);
        }

        static bool TryQuarantine(string path, string quarantineId)
        {
            if (!File.Exists(path)) return true;
            try
            {
                File.Copy(path, path + ".damaged-" + quarantineId, false);
                return true;
            }
            catch { return false; }
        }

        void SetSimulation(GameState state)
        {
            if (Sim != null) Sim.OnNotice -= SetNotice;
            Sim = new Simulation(state);
            Sim.OnNotice += SetNotice;
            Sim.Recalculate();
            clock=State.DayProgressSeconds;
        }

        void Update()
        {
            if (Sim == null) return;
            float elapsed = Time.unscaledDeltaTime;
            if (!HelpOpen && !ModalOpen && !ResearchOpen)
            {
                clock += elapsed * GameSpeed;
                while (clock >= SecondsPerDay) { clock -= SecondsPerDay; Sim.Tick(); RefreshWorld(false); Changed?.Invoke(); }
                State.DayProgressSeconds=clock;
                autoSaveClock += elapsed * GameSpeed;
                if (autoSaveClock > 90f && !SmokeMode && allowAutoSave) { autoSaveClock = 0; Save(false); }
            }
            if (noticeClock > 0) { noticeClock -= elapsed; if (noticeClock <= 0) { Notice = ""; Changed?.Invoke(); } }
            uiClock += elapsed;
            if (uiClock >= .5f) { uiClock = 0; Changed?.Invoke(); }
            if (!SmokeMode&&!Application.isMobilePlatform) HandleInput();
        }

        void HandleInput()
        {
            if(Tutorial!=null&&Tutorial.IntroPlaying)
            {
                if(Input.GetKeyDown(KeyCode.Escape))Tutorial.SkipIntro();
                ClearGestures();return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (ModalOpen) { ModalOpen = false; Changed?.Invoke(); }
                else if (ResearchOpen) ToggleResearch();
                else if (HelpOpen) ToggleHelp();
                else { CancelProjectSite();SelectedTool = BuildingKind.None; DemolitionMode = false; Factory?.Close(); Changed?.Invoke(); }
                ClearGestures(); return;
            }
            if (UiTextInputFocused || ModalOpen) { ClearGestures(); return; }
            if (Input.GetKeyDown(KeyCode.F1)) ToggleHelp();
            if (Input.GetKeyDown(KeyCode.T)) ToggleResearch();
            if(Input.GetKeyDown(KeyCode.G))
            {
                if(HelpOpen)ToggleHelp();
                if(ResearchOpen)ToggleResearch();
                cityHud.ToggleFactoryTools();
                ClearGestures();return;
            }
            if (HelpOpen || ResearchOpen) { ClearGestures(); return; }
            if(Input.GetKeyDown(KeyCode.PageUp)){Factory.SetFloor(Math.Min(FactoryLayers.MaxFloor,Factory.ActiveFloor+1));ClearGestures();return;}
            if(Input.GetKeyDown(KeyCode.PageDown)){Factory.SetFloor(Math.Max(0,Factory.ActiveFloor-1));ClearGestures();return;}
            if (Input.GetKeyDown(KeyCode.Space) && !(Tutorial!=null&&Tutorial.ConsumesAdvanceShortcut)) SetSpeed(GameSpeed == 0 ? 1 : 0);
            else if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(3);
            if (Input.GetKeyDown(KeyCode.R)) { if(FactoryToolActive||SelectedFactory!=null)Factory.Rotate();else SelectTool(BuildingKind.Road); }
            else if (Input.GetKeyDown(KeyCode.H)) SelectTool(BuildingKind.House);
            else if (Input.GetKeyDown(KeyCode.Delete)) { if(FactoryToolActive||SelectedFactory!=null)Factory.SelectRemove();else SelectDemolish(); }
            if (Input.GetKeyDown(KeyCode.F5)) SaveGame();
            if (Input.GetKeyDown(KeyCode.M)) { AudioListener.volume = AudioListener.volume > 0 ? 0 : 0.45f; SetNotice(AudioListener.volume > 0 ? "소리를 켰습니다." : "소리를 껐습니다."); }
            bool overUI = IsPointerOverUI();
            if (Input.GetMouseButtonDown(1)) { rightDown = Input.mousePosition; rightHasDragged = false; rightWorldGesture = !overUI; }
            if (Input.GetMouseButtonDown(2)) middleWorldGesture = !overUI;
            if (Input.GetMouseButton(1) && Vector3.Distance(rightDown, Input.mousePosition) >= 6f) rightHasDragged = true;
            if (Input.GetMouseButtonUp(1)) { if (rightWorldGesture && !rightHasDragged && !overUI) SelectTool(BuildingKind.None); rightWorldGesture = false; }
            if (Input.GetMouseButtonUp(2)) middleWorldGesture = false;
            if (Input.GetMouseButtonDown(0)) { leftWorldStroke = !overUI; lastPaint = -1; lastFactoryPaint=-1; }
            if (!Input.GetMouseButton(0)) { lastPaint = -1; lastFactoryPaint=-1; leftWorldStroke = false; }
            if (overUI) { Board.Highlight(null, false); Factory.View.Highlight(-1,-1,false);ProjectView?.HidePlacement(); lastPaint = -1;lastFactoryPaint=-1; return; }
            if(ProjectToolActive)
            {
                Board.Highlight(null,false);Factory.View.Highlight(-1,-1,false);
                if(Physics.Raycast(CameraRig.Camera.ScreenPointToRay(Input.mousePosition),out var projectHit,200f,1<<8))
                {
                    var projectTile=projectHit.collider.GetComponent<TileHandle>();
                    if(projectTile!=null)
                    {
                        bool validSite=CityProjects.CanStart(State,ProjectSiteTool,projectTile.X,projectTile.Z,out _);
                        ProjectView?.ShowPlacement(ProjectSiteTool,projectTile.X,projectTile.Z,validSite);
                        if(Input.GetMouseButtonDown(0)&&leftWorldStroke)StartProjectAt(projectTile.X,projectTile.Z);
                    }
                    else ProjectView?.HidePlacement();
                }
                else ProjectView?.HidePlacement();
                return;
            }
            if(FactoryToolActive||Factory.ActiveFloor>0)
            {
                Board.Highlight(null,false);
                if(!Factory.View.TryGetFloorPoint(Input.mousePosition,Factory.ActiveFloor,out var factoryPoint)){Factory.View.Highlight(-1,-1,false);return;}
                var grid=FactoryController.GridAt(factoryPoint);Factory.HighlightPoint(factoryPoint);
                int cellIndex=grid.y*Factory.State.Width+grid.x;
                bool painting=leftWorldStroke&&Factory.SelectedTool==FactoryKind.Belt&&Input.GetMouseButton(0)&&cellIndex!=lastFactoryPaint;
                if(Input.GetMouseButtonDown(0)&&leftWorldStroke||painting)
                {
                    if(painting&&lastFactoryPaint>=0)
                    {
                        int fx=lastFactoryPaint%Factory.State.Width,fz=lastFactoryPaint/Factory.State.Width;
                        while(fx!=grid.x||fz!=grid.y){if(fx!=grid.x)fx+=Math.Sign(grid.x-fx);else fz+=Math.Sign(grid.y-fz);if(Factory.Sim.GetAt(fx,fz,Factory.ActiveFloor)==null)Factory.PlaceAt(fx,fz);}
                    }
                    else Factory.PlaceAt(grid.x,grid.y);
                    lastFactoryPaint=cellIndex;
                }
                return;
            }
            if (Input.GetMouseButtonDown(0) && leftWorldStroke && SelectedTool == BuildingKind.None && !DemolitionMode && Citizens.TrySelect(Input.mousePosition))
            {
                SelectedCell = null; Factory?.ClearSelection(); Changed?.Invoke();
                if(Citizens.IsGuideSelected)Tutorial?.OpenGuide();return;
            }
            if (!Physics.Raycast(CameraRig.Camera.ScreenPointToRay(Input.mousePosition), out var hit, 200f, 1 << 8)) { Board.Highlight(null, false); return; }
            var tile = hit.collider.GetComponent<TileHandle>();
            if (tile == null) return;
            var factoryGrid=FactoryController.GridAt(hit.point);
            Factory.View.Highlight(-1,-1,false);
            if(Input.GetMouseButtonDown(0)&&SelectedTool==BuildingKind.None&&!DemolitionMode&&Factory.SelectAt(factoryGrid.x,factoryGrid.y))return;
            var cell = Sim.GetCell(tile.X, tile.Z);
            bool valid = DemolitionMode ? cell.Building != BuildingKind.None && cell.Building != BuildingKind.TownHall : SelectedTool == BuildingKind.None || Sim.CanBuild(SelectedTool, tile.X, tile.Z, out _);
            Board.Highlight(cell, valid);
            int index = tile.Z * State.Size + tile.X;
            bool paint = leftWorldStroke && SelectedTool == BuildingKind.Road && Input.GetMouseButton(0) && index != lastPaint;
            if (!(Input.GetMouseButtonDown(0) && leftWorldStroke) && !paint) return;
            if (paint && lastPaint >= 0)
            {
                PaintRoadBetween(lastPaint % State.Size, lastPaint / State.Size, tile.X, tile.Z);
                lastPaint = index; return;
            }
            lastPaint = index;
            InteractCell(tile.X, tile.Z);
        }

        void ClearGestures() { leftWorldStroke = rightWorldGesture = middleWorldGesture = false; lastPaint = -1;lastFactoryPaint=-1; Board.Highlight(null, false);Factory?.View?.Highlight(-1,-1,false);ProjectView?.HidePlacement(); }
        void PaintRoadBetween(int x, int z, int endX, int endZ)
        {
            // An orthogonal staircase fills fast drags without leaving disconnected diagonal roads.
            while (x != endX || z != endZ)
            {
                if (x != endX) { x += Math.Sign(endX - x); if (Sim.GetCell(x,z)?.Building != BuildingKind.Road) InteractCell(x,z); }
                if (z != endZ) { z += Math.Sign(endZ - z); if (Sim.GetCell(x,z)?.Building != BuildingKind.Road) InteractCell(x,z); }
            }
        }

        public void InteractCell(int x, int z)
        {
            if(ProjectToolActive){StartProjectAt(x,z);return;}
            if(SelectedTool==BuildingKind.None&&!DemolitionMode&&CityProjects.Occupies(State,x,z)){cityHud.OpenCityProjects();return;}
            Citizens?.ClearSelection();
            Factory?.ClearSelection();
            SelectedCell = Sim.GetCell(x,z);
            if (SelectedCell == null) return;
            bool success = false;
            string reason = "";
            if (DemolitionMode) success = Sim.Demolish(x,z,out reason);
            else if (SelectedTool != BuildingKind.None) success = Sim.Build(SelectedTool,x,z,out reason);
            else if (!Sim.IsOwned(x,z)) SetNotice("미개척 구역입니다. 하단 영토 버튼에서 매입할 수 있습니다.");
            if (DemolitionMode || SelectedTool != BuildingKind.None)
            {
                SetNotice(reason);
                sounds.Play(success ? Soundscape.Cue.Build : Soundscape.Cue.Denied);
                if (success) RefreshWorld(true);
                Feel?.Play(success?(DemolitionMode?FeelCue.Demolish:FeelCue.Build):FeelCue.Denied,BoardView.Position(x,z),success&&!DemolitionMode?Board.ModelAt(x,z):null);
            }
            else Feel?.Play(FeelCue.Select,BoardView.Position(x,z),Board.ModelAt(x,z));
            Changed?.Invoke();
        }

        public void SelectTool(BuildingKind kind) { CancelProjectSite();Factory?.Close();SelectedTool = kind; DemolitionMode = false; SelectedCell = null; Citizens?.ClearSelection(); Changed?.Invoke(); sounds?.Play(Soundscape.Cue.Click); }
        public void SelectDemolish() { CancelProjectSite();Factory?.Close();SelectedTool = BuildingKind.None; DemolitionMode = !DemolitionMode; Citizens?.ClearSelection(); Changed?.Invoke(); }
        public void ClearCityToolForFactory(){ProjectSiteTool=CityProjectKind.None;ProjectView?.HidePlacement();SelectedTool=BuildingKind.None;SelectedCell=null;DemolitionMode=false;Citizens?.ClearSelection();}
        public void ClearConstructionTools(){ClearCityToolForFactory();Factory?.CancelTools();}
        public void ClearCitySelection(){SelectedCell=null;Citizens?.ClearSelection();}
        public void NotifyWorldSelection(){Changed?.Invoke();}
        public void SetSpeed(float speed) { GameSpeed = speed == 0 ? 0 : speed >= 3 ? 3 : 1; Changed?.Invoke(); }
        public void ToggleHelp() { HelpOpen = !HelpOpen; if(HelpOpen)ResearchOpen=false; Changed?.Invoke(); }
        public void ToggleResearch() { ResearchOpen = !ResearchOpen; if(ResearchOpen)HelpOpen=false; ClearGestures(); Changed?.Invoke(); }
        public void Research(TechId id)
        {
            bool success = Sim.StartResearch(id,out var reason);
            if(success) ResearchOpen=false;
            SetNotice(reason); sounds.Play(success ? Soundscape.Cue.Expand : Soundscape.Cue.Denied); Changed?.Invoke();
            Feel?.Play(success?FeelCue.ResearchStart:FeelCue.Denied,BoardView.Position(10,10));
        }
        public void RefreshWorld(bool structureChanged) { Board?.Refresh(structureChanged); Citizens?.Refresh();Factory?.NotifyCityChanged();ProjectView?.Refresh(); }
        public void OpenFactory(){if(Factory.IsOpen)Factory.Close();else{HelpOpen=false;ResearchOpen=false;ModalOpen=false;Factory.Open(false);}}
        public void SetFactoryDisplay(bool open)
        {
            // Kept for old callers: factories now share the city, so no surface is hidden.
            Changed?.Invoke();
        }
        public void SetNotice(string message) { Notice = message; NoticeVersion++; noticeClock = 9; Changed?.Invoke(); }
        public bool IsPointerOverUI() => IsScreenPointOverUI(Input.mousePosition);
        public bool IsScreenPointOverUI(Vector2 position)
        {
            if(Tutorial!=null&&Tutorial.IntroPlaying)return true;
            if (HelpOpen || ModalOpen || ResearchOpen) return true;
            if (EventSystem.current == null) return false;
            if (pointer == null) pointer = new PointerEventData(EventSystem.current);
            pointer.position = position; uiHits.Clear(); EventSystem.current.RaycastAll(pointer, uiHits);
            return uiHits.Count > 0;
        }
        public bool InteractScreenPoint(Vector2 position)
        {
            if (IsScreenPointOverUI(position)) return false;
            if(!ProjectToolActive&&(FactoryToolActive||Factory.ActiveFloor>0))return Factory.InteractScreenPoint(position);
            if(ProjectToolActive)
            {
                if(!Physics.Raycast(CameraRig.Camera.ScreenPointToRay(position),out var projectHit,200f,1<<8))return false;
                var projectTile=projectHit.collider.GetComponent<TileHandle>();if(projectTile==null)return false;
                StartProjectAt(projectTile.X,projectTile.Z);return true;
            }
            if (!FactoryToolActive&&SelectedTool==BuildingKind.None && !DemolitionMode && Citizens!=null && Citizens.TrySelect(position)) { SelectedCell=null; Factory?.ClearSelection(); Changed?.Invoke();if(Citizens.IsGuideSelected)Tutorial?.OpenGuide();return true; }
            if (!Physics.Raycast(CameraRig.Camera.ScreenPointToRay(position), out var hit, 200f, 1 << 8)) return false;
            var tile = hit.collider.GetComponent<TileHandle>(); if (tile == null) return false;
            var micro=FactoryController.GridAt(hit.point);
            if(FactoryToolActive){Factory.PlaceAt(micro.x,micro.y);return true;}
            if(SelectedTool==BuildingKind.None&&!DemolitionMode&&Factory.SelectAt(micro.x,micro.y))return true;
            InteractCell(tile.X,tile.Z); return true;
        }
        public void SelectProjectSite(CityProjectKind kind)
        {
            var spec=CityProjects.Get(kind);
            if(spec==null){SetNotice("프로젝트를 선택하세요.");return;}
            if(!TechCatalog.Has(State,spec.RequiredTech)){SetNotice(TechCatalog.Get(spec.RequiredTech).Name+" 연구가 필요합니다.");return;}
            cityHud.CloseTransientPanels();Factory?.Close();ClearCityToolForFactory();
            ProjectSiteTool=kind;HelpOpen=ResearchOpen=ModalOpen=false;
            SetNotice(spec.Name+" 부지를 선택하세요 · "+spec.Width+"×"+spec.Height+" 도시 칸 · 시작 비용 "+spec.CoinCost+"G");Changed?.Invoke();
        }
        public void CancelProjectSite(){ProjectSiteTool=CityProjectKind.None;ProjectView?.HidePlacement();Changed?.Invoke();}
        public bool StartProjectAt(int x,int z)
        {
            if(!ProjectToolActive)return false;
            bool okay=CityProjects.Start(State,ProjectSiteTool,x,z,out string reason);
            if(okay){ProjectSiteTool=CityProjectKind.None;ProjectView?.HidePlacement();Sim.Recalculate();RefreshWorld(true);cityHud.OpenCityProjects();}
            SetNotice(reason);Feel?.Play(okay?FeelCue.Build:FeelCue.Denied,BoardView.Position(x,z));return okay;
        }
        public void DeliverProject(CityProjectKind kind)
        {
            bool okay=CityProjects.Deliver(State,kind,out string reason);
            if(okay){Sim.Recalculate();RefreshWorld(true);}
            SetNotice(reason);
        }
        public void CancelProject(CityProjectKind kind)
        {
            bool okay=CityProjects.Cancel(State,kind,out string reason);
            if(okay){Sim.Recalculate();RefreshWorld(true);}
            SetNotice(reason);
        }
        public void BuyRegion(int id)
        {
            bool ok = Sim.BuyRegion(id, out var reason);
            SetNotice(reason); sounds.Play(ok ? Soundscape.Cue.Expand : Soundscape.Cue.Denied);
            if (ok) { RefreshWorld(true); CameraRig.FrameRegion(id); }
            Feel?.Play(ok?FeelCue.Expand:FeelCue.Denied,BoardView.Position(id%3*7+3,id/3*7+3));
            Changed?.Invoke();
        }
        public void UpgradeSelected()
        {
            if (SelectedCell == null) return;
            bool ok = Sim.Upgrade(SelectedCell.X,SelectedCell.Z,out var reason);
            SetNotice(reason); sounds.Play(ok ? Soundscape.Cue.Build : Soundscape.Cue.Denied);
            if (ok) RefreshWorld(true);
            Feel?.Play(ok?FeelCue.Upgrade:FeelCue.Denied,BoardView.Position(SelectedCell.X,SelectedCell.Z),ok?Board.ModelAt(SelectedCell.X,SelectedCell.Z):null);
            Changed?.Invoke();
        }

        public static int TradePrice(Resource resource)
        {
            return ResourceCatalog.Get(resource)?.TradePrice ?? 0;
        }
        public void Trade(Resource resource, bool buy)
        {
            if (!ResourceCatalog.IsTradeable(resource))
            {
                SetNotice("이 물자는 교역할 수 없습니다. 공장 공정으로 생산하세요.");
                return;
            }
            const int amount = 10;
            int price = TradePrice(resource);
            int index = (int)resource;
            if (buy)
            {
                if (State.Coins < amount*price) { SetNotice("수입할 자금이 부족합니다. 남는 물자를 판매하거나 세금을 모으세요."); Feel?.Play(FeelCue.Denied,BoardView.Position(10,10));return; }
                State.Coins -= amount*price; State.Stock[index] += amount;
            }
            else
            {
                if (Sim.Get(resource) < amount) { SetNotice("판매하려면 물자가 10개 이상 필요합니다."); Feel?.Play(FeelCue.Denied,BoardView.Position(10,10));return; }
                State.Stock[index] -= amount; State.Coins += amount * Mathf.Max(1, price/2);
            }
            State.Stock[(int)Resource.Coins] = State.Coins;
            Sim.Recalculate();
            SetNotice(Catalog.ResourceName(resource) + " 10개를 " + (buy ? "수입했습니다." : "판매했습니다."));
            sounds.Play(Soundscape.Cue.Click); Changed?.Invoke();
            Feel?.Play(FeelCue.Trade,BoardView.Position(10,10));
        }

        public bool TrySaveGame(out string error)
        {
            if (Sim == null)
            {
                error = "저장할 도시가 없습니다.";
                return false;
            }
            bool saved = SaveStore.TrySave(SavePath, Sim.State, out error);
            if (saved) allowAutoSave = true;
            return saved;
        }
        public bool SaveGame() => Save(true);
        bool Save(bool notify)
        {
            if (TrySaveGame(out var error))
            {
                if(notify) { SetNotice("도시를 저장했습니다. 다음 실행 때 이어서 시작합니다.");Feel?.Play(FeelCue.Save,BoardView.Position(10,10));Tutorial?.NotifySave(true); }
                return true;
            }
            SetNotice("저장 실패: " + error);
            if(notify)Feel?.Play(FeelCue.Denied,BoardView.Position(10,10));
            if(notify)Tutorial?.NotifySave(false);
            return false;
        }
        public void LoadGame()
        {
            if (SaveStore.TryLoad(SavePath, out var loaded, out var error)) { CloseFactoryForCityStateChange(); SetSimulation(loaded); ResetView(); SetNotice("저장한 도시를 불러왔습니다."); }
            else SetNotice("불러오기 실패: " + error);
        }
        public void NewGame()
        {
            var fresh = GameState.CreateNew();
            if (!SmokeMode)
            {
                if (!SaveStore.TrySave(SavePath + ".previous-city.json", State, out var backupError))
                {
                    SetNotice("이전 도시를 백업하지 못했습니다. 현재 도시를 유지합니다. " + backupError); return;
                }
                if (!SaveStore.TrySave(SavePath, fresh, out var saveError))
                {
                    SetNotice("새 도시를 저장하지 못했습니다. 현재 도시를 유지합니다. " + saveError); return;
                }
                allowAutoSave = true;
            }
            CloseFactoryForCityStateChange(); SetSimulation(fresh); ResetView(); SetNotice("중세 마을을 시작했습니다. 곡물을 생산하고 T 키로 기술을 연구하세요.");
        }
        void CloseFactoryForCityStateChange() { if (Factory != null && Factory.IsOpen) Factory.Close(); }
        void ResetView() { ProjectSiteTool=CityProjectKind.None;ProjectView?.HidePlacement();SelectedCell = null; SelectedTool = BuildingKind.None; DemolitionMode = false; HelpOpen=false; ResearchOpen=false; ModalOpen=false; clock=State.DayProgressSeconds; autoSaveClock=0; GameSpeed=1; RefreshWorld(true); Citizens?.ClearSelection(); CameraRig.Home(); Changed?.Invoke(); }
        void OnApplicationQuit() { if (!SmokeMode && Sim != null && allowAutoSave) Save(false); }
        void OnApplicationPause(bool paused){if(paused&&!SmokeMode&&Sim!=null&&allowAutoSave)Save(false);}
    }
}

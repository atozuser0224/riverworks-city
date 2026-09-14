using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Opt-in executable verification. Uses a separate save slot and never loads the player's city.</summary>
    public sealed class RuntimeSmokeTest : MonoBehaviour
    {
        GameController controller;
        string output;
        readonly List<string> checks=new List<string>();
        readonly List<string> errors=new List<string>();
        readonly Dictionary<string,ulong> captureHashes=new Dictionary<string,ulong>();
        bool completed;
        public void Initialize(GameController value)
        {
            controller=value;
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-riverworks-output");
            output=at>=0 && at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"SmokeArtifacts");
            Directory.CreateDirectory(output); Application.logMessageReceived+=OnLog;
            StartCoroutine(Run());
        }
        void OnLog(string condition,string trace,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) errors.Add(condition+"\n"+trace); }
        void OnDestroy() { Application.logMessageReceived-=OnLog; }
        void Assert(bool condition,string name) { if(!condition) throw new Exception("SMOKE FAIL: "+name); checks.Add("PASS "+name); Debug.Log("SMOKE PASS: "+name); }
        IEnumerator Run()
        {
            yield return SmokeExecution.Run(RunSteps(), exception => Fail(exception.ToString()));
            if(!completed)Complete();
        }
        IEnumerator RunSteps()
        {
            controller.SetSpeed(0);
            // Hidden Windows launches can defer their first swap chain until a resize.
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.35f);
            Screen.SetResolution(1600,900,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.35f);
            float splashDeadline=Time.realtimeSinceStartup+10f;
            while(!SplashScreen.isFinished && Time.realtimeSinceStartup<splashDeadline) yield return null;
            if(!SplashScreen.isFinished) { Fail("Splash screen did not finish within 10 seconds"); Complete(); yield break; }
            yield return null;
            yield return Capture("00-ready.png",null);
            if(errors.Count>0) {Complete();yield break;}
            IEnumerator gameplay = VerifyGameplayBeforeResearch();
            while(true)
            {
                object step;
                try { if(!gameplay.MoveNext())break; step=gameplay.Current; }
                catch(Exception e) {Fail(e.ToString());Complete();yield break;}
                yield return step;
            }
            controller.ToggleResearch();
            yield return new WaitForEndOfFrame();
            yield return Capture("01-medieval-research-1600.png","00-ready.png");
            if(errors.Count>0) {Complete();yield break;}
            try
            {
                Assert(controller.ResearchOpen,"research tree opens and blocks the world");
                VerifyResearchCatalog();
                Click("Button_연구 시작_Stonecraft");
                Assert(!controller.ResearchOpen && controller.State.ActiveResearch==TechId.Stonecraft,"research starts through the rendered UI button");
                VerifyActiveResearchSave();
                int researchStartDay=controller.State.Day;
                controller.Sim.Tick();controller.Sim.Tick();controller.RefreshWorld(false);
                Assert(controller.State.Day==researchStartDay+2,"simulation ticks advance the research calendar");
                Assert(TechCatalog.Has(controller.State,TechId.Stonecraft) && controller.State.ActiveResearch==TechId.None,"Stonecraft completes after its two legitimate simulation ticks");
                Assert(controller.Sim.UpgradeLevelCap==2,"completed Stonecraft unlocks level two upgrades");
                controller.InteractCell(8,9);controller.UpgradeSelected();
                Assert(controller.Sim.GetCell(8,9).Level==2,"selected building upgrades after its technology completes");
            }
            catch(Exception e) {Fail(e.ToString());Complete();yield break;}

            controller.ToggleResearch();
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.7f);
            yield return new WaitForEndOfFrame();
            yield return Capture("02-medieval-research-1280.png",null);
            if(errors.Count>0) {Complete();yield break;}
            try { VerifyResearchScroll(); } catch(Exception e) { Fail(e.ToString()); Complete(); yield break; }
            controller.ToggleResearch();
            try { VerifyLayout(); } catch(Exception e) { Fail(e.ToString()); Complete(); yield break; }

            try { PrepareMedievalCitizens(); } catch(Exception e) { Fail(e.ToString()); Complete(); yield break; }
            Screen.SetResolution(1600,900,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.8f);
            try { Assert(controller.Citizens.ResidentCount==controller.State.Population,"resident identities equal the aggregate population"); }
            catch(Exception e) {Fail(e.ToString());Complete();yield break;}
            yield return VerifyRenderedCitizens();
            if(errors.Count>0) {Complete();yield break;}
            yield return new WaitForSecondsRealtime(1.9f);
            controller.SetNotice("주민들이 집과 일터, 시장 사이를 걸어 다니는 중세 마을");
            yield return Capture("03-medieval-living-city.png",null);
            if(errors.Count>0) {Complete();yield break;}
            yield return RecordWalkingPreview();

            try
            {
                ProgressTo(TechId.Scholarship);
                Assert(controller.State.Era==Era.Renaissance && !TechCatalog.Has(controller.State,TechId.UrbanPlanning),"Scholarship phase remains in the Renaissance");
                PrepareEraShowcase(false);
            }
            catch(Exception e) {Fail(e.ToString());Complete();yield break;}
            yield return new WaitForSecondsRealtime(2.7f);
            yield return Capture("04-renaissance-city.png","03-medieval-living-city.png");
            if(errors.Count>0) {Complete();yield break;}
            try { Assert(controller.State.Era==Era.Renaissance,"Guilds advances the city to the Renaissance"); ProgressTo(TechId.UrbanPlanning,true); PrepareEraShowcase(true); }
            catch(Exception e) {Fail(e.ToString());Complete();yield break;}
            yield return new WaitForSecondsRealtime(2.7f);
            yield return Capture("05-industrial-city.png","04-renaissance-city.png");
            if(errors.Count>0) {Complete();yield break;}
            try
            {
                int technologyCount=TechCatalog.All.Count();
                Assert(controller.State.Era==Era.Industrial && controller.State.Technologies.Count==technologyCount && TechCatalog.All.All(t=>TechCatalog.Has(controller.State,t.Id)),"all "+technologyCount+" catalog researches complete in the Industrial era");
                VerifyCompletedEraSave();
            }
            catch(Exception e) {Fail(e.ToString());Complete();yield break;}

            controller.ToggleHelp();yield return null;
            yield return Capture("06-help.png","05-industrial-city.png");
            controller.ToggleHelp();
            if(errors.Count>0) {Complete();yield break;}
            // Newly activated modal graphics acquire their raycast depth after rendering.
            Click("Button_Menu");
            yield return new WaitForEndOfFrame();
            Click("Button_새 도시");
            yield return new WaitForEndOfFrame();
            try { Click("Button_취소");Assert(!controller.ModalOpen,"new-city confirmation cancels"); }
            catch(Exception e) {Fail(e.ToString());Complete();yield break;}
            Complete();
        }
        IEnumerator VerifyGameplayBeforeResearch()
        {
            Assert(controller.State.Cells.Count==441,"441 rendered map cells");
            Assert(FindObjectsByType<TileHandle>().Length==441,"tile colliders and handles");
            Assert(FindObjectsByType<Canvas>().Length==1,"native HUD canvas");
            Assert(controller.CameraRig.Camera!=null,"isometric camera");
            VerifyLayout();
            var screen=controller.CameraRig.Camera.WorldToScreenPoint(BoardView.Position(10,10));
            Assert(Physics.Raycast(controller.CameraRig.Camera.ScreenPointToRay(screen),out var hit,200,1<<8)&&hit.collider.GetComponent<TileHandle>()?.X==10,"world picking through 3D buildings");
            Click("Button_주거");
            yield return new WaitForEndOfFrame();
            Click("Button_주택");
            Assert(controller.SelectedTool==BuildingKind.House,"HUD build action selects house");
            float coins=controller.State.Coins;
            var buildScreen=controller.CameraRig.Camera.WorldToScreenPoint(BoardView.Position(8,9));
            Assert(buildScreen.z>0&&!controller.IsScreenPointOverUI(buildScreen),"world point is not blocked by HUD");
            Assert(controller.InteractScreenPoint(buildScreen),"screen pointer routes to buildable world tile");
            Assert(controller.Sim.GetCell(8,9).Building==BuildingKind.House&&controller.State.Coins<coins,"controller construction spends materials");
            Assert(controller.Sim.GetCell(8,9).Connected,"placed house connects to road");
            controller.SelectTool(BuildingKind.None);controller.InteractCell(8,9);
            float beforeCoins=controller.State.Coins;int beforeLevel=controller.Sim.GetCell(8,9).Level;
            controller.UpgradeSelected();
            Assert(controller.Sim.GetCell(8,9).Level==beforeLevel && Mathf.Approximately(controller.State.Coins,beforeCoins),"upgrade is rejected before Stonecraft without mutation");
            Cell late=controller.Sim.GetCell(8,11);BuildingKind prior=late.Building;float lateCoins=controller.State.Coins;
            Assert(!controller.Sim.Build(BuildingKind.Mill,8,11,out _) && late.Building==prior && Mathf.Approximately(controller.State.Coins,lateCoins),"late production building is rejected before its research without mutation");
            float bread=controller.Sim.Get(Resource.Bread);controller.Trade(Resource.Bread,true);
            Assert(controller.Sim.Get(Resource.Bread)==bread+10,"import trade");
            controller.Trade(Resource.Bread,false);Assert(controller.Sim.Get(Resource.Bread)==bread,"export trade");
            int regions=controller.State.OwnedRegions.Count;controller.BuyRegion(1);
            Assert(controller.State.OwnedRegions.Count==regions+1,"territory purchase through controller");
            controller.ToggleHelp();Assert(controller.HelpOpen,"help opens");
            Assert(!controller.InteractScreenPoint(buildScreen),"help overlay blocks world construction");
            controller.ToggleHelp();Assert(!controller.HelpOpen,"help closes");
            Assert(errors.Count==0,"no runtime errors during gameplay");
        }
        static void Click(string name)
        {
            var button=FindObjectsByType<Button>(FindObjectsInactive.Include).FirstOrDefault(b=>b.name==name);
            if(button==null||!button.gameObject.activeInHierarchy||!button.interactable) throw new Exception("UI button unavailable: "+name);
            Canvas.ForceUpdateCanvases();
            var rect=(RectTransform)button.transform;
            Vector3[] corners=new Vector3[4];rect.GetWorldCorners(corners);
            var center=new Vector2((corners[0].x+corners[2].x)*.5f,(corners[0].y+corners[2].y)*.5f);
            if(corners[2].x-corners[0].x<1||corners[2].y-corners[0].y<1||center.x<0||center.x>Screen.width||center.y<0||center.y>Screen.height) throw new Exception("UI button has no visible screen rect: "+name);
            if(EventSystem.current==null) throw new Exception("EventSystem unavailable for UI click: "+name);
            var pointer=new PointerEventData(EventSystem.current){position=center,button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            if(hits.Count==0||!(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform))) throw new Exception("UI button is not the first raycast target: "+name+(hits.Count==0?" no hits":" hit="+hits[0].gameObject.name)+" center="+center+" depth="+button.targetGraphic.depth+" cull="+button.targetGraphic.canvasRenderer.cull);
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        static void AssertFirstHit(string name)
        {
            var button=FindObjectsByType<Button>(FindObjectsInactive.Include).FirstOrDefault(b=>b.name==name);
            if(button==null||!button.gameObject.activeInHierarchy)throw new Exception("UI button unavailable for hit test: "+name);
            Canvas.ForceUpdateCanvases();var rect=(RectTransform)button.transform;Vector3[] corners=new Vector3[4];rect.GetWorldCorners(corners);
            var center=new Vector2((corners[0].x+corners[2].x)*.5f,(corners[0].y+corners[2].y)*.5f);
            if(center.x<0||center.x>Screen.width||center.y<0||center.y>Screen.height)throw new Exception("UI button is outside the screen: "+name);
            var pointer=new PointerEventData(EventSystem.current){position=center,button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            if(hits.Count==0||!(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)))throw new Exception("UI button is not the first raycast target: "+name+(hits.Count==0?" no hits":" hit="+hits[0].gameObject.name));
        }
        void VerifyLayout()
        {
            var hud=FindAnyObjectByType<Hud>();
            Assert(hud!=null,"HUD exists for layout validation");
            Assert(hud.VerifyLayout(out var reason),"active categories and buttons are visible and raycastable at "+Screen.width+"x"+Screen.height+": "+reason);
            AssertFirstHit("Button_Factory");
            foreach(string category in new[]{"주거","생산","산업","도시"})
            {
                Click("Button_"+category);Canvas.ForceUpdateCanvases();
                if(!hud.VerifyLayout(out reason)) throw new Exception("Category layout invalid: "+category+" "+reason);
                foreach(var button in FindObjectsByType<Button>())
                {
                    if(!button.gameObject.activeInHierarchy)continue;
                    var rect=(RectTransform)button.transform;var corners=new Vector3[4];rect.GetWorldCorners(corners);
                    if(rect.rect.width<=0||rect.rect.height<=0||corners[0].x<-.5f||corners[0].y<-.5f||corners[2].x>Screen.width+.5f||corners[2].y>Screen.height+.5f)throw new Exception("Visible button bounds invalid: "+button.name);
                }
            }
            Click("Button_생산");
            AssertFirstHit("Button_Factory");
            Assert(true,"all four build categories and the factory action fit at "+Screen.width+"x"+Screen.height);
        }
        void VerifyResearchCatalog()
        {
            TechSpec[] specs=TechCatalog.All.ToArray();
            int enumTechnologyCount=Enum.GetValues(typeof(TechId)).Cast<TechId>().Count(id=>id!=TechId.None);
            Assert(specs.Length==enumTechnologyCount,"technology catalog covers every non-None technology ID dynamically");
            Assert(specs.Select(s=>s.Id).Distinct().Count()==specs.Length,"technology catalog contains no duplicate IDs");
            Assert(specs.All(spec=>FindButton("Button_연구 시작_"+spec.Id)!=null),"research tree renders one button for every catalog technology");
            ScrollRect[] scrolls=FindResearchScrolls();
            int eraCount=specs.Select(spec=>spec.Era).Distinct().Count();
            Assert(scrolls.Length==eraCount && scrolls.All(scroll=>scroll.content!=null && (scroll.viewport??scroll.GetComponent<RectTransform>())!=null),"research tree renders one scrollable viewport per catalog era");
            Canvas.ForceUpdateCanvases();
            Assert(scrolls.Any(scroll=>scroll.content.rect.height>(scroll.viewport??scroll.GetComponent<RectTransform>()).rect.height+.5f || scroll.content.rect.width>(scroll.viewport??scroll.GetComponent<RectTransform>()).rect.width+.5f),"expanded research tree content extends beyond its viewport");
        }
        void VerifyResearchScroll()
        {
            VerifyResearchCatalog();
            TechSpec last=TechCatalog.All.Last();
            ScrollRect lastScroll=FindResearchScroll(last.Id);
            Assert(lastScroll!=null,"last catalog technology belongs to a research viewport");
            if(lastScroll.vertical)lastScroll.verticalNormalizedPosition=0;
            if(lastScroll.horizontal)lastScroll.horizontalNormalizedPosition=1;
            Canvas.ForceUpdateCanvases();
            AssertFirstHit("Button_연구 시작_"+last.Id);
            ScrollRect firstScroll=FindResearchScroll(TechId.CropRotation);
            Assert(firstScroll!=null,"first catalog technology belongs to a research viewport");
            if(firstScroll.vertical)firstScroll.verticalNormalizedPosition=1;
            if(firstScroll.horizontal)firstScroll.horizontalNormalizedPosition=0;
            Canvas.ForceUpdateCanvases();
            AssertFirstHit("Button_연구 시작_CropRotation");
            Assert(true,"research scrolling reaches the first and last catalog technologies at "+Screen.width+"x"+Screen.height);
        }
        static Button FindButton(string name) => FindObjectsByType<Button>(FindObjectsInactive.Include).FirstOrDefault(button=>button.name==name);
        static ScrollRect FindResearchScroll(TechId id)
        {
            Button button=FindButton("Button_연구 시작_"+id);
            return button==null?null:button.GetComponentInParent<ScrollRect>();
        }
        static ScrollRect[] FindResearchScrolls()
        {
            Transform overlay=FindObjectsByType<Transform>(FindObjectsInactive.Include).FirstOrDefault(item=>item.name=="ResearchOverlay");
            return overlay==null?Array.Empty<ScrollRect>():FindObjectsByType<ScrollRect>(FindObjectsInactive.Include).Where(item=>item.transform.IsChildOf(overlay)).ToArray();
        }
        IEnumerator Capture(string name,string mustDifferFrom)
        {
            float deadline=Time.realtimeSinceStartup+5f;string lastReason="no frame captured";
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return new WaitForEndOfFrame();
                Texture2D texture=null;bool captured=false;
                try
                {
                    texture=ScreenCapture.CaptureScreenshotAsTexture();
                    if(VisibleFrame(texture,out lastReason))
                    {
                        byte[] png=texture.EncodeToPNG();ulong hash=Hash(png);
                        if(mustDifferFrom!=null&&captureHashes.TryGetValue(mustDifferFrom,out var prior)&&prior==hash) lastReason="frame is byte-identical to "+mustDifferFrom;
                        else {File.WriteAllBytes(Path.Combine(output,name),png);captureHashes[name]=hash;checks.Add("CAPTURE "+name+" "+Screen.width+"x"+Screen.height+" visible");captured=true;}
                    }
                }
                catch(Exception e) { lastReason=e.Message; }
                finally { if(texture!=null) Destroy(texture); }
                if(captured) yield break;
            }
            Fail("CAPTURE "+name+" failed within 5 seconds: "+lastReason);
        }
        static bool VisibleFrame(Texture2D texture,out string reason)
        {
            if(texture==null||texture.width<320||texture.height<180) {reason="missing or undersized texture";return false;}
            Color32[] pixels=texture.GetPixels32();int stride=Mathf.Max(1,pixels.Length/4096),samples=0,nonDark=0;
            var colors=new HashSet<int>();
            for(int i=0;i<pixels.Length;i+=stride) {var p=pixels[i];samples++;if(p.r+p.g+p.b>=24) nonDark++;colors.Add((p.r>>4)<<8|(p.g>>4)<<4|(p.b>>4));}
            if(nonDark<samples/20) {reason="fewer than 5% sampled pixels are visible";return false;}
            if(colors.Count<16) {reason="fewer than 16 quantized sampled colors";return false;}
            reason="";return true;
        }
        static ulong Hash(byte[] bytes) {unchecked{ulong value=1469598103934665603UL;foreach(byte b in bytes){value^=b;value*=1099511628211UL;}return value;}}
        static string DurableJson(GameState state)
        {
            var copy=JsonUtility.FromJson<GameState>(JsonUtility.ToJson(state));
            foreach(var cell in copy.Cells) {cell.Connected=false;cell.Status="";}
            return JsonUtility.ToJson(copy);
        }
        void Fail(string message) {errors.Add(message);Debug.Log("RIVERWORKS SMOKE FAILURE: "+message);}
        void VerifyActiveResearchSave()
        {
            GameState s=controller.State;
            TechId active=s.ActiveResearch;int remaining=s.ResearchDaysRemaining;float points=s.ResearchPoints;Era era=s.Era;
            string durable=DurableJson(s);controller.SaveGame();
            s.ActiveResearch=TechId.None;s.ResearchDaysRemaining=0;s.ResearchPoints=999;s.Era=Era.Industrial;
            controller.LoadGame();
            Assert(controller.State.ActiveResearch==active && controller.State.ResearchDaysRemaining==remaining,"save reload preserves active research and remaining days");
            Assert(Mathf.Approximately(controller.State.ResearchPoints,points) && controller.State.Era==era,"save reload preserves research points and era");
            Assert(DurableJson(controller.State)==durable,"native save reload preserves complete durable state");
        }
        void VerifyCompletedEraSave()
        {
            int technologyCount=TechCatalog.All.Count();
            string durable=DurableJson(controller.State);controller.SaveGame();
            controller.State.Technologies.Clear();controller.State.Era=Era.Medieval;controller.State.ResearchPoints=0;
            controller.LoadGame();
            Assert(controller.State.Era==Era.Industrial && controller.State.Technologies.Count==technologyCount && TechCatalog.All.All(t=>TechCatalog.Has(controller.State,t.Id)),"save reload preserves all "+technologyCount+" completed technologies and Industrial era");
            Assert(DurableJson(controller.State)==durable,"Industrial native save reload preserves all durable state");
        }
        void PrepareMedievalCitizens()
        {
            var s=controller.State;s.Coins=10000;s.Stock[(int)Resource.Timber]=1000;s.Stock[(int)Resource.Stone]=1000;s.Stock[(int)Resource.Grain]=1000;s.Stock[(int)Resource.Bread]=1000;
            s.Population=32;
            // Visual fixture: enough connected homes and medieval jobs for a busy street scene.
            Force(BuildingKind.House,8,11,1);Force(BuildingKind.House,12,11,1);
            Force(BuildingKind.StudyHouse,9,11,1);Force(BuildingKind.Lumberyard,11,11,1);
            controller.Sim.Recalculate();controller.RefreshWorld(true);controller.CameraRig.Home();controller.CameraRig.SetZoom(5.0f);controller.SetSpeed(1);controller.SelectTool(BuildingKind.None);
            controller.SetNotice("중세 주민들이 집과 일터 사이를 실제 도로로 이동합니다");
        }
        IEnumerator VerifyRenderedCitizens()
        {
            float deadline=Time.realtimeSinceStartup+8f;int id=-1;Vector3 first=default;
            while(Time.realtimeSinceStartup<deadline && id<0)
            {
                foreach(var resident in controller.Citizens.Simulation.Residents)
                    if(controller.Citizens.TryGetVisibleResident(resident.Id,out first)){id=resident.Id;break;}
                if(id<0)yield return null;
            }
            if(id<0){Fail("no rendered pedestrian became visible within 8 seconds");yield break;}
            bool moved=false;deadline=Time.realtimeSinceStartup+6f;Vector3 current=first;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;
                if(controller.Citizens.TryGetVisibleResident(id,out current) && Vector3.Distance(first,current)>.25f){moved=true;break;}
            }
            if(!moved){Fail("rendered pedestrian did not change world position");yield break;}
            Assert(true,"rendered pedestrian travels at least a quarter tile");
            controller.SetSpeed(0);Vector3 paused=current;yield return new WaitForSecondsRealtime(.45f);
            if(!controller.Citizens.TryGetVisibleResident(id,out current) || Vector3.Distance(paused,current)>=.0001f){Fail("pause did not freeze rendered pedestrian position");yield break;}
            Assert(true,"pause freezes rendered pedestrian position");
            bool selected=false;
            foreach(var resident in controller.Citizens.Simulation.Residents)
            {
                if(!controller.Citizens.TryGetVisibleResident(resident.Id,out var world))continue;
                Vector3 pixel=controller.CameraRig.Camera.WorldToScreenPoint(world+Vector3.up*.16f);
                if(pixel.z<=0 || pixel.x<1 || pixel.y<1 || pixel.x>=Screen.width-1 || pixel.y>=Screen.height-1 || controller.IsScreenPointOverUI(pixel))continue;
                if(controller.InteractScreenPoint(pixel)){selected=true;break;}
            }
            if(!selected || string.IsNullOrEmpty(controller.ResidentDetails)){Fail("screen pixel did not select a rendered NPC through the controller path");yield break;}
            Assert(true,"screen pixel selects a rendered NPC through the controller path");
            if(controller.Citizens.VisibleCount<=0 || controller.Citizens.VisibleCount>CitizenSimulation.StreetCapacity){Fail("visible pedestrian count is outside the street rendering budget");yield break;}
            Assert(true,"visible pedestrians respect the street rendering budget");
            controller.SetSpeed(1);
        }
        void ProgressTo(TechId target,bool completeCatalog=false)
        {
            // Screenshot fixture: progression still uses StartResearch and Tick in dependency order.
            var s=controller.State;s.Coins=100000;s.ResearchPoints=100000;s.Stock[(int)Resource.Timber]=10000;s.Stock[(int)Resource.Stone]=10000;s.Stock[(int)Resource.Grain]=10000;s.Stock[(int)Resource.Bread]=10000;
            TechSpec[] specs=TechCatalog.All.ToArray();
            var byId=specs.ToDictionary(spec=>spec.Id);
            if(!byId.ContainsKey(target))throw new Exception("Progression target is not in the technology catalog: "+target);
            var visiting=new HashSet<TechId>();
            ResearchWithPrerequisites(target);
            if(completeCatalog)foreach(TechSpec spec in specs)ResearchWithPrerequisites(spec.Id);
            controller.RefreshWorld(true);

            void ResearchWithPrerequisites(TechId id)
            {
                if(TechCatalog.Has(s,id))return;
                if(!byId.TryGetValue(id,out TechSpec spec))throw new Exception("Technology prerequisite is not in the catalog: "+id);
                if(!visiting.Add(id))throw new Exception("Technology prerequisite cycle includes "+id);
                foreach(TechId prerequisite in spec.Prerequisites??Array.Empty<TechId>())ResearchWithPrerequisites(prerequisite);
                visiting.Remove(id);
                if(!controller.Sim.StartResearch(id,out var why))throw new Exception("Legal research "+id+" failed: "+why);
                int guard=Math.Max(1,spec.DurationDays+1);
                while(s.ActiveResearch!=TechId.None && guard-->0)controller.Sim.Tick();
                if(!TechCatalog.Has(s,id))throw new Exception("Research did not complete: "+id);
            }
        }
        void PrepareEraShowcase(bool industrial)
        {
            var s=controller.State;
            s.Coins=100000;s.Stock[(int)Resource.Timber]=10000;s.Stock[(int)Resource.Stone]=10000;s.Stock[(int)Resource.Bread]=10000;s.Population=64;
            foreach(int id in new[]{1,3,5}) if(!s.OwnedRegions.Contains(id) && !controller.Sim.BuyRegion(id,out var why)) throw new Exception("Showcase region "+id+" failed: "+why);
            for(int z=4;z<=13;z++)
            {
                var c=controller.Sim.GetCell(10,z);if(c.Building==BuildingKind.None){c.Terrain=TerrainKind.Grass;BuildVisual(BuildingKind.Road,10,z);}
            }
            for(int x=4;x<=16;x++) { var c=controller.Sim.GetCell(x,10);if(c.Building==BuildingKind.None){c.Terrain=TerrainKind.Grass;BuildVisual(BuildingKind.Road,x,10);} }
            var kinds=industrial
                ?new[]{BuildingKind.Mill,BuildingKind.Bakery,BuildingKind.Market,BuildingKind.Warehouse,BuildingKind.Park,BuildingKind.Smelter,BuildingKind.Workshop,BuildingKind.Mine,BuildingKind.SteamPlant,BuildingKind.Academy}
                :new[]{BuildingKind.Mill,BuildingKind.Bakery,BuildingKind.Market,BuildingKind.Warehouse,BuildingKind.Park,BuildingKind.Academy};
            int[,] positions={{8,9},{12,9},{9,11},{11,11},{7,9},{6,9},{5,9},{4,9},{7,11},{6,11}};
            for(int i=0;i<kinds.Length;i++) {int x=positions[i,0],z=positions[i,1];var c=controller.Sim.GetCell(x,z);c.Building=BuildingKind.None;c.Level=0;c.Terrain=kinds[i]==BuildingKind.Mine||kinds[i]==BuildingKind.Quarry?TerrainKind.Rock:kinds[i]==BuildingKind.Lumberyard?TerrainKind.Forest:TerrainKind.Grass;controller.Sim.Recalculate();BuildVisual(kinds[i],x,z);}
            foreach(var p in new[]{new Vector2Int(9,5),new Vector2Int(11,5),new Vector2Int(9,6),new Vector2Int(11,6),new Vector2Int(9,7),new Vector2Int(11,7),new Vector2Int(14,9),new Vector2Int(15,9)}){var c=controller.Sim.GetCell(p.x,p.y);c.Terrain=TerrainKind.Grass;if(c.Building==BuildingKind.None)BuildVisual(BuildingKind.House,p.x,p.y);if(c.Level==1&&!controller.Sim.Upgrade(p.x,p.y,out var why))throw new Exception("Showcase house upgrade failed: "+why);}
            if(industrial)Assert(new[]{BuildingKind.Mine,BuildingKind.Smelter,BuildingKind.Workshop,BuildingKind.SteamPlant}.All(kind=>s.Cells.Any(c=>c.Building==kind)),"industrial showcase contains the complete heavy-industry chain and steam power");
            controller.RefreshWorld(true);controller.CameraRig.Home();controller.SetSpeed(1);controller.SelectTool(BuildingKind.None);controller.SetNotice("산업과 일상이 함께 자라는 도시 · 확장된 도시의 시각 검증 장면");
            void BuildVisual(BuildingKind kind,int x,int z){if(!controller.Sim.Build(kind,x,z,out var why))throw new Exception("Showcase "+kind+" at "+x+","+z+" failed: "+why);}
        }
        void Force(BuildingKind kind,int x,int z,int level){var c=controller.Sim.GetCell(x,z);c.Terrain=TerrainKind.Grass;c.Building=kind;c.Level=level;}
        IEnumerator RecordWalkingPreview()
        {
            string directory=Path.Combine(output,"walking-frames");Directory.CreateDirectory(directory);
            for(int i=0;i<24;i++)
            {
                yield return new WaitForEndOfFrame();
                var frame=ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(directory,"frame-"+i.ToString("D3")+".png"),frame.EncodeToPNG());Destroy(frame);
                yield return new WaitForSecondsRealtime(.10f);
            }
            checks.Add("CAPTURE 24 walking preview frames from the running game");
        }
        void Complete()
        {
            if(completed)return;completed=true;
            checks.Insert(0,"RUN UTC "+DateTime.UtcNow.ToString("O")+" PLATFORM "+Application.platform+" BUILD "+Application.buildGUID+" UNITY "+Application.unityVersion);
#if UNITY_STANDALONE || UNITY_EDITOR
            try {var exe=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;checks.Insert(1,"EXECUTABLE "+exe+" UTC "+File.GetLastWriteTimeUtc(exe).ToString("O"));} catch(Exception e) {checks.Insert(1,"EXECUTABLE metadata unavailable: "+e.Message);}
#endif
            File.WriteAllLines(Path.Combine(output,"runtime-results.txt"),checks.Concat(new[]{"ERRORS "+errors.Count}).Concat(errors));
            Debug.Log(errors.Count==0?"RIVERWORKS_RUNTIME_SMOKE_PASS":"RIVERWORKS_RUNTIME_SMOKE_FAILED");
            Application.Quit(errors.Count==0?0:1);
        }
    }
}

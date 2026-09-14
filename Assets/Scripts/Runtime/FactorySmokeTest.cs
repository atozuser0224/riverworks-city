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
    public sealed class FactorySmokeTest:MonoBehaviour
    {
        GameController game;FactoryController factory;string output;bool done;
        readonly List<string> results=new List<string>(),errors=new List<string>();
        public void Initialize(GameController controller)
        {
            game=controller;factory=game.Factory;
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-riverworks-output");
            output=at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.persistentDataPath,"FactorySmoke");
            Directory.CreateDirectory(output);Application.logMessageReceived+=OnLog;StartCoroutine(Guarded());
        }
        void OnLog(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
        void OnDestroy(){Application.logMessageReceived-=OnLog;}
        void Check(bool okay,string message){if(!okay)throw new Exception(message);results.Add("PASS "+message);Debug.Log("FACTORY PASS: "+message);}
        IEnumerator Guarded()
        {
            var run=Run();while(true){object current;try{if(!run.MoveNext())break;current=run.Current;}catch(Exception ex){errors.Add(ex.ToString());Finish();yield break;}yield return current;}
            Finish();
        }
        IEnumerator Run()
        {
            game.SetSpeed(0);Screen.SetResolution(1280,720,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
            Screen.SetResolution(1600,900,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();
            Click("Button_Factory");yield return new WaitForEndOfFrame();
            Check(factory.IsOpen&&!factory.IsPractice,"city button opens the real factory");
            Check(EventSystem.current!=null&&EventSystem.current.gameObject.activeInHierarchy,"factory mode retains its active EventSystem");
            Check(!game.Board.gameObject.activeSelf&&factory.View.Camera.isActiveAndEnabled,"factory and city cameras switch");
            float originalCoins=game.State.Coins;factory.SelectTool(FactoryKind.Drill);factory.PlaceAt(1,7);
            Check(factory.State.Entities.Count==0&&game.State.Coins==originalCoins,"medieval tech lock does not spend resources");
            FactoryState realReference=game.State.Factory;string cityBefore=JsonUtility.ToJson(game.State);
            Click("FactoryButton_Practice");yield return new WaitForEndOfFrame();
            Check(factory.IsPractice&&factory.PracticeBlueprintIndex==0&&factory.State.Entities.Count>20,"practice opens the selected complete blueprint");
            Check(FactoryBlueprints.Count>1&&!string.IsNullOrWhiteSpace(FactoryBlueprints.Name(0))&&!string.IsNullOrWhiteSpace(FactoryBlueprints.Description(0)),"practice blueprint catalog is available to the chooser");
            FactoryState toolsPractice=factory.State;
            TickPracticeUntil(()=>factory.State.Exported[(int)Resource.Tools]>0,8000);
            factory.View.Refresh(false);
            Check(factory.State.Produced[(int)Resource.Tools]>0&&factory.State.Exported[(int)Resource.Tools]>0,"real conveyor example crafts and exports tools");
            Check(JsonUtility.ToJson(game.State)==cityBefore,"practice cannot mutate city resources or real layout");
            for(int i=0;i<200&&factory.Sim.MovingItems==0;i++)factory.Sim.Tick(.1f);
            Check(factory.Sim.MovingItems>0,"actual in-flight cargo exists");
            int made=factory.State.Produced[(int)Resource.Tools];factory.State.PowerBudget=0;
            for(int i=0;i<100;i++)factory.Sim.Tick(.1f);
            Check(factory.State.Produced[(int)Resource.Tools]==made,"power failure stops machine production");
            factory.State.PowerBudget=20;for(int i=0;i<200;i++){factory.Sim.Tick(.1f);factory.Sim.TakeExports(Resource.Tools);}
            Check(factory.State.Produced[(int)Resource.Tools]>made,"restored power resumes production");
            VerifyCameraBounds();
            Click("FactoryButton_Belt");yield return new WaitForEndOfFrame();
            var pixel=factory.View.Camera.WorldToScreenPoint(factory.View.WorldPosition(5,12));
            RaycastHit floorHit;
            Check(pixel.z>0&&!factory.IsScreenPointOverUI(pixel)&&Physics.Raycast(factory.View.Camera.ScreenPointToRay(pixel),out floorHit,200,1<<10)&&floorHit.collider.GetComponent<FactoryTileHandle>()?.X==5&&floorHit.collider.GetComponent<FactoryTileHandle>()?.Z==12,"factory floor is reached by the shared camera and physics raycast");
            Check(factory.InteractScreenPoint(pixel),"screen pointer routes through factory interaction");
            Check(factory.Sim.GetAt(5,12)?.Kind==FactoryKind.Belt,"screen picking places the selected conveyor");
            int direction=factory.Direction;Click("FactoryButton_Rotate");Check(factory.Direction==(direction+1)%4,"rotate button changes placement direction");
            factory.SelectTool(FactoryKind.None);factory.PlaceAt(15,6);
            factory.ChooseRecipe(FactoryRecipe.Flour);Check(factory.SelectedEntity.Recipe==FactoryRecipe.Flour,"assembler recipe can be selected");
            factory.ChooseRecipe(FactoryRecipe.Tools);factory.PlaceAt(14,7);factory.ChooseFilter(Resource.Steel);
            Check(factory.SelectedEntity.Filter==Resource.Steel,"inserter filter configuration persists");
            string toolsPracticeJson=JsonUtility.ToJson(factory.State);
            Click("FactoryButton_BlueprintNext");yield return new WaitForEndOfFrame();
            Check(factory.PracticeBlueprintIndex==1&&!ReferenceEquals(factory.State,toolsPractice),"blueprint chooser switches to a separate bread practice");
            TickPracticeUntil(()=>factory.State.Exported[(int)Resource.Bread]>0,8000);
            Check(factory.State.Produced[(int)Resource.Flour]>0&&factory.State.Produced[(int)Resource.Bread]>0&&factory.State.Exported[(int)Resource.Bread]>0,"bread blueprint mills, bakes and exports through the real simulation");
            Click("FactoryButton_BlueprintNext");yield return new WaitForEndOfFrame();
            Check(factory.PracticeBlueprintIndex==2,"blueprint chooser switches to the splitter practice");
            var stores=factory.State.Entities.Where(e=>e.Kind==FactoryKind.Storage).OrderBy(e=>e.Id).ToList();
            for(int i=0;i<8000&&stores.Sum(e=>e.Input[(int)Resource.Grain])<12;i++)factory.Sim.Tick(.1f);
            Check(stores.Count==2&&stores.Sum(e=>e.Input[(int)Resource.Grain])>=12&&Math.Abs(stores[0].Input[(int)Resource.Grain]-stores[1].Input[(int)Resource.Grain])<=1,"splitter blueprint balances actual cargo between both stores");
            Click("FactoryButton_BlueprintNext");yield return new WaitForEndOfFrame();
            Check(factory.PracticeBlueprintIndex==0&&ReferenceEquals(factory.State,toolsPractice)&&JsonUtility.ToJson(factory.State)==toolsPracticeJson,"returning to a practice blueprint preserves its edits and factory data");
            Check(ReferenceEquals(game.State.Factory,realReference)&&JsonUtility.ToJson(game.State)==cityBefore,"switching practice blueprints leaves the real city and factory untouched");
            factory.SelectTool(FactoryKind.None);factory.PlaceAt(15,6);factory.SetNotice("광석과 목재가 실제 벨트·투입기를 거쳐 도구로 가공됩니다");
            yield return new WaitForSecondsRealtime(.3f);yield return Capture("01-factory-design-1600.png");
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();
            ValidateButtons();yield return Capture("02-factory-design-1280.png");
            Screen.SetResolution(1600,900,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
            game.SetSpeed(1);yield return Record();game.SetSpeed(0);

            // Integration fixture grants technology and layout, but materials enter through the real city API.
            var allTechnologies=TechCatalog.All.Select(t=>t.Id).Distinct().ToList();game.State.Technologies=allTechnologies;game.State.Era=Era.Industrial;
            Check(game.State.Technologies.Count==TechCatalog.All.Count()&&game.State.Technologies.All(id=>TechCatalog.Has(game.State,id)),"integration fixture grants every technology in the dynamic catalog");
            var wind=game.Sim.GetCell(11,8);wind.Building=BuildingKind.Windmill;wind.Level=3;game.Sim.Recalculate();
            game.State.Factory=FactoryBlueprints.Create(0);game.State.Factory.Entities.First(e=>e.Kind==FactoryKind.ImportDock).Input[(int)Resource.Timber]=0;
            Click("FactoryButton_Real");yield return new WaitForEndOfFrame();
            factory.SelectTool(FactoryKind.None);factory.PlaceAt(15,0);
            float timberBefore=game.Sim.Get(Resource.Timber);factory.Feed(Resource.Timber,10);
            Check(game.Sim.Get(Resource.Timber)==timberBefore-10&&factory.SelectedEntity.Input[(int)Resource.Timber]==10,"real feed transfers city timber exactly once");
            Check(game.SmokeMode&&string.Equals(Path.GetFileName(game.SavePath),"smoke-test.json",StringComparison.Ordinal),"save verification uses the isolated smoke slot");
            VerifySaveFailurePropagation();
            Check(game.TrySaveGame(out var saveError),"real factory save succeeds after restoring valid state");
            string factoryJson=JsonUtility.ToJson(game.State.Factory);factory.SelectedEntity.Input[(int)Resource.Timber]=0;
            game.LoadGame();yield return null;factory.OpenReal();
            Check(JsonUtility.ToJson(game.State.Factory)==factoryJson,"factory layout, buffers and cargo round-trip in city save");
            float toolsBefore=game.Sim.Get(Resource.Tools);game.SetSpeed(3);
            float deadline=Time.realtimeSinceStartup+24f;
            while(game.Sim.Get(Resource.Tools)<=toolsBefore&&Time.realtimeSinceStartup<deadline)yield return null;
            game.SetSpeed(0);
            Check(game.Sim.Get(Resource.Tools)>toolsBefore&&factory.State.Exported[(int)Resource.Tools]>0,"real factory exports are credited to the city");
            factory.SelectTool(FactoryKind.None);factory.PlaceAt(22,7);factory.SetNotice("실제 공장 완제품이 도시 재고로 반출되었습니다");
            yield return new WaitForSecondsRealtime(.3f);yield return Capture("03-real-city-export.png");
            Click("FactoryButton_Close");yield return null;
            Check(!factory.IsOpen&&game.CameraRig.Camera.isActiveAndEnabled&&game.Board.gameObject.activeSelf,"close returns to the living city");
            Check(errors.Count==0,"no runtime errors in factory mode");
        }
        void TickPracticeUntil(Func<bool> complete,int maximumTicks)
        {
            var resources=(Resource[])Enum.GetValues(typeof(Resource));
            for(int i=0;i<maximumTicks&&!complete();i++)
            {
                factory.Sim.Tick(.1f);
                foreach(var resource in resources)if(resource!=Resource.Coins)factory.Sim.TakeExports(resource);
            }
        }
        void VerifyCameraBounds()
        {
            var view=factory.View;view.HomeCamera();
            float minX=Mathf.Min(view.WorldPosition(0,0).x,view.WorldPosition(23,0).x)-2f,maxX=Mathf.Max(view.WorldPosition(0,0).x,view.WorldPosition(23,0).x)+2f;
            float minZ=Mathf.Min(view.WorldPosition(0,0).z,view.WorldPosition(0,15).z)-2f,maxZ=Mathf.Max(view.WorldPosition(0,0).z,view.WorldPosition(0,15).z)+2f;
            view.PanScreen(new Vector2(100000f,100000f));Vector3 low=view.CameraFocus;
            view.PanScreen(new Vector2(-100000f,-100000f));Vector3 high=view.CameraFocus;
            bool finite=Finite(low.x)&&Finite(low.z)&&Finite(high.x)&&Finite(high.z);
            Check(finite&&Mathf.Abs(low.x-minX)<.01f&&Mathf.Abs(low.z-minZ)<.01f&&Mathf.Abs(high.x-maxX)<.01f&&Mathf.Abs(high.z-maxZ)<.01f,"extreme mobile camera pans clamp to both factory bounds");
            view.HomeCamera();Vector3 home=view.CameraFocus,expected=view.WorldPosition(11,7);
            Check(Mathf.Abs(home.x-expected.x)<.01f&&Mathf.Abs(home.z-expected.z)<.01f,"factory camera home restores the visible grid center");
        }
        void VerifySaveFailurePropagation()
        {
            int coinIndex=(int)Resource.Coins;float validMirror=game.State.Stock[coinIndex];
            bool rejected=false,propagated=false;string expectedError="";
            try
            {
                game.State.Stock[coinIndex]=game.State.Coins+1f;
                rejected=!game.TrySaveGame(out expectedError)&&!string.IsNullOrWhiteSpace(expectedError);
                factory.Save();
                propagated=!string.IsNullOrWhiteSpace(expectedError)&&factory.Notice.Contains(expectedError)&&factory.Notice.Contains("실패");
            }
            finally { game.State.Stock[coinIndex]=validMirror; }
            Check(rejected,"invalid real state is rejected before writing the smoke save");
            Check(propagated,"factory save reports the real persistence failure instead of success");
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static Button ButtonNamed(string name)=>FindObjectsByType<Button>(FindObjectsInactive.Include).FirstOrDefault(b=>b.name==name);
        static void Click(string name)
        {
            Canvas.ForceUpdateCanvases();var button=ButtonNamed(name);if(button==null||!button.gameObject.activeInHierarchy||!button.interactable)throw new Exception("Button unavailable: "+name);
            var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);var point=(corners[0]+corners[2])*.5f;
            var pointer=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            if(hits.Count==0||!(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)))throw new Exception("Button obstructed: "+name+" by "+(hits.Count>0?hits[0].gameObject.name:"no raycast"));
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        void ValidateButtons()
        {
            Canvas.ForceUpdateCanvases();foreach(var button in FindObjectsByType<Button>())
            {
                var rect=(RectTransform)button.transform;var c=new Vector3[4];rect.GetWorldCorners(c);
                if(rect.rect.width<=0||rect.rect.height<=0||c[0].x<-.5f||c[0].y<-.5f||c[2].x>Screen.width+.5f||c[2].y>Screen.height+.5f)throw new Exception("Factory button outside screen: "+button.name);
            }
            Check(true,"factory controls fit 1280x720");
        }
        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();var texture=ScreenCapture.CaptureScreenshotAsTexture();
            if(CameraRegionVisible(texture))results.Add("PASS visible factory camera pixels in "+name);
            else errors.Add("Factory camera capture is black or blank: "+name);
            File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());Destroy(texture);results.Add("CAPTURE "+name);
        }
        IEnumerator Record()
        {
            string directory=Path.Combine(output,"frames");Directory.CreateDirectory(directory);
            bool visible=true;
            for(int i=0;i<24;i++){yield return new WaitForEndOfFrame();var frame=ScreenCapture.CaptureScreenshotAsTexture();visible&=CameraRegionVisible(frame);File.WriteAllBytes(Path.Combine(directory,"frame-"+i.ToString("D3")+".png"),frame.EncodeToPNG());Destroy(frame);yield return new WaitForSecondsRealtime(.1f);}
            if(visible)results.Add("PASS visible factory camera pixels in all conveyor motion frames");
            else errors.Add("One or more conveyor motion frames have a black or blank factory camera region");
            results.Add("CAPTURE 24 actual conveyor motion frames");
        }
        bool CameraRegionVisible(Texture2D texture)
        {
            if(texture==null||factory?.View?.Camera==null)return false;
            Rect rect=factory.View.Camera.pixelRect;
            int left=Mathf.Clamp(Mathf.FloorToInt(rect.xMin),0,texture.width-1),right=Mathf.Clamp(Mathf.CeilToInt(rect.xMax),left+1,texture.width);
            int bottom=Mathf.Clamp(Mathf.FloorToInt(rect.yMin),0,texture.height-1),top=Mathf.Clamp(Mathf.CeilToInt(rect.yMax),bottom+1,texture.height);
            int stepX=Mathf.Max(1,(right-left)/32),stepY=Mathf.Max(1,(top-bottom)/20),minimum=255,maximum=0,visible=0,samples=0;
            for(int y=bottom;y<top;y+=stepY)for(int x=left;x<right;x+=stepX)
            {
                Color32 pixel=texture.GetPixel(x,y);int light=(pixel.r+pixel.g+pixel.b)/3;
                minimum=Math.Min(minimum,light);maximum=Math.Max(maximum,light);if(light>10)visible++;samples++;
            }
            return samples>0&&visible*20>=samples&&maximum-minimum>=6;
        }
        void Finish()
        {
            if(done)return;done=true;
            int exitCode=errors.Count==0?0:1;
            results.Insert(0,"RUN "+DateTime.UtcNow.ToString("O")+" BUILD "+Application.buildGUID);
            File.WriteAllLines(Path.Combine(output,"factory-results.txt"),results.Concat(new[]{"ERRORS "+errors.Count}).Concat(errors).Concat(new[]{"EXIT "+exitCode}));
            Debug.Log(exitCode==0?"FACTORY_RUNTIME_PASS":"FACTORY_RUNTIME_FAIL");Application.Quit(exitCode);
        }
    }
}

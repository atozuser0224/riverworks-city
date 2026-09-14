using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riverworks
{
    public sealed class TileHandle : MonoBehaviour { public int X,Z; }

    public sealed class BoardView : MonoBehaviour
    {
        public const float Spacing=1.06f;
        GameController controller;
        Transform tilesRoot, modelsRoot, trafficRoot;
        readonly Dictionary<int,GameObject> models=new Dictionary<int,GameObject>();
        readonly Dictionary<int,Transform> buildingTransforms=new Dictionary<int,Transform>();
        readonly int[] signatures=new int[441];
        readonly Material[] tileMaterials=new Material[441];
        readonly List<LineRenderer> boundaries=new List<LineRenderer>();
        readonly List<Truck> trucks=new List<Truck>();
        readonly Queue<GameObject> vanPool=new Queue<GameObject>();
        readonly Dictionary<int,List<Vector3>> routeCache=new Dictionary<int,List<Vector3>>();
        readonly List<Material> ownedMaterials=new List<Material>();
        MeshRenderer[] tiles;
        Material grass, forest, rock, water, lockedGrass, lockedForest, lockedRock, lockedWater, asphalt, curb, stripe, baseMat, validMat, invalidMat, mint, red, gold, dirt, cobble, bridgeWood;
        Era renderedEra;
        LineRenderer highlight;
        Transform ghost;
        MeshRenderer ghostRenderer;
        readonly Vector3[] highlightPoints=new Vector3[5];
        int lastHighlightIndex=int.MinValue;
        bool lastHighlightValid, lastHighlightGhost;
        float dispatchClock;
        int nextSource;
        readonly int[] dx={1,-1,0,0}, dz={0,0,1,-1};
        public static Vector3 Position(int x,int z) => new Vector3((x-10)*Spacing,0,(z-10)*Spacing);
        public Transform ModelAt(int x,int z)
        {
            return x>=0&&x<21&&z>=0&&z<21&&models.TryGetValue(z*21+x,out var model)&&model!=null ? model.transform : null;
        }
        public bool TryGetBuildingTransform(int index,out Transform building)
        {
            if(index>=0&&index<signatures.Length&&buildingTransforms.TryGetValue(index,out building)&&building!=null) return true;
            building=null; return false;
        }

        public void Initialize(GameController game)
        {
            controller=game;
            for(int i=0;i<signatures.Length;i++) signatures[i]=int.MinValue;
            grass=Mat("Meadow",new Color(.46f,.61f,.40f)); forest=Mat("Forest floor",new Color(.42f,.58f,.43f)); rock=Mat("Stone field",new Color(.68f,.69f,.61f)); water=Mat("River",new Color(.27f,.58f,.60f),.68f);
            lockedGrass=Mat("Undeveloped meadow",new Color(.60f,.71f,.65f)); lockedForest=Mat("Undeveloped forest",new Color(.47f,.63f,.56f)); lockedRock=Mat("Undeveloped rocks",new Color(.66f,.71f,.68f)); lockedWater=Mat("Distant river",new Color(.35f,.63f,.67f),.6f);
            asphalt=Mat("Slate road",new Color(.29f,.37f,.40f)); curb=Mat("Limestone pavement",new Color(.83f,.82f,.70f)); stripe=Mat("Road markings",new Color(.93f,.85f,.59f)); baseMat=Mat("Board edge",new Color(.25f,.36f,.37f));
            dirt=Mat("Medieval earth",new Color(.49f,.37f,.24f)); cobble=Mat("Renaissance cobbles",new Color(.53f,.52f,.45f)); bridgeWood=Mat("Timber bridge",new Color(.38f,.23f,.12f));
            mint=Mat("Delivery teal",new Color(.11f,.59f,.52f)); red=Mat("Delivery coral",new Color(.95f,.35f,.23f)); gold=Mat("Brass",new Color(.88f,.67f,.32f));
            validMat=Mat("Valid outline",new Color(.17f,.98f,.77f),0,true); invalidMat=Mat("Invalid outline",new Color(1,.32f,.26f),0,true);
            tilesRoot=new GameObject("Land tiles").transform; tilesRoot.SetParent(transform,false);
            modelsRoot=new GameObject("Buildings and nature").transform; modelsRoot.SetParent(transform,false);
            trafficRoot=new GameObject("Supply vehicles").transform; trafficRoot.SetParent(transform,false);
            var tableMat=Mat("Backdrop",new Color(.74f,.82f,.82f));
            Box(transform,"Table",new Vector3(200,.3f,200),new Vector3(0,-.98f,0),tableMat);
            Box(transform,"Board foundation",new Vector3(22.65f,.64f,22.65f),new Vector3(0,-.55f,0),baseMat);
            Box(transform,"Board rim",new Vector3(22.72f,.06f,22.72f),new Vector3(0,-.24f,0),curb);
            tiles=new MeshRenderer[441];
            for(int z=0;z<21;z++) for(int x=0;x<21;x++)
            {
                var tile=Box(tilesRoot,$"Plot {x} {z}",new Vector3(1.035f,.20f,1.035f),Position(x,z)-Vector3.up*.12f,grass,true);
                tile.layer=8; var handle=tile.AddComponent<TileHandle>(); handle.X=x; handle.Z=z;
                tiles[z*21+x]=tile.GetComponent<MeshRenderer>();
            }
            for(int id=0;id<9;id++)
            {
                float cx=(id%3-1)*7*Spacing, cz=(id/3-1)*7*Spacing, half=3.5f*Spacing;
                var line=Line("District "+id,transform,.037f,validMat);
                line.positionCount=5;
                line.SetPositions(new[]{new Vector3(cx-half,.001f,cz-half),new Vector3(cx-half,.001f,cz+half),new Vector3(cx+half,.001f,cz+half),new Vector3(cx+half,.001f,cz-half),new Vector3(cx-half,.001f,cz-half)});
                boundaries.Add(line);
            }
            highlight=Line("Selected plot",transform,.05f,validMat); highlight.positionCount=5; highlight.enabled=false;
            ghost=Box(transform,"Build preview pad",new Vector3(.8f,.014f,.8f),Vector3.zero,validMat).transform; ghostRenderer=ghost.GetComponent<MeshRenderer>(); ghost.gameObject.SetActive(false);
            ConfigureLight(); Refresh(true);
        }

        void ConfigureLight()
        {
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.76f,.82f,.86f);
            RenderSettings.ambientEquatorColor=new Color(.56f,.65f,.66f);
            RenderSettings.ambientGroundColor=new Color(.38f,.42f,.39f);
            RenderSettings.fog=false;
            var sun=new GameObject("Warm afternoon sun").AddComponent<Light>(); sun.transform.SetParent(transform); sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(48,-35,0); sun.intensity=1.1f; sun.color=new Color(1,.91f,.76f); sun.shadows=LightShadows.Soft; sun.shadowBias=.04f; sun.shadowNormalBias=.18f;
            RenderSettings.sun=sun;
            QualitySettings.shadows=ShadowQuality.All; QualitySettings.shadowResolution=Application.isMobilePlatform?ShadowResolution.Medium:ShadowResolution.High; QualitySettings.shadowDistance=Application.isMobilePlatform?36:80; QualitySettings.antiAliasing=Application.isMobilePlatform?2:4;
        }

        public void Refresh(bool changed)
        {
            bool topologyChanged=renderedEra!=controller.State.Era;
            renderedEra=controller.State.Era;
            for(int i=0;i<441;i++)
            {
                var cell=controller.State.Cells[i]; bool owned=controller.Sim.IsOwned(cell.X,cell.Z);
                var terrainMaterial=TerrainMaterial(cell.Terrain,owned);
                if(tileMaterials[i]!=terrainMaterial) { tileMaterials[i]=terrainMaterial; tiles[i].sharedMaterial=terrainMaterial; }
                int roadMask=RoadMask(cell.X,cell.Z);
                int signature=(int)cell.Building | (cell.Level<<5) | ((int)cell.Terrain<<8) | (owned?1<<11:0) | (roadMask<<12) | (cell.Building!=BuildingKind.None?(int)renderedEra<<16:0);
                int old=signatures[i];
                if(old==signature) continue;
                if(old!=int.MinValue && ((old^(signature))&31)!=0 && (IsRoadKind(old&31)||IsRoadKind((int)cell.Building))) topologyChanged=true;
                signatures[i]=signature;
                if(models.TryGetValue(i,out var previous)) { buildingTransforms.Remove(i); previous.SetActive(false); Destroy(previous); }
                var root=new GameObject("Details "+cell.X+" "+cell.Z); root.transform.SetParent(modelsRoot,false); root.transform.localPosition=Position(cell.X,cell.Z); models[i]=root;
                if(cell.Building==BuildingKind.Road) CreateRoad(root.transform,cell);
                else if(cell.Building!=BuildingKind.None)
                {
                    Box(root.transform,"Building lot",new Vector3(.96f,.055f,.96f),new Vector3(0,.012f,0),renderedEra==Era.Medieval?dirt:curb);
                    var building=BuildingVisuals.Create(cell.Building,root.transform,cell.Level,renderedEra); building.transform.localPosition=Vector3.up*.04f; buildingTransforms[i]=building.transform;
                    // Face the nearest connected road, keeping the front entrance readable.
                    if(IsRoad(cell.X,cell.Z+1)) building.transform.localRotation=Quaternion.Euler(0,180,0);
                    else if(IsRoad(cell.X-1,cell.Z)) building.transform.localRotation=Quaternion.Euler(0,90,0);
                    else if(IsRoad(cell.X+1,cell.Z)) building.transform.localRotation=Quaternion.Euler(0,-90,0);
                }
                else if(cell.Terrain==TerrainKind.Forest)
                {
                    var tree=BuildingVisuals.CreateTree(root.transform,i); tree.transform.localScale=Vector3.one*(owned?1.25f:1.1f); tree.transform.localPosition=new Vector3(-.18f,0,-.13f);
                    var tree2=BuildingVisuals.CreateTree(root.transform,i+42); tree2.transform.localScale=Vector3.one*.9f; tree2.transform.localPosition=new Vector3(.23f,0,.23f);
                }
                else if(cell.Terrain==TerrainKind.Rock) BuildingVisuals.CreateRock(root.transform,i);
                else if(cell.Terrain==TerrainKind.Water)
                {
                    for(int k=0;k<2;k++) Box(root.transform,"Water glint",new Vector3(.2f+k*.1f,.008f,.018f),new Vector3(-.18f+k*.24f,-.009f,-.25f+k*.35f),lockedWater);
                }
                else if(!owned && (i*37)%13==0)
                {
                    var tree=BuildingVisuals.CreateTree(root.transform,i); tree.transform.localScale=Vector3.one*.8f;
                }
            }
            for(int id=0;id<9;id++)
            {
                bool owned=controller.State.OwnedRegions.Contains(id);
                boundaries[id].sharedMaterial=owned?stripe:curb;
                boundaries[id].widthMultiplier=owned?.065f:.018f;
            }
            if(topologyChanged) { routeCache.Clear(); ClearTraffic(); }
        }

        Material TerrainMaterial(TerrainKind kind,bool owned)
        {
            if(kind==TerrainKind.Water) return owned?water:lockedWater;
            if(kind==TerrainKind.Rock) return owned?rock:lockedRock;
            if(kind==TerrainKind.Forest) return owned?forest:lockedForest;
            return owned?grass:lockedGrass;
        }
        bool IsRoad(int x,int z) { var c=controller.Sim.GetCell(x,z); return c!=null&&(c.Building==BuildingKind.Road||c.Building==BuildingKind.TownHall); }
        static bool IsRoadKind(int kind) => kind==(int)BuildingKind.Road || kind==(int)BuildingKind.TownHall;
        int RoadMask(int x,int z) { int mask=0; for(int d=0;d<4;d++) if(IsRoad(x+dx[d],z+dz[d])) mask|=1<<d; return mask; }
        void CreateRoad(Transform parent,Cell cell)
        {
            bool bridge=cell.Terrain==TerrainKind.Water;
            bool industrial=renderedEra==Era.Industrial;
            Material surface=industrial?asphalt:(bridge?bridgeWood:(renderedEra==Era.Medieval?dirt:cobble));
            Box(parent,bridge?"Bridge deck":"Road",new Vector3(1.04f,bridge?.14f:.055f,1.04f),new Vector3(0,bridge?.025f:.006f,0),surface);
            if(!industrial)
            {
                for(int k=0;k<3;k++)Box(parent,bridge?"Deck plank":"Paving detail",new Vector3(.84f,.008f,.027f),new Vector3(0,bridge?.104f:.039f,(k-1)*.29f),bridge?dirt:(renderedEra==Era.Medieval?bridgeWood:curb));
            }
            for(int d=0;d<4;d++)
            {
                bool connected=IsRoad(cell.X+dx[d],cell.Z+dz[d]);
                if(connected && industrial)
                {
                    var size=dx[d]!=0?new Vector3(.13f,.007f,.025f):new Vector3(.025f,.007f,.13f);
                    Box(parent,"Lane dash",size,new Vector3(dx[d]*.34f,bridge?.103f:.04f,dz[d]*.34f),stripe);
                }
                else if(!connected)
                {
                    var size=dx[d]!=0?new Vector3(.08f,bridge?.22f:.09f,1.04f):new Vector3(1.04f,bridge?.22f:.09f,.08f);
                    Box(parent,bridge?"Bridge railing":"Sidewalk",size,new Vector3(dx[d]*.48f,bridge?.15f:.046f,dz[d]*.48f),renderedEra==Era.Medieval?bridgeWood:curb);
                }
            }
        }

        public void Highlight(Cell cell,bool valid)
        {
            bool active=cell!=null;
            int index=active?cell.Z*21+cell.X:-1;
            bool showGhost=active&&controller.SelectedTool!=BuildingKind.None;
            if(index==lastHighlightIndex && valid==lastHighlightValid && showGhost==lastHighlightGhost) return;
            lastHighlightIndex=index; lastHighlightValid=valid; lastHighlightGhost=showGhost;
            highlight.enabled=active; ghost.gameObject.SetActive(active&&controller.SelectedTool!=BuildingKind.None);
            if(!active) return;
            var p=Position(cell.X,cell.Z)+Vector3.up*.10f;
            highlight.sharedMaterial=valid?validMat:invalidMat;
            highlightPoints[0]=p+new Vector3(-.5f,0,-.5f); highlightPoints[1]=p+new Vector3(-.5f,0,.5f); highlightPoints[2]=p+new Vector3(.5f,0,.5f); highlightPoints[3]=p+new Vector3(.5f,0,-.5f); highlightPoints[4]=highlightPoints[0];
            highlight.SetPositions(highlightPoints);
            ghost.position=p-Vector3.up*.035f; ghostRenderer.sharedMaterial=valid?validMat:invalidMat;
            ghost.localScale=new Vector3(.23f,.014f,.23f);
        }

        void Update()
        {
            if(controller==null || controller.HelpOpen || controller.ModalOpen || controller.ResearchOpen) return;
            float dt=Time.deltaTime*controller.GameSpeed;
            dispatchClock+=dt;
            if(dispatchClock>1.1f) { dispatchClock=0; Dispatch(); }
            for(int i=trucks.Count-1;i>=0;i--)
            {
                var t=trucks[i]; if(t.Root==null) { trucks.RemoveAt(i); continue; }
                if(t.Step>=t.Route.Count) { RecycleVan(t.Root); trucks.RemoveAt(i); continue; }
                Vector3 destination=t.Route[t.Step]+Vector3.up*.11f;
                var direction=destination-t.Root.transform.position;
                if(direction.sqrMagnitude<.003f) { t.Step++; continue; }
                t.Root.transform.rotation=Quaternion.Slerp(t.Root.transform.rotation,Quaternion.LookRotation(direction),dt*12);
                t.Root.transform.position=Vector3.MoveTowards(t.Root.transform.position,destination,dt*.75f);
            }
        }
        void Dispatch()
        {
            if(controller.State.Era!=Era.Industrial) return;
            if(trucks.Count>=12) return;
            for(int n=0;n<441;n++)
            {
                int index=(nextSource+n)%441; Cell cell=controller.State.Cells[index];
                if(!cell.Connected||cell.Building==BuildingKind.None||cell.Building==BuildingKind.Road||cell.Building==BuildingKind.TownHall||cell.Building==BuildingKind.Park||cell.Building==BuildingKind.Windmill||cell.Building==BuildingKind.House) continue;
                if(!routeCache.TryGetValue(index,out var route)) { route=RoadRoute(cell); routeCache[index]=route; }
                if(route.Count<2) continue;
                nextSource=(index+1)%441;
                var root=GetVan(n%2==0?mint:red); root.transform.position=route[0]+Vector3.up*.11f;
                trucks.Add(new Truck{Root=root,Route=route,Step=1}); return;
            }
        }
        List<Vector3> RoadRoute(Cell source)
        {
            var result=new List<Vector3>(); var q=new Queue<int>(); var previous=new Dictionary<int,int>();
            for(int d=0;d<4;d++) { var c=controller.Sim.GetCell(source.X+dx[d],source.Z+dz[d]); if(c!=null && c.Connected && IsRoad(c.X,c.Z)) { int id=c.Z*21+c.X; q.Enqueue(id); previous[id]=-1; } }
            int goal=10*21+10;
            while(q.Count>0)
            {
                int id=q.Dequeue(); if(id==goal)
                {
                    while(id>=0) { result.Add(Position(id%21,id/21)); id=previous[id]; } result.Reverse(); return result;
                }
                for(int d=0;d<4;d++) { int x=id%21+dx[d],z=id/21+dz[d],next=z*21+x; if(IsRoad(x,z)&&!previous.ContainsKey(next)) {previous[next]=id;q.Enqueue(next);} }
            }
            return result;
        }
        GameObject GetVan(Material cargoMaterial)
        {
            GameObject root;
            if(vanPool.Count>0) { root=vanPool.Dequeue(); root.SetActive(true); root.transform.rotation=Quaternion.identity; root.transform.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial=cargoMaterial; return root; }
            root=new GameObject("Supply van"); root.transform.SetParent(trafficRoot,false);
            Box(root.transform,"Cargo",new Vector3(.16f,.13f,.24f),new Vector3(0,.06f,-.015f),cargoMaterial);
            Box(root.transform,"Cabin",new Vector3(.16f,.09f,.1f),new Vector3(0,.045f,.14f),curb);
            Box(root.transform,"Windscreen",new Vector3(.13f,.037f,.012f),new Vector3(0,.073f,.194f),asphalt);
            Box(root.transform,"Wheels",new Vector3(.20f,.045f,.20f),new Vector3(0,-.025f,.025f),asphalt); return root;
        }
        void RecycleVan(GameObject root) { if(!root)return; root.SetActive(false); vanPool.Enqueue(root); }
        void ClearTraffic() { foreach(var truck in trucks) RecycleVan(truck.Root); trucks.Clear(); }
        sealed class Truck { public GameObject Root; public List<Vector3> Route; public int Step; }
        Material Mat(string name,Color color,float smooth=.15f,bool unlit=false) { var m=new Material(Shader.Find(unlit?"Unlit/Color":"Standard")){name=name,color=color}; if(!unlit) m.SetFloat("_Glossiness",smooth); ownedMaterials.Add(m); return m; }
        void OnDestroy() { foreach(var material in ownedMaterials) if(material) Destroy(material); ownedMaterials.Clear(); }
        static LineRenderer Line(string name,Transform parent,float width,Material material) { var o=new GameObject(name); o.transform.SetParent(parent,false); var l=o.AddComponent<LineRenderer>(); l.sharedMaterial=material;l.widthMultiplier=width;l.useWorldSpace=false;l.shadowCastingMode=ShadowCastingMode.Off;l.receiveShadows=false; return l; }
        static GameObject Box(Transform parent,string name,Vector3 scale,Vector3 position,Material material,bool collider=false)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.localPosition=position;obj.transform.localScale=scale;obj.GetComponent<MeshRenderer>().sharedMaterial=material;
            if(!collider) { obj.GetComponent<Collider>().enabled=false; Destroy(obj.GetComponent<Collider>()); } return obj;
        }
    }
}

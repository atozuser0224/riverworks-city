using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riverworks
{
    public sealed class FactoryTileHandle : MonoBehaviour { public int X,Z; }

    public sealed class FactoryView : MonoBehaviour
    {
        const float CargoBaseHeight=FactoryVisuals.CargoRootHeight;
        static readonly float MicroScale=BoardView.Spacing*.5f;
        FactoryController controller;
        Transform modelRoot,cargoRoot,wireRoot,previewRoot;
        readonly Dictionary<int,GameObject> models=new Dictionary<int,GameObject>();
        readonly Dictionary<int,int> renderedSignatures=new Dictionary<int,int>();
        readonly Dictionary<int,Transform> animatedArms=new Dictionary<int,Transform>();
        readonly List<CargoVisual> cargoPool=new List<CargoVisual>();
        readonly List<PowerWire> wires=new List<PowerWire>();
        readonly Dictionary<int,int> renderedPowerSignatures=new Dictionary<int,int>();
        readonly HashSet<int> seenEntityIds=new HashSet<int>();
        readonly List<int> staleModelIds=new List<int>();
        readonly List<Material> ownedMaterials=new List<Material>();
        FactoryState boundState;
        FactoryVisuals.Palette palette;
        Material valid,invalid,selected,wireOn,wireOff;
        LineRenderer outline,selectionOutline;
        Transform directionArrow;
        int highlightX=int.MinValue,highlightZ=int.MinValue;
        bool highlightValid;
        Vector3 cameraFocus;
        int selectedEntityId=int.MinValue,selectionSignature=int.MinValue;

        sealed class PowerWire
        {
            public LineRenderer Line;
            public FactoryEntity First,Second;
        }

        sealed class CargoVisual
        {
            public GameObject Root;
            public MeshRenderer Renderer;
            public Resource Resource=Resource.Coins;
        }

        public Camera Camera { get { return controller!=null&&controller.Game!=null&&controller.Game.CameraRig!=null?controller.Game.CameraRig.Camera:null; } }
        public Vector3 CameraFocus { get { return cameraFocus; } }

        public void Initialize(FactoryController value)
        {
            controller=value;
            gameObject.name="Factory machines on city board";
            if(controller!=null&&controller.Game!=null&&controller.Game.Board!=null) transform.SetParent(controller.Game.Board.transform,false);
            BuildMaterials();
            modelRoot=Root("Factory machines"); cargoRoot=Root("Actual moving resources"); wireRoot=Root("Pole network"); previewRoot=Root("Placement preview");
            BuildPreview();
            cameraFocus=BoardView.Position(10,10);
            Refresh(true);
        }

        Transform Root(string name) { var o=new GameObject(name).transform; o.SetParent(transform,false); return o; }

        void BuildMaterials()
        {
            valid=Mat("Placement valid",new Color(.15f,.95f,.66f),true); invalid=Mat("Placement invalid",new Color(1f,.22f,.18f),true);
            selected=Mat("Selected machine",new Color(1f,.72f,.12f),true);
            wireOn=Mat("Live power cable",new Color(.10f,.88f,.76f),true); wireOff=Mat("Unpowered cable",new Color(.10f,.13f,.14f),true);
            palette=new FactoryVisuals.Palette {
                Slate=Mat("Machine slate",new Color(.35f,.40f,.39f)), Dark=Mat("Machine dark",new Color(.18f,.22f,.21f)), Brass=Mat("Brass yellow",new Color(.88f,.67f,.32f)), Belt=Mat("Conveyor rubber",new Color(.20f,.22f,.19f)),
                Teal=Mat("Power teal",new Color(.11f,.59f,.52f)), Orange=Mat("Furnace orange",new Color(1f,.29f,.05f),true), Violet=Mat("Assembler violet",new Color(.43f,.31f,.55f)), Blue=Mat("Storage blue",new Color(.22f,.42f,.50f)),
                Iron=Mat("Iron",new Color(.67f,.73f,.73f)), Wood=Mat("Timber",new Color(.48f,.27f,.10f)), Flour=Mat("Flour",new Color(.92f,.88f,.72f)), Bread=Mat("Bread",new Color(.72f,.38f,.12f)), Ore=Mat("Iron ore",new Color(.72f,.29f,.11f)), Steel=Mat("Steel",new Color(.56f,.65f,.69f)), Tools=Mat("Tools",new Color(.86f,.46f,.13f)), White=Mat("Insulator",new Color(.88f,.91f,.88f))
            };
        }

        void BuildPreview()
        {
            selectionOutline=Line("Selected machine footprint",previewRoot,.065f,selected); selectionOutline.positionCount=5; selectionOutline.enabled=false;
            outline=Line("Placement footprint",previewRoot,.075f,valid); outline.positionCount=5; outline.enabled=false;
            directionArrow=new GameObject("Placement direction").transform; directionArrow.SetParent(previewRoot,false);
            directionArrow.localScale=Vector3.one*MicroScale;
            var stem=Primitive(directionArrow,"Stem",PrimitiveType.Cube,new Vector3(.10f,.025f,.62f),new Vector3(0,.05f,.38f),valid);
            var head=Primitive(directionArrow,"Head",PrimitiveType.Cube,new Vector3(.30f,.025f,.30f),new Vector3(0,.05f,.72f),valid); head.transform.localRotation=Quaternion.Euler(0,45,0);
            directionArrow.gameObject.SetActive(false);
        }

        public Vector3 WorldPosition(int x,int z)
        {
            return BoardView.Position(0,0)+new Vector3((x*.5f-.25f)*BoardView.Spacing,0,(z*.5f-.25f)*BoardView.Spacing);
        }
        public Transform ModelFor(int entityId)
        {
            return models.TryGetValue(entityId,out var model)&&model!=null ? model.transform : null;
        }
        public void HomeCamera()
        {
            cameraFocus=BoardView.Position(10,10);
            if(controller!=null&&controller.Game!=null&&controller.Game.CameraRig!=null) controller.Game.CameraRig.Home();
        }
        public void PanScreen(Vector2 delta)
        {
            if(controller!=null&&controller.Game!=null&&controller.Game.CameraRig!=null) controller.Game.CameraRig.PanScreen(delta);
        }
        public void Zoom(float factor){if(factor>0&&controller!=null&&controller.Game!=null&&controller.Game.CameraRig!=null)controller.Game.CameraRig.Zoom(factor);}

        public void Refresh(bool structureChanged)
        {
            if(controller==null || controller.Sim==null) return;
            var state=controller.Sim.State;
            if(state!=boundState)
            {
                boundState=state; ClearRenderedState(); ReconcileModels(state); RebuildWires(state);
            }
            else
            {
                if(structureChanged || StructureDiffers(state)) ReconcileModels(state);
                if(PowerTopologyDiffers(state)) RebuildWires(state);
            }
            UpdateDynamic(state);
            UpdateSelectionOutline();
        }

        bool StructureDiffers(FactoryState state)
        {
            if(state==null || state.Entities==null) return models.Count!=0;
            if(models.Count!=state.Entities.Count) return true;
            for(int i=0;i<state.Entities.Count;i++)
            {
                var e=state.Entities[i]; int old;
                if(!renderedSignatures.TryGetValue(e.Id,out old) || old!=Signature(e)) return true;
            }
            return false;
        }

        void ClearRenderedState()
        {
            foreach(var pair in models) if(pair.Value) Destroy(pair.Value);
            models.Clear(); renderedSignatures.Clear(); animatedArms.Clear(); ClearWires(); renderedPowerSignatures.Clear();
            selectionOutline.enabled=false; selectedEntityId=int.MinValue; selectionSignature=int.MinValue;
        }

        void ReconcileModels(FactoryState state)
        {
            if(state==null || state.Entities==null) return;
            seenEntityIds.Clear();
            for(int i=0;i<state.Entities.Count;i++)
            {
                var e=state.Entities[i]; seenEntityIds.Add(e.Id); int signature=Signature(e),oldSignature;
                if(renderedSignatures.TryGetValue(e.Id,out oldSignature) && oldSignature==signature) continue;
                GameObject old; if(models.TryGetValue(e.Id,out old) && old) Destroy(old);
                var root=FactoryVisuals.Build(e,modelRoot,palette);
                root.transform.localPosition=LocalMicroPosition(e.X,e.Z);
                root.transform.localScale=Vector3.one*MicroScale;
                models[e.Id]=root; renderedSignatures[e.Id]=signature;
                animatedArms[e.Id]=root.transform.Find("Directional model/Animated arm");
            }
            staleModelIds.Clear(); foreach(var pair in models) if(!seenEntityIds.Contains(pair.Key)) staleModelIds.Add(pair.Key);
            for(int i=0;i<staleModelIds.Count;i++)
            {
                int id=staleModelIds[i]; GameObject old=models[id]; if(old) Destroy(old);
                models.Remove(id); renderedSignatures.Remove(id); animatedArms.Remove(id);
            }
        }

        static int Signature(FactoryEntity e) { unchecked { return (((e.X*31+e.Z)*31+(int)e.Kind)*31+e.Direction); } }

        bool PowerTopologyDiffers(FactoryState state)
        {
            if(state==null || state.Entities==null) return renderedPowerSignatures.Count!=0;
            int count=0;
            for(int i=0;i<state.Entities.Count;i++)
            {
                var e=state.Entities[i]; if(e.Kind!=FactoryKind.Pole && e.Kind!=FactoryKind.PowerInlet) continue;
                count++; int old; if(!renderedPowerSignatures.TryGetValue(e.Id,out old) || old!=PowerSignature(e)) return true;
            }
            return count!=renderedPowerSignatures.Count;
        }

        void RebuildWires(FactoryState state)
        {
            ClearWires(); renderedPowerSignatures.Clear();
            if(state==null || state.Entities==null) return;
            var nodes=new List<FactoryEntity>();
            for(int i=0;i<state.Entities.Count;i++) if(state.Entities[i].Kind==FactoryKind.Pole || state.Entities[i].Kind==FactoryKind.PowerInlet) { nodes.Add(state.Entities[i]); renderedPowerSignatures[state.Entities[i].Id]=PowerSignature(state.Entities[i]); }
            for(int i=0;i<nodes.Count;i++)
            {
                for(int j=i+1;j<nodes.Count;j++)
                {
                    if(FactorySimulation.GridDistance(nodes[i],nodes[j])>6) continue;
                    bool powered=nodes[i].Powered&&nodes[j].Powered;
                    var line=Line("Power wire "+nodes[i].Id+"-"+nodes[j].Id,wireRoot,powered?.024f:.012f,powered?wireOn:wireOff); line.positionCount=2;
                    line.SetPositions(new[]{WirePoint(nodes[i]),WirePoint(nodes[j])});
                    wires.Add(new PowerWire { Line=line, First=nodes[i], Second=nodes[j] });
                }
            }
        }

        Vector3 WirePoint(FactoryEntity entity)
        {
            var spec=FactoryCatalog.Get(entity.Kind);
            float x=spec==null?0:(spec.Width-1)*.5f,z=spec==null?0:(spec.Height-1)*.5f;
            return LocalMicroPosition(entity.X,entity.Z)+new Vector3(x*MicroScale,1.55f*MicroScale,z*MicroScale);
        }

        static int PowerSignature(FactoryEntity entity) { unchecked { return (entity.X*31+entity.Z)*31+(int)entity.Kind; } }

        void ClearWires() { for(int i=0;i<wires.Count;i++) if(wires[i].Line) Destroy(wires[i].Line.gameObject); wires.Clear(); }

        void UpdateDynamic(FactoryState state)
        {
            for(int i=0;i<wires.Count;i++)
            {
                var wire=wires[i]; if(!wire.Line) continue;
                bool powered=wire.First.Powered&&wire.Second.Powered;
                wire.Line.sharedMaterial=powered?wireOn:wireOff;
                wire.Line.widthMultiplier=powered?.024f:.012f;
            }
            if(state==null||state.Entities==null)
            {
                for(int i=0;i<cargoPool.Count;i++) cargoPool[i].Root.SetActive(false);
                return;
            }
            int cargoIndex=0;
            for(int i=0;i<state.Entities.Count;i++)
            {
                var e=state.Entities[i]; GameObject root; if(!models.TryGetValue(e.Id,out root) || !root) continue;
                Transform arm; animatedArms.TryGetValue(e.Id,out arm);
                if(arm) arm.localRotation=Quaternion.Euler(-20f+Mathf.Clamp01(e.CargoProgress)*40f,Mathf.Sin(Mathf.Clamp01(e.CargoProgress)*Mathf.PI)*18f,0);
                if(e.CargoResource==Resource.Coins) continue;
                var cargo=GetCargo(cargoIndex++,e.CargoResource); cargo.SetActive(true);
                float progress=Mathf.Clamp01(e.CargoProgress);
                var spec=FactoryCatalog.Get(e.Kind);
                float centerX=spec==null?0:(spec.Width-1)*.5f,centerZ=spec==null?0:(spec.Height-1)*.5f;
                Vector3 local=LocalMicroPosition(e.X,e.Z)+new Vector3(centerX*MicroScale,CargoBaseHeight*MicroScale,centerZ*MicroScale);
                Vector3 forward=Direction(e.Direction);
                if(e.Kind==FactoryKind.Belt || e.Kind==FactoryKind.Splitter) local+=forward*((progress-.5f)*.72f*MicroScale);
                else if(e.Kind==FactoryKind.Inserter) local+=forward*((progress-.5f)*1.45f*MicroScale)+Vector3.up*(Mathf.Sin(progress*Mathf.PI)*.55f*MicroScale);
                else local+=forward*(.35f*MicroScale);
                cargo.transform.localPosition=local;
                cargo.transform.localRotation=Quaternion.Euler(0,45f+progress*90f,0);
            }
            for(int i=cargoIndex;i<cargoPool.Count;i++) cargoPool[i].Root.SetActive(false);
        }

        GameObject GetCargo(int index,Resource resource)
        {
            CargoVisual cargo;
            if(index>=cargoPool.Count)
            {
                var root=FactoryVisuals.Cargo(cargoRoot,palette.White);
                root.transform.localScale=Vector3.one*MicroScale;
                cargo=new CargoVisual { Root=root, Renderer=root.transform.GetChild(0).GetComponent<MeshRenderer>() }; cargoPool.Add(cargo);
            }
            else cargo=cargoPool[index];
            if(cargo.Resource!=resource)
            {
                cargo.Resource=resource; cargo.Renderer.sharedMaterial=palette.ForResource(resource); cargo.Root.name="Actual cargo "+resource;
            }
            return cargo.Root;
        }

        void UpdateSelectionOutline()
        {
            var entity=controller.SelectedEntity; GameObject root;
            if(entity==null || !models.TryGetValue(entity.Id,out root) || !root)
            {
                selectionOutline.enabled=false; selectedEntityId=int.MinValue; selectionSignature=int.MinValue; return;
            }
            selectionOutline.enabled=true;
            int signature=Signature(entity); if(entity.Id==selectedEntityId && signature==selectionSignature) return; selectedEntityId=entity.Id; selectionSignature=signature;
            var spec=FactoryCatalog.Get(entity.Kind); int width=spec==null?1:spec.Width,height=spec==null?1:spec.Height;
            var center=LocalMicroPosition(entity.X,entity.Z)+new Vector3((width-1)*MicroScale*.5f,.112f,(height-1)*MicroScale*.5f);
            float halfWidth=width*MicroScale*.5f,halfHeight=height*MicroScale*.5f;
            selectionOutline.SetPositions(Rectangle(center,halfWidth,halfHeight));
        }

        public void Highlight(int x,int z,bool isValid)
        {
            highlightX=x; highlightZ=z; highlightValid=isValid;
            bool active=x>=0&&z>=0&&controller!=null;
            outline.enabled=active; directionArrow.gameObject.SetActive(active&&controller.SelectedTool!=FactoryKind.None&&!controller.RemovalMode);
            if(!active) return;
            int width=1,height=1;
            if(controller.SelectedTool!=FactoryKind.None)
            {
                var spec=FactoryCatalog.Get(controller.SelectedTool); if(spec!=null) { width=spec.Width; height=spec.Height; }
            }
            var center=LocalMicroPosition(x,z)+new Vector3((width-1)*MicroScale*.5f,.116f,(height-1)*MicroScale*.5f);
            float halfWidth=width*MicroScale*.5f,halfHeight=height*MicroScale*.5f;
            outline.sharedMaterial=isValid?valid:invalid; outline.SetPositions(Rectangle(center,halfWidth,halfHeight));
            directionArrow.localPosition=center; directionArrow.localRotation=Quaternion.Euler(0,90-90*controller.Direction,0);
            var material=isValid?valid:invalid; foreach(var renderer in directionArrow.GetComponentsInChildren<MeshRenderer>()) renderer.sharedMaterial=material;
        }

        void Update()
        {
            if(controller==null || controller.Sim==null) return;
            Refresh(false);
        }

        Vector3 LocalMicroPosition(int x,int z)
        {
            return transform.InverseTransformPoint(WorldPosition(x,z));
        }

        static Vector3[] Rectangle(Vector3 center,float halfWidth,float halfHeight)
        {
            return new[]{
                center+new Vector3(-halfWidth,0,-halfHeight),center+new Vector3(-halfWidth,0,halfHeight),
                center+new Vector3(halfWidth,0,halfHeight),center+new Vector3(halfWidth,0,-halfHeight),
                center+new Vector3(-halfWidth,0,-halfHeight)
            };
        }

        static Vector3 Direction(int direction)
        {
            switch(direction&3) { case 0:return Vector3.right; case 1:return Vector3.forward; case 2:return Vector3.left; default:return Vector3.back; }
        }

        Material Mat(string name,Color color,bool unlit=false)
        {
            var shader=Shader.Find(unlit?"Unlit/Color":"Standard"); var m=new Material(shader){name=name,color=color}; if(!unlit) m.SetFloat("_Glossiness",.18f); ownedMaterials.Add(m); return m;
        }

        static LineRenderer Line(string name,Transform parent,float width,Material material)
        {
            var o=new GameObject(name); o.transform.SetParent(parent,false); var l=o.AddComponent<LineRenderer>(); l.sharedMaterial=material; l.widthMultiplier=width; l.useWorldSpace=false; l.shadowCastingMode=ShadowCastingMode.Off; l.receiveShadows=false; return l;
        }

        static GameObject Primitive(Transform parent,string name,PrimitiveType type,Vector3 scale,Vector3 position,Material material)
        {
            var o=GameObject.CreatePrimitive(type); o.name=name; o.transform.SetParent(parent,false); o.transform.localScale=scale; o.transform.localPosition=position; o.GetComponent<MeshRenderer>().sharedMaterial=material; var c=o.GetComponent<Collider>(); if(c) { c.enabled=false; Destroy(c); } return o;
        }

        void OnDestroy() { for(int i=0;i<ownedMaterials.Count;i++) if(ownedMaterials[i]) Destroy(ownedMaterials[i]); ownedMaterials.Clear(); }
    }
}

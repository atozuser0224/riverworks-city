using UnityEngine;

namespace Riverworks
{
    public enum ResidentAction { Idle, Walk, Talk, Carry, Chop, Dig, Farm, Knead, Hammer, Read, Operate, Sit }

    public enum WorksitePropKind { None, Workbench, Log, Rock, FieldPatch, Dough, Anvil, Sack, Crate, ReadingStand, CoalBin }

    public readonly struct WorksitePose
    {
        public readonly Vector3 LocalPosition;
        public readonly float LocalYaw;
        public readonly ResidentAction Action;
        public readonly string Label;
        public readonly WorksitePropKind PropKind;

        public WorksitePose(Vector3 localPosition,float localYaw,ResidentAction action,string label,WorksitePropKind propKind)
        {
            LocalPosition=localPosition; LocalYaw=localYaw; Action=action; Label=label; PropKind=propKind;
        }
    }

    /// <summary>Presentation-only resident poses and shared, collider-free work props.</summary>
    public static class CitizenWorksites
    {
        public const int MaximumSlotsPerBuilding=2;
        const string StationPrefix="Resident Worksite Station ";
        static Material wood,iron,stone,grain,cloth,soil,coal;

        public static bool IsWorksiteDwell(Citizen citizen,GameState state)
        {
            if(citizen==null||state==null||!citizen.Indoors||citizen.CurrentIndex<0||citizen.CurrentIndex>=state.Cells.Count) return false;
            BuildingKind kind=state.Cells[citizen.CurrentIndex].Building;
            if(citizen.Activity==CitizenActivity.Working)
                return citizen.CurrentIndex==citizen.JobIndex&&IsWorkingBuilding(kind);
            return citizen.Activity==CitizenActivity.Leisure&&
                   (kind==BuildingKind.Market||kind==BuildingKind.Park||kind==BuildingKind.TownHall);
        }

        public static bool TryGetPose(Citizen citizen,GameState state,int slot,out WorksitePose pose)
        {
            if(!IsWorksiteDwell(citizen,state)) { pose=default; return false; }
            return TryGetPose(state.Cells[citizen.CurrentIndex].Building,slot,out pose);
        }

        public static bool TryGetPose(BuildingKind kind,int slot,out WorksitePose pose)
        {
            if(slot<0||slot>=MaximumSlotsPerBuilding) { pose=default; return false; }
            float side=slot==0?-1f:1f;
            switch(kind)
            {
                case BuildingKind.Lumberyard: pose=At(side*.38f,-.43f,side*8f,ResidentAction.Chop,"통나무 다듬기",WorksitePropKind.Log); return true;
                case BuildingKind.Quarry: pose=At(side*.38f,-.43f,side*7f,ResidentAction.Dig,"돌 캐기",WorksitePropKind.Rock); return true;
                case BuildingKind.Farm: pose=At(side*.35f,-.33f,side*6f,ResidentAction.Farm,"밭 경작하기",WorksitePropKind.FieldPatch); return true;
                case BuildingKind.Mill: pose=At(side*.38f,-.43f,side*8f,ResidentAction.Carry,"곡물 자루 나르기",WorksitePropKind.Sack); return true;
                case BuildingKind.Bakery: pose=At(side*.38f,-.43f,side*7f,ResidentAction.Knead,"반죽하고 굽기",WorksitePropKind.Dough); return true;
                case BuildingKind.Mine: pose=At(side*.38f,-.43f,side*8f,ResidentAction.Dig,"광석 캐기",WorksitePropKind.Rock); return true;
                case BuildingKind.Smelter: pose=At(side*.38f,-.43f,side*7f,ResidentAction.Hammer,"쇠 달구고 두드리기",WorksitePropKind.Anvil); return true;
                case BuildingKind.Workshop: pose=At(side*.38f,-.43f,side*7f,ResidentAction.Hammer,"도구 만들기",WorksitePropKind.Workbench); return true;
                case BuildingKind.Windmill: pose=At(side*.38f,-.43f,side*8f,ResidentAction.Carry,"곡물 자루 옮기기",WorksitePropKind.Sack); return true;
                case BuildingKind.Warehouse: pose=At(side*.38f,-.43f,side*9f,ResidentAction.Carry,"화물 나르기",WorksitePropKind.Crate); return true;
                case BuildingKind.Market: pose=At(side*.29f,-.36f,-side*22f,ResidentAction.Talk,"흥정하고 장보기",WorksitePropKind.None); return true;
                case BuildingKind.StudyHouse: pose=At(side*.38f,-.43f,side*6f,ResidentAction.Read,"책 읽고 연구하기",WorksitePropKind.ReadingStand); return true;
                case BuildingKind.Academy: pose=At(side*.38f,-.43f,side*7f,ResidentAction.Read,"강의하고 연구하기",WorksitePropKind.ReadingStand); return true;
                case BuildingKind.SteamPlant: pose=At(side*.38f,-.43f,side*8f,ResidentAction.Operate,"보일러 돌보기",WorksitePropKind.CoalBin); return true;
                case BuildingKind.Park: pose=At(side*.24f,-.30f,slot==0?18f:-18f,ResidentAction.Sit,"공원에서 쉬기",WorksitePropKind.None); return true;
                case BuildingKind.TownHall: pose=At(side*.23f,-.37f,-side*18f,ResidentAction.Talk,"광장에서 이야기하기",WorksitePropKind.None); return true;
                default: pose=default; return false;
            }
        }

        /// <summary>Creates the stationary prop group once below the already-oriented building root.</summary>
        public static GameObject CreateStation(Transform buildingRoot,BuildingKind kind,int slot)
        {
            if(buildingRoot==null||!TryGetPose(kind,slot,out WorksitePose pose)) return null;
            string stationName=StationPrefix+slot;
            Transform existing=buildingRoot.Find(stationName);
            if(existing!=null) return existing.gameObject;
            var root=new GameObject(stationName); root.transform.SetParent(buildingRoot,false);
            root.transform.localPosition=pose.LocalPosition;
            root.transform.localRotation=Quaternion.Euler(0f,pose.LocalYaw,0f);
            AddStationVisual(root.transform,pose.PropKind,slot);
            return root;
        }

        static WorksitePose At(float x,float z,float yaw,ResidentAction action,string label,WorksitePropKind prop)
            =>new WorksitePose(new Vector3(x,0f,z),yaw,action,label,prop);

        static bool IsWorkingBuilding(BuildingKind kind)
        {
            switch(kind)
            {
                case BuildingKind.Lumberyard: case BuildingKind.Quarry: case BuildingKind.Farm:
                case BuildingKind.Mill: case BuildingKind.Bakery: case BuildingKind.Mine:
                case BuildingKind.Smelter: case BuildingKind.Workshop: case BuildingKind.Windmill:
                case BuildingKind.Warehouse: case BuildingKind.StudyHouse: case BuildingKind.Academy:
                case BuildingKind.SteamPlant: return true;
                default: return false;
            }
        }

        static void AddStationVisual(Transform parent,WorksitePropKind kind,int slot)
        {
            switch(kind)
            {
                case WorksitePropKind.Workbench:
                    Box(parent,"Small workbench",new Vector3(.20f,.035f,.10f),new Vector3(0,.13f,.08f),Wood);
                    Box(parent,"Bench legs",new Vector3(.14f,.12f,.035f),new Vector3(0,.065f,.08f),Wood); break;
                case WorksitePropKind.Log:
                    Cyl(parent,"Work log",.065f,.065f,.25f,new Vector3(0,.07f,.09f),new Vector3(0,0,90),Wood);
                    Cyl(parent,"Chopping stump",.075f,.085f,.10f,new Vector3(0,.05f,.09f),Vector3.zero,Wood); break;
                case WorksitePropKind.Rock:
                    Ball(parent,"Work rock",new Vector3(.13f,.09f,.11f),new Vector3(0,.07f,.09f),Stone); break;
                case WorksitePropKind.FieldPatch:
                    Box(parent,"Worked soil",new Vector3(.21f,.012f,.13f),new Vector3(0,.008f,.08f),Soil);
                    for(int i=-1;i<=1;i++) Box(parent,"Seed row",new Vector3(.018f,.014f,.11f),new Vector3(i*.055f,.017f,.08f),Grain); break;
                case WorksitePropKind.Dough:
                    Box(parent,"Dough table",new Vector3(.19f,.025f,.11f),new Vector3(0,.13f,.08f),Wood);
                    Box(parent,"Table stand",new Vector3(.12f,.12f,.035f),new Vector3(0,.065f,.08f),Wood);
                    Ball(parent,"Dough",new Vector3(.09f,.018f,.06f),new Vector3(0,.155f,.08f),Cloth); break;
                case WorksitePropKind.Anvil:
                    Box(parent,"Small anvil",new Vector3(.14f,.04f,.065f),new Vector3(0,.15f,.08f),Iron);
                    Box(parent,"Anvil stand",new Vector3(.055f,.13f,.055f),new Vector3(0,.075f,.08f),Wood); break;
                case WorksitePropKind.Sack:
                    Ball(parent,"Grain sack",new Vector3(.10f,.14f,.07f),new Vector3(0,.08f,.09f),Grain); break;
                case WorksitePropKind.Crate:
                    Box(parent,"Cargo crate",new Vector3(.13f,.13f,.13f),new Vector3(0,.07f,.09f),Wood);
                    Box(parent,"Crate band",new Vector3(.135f,.025f,.135f),new Vector3(0,.07f,.09f),Iron); break;
                case WorksitePropKind.ReadingStand:
                    Box(parent,"Reading stand",new Vector3(.15f,.025f,.10f),new Vector3(0,.15f,.08f),Wood);
                    Box(parent,"Stand post",new Vector3(.035f,.14f,.035f),new Vector3(0,.075f,.08f),Wood);
                    Box(parent,"Open book",new Vector3(.13f,.009f,.08f),new Vector3(0,.17f,.075f),Cloth); break;
                case WorksitePropKind.CoalBin:
                    Box(parent,"Coal bin",new Vector3(.16f,.10f,.13f),new Vector3(0,.055f,.09f),Iron);
                    for(int i=0;i<3;i++) Ball(parent,"Coal",Vector3.one*.045f,new Vector3((i-1)*.04f,.12f,.09f+(i%2)*.025f),Coal); break;
            }
        }

        static Material Shared(ref Material slot,string name,Color color)
        {
            if(slot) return slot;
            slot=new Material(Shader.Find("Standard")){name="Riverworks Worksite "+name,color=color};
            slot.SetFloat("_Glossiness",.1f); return slot;
        }
        static Material Wood=>Shared(ref wood,"Wood",new Color(.34f,.18f,.10f));
        static Material Iron=>Shared(ref iron,"Iron",new Color(.12f,.13f,.14f));
        static Material Stone=>Shared(ref stone,"Stone",new Color(.40f,.43f,.44f));
        static Material Grain=>Shared(ref grain,"Grain",new Color(.90f,.67f,.20f));
        static Material Cloth=>Shared(ref cloth,"Canvas",new Color(.94f,.90f,.79f));
        static Material Soil=>Shared(ref soil,"Soil",new Color(.29f,.18f,.12f));
        static Material Coal=>Shared(ref coal,"Coal",new Color(.07f,.07f,.065f));

        static GameObject Primitive(PrimitiveType type,Transform parent,string name,Vector3 scale,Vector3 position,Material material)
        {
            GameObject value=GameObject.CreatePrimitive(type); value.name=name; value.transform.SetParent(parent,false);
            value.transform.localScale=scale; value.transform.localPosition=position;
            Collider collider=value.GetComponent<Collider>(); if(collider!=null) Object.Destroy(collider);
            value.GetComponent<Renderer>().sharedMaterial=material; return value;
        }
        static GameObject Box(Transform parent,string name,Vector3 scale,Vector3 position,Material material)
            =>Primitive(PrimitiveType.Cube,parent,name,scale,position,material);
        static GameObject Ball(Transform parent,string name,Vector3 scale,Vector3 position,Material material)
            =>Primitive(PrimitiveType.Sphere,parent,name,scale,position,material);
        static GameObject Cyl(Transform parent,string name,float top,float bottom,float height,Vector3 position,Vector3 rotation,Material material)
        {
            GameObject value=Primitive(PrimitiveType.Cylinder,parent,name,new Vector3(Mathf.Max(top,bottom)*2f,height*.5f,Mathf.Max(top,bottom)*2f),position,material);
            value.transform.localEulerAngles=rotation; return value;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riverworks
{
    /// <summary>Procedural, collider-free industrial models used by FactoryView.</summary>
    internal static class FactoryVisuals
    {
        internal const float TransportLift=.80f;
        internal const float CargoRootHeight=TransportLift+.31f;

        internal sealed class Palette
        {
            public Material Slate, Dark, Brass, Belt, Teal, Orange, Violet, Blue, Iron, Wood, Flour, Bread, Ore, Steel, Tools, White;
            public readonly Dictionary<Resource,Material> Resources=new Dictionary<Resource,Material>();

            public Material ForResource(Resource resource)
            {
                Material catalogMaterial;
                if(Resources.TryGetValue(resource,out catalogMaterial)) return catalogMaterial;
                switch(resource)
                {
                    case Resource.Timber: return Wood;
                    case Resource.Stone: return Slate;
                    case Resource.Grain: return Brass;
                    case Resource.Flour: return Flour;
                    case Resource.Bread: return Bread;
                    case Resource.Ore: return Ore;
                    case Resource.Steel: return Steel;
                    case Resource.Tools: return Tools;
                    default: return White;
                }
            }
        }

        public static GameObject Build(FactoryEntity entity, Transform parent, Palette p)
        {
            var root=new GameObject(entity.Kind+" #"+entity.Id);
            root.transform.SetParent(parent,false);
            var visual=new GameObject("Directional model").transform; visual.SetParent(root.transform,false);
            var spec=FactoryCatalog.Get(entity.Kind);
            bool large=spec!=null&&(spec.Width>1||spec.Height>1);
            bool raisedTransport=entity.Kind==FactoryKind.Belt||entity.Kind==FactoryKind.Splitter||entity.Kind==FactoryKind.Inserter;
            visual.localPosition=new Vector3(spec==null?0:(spec.Width-1)*.5f,raisedTransport?TransportLift:0,spec==null?0:(spec.Height-1)*.5f);
            visual.localRotation=Quaternion.Euler(0,90-90*entity.Direction,0);
            switch(entity.Kind)
            {
                case FactoryKind.Belt: Belt(visual,p,false); break;
                case FactoryKind.Splitter: Belt(visual,p,true); break;
                case FactoryKind.Inserter: Inserter(visual,p); break;
                case FactoryKind.Drill: Drill(visual,p); break;
                case FactoryKind.Furnace: Furnace(visual,p); break;
                case FactoryKind.Assembler: Assembler(visual,p); break;
                case FactoryKind.Storage: Storage(visual,p); break;
                case FactoryKind.ImportDock: Dock(visual,p,true); break;
                case FactoryKind.ExportDock: Dock(visual,p,false); break;
                case FactoryKind.PowerInlet: PowerInlet(visual,p); break;
                case FactoryKind.Pole: Pole(visual,p); break;
                case FactoryKind.Pipe: Pipe(visual,p,false); break;
                case FactoryKind.PipeJunction: Pipe(visual,p,true); break;
                case FactoryKind.FluidTank: FluidTank(visual,p); break;
                case FactoryKind.WaterPump: WaterPump(visual,p); break;
                case FactoryKind.OilPump: OilPump(visual,p); break;
                case FactoryKind.Foundry: Foundry(visual,p); break;
                case FactoryKind.MachiningBench: MachiningBench(visual,p); break;
                case FactoryKind.Refinery: Refinery(visual,p); break;
                case FactoryKind.ChemicalPlant: ChemicalPlant(visual,p); break;
                case FactoryKind.Manufacturer: Manufacturer(visual,p); break;
                case FactoryKind.ItemLift: ItemLift(visual,p,entity.IsLinkSender); break;
                case FactoryKind.FluidRiser: FluidRiser(visual,p,entity.IsLinkSender); break;
            }
            if(entity.Kind!=FactoryKind.Pole && entity.Kind!=FactoryKind.PowerInlet &&
               entity.Kind!=FactoryKind.Pipe && entity.Kind!=FactoryKind.PipeJunction && entity.Kind!=FactoryKind.FluidTank &&
               entity.Kind!=FactoryKind.ItemLift && entity.Kind!=FactoryKind.FluidRiser)
                Arrow(visual,p.Brass,p.Dark,entity.Kind==FactoryKind.Splitter,large);
            if((int)entity.Kind>=(int)FactoryKind.WaterPump||entity.ControllerInstalled)
                Box(visual,"Machine status beacon",new Vector3(.20f,.12f,.20f),new Vector3(.68f,1.08f,.68f),p.Teal);
            if(entity.ControllerInstalled)
                Box(visual,"Automation controller badge",new Vector3(.30f,.24f,.06f),new Vector3(-.68f,.72f,.72f),p.Violet);
            return root;
        }

        static void Belt(Transform r,Palette p,bool splitter)
        {
            for(int x=-1;x<=1;x+=2) for(int z=-1;z<=1;z+=2)
                Box(r,"Raised belt support",new Vector3(.075f,.72f,.075f),new Vector3(x*.36f,-.36f,z*.34f),p.Brass);
            Box(r,"Belt frame",new Vector3(.92f,.13f,.88f),new Vector3(0,.10f,0),p.Dark);
            Box(r,"Moving belt",new Vector3(.68f,.035f,.94f),new Vector3(0,.185f,0),p.Belt);
            for(int z=-1;z<=1;z++) Box(r,"Roller",new Vector3(.78f,.045f,.055f),new Vector3(0,.215f,z*.31f),p.Brass);
            if(splitter)
            {
                Box(r,"Left branch",new Vector3(.44f,.12f,.58f),new Vector3(-.29f,.10f,.22f),p.Dark);
                Box(r,"Branch belt",new Vector3(.48f,.035f,.36f),new Vector3(-.31f,.185f,.25f),p.Belt);
            }
        }

        static void Inserter(Transform r,Palette p)
        {
            for(int x=-1;x<=1;x+=2) Box(r,"Raised inserter support",new Vector3(.07f,.72f,.07f),new Vector3(x*.18f,-.36f,0),p.Brass);
            Cylinder(r,"Turntable",.25f,.10f,new Vector3(0,.10f,0),p.Dark);
            Cylinder(r,"Brass bearing",.13f,.13f,new Vector3(0,.21f,0),p.Brass);
            var pivot=new GameObject("Animated arm").transform; pivot.SetParent(r,false); pivot.localPosition=new Vector3(0,.31f,0);
            Box(pivot,"Arm",new Vector3(.09f,.09f,.72f),new Vector3(0,.16f,.27f),p.Teal);
            Box(pivot,"Grabber",new Vector3(.24f,.08f,.10f),new Vector3(0,.13f,.65f),p.Brass);
        }

        static void Drill(Transform r,Palette p)
        {
            Base2(r,p.Dark); Box(r,"Drill tower",new Vector3(.58f,1.15f,.58f),new Vector3(-.25f,.68f,-.15f),p.Slate);
            Cylinder(r,"Flywheel",.42f,.13f,new Vector3(.22f,.70f,-.12f),p.Brass,Quaternion.Euler(0,0,90));
            Cylinder(r,"Bore",.15f,.92f,new Vector3(.38f,.47f,.42f),p.Iron);
            Cone(r,"Drill bit",.22f,.40f,new Vector3(.38f,.12f,.42f),p.Ore);
            Box(r,"Ore chute",new Vector3(.55f,.18f,.48f),new Vector3(.32f,.25f,.72f),p.Ore);
        }

        static void Furnace(Transform r,Palette p)
        {
            Base2(r,p.Dark); Cylinder(r,"Furnace body",.72f,1.12f,new Vector3(0,.64f,0),p.Slate);
            Cylinder(r,"Chimney",.27f,.92f,new Vector3(-.30f,1.58f,-.12f),p.Dark);
            Box(r,"Fire",new Vector3(.62f,.48f,.06f),new Vector3(0,.52f,.72f),p.Orange);
            Box(r,"Hearth rim",new Vector3(.82f,.10f,.12f),new Vector3(0,.28f,.75f),p.Brass);
        }

        static void Assembler(Transform r,Palette p)
        {
            Base2(r,p.Dark); Box(r,"Assembler cabinet",new Vector3(1.45f,.78f,1.25f),new Vector3(0,.48f,0),p.Violet);
            Box(r,"Top carriage",new Vector3(1.18f,.20f,.42f),new Vector3(0,.98f,-.10f),p.Iron);
            Cylinder(r,"Tool head",.18f,.48f,new Vector3(0,.72f,.27f),p.Brass);
            Box(r,"Work bed",new Vector3(.92f,.09f,.54f),new Vector3(0,.46f,.32f),p.Dark);
            for(int x=-1;x<=1;x+=2) Box(r,"Piston",new Vector3(.15f,.65f,.15f),new Vector3(x*.58f,.74f,-.35f),p.Teal);
        }

        static void Storage(Transform r,Palette p)
        {
            Box(r,"Warehouse",new Vector3(.84f,.72f,.84f),new Vector3(0,.39f,0),p.Blue);
            Box(r,"Band X",new Vector3(.92f,.07f,.92f),new Vector3(0,.31f,0),p.Brass);
            Box(r,"Band Y",new Vector3(.07f,.79f,.92f),new Vector3(0,.39f,0),p.Brass);
        }

        static void Dock(Transform r,Palette p,bool import)
        {
            Base2(r,p.Dark); Box(r,import?"Import gantry":"Export gantry",new Vector3(1.48f,.15f,.24f),new Vector3(0,1.10f,-.30f),import?p.Teal:p.Blue);
            for(int x=-1;x<=1;x+=2) Box(r,"Gantry leg",new Vector3(.16f,1.05f,.16f),new Vector3(x*.66f,.57f,-.30f),p.Brass);
            Box(r,"Freight platform",new Vector3(1.42f,.18f,.92f),new Vector3(0,.18f,.24f),p.Slate);
            for(int x=-1;x<=1;x+=2) Box(r,"Cargo crate",new Vector3(.48f,.45f,.48f),new Vector3(x*.35f,.48f,.22f),import?p.Wood:p.Blue);
        }

        static void PowerInlet(Transform r,Palette p)
        {
            Base2(r,p.Dark); Box(r,"Transformer",new Vector3(1.22f,.91f,.72f),new Vector3(0,.54f,.05f),p.Teal);
            for(int x=-1;x<=1;x+=2) Cylinder(r,"Coil",.21f,.78f,new Vector3(x*.42f,1.12f,.05f),p.Brass);
            Box(r,"Warning plate",new Vector3(.52f,.40f,.04f),new Vector3(0,.62f,.43f),p.Brass);
        }

        static void Pole(Transform r,Palette p)
        {
            Cylinder(r,"Power pole",.075f,1.48f,new Vector3(0,.76f,0),p.Teal);
            Box(r,"Crossbar",new Vector3(.76f,.09f,.09f),new Vector3(0,1.45f,0),p.Brass);
            for(int x=-1;x<=1;x+=2) Cylinder(r,"Insulator",.06f,.16f,new Vector3(x*.29f,1.56f,0),p.White);
        }

        static void Pipe(Transform r,Palette p,bool junction)
        {
            Cylinder(r,"Pipe tube",.13f,.88f,new Vector3(0,.24f,0),p.Iron,Quaternion.Euler(90,0,0));
            for(int z=-1;z<=1;z+=2) Cylinder(r,"Pipe flange",.19f,.07f,new Vector3(0,.24f,z*.43f),p.Brass,Quaternion.Euler(90,0,0));
            if(junction)
            {
                Cylinder(r,"Left branch tube",.13f,.52f,new Vector3(-.25f,.24f,.12f),p.Iron,Quaternion.Euler(0,0,90));
                Cylinder(r,"Left branch flange",.19f,.07f,new Vector3(-.49f,.24f,.12f),p.Brass,Quaternion.Euler(0,0,90));
            }
            FlowPulse(r,p.White,new Vector3(0,.24f,.05f));
            Arrow(r,p.Teal,p.Dark,junction,false);
        }

        static void FluidTank(Transform r,Palette p)
        {
            Cylinder(r,"Tank shell",.40f,.76f,new Vector3(0,.48f,0),p.Slate);
            Cylinder(r,"Tank fluid fill",.065f,.58f,new Vector3(.43f,.39f,0),p.White);
            Cylinder(r,"Tank roof",.43f,.08f,new Vector3(0,.90f,0),p.Dark);
            for(int x=-1;x<=1;x+=2) Box(r,"Tank foot",new Vector3(.12f,.22f,.12f),new Vector3(x*.27f,.11f,0),p.Brass);
            Cylinder(r,"Tank outlet",.11f,.38f,new Vector3(0,.20f,.35f),p.Iron,Quaternion.Euler(90,0,0));
            FlowPulse(r,p.White,new Vector3(0,.20f,.45f));
            Arrow(r,p.Teal,p.Dark,false,false);
        }

        static void WaterPump(Transform r,Palette p)
        {
            Base2(r,p.Dark); Box(r,"Pump housing",new Vector3(.92f,.62f,.72f),new Vector3(0,.43f,.08f),p.Blue);
            Cylinder(r,"Water impeller",.34f,.18f,new Vector3(0,.53f,.48f),p.Teal,Quaternion.Euler(90,0,0));
            Cylinder(r,"Intake well",.30f,.22f,new Vector3(-.48f,.23f,-.38f),p.Iron);
            PipeRun(r,p.Iron,new Vector3(-.48f,.48f,-.30f),Quaternion.Euler(45,0,0));
        }

        static void OilPump(Transform r,Palette p)
        {
            Base2(r,p.Dark); Cylinder(r,"Pumpjack pivot",.12f,.82f,new Vector3(-.42f,.54f,-.18f),p.Brass);
            var arm=new GameObject("Animated arm").transform; arm.SetParent(r,false); arm.localPosition=new Vector3(-.42f,.90f,-.18f);
            Box(arm,"Walking beam",new Vector3(1.38f,.13f,.18f),new Vector3(.40f,0,0),p.Slate);
            Box(arm,"Horse head",new Vector3(.18f,.58f,.28f),new Vector3(1.05f,-.16f,0),p.Dark);
            Box(r,"Counterweight",new Vector3(.38f,.42f,.32f),new Vector3(-.64f,.76f,-.18f),p.Orange);
            Cylinder(r,"Oil well",.24f,.28f,new Vector3(.62f,.23f,-.18f),p.Iron);
        }

        static void Foundry(Transform r,Palette p)
        {
            Base2(r,p.Dark); Cylinder(r,"Foundry crucible",.55f,.88f,new Vector3(-.20f,.57f,0),p.Slate);
            Box(r,"Molten metal",new Vector3(.72f,.08f,.72f),new Vector3(-.20f,1.03f,0),p.Orange);
            Box(r,"Casting bed",new Vector3(.62f,.18f,1.28f),new Vector3(.58f,.28f,.06f),p.Iron);
            Cylinder(r,"Foundry stack",.18f,1.22f,new Vector3(-.63f,.81f,-.58f),p.Dark);
        }

        static void MachiningBench(Transform r,Palette p)
        {
            Base2(r,p.Dark); Box(r,"Machine bed",new Vector3(1.48f,.32f,.76f),new Vector3(0,.34f,0),p.Slate);
            Box(r,"Sliding carriage",new Vector3(.52f,.20f,.68f),new Vector3(-.32f,.58f,0),p.Teal);
            Cylinder(r,"Lathe chuck",.27f,.20f,new Vector3(.48f,.63f,0),p.Brass,Quaternion.Euler(0,0,90));
            Cylinder(r,"Work spindle",.10f,.72f,new Vector3(.06f,.63f,0),p.Steel,Quaternion.Euler(0,0,90));
            Box(r,"Control stand",new Vector3(.28f,.68f,.30f),new Vector3(.66f,.54f,-.54f),p.Blue);
        }

        static void Refinery(Transform r,Palette p)
        {
            Base2(r,p.Dark); Cylinder(r,"Tall distillation column",.28f,1.72f,new Vector3(-.43f,.95f,-.12f),p.Iron);
            Cylinder(r,"Short distillation column",.38f,1.15f,new Vector3(.38f,.66f,.20f),p.Slate);
            for(int y=0;y<4;y++) Cylinder(r,"Column tray",.34f,.045f,new Vector3(-.43f,.38f+y*.39f,-.12f),p.Brass);
            PipeRun(r,p.Teal,new Vector3(0,.78f,-.18f),Quaternion.Euler(0,0,90));
            Box(r,"Refinery furnace",new Vector3(.52f,.52f,.52f),new Vector3(.45f,.36f,-.53f),p.Orange);
        }

        static void ChemicalPlant(Transform r,Palette p)
        {
            Base2(r,p.Dark);
            for(int x=-1;x<=1;x+=2) { Cylinder(r,"Reaction vessel",.38f,.92f,new Vector3(x*.43f,.58f,0),x<0?p.Teal:p.Violet); Cylinder(r,"Vessel cap",.42f,.07f,new Vector3(x*.43f,1.06f,0),p.Brass); }
            PipeRun(r,p.Iron,new Vector3(0,1.13f,0),Quaternion.Euler(0,0,90));
            Box(r,"Chemical control cabinet",new Vector3(.76f,.48f,.24f),new Vector3(0,.43f,-.66f),p.Blue);
        }

        static void Manufacturer(Transform r,Palette p)
        {
            Base2(r,p.Dark); Box(r,"Manufacturer enclosure",new Vector3(1.50f,.78f,1.34f),new Vector3(0,.50f,0),p.Blue);
            Box(r,"Assembly conveyor",new Vector3(.42f,.12f,1.52f),new Vector3(0,.48f,.15f),p.Belt);
            for(int x=-1;x<=1;x+=2)
            {
                Cylinder(r,"Robot pedestal",.15f,.22f,new Vector3(x*.48f,.83f,-.22f),p.Brass);
                var robot=new GameObject("Robot bank").transform; robot.SetParent(r,false); robot.localPosition=new Vector3(x*.48f,.98f,-.22f);
                Box(robot,"Robot upper arm",new Vector3(.13f,.48f,.13f),new Vector3(0,.18f,.10f),p.Teal);
                Box(robot,"Robot forearm",new Vector3(.12f,.12f,.52f),new Vector3(-x*.06f,.37f,.31f),p.Iron);
            }
            Box(r,"Manufacturer status bar",new Vector3(1.16f,.10f,.06f),new Vector3(0,1.00f,.69f),p.Violet);
        }

        static void ItemLift(Transform r,Palette p,bool sender)
        {
            Box(r,"Lift landing",new Vector3(.86f,.10f,.86f),new Vector3(0,.08f,0),p.Dark);
            for(int x=-1;x<=1;x+=2) Box(r,"Lift frame",new Vector3(.08f,1.30f,.08f),new Vector3(x*.36f,.70f,-.34f),p.Brass);
            Box(r,"Lift crossbar",new Vector3(.82f,.08f,.08f),new Vector3(0,1.34f,-.34f),p.Brass);
            Box(r,"Lift cage",new Vector3(.62f,.52f,.58f),new Vector3(0,.40f,.02f),sender?p.Teal:p.Blue);
            if(sender)
            {
                var span=new GameObject("Vertical link span").transform; span.SetParent(r,false);
                for(int x=-1;x<=1;x+=2) Box(span,"Vertical lift rail",new Vector3(.07f,3.10f,.07f),new Vector3(x*.34f,1.55f,-.32f),p.Iron);
                Box(span,"Moving lift cargo",new Vector3(.34f,.34f,.34f),new Vector3(0,.35f,.02f),p.White).SetActive(false);
                Box(r,"Vertical direction stem",new Vector3(.08f,.42f,.08f),new Vector3(0,.92f,.38f),p.Teal);
                var up=Box(r,"Vertical direction head",new Vector3(.22f,.22f,.22f),new Vector3(0,1.18f,.38f),p.Teal); up.transform.localRotation=Quaternion.Euler(0,45,0);
            }
            else Arrow(r,p.Teal,p.Dark,false,false);
        }

        static void FluidRiser(Transform r,Palette p,bool sender)
        {
            Cylinder(r,"Riser landing flange",.32f,.10f,new Vector3(0,.12f,0),p.Brass);
            Cylinder(r,"Riser elbow",.15f,.70f,new Vector3(0,.40f,.18f),p.Iron,Quaternion.Euler(90,0,0));
            if(sender)
            {
                var span=new GameObject("Vertical link span").transform; span.SetParent(r,false);
                Cylinder(span,"Vertical riser pipe",.14f,3.10f,new Vector3(0,1.55f,0),p.Iron);
                for(int y=0;y<=3;y++) Cylinder(span,"Riser coupling",.21f,.07f,new Vector3(0,y*1.03f,0),p.Brass);
                Cylinder(span,"Vertical fluid pulse",.09f,.12f,new Vector3(0,.25f,0),p.White);
            }
            if(!sender) Arrow(r,p.Teal,p.Dark,false,false);
        }

        static void FlowPulse(Transform r,Material material,Vector3 position)
        {
            var pulse=Cylinder(r,"Fluid flow pulse",.075f,.10f,position,material,Quaternion.Euler(90,0,0));
            pulse.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
        }

        static void PipeRun(Transform r,Material material,Vector3 position,Quaternion rotation)
        { Cylinder(r,"Process pipe",.09f,.92f,position,material,rotation); }

        static void Base2(Transform r,Material m) { Box(r,"Machine foundation",new Vector3(1.82f,.16f,1.82f),new Vector3(0,.09f,0),m); }
        static void Arrow(Transform r,Material bright,Material backing,bool branch,bool large)
        {
            float edge=large?1.02f:.48f,y=large?.245f:.285f;
            Box(r,"Output port backing",new Vector3(.42f,.028f,.22f),new Vector3(0,y-.014f,edge),backing);
            Box(r,"Direction stem backing",new Vector3(.15f,.026f,.38f),new Vector3(0,y,edge-.20f),backing);
            var headBacking=Box(r,"Direction head backing",new Vector3(.34f,.026f,.34f),new Vector3(0,y,edge+.04f),backing); headBacking.transform.localRotation=Quaternion.Euler(0,45,0);
            Box(r,"Direction stem",new Vector3(.08f,.020f,.32f),new Vector3(0,y+.018f,edge-.20f),bright);
            var head=Box(r,"Direction head",new Vector3(.24f,.020f,.24f),new Vector3(0,y+.018f,edge+.04f),bright); head.transform.localRotation=Quaternion.Euler(0,45,0);
            if(branch)
            {
                var branchBacking=Box(r,"Branch arrow backing",new Vector3(.28f,.026f,.28f),new Vector3(-.34f,y,edge-.10f),backing); branchBacking.transform.localRotation=Quaternion.Euler(0,45,0);
                var h=Box(r,"Branch arrow",new Vector3(.19f,.020f,.19f),new Vector3(-.34f,y+.018f,edge-.10f),bright); h.transform.localRotation=Quaternion.Euler(0,45,0);
            }
        }

        public static GameObject Cargo(Transform parent,Material material)
        {
            var root=new GameObject("Actual cargo"); root.transform.SetParent(parent,false);
            Box(root.transform,"Item",new Vector3(.30f,.30f,.30f),new Vector3(0,.09f,0),material);
            var marker=Box(root.transform,"High contrast cargo marker",new Vector3(.38f,.045f,.38f),new Vector3(0,-.085f,0),material);
            marker.transform.localRotation=Quaternion.Euler(0,45,0);
            return root;
        }

        static GameObject Box(Transform parent,string name,Vector3 scale,Vector3 position,Material material)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube); o.name=name; o.transform.SetParent(parent,false); o.transform.localPosition=position; o.transform.localScale=scale; o.GetComponent<MeshRenderer>().sharedMaterial=material; Strip(o); return o;
        }

        static GameObject Cylinder(Transform parent,string name,float radius,float height,Vector3 position,Material material,Quaternion rotation=default(Quaternion))
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cylinder); o.name=name; o.transform.SetParent(parent,false); o.transform.localPosition=position; o.transform.localScale=new Vector3(radius,height*.5f,radius); o.transform.localRotation=rotation==default(Quaternion)?Quaternion.identity:rotation; o.GetComponent<MeshRenderer>().sharedMaterial=material; Strip(o); return o;
        }

        static void Cone(Transform parent,string name,float radius,float height,Vector3 position,Material material)
        {
            // A compact tapered silhouette without importing a mesh.
            var root=new GameObject(name).transform; root.SetParent(parent,false); root.localPosition=position;
            for(int i=0;i<3;i++) Cylinder(root,"Bit section",radius*(1-i*.25f),height/3,new Vector3(0,(i-1)*height/3,0),material);
        }

        static void Strip(GameObject o)
        {
            var c=o.GetComponent<Collider>(); if(c) { c.enabled=false; Object.Destroy(c); }
            var renderer=o.GetComponent<Renderer>();
            if(renderer)
            {
                Vector3 scale=o.transform.localScale; bool tiny=Mathf.Max(scale.x,Mathf.Max(scale.y,scale.z))<=.5f;
                renderer.shadowCastingMode=Application.isMobilePlatform&&tiny?ShadowCastingMode.Off:ShadowCastingMode.On;
                renderer.receiveShadows=!(Application.isMobilePlatform&&tiny);
            }
        }
    }
}

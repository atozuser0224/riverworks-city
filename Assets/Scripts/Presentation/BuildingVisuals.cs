using UnityEngine;

namespace Riverworks
{
    /// <summary>Procedural, collider-free tabletop miniatures used by the map renderer.</summary>
    public static class BuildingVisuals
    {
        static Material ivory, warmIvory, terracotta, coral, mint, darkMint, indigo, brass, glass, wood, stone, soil, grain, white;
        static Material thatch, plaster, brick, slate, iron;
        static Mesh cone8, pyramid4;

        static Material Mat(ref Material slot, string name, Color color, float smooth = .18f, float metallic = 0f)
        {
            if (slot) return slot;
            slot = new Material(Shader.Find("Standard")) { name = "Riverworks " + name, color = color };
            slot.SetFloat("_Glossiness", smooth);
            slot.SetFloat("_Metallic", metallic);
            return slot;
        }

        static Material Ivory => Mat(ref ivory, "Ivory", new Color(.91f, .84f, .69f), .16f);
        static Material WarmIvory => Mat(ref warmIvory, "Warm Ivory", new Color(.72f, .59f, .43f), .12f);
        static Material Terra => Mat(ref terracotta, "Terracotta", new Color(.67f, .22f, .16f), .2f);
        static Material Coral => Mat(ref coral, "Coral", new Color(.91f, .36f, .28f), .22f);
        static Material Mint => Mat(ref mint, "Mint", new Color(.34f, .65f, .50f), .16f);
        static Material DarkMint => Mat(ref darkMint, "Dark Mint", new Color(.12f, .36f, .30f), .18f);
        static Material Indigo => Mat(ref indigo, "Smoked Indigo", new Color(.16f, .20f, .29f), .26f, .05f);
        static Material Brass => Mat(ref brass, "Brass", new Color(.78f, .55f, .19f), .42f, .55f);
        static Material Glass => Mat(ref glass, "Window", new Color(.20f, .43f, .48f), .65f, .08f);
        static Material Wood => Mat(ref wood, "Wood", new Color(.34f, .18f, .10f), .12f);
        static Material Stone => Mat(ref stone, "Stone", new Color(.40f, .43f, .44f), .1f);
        static Material Soil => Mat(ref soil, "Soil", new Color(.29f, .18f, .12f), .08f);
        static Material Grain => Mat(ref grain, "Grain", new Color(.90f, .67f, .20f), .12f);
        static Material White => Mat(ref white, "Canvas", new Color(.94f, .90f, .79f), .12f);
        static Material Thatch => Mat(ref thatch, "Thatch", new Color(.64f, .46f, .20f), .05f);
        static Material Plaster => Mat(ref plaster, "Lime Plaster", new Color(.88f, .83f, .70f), .14f);
        static Material Brick => Mat(ref brick, "Fired Brick", new Color(.48f, .16f, .11f), .10f);
        static Material Slate => Mat(ref slate, "Slate", new Color(.18f, .22f, .24f), .16f);
        static Material Iron => Mat(ref iron, "Black Iron", new Color(.12f, .13f, .14f), .26f, .42f);

        public static GameObject Create(BuildingKind kind, Transform parent, int level = 1, Era era = Era.Medieval)
        {
            var root = new GameObject(kind + " Miniature");
            root.transform.SetParent(parent, false);
            level = Mathf.Clamp(level, 1, 3);
            switch (kind)
            {
                case BuildingKind.TownHall: TownHall(root.transform, level, era); break;
                case BuildingKind.Road: Road(root.transform); break;
                case BuildingKind.House: House(root.transform, level, era); break;
                case BuildingKind.Lumberyard: Lumberyard(root.transform, level, era); break;
                case BuildingKind.Quarry: Quarry(root.transform, level); break;
                case BuildingKind.Farm: Farm(root.transform, level, era); break;
                case BuildingKind.Mill: Mill(root.transform, level, era); break;
                case BuildingKind.Bakery: Bakery(root.transform, level, era); break;
                case BuildingKind.Mine: Mine(root.transform, level); break;
                case BuildingKind.Smelter: Smelter(root.transform, level, era); break;
                case BuildingKind.Workshop: Workshop(root.transform, level, era); break;
                case BuildingKind.Windmill: Windmill(root.transform, level, era); break;
                case BuildingKind.Park: Park(root.transform, level); break;
                case BuildingKind.Warehouse: Warehouse(root.transform, level, era); break;
                case BuildingKind.Market: Market(root.transform, level, era); break;
                case BuildingKind.StudyHouse: StudyHouse(root.transform, level, era); break;
                case BuildingKind.Academy: Academy(root.transform, level, era); break;
                case BuildingKind.SteamPlant: SteamPlant(root.transform, level); break;
                default: Box(root.transform, "Plot Marker", new Vector3(.20f,.06f,.20f), new Vector3(0,.03f,0), Brass); break;
            }
            if(kind != BuildingKind.None && kind != BuildingKind.Road) UpgradeMarker(root.transform, level);
            return root;
        }

        public static GameObject CreateTree(Transform parent, int seed = 0)
        {
            var root = new GameObject("Mint Tree"); root.transform.SetParent(parent, false);
            var r = new System.Random(seed * 397 ^ 0x51D);
            float s = .86f + (float)r.NextDouble() * .22f;
            Cyl(root.transform,"Trunk",.045f,.055f,.33f,new Vector3(0,.165f,0),Wood,7);
            Ball(root.transform,"Crown",new Vector3(.29f,.30f,.27f)*s,new Vector3(0,.48f,0),Mint);
            Ball(root.transform,"Crown Shade",new Vector3(.20f,.22f,.20f)*s,new Vector3(-.10f,.41f,.02f),DarkMint);
            Ball(root.transform,"Crown Light",new Vector3(.17f,.18f,.17f)*s,new Vector3(.10f,.57f,-.03f),Mint);
            return root;
        }

        public static GameObject CreateRock(Transform parent, int seed = 0)
        {
            var root = new GameObject("Quarry Rock"); root.transform.SetParent(parent, false);
            var r = new System.Random(seed * 733 ^ 0xA71);
            for (int i=0;i<3;i++) {
                float x=(float)(r.NextDouble()-.5)*.22f, z=(float)(r.NextDouble()-.5)*.18f;
                var o=Ball(root.transform,"Faceted Rock",new Vector3(.23f-i*.035f,.18f+i*.025f,.20f-i*.02f),new Vector3(x,.08f+i*.025f,z),Stone);
                o.transform.localRotation=Quaternion.Euler((float)r.NextDouble()*18f,(float)r.NextDouble()*180f,(float)r.NextDouble()*12f);
            }
            return root;
        }

        static void TownHall(Transform p,int lv,Era era) {
            if (era == Era.Medieval) { MedievalTownHall(p,lv); return; }
            Box(p,"Foundation",new Vector3(.69f,.07f,.57f),new Vector3(0,.035f,.02f),Stone);
            Box(p,era==Era.Industrial?"Brick Municipal Hall":"Plastered Civic Hall",new Vector3(.58f,.48f,.43f),new Vector3(0,.31f,.02f),era==Era.Industrial?Brick:Plaster);
            Box(p,"Front Portico",new Vector3(.28f,.35f,.10f),new Vector3(0,.25f,-.245f),WarmIvory);
            for(int i=-1;i<=1;i+=2) Cyl(p,"Column",.022f,.028f,.31f,new Vector3(i*.105f,.22f,-.31f),White,8);
            Door(p,new Vector3(0,.18f,-.302f),new Vector3(.095f,.23f,.014f));
            Window(p,new Vector3(-.18f,.34f,-.202f)); Window(p,new Vector3(.18f,.34f,-.202f));
            HipRoof(p,new Vector3(0,.61f,.02f),new Vector3(.68f,.23f,.53f),era==Era.Industrial?Slate:Terra);
            Box(p,"Clock Tower",new Vector3(.20f,.25f,.19f),new Vector3(0,.79f,.02f),era==Era.Industrial?Brick:Plaster);
            Disk(p,"Clock",.062f,.014f,new Vector3(0,.82f,-.083f),new Vector3(90,0,0),White,16);
            Disk(p,"Clock Rim",.071f,.008f,new Vector3(0,.82f,-.092f),new Vector3(90,0,0),Brass,16);
            Spire(p,new Vector3(0,1.00f,.02f),.16f,.20f,Terra); Box(p,"Flag",new Vector3(.12f,.065f,.008f),new Vector3(.06f,1.19f,.02f),Coral);
            LevelCrates(p,lv,new Vector3(.30f,.10f,.23f));
        }

        static void MedievalTownHall(Transform p,int lv) {
            Box(p,"Rough Stone Footing",new Vector3(.72f,.10f,.58f),new Vector3(0,.05f,.02f),Stone);
            Box(p,"Great Timber Hall",new Vector3(.58f,.45f,.43f),new Vector3(0,.31f,.02f),Ivory);
            for(int i=-2;i<=2;i++) Box(p,"Oak Upright",new Vector3(.035f,.46f,.035f),new Vector3(i*.13f,.32f,-.205f),Wood);
            for(int i=-1;i<=1;i+=2) { var brace=Box(p,"Cross Brace",new Vector3(.035f,.28f,.038f),new Vector3(i*.195f,.32f,-.224f),Wood); brace.transform.localRotation=Quaternion.Euler(0,0,i*36f); }
            Box(p,"Oak Beam",new Vector3(.62f,.04f,.04f),new Vector3(0,.29f,-.22f),Wood);
            Box(p,"Carved Porch",new Vector3(.25f,.32f,.12f),new Vector3(0,.22f,-.25f),Wood);
            Door(p,new Vector3(0,.17f,-.318f),new Vector3(.10f,.24f,.018f));
            GableRoof(p,new Vector3(0,.60f,.02f),new Vector3(.72f,.25f,.55f),Thatch);
            Box(p,"Bell Loft",new Vector3(.18f,.22f,.17f),new Vector3(0,.78f,.02f),Wood);
            Spire(p,new Vector3(0,.96f,.02f),.13f,.19f,Slate);
            Cyl(p,"Council Bell",.045f,.065f,.09f,new Vector3(0,.80f,-.08f),Brass,10);
            Box(p,"Guild Pennant",new Vector3(.12f,.07f,.01f),new Vector3(.06f,1.10f,.02f),Coral);
            LevelCrates(p,lv,new Vector3(.30f,.10f,.23f));
        }

        static void Road(Transform p) {
            Box(p,"Cobble",new Vector3(.82f,.035f,.50f),new Vector3(0,.018f,0),Stone);
            for(int i=-3;i<=3;i++) Box(p,"Paving Seam",new Vector3(.008f,.007f,.46f),new Vector3(i*.105f,.039f,0),WarmIvory);
            for(int i=-2;i<=2;i++) Box(p,"Center Stone",new Vector3(.075f,.009f,.025f),new Vector3(i*.15f,.041f,0),Ivory);
        }

        static void House(Transform p,int lv,Era era) {
            var wall=era==Era.Industrial?Brick:era==Era.Renaissance?Plaster:Ivory;
            var roof=era==Era.Medieval?Thatch:era==Era.Renaissance?Terra:Slate;
            Box(p,era==Era.Medieval?"Wattle Cottage":era==Era.Renaissance?"Plaster Townhouse":"Brick Row House",new Vector3(.48f,.40f,.40f),new Vector3(0,.25f,.03f),wall);
            if(era==Era.Medieval) HalfTimberFront(p,new Vector3(0,.25f,-.18f),.46f,.39f);
            else if(era==Era.Renaissance) { Box(p,"Stone String Course",new Vector3(.50f,.035f,.025f),new Vector3(0,.28f,-.185f),Stone); Box(p,"Plaster Quoin",new Vector3(.045f,.39f,.03f),new Vector3(-.215f,.25f,-.19f),Stone); }
            else for(int i=-1;i<=1;i++) Box(p,"Brick Course",new Vector3(.46f,.012f,.015f),new Vector3(0,.14f+i*.10f,-.185f),WarmIvory);
            GableRoof(p,new Vector3(0,.55f,.03f),new Vector3(.58f,.18f,.50f),roof);
            Box(p,era==Era.Industrial?"Brick Chimney":"Hearth Chimney",new Vector3(.07f,.25f,.07f),new Vector3(.15f,.66f,.10f),era==Era.Industrial?Brick:Stone);
            Door(p,new Vector3(.08f,.16f,-.178f),new Vector3(.09f,.22f,.014f));
            Window(p,new Vector3(-.12f,.27f,-.178f));
            Box(p,era==Era.Medieval?"Herb Box":"Flower Box",new Vector3(.12f,.035f,.04f),new Vector3(-.12f,.19f,-.21f),era==Era.Medieval?Mint:Coral);
            Box(p,"Side Wing",new Vector3(.20f,.27f,.27f),new Vector3(-.30f,.18f,.08f),lv>1?wall:WarmIvory);
            if(lv>1) GableRoof(p,new Vector3(-.30f,.34f,.08f),new Vector3(.25f,.11f,.33f),roof);
            if(lv>2) Box(p,"Dormer",new Vector3(.13f,.13f,.11f),new Vector3(.08f,.65f,-.10f),wall);
        }

        static void HalfTimberFront(Transform p,Vector3 center,float width,float height) {
            for(int i=-1;i<=1;i++) Box(p,"Dark Oak Upright",new Vector3(.027f,height,.025f),center+new Vector3(i*width*.43f,0,-.018f),Wood);
            Box(p,"Dark Oak Beam",new Vector3(width,.028f,.026f),center+new Vector3(0,.04f,-.019f),Wood);
            for(int i=-1;i<=1;i+=2) { var b=Box(p,"Dark Oak Brace",new Vector3(.025f,height*.56f,.027f),center+new Vector3(i*width*.21f,.02f,-.022f),Wood); b.transform.localRotation=Quaternion.Euler(0,0,i*38f); }
        }

        static void Lumberyard(Transform p,int lv,Era era) {
            Box(p,"Sawmill",new Vector3(.42f,.35f,.36f),new Vector3(.13f,.22f,.08f),era==Era.Industrial?Brick:Ivory); GableRoof(p,new Vector3(.13f,.44f,.08f),new Vector3(.50f,.14f,.44f),era==Era.Medieval?Thatch:era==Era.Renaissance?Terra:Slate);
            Box(p,"Open Shed Roof",new Vector3(.34f,.045f,.33f),new Vector3(-.25f,.34f,.06f),Terra);
            for(int i=0;i<3;i++){ float z=-.18f+i*.12f; Cyl(p,"Log",.045f,.045f,.38f,new Vector3(-.22f,.08f,z),Wood,8,new Vector3(0,0,90)); }
            Disk(p,"Saw Blade",.11f,.016f,new Vector3(.13f,.25f,-.11f),new Vector3(90,0,0),Brass,16);
            Box(p,"Saw Slot",new Vector3(.018f,.12f,.018f),new Vector3(.13f,.25f,-.124f),Indigo);
            LevelCrates(p,lv,new Vector3(.29f,.08f,.26f));
        }

        static void Quarry(Transform p,int lv) {
            Box(p,"Cut Stone Yard",new Vector3(.75f,.045f,.68f),new Vector3(0,.023f,.02f),WarmIvory);
            for(int i=0;i<4;i++){ var b=Box(p,"Stone Block",new Vector3(.17f,.10f,.14f),new Vector3(-.22f+(i%2)*.19f,.08f+(i/2)*.10f,.12f),Stone); b.transform.localRotation=Quaternion.Euler(0,(i*11)%20,0); }
            Box(p,"Crane Mast",new Vector3(.045f,.58f,.045f),new Vector3(.27f,.31f,.12f),Wood); Box(p,"Crane Arm",new Vector3(.42f,.04f,.04f),new Vector3(.09f,.57f,.12f),Wood);
            Box(p,"Cable",new Vector3(.012f,.28f,.012f),new Vector3(-.095f,.43f,.12f),Indigo); Box(p,"Hook",new Vector3(.055f,.055f,.035f),new Vector3(-.095f,.28f,.12f),Brass);
            CreateRock(p,37+lv).transform.localPosition=new Vector3(.18f,0,-.20f);
        }

        static void Farm(Transform p,int lv,Era era) {
            Box(p,"Soil",new Vector3(.78f,.035f,.68f),new Vector3(0,.018f,.04f),Soil);
            for(int row=0;row<5;row++) for(int i=0;i<5;i++){
                float x=-.30f+row*.15f,z=-.21f+i*.105f;
                Box(p,"Crop",new Vector3(.025f,.10f,.025f),new Vector3(x,.07f,z),row%2==0?Grain:Mint);
            }
            Box(p,"Farm Shed",new Vector3(.24f,.22f,.22f),new Vector3(.23f,.15f,.18f),era==Era.Industrial?Brick:Ivory); GableRoof(p,new Vector3(.23f,.28f,.18f),new Vector3(.29f,.10f,.27f),era==Era.Medieval?Thatch:era==Era.Renaissance?Terra:Slate);
            Box(p,"Fence",new Vector3(.72f,.035f,.025f),new Vector3(0,.11f,.38f),Wood);
            if(lv>1) Cyl(p,"Silo",.09f,.09f,.28f,new Vector3(.27f,.16f,-.18f),Indigo,12); if(lv>1) Spire(p,new Vector3(.27f,.34f,-.18f),.105f,.10f,Terra);
        }

        static void Mill(Transform p,int lv,Era era) {
            Box(p,"Mill",new Vector3(.49f,.52f,.42f),new Vector3(0,.30f,.06f),era==Era.Industrial?Brick:era==Era.Renaissance?Plaster:Ivory); GableRoof(p,new Vector3(0,.61f,.06f),new Vector3(.58f,.17f,.50f),era==Era.Medieval?Thatch:era==Era.Renaissance?Terra:Slate);
            Door(p,new Vector3(.10f,.16f,-.158f),new Vector3(.10f,.22f,.014f)); Window(p,new Vector3(-.12f,.36f,-.158f));
            var wheel=new GameObject("Turning Mill Wheel"); wheel.transform.SetParent(p,false); wheel.transform.localPosition=new Vector3(-.32f,.20f,.07f);
            Disk(wheel.transform,"Wheel Rim",.18f,.055f,new Vector3(.035f,0,0),new Vector3(0,0,90),Wood,12); Disk(wheel.transform,"Wheel Hub",.045f,.07f,Vector3.zero,new Vector3(0,0,90),Brass,12);
            for(int i=0;i<6;i++){ var s=Box(wheel.transform,"Wheel Spoke",new Vector3(.025f,.30f,.025f),Vector3.zero,Wood); s.transform.localRotation=Quaternion.Euler(i*30f,0,0); }
            var wheelSpin=wheel.AddComponent<MiniatureSpinner>(); wheelSpin.axis=Vector3.right; wheelSpin.degreesPerSecond=11f+lv*3f;
            LevelCrates(p,lv,new Vector3(.27f,.08f,.25f));
        }

        static void Bakery(Transform p,int lv,Era era) {
            Box(p,"Bakery",new Vector3(.55f,.42f,.40f),new Vector3(0,.25f,.04f),era==Era.Industrial?Brick:era==Era.Renaissance?Plaster:Ivory); HipRoof(p,new Vector3(0,.52f,.04f),new Vector3(.64f,.20f,.49f),era==Era.Medieval?Thatch:era==Era.Renaissance?Terra:Slate);
            Box(p,"Oven Chimney",new Vector3(.10f,.35f,.10f),new Vector3(.18f,.67f,.10f),WarmIvory); Box(p,"Chimney Cap",new Vector3(.14f,.04f,.14f),new Vector3(.18f,.86f,.10f),Indigo);
            Door(p,new Vector3(.13f,.16f,-.168f),new Vector3(.10f,.23f,.014f)); Window(p,new Vector3(-.13f,.29f,-.168f));
            Awning(p,new Vector3(-.13f,.40f,-.245f),.25f,Coral); Box(p,"Bread Sign",new Vector3(.13f,.09f,.02f),new Vector3(.23f,.41f,-.225f),Brass);
            if(lv>1) Box(p,"Cafe Table",new Vector3(.16f,.025f,.16f),new Vector3(-.30f,.15f,-.25f),Wood);
        }

        static void Mine(Transform p,int lv) {
            Box(p,"Rock Face",new Vector3(.67f,.42f,.34f),new Vector3(0,.22f,.12f),Stone); Spire(p,new Vector3(-.18f,.49f,.12f),.22f,.28f,Stone); Spire(p,new Vector3(.18f,.49f,.12f),.25f,.32f,Stone);
            Box(p,"Mine Mouth",new Vector3(.28f,.30f,.035f),new Vector3(0,.17f,-.068f),Indigo); Spire(p,new Vector3(0,.34f,-.068f),.14f,.14f,Indigo);
            for(int i=-1;i<=1;i+=2){ Box(p,"Timber Post",new Vector3(.035f,.32f,.035f),new Vector3(i*.135f,.18f,-.09f),Wood); }
            Box(p,"Lintel",new Vector3(.32f,.035f,.045f),new Vector3(0,.34f,-.09f),Wood);
            for(int i=0;i<4;i++) Box(p,"Rail",new Vector3(.025f,.02f,.42f),new Vector3(-.07f+i%2*.14f,.02f,-.20f),Brass);
            Box(p,"Ore Cart",new Vector3(.22f,.13f,.18f),new Vector3(.25f,.10f,-.21f),Indigo); if(lv>1) LevelCrates(p,lv,new Vector3(-.28f,.08f,-.20f));
        }

        static void Smelter(Transform p,int lv,Era era) {
            if(era==Era.Medieval) { MedievalForge(p,lv); return; }
            Box(p,"Smelter Hall",new Vector3(.56f,.38f,.42f),new Vector3(0,.22f,.06f),WarmIvory); GableRoof(p,new Vector3(0,.46f,.06f),new Vector3(.64f,.16f,.50f),Indigo);
            Cyl(p,"Furnace",.14f,.17f,.38f,new Vector3(-.15f,.24f,-.13f),Indigo,12); Cyl(p,"Furnace Ring",.18f,.18f,.05f,new Vector3(-.15f,.25f,-.13f),Brass,12);
            Box(p,"Fire",new Vector3(.12f,.12f,.02f),new Vector3(-.15f,.17f,-.322f),Coral);
            Cyl(p,era==Era.Industrial?"Brick Smoke Stack":"Stone Furnace Flue",.06f,.085f,.58f,new Vector3(.19f,.56f,.10f),era==Era.Industrial?Brick:Stone,12); Cyl(p,"Stack Rim",.09f,.09f,.05f,new Vector3(.19f,.87f,.10f),era==Era.Industrial?Iron:Brass,12);
            if(lv>1) Cyl(p,"Tank",.10f,.10f,.26f,new Vector3(.30f,.16f,-.19f),Indigo,12);
        }

        static void Workshop(Transform p,int lv,Era era) {
            if(era==Era.Medieval) { MedievalWorkshop(p,lv); return; }
            Box(p,era==Era.Industrial?"Brick Machine Shop":"Guild Workshop",new Vector3(.57f,.43f,.42f),new Vector3(0,.25f,.05f),era==Era.Industrial?Brick:Plaster);
            if(era==Era.Industrial) SawRoof(p,new Vector3(0,.52f,.05f),new Vector3(.64f,.16f,.49f)); else GableRoof(p,new Vector3(0,.52f,.05f),new Vector3(.64f,.16f,.49f),Terra);
            Box(p,"Wide Door",new Vector3(.22f,.27f,.018f),new Vector3(.11f,.17f,-.17f),Wood); Window(p,new Vector3(-.17f,.30f,-.17f));
            var largeGear=Gear(p,new Vector3(-.20f,.61f,.04f),.09f); var largeSpin=largeGear.AddComponent<MiniatureSpinner>(); largeSpin.degreesPerSecond=16f;
            var smallGear=Gear(p,new Vector3(-.05f,.62f,.04f),.065f); var smallSpin=smallGear.AddComponent<MiniatureSpinner>(); smallSpin.degreesPerSecond=-23f;
            Box(p,"Work Awning",new Vector3(.28f,.035f,.18f),new Vector3(.24f,.35f,-.25f),Coral).transform.localRotation=Quaternion.Euler(12,0,0);
            if(lv>1) Cyl(p,"Roof Tank",.09f,.09f,.17f,new Vector3(.22f,.68f,.08f),Brass,12);
        }

        static void Windmill(Transform p,int lv,Era era) {
            Cyl(p,era==Era.Medieval?"Rough Stone Tower":era==Era.Renaissance?"Plastered Tower":"Brick Mill Tower",.22f,.30f,.67f,new Vector3(0,.345f,.05f),era==Era.Medieval?Stone:era==Era.Renaissance?Plaster:Brick,12); Spire(p,new Vector3(0,.76f,.05f),.25f,.22f,era==Era.Industrial?Slate:Thatch);
            Door(p,new Vector3(0,.15f,-.258f),new Vector3(.09f,.22f,.015f)); Window(p,new Vector3(0,.44f,-.258f));
            var rotor=new GameObject("Turning Sails"); rotor.transform.SetParent(p,false); rotor.transform.localPosition=new Vector3(0,.76f,-.285f);
            Disk(rotor.transform,"Hub",.055f,.07f,Vector3.zero,new Vector3(90,0,0),Brass,12);
            for(int i=0;i<4;i++){ float angle=i*Mathf.PI*.5f; var arm=Box(rotor.transform,era==Era.Medieval?"Cloth Sail":"Shutter Sail",new Vector3(.065f,.42f,.022f),new Vector3(-Mathf.Sin(angle)*.20f,Mathf.Cos(angle)*.20f,0),era==Era.Medieval?White:era==Era.Renaissance?Coral:Iron); arm.transform.localRotation=Quaternion.Euler(0,0,i*90f); }
            rotor.AddComponent<MiniatureSpinner>().degreesPerSecond = 18f + lv*5f;
            if(lv>1) LevelCrates(p,lv,new Vector3(.29f,.08f,.22f));
        }

        static void MedievalForge(Transform p,int lv) {
            Box(p,"Open Timber Forge",new Vector3(.52f,.27f,.38f),new Vector3(0,.17f,.06f),Ivory);
            GableRoof(p,new Vector3(0,.36f,.06f),new Vector3(.62f,.17f,.48f),Thatch);
            Box(p,"Stone Hearth",new Vector3(.24f,.18f,.18f),new Vector3(-.12f,.12f,-.19f),Stone);
            Box(p,"Forge Fire",new Vector3(.13f,.08f,.025f),new Vector3(-.12f,.14f,-.29f),Coral);
            Cyl(p,"Short Clay Flue",.06f,.09f,.32f,new Vector3(-.12f,.49f,.02f),Stone,10);
            Box(p,"Anvil",new Vector3(.16f,.055f,.08f),new Vector3(.20f,.18f,-.20f),Iron);
            Box(p,"Anvil Stand",new Vector3(.07f,.15f,.07f),new Vector3(.20f,.09f,-.20f),Wood);
            Box(p,"Hand Bellows",new Vector3(.18f,.06f,.10f),new Vector3(.20f,.11f,.18f),Wood).transform.localRotation=Quaternion.Euler(0,20,0);
            LevelCrates(p,lv,new Vector3(.28f,.08f,.23f));
        }

        static void MedievalWorkshop(Transform p,int lv) {
            Box(p,"Craft Guild",new Vector3(.56f,.39f,.42f),new Vector3(0,.23f,.05f),Ivory);
            HalfTimberFront(p,new Vector3(0,.23f,-.17f),.53f,.37f);
            GableRoof(p,new Vector3(0,.49f,.05f),new Vector3(.65f,.18f,.50f),Thatch);
            Box(p,"Open Workbench",new Vector3(.27f,.04f,.14f),new Vector3(.20f,.18f,-.25f),Wood);
            var grind=new GameObject("Hand Cranked Grindstone"); grind.transform.SetParent(p,false); grind.transform.localPosition=new Vector3(-.22f,.20f,-.24f);
            Disk(grind.transform,"Stone Wheel",.10f,.05f,Vector3.zero,new Vector3(0,90,0),Stone,12);
            Box(grind.transform,"Wooden Crank",new Vector3(.13f,.018f,.018f),new Vector3(.06f,0,-.035f),Wood);
            grind.AddComponent<MiniatureSpinner>().axis=Vector3.right;
            LevelCrates(p,lv,new Vector3(.28f,.08f,.23f));
        }

        static void Park(Transform p,int lv) {
            Box(p,"Park Lawn",new Vector3(.78f,.025f,.70f),new Vector3(0,.013f,.02f),Mint);
            Box(p,"Cross Path",new Vector3(.13f,.012f,.68f),new Vector3(0,.032f,.02f),WarmIvory); Box(p,"Cross Path",new Vector3(.72f,.012f,.13f),new Vector3(0,.033f,.02f),WarmIvory);
            Cyl(p,"Fountain Basin",.16f,.16f,.07f,new Vector3(0,.07f,.02f),Stone,16); Cyl(p,"Water",.125f,.125f,.012f,new Vector3(0,.112f,.02f),Glass,16); Cyl(p,"Fountain",.025f,.035f,.20f,new Vector3(0,.20f,.02f),Brass,10);
            var t1=CreateTree(p,41+lv); t1.transform.localPosition=new Vector3(-.26f,0,.22f); t1.transform.localScale=Vector3.one*.65f;
            var t2=CreateTree(p,81+lv); t2.transform.localPosition=new Vector3(.27f,0,-.20f); t2.transform.localScale=Vector3.one*.55f;
            Box(p,"Bench",new Vector3(.23f,.04f,.055f),new Vector3(-.24f,.13f,-.18f),Wood); Box(p,"Bench Back",new Vector3(.23f,.12f,.025f),new Vector3(-.24f,.19f,-.15f),Wood);
        }

        static void Warehouse(Transform p,int lv,Era era) {
            Box(p,"Warehouse",new Vector3(.68f,.42f,.50f),new Vector3(0,.24f,.05f),era==Era.Industrial?Brick:WarmIvory); GableRoof(p,new Vector3(0,.50f,.05f),new Vector3(.76f,.15f,.58f),era==Era.Medieval?Thatch:era==Era.Renaissance?Terra:Slate);
            Box(p,"Loading Door",new Vector3(.25f,.28f,.02f),new Vector3(.10f,.17f,-.211f),Indigo);
            for(int i=0;i<3;i++) Box(p,"Door Slat",new Vector3(.22f,.012f,.008f),new Vector3(.10f,.10f+i*.07f,-.225f),Brass);
            Awning(p,new Vector3(.10f,.37f,-.29f),.34f,Coral); Window(p,new Vector3(-.22f,.30f,-.211f));
            LevelCrates(p,Mathf.Max(2,lv),new Vector3(-.30f,.08f,-.25f));
        }

        static void Market(Transform p,int lv,Era era) {
            Box(p,"Market Hall",new Vector3(.42f,.32f,.34f),new Vector3(0,.20f,.11f),Ivory); HipRoof(p,new Vector3(0,.41f,.11f),new Vector3(.50f,.16f,.42f),Terra);
            for(int side=-1;side<=1;side+=2){ float x=side*.26f; Box(p,"Stall Counter",new Vector3(.22f,.08f,.12f),new Vector3(x,.12f,-.20f),Wood); Awning(p,new Vector3(x,.30f,-.24f),.25f,side<0?Coral:Mint); }
            Door(p,new Vector3(0,.14f,-.068f),new Vector3(.09f,.20f,.014f));
            Box(p,"Market Sign",new Vector3(.22f,.10f,.025f),new Vector3(0,.52f,-.12f),Brass);
            for(int i=-1;i<=1;i++) Ball(p,"Produce",Vector3.one*.045f,new Vector3(-.26f+i*.06f,.19f,-.27f),i==0?Grain:Coral);
            if(lv>1) Box(p,"Banner",new Vector3(.06f,.20f,.012f),new Vector3(.31f,.52f,.12f),Coral);
        }

        static void StudyHouse(Transform p,int lv,Era era) {
            var wall=era==Era.Medieval?Ivory:era==Era.Renaissance?Plaster:Brick;
            Box(p,era==Era.Medieval?"Study House":era==Era.Renaissance?"Scholars House":"Technical Institute",new Vector3(.52f,.42f,.40f),new Vector3(0,.25f,.04f),wall);
            if(era==Era.Medieval) HalfTimberFront(p,new Vector3(0,.25f,-.17f),.49f,.40f);
            else Box(p,"Stone Course",new Vector3(.51f,.03f,.025f),new Vector3(0,.29f,-.18f),Stone);
            GableRoof(p,new Vector3(0,.55f,.04f),new Vector3(.62f,.20f,.50f),era==Era.Medieval?Thatch:era==Era.Renaissance?Terra:Slate);
            Door(p,new Vector3(.12f,.16f,-.17f),new Vector3(.09f,.22f,.015f)); Window(p,new Vector3(-.12f,.29f,-.17f));
            Box(p,"Lectern",new Vector3(.16f,.035f,.12f),new Vector3(-.26f,.18f,-.23f),Wood).transform.localRotation=Quaternion.Euler(0,0,-12);
            Box(p,"Open Parchment",new Vector3(.13f,.008f,.10f),new Vector3(-.26f,.205f,-.23f),White);
            Box(p,"Scholar Sign",new Vector3(.14f,.11f,.02f),new Vector3(.24f,.43f,-.20f),Brass);
            LevelCrates(p,lv,new Vector3(.28f,.08f,.23f));
        }

        static void Academy(Transform p,int lv,Era era) {
            Box(p,era==Era.Industrial?"Brick Polytechnic":"Stone Academy",new Vector3(.64f,.48f,.45f),new Vector3(0,.29f,.05f),era==Era.Industrial?Brick:Plaster);
            Box(p,"Rusticated Base",new Vector3(.68f,.12f,.49f),new Vector3(0,.08f,.05f),Stone);
            HipRoof(p,new Vector3(0,.61f,.05f),new Vector3(.72f,.20f,.54f),era==Era.Industrial?Slate:Terra);
            for(int i=-1;i<=1;i+=2) Cyl(p,"Classical Column",.025f,.032f,.35f,new Vector3(i*.12f,.25f,-.235f),White,10);
            Door(p,new Vector3(0,.18f,-.185f),new Vector3(.10f,.25f,.016f));
            Window(p,new Vector3(-.21f,.35f,-.185f)); Window(p,new Vector3(.21f,.35f,-.185f));
            Spire(p,new Vector3(0,.78f,.05f),.08f,.13f,Brass);
            Box(p,"Academy Banner",new Vector3(.08f,.22f,.012f),new Vector3(.29f,.54f,-.20f),Coral);
            LevelCrates(p,lv,new Vector3(.28f,.08f,.25f));
        }

        static void SteamPlant(Transform p,int lv) {
            Box(p,"Brick Engine House",new Vector3(.60f,.42f,.45f),new Vector3(-.05f,.25f,.05f),Brick);
            GableRoof(p,new Vector3(-.05f,.52f,.05f),new Vector3(.67f,.16f,.52f),Slate);
            Box(p,"Riveted Boiler",new Vector3(.36f,.24f,.24f),new Vector3(-.05f,.20f,-.20f),Iron);
            for(int i=-1;i<=1;i+=2) Disk(p,"Boiler Band",.13f,.025f,new Vector3(-.05f+i*.12f,.20f,-.20f),new Vector3(0,0,90),Brass,12);
            Cyl(p,"Tall Brick Stack",.065f,.095f,.78f,new Vector3(.26f,.47f,.12f),Brick,12);
            Cyl(p,"Stack Cap",.085f,.085f,.045f,new Vector3(.26f,.88f,.12f),Iron,12);
            var fly=Gear(p,new Vector3(-.25f,.22f,-.30f),.13f); fly.name="Turning Flywheel"; fly.AddComponent<MiniatureSpinner>().degreesPerSecond=24f+lv*5f;
            Box(p,"Steam Pipe",new Vector3(.035f,.31f,.035f),new Vector3(.10f,.36f,-.24f),Brass);
            Box(p,"Coal Timber Bunker",new Vector3(.20f,.15f,.18f),new Vector3(.28f,.11f,-.22f),Wood);
            LevelCrates(p,lv,new Vector3(-.31f,.08f,.24f));
        }

        static GameObject Primitive(PrimitiveType type,Transform p,string name,Vector3 scale,Vector3 pos,Material mat) {
            var o=GameObject.CreatePrimitive(type); o.name=name; o.transform.SetParent(p,false); o.transform.localScale=scale; o.transform.localPosition=pos;
            var c=o.GetComponent<Collider>(); if(c){ c.enabled=false; Object.Destroy(c); }
            var r=o.GetComponent<Renderer>(); r.sharedMaterial=mat;
            bool tiny = scale.x * scale.y * scale.z < .006f;
            r.shadowCastingMode = tiny ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = !tiny;
            return o;
        }
        static GameObject Box(Transform p,string n,Vector3 s,Vector3 pos,Material m)=>Primitive(PrimitiveType.Cube,p,n,s,pos,m);
        static GameObject Ball(Transform p,string n,Vector3 s,Vector3 pos,Material m)=>Primitive(PrimitiveType.Sphere,p,n,s,pos,m);
        static GameObject Cyl(Transform p,string n,float top,float bottom,float h,Vector3 pos,Material m,int sides=12,Vector3? rot=null) {
            var o=Primitive(PrimitiveType.Cylinder,p,n,new Vector3(Mathf.Max(top,bottom)*2,h*.5f,Mathf.Max(top,bottom)*2),pos,m); if(rot.HasValue)o.transform.localEulerAngles=rot.Value; return o;
        }
        static GameObject Disk(Transform p,string n,float radius,float depth,Vector3 pos,Vector3 rot,Material m,int sides=16)=>Cyl(p,n,radius,radius,depth,pos,m,sides,rot);
        static void Window(Transform p,Vector3 pos){ Box(p,"Brass Window Frame",new Vector3(.115f,.13f,.018f),pos+new Vector3(0,0,-.008f),Brass); Box(p,"Blue Glass",new Vector3(.088f,.102f,.012f),pos+new Vector3(0,0,-.019f),Glass); Box(p,"Window Bar",new Vector3(.010f,.105f,.009f),pos+new Vector3(0,0,-.027f),Ivory); }
        static void Door(Transform p,Vector3 pos,Vector3 size){ Box(p,"Door",size,pos,Wood); Ball(p,"Door Knob",Vector3.one*.025f,pos+new Vector3(size.x*.30f,0,-.018f),Brass); }
        static void Awning(Transform p,Vector3 pos,float width,Material m){ var a=Box(p,"Striped Awning",new Vector3(width,.035f,.17f),pos,m); a.transform.localRotation=Quaternion.Euler(16,0,0); for(int i=-1;i<=1;i++) Box(p,"Awning Stripe",new Vector3(width/7,.039f,.172f),pos+new Vector3(i*width*.28f,-.001f,-.002f),White).transform.localRotation=Quaternion.Euler(16,0,0); }
        static void GableRoof(Transform p,Vector3 pos,Vector3 size,Material m){ var a=Box(p,"Roof Left",new Vector3(size.x*.54f,.055f,size.z),pos+new Vector3(-size.x*.225f,0,0),m); a.transform.localRotation=Quaternion.Euler(0,0,25); var b=Box(p,"Roof Right",new Vector3(size.x*.54f,.055f,size.z),pos+new Vector3(size.x*.225f,0,0),m); b.transform.localRotation=Quaternion.Euler(0,0,-25); Box(p,"Roof Ridge",new Vector3(.035f,.035f,size.z*1.03f),pos+new Vector3(0,size.y*.44f,0),Brass); }
        static void HipRoof(Transform p,Vector3 pos,Vector3 size,Material m){ Taper(p,"Hipped Roof",pos,size,m,4,ref pyramid4,45f); Box(p,"Roof Cap",new Vector3(.11f,.035f,.11f),pos+new Vector3(0,size.y*.52f,0),Brass); }
        static void Spire(Transform p,Vector3 pos,float radius,float height,Material m){ Taper(p,"Spire",pos,new Vector3(radius*2,height,radius*2),m,8,ref cone8,0f); }
        static GameObject Taper(Transform p,string name,Vector3 pos,Vector3 size,Material mat,int sides,ref Mesh shared,float yaw) {
            if(!shared) {
                var vertices=new Vector3[sides+2]; var triangles=new int[sides*6];
                vertices[0]=new Vector3(0,.5f,0); vertices[1]=new Vector3(0,-.5f,0);
                for(int i=0;i<sides;i++){ float a=i*Mathf.PI*2f/sides; vertices[i+2]=new Vector3(Mathf.Sin(a)*.5f,-.5f,Mathf.Cos(a)*.5f); int n=(i+1)%sides; int t=i*6; triangles[t]=0; triangles[t+1]=i+2; triangles[t+2]=n+2; triangles[t+3]=1; triangles[t+4]=n+2; triangles[t+5]=i+2; }
                shared=new Mesh { name="Riverworks Taper " + sides }; shared.vertices=vertices; shared.triangles=triangles; shared.RecalculateNormals(); shared.RecalculateBounds();
            }
            var o=new GameObject(name); o.transform.SetParent(p,false); o.transform.localPosition=pos; o.transform.localScale=size; o.transform.localRotation=Quaternion.Euler(0,yaw,0);
            o.AddComponent<MeshFilter>().sharedMesh=shared; var r=o.AddComponent<MeshRenderer>(); r.sharedMaterial=mat; r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows=true; return o;
        }
        static void SawRoof(Transform p,Vector3 pos,Vector3 size){ for(int i=-1;i<=1;i++){ var r=Box(p,"Sawtooth Roof",new Vector3(size.x/3+.02f,.045f,size.z),pos+new Vector3(i*size.x/3,Mathf.Abs(i)*.025f,0),Indigo); r.transform.localRotation=Quaternion.Euler(0,0,-12); } }
        static GameObject Gear(Transform p,Vector3 pos,float radius){ var root=new GameObject("Turning Gear"); root.transform.SetParent(p,false); root.transform.localPosition=pos; Disk(root.transform,"Gear",radius,.025f,Vector3.zero,new Vector3(90,0,0),Brass,12); for(int i=0;i<8;i++){ float a=i*Mathf.PI/4; Box(root.transform,"Gear Tooth",new Vector3(.025f,.025f,.035f),new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,-.018f),Brass).transform.localRotation=Quaternion.Euler(0,0,-i*45); } return root; }
        static void UpgradeMarker(Transform p,int level){ if(level<2)return; Cyl(p,"Upgrade Finial",.014f,.014f,.34f,new Vector3(.32f,.17f,.28f),Brass,8); Ball(p,"Level Crest",Vector3.one*.055f,new Vector3(.32f,.35f,.28f),level>2?Coral:Brass); if(level>2){ var flag=Box(p,"Mastery Pennant",new Vector3(.13f,.075f,.012f),new Vector3(.255f,.43f,.28f),Coral); flag.transform.localRotation=Quaternion.Euler(0,0,-8f); } }
        static void LevelCrates(Transform p,int lv,Vector3 pos){ for(int i=0;i<lv;i++){ var q=Box(p,"Supply Crate",new Vector3(.13f,.12f,.13f),pos+new Vector3((i%2)*.13f,i/2*.12f,0),Wood); Box(q.transform,"Crate Band",new Vector3(1.03f,.13f,.12f),Vector3.zero,Brass); } }
    }

    public sealed class MiniatureSpinner : MonoBehaviour
    {
        public float degreesPerSecond = 22f;
        public Vector3 axis = Vector3.forward;
        GameController controller;

        void Start() { controller = FindAnyObjectByType<GameController>(); }
        void Update()
        {
            if (controller == null || controller.GameSpeed <= 0f || controller.HelpOpen || controller.ModalOpen || controller.ResearchOpen) return;
            transform.Rotate(axis, degreesPerSecond * controller.GameSpeed * Time.deltaTime, Space.Self);
        }
    }
}

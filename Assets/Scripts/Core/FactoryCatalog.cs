using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    [Serializable]
    public class FactorySpec
    {
        public FactoryKind Kind; public string Name, Description; public int Width, Height, CoinCost, TimberCost, StoneCost;
        public TechId RequiredTech; public float PowerDemand; public int InputCapacity, OutputCapacity;
        public FactorySpec(FactoryKind kind,string name,string description,int width,int height,int coins,int timber,int stone,TechId tech,float power)
            : this(kind,name,description,width,height,coins,timber,stone,tech,power,IsLegacyStorage(kind)?80:24,IsLegacyStorage(kind)?80:24) { }
        public FactorySpec(FactoryKind kind,string name,string description,int width,int height,int coins,int timber,int stone,TechId tech,float power,int inputCapacity,int outputCapacity)
        { Kind=kind;Name=name;Description=description;Width=width;Height=height;CoinCost=coins;TimberCost=timber;StoneCost=stone;RequiredTech=tech;PowerDemand=power;InputCapacity=inputCapacity;OutputCapacity=outputCapacity; }
        static bool IsLegacyStorage(FactoryKind kind) => kind==FactoryKind.Storage||kind==FactoryKind.ImportDock||kind==FactoryKind.ExportDock;
    }

    public static class FactoryCatalog
    {
        static readonly FactorySpec[] Specs =
        {
            S(FactoryKind.Belt,"운송 벨트","물자를 한 칸씩 운반합니다.",1,1,5,1,0,TechId.MechanicalPower,0),
            S(FactoryKind.Inserter,"투입기","뒤에서 집어 앞에 놓습니다.",1,1,12,2,0,TechId.MechanicalPower,.25f),
            S(FactoryKind.Drill,"채굴기","광맥에서 광물을 채굴합니다.",2,2,80,12,8,TechId.Metallurgy,2),
            S(FactoryKind.Furnace,"용광로","광석을 금속으로 제련합니다.",2,2,100,8,18,TechId.Metallurgy,3),
            S(FactoryKind.Assembler,"조립기","선택한 제조법으로 물자를 조립합니다.",2,2,140,18,12,TechId.Toolmaking,4),
            S(FactoryKind.Storage,"창고","고체 물자를 최대 80개 보관합니다.",1,1,35,10,2,TechId.None,0),
            S(FactoryKind.ImportDock,"반입 부두","도시 물자를 공장으로 반입합니다.",2,2,70,12,8,TechId.Guilds,0),
            S(FactoryKind.ExportDock,"반출 부두","완제품을 도시로 내보냅니다.",2,2,70,12,8,TechId.Guilds,0),
            S(FactoryKind.PowerInlet,"전력 인입구","도시의 남는 전력을 공장에 연결합니다.",2,2,90,8,12,TechId.SteamPower,0),
            S(FactoryKind.Pole,"전신주","6칸 거리까지 전력망을 중계합니다.",1,1,16,3,0,TechId.SteamPower,0),
            S(FactoryKind.Splitter,"분배기","앞과 왼쪽으로 번갈아 분배합니다.",1,1,30,4,2,TechId.MechanicalPower,0),
            N(FactoryKind.Pipe,"파이프","액체 한 종류를 운반합니다.",1,1,8,1,1,TechId.FluidHandling,0,40),
            N(FactoryKind.PipeJunction,"파이프 분기점","액체 흐름을 두 방향으로 나눕니다.",1,1,18,2,2,TechId.FluidHandling,0,40),
            N(FactoryKind.FluidTank,"유체 탱크","액체 한 종류를 최대 240L 저장합니다.",1,1,55,8,8,TechId.FluidHandling,0,240),
            N(FactoryKind.WaterPump,"물 펌프","수원에서 물을 끌어 올립니다.",2,2,95,12,10,TechId.FluidHandling,2,80),
            N(FactoryKind.OilPump,"원유 펌프","확인된 유전에서 원유를 추출합니다.",2,2,150,20,18,TechId.OilRefining,2,80),
            N(FactoryKind.Foundry,"주조소","합금과 알루미늄을 대량 제련합니다.",2,2,210,20,30,TechId.MetallurgicalEfficiency,4,80),
            N(FactoryKind.MachiningBench,"정밀 가공대","금속 부품을 절삭하고 성형합니다.",2,2,190,28,18,TechId.Toolmaking,4,80),
            N(FactoryKind.Refinery,"정유소","원유와 광물 용액을 정제합니다.",2,2,320,30,35,TechId.FluidHandling,8,80),
            N(FactoryKind.ChemicalPlant,"화학 공장","산업 화학물을 합성합니다.",2,2,280,25,28,TechId.Petrochemistry,6,80),
            N(FactoryKind.Manufacturer,"제조기","복잡한 부품을 자동 조립합니다.",2,2,420,40,40,TechId.AdvancedManufacturing,10,80),
            new FactorySpec(FactoryKind.ItemLift,"화물 승강기","인접한 두 층을 잇는 한 쌍의 화물 승강기입니다. 표시 가격은 끝점 하나 기준입니다.",1,1,45,4,4,TechId.Logistics,1,0,0),
            new FactorySpec(FactoryKind.FluidRiser,"유체 라이저","인접한 두 층을 잇는 한 쌍의 유체 라이저입니다. 표시 가격은 끝점 하나 기준입니다.",1,1,35,2,4,TechId.FluidHandling,1,40,0)
        };

        static readonly RecipeSpec[] RecipeSpecs =
        {
            R(FactoryRecipe.IronPlate,"강철 제련",4,TechId.None,M(FactoryKind.Furnace),I(Resource.Ore,2),O(Resource.Steel,1)),
            R(FactoryRecipe.Tools,"도구",6,TechId.None,M(FactoryKind.Assembler),I(Resource.Steel,1,Resource.Timber,1),O(Resource.Tools,1)),
            R(FactoryRecipe.Flour,"밀가루",4,TechId.None,M(FactoryKind.Assembler),I(Resource.Grain,2),O(Resource.Flour,2)),
            R(FactoryRecipe.Bread,"빵",5,TechId.None,M(FactoryKind.Assembler),I(Resource.Flour,2),O(Resource.Bread,3)),
            X(FactoryRecipe.IronMining,"철광석 채굴",2,TechId.None,FactoryKind.Drill,Resource.Ore,1),
            X(FactoryRecipe.CopperMining,"구리 채굴",2,TechId.Metallurgy,FactoryKind.Drill,Resource.CopperOre,1),
            X(FactoryRecipe.CoalMining,"석탄 채굴",2,TechId.Metallurgy,FactoryKind.Drill,Resource.Coal,1),
            X(FactoryRecipe.BauxiteMining,"보크사이트 채굴",3,TechId.AluminumProcessing,FactoryKind.Drill,Resource.Bauxite,1),
            X(FactoryRecipe.WaterExtraction,"물 추출",1,TechId.FluidHandling,FactoryKind.WaterPump,Resource.Water,4),
            X(FactoryRecipe.OilExtraction,"원유 추출",2,TechId.OilRefining,FactoryKind.OilPump,Resource.CrudeOil,4),
            R(FactoryRecipe.CopperSmelting,"구리 제련",4,TechId.Metallurgy,M(FactoryKind.Furnace),I(Resource.CopperOre,2),O(Resource.Copper,1)),
            R(FactoryRecipe.AlloySteel,"합금강 주조",6,TechId.MetallurgicalEfficiency,M(FactoryKind.Foundry),I(Resource.Ore,3,Resource.Coal,1),O(Resource.Steel,3)),
            R(FactoryRecipe.GearCutting,"기어 절삭",3,TechId.Toolmaking,M(FactoryKind.MachiningBench),I(Resource.Steel,1),O(Resource.Gear,2)),
            R(FactoryRecipe.WireDrawing,"전선 인발",3,TechId.Toolmaking,M(FactoryKind.MachiningBench),I(Resource.Copper,1),O(Resource.Wire,3)),
            R(FactoryRecipe.BeamRolling,"강철 보 압연",5,TechId.Toolmaking,M(FactoryKind.MachiningBench),I(Resource.Steel,2),O(Resource.SteelBeam,1)),
            R(FactoryRecipe.PipeRolling,"강철 파이프 압연",4,TechId.Toolmaking,M(FactoryKind.MachiningBench),I(Resource.Steel,1),O(Resource.SteelPipe,2)),
            R(FactoryRecipe.SilicaCrushing,"실리카 분쇄",4,TechId.Toolmaking,M(FactoryKind.MachiningBench),I(Resource.Stone,2),O(Resource.Silica,3)),
            R(FactoryRecipe.FrameAssembly,"모듈 프레임 조립",8,TechId.MassProduction,M(FactoryKind.Assembler),I(Resource.SteelBeam,2,Resource.Gear,2,Resource.SteelPipe,2),O(Resource.ModularFrame,1)),
            R(FactoryRecipe.MotorAssembly,"모터 조립",8,TechId.Electronics,M(FactoryKind.Assembler),I(Resource.Gear,2,Resource.Wire,3,Resource.SteelPipe,1,Resource.Rubber,1),O(Resource.Motor,1)),
            R(FactoryRecipe.CircuitPrinting,"회로 인쇄",6,TechId.Electronics,M(FactoryKind.Assembler),I(Resource.Copper,1,Resource.Wire,2,Resource.Plastic,2),O(Resource.Circuit,2)),
            R(FactoryRecipe.AdvancedCircuitAssembly,"고급 회로 조립",8,TechId.Electronics,M(FactoryKind.Assembler),I(Resource.Circuit,2,Resource.Plastic,2,Resource.Sulfur,1),O(Resource.AdvancedCircuit,1)),
            R(FactoryRecipe.ComputerAssembly,"컴퓨터 조립",10,TechId.AdvancedManufacturing,M(FactoryKind.Manufacturer),I(Resource.AdvancedCircuit,2,Resource.Circuit,4,Resource.Wire,4,Resource.Plastic,2),O(Resource.Computer,1)),
            R(FactoryRecipe.ControlUnitAssembly,"제어 장치 조립",14,TechId.IndustrialControl,M(FactoryKind.Manufacturer),I(Resource.Computer,1,Resource.Motor,2,Resource.Battery,2,Resource.ModularFrame,2,Resource.AluminumCasing,2),O(Resource.ControlUnit,1)),
            R(FactoryRecipe.OilRefining,"원유 정제",8,TechId.OilRefining,M(FactoryKind.Refinery),I(Resource.CrudeOil,8,Resource.Water,4),O(Resource.HeavyOil,3,Resource.PetroleumGas,5)),
            R(FactoryRecipe.PlasticPolymerization,"플라스틱 중합",6,TechId.Petrochemistry,M(FactoryKind.ChemicalPlant),I(Resource.PetroleumGas,3,Resource.Coal,1),O(Resource.Plastic,2)),
            R(FactoryRecipe.RubberPolymerization,"고무 중합",6,TechId.Petrochemistry,M(FactoryKind.ChemicalPlant),I(Resource.PetroleumGas,2,Resource.Water,2),O(Resource.Rubber,2)),
            R(FactoryRecipe.SulfurRecovery,"황 회수",5,TechId.Petrochemistry,M(FactoryKind.ChemicalPlant),I(Resource.PetroleumGas,2,Resource.Water,2),O(Resource.Sulfur,2)),
            R(FactoryRecipe.SulfuricAcid,"황산 합성",8,TechId.Petrochemistry,M(FactoryKind.ChemicalPlant),I(Resource.Sulfur,2,Resource.Water,4,Resource.PetroleumGas,1),O(Resource.SulfuricAcid,5)),
            R(FactoryRecipe.HeavyOilCracking,"중유 분해",6,TechId.Petrochemistry,M(FactoryKind.Refinery),I(Resource.HeavyOil,3,Resource.Water,2),O(Resource.PetroleumGas,4)),
            R(FactoryRecipe.FuelRefining,"연료 정제",5,TechId.Petrochemistry,M(FactoryKind.Refinery),I(Resource.HeavyOil,3),O(Resource.Fuel,4)),
            R(FactoryRecipe.PlasticRecycling,"플라스틱 재생",7,TechId.AdvancedManufacturing,M(FactoryKind.ChemicalPlant),I(Resource.Rubber,2,Resource.Fuel,3),O(Resource.Plastic,4)),
            R(FactoryRecipe.RubberRecycling,"고무 재생",7,TechId.AdvancedManufacturing,M(FactoryKind.ChemicalPlant),I(Resource.Plastic,2,Resource.Fuel,3),O(Resource.Rubber,4)),
            R(FactoryRecipe.AluminaRefining,"알루미나 정제",8,TechId.AluminumProcessing,M(FactoryKind.Refinery),I(Resource.Bauxite,3,Resource.Water,4),O(Resource.AluminaSolution,4,Resource.Silica,1)),
            R(FactoryRecipe.ScrapRefining,"알루미늄 스크랩 정제",8,TechId.AluminumProcessing,M(FactoryKind.Refinery),I(Resource.AluminaSolution,4,Resource.Coal,1),O(Resource.AluminumScrap,6,Resource.Water,2)),
            R(FactoryRecipe.AluminumSmelting,"알루미늄 제련",7,TechId.AluminumProcessing,M(FactoryKind.Foundry),I(Resource.AluminumScrap,3,Resource.Silica,2),O(Resource.Aluminum,2)),
            R(FactoryRecipe.CasingPressing,"케이싱 성형",6,TechId.AluminumProcessing,M(FactoryKind.MachiningBench),I(Resource.Aluminum,2,Resource.Copper,1),O(Resource.AluminumCasing,2)),
            R(FactoryRecipe.BatteryAssembly,"배터리 조립",10,TechId.EnergyStorage,M(FactoryKind.ChemicalPlant),I(Resource.Aluminum,2,Resource.Copper,1,Resource.SulfuricAcid,4),O(Resource.Battery,1,Resource.Water,2))
        };

        static readonly Dictionary<FactoryKind,FactorySpec> ByKind=Specs.ToDictionary(s=>s.Kind);
        static readonly Dictionary<FactoryRecipe,RecipeSpec> ByRecipe=RecipeSpecs.ToDictionary(s=>s.Id);
        public static IEnumerable<FactorySpec> All => Specs;
        public static IReadOnlyList<RecipeSpec> Recipes => Array.AsReadOnly(RecipeSpecs);
        public static FactorySpec Get(FactoryKind kind)=>ByKind.TryGetValue(kind,out var spec)?spec:null;
        public static RecipeSpec GetRecipe(FactoryRecipe recipe)=>ByRecipe.TryGetValue(recipe,out var spec)?spec:null;
        public static IReadOnlyList<RecipeSpec> RecipesFor(FactoryKind kind)=>Array.AsReadOnly(RecipeSpecs.Where(r=>r.Machines.Contains(kind)).ToArray());
        public static bool IsRecipeCompatible(FactoryKind kind,FactoryRecipe recipe)=>recipe==FactoryRecipe.None?IsProduction(kind):GetRecipe(recipe)?.Machines.Contains(kind)==true;
        public static bool IsProduction(FactoryKind k)=>k==FactoryKind.Drill||k==FactoryKind.Furnace||k==FactoryKind.Assembler||k==FactoryKind.WaterPump||k==FactoryKind.OilPump||k==FactoryKind.Foundry||k==FactoryKind.MachiningBench||k==FactoryKind.Refinery||k==FactoryKind.ChemicalPlant||k==FactoryKind.Manufacturer;
        public static bool IsFluidTransport(FactoryKind k)=>k==FactoryKind.Pipe||k==FactoryKind.PipeJunction||k==FactoryKind.FluidTank||k==FactoryKind.FluidRiser;
        public static bool IsClockable(FactoryKind k)=>IsProduction(k);
        public static string RecipeName(FactoryRecipe recipe)=>GetRecipe(recipe)?.Name??"없음";
        public static string InputText(FactoryRecipe recipe)=>Text(GetRecipe(recipe)?.Inputs);
        public static string OutputText(FactoryRecipe recipe)=>Text(GetRecipe(recipe)?.Outputs);
        public static float RecipeDuration(FactoryRecipe recipe)=>GetRecipe(recipe)?.Duration??0f;

        static string Text(RecipeAmount[] a)=>a==null||a.Length==0?"-":string.Join(" + ",a.Select(v=>$"{ResourceCatalog.Get(v.Resource).Name} {v.Amount}{ResourceCatalog.Get(v.Resource).Unit}"));
        static FactorySpec S(FactoryKind k,string n,string d,int w,int h,int c,int t,int s,TechId tech,float p)=>new FactorySpec(k,n,d,w,h,c,t,s,tech,p);
        static FactorySpec N(FactoryKind k,string n,string d,int w,int h,int c,int t,int s,TechId tech,float p,int cap)=>new FactorySpec(k,n,d,w,h,c,t,s,tech,p,cap,cap);
        static FactoryKind[] M(params FactoryKind[] a)=>a;
        static RecipeAmount[] I(params object[] a)=>Amounts(a); static RecipeAmount[] O(params object[] a)=>Amounts(a);
        static RecipeAmount[] Amounts(object[] a){var r=new RecipeAmount[a.Length/2];for(int i=0;i<a.Length;i+=2)r[i/2]=new RecipeAmount((Resource)a[i],(int)a[i+1]);return r;}
        static RecipeSpec R(FactoryRecipe id,string n,float d,TechId t,FactoryKind[] m,RecipeAmount[] i,RecipeAmount[] o)=>new RecipeSpec{Id=id,Name=n,Description=n,Duration=d,RequiredTech=t,Machines=m,Inputs=i,Outputs=o};
        static RecipeSpec X(FactoryRecipe id,string n,float d,TechId t,FactoryKind m,Resource source,int count)=>new RecipeSpec{Id=id,Name=n,Description=n,Duration=d,RequiredTech=t,Machines=M(m),Outputs=O(source,count),IsExtraction=true,SourceResource=source};
    }
}

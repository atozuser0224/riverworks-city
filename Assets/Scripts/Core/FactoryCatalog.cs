using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    [Serializable]
    public class FactorySpec
    {
        public FactoryKind Kind; public string Name, Description; public int Width, Height, CoinCost, TimberCost, StoneCost;
        public TechId RequiredTech; public float PowerDemand;
        public FactorySpec(FactoryKind kind, string name, string description, int width, int height, int coins, int timber, int stone, TechId tech, float power)
        { Kind = kind; Name = name; Description = description; Width = width; Height = height; CoinCost = coins; TimberCost = timber; StoneCost = stone; RequiredTech = tech; PowerDemand = power; }
    }

    public static class FactoryCatalog
    {
        static readonly FactorySpec[] Specs = {
            new FactorySpec(FactoryKind.Belt,"운송 벨트","물자를 한 칸씩 운반합니다.",1,1,5,1,0,TechId.MechanicalPower,0),
            new FactorySpec(FactoryKind.Inserter,"투입기","뒤에서 집어 앞에 놓습니다.",1,1,12,2,0,TechId.MechanicalPower,.25f),
            new FactorySpec(FactoryKind.Drill,"채굴기","광맥에서 철광석을 캡니다.",2,2,80,12,8,TechId.Metallurgy,2),
            new FactorySpec(FactoryKind.Furnace,"용광로","철광석을 강철로 제련합니다.",2,2,100,8,18,TechId.Metallurgy,3),
            new FactorySpec(FactoryKind.Assembler,"조립기","선택한 제조법으로 물자를 조립합니다.",2,2,140,18,12,TechId.Toolmaking,4),
            new FactorySpec(FactoryKind.Storage,"창고","물자를 최대 80개 보관합니다.",1,1,35,10,2,TechId.None,0),
            new FactorySpec(FactoryKind.ImportDock,"반입 부두","도시 물자를 공장으로 반입합니다.",2,2,70,12,8,TechId.Guilds,0),
            new FactorySpec(FactoryKind.ExportDock,"반출 부두","완제품을 도시로 내보냅니다.",2,2,70,12,8,TechId.Guilds,0),
            new FactorySpec(FactoryKind.PowerInlet,"전력 인입구","도시의 남는 전력을 공장에 연결합니다.",2,2,90,8,12,TechId.SteamPower,0),
            new FactorySpec(FactoryKind.Pole,"전신주","6칸 거리까지 전력망을 중계합니다.",1,1,16,3,0,TechId.SteamPower,0),
            new FactorySpec(FactoryKind.Splitter,"분배기","앞과 왼쪽으로 번갈아 분배합니다.",1,1,30,4,2,TechId.MechanicalPower,0)
        };
        public static IEnumerable<FactorySpec> All => Specs;
        static readonly Dictionary<FactoryKind,FactorySpec> ByKind=Specs.ToDictionary(spec=>spec.Kind);
        public static FactorySpec Get(FactoryKind kind) => ByKind.TryGetValue(kind,out var spec)?spec:null;
        public static string RecipeName(FactoryRecipe recipe) => recipe switch { FactoryRecipe.IronPlate => "강철 제련", FactoryRecipe.Tools => "도구", FactoryRecipe.Flour => "밀가루", FactoryRecipe.Bread => "빵", _ => "없음" };
        public static string InputText(FactoryRecipe recipe) => recipe switch { FactoryRecipe.IronPlate => "철광석 2", FactoryRecipe.Tools => "강철 1 + 목재 1", FactoryRecipe.Flour => "곡물 2", FactoryRecipe.Bread => "밀가루 2", _ => "-" };
        public static string OutputText(FactoryRecipe recipe) => recipe switch { FactoryRecipe.IronPlate => "강철 1", FactoryRecipe.Tools => "도구 1", FactoryRecipe.Flour => "밀가루 2", FactoryRecipe.Bread => "빵 3", _ => "-" };
        public static float RecipeDuration(FactoryRecipe recipe) => recipe switch { FactoryRecipe.IronPlate => 4f, FactoryRecipe.Tools => 6f, FactoryRecipe.Flour => 4f, FactoryRecipe.Bread => 5f, _ => 0f };
    }
}

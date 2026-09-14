using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    public sealed class ResourceSpec
    {
        public Resource Id;
        public string Name = "";
        public string Unit = "개";
        public string ColorHex = "#FFFFFF";
        public bool IsFluid;
        public int TradePrice;
    }

    public static class ResourceCatalog
    {
        public const int LegacyCount = 9;
        public const int Count = 38;
        public const int InventoryCount = Count;

        static readonly ResourceSpec[] Specs =
        {
            S(Resource.Coins,"코인","#F5C451",0), S(Resource.Timber,"목재","#9B6A45",4), S(Resource.Stone,"석재","#8B949E",5),
            S(Resource.Grain,"곡물","#D9B44A",3), S(Resource.Flour,"밀가루","#EEE1C6",5), S(Resource.Bread,"빵","#C9823D",7),
            S(Resource.Ore,"철광석","#6F7782",5), S(Resource.Steel,"강철","#AAB5C2",11), S(Resource.Tools,"도구","#D48A45",16),
            S(Resource.CopperOre,"구리 광석","#A86645",8), S(Resource.Copper,"구리","#D27B47"), S(Resource.Coal,"석탄","#34383D",5),
            S(Resource.Gear,"기어","#AEB7BE"), S(Resource.Wire,"전선","#E09B5A"), S(Resource.SteelBeam,"강철 보","#8996A3"),
            S(Resource.SteelPipe,"강철 파이프","#738899"), S(Resource.Plastic,"플라스틱","#E8E8DF"), S(Resource.Rubber,"고무","#30363B"),
            S(Resource.Sulfur,"황","#E4D447"), S(Resource.Circuit,"회로 기판","#4EAA69"), S(Resource.Motor,"모터","#627D91"),
            S(Resource.AdvancedCircuit,"고급 회로","#D45B58"), S(Resource.Computer,"컴퓨터","#769BC7"), S(Resource.Battery,"배터리","#B9D052"),
            S(Resource.ModularFrame,"모듈 프레임","#8492A1"), S(Resource.ControlUnit,"제어 장치","#8D72C7"), S(Resource.Bauxite,"보크사이트","#B45E4C",9),
            S(Resource.Silica,"실리카","#E4DDD0"), S(Resource.AluminumScrap,"알루미늄 스크랩","#B9C1C7"), S(Resource.Aluminum,"알루미늄","#D2DBE0"),
            S(Resource.AluminumCasing,"알루미늄 케이싱","#BAC9D1"), F(Resource.Water,"물","#4C9EDF"), F(Resource.CrudeOil,"원유","#40342E"),
            F(Resource.HeavyOil,"중유","#5A4434"), F(Resource.PetroleumGas,"석유 가스","#D9A95B"), F(Resource.SulfuricAcid,"황산","#C9D94D"),
            F(Resource.AluminaSolution,"알루미나 용액","#D8C7A2"), F(Resource.Fuel,"연료","#D98245")
        };

        static readonly IReadOnlyList<ResourceSpec> Tradeable = Array.AsReadOnly(Specs.Where(s => s.TradePrice > 0).ToArray());
        static readonly IReadOnlyList<ResourceSpec> Solids = Array.AsReadOnly(Specs.Where(s => s.Id != Resource.Coins && !s.IsFluid).ToArray());
        static readonly IReadOnlyList<ResourceSpec> Fluids = Array.AsReadOnly(Specs.Where(s => s.IsFluid).ToArray());

        public static IReadOnlyList<ResourceSpec> All => Array.AsReadOnly(Specs);
        public static IReadOnlyList<ResourceSpec> TradeableResources => Tradeable;
        public static IReadOnlyList<ResourceSpec> SolidResources => Solids;
        public static IReadOnlyList<ResourceSpec> FluidResources => Fluids;
        public static ResourceSpec Get(Resource resource) => IsValid(resource) ? Specs[(int)resource] : null;
        public static bool IsValid(Resource resource) => (int)resource >= 0 && (int)resource < Count;
        public static bool IsSolid(Resource resource) => IsValid(resource) && resource != Resource.Coins && !Specs[(int)resource].IsFluid;
        public static bool IsFluid(Resource resource) => IsValid(resource) && Specs[(int)resource].IsFluid;
        public static bool IsTransportable(Resource resource) => IsSolid(resource);
        public static bool IsTradeable(Resource resource) => IsValid(resource) && Specs[(int)resource].TradePrice > 0;
        public static bool CanFeed(Resource resource) => IsValid(resource) && resource != Resource.Coins;

        static ResourceSpec S(Resource id, string name, string color, int price = 0) =>
            new ResourceSpec { Id = id, Name = name, Unit = id == Resource.Coins ? "G" : "개", ColorHex = color, TradePrice = price };
        static ResourceSpec F(Resource id, string name, string color) =>
            new ResourceSpec { Id = id, Name = name, Unit = "L", ColorHex = color, IsFluid = true };
    }
}

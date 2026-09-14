using System.Collections.Generic;

namespace Riverworks
{
    public class BuildingSpec
    {
        public BuildingKind Kind;
        public string Name = "", Description = "", Category = "";
        public int Cost, TimberCost, StoneCost, UnlockPopulation;
        public Resource Output;
        public float OutputAmount;
        public Dictionary<Resource, float> Inputs = new Dictionary<Resource, float>();
    }

    public static class Catalog
    {
        private static readonly Dictionary<BuildingKind, BuildingSpec> Specs = Create();
        public static IEnumerable<BuildingSpec> All => Specs.Values;
        public static BuildingSpec Get(BuildingKind kind) => Specs.TryGetValue(kind, out var spec) ? spec : Specs[BuildingKind.None];
        public static string ResourceName(Resource resource)
        {
            return ResourceCatalog.Get(resource)?.Name ?? "알 수 없음";
        }

        private static Dictionary<BuildingKind, BuildingSpec> Create()
        {
            var d = new Dictionary<BuildingKind, BuildingSpec>();
            Add(BuildingKind.None, "없음", "", "");
            Add(BuildingKind.TownHall, "시청", "도시의 도로망과 행정을 담당합니다.", "도시");
            Add(BuildingKind.Road, "도로", "건물을 시청에 연결합니다. 물 위에는 다리가 놓입니다.", "기반시설", 8);
            Add(BuildingKind.House, "주택", "빵을 소비하며 주민이 성장합니다.", "도시", 55, 12, 4);
            Add(BuildingKind.Lumberyard, "벌목장", "숲 가까이에서 목재를 생산합니다.", "원료", 75, 8, 3, 0, Resource.Timber, 2.5f);
            Add(BuildingKind.Quarry, "채석장", "바위 가까이에서 석재를 생산합니다.", "원료", 90, 12, 0, 0, Resource.Stone, 2.0f);
            Add(BuildingKind.Farm, "농장", "곡물을 재배합니다.", "식량", 70, 10, 2, 0, Resource.Grain, 3.0f);
            Add(BuildingKind.Mill, "제분소", "곡물을 밀가루로 가공합니다.", "식량", 110, 18, 8, 10, Resource.Flour, 2.0f, Resource.Grain, 2);
            Add(BuildingKind.Bakery, "제과점", "밀가루로 빵을 굽습니다.", "식량", 135, 20, 10, 14, Resource.Bread, 2.2f, Resource.Flour, 1.5f);
            Add(BuildingKind.Mine, "광산", "바위 지대에서 광석을 캡니다.", "산업", 160, 25, 15, 20, Resource.Ore, 2.0f);
            Add(BuildingKind.Smelter, "제련소", "광석을 강철로 제련합니다.", "산업", 220, 25, 25, 25, Resource.Steel, 1.5f, Resource.Ore, 2);
            Add(BuildingKind.Workshop, "공방", "강철과 목재로 도구를 만듭니다.", "산업", 260, 35, 20, 30, Resource.Tools, 1.0f, Resource.Steel, 1, Resource.Timber, 1);
            Add(BuildingKind.Windmill, "풍차", "산업 시설에 동력을 공급합니다.", "기반시설", 120, 20, 10);
            Add(BuildingKind.Park, "공원", "주변 주민의 행복을 높입니다.", "도시", 85, 8, 4, 12);
            Add(BuildingKind.Warehouse, "창고", "생산 효율과 도시 물류를 돕습니다.", "기반시설", 145, 30, 15, 18);
            Add(BuildingKind.Market, "시장", "부족한 빵을 비상 수입합니다.", "도시", 175, 25, 15, 22);
            Add(BuildingKind.StudyHouse, "서재", "학자들이 지식을 모아 연구를 빠르게 합니다.", "연구", 95, 16, 6);
            Add(BuildingKind.Academy, "학술원", "도시의 연구를 크게 발전시킵니다.", "연구", 240, 24, 30, 18);
            Add(BuildingKind.SteamPlant, "증기 동력소", "목재를 태워 강력한 산업 동력을 공급합니다.", "기반시설", 320, 40, 35, 28);
            return d;

            void Add(BuildingKind kind, string name, string desc, string category, int cost = 0, int timber = 0, int stone = 0, int unlock = 0, Resource output = Resource.Coins, float amount = 0, params object[] inputs)
            {
                var spec = new BuildingSpec { Kind = kind, Name = name, Description = desc, Category = category, Cost = cost, TimberCost = timber, StoneCost = stone, UnlockPopulation = unlock, Output = output, OutputAmount = amount };
                for (int i = 0; i < inputs.Length; i += 2) spec.Inputs[(Resource)inputs[i]] = System.Convert.ToSingle(inputs[i + 1]);
                d[kind] = spec;
            }
        }
    }
}

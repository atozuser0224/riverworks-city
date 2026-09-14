using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    public sealed class TechSpec
    {
        public TechId Id;
        public string Name = "", Description = "", Benefit = "";
        public Era Era;
        public int ResearchCost, CoinCost, DurationDays;
        public TechId[] Prerequisites = Array.Empty<TechId>();
        public BuildingKind[] UnlockBuildings = Array.Empty<BuildingKind>();
    }

    public static class TechCatalog
    {
        private static readonly TechSpec[] Specs =
        {
            Spec(TechId.CropRotation, "윤작", "밭을 번갈아 경작하는 농법입니다.", "농장 생산량 +25%", Era.Medieval, 6, 45, 2, null),
            Spec(TechId.Stonecraft, "석조술", "튼튼한 석재 건축법을 정립합니다.", "채석장·공원과 2단계 증축", Era.Medieval, 7, 55, 2, null, BuildingKind.Quarry, BuildingKind.Park),
            Spec(TechId.MechanicalPower, "기계 동력", "풍차의 회전력을 생산에 이용합니다.", "풍차·제분소·제과점", Era.Medieval, 8, 65, 3, new[] { TechId.CropRotation }, BuildingKind.Windmill, BuildingKind.Mill, BuildingKind.Bakery),
            Spec(TechId.Guilds, "길드", "전문 장인과 상인의 조직을 세웁니다.", "르네상스 진입, 창고·시장", Era.Renaissance, 9, 80, 3, new[] { TechId.Stonecraft, TechId.MechanicalPower }, BuildingKind.Warehouse, BuildingKind.Market),
            Spec(TechId.Metallurgy, "금속학", "광석에서 쓸모 있는 금속을 얻습니다.", "광산·제련소", Era.Renaissance, 10, 95, 3, new[] { TechId.Guilds }, BuildingKind.Mine, BuildingKind.Smelter),
            Spec(TechId.Scholarship, "학문", "지식을 체계적으로 기록하고 전수합니다.", "학술원", Era.Renaissance, 10, 100, 3, new[] { TechId.Guilds }, BuildingKind.Academy),
            Spec(TechId.Toolmaking, "도구 제작", "정밀한 생산 도구를 표준화합니다.", "공방", Era.Renaissance, 11, 115, 4, new[] { TechId.Metallurgy }, BuildingKind.Workshop),
            Spec(TechId.SteamPower, "증기력", "증기로 대규모 기계를 움직입니다.", "산업 시대 진입, 증기 동력소", Era.Industrial, 13, 140, 4, new[] { TechId.Scholarship, TechId.Toolmaking }, BuildingKind.SteamPlant),
            Spec(TechId.UrbanPlanning, "도시 계획", "도로와 구역을 장기적으로 설계합니다.", "3단계 증축과 대도시 승리", Era.Industrial, 15, 165, 5, new[] { TechId.SteamPower }),
            Spec(TechId.Forestry, "산림 경영", "숲을 순환 관리해 목재 수확량을 높입니다.", "벌목장 생산량 +25%", Era.Medieval, 8, 70, 2, new[] { TechId.CropRotation }),
            Spec(TechId.Irrigation, "관개", "수로와 물길로 밭에 안정적으로 물을 공급합니다.", "농장 생산량 +20%", Era.Medieval, 9, 75, 3, new[] { TechId.CropRotation }),
            Spec(TechId.Masonry, "석공술", "정교한 절단과 쌓기 기술로 석재 손실을 줄입니다.", "채석장 생산량 +25%", Era.Medieval, 9, 80, 3, new[] { TechId.Stonecraft }),
            Spec(TechId.Logistics, "물류", "표준 운송 규칙으로 공장 물자의 흐름을 개선합니다.", "공장 벨트 속도 +50%", Era.Renaissance, 11, 120, 3, new[] { TechId.Guilds }),
            Spec(TechId.Education, "교육", "서재의 교육 과정을 정비해 지식 전파를 빠르게 합니다.", "서재 레벨당 일일 연구 +1", Era.Renaissance, 12, 130, 4, new[] { TechId.Scholarship }),
            Spec(TechId.MetallurgicalEfficiency, "금속 공정 효율", "선광과 제련 공정을 개선해 금속 생산 손실을 줄입니다.", "광산·제련소 생산량 +20%", Era.Renaissance, 13, 145, 4, new[] { TechId.Metallurgy }),
            Spec(TechId.Electrification, "전기화", "동력망을 전기로 전환해 공장 전력 손실을 줄입니다.", "공장 전력 수요 -15%", Era.Industrial, 16, 190, 5, new[] { TechId.SteamPower }),
            Spec(TechId.MassProduction, "대량 생산", "부품 규격과 조립 공정을 표준화합니다.", "공장 기계 제작 속도 +25%", Era.Industrial, 17, 210, 5, new[] { TechId.Toolmaking }),
            Spec(TechId.Automation, "자동화", "감지와 제어 장치로 공장 운반을 자동화합니다.", "공장 투입기 속도 +50%", Era.Industrial, 20, 260, 6, new[] { TechId.Electrification, TechId.MassProduction })
        };

        public static IEnumerable<TechSpec> All => Specs;
        public static TechSpec Get(TechId id) => Specs.FirstOrDefault(s => s.Id == id);
        public static string EraName(Era era) => era == Era.Medieval ? "중세" : era == Era.Renaissance ? "르네상스" : "산업 시대";
        public static TechId RequiredTechnology(BuildingKind kind)
        {
            TechSpec spec = Specs.FirstOrDefault(s => s.UnlockBuildings.Contains(kind));
            return spec == null ? TechId.None : spec.Id;
        }
        public static bool Has(GameState state, TechId id) => id == TechId.None || (state?.Technologies?.Contains(id) ?? false);

        public static void MigrateLegacy(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Technologies ??= new List<TechId>();
            if (state.Version == 1)
            {
                state.Technologies.Clear();
                state.Technologies.AddRange(Specs.Select(s => s.Id));
                state.Era = Era.Industrial;
                state.ActiveResearch = TechId.None;
                state.ResearchDaysRemaining = 0;
                state.Version = 2;
            }
            if(state.Version==2){state.Factory=FactoryState.CreateEmpty();state.Version=3;}
            CityLogistics.Migrate(state);
            state.Technologies.RemoveAll(t => t == TechId.None || !Enum.IsDefined(typeof(TechId), t));
            state.Technologies = state.Technologies.Distinct().ToList();
        }

        private static TechSpec Spec(TechId id, string name, string description, string benefit, Era era, int research, int coins, int days, TechId[] prereqs, params BuildingKind[] unlocks) =>
            new TechSpec { Id = id, Name = name, Description = description, Benefit = benefit, Era = era, ResearchCost = research, CoinCost = coins, DurationDays = days, Prerequisites = prereqs ?? Array.Empty<TechId>(), UnlockBuildings = unlocks ?? Array.Empty<BuildingKind>() };
    }
}

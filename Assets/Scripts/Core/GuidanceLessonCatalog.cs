using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    /// <summary>Explicit values are persisted as bits in TutorialProgress.GuidanceSeenMask.</summary>
    public enum GuidanceLessonId
    {
        BuildingUpgrades = 0,
        WindmillPower = 1,
        FoodProductionChain = 2,
        HousingAndHappiness = 3,
        TerritoryExpansion = 4,
        MarketAndTrade = 5,
        Warehouse = 6,
        MiningAndMetal = 7,
        WorkshopAndTools = 8,
        SteamAndFuel = 9,
        SharedMicrogridFactory = 10,
        PowerInletAndPoles = 11,
        BeltsAndInserters = 12,
        RecipesAndBuffers = 13,
        SplitterFiltersAndBackpressure = 14,
        PhysicalCityImportsExports = 15,
        OptionalResearch = 16,
        ResidentLlmSettings = 17,
        SaveBackupAndReducedEffects = 18
    }

    public sealed class GuidanceLessonDefinition
    {
        public GuidanceLessonId Id;
        public string StableId = "";
        public string Title = "";
        public string Goal = "";
        public string[] Pages = Array.Empty<string>();
        public TechId RequiredTech;
        public int MinimumPopulation;
    }

    /// <summary>
    /// Feature lessons for systems that exist in the current game. Automatic selection is a pure
    /// state query; page timing and the final acknowledgement belong to runtime presentation.
    /// </summary>
    public static class GuidanceLessonCatalog
    {
        public const int Count = 19;
        public const int KnownSeenMask = (1 << Count) - 1;

        private static readonly GuidanceLessonDefinition[] Lessons =
        {
            Lesson(GuidanceLessonId.BuildingUpgrades, "building-upgrades", "건물 증축",
                TechId.Stonecraft, 0,
                "석조술을 익히면 시청·도로를 제외한 건물을 2단계로 증축할 수 있어요. 건물을 고른 뒤 오른쪽 정보창의 증축 버튼을 확인해 보세요.",
                "증축은 코인과 자재를 더 쓰는 대신 한 부지의 수용량이나 생산량을 높여요. 도시 계획을 연구하면 3단계까지 올릴 수 있답니다."),
            Lesson(GuidanceLessonId.WindmillPower, "windmill-power", "풍차와 도시 전력",
                TechId.MechanicalPower, 0,
                "기계 동력을 익히면 풍차, 제분소, 제과점이 열려요. 도로에 연결된 풍차는 단계마다 전력 8을 공급해요.",
                "제분소·제과점과 훗날의 광산·제련소·공방은 전력을 사용해요. 전력이 모자라면 생산 상태가 멈추니 상단 전력 수치를 살펴보세요."),
            Lesson(GuidanceLessonId.FoodProductionChain, "food-production-chain", "곡물에서 빵까지",
                TechId.MechanicalPower, 10,
                "농장은 곡물을 만들고, 제분소는 곡물을 밀가루로 바꿔요. 인구 10명부터 제분소를 지을 수 있고 풍차 전력이 필요해요.",
                "인구 14명이 되면 제과점이 열려 밀가루를 빵으로 만들어요. 세 시설을 도로에 연결하고 재고가 어느 단계에서 막히는지 차례로 보세요."),
            Lesson(GuidanceLessonId.HousingAndHappiness, "housing-and-happiness", "주택과 행복",
                TechId.None, 12,
                "연결된 주택은 단계마다 주민 6명이 살 자리를 만들어요. 빵이 부족하면 곡물로 버틸 수 있지만, 식량이 계속 모자라면 행복과 성장이 낮아져요.",
                "인구 12명부터 공원을 지어 생활 환경을 보탤 수 있어요. 주택의 연결 상태, 식량 재고, 행복도를 함께 보면 성장 정체의 원인을 찾기 쉬워요."),
            Lesson(GuidanceLessonId.TerritoryExpansion, "territory-expansion", "영토 확장",
                TechId.None, 12,
                "영토 창에서는 현재 구역과 변을 맞댄 구역만 살 수 있어요. 첫 확장 비용은 320코인이며, 가진 구역이 늘수록 다음 가격도 올라가요.",
                "새 구역을 사기 전에 숲과 바위, 강의 위치를 살펴보세요. 구입한 땅에도 시청에서 이어진 도로가 있어야 건물이 제대로 일해요."),
            Lesson(GuidanceLessonId.MarketAndTrade, "market-and-trade", "시장과 교역",
                TechId.Guilds, 22,
                "길드를 익히고 인구 22명이 되면 시장을 지을 수 있어요. 연결된 시장은 빵 재고가 아주 낮을 때 코인으로 비상 식량을 들여와요.",
                "교역 창의 구매와 판매는 버튼을 눌렀을 때만 실행돼요. 가격과 보유량을 확인하고, 생산이 안정될 때 남는 물자만 파는 편이 안전해요."),
            Lesson(GuidanceLessonId.Warehouse, "warehouse", "도시 창고",
                TechId.Guilds, 18,
                "길드와 인구 18명이 준비되면 도시 창고를 지을 수 있어요. 연결된 창고는 생산 효율을 조금 높이고 물류 설비와 도시 재고가 만나는 지점이 돼요.",
                "창고도 도로에서 끊기면 공용 재고를 주고받지 못해요. 자동 물류를 붙이기 전에는 먼저 연결 표시를 확인하세요."),
            Lesson(GuidanceLessonId.MiningAndMetal, "mining-and-metal", "광석과 강철",
                TechId.Metallurgy, 25,
                "금속학을 익히면 광산과 제련소가 열려요. 광산은 바위 위나 가까이에 놓고 전력을 공급해야 광석을 만들어요.",
                "인구 25명이 되면 제련소가 광석을 강철로 바꿀 수 있어요. 광산과 제련소의 도로·전력·입력 재고를 순서대로 확인해 보세요."),
            Lesson(GuidanceLessonId.WorkshopAndTools, "workshop-and-tools", "공방과 도구",
                TechId.Toolmaking, 30,
                "도구 제작을 익히고 인구 30명이 되면 공방이 열려요. 공방은 강철 1과 목재 1을 사용해 도구를 만들며 전력도 필요해요.",
                "도구는 산업 목표의 실제 생산 기록에 쓰이고, 인구 24명부터는 주민 생활에도 선택적으로 공급돼 세수와 행복을 보탤 수 있어요."),
            Lesson(GuidanceLessonId.SteamAndFuel, "steam-and-fuel", "증기 동력과 연료",
                TechId.SteamPower, 28,
                "증기력을 익히고 인구 28명이 되면 증기 동력소를 지을 수 있어요. 연결된 동력소는 단계마다 하루에 목재 0.6을 태워 전력 18을 공급해요.",
                "연료가 부족한 동력소는 전력을 만들지 못해요. 목재 생산과 재고를 먼저 안정시키고 전력 수요가 큰 금속 시설을 늘리세요."),
            Lesson(GuidanceLessonId.SharedMicrogridFactory, "shared-microgrid-factory", "같은 도시의 미세 격자",
                TechId.SteamPower, 0,
                "산업 설비는 별도 지도가 아니라 지금 보는 21×21 도시 위에 놓여요. 도시 한 칸을 2×2로 나눈 42×42 미세 격자를 사용해요.",
                "큰 설비는 도시 한 칸을 차지하고 경계에 맞춰 놓아야 해요. 도시 건물과 설비는 같은 부지를 쓰므로 배치 미리보기에서 충돌 여부를 확인하세요."),
            Lesson(GuidanceLessonId.PowerInletAndPoles, "power-inlet-and-poles", "전력 인입구와 전신주",
                TechId.SteamPower, 0,
                "전력 인입구는 연결된 도시 도로에 닿아야 남는 도시 전력을 설비망으로 가져와요. 도시 발전량을 넘겨 쓸 수는 없어요.",
                "전신주는 인입구에서 전력을 이어 주며 설비 점유 영역 사이 6칸까지 연결해요. 설비 정보창의 전력 상태와 보이는 전선을 함께 확인하세요."),
            Lesson(GuidanceLessonId.BeltsAndInserters, "belts-and-inserters", "벨트와 투입기 방향",
                TechId.SteamPower, 0,
                "벨트는 화살표 방향으로 물자를 한 칸씩 옮겨요. 회전 버튼이나 R 키로 놓기 전 방향을 맞추고, 도로 위에는 경량 물류 설비를 함께 둘 수 있어요.",
                "투입기는 뒤쪽 한 칸에서 집어 앞쪽 한 칸에 놓아요. 전력이 필요하므로 멈췄다면 방향뿐 아니라 전신주 연결도 살펴보세요."),
            Lesson(GuidanceLessonId.RecipesAndBuffers, "recipes-and-buffers", "제조법과 입출력 버퍼",
                TechId.SteamPower, 0,
                "용광로와 조립기는 정보창에서 실제 지원 제조법을 고를 수 있어요. 제조법을 바꾸면 설비 안의 물자는 회수되고 진행 중이던 공정은 처음부터 다시 시작해요.",
                "입력과 출력은 각각 제한된 물리 버퍼에 머물러요. 필요한 원료가 없거나 출력 버퍼가 가득 차면 생산이 기다리므로 정보창 수치를 확인하세요."),
            Lesson(GuidanceLessonId.SplitterFiltersAndBackpressure, "splitter-filters-backpressure", "분배기·필터·정체",
                TechId.SteamPower, 0,
                "분배기는 들어온 물자를 앞과 옆 출구로 번갈아 보내요. 투입기 필터를 고르면 지정한 물자만 집어 혼합 벨트의 흐름을 나눌 수 있어요.",
                "받는 곳이 가득 차면 물자는 사라지지 않고 앞 단계에서 기다려요. 막힌 출력부터 거꾸로 따라가면 정체 원인을 찾기 쉬워요."),
            Lesson(GuidanceLessonId.PhysicalCityImportsExports, "physical-city-imports-exports", "도시와 설비 물류",
                TechId.Guilds, 0,
                "반입 부두의 정보창에서는 도시 재고를 실제로 옮겨 담을 수 있어요. 연결된 도시 창고와 시청도 투입기를 붙이면 공용 재고의 물자를 설비 흐름으로 꺼내요.",
                "반출 부두는 연결된 도로에 닿아야 물자를 도시 재고로 돌려보내요. 연결이 끊기면 물자는 부두에서 안전하게 기다려요.",
                "이동 중 물자와 도시 건물의 입출력 버퍼는 실제로 저장돼요. 같은 물자를 도시 재고와 설비 재고에 동시에 더하지 않으므로 흐름의 한쪽씩 확인하세요."),
            Lesson(GuidanceLessonId.OptionalResearch, "optional-research", "선택 연구 가지",
                TechId.CropRotation, 0,
                "핵심 시대 연구 외에도 산림 경영, 관개, 석공술처럼 현재 생산을 강화하는 선택 연구가 있어요. 필요한 자원 흐름에 맞는 가지부터 골라 보세요.",
                "선택 연구를 전부 끝내야 다음 시대로 가는 것은 아니에요. 연구 창의 선행 기술과 효과를 비교하고 도시 문제에 맞춰 천천히 익히면 돼요."),
            Lesson(GuidanceLessonId.ResidentLlmSettings, "resident-llm-settings", "주민 AI 연결 설정",
                TechId.None, 16,
                "메뉴의 주민 AI는 호환되는 Riverworks 게이트웨이 주소가 따로 준비됐을 때만 LLM 계획을 요청해요. 설정 화면이 보인다는 것만으로 연결 준비가 끝난 것은 아니에요.",
                "게이트웨이와 제공자 자격 증명을 별도로 준비한 뒤 연결 테스트가 준비됨을 보여야 해요. 그전이나 오류가 날 때 주민은 안전하게 로컬 규칙으로 움직여요.",
                "접근 토큰은 필요할 때만 입력하며 실행 세션에만 머물러요. 적용 여부와 모델 출처는 주민 정보와 상태 화면에서 직접 확인하세요."),
            Lesson(GuidanceLessonId.SaveBackupAndReducedEffects, "save-backup-reduced-effects", "저장 백업과 화면 효과",
                TechId.None, 0,
                "메뉴 저장과 F5는 현재 도시를 저장하고, 기존 파일을 바꿀 때 이전 정상본을 .bak으로 남겨요. 새 도시를 시작할 때는 이전 도시도 별도 파일로 보관해요.",
                "메뉴의 화면 효과 설정을 절제로 바꾸면 흔들림과 큰 움직임을 줄여요. 이 선택은 따로 기억되며, 건설·연구·저장 규칙은 그대로 유지돼요.")
        };

        private static readonly Dictionary<GuidanceLessonId, GuidanceLessonDefinition> ById =
            Lessons.ToDictionary(lesson => lesson.Id);

        public static IEnumerable<GuidanceLessonDefinition> All => Lessons;

        public static GuidanceLessonDefinition Get(GuidanceLessonId id) =>
            ById.TryGetValue(id, out GuidanceLessonDefinition lesson) ? lesson : null;

        public static bool IsUnlocked(GameState state, GuidanceLessonId id)
        {
            GuidanceLessonDefinition lesson = Get(id);
            return state != null && lesson != null && state.Population >= lesson.MinimumPopulation &&
                   TechCatalog.Has(state, lesson.RequiredTech);
        }

        public static bool IsSeen(GameState state, GuidanceLessonId id)
        {
            TutorialProgress progress = state?.Tutorial;
            return progress != null && Get(id) != null && (progress.GuidanceSeenMask & Bit(id)) != 0;
        }

        public static GuidanceLessonDefinition GetNextUnseenUnlocked(GameState state)
        {
            TutorialProgress progress = state?.Tutorial;
            if (progress == null || !progress.Completed || !progress.GuidanceEnabled ||
                !IsValidSeenMask(progress.GuidanceSeenMask)) return null;
            foreach (GuidanceLessonDefinition lesson in Lessons)
                if (!IsSeen(state, lesson.Id) && IsUnlocked(state, lesson.Id)) return lesson;
            return null;
        }

        /// <summary>
        /// Records one fully read lesson. Runtime owns page reveal and acknowledgement timing.
        /// Manual advice may mark a lesson even before its automatic unlock condition is met.
        /// </summary>
        public static bool MarkSeen(GameState state, GuidanceLessonId id)
        {
            TutorialProgress progress = state?.Tutorial;
            if (progress == null || Get(id) == null || !IsValidSeenMask(progress.GuidanceSeenMask)) return false;
            int bit = Bit(id);
            if ((progress.GuidanceSeenMask & bit) != 0) return false;
            progress.GuidanceSeenMask |= bit;
            return true;
        }

        public static bool IsValidSeenMask(int mask) => mask >= 0 && (mask & ~KnownSeenMask) == 0;

        private static int Bit(GuidanceLessonId id) => 1 << (int)id;

        private static GuidanceLessonDefinition Lesson(GuidanceLessonId id, string stableId, string title,
            TechId requiredTech, int minimumPopulation, params string[] pages) =>
            new GuidanceLessonDefinition
            {
                Id = id,
                StableId = stableId,
                Title = title,
                Goal = "기능 안내를 끝까지 읽어 보세요.",
                Pages = pages ?? Array.Empty<string>(),
                RequiredTech = requiredTech,
                MinimumPopulation = minimumPopulation
            };
    }
}

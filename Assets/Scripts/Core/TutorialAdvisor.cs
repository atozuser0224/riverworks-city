using System;

namespace Riverworks
{
    public enum AdvisorTopic
    {
        City = 0,
        Production = 1,
        Research = 2,
        Factory = 3,
        Territory = 4
    }

    /// <summary>Deterministic local advice from village clerk Danwoo. No network or model calls.</summary>
    public static class TutorialAdvisor
    {
        public const string Name = "단우";

        public static string Get(AdvisorTopic topic, GameState state)
        {
            if (!Enum.IsDefined(typeof(AdvisorTopic), topic)) return "그 주제는 아직 제 장부에 없어요.";
            switch (topic)
            {
                case AdvisorTopic.City:
                    return CityAdvice(state);
                case AdvisorTopic.Production:
                    return ProductionAdvice(state);
                case AdvisorTopic.Research:
                    return ResearchAdvice(state);
                case AdvisorTopic.Factory:
                    return FactoryAdvice(state);
                case AdvisorTopic.Territory:
                    return TerritoryAdvice(state);
                default:
                    return "그 주제는 아직 제 장부에 없어요.";
            }
        }

        private static string CityAdvice(GameState state)
        {
            string condition = state != null && state.Happiness < 55
                ? "지금은 행복도가 낮아요. 빵 재고와 끊긴 주택부터 살펴보면 회복이 빨라요."
                : "주택을 도로에 붙여 늘리고, 빵 재고가 줄기 전에 생산을 보태면 주민이 안정적으로 자라요.";
            return "도시는 시청에서 이어진 도로망을 중심으로 움직여요. 건물이 있어도 연결 표시가 없으면 생산과 생활이 멈춰요.\n\n" + condition;
        }

        private static string ProductionAdvice(GameState state)
        {
            string stock = state != null && state.Stock != null && state.Stock.Count > (int)Resource.Timber && state.Stock[(int)Resource.Timber] < 20
                ? "목재가 얼마 남지 않았으니 연결된 벌목장을 먼저 보태는 편이 좋아요."
                : "벌목장·채석장·농장처럼 원료를 만드는 시설부터 안정시키고 다음 가공 시설을 잇는 편이 좋아요.";
            return "생산 건물은 알맞은 지형과 도로 연결이 모두 필요해요. 가공 시설은 원료와 전력까지 확인해 주세요.\n\n" + stock;
        }

        private static string ResearchAdvice(GameState state)
        {
            string next = state != null && TechCatalog.Has(state, TechId.CropRotation)
                ? "윤작을 익혔으니 다음 핵심 길은 기계 동력이에요. 풍차와 식량 가공 시설이 열려요."
                : "첫 연구로 윤작을 익히면 농장 생산이 늘고 기계 동력으로 가는 길이 열려요.";
            return "서재와 학술원은 매일 들어오는 연구 점수를 늘려 줘요. 연구는 시작 비용을 낸 뒤 실제 도시 날짜가 지나야 완료돼요.\n\n" + next;
        }

        private static string FactoryAdvice(GameState state)
        {
            string next = state != null && TechCatalog.Has(state, TechId.MechanicalPower)
                ? "기계 동력을 익혔군요. 전력 인입구와 전신주를 먼저 잇고, 투입·가공·반출 순서로 짧게 시험해 보세요."
                : "먼저 윤작 다음의 기계 동력을 연구해 생산 흐름의 기초를 익혀 두면 좋아요.";
            return "산업 설비는 별도 세계가 아니라 같은 21×21 도시 위의 42×42 미세 격자를 써요. 도시 한 칸은 설비 두 칸이고, 도로·건물과 자리를 함께 사용해요.\n\n" + next;
        }

        private static string TerritoryAdvice(GameState state)
        {
            string count = state?.OwnedRegions == null ? "현재 영토 장부를 읽을 수 없어요."
                : $"현재 소유 구역은 {state.OwnedRegions.Count}곳이에요. 자원 지형과 도로를 먼저 살핀 뒤 다음 구역을 고르세요.";
            return "새 구역은 이미 가진 구역과 변을 맞댄 곳만 살 수 있어요. 확장할수록 다음 구역의 가격도 올라가요.\n\n" + count;
        }
    }
}

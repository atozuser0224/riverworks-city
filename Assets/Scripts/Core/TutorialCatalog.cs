using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    public sealed class TutorialStepDefinition
    {
        public TutorialStepId Id;
        public string StableId = "";
        public string Speaker = "단우";
        public string Title = "";
        public string Dialogue = "";
        public string Goal = "";
        public BuildingKind HighlightBuilding;
        public int PreferredX = -1;
        public int PreferredZ = -1;
    }

    /// <summary>Pure tutorial goals plus the only guarded progress transition.</summary>
    public static class TutorialCatalog
    {
        private static readonly TutorialStepDefinition[] Steps =
        {
            Step(TutorialStepId.Welcome, "welcome", "단우와 인사하기",
                "어서 오세요, 시장님. 저는 마을 서기 단우예요.\n\n작은 일부터 함께 해 볼게요. 준비되면 이야기를 끝까지 읽고 계속을 눌러 주세요.",
                "단우의 인사를 확인하세요."),
            Step(TutorialStepId.SelectTownHall, "select-town-hall", "시청 살펴보기",
                "마을의 중심은 시청이에요. 건물을 선택하면 하는 일과 상태를 확인할 수 있어요.\n\n가운데 시청을 직접 선택해 볼까요?", "시청을 선택하세요.", BuildingKind.TownHall, 10, 10),
            Step(TutorialStepId.ExtendRoad, "extend-connected-road", "도로 잇기",
                "모든 건물은 시청에서 이어진 도로가 필요해요. 끊긴 길 옆의 건물은 일을 하지 못해요.\n\n시청 북쪽 길을 한 칸 더 이어 보세요. 다른 연결 지점에 지어도 괜찮아요.",
                "기존 도로망에 연결된 새 도로를 건설하세요.", BuildingKind.Road, 10, 7),
            Step(TutorialStepId.BuildHouse, "build-connected-house", "새 이웃 맞이하기",
                "주택은 주민이 머물 자리를 만들어요. 도로에 닿아야 새 이웃이 편히 오갈 수 있답니다.\n\n방금 이은 길 곁에 주택을 지어 보세요.",
                "도로망에 연결된 새 주택을 건설하세요.", BuildingKind.House, 9, 7),
            Step(TutorialStepId.BuildLumberyard, "build-connected-lumberyard", "숲에서 목재 얻기",
                "벌목장은 숲 위나 숲 가까이에 두어야 해요. 생산 건물도 길이 이어져야 물자를 보낼 수 있고요.\n\n서쪽 숲의 7, 10 지점이 안전한 첫 자리예요.",
                "도로망에 연결된 새 벌목장을 건설하세요.", BuildingKind.Lumberyard, 7, 10),
            Step(TutorialStepId.BuildStudyHouse, "build-connected-study-house", "서재 세우기",
                "서재는 주민의 지식을 모아 매일 연구 점수를 더해 줘요. 우리 마을의 다음 시대를 준비하는 곳이죠.\n\n도로 옆 11, 8 지점에 서재를 세워 보세요.",
                "도로망에 연결된 새 서재를 건설하세요.", BuildingKind.StudyHouse, 11, 8),
            Step(TutorialStepId.StartCropRotation, "start-crop-rotation", "윤작 연구 시작",
                "이제 연구 창에서 윤작을 골라 볼까요? 연구를 시작할 때 비용과 연구 점수를 한 번 사용해요.\n\n윤작은 농장 생산량을 높이는 첫 기술이에요.",
                "윤작 연구를 시작하세요."),
            Step(TutorialStepId.CompleteCropRotation, "complete-crop-rotation", "시간 흘려보기",
                "연구는 도시의 하루가 지나야 진행돼요. 일시정지를 풀고 시간이 흐르도록 해 주세요.\n\n윤작이 실제로 완료될 때까지 기다려 볼까요?", "일시정지를 풀고 윤작 연구를 완료하세요."),
            Step(TutorialStepId.MechanicalPowerAdvice, "mechanical-power-advice", "다음 발전 살펴보기",
                "훌륭해요. 다음 핵심 연구는 기계 동력이에요. 풍차와 제분소, 제과점으로 생산 흐름을 넓힐 수 있어요.\n\n훗날 산업 설비를 놓을 때도 같은 도시의 42×42 미세 격자를 쓰며, 도로·전력·입출력 흐름을 함께 살피면 돼요.",
                "단우의 다음 발전 조언을 확인하세요."),
            Step(TutorialStepId.SaveCity, "save-city", "도시 저장하기",
                "여기까지 만든 도시는 저장해 두면 안심이에요. 메뉴의 저장 버튼을 직접 눌러 주세요.\n\n저장이 성공했다는 확인을 받은 뒤에만 다음으로 넘어갈게요.",
                "메뉴에서 현재 도시를 성공적으로 저장하세요."),
            Step(TutorialStepId.Finish, "finish", "첫걸음 마치기",
                "이제 혼자서도 마을을 차근차근 키울 수 있겠어요. 막힐 때는 언제든 단우의 조언을 펼쳐 보세요.\n\n도로, 생산, 연구를 하나씩 잇다 보면 멋진 도시가 될 거예요.",
                "마지막 인사를 확인하세요.")
        };

        private static readonly Dictionary<TutorialStepId, TutorialStepDefinition> ById =
            Steps.ToDictionary(step => step.Id);

        public static IEnumerable<TutorialStepDefinition> All => Steps;

        public static TutorialStepDefinition Get(TutorialStepId id) =>
            ById.TryGetValue(id, out TutorialStepDefinition definition) ? definition : null;

        public static TutorialStepDefinition Current(GameState state) =>
            state?.Tutorial == null ? null : Get(state.Tutorial.Step);

        public static bool EvaluateGoal(GameState state, TutorialFacts facts)
        {
            TutorialProgress progress = state?.Tutorial;
            if (progress == null || !progress.Enabled || progress.Completed || progress.Skipped) return false;
            switch (progress.Step)
            {
                case TutorialStepId.Welcome:
                case TutorialStepId.MechanicalPowerAdvice:
                case TutorialStepId.Finish:
                    return true;
                case TutorialStepId.SelectTownHall:
                    return SelectedTownHall(state, facts);
                case TutorialStepId.ExtendRoad:
                    return ConnectedCount(state, BuildingKind.Road) > progress.RoadBaseline;
                case TutorialStepId.BuildHouse:
                    return ConnectedCount(state, BuildingKind.House) > progress.HouseBaseline;
                case TutorialStepId.BuildLumberyard:
                    return ConnectedCount(state, BuildingKind.Lumberyard) > progress.LumberyardBaseline;
                case TutorialStepId.BuildStudyHouse:
                    return ConnectedCount(state, BuildingKind.StudyHouse) > progress.StudyHouseBaseline;
                case TutorialStepId.StartCropRotation:
                    return state.ActiveResearch == TechId.CropRotation || TechCatalog.Has(state, TechId.CropRotation);
                case TutorialStepId.CompleteCropRotation:
                    return TechCatalog.Has(state, TechId.CropRotation);
                case TutorialStepId.SaveCity:
                    return facts.SaveSucceeded && facts.SuccessfulSaveVersion == state.Version;
                default:
                    return false;
            }
        }

        public static bool CanAdvance(GameState state, TutorialFacts facts) =>
            EvaluateGoal(state, facts) && facts.DialogueAcknowledged;

        /// <summary>
        /// Advances only after both the real goal and the fully acknowledged dialogue are true.
        /// It changes tutorial progress only; it never builds, researches, ticks, saves, or grants resources.
        /// </summary>
        public static bool TryAdvance(GameState state, TutorialFacts facts)
        {
            if (!CanAdvance(state, facts)) return false;
            TutorialProgress progress = state.Tutorial;
            if (progress.Step == TutorialStepId.Finish)
            {
                progress.Completed = true;
                progress.Enabled = false;
                progress.Skipped = false;
                progress.Collapsed = false;
                return true;
            }
            progress.Step = (TutorialStepId)((int)progress.Step + 1);
            progress.Collapsed = false;
            return true;
        }

        public static bool TryValidate(GameState state, out string error)
        {
            error = "";
            TutorialProgress progress = state?.Tutorial;
            if (progress == null) return true;
            if (!Enum.IsDefined(typeof(TutorialStepId), progress.Step) || Get(progress.Step) == null)
                return Fail("알 수 없는 튜토리얼 단계입니다.", out error);
            if (progress.RoadBaseline < 0 || progress.HouseBaseline < 0 || progress.LumberyardBaseline < 0 ||
                progress.StudyHouseBaseline < 0 || progress.StartDay < 0)
                return Fail("튜토리얼 기준값은 음수일 수 없습니다.", out error);
            if (!GuidanceLessonCatalog.IsValidSeenMask(progress.GuidanceSeenMask))
                return Fail("알 수 없는 기능 안내 기록이 들어 있습니다.", out error);
            int cellCount = state.Cells?.Count ?? 0;
            if (progress.RoadBaseline > cellCount || progress.HouseBaseline > cellCount ||
                progress.LumberyardBaseline > cellCount || progress.StudyHouseBaseline > cellCount ||
                progress.StartDay > state.Day)
                return Fail("튜토리얼 기준값이 도시 범위를 벗어났습니다.", out error);
            int terminalFlags = (progress.Completed ? 1 : 0) + (progress.Skipped ? 1 : 0);
            if (terminalFlags > 1 || (progress.Enabled && terminalFlags != 0) ||
                (!progress.Enabled && terminalFlags == 0) ||
                (progress.Completed && progress.Step != TutorialStepId.Finish))
                return Fail("튜토리얼 활성 상태가 서로 맞지 않습니다.", out error);
            return true;
        }

        private static bool SelectedTownHall(GameState state, TutorialFacts facts)
        {
            if (!facts.HasSelectedCell || facts.SelectedBuilding != BuildingKind.TownHall ||
                facts.SelectedX < 0 || facts.SelectedZ < 0 || facts.SelectedX >= state.Size || facts.SelectedZ >= state.Size)
                return false;
            Cell selected = state.Cells[facts.SelectedZ * state.Size + facts.SelectedX];
            return selected != null && selected.Building == BuildingKind.TownHall;
        }

        private static int ConnectedCount(GameState state, BuildingKind kind) =>
            state.Cells == null ? 0 : state.Cells.Count(cell => cell != null && cell.Connected && cell.Building == kind);

        private static TutorialStepDefinition Step(TutorialStepId id, string stableId, string title, string dialogue,
            string goal, BuildingKind highlight = BuildingKind.None, int x = -1, int z = -1) =>
            new TutorialStepDefinition
            {
                Id = id,
                StableId = stableId,
                Title = title,
                Dialogue = dialogue,
                Goal = goal,
                HighlightBuilding = highlight,
                PreferredX = x,
                PreferredZ = z
            };

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}

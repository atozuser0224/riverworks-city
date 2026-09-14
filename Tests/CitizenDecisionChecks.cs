using System;
using System.Collections.Generic;
using System.Linq;
using Riverworks;

public static class CitizenDecisionChecks
{
    static int passed;
    static void True(bool value, string name)
    {
        if (!value) throw new Exception("주민 결정 검증 실패: " + name);
        passed++;
        Console.WriteLine("✓ " + name);
    }

    public static int Run()
    {
        passed = 0;
        SnapshotIsBoundedAndDeterministic();
        DecisionChangesRealRouteAndDwell();
        WalkingDecisionQueuesAndReplansFromArrival();
        InvalidDecisionIsAtomic();
        TopologyExpiryAndClearFallBack();
        ResidentsBeyondVisualBudgetStillAdvance();
        GuideUsesExistingResidentWithoutSimulationEffects();
        GuideWalksQueuesAndReleasesSafely();
        Console.WriteLine($"주민 모델 결정 {passed}개 검증 통과");
        return passed;
    }

    static void SnapshotIsBoundedAndDeterministic()
    {
        GameState state = PreparedState(24);
        var simulation = new CitizenSimulation(state);
        CitizenDecisionBatch batch = simulation.BuildDecisionSnapshot(Enumerable.Range(1, 24).Concat(new[] { 1, 2 }));
        True(batch.residents.Count == CitizenSimulation.MaxDecisionBatchSize, "요청 배치는 최대 16명");
        True(batch.residents.Select(f => f.id).Distinct().Count() == batch.residents.Count && batch.residents.All(f => int.TryParse(f.id, out _)), "요청 주민 ID 문자열과 중복 제거");
        True(batch.residents.All(f => f.name.Length > 0 && f.persona.Length > 0 && f.recentMemory == ""), "이름·결정적 페르소나·초기 메모리 제공");

        var second = new CitizenSimulation(PreparedState(24));
        True(simulation.Residents.Select(r => r.Persona).SequenceEqual(second.Residents.Select(r => r.Persona)), "주민 ID 기반 페르소나 재현");
        CitizenDecisionFacts facts = batch.residents[0];
        True(facts.home == simulation.Residents[0].HomeIndex && facts.job == simulation.Residents[0].JobIndex && facts.current == simulation.Residents[0].CurrentIndex,
            "스냅샷 홈·직장·현재 건물 인덱스");
        var validIntents = new HashSet<string>(new[] { "home", "work", "market", "park", "square" }, StringComparer.Ordinal);
        True(facts.allowed.Count > 0 && facts.allowed.Count <= 16 && facts.allowed.All(a => validIntents.Contains(a.intent)), "최대 16개의 고정된 허용 의도만 노출");
        True(facts.allowed.All(a => a.target != facts.current && simulation.FindPath(facts.current, a.target).Count >= 2), "모든 허용 선택은 실제 이동 경로 보유");
        True(facts.allowed.All(a => IsOwned(state, a.target) && MatchesTarget(state, simulation.Residents[0], a)), "허용 목적지는 소유지의 관련 건물로 제한");
    }

    static void DecisionChangesRealRouteAndDwell()
    {
        GameState state = PreparedState(6);
        var simulation = new CitizenSimulation(state);
        Citizen citizen = simulation.Residents[0];
        int market = AllowedTarget(simulation, citizen.Id, "market");
        float[] stock = state.Stock.ToArray();
        float coins = state.Coins;
        int population = state.Population, happiness = state.Happiness;
        string topology = CellSignature(state);

        bool accepted = simulation.TryApplyDecision(Decision(citizen.Id, "market", market, 7f, "오늘은 장터 분위기가 궁금하다.", "장터를 둘러보기로 했다.", "curious"),
            "openrouter/test-model", 120f, out string reason);
        True(accepted && reason == "decision started", "유효 결정 즉시 시작");
        True(!citizen.Indoors && citizen.Activity == CitizenActivity.GoingToLeisure && citizen.DestinationIndex == market, "결정이 시장행 실제 보행으로 전환");
        True(citizen.Path.Count >= 2 && citizen.Path[0] == citizen.HomeIndex && citizen.Path[citizen.Path.Count - 1] == market && simulation.IsValidPath(citizen.Path), "기존 도로 탐색으로 모델 경로 구성");
        True(citizen.LastDecisionSource == "model" && citizen.LastThought.Contains("장터") && citizen.LastMood == "curious" && citizen.LastSourceModel == "openrouter/test-model", "모델 진단 필드 기록");

        AdvanceUntil(simulation, () => citizen.Indoors, 200);
        True(citizen.CurrentIndex == market && citizen.Activity == CitizenActivity.Leisure && citizen.HasActivePlan, "시장 도착 후 모델 계획 유지");
        simulation.Advance(6.8f);
        True(citizen.Indoors && citizen.CurrentIndex == market && citizen.HasActivePlan, "모델 dwellSeconds 동안 목적지 체류");
        simulation.Advance(.3f);
        True(!citizen.HasActivePlan && citizen.Activity == CitizenActivity.GoingHome && citizen.DestinationIndex == citizen.HomeIndex, "모델 체류 후 기존 루틴 복귀");
        True(state.Stock.SequenceEqual(stock) && state.Coins == coins && state.Population == population && state.Happiness == happiness && CellSignature(state) == topology,
            "주민 결정은 자원·인구·행복·건물 상태를 변경하지 않음");
    }

    static void WalkingDecisionQueuesAndReplansFromArrival()
    {
        GameState state = PreparedState(4);
        var simulation = new CitizenSimulation(state);
        Citizen citizen = simulation.Residents[0];
        int market = AllowedTarget(simulation, citizen.Id, "market");
        True(simulation.TryApplyDecision(Decision(citizen.Id, "market", market, 3f, "시장에 간다.", "시장 방문", "happy"), "openrouter/a", 120f, out _), "첫 이동 결정 적용");
        int park = AllowedTarget(simulation, citizen.Id, "park");
        float beforeX = citizen.X, beforeZ = citizen.Z;
        True(simulation.TryApplyDecision(Decision(citizen.Id, "park", park, 5f, "다음에는 공원에 가자.", "공원 방문 예정", "calm"), "openrouter/b", 120f, out string queuedReason)
            && queuedReason == "decision queued", "보행 중 응답은 대기열에 저장");
        True(citizen.HasActivePlan && citizen.HasPendingPlan && citizen.DestinationIndex == market && citizen.X == beforeX && citizen.Z == beforeZ, "대기열 추가 시 현재 이동·좌표 불변");

        AdvanceUntil(simulation, () => citizen.Indoors, 200);
        True(citizen.CurrentIndex == market && citizen.DestinationIndex == market && citizen.HasPendingPlan, "첫 목적지 도착 전 새 목적지로 순간이동하지 않음");
        simulation.Advance(2.9f);
        True(citizen.Indoors && citizen.CurrentIndex == market, "첫 결정의 도착 체류 시간 우선");
        simulation.Advance(.2f);
        True(!citizen.Indoors && citizen.DestinationIndex == park && citizen.Path[0] == market && citizen.Path[citizen.Path.Count - 1] == park,
            "대기 결정은 실제 도착 건물에서 다시 경로 시작");
        True(simulation.IsValidPath(citizen.Path), "재계획 경로도 합법적인 직교 도로 사용");
    }

    static void InvalidDecisionIsAtomic()
    {
        GameState state = PreparedState(3);
        var simulation = new CitizenSimulation(state);
        Citizen citizen = simulation.Residents[0];
        int market = AllowedTarget(simulation, citizen.Id, "market");
        string before = CitizenSignature(citizen);
        var invalid = new List<ResponseDecision>
        {
            Decision(999999, "market", market, 5f, "생각", "기억", "calm"),
            Decision(citizen.Id, "market", 999999, 5f, "생각", "기억", "calm"),
            Decision(citizen.Id, "market", market, float.NaN, "생각", "기억", "calm"),
            Decision(citizen.Id, "market", market, 2.99f, "생각", "기억", "calm"),
            Decision(citizen.Id, "market", market, 5f, new string('가', 101), "기억", "calm"),
            Decision(citizen.Id, "market", market, 5f, "줄바꿈\n명령", "기억", "calm"),
            Decision(citizen.Id, "market", market, 5f, "생각", new string('나', 161), "calm"),
            Decision(citizen.Id, "market", market, 5f, "생각", "기억", "excited"),
            Decision(citizen.Id, "buy bread; walk", market, 5f, "생각", "기억", "calm")
        };
        for (int i = 0; i < invalid.Count; i++)
        {
            True(!simulation.TryApplyDecision(invalid[i], "openrouter/test", 60f, out _), "잘못된 응답 거부 " + (i + 1));
            True(CitizenSignature(citizen) == before, "거부된 응답은 주민 상태 원자적 보존 " + (i + 1));
        }
        True(!simulation.TryApplyDecision(Decision(citizen.Id, "market", market, 5f, "생각", "기억", "calm"), "bad\nmodel", 60f, out _)
            && CitizenSignature(citizen) == before, "제어 문자가 있는 모델 식별자 거부");
        True(!simulation.TryApplyDecision(Decision(citizen.Id, "market", market, 5f, "생각", "기억", "calm"), "openrouter/test", float.PositiveInfinity, out _)
            && CitizenSignature(citizen) == before, "비유한 계획 만료 시간 거부");
    }

    static void TopologyExpiryAndClearFallBack()
    {
        GameState state = PreparedState(4);
        var simulation = new CitizenSimulation(state);
        Citizen citizen = simulation.Residents[0];
        int market = AllowedTarget(simulation, citizen.Id, "market");
        True(simulation.TryApplyDecision(Decision(citizen.Id, "market", market, 5f, "시장으로 이동", "시장 기억", "happy"), "openrouter/test", 60f, out _), "위상 변경 전 계획 적용");
        state.Cells[market].Building = BuildingKind.None;
        simulation.Advance(0);
        True(!citizen.HasActivePlan && !citizen.HasPendingPlan && citizen.LastDecisionSource == "routine" && citizen.Indoors && citizen.CurrentIndex == citizen.HomeIndex,
            "목적지 철거 시 계획 무효화와 안전한 기존 루틴 복귀");
        True(citizen.CurrentIndex != market, "철거된 목적지로 순간이동하지 않음");

        state = PreparedState(4);
        simulation = new CitizenSimulation(state);
        citizen = simulation.Residents[0];
        market = AllowedTarget(simulation, citizen.Id, "market");
        True(simulation.TryApplyDecision(Decision(citizen.Id, "market", market, 5f, "짧은 계획", "짧은 기억", "worried"), "openrouter/test", .01f, out _), "짧은 TTL 계획 적용");
        simulation.Advance(.02f);
        True(!citizen.HasActivePlan && citizen.LastDecisionSource == "routine" && citizen.CurrentIndex != market, "만료 계획은 목적지 이동 권한을 잃고 기존 이동으로 복귀");

        state = PreparedState(4);
        simulation = new CitizenSimulation(state);
        citizen = simulation.Residents[0];
        market = AllowedTarget(simulation, citizen.Id, "market");
        True(simulation.TryApplyDecision(Decision(citizen.Id, "market", market, 5f, "세션 생각", "세션 기억", "happy"), "openrouter/test", 60f, out _), "초기화 전 계획 적용");
        simulation.ClearLlmPlans();
        True(!citizen.HasActivePlan && !citizen.HasPendingPlan && citizen.LastDecisionSource == "routine" && citizen.LastThought == "" && citizen.RecentMemory == "" && citizen.LastSourceModel == "" && citizen.LastMood == "calm",
            "비활성화·세션 재시작 시 모델 계획과 세션 진단 초기화");
    }

    static void ResidentsBeyondVisualBudgetStillAdvance()
    {
        GameState state = PreparedState(CitizenSimulation.StreetCapacity + 12);
        var simulation = new CitizenSimulation(state);
        Citizen citizen = simulation.Residents[CitizenSimulation.StreetCapacity];
        CitizenDecisionBatch one = simulation.BuildDecisionSnapshot(new[] { citizen.Id });
        int market = one.residents[0].allowed.Single(a => a.intent == "market").target;
        float x = citizen.X, z = citizen.Z;
        True(simulation.TryApplyDecision(Decision(citizen.Id, "market", market, 5f, "장터 산책", "장터를 향함", "curious"), "openrouter/test", 60f, out _), "시각 예산 밖 주민 결정 적용");
        simulation.Advance(.5f);
        True(!citizen.Indoors && (citizen.X != x || citizen.Z != z), "129번째 주민도 시뮬레이션에서 실제 이동");
    }

    static void GuideUsesExistingResidentWithoutSimulationEffects()
    {
        GameState empty = PreparedState(0);
        var noResidents = new CitizenSimulation(empty);
        True(!noResidents.EnsureGuide() && noResidents.GuideCitizen == null && noResidents.ResidentCount == 0, "인구가 없으면 가이드 주민을 새로 만들지 않음");

        GameState state = PreparedState(5);
        var simulation = new CitizenSimulation(state);
        Citizen original = simulation.Residents[0];
        float coins = state.Coins;
        float[] stock = state.Stock.ToArray();
        int population = state.Population;
        string topology = CellSignature(state);
        True(simulation.EnsureGuide() && ReferenceEquals(original, simulation.GuideCitizen), "기존 ID 1 주민을 가이드로 지정");
        True(simulation.GuideCitizen.Id == 1 && simulation.GuideCitizen.Name == "단우" && simulation.GuideCitizen.Persona == "친절한 서기" && simulation.GuideCitizen.IsGuide,
            "단우 이름·서기 페르소나·가이드 역할 지정");
        True(simulation.ResidentCount == population && state.Population == population && state.Coins == coins && state.Stock.SequenceEqual(stock) && CellSignature(state) == topology,
            "가이드 지정은 인구·비용·자원·도시 상태 무변이");

        True(simulation.SetGuideControl(true) && simulation.GuideControlled, "가이드 전용 제어 시작");
        CitizenDecisionBatch batch = simulation.BuildDecisionSnapshot(simulation.Residents.Select(r => r.Id));
        True(batch.residents.All(f => f.id != "1") && batch.residents.Any(), "제어 중 단우만 모델 결정 배치에서 제외");
        int market = AllowedTargetForOther(simulation, "market");
        string before = CitizenSignature(simulation.GuideCitizen);
        True(!simulation.TryApplyDecision(Decision(1, "market", market, 5f, "모델 지시", "모델 기억", "happy"), "openrouter/test", 60f, out _)
            && CitizenSignature(simulation.GuideCitizen) == before, "제어 중 단우 모델 응답을 원자적으로 거부");
    }

    static void GuideWalksQueuesAndReleasesSafely()
    {
        GameState state = PreparedState(5);
        var simulation = new CitizenSimulation(state);
        simulation.EnsureGuide();
        simulation.SetGuideControl(true);
        Citizen guide = simulation.GuideCitizen;
        float coins = state.Coins;
        float[] stock = state.Stock.ToArray();
        int population = state.Population;
        float startX = guide.X, startZ = guide.Z;
        True(simulation.GuideToNear(10, 10) && simulation.GuideWalking && guide.Path.Count >= 2 && simulation.IsValidPath(guide.Path), "시청 옆 기존 도로로 가이드 경로 시작");
        simulation.Advance(1f);
        True(guide.X == startX && guide.Z == startZ, "일반 게임 진행은 제어 중 가이드를 움직이지 않음");
        simulation.AdvanceGuide(.5f);
        True(guide.X != startX || guide.Z != startZ, "unscaled 가이드 진행만 정상 보행 속도로 이동");
        AdvanceGuideUntil(simulation, () => !simulation.GuideWalking, 200);
        int townRoad = guide.CurrentIndex;
        True(!guide.Indoors && guide.Path.Count == 0 && state.Cells[townRoad].Building == BuildingKind.Road && Adjacent(state, townRoad, 10, 10),
            "도착한 단우는 설명 부지 대신 인접 도로에 서서 표시됨");

        True(simulation.GuideToNear(7, 10), "첫 가이드 이동 시작");
        int firstDestination = guide.DestinationIndex;
        simulation.AdvanceGuide(.2f);
        float queuedX = guide.X, queuedZ = guide.Z;
        True(simulation.GuideToNear(10, 7), "보행 중 다음 설명 지점 예약");
        int unchangedDestination = guide.DestinationIndex;
        True(unchangedDestination == firstDestination && guide.X == queuedX && guide.Z == queuedZ, "예약 시 현재 경로와 좌표를 바꾸지 않음");
        AdvanceGuideUntil(simulation, () => guide.CurrentIndex == firstDestination, 300);
        True(simulation.GuideWalking && guide.Path[0] == firstDestination && guide.DestinationIndex != firstDestination,
            "첫 도착 뒤 실제 도착 도로에서 예약 목표로 재계획");
        AdvanceGuideUntil(simulation, () => !simulation.GuideWalking, 300);
        True(state.Coins == coins && state.Stock.SequenceEqual(stock) && state.Population == population, "가이드 보행은 경제·자원·인구를 변경하지 않음");

        float standingX = guide.X, standingZ = guide.Z;
        Place(state, 9, 7, BuildingKind.House);
        simulation.Synchronize();
        True(simulation.GuideControlled && !guide.Indoors && guide.X == standingX && guide.Z == standingZ, "인접 건설 위상 변경에도 제어 중 가이드 위치 보존");
        True(!simulation.GuideToNear(0, 0) && guide.X == standingX && guide.Z == standingZ, "인접 도로가 없는 목표는 이동 없이 거부");

        True(simulation.SetGuideControl(false) && !simulation.GuideControlled && guide.Name == "단우" && guide.IsGuide, "제어 해제 후 단우 정체성 유지");
        CitizenDecisionBatch released = simulation.BuildDecisionSnapshot(new[] { guide.Id });
        True(released.residents.Count == 1 && released.residents[0].id == "1", "제어 해제 후 단우가 일반 모델 결정 대상에 복귀");
        CitizenAllowedDecision releasedChoice = released.residents[0].allowed[0];
        True(simulation.TryApplyDecision(Decision(guide.Id, releasedChoice.intent, releasedChoice.target, 5f, "안내 뒤 자유 시간", "안내를 마침", "calm"), "openrouter/test", 60f, out _),
            "제어 해제 후 단우 모델 결정 적용 허용");

        GameState replacement = PreparedState(3);
        simulation.Reset(replacement);
        True(simulation.GuideCitizen == null && !simulation.GuideControlled && !simulation.Residents.Any(r => r.IsGuide), "새 상태 로드 시 가이드 세션 제어 초기화");
    }

    static GameState PreparedState(int population)
    {
        GameState state = GameState.CreateNew();
        state.Population = population;
        Place(state, 13, 10, BuildingKind.Market);
        Place(state, 7, 10, BuildingKind.Park);
        return state;
    }

    static ResponseDecision Decision(int id, string intent, int target, float dwell, string thought, string memory, string mood)
    {
        return new ResponseDecision { id = id.ToString(), intent = intent, target = target, dwellSeconds = dwell, thought = thought, memory = memory, mood = mood };
    }

    static int AllowedTarget(CitizenSimulation simulation, int id, string intent)
    {
        CitizenDecisionFacts facts = simulation.BuildDecisionSnapshot(new[] { id }).residents.Single();
        return facts.allowed.First(a => a.intent == intent).target;
    }

    static int AllowedTargetForOther(CitizenSimulation simulation, string intent)
    {
        CitizenDecisionBatch batch = simulation.BuildDecisionSnapshot(simulation.Residents.Select(r => r.Id));
        return batch.residents.SelectMany(f => f.allowed).First(a => a.intent == intent).target;
    }

    static void AdvanceUntil(CitizenSimulation simulation, Func<bool> condition, int maximumSteps)
    {
        for (int i = 0; i < maximumSteps && !condition(); i++) simulation.Advance(.1f);
        True(condition(), "제한 시간 안에 목적지 도착");
    }

    static void AdvanceGuideUntil(CitizenSimulation simulation, Func<bool> condition, int maximumSteps)
    {
        for (int i = 0; i < maximumSteps && !condition(); i++) simulation.AdvanceGuide(.1f);
        True(condition(), "제한 시간 안에 가이드 목적지 도착");
    }

    static string CitizenSignature(Citizen citizen)
    {
        return string.Join("|", citizen.Id, citizen.Activity, citizen.Indoors, citizen.X, citizen.Z, citizen.CurrentIndex, citizen.DestinationIndex,
            citizen.LastDecisionSource, citizen.LastThought, citizen.LastMood, citizen.RecentMemory, citizen.LastSourceModel,
            citizen.HasActivePlan, citizen.ActiveIntent, citizen.ActiveTargetIndex, citizen.HasPendingPlan, citizen.PendingIntent, citizen.PendingTargetIndex,
            string.Join(",", citizen.Path));
    }

    static string CellSignature(GameState state) => string.Join(";", state.Cells.Select(c => $"{(int)c.Terrain},{(int)c.Building},{c.Level},{c.Connected},{c.Progress}"));
    static bool Adjacent(GameState state, int index, int x, int z) => Math.Abs(index % state.Size - x) + Math.Abs(index / state.Size - z) == 1;
    static bool IsOwned(GameState state, int index) => state.OwnedRegions.Contains(((index / state.Size) / 7) * 3 + (index % state.Size) / 7);
    static bool MatchesTarget(GameState state, Citizen citizen, CitizenAllowedDecision allowed)
    {
        BuildingKind kind = state.Cells[allowed.target].Building;
        if (allowed.intent == "home") return allowed.target == citizen.HomeIndex && kind == BuildingKind.House;
        if (allowed.intent == "work") return allowed.target == citizen.JobIndex && kind != BuildingKind.None && kind != BuildingKind.Road && kind != BuildingKind.House && kind != BuildingKind.TownHall && kind != BuildingKind.Park && kind != BuildingKind.Market;
        if (allowed.intent == "market") return kind == BuildingKind.Market;
        if (allowed.intent == "park") return kind == BuildingKind.Park;
        return allowed.intent == "square" && kind == BuildingKind.TownHall;
    }
    static void Place(GameState state, int x, int z, BuildingKind kind)
    {
        Cell cell = state.Cells[z * state.Size + x];
        cell.Building = kind;
        cell.Level = 1;
        cell.Terrain = TerrainKind.Grass;
    }
}

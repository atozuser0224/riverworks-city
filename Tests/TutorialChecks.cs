using Riverworks;

static class TutorialChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        StableCatalogAndFreshBaselines();
        RealActionsReachEveryGate();
        SkipResumeAndReplayAreExplicit();
        LegacyNullAndInvalidProgressAreSafe();
        AdviceIsLocalAndResponsive();
        GuidanceCatalogMatchesCurrentFeatures();
        GuidanceUnlocksAndSeenBitsAreGuarded();
        return passed;
    }

    static void StableCatalogAndFreshBaselines()
    {
        GameState state = GameState.CreateNew();
        var simulation = new Simulation(state);
        TutorialProgress progress = state.Tutorial;

        True(progress != null && progress.Enabled && progress.Step == TutorialStepId.Welcome,
            "새 도시는 활성 튜토리얼로 시작");
        True(progress.GuidanceEnabled && !progress.TownHallIntroPlayed && progress.GuidanceSeenMask == 0,
            "새 도시만 후속 안내 활성화");
        Equal(6, progress.RoadBaseline, "초기 도로 기준선");
        Equal(2, progress.HouseBaseline, "초기 주택 기준선");
        Equal(0, progress.LumberyardBaseline, "초기 벌목장 기준선");
        Equal(0, progress.StudyHouseBaseline, "초기 서재 기준선");
        Equal(11, TutorialCatalog.All.Count(), "안정적인 11단계 카탈로그");
        Equal(11, TutorialCatalog.All.Select(step => step.StableId).Distinct().Count(), "단계 문자열 ID 중복 없음");
        True(TutorialCatalog.All.All(step => (int)step.Id >= 0 && (int)step.Id <= 10), "단계 enum 저장 범위 고정");

        var acknowledged = Facts(acknowledged: true);
        True(TutorialCatalog.TryAdvance(state, acknowledged), "환영 인사는 확인 후 진행");
        True(state.Tutorial.Step == TutorialStepId.SelectTownHall, "시청 선택 단계 진입");
        True(!TutorialCatalog.TryAdvance(state, acknowledged), "시청을 고르지 않고 수동 진행 불가");

        var selected = Facts(acknowledged: true);
        selected.HasSelectedCell = true;
        selected.SelectedX = 10;
        selected.SelectedZ = 10;
        selected.SelectedBuilding = BuildingKind.TownHall;
        True(TutorialCatalog.TryAdvance(state, selected), "실제 시청 선택으로 진행");
        True(state.Tutorial.Step == TutorialStepId.ExtendRoad, "도로 단계 진입");
        True(!TutorialCatalog.EvaluateGoal(state, Facts(true)), "기존 연결 도로는 새 도로 목표를 즉시 통과하지 않음");
        True(simulation.GetCell(10, 7).Terrain == TerrainKind.Grass && simulation.CanBuild(BuildingKind.Road, 10, 7, out _),
            "추천 도로 좌표가 실제 건설 가능");
        True(simulation.GetCell(9, 7).Terrain == TerrainKind.Grass, "추천 주택 좌표가 평지");
        True(simulation.GetCell(7, 10).Terrain == TerrainKind.Forest, "추천 벌목장 좌표가 숲");
        True(simulation.GetCell(11, 8).Terrain == TerrainKind.Grass, "추천 서재 좌표가 평지");
    }

    static void RealActionsReachEveryGate()
    {
        GameState state = GameState.CreateNew();
        var simulation = new Simulation(state);
        AdvanceWelcomeAndTownHall(state);

        float coinsBeforeGate = state.Coins;
        List<float> stockBeforeGate = state.Stock.ToList();
        True(!TutorialCatalog.TryAdvance(state, Facts(true)), "행동 전 도로 단계 진행 거부");
        Equal(coinsBeforeGate, state.Coins, "거부된 진행은 코인을 바꾸지 않음");
        True(stockBeforeGate.SequenceEqual(state.Stock), "거부된 진행은 재고를 바꾸지 않음");

        True(simulation.Build(BuildingKind.Road, 13, 13, out _), "소유지의 떨어진 도로도 실제 건설 가능");
        True(!simulation.GetCell(13, 13).Connected && !TutorialCatalog.EvaluateGoal(state, Facts()),
            "새 도로라도 시청 도로망과 끊겨 있으면 목표 미달");
        True(simulation.Build(BuildingKind.Road, 10, 7, out _), "추천 위치에 실제 연결 도로 건설");
        True(TutorialCatalog.EvaluateGoal(state, Facts()), "새 연결 도로 목표 감지");
        True(!TutorialCatalog.TryAdvance(state, Facts()), "대사가 확인되지 않으면 완성 목표도 대기");
        True(TutorialCatalog.TryAdvance(state, Facts(true)), "도로 목표와 대사 확인 뒤 진행");

        True(simulation.Build(BuildingKind.House, 9, 7, out _), "추천 위치에 실제 연결 주택 건설");
        True(simulation.GetCell(9, 7).Connected && TutorialCatalog.TryAdvance(state, Facts(true)), "연결 주택 목표 통과");

        True(simulation.Build(BuildingKind.Lumberyard, 7, 10, out _), "숲 위에 실제 벌목장 건설");
        True(simulation.GetCell(7, 10).Connected && TutorialCatalog.TryAdvance(state, Facts(true)), "연결 벌목장 목표 통과");

        True(simulation.Build(BuildingKind.StudyHouse, 11, 8, out _), "추천 위치에 실제 서재 건설");
        True(simulation.GetCell(11, 8).Connected && TutorialCatalog.TryAdvance(state, Facts(true)), "연결 서재 목표 통과");

        True(!TutorialCatalog.TryAdvance(state, Facts(true)), "윤작 시작 전 연구 단계 진행 거부");
        True(simulation.StartResearch(TechId.CropRotation, out _), "실제 윤작 연구 시작");
        True(TutorialCatalog.TryAdvance(state, Facts(true)), "진행 중인 윤작을 시작 목표로 인정");
        True(state.Tutorial.Step == TutorialStepId.CompleteCropRotation, "윤작 완료 단계 진입");
        True(!TutorialCatalog.TryAdvance(state, Facts(true)), "실제 연구 완료 전 진행 거부");

        int duration = TechCatalog.Get(TechId.CropRotation).DurationDays;
        for (int day = 0; day < duration; day++) simulation.Tick();
        True(TechCatalog.Has(state, TechId.CropRotation), "실제 도시 틱으로 윤작 완료");
        True(TutorialCatalog.TryAdvance(state, Facts(true)), "완료된 윤작 목표 통과");
        True(TutorialCatalog.TryAdvance(state, Facts(true)), "기계 동력 안내 확인");
        True(state.Tutorial.Step == TutorialStepId.SaveCity, "저장 단계 진입");

        var failedSave = Facts(true);
        failedSave.SaveSucceeded = false;
        failedSave.SuccessfulSaveVersion = state.Version;
        True(!TutorialCatalog.TryAdvance(state, failedSave), "실패한 저장은 목표를 통과하지 않음");
        var wrongVersion = Facts(true);
        wrongVersion.SaveSucceeded = true;
        wrongVersion.SuccessfulSaveVersion = state.Version - 1;
        True(!TutorialCatalog.TryAdvance(state, wrongVersion), "다른 저장 버전 성공은 목표를 통과하지 않음");
        var saved = Facts(true);
        saved.SaveSucceeded = true;
        saved.SuccessfulSaveVersion = state.Version;
        True(TutorialCatalog.TryAdvance(state, saved), "현재 v4 도시의 실제 저장 성공 감지");
        True(state.Tutorial.Step == TutorialStepId.Finish && !state.Tutorial.Completed, "마지막 인사 전 완료 저장 안 함");
        True(!TutorialCatalog.TryAdvance(state, Facts()), "마지막 대사 확인 전 완료 불가");
        True(TutorialCatalog.TryAdvance(state, Facts(true)), "마지막 인사 확인");
        True(state.Tutorial.Completed && !state.Tutorial.Enabled && !state.Tutorial.Skipped, "튜토리얼 완료 상태 일관성");
        True(TutorialCatalog.TryValidate(state, out _), "완료 상태 저장 검증 통과");
    }

    static void SkipResumeAndReplayAreExplicit()
    {
        GameState state = GameState.CreateNew();
        TutorialProgress progress = state.Tutorial;
        string economy = EconomySignature(state);

        progress.Skip();
        True(progress.Skipped && !progress.Enabled && progress.Step == TutorialStepId.Welcome, "건너뛰기는 현재 단계 보존");
        True(!TutorialCatalog.TryAdvance(state, Facts(true)), "건너뛴 상태는 자동 진행하지 않음");
        True(economy == EconomySignature(state), "건너뛰기는 도시 경제를 바꾸지 않음");
        progress.Resume();
        True(progress.Enabled && !progress.Skipped && progress.Step == TutorialStepId.Welcome, "명시적 이어하기가 같은 단계 복원");
        progress.SetCollapsed(true);
        True(progress.Collapsed, "접힘 상태 명시 저장");

        var simulation = new Simulation(state);
        simulation.Build(BuildingKind.Road, 13, 13, out _);
        True(!simulation.GetCell(13, 13).Connected, "재시작 전 끊긴 도로 확인");
        string beforeReplay = EconomySignature(state);
        TutorialProgress replay = TutorialProgress.Replay(state);
        Equal(6, replay.RoadBaseline, "기존 도시 재시작은 현재 연결 도로만 새 기준선으로 사용");
        True(replay.Step == TutorialStepId.Welcome && replay.Enabled, "재시작은 첫 단계의 새 진행 상태 생성");
        True(ReferenceEquals(progress, state.Tutorial), "Replay는 호출자가 선택하기 전 저장 상태를 덮어쓰지 않음");
        True(beforeReplay == EconomySignature(state), "재시작 준비는 도시 경제를 바꾸지 않음");
    }

    static void LegacyNullAndInvalidProgressAreSafe()
    {
        True(new GameState().Tutorial == null, "GameState 기본 튜토리얼 필드는 null");
        GameState legacy = GameState.CreateNew();
        legacy.Tutorial = null;
        string before = EconomySignature(legacy);
        True(TutorialCatalog.TryValidate(legacy, out _), "튜토리얼 필드 없는 v1-v4 저장 허용");
        True(TutorialCatalog.Current(legacy) == null && !TutorialCatalog.TryAdvance(legacy, Facts(true)), "legacy null은 자동 실행하지 않음");
        True(before == EconomySignature(legacy), "legacy null 검증과 조회는 도시를 바꾸지 않음");

        GameState invalid = GameState.CreateNew();
        string economy = EconomySignature(invalid);
        invalid.Tutorial.Step = (TutorialStepId)999;
        True(!TutorialCatalog.TryValidate(invalid, out _), "알 수 없는 튜토리얼 enum 거부");
        True(economy == EconomySignature(invalid), "enum 검증 실패는 경제를 바꾸지 않음");
        invalid.Tutorial.Step = TutorialStepId.Welcome;
        invalid.Tutorial.RoadBaseline = -1;
        True(!TutorialCatalog.TryValidate(invalid, out _), "음수 기준선 거부");
        True(economy == EconomySignature(invalid), "음수 검증 실패는 경제를 바꾸지 않음");
        invalid.Tutorial.RoadBaseline = invalid.Cells.Count + 1;
        True(!TutorialCatalog.TryValidate(invalid, out _), "도시 범위 밖 기준선 거부");
        True(economy == EconomySignature(invalid), "범위 검증 실패는 경제를 바꾸지 않음");
        invalid.Tutorial.RoadBaseline = 6;
        invalid.Tutorial.Enabled = true;
        invalid.Tutorial.Skipped = true;
        True(!TutorialCatalog.TryValidate(invalid, out _), "활성과 건너뜀의 모순 거부");
        True(economy == EconomySignature(invalid), "상태 검증 실패는 경제를 바꾸지 않음");
        invalid.Tutorial.Enabled = true;
        invalid.Tutorial.Skipped = false;
        invalid.Tutorial.GuidanceSeenMask = GuidanceLessonCatalog.KnownSeenMask | (1 << GuidanceLessonCatalog.Count);
        True(!TutorialCatalog.TryValidate(invalid, out _), "알 수 없는 기능 안내 비트 거부");
        True(economy == EconomySignature(invalid), "안내 비트 검증 실패는 경제를 바꾸지 않음");

        var oldTutorialShape = new TutorialProgress();
        True(!oldTutorialShape.GuidanceEnabled && !oldTutorialShape.TownHallIntroPlayed && oldTutorialShape.GuidanceSeenMask == 0,
            "이전 튜토리얼 저장의 누락 필드는 자동 안내를 켜지 않음");
    }

    static void AdviceIsLocalAndResponsive()
    {
        GameState state = GameState.CreateNew();
        foreach (AdvisorTopic topic in Enum.GetValues<AdvisorTopic>())
        {
            string advice = TutorialAdvisor.Get(topic, state);
            True(!string.IsNullOrWhiteSpace(advice) && advice.Contains("\n\n"), topic + " 조언은 읽기 쉬운 두 문단");
        }
        string before = TutorialAdvisor.Get(AdvisorTopic.Research, state);
        state.Technologies.Add(TechId.CropRotation);
        string after = TutorialAdvisor.Get(AdvisorTopic.Research, state);
        True(before != after && after.Contains("기계 동력"), "연구 상태에 맞춘 결정적 조언");
        Equal("단우", TutorialAdvisor.Name, "친근한 마을 서기 이름");
    }

    static void GuidanceCatalogMatchesCurrentFeatures()
    {
        GuidanceLessonId[] expected =
        {
            GuidanceLessonId.BuildingUpgrades,
            GuidanceLessonId.WindmillPower,
            GuidanceLessonId.FoodProductionChain,
            GuidanceLessonId.HousingAndHappiness,
            GuidanceLessonId.TerritoryExpansion,
            GuidanceLessonId.MarketAndTrade,
            GuidanceLessonId.Warehouse,
            GuidanceLessonId.MiningAndMetal,
            GuidanceLessonId.WorkshopAndTools,
            GuidanceLessonId.SteamAndFuel,
            GuidanceLessonId.SharedMicrogridFactory,
            GuidanceLessonId.PowerInletAndPoles,
            GuidanceLessonId.BeltsAndInserters,
            GuidanceLessonId.RecipesAndBuffers,
            GuidanceLessonId.SplitterFiltersAndBackpressure,
            GuidanceLessonId.PhysicalCityImportsExports,
            GuidanceLessonId.OptionalResearch,
            GuidanceLessonId.ResidentLlmSettings,
            GuidanceLessonId.SaveBackupAndReducedEffects
        };
        GuidanceLessonDefinition[] lessons = GuidanceLessonCatalog.All.ToArray();
        Equal(19, GuidanceLessonCatalog.Count, "현재 기능 안내 수 19개 고정");
        Equal(GuidanceLessonCatalog.Count, lessons.Length, "안내 상수와 카탈로그 수 일치");
        True(lessons.Select(lesson => lesson.Id).SequenceEqual(expected), "기능 안내 ID와 순서 고정");
        Equal(19, lessons.Select(lesson => lesson.StableId).Distinct().Count(), "기능 안내 문자열 ID 중복 없음");
        True(lessons.All(lesson => lesson.Pages != null && lesson.Pages.Length >= 2 && lesson.Pages.Length <= 3 &&
                                   lesson.Pages.All(page => !string.IsNullOrWhiteSpace(page) && page.Length <= 140)),
            "모든 기능 안내가 2~3개의 짧은 페이지 제공");
        True(lessons.All(lesson => !string.IsNullOrWhiteSpace(lesson.Title) && !string.IsNullOrWhiteSpace(lesson.Goal)),
            "모든 기능 안내에 제목과 읽기 목표 제공");
        Equal((1 << 19) - 1, GuidanceLessonCatalog.KnownSeenMask, "19비트 안내 마스크 고정");
        True(GuidanceLessonCatalog.Get((GuidanceLessonId)999) == null, "알 수 없는 기능 안내 ID 안전 조회");

        string residentPages = string.Join(" ", GuidanceLessonCatalog.Get(GuidanceLessonId.ResidentLlmSettings).Pages);
        True(residentPages.Contains("게이트웨이") && residentPages.Contains("준비가 끝난 것은 아니에요") &&
             residentPages.Contains("로컬 규칙"), "주민 LLM 안내가 연결 요건과 미준비 상태를 정확히 설명");
    }

    static void GuidanceUnlocksAndSeenBitsAreGuarded()
    {
        GameState state = GameState.CreateNew();
        TutorialProgress progress = state.Tutorial;
        string economy = EconomySignature(state);

        True(GuidanceLessonCatalog.GetNextUnseenUnlocked(state) == null, "기본 튜토리얼 완료 전 자동 기능 안내 없음");
        True(progress.MarkTownHallIntroPlayed() && !progress.MarkTownHallIntroPlayed(), "시청 오프닝 기록은 한 번만 전환");
        True(economy == EconomySignature(state), "시청 오프닝 기록은 도시 경제를 바꾸지 않음");

        progress.Step = TutorialStepId.Finish;
        progress.Enabled = false;
        progress.Completed = true;
        state.Technologies.Add(TechId.CropRotation);
        GuidanceLessonDefinition next = GuidanceLessonCatalog.GetNextUnseenUnlocked(state);
        True(next != null && next.Id == GuidanceLessonId.OptionalResearch,
            "기본 완료 뒤 실제 윤작 상태로 선택 연구 안내 해금");
        True(GuidanceLessonCatalog.MarkSeen(state, next.Id), "완전히 읽은 기능 안내 기록");
        True(!GuidanceLessonCatalog.MarkSeen(state, next.Id), "같은 기능 안내 중복 기록 거부");
        True(GuidanceLessonCatalog.IsSeen(state, next.Id), "안내 읽음 비트 조회");
        next = GuidanceLessonCatalog.GetNextUnseenUnlocked(state);
        True(next != null && next.Id == GuidanceLessonId.SaveBackupAndReducedEffects,
            "읽은 안내 다음의 해금된 기능을 한 번에 하나 반환");

        progress.SetGuidanceEnabled(false);
        True(GuidanceLessonCatalog.GetNextUnseenUnlocked(state) == null, "자동 기능 안내 명시적 비활성화");
        True(GuidanceLessonCatalog.Get(GuidanceLessonId.BuildingUpgrades) != null,
            "자동 안내를 꺼도 수동 카탈로그 전체 조회 가능");
        True(GuidanceLessonCatalog.MarkSeen(state, GuidanceLessonId.BuildingUpgrades),
            "수동으로 끝까지 읽은 잠긴 안내도 기록 가능");
        progress.SetGuidanceEnabled(true);

        True(!GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.WindmillPower), "기계 동력 전 풍차 안내 잠김");
        state.Technologies.Add(TechId.MechanicalPower);
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.WindmillPower), "기계 동력으로 풍차 안내 해금");
        True(!GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.FoodProductionChain), "인구 10 전 식량 가공 안내 잠김");
        state.Population = 10;
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.FoodProductionChain), "기계 동력과 인구 10으로 식량 가공 안내 해금");
        state.Population = 12;
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.HousingAndHappiness) &&
             GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.TerritoryExpansion),
            "인구 12로 주택 행복과 영토 안내 해금");
        state.Population = 18;
        state.Technologies.Add(TechId.Guilds);
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.Warehouse), "길드와 인구 18로 창고 안내 해금");
        True(!GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.MarketAndTrade), "인구 22 전 시장 안내 잠김");
        state.Population = 22;
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.MarketAndTrade), "길드와 인구 22로 시장 안내 해금");
        state.Population = 25;
        state.Technologies.Add(TechId.Metallurgy);
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.MiningAndMetal), "금속학과 인구 25로 금속 흐름 안내 해금");
        state.Population = 30;
        state.Technologies.Add(TechId.Toolmaking);
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.WorkshopAndTools), "도구 제작과 인구 30으로 공방 안내 해금");
        state.Technologies.Add(TechId.SteamPower);
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.SteamAndFuel) &&
             GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.SharedMicrogridFactory) &&
             GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.PowerInletAndPoles),
            "증기력과 성장 상태로 증기·미세 격자·설비 전력 안내 해금");
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.BeltsAndInserters) &&
             GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.RecipesAndBuffers) &&
             GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.SplitterFiltersAndBackpressure),
            "증기력으로 고급 설비 흐름 안내 해금");
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.PhysicalCityImportsExports),
            "길드로 실제 도시 반입·반출 안내 해금");
        True(GuidanceLessonCatalog.IsUnlocked(state, GuidanceLessonId.ResidentLlmSettings),
            "인구 성장으로 주민 AI 연결 설정 안내 해금");
        True(economy.Split('|')[2] != EconomySignature(state).Split('|')[2], "검사용 실제 기술 상태가 변경됨");

        int seenBeforeInvalid = progress.GuidanceSeenMask;
        True(!GuidanceLessonCatalog.MarkSeen(state, (GuidanceLessonId)999) &&
             progress.GuidanceSeenMask == seenBeforeInvalid, "알 수 없는 ID는 읽음 마스크를 바꾸지 않음");
        progress.GuidanceSeenMask = GuidanceLessonCatalog.KnownSeenMask | (1 << GuidanceLessonCatalog.Count);
        True(!GuidanceLessonCatalog.MarkSeen(state, GuidanceLessonId.Warehouse), "손상된 기존 마스크에는 새 비트를 기록하지 않음");
        True(GuidanceLessonCatalog.GetNextUnseenUnlocked(state) == null, "손상된 마스크로 자동 안내하지 않음");

        GameState legacy = GameState.CreateNew();
        legacy.Tutorial = null;
        True(GuidanceLessonCatalog.GetNextUnseenUnlocked(legacy) == null &&
             !GuidanceLessonCatalog.MarkSeen(legacy, GuidanceLessonId.SaveBackupAndReducedEffects),
            "튜토리얼 없는 legacy 도시는 조용히 유지");
    }

    static void AdvanceWelcomeAndTownHall(GameState state)
    {
        True(TutorialCatalog.TryAdvance(state, Facts(true)), "환영 단계 진행");
        var selected = Facts(true);
        selected.HasSelectedCell = true;
        selected.SelectedX = 10;
        selected.SelectedZ = 10;
        selected.SelectedBuilding = BuildingKind.TownHall;
        True(TutorialCatalog.TryAdvance(state, selected), "시청 선택 단계 진행");
    }

    static TutorialFacts Facts(bool acknowledged = false) => new TutorialFacts { DialogueAcknowledged = acknowledged };

    static string EconomySignature(GameState state) =>
        state.Coins + "|" + string.Join(",", state.Stock) + "|" + string.Join(",", state.Technologies) + "|" +
        string.Join(",", state.Cells.Select(cell => (int)cell.Building + ":" + cell.Level));

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("실패: " + name);
        passed++;
        Console.WriteLine("통과 " + name);
    }

    static void Equal<T>(T expected, T actual, string name) where T : IEquatable<T> =>
        True(expected.Equals(actual), $"{name} (기대 {expected}, 실제 {actual})");
}

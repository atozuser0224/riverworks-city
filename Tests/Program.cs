using Riverworks;

static class Check
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("실패: " + name); passed++; Console.WriteLine("✓ " + name); }
    static void Equal<T>(T expected, T actual, string name) where T : IEquatable<T> => True(expected.Equals(actual), $"{name} (기대 {expected}, 실제 {actual})");
    static Cell Empty(GameState s, int x, int z, TerrainKind terrain = TerrainKind.Grass) { var c = s.Cells[z * s.Size + x]; c.Building = BuildingKind.None; c.Level = 0; c.Terrain = terrain; return c; }

    public static int Main()
    {
        try
        {
            NewGameAndCatalog(); TechnologyProgressionAndMigration(); ExpandedTechnologyPrerequisitesAndBenefits(); RiverIsCoherent(); InvalidMutationsAreAtomic(); CoinMirrorConsistency(); RoadConnectivity(); ProductionChainAndPower(); SteamPowerPreviewRequiresFuel(); HousingStarvationRecovery(); GhostPopulationIsCapped(); ComfortToolsAreOptional(); MarketStatus(); ExpansionRules(); UpgradeAndRefund(); StarterResourcePlots(); IndustrialObjectiveProgressMatchesRouteGates(); MilestonesAndEndlessWin(); NaturalProgressionFromNewGame(); passed += CitizenChecks.Run();
            passed += CitizenDecisionChecks.Run();
            passed += TutorialChecks.Run();
            passed += FactoryChecks.Run();
            passed += BlueprintChecks.Run();
            passed += FactoryPropertyChecks.Run();
            passed += CityLogisticsChecks.Run();
            passed += CityFactoryChecks.Run();
            passed += TechnologyGraphChecks.Run();
            passed += ResearchGraphChecks.Run();
            passed += IndustryCatalogChecks.Run();
            passed += IndustryProductionChecks.Run();
            passed += IndustrySaveChecks.Run();
            passed += IndustryFluidChecks.Run();
            passed += IndustryChainChecks.Run();
            passed += IndustrialScenarioChecks.Run();
            passed += ExpansionCatalogChecks.Run();
            passed += FactoryLayerChecks.Run();
            passed += LayerFluidChecks.Run();
            passed += FactoryAutomationChecks.Run();
            passed += CityProjectChecks.Run();
            passed += ExpansionSaveChecks.Run();
            passed += ExpansionFlowChecks.Run();
            Console.WriteLine($"\n전체 {passed}개 검증 통과"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static void NewGameAndCatalog()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s);
        Equal(441, s.Cells.Count, "21x21 맵 생성"); True(s.OwnedRegions.SequenceEqual(new[] { 4 }), "중앙 지역 소유");
        True(sim.GetCell(10, 10).Building == BuildingKind.TownHall, "시청 배치"); True(sim.Get(Resource.Bread) > 0 && s.Coins >= 1000, "초보자 비상 자원");
        Equal(6, s.Version, "새 게임 저장 버전"); True(s.Era == Era.Medieval, "중세 시작");
        Equal(0, s.Technologies.Count, "기술 없이 시작"); True(!s.Cells.Any(c => c.Building == BuildingKind.Windmill), "시작 풍차 없음");
        Equal(26, TechCatalog.All.Count(), "복합 산업 기술 카탈로그 26개");
        Equal(Enum.GetValues<BuildingKind>().Length, Catalog.All.Count(), "모든 건물 카탈로그 등록"); True(Catalog.ResourceName(Resource.Tools) == "도구", "한국어 자원명");
    }

    static void TechnologyProgressionAndMigration()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s);
        float coins = s.Coins, research = s.ResearchPoints;
        True(!sim.StartResearch(TechId.Guilds, out var why) && why.Contains("선행"), "선행 기술 없는 연구 거부");
        Equal(coins, s.Coins, "실패 연구 코인 불변"); Equal(research, s.ResearchPoints, "실패 연구 점수 불변");
        True(!sim.IsBuildingUnlocked(BuildingKind.Mill, out why) && why.Contains("기계 동력"), "기술 건물 잠금");
        Cell home = s.Cells.First(c => c.Building == BuildingKind.House);
        True(!sim.Upgrade(home.X, home.Z, out why), "석조술 전 증축 잠금");
        float initialGrain = sim.Get(Resource.Grain); s.Stock[(int)Resource.Bread] = 0; sim.Tick();
        True(s.LastFoodSatisfied && s.LastGrainConsumed > 0, "빵 이전 곡물 식사");
        TechSpec crop = TechCatalog.Get(TechId.CropRotation); float beforeResearch = s.ResearchPoints; float beforeCoins = s.Coins;
        True(sim.StartResearch(TechId.CropRotation, out why), "윤작 연구 시작");
        Equal(beforeResearch - crop.ResearchCost, s.ResearchPoints, "연구 시작 시 점수 1회 차감");
        Equal(beforeCoins - crop.CoinCost, s.Coins, "연구 시작 시 코인 1회 차감");
        float afterStartCoins = s.Coins; sim.Tick(); True(s.ActiveResearch == TechId.CropRotation && !TechCatalog.Has(s, TechId.CropRotation), "연구 기간 중 미완료");
        True(Math.Abs(s.Coins - afterStartCoins - sim.LastIncome) < .01f, "연구 비용 중복 차감 없음");
        sim.Tick(); True(TechCatalog.Has(s, TechId.CropRotation) && s.ActiveResearch == TechId.None, "예정 일수 후 연구 완료");

        var legacy = GameState.CreateNew(); legacy.Version = 1; legacy.Day = 37; legacy.Coins = 777; legacy.Population = 6;
        var legacySim = new Simulation(legacy);
        Equal(6, legacy.Version, "v1 저장 v6 마이그레이션"); True(legacy.Era == Era.Industrial, "기존 도시 산업 시대 승계");
        Equal(18, legacy.Technologies.Count, "v1 기존 도시가 원래 18개 기술만 승계"); Equal(37, legacy.Day, "마이그레이션 날짜 보존"); Equal(777f, legacySim.Get(Resource.Coins), "마이그레이션 자원 보존");
    }

    static void ExpandedTechnologyPrerequisitesAndBenefits()
    {
        TechId[] additions =
        {
            TechId.Forestry, TechId.Irrigation, TechId.Masonry, TechId.Logistics, TechId.Education,
            TechId.MetallurgicalEfficiency, TechId.Electrification, TechId.MassProduction, TechId.Automation
        };
        True(additions.All(id => TechCatalog.Get(id) != null), "새 기술 9개 카탈로그 등록");
        True(additions.All(id => TechCatalog.Get(id).Prerequisites.Length > 0), "새 기술마다 선행 기술 지정");
        True(TechCatalog.Get(TechId.Automation).Prerequisites.SequenceEqual(new[] { TechId.Electrification, TechId.MassProduction }), "자동화 복수 선행 기술");

        var blocked = GameState.CreateNew(); var blockedSim = new Simulation(blocked);
        blocked.ResearchPoints = 999; blocked.Coins = 999; blocked.Stock[(int)Resource.Coins] = blocked.Coins;
        float coins = blocked.Coins, research = blocked.ResearchPoints; TechId active = blocked.ActiveResearch; int days = blocked.ResearchDaysRemaining;
        True(!blockedSim.StartResearch(TechId.Automation, out var why) && why.Contains("선행"), "자동화 선행 기술 누락 거부");
        Equal(coins, blocked.Coins, "선행 기술 실패 코인 원자성"); Equal(research, blocked.ResearchPoints, "선행 기술 실패 연구점수 원자성");
        True(blocked.ActiveResearch == active && blocked.ResearchDaysRemaining == days, "선행 기술 실패 연구 상태 원자성");
        blocked.Technologies.Add(TechId.Electrification);
        True(!blockedSim.StartResearch(TechId.Automation, out why) && why.Contains(TechCatalog.Get(TechId.MassProduction).Name), "자동화는 두 번째 선행 기술도 요구");
        Equal(coins, blocked.Coins, "복수 선행 실패 코인 원자성"); Equal(research, blocked.ResearchPoints, "복수 선행 실패 연구점수 원자성");

        float lumberBase = MeasureTownOutput(BuildingKind.Lumberyard, Resource.Timber);
        float lumberTech = MeasureTownOutput(BuildingKind.Lumberyard, Resource.Timber, TechId.CropRotation, TechId.Forestry);
        Near(lumberBase * 1.25f, lumberTech, "산림 경영 벌목장 실제 생산 +25%");

        float farmBase = MeasureTownOutput(BuildingKind.Farm, Resource.Grain, TechId.CropRotation);
        float farmTech = MeasureTownOutput(BuildingKind.Farm, Resource.Grain, TechId.CropRotation, TechId.Irrigation);
        Near(farmBase * 1.2f, farmTech, "관개 농장 실제 생산 +20%");

        float quarryBase = MeasureTownOutput(BuildingKind.Quarry, Resource.Stone, TechId.Stonecraft);
        float quarryTech = MeasureTownOutput(BuildingKind.Quarry, Resource.Stone, TechId.Stonecraft, TechId.Masonry);
        Near(quarryBase * 1.25f, quarryTech, "석공술 채석장 실제 생산 +25%");

        float mineBase = MeasureTownOutput(BuildingKind.Mine, Resource.Ore, TechId.Metallurgy);
        float mineTech = MeasureTownOutput(BuildingKind.Mine, Resource.Ore, TechId.Metallurgy, TechId.MetallurgicalEfficiency);
        Near(mineBase * 1.2f, mineTech, "금속 공정 효율 광산 실제 생산 +20%");
        float smelterBase = MeasureTownOutput(BuildingKind.Smelter, Resource.Steel, TechId.Metallurgy);
        float smelterTech = MeasureTownOutput(BuildingKind.Smelter, Resource.Steel, TechId.Metallurgy, TechId.MetallurgicalEfficiency);
        Near(smelterBase * 1.2f, smelterTech, "금속 공정 효율 제련소 실제 생산 +20%");

        var education = GameState.CreateNew();
        foreach (Cell cell in education.Cells.Where(c => c.Building != BuildingKind.TownHall && c.Building != BuildingKind.Road)) { cell.Building = BuildingKind.None; cell.Level = 0; }
        Cell study = education.Cells[9 * education.Size + 9]; study.Building = BuildingKind.StudyHouse; study.Level = 2;
        var educationSim = new Simulation(education); float beforePerDay = educationSim.ResearchPerDay; float beforePoints = education.ResearchPoints;
        education.Technologies.Add(TechId.Education);
        Near(beforePerDay + 2f, educationSim.ResearchPerDay, "교육 서재 레벨당 일일 연구 +1");
        educationSim.Tick(); Near(beforePoints + beforePerDay + 2f, education.ResearchPoints, "교육 보너스가 실제 연구점수에 반영");
    }

    static float MeasureTownOutput(BuildingKind kind, Resource resource, params TechId[] technologies)
    {
        var state = GameState.CreateNew();
        foreach (Cell cell in state.Cells.Where(c => c.Building != BuildingKind.TownHall && c.Building != BuildingKind.Road)) { cell.Building = BuildingKind.None; cell.Level = 0; }
        state.Technologies.AddRange(technologies);
        state.Stock[(int)Resource.Ore] = 100;
        Cell producer = state.Cells[9 * state.Size + 9]; producer.Building = kind; producer.Level = 1;
        producer.Terrain = kind == BuildingKind.Lumberyard ? TerrainKind.Forest : kind == BuildingKind.Quarry || kind == BuildingKind.Mine ? TerrainKind.Rock : TerrainKind.Grass;
        if (kind == BuildingKind.Mine || kind == BuildingKind.Smelter)
        {
            Cell windmill = state.Cells[9 * state.Size + 11]; windmill.Building = BuildingKind.Windmill; windmill.Level = 1;
        }
        var simulation = new Simulation(state); float before = simulation.Get(resource); simulation.Tick(); return simulation.Get(resource) - before;
    }

    static void Near(float expected, float actual, string name) => True(Math.Abs(expected - actual) < .001f, $"{name} (기대 {expected}, 실제 {actual})");

    static void RiverIsCoherent()
    {
        var s = GameState.CreateNew();
        True(Enumerable.Range(0, s.Size).All(z => s.Cells[z * s.Size + 2].Terrain == TerrainKind.Water), "서쪽 강줄기 연속성");
        True(s.Cells.Count(c => c.Terrain == TerrainKind.Water) >= 35, "두 타일 폭의 강 지형");
        True(!s.Cells.Any(c => c.Terrain == TerrainKind.Water && c.X > 3), "무작위 물웅덩이 제거");
    }

    static void InvalidMutationsAreAtomic()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s); float coins = s.Coins, wood = sim.Get(Resource.Timber);
        True(!sim.Build(BuildingKind.House, 0, 0, out var why) && why.Contains("지역"), "미소유 지역 건설 거부");
        True(!sim.Build(BuildingKind.House, 10, 10, out why), "점유 셀 건설 거부");
        Empty(s, 13, 13, TerrainKind.Water); True(!sim.Build(BuildingKind.House, 13, 13, out why) && why.Contains("물"), "수면 일반 건물 거부");
        Equal(coins, s.Coins, "실패 시 코인 불변"); Equal(wood, sim.Get(Resource.Timber), "실패 시 자재 불변");
        True(!sim.Upgrade(10, 10, out why), "시청 업그레이드 거부"); True(!sim.Demolish(10, 10, out why), "시청 철거 거부");
        True(!sim.CanBuild((BuildingKind)999, 8, 8, out why) && why.Contains("알 수 없는"), "잘못된 건물 enum 안전 거부");
    }

    static void CoinMirrorConsistency()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s);
        void Synced(string action) => Equal(s.Coins, s.Stock[(int)Resource.Coins], action + " 후 코인 미러");
        Synced("생성"); Empty(s, 10, 11); True(sim.Build(BuildingKind.Road, 10, 11, out _), "코인 검증용 건설"); Synced("건설");
        True(!sim.Build(BuildingKind.Road, 10, 11, out _), "코인 검증용 실패 건설"); Synced("실패 건설");
        True(sim.Demolish(10, 11, out _), "코인 검증용 철거"); Synced("철거");
        sim.Tick(); Synced("틱"); sim.Recalculate(); Synced("재계산");
        True(sim.BuyRegion(1, out _), "코인 검증용 지역 매입"); Synced("지역 매입");
    }

    static void RoadConnectivity()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s);
        Empty(s, 13, 12); s.Coins = 9999; s.Stock[(int)Resource.Timber] = 999; s.Stock[(int)Resource.Stone] = 999;
        True(sim.Build(BuildingKind.House, 13, 12, out _), "떨어진 주택 건설"); True(!sim.GetCell(13, 12).Connected, "도로 없는 건물 미연결");
        Empty(s, 13, 10); Empty(s, 13, 11); True(sim.Build(BuildingKind.Road, 13, 10, out _), "도로 1 건설"); True(sim.Build(BuildingKind.Road, 13, 11, out _), "도로 2 건설");
        True(sim.GetCell(13, 12).Connected, "BFS 도로 연결 복구");
        True(sim.Demolish(13, 10, out _), "중간 도로 철거"); True(!sim.GetCell(13, 12).Connected && sim.GetCell(13, 12).Status.Contains("연결"), "도로 단절 진단");
    }

    static void ProductionChainAndPower()
    {
        var s = GameState.CreateNew(); s.Version = 1; foreach (var h in s.Cells.Where(c => c.Building == BuildingKind.House)) h.Level = 3; var sim = new Simulation(s); s.Population = 40; s.Coins = 9999; s.Stock[(int)Resource.Timber] = 999; s.Stock[(int)Resource.Stone] = 999;
        // Connected production buildings beside the seeded horizontal road.
        Empty(s, 8, 9); Empty(s, 12, 9); Empty(s, 8, 11); Empty(s, 9, 11); Empty(s, 11, 11); Empty(s, 12, 11); Empty(s, 10, 7);
        True(sim.Build(BuildingKind.Farm, 8, 9, out _), "농장 건설"); True(sim.Build(BuildingKind.Mill, 12, 9, out _), "제분소 건설"); True(sim.Build(BuildingKind.Bakery, 8, 11, out _), "제과점 건설"); True(sim.Build(BuildingKind.Windmill, 10, 7, out _), "생산 동력 풍차 건설");
        float grain = sim.Get(Resource.Grain), flour = sim.Get(Resource.Flour), bread = sim.Get(Resource.Bread);
        sim.Tick(); True(sim.Get(Resource.Grain) != grain && sim.Get(Resource.Flour) != flour && sim.Get(Resource.Bread) != bread, "한 틱 생산 체인 가동"); True(sim.PowerUsed >= 2 && sim.PowerCapacity >= 8, "동력 용량과 사용량 계산");
        foreach (var farm in s.Cells.Where(c => c.Building == BuildingKind.Farm)) { farm.Building = BuildingKind.None; farm.Level = 0; } s.Stock[(int)Resource.Grain] = 0; sim.Tick(); True(sim.GetCell(12, 9).Status.Contains("원료 부족"), "원료 부족 상태 표시");
        True(s.TotalProduced > 0, "총생산 누적");
    }

    static void SteamPowerPreviewRequiresFuel()
    {
        var state = GameState.CreateNew();
        Cell first = state.Cells[9 * state.Size + 9]; first.Building = BuildingKind.SteamPlant; first.Level = 1;
        Cell second = state.Cells[9 * state.Size + 11]; second.Building = BuildingKind.SteamPlant; second.Level = 1;
        state.Stock[(int)Resource.Timber] = 0;

        var loaded = new Simulation(state);
        Equal(0, loaded.PowerCapacity, "연료 없는 저장 로드에서 증기 동력 미계상");

        state.Stock[(int)Resource.Timber] = .6f;
        loaded.Recalculate();
        Equal(18, loaded.PowerCapacity, "두 증기 동력소가 한 곳분 연료를 중복 사용하지 않음");
        Near(.6f, loaded.Get(Resource.Timber), "동력 미리보기는 연료를 차감하지 않음");

        state.Stock[(int)Resource.Timber] = 0;
        loaded.Recalculate();
        Equal(0, loaded.PowerCapacity, "거래·재계산 뒤 연료 없으면 증기 동력 제거");

        state.Stock[(int)Resource.Timber] = .6f;
        loaded.Tick();
        Equal(18, loaded.PowerCapacity, "도시 틱은 연료가 있는 증기 동력소만 실제 가동");
        Near(0, loaded.Get(Resource.Timber), "도시 틱은 가동한 증기 동력소 연료 실제 차감");
    }

    static void HousingStarvationRecovery()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s); s.Stock[(int)Resource.Bread] = 0; s.Stock[(int)Resource.Grain] = 0; foreach (Cell farm in s.Cells.Where(c => c.Building == BuildingKind.Farm)) farm.Building = BuildingKind.None; int happy = s.Happiness;
        sim.Tick(); True(s.Happiness < happy && s.Cells.Any(c => c.Building == BuildingKind.House && c.Status.Contains("식량 부족")), "식량 부족 시 행복 감소");
        s.Stock[(int)Resource.Bread] = 50; int low = s.Happiness; for (int i = 0; i < 6; i++) sim.Tick();
        True(s.Happiness > low && s.Population > 8, "식량 회복 시 행복과 인구 성장");
    }

    static void GhostPopulationIsCapped()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s); s.Population = 12;
        Cell first = s.Cells.First(c => c.Building == BuildingKind.House); True(sim.Demolish(first.X, first.Z, out _), "주택 철거"); Equal(6, s.Population, "철거 직후 전체 주택 수용량으로 인구 제한");
        Cell last = s.Cells.First(c => c.Building == BuildingKind.House); True(sim.Demolish(last.X, last.Z, out _), "마지막 주택 철거"); Equal(0, s.Population, "주택 없는 유령 인구 제거");
    }

    static void ComfortToolsAreOptional()
    {
        var without = GameState.CreateNew(); foreach (var h in without.Cells.Where(c => c.Building == BuildingKind.House)) h.Level = 3; var sim = new Simulation(without); without.Population = 24; without.Stock[(int)Resource.Bread] = 50; sim.Tick();
        True(without.LastToolsDemand > 0 && without.LastToolsDelivered == 0, "인구 24부터 선택 도구 수요 기록");
        int happinessWithout = without.Happiness; float incomeWithout = sim.LastIncome;
        var supplied = GameState.CreateNew(); foreach (var h in supplied.Cells.Where(c => c.Building == BuildingKind.House)) h.Level = 3; var suppliedSim = new Simulation(supplied); supplied.Population = 24; supplied.Stock[(int)Resource.Bread] = 50; supplied.Stock[(int)Resource.Tools] = 1; suppliedSim.Tick();
        True(supplied.LastToolsDelivered == supplied.LastToolsDemand && supplied.Happiness >= happinessWithout, "도구 공급 행복 보너스");
        True(suppliedSim.LastIncome > incomeWithout, "도구 공급 세수 보너스");
    }

    static void MarketStatus()
    {
        var s = GameState.CreateNew(); s.Version = 1; foreach (var h in s.Cells.Where(c => c.Building == BuildingKind.House)) h.Level = 3; var sim = new Simulation(s); s.Population = 22;
        Empty(s, 8, 11); True(sim.Build(BuildingKind.Market, 8, 11, out _), "시장 건설"); s.Stock[(int)Resource.Bread] = 0; sim.Tick();
        Equal("빵 비상 수입", sim.GetCell(8, 11).Status, "시장 비상 수입 상태");
        s.Stock[(int)Resource.Bread] = 0; s.Coins = 0; s.Stock[(int)Resource.Coins] = 0; sim.Tick(); Equal("빵 비상 수입 자금 부족", sim.GetCell(8, 11).Status, "시장 자금 부족 상태");
        True(Catalog.Get(BuildingKind.Market).Description.Contains("빵"), "시장 카탈로그 빵 명시");
    }

    static void ExpansionRules()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s); s.Coins = 9999;
        True(!sim.BuyRegion(0, out var why) && why.Contains("맞닿은"), "비인접 지역 구매 거부");
        int cost = sim.RegionCost(1); True(sim.BuyRegion(1, out _), "인접 지역 구매"); Equal(9999f - cost, s.Coins, "지역 비용 정확히 차감");
        True(!sim.BuyRegion(1, out _), "중복 지역 구매 거부"); True(sim.BuyRegion(0, out _), "새 인접 지역 연쇄 확장");
    }

    static void UpgradeAndRefund()
    {
        var s = GameState.CreateNew(); s.Version = 1; var sim = new Simulation(s); s.Coins = 9999; s.Stock[(int)Resource.Timber] = 999; s.Stock[(int)Resource.Stone] = 999;
        Cell house = s.Cells.First(c => c.Building == BuildingKind.House); float before = s.Coins;
        True(sim.Upgrade(house.X, house.Z, out _), "레벨 2 업그레이드"); True(sim.Upgrade(house.X, house.Z, out _), "레벨 3 업그레이드"); True(!sim.Upgrade(house.X, house.Z, out _), "레벨 상한 적용");
        float spent = before - s.Coins; True(sim.Demolish(house.X, house.Z, out _), "업그레이드 건물 철거"); True(s.Coins > before - spent, "철거 환급 지급"); Equal(0, house.Level, "철거 레벨 초기화");
        True(s.Coins < before, "업그레이드 후 철거 반복으로 이익 불가");
    }

    static void StarterResourcePlots()
    {
        var s = GameState.CreateNew(); s.Version = 1; var sim = new Simulation(s);
        True(sim.Build(BuildingKind.Lumberyard, 7, 10, out _), "시작 지역 숲 벌목장 건설");
        True(sim.Build(BuildingKind.Quarry, 13, 10, out _), "시작 지역 바위 채석장 건설");
        sim.Tick(); True(sim.Get(Resource.Timber) > 82 && sim.Get(Resource.Stone) > 62, "초기 지역 재생 원료 생산");
        True(sim.GetCell(7, 10).Terrain == TerrainKind.Forest && sim.GetCell(13, 10).Terrain == TerrainKind.Rock, "건설 후 자원 지형 유지");
    }

    static void IndustrialObjectiveProgressMatchesRouteGates()
    {
        var state = GameState.CreateNew();
        foreach (Cell home in state.Cells.Where(c => c.Building == BuildingKind.House)) home.Level = 3;
        state.Milestone = 3; state.Population = 30; state.TotalToolsProduced = 8;
        var simulation = new Simulation(state);
        True(simulation.ObjectiveProgress < 1, "도구 수량만으로 산업 목표 진행률 100% 불가");
        True(simulation.ObjectiveDescription.Contains("공장") && simulation.ObjectiveDescription.Contains("반출"), "산업 목표에 실제 공장 대체 경로 안내");

        state.Factory.Produced[(int)Resource.Ore] = 1;
        state.Factory.Produced[(int)Resource.Steel] = 1;
        state.Factory.Produced[(int)Resource.Tools] = 8;
        simulation.Recalculate();
        True(simulation.ObjectiveProgress < 1, "공장 도구 미반출 상태는 산업 목표 진행률 미완료");

        state.Factory.Exported[(int)Resource.Tools] = 4;
        simulation.Recalculate();
        Near(.5f, simulation.ObjectiveProgress, "공장 도구 반출량이 산업 목표 진행률에 반영");

        state.Factory.Exported[(int)Resource.Tools] = 8;
        simulation.Recalculate();
        Near(1, simulation.ObjectiveProgress, "공장 광석·강철·도구 생산과 반출 완료 시 진행률 100%");
    }

    static void MilestonesAndEndlessWin()
    {
        var s = GameState.CreateNew(); s.Version = 1; var sim = new Simulation(s); s.Coins = 99999; s.Stock[(int)Resource.Timber] = 9999; s.Stock[(int)Resource.Stone] = 9999;
        // Objective conditions can be reached through ordinary state progression; inject late resources to isolate evaluator.
        Empty(s, 8, 9); Empty(s, 12, 9); sim.Build(BuildingKind.House, 8, 9, out _); sim.Build(BuildingKind.StudyHouse, 12, 9, out _); sim.Tick(); True(s.Milestone >= 1, "첫 연결 목표 달성");
        s.Population = 16; s.Stock[(int)Resource.Bread] = 10; sim.Tick(); True(s.Milestone >= 2, "식량/인구 목표 달성");
        sim.BuyRegion(1, out _); sim.Tick(); True(s.Milestone >= 3, "확장 목표 달성");
        foreach (var home in s.Cells.Where(c => c.Building == BuildingKind.House).ToList()) { sim.Upgrade(home.X, home.Z, out _); sim.Upgrade(home.X, home.Z, out _); }
        s.Population = 30; s.Stock[(int)Resource.Tools] = 8; sim.Tick(); Equal(3, s.Milestone, "수입 도구는 산업 목표 생산량으로 계산하지 않음");
        Empty(s, 12, 11, TerrainKind.Grass); Empty(s, 9, 11); Empty(s, 11, 11);
        s.Stock[(int)Resource.Ore] = 50; s.Stock[(int)Resource.Steel] = 50;
        True(sim.Build(BuildingKind.Mine, 12, 11, out _), "산업 목표 광산 연결"); True(sim.Build(BuildingKind.Smelter, 9, 11, out _), "산업 목표 제련소 연결"); True(sim.Build(BuildingKind.Workshop, 11, 11, out _), "산업 목표 공방 연결");
        s.TotalToolsProduced = 8; Cell wind = sim.GetCell(11, 8); wind.Building = BuildingKind.None; wind.Level = 0; sim.Tick(); Equal(3, s.Milestone, "생산 이력 없는 산업 체인은 목표 미달");
        wind.Building = BuildingKind.Windmill; wind.Level = 1; sim.Recalculate();
        for (int i = 0; i < 12 && s.Milestone < 4; i++) sim.Tick(); True(s.Milestone >= 4 && s.TotalToolsProduced >= 8, "실제 공방 생산으로 산업 목표 달성");
        sim.BuyRegion(0, out _); sim.BuyRegion(2, out _); s.Population = 45; sim.Tick(); True(s.Won && s.Milestone >= 5, "대도시 승리 달성");
        int day = s.Day; sim.Tick(); True(s.Day == day + 1 && sim.ObjectiveTitle == "자유 건설", "승리 후 무한 플레이 지속");
    }

    static void NaturalProgressionFromNewGame()
    {
        var s = GameState.CreateNew(); var sim = new Simulation(s);
        void Build(BuildingKind kind, int x, int z) { True(sim.Build(kind, x, z, out var why), $"자연 진행 건설 {Catalog.Get(kind).Name} ({x},{z}): {why}"); }
        void Upgrade(Cell cell) { True(sim.Upgrade(cell.X, cell.Z, out var why), $"자연 진행 업그레이드 ({cell.X},{cell.Z}): {why}"); }
        bool Trade(Resource resource, bool buy)
        {
            int price = resource switch { Resource.Timber => 4, Resource.Stone => 5, Resource.Grain => 3, Resource.Flour => 5, Resource.Bread => 7, Resource.Ore => 5, Resource.Steel => 11, Resource.Tools => 16, _ => 0 };
            if (price == 0 || (buy && s.Coins < price * 10) || (!buy && sim.Get(resource) < 10)) return false;
            if (buy) { s.Coins -= price * 10; s.Stock[(int)resource] += 10; }
            else { s.Stock[(int)resource] -= 10; s.Coins += 10 * Math.Max(1, price / 2); }
            s.Stock[(int)Resource.Coins] = s.Coins; sim.Recalculate(); return true;
        }
        void TickEconomy(int maximum, Func<bool> done)
        {
            for (int i = 0; i < maximum && !done(); i++)
            {
                if (sim.Get(Resource.Bread) < 5) Trade(Resource.Bread, true);
                if (sim.Get(Resource.Timber) >= 80) Trade(Resource.Timber, false);
                if (sim.Get(Resource.Stone) >= 80) Trade(Resource.Stone, false);
                if (sim.Get(Resource.Tools) >= 10) Trade(Resource.Tools, false);
                sim.Tick();
            }
            True(done(), $"자연 진행 제한 {maximum}일 안에 목표 도달");
        }
        void Research(TechId id)
        {
            TechSpec tech = TechCatalog.Get(id);
            TickEconomy(120, () => s.ResearchPoints >= tech.ResearchCost && s.Coins >= tech.CoinCost);
            True(sim.StartResearch(id, out var why), $"자연 진행 연구 시작 {tech.Name}: {why}");
            int endDay = s.Day + tech.DurationDays;
            TickEconomy(tech.DurationDays + 1, () => TechCatalog.Has(s, id));
            True(s.Day <= endDay, $"{tech.Name} 예정 기간 내 완료");
        }

        Build(BuildingKind.Road, 10, 11); Build(BuildingKind.StudyHouse, 12, 9); Build(BuildingKind.Lumberyard, 7, 10); sim.Tick(); Equal(1, s.Milestone, "자연 진행 첫 도로망 목표");
        Research(TechId.CropRotation); Research(TechId.Stonecraft); Research(TechId.MechanicalPower);
        Build(BuildingKind.Windmill, 10, 7);
        Build(BuildingKind.House, 8, 9); TickEconomy(30, () => s.Population >= 10); Build(BuildingKind.Mill, 12, 11);
        TickEconomy(80, () => s.Population >= 14);
        Build(BuildingKind.Bakery, 8, 11);
        TickEconomy(80, () => s.Milestone >= 2); Equal(2, s.Milestone, "자연 진행 자급 도시 목표");
        True(sim.BuyRegion(1, out var regionWhy), "자연 진행 첫 지역 매입: " + regionWhy); sim.Tick(); Equal(3, s.Milestone, "자연 진행 확장 목표");

        Build(BuildingKind.Quarry, 13, 10);
        for (int i = 0; i < 30 && (sim.Get(Resource.Timber) < 60 || sim.Get(Resource.Stone) < 25); i++) { if (sim.Get(Resource.Bread) < 5) Trade(Resource.Bread, true); sim.Tick(); }
        True(sim.Get(Resource.Timber) >= 60 && sim.Get(Resource.Stone) >= 25, "자연 진행 업그레이드 자재 생산");
        foreach (Cell home in s.Cells.Where(c => c.Building == BuildingKind.House).ToList()) Upgrade(home);
        TickEconomy(160, () => s.Population >= 30);
        Research(TechId.Guilds); True(s.Era == Era.Renaissance, "길드 연구로 르네상스 진입");
        Research(TechId.Metallurgy); Research(TechId.Scholarship); Build(BuildingKind.Road, 10, 12); Build(BuildingKind.Academy, 9, 12);
        Research(TechId.Toolmaking); Research(TechId.SteamPower); True(s.Era == Era.Industrial, "증기력 연구로 산업 시대 진입");
        TickEconomy(200, () => s.Coins >= 320 && sim.Get(Resource.Timber) >= 40 && sim.Get(Resource.Stone) >= 35);
        Build(BuildingKind.SteamPlant, 11, 12); Research(TechId.UrbanPlanning);
        Equal(9, s.Technologies.Count, "자연 진행 기존 승리 기술 9개 완료");
        True(!s.Technologies.Any(t => (int)t > (int)TechId.UrbanPlanning), "추가 기술 없이 기존 승리 경로 유지");
        TickEconomy(200, () => s.Coins >= 200 && sim.Get(Resource.Timber) >= 40 && sim.Get(Resource.Stone) >= 15);
        foreach (Cell home in s.Cells.Where(c => c.Building == BuildingKind.House && c.Level < 3).ToList()) Upgrade(home);
        for (int i = 0; i < 240 && (sim.Get(Resource.Timber) < 85 || sim.Get(Resource.Stone) < 60 || s.Coins < 640); i++) { if (sim.Get(Resource.Bread) < 5) Trade(Resource.Bread, true); if (sim.Get(Resource.Timber) >= 105) Trade(Resource.Timber, false); if (sim.Get(Resource.Stone) >= 80) Trade(Resource.Stone, false); sim.Tick(); }
        True(sim.Get(Resource.Timber) >= 85 && sim.Get(Resource.Stone) >= 60 && s.Coins >= 640, "자연 진행 중공업 건설 자원 확보");
        True(sim.Demolish(12, 11, out _), "제분소 부지를 광산으로 전환"); Build(BuildingKind.Mine, 12, 11); Build(BuildingKind.Smelter, 9, 11); Build(BuildingKind.Workshop, 11, 11);
        TickEconomy(100, () => s.Milestone >= 4); Equal(4, s.Milestone, "자연 진행 실제 공방 산업 목표"); True(s.TotalToolsProduced >= 8, "자연 진행 공방 도구 누적 생산");
        TickEconomy(200, () => s.Population >= 45 && s.Coins >= sim.RegionCost(0));
        True(sim.BuyRegion(0, out regionWhy), "자연 진행 두 번째 확장: " + regionWhy);
        TickEconomy(300, () => s.Coins >= sim.RegionCost(2));
        True(sim.BuyRegion(2, out regionWhy), "자연 진행 세 번째 확장: " + regionWhy); sim.Tick();
        True(s.Won && s.Milestone == 5, "주입 없는 새 게임 승리");
        True(s.Day < 700, "합리적인 일수 내 완주"); Equal(s.Coins, s.Stock[(int)Resource.Coins], "자연 진행 최종 코인 미러");
        Console.WriteLine($"  자연 진행 결과: {s.Day}일, 인구 {s.Population}, 행복 {s.Happiness}, 코인 {s.Coins:0}, 총생산 {s.TotalProduced:0}");
    }
}

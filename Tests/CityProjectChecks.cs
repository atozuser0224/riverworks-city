using System;
using System.Linq;
using Riverworks;

public static class CityProjectChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        CatalogAndAtomicStart();
        PartialDeliveryAndCancel();
        BridgeCompletionCreatesProtectedRoads();
        PowerConsumesCoalOnlyWhenProduced();
        ResearchBonusRequiresConnection();
        ValidationRejectsMalformedState();
        Console.WriteLine($"도시 대형 프로젝트 {passed}개 검증 통과");
        return passed;
    }

    static void CatalogAndAtomicStart()
    {
        GameState state = Fixture();
        Equal(3, CityProjects.All.Count, "대형 프로젝트 3종 카탈로그");
        float coins = state.Coins;
        True(!CityProjects.Start(state, CityProjectKind.CentralPowerPlant, 20, 20, out _), "경계 밖 시작 거부");
        Near(coins, state.Coins, "실패 시작 비용 원자성");
        True(CityProjects.Start(state, CityProjectKind.CentralPowerPlant, 12, 11, out _), "중앙 발전소 시작");
        Near(coins - 600, state.Coins, "시작 비용 1회 차감");
        True(!CityProjects.Start(state, CityProjectKind.CentralPowerPlant, 15, 11, out _), "동종 프로젝트 중복 거부");
        True(CityProjects.Occupies(state, 13, 12), "2x2 실제 부지 점유");
        var sim = new Simulation(state);
        True(!sim.CanBuild(BuildingKind.Road, 13, 12, out _), "프로젝트 부지 도시 건설 차단");
    }

    static void PartialDeliveryAndCancel()
    {
        GameState state = Fixture();
        True(CityProjects.Start(state, CityProjectKind.ResearchCampus, 12, 14, out _), "연구 단지 시작");
        CityProjectState project = CityProjects.Find(state, CityProjectKind.ResearchCampus);
        state.Stock[(int)Resource.Stone] = 7;
        state.Stock[(int)Resource.SteelBeam] = 1.99995f;
        True(CityProjects.Deliver(state, project.Kind, out _), "가용 정수 자재 부분 납품");
        Equal(7, project.Delivered[(int)Resource.Stone], "돌 부분 납품 보존");
        Equal(1, project.Delivered[(int)Resource.SteelBeam], "소수 재고는 실제 정수 수량만 납품");
        Near(.99995f, state.Stock[(int)Resource.SteelBeam], "부분 납품 후 재고 비음수 보존");
        Equal(0, project.DaysRemaining, "미충족 단계 공사 미시작");
        int day = state.Day;
        CityProjects.Tick(state);
        Equal(day, state.Day, "프로젝트 틱은 도시 날짜를 임의 변경하지 않음");
        float beforeCancel = state.Coins;
        True(CityProjects.Cancel(state, project.Kind, out _), "진행 중 프로젝트 취소");
        Near(7, state.Stock[(int)Resource.Stone], "현재 단계 돌 전량 반환");
        Near(1.99995f, state.Stock[(int)Resource.SteelBeam], "현재 단계 강철 보 전량 반환");
        Near(beforeCancel + 500 * .35f, state.Coins, "시작 비용 35퍼센트 반환");
        True(CityProjects.Find(state, project.Kind) == null, "취소 후 프로젝트 제거");
    }

    static void BridgeCompletionCreatesProtectedRoads()
    {
        GameState state = Fixture();
        for (int x = 1; x <= 4; x++) Set(state, x, 10, x == 2 || x == 3 ? TerrainKind.Water : TerrainKind.Grass, BuildingKind.None);
        for (int x = 5; x <= 9; x++) Set(state, x, 10, TerrainKind.Grass, BuildingKind.Road);
        var sim = new Simulation(state);
        sim.Recalculate();
        True(CityProjects.Start(state, CityProjectKind.GrandBridge, 1, 10, out _), "육지 양끝과 수면 내부의 대교 시작");
        Complete(state, CityProjectKind.GrandBridge);
        CityProjectState bridge = CityProjects.Find(state, CityProjectKind.GrandBridge);
        Equal(3, bridge.Stage, "대교 3단계 완공");
        True(Enumerable.Range(1, 4).All(x => state.Cells[10 * state.Size + x].Building == BuildingKind.Road), "완공 대교가 실제 도로로 전환");
        sim.Recalculate();
        True(state.Cells[10 * state.Size + 1].Connected, "대교 도로가 실제 경로망에 연결");
        True(!sim.Demolish(2, 10, out _), "완공 프로젝트 도로 개별 철거 차단");
        True(!CityProjects.Cancel(state, CityProjectKind.GrandBridge, out _), "완공 프로젝트 취소 거부");
    }

    static void PowerConsumesCoalOnlyWhenProduced()
    {
        GameState state = Fixture();
        True(CityProjects.Start(state, CityProjectKind.CentralPowerPlant, 12, 11, out _), "발전소 완공 검사 시작");
        Complete(state, CityProjectKind.CentralPowerPlant);
        state.Stock[(int)Resource.Coal] = 2;
        Near(80, CityProjects.PreviewPower(state), "석탄 보유 발전량 미리보기");
        Near(2, state.Stock[(int)Resource.Coal], "미리보기 석탄 무소비");
        Equal(80, CityProjects.ProducePower(state), "실제 발전 80 공급");
        Near(0, state.Stock[(int)Resource.Coal], "마지막 석탄 2개 정확 소비");
        Near(80, CityProjects.PreviewPower(state), "결제한 당일 재계산 발전량 유지");
        Equal(80, CityProjects.ProducePower(state), "같은 날 반복 발전 공급 유지");
        Near(0, state.Stock[(int)Resource.Coal], "같은 날 반복 호출 연료 무중복");

        Set(state, 11, 11, TerrainKind.Grass, BuildingKind.None);
        Set(state, 11, 12, TerrainKind.Grass, BuildingKind.None);
        new Simulation(state).Recalculate();
        Equal(0, CityProjects.PreviewPower(state), "결제 당일 도로 단선 발전 정지");
        Set(state, 11, 11, TerrainKind.Grass, BuildingKind.Road);
        new Simulation(state).Recalculate();
        Equal(80, CityProjects.PreviewPower(state), "결제 당일 도로 복구 발전 재개");
        state.Day++;
        Equal(0, CityProjects.PreviewPower(state), "다음 날 석탄 없으면 발전 정지");
        Equal(0, CityProjects.ProducePower(state), "다음 날 무연료 실제 발전 정지");
    }

    static void ResearchBonusRequiresConnection()
    {
        GameState state = Fixture();
        True(CityProjects.Start(state, CityProjectKind.ResearchCampus, 12, 14, out _), "연구 단지 완공 검사 시작");
        Complete(state, CityProjectKind.ResearchCampus);
        Near(10, CityProjects.ResearchBonus(state), "연결된 연구 단지 하루 보너스");
        Set(state, 11, 13, TerrainKind.Grass, BuildingKind.None);
        new Simulation(state).Recalculate();
        Near(0, CityProjects.ResearchBonus(state), "도로 단절 시 연구 보너스 정지");
    }

    static void ValidationRejectsMalformedState()
    {
        GameState state = Fixture();
        True(CityProjects.Start(state, CityProjectKind.ResearchCampus, 12, 14, out _), "검증용 프로젝트 시작");
        CityProjects.Validate(state);
        CityProjectState project = CityProjects.Find(state, CityProjectKind.ResearchCampus);
        project.Delivered[(int)Resource.Water] = 1;
        bool rejected = false;
        try { CityProjects.Validate(state); } catch (ArgumentException) { rejected = true; }
        True(rejected, "프로젝트 유체 납품 저장 거부");
    }

    static GameState Fixture()
    {
        GameState state = GameState.CreateNew();
        state.CityProjects.Clear();
        state.OwnedRegions = Enumerable.Range(0, 9).ToList();
        state.Technologies = TechCatalog.All.Select(spec => spec.Id).Where(id => id != TechId.None).ToList();
        state.Coins = 10000;
        for (int i = 0; i < state.Stock.Count; i++) state.Stock[i] = 0;
        state.Stock[(int)Resource.Coins] = state.Coins;
        foreach (Cell cell in state.Cells) Set(state, cell.X, cell.Z, TerrainKind.Grass, BuildingKind.None);
        Set(state, 10, 10, TerrainKind.Grass, BuildingKind.TownHall);
        for (int z = 11; z <= 16; z++) Set(state, 11, z, TerrainKind.Grass, BuildingKind.Road);
        Set(state, 11, 10, TerrainKind.Grass, BuildingKind.Road);
        var sim = new Simulation(state);
        sim.Recalculate();
        return state;
    }

    static void Complete(GameState state, CityProjectKind kind)
    {
        CityProjectSpec spec = CityProjects.Get(kind);
        CityProjectState project = CityProjects.Find(state, kind);
        while (project.Stage < spec.Stages.Length)
        {
            foreach (RecipeAmount requirement in spec.Stages[project.Stage].Requirements)
                state.Stock[(int)requirement.Resource] = requirement.Amount;
            True(CityProjects.Deliver(state, kind, out _), $"{kind} {project.Stage + 1}단계 자재 납품");
            int days = project.DaysRemaining;
            for (int i = 0; i < days; i++) { state.Day++; CityProjects.Tick(state); }
        }
    }

    static void Set(GameState state, int x, int z, TerrainKind terrain, BuildingKind building)
    {
        Cell cell = state.Cells[z * state.Size + x];
        cell.Terrain = terrain;
        cell.Building = building;
        cell.Level = building == BuildingKind.None ? 0 : 1;
        cell.Connected = false;
        cell.Progress = 0;
        cell.Status = "";
    }

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("도시 프로젝트 검사 실패: " + name);
        passed++;
        Console.WriteLine("✓ " + name);
    }

    static void Equal(int expected, int actual, string name)
    {
        if (expected != actual) throw new Exception($"도시 프로젝트 검사 실패: {name} (기대 {expected}, 실제 {actual})");
        passed++;
        Console.WriteLine("✓ " + name);
    }

    static void Near(float expected, float actual, string name)
    {
        if (Math.Abs(expected - actual) > .001f) throw new Exception($"도시 프로젝트 검사 실패: {name} (기대 {expected}, 실제 {actual})");
        passed++;
        Console.WriteLine("✓ " + name);
    }
}

using Riverworks;

public static class FactoryChecks
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("공장 검사 실패: " + name); passed++; Console.WriteLine("✓ " + name); }
    static int Stored(FactoryState s, Resource r) => s.Entities.Sum(e => e.Input[(int)r] + e.Output[(int)r] + (e.CargoResource == r ? 1 : 0));

    public static int Run()
    {
        passed = 0;
        CatalogAndPlacement(); TransferBackpressureAndCycle(); InserterAtomicity(); MachinePowerAndRecovery(); TechnologyRatePowerAndMassProduction(); PowerCacheInvalidation(); SplitterFallbackAndRotation(); ExampleExportsTools(); SaveContract();
        return passed;
    }

    static void CatalogAndPlacement()
    {
        var s = FactoryState.CreateEmpty(); var sim = new FactorySimulation(s);
        True(FactoryCatalog.All.Count() == 11, "모든 공장 설비 카탈로그 등록");
        True(!sim.TryPlace(FactoryKind.Drill, 10, 10, 0, out var why) && why.Contains("광맥"), "광맥 밖 채굴기 거부");
        True(sim.TryPlace(FactoryKind.Drill, 1, 7, 3, out why), "광맥 위 2x2 채굴기 배치");
        True(sim.GetAt(2, 8)?.Kind == FactoryKind.Drill, "2x2 점유 셀 조회");
        True(!sim.TryPlace(FactoryKind.Belt, 2, 8, 0, out why) && why.Contains("차지"), "겹친 설비 원자적 거부");
        True(!sim.TryPlace(FactoryKind.ExportDock, 23, 15, 0, out why) && why.Contains("경계"), "2x2 경계 초과 거부");
        True(sim.Rotate(sim.GetAt(1, 7).Id, out _) && sim.GetAt(1, 7).Direction == 0, "방향 3에서 동쪽 0으로 회전");
        var inlet = new FactoryEntity { Kind = FactoryKind.PowerInlet, X = 0, Z = 0 };
        var pole = new FactoryEntity { Kind = FactoryKind.Pole, X = 7, Z = 0 };
        var machine = new FactoryEntity { Kind = FactoryKind.Assembler, X = 5, Z = 0 };
        True(FactorySimulation.GridDistance(inlet, pole) == 6 && FactorySimulation.GridDistance(inlet, machine) == 4, "footprint 가장자리 기준 전력 거리");
        True(FactoryCatalog.RecipeDuration(FactoryRecipe.IronPlate) == 4f && FactoryCatalog.RecipeDuration(FactoryRecipe.Tools) == 6f && FactoryCatalog.RecipeDuration(FactoryRecipe.Bread) == 5f && FactoryCatalog.RecipeDuration(FactoryRecipe.None) == 0f, "공개 제조 시간 카탈로그");
    }

    static void TransferBackpressureAndCycle()
    {
        var s = FactoryState.CreateEmpty(); var sim = new FactorySimulation(s);
        sim.TryPlace(FactoryKind.Belt, 5, 5, 0, out _); sim.TryPlace(FactoryKind.Belt, 6, 5, 0, out _);
        var first = sim.GetAt(5, 5); var second = sim.GetAt(6, 5); first.CargoResource = Resource.Timber;
        sim.Tick(1); True(first.CargoResource == Resource.Coins && second.CargoResource == Resource.Timber && second.CargoProgress == 0, "한 틱에 한 설비만 이동");
        sim.Tick(1); True(second.CargoResource == Resource.Timber && Math.Abs(second.CargoProgress - 1) < .001f, "막힌 벨트 역압 보존");
        int before = Stored(s, Resource.Timber); for (int i = 0; i < 30; i++) sim.Tick(.1f);
        True(Stored(s, Resource.Timber) == before, "정체 큐에서 물자 무손실");

        var loop = FactoryState.CreateEmpty(); var cycle = new FactorySimulation(loop);
        cycle.TryPlace(FactoryKind.Belt, 3, 3, 0, out _); cycle.TryPlace(FactoryKind.Belt, 4, 3, 1, out _);
        cycle.TryPlace(FactoryKind.Belt, 4, 4, 2, out _); cycle.TryPlace(FactoryKind.Belt, 3, 4, 3, out _);
        cycle.GetAt(3, 3).CargoResource = Resource.Stone;
        for (int i = 0; i < 40; i++) cycle.Tick(.25f);
        True(Stored(loop, Resource.Stone) == 1, "순환 벨트 물자 보존");

        var opposed = FactoryState.CreateEmpty(); var jam = new FactorySimulation(opposed);
        jam.TryPlace(FactoryKind.Belt, 3, 3, 0, out _); jam.TryPlace(FactoryKind.Belt, 4, 3, 2, out _);
        jam.GetAt(3, 3).CargoResource = Resource.Ore; jam.Tick(1);
        True(jam.GetAt(3, 3).CargoResource == Resource.Ore && jam.GetAt(4, 3).CargoResource == Resource.Coins, "마주 보는 벨트가 물자를 왕복시키지 않고 역압");
    }

    static void InserterAtomicity()
    {
        var s = FactoryState.CreateEmpty(); var sim = new FactorySimulation(s);
        sim.TryPlace(FactoryKind.PowerInlet, 1, 1, 0, out _); sim.TryPlace(FactoryKind.Inserter, 3, 1, 0, out _); sim.TryPlace(FactoryKind.Belt, 4, 1, 0, out _);
        var arm = sim.GetAt(3, 1); arm.CargoResource = Resource.Timber; arm.CargoProgress = 1;
        True(!sim.Rotate(arm.Id, out var why) && why.Contains("운반 중") && arm.Direction == 0 && arm.CargoResource == Resource.Timber, "적재 투입기 회전 거부와 상태 보존");
        sim.Tick(.1f); var belt = sim.GetAt(4, 1);
        True(arm.CargoResource == Resource.Coins && belt.CargoResource == Resource.Timber && belt.CargoProgress == 0, "투입기가 놓은 물자는 같은 틱에 벨트 재이동 금지");
        arm.CargoResource = Resource.Steel; arm.CargoProgress = .4f; int recovered = s.Recovered[(int)Resource.Steel];
        True(sim.Remove(3, 1, out _) && s.Recovered[(int)Resource.Steel] == recovered + 1, "투입기 보유 물자 철거 회수");
    }

    static void MachinePowerAndRecovery()
    {
        var s = FactoryState.CreateEmpty(); var sim = new FactorySimulation(s);
        sim.TryPlace(FactoryKind.PowerInlet, 1, 1, 0, out _); sim.TryPlace(FactoryKind.Furnace, 4, 1, 0, out _);
        var furnace = sim.GetAt(4, 1); sim.AddRaw(furnace, Resource.Ore, 2);
        s.PowerBudget = 0; sim.Tick(5); True(furnace.Output[(int)Resource.Steel] == 0 && furnace.Progress == 0, "전력 차단 시 가공 정지");
        s.PowerBudget = 20; sim.Tick(4); True(furnace.Output[(int)Resource.Steel] == 1 && furnace.Input[(int)Resource.Ore] == 0, "전력 복구 후 원자적 제련 완료");

        sim.AddRaw(furnace, Resource.Ore, 2); sim.Tick(2); int expectedOre = furnace.Input[(int)Resource.Ore]; int expectedSteel = furnace.Output[(int)Resource.Steel];
        True(sim.Remove(4, 1, out _), "가공 중 설비 철거");
        True(s.Recovered[(int)Resource.Ore] == expectedOre && s.Recovered[(int)Resource.Steel] == expectedSteel, "가공 예약 원료와 출력 전량 회수");
        True(s.Recovered[(int)Resource.Coins] == 0, "Coins 센티널은 물자로 회수하지 않음");
    }

    static void TechnologyRatePowerAndMassProduction()
    {
        var plainBeltState = FactoryState.CreateEmpty(); var plainBelt = new FactorySimulation(plainBeltState);
        plainBelt.TryPlace(FactoryKind.Belt, 1, 1, 0, out _); plainBelt.TryPlace(FactoryKind.Belt, 2, 1, 0, out _);
        plainBelt.GetAt(1, 1).CargoResource = Resource.Timber; plainBelt.Tick(.4f);
        True(plainBelt.GetAt(1, 1).CargoResource == Resource.Timber && Math.Abs(plainBelt.GetAt(1, 1).CargoProgress - .8f) < .001f, "기본 벨트 속도 유지");

        var fastBeltState = FactoryState.CreateEmpty(); var fastBelt = new FactorySimulation(fastBeltState);
        fastBelt.TryPlace(FactoryKind.Belt, 1, 1, 0, out _); fastBelt.TryPlace(FactoryKind.Belt, 2, 1, 0, out _);
        fastBelt.GetAt(1, 1).CargoResource = Resource.Timber;
        fastBelt.ConfigureTechnology(WithTechnology(TechId.Logistics)); fastBelt.Tick(.4f);
        True(fastBelt.GetAt(1, 1).CargoResource == Resource.Coins && fastBelt.GetAt(2, 1).CargoResource == Resource.Timber && fastBelt.BeltSpeedMultiplier == 1.5f, "물류 연구 벨트 처리율 1.5배");

        var armState = FactoryState.CreateEmpty(); var armSim = new FactorySimulation(armState);
        armSim.TryPlace(FactoryKind.PowerInlet, 0, 0, 0, out _); armSim.TryPlace(FactoryKind.Inserter, 2, 0, 0, out _); armSim.TryPlace(FactoryKind.Storage, 3, 0, 0, out _);
        armSim.GetAt(2, 0).CargoResource = Resource.Grain;
        armSim.ConfigureTechnology(WithTechnology(TechId.Automation)); armSim.Tick(.5f);
        True(armSim.GetAt(2, 0).CargoResource == Resource.Coins && armSim.GetAt(3, 0).Input[(int)Resource.Grain] == 1 && armSim.InserterSpeedMultiplier == 1.5f, "자동화 연구 투입기 처리율 1.5배");

        var craftState = FactoryState.CreateEmpty(); var craft = new FactorySimulation(craftState);
        craft.TryPlace(FactoryKind.PowerInlet, 0, 0, 0, out _); craft.TryPlace(FactoryKind.Furnace, 2, 0, 0, out _);
        var furnace = craft.GetAt(2, 0); craft.AddRaw(furnace, Resource.Ore, 2);
        craft.ConfigureTechnology(WithTechnology(TechId.MassProduction)); craft.Tick(3.2f);
        True(furnace.Input[(int)Resource.Ore] == 0 && furnace.Output[(int)Resource.Steel] == 1 && craftState.Produced[(int)Resource.Steel] == 1 && craft.MachineSpeedMultiplier == 1.25f, "대량생산 연구 제조율과 원료 원자적 소비");

        var powerState = FactoryState.CreateEmpty(); powerState.PowerBudget = 7;
        var power = new FactorySimulation(powerState);
        power.TryPlace(FactoryKind.PowerInlet, 0, 0, 0, out _); power.TryPlace(FactoryKind.Assembler, 5, 0, 0, out _); power.TryPlace(FactoryKind.Assembler, 5, 2, 0, out _);
        var firstAssembler = power.GetAt(5, 0); var secondAssembler = power.GetAt(5, 2);
        True(firstAssembler.Powered && !secondAssembler.Powered && Math.Abs(power.PowerUsed - 4f) < .001f, "기본 조립기 둘은 전력 7 예산 초과");
        power.ConfigureTechnology(WithTechnology(TechId.Electrification));
        True(firstAssembler.Powered && secondAssembler.Powered && Math.Abs(power.PowerUsed - 6.8f) < .001f && power.PowerDemandMultiplier == .85f, "전기화 연구 전력 수요 15퍼센트 절감");
        power.ConfigureTechnology(null);
        True(firstAssembler.Powered && !secondAssembler.Powered && power.BeltSpeedMultiplier == 1f && power.InserterSpeedMultiplier == 1f && power.MachineSpeedMultiplier == 1f && power.PowerDemandMultiplier == 1f, "기술 배율은 런타임 구성마다 기본값으로 재설정");
    }

    static void PowerCacheInvalidation()
    {
        var state = FactoryState.CreateEmpty(); var sim = new FactorySimulation(state);
        sim.TryPlace(FactoryKind.PowerInlet, 0, 0, 0, out _); sim.TryPlace(FactoryKind.Pole, 7, 0, 0, out _); sim.TryPlace(FactoryKind.Assembler, 11, 0, 0, out _);
        var assembler = sim.GetAt(11, 0);
        True(assembler.Powered, "전력 캐시가 새 인입구·전신주·설비 배치를 즉시 반영");
        sim.Recalculate(); sim.Recalculate();
        True(assembler.Powered && Math.Abs(sim.PowerUsed - 4f) < .001f, "변경 없는 재계산이 전력 결과를 보존");
        sim.Remove(7, 0, out _);
        True(!assembler.Powered && sim.PowerUsed == 0, "전력 캐시가 전신주 철거를 즉시 반영");
        sim.TryPlace(FactoryKind.Pole, 7, 0, 0, out _);
        True(assembler.Powered, "전력 캐시가 전신주 재배치를 즉시 반영");
        state.PowerBudget = 0; sim.Recalculate();
        True(!assembler.Powered && sim.PowerAvailable == 0, "전력 캐시가 예산 변경을 즉시 반영");
    }

    static void SplitterFallbackAndRotation()
    {
        var s = FactoryState.CreateEmpty(); var sim = new FactorySimulation(s);
        sim.TryPlace(FactoryKind.Splitter, 8, 8, 0, out _); sim.TryPlace(FactoryKind.Storage, 9, 8, 0, out _); sim.TryPlace(FactoryKind.Storage, 8, 9, 0, out _);
        var split = sim.GetAt(8, 8); var forward = sim.GetAt(9, 8); var left = sim.GetAt(8, 9);
        for (int i = 0; i < 6; i++) { split.CargoResource = Resource.Grain; split.CargoProgress = 1; sim.Tick(.01f); }
        True(forward.Input[(int)Resource.Grain] == 3 && left.Input[(int)Resource.Grain] == 3, "분배기 양쪽 공정 분배");
        while (forward.Input.Sum() < 80) forward.Input[(int)Resource.Stone]++;
        split.SplitLeft = false; split.CargoResource = Resource.Flour; split.CargoProgress = 1; sim.Tick(.01f);
        True(left.Input[(int)Resource.Flour] == 1 && split.CargoResource == Resource.Coins, "첫 분기 차단 시 다른 분기로 우회");
        int id = split.Id; sim.Rotate(id, out _); True(split.Direction == 1, "분배기 회전 방향 반영");
    }

    static void ExampleExportsTools()
    {
        FactoryState s = FactoryState.CreateExample(); var sim = new FactorySimulation(s);
        True(sim.PowerAvailable == 20 && sim.PowerUsed <= 20, "예제 공장이 전력 20 예산 안에서 가동");
        for (int i = 0; i < 6000 && s.Entities.Where(e => e.Kind == FactoryKind.ExportDock).Sum(e => e.Input[(int)Resource.Tools]) == 0; i++) sim.Tick(.1f);
        int waiting = s.Entities.Where(e => e.Kind == FactoryKind.ExportDock).Sum(e => e.Input[(int)Resource.Tools]);
        True(waiting > 0 && s.Produced[(int)Resource.Tools] > 0, "예제 라인이 실제 도구 생산·반출 대기");
        int taken = sim.TakeExports(Resource.Tools, 1); True(taken == 1 && s.Exported[(int)Resource.Tools] == 1, "반출 부두에서 도구 인계 누적");
        True(s.Entities.All(e => e.Input[0] == 0 && e.Output[0] == 0 && (e.CargoResource != Resource.Coins || e.CargoProgress == 0)), "Coins 물자 생성 금지");
        int produced = s.Produced[(int)Resource.Tools]; for (int i = 0; i < 6000; i++) { sim.TakeExports(Resource.Tools); sim.Tick(.1f); }
        True(s.Produced[(int)Resource.Tools] > produced && s.Entities.First(e => e.Kind == FactoryKind.Assembler).Input[(int)Resource.Timber] <= 12, "장시간 가동에서도 이중 원료 버퍼가 교착되지 않음");
    }

    static void SaveContract()
    {
        FactoryState s = FactoryState.CreateExample(); FactorySimulation.ValidateState(s);
        True(s.Version == 1 && s.Entities.Count > 0 && s.Entities.All(e => e.Input.Count == 9 && e.Output.Count == 9), "직렬화 DTO 고정 길이 목록");
        var bad = FactoryState.CreateEmpty(); bad.Produced[0] = 1;
        bool rejected = false; try { FactorySimulation.ValidateState(bad); } catch (System.IO.InvalidDataException) { rejected = true; }
        True(rejected, "센티널 오염 저장 데이터 거부");
        bad = FactoryState.CreateEmpty(); bad.ElapsedSeconds = float.NaN; rejected = false; try { FactorySimulation.ValidateState(bad); } catch (System.IO.InvalidDataException) { rejected = true; }
        True(rejected, "비정상 시간 저장 데이터 거부");
        bad = FactoryState.CreateEmpty(); bad.NextEntityId = 0; rejected = false; try { FactorySimulation.ValidateState(bad); } catch (System.IO.InvalidDataException) { rejected = true; }
        True(rejected, "비정상 다음 ID 저장 데이터 거부");
    }

    static void AddRaw(this FactorySimulation sim, FactoryEntity entity, Resource resource, int amount) => entity.Input[(int)resource] += amount;
    static GameState WithTechnology(TechId id) { var state = new GameState(); state.Technologies.Add(id); return state; }
}

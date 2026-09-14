using Riverworks;

public static class BlueprintChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        CatalogContract();
        for (int index = 0; index < FactoryBlueprints.Count; index++) SchemaAndLayout(index);
        ToolsLineProducesWithoutStarving();
        BreadLineProducesWithoutStarving();
        SplitterBalancesWithoutStarving();
        return passed;
    }

    static void CatalogContract()
    {
        True(FactoryBlueprints.Count == 3, "블루프린트 3종 제공");
        True(Enumerable.Range(0, FactoryBlueprints.Count).All(index =>
            !string.IsNullOrWhiteSpace(FactoryBlueprints.Name(index)) &&
            !string.IsNullOrWhiteSpace(FactoryBlueprints.Description(index))), "모든 블루프린트 이름과 설명 제공");
        True(Enumerable.Range(0, FactoryBlueprints.Count).Select(FactoryBlueprints.Name).Distinct().Count() == FactoryBlueprints.Count,
            "블루프린트 이름 중복 없음");
        True(ThrowsOutOfRange(() => FactoryBlueprints.Create(-1)) &&
             ThrowsOutOfRange(() => FactoryBlueprints.Name(FactoryBlueprints.Count)) &&
             ThrowsOutOfRange(() => FactoryBlueprints.Description(FactoryBlueprints.Count)), "범위를 벗어난 블루프린트 인덱스 거부");
    }

    static void SchemaAndLayout(int index)
    {
        FactoryState state = FactoryBlueprints.Create(index);
        FactorySimulation.ValidateState(state);
        True(state.Version == 3 && state.Width == 24 && state.Height == 16 && state.PowerBudget == 20,
            $"{FactoryBlueprints.Name(index)} 스키마와 전력 예산");
        True(NoOverlap(state), $"{FactoryBlueprints.Name(index)} 설비 겹침 없음");
        True(state.Entities.All(entity => entity.Input.Count == ResourceCatalog.Count && entity.Output.Count == ResourceCatalog.Count &&
            entity.Input[0] == 0 && entity.Output[0] == 0), $"{FactoryBlueprints.Name(index)} 고정 길이 물자 목록");
        True(state.Produced.Sum() == 0 && state.Exported.Sum() == 0 && state.Recovered.Sum() == 0 &&
            state.Entities.All(entity => entity.Output.Sum() == 0 && entity.CargoResource == Resource.Coins &&
                entity.CargoProgress == 0 && entity.Progress == 0 &&
                (entity.Input.Sum() == 0 || entity.Kind == FactoryKind.ImportDock)),
            $"{FactoryBlueprints.Name(index)} 반입 원료만 초기 투입");
    }

    static void ToolsLineProducesWithoutStarving()
    {
        FactoryState state = FactoryBlueprints.Create(0);
        var sim = new FactorySimulation(state);
        RunUntil(sim, 8000, () => ExportWaiting(state, Resource.Tools) >= 1);
        True(ExportWaiting(state, Resource.Tools) >= 1 && state.Produced[(int)Resource.Tools] >= 1,
            "도구 생산 라인이 도구를 생산하고 반출 부두까지 운송");

        int before = state.Produced[(int)Resource.Tools];
        sim.TakeExports(Resource.Tools);
        RunUntil(sim, 8000, () => state.Produced[(int)Resource.Tools] > before);
        True(state.Produced[(int)Resource.Tools] > before, "도구 생산 라인이 첫 완제품 뒤에도 계속 진행");
    }

    static void BreadLineProducesWithoutStarving()
    {
        FactoryState state = FactoryBlueprints.Create(1);
        var sim = new FactorySimulation(state);
        RunUntil(sim, 8000, () => ExportWaiting(state, Resource.Bread) >= 3);
        True(state.Produced[(int)Resource.Flour] >= 2 && state.Produced[(int)Resource.Bread] >= 3 &&
            ExportWaiting(state, Resource.Bread) >= 3, "빵 생산 라인이 제분과 제빵을 거쳐 빵을 반출 부두까지 운송");

        int before = state.Produced[(int)Resource.Bread];
        sim.TakeExports(Resource.Bread);
        RunUntil(sim, 8000, () => state.Produced[(int)Resource.Bread] > before);
        True(state.Produced[(int)Resource.Bread] > before, "빵 생산 라인이 첫 완제품 뒤에도 계속 진행");
    }

    static void SplitterBalancesWithoutStarving()
    {
        FactoryState state = FactoryBlueprints.Create(2);
        var sim = new FactorySimulation(state);
        List<FactoryEntity> stores = state.Entities.Where(entity => entity.Kind == FactoryKind.Storage).OrderBy(entity => entity.Id).ToList();
        RunUntil(sim, 8000, () => stores.Sum(store => store.Input[(int)Resource.Grain]) >= 12);
        int first = stores[0].Input[(int)Resource.Grain], second = stores[1].Input[(int)Resource.Grain];
        True(first + second >= 12 && Math.Abs(first - second) <= 1, "분배기가 곡물을 두 창고에 균등 분배");

        int before = first + second;
        RunUntil(sim, 8000, () => stores.Sum(store => store.Input[(int)Resource.Grain]) > before);
        True(stores.Sum(store => store.Input[(int)Resource.Grain]) > before, "균등 분배 라인이 첫 묶음 뒤에도 계속 진행");
    }

    static void RunUntil(FactorySimulation sim, int maximumTicks, Func<bool> done)
    {
        for (int tick = 0; tick < maximumTicks && !done(); tick++) sim.Tick(.1f);
    }

    static int ExportWaiting(FactoryState state, Resource resource) => state.Entities
        .Where(entity => entity.Kind == FactoryKind.ExportDock)
        .Sum(entity => entity.Input[(int)resource]);

    static bool NoOverlap(FactoryState state)
    {
        var occupied = new HashSet<int>();
        foreach (FactoryEntity entity in state.Entities)
        {
            FactorySpec spec = FactoryCatalog.Get(entity.Kind);
            for (int z = entity.Z; z < entity.Z + spec.Height; z++)
                for (int x = entity.X; x < entity.X + spec.Width; x++)
                    if (!occupied.Add(z * state.Width + x)) return false;
        }
        return true;
    }

    static bool ThrowsOutOfRange(Action action)
    {
        try { action(); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("블루프린트 검사 실패: " + name);
        passed++;
        Console.WriteLine("✓ " + name);
    }
}

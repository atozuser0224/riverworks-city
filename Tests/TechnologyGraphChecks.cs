using Riverworks;

public static class TechnologyGraphChecks
{
    static int passed;

    static readonly TechId[] CoreTechnologies =
    {
        TechId.CropRotation,
        TechId.Stonecraft,
        TechId.MechanicalPower,
        TechId.Guilds,
        TechId.Metallurgy,
        TechId.Scholarship,
        TechId.Toolmaking,
        TechId.SteamPower,
        TechId.UrbanPlanning
    };

    static readonly TechId[] OptionalTechnologies =
    {
        TechId.Forestry,
        TechId.Irrigation,
        TechId.Masonry,
        TechId.Logistics,
        TechId.Education,
        TechId.MetallurgicalEfficiency,
        TechId.Electrification,
        TechId.MassProduction,
        TechId.Automation
    };

    public static int Run()
    {
        passed = 0;
        CatalogIsACompleteDag();
        OriginalTechnologyContractAndEraProgression();
        EveryTechnologyCanBeResearchedFromANewGame();
        ExistingV3TechnologyStateRemainsValid();
        OptionalResearchDoesNotRegressIndustrialEra();
        return passed;
    }

    static void CatalogIsACompleteDag()
    {
        TechSpec[] specs = TechCatalog.All.ToArray();
        TechId[] enumIds = Enum.GetValues<TechId>().Where(id => id != TechId.None).ToArray();

        True(specs.Length == 26 && enumIds.Length == 26, "기술 enum과 카탈로그가 각각 26개");
        True(specs.All(spec => spec != null && spec.Id != TechId.None), "카탈로그에 None 또는 null 기술 없음");
        True(specs.Select(spec => spec.Id).Distinct().Count() == specs.Length, "기술 ID 중복 없음");
        True(enumIds.All(id => specs.Any(spec => spec.Id == id)), "모든 기술 enum이 카탈로그에 등록");

        var catalogIds = specs.Select(spec => spec.Id).ToHashSet();
        True(specs.All(spec => spec.Prerequisites != null), "모든 선행 기술 목록이 유효");
        True(specs.All(spec => spec.Prerequisites.Distinct().Count() == spec.Prerequisites.Length), "한 기술의 선행 기술 중복 없음");
        True(specs.All(spec => spec.Prerequisites.All(required => required != TechId.None && catalogIds.Contains(required))), "모든 선행 기술이 카탈로그에 존재");
        True(specs.All(spec => !spec.Prerequisites.Contains(spec.Id)), "자기 자신을 선행 기술로 요구하지 않음");

        TechId[] order = TopologicalOrder(specs);
        True(order.Length == 26 && order.Distinct().Count() == 26, "기술 그래프가 순환 없는 DAG이며 전체 도달 가능");

        var rank = order.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        True(specs.All(spec => spec.Prerequisites.All(required => rank[required] < rank[spec.Id])), "모든 선행 기술이 위상 순서에서 먼저 도달");

        TechId[] factoryBonuses = { TechId.Logistics, TechId.Electrification, TechId.MassProduction, TechId.Automation };
        True(factoryBonuses.All(rank.ContainsKey), "공장 보너스 기술 4종이 연구 그래프에서 도달 가능");
        True(factoryBonuses.All(id => TechCatalog.Get(id).Prerequisites.All(required => rank[required] < rank[id])), "공장 보너스에 후행 기술 선행 조건 교착 없음");
    }

    static void OriginalTechnologyContractAndEraProgression()
    {
        var expectedPrerequisites = new Dictionary<TechId, TechId[]>
        {
            [TechId.CropRotation] = Array.Empty<TechId>(),
            [TechId.Stonecraft] = Array.Empty<TechId>(),
            [TechId.MechanicalPower] = new[] { TechId.CropRotation },
            [TechId.Guilds] = new[] { TechId.Stonecraft, TechId.MechanicalPower },
            [TechId.Metallurgy] = new[] { TechId.Guilds },
            [TechId.Scholarship] = new[] { TechId.Guilds },
            [TechId.Toolmaking] = new[] { TechId.Metallurgy },
            [TechId.SteamPower] = new[] { TechId.Scholarship, TechId.Toolmaking },
            [TechId.UrbanPlanning] = new[] { TechId.SteamPower }
        };

        True(CoreTechnologies.Select((id, index) => (int)id == index + 1).All(value => value), "기존 9개 기술 enum 순서 보존");
        True(CoreTechnologies.All(id => TechCatalog.Get(id).Prerequisites.SequenceEqual(expectedPrerequisites[id])), "기존 9개 기술 선행 관계 보존");

        GameState state = RichNewGame();
        var simulation = new Simulation(state);
        foreach (TechId id in CoreTechnologies)
        {
            True(simulation.StartResearch(id, out string reason), $"기존 기술 연구 가능: {id} ({reason})");
            state.ResearchDaysRemaining = 1;
            simulation.Tick();

            Era expectedEra = id < TechId.Guilds
                ? Era.Medieval
                : id < TechId.SteamPower ? Era.Renaissance : Era.Industrial;
            True(state.Era == expectedEra, $"기존 기술 완료 후 시대 전환 유지: {id}");
        }
    }

    static void EveryTechnologyCanBeResearchedFromANewGame()
    {
        TechId[] order = TopologicalOrder(TechCatalog.All.ToArray());
        GameState state = RichNewGame();
        var simulation = new Simulation(state);

        foreach (TechId id in order)
        {
            True(simulation.CanResearch(id, out string reason), $"위상 순서에서 연구 가능: {id} ({reason})");
            True(simulation.StartResearch(id, out reason), $"위상 순서에서 연구 시작: {id} ({reason})");
            state.ResearchDaysRemaining = 1;
            simulation.Tick();
        }

        True(state.Technologies.Distinct().Count() == 26 && order.All(state.Technologies.Contains), "새 게임에서 26개 기술 모두 실제 연구 완료");
    }

    static void ExistingV3TechnologyStateRemainsValid()
    {
        GameState state = GameState.CreateNew();
        state.Version = 3;
        state.Era = Era.Industrial;
        state.Technologies = new List<TechId>(CoreTechnologies);

        _ = new Simulation(state);

        True(state.Version == 6 && state.Factory != null && state.Factory.Version == 3 && state.Factory.Width == 42 && state.Factory.Height == 42 && state.ArchivedFactory != null && state.ArchivedFactory.Version == 3,
            "기존 v3 저장이 기술 상태를 유지하며 공유 도시 v6로 이동");
        True(state.Technologies.SequenceEqual(CoreTechnologies), "기존 v3의 9개 연구 상태 그대로 유지");
        True(state.Era == Era.Industrial, "기존 v3 산업 시대 유지");
        True(OptionalTechnologies.All(id => !state.Technologies.Contains(id)), "기존 v3에 새 선택 기술을 자동 부여하지 않음");
    }

    static void OptionalResearchDoesNotRegressIndustrialEra()
    {
        GameState state = RichNewGame();
        state.Era = Era.Industrial;
        state.Technologies.AddRange(CoreTechnologies);
        var simulation = new Simulation(state);
        TechId[] order = TopologicalOrder(TechCatalog.All.ToArray()).Where(OptionalTechnologies.Contains).ToArray();

        foreach (TechId id in order)
        {
            True(simulation.StartResearch(id, out string reason), $"기존 산업 도시에서 선택 기술 연구 가능: {id} ({reason})");
            True(state.Era == Era.Industrial, $"선택 기술 시작 시 산업 시대 유지: {id}");
            state.ResearchDaysRemaining = 1;
            simulation.Tick();
            True(state.Era == Era.Industrial, $"선택 기술 완료 후 산업 시대 유지: {id}");
        }

        True(OptionalTechnologies.All(state.Technologies.Contains), "기존 산업 도시에서 새 선택 기술 9개 모두 도달");
    }

    static TechId[] TopologicalOrder(IReadOnlyCollection<TechSpec> specs)
    {
        var remaining = specs.ToDictionary(spec => spec.Id);
        var researched = new HashSet<TechId>();
        var order = new List<TechId>();

        while (remaining.Count > 0)
        {
            TechId[] ready = remaining.Values
                .Where(spec => spec.Prerequisites.All(researched.Contains))
                .Select(spec => spec.Id)
                .ToArray();
            if (ready.Length == 0) break;
            foreach (TechId id in ready)
            {
                researched.Add(id);
                order.Add(id);
                remaining.Remove(id);
            }
        }

        return order.ToArray();
    }

    static GameState RichNewGame()
    {
        GameState state = GameState.CreateNew();
        state.Coins = 1_000_000;
        state.Stock[(int)Resource.Coins] = state.Coins;
        state.ResearchPoints = 1_000_000;
        return state;
    }

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("기술 그래프 검사 실패: " + name);
        passed++;
        Console.WriteLine("✓ " + name);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Riverworks;

public static class CityFactoryChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        PhysicalFactoryCompletesIndustrialMilestone();
        ExportedImportsDoNotCompleteIndustrialMilestone();
        UnexportedFactoryOutputDoesNotCompleteIndustrialMilestone();
        FactoryResearchGatesMatchTechnologyCatalog();
        VersionThreeMigrationPreservesFactoryData();
        VersionTwoMigrationCreatesAnEmptyFactory();
        return passed;
    }

    static void PhysicalFactoryCompletesIndustrialMilestone()
    {
        GameState state = IndustrialEvaluatorFixture();
        state.Factory=FactoryState.CreateExample();state.TotalToolsProduced=0;
        var physical=new FactorySimulation(state.Factory);
        for(int i=0;i<6000 && state.Factory.Exported[(int)Resource.Tools]<8;i++)
        {
            int before=state.Factory.Produced[(int)Resource.Tools];physical.Tick(.1f);
            state.TotalToolsProduced+=state.Factory.Produced[(int)Resource.Tools]-before;
            physical.TakeExports(Resource.Tools);
        }

        new Simulation(state).Tick();

        True(state.Milestone == 4,
            "실제 공장 Tick 생산·반출을 거쳐 산업 도시 이정표 완료");
    }

    static void ExportedImportsDoNotCompleteIndustrialMilestone()
    {
        GameState state = IndustrialEvaluatorFixture();
        state.Factory.Exported[(int)Resource.Tools] = 8;

        new Simulation(state).Tick();

        True(state.Milestone == 3,
            "격리된 이정표 평가: 생산 이력 없는 수입 도구 재반출은 산업 도시로 인정하지 않음");
    }

    static void UnexportedFactoryOutputDoesNotCompleteIndustrialMilestone()
    {
        GameState state = IndustrialEvaluatorFixture();
        RecordPhysicalToolsChain(state.Factory, exportedTools: 0);

        new Simulation(state).Tick();

        True(state.Milestone == 3,
            "격리된 이정표 평가: 반출하지 않고 공장에 쌓아 둔 도구는 산업 도시를 완료하지 않음");
    }

    static void FactoryResearchGatesMatchTechnologyCatalog()
    {
        var expected = new Dictionary<FactoryKind, TechId>
        {
            [FactoryKind.Belt] = TechId.MechanicalPower,
            [FactoryKind.Inserter] = TechId.MechanicalPower,
            [FactoryKind.Drill] = TechId.Metallurgy,
            [FactoryKind.Furnace] = TechId.Metallurgy,
            [FactoryKind.Assembler] = TechId.Toolmaking,
            [FactoryKind.Storage] = TechId.None,
            [FactoryKind.ImportDock] = TechId.Guilds,
            [FactoryKind.ExportDock] = TechId.Guilds,
            [FactoryKind.PowerInlet] = TechId.SteamPower,
            [FactoryKind.Pole] = TechId.SteamPower,
            [FactoryKind.Splitter] = TechId.MechanicalPower
        };

        List<FactorySpec> specs = FactoryCatalog.All.ToList();
        True(specs.Count == expected.Count && specs.Select(spec => spec.Kind).Distinct().Count() == expected.Count,
            "공장 설비마다 연구 관문이 하나씩 정의됨");
        True(specs.All(spec => expected.TryGetValue(spec.Kind, out TechId required) && spec.RequiredTech == required),
            "실제 공장 설비의 초기·중기 연구 관문이 카탈로그 계약과 일치");
        True(specs.Where(spec => spec.RequiredTech != TechId.None).All(spec =>
        {
            TechSpec technology = TechCatalog.Get(spec.RequiredTech);
            return technology != null && technology.Prerequisites.All(prerequisite => TechCatalog.Get(prerequisite) != null);
        }), "공장 필수 기술과 그 선행 기술이 기술 카탈로그에 모두 존재");

        GameState fresh = GameState.CreateNew();
        True(TechCatalog.Has(fresh, FactoryCatalog.Get(FactoryKind.Storage).RequiredTech) &&
             specs.Where(spec => spec.Kind != FactoryKind.Storage).All(spec => !TechCatalog.Has(fresh, spec.RequiredTech)),
            "새 도시에서는 기본 창고만 연구 없이 사용 가능");

        foreach (TechId required in specs.Select(spec => spec.RequiredTech).Where(id => id != TechId.None).Distinct())
        {
            GameState state = GameState.CreateNew();
            GrantPrerequisites(state, required);
            state.Coins = 100000;
            state.ResearchPoints = 100000;
            var simulation = new Simulation(state);

            True(!TechCatalog.Has(state, required) && simulation.CanResearch(required, out _),
                $"{TechCatalog.Get(required).Name}: 선행 연구 완료 뒤 연구 가능하지만 아직 해금되지 않음");
            state.Technologies.Add(required);
            True(TechCatalog.Has(state, required),
                $"{TechCatalog.Get(required).Name}: 필수 기술을 실제 보유한 뒤 공장 관문 통과");
        }
    }

    static void VersionThreeMigrationPreservesFactoryData()
    {
        GameState state = GameState.CreateNew();
        FactoryState factory = FactoryState.CreateEmpty();
        factory.Produced[(int)Resource.Ore] = 4;
        factory.Produced[(int)Resource.Steel] = 2;
        factory.Produced[(int)Resource.Tools] = 1;
        factory.Exported[(int)Resource.Tools] = 1;
        factory.ElapsedSeconds = 42.5f;
        state.Version = 3;
        state.Factory = factory;

        new Simulation(state);

        True(state.Version == 4 && ReferenceEquals(state.ArchivedFactory, factory) && !ReferenceEquals(state.Factory, factory) &&
             state.Factory.Width == 42 && state.Factory.Height == 42 &&
             state.Factory.Produced[(int)Resource.Ore] == 4 &&
             state.Factory.Produced[(int)Resource.Steel] == 2 &&
             state.Factory.Produced[(int)Resource.Tools] == 1 &&
             state.Factory.Exported[(int)Resource.Tools] == 1 &&
             Math.Abs(state.Factory.ElapsedSeconds - 42.5f) < .001f,
            "v3 공장 통계를 보존하고 기존 내부를 보관한 뒤 공유 도시 v4로 이동");
    }

    static void VersionTwoMigrationCreatesAnEmptyFactory()
    {
        GameState state = GameState.CreateNew();
        FactoryState obsoleteFactory = FactoryState.CreateEmpty();
        obsoleteFactory.Produced[(int)Resource.Tools] = 8;
        obsoleteFactory.Exported[(int)Resource.Tools] = 8;
        obsoleteFactory.ElapsedSeconds = 99;
        state.Version = 2;
        state.Factory = obsoleteFactory;

        new Simulation(state);

        True(state.Version == 4 && state.Factory != null && !ReferenceEquals(state.Factory, obsoleteFactory) &&
             state.Factory.Width == 42 && state.Factory.Height == 42 && state.ArchivedFactory != null &&
             state.Factory.Entities.Count == 0 && state.Factory.Produced.All(value => value == 0) &&
             state.Factory.Exported.All(value => value == 0) && state.Factory.Recovered.All(value => value == 0) &&
             Math.Abs(state.Factory.ElapsedSeconds) < .001f,
            "v2 저장은 기존 단계 마이그레이션 후 빈 공유 도시 v4 공장으로 이동");
    }

    static GameState IndustrialEvaluatorFixture()
    {
        GameState state = GameState.CreateNew();
        foreach (Cell house in state.Cells.Where(cell => cell.Building == BuildingKind.House)) house.Level = 3;
        state.Milestone = 3;
        state.Population = 30;
        state.TotalToolsProduced = 8;
        state.Factory = FactoryState.CreateEmpty();
        return state;
    }

    static void RecordPhysicalToolsChain(FactoryState factory, int exportedTools)
    {
        factory.Produced[(int)Resource.Ore] = 1;
        factory.Produced[(int)Resource.Steel] = 1;
        factory.Produced[(int)Resource.Tools] = 8;
        factory.Exported[(int)Resource.Tools] = exportedTools;
    }

    static void GrantPrerequisites(GameState state, TechId technology)
    {
        foreach (TechId prerequisite in TechCatalog.Get(technology).Prerequisites)
        {
            GrantPrerequisites(state, prerequisite);
            if (!state.Technologies.Contains(prerequisite)) state.Technologies.Add(prerequisite);
        }
    }

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("도시-공장 검사 실패: " + name);
        passed++;
        Console.WriteLine("✓ " + name);
    }
}

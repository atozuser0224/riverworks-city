using Riverworks;

public static class ResearchGraphChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        CatalogGraphHasEveryExpectedTechnologyAndEdge();
        GuildsRequiresBothBranches();
        AutomationPlanMergesSharedAncestorsOnce();
        CompletedTechnologiesAreSkippedAndCostsMatch();
        InvalidGraphsAreRejectedAtConstruction();
        return passed;
    }

    static void CatalogGraphHasEveryExpectedTechnologyAndEdge()
    {
        var graph = new ResearchGraph(TechCatalog.All);
        TechId[] sourceOrder = TechCatalog.All.Select(spec => spec.Id).ToArray();
        var expectedEdges = new[]
        {
            Edge(TechId.CropRotation, TechId.MechanicalPower),
            Edge(TechId.Stonecraft, TechId.Guilds),
            Edge(TechId.MechanicalPower, TechId.Guilds),
            Edge(TechId.Guilds, TechId.Metallurgy),
            Edge(TechId.Guilds, TechId.Scholarship),
            Edge(TechId.Metallurgy, TechId.Toolmaking),
            Edge(TechId.Scholarship, TechId.SteamPower),
            Edge(TechId.Toolmaking, TechId.SteamPower),
            Edge(TechId.SteamPower, TechId.UrbanPlanning),
            Edge(TechId.CropRotation, TechId.Forestry),
            Edge(TechId.CropRotation, TechId.Irrigation),
            Edge(TechId.Stonecraft, TechId.Masonry),
            Edge(TechId.Guilds, TechId.Logistics),
            Edge(TechId.Scholarship, TechId.Education),
            Edge(TechId.Metallurgy, TechId.MetallurgicalEfficiency),
            Edge(TechId.SteamPower, TechId.Electrification),
            Edge(TechId.Toolmaking, TechId.MassProduction),
            Edge(TechId.Electrification, TechId.Automation),
            Edge(TechId.MassProduction, TechId.Automation),
            Edge(TechId.SteamPower, TechId.FluidHandling), Edge(TechId.Logistics, TechId.FluidHandling),
            Edge(TechId.FluidHandling, TechId.OilRefining), Edge(TechId.MetallurgicalEfficiency, TechId.OilRefining),
            Edge(TechId.OilRefining, TechId.Petrochemistry), Edge(TechId.Petrochemistry, TechId.Electronics), Edge(TechId.Toolmaking, TechId.Electronics),
            Edge(TechId.FluidHandling, TechId.AluminumProcessing), Edge(TechId.MetallurgicalEfficiency, TechId.AluminumProcessing),
            Edge(TechId.AluminumProcessing, TechId.EnergyStorage), Edge(TechId.Petrochemistry, TechId.EnergyStorage),
            Edge(TechId.Electronics, TechId.AdvancedManufacturing), Edge(TechId.MassProduction, TechId.AdvancedManufacturing),
            Edge(TechId.AdvancedManufacturing, TechId.IndustrialControl), Edge(TechId.EnergyStorage, TechId.IndustrialControl), Edge(TechId.Automation, TechId.IndustrialControl)
        };

        True(graph.OrderedTechnologies.Select(spec => spec.Id).SequenceEqual(sourceOrder), "26개 기술의 안정적 위상 순서가 카탈로그 순서를 보존");
        True(graph.Edges.Count == 35 && expectedEdges.All(graph.Edges.Contains), "26개 실제 카탈로그의 선행 간선 35개를 빠짐없이 제공");
        True(graph.Depth(TechId.CropRotation) == 0 && graph.Depth(TechId.Automation) == 7, "루트와 자동화의 최장 선행 깊이를 계산");
    }

    static void GuildsRequiresBothBranches()
    {
        var graph = new ResearchGraph(TechCatalog.All);
        True(graph.Ancestors(TechId.Guilds).SequenceEqual(new[] { TechId.CropRotation, TechId.Stonecraft, TechId.MechanicalPower }),
            "길드의 석공술 분기와 윤작-기계 동력 분기를 모두 탐색");
        True(graph.DirectSuccessors(TechId.Guilds).SequenceEqual(new[] { TechId.Metallurgy, TechId.Scholarship, TechId.Logistics }),
            "길드에서 직접 이어지는 세 기술을 안정적 순서로 제공");
        True(graph.Descendants(TechId.Guilds).Contains(TechId.Automation), "길드의 간접 후속 기술에 자동화를 포함");
    }

    static void AutomationPlanMergesSharedAncestorsOnce()
    {
        var graph = new ResearchGraph(TechCatalog.All);
        IReadOnlyList<TechId> plan = graph.PlanTo(TechId.Automation, Array.Empty<TechId>());
        TechId[] expected =
        {
            TechId.CropRotation, TechId.Stonecraft, TechId.MechanicalPower, TechId.Guilds,
            TechId.Metallurgy, TechId.Scholarship, TechId.Toolmaking, TechId.SteamPower,
            TechId.Electrification, TechId.MassProduction, TechId.Automation
        };

        True(plan.SequenceEqual(expected), "자동화까지 필요한 두 산업 분기를 위상 순서로 계획");
        True(plan.Count == plan.Distinct().Count() && plan.Count(id => id == TechId.Guilds) == 1,
            "공유 조상 길드를 포함한 모든 기술이 계획에 한 번만 등장");
    }

    static void CompletedTechnologiesAreSkippedAndCostsMatch()
    {
        var graph = new ResearchGraph(TechCatalog.All);
        TechId[] completed = { TechId.CropRotation, TechId.Stonecraft, TechId.MechanicalPower, TechId.Guilds };
        ResearchPlanSummary summary = graph.PlanSummaryTo(TechId.Automation, completed);

        True(summary.Technologies.SequenceEqual(new[]
        {
            TechId.Metallurgy, TechId.Scholarship, TechId.Toolmaking, TechId.SteamPower,
            TechId.Electrification, TechId.MassProduction, TechId.Automation
        }), "이미 완료한 길드 이전 기술은 자동화 계획에서 제외");
        True(summary.KnowledgeCost == 97, "남은 자동화 계획 연구 비용 합계는 97");
        True(summary.CoinCost == 1110, "남은 자동화 계획 코인 비용 합계는 1110");
        True(summary.DurationDays == 30, "남은 자동화 계획 연구 기간 합계는 30일");
    }

    static void InvalidGraphsAreRejectedAtConstruction()
    {
        Throws(() => new ResearchGraph(Array.Empty<TechSpec>()), "빈 그래프 거부");
        Throws(() => new ResearchGraph(new TechSpec[] { null }), "null 기술 거부");
        Throws(() => new ResearchGraph(new[] { Spec(TechId.CropRotation), Spec(TechId.CropRotation) }), "중복 기술 ID 거부");
        Throws(() => new ResearchGraph(new[] { Spec(TechId.CropRotation, (TechId)999) }), "알 수 없는 선행 기술 거부");
        Throws(() => new ResearchGraph(new[] { Spec(TechId.CropRotation, TechId.Stonecraft) }), "그래프에 없는 선행 기술 거부");
        Throws(() => new ResearchGraph(new[] { Spec(TechId.CropRotation, TechId.CropRotation) }), "자기 자신을 잇는 간선 거부");
        Throws(() => new ResearchGraph(new[] { Spec(TechId.CropRotation), Spec(TechId.Stonecraft, TechId.CropRotation, TechId.CropRotation) }), "중복 간선 거부");
        Throws(() => new ResearchGraph(new[] { Spec(TechId.CropRotation, TechId.Stonecraft), Spec(TechId.Stonecraft, TechId.CropRotation) }), "순환 그래프 거부");
    }

    static ResearchEdge Edge(TechId source, TechId target) => new ResearchEdge(source, target);

    static TechSpec Spec(TechId id, params TechId[] prerequisites) => new TechSpec
    {
        Id = id,
        Prerequisites = prerequisites,
        UnlockBuildings = Array.Empty<BuildingKind>()
    };

    static void Throws(Action action, string name)
    {
        try
        {
            action();
            throw new Exception("예외가 발생하지 않음");
        }
        catch (ArgumentException)
        {
            passed++;
            Console.WriteLine("✓ " + name);
        }
    }

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("연구 그래프 검사 실패: " + name);
        passed++;
        Console.WriteLine("✓ " + name);
    }
}

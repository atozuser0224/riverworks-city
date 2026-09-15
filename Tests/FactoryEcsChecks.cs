using System;
using Riverworks;

public static class FactoryEcsChecks
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("공장 ECS 검증 실패: " + name); passed++; Console.WriteLine("✓ " + name); }

    public static int Run()
    {
        passed = 0;
        PowerGraphPowersMachines(); PowerGraphStopsWithoutSource(); BridgeMirrorsEntityColumn(); MachineBridgeRunsRecipe(); AutomationMatchesLegacyEvaluation();
        Console.WriteLine($"공장 ECS {passed}개 검증 통과"); return passed;
    }

    static void PowerGraphPowersMachines()
    {
        var state = FactoryState.CreateEmpty();
        var sim = new FactorySimulation(state);
        True(sim.TryPlace(FactoryKind.PowerInlet, 6, 2, 0, out string inletReason), "전력 인입구 배치: " + inletReason);
        True(sim.TryPlace(FactoryKind.Furnace, 8, 7, 0, out string furnaceReason), "용광로 배치: " + furnaceReason);
        FactoryEntity furnace = sim.GetAt(8, 7);
        True(sim.SetRecipe(furnace.Id, FactoryRecipe.IronPlate, out string recipeReason), "용광로 제조법 선택: " + recipeReason);
        sim.Recalculate();
        True(furnace.Powered, "전력망 안의 용광로가 전력을 공급받음");
        True(Math.Abs(sim.PowerUsed - 3f) < .001f, "ECS 전력 그래프가 수요 3을 집계: " + sim.PowerUsed);
        True(Math.Abs(sim.PowerAvailable - state.PowerBudget) < .001f, "전력 예산이 가용량으로 보고됨");
    }

    static void PowerGraphStopsWithoutSource()
    {
        var state = FactoryState.CreateEmpty();
        var sim = new FactorySimulation(state);
        True(sim.TryPlace(FactoryKind.Furnace, 8, 7, 0, out string furnaceReason), "외딴 용광로 배치: " + furnaceReason);
        FactoryEntity furnace = sim.GetAt(8, 7);
        sim.SetRecipe(furnace.Id, FactoryRecipe.IronPlate, out _);
        sim.Recalculate();
        True(!furnace.Powered, "전력원이 없으면 용광로가 정지");
        True(furnace.Status.Contains("전력"), "정지 사유가 전력 상태로 표시: " + furnace.Status);
    }

    static void BridgeMirrorsEntityColumn()
    {
        var state = FactoryState.CreateEmpty();
        var sim = new FactorySimulation(state);
        sim.TryPlace(FactoryKind.PowerInlet, 6, 2, 0, out _);
        sim.TryPlace(FactoryKind.Pole, 3, 5, 0, out _);
        sim.TryPlace(FactoryKind.Furnace, 8, 7, 0, out _);
        var bridge = new FactoryPowerBridge();
        bridge.Import(state.Entities, state.PowerBudget, 1f, null);
        True(bridge.Nodes.Count == state.Entities.Count, "브리지 컴포넌트 수가 엔티티 수와 일치");
        bridge.Run();
        bridge.Export();
        int live = 0;
        for (int i = 0; i < bridge.Nodes.Count; i++) if (bridge.Nodes.ValueAt(i).IsLive != 0) live++;
        True(live >= 1, "살아있는 전력원이 컴포넌트 컬럼에 기록됨");
        True(bridge.PowerAvailable > 0f, "브리지가 전력 예산을 계산");
    }

    static void MachineBridgeRunsRecipe()
    {
        FactoryState state = FactoryState.CreateExample();
        var sim = new FactorySimulation(state);
        FactoryEntity furnace = sim.GetAt(8, 7);
        furnace.Input[(int)Resource.Ore] = 20;
        float before = state.Produced[(int)Resource.Steel];
        sim.Tick(5f);
        True(state.Produced[(int)Resource.Steel] > before, "ECS 제조 시스템이 강철을 생산");
        True(furnace.Status == "생산 완료", "ECS 제조 시스템이 완료 상태를 기록: " + furnace.Status);
        var bridge = new FactoryMachineBridge();
        bridge.Import(state.Entities, state, sim, 1f);
        int machines = 0;
        for (int i = 0; i < bridge.Machines.Count; i++) if (bridge.Machines.ValueAt(i).IsMachine != 0) machines++;
        True(machines > 0 && machines < state.Entities.Count, "제조 컴포넌트가 생산 설비에만 생성됨");
    }

    static void AutomationMatchesLegacyEvaluation()
    {
        FactoryState first = BuildAutomationFactory(out GameState firstGame);
        FactoryState second = BuildAutomationFactory(out GameState secondGame);
        FactoryAutomation.Evaluate(first, firstGame);
        var bridge = new FactoryAutomationBridge();
        bridge.Import(second, secondGame);
        bridge.Run();
        bridge.Export(second);
        bool matches = true;
        string detail = "";
        for (int i = 0; i < first.Entities.Count; i++)
        {
            FactoryEntity expected = first.Entities[i], actual = second.Entities[i];
            matches &= expected.AutomationBlocked == actual.AutomationBlocked;
            matches &= string.Equals(expected.Status, actual.Status, StringComparison.Ordinal);
            if (matches) continue;
            detail = "기존 " + expected.AutomationBlocked + "/" + expected.Status + ", ECS " + actual.AutomationBlocked + "/" + actual.Status;
            break;
        }
        True(matches, "자동화 ECS가 기존 평가와 동일한 차단·상태 결과" + (matches ? "" : " (" + detail + ")"));
    }

    static FactoryState BuildAutomationFactory(out GameState game)
    {
        game = GameState.CreateNew();
        FactoryState factory = game.Factory;
        factory.Entities.Clear();
        factory.AutomationRules.Clear();
        var source = new FactoryEntity { Id = 1, Kind = FactoryKind.Storage, ClockPercent = 100, ControllerInstalled = true };
        source.Input[(int)Resource.Steel] = 10;
        factory.Entities.Add(source);
        var target = new FactoryEntity
        {
            Id = 2, Kind = FactoryKind.Furnace, ClockPercent = 100, ControllerInstalled = true,
            Status = "조건 자동화 대기"
        };
        factory.Entities.Add(target);
        factory.AutomationRules.Add(new AutomationRule
        {
            Id = 1, SourceEntityId = 1, TargetEntityId = 2, Resource = Resource.Steel,
            Comparison = AutomationComparison.AtLeast, Action = AutomationAction.StopWhenTrue, Threshold = 5, Enabled = true
        });
        factory.AutomationRules.Add(new AutomationRule
        {
            Id = 2, SourceEntityId = 0, TargetEntityId = 2, Resource = Resource.Steel,
            Comparison = AutomationComparison.AtLeast, Action = AutomationAction.AllowWhenTrue, Threshold = 100, Enabled = true
        });
        return factory;
    }
}

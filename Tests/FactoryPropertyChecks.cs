using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Riverworks;

public static class FactoryPropertyChecks
{
    const int ExampleTimber = 80;
    static int passed;

    public static int Run()
    {
        passed = 0;
        LongRunningExampleConservesIngredients();
        PowerCutsPauseProductionWithoutLosingMatter();
        RemovalReturnsEveryRemainingItem();
        OpposingCyclesConserveTheirCargo();
        RejectedSeededOperationsAreAtomic();
        InvalidStatesAreRejectedWithoutNormalization();
        TechnologyConfigurationsPreserveMatter();
        return passed;
    }

    static void LongRunningExampleConservesIngredients()
    {
        FactoryState state = FactoryState.CreateExample();
        var sim = new FactorySimulation(state);
        var random = new Random(0x51A7);

        for (int step = 0; step < 30000; step++)
        {
            if (step % 997 == 0) state.PowerBudget = 0;
            if (step % 997 == 149) state.PowerBudget = 20;
            if (step % 43 == 0) sim.TakeExports(Resource.Tools, random.Next(1, 5));
            sim.Tick((float)(0.02 + random.NextDouble() * 0.23));
            if (step % 31 == 0) AssertExampleBalance(state, "장시간 가동 " + step);
        }

        state.PowerBudget = 20;
        for (int i = 0; i < 2000; i++) sim.Tick(.1f);
        AssertExampleBalance(state, "장시간 가동 종료");
        True(state.Produced[(int)Resource.Ore] > 0, "장시간 가동이 실제 광석을 생성");
        True(state.Produced[(int)Resource.Tools] > 0, "장시간 가동이 실제 도구를 생산");
        True(state.Exported[(int)Resource.Tools] > 0, "장시간 가동 중 반출된 도구도 보존식에 포함");
    }

    static void PowerCutsPauseProductionWithoutLosingMatter()
    {
        FactoryState state = FactoryState.CreateExample();
        var sim = new FactorySimulation(state);
        for (int i = 0; i < 2500; i++) sim.Tick(.1f);
        AssertExampleBalance(state, "정전 직전");

        int[] producedBefore = state.Produced.ToArray();
        state.PowerBudget = 0;
        for (int i = 0; i < 3000; i++)
        {
            sim.Tick(.1f);
            if (i % 37 == 0) AssertExampleBalance(state, "정전 " + i);
        }

        True(state.Produced.SequenceEqual(producedBefore), "정전 동안 채굴과 제조 누적량 정지");
        True(sim.PowerUsed == 0, "정전 동안 전력 소비 없음");

        int toolsBefore = state.Produced[(int)Resource.Tools];
        state.PowerBudget = 20;
        for (int i = 0; i < 5000 && state.Produced[(int)Resource.Tools] == toolsBefore; i++) sim.Tick(.1f);
        AssertExampleBalance(state, "전력 복구 후");
        True(state.Produced[(int)Resource.Tools] > toolsBefore, "전력 복구 후 생산 재개");
    }

    static void RemovalReturnsEveryRemainingItem()
    {
        FactoryState state = FactoryState.CreateExample();
        var sim = new FactorySimulation(state);
        for (int i = 0; i < 7000; i++)
        {
            if (i % 131 == 0) sim.TakeExports(Resource.Tools, 2);
            sim.Tick(.1f);
        }
        AssertExampleBalance(state, "철거 전");

        var positions = state.Entities.Select(e => new[] { e.X, e.Z }).ToList();
        Shuffle(positions, new Random(90210));
        foreach (int[] position in positions)
        {
            Require(sim.Remove(position[0], position[1], out string reason), "철거 실패: " + reason);
            AssertExampleBalance(state, $"철거 ({position[0]},{position[1]})");
            FactorySimulation.ValidateState(state);
        }

        True(state.Entities.Count == 0, "무작위 순서로 모든 설비 철거");
        True(OreAtoms(state) == state.Produced[(int)Resource.Ore], "철거 후 생성 광석 전량 회수 또는 반출");
        True(TimberAtoms(state) == ExampleTimber, "철거 후 초기 목재 전량 회수 또는 반출");
    }

    static void OpposingCyclesConserveTheirCargo()
    {
        FactoryState state = FactoryState.CreateEmpty();
        var sim = new FactorySimulation(state);

        Place(sim, FactoryKind.Belt, 3, 3, 0); Place(sim, FactoryKind.Belt, 4, 3, 1);
        Place(sim, FactoryKind.Belt, 4, 4, 2); Place(sim, FactoryKind.Belt, 3, 4, 3);
        Place(sim, FactoryKind.Belt, 8, 3, 1); Place(sim, FactoryKind.Belt, 8, 4, 0);
        Place(sim, FactoryKind.Belt, 9, 4, 3); Place(sim, FactoryKind.Belt, 9, 3, 2);
        Place(sim, FactoryKind.Belt, 13, 3, 0); Place(sim, FactoryKind.Belt, 14, 3, 2);

        sim.GetAt(3, 3).CargoResource = Resource.Ore;
        sim.GetAt(8, 3).CargoResource = Resource.Tools;
        sim.GetAt(13, 3).CargoResource = Resource.Timber;
        sim.GetAt(14, 3).CargoResource = Resource.Steel;
        var expected = CargoCounts(state);
        var random = new Random(7719);

        for (int step = 0; step < 40000; step++)
        {
            sim.Tick((float)(0.01 + random.NextDouble() * .79));
            if (step % 47 == 0) AssertCargoCounts(state, expected, "상반 순환 " + step);
        }
        True(CargoCounts(state).SequenceEqual(expected), "시계·반시계 순환과 마주 보는 벨트의 장기 보존");

        foreach (var position in state.Entities.Select(e => new[] { e.X, e.Z }).ToList())
            Require(sim.Remove(position[0], position[1], out string reason), "순환 벨트 철거 실패: " + reason);
        AssertCargoCounts(state, expected, "순환 벨트 철거 후");
        True(state.Recovered.Sum() == expected.Sum(), "순환망 철거 시 운송 화물 전량 회수");
    }

    static void RejectedSeededOperationsAreAtomic()
    {
        string first = RunRejectedSequence(421337);
        string second = RunRejectedSequence(421337);
        True(first == second, "고정 시드의 거부 연산 결과 결정적");
        True(first == Fingerprint(FactoryState.CreateExample()), "거부된 연산이 유효 상태를 변경하지 않음");
    }

    static string RunRejectedSequence(int seed)
    {
        FactoryState state = FactoryState.CreateExample();
        var sim = new FactorySimulation(state);
        var random = new Random(seed);
        FactoryEntity drill = state.Entities.First(e => e.Kind == FactoryKind.Drill);
        FactoryEntity furnace = state.Entities.First(e => e.Kind == FactoryKind.Furnace);
        FactoryEntity inserter = state.Entities.First(e => e.Kind == FactoryKind.Inserter);

        for (int step = 0; step < 1200; step++)
        {
            string before = Fingerprint(state);
            bool rejected;
            switch (random.Next(14))
            {
                case 0: rejected = !sim.TryPlace(FactoryKind.None, random.Next(24), random.Next(16), random.Next(4), out _); break;
                case 1: rejected = !sim.TryPlace(FactoryKind.Belt, -1, random.Next(16), random.Next(4), out _); break;
                case 2: rejected = !sim.TryPlace(FactoryKind.Belt, drill.X, drill.Z, random.Next(4), out _); break;
                case 3: rejected = !sim.AddInput(int.MaxValue, Resource.Timber, 1, out _); break;
                case 4: rejected = !sim.AddInput(drill.Id, Resource.Timber, 1, out _); break;
                case 5: rejected = !sim.AddInput(state.Entities.First(e => e.Kind == FactoryKind.ImportDock).Id, Resource.Timber, 0, out _); break;
                case 6: rejected = !sim.SetRecipe(furnace.Id, FactoryRecipe.Tools, out _); break;
                case 7: rejected = !sim.SetRecipe(int.MaxValue, FactoryRecipe.IronPlate, out _); break;
                case 8: rejected = !sim.SetFilter(drill.Id, Resource.Ore, out _); break;
                case 9: rejected = !sim.SetFilter(inserter.Id, (Resource)999, out _); break;
                case 10: rejected = !sim.Rotate(int.MaxValue, out _); break;
                case 11: rejected = !sim.Remove(0, 15, out _); break;
                case 12: sim.Tick(random.Next(2) == 0 ? float.NaN : float.PositiveInfinity); rejected = true; break;
                default: rejected = sim.TakeExports(Resource.Coins, random.Next(1, 10)) == 0; break;
            }
            Require(rejected, "고정 시드 연산이 거부되지 않음: " + step);
            Require(before == Fingerprint(state), "거부 연산이 상태를 변경함: " + step);
        }
        return Fingerprint(state);
    }

    static void InvalidStatesAreRejectedWithoutNormalization()
    {
        var random = new Random(0xBAD5EED);
        for (int i = 0; i < 200; i++)
        {
            FactoryState state = FactoryState.CreateEmpty();
            switch (random.Next(7))
            {
                case 0: state.Version = 999; break;
                case 1: state.Width = 0; break;
                case 2: state.Height = 257; break;
                case 3: state.NextEntityId = 0; break;
                case 4: state.PowerBudget = -1; break;
                case 5: state.ElapsedSeconds = float.NaN; break;
                default: state.Produced[0] = 1; break;
            }
            string before = Fingerprint(state);
            bool rejected = false;
            try { _ = new FactorySimulation(state); }
            catch (System.IO.InvalidDataException) { rejected = true; }
            Require(rejected, "손상 상태를 생성자가 거부하지 않음: " + i);
            Require(before == Fingerprint(state), "손상 상태가 거부 과정에서 정규화됨: " + i);
        }
        True(true, "고정 시드 손상 상태를 변경 없이 거부");
    }

    static void TechnologyConfigurationsPreserveMatter()
    {
        FactoryState baselineState = FactoryState.CreateExample();
        var baseline = new FactorySimulation(baselineState);
        baseline.ConfigureTechnology(null);
        AssertExampleBalance(baselineState, "기술 미적용 구성 직후");
        True(Approximately(baseline.BeltSpeedMultiplier, 1f)
            && Approximately(baseline.InserterSpeedMultiplier, 1f)
            && Approximately(baseline.MachineSpeedMultiplier, 1f)
            && Approximately(baseline.PowerDemandMultiplier, 1f), "기술 미적용 배율은 기준값");

        GameState upgradedGame = GameState.CreateNew();
        upgradedGame.Technologies.Clear();
        upgradedGame.Technologies.AddRange(new[]
        {
            TechId.Logistics,
            TechId.Automation,
            TechId.MassProduction,
            TechId.Electrification
        });

        FactoryState upgradedState = FactoryState.CreateExample();
        var upgraded = new FactorySimulation(upgradedState);
        upgraded.ConfigureTechnology(upgradedGame);
        AssertExampleBalance(upgradedState, "기술 적용 구성 직후");
        True(upgraded.BeltSpeedMultiplier > 1f, "물류 기술이 벨트 처리량을 향상");
        True(upgraded.InserterSpeedMultiplier > 1f, "자동화 기술이 투입기 처리량을 향상");
        True(upgraded.MachineSpeedMultiplier > 1f, "대량생산 기술이 제조 처리량을 향상");
        True(upgraded.PowerDemandMultiplier > 0f && upgraded.PowerDemandMultiplier < 1f, "전기화 기술이 전력 효율을 향상");

        RunConfiguredFactory(baseline, baselineState, null, 8841);
        RunConfiguredFactory(upgraded, upgradedState, upgradedGame, 8841);
        True(baselineState.Produced[(int)Resource.Tools] > 0 && upgradedState.Produced[(int)Resource.Tools] > 0,
            "기준 및 전체 기술 공장이 실제 생산하면서 보존식 유지");
    }

    static void RunConfiguredFactory(FactorySimulation sim, FactoryState state, GameState technology, int seed)
    {
        var random = new Random(seed);
        for (int step = 0; step < 24000; step++)
        {
            if (step % 211 == 0)
            {
                int oreBefore = OreAtoms(state), timberBefore = TimberAtoms(state);
                sim.ConfigureTechnology(technology);
                Require(oreBefore == OreAtoms(state) && timberBefore == TimberAtoms(state), "기술 재구성이 물자를 변경함: " + step);
            }
            if (step % 79 == 0) sim.TakeExports(Resource.Tools, random.Next(1, 4));
            sim.Tick((float)(.03 + random.NextDouble() * .17));
            if (step % 41 == 0) AssertExampleBalance(state, "기술 구성 가동 " + step);
        }
        AssertExampleBalance(state, "기술 구성 가동 종료");
    }

    static void AssertExampleBalance(FactoryState state, string context)
    {
        int ore = OreAtoms(state), generated = state.Produced[(int)Resource.Ore];
        int timber = TimberAtoms(state);
        Require(ore == generated, $"{context}: 광석 원자 {ore}, 생성 광석 {generated}");
        Require(timber == ExampleTimber, $"{context}: 목재 원자 {timber}, 초기 목재 {ExampleTimber}");
        Require(state.Entities.All(e => e.Input[0] == 0 && e.Output[0] == 0), context + ": Coins 센티널 버퍼 오염");
    }

    static int OreAtoms(FactoryState state) => PhysicalCount(state, Resource.Ore)
        + 2 * PhysicalCount(state, Resource.Steel)
        + 2 * PhysicalCount(state, Resource.Tools);

    static int TimberAtoms(FactoryState state) => PhysicalCount(state, Resource.Timber)
        + PhysicalCount(state, Resource.Tools);

    static int PhysicalCount(FactoryState state, Resource resource)
    {
        int index = (int)resource;
        return state.Entities.Sum(e => e.Input[index] + e.Output[index] + (e.CargoResource == resource ? 1 : 0))
            + state.Recovered[index] + state.Exported[index];
    }

    static int[] CargoCounts(FactoryState state)
    {
        var counts = new int[ResourceCatalog.Count];
        for (int i = 1; i < counts.Length; i++) counts[i] = PhysicalCount(state, (Resource)i);
        return counts;
    }

    static void AssertCargoCounts(FactoryState state, int[] expected, string context)
    {
        int[] actual = CargoCounts(state);
        for (int i = 1; i < expected.Length; i++)
            Require(actual[i] == expected[i], $"{context}: {(Resource)i} 예상 {expected[i]}, 실제 {actual[i]}");
    }

    static void Place(FactorySimulation sim, FactoryKind kind, int x, int z, int direction)
    {
        Require(sim.TryPlace(kind, x, z, direction, out string reason), $"{kind} ({x},{z}) 배치 실패: {reason}");
    }

    static bool Approximately(float actual, float expected) => Math.Abs(actual - expected) < .0001f;

    static void Shuffle<T>(IList<T> values, Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            T value = values[i]; values[i] = values[j]; values[j] = value;
        }
    }

    static string Fingerprint(FactoryState state)
    {
        var text = new StringBuilder();
        text.Append(state.Version).Append('|').Append(state.Width).Append('|').Append(state.Height).Append('|')
            .Append(state.NextEntityId).Append('|').Append(state.PowerBudget).Append('|')
            .Append(state.ElapsedSeconds.ToString("R", CultureInfo.InvariantCulture));
        Append(text, state.Produced); Append(text, state.Exported); Append(text, state.Recovered);
        foreach (FactoryEntity entity in state.Entities)
        {
            text.Append(";E:").Append(entity.Id).Append(',').Append((int)entity.Kind).Append(',')
                .Append(entity.X).Append(',').Append(entity.Z).Append(',').Append(entity.Direction).Append(',')
                .Append((int)entity.Recipe).Append(',').Append((int)entity.Filter).Append(',')
                .Append((int)entity.CargoResource).Append(',')
                .Append(entity.CargoProgress.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(entity.Progress.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(entity.Powered ? 1 : 0).Append(',').Append(entity.SplitLeft ? 1 : 0).Append(',')
                .Append(entity.Status ?? "<null>");
            Append(text, entity.Input); Append(text, entity.Output);
        }
        return text.ToString();
    }

    static void Append(StringBuilder text, IEnumerable<int> values)
    {
        text.Append('[').Append(string.Join(",", values)).Append(']');
    }

    static void Require(bool value, string message)
    {
        if (!value) throw new Exception("공장 속성 검사 실패: " + message);
    }

    static void True(bool value, string name)
    {
        Require(value, name);
        passed++;
        Console.WriteLine("✓ " + name);
    }
}

using System;
using System.Linq;
using Riverworks;

public static class CitizenLifecycleChecks
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("시민 생애 검증 실패: " + name); passed++; Console.WriteLine("✓ " + name); }

    struct TestNumber { public int Value; }

    public static int Run()
    {
        passed = 0;
        EcsKernelKeepsHandlesStable(); EcsComponentStoreStaysDense();
        NewGameSeedsLifeRecords(); DirectPopulationChangesReconcile(); BirthsStartAsChildrenAndGrowUp();
        WorkersRetireAndEldersPassAway(); StarvationRemovesAWorker(); AdvanceIsDeterministic(); SimulationKeepsRecordsInSync();
        Console.WriteLine($"시민 생애 ECS {passed}개 검증 통과"); return passed;
    }

    static void EcsKernelKeepsHandlesStable()
    {
        var world = new EcsWorld();
        Entity first = world.Create();
        world.Destroy(first);
        Entity second = world.Create();
        True(!world.IsAlive(first) && world.IsAlive(second), "회수된 엔티티 핸들은 이전 버전을 가리키지 않음");
        True(first.Index == second.Index && first.Version != second.Version, "재사용 슬롯은 버전을 증가시킴");
        world.DeferDestroy(second);
        True(world.IsAlive(second), "지연 파괴는 틱 종료 전까지 엔티티를 유지");
        world.FlushStructuralChanges();
        True(!world.IsAlive(second), "지연 파괴는 플러시 후 실제로 제거");
    }

    static void EcsComponentStoreStaysDense()
    {
        var world = new EcsWorld();
        ComponentStore<TestNumber> store = world.Store<TestNumber>();
        Entity a = world.Create(), b = world.Create(), c = world.Create();
        store.Set(a.Index, new TestNumber { Value = 11 });
        store.Set(b.Index, new TestNumber { Value = 22 });
        store.Set(c.Index, new TestNumber { Value = 33 });
        True(store.Count == 3 && store.TryGet(b.Index, out TestNumber bValue) && bValue.Value == 22, "컴포넌트 추가와 조회");
        store.RemoveAt(b.Index);
        True(store.Count == 2 && !store.Has(b.Index), "컴포넌트 제거");
        bool dense = true;
        for (int i = 0; i < store.Count; i++) dense &= store.Has(store.EntityAt(i));
        True(dense, "스왑 제거 후에도 밀집 배열이 유지됨");
        world.Destroy(a);
        True(!store.Has(a.Index) && store.Count == 1, "엔티티 파괴는 모든 컴포넌트를 함께 제거");
    }

    static void NewGameSeedsLifeRecords()
    {
        var state = GameState.CreateNew();
        new Simulation(state);
        True(CitizenLifecycle.Count(state) == state.Population, "새 도시의 생애 기록이 인구와 일치");
        True(state.Population == 8, "새 도시 인구 보존");
        state.Population = 60;
        CitizenLifecycle.Reconcile(state);
        True(CitizenLifecycle.Workers(state) > 0 && CitizenLifecycle.Dependents(state) > 0, "재구성한 도시에 노동 인구와 부양 인구가 함께 존재");
        CitizenLifecycleState citizens = CitizenLifecycle.Ensure(state);
        True(citizens.Count == 60 && citizens.NextId > citizens.Count, "시민 ID가 기존 ID와 충돌하지 않음");
        True(citizens.Stages.All(stage => Enum.IsDefined(typeof(LifeStage), stage)), "모든 생애 단계가 유효");
    }

    static void DirectPopulationChangesReconcile()
    {
        var state = GameState.CreateNew();
        state.Population = 40;
        CitizenLifecycle.Reconcile(state);
        True(CitizenLifecycle.Count(state) == 40, "직접 늘린 인구만큼 기록 생성");
        state.Population = 12;
        CitizenLifecycle.Reconcile(state);
        True(CitizenLifecycle.Count(state) == 12 && state.Population == 12, "줄어든 인구만큼 기록 정리");
    }

    static void BirthsStartAsChildrenAndGrowUp()
    {
        var state = GameState.CreateNew();
        state.Population = 0;
        CitizenLifecycle.Reconcile(state);
        CitizenLifecycle.RecordBirth(state);
        CitizenLifecycleState citizens = CitizenLifecycle.Ensure(state);
        True(citizens.Count == 1 && citizens.Stages[0] == (int)LifeStage.Child, "출생은 유아 단계로 시작");
        CitizenLifecycle.Advance(state, CitizenLifecycle.YouthAgeDays);
        citizens = CitizenLifecycle.Ensure(state);
        True(citizens.Stages[0] == (int)LifeStage.Youth, "유아는 청소년으로 성장");
        CitizenLifecycle.Advance(state, CitizenLifecycle.WorkerAgeDays - CitizenLifecycle.YouthAgeDays);
        citizens = CitizenLifecycle.Ensure(state);
        True(citizens.Stages[0] == (int)LifeStage.Worker, "청소년은 노동 인구로 성장");
        CitizenLifecycle.Advance(state, CitizenLifecycle.RetirementAgeDays - CitizenLifecycle.WorkerAgeDays);
        citizens = CitizenLifecycle.Ensure(state);
        True(citizens.Stages[0] == (int)LifeStage.Elder && (citizens.Flags[0] & CitizenLifecycle.FlagRetired) != 0, "노동 인구는 정년 후 은퇴");
    }

    static void WorkersRetireAndEldersPassAway()
    {
        var state = GameState.CreateNew();
        state.Population = 6;
        CitizenLifecycle.Reconcile(state);
        int deaths = CitizenLifecycle.Advance(state, CitizenLifecycle.MaximumLifespanDays + CitizenLifecycle.DaysPerYear);
        True(deaths == 6 && CitizenLifecycle.Count(state) == 0, "최대 수명을 넘긴 시민은 모두 사망");
        True(state.Population == 0, "사망 후 인구가 실제 기록과 동기화");
    }

    static void StarvationRemovesAWorker()
    {
        var state = GameState.CreateNew();
        state.Population = 30;
        CitizenLifecycle.Reconcile(state);
        int workers = CitizenLifecycle.Workers(state);
        CitizenLifecycle.RemoveOne(state);
        True(CitizenLifecycle.Count(state) == 29 && state.Population == 29, "기근은 인구를 한 명 줄임");
        True(workers == 0 || CitizenLifecycle.Workers(state) == workers - 1, "기근 사망은 노동 인구를 우선 제거");
    }

    static void AdvanceIsDeterministic()
    {
        var first = GameState.CreateNew(); first.Population = 30;
        var second = GameState.CreateNew(); second.Population = 30;
        CitizenLifecycle.Reconcile(first); CitizenLifecycle.Reconcile(second);
        for (int day = 0; day < 400; day++)
        {
            first.Day = day;
            second.Day = day;
            CitizenLifecycle.Advance(first, 3);
            CitizenLifecycle.Advance(second, 3);
        }
        True(CitizenLifecycle.Count(first) == CitizenLifecycle.Count(second), "같은 도시는 같은 사망 수를 재현");
        CitizenLifecycleState a = CitizenLifecycle.Ensure(first), b = CitizenLifecycle.Ensure(second);
        True(a.AgeDays.SequenceEqual(b.AgeDays) && a.Stages.SequenceEqual(b.Stages), "같은 도시는 같은 나이와 단계를 재현");
    }

    static void SimulationKeepsRecordsInSync()
    {
        var state = GameState.CreateNew();
        var sim = new Simulation(state);
        for (int day = 0; day < 40; day++) sim.Tick();
        True(CitizenLifecycle.Count(state) == state.Population, "시뮬레이션 틱 후에도 인구와 기록이 일치");
        True(CitizenLifecycle.Workers(state) + CitizenLifecycle.Dependents(state) +
             CitizenLifecycle.CountStage(state, LifeStage.Elder) == state.Population, "모든 시민이 하나의 생애 단계에 속함");
    }
}

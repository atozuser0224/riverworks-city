using System;

namespace Riverworks
{
    public static class FactoryBlueprints
    {
        static readonly string[] Names =
        {
            "도구 생산 라인",
            "밀가루와 빵 생산 라인",
            "균등 분배 창고"
        };

        static readonly string[] Descriptions =
        {
            "철광석을 제련하고 목재와 조립해 도구를 반출하는 완성형 예제입니다.",
            "곡물을 밀가루로 가공한 뒤 빵을 만들어 반출하는 2단계 생산 예제입니다.",
            "분배기가 한 종류의 물자를 두 창고에 번갈아 보내는 물류 예제입니다."
        };

        public static int Count => Names.Length;

        public static string Name(int index)
        {
            CheckIndex(index);
            return Names[index];
        }

        public static string Description(int index)
        {
            CheckIndex(index);
            return Descriptions[index];
        }

        public static FactoryState Create(int index)
        {
            CheckIndex(index);
            switch (index)
            {
                case 0: return FactoryState.CreateExample();
                case 1: return CreateBreadLine();
                case 2: return CreateBalancedStorage();
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        static FactoryState CreateBreadLine()
        {
            FactoryState state = FactoryState.CreateEmpty();
            state.PowerBudget = 20;
            var sim = new FactorySimulation(state);

            Place(sim, FactoryKind.ImportDock, 1, 1);
            Place(sim, FactoryKind.Inserter, 3, 2);
            for (int x = 4; x <= 6; x++) Place(sim, FactoryKind.Belt, x, 2);
            Place(sim, FactoryKind.Inserter, 7, 2);
            Place(sim, FactoryKind.Assembler, 8, 1);
            Place(sim, FactoryKind.Inserter, 10, 2);
            for (int x = 11; x <= 13; x++) Place(sim, FactoryKind.Belt, x, 2);
            Place(sim, FactoryKind.Inserter, 14, 2);
            Place(sim, FactoryKind.Assembler, 15, 1);
            Place(sim, FactoryKind.Inserter, 17, 2);
            for (int x = 18; x <= 20; x++) Place(sim, FactoryKind.Belt, x, 2);
            Place(sim, FactoryKind.Inserter, 21, 2);
            Place(sim, FactoryKind.ExportDock, 22, 1);

            Place(sim, FactoryKind.PowerInlet, 7, 6);
            Place(sim, FactoryKind.Pole, 3, 4);
            Place(sim, FactoryKind.Pole, 8, 4);
            Place(sim, FactoryKind.Pole, 13, 4);
            Place(sim, FactoryKind.Pole, 18, 4);
            Place(sim, FactoryKind.Pole, 22, 4);

            SetRecipe(sim, 8, 1, FactoryRecipe.Flour);
            SetRecipe(sim, 15, 1, FactoryRecipe.Bread);
            AddInput(sim, 1, 1, Resource.Grain, 80);
            sim.Recalculate();
            return state;
        }

        static FactoryState CreateBalancedStorage()
        {
            FactoryState state = FactoryState.CreateEmpty();
            state.PowerBudget = 20;
            var sim = new FactorySimulation(state);

            Place(sim, FactoryKind.ImportDock, 1, 1);
            Place(sim, FactoryKind.Inserter, 3, 2);
            Place(sim, FactoryKind.Belt, 4, 2);
            Place(sim, FactoryKind.Belt, 5, 2);
            Place(sim, FactoryKind.Belt, 6, 2);
            Place(sim, FactoryKind.Splitter, 7, 2);
            Place(sim, FactoryKind.Belt, 8, 2);
            Place(sim, FactoryKind.Storage, 9, 2);
            Place(sim, FactoryKind.Belt, 7, 3, 1);
            Place(sim, FactoryKind.Storage, 7, 4);

            Place(sim, FactoryKind.PowerInlet, 1, 6);
            Place(sim, FactoryKind.Pole, 4, 5);

            AddInput(sim, 1, 1, Resource.Grain, 60);
            sim.Recalculate();
            return state;
        }

        static void Place(FactorySimulation sim, FactoryKind kind, int x, int z, int direction = 0)
        {
            if (!sim.TryPlace(kind, x, z, direction, out string reason))
                throw new InvalidOperationException($"블루프린트 설비 배치 실패: {kind} ({x}, {z}): {reason}");
        }

        static void SetRecipe(FactorySimulation sim, int x, int z, FactoryRecipe recipe)
        {
            FactoryEntity entity = sim.GetAt(x, z);
            string reason = "설비를 찾을 수 없습니다.";
            if (entity == null || !sim.SetRecipe(entity.Id, recipe, out reason))
                throw new InvalidOperationException($"블루프린트 제조법 설정 실패: {recipe} ({x}, {z}): {reason}");
        }

        static void AddInput(FactorySimulation sim, int x, int z, Resource resource, int amount)
        {
            FactoryEntity entity = sim.GetAt(x, z);
            string reason = "설비를 찾을 수 없습니다.";
            if (entity == null || !sim.AddInput(entity.Id, resource, amount, out reason))
                throw new InvalidOperationException($"블루프린트 원료 투입 실패: {resource} ({x}, {z}): {reason}");
        }

        static void CheckIndex(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, "존재하지 않는 공장 블루프린트입니다.");
        }
    }
}

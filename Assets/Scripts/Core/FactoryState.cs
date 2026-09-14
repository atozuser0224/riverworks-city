using System;
using System.Collections.Generic;

namespace Riverworks
{
    public enum FactoryKind { None, Belt, Inserter, Drill, Furnace, Assembler, Storage, ImportDock, ExportDock, PowerInlet, Pole, Splitter }
    public enum FactoryRecipe { None, IronPlate, Tools, Flour, Bread }

    [Serializable]
    public class FactoryEntity
    {
        public int Id, X, Z, Direction;
        public FactoryKind Kind;
        public FactoryRecipe Recipe;
        public Resource Filter = Resource.Coins;
        public List<int> Input = FactoryState.NewInventory();
        public List<int> Output = FactoryState.NewInventory();
        public Resource CargoResource = Resource.Coins;
        public float CargoProgress, Progress;
        public bool Powered;
        public string Status = "";
        public bool SplitLeft;
    }

    [Serializable]
    public class FactoryState
    {
        public int Version = 1, Width = 24, Height = 16, NextEntityId = 1;
        public List<FactoryEntity> Entities = new List<FactoryEntity>();
        public List<int> Produced = NewInventory();
        public List<int> Exported = NewInventory();
        public List<int> Recovered = NewInventory();
        public int PowerBudget = 20;
        public float ElapsedSeconds;

        public static List<int> NewInventory() => new List<int>(new int[9]);
        public static FactoryState CreateEmpty() => new FactoryState();
        public static FactoryState CreateCityGrid() => new FactoryState { Width = 42, Height = 42 };

        public static FactoryState CreateExample()
        {
            var state = CreateEmpty();
            var sim = new FactorySimulation(state);
            void Put(FactoryKind kind, int x, int z, int direction = 0)
            {
                if (!sim.TryPlace(kind, x, z, direction, out string reason))
                    throw new InvalidOperationException("예제 공장 배치 실패: " + reason);
            }
            Put(FactoryKind.Drill, 1, 7); for (int x = 3; x <= 6; x++) Put(FactoryKind.Belt, x, 7);
            Put(FactoryKind.Inserter, 7, 7); Put(FactoryKind.Furnace, 8, 7); Put(FactoryKind.Inserter, 10, 7);
            for (int x = 11; x <= 13; x++) Put(FactoryKind.Belt, x, 7);
            Put(FactoryKind.Inserter, 14, 7); Put(FactoryKind.Assembler, 15, 6); Put(FactoryKind.Inserter, 17, 7);
            for (int x = 18; x <= 20; x++) Put(FactoryKind.Belt, x, 7);
            Put(FactoryKind.Inserter, 21, 7); Put(FactoryKind.ExportDock, 22, 7);
            Put(FactoryKind.ImportDock, 15, 0, 1); Put(FactoryKind.Inserter, 15, 2, 1);
            Put(FactoryKind.Belt, 15, 3, 1); Put(FactoryKind.Belt, 15, 4, 1); Put(FactoryKind.Inserter, 15, 5, 1);
            Put(FactoryKind.PowerInlet, 6, 2); Put(FactoryKind.Pole, 3, 5); Put(FactoryKind.Pole, 8, 5);
            Put(FactoryKind.Pole, 13, 5); Put(FactoryKind.Pole, 18, 5); Put(FactoryKind.Pole, 22, 5);
            var furnace = sim.GetAt(8, 7); sim.SetRecipe(furnace.Id, FactoryRecipe.IronPlate, out _);
            var assembler = sim.GetAt(15, 6); sim.SetRecipe(assembler.Id, FactoryRecipe.Tools, out _);
            var wood = sim.GetAt(15, 0); sim.AddInput(wood.Id, Resource.Timber, 80, out _);
            sim.Recalculate();
            return state;
        }
    }
}

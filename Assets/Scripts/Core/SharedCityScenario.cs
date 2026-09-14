using System;
using System.Linq;

namespace Riverworks
{
    /// <summary>
    /// Deterministic shared-city showcase used by executable and editor verification.
    /// Construction resources and technologies are fixture data; production buffers,
    /// moving cargo, and city exports are deliberately left for real simulation ticks.
    /// </summary>
    public static class SharedCityScenario
    {
        public const int FarmX = 8, FarmZ = 8;
        public const int LumberyardX = 8, LumberyardZ = 12;
        public const int WarehouseX = 14, WarehouseZ = 8;
        public const int MarketX = 14, MarketZ = 9;
        public const int RockX = 12, RockZ = 12;
        public const int RoadBeltMicroX = 24, RoadBeltMicroZ = 20;
        public const int ProcessorMicroX = 22, ProcessorMicroZ = 16;
        public const int DrillMicroX = 24, DrillMicroZ = 24;
        public const int ConnectedInletMicroX = 22, ConnectedInletMicroZ = 22;
        public const int DisconnectedDockMicroX = 28, DisconnectedDockMicroZ = 22;
        public const int DisconnectedInletMicroX = 28, DisconnectedInletMicroZ = 24;

        public static GameState Create()
        {
            GameState state = GameState.CreateNew();
            state.Factory = FactoryState.CreateCityGrid();
            state.ArchivedFactory = null;
            state.OwnedRegions.Clear();
            state.OwnedRegions.Add(4);
            state.OwnedRegions.Add(5);
            state.Technologies = TechCatalog.All.Select(spec => spec.Id).Distinct().ToList();
            state.Era = Era.Industrial;
            state.ActiveResearch = TechId.None;
            state.ResearchDaysRemaining = 0;
            state.Coins = 100000;
            for (int i = 0; i < state.Stock.Count; i++) state.Stock[i] = i == (int)Resource.Coins ? state.Coins : 10000;

            foreach (Cell cell in state.Cells)
            {
                int region = (cell.Z / 7) * 3 + cell.X / 7;
                if (region != 4 && region != 5) continue;
                cell.Building = BuildingKind.None;
                cell.Level = 0;
                cell.Progress = 0;
                cell.Connected = false;
                cell.Status = "";
                cell.Terrain = TerrainKind.Grass;
                cell.LogisticsInput = Cell.NewLogisticsBuffer();
                cell.LogisticsOutput = Cell.NewLogisticsBuffer();
            }

            Cell town = state.Cells[10 * state.Size + 10];
            town.Building = BuildingKind.TownHall;
            town.Level = 1;
            state.Cells[7 * state.Size + 7].Terrain = TerrainKind.Water;
            state.Cells[RockZ * state.Size + RockX].Terrain = TerrainKind.Rock;
            state.Cells[LumberyardZ * state.Size + LumberyardX - 1].Terrain = TerrainKind.Forest;

            var city = new Simulation(state);
            int[,] roads =
            {
                {10,8},{10,9},{9,8},{9,9},{8,9},
                {10,11},{10,12},{9,11},{8,11},
                {11,10},{12,10},{13,10},{13,9},{13,8}
            };
            for (int i = 0; i < roads.GetLength(0); i++) Build(city, BuildingKind.Road, roads[i, 0], roads[i, 1]);
            Build(city, BuildingKind.House, 9, 7);
            Build(city, BuildingKind.House, 10, 7);
            Build(city, BuildingKind.House, 7, 9);
            Build(city, BuildingKind.House, 7, 11);
            state.Population = 24;
            // Earlier city goals have been met; keep milestone rewards out of logistics accounting checks.
            state.Milestone=3;state.Won=false;
            Build(city, BuildingKind.Farm, FarmX, FarmZ);
            Build(city, BuildingKind.Lumberyard, LumberyardX, LumberyardZ);
            Build(city, BuildingKind.Warehouse, WarehouseX, WarehouseZ);
            Build(city, BuildingKind.Market, MarketX, MarketZ);
            Build(city, BuildingKind.Windmill, 12, 9);

            var environment = new CityLogistics(state);
            var factory = new FactorySimulation(state.Factory, environment);
            Put(factory, FactoryKind.PowerInlet, ConnectedInletMicroX, ConnectedInletMicroZ);
            Put(factory, FactoryKind.Pole, 20, 19, 0);
            Put(factory, FactoryKind.Pole, 26, 20, 0);

            Put(factory, FactoryKind.Inserter, 18, 16, 0);
            Put(factory, FactoryKind.Belt, 19, 16, 0);
            Put(factory, FactoryKind.Belt, 20, 16, 0);
            Put(factory, FactoryKind.Inserter, 21, 16, 0);
            FactoryEntity processor = Put(factory, FactoryKind.Assembler, ProcessorMicroX, ProcessorMicroZ, 0);
            if (!factory.SetRecipe(processor.Id, FactoryRecipe.Flour, out string recipeReason))
                throw new InvalidOperationException("Shared-city processor recipe failed: " + recipeReason);
            Put(factory, FactoryKind.Inserter, 24, 16, 0);
            Put(factory, FactoryKind.Belt, 25, 16, 0);
            Put(factory, FactoryKind.Belt, 26, 16, 0);
            Put(factory, FactoryKind.Inserter, 27, 16, 0);

            Put(factory, FactoryKind.Inserter, 18, 24, 0);
            Put(factory, FactoryKind.Belt, 19, 24, 0);
            Put(factory, FactoryKind.Belt, 20, 24, 0);
            Put(factory, FactoryKind.Inserter, 21, 24, 0);
            Put(factory, FactoryKind.Storage, 22, 24, 0);

            Put(factory, FactoryKind.Drill, DrillMicroX, DrillMicroZ, 0);
            Put(factory, FactoryKind.Storage, 26, 26, 0);
            Put(factory, FactoryKind.ExportDock, DisconnectedDockMicroX, DisconnectedDockMicroZ, 0);
            Put(factory, FactoryKind.PowerInlet, DisconnectedInletMicroX, DisconnectedInletMicroZ, 0);
            factory.InvalidateEnvironment();
            city.Recalculate();
            return state;
        }

        static void Build(Simulation city, BuildingKind kind, int x, int z)
        {
            if (!city.Build(kind, x, z, out string reason))
                throw new InvalidOperationException($"Shared-city {kind} at {x},{z} failed: {reason}");
        }

        static FactoryEntity Put(FactorySimulation factory, FactoryKind kind, int x, int z, int direction = 0)
        {
            if (!factory.TryPlace(kind, x, z, direction, out string reason))
                throw new InvalidOperationException($"Shared-city {kind} at {x},{z} failed: {reason}");
            return factory.GetAt(x, z) ?? throw new InvalidOperationException($"Shared-city {kind} at {x},{z} was not retained.");
        }
    }
}

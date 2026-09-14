using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    public enum IndustryScenarioKind
    {
        OilChemistry,
        AluminumRecycling,
        ControlAssembly
    }

    /// <summary>
    /// Deterministic, saveable industrial layouts used by native smoke checks and captures.
    /// The terrain is intentionally controlled fixture data: only the pump lots are changed
    /// to water, while the surveyed oil/bauxite deposits retain their real city coordinates.
    /// Prepared components in ControlAssembly represent an explicitly final-stage fixture.
    /// </summary>
    public static class IndustrialScenario
    {
        public const int OilRefineryX = 36, OilRefineryZ = 12;
        public const int PlasticPlantX = 38, PlasticPlantZ = 16;
        public const int AluminaRefineryX = 32, AluminaRefineryZ = 26;
        public const int ScrapRefineryX = 36, ScrapRefineryZ = 26;
        public const int AluminumFoundryX = 40, AluminumFoundryZ = 30;
        public const int ControlManufacturerX = 36, ControlManufacturerZ = 16;

        public static GameState Create(IndustryScenarioKind kind)
        {
            GameState state = CreateBaseState();
            var factory = new FactorySimulation(state.Factory, new CityLogistics(state));
            factory.ConfigureTechnology(state);

            switch (kind)
            {
                case IndustryScenarioKind.OilChemistry: BuildOilChemistry(state, factory); break;
                case IndustryScenarioKind.AluminumRecycling: BuildAluminumRecycling(state, factory); break;
                case IndustryScenarioKind.ControlAssembly: BuildControlAssembly(state, factory); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown industrial scenario.");
            }

            factory.InvalidateEnvironment();
            FactoryStateValidation.Validate(state.Factory);
            FactoryLayers.ValidateCity(state);
            CityProjects.Validate(state);
            return state;
        }

        public static FactoryEntity FindMachine(GameState state, FactoryRecipe recipe)
        {
            if (state?.Factory?.Entities == null) return null;
            return state.Factory.Entities.FirstOrDefault(entity => entity != null && entity.Recipe == recipe);
        }

        static GameState CreateBaseState()
        {
            GameState state = GameState.CreateNew();
            state.Version = 6;
            state.Factory = FactoryState.CreateCityGrid();
            state.ArchivedFactory = null;
            state.OwnedRegions = Enumerable.Range(0, 9).ToList();
            state.Technologies = TechCatalog.All.Select(spec => spec.Id).Where(id => id != TechId.None).Distinct().ToList();
            state.Era = Era.Industrial;
            state.ActiveResearch = TechId.None;
            state.ResearchDaysRemaining = 0;
            state.Coins = 100000;
            for (int i = 0; i < state.Stock.Count; i++) state.Stock[i] = 0;
            state.Stock[(int)Resource.Coins] = state.Coins;
            state.Stock[(int)Resource.Timber] = 10000;
            state.Stock[(int)Resource.Stone] = 10000;

            foreach (Cell cell in state.Cells)
            {
                cell.Building = BuildingKind.None;
                cell.Level = 0;
                cell.Connected = false;
                cell.Progress = 0;
                cell.Status = "";
                cell.Terrain = TerrainKind.Grass;
                cell.LogisticsInput = Cell.NewLogisticsBuffer();
                cell.LogisticsOutput = Cell.NewLogisticsBuffer();
            }

            Cell town = state.Cells[10 * state.Size + 10];
            town.Building = BuildingKind.TownHall;
            town.Level = 1;

            var city = new Simulation(state);
            for (int z = 0; z < state.Size; z++)
                if (z != 10) Build(city, BuildingKind.Road, 10, z);
            for (int x = 0; x < state.Size; x++)
                if (x != 10) Build(city, BuildingKind.Road, x, 10);
            // Connected spur for the aluminum fixture's road-facing export dock.
            for (int z = 11; z <= 13; z++) Build(city, BuildingKind.Road, 19, z);
            for (int z = 0; z < state.Size; z++)
                if (z != 10) Build(city, BuildingKind.Windmill, 11, z);

            city.Recalculate();
            state.Factory.PowerBudget = city.PowerCapacity;
            state.Factory.Recovered = FactoryState.NewInventory();
            return state;
        }

        static void BuildOilChemistry(GameState state, FactorySimulation sim)
        {
            SetTerrain(state, 15, 4, TerrainKind.Water);

            Put(sim, FactoryKind.OilPump, 32, 8, 0);
            Put(sim, FactoryKind.WaterPump, 30, 8, 3);
            FactoryEntity refinery = Put(sim, FactoryKind.Refinery, OilRefineryX, OilRefineryZ, 0);
            Recipe(sim, refinery, FactoryRecipe.OilRefining);
            FactoryEntity chemical = Put(sim, FactoryKind.ChemicalPlant, PlasticPlantX, PlasticPlantZ, 0);
            Recipe(sim, chemical, FactoryRecipe.PlasticPolymerization);

            // Crude oil: oil-pump front port -> east, south, east into refinery rear port 0.
            Put(sim, FactoryKind.Pipe, 34, 8, 0);
            for (int z = 8; z <= 11; z++) Put(sim, FactoryKind.Pipe, 35, z, 1);
            Put(sim, FactoryKind.Pipe, 35, 12, 0);

            // Fresh water: north-facing pump -> west, south, east into refinery rear port 1.
            Put(sim, FactoryKind.Pipe, 30, 7, 2);
            for (int z = 7; z <= 12; z++) Put(sim, FactoryKind.Pipe, 29, z, 1);
            for (int x = 29; x <= 35; x++) Put(sim, FactoryKind.Pipe, x, 13, 0);

            // Heavy oil is deliberately retained so byproduct backpressure is observable.
            Put(sim, FactoryKind.FluidTank, 38, 12, 0);

            // Petroleum gas turns around the tank and enters the chemical plant's rear port.
            Put(sim, FactoryKind.Pipe, 38, 13, 0);
            Put(sim, FactoryKind.Pipe, 39, 13, 1);
            Put(sim, FactoryKind.Pipe, 39, 14, 1);
            Put(sim, FactoryKind.Pipe, 39, 15, 2);
            Put(sim, FactoryKind.Pipe, 38, 15, 2);
            Put(sim, FactoryKind.Pipe, 37, 15, 1);
            Put(sim, FactoryKind.Pipe, 37, 16, 0);

            FactoryEntity coal = Put(sim, FactoryKind.Storage, 36, 17, 0);
            Input(sim, coal, Resource.Coal, 40);
            Put(sim, FactoryKind.Inserter, 37, 17, 0);

            Put(sim, FactoryKind.Inserter, 40, 16, 0);
            Put(sim, FactoryKind.Belt, 41, 16, 1);
            Put(sim, FactoryKind.Inserter, 41, 17, 1);
            Put(sim, FactoryKind.ExportDock, 40, 18, 0);
            AddPowerSpine(sim, 16, 18, 24, 30, 34);
            Put(sim, FactoryKind.Pole, 30, 11, 0);
            Put(sim, FactoryKind.Pole, 40, 15, 0);
        }

        static void BuildAluminumRecycling(GameState state, FactorySimulation sim)
        {
            SetTerrain(state, 15, 14, TerrainKind.Water);

            Put(sim, FactoryKind.WaterPump, 30, 28, 3);
            FactoryEntity alumina = Put(sim, FactoryKind.Refinery, AluminaRefineryX, AluminaRefineryZ, 0);
            Recipe(sim, alumina, FactoryRecipe.AluminaRefining);
            FactoryEntity scrap = Put(sim, FactoryKind.Refinery, ScrapRefineryX, ScrapRefineryZ, 0);
            Recipe(sim, scrap, FactoryRecipe.ScrapRefining);
            FactoryEntity foundry = Put(sim, FactoryKind.Foundry, AluminumFoundryX, AluminumFoundryZ, 0);
            Recipe(sim, foundry, FactoryRecipe.AluminumSmelting);

            // Fresh water merges with the recovered-water return immediately before the alumina input.
            Put(sim, FactoryKind.Pipe, 30, 27, 3);
            Put(sim, FactoryKind.PipeJunction, 30, 26, 0);
            Put(sim, FactoryKind.Pipe, 31, 26, 0);
            Put(sim, FactoryKind.Pipe, 34, 26, 0);
            Put(sim, FactoryKind.Pipe, 35, 26, 0);
            Put(sim, FactoryKind.Pipe, 38, 26, 3);
            Put(sim, FactoryKind.Pipe, 38, 25, 3);
            Put(sim, FactoryKind.Pipe, 38, 24, 2);
            for (int x = 31; x <= 37; x++) Put(sim, FactoryKind.Pipe, x, 24, 2);
            Put(sim, FactoryKind.Pipe, 30, 24, 1);
            Put(sim, FactoryKind.Pipe, 30, 25, 1);

            FactoryEntity bauxite = Put(sim, FactoryKind.Storage, 32, 29, 0);
            Input(sim, bauxite, Resource.Bauxite, 60);
            Put(sim, FactoryKind.Inserter, 32, 28, 3);
            FactoryEntity coal = Put(sim, FactoryKind.Storage, 36, 29, 0);
            Input(sim, coal, Resource.Coal, 40);
            Put(sim, FactoryKind.Inserter, 36, 28, 3);

            // Scrap uses its own item lane into the foundry's east tile.
            Put(sim, FactoryKind.Inserter, 38, 27, 0);
            Put(sim, FactoryKind.Belt, 39, 27, 0);
            Put(sim, FactoryKind.Belt, 40, 27, 0);
            Put(sim, FactoryKind.Belt, 41, 27, 1);
            Put(sim, FactoryKind.Belt, 41, 28, 1);
            Put(sim, FactoryKind.Inserter, 41, 29, 1);

            // Stone is actually crushed; the resulting silica travels on a second lane.
            FactoryEntity crusher = Put(sim, FactoryKind.MachiningBench, 36, 34, 0);
            Recipe(sim, crusher, FactoryRecipe.SilicaCrushing);
            FactoryEntity stone = Put(sim, FactoryKind.Storage, 34, 34, 0);
            Input(sim, stone, Resource.Stone, 60);
            Put(sim, FactoryKind.Inserter, 35, 34, 0);
            Put(sim, FactoryKind.Inserter, 38, 34, 0);
            Put(sim, FactoryKind.Belt, 39, 34, 0);
            Put(sim, FactoryKind.Belt, 40, 34, 3);
            Put(sim, FactoryKind.Belt, 40, 33, 3);
            Put(sim, FactoryKind.Inserter, 40, 32, 3);

            // Finished aluminum leaves the foundry through a separate west-side lane.
            Put(sim, FactoryKind.Inserter, 39, 31, 2);
            Put(sim, FactoryKind.Belt, 38, 31, 3);
            Put(sim, FactoryKind.Inserter, 38, 30, 3);
            Put(sim, FactoryKind.ExportDock, 38, 28, 0);
            AddPowerSpine(sim, 30, 18, 24, 30, 36);
            Put(sim, FactoryKind.Pole, 40, 28, 0);
        }

        static void BuildControlAssembly(GameState state, FactorySimulation sim)
        {
            FactoryEntity manufacturer = Put(sim, FactoryKind.Manufacturer, ControlManufacturerX, ControlManufacturerZ, 0);
            Recipe(sim, manufacturer, FactoryRecipe.ControlUnitAssembly);

            Resource[] prepared =
            {
                Resource.Computer, Resource.Motor, Resource.Battery,
                Resource.ModularFrame, Resource.AluminumCasing
            };
            AddPreparedLane(sim, prepared[0], 32, 16, 33, 16, 34, 16, 35, 16, 0);
            AddPreparedLane(sim, prepared[1], 32, 17, 33, 17, 34, 17, 35, 17, 0);
            AddPreparedLane(sim, prepared[2], 36, 12, 36, 13, 36, 14, 36, 15, 1);
            AddPreparedLane(sim, prepared[3], 37, 12, 37, 13, 37, 14, 37, 15, 1);
            FactoryEntity casing = Put(sim, FactoryKind.Storage, 36, 23, 3);
            Input(sim, casing, prepared[4], 40);
            FactoryEntity casingSource = Put(sim, FactoryKind.Inserter, 36, 22, 3);
            Filter(sim, casingSource, prepared[4]);
            Put(sim, FactoryKind.Belt, 36, 21, 3);
            Put(sim, FactoryKind.Belt, 36, 20, 3);
            Put(sim, FactoryKind.Belt, 36, 19, 3);
            FactoryEntity casingTarget = Put(sim, FactoryKind.Inserter, 36, 18, 3);
            Filter(sim, casingTarget, prepared[4]);

            Put(sim, FactoryKind.Inserter, 38, 16, 0);
            Put(sim, FactoryKind.Belt, 39, 16, 1);
            Put(sim, FactoryKind.Inserter, 39, 17, 1);
            Put(sim, FactoryKind.ExportDock, 38, 18, 0);
            AddPowerSpine(sim, 16, 18, 24, 30);
            Put(sim, FactoryKind.Pole, 34, 19, 0);
            Put(sim, FactoryKind.Pole, 40, 17, 0);
        }

        static void AddPreparedLane(FactorySimulation sim, Resource resource, int storageX, int storageZ,
            int sourceInserterX, int sourceInserterZ, int beltX, int beltZ, int targetInserterX, int targetInserterZ, int direction)
        {
            FactoryEntity storage = Put(sim, FactoryKind.Storage, storageX, storageZ, direction);
            Input(sim, storage, resource, 40);
            FactoryEntity source = Put(sim, FactoryKind.Inserter, sourceInserterX, sourceInserterZ, direction);
            Filter(sim, source, resource);
            Put(sim, FactoryKind.Belt, beltX, beltZ, direction);
            FactoryEntity target = Put(sim, FactoryKind.Inserter, targetInserterX, targetInserterZ, direction);
            Filter(sim, target, resource);
        }

        static void AddPowerSpine(FactorySimulation sim, int z, int inletX, params int[] poleXs)
        {
            Put(sim, FactoryKind.PowerInlet, inletX, z, 0);
            foreach (int x in poleXs) Put(sim, FactoryKind.Pole, x, z, 0);
        }

        static void SetTerrain(GameState state, int x, int z, TerrainKind terrain) => state.Cells[z * state.Size + x].Terrain = terrain;

        static void Build(Simulation city, BuildingKind kind, int x, int z)
        {
            if (!city.Build(kind, x, z, out string reason))
                throw new InvalidOperationException($"Industrial fixture city {kind} at {x},{z} failed: {reason}");
        }

        static FactoryEntity Put(FactorySimulation sim, FactoryKind kind, int x, int z, int direction)
        {
            if (!sim.TryPlace(kind, x, z, direction, out string reason))
                throw new InvalidOperationException($"Industrial fixture {kind} at {x},{z} failed: {reason}");
            return sim.GetAt(x, z) ?? throw new InvalidOperationException($"Industrial fixture {kind} at {x},{z} was not retained.");
        }

        static void Recipe(FactorySimulation sim, FactoryEntity entity, FactoryRecipe recipe)
        {
            if (!sim.SetRecipe(entity.Id, recipe, out string reason))
                throw new InvalidOperationException($"Industrial fixture recipe {recipe} failed: {reason}");
        }

        static void Input(FactorySimulation sim, FactoryEntity entity, Resource resource, int amount)
        {
            if (!sim.AddInput(entity.Id, resource, amount, out string reason))
                throw new InvalidOperationException($"Industrial fixture input {resource} failed: {reason}");
        }

        static void Filter(FactorySimulation sim, FactoryEntity entity, Resource resource)
        {
            if (!sim.SetFilter(entity.Id, resource, out string reason))
                throw new InvalidOperationException($"Industrial fixture filter {resource} failed: {reason}");
        }
    }
}

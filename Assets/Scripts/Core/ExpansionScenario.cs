using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    /// <summary>
    /// Deterministic v0.9 fixture for native expansion smoke checks. The food line has no
    /// prepared flour or bread: its only item seed is grain in the ground-floor storage.
    /// Water is prepared separately to exercise the vertical fluid path.
    /// </summary>
    public static class ExpansionScenario
    {
        public const int FlourMachineX = 30, FlourMachineZ = 24, FlourMachineFloor = 1;
        public const int BreadMachineX = 36, BreadMachineZ = 24, BreadMachineFloor = 2;
        public const int GroundTankX = 26, GroundTankZ = 28, GroundTankFloor = 0;
        public const int TopTankX = 34, TopTankZ = 28, TopTankFloor = 2;
        public const int CampusX = 14, CampusZ = 5;

        public static GameState Create()
        {
            GameState state = CreateBaseState();
            var city = new Simulation(state);
            BuildCityInfrastructure(city);

            BuildPlatforms(state);

            var factory = new FactorySimulation(state.Factory, new CityLogistics(state));
            factory.ConfigureTechnology(state);
            BuildPower(factory);
            BuildFoodLine(factory);
            BuildFluidLine(factory);
            InstallAutomation(state, factory);

            city.Recalculate();
            state.Factory.PowerBudget = city.PowerCapacity;
            factory.InvalidateEnvironment();

            FactoryStateValidation.Validate(state.Factory);
            FactoryLayers.ValidateCity(state);
            CityProjects.Validate(state);
            ValidateFixture(state);
            return state;
        }

        public static FactoryEntity FindBreadMachine(GameState state)
        {
            return state?.Factory?.Entities?.FirstOrDefault(entity => entity != null &&
                entity.Kind == FactoryKind.Assembler && entity.Recipe == FactoryRecipe.Bread &&
                entity.Floor == BreadMachineFloor);
        }

        public static FactoryEntity FindTopTank(GameState state)
        {
            return state?.Factory?.Entities?.FirstOrDefault(entity => entity != null &&
                entity.Kind == FactoryKind.FluidTank && entity.X == TopTankX &&
                entity.Z == TopTankZ && entity.Floor == TopTankFloor);
        }

        static GameState CreateBaseState()
        {
            GameState state = GameState.CreateNew();
            state.Version = 6;
            state.Factory = FactoryState.CreateCityGrid();
            state.ArchivedFactory = null;
            state.OwnedRegions = Enumerable.Range(0, 9).ToList();
            state.Technologies = TechCatalog.All.Select(spec => spec.Id)
                .Where(id => id != TechId.None).Distinct().ToList();
            state.Era = Era.Industrial;
            state.ActiveResearch = TechId.None;
            state.ResearchDaysRemaining = 0;
            state.ResearchPoints = 0;
            state.Population = 0;
            state.Happiness = 72;
            state.Tutorial = null;
            state.Day = 0;
            state.DayProgressSeconds = 0;
            state.Milestone = 0;
            state.Won = false;
            state.TotalProduced = 0;
            state.TotalToolsProduced = 0;
            state.LastBreadDemand = 0;
            state.LastToolsDemand = 0;
            state.LastToolsDelivered = 0;
            state.LastGrainConsumed = 0;
            state.LastFoodSatisfied = false;
            state.CityProjects = new List<CityProjectState>();

            state.Coins = 100000;
            for (int i = 0; i < state.Stock.Count; i++) state.Stock[i] = 0;
            state.Stock[(int)Resource.Coins] = state.Coins;
            state.Stock[(int)Resource.Timber] = 10000;
            state.Stock[(int)Resource.Stone] = 10000;
            state.Stock[(int)Resource.SteelBeam] = 1000;
            state.Stock[(int)Resource.ModularFrame] = 500;
            state.Stock[(int)Resource.Circuit] = 100;
            state.Stock[(int)Resource.Computer] = 100;
            state.Stock[(int)Resource.ControlUnit] = 20;
            state.Stock[(int)Resource.Grain] = 0;
            state.Stock[(int)Resource.Flour] = 0;
            state.Stock[(int)Resource.Bread] = 0;

            foreach (Cell cell in state.Cells)
            {
                cell.Terrain = TerrainKind.Grass;
                cell.Building = BuildingKind.None;
                cell.Level = 0;
                cell.Connected = false;
                cell.Progress = 0;
                cell.Status = "";
                cell.LogisticsInput = Cell.NewLogisticsBuffer();
                cell.LogisticsOutput = Cell.NewLogisticsBuffer();
            }
            Cell townHall = state.Cells[10 * state.Size + 10];
            townHall.Building = BuildingKind.TownHall;
            townHall.Level = 1;
            return state;
        }

        static void BuildCityInfrastructure(Simulation city)
        {
            for (int z = 0; z < city.State.Size; z++)
                if (z != 10) Build(city, BuildingKind.Road, 10, z);
            for (int x = 0; x < city.State.Size; x++)
                if (x != 10) Build(city, BuildingKind.Road, x, 10);

            // A connected west approach leaves the 2x2 campus footprint at 14,5 clear.
            for (int x = 11; x <= 13; x++) Build(city, BuildingKind.Road, x, 5);
            // The ground export dock at city lot 19,15 touches this connected road spur.
            for (int x = 11; x <= 18; x++) Build(city, BuildingKind.Road, x, 15);

            int windmills = 0;
            for (int z = 0; z < city.State.Size; z++)
            {
                if (z == 5 || z == 10 || z == 15) continue;
                Build(city, BuildingKind.Windmill, 11, z);
                windmills++;
            }
            Build(city, BuildingKind.Windmill, 9, 0); windmills++;
            Build(city, BuildingKind.Windmill, 9, 1); windmills++;
            if (windmills != 20) throw new InvalidOperationException("Expansion fixture must contain exactly twenty connected windmills.");
            city.Recalculate();
            if (city.PowerCapacity < 160) throw new InvalidOperationException("Expansion fixture city power must be at least 160.");
        }

        static void BuildPlatforms(GameState state)
        {
            int[,] floorOne =
            {
                { 26, 24 }, { 28, 24 }, { 30, 24 }, { 32, 24 }, { 34, 24 },
                { 36, 24 }, { 38, 24 }, { 38, 26 }, { 38, 28 },
                { 28, 22 }, { 34, 22 }, { 40, 22 },
                { 26, 28 }, { 28, 28 }, { 30, 28 }, { 32, 28 }, { 34, 28 },
                { 36, 28 }, { 40, 28 }
            };
            int[,] floorTwo =
            {
                { 32, 24 }, { 34, 24 }, { 36, 24 }, { 38, 24 },
                { 28, 22 }, { 34, 22 }, { 40, 22 },
                { 32, 28 }, { 34, 28 }, { 36, 28 }
            };
            for (int i = 0; i < floorOne.GetLength(0); i++) Platform(state, floorOne[i, 0], floorOne[i, 1], 1);
            for (int i = 0; i < floorTwo.GetLength(0); i++) Platform(state, floorTwo[i, 0], floorTwo[i, 1], 2);
        }

        static void BuildPower(FactorySimulation sim)
        {
            Put(sim, FactoryKind.PowerInlet, 24, 22, 0, 0);
            foreach (int x in new[] { 28, 34, 40 })
            {
                Put(sim, FactoryKind.Pole, x, 22, 0, 0);
                Put(sim, FactoryKind.Pole, x, 22, 0, 1);
                Put(sim, FactoryKind.Pole, x, 22, 0, 2);
            }
            Put(sim, FactoryKind.Pole, 30, 28, 0, 0);
            Put(sim, FactoryKind.Pole, 40, 28, 0, 0);
            Put(sim, FactoryKind.Pole, 26, 28, 0, 1);
            Put(sim, FactoryKind.Pole, 36, 28, 0, 1);
            Put(sim, FactoryKind.Pole, 40, 28, 0, 1);
            Put(sim, FactoryKind.Pole, 36, 28, 0, 2);
        }

        static void BuildFoodLine(FactorySimulation sim)
        {
            FactoryEntity grain = Put(sim, FactoryKind.Storage, 24, 24, 0, 0);
            Input(sim, grain, Resource.Grain, 80);
            Filter(sim, Put(sim, FactoryKind.Inserter, 25, 24, 0, 0), Resource.Grain);

            FactoryEntity grainLift = Link(sim, FactoryKind.ItemLift, 26, 24, 0, 0, 1, out FactoryEntity grainReceiver);
            Filter(sim, grainLift, Resource.Grain);
            Filter(sim, grainReceiver, Resource.Grain);
            Put(sim, FactoryKind.Belt, 27, 24, 0, 1);
            Put(sim, FactoryKind.Belt, 28, 24, 0, 1);
            Filter(sim, Put(sim, FactoryKind.Inserter, 29, 24, 0, 1), Resource.Grain);

            FactoryEntity flour = Put(sim, FactoryKind.Assembler, FlourMachineX, FlourMachineZ, 0, FlourMachineFloor);
            Recipe(sim, flour, FactoryRecipe.Flour);
            Filter(sim, Put(sim, FactoryKind.Inserter, 32, 24, 0, 1), Resource.Flour);
            FactoryEntity flourLift = Link(sim, FactoryKind.ItemLift, 33, 24, 0, 1, 2, out FactoryEntity flourReceiver);
            Filter(sim, flourLift, Resource.Flour);
            Filter(sim, flourReceiver, Resource.Flour);
            Put(sim, FactoryKind.Belt, 34, 24, 0, 2);
            Filter(sim, Put(sim, FactoryKind.Inserter, 35, 24, 0, 2), Resource.Flour);

            FactoryEntity bread = Put(sim, FactoryKind.Assembler, BreadMachineX, BreadMachineZ, 0, BreadMachineFloor);
            Recipe(sim, bread, FactoryRecipe.Bread);
            Filter(sim, Put(sim, FactoryKind.Inserter, 38, 24, 0, 2), Resource.Bread);

            FactoryEntity upperDown = Link(sim, FactoryKind.ItemLift, 39, 24, 0, 2, 1, out FactoryEntity middleReceiver);
            Filter(sim, upperDown, Resource.Bread);
            Filter(sim, middleReceiver, Resource.Bread);
            Rotate(sim, middleReceiver);
            Put(sim, FactoryKind.Belt, 39, 25, 1, 1);

            FactoryEntity lowerDown = Link(sim, FactoryKind.ItemLift, 39, 26, 1, 1, 0, out FactoryEntity groundReceiver);
            Filter(sim, lowerDown, Resource.Bread);
            Filter(sim, groundReceiver, Resource.Bread);
            Put(sim, FactoryKind.Belt, 39, 27, 1, 0);
            Put(sim, FactoryKind.Belt, 39, 28, 1, 0);
            Filter(sim, Put(sim, FactoryKind.Inserter, 39, 29, 1, 0), Resource.Bread);
            Put(sim, FactoryKind.ExportDock, 38, 30, 0, 0);
        }

        static void BuildFluidLine(FactorySimulation sim)
        {
            FactoryEntity groundTank = Put(sim, FactoryKind.FluidTank, GroundTankX, GroundTankZ, 0, GroundTankFloor);
            Filter(sim, groundTank, Resource.Water);
            Input(sim, groundTank, Resource.Water, 40);
            Filter(sim, Put(sim, FactoryKind.Pipe, 27, 28, 0, 0), Resource.Water);

            FactoryEntity firstRiser = Link(sim, FactoryKind.FluidRiser, 28, 28, 0, 0, 1, out FactoryEntity firstReceiver);
            Filter(sim, firstRiser, Resource.Water);
            Filter(sim, firstReceiver, Resource.Water);
            Filter(sim, Put(sim, FactoryKind.Pipe, 29, 28, 0, 1), Resource.Water);
            Filter(sim, Put(sim, FactoryKind.FluidTank, 30, 28, 0, 1), Resource.Water);
            Filter(sim, Put(sim, FactoryKind.Pipe, 31, 28, 0, 1), Resource.Water);

            FactoryEntity secondRiser = Link(sim, FactoryKind.FluidRiser, 32, 28, 0, 1, 2, out FactoryEntity secondReceiver);
            Filter(sim, secondRiser, Resource.Water);
            Filter(sim, secondReceiver, Resource.Water);
            Filter(sim, Put(sim, FactoryKind.Pipe, 33, 28, 0, 2), Resource.Water);
            Filter(sim, Put(sim, FactoryKind.FluidTank, TopTankX, TopTankZ, 0, TopTankFloor), Resource.Water);
        }

        static void InstallAutomation(GameState state, FactorySimulation sim)
        {
            FactoryEntity bread = FindBreadMachine(state) ?? throw new InvalidOperationException("Expansion fixture bread machine is missing.");
            FactoryEntity groundTank = sim.GetAt(GroundTankX, GroundTankZ, GroundTankFloor)
                ?? throw new InvalidOperationException("Expansion fixture ground tank is missing.");
            FactoryEntity topTank = FindTopTank(state) ?? throw new InvalidOperationException("Expansion fixture top tank is missing.");

            var breadDemand = new AutomationRule
            {
                SourceEntityId = 0,
                TargetEntityId = bread.Id,
                Resource = Resource.Bread,
                Comparison = AutomationComparison.AtMost,
                Action = AutomationAction.AllowWhenTrue,
                Threshold = 0,
                Enabled = true
            };
            SaveRule(state, breadDemand);

            var waterCeiling = new AutomationRule
            {
                SourceEntityId = topTank.Id,
                TargetEntityId = groundTank.Id,
                Resource = Resource.Water,
                Comparison = AutomationComparison.AtLeast,
                Action = AutomationAction.StopWhenTrue,
                Threshold = 8,
                Enabled = true
            };
            SaveRule(state, waterCeiling);
        }

        static void ValidateFixture(GameState state)
        {
            if (state.Version != 6 || state.Factory.Version != 3 || state.Factory.Width != 42 || state.Factory.Height != 42)
                throw new InvalidOperationException("Expansion fixture schema versions are incorrect.");
            if (state.Population != 0 || state.Tutorial != null || state.Technologies.Count != 26)
                throw new InvalidOperationException("Expansion fixture population, tutorial, or technology seed is incorrect.");
            if (state.Stock[(int)Resource.Grain] != 0 || state.Stock[(int)Resource.Flour] != 0 || state.Stock[(int)Resource.Bread] != 0)
                throw new InvalidOperationException("Expansion fixture city food stock must start empty.");
            if (state.Factory.Produced.Any(amount => amount != 0) || state.Factory.Exported.Any(amount => amount != 0))
                throw new InvalidOperationException("Expansion fixture cannot seed production or export results.");
            FactoryEntity grain = state.Factory.Entities.SingleOrDefault(entity => entity.Kind == FactoryKind.Storage && entity.X == 24 && entity.Z == 24 && entity.Floor == 0);
            if (grain == null || grain.Input[(int)Resource.Grain] != 80 || grain.Input.Where((amount, index) => index != (int)Resource.Grain).Any(amount => amount != 0))
                throw new InvalidOperationException("Expansion fixture must seed only eighty grain in its food line.");
            FactoryEntity groundTank = state.Factory.Entities.Single(entity => entity.Kind == FactoryKind.FluidTank && entity.X == GroundTankX && entity.Z == GroundTankZ && entity.Floor == GroundTankFloor);
            if (groundTank.Input[(int)Resource.Water] != 40) throw new InvalidOperationException("Expansion fixture prepared water is missing.");
            if (state.Factory.AutomationRules.Count != 2 || state.Factory.Entities.Count(entity => entity.ControllerInstalled) != 2)
                throw new InvalidOperationException("Expansion fixture must install exactly two automation controllers and rules.");
            if (state.CityProjects.Count != 0)
                throw new InvalidOperationException("Expansion fixture must leave city projects unstarted for native UI placement.");
            if (!CityProjects.CanStart(state, CityProjectKind.ResearchCampus, CampusX, CampusZ, out string projectReason))
                throw new InvalidOperationException("Expansion fixture Research Campus site is not startable: " + projectReason);
        }

        static void Build(Simulation city, BuildingKind kind, int x, int z)
        {
            if (!city.Build(kind, x, z, out string reason))
                throw new InvalidOperationException($"Expansion fixture city {kind} at {x},{z} failed: {reason}");
        }

        static void Platform(GameState state, int x, int z, int floor)
        {
            if (!FactoryLayers.TryPlacePlatform(state, x, z, floor, out string reason))
                throw new InvalidOperationException($"Expansion fixture platform at {x},{z}, floor {floor} failed: {reason}");
        }

        static FactoryEntity Put(FactorySimulation sim, FactoryKind kind, int x, int z, int direction, int floor)
        {
            if (!sim.TryPlace(kind, x, z, direction, floor, out string reason))
                throw new InvalidOperationException($"Expansion fixture {kind} at {x},{z}, floor {floor} failed: {reason}");
            return sim.GetAt(x, z, floor) ?? throw new InvalidOperationException($"Expansion fixture {kind} was not retained.");
        }

        static FactoryEntity Link(FactorySimulation sim, FactoryKind kind, int x, int z, int direction,
            int fromFloor, int toFloor, out FactoryEntity receiver)
        {
            int senderId = sim.State.NextEntityId;
            if (!sim.TryPlaceLink(kind, x, z, direction, fromFloor, toFloor, out string reason))
                throw new InvalidOperationException($"Expansion fixture {kind} link at {x},{z} failed: {reason}");
            FactoryEntity sender = sim.State.Entities.Single(entity => entity.Id == senderId);
            receiver = sim.State.Entities.Single(entity => entity.Id == senderId + 1);
            return sender;
        }

        static void Recipe(FactorySimulation sim, FactoryEntity entity, FactoryRecipe recipe)
        {
            if (!sim.SetRecipe(entity.Id, recipe, out string reason))
                throw new InvalidOperationException($"Expansion fixture recipe {recipe} failed: {reason}");
        }

        static void Input(FactorySimulation sim, FactoryEntity entity, Resource resource, int amount)
        {
            if (!sim.AddInput(entity.Id, resource, amount, out string reason))
                throw new InvalidOperationException($"Expansion fixture input {resource} failed: {reason}");
        }

        static void Filter(FactorySimulation sim, FactoryEntity entity, Resource resource)
        {
            if (!sim.SetFilter(entity.Id, resource, out string reason))
                throw new InvalidOperationException($"Expansion fixture filter {resource} failed: {reason}");
        }

        static void Rotate(FactorySimulation sim, FactoryEntity entity)
        {
            if (!sim.Rotate(entity.Id, out string reason))
                throw new InvalidOperationException($"Expansion fixture rotation failed: {reason}");
        }

        static void SaveRule(GameState state, AutomationRule rule)
        {
            if (!FactoryAutomation.SaveRule(state, rule, out string reason))
                throw new InvalidOperationException("Expansion fixture automation rule failed: " + reason);
        }
    }
}

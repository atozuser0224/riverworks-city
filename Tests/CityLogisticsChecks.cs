using System;
using System.Linq;
using Riverworks;

public static class CityLogisticsChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        NewGamesUseTheSharedCityGrid();
        PlacementAndCityConstructionShareLots();
        PowerAndExportsRequireConnectedRoads();
        CityProductionUsesPhysicalBuffersAndInserters();
        FullOutputBacksUpBeforeInputConsumption();
        DemolitionRecoversPhysicalBuffers();
        VersionThreeMigrationArchivesAndRecoversOnce();
        VersionFourStateIsStable();
        SharedScenarioBuildsAgainstTheCoreContract();
        return passed;
    }

    static void NewGamesUseTheSharedCityGrid()
    {
        GameState state = GameState.CreateNew();
        True(state.Version == 6 && state.Factory != null && state.Factory.Version == 3 && state.Factory.Width == 42 && state.Factory.Height == 42,
            "new games use the 42x42 factory grid over the 21x21 city");
        True(state.Cells.All(cell => cell.LogisticsInput.Count == ResourceCatalog.Count && cell.LogisticsOutput.Count == ResourceCatalog.Count),
            "every city lot starts with fixed physical input and output buffers");
    }

    static void PlacementAndCityConstructionShareLots()
    {
        GameState state = GameState.CreateNew();
        var city = new Simulation(state);
        var environment = new CityLogistics(state);
        var factory = new FactorySimulation(state.Factory, environment);

        Cell lot = Clear(state, 7, 7, TerrainKind.Grass);
        bool storagePlaced = factory.TryPlace(FactoryKind.Storage, 14, 14, 0, out string storageReason);
        True(storagePlaced, "aligned machine occupies an owned city lot: " + storageReason);
        True(!city.CanBuild(BuildingKind.Road, 7, 7, out _), "city road cannot overlap a factory machine");
        True(factory.Remove(14, 14, out _), "machine can be removed from shared lot");

        True(factory.TryPlace(FactoryKind.Belt, 14, 14, 0, out _), "light transport occupies a half-lot cell");
        True(city.CanBuild(BuildingKind.Road, 7, 7, out _), "city road may coexist with light transport");
        True(!city.CanBuild(BuildingKind.House, 7, 7, out _), "ordinary city building cannot overlap light transport");
        lot.Building = BuildingKind.Road; lot.Level = 1;
        True(city.Demolish(7, 7, out _) && lot.Building == BuildingKind.None && factory.GetAt(14, 14)?.Kind == FactoryKind.Belt,
            "dry road removal leaves valid light transport in place");
        True(factory.Remove(14, 14, out _), "light transport removal succeeds");

        True(!factory.CanPlace(FactoryKind.Belt, 0, 0, 0, out _), "factory equipment is rejected on unowned city land");
        True(!factory.CanPlace(FactoryKind.Storage, 20, 20, 0, out _), "factory machine is rejected on the Town Hall");
        True(!factory.CanPlace(FactoryKind.Furnace, 15, 14, 0, out _), "2x2 city machines align to exact microcell lots");

        lot.Terrain = TerrainKind.Water;
        True(!factory.CanPlace(FactoryKind.Belt, 14, 14, 0, out _), "water rejects light transport without a bridge");
        lot.Building = BuildingKind.Road; lot.Level = 1;
        True(factory.TryPlace(FactoryKind.Belt, 14, 14, 0, out _), "road bridge supports light transport");
        True(!city.Demolish(7, 7, out _) && lot.Building == BuildingKind.Road,
            "water road bridge cannot be removed while supporting light transport");
        True(!factory.CanPlace(FactoryKind.Storage, 14, 14, 0, out _), "road bridge still rejects a machine footprint");

        Cell rock = Clear(state, 8, 7, TerrainKind.Rock);
        True(factory.TryPlace(FactoryKind.Drill, 16, 14, 0, out _), "drill uses an actual city rock lot");
        rock.Terrain = TerrainKind.Grass;
        True(!factory.CanPlace(FactoryKind.Drill, 18, 14, 0, out _), "drill rejects a city lot without rock");
    }

    static void PowerAndExportsRequireConnectedRoads()
    {
        GameState state = GameState.CreateNew();
        _ = new Simulation(state);
        var environment = new CityLogistics(state);
        var factory = new FactorySimulation(state.Factory, environment);

        Clear(state, 7, 7, TerrainKind.Grass);
        Clear(state, 8, 7, TerrainKind.Grass);
        True(factory.TryPlace(FactoryKind.PowerInlet, 14, 14, 0, out _), "power inlet can be built before its road arrives");
        True(factory.TryPlace(FactoryKind.Inserter, 16, 14, 0, out _), "unpowered inserter can be laid out in the city");
        factory.InvalidateEnvironment();
        True(factory.PowerAvailable == 0 && !factory.GetAt(16, 14).Powered, "disconnected power inlet supplies no factory power");

        Cell powerRoad = state.Cells[7 * state.Size + 8];
        powerRoad.Building = BuildingKind.Road; powerRoad.Level = 1; powerRoad.Connected = true;
        factory.InvalidateEnvironment();
        True(factory.PowerAvailable == state.Factory.PowerBudget && factory.GetAt(16, 14).Powered,
            "adjacent connected city road activates the power inlet");

        Clear(state, 12, 8, TerrainKind.Grass);
        Clear(state, 12, 9, TerrainKind.Grass);
        True(factory.TryPlace(FactoryKind.ExportDock, 24, 16, 0, out _), "export dock can be staged before road connection");
        FactoryEntity dock = factory.GetAt(24, 16);
        dock.Input[(int)Resource.Tools] = 1;
        True(factory.TakeExports(Resource.Tools, 1) == 0 && dock.Input[(int)Resource.Tools] == 1,
            "disconnected export dock cannot hand items to the city");
        Cell dockRoad = state.Cells[9 * state.Size + 12];
        dockRoad.Building = BuildingKind.Road; dockRoad.Level = 1; dockRoad.Connected = true;
        True(factory.TakeExports(Resource.Tools, 1) == 1 && state.Factory.Exported[(int)Resource.Tools] == 1,
            "road-connected export dock completes a physical handoff");
    }

    static void CityProductionUsesPhysicalBuffersAndInserters()
    {
        GameState state = GameState.CreateNew();
        foreach (Cell home in state.Cells.Where(cell => cell.Building == BuildingKind.House))
        {
            home.Building = BuildingKind.None; home.Level = 0;
        }
        state.Population = 0;
        Cell farm = state.Cells[8 * state.Size + 9];
        farm.Building = BuildingKind.Farm; farm.Level = 1; farm.Terrain = TerrainKind.Grass;
        Cell transportRoad = state.Cells[8 * state.Size + 11];
        transportRoad.Building = BuildingKind.Road; transportRoad.Level = 1;
        Cell mill = Clear(state, 12, 8, TerrainKind.Grass);
        mill.Building = BuildingKind.Mill; mill.Level = 1;
        Cell windmill = Clear(state, 12, 9, TerrainKind.Grass);
        windmill.Building = BuildingKind.Windmill; windmill.Level = 1;

        var city = new Simulation(state);
        var environment = new CityLogistics(state);
        var factory = new FactorySimulation(state.Factory, environment);
        Place(factory, FactoryKind.PowerInlet, 16, 18, 0);
        Place(factory, FactoryKind.Pole, 20, 18, 0);
        Place(factory, FactoryKind.Inserter, 20, 16, 0);
        Place(factory, FactoryKind.Belt, 21, 16, 0);
        Place(factory, FactoryKind.Belt, 22, 16, 0);
        Place(factory, FactoryKind.Inserter, 23, 16, 0);
        factory.InvalidateEnvironment();

        float globalGrain = state.Stock[(int)Resource.Grain];
        city.Tick();
        True(farm.LogisticsOutput[(int)Resource.Grain] >= 3f && Math.Abs(state.Stock[(int)Resource.Grain] - globalGrain) < .001f,
            "configured city producer writes fractional yield to its physical output buffer");
        True(state.Factory.Produced[(int)Resource.Grain] >= 3, "whole city output is counted as physical factory production");

        for (int i = 0; i < 200 && mill.LogisticsInput[(int)Resource.Grain] < 2f; i++) factory.Tick(.1f);
        True(mill.LogisticsInput[(int)Resource.Grain] >= 2f, "inserters and belts deliver real farm items into the city processor");
        float flour = state.Stock[(int)Resource.Flour];
        float millInput = mill.LogisticsInput[(int)Resource.Grain];
        city.Tick();
        True(mill.LogisticsInput[(int)Resource.Grain] < millInput && state.Stock[(int)Resource.Flour] > flour,
            "automated city processor consumes its physical input and produces on a real city tick");
        True(Math.Abs(state.Stock[(int)Resource.Grain] - globalGrain) < .001f,
            "automated processor does not duplicate or consume global grain");

        Cell warehouse = Clear(state, 13, 8, TerrainKind.Grass);
        warehouse.Building = BuildingKind.Warehouse; warehouse.Level = 1; warehouse.Connected = true;
        float tools = state.Stock[(int)Resource.Tools];
        int exported = state.Factory.Exported[(int)Resource.Tools];
        True(environment.TryGive(26, 16, Resource.Tools) && state.Stock[(int)Resource.Tools] == tools + 1 && state.Factory.Exported[(int)Resource.Tools] == exported + 1,
            "connected warehouse hands a physical item into global city stock exactly once");

        Cell market = Clear(state, 13, 9, TerrainKind.Grass);
        market.Building = BuildingKind.Market; market.Level = 1; market.Connected = true;
        True(!environment.TryGive(26, 18, Resource.Ore) && environment.TryGive(26, 18, Resource.Bread),
            "market accepts only bread and tools from physical logistics");
    }

    static void FullOutputBacksUpBeforeInputConsumption()
    {
        GameState state = GameState.CreateNew();
        state.Population = 0;
        foreach (Cell cell in state.Cells.Where(cell => cell.Building == BuildingKind.House)) { cell.Building = BuildingKind.None; cell.Level = 0; }
        Cell mill = Clear(state, 12, 8, TerrainKind.Grass); mill.Building = BuildingKind.Mill; mill.Level = 1;
        Cell inputRoad = state.Cells[8 * state.Size + 11]; inputRoad.Building = BuildingKind.Road; inputRoad.Level = 1;
        Cell outputRoad = Clear(state, 13, 8, TerrainKind.Grass); outputRoad.Building = BuildingKind.Road; outputRoad.Level = 1;
        Cell windmill = Clear(state, 12, 9, TerrainKind.Grass); windmill.Building = BuildingKind.Windmill; windmill.Level = 1;
        var city = new Simulation(state);
        var environment = new CityLogistics(state);
        var factory = new FactorySimulation(state.Factory, environment);
        Place(factory, FactoryKind.Inserter, 23, 16, 0); // drops at the mill
        Place(factory, FactoryKind.Inserter, 26, 16, 0); // picks up from the mill
        mill.LogisticsInput[(int)Resource.Grain] = 10;
        mill.LogisticsOutput[(int)Resource.Flour] = CityLogistics.BufferCapacity;
        float beforeInput = mill.LogisticsInput[(int)Resource.Grain];
        float beforeProgress = mill.Progress;
        city.Tick();
        True(Math.Abs(mill.LogisticsInput[(int)Resource.Grain] - beforeInput) < .001f && Math.Abs(mill.Progress - beforeProgress) < .001f,
            "full automated output backs up atomically before processor input is consumed");
    }

    static void DemolitionRecoversPhysicalBuffers()
    {
        GameState state = GameState.CreateNew();
        var city = new Simulation(state);
        Cell farm = Clear(state, 12, 8, TerrainKind.Grass);
        farm.Building = BuildingKind.Mill; farm.Level = 1;
        farm.LogisticsInput[(int)Resource.Grain] = 1.25f;
        farm.LogisticsOutput[(int)Resource.Flour] = 2.75f;
        float grain = state.Stock[(int)Resource.Grain];
        float flour = state.Stock[(int)Resource.Flour];

        True(city.Demolish(farm.X, farm.Z, out _), "city producer with buffered physical inventory can be demolished");
        True(Math.Abs(state.Stock[(int)Resource.Grain] - grain - 1.25f) < .001f &&
             Math.Abs(state.Stock[(int)Resource.Flour] - flour - 2.75f) < .001f,
            "demolition returns every fractional input and output item to city stock");
        True(CityLogistics.BufferTotal(farm.LogisticsInput) == 0 && CityLogistics.BufferTotal(farm.LogisticsOutput) == 0,
            "demolition clears recovered physical buffers exactly once");
    }

    static void VersionThreeMigrationArchivesAndRecoversOnce()
    {
        GameState state = GameState.CreateNew();
        FactoryState old = FactoryState.CreateEmpty();
        var oldSimulation = new FactorySimulation(old);
        Place(oldSimulation, FactoryKind.Storage, 5, 5, 0);
        Place(oldSimulation, FactoryKind.Belt, 6, 5, 0);
        oldSimulation.GetAt(5, 5).Input[(int)Resource.Grain] = 2;
        oldSimulation.GetAt(6, 5).CargoResource = Resource.Stone;
        old.Recovered[(int)Resource.Bread] = 3;
        old.Produced[(int)Resource.Tools] = 9;
        old.Exported[(int)Resource.Tools] = 4;
        old.ElapsedSeconds = 42.5f;
        MakeLegacy(state,old,3);
        state.Factory = old;
        state.ArchivedFactory = null;
        float coins = state.Coins, timber = state.Stock[(int)Resource.Timber], stone = state.Stock[(int)Resource.Stone];
        float grain = state.Stock[(int)Resource.Grain], bread = state.Stock[(int)Resource.Bread];

        _ = new Simulation(state);

        True(state.Version == 6 && state.Factory != null && state.Factory.Version == 3 && state.ArchivedFactory != null && state.ArchivedFactory.Version == 3 && ReferenceEquals(state.ArchivedFactory, old) && state.Factory.Width == 42 && state.Factory.Height == 42,
            "v3 migration archives the exact old interior and activates shared city geometry");
        True(old.Entities.Count == 2 && old.Entities[0].Input[(int)Resource.Grain] == 2 && old.Entities[1].CargoResource == Resource.Stone,
            "archived v3 DTO is not mutated during migration");
        True(state.Stock[(int)Resource.Grain] == grain + 2 && state.Stock[(int)Resource.Bread] == bread + 3,
            "old buffers and recovered items return to city stock");
        True(state.Coins == coins + 40 && state.Stock[(int)Resource.Timber] == timber + 11 && state.Stock[(int)Resource.Stone] == stone + 3,
            "old cargo and full construction costs are recovered");
        True(state.Factory.Produced[(int)Resource.Tools] == 9 && state.Factory.Exported[(int)Resource.Tools] == 4 && Math.Abs(state.Factory.ElapsedSeconds - 42.5f) < .001f,
            "old physical production statistics survive on the active shared grid");

        FactoryState active = state.Factory;
        float stockTotal = state.Stock.Sum();
        CityLogistics.Migrate(state);
        True(ReferenceEquals(state.Factory, active) && ReferenceEquals(state.ArchivedFactory, old) && Math.Abs(state.Stock.Sum() - stockTotal) < .001f,
            "v4 migration is idempotent and never recovers the archive twice");
    }

    static void VersionFourStateIsStable()
    {
        GameState state = GameState.CreateNew();
        state.Factory.Produced[(int)Resource.Tools] = 2;
        FactoryState active = state.Factory;
        _ = new Simulation(state);
        True(state.Version == 6 && state.Factory.Version == 3 && ReferenceEquals(state.Factory, active) && state.Factory.Produced[(int)Resource.Tools] == 2,
            "valid v4 shared factory state is not reset on load normalization");
    }

    static void SharedScenarioBuildsAgainstTheCoreContract()
    {
        GameState state = SharedCityScenario.Create();
        FactorySimulation.ValidateState(state.Factory);
        True(state.Version == 6 && state.Factory.Version == 3 && state.Factory.Width == 42 && state.Factory.Entities.Count > 0 &&
             state.Cells.Any(cell => cell.Building == BuildingKind.House) &&
             state.Cells.Any(cell => cell.Building == BuildingKind.Warehouse),
            "shared smoke fixture builds houses, city industry, and physical factory equipment together");
    }

    static Cell Clear(GameState state, int x, int z, TerrainKind terrain)
    {
        Cell cell = state.Cells[z * state.Size + x];
        cell.Building = BuildingKind.None; cell.Level = 0; cell.Progress = 0; cell.Status = ""; cell.Terrain = terrain;
        cell.LogisticsInput = Cell.NewLogisticsBuffer(); cell.LogisticsOutput = Cell.NewLogisticsBuffer();
        return cell;
    }

    static void Place(FactorySimulation simulation, FactoryKind kind, int x, int z, int direction)
    {
        if (!simulation.TryPlace(kind, x, z, direction, out string reason)) throw new Exception($"placement failed for {kind} at {x},{z}: {reason}");
    }

    static void MakeLegacy(GameState state,FactoryState factory,int version)
    {
        state.Version=version;
        state.Stock.RemoveRange(ResourceCatalog.LegacyCount,state.Stock.Count-ResourceCatalog.LegacyCount);
        foreach(Cell cell in state.Cells)
        {
            cell.LogisticsInput.RemoveRange(ResourceCatalog.LegacyCount,cell.LogisticsInput.Count-ResourceCatalog.LegacyCount);
            cell.LogisticsOutput.RemoveRange(ResourceCatalog.LegacyCount,cell.LogisticsOutput.Count-ResourceCatalog.LegacyCount);
        }
        factory.Version=1;
        factory.Produced.RemoveRange(ResourceCatalog.LegacyCount,factory.Produced.Count-ResourceCatalog.LegacyCount);
        factory.Exported.RemoveRange(ResourceCatalog.LegacyCount,factory.Exported.Count-ResourceCatalog.LegacyCount);
        factory.Recovered.RemoveRange(ResourceCatalog.LegacyCount,factory.Recovered.Count-ResourceCatalog.LegacyCount);
        foreach(FactoryEntity entity in factory.Entities)
        {
            entity.Input.RemoveRange(ResourceCatalog.LegacyCount,entity.Input.Count-ResourceCatalog.LegacyCount);
            entity.Output.RemoveRange(ResourceCatalog.LegacyCount,entity.Output.Count-ResourceCatalog.LegacyCount);
            entity.Paused=false;entity.ClockPercent=100;entity.FluidProgress=0;entity.FluidCursor=0;
        }
    }

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("city logistics check failed: " + name);
        passed++;
        Console.WriteLine("ok " + name);
    }
}

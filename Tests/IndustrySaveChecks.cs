using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Riverworks;

public static class IndustrySaveChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        LegacyFactoryUpgradePreservesValues();
        LegacyUpgradeRejectsBeforeMutation();
        GameUpgradeExpandsEveryResourceArray();
        CurrentHighResourceStateValidatesStrictly();
        return passed;
    }

    static void LegacyFactoryUpgradePreservesValues()
    {
        FactoryState state = LegacyFactory(42, 42);
        FactoryEntity belt = new FactoryEntity
        {
            Id = 1, Kind = FactoryKind.Belt, X = 2, Z = 2,
            CargoResource = Resource.Tools, CargoProgress = .375f, Progress = 0,
            Input = LegacyInts(), Output = LegacyInts()
        };
        state.Entities.Add(belt);
        state.NextEntityId = 2;
        state.Produced[(int)Resource.Tools] = 17;
        state.Exported[(int)Resource.Bread] = 4;
        state.Recovered[(int)Resource.Grain] = 3;
        state.ElapsedSeconds = 12.625f;

        FactoryStateValidation.UpgradeLegacy(state);
        True(state.Version == 2 && state.Produced.Count == ResourceCatalog.Count && belt.Input.Count == ResourceCatalog.Count,
            "legacy factory arrays expand to the current resource width");
        True(state.Produced[(int)Resource.Tools] == 17 && state.Exported[(int)Resource.Bread] == 4 &&
             state.Recovered[(int)Resource.Grain] == 3 && Math.Abs(state.ElapsedSeconds - 12.625f) < .0001f &&
             belt.CargoResource == Resource.Tools && Math.Abs(belt.CargoProgress - .375f) < .0001f,
            "legacy counters, elapsed time and fractional cargo progress survive");
        True(state.Produced.Skip(ResourceCatalog.LegacyCount).All(value => value == 0) && !belt.Paused &&
             belt.ClockPercent == 100 && belt.FluidProgress == 0 && belt.FluidCursor == 0,
            "new resources and runtime flags receive deterministic defaults");
    }

    static void LegacyUpgradeRejectsBeforeMutation()
    {
        FactoryState bad = LegacyFactory(42, 42);
        bad.Produced.Add(0);
        int width = bad.Produced.Count;
        bool rejected = ThrowsInvalid(() => FactoryStateValidation.UpgradeLegacy(bad));
        True(rejected && bad.Version == 1 && bad.Produced.Count == width,
            "malformed legacy width rejects before any upgrade mutation");

        bad = LegacyFactory(42, 42);
        bad.Entities.Add(new FactoryEntity { Id = 1, Kind = FactoryKind.Pipe, Input = LegacyInts(), Output = LegacyInts() });
        bad.NextEntityId = 2;
        True(ThrowsInvalid(() => FactoryStateValidation.UpgradeLegacy(bad)) && bad.Version == 1,
            "legacy saves cannot smuggle new factory kinds");
    }

    static void GameUpgradeExpandsEveryResourceArray()
    {
        GameState state = GameState.CreateNew();
        state.Version = 4;
        Shrink(state.Stock);
        foreach (Cell cell in state.Cells) { Shrink(cell.LogisticsInput); Shrink(cell.LogisticsOutput); }
        state.Cells[0].LogisticsInput[(int)Resource.Grain] = 1.25f;
        state.Cells[0].LogisticsOutput[(int)Resource.Flour] = 2.75f;
        state.Factory = LegacyFactory(42, 42);
        state.ArchivedFactory = LegacyFactory(24, 16);
        state.Factory.Produced[(int)Resource.Tools] = 9;
        state.ArchivedFactory.Exported[(int)Resource.Bread] = 6;

        IndustryMigration.UpgradeLegacy(state);
        True(state.Version == 5 && state.Stock.Count == ResourceCatalog.Count &&
             state.Cells.All(cell => cell.LogisticsInput.Count == ResourceCatalog.Count && cell.LogisticsOutput.Count == ResourceCatalog.Count) &&
             state.Factory.Version == 2 && state.ArchivedFactory.Version == 2,
            "game migration expands stock, city buffers, active factory and archive");
        True(Math.Abs(state.Cells[0].LogisticsInput[(int)Resource.Grain] - 1.25f) < .0001f &&
             Math.Abs(state.Cells[0].LogisticsOutput[(int)Resource.Flour] - 2.75f) < .0001f &&
             state.Factory.Produced[(int)Resource.Tools] == 9 && state.ArchivedFactory.Exported[(int)Resource.Bread] == 6,
            "fractional city buffers and aggregate factory counters survive migration");
        True(state.Stock.Skip(ResourceCatalog.LegacyCount).All(value => value == 0),
            "legacy migration grants no newly added resource");
    }

    static void CurrentHighResourceStateValidatesStrictly()
    {
        FactoryState state = FactoryState.CreateCityGrid();
        state.Produced[(int)Resource.ControlUnit] = 3;
        state.Recovered[(int)Resource.Fuel] = 11;
        FactoryStateValidation.Validate(state);
        True(state.Produced[(int)Resource.ControlUnit] == 3 && state.Recovered[(int)Resource.Fuel] == 11,
            "current aggregate inventories preserve highest resource ids");
        state.Exported.RemoveAt(state.Exported.Count - 1);
        True(ThrowsInvalid(() => FactoryStateValidation.Validate(state)),
            "current saves reject truncated resource arrays");
    }

    static FactoryState LegacyFactory(int width, int height) => new FactoryState
    {
        Version = 1, Width = width, Height = height, NextEntityId = 1,
        Entities = new List<FactoryEntity>(), Produced = LegacyInts(), Exported = LegacyInts(), Recovered = LegacyInts(),
        PowerBudget = 20, ElapsedSeconds = 0
    };

    static List<int> LegacyInts() => new List<int>(new int[ResourceCatalog.LegacyCount]);
    static void Shrink(List<float> values) => values.RemoveRange(ResourceCatalog.LegacyCount, values.Count - ResourceCatalog.LegacyCount);
    static bool ThrowsInvalid(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    static void True(bool value, string name) { if (!value) throw new Exception("industry save check failed: " + name); passed++; Console.WriteLine("ok " + name); }
}

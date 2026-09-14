using System;
using System.IO;
using System.Linq;
using Riverworks;

public static class FactoryLayerChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        GroundApiAndFloorIsolation();
        PlatformsAreAtomicAndStructural();
        ItemLiftIsPairedPoweredAndOneHop();
        LayeredPowerUsesAlignedPolesOnly();
        RemovalRecoversBothEndpointsAndController();
        CityAutomationIgnoresUpperInserters();
        StoppedSourcesCannotBeDrainedByInserters();
        return passed;
    }

    static void GroundApiAndFloorIsolation()
    {
        FactoryState state = FactoryState.CreateEmpty();
        state.Platforms.Add(new FactoryPlatform { X = 4, Z = 4, Floor = 1 });
        var simulation = new FactorySimulation(state);
        True(simulation.TryPlace(FactoryKind.Belt, 4, 4, 0, out _), "legacy placement API creates a ground entity");
        True(simulation.TryPlace(FactoryKind.Belt, 4, 4, 0, 1, out _), "same coordinates can contain an upper entity");
        Equal(0, simulation.GetAt(4, 4).Floor, "legacy lookup delegates to ground floor");
        Equal(1, simulation.GetAt(4, 4, 1).Floor, "floor lookup selects the upper entity");
        True(!simulation.TryPlace(FactoryKind.Belt, 6, 4, 0, 1, out _), "unsupported upper placement is rejected");
        True(!simulation.TryPlace(FactoryKind.ImportDock, 4, 4, 0, 1, out _), "ground-only equipment is rejected upstairs");
        True(simulation.CanPlace(FactoryKind.ItemLift, 6, 4, 0, 0, out _), "link endpoint preview can validate one floor");
        True(!simulation.TryPlace(FactoryKind.ItemLift, 6, 4, 0, 0, out _), "ordinary placement cannot create an orphan link endpoint");
    }

    static void PlatformsAreAtomicAndStructural()
    {
        GameState game = GameState.CreateNew();
        game.Technologies.Add(TechId.MassProduction); game.Technologies.Add(TechId.AdvancedManufacturing);
        game.Stock[(int)Resource.SteelBeam] = 10; game.Stock[(int)Resource.ModularFrame] = 10;
        float coins = game.Coins, beams = game.Stock[(int)Resource.SteelBeam], frames = game.Stock[(int)Resource.ModularFrame];
        True(FactoryLayers.TryPlacePlatform(game, 16, 16, 1, out _), "owned empty lot accepts floor-one platform");
        Near(coins - FactoryLayers.CoinCost, game.Coins, "platform deducts exact coins");
        Near(beams - FactoryLayers.SteelBeamCost, game.Stock[(int)Resource.SteelBeam], "platform deducts exact beams");
        Near(frames - FactoryLayers.ModularFrameCost, game.Stock[(int)Resource.ModularFrame], "platform deducts exact frame");
        True(FactoryLayers.TryPlacePlatform(game, 16, 16, 2, out _), "floor two accepts matching floor-one support");
        True(!FactoryLayers.RemovePlatform(game, 16, 16, 1, out _), "lower platform cannot be removed below an upper platform");
        True(FactoryLayers.RemovePlatform(game, 16, 16, 2, out _), "upper platform removes safely");
        True(!FactoryLayers.CanPlacePlatform(game, 0, 0, 1, out _), "unowned city lot rejects a platform");
        True(!FactoryLayers.CanPlacePlatform(game, 18, 18, 1, out _), "occupied city building lot rejects a platform");
        True(!CityLogistics.CanBuildCity(game, BuildingKind.House, 8, 8, out _), "platform blocks a city building");
        True(CityLogistics.CanBuildCity(game, BuildingKind.Road, 8, 8, out _), "road may remain below a platform");

        FactoryState malformed = FactoryState.CreateEmpty();
        malformed.Platforms.Add(new FactoryPlatform { X = 2, Z = 2, Floor = 2 });
        Throws<InvalidDataException>(() => FactoryLayers.Validate(malformed), "floor-two platform without lower support is invalid");
    }

    static void ItemLiftIsPairedPoweredAndOneHop()
    {
        FactoryState state = LayeredLiftState(out FactorySimulation simulation, out FactoryEntity sender, out FactoryEntity receiver, out FactoryEntity upperBelt);
        FactoryEntity groundBelt = simulation.GetAt(3, 4, 0);
        FactoryEntity wrongPlanar = simulation.GetAt(3, 4, 1);
        wrongPlanar.CargoResource = Resource.Stone; wrongPlanar.CargoProgress = 1;
        simulation.Tick(1);
        True(wrongPlanar.CargoResource == Resource.Stone && receiver.CargoResource == Resource.Coins, "receiver rejects planar incoming cargo");

        groundBelt.CargoResource = Resource.Steel; groundBelt.CargoProgress = 1;
        simulation.Tick(1);
        True(groundBelt.CargoResource == Resource.Coins && sender.CargoResource == Resource.Steel, "ground belt feeds the lift sender through planar transport");
        True(receiver.CargoResource == Resource.Coins, "cargo newly received by sender cannot move vertically in the same tick");
        Equal(1, CargoCount(state, Resource.Steel), "planar sender intake conserves cargo");
        simulation.Tick(1);
        True(sender.CargoResource == Resource.Coins && receiver.CargoResource == Resource.Steel, "powered sender moves the belt-fed item vertically on the next tick");
        True(upperBelt.CargoResource == Resource.Coins, "freshly received lift cargo cannot move again in the same tick");
        Equal(1, CargoCount(state, Resource.Steel), "vertical transfer conserves cargo");
        simulation.Tick(1);
        True(receiver.CargoResource == Resource.Coins && upperBelt.CargoResource == Resource.Steel, "receiver emits on the following tick");
        Equal(1, CargoCount(state, Resource.Steel), "upper planar output conserves cargo end to end");

        upperBelt.CargoResource = Resource.Coins; upperBelt.CargoProgress = 0;
        sender.CargoResource = Resource.Wire; sender.CargoProgress = 0;
        simulation.SetPaused(sender.Id, true, out _); simulation.Tick(2);
        True(sender.CargoResource == Resource.Wire && receiver.CargoResource == Resource.Coins, "paused sender preserves vertical cargo and progress");
        simulation.SetPaused(sender.Id, false, out _);
        Equal(2, state.Entities.Count(entity => entity.Kind == FactoryKind.ItemLift), "paired lift has exactly two endpoints");
    }

    static void LayeredPowerUsesAlignedPolesOnly()
    {
        FactoryEntity lower = Entity(1, FactoryKind.Pole, 2, 2, 0);
        FactoryEntity aligned = Entity(2, FactoryKind.Pole, 2, 2, 1);
        FactoryEntity offset = Entity(3, FactoryKind.Pole, 3, 2, 1);
        Equal(1, FactorySimulation.GridDistance(lower, aligned), "aligned adjacent-floor poles bridge vertically");
        Equal(int.MaxValue, FactorySimulation.GridDistance(lower, offset), "offset poles cannot bridge floors");
        FactoryEntity machine = Entity(4, FactoryKind.Assembler, 2, 2, 1);
        Equal(int.MaxValue, FactorySimulation.GridDistance(lower, machine), "a lower pole cannot directly power an upper machine");
    }

    static void RemovalRecoversBothEndpointsAndController()
    {
        FactoryState state = LayeredLiftState(out FactorySimulation simulation, out FactoryEntity sender, out FactoryEntity receiver, out _);
        sender.CargoResource = Resource.Gear; receiver.CargoResource = Resource.Wire; sender.ControllerInstalled = true;
        state.AutomationRules.Add(new AutomationRule { Id = 1, SourceEntityId = 0, TargetEntityId = sender.Id, Resource = Resource.Gear, Comparison = AutomationComparison.AtLeast, Action = AutomationAction.AllowWhenTrue, Threshold = 1 });
        state.NextAutomationRuleId = 2;
        True(simulation.Remove(sender.X, sender.Z, sender.Floor, out _), "removing one lift endpoint removes the reciprocal pair");
        Equal(0, state.Entities.Count(entity => entity.Kind == FactoryKind.ItemLift), "no orphan lift endpoint remains");
        Equal(1, state.Recovered[(int)Resource.Gear], "sender cargo is recovered");
        Equal(1, state.Recovered[(int)Resource.Wire], "receiver cargo is recovered");
        Equal(1, state.Recovered[(int)Resource.ControlUnit], "installed controller is recovered once");
        Equal(0, state.AutomationRules.Count, "rules referencing removed endpoints are cleared");
    }

    static void CityAutomationIgnoresUpperInserters()
    {
        GameState game = GameState.CreateNew(); Cell cell = game.Cells[10 * game.Size + 10];
        game.Factory.Entities.Clear();
        game.Factory.Entities.Add(Entity(1, FactoryKind.Inserter, cell.X * CityLogistics.Resolution + 1, cell.Z * CityLogistics.Resolution, 1));
        True(!CityLogistics.HasAutomatedOutput(game, cell) && !CityLogistics.HasAutomatedInput(game, cell), "city automatic IO ignores upper inserters");
    }

    static void StoppedSourcesCannotBeDrainedByInserters()
    {
        VerifyStoppedStorage(false);
        VerifyStoppedStorage(true);
    }

    static void VerifyStoppedStorage(bool automated)
    {
        FactoryState state = FactoryState.CreateEmpty(); state.PowerBudget = 100;
        var simulation = new FactorySimulation(state);
        FactoryEntity storage = Place(simulation, FactoryKind.Storage, 1, 4, 0, 0);
        FactoryEntity inserter = Place(simulation, FactoryKind.Inserter, 2, 4, 0, 0);
        Place(simulation, FactoryKind.Belt, 3, 4, 0, 0);
        Place(simulation, FactoryKind.PowerInlet, 2, 1, 0, 0);
        storage.Input[(int)Resource.Steel] = 1;
        if (automated)
        {
            storage.ControllerInstalled = true;
            state.AutomationRules.Add(new AutomationRule
            {
                Id = 1, SourceEntityId = storage.Id, TargetEntityId = storage.Id, Resource = Resource.Steel,
                Comparison = AutomationComparison.AtLeast, Action = AutomationAction.StopWhenTrue, Threshold = 0
            });
            state.NextAutomationRuleId = 2;
        }
        else simulation.SetPaused(storage.Id, true, out _);

        simulation.Tick(1);
        Equal(1, storage.Input[(int)Resource.Steel], automated ? "automation-stopped storage preserves inventory" : "manually paused storage preserves inventory");
        True(inserter.CargoResource == Resource.Coins, automated ? "automation-stopped source cannot be drained" : "paused source cannot be drained");

        if (automated) state.AutomationRules.Clear();
        else simulation.SetPaused(storage.Id, false, out _);
        simulation.Tick(1);
        Equal(0, storage.Input[(int)Resource.Steel], automated ? "removing automation stop permits intake" : "resuming source permits intake");
        True(inserter.CargoResource == Resource.Steel, "released source transfers its preserved item");
    }

    static FactoryState LayeredLiftState(out FactorySimulation simulation, out FactoryEntity sender, out FactoryEntity receiver, out FactoryEntity upperBelt)
    {
        FactoryState state = FactoryState.CreateEmpty(); state.PowerBudget = 100;
        state.Platforms.Add(new FactoryPlatform { X = 2, Z = 4, Floor = 1 });
        state.Platforms.Add(new FactoryPlatform { X = 4, Z = 4, Floor = 1 });
        simulation = new FactorySimulation(state);
        Place(simulation, FactoryKind.PowerInlet, 0, 4, 0, 0);
        Place(simulation, FactoryKind.Pole, 2, 4, 0, 0);
        Place(simulation, FactoryKind.Pole, 2, 4, 0, 1);
        Place(simulation, FactoryKind.Belt, 3, 4, 0, 0);
        Place(simulation, FactoryKind.Belt, 3, 4, 0, 1);
        True(simulation.TryPlaceLink(FactoryKind.ItemLift, 4, 4, 0, 0, 1, out string reason), "paired item lift placement: " + reason);
        upperBelt = Place(simulation, FactoryKind.Belt, 5, 4, 0, 1);
        sender = state.Entities.Single(entity => entity.Kind == FactoryKind.ItemLift && entity.IsLinkSender);
        receiver = state.Entities.Single(entity => entity.Kind == FactoryKind.ItemLift && !entity.IsLinkSender);
        return state;
    }

    static FactoryEntity Place(FactorySimulation simulation, FactoryKind kind, int x, int z, int direction, int floor)
    {
        True(simulation.TryPlace(kind, x, z, direction, floor, out string reason), $"place {kind} on floor {floor}: {reason}");
        return simulation.GetAt(x, z, floor);
    }

    static FactoryEntity Entity(int id, FactoryKind kind, int x, int z, int floor) => new FactoryEntity
    { Id = id, Kind = kind, X = x, Z = z, Floor = floor, Direction = 0, ClockPercent = 100 };
    static int CargoCount(FactoryState state, Resource resource) => state.Entities.Count(entity => entity.CargoResource == resource);
    static void True(bool value, string name) { if (!value) throw new Exception("factory layer check failed: " + name); passed++; }
    static void Equal(int expected, int actual, string name) => True(expected == actual, $"{name} (expected {expected}, actual {actual})");
    static void Near(float expected, float actual, string name) => True(Math.Abs(expected - actual) < .001f, $"{name} (expected {expected}, actual {actual})");
    static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { passed++; return; }
        throw new Exception("factory layer check failed: " + name);
    }
}

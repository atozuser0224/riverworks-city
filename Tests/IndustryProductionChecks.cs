using System;
using System.Linq;
using Riverworks;

public static class IndustryProductionChecks
{
    static int passed;

    public static int Run()
    {
        passed = 0;
        MultiInputAndOutputAreAtomic();
        ProgressPauseAndClockAreStable();
        RecipeChangesRecoverWithoutPartialMutation();
        SolidAndFluidBoundariesAreEnforced();
        AutoInserterLeavesWrongMaterialAtSource();
        LargeDeltaIsBoundedByRealBuffers();
        return passed;
    }

    static void MultiInputAndOutputAreAtomic()
    {
        var state = FactoryState.CreateEmpty();
        var sim = new FactorySimulation(state);
        FactoryEntity refinery = Place(sim, FactoryKind.Refinery, 4, 4);
        Place(sim, FactoryKind.PowerInlet, 1, 4);
        True(sim.SetRecipe(refinery.Id, FactoryRecipe.OilRefining, out _), "refinery accepts its catalog recipe");
        refinery.Input[(int)Resource.CrudeOil] = 8;
        refinery.Input[(int)Resource.Water] = 4;
        refinery.Output[(int)Resource.HeavyOil] = 76;
        sim.Recalculate();
        sim.Tick(20);
        Equal(8, refinery.Input[(int)Resource.CrudeOil], "blocked byproduct preserves crude oil");
        Equal(4, refinery.Input[(int)Resource.Water], "blocked byproduct preserves water");
        Equal(76, refinery.Output[(int)Resource.HeavyOil], "blocked batch creates no primary output");
        Equal(0, refinery.Output[(int)Resource.PetroleumGas], "blocked batch creates no byproduct");
        True(refinery.Status.Contains("부산물") || refinery.Status.Contains("출력"), "blocked batch exposes a readable status");

        refinery.Output[(int)Resource.HeavyOil] = 0;
        sim.Tick(20);
        Equal(0, refinery.Input[(int)Resource.CrudeOil], "successful multi-input batch consumes crude oil");
        Equal(0, refinery.Input[(int)Resource.Water], "successful multi-input batch consumes water");
        Equal(3, refinery.Output[(int)Resource.HeavyOil], "successful batch creates primary fluid");
        Equal(5, refinery.Output[(int)Resource.PetroleumGas], "successful batch creates byproduct fluid");
    }

    static void ProgressPauseAndClockAreStable()
    {
        var state = FactoryState.CreateEmpty(); state.PowerBudget = 100;
        var sim = new FactorySimulation(state);
        FactoryEntity machine = Place(sim, FactoryKind.Foundry, 4, 4);
        Place(sim, FactoryKind.PowerInlet, 1, 4);
        True(sim.SetRecipe(machine.Id, FactoryRecipe.AlloySteel, out _), "foundry recipe selected");
        machine.Input[(int)Resource.Ore] = 30; machine.Input[(int)Resource.Coal] = 10;
        True(sim.SetClock(machine.Id, 50, out _), "base underclock is available");
        sim.Tick(2);
        Near(1, machine.Progress, "50 percent clock advances at half speed");
        float stored = machine.Progress;
        True(sim.SetPaused(machine.Id, true, out _), "machine can pause");
        sim.Tick(5);
        Near(stored, machine.Progress, "pause preserves production progress");
        True(!machine.Powered && sim.PowerUsed == 0, "paused machine uses no power");
        True(sim.SetPaused(machine.Id, false, out _), "machine can resume");

        var game = GameState.CreateNew();
        game.Technologies.Add(TechId.AdvancedManufacturing);
        sim.ConfigureTechnology(game);
        True(sim.SetClock(machine.Id, 100, out _), "normal clock selected");
        sim.Recalculate(); float normalPower = sim.PowerUsed;
        True(sim.SetClock(machine.Id, 200, out _), "researched overclock selected");
        sim.Recalculate();
        Near(normalPower * 4, sim.PowerUsed, "200 percent clock has quadratic power demand");

        var locked = new FactorySimulation(FactoryState.CreateEmpty());
        FactoryEntity lockedMachine = Place(locked, FactoryKind.Foundry, 4, 4);
        locked.ConfigureTechnology(GameState.CreateNew());
        True(!locked.SetClock(lockedMachine.Id, 150, out _), "overclock is technology gated");
    }

    static void RecipeChangesRecoverWithoutPartialMutation()
    {
        var state = FactoryState.CreateEmpty();
        var sim = new FactorySimulation(state);
        FactoryEntity machine = Place(sim, FactoryKind.MachiningBench, 4, 4);
        True(sim.SetRecipe(machine.Id, FactoryRecipe.GearCutting, out _), "initial machining recipe selected");
        machine.Input[(int)Resource.Steel] = 7;
        machine.Output[(int)Resource.Gear] = 5;
        machine.Progress = 1;
        True(!sim.SetRecipe(machine.Id, FactoryRecipe.WireDrawing, out _), "clean-buffer recipe change rejects occupied machine");
        Equal(7, machine.Input[(int)Resource.Steel], "rejected recipe change preserves input");
        True(!sim.ReconfigureRecipe(machine.Id, FactoryRecipe.OilRefining, out _), "incompatible reconfigure rejects before recovery");
        Equal(0, state.Recovered[(int)Resource.Steel], "invalid reconfigure performs no partial recovery");
        True(sim.ReconfigureRecipe(machine.Id, FactoryRecipe.WireDrawing, out _), "compatible reconfigure succeeds");
        Equal(7, state.Recovered[(int)Resource.Steel], "reconfigure recovers all input");
        Equal(5, state.Recovered[(int)Resource.Gear], "reconfigure recovers all output");
        Near(0, machine.Progress, "reconfigure resets progress");
    }

    static void SolidAndFluidBoundariesAreEnforced()
    {
        var state = FactoryState.CreateEmpty();
        var sim = new FactorySimulation(state);
        FactoryEntity storage = Place(sim, FactoryKind.Storage, 1, 1);
        FactoryEntity tank = Place(sim, FactoryKind.FluidTank, 3, 1);
        FactoryEntity inserter = Place(sim, FactoryKind.Inserter, 5, 1);
        FactoryEntity filteredTank = Place(sim, FactoryKind.FluidTank, 3, 3);
        True(!sim.AddInput(storage.Id, Resource.Water, 1, out _), "solid storage rejects fluid");
        True(!sim.AddInput(tank.Id, Resource.Steel, 1, out _), "tank rejects solid");
        True(sim.AddInput(tank.Id, Resource.Water, 10, out _), "tank accepts fluid");
        True(!sim.AddInput(tank.Id, Resource.CrudeOil, 1, out _), "tank rejects fluid mixing");
        True(!sim.SetFilter(tank.Id, Resource.CrudeOil, out _), "occupied tank rejects incompatible filter");
        True(!sim.SetFilter(inserter.Id, Resource.Water, out _), "inserter rejects fluid filter");
        True(sim.SetFilter(filteredTank.Id, Resource.Water, out _), "empty tank accepts a fixed fluid filter");
        True(sim.AddInput(filteredTank.Id, Resource.Water, 4, out _), "fixed-filter tank accepts its matching fluid");
        True(sim.AddInput(filteredTank.Id, Resource.Water, 2, out _), "fixed-filter tank accepts more of the same fluid");
        True(!sim.AddInput(filteredTank.Id, Resource.CrudeOil, 3, out _), "fixed-filter tank rejects a different fluid");
        Equal(6, filteredTank.Input[(int)Resource.Water], "rejected filtered input preserves matching fluid quantity");
        Equal(0, filteredTank.Input[(int)Resource.CrudeOil], "rejected filtered input adds no incompatible fluid");
        Equal(0, sim.TakeExports(Resource.Water), "fluid can never leave through item export");
        True(sim.Remove(tank.X, tank.Z, out _), "fluid tank can be demolished");
        Equal(10, state.Recovered[(int)Resource.Water], "demolition recovers all fluid");
    }

    static void LargeDeltaIsBoundedByRealBuffers()
    {
        var state = FactoryState.CreateEmpty(); state.PowerBudget = 100;
        var sim = new FactorySimulation(state);
        FactoryEntity drill = Place(sim, FactoryKind.Drill, 1, 7);
        Place(sim, FactoryKind.PowerInlet, 4, 7);
        sim.Tick(1000000);
        int capacity = FactoryCatalog.Get(FactoryKind.Drill).OutputCapacity;
        Equal(capacity, drill.Output[(int)Resource.Ore], "large delta cannot exceed output capacity");
        Equal(capacity, state.Produced[(int)Resource.Ore], "large delta records only real produced units");
        True(drill.Progress >= 0 && drill.Progress < FactoryCatalog.GetRecipe(FactoryRecipe.IronMining).Duration, "large-delta progress remains valid");
    }

    static void AutoInserterLeavesWrongMaterialAtSource()
    {
        var state = FactoryState.CreateEmpty(); state.PowerBudget = 100;
        var sim = new FactorySimulation(state);
        FactoryEntity belt = Place(sim, FactoryKind.Belt, 1, 4);
        FactoryEntity inserter = Place(sim, FactoryKind.Inserter, 2, 4);
        FactoryEntity assembler = Place(sim, FactoryKind.Assembler, 3, 4);
        Place(sim, FactoryKind.PowerInlet, 2, 1);
        True(sim.SetRecipe(assembler.Id, FactoryRecipe.CircuitPrinting, out _), "multi-input assembler recipe selected");
        belt.CargoResource = Resource.Steel; belt.CargoProgress = 1;
        sim.Tick(1);
        True(belt.CargoResource == Resource.Steel && inserter.CargoResource == Resource.Coins, "auto inserter leaves an unneeded mixed-belt item at its source");
        belt.CargoResource = Resource.Copper; belt.CargoProgress = 0;
        sim.Tick(1);
        True(inserter.CargoResource == Resource.Copper && belt.CargoResource == Resource.Coins, "auto inserter picks a needed copper ingredient without loss");
        sim.Tick(1);
        Equal(1, assembler.Input[(int)Resource.Copper], "needed ingredient reaches its multi-input machine");
        belt.CargoResource = Resource.Wire; belt.CargoProgress = 0;
        sim.Tick(1);
        True(inserter.CargoResource == Resource.Wire && belt.CargoResource == Resource.Coins, "auto inserter also picks a second needed ingredient");
        sim.Tick(1);
        Equal(1, assembler.Input[(int)Resource.Wire], "wire reaches the machine without losing the earlier copper");
        Equal(1, assembler.Input[(int)Resource.Copper], "later intake preserves the earlier ingredient");
    }

    static FactoryEntity Place(FactorySimulation simulation, FactoryKind kind, int x, int z)
    {
        True(simulation.TryPlace(kind, x, z, 0, out string reason), $"place {kind}: {reason}");
        return simulation.GetAt(x, z);
    }

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("industry production check failed: " + name);
        passed++;
    }
    static void Equal(int expected, int actual, string name) => True(expected == actual, $"{name} (expected {expected}, actual {actual})");
    static void Near(float expected, float actual, string name) => True(Math.Abs(expected - actual) < .001f, $"{name} (expected {expected}, actual {actual})");
}

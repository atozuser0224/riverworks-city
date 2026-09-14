using System;
using System.Collections.Generic;
using System.Linq;
using Riverworks;

public static class IndustryFluidChecks
{
    static int passed;
    public static int Run()
    {
        passed = 0;
        RotatedMachinePortsAreOutsideFootprint();
        SerialPipesUseTickStartSnapshot();
        JunctionBranchesFairly();
        MixedFluidIsRejectedWithoutLoss();
        MachineInputsRequireTheExactRecipePort();
        ByproductOutputUsesItsOwnPort();
        PausedValvesStopFlowAndStoredOutputsCanDrain();
        ProgressRemainsFiniteAndBounded();
        return passed;
    }

    static void RotatedMachinePortsAreOutsideFootprint()
    {
        FactoryEntity refinery = Entity(1, FactoryKind.Refinery, 10, 10, 1, FactoryRecipe.OilRefining);
        FluidPort[] ports = FluidPorts.For(refinery).ToArray();
        True(ports.Length == 4, "refinery exposes two fluid inputs and two fluid outputs");
        True(ports[0].IsInput && ports[0].Resource == Resource.CrudeOil && ports[0].Z == 9, "north-facing rear crude port rotates outside footprint");
        True(ports[1].IsInput && ports[1].Resource == Resource.Water && ports[1].Z == 9 && ports[1].X != ports[0].X, "ordered second input has a separate rotated slot");
        True(!ports[2].IsInput && ports[2].Resource == Resource.HeavyOil && ports[2].Z == 12, "north-facing heavy-oil output is in front");
        True(!ports[3].IsInput && ports[3].Resource == Resource.PetroleumGas && ports[3].Z == 12, "byproduct gets the second front port");
    }

    static void SerialPipesUseTickStartSnapshot()
    {
        FactoryEntity source = Entity(1, FactoryKind.Pipe, 1, 1, 0);
        FactoryEntity next = Entity(2, FactoryKind.Pipe, 2, 1, 0);
        FactoryEntity tank = Entity(3, FactoryKind.FluidTank, 3, 1, 0);
        source.Input[(int)Resource.Water] = 8;
        FactoryState state = State(source, next, tank);
        var simulation = new FactoryFluidSimulation(state);
        simulation.Tick(1f);
        True(source.Input[(int)Resource.Water] == 0 && next.Input[(int)Resource.Water] == 8, "first pipe advances its tick-start water");
        True(tank.Input[(int)Resource.Water] == 0, "newly received water cannot cross a second pipe in the same tick");
        simulation.Tick(1f);
        True(tank.Input[(int)Resource.Water] == 8, "water advances on the following tick");
    }

    static void JunctionBranchesFairly()
    {
        FactoryEntity junction = Entity(1, FactoryKind.PipeJunction, 5, 5, 0);
        FactoryEntity front = Entity(2, FactoryKind.Pipe, 6, 5, 0);
        FactoryEntity left = Entity(3, FactoryKind.Pipe, 5, 4, 3);
        junction.Input[(int)Resource.Water] = 16;
        var simulation = new FactoryFluidSimulation(State(junction, front, left));
        simulation.Tick(1f);
        simulation.Tick(1f);
        True(front.Input[(int)Resource.Water] > 0 && left.Input[(int)Resource.Water] > 0, "junction rotates its front and left choices");
        True(front.Input[(int)Resource.Water] + left.Input[(int)Resource.Water] == 16, "branching neither duplicates nor loses fluid");
    }

    static void MixedFluidIsRejectedWithoutLoss()
    {
        FactoryEntity water = Entity(1, FactoryKind.Pipe, 1, 1, 0);
        FactoryEntity oil = Entity(2, FactoryKind.Pipe, 2, 1, 0);
        water.Input[(int)Resource.Water] = 5;
        oil.Input[(int)Resource.CrudeOil] = 3;
        new FactoryFluidSimulation(State(water, oil)).Tick(1f);
        True(water.Input[(int)Resource.Water] == 5 && oil.Input[(int)Resource.CrudeOil] == 3, "a container holding oil rejects water without loss");
        True(water.FluidProgress >= 0f && water.FluidProgress <= 1f, "blocked flow credit stays bounded");
    }

    static void MachineInputsRequireTheExactRecipePort()
    {
        FactoryEntity refinery = Entity(3, FactoryKind.Refinery, 3, 3, 0, FactoryRecipe.OilRefining);
        FactoryEntity crude = Entity(1, FactoryKind.Pipe, 2, 3, 0);
        FactoryEntity water = Entity(2, FactoryKind.Pipe, 2, 4, 0);
        crude.Input[(int)Resource.CrudeOil] = 8;
        water.Input[(int)Resource.Water] = 4;
        new FactoryFluidSimulation(State(crude, water, refinery)).Tick(1f);
        True(refinery.Input[(int)Resource.CrudeOil] == 8 && refinery.Input[(int)Resource.Water] == 4, "each rear slot accepts its matching recipe fluid");

        FactoryEntity wrong = Entity(4, FactoryKind.Pipe, 2, 3, 0);
        wrong.Input[(int)Resource.Water] = 1;
        FactoryEntity cleanRefinery = Entity(5, FactoryKind.Refinery, 3, 3, 0, FactoryRecipe.OilRefining);
        new FactoryFluidSimulation(State(wrong, cleanRefinery)).Tick(1f);
        True(cleanRefinery.Input[(int)Resource.Water] == 0 && wrong.Input[(int)Resource.Water] == 1, "water cannot enter the crude-oil slot");
    }

    static void ByproductOutputUsesItsOwnPort()
    {
        FactoryEntity refinery = Entity(1, FactoryKind.Refinery, 5, 5, 0, FactoryRecipe.ScrapRefining);
        FactoryEntity first = Entity(2, FactoryKind.Pipe, 7, 5, 0);
        FactoryEntity second = Entity(3, FactoryKind.Pipe, 7, 6, 0);
        refinery.Output[(int)Resource.AluminumScrap] = 6;
        refinery.Output[(int)Resource.Water] = 2;
        new FactoryFluidSimulation(State(refinery, first, second)).Tick(1f);
        True(first.Input[(int)Resource.Water] == 2, "solid main output does not consume a fluid port slot");
        True(second.Input[(int)Resource.Water] == 0 && refinery.Output[(int)Resource.Water] == 0, "the sole fluid byproduct uses ordered fluid output slot zero");

        FluidPort waterPort = FluidPorts.For(refinery).Single(port => !port.IsInput && port.Resource == Resource.Water);
        True(waterPort.X == 7 && waterPort.Z == 5, "fluid ordering ignores solid outputs for byproduct return loops");
        FactoryEntity refineryAgain = Entity(5, FactoryKind.Refinery, 5, 5, 0, FactoryRecipe.ScrapRefining);
        refineryAgain.Output[(int)Resource.Water] = 2;
        FactoryEntity returnPipe = Entity(4, FactoryKind.Pipe, waterPort.X, waterPort.Z, 0);
        new FactoryFluidSimulation(State(refineryAgain, returnPipe)).Tick(1f);
        True(returnPipe.Input[(int)Resource.Water] == 2, "fluid byproduct enters the exact return-loop port");
    }

    static void PausedValvesStopFlowAndStoredOutputsCanDrain()
    {
        FactoryEntity paused = Entity(1, FactoryKind.Pipe, 1, 1, 0);
        FactoryEntity sink = Entity(2, FactoryKind.FluidTank, 2, 1, 0);
        paused.Input[(int)Resource.Water] = 4;
        paused.Paused = true;
        new FactoryFluidSimulation(State(paused, sink)).Tick(1f);
        True(paused.Input[(int)Resource.Water] == 4 && sink.Input[(int)Resource.Water] == 0, "paused valves stop transport");

        FactoryEntity pump = Entity(3, FactoryKind.WaterPump, 5, 5, 0, FactoryRecipe.WaterExtraction);
        FactoryEntity pipe = Entity(4, FactoryKind.Pipe, 7, 5, 0);
        pump.Output[(int)Resource.Water] = 4;
        pump.Powered = false;
        new FactoryFluidSimulation(State(pump, pipe)).Tick(1f);
        True(pump.Output[(int)Resource.Water] == 0 && pipe.Input[(int)Resource.Water] == 4, "already-produced output can drain without machine power");
    }

    static void ProgressRemainsFiniteAndBounded()
    {
        FactoryEntity pipe = Entity(1, FactoryKind.Pipe, 1, 1, 0);
        pipe.Input[(int)Resource.Fuel] = 1;
        pipe.FluidProgress = float.NaN;
        new FactoryFluidSimulation(State(pipe)).Tick(100000f);
        True(!float.IsNaN(pipe.FluidProgress) && !float.IsInfinity(pipe.FluidProgress) && pipe.FluidProgress >= 0f && pipe.FluidProgress <= 1f, "saved fluid progress normalizes to finite bounded credit");
        True(pipe.FluidCursor >= 0 && pipe.FluidCursor <= 3, "fairness cursor remains in its save range");
    }

    static FactoryEntity Entity(int id, FactoryKind kind, int x, int z, int direction, FactoryRecipe recipe = FactoryRecipe.None)
    {
        return new FactoryEntity { Id = id, Kind = kind, X = x, Z = z, Direction = direction, Recipe = recipe, Powered = true };
    }

    static FactoryState State(params FactoryEntity[] entities)
    {
        var state = FactoryState.CreateEmpty();
        state.Width = state.Height = 24;
        state.Entities = new List<FactoryEntity>(entities);
        state.NextEntityId = entities.Length == 0 ? 1 : entities.Max(entity => entity.Id) + 1;
        return state;
    }

    static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Industry fluid check failed: " + message);
        passed++;
        Console.WriteLine("ok " + message);
    }
}

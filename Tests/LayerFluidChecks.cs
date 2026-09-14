using System;
using System.Collections.Generic;
using System.Linq;
using Riverworks;

public static class LayerFluidChecks
{
    static int passed;
    public static int Run()
    {
        passed=0;
        PortsCarryMachineFloorAndKeepLegacyConstructor();
        CoincidentFloorsDoNotConnect();
        RiserMovesOnlySenderToReciprocalReceiver();
        RiserRequiresBothEndpointsPoweredAndRunning();
        VerticalTransferUsesTickStartSnapshot();
        RiserTypingRejectsMixedFluid();
        AutomationStopsOrdinarySourcesAndTargets();
        PreproducedMachineFluidDrainsWithoutPower();
        BlockedMachineKeepsProductionStatus();
        return passed;
    }

    static void PortsCarryMachineFloorAndKeepLegacyConstructor()
    {
        FactoryEntity refinery = Entity(1, FactoryKind.Refinery, 8, 8, 0, 2, FactoryRecipe.OilRefining);
        FluidPort[] ports = FluidPorts.For(refinery).ToArray();
        True(ports.Length == 4 && ports.All(port => port.Floor == 2), "generated machine ports keep their layer");
        var legacy = new FluidPort(3, 4, Resource.Water, true);
        True(legacy.X == 3 && legacy.Z == 4 && legacy.Floor == 0 && legacy.IsInput, "legacy port constructor delegates to ground floor");
    }

    static void CoincidentFloorsDoNotConnect()
    {
        FactoryEntity source = Entity(1, FactoryKind.Pipe, 1, 1, 0, 0);
        FactoryEntity upperTarget = Entity(2, FactoryKind.Pipe, 2, 1, 0, 1);
        source.Input[(int)Resource.Water] = 5;
        new FactoryFluidSimulation(State(source, upperTarget)).Tick(1f);
        True(source.Input[(int)Resource.Water] == 5 && upperTarget.Input[(int)Resource.Water] == 0,
            "ordinary pipe lookup cannot cross coincident floors");
    }

    static void RiserMovesOnlySenderToReciprocalReceiver()
    {
        FactoryEntity sender = Riser(1, 2, 2, 0, true, 2);
        FactoryEntity receiver = Riser(2, 2, 2, 1, false, 1);
        sender.Input[(int)Resource.Water] = 8;
        FactoryEntity upperPipe = Entity(3, FactoryKind.Pipe, 3, 2, 0, 1);
        var simulation = new FactoryFluidSimulation(State(sender, receiver, upperPipe));
        simulation.Tick(1f);
        True(sender.Input[(int)Resource.Water] == 0 && receiver.Input[(int)Resource.Water] == 8,
            "sender transfers to its reciprocal adjacent-floor receiver");
        True(upperPipe.Input[(int)Resource.Water] == 0, "receiver cannot forward newly received fluid in the vertical tick");
        simulation.Tick(1f);
        True(receiver.Input[(int)Resource.Water] == 0 && upperPipe.Input[(int)Resource.Water] == 8,
            "receiver emits only forward on its own floor on the next tick");

        receiver.Input[(int)Resource.Water] = 1;
        sender.Input[(int)Resource.Water] = 0;
        simulation.Tick(1f);
        True(sender.Input[(int)Resource.Water] == 0, "receiver never sends fluid vertically back to sender");
    }

    static void RiserRequiresBothEndpointsPoweredAndRunning()
    {
        FactoryEntity sender = Riser(1, 4, 4, 0, true, 2);
        FactoryEntity receiver = Riser(2, 4, 4, 1, false, 1);
        sender.Input[(int)Resource.CrudeOil] = 4;
        receiver.Powered = false;
        var simulation = new FactoryFluidSimulation(State(sender, receiver));
        simulation.Tick(1f);
        True(sender.Input[(int)Resource.CrudeOil] == 4 && receiver.Input[(int)Resource.CrudeOil] == 0,
            "unpowered receiver closes the vertical link");
        receiver.Powered = true;
        sender.Powered = false;
        simulation.Tick(1f);
        True(sender.Input[(int)Resource.CrudeOil] == 4, "unpowered sender cannot lift fluid");
        sender.Powered = true;
        receiver.AutomationBlocked = true;
        simulation.Tick(1f);
        True(sender.Input[(int)Resource.CrudeOil] == 4, "automatically stopped receiver closes the link");
    }

    static void VerticalTransferUsesTickStartSnapshot()
    {
        FactoryEntity pipe = Entity(1, FactoryKind.Pipe, 1, 5, 0, 0);
        FactoryEntity sender = Riser(2, 2, 5, 0, true, 3);
        FactoryEntity receiver = Riser(3, 2, 5, 1, false, 2);
        pipe.Input[(int)Resource.Water] = 8;
        var simulation = new FactoryFluidSimulation(State(pipe, sender, receiver));
        simulation.Tick(1f);
        True(sender.Input[(int)Resource.Water] == 8 && receiver.Input[(int)Resource.Water] == 0,
            "fluid arriving planarly cannot rise in the same tick");
        simulation.Tick(1f);
        True(receiver.Input[(int)Resource.Water] == 8, "tick-start sender fluid rises on the following tick");
    }

    static void RiserTypingRejectsMixedFluid()
    {
        FactoryEntity sender = Riser(1, 7, 7, 0, true, 2);
        FactoryEntity receiver = Riser(2, 7, 7, 1, false, 1);
        sender.Input[(int)Resource.Water] = 3;
        receiver.Input[(int)Resource.CrudeOil] = 2;
        new FactoryFluidSimulation(State(sender, receiver)).Tick(1f);
        True(sender.Input[(int)Resource.Water] == 3 && receiver.Input[(int)Resource.CrudeOil] == 2,
            "vertical endpoint rejects mixed fluid without loss");
        True(receiver.Status.Contains("혼합"), "mixed riser endpoint reports a readable reason");
    }

    static void AutomationStopsOrdinarySourcesAndTargets()
    {
        FactoryEntity source = Entity(1, FactoryKind.Pipe, 1, 9, 0, 1);
        FactoryEntity target = Entity(2, FactoryKind.Pipe, 2, 9, 0, 1);
        source.Input[(int)Resource.Fuel] = 2;
        source.AutomationBlocked = true;
        var simulation = new FactoryFluidSimulation(State(source, target));
        simulation.Tick(1f);
        True(source.Input[(int)Resource.Fuel] == 2, "automation-stopped ordinary source does not flow");
        source.AutomationBlocked = false;
        target.AutomationBlocked = true;
        simulation.Tick(1f);
        True(source.Input[(int)Resource.Fuel] == 2 && target.Input[(int)Resource.Fuel] == 0,
            "automation-stopped ordinary target rejects flow");
    }

    static void PreproducedMachineFluidDrainsWithoutPower()
    {
        FactoryEntity pump = Entity(1, FactoryKind.WaterPump, 10, 10, 0, 0, FactoryRecipe.WaterExtraction);
        FactoryEntity pipe = Entity(2, FactoryKind.Pipe, 12, 10, 0, 0);
        pump.Powered = false;
        pump.Output[(int)Resource.Water] = 4;
        new FactoryFluidSimulation(State(pump, pipe)).Tick(1f);
        True(pump.Output[(int)Resource.Water] == 0 && pipe.Input[(int)Resource.Water] == 4,
            "v0.8 preproduced machine output still drains without machine power");
    }

    static void BlockedMachineKeepsProductionStatus()
    {
        FactoryEntity pump = Entity(1, FactoryKind.WaterPump, 14, 14, 0, 0, FactoryRecipe.WaterExtraction);
        pump.Output[(int)Resource.Water] = 4;
        pump.Status = "출력 공간 부족: 물";
        new FactoryFluidSimulation(State(pump)).Tick(1f);
        True(pump.Status == "출력 공간 부족: 물", "fluid pass preserves the production machine's readable blockage reason");
    }

    static FactoryEntity Riser(int id, int x, int z, int floor, bool sender, int linkId)
    {
        FactoryEntity entity = Entity(id, FactoryKind.FluidRiser, x, z, 0, floor);
        entity.IsLinkSender = sender;
        entity.LinkId = linkId;
        return entity;
    }

    static FactoryEntity Entity(int id, FactoryKind kind, int x, int z, int direction, int floor,
        FactoryRecipe recipe = FactoryRecipe.None)
    {
        return new FactoryEntity
        {
            Id = id, Kind = kind, X = x, Z = z, Direction = direction, Floor = floor,
            Recipe = recipe, Powered = true
        };
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
        if (!condition) throw new InvalidOperationException("Layer fluid check failed: " + message);
        passed++;Console.WriteLine("ok "+message);
    }
}

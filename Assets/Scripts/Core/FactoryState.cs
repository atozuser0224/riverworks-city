using System;
using System.Collections.Generic;

namespace Riverworks
{
    public enum FactoryKind
    {
        None = 0, Belt = 1, Inserter = 2, Drill = 3, Furnace = 4, Assembler = 5, Storage = 6,
        ImportDock = 7, ExportDock = 8, PowerInlet = 9, Pole = 10, Splitter = 11, Pipe = 12,
        PipeJunction = 13, FluidTank = 14, WaterPump = 15, OilPump = 16, Foundry = 17,
        MachiningBench = 18, Refinery = 19, ChemicalPlant = 20, Manufacturer = 21,
        ItemLift = 22, FluidRiser = 23
    }
    public enum FactoryRecipe
    {
        None = 0, IronPlate = 1, Tools = 2, Flour = 3, Bread = 4, IronMining = 5, CopperMining = 6,
        CoalMining = 7, BauxiteMining = 8, WaterExtraction = 9, OilExtraction = 10, CopperSmelting = 11,
        AlloySteel = 12, GearCutting = 13, WireDrawing = 14, BeamRolling = 15, PipeRolling = 16,
        SilicaCrushing = 17, FrameAssembly = 18, MotorAssembly = 19, CircuitPrinting = 20,
        AdvancedCircuitAssembly = 21, ComputerAssembly = 22, ControlUnitAssembly = 23, OilRefining = 24,
        PlasticPolymerization = 25, RubberPolymerization = 26, SulfurRecovery = 27, SulfuricAcid = 28,
        HeavyOilCracking = 29, FuelRefining = 30, PlasticRecycling = 31, RubberRecycling = 32,
        AluminaRefining = 33, ScrapRefining = 34, AluminumSmelting = 35, CasingPressing = 36,
        BatteryAssembly = 37
    }

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
        public bool Paused;
        public int ClockPercent = 100;
        public float FluidProgress;
        public int FluidCursor;
        public int Floor;
        public int LinkId;
        public bool IsLinkSender;
        public bool ControllerInstalled;
        [NonSerialized] public bool AutomationBlocked;
        public bool IsStopped => Paused || AutomationBlocked;
    }

    [Serializable]
    public class FactoryState
    {
        public int Version = 3, Width = 24, Height = 16, NextEntityId = 1;
        public List<FactoryEntity> Entities = new List<FactoryEntity>();
        public List<int> Produced = NewInventory();
        public List<int> Exported = NewInventory();
        public List<int> Recovered = NewInventory();
        public int PowerBudget = 20;
        public float ElapsedSeconds;
        public List<FactoryPlatform> Platforms = new List<FactoryPlatform>();
        public List<AutomationRule> AutomationRules = new List<AutomationRule>();
        public int NextAutomationRuleId = 1;

        public static List<int> NewInventory() => new List<int>(new int[ResourceCatalog.InventoryCount]);
        public static FactoryState CreateEmpty() => new FactoryState();
        public static FactoryState CreateCityGrid() => new FactoryState { Width = 42, Height = 42 };
        internal static FactoryState CreateVersion2Empty() => new FactoryState { Version = 2, Platforms = null, AutomationRules = null, NextAutomationRuleId = 0 };
        internal static FactoryState CreateVersion2CityGrid() => new FactoryState { Version = 2, Width = 42, Height = 42, Platforms = null, AutomationRules = null, NextAutomationRuleId = 0 };

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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    /// <summary>Power-grid component: one dense column entry per factory entity.</summary>
    public struct FactoryPowerNodeComponent
    {
        public int Id;
        public int Kind;
        public int X, Z, Floor, Direction;
        public int Width, Height;
        public int ClockPercent;
        public int Paused;
        public int AutomationBlocked;
        public int IsPowerNode;
        public int IsInlet;
        public int IsLive;
        public int Covered;
        public int Powered;
        public int StatusMode;
    }

    /// <summary>
    /// Data-oriented factory power graph. The original object loop was replaced by a column
    /// pass: source inlets are seeded, connectivity propagates to a fixed point, then demand is
    /// satisfied in deterministic id order. Status strings are exported as small modes so the
    /// system stays free of display text.
    /// </summary>
    public sealed class FactoryPowerSystem : IEcsSystem
    {
        public const int StatusUnchanged = 0;
        public const int StatusPaused = 1;
        public const int StatusAutomationBlocked = 2;
        public const int StatusGridConnected = 3;
        public const int StatusInletNeeded = 4;
        public const int StatusBudgetShort = 5;
        public const int StatusOutOfRange = 6;

        public void Tick(EcsWorld world, in EcsTick tick)
        {
            if (!(tick.Context is FactoryPowerBridge bridge)) return;
            ComponentStore<FactoryPowerNodeComponent> nodes = world.Store<FactoryPowerNodeComponent>();
            float budget = Math.Max(0f, bridge.PowerBudget);

            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < nodes.Count; i++)
                {
                    ref FactoryPowerNodeComponent node = ref nodes.ValueAt(i);
                    if (node.IsPowerNode == 0 || Stopped(node) || node.IsLive != 0) continue;
                    for (int j = 0; j < nodes.Count; j++)
                    {
                        if (i == j) continue;
                        FactoryPowerNodeComponent source = nodes.ValueAt(j);
                        if (source.IsPowerNode == 0 || Stopped(source) || source.IsLive == 0) continue;
                        if (Distance(node, source) > 6) continue;
                        node.IsLive = 1;
                        changed = true;
                        break;
                    }
                }
            } while (changed);

            int liveCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                ref FactoryPowerNodeComponent node = ref nodes.ValueAt(i);
                if (node.IsPowerNode == 0) continue;
                bool live = node.IsLive != 0;
                if (live) liveCount++;
                node.StatusMode = live ? StatusGridConnected
                    : node.Paused != 0 ? StatusPaused
                    : node.AutomationBlocked != 0 ? StatusAutomationBlocked
                    : StatusInletNeeded;
                node.Powered = live ? 1 : 0;
            }

            float available = liveCount > 0 ? budget : 0f;
            float used = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                ref FactoryPowerNodeComponent node = ref nodes.ValueAt(i);
                float demand = Demand(node, bridge);
                if (demand <= 0f)
                {
                    if (node.IsPowerNode == 0) node.Powered = Stopped(node) ? 0 : 1;
                    continue;
                }
                bool covered = false;
                for (int j = 0; j < nodes.Count; j++)
                {
                    FactoryPowerNodeComponent source = nodes.ValueAt(j);
                    if (source.IsPowerNode == 0 || source.IsLive == 0) continue;
                    if (Distance(node, source) <= 4) { covered = true; break; }
                }
                node.Covered = covered ? 1 : 0;
                bool powered = !Stopped(node) && covered && used + demand <= available + .0001f;
                node.Powered = powered ? 1 : 0;
                if (powered) used += demand;
                else node.StatusMode = node.Paused != 0 ? StatusPaused : node.AutomationBlocked != 0 ? StatusAutomationBlocked
                    : covered ? StatusBudgetShort : StatusOutOfRange;
            }

            bridge.PowerAvailable = available;
            bridge.PowerUsed = used;
        }

        static bool Stopped(in FactoryPowerNodeComponent node) => node.Paused != 0 || node.AutomationBlocked != 0;

        static float Demand(in FactoryPowerNodeComponent node, FactoryPowerBridge bridge)
        {
            if (Stopped(node)) return 0f;
            FactorySpec spec = FactoryCatalog.Get((FactoryKind)node.Kind);
            float demand = spec == null ? 0f : spec.PowerDemand;
            if (demand <= 0f) return 0f;
            float clock = FactoryCatalog.IsClockable((FactoryKind)node.Kind) ? node.ClockPercent / 100f : 1f;
            return demand * bridge.PowerDemandMultiplier * clock * clock;
        }

        static int Distance(in FactoryPowerNodeComponent first, in FactoryPowerNodeComponent second)
        {
            if (first.Floor != second.Floor)
            {
                bool verticalPoles = first.Kind == (int)FactoryKind.Pole && second.Kind == (int)FactoryKind.Pole &&
                    first.X == second.X && first.Z == second.Z && Math.Abs(first.Floor - second.Floor) == 1;
                return verticalPoles ? 1 : int.MaxValue;
            }
            return Math.Max(
                AxisDistance(first.X, first.X + first.Width - 1, second.X, second.X + second.Width - 1),
                AxisDistance(first.Z, first.Z + first.Height - 1, second.Z, second.Z + second.Height - 1));
        }

        static int AxisDistance(int aMin, int aMax, int bMin, int bMax) =>
            aMax < bMin ? bMin - aMax : bMax < aMin ? aMin - bMax : 0;
    }

    /// <summary>
    /// Bridges the serialized factory DTO into the ECS power world and writes the results back.
    /// Import order is the simulation's deterministic processing order, so results are identical
    /// to the previous object-based graph.
    /// </summary>
    public sealed class FactoryPowerBridge
    {
        public readonly EcsWorld World = new EcsWorld();
        public readonly ComponentStore<FactoryPowerNodeComponent> Nodes;
        readonly List<FactoryEntity> imported = new List<FactoryEntity>();

        public float PowerBudget;
        public float PowerDemandMultiplier = 1f;
        public float PowerAvailable;
        public float PowerUsed;

        public FactoryPowerBridge()
        {
            Nodes = World.Store<FactoryPowerNodeComponent>();
            World.AddSystem(new FactoryPowerSystem());
        }

        public void Import(IReadOnlyList<FactoryEntity> ordered, float powerBudget, float powerDemandMultiplier, IFactoryEnvironment environment)
        {
            PowerBudget = powerBudget;
            PowerDemandMultiplier = powerDemandMultiplier;
            imported.Clear();
            while (Nodes.Count > 0) World.Destroy(World.Handle(Nodes.EntityAt(Nodes.Count - 1)));
            for (int i = 0; i < ordered.Count; i++)
            {
                FactoryEntity entity = ordered[i];
                FactorySpec spec = FactoryCatalog.Get(entity.Kind);
                bool isPowerNode = entity.Kind == FactoryKind.PowerInlet || entity.Kind == FactoryKind.Pole;
                bool live = !entity.IsStopped && entity.Kind == FactoryKind.PowerInlet && entity.Floor == 0 &&
                    (environment == null || environment.CanSupplyPower(entity));
                Entity handle = World.Create();
                Nodes.Set(handle.Index, new FactoryPowerNodeComponent
                {
                    Id = entity.Id,
                    Kind = (int)entity.Kind,
                    X = entity.X,
                    Z = entity.Z,
                    Floor = entity.Floor,
                    Direction = entity.Direction,
                    Width = spec == null ? 1 : spec.Width,
                    Height = spec == null ? 1 : spec.Height,
                    ClockPercent = entity.ClockPercent,
                    Paused = entity.Paused ? 1 : 0,
                    AutomationBlocked = entity.AutomationBlocked ? 1 : 0,
                    IsPowerNode = isPowerNode ? 1 : 0,
                    IsInlet = entity.Kind == FactoryKind.PowerInlet ? 1 : 0,
                    IsLive = live ? 1 : 0
                });
                imported.Add(entity);
            }
        }

        public void Run() => World.Tick(new EcsTick(0f, this));

        public void Export()
        {
            int count = Math.Min(imported.Count, Nodes.Count);
            for (int i = 0; i < count; i++)
            {
                FactoryEntity entity = imported[i];
                if (entity == null) continue;
                ref FactoryPowerNodeComponent node = ref Nodes.ValueAt(i);
                entity.Powered = node.Powered != 0;
                switch (node.StatusMode)
                {
                    case FactoryPowerSystem.StatusPaused: entity.Status = "수동 일시정지"; break;
                    case FactoryPowerSystem.StatusAutomationBlocked: entity.Status = "자동화 조건 대기"; break;
                    case FactoryPowerSystem.StatusGridConnected: entity.Status = "전력망 연결"; break;
                    case FactoryPowerSystem.StatusInletNeeded: entity.Status = "전력 인입구 연결 필요"; break;
                    case FactoryPowerSystem.StatusBudgetShort: entity.Status = "공장 전력 예산 부족"; break;
                    case FactoryPowerSystem.StatusOutOfRange: entity.Status = "전력망 범위 밖"; break;
                }
            }
        }
    }

    /// <summary>Recipe machine component: the hot production fields of a factory entity.</summary>
    public struct FactoryMachineComponent
    {
        public int Id;
        public int Kind;
        public int Recipe;
        public int ClockPercent;
        public int Paused;
        public int AutomationBlocked;
        public int Powered;
        public int IsMachine;
        public int IsExtraction;
        public int RecipeMissing;
        public int SourceOk;
        public int StatusMode;
        public float Progress;
        public string GateText;
    }

    /// <summary>Flat inventory column for machines: solid counts per resource.</summary>
    public struct FactoryInventoryComponent
    {
        public int[] Input;
        public int[] Output;
    }

    /// <summary>
    /// Data-oriented recipe processing. The original per-entity method is replaced by a column
    /// pass with the same gates, same batch math and the same status strings.
    /// </summary>
    public sealed class FactoryMachineSystem : IEcsSystem
    {
        public const int StatusUnchanged = 0;
        public const int StatusRecipeMissing = 1;
        public const int StatusGate = 2;
        public const int StatusSourceMissing = 3;
        public const int StatusInputShort = 4;
        public const int StatusOutputFull = 5;
        public const int StatusWorking = 6;
        public const int StatusCompleted = 7;

        public void Tick(EcsWorld world, in EcsTick tick)
        {
            if (!(tick.Context is FactoryMachineBridge bridge)) return;
            ComponentStore<FactoryMachineComponent> machines = world.Store<FactoryMachineComponent>();
            ComponentStore<FactoryInventoryComponent> inventories = world.Store<FactoryInventoryComponent>();
            for (int i = 0; i < machines.Count; i++)
            {
                ref FactoryMachineComponent machine = ref machines.ValueAt(i);
                if (machine.IsMachine == 0) continue;
                if (machine.Powered == 0 || machine.Paused != 0 || machine.AutomationBlocked != 0) continue;
                if (machine.RecipeMissing != 0) { machine.StatusMode = StatusRecipeMissing; continue; }
                if (!string.IsNullOrEmpty(machine.GateText)) { machine.StatusMode = StatusGate; continue; }
                RecipeSpec recipe = FactoryCatalog.GetRecipe((FactoryRecipe)machine.Recipe);
                if (recipe == null) { machine.StatusMode = StatusRecipeMissing; continue; }
                if (recipe.IsExtraction && machine.SourceOk == 0) { machine.StatusMode = StatusSourceMissing; continue; }
                if (!inventories.TryGet(machines.EntityAt(i), out FactoryInventoryComponent inventory))
                {
                    machine.StatusMode = StatusInputShort;
                    continue;
                }
                FactorySpec spec = FactoryCatalog.Get((FactoryKind)machine.Kind);
                if (spec == null) continue;
                if (!HasInputs(inventory, recipe)) { machine.StatusMode = StatusInputShort; continue; }
                if (!HasOutputSpace(inventory, recipe, spec)) { machine.StatusMode = StatusOutputFull; continue; }
                machine.Progress += tick.DeltaSeconds * bridge.MachineSpeed * machine.ClockPercent / 100f;
                machine.StatusMode = StatusWorking;
                int batches = MaximumPossibleBatches(machine, inventory, recipe, spec);
                while (batches-- > 0)
                {
                    machine.Progress -= recipe.Duration;
                    foreach (RecipeAmount input in recipe.Inputs) inventory.Input[(int)input.Resource] -= input.Amount;
                    foreach (RecipeAmount output in recipe.Outputs)
                    {
                        inventory.Output[(int)output.Resource] += output.Amount;
                        bridge.State.Produced[(int)output.Resource] += output.Amount;
                    }
                    machine.StatusMode = StatusCompleted;
                }
                // Buffer capacity bounds the number of useful completions even for an extreme delta.
                if (machine.Progress >= recipe.Duration) machine.Progress = Math.Max(0f, recipe.Duration - .000001f);
            }
        }

        static bool HasInputs(FactoryInventoryComponent inventory, RecipeSpec recipe) =>
            recipe.Inputs.All(amount => inventory.Input[(int)amount.Resource] >= amount.Amount);

        static bool HasOutputSpace(FactoryInventoryComponent inventory, RecipeSpec recipe, FactorySpec spec) =>
            Total(inventory.Output) + recipe.Outputs.Sum(amount => amount.Amount) <= spec.OutputCapacity;

        static int MaximumPossibleBatches(FactoryMachineComponent machine, FactoryInventoryComponent inventory, RecipeSpec recipe, FactorySpec spec)
        {
            int progress = recipe.Duration > 0 ? (int)Math.Floor(machine.Progress / recipe.Duration) : 0;
            int input = recipe.Inputs.Length == 0 ? int.MaxValue : recipe.Inputs.Min(amount => inventory.Input[(int)amount.Resource] / amount.Amount);
            int free = spec.OutputCapacity - Total(inventory.Output);
            int output = Math.Max(1, recipe.Outputs.Sum(amount => amount.Amount));
            return Math.Max(0, Math.Min(progress, Math.Min(input, free / output)));
        }

        static int Total(int[] inventory)
        {
            int total = 0;
            for (int i = 1; i < inventory.Length; i++) total += inventory[i];
            return total;
        }
    }

    /// <summary>Imports factory recipe machines into the ECS world and writes progress back.</summary>
    public sealed class FactoryMachineBridge
    {
        public readonly EcsWorld World = new EcsWorld();
        public readonly ComponentStore<FactoryMachineComponent> Machines;
        public readonly ComponentStore<FactoryInventoryComponent> Inventories;
        readonly Dictionary<int, int> indexById = new Dictionary<int, int>();
        readonly Dictionary<int, int[]> inputPool = new Dictionary<int, int[]>();
        readonly Dictionary<int, int[]> outputPool = new Dictionary<int, int[]>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> stale = new List<int>();
        IReadOnlyList<FactoryEntity> lastOrdered;

        public FactoryState State;
        public float MachineSpeed = 1f;
        public float Seconds = 1f;

        public FactoryMachineBridge()
        {
            Machines = World.Store<FactoryMachineComponent>();
            Inventories = World.Store<FactoryInventoryComponent>();
            World.AddSystem(new FactoryMachineSystem());
        }

        public void Import(IReadOnlyList<FactoryEntity> ordered, FactoryState state, FactorySimulation simulation, float machineSpeed)
        {
            State = state;
            MachineSpeed = machineSpeed;
            lastOrdered = ordered;
            seen.Clear();
            int slots = ResourceCatalog.InventoryCount;
            for (int i = 0; i < ordered.Count; i++)
            {
                FactoryEntity entity = ordered[i];
                if (entity == null) continue;
                if (!FactoryCatalog.IsProduction(entity.Kind)) continue;
                int id = entity.Id;
                seen.Add(id);
                int entityIndex;
                if (!indexById.TryGetValue(id, out entityIndex) || !World.IsAlive(World.Handle(entityIndex)))
                {
                    Entity handle = World.Create();
                    entityIndex = handle.Index;
                    indexById[id] = entityIndex;
                }
                FactoryRecipe effective = entity.Kind == FactoryKind.Drill && entity.Recipe == FactoryRecipe.None
                    ? FactoryRecipe.IronMining : entity.Recipe;
                RecipeSpec recipe = FactoryCatalog.GetRecipe(effective);
                string gate = "";
                bool sourceOk = true;
                if (recipe != null && simulation != null)
                {
                    bool canUse = simulation.CanUseRecipe(effective, out gate);
                    if (canUse && recipe.IsExtraction)
                        sourceOk = simulation.IsExtractionSourceAvailable(entity, recipe.SourceResource);
                }
                Machines.Set(entityIndex, new FactoryMachineComponent
                {
                    Id = id,
                    Kind = (int)entity.Kind,
                    Recipe = (int)effective,
                    ClockPercent = entity.ClockPercent,
                    Paused = entity.Paused ? 1 : 0,
                    AutomationBlocked = entity.AutomationBlocked ? 1 : 0,
                    Powered = entity.Powered ? 1 : 0,
                    IsMachine = FactoryCatalog.IsProduction(entity.Kind) ? 1 : 0,
                    IsExtraction = recipe != null && recipe.IsExtraction ? 1 : 0,
                    RecipeMissing = recipe == null ? 1 : 0,
                    SourceOk = sourceOk ? 1 : 0,
                    Progress = entity.Progress,
                    GateText = gate
                });
                if (MachineComponentWanted(entity.Kind))
                {
                    if (!inputPool.TryGetValue(id, out int[] input)) { input = new int[slots]; inputPool[id] = input; }
                    if (!outputPool.TryGetValue(id, out int[] output)) { output = new int[slots]; outputPool[id] = output; }
                    Array.Clear(input, 0, input.Length);
                    Array.Clear(output, 0, output.Length);
                    for (int resource = 0; resource < slots && resource < entity.Input.Count; resource++) input[resource] = entity.Input[resource];
                    for (int resource = 0; resource < slots && resource < entity.Output.Count; resource++) output[resource] = entity.Output[resource];
                    Inventories.Set(entityIndex, new FactoryInventoryComponent { Input = input, Output = output });
                }
            }
            stale.Clear();
            foreach (KeyValuePair<int, int> pair in indexById)
                if (!seen.Contains(pair.Key)) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++)
            {
                int id = stale[i];
                World.Destroy(World.Handle(indexById[id]));
                indexById.Remove(id);
                inputPool.Remove(id);
                outputPool.Remove(id);
            }
        }

        static bool MachineComponentWanted(FactoryKind kind) => FactoryCatalog.IsProduction(kind);

        public void Run() => World.Tick(new EcsTick(Seconds, this));

        public void Export()
        {
            IReadOnlyList<FactoryEntity> ordered = lastOrdered;
            if (ordered == null) return;
            for (int i = 0; i < ordered.Count; i++)
            {
                FactoryEntity entity = ordered[i];
                if (entity == null) continue;
                if (!indexById.TryGetValue(entity.Id, out int entityIndex)) continue;
                if (!Machines.TryGet(entityIndex, out FactoryMachineComponent machine)) continue;
                if (machine.IsMachine == 0) continue;
                entity.Progress = machine.Progress;
                if (Inventories.TryGet(entityIndex, out FactoryInventoryComponent inventory))
                {
                    for (int resource = 0; resource < inventory.Input.Length && resource < entity.Input.Count; resource++) entity.Input[resource] = inventory.Input[resource];
                    for (int resource = 0; resource < inventory.Output.Length && resource < entity.Output.Count; resource++) entity.Output[resource] = inventory.Output[resource];
                }
                switch (machine.StatusMode)
                {
                    case FactoryMachineSystem.StatusRecipeMissing: entity.Status = "제조법을 선택하세요."; break;
                    case FactoryMachineSystem.StatusGate: entity.Status = machine.GateText ?? ""; break;
                    case FactoryMachineSystem.StatusSourceMissing: entity.Status = "필요한 자원 지형이 없습니다."; break;
                    case FactoryMachineSystem.StatusInputShort: entity.Status = "제조법 원료 부족"; break;
                    case FactoryMachineSystem.StatusOutputFull: entity.Status = "출력 또는 부산물 공간 부족"; break;
                    case FactoryMachineSystem.StatusWorking: entity.Status = machine.IsExtraction != 0 ? "자원 추출 중" : "가공 중"; break;
                    case FactoryMachineSystem.StatusCompleted: entity.Status = "생산 완료"; break;
                }
            }
        }
    }

    /// <summary>Automation-relevant slice of a factory entity.</summary>
    public struct FactoryAutomationEntityComponent
    {
        public int Id;
        public int Kind;
        public int IsLinkSender;
        public int ControllerInstalled;
        public int AutomationBlocked;
        public int MissingMeasurement;
    }

    /// <summary>One enabled automation rule as a component row.</summary>
    public struct FactoryAutomationRuleComponent
    {
        public int SourceEntityId;
        public int TargetEntityId;
        public int Resource;
        public int Comparison;
        public int Action;
        public int Threshold;
        public int ValidShape;
    }

    public struct FactoryAutomationInventoryComponent
    {
        public int[] Input;
        public int[] Output;
        public int CargoResource;
        public float CargoProgress;
    }

    /// <summary>
    /// Data-oriented automation evaluation. It clears transient flags for every entity, measures
    /// each enabled rule from a tick-start snapshot, then marks blocked targets exactly like the
    /// original loop, including the two status strings.
    /// </summary>
    public sealed class FactoryAutomationSystem : IEcsSystem
    {
        public void Tick(EcsWorld world, in EcsTick tick)
        {
            if (!(tick.Context is FactoryAutomationBridge bridge)) return;
            ComponentStore<FactoryAutomationEntityComponent> entities = world.Store<FactoryAutomationEntityComponent>();
            ComponentStore<FactoryAutomationRuleComponent> rules = world.Store<FactoryAutomationRuleComponent>();
            ComponentStore<FactoryAutomationInventoryComponent> inventories = world.Store<FactoryAutomationInventoryComponent>();

            for (int i = 0; i < entities.Count; i++)
            {
                ref FactoryAutomationEntityComponent entity = ref entities.ValueAt(i);
                entity.AutomationBlocked = 0;
                entity.MissingMeasurement = 0;
            }
            if (rules.Count == 0) return;

            for (int r = 0; r < rules.Count; r++)
            {
                FactoryAutomationRuleComponent rule = rules.ValueAt(r);
                int targetIndex = bridge.IndexOf(rule.TargetEntityId);
                int value = ReadValue(bridge, inventories, rule);
                FactoryAutomationEntityComponent target = default;
                bool targetMissing = targetIndex < 0 || !entities.TryGet(targetIndex, out target);
                bool invalid = targetMissing || rule.ValidShape == 0 || value < 0 ||
                    !IsControllable(target) || target.ControllerInstalled == 0;
                bool blocked;
                if (invalid) blocked = true;
                else
                {
                    bool comparisonTrue = rule.Comparison == (int)AutomationComparison.AtLeast
                        ? value >= rule.Threshold
                        : value <= rule.Threshold;
                    blocked = rule.Action == (int)AutomationAction.AllowWhenTrue ? !comparisonTrue : comparisonTrue;
                }
                if (!blocked || targetMissing) continue;
                target.AutomationBlocked = 1;
                if (invalid) target.MissingMeasurement = 1;
                entities.Set(targetIndex, in target);
            }
        }

        static int ReadValue(FactoryAutomationBridge bridge, ComponentStore<FactoryAutomationInventoryComponent> inventories, FactoryAutomationRuleComponent rule)
        {
            if (!FactoryAutomation.ValidResource((Resource)rule.Resource) || rule.SourceEntityId < 0) return -1;
            int resourceIndex = rule.Resource;
            if (rule.SourceEntityId == 0)
            {
                List<float> stock = bridge.Stock;
                if (stock == null || stock.Count != ResourceCatalog.InventoryCount || stock.Any(value => !Finite(value) || value < 0)) return -1;
                float value = stock[resourceIndex];
                if (value > int.MaxValue) return -1;
                return (int)Math.Floor(value);
            }
            int sourceIndex = bridge.IndexOf(rule.SourceEntityId);
            if (sourceIndex < 0) return -1;
            if (!inventories.TryGet(sourceIndex, out FactoryAutomationInventoryComponent source)) return -1;
            if (source.Input == null || source.Output == null ||
                source.Input.Length != ResourceCatalog.InventoryCount || source.Output.Length != ResourceCatalog.InventoryCount) return -1;
            if (source.Input[0] != 0 || source.Output[0] != 0) return -1;
            for (int i = 0; i < source.Input.Length; i++)
                if (source.Input[i] < 0 || source.Output[i] < 0) return -1;
            if (!ResourceCatalog.IsValid((Resource)source.CargoResource)) return -1;
            if (!Finite(source.CargoProgress) || source.CargoProgress < 0f || source.CargoProgress > 1f) return -1;
            bool hasCargo = ResourceCatalog.IsTransportable((Resource)source.CargoResource);
            if ((source.CargoResource == (int)Resource.Coins && source.CargoProgress != 0f) ||
                (source.CargoResource != (int)Resource.Coins && !hasCargo)) return -1;
            long total = (long)source.Input[resourceIndex] + source.Output[resourceIndex];
            if (source.CargoResource == resourceIndex) total++;
            return total > int.MaxValue ? -1 : (int)total;
        }

        static bool IsControllable(FactoryAutomationEntityComponent entity)
        {
            FactoryKind kind = (FactoryKind)entity.Kind;
            if (kind == FactoryKind.None || FactoryCatalog.Get(kind) == null) return false;
            return (kind != FactoryKind.ItemLift && kind != FactoryKind.FluidRiser) || entity.IsLinkSender != 0;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Imports factory entities, enabled automation rules and measurement sources.</summary>
    public sealed class FactoryAutomationBridge
    {
        public readonly EcsWorld World = new EcsWorld();
        public readonly ComponentStore<FactoryAutomationEntityComponent> Entities;
        public readonly ComponentStore<FactoryAutomationRuleComponent> Rules;
        public readonly ComponentStore<FactoryAutomationInventoryComponent> Inventories;
        readonly Dictionary<int, int> indexById = new Dictionary<int, int>();
        readonly Dictionary<int, int> ruleIndexById = new Dictionary<int, int>();
        readonly Dictionary<int, int[]> sourceInputPool = new Dictionary<int, int[]>();
        readonly Dictionary<int, int[]> sourceOutputPool = new Dictionary<int, int[]>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly HashSet<int> ruleSeen = new HashSet<int>();
        readonly HashSet<int> sourceIds = new HashSet<int>();
        readonly List<int> stale = new List<int>();
        readonly List<int> staleRules = new List<int>();
        bool lastHadRules;

        public List<float> Stock;

        public FactoryAutomationBridge()
        {
            Entities = World.Store<FactoryAutomationEntityComponent>();
            Rules = World.Store<FactoryAutomationRuleComponent>();
            Inventories = World.Store<FactoryAutomationInventoryComponent>();
            World.AddSystem(new FactoryAutomationSystem());
        }

        public int IndexOf(int id) => indexById.TryGetValue(id, out int index) ? index : -1;

        static bool HasEnabledRules(FactoryState factory)
        {
            if (factory == null || factory.AutomationRules == null) return false;
            for (int i = 0; i < factory.AutomationRules.Count; i++)
            {
                AutomationRule rule = factory.AutomationRules[i];
                if (rule != null && rule.Enabled) return true;
            }
            return false;
        }

        public void Import(FactoryState factory, GameState game)
        {
            Stock = game == null ? null : game.Stock;
            bool hasRules = HasEnabledRules(factory);
            // Without enabled rules nothing can be blocked, so an already empty/released
            // world stays untouched instead of re-importing every entity each tick.
            if (!hasRules && !lastHadRules) return;
            lastHadRules = hasRules;
            if (factory == null || factory.Entities == null) return;

            sourceIds.Clear();
            if (factory.AutomationRules != null)
            {
                for (int i = 0; i < factory.AutomationRules.Count; i++)
                {
                    AutomationRule rule = factory.AutomationRules[i];
                    if (rule != null && rule.Enabled && rule.SourceEntityId > 0) sourceIds.Add(rule.SourceEntityId);
                }
            }

            seen.Clear();
            int slots = ResourceCatalog.InventoryCount;
            for (int i = 0; i < factory.Entities.Count; i++)
            {
                FactoryEntity entity = factory.Entities[i];
                if (entity == null) continue;
                int id = entity.Id;
                seen.Add(id);
                int entityIndex;
                if (!indexById.TryGetValue(id, out entityIndex) || !Entities.TryGet(entityIndex, out FactoryAutomationEntityComponent existing) || existing.Id != id)
                {
                    Entity handle = World.Create();
                    entityIndex = handle.Index;
                    indexById[id] = entityIndex;
                }
                Entities.Set(entityIndex, new FactoryAutomationEntityComponent
                {
                    Id = id,
                    Kind = (int)entity.Kind,
                    IsLinkSender = entity.IsLinkSender ? 1 : 0,
                    ControllerInstalled = entity.ControllerInstalled ? 1 : 0
                });
                if (sourceIds.Contains(id))
                {
                    if (!sourceInputPool.TryGetValue(id, out int[] input)) { input = new int[slots]; sourceInputPool[id] = input; }
                    if (!sourceOutputPool.TryGetValue(id, out int[] output)) { output = new int[slots]; sourceOutputPool[id] = output; }
                    Array.Clear(input, 0, input.Length);
                    Array.Clear(output, 0, output.Length);
                    for (int resource = 0; resource < slots && resource < entity.Input.Count; resource++) input[resource] = entity.Input[resource];
                    for (int resource = 0; resource < slots && resource < entity.Output.Count; resource++) output[resource] = entity.Output[resource];
                    Inventories.Set(entityIndex, new FactoryAutomationInventoryComponent
                    {
                        Input = input,
                        Output = output,
                        CargoResource = (int)entity.CargoResource,
                        CargoProgress = entity.CargoProgress
                    });
                }
            }
            stale.Clear();
            foreach (KeyValuePair<int, int> pair in indexById)
                if (!seen.Contains(pair.Key)) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++)
            {
                int id = stale[i];
                World.Destroy(World.Handle(indexById[id]));
                indexById.Remove(id);
                sourceInputPool.Remove(id);
                sourceOutputPool.Remove(id);
            }

            ruleSeen.Clear();
            if (factory.AutomationRules != null)
            {
                for (int i = 0; i < factory.AutomationRules.Count; i++)
                {
                    AutomationRule rule = factory.AutomationRules[i];
                    if (rule == null || !rule.Enabled) continue;
                    int key = rule.Id > 0 ? rule.Id : -(i + 1);
                    ruleSeen.Add(key);
                    int ruleIndex;
                    if (!ruleIndexById.TryGetValue(key, out ruleIndex) || !Rules.Has(ruleIndex))
                    {
                        Entity handle = World.Create();
                        ruleIndex = handle.Index;
                        ruleIndexById[key] = ruleIndex;
                    }
                    Rules.Set(ruleIndex, new FactoryAutomationRuleComponent
                    {
                        SourceEntityId = rule.SourceEntityId,
                        TargetEntityId = rule.TargetEntityId,
                        Resource = (int)rule.Resource,
                        Comparison = (int)rule.Comparison,
                        Action = (int)rule.Action,
                        Threshold = rule.Threshold,
                        ValidShape = FactoryAutomation.ValidRuleShape(rule) ? 1 : 0
                    });
                }
            }
            staleRules.Clear();
            foreach (KeyValuePair<int, int> pair in ruleIndexById)
                if (!ruleSeen.Contains(pair.Key)) staleRules.Add(pair.Key);
            for (int i = 0; i < staleRules.Count; i++)
            {
                int key = staleRules[i];
                World.Destroy(World.Handle(ruleIndexById[key]));
                ruleIndexById.Remove(key);
            }
        }

        public void Run() => World.Tick(new EcsTick(0f, this));

        public void Export(FactoryState factory)
        {
            if (factory == null || factory.Entities == null) return;
            bool hasComponents = indexById.Count > 0;
            for (int i = 0; i < factory.Entities.Count; i++)
            {
                FactoryEntity entity = factory.Entities[i];
                if (entity == null) continue;
                bool blocked = false;
                bool missing = false;
                if (hasComponents && indexById.TryGetValue(entity.Id, out int entityIndex) &&
                    Entities.TryGet(entityIndex, out FactoryAutomationEntityComponent component))
                {
                    blocked = component.AutomationBlocked != 0;
                    missing = component.MissingMeasurement != 0;
                }
                entity.AutomationBlocked = blocked;
                if (entity.Status != null && (entity.Status.StartsWith("조건 자동화", StringComparison.Ordinal) ||
                    entity.Status.StartsWith("자동화 조건", StringComparison.Ordinal))) entity.Status = "";
                if (blocked) entity.Status = missing ? "자동화 조건 오류: 원본 측정 실패" : "자동화 조건 대기";
            }
        }
    }

    /// <summary>Belt, item-lift and extraction slice of a factory entity.</summary>
    public struct FactoryTransportComponent
    {
        public int Id, Kind, X, Z, Floor, Direction, Width, Height;
        public int Paused, AutomationBlocked, Powered;
        public int CargoResource;
        public float CargoProgress;
        public int Filter, LinkId, IsLinkSender, SplitLeft;
        public int Recipe;
        public int HadCargoAtTickStart;
        public int StatusMode;
    }

    /// <summary>Solid inventory column for entities that accept or provide items.</summary>
    public struct FactoryTransportInventoryComponent
    {
        public int[] Input;
        public int[] Output;
    }

    /// <summary>
    /// Data-oriented belt, item-lift and extraction passes. Movement, acceptance, filters and
    /// status strings mirror the original object methods exactly; inserters stay on the object
    /// path one step earlier so tick order is preserved.
    /// </summary>
    public sealed class FactoryTransportSystem : IEcsSystem
    {
        public const int StatusInTransit = 1;
        public const int StatusReadyExport = 2;
        public const int StatusStored = 3;
        public const int StatusBeltStuck = 4;
        public const int StatusLiftDump = 5;
        public const int StatusLiftExitBlocked = 6;
        public const int StatusLiftWait = 7;
        public const int StatusLiftCarry = 8;
        public const int StatusLiftDone = 9;
        public const int StatusLiftArrived = 10;
        public const int StatusExtractCity = 11;
        public const int StatusExtractDump = 12;
        public const int StatusExtractBlocked = 13;
        public const int StatusCityCarry = 14;
        public const int StatusCarrying = 15;
        public const int StatusNothing = 16;
        public const int StatusDropBlocked = 17;

        static readonly int[] Dx = { 1, 0, -1, 0 }, Dz = { 0, 1, 0, -1 };

        public void Tick(EcsWorld world, in EcsTick tick)
        {
            if (!(tick.Context is FactoryTransportBridge bridge)) return;
            ComponentStore<FactoryTransportComponent> store = world.Store<FactoryTransportComponent>();
            ComponentStore<FactoryTransportInventoryComponent> inventories = world.Store<FactoryTransportInventoryComponent>();

            for (int i = 0; i < store.Count; i++)
            {
                ref FactoryTransportComponent entity = ref store.ValueAt(i);
                if (entity.Kind != (int)FactoryKind.Inserter || entity.Powered == 0 || Stopped(entity)) continue;
                ProcessInserter(bridge, store, inventories, i, tick.DeltaSeconds * bridge.InserterSpeed, entity.HadCargoAtTickStart != 0);
            }

            for (int i = store.Count - 1; i >= 0; i--)
            {
                ref FactoryTransportComponent entity = ref store.ValueAt(i);
                if (entity.HadCargoAtTickStart == 0 || Stopped(entity)) continue;
                if (entity.Kind != (int)FactoryKind.Belt && entity.Kind != (int)FactoryKind.Splitter) continue;
                ProcessBelt(bridge, store, inventories, i, tick.DeltaSeconds * bridge.BeltSpeed);
            }

            for (int i = 0; i < store.Count; i++)
            {
                ref FactoryTransportComponent entity = ref store.ValueAt(i);
                if (entity.Kind != (int)FactoryKind.Drill || entity.Powered == 0 || Stopped(entity)) continue;
                EmitExtraction(bridge, store, inventories, i);
            }

            ProcessItemLifts(bridge, store, inventories, tick.DeltaSeconds);
        }

        static void ProcessBelt(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, int index, float seconds)
        {
            ref FactoryTransportComponent entity = ref store.ValueAt(index);
            if (!ResourceCatalog.IsTransportable((Resource)entity.CargoResource)) { entity.CargoProgress = 0f; return; }
            entity.CargoProgress = Math.Min(1f, entity.CargoProgress + seconds * 2f);
            if (entity.CargoProgress < 1f) return;
            int first = entity.Direction;
            int second = (entity.Direction + 1) & 3;
            if (entity.Kind == (int)FactoryKind.Splitter && entity.SplitLeft != 0) { int swap = first; first = second; second = swap; }
            bool moved = TrySend(bridge, store, inventories, index, first);
            if (!moved && entity.Kind == (int)FactoryKind.Splitter) moved = TrySend(bridge, store, inventories, index, second);
            if (moved && entity.Kind == (int)FactoryKind.Splitter) entity.SplitLeft = entity.SplitLeft != 0 ? 0 : 1;
            else if (!moved) entity.StatusMode = StatusBeltStuck;
        }

        static void ProcessInserter(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, int index, float seconds, bool startedWithCargo)
        {
            ref FactoryTransportComponent entity = ref store.ValueAt(index);
            if (startedWithCargo)
            {
                entity.CargoProgress = Math.Min(1f, entity.CargoProgress + seconds * 1.5f);
                if (entity.CargoProgress >= 1f && entity.Floor == 0 && bridge.Environment != null)
                {
                    CellInFront(entity, out int cityX, out int cityZ);
                    if (bridge.EntityAt(cityX, cityZ, 0) < 0 && bridge.Environment.TryGive(cityX, cityZ, (Resource)entity.CargoResource))
                    {
                        ClearCargo(ref entity);
                        return;
                    }
                }
                if (entity.CargoProgress >= 1f)
                {
                    CellInFront(entity, out int x, out int z);
                    int dropIndex = bridge.EntityAt(x, z, entity.Floor);
                    if (dropIndex >= 0 && Accept(bridge, store, inventories, dropIndex, entity.CargoResource, true)) ClearCargo(ref entity);
                    else entity.StatusMode = StatusDropBlocked;
                }
                return;
            }
            if (ResourceCatalog.IsTransportable((Resource)entity.CargoResource)) return;
            int bx = entity.X - Dx[entity.Direction], bz = entity.Z - Dz[entity.Direction];
            int sourceIndex = bridge.EntityAt(bx, bz, entity.Floor);
            CellInFront(entity, out int tx, out int tz);
            int targetIndex = bridge.EntityAt(tx, tz, entity.Floor);
            if (sourceIndex < 0 && entity.Floor == 0 && bridge.Environment != null &&
                TryTakeExternalForTarget(bridge, store, inventories, bx, bz, entity.Filter, targetIndex, out int externalItem))
            {
                entity.CargoResource = externalItem;
                entity.CargoProgress = 0f;
                entity.StatusMode = StatusCityCarry;
                return;
            }
            if (sourceIndex >= 0 && TakeOne(bridge, store, inventories, sourceIndex, entity.Filter, targetIndex, out int item))
            {
                entity.CargoResource = item;
                entity.CargoProgress = 0f;
                entity.StatusMode = StatusCarrying;
            }
            else entity.StatusMode = StatusNothing;
        }

        static bool TakeOne(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, int sourceIndex, int filter, int targetIndex, out int item)
        {
            item = (int)Resource.Coins;
            if (!store.TryGet(sourceIndex, out FactoryTransportComponent source)) return false;
            if (Stopped(source)) return false;
            bool cargoMatch = ResourceCatalog.IsTransportable((Resource)source.CargoResource) && Matches(source.CargoResource, filter) &&
                (targetIndex < 0 || CanAccept(bridge, store, inventories, targetIndex, source.CargoResource, true, 0));
            if (source.Kind == (int)FactoryKind.ItemLift && source.IsLinkSender == 0 && cargoMatch)
            {
                item = source.CargoResource;
                ClearCargo(ref source);
                store.Set(sourceIndex, in source);
                return true;
            }
            if ((source.Kind == (int)FactoryKind.Belt || source.Kind == (int)FactoryKind.Splitter) && cargoMatch)
            {
                item = source.CargoResource;
                ClearCargo(ref source);
                store.Set(sourceIndex, in source);
                return true;
            }
            bool fromOutput;
            if (FactoryCatalog.IsProduction((FactoryKind)source.Kind)) fromOutput = true;
            else if (source.Kind == (int)FactoryKind.Storage || source.Kind == (int)FactoryKind.ImportDock) fromOutput = false;
            else return false;
            if (!inventories.TryGet(sourceIndex, out FactoryTransportInventoryComponent inventory)) return false;
            int[] slots = fromOutput ? inventory.Output : inventory.Input;
            foreach (ResourceSpec resource in ResourceCatalog.SolidResources)
            {
                if (slots[(int)resource.Id] <= 0 || !Matches((int)resource.Id, filter)) continue;
                if (targetIndex >= 0 && !CanAccept(bridge, store, inventories, targetIndex, (int)resource.Id, true, 0)) continue;
                slots[(int)resource.Id]--;
                item = (int)resource.Id;
                return true;
            }
            return false;
        }

        static bool TryTakeExternalForTarget(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, int x, int z, int filter, int targetIndex, out int item)
        {
            item = (int)Resource.Coins;
            if (targetIndex >= 0)
            {
                if (filter != (int)Resource.Coins)
                {
                    if (!CanAccept(bridge, store, inventories, targetIndex, filter, true, 0) ||
                        !bridge.Environment.TryTake(x, z, (Resource)filter, out Resource filtered) || (int)filtered != filter) return false;
                    item = (int)filtered;
                    return true;
                }
                foreach (ResourceSpec candidate in ResourceCatalog.SolidResources)
                {
                    if (!CanAccept(bridge, store, inventories, targetIndex, (int)candidate.Id, true, 0) ||
                        !bridge.Environment.TryTake(x, z, candidate.Id, out Resource taken)) continue;
                    if ((int)taken != (int)candidate.Id) return false;
                    item = (int)taken;
                    return true;
                }
                return false;
            }
            if (!bridge.Environment.TryTake(x, z, (Resource)filter, out Resource external) || !ResourceCatalog.IsTransportable(external)) return false;
            item = (int)external;
            return true;
        }

        static void EmitExtraction(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, int index)
        {
            ref FactoryTransportComponent entity = ref store.ValueAt(index);
            RecipeSpec recipe = FactoryCatalog.GetRecipe(EffectiveRecipe((FactoryKind)entity.Kind, (FactoryRecipe)entity.Recipe));
            if (recipe == null || !recipe.IsExtraction) return;
            if (!inventories.TryGet(store.EntityAt(index), out FactoryTransportInventoryComponent inventory)) return;
            foreach (RecipeAmount output in recipe.Outputs)
            {
                if (!ResourceCatalog.IsTransportable(output.Resource) || inventory.Output[(int)output.Resource] <= 0) continue;
                CellInFront(entity, out int x, out int z);
                int targetIndex = bridge.EntityAt(x, z, entity.Floor);
                if (targetIndex < 0 && entity.Floor == 0 && bridge.Environment != null && bridge.Environment.TryGive(x, z, output.Resource))
                {
                    inventory.Output[(int)output.Resource]--;
                    entity.StatusMode = StatusExtractCity;
                    return;
                }
                if (targetIndex >= 0 && Accept(bridge, store, inventories, targetIndex, (int)output.Resource, false))
                {
                    inventory.Output[(int)output.Resource]--;
                    entity.StatusMode = StatusExtractDump;
                    return;
                }
                entity.StatusMode = StatusExtractBlocked;
                return;
            }
        }

        static void ProcessItemLifts(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, float seconds)
        {
            for (int i = 0; i < store.Count; i++)
            {
                ref FactoryTransportComponent receiver = ref store.ValueAt(i);
                if (receiver.Kind != (int)FactoryKind.ItemLift || receiver.IsLinkSender != 0 || Stopped(receiver) ||
                    receiver.Powered == 0 || receiver.HadCargoAtTickStart == 0) continue;
                if (!ResourceCatalog.IsTransportable((Resource)receiver.CargoResource)) continue;
                receiver.CargoProgress = Math.Min(1f, receiver.CargoProgress + seconds * 2f * bridge.BeltSpeed);
                if (receiver.CargoProgress < 1f) continue;
                int x = receiver.X + Dx[receiver.Direction], z = receiver.Z + Dz[receiver.Direction];
                int targetIndex = bridge.EntityAt(x, z, receiver.Floor);
                int item = receiver.CargoResource;
                if (targetIndex < 0 && receiver.Floor == 0 && bridge.Environment != null && bridge.Environment.TryGive(x, z, (Resource)item))
                {
                    ClearCargo(ref receiver);
                    receiver.StatusMode = StatusLiftDump;
                    continue;
                }
                if (targetIndex >= 0 && Accept(bridge, store, inventories, targetIndex, item, false))
                {
                    ClearCargo(ref receiver);
                    receiver.StatusMode = StatusLiftDump;
                }
                else receiver.StatusMode = StatusLiftExitBlocked;
            }

            for (int i = 0; i < store.Count; i++)
            {
                ref FactoryTransportComponent sender = ref store.ValueAt(i);
                if (sender.Kind != (int)FactoryKind.ItemLift || sender.IsLinkSender == 0 || Stopped(sender) ||
                    sender.Powered == 0 || sender.HadCargoAtTickStart == 0) continue;
                if (!ResourceCatalog.IsTransportable((Resource)sender.CargoResource)) continue;
                int receiverIndex = bridge.IndexOfId(sender.LinkId);
                if (receiverIndex < 0 || !store.TryGet(receiverIndex, out FactoryTransportComponent receiver))
                {
                    sender.StatusMode = StatusLiftWait;
                    continue;
                }
                if (receiver.Kind != (int)FactoryKind.ItemLift || receiver.IsLinkSender != 0 || receiver.LinkId != sender.Id ||
                    Stopped(receiver) || receiver.Powered == 0 ||
                    ResourceCatalog.IsTransportable((Resource)receiver.CargoResource) || !Matches(sender.CargoResource, receiver.Filter))
                {
                    sender.StatusMode = StatusLiftWait;
                    continue;
                }
                sender.CargoProgress = Math.Min(1f, sender.CargoProgress + seconds * 2f);
                if (sender.CargoProgress < 1f) { sender.StatusMode = StatusLiftCarry; continue; }
                int item = sender.CargoResource;
                ClearCargo(ref sender);
                receiver.CargoResource = item;
                receiver.CargoProgress = 0f;
                sender.StatusMode = StatusLiftDone;
                receiver.StatusMode = StatusLiftArrived;
                store.Set(receiverIndex, in receiver);
            }
        }

        static bool TrySend(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, int index, int direction)
        {
            ref FactoryTransportComponent entity = ref store.ValueAt(index);
            int tx = entity.X + Dx[direction], tz = entity.Z + Dz[direction];
            int targetIndex = bridge.EntityAt(tx, tz, entity.Floor);
            int item = entity.CargoResource;
            if (targetIndex >= 0 && store.TryGet(targetIndex, out FactoryTransportComponent target) &&
                (target.Kind == (int)FactoryKind.Belt || target.Kind == (int)FactoryKind.Splitter) &&
                target.X + Dx[target.Direction] == entity.X && target.Z + Dz[target.Direction] == entity.Z) return false;
            if (targetIndex < 0 && entity.Floor == 0 && bridge.Environment != null && bridge.Environment.TryGive(tx, tz, (Resource)item))
            {
                ClearCargo(ref entity);
                return true;
            }
            if (targetIndex >= 0 && Accept(bridge, store, inventories, targetIndex, item, false))
            {
                ClearCargo(ref entity);
                return true;
            }
            return false;
        }

        static bool Accept(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, int targetIndex, int item, bool fromInserter)
        {
            if (!CanAccept(bridge, store, inventories, targetIndex, item, fromInserter, 0)) return false;
            if (!store.TryGet(targetIndex, out FactoryTransportComponent target)) return false;
            int kind = target.Kind;
            if (kind == (int)FactoryKind.Belt || kind == (int)FactoryKind.Splitter ||
                kind == (int)FactoryKind.Inserter || kind == (int)FactoryKind.ItemLift)
            {
                target.CargoResource = item;
                target.CargoProgress = 0f;
                target.StatusMode = StatusInTransit;
                store.Set(targetIndex, in target);
                return true;
            }
            if (kind == (int)FactoryKind.ExportDock || kind == (int)FactoryKind.Storage || kind == (int)FactoryKind.ImportDock)
            {
                if (!inventories.TryGet(targetIndex, out FactoryTransportInventoryComponent inventory)) return false;
                inventory.Input[item]++;
                target.StatusMode = kind == (int)FactoryKind.ExportDock ? StatusReadyExport : StatusStored;
                store.Set(targetIndex, in target);
                return true;
            }
            if (fromInserter && FactoryCatalog.IsProduction((FactoryKind)kind))
            {
                if (!inventories.TryGet(targetIndex, out FactoryTransportInventoryComponent inventory)) return false;
                inventory.Input[item]++;
                return true;
            }
            return false;
        }

        static bool CanAccept(FactoryTransportBridge bridge, ComponentStore<FactoryTransportComponent> store, ComponentStore<FactoryTransportInventoryComponent> inventories, int targetIndex, int item, bool fromInserter, int depth)
        {
            if (targetIndex < 0 || depth > 4) return false;
            if (!store.TryGet(targetIndex, out FactoryTransportComponent target)) return false;
            if (Stopped(target) || !ResourceCatalog.IsTransportable((Resource)item)) return false;
            int kind = target.Kind;
            if (kind == (int)FactoryKind.ItemLift)
                return target.IsLinkSender != 0 && !ResourceCatalog.IsTransportable((Resource)target.CargoResource) && Matches(item, target.Filter);
            if (kind == (int)FactoryKind.Inserter)
            {
                if (ResourceCatalog.IsTransportable((Resource)target.CargoResource) || !Matches(item, target.Filter)) return false;
                CellInFront(target, out int x, out int z);
                int downstream = bridge.EntityAt(x, z, target.Floor);
                return downstream < 0 || CanAccept(bridge, store, inventories, downstream, item, true, depth + 1);
            }
            if (kind == (int)FactoryKind.Belt || kind == (int)FactoryKind.Splitter)
                return !ResourceCatalog.IsTransportable((Resource)target.CargoResource);
            if (kind == (int)FactoryKind.ExportDock || kind == (int)FactoryKind.Storage || kind == (int)FactoryKind.ImportDock)
            {
                if (!inventories.TryGet(targetIndex, out FactoryTransportInventoryComponent inventory)) return false;
                return Total(inventory.Input) < FactoryCatalog.Get((FactoryKind)kind).InputCapacity;
            }
            if (!fromInserter || !FactoryCatalog.IsProduction((FactoryKind)kind)) return false;
            if (!inventories.TryGet(targetIndex, out FactoryTransportInventoryComponent production)) return false;
            RecipeSpec recipe = FactoryCatalog.GetRecipe(EffectiveRecipe((FactoryKind)kind, (FactoryRecipe)target.Recipe));
            RecipeAmount ingredient = recipe == null ? default : recipe.Inputs.FirstOrDefault(amount => amount.Resource == (Resource)item);
            if (recipe == null || ingredient.Amount <= 0) return false;
            int capacity = FactoryCatalog.Get((FactoryKind)kind).InputCapacity;
            if (Total(production.Input) >= capacity) return false;
            int perBatch = recipe.Inputs.Sum(amount => amount.Amount);
            int quotaBatches = Math.Max(1, capacity / Math.Max(1, perBatch));
            return production.Input[item] < ingredient.Amount * quotaBatches;
        }

        static void CellInFront(in FactoryTransportComponent entity, out int x, out int z)
        {
            x = entity.X; z = entity.Z;
            if (entity.Direction == 0) { x = entity.X + entity.Width; z = entity.Z + (entity.Height - 1) / 2; }
            else if (entity.Direction == 2) { x = entity.X - 1; z = entity.Z + (entity.Height - 1) / 2; }
            else if (entity.Direction == 1) { x = entity.X + (entity.Width - 1) / 2; z = entity.Z + entity.Height; }
            else { x = entity.X + (entity.Width - 1) / 2; z = entity.Z - 1; }
        }

        static FactoryRecipe EffectiveRecipe(FactoryKind kind, FactoryRecipe recipe) =>
            kind == FactoryKind.Drill && recipe == FactoryRecipe.None ? FactoryRecipe.IronMining : recipe;

        static bool Matches(int item, int filter) => filter == (int)Resource.Coins || item == filter;
        static bool Stopped(in FactoryTransportComponent entity) => entity.Paused != 0 || entity.AutomationBlocked != 0;
        static void ClearCargo(ref FactoryTransportComponent entity) { entity.CargoResource = (int)Resource.Coins; entity.CargoProgress = 0f; }

        static int Total(int[] inventory)
        {
            int total = 0;
            for (int i = 1; i < inventory.Length; i++) total += inventory[i];
            return total;
        }
    }

    /// <summary>Imports factory entities plus a cell index and writes transport results back.</summary>
    public sealed class FactoryTransportBridge
    {
        public readonly EcsWorld World = new EcsWorld();
        public readonly ComponentStore<FactoryTransportComponent> Entities;
        public readonly ComponentStore<FactoryTransportInventoryComponent> Inventories;
        readonly Dictionary<int, int> indexById = new Dictionary<int, int>();
        readonly Dictionary<int, int[]> inputPool = new Dictionary<int, int[]>();
        readonly Dictionary<int, int[]> outputPool = new Dictionary<int, int[]>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> stale = new List<int>();
        IReadOnlyList<FactoryEntity> lastOrdered;
        int[] spatial = Array.Empty<int>();
        int width, height;

        public IFactoryEnvironment Environment;
        public float BeltSpeed = 1f;
        public float InserterSpeed = 1f;
        public float Seconds = 1f;

        public FactoryTransportBridge()
        {
            Entities = World.Store<FactoryTransportComponent>();
            Inventories = World.Store<FactoryTransportInventoryComponent>();
            World.AddSystem(new FactoryTransportSystem());
        }

        public int EntityAt(int x, int z, int floor)
        {
            if (x < 0 || z < 0 || x >= width || z >= height) return -1;
            int key = floor * width * height + z * width + x;
            if (key < 0 || key >= spatial.Length) return -1;
            int packed = spatial[key];
            return packed == 0 ? -1 : packed - 1;
        }

        public int IndexOfId(int id) => indexById.TryGetValue(id, out int index) ? index : -1;

        public void Import(IReadOnlyList<FactoryEntity> ordered, FactoryState state, IFactoryEnvironment environment, ICollection<int> cargoAtTickStart, float beltSpeed, float inserterSpeed)
        {
            Environment = environment;
            BeltSpeed = beltSpeed;
            InserterSpeed = inserterSpeed;
            lastOrdered = ordered;
            if (state == null) return;
            width = state.Width;
            height = state.Height;
            int floors = FactoryLayers.MaxFloor + 1;
            int spatialSize = width * height * floors;
            if (spatial.Length < spatialSize) spatial = new int[spatialSize];
            Array.Clear(spatial, 0, spatial.Length);

            seen.Clear();
            int slots = ResourceCatalog.InventoryCount;
            for (int i = 0; i < ordered.Count; i++)
            {
                FactoryEntity entity = ordered[i];
                if (entity == null) continue;
                int id = entity.Id;
                seen.Add(id);
                int entityIndex;
                if (!indexById.TryGetValue(id, out entityIndex) || !World.IsAlive(World.Handle(entityIndex)))
                {
                    Entity handle = World.Create();
                    entityIndex = handle.Index;
                    indexById[id] = entityIndex;
                }
                FactorySpec spec = FactoryCatalog.Get(entity.Kind);
                int entityWidth = spec == null ? 1 : spec.Width;
                int entityHeight = spec == null ? 1 : spec.Height;
                Entities.Set(entityIndex, new FactoryTransportComponent
                {
                    Id = id,
                    Kind = (int)entity.Kind,
                    X = entity.X,
                    Z = entity.Z,
                    Floor = entity.Floor,
                    Direction = entity.Direction,
                    Width = entityWidth,
                    Height = entityHeight,
                    Paused = entity.Paused ? 1 : 0,
                    AutomationBlocked = entity.AutomationBlocked ? 1 : 0,
                    Powered = entity.Powered ? 1 : 0,
                    CargoResource = (int)entity.CargoResource,
                    CargoProgress = entity.CargoProgress,
                    Filter = (int)entity.Filter,
                    LinkId = entity.LinkId,
                    IsLinkSender = entity.IsLinkSender ? 1 : 0,
                    SplitLeft = entity.SplitLeft ? 1 : 0,
                    Recipe = (int)entity.Recipe,
                    HadCargoAtTickStart = cargoAtTickStart != null && cargoAtTickStart.Contains(id) ? 1 : 0
                });
                for (int z = entity.Z; z < entity.Z + entityHeight; z++)
                for (int x = entity.X; x < entity.X + entityWidth; x++)
                {
                    if (x < 0 || z < 0 || x >= width || z >= height) continue;
                    int key = entity.Floor * width * height + z * width + x;
                    if (key >= 0 && key < spatial.Length && spatial[key] == 0) spatial[key] = entityIndex + 1;
                }
                if (HoldsInventory(entity.Kind))
                {
                    if (!inputPool.TryGetValue(id, out int[] input)) { input = new int[slots]; inputPool[id] = input; }
                    if (!outputPool.TryGetValue(id, out int[] output)) { output = new int[slots]; outputPool[id] = output; }
                    Array.Clear(input, 0, input.Length);
                    Array.Clear(output, 0, output.Length);
                    for (int resource = 0; resource < slots && resource < entity.Input.Count; resource++) input[resource] = entity.Input[resource];
                    for (int resource = 0; resource < slots && resource < entity.Output.Count; resource++) output[resource] = entity.Output[resource];
                    Inventories.Set(entityIndex, new FactoryTransportInventoryComponent { Input = input, Output = output });
                }
            }

            stale.Clear();
            foreach (KeyValuePair<int, int> pair in indexById)
                if (!seen.Contains(pair.Key)) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++)
            {
                int id = stale[i];
                World.Destroy(World.Handle(indexById[id]));
                indexById.Remove(id);
                inputPool.Remove(id);
                outputPool.Remove(id);
            }
        }

        static bool HoldsInventory(FactoryKind kind) =>
            FactoryCatalog.IsProduction(kind) || kind == FactoryKind.Storage || kind == FactoryKind.ImportDock || kind == FactoryKind.ExportDock;

        public void Run() => World.Tick(new EcsTick(Seconds, this));

        public void Export()
        {
            IReadOnlyList<FactoryEntity> ordered = lastOrdered;
            if (ordered == null) return;
            for (int i = 0; i < ordered.Count; i++)
            {
                FactoryEntity entity = ordered[i];
                if (entity == null) continue;
                if (!indexById.TryGetValue(entity.Id, out int entityIndex)) continue;
                if (!Entities.TryGet(entityIndex, out FactoryTransportComponent component)) continue;
                entity.CargoResource = (Resource)component.CargoResource;
                entity.CargoProgress = component.CargoProgress;
                entity.SplitLeft = component.SplitLeft != 0;
                if (Inventories.TryGet(entityIndex, out FactoryTransportInventoryComponent inventory))
                {
                    for (int resource = 0; resource < inventory.Input.Length && resource < entity.Input.Count; resource++) entity.Input[resource] = inventory.Input[resource];
                    for (int resource = 0; resource < inventory.Output.Length && resource < entity.Output.Count; resource++) entity.Output[resource] = inventory.Output[resource];
                }
                switch (component.StatusMode)
                {
                    case FactoryTransportSystem.StatusInTransit: entity.Status = "이송 중"; break;
                    case FactoryTransportSystem.StatusReadyExport: entity.Status = "반출 대기"; break;
                    case FactoryTransportSystem.StatusStored: entity.Status = "보관 중"; break;
                    case FactoryTransportSystem.StatusBeltStuck: entity.Status = "벨트 정체"; break;
                    case FactoryTransportSystem.StatusLiftDump: entity.Status = "리프트 화물 배출"; break;
                    case FactoryTransportSystem.StatusLiftExitBlocked: entity.Status = "리프트 출구 막힘"; break;
                    case FactoryTransportSystem.StatusLiftWait: entity.Status = "수직 리프트 대기"; break;
                    case FactoryTransportSystem.StatusLiftCarry: entity.Status = "수직 운반 중"; break;
                    case FactoryTransportSystem.StatusLiftDone: entity.Status = "수직 운반 완료"; break;
                    case FactoryTransportSystem.StatusLiftArrived: entity.Status = "리프트 화물 도착"; break;
                    case FactoryTransportSystem.StatusExtractCity: entity.Status = "도시로 자원 전달"; break;
                    case FactoryTransportSystem.StatusExtractDump: entity.Status = "추출물 배출"; break;
                    case FactoryTransportSystem.StatusExtractBlocked: entity.Status = "출구 막힘"; break;
                    case FactoryTransportSystem.StatusCityCarry: entity.Status = "도시 물자 운반 중"; break;
                    case FactoryTransportSystem.StatusCarrying: entity.Status = "물자 운반 중"; break;
                    case FactoryTransportSystem.StatusNothing: entity.Status = "집을 물자 없음"; break;
                    case FactoryTransportSystem.StatusDropBlocked: entity.Status = "내려놓을 공간 없음"; break;
                }
            }
        }
    }
}

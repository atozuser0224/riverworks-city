using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Riverworks
{
    public sealed class FactoryFluidSimulation
    {
        const int PipeCapacity = 40;
        const int TankCapacity = 240;
        static readonly int[] Dx = { 1, 0, -1, 0 };
        static readonly int[] Dz = { 0, 1, 0, -1 };

        readonly FactoryState state;
        readonly List<FactoryEntity> ordered = new List<FactoryEntity>();
        readonly Dictionary<long, FactoryEntity> cells = new Dictionary<long, FactoryEntity>();
        readonly Dictionary<long, FactoryEntity> machineInputs = new Dictionary<long, FactoryEntity>();
        readonly Dictionary<int, FactoryEntity> byId = new Dictionary<int, FactoryEntity>();
        readonly HashSet<int> typedRejections = new HashSet<int>();
        long cachedTopology = long.MinValue;

        sealed class Snapshot
        {
            public readonly Dictionary<int, int[]> Quantities = new Dictionary<int, int[]>();
            public readonly Dictionary<int, int> Totals = new Dictionary<int, int>();
        }

        sealed class Destination
        {
            public FactoryEntity Entity;
            public Resource Resource;
            public int Capacity;
            public int ResourceQuota;
        }

        public FactoryFluidSimulation(FactoryState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            EnsureTopology();
        }

        public void Tick(float seconds)
        {
            if (!(seconds > 0f) || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            EnsureTopology();
            foreach (FactoryEntity entity in ordered)
            {
                entity.FluidProgress = ClampCredit(entity.FluidProgress);
                entity.FluidCursor = Mod(entity.FluidCursor, 4);
            }
            Snapshot snapshot = TakeSnapshot();
            var reservedTotal = new Dictionary<int, int>();
            var reservedResource = new Dictionary<long, int>();
            typedRejections.Clear();

            foreach (FactoryEntity source in ordered)
            {
                if (source.IsStopped || (source.Kind == FactoryKind.FluidRiser && !source.Powered)) continue;
                List<Resource> resources = SourceResources(source, snapshot);
                if (resources.Count == 0)
                {
                    source.FluidProgress = ClampCredit(source.FluidProgress);
                    if (FactoryCatalog.IsFluidTransport(source.Kind) && !typedRejections.Contains(source.Id)) source.Status = "유체 없음";
                    continue;
                }

                float accumulated = ClampCredit(source.FluidProgress) + seconds * Rate(source);
                int budget = (int)Math.Floor(accumulated + .000001f);
                if (budget <= 0)
                {
                    source.FluidProgress = ClampCredit(accumulated);
                    continue;
                }

                int moved = 0;
                int start = Mod(source.FluidCursor, Math.Max(1, resources.Count));
                for (int resourceOffset = 0; resourceOffset < resources.Count && moved < budget; resourceOffset++)
                {
                    Resource resource = resources[(start + resourceOffset) % resources.Count];
                    int available = snapshot.Quantities[source.Id][(int)resource];
                    while (available > 0 && moved < budget)
                    {
                        List<Destination> destinations = Destinations(source, resource, snapshot);
                        if (destinations.Count == 0) break;
                        int branchStart = Mod(source.FluidCursor, destinations.Count);
                        bool sent = false;
                        for (int branchOffset = 0; branchOffset < destinations.Count; branchOffset++)
                        {
                            Destination destination = destinations[(branchStart + branchOffset) % destinations.Count];
                            if (!HasHeadroom(destination, snapshot, reservedTotal, reservedResource)) continue;
                            SourceInventory(source)[(int)resource]--;
                            destination.Entity.Input[(int)resource]++;
                            Reserve(destination.Entity.Id, resource, reservedTotal, reservedResource);
                            available--;
                            moved++;
                            source.FluidCursor = Mod(source.FluidCursor + branchOffset + 1, 4);
                            sent = true;
                            break;
                        }
                        if (!sent) break;
                    }
                }

                source.FluidProgress = moved > 0 ? ClampCredit(accumulated - moved) : ClampCredit(accumulated);
                if (FactoryCatalog.IsFluidTransport(source.Kind) && !typedRejections.Contains(source.Id))
                    source.Status = moved > 0 ? "유체 이송 중 · " + moved + "L" : "유체 출구 막힘";
            }
        }

        public static int Volume(FactoryEntity entity)
        {
            if (entity == null || entity.Input == null) return 0;
            int total = 0;
            for (int i = 31; i < Math.Min(ResourceCatalog.InventoryCount, entity.Input.Count); i++) total += entity.Input[i];
            return total;
        }

        public static Resource StoredResource(FactoryEntity entity)
        {
            if (entity == null || entity.Input == null) return Resource.Coins;
            for (int i = 31; i < Math.Min(ResourceCatalog.InventoryCount, entity.Input.Count); i++)
                if (entity.Input[i] > 0) return (Resource)i;
            return Resource.Coins;
        }

        public static float FlowRate(FactoryEntity entity) => entity != null && entity.Kind == FactoryKind.FluidTank ? 12f : 8f;
        public static float Flowrate(FactoryEntity entity) => FlowRate(entity);

        Snapshot TakeSnapshot()
        {
            var snapshot = new Snapshot();
            foreach (FactoryEntity entity in ordered)
            {
                int[] quantities = new int[ResourceCatalog.InventoryCount];
                List<int> inventory = SourceInventory(entity);
                if (inventory != null)
                    for (int i = 31; i < Math.Min(quantities.Length, inventory.Count); i++) quantities[i] = inventory[i];
                snapshot.Quantities[entity.Id] = quantities;
                snapshot.Totals[entity.Id] = Total(entity.Input);
            }
            return snapshot;
        }

        List<Resource> SourceResources(FactoryEntity source, Snapshot snapshot)
        {
            var result = new List<Resource>();
            if (FactoryCatalog.IsProduction(source.Kind))
            {
                foreach (FluidPort port in FluidPorts.For(source))
                    if (!port.IsInput && snapshot.Quantities[source.Id][(int)port.Resource] > 0 && !result.Contains(port.Resource)) result.Add(port.Resource);
            }
            else if (FactoryCatalog.IsFluidTransport(source.Kind))
            {
                for (int i = 31; i < ResourceCatalog.InventoryCount; i++)
                    if (snapshot.Quantities[source.Id][i] > 0) result.Add((Resource)i);
            }
            return result;
        }

        List<Destination> Destinations(FactoryEntity source, Resource resource, Snapshot snapshot)
        {
            var result = new List<Destination>(2);
            if (FactoryCatalog.IsProduction(source.Kind))
            {
                foreach (FluidPort port in FluidPorts.For(source))
                {
                    if (port.IsInput || port.Resource != resource) continue;
                    FactoryEntity target = At(port.X, port.Z, port.Floor);
                    if (CanContainerReceive(target, source, resource)) result.Add(ContainerDestination(target, resource));
                }
                return result;
            }

            if (source.Kind == FactoryKind.FluidRiser && source.IsLinkSender)
            {
                FactoryEntity receiver = RiserReceiver(source);
                if (CanRiserReceive(receiver, source, resource)) result.Add(ContainerDestination(receiver, resource));
                return result;
            }

            foreach (int direction in OutputDirections(source))
            {
                int x = source.X + Dx[direction];
                int z = source.Z + Dz[direction];
                FactoryEntity target = At(x, z, source.Floor);
                if (CanContainerReceive(target, source, resource))
                {
                    result.Add(ContainerDestination(target, resource));
                    continue;
                }

                FactoryEntity machine = MachineAtInputPort(source.X, source.Z, source.Floor, resource);
                if (machine != null && direction == DirectionToward(source.X, source.Z, machine))
                    result.Add(MachineDestination(machine, resource));
            }
            return result;
        }

        bool CanContainerReceive(FactoryEntity target, FactoryEntity source, Resource resource)
        {
            if (target == null || target.IsStopped || !FactoryCatalog.IsFluidTransport(target.Kind)) return false;
            if (target.Floor != source.Floor) return false;
            if (target.Kind == FactoryKind.FluidRiser && (!target.IsLinkSender || !target.Powered)) return false;
            if (target.Filter != Resource.Coins && target.Filter != resource)
            {
                target.Status = "유체 필터 불일치";
                typedRejections.Add(target.Id);
                return false;
            }
            Resource stored = StoredResource(target);
            if (stored != Resource.Coins && stored != resource)
            {
                target.Status = "유체 혼합 차단";
                typedRejections.Add(target.Id);
                return false;
            }

            int incomingDirection = DirectionToward(target.X, target.Z, source);
            int front = target.Direction & 3;
            if (target.Kind == FactoryKind.FluidTank || target.Kind == FactoryKind.Pipe ||
                target.Kind == FactoryKind.PipeJunction || target.Kind == FactoryKind.FluidRiser)
                return incomingDirection != front;
            return false;
        }

        bool CanRiserReceive(FactoryEntity receiver, FactoryEntity sender, Resource resource)
        {
            if (receiver == null || receiver.IsStopped || !receiver.Powered || receiver.IsLinkSender) return false;
            if (receiver.Kind != FactoryKind.FluidRiser || receiver.LinkId != sender.Id || sender.LinkId != receiver.Id) return false;
            if (receiver.X != sender.X || receiver.Z != sender.Z || Math.Abs(receiver.Floor - sender.Floor) != 1) return false;
            if (receiver.Filter != Resource.Coins && receiver.Filter != resource)
            {
                receiver.Status = "유체 필터 불일치";
                typedRejections.Add(receiver.Id);
                return false;
            }
            Resource stored = StoredResource(receiver);
            if (stored != Resource.Coins && stored != resource)
            {
                receiver.Status = "유체 혼합 차단";
                typedRejections.Add(receiver.Id);
                return false;
            }
            return true;
        }

        FactoryEntity RiserReceiver(FactoryEntity sender)
        {
            return sender.LinkId > 0 && byId.TryGetValue(sender.LinkId, out FactoryEntity receiver) ? receiver : null;
        }

        FactoryEntity MachineAtInputPort(int portX, int portZ, int floor, Resource resource)
        {
            if (!machineInputs.TryGetValue(PortKey(portX, portZ, floor, resource), out FactoryEntity machine)) return null;
            return machine.IsStopped ? null : machine;
        }

        static IEnumerable<int> OutputDirections(FactoryEntity source)
        {
            yield return source.Direction & 3;
            if (source.Kind == FactoryKind.PipeJunction) yield return (source.Direction + 3) & 3;
        }

        static Destination ContainerDestination(FactoryEntity entity, Resource resource)
        {
            int capacity = entity.Kind == FactoryKind.FluidTank ? TankCapacity : PipeCapacity;
            return new Destination { Entity = entity, Resource = resource, Capacity = capacity, ResourceQuota = capacity };
        }

        static Destination MachineDestination(FactoryEntity entity, Resource resource)
        {
            FactorySpec spec = FactoryCatalog.Get(entity.Kind);
            RecipeSpec recipe = FactoryCatalog.GetRecipe(EffectiveRecipe(entity));
            int capacity = spec == null ? 80 : spec.InputCapacity;
            int sum = recipe == null ? 1 : Math.Max(1, recipe.Inputs.Sum(amount => amount.Amount));
            int amount = 1;
            if (recipe != null)
                foreach (RecipeAmount input in recipe.Inputs) if (input.Resource == resource) { amount = input.Amount; break; }
            int quota = Math.Max(amount, capacity * amount / sum);
            return new Destination { Entity = entity, Resource = resource, Capacity = capacity, ResourceQuota = quota };
        }

        static bool HasHeadroom(Destination destination, Snapshot snapshot, Dictionary<int, int> reservedTotal, Dictionary<long, int> reservedResource)
        {
            int id = destination.Entity.Id;
            int totalReserved = reservedTotal.TryGetValue(id, out int tr) ? tr : 0;
            if (snapshot.Totals[id] + totalReserved >= destination.Capacity) return false;
            long key = ResourceKey(id, destination.Resource);
            int resourceReserved = reservedResource.TryGetValue(key, out int rr) ? rr : 0;
            int original = destination.Entity.Input[(int)destination.Resource] - resourceReserved;
            return original + resourceReserved < destination.ResourceQuota;
        }

        static void Reserve(int id, Resource resource, Dictionary<int, int> reservedTotal, Dictionary<long, int> reservedResource)
        {
            reservedTotal[id] = (reservedTotal.TryGetValue(id, out int total) ? total : 0) + 1;
            long key = ResourceKey(id, resource);
            reservedResource[key] = (reservedResource.TryGetValue(key, out int amount) ? amount : 0) + 1;
        }

        void EnsureTopology()
        {
            long hash = TopologyHash();
            if (hash == cachedTopology) return;
            ordered.Clear();
            ordered.AddRange(state.Entities.Where(entity => entity != null));
            ordered.Sort((a, b) => a.Id.CompareTo(b.Id));
            cells.Clear();
            machineInputs.Clear();
            byId.Clear();
            foreach (FactoryEntity entity in ordered)
            {
                byId[entity.Id] = entity;
                FactorySpec spec = FactoryCatalog.Get(entity.Kind);
                if (spec == null) continue;
                for (int z = entity.Z; z < entity.Z + spec.Height; z++)
                    for (int x = entity.X; x < entity.X + spec.Width; x++) cells[CellKey(x, z, entity.Floor)] = entity;
                if (FactoryCatalog.IsProduction(entity.Kind))
                    foreach (FluidPort port in FluidPorts.For(entity))
                        if (port.IsInput && !machineInputs.ContainsKey(PortKey(port.X, port.Z, port.Floor, port.Resource)))
                            machineInputs[PortKey(port.X, port.Z, port.Floor, port.Resource)] = entity;
            }
            cachedTopology = hash;
        }

        long TopologyHash()
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                hash = Mix(hash, state.Entities.Count);
                foreach (FactoryEntity entity in state.Entities)
                {
                    if (entity == null) { hash = Mix(hash, 0); continue; }
                    hash = Mix(hash, RuntimeHelpers.GetHashCode(entity));
                    hash = Mix(hash, entity.Id);
                    hash = Mix(hash, (int)entity.Kind);
                    hash = Mix(hash, entity.X);
                    hash = Mix(hash, entity.Z);
                    hash = Mix(hash, entity.Floor);
                    hash = Mix(hash, entity.Direction);
                    hash = Mix(hash, (int)entity.Recipe);
                    hash = Mix(hash, entity.LinkId);
                    hash = Mix(hash, entity.IsLinkSender ? 1 : 0);
                }
                return hash;
            }
        }

        FactoryEntity At(int x, int z, int floor) => cells.TryGetValue(CellKey(x, z, floor), out FactoryEntity entity) ? entity : null;
        static List<int> SourceInventory(FactoryEntity entity) => FactoryCatalog.IsFluidTransport(entity.Kind) ? entity.Input : FactoryCatalog.IsProduction(entity.Kind) ? entity.Output : null;
        static float Rate(FactoryEntity entity) => entity.Kind == FactoryKind.FluidTank ? 12f : 8f;
        static float ClampCredit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) ? Math.Max(0f, Math.Min(1f, value)) : 0f;
        static int Total(List<int> inventory) { int total = 0; if (inventory != null) for (int i = 1; i < inventory.Count; i++) total += inventory[i]; return total; }
        static int DirectionToward(int x, int z, FactoryEntity target)
        {
            FactorySpec spec = FactoryCatalog.Get(target.Kind);
            int minX = target.X, maxX = target.X + spec.Width - 1, minZ = target.Z, maxZ = target.Z + spec.Height - 1;
            if (x < minX) return 0;
            if (x > maxX) return 2;
            if (z < minZ) return 1;
            return 3;
        }
        static FactoryRecipe EffectiveRecipe(FactoryEntity entity)
        {
            if (entity.Recipe != FactoryRecipe.None) return entity.Recipe;
            if (entity.Kind == FactoryKind.Drill) return FactoryRecipe.IronMining;
            if (entity.Kind == FactoryKind.WaterPump) return FactoryRecipe.WaterExtraction;
            if (entity.Kind == FactoryKind.OilPump) return FactoryRecipe.OilExtraction;
            return FactoryRecipe.None;
        }
        static long CellKey(int x, int z, int floor) => ((long)(ushort)x << 32) | ((long)(ushort)z << 16) | (ushort)floor;
        static long PortKey(int x, int z, int floor, Resource resource) => ((long)(ushort)x << 48) | ((long)(ushort)z << 32) | ((long)(byte)floor << 24) | (byte)resource;
        static long ResourceKey(int id, Resource resource) => ((long)id << 32) | (uint)resource;
        static long Mix(long hash, int value) => unchecked((hash ^ (uint)value) * 1099511628211L);
        static int Mod(int value, int modulus) { int result = value % modulus; return result < 0 ? result + modulus : result; }
    }
}

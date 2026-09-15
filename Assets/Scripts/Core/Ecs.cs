using System;
using System.Collections.Generic;

namespace Riverworks
{
    /// <summary>
    /// Stable entity handle. The version guard means a recycled index can never alias an old
    /// handle, which keeps long-running simulations and deferred destruction safe.
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Index;
        public readonly int Version;

        public Entity(int index, int version) { Index = index; Version = version; }

        public bool IsValid => Index >= 0;
        public bool Equals(Entity other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object value) => value is Entity other && Equals(other);
        public override int GetHashCode() => unchecked(Index * 397 ^ Version);
        public static bool operator ==(Entity first, Entity second) => first.Equals(second);
        public static bool operator !=(Entity first, Entity second) => !first.Equals(second);
        public override string ToString() => "Entity(" + Index + "v" + Version + ")";
    }

    public interface IEcsComponentStore
    {
        int Count { get; }
        bool RemoveAt(int entityIndex);
        void Clear();
    }

    /// <summary>
    /// Sparse-set component column. Add, remove and lookup are O(1) and the dense arrays are
    /// what systems iterate, so component data stays contiguous instead of pointer-chasing.
    /// </summary>
    public sealed class ComponentStore<T> : IEcsComponentStore where T : struct
    {
        readonly Dictionary<int, int> slots = new Dictionary<int, int>();
        int[] entities = new int[64];
        T[] values = new T[64];
        int count;

        public int Count => count;
        public bool Has(int entityIndex) => slots.ContainsKey(entityIndex);
        public int EntityAt(int denseIndex) => entities[denseIndex];
        public ref T ValueAt(int denseIndex) => ref values[denseIndex];

        public void Set(int entityIndex, in T value)
        {
            if (slots.TryGetValue(entityIndex, out int slot)) { values[slot] = value; return; }
            if (count >= values.Length)
            {
                Array.Resize(ref values, values.Length * 2);
                Array.Resize(ref entities, entities.Length * 2);
            }
            entities[count] = entityIndex;
            values[count] = value;
            slots.Add(entityIndex, count);
            count++;
        }

        public bool TryGet(int entityIndex, out T value)
        {
            if (slots.TryGetValue(entityIndex, out int slot)) { value = values[slot]; return true; }
            value = default;
            return false;
        }

        public bool RemoveAt(int entityIndex)
        {
            if (!slots.TryGetValue(entityIndex, out int slot)) return false;
            int last = count - 1;
            if (slot != last)
            {
                int moved = entities[last];
                entities[slot] = moved;
                values[slot] = values[last];
                slots[moved] = slot;
            }
            values[last] = default;
            entities[last] = 0;
            slots.Remove(entityIndex);
            count--;
            return true;
        }

        public void Clear()
        {
            slots.Clear();
            Array.Clear(values, 0, count);
            Array.Clear(entities, 0, count);
            count = 0;
        }
    }

    public struct EcsTick
    {
        public float DeltaSeconds;
        public object Context;
        public EcsTick(float deltaSeconds, object context) { DeltaSeconds = deltaSeconds; Context = context; }
    }

    public interface IEcsSystem
    {
        void Tick(EcsWorld world, in EcsTick tick);
    }

    /// <summary>
    /// Minimal deterministic ECS kernel shared by the city, factory and citizen simulations.
    /// Systems run in registration order and structural changes are deferred to the end of the
    /// tick, so iteration stays stable while entities are destroyed.
    /// </summary>
    public sealed class EcsWorld
    {
        int[] versions = new int[64];
        bool[] alive = new bool[64];
        readonly Stack<int> free = new Stack<int>();
        readonly Dictionary<Type, IEcsComponentStore> storesByType = new Dictionary<Type, IEcsComponentStore>();
        readonly List<IEcsComponentStore> componentStores = new List<IEcsComponentStore>();
        readonly List<IEcsSystem> systems = new List<IEcsSystem>();
        readonly List<int> pendingDestroy = new List<int>();
        int nextIndex;
        int entityCount;

        public int EntityCount => entityCount;

        public Entity Create()
        {
            int index;
            if (free.Count > 0) index = free.Pop();
            else
            {
                index = nextIndex++;
                if (index >= versions.Length) Grow(index + 1);
                versions[index] = 1;
            }
            alive[index] = true;
            entityCount++;
            return new Entity(index, versions[index]);
        }

        public bool IsAlive(Entity entity) => IsAlive(entity.Index, entity.Version);
        public bool IsAlive(int index, int version) =>
            index >= 0 && index < nextIndex && alive[index] && versions[index] == version;

        public Entity Handle(int index) => new Entity(index, versions[index]);

        public void Destroy(Entity entity)
        {
            if (!IsAlive(entity)) return;
            for (int i = 0; i < componentStores.Count; i++) componentStores[i].RemoveAt(entity.Index);
            alive[entity.Index] = false;
            versions[entity.Index]++;
            free.Push(entity.Index);
            entityCount--;
        }

        /// <summary>Queues a destroy so systems can safely finish iterating the dense arrays.</summary>
        public void DeferDestroy(Entity entity)
        {
            if (IsAlive(entity)) pendingDestroy.Add(entity.Index);
        }

        public void FlushStructuralChanges()
        {
            for (int i = 0; i < pendingDestroy.Count; i++)
            {
                int index = pendingDestroy[i];
                if (index >= 0 && index < nextIndex && alive[index]) Destroy(new Entity(index, versions[index]));
            }
            pendingDestroy.Clear();
        }

        public ComponentStore<T> Store<T>() where T : struct
        {
            if (storesByType.TryGetValue(typeof(T), out IEcsComponentStore existing)) return (ComponentStore<T>)existing;
            var store = new ComponentStore<T>();
            storesByType.Add(typeof(T), store);
            componentStores.Add(store);
            return store;
        }

        public void AddSystem(IEcsSystem system)
        {
            if (system != null) systems.Add(system);
        }

        public void Tick(in EcsTick tick)
        {
            for (int i = 0; i < systems.Count; i++) systems[i].Tick(this, in tick);
            FlushStructuralChanges();
        }

        void Grow(int capacity)
        {
            int size = Math.Max(capacity, versions.Length * 2);
            Array.Resize(ref versions, size);
            Array.Resize(ref alive, size);
        }
    }
}

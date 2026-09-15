using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Riverworks
{
    public enum LifeStage { Child = 0, Youth = 1, Worker = 2, Elder = 3 }

    /// <summary>
    /// Save DTO for the citizen life store. The runtime simulation owns an
    /// <see cref="EcsWorld"/>; this class is only the serialized mirror written back whenever a
    /// life system mutates the population, so the existing save format keeps working.
    /// </summary>
    [Serializable]
    public sealed class CitizenLifecycleState
    {
        public int NextId = 1;
        public List<int> Ids = new List<int>();
        public List<int> AgeDays = new List<int>();
        public List<int> Stages = new List<int>();
        public List<int> Homes = new List<int>();
        public List<int> Jobs = new List<int>();
        public List<int> Flags = new List<int>();

        public int Count => Ids == null ? 0 : Ids.Count;
    }

    /// <summary>Per-citizen life component: one dense column entry per living resident.</summary>
    public struct CitizenLifeComponent
    {
        public int Id;
        public int AgeDays;
        public int Stage;
        public int Home;
        public int Job;
        public int Retired;
    }

    /// <summary>
    /// Data-oriented citizen life simulation on the shared ECS kernel. Systems age the column,
    /// retire workers and roll deterministic mortality; the city economy only ever sees the
    /// population count and the save DTO.
    /// </summary>
    public static class CitizenLifecycle
    {
        public const int DaysPerYear = 60;
        public const int YouthAgeDays = 6 * DaysPerYear;
        public const int WorkerAgeDays = 16 * DaysPerYear;
        public const int RetirementAgeDays = 60 * DaysPerYear;
        public const int FrailAgeDays = 72 * DaysPerYear;
        public const int MaximumLifespanDays = 90 * DaysPerYear;
        public const int FlagRetired = 1;

        static readonly ConditionalWeakTable<GameState, CitizenLifeWorld> Worlds =
            new ConditionalWeakTable<GameState, CitizenLifeWorld>();

        sealed class CitizenLifeWorld
        {
            public readonly EcsWorld World = new EcsWorld();
            public readonly ComponentStore<CitizenLifeComponent> Lives;
            public readonly GameState State;
            public int NextId = 1;
            public int Day;
            public int LastDeaths;
            public bool Dirty;

            public CitizenLifeWorld(GameState state)
            {
                State = state;
                Lives = World.Store<CitizenLifeComponent>();
                World.AddSystem(new CitizenAgeingSystem());
                World.AddSystem(new CitizenMortalitySystem());
            }
        }

        sealed class CitizenAgeingSystem : IEcsSystem
        {
            public void Tick(EcsWorld world, in EcsTick tick)
            {
                ComponentStore<CitizenLifeComponent> lives = world.Store<CitizenLifeComponent>();
                for (int i = 0; i < lives.Count; i++)
                {
                    ref CitizenLifeComponent life = ref lives.ValueAt(i);
                    life.AgeDays++;
                    LifeStage stage = StageForAge(life.AgeDays);
                    life.Stage = (int)stage;
                    life.Retired = stage == LifeStage.Elder ? 1 : 0;
                }
            }
        }

        sealed class CitizenMortalitySystem : IEcsSystem
        {
            public void Tick(EcsWorld world, in EcsTick tick)
            {
                if (!(tick.Context is CitizenLifeWorld owner)) return;
                ComponentStore<CitizenLifeComponent> lives = world.Store<CitizenLifeComponent>();
                for (int i = 0; i < lives.Count; i++)
                {
                    CitizenLifeComponent life = lives.ValueAt(i);
                    if (!DiesToday(life.Id, life.AgeDays, owner.Day)) continue;
                    world.DeferDestroy(world.Handle(lives.EntityAt(i)));
                    owner.LastDeaths++;
                }
            }
        }

        /// <summary>Returns the always-present save mirror after flushing pending world changes.</summary>
        public static CitizenLifecycleState Ensure(GameState state)
        {
            CitizenLifeWorld world = WorldFor(state);
            if (world == null) return null;
            Flush(world);
            return world.State.Citizens;
        }

        /// <summary>Seeds, trims or keeps records so the store always matches state.Population.</summary>
        public static void Reconcile(GameState state)
        {
            CitizenLifeWorld world = WorldFor(state);
            if (world == null) return;
            int target = Math.Max(0, state.Population);
            while (world.Lives.Count > target) DestroyDense(world, world.Lives.Count - 1);
            while (world.Lives.Count < target) CreateCitizen(world, SeededAge(world.NextId), -1, -1);
            state.Population = world.Lives.Count;
            Flush(world);
        }

        /// <summary>Runs ageing, retirement and mortality for the requested number of days.</summary>
        public static int Advance(GameState state, int days)
        {
            CitizenLifeWorld world = WorldFor(state);
            if (world == null || days <= 0) return 0;
            int deaths = 0;
            for (int day = 0; day < days; day++)
            {
                world.Day = state.Day + day;
                world.LastDeaths = 0;
                world.World.Tick(new EcsTick(1f, world));
                deaths += world.LastDeaths;
            }
            world.Dirty = true;
            if (deaths > 0) state.Population = world.Lives.Count;
            Flush(world);
            return deaths;
        }

        public static void RecordBirth(GameState state)
        {
            CitizenLifeWorld world = WorldFor(state);
            if (world == null) return;
            CreateCitizen(world, 0, -1, -1);
            state.Population = world.Lives.Count;
            Flush(world);
        }

        /// <summary>Removes a working adult first, then the newest dependent.</summary>
        public static bool RemoveOne(GameState state)
        {
            CitizenLifeWorld world = WorldFor(state);
            if (world == null || world.Lives.Count == 0) return false;
            int chosen = world.Lives.Count - 1;
            for (int i = 0; i < world.Lives.Count; i++)
            {
                if (world.Lives.ValueAt(i).Stage != (int)LifeStage.Worker) continue;
                chosen = i;
                break;
            }
            DestroyDense(world, chosen);
            state.Population = world.Lives.Count;
            Flush(world);
            return true;
        }

        public static void SyncPopulation(GameState state)
        {
            CitizenLifeWorld world = WorldFor(state);
            if (world != null) state.Population = world.Lives.Count;
        }

        public static int Count(GameState state)
        {
            CitizenLifeWorld world = WorldFor(state);
            return world == null ? 0 : world.Lives.Count;
        }

        public static int CountStage(GameState state, LifeStage stage)
        {
            CitizenLifeWorld world = WorldFor(state);
            if (world == null) return 0;
            int total = 0;
            for (int i = 0; i < world.Lives.Count; i++) if (world.Lives.ValueAt(i).Stage == (int)stage) total++;
            return total;
        }

        public static int Workers(GameState state) => CountStage(state, LifeStage.Worker);
        public static int Dependents(GameState state) => CountStage(state, LifeStage.Child) + CountStage(state, LifeStage.Youth);

        public static LifeStage StageForAge(int ageDays)
        {
            if (ageDays < YouthAgeDays) return LifeStage.Child;
            if (ageDays < WorkerAgeDays) return LifeStage.Youth;
            if (ageDays < RetirementAgeDays) return LifeStage.Worker;
            return LifeStage.Elder;
        }

        public static float AgeYears(int ageDays) => ageDays / (float)DaysPerYear;

        static CitizenLifeWorld WorldFor(GameState state)
        {
            if (state == null) return null;
            return Worlds.GetValue(state, key =>
            {
                var world = new CitizenLifeWorld(key);
                Build(world);
                return world;
            });
        }

        static void Build(CitizenLifeWorld world)
        {
            CitizenLifecycleState dto = world.State.Citizens ??= new CitizenLifecycleState();
            Repair(dto);
            for (int i = 0; i < dto.Count; i++)
            {
                Entity entity = world.World.Create();
                world.Lives.Set(entity.Index, new CitizenLifeComponent
                {
                    Id = dto.Ids[i],
                    AgeDays = dto.AgeDays[i],
                    Stage = dto.Stages[i],
                    Home = dto.Homes[i],
                    Job = dto.Jobs[i],
                    Retired = dto.Flags[i] & FlagRetired
                });
            }
            world.NextId = dto.NextId;
        }

        static void Flush(CitizenLifeWorld world)
        {
            if (!world.Dirty) return;
            CitizenLifecycleState dto = world.State.Citizens ??= new CitizenLifecycleState();
            dto.Ids.Clear(); dto.AgeDays.Clear(); dto.Stages.Clear();
            dto.Homes.Clear(); dto.Jobs.Clear(); dto.Flags.Clear();
            for (int i = 0; i < world.Lives.Count; i++)
            {
                CitizenLifeComponent life = world.Lives.ValueAt(i);
                dto.Ids.Add(life.Id);
                dto.AgeDays.Add(life.AgeDays);
                dto.Stages.Add(life.Stage);
                dto.Homes.Add(life.Home);
                dto.Jobs.Add(life.Job);
                dto.Flags.Add(life.Retired);
            }
            dto.NextId = world.NextId;
            world.Dirty = false;
        }

        static void CreateCitizen(CitizenLifeWorld world, int ageDays, int home, int job)
        {
            Entity entity = world.World.Create();
            LifeStage stage = StageForAge(ageDays);
            world.Lives.Set(entity.Index, new CitizenLifeComponent
            {
                Id = world.NextId++,
                AgeDays = ageDays,
                Stage = (int)stage,
                Home = home,
                Job = job,
                Retired = stage == LifeStage.Elder ? 1 : 0
            });
            world.Dirty = true;
        }

        static void DestroyDense(CitizenLifeWorld world, int denseIndex)
        {
            if (denseIndex < 0 || denseIndex >= world.Lives.Count) return;
            world.World.Destroy(world.World.Handle(world.Lives.EntityAt(denseIndex)));
            world.Dirty = true;
        }

        static void Repair(CitizenLifecycleState citizens)
        {
            if (citizens.Ids != null && citizens.AgeDays != null && citizens.Stages != null &&
                citizens.Homes != null && citizens.Jobs != null && citizens.Flags != null &&
                citizens.Ids.Count == citizens.AgeDays.Count && citizens.Ids.Count == citizens.Stages.Count &&
                citizens.Ids.Count == citizens.Homes.Count && citizens.Ids.Count == citizens.Jobs.Count &&
                citizens.Ids.Count == citizens.Flags.Count) return;

            citizens.Ids ??= new List<int>();
            citizens.AgeDays ??= new List<int>();
            citizens.Stages ??= new List<int>();
            citizens.Homes ??= new List<int>();
            citizens.Jobs ??= new List<int>();
            citizens.Flags ??= new List<int>();
            int count = Math.Min(Math.Min(citizens.Ids.Count, citizens.AgeDays.Count),
                Math.Min(Math.Min(citizens.Stages.Count, citizens.Homes.Count),
                    Math.Min(citizens.Jobs.Count, citizens.Flags.Count)));
            Trim(citizens.Ids, count); Trim(citizens.AgeDays, count); Trim(citizens.Stages, count);
            Trim(citizens.Homes, count); Trim(citizens.Jobs, count); Trim(citizens.Flags, count);
            int maxId = 0;
            for (int i = 0; i < citizens.Count; i++) if (citizens.Ids[i] > maxId) maxId = citizens.Ids[i];
            if (citizens.NextId <= maxId) citizens.NextId = maxId + 1;
        }

        static void Trim(List<int> values, int count)
        {
            while (values.Count > count) values.RemoveAt(values.Count - 1);
        }

        static int SeededAge(int id)
        {
            int slot = StableRoll(id, 17) % 100;
            if (slot < 22) return YouthAgeDays + slot * 7;
            if (slot < 78) return WorkerAgeDays + (slot - 22) * 24;
            return RetirementAgeDays + (slot - 78) * 40;
        }

        static bool DiesToday(int id, int age, int day)
        {
            if (age >= MaximumLifespanDays) return true;
            if (age < FrailAgeDays) return false;
            int span = Math.Max(1, MaximumLifespanDays - FrailAgeDays);
            int permille = 1 + (age - FrailAgeDays) * 12 / span;
            return StableRoll(id, day) % 1000 < permille;
        }

        static int StableRoll(int id, int salt)
        {
            unchecked
            {
                uint value = (uint)id * 747796405u + (uint)salt * 2891336453u + 0x9E3779B9u;
                value ^= value >> 15;
                value *= 0x85EBCA6Bu;
                value ^= value >> 13;
                return (int)(value & 0x7FFFFFFF);
            }
        }
    }
}

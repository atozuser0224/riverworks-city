using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Riverworks
{
    public class FactorySimulation
    {
        const int MachineCapacity = 24, StorageCapacity = 80;
        static readonly int[] Dx = { 1, 0, -1, 0 }, Dz = { 0, 1, 0, -1 };
        readonly List<FactoryEntity> processingOrder = new List<FactoryEntity>();
        readonly List<FactoryEntity> reverseTransportOrder = new List<FactoryEntity>();
        readonly List<FactoryEntity> powerNodes = new List<FactoryEntity>();
        readonly HashSet<int> livePowerNodes = new HashSet<int>();
        readonly HashSet<int> cargoAtTickStart = new HashSet<int>();
        readonly IFactoryEnvironment environment;
        long cachedStructureHash = long.MinValue, cachedPowerHash = long.MinValue;
        bool hasStructureCache, hasPowerCache;
        public FactoryState State { get; }
        public float PowerUsed { get; private set; }
        public float PowerAvailable { get; private set; }
        public int MovingItems { get; private set; }
        public float BeltSpeedMultiplier { get; private set; } = 1f;
        public float InserterSpeedMultiplier { get; private set; } = 1f;
        public float MachineSpeedMultiplier { get; private set; } = 1f;
        public float PowerDemandMultiplier { get; private set; } = 1f;

        public FactorySimulation(FactoryState state, IFactoryEnvironment environment = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            this.environment = environment;
            ValidateState(state); Normalize(); Recalculate();
        }

        public void ConfigureTechnology(GameState gameState)
        {
            BeltSpeedMultiplier = TechCatalog.Has(gameState, TechId.Logistics) ? 1.5f : 1f;
            InserterSpeedMultiplier = TechCatalog.Has(gameState, TechId.Automation) ? 1.5f : 1f;
            MachineSpeedMultiplier = TechCatalog.Has(gameState, TechId.MassProduction) ? 1.25f : 1f;
            PowerDemandMultiplier = TechCatalog.Has(gameState, TechId.Electrification) ? .85f : 1f;
            Recalculate();
        }

        public FactoryEntity GetAt(int x, int z)
        {
            foreach (FactoryEntity e in State.Entities)
            {
                FactorySpec spec = FactoryCatalog.Get(e.Kind); if (spec != null && x >= e.X && x < e.X + spec.Width && z >= e.Z && z < e.Z + spec.Height) return e;
            }
            return null;
        }

        public bool CanPlace(FactoryKind kind, int x, int z, int direction, out string reason)
        {
            FactorySpec spec = FactoryCatalog.Get(kind);
            if (spec == null || kind == FactoryKind.None) { reason = "알 수 없는 공장 설비입니다."; return false; }
            if (direction < 0 || direction > 3) { reason = "방향 값이 올바르지 않습니다."; return false; }
            if (x < 0 || z < 0 || x + spec.Width > State.Width || z + spec.Height > State.Height) { reason = "공장 부지 경계를 벗어납니다."; return false; }
            for (int zz = z; zz < z + spec.Height; zz++) for (int xx = x; xx < x + spec.Width; xx++)
                if (GetAt(xx, zz) != null) { reason = "이미 다른 설비가 차지한 자리입니다."; return false; }
            if (environment != null && !environment.CanPlace(kind, x, z, direction, out reason)) return false;
            if (kind == FactoryKind.Drill)
            {
                bool ore = false; for (int zz = z; zz < z + spec.Height; zz++) for (int xx = x; xx < x + spec.Width; xx++) ore |= environment != null ? environment.HasOre(xx, zz) : HasOre(xx, zz);
                if (!ore) { reason = "철광맥 위에만 채굴기를 놓을 수 있습니다."; return false; }
            }
            reason = ""; return true;
        }

        public bool TryPlace(FactoryKind kind, int x, int z, int direction, out string reason)
        {
            if (!CanPlace(kind, x, z, direction, out reason)) return false;
            var e = new FactoryEntity { Id = State.NextEntityId++, Kind = kind, X = x, Z = z, Direction = direction };
            if (kind == FactoryKind.Furnace) e.Recipe = FactoryRecipe.IronPlate;
            State.Entities.Add(e); Recalculate(); return true;
        }

        public bool Remove(int x, int z, out string reason)
        {
            FactoryEntity e = GetAt(x, z); if (e == null) { reason = "철거할 설비가 없습니다."; return false; }
            EnsureInventory(State.Recovered);
            for (int i = 1; i < 9; i++) { State.Recovered[i] += e.Input[i] + e.Output[i]; e.Input[i] = e.Output[i] = 0; }
            if (IsCargo(e.CargoResource)) { State.Recovered[(int)e.CargoResource]++; e.CargoResource = Resource.Coins; e.CargoProgress = 0; }
            State.Entities.Remove(e); Recalculate(); reason = ""; return true;
        }

        public bool Rotate(int id, out string reason)
        {
            FactoryEntity e = Find(id); if (e == null) { reason = "설비를 찾을 수 없습니다."; return false; }
            if (IsCargo(e.CargoResource)) { reason = "물자를 운반 중에는 회전할 수 없습니다."; return false; }
            e.Direction = (e.Direction + 1) & 3; reason = ""; return true;
        }

        public bool SetRecipe(int id, FactoryRecipe recipe, out string reason)
        {
            FactoryEntity e = Find(id); if (e == null) { reason = "설비를 찾을 수 없습니다."; return false; }
            bool valid = e.Kind == FactoryKind.Furnace ? recipe == FactoryRecipe.IronPlate : e.Kind == FactoryKind.Assembler && (recipe == FactoryRecipe.Tools || recipe == FactoryRecipe.Flour || recipe == FactoryRecipe.Bread);
            if (!valid) { reason = "이 설비에서 사용할 수 없는 제조법입니다."; return false; }
            if (e.Progress > 0 || Total(e.Input) > 0) { reason = "원료가 있거나 가공 중에는 제조법을 바꿀 수 없습니다."; return false; }
            e.Recipe = recipe; reason = ""; return true;
        }

        public bool SetFilter(int id, Resource resource, out string reason)
        {
            FactoryEntity e = Find(id); if (e == null || e.Kind != FactoryKind.Inserter) { reason = "투입기를 선택해야 합니다."; return false; }
            if ((int)resource < 0 || (int)resource > 8) { reason = "알 수 없는 물자입니다."; return false; }
            e.Filter = resource; reason = ""; return true;
        }

        public bool AddInput(int id, Resource resource, int amount, out string reason)
        {
            FactoryEntity e = Find(id);
            if (e == null || (e.Kind != FactoryKind.ImportDock && e.Kind != FactoryKind.Storage)) { reason = "반입 부두나 창고에만 물자를 넣을 수 있습니다."; return false; }
            if (!IsCargo(resource) || amount <= 0) { reason = "올바른 물자와 수량이 필요합니다."; return false; }
            int free = StorageCapacity - Total(e.Input); if (amount > free) { reason = "보관 공간이 부족합니다."; return false; }
            e.Input[(int)resource] += amount; reason = ""; return true;
        }

        public int TakeExports(Resource resource, int maximum = int.MaxValue)
        {
            if (!IsCargo(resource) || maximum <= 0) return 0;
            int taken = 0;
            foreach (FactoryEntity e in State.Entities.Where(x => x.Kind == FactoryKind.ExportDock))
            {
                if (environment != null && !environment.CanExport(e)) continue;
                int n = Math.Min(e.Input[(int)resource], maximum - taken); e.Input[(int)resource] -= n; taken += n;
                if (taken >= maximum) break;
            }
            State.Exported[(int)resource] += taken; return taken;
        }

        public void Tick(float seconds)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            Recalculate(); State.ElapsedSeconds += seconds;
            cargoAtTickStart.Clear();
            foreach (FactoryEntity e in processingOrder) if (IsCargo(e.CargoResource)) cargoAtTickStart.Add(e.Id);
            foreach (FactoryEntity e in processingOrder) ProcessMachine(e, seconds);
            foreach (FactoryEntity e in processingOrder) if (e.Kind == FactoryKind.Inserter && e.Powered) ProcessInserter(e, seconds * InserterSpeedMultiplier, cargoAtTickStart.Contains(e.Id));
            foreach (FactoryEntity e in reverseTransportOrder) if (cargoAtTickStart.Contains(e.Id)) ProcessBelt(e, seconds * BeltSpeedMultiplier);
            foreach (FactoryEntity e in processingOrder) if (e.Kind == FactoryKind.Drill && e.Powered) EmitDrill(e);
            Recalculate();
        }

        public void Recalculate()
        {
            Normalize();
            long structureHash = StructureHash();
            if (!hasStructureCache || structureHash != cachedStructureHash) RebuildEntityCaches(structureHash);
            long powerHash = PowerHash(structureHash);
            if (!hasPowerCache || powerHash != cachedPowerHash) RecalculatePowerGraph(powerHash);
            int moving = 0; foreach (FactoryEntity e in State.Entities) if (IsCargo(e.CargoResource)) moving++;
            MovingItems = moving;
        }

        public void InvalidateEnvironment()
        {
            hasPowerCache = false;
            Recalculate();
        }

        public static bool HasOre(int x, int z) => x >= 1 && x <= 4 && z >= 6 && z <= 10;

        public static void ValidateState(FactoryState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Version != 1 || state.Width <= 0 || state.Height <= 0 || state.Width > 256 || state.Height > 256) throw new InvalidDataException("지원하지 않는 공장 저장 형식입니다.");
            if (state.Entities == null || state.Produced == null || state.Exported == null || state.Recovered == null) throw new InvalidDataException("공장 저장 데이터가 비어 있습니다.");
            if (state.PowerBudget < 0 || state.PowerBudget > 1000000 || !Finite(state.ElapsedSeconds) || state.ElapsedSeconds < 0) throw new InvalidDataException("잘못된 공장 전력 또는 시간 데이터입니다.");
            var ids = new HashSet<int>(); var occupied = new HashSet<int>();
            foreach (FactoryEntity e in state.Entities)
            {
                FactorySpec spec = e == null ? null : FactoryCatalog.Get(e.Kind);
                if (spec == null || e.Id <= 0 || !ids.Add(e.Id) || e.Direction < 0 || e.Direction > 3 || e.X < 0 || e.Z < 0 || e.X + spec.Width > state.Width || e.Z + spec.Height > state.Height) throw new InvalidDataException("잘못된 공장 설비 데이터입니다.");
                if (e.Input == null || e.Output == null || e.Input.Count != 9 || e.Output.Count != 9 || e.Input.Any(v => v < 0) || e.Output.Any(v => v < 0) || e.Input[0] != 0 || e.Output[0] != 0) throw new InvalidDataException("잘못된 공장 물자 데이터입니다.");
                if (!Finite(e.CargoProgress) || e.CargoProgress < 0 || e.CargoProgress > 1 || !Finite(e.Progress) || e.Progress < 0) throw new InvalidDataException("잘못된 공장 진행률입니다.");
                if ((e.CargoResource == Resource.Coins && e.CargoProgress != 0) || (!IsCargo(e.CargoResource) && e.CargoResource != Resource.Coins) || (IsCargo(e.CargoResource) && e.Kind != FactoryKind.Belt && e.Kind != FactoryKind.Splitter && e.Kind != FactoryKind.Inserter)) throw new InvalidDataException("잘못된 운송 물자입니다.");
                if ((int)e.Filter < 0 || (int)e.Filter > 8 || (e.Kind != FactoryKind.Inserter && e.Filter != Resource.Coins)) throw new InvalidDataException("잘못된 투입기 필터입니다.");
                bool recipeOkay = e.Kind == FactoryKind.Furnace ? e.Recipe == FactoryRecipe.IronPlate : e.Kind == FactoryKind.Assembler ? e.Recipe == FactoryRecipe.None || e.Recipe == FactoryRecipe.Tools || e.Recipe == FactoryRecipe.Flour || e.Recipe == FactoryRecipe.Bread : e.Recipe == FactoryRecipe.None;
                if (!recipeOkay) throw new InvalidDataException("설비와 제조법이 맞지 않습니다.");
                float maxProgress = e.Kind == FactoryKind.Drill ? 2 : FactoryCatalog.RecipeDuration(e.Recipe);
                if (e.Progress > maxProgress) throw new InvalidDataException("설비 진행률이 제조 주기를 벗어납니다.");
                int inputTotal = Total(e.Input), outputTotal = Total(e.Output);
                if ((e.Kind == FactoryKind.Storage || e.Kind == FactoryKind.ImportDock || e.Kind == FactoryKind.ExportDock) && inputTotal > StorageCapacity) throw new InvalidDataException("보관 설비 용량을 초과했습니다.");
                if ((e.Kind == FactoryKind.Drill || e.Kind == FactoryKind.Furnace || e.Kind == FactoryKind.Assembler) && (inputTotal > MachineCapacity || outputTotal > MachineCapacity)) throw new InvalidDataException("생산 설비 용량을 초과했습니다.");
                if (e.Kind == FactoryKind.Drill && (((state.Width != 42 || state.Height != 42) && !HasOreInFootprint(e)) || inputTotal != 0 || e.Output.Where((v, i) => i != (int)Resource.Ore && v != 0).Any())) throw new InvalidDataException("잘못된 채굴기 데이터입니다.");
                if (e.Kind == FactoryKind.Furnace && (e.Input.Where((v, i) => i != (int)Resource.Ore && v != 0).Any() || e.Output.Where((v, i) => i != (int)Resource.Steel && v != 0).Any())) throw new InvalidDataException("잘못된 용광로 물자입니다.");
                if (e.Kind == FactoryKind.Assembler && e.Input.Where((v, i) => i > 0 && v != 0 && !NeededBy(e.Recipe, (Resource)i)).Any()) throw new InvalidDataException("조립기 제조법과 원료가 맞지 않습니다.");
                bool buffersAllowed = e.Kind == FactoryKind.Drill || e.Kind == FactoryKind.Furnace || e.Kind == FactoryKind.Assembler || e.Kind == FactoryKind.Storage || e.Kind == FactoryKind.ImportDock || e.Kind == FactoryKind.ExportDock;
                if (!buffersAllowed && (inputTotal != 0 || outputTotal != 0)) throw new InvalidDataException("운송 설비에 잘못된 버퍼가 있습니다.");
                if ((e.Kind == FactoryKind.Storage || e.Kind == FactoryKind.ImportDock || e.Kind == FactoryKind.ExportDock) && outputTotal != 0) throw new InvalidDataException("보관 설비에 잘못된 출력 버퍼가 있습니다.");
                for (int z = e.Z; z < e.Z + spec.Height; z++) for (int x = e.X; x < e.X + spec.Width; x++) if (!occupied.Add(z * state.Width + x)) throw new InvalidDataException("공장 설비가 겹칩니다.");
            }
            if (state.NextEntityId <= 0 || ids.Any(id => id >= state.NextEntityId)) throw new InvalidDataException("다음 설비 ID가 올바르지 않습니다.");
            ValidateInventory(state.Produced); ValidateInventory(state.Exported); ValidateInventory(state.Recovered);
        }

        void ProcessMachine(FactoryEntity e, float seconds)
        {
            if (!e.Powered || (e.Kind != FactoryKind.Drill && e.Kind != FactoryKind.Furnace && e.Kind != FactoryKind.Assembler)) return;
            if (e.Kind == FactoryKind.Drill)
            {
                if (Total(e.Output) >= MachineCapacity) { e.Status = "출력 공간 부족"; return; }
                e.Progress += seconds; e.Status = "철광석 채굴 중";
                while (e.Progress >= 2f && Total(e.Output) < MachineCapacity) { e.Progress -= 2f; e.Output[(int)Resource.Ore]++; State.Produced[(int)Resource.Ore]++; }
                return;
            }
            Recipe(e.Recipe, out Resource a, out int ac, out Resource b, out int bc, out Resource output, out int count, out float duration);
            if (!IsCargo(output)) { e.Status = "제조법을 선택하세요"; e.Progress = 0; return; }
            bool inputs = e.Input[(int)a] >= ac && (bc == 0 || e.Input[(int)b] >= bc);
            if (!inputs) { e.Status = "제조법 원료 부족"; e.Progress = 0; return; }
            if (Total(e.Output) + count > MachineCapacity) { e.Status = "출력 공간 부족"; return; }
            e.Progress += seconds * MachineSpeedMultiplier; e.Status = "가공 중";
            if (e.Progress >= duration)
            {
                e.Progress -= duration; e.Input[(int)a] -= ac; if (bc > 0) e.Input[(int)b] -= bc;
                e.Output[(int)output] += count; State.Produced[(int)output] += count; e.Status = "생산 완료";
            }
        }

        void EmitDrill(FactoryEntity e)
        {
            if (e.Output[(int)Resource.Ore] <= 0) return;
            CellInFront(e, out int x, out int z); FactoryEntity target = GetAt(x, z);
            if (target == null && environment != null && environment.TryGive(x, z, Resource.Ore))
            {
                e.Output[(int)Resource.Ore]--;
                e.Status = "도시로 광석 전달";
                return;
            }
            if (target != null && Accept(target, Resource.Ore, false)) { e.Output[(int)Resource.Ore]--; e.Status = "철광석 배출"; }
            else if (e.Output[(int)Resource.Ore] > 0) e.Status = "출구 막힘";
        }

        void ProcessInserter(FactoryEntity e, float seconds, bool startedWithCargo)
        {
            if (startedWithCargo)
            {
                e.CargoProgress = Math.Min(1, e.CargoProgress + seconds * 1.5f);
                if (e.CargoProgress >= 1 && environment != null)
                {
                    CellInFront(e, out int cityX, out int cityZ);
                    if (GetAt(cityX, cityZ) == null && environment.TryGive(cityX, cityZ, e.CargoResource))
                    {
                        ClearCargo(e);
                        return;
                    }
                }
                if (e.CargoProgress >= 1) { CellInFront(e, out int x, out int z); FactoryEntity target = GetAt(x, z); if (target != null && Accept(target, e.CargoResource, true)) ClearCargo(e); else e.Status = "내려놓을 공간 없음"; }
                return;
            }
            if (IsCargo(e.CargoResource)) return;
            int bx = e.X - Dx[e.Direction], bz = e.Z - Dz[e.Direction]; FactoryEntity source = GetAt(bx, bz);
            if (source == null && environment != null && environment.TryTake(bx, bz, e.Filter, out Resource externalItem))
            {
                e.CargoResource = externalItem;
                e.CargoProgress = 0;
                e.Status = "도시 물자 운반 중";
                return;
            }
            if (source != null && TakeOne(source, e.Filter, out Resource item)) { e.CargoResource = item; e.CargoProgress = 0; e.Status = "물자 운반 중"; }
            else e.Status = "집을 물자 없음";
        }

        void ProcessBelt(FactoryEntity e, float seconds)
        {
            if (!IsCargo(e.CargoResource)) { e.CargoProgress = 0; return; }
            e.CargoProgress = Math.Min(1, e.CargoProgress + seconds * 2f); if (e.CargoProgress < 1) return;
            int firstDir = e.Direction, secondDir = (e.Direction + 1) & 3;
            if (e.Kind == FactoryKind.Splitter && e.SplitLeft) { firstDir = secondDir; secondDir = e.Direction; }
            bool moved = TrySend(e, firstDir); if (!moved && e.Kind == FactoryKind.Splitter) moved = TrySend(e, secondDir);
            if (moved && e.Kind == FactoryKind.Splitter) e.SplitLeft = !e.SplitLeft; else if (!moved) e.Status = "벨트 정체";
        }

        bool TrySend(FactoryEntity e, int direction)
        {
            int targetX = e.X + Dx[direction], targetZ = e.Z + Dz[direction];
            FactoryEntity target = GetAt(targetX, targetZ); Resource item = e.CargoResource;
            if (target != null && (target.Kind == FactoryKind.Belt || target.Kind == FactoryKind.Splitter) && target.X + Dx[target.Direction] == e.X && target.Z + Dz[target.Direction] == e.Z) return false;
            if (target == null && environment != null && environment.TryGive(targetX, targetZ, item)) { ClearCargo(e); return true; }
            if (target != null && Accept(target, item, false)) { ClearCargo(e); return true; } return false;
        }

        bool Accept(FactoryEntity target, Resource item, bool fromInserter)
        {
            if (!IsCargo(item)) return false;
            if (target.Kind == FactoryKind.Belt || target.Kind == FactoryKind.Splitter || target.Kind == FactoryKind.Inserter)
            {
                if (IsCargo(target.CargoResource)) return false; target.CargoResource = item; target.CargoProgress = 0; target.Status = "운송 중"; return true;
            }
            if (target.Kind == FactoryKind.ExportDock || target.Kind == FactoryKind.Storage || target.Kind == FactoryKind.ImportDock)
            {
                if (Total(target.Input) >= StorageCapacity) return false; target.Input[(int)item]++; target.Status = target.Kind == FactoryKind.ExportDock ? "반출 대기" : "보관 중"; return true;
            }
            if (fromInserter && (target.Kind == FactoryKind.Furnace || target.Kind == FactoryKind.Assembler))
            {
                if (!NeededBy(target.Recipe, item) || Total(target.Input) >= MachineCapacity) return false;
                int ingredientLimit = RecipeIngredientCount(target.Recipe) > 1 ? MachineCapacity / 2 : MachineCapacity;
                if (target.Input[(int)item] >= ingredientLimit) return false;
                target.Input[(int)item]++; return true;
            }
            return false;
        }

        bool TakeOne(FactoryEntity source, Resource filter, out Resource item)
        {
            item = Resource.Coins;
            if ((source.Kind == FactoryKind.Belt || source.Kind == FactoryKind.Splitter) && IsCargo(source.CargoResource) && Matches(source.CargoResource, filter)) { item = source.CargoResource; ClearCargo(source); return true; }
            List<int> inventory = source.Kind == FactoryKind.Furnace || source.Kind == FactoryKind.Assembler || source.Kind == FactoryKind.Drill ? source.Output : (source.Kind == FactoryKind.Storage || source.Kind == FactoryKind.ImportDock ? source.Input : null);
            if (inventory == null) return false;
            for (int i = 1; i < 9; i++) if (inventory[i] > 0 && Matches((Resource)i, filter)) { inventory[i]--; item = (Resource)i; return true; }
            return false;
        }

        static void Recipe(FactoryRecipe recipe, out Resource a, out int ac, out Resource b, out int bc, out Resource output, out int count, out float duration)
        {
            a = b = output = Resource.Coins; ac = bc = count = 0; duration = FactoryCatalog.RecipeDuration(recipe);
            switch (recipe) {
                case FactoryRecipe.IronPlate: a = Resource.Ore; ac = 2; output = Resource.Steel; count = 1; break;
                case FactoryRecipe.Tools: a = Resource.Steel; ac = 1; b = Resource.Timber; bc = 1; output = Resource.Tools; count = 1; break;
                case FactoryRecipe.Flour: a = Resource.Grain; ac = 2; output = Resource.Flour; count = 2; break;
                case FactoryRecipe.Bread: a = Resource.Flour; ac = 2; output = Resource.Bread; count = 3; break;
            }
        }

        static bool NeededBy(FactoryRecipe recipe, Resource resource) { Recipe(recipe, out var a, out _, out var b, out int bc, out _, out _, out _); return resource == a || (bc > 0 && resource == b); }
        static int RecipeIngredientCount(FactoryRecipe recipe) { Recipe(recipe, out _, out _, out _, out int bc, out _, out _, out _); return bc > 0 ? 2 : 1; }
        static bool Matches(Resource item, Resource filter) => filter == Resource.Coins || item == filter;
        static bool IsCargo(Resource r) => (int)r >= 1 && (int)r <= 8;
        static int Total(List<int> inventory) { int n = 0; for (int i = 1; i < Math.Min(9, inventory.Count); i++) n += inventory[i]; return n; }
        long StructureHash()
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                hash = Mix(hash, State.Entities.Count);
                foreach (FactoryEntity e in State.Entities)
                {
                    hash = Mix(hash, RuntimeHelpers.GetHashCode(e));
                    hash = Mix(hash, e.Id); hash = Mix(hash, (int)e.Kind);
                    hash = Mix(hash, e.X); hash = Mix(hash, e.Z);
                }
                return hash;
            }
        }
        void RebuildEntityCaches(long structureHash)
        {
            processingOrder.Clear(); processingOrder.AddRange(State.Entities);
            processingOrder.Sort((a, b) => a.Id.CompareTo(b.Id));
            reverseTransportOrder.Clear();
            for (int i = processingOrder.Count - 1; i >= 0; i--)
            {
                FactoryEntity e = processingOrder[i];
                if (e.Kind == FactoryKind.Belt || e.Kind == FactoryKind.Splitter) reverseTransportOrder.Add(e);
            }
            cachedStructureHash = structureHash; hasStructureCache = true; hasPowerCache = false;
        }
        long PowerHash(long structureHash)
        {
            long hash = Mix(structureHash, State.PowerBudget);
            return Mix(hash, PowerDemandMultiplier.GetHashCode());
        }
        void RecalculatePowerGraph(long powerHash)
        {
            powerNodes.Clear(); livePowerNodes.Clear();
            foreach (FactoryEntity e in processingOrder)
            {
                if (e.Kind != FactoryKind.PowerInlet && e.Kind != FactoryKind.Pole) continue;
                powerNodes.Add(e);
                if (e.Kind == FactoryKind.PowerInlet && (environment == null || environment.CanSupplyPower(e))) livePowerNodes.Add(e.Id);
            }
            bool changed;
            do
            {
                changed = false;
                foreach (FactoryEntity node in powerNodes)
                {
                    if (livePowerNodes.Contains(node.Id)) continue;
                    foreach (FactoryEntity source in powerNodes)
                    {
                        if (!livePowerNodes.Contains(source.Id) || GridDistance(node, source) > 6) continue;
                        livePowerNodes.Add(node.Id); changed = true; break;
                    }
                }
            } while (changed);
            foreach (FactoryEntity node in powerNodes)
            {
                node.Powered = livePowerNodes.Contains(node.Id);
                node.Status = node.Powered ? "전력망 연결" : "전력 인입구와 연결 필요";
            }
            PowerAvailable = livePowerNodes.Count > 0 ? Math.Max(0, State.PowerBudget) : 0;
            PowerUsed = 0;
            foreach (FactoryEntity e in processingOrder)
            {
                float demand = Demand(e);
                if (demand <= 0)
                {
                    if (e.Kind != FactoryKind.PowerInlet && e.Kind != FactoryKind.Pole) e.Powered = true;
                    continue;
                }
                bool covered = false;
                foreach (FactoryEntity node in powerNodes)
                {
                    if (livePowerNodes.Contains(node.Id) && GridDistance(e, node) <= 4) { covered = true; break; }
                }
                e.Powered = covered && PowerUsed + demand <= PowerAvailable + .0001f;
                if (e.Powered) PowerUsed += demand;
                else e.Status = covered ? "도시 전력 예산 부족" : "전력망 범위 밖";
            }
            cachedPowerHash = powerHash; hasPowerCache = true;
        }
        static long Mix(long hash, int value) => unchecked((hash ^ (uint)value) * 1099511628211L);
        float Demand(FactoryEntity e) => (FactoryCatalog.Get(e.Kind)?.PowerDemand ?? 0) * PowerDemandMultiplier;
        FactoryEntity Find(int id) => State.Entities.FirstOrDefault(e => e.Id == id);
        static void ClearCargo(FactoryEntity e) { e.CargoResource = Resource.Coins; e.CargoProgress = 0; }
        static void EnsureInventory(List<int> values) { while (values.Count < 9) values.Add(0); if (values.Count > 9) values.RemoveRange(9, values.Count - 9); values[0] = 0; }
        void Normalize()
        {
            EnsureInventory(State.Produced); EnsureInventory(State.Exported); EnsureInventory(State.Recovered);
            int maxId = 0;
            foreach (FactoryEntity e in State.Entities) { EnsureInventory(e.Input); EnsureInventory(e.Output); if (e.Id > maxId) maxId = e.Id; }
            State.NextEntityId = Math.Max(State.NextEntityId, maxId + 1);
        }
        static void ValidateInventory(List<int> values) { if (values.Count != 9 || values.Any(v => v < 0) || values[0] != 0) throw new InvalidDataException("잘못된 공장 누적 물자 데이터입니다."); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool HasOreInFootprint(FactoryEntity e) { FactorySpec s = FactoryCatalog.Get(e.Kind); for (int z = e.Z; z < e.Z + s.Height; z++) for (int x = e.X; x < e.X + s.Width; x++) if (HasOre(x, z)) return true; return false; }
        public static int GridDistance(FactoryEntity a, FactoryEntity b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            FactorySpec sa = FactoryCatalog.Get(a.Kind), sb = FactoryCatalog.Get(b.Kind);
            if (sa == null || sb == null) throw new ArgumentException("Unknown factory entity kind.");
            int dx = AxisDistance(a.X, a.X + sa.Width - 1, b.X, b.X + sb.Width - 1);
            int dz = AxisDistance(a.Z, a.Z + sa.Height - 1, b.Z, b.Z + sb.Height - 1);
            return Math.Max(dx, dz);
        }
        static int AxisDistance(int aMin, int aMax, int bMin, int bMax) => aMax < bMin ? bMin - aMax : bMax < aMin ? aMin - bMax : 0;
        static void CellInFront(FactoryEntity e, out int x, out int z)
        {
            FactorySpec s = FactoryCatalog.Get(e.Kind); x = e.X; z = e.Z;
            if (e.Direction == 0) { x = e.X + s.Width; z = e.Z + (s.Height - 1) / 2; }
            else if (e.Direction == 2) { x = e.X - 1; z = e.Z + (s.Height - 1) / 2; }
            else if (e.Direction == 1) { x = e.X + (s.Width - 1) / 2; z = e.Z + s.Height; }
            else { x = e.X + (s.Width - 1) / 2; z = e.Z - 1; }
        }
    }
}

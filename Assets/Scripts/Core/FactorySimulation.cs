using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Riverworks
{
    public class FactorySimulation
    {
        static readonly int[] Dx = { 1, 0, -1, 0 }, Dz = { 0, 1, 0, -1 };
        readonly List<FactoryEntity> processingOrder = new List<FactoryEntity>();
        readonly List<FactoryEntity> reverseTransportOrder = new List<FactoryEntity>();
        readonly List<FactoryEntity> powerNodes = new List<FactoryEntity>();
        readonly HashSet<int> livePowerNodes = new HashSet<int>();
        readonly HashSet<int> cargoAtTickStart = new HashSet<int>();
        readonly IFactoryEnvironment environment;
        readonly FactoryFluidSimulation fluidSimulation;
        GameState technologyState;
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
            if (state.Version == 1) FactoryStateValidation.UpgradeLegacy(state);
            if (state.Version == 2) FactoryStateValidation.UpgradeVersion2(state);
            else FactoryStateValidation.Validate(state);
            fluidSimulation = new FactoryFluidSimulation(State);
            Recalculate();
        }

        public void ConfigureTechnology(GameState gameState)
        {
            technologyState = gameState;
            BeltSpeedMultiplier = TechCatalog.Has(gameState, TechId.Logistics) ? 1.5f : 1f;
            InserterSpeedMultiplier = TechCatalog.Has(gameState, TechId.Automation) ? 1.5f : 1f;
            MachineSpeedMultiplier = TechCatalog.Has(gameState, TechId.MassProduction) ? 1.25f : 1f;
            PowerDemandMultiplier = TechCatalog.Has(gameState, TechId.Electrification) ? .85f : 1f;
            hasPowerCache = false;
            Recalculate();
        }

        public FactoryEntity GetAt(int x, int z) => GetAt(x, z, 0);

        public FactoryEntity GetAt(int x, int z, int floor)
        {
            foreach (FactoryEntity e in State.Entities)
            {
                FactorySpec spec = FactoryCatalog.Get(e.Kind);
                if (spec != null && e.Floor == floor && x >= e.X && x < e.X + spec.Width && z >= e.Z && z < e.Z + spec.Height) return e;
            }
            return null;
        }

        public bool CanPlace(FactoryKind kind, int x, int z, int direction, out string reason) => CanPlace(kind, x, z, direction, 0, out reason);

        public bool CanPlace(FactoryKind kind, int x, int z, int direction, int floor, out string reason)
            => CanPlaceInternal(kind, x, z, direction, floor, IsVerticalLink(kind), out reason);

        bool CanPlaceInternal(FactoryKind kind, int x, int z, int direction, int floor, bool linkEndpoint, out string reason)
        {
            FactorySpec spec = FactoryCatalog.Get(kind);
            if (spec == null || kind == FactoryKind.None) { reason = "알 수 없는 공장 설비입니다."; return false; }
            if (IsVerticalLink(kind) && !linkEndpoint) { reason = "수직 연결 설비는 두 층을 함께 선택해 배치해야 합니다."; return false; }
            if (!IsVerticalLink(kind) && linkEndpoint) { reason = "수직 연결 설비만 쌍으로 배치할 수 있습니다."; return false; }
            if (floor < 0 || floor > FactoryLayers.MaxFloor) { reason = "지원하지 않는 공장 층입니다."; return false; }
            if (direction < 0 || direction > 3) { reason = "방향 값이 올바르지 않습니다."; return false; }
            if (x < 0 || z < 0 || x + spec.Width > State.Width || z + spec.Height > State.Height) { reason = "공장 부지 경계를 벗어납니다."; return false; }
            for (int zz = z; zz < z + spec.Height; zz++) for (int xx = x; xx < x + spec.Width; xx++)
                if (GetAt(xx, zz, floor) != null) { reason = "이미 같은 층의 다른 설비가 차지한 자리입니다."; return false; }
            if (!FactoryLayers.Supports(State, kind, x, z, floor)) { reason = "이 층의 설비 바닥 전체를 지지하는 플랫폼이 필요합니다."; return false; }
            if (environment is IFactoryLayerEnvironment layers)
            {
                if (!layers.CanPlaceOnFloor(kind, x, z, direction, floor, out reason)) return false;
            }
            else if (floor == 0 && environment != null && !environment.CanPlace(kind, x, z, direction, out reason)) return false;
            if (floor == 0 && !PlacementSourceAvailable(kind, x, z, spec, out reason)) return false;
            reason = ""; return true;
        }

        public bool TryPlace(FactoryKind kind, int x, int z, int direction, out string reason)
            => TryPlace(kind, x, z, direction, 0, out reason);

        public bool TryPlace(FactoryKind kind, int x, int z, int direction, int floor, out string reason)
        {
            if (!CanPlaceInternal(kind, x, z, direction, floor, false, out reason)) return false;
            var e = new FactoryEntity { Id = State.NextEntityId++, Kind = kind, X = x, Z = z, Direction = direction, Floor = floor, ClockPercent = 100 };
            if (kind == FactoryKind.Furnace) e.Recipe = FactoryRecipe.IronPlate;
            else if (kind == FactoryKind.WaterPump) e.Recipe = FactoryRecipe.WaterExtraction;
            else if (kind == FactoryKind.OilPump) e.Recipe = FactoryRecipe.OilExtraction;
            State.Entities.Add(e); Recalculate(); reason = ""; return true;
        }

        public bool TryPlaceLink(FactoryKind kind, int x, int z, int direction, int fromFloor, int toFloor, out string reason)
        {
            if (!IsVerticalLink(kind)) { reason = "아이템 리프트 또는 유체 라이저만 수직 연결할 수 있습니다."; return false; }
            if (Math.Abs(fromFloor - toFloor) != 1) { reason = "서로 인접한 두 층만 연결할 수 있습니다."; return false; }
            if (State.NextEntityId <= 0 || State.NextEntityId >= int.MaxValue) { reason = "새 수직 연결 설비 ID를 안전하게 만들 수 없습니다."; return false; }
            if (!CanPlaceInternal(kind, x, z, direction, fromFloor, true, out reason) || !CanPlaceInternal(kind, x, z, direction, toFloor, true, out reason)) return false;
            int senderId = State.NextEntityId, receiverId = senderId + 1;
            var sender = new FactoryEntity { Id = senderId, LinkId = receiverId, IsLinkSender = true, Kind = kind, X = x, Z = z, Direction = direction, Floor = fromFloor, ClockPercent = 100 };
            var receiver = new FactoryEntity { Id = receiverId, LinkId = senderId, IsLinkSender = false, Kind = kind, X = x, Z = z, Direction = direction, Floor = toFloor, ClockPercent = 100 };
            State.Entities.Add(sender); State.Entities.Add(receiver); State.NextEntityId += 2;
            Recalculate(); reason = "수직 연결 설비 배치 완료"; return true;
        }

        public bool Remove(int x, int z, out string reason) => Remove(x, z, 0, out reason);

        public bool Remove(int x, int z, int floor, out string reason)
        {
            FactoryEntity e = GetAt(x, z, floor);
            if (e == null) { reason = "철거할 설비가 없습니다."; return false; }
            var removing = new List<FactoryEntity> { e };
            if (IsVerticalLink(e.Kind))
            {
                FactoryEntity other = Find(e.LinkId);
                if (other == null || other.Kind != e.Kind || other.LinkId != e.Id) { reason = "수직 연결 쌍이 손상되어 안전하게 철거할 수 없습니다."; return false; }
                removing.Add(other);
            }
            foreach (FactoryEntity target in removing)
            {
                RecoverEntity(target);
                FactoryAutomation.RemoveReferences(State, target.Id);
            }
            foreach (FactoryEntity target in removing) State.Entities.Remove(target);
            Recalculate(); reason = ""; return true;
        }

        public bool Rotate(int id, out string reason)
        {
            FactoryEntity e = Find(id);
            if (e == null) { reason = "설비를 찾을 수 없습니다."; return false; }
            if (ResourceCatalog.IsTransportable(e.CargoResource)) { reason = "물자를 운반 중인 설비는 회전할 수 없습니다."; return false; }
            e.Direction = (e.Direction + 1) & 3; reason = ""; return true;
        }

        public bool CanUseRecipe(FactoryRecipe recipe, out string reason)
        {
            RecipeSpec spec = FactoryCatalog.GetRecipe(recipe);
            if (spec == null || recipe == FactoryRecipe.None) { reason = "유효한 제조법이 아닙니다."; return false; }
            if (technologyState != null && spec.RequiredTech != TechId.None && !TechCatalog.Has(technologyState, spec.RequiredTech))
            { reason = $"{TechCatalog.Get(spec.RequiredTech)?.Name ?? "필요 기술"} 연구가 필요합니다."; return false; }
            reason = ""; return true;
        }

        public bool SetRecipe(int id, FactoryRecipe recipe, out string reason)
        {
            FactoryEntity e = Find(id);
            if (!ValidateRecipeChoice(e, recipe, out reason)) return false;
            if (e.Recipe == recipe) { reason = ""; return true; }
            if (e.Progress > 0 || Total(e.Input) > 0 || Total(e.Output) > 0)
            { reason = "진행도와 입출력 버퍼가 비어 있어야 제조법을 바꿀 수 있습니다."; return false; }
            e.Recipe = recipe; e.Progress = 0; reason = ""; return true;
        }

        public bool ReconfigureRecipe(int id, FactoryRecipe recipe, out string reason)
        {
            FactoryEntity e = Find(id);
            if (!ValidateRecipeChoice(e, recipe, out reason)) return false;
            if (e.Recipe == recipe) { reason = ""; return true; }
            RecoverBuffers(e); e.Recipe = recipe; reason = ""; return true;
        }

        public bool SetPaused(int id, bool paused, out string reason)
        {
            FactoryEntity e = Find(id);
            if (e == null) { reason = "설비를 찾을 수 없습니다."; return false; }
            e.Paused = paused; hasPowerCache = false; Recalculate(); reason = ""; return true;
        }

        public bool SetClock(int id, int percent, out string reason)
        {
            FactoryEntity e = Find(id);
            if (e == null) { reason = "설비를 찾을 수 없습니다."; return false; }
            if (!FactoryCatalog.IsClockable(e.Kind)) { reason = "이 설비는 클럭을 조절할 수 없습니다."; return false; }
            if (percent != 50 && percent != 100 && percent != 150 && percent != 200) { reason = "클럭은 50%, 100%, 150%, 200%만 사용할 수 있습니다."; return false; }
            if (percent > 100 && (technologyState == null || !TechCatalog.Has(technologyState, TechId.AdvancedManufacturing)))
            { reason = "고급 제조 연구가 필요합니다."; return false; }
            e.ClockPercent = percent; hasPowerCache = false; Recalculate(); reason = ""; return true;
        }

        public bool SetFilter(int id, Resource resource, out string reason)
        {
            FactoryEntity e = Find(id);
            if (e == null) { reason = "설비를 찾을 수 없습니다."; return false; }
            bool solid = (e.Kind == FactoryKind.Inserter || e.Kind == FactoryKind.ItemLift) && (resource == Resource.Coins || ResourceCatalog.IsSolid(resource));
            bool fluid = FactoryCatalog.IsFluidTransport(e.Kind) && (resource == Resource.Coins || ResourceCatalog.IsFluid(resource));
            if (!solid && !fluid) { reason = "이 설비에서 사용할 수 없는 필터입니다."; return false; }
            if (fluid && resource != Resource.Coins && HasDifferentFluid(e, resource)) { reason = "다른 유체가 남아 있어 필터를 바꿀 수 없습니다."; return false; }
            e.Filter = resource; reason = ""; return true;
        }

        public bool AddInput(int id, Resource resource, int amount, out string reason)
        {
            FactoryEntity e = Find(id);
            if (e == null) { reason = "설비를 찾을 수 없습니다."; return false; }
            bool solid = (e.Kind == FactoryKind.ImportDock || e.Kind == FactoryKind.Storage) && ResourceCatalog.IsTransportable(resource);
            bool fluid = e.Kind == FactoryKind.FluidTank && ResourceCatalog.IsFluid(resource);
            if ((!solid && !fluid) || amount <= 0) { reason = "이 설비에 넣을 수 없는 물자 또는 수량입니다."; return false; }
            if (fluid && e.Filter != Resource.Coins && e.Filter != resource) { reason = "탱크의 유체 필터와 투입 유체가 일치하지 않습니다."; return false; }
            if (fluid && HasDifferentFluid(e, resource)) { reason = "탱크에는 한 종류의 유체만 저장할 수 있습니다."; return false; }
            int capacity = FactoryCatalog.Get(e.Kind).InputCapacity;
            if (Total(e.Input) + amount > capacity) { reason = "보관 공간이 부족합니다."; return false; }
            e.Input[(int)resource] += amount; reason = ""; return true;
        }

        public int TakeExports(Resource resource, int maximum = int.MaxValue)
        {
            if (!ResourceCatalog.IsTransportable(resource) || maximum <= 0) return 0;
            int taken = 0;
            foreach (FactoryEntity e in State.Entities.Where(x => x.Kind == FactoryKind.ExportDock))
            {
                if (e.IsStopped) continue;
                if (environment != null && !environment.CanExport(e)) continue;
                int n = Math.Min(e.Input[(int)resource], maximum - taken); e.Input[(int)resource] -= n; taken += n;
                if (taken >= maximum) break;
            }
            State.Exported[(int)resource] += taken; return taken;
        }

        public void Tick(float seconds)
        {
            if (seconds <= 0 || !Finite(seconds)) return;
            FactoryAutomation.Evaluate(State, technologyState);
            Recalculate(); State.ElapsedSeconds += seconds;
            cargoAtTickStart.Clear();
            foreach (FactoryEntity e in processingOrder) if (ResourceCatalog.IsTransportable(e.CargoResource)) cargoAtTickStart.Add(e.Id);
            foreach (FactoryEntity e in processingOrder) ProcessMachine(e, seconds);
            foreach (FactoryEntity e in processingOrder) if (e.Kind == FactoryKind.Inserter && e.Powered && !e.IsStopped) ProcessInserter(e, seconds * InserterSpeedMultiplier, cargoAtTickStart.Contains(e.Id));
            foreach (FactoryEntity e in reverseTransportOrder) if (cargoAtTickStart.Contains(e.Id) && !e.IsStopped) ProcessBelt(e, seconds * BeltSpeedMultiplier);
            foreach (FactoryEntity e in processingOrder) if (e.Kind == FactoryKind.Drill && e.Powered && !e.IsStopped) EmitExtraction(e);
            ProcessItemLifts(seconds);
            fluidSimulation.Tick(seconds);
            foreach (FactoryEntity e in processingOrder)
            {
                RecipeSpec recipe = FactoryCatalog.IsProduction(e.Kind) ? FactoryCatalog.GetRecipe(EffectiveRecipe(e)) : null;
                if (e.IsStopped && e.Paused) e.Status = "수동 일시정지";
                else if (e.AutomationBlocked && (e.Status == null || !e.Status.StartsWith("자동화 조건 오류", StringComparison.Ordinal))) e.Status = "자동화 조건 대기";
                else if (recipe != null && HasInputs(e, recipe) && !HasOutputSpace(e, recipe)) e.Status = "출력 또는 부산물 공간 부족";
            }
            Recalculate();
        }

        public void Recalculate()
        {
            long structureHash = StructureHash();
            if (!hasStructureCache || structureHash != cachedStructureHash) RebuildEntityCaches(structureHash);
            long powerHash = PowerHash(structureHash);
            if (!hasPowerCache || powerHash != cachedPowerHash) RecalculatePowerGraph(powerHash);
            MovingItems = State.Entities.Count(e => ResourceCatalog.IsTransportable(e.CargoResource));
        }

        public void InvalidateEnvironment() { hasPowerCache = false; Recalculate(); }
        public static bool HasOre(int x, int z) => x >= 1 && x <= 4 && z >= 6 && z <= 10;
        public static void ValidateState(FactoryState state) => FactoryStateValidation.Validate(state);

        void ProcessMachine(FactoryEntity e, float seconds)
        {
            if (!e.Powered || e.IsStopped || !FactoryCatalog.IsProduction(e.Kind)) return;
            FactoryRecipe effective = EffectiveRecipe(e); RecipeSpec recipe = FactoryCatalog.GetRecipe(effective);
            if (recipe == null) { e.Status = "제조법을 선택하세요."; return; }
            if (!CanUseRecipe(effective, out string gate)) { e.Status = gate; return; }
            if (recipe.IsExtraction && !SourceAvailable(e, recipe.SourceResource)) { e.Status = "필요한 자원 지형이 없습니다."; return; }
            if (!HasInputs(e, recipe)) { e.Status = "제조법 원료 부족"; return; }
            if (!HasOutputSpace(e, recipe)) { e.Status = "출력 또는 부산물 공간 부족"; return; }
            e.Progress += seconds * MachineSpeedMultiplier * e.ClockPercent / 100f;
            e.Status = recipe.IsExtraction ? "자원 추출 중" : "가공 중";
            int batches = MaximumPossibleBatches(e, recipe);
            while (batches-- > 0)
            {
                e.Progress -= recipe.Duration;
                foreach (RecipeAmount input in recipe.Inputs) e.Input[(int)input.Resource] -= input.Amount;
                foreach (RecipeAmount output in recipe.Outputs) { e.Output[(int)output.Resource] += output.Amount; State.Produced[(int)output.Resource] += output.Amount; }
                e.Status = "생산 완료";
            }
            // Buffer capacity bounds the number of useful completions even for an extreme delta.
            if (e.Progress >= recipe.Duration) e.Progress = Math.Max(0, recipe.Duration - .000001f);
        }

        void EmitExtraction(FactoryEntity e)
        {
            RecipeSpec recipe = FactoryCatalog.GetRecipe(EffectiveRecipe(e));
            if (recipe == null || !recipe.IsExtraction) return;
            foreach (RecipeAmount output in recipe.Outputs)
            {
                if (!ResourceCatalog.IsTransportable(output.Resource) || e.Output[(int)output.Resource] <= 0) continue;
                CellInFront(e, out int x, out int z); FactoryEntity target = GetAt(x, z, e.Floor);
                if (target == null && e.Floor == 0 && environment != null && environment.TryGive(x, z, output.Resource)) { e.Output[(int)output.Resource]--; e.Status = "도시로 자원 전달"; return; }
                if (target != null && Accept(target, output.Resource, false)) { e.Output[(int)output.Resource]--; e.Status = "추출물 배출"; return; }
                e.Status = "출구 막힘"; return;
            }
        }

        void ProcessInserter(FactoryEntity e, float seconds, bool startedWithCargo)
        {
            if (startedWithCargo)
            {
                e.CargoProgress = Math.Min(1, e.CargoProgress + seconds * 1.5f);
                if (e.CargoProgress >= 1 && e.Floor == 0 && environment != null)
                {
                    CellInFront(e, out int cityX, out int cityZ);
                    if (GetAt(cityX, cityZ, 0) == null && environment.TryGive(cityX, cityZ, e.CargoResource)) { ClearCargo(e); return; }
                }
                if (e.CargoProgress >= 1)
                {
                    CellInFront(e, out int x, out int z); FactoryEntity dropTarget = GetAt(x, z, e.Floor);
                    if (dropTarget != null && Accept(dropTarget, e.CargoResource, true)) ClearCargo(e); else e.Status = "내려놓을 공간 없음";
                }
                return;
            }
            if (ResourceCatalog.IsTransportable(e.CargoResource)) return;
            int bx = e.X - Dx[e.Direction], bz = e.Z - Dz[e.Direction]; FactoryEntity source = GetAt(bx, bz, e.Floor);
            CellInFront(e, out int tx, out int tz); FactoryEntity target = GetAt(tx, tz, e.Floor);
            if (source == null && e.Floor == 0 && environment != null && TryTakeExternalForTarget(bx, bz, e.Filter, target, out Resource externalItem))
            { e.CargoResource = externalItem; e.CargoProgress = 0; e.Status = "도시 물자 운반 중"; return; }
            if (source != null && TakeOne(source, e.Filter, target, out Resource item)) { e.CargoResource = item; e.CargoProgress = 0; e.Status = "물자 운반 중"; }
            else e.Status = "집을 물자 없음";
        }

        void ProcessBelt(FactoryEntity e, float seconds)
        {
            if (!ResourceCatalog.IsTransportable(e.CargoResource)) { e.CargoProgress = 0; return; }
            e.CargoProgress = Math.Min(1, e.CargoProgress + seconds * 2f); if (e.CargoProgress < 1) return;
            int first = e.Direction, second = (e.Direction + 1) & 3;
            if (e.Kind == FactoryKind.Splitter && e.SplitLeft) { first = second; second = e.Direction; }
            bool moved = TrySend(e, first); if (!moved && e.Kind == FactoryKind.Splitter) moved = TrySend(e, second);
            if (moved && e.Kind == FactoryKind.Splitter) e.SplitLeft = !e.SplitLeft; else if (!moved) e.Status = "벨트 정체";
        }

        void ProcessItemLifts(float seconds)
        {
            foreach (FactoryEntity receiver in processingOrder)
            {
                if (receiver.Kind != FactoryKind.ItemLift || receiver.IsLinkSender || receiver.IsStopped || !receiver.Powered || !cargoAtTickStart.Contains(receiver.Id)) continue;
                if (!ResourceCatalog.IsTransportable(receiver.CargoResource)) continue;
                receiver.CargoProgress = Math.Min(1, receiver.CargoProgress + seconds * 2f * BeltSpeedMultiplier);
                if (receiver.CargoProgress < 1) continue;
                int x = receiver.X + Dx[receiver.Direction], z = receiver.Z + Dz[receiver.Direction];
                FactoryEntity target = GetAt(x, z, receiver.Floor); Resource item = receiver.CargoResource;
                if (target == null && receiver.Floor == 0 && environment != null && environment.TryGive(x, z, item)) { ClearCargo(receiver); receiver.Status = "리프트 화물 배출"; continue; }
                if (target != null && Accept(target, item, false)) { ClearCargo(receiver); receiver.Status = "리프트 화물 배출"; }
                else receiver.Status = "리프트 출구 막힘";
            }
            foreach (FactoryEntity sender in processingOrder)
            {
                if (sender.Kind != FactoryKind.ItemLift || !sender.IsLinkSender || sender.IsStopped || !sender.Powered || !cargoAtTickStart.Contains(sender.Id)) continue;
                if (!ResourceCatalog.IsTransportable(sender.CargoResource)) continue;
                FactoryEntity receiver = Find(sender.LinkId);
                if (receiver == null || receiver.Kind != FactoryKind.ItemLift || receiver.IsLinkSender || receiver.LinkId != sender.Id || receiver.IsStopped || !receiver.Powered ||
                    ResourceCatalog.IsTransportable(receiver.CargoResource) || !Matches(sender.CargoResource, receiver.Filter))
                { sender.Status = "수직 리프트 대기"; continue; }
                sender.CargoProgress = Math.Min(1, sender.CargoProgress + seconds * 2f);
                if (sender.CargoProgress < 1) { sender.Status = "수직 운반 중"; continue; }
                Resource item = sender.CargoResource; ClearCargo(sender);
                receiver.CargoResource = item; receiver.CargoProgress = 0;
                sender.Status = "수직 운반 완료"; receiver.Status = "리프트 화물 도착";
            }
        }

        bool TrySend(FactoryEntity e, int direction)
        {
            int tx = e.X + Dx[direction], tz = e.Z + Dz[direction]; FactoryEntity target = GetAt(tx, tz, e.Floor); Resource item = e.CargoResource;
            if (target != null && (target.Kind == FactoryKind.Belt || target.Kind == FactoryKind.Splitter) && target.X + Dx[target.Direction] == e.X && target.Z + Dz[target.Direction] == e.Z) return false;
            if (target == null && e.Floor == 0 && environment != null && environment.TryGive(tx, tz, item)) { ClearCargo(e); return true; }
            if (target != null && Accept(target, item, false)) { ClearCargo(e); return true; }
            return false;
        }

        bool Accept(FactoryEntity target, Resource item, bool fromInserter)
        {
            if (!CanAccept(target, item, fromInserter)) return false;
            if (target.Kind == FactoryKind.Belt || target.Kind == FactoryKind.Splitter || target.Kind == FactoryKind.Inserter || target.Kind == FactoryKind.ItemLift)
            {
                target.CargoResource = item; target.CargoProgress = 0; target.Status = "이송 중"; return true;
            }
            if (target.Kind == FactoryKind.ExportDock || target.Kind == FactoryKind.Storage || target.Kind == FactoryKind.ImportDock)
            {
                target.Input[(int)item]++; target.Status = target.Kind == FactoryKind.ExportDock ? "반출 대기" : "보관 중"; return true;
            }
            if (fromInserter && FactoryCatalog.IsProduction(target.Kind))
            {
                target.Input[(int)item]++; return true;
            }
            return false;
        }

        bool CanAccept(FactoryEntity target, Resource item, bool fromInserter) => CanAccept(target, item, fromInserter, 0);

        bool CanAccept(FactoryEntity target, Resource item, bool fromInserter, int depth)
        {
            if (target == null || target.IsStopped || !ResourceCatalog.IsTransportable(item) || depth > 4) return false;
            if (target.Kind == FactoryKind.ItemLift)
                return target.IsLinkSender && !ResourceCatalog.IsTransportable(target.CargoResource) && Matches(item, target.Filter);
            if (target.Kind == FactoryKind.Inserter)
            {
                if (ResourceCatalog.IsTransportable(target.CargoResource) || !Matches(item, target.Filter)) return false;
                CellInFront(target, out int x, out int z); FactoryEntity downstream = GetAt(x, z, target.Floor);
                return downstream == null || CanAccept(downstream, item, true, depth + 1);
            }
            if (target.Kind == FactoryKind.Belt || target.Kind == FactoryKind.Splitter)
                return !ResourceCatalog.IsTransportable(target.CargoResource);
            if (target.Kind == FactoryKind.ExportDock || target.Kind == FactoryKind.Storage || target.Kind == FactoryKind.ImportDock)
                return Total(target.Input) < FactoryCatalog.Get(target.Kind).InputCapacity;
            if (!fromInserter || !FactoryCatalog.IsProduction(target.Kind)) return false;
            RecipeSpec recipe = FactoryCatalog.GetRecipe(EffectiveRecipe(target));
            RecipeAmount ingredient = recipe == null ? default : recipe.Inputs.FirstOrDefault(a => a.Resource == item);
            if (recipe == null || ingredient.Amount <= 0) return false;
            int capacity = FactoryCatalog.Get(target.Kind).InputCapacity;
            if (Total(target.Input) >= capacity) return false;
            int perBatch = recipe.Inputs.Sum(a => a.Amount);
            int quotaBatches = Math.Max(1, capacity / Math.Max(1, perBatch));
            return target.Input[(int)item] < ingredient.Amount * quotaBatches;
        }

        bool TryTakeExternalForTarget(int x, int z, Resource filter, FactoryEntity target, out Resource item)
        {
            item = Resource.Coins;
            if (target != null)
            {
                if (filter != Resource.Coins)
                {
                    if (!CanAccept(target, filter, true) || !environment.TryTake(x, z, filter, out Resource filtered) || filtered != filter) return false;
                    item = filtered; return true;
                }
                foreach (ResourceSpec candidate in ResourceCatalog.SolidResources)
                {
                    if (!CanAccept(target, candidate.Id, true) || !environment.TryTake(x, z, candidate.Id, out Resource taken)) continue;
                    if (taken != candidate.Id) return false;
                    item = taken; return true;
                }
                return false;
            }
            if (!environment.TryTake(x, z, filter, out Resource external) || !ResourceCatalog.IsTransportable(external)) return false;
            item = external; return true;
        }

        bool TakeOne(FactoryEntity source, Resource filter, FactoryEntity target, out Resource item)
        {
            item = Resource.Coins;
            if (source == null || source.IsStopped) return false;
            if (source.Kind == FactoryKind.ItemLift && !source.IsLinkSender && ResourceCatalog.IsTransportable(source.CargoResource) && Matches(source.CargoResource, filter) &&
                (target == null || CanAccept(target, source.CargoResource, true)))
            { item = source.CargoResource; ClearCargo(source); return true; }
            if ((source.Kind == FactoryKind.Belt || source.Kind == FactoryKind.Splitter) && ResourceCatalog.IsTransportable(source.CargoResource) &&
                Matches(source.CargoResource, filter) && (target == null || CanAccept(target, source.CargoResource, true)))
            { item = source.CargoResource; ClearCargo(source); return true; }
            List<int> inventory = FactoryCatalog.IsProduction(source.Kind) ? source.Output :
                (source.Kind == FactoryKind.Storage || source.Kind == FactoryKind.ImportDock ? source.Input : null);
            if (inventory == null) return false;
            foreach (ResourceSpec resource in ResourceCatalog.SolidResources)
                if (inventory[(int)resource.Id] > 0 && Matches(resource.Id, filter) && (target == null || CanAccept(target, resource.Id, true)))
                { inventory[(int)resource.Id]--; item = resource.Id; return true; }
            return false;
        }

        bool ValidateRecipeChoice(FactoryEntity e, FactoryRecipe recipe, out string reason)
        {
            if (e == null) { reason = "설비를 찾을 수 없습니다."; return false; }
            if (recipe == FactoryRecipe.None)
            {
                if (!FactoryCatalog.IsProduction(e.Kind) || e.Kind == FactoryKind.Drill || e.Kind == FactoryKind.WaterPump || e.Kind == FactoryKind.OilPump)
                { reason = "이 설비는 제조법 없음 상태를 사용할 수 없습니다."; return false; }
                reason = ""; return true;
            }
            if (!FactoryCatalog.IsRecipeCompatible(e.Kind, recipe)) { reason = "이 설비에서 사용할 수 없는 제조법입니다."; return false; }
            if (!CanUseRecipe(recipe, out reason)) return false;
            RecipeSpec spec = FactoryCatalog.GetRecipe(recipe);
            if (spec.IsExtraction && environment != null && !SourceAvailable(e, spec.SourceResource))
            { reason = "선택한 자원과 설비 아래의 자원 지형이 일치하지 않습니다."; return false; }
            reason = ""; return true;
        }

        void RecoverBuffers(FactoryEntity e)
        {
            for (int i = 1; i < ResourceCatalog.Count; i++) { State.Recovered[i] += e.Input[i] + e.Output[i]; e.Input[i] = e.Output[i] = 0; }
            e.Progress = 0; e.FluidProgress = 0;
        }

        void RecoverEntity(FactoryEntity e)
        {
            RecoverBuffers(e);
            if (ResourceCatalog.IsTransportable(e.CargoResource)) { State.Recovered[(int)e.CargoResource]++; ClearCargo(e); }
            if (e.ControllerInstalled) { State.Recovered[(int)Resource.ControlUnit]++; e.ControllerInstalled = false; }
        }

        bool PlacementSourceAvailable(FactoryKind kind, int x, int z, FactorySpec spec, out string reason)
        {
            if (kind != FactoryKind.Drill && kind != FactoryKind.WaterPump && kind != FactoryKind.OilPump) { reason = ""; return true; }
            IIndustryEnvironment industry = environment as IIndustryEnvironment;
            if (kind == FactoryKind.WaterPump)
            {
                if (environment == null) { reason = "물 지형을 확인할 환경이 필요합니다."; return false; }
                for (int zz = z; zz < z + spec.Height; zz++) for (int xx = x; xx < x + spec.Width; xx++) if (industry != null && industry.HasWater(xx, zz)) { reason = ""; return true; }
                reason = "물 위에 물 펌프를 배치해야 합니다."; return false;
            }
            Resource source = kind == FactoryKind.OilPump ? Resource.CrudeOil : Resource.Ore;
            for (int zz = z; zz < z + spec.Height; zz++) for (int xx = x; xx < x + spec.Width; xx++)
            {
                bool found = source == Resource.Ore
                    ? (environment != null ? environment.HasOre(xx, zz) : HasOre(xx, zz)) || (industry != null &&
                        (industry.HasDeposit(Resource.CopperOre, xx, zz) || industry.HasDeposit(Resource.Coal, xx, zz) || industry.HasDeposit(Resource.Bauxite, xx, zz)))
                    : industry != null && industry.HasDeposit(source, xx, zz);
                if (found) { reason = ""; return true; }
            }
            if (environment == null)
            {
                reason = kind == FactoryKind.Drill ? "철 광맥 위에 채굴기를 배치해야 합니다." : "원유 매장지를 확인할 환경이 필요합니다.";
                return false;
            }
            reason = source == Resource.CrudeOil ? "원유 매장지 위에 배치해야 합니다." : "철 광맥 위에 채굴기를 배치해야 합니다."; return false;
        }

        bool SourceAvailable(FactoryEntity e, Resource source)
        {
            if (environment == null)
            {
                if (source != Resource.Ore) return false;
                FactorySpec legacySpec = FactoryCatalog.Get(e.Kind);
                for (int z = e.Z; z < e.Z + legacySpec.Height; z++) for (int x = e.X; x < e.X + legacySpec.Width; x++) if (HasOre(x, z)) return true;
                return false;
            }
            FactorySpec spec = FactoryCatalog.Get(e.Kind); IIndustryEnvironment industry = environment as IIndustryEnvironment;
            for (int z = e.Z; z < e.Z + spec.Height; z++) for (int x = e.X; x < e.X + spec.Width; x++)
            {
                if (source == Resource.Water && industry != null && industry.HasWater(x, z)) return true;
                if (source == Resource.Ore && environment.HasOre(x, z)) return true;
                if (source != Resource.Water && source != Resource.Ore && industry != null && industry.HasDeposit(source, x, z)) return true;
            }
            return false;
        }

        static FactoryRecipe EffectiveRecipe(FactoryEntity e) => e.Kind == FactoryKind.Drill && e.Recipe == FactoryRecipe.None ? FactoryRecipe.IronMining : e.Recipe;
        static bool HasInputs(FactoryEntity e, RecipeSpec recipe) => recipe.Inputs.All(a => e.Input[(int)a.Resource] >= a.Amount);
        static bool HasOutputSpace(FactoryEntity e, RecipeSpec recipe) => Total(e.Output) + recipe.Outputs.Sum(a => a.Amount) <= FactoryCatalog.Get(e.Kind).OutputCapacity;
        static int MaximumPossibleBatches(FactoryEntity e, RecipeSpec recipe)
        {
            int progress = recipe.Duration > 0 ? (int)Math.Floor(e.Progress / recipe.Duration) : 0;
            int input = recipe.Inputs.Length == 0 ? int.MaxValue : recipe.Inputs.Min(a => e.Input[(int)a.Resource] / a.Amount);
            int free = FactoryCatalog.Get(e.Kind).OutputCapacity - Total(e.Output), output = Math.Max(1, recipe.Outputs.Sum(a => a.Amount));
            return Math.Max(0, Math.Min(progress, Math.Min(input, free / output)));
        }
        static bool HasDifferentFluid(FactoryEntity e, Resource resource)
        { for (int i = 1; i < ResourceCatalog.Count; i++) if (e.Input[i] > 0 && (Resource)i != resource) return true; return false; }
        static bool Matches(Resource item, Resource filter) => filter == Resource.Coins || item == filter;
        static bool IsVerticalLink(FactoryKind kind) => kind == FactoryKind.ItemLift || kind == FactoryKind.FluidRiser;
        static int Total(List<int> inventory) { int total = 0; for (int i = 1; i < inventory.Count; i++) total += inventory[i]; return total; }

        long StructureHash()
        {
            unchecked
            {
                long hash = 1469598103934665603L; hash = Mix(hash, State.Entities.Count);
                foreach (FactoryEntity e in State.Entities)
                {
                    hash = Mix(hash, RuntimeHelpers.GetHashCode(e)); hash = Mix(hash, e.Id); hash = Mix(hash, (int)e.Kind);
                    hash = Mix(hash, e.X); hash = Mix(hash, e.Z); hash = Mix(hash, e.Floor); hash = Mix(hash, e.LinkId);
                    hash = Mix(hash, e.IsLinkSender ? 1 : 0); hash = Mix(hash, e.Direction);
                }
                return hash;
            }
        }
        void RebuildEntityCaches(long hash)
        {
            processingOrder.Clear(); processingOrder.AddRange(State.Entities); processingOrder.Sort((a, b) => a.Id.CompareTo(b.Id));
            reverseTransportOrder.Clear(); for (int i = processingOrder.Count - 1; i >= 0; i--) if (processingOrder[i].Kind == FactoryKind.Belt || processingOrder[i].Kind == FactoryKind.Splitter) reverseTransportOrder.Add(processingOrder[i]);
            cachedStructureHash = hash; hasStructureCache = true; hasPowerCache = false;
        }
        long PowerHash(long structureHash)
        {
            long hash = Mix(Mix(structureHash, State.PowerBudget), PowerDemandMultiplier.GetHashCode());
            foreach (FactoryEntity e in processingOrder) { hash = Mix(hash, e.IsStopped ? 1 : 0); hash = Mix(hash, e.Paused ? 1 : 0); hash = Mix(hash, e.ClockPercent); }
            return hash;
        }
        void RecalculatePowerGraph(long hash)
        {
            powerNodes.Clear(); livePowerNodes.Clear();
            foreach (FactoryEntity e in processingOrder)
            {
                if (e.Kind != FactoryKind.PowerInlet && e.Kind != FactoryKind.Pole) continue;
                powerNodes.Add(e); if (!e.IsStopped && e.Kind == FactoryKind.PowerInlet && e.Floor == 0 && (environment == null || environment.CanSupplyPower(e))) livePowerNodes.Add(e.Id);
            }
            bool changed;
            do
            {
                changed = false;
                foreach (FactoryEntity node in powerNodes)
                {
                    if (node.IsStopped || livePowerNodes.Contains(node.Id)) continue;
                    foreach (FactoryEntity source in powerNodes)
                        if (!source.IsStopped && livePowerNodes.Contains(source.Id) && GridDistance(node, source) <= 6) { livePowerNodes.Add(node.Id); changed = true; break; }
                }
            } while (changed);
            foreach (FactoryEntity node in powerNodes) { node.Powered = livePowerNodes.Contains(node.Id); node.Status = node.Paused ? "수동 일시정지" : node.AutomationBlocked ? "자동화 조건 대기" : node.Powered ? "전력망 연결" : "전력 인입구 연결 필요"; }
            PowerAvailable = livePowerNodes.Count > 0 ? Math.Max(0, State.PowerBudget) : 0; PowerUsed = 0;
            foreach (FactoryEntity e in processingOrder)
            {
                float demand = Demand(e);
                if (demand <= 0) { if (e.Kind != FactoryKind.PowerInlet && e.Kind != FactoryKind.Pole) e.Powered = !e.IsStopped; continue; }
                bool covered = powerNodes.Any(n => livePowerNodes.Contains(n.Id) && GridDistance(e, n) <= 4);
                e.Powered = !e.IsStopped && covered && PowerUsed + demand <= PowerAvailable + .0001f;
                if (e.Powered) PowerUsed += demand; else e.Status = e.Paused ? "수동 일시정지" : e.AutomationBlocked ? "자동화 조건 대기" : covered ? "공장 전력 예산 부족" : "전력망 범위 밖";
            }
            cachedPowerHash = hash; hasPowerCache = true;
        }
        static long Mix(long hash, int value) => unchecked((hash ^ (uint)value) * 1099511628211L);
        float Demand(FactoryEntity e)
        {
            if (e.IsStopped) return 0;
            float clock = FactoryCatalog.IsClockable(e.Kind) ? e.ClockPercent / 100f : 1f;
            return (FactoryCatalog.Get(e.Kind)?.PowerDemand ?? 0) * PowerDemandMultiplier * clock * clock;
        }
        FactoryEntity Find(int id) => State.Entities.FirstOrDefault(e => e.Id == id);
        static void ClearCargo(FactoryEntity e) { e.CargoResource = Resource.Coins; e.CargoProgress = 0; }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static int GridDistance(FactoryEntity a, FactoryEntity b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a)); if (b == null) throw new ArgumentNullException(nameof(b));
            FactorySpec sa = FactoryCatalog.Get(a.Kind), sb = FactoryCatalog.Get(b.Kind); if (sa == null || sb == null) throw new ArgumentException("Unknown factory entity kind.");
            if (a.Floor != b.Floor)
            {
                bool verticalPoles = a.Kind == FactoryKind.Pole && b.Kind == FactoryKind.Pole && a.X == b.X && a.Z == b.Z && Math.Abs(a.Floor - b.Floor) == 1;
                return verticalPoles ? 1 : int.MaxValue;
            }
            return Math.Max(AxisDistance(a.X, a.X + sa.Width - 1, b.X, b.X + sb.Width - 1), AxisDistance(a.Z, a.Z + sa.Height - 1, b.Z, b.Z + sb.Height - 1));
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

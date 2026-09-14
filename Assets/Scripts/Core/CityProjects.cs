using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    public enum CityProjectKind { None = 0, GrandBridge = 1, CentralPowerPlant = 2, ResearchCampus = 3 }

    [Serializable]
    public sealed class CityProjectState
    {
        public CityProjectKind Kind;
        public int X, Z, Stage, DaysRemaining, StartedDay;
        public int CompletedDay = -1;
        public int FuelDay = -1;
        public List<int> Delivered = FactoryState.NewInventory();
    }

    public sealed class StageSpec
    {
        public string Name;
        public RecipeAmount[] Requirements;
        public int Days;

        public StageSpec(string name, int days, params RecipeAmount[] requirements)
        {
            Name = name;
            Days = days;
            Requirements = requirements ?? Array.Empty<RecipeAmount>();
        }
    }

    public sealed class CityProjectSpec
    {
        public CityProjectKind Kind;
        public string Name, Description, RewardText;
        public int Width, Height, CoinCost;
        public TechId RequiredTech;
        public StageSpec[] Stages;
    }

    public static class CityProjects
    {
        static readonly CityProjectSpec[] Specs =
        {
            Spec(CityProjectKind.GrandBridge, "대교", "수면을 가로질러 실제 도로망을 잇는 4칸 교량입니다.", 4, 1, TechId.UrbanPlanning, 400,
                "완공 시 네 칸 모두 실제 도로가 됩니다.",
                Stage("기초 공사", 2, Resource.Stone, 20, Resource.SteelBeam, 12),
                Stage("교량 상판", 2, Resource.SteelPipe, 16, Resource.ModularFrame, 8),
                Stage("교량 기계", 2, Resource.Motor, 4, Resource.ControlUnit, 2)),
            Spec(CityProjectKind.CentralPowerPlant, "중앙 발전소", "석탄을 사용하는 대규모 도시 발전소입니다.", 2, 2, TechId.AdvancedManufacturing, 600,
                "도로 연결과 석탄 2개로 하루 동안 도시 전력 80을 공급합니다.",
                Stage("기초 공사", 2, Resource.Stone, 30, Resource.SteelBeam, 10),
                Stage("발전 설비", 3, Resource.Motor, 8, Resource.SteelPipe, 20, Resource.ModularFrame, 10),
                Stage("제어 설비", 2, Resource.Computer, 4, Resource.Battery, 8, Resource.ControlUnit, 4)),
            Spec(CityProjectKind.ResearchCampus, "연구 단지", "도시 연구를 집중시키는 대형 연구 시설입니다.", 2, 2, TechId.AdvancedManufacturing, 500,
                "도로 연결 시 하루 연구 점수 10을 추가합니다.",
                Stage("기초 공사", 2, Resource.Stone, 20, Resource.SteelBeam, 8),
                Stage("연구동 건설", 2, Resource.Circuit, 12, Resource.ModularFrame, 8),
                Stage("중앙 연구실", 3, Resource.Computer, 8, Resource.ControlUnit, 3))
        };

        public static IReadOnlyList<CityProjectSpec> All => Specs;

        public static CityProjectSpec Get(CityProjectKind kind) => Specs.FirstOrDefault(spec => spec.Kind == kind);

        public static CityProjectState Find(GameState state, CityProjectKind kind) =>
            state?.CityProjects?.FirstOrDefault(project => project != null && project.Kind == kind);

        public static bool Occupies(GameState state, int cityX, int cityZ)
        {
            if (state?.CityProjects == null) return false;
            foreach (CityProjectState project in state.CityProjects)
            {
                CityProjectSpec spec = project == null ? null : Get(project.Kind);
                if (spec != null && cityX >= project.X && cityX < project.X + spec.Width && cityZ >= project.Z && cityZ < project.Z + spec.Height)
                    return true;
            }
            return false;
        }

        public static bool CanStart(GameState state, CityProjectKind kind, int x, int z, out string reason)
        {
            CityProjectSpec spec = Get(kind);
            if (state == null) return Fail("도시 상태가 없습니다.", out reason);
            if (spec == null || kind == CityProjectKind.None) return Fail("알 수 없는 대형 프로젝트입니다.", out reason);
            if (state.CityProjects == null) return Fail("대형 프로젝트 장부가 없습니다.", out reason);
            if (Find(state, kind) != null) return Fail("같은 종류의 대형 프로젝트는 도시에 하나만 둘 수 있습니다.", out reason);
            if (!TechCatalog.Has(state, spec.RequiredTech)) return Fail($"기술 '{TechCatalog.Get(spec.RequiredTech)?.Name}' 연구가 필요합니다.", out reason);
            if (x < 0 || z < 0 || x + spec.Width > state.Size || z + spec.Height > state.Size)
                return Fail("프로젝트 부지가 도시 경계를 벗어납니다.", out reason);
            if (state.Coins + .001f < spec.CoinCost) return Fail($"코인이 {spec.CoinCost - state.Coins:0} 부족합니다.", out reason);

            bool interiorWater = false;
            for (int zz = z; zz < z + spec.Height; zz++)
            for (int xx = x; xx < x + spec.Width; xx++)
            {
                Cell cell = CellAt(state, xx, zz);
                if (!IsOwned(state, xx, zz)) return Fail("프로젝트 전체 부지를 먼저 소유해야 합니다.", out reason);
                if (Occupies(state, xx, zz)) return Fail("다른 대형 프로젝트가 부지를 차지하고 있습니다.", out reason);
                if (HasFactoryOrPlatform(state, xx, zz)) return Fail("공장 설비나 상층 구조물이 부지를 차지하고 있습니다.", out reason);
                if (kind == CityProjectKind.GrandBridge)
                {
                    if (cell.Building != BuildingKind.None && cell.Building != BuildingKind.Road)
                        return Fail("대교 부지에는 기존 도로 외의 도시 건물을 둘 수 없습니다.", out reason);
                    bool endpoint = xx == x || xx == x + spec.Width - 1;
                    if (endpoint && cell.Terrain == TerrainKind.Water) return Fail("대교 양 끝은 육지여야 합니다.", out reason);
                    if (!endpoint && cell.Terrain == TerrainKind.Water) interiorWater = true;
                }
                else
                {
                    if (cell.Terrain == TerrainKind.Water) return Fail("이 프로젝트는 육지에만 지을 수 있습니다.", out reason);
                    if (cell.Building != BuildingKind.None) return Fail("빈 부지에만 프로젝트를 시작할 수 있습니다.", out reason);
                }
            }

            if (kind == CityProjectKind.GrandBridge)
            {
                if (!interiorWater) return Fail("대교 내부 구간에는 수면이 하나 이상 있어야 합니다.", out reason);
                var candidate = NewState(kind, x, z, state.Day);
                if (!IsConnected(state, candidate)) return Fail("대교 한쪽 끝에 시청과 연결된 도로가 닿아야 합니다.", out reason);
            }
            reason = "프로젝트 시작 가능";
            return true;
        }

        public static bool Start(GameState state, CityProjectKind kind, int x, int z, out string reason)
        {
            if (!CanStart(state, kind, x, z, out reason)) return false;
            CityProjectSpec spec = Get(kind);
            state.Coins -= spec.CoinCost;
            state.Stock[(int)Resource.Coins] = state.Coins;
            state.CityProjects.Add(NewState(kind, x, z, state.Day));
            reason = $"{spec.Name} 프로젝트를 시작했습니다.";
            return true;
        }

        public static bool Deliver(GameState state, CityProjectKind kind, out string reason)
        {
            CityProjectState project = Find(state, kind);
            CityProjectSpec spec = Get(kind);
            if (project == null || spec == null) return Fail("진행 중인 프로젝트가 없습니다.", out reason);
            if (project.Stage >= spec.Stages.Length) return Fail("이미 완공된 프로젝트입니다.", out reason);
            if (project.DaysRemaining > 0) return Fail("현재 단계는 이미 공사 중입니다.", out reason);

            StageSpec stage = spec.Stages[project.Stage];
            var transfers = new List<RecipeAmount>();
            foreach (RecipeAmount requirement in stage.Requirements)
            {
                int delivered = project.Delivered[(int)requirement.Resource];
                int remaining = requirement.Amount - delivered;
                int available = (int)Math.Floor(Math.Max(0, state.Stock[(int)requirement.Resource]));
                int amount = Math.Min(Math.Max(0, remaining), available);
                if (amount > 0) transfers.Add(new RecipeAmount(requirement.Resource, amount));
            }
            if (transfers.Count == 0) return Fail("납품할 수 있는 현재 단계 자재가 없습니다.", out reason);

            int total = 0;
            foreach (RecipeAmount transfer in transfers)
            {
                state.Stock[(int)transfer.Resource] -= transfer.Amount;
                project.Delivered[(int)transfer.Resource] += transfer.Amount;
                total += transfer.Amount;
            }
            if (StageSupplied(project, stage)) project.DaysRemaining = stage.Days;
            reason = $"{stage.Name} 자재 {total}개를 납품했습니다.";
            return true;
        }

        public static bool Cancel(GameState state, CityProjectKind kind, out string reason)
        {
            CityProjectState project = Find(state, kind);
            CityProjectSpec spec = Get(kind);
            if (project == null || spec == null) return Fail("취소할 프로젝트가 없습니다.", out reason);
            if (project.Stage >= spec.Stages.Length) return Fail("완공된 프로젝트는 취소할 수 없습니다.", out reason);
            for (int i = 1; i < ResourceCatalog.Count; i++)
                state.Stock[i] += project.Delivered[i];
            state.Coins += spec.CoinCost * .35f;
            state.Stock[(int)Resource.Coins] = state.Coins;
            state.CityProjects.Remove(project);
            reason = $"{spec.Name} 프로젝트를 취소했습니다. 현재 단계 자재와 시작 비용의 35%를 돌려받았습니다.";
            return true;
        }

        public static void Tick(GameState state)
        {
            if (state?.CityProjects == null) return;
            foreach (CityProjectState project in state.CityProjects)
            {
                CityProjectSpec spec = project == null ? null : Get(project.Kind);
                if (spec == null || project.Stage >= spec.Stages.Length || project.DaysRemaining <= 0 || !IsConnected(state, project)) continue;
                project.DaysRemaining--;
                if (project.DaysRemaining > 0) continue;
                project.Stage++;
                ClearDelivered(project.Delivered);
                if (project.Stage < spec.Stages.Length) continue;
                project.CompletedDay = state.Day;
                if (project.Kind == CityProjectKind.GrandBridge) CompleteBridge(state, project, spec);
            }
        }

        public static bool IsConnected(GameState state, CityProjectState project)
        {
            CityProjectSpec spec = project == null ? null : Get(project.Kind);
            if (state == null || spec == null) return false;
            if (project.Kind == CityProjectKind.GrandBridge)
                return TouchesConnectedRoad(state, project.X, project.Z) || TouchesConnectedRoad(state, project.X + spec.Width - 1, project.Z);
            for (int z = project.Z; z < project.Z + spec.Height; z++)
            for (int x = project.X; x < project.X + spec.Width; x++)
                if (TouchesConnectedRoad(state, x, z)) return true;
            return false;
        }

        public static float ResearchBonus(GameState state) => CompletedConnected(state, CityProjectKind.ResearchCampus) ? 10f : 0f;

        public static int PreviewPower(GameState state)
        {
            CityProjectState project = Find(state, CityProjectKind.CentralPowerPlant);
            CityProjectSpec spec = Get(CityProjectKind.CentralPowerPlant);
            return project != null && spec != null && project.Stage == spec.Stages.Length && IsConnected(state, project) &&
                (project.FuelDay == state.Day || Stock(state, Resource.Coal) >= 2f) ? 80 : 0;
        }

        public static int ProducePower(GameState state)
        {
            CityProjectState project = Find(state, CityProjectKind.CentralPowerPlant);
            CityProjectSpec spec = Get(CityProjectKind.CentralPowerPlant);
            if (project == null || spec == null || project.Stage != spec.Stages.Length) return 0;
            if (!IsConnected(state, project))
            {
                SetStatus(state, project, spec, "도로 연결 끊김 · 발전 정지");
                return 0;
            }
            if (project.FuelDay == state.Day)
            {
                SetStatus(state, project, spec, "중앙 발전 가동 · 전력 +80");
                return 80;
            }
            if (Stock(state, Resource.Coal) < 2f)
            {
                SetStatus(state, project, spec, "연료 부족 · 석탄 2 필요");
                return 0;
            }
            state.Stock[(int)Resource.Coal] -= 2f;
            project.FuelDay = state.Day;
            SetStatus(state, project, spec, "중앙 발전 가동 · 전력 +80");
            return 80;
        }

        public static void Validate(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.CityProjects == null) throw new ArgumentException("CityProjects cannot be null.");
            var kinds = new HashSet<CityProjectKind>();
            var occupied = new HashSet<int>();
            foreach (CityProjectState project in state.CityProjects)
            {
                CityProjectSpec spec = project == null ? null : Get(project.Kind);
                if (spec == null || project.Kind == CityProjectKind.None) throw new ArgumentException("Unknown city project kind.");
                if (!kinds.Add(project.Kind)) throw new ArgumentException("Only one city project of each kind is allowed.");
                if (!TechCatalog.Has(state, spec.RequiredTech)) throw new ArgumentException("City project required technology is missing.");
                if (project.X < 0 || project.Z < 0 || project.X + spec.Width > state.Size || project.Z + spec.Height > state.Size)
                    throw new ArgumentException("City project footprint is outside the city.");
                if (project.Stage < 0 || project.Stage > spec.Stages.Length) throw new ArgumentException("Invalid city project stage.");
                if (project.Delivered == null || project.Delivered.Count != ResourceCatalog.Count || project.Delivered.Any(value => value < 0))
                    throw new ArgumentException("Invalid city project delivery inventory.");
                if (project.Delivered[(int)Resource.Coins] != 0 || ResourceCatalog.FluidResources.Any(resource => project.Delivered[(int)resource.Id] != 0))
                    throw new ArgumentException("Projects cannot contain delivered coins or fluids.");
                if (project.StartedDay < 0 || project.StartedDay > state.Day) throw new ArgumentException("Invalid city project start day.");

                if (project.Stage == spec.Stages.Length)
                {
                    if (project.DaysRemaining != 0 || project.CompletedDay < project.StartedDay || project.CompletedDay > state.Day || project.Delivered.Any(value => value != 0))
                        throw new ArgumentException("Invalid completed city project state.");
                    if (project.Kind == CityProjectKind.CentralPowerPlant)
                    {
                        if (project.FuelDay != -1 && (project.FuelDay < project.CompletedDay || project.FuelDay > state.Day))
                            throw new ArgumentException("Invalid central power plant fuel day.");
                    }
                    else if (project.FuelDay != -1) throw new ArgumentException("Only a completed central power plant can retain a fuel day.");
                }
                else
                {
                    if (project.CompletedDay != -1) throw new ArgumentException("Active city project cannot have a completion day.");
                    if (project.FuelDay != -1) throw new ArgumentException("Active city project cannot retain a fuel day.");
                    StageSpec stage = spec.Stages[project.Stage];
                    foreach (RecipeAmount requirement in stage.Requirements)
                        if (project.Delivered[(int)requirement.Resource] > requirement.Amount) throw new ArgumentException("Delivered material exceeds the current requirement.");
                    for (int i = 1; i < ResourceCatalog.Count; i++)
                        if (project.Delivered[i] > 0 && !stage.Requirements.Any(requirement => (int)requirement.Resource == i))
                            throw new ArgumentException("Delivery inventory contains a material from another stage.");
                    bool supplied = StageSupplied(project, stage);
                    if (project.DaysRemaining < 0 || project.DaysRemaining > stage.Days || (supplied && project.DaysRemaining == 0) || (!supplied && project.DaysRemaining != 0))
                        throw new ArgumentException("Project days do not match current delivery progress.");
                }

                for (int z = project.Z; z < project.Z + spec.Height; z++)
                for (int x = project.X; x < project.X + spec.Width; x++)
                {
                    if (!occupied.Add(z * state.Size + x)) throw new ArgumentException("City projects overlap.");
                    if (!IsOwned(state, x, z)) throw new ArgumentException("City project occupies unowned land.");
                    if (HasFactoryOrPlatform(state, x, z)) throw new ArgumentException("City project overlaps factory infrastructure.");
                    Cell cell = CellAt(state, x, z);
                    if (project.Kind == CityProjectKind.GrandBridge)
                    {
                        bool endpoint = x == project.X || x == project.X + spec.Width - 1;
                        if (endpoint && cell.Terrain == TerrainKind.Water) throw new ArgumentException("Bridge endpoints must be land.");
                        if (cell.Building != BuildingKind.None && cell.Building != BuildingKind.Road)
                            throw new ArgumentException("Bridge overlaps a city building.");
                        if (project.Stage == spec.Stages.Length && (cell.Building != BuildingKind.Road || cell.Level != 1))
                            throw new ArgumentException("Completed bridge must own real level-one roads.");
                    }
                    else if (cell.Terrain == TerrainKind.Water || cell.Building != BuildingKind.None)
                        throw new ArgumentException("Land project footprint is obstructed.");
                }
                if (project.Kind == CityProjectKind.GrandBridge &&
                    !Enumerable.Range(project.X + 1, spec.Width - 2).Any(x => CellAt(state, x, project.Z).Terrain == TerrainKind.Water))
                    throw new ArgumentException("Bridge must span water.");
            }
        }

        static CityProjectState NewState(CityProjectKind kind, int x, int z, int day) =>
            new CityProjectState { Kind = kind, X = x, Z = z, Stage = 0, DaysRemaining = 0, StartedDay = day, CompletedDay = -1, FuelDay = -1 };

        static bool StageSupplied(CityProjectState project, StageSpec stage) =>
            stage.Requirements.All(requirement => project.Delivered[(int)requirement.Resource] == requirement.Amount);

        static bool CompletedConnected(GameState state, CityProjectKind kind)
        {
            CityProjectState project = Find(state, kind);
            CityProjectSpec spec = Get(kind);
            return project != null && spec != null && project.Stage == spec.Stages.Length && IsConnected(state, project);
        }

        static float Stock(GameState state, Resource resource) =>
            state?.Stock != null && state.Stock.Count > (int)resource ? state.Stock[(int)resource] : 0f;

        static void CompleteBridge(GameState state, CityProjectState project, CityProjectSpec spec)
        {
            for (int x = project.X; x < project.X + spec.Width; x++)
            {
                Cell cell = CellAt(state, x, project.Z);
                cell.Building = BuildingKind.Road;
                cell.Level = 1;
                cell.Progress = 0;
                cell.Status = "대교 도로";
            }
        }

        static bool TouchesConnectedRoad(GameState state, int x, int z)
        {
            int[] dx = { 1, -1, 0, 0 };
            int[] dz = { 0, 0, 1, -1 };
            for (int i = 0; i < 4; i++)
            {
                Cell cell = CellAt(state, x + dx[i], z + dz[i]);
                if (cell != null && cell.Connected && (cell.Building == BuildingKind.Road || cell.Building == BuildingKind.TownHall)) return true;
            }
            return false;
        }

        static bool HasFactoryOrPlatform(GameState state, int cityX, int cityZ)
        {
            if (state.Factory?.Entities != null)
                foreach (FactoryEntity entity in state.Factory.Entities)
                {
                    FactorySpec spec = entity == null ? null : FactoryCatalog.Get(entity.Kind);
                    if (spec == null) continue;
                    int firstX = entity.X / CityLogistics.Resolution, lastX = (entity.X + spec.Width - 1) / CityLogistics.Resolution;
                    int firstZ = entity.Z / CityLogistics.Resolution, lastZ = (entity.Z + spec.Height - 1) / CityLogistics.Resolution;
                    if (cityX >= firstX && cityX <= lastX && cityZ >= firstZ && cityZ <= lastZ) return true;
                }
            if (state.Factory?.Platforms != null)
                foreach (FactoryPlatform platform in state.Factory.Platforms)
                    if (platform != null && platform.X / CityLogistics.Resolution == cityX && platform.Z / CityLogistics.Resolution == cityZ) return true;
            return false;
        }

        static bool IsOwned(GameState state, int x, int z) =>
            state.OwnedRegions != null && state.OwnedRegions.Contains((z / 7) * 3 + x / 7);

        static Cell CellAt(GameState state, int x, int z) =>
            state?.Cells != null && x >= 0 && z >= 0 && x < state.Size && z < state.Size ? state.Cells[z * state.Size + x] : null;

        static void ClearDelivered(List<int> delivered)
        {
            for (int i = 0; i < delivered.Count; i++) delivered[i] = 0;
        }

        static void SetStatus(GameState state, CityProjectState project, CityProjectSpec spec, string status)
        {
            for (int z = project.Z; z < project.Z + spec.Height; z++)
            for (int x = project.X; x < project.X + spec.Width; x++)
                CellAt(state, x, z).Status = status;
        }

        static StageSpec Stage(string name, int days, params object[] amounts)
        {
            var requirements = new RecipeAmount[amounts.Length / 2];
            for (int i = 0; i < requirements.Length; i++) requirements[i] = new RecipeAmount((Resource)amounts[i * 2], (int)amounts[i * 2 + 1]);
            return new StageSpec(name, days, requirements);
        }

        static CityProjectSpec Spec(CityProjectKind kind, string name, string description, int width, int height, TechId tech, int coins,
            string reward, params StageSpec[] stages) => new CityProjectSpec
            { Kind = kind, Name = name, Description = description, Width = width, Height = height, RequiredTech = tech, CoinCost = coins, Stages = stages, RewardText = reward };

        static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}

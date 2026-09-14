using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    public interface IFactoryEnvironment
    {
        bool CanPlace(FactoryKind kind, int x, int z, int direction, out string reason);
        bool HasOre(int x, int z);
        bool TryTake(int x, int z, Resource filter, out Resource item);
        bool TryGive(int x, int z, Resource item);
        bool CanSupplyPower(FactoryEntity entity);
        bool CanExport(FactoryEntity entity);
    }

    /// <summary>Connects the half-lot factory grid to the authoritative 21x21 city.</summary>
    public sealed class CityLogistics : IFactoryEnvironment
    {
        public static int Resolution = 2;
        public const float BufferCapacity = 80f;

        readonly GameState state;

        public CityLogistics(GameState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            EnsureCityBuffers(state);
        }

        public bool CanPlace(FactoryKind kind, int x, int z, int direction, out string reason)
        {
            FactorySpec spec = FactoryCatalog.Get(kind);
            if (spec == null || kind == FactoryKind.None)
            {
                reason = "알 수 없는 공장 설비입니다.";
                return false;
            }

            bool light = IsLightTransport(kind);
            if (!light && (spec.Width > 1 || spec.Height > 1) &&
                (spec.Width != Resolution || spec.Height != Resolution || x % Resolution != 0 || z % Resolution != 0))
            {
                reason = "2x2 기계는 도시 부지 경계에 맞춰야 합니다.";
                return false;
            }

            var checkedLots = new HashSet<int>();
            for (int zz = z; zz < z + spec.Height; zz++)
            for (int xx = x; xx < x + spec.Width; xx++)
            {
                Cell cell = CellAtMicro(xx, zz);
                if (cell == null)
                {
                    reason = "공장 설비는 도시 경계 안에 배치해야 합니다.";
                    return false;
                }
                int lot = cell.Z * state.Size + cell.X;
                if (!checkedLots.Add(lot)) continue;
                if (!IsOwned(cell.X, cell.Z))
                {
                    reason = "공장 설비를 놓으려면 이 도시 구역을 먼저 매입해야 합니다.";
                    return false;
                }
                if (light)
                {
                    if (cell.Terrain == TerrainKind.Water && cell.Building != BuildingKind.Road)
                    {
                        reason = "수면의 경량 물류 설비에는 도로 교량이 필요합니다.";
                        return false;
                    }
                    if (cell.Building != BuildingKind.None && cell.Building != BuildingKind.Road)
                    {
                        reason = "이 부지는 도시 건물이 차지하고 있습니다.";
                        return false;
                    }
                }
                else
                {
                    if (cell.Terrain == TerrainKind.Water)
                    {
                        reason = "기계는 수면에 배치할 수 없습니다.";
                        return false;
                    }
                    if (cell.Building != BuildingKind.None)
                    {
                        reason = "이 기계 부지는 도시 건물이나 도로가 차지하고 있습니다.";
                        return false;
                    }
                }
            }

            reason = "";
            return true;
        }

        public bool HasOre(int x, int z)
        {
            Cell cell = CellAtMicro(x, z);
            return cell != null && cell.Terrain == TerrainKind.Rock;
        }

        public bool TryTake(int x, int z, Resource filter, out Resource item)
        {
            item = Resource.Coins;
            Cell cell = CellAtMicro(x, z);
            if (cell == null) return false;
            EnsureCellBuffers(cell);

            BuildingSpec producer = Catalog.Get(cell.Building);
            if (producer.OutputAmount > 0 && IsCargo(producer.Output) && Matches(producer.Output, filter) && cell.LogisticsOutput[(int)producer.Output] + .0001f >= 1f)
            {
                cell.LogisticsOutput[(int)producer.Output] = Math.Max(0,cell.LogisticsOutput[(int)producer.Output]-1f);
                item = producer.Output;
                return true;
            }

            if (!cell.Connected || (cell.Building != BuildingKind.Warehouse && cell.Building != BuildingKind.TownHall)) return false;
            EnsureStock(state);
            for (int i = 1; i < 9; i++)
            {
                Resource candidate = (Resource)i;
                if (!Matches(candidate, filter) || state.Stock[i] + .0001f < 1f) continue;
                state.Stock[i] = Math.Max(0,state.Stock[i]-1f);
                item = candidate;
                return true;
            }
            return false;
        }

        public bool TryGive(int x, int z, Resource item)
        {
            if (!IsCargo(item)) return false;
            Cell cell = CellAtMicro(x, z);
            if (cell == null) return false;
            EnsureCellBuffers(cell);

            BuildingSpec processor = Catalog.Get(cell.Building);
            if (processor.Inputs.ContainsKey(item))
            {
                if (BufferTotal(cell.LogisticsInput) + 1f > BufferCapacity + .0001f) return false;
                cell.LogisticsInput[(int)item] += 1f;
                return true;
            }

            bool globalPort = cell.Connected && (cell.Building == BuildingKind.Warehouse || cell.Building == BuildingKind.TownHall ||
                (cell.Building == BuildingKind.Market && (item == Resource.Bread || item == Resource.Tools)));
            if (!globalPort) return false;
            EnsureStock(state);
            state.Stock[(int)item] += 1f;
            EnsureInventory(state.Factory.Exported);
            state.Factory.Exported[(int)item]++;
            return true;
        }

        public bool CanSupplyPower(FactoryEntity entity) => entity != null && entity.Kind == FactoryKind.PowerInlet && TouchesConnectedRoad(entity);

        public bool CanExport(FactoryEntity entity) => entity != null && entity.Kind == FactoryKind.ExportDock && TouchesConnectedRoad(entity);

        public static bool CanBuildCity(GameState state, BuildingKind kind, int x, int z, out string reason)
        {
            if (state?.Factory?.Entities == null)
            {
                reason = "";
                return true;
            }
            int minX = x * Resolution, minZ = z * Resolution;
            int maxX = minX + Resolution - 1, maxZ = minZ + Resolution - 1;
            foreach (FactoryEntity entity in state.Factory.Entities)
            {
                FactorySpec spec = entity == null ? null : FactoryCatalog.Get(entity.Kind);
                if (spec == null || entity.X > maxX || entity.Z > maxZ || entity.X + spec.Width - 1 < minX || entity.Z + spec.Height - 1 < minZ) continue;
                if (kind == BuildingKind.Road && IsLightTransport(entity.Kind)) continue;
                reason = kind == BuildingKind.Road ? "이 도로 부지는 공장 기계가 차지하고 있습니다." : "이 도시 부지는 공장 설비가 차지하고 있습니다.";
                return false;
            }
            reason = "";
            return true;
        }

        internal static bool CanDemolishCity(GameState state, Cell cell, out string reason)
        {
            if (state?.Factory?.Entities == null || cell == null || cell.Building != BuildingKind.Road || cell.Terrain != TerrainKind.Water)
            {
                reason = "";
                return true;
            }
            int minX = cell.X * Resolution, minZ = cell.Z * Resolution;
            int maxX = minX + Resolution - 1, maxZ = minZ + Resolution - 1;
            foreach (FactoryEntity entity in state.Factory.Entities)
            {
                FactorySpec spec = entity == null ? null : FactoryCatalog.Get(entity.Kind);
                if (spec == null || !IsLightTransport(entity.Kind) || entity.X > maxX || entity.Z > maxZ ||
                    entity.X + spec.Width - 1 < minX || entity.Z + spec.Height - 1 < minZ) continue;
                reason = "수면 물류 설비를 먼저 철거해야 도로 교량을 철거할 수 있습니다.";
                return false;
            }
            reason = "";
            return true;
        }

        public static bool HasAutomatedOutput(GameState state, Cell cell)
        {
            if (state?.Factory?.Entities == null || cell == null) return false;
            foreach (FactoryEntity inserter in state.Factory.Entities)
            {
                if (inserter == null || inserter.Kind != FactoryKind.Inserter) continue;
                int pickupX = inserter.X - DirectionX(inserter.Direction);
                int pickupZ = inserter.Z - DirectionZ(inserter.Direction);
                if (MicroBelongsToCell(pickupX, pickupZ, cell)) return true;
            }
            return false;
        }

        public static bool HasAutomatedInput(GameState state, Cell cell)
        {
            if (state?.Factory?.Entities == null || cell == null) return false;
            foreach (FactoryEntity inserter in state.Factory.Entities)
            {
                if (inserter == null || inserter.Kind != FactoryKind.Inserter) continue;
                int dropX = inserter.X + DirectionX(inserter.Direction);
                int dropZ = inserter.Z + DirectionZ(inserter.Direction);
                if (MicroBelongsToCell(dropX, dropZ, cell)) return true;
            }
            return false;
        }

        public static void Migrate(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            EnsureCityBuffers(state);
            if (state.Version != 3) return;

            FactoryState old = state.Factory ?? FactoryState.CreateEmpty();
            FactoryState active = FactoryState.CreateCityGrid();
            CopyInventory(old.Produced, active.Produced);
            CopyInventory(old.Exported, active.Exported);
            active.ElapsedSeconds = old.ElapsedSeconds;

            EnsureStock(state);
            if (old.Recovered != null)
                for (int i = 1; i < Math.Min(9, old.Recovered.Count); i++) state.Stock[i] += Math.Max(0, old.Recovered[i]);
            if (old.Entities != null)
            {
                foreach (FactoryEntity entity in old.Entities)
                {
                    if (entity == null) continue;
                    RecoverInventory(state, entity.Input);
                    RecoverInventory(state, entity.Output);
                    if (IsCargo(entity.CargoResource)) state.Stock[(int)entity.CargoResource] += 1f;
                    FactorySpec spec = FactoryCatalog.Get(entity.Kind);
                    if (spec == null) continue;
                    state.Coins += spec.CoinCost;
                    state.Stock[(int)Resource.Timber] += spec.TimberCost;
                    state.Stock[(int)Resource.Stone] += spec.StoneCost;
                }
            }
            state.Stock[(int)Resource.Coins] = state.Coins;
            state.ArchivedFactory = old;
            state.Factory = active;
            state.Version = 4;
        }

        internal static void EnsureCityBuffers(GameState state)
        {
            if (state?.Cells == null) return;
            foreach (Cell cell in state.Cells) if (cell != null) EnsureCellBuffers(cell);
        }

        internal static void EnsureCellBuffers(Cell cell)
        {
            cell.LogisticsInput ??= Cell.NewLogisticsBuffer();
            cell.LogisticsOutput ??= Cell.NewLogisticsBuffer();
            EnsureBuffer(cell.LogisticsInput);
            EnsureBuffer(cell.LogisticsOutput);
        }

        internal static float BufferTotal(List<float> buffer)
        {
            float total = 0;
            if (buffer != null) for (int i = 1; i < Math.Min(9, buffer.Count); i++) total += buffer[i];
            return total;
        }

        static bool IsLightTransport(FactoryKind kind) => kind == FactoryKind.Belt || kind == FactoryKind.Inserter || kind == FactoryKind.Pole || kind == FactoryKind.Splitter;
        static bool IsCargo(Resource resource) => (int)resource >= 1 && (int)resource <= 8;
        static bool Matches(Resource item, Resource filter) => filter == Resource.Coins || item == filter;
        static int DirectionX(int direction) => direction == 0 ? 1 : direction == 2 ? -1 : 0;
        static int DirectionZ(int direction) => direction == 1 ? 1 : direction == 3 ? -1 : 0;
        static bool MicroBelongsToCell(int x, int z, Cell cell) => x >= 0 && z >= 0 && x / Resolution == cell.X && z / Resolution == cell.Z;

        Cell CellAtMicro(int x, int z)
        {
            if (x < 0 || z < 0) return null;
            int cityX = x / Resolution, cityZ = z / Resolution;
            if (cityX >= state.Size || cityZ >= state.Size || state.Cells == null || state.Cells.Count != state.Size * state.Size) return null;
            return state.Cells[cityZ * state.Size + cityX];
        }

        bool IsOwned(int x, int z) => state.OwnedRegions != null && state.OwnedRegions.Contains((z / 7) * 3 + x / 7);

        bool TouchesConnectedRoad(FactoryEntity entity)
        {
            FactorySpec spec = FactoryCatalog.Get(entity.Kind);
            if (spec == null) return false;
            int firstX = entity.X / Resolution, lastX = (entity.X + spec.Width - 1) / Resolution;
            int firstZ = entity.Z / Resolution, lastZ = (entity.Z + spec.Height - 1) / Resolution;
            for (int z = firstZ; z <= lastZ; z++)
            for (int x = firstX; x <= lastX; x++)
            {
                if (IsConnectedRoad(x + 1, z) || IsConnectedRoad(x - 1, z) || IsConnectedRoad(x, z + 1) || IsConnectedRoad(x, z - 1)) return true;
            }
            return false;
        }

        bool IsConnectedRoad(int x, int z)
        {
            if (x < 0 || z < 0 || x >= state.Size || z >= state.Size || state.Cells == null || state.Cells.Count != state.Size * state.Size) return false;
            Cell cell = state.Cells[z * state.Size + x];
            return cell != null && cell.Connected && (cell.Building == BuildingKind.Road || cell.Building == BuildingKind.TownHall);
        }

        static void EnsureStock(GameState state)
        {
            state.Stock ??= new List<float>();
            while (state.Stock.Count < 9) state.Stock.Add(0);
            if (state.Factory == null) state.Factory = FactoryState.CreateCityGrid();
        }

        static void EnsureBuffer(List<float> buffer)
        {
            while (buffer.Count < 9) buffer.Add(0);
            if (buffer.Count > 9) buffer.RemoveRange(9, buffer.Count - 9);
            buffer[0] = 0;
        }

        static void EnsureInventory(List<int> inventory)
        {
            while (inventory.Count < 9) inventory.Add(0);
            if (inventory.Count > 9) inventory.RemoveRange(9, inventory.Count - 9);
            inventory[0] = 0;
        }

        static void CopyInventory(List<int> source, List<int> destination)
        {
            EnsureInventory(destination);
            if (source == null) return;
            for (int i = 1; i < Math.Min(9, source.Count); i++) destination[i] = Math.Max(0, source[i]);
        }

        static void RecoverInventory(GameState state, List<int> inventory)
        {
            if (inventory == null) return;
            for (int i = 1; i < Math.Min(9, inventory.Count); i++) state.Stock[i] += Math.Max(0, inventory[i]);
        }
    }
}

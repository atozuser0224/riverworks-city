using System;
using System.Collections.Generic;
using System.IO;

namespace Riverworks
{
    [Serializable]
    public sealed class FactoryPlatform
    {
        public int X, Z, Floor;
    }

    public static class FactoryLayers
    {
        public const int MaxFloor = 2;
        public const int PlatformSize = 2;
        public const int CoinCost = 30;
        public const int SteelBeamCost = 2;
        public const int ModularFrameCost = 1;

        public static bool HasPlatform(FactoryState state, int x, int z, int floor)
        {
            if (state?.Platforms == null || floor < 1 || floor > MaxFloor) return false;
            foreach (FactoryPlatform platform in state.Platforms)
                if (platform != null && platform.Floor == floor && x >= platform.X && x < platform.X + PlatformSize && z >= platform.Z && z < platform.Z + PlatformSize)
                    return true;
            return false;
        }

        public static bool Supports(FactoryState state, FactoryKind kind, int x, int z, int floor)
        {
            if (floor == 0) return true;
            if (state == null || floor < 1 || floor > MaxFloor || IsGroundOnly(kind)) return false;
            FactorySpec spec = FactoryCatalog.Get(kind);
            if (spec == null) return false;
            for (int zz = z; zz < z + spec.Height; zz++)
            for (int xx = x; xx < x + spec.Width; xx++)
                if (!HasPlatform(state, xx, zz, floor)) return false;
            return true;
        }

        public static bool CanPlacePlatform(GameState gameState, int x, int z, int floor, out string reason)
        {
            if (gameState?.Factory == null) { reason = "공장 상태가 없습니다."; return false; }
            FactoryState state = gameState.Factory;
            if (floor < 1 || floor > MaxFloor) { reason = "플랫폼은 지상 위의 2층 또는 3층에 건설할 수 있습니다."; return false; }
            if (x < 0 || z < 0 || x + PlatformSize > state.Width || z + PlatformSize > state.Height || (x & 1) != 0 || (z & 1) != 0)
            { reason = "플랫폼은 도시 부지에 맞춘 2x2 위치가 필요합니다."; return false; }
            if (HasPlatform(state, x, z, floor)) { reason = "이미 이 층에 플랫폼이 있습니다."; return false; }
            TechId technology = floor == 1 ? TechId.MassProduction : TechId.AdvancedManufacturing;
            if (!TechCatalog.Has(gameState, technology)) { reason = $"{TechCatalog.Get(technology)?.Name ?? "필요 기술"} 연구가 필요합니다."; return false; }
            if (floor == 2 && !HasExactPlatform(state, x, z, 1)) { reason = "3층 플랫폼 아래에 같은 위치의 2층 플랫폼이 필요합니다."; return false; }
            if (!CanUseCityLot(gameState, x, z, out reason)) return false;
            if (!ValidStock(gameState)) { reason = "도시 자원 상태가 올바르지 않습니다."; return false; }
            if (gameState.Coins + .001f < CoinCost) { reason = $"코인이 {CoinCost - gameState.Coins:0} 부족합니다."; return false; }
            if (gameState.Stock[(int)Resource.SteelBeam] + .001f < SteelBeamCost) { reason = "강철 보가 2개 필요합니다."; return false; }
            if (gameState.Stock[(int)Resource.ModularFrame] + .001f < ModularFrameCost) { reason = "모듈 프레임이 1개 필요합니다."; return false; }
            reason = ""; return true;
        }

        public static bool TryPlacePlatform(GameState gameState, int x, int z, int floor, out string reason)
        {
            if (!CanPlacePlatform(gameState, x, z, floor, out reason)) return false;
            gameState.Coins -= CoinCost;
            gameState.Stock[(int)Resource.Coins] = gameState.Coins;
            gameState.Stock[(int)Resource.SteelBeam] -= SteelBeamCost;
            gameState.Stock[(int)Resource.ModularFrame] -= ModularFrameCost;
            gameState.Factory.Platforms.Add(new FactoryPlatform { X = x, Z = z, Floor = floor });
            reason = floor == 1 ? "2층 플랫폼 건설 완료" : "3층 플랫폼 건설 완료";
            return true;
        }

        public static bool RemovePlatform(GameState gameState, int x, int z, int floor, out string reason)
        {
            FactoryState state = gameState?.Factory;
            FactoryPlatform platform = FindExact(state, x, z, floor);
            if (platform == null) { reason = "철거할 플랫폼이 없습니다."; return false; }
            if (state.Entities != null)
            {
                foreach (FactoryEntity entity in state.Entities)
                {
                    FactorySpec spec = entity == null ? null : FactoryCatalog.Get(entity.Kind);
                    if (spec != null && entity.Floor == floor && Overlaps(x, z, PlatformSize, PlatformSize, entity.X, entity.Z, spec.Width, spec.Height))
                    { reason = "플랫폼 위 설비를 먼저 철거해야 합니다."; return false; }
                }
            }
            if (floor < MaxFloor && FindExact(state, x, z, floor + 1) != null) { reason = "위층 플랫폼을 먼저 철거해야 합니다."; return false; }
            if (!ValidStock(gameState)) { reason = "도시 자원 상태가 올바르지 않습니다."; return false; }
            state.Platforms.Remove(platform);
            gameState.Coins += CoinCost * .35f;
            gameState.Stock[(int)Resource.Coins] = gameState.Coins;
            gameState.Stock[(int)Resource.SteelBeam] += SteelBeamCost;
            gameState.Stock[(int)Resource.ModularFrame] += ModularFrameCost;
            reason = "플랫폼 철거 완료 · 자재 전량과 코인 35% 회수";
            return true;
        }

        public static void Validate(FactoryState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Platforms == null) throw new InvalidDataException("Factory platforms are missing.");
            var keys = new HashSet<string>();
            foreach (FactoryPlatform platform in state.Platforms)
            {
                if (platform == null || platform.Floor < 1 || platform.Floor > MaxFloor || platform.X < 0 || platform.Z < 0 ||
                    platform.X + PlatformSize > state.Width || platform.Z + PlatformSize > state.Height || (platform.X & 1) != 0 || (platform.Z & 1) != 0)
                    throw new InvalidDataException("Factory platform is invalid.");
                if (!keys.Add(platform.X + ":" + platform.Z + ":" + platform.Floor)) throw new InvalidDataException("Factory platform is duplicated.");
                if (platform.Floor == 2 && !HasExactPlatform(state, platform.X, platform.Z, 1)) throw new InvalidDataException("Upper platform has no lower support.");
            }
            if (state.Entities == null) throw new InvalidDataException("Factory entities are missing.");
            foreach (FactoryEntity entity in state.Entities)
                if (entity != null && !Supports(state, entity.Kind, entity.X, entity.Z, entity.Floor)) throw new InvalidDataException("Upper factory entity lacks platform support.");
        }

        public static void ValidateCity(GameState gameState)
        {
            if (gameState?.Factory == null) throw new InvalidDataException("Factory state is missing.");
            Validate(gameState.Factory);
            foreach (FactoryPlatform platform in gameState.Factory.Platforms)
                if (!CanUseCityLot(gameState, platform.X, platform.Z, out string reason)) throw new InvalidDataException("Factory platform city collision: " + reason);
        }

        static bool CanUseCityLot(GameState gameState, int microX, int microZ, out string reason)
        {
            int cityX = microX / CityLogistics.Resolution, cityZ = microZ / CityLogistics.Resolution;
            if (cityX < 0 || cityZ < 0 || cityX >= gameState.Size || cityZ >= gameState.Size || gameState.Cells == null || gameState.Cells.Count != gameState.Size * gameState.Size)
            { reason = "도시 경계를 벗어납니다."; return false; }
            if (gameState.OwnedRegions == null || !gameState.OwnedRegions.Contains((cityZ / 7) * 3 + cityX / 7)) { reason = "먼저 이 도시 구역을 매입해야 합니다."; return false; }
            Cell cell = gameState.Cells[cityZ * gameState.Size + cityX];
            if (cell == null || cell.Terrain == TerrainKind.Water) { reason = "플랫폼은 소유한 육지에만 건설할 수 있습니다."; return false; }
            if (cell.Building != BuildingKind.None && cell.Building != BuildingKind.Road) { reason = "도시 건물이 있는 부지에는 플랫폼을 건설할 수 없습니다."; return false; }
            if (CityProjects.Occupies(gameState, cityX, cityZ)) { reason = "도시 프로젝트가 사용하는 부지입니다."; return false; }
            reason = ""; return true;
        }

        static bool HasExactPlatform(FactoryState state, int x, int z, int floor) => FindExact(state, x, z, floor) != null;
        static FactoryPlatform FindExact(FactoryState state, int x, int z, int floor)
        {
            if (state?.Platforms == null) return null;
            foreach (FactoryPlatform platform in state.Platforms) if (platform != null && platform.X == x && platform.Z == z && platform.Floor == floor) return platform;
            return null;
        }
        static bool IsGroundOnly(FactoryKind kind) => kind == FactoryKind.Drill || kind == FactoryKind.WaterPump || kind == FactoryKind.OilPump ||
            kind == FactoryKind.ImportDock || kind == FactoryKind.ExportDock || kind == FactoryKind.PowerInlet;
        static bool Overlaps(int ax, int az, int aw, int ah, int bx, int bz, int bw, int bh) => ax < bx + bw && ax + aw > bx && az < bz + bh && az + ah > bz;
        static bool ValidStock(GameState state)
        {
            if (state.Stock == null || state.Stock.Count != ResourceCatalog.Count || float.IsNaN(state.Coins) || float.IsInfinity(state.Coins) || state.Coins < 0) return false;
            foreach (float value in state.Stock) if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) return false;
            return true;
        }
    }
}

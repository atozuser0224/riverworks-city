using System;
using System.Collections.Generic;

namespace Riverworks
{
    public enum Resource { Coins, Timber, Stone, Grain, Flour, Bread, Ore, Steel, Tools }
    public enum BuildingKind { None, TownHall, Road, House, Lumberyard, Quarry, Farm, Mill, Bakery, Mine, Smelter, Workshop, Windmill, Park, Warehouse, Market, StudyHouse, Academy, SteamPlant }
    public enum TerrainKind { Grass, Forest, Rock, Water }
    public enum Era { Medieval, Renaissance, Industrial }
    public enum TechId
    {
        None,
        CropRotation,
        Stonecraft,
        MechanicalPower,
        Guilds,
        Metallurgy,
        Scholarship,
        Toolmaking,
        SteamPower,
        UrbanPlanning,
        Forestry,
        Irrigation,
        Masonry,
        Logistics,
        Education,
        MetallurgicalEfficiency,
        Electrification,
        MassProduction,
        Automation
    }

    [Serializable]
    public class Cell
    {
        public int X, Z;
        public TerrainKind Terrain;
        public BuildingKind Building;
        public int Level;
        public bool Connected;
        public float Progress;
        public string Status = "";
        public List<float> LogisticsInput = NewLogisticsBuffer();
        public List<float> LogisticsOutput = NewLogisticsBuffer();

        public static List<float> NewLogisticsBuffer() => new List<float>(new float[9]);
    }

    [Serializable]
    public class GameState
    {
        public int Version = 4, Size = 21, Day, Population, Happiness;
        public float Coins;
        public float DayProgressSeconds;
        public List<float> Stock = new List<float>();
        public List<Cell> Cells = new List<Cell>();
        public List<int> OwnedRegions = new List<int>();
        public int Milestone;
        public float TotalProduced;
        public float TotalToolsProduced;
        public float LastBreadDemand, LastToolsDemand, LastToolsDelivered;
        public bool Won;
        public Era Era = Era.Medieval;
        public float ResearchPoints;
        public List<TechId> Technologies = new List<TechId>();
        public TechId ActiveResearch;
        public int ResearchDaysRemaining;
        public float LastGrainConsumed;
        public bool LastFoodSatisfied;
        public FactoryState Factory = FactoryState.CreateCityGrid();
        public FactoryState ArchivedFactory;
        public TutorialProgress Tutorial;

        public static GameState CreateNew()
        {
            var state = new GameState { Coins = 1100, Population = 8, Happiness = 72, ResearchPoints = 10 };
            for (int i = 0; i < Enum.GetValues(typeof(Resource)).Length; i++) state.Stock.Add(0);
            state.Stock[(int)Resource.Coins] = state.Coins;
            state.Stock[(int)Resource.Timber] = 90;
            state.Stock[(int)Resource.Stone] = 65;
            state.Stock[(int)Resource.Grain] = 18;
            state.Stock[(int)Resource.Flour] = 8;
            state.Stock[(int)Resource.Bread] = 14;
            state.OwnedRegions.Add(4);

            for (int z = 0; z < state.Size; z++)
            for (int x = 0; x < state.Size; x++)
            {
                int hash = unchecked(x * 73856093 ^ z * 19349663 ^ 0x51A7);
                int value = (hash & 0x7fffffff) % 100;
                TerrainKind terrain = value < 12 ? TerrainKind.Forest : value < 20 ? TerrainKind.Rock : TerrainKind.Grass;
                // A continuous, narrow western river replaces scattered one-tile puddles.
                if (x == 2 || (x == 3 && z % 4 != 0)) terrain = TerrainKind.Water;
                if (x >= 7 && x <= 13 && z >= 7 && z <= 13) terrain = TerrainKind.Grass;
                state.Cells.Add(new Cell { X = x, Z = z, Terrain = terrain });
            }

            Set(10, 10, BuildingKind.TownHall);
            Set(10, 9, BuildingKind.Road); Set(10, 8, BuildingKind.Road);
            Set(9, 10, BuildingKind.Road); Set(8, 10, BuildingKind.Road);
            Set(11, 10, BuildingKind.Road); Set(12, 10, BuildingKind.Road);
            Set(9, 9, BuildingKind.House); Set(11, 9, BuildingKind.House);
            Set(9, 8, BuildingKind.Farm);
            state.Cells[8 * state.Size + 9].Terrain = TerrainKind.Grass;
            // Starter-region renewable resource pockets: reachable from the seeded road.
            state.Cells[10 * state.Size + 7].Terrain = TerrainKind.Forest;
            state.Cells[9 * state.Size + 7].Terrain = TerrainKind.Forest;
            state.Cells[10 * state.Size + 13].Terrain = TerrainKind.Rock;
            state.Cells[11 * state.Size + 13].Terrain = TerrainKind.Rock;
            state.Tutorial = TutorialProgress.Create(state);
            return state;

            void Set(int x, int z, BuildingKind kind)
            {
                Cell cell = state.Cells[z * state.Size + x];
                cell.Building = kind; cell.Level = 1;
            }
        }
    }
}

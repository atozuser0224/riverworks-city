using System;
using System.Collections.Generic;
using System.Linq;

namespace Riverworks
{
    /// <summary>
    /// Explicit numeric values keep persisted tutorial steps stable when labels or copy change.
    /// </summary>
    public enum TutorialStepId
    {
        Welcome = 0,
        SelectTownHall = 1,
        ExtendRoad = 2,
        BuildHouse = 3,
        BuildLumberyard = 4,
        BuildStudyHouse = 5,
        StartCropRotation = 6,
        CompleteCropRotation = 7,
        MechanicalPowerAdvice = 8,
        SaveCity = 9,
        Finish = 10
    }

    /// <summary>
    /// Small save DTO. Dialogue typing and transient UI state deliberately do not belong here.
    /// </summary>
    [Serializable]
    public sealed class TutorialProgress
    {
        public bool Enabled;
        public bool Completed;
        public bool Skipped;
        public bool Collapsed;
        public TutorialStepId Step;
        public int RoadBaseline;
        public int HouseBaseline;
        public int LumberyardBaseline;
        public int StudyHouseBaseline;
        public int StartDay;
        public bool TownHallIntroPlayed;
        public bool GuidanceEnabled;
        public int GuidanceSeenMask;

        public static TutorialProgress Create(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return new TutorialProgress
            {
                Enabled = true,
                Step = TutorialStepId.Welcome,
                RoadBaseline = ConnectedCount(state, BuildingKind.Road),
                HouseBaseline = ConnectedCount(state, BuildingKind.House),
                LumberyardBaseline = ConnectedCount(state, BuildingKind.Lumberyard),
                StudyHouseBaseline = ConnectedCount(state, BuildingKind.StudyHouse),
                StartDay = Math.Max(0, state.Day),
                GuidanceEnabled = true
            };
        }

        /// <summary>Stops prompts until Resume is chosen. It never changes the city.</summary>
        public void Skip()
        {
            if (Completed) return;
            Enabled = false;
            Skipped = true;
        }

        /// <summary>Continues at the saved step after an explicit skip.</summary>
        public void Resume()
        {
            if (Completed) return;
            Enabled = true;
            Skipped = false;
        }

        public void SetCollapsed(bool collapsed) => Collapsed = collapsed;

        /// <summary>Runtime calls this after the one-time Town Hall opening cue actually plays.</summary>
        public bool MarkTownHallIntroPlayed()
        {
            if (TownHallIntroPlayed) return false;
            TownHallIntroPlayed = true;
            return true;
        }

        /// <summary>
        /// Controls post-tutorial automatic feature lessons. Old serialized progress omits this
        /// field and therefore remains false until the player explicitly changes it.
        /// </summary>
        public void SetGuidanceEnabled(bool enabled) => GuidanceEnabled = enabled;

        /// <summary>
        /// Starts a clean replay against the city's current buildings. Existing valid buildings
        /// remain untouched and become the new anti-instant-completion baselines.
        /// </summary>
        public static TutorialProgress Replay(GameState state) => Create(state);

        /// <summary>
        /// Computes the same road reachability used by Simulation without changing Cell.Connected.
        /// This makes fresh-game baselines correct before Simulation exists and keeps disconnected
        /// buildings from inflating replay baselines.
        /// </summary>
        private static int ConnectedCount(GameState state, BuildingKind kind)
        {
            if (state.Cells == null || state.Size <= 0 || state.Cells.Count != state.Size * state.Size) return 0;
            Cell townHall = state.Cells.FirstOrDefault(cell => cell != null && cell.Building == BuildingKind.TownHall);
            if (townHall == null) return 0;

            var roadNetwork = new HashSet<int>();
            var queue = new Queue<Cell>();
            queue.Enqueue(townHall);
            while (queue.Count > 0)
            {
                Cell current = queue.Dequeue();
                AddRoad(current.X + 1, current.Z);
                AddRoad(current.X - 1, current.Z);
                AddRoad(current.X, current.Z + 1);
                AddRoad(current.X, current.Z - 1);
            }

            if (kind == BuildingKind.Road) return roadNetwork.Count;
            int count = 0;
            foreach (Cell cell in state.Cells)
            {
                if (cell == null || cell.Building != kind) continue;
                if (kind == BuildingKind.TownHall || TouchesNetwork(cell.X, cell.Z)) count++;
            }
            return count;

            void AddRoad(int x, int z)
            {
                if (x < 0 || z < 0 || x >= state.Size || z >= state.Size) return;
                int index = z * state.Size + x;
                Cell cell = state.Cells[index];
                if (cell != null && cell.Building == BuildingKind.Road && roadNetwork.Add(index)) queue.Enqueue(cell);
            }

            bool TouchesNetwork(int x, int z) => IsTownOrRoad(x + 1, z) || IsTownOrRoad(x - 1, z) ||
                IsTownOrRoad(x, z + 1) || IsTownOrRoad(x, z - 1);

            bool IsTownOrRoad(int x, int z)
            {
                if (x < 0 || z < 0 || x >= state.Size || z >= state.Size) return false;
                int index = z * state.Size + x;
                Cell cell = state.Cells[index];
                return cell != null && (cell.Building == BuildingKind.TownHall || roadNetwork.Contains(index));
            }
        }
    }

    /// <summary>
    /// One-frame facts supplied by runtime UI. They are evaluated but never persisted.
    /// </summary>
    public struct TutorialFacts
    {
        public bool DialogueAcknowledged;
        public bool HasSelectedCell;
        public int SelectedX;
        public int SelectedZ;
        public BuildingKind SelectedBuilding;
        public bool SaveSucceeded;
        public int SuccessfulSaveVersion;
    }
}

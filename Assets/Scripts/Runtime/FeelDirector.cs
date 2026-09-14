using System;
using UnityEngine;

namespace Riverworks
{
    public enum FeelCue
    {
        Build,
        Demolish,
        Upgrade,
        Expand,
        Select,
        Denied,
        Trade,
        Save,
        ResearchStart,
        ResearchComplete,
        EraAdvance,
        GoalComplete,
        FactoryBuild,
        FactoryRemove,
        FactoryRecipe,
        FactoryTransfer,
        TownHallArrival
    }

    /// <summary>
    /// Owns the game's Feel feedback entry point. Gameplay code only describes what happened;
    /// this component keeps presentation throttling, pooling and state-edge detection separate.
    /// </summary>
    public sealed class FeelDirector : MonoBehaviour
    {
        public const string ReducedMotionPlayerPrefsKey = "Riverworks.ReducedMotion";
        const float RapidBuildInterval = 0.055f;

        static bool reducedMotionLoaded;
        static bool reducedMotion;

        GameController game;
        GameState observedState;
        WorldFeelEffects worldEffects;
        TechId previousResearch;
        Era previousEra;
        int previousMilestone;
        readonly int[] cueCounts = new int[Enum.GetValues(typeof(FeelCue)).Length];
        readonly float[] lastPlayedAt = new float[Enum.GetValues(typeof(FeelCue)).Length];

        public Transform CameraOffset { get; private set; }
        public int PlayedCount { get; private set; }
        public FeelCue LastCue { get; private set; }
        public int ActiveWorldEffects => worldEffects != null ? worldEffects.ActiveWorldEffects : 0;
        public int PoolSize => worldEffects != null ? worldEffects.PoolSize : 0;
        public event Action<FeelCue> CuePlayed;

        public static event Action<bool> ReducedMotionChanged;

        public static bool ReducedMotion
        {
            get
            {
                if (!reducedMotionLoaded)
                {
                    reducedMotion = PlayerPrefs.GetInt(ReducedMotionPlayerPrefsKey, 0) != 0;
                    reducedMotionLoaded = true;
                }
                return reducedMotion;
            }
            set
            {
                bool oldValue = ReducedMotion;
                if (oldValue == value) return;
                reducedMotion = value;
                PlayerPrefs.SetInt(ReducedMotionPlayerPrefsKey, value ? 1 : 0);
                PlayerPrefs.Save();
                ReducedMotionChanged?.Invoke(value);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            reducedMotionLoaded = false;
            reducedMotion = false;
            ReducedMotionChanged = null;
        }

        public void Initialize(GameController controller)
        {
            if (game == controller && worldEffects != null) return;
            if (game != null) game.Changed -= ObserveState;

            game = controller;
            EnsurePresentationObjects();
            CaptureBaseline(game != null ? game.State : null);
            if (game != null) game.Changed += ObserveState;

            for (int i = 0; i < lastPlayedAt.Length; i++) lastPlayedAt[i] = float.NegativeInfinity;
        }

        public void Play(FeelCue cue, Vector3 worldPosition, Transform target = null)
        {
            EnsurePresentationObjects();
            int index = (int)cue;
            if (index < 0 || index >= cueCounts.Length) return;

            float now = Time.unscaledTime;
            if (IsRapidCue(cue) && now - lastPlayedAt[index] < RapidBuildInterval) return;
            lastPlayedAt[index] = now;

            PlayedCount++;
            LastCue = cue;
            cueCounts[index]++;
            bool reduce = ReducedMotion;
            worldEffects.PlayWorld(cue, worldPosition, reduce);
            game?.CameraRig?.Cinematics?.Pulse(cue);
            if (target != null) worldEffects.PlayTarget(cue, target, reduce);
            if (!reduce && IsMajorCue(cue)) worldEffects.PlayCamera(cue);
            CuePlayed?.Invoke(cue);
        }

        public int CountFor(FeelCue cue)
        {
            int index = (int)cue;
            return index >= 0 && index < cueCounts.Length ? cueCounts[index] : 0;
        }

        public void StopAllAndRestore()
        {
            worldEffects?.StopAllAndRestore();
            game?.CameraRig?.Cinematics?.Reset();
        }

        void EnsurePresentationObjects()
        {
            if (CameraOffset == null)
            {
                var offset = new GameObject("Feel camera additive offset");
                CameraOffset = offset.transform;
                CameraOffset.SetParent(transform, false);
            }
            if (worldEffects == null)
            {
                var host = new GameObject("World Feel effects");
                host.transform.SetParent(transform, false);
                worldEffects = host.AddComponent<WorldFeelEffects>();
                worldEffects.Initialize(CameraOffset);
            }
        }

        void ObserveState()
        {
            GameState current = game != null ? game.State : null;
            if (!ReferenceEquals(current, observedState))
            {
                // Loading and New Game replace the state object. Treat that state as a silent
                // baseline so saved progress never looks like an achievement that just happened.
                StopAllAndRestore();
                CaptureBaseline(current);
                return;
            }
            if (current == null) return;

            TechId completedResearch = previousResearch != TechId.None
                && current.ActiveResearch == TechId.None
                && current.Technologies != null
                && current.Technologies.Contains(previousResearch)
                ? previousResearch
                : TechId.None;
            bool eraAdvanced = current.Era > previousEra;
            int goalsCompleted = Math.Max(0, current.Milestone - previousMilestone);

            CaptureBaseline(current);

            // These transitions are emitted by the simulation tick, so they cannot be hooked at
            // the input call site. Vector3.zero is the town-centred celebration origin.
            if (completedResearch != TechId.None) Play(FeelCue.ResearchComplete, Vector3.zero);
            if (eraAdvanced) Play(FeelCue.EraAdvance, Vector3.zero);
            for (int i = 0; i < goalsCompleted; i++) Play(FeelCue.GoalComplete, Vector3.zero);
        }

        void CaptureBaseline(GameState state)
        {
            observedState = state;
            previousResearch = state != null ? state.ActiveResearch : TechId.None;
            previousEra = state != null ? state.Era : Era.Medieval;
            previousMilestone = state != null ? state.Milestone : 0;
        }

        static bool IsRapidCue(FeelCue cue)
        {
            return cue == FeelCue.Build || cue == FeelCue.FactoryBuild || cue == FeelCue.FactoryTransfer;
        }

        static bool IsMajorCue(FeelCue cue)
        {
            return cue == FeelCue.ResearchComplete || cue == FeelCue.EraAdvance
                || cue == FeelCue.GoalComplete || cue == FeelCue.TownHallArrival;
        }

        void OnDestroy()
        {
            if (game != null) game.Changed -= ObserveState;
            StopAllAndRestore();
        }
    }
}

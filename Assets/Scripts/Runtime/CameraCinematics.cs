using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEngine;

namespace Riverworks
{
    /// <summary>
    /// Drives an OrbitCamera through an invisible pose Transform. The Transform is animated by
    /// Feel, while OrbitCamera remains the sole owner of the actual Camera and manual controls.
    /// </summary>
    public sealed class CameraCinematics : MonoBehaviour
    {
        enum CinematicMode { None, Pulse, Teaching, GuideFollow, TownHall }

        const float ManualSuppressionSeconds = 3f;
        const float LowPriorityPulseInterval = 1.2f;
        const float MinimumSize = OrbitCamera.MinimumZoom;
        const float MaximumSize = OrbitCamera.MaximumZoom;

        OrbitCamera orbit;
        Transform poseData;
        MMF_Player player;
        MMF_Position positionFeedback;
        MMF_Scale sizeFeedback;
        bool playerInitialized;
        CinematicMode mode;
        float endsAt;
        float suppressUntil;
        float lastLowPriorityPulseAt = float.NegativeInfinity;
        Vector3 restingFocus;
        float restingSize = 7f;
        Vector3 teachingDestination;
        float teachingDestinationSize;
        Transform followTarget;
        Vector3 followRestoreFocus;
        float followRestoreSize;
        float followSize = 2.8f;
        bool followEntryComplete;

        public bool IsActive { get; private set; }
        public int PlayCount { get; private set; }
        public bool IsFollowing { get; private set; }
        public Transform FollowTargetTransform => followTarget;
        public Vector3 Focus => IsFollowing && followEntryComplete && FollowTargetIsUsable()
            ? FollowFocus() : poseData != null ? SafeFocus(poseData.localPosition) : AuthoredFocus;
        public float Size => poseData != null ? SafeSize(poseData.localScale.x) : AuthoredSize;

        Vector3 AuthoredFocus => orbit != null ? SafeFocus(orbit.AuthoredFocus) : Vector3.zero;
        float AuthoredSize => orbit != null ? SafeSize(orbit.AuthoredSize) : 7f;

        public void Initialize(OrbitCamera cameraRig)
        {
            if (ReferenceEquals(orbit, cameraRig) && poseData != null) return;
            StopPlayer();
            orbit = cameraRig;
            EnsurePlayer();
            IsActive = false;
            ClearFollowState();
            mode = CinematicMode.None;
            endsAt = 0f;
            suppressUntil = 0f;
            SetPose(AuthoredFocus, AuthoredSize);
        }

        public void PlayTownHall(Vector3 center)
        {
            if (orbit == null || FeelDirector.ReducedMotion) return;

            Vector3 startFocus = IsActive ? Focus : AuthoredFocus;
            float startSize = IsActive ? Size : AuthoredSize;
            center = SafeFocus(center);
            StopPlayer();
            ClearFollowState();
            restingFocus = startFocus;
            restingSize = startSize;
            SetPose(startFocus, startSize);

            positionFeedback.Active = true;
            positionFeedback.AnimatePositionDuration = 0.42f;
            positionFeedback.RelativePosition = false;
            positionFeedback.InitialPosition = startFocus;
            positionFeedback.DestinationPosition = center;
            positionFeedback.AnimatePositionTween = new MMTweenType(MMTween.MMTweenCurve.EaseOutCubic);

            sizeFeedback.Active = true;
            sizeFeedback.AnimateScaleDuration = 1.50f;
            sizeFeedback.RemapCurveZero = startSize;
            sizeFeedback.RemapCurveOne = 3.80f;
            sizeFeedback.AnimateScaleTweenX = Tween(
                0f, 0f,
                0.18f, 0.18f,
                0.34f, 1f,
                0.62f, 1f,
                0.80f, 0.48f,
                1f, 0f);

            StartSequence(CinematicMode.TownHall, 1.52f);
        }

        public void FocusTeaching(Vector3 target, float size = 4.5f)
        {
            if (orbit == null || mode == CinematicMode.TownHall || Time.unscaledTime < suppressUntil) return;
            target = SafeFocus(target);
            size = SafeSize(size);

            if (FeelDirector.ReducedMotion)
            {
                StopPlayer();
                IsActive = false;
                mode = CinematicMode.None;
                SetPose(AuthoredFocus, AuthoredSize);
                return;
            }

            Vector3 startFocus = IsActive ? Focus : orbit.DisplayedFocus;
            float startSize = IsActive ? Size : orbit.DisplayedSize;
            startFocus = SafeFocus(startFocus);
            startSize = SafeSize(startSize);
            StopPlayer();
            ClearFollowState();
            restingFocus = startFocus;
            restingSize = startSize;
            teachingDestination = target;
            teachingDestinationSize = size;
            SetPose(startFocus, startSize);

            positionFeedback.Active = true;
            positionFeedback.AnimatePositionDuration = 0.45f;
            positionFeedback.RelativePosition = false;
            positionFeedback.InitialPosition = startFocus;
            positionFeedback.DestinationPosition = target;
            positionFeedback.AnimatePositionTween = new MMTweenType(MMTween.MMTweenCurve.EaseInOutCubic);

            sizeFeedback.Active = true;
            sizeFeedback.AnimateScaleDuration = 0.45f;
            sizeFeedback.RemapCurveZero = startSize;
            sizeFeedback.RemapCurveOne = size;
            sizeFeedback.AnimateScaleTweenX = Tween(0f, 0f, 1f, 1f);

            StartSequence(CinematicMode.Teaching, 0.47f);
        }

        public void FollowTarget(Transform target, float size = 2.8f)
        {
            if (orbit == null) return;
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                if (IsFollowing) StopFollowing(false);
                return;
            }
            if (FeelDirector.ReducedMotion || mode == CinematicMode.TownHall
                || Time.unscaledTime < suppressUntil) return;

            size = SafeSize(size);
            if (IsFollowing && followTarget == target)
            {
                followSize = size;
                if (followEntryComplete) SetPose(FollowFocus(), followSize);
                return;
            }

            Vector3 startFocus = IsActive ? Focus : orbit.DisplayedFocus;
            float startSize = IsActive ? Size : orbit.DisplayedSize;
            startFocus = SafeFocus(startFocus);
            startSize = SafeSize(startSize);
            StopPlayer();
            ClearFollowState();

            followTarget = target;
            IsFollowing = true;
            followEntryComplete = false;
            followSize = size;
            followRestoreFocus = AuthoredFocus;
            followRestoreSize = AuthoredSize;
            restingFocus = startFocus;
            restingSize = startSize;
            SetPose(startFocus, startSize);

            positionFeedback.Active = true;
            positionFeedback.AnimatePositionDuration = 0.40f;
            positionFeedback.RelativePosition = false;
            positionFeedback.InitialPosition = startFocus;
            positionFeedback.DestinationPosition = FollowFocus();
            positionFeedback.AnimatePositionTween = new MMTweenType(MMTween.MMTweenCurve.EaseInOutCubic);

            sizeFeedback.Active = true;
            sizeFeedback.AnimateScaleDuration = 0.40f;
            sizeFeedback.RemapCurveZero = startSize;
            sizeFeedback.RemapCurveOne = followSize;
            sizeFeedback.AnimateScaleTweenX = Tween(0f, 0f, 1f, 1f);

            StartSequence(CinematicMode.GuideFollow, 0.42f);
        }

        public void StopFollowing(bool restore = false)
        {
            if (!IsFollowing) return;
            Vector3 focus = restore ? followRestoreFocus : orbit != null ? orbit.DisplayedFocus : Focus;
            float size = restore ? followRestoreSize : orbit != null ? orbit.DisplayedSize : Size;
            focus = SafeFocus(focus);
            size = SafeSize(size);
            StopPlayer();
            ClearFollowState();
            IsActive = false;
            mode = CinematicMode.None;
            endsAt = 0f;
            if (orbit != null) orbit.CommitFocus(focus, size);
            SetPose(focus, size);
        }

        public void Pulse(FeelCue cue)
        {
            if (orbit == null || FeelDirector.ReducedMotion || mode == CinematicMode.TownHall
                || mode == CinematicMode.Teaching || mode == CinematicMode.GuideFollow
                || Time.unscaledTime < suppressUntil) return;

            bool major;
            float factor;
            float duration;
            if (!TryPulseProfile(cue, out major, out factor, out duration)) return;
            if (!major && Time.unscaledTime - lastLowPriorityPulseAt < LowPriorityPulseInterval) return;
            if (!major) lastLowPriorityPulseAt = Time.unscaledTime;

            bool replacingPulse = IsActive && mode == CinematicMode.Pulse;
            Vector3 startFocus = replacingPulse ? restingFocus : IsActive ? Focus : orbit.DisplayedFocus;
            float startSize = replacingPulse ? restingSize : IsActive ? Size : orbit.DisplayedSize;
            startFocus = SafeFocus(startFocus);
            startSize = SafeSize(startSize);
            StopPlayer();
            restingFocus = startFocus;
            restingSize = startSize;
            SetPose(startFocus, startSize);

            positionFeedback.Active = false;
            sizeFeedback.Active = true;
            sizeFeedback.AnimateScaleDuration = duration;
            sizeFeedback.RemapCurveZero = startSize;
            sizeFeedback.RemapCurveOne = SafeSize(startSize * factor);
            sizeFeedback.AnimateScaleTweenX = major
                ? Tween(0f, 0f, 0.30f, 1f, 0.64f, 0.34f, 1f, 0f)
                : Tween(0f, 0f, 0.38f, 1f, 1f, 0f);

            StartSequence(CinematicMode.Pulse, duration + 0.02f);
        }

        public void CancelForManualInput()
        {
            suppressUntil = Time.unscaledTime + ManualSuppressionSeconds;
            if (orbit == null || !IsActive) return;

            // Orbit exposes its actually displayed smooth pose. Commit that exact pose before
            // releasing cinematic ownership so a wheel, pan or pinch cannot cause a camera jump.
            Vector3 displayedFocus = SafeFocus(orbit.DisplayedFocus);
            float displayedSize = SafeSize(orbit.DisplayedSize);
            StopPlayer();
            ClearFollowState();
            orbit.CommitFocus(displayedFocus, displayedSize);
            IsActive = false;
            mode = CinematicMode.None;
            endsAt = 0f;
            SetPose(displayedFocus, displayedSize);
        }

        public void Reset()
        {
            StopPlayer();
            ClearFollowState();
            IsActive = false;
            mode = CinematicMode.None;
            endsAt = 0f;
            SetPose(AuthoredFocus, AuthoredSize);
        }

        void Update()
        {
            if (!IsActive) return;
            if (FeelDirector.ReducedMotion)
            {
                Reset();
                return;
            }
            if (mode == CinematicMode.GuideFollow && !FollowTargetIsUsable())
            {
                StopFollowing(false);
                return;
            }
            if (Time.unscaledTime < endsAt) return;

            if (mode == CinematicMode.GuideFollow)
            {
                StopPlayer();
                followEntryComplete = true;
                endsAt = 0f;
                SetPose(FollowFocus(), followSize);
                return;
            }

            CinematicMode completed = mode;
            StopPlayer();
            IsActive = false;
            mode = CinematicMode.None;
            endsAt = 0f;
            if (completed == CinematicMode.Teaching && orbit != null)
            {
                orbit.CommitFocus(teachingDestination, teachingDestinationSize);
                SetPose(teachingDestination, teachingDestinationSize);
            }
            else SetPose(restingFocus, restingSize);
        }

        void LateUpdate()
        {
            if (!IsFollowing || !followEntryComplete) return;
            if (!FollowTargetIsUsable())
            {
                StopFollowing(false);
                return;
            }
            // OrbitCamera reads Focus in its own LateUpdate and performs the visible smoothing.
            // Mirroring the value here keeps the hidden data Transform inspectable without
            // restarting or reinitializing its completed Feel entry sequence.
            SetPose(FollowFocus(), followSize);
        }

        void EnsurePlayer()
        {
            if (poseData == null)
            {
                var data = new GameObject("Camera cinematic pose data");
                poseData = data.transform;
                poseData.SetParent(transform, false);
            }
            if (player != null) return;

            var host = new GameObject("Camera cinematics MMF player");
            host.transform.SetParent(transform, false);
            player = host.AddComponent<MMF_Player>();
            player.InitializationMode = MMFeedbacks.InitializationModes.Script;
            player.AutoInitialization = false;
            player.ForceTimescaleMode = true;
            player.ForcedTimescaleMode = TimescaleModes.Unscaled;
            player.PlayerTimescaleMode = TimescaleModes.Unscaled;
            player.StopFeedbacksOnDisable = true;
            player.RestoreInitialValuesOnDisable = false;

            positionFeedback = new MMF_Position
            {
                AnimatePositionTarget = poseData.gameObject,
                Mode = MMF_Position.Modes.AtoB,
                Space = MMF_Position.Spaces.Local,
                MovementMode = MMF_Position.MovementModes.Duration,
                RelativePosition = false,
                DeterminePositionsOnPlay = false,
                InitialPosition = Vector3.zero,
                DestinationPosition = Vector3.zero,
                AnimatePositionTween = new MMTweenType(MMTween.MMTweenCurve.EaseInOutCubic)
            };
            sizeFeedback = new MMF_Scale
            {
                AnimateScaleTarget = poseData,
                Mode = MMF_Scale.Modes.Absolute,
                MovementMode = MMF_Scale.MovementModes.Duration,
                AnimateX = true,
                AnimateY = false,
                AnimateZ = false,
                UniformScaling = false,
                DetermineScaleOnPlay = true,
                AllowAdditivePlays = false,
                RemapCurveZero = 7f,
                RemapCurveOne = 7f,
                AnimateScaleTweenX = Tween(0f, 0f, 1f, 0f)
            };
            player.AddFeedback(positionFeedback);
            player.AddFeedback(sizeFeedback);
            player.Initialization();
            player.ComputeCachedTotalDuration();
            playerInitialized = true;
        }

        void StartSequence(CinematicMode nextMode, float duration)
        {
            player.Initialization(true);
            player.ComputeCachedTotalDuration();
            IsActive = true;
            mode = nextMode;
            endsAt = Time.unscaledTime + Mathf.Max(0.02f, duration);
            PlayCount++;
            player.PlayFeedbacks();
        }

        void StopPlayer()
        {
            if (playerInitialized && player != null) player.StopFeedbacks();
        }

        void SetPose(Vector3 focus, float size)
        {
            if (poseData == null) return;
            poseData.localPosition = SafeFocus(focus);
            poseData.localRotation = Quaternion.identity;
            poseData.localScale = new Vector3(SafeSize(size), 1f, 1f);
        }

        Vector3 FollowFocus()
        {
            return FollowTargetIsUsable() ? SafeFocus(followTarget.position + Vector3.up * 0.15f) : AuthoredFocus;
        }

        bool FollowTargetIsUsable()
        {
            return followTarget != null && followTarget.gameObject.activeInHierarchy;
        }

        void ClearFollowState()
        {
            IsFollowing = false;
            followTarget = null;
            followEntryComplete = false;
        }

        static bool TryPulseProfile(FeelCue cue, out bool major, out float factor, out float duration)
        {
            major = false;
            factor = 1f;
            duration = 0f;
            switch (cue)
            {
                case FeelCue.Build:
                    factor = 0.96f; duration = 0.30f; return true;
                case FeelCue.Upgrade:
                    factor = 0.95f; duration = 0.34f; return true;
                case FeelCue.ResearchStart:
                case FeelCue.ResearchComplete:
                    major = true; factor = 0.92f; duration = 0.66f; return true;
                case FeelCue.EraAdvance:
                case FeelCue.GoalComplete:
                    major = true; factor = 0.90f; duration = 0.70f; return true;
                default:
                    return false;
            }
        }

        static Vector3 SafeFocus(Vector3 value)
        {
            if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z)) return Vector3.zero;
            return new Vector3(Mathf.Clamp(value.x, -12f, 12f), Mathf.Clamp(value.y, -4f, 8f), Mathf.Clamp(value.z, -12f, 12f));
        }

        static float SafeSize(float value)
        {
            return Finite(value) ? Mathf.Clamp(value, MinimumSize, MaximumSize) : 7f;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static MMTweenType Tween(params float[] timeValuePairs)
        {
            int count = timeValuePairs.Length / 2;
            var keys = new Keyframe[count];
            for (int i = 0; i < count; i++) keys[i] = new Keyframe(timeValuePairs[i * 2], timeValuePairs[i * 2 + 1]);
            return new MMTweenType(new AnimationCurve(keys));
        }

        void OnDisable()
        {
            Reset();
        }

        void OnDestroy()
        {
            StopPlayer();
        }
    }
}

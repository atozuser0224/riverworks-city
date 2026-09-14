using System.Collections.Generic;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>
    /// Lazily builds Feel 5.6.1 players for runtime-created HUD controls.
    /// Players use unscaled time so menus stay responsive while the simulation is paused.
    /// </summary>
    public sealed class FeelUiFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public enum FeedbackRole { Button, Panel, Resource }

        static FeelUiFeedback lastPressedButton;
        static float lastPressedAt = float.NegativeInfinity;

        public static int PlayersCreated { get; private set; }
        public static int PlaysRequested { get; private set; }
        public static int DeniedNudgesPlayed { get; private set; }
        public static int PanelEntrancesPlayed { get; private set; }
        public static int ResourcePulsesPlayed { get; private set; }

        public static bool ReducedMotion
        {
            get => FeelDirector.ReducedMotion;
            set => FeelDirector.ReducedMotion = value;
        }

        public FeedbackRole Role => role;
        public int CachedPlayerCount => players.Count;
        public bool UsesReducedMotionProfile => profileReducedMotion;

        FeedbackRole role;
        Button button;
        GameObject nudgeTarget;
        Text resourceText;
        Color resourceAccent = new Color(.29f, .75f, .67f, 1f);
        bool configured;
        bool pointerInside;
        bool pointerDown;
        bool profileReducedMotion;

        readonly List<MMF_Player> players = new List<MMF_Player>(4);
        MMF_Player hoverPlayer;
        MMF_Player pressPlayer;
        MMF_Player releaseToHoverPlayer;
        MMF_Player releasePlayer;
        MMF_Player deniedPlayer;
        MMF_Player panelPlayer;
        MMF_Player resourcePlayer;

        public static FeelUiFeedback AttachButton(Button target)
        {
            if (target == null) return null;
            FeelUiFeedback feedback = target.GetComponent<FeelUiFeedback>() ?? target.gameObject.AddComponent<FeelUiFeedback>();
            feedback.ConfigureButton(target);
            return feedback;
        }

        public static FeelUiFeedback AttachPanel(GameObject target)
        {
            if (target == null) return null;
            FeelUiFeedback feedback = target.GetComponent<FeelUiFeedback>() ?? target.AddComponent<FeelUiFeedback>();
            feedback.ConfigurePanel();
            return feedback;
        }

        public static FeelUiFeedback AttachResource(Text target, Color accent)
        {
            if (target == null) return null;
            FeelUiFeedback feedback = target.GetComponent<FeelUiFeedback>() ?? target.gameObject.AddComponent<FeelUiFeedback>();
            feedback.ConfigureResource(target, accent);
            return feedback;
        }

        public static void PulseResource(Text target)
        {
            FeelUiFeedback feedback = target == null ? null : target.GetComponent<FeelUiFeedback>();
            if (feedback != null) feedback.PlayResourcePulse();
        }

        public static bool PlayLastDenied(float maximumAge = .35f)
        {
            if (lastPressedButton == null || !lastPressedButton.isActiveAndEnabled || Time.unscaledTime-lastPressedAt>maximumAge)
                return false;
            lastPressedButton.PlayDeniedNudge();
            return true;
        }

        public static void ResetTestCounters()
        {
            PlayersCreated = 0;
            PlaysRequested = 0;
            DeniedNudgesPlayed = 0;
            PanelEntrancesPlayed = 0;
            ResourcePulsesPlayed = 0;
            lastPressedButton = null;
            lastPressedAt = float.NegativeInfinity;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticState()
        {
            ResetTestCounters();
        }

        void ConfigureButton(Button target)
        {
            role = FeedbackRole.Button;
            button = target;
            Text label = target.GetComponentInChildren<Text>();
            nudgeTarget = label != null ? label.gameObject : target.gameObject;
            resourceText = null;
            configured = true;
            profileReducedMotion = ReducedMotion;
        }

        void ConfigurePanel()
        {
            role = FeedbackRole.Panel;
            button = null;
            nudgeTarget = null;
            resourceText = null;
            configured = true;
            profileReducedMotion = ReducedMotion;
        }

        void ConfigureResource(Text target, Color accent)
        {
            role = FeedbackRole.Resource;
            button = null;
            nudgeTarget = null;
            resourceText = target;
            resourceAccent = accent;
            configured = true;
            profileReducedMotion = ReducedMotion;
        }

        void OnEnable()
        {
            FeelDirector.ReducedMotionChanged += OnReducedMotionChanged;
            if (configured && role == FeedbackRole.Panel) PlayPanelEntrance();
        }

        void OnDisable()
        {
            FeelDirector.ReducedMotionChanged -= OnReducedMotionChanged;
            if (ReferenceEquals(lastPressedButton,this)) lastPressedButton = null;
            pointerInside = false;
            pointerDown = false;
            StopAndRestoreAll();
        }

        void OnDestroy()
        {
            FeelDirector.ReducedMotionChanged -= OnReducedMotionChanged;
            if (ReferenceEquals(lastPressedButton,this)) lastPressedButton = null;
            StopAndRestoreAll();
        }

        void OnReducedMotionChanged(bool value)
        {
            ResetProfile();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!configured || role != FeedbackRole.Button) return;
            pointerInside = true;
            if (pointerDown || button == null || !button.interactable) return;
            StopAndRestoreScalePlayers();
            Play(ref hoverPlayer, () => CreateScalePlayer(HoverCurve(), ReducedMotion ? .01f : .025f, ReducedMotion ? .08f : .11f));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!configured || role != FeedbackRole.Button) return;
            pointerInside = false;
            if (!pointerDown) StopAndRestoreScalePlayers();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!configured || role != FeedbackRole.Button || eventData.button != PointerEventData.InputButton.Left) return;
            if (button == null || !button.interactable)
            {
                PlayDeniedNudge();
                return;
            }

            pointerDown = true;
            lastPressedButton = this;
            lastPressedAt = Time.unscaledTime;
            StopAndRestoreScalePlayers();
            Play(ref pressPlayer, () => CreateScalePlayer(HoldCurve(), ReducedMotion ? -.015f : -.04f, ReducedMotion ? .055f : .075f));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!configured || role != FeedbackRole.Button || eventData.button != PointerEventData.InputButton.Left || !pointerDown) return;
            pointerDown = false;
            StopAndRestoreScalePlayers();
            if (button == null || !button.interactable)
            {
                PlayDeniedNudge();
                return;
            }

            float peak = ReducedMotion ? .02f : .05f;
            float settle = pointerInside ? (ReducedMotion ? .01f : .025f) : 0f;
            if (pointerInside)
                Play(ref releaseToHoverPlayer, () => CreateScalePlayer(PopCurve(peak, settle), 1f, ReducedMotion ? .09f : .13f));
            else
                Play(ref releasePlayer, () => CreateScalePlayer(PopCurve(peak, 0f), 1f, ReducedMotion ? .09f : .13f));
        }

        public void PlayPanelEntrance()
        {
            if (!configured || role != FeedbackRole.Panel) return;
            EnsureCurrentProfile();
            StopAndRestore(panelPlayer);
            Play(ref panelPlayer, CreatePanelPlayer);
            PanelEntrancesPlayed++;
        }

        public void PlayResourcePulse()
        {
            if (!configured || role != FeedbackRole.Resource || resourceText == null) return;
            EnsureCurrentProfile();
            StopAndRestore(resourcePlayer);
            Play(ref resourcePlayer, CreateResourcePlayer);
            ResourcePulsesPlayed++;
        }

        void PlayDeniedNudge()
        {
            EnsureCurrentProfile();
            StopAndRestore(deniedPlayer);
            Play(ref deniedPlayer, CreateDeniedPlayer);
            DeniedNudgesPlayed++;
        }

        void Play(ref MMF_Player player, System.Func<MMF_Player> factory)
        {
            EnsureCurrentProfile();
            if (player == null) player = factory();
            if (player == null) return;
            player.PlayFeedbacks();
            PlaysRequested++;
        }

        MMF_Player CreateScalePlayer(AnimationCurve curve, float amount, float duration)
        {
            return CreatePlayer(player =>
            {
                MMF_Scale scale = new MMF_Scale
                {
                    AnimateScaleTarget = transform,
                    Mode = MMF_Scale.Modes.Additive,
                    MovementMode = MMF_Scale.MovementModes.Duration,
                    AnimateScaleDuration = duration,
                    RemapCurveZero = 0f,
                    RemapCurveOne = amount,
                    AnimateX = true,
                    AnimateY = true,
                    AnimateZ = false,
                    UniformScaling = false,
                    AllowAdditivePlays = false,
                    DetermineScaleOnPlay = true,
                    AnimateScaleTweenX = new MMTweenType(curve, "AnimateX"),
                    AnimateScaleTweenY = new MMTweenType(curve, "AnimateY"),
                    AnimateScaleTweenZ = new MMTweenType(curve, "AnimateZ")
                };
                player.AddFeedback(scale);
            });
        }

        MMF_Player CreatePanelPlayer()
        {
            return CreatePlayer(player =>
            {
                CanvasGroup group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
                MMF_CanvasGroup fade = new MMF_CanvasGroup
                {
                    TargetCanvasGroup = group,
                    Mode = MMF_FeedbackBase.Modes.OverTime,
                    Duration = ReducedMotion ? .12f : .18f,
                    AlphaCurve = new MMTweenType(EaseOutCurve()),
                    RemapZero = 0f,
                    RemapOne = 1f,
                    StartsOff = false,
                    EndsOff = false,
                    RelativeValues = false,
                    AllowAdditivePlays = false,
                    DisableOnStop = false,
                    OnlyPlayIfTargetIsActive = true
                };
                player.AddFeedback(fade);

                if (!ReducedMotion)
                {
                    AnimationCurve settle = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.72f, -.08f), new Keyframe(1f, 0f));
                    MMF_Scale scale = new MMF_Scale
                    {
                        AnimateScaleTarget = transform,
                        Mode = MMF_Scale.Modes.Additive,
                        MovementMode = MMF_Scale.MovementModes.Duration,
                        AnimateScaleDuration = .18f,
                        RemapCurveZero = 0f,
                        RemapCurveOne = -.04f,
                        AnimateX = true,
                        AnimateY = true,
                        AnimateZ = false,
                        UniformScaling = false,
                        DetermineScaleOnPlay = true,
                        AnimateScaleTweenX = new MMTweenType(settle, "AnimateX"),
                        AnimateScaleTweenY = new MMTweenType(settle, "AnimateY"),
                        AnimateScaleTweenZ = new MMTweenType(settle, "AnimateZ")
                    };
                    player.AddFeedback(scale);
                }
            });
        }

        MMF_Player CreateResourcePlayer()
        {
            return CreatePlayer(player =>
            {
                float duration = ReducedMotion ? .14f : .24f;
                AnimationCurve pulse = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.3f, 1f), new Keyframe(1f, 0f));
                MMF_Scale scale = new MMF_Scale
                {
                    AnimateScaleTarget = transform,
                    Mode = MMF_Scale.Modes.Additive,
                    MovementMode = MMF_Scale.MovementModes.Duration,
                    AnimateScaleDuration = duration,
                    RemapCurveZero = 0f,
                    RemapCurveOne = ReducedMotion ? .018f : .06f,
                    AnimateX = true,
                    AnimateY = true,
                    AnimateZ = false,
                    UniformScaling = false,
                    DetermineScaleOnPlay = true,
                    AnimateScaleTweenX = new MMTweenType(pulse, "AnimateX"),
                    AnimateScaleTweenY = new MMTweenType(pulse, "AnimateY"),
                    AnimateScaleTweenZ = new MMTweenType(pulse, "AnimateZ")
                };
                player.AddFeedback(scale);

                MMF_TextColor color = new MMF_TextColor
                {
                    TargetText = resourceText,
                    ColorMode = MMF_TextColor.ColorModes.Interpolate,
                    Duration = duration,
                    DestinationColor = resourceAccent,
                    ColorCurve = pulse,
                    AllowAdditivePlays = false
                };
                player.AddFeedback(color);
            });
        }

        MMF_Player CreateDeniedPlayer()
        {
            return CreatePlayer(player =>
            {
                AnimationCurve nudge = ReducedMotion
                    ? new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.35f, -1f), new Keyframe(.7f, 1f), new Keyframe(1f, 0f))
                    : new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.2f, -1f), new Keyframe(.48f, 1f), new Keyframe(.74f, -.55f), new Keyframe(1f, 0f));
                MMF_Position position = new MMF_Position
                {
                    AnimatePositionTarget = nudgeTarget != null ? nudgeTarget : gameObject,
                    Mode = MMF_Position.Modes.AlongCurve,
                    Space = MMF_Position.Spaces.RectTransform,
                    MovementMode = MMF_Position.MovementModes.Duration,
                    AnimatePositionDuration = ReducedMotion ? .1f : .16f,
                    RelativePosition = true,
                    DeterminePositionsOnPlay = true,
                    InitialPosition = Vector3.zero,
                    AnimateX = true,
                    AnimateY = false,
                    AnimateZ = false,
                    RemapCurveZero = 0f,
                    RemapCurveOne = ReducedMotion ? 2f : 5f,
                    AnimatePositionTweenX = new MMTweenType(nudge, "AnimateX"),
                    AnimatePositionTweenY = new MMTweenType(nudge, "AnimateY"),
                    AnimatePositionTweenZ = new MMTweenType(nudge, "AnimateZ")
                };
                player.AddFeedback(position);
            });
        }

        MMF_Player CreatePlayer(System.Action<MMF_Player> configure)
        {
            // MMF_Player disallows siblings of the same type. Each lazy cue gets a
            // tiny child host while all feedback targets remain the original UI object.
            GameObject host = new GameObject("__Feel_" + role + "_" + players.Count);
            host.transform.SetParent(transform, false);
            MMF_Player player = host.AddComponent<MMF_Player>();
            player.InitializationMode = MMFeedbacks.InitializationModes.Script;
            player.AutoInitialization = false;
            player.ForceTimescaleMode = true;
            player.ForcedTimescaleMode = TimescaleModes.Unscaled;
            player.PlayerTimescaleMode = TimescaleModes.Unscaled;
            player.RestoreInitialValuesOnDisable = true;
            player.StopFeedbacksOnDisable = true;
            configure(player);
            player.Initialization();
            player.ComputeCachedTotalDuration();
            players.Add(player);
            PlayersCreated++;
            return player;
        }

        void EnsureCurrentProfile()
        {
            if (profileReducedMotion != ReducedMotion) ResetProfile();
        }

        void ResetProfile()
        {
            StopAndRestoreAll();
            foreach (MMF_Player player in players)
            {
                if (player == null) continue;
                GameObject host = player.gameObject;
                if (Application.isPlaying) Destroy(host);
                else DestroyImmediate(host);
            }
            players.Clear();
            hoverPlayer = pressPlayer = releaseToHoverPlayer = releasePlayer = deniedPlayer = null;
            panelPlayer = resourcePlayer = null;
            profileReducedMotion = ReducedMotion;
        }

        void StopAndRestoreScalePlayers()
        {
            StopAndRestore(hoverPlayer);
            StopAndRestore(pressPlayer);
            StopAndRestore(releaseToHoverPlayer);
            StopAndRestore(releasePlayer);
        }

        void StopAndRestoreAll()
        {
            foreach (MMF_Player player in players) StopAndRestore(player);
        }

        static void StopAndRestore(MMF_Player player)
        {
            if (player == null) return;
            player.StopFeedbacks();
            player.RestoreInitialValues();
        }

        static AnimationCurve HoverCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
        }

        static AnimationCurve HoldCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
        }

        static AnimationCurve PopCurve(float peak, float settle)
        {
            return new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.38f, peak), new Keyframe(1f, settle));
        }

        static AnimationCurve EaseOutCurve()
        {
            return new AnimationCurve(new Keyframe(0f, 0f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f));
        }
    }
}

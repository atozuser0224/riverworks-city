using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riverworks
{
    /// <summary>
    /// A fixed-budget, unscaled-time pool of transient world effects and target animations.
    /// Every animation is driven by an MMF_Player feedback sequence.
    /// </summary>
    public sealed class WorldFeelEffects : MonoBehaviour
    {
        const int WorldPoolCapacity = 10;
        const int TargetPoolCapacity = 6;
        const int RingSegments = 48;

        sealed class WorldSlot
        {
            public GameObject Root;
            public MMF_Player Player;
            public MMF_Scale Scale;
            public MMF_Position Position;
            public MMF_LineRenderer LineFade;
            public MMF_Particles ParticlesFeedback;
            public LineRenderer Ring;
            public Transform RingTransform;
            public ParticleSystem Particles;
            public float EndsAt;
        }

        sealed class TargetSlot
        {
            public GameObject Host;
            public MMF_Player Player;
            public MMF_Scale Scale;
            public MMF_Position Position;
            public Transform Target;
            public Vector3 BasePosition;
            public Vector3 BaseScale;
            public float EndsAt;
            public bool Active;
        }

        struct WorldStyle
        {
            public Color Color;
            public float Radius;
            public float Duration;
            public float Rise;
            public float Width;
            public int ParticleCount;
            public float ParticleSpeed;
            public float ParticleSize;
            public bool AnimateScale;
        }

        readonly WorldSlot[] worldSlots = new WorldSlot[WorldPoolCapacity];
        readonly TargetSlot[] targetSlots = new TargetSlot[TargetPoolCapacity];
        Material lineMaterial;
        Transform cameraOffset;
        MMF_Player cameraPlayer;
        MMF_Position cameraPosition;
        float cameraEndsAt;
        int nextWorldSlot;
        int nextTargetSlot;
        bool initialized;

        public int PoolSize => worldSlots.Length;
        public int ActiveWorldEffects
        {
            get
            {
                int count = 0;
                for (int i = 0; i < worldSlots.Length; i++)
                    if (worldSlots[i] != null && worldSlots[i].Root.activeSelf) count++;
                return count;
            }
        }

        public void Initialize(Transform additiveCameraOffset)
        {
            if (initialized)
            {
                cameraOffset = additiveCameraOffset;
                ConfigureCameraTarget();
                return;
            }

            initialized = true;
            cameraOffset = additiveCameraOffset;
            lineMaterial = CreateLineMaterial();
            for (int i = 0; i < worldSlots.Length; i++) worldSlots[i] = CreateWorldSlot(i);
            for (int i = 0; i < targetSlots.Length; i++) targetSlots[i] = CreateTargetSlot(i);
            CreateCameraPlayer();
        }

        public bool PlayWorld(FeelCue cue, Vector3 worldPosition, bool reducedMotion)
        {
            if (!initialized) Initialize(cameraOffset);
            CleanupDestroyedTargets();
            WorldSlot slot = AcquireWorldSlot(IsCelebration(cue));
            if (slot == null) return false;

            StopWorldSlot(slot);
            WorldStyle style = StyleFor(cue, reducedMotion);
            slot.Root.transform.position = worldPosition + Vector3.up * 0.075f;
            slot.Root.transform.localScale = Vector3.one;
            slot.RingTransform.localScale = Vector3.one;
            slot.Root.SetActive(true);

            ConfigureWorldSlot(slot, style, cue);
            slot.EndsAt = Time.unscaledTime + style.Duration + 0.06f;
            slot.Player.ComputeCachedTotalDuration();
            slot.Player.PlayFeedbacks(worldPosition);
            return true;
        }

        public bool PlayTarget(FeelCue cue, Transform target, bool reducedMotion)
        {
            CleanupDestroyedTargets();
            if (target == null || reducedMotion || !HasTargetMotion(cue)) return false;
            if (!initialized) Initialize(cameraOffset);

            TargetSlot slot = FindTargetSlot(target) ?? AcquireTargetSlot();
            if (slot == null) return false;
            StopTargetSlot(slot);

            slot.Target = target;
            slot.BasePosition = target.localPosition;
            slot.BaseScale = target.localScale;
            ConfigureTargetSlot(slot, cue);
            slot.Active = true;
            slot.EndsAt = Time.unscaledTime + TargetDuration(cue) + 0.04f;
            slot.Player.Initialization(true);
            slot.Player.ComputeCachedTotalDuration();
            slot.Player.PlayFeedbacks(target.position);
            return true;
        }

        public void PlayCamera(FeelCue cue)
        {
            if (cameraOffset == null || cameraPlayer == null) return;
            StopCamera();

            float amplitude = cue == FeelCue.TownHallArrival ? 0.070f
                : cue == FeelCue.EraAdvance ? 0.055f
                : cue == FeelCue.GoalComplete ? 0.042f
                : 0.025f;
            float duration = cue == FeelCue.TownHallArrival ? 0.28f
                : cue == FeelCue.EraAdvance ? 0.46f : 0.34f;
            cameraPosition.AnimatePositionDuration = duration;
            cameraPosition.RemapCurveZero = 0f;
            cameraPosition.RemapCurveOne = amplitude;
            cameraOffset.localPosition = Vector3.zero;
            cameraOffset.localEulerAngles = Vector3.zero;
            cameraPlayer.Initialization(true);
            cameraPlayer.ComputeCachedTotalDuration();
            cameraPlayer.PlayFeedbacks();
            cameraEndsAt = Time.unscaledTime + duration + 0.04f;
        }

        public void StopAllAndRestore()
        {
            for (int i = 0; i < worldSlots.Length; i++)
                if (worldSlots[i] != null) StopWorldSlot(worldSlots[i]);
            for (int i = 0; i < targetSlots.Length; i++)
                if (targetSlots[i] != null) StopTargetSlot(targetSlots[i]);
            StopCamera();
        }

        void Update()
        {
            float now = Time.unscaledTime;
            CleanupDestroyedTargets();
            for (int i = 0; i < worldSlots.Length; i++)
            {
                WorldSlot slot = worldSlots[i];
                if (slot != null && slot.Root.activeSelf && now >= slot.EndsAt) StopWorldSlot(slot);
            }
            for (int i = 0; i < targetSlots.Length; i++)
            {
                TargetSlot slot = targetSlots[i];
                if (slot != null && slot.Active && now >= slot.EndsAt) StopTargetSlot(slot);
            }
            if (cameraEndsAt > 0f && now >= cameraEndsAt) StopCamera();
        }

        void CleanupDestroyedTargets()
        {
            for (int i = 0; i < targetSlots.Length; i++)
            {
                TargetSlot slot = targetSlots[i];
                if (slot == null || !slot.Active) continue;
                // BoardView disables an obsolete model before Destroy; stopping here prevents
                // Feel's next coroutine step from touching a destroyed Unity object.
                if (slot.Target == null || !slot.Target.gameObject.activeInHierarchy) StopTargetSlot(slot);
            }
        }

        WorldSlot CreateWorldSlot(int index)
        {
            var root = new GameObject("Feel world slot " + index.ToString("00"));
            root.transform.SetParent(transform, false);

            var ringHost = new GameObject("Animated ring");
            ringHost.transform.SetParent(root.transform, false);
            var ring = ringHost.AddComponent<LineRenderer>();
            ring.sharedMaterial = lineMaterial;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = RingSegments;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.textureMode = LineTextureMode.Stretch;
            ring.numCornerVertices = 2;
            ring.numCapVertices = 2;
            for (int p = 0; p < RingSegments; p++)
            {
                float angle = p * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(p, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }

            var particles = root.AddComponent<ParticleSystem>();
            var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = lineMaterial;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.maxParticles = 32;
            var emission = particles.emission;
            emission.enabled = false;

            var player = ConfigurePlayer(root);
            var scale = new MMF_Scale
            {
                AnimateScaleTarget = ringHost.transform,
                Mode = MMF_Scale.Modes.Absolute,
                MovementMode = MMF_Scale.MovementModes.Duration,
                AnimateX = true,
                AnimateY = true,
                AnimateZ = true,
                UniformScaling = true,
                RemapCurveZero = 0.12f,
                RemapCurveOne = 1f,
                AnimateScaleTweenX = Tween(0f, 0f, 1f, 1f),
                AnimateScaleTweenY = Tween(0f, 0f, 1f, 1f),
                AnimateScaleTweenZ = Tween(0f, 0f, 1f, 1f)
            };
            var position = new MMF_Position
            {
                AnimatePositionTarget = root,
                Mode = MMF_Position.Modes.AlongCurve,
                Space = MMF_Position.Spaces.World,
                RelativePosition = true,
                DeterminePositionsOnPlay = true,
                InitialPosition = Vector3.zero,
                AnimateX = false,
                AnimateY = true,
                AnimateZ = false,
                AnimatePositionTweenY = Tween(0f, 0f, 0.55f, 1f, 1f, 0f)
            };
            var lineFade = new MMF_LineRenderer
            {
                TargetLineRenderer = ring,
                Mode = MMF_LineRenderer.Modes.OverTime,
                ModifyColor = true,
                ModifyWidth = true,
                Transition = Tween(0f, 0f, 1f, 1f)
            };
            var particleFeedback = new MMF_Particles
            {
                BoundParticleSystem = particles,
                Mode = MMF_Particles.Modes.Emit,
                StopSystemOnInit = true,
                StopSystemOnReset = true,
                StopSystemOnStopFeedback = true
            };
            player.AddFeedback(scale);
            player.AddFeedback(position);
            player.AddFeedback(lineFade);
            player.AddFeedback(particleFeedback);
            player.Initialization();
            player.ComputeCachedTotalDuration();

            var slot = new WorldSlot
            {
                Root = root,
                Player = player,
                Scale = scale,
                Position = position,
                LineFade = lineFade,
                ParticlesFeedback = particleFeedback,
                Ring = ring,
                RingTransform = ringHost.transform,
                Particles = particles
            };
            root.SetActive(false);
            return slot;
        }

        TargetSlot CreateTargetSlot(int index)
        {
            var host = new GameObject("Feel target player " + index.ToString("00"));
            host.transform.SetParent(transform, false);
            var player = ConfigurePlayer(host);
            var scale = new MMF_Scale
            {
                Mode = MMF_Scale.Modes.Additive,
                MovementMode = MMF_Scale.MovementModes.Duration,
                RemapCurveZero = 0f,
                AnimateX = true,
                AnimateY = true,
                AnimateZ = true,
                DetermineScaleOnPlay = true,
                AllowAdditivePlays = false
            };
            var position = new MMF_Position
            {
                Mode = MMF_Position.Modes.AlongCurve,
                Space = MMF_Position.Spaces.Local,
                RelativePosition = true,
                DeterminePositionsOnPlay = true,
                InitialPosition = Vector3.zero,
                AnimateX = false,
                AnimateY = true,
                AnimateZ = false,
                AllowAdditivePlays = false
            };
            player.AddFeedback(position);
            player.AddFeedback(scale);
            return new TargetSlot { Host = host, Player = player, Scale = scale, Position = position };
        }

        void CreateCameraPlayer()
        {
            var host = new GameObject("Feel camera player");
            host.transform.SetParent(transform, false);
            cameraPlayer = ConfigurePlayer(host);
            cameraPosition = new MMF_Position
            {
                Mode = MMF_Position.Modes.AlongCurve,
                Space = MMF_Position.Spaces.Local,
                RelativePosition = true,
                DeterminePositionsOnPlay = true,
                InitialPosition = Vector3.zero,
                AnimateX = true,
                AnimateY = true,
                AnimateZ = false,
                AnimatePositionTweenX = Tween(0f, 0f, 0.18f, 1f, 0.42f, -0.65f, 0.68f, 0.32f, 1f, 0f),
                AnimatePositionTweenY = Tween(0f, 0f, 0.30f, -0.34f, 0.64f, 0.18f, 1f, 0f)
            };
            cameraPlayer.AddFeedback(cameraPosition);
            ConfigureCameraTarget();
        }

        void ConfigureCameraTarget()
        {
            if (cameraPosition == null || cameraOffset == null) return;
            cameraPosition.AnimatePositionTarget = cameraOffset.gameObject;
            cameraOffset.localPosition = Vector3.zero;
            cameraOffset.localEulerAngles = Vector3.zero;
            cameraPlayer.Initialization(true);
            cameraPlayer.ComputeCachedTotalDuration();
        }

        void ConfigureWorldSlot(WorldSlot slot, WorldStyle style, FeelCue cue)
        {
            slot.Ring.widthMultiplier = style.Width;
            slot.Ring.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
            slot.Ring.colorGradient = Gradient(style.Color, style.Color.a, style.Color.a * 0.75f);
            slot.Scale.AnimateScaleDuration = style.Duration;
            slot.Scale.RemapCurveZero = style.AnimateScale ? 0.10f : style.Radius;
            slot.Scale.RemapCurveOne = style.Radius;
            slot.Position.AnimatePositionDuration = style.Duration;
            slot.Position.RemapCurveZero = 0f;
            slot.Position.RemapCurveOne = style.Rise;
            slot.LineFade.Duration = style.Duration;
            slot.LineFade.NewColor = Gradient(style.Color, 0f, 0f);
            slot.LineFade.NewWidth = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 0.16f));
            slot.ParticlesFeedback.EmitCount = style.ParticleCount;
            slot.ParticlesFeedback.DeclaredDuration = style.Duration;

            ParticleSystem particles = slot.Particles;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.duration = Mathf.Max(0.1f, style.Duration);
            main.startLifetime = new ParticleSystem.MinMaxCurve(style.Duration * 0.34f, style.Duration * 0.70f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(style.ParticleSpeed * 0.55f, style.ParticleSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(style.ParticleSize * 0.65f, style.ParticleSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = style.Color;
            main.gravityModifier = cue == FeelCue.Demolish || cue == FeelCue.FactoryRemove ? 0.22f : -0.02f;
            var shape = particles.shape;
            shape.enabled = style.ParticleCount > 0;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0.08f, style.Radius * 0.28f);
            shape.radiusThickness = 1f;
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = Gradient(style.Color, style.Color.a, 0f);
        }

        void ConfigureTargetSlot(TargetSlot slot, FeelCue cue)
        {
            float duration = TargetDuration(cue);
            float scaleAmount = cue == FeelCue.Upgrade ? 0.16f
                : cue == FeelCue.Build ? 0.15f
                : cue == FeelCue.FactoryBuild ? 0.09f
                : cue == FeelCue.Demolish || cue == FeelCue.FactoryRemove ? 0.10f
                : 0.035f;

            slot.Scale.AnimateScaleTarget = slot.Target;
            slot.Scale.AnimateScaleDuration = duration;
            slot.Scale.RemapCurveOne = scaleAmount;
            slot.Position.AnimatePositionTarget = slot.Target.gameObject;
            slot.Position.AnimatePositionDuration = duration;
            slot.Position.RemapCurveZero = 0f;

            if (cue == FeelCue.Build || cue == FeelCue.FactoryBuild)
            {
                // A grounded landing: wider at contact, briefly compressed vertically, then exact baseline.
                slot.Scale.AnimateScaleTweenX = Tween(0f, -0.75f, 0.28f, 0.55f, 0.62f, -0.12f, 1f, 0f);
                slot.Scale.AnimateScaleTweenY = Tween(0f, -1.00f, 0.28f, -0.45f, 0.55f, 0.40f, 1f, 0f);
                slot.Scale.AnimateScaleTweenZ = slot.Scale.AnimateScaleTweenX;
                slot.Position.AnimateX = false;
                slot.Position.AnimateY = true;
                slot.Position.RemapCurveOne = cue == FeelCue.FactoryBuild ? 0.08f : 0.13f;
                slot.Position.AnimatePositionTweenY = Tween(0f, 1f, 0.34f, -0.12f, 0.62f, 0.08f, 1f, 0f);
            }
            else if (cue == FeelCue.Upgrade)
            {
                slot.Scale.AnimateScaleTweenX = Tween(0f, 0f, 0.28f, 0.75f, 0.58f, -0.18f, 1f, 0f);
                slot.Scale.AnimateScaleTweenY = slot.Scale.AnimateScaleTweenX;
                slot.Scale.AnimateScaleTweenZ = slot.Scale.AnimateScaleTweenX;
                slot.Position.AnimateX = false;
                slot.Position.AnimateY = true;
                slot.Position.RemapCurveOne = 0.18f;
                slot.Position.AnimatePositionTweenY = Tween(0f, 0f, 0.32f, 1f, 0.68f, -0.10f, 1f, 0f);
            }
            else if (cue == FeelCue.Denied)
            {
                slot.Scale.AnimateScaleTweenX = Tween(0f, 0f, 0.30f, -0.5f, 0.62f, 0.28f, 1f, 0f);
                slot.Scale.AnimateScaleTweenY = slot.Scale.AnimateScaleTweenX;
                slot.Scale.AnimateScaleTweenZ = slot.Scale.AnimateScaleTweenX;
                slot.Position.AnimateX = true;
                slot.Position.AnimateY = false;
                slot.Position.RemapCurveOne = 0.045f;
                slot.Position.AnimatePositionTweenX = Tween(0f, 0f, 0.24f, -1f, 0.52f, 0.72f, 0.78f, -0.30f, 1f, 0f);
            }
            else
            {
                slot.Scale.AnimateScaleTweenX = Tween(0f, 0f, 0.38f, 1f, 1f, 0f);
                slot.Scale.AnimateScaleTweenY = slot.Scale.AnimateScaleTweenX;
                slot.Scale.AnimateScaleTweenZ = slot.Scale.AnimateScaleTweenX;
                slot.Position.AnimateX = false;
                slot.Position.AnimateY = true;
                slot.Position.RemapCurveOne = 0.04f;
                slot.Position.AnimatePositionTweenY = Tween(0f, 0f, 0.38f, 1f, 1f, 0f);
            }
        }

        WorldSlot AcquireWorldSlot(bool mayReplace)
        {
            for (int i = 0; i < worldSlots.Length; i++)
            {
                int index = (nextWorldSlot + i) % worldSlots.Length;
                if (!worldSlots[index].Root.activeSelf)
                {
                    nextWorldSlot = (index + 1) % worldSlots.Length;
                    return worldSlots[index];
                }
            }
            if (!mayReplace) return null;
            WorldSlot oldest = worldSlots[0];
            for (int i = 1; i < worldSlots.Length; i++)
                if (worldSlots[i].EndsAt < oldest.EndsAt) oldest = worldSlots[i];
            return oldest;
        }

        TargetSlot FindTargetSlot(Transform target)
        {
            for (int i = 0; i < targetSlots.Length; i++)
                if (targetSlots[i].Active && targetSlots[i].Target == target) return targetSlots[i];
            return null;
        }

        TargetSlot AcquireTargetSlot()
        {
            for (int i = 0; i < targetSlots.Length; i++)
            {
                int index = (nextTargetSlot + i) % targetSlots.Length;
                if (!targetSlots[index].Active)
                {
                    nextTargetSlot = (index + 1) % targetSlots.Length;
                    return targetSlots[index];
                }
            }
            TargetSlot oldest = targetSlots[0];
            for (int i = 1; i < targetSlots.Length; i++)
                if (targetSlots[i].EndsAt < oldest.EndsAt) oldest = targetSlots[i];
            return oldest;
        }

        void StopWorldSlot(WorldSlot slot)
        {
            if (slot == null) return;
            if (slot.Player != null) slot.Player.StopFeedbacks();
            if (slot.Particles != null) slot.Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (slot.Root != null)
            {
                slot.Root.transform.localScale = Vector3.one;
                if (slot.RingTransform != null) slot.RingTransform.localScale = Vector3.one;
                slot.Root.SetActive(false);
            }
            slot.EndsAt = 0f;
        }

        void StopTargetSlot(TargetSlot slot)
        {
            if (slot == null) return;
            // Fresh target players have null feedback Timing data until their first
            // Initialization(). Feel's StopFeedbacks dereferences that Timing data.
            if (slot.Active && slot.Player != null) slot.Player.StopFeedbacks();
            if (slot.Target != null)
            {
                slot.Target.localPosition = slot.BasePosition;
                slot.Target.localScale = slot.BaseScale;
            }
            slot.Target = null;
            slot.Active = false;
            slot.EndsAt = 0f;
        }

        void StopCamera()
        {
            if (cameraPlayer != null) cameraPlayer.StopFeedbacks();
            if (cameraOffset != null)
            {
                cameraOffset.localPosition = Vector3.zero;
                cameraOffset.localEulerAngles = Vector3.zero;
            }
            cameraEndsAt = 0f;
        }

        static MMF_Player ConfigurePlayer(GameObject host)
        {
            var player = host.AddComponent<MMF_Player>();
            player.InitializationMode = MMFeedbacks.InitializationModes.Script;
            player.AutoInitialization = false;
            player.ForceTimescaleMode = true;
            player.ForcedTimescaleMode = TimescaleModes.Unscaled;
            player.PlayerTimescaleMode = TimescaleModes.Unscaled;
            player.StopFeedbacksOnDisable = true;
            player.RestoreInitialValuesOnDisable = false;
            return player;
        }

        static MMTweenType Tween(params float[] timeValuePairs)
        {
            int count = timeValuePairs.Length / 2;
            var keys = new Keyframe[count];
            for (int i = 0; i < count; i++) keys[i] = new Keyframe(timeValuePairs[i * 2], timeValuePairs[i * 2 + 1]);
            return new MMTweenType(new AnimationCurve(keys));
        }

        static Gradient Gradient(Color color, float startAlpha, float endAlpha)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(endAlpha, 1f) });
            return gradient;
        }

        static Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            return new Material(shader) { name = "Riverworks Feel pooled unlit material", color = Color.white };
        }

        static bool HasTargetMotion(FeelCue cue)
        {
            return cue == FeelCue.Build || cue == FeelCue.Upgrade || cue == FeelCue.Select
                || cue == FeelCue.Denied || cue == FeelCue.FactoryBuild
                || cue == FeelCue.FactoryRemove || cue == FeelCue.FactoryRecipe;
        }

        static bool IsCelebration(FeelCue cue)
        {
            return cue == FeelCue.ResearchComplete || cue == FeelCue.EraAdvance
                || cue == FeelCue.GoalComplete || cue == FeelCue.TownHallArrival;
        }

        static float TargetDuration(FeelCue cue)
        {
            if (cue == FeelCue.Upgrade) return 0.46f;
            if (cue == FeelCue.Denied) return 0.24f;
            if (cue == FeelCue.FactoryBuild) return 0.30f;
            if (cue == FeelCue.Build) return 0.34f;
            return 0.28f;
        }

        static WorldStyle StyleFor(FeelCue cue, bool reducedMotion)
        {
            WorldStyle style;
            switch (cue)
            {
                case FeelCue.Demolish: style = Style(new Color(0.62f, 0.43f, 0.25f, 0.78f), 0.68f, 0.48f, 0.10f, 0.070f, 13, 1.05f, 0.09f); break;
                case FeelCue.Upgrade: style = Style(new Color(1.00f, 0.72f, 0.22f, 0.88f), 0.82f, 0.52f, 0.12f, 0.072f, 12, 1.20f, 0.075f); break;
                case FeelCue.Expand: style = Style(new Color(0.30f, 0.78f, 0.62f, 0.58f), 2.20f, 0.88f, 0.08f, 0.046f, 7, 0.75f, 0.065f); break;
                case FeelCue.Select: style = Style(new Color(0.82f, 0.93f, 0.90f, 0.48f), 0.42f, 0.28f, 0.035f, 0.040f, 0, 0f, 0f); break;
                case FeelCue.Denied: style = Style(new Color(0.84f, 0.32f, 0.25f, 0.48f), 0.40f, 0.28f, 0.025f, 0.045f, 0, 0f, 0f); break;
                case FeelCue.Trade: style = Style(new Color(0.20f, 0.78f, 0.74f, 0.72f), 0.62f, 0.44f, 0.09f, 0.055f, 8, 0.90f, 0.065f); break;
                case FeelCue.Save: style = Style(new Color(0.36f, 0.66f, 0.94f, 0.64f), 0.58f, 0.42f, 0.06f, 0.050f, 5, 0.70f, 0.055f); break;
                case FeelCue.ResearchStart: style = Style(new Color(0.55f, 0.42f, 0.92f, 0.72f), 0.92f, 0.58f, 0.13f, 0.058f, 12, 1.05f, 0.060f); break;
                case FeelCue.ResearchComplete: style = Style(new Color(0.33f, 0.90f, 0.92f, 0.84f), 1.62f, 0.86f, 0.19f, 0.066f, 19, 1.45f, 0.072f); break;
                case FeelCue.EraAdvance: style = Style(new Color(1.00f, 0.74f, 0.25f, 0.88f), 2.85f, 1.12f, 0.24f, 0.075f, 28, 1.70f, 0.082f); break;
                case FeelCue.GoalComplete: style = Style(new Color(0.42f, 0.92f, 0.48f, 0.86f), 2.45f, 1.02f, 0.22f, 0.071f, 24, 1.55f, 0.078f); break;
                case FeelCue.FactoryBuild: style = Style(new Color(0.95f, 0.62f, 0.20f, 0.78f), 0.50f, 0.40f, 0.075f, 0.052f, 8, 0.90f, 0.060f); break;
                case FeelCue.FactoryRemove: style = Style(new Color(0.66f, 0.46f, 0.28f, 0.74f), 0.50f, 0.42f, 0.08f, 0.052f, 10, 0.95f, 0.070f); break;
                case FeelCue.FactoryRecipe: style = Style(new Color(0.66f, 0.48f, 0.94f, 0.72f), 0.46f, 0.38f, 0.075f, 0.048f, 6, 0.72f, 0.052f); break;
                case FeelCue.FactoryTransfer: style = Style(new Color(0.25f, 0.86f, 0.78f, 0.60f), 0.34f, 0.32f, 0.055f, 0.040f, 4, 0.62f, 0.045f); break;
                case FeelCue.TownHallArrival: style = Style(new Color(1.00f, 0.72f, 0.25f, 0.86f), 1.08f, 0.54f, 0.10f, 0.076f, 15, 1.10f, 0.080f); break;
                default: style = Style(new Color(0.35f, 0.82f, 0.56f, 0.76f), 0.70f, 0.44f, 0.09f, 0.060f, 8, 0.92f, 0.062f); break;
            }

            if (reducedMotion)
            {
                style.Radius = Mathf.Min(style.Radius, IsCelebration(cue) ? 0.95f : 0.55f);
                style.Rise = 0f;
                style.Duration = Mathf.Min(style.Duration, 0.42f);
                style.ParticleCount = Mathf.Min(style.ParticleCount, 5);
                style.ParticleSpeed *= 0.35f;
                style.AnimateScale = false;
            }
            return style;
        }

        static WorldStyle Style(Color color, float radius, float duration, float rise, float width, int count, float speed, float size)
        {
            return new WorldStyle
            {
                Color = color,
                Radius = radius,
                Duration = duration,
                Rise = rise,
                Width = width,
                ParticleCount = count,
                ParticleSpeed = speed,
                ParticleSize = size,
                AnimateScale = true
            };
        }

        void OnDestroy()
        {
            StopAllAndRestore();
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}

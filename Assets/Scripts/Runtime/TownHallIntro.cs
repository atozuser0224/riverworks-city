using System;
using System.Collections;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEngine;

namespace Riverworks
{
    /// <summary>
    /// A presentation-only new-city arrival. TutorialDirector owns when this runs and when
    /// the following conversation begins; this component never changes simulation state.
    /// </summary>
    public sealed class TownHallIntro : MonoBehaviour
    {
        const float ArrivalDuration = 0.82f;
        const float IntroDuration = 1.58f;
        const float ImpactAt = 0.51f;
        const float ReducedArrivalDuration = 0.28f;
        const float ReducedIntroDuration = 0.58f;
        const float ReducedImpactAt = 0.12f;

        GameController game;
        GameState observedState;
        Transform townHall;
        Vector3 baseLocalPosition;
        Quaternion baseLocalRotation;
        Vector3 baseLocalScale;
        Vector3 impactWorldPosition;
        GameObject playerHost;
        MMF_Player arrivalPlayer;
        MMF_Position arrivalPosition;
        MMF_Scale arrivalScale;
        bool playerInitialized;
        AudioSource thudSource;
        AudioClip thudClip;
        int activeRun;
        Transform lastTarget;

        public bool IsPlaying { get; private set; }
        public int PlayCount { get; private set; }
        public Transform Target => townHall != null ? townHall : lastTarget;
        public string TargetName => Target != null ? Target.name : "Details 10 10";
        public Vector3 BaseLocalPosition => baseLocalPosition;
        public Vector3 BaseLocalScale => baseLocalScale;
        public float BaseLocalY => baseLocalPosition.y;
        public float CurrentLocalY => Target != null ? Target.localPosition.y : baseLocalPosition.y;
        public bool FeedbackPlaying => arrivalPlayer != null && arrivalPlayer.IsPlaying;
        public event Action Finished;

        public void Initialize(GameController controller)
        {
            if (game == controller) return;
            if (game != null) game.Changed -= ObserveGame;
            if (IsPlaying) Skip();
            game = controller;
            observedState = game != null ? game.State : null;
            if (game != null) game.Changed += ObserveGame;
        }

        public IEnumerator PlayArrival()
        {
            if (IsPlaying) Skip();

            int run = ++activeRun;
            PlayCount++;
            IsPlaying = true;
            observedState = game != null ? game.State : null;
            townHall = game != null && game.Board != null ? game.Board.ModelAt(10, 10) : null;
            if (townHall == null || !townHall.gameObject.activeInHierarchy)
            {
                Complete(run);
                yield break;
            }

            CaptureBaseTransform();
            game?.CameraRig?.Cinematics?.PlayTownHall(impactWorldPosition);
            EnsurePlayer();
            bool reduced = FeelDirector.ReducedMotion;
            ConfigureArrival(reduced);
            arrivalPlayer.Initialization(true);
            arrivalPlayer.ComputeCachedTotalDuration();
            playerInitialized = true;
            arrivalPlayer.PlayFeedbacks(townHall.position);

            float impactTime = reduced ? ReducedImpactAt : ImpactAt;
            float totalDuration = reduced ? ReducedIntroDuration : IntroDuration;
            float elapsed = 0f;
            bool impactPlayed = false;
            while (IsPlaying && run == activeRun && elapsed < totalDuration)
            {
                if (!TownHallIsUsable())
                {
                    Skip();
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                if (!impactPlayed && elapsed >= impactTime)
                {
                    impactPlayed = true;
                    PlayImpact();
                }
                yield return null;
            }

            if (!IsPlaying || run != activeRun) yield break;
            if (!impactPlayed) PlayImpact();
            Complete(run);
        }

        public void Skip()
        {
            if (!IsPlaying) return;
            activeRun++;
            IsPlaying = false;
            StopAndRestore();
            Finished?.Invoke();
        }

        void Complete(int run)
        {
            if (!IsPlaying || run != activeRun) return;
            IsPlaying = false;
            StopAndRestore();
            Finished?.Invoke();
        }

        void CaptureBaseTransform()
        {
            baseLocalPosition = townHall.localPosition;
            baseLocalRotation = townHall.localRotation;
            baseLocalScale = townHall.localScale;
            impactWorldPosition = townHall.position;
            lastTarget = townHall;
        }

        void EnsurePlayer()
        {
            if (arrivalPlayer != null) return;

            playerHost = new GameObject("Town Hall arrival MMF player");
            playerHost.transform.SetParent(transform, false);
            arrivalPlayer = playerHost.AddComponent<MMF_Player>();
            arrivalPlayer.InitializationMode = MMFeedbacks.InitializationModes.Script;
            arrivalPlayer.AutoInitialization = false;
            arrivalPlayer.ForceTimescaleMode = true;
            arrivalPlayer.ForcedTimescaleMode = TimescaleModes.Unscaled;
            arrivalPlayer.PlayerTimescaleMode = TimescaleModes.Unscaled;
            arrivalPlayer.StopFeedbacksOnDisable = true;
            arrivalPlayer.RestoreInitialValuesOnDisable = false;

            arrivalPosition = new MMF_Position
            {
                Mode = MMF_Position.Modes.AlongCurve,
                Space = MMF_Position.Spaces.Local,
                RelativePosition = true,
                DeterminePositionsOnPlay = true,
                InitialPosition = Vector3.zero,
                AnimateX = false,
                AnimateY = true,
                AnimateZ = false,
                RemapCurveZero = 0f,
                AllowAdditivePlays = false
            };
            arrivalScale = new MMF_Scale
            {
                Mode = MMF_Scale.Modes.Additive,
                MovementMode = MMF_Scale.MovementModes.Duration,
                RemapCurveZero = 0f,
                AnimateX = true,
                AnimateY = true,
                AnimateZ = true,
                UniformScaling = false,
                DetermineScaleOnPlay = true,
                AllowAdditivePlays = false
            };
            arrivalPlayer.AddFeedback(arrivalPosition);
            arrivalPlayer.AddFeedback(arrivalScale);
        }

        void ConfigureArrival(bool reduced)
        {
            townHall.localPosition = baseLocalPosition;
            townHall.localRotation = baseLocalRotation;
            townHall.localScale = baseLocalScale;
            arrivalPosition.AnimatePositionTarget = townHall.gameObject;
            arrivalScale.AnimateScaleTarget = townHall;

            if (reduced)
            {
                arrivalPosition.AnimatePositionDuration = ReducedArrivalDuration;
                arrivalPosition.RemapCurveOne = 0f;
                arrivalPosition.AnimatePositionTweenY = Tween(0f, 0f, 1f, 0f);
                arrivalScale.AnimateScaleDuration = ReducedArrivalDuration;
                arrivalScale.RemapCurveOne = 1f;
                arrivalScale.AnimateScaleTweenX = Tween(0f, -0.035f, 1f, 0f);
                arrivalScale.AnimateScaleTweenY = arrivalScale.AnimateScaleTweenX;
                arrivalScale.AnimateScaleTweenZ = arrivalScale.AnimateScaleTweenX;
                return;
            }

            arrivalPosition.AnimatePositionDuration = ArrivalDuration;
            arrivalPosition.RemapCurveOne = 1f;
            arrivalPosition.AnimatePositionTweenY = Tween(
                0f, 2.20f,
                0.14f, 1.80f,
                0.36f, 0.82f,
                0.60f, 0.08f,
                0.66f, -0.09f,
                0.76f, 0.045f,
                0.87f, -0.018f,
                1f, 0f);

            arrivalScale.AnimateScaleDuration = ArrivalDuration;
            arrivalScale.RemapCurveOne = 1f;
            arrivalScale.AnimateScaleTweenX = Tween(
                0f, 0f, 0.56f, 0f, 0.64f, 0.14f, 0.74f, -0.045f, 0.86f, 0.018f, 1f, 0f);
            arrivalScale.AnimateScaleTweenY = Tween(
                0f, 0f, 0.56f, 0f, 0.64f, -0.18f, 0.74f, 0.065f, 0.86f, -0.022f, 1f, 0f);
            arrivalScale.AnimateScaleTweenZ = arrivalScale.AnimateScaleTweenX;
        }

        void PlayImpact()
        {
            Vector3 worldPosition = TownHallIsUsable() ? townHall.position : impactWorldPosition;
            game?.Feel?.Play(FeelCue.TownHallArrival, worldPosition);
            EnsureThud();
            if (thudSource == null || thudClip == null) return;
            thudSource.transform.position = worldPosition;
            thudSource.pitch = FeelDirector.ReducedMotion ? 0.96f : 1f;
            thudSource.PlayOneShot(thudClip, FeelDirector.ReducedMotion ? 0.28f : 0.46f);
        }

        void EnsureThud()
        {
            if (thudSource == null)
            {
                var host = new GameObject("Town Hall soft thud");
                host.transform.SetParent(transform, false);
                thudSource = host.AddComponent<AudioSource>();
                thudSource.playOnAwake = false;
                thudSource.loop = false;
                thudSource.spatialBlend = 0.58f;
                thudSource.rolloffMode = AudioRolloffMode.Linear;
                thudSource.minDistance = 2.5f;
                thudSource.maxDistance = 24f;
            }
            if (thudClip == null) thudClip = CreateSoftThud();
        }

        static AudioClip CreateSoftThud()
        {
            const int sampleRate = 22050;
            const float duration = 0.27f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            var samples = new float[sampleCount];
            uint noiseState = 0x51A7u;
            float phase = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float normalized = t / duration;
                float frequency = Mathf.Lerp(82f, 48f, normalized);
                phase += Mathf.PI * 2f * frequency / sampleRate;
                float envelope = Mathf.Exp(-15f * t) * Mathf.SmoothStep(1f, 0f, normalized);
                noiseState = noiseState * 1664525u + 1013904223u;
                float noise = ((noiseState >> 8) / 16777215f) * 2f - 1f;
                float body = Mathf.Sin(phase) * 0.34f + Mathf.Sin(phase * 1.87f) * 0.07f;
                samples[i] = Mathf.Clamp((body + noise * 0.035f) * envelope, -0.48f, 0.48f);
            }
            AudioClip clip = AudioClip.Create("Riverworks soft town hall thud", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void StopAndRestore()
        {
            if (playerInitialized && arrivalPlayer != null) arrivalPlayer.StopFeedbacks();
            game?.CameraRig?.Cinematics?.Reset();
            game?.Feel?.StopAllAndRestore();
            if (townHall != null)
            {
                townHall.localPosition = baseLocalPosition;
                townHall.localRotation = baseLocalRotation;
                townHall.localScale = baseLocalScale;
            }
            if (thudSource != null) thudSource.Stop();
            townHall = null;
        }

        bool TownHallIsUsable()
        {
            return townHall != null && townHall.gameObject.activeInHierarchy;
        }

        void ObserveGame()
        {
            GameState current = game != null ? game.State : null;
            bool stateChanged = !ReferenceEquals(current, observedState);
            observedState = current;
            if (IsPlaying && (stateChanged || !TownHallIsUsable())) Skip();
        }

        void Update()
        {
            if (IsPlaying && !TownHallIsUsable()) Skip();
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
            if (IsPlaying) Skip();
        }

        void OnDestroy()
        {
            if (game != null) game.Changed -= ObserveGame;
            if (IsPlaying) Skip();
            if (thudClip != null) Destroy(thudClip);
        }
    }
}

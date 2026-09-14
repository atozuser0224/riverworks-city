using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace Riverworks
{
    /// <summary>Owns the replaceable visual, animation and action-readable props below a resident root.</summary>
    public sealed class ResidentActor : MonoBehaviour
    {
        public const float ModelHeight = .36f;
        const float CrossFadeSeconds = .16f;
        const float OffscreenStep = .10f;

        sealed class BonePose
        {
            public Transform Bone;
            public Quaternion Rotation;
        }

        static Material toolWood, toolIron, toolPaper, toolCanvas;
        readonly Dictionary<HumanBodyBones, BonePose> bones = new Dictionary<HumanBodyBones, BonePose>();
        readonly Dictionary<ResidentAction, GameObject> tools = new Dictionary<ResidentAction, GameObject>();
        readonly List<Renderer> modelRenderers = new List<Renderer>();

        Citizen citizen;
        ResidentVisualLibrary library;
        GameObject model;
        Animator animator;
        Transform modelRoot;
        Vector3 fixedModelPosition;
        Quaternion fixedModelRotation;
        TextMesh label;
        TextMesh labelShadow;
        GameObject toolRoot;
        bool guideStyle;
        int requestedModelIndex;
        float sourceHeight;
        float animationTime;
        float offscreenTime;
        ResidentAction currentAction = ResidentAction.Idle;
        AnimationClip activeClip;
        bool activeClipMatchesAction;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable previousPlayable;
        AnimationClipPlayable currentPlayable;
        bool hasPreviousPlayable;
        bool hasCurrentPlayable;
        float fadeTime;

        public ResidentAction CurrentAction => currentAction;
        public float AnimationTime => animationTime;
        public Animator Animator => animator;
        public GameObject Model => model;
        public bool HasImportedModel => model != null && animator != null;
        public string ActiveClipName { get; private set; } = "None";
        public int VisibleToolCount { get; private set; }

        public void Initialize(Citizen value, bool guide, ResidentVisualLibrary visualLibrary, int modelIndex = -1)
        {
            citizen = value;
            library = visualLibrary;
            guideStyle = guide;
            requestedModelIndex = modelIndex;
            BuildVisual();
            SetAction(ResidentAction.Idle, true);
        }

        public void SetGuideStyle(bool guide)
        {
            SetAppearance(guide, requestedModelIndex);
        }

        public void SetAppearance(bool guide, int modelIndex)
        {
            if (guideStyle == guide && requestedModelIndex == modelIndex) return;
            guideStyle = guide;
            requestedModelIndex = modelIndex;
            BuildVisual();
            SetAction(currentAction, true);
        }

        public void Tick(ResidentAction action, float dt, bool selected, bool showLabel, string labelText, Camera camera)
        {
            if (action != currentAction) SetAction(action, false);
            UpdateLabel(showLabel, labelText, selected, camera);
            ToggleTools(action);

            if (modelRoot == null || dt <= 0f) return;
            dt = Mathf.Min(dt, .25f);
            animationTime += dt;

            bool visible = modelRenderers.Count == 0;
            for (int i = 0; i < modelRenderers.Count && !visible; i++)
                visible = modelRenderers[i] != null && modelRenderers[i].isVisible;

            float evaluateDt = dt;
            if (!visible)
            {
                offscreenTime += dt;
                if (offscreenTime < OffscreenStep) return;
                evaluateDt = offscreenTime;
                offscreenTime = 0f;
            }
            else if (offscreenTime > 0f)
            {
                evaluateDt += offscreenTime;
                offscreenTime = 0f;
            }

            Evaluate(evaluateDt);
            ResetModelRoot();
            if (!activeClipMatchesAction) ApplyProceduralPose(action, animationTime);
            PositionTwoHandTool(action);
        }

        void BuildVisual()
        {
            DestroyGraph();
            bones.Clear();
            tools.Clear();
            modelRenderers.Clear();
            animator = null;
            modelRoot = null;
            activeClip = null;
            activeClipMatchesAction = false;
            ActiveClipName = "None";

            if (model != null)
            {
                model.SetActive(false);
                Destroy(model);
            }
            if (toolRoot != null) Destroy(toolRoot);
            if (label != null) Destroy(label.gameObject);

            GameObject prefab = SelectPrefab();
            if (prefab != null)
            {
                model = Instantiate(prefab, transform, false);
                model.name = guideStyle ? "Resident guide model" : "Resident model";
                modelRoot = model.transform;
                sourceHeight = ResolveSourceHeight(prefab);
                float scale = sourceHeight > .0001f ? ModelHeight / sourceHeight : 1f;
                modelRoot.localScale *= scale;
                modelRoot.localPosition *= scale;
                fixedModelPosition = modelRoot.localPosition;
                fixedModelRotation = modelRoot.localRotation;
                animator = model.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    animator.runtimeAnimatorController = null;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    CaptureBones();
                    CreateGraph();
                }
                modelRenderers.AddRange(model.GetComponentsInChildren<Renderer>(true));
                if (Application.isMobilePlatform)
                {
                    for (int i = 0; i < modelRenderers.Count; i++)
                    {
                        modelRenderers[i].shadowCastingMode = ShadowCastingMode.Off;
                        modelRenderers[i].receiveShadows = false;
                    }
                }
            }

            CreateTools();
            CreateLabel();
            ResetModelRoot();
        }

        GameObject SelectPrefab()
        {
            if (library == null) return null;
            if (guideStyle && library.GuideModel != null) return library.GuideModel;
            if (library.Models == null || library.Models.Length == 0) return null;
            int index = requestedModelIndex;
            if (index < 0) index = citizen == null ? 0 : Mathf.Abs(citizen.Id) % Mathf.Min(3, library.Models.Length);
            index = Mathf.Clamp(index, 0, library.Models.Length - 1);
            return library.Models[index];
        }

        float ResolveSourceHeight(GameObject prefab)
        {
            if (library != null && guideStyle && library.GuideHeight > .0001f) return library.GuideHeight;
            if (library != null && library.ModelHeights != null && library.Models != null)
            {
                for (int i = 0; i < library.Models.Length && i < library.ModelHeights.Length; i++)
                    if (library.Models[i] == prefab && library.ModelHeights[i] > .0001f) return library.ModelHeights[i];
            }
            return 1f;
        }

        void CreateGraph()
        {
            graph = PlayableGraph.Create("Resident animation " + (citizen == null ? 0 : citizen.Id));
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Resident pose", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        void SetAction(ResidentAction action, bool immediate)
        {
            currentAction = action;
            AnimationClip requested = ClipFor(action);
            AnimationClip chosen = requested != null ? requested : library != null ? library.Idle : null;
            activeClipMatchesAction = requested != null || action == ResidentAction.Idle;
            activeClip = chosen;
            ActiveClipName = chosen == null ? "Procedural " + action : activeClipMatchesAction
                ? chosen.name : chosen.name + " (fallback for " + action + ")";

            if (!graph.IsValid() || chosen == null) return;
            if (hasCurrentPlayable && currentPlayable.IsValid() && currentPlayable.GetAnimationClip() == chosen)
            {
                if (immediate) currentPlayable.SetTime(0);
                PoseImmediately(action);
                return;
            }

            if (hasPreviousPlayable && previousPlayable.IsValid())
            {
                graph.Disconnect(mixer, 0);
                previousPlayable.Destroy();
            }
            if (hasCurrentPlayable && currentPlayable.IsValid())
            {
                previousPlayable = currentPlayable;
                hasPreviousPlayable = true;
                graph.Disconnect(mixer, 1);
                graph.Connect(previousPlayable, 0, mixer, 0);
            }
            else hasPreviousPlayable = false;

            currentPlayable = AnimationClipPlayable.Create(graph, chosen);
            currentPlayable.SetApplyFootIK(true);
            currentPlayable.SetApplyPlayableIK(false);
            currentPlayable.SetDuration(chosen.length);
            double startTime = action == ResidentAction.Walk || action == ResidentAction.Carry
                ? StablePhase() * chosen.length : 0d;
            currentPlayable.SetTime(startTime);
            currentPlayable.SetSpeed(action == ResidentAction.Walk ? 3f : action == ResidentAction.Carry ? .65f : 1f);
            currentPlayable.SetDone(false);
            graph.Connect(currentPlayable, 0, mixer, 1);
            hasCurrentPlayable = true;
            fadeTime = immediate || !hasPreviousPlayable ? CrossFadeSeconds : 0f;
            mixer.SetInputWeight(0, fadeTime >= CrossFadeSeconds ? 0f : 1f);
            mixer.SetInputWeight(1, fadeTime >= CrossFadeSeconds ? 1f : 0f);
            PoseImmediately(action);
        }

        float StablePhase()
        {
            uint id = (uint)(citizen == null ? 0 : citizen.Id);
            id ^= id << 13; id ^= id >> 17; id ^= id << 5;
            return (id & 1023u) / 1024f;
        }

        void PoseImmediately(ResidentAction action)
        {
            if (!graph.IsValid()) return;
            graph.Evaluate(0f);
            ResetModelRoot();
            if (!activeClipMatchesAction) ApplyProceduralPose(action, animationTime);
            PositionTwoHandTool(action);
        }

        void Evaluate(float dt)
        {
            if (!graph.IsValid()) return;
            WrapPlayable(currentPlayable, hasCurrentPlayable);
            WrapPlayable(previousPlayable, hasPreviousPlayable);
            if (hasPreviousPlayable)
            {
                fadeTime = Mathf.Min(CrossFadeSeconds, fadeTime + dt);
                float blend = fadeTime / CrossFadeSeconds;
                mixer.SetInputWeight(0, 1f - blend);
                mixer.SetInputWeight(1, blend);
                if (fadeTime >= CrossFadeSeconds)
                {
                    graph.Disconnect(mixer, 0);
                    if (previousPlayable.IsValid()) previousPlayable.Destroy();
                    hasPreviousPlayable = false;
                }
            }
            graph.Evaluate(dt);
        }

        static void WrapPlayable(AnimationClipPlayable playable, bool valid)
        {
            if (!valid || !playable.IsValid()) return;
            AnimationClip clip = playable.GetAnimationClip();
            if (clip == null || clip.length <= .0001f || playable.GetTime() < clip.length) return;
            playable.SetTime(playable.GetTime() % clip.length);
            playable.SetDone(false);
        }

        AnimationClip ClipFor(ResidentAction action)
        {
            if (library == null) return null;
            switch (action)
            {
                case ResidentAction.Walk: return library.Walk;
                case ResidentAction.Carry: return library.Carry;
                case ResidentAction.Chop: return library.Chop;
                case ResidentAction.Dig: return library.Dig;
                case ResidentAction.Farm: return library.Farm;
                case ResidentAction.Knead: return library.Knead;
                case ResidentAction.Hammer: return library.Hammer;
                case ResidentAction.Read: return library.Read;
                case ResidentAction.Talk: return library.Talk;
                case ResidentAction.Operate: return library.Operate;
                case ResidentAction.Sit: return library.Sit;
                default: return library.Idle;
            }
        }

        void CaptureBones()
        {
            HumanBodyBones[] wanted = {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
            };
            for (int i = 0; i < wanted.Length; i++)
            {
                Transform bone = animator.GetBoneTransform(wanted[i]);
                if (bone != null) bones[wanted[i]] = new BonePose { Bone = bone, Rotation = bone.localRotation };
            }
        }

        void ApplyProceduralPose(ResidentAction action, float time)
        {
            float beat = Mathf.Sin(time * (action == ResidentAction.Talk ? 5f : 8f));
            switch (action)
            {
                case ResidentAction.Talk:
                    Rotate(HumanBodyBones.Head, new Vector3(0, beat * 8f, beat * 3f));
                    AimArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                        new Vector3(.13f, .25f + beat * .018f, .08f), 1f); break;
                case ResidentAction.Knead:
                    BendTorso(13f); AimBothHands(new Vector3(0f, .18f + beat * .012f, .10f)); break;
                case ResidentAction.Hammer:
                    BendTorso(8f); AimArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                        HumanBodyBones.RightHand, new Vector3(.08f, .23f + beat * .09f, .08f), 1f); break;
                case ResidentAction.Read:
                    BendTorso(6f); Rotate(HumanBodyBones.Head, new Vector3(18f, beat * 2f, 0));
                    AimBothHands(new Vector3(0f, .24f, .09f)); break;
                case ResidentAction.Operate:
                    BendTorso(5f); AimBothHands(new Vector3(0f, .22f + beat * .018f, .105f)); break;
                case ResidentAction.Chop: case ResidentAction.Dig: case ResidentAction.Farm:
                    BendTorso(12f + beat * 5f); AimBothHands(new Vector3(0f, .22f + beat * .075f, .11f)); break;
                case ResidentAction.Carry:
                    AimBothHands(new Vector3(0f, .19f, .11f)); break;
                case ResidentAction.Sit:
                    BendTorso(7f); break;
            }
        }

        void BendTorso(float x)
        {
            Rotate(HumanBodyBones.Spine, new Vector3(x * .45f, 0, 0));
            Rotate(HumanBodyBones.Chest, new Vector3(x * .55f, 0, 0));
        }

        // A small two-segment aiming helper keeps hand actions readable across different humanoid bone axes.
        void AimBothHands(Vector3 localTarget)
        {
            AimArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                localTarget + Vector3.left * .035f, -1f);
            AimArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                localTarget + Vector3.right * .035f, 1f);
        }

        void AimArm(HumanBodyBones upperKey, HumanBodyBones lowerKey, HumanBodyBones handKey,
            Vector3 localTarget, float side)
        {
            if (!bones.TryGetValue(upperKey, out BonePose upper) ||
                !bones.TryGetValue(lowerKey, out BonePose lower) ||
                !bones.TryGetValue(handKey, out BonePose hand)) return;

            Vector3 shoulder = upper.Bone.position;
            Vector3 elbow = lower.Bone.position;
            Vector3 wrist = hand.Bone.position;
            float upperLength = Vector3.Distance(shoulder, elbow);
            float lowerLength = Vector3.Distance(elbow, wrist);
            if (upperLength < .00001f || lowerLength < .00001f) return;

            Vector3 target = transform.TransformPoint(localTarget);
            Vector3 toTarget = target - shoulder;
            float distance = Mathf.Clamp(toTarget.magnitude,
                Mathf.Abs(upperLength - lowerLength) + .0001f, upperLength + lowerLength - .0001f);
            Vector3 direction = toTarget.sqrMagnitude > .000001f ? toTarget.normalized : transform.forward;
            Vector3 hint = transform.right * side + transform.forward * .45f - transform.up * .15f;
            Vector3 planeNormal = Vector3.Cross(direction, hint).normalized;
            if (planeNormal.sqrMagnitude < .0001f) planeNormal = Vector3.Cross(direction, transform.up).normalized;
            Vector3 bendDirection = Vector3.Cross(planeNormal, direction).normalized;
            float adjacent = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                             (2f * upperLength * distance);
            adjacent = Mathf.Clamp(adjacent, -1f, 1f);
            float perpendicular = Mathf.Sqrt(Mathf.Max(0f, 1f - adjacent * adjacent));
            Vector3 solvedElbow = shoulder + direction * (upperLength * adjacent) +
                                  bendDirection * (upperLength * perpendicular);

            Vector3 currentUpper = lower.Bone.position - upper.Bone.position;
            Vector3 solvedUpper = solvedElbow - shoulder;
            if (currentUpper.sqrMagnitude > .000001f && solvedUpper.sqrMagnitude > .000001f)
                upper.Bone.rotation = Quaternion.FromToRotation(currentUpper, solvedUpper) * upper.Bone.rotation;

            Vector3 currentLower = hand.Bone.position - lower.Bone.position;
            Vector3 solvedLower = target - lower.Bone.position;
            if (currentLower.sqrMagnitude > .000001f && solvedLower.sqrMagnitude > .000001f)
                lower.Bone.rotation = Quaternion.FromToRotation(currentLower, solvedLower) * lower.Bone.rotation;
        }

        void Rotate(HumanBodyBones key, Vector3 euler)
        {
            if (bones.TryGetValue(key, out BonePose pose)) pose.Bone.localRotation *= Quaternion.Euler(euler);
        }

        void CreateTools()
        {
            toolRoot = new GameObject("Resident action tools");
            toolRoot.transform.SetParent(transform, false);
            AddTool(ResidentAction.Read, "Book", ToolBox(new Vector3(.085f,.012f,.055f), ToolPaper));
            AddTool(ResidentAction.Chop, "Axe", HaftAndHead(new Vector3(.018f,.15f,.018f), new Vector3(.065f,.035f,.015f)));
            AddTool(ResidentAction.Dig, "Pickaxe", HaftAndHead(new Vector3(.018f,.16f,.018f), new Vector3(.11f,.018f,.018f)));
            AddTool(ResidentAction.Farm, "Hoe", HaftAndHead(new Vector3(.018f,.17f,.018f), new Vector3(.075f,.014f,.03f)));
            AddTool(ResidentAction.Hammer, "Hammer", HaftAndHead(new Vector3(.018f,.12f,.018f), new Vector3(.065f,.032f,.032f)));
            AddTool(ResidentAction.Carry, "Presentation crate", ToolBox(new Vector3(.12f,.10f,.10f), ToolWood));
            AddTool(ResidentAction.Knead, "Ladle", HaftAndHead(new Vector3(.012f,.12f,.012f), new Vector3(.035f,.025f,.035f)));
            AddTool(ResidentAction.Operate, "Wrench", HaftAndHead(new Vector3(.014f,.11f,.014f), new Vector3(.05f,.018f,.018f)));
            ToggleTools(ResidentAction.Idle);
        }

        GameObject ToolBox(Vector3 size, Material material)
        {
            GameObject root = new GameObject("Tool visual");
            Primitive(root.transform, PrimitiveType.Cube, size, Vector3.zero, material);
            return root;
        }

        GameObject HaftAndHead(Vector3 haftSize, Vector3 headSize)
        {
            GameObject root = new GameObject("Tool visual");
            Primitive(root.transform, PrimitiveType.Cube, haftSize, new Vector3(0,-haftSize.y*.35f,0), ToolWood);
            Primitive(root.transform, PrimitiveType.Cube, headSize, new Vector3(0,haftSize.y*.15f,0), ToolIron);
            return root;
        }

        void AddTool(ResidentAction action, string name, GameObject value)
        {
            value.name = name + " (work presentation prop)";
            bool twoHanded = action == ResidentAction.Read || action == ResidentAction.Carry;
            Transform hand = animator == null || twoHanded ? null : animator.GetBoneTransform(HumanBodyBones.RightHand);
            value.transform.SetParent(hand != null ? hand : toolRoot.transform, false);
            value.transform.localPosition = hand != null ? new Vector3(0,.018f,.025f) : new Vector3(.07f,.18f,.04f);
            value.transform.localRotation = Quaternion.Euler(0, 0, action == ResidentAction.Read ? 0 : -18f);
            if (hand != null)
            {
                Vector3 inherited = hand.lossyScale;
                value.transform.localScale = new Vector3(
                    Mathf.Abs(inherited.x) > .0001f ? 1f / Mathf.Abs(inherited.x) : 1f,
                    Mathf.Abs(inherited.y) > .0001f ? 1f / Mathf.Abs(inherited.y) : 1f,
                    Mathf.Abs(inherited.z) > .0001f ? 1f / Mathf.Abs(inherited.z) : 1f);
            }
            tools[action] = value;
        }

        void PositionTwoHandTool(ResidentAction action)
        {
            if ((action != ResidentAction.Read && action != ResidentAction.Carry) ||
                !tools.TryGetValue(action, out GameObject value) || value == null || animator == null) return;
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (left == null || right == null) return;
            value.transform.position = (left.position + right.position) * .5f + transform.forward * .012f;
            value.transform.rotation = transform.rotation;
        }

        void ToggleTools(ResidentAction action)
        {
            VisibleToolCount = 0;
            foreach (KeyValuePair<ResidentAction, GameObject> pair in tools)
            {
                bool show = pair.Key == action;
                if (pair.Value != null && pair.Value.activeSelf != show) pair.Value.SetActive(show);
                if (show && pair.Value != null) VisibleToolCount++;
            }
        }

        void CreateLabel()
        {
            GameObject value = new GameObject("Resident action label");
            value.transform.SetParent(transform, false);
            value.transform.localPosition = new Vector3(0, .43f, 0);
            label = value.AddComponent<TextMesh>();
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 16;
            label.characterSize = .085f;
            label.color = new Color(.96f,.93f,.83f,1f);
            Font font = GameFont.Load();
            label.font = font;
            if (font != null && font.material != null) label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            var shadowObject = new GameObject("Activity text shadow");
            shadowObject.transform.SetParent(value.transform, false);
            shadowObject.transform.localPosition = new Vector3(.006f, -.006f, .002f);
            labelShadow = shadowObject.AddComponent<TextMesh>();
            labelShadow.anchor = label.anchor;
            labelShadow.alignment = label.alignment;
            labelShadow.fontSize = label.fontSize;
            labelShadow.characterSize = label.characterSize;
            labelShadow.font = font;
            labelShadow.color = new Color(.04f, .07f, .07f, 1f);
            if (font != null && font.material != null) labelShadow.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            label.gameObject.SetActive(false);
        }

        void UpdateLabel(bool show, string value, bool selected, Camera camera)
        {
            if (label == null) return;
            if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
            if (!show) return;
            string next = value ?? string.Empty;
            if (!string.Equals(label.text, next, StringComparison.Ordinal))
            {
                label.text = next;
                if (labelShadow != null) labelShadow.text = next;
            }
            Color color = selected ? new Color(1f,.78f,.18f,1f) : new Color(.96f,.93f,.83f,1f);
            if (label.color != color) label.color = color;
            if (camera != null) label.transform.rotation = camera.transform.rotation;
        }

        static GameObject Primitive(Transform parent, PrimitiveType type, Vector3 scale, Vector3 position, Material material)
        {
            GameObject value = GameObject.CreatePrimitive(type);
            value.transform.SetParent(parent, false);
            value.transform.localScale = scale;
            value.transform.localPosition = position;
            Collider collider = value.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = value.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                if (Application.isMobilePlatform)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }
            return value;
        }

        static Material Shared(ref Material slot, string name, Color color)
        {
            if (slot != null) return slot;
            Shader shader = Shader.Find("Standard");
            slot = new Material(shader) { name = "Resident shared " + name, color = color };
            slot.SetFloat("_Glossiness", .08f);
            return slot;
        }

        static Material ToolWood => Shared(ref toolWood, "tool wood", new Color(.34f,.18f,.08f));
        static Material ToolIron => Shared(ref toolIron, "tool iron", new Color(.18f,.20f,.22f));
        static Material ToolPaper => Shared(ref toolPaper, "book pages", new Color(.91f,.83f,.63f));
        static Material ToolCanvas => Shared(ref toolCanvas, "canvas", new Color(.55f,.35f,.18f));

        void ResetModelRoot()
        {
            if (modelRoot == null) return;
            modelRoot.localPosition = fixedModelPosition;
            modelRoot.localRotation = fixedModelRotation;
        }

        void DestroyGraph()
        {
            hasPreviousPlayable = false;
            hasCurrentPlayable = false;
            if (graph.IsValid()) graph.Destroy();
        }

        void OnDisable()
        {
            if (graph.IsValid()) graph.Stop();
        }

        void OnEnable()
        {
            if (graph.IsValid()) graph.Play();
        }

        void OnDestroy()
        {
            DestroyGraph();
        }
    }
}

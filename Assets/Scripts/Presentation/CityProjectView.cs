using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Riverworks
{
    /// <summary>
    /// Presentation-only miniatures for the city's unique construction projects.
    /// Project state and occupied cells remain wholly owned by the core simulation.
    /// </summary>
    public sealed class CityProjectView : MonoBehaviour
    {
        const float CaptionZoom = 6.2f;
        const float Plot = BoardView.Spacing;

        sealed class ProjectFigure
        {
            public CityProjectState State;
            public GameObject Root;
            public TextMesh Caption;
            public TextMesh CaptionShadow;
            public readonly List<Transform> Rotors = new List<Transform>();
            public int X;
            public int Z;
            public int Stage;
        }

        readonly Dictionary<CityProjectKind, ProjectFigure> figures =
            new Dictionary<CityProjectKind, ProjectFigure>();
        readonly HashSet<CityProjectKind> activeKinds = new HashSet<CityProjectKind>();
        readonly List<CityProjectKind> retiredKinds = new List<CityProjectKind>();
        readonly List<Material> ownedMaterials = new List<Material>();

        GameController controller;
        Transform projectRoot;
        Transform placementRoot;
        LineRenderer placementLine;
        Material concrete, paleConcrete, steel, darkSteel, copper, brass, glass;
        Material brick, ivory, roof, lawn, blueprint, cable, energy, validPlacement, invalidPlacement;

        public int VisibleProjectCount => figures.Count;

        public void Initialize(GameController game)
        {
            controller = game;
            projectRoot = new GameObject("City projects").transform;
            projectRoot.SetParent(transform, false);
            CreatePalette();
            Refresh();
        }

        /// <summary>Shows a collider-free, exact city-lot footprint for project placement.</summary>
        public void ShowPlacement(CityProjectKind kind, int x, int z, bool valid)
        {
            CityProjectSpec spec = CityProjects.Get(kind);
            if (projectRoot == null || spec == null || kind == CityProjectKind.None)
            {
                HidePlacement();
                return;
            }
            EnsurePlacement();
            placementRoot.gameObject.SetActive(true);
            placementRoot.localPosition = BoardView.Position(x, z) + Vector3.up * .115f;
            float minX = -Plot * .5f;
            float minZ = -Plot * .5f;
            float maxX = (spec.Width - .5f) * Plot;
            float maxZ = (spec.Height - .5f) * Plot;
            placementLine.SetPositions(new[]
            {
                new Vector3(minX, 0f, minZ), new Vector3(minX, 0f, maxZ),
                new Vector3(maxX, 0f, maxZ), new Vector3(maxX, 0f, minZ),
                new Vector3(minX, 0f, minZ)
            });
            placementLine.sharedMaterial = valid ? validPlacement : invalidPlacement;
        }

        public void HidePlacement()
        {
            if (placementRoot != null) placementRoot.gameObject.SetActive(false);
        }

        public void Refresh()
        {
            if (controller == null || projectRoot == null) return;

            activeKinds.Clear();
            List<CityProjectState> projects = controller.State == null ? null : controller.State.CityProjects;
            if (projects != null)
            {
                for (int i = 0; i < projects.Count; i++)
                {
                    CityProjectState state = projects[i];
                    if (state == null || state.Kind == CityProjectKind.None || !activeKinds.Add(state.Kind)) continue;

                    int stage = Mathf.Clamp(state.Stage, 0, 3);
                    if (!figures.TryGetValue(state.Kind, out ProjectFigure figure) ||
                        figure.X != state.X || figure.Z != state.Z || figure.Stage != stage)
                    {
                        if (figure != null) DestroyFigure(figure);
                        figure = BuildFigure(state, stage);
                        figures[state.Kind] = figure;
                    }
                    figure.State = state;
                    UpdateCaption(figure);
                }
            }

            retiredKinds.Clear();
            foreach (KeyValuePair<CityProjectKind, ProjectFigure> pair in figures)
                if (!activeKinds.Contains(pair.Key)) retiredKinds.Add(pair.Key);
            for (int i = 0; i < retiredKinds.Count; i++)
            {
                CityProjectKind kind = retiredKinds[i];
                DestroyFigure(figures[kind]);
                figures.Remove(kind);
            }
            UpdateBillboards();
        }

        void LateUpdate()
        {
            UpdateBillboards();
            if (controller == null || controller.GameSpeed <= 0f) return;
            float degrees = Time.deltaTime * controller.GameSpeed * 90f;
            foreach (ProjectFigure figure in figures.Values)
                for (int i = 0; i < figure.Rotors.Count; i++)
                    if (figure.Rotors[i] != null) figure.Rotors[i].Rotate(Vector3.forward, degrees, Space.Self);
        }

        ProjectFigure BuildFigure(CityProjectState state, int stage)
        {
            var root = new GameObject(state.Kind + " project");
            root.transform.SetParent(projectRoot, false);
            root.transform.localPosition = BoardView.Position(state.X, state.Z);
            var figure = new ProjectFigure { State = state, Root = root, X = state.X, Z = state.Z, Stage = stage };

            switch (state.Kind)
            {
                case CityProjectKind.GrandBridge: BuildGrandBridge(root.transform, stage); break;
                case CityProjectKind.CentralPowerPlant: BuildPowerPlant(root.transform, stage, figure.Rotors); break;
                case CityProjectKind.ResearchCampus: BuildResearchCampus(root.transform, stage); break;
            }
            CreateCaption(figure, CaptionHeight(state.Kind));
            return figure;
        }

        void BuildGrandBridge(Transform parent, int stage)
        {
            float centerX = Plot * 1.5f;
            if (stage < 3)
                Outline(parent, "Bridge blueprint footprint", new Vector3(centerX, .018f, 0f),
                    new Vector2(Plot * 4f, Plot), blueprint);

            // Piers and edge girders leave the road centre at its native board height so
            // pedestrians continue across ordinary road and water-road cells unchanged.
            for (int x = 0; x < 4; x++)
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = new Vector3(x * Plot, .045f, side * Plot * .39f);
                Box(parent, "Bridge footing", new Vector3(.22f, .09f, .18f), position, concrete);
            }
            if (stage == 0) return;

            for (int side = -1; side <= 1; side += 2)
            {
                Box(parent, "Road-edge steel girder", new Vector3(Plot * 4f, .10f, .075f),
                    new Vector3(centerX, .105f, side * Plot * .43f), darkSteel);
                for (int tower = 0; tower < 2; tower++)
                {
                    float x = (tower == 0 ? .72f : 2.28f) * Plot;
                    Box(parent, "Suspension tower", new Vector3(.12f, .92f, .12f),
                        new Vector3(x, .53f, side * Plot * .36f), steel);
                }
            }
            for (int tower = 0; tower < 2; tower++)
            {
                float x = (tower == 0 ? .72f : 2.28f) * Plot;
                Box(parent, "Tower crossbeam", new Vector3(.17f, .10f, Plot * .84f),
                    new Vector3(x, .89f, 0f), steel);
            }
            if (stage == 1) return;

            for (int side = -1; side <= 1; side += 2)
            {
                var points = new Vector3[17];
                for (int i = 0; i < points.Length; i++)
                {
                    float t = i / (points.Length - 1f);
                    float x = t * Plot * 3f;
                    float nearestTower = Mathf.Min(Mathf.Abs(x - .72f * Plot), Mathf.Abs(x - 2.28f * Plot));
                    float y = .38f + Mathf.Clamp01(nearestTower / (.78f * Plot)) * .48f;
                    points[i] = new Vector3(x, y, side * Plot * .36f);
                }
                Polyline(parent, "Suspension cable", points, .025f, cable);
                for (int i = 1; i < 8; i++)
                {
                    float x = i * Plot * 3f / 8f;
                    Line(parent, "Vertical hanger", new Vector3(x, .16f, side * Plot * .39f),
                        new Vector3(x, .57f, side * Plot * .36f), .012f, cable);
                }
            }
            if (stage == 2) return;

            for (int tower = 0; tower < 2; tower++)
            for (int side = -1; side <= 1; side += 2)
            {
                float x = (tower == 0 ? .72f : 2.28f) * Plot;
                Ball(parent, "Brass tower cap", new Vector3(.17f, .12f, .17f),
                    new Vector3(x, 1.04f, side * Plot * .36f), brass);
            }
            for (int x = 0; x < 4; x++)
                Ball(parent, "Bridge lamp", Vector3.one * .075f,
                    new Vector3(x * Plot, .25f, -Plot * .43f), energy);
        }

        void BuildPowerPlant(Transform parent, int stage, List<Transform> rotors)
        {
            Vector3 centre = new Vector3(Plot * .5f, 0f, Plot * .5f);
            Box(parent, "Power plant foundation", new Vector3(Plot * 1.92f, .07f, Plot * 1.92f),
                centre + Vector3.up * .035f, concrete);
            if (stage < 3)
                Outline(parent, "Power plant blueprint footprint", centre + Vector3.up * .075f,
                    new Vector2(Plot * 2f, Plot * 2f), blueprint);
            for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
                Box(parent, "Foundation pedestal", new Vector3(.22f, .13f, .22f),
                    new Vector3(.22f + x * .56f, .12f, .22f + z * .56f), paleConcrete);
            if (stage == 0) return;

            for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
                Box(parent, "Plant frame column", new Vector3(.075f, .72f, .075f),
                    new Vector3(.12f + x * .76f, .43f, .12f + z * .76f), steel);
            Box(parent, "Plant frame beam", new Vector3(.84f, .08f, .08f), new Vector3(.5f, .76f, .12f), steel);
            Box(parent, "Plant frame beam", new Vector3(.84f, .08f, .08f), new Vector3(.5f, .76f, .88f), steel);
            if (stage == 1) return;

            Cyl(parent, "Main turbine casing", .23f, .23f, .65f, new Vector3(.53f, .29f, .48f),
                new Vector3(0f, 0f, 90f), darkSteel, 16);
            Transform rotor = new GameObject("Turbine rotor").transform;
            rotor.SetParent(parent, false);
            rotor.localPosition = new Vector3(.19f, .29f, .48f);
            rotor.localRotation = Quaternion.Euler(0f, 90f, 0f);
            for (int i = 0; i < 4; i++)
            {
                GameObject blade = Box(rotor, "Turbine blade", new Vector3(.035f, .27f, .055f),
                    new Vector3(0f, .11f, 0f), brass);
                blade.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
            }
            rotors.Add(rotor);
            Cyl(parent, "Power station chimney", .13f, .18f, 1.05f, new Vector3(.82f, .57f, .78f),
                Vector3.zero, brick, 16);
            Cyl(parent, "Chimney cap", .16f, .16f, .07f, new Vector3(.82f, 1.11f, .78f),
                Vector3.zero, darkSteel, 16);
            Pipe(parent, "Steam pipe", new Vector3(.18f, .27f, .19f), new Vector3(.72f, .27f, .19f), .055f, copper);
            Pipe(parent, "Steam riser", new Vector3(.72f, .27f, .19f), new Vector3(.72f, .62f, .19f), .055f, copper);
            if (stage == 2) return;

            // An open-front turbine hall keeps the working rotor readable after completion.
            Box(parent, "Generator hall rear wall", new Vector3(.82f, .52f, .08f), new Vector3(.49f, .35f, .74f), brick);
            Box(parent, "Generator hall side wall", new Vector3(.08f, .52f, .66f), new Vector3(.12f, .35f, .41f), brick);
            Box(parent, "Generator hall side wall", new Vector3(.08f, .52f, .66f), new Vector3(.86f, .35f, .41f), brick);
            Box(parent, "Generator hall roof", new Vector3(.91f, .09f, .79f), new Vector3(.49f, .65f, .43f), roof);
            for (int i = 0; i < 3; i++)
                Box(parent, "Generator hall window", new Vector3(.14f, .14f, .018f),
                    new Vector3(.28f + i * .21f, .39f, .066f), glass);
            Box(parent, "Control annex", new Vector3(.50f, .32f, .42f), new Vector3(1.38f, .22f, .50f), ivory);
            Box(parent, "Control annex roof", new Vector3(.56f, .06f, .48f), new Vector3(1.38f, .41f, .50f), darkSteel);
            for (int i = 0; i < 3; i++)
                Box(parent, "Transformer coil", new Vector3(.08f, .22f, .08f),
                    new Vector3(1.24f + i * .14f, .18f, 1.22f), copper);
        }

        void BuildResearchCampus(Transform parent, int stage)
        {
            Vector3 centre = new Vector3(Plot * .5f, 0f, Plot * .5f);
            Box(parent, "Campus foundation", new Vector3(Plot * 1.92f, .065f, Plot * 1.92f),
                centre + Vector3.up * .0325f, paleConcrete);
            if (stage < 3)
                Outline(parent, "Campus blueprint footprint", centre + Vector3.up * .07f,
                    new Vector2(Plot * 2f, Plot * 2f), blueprint);
            for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
                Box(parent, "Campus footing", new Vector3(.20f, .12f, .20f),
                    new Vector3(.20f + x * .60f, .10f, .20f + z * .60f), concrete);
            if (stage == 0) return;

            Box(parent, "Library structural frame", new Vector3(.82f, .48f, .67f),
                new Vector3(.51f, .31f, .52f), steel);
            for (int i = 0; i < 4; i++)
                Box(parent, "Library column", new Vector3(.055f, .56f, .055f),
                    new Vector3(.20f + (i % 2) * .62f, .34f, .23f + (i / 2) * .58f), paleConcrete);
            Box(parent, "Workshop frame", new Vector3(.50f, .32f, .52f),
                new Vector3(1.37f, .23f, .51f), steel);
            if (stage == 1) return;

            Ball(parent, "Library dome", new Vector3(.62f, .38f, .62f),
                new Vector3(.51f, .73f, .52f), copper);
            Cyl(parent, "Dome lantern", .09f, .12f, .22f, new Vector3(.51f, 1.00f, .52f),
                Vector3.zero, ivory, 12);
            for (int i = -1; i <= 1; i++)
                Box(parent, "Workshop skylight", new Vector3(.13f, .035f, .26f),
                    new Vector3(1.37f + i * .15f, .43f, .51f), glass);
            for (int i = 0; i < 3; i++)
                Cyl(parent, "Research instrument", .035f, .055f, .24f,
                    new Vector3(1.18f + i * .18f, .18f, 1.27f), Vector3.zero, brass, 10);
            if (stage == 2) return;

            Box(parent, "Central library", new Vector3(.86f, .54f, .71f),
                new Vector3(.51f, .34f, .52f), ivory);
            Box(parent, "Research workshop", new Vector3(.55f, .37f, .57f),
                new Vector3(1.37f, .25f, .51f), brick);
            Box(parent, "Workshop roof", new Vector3(.61f, .07f, .63f),
                new Vector3(1.37f, .47f, .51f), roof);
            Box(parent, "Library entrance", new Vector3(.25f, .34f, .10f),
                new Vector3(.51f, .24f, .12f), darkSteel);
            for (int i = -1; i <= 1; i += 2)
                Box(parent, "Library window", new Vector3(.16f, .17f, .018f),
                    new Vector3(.51f + i * .25f, .38f, .155f), glass);
            Box(parent, "Campus lawn", new Vector3(.56f, .018f, .46f),
                new Vector3(.48f, .082f, 1.40f), lawn);
        }

        void CreateCaption(ProjectFigure figure, float height)
        {
            CityProjectSpec spec = CityProjects.Get(figure.State.Kind);
            float centerX = spec == null ? 0f : (spec.Width - 1) * Plot * .5f;
            float centerZ = spec == null ? 0f : (spec.Height - 1) * Plot * .5f;
            figure.CaptionShadow = Text(figure.Root.transform, "Project caption shadow",
                new Vector3(centerX, height - .008f, centerZ + .008f), new Color(.03f, .05f, .07f, .92f));
            figure.Caption = Text(figure.Root.transform, "Project status caption",
                new Vector3(centerX, height, centerZ), new Color(.98f, .94f, .82f, 1f));
        }

        TextMesh Text(Transform parent, string name, Vector3 position, Color color)
        {
            var value = new GameObject(name);
            value.transform.SetParent(parent, false);
            value.transform.localPosition = position;
            TextMesh text = value.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 28;
            text.characterSize = .055f;
            text.color = color;
            Font font = GameFont.Load();
            text.font = font;
            MeshRenderer renderer = text.GetComponent<MeshRenderer>();
            if (font != null && font.material != null) renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return text;
        }

        void UpdateCaption(ProjectFigure figure)
        {
            if (figure.Caption == null || figure.State == null) return;
            string value = CaptionText(figure.State);
            if (!string.Equals(figure.Caption.text, value, StringComparison.Ordinal)) figure.Caption.text = value;
            if (figure.CaptionShadow != null && !string.Equals(figure.CaptionShadow.text, value, StringComparison.Ordinal))
                figure.CaptionShadow.text = value;
        }

        static string CaptionText(CityProjectState state)
        {
            CityProjectSpec spec = CityProjects.Get(state.Kind);
            string name = spec == null || string.IsNullOrEmpty(spec.Name) ? state.Kind.ToString() : spec.Name;
            if (state.Stage >= 3) return name + "\n완공";

            string stageName = "단계 " + (state.Stage + 1);
            if (spec != null && spec.Stages != null && state.Stage >= 0 && state.Stage < spec.Stages.Length &&
                spec.Stages[state.Stage] != null && !string.IsNullOrEmpty(spec.Stages[state.Stage].Name))
                stageName = spec.Stages[state.Stage].Name;
            if (state.DaysRemaining > 0) return name + " · " + stageName + "\n공사 " + state.DaysRemaining + "일 남음";

            string delivery = DeliveryText(state, spec);
            return name + " · " + stageName + "\n" + delivery;
        }

        static string DeliveryText(CityProjectState state, CityProjectSpec spec)
        {
            if (spec == null || spec.Stages == null || state.Stage < 0 || state.Stage >= spec.Stages.Length ||
                spec.Stages[state.Stage] == null || spec.Stages[state.Stage].Requirements == null)
                return "자재 납품 대기";
            RecipeAmount[] requirements = spec.Stages[state.Stage].Requirements;
            var result = new StringBuilder("납품 ");
            int shown = 0;
            for (int i = 0; i < requirements.Length; i++)
            {
                RecipeAmount requirement = requirements[i];
                int delivered = state.Delivered != null && (int)requirement.Resource >= 0 &&
                    (int)requirement.Resource < state.Delivered.Count ? state.Delivered[(int)requirement.Resource] : 0;
                int remaining = Mathf.Max(0, requirement.Amount - delivered);
                if (remaining == 0) continue;
                if (shown > 0) result.Append(" · ");
                ResourceSpec resource = ResourceCatalog.Get(requirement.Resource);
                result.Append(resource == null ? requirement.Resource.ToString() : resource.Name);
                result.Append(' ');
                result.Append(remaining);
                shown++;
            }
            return shown == 0 ? "자재 납품 완료" : result.ToString();
        }

        void UpdateBillboards()
        {
            Camera camera = controller != null && controller.CameraRig != null ? controller.CameraRig.Camera : null;
            bool show = camera != null && camera.orthographicSize <= CaptionZoom;
            foreach (ProjectFigure figure in figures.Values)
            {
                SetCaptionVisible(figure.Caption, show, camera);
                SetCaptionVisible(figure.CaptionShadow, show, camera);
            }
        }

        static void SetCaptionVisible(TextMesh text, bool show, Camera camera)
        {
            if (text == null) return;
            if (text.gameObject.activeSelf != show) text.gameObject.SetActive(show);
            if (show) text.transform.rotation = camera.transform.rotation;
        }

        static float CaptionHeight(CityProjectKind kind)
        {
            switch (kind)
            {
                case CityProjectKind.GrandBridge: return 1.28f;
                case CityProjectKind.CentralPowerPlant: return 1.36f;
                case CityProjectKind.ResearchCampus: return 1.26f;
                default: return .8f;
            }
        }

        void CreatePalette()
        {
            concrete = Mat("Project concrete", new Color(.43f, .47f, .49f), .10f);
            paleConcrete = Mat("Project pale concrete", new Color(.73f, .73f, .67f), .12f);
            steel = Mat("Project structural steel", new Color(.38f, .50f, .57f), .28f, .35f);
            darkSteel = Mat("Project dark steel", new Color(.12f, .18f, .22f), .30f, .45f);
            copper = Mat("Project copper", new Color(.66f, .31f, .16f), .35f, .45f);
            brass = Mat("Project brass", new Color(.85f, .61f, .20f), .40f, .50f);
            glass = Mat("Project glass", new Color(.22f, .55f, .62f), .62f, .08f);
            brick = Mat("Project brick", new Color(.50f, .17f, .12f), .10f);
            ivory = Mat("Project ivory", new Color(.88f, .82f, .68f), .14f);
            roof = Mat("Project slate roof", new Color(.17f, .22f, .25f), .18f);
            lawn = Mat("Project campus lawn", new Color(.28f, .53f, .35f), .08f);
            blueprint = Mat("Project blueprint", new Color(.16f, .75f, .88f), .05f, 0f, true);
            cable = Mat("Project bridge cable", new Color(.12f, .14f, .16f), .28f, .55f);
            energy = Mat("Project electric light", new Color(1f, .81f, .25f), .05f, 0f, true);
            validPlacement = Mat("Valid project placement", new Color(.17f, .98f, .77f), .05f, 0f, true);
            invalidPlacement = Mat("Invalid project placement", new Color(1f, .32f, .26f), .05f, 0f, true);
        }

        void EnsurePlacement()
        {
            if (placementRoot != null) return;
            placementRoot = new GameObject("City project placement footprint").transform;
            placementRoot.SetParent(transform, false);
            placementLine = placementRoot.gameObject.AddComponent<LineRenderer>();
            placementLine.useWorldSpace = false;
            placementLine.positionCount = 5;
            placementLine.widthMultiplier = .065f;
            placementLine.numCornerVertices = 2;
            placementLine.shadowCastingMode = ShadowCastingMode.Off;
            placementLine.receiveShadows = false;
        }

        Material Mat(string name, Color color, float smooth, float metallic = 0f, bool unlit = false)
        {
            Shader shader = Shader.Find(unlit ? "Unlit/Color" : "Standard");
            var material = new Material(shader) { name = name, color = color };
            if (!unlit)
            {
                material.SetFloat("_Glossiness", smooth);
                material.SetFloat("_Metallic", metallic);
            }
            ownedMaterials.Add(material);
            return material;
        }

        static GameObject Box(Transform parent, string name, Vector3 scale, Vector3 position, Material material)
            => Primitive(parent, PrimitiveType.Cube, name, scale, position, material);

        static GameObject Ball(Transform parent, string name, Vector3 scale, Vector3 position, Material material)
            => Primitive(parent, PrimitiveType.Sphere, name, scale, position, material);

        static GameObject Cyl(Transform parent, string name, float top, float bottom, float height,
            Vector3 position, Vector3 rotation, Material material, int ignoredSegments)
        {
            GameObject value = Primitive(parent, PrimitiveType.Cylinder, name,
                new Vector3(Mathf.Max(top, bottom) * 2f, height * .5f, Mathf.Max(top, bottom) * 2f), position, material);
            value.transform.localEulerAngles = rotation;
            return value;
        }

        static GameObject Primitive(Transform parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 position, Material material)
        {
            GameObject value = GameObject.CreatePrimitive(type);
            value.name = name;
            value.transform.SetParent(parent, false);
            value.transform.localScale = scale;
            value.transform.localPosition = position;
            Renderer renderer = value.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            Collider collider = value.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            return value;
        }

        static void Pipe(Transform parent, string name, Vector3 from, Vector3 to, float radius, Material material)
        {
            Vector3 delta = to - from;
            GameObject pipe = Primitive(parent, PrimitiveType.Cylinder, name,
                new Vector3(radius * 2f, delta.magnitude * .5f, radius * 2f), (from + to) * .5f, material);
            if (delta.sqrMagnitude > .000001f) pipe.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta);
        }

        static void Outline(Transform parent, string name, Vector3 center, Vector2 size, Material material)
        {
            Vector3 half = new Vector3(size.x * .5f, 0f, size.y * .5f);
            Polyline(parent, name, new[]
            {
                center + new Vector3(-half.x, 0f, -half.z), center + new Vector3(-half.x, 0f, half.z),
                center + new Vector3(half.x, 0f, half.z), center + new Vector3(half.x, 0f, -half.z),
                center + new Vector3(-half.x, 0f, -half.z)
            }, .028f, material);
        }

        static void Line(Transform parent, string name, Vector3 from, Vector3 to, float width, Material material)
            => Polyline(parent, name, new[] { from, to }, width, material);

        static void Polyline(Transform parent, string name, Vector3[] points, float width, Material material)
        {
            var value = new GameObject(name);
            value.transform.SetParent(parent, false);
            LineRenderer line = value.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.widthMultiplier = width;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        static void DestroyFigure(ProjectFigure figure)
        {
            if (figure == null || figure.Root == null) return;
            figure.Root.SetActive(false);
            Destroy(figure.Root);
        }

        void OnDestroy()
        {
            foreach (Material material in ownedMaterials)
                if (material != null) Destroy(material);
            ownedMaterials.Clear();
            figures.Clear();
        }
    }
}

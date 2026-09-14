using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Opt-in native verification of the research graph, map interaction, and one real research start.</summary>
    public sealed class ResearchTreeSmokeTest : MonoBehaviour
    {
        const string SmokeArgument = "-riverworks-research-tree-smoke";
        const string ResultFile = "research-tree-results.txt";
        const int MaximumErrors = 64;
        const int MaximumErrorCharacters = 4096;

        static readonly Vector2Int[] Viewports =
        {
            new Vector2Int(1600, 900), new Vector2Int(1280, 720),
            new Vector2Int(1024, 768), new Vector2Int(2048, 1536)
        };

        GameController game;
        ResearchTreeView tree;
        Canvas canvas;
        UiSmokeViewport viewport;
        string output;
        bool finished;
        int suppressedErrors;
        int captures;
        int oldWidth;
        int oldHeight;
        int oldTargetFrameRate;
        int oldVsync;
        float oldTimeScale;
        FullScreenMode oldFullScreenMode;
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();

        public void Initialize(GameController controller)
        {
            game = controller;
            string[] arguments = Environment.GetCommandLineArgs();
            int outputAt = Array.IndexOf(arguments, "-riverworks-output");
            output = outputAt >= 0 && outputAt + 1 < arguments.Length
                ? arguments[outputAt + 1]
                : Path.Combine(Application.persistentDataPath, "ResearchTreeSmoke");
            Directory.CreateDirectory(output);
            oldWidth = Screen.width;
            oldHeight = Screen.height;
            oldFullScreenMode = Screen.fullScreenMode;
            oldTargetFrameRate = Application.targetFrameRate;
            oldVsync = QualitySettings.vSyncCount;
            oldTimeScale = Time.timeScale;
            Application.logMessageReceived += OnLog;
            StartCoroutine(GuardedRun());
        }

        void OnDestroy()
        {
            DisposeViewport();
            RestoreGlobals();
            Application.logMessageReceived -= OnLog;
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                RecordError((message ?? "") + "\n" + (trace ?? ""));
        }

        IEnumerator GuardedRun()
        {
            yield return SmokeExecution.Run(RunSteps(), exception => RecordError(exception.ToString()), 180f);
            Finish();
        }

        IEnumerator RunSteps()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            Require(Array.IndexOf(arguments, SmokeArgument) >= 0,
                "research tree smoke only runs behind its explicit command flag");
            Require(game != null && game.SmokeMode && game.CityHud != null && game.CameraRig != null,
                "controller initialized the HUD and camera in isolated smoke mode");
            Require(!string.Equals(Path.GetFileName(game.SavePath), "city-v1.json", StringComparison.OrdinalIgnoreCase),
                "controlled research fixture cannot overwrite the player's city save");

            game.SetSpeed(0f);
            game.ResidentAi?.SetEnabled(false);
            game.Tutorial?.SkipIntro();
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.3f);
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.4f);

            GameState fixture = CreateResearchFixture();
            Require(SaveStore.TrySave(game.SavePath, fixture, out string saveError),
                "controlled research fixture saves only to the isolated smoke slot: " + saveError);
            game.LoadGame();
            game.SetSpeed(0f);
            game.RefreshWorld(true);
            yield return null;

            if (!game.ResearchOpen) game.ToggleResearch();
            yield return null;
            Canvas.ForceUpdateCanvases();
            tree = game.CityHud.ResearchTree;
            canvas = game.CityHud.GetComponent<Canvas>();
            Require(tree != null && canvas != null && tree.gameObject.activeInHierarchy,
                "research tree opens through the real HUD and exposes its render canvas");

            VerifyGraphAndLayout();
            VerifyCatalogAndPlanContracts();
            yield return VerifySearchFiltersAndNavigation();

            foreach (Vector2Int size in Viewports)
                yield return VerifyViewport(size.x, size.y);

            yield return VerifyRealResearchStart();
            Require(captures == Viewports.Length * 4,
                "four exact research views were captured at each of four viewports");
        }

        static GameState CreateResearchFixture()
        {
            GameState state = GameState.CreateNew();
            state.Technologies.Clear();
            state.ActiveResearch = TechId.None;
            state.ResearchDaysRemaining = 0;
            state.ResearchPoints = 1000f;
            state.Coins = 10000f;
            state.Stock[(int)Resource.Coins] = state.Coins;
            state.Population = Math.Max(state.Population, 60);
            return state;
        }

        void VerifyGraphAndLayout()
        {
            ResearchGraph graph = tree.Graph;
            ResearchTreeLayout layout = tree.Layout;
            Require(graph != null && layout != null, "tree exposes its immutable graph and deterministic layout");
            Require(graph.OrderedTechnologies.Count == 18, "research graph contains exactly 18 technologies");
            Require(graph.Edges.Count == 19 && graph.Edges.Distinct().Count() == 19,
                "research graph contains exactly 19 unique prerequisite edges");
            Require(tree.Nodes.Count == 18 && tree.Connections.Count == 19,
                "rendered node and connection dictionaries match the graph exactly");
            Require(tree.GraphViewport != null && tree.GraphViewport.name == "ResearchGraphViewport" &&
                    tree.GraphContent != null && tree.GraphContent.name == "ResearchGraphContent" && tree.TreeScroll != null,
                "scene has the named masked graph viewport, graph content, and ScrollRect");
            Require(layout.ContentSize.x > 0 && layout.ContentSize.y > 0,
                "layout content has positive width and height in positive-Y-down coordinates");

            foreach (TechSpec spec in graph.OrderedTechnologies)
            {
                Rect rect = layout.NodeRect(spec.Id);
                Require(rect.width == 224f && rect.height == 156f && rect.xMin >= 0 && rect.yMin >= 0 &&
                        rect.xMax <= layout.ContentSize.x + .1f && rect.yMax <= layout.ContentSize.y + .1f,
                    spec.Id + " has a 224x156 positive-Y-down rect inside content bounds");
                Require(tree.Nodes.TryGetValue(spec.Id, out RectTransform root) && root != null &&
                        tree.NodeFor(spec.Id) == root &&
                        root.name == "ResearchNode_" + spec.Id && Mathf.Abs(root.rect.width - 224f) < .6f &&
                        Mathf.Abs(root.rect.height - 156f) < .6f && root.GetComponent<Button>() != null &&
                        root.GetComponent<Button>().interactable,
                    spec.Id + " renders as a full selectable research node");
                Button start = tree.StartButtonFor(spec.Id);
                Require(start != null && start.name == "Button_연구 시작_" + spec.Id &&
                        Mathf.Abs(((RectTransform)start.transform).rect.width - 208f) < .6f &&
                        Mathf.Abs(((RectTransform)start.transform).rect.height - 44f) < .6f,
                    spec.Id + " keeps a stable 208x44 start action");
            }

            foreach (ResearchEdge edge in graph.Edges)
            {
                IReadOnlyList<Vector2> points = layout.EdgePoints(edge);
                Require(points.Count >= 2 && points.All(point => point.x >= -.1f && point.y >= -.1f &&
                        point.x <= layout.ContentSize.x + .1f && point.y <= layout.ContentSize.y + .1f),
                    edge.Source + " -> " + edge.Target + " stays inside graph content");
                foreach (TechSpec other in graph.OrderedTechnologies)
                {
                    if (other.Id == edge.Source || other.Id == edge.Target) continue;
                    Rect interior = Inset(layout.NodeRect(other.Id), 1f);
                    for (int index = 0; index < points.Count - 1; index++)
                        Require(!SegmentIntersectsRect(points[index], points[index + 1], interior),
                            edge.Source + " -> " + edge.Target + " avoids the interior of " + other.Id);
                }
                Require(tree.Connections.TryGetValue(edge, out ResearchConnectionGraphic line) && line != null &&
                        line.Points.Count == points.Count && line.StrokeWidth > 0f && !line.raycastTarget,
                    edge.Source + " -> " + edge.Target + " renders with a non-raycast connection graphic");
            }
            Require(tree.VerifyLayout(out string reason), "ResearchTreeView.VerifyLayout passes: " + reason);
        }

        void VerifyCatalogAndPlanContracts()
        {
            ResearchGraph graph = tree.Graph;
            TechSpec guilds = TechCatalog.Get(TechId.Guilds);
            Require(guilds.Prerequisites.Length == 2 && guilds.Prerequisites.Contains(TechId.Stonecraft) &&
                    guilds.Prerequisites.Contains(TechId.MechanicalPower),
                "Guilds retains both direct prerequisites as an AND requirement");
            IReadOnlyList<TechId> automationPlan = graph.PlanTo(TechId.Automation, Array.Empty<TechId>());
            Require(automationPlan.Count == automationPlan.Distinct().Count() && automationPlan.Last() == TechId.Automation &&
                    graph.Ancestors(TechId.Automation).All(automationPlan.Contains),
                "Automation plan contains every ancestor once and ends at Automation");
            ResearchPlanSummary summary = graph.PlanSummaryTo(TechId.Automation, Array.Empty<TechId>());
            Require(summary.Technologies.SequenceEqual(automationPlan) && summary.KnowledgeCost > 0 &&
                    summary.CoinCost > 0 && summary.DurationDays > 0,
                "Automation plan summary totals upfront knowledge, coins, and duration");
            Require(FactoryCatalog.Get(FactoryKind.Belt).RequiredTech == TechId.MechanicalPower &&
                    FactoryCatalog.Get(FactoryKind.Inserter).RequiredTech == TechId.MechanicalPower &&
                    FactoryCatalog.Get(FactoryKind.Splitter).RequiredTech == TechId.MechanicalPower,
                "Mechanical Power actually unlocks Belt, Inserter, and Splitter factory equipment");

            tree.FocusTechnology(TechId.MechanicalPower);
            Canvas.ForceUpdateCanvases();
            Require(tree.Details != null && tree.Details.SelectedTechnology == TechId.MechanicalPower &&
                    tree.Details.Scroll != null && tree.Details.Scroll.viewport != null,
                "detail panel selects Mechanical Power and exposes its scroll viewport");
            Text detailText = tree.Details.GetComponentsInChildren<Text>(true)
                .FirstOrDefault(label => label.text != null && label.text.Contains(TechCatalog.Get(TechId.MechanicalPower).Name));
            Require(detailText != null, "detail panel renders the selected technology's catalog data");
            string renderedDetails = string.Join("\n", tree.Details.GetComponentsInChildren<Text>(true)
                .Select(label => label.text ?? ""));
            TechSpec mechanical = TechCatalog.Get(TechId.MechanicalPower);
            Require(renderedDetails.Contains(mechanical.ResearchCost.ToString()) &&
                    renderedDetails.Contains(mechanical.CoinCost.ToString()) &&
                    renderedDetails.Contains(mechanical.DurationDays.ToString()),
                "detail renders Mechanical Power knowledge, coin, and duration values from the catalog");
            Require(new[] { FactoryKind.Belt, FactoryKind.Inserter, FactoryKind.Splitter }
                    .Select(kind => FactoryCatalog.Get(kind).Name).All(renderedDetails.Contains),
                "Mechanical Power detail lists its actual Belt, Inserter, and Splitter unlocks");
        }

        IEnumerator VerifySearchFiltersAndNavigation()
        {
            tree.FocusTechnology(TechId.Guilds);
            Canvas.ForceUpdateCanvases();
            float normalStroke = tree.Connections.Values.Min(line => line.StrokeWidth);
            ResearchEdge[] selectedEdges = tree.Graph.Edges.Where(edge => edge.Target == TechId.Guilds).ToArray();
            Require(selectedEdges.Length == 2 && selectedEdges.All(edge => tree.Connections[edge].StrokeWidth > normalStroke),
                "Guilds selection highlights both incoming prerequisite lines above normal stroke width");

            tree.SetSearch(TechCatalog.Get(TechId.Guilds).Name);
            Require(tree.MatchingCount >= 1, "Korean technology-name search finds Guilds");
            tree.SetSearch("Automation");
            Require(tree.MatchingCount >= 1, "English technology-ID search finds Automation");
            tree.SetSearch("__riverworks_no_research_result__");
            Require(tree.MatchingCount == 0, "unmatched research search reports zero results");
            InputField searchInput = game.CityHud.GetComponentsInChildren<InputField>(true)
                .FirstOrDefault(input => input.name == "Input_ResearchSearch");
            Require(searchInput != null, "research tree exposes the stable search input");
            searchInput.ActivateInputField();
            yield return null;
            Require(game.UiTextInputFocused,
                "activating the real search InputField suppresses gameplay shortcuts (no synthetic text entry)");
            searchInput.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            Require(!game.UiTextInputFocused, "deactivating search restores gameplay shortcut eligibility");
            tree.ResetView();
            Require(tree.MatchingCount == 18 && tree.ZoomLevel == 1,
                "ResetView clears search and restores overview zoom");

            tree.FocusTechnology(TechId.Guilds);
            TechId selected = tree.SelectedTechnology;
            foreach (ResearchFilter filter in new[] { ResearchFilter.Available, ResearchFilter.Unfinished, ResearchFilter.All })
            {
                tree.SetFilter(filter);
                Canvas.ForceUpdateCanvases();
                Require(tree.SelectedTechnology == selected && tree.Nodes.Count == 18,
                    filter + " filter preserves selection and graph context");
            }

            foreach (TechSpec spec in tree.Graph.OrderedTechnologies)
            {
                tree.FocusTechnology(spec.Id);
                Canvas.ForceUpdateCanvases();
                Button nodeButton = tree.Nodes[spec.Id].GetComponent<Button>();
                Require(nodeButton != null && IsFirstRaycastTarget(nodeButton),
                    spec.Id + " can be focused, scrolled into view, and reached by EventSystem raycast");
            }

            tree.SetZoom(99);
            Require(tree.ZoomLevel == 2, "zoom clamps above its supported range to 2x");
            tree.SetZoom(-99);
            Require(tree.ZoomLevel == 1, "zoom clamps below its supported range to 1x");
            tree.SetZoom(2);
            tree.FocusTechnology(TechId.Automation);
            Canvas.ForceUpdateCanvases();
            Require(IsFirstRaycastTarget(tree.Nodes[TechId.Automation].GetComponent<Button>()),
                "2x panning clamps around Automation while keeping its node pointer-reachable");

            tree.ResetView();
            foreach (Era era in new[] { Era.Medieval, Era.Renaissance, Era.Industrial })
            {
                Click(FindButton("Button_ResearchEra_" + era));
                yield return null;
                Require(tree.SelectedTechnology != TechId.None && TechCatalog.Get(tree.SelectedTechnology).Era == era,
                    era + " era button navigates into that era");
            }
            Click(FindButton("Button_ResearchZoomIn"));
            Require(tree.ZoomLevel == 2, "zoom-in button reaches 2x through EventSystem");
            Click(FindButton("Button_ResearchZoomOut"));
            Require(tree.ZoomLevel == 1, "zoom-out button returns to 1x through EventSystem");
            Click(FindButton("Button_ResearchReset"));
            Require(tree.ZoomLevel == 1 && tree.MatchingCount == 18, "reset button restores the complete overview");
            VerifyMiniMapPointerNavigation();
        }

        IEnumerator VerifyViewport(int width, int height)
        {
            DisposeViewport();
            viewport = new UiSmokeViewport(game.CameraRig.Camera, canvas, width, height);
            yield return null;
            Canvas.ForceUpdateCanvases();
            string prefix = width + "x" + height;
            Require(tree.VerifyLayout(out string overviewReason), prefix + " overview layout passes: " + overviewReason);
            tree.ResetView();
            yield return Capture(prefix + "-01-overview.png");

            tree.FocusTechnology(TechId.Guilds);
            yield return Capture(prefix + "-02-guilds-selected-path.png");
            tree.FocusTechnology(TechId.Automation);
            yield return Capture(prefix + "-03-automation.png");
            tree.SetZoom(2);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Require(IsFirstRaycastTarget(tree.Nodes[TechId.Automation].GetComponent<Button>()),
                prefix + " keeps selected Automation visible and pointer-reachable immediately after 2x zoom");
            tree.SetSearch(width == 1024 ? TechCatalog.Get(TechId.Guilds).Name : "Automation");
            yield return Capture(prefix + "-04-zoom2-search.png");
            Require(tree.VerifyLayout(out string finalReason), prefix + " zoom/search layout passes: " + finalReason);
            VerifyDetailTextFits(prefix);
            tree.ResetView();
        }

        void VerifyDetailTextFits(string viewportName)
        {
            Text[] labels = tree.Details.GetComponentsInChildren<Text>(false);
            Require(labels.Length > 4 && labels.All(label =>
                    label.rectTransform.rect.height + .75f >= label.preferredHeight),
                viewportName + " keeps every active detail label's preferred height inside its own rect, including scroll content");
        }

        void VerifyMiniMapPointerNavigation()
        {
            ResearchMiniMap miniMap = game.CityHud.GetComponentInChildren<ResearchMiniMap>(true);
            Require(miniMap != null && miniMap.raycastTarget && miniMap.gameObject.activeInHierarchy,
                "research minimap is an active raycast graphic");
            tree.SetZoom(2);
            tree.FocusTechnology(TechId.CropRotation);
            Canvas.ForceUpdateCanvases();
            Vector2 before = tree.GraphCenter;
            RectTransform rect = miniMap.rectTransform;
            Rect frame = rect.rect;
            float scale = Mathf.Min((frame.width - 12f) / tree.Layout.ContentSize.x,
                (frame.height - 12f) / tree.Layout.ContentSize.y);
            Vector2 graphPoint = tree.Layout.NodeRect(TechId.Automation).center;
            Vector2 local = new Vector2(frame.xMin + 6f + graphPoint.x * scale,
                frame.yMax - 6f - graphPoint.y * scale);
            Canvas owner = miniMap.GetComponentInParent<Canvas>();
            Camera eventCamera = owner != null && owner.renderMode != RenderMode.ScreenSpaceOverlay ? owner.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(eventCamera, rect.TransformPoint(local));
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = screen,
                button = PointerEventData.InputButton.Left,
                pointerId = -1
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && (hits[0].gameObject == miniMap.gameObject ||
                    hits[0].gameObject.transform.IsChildOf(miniMap.transform)),
                "Automation marker on the minimap is the first EventSystem raycast target");
            pointer.pointerPressRaycast = hits[0];
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Canvas.ForceUpdateCanvases();
            Vector2 after = tree.GraphCenter;
            Require((after - before).sqrMagnitude > 100f &&
                    (after - graphPoint).sqrMagnitude < (before - graphPoint).sqrMagnitude,
                "real minimap pointer click pans the graph toward the Automation marker");
            tree.ResetView();
        }

        IEnumerator VerifyRealResearchStart()
        {
            tree.ResetView();
            tree.FocusTechnology(TechId.MechanicalPower);
            Canvas.ForceUpdateCanvases();
            Button locked = tree.StartButtonFor(TechId.Guilds);
            Require(locked != null && !locked.interactable,
                "start rules protect Guilds while either prerequisite is missing");

            game.State.Technologies.Add(TechId.CropRotation);
            game.NotifyWorldSelection();
            yield return null;
            Require(tree.Nodes[TechId.CropRotation].GetComponent<Button>().interactable,
                "a completed technology remains selectable as a full research node");
            tree.FocusTechnology(TechId.MechanicalPower);
            Canvas.ForceUpdateCanvases();
            TechSpec spec = TechCatalog.Get(TechId.MechanicalPower);
            float knowledgeBefore = game.State.ResearchPoints;
            float coinsBefore = game.State.Coins;
            Button detailStart = FindButton("Button_ResearchDetailStart");
            Require(detailStart != null && detailStart.interactable &&
                    Mathf.Abs(((RectTransform)detailStart.transform).rect.height - 44f) < .6f,
                "detail panel exposes a real 44 pixel start action when prerequisites and funds allow it");
            Click(detailStart);
            yield return null;
            Require(game.State.ActiveResearch == TechId.MechanicalPower &&
                    game.State.ResearchDaysRemaining == spec.DurationDays &&
                    Mathf.Approximately(game.State.ResearchPoints, knowledgeBefore - spec.ResearchCost) &&
                    Mathf.Approximately(game.State.Coins, coinsBefore - spec.CoinCost),
                "real GameController starts Mechanical Power and deducts knowledge and coins once upfront");
            Require(!game.ResearchOpen, "successful real research start closes the research surface");
            float chargedKnowledge = game.State.ResearchPoints;
            float chargedCoins = game.State.Coins;
            game.SetSpeed(0f);
            yield return new WaitForSecondsRealtime(.15f);
            Require(Mathf.Approximately(game.State.ResearchPoints, chargedKnowledge) &&
                    Mathf.Approximately(game.State.Coins, chargedCoins),
                "research duration does not charge knowledge or coins as a per-time rate");

            game.ToggleResearch();
            yield return null;
            tree = game.CityHud.ResearchTree;
            Click(FindButton("Button_ResearchCurrent"));
            Require(tree.SelectedTechnology == TechId.MechanicalPower,
                "current-research button returns to the active Mechanical Power node");
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.3f);
            yield return null;
            Texture2D texture = null;
            try
            {
                texture = viewport.Capture();
                Require(texture != null && texture.width == viewport.Width && texture.height == viewport.Height,
                    name + " renders at its exact requested viewport size");
                File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
                results.Add("CAPTURE " + name + " " + texture.width + "x" + texture.height);
                captures++;
            }
            finally { if (texture != null) Destroy(texture); }
        }

        void Click(Button button)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
                throw new InvalidOperationException("UI button unavailable: " + (button == null ? "null" : button.name));
            Canvas.ForceUpdateCanvases();
            PointerEventData pointer = PointerAtButtonCenter(button);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || !IsButtonHit(button, hits[0].gameObject))
                throw new InvalidOperationException("UI button is not the first EventSystem raycast target: " + button.name +
                    (hits.Count == 0 ? " (no hits)" : " (hit " + hits[0].gameObject.name + ")"));
            GameObject hit = hits[0].gameObject;
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler);
            Check(true, "EventSystem raycast clicks " + button.name);
        }

        bool IsFirstRaycastTarget(Button button)
        {
            if (button == null || EventSystem.current == null || !button.gameObject.activeInHierarchy) return false;
            PointerEventData pointer;
            try { pointer = PointerAtButtonCenter(button); }
            catch { return false; }
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            return hits.Count > 0 && IsButtonHit(button, hits[0].gameObject);
        }

        static bool IsButtonHit(Button button, GameObject hit) =>
            hit == button.gameObject || hit.transform.IsChildOf(button.transform);

        static PointerEventData PointerAtButtonCenter(Button button)
        {
            if (EventSystem.current == null) throw new InvalidOperationException("EventSystem unavailable");
            RectTransform rect = button.transform as RectTransform;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Canvas owner = button.GetComponentInParent<Canvas>();
            Camera eventCamera = owner != null && owner.renderMode != RenderMode.ScreenSpaceOverlay ? owner.worldCamera : null;
            Vector2 center = RectTransformUtility.WorldToScreenPoint(eventCamera, (corners[0] + corners[2]) * .5f);
            return new PointerEventData(EventSystem.current)
            {
                position = center,
                button = PointerEventData.InputButton.Left,
                pointerId = -1
            };
        }

        Button FindButton(string name) => game?.CityHud == null ? null :
            game.CityHud.GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == name);

        static Rect Inset(Rect rect, float amount) =>
            Rect.MinMaxRect(rect.xMin + amount, rect.yMin + amount, rect.xMax - amount, rect.yMax - amount);

        static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect)
        {
            if (rect.Contains(a) || rect.Contains(b)) return true;
            return SegmentsIntersect(a, b, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin)) ||
                   SegmentsIntersect(a, b, new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax)) ||
                   SegmentsIntersect(a, b, new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax)) ||
                   SegmentsIntersect(a, b, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin));
        }

        static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a), abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
            return abC * abD <= 0f && cdA * cdB <= 0f;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        void Check(bool okay, string message)
        {
            if (okay)
            {
                results.Add("PASS " + message);
                Debug.Log("RESEARCH TREE PASS: " + message);
            }
            else RecordError("FAIL " + message);
        }

        void Require(bool okay, string message)
        {
            if (!okay) throw new InvalidOperationException(message);
            Check(true, message);
        }

        void RecordError(string message)
        {
            if (errors.Count >= MaximumErrors) { suppressedErrors++; return; }
            message = string.IsNullOrEmpty(message) ? "Unknown error" : message;
            if (message.Length > MaximumErrorCharacters)
                message = message.Substring(0, MaximumErrorCharacters) + "...[truncated]";
            errors.Add(message);
        }

        void DisposeViewport()
        {
            viewport?.Dispose();
            viewport = null;
        }

        void RestoreGlobals()
        {
            Time.timeScale = oldTimeScale;
            QualitySettings.vSyncCount = oldVsync;
            Application.targetFrameRate = oldTargetFrameRate;
            if (oldWidth > 0 && oldHeight > 0) Screen.SetResolution(oldWidth, oldHeight, oldFullScreenMode);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            game?.SetSpeed(0f);
            DisposeViewport();
            RestoreGlobals();
            Application.logMessageReceived -= OnLog;
            int exitCode = errors.Count == 0 && suppressedErrors == 0 ? 0 : 1;
            results.Insert(0, "RUN " + DateTime.UtcNow.ToString("O") + " BUILDGUID " + Application.buildGUID +
                " UNITY " + Application.unityVersion);
            results.Add("FIXTURE_UI_STATE controlled isolated research fixture; one real GameController research start");
            results.Add("ERRORS " + errors.Count);
            results.Add("SUPPRESSED_ERRORS " + suppressedErrors);
            results.AddRange(errors);
            results.Add("EXIT " + exitCode);
            try { File.WriteAllLines(Path.Combine(output, ResultFile), results); }
            catch (Exception exception)
            {
                Debug.LogError("Could not write " + ResultFile + ": " + exception.Message);
                exitCode = 1;
            }
            Debug.Log(exitCode == 0 ? "RIVERWORKS_RESEARCH_TREE_SMOKE_PASS" : "RIVERWORKS_RESEARCH_TREE_SMOKE_FAIL");
            Application.Quit(exitCode);
        }
    }
}

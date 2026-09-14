using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    public enum ResearchFilter { All, Available, Unfinished }

    /// <summary>A navigable map of the real research DAG; selecting a path never changes simulation state.</summary>
    public sealed class ResearchTreeView : MonoBehaviour
    {
        sealed class Node
        {
            public RectTransform Rect;
            public Image Surface, SelectionStripe, Progress;
            public Text Title, State, Costs;
            public Image StateSurface;
            public Button Start;
            public CanvasGroup Group;
        }

        readonly Dictionary<TechId, Node> nodeViews = new Dictionary<TechId, Node>();
        readonly Dictionary<TechId, RectTransform> nodes = new Dictionary<TechId, RectTransform>();
        readonly Dictionary<ResearchEdge, ResearchConnectionGraphic> connections = new Dictionary<ResearchEdge, ResearchConnectionGraphic>();
        readonly Dictionary<ResearchFilter, Button> filterButtons = new Dictionary<ResearchFilter, Button>();
        readonly Dictionary<Era, Button> eraButtons = new Dictionary<Era, Button>();
        readonly Dictionary<ResearchEdge, int> edgeStyles = new Dictionary<ResearchEdge, int>();
        readonly HashSet<TechId> selectedPath = new HashSet<TechId>();
        readonly List<TechId> matches = new List<TechId>();
        readonly Dictionary<TechId, string> searchableText = new Dictionary<TechId, string>();
        GameController controller;
        RectTransform root, toolbar, footer, detailDock;
        ResearchMiniMap miniMap;
        InputField searchInput;
        Text economy, zoomLabel, hint;
        Button previousZoom, nextZoom, nextMatch;
        GameState observedState;
        string stateSignature = "";
        Vector2 lastSize = new Vector2(-1, -1);
        bool initialized, refreshPending;
        int matchIndex = -1;
        string search = "";
        ResearchFilter filter;

        public ResearchGraph Graph { get; private set; }
        public ResearchTreeLayout Layout { get; private set; }
        public RectTransform GraphContent { get; private set; }
        public RectTransform GraphViewport { get; private set; }
        public ScrollRect TreeScroll { get; private set; }
        public ResearchDetailPanel Details { get; private set; }
        public TechId SelectedTechnology { get; private set; }
        public int ZoomLevel { get; private set; } = 1;
        public int MatchingCount => matches.Count;
        public string SearchQuery => search;
        public ResearchFilter Filter => filter;
        public IReadOnlyDictionary<TechId, RectTransform> Nodes => nodes;
        public IReadOnlyDictionary<ResearchEdge, ResearchConnectionGraphic> Connections => connections;
        public IReadOnlyCollection<TechId> SelectedPath => selectedPath;

        public void Initialize(GameController gameController)
        {
            if (initialized) throw new InvalidOperationException("Research tree is already initialized.");
            controller = gameController ?? throw new ArgumentNullException(nameof(gameController));
            root = transform as RectTransform;
            Graph = new ResearchGraph(TechCatalog.All);
            Layout = new ResearchTreeLayout(Graph);
            SelectedTechnology = Graph.OrderedTechnologies[0].Id;
            Build();
            initialized = true;
            ApplyLayout(true);
            Details.Initialize(controller, Graph, id => FocusTechnology(id), StartResearch);
            Refresh();
            ResetView();
        }

        void OnEnable()
        {
            if (initialized) StartCoroutine(SettleOpening());
        }

        IEnumerator SettleOpening()
        {
            yield return null;
            ApplyLayout(true);
            Refresh();
            if (controller.State.ActiveResearch != TechId.None) FocusTechnology(controller.State.ActiveResearch);
            else FocusTechnology(SelectedTechnology);
        }

        void OnDisable()
        {
            if (searchInput != null) searchInput.DeactivateInputField();
        }

        void LateUpdate()
        {
            if (!initialized) return;
            ApplyLayout(false);
            if (refreshPending) { refreshPending = false; Refresh(); }
        }

        void Build()
        {
            toolbar = ResearchUi.Rect("ResearchToolbar", root);
            searchInput = MakeSearch(toolbar);
            nextMatch = ToolbarButton("Button_ResearchSearchNext", "다음", 182, 44, NextSearchMatch);
            filterButtons[ResearchFilter.All] = ToolbarButton("Button_ResearchFilter_All", "전체", 232, 56, () => SetFilter(ResearchFilter.All));
            filterButtons[ResearchFilter.Available] = ToolbarButton("Button_ResearchFilter_Available", "연구 가능", 294, 76, () => SetFilter(ResearchFilter.Available));
            filterButtons[ResearchFilter.Unfinished] = ToolbarButton("Button_ResearchFilter_Unfinished", "미완료", 376, 70, () => SetFilter(ResearchFilter.Unfinished));
            ToolbarButton("Button_ResearchCurrent", "현재 연구", 452, 80, FocusCurrent);
            ToolbarButton("Button_ResearchReset", "처음으로", 538, 72, ResetView);
            previousZoom = ToolbarButton("Button_ResearchZoomOut", "−", 616, 44, () => SetZoom(ZoomLevel - 1));
            zoomLabel = ResearchUi.Label("ResearchZoomLabel", toolbar, "100%", 11, HudStyle.TextMuted, TextAnchor.MiddleCenter);
            ResearchUi.Place(zoomLabel.rectTransform, 664, 0, 48, 44);
            nextZoom = ToolbarButton("Button_ResearchZoomIn", "+", 716, 44, () => SetZoom(ZoomLevel + 1));
            economy = ResearchUi.Label("ResearchEconomy", toolbar, "", 11, HudStyle.Text, TextAnchor.MiddleRight);

            GraphViewport = ResearchUi.Panel("ResearchGraphViewport", root, HudStyle.Surface);
            GraphViewport.gameObject.AddComponent<RectMask2D>();
            var mapScroll = GraphViewport.gameObject.AddComponent<ResearchMapScrollRect>();
            mapScroll.Owner = this;
            TreeScroll = mapScroll;
            GraphContent = ResearchUi.Rect("ResearchGraphContent", GraphViewport);
            ResearchUi.Place(GraphContent, 0, 0, Layout.ContentSize.x, Layout.ContentSize.y);
            TreeScroll.viewport = GraphViewport;
            TreeScroll.content = GraphContent;
            TreeScroll.horizontal = TreeScroll.vertical = true;
            TreeScroll.movementType = ScrollRect.MovementType.Clamped;
            TreeScroll.inertia = true;
            TreeScroll.decelerationRate = .08f;
            TreeScroll.scrollSensitivity = 40;
            TreeScroll.onValueChanged.AddListener(_ => miniMap?.Repaint());

            BuildEraBands();
            foreach (ResearchEdge edge in Graph.Edges)
            {
                RectTransform lineRoot = ResearchUi.Rect("ResearchEdge_" + edge.Source + "_" + edge.Target, GraphContent);
                ResearchUi.Place(lineRoot, 0, 0, Layout.ContentSize.x, Layout.ContentSize.y);
                ResearchConnectionGraphic graphic = lineRoot.gameObject.AddComponent<ResearchConnectionGraphic>();
                graphic.SetPath(Layout.EdgePoints(edge), HudStyle.TextMuted, 2, true);
                connections.Add(edge, graphic);
            }
            foreach (TechSpec spec in Graph.OrderedTechnologies) BuildNode(spec);

            detailDock = ResearchUi.Rect("ResearchDetails", root);
            Details = detailDock.gameObject.AddComponent<ResearchDetailPanel>();
            footer = ResearchUi.Rect("ResearchNavigation", root);
            float eraX = 0;
            foreach (ResearchEraBand band in Layout.EraBands)
            {
                Era era = band.Era;
                Button button = ResearchUi.Button("Button_ResearchEra_" + era, footer,
                    TechCatalog.EraName(era), () => FocusEra(era));
                ResearchUi.Place(button.transform as RectTransform, eraX, 0, 90, 44);
                eraButtons.Add(era, button);
                eraX += 96;
            }
            hint = ResearchUi.Label("ResearchNavigationHint", footer, "", 11, HudStyle.TextMuted, TextAnchor.MiddleLeft);
            Color[] legendColors = { HudStyle.Positive, HudStyle.Accent, HudStyle.Text, HudStyle.TextMuted };
            string[] legendLabels = { "완료", "진행 / 경로", "가능", "선행 필요" };
            for (int i = 0; i < legendLabels.Length; i++)
            {
                RectTransform mark = ResearchUi.Panel("ResearchLegendMark_" + i, footer, legendColors[i]);
                ResearchUi.Place(mark, 306 + i * 90, 7, 10, 10);
                mark.GetComponent<Image>().raycastTarget = false;
                Text label = ResearchUi.Label("ResearchLegend_" + i, footer, legendLabels[i], 11, HudStyle.TextMuted);
                ResearchUi.Place(label.rectTransform, 322 + i * 90, 4, 76, 20);
            }
            RectTransform miniRoot = ResearchUi.Rect("ResearchMiniMap", GraphViewport);
            miniRoot.anchorMin = miniRoot.anchorMax = miniRoot.pivot = new Vector2(1, 0);
            miniRoot.anchoredPosition = new Vector2(-8, 8);
            miniRoot.sizeDelta = new Vector2(196, 76);
            miniMap = miniRoot.gameObject.AddComponent<ResearchMiniMap>();
            miniMap.Bind(this);
        }

        Button ToolbarButton(string name, string text, float x, float width, Action click)
        {
            Button button = ResearchUi.Button(name, toolbar, text, click);
            ResearchUi.Place(button.transform as RectTransform, x, 0, width, 44);
            return button;
        }

        InputField MakeSearch(Transform parent)
        {
            RectTransform fieldRoot = ResearchUi.Panel("Input_ResearchSearch", parent, HudStyle.Surface);
            ResearchUi.Place(fieldRoot, 0, 0, 176, 44);
            Text text = ResearchUi.Label("Text", fieldRoot, "", 11, HudStyle.Text, TextAnchor.MiddleLeft);
            ResearchUi.Stretch(text.rectTransform, 10, 2, 10, 2);
            Text placeholder = ResearchUi.Label("Placeholder", fieldRoot, "기술 이름·효과 검색", 11, HudStyle.TextMuted, TextAnchor.MiddleLeft);
            ResearchUi.Stretch(placeholder.rectTransform, 10, 2, 10, 2);
            InputField input = fieldRoot.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = fieldRoot.GetComponent<Image>();
            input.characterLimit = 60;
            input.lineType = InputField.LineType.SingleLine;
            input.onValueChanged.AddListener(value => { search = value.Trim(); matchIndex = -1; RefreshPresentation(); });
            input.onEndEdit.AddListener(_ => { if (isActiveAndEnabled && matches.Count > 0) NextSearchMatch(); });
            return input;
        }

        void BuildEraBands()
        {
            foreach (ResearchEraBand band in Layout.EraBands)
            {
                Color tint = Color.Lerp(HudStyle.Surface, HudStyle.SurfaceRaised, ((int)band.Era % 2) == 0 ? .22f : .42f);
                RectTransform region = ResearchUi.Panel("ResearchEraBand_" + band.Era, GraphContent, tint);
                ResearchUi.Place(region, band.MinX - 12, 0, band.MaxX - band.MinX + 24, Layout.ContentSize.y);
                region.GetComponent<Image>().raycastTarget = false;
                Text era = ResearchUi.Label("ResearchEraTitle_" + band.Era, region,
                    TechCatalog.EraName(band.Era), 22, HudStyle.Text);
                ResearchUi.Place(era.rectTransform, 12, 12, 220, 30);
                int total = Graph.OrderedTechnologies.Count(spec => spec.Era == band.Era);
                Text count = ResearchUi.Label("ResearchEraCount_" + band.Era, region,
                    total + "개의 기술", 11, HudStyle.TextMuted, TextAnchor.MiddleRight);
                ResearchUi.Place(count.rectTransform, band.MaxX - band.MinX - 110, 14, 110, 22);
            }
        }

        void BuildNode(TechSpec spec)
        {
            TechId id = spec.Id;
            Button select = ResearchUi.Button("ResearchNode_" + id, GraphContent, "", () => FocusTechnology(id));
            RectTransform rect = select.transform as RectTransform;
            Rect position = Layout.NodeRect(id);
            ResearchUi.Place(rect, position.x, position.y, position.width, position.height);
            select.GetComponentInChildren<Text>().gameObject.SetActive(false);
            var view = new Node { Rect = rect, Surface = rect.GetComponent<Image>(), Group = rect.gameObject.AddComponent<CanvasGroup>() };
            RectTransform stripe = ResearchUi.Panel("Selection", rect, HudStyle.Accent);
            ResearchUi.Place(stripe, 0, 0, 4, position.height);
            view.SelectionStripe = stripe.GetComponent<Image>();
            view.SelectionStripe.raycastTarget = false;
            view.Title = ResearchUi.Label("TechName_" + id, rect, spec.Name, 11, HudStyle.Text, TextAnchor.MiddleLeft);
            ResearchUi.Place(view.Title.rectTransform, 12, 8, 144, 24);
            RectTransform status = ResearchUi.Panel("TechStatus_" + id, rect, HudStyle.Surface);
            ResearchUi.Place(status, 162, 9, 54, 22);
            view.StateSurface = status.GetComponent<Image>();
            view.StateSurface.raycastTarget = false;
            view.State = ResearchUi.Label("TechStateLabel_" + id, status, "", 11, HudStyle.Text, TextAnchor.MiddleCenter);
            ResearchUi.Stretch(view.State.rectTransform, 1, 1, 1, 1);
            RectTransform track = ResearchUi.Panel("ResearchProgressTrack", rect, HudStyle.Surface);
            ResearchUi.Place(track, 12, 34, 200, 3);
            track.GetComponent<Image>().raycastTarget = false;
            RectTransform fill = ResearchUi.Panel("ResearchProgressFill", track, HudStyle.Accent);
            ResearchUi.Place(fill, 0, 0, 0, 3);
            view.Progress = fill.GetComponent<Image>();
            view.Progress.raycastTarget = false;
            Text benefit = ResearchUi.Label("TechEffect_" + id, rect, spec.Benefit, 11, HudStyle.TextMuted);
            ResearchUi.Place(benefit.rectTransform, 12, 44, 200, 30);
            view.Costs = ResearchUi.Label("TechCosts_" + id, rect,
                "지식 " + spec.ResearchCost + " · " + spec.CoinCost + "G · " + spec.DurationDays + "일", 11, HudStyle.TextMuted);
            ResearchUi.Place(view.Costs.rectTransform, 12, 80, 200, 18);
            view.Start = ResearchUi.Button("Button_연구 시작_" + id, rect, "연구 시작", () => StartResearch(id));
            ResearchUi.Place(view.Start.transform as RectTransform, 8, 104, 208, 44);
            nodeViews.Add(id, view);
            nodes.Add(id, rect);
            string unlocks = string.Join(" ", spec.UnlockBuildings.Select(kind => Catalog.Get(kind).Name));
            string factories = string.Join(" ", FactoryCatalog.All.Where(item => item.RequiredTech == id).Select(item => item.Name));
            searchableText[id] = spec.Name + " " + id + " " + spec.Description + " " + spec.Benefit + " " + unlocks + " " + factories;
        }

        void ApplyLayout(bool force)
        {
            if (root == null || GraphViewport == null) return;
            Vector2 size = root.rect.size;
            if (size.x < 1 || size.y < 1 || (!force && size == lastSize)) return;
            Vector2 previousCenter = GraphCenter;
            bool hadSize = lastSize.x > 0;
            lastSize = size;
            float detailWidth = size.x >= 1280 ? 330 : 300;
            float toolbarHeight = size.x >= 790 ? 52 : 104;
            float footerHeight = 52;
            float graphWidth = Mathf.Max(260, size.x - detailWidth - 12);
            bool narrow = size.x < 680;
            if (narrow) { detailWidth = Mathf.Max(260, size.x); graphWidth = size.x; }
            ResearchUi.Place(toolbar, 0, 0, size.x, toolbarHeight - 8);
            ResearchUi.Place(GraphViewport, 0, toolbarHeight, graphWidth,
                Mathf.Max(160, size.y - toolbarHeight - footerHeight));
            ResearchUi.Place(detailDock, graphWidth + 12, toolbarHeight, detailWidth,
                Mathf.Max(160, size.y - toolbarHeight - footerHeight));
            detailDock.gameObject.SetActive(!narrow);
            ResearchUi.Place(footer, 0, size.y - 44, size.x, 44);
            ResearchUi.Place(economy.rectTransform, 774, 0, Mathf.Max(1, size.x - 774), 44);
            economy.gameObject.SetActive(size.x >= 850);
            foreach (Transform child in toolbar)
            {
                if (child == searchInput.transform || child == economy.transform) continue;
                RectTransform rect = child as RectTransform;
                if (rect == null) continue;
                float x = rect.anchoredPosition.x;
                // Reflow toolbar actions to a second row only on very narrow logical canvases.
                float originalX = child.name == "ResearchZoomLabel" ? 664 : ToolbarOriginalX(child.name);
                if (originalX < 0) continue;
                rect.anchoredPosition = size.x < 790 && originalX >= 452
                    ? new Vector2(originalX - 452, -52) : new Vector2(originalX, 0);
            }
            ResearchUi.Place(hint.rectTransform, 306, 24, Mathf.Max(1, size.x - 306), 20);
            if (Details != null && initialized) Details.Refresh();
            if (hadSize) CenterOnGraphPoint(previousCenter);
            else ClampPan();
            miniMap?.Repaint();
        }

        static float ToolbarOriginalX(string name)
        {
            switch (name)
            {
                case "Button_ResearchSearchNext": return 182;
                case "Button_ResearchFilter_All": return 232;
                case "Button_ResearchFilter_Available": return 294;
                case "Button_ResearchFilter_Unfinished": return 376;
                case "Button_ResearchCurrent": return 452;
                case "Button_ResearchReset": return 538;
                case "Button_ResearchZoomOut": return 616;
                case "Button_ResearchZoomIn": return 716;
                default: return -1;
            }
        }

        public void Refresh()
        {
            if (!initialized || controller.State == null) return;
            GameState state = controller.State;
            if (!ReferenceEquals(observedState, state))
            {
                observedState = state;
                SelectedTechnology = state.ActiveResearch != TechId.None ? state.ActiveResearch : Graph.OrderedTechnologies[0].Id;
                stateSignature = "";
                ZoomLevel = 1;
                GraphContent.localScale = Vector3.one;
                GraphContent.anchoredPosition = Vector2.zero;
            }
            string signature = (int)SelectedTechnology + "|" + state.Coins + "|" + state.ResearchPoints + "|" +
                (int)state.ActiveResearch + "|" + state.ResearchDaysRemaining + "|" + state.Day + "|" +
                controller.Sim.ResearchPerDay + "|" + string.Join(",", state.Technologies);
            if (signature == stateSignature) return;
            stateSignature = signature;
            RefreshPresentation();
        }

        void RefreshPresentation()
        {
            if (!initialized || controller.State == null) return;
            selectedPath.Clear();
            foreach (TechId id in Graph.Ancestors(SelectedTechnology)) selectedPath.Add(id);
            selectedPath.Add(SelectedTechnology);
            matches.Clear();
            GameState state = controller.State;
            int finished = 0;
            foreach (TechSpec spec in Graph.OrderedTechnologies)
            {
                Node view = nodeViews[spec.Id];
                bool complete = TechCatalog.Has(state, spec.Id);
                bool active = state.ActiveResearch == spec.Id;
                bool available = controller.Sim.CanResearch(spec.Id, out string reason);
                bool prerequisitesMet = spec.Prerequisites.All(id => TechCatalog.Has(state, id));
                if (complete) finished++;
                bool match = (filter == ResearchFilter.All || filter == ResearchFilter.Available && available || filter == ResearchFilter.Unfinished && !complete) &&
                    (search.Length == 0 || searchableText[spec.Id].IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match) matches.Add(spec.Id);
                view.Group.alpha = match ? 1 : .4f;
                view.SelectionStripe.gameObject.SetActive(spec.Id == SelectedTechnology);
                view.Surface.color = spec.Id == SelectedTechnology
                    ? Color.Lerp(HudStyle.SurfaceRaised, HudStyle.Accent, .22f)
                    : complete ? Color.Lerp(HudStyle.SurfaceRaised, HudStyle.Positive, .13f) : HudStyle.SurfaceRaised;
                view.State.text = complete ? "완료" : active ? "진행 중" : available ? "가능" : prerequisitesMet ? "대기" : "잠김";
                view.StateSurface.color = complete ? HudStyle.Positive : active ? HudStyle.Accent : HudStyle.Surface;
                view.State.color = HudStyle.Foreground(view.StateSurface.color);
                view.Start.interactable = available;
                view.Start.GetComponentInChildren<Text>().text = complete ? "연구 완료" : active ? state.ResearchDaysRemaining + "일 남음" :
                    available ? "연구 시작" : !prerequisitesMet ? "선행 기술 필요" : reason;
                ResearchUi.SetButtonColor(view.Start, available ? HudStyle.Positive : active ? HudStyle.Accent : HudStyle.Surface);
                view.Progress.rectTransform.sizeDelta = new Vector2(complete ? 200 : active ? Mathf.Round(200 * controller.Sim.ResearchProgress) : 0, 3);
                view.Progress.color = complete ? HudStyle.Positive : HudStyle.Accent;
            }
            foreach (ResearchEdge edge in Graph.Edges)
            {
                bool route = selectedPath.Contains(edge.Source) && selectedPath.Contains(edge.Target);
                bool learned = TechCatalog.Has(state, edge.Source);
                int style = route ? 2 : learned ? 1 : 0;
                if (!edgeStyles.TryGetValue(edge, out int previous) || previous != style)
                {
                    connections[edge].SetPath(Layout.EdgePoints(edge), route ? HudStyle.Accent : learned ? HudStyle.Positive :
                        Color.Lerp(HudStyle.SurfaceRaised, HudStyle.TextMuted, .7f), route ? 4 : 2, !route && !learned);
                    edgeStyles[edge] = style;
                }
            }
            foreach (var pair in filterButtons) ResearchUi.SetButtonColor(pair.Value, pair.Key == filter ? HudStyle.Accent : HudStyle.SurfaceRaised);
            Era selectedEra = TechCatalog.Get(SelectedTechnology).Era;
            foreach (var pair in eraButtons) ResearchUi.SetButtonColor(pair.Value, pair.Key == selectedEra ? HudStyle.Accent : HudStyle.SurfaceRaised);
            economy.text = "지식 " + Mathf.FloorToInt(state.ResearchPoints) + " · " + state.Coins.ToString("N0") + "G\n+" +
                controller.Sim.ResearchPerDay.ToString("0.#") + "/일 · 발견 " + finished + "/" + nodes.Count;
            nextMatch.interactable = matches.Count > 0;
            hint.text = search.Length > 0 || filter != ResearchFilter.All
                ? "일치 " + matches.Count + "/" + nodes.Count + " · 흐린 기술도 선택 가능 · 드래그 이동"
                : "드래그 이동 · 휠 좌우 · Shift+휠 상하 · 작은 지도 클릭";
            Details.ShowTechnology(SelectedTechnology);
            UpdateZoomControls();
            miniMap?.Repaint();
        }

        public RectTransform NodeFor(TechId id) => nodes.TryGetValue(id, out RectTransform value) ? value : null;
        public Button StartButtonFor(TechId id) => nodeViews.TryGetValue(id, out Node value) ? value.Start : null;

        public void FocusTechnology(TechId id, bool select = true)
        {
            if (!initialized || !nodes.ContainsKey(id)) return;
            ApplyLayout(false);
            if (select)
            {
                SelectedTechnology = id;
                stateSignature = "";
                RefreshPresentation();
            }
            CenterOnGraphPoint(Layout.NodeRect(id).center);
        }

        void FocusEra(Era era)
        {
            TechSpec target = Graph.OrderedTechnologies.FirstOrDefault(spec => spec.Era == era && controller.Sim.CanResearch(spec.Id, out _))
                ?? Graph.OrderedTechnologies.First(spec => spec.Era == era);
            FocusTechnology(target.Id);
        }

        void FocusCurrent()
        {
            TechId id = controller.State.ActiveResearch;
            if (id == TechId.None) id = Graph.OrderedTechnologies.FirstOrDefault(spec => controller.Sim.CanResearch(spec.Id, out _))?.Id ?? SelectedTechnology;
            FocusTechnology(id);
        }

        public void ResetView()
        {
            if (!initialized) return;
            SetSearch("");
            SetFilter(ResearchFilter.All);
            SetZoom(1);
            SelectedTechnology = Graph.OrderedTechnologies[0].Id;
            RefreshPresentation();
            TreeScroll.StopMovement();
            GraphContent.anchoredPosition = Vector2.zero;
            ClampPan();
            miniMap?.Repaint();
        }

        public void SetSearch(string query)
        {
            if (searchInput == null) return;
            query = (query ?? "").Trim();
            if (query.Length > 60) query = query.Substring(0, 60);
            searchInput.SetTextWithoutNotify(query);
            search = query;
            matchIndex = -1;
            RefreshPresentation();
        }

        public void SetFilter(ResearchFilter value)
        {
            if (!Enum.IsDefined(typeof(ResearchFilter), value)) return;
            filter = value;
            matchIndex = -1;
            RefreshPresentation();
        }

        void NextSearchMatch()
        {
            if (matches.Count == 0) return;
            matchIndex = (matchIndex + 1) % matches.Count;
            FocusTechnology(matches[matchIndex]);
        }

        public void SetZoom(int level)
        {
            if (GraphContent == null) return;
            Canvas.ForceUpdateCanvases();
            ApplyLayout(false);
            Vector2 center = GraphCenter;
            if (nodes.ContainsKey(SelectedTechnology))
            {
                Vector2 selected = Layout.NodeRect(SelectedTechnology).center;
                Vector2 onMap = new Vector2(selected.x * ZoomLevel + GraphContent.anchoredPosition.x,
                    selected.y * ZoomLevel - GraphContent.anchoredPosition.y);
                if (onMap.x >= 0 && onMap.x <= GraphViewport.rect.width && onMap.y >= 0 && onMap.y <= GraphViewport.rect.height)
                    center = selected;
            }
            ZoomLevel = Mathf.Clamp(level, 1, 2);
            GraphContent.localScale = new Vector3(ZoomLevel, ZoomLevel, 1);
            CenterOnGraphPoint(center);
            UpdateZoomControls();
            // Commit the scaled ScrollRect bounds and mask before callers use the new view.
            Canvas.ForceUpdateCanvases();
        }

        void UpdateZoomControls()
        {
            if (zoomLabel == null) return;
            zoomLabel.text = ZoomLevel * 100 + "%";
            previousZoom.interactable = ZoomLevel > 1;
            nextZoom.interactable = ZoomLevel < 2;
        }

        public Vector2 GraphCenter => GraphContent == null || GraphViewport == null ? Vector2.zero :
            new Vector2((-GraphContent.anchoredPosition.x + GraphViewport.rect.width * .5f) / ZoomLevel,
                (GraphContent.anchoredPosition.y + GraphViewport.rect.height * .5f) / ZoomLevel);

        public void CenterOnGraphPoint(Vector2 point)
        {
            if (GraphContent == null || GraphViewport == null) return;
            TreeScroll.StopMovement();
            GraphContent.anchoredPosition = new Vector2(GraphViewport.rect.width * .5f - point.x * ZoomLevel,
                point.y * ZoomLevel - GraphViewport.rect.height * .5f);
            ClampPan();
            miniMap?.Repaint();
        }

        public void PanBy(Vector2 delta)
        {
            if (GraphContent == null) return;
            TreeScroll.StopMovement();
            GraphContent.anchoredPosition += delta;
            ClampPan();
            miniMap?.Repaint();
        }

        void ClampPan()
        {
            if (GraphContent == null || GraphViewport == null) return;
            Vector2 maximum = Vector2.Max(Vector2.zero, Layout.ContentSize * ZoomLevel - GraphViewport.rect.size);
            Vector2 position = GraphContent.anchoredPosition;
            GraphContent.anchoredPosition = new Vector2(Mathf.Round(Mathf.Clamp(position.x, -maximum.x, 0)),
                Mathf.Round(Mathf.Clamp(position.y, 0, maximum.y)));
        }

        void StartResearch(TechId id)
        {
            if (!controller.ResearchOpen || !controller.Sim.CanResearch(id, out _)) return;
            SelectedTechnology = id;
            controller.Research(id);
            stateSignature = "";
            refreshPending = true;
        }

        public Color MiniMapNodeColor(TechId id)
        {
            if (id == SelectedTechnology) return HudStyle.Accent;
            if (TechCatalog.Has(controller.State, id)) return HudStyle.Positive;
            return controller.Sim.CanResearch(id, out _) ? HudStyle.Text : HudStyle.TextMuted;
        }

        public bool VerifyLayout(out string reason)
        {
            if (!initialized || nodes.Count != Graph.OrderedTechnologies.Count || connections.Count != Graph.Edges.Count)
            { reason = "기술 또는 선행 연결선이 누락됐습니다."; return false; }
            if (GraphViewport.rect.width < 1 || GraphViewport.rect.height < 1 || !TreeScroll.horizontal || !TreeScroll.vertical)
            { reason = "기술 지도 이동 영역이 올바르지 않습니다."; return false; }
            foreach (var pair in nodes)
            {
                Rect rect = Layout.NodeRect(pair.Key);
                Button start = StartButtonFor(pair.Key);
                RectTransform buttonRect = start.transform as RectTransform;
                if (rect.xMin < 0 || rect.yMin < 0 || rect.xMax > Layout.ContentSize.x || rect.yMax > Layout.ContentSize.y ||
                    buttonRect.rect.width < 200 || buttonRect.rect.height < HudStyle.TouchSize ||
                    start.name != "Button_연구 시작_" + pair.Key)
                { reason = "기술 카드 영역이 올바르지 않습니다: " + pair.Key; return false; }
            }
            foreach (ResearchEdge edge in Graph.Edges)
            {
                if (!connections.ContainsKey(edge) || connections[edge].raycastTarget || connections[edge].Points.Count < 2)
                { reason = "선행 연결선이 올바르지 않습니다."; return false; }
            }
            reason = nodes.Count + "개 기술과 선행 연결 지도 정상";
            return true;
        }
    }
}

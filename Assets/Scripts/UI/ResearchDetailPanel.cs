using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Selected-technology explanation and route planning for the research graph.</summary>
    public sealed class ResearchDetailPanel : MonoBehaviour
    {
        GameController controller;
        ResearchGraph graph;
        Action<TechId> focusTechnology;
        Action<TechId> startResearch;
        RectTransform header;
        RectTransform content;
        Text title;
        Text eraAndStatus;
        Button startButton;
        bool initialized;
        bool cacheValid;
        GameState cachedState;
        TechId cachedSelection;
        TechId cachedActive;
        int cachedRemaining;
        int cachedDay;
        float cachedKnowledge;
        float cachedCoins;
        ulong cachedTechnologyMask;
        Vector2 cachedSize;

        public TechId SelectedTechnology { get; private set; }
        public ScrollRect Scroll { get; private set; }

        public void Initialize(GameController gameController, ResearchGraph researchGraph,
            Action<TechId> focus, Action<TechId> start)
        {
            if (initialized) throw new InvalidOperationException("Research detail panel is already initialized.");
            controller = gameController ?? throw new ArgumentNullException(nameof(gameController));
            graph = researchGraph ?? throw new ArgumentNullException(nameof(researchGraph));
            focusTechnology = focus;
            startResearch = start;
            initialized = true;
            Build();
            SelectedTechnology = graph.OrderedTechnologies[0].Id;
            Refresh();
        }

        public void ShowTechnology(TechId id)
        {
            if (TechCatalog.Get(id) == null) return;
            bool changed = SelectedTechnology != id;
            SelectedTechnology = id;
            if (changed) cacheValid = false;
            Refresh();
            if (changed && Scroll != null) Scroll.verticalNormalizedPosition = 1f;
        }

        public void Refresh()
        {
            if (controller == null || graph == null || content == null) return;
            TechSpec spec = TechCatalog.Get(SelectedTechnology);
            if (spec == null) return;

            GameState state = controller.State;
            ulong technologyMask = TechnologyMask(state);
            Vector2 panelSize = (transform as RectTransform).rect.size;
            if (cacheValid && ReferenceEquals(cachedState, state) && cachedSelection == SelectedTechnology &&
                cachedActive == state.ActiveResearch && cachedRemaining == state.ResearchDaysRemaining &&
                cachedDay == state.Day && Mathf.Approximately(cachedKnowledge, state.ResearchPoints) &&
                Mathf.Approximately(cachedCoins, state.Coins) && cachedTechnologyMask == technologyMask &&
                Approximately(cachedSize, panelSize)) return;

            bool selectionChanged = !cacheValid || cachedSelection != SelectedTechnology;
            float scrollPosition = Scroll == null ? 1f : Scroll.verticalNormalizedPosition;
            ClearContent();
            bool complete = TechCatalog.Has(state, spec.Id);
            bool active = state.ActiveResearch == spec.Id;
            bool canResearch = controller.Sim.CanResearch(spec.Id, out string reason);

            title.text = spec.Name;
            eraAndStatus.text = TechCatalog.EraName(spec.Era) + "  ·  " +
                (complete ? "연구 완료" : active ? "연구 진행 중" : canResearch ? "연구 가능" : reason);
            startButton.interactable = canResearch;
            ResearchUi.SetButtonColor(startButton,
                canResearch ? HudStyle.Positive : active ? HudStyle.Accent : HudStyle.SurfaceRaised);
            Text actionText = startButton.GetComponentInChildren<Text>();
            if (actionText != null)
                actionText.text = complete ? "연구 완료" : active ? "연구 진행 중" : canResearch ? "연구 시작" : reason;

            float y = 0f;
            AddBody(spec.Description, ref y, 46f, HudStyle.Text);

            if (active)
            {
                int remaining = Mathf.Max(0, state.ResearchDaysRemaining);
                AddSection("진행 상황", ref y);
                AddBody($"{Mathf.RoundToInt(controller.Sim.ResearchProgress * 100f)}% · {remaining}일 남음\n현재 {state.Day}일 → 예상 완료 {state.Day + remaining}일", ref y, 48f, HudStyle.Accent);
            }

            AddSection("연구 비용", ref y);
            AddBody($"지식 {spec.ResearchCost} (보유 {Mathf.FloorToInt(state.ResearchPoints)})\n코인 {spec.CoinCost}G (보유 {Mathf.FloorToInt(state.Coins)}G)\n기간 {spec.DurationDays}일 · 지식/일은 기간을 단축하지 않음", ref y, 62f, HudStyle.Text);

            AddSection("실제 효과", ref y);
            AddBody(EffectText(spec.Id), ref y, EffectHeight(spec.Id), HudStyle.Positive);

            AddUnlocks(spec, ref y);
            AddPrerequisites(spec, state, ref y);
            AddSuccessors(spec, ref y);
            AddPlan(spec, state, ref y);

            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(y + 10f, 1f));
            Canvas.ForceUpdateCanvases();
            if (Scroll != null) Scroll.verticalNormalizedPosition = selectionChanged ? 1f : scrollPosition;
            cachedState = state;
            cachedSelection = SelectedTechnology;
            cachedActive = state.ActiveResearch;
            cachedRemaining = state.ResearchDaysRemaining;
            cachedDay = state.Day;
            cachedKnowledge = state.ResearchPoints;
            cachedCoins = state.Coins;
            cachedTechnologyMask = technologyMask;
            cachedSize = panelSize;
            cacheValid = true;
        }

        void Build()
        {
            RectTransform root = transform as RectTransform;
            RectTransform surface = ResearchUi.Panel("ResearchDetailSurface", root, HudStyle.SurfaceRaised);
            ResearchUi.Stretch(surface, 0, 0, 0, 0);

            header = ResearchUi.Rect("ResearchDetailHeader", surface);
            header.anchorMin = new Vector2(0, 1);
            header.anchorMax = new Vector2(1, 1);
            header.pivot = new Vector2(.5f, 1);
            header.anchoredPosition = new Vector2(0, -12f);
            header.sizeDelta = new Vector2(-28f, 60f);
            title = ResearchUi.Label("ResearchDetailTitle", header, "", HudStyle.TitleSize, HudStyle.Text, TextAnchor.UpperLeft);
            ResearchUi.Stretch(title.rectTransform, 0, 0, 0, 30);
            eraAndStatus = ResearchUi.Label("ResearchDetailStatus", header, "", HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.UpperLeft);
            ResearchUi.Stretch(eraAndStatus.rectTransform, 0, 32, 0, 0);

            RectTransform viewport = ResearchUi.Panel("ResearchDetailViewport", surface, HudStyle.Surface);
            ResearchUi.Stretch(viewport, 12, 80, 18, 64);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            content = ResearchUi.Rect("ResearchDetailContent", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            Scroll = viewport.gameObject.AddComponent<ScrollRect>();
            Scroll.viewport = viewport;
            Scroll.content = content;
            Scroll.horizontal = false;
            Scroll.vertical = true;
            Scroll.movementType = ScrollRect.MovementType.Clamped;
            Scroll.scrollSensitivity = 24f;

            RectTransform scrollbarTrack = ResearchUi.Panel("ResearchDetailScrollbar", surface, HudStyle.Surface);
            scrollbarTrack.anchorMin = new Vector2(1, 0);
            scrollbarTrack.anchorMax = new Vector2(1, 1);
            scrollbarTrack.offsetMin = new Vector2(-12, 64);
            scrollbarTrack.offsetMax = new Vector2(-6, -80);
            RectTransform slidingArea = ResearchUi.Rect("Sliding Area", scrollbarTrack);
            ResearchUi.Stretch(slidingArea, 0, 0, 0, 0);
            RectTransform handle = ResearchUi.Panel("Handle", slidingArea, HudStyle.TextMuted);
            ResearchUi.Stretch(handle, 0, 0, 0, 0);
            Scrollbar scrollbar = scrollbarTrack.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            Scroll.verticalScrollbar = scrollbar;
            Scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            Scroll.verticalScrollbarSpacing = 0;

            startButton = ResearchUi.Button("Button_ResearchDetailStart", surface, "연구 시작",
                () =>
                {
                    TechId id = SelectedTechnology;
                    startResearch?.Invoke(id);
                    Refresh();
                }, HudStyle.Accent);
            RectTransform action = startButton.transform as RectTransform;
            action.anchorMin = new Vector2(0, 0);
            action.anchorMax = new Vector2(1, 0);
            action.pivot = new Vector2(.5f, 0);
            action.anchoredPosition = new Vector2(0, 10);
            action.sizeDelta = new Vector2(-24, HudStyle.TouchSize);
        }

        void AddUnlocks(TechSpec spec, ref float y)
        {
            BuildingKind[] buildings = spec.UnlockBuildings ?? Array.Empty<BuildingKind>();
            FactorySpec[] factories = FactoryCatalog.All.Where(item => item != null && item.RequiredTech == spec.Id).ToArray();
            RecipeSpec[] recipes = FactoryCatalog.Recipes.Where(item => item != null && item.RequiredTech == spec.Id).ToArray();
            if (buildings.Length == 0 && factories.Length == 0 && recipes.Length == 0) return;

            AddSection("해금", ref y);
            foreach (BuildingKind kind in buildings)
            {
                BuildingSpec building = Catalog.Get(kind);
                string population = building.UnlockPopulation > 0 ? $" · 인구 {building.UnlockPopulation} 필요" : "";
                AddBody("시설 · " + building.Name + population, ref y, 26f, HudStyle.Text);
            }
            foreach (FactorySpec factory in factories)
                AddBody("공장 설비 · " + factory.Name, ref y, 26f, HudStyle.Text);
            foreach (RecipeSpec recipe in recipes)
                AddBody("제조법 · " + recipe.Name, ref y, 26f, HudStyle.Text);
        }

        void AddPrerequisites(TechSpec spec, GameState state, ref float y)
        {
            TechId[] prerequisites = spec.Prerequisites ?? Array.Empty<TechId>();
            AddSection("직접 선행 기술" + (prerequisites.Length > 1 ? " · 모두 필요" : ""), ref y);
            if (prerequisites.Length == 0)
            {
                AddBody("없음", ref y, 24f, HudStyle.TextMuted);
                return;
            }
            foreach (TechId id in prerequisites)
            {
                TechSpec required = TechCatalog.Get(id);
                string stateText = TechCatalog.Has(state, id) ? "완료" : state.ActiveResearch == id ? "진행 중" : "필요";
                AddLink("Button_ResearchPrerequisite_" + id, required.Name + " · " + stateText, id, ref y);
            }
        }

        void AddSuccessors(TechSpec spec, ref float y)
        {
            IReadOnlyList<TechId> successors = graph.DirectSuccessors(spec.Id);
            AddSection("다음 기술", ref y);
            if (successors.Count == 0)
            {
                AddBody("최종 기술", ref y, 24f, HudStyle.TextMuted);
                return;
            }
            foreach (TechId id in successors)
                AddLink("Button_ResearchSuccessor_" + id, TechCatalog.Get(id).Name, id, ref y);
        }

        void AddPlan(TechSpec spec, GameState state, ref float y)
        {
            IReadOnlyList<TechId> rawPlan = graph.PlanTo(spec.Id, state.Technologies);
            bool activeOnPath = state.ActiveResearch != TechId.None && rawPlan.Contains(state.ActiveResearch);
            var paidAsComplete = new List<TechId>(state.Technologies);
            if (activeOnPath) paidAsComplete.Add(state.ActiveResearch);
            ResearchPlanSummary unpaid = graph.PlanSummaryTo(spec.Id, paidAsComplete);
            int planDays = unpaid.DurationDays + (activeOnPath ? Mathf.Max(0, state.ResearchDaysRemaining) : 0);

            AddSection("연구 경로", ref y);
            if (rawPlan.Count == 0)
            {
                AddBody("이미 완료한 기술", ref y, 24f, HudStyle.Positive);
                return;
            }

            string route = string.Join(" → ", rawPlan.Select(id => TechCatalog.Get(id).Name));
            AddBody(route, ref y, Mathf.Max(42f, 18f * Mathf.Ceil(route.Length / 24f)), HudStyle.Text);
            AddBody($"남은 합계 · 지식 {unpaid.KnowledgeCost} · 코인 {unpaid.CoinCost}G · {planDays}일 (자원 대기 제외 · 기초 도시 일수)", ref y, 42f, HudStyle.Accent);
            if (activeOnPath)
                AddBody("진행 중 연구는 이미 낸 비용을 제외하고 남은 일수만 계산", ref y, 32f, HudStyle.TextMuted);
            else if (state.ActiveResearch != TechId.None)
                AddBody($"다른 연구 완료까지 {Mathf.Max(0, state.ResearchDaysRemaining)}일 대기 · 위 합계는 선택한 현재 계획만 표시", ref y, 32f, HudStyle.Danger);

            TechId first = rawPlan[0];
            AddLink("Button_ResearchPlanFirst", "첫 단계로 이동 · " + TechCatalog.Get(first).Name, first, ref y);
        }

        void AddSection(string text, ref float y)
        {
            y += 10f;
            Text label = ResearchUi.Label("ResearchDetailSection", content, text, HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.UpperLeft);
            label.fontStyle = FontStyle.Normal;
            label.supportRichText = false;
            ResearchUi.Place(label.rectTransform, 10, y, 100, 24);
            SetContentWidth(label.rectTransform, 10, 10);
            y += 26f;
        }

        void AddBody(string text, ref float y, float height, Color color)
        {
            Text label = ResearchUi.Label("ResearchDetailText", content, text ?? "", HudStyle.BodySize, color, TextAnchor.UpperLeft);
            label.fontStyle = FontStyle.Normal;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            ResearchUi.Place(label.rectTransform, 10, y, 100, height);
            SetContentWidth(label.rectTransform, 10, 10);
            float fittedHeight = Mathf.Max(height, Mathf.Ceil(label.preferredHeight) + 4f);
            label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fittedHeight);
            y += fittedHeight + 4f;
        }

        void AddLink(string name, string text, TechId id, ref float y)
        {
            Button button = ResearchUi.Button(name, content, text, () => focusTechnology?.Invoke(id), HudStyle.SurfaceRaised);
            RectTransform rect = button.transform as RectTransform;
            ResearchUi.Place(rect, 10, y, 100, HudStyle.TouchSize);
            SetContentWidth(rect, 10, 10);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) { label.fontStyle = FontStyle.Normal; label.supportRichText = false; }
            y += HudStyle.TouchSize + 6f;
        }

        static void SetContentWidth(RectTransform rect, float left, float right)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2((left - right) * .5f, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(-(left + right), rect.sizeDelta.y);
        }

        void ClearContent()
        {
            for (int index = content.childCount - 1; index >= 0; index--)
            {
                GameObject child = content.GetChild(index).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        static bool Approximately(Vector2 first, Vector2 second) =>
            Mathf.Abs(first.x - second.x) < .5f && Mathf.Abs(first.y - second.y) < .5f;

        static ulong TechnologyMask(GameState state)
        {
            ulong mask = 0;
            if (state?.Technologies == null) return mask;
            foreach (TechId id in state.Technologies)
            {
                int bit = (int)id;
                if (bit > 0 && bit < 64) mask |= 1UL << bit;
            }
            return mask;
        }

        static float EffectHeight(TechId id) => id == TechId.UrbanPlanning || id == TechId.Education ? 48f : 30f;

        static string EffectText(TechId id)
        {
            switch (id)
            {
                case TechId.CropRotation: return "농장 생산량 ×1.25";
                case TechId.Stonecraft: return "일반 건물 증축 상한 레벨 2";
                case TechId.MechanicalPower: return "풍차·제분소·제과점과 초기 물류 설비 해금";
                case TechId.Guilds: return "르네상스 시대 진입";
                case TechId.Metallurgy: return "광산·제련소와 채굴·제련 공장 설비 해금";
                case TechId.Scholarship: return "학술원 해금";
                case TechId.Toolmaking: return "공방과 조립기 해금";
                case TechId.SteamPower: return "산업 시대 진입 · 증기 동력소와 전력 설비 해금";
                case TechId.UrbanPlanning: return "일반 건물 증축 상한 레벨 3\n대도시 최종 목표의 필수 조건";
                case TechId.Forestry: return "벌목장 생산량 ×1.25";
                case TechId.Irrigation: return "농장 생산량 ×1.20 · 윤작과 함께면 총 ×1.50";
                case TechId.Masonry: return "채석장 생산량 ×1.25";
                case TechId.Logistics: return "공장 벨트 속도 ×1.50";
                case TechId.Education: return "연결된 서재의 레벨당 지식/일 1.5 → 2.5\n레벨당 +1 지식/일";
                case TechId.MetallurgicalEfficiency: return "광산·제련소 생산량 ×1.20";
                case TechId.Electrification: return "공장 전력 수요 ×0.85 (15% 감소)";
                case TechId.MassProduction: return "공장 기계 제작 속도 ×1.25";
                case TechId.Automation: return "공장 삽입기 속도 ×1.50";
                case TechId.FluidHandling: return "파이프·분기 파이프·유체 탱크·물 추출과 유체 운송 해금";
                case TechId.OilRefining: return "원유 추출·정유 시설과 원유 정제 제조법 해금";
                case TechId.Petrochemistry: return "화학 공장과 플라스틱·고무·황·연료 공정 해금";
                case TechId.Electronics: return "회로·모터·고급 회로 생산 공정 해금";
                case TechId.AluminumProcessing: return "보크사이트에서 알루미늄과 케이싱까지의 생산 공정 해금";
                case TechId.EnergyStorage: return "배터리 생산 공정 해금";
                case TechId.AdvancedManufacturing: return "제조기·컴퓨터·재활용 공정과 150%·200% 오버클럭 해금";
                case TechId.IndustrialControl: return "제어 장치 생산 공정 해금";
                default: return "효과 없음";
            }
        }
    }
}

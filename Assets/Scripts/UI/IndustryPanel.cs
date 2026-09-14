using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Searchable, catalog-backed industry codex and live factory overview.</summary>
    public sealed class IndustryPanel : MonoBehaviour
    {
        enum Page { Recipes, Resources, Production }

        GameController game;
        Action closed;
        RectTransform window, viewport, content, searchRect;
        InputField search;
        Text heading, summary;
        Button recipesTab, resourcesTab, productionTab;
        Page page;
        FactoryRecipe selectedRecipe = FactoryRecipe.IronPlate;
        bool initialized;

        public bool IsOpen => gameObject.activeSelf;

        public void Initialize(GameController controller, Action onClosed = null)
        {
            if (initialized) throw new InvalidOperationException("Industry panel is already initialized.");
            game = controller ?? throw new ArgumentNullException(nameof(controller));
            closed = onClosed;
            initialized = true;
            Build();
            gameObject.SetActive(false);
        }

        public void Open()
        {
            if (!initialized) return;
            game.ModalOpen = true;
            gameObject.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (!initialized) return;
            gameObject.SetActive(false);
            if (game != null) game.ModalOpen = false;
            closed?.Invoke();
        }

        void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (game == null || !game.ModalOpen) { gameObject.SetActive(false); return; }
            ApplyLayout();
        }

        void Build()
        {
            RectTransform root = transform as RectTransform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            Image dim = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            dim.color = HudStyle.Dim; dim.raycastTarget = true;

            window = ResearchUi.Panel("IndustryWindow", root, HudStyle.SurfaceRaised);
            heading = ResearchUi.Label("IndustryTitle", window, "산업 도감", HudStyle.TitleSize, HudStyle.Text, TextAnchor.MiddleLeft);
            Button close = ResearchUi.Button("Button_IndustryClose", window, "×", Close, HudStyle.Surface);
            ResearchUi.Place(close.transform as RectTransform, 0, 0, HudStyle.TouchSize, HudStyle.TouchSize);

            recipesTab = ResearchUi.Button("Button_IndustryRecipes", window, "제조법 37", () => SetPage(Page.Recipes));
            resourcesTab = ResearchUi.Button("Button_IndustryResources", window, "자원 38", () => SetPage(Page.Resources));
            productionTab = ResearchUi.Button("Button_IndustryProduction", window, "생산 현황", () => SetPage(Page.Production));

            searchRect = ResearchUi.Panel("IndustrySearch", window, HudStyle.Surface);
            search = searchRect.gameObject.AddComponent<InputField>();
            Text inputText = ResearchUi.Label("Text", searchRect, "", HudStyle.BodySize, HudStyle.Text, TextAnchor.MiddleLeft);
            ResearchUi.Stretch(inputText.rectTransform, 12, 2, 12, 2);
            Text placeholder = ResearchUi.Label("Placeholder", searchRect, "제조법·자원·재료 검색", HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.MiddleLeft);
            ResearchUi.Stretch(placeholder.rectTransform, 12, 2, 12, 2);
            search.textComponent = inputText; search.placeholder = placeholder;
            search.lineType = InputField.LineType.SingleLine;
            search.characterLimit = 60;
            search.onValueChanged.AddListener(_ => Refresh());

            summary = ResearchUi.Label("IndustrySummary", window, "", HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.MiddleRight);
            viewport = ResearchUi.Panel("IndustryViewport", window, new Color(0, 0, 0, 0));
            viewport.gameObject.AddComponent<RectMask2D>();
            content = ResearchUi.Rect("IndustryContent", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 34;
            SetPage(Page.Recipes);
        }

        void SetPage(Page value)
        {
            page = value;
            if (searchRect != null) searchRect.gameObject.SetActive(value != Page.Production);
            Refresh();
        }

        public void Refresh()
        {
            if (!initialized || content == null) return;
            ApplyLayout();
            ClearContent();
            ResearchUi.SetButtonColor(recipesTab, page == Page.Recipes ? HudStyle.Accent : HudStyle.Surface);
            ResearchUi.SetButtonColor(resourcesTab, page == Page.Resources ? HudStyle.Accent : HudStyle.Surface);
            ResearchUi.SetButtonColor(productionTab, page == Page.Production ? HudStyle.Accent : HudStyle.Surface);
            if (page == Page.Recipes) BuildRecipes();
            else if (page == Page.Resources) BuildResources();
            else BuildProduction();
            ApplyLayout();
        }

        void BuildRecipes()
        {
            string query = (search == null ? "" : search.text).Trim();
            RecipeSpec[] matches = FactoryCatalog.Recipes.Where(r => r != null && r.Id != FactoryRecipe.None && Matches(r, query)).ToArray();
            summary.text = "표시 " + matches.Length + " / " + FactoryCatalog.Recipes.Count(r => r != null && r.Id != FactoryRecipe.None);
            float y = 0;
            foreach (RecipeSpec recipe in matches)
            {
                bool selected = recipe.Id == selectedRecipe;
                Button card = ResearchUi.Button("Button_IndustryRecipe_" + recipe.Id, content, "", () => { selectedRecipe = recipe.Id; Refresh(); },
                    selected ? Color.Lerp(HudStyle.SurfaceRaised, HudStyle.Accent, .22f) : HudStyle.SurfaceRaised);
                ResearchUi.Place(card.transform as RectTransform, 0, y, 100, selected ? 166 : 92);
                StretchWidth(card.transform as RectTransform, 0, 0);
                Text label = card.GetComponentInChildren<Text>(); label.alignment = TextAnchor.UpperLeft;
                label.text = RecipeDescription(recipe, selected);
                y += (selected ? 166 : 92) + 8;
            }
            if (matches.Length == 0) AddEmpty("검색 결과가 없습니다.", ref y);
            content.sizeDelta = new Vector2(0, Mathf.Max(1, y));
        }

        static bool Matches(RecipeSpec recipe, string query)
        {
            if (query.Length == 0) return true;
            string text = recipe.Name + " " + recipe.Description + " " + recipe.Id + " " +
                string.Join(" ", recipe.Inputs.Concat(recipe.Outputs).Select(a => ResourceCatalog.Get(a.Resource)?.Name));
            return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string RecipeDescription(RecipeSpec recipe, bool expanded)
        {
            string machines = string.Join("·", recipe.Machines.Select(k => FactoryCatalog.Get(k)?.Name ?? k.ToString()));
            string input = Amounts(recipe.Inputs), output = Amounts(recipe.Outputs);
            float duration = Mathf.Max(.01f, recipe.Duration);
            string rates = "분당 " + RateAmounts(recipe.Outputs, 60f / duration);
            string locked = recipe.RequiredTech != TechId.None && !TechCatalog.Has(game.State, recipe.RequiredTech)
                ? "잠김 · " + TechCatalog.Get(recipe.RequiredTech).Name + " 연구 필요" : "사용 가능";
            string baseText = recipe.Name + "   " + locked + "\n" + input + "  →  " + output +
                "\n" + duration.ToString("0.#") + "초 · " + rates + " · " + machines;
            if (!expanded) return baseText;
            string kind = recipe.IsExtraction ? "원료 추출" : recipe.Inputs.Any(a => recipe.Outputs.Any(o => o.Resource == a.Resource)) ? "순환·재활용 공정" :
                recipe.Outputs.Length > 1 ? "주산물 + 부산물 공정 (모든 출력 공간 필요)" : "가공 공정";
            return baseText + "\n" + kind + " · 기본 전력 " + BasePower(recipe).ToString("0.#") + "\n" + recipe.Description;
        }

        void BuildResources()
        {
            string query = (search == null ? "" : search.text).Trim();
            ResourceSpec[] matches = ResourceCatalog.All.Where(r => r != null &&
                (query.Length == 0 || (r.Name + " " + r.Id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            summary.text = "표시 " + matches.Length + " / " + ResourceCatalog.Count;
            float y = 0;
            foreach (ResourceSpec resource in matches)
            {
                float amount = game.State.Stock[(int)resource.Id];
                string phase = resource.Id == Resource.Coins ? "통화" : resource.IsFluid ? "유체" : "고체";
                string trade = resource.TradePrice > 0 ? " · 교역 " + resource.TradePrice + "G" : " · 교역 불가";
                AddRow("IndustryResource_" + resource.Id, resource.Name, amount.ToString("#,0.##") + resource.Unit + " · " + phase + trade, ref y);
            }
            if (matches.Length == 0) AddEmpty("검색 결과가 없습니다.", ref y);
            content.sizeDelta = new Vector2(0, Mathf.Max(1, y));
        }

        void BuildProduction()
        {
            FactoryState state = game.State.Factory;
            int machines = state?.Entities?.Count ?? 0;
            int running = state?.Entities?.Count(e => FactoryCatalog.IsProduction(e.Kind) && !e.IsStopped) ?? 0;
            summary.text = "설비 " + machines + " · 가동 " + running;
            float y = 0;
            AddBanner("현재 생산", "누적 생산량과 도시로 반출된 수량입니다. 유체는 아이템 반출 부두로 내보낼 수 없습니다.", ref y);
            if (state != null)
            {
                foreach (ResourceSpec resource in ResourceCatalog.All.Where(r => r.Id != Resource.Coins))
                {
                    int made = Inventory(state.Produced, resource.Id), exported = Inventory(state.Exported, resource.Id);
                    if (made == 0 && exported == 0) continue;
                    AddRow("IndustryProduction_" + resource.Id, resource.Name,
                        "생산 " + made.ToString("N0") + resource.Unit + " · 반출 " + exported.ToString("N0") + resource.Unit, ref y);
                }
                AddBanner("선택 목표 · 제어 장치 3개 반출", GoalText(state), ref y);
                AddBanner("공정 읽는 법", "원료 추출 → 1차 가공 → 부품 → 고급 부품 → 제어 장치. 재활용 제조법은 순환할 수 있으므로 도감의 투입·산출 비율을 확인하세요. 부산물 저장 공간이 가득 차면 전체 배치가 멈춥니다.", ref y);
            }
            content.sizeDelta = new Vector2(0, Mathf.Max(1, y));
        }

        static string GoalText(FactoryState state)
        {
            int exported = Inventory(state.Exported, Resource.ControlUnit);
            int produced = Inventory(state.Produced, Resource.ControlUnit);
            return "진행 " + Mathf.Min(3, exported) + "/3 · 누적 생산 " + produced + "개 · 남은 반출 " + Mathf.Max(0, 3 - exported) + "개 (선택 목표, 경제 효과 없음)";
        }

        void AddRow(string name, string title, string body, ref float y)
        {
            RectTransform row = ResearchUi.Panel(name, content, HudStyle.SurfaceRaised);
            ResearchUi.Place(row, 0, y, 100, 58); StretchWidth(row, 0, 0);
            Text a = ResearchUi.Label("Name", row, title, HudStyle.BodySize, HudStyle.Text, TextAnchor.MiddleLeft);
            ResearchUi.Place(a.rectTransform, 12, 4, 190, 50);
            Text b = ResearchUi.Label("Details", row, body, HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.MiddleRight);
            b.rectTransform.anchorMin = new Vector2(0, 0); b.rectTransform.anchorMax = Vector2.one;
            b.rectTransform.offsetMin = new Vector2(210, 4); b.rectTransform.offsetMax = new Vector2(-12, -4);
            y += 66;
        }

        void AddBanner(string title, string body, ref float y)
        {
            RectTransform row = ResearchUi.Panel("IndustryBanner", content, HudStyle.SurfaceRaised);
            ResearchUi.Place(row, 0, y, 100, 82); StretchWidth(row, 0, 0);
            Text a = ResearchUi.Label("Title", row, title, HudStyle.BodySize, HudStyle.Accent, TextAnchor.UpperLeft);
            ResearchUi.Place(a.rectTransform, 12, 9, 100, 22); StretchWidth(a.rectTransform, 12, 12);
            Text b = ResearchUi.Label("Body", row, body, HudStyle.BodySize, HudStyle.Text, TextAnchor.UpperLeft);
            ResearchUi.Place(b.rectTransform, 12, 34, 100, 40); StretchWidth(b.rectTransform, 12, 12);
            y += 90;
        }

        void AddEmpty(string text, ref float y)
        {
            Text label = ResearchUi.Label("IndustryEmpty", content, text, HudStyle.BodySize, HudStyle.TextMuted, TextAnchor.MiddleCenter);
            ResearchUi.Place(label.rectTransform, 0, y, 100, 80); StretchWidth(label.rectTransform, 0, 0); y += 80;
        }

        void ApplyLayout()
        {
            if (window == null) return;
            RectTransform root = transform as RectTransform;
            float w = Mathf.Clamp(root.rect.width - 32, 280, 1080), h = Mathf.Clamp(root.rect.height - 32, 240, 760);
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero; window.sizeDelta = new Vector2(w, h);
            ResearchUi.Place(heading.rectTransform, 24, 12, Mathf.Max(120, w - 90), 44);
            ResearchUi.Place(window.Find("Button_IndustryClose") as RectTransform, w - 60, 12, 44, 44);
            float tabWidth = Mathf.Max(86, Mathf.Min(138, (w - 48) / 3));
            ResearchUi.Place(recipesTab.transform as RectTransform, 24, 64, tabWidth, 44);
            ResearchUi.Place(resourcesTab.transform as RectTransform, 30 + tabWidth, 64, tabWidth, 44);
            ResearchUi.Place(productionTab.transform as RectTransform, 36 + tabWidth * 2, 64, tabWidth, 44);
            ResearchUi.Place(searchRect, 24, 116, Mathf.Min(330, w - 48), 44);
            summary.rectTransform.anchorMin = summary.rectTransform.anchorMax = new Vector2(1, 1);
            summary.rectTransform.pivot = new Vector2(1, 1); summary.rectTransform.anchoredPosition = new Vector2(-24, -122);
            summary.rectTransform.sizeDelta = new Vector2(Mathf.Max(1, w - 390), 32);
            float top = page == Page.Production ? 116 : 168;
            ResearchUi.Place(viewport, 24, top, w - 48, h - top - 20);
        }

        static void StretchWidth(RectTransform rect, float left, float right)
        {
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2((left - right) * .5f, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(-(left + right), rect.sizeDelta.y);
        }

        static string Amounts(IEnumerable<RecipeAmount> amounts) => amounts == null ? "없음" :
            string.Join(" + ", amounts.Select(a => (ResourceCatalog.Get(a.Resource)?.Name ?? a.Resource.ToString()) + " " + a.Amount + (ResourceCatalog.Get(a.Resource)?.Unit ?? "")));
        static string RateAmounts(IEnumerable<RecipeAmount> amounts, float multiplier) => amounts == null ? "없음" :
            string.Join(" + ", amounts.Select(a => (ResourceCatalog.Get(a.Resource)?.Name ?? a.Resource.ToString()) + " " + (a.Amount * multiplier).ToString("0.#") + (ResourceCatalog.Get(a.Resource)?.Unit ?? "")));
        static float BasePower(RecipeSpec recipe) => recipe.Machines.Select(k => FactoryCatalog.Get(k)?.PowerDemand ?? 0).DefaultIfEmpty(0).Min();
        static int Inventory(IList<int> values, Resource resource) => values != null && (int)resource >= 0 && (int)resource < values.Count ? values[(int)resource] : 0;

        void ClearContent()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Runtime-built control room for the physical factory interior.</summary>
    public sealed class FactoryHud : MonoBehaviour
    {
        const int ResourceCount = 9;
        static readonly Color Navy = Hex("101B2B");
        static readonly Color Navy2 = Hex("192A3E");
        static readonly Color Navy3 = Hex("243A51");
        static readonly Color Brass = Hex("D7A928");
        static readonly Color BrassDark = Hex("8A6819");
        static readonly Color Cream = Hex("F3EBD8");
        static readonly Color Muted = Hex("9EACB8");
        static readonly Color Teal = Hex("43B5A0");
        static readonly Color Red = Hex("C75A4A");
        static readonly Color Green = Hex("62B47A");

        FactoryController controller;
        Font font;
        Sprite rounded;
        Text modeBadge, economyText, telemetryText, toolInfo, directionText, noticeText, blueprintText;
        Text inspectorTitle, inspectorStatus, inventoryText, cargoText, progressText, flowText;
        Image progressFill;
        Button practiceButton, realButton, rotateButton, removeButton;
        GameObject toolbarPanel, inspectorPanel, blueprintPanel;
        RectTransform safeAreaRoot;
        Rect lastSafeArea;
        int lastSafeScreenWidth = -1, lastSafeScreenHeight = -1;
        bool mobileToolsOpen = true, mobileInspectorOpen;
        readonly Dictionary<FactoryKind, Button> toolButtons = new Dictionary<FactoryKind, Button>();
        readonly Dictionary<FactoryKind, Text> toolLabels = new Dictionary<FactoryKind, Text>();
        readonly Dictionary<FactoryRecipe, Button> recipeButtons = new Dictionary<FactoryRecipe, Button>();
        readonly Dictionary<Resource, Button> filterButtons = new Dictionary<Resource, Button>();
        readonly Dictionary<Resource, Button> feedButtons = new Dictionary<Resource, Button>();
        readonly List<Button> speedButtons = new List<Button>();

        public void Initialize(FactoryController value)
        {
            if (controller != null) controller.Changed -= Refresh;
            controller = value;
            if (controller == null) return;
            BuildInterface();
            controller.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (controller != null) controller.Changed -= Refresh;
            if (rounded != null)
            {
                Texture2D texture = rounded.texture;
                Destroy(rounded);
                Destroy(texture);
            }
        }

        void BuildInterface()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            safeAreaRoot = null;
            toolButtons.Clear(); toolLabels.Clear(); recipeButtons.Clear(); filterButtons.Clear(); feedButtons.Clear(); speedButtons.Clear();
            font = GameFont.Load();
            rounded = MakeRoundedSprite();

            Canvas canvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            CanvasScaler scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Application.isMobilePlatform ? new Vector2(1280, 720) : new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }

            BuildTopBar();
            BuildToolbar();
            BuildInspector();
            BuildBottomBar();
            if (Application.isMobilePlatform) { BuildMobileControls(); ApplyMobileSafeArea(); }
        }

        void BuildTopBar()
        {
            if (Application.isMobilePlatform) { BuildMobileTopBar(); return; }
            GameObject top = Panel("FactoryTopBar", transform, Navy, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -150), Vector2.zero);
            GameObject brand = Box("Brand", top.transform, new Vector2(16, -14), new Vector2(242, 120), Navy2);
            LabelAt("FACTORY", brand.transform, 30, Brass, FontStyle.Bold, new Vector2(16, -15), new Vector2(205, 37));
            LabelAt("공장 설계", brand.transform, 16, Cream, FontStyle.Bold, new Vector2(17, -53), new Vector2(200, 25));
            modeBadge = LabelAt("", brand.transform, 11, Cream, FontStyle.Bold, new Vector2(17, -86), new Vector2(205, 22));

            economyText = LabelAt("", top.transform, 17, Cream, FontStyle.Bold, new Vector2(282, -24), new Vector2(270, 58));
            telemetryText = LabelAt("", top.transform, 12, Muted, FontStyle.Bold, new Vector2(565, -18), new Vector2(400, 118));

            blueprintPanel = Box("FactoryBlueprintChooser", top.transform, new Vector2(282, -87), new Vector2(270, 55), Navy2);
            MakeButton("‹", blueprintPanel.transform, new Vector2(6, -7), new Vector2(34, 41), Navy3, () => SelectBlueprintRelative(-1), "FactoryButton_BlueprintPrevious", 18);
            blueprintText = LabelAt("", blueprintPanel.transform, 10, Cream, FontStyle.Bold, new Vector2(45, -5), new Vector2(180, 45));
            blueprintText.alignment = TextAnchor.UpperCenter; blueprintText.resizeTextForBestFit = true; blueprintText.resizeTextMinSize = 8; blueprintText.resizeTextMaxSize = 10;
            MakeButton("›", blueprintPanel.transform, new Vector2(230, -7), new Vector2(34, 41), Navy3, () => SelectBlueprintRelative(1), "FactoryButton_BlueprintNext", 18);

            practiceButton = MakeButton("연습 공장", top.transform, new Vector2(982, -18), new Vector2(126, 42), Navy3, () => controller.OpenPractice(), "FactoryButton_Practice", 13);
            realButton = MakeButton("실제 공장", top.transform, new Vector2(1117, -18), new Vector2(126, 42), Navy3, () => controller.OpenReal(), "FactoryButton_Real", 13);
            MakeButton("저장", top.transform, new Vector2(1252, -18), new Vector2(82, 42), Navy3, () => controller.Save(), "FactoryButton_Save", 13);
            MakeButton("도시로", top.transform, new Vector2(1343, -18), new Vector2(104, 42), BrassDark, () => controller.Close(), "FactoryButton_Close", 13);

            string[] labels = { "일시정지", "1×", "3×" };
            float[] speeds = { 0, 1, 3 };
            for (int i = 0; i < speeds.Length; i++)
            {
                float speed = speeds[i];
                Button button = MakeButton(labels[i], top.transform, new Vector2(982 + i * 88, -76), new Vector2(80, 38), Navy3, () => controller.SetSpeed(speed), "FactoryButton_Speed" + i, 12);
                speedButtons.Add(button);
            }
            LabelAt("연습 모드의 생산품과 수출은 도시에 반영되지 않습니다.", top.transform, 12, Muted, FontStyle.Normal, new Vector2(1260, -83), new Vector2(320, 38));
        }

        void BuildToolbar()
        {
            if (Application.isMobilePlatform) { BuildMobileToolbar(); return; }
            GameObject left = toolbarPanel = Panel("FactoryToolbar", transform, Navy, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 100), new Vector2(264, -158));
            LabelAt("설비 도구", left.transform, 18, Brass, FontStyle.Bold, new Vector2(16, -14), new Vector2(150, 28));
            rotateButton = MakeButton("회전  R", left.transform, new Vector2(157, -10), new Vector2(91, 34), Navy3, () => controller.Rotate(), "FactoryButton_Rotate", 11);

            FactoryKind[] kinds = Enum.GetValues(typeof(FactoryKind)).Cast<FactoryKind>().Where(k => k != FactoryKind.None).ToArray();
            for (int i = 0; i < kinds.Length; i++)
            {
                FactoryKind kind = kinds[i];
                FactorySpec spec = FactoryCatalog.Get(kind);
                string title = KindName(kind);
                string detail = string.Format("{0}×{1}  ¢{2}  목{3} 석{4}", spec.Width, spec.Height, spec.CoinCost, spec.TimberCost, spec.StoneCost);
                Button button = MakeButton(title + "\n" + detail, left.transform, new Vector2(14, -52 - i * 43), new Vector2(234, 38), Navy2, () => controller.SelectTool(kind), "FactoryButton_" + kind, 11);
                Text label = button.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                Rect(label.gameObject, Vector2.zero, Vector2.one, new Vector2(10, 2), new Vector2(-7, -2));
                toolButtons[kind] = button; toolLabels[kind] = label;
            }

            removeButton = MakeButton("철거 모드", left.transform, new Vector2(14, -526), new Vector2(234, 34), Red, () => controller.SelectRemove(), "FactoryButton_Remove", 12);
            directionText = LabelAt("", left.transform, 12, Cream, FontStyle.Bold, new Vector2(15, -563), new Vector2(232, 20));
            toolInfo = LabelAt("", left.transform, 11, Muted, FontStyle.Normal, new Vector2(15, -587), new Vector2(232, 48));
        }

        void BuildInspector()
        {
            GameObject right = inspectorPanel = Panel("FactoryInspector", transform, Navy, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-312, 100), new Vector2(0, -158));
            bool mobile = Application.isMobilePlatform;
            Transform content = right.transform;
            if (mobile)
            {
                Rect(right, new Vector2(1,0), new Vector2(1,1), new Vector2(-360,82), new Vector2(0,-88));
                GameObject viewport = new GameObject("FactoryInspectorScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
                viewport.transform.SetParent(right.transform, false); Rect(viewport, Vector2.zero, Vector2.one, new Vector2(6,6), new Vector2(-6,-6));
                Image viewportImage = viewport.GetComponent<Image>(); viewportImage.color = new Color(0,0,0,0); viewportImage.raycastTarget = true;
                viewport.GetComponent<Mask>().showMaskGraphic = false;
                GameObject contentObject = new GameObject("FactoryInspectorContent", typeof(RectTransform)); contentObject.transform.SetParent(viewport.transform, false);
                RectTransform contentRect = contentObject.GetComponent<RectTransform>(); contentRect.anchorMin = new Vector2(0,1); contentRect.anchorMax = new Vector2(1,1); contentRect.pivot = new Vector2(.5f,1); contentRect.anchoredPosition = Vector2.zero; contentRect.sizeDelta = new Vector2(0,680);
                ScrollRect scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = contentRect; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36; scroll.verticalNormalizedPosition = 1;
                content = contentObject.transform;
            }
            inspectorTitle = LabelAt("설비 검사기", content, 18, Brass, FontStyle.Bold, new Vector2(16, -14), new Vector2(280, 30));
            inspectorStatus = LabelAt("설비를 선택하세요.", content, 13, Cream, FontStyle.Bold, new Vector2(16, -49), new Vector2(280, 52));
            inventoryText = LabelAt("", content, 12, Muted, FontStyle.Normal, new Vector2(16, -108), new Vector2(280, 102));
            cargoText = LabelAt("", content, 12, Cream, FontStyle.Bold, new Vector2(16, -215), new Vector2(280, 44));
            progressText = LabelAt("진행 0%", content, 11, Muted, FontStyle.Bold, new Vector2(16, -260), new Vector2(280, 20));
            GameObject progress = Box("RecipeProgress", content, new Vector2(16, -284), new Vector2(280, 12), Navy3);
            GameObject fill = Box("Fill", progress.transform, Vector2.zero, Vector2.zero, Brass);
            progressFill = fill.GetComponent<Image>();
            Rect(fill, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero);

            LabelAt("생산 레시피", content, 13, Brass, FontStyle.Bold, new Vector2(16, -312), new Vector2(280, 22));
            FactoryRecipe[] recipes = { FactoryRecipe.IronPlate, FactoryRecipe.Tools, FactoryRecipe.Flour, FactoryRecipe.Bread };
            for (int i = 0; i < recipes.Length; i++)
            {
                FactoryRecipe recipe = recipes[i];
                float y = mobile ? -340 - (i / 2) * 52 : -340 - (i / 2) * 38;
                Button button = MakeButton(FactoryCatalog.RecipeName(recipe), content, new Vector2(16 + (i % 2) * 143, y), new Vector2(137, mobile ? 48 : 32), Navy3, () => controller.ChooseRecipe(recipe), "FactoryButton_Recipe_" + recipe, mobile ? 11 : 10);
                recipeButtons[recipe] = button;
            }

            float filterLabelY = mobile ? -450 : -422;
            float filterButtonY = mobile ? -478 : -450;
            LabelAt("투입기 필터", content, 13, Brass, FontStyle.Bold, new Vector2(16, filterLabelY), new Vector2(280, 22));
            Resource[] filters = { Resource.Coins, Resource.Ore, Resource.Steel, Resource.Timber };
            for (int i = 0; i < filters.Length; i++)
            {
                Resource resource = filters[i];
                Button button = MakeButton(resource == Resource.Coins ? "전체" : ResourceName(resource), content, new Vector2(16 + i * 71, filterButtonY), new Vector2(66, mobile ? 48 : 31), Navy3, () => controller.ChooseFilter(resource), "FactoryButton_Filter_" + resource, mobile ? 11 : 10);
                filterButtons[resource] = button;
            }

            float feedLabelY = mobile ? -537 : -497;
            LabelAt("보관함 직접 투입  +10", content, 13, Brass, FontStyle.Bold, new Vector2(16, feedLabelY), new Vector2(280, 22));
            Resource[] feeds = { Resource.Timber, Resource.Stone, Resource.Grain, Resource.Flour, Resource.Bread, Resource.Ore, Resource.Steel, Resource.Tools };
            for (int i = 0; i < feeds.Length; i++)
            {
                Resource resource = feeds[i];
                float y = mobile ? -565 - (i / 4) * 52 : -525 - (i / 4) * 36;
                Button button = MakeButton(ResourceName(resource), content, new Vector2(16 + (i % 4) * 71, y), new Vector2(66, mobile ? 48 : 30), Navy3, () => controller.Feed(resource, 10), "FactoryButton_Feed_" + resource, mobile ? 11 : 10);
                feedButtons[resource] = button;
            }
        }

        void BuildBottomBar()
        {
            if (Application.isMobilePlatform)
            {
                GameObject bottomMobile = Panel("FactoryBottomBar", transform, Navy, new Vector2(0,0), new Vector2(1,0), Vector2.zero, new Vector2(0,72));
                flowText = LabelAt("채굴기 → 벨트 → 투입기 → 생산 → 수출", bottomMobile.transform, 13, Cream, FontStyle.Bold, new Vector2(18,-8), new Vector2(760,22));
                noticeText = LabelAt("", bottomMobile.transform, 11, Muted, FontStyle.Bold, new Vector2(18,-32), new Vector2(1244,36));
                RectTransform noticeRect=noticeText.rectTransform;noticeRect.anchorMin=new Vector2(0,1);noticeRect.anchorMax=new Vector2(1,1);noticeRect.offsetMin=new Vector2(18,-68);noticeRect.offsetMax=new Vector2(-18,-32);
                return;
            }
            GameObject bottom = Panel("FactoryBottomBar", transform, Navy, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 92));
            LabelAt("물류 흐름", bottom.transform, 12, Brass, FontStyle.Bold, new Vector2(18, -12), new Vector2(110, 22));
            flowText = LabelAt("채굴기 → 벨트 → 투입기 → 제련/조립 → 투입기 → 수출 부두", bottom.transform, 14, Cream, FontStyle.Bold, new Vector2(125, -11), new Vector2(840, 24));
            LabelAt("좌클릭 설치/선택 · 철거 모드에서 좌클릭 제거 · R 설비 방향 전환 · Q/E 카메라 · 휠 확대", bottom.transform, 11, Muted, FontStyle.Normal, new Vector2(18, -45), new Vector2(930, 23));
            noticeText = LabelAt("", bottom.transform, 13, Cream, FontStyle.Bold, new Vector2(975, -14), new Vector2(595, 56));
        }

        void BuildMobileTopBar()
        {
            GameObject top = Panel("FactoryTopBar", transform, Navy, new Vector2(0,1), new Vector2(1,1), new Vector2(0,-80), Vector2.zero);
            LabelAt("FACTORY", top.transform, 22, Brass, FontStyle.Bold, new Vector2(16,-9), new Vector2(132,30));
            modeBadge = LabelAt("", top.transform, 11, Cream, FontStyle.Bold, new Vector2(16,-42), new Vector2(220,22));
            economyText = LabelAt("", top.transform, 12, Cream, FontStyle.Bold, new Vector2(245,-10), new Vector2(210,55));
            telemetryText = LabelAt("", top.transform, 11, Muted, FontStyle.Bold, new Vector2(460,-10), new Vector2(300,55));
            practiceButton = MakeButton("연습", top.transform, new Vector2(770,-14), new Vector2(76,48), Navy3, () => controller.OpenPractice(), "FactoryButton_Practice", 12);
            realButton = MakeButton("실제", top.transform, new Vector2(852,-14), new Vector2(76,48), Navy3, () => controller.OpenReal(), "FactoryButton_Real", 12);
            MakeButton("저장", top.transform, new Vector2(934,-14), new Vector2(76,48), Navy3, () => controller.Save(), "FactoryButton_Save", 12);
            MakeButton("도시로", top.transform, new Vector2(1016,-14), new Vector2(94,48), BrassDark, () => controller.Close(), "FactoryButton_Close", 13);
            string[] labels = { "Ⅱ", "1×", "3×" }; float[] speeds = { 0,1,3 };
            for (int i=0;i<3;i++) { float speed=speeds[i]; Button b=MakeButton(labels[i],top.transform,new Vector2(1116+i*52,-14),new Vector2(48,48),Navy3,()=>controller.SetSpeed(speed),"FactoryButton_Speed"+i,13); speedButtons.Add(b); }
        }

        void BuildMobileToolbar()
        {
            toolbarPanel = Panel("FactoryToolbar", transform, Navy, new Vector2(0,0), new Vector2(1,0), new Vector2(0,72), new Vector2(0,230));
            GameObject viewport = new GameObject("FactoryToolScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewport.transform.SetParent(toolbarPanel.transform,false); Rect(viewport,new Vector2(0,0),new Vector2(1,1),new Vector2(12,12),new Vector2(-12,-58));
            Image viewportImage=viewport.GetComponent<Image>(); viewportImage.color=new Color(0,0,0,0); viewportImage.raycastTarget=true;
            viewport.GetComponent<Mask>().showMaskGraphic=false;
            GameObject content=new GameObject("FactoryToolContent",typeof(RectTransform),typeof(HorizontalLayoutGroup),typeof(ContentSizeFitter)); content.transform.SetParent(viewport.transform,false);
            RectTransform contentRect=content.GetComponent<RectTransform>(); contentRect.anchorMin=new Vector2(0,0);contentRect.anchorMax=new Vector2(0,1);contentRect.pivot=new Vector2(0,.5f);contentRect.offsetMin=Vector2.zero;contentRect.offsetMax=Vector2.zero;
            HorizontalLayoutGroup layout=content.GetComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.padding=new RectOffset(2,2,2,2);layout.childForceExpandWidth=false;layout.childForceExpandHeight=true;
            ContentSizeFitter fitter=content.GetComponent<ContentSizeFitter>();fitter.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll=viewport.GetComponent<ScrollRect>();scroll.content=contentRect;scroll.horizontal=true;scroll.vertical=false;scroll.movementType=ScrollRect.MovementType.Elastic;scroll.scrollSensitivity=32;
            blueprintPanel=Box("FactoryBlueprintChooser",content.transform,Vector2.zero,new Vector2(250,72),Navy2);
            LayoutElement blueprintLayout=blueprintPanel.AddComponent<LayoutElement>();blueprintLayout.preferredWidth=250;blueprintLayout.minWidth=250;
            MakeButton("‹",blueprintPanel.transform,new Vector2(6,-8),new Vector2(34,54),Navy3,()=>SelectBlueprintRelative(-1),"FactoryButton_BlueprintPrevious",18);
            blueprintText=LabelAt("",blueprintPanel.transform,10,Cream,FontStyle.Bold,new Vector2(44,-5),new Vector2(162,59));blueprintText.alignment=TextAnchor.UpperCenter;blueprintText.resizeTextForBestFit=true;blueprintText.resizeTextMinSize=8;blueprintText.resizeTextMaxSize=10;
            MakeButton("›",blueprintPanel.transform,new Vector2(210,-8),new Vector2(34,54),Navy3,()=>SelectBlueprintRelative(1),"FactoryButton_BlueprintNext",18);
            foreach (FactoryKind kind in Enum.GetValues(typeof(FactoryKind)).Cast<FactoryKind>().Where(k=>k!=FactoryKind.None))
            {
                FactorySpec spec=FactoryCatalog.Get(kind); Button b=MakeButton(KindName(kind)+"\n"+spec.Width+"×"+spec.Height,content.transform,Vector2.zero,new Vector2(126,72),Navy2,()=>controller.SelectTool(kind),"FactoryButton_"+kind,12);
                LayoutElement le=b.gameObject.AddComponent<LayoutElement>();le.preferredWidth=126;le.minWidth=126; toolButtons[kind]=b;toolLabels[kind]=b.GetComponentInChildren<Text>();
            }
            rotateButton=MakeButton("↻ 회전",toolbarPanel.transform,new Vector2(12,-10),new Vector2(96,48),Navy3,()=>controller.Rotate(),"FactoryButton_Rotate",12);
            removeButton=MakeButton("철거",toolbarPanel.transform,new Vector2(114,-10),new Vector2(88,48),Red,()=>controller.SelectRemove(),"FactoryButton_Remove",12);
            MakeButton("취소",toolbarPanel.transform,new Vector2(208,-10),new Vector2(88,48),Navy3,()=>controller.SelectTool(FactoryKind.None),"FactoryButton_Cancel",12);
            directionText=LabelAt("",toolbarPanel.transform,12,Cream,FontStyle.Bold,new Vector2(310,-17),new Vector2(230,30));
            toolInfo=LabelAt("",toolbarPanel.transform,11,Muted,FontStyle.Normal,new Vector2(540,-10),new Vector2(610,44));
        }

        void BuildMobileControls()
        {
            Button tools=MakeButton("설비 ▲",transform,new Vector2(12,-88),new Vector2(104,48),BrassDark,()=>{mobileToolsOpen=!mobileToolsOpen;toolbarPanel.SetActive(mobileToolsOpen);UpdateMobileViewport();},"FactoryButton_ToolsDrawer",13);
            Button inspect=MakeButton("검사기",transform,new Vector2(-12,-88),new Vector2(104,48),Navy3,()=>{mobileInspectorOpen=!mobileInspectorOpen;inspectorPanel.SetActive(mobileInspectorOpen);UpdateMobileViewport();},"FactoryButton_InspectorDrawer",13);
            tools.GetComponent<RectTransform>().anchorMin=tools.GetComponent<RectTransform>().anchorMax=new Vector2(0,1);
            inspect.GetComponent<RectTransform>().anchorMin=inspect.GetComponent<RectTransform>().anchorMax=new Vector2(1,1);inspect.GetComponent<RectTransform>().pivot=new Vector2(1,1);
            inspectorPanel.SetActive(false); UpdateMobileViewport();
        }

        void UpdateMobileViewport()
        {
            if (controller==null || controller.View==null || controller.View.Camera==null) return;
            float bottom=mobileToolsOpen ? 230f/720f : 72f/720f;
            float right=mobileInspectorOpen ? 360f/1280f : 0f;
            controller.View.Camera.rect=new Rect(0,bottom,1-right,Mathf.Max(.1f,1f-bottom-80f/720f));
        }

        void Update()
        {
            if (Application.isMobilePlatform) UpdateMobileSafeArea();
        }

        void ApplyMobileSafeArea()
        {
            GameObject root=new GameObject("FactorySafeArea",typeof(RectTransform));root.transform.SetParent(transform,false);
            safeAreaRoot=root.GetComponent<RectTransform>();safeAreaRoot.offsetMin=safeAreaRoot.offsetMax=Vector2.zero;
            List<Transform> children=new List<Transform>();foreach(Transform child in transform)if(child!=root.transform&&child.GetComponent<EventSystem>()==null)children.Add(child);
            foreach(Transform child in children)child.SetParent(root.transform,false);
            lastSafeScreenWidth=lastSafeScreenHeight=-1;UpdateMobileSafeArea();
        }

        void UpdateMobileSafeArea()
        {
            if(safeAreaRoot==null||Screen.width<=0||Screen.height<=0)return;
            Rect safe=Screen.safeArea;
            if(lastSafeScreenWidth==Screen.width&&lastSafeScreenHeight==Screen.height&&lastSafeArea==safe)return;
            safeAreaRoot.anchorMin=new Vector2(safe.xMin/Screen.width,safe.yMin/Screen.height);
            safeAreaRoot.anchorMax=new Vector2(safe.xMax/Screen.width,safe.yMax/Screen.height);
            safeAreaRoot.offsetMin=safeAreaRoot.offsetMax=Vector2.zero;
            lastSafeArea=safe;lastSafeScreenWidth=Screen.width;lastSafeScreenHeight=Screen.height;
            UpdateMobileViewport();
        }

        void Refresh()
        {
            if (controller == null || controller.Sim == null) return;
            FactoryState state = controller.State;
            bool practice = controller.IsPractice;
            modeBadge.text = practice ? "◆ 연습 모드 · 무제한 설계" : "◆ 실제 도시 공장";
            modeBadge.color = practice ? Brass : Teal;
            economyText.text = practice ? "자원 비용 없음\n도시와 독립된 시험 설비" : string.Format("도시 코인  {0:N0}\n목재 {1:N0} · 석재 {2:N0}", controller.Game.State.Coins, controller.Game.Sim.Get(Resource.Timber), controller.Game.Sim.Get(Resource.Stone));
            float cityPower = Mathf.Max(0, controller.Game.Sim.PowerCapacity - controller.Game.Sim.PowerUsed);
            if (Application.isMobilePlatform)
                telemetryText.text = string.Format("전력 {0:0.##}/{1:0.##} · {2}\n속도 벨트 {3} · 투입 {4} · 기계 {5}\n이동 {6} · 생산 {7:N0} · 수출 {8:N0}",
                    controller.Sim.PowerUsed, controller.Sim.PowerAvailable, practice ? "도시 전력 미사용" : "도시 여유 " + cityPower.ToString("0.##"),
                    Multiplier(controller.Sim.BeltSpeedMultiplier), Multiplier(controller.Sim.InserterSpeedMultiplier), Multiplier(controller.Sim.MachineSpeedMultiplier),
                    controller.Sim.MovingItems, Sum(state.Produced), Sum(state.Exported));
            else
                telemetryText.text = string.Format("공장 전력 사용 {0:0.##} / 공급 {1:0.##}\n{2}\n기술 효과  벨트 {3} · 투입기 {4}\n기계 {5} · 전력 수요 {6}   |   이동 {7} · 생산 {8:N0} · 수출 {9:N0}",
                    controller.Sim.PowerUsed, controller.Sim.PowerAvailable, practice ? "외부 도시 전력 미사용" : "도시 여유 전력 " + cityPower.ToString("0.##"),
                    Multiplier(controller.Sim.BeltSpeedMultiplier), Multiplier(controller.Sim.InserterSpeedMultiplier), Multiplier(controller.Sim.MachineSpeedMultiplier), Multiplier(controller.Sim.PowerDemandMultiplier),
                    controller.Sim.MovingItems, Sum(state.Produced), Sum(state.Exported));
            RefreshBlueprintChooser(practice);
            SetButtonColor(practiceButton, practice ? BrassDark : Navy3);
            SetButtonColor(realButton, practice ? Navy3 : Teal);

            FactoryKind selectedTool = controller.SelectedTool;
            foreach (var pair in toolButtons)
            {
                FactorySpec spec = FactoryCatalog.Get(pair.Key);
                bool tech = practice || spec.RequiredTech == TechId.None || controller.Game.State.Technologies.Contains(spec.RequiredTech);
                pair.Value.interactable = tech;
                SetButtonColor(pair.Value, selectedTool == pair.Key && !controller.RemovalMode ? BrassDark : Navy2);
                string gate = spec.RequiredTech == TechId.None ? "기본" : (tech ? "✓" : "잠금 ") + TechName(spec.RequiredTech);
                toolLabels[pair.Key].text = KindName(pair.Key) + "  [" + gate + "]\n" + string.Format("{0}×{1}  ¢{2}  목{3} 석{4}", spec.Width, spec.Height, spec.CoinCost, spec.TimberCost, spec.StoneCost);
            }
            SetButtonColor(removeButton, controller.RemovalMode ? BrassDark : Red);
            directionText.text = "방향  " + DirectionName(controller.Direction) + "   ·   R 회전";
            if (controller.RemovalMode) toolInfo.text = "철거할 설비를 선택하세요. 내부 물자는 회수되며 실제 공장은 건설비 일부를 돌려받습니다.";
            else if (selectedTool == FactoryKind.None) toolInfo.text = "왼쪽에서 설비를 고른 뒤 바닥을 클릭하세요. 벨트와 투입기의 화살표가 물자 이동 방향입니다.";
            else
            {
                FactorySpec spec = FactoryCatalog.Get(selectedTool);
                string gate = spec.RequiredTech == TechId.None ? "기술 제한 없음" : "필요 기술: " + TechName(spec.RequiredTech);
                toolInfo.text = spec.Description + "\n" + gate + " · 전력 " + spec.PowerDemand.ToString("0.##");
            }

            RefreshInspector(controller.SelectedEntity);
            noticeText.text = string.IsNullOrEmpty(controller.Notice) ? "공장 설비는 실제 물자를 한 칸씩 운반합니다. 막힌 출구에는 물자가 대기합니다." : controller.Notice;
            for (int i = 0; i < speedButtons.Count; i++)
            {
                float speed = i == 0 ? 0 : i == 1 ? 1 : 3;
                SetButtonColor(speedButtons[i], Mathf.Approximately(controller.Game.GameSpeed, speed) ? BrassDark : Navy3);
            }
        }

        void RefreshInspector(FactoryEntity entity)
        {
            bool practice=controller.IsPractice;
            bool selected = entity != null;
            inspectorTitle.text = selected ? KindName(entity.Kind) + "  #" + entity.Id : "설비 검사기";
            inspectorStatus.text = selected
                ? string.Format("좌표 ({0}, {1}) · {2}\n{3}  {4}", entity.X, entity.Z, DirectionName(entity.Direction), entity.Powered ? "● 전력 연결" : "○ 전력 없음", entity.Status)
                : "설비를 선택하면 입출력과 작동 상태를 확인할 수 있습니다.";
            inspectorStatus.color = selected && entity.Powered ? Green : Cream;
            inventoryText.text = selected ? "투입 재고\n" + Inventory(entity.Input) + "\n산출 재고\n" + Inventory(entity.Output) : "투입 재고\n—\n산출 재고\n—";
            cargoText.text = selected && entity.CargoResource != Resource.Coins ? "운반 중  " + ResourceName(entity.CargoResource) : "운반 중  없음";
            float duration = !selected ? 0 : entity.Kind == FactoryKind.Drill ? 2f : FactoryCatalog.RecipeDuration(entity.Recipe);
            float progress = selected && duration > 0 ? Mathf.Clamp01(entity.Progress / duration) : 0;
            progressText.text = selected ? "공정 진행  " + Mathf.RoundToInt(progress * 100) + "%" : "공정 진행  0%";
            RectTransform fill = progressFill.rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(progress, 1); fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;

            bool machine = selected && (entity.Kind == FactoryKind.Furnace || entity.Kind == FactoryKind.Assembler);
            foreach (var pair in recipeButtons)
            {
                bool compatible = machine && ((entity.Kind == FactoryKind.Furnace && pair.Key == FactoryRecipe.IronPlate) ||
                    (entity.Kind == FactoryKind.Assembler && pair.Key != FactoryRecipe.IronPlate));
                pair.Value.interactable = compatible;
                SetButtonColor(pair.Value, machine && entity.Recipe == pair.Key ? BrassDark : Navy3);
            }
            bool inserter = selected && entity.Kind == FactoryKind.Inserter;
            foreach (var pair in filterButtons)
            {
                pair.Value.interactable = inserter;
                SetButtonColor(pair.Value, inserter && entity.Filter == pair.Key ? BrassDark : Navy3);
            }
            bool canFeed = selected && (entity.Kind == FactoryKind.Storage || entity.Kind == FactoryKind.ImportDock);
            foreach (var pair in feedButtons)
            {
                float available = practice ? 0 : controller.Game.Sim.Get(pair.Key);
                pair.Value.interactable = canFeed && (practice || available >= 10);
                Text label = pair.Value.GetComponentInChildren<Text>();
                if (label != null) label.text = Application.isMobilePlatform
                    ? ResourceName(pair.Key) + "\n" + (practice ? "무제한" : available.ToString("0.#") + " 보유")
                    : ResourceName(pair.Key) + " " + (practice ? "∞" : available.ToString("0.#"));
            }

            if (!selected) flowText.text = "채굴기 → 벨트 → 투입기 → 제련/조립 → 투입기 → 수출 부두";
            else if (entity.Recipe != FactoryRecipe.None)
                flowText.text = FactoryCatalog.InputText(entity.Recipe) + "  →  " + FactoryCatalog.RecipeName(entity.Recipe) + "  →  " + FactoryCatalog.OutputText(entity.Recipe);
            else flowText.text = KindName(entity.Kind) + " · " + (string.IsNullOrEmpty(entity.Status) ? "대기" : entity.Status);
        }

        void RefreshBlueprintChooser(bool practice)
        {
            if (blueprintPanel == null || blueprintText == null) return;
            blueprintPanel.SetActive(practice);
            if (!practice) return;
            int count = FactoryBlueprints.Count;
            if (count <= 0) { blueprintText.text = "연습 청사진 없음"; return; }
            int index = Mathf.Clamp(controller.PracticeBlueprintIndex, 0, count - 1);
            blueprintText.text = string.Format("연습 청사진 {0}/{1} · {2}\n{3}", index + 1, count, FactoryBlueprints.Name(index), FactoryBlueprints.Description(index));
        }

        void SelectBlueprintRelative(int delta)
        {
            int count = FactoryBlueprints.Count;
            if (controller == null || !controller.IsPractice || count <= 0) return;
            int index = ((controller.PracticeBlueprintIndex + delta) % count + count) % count;
            controller.SelectBlueprint(index);
        }

        static int Sum(List<int> values)
        {
            if (values == null) return 0;
            int total = 0; for (int i = 1; i < values.Count; i++) total += Math.Max(0, values[i]); return total;
        }

        static string Multiplier(float value) => value.ToString("0.##") + "×";

        static string Inventory(List<int> values)
        {
            if (values == null) return "—";
            List<string> entries = new List<string>();
            for (int i = 1; i < values.Count && i < ResourceCount; i++)
                if (values[i] > 0) entries.Add(ResourceName((Resource)i) + " " + values[i]);
            return entries.Count == 0 ? "—" : string.Join(" · ", entries);
        }

        static string KindName(FactoryKind kind)
        {
            switch (kind)
            {
                case FactoryKind.Belt: return "운송 벨트"; case FactoryKind.Inserter: return "투입기"; case FactoryKind.Drill: return "채굴기";
                case FactoryKind.Furnace: return "용광로"; case FactoryKind.Assembler: return "조립기"; case FactoryKind.Storage: return "보관함";
                case FactoryKind.ImportDock: return "반입 부두"; case FactoryKind.ExportDock: return "수출 부두"; case FactoryKind.PowerInlet: return "전력 인입구";
                case FactoryKind.Pole: return "전신주"; case FactoryKind.Splitter: return "분배기"; default: return "선택 없음";
            }
        }

        static string ResourceName(Resource resource)
        {
            switch (resource)
            {
                case Resource.Timber: return "목재"; case Resource.Stone: return "석재"; case Resource.Grain: return "곡물"; case Resource.Flour: return "밀가루";
                case Resource.Bread: return "빵"; case Resource.Ore: return "철광석"; case Resource.Steel: return "철판"; case Resource.Tools: return "도구"; default: return "전체";
            }
        }

        static string TechName(TechId tech)
        {
            switch (tech)
            {
                case TechId.CropRotation: return "윤작"; case TechId.Stonecraft: return "석조술"; case TechId.MechanicalPower: return "기계 동력";
                case TechId.Guilds: return "길드"; case TechId.Metallurgy: return "야금술"; case TechId.Scholarship: return "학문";
                case TechId.Toolmaking: return "도구 제작"; case TechId.SteamPower: return "증기 동력"; case TechId.UrbanPlanning: return "도시 계획";
                case TechId.Forestry: return "산림 경영"; case TechId.Irrigation: return "관개"; case TechId.Masonry: return "석공술";
                case TechId.Logistics: return "물류"; case TechId.Education: return "교육"; case TechId.MetallurgicalEfficiency: return "금속 공정 효율";
                case TechId.Electrification: return "전기화"; case TechId.MassProduction: return "대량 생산"; case TechId.Automation: return "자동화"; default: return "없음";
            }
        }

        static string DirectionName(int direction)
        {
            switch ((direction % 4 + 4) % 4) { case 0: return "동쪽 →"; case 1: return "북쪽 ↑"; case 2: return "서쪽 ←"; default: return "남쪽 ↓"; }
        }

        GameObject Panel(string name, Transform parent, Color color, Vector2 amin, Vector2 amax, Vector2 offMin, Vector2 offMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = true; Rect(go, amin, amax, offMin, offMax); return go;
        }

        GameObject Box(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>(); image.sprite = rounded; image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = true;
            Rect(go, new Vector2(0, 1), new Vector2(0, 1), pos, size); go.GetComponent<RectTransform>().pivot = new Vector2(0, 1); return go;
        }

        Text LabelAt(string value, Transform parent, int size, Color color, FontStyle style, Vector2 pos, Vector2 dimensions)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>(); text.text = value; text.font = font; text.fontSize = size; text.color = color; text.fontStyle = style;
            text.alignment = TextAnchor.UpperLeft; text.supportRichText = true; text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            Rect(go, new Vector2(0, 1), new Vector2(0, 1), pos, dimensions); go.GetComponent<RectTransform>().pivot = new Vector2(0, 1); return text;
        }

        Button MakeButton(string value, Transform parent, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction click, string objectName, int textSize)
        {
            GameObject go = Box(objectName, parent, pos, size, color); Button button = go.AddComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            ColorBlock colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1); colors.pressedColor = new Color(.75f, .75f, .75f, 1); colors.disabledColor = new Color(.46f, .46f, .46f, .72f); button.colors = colors;
            button.onClick.AddListener(click);
            Text label = LabelAt(value, go.transform, textSize, Cream, FontStyle.Bold, Vector2.zero, Vector2.zero); label.alignment = TextAnchor.MiddleCenter;
            Rect(label.gameObject, Vector2.zero, Vector2.one, new Vector2(5, 2), new Vector2(-5, -2)); return button;
        }

        static void Rect(GameObject go, Vector2 amin, Vector2 amax, Vector2 offsetOrPos, Vector2 sizeOrOffset)
        {
            RectTransform rt = go.GetComponent<RectTransform>(); rt.anchorMin = amin; rt.anchorMax = amax;
            if (amin == amax) { rt.anchoredPosition = offsetOrPos; rt.sizeDelta = sizeOrOffset; }
            else { rt.offsetMin = offsetOrPos; rt.offsetMax = sizeOrOffset; }
        }

        static void SetButtonColor(Button button, Color color) { if (button != null && button.targetGraphic != null) button.targetGraphic.color = color; }

        static Sprite MakeRoundedSprite()
        {
            const int n = 24, radius = 5; Texture2D texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
            texture.name = "Factory HUD Rounded UI"; texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x - (n - 1) * .5f) - (n * .5f - radius));
                float dy = Mathf.Max(0, Mathf.Abs(y - (n - 1) * .5f) - (n * .5f - radius));
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius + 1 - Mathf.Sqrt(dx * dx + dy * dy))));
            }
            texture.Apply(); return Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Color Hex(string value) { Color color; ColorUtility.TryParseHtmlString("#" + value, out color); return color; }
    }
}

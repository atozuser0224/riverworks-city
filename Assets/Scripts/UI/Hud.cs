using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Runtime-built, scene-independent interface for Riverworks.</summary>
    public sealed partial class Hud : MonoBehaviour
    {
        // Compact city-builder palette: the world remains the brightest layer.
        static readonly Color Navy = HudStyle.Surface;
        static readonly Color Navy2 = HudStyle.SurfaceRaised;
        static readonly Color Cream = HudStyle.Text;
        static readonly Color Paper = HudStyle.SurfaceRaised;
        static readonly Color Coral = HudStyle.Accent;
        static readonly Color Teal = HudStyle.Positive;
        static readonly Color Gold = HudStyle.Accent;
        static readonly Color Muted = HudStyle.TextMuted;
        static readonly Color Ink = HudStyle.Text;

        GameController controller;
        FactoryController subscribedFactory;
        FeelDirector subscribedFeel;
        Font font;
        Sprite rounded;
        readonly Dictionary<Resource, Text> resourceTexts = new Dictionary<Resource, Text>();
        readonly Dictionary<Resource, float> displayedResourceValues = new Dictionary<Resource, float>();
        readonly Dictionary<BuildingKind, Button> buildButtons = new Dictionary<BuildingKind, Button>();
        readonly Dictionary<BuildingKind, Text> buildLabels = new Dictionary<BuildingKind, Text>();
        readonly Dictionary<FactoryKind, Button> factoryButtons = new Dictionary<FactoryKind, Button>();
        readonly Dictionary<FactoryKind, Text> factoryLabels = new Dictionary<FactoryKind, Text>();
        readonly Dictionary<string, Button> categoryButtons = new Dictionary<string, Button>();
        readonly List<Button> regionButtons = new List<Button>();
        readonly List<Text> regionLabels = new List<Text>();
        readonly List<Button> speedButtons = new List<Button>();
        readonly Dictionary<Resource, Button> buyButtons = new Dictionary<Resource, Button>();
        readonly Dictionary<FactoryRecipe, Button> factoryRecipeButtons = new Dictionary<FactoryRecipe, Button>();
        readonly Dictionary<Resource, Button> factoryFilterButtons = new Dictionary<Resource, Button>();
        readonly Dictionary<Resource, Button> factoryFeedButtons = new Dictionary<Resource, Button>();
        readonly Dictionary<Resource, Text> factoryFeedLabels = new Dictionary<Resource, Text>();

        Text populationText, incomeText, eraText, researchSummaryText, objectiveTitle, objectiveBody, objectiveProgress, objectiveChipText;
        Text inspectorTitle, inspectorBody, noticeText, buildInfo, modeText, upgradeLabel, factoryConfigureSummary, overviewBody, feelModeLabel;
        Button upgradeButton, factoryConfigureButton, factoryRotateButton, buildCollapseButton, removeButton;
        Button factoryPauseButton, factoryRecoverButton, factoryAutomationButton, factoryFoundationButton, factoryLinkDownButton, factoryLinkUpButton;
        readonly Dictionary<int, Button> factoryClockButtons = new Dictionary<int, Button>();
        readonly Dictionary<int, Button> factoryFloorButtons = new Dictionary<int, Button>();
        GameObject objectivePanel, inspectorPanel, buildChoicesPanel, activeToolPanel;
        GameObject helpOverlay, confirmOverlay, researchOverlay, mobileTradeOverlay, factoryConfigureOverlay;
        GameObject menuOverlay, overviewOverlay, territoryOverlay;
        GameObject factoryRecipeSection, factoryFilterSection, factoryFeedSection, factoryFloorStrip;
        CanvasGroup noticeGroup;
        RectTransform buildViewport, buildScrollViewport, buildChoicesRect, buildTooltipRect, activeToolRect, activeToolContent, inspectorRowsRoot;
        readonly List<Text> inspectorRowLabels = new List<Text>();
        readonly List<Text> inspectorRowValues = new List<Text>();
        readonly Dictionary<Resource, GameObject> resourceChips = new Dictionary<Resource, GameObject>();
        readonly Dictionary<Resource, bool> stickyResourceVisibility = new Dictionary<Resource, bool>();
        GameObject buildTooltip;
        Text buildTooltipText;
        CanvasScaler canvasScaler;
        Canvas hudCanvas;
        GameState observedUiState;
        Rect currentSafePixels;
        int appliedUiWidth=-1, appliedUiHeight=-1, appliedUiScale=-1;
        RectTransform researchWindow;
        RectTransform factoryConfigureWindow;
        RectTransform mobileSafeAreaRoot;
        string category = "주거";
        string lastCityCategory = "주거";
        BuildingKind hoveredKind = BuildingKind.None;
        FactoryKind hoveredFactoryKind = FactoryKind.None;
        float noticeVisibleUntil;
        bool objectiveExpanded, buildTrayOpen, mobileTradeOpen, factoryModalOpen, menuOpen, overviewOpen, territoryOpen, confirmOpen, observedFactoryPaletteOpen;
        Rect appliedSafeArea;
        int appliedScreenWidth=-1, appliedScreenHeight=-1, lastNoticeVersion=-1;

        public ResearchTreeView ResearchTree { get; private set; }
        public IndustryPanel Industry { get; private set; }
        public AutomationRulePanel Automation { get; private set; }
        public CityProjectPanel Projects { get; private set; }

        public bool ConstructionBarVisible => (buildChoicesPanel!=null&&buildChoicesPanel.activeInHierarchy) || (activeToolPanel!=null&&activeToolPanel.activeInHierarchy);
        public float ConstructionBarHeight => buildChoicesPanel!=null&&buildChoicesPanel.activeInHierarchy ? 72f : activeToolPanel!=null&&activeToolPanel.activeInHierarchy ? 52f : 0f;

        public void Initialize(GameController value)
        {
            if (controller != null) controller.Changed -= Refresh;
            if (subscribedFeel != null) subscribedFeel.CuePlayed -= OnFeelCue;
            controller = value;
            if (controller == null) return;
            subscribedFeel=controller.Feel;
            if(subscribedFeel!=null)subscribedFeel.CuePlayed+=OnFeelCue;
            lastNoticeVersion=-1;
            displayedResourceValues.Clear();
            BuildInterface();
            controller.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (controller != null) controller.Changed -= Refresh;
            if (subscribedFactory != null) subscribedFactory.Changed -= OnFactoryChanged;
            if (subscribedFeel != null) subscribedFeel.CuePlayed -= OnFeelCue;
            if (controller != null) controller.ModalOpen = false;
            if (rounded != null) { Texture2D texture=rounded.texture; Destroy(rounded); Destroy(texture); }
        }

        void OnEnable()
        {
            if(controller!=null)Refresh();
        }

        void Update()
        {
            EnsureFactorySubscription();
            bool paletteOpen=controller!=null && controller.Factory!=null && controller.Factory.IsOpen;
            if(paletteOpen!=observedFactoryPaletteOpen)Refresh();
            if(factoryModalOpen && controller!=null && !controller.ModalOpen)
            {
                factoryModalOpen=false;
                RefreshModalOverlays();
            }
            if(controller!=null && !controller.ModalOpen && (menuOpen||overviewOpen||territoryOpen||mobileTradeOpen||confirmOpen))
            {
                menuOpen=overviewOpen=territoryOpen=mobileTradeOpen=confirmOpen=false;
                RefreshModalOverlays();
            }
            UpdateDisplayLayout(false);
            if (noticeGroup != null)
            {
                float target = Time.unscaledTime < noticeVisibleUntil ? 1f : 0f;
                noticeGroup.alpha = Mathf.MoveTowards(noticeGroup.alpha, target, Time.unscaledDeltaTime * 3f);
            }
        }

        void EnsureFactorySubscription()
        {
            FactoryController current=controller==null?null:controller.Factory;
            if(ReferenceEquals(current,subscribedFactory))return;
            if(subscribedFactory!=null)subscribedFactory.Changed-=OnFactoryChanged;
            subscribedFactory=current;
            if(subscribedFactory!=null)subscribedFactory.Changed+=OnFactoryChanged;
        }

        void OnFactoryChanged()
        {
            if(subscribedFactory!=null&&(subscribedFactory.IsOpen||subscribedFactory.SelectedEntity!=null||factoryModalOpen))Refresh();
        }

        void OnFeelCue(FeelCue cue)
        {
            if(cue==FeelCue.Denied)FeelUiFeedback.PlayLastDenied();
        }

        void SelectCategory(string value)
        {
            bool sameCategory=category==value;
            bool openRequested=!sameCategory||!buildTrayOpen;
            bool factoryCategory=IsFactoryCategory(value);
            if(openRequested)controller.ClearConstructionTools();
            if(factoryCategory)
            {
                if(controller.Factory==null)return;
                if(!controller.Factory.IsOpen)controller.Factory.Open(false);
                observedFactoryPaletteOpen=true;
            }
            else
            {
                lastCityCategory=value;
                if(controller.Factory!=null && controller.Factory.IsOpen)controller.Factory.Close();
                observedFactoryPaletteOpen=false;
            }
            category=value;
            buildTrayOpen=openRequested;
            hoveredKind=BuildingKind.None;hoveredFactoryKind=FactoryKind.None;
            RefreshBuildChoices();RefreshBuildInfo();controller.NotifyWorldSelection();
        }

        public void ToggleFactoryTools()=>SelectCategory("설비");

        void SelectCityTool(BuildingKind kind)
        {
            controller.SelectTool(kind);
            buildTrayOpen=false;
            Refresh();
        }

        void SelectFactoryTool(FactoryKind kind)
        {
            if(controller.Factory==null)return;
            if(!controller.Factory.IsOpen)controller.Factory.Open(false);
            observedFactoryPaletteOpen=true;
            controller.Factory.SelectTool(kind);
            category=FactoryCategory(kind);
            buildTrayOpen=false;
            Refresh();
        }

        void CancelTool()
        {
            if(controller.Factory!=null && controller.Factory.IsOpen)
            {
                controller.Factory.CancelTools();
                controller.NotifyWorldSelection();
            }
            else controller.SelectTool(BuildingKind.None);
            buildTrayOpen=false;
            Refresh();
        }

        void RotateFactoryTool()
        {
            if(controller.Factory==null || controller.Factory.SelectedTool==FactoryKind.None)return;
            controller.Factory.Rotate();RefreshBuildInfo();
        }

        void SelectRemoveTool()
        {
            if(controller.Factory!=null && controller.Factory.IsOpen)controller.Factory.SelectRemove();
            else controller.SelectDemolish();
            Refresh();
        }

        void ChooseFactoryRecipe(FactoryRecipe recipe)
        {
            if(controller.Factory==null)return;
            controller.Factory.ChooseRecipe(recipe);Refresh();
        }

        void ChooseFactoryFilter(Resource resource)
        {
            if(controller.Factory==null)return;
            controller.Factory.ChooseFilter(resource);Refresh();
        }

        void FeedFactory(Resource resource)
        {
            if(controller.Factory==null)return;
            controller.Factory.Feed(resource,10);Refresh();
        }

        public void OpenIndustryCodex()
        {
            CloseTransientPanels();
            Industry?.Open();
        }

        public void OpenAutomationRules()
        {
            FactoryEntity selected=controller==null?null:controller.SelectedFactory;
            if(selected==null)return;
            if((selected.Kind==FactoryKind.ItemLift||selected.Kind==FactoryKind.FluidRiser)&&!selected.IsLinkSender)return;
            CloseTransientPanels();
            Automation?.Open(selected.Id);
        }

        public void OpenCityProjects()
        {
            CloseTransientPanels();
            Projects?.Open();
        }

        void BuildInterface()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            font = LoadFont();
            rounded = MakeRoundedSprite();

            Canvas canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();hudCanvas=canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvasScaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            Vector2 initialPixels=HudStyle.PixelDimensions(canvas);
            canvasScaler.scaleFactor = HudStyle.IntegerScale(Mathf.RoundToInt(initialPixels.x),Mathf.RoundToInt(initialPixels.y),Screen.dpi,Application.isMobilePlatform);
            if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            BuildTopBar();
            BuildObjective();
            BuildInspector();
            BuildBottomBar();
            BuildOverlays();
            ApplyMobileSafeArea();
            UpdateDisplayLayout(true);
        }

        void BuildTopBar()
        {
            GameObject top = Panel("TopBar", transform, new Color(Navy.r,Navy.g,Navy.b,.98f), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -52), Vector2.zero);
            GameObject resourceViewport=new GameObject("ResourceViewport",typeof(RectTransform),typeof(RectMask2D),typeof(ScrollRect));
            resourceViewport.transform.SetParent(top.transform,false);
            Rect(resourceViewport,new Vector2(0,0),new Vector2(1,1),new Vector2(8,4),new Vector2(-216,-4));
            GameObject resourceContent=new GameObject("ResourceContent",typeof(RectTransform),typeof(HorizontalLayoutGroup),typeof(ContentSizeFitter));
            resourceContent.transform.SetParent(resourceViewport.transform,false);
            RectTransform resourceContentRect=resourceContent.GetComponent<RectTransform>();
            resourceContentRect.anchorMin=new Vector2(0,0);resourceContentRect.anchorMax=new Vector2(0,1);resourceContentRect.pivot=new Vector2(0,.5f);resourceContentRect.anchoredPosition=Vector2.zero;resourceContentRect.sizeDelta=Vector2.zero;
            HorizontalLayoutGroup resourceLayout=resourceContent.GetComponent<HorizontalLayoutGroup>();resourceLayout.spacing=4;resourceLayout.childForceExpandWidth=false;resourceLayout.childForceExpandHeight=true;
            ContentSizeFitter resourceFitter=resourceContent.GetComponent<ContentSizeFitter>();resourceFitter.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect resourceScroll=resourceViewport.GetComponent<ScrollRect>();resourceScroll.viewport=resourceViewport.GetComponent<RectTransform>();resourceScroll.content=resourceContentRect;resourceScroll.horizontal=true;resourceScroll.vertical=false;resourceScroll.movementType=ScrollRect.MovementType.Clamped;
            Resource[] shown = { Resource.Coins, Resource.Timber, Resource.Stone, Resource.Grain, Resource.Flour, Resource.Bread, Resource.Ore, Resource.Steel, Resource.Tools };
            foreach (Resource res in shown)
            {
                float width=res==Resource.Coins?82:68;
                GameObject chip = Box(res.ToString(), resourceContent.transform, Vector2.zero, new Vector2(width,42), new Color(Navy2.r,Navy2.g,Navy2.b,.86f),Vector2.zero);
                LayoutElement chipLayout=chip.AddComponent<LayoutElement>();chipLayout.preferredWidth=width;chipLayout.preferredHeight=42;
                Text t = Label("", chip.transform, HudStyle.BodySize, Cream, FontStyle.Normal, TextAnchor.MiddleCenter);
                Rect(t.gameObject, Vector2.zero, Vector2.one, new Vector2(3, 2), new Vector2(-3, -2));
                resourceTexts[res] = t;
                resourceChips[res] = chip;
                FeelUiFeedback.AttachResource(t,res==Resource.Coins?Gold:Teal);
            }
            GameObject populationChip=Box("Population",resourceContent.transform,Vector2.zero,new Vector2(232,42),new Color(Navy2.r,Navy2.g,Navy2.b,.86f),Vector2.zero);
            LayoutElement populationLayout=populationChip.AddComponent<LayoutElement>();populationLayout.preferredWidth=232;populationLayout.preferredHeight=42;
            populationText = Label("", populationChip.transform, HudStyle.BodySize, Cream, FontStyle.Normal, TextAnchor.MiddleCenter);
            Rect(populationText.gameObject,Vector2.zero,new Vector2(.58f,1),new Vector2(4,1),new Vector2(-2,-1));
            incomeText=Label("",populationChip.transform,HudStyle.BodySize,Cream,FontStyle.Normal,TextAnchor.MiddleCenter);
            Rect(incomeText.gameObject,new Vector2(.58f,0),Vector2.one,new Vector2(2,1),new Vector2(-4,-1));

            Button research = MakeButton("기술 연구", top.transform, new Vector2(-8,-4), new Vector2(200,HudStyle.TouchSize), Navy2, () => controller.ToggleResearch(), new Vector2(1,1), HudStyle.BodySize);
            research.name="Button_기술 연구";
            AddButtonIcon(research,"info");
            eraText = research.GetComponentInChildren<Text>();
            Rect(eraText.gameObject,new Vector2(0,.5f),Vector2.one,new Vector2(25,1),new Vector2(-3,-1));
            researchSummaryText = Label("", research.transform, HudStyle.BodySize, Cream, FontStyle.Normal, TextAnchor.MiddleCenter);
            Rect(researchSummaryText.gameObject,Vector2.zero,new Vector2(1,.5f),new Vector2(25,1),new Vector2(-3,-1));
        }

        void BuildObjective()
        {
            Button chip=MakeButton("현재 목표",transform,new Vector2(8,-60),new Vector2(250,44),new Color(Navy.r,Navy.g,Navy.b,.97f),ToggleObjectives,new Vector2(0,1),HudStyle.BodySize);
            chip.name="Button_Objectives";
            objectiveChipText=chip.GetComponentInChildren<Text>();
            objectiveChipText.alignment=TextAnchor.MiddleLeft;
            Rect(objectiveChipText.gameObject,Vector2.zero,Vector2.one,new Vector2(12,2),new Vector2(-26,-2));
            LabelAt("›",chip.transform,HudStyle.TitleSize,Muted,FontStyle.Normal,new Vector2(224,-10),new Vector2(18,24)).alignment=TextAnchor.MiddleCenter;

            objectivePanel = Box("Objectives", transform, new Vector2(8, -110), new Vector2(276, 160), new Color(Paper.r,Paper.g,Paper.b,.98f), new Vector2(0,1));
            Button close=MakeButton("",objectivePanel.transform,new Vector2(-8,-4),new Vector2(44,44),Navy,()=>SetObjectives(false),new Vector2(1,1),11);
            close.name="Button_ObjectivesClose";
            AddCenteredModalIcon(close,HudAssets.CloseIcon);
            objectiveTitle = LabelAt("", objectivePanel.transform, HudStyle.TitleSize, Cream, FontStyle.Normal, new Vector2(14,-10), new Vector2(190,30));
            objectiveBody = LabelAt("", objectivePanel.transform, HudStyle.BodySize, Ink, FontStyle.Normal, new Vector2(14,-48), new Vector2(248,58));
            objectiveProgress = LabelAt("", objectivePanel.transform, HudStyle.BodySize, Teal, FontStyle.Normal, new Vector2(14,-116), new Vector2(248,22));
            FeelUiFeedback.AttachPanel(objectivePanel);
            objectivePanel.SetActive(false);
        }

        void BuildInspector()
        {
            inspectorPanel = Box("Inspector", transform, new Vector2(-8,-60), new Vector2(292,214), new Color(Paper.r,Paper.g,Paper.b,.98f), new Vector2(1,1));
            inspectorTitle = LabelAt("선택 정보", inspectorPanel.transform, HudStyle.TitleSize, Cream, FontStyle.Normal, new Vector2(14,-8), new Vector2(214,30));
            Button close=MakeButton("",inspectorPanel.transform,new Vector2(-8,-4),new Vector2(44,44),Navy,CloseInspector,new Vector2(1,1),11);
            close.name="Button_InspectorClose";
            AddCenteredModalIcon(close,HudAssets.CloseIcon);
            GameObject rowsViewport=new GameObject("InspectorRowsViewport",typeof(RectTransform),typeof(RectMask2D),typeof(ScrollRect));rowsViewport.transform.SetParent(inspectorPanel.transform,false);
            Rect(rowsViewport,new Vector2(0,1),new Vector2(1,1),new Vector2(14,-158),new Vector2(-14,-42));
            GameObject rows=new GameObject("InspectorRows",typeof(RectTransform),typeof(VerticalLayoutGroup),typeof(ContentSizeFitter));rows.transform.SetParent(rowsViewport.transform,false);
            inspectorRowsRoot=rows.GetComponent<RectTransform>();inspectorRowsRoot.anchorMin=new Vector2(0,1);inspectorRowsRoot.anchorMax=new Vector2(1,1);inspectorRowsRoot.pivot=new Vector2(.5f,1);inspectorRowsRoot.anchoredPosition=Vector2.zero;inspectorRowsRoot.sizeDelta=Vector2.zero;
            VerticalLayoutGroup rowsLayout=rows.GetComponent<VerticalLayoutGroup>();rowsLayout.spacing=2;rowsLayout.childForceExpandHeight=false;rowsLayout.childForceExpandWidth=true;
            rows.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect rowsScroll=rowsViewport.GetComponent<ScrollRect>();rowsScroll.viewport=rowsViewport.GetComponent<RectTransform>();rowsScroll.content=inspectorRowsRoot;rowsScroll.horizontal=false;rowsScroll.vertical=true;rowsScroll.movementType=ScrollRect.MovementType.Clamped;
            for(int i=0;i<10;i++)CreateInspectorRow(rows.transform);
            inspectorBody = LabelAt("", inspectorPanel.transform, HudStyle.BodySize, Ink, FontStyle.Normal, new Vector2(14,-42), new Vector2(264,116));
            inspectorBody.gameObject.SetActive(false);
            upgradeButton=MakeButton("선택 건물 업그레이드", inspectorPanel.transform, new Vector2(14,-164), new Vector2(264,44), Navy2, () => controller.UpgradeSelected(), new Vector2(0,1),11);
            upgradeLabel=upgradeButton.GetComponentInChildren<Text>();
            factoryConfigureButton=MakeButton("설비 구성",inspectorPanel.transform,new Vector2(14,-164),new Vector2(264,44),Navy2,()=>SetFactoryModal(true),new Vector2(0,1),11);
            factoryConfigureButton.name="Button_FactoryConfigure";
            factoryConfigureButton.gameObject.SetActive(false);
            FeelUiFeedback.AttachPanel(inspectorPanel);
            inspectorPanel.SetActive(false);
        }

        void BuildBottomBar()
        {
            GameObject bottom = Panel("BuildBar", transform, new Color(Navy.r,Navy.g,Navy.b,.98f), new Vector2(0,0), new Vector2(1,0), new Vector2(8,8), new Vector2(-8,60));
            GameObject groupViewport=new GameObject("BottomGroupsViewport",typeof(RectTransform),typeof(RectMask2D),typeof(ScrollRect));groupViewport.transform.SetParent(bottom.transform,false);
            Rect(groupViewport,Vector2.zero,Vector2.one,new Vector2(6,4),new Vector2(-88,-4));
            GameObject groups=new GameObject("BottomGroups",typeof(RectTransform),typeof(HorizontalLayoutGroup),typeof(ContentSizeFitter));groups.transform.SetParent(groupViewport.transform,false);
            RectTransform groupsRect=groups.GetComponent<RectTransform>();groupsRect.anchorMin=new Vector2(0,0);groupsRect.anchorMax=new Vector2(0,1);groupsRect.pivot=new Vector2(0,.5f);groupsRect.anchoredPosition=Vector2.zero;groupsRect.sizeDelta=Vector2.zero;
            HorizontalLayoutGroup groupsLayout=groups.GetComponent<HorizontalLayoutGroup>();groupsLayout.spacing=4;groupsLayout.childForceExpandWidth=false;groupsLayout.childForceExpandHeight=true;
            ContentSizeFitter groupsFitter=groups.GetComponent<ContentSizeFitter>();groupsFitter.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect groupScroll=groupViewport.GetComponent<ScrollRect>();groupScroll.viewport=groupViewport.GetComponent<RectTransform>();groupScroll.content=groupsRect;groupScroll.horizontal=true;groupScroll.vertical=false;groupScroll.movementType=ScrollRect.MovementType.Clamped;
            string[] cats = { "주거", "생산", "산업", "도시", "설비", "물류" };
            for (int i=0;i<cats.Length;i++)
            {
                string c=cats[i];
                string label=c=="설비"?"설비 G":c;
                Button tab=MakeLayoutButton(label,groups.transform,50,c==category?Coral:Navy2,()=>SelectCategory(c));
                tab.name=c=="설비"?"Button_Factory":"Button_"+c;
                categoryButtons[c]=tab;
            }
            Button collapse=MakeLayoutButton("목록 ▴",groups.transform,66,Navy2,ToggleBuildTray);buildCollapseButton=collapse;
            collapse.name="Button_BuildCollapse";
            Button remove=MakeLayoutButton("철거",groups.transform,50,Navy2,SelectRemoveTool);removeButton=remove;
            remove.name="Button_철거";
            Button menu=MakeButton("메뉴",bottom.transform,new Vector2(-6,-4),new Vector2(76,44),Navy2,OpenMenu,new Vector2(1,1),12);
            menu.name="Button_Menu";AddButtonIcon(menu,"menu");

            AddDivider(groups.transform);
            float[] speeds={0,1,3}; string[] names={"Ⅱ","1×","3×"};
            for(int i=0;i<3;i++)
            {
                float speed=speeds[i];Button b=MakeLayoutButton(names[i],groups.transform,44,Navy2,()=>controller.SetSpeed(speed));
                b.name="Button_"+names[i];speedButtons.Add(b);ConfigureSpeedButton(b,i);
            }
            AddDivider(groups.transform);
            Button overview=MakeLayoutButton("현황",groups.transform,52,Navy2,()=>OpenUtility("overview"));overview.name="Button_Overview";
            Button territory=MakeLayoutButton("영토",groups.transform,52,Navy2,()=>OpenUtility("territory"));territory.name="Button_Territory";
            Button trade=MakeLayoutButton("교역",groups.transform,52,Navy2,()=>OpenUtility("trade"));trade.name="Button_Trade";

            buildChoicesPanel=Box("BuildChoices",transform,new Vector2(8,68),new Vector2(600,72),new Color(Navy.r,Navy.g,Navy.b,.98f),new Vector2(0,0));
            buildChoicesRect=buildChoicesPanel.GetComponent<RectTransform>();
            GameObject viewport = new GameObject("BuildChoiceViewport", typeof(RectTransform),typeof(RectMask2D),typeof(ScrollRect));
            viewport.transform.SetParent(buildChoicesPanel.transform,false); Rect(viewport,Vector2.zero,Vector2.one,new Vector2(10,8),new Vector2(-10,-8));
            buildScrollViewport=viewport.GetComponent<RectTransform>();
            GameObject content=new GameObject("BuildChoiceContent",typeof(RectTransform),typeof(HorizontalLayoutGroup),typeof(ContentSizeFitter));content.transform.SetParent(viewport.transform,false);
            buildViewport=content.GetComponent<RectTransform>();buildViewport.anchorMin=new Vector2(0,0);buildViewport.anchorMax=new Vector2(0,1);buildViewport.pivot=new Vector2(0,.5f);buildViewport.anchoredPosition=Vector2.zero;buildViewport.sizeDelta=Vector2.zero;
            HorizontalLayoutGroup layout=content.GetComponent<HorizontalLayoutGroup>();layout.spacing=6;layout.childForceExpandWidth=false;layout.childForceExpandHeight=true;
            ContentSizeFitter contentFitter=content.GetComponent<ContentSizeFitter>();contentFitter.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect buildScroll=viewport.GetComponent<ScrollRect>();buildScroll.viewport=buildScrollViewport;buildScroll.content=buildViewport;buildScroll.horizontal=true;buildScroll.vertical=false;buildScroll.movementType=ScrollRect.MovementType.Clamped;
            factoryFloorStrip=Box("FactoryFloorStrip",content.transform,Vector2.zero,new Vector2(610,56),HudStyle.Surface,new Vector2(0,0));
            LayoutElement floorStripLayout=factoryFloorStrip.AddComponent<LayoutElement>();floorStripLayout.preferredWidth=610;floorStripLayout.preferredHeight=56;
            string[] floorNames={"지상","2층","3층"};
            for(int i=0;i<floorNames.Length;i++)
            {
                int floor=i;
                Button button=MakeButton(floorNames[i],factoryFloorStrip.transform,new Vector2(i*72,-6),new Vector2(66,44),Navy2,()=>{controller.Factory?.SetFloor(floor);Refresh();},new Vector2(0,1),11);
                button.name="Button_FactoryFloor_"+floor;factoryFloorButtons[floor]=button;
            }
            factoryFoundationButton=MakeButton("기반 설치",factoryFloorStrip.transform,new Vector2(222,-6),new Vector2(96,44),Navy2,()=>{controller.Factory?.SelectFoundation();Refresh();},new Vector2(0,1),11);
            factoryFoundationButton.name="Button_FactoryFoundation";
            factoryLinkDownButton=MakeButton("연결층 ↓",factoryFloorStrip.transform,new Vector2(324,-6),new Vector2(132,44),Navy2,()=>{if(controller.Factory!=null)controller.Factory.SetLinkTargetFloor(controller.Factory.ActiveFloor-1);Refresh();},new Vector2(0,1),11);
            factoryLinkDownButton.name="Button_FactoryLinkDown";
            factoryLinkUpButton=MakeButton("연결층 ↑",factoryFloorStrip.transform,new Vector2(462,-6),new Vector2(132,44),Navy2,()=>{if(controller.Factory!=null)controller.Factory.SetLinkTargetFloor(controller.Factory.ActiveFloor+1);Refresh();},new Vector2(0,1),11);
            factoryLinkUpButton.name="Button_FactoryLinkUp";
            foreach (BuildingSpec spec in Catalog.All)
            {
                if (spec.Kind == BuildingKind.None || spec.Kind == BuildingKind.TownHall) continue;
                Button b=MakeButton(spec.Name,content.transform,Vector2.zero,new Vector2(96,56),Navy2,()=>SelectCityTool(spec.Kind),Vector2.zero,11);
                LayoutElement le=b.gameObject.AddComponent<LayoutElement>(); le.preferredWidth=96; le.preferredHeight=56;
                buildButtons[spec.Kind]=b; buildLabels[spec.Kind]=b.GetComponentInChildren<Text>();
                AttachBuildTooltip(b,spec.Description);
            }
            foreach (FactorySpec spec in FactoryCatalog.All)
            {
                if(spec==null || spec.Kind==FactoryKind.None)continue;
                FactoryKind kind=spec.Kind;
                Button b=MakeButton(spec.Name,content.transform,Vector2.zero,new Vector2(104,56),Navy2,()=>SelectFactoryTool(kind),Vector2.zero,11);
                LayoutElement le=b.gameObject.AddComponent<LayoutElement>();le.preferredWidth=104;le.preferredHeight=56;
                factoryButtons[kind]=b;factoryLabels[kind]=b.GetComponentInChildren<Text>();
                AttachBuildTooltip(b,spec.Description);
            }
            buildInfo = Label("", buildChoicesPanel.transform, HudStyle.BodySize, Cream, FontStyle.Normal, TextAnchor.UpperLeft);buildInfo.gameObject.SetActive(false);
            buildTooltip=Box("BuildTooltip",transform,new Vector2(8,148),new Vector2(360,58),new Color(Paper.r,Paper.g,Paper.b,.99f),new Vector2(0,0));
            buildTooltipRect=buildTooltip.GetComponent<RectTransform>();buildTooltipText=Label("",buildTooltip.transform,HudStyle.BodySize,Cream,FontStyle.Normal,TextAnchor.MiddleLeft);Rect(buildTooltipText.gameObject,Vector2.zero,Vector2.one,new Vector2(10,6),new Vector2(-10,-6));buildTooltip.SetActive(false);

            activeToolPanel=Box("ActiveTool",transform,new Vector2(0,68),new Vector2(460,52),new Color(Navy.r,Navy.g,Navy.b,.98f),new Vector2(.5f,0));
            activeToolRect=activeToolPanel.GetComponent<RectTransform>();
            GameObject activeViewport=new GameObject("ActiveToolViewport",typeof(RectTransform),typeof(RectMask2D),typeof(ScrollRect));activeViewport.transform.SetParent(activeToolPanel.transform,false);Rect(activeViewport,Vector2.zero,Vector2.one,new Vector2(4,4),new Vector2(-4,-4));
            GameObject activeContent=new GameObject("ActiveToolContent",typeof(RectTransform));activeContent.transform.SetParent(activeViewport.transform,false);activeToolContent=activeContent.GetComponent<RectTransform>();activeToolContent.anchorMin=new Vector2(0,0);activeToolContent.anchorMax=new Vector2(0,1);activeToolContent.pivot=new Vector2(0,.5f);activeToolContent.anchoredPosition=Vector2.zero;activeToolContent.sizeDelta=new Vector2(452,0);
            ScrollRect activeScroll=activeViewport.GetComponent<ScrollRect>();activeScroll.viewport=activeViewport.GetComponent<RectTransform>();activeScroll.content=activeToolContent;activeScroll.horizontal=true;activeScroll.vertical=false;activeScroll.movementType=ScrollRect.MovementType.Clamped;
            modeText=LabelAt("",activeContent.transform,HudStyle.BodySize,Cream,FontStyle.Normal,new Vector2(8,-0),new Vector2(230,42));
            modeText.alignment=TextAnchor.MiddleLeft;
            Button cancel=MakeButton("취소",activeContent.transform,new Vector2(242,0),new Vector2(86,44),Navy2,CancelTool,new Vector2(0,1),12);
            cancel.name="Button_ToolCancel";
            factoryRotateButton=MakeButton("회전  R",activeContent.transform,new Vector2(334,0),new Vector2(106,44),Navy2,RotateFactoryTool,new Vector2(0,1),11);
            factoryRotateButton.name="Button_FactoryRotate";AddButtonIcon(factoryRotateButton,"rotate");
            FeelUiFeedback.AttachPanel(buildChoicesPanel);
            FeelUiFeedback.AttachPanel(activeToolPanel);
            buildChoicesPanel.SetActive(false);
            activeToolPanel.SetActive(false);
        }

        Button MakeLayoutButton(string value,Transform parent,float width,Color color,UnityEngine.Events.UnityAction click)
        {
            Button button=MakeButton(value,parent,Vector2.zero,new Vector2(width,HudStyle.TouchSize),color,click,Vector2.zero,HudStyle.BodySize);
            LayoutElement layout=button.gameObject.AddComponent<LayoutElement>();layout.preferredWidth=width;layout.preferredHeight=HudStyle.TouchSize;
            return button;
        }

        void AddDivider(Transform parent)
        {
            GameObject divider=new GameObject("Divider",typeof(RectTransform),typeof(Image),typeof(LayoutElement));divider.transform.SetParent(parent,false);
            LayoutElement layout=divider.GetComponent<LayoutElement>();layout.preferredWidth=6;layout.preferredHeight=HudStyle.TouchSize;
            Image image=divider.GetComponent<Image>();image.sprite=HudAssets.Divider;image.color=HudStyle.TextMuted;image.preserveAspect=true;image.raycastTarget=false;
        }

        void ConfigureSpeedButton(Button button,int index)
        {
            Text label=button.GetComponentInChildren<Text>();if(label!=null)label.gameObject.SetActive(false);
            string iconName=index==0?HudAssets.PauseIcon:HudAssets.PlayIcon;
            Sprite sprite=HudAssets.Icon(iconName);if(sprite==null)return;
            int iconCount=index==2?2:1;
            for(int i=0;i<iconCount;i++)
            {
                GameObject icon=new GameObject("Icon_"+iconName+"_"+i,typeof(RectTransform),typeof(Image));icon.transform.SetParent(button.transform,false);
                float x=iconCount==1?0:(i==0?-7:7);Rect(icon,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(x,0),new Vector2(16,16));
                Image image=icon.GetComponent<Image>();image.sprite=sprite;image.color=HudStyle.Foreground(button.targetGraphic.color);image.preserveAspect=true;image.raycastTarget=false;
            }
        }

        void AttachBuildTooltip(Button button,string initialText)
        {
            HudTooltipTrigger trigger=button.gameObject.AddComponent<HudTooltipTrigger>();trigger.TooltipText=initialText;trigger.Show=ShowBuildTooltip;trigger.Hide=HideBuildTooltip;
        }

        static void SetBuildTooltip(Button button,string text)
        {
            HudTooltipTrigger trigger=button==null?null:button.GetComponent<HudTooltipTrigger>();if(trigger!=null)trigger.TooltipText=text;
        }

        void ShowBuildTooltip(RectTransform source,string text)
        {
            if(buildTooltip==null||buildTooltipText==null)return;buildTooltipText.text=text;buildTooltip.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Vector3[] corners=new Vector3[4];source.GetWorldCorners(corners);Vector3 local=((RectTransform)transform).InverseTransformPoint(corners[1]);
            float scale=Mathf.Max(1,appliedUiScale);float logicalWidth=currentSafePixels.width/scale;
            float logicalHeight=currentSafePixels.height/scale;
            float tooltipHeight=Mathf.Max(58,buildTooltipText.preferredHeight+16);
            buildTooltipRect.sizeDelta=new Vector2(buildTooltipRect.sizeDelta.x,tooltipHeight);
            float bottom=Mathf.Clamp(148,8,Mathf.Max(8,logicalHeight-tooltipHeight-8));
            buildTooltipRect.pivot=new Vector2(0,0);buildTooltipRect.anchoredPosition=new Vector2(Mathf.Clamp(local.x,8,Mathf.Max(8,logicalWidth-buildTooltipRect.rect.width-8)),bottom);
        }

        void HideBuildTooltip(){if(buildTooltip!=null)buildTooltip.SetActive(false);}

        void ApplyMobileSafeArea()
        {
            GameObject root=new GameObject("CitySafeArea",typeof(RectTransform));root.transform.SetParent(transform,false);
            mobileSafeAreaRoot=root.GetComponent<RectTransform>();
            UpdateMobileSafeArea(true);
            List<Transform> children=new List<Transform>();foreach(Transform child in transform)if(child!=root.transform&&child.GetComponent<EventSystem>()==null)children.Add(child);
            foreach(Transform child in children)child.SetParent(root.transform,false);
        }

        void UpdateMobileSafeArea(bool force)
        {
            Vector2 pixels=HudStyle.PixelDimensions(hudCanvas);int pixelWidth=Mathf.RoundToInt(pixels.x),pixelHeight=Mathf.RoundToInt(pixels.y);
            if(mobileSafeAreaRoot==null || pixelWidth<=0 || pixelHeight<=0)return;
            bool virtualFrame=hudCanvas!=null&&hudCanvas.worldCamera!=null&&hudCanvas.worldCamera.targetTexture!=null;
            Rect safe=virtualFrame?new Rect(0,0,pixelWidth,pixelHeight):Screen.safeArea;
            if(!force && appliedScreenWidth==pixelWidth && appliedScreenHeight==pixelHeight && appliedSafeArea==safe)return;
            mobileSafeAreaRoot.anchorMin=new Vector2(safe.xMin/pixelWidth,safe.yMin/pixelHeight);
            mobileSafeAreaRoot.anchorMax=new Vector2(safe.xMax/pixelWidth,safe.yMax/pixelHeight);
            mobileSafeAreaRoot.offsetMin=mobileSafeAreaRoot.offsetMax=Vector2.zero;
            appliedSafeArea=safe;
            currentSafePixels=safe;
            appliedScreenWidth=pixelWidth;
            appliedScreenHeight=pixelHeight;
        }

        void UpdateDisplayLayout(bool force)
        {
            Vector2 pixels=HudStyle.PixelDimensions(hudCanvas);int pixelWidth=Mathf.RoundToInt(pixels.x),pixelHeight=Mathf.RoundToInt(pixels.y);
            if(pixelWidth<=0||pixelHeight<=0)return;
            Rect previousSafe=currentSafePixels;UpdateMobileSafeArea(false);bool safeChanged=previousSafe!=currentSafePixels;
            int scale=HudStyle.IntegerScale(pixelWidth,pixelHeight,Screen.dpi,Application.isMobilePlatform);
            if(!force&&!safeChanged&&appliedUiWidth==pixelWidth&&appliedUiHeight==pixelHeight&&appliedUiScale==scale)return;
            if(canvasScaler!=null)canvasScaler.scaleFactor=scale;
            UpdateMobileSafeArea(true);
            appliedUiWidth=pixelWidth;appliedUiHeight=pixelHeight;appliedUiScale=scale;
            ResizeConstructionPanels();
            RefreshModalLayouts();
        }

        void ResizeConstructionPanels()
        {
            if(buildChoicesRect==null)return;
            float logicalWidth=Mathf.Max(1,currentSafePixels.width/Mathf.Max(1,appliedUiScale));
            float contentWidth=20;
            foreach(var pair in buildButtons)if(pair.Value!=null&&pair.Value.gameObject.activeSelf)contentWidth+=102;
            foreach(var pair in factoryButtons)if(pair.Value!=null&&pair.Value.gameObject.activeSelf)contentWidth+=110;
            buildChoicesRect.sizeDelta=new Vector2(Mathf.Clamp(contentWidth,116,Mathf.Max(116,logicalWidth-16)),72);
            if(activeToolRect!=null)activeToolRect.sizeDelta=new Vector2(Mathf.Clamp(logicalWidth-16,116,460),52);
            if(buildTooltipRect!=null)buildTooltipRect.sizeDelta=new Vector2(Mathf.Min(420,Mathf.Max(220,logicalWidth-16)),58);
        }

        void Refresh()
        {
            if (!isActiveAndEnabled || controller == null || controller.Sim == null) return;
            bool factoryPaletteOpen=controller.Factory!=null && controller.Factory.IsOpen;
            if(factoryPaletteOpen!=observedFactoryPaletteOpen)
            {
                observedFactoryPaletteOpen=factoryPaletteOpen;
                if(factoryPaletteOpen){category="설비";buildTrayOpen=true;}
                else if(IsFactoryCategory(category))category=lastCityCategory;
            }
            Simulation sim=controller.Sim; GameState s=controller.State;
            RefreshAdvancedResourceVisibility(sim,s);
            foreach(var pair in resourceTexts)
            {
                float amount=Mathf.Floor(pair.Key==Resource.Coins?s.Coins:sim.Get(pair.Key));
                string value=((int)amount).ToString("N0");
                pair.Value.text=Catalog.ResourceName(pair.Key)+"  "+value;
                float previous;
                if(displayedResourceValues.TryGetValue(pair.Key,out previous) && amount>previous+.0001f)
                    FeelUiFeedback.PulseResource(pair.Value);
                displayedResourceValues[pair.Key]=amount;
            }
            string income=sim.LastIncome>=0?"+"+sim.LastIncome.ToString("0.0"):sim.LastIncome.ToString("0.0");
            populationText.text="주민 "+s.Population+" · 행복 "+s.Happiness+"%\n"+s.Day+"일";
            incomeText.text="수입\n"+income+"G";
            incomeText.color=sim.LastIncome<0?HudStyle.Danger:HudStyle.Text;
            RefreshResearch();
            objectiveTitle.text=sim.ObjectiveTitle ?? "도시를 성장시키세요";
            objectiveBody.text=sim.ObjectiveDescription ?? "생산망과 주거지를 연결하세요.";
            objectiveProgress.text="진행도  "+Mathf.RoundToInt(Mathf.Clamp01(sim.ObjectiveProgress)*100f)+"%";
            objectiveChipText.text="목표  ·  "+objectiveTitle.text+"  "+Mathf.RoundToInt(Mathf.Clamp01(sim.ObjectiveProgress)*100f)+"%";
            RefreshInspector(); RefreshBuildChoices(); RefreshBuildInfo();
            RefreshOverview();
            RefreshFeelModeLabel();
            if(Automation!=null&&Automation.IsOpen)Automation.Refresh();
            if(Projects!=null&&Projects.IsOpen)Projects.Refresh();
            for(int i=0;i<speedButtons.Count;i++){float v=i==0?0:i==1?1:3; SetButtonColor(speedButtons[i],Mathf.Approximately(controller.GameSpeed,v)?Coral:Navy2);}
            FactoryKind factoryTool=controller.Factory==null?FactoryKind.None:controller.Factory.SelectedTool;
            modeText.text=ActiveToolSummary(factoryTool);
            if(buildCollapseButton!=null)buildCollapseButton.GetComponentInChildren<Text>().text=buildTrayOpen?"목록 ▾":"목록 ▴";
            bool removal=controller.DemolitionMode||(controller.Factory!=null&&controller.Factory.RemovalMode);
            SetButtonColor(removeButton,removal?HudStyle.Danger:HudStyle.SurfaceRaised);
            if(removeButton!=null&&!removal)
            {
                Text removeLabel=removeButton.GetComponentInChildren<Text>();if(removeLabel!=null)removeLabel.color=HudStyle.Danger;
            }
            helpOverlay.SetActive(controller.HelpOpen);
            researchOverlay.SetActive(controller.ResearchOpen);
            RefreshModalOverlays();
            if(controller.NoticeVersion!=lastNoticeVersion)
            {
                lastNoticeVersion=controller.NoticeVersion;
                if(!string.IsNullOrEmpty(controller.Notice)){noticeText.text=controller.Notice;noticeVisibleUntil=Time.unscaledTime+4f;}
            }
        }

        void RefreshAdvancedResourceVisibility(Simulation sim,GameState state)
        {
            if(!ReferenceEquals(observedUiState,state))
            {
                observedUiState=state;stickyResourceVisibility.Clear();
            }
            Resource[] advanced={Resource.Ore,Resource.Steel,Resource.Tools};
            BuildingKind[] buildings={BuildingKind.Mine,BuildingKind.Smelter,BuildingKind.Workshop};
            for(int i=0;i<advanced.Length;i++)
            {
                Resource resource=advanced[i];bool shown;
                stickyResourceVisibility.TryGetValue(resource,out shown);
                TechId required=TechCatalog.RequiredTechnology(buildings[i]);
                bool unlocked=required==TechId.None||TechCatalog.Has(state,required);
                bool built=state.Cells!=null&&state.Cells.Any(cell=>cell.Building==buildings[i]);
                shown=shown||sim.Get(resource)>0||unlocked||built;
                stickyResourceVisibility[resource]=shown;
                GameObject chip;if(resourceChips.TryGetValue(resource,out chip))chip.SetActive(shown);
            }
        }

        string ActiveToolSummary(FactoryKind factoryTool)
        {
            if(controller.Factory!=null&&controller.Factory.RemovalMode)return "설비 철거\n선택한 설비를 제거합니다";
            if(controller.Factory!=null&&controller.Factory.FoundationMode)return (controller.Factory.ActiveFloor==1?"2층":"3층")+" 기반 설치\n30G · 강철 보 2 · 모듈 프레임 1";
            if(controller.DemolitionMode)return "철거 모드\n선택한 건물을 제거합니다";
            if(factoryTool!=FactoryKind.None)
            {
                FactorySpec spec=FactoryCatalog.Get(factoryTool);
                return spec.Name+" 배치\n"+spec.CoinCost+"G · 목재 "+spec.TimberCost+" · 석재 "+spec.StoneCost;
            }
            if(controller.SelectedTool!=BuildingKind.None)
            {
                BuildingSpec spec=Catalog.Get(controller.SelectedTool);
                return spec.Name+" 배치\n"+spec.Cost+"G · 목재 "+spec.TimberCost+" · 석재 "+spec.StoneCost;
            }
            return "건설 도구";
        }

        void RefreshInspector()
        {
            Cell c=controller.SelectedCell;
            FactoryEntity machine=controller.SelectedFactory;
            bool hasResident=!string.IsNullOrEmpty(controller.ResidentDetails);
            bool hasSelection=machine!=null || hasResident || c!=null;
            if(inspectorPanel!=null)inspectorPanel.SetActive(hasSelection);
            if(!hasSelection)return;
            if(machine!=null)
            {
                FactorySpec spec=FactoryCatalog.Get(machine.Kind);
                inspectorTitle.text=spec==null?"산업 설비":spec.Name;
                string status=string.IsNullOrEmpty(machine.Status)?"상태 확인 중":machine.Status;
                bool bad=!machine.Powered||StatusIsProblem(status);
                var rows=new List<InspectorRow>();
                rows.Add(new InspectorRow("상태",(bad?"●  ":"")+status,bad));
                int ruleCount=controller.State.Factory.AutomationRules==null?0:controller.State.Factory.AutomationRules.Count(rule=>rule!=null&&rule.TargetEntityId==machine.Id&&rule.Enabled);
                string control=machine.Paused?"수동 정지":machine.AutomationBlocked?"자동 조건 대기":ruleCount>0?"자동 조건 "+ruleCount+"개 · 가동 허용":"수동 운전";
                rows.Add(new InspectorRow("제어",control,machine.AutomationBlocked));
                string floor=(machine.Floor==0?"지상":machine.Floor==1?"2층":"3층");
                string link=machine.LinkId>0?" · "+(machine.IsLinkSender?"송신":"수신")+" → 설비 #"+machine.LinkId:"";
                rows.Add(new InspectorRow("층/연결",floor+link));
                bool filterable=machine.Kind==FactoryKind.Inserter||FactoryCatalog.IsFluidTransport(machine.Kind);
                string configuration=FactoryCatalog.IsProduction(machine.Kind)?FactoryCatalog.RecipeName(machine.Recipe):filterable?(machine.Filter==Resource.Coins?"자동":ResourceCatalog.Get(machine.Filter)?.Name):"";
                if(!string.IsNullOrEmpty(configuration))rows.Add(new InspectorRow(filterable?"필터":"제조법",configuration));
                if(FactoryCatalog.IsClockable(machine.Kind))rows.Add(new InspectorRow("가동률",(machine.Paused?"정지 · ":"")+machine.ClockPercent+"%"));
                string activity=FactoryActivityText(machine).Trim().Trim('·').Trim();
                rows.Add(new InspectorRow("생산",string.IsNullOrEmpty(activity)?"대기":activity));
                if(spec!=null&&spec.PowerDemand>0)rows.Add(new InspectorRow("전력",(machine.Powered?"공급됨":"●  공급 필요")+" · "+spec.PowerDemand.ToString("0.##"),!machine.Powered));
                rows.Add(new InspectorRow("투입",FactoryInventoryText(machine.Input)));
                rows.Add(new InspectorRow("출력",FactoryInventoryText(machine.Output)));
                rows.Add(new InspectorRow("좌표","설비 #"+machine.Id+" · "+machine.X+", "+machine.Z+" · "+DirectionText(machine.Direction),false,true));
                SetInspectorRows(rows);
                upgradeButton.gameObject.SetActive(false);
                factoryConfigureButton.gameObject.SetActive(true);
                factoryConfigureButton.interactable=controller.Factory!=null;
            }
            else if(hasResident)
            {
                inspectorTitle.text="주민 정보";
                inspectorBody.text=controller.ResidentDetails+"\n\n현재 거리의 주민  "+controller.PeopleOnStreet+"명";
                inspectorBody.gameObject.SetActive(true);inspectorRowsRoot.gameObject.SetActive(false);
            }
            else
            {
                bool owned=controller.Sim.IsOwned(c.X,c.Z); int region=(c.Z/7)*3+c.X/7;
                string building=c.Building==BuildingKind.None?"빈 터":Catalog.Get(c.Building).Name;
                inspectorTitle.text=building;
                string recipe=c.Building==BuildingKind.None?"":RecipeText(Catalog.Get(c.Building),c.Level);
                string status=!string.IsNullOrEmpty(c.Status)?c.Status:(c.Building!=BuildingKind.None&&!c.Connected?"연결 끊김":"상태 양호");
                bool bad=StatusIsProblem(status)||(c.Building!=BuildingKind.None&&!c.Connected);
                var rows=new List<InspectorRow>();rows.Add(new InspectorRow("상태",(bad?"●  ":"")+status,bad));
                if(!string.IsNullOrEmpty(recipe))rows.Add(new InspectorRow("생산",recipe));
                if(c.Building!=BuildingKind.None)rows.Add(new InspectorRow("레벨","Lv."+c.Level));
                if(IsPoweredBuilding(c.Building))rows.Add(new InspectorRow("전력",controller.PowerUsageText+" / "+controller.Sim.PowerCapacity));
                rows.Add(new InspectorRow("지형",(owned?"내 영토":"미소유 영토")+" · "+TerrainName(c.Terrain)));
                rows.Add(new InspectorRow("좌표",c.X+", "+c.Z+" · 지역 "+(region+1),false,true));
                SetInspectorRows(rows);
            }
            if(machine==null&&!hasResident)
            {
                upgradeButton.gameObject.SetActive(true);
                factoryConfigureButton.gameObject.SetActive(false);
                RefreshUpgrade(c);
            }
            else if(hasResident)
            {
                upgradeButton.gameObject.SetActive(false);factoryConfigureButton.gameObject.SetActive(false);
            }
        }

        struct InspectorRow
        {
            public readonly string Label,Value;public readonly bool Danger,Muted;
            public InspectorRow(string label,string value,bool danger=false,bool muted=false){Label=label;Value=value;Danger=danger;Muted=muted;}
        }

        void CreateInspectorRow(Transform parent)
        {
            GameObject row=new GameObject("InspectorRow",typeof(RectTransform),typeof(LayoutElement),typeof(HorizontalLayoutGroup));row.transform.SetParent(parent,false);row.GetComponent<LayoutElement>().preferredHeight=20;
            HorizontalLayoutGroup layout=row.GetComponent<HorizontalLayoutGroup>();layout.spacing=4;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=false;layout.childForceExpandHeight=true;
            Text label=Label("",row.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,TextAnchor.MiddleLeft);
            LayoutElement labelLayout=label.gameObject.AddComponent<LayoutElement>();labelLayout.minWidth=72;labelLayout.preferredWidth=72;labelLayout.flexibleWidth=0;
            Text value=Label("",row.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,TextAnchor.MiddleLeft);
            LayoutElement valueLayout=value.gameObject.AddComponent<LayoutElement>();valueLayout.minWidth=0;valueLayout.flexibleWidth=1;
            value.horizontalOverflow=HorizontalWrapMode.Wrap;value.verticalOverflow=VerticalWrapMode.Overflow;inspectorRowLabels.Add(label);inspectorRowValues.Add(value);
        }

        void SetInspectorRows(IList<InspectorRow> rows)
        {
            inspectorBody.gameObject.SetActive(false);inspectorRowsRoot.gameObject.SetActive(true);
            for(int i=0;i<inspectorRowLabels.Count;i++)
            {
                bool active=i<rows.Count;inspectorRowLabels[i].transform.parent.gameObject.SetActive(active);if(!active)continue;
                InspectorRow row=rows[i];inspectorRowLabels[i].text=row.Label;inspectorRowValues[i].text=row.Value;
                inspectorRowValues[i].color=row.Danger?HudStyle.Danger:row.Muted?HudStyle.TextMuted:HudStyle.Text;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(inspectorRowsRoot);Canvas.ForceUpdateCanvases();
            for(int i=0;i<inspectorRowLabels.Count;i++)
            {
                if(!inspectorRowLabels[i].transform.parent.gameObject.activeSelf)continue;
                LayoutElement rowLayout=inspectorRowLabels[i].transform.parent.GetComponent<LayoutElement>();
                if(rowLayout!=null)rowLayout.preferredHeight=Mathf.Max(20,inspectorRowValues[i].preferredHeight+2);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(inspectorRowsRoot);
        }

        static bool IsPoweredBuilding(BuildingKind kind)
        {
            return kind==BuildingKind.Mill||kind==BuildingKind.Bakery||kind==BuildingKind.Mine||kind==BuildingKind.Smelter||kind==BuildingKind.Workshop||kind==BuildingKind.Windmill||kind==BuildingKind.SteamPlant;
        }

        static bool StatusIsProblem(string status)
        {
            if(string.IsNullOrEmpty(status))return false;
            return status.Contains("부족")||status.Contains("필요")||status.Contains("끊김")||status.Contains("중단")||status.Contains("없음");
        }

        void RefreshBuildChoices()
        {
            FactoryKind selectedFactory=controller.Factory==null?FactoryKind.None:controller.Factory.SelectedTool;
            bool activeTool=controller.SelectedTool!=BuildingKind.None||controller.DemolitionMode||selectedFactory!=FactoryKind.None||(controller.Factory!=null&&(controller.Factory.RemovalMode||controller.Factory.FoundationMode));
            if(buildChoicesPanel!=null)buildChoicesPanel.SetActive(buildTrayOpen&&!activeTool);
            if(activeToolPanel!=null)activeToolPanel.SetActive(activeTool);
            foreach(var tab in categoryButtons) SetButtonColor(tab.Value,tab.Key==category?Coral:Navy2);
            bool factoryCategory=IsFactoryCategory(category);
            if(factoryFloorStrip!=null&&activeToolContent!=null&&buildViewport!=null)
            {
                Transform desired=activeTool&&factoryCategory?activeToolContent:buildViewport;
                if(factoryFloorStrip.transform.parent!=desired)factoryFloorStrip.transform.SetParent(desired,false);
                RectTransform strip=factoryFloorStrip.transform as RectTransform;
                if(desired==activeToolContent)
                {
                    strip.anchorMin=strip.anchorMax=strip.pivot=new Vector2(0,1);strip.anchoredPosition=new Vector2(452,0);strip.sizeDelta=new Vector2(610,44);
                    activeToolContent.sizeDelta=new Vector2(1062,0);
                }
                else
                {
                    strip.sizeDelta=new Vector2(610,56);
                    activeToolContent.sizeDelta=new Vector2(452,0);
                }
                float stripButtonY=desired==activeToolContent?0:-6;
                foreach(Button floorButton in factoryFloorButtons.Values)SetFactoryStripButtonY(floorButton,stripButtonY);
                SetFactoryStripButtonY(factoryFoundationButton,stripButtonY);
                SetFactoryStripButtonY(factoryLinkDownButton,stripButtonY);
                SetFactoryStripButtonY(factoryLinkUpButton,stripButtonY);
            }
            if(factoryFloorStrip!=null)factoryFloorStrip.SetActive(factoryCategory);
            if(controller.Factory!=null)
            {
                foreach(var pair in factoryFloorButtons)
                {
                    pair.Value.interactable=true;
                    Text label=pair.Value.GetComponentInChildren<Text>();
                    if(label!=null)label.text=pair.Key==0?"지상":pair.Key==1?"2층":"3층";
                    SetButtonColor(pair.Value,controller.Factory.ActiveFloor==pair.Key?Coral:Navy2);
                }
                bool foundationVisible=factoryCategory&&category=="설비";
                factoryFoundationButton.gameObject.SetActive(foundationVisible);
                TechId foundationTech=controller.Factory.ActiveFloor>=2?TechId.AdvancedManufacturing:TechId.MassProduction;
                bool foundationUnlocked=TechCatalog.Has(controller.State,foundationTech);
                factoryFoundationButton.interactable=foundationUnlocked;
                factoryFoundationButton.GetComponentInChildren<Text>(true).text=foundationUnlocked?"기반 설치":"기반 잠김\n"+TechCatalog.Get(foundationTech).Name;
                SetButtonColor(factoryFoundationButton,controller.Factory.FoundationMode?Coral:Navy2);
                bool linkTool=selectedFactory==FactoryKind.ItemLift||selectedFactory==FactoryKind.FluidRiser;
                int activeFloor=controller.Factory.ActiveFloor;
                factoryLinkDownButton.gameObject.SetActive(linkTool);
                factoryLinkUpButton.gameObject.SetActive(linkTool);
                factoryLinkDownButton.interactable=linkTool&&activeFloor>0;
                factoryLinkUpButton.interactable=linkTool&&activeFloor<FactoryLayers.MaxFloor;
                factoryLinkDownButton.GetComponentInChildren<Text>(true).text="연결층 ↓ "+FloorName(activeFloor-1);
                factoryLinkUpButton.GetComponentInChildren<Text>(true).text="연결층 ↑ "+FloorName(activeFloor+1);
                SetButtonColor(factoryLinkDownButton,linkTool&&controller.Factory.LinkTargetFloor==activeFloor-1?Coral:Navy2);
                SetButtonColor(factoryLinkUpButton,linkTool&&controller.Factory.LinkTargetFloor==activeFloor+1?Coral:Navy2);
            }
            foreach(var pair in buildButtons)
            {
                BuildingSpec spec=Catalog.Get(pair.Key);
                bool visible=!factoryCategory && MapCategory(spec)==category;
                pair.Value.gameObject.SetActive(visible);
                string lockReason;
                bool unlocked=controller.Sim.IsBuildingUnlocked(pair.Key,out lockReason);
                bool affordable=controller.State.Coins>=spec.Cost && controller.Sim.Get(Resource.Timber)>=spec.TimberCost && controller.Sim.Get(Resource.Stone)>=spec.StoneCost;
                pair.Value.interactable=unlocked && affordable;
                SetButtonColor(pair.Value,controller.SelectedTool==pair.Key?Coral:(unlocked&&affordable?Navy2:new Color(.28f,.32f,.36f,1)));
                TechId required=TechCatalog.RequiredTechnology(pair.Key);
                string requiredName=required==TechId.None?"잠김":TechCatalog.Get(required).Name;
                buildLabels[pair.Key].text=spec.Name+"\n"+(unlocked?spec.Cost+"G":"잠김 "+requiredName);
                SetBuildTooltip(pair.Value,spec.Description+"\n비용 "+spec.Cost+"G · 목재 "+spec.TimberCost+" · 석재 "+spec.StoneCost+(unlocked?"":"\n"+lockReason));
            }
            foreach(var pair in factoryButtons)
            {
                FactorySpec spec=FactoryCatalog.Get(pair.Key);
                bool visible=factoryCategory && FactoryCategory(pair.Key)==category;
                pair.Value.gameObject.SetActive(visible);
                if(!visible)continue;
                string reason="설비 시스템 준비 중";
                bool available=controller.Factory!=null && controller.Factory.CanAfford(pair.Key,out reason);
                pair.Value.interactable=available;
                bool selected=controller.Factory!=null && controller.Factory.SelectedTool==pair.Key;
                SetButtonColor(pair.Value,selected?Coral:(available?Navy2:new Color(.28f,.32f,.36f,1)));
                bool techUnlocked=spec.RequiredTech==TechId.None||TechCatalog.Has(controller.State,spec.RequiredTech);
                string technology=spec.RequiredTech==TechId.None?"잠김":TechCatalog.Get(spec.RequiredTech).Name;
                bool paired=pair.Key==FactoryKind.ItemLift||pair.Key==FactoryKind.FluidRiser;
                int multiplier=paired?2:1;
                factoryLabels[pair.Key].text=spec.Name+"\n"+(techUnlocked?spec.CoinCost*multiplier+"G"+(paired?" · 한 쌍":""):"잠김 "+technology);
                SetBuildTooltip(pair.Value,spec.Description+"\n총비용 "+spec.CoinCost*multiplier+"G · 목재 "+spec.TimberCost*multiplier+" · 석재 "+spec.StoneCost*multiplier+(available?"":"\n"+reason));
            }
            if(factoryRotateButton!=null)
            {
                factoryRotateButton.gameObject.SetActive(selectedFactory!=FactoryKind.None);
                factoryRotateButton.interactable=selectedFactory!=FactoryKind.None;
                factoryRotateButton.GetComponentInChildren<Text>().text="회전  "+(selectedFactory==FactoryKind.None?"R":DirectionText(controller.Factory.Direction)+"  R");
            }
            if(buildChoicesPanel!=null&&buildChoicesPanel.activeSelf)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(buildViewport);
                Canvas.ForceUpdateCanvases();
            }
            ResizeConstructionPanels();
        }

        public bool VerifyLayout(out string reason)
        {
            if(buildViewport==null || buildViewport.rect.height<=0){reason="건설 목록 영역의 높이가 올바르지 않습니다.";return false;}
            int expectedFactoryCount=FactoryCatalog.All.Count(spec=>spec!=null && spec.Kind!=FactoryKind.None);
            if(categoryButtons.Count!=6 || !categoryButtons.ContainsKey("설비") || !categoryButtons.ContainsKey("물류")){reason="도시와 설비 분류 탭이 모두 생성되지 않았습니다.";return false;}
            if(factoryButtons.Count!=expectedFactoryCount || factoryRotateButton==null){reason="설비 도구 또는 회전 버튼이 모두 생성되지 않았습니다.";return false;}
            if(factoryFloorButtons.Count!=FactoryLayers.MaxFloor+1||!factoryFloorButtons.ContainsKey(0)||!factoryFloorButtons.ContainsKey(1)||!factoryFloorButtons.ContainsKey(2)||factoryFoundationButton==null||factoryLinkDownButton==null||factoryLinkUpButton==null){reason="공장 층과 기반·연결층 도구가 모두 생성되지 않았습니다.";return false;}
            int visible=0;
            foreach(var button in buildButtons.Values)
            {
                if(!button.gameObject.activeInHierarchy)continue;
                visible++;
                RectTransform rect=button.transform as RectTransform;
                if(rect==null || rect.rect.height<=0){reason="활성 건설 버튼의 높이가 올바르지 않습니다.";return false;}
            }
            foreach(var button in factoryButtons.Values)
            {
                if(!button.gameObject.activeInHierarchy)continue;
                visible++;
                RectTransform rect=button.transform as RectTransform;
                if(rect==null || rect.rect.height<=0){reason="활성 설비 버튼의 높이가 올바르지 않습니다.";return false;}
            }
            if(buildChoicesPanel!=null&&buildChoicesPanel.activeInHierarchy&&visible==0){reason="열린 분류에 표시된 건설 버튼이 없습니다.";return false;}
            Image factoryBlocker=factoryConfigureOverlay==null?null:factoryConfigureOverlay.GetComponent<Image>();
            RectTransform factoryOverlayRect=factoryConfigureOverlay==null?null:factoryConfigureOverlay.transform as RectTransform;
            if(factoryBlocker==null || !factoryBlocker.raycastTarget || factoryConfigureWindow==null || factoryOverlayRect==null || !ContainsBounds(factoryOverlayRect,factoryConfigureWindow,1f)){reason="설비 구성 창이 화면 안에서 월드 입력을 차단하지 못합니다.";return false;}
            int recipeCount=FactoryCatalog.Recipes.Count(item=>item!=null&&item.Id!=FactoryRecipe.None);
            if(factoryRecipeButtons.Count!=recipeCount || factoryFilterButtons.Count!=ResourceCatalog.Count || factoryFeedButtons.Count!=ResourceCatalog.Count-1){reason="설비 구성 선택지가 카탈로그와 일치하지 않습니다.";return false;}
            if(!factoryRecipeButtons.ContainsKey(FactoryRecipe.IronPlate)||!factoryRecipeButtons.ContainsKey(FactoryRecipe.Tools)||!factoryRecipeButtons.ContainsKey(FactoryRecipe.Flour)||!factoryRecipeButtons.ContainsKey(FactoryRecipe.Bread)){reason="기존 제조법 버튼 이름이 보존되지 않았습니다.";return false;}
            if(Industry==null){reason="산업 도감이 생성되지 않았습니다.";return false;}
            if(Automation==null||Projects==null||factoryAutomationButton==null){reason="자동화 또는 도시 프로젝트 UI가 생성되지 않았습니다.";return false;}
            if (researchOverlay == null || researchWindow == null || ResearchTree == null)
            { reason = "연구 지도가 생성되지 않았습니다."; return false; }
            Image blocker = researchOverlay.GetComponent<Image>();
            if (blocker == null || !blocker.raycastTarget)
            { reason = "연구 화면이 월드 입력을 차단하지 않습니다."; return false; }
            RectTransform overlayRect = researchOverlay.transform as RectTransform;
            if (overlayRect == null || !ContainsBounds(overlayRect, researchWindow, 1f))
            { reason = "연구 창이 현재 화면 영역을 벗어났습니다."; return false; }
            if (!ResearchTree.VerifyLayout(out reason)) return false;
            reason="HUD 레이아웃 정상";return true;
        }

        static bool ContainsBounds(RectTransform parent,RectTransform child,float tolerance)
        {
            Rect rect=parent.rect;
            // Only inspect this rectangle: scroll content intentionally extends beyond its masked viewport.
            var corners=new Vector3[4];child.GetWorldCorners(corners);
            foreach(var corner in corners){var p=parent.InverseTransformPoint(corner);if(p.x<rect.xMin-tolerance||p.x>rect.xMax+tolerance||p.y<rect.yMin-tolerance||p.y>rect.yMax+tolerance)return false;}
            return true;
        }

        void RefreshBuildInfo()
        {
            if(IsFactoryCategory(category))
            {
                FactoryKind kind=hoveredFactoryKind!=FactoryKind.None?hoveredFactoryKind:controller.Factory==null?FactoryKind.None:controller.Factory.SelectedTool;
                if(kind==FactoryKind.None){buildInfo.text="설비를 고르세요. 2×2 미세 칸이 도시 부지 1칸이며 R 또는 회전 버튼으로 방향을 바꿉니다.";return;}
                FactorySpec spec=FactoryCatalog.Get(kind);string factoryReason="설비 시스템 준비 중";
                bool available=controller.Factory!=null && controller.Factory.CanAfford(kind,out factoryReason);
                string tech=spec.RequiredTech==TechId.None?"기본 설비":TechCatalog.Get(spec.RequiredTech).Name;
                buildInfo.text=spec.Description+"  ·  "+spec.Width+"×"+spec.Height+" 미세 칸  ·  "+DirectionText(controller.Factory.Direction)+" 방향\n비용  "+spec.CoinCost+"G  목재 "+spec.TimberCost+"  석재 "+spec.StoneCost+"  ·  기술 "+tech+(available?"":"  ·  "+factoryReason);
                return;
            }
            BuildingKind k=hoveredKind!=BuildingKind.None?hoveredKind:controller.SelectedTool;
            if(k==BuildingKind.None){buildInfo.text="건물을 고르면 비용과 설명을 확인할 수 있습니다.";return;}
            BuildingSpec s=Catalog.Get(k);
            string reason;
            bool unlocked=controller.Sim.IsBuildingUnlocked(k,out reason);
            string unlock=unlocked?"":" · "+reason;
            string recipe=RecipeText(s,1);
            buildInfo.text=s.Description+"\n비용  "+s.Cost+"G  목재 "+s.TimberCost+"  석재 "+s.StoneCost+unlock+(string.IsNullOrEmpty(recipe)?"":"  ·  "+recipe);
        }

        static string MapCategory(BuildingSpec s)
        {
            if(s.Kind==BuildingKind.House || s.Kind==BuildingKind.TownHall) return "주거";
            if(s.Kind==BuildingKind.Lumberyard || s.Kind==BuildingKind.Quarry || s.Kind==BuildingKind.Farm || s.Kind==BuildingKind.Mill || s.Kind==BuildingKind.Bakery || s.Kind==BuildingKind.Windmill) return "생산";
            if(s.Kind==BuildingKind.Mine || s.Kind==BuildingKind.Smelter || s.Kind==BuildingKind.Workshop) return "산업";
            return "도시";
        }

        static bool IsFactoryCategory(string value)=>value=="설비" || value=="물류";

        static string FactoryCategory(FactoryKind kind)
        {
            switch(kind)
            {
                case FactoryKind.Belt:
                case FactoryKind.Inserter:
                case FactoryKind.Splitter:
                case FactoryKind.Pipe:
                case FactoryKind.PipeJunction:
                case FactoryKind.FluidTank:
                case FactoryKind.ItemLift:
                case FactoryKind.FluidRiser:
                case FactoryKind.ImportDock:
                case FactoryKind.ExportDock:return "물류";
                default:return "설비";
            }
        }

        static string DirectionText(int direction)
        {
            switch((direction%4+4)%4){case 0:return "동쪽 →";case 1:return "북쪽 ↑";case 2:return "서쪽 ←";default:return "남쪽 ↓";}
        }

        static string FloorName(int floor)=>floor==0?"지상":floor==1?"2층":floor==2?"3층":"-";

        static void SetFactoryStripButtonY(Button button,float y)
        {
            RectTransform rect=button==null?null:button.transform as RectTransform;
            if(rect!=null)rect.anchoredPosition=new Vector2(rect.anchoredPosition.x,y);
        }

        static string FactoryInventoryText(List<int> inventory)
        {
            if(inventory==null)return "없음";
            var values=new List<string>();
            for(int i=1;i<Math.Min(ResourceCatalog.InventoryCount,inventory.Count);i++)if(inventory[i]>0)
            {
                ResourceSpec spec=ResourceCatalog.Get((Resource)i);
                values.Add((spec==null?((Resource)i).ToString():spec.Name)+" "+inventory[i]+(spec?.Unit??""));
            }
            return values.Count==0?"없음":string.Join(" · ",values);
        }

        static string FactoryActivityText(FactoryEntity machine)
        {
            if(machine==null)return "";
            if(ResourceCatalog.IsTransportable(machine.CargoResource))return "운송 "+ResourceCatalog.Get(machine.CargoResource).Name+" "+Mathf.RoundToInt(Mathf.Clamp01(machine.CargoProgress)*100f)+"%  ·  ";
            RecipeSpec recipe=FactoryCatalog.GetRecipe(machine.Recipe);
            float duration=recipe==null?0:recipe.Duration;
            return duration>0?"공정 "+Mathf.RoundToInt(Mathf.Clamp01(machine.Progress/duration)*100f)+"%  ·  ":"";
        }

        static string TerrainName(TerrainKind terrain)
        {
            switch(terrain){case TerrainKind.Forest:return "숲";case TerrainKind.Rock:return "바위";case TerrainKind.Water:return "물";default:return "평지";}
        }

        static string RecipeText(BuildingSpec spec, int level)
        {
            if(spec==null || spec.OutputAmount<=0)return "";
            float scale=.75f+Mathf.Clamp(level,1,3)*.25f;
            string output=Catalog.ResourceName(spec.Output)+" +"+(spec.OutputAmount*scale).ToString("0.#");
            if(spec.Inputs==null || spec.Inputs.Count==0)return "기본 /일 "+output;
            string inputs=string.Join(" + ",spec.Inputs.Select(p=>Catalog.ResourceName(p.Key)+" "+(p.Value*scale).ToString("0.#")));
            return "기본 /일 "+inputs+" → "+output;
        }

        void RefreshUpgrade(Cell c)
        {
            int cap=controller.Sim.UpgradeLevelCap;
            bool valid=c!=null && c.Building!=BuildingKind.None && c.Building!=BuildingKind.Road && c.Building!=BuildingKind.TownHall && c.Level<cap;
            upgradeButton.interactable=valid;
            if(!valid){upgradeLabel.text=c!=null&&c.Building!=BuildingKind.None&&c.Level>=cap?"현재 시대 최고 레벨 (Lv."+cap+")":"업그레이드할 건물 선택";return;}
            BuildingSpec spec=Catalog.Get(c.Building);int coins=Mathf.CeilToInt(spec.Cost*(.65f+c.Level*.25f));int timber=Mathf.CeilToInt(spec.TimberCost*.5f*c.Level);int stone=Mathf.CeilToInt(spec.StoneCost*.5f*c.Level);
            bool affordable=controller.State.Coins>=coins && controller.Sim.Get(Resource.Timber)>=timber && controller.Sim.Get(Resource.Stone)>=stone;
            upgradeButton.interactable=affordable;
            upgradeLabel.text="Lv."+(c.Level+1)+"  "+coins+"G · 목재 "+timber+" · 석재 "+stone;
        }

        void ToggleObjectives(){SetObjectives(!objectiveExpanded);}
        void ToggleFeelEffects()
        {
            FeelUiFeedback.ReducedMotion=!FeelUiFeedback.ReducedMotion;
            RefreshFeelModeLabel();
        }

        void RefreshFeelModeLabel()
        {
            if(feelModeLabel!=null)feelModeLabel.text="화면 효과: "+(FeelUiFeedback.ReducedMotion?"절제":"풍부");
        }

        void SetObjectives(bool open)
        {
            objectiveExpanded=open;
            if(objectivePanel!=null)objectivePanel.SetActive(open);
        }

        void ToggleBuildTray()
        {
            bool hasTool=controller.SelectedTool!=BuildingKind.None||controller.DemolitionMode||(controller.Factory!=null&&(controller.Factory.SelectedTool!=FactoryKind.None||controller.Factory.RemovalMode||controller.Factory.FoundationMode));
            if(hasTool){CancelTool();buildTrayOpen=true;}
            else buildTrayOpen=!buildTrayOpen;
            RefreshBuildChoices();RefreshBuildInfo();
        }

        void CloseInspector()
        {
            controller.ClearCitySelection();
            if(controller.Factory!=null)controller.Factory.ClearSelection();
            controller.NotifyWorldSelection();
            RefreshInspector();
        }

        GameObject Box(string name, Transform parent, Vector2 pos, Vector2 size, Color color, Vector2? pivot=null, Vector2? anchorMax=null)
        {
            GameObject go=Panel(name,parent,color,Vector2.zero,Vector2.zero,Vector2.zero,size);
            RectTransform rt=(RectTransform)go.transform; rt.anchorMin=rt.anchorMax=pivot??new Vector2(0,1); if(anchorMax.HasValue) rt.anchorMax=anchorMax.Value; rt.pivot=pivot??new Vector2(0,1); rt.anchoredPosition=pos; rt.sizeDelta=size; return go;
        }

        GameObject Panel(string name, Transform parent, Color color, Vector2 amin, Vector2 amax, Vector2 offMin, Vector2 offMax)
        {
            GameObject go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);Image image=go.GetComponent<Image>();image.sprite=HudAssets.Panel??rounded;image.type=Image.Type.Sliced;image.color=color;Rect(go,amin,amax,offMin,offMax);return go;
        }

        Text Label(string value, Transform parent, int size, Color color, FontStyle style, TextAnchor align)
        {
            GameObject go=new GameObject("Text",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);Text t=go.GetComponent<Text>();t.text=value;t.font=font;t.fontSize=size>=20?HudStyle.TitleSize:HudStyle.BodySize;t.color=color;t.fontStyle=FontStyle.Normal;t.alignment=align;t.supportRichText=true;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;
        }

        Text LabelAt(string value,Transform parent,int size,Color color,FontStyle style,Vector2 pos,Vector2 dimensions)
        {Text t=Label(value,parent,size,color,style,TextAnchor.UpperLeft);Rect(t.gameObject,new Vector2(0,1),new Vector2(0,1),pos,dimensions);((RectTransform)t.transform).pivot=new Vector2(0,1);return t;}

        Button MakeButton(string value,Transform parent,Vector2 pos,Vector2 size,Color color,UnityEngine.Events.UnityAction click,Vector2 pivot,int textSize=13)
        {
            GameObject go=Box("Button_"+value,parent,pos,size,color,pivot);Button b=go.AddComponent<Button>();Image image=go.GetComponent<Image>();image.sprite=HudAssets.Panel??rounded;image.type=Image.Type.Sliced;b.targetGraphic=image;ColorBlock cb=b.colors;cb.normalColor=Color.white;cb.highlightedColor=new Color(1.08f,1.08f,1.08f,1);cb.pressedColor=new Color(.82f,.82f,.82f,1);cb.disabledColor=new Color(.56f,.56f,.56f,.7f);b.colors=cb;b.onClick.AddListener(()=>{HudTooltipTrigger tooltip=go.GetComponent<HudTooltipTrigger>();if(tooltip==null||!tooltip.ConsumeClick)click();});Text t=Label(value,go.transform,textSize,HudStyle.Foreground(color),FontStyle.Normal,TextAnchor.MiddleCenter);Rect(t.gameObject,Vector2.zero,Vector2.one,new Vector2(4,2),new Vector2(-4,-2));FeelUiFeedback.AttachButton(b);return b;
        }

        void AddButtonIcon(Button button,string semanticName)
        {
            if(button==null)return;
            Sprite sprite=HudAssets.Icon(semanticName);
            if(sprite==null)return;
            GameObject icon=new GameObject("Icon_"+semanticName,typeof(RectTransform),typeof(Image));
            icon.transform.SetParent(button.transform,false);
            Rect(icon,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(16,0),new Vector2(20,20));
            Image image=icon.GetComponent<Image>();image.sprite=sprite;image.color=button.targetGraphic==null?Cream:HudStyle.Foreground(button.targetGraphic.color);image.preserveAspect=true;image.raycastTarget=false;
            Text label=button.GetComponentInChildren<Text>();
            if(label!=null)Rect(label.gameObject,Vector2.zero,Vector2.one,new Vector2(26,2),new Vector2(-4,-2));
        }

        void AddHover(GameObject go,UnityEngine.Events.UnityAction enter,UnityEngine.Events.UnityAction exit)
        {EventTrigger tr=go.AddComponent<EventTrigger>();AddTrigger(tr,EventTriggerType.PointerEnter,enter);AddTrigger(tr,EventTriggerType.PointerExit,exit);}
        static void AddTrigger(EventTrigger tr,EventTriggerType type,UnityEngine.Events.UnityAction a){EventTrigger.Entry e=new EventTrigger.Entry{eventID=type};e.callback.AddListener(_=>a());tr.triggers.Add(e);}
        static void SetButtonColor(Button b,Color c)
        {
            if(b==null||b.targetGraphic==null)return;b.targetGraphic.color=c;Color foreground=HudStyle.Foreground(c);
            foreach(Text text in b.GetComponentsInChildren<Text>(true))text.color=foreground;
            foreach(Image image in b.GetComponentsInChildren<Image>(true))if(image.gameObject!=b.gameObject)image.color=foreground;
        }

        static void Rect(GameObject go,Vector2 amin,Vector2 amax,Vector2 offsetOrPos,Vector2 sizeOrOffset)
        {RectTransform rt=go.GetComponent<RectTransform>();rt.anchorMin=amin;rt.anchorMax=amax;if(amin==amax){rt.anchoredPosition=offsetOrPos;rt.sizeDelta=sizeOrOffset;}else{rt.offsetMin=offsetOrPos;rt.offsetMax=sizeOrOffset;}}

        static Font LoadFont()
        {
            return GameFont.Load();
        }

        static Sprite MakeRoundedSprite()
        {
            const int n=24,r=6;Texture2D t=new Texture2D(n,n,TextureFormat.RGBA32,false);t.name="Riverworks Rounded UI";t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Bilinear;
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){float dx=Mathf.Max(0,Mathf.Abs(x-(n-1)*.5f)-(n*.5f-r));float dy=Mathf.Max(0,Mathf.Abs(y-(n-1)*.5f)-(n*.5f-r));float a=Mathf.Clamp01(r+1-Mathf.Sqrt(dx*dx+dy*dy));t.SetPixel(x,y,new Color(1,1,1,a));}t.Apply();
            return Sprite.Create(t,new Rect(0,0,n,n),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(r,r,r,r));
        }

        static Color Hex(string value){Color c;ColorUtility.TryParseHtmlString("#"+value,out c);return c;}
    }
}

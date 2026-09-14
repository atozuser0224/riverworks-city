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
        static readonly Color Navy = Hex("101820");
        static readonly Color Navy2 = Hex("253442");
        static readonly Color Cream = Hex("DCE6EB");
        static readonly Color Paper = Hex("1B2731");
        static readonly Color Coral = Hex("C89B55");
        static readonly Color Teal = Hex("4A9F98");
        static readonly Color Gold = Hex("B88A48");
        static readonly Color Muted = Hex("8FA1AC");
        static readonly Color Ink = Hex("DCE6EB");

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
        readonly Dictionary<TechId, Button> researchButtons = new Dictionary<TechId, Button>();
        readonly Dictionary<TechId, Text> researchCardTexts = new Dictionary<TechId, Text>();
        readonly Dictionary<TechId, Text> researchStatusTexts = new Dictionary<TechId, Text>();
        readonly Dictionary<TechId, Image> researchCardImages = new Dictionary<TechId, Image>();
        readonly Dictionary<TechId, RectTransform> researchCardRects = new Dictionary<TechId, RectTransform>();
        readonly Dictionary<Era, RectTransform> researchViewports = new Dictionary<Era, RectTransform>();
        readonly Dictionary<FactoryRecipe, Button> factoryRecipeButtons = new Dictionary<FactoryRecipe, Button>();
        readonly Dictionary<Resource, Button> factoryFilterButtons = new Dictionary<Resource, Button>();
        readonly Dictionary<Resource, Button> factoryFeedButtons = new Dictionary<Resource, Button>();
        readonly Dictionary<Resource, Text> factoryFeedLabels = new Dictionary<Resource, Text>();

        Text populationText, eraText, researchSummaryText, objectiveTitle, objectiveBody, objectiveProgress, objectiveChipText;
        Text inspectorTitle, inspectorBody, noticeText, buildInfo, modeText, upgradeLabel, factoryConfigureSummary, overviewBody, feelModeLabel;
        Button upgradeButton, factoryConfigureButton, factoryRotateButton;
        GameObject objectivePanel, inspectorPanel, buildChoicesPanel, activeToolPanel;
        GameObject helpOverlay, confirmOverlay, researchOverlay, mobileTradeOverlay, factoryConfigureOverlay;
        GameObject menuOverlay, overviewOverlay, territoryOverlay;
        GameObject factoryRecipeSection, factoryFilterSection, factoryFeedSection;
        CanvasGroup noticeGroup;
        RectTransform buildViewport;
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
            if(Application.isMobilePlatform)UpdateMobileSafeArea(false);
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

        void BuildInterface()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            font = LoadFont();
            rounded = MakeRoundedSprite();

            Canvas canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = .5f;
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
            if (Application.isMobilePlatform) ApplyMobileSafeArea();
        }

        void BuildTopBar()
        {
            GameObject top = Panel("TopBar", transform, new Color(Navy.r,Navy.g,Navy.b,.98f), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -52), Vector2.zero);
            Resource[] shown = { Resource.Coins, Resource.Timber, Resource.Stone, Resource.Grain, Resource.Flour, Resource.Bread, Resource.Ore, Resource.Steel, Resource.Tools };
            float x = 8;
            foreach (Resource res in shown)
            {
                float width=res==Resource.Coins?82:68;
                GameObject chip = Box(res.ToString(), top.transform, new Vector2(x,-5), new Vector2(width,42), new Color(Navy2.r,Navy2.g,Navy2.b,.86f));
                Text t = Label("", chip.transform, 12, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);
                Rect(t.gameObject, Vector2.zero, Vector2.one, new Vector2(3, 2), new Vector2(-3, -2));
                resourceTexts[res] = t;
                FeelUiFeedback.AttachResource(t,res==Resource.Coins?Gold:Teal);
                x += width+4;
            }
            populationText = Label("", top.transform, 12, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);
            Rect(populationText.gameObject,new Vector2(1,1),new Vector2(1,1),new Vector2(-302,-26),new Vector2(250,42));

            Button research = MakeButton("기술 연구", top.transform, new Vector2(-8,-4), new Vector2(164,44), Gold, () => controller.ToggleResearch(), new Vector2(1,1), 12);
            research.name="Button_기술 연구";
            AddButtonIcon(research,"info");
            eraText = research.GetComponentInChildren<Text>();
            Rect(eraText.gameObject,new Vector2(0,.42f),Vector2.one,new Vector2(25,0),new Vector2(-3,-1));
            researchSummaryText = Label("", research.transform, 10, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);
            Rect(researchSummaryText.gameObject,Vector2.zero,new Vector2(1,.42f),new Vector2(25,1),new Vector2(-3,0));
        }

        void BuildObjective()
        {
            Button chip=MakeButton("현재 목표",transform,new Vector2(8,-60),new Vector2(250,44),new Color(Navy.r,Navy.g,Navy.b,.97f),ToggleObjectives,new Vector2(0,1),12);
            chip.name="Button_Objectives";
            objectiveChipText=chip.GetComponentInChildren<Text>();
            objectiveChipText.alignment=TextAnchor.MiddleLeft;
            Rect(objectiveChipText.gameObject,Vector2.zero,Vector2.one,new Vector2(12,2),new Vector2(-26,-2));
            LabelAt("›",chip.transform,18,Coral,FontStyle.Bold,new Vector2(224,-10),new Vector2(18,24)).alignment=TextAnchor.MiddleCenter;

            objectivePanel = Box("Objectives", transform, new Vector2(8, -110), new Vector2(276, 160), new Color(Paper.r,Paper.g,Paper.b,.98f), new Vector2(0,1));
            LabelAt("현재 목표", objectivePanel.transform, 11, Coral, FontStyle.Bold, new Vector2(14,-10), new Vector2(200,18));
            Button close=MakeButton("닫기",objectivePanel.transform,new Vector2(210,-4),new Vector2(58,44),Navy2,()=>SetObjectives(false),new Vector2(0,1),11);
            close.name="Button_ObjectivesClose";
            objectiveTitle = LabelAt("", objectivePanel.transform, 16, Cream, FontStyle.Bold, new Vector2(14,-34), new Vector2(244,24));
            objectiveBody = LabelAt("", objectivePanel.transform, 12, Ink, FontStyle.Normal, new Vector2(14,-62), new Vector2(248,50));
            objectiveProgress = LabelAt("", objectivePanel.transform, 12, Teal, FontStyle.Bold, new Vector2(14,-120), new Vector2(248,22));
            FeelUiFeedback.AttachPanel(objectivePanel);
            objectivePanel.SetActive(false);
        }

        void BuildInspector()
        {
            inspectorPanel = Box("Inspector", transform, new Vector2(-8,-60), new Vector2(276,230), new Color(Paper.r,Paper.g,Paper.b,.98f), new Vector2(1,1));
            inspectorTitle = LabelAt("선택 정보", inspectorPanel.transform, 16, Cream, FontStyle.Bold, new Vector2(14,-11), new Vector2(210,26));
            Button close=MakeButton("닫기",inspectorPanel.transform,new Vector2(-8,-4),new Vector2(58,44),Navy2,CloseInspector,new Vector2(1,1),11);
            close.name="Button_InspectorClose";
            inspectorBody = LabelAt("", inspectorPanel.transform, 12, Ink, FontStyle.Normal, new Vector2(14,-43), new Vector2(248,136));
            inspectorBody.alignment = TextAnchor.UpperLeft;
            upgradeButton=MakeButton("선택 건물 업그레이드", inspectorPanel.transform, new Vector2(14,-178), new Vector2(248,44), Teal, () => controller.UpgradeSelected(), new Vector2(0,1),11);
            upgradeLabel=upgradeButton.GetComponentInChildren<Text>();
            factoryConfigureButton=MakeButton("설비 구성",inspectorPanel.transform,new Vector2(14,-178),new Vector2(248,44),Teal,()=>SetFactoryModal(true),new Vector2(0,1),11);
            factoryConfigureButton.name="Button_FactoryConfigure";
            factoryConfigureButton.gameObject.SetActive(false);
            FeelUiFeedback.AttachPanel(inspectorPanel);
            inspectorPanel.SetActive(false);
        }

        void BuildBottomBar()
        {
            GameObject bottom = Panel("BuildBar", transform, new Color(Navy.r,Navy.g,Navy.b,.98f), new Vector2(0,0), new Vector2(1,0), new Vector2(8,8), new Vector2(-8,60));
            string[] cats = { "주거", "생산", "산업", "도시", "설비", "물류" };
            for (int i=0;i<cats.Length;i++)
            {
                string c=cats[i];
                string label=c=="설비"?"설비 G":c;
                Button tab=MakeButton(label,bottom.transform,new Vector2(6+i*66,-4),new Vector2(62,44),c==category?Coral:Navy2,()=>SelectCategory(c),new Vector2(0,1),12);
                tab.name=c=="설비"?"Button_Factory":"Button_"+c;
                categoryButtons[c]=tab;
            }
            Button collapse=MakeButton("도구  ▴",bottom.transform,new Vector2(406,-4),new Vector2(86,44),Navy2,ToggleBuildTray,new Vector2(0,1),11);
            collapse.name="Button_BuildCollapse";
            Button remove=MakeButton("철거",bottom.transform,new Vector2(498,-4),new Vector2(66,44),new Color(.55f,.28f,.25f,1),SelectRemoveTool,new Vector2(0,1),12);
            remove.name="Button_철거";
            Button menu=MakeButton("메뉴",bottom.transform,new Vector2(-6,-4),new Vector2(76,44),Navy2,OpenMenu,new Vector2(1,1),12);
            menu.name="Button_Menu";AddButtonIcon(menu,"menu");

            buildChoicesPanel=Panel("BuildChoices",transform,new Color(Navy.r,Navy.g,Navy.b,.98f),new Vector2(0,0),new Vector2(1,0),new Vector2(8,68),new Vector2(-8,140));
            GameObject viewport = new GameObject("BuildChoiceViewport", typeof(RectTransform),typeof(RectMask2D));
            viewport.transform.SetParent(buildChoicesPanel.transform,false); Rect(viewport,new Vector2(0,0),new Vector2(1,1),new Vector2(10,8),new Vector2(-300,-8));
            buildViewport=viewport.GetComponent<RectTransform>();
            HorizontalLayoutGroup layout=viewport.AddComponent<HorizontalLayoutGroup>(); layout.spacing=6; layout.padding=new RectOffset(0,0,0,0);layout.childForceExpandWidth=false; layout.childForceExpandHeight=true;
            foreach (BuildingSpec spec in Catalog.All)
            {
                if (spec.Kind == BuildingKind.None || spec.Kind == BuildingKind.TownHall) continue;
                Button b=MakeButton(spec.Name,viewport.transform,Vector2.zero,new Vector2(96,56),Navy2,()=>SelectCityTool(spec.Kind),Vector2.zero,11);
                LayoutElement le=b.gameObject.AddComponent<LayoutElement>(); le.preferredWidth=96; le.preferredHeight=56;
                buildButtons[spec.Kind]=b; buildLabels[spec.Kind]=b.GetComponentInChildren<Text>();
                AddHover(b.gameObject,()=>{hoveredKind=spec.Kind;RefreshBuildInfo();},()=>{hoveredKind=BuildingKind.None;RefreshBuildInfo();});
            }
            foreach (FactorySpec spec in FactoryCatalog.All)
            {
                if(spec==null || spec.Kind==FactoryKind.None)continue;
                FactoryKind kind=spec.Kind;
                Button b=MakeButton(spec.Name,viewport.transform,Vector2.zero,new Vector2(104,56),Navy2,()=>SelectFactoryTool(kind),Vector2.zero,11);
                LayoutElement le=b.gameObject.AddComponent<LayoutElement>();le.preferredWidth=104;le.preferredHeight=56;
                factoryButtons[kind]=b;factoryLabels[kind]=b.GetComponentInChildren<Text>();
                AddHover(b.gameObject,()=>{hoveredFactoryKind=kind;RefreshBuildInfo();},()=>{hoveredFactoryKind=FactoryKind.None;RefreshBuildInfo();});
            }
            buildInfo = Label("", buildChoicesPanel.transform, 11, Cream, FontStyle.Normal, TextAnchor.UpperLeft);
            Rect(buildInfo.gameObject,new Vector2(1,0),new Vector2(1,1),new Vector2(-286,8),new Vector2(-10,-8));
            buildInfo.alignment=TextAnchor.UpperLeft;

            activeToolPanel=Box("ActiveTool",transform,new Vector2(0,68),new Vector2(460,52),new Color(Navy.r,Navy.g,Navy.b,.98f),new Vector2(.5f,0));
            modeText=LabelAt("",activeToolPanel.transform,12,Cream,FontStyle.Bold,new Vector2(12,-7),new Vector2(230,36));
            modeText.alignment=TextAnchor.MiddleLeft;
            Button cancel=MakeButton("취소",activeToolPanel.transform,new Vector2(250,-4),new Vector2(86,44),Navy2,CancelTool,new Vector2(0,1),12);
            cancel.name="Button_ToolCancel";
            factoryRotateButton=MakeButton("회전  R",activeToolPanel.transform,new Vector2(342,-4),new Vector2(106,44),Gold,RotateFactoryTool,new Vector2(0,1),11);
            factoryRotateButton.name="Button_FactoryRotate";AddButtonIcon(factoryRotateButton,"rotate");

            float[] speeds={0,1,3}; string[] names={"Ⅱ","1×","3×"};
            for(int i=0;i<3;i++){float s=speeds[i];Button b=MakeButton(names[i],bottom.transform,new Vector2(570+i*48,-4),new Vector2(44,44),Navy2,()=>controller.SetSpeed(s),new Vector2(0,1),11);speedButtons.Add(b);}
            Button overview=MakeButton("현황",bottom.transform,new Vector2(730,-4),new Vector2(72,44),Navy2,()=>OpenUtility("overview"),new Vector2(0,1),11);overview.name="Button_Overview";
            Button territory=MakeButton("영토",bottom.transform,new Vector2(808,-4),new Vector2(72,44),Navy2,()=>OpenUtility("territory"),new Vector2(0,1),11);territory.name="Button_Territory";
            Button trade=MakeButton("교역",bottom.transform,new Vector2(886,-4),new Vector2(72,44),Teal,()=>OpenUtility("trade"),new Vector2(0,1),11);trade.name="Button_Trade";
            FeelUiFeedback.AttachPanel(buildChoicesPanel);
            FeelUiFeedback.AttachPanel(activeToolPanel);
            buildChoicesPanel.SetActive(false);
            activeToolPanel.SetActive(false);
        }

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
            if(mobileSafeAreaRoot==null || Screen.width<=0 || Screen.height<=0)return;
            Rect safe=Screen.safeArea;
            if(!force && appliedScreenWidth==Screen.width && appliedScreenHeight==Screen.height && appliedSafeArea==safe)return;
            mobileSafeAreaRoot.anchorMin=new Vector2(safe.xMin/Screen.width,safe.yMin/Screen.height);
            mobileSafeAreaRoot.anchorMax=new Vector2(safe.xMax/Screen.width,safe.yMax/Screen.height);
            mobileSafeAreaRoot.offsetMin=mobileSafeAreaRoot.offsetMax=Vector2.zero;
            appliedSafeArea=safe;
            appliedScreenWidth=Screen.width;
            appliedScreenHeight=Screen.height;
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
            populationText.text="주민 "+s.Population+" · 행복 "+s.Happiness+"%\n"+s.Day+"일 · 수입 "+income+"G";
            RefreshResearch();
            objectiveTitle.text=sim.ObjectiveTitle ?? "도시를 성장시키세요";
            objectiveBody.text=sim.ObjectiveDescription ?? "생산망과 주거지를 연결하세요.";
            objectiveProgress.text="진행도  "+Mathf.RoundToInt(Mathf.Clamp01(sim.ObjectiveProgress)*100f)+"%";
            objectiveChipText.text="목표  ·  "+objectiveTitle.text+"  "+Mathf.RoundToInt(Mathf.Clamp01(sim.ObjectiveProgress)*100f)+"%";
            RefreshInspector(); RefreshBuildChoices(); RefreshBuildInfo();
            RefreshOverview();
            RefreshFeelModeLabel();
            for(int i=0;i<speedButtons.Count;i++){float v=i==0?0:i==1?1:3; SetButtonColor(speedButtons[i],Mathf.Approximately(controller.GameSpeed,v)?Coral:Navy2);}
            FactoryKind factoryTool=controller.Factory==null?FactoryKind.None:controller.Factory.SelectedTool;
            modeText.text=controller.Factory!=null&&controller.Factory.RemovalMode?"설비 철거":factoryTool!=FactoryKind.None?FactoryCatalog.Get(factoryTool).Name+" 배치":controller.DemolitionMode?"철거 모드":controller.SelectedTool!=BuildingKind.None?Catalog.Get(controller.SelectedTool).Name+" 배치":"건설 도구";
            helpOverlay.SetActive(controller.HelpOpen);
            researchOverlay.SetActive(controller.ResearchOpen);
            RefreshModalOverlays();
            if(controller.NoticeVersion!=lastNoticeVersion)
            {
                lastNoticeVersion=controller.NoticeVersion;
                if(!string.IsNullOrEmpty(controller.Notice)){noticeText.text=controller.Notice;noticeVisibleUntil=Time.unscaledTime+4f;}
            }
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
                string configuration=machine.Kind==FactoryKind.Furnace||machine.Kind==FactoryKind.Assembler?"\n제조법  "+FactoryCatalog.RecipeName(machine.Recipe):machine.Kind==FactoryKind.Inserter?"\n필터  "+(machine.Filter==Resource.Coins?"모든 물자":Catalog.ResourceName(machine.Filter)):"";
                inspectorBody.text="설비 #"+machine.Id+"  ·  미세 좌표 "+machine.X+", "+machine.Z+"\n방향  "+DirectionText(machine.Direction)+"  ·  크기 "+spec.Width+"×"+spec.Height+" 미세 칸\n전력  "+(machine.Powered?"공급됨":"공급 필요")+"  ·  수요 "+spec.PowerDemand.ToString("0.##")+configuration+"\n투입  "+FactoryInventoryText(machine.Input)+"\n출력  "+FactoryInventoryText(machine.Output)+"\n"+FactoryActivityText(machine)+(string.IsNullOrEmpty(machine.Status)?"상태 확인 중":machine.Status);
                upgradeButton.gameObject.SetActive(false);
                factoryConfigureButton.gameObject.SetActive(true);
                factoryConfigureButton.interactable=controller.Factory!=null;
            }
            else if(hasResident)
            {
                inspectorTitle.text="주민 정보";
                inspectorBody.text=controller.ResidentDetails+"\n\n현재 거리의 주민  "+controller.PeopleOnStreet+"명";
            }
            else
            {
                bool owned=controller.Sim.IsOwned(c.X,c.Z); int region=(c.Z/7)*3+c.X/7;
                string building=c.Building==BuildingKind.None?"빈 터":Catalog.Get(c.Building).Name;
                inspectorTitle.text=building;
                string recipe=c.Building==BuildingKind.None?"":RecipeText(Catalog.Get(c.Building),c.Level);
                inspectorBody.text="좌표  "+c.X+", "+c.Z+"  ·  지역 "+(region+1)+"\n"+(owned?"내 영토":"미소유 영토")+"  ·  "+TerrainName(c.Terrain)+"\n"+(c.Building==BuildingKind.None?"":("레벨 "+c.Level+"  ·  "+(c.Connected?"도로 연결됨":"연결 끊김")+"\n"))+(!string.IsNullOrEmpty(c.Status)?c.Status:"상태 양호")+(string.IsNullOrEmpty(recipe)?"":"\n"+recipe)+"\n동력 "+controller.PowerUsageText+" / "+controller.Sim.PowerCapacity;
            }
            if(machine==null)
            {
                upgradeButton.gameObject.SetActive(true);
                factoryConfigureButton.gameObject.SetActive(false);
                RefreshUpgrade(c);
            }
        }

        void RefreshBuildChoices()
        {
            FactoryKind selectedFactory=controller.Factory==null?FactoryKind.None:controller.Factory.SelectedTool;
            bool activeTool=controller.SelectedTool!=BuildingKind.None||controller.DemolitionMode||selectedFactory!=FactoryKind.None||(controller.Factory!=null&&controller.Factory.RemovalMode);
            if(buildChoicesPanel!=null)buildChoicesPanel.SetActive(buildTrayOpen&&!activeTool);
            if(activeToolPanel!=null)activeToolPanel.SetActive(activeTool);
            foreach(var tab in categoryButtons) SetButtonColor(tab.Value,tab.Key==category?Coral:Navy2);
            bool factoryCategory=IsFactoryCategory(category);
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
                buildLabels[pair.Key].text=spec.Name+"\n"+(unlocked?spec.Cost+"G":ShortReason(lockReason));
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
                factoryLabels[pair.Key].text=spec.Name+"\n"+(available?spec.CoinCost+"G":ShortReason(reason));
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
        }

        public bool VerifyLayout(out string reason)
        {
            if(buildViewport==null || buildViewport.rect.height<=0){reason="건설 목록 영역의 높이가 올바르지 않습니다.";return false;}
            int expectedFactoryCount=FactoryCatalog.All.Count(spec=>spec!=null && spec.Kind!=FactoryKind.None);
            if(categoryButtons.Count!=6 || !categoryButtons.ContainsKey("설비") || !categoryButtons.ContainsKey("물류")){reason="도시와 설비 분류 탭이 모두 생성되지 않았습니다.";return false;}
            if(factoryButtons.Count!=expectedFactoryCount || factoryRotateButton==null){reason="설비 도구 또는 회전 버튼이 모두 생성되지 않았습니다.";return false;}
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
            if(factoryRecipeButtons.Count!=4 || factoryFilterButtons.Count!=9 || factoryFeedButtons.Count!=8){reason="설비 구성 선택지가 모두 생성되지 않았습니다.";return false;}
            int expectedResearchCount=TechCatalog.All.Count(spec=>spec!=null);
            if(researchOverlay==null || researchWindow==null || researchCardTexts.Count!=expectedResearchCount || researchButtons.Count!=expectedResearchCount){reason=expectedResearchCount+"개 기술 카드가 모두 생성되지 않았습니다.";return false;}
            Image blocker=researchOverlay.GetComponent<Image>();
            if(blocker==null || !blocker.raycastTarget){reason="연구 화면이 월드 입력을 차단하지 않습니다.";return false;}
            RectTransform overlayRect=researchOverlay.transform as RectTransform;
            if(overlayRect==null || !ContainsBounds(overlayRect,researchWindow,1f)){reason="연구 창이 현재 화면 영역을 벗어났습니다.";return false;}
            foreach(var viewport in researchViewports)
                if(viewport.Value==null || viewport.Value.rect.width<=0 || viewport.Value.rect.height<=0 || !ContainsBounds(researchWindow,viewport.Value,1f)){reason="연구 시대 스크롤 영역이 창을 벗어났습니다: "+viewport.Key;return false;}
            TechId[] initialResearch={TechId.Stonecraft,TechId.CropRotation};
            foreach(TechId id in initialResearch)
            {
                TechSpec spec=TechCatalog.Get(id);
                if(spec==null)continue;
                RectTransform viewport;
                RectTransform cardRect;
                if(!researchViewports.TryGetValue(spec.Era,out viewport) || !researchCardRects.TryGetValue(id,out cardRect) || !ContainsBounds(viewport,cardRect,1f)){reason="초기 연구 버튼이 첫 화면에 보이지 않습니다: "+id;return false;}
            }
            foreach(var pair in researchButtons)
            {
                RectTransform rect=pair.Value.transform as RectTransform;
                if(rect==null || rect.rect.width<200 || rect.rect.height<24){reason="기술 연구 버튼 영역이 올바르지 않습니다: "+pair.Key;return false;}
                if(pair.Value.gameObject.name!="Button_연구 시작_"+pair.Key){reason="기술 연구 버튼 이름이 안정적이지 않습니다: "+pair.Key;return false;}
            }
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
                case FactoryKind.ImportDock:
                case FactoryKind.ExportDock:return "물류";
                default:return "설비";
            }
        }

        static string DirectionText(int direction)
        {
            switch((direction%4+4)%4){case 0:return "동쪽 →";case 1:return "북쪽 ↑";case 2:return "서쪽 ←";default:return "남쪽 ↓";}
        }

        static string FactoryInventoryText(List<int> inventory)
        {
            if(inventory==null)return "없음";
            var values=new List<string>();
            for(int i=1;i<Math.Min(9,inventory.Count);i++)if(inventory[i]>0)values.Add(Catalog.ResourceName((Resource)i)+" "+inventory[i]);
            return values.Count==0?"없음":string.Join(" · ",values);
        }

        static string FactoryActivityText(FactoryEntity machine)
        {
            if(machine==null)return "";
            if((int)machine.CargoResource>=1&&(int)machine.CargoResource<=8)return "운송 "+Catalog.ResourceName(machine.CargoResource)+" "+Mathf.RoundToInt(Mathf.Clamp01(machine.CargoProgress)*100f)+"%  ·  ";
            float duration=machine.Kind==FactoryKind.Drill?2f:FactoryCatalog.RecipeDuration(machine.Recipe);
            return duration>0?"공정 "+Mathf.RoundToInt(Mathf.Clamp01(machine.Progress/duration)*100f)+"%  ·  ":"";
        }

        static string TerrainName(TerrainKind terrain)
        {
            switch(terrain){case TerrainKind.Forest:return "숲";case TerrainKind.Rock:return "바위";case TerrainKind.Water:return "물";default:return "평지";}
        }

        static string ShortReason(string reason)
        {
            if(string.IsNullOrEmpty(reason)) return "잠김";
            const int max=13;
            string clean=reason.Replace("필요합니다."," 필요").Replace("필요합니다"," 필요").Replace("연구가 ","");
            return clean.Length<=max?clean:clean.Substring(0,max-1)+"…";
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
            bool hasTool=controller.SelectedTool!=BuildingKind.None||controller.DemolitionMode||(controller.Factory!=null&&(controller.Factory.SelectedTool!=FactoryKind.None||controller.Factory.RemovalMode));
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
            GameObject go=new GameObject("Text",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);Text t=go.GetComponent<Text>();t.text=value;t.font=font;t.fontSize=size;t.color=color;t.fontStyle=style;t.alignment=align;t.supportRichText=true;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;
        }

        Text LabelAt(string value,Transform parent,int size,Color color,FontStyle style,Vector2 pos,Vector2 dimensions)
        {Text t=Label(value,parent,size,color,style,TextAnchor.UpperLeft);Rect(t.gameObject,new Vector2(0,1),new Vector2(0,1),pos,dimensions);((RectTransform)t.transform).pivot=new Vector2(0,1);return t;}

        Button MakeButton(string value,Transform parent,Vector2 pos,Vector2 size,Color color,UnityEngine.Events.UnityAction click,Vector2 pivot,int textSize=13)
        {
            GameObject go=Box("Button_"+value,parent,pos,size,color,pivot);Button b=go.AddComponent<Button>();Image image=go.GetComponent<Image>();image.sprite=HudAssets.Panel??rounded;image.type=Image.Type.Sliced;b.targetGraphic=image;ColorBlock cb=b.colors;cb.normalColor=Color.white;cb.highlightedColor=new Color(1.08f,1.08f,1.08f,1);cb.pressedColor=new Color(.82f,.82f,.82f,1);cb.disabledColor=new Color(.56f,.56f,.56f,.7f);b.colors=cb;b.onClick.AddListener(click);Text t=Label(value,go.transform,textSize,Cream,FontStyle.Bold,TextAnchor.MiddleCenter);Rect(t.gameObject,Vector2.zero,Vector2.one,new Vector2(4,2),new Vector2(-4,-2));FeelUiFeedback.AttachButton(b);return b;
        }

        void AddButtonIcon(Button button,string semanticName)
        {
            if(button==null)return;
            Sprite sprite=HudAssets.Icon(semanticName);
            if(sprite==null)return;
            GameObject icon=new GameObject("Icon_"+semanticName,typeof(RectTransform),typeof(Image));
            icon.transform.SetParent(button.transform,false);
            Rect(icon,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(16,0),new Vector2(20,20));
            Image image=icon.GetComponent<Image>();image.sprite=sprite;image.color=Cream;image.preserveAspect=true;image.raycastTarget=false;
            Text label=button.GetComponentInChildren<Text>();
            if(label!=null)Rect(label.gameObject,Vector2.zero,Vector2.one,new Vector2(26,2),new Vector2(-4,-2));
        }

        void AddHover(GameObject go,UnityEngine.Events.UnityAction enter,UnityEngine.Events.UnityAction exit)
        {EventTrigger tr=go.AddComponent<EventTrigger>();AddTrigger(tr,EventTriggerType.PointerEnter,enter);AddTrigger(tr,EventTriggerType.PointerExit,exit);}
        void AddClick(GameObject go,PointerEventData.InputButton button,UnityEngine.Events.UnityAction action)
        {EventTrigger tr=go.GetComponent<EventTrigger>();if(tr==null)tr=go.AddComponent<EventTrigger>();EventTrigger.Entry e=new EventTrigger.Entry{eventID=EventTriggerType.PointerClick};e.callback.AddListener(d=>{PointerEventData p=d as PointerEventData;if(p!=null&&p.button==button)action();});tr.triggers.Add(e);}
        static void AddTrigger(EventTrigger tr,EventTriggerType type,UnityEngine.Events.UnityAction a){EventTrigger.Entry e=new EventTrigger.Entry{eventID=type};e.callback.AddListener(_=>a());tr.triggers.Add(e);}
        static void SetButtonColor(Button b,Color c){if(b!=null&&b.targetGraphic!=null)b.targetGraphic.color=c;}

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

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    public sealed partial class Hud
    {
        readonly Dictionary<Resource, Button> modalSellButtons = new Dictionary<Resource, Button>();
        readonly Dictionary<Resource, Text> modalTradeHoldingLabels = new Dictionary<Resource, Text>();
        readonly Dictionary<RectTransform, Vector2> modalScrollableBodies = new Dictionary<RectTransform, Vector2>();
        RectTransform modalHelpWindow, modalMenuWindow, modalOverviewWindow, modalTerritoryWindow, modalTradeWindow, modalConfirmWindow;
        RectTransform modalFactoryBodyContent;
        Text modalTradeCoins;
        int modalLayoutWidth = -1, modalLayoutHeight = -1;

        void BuildOverlays()
        {
            GameObject notice=Box("Notice",transform,new Vector2(0,-60),new Vector2(500,38),new Color(HudStyle.Surface.r,HudStyle.Surface.g,HudStyle.Surface.b,.94f),new Vector2(.5f,1));
            noticeText = Label("", notice.transform, HudStyle.BodySize, HudStyle.Text, FontStyle.Normal, TextAnchor.MiddleCenter);
            Rect(noticeText.gameObject,Vector2.zero,Vector2.one,new Vector2(12,2),new Vector2(-12,-2));
            noticeGroup=notice.AddComponent<CanvasGroup>(); noticeGroup.alpha=0; noticeGroup.blocksRaycasts=false; noticeGroup.interactable=false;

            helpOverlay = Overlay("HelpOverlay");
            GameObject help=ModalCard("HelpCard","RIVERWORKS 안내서",new Vector2(680,540),"Button_HelpClose",()=>SetHelp(false));
            modalHelpWindow=help.GetComponent<RectTransform>();
            Text h=LabelAt("중세 도시를 키우는 법\n\n1. 시청에서 흙길을 이어 주택·생산지·서재를 연결하세요.\n2. 곡물과 빵을 공급하고 연구로 새 건물과 설비를 여세요.\n3. 산업 설비도 같은 도시의 보유 영토에 놓습니다. 별도 공장 화면은 없습니다.\n4. 설비의 2×2 미세 칸은 도시 부지 1칸입니다. 방향과 지형을 배치 전에 확인하세요.\n5. 생산지·창고·시장과 설비는 투입기·벨트·도로로 실제 물자를 주고받습니다.\n6. 주민이나 건물, 설비를 누르면 오른쪽에서 상태와 구성을 확인할 수 있습니다.\n\n조작\n좌클릭: 건설/선택   우클릭 드래그: 화면 이동\nWASD: 화면 이동   휠: 확대/축소   Q/E: 카메라 회전   F: 시청 보기\nT: 기술 연구   G: 산업 설비 목록   H: 주택\nR: 설비 선택 중 회전 / 그 외 흙길   Delete: 철거\nEsc: 닫기/도구 취소   Space: 정지/재생   1/2/3: 속도\nF5: 저장   M: 소리   F1: 도움말\n\n교역 창에서 각 물자의 구매 또는 판매 버튼을 누르세요. 거래는 10개 단위입니다.",help.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(28,-72),new Vector2(624,440)); h.alignment=TextAnchor.UpperLeft;
            MakeModalBodyScrollable(help,504);
            FeelUiFeedback.AttachPanel(help);
            helpOverlay.SetActive(false);

            BuildResearchOverlay();
            BuildFactoryConfigureOverlay();
            BuildUtilityOverlays();
            GameObject industryObject=new GameObject("IndustryPanel",typeof(RectTransform),typeof(Image),typeof(IndustryPanel));
            industryObject.transform.SetParent(transform,false);
            Industry=industryObject.GetComponent<IndustryPanel>();
            Industry.Initialize(controller,RefreshModalOverlays);
            GameObject automationObject=new GameObject("AutomationRulePanel",typeof(RectTransform),typeof(Image),typeof(AutomationRulePanel));
            automationObject.transform.SetParent(transform,false);
            Automation=automationObject.GetComponent<AutomationRulePanel>();
            Automation.Initialize(controller,RefreshModalOverlays);
            GameObject projectsObject=new GameObject("CityProjectPanel",typeof(RectTransform),typeof(Image),typeof(CityProjectPanel));
            projectsObject.transform.SetParent(transform,false);
            Projects=projectsObject.GetComponent<CityProjectPanel>();
            Projects.Initialize(controller,RefreshModalOverlays);

            confirmOverlay=Overlay("NewGameConfirm");
            GameObject cc=ModalCard("ConfirmCard","새 도시를 시작할까요?",new Vector2(460,235),"Button_ConfirmClose",()=>SetConfirm(false));
            modalConfirmWindow=cc.GetComponent<RectTransform>();
            Text warn=LabelAt("이전 도시는 별도 백업 파일로 보관됩니다.\n새 도시를 시작합니다.",cc.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(30,-82),new Vector2(400,55)); warn.alignment=TextAnchor.MiddleCenter;
            MakeButton("취소",cc.transform,new Vector2(40,-161),new Vector2(175,44),HudStyle.SurfaceRaised,()=>SetConfirm(false),new Vector2(0,1),HudStyle.BodySize);
            MakeButton("새 도시 시작",cc.transform,new Vector2(245,-161),new Vector2(175,44),HudStyle.Danger,()=>{SetConfirm(false);controller.NewGame();},new Vector2(0,1),HudStyle.BodySize);
            FeelUiFeedback.AttachPanel(cc);
            confirmOverlay.SetActive(false);
        }

        void BuildFactoryConfigureOverlay()
        {
            factoryConfigureOverlay=Overlay("FactoryConfigureOverlay");
            GameObject card=ModalCard("FactoryConfigureCard","설비 구성",new Vector2(900,700),"Button_FactoryConfigureClose",()=>SetFactoryModal(false));
            factoryConfigureWindow=card.GetComponent<RectTransform>();
            factoryConfigureSummary=LabelAt("",card.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(24,-61),new Vector2(852,86));
            factoryConfigureSummary.alignment=TextAnchor.UpperLeft;

            factoryPauseButton=MakeButton("일시 정지",card.transform,new Vector2(24,-152),new Vector2(128,44),HudStyle.SurfaceRaised,()=>{controller.Factory?.SetPaused(!(controller.SelectedFactory?.Paused??false));RefreshFactoryConfigure();},new Vector2(0,1),HudStyle.BodySize);
            factoryPauseButton.name="Button_FactoryPause";
            int[] clocks={50,100,150,200};
            for(int i=0;i<clocks.Length;i++)
            {
                int clock=clocks[i];
                Button button=MakeButton(clock+"%",card.transform,new Vector2(160+i*92,-152),new Vector2(84,44),HudStyle.SurfaceRaised,()=>{controller.Factory?.SetClock(clock);RefreshFactoryConfigure();},new Vector2(0,1),HudStyle.BodySize);
                button.name="Button_FactoryClock_"+clock;factoryClockButtons[clock]=button;
            }
            factoryRecoverButton=MakeButton("재고 회수",card.transform,new Vector2(548,-152),new Vector2(150,44),HudStyle.Danger,()=>{controller.Factory?.RecoverSelected();RefreshFactoryConfigure();},new Vector2(0,1),HudStyle.BodySize);
            factoryRecoverButton.name="Button_FactoryRecover";
            Button codex=MakeButton("산업 도감",card.transform,new Vector2(706,-152),new Vector2(170,44),HudStyle.Accent,OpenIndustryCodex,new Vector2(0,1),HudStyle.BodySize);
            codex.name="Button_FactoryIndustryCodex";
            factoryAutomationButton=MakeButton("자동 조건",card.transform,new Vector2(706,-202),new Vector2(170,44),HudStyle.Accent,OpenAutomationRules,new Vector2(0,1),HudStyle.BodySize);
            factoryAutomationButton.name="Button_FactoryAutomation";

            factoryRecipeSection=Box("FactoryRecipeSection",card.transform,new Vector2(24,-208),new Vector2(852,532),HudStyle.SurfaceRaised,new Vector2(0,1));
            Text recipeHeading=LabelAt("제조법",factoryRecipeSection.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(12,-8),new Vector2(80,22));
            IgnoreLayout(recipeHeading.gameObject);
            GridLayoutGroup recipeLayout=factoryRecipeSection.AddComponent<GridLayoutGroup>();
            recipeLayout.padding=new RectOffset(12,12,38,12);recipeLayout.cellSize=new Vector2(199,88);recipeLayout.spacing=new Vector2(8,8);recipeLayout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;recipeLayout.constraintCount=4;
            foreach(RecipeSpec spec in FactoryCatalog.Recipes.Where(item=>item!=null&&item.Id!=FactoryRecipe.None))
            {
                FactoryRecipe recipe=spec.Id;
                Button button=MakeButton(spec.Name,factoryRecipeSection.transform,Vector2.zero,new Vector2(199,88),HudStyle.SurfaceRaised,()=>ChooseFactoryRecipe(recipe),new Vector2(0,1),HudStyle.BodySize);
                button.name="Button_FactoryRecipe_"+recipe;factoryRecipeButtons[recipe]=button;
            }

            factoryFilterSection=Box("FactoryFilterSection",card.transform,new Vector2(24,-208),new Vector2(852,532),HudStyle.SurfaceRaised,new Vector2(0,1));
            Text filterHeading=LabelAt("운송 필터 · 자동 또는 같은 상의 물질만 선택",factoryFilterSection.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(12,-8),new Vector2(420,22));
            IgnoreLayout(filterHeading.gameObject);
            GridLayoutGroup filterGrid=factoryFilterSection.AddComponent<GridLayoutGroup>();
            filterGrid.padding=new RectOffset(12,8,38,8); filterGrid.cellSize=new Vector2(199,44); filterGrid.spacing=new Vector2(8,6); filterGrid.constraint=GridLayoutGroup.Constraint.FixedColumnCount; filterGrid.constraintCount=4;
            foreach(ResourceSpec resourceSpec in ResourceCatalog.All)
            {
                Resource resource=resourceSpec.Id;
                string label=resource==Resource.Coins?"자동":resourceSpec.Name;
                Button button=MakeButton(label,factoryFilterSection.transform,Vector2.zero,new Vector2(160,44),HudStyle.SurfaceRaised,()=>ChooseFactoryFilter(resource),new Vector2(0,1),HudStyle.BodySize);
                button.name="Button_FactoryFilter_"+resource;factoryFilterButtons[resource]=button;
            }

            factoryFeedSection=Box("FactoryFeedSection",card.transform,new Vector2(24,-208),new Vector2(852,532),HudStyle.SurfaceRaised,new Vector2(0,1));
            Text feedHeading=LabelAt("도시 재고 수동 투입 · 10개/L",factoryFeedSection.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(12,-8),new Vector2(260,22));
            IgnoreLayout(feedHeading.gameObject);
            GridLayoutGroup feedGrid=factoryFeedSection.AddComponent<GridLayoutGroup>();
            feedGrid.padding=new RectOffset(12,8,38,8); feedGrid.cellSize=new Vector2(199,44); feedGrid.spacing=new Vector2(8,6); feedGrid.constraint=GridLayoutGroup.Constraint.FixedColumnCount; feedGrid.constraintCount=4;
            foreach(ResourceSpec resourceSpec in ResourceCatalog.All.Where(item=>item.Id!=Resource.Coins))
            {
                Resource resource=resourceSpec.Id;
                Button button=MakeButton("",factoryFeedSection.transform,Vector2.zero,new Vector2(149,44),HudStyle.SurfaceRaised,()=>FeedFactory(resource),new Vector2(0,1),HudStyle.BodySize);
                button.name="Button_FactoryFeed_"+resource;factoryFeedButtons[resource]=button;factoryFeedLabels[resource]=button.GetComponentInChildren<Text>();
            }
            FeelUiFeedback.AttachPanel(card);
            modalFactoryBodyContent=MakeModalBodyScrollable(card,760);
            factoryConfigureOverlay.SetActive(false);
        }

        void BuildUtilityOverlays()
        {
            menuOverlay=Overlay("MenuOverlay");
            GameObject menu=ModalCard("MenuCard","도시 메뉴",new Vector2(430,556),"Button_MenuClose",CloseTransientPanels);
            modalMenuWindow=menu.GetComponent<RectTransform>();
            LabelAt("도시 정보",menu.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(24,-72),new Vector2(160,18));
            Button overview=MakeButton("현황",menu.transform,new Vector2(24,-96),new Vector2(118,48),HudStyle.SurfaceRaised,()=>OpenUtility("overview"),new Vector2(0,1),HudStyle.BodySize);
            overview.name="Button_MenuOverview";AddButtonIcon(overview,"info");
            Button territory=MakeButton("영토",menu.transform,new Vector2(156,-96),new Vector2(118,48),HudStyle.SurfaceRaised,()=>OpenUtility("territory"),new Vector2(0,1),HudStyle.BodySize);
            territory.name="Button_MenuTerritory";AddButtonIcon(territory,"map");
            Button trade=MakeButton("교역",menu.transform,new Vector2(288,-96),new Vector2(118,48),HudStyle.SurfaceRaised,()=>OpenUtility("trade"),new Vector2(0,1),HudStyle.BodySize);
            trade.name="Button_MenuTrade";

            LabelAt("시간",menu.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(24,-164),new Vector2(160,18));
            float[] speeds={0,1,3};string[] speedNames={"Ⅱ  정지","1×  보통","3×  빠르게"};
            for(int i=0;i<3;i++){float s=speeds[i];Button b=MakeButton(speedNames[i],menu.transform,new Vector2(24+i*128,-188),new Vector2(118,46),HudStyle.SurfaceRaised,()=>controller.SetSpeed(s),new Vector2(0,1),HudStyle.BodySize);}

            LabelAt("도시 관리",menu.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(24,-254),new Vector2(160,18));
            Button save=MakeButton("저장",menu.transform,new Vector2(24,-278),new Vector2(118,46),HudStyle.Positive,()=>controller.SaveGame(),new Vector2(0,1),HudStyle.BodySize);save.name="Button_저장";
            Button load=MakeButton("불러오기",menu.transform,new Vector2(156,-278),new Vector2(118,46),HudStyle.SurfaceRaised,()=>controller.LoadGame(),new Vector2(0,1),HudStyle.BodySize);load.name="Button_불러오기";
            Button fresh=MakeButton("새 도시",menu.transform,new Vector2(288,-278),new Vector2(118,46),HudStyle.Danger,()=>SetConfirm(true),new Vector2(0,1),HudStyle.BodySize);fresh.name="Button_새 도시";
            Button help=MakeButton("도움말  F1",menu.transform,new Vector2(24,-344),new Vector2(382,46),HudStyle.SurfaceRaised,()=>{CloseTransientPanels();SetHelp(true);},new Vector2(0,1),HudStyle.BodySize);help.name="Button_도움말";
            Button industry=MakeButton("산업 도감 · 37개 제조법",menu.transform,new Vector2(24,-400),new Vector2(382,44),HudStyle.Accent,OpenIndustryCodex,new Vector2(0,1),HudStyle.BodySize);
            industry.name="Button_IndustryCodex";
            Button projects=MakeButton("도시 프로젝트",menu.transform,new Vector2(24,-450),new Vector2(382,44),HudStyle.Accent,OpenCityProjects,new Vector2(0,1),HudStyle.BodySize);
            projects.name="Button_CityProjects";
            Button effects=MakeButton("",menu.transform,new Vector2(24,-500),new Vector2(382,44),HudStyle.SurfaceRaised,ToggleFeelEffects,new Vector2(0,1),HudStyle.BodySize);
            effects.name="Button_ReducedMotion";
            feelModeLabel=effects.GetComponentInChildren<Text>();
            RefreshFeelModeLabel();
            Button guide=MakeButton("단우에게 조언 듣기",menu.transform,new Vector2(24,-554),new Vector2(185,44),HudStyle.SurfaceRaised,()=>{CloseTransientPanels();controller.Tutorial?.OpenGuide();},new Vector2(0,1),HudStyle.BodySize);
            guide.name="Button_Tutorial";
            Button residentAi=MakeButton("주민 AI",menu.transform,new Vector2(221,-554),new Vector2(185,44),HudStyle.SurfaceRaised,()=>{CloseTransientPanels();controller.ResidentAi?.OpenSettings();},new Vector2(0,1),HudStyle.BodySize);
            residentAi.name="Button_ResidentAi";
            LabelAt("도시는 자동 저장됩니다.",menu.transform,HudStyle.BodySize,HudStyle.Positive,FontStyle.Normal,new Vector2(24,-608),new Vector2(382,22)).alignment=TextAnchor.MiddleCenter;
            MakeModalBodyScrollable(menu,640);
            FeelUiFeedback.AttachPanel(menu);
            menuOverlay.SetActive(false);

            overviewOverlay=Overlay("OverviewOverlay");
            GameObject overviewCard=ModalCard("OverviewCard","도시 현황",new Vector2(540,350),"Button_OverviewClose",CloseTransientPanels);
            modalOverviewWindow=overviewCard.GetComponent<RectTransform>();
            overviewBody=LabelAt("",overviewCard.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(28,-78),new Vector2(484,238));
            overviewBody.alignment=TextAnchor.UpperLeft;
            MakeModalBodyScrollable(overviewCard,316);
            FeelUiFeedback.AttachPanel(overviewCard);
            overviewOverlay.SetActive(false);

            territoryOverlay=Overlay("TerritoryOverlay");
            GameObject territoryCard=ModalCard("TerritoryCard","영토 확장",new Vector2(520,440),"Button_TerritoryClose",CloseTransientPanels);
            modalTerritoryWindow=territoryCard.GetComponent<RectTransform>();
            LabelAt("인접한 구역을 매입해 도시와 산업 설비를 함께 확장합니다.",territoryCard.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(24,-68),new Vector2(472,32));
            for(int i=0;i<9;i++)
            {
                int region=i;int col=i%3,row=2-i/3;
                Button b=MakeButton("",territoryCard.transform,new Vector2(24+col*158,-118-row*82),new Vector2(146,68),HudStyle.SurfaceRaised,()=>controller.BuyRegion(region),new Vector2(0,1),HudStyle.BodySize);
                b.name="Button_Region_"+(i+1);regionButtons.Add(b);regionLabels.Add(b.GetComponentInChildren<Text>());
            }
            MakeModalBodyScrollable(territoryCard,416);
            FeelUiFeedback.AttachPanel(territoryCard);
            territoryOverlay.SetActive(false);

            mobileTradeOverlay=Overlay("TradeOverlay");
            GameObject tradeCard=ModalCard("TradeCard","긴급 교역 · 10개 단위",new Vector2(760,620),"Button_TradeClose",CloseTransientPanels);
            modalTradeWindow=tradeCard.GetComponent<RectTransform>();
            modalTradeCoins=LabelAt("",tradeCard.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(470,-20),new Vector2(210,30));
            modalTradeCoins.gameObject.name="TradeHoldingsHeader";
            modalTradeCoins.alignment=TextAnchor.MiddleRight;
            ResourceSpec[] tradeResources=ResourceCatalog.TradeableResources.ToArray();
            for(int i=0;i<tradeResources.Length;i++)
            {
                Resource resource=tradeResources[i].Id;int col=i%2,row=i/2;float x=24+col*368,y=-82-row*86;
                Text holding=LabelAt("",tradeCard.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(x,y),new Vector2(94,56));
                holding.alignment=TextAnchor.MiddleLeft;modalTradeHoldingLabels[resource]=holding;
                Button buy=MakeButton("구매  -"+(GameController.TradePrice(resource)*10)+"G",tradeCard.transform,new Vector2(x+96,y),new Vector2(122,56),HudStyle.Positive,()=>controller.Trade(resource,true),new Vector2(0,1),HudStyle.BodySize);
                buy.name="Button_MobileBuy_"+resource;buyButtons[resource]=buy;
                Button sell=MakeButton("판매  +"+(Mathf.Max(1,GameController.TradePrice(resource)/2)*10)+"G",tradeCard.transform,new Vector2(x+224,y),new Vector2(122,56),HudStyle.Positive,()=>controller.Trade(resource,false),new Vector2(0,1),HudStyle.BodySize);
                sell.name="Button_MobileSell_"+resource;
                modalSellButtons[resource]=sell;
            }
            MakeModalBodyScrollable(tradeCard,Mathf.Max(588,94+Mathf.CeilToInt(tradeResources.Length/2f)*86),modalTradeCoins.transform);
            FeelUiFeedback.AttachPanel(tradeCard);
            mobileTradeOverlay.SetActive(false);
        }

        void BuildResearchOverlay()
        {
            researchOverlay = Overlay("ResearchOverlay");
            GameObject card = ModalCard("ResearchCard", "기술 연구 · 도시의 발전", new Vector2(1248, 592),
                "Button_ResearchClose", () => controller.ToggleResearch());
            researchWindow = card.GetComponent<RectTransform>();
            RectTransform body = ResearchUi.Rect("ResearchTree", card.transform);
            ResearchUi.Stretch(body, 16, 64, 16, 12);
            ResearchTree = body.gameObject.AddComponent<ResearchTreeView>();
            ResearchTree.Initialize(controller);
            FeelUiFeedback.AttachPanel(card);
            researchOverlay.SetActive(false);
        }

        void RefreshResearch()
        {
            GameState state = controller.State;
            eraText.text = TechCatalog.EraName(state.Era) + " · 지식 " + Mathf.FloorToInt(state.ResearchPoints) + "점";
            if (state.ActiveResearch == TechId.None)
            {
                int available = TechCatalog.All.Count(spec => controller.Sim.CanResearch(spec.Id, out _));
                researchSummaryText.text = "가능 " + available + " · +" + controller.Sim.ResearchPerDay.ToString("0.#") + "/일";
            }
            else
            {
                TechSpec active = TechCatalog.Get(state.ActiveResearch);
                researchSummaryText.text = active.Name + " " + Mathf.RoundToInt(controller.Sim.ResearchProgress * 100) + "%";
            }
            ResearchTree?.Refresh();
            RefreshTradeState();
        }

        void RefreshTradeState()
        {
            if(controller==null || controller.Sim==null)return;
            if(modalTradeCoins!=null)modalTradeCoins.text="보유 코인  "+controller.State.Coins.ToString("N0")+"G";
            foreach(var pair in modalTradeHoldingLabels)
            {
                int holding=Mathf.FloorToInt(controller.Sim.Get(pair.Key));
                ResourceSpec resource=ResourceCatalog.Get(pair.Key);
                pair.Value.text=resource.Name+"\n보유 "+holding.ToString("N0")+resource.Unit;
                Button buy;
                if(buyButtons.TryGetValue(pair.Key,out buy))buy.interactable=controller.State.Coins>=GameController.TradePrice(pair.Key)*10;
                Button sell;
                if(modalSellButtons.TryGetValue(pair.Key,out sell))sell.interactable=holding>=10;
            }
        }

        void RefreshOverview()
        {
            if(controller==null||overviewBody==null)return;
            bool breadShortage=controller.State.Cells.Any(cell=>cell.Connected&&cell.Building==BuildingKind.House&&!string.IsNullOrEmpty(cell.Status)&&cell.Status.StartsWith("빵 부족"));
            float breadDelivered=breadShortage?0:controller.State.LastBreadDemand;
            overviewBody.text="주민  "+controller.State.Population+"명  ·  행복 "+controller.State.Happiness+"%  ·  거리의 주민 "+controller.PeopleOnStreet+"명\n\n연결 건물  "+controller.Sim.ConnectedBuildings+"\n동력  "+controller.PowerUsageText+" / "+controller.Sim.PowerCapacity+"\n식량  곡물 "+controller.State.LastGrainConsumed.ToString("0.##")+" · 빵 "+breadDelivered.ToString("0.##")+" / "+controller.State.LastBreadDemand.ToString("0.##")+"\n생활 도구  "+controller.State.LastToolsDelivered.ToString("0.###")+" / "+controller.State.LastToolsDemand.ToString("0.###")+" 매일\n누적 도구 생산  "+controller.State.TotalToolsProduced.ToString("0.#")+"\n\n시대  "+TechCatalog.EraName(controller.State.Era)+"  ·  연구 점수 "+Mathf.FloorToInt(controller.State.ResearchPoints)+"점";
            for(int i=0;i<regionButtons.Count;i++)
            {
                bool owned=controller.State.OwnedRegions.Contains(i);int cost=controller.Sim.RegionCost(i);
                bool adjacent=controller.State.OwnedRegions.Any(o=>Mathf.Abs(o%3-i%3)+Mathf.Abs(o/3-i/3)==1);
                string availability=owned?"보유":!adjacent?"인접 필요":controller.State.Coins<cost?"코인 부족":"매입 가능";
                regionLabels[i].text=(i+1)+"구역\n"+cost+"G · "+availability;
                regionButtons[i].interactable=!owned&&adjacent&&controller.State.Coins>=cost;
                SetButtonColor(regionButtons[i],owned?HudStyle.Positive:HudStyle.SurfaceRaised);
            }
        }

        public void OpenMenu()
        {
            CloseStandalonePanels();
            menuOpen=true;overviewOpen=territoryOpen=mobileTradeOpen=confirmOpen=factoryModalOpen=false;
            controller.ModalOpen=true;
            RefreshModalOverlays();
        }

        void OpenUtility(string panel)
        {
            CloseStandalonePanels();
            menuOpen=false;
            overviewOpen=panel=="overview";
            territoryOpen=panel=="territory";
            mobileTradeOpen=panel=="trade";
            confirmOpen=factoryModalOpen=false;
            controller.ModalOpen=true;
            RefreshModalOverlays();
        }

        public void CloseTransientPanels()
        {
            CloseStandalonePanels();
            menuOpen=overviewOpen=territoryOpen=mobileTradeOpen=confirmOpen=factoryModalOpen=false;
            controller.ModalOpen=false;
            RefreshModalOverlays();
        }

        void SetHelp(bool open){if(controller.HelpOpen!=open) controller.ToggleHelp();}

        void SetConfirm(bool open)
        {
            if(open)CloseStandalonePanels();
            menuOpen=overviewOpen=territoryOpen=mobileTradeOpen=factoryModalOpen=false;
            confirmOpen=open;
            controller.ModalOpen=open;
            RefreshModalOverlays();
        }

        void SetFactoryModal(bool open)
        {
            if(open && controller.SelectedFactory==null)return;
            if(open)CloseStandalonePanels();
            factoryModalOpen=open;
            if(open)menuOpen=overviewOpen=territoryOpen=mobileTradeOpen=confirmOpen=false;
            controller.ModalOpen=open;
            RefreshModalOverlays();
        }

        void CloseStandalonePanels()
        {
            if(Automation!=null&&Automation.IsOpen)Automation.Close();
            if(Projects!=null&&Projects.IsOpen)Projects.Close();
            if(Industry!=null&&Industry.IsOpen)Industry.Close();
        }

        void RefreshModalOverlays()
        {
            if(controller==null)return;
            if(!controller.ModalOpen)menuOpen=overviewOpen=territoryOpen=mobileTradeOpen=confirmOpen=factoryModalOpen=false;
            if(factoryModalOpen && controller.SelectedFactory==null){factoryModalOpen=false;controller.ModalOpen=false;}
            if(menuOverlay!=null)menuOverlay.SetActive(controller.ModalOpen&&menuOpen);
            if(overviewOverlay!=null)overviewOverlay.SetActive(controller.ModalOpen&&overviewOpen);
            if(territoryOverlay!=null)territoryOverlay.SetActive(controller.ModalOpen&&territoryOpen);
            if(confirmOverlay!=null)confirmOverlay.SetActive(controller.ModalOpen&&confirmOpen);
            if(mobileTradeOverlay!=null)mobileTradeOverlay.SetActive(controller.ModalOpen && mobileTradeOpen);
            if(factoryConfigureOverlay!=null)factoryConfigureOverlay.SetActive(controller.ModalOpen && factoryModalOpen);
            if(factoryModalOpen)RefreshFactoryConfigure();
            Canvas.ForceUpdateCanvases();
        }

        void RefreshFactoryConfigure()
        {
            FactoryEntity machine=controller.SelectedFactory;
            if(machine==null || controller.Factory==null)return;
            FactorySpec spec=FactoryCatalog.Get(machine.Kind);
            RecipeSpec active=FactoryCatalog.GetRecipe(machine.Recipe);
            string process=active==null?"": "\n"+FactoryCatalog.InputText(active.Id)+" → "+FactoryCatalog.OutputText(active.Id)+" · "+active.Duration.ToString("0.#")+"초 · 기본 분당 "+RecipeRateText(active);
            factoryConfigureSummary.text=spec.Name+" #"+machine.Id+" · "+DirectionText(machine.Direction)+" · "+(machine.Paused?"일시 정지":string.IsNullOrEmpty(machine.Status)?"상태 확인 중":machine.Status)+
                " · 가동률 "+machine.ClockPercent+"%\n투입 ["+FactoryInventoryText(machine.Input)+"]  /  출력 ["+FactoryInventoryText(machine.Output)+"] · 용량 "+spec.InputCapacity+"/"+spec.OutputCapacity+process;
            factoryPauseButton.GetComponentInChildren<Text>().text=machine.Paused?"가동 재개":"일시 정지";
            SetButtonColor(factoryPauseButton,machine.Paused?HudStyle.Positive:HudStyle.SurfaceRaised);
            bool recoverable=HasFactoryInventory(machine);
            factoryRecoverButton.interactable=recoverable;
            factoryRecoverButton.GetComponentInChildren<Text>().text=recoverable?"재고 회수":"회수할 재고 없음";
            bool receiver=(machine.Kind==FactoryKind.ItemLift||machine.Kind==FactoryKind.FluidRiser)&&!machine.IsLinkSender;
            factoryAutomationButton.interactable=!receiver;
            factoryAutomationButton.GetComponentInChildren<Text>().text=receiver?"자동 조건 · 송신측에서 설정":"자동 조건";
            bool clockable=FactoryCatalog.IsClockable(machine.Kind);
            foreach(var pair in factoryClockButtons)
            {
                pair.Value.gameObject.SetActive(clockable);
                if(!clockable)continue;
                bool advanced=pair.Key<=100||TechCatalog.Has(controller.State,TechId.AdvancedManufacturing);
                pair.Value.interactable=advanced;
                pair.Value.GetComponentInChildren<Text>().text=pair.Key+"%"+(advanced?"":"\n고급 제조 필요");
                SetButtonColor(pair.Value,machine.ClockPercent==pair.Key?Coral:HudStyle.SurfaceRaised);
            }

            bool recipes=FactoryCatalog.IsProduction(machine.Kind);
            factoryRecipeSection.SetActive(recipes);
            foreach(var pair in factoryRecipeButtons)
            {
                bool compatible=FactoryCatalog.IsRecipeCompatible(machine.Kind,pair.Key);
                pair.Value.gameObject.SetActive(compatible);
                if(!compatible)continue;
                RecipeSpec recipe=FactoryCatalog.GetRecipe(pair.Key);
                bool unlocked=compatible&&(recipe.RequiredTech==TechId.None||TechCatalog.Has(controller.State,recipe.RequiredTech));
                pair.Value.interactable=unlocked;
                string lockText=unlocked?"":"\n잠김 · "+TechCatalog.Get(recipe.RequiredTech).Name+" 연구 필요";
                pair.Value.GetComponentInChildren<Text>().text=recipe.Name+lockText+"\n"+FactoryCatalog.InputText(recipe.Id)+" → "+FactoryCatalog.OutputText(recipe.Id)+" · "+recipe.Duration.ToString("0.#")+"초";
                SetButtonColor(pair.Value,machine.Recipe==pair.Key?Coral:Navy2);
            }

            bool solidFilter=machine.Kind==FactoryKind.Inserter||machine.Kind==FactoryKind.ItemLift;
            bool fluidFilter=machine.Kind==FactoryKind.Pipe||machine.Kind==FactoryKind.PipeJunction||machine.Kind==FactoryKind.FluidTank;
            bool filters=solidFilter||fluidFilter;
            factoryFilterSection.SetActive(filters);
            foreach(var pair in factoryFilterButtons)
            {
                bool visible=filters&&(pair.Key==Resource.Coins||(solidFilter&&ResourceCatalog.IsSolid(pair.Key))||(fluidFilter&&ResourceCatalog.IsFluid(pair.Key)));
                pair.Value.gameObject.SetActive(visible);
                SetButtonColor(pair.Value,machine.Filter==pair.Key?Coral:Navy2);
            }

            bool solidFeed=machine.Kind==FactoryKind.Storage || machine.Kind==FactoryKind.ImportDock;
            bool fluidFeed=machine.Kind==FactoryKind.FluidTank;
            bool feed=solidFeed||fluidFeed;
            factoryFeedSection.SetActive(feed);
            foreach(var pair in factoryFeedButtons)
            {
                bool visible=feed&&((solidFeed&&ResourceCatalog.IsSolid(pair.Key))||(fluidFeed&&ResourceCatalog.IsFluid(pair.Key)));
                pair.Value.gameObject.SetActive(visible);
                int available=Mathf.FloorToInt(controller.Sim.Get(pair.Key));
                ResourceSpec resource=ResourceCatalog.Get(pair.Key);
                pair.Value.interactable=visible && available>=10;
                factoryFeedLabels[pair.Key].text=resource.Name+" +10"+resource.Unit+"  ("+available+resource.Unit+")";
                SetButtonColor(pair.Value,HudStyle.SurfaceRaised);
            }
            AdaptFactoryOptionGrids();
            LayoutFactoryOptionSections(recipes,filters,feed);
            RefreshModalBodyLayouts();
            LayoutRebuilder.ForceRebuildLayoutImmediate(factoryConfigureWindow);
        }

        static bool HasFactoryInventory(FactoryEntity machine)
        {
            if(machine==null)return false;
            if(ResourceCatalog.IsTransportable(machine.CargoResource))return true;
            for(int i=1;i<ResourceCatalog.Count;i++)if((i<machine.Input.Count&&machine.Input[i]>0)||(i<machine.Output.Count&&machine.Output[i]>0))return true;
            return false;
        }

        static string RecipeRateText(RecipeSpec recipe)
        {
            if(recipe==null||recipe.Duration<=0)return "-";
            return string.Join(" + ",recipe.Outputs.Select(amount=>
            {
                ResourceSpec resource=ResourceCatalog.Get(amount.Resource);
                return resource.Name+" "+(amount.Amount*60f/recipe.Duration).ToString("0.#")+resource.Unit;
            }));
        }

        void AdaptFactoryOptionGrids()
        {
            if(factoryConfigureWindow==null)return;
            float logicalWidth=900f;
            if(modalFactoryBodyContent!=null)
            {
                float measured=modalFactoryBodyContent.rect.width;
                if(measured>1)logicalWidth=measured;
                else if(modalFactoryBodyContent.sizeDelta.x>1)logicalWidth=modalFactoryBodyContent.sizeDelta.x;
            }
            float available=Mathf.Max(232,logicalWidth-48);
            int columns=available>=820?4:available>=610?3:available>=400?2:1;
            float cell=Mathf.Floor((available-24-(columns-1)*8)/columns);
            GameObject[] sections={factoryRecipeSection,factoryFilterSection,factoryFeedSection};
            foreach(GameObject section in sections)
            {
                if(section==null)continue;
                RectTransform rect=section.transform as RectTransform;rect.sizeDelta=new Vector2(available,rect.sizeDelta.y);
                GridLayoutGroup grid=section.GetComponent<GridLayoutGroup>();
                if(grid==null)continue;
                grid.constraintCount=columns;grid.cellSize=new Vector2(cell,grid.cellSize.y);
            }
        }

        void LayoutFactoryOptionSections(bool recipes,bool filters,bool feed)
        {
            // Summary and controls occupy the first 132 logical pixels of the existing scroll body.
            float cursor=194f;
            PlaceFactoryOptionSection(factoryRecipeSection,recipes,ref cursor);
            PlaceFactoryOptionSection(factoryFilterSection,filters,ref cursor);
            PlaceFactoryOptionSection(factoryFeedSection,feed,ref cursor);
            float contentHeight=Mathf.Max(220f,cursor+16f);
            if(modalFactoryBodyContent!=null)
            {
                float logicalWidth=Mathf.Max(900f,modalFactoryBodyContent.rect.width);
                modalScrollableBodies[modalFactoryBodyContent]=new Vector2(logicalWidth,contentHeight);
            }
        }

        static void PlaceFactoryOptionSection(GameObject section,bool visible,ref float cursor)
        {
            if(section==null||!visible)return;
            GridLayoutGroup grid=section.GetComponent<GridLayoutGroup>();
            RectTransform rect=section.transform as RectTransform;
            if(grid==null||rect==null)return;
            int active=0;
            foreach(Transform child in section.transform)
            {
                if(!child.gameObject.activeSelf)continue;
                LayoutElement layout=child.GetComponent<LayoutElement>();
                if(layout!=null&&layout.ignoreLayout)continue;
                active++;
            }
            int columns=Mathf.Max(1,grid.constraintCount);
            int rows=Mathf.CeilToInt(active/(float)columns);
            float height=grid.padding.top+grid.padding.bottom;
            if(rows>0)height+=rows*grid.cellSize.y+(rows-1)*grid.spacing.y;
            rect.anchoredPosition=new Vector2(24,-cursor);
            rect.sizeDelta=new Vector2(rect.sizeDelta.x,Mathf.Max(44,height));
            cursor+=rect.sizeDelta.y+12f;
        }

        static void IgnoreLayout(GameObject target)
        {
            LayoutElement element=target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.ignoreLayout=true;
        }

        GameObject ModalCard(string name,string title,Vector2 size,string closeName,UnityEngine.Events.UnityAction onClose)
        {
            Transform parent=name=="HelpCard"?helpOverlay.transform:
                name=="ResearchCard"?researchOverlay.transform:
                name=="FactoryConfigureCard"?factoryConfigureOverlay.transform:
                name=="MenuCard"?menuOverlay.transform:
                name=="OverviewCard"?overviewOverlay.transform:
                name=="TerritoryCard"?territoryOverlay.transform:
                name=="TradeCard"?mobileTradeOverlay.transform:confirmOverlay.transform;
            GameObject card=Box(name,parent,Vector2.zero,size,HudStyle.SurfaceRaised,new Vector2(.5f,.5f));
            Text heading=LabelAt(title,card.transform,HudStyle.TitleSize,HudStyle.Text,FontStyle.Normal,new Vector2(24,-16),new Vector2(size.x-92,44));
            Rect(heading.gameObject,new Vector2(0,1),new Vector2(1,1),new Vector2(24,-60),new Vector2(-84,-16));
            heading.alignment=TextAnchor.MiddleLeft;
            Button close=MakeButton("",card.transform,new Vector2(-16,-16),new Vector2(HudStyle.TouchSize,HudStyle.TouchSize),HudStyle.Surface,onClose,new Vector2(1,1),HudStyle.BodySize);
            close.name=closeName;
            AddCenteredModalIcon(close,HudAssets.CloseIcon);
            return card;
        }

        static void AddCenteredModalIcon(Button button,string semanticName)
        {
            Sprite sprite=HudAssets.Icon(semanticName);
            if(button==null||sprite==null)return;
            GameObject icon=new GameObject("Icon_"+semanticName,typeof(RectTransform),typeof(Image));
            icon.transform.SetParent(button.transform,false);
            RectTransform iconRect=icon.GetComponent<RectTransform>();iconRect.anchorMin=iconRect.anchorMax=iconRect.pivot=new Vector2(.5f,.5f);iconRect.anchoredPosition=Vector2.zero;iconRect.sizeDelta=new Vector2(22,22);
            Image image=icon.GetComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.color=HudStyle.Foreground(HudStyle.Surface);image.raycastTarget=false;
        }

        RectTransform MakeModalBodyScrollable(GameObject card,float contentHeight,params Transform[] fixedHeaderChildren)
        {
            RectTransform cardRect=card.GetComponent<RectTransform>();
            List<Transform> bodyChildren=new List<Transform>();
            for(int i=2;i<card.transform.childCount;i++)
            {
                Transform child=card.transform.GetChild(i);
                if(fixedHeaderChildren==null||!fixedHeaderChildren.Contains(child))bodyChildren.Add(child);
            }
            GameObject viewport=new GameObject("ModalBodyViewport",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));
            viewport.transform.SetParent(card.transform,false);
            Rect(viewport,new Vector2(0,0),new Vector2(1,1),new Vector2(0,12),new Vector2(0,-64));
            Image viewportImage=viewport.GetComponent<Image>();viewportImage.color=new Color(0,0,0,0);viewportImage.raycastTarget=true;
            GameObject content=new GameObject("ModalBodyContent",typeof(RectTransform));content.transform.SetParent(viewport.transform,false);
            RectTransform contentRect=content.GetComponent<RectTransform>();contentRect.anchorMin=contentRect.anchorMax=new Vector2(0,1);contentRect.pivot=new Vector2(0,1);contentRect.anchoredPosition=Vector2.zero;contentRect.sizeDelta=new Vector2(cardRect.sizeDelta.x,contentHeight);
            foreach(Transform child in bodyChildren)
            {
                RectTransform childRect=child as RectTransform;
                child.SetParent(content.transform,false);
                if(childRect!=null)childRect.anchoredPosition+=new Vector2(0,64);
            }
            ScrollRect scroll=viewport.GetComponent<ScrollRect>();scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=contentRect;scroll.horizontal=true;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;
            modalScrollableBodies[contentRect]=new Vector2(cardRect.sizeDelta.x,contentHeight);
            return contentRect;
        }

        void RefreshModalBodyLayouts()
        {
            foreach(var pair in modalScrollableBodies)
            {
                RectTransform content=pair.Key;
                RectTransform viewport=content.parent as RectTransform;
                if(viewport==null)continue;
                content.sizeDelta=new Vector2(Mathf.Max(pair.Value.x,viewport.rect.width),Mathf.Max(pair.Value.y,viewport.rect.height));
            }
        }

        void RefreshModalLayouts()
        {
            Vector2 pixelDimensions=HudStyle.PixelDimensions(hudCanvas);
            int width=Mathf.RoundToInt(pixelDimensions.x),height=Mathf.RoundToInt(pixelDimensions.y);
            if(width<=0||height<=0)return;
            modalLayoutWidth=width;modalLayoutHeight=height;
            ClampModalWindow(modalHelpWindow,new Vector2(680,540),24);
            ClampModalWindow(modalMenuWindow,new Vector2(430,670),24);
            ClampModalWindow(modalOverviewWindow,new Vector2(540,350),24);
            ClampModalWindow(modalTerritoryWindow,new Vector2(520,440),24);
            ClampModalWindow(modalTradeWindow,new Vector2(760,620),24);
            ClampModalWindow(modalConfirmWindow,new Vector2(460,235),24);
            ClampModalWindow(factoryConfigureWindow,new Vector2(900,factoryConfigureWindow==null?700:factoryConfigureWindow.sizeDelta.y),24);
            if (researchWindow != null)
            {
                RectTransform overlay = researchOverlay.transform as RectTransform;
                float scale = Mathf.Max(1, canvasScaler == null ? 1 : canvasScaler.scaleFactor);
                float availableWidth = overlay != null && overlay.rect.width > 0 ? overlay.rect.width : width / scale;
                float availableHeight = overlay != null && overlay.rect.height > 0 ? overlay.rect.height : height / scale;
                researchWindow.anchorMin = researchWindow.anchorMax = new Vector2(.5f, .5f);
                bool desktopMargins = availableHeight >= 480;
                researchWindow.anchoredPosition = new Vector2(0, desktopMargins ? 4 : 0);
                researchWindow.sizeDelta = new Vector2(Mathf.Min(1800, Mathf.Max(280, availableWidth - 32)),
                    Mathf.Max(220, availableHeight - (desktopMargins ? 128 : 32)));
            }
            RefreshModalBodyLayouts();
            Canvas.ForceUpdateCanvases();
        }

        void ClampModalWindow(RectTransform window,Vector2 preferred,float margin)
        {
            if(window==null)return;
            RectTransform parent=window.parent as RectTransform;
            Vector2 pixels=HudStyle.PixelDimensions(hudCanvas);float scale=Mathf.Max(1,canvasScaler==null?1:canvasScaler.scaleFactor);
            float width=parent!=null&&parent.rect.width>0?parent.rect.width:pixels.x/scale;
            float height=parent!=null&&parent.rect.height>0?parent.rect.height:pixels.y/scale;
            window.anchorMin=window.anchorMax=new Vector2(.5f,.5f);window.anchoredPosition=Vector2.zero;
            window.sizeDelta=new Vector2(Mathf.Min(preferred.x,Mathf.Max(180,width-margin*2)),Mathf.Min(preferred.y,Mathf.Max(120,height-margin*2)));
        }

        GameObject Overlay(string name)
        {
            GameObject o=Panel(name,transform,HudStyle.Dim,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            Rect(o,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); return o;
        }
    }
}

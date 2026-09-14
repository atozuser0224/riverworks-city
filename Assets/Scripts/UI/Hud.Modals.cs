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
        readonly Dictionary<Era, RectTransform> modalResearchContents = new Dictionary<Era, RectTransform>();
        readonly Dictionary<Era, List<TechId>> modalResearchCardsByEra = new Dictionary<Era, List<TechId>>();
        readonly Dictionary<TechId, RectTransform> modalResearchTitles = new Dictionary<TechId, RectTransform>();
        readonly Dictionary<TechId, RectTransform> modalResearchBadges = new Dictionary<TechId, RectTransform>();
        readonly Dictionary<RectTransform, Vector2> modalScrollableBodies = new Dictionary<RectTransform, Vector2>();
        RectTransform modalHelpWindow, modalMenuWindow, modalOverviewWindow, modalTerritoryWindow, modalTradeWindow, modalConfirmWindow;
        RectTransform modalResearchEraViewport, modalResearchEraStrip;
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
            GameObject card=ModalCard("FactoryConfigureCard","설비 구성",new Vector2(690,330),"Button_FactoryConfigureClose",()=>SetFactoryModal(false));
            factoryConfigureWindow=card.GetComponent<RectTransform>();
            factoryConfigureSummary=LabelAt("",card.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(24,-61),new Vector2(642,54));
            factoryConfigureSummary.alignment=TextAnchor.UpperLeft;

            factoryRecipeSection=Box("FactoryRecipeSection",card.transform,new Vector2(24,-124),new Vector2(642,82),HudStyle.SurfaceRaised,new Vector2(0,1));
            Text recipeHeading=LabelAt("제조법",factoryRecipeSection.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(12,-8),new Vector2(80,22));
            IgnoreLayout(recipeHeading.gameObject);
            HorizontalLayoutGroup recipeLayout=factoryRecipeSection.AddComponent<HorizontalLayoutGroup>();
            recipeLayout.padding=new RectOffset(94,8,8,8); recipeLayout.spacing=8; recipeLayout.childAlignment=TextAnchor.MiddleLeft;
            recipeLayout.childControlWidth=true; recipeLayout.childForceExpandWidth=true; recipeLayout.childControlHeight=false; recipeLayout.childForceExpandHeight=false;
            FactoryRecipe[] recipes={FactoryRecipe.IronPlate,FactoryRecipe.Tools,FactoryRecipe.Flour,FactoryRecipe.Bread};
            for(int i=0;i<recipes.Length;i++)
            {
                FactoryRecipe recipe=recipes[i];
                Button button=MakeButton(FactoryCatalog.RecipeName(recipe),factoryRecipeSection.transform,Vector2.zero,new Vector2(126,54),HudStyle.SurfaceRaised,()=>ChooseFactoryRecipe(recipe),new Vector2(0,1),HudStyle.BodySize);
                button.name="Button_FactoryRecipe_"+recipe;factoryRecipeButtons[recipe]=button;
            }

            factoryFilterSection=Box("FactoryFilterSection",card.transform,new Vector2(24,-124),new Vector2(642,174),HudStyle.SurfaceRaised,new Vector2(0,1));
            Text filterHeading=LabelAt("투입기 필터",factoryFilterSection.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(12,-8),new Vector2(110,22));
            IgnoreLayout(filterHeading.gameObject);
            GridLayoutGroup filterGrid=factoryFilterSection.AddComponent<GridLayoutGroup>();
            filterGrid.padding=new RectOffset(126,8,34,8); filterGrid.cellSize=new Vector2(160,44); filterGrid.spacing=new Vector2(8,4); filterGrid.constraint=GridLayoutGroup.Constraint.FixedColumnCount; filterGrid.constraintCount=3;
            Resource[] filters={Resource.Coins,Resource.Timber,Resource.Stone,Resource.Grain,Resource.Flour,Resource.Bread,Resource.Ore,Resource.Steel,Resource.Tools};
            for(int i=0;i<filters.Length;i++)
            {
                Resource resource=filters[i];
                string label=resource==Resource.Coins?"모든 물자":Catalog.ResourceName(resource);
                Button button=MakeButton(label,factoryFilterSection.transform,Vector2.zero,new Vector2(160,44),HudStyle.SurfaceRaised,()=>ChooseFactoryFilter(resource),new Vector2(0,1),HudStyle.BodySize);
                button.name="Button_FactoryFilter_"+resource;factoryFilterButtons[resource]=button;
            }

            factoryFeedSection=Box("FactoryFeedSection",card.transform,new Vector2(24,-124),new Vector2(642,134),HudStyle.SurfaceRaised,new Vector2(0,1));
            Text feedHeading=LabelAt("도시 재고 투입 · 10개",factoryFeedSection.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(12,-8),new Vector2(170,22));
            IgnoreLayout(feedHeading.gameObject);
            GridLayoutGroup feedGrid=factoryFeedSection.AddComponent<GridLayoutGroup>();
            feedGrid.padding=new RectOffset(12,8,34,8); feedGrid.cellSize=new Vector2(149,44); feedGrid.spacing=new Vector2(8,4); feedGrid.constraint=GridLayoutGroup.Constraint.FixedColumnCount; feedGrid.constraintCount=4;
            Resource[] feed={Resource.Timber,Resource.Stone,Resource.Grain,Resource.Flour,Resource.Bread,Resource.Ore,Resource.Steel,Resource.Tools};
            for(int i=0;i<feed.Length;i++)
            {
                Resource resource=feed[i];
                Button button=MakeButton("",factoryFeedSection.transform,Vector2.zero,new Vector2(149,44),HudStyle.SurfaceRaised,()=>FeedFactory(resource),new Vector2(0,1),HudStyle.BodySize);
                button.name="Button_FactoryFeed_"+resource;factoryFeedButtons[resource]=button;factoryFeedLabels[resource]=button.GetComponentInChildren<Text>();
            }
            FeelUiFeedback.AttachPanel(card);
            modalFactoryBodyContent=MakeModalBodyScrollable(card,298);
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
            Button effects=MakeButton("",menu.transform,new Vector2(24,-400),new Vector2(382,44),HudStyle.SurfaceRaised,ToggleFeelEffects,new Vector2(0,1),HudStyle.BodySize);
            effects.name="Button_ReducedMotion";
            feelModeLabel=effects.GetComponentInChildren<Text>();
            RefreshFeelModeLabel();
            Button guide=MakeButton("단우에게 조언 듣기",menu.transform,new Vector2(24,-456),new Vector2(185,44),HudStyle.SurfaceRaised,()=>{CloseTransientPanels();controller.Tutorial?.OpenGuide();},new Vector2(0,1),HudStyle.BodySize);
            guide.name="Button_Tutorial";
            Button residentAi=MakeButton("주민 AI",menu.transform,new Vector2(221,-456),new Vector2(185,44),HudStyle.SurfaceRaised,()=>{CloseTransientPanels();controller.ResidentAi?.OpenSettings();},new Vector2(0,1),HudStyle.BodySize);
            residentAi.name="Button_ResidentAi";
            LabelAt("도시는 자동 저장됩니다.",menu.transform,HudStyle.BodySize,HudStyle.Positive,FontStyle.Normal,new Vector2(24,-512),new Vector2(382,22)).alignment=TextAnchor.MiddleCenter;
            MakeModalBodyScrollable(menu,540);
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
            GameObject tradeCard=ModalCard("TradeCard","긴급 교역 · 10개 단위",new Vector2(760,470),"Button_TradeClose",CloseTransientPanels);
            modalTradeWindow=tradeCard.GetComponent<RectTransform>();
            modalTradeCoins=LabelAt("",tradeCard.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(470,-20),new Vector2(210,30));
            modalTradeCoins.gameObject.name="TradeHoldingsHeader";
            modalTradeCoins.alignment=TextAnchor.MiddleRight;
            Resource[] tradeResources={Resource.Timber,Resource.Stone,Resource.Grain,Resource.Flour,Resource.Bread,Resource.Ore,Resource.Steel,Resource.Tools};
            for(int i=0;i<tradeResources.Length;i++)
            {
                Resource resource=tradeResources[i];int col=i%2,row=i/2;float x=24+col*368,y=-82-row*86;
                Text holding=LabelAt("",tradeCard.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(x,y),new Vector2(94,56));
                holding.alignment=TextAnchor.MiddleLeft;modalTradeHoldingLabels[resource]=holding;
                Button buy=MakeButton("구매  -"+(GameController.TradePrice(resource)*10)+"G",tradeCard.transform,new Vector2(x+96,y),new Vector2(122,56),HudStyle.Positive,()=>controller.Trade(resource,true),new Vector2(0,1),HudStyle.BodySize);
                buy.name="Button_MobileBuy_"+resource;buyButtons[resource]=buy;
                Button sell=MakeButton("판매  +"+(Mathf.Max(1,GameController.TradePrice(resource)/2)*10)+"G",tradeCard.transform,new Vector2(x+224,y),new Vector2(122,56),HudStyle.Positive,()=>controller.Trade(resource,false),new Vector2(0,1),HudStyle.BodySize);
                sell.name="Button_MobileSell_"+resource;
                modalSellButtons[resource]=sell;
            }
            MakeModalBodyScrollable(tradeCard,438,modalTradeCoins.transform);
            FeelUiFeedback.AttachPanel(tradeCard);
            mobileTradeOverlay.SetActive(false);
        }

        void BuildResearchOverlay()
        {
            researchButtons.Clear();
            researchCardTexts.Clear();
            researchStatusTexts.Clear();
            researchCardImages.Clear();
            researchCardRects.Clear();
            researchViewports.Clear();
            modalResearchContents.Clear();
            modalResearchCardsByEra.Clear();
            modalResearchTitles.Clear();modalResearchBadges.Clear();

            researchOverlay=Overlay("ResearchOverlay");
            Vector2 windowSize=new Vector2(1180,640);
            GameObject card=ModalCard("ResearchCard","도시 기술 연구",windowSize,"Button_ResearchClose",()=>controller.ToggleResearch());
            researchWindow=card.GetComponent<RectTransform>();
            Text intro=LabelAt("시대별 열을 위아래로 스크롤하세요. 선행 기술에서 다음 기술로 이어지는 경로를 카드에서 확인할 수 있습니다.",card.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(28,-54),new Vector2(windowSize.x-210,24));
            intro.alignment=TextAnchor.MiddleLeft;

            List<TechSpec> specs=TechCatalog.All.Where(spec=>spec!=null).ToList();
            List<Era> eras=specs.Select(spec=>spec.Era).Distinct().OrderBy(era=>(int)era).ToList();
            float side=24f;
            float gap=14f;
            float columnHeight=windowSize.y-102f;
            GameObject eraViewport=new GameObject("ResearchEraViewport",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));
            eraViewport.transform.SetParent(card.transform,false);
            Rect(eraViewport,new Vector2(0,0),new Vector2(1,1),new Vector2(side,68),new Vector2(-side,-88));
            Image eraViewportImage=eraViewport.GetComponent<Image>();eraViewportImage.color=new Color(0,0,0,0);eraViewportImage.raycastTarget=true;
            modalResearchEraViewport=eraViewport.GetComponent<RectTransform>();
            GameObject eraStrip=new GameObject("ResearchEraStrip",typeof(RectTransform));eraStrip.transform.SetParent(eraViewport.transform,false);
            modalResearchEraStrip=eraStrip.GetComponent<RectTransform>();modalResearchEraStrip.anchorMin=new Vector2(0,0);modalResearchEraStrip.anchorMax=new Vector2(1,1);modalResearchEraStrip.offsetMin=modalResearchEraStrip.offsetMax=Vector2.zero;
            ScrollRect eraScroll=eraViewport.GetComponent<ScrollRect>();eraScroll.viewport=modalResearchEraViewport;eraScroll.content=modalResearchEraStrip;eraScroll.horizontal=true;eraScroll.vertical=false;eraScroll.movementType=ScrollRect.MovementType.Clamped;eraScroll.scrollSensitivity=42;
            for(int col=0;col<eras.Count;col++)
            {
                Era era=eras[col];
                List<TechSpec> eraSpecs=specs.Where(spec=>spec.Era==era).OrderBy(spec=>InitialResearchOrder(spec.Id)).ThenBy(spec=>specs.IndexOf(spec)).ToList();
                float min=(float)col/Mathf.Max(1,eras.Count),max=(float)(col+1)/Mathf.Max(1,eras.Count);
                GameObject column=Box("ResearchEra_"+era,modalResearchEraStrip,Vector2.zero,Vector2.zero,HudStyle.SurfaceRaised,new Vector2(0,1));
                RectTransform columnRect=column.GetComponent<RectTransform>();
                Rect(column,new Vector2(min,0),new Vector2(max,1),new Vector2(col==0?0:gap*.5f,0),new Vector2(-(col==eras.Count-1?0:gap*.5f),0));
                float columnWidth=(windowSize.x-side*2-gap*Mathf.Max(0,eras.Count-1))/Mathf.Max(1,eras.Count);
                Text eraHeading=LabelAt(TechCatalog.EraName(era),column.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(12,-10),new Vector2(columnWidth-24,24));
                Rect(eraHeading.gameObject,new Vector2(0,1),new Vector2(1,1),new Vector2(12,-34),new Vector2(-156,-10));
                Text countLabel=LabelAt(eraSpecs.Count+"개 · 세로 스크롤",column.transform,HudStyle.BodySize,HudStyle.TextMuted,FontStyle.Normal,new Vector2(columnWidth-152,-12),new Vector2(140,20));
                countLabel.alignment=TextAnchor.UpperRight;
                Rect(countLabel.gameObject,new Vector2(1,1),new Vector2(1,1),new Vector2(-152,-12),new Vector2(140,20));

                GameObject viewport=new GameObject("ResearchScroll_"+era,typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));
                viewport.transform.SetParent(column.transform,false);
                Rect(viewport,new Vector2(0,1),new Vector2(0,1),new Vector2(8,-42),new Vector2(columnWidth-16,columnHeight-50));
                Image viewportImage=viewport.GetComponent<Image>();
                viewportImage.color=new Color(0,0,0,0);
                viewportImage.raycastTarget=true;
                RectTransform viewportRect=viewport.GetComponent<RectTransform>();
                viewportRect.pivot=new Vector2(0,1);
                researchViewports[era]=viewportRect;
                modalResearchCardsByEra[era]=eraSpecs.Select(spec=>spec.Id).ToList();

                const float cardHeight=202f;
                const float cardGap=10f;
                float contentHeight=Mathf.Max(viewportRect.sizeDelta.y,eraSpecs.Count*(cardHeight+cardGap)-cardGap);
                GameObject content=new GameObject("ResearchContent_"+era,typeof(RectTransform));
                content.transform.SetParent(viewport.transform,false);
                RectTransform contentRect=content.GetComponent<RectTransform>();
                contentRect.anchorMin=new Vector2(0,1);
                contentRect.anchorMax=new Vector2(1,1);
                contentRect.pivot=new Vector2(.5f,1);
                contentRect.anchoredPosition=Vector2.zero;
                contentRect.sizeDelta=new Vector2(0,contentHeight);
                modalResearchContents[era]=contentRect;

                ScrollRect scroll=viewport.GetComponent<ScrollRect>();
                scroll.viewport=viewportRect;
                scroll.content=contentRect;
                scroll.horizontal=false;
                scroll.vertical=true;
                scroll.movementType=ScrollRect.MovementType.Clamped;
                scroll.inertia=true;
                scroll.decelerationRate=.12f;
                scroll.scrollSensitivity=34f;

                for(int row=0;row<eraSpecs.Count;row++)
                    BuildResearchCard(content.transform,eraSpecs[row],new Vector2(0,-row*(cardHeight+cardGap)),new Vector2(columnWidth-16,cardHeight));
            }
            FeelUiFeedback.AttachPanel(card);
            researchOverlay.SetActive(false);
            RefreshModalLayouts();
        }

        void BuildResearchCard(Transform parent, TechSpec spec, Vector2 pos, Vector2 size)
        {
            TechId id=spec.Id;
            GameObject card=Box("TechCard_"+id,parent,pos,size,new Color(HudStyle.Surface.r,HudStyle.Surface.g,HudStyle.Surface.b,.98f),new Vector2(0,1));
            researchCardImages[id]=card.GetComponent<Image>();
            researchCardRects[id]=card.GetComponent<RectTransform>();
            Text title=LabelAt(spec.Name,card.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(12,-10),new Vector2(size.x-132,26));
            modalResearchTitles[id]=title.rectTransform;
            title.verticalOverflow=VerticalWrapMode.Overflow;
            GameObject badge=Box("TechStatus_"+id,card.transform,new Vector2(size.x-112,-10),new Vector2(100,24),HudStyle.SurfaceRaised,new Vector2(0,1));
            modalResearchBadges[id]=badge.GetComponent<RectTransform>();
            Text status=Label("",badge.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,TextAnchor.MiddleCenter);
            Rect(status.gameObject,Vector2.zero,Vector2.one,new Vector2(4,1),new Vector2(-4,-1));
            status.alignment=TextAnchor.MiddleCenter;
            researchStatusTexts[id]=status;
            Text details=LabelAt("",card.transform,HudStyle.BodySize,HudStyle.Text,FontStyle.Normal,new Vector2(12,-40),new Vector2(size.x-24,116));
            details.gameObject.name="TechEffect_"+id;
            details.alignment=TextAnchor.UpperLeft;
            Button button=MakeButton("연구 시작_"+id,card.transform,new Vector2(12,-154),new Vector2(size.x-24,44),HudStyle.SurfaceRaised,()=>controller.Research(id),new Vector2(0,1),HudStyle.BodySize);
            button.name="Button_연구 시작_"+id;
            button.GetComponentInChildren<Text>().text="연구 시작";
            researchCardTexts[id]=details;
            researchButtons[id]=button;
        }

        static int InitialResearchOrder(TechId id)
        {
            if(id==TechId.Stonecraft)return -2;
            if(id==TechId.CropRotation)return -1;
            return 0;
        }

        void RefreshResearch()
        {
            GameState s=controller.State;
            eraText.text=TechCatalog.EraName(s.Era)+" · 지식 "+Mathf.FloorToInt(s.ResearchPoints)+"점";
            if(s.ActiveResearch==TechId.None)
            {
                int ready=TechCatalog.All.Count(t=>{string reason;return controller.Sim.CanResearch(t.Id,out reason);});
                researchSummaryText.text="가능 "+ready+" · +"+controller.Sim.ResearchPerDay.ToString("0.#")+"/일";
            }
            else
            {
                TechSpec active=TechCatalog.Get(s.ActiveResearch);
                researchSummaryText.text=active.Name+" "+Mathf.RoundToInt(controller.Sim.ResearchProgress*100f)+"%";
            }
            foreach(var pair in researchCardTexts)
            {
                TechSpec spec=TechCatalog.Get(pair.Key);
                if(spec==null)continue;
                bool completed=TechCatalog.Has(s,pair.Key);
                bool active=s.ActiveResearch==pair.Key;
                string prerequisites=spec.Prerequisites==null||spec.Prerequisites.Length==0?"없음":string.Join(" + ",spec.Prerequisites.Select(p=>{TechSpec prerequisite=TechCatalog.Get(p);return prerequisite==null?p.ToString():prerequisite.Name;}));
                string unlocks=spec.UnlockBuildings==null||spec.UnlockBuildings.Length==0?"":string.Join(", ",spec.UnlockBuildings.Select(b=>{BuildingSpec building=Catalog.Get(b);return building==null?b.ToString():building.Name;}));
                string benefits=string.IsNullOrEmpty(spec.Benefit)?"없음":spec.Benefit;
                if(!string.IsNullOrEmpty(unlocks))benefits+=" · 시설: "+unlocks;
                pair.Value.text=completed?"효과 · "+CompletedBenefitSummary(spec):spec.Description+"\n선행 → "+prerequisites+"\n지식 "+spec.ResearchCost+" · "+spec.CoinCost+"G · "+spec.DurationDays+"일\n효과: "+benefits;
                Button button=researchButtons[pair.Key];
                string reason;
                bool can=controller.Sim.CanResearch(pair.Key,out reason);
                button.interactable=can;
                button.gameObject.SetActive(!completed);
                if(!completed)button.GetComponentInChildren<Text>().text=active?"연구 중 · "+Mathf.RoundToInt(controller.Sim.ResearchProgress*100f)+"%":can?"연구 시작":reason;
                SetButtonColor(button,active?HudStyle.Accent:can?HudStyle.SurfaceRaised:new Color(.38f,.4f,.42f,1));
                Text status=researchStatusTexts[pair.Key];
                status.text=completed?"완료":active?"진행 "+Mathf.RoundToInt(controller.Sim.ResearchProgress*100f)+"%":can?"연구 가능":"잠김";
                Image statusBackground=status.transform.parent.GetComponent<Image>();
                if(statusBackground!=null)
                {
                    statusBackground.color=completed?HudStyle.Positive:active?HudStyle.Accent:HudStyle.SurfaceRaised;
                    status.color=can||active||completed?HudStyle.Foreground(statusBackground.color):HudStyle.TextMuted;
                }
                Image cardImage=researchCardImages[pair.Key];
                if(cardImage!=null)cardImage.color=completed?Color.Lerp(HudStyle.Surface,HudStyle.Positive,.2f):active?Color.Lerp(HudStyle.Surface,HudStyle.Accent,.2f):HudStyle.Surface;
            }
            RefreshResearchCardLayout();
            RefreshTradeState();
        }

        void RefreshResearchCardLayout()
        {
            const float minimumFullHeight=184f, completedHeight=82f, gap=10f;
            foreach(var eraPair in modalResearchCardsByEra)
            {
                RectTransform content;
                RectTransform viewport;
                if(!modalResearchContents.TryGetValue(eraPair.Key,out content) || !researchViewports.TryGetValue(eraPair.Key,out viewport))continue;
                List<TechId> ordered=eraPair.Value.OrderBy(ResearchCardRank).ThenBy(id=>InitialResearchOrder(id)).ThenBy(id=>eraPair.Value.IndexOf(id)).ToList();
                float y=0;
                foreach(TechId id in ordered)
                {
                    RectTransform rect=researchCardRects[id];
                    bool completed=TechCatalog.Has(controller.State,id);
                    Text details=researchCardTexts[id];
                    details.gameObject.SetActive(true);
                    float measuredDetailsHeight=Mathf.Ceil(Mathf.Max(0,details.preferredHeight));
                    float height=completed?completedHeight:Mathf.Max(minimumFullHeight,measuredDetailsHeight+84);
                    rect.anchoredPosition=new Vector2(0,-y);
                    rect.sizeDelta=new Vector2(0,height);
                    details.rectTransform.offsetMin=new Vector2(12,completed?-64:-(height-44));
                    details.rectTransform.offsetMax=new Vector2(-12,completed?-42:-40);
                    RectTransform button=researchButtons[id].transform as RectTransform;
                    if(button!=null){button.offsetMin=new Vector2(12,-height);button.offsetMax=new Vector2(-12,-(height-44));}
                    y+=height+gap;
                }
                content.sizeDelta=new Vector2(0,Mathf.Max(viewport.rect.height,Mathf.Max(0,y-gap)));
            }
        }

        int ResearchCardRank(TechId id)
        {
            if(TechCatalog.Has(controller.State,id))return 2;
            string reason;
            return controller.Sim.CanResearch(id,out reason)?0:1;
        }

        static string CompletedBenefitSummary(TechSpec spec)
        {
            switch(spec.Id)
            {
                case TechId.Stonecraft:return "채석장·공원 · 증축 2단계";
                case TechId.MechanicalPower:return "풍차·제분소·제과점";
                case TechId.Guilds:return "르네상스 · 창고·시장";
                case TechId.SteamPower:return "산업 시대 · 증기 동력소";
                default:return string.IsNullOrEmpty(spec.Benefit)?"연구 완료":spec.Benefit;
            }
        }

        void RefreshTradeState()
        {
            if(controller==null || controller.Sim==null)return;
            if(modalTradeCoins!=null)modalTradeCoins.text="보유 코인  "+controller.State.Coins.ToString("N0")+"G";
            foreach(var pair in modalTradeHoldingLabels)
            {
                int holding=Mathf.FloorToInt(controller.Sim.Get(pair.Key));
                pair.Value.text=Catalog.ResourceName(pair.Key)+"\n보유 "+holding.ToString("N0");
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
            menuOpen=true;overviewOpen=territoryOpen=mobileTradeOpen=confirmOpen=factoryModalOpen=false;
            controller.ModalOpen=true;
            RefreshModalOverlays();
        }

        void OpenUtility(string panel)
        {
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
            menuOpen=overviewOpen=territoryOpen=mobileTradeOpen=confirmOpen=factoryModalOpen=false;
            controller.ModalOpen=false;
            RefreshModalOverlays();
        }

        void SetHelp(bool open){if(controller.HelpOpen!=open) controller.ToggleHelp();}

        void SetConfirm(bool open)
        {
            menuOpen=overviewOpen=territoryOpen=mobileTradeOpen=factoryModalOpen=false;
            confirmOpen=open;
            controller.ModalOpen=open;
            RefreshModalOverlays();
        }

        void SetFactoryModal(bool open)
        {
            if(open && controller.SelectedFactory==null)return;
            factoryModalOpen=open;
            if(open)menuOpen=overviewOpen=territoryOpen=mobileTradeOpen=confirmOpen=false;
            controller.ModalOpen=open;
            RefreshModalOverlays();
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
            bool configurable=machine.Kind==FactoryKind.Furnace||machine.Kind==FactoryKind.Assembler||machine.Kind==FactoryKind.Inserter||machine.Kind==FactoryKind.Storage||machine.Kind==FactoryKind.ImportDock;
            factoryConfigureSummary.text=spec.Name+" #"+machine.Id+" · "+DirectionText(machine.Direction)+" · "+(string.IsNullOrEmpty(machine.Status)?"상태 확인 중":machine.Status)+"\n"+(configurable?"투입 "+FactoryInventoryText(machine.Input)+"  /  출력 "+FactoryInventoryText(machine.Output):"이 설비는 별도 설정 항목이 없습니다.");

            bool recipes=machine.Kind==FactoryKind.Furnace || machine.Kind==FactoryKind.Assembler;
            factoryRecipeSection.SetActive(recipes);
            foreach(var pair in factoryRecipeButtons)
            {
                bool compatible=machine.Kind==FactoryKind.Furnace?pair.Key==FactoryRecipe.IronPlate:machine.Kind==FactoryKind.Assembler&&(pair.Key==FactoryRecipe.Tools||pair.Key==FactoryRecipe.Flour||pair.Key==FactoryRecipe.Bread);
                pair.Value.gameObject.SetActive(compatible);
                pair.Value.interactable=compatible;
                SetButtonColor(pair.Value,machine.Recipe==pair.Key?Coral:Navy2);
            }

            bool filters=machine.Kind==FactoryKind.Inserter;
            factoryFilterSection.SetActive(filters);
            foreach(var pair in factoryFilterButtons)SetButtonColor(pair.Value,machine.Filter==pair.Key?Coral:Navy2);

            bool feed=machine.Kind==FactoryKind.Storage || machine.Kind==FactoryKind.ImportDock;
            factoryFeedSection.SetActive(feed);
            foreach(var pair in factoryFeedButtons)
            {
                int available=Mathf.FloorToInt(controller.Sim.Get(pair.Key));
                pair.Value.interactable=feed && available>=10;
                factoryFeedLabels[pair.Key].text=Catalog.ResourceName(pair.Key)+" +10  ("+available+")";
                SetButtonColor(pair.Value,HudStyle.SurfaceRaised);
            }
            float sectionHeight=filters?174f:feed?134f:recipes?82f:0f;
            factoryConfigureWindow.sizeDelta=new Vector2(690,sectionHeight>0?148f+sectionHeight:196f);
            if(modalFactoryBodyContent!=null)modalScrollableBodies[modalFactoryBodyContent]=new Vector2(690,Mathf.Max(124,sectionHeight+72));
            RefreshModalBodyLayouts();
            LayoutRebuilder.ForceRebuildLayoutImmediate(factoryConfigureWindow);
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
            ClampModalWindow(modalMenuWindow,new Vector2(430,556),24);
            ClampModalWindow(modalOverviewWindow,new Vector2(540,350),24);
            ClampModalWindow(modalTerritoryWindow,new Vector2(520,440),24);
            ClampModalWindow(modalTradeWindow,new Vector2(760,470),24);
            ClampModalWindow(modalConfirmWindow,new Vector2(460,235),24);
            ClampModalWindow(factoryConfigureWindow,new Vector2(690,factoryConfigureWindow==null?330:factoryConfigureWindow.sizeDelta.y),24);
            if(researchWindow==null)return;
            RectTransform overlay=researchOverlay.transform as RectTransform;
            float fallbackScale=Mathf.Max(1,canvasScaler==null?1:canvasScaler.scaleFactor);
            float availableWidth=overlay!=null&&overlay.rect.width>0?overlay.rect.width:width/fallbackScale;
            float availableHeight=overlay!=null&&overlay.rect.height>0?overlay.rect.height:height/fallbackScale;
            researchWindow.anchorMin=researchWindow.anchorMax=new Vector2(.5f,.5f);
            bool desktopMargins=availableHeight>=480;
            researchWindow.anchoredPosition=new Vector2(0,desktopMargins?4:0);
            float researchHeight=desktopMargins?availableHeight-128:availableHeight-32;
            researchWindow.sizeDelta=new Vector2(Mathf.Min(1180,Mathf.Max(180,availableWidth-48)),Mathf.Max(180,researchHeight));
            int eraCount=Mathf.Max(1,researchViewports.Count);
            float bodyWidth=Mathf.Max(192,researchWindow.sizeDelta.x-48);
            bool horizontalEras=bodyWidth<eraCount*220;
            modalResearchEraViewport.anchorMin=Vector2.zero;modalResearchEraViewport.anchorMax=Vector2.one;
            modalResearchEraViewport.offsetMin=new Vector2(24,68);modalResearchEraViewport.offsetMax=new Vector2(-24,-88);
            modalResearchEraStrip.anchorMin=new Vector2(0,0);modalResearchEraStrip.anchorMax=new Vector2(horizontalEras?0:1,1);
            modalResearchEraStrip.pivot=new Vector2(0,.5f);modalResearchEraStrip.anchoredPosition=Vector2.zero;
            const float narrowEraWidth=252;
            modalResearchEraStrip.sizeDelta=new Vector2(horizontalEras?eraCount*narrowEraWidth:0,0);
            int index=0;
            foreach(var pair in researchViewports.OrderBy(p=>(int)p.Key))
            {
                RectTransform viewport=pair.Value;
                RectTransform column=viewport.parent as RectTransform;
                float min=(float)index/eraCount,max=(float)(index+1)/eraCount;
                if(horizontalEras)
                {
                    column.anchorMin=new Vector2(0,0);column.anchorMax=new Vector2(0,1);
                    column.offsetMin=new Vector2(index*narrowEraWidth+(index==0?0:7),0);column.offsetMax=new Vector2((index+1)*narrowEraWidth-(index==eraCount-1?0:7),0);
                }
                else
                {
                    column.anchorMin=new Vector2(min,0);column.anchorMax=new Vector2(max,1);
                    column.offsetMin=new Vector2(index==0?0:7,0);column.offsetMax=new Vector2(-(index==eraCount-1?0:7),0);
                }
                viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;
                viewport.offsetMin=new Vector2(8,8);viewport.offsetMax=new Vector2(-8,-42);
                index++;
            }
            foreach(var pair in researchCardRects)
            {
                RectTransform card=pair.Value;
                card.anchorMin=new Vector2(0,1);card.anchorMax=new Vector2(1,1);card.pivot=new Vector2(.5f,1);card.sizeDelta=new Vector2(0,card.sizeDelta.y);
                RectTransform title=modalResearchTitles[pair.Key];
                title.anchorMin=new Vector2(0,1);title.anchorMax=new Vector2(1,1);title.offsetMin=new Vector2(12,-36);title.offsetMax=new Vector2(-112,-10);
                RectTransform badge=modalResearchBadges[pair.Key];
                badge.anchorMin=badge.anchorMax=new Vector2(1,1);badge.pivot=new Vector2(1,1);badge.anchoredPosition=new Vector2(-12,-10);badge.sizeDelta=new Vector2(88,24);
                RectTransform details=researchCardTexts[pair.Key].rectTransform;
                details.anchorMin=new Vector2(0,1);details.anchorMax=new Vector2(1,1);
                RectTransform button=researchButtons[pair.Key].transform as RectTransform;
                button.anchorMin=new Vector2(0,1);button.anchorMax=new Vector2(1,1);
            }
            RefreshResearchCardLayout();
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

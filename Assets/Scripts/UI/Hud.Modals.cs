using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    public sealed partial class Hud
    {
        void BuildOverlays()
        {
            GameObject notice=Box("Notice",transform,new Vector2(0,-60),new Vector2(500,38),new Color(Navy.r,Navy.g,Navy.b,.94f),new Vector2(.5f,1));
            noticeText = Label("", notice.transform, 13, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);
            Rect(noticeText.gameObject,Vector2.zero,Vector2.one,new Vector2(12,2),new Vector2(-12,-2));
            noticeGroup=notice.AddComponent<CanvasGroup>(); noticeGroup.alpha=0; noticeGroup.blocksRaycasts=false; noticeGroup.interactable=false;

            helpOverlay = Overlay("HelpOverlay");
            GameObject help=Box("HelpCard",helpOverlay.transform,Vector2.zero,new Vector2(650,500),Paper,new Vector2(.5f,.5f));
            LabelAt("RIVERWORKS 안내서",help.transform,25,Cream,FontStyle.Bold,new Vector2(30,-28),new Vector2(570,40));
            Text h=LabelAt("중세 도시를 키우는 법\n\n1. 시청에서 흙길을 이어 주택·생산지·서재를 연결하세요.\n2. 곡물과 빵을 공급하고 연구로 새 건물과 설비를 여세요.\n3. 산업 설비도 같은 도시의 보유 영토에 놓습니다. 별도 공장 화면은 없습니다.\n4. 설비의 2×2 미세 칸은 도시 부지 1칸입니다. 방향과 지형을 배치 전에 확인하세요.\n5. 생산지·창고·시장과 설비는 투입기·벨트·도로로 실제 물자를 주고받습니다.\n6. 주민이나 건물, 설비를 누르면 오른쪽에서 상태와 구성을 확인할 수 있습니다.\n\n조작\n좌클릭: 건설/선택   우클릭 드래그: 화면 이동\nWASD: 화면 이동   휠: 확대/축소   Q/E: 도시 건물 회전   F: 시청 보기\nG: 산업 설비 팔레트   R: 선택한 설비 도구 회전\nDelete: 철거   Esc: 닫기/도구 취소   1/2/3: 속도\nF5: 저장   M: 소리   F1: 도움말\n\n교역 버튼은 좌클릭 구매, 우클릭 판매입니다.",help.transform,13,Ink,FontStyle.Normal,new Vector2(34,-78),new Vector2(580,360)); h.alignment=TextAnchor.UpperLeft;
            Button helpClose=MakeButton("도시로 돌아가기",help.transform,new Vector2(175,-438),new Vector2(300,44),Coral,()=>SetHelp(false),new Vector2(0,1));
            helpClose.name="Button_HelpClose";
            FeelUiFeedback.AttachPanel(help);
            helpOverlay.SetActive(false);

            BuildResearchOverlay();
            BuildFactoryConfigureOverlay();
            BuildUtilityOverlays();

            confirmOverlay=Overlay("NewGameConfirm");
            GameObject cc=Box("ConfirmCard",confirmOverlay.transform,Vector2.zero,new Vector2(460,235),Paper,new Vector2(.5f,.5f));
            LabelAt("새 도시를 시작할까요?",cc.transform,24,Cream,FontStyle.Bold,new Vector2(30,-28),new Vector2(400,36));
            Text warn=LabelAt("이전 도시는 별도 백업 파일로 보관됩니다.\n새 도시를 시작합니다.",cc.transform,15,Ink,FontStyle.Normal,new Vector2(30,-82),new Vector2(400,55)); warn.alignment=TextAnchor.MiddleCenter;
            MakeButton("취소",cc.transform,new Vector2(40,-161),new Vector2(175,44),Navy2,()=>SetConfirm(false),new Vector2(0,1));
            MakeButton("새 도시 시작",cc.transform,new Vector2(245,-161),new Vector2(175,44),Coral,()=>{SetConfirm(false);controller.NewGame();},new Vector2(0,1));
            FeelUiFeedback.AttachPanel(cc);
            confirmOverlay.SetActive(false);
        }

        void BuildFactoryConfigureOverlay()
        {
            factoryConfigureOverlay=Overlay("FactoryConfigureOverlay");
            GameObject card=Box("FactoryConfigureCard",factoryConfigureOverlay.transform,Vector2.zero,new Vector2(690,330),Paper,new Vector2(.5f,.5f));
            factoryConfigureWindow=card.GetComponent<RectTransform>();
            LabelAt("설비 구성",card.transform,25,Cream,FontStyle.Bold,new Vector2(24,-18),new Vector2(420,36));
            Button close=MakeButton("닫기",card.transform,new Vector2(566,-14),new Vector2(100,44),Coral,()=>SetFactoryModal(false),new Vector2(0,1),12);
            close.name="Button_FactoryConfigureClose";
            factoryConfigureSummary=LabelAt("",card.transform,13,Ink,FontStyle.Normal,new Vector2(24,-61),new Vector2(642,54));
            factoryConfigureSummary.alignment=TextAnchor.UpperLeft;

            factoryRecipeSection=Box("FactoryRecipeSection",card.transform,new Vector2(24,-124),new Vector2(642,82),Navy2,new Vector2(0,1));
            LabelAt("제조법",factoryRecipeSection.transform,13,Coral,FontStyle.Bold,new Vector2(12,-8),new Vector2(80,22));
            FactoryRecipe[] recipes={FactoryRecipe.IronPlate,FactoryRecipe.Tools,FactoryRecipe.Flour,FactoryRecipe.Bread};
            for(int i=0;i<recipes.Length;i++)
            {
                FactoryRecipe recipe=recipes[i];
                Button button=MakeButton(FactoryCatalog.RecipeName(recipe),factoryRecipeSection.transform,new Vector2(94+i*134,-8),new Vector2(126,54),Navy2,()=>ChooseFactoryRecipe(recipe),new Vector2(0,1),11);
                button.name="Button_FactoryRecipe_"+recipe;factoryRecipeButtons[recipe]=button;
            }

            factoryFilterSection=Box("FactoryFilterSection",card.transform,new Vector2(24,-124),new Vector2(642,160),Navy2,new Vector2(0,1));
            LabelAt("투입기 필터",factoryFilterSection.transform,13,Coral,FontStyle.Bold,new Vector2(12,-8),new Vector2(110,22));
            Resource[] filters={Resource.Coins,Resource.Timber,Resource.Stone,Resource.Grain,Resource.Flour,Resource.Bread,Resource.Ore,Resource.Steel,Resource.Tools};
            for(int i=0;i<filters.Length;i++)
            {
                Resource resource=filters[i];int col=i%3,row=i/3;
                string label=resource==Resource.Coins?"모든 물자":Catalog.ResourceName(resource);
                Button button=MakeButton(label,factoryFilterSection.transform,new Vector2(126+col*169,-7-row*48),new Vector2(160,44),Navy2,()=>ChooseFactoryFilter(resource),new Vector2(0,1),11);
                button.name="Button_FactoryFilter_"+resource;factoryFilterButtons[resource]=button;
            }

            factoryFeedSection=Box("FactoryFeedSection",card.transform,new Vector2(24,-124),new Vector2(642,125),Navy2,new Vector2(0,1));
            LabelAt("도시 재고 투입 · 10개",factoryFeedSection.transform,13,Coral,FontStyle.Bold,new Vector2(12,-8),new Vector2(170,22));
            Resource[] feed={Resource.Timber,Resource.Stone,Resource.Grain,Resource.Flour,Resource.Bread,Resource.Ore,Resource.Steel,Resource.Tools};
            for(int i=0;i<feed.Length;i++)
            {
                Resource resource=feed[i];int col=i%4,row=i/4;
                Button button=MakeButton("",factoryFeedSection.transform,new Vector2(12+col*157,-33-row*46),new Vector2(149,44),Teal,()=>FeedFactory(resource),new Vector2(0,1),10);
                button.name="Button_FactoryFeed_"+resource;factoryFeedButtons[resource]=button;factoryFeedLabels[resource]=button.GetComponentInChildren<Text>();
            }
            FeelUiFeedback.AttachPanel(card);
            factoryConfigureOverlay.SetActive(false);
        }

        void BuildUtilityOverlays()
        {
            menuOverlay=Overlay("MenuOverlay");
            GameObject menu=Box("MenuCard",menuOverlay.transform,Vector2.zero,new Vector2(430,556),Paper,new Vector2(.5f,.5f));
            LabelAt("도시 메뉴",menu.transform,24,Cream,FontStyle.Bold,new Vector2(24,-20),new Vector2(260,34));
            Button menuClose=MakeButton("닫기",menu.transform,new Vector2(314,-14),new Vector2(92,44),Navy2,CloseTransientPanels,new Vector2(0,1),12);
            menuClose.name="Button_MenuClose";AddButtonIcon(menuClose,"close");
            LabelAt("도시 정보",menu.transform,11,Muted,FontStyle.Bold,new Vector2(24,-72),new Vector2(160,18));
            Button overview=MakeButton("현황",menu.transform,new Vector2(24,-96),new Vector2(118,48),Navy2,()=>OpenUtility("overview"),new Vector2(0,1),13);
            overview.name="Button_MenuOverview";AddButtonIcon(overview,"info");
            Button territory=MakeButton("영토",menu.transform,new Vector2(156,-96),new Vector2(118,48),Navy2,()=>OpenUtility("territory"),new Vector2(0,1),13);
            territory.name="Button_MenuTerritory";AddButtonIcon(territory,"map");
            Button trade=MakeButton("교역",menu.transform,new Vector2(288,-96),new Vector2(118,48),Teal,()=>OpenUtility("trade"),new Vector2(0,1),13);
            trade.name="Button_MenuTrade";

            LabelAt("시간",menu.transform,11,Muted,FontStyle.Bold,new Vector2(24,-164),new Vector2(160,18));
            float[] speeds={0,1,3};string[] speedNames={"Ⅱ  정지","1×  보통","3×  빠르게"};
            for(int i=0;i<3;i++){float s=speeds[i];Button b=MakeButton(speedNames[i],menu.transform,new Vector2(24+i*128,-188),new Vector2(118,46),Navy2,()=>controller.SetSpeed(s),new Vector2(0,1),11);}

            LabelAt("도시 관리",menu.transform,11,Muted,FontStyle.Bold,new Vector2(24,-254),new Vector2(160,18));
            Button save=MakeButton("저장",menu.transform,new Vector2(24,-278),new Vector2(118,46),Teal,()=>controller.SaveGame(),new Vector2(0,1),12);save.name="Button_저장";
            Button load=MakeButton("불러오기",menu.transform,new Vector2(156,-278),new Vector2(118,46),Navy2,()=>controller.LoadGame(),new Vector2(0,1),12);load.name="Button_불러오기";
            Button fresh=MakeButton("새 도시",menu.transform,new Vector2(288,-278),new Vector2(118,46),new Color(.55f,.28f,.25f,1),()=>SetConfirm(true),new Vector2(0,1),12);fresh.name="Button_새 도시";
            Button help=MakeButton("도움말  F1",menu.transform,new Vector2(24,-344),new Vector2(382,46),Navy2,()=>{CloseTransientPanels();SetHelp(true);},new Vector2(0,1),12);help.name="Button_도움말";
            Button effects=MakeButton("",menu.transform,new Vector2(24,-400),new Vector2(382,44),Navy2,ToggleFeelEffects,new Vector2(0,1),12);
            effects.name="Button_ReducedMotion";
            feelModeLabel=effects.GetComponentInChildren<Text>();
            RefreshFeelModeLabel();
            Button guide=MakeButton("단우에게 조언 듣기",menu.transform,new Vector2(24,-456),new Vector2(185,44),Navy2,()=>{CloseTransientPanels();controller.Tutorial?.OpenGuide();},new Vector2(0,1),12);
            guide.name="Button_Tutorial";
            Button residentAi=MakeButton("주민 AI",menu.transform,new Vector2(221,-456),new Vector2(185,44),Navy2,()=>{CloseTransientPanels();controller.ResidentAi?.OpenSettings();},new Vector2(0,1),12);
            residentAi.name="Button_ResidentAi";
            LabelAt("도시는 자동 저장됩니다.",menu.transform,11,Teal,FontStyle.Bold,new Vector2(24,-512),new Vector2(382,22)).alignment=TextAnchor.MiddleCenter;
            FeelUiFeedback.AttachPanel(menu);
            menuOverlay.SetActive(false);

            overviewOverlay=Overlay("OverviewOverlay");
            GameObject overviewCard=Box("OverviewCard",overviewOverlay.transform,Vector2.zero,new Vector2(540,350),Paper,new Vector2(.5f,.5f));
            LabelAt("도시 현황",overviewCard.transform,24,Cream,FontStyle.Bold,new Vector2(24,-20),new Vector2(340,34));
            Button overviewClose=MakeButton("닫기",overviewCard.transform,new Vector2(424,-14),new Vector2(92,44),Navy2,CloseTransientPanels,new Vector2(0,1),12);
            overviewClose.name="Button_OverviewClose";AddButtonIcon(overviewClose,"close");
            overviewBody=LabelAt("",overviewCard.transform,14,Ink,FontStyle.Normal,new Vector2(28,-78),new Vector2(484,238));
            overviewBody.alignment=TextAnchor.UpperLeft;
            FeelUiFeedback.AttachPanel(overviewCard);
            overviewOverlay.SetActive(false);

            territoryOverlay=Overlay("TerritoryOverlay");
            GameObject territoryCard=Box("TerritoryCard",territoryOverlay.transform,Vector2.zero,new Vector2(520,440),Paper,new Vector2(.5f,.5f));
            LabelAt("영토 확장",territoryCard.transform,24,Cream,FontStyle.Bold,new Vector2(24,-20),new Vector2(320,34));
            Button territoryClose=MakeButton("닫기",territoryCard.transform,new Vector2(404,-14),new Vector2(92,44),Navy2,CloseTransientPanels,new Vector2(0,1),12);
            territoryClose.name="Button_TerritoryClose";AddButtonIcon(territoryClose,"close");
            LabelAt("인접한 구역을 매입해 도시와 산업 설비를 함께 확장합니다.",territoryCard.transform,12,Muted,FontStyle.Normal,new Vector2(24,-68),new Vector2(472,32));
            for(int i=0;i<9;i++)
            {
                int region=i;int col=i%3,row=2-i/3;
                Button b=MakeButton("",territoryCard.transform,new Vector2(24+col*158,-118-row*82),new Vector2(146,68),Navy2,()=>controller.BuyRegion(region),new Vector2(0,1),13);
                b.name="Button_Region_"+(i+1);regionButtons.Add(b);regionLabels.Add(b.GetComponentInChildren<Text>());
            }
            FeelUiFeedback.AttachPanel(territoryCard);
            territoryOverlay.SetActive(false);

            mobileTradeOverlay=Overlay("TradeOverlay");
            GameObject tradeCard=Box("TradeCard",mobileTradeOverlay.transform,Vector2.zero,new Vector2(760,450),Paper,new Vector2(.5f,.5f));
            LabelAt("긴급 교역 · 10개 단위",tradeCard.transform,24,Cream,FontStyle.Bold,new Vector2(24,-18),new Vector2(450,36));
            Button tradeClose=MakeButton("닫기",tradeCard.transform,new Vector2(632,-16),new Vector2(104,44),Navy2,CloseTransientPanels,new Vector2(0,1),12);
            tradeClose.name="Button_TradeClose";AddButtonIcon(tradeClose,"close");
            Resource[] tradeResources={Resource.Timber,Resource.Stone,Resource.Grain,Resource.Flour,Resource.Bread,Resource.Ore,Resource.Steel,Resource.Tools};
            for(int i=0;i<tradeResources.Length;i++)
            {
                Resource resource=tradeResources[i];int col=i%2,row=i/2;float x=24+col*368,y=-82-row*82;
                LabelAt(Catalog.ResourceName(resource),tradeCard.transform,14,Ink,FontStyle.Bold,new Vector2(x,y),new Vector2(94,54));
                Button buy=MakeButton("구매  -"+(GameController.TradePrice(resource)*10)+"G",tradeCard.transform,new Vector2(x+96,y),new Vector2(122,56),Teal,()=>controller.Trade(resource,true),new Vector2(0,1),11);
                buy.name="Button_MobileBuy_"+resource;buyButtons[resource]=buy;
                Button sell=MakeButton("판매  +"+(Mathf.Max(1,GameController.TradePrice(resource)/2)*10)+"G",tradeCard.transform,new Vector2(x+224,y),new Vector2(122,56),Gold,()=>controller.Trade(resource,false),new Vector2(0,1),11);
                sell.name="Button_MobileSell_"+resource;
            }
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

            researchOverlay=Overlay("ResearchOverlay");
            Vector2 windowSize=new Vector2(1180,640);
            GameObject card=Box("ResearchCard",researchOverlay.transform,Vector2.zero,windowSize,Paper,new Vector2(.5f,.5f));
            researchWindow=card.GetComponent<RectTransform>();
            LabelAt("도시 기술 연구",card.transform,27,Cream,FontStyle.Bold,new Vector2(28,-18),new Vector2(360,38));
            Text intro=LabelAt("시대별 열을 위아래로 스크롤하세요. 선행 기술에서 다음 기술로 이어지는 경로를 카드에서 확인할 수 있습니다.",card.transform,12,Muted,FontStyle.Normal,new Vector2(28,-54),new Vector2(windowSize.x-210,24));
            intro.alignment=TextAnchor.MiddleLeft;
            Button researchClose=MakeButton("연구 닫기",card.transform,new Vector2(windowSize.x-152,-16),new Vector2(124,44),Coral,()=>controller.ToggleResearch(),new Vector2(0,1),12);
            researchClose.name="Button_ResearchClose";

            List<TechSpec> specs=TechCatalog.All.Where(spec=>spec!=null).ToList();
            List<Era> eras=specs.Select(spec=>spec.Era).Distinct().OrderBy(era=>(int)era).ToList();
            float side=28f;
            float gap=14f;
            float columnWidth=(windowSize.x-side*2-gap*Mathf.Max(0,eras.Count-1))/Mathf.Max(1,eras.Count);
            float columnHeight=windowSize.y-102f;
            for(int col=0;col<eras.Count;col++)
            {
                Era era=eras[col];
                List<TechSpec> eraSpecs=specs.Where(spec=>spec.Era==era).OrderBy(spec=>InitialResearchOrder(spec.Id)).ThenBy(spec=>specs.IndexOf(spec)).ToList();
                float x=side+col*(columnWidth+gap);
                GameObject column=Box("ResearchEra_"+era,card.transform,new Vector2(x,-88),new Vector2(columnWidth,columnHeight),Navy2,new Vector2(0,1));
                Color accent=EraColor(col);
                LabelAt(TechCatalog.EraName(era),column.transform,16,accent,FontStyle.Bold,new Vector2(12,-10),new Vector2(columnWidth-24,24));
                Text countLabel=LabelAt(eraSpecs.Count+"개 · 세로 스크롤",column.transform,10,Muted,FontStyle.Bold,new Vector2(columnWidth-152,-12),new Vector2(140,20));
                countLabel.alignment=TextAnchor.UpperRight;

                GameObject viewport=new GameObject("ResearchScroll_"+era,typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));
                viewport.transform.SetParent(column.transform,false);
                Rect(viewport,new Vector2(0,1),new Vector2(0,1),new Vector2(8,-42),new Vector2(columnWidth-16,columnHeight-50));
                Image viewportImage=viewport.GetComponent<Image>();
                viewportImage.color=new Color(0,0,0,0);
                viewportImage.raycastTarget=true;
                RectTransform viewportRect=viewport.GetComponent<RectTransform>();
                viewportRect.pivot=new Vector2(0,1);
                researchViewports[era]=viewportRect;

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
        }

        void BuildResearchCard(Transform parent, TechSpec spec, Vector2 pos, Vector2 size)
        {
            TechId id=spec.Id;
            GameObject card=Box("TechCard_"+id,parent,pos,size,new Color(Paper.r,Paper.g,Paper.b,.98f),new Vector2(0,1));
            researchCardImages[id]=card.GetComponent<Image>();
            researchCardRects[id]=card.GetComponent<RectTransform>();
            Text title=LabelAt(spec.Name,card.transform,17,Cream,FontStyle.Bold,new Vector2(12,-10),new Vector2(size.x-132,26));
            title.verticalOverflow=VerticalWrapMode.Overflow;
            GameObject badge=Box("TechStatus_"+id,card.transform,new Vector2(size.x-112,-10),new Vector2(100,24),Navy2,new Vector2(0,1));
            Text status=Label("",badge.transform,11,Cream,FontStyle.Bold,TextAnchor.MiddleCenter);
            Rect(status.gameObject,Vector2.zero,Vector2.one,new Vector2(4,1),new Vector2(-4,-1));
            status.alignment=TextAnchor.MiddleCenter;
            researchStatusTexts[id]=status;
            Text details=LabelAt("",card.transform,12,Ink,FontStyle.Normal,new Vector2(12,-40),new Vector2(size.x-24,116));
            details.alignment=TextAnchor.UpperLeft;
            Button button=MakeButton("연구 시작_"+id,card.transform,new Vector2(12,-154),new Vector2(size.x-24,44),Navy2,()=>controller.Research(id),new Vector2(0,1),12);
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

        static Color EraColor(int index)
        {
            return index%3==0?Coral:index%3==1?Teal:Gold;
        }

        void RefreshResearch()
        {
            GameState s=controller.State;
            eraText.text=TechCatalog.EraName(s.Era)+" · 기술 연구\n지식 "+Mathf.FloorToInt(s.ResearchPoints)+"점";
            if(s.ActiveResearch==TechId.None)
            {
                int ready=TechCatalog.All.Count(t=>{string reason;return controller.Sim.CanResearch(t.Id,out reason);});
                researchSummaryText.text="연구 가능 "+ready+" · +"+controller.Sim.ResearchPerDay.ToString("0.#")+"/일";
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
                pair.Value.text=spec.Description+"\n<b>선행 → "+prerequisites+"</b>\n지식 "+spec.ResearchCost+" · "+spec.CoinCost+"G · "+spec.DurationDays+"일\n효과: "+benefits;
                Button button=researchButtons[pair.Key];
                string reason;
                bool can=controller.Sim.CanResearch(pair.Key,out reason);
                button.interactable=can;
                button.GetComponentInChildren<Text>().text=completed?"연구 완료":active?"연구 중 · "+Mathf.RoundToInt(controller.Sim.ResearchProgress*100f)+"%":can?"연구 시작":ShortReason(reason);
                SetButtonColor(button,completed?Teal:active?Gold:can?Navy2:new Color(.38f,.4f,.42f,1));
                Text status=researchStatusTexts[pair.Key];
                status.text=completed?"완료":active?"진행 "+Mathf.RoundToInt(controller.Sim.ResearchProgress*100f)+"%":can?"연구 가능":"잠김";
                Image statusBackground=status.transform.parent.GetComponent<Image>();
                if(statusBackground!=null)statusBackground.color=completed?Teal:active?Gold:can?Navy2:Muted;
                Image cardImage=researchCardImages[pair.Key];
                if(cardImage!=null)cardImage.color=completed?new Color(.12f,.29f,.28f,1):active?new Color(.34f,.27f,.16f,1):Paper;
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
                regionLabels[i].text=owned?(i+1)+"구역\n보유":(i+1)+"구역\n"+cost+"G";
                bool adjacent=controller.State.OwnedRegions.Any(o=>Mathf.Abs(o%3-i%3)+Mathf.Abs(o/3-i/3)==1);
                regionButtons[i].interactable=!owned&&adjacent&&controller.State.Coins>=cost;
                SetButtonColor(regionButtons[i],owned?Teal:Navy2);
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
                SetButtonColor(pair.Value,pair.Value.interactable?Teal:new Color(.38f,.4f,.42f,1));
            }
        }

        GameObject Overlay(string name)
        {
            GameObject o=Panel(name,transform,new Color(Navy.r,Navy.g,Navy.b,.78f),Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            Rect(o,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); return o;
        }
    }
}

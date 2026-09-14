using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Riverworks
{
    /// <summary>Modal city-project browser and delivery controls for the main HUD.</summary>
    public sealed class CityProjectPanel : MonoBehaviour
    {
        const float PreferredWidth=860f,PreferredHeight=640f,Margin=24f;
        GameController game;
        Action closed;
        GameObject overlay;
        RectTransform card,content;
        ScrollRect scroll;
        Font font;
        CityProjectKind selected=CityProjectKind.GrandBridge;
        readonly Dictionary<CityProjectKind,Button> kindButtons=new Dictionary<CityProjectKind,Button>();
        readonly List<Text> requirementLabels=new List<Text>();
        Text description,status,stageHistory,materialTitle,reward,cancelWarning;
        Button siteButton,deliverButton,cancelButton;
        bool open,cancelPending;
        Vector2 lastSize=new Vector2(-1,-1);

        public bool IsOpen=>open;

        public void Initialize(GameController controller,Action onClosed)
        {
            if(game!=null)game.Changed-=Refresh;
            game=controller;closed=onClosed;
            if(game==null)return;
            RectTransform hostRect=transform as RectTransform;
            if(hostRect!=null){hostRect.anchorMin=Vector2.zero;hostRect.anchorMax=Vector2.one;hostRect.offsetMin=hostRect.offsetMax=Vector2.zero;}
            Image hostImage=GetComponent<Image>();
            if(hostImage!=null){hostImage.color=Color.clear;hostImage.raycastTarget=false;}
            font=GameFont.Load();
            Build();
            game.Changed+=Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if(game!=null)game.Changed-=Refresh;
            if(open&&game!=null)game.ModalOpen=false;
        }

        void Update()
        {
            if(!open||game==null)return;
            if(!game.ModalOpen){Hide(false);return;}
            Clamp();
        }

        public void Open()
        {
            if(game==null||overlay==null)return;
            if(game.HelpOpen)game.ToggleHelp();
            if(game.ResearchOpen)game.ToggleResearch();
            if(game.CityHud!=null)game.CityHud.CloseTransientPanels();
            open=true;cancelPending=false;
            overlay.SetActive(true);overlay.transform.SetAsLastSibling();
            game.ModalOpen=true;
            Clamp(true);
            scroll.verticalNormalizedPosition=1;
            Refresh();
        }

        public void Close()=>Hide(true);

        void Hide(bool releaseModal)
        {
            if(!open)return;
            open=false;cancelPending=false;
            if(overlay!=null)overlay.SetActive(false);
            if(releaseModal&&game!=null)game.ModalOpen=false;
            closed?.Invoke();
        }

        public void Refresh()
        {
            if(game==null||description==null)return;
            CityProjectSpec spec=CityProjects.Get(selected);
            if(spec==null)return;
            foreach(var pair in kindButtons)
            {
                pair.Value.interactable=pair.Key!=selected;
                SetColor(pair.Value,pair.Key==selected?HudStyle.Accent:HudStyle.SurfaceRaised);
            }
            CityProjectState state=CityProjects.Find(game.State,selected);
            description.text=spec.Description+"\n크기 "+spec.Width+"×"+spec.Height+" 도시 칸 · 시작 비용 "+spec.CoinCost.ToString("N0")+"G · 필요 기술 "+TechnologyName(spec.RequiredTech);
            reward.text=RewardSummary(spec);
            bool started=state!=null;
            bool complete=started&&state.Stage>=spec.Stages.Length;
            siteButton.gameObject.SetActive(!started);
            siteButton.GetComponentInChildren<Text>(true).text="부지 선택 · "+spec.CoinCost.ToString("N0")+"G";
            deliverButton.gameObject.SetActive(started&&!complete);
            cancelButton.gameObject.SetActive(started&&!complete);
            cancelWarning.gameObject.SetActive(cancelPending&&started&&!complete);
            if(!started)
            {
                StageSpec first=spec.Stages[0];
                status.text="미시작 · 버튼을 누른 뒤 도시에서 실제 부지를 선택하세요.";
                stageHistory.text="공사는 3단계입니다. 각 단계 재료를 직접 지급해야 공사일이 시작됩니다.";
                materialTitle.text="1단계 준비 재료 · "+first.Name+" · 공사 "+first.Days+"일";
                SetRequirements(first.Requirements,null,true);
                return;
            }
            if(complete)
            {
                status.text="완료 · "+state.CompletedDay+"일 · 부지 "+state.X+", "+state.Z+" · "+(CityProjects.IsConnected(game.State,state)?"도로 연결":"도로 연결 필요");
                stageHistory.text=string.Join("\n",spec.Stages.Select((stage,index)=>(index+1)+"단계 "+stage.Name+" · 완료"));
                materialTitle.text="현재 단계 재료";
                SetRequirements(null,null);
                return;
            }
            var current=spec.Stages[state.Stage];
            status.text=(state.Stage+1)+"/"+spec.Stages.Length+"단계 · "+current.Name+(state.DaysRemaining>0?" · 공사 "+state.DaysRemaining+"일 남음":" · 재료 지급 대기")+" · 부지 "+state.X+", "+state.Z+" · "+(CityProjects.IsConnected(game.State,state)?"도로 연결":"도로 연결 필요");
            stageHistory.text=state.Stage==0?"이전 단계 없음":string.Join("\n",spec.Stages.Take(state.Stage).Select((stage,index)=>(index+1)+"단계 "+stage.Name+" · 완료"));
            materialTitle.text=(state.Stage+1)+"단계 재료 · "+current.Name+" · 공사 "+current.Days+"일";
            SetRequirements(current.Requirements,state);
            deliverButton.interactable=state.DaysRemaining==0;
            SetColor(deliverButton,deliverButton.interactable?HudStyle.Positive:HudStyle.SurfaceRaised);
            int coinRefund=Mathf.FloorToInt(spec.CoinCost*.35f);
            cancelWarning.text="취소하면 현재 단계에 지급한 재료는 전부 돌아오고 "+coinRefund.ToString("N0")+"G(시작 비용의 35%)를 돌려받습니다. 완료한 이전 단계의 재료는 소모된 상태로 유지됩니다.";
            cancelButton.GetComponentInChildren<Text>().text=cancelPending?"정말 취소":"프로젝트 취소";
            SetColor(cancelButton,cancelPending?HudStyle.Danger:HudStyle.SurfaceRaised);
        }

        void SetRequirements(RecipeAmount[] requirements,CityProjectState state,bool planning=false)
        {
            for(int i=0;i<requirementLabels.Count;i++)
            {
                bool visible=requirements!=null&&i<requirements.Length;
                requirementLabels[i].gameObject.SetActive(visible);
                if(!visible)continue;
                RecipeAmount needed=requirements[i];
                int delivered=state!=null&&state.Delivered!=null&&(int)needed.Resource<state.Delivered.Count?state.Delivered[(int)needed.Resource]:0;
                int held=Mathf.FloorToInt(game.Sim.Get(needed.Resource));
                requirementLabels[i].text=ResourceCatalog.Get(needed.Resource).Name+(planning?"  필요 "+needed.Amount:"  지급 "+delivered+" / "+needed.Amount)+"  ·  도시 재고 "+held;
            }
        }

        void SelectKind(CityProjectKind kind)
        {
            selected=kind;cancelPending=false;Refresh();
        }

        void SelectSite(){cancelPending=false;game.SelectProjectSite(selected);Hide(true);}
        void Deliver(){cancelPending=false;game.DeliverProject(selected);Refresh();}
        void Cancel()
        {
            if(!cancelPending){cancelPending=true;Refresh();return;}
            cancelPending=false;game.CancelProject(selected);Refresh();
        }

        void Build()
        {
            foreach(Transform child in transform)Destroy(child.gameObject);
            overlay=Surface("CityProjectOverlay",transform,HudStyle.Dim);
            overlay.GetComponent<Image>().raycastTarget=true;
            Stretch((RectTransform)overlay.transform,Vector2.zero,Vector2.zero);
            GameObject cardObject=Surface("CityProjectCard",overlay.transform,HudStyle.Surface);
            card=(RectTransform)cardObject.transform;card.anchorMin=card.anchorMax=card.pivot=new Vector2(.5f,.5f);card.sizeDelta=new Vector2(PreferredWidth,PreferredHeight);
            FeelUiFeedback.AttachPanel(cardObject);
            Text title=Label("도시 대형 프로젝트",card,HudStyle.TitleSize,HudStyle.Text,TextAnchor.MiddleLeft);PlaceHorizontal(title.rectTransform,24,80,-12,HudStyle.TouchSize);
            Button close=MakeIconButton(card,new Vector2(-24,-12),HudAssets.CloseIcon,Close);close.name="Button_CityProjectClose";
            GameObject viewport=Surface("CityProjectViewport",card,Color.clear);
            RectTransform viewportRect=(RectTransform)viewport.transform;viewportRect.anchorMin=Vector2.zero;viewportRect.anchorMax=Vector2.one;viewportRect.offsetMin=new Vector2(0,12);viewportRect.offsetMax=new Vector2(0,-64);
            viewport.AddComponent<RectMask2D>();scroll=viewport.AddComponent<ScrollRect>();scroll.viewport=viewportRect;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=28;
            GameObject contentObject=new GameObject("CityProjectContent",typeof(RectTransform));contentObject.transform.SetParent(viewport.transform,false);
            content=(RectTransform)contentObject.transform;content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.pivot=new Vector2(.5f,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=new Vector2(0,548);scroll.content=content;
            CityProjectKind[] kinds={CityProjectKind.GrandBridge,CityProjectKind.CentralPowerPlant,CityProjectKind.ResearchCampus};
            for(int i=0;i<kinds.Length;i++)
            {
                CityProjectKind kind=kinds[i];CityProjectSpec spec=CityProjects.Get(kind);
                Button button=MakeButton("Button_CityProjectKind_"+kind,spec.Name,content,i,3,-4,HudStyle.SurfaceRaised,()=>SelectKind(kind));kindButtons[kind]=button;
            }
            description=Label("",content,HudStyle.BodySize,HudStyle.Text,TextAnchor.UpperLeft);PlaceHorizontal(description.rectTransform,24,24,-58,58);
            status=Label("",content,HudStyle.BodySize,HudStyle.Text,TextAnchor.MiddleLeft);PlaceHorizontal(status.rectTransform,24,24,-122,32);
            stageHistory=Label("",content,HudStyle.BodySize,HudStyle.TextMuted,TextAnchor.UpperLeft);PlaceHorizontal(stageHistory.rectTransform,24,24,-158,58);
            materialTitle=Label("현재 단계 재료",content,HudStyle.BodySize,HudStyle.TextMuted,TextAnchor.MiddleLeft);PlaceHorizontal(materialTitle.rectTransform,24,24,-220,22);
            for(int i=0;i<4;i++){Text row=Label("",content,HudStyle.BodySize,HudStyle.Text,TextAnchor.MiddleLeft);PlaceHorizontal(row.rectTransform,24,24,-246-i*28,24);requirementLabels.Add(row);}
            reward=Label("",content,HudStyle.BodySize,HudStyle.Positive,TextAnchor.UpperLeft);PlaceHorizontal(reward.rectTransform,24,24,-362,42);
            siteButton=MakeButton("Button_CityProjectSite","부지 선택",content,0,1,-410,HudStyle.Positive,SelectSite);
            deliverButton=MakeButton("Button_CityProjectDeliver","현재 단계 재료 지급",content,0,2,-410,HudStyle.Positive,Deliver);
            cancelButton=MakeButton("Button_CityProjectCancel","프로젝트 취소",content,1,2,-410,HudStyle.SurfaceRaised,Cancel);
            cancelWarning=Label("",content,HudStyle.BodySize,HudStyle.Danger,TextAnchor.UpperLeft);PlaceHorizontal(cancelWarning.rectTransform,24,24,-462,62);
            overlay.SetActive(false);
        }

        static string TechnologyName(TechId id){TechSpec tech=TechCatalog.Get(id);return tech==null?id.ToString():tech.Name;}

        static string RewardSummary(CityProjectSpec spec)
        {
            switch(spec.Kind)
            {
                case CityProjectKind.GrandBridge:return "완료 보상 · 4×1 실제 도로(레벨 1)로 전환";
                case CityProjectKind.CentralPowerPlant:return "완료 보상 · 도로 연결 및 석탄 2/일 소비 시 도시 전력 +80";
                case CityProjectKind.ResearchCampus:return "완료 보상 · 도로 연결 시 일일 연구 +10";
                default:return "완료 보상 · "+spec.RewardText;
            }
        }

        void Clamp(bool force=false)
        {
            RectTransform parent=overlay.transform as RectTransform;if(parent==null)return;
            Vector2 size=parent.rect.size;if(!force&&size==lastSize)return;lastSize=size;
            card.sizeDelta=new Vector2(Mathf.Min(PreferredWidth,Mathf.Max(300,size.x-Margin*2)),Mathf.Min(PreferredHeight,Mathf.Max(260,size.y-Margin*2)));
            content.sizeDelta=new Vector2(0,Mathf.Max(548,card.rect.height-76));
        }

        GameObject Surface(string name,Transform parent,Color color)
        {
            GameObject root=new GameObject(name,typeof(RectTransform),typeof(Image));root.transform.SetParent(parent,false);Image image=root.GetComponent<Image>();image.sprite=HudAssets.Panel;image.type=Image.Type.Sliced;image.color=color;return root;
        }

        Text Label(string value,Transform parent,int size,Color color,TextAnchor alignment)
        {
            GameObject root=new GameObject("Text",typeof(RectTransform),typeof(Text));root.transform.SetParent(parent,false);Text text=root.GetComponent<Text>();text.text=value;text.font=font;text.fontSize=size;text.fontStyle=FontStyle.Normal;text.color=color;text.alignment=alignment;text.supportRichText=false;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }

        Button MakeButton(string name,string label,Transform parent,int index,int count,float top,Color color,UnityEngine.Events.UnityAction action)
        {
            GameObject root=Surface(name,parent,color);PlaceInRow((RectTransform)root.transform,index,count,top);Button button=root.AddComponent<Button>();button.targetGraphic=root.GetComponent<Image>();button.onClick.AddListener(action);
            Text text=Label(label,root.transform,HudStyle.BodySize,HudStyle.Foreground(color),TextAnchor.MiddleCenter);Stretch(text.rectTransform,new Vector2(4,2),new Vector2(-4,-2));FeelUiFeedback.AttachButton(button);return button;
        }

        Button MakeIconButton(Transform parent,Vector2 position,string iconName,UnityEngine.Events.UnityAction action)
        {
            GameObject root=Surface("Button_Icon",parent,HudStyle.SurfaceRaised);RectTransform rect=(RectTransform)root.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,1);rect.anchoredPosition=position;rect.sizeDelta=new Vector2(HudStyle.TouchSize,HudStyle.TouchSize);
            Button button=root.AddComponent<Button>();button.targetGraphic=root.GetComponent<Image>();button.onClick.AddListener(action);
            GameObject iconObject=new GameObject("Icon",typeof(RectTransform),typeof(Image));iconObject.transform.SetParent(root.transform,false);RectTransform icon=(RectTransform)iconObject.transform;icon.anchorMin=icon.anchorMax=icon.pivot=new Vector2(.5f,.5f);icon.sizeDelta=new Vector2(22,22);
            Image image=iconObject.GetComponent<Image>();image.sprite=HudAssets.Icon(iconName);image.preserveAspect=true;image.color=HudStyle.Text;image.raycastTarget=false;FeelUiFeedback.AttachButton(button);return button;
        }

        static void SetColor(Button button,Color color){if(button==null)return;button.targetGraphic.color=color;Text text=button.GetComponentInChildren<Text>();if(text!=null)text.color=HudStyle.Foreground(color);}
        static void PlaceInRow(RectTransform rect,int index,int count,float top){float gap=12,left=24,right=24;rect.anchorMin=new Vector2((float)index/count,1);rect.anchorMax=new Vector2((float)(index+1)/count,1);rect.pivot=new Vector2(.5f,1);rect.offsetMin=new Vector2(left+(index==0?0:gap*.5f),top-HudStyle.TouchSize);rect.offsetMax=new Vector2(-right-(index==count-1?0:gap*.5f),top);}
        static void PlaceHorizontal(RectTransform rect,float left,float right,float top,float height){rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(.5f,1);rect.offsetMin=new Vector2(left,top-height);rect.offsetMax=new Vector2(-right,top);}
        static void Stretch(RectTransform rect,Vector2 min,Vector2 max){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=min;rect.offsetMax=max;}
    }
}

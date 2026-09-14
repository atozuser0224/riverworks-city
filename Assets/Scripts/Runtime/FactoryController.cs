using System;
using UnityEngine;
namespace Riverworks
{
    /// <summary>Physical machinery shares the city terrain, ownership, camera and inventories.</summary>
    public sealed class FactoryController : MonoBehaviour
    {
        public GameController Game { get; private set; }
        public FactorySimulation Sim { get; private set; }
        public FactoryState State => Sim.State;
        public bool IsOpen { get; private set; }
        public bool IsPractice => false;
        public int PracticeBlueprintIndex => 0;
        public bool RemovalMode { get; private set; }
        public FactoryKind SelectedTool { get; private set; }
        public int Direction { get; private set; }
        public int Rotation => Direction;
        public FactoryEntity SelectedEntity { get; private set; }
        public FactoryPlatform SelectedPlatform { get; private set; }
        public int ActiveFloor { get; private set; }
        public int LinkTargetFloor { get; private set; } = 1;
        public bool FoundationMode { get; private set; }
        public FactoryView View { get; private set; }
        public string Notice { get; private set; } = "";
        public event Action Changed;
        GameState boundCity;
        float elapsed,uiClock;
        int lastBudget=-1,lastTech=-1;
        public void Initialize(GameController game)
        {
            Game=game;Bind();
            View=new GameObject("Machinery in the city").AddComponent<FactoryView>();View.Initialize(this);
        }
        void Bind()
        {
            if(Game.State.Factory==null)Game.State.Factory=FactoryState.CreateCityGrid();
            if(!ReferenceEquals(boundCity,Game.State)||Sim==null||!ReferenceEquals(Sim.State,Game.State.Factory))
            {
                boundCity=Game.State;Sim=new FactorySimulation(boundCity.Factory,new CityLogistics(boundCity));
                SelectedEntity=null;SelectedPlatform=null;ActiveFloor=0;LinkTargetFloor=1;FoundationMode=false;lastBudget=-1;lastTech=-1;elapsed=0;
            }
            Configure();
        }
        void Configure()
        {
            int budget=Math.Max(0,Game.Sim.PowerCapacity-Game.Sim.PowerUsed);
            int mask=0;foreach(var tech in Game.State.Technologies)mask=unchecked(mask*31+(int)tech);
            if(mask!=lastTech){Sim.ConfigureTechnology(Game.State);lastTech=mask;}
            if(budget!=lastBudget){State.PowerBudget=budget;Sim.Recalculate();lastBudget=budget;}
        }
        public void NotifyCityChanged()
        {
            Bind();Sim.InvalidateEnvironment();Sim.Recalculate();View?.Refresh(false);
            if(SelectedEntity!=null&&!State.Entities.Contains(SelectedEntity))SelectedEntity=null;
        }
        public void Open(bool practice=false){Bind();IsOpen=true;RemovalMode=false;Changed?.Invoke();Game.NotifyWorldSelection();}
        public void OpenReal()=>Open();
        public void OpenPractice(){Open();SetNotice("이제 별도 실습장 없이 도시의 같은 부지에서 설비를 건설합니다.");}
        public void SelectBlueprint(int index){SetNotice("실습 전용 공장은 제거되었습니다. 도시 부지에 생산선을 배치하세요.");}
        public void Close(){bool upper=ActiveFloor>0;IsOpen=false;ActiveFloor=0;LinkTargetFloor=1;CancelTools();if(upper)AdjustFloorFocus(0);View?.Refresh(true);Changed?.Invoke();Game.NotifyWorldSelection();}
        public void CancelTools(){SelectedTool=FactoryKind.None;FoundationMode=false;RemovalMode=false;SelectedEntity=null;SelectedPlatform=null;View?.Highlight(-1,-1,false);}
        public void ClearSelection(){SelectedEntity=null;SelectedPlatform=null;}
        public void SetFloor(int floor)
        {
            if(floor<0||floor>FactoryLayers.MaxFloor)return;
            ActiveFloor=floor;LinkTargetFloor=floor<FactoryLayers.MaxFloor?floor+1:floor-1;
            AdjustFloorFocus(floor);
            SelectedEntity=null;SelectedPlatform=null;IsOpen=true;
            if(FoundationMode&&floor==0)FoundationMode=false;
            Game.ClearCityToolForFactory();View?.Highlight(-1,-1,false);View?.Refresh(true);
            Changed?.Invoke();Game.NotifyWorldSelection();
        }
        void AdjustFloorFocus(int floor)
        {
            var camera=Game?.CameraRig;if(camera==null)return;
            camera.Cinematics?.CancelForManualInput();
            Vector3 focus=camera.AuthoredFocus;focus.y=floor*FactoryView.FloorHeight;
            camera.CommitFocus(focus,camera.AuthoredSize);
        }
        public void SetLinkTargetFloor(int floor)
        {
            if(floor<0||floor>FactoryLayers.MaxFloor||Math.Abs(floor-ActiveFloor)!=1){SetNotice("층간 운송은 바로 위층이나 아래층으로 연결하세요.");return;}
            LinkTargetFloor=floor;Changed?.Invoke();Game.NotifyWorldSelection();
        }
        public void SelectFoundation()
        {
            Game.ClearCityToolForFactory();SelectedTool=FactoryKind.None;SelectedEntity=null;SelectedPlatform=null;RemovalMode=false;IsOpen=true;
            if(ActiveFloor==0)SetFloor(1);
            FoundationMode=true;Changed?.Invoke();Game.NotifyWorldSelection();
        }
        public void SelectTool(FactoryKind kind)
        {
            Game.ClearCityToolForFactory();SelectedTool=kind;SelectedEntity=null;SelectedPlatform=null;FoundationMode=false;RemovalMode=false;IsOpen=true;
            Changed?.Invoke();Game.NotifyWorldSelection();
        }
        public void SelectRemove(){Game.ClearCityToolForFactory();SelectedTool=FactoryKind.None;FoundationMode=false;RemovalMode=!RemovalMode;IsOpen=true;Changed?.Invoke();Game.NotifyWorldSelection();}
        public void SetSpeed(float speed){Game.SetSpeed(speed);Changed?.Invoke();}
        public void SetNotice(string message){Notice=message;Game.SetNotice(message);Changed?.Invoke();}
        public void Save(){if(Game.TrySaveGame(out var error)){SetNotice("도시와 같은 지도 위의 생산선을 저장했습니다.");Game.Feel?.Play(FeelCue.Save,BoardView.Position(10,10));}else{SetNotice("저장 실패: "+error);Game.Feel?.Play(FeelCue.Denied,BoardView.Position(10,10));}}
        public void Rotate()
        {
            if(SelectedTool!=FactoryKind.None){Direction=(Direction+1)%4;Changed?.Invoke();Game.NotifyWorldSelection();return;}
            if(SelectedEntity!=null){bool okay=Sim.Rotate(SelectedEntity.Id,out var reason);if(okay)WorldChanged();SetNotice(okay?"설비 방향을 바꿨습니다.":reason);Game.Feel?.Play(okay?FeelCue.FactoryRecipe:FeelCue.Denied,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z,SelectedEntity.Floor),okay?View.ModelFor(SelectedEntity.Id):null);}
        }
        public bool CanAfford(FactoryKind kind,out string reason)
        {
            var spec=FactoryCatalog.Get(kind);reason="";
            if(spec==null){reason="설비를 선택하세요.";return false;}
            if(!TechCatalog.Has(Game.State,spec.RequiredTech)){reason=TechCatalog.Get(spec.RequiredTech).Name+" 연구가 필요합니다.";return false;}
            int count=IsVerticalLink(kind)?2:1;
            if(Game.State.Coins<spec.CoinCost*count||Game.Sim.Get(Resource.Timber)<spec.TimberCost*count||Game.Sim.Get(Resource.Stone)<spec.StoneCost*count)
            {reason=$"필요: {spec.CoinCost*count}G · 목재 {spec.TimberCost*count} · 석재 {spec.StoneCost*count}";return false;}
            return true;
        }
        public bool CanPlaceAt(int x,int z,out string reason)
        {
            SnapPlacement(ref x,ref z);
            if(FoundationMode)return FactoryLayers.CanPlacePlatform(Game.State,x,z,ActiveFloor,out reason);
            if(!CanAfford(SelectedTool,out reason))return false;
            if(IsVerticalLink(SelectedTool))
            {
                if(Math.Abs(LinkTargetFloor-ActiveFloor)!=1){reason="인접한 연결층을 선택하세요.";return false;}
                if(!Sim.CanPlace(SelectedTool,x,z,Direction,ActiveFloor,out reason))return false;
                return Sim.CanPlace(SelectedTool,x,z,Direction,LinkTargetFloor,out reason);
            }
            return Sim.CanPlace(SelectedTool,x,z,Direction,ActiveFloor,out reason);
        }
        public void PlaceAt(int x,int z)
        {
            Bind();
            if(SelectedTool!=FactoryKind.None||FoundationMode)SnapPlacement(ref x,ref z);
            if(RemovalMode)
            {
                var old=Sim.GetAt(x,z,ActiveFloor);var spec=old==null?null:FactoryCatalog.Get(old.Kind);
                if(old==null&&ActiveFloor>0)
                {
                    int px=x/2*2,pz=z/2*2;
                    bool removed=FactoryLayers.RemovePlatform(Game.State,px,pz,ActiveFloor,out var platformReason);
                    if(removed){SelectedPlatform=null;WorldChanged();}
                    SetNotice(platformReason);return;
                }
                int count=old!=null&&IsVerticalLink(old.Kind)?2:1;
                if(Sim.Remove(x,z,ActiveFloor,out var reason))
                {
                    if(spec!=null){Game.State.Coins+=spec.CoinCost*.35f*count;Game.State.Stock[1]+=spec.TimberCost*.35f*count;Game.State.Stock[2]+=spec.StoneCost*.35f*count;}
                    Recover();SelectedEntity=null;WorldChanged();SetNotice("설비를 철거하고 물자와 일부 건설비를 회수했습니다.");
                    Game.Feel?.Play(FeelCue.FactoryRemove,View.WorldPosition(x,z,ActiveFloor));
                }else {SetNotice(reason);Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(x,z,ActiveFloor));}
                return;
            }
            if(FoundationMode)
            {
                bool placed=FactoryLayers.TryPlacePlatform(Game.State,x,z,ActiveFloor,out var platformReason);
                if(placed)WorldChanged();
                SetNotice(platformReason);Game.Feel?.Play(placed?FeelCue.FactoryBuild:FeelCue.Denied,View.WorldPosition(x,z,ActiveFloor));return;
            }
            if(SelectedTool==FactoryKind.None){SelectAt(x,z);return;}
            if(!CanPlaceAt(x,z,out var message)){SetNotice(message);Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(x,z,ActiveFloor));return;}
            var price=FactoryCatalog.Get(SelectedTool);
            bool linked=IsVerticalLink(SelectedTool);
            bool okay=linked?Sim.TryPlaceLink(SelectedTool,x,z,Direction,ActiveFloor,LinkTargetFloor,out message):Sim.TryPlace(SelectedTool,x,z,Direction,ActiveFloor,out message);
            if(okay)
            {
                int count=linked?2:1;
                Game.State.Coins-=price.CoinCost*count;Game.State.Stock[1]-=price.TimberCost*count;Game.State.Stock[2]-=price.StoneCost*count;
                SyncCoins();SelectedEntity=Sim.GetAt(x,z,ActiveFloor);WorldChanged();SetNotice(price.Name+(linked?" 층간 연결 완료":" 건설 완료"));
                Game.Feel?.Play(FeelCue.FactoryBuild,View.WorldPosition(x,z,ActiveFloor),SelectedEntity!=null?View.ModelFor(SelectedEntity.Id):null);
            }else {SetNotice(message);Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(x,z,ActiveFloor));}
        }
        public bool SelectAt(int x,int z)
        {
            SelectedEntity=Sim.GetAt(x,z,ActiveFloor);SelectedPlatform=null;
            if(SelectedEntity==null)
            {
                if(ActiveFloor>0)SelectedPlatform=State.Platforms.Find(p=>p.Floor==ActiveFloor&&x>=p.X&&x<p.X+2&&z>=p.Z&&z<p.Z+2);
                if(SelectedPlatform==null)return false;
                Game.ClearCitySelection();Changed?.Invoke();Game.NotifyWorldSelection();return true;
            }
            Game.ClearCitySelection();Changed?.Invoke();Game.NotifyWorldSelection();
            Game.Feel?.Play(FeelCue.Select,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z,SelectedEntity.Floor),View.ModelFor(SelectedEntity.Id));return true;
        }
        public bool SelectAt(int x,int z,int floor){if(floor<0||floor>FactoryLayers.MaxFloor)return false;if(floor!=ActiveFloor)SetFloor(floor);return SelectAt(x,z);}
        static bool IsVerticalLink(FactoryKind kind)=>kind==FactoryKind.ItemLift||kind==FactoryKind.FluidRiser;
        public void ChooseRecipe(FactoryRecipe recipe)
        {
            if(SelectedEntity==null)return;
            Configure();
            bool changed=SelectedEntity.Recipe!=recipe;
            bool okay=Sim.ReconfigureRecipe(SelectedEntity.Id,recipe,out var reason);
            if(okay)Recover();
            SetNotice(okay?(changed?"제조법을 변경하고 기존 물자를 회수했습니다.":"현재 제조법입니다."):reason);WorldChanged();
            if(changed||!okay)Game.Feel?.Play(okay?FeelCue.FactoryRecipe:FeelCue.Denied,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z,SelectedEntity.Floor),okay?View.ModelFor(SelectedEntity.Id):null);
        }
        public void ChooseFilter(Resource resource){if(SelectedEntity==null)return;bool okay=Sim.SetFilter(SelectedEntity.Id,resource,out var reason);SetNotice(okay?"물자·유체 필터를 바꿨습니다.":reason);View?.Refresh(false);}
        public void SetPaused(bool paused)
        {
            if(SelectedEntity==null)return;
            bool okay=Sim.SetPaused(SelectedEntity.Id,paused,out string reason);
            if(okay)WorldChanged();
            SetNotice(okay?(paused?"설비를 일시 정지했습니다.":"설비를 다시 가동합니다."):reason);
        }
        public void SetClock(int percent)
        {
            if(SelectedEntity==null)return;
            Configure();
            bool okay=Sim.SetClock(SelectedEntity.Id,percent,out string reason);
            if(okay)WorldChanged();
            SetNotice(okay?"설비 가동률을 "+percent+"%로 설정했습니다.":reason);
        }
        public bool SaveAutomationRule(AutomationRule rule,out string reason)
        {
            Bind();bool okay=FactoryAutomation.SaveRule(Game.State,rule,out reason);
            if(okay){FactoryAutomation.Evaluate(State,Game.State);Sim.InvalidateEnvironment();WorldChanged();}
            SetNotice(reason);return okay;
        }
        public void SaveAutomationRule(AutomationRule rule){SaveAutomationRule(rule,out _);}
        public bool RemoveAutomationRule(int id,out string reason)
        {
            Bind();bool okay=FactoryAutomation.RemoveRule(Game.State,id,out reason);
            if(okay){FactoryAutomation.Evaluate(State,Game.State);Sim.InvalidateEnvironment();WorldChanged();}
            SetNotice(reason);return okay;
        }
        public void RemoveAutomationRule(int id){RemoveAutomationRule(id,out _);}
        public void RecoverSelected()
        {
            if(SelectedEntity==null)return;
            FactoryEntity entity=SelectedEntity;
            for(int i=1;i<ResourceCatalog.Count;i++)
            {
                State.Recovered[i]+=entity.Input[i]+entity.Output[i];
                entity.Input[i]=entity.Output[i]=0;
            }
            if(ResourceCatalog.IsTransportable(entity.CargoResource))State.Recovered[(int)entity.CargoResource]++;
            entity.CargoResource=Resource.Coins;
            entity.CargoProgress=entity.Progress=entity.FluidProgress=0;
            Recover();Sim.Recalculate();WorldChanged();
            SetNotice("설비의 물자와 유체를 도시 회수 재고로 옮겼습니다.");
        }
        public void Feed(Resource resource,int amount=10)
        {
            if(SelectedEntity==null){SetNotice("창고·반입 부두 또는 유체 탱크를 선택하세요.");return;}
            if(!ResourceCatalog.CanFeed(resource)||amount<=0||amount>1000)return;
            if(Game.Sim.Get(resource)<amount){SetNotice(Catalog.ResourceName(resource)+" 재고가 부족합니다.");Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z,SelectedEntity.Floor));return;}
            if(Sim.AddInput(SelectedEntity.Id,resource,amount,out var reason)){Game.State.Stock[(int)resource]-=amount;SetNotice(Catalog.ResourceName(resource)+" "+amount+ResourceCatalog.Get(resource).Unit+" 투입");Game.Feel?.Play(FeelCue.FactoryTransfer,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z,SelectedEntity.Floor),View.ModelFor(SelectedEntity.Id));}else{SetNotice(reason);Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z,SelectedEntity.Floor));}
        }
        void Recover(){for(int i=1;i<ResourceCatalog.Count;i++){Game.State.Stock[i]+=State.Recovered[i];State.Recovered[i]=0;}SyncCoins();}
        void SyncCoins()=>Game.State.Stock[0]=Game.State.Coins;
        void WorldChanged(){Game.RefreshWorld(true);View?.Refresh(false);Changed?.Invoke();Game.NotifyWorldSelection();}
        public bool IsPointerOverUI()=>Game.IsScreenPointOverUI(Input.mousePosition);
        public bool IsScreenPointOverUI(Vector2 point)=>Game.IsScreenPointOverUI(point);
        public static Vector2Int GridAt(Vector3 point)
        {
            var origin=BoardView.Position(0,0);
            return new Vector2Int(Mathf.FloorToInt((point.x-origin.x)/BoardView.Spacing*2+1),Mathf.FloorToInt((point.z-origin.z)/BoardView.Spacing*2+1));
        }
        public bool InteractScreenPoint(Vector2 point)
        {
            if(Game.IsScreenPointOverUI(point)||!View.Camera.pixelRect.Contains(point))return false;
            if(!View.TryGetFloorPoint(point,ActiveFloor,out var world))return false;
            var grid=GridAt(world);PlaceAt(grid.x,grid.y);return true;
        }
        public void HighlightPoint(Vector3 point)
        {
            var grid=GridAt(point);
            int x=grid.x,z=grid.y;if(SelectedTool!=FactoryKind.None||FoundationMode)SnapPlacement(ref x,ref z);
            bool valid=SelectedTool==FactoryKind.None&&!FoundationMode||CanPlaceAt(x,z,out _);
            View.Highlight(x,z,valid);
        }
        void SnapPlacement(ref int x,ref int z){var spec=FactoryCatalog.Get(SelectedTool);if(FoundationMode||spec!=null&&spec.Width==2&&spec.Height==2){x=Mathf.FloorToInt(x/2f)*2;z=Mathf.FloorToInt(z/2f)*2;}}
        void Update()
        {
            if(Game==null)return;Bind();
            if(!Game.HelpOpen&&!Game.ResearchOpen&&!Game.ModalOpen)
            {
                elapsed+=Time.unscaledDeltaTime*Game.GameSpeed;
                while(elapsed>=.1f)
                {
                    elapsed-=.1f;Configure();int tools=State.Produced[(int)Resource.Tools],total=0;
                    for(int i=1;i<ResourceCatalog.Count;i++)if(ResourceCatalog.IsSolid((Resource)i))total+=State.Produced[i];
                    Sim.Tick(.1f);
                    int nextTotal=0;
                    for(int i=1;i<ResourceCatalog.Count;i++)if(ResourceCatalog.IsSolid((Resource)i)){nextTotal+=State.Produced[i];Game.State.Stock[i]+=Sim.TakeExports((Resource)i);}
                    Game.State.TotalToolsProduced+=Math.Max(0,State.Produced[(int)Resource.Tools]-tools);
                    Game.State.TotalProduced+=Math.Max(0,nextTotal-total);
                }
            }
            uiClock+=Time.unscaledDeltaTime;
            if(uiClock>=.25f){uiClock=0;Changed?.Invoke();}
        }
    }
}

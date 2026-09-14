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
                SelectedEntity=null;lastBudget=-1;lastTech=-1;elapsed=0;
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
        public void Close(){IsOpen=false;CancelTools();Changed?.Invoke();Game.NotifyWorldSelection();}
        public void CancelTools(){SelectedTool=FactoryKind.None;RemovalMode=false;SelectedEntity=null;View?.Highlight(-1,-1,false);}
        public void ClearSelection(){SelectedEntity=null;}
        public void SelectTool(FactoryKind kind)
        {
            Game.ClearCityToolForFactory();SelectedTool=kind;SelectedEntity=null;RemovalMode=false;IsOpen=true;
            Changed?.Invoke();Game.NotifyWorldSelection();
        }
        public void SelectRemove(){Game.ClearCityToolForFactory();SelectedTool=FactoryKind.None;RemovalMode=!RemovalMode;IsOpen=true;Changed?.Invoke();Game.NotifyWorldSelection();}
        public void SetSpeed(float speed){Game.SetSpeed(speed);Changed?.Invoke();}
        public void SetNotice(string message){Notice=message;Game.SetNotice(message);Changed?.Invoke();}
        public void Save(){if(Game.TrySaveGame(out var error)){SetNotice("도시와 같은 지도 위의 생산선을 저장했습니다.");Game.Feel?.Play(FeelCue.Save,BoardView.Position(10,10));}else{SetNotice("저장 실패: "+error);Game.Feel?.Play(FeelCue.Denied,BoardView.Position(10,10));}}
        public void Rotate()
        {
            if(SelectedTool!=FactoryKind.None){Direction=(Direction+1)%4;Changed?.Invoke();Game.NotifyWorldSelection();return;}
            if(SelectedEntity!=null){bool okay=Sim.Rotate(SelectedEntity.Id,out var reason);if(okay)WorldChanged();SetNotice(okay?"설비 방향을 바꿨습니다.":reason);Game.Feel?.Play(okay?FeelCue.FactoryRecipe:FeelCue.Denied,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z),okay?View.ModelFor(SelectedEntity.Id):null);}
        }
        public bool CanAfford(FactoryKind kind,out string reason)
        {
            var spec=FactoryCatalog.Get(kind);reason="";
            if(spec==null){reason="설비를 선택하세요.";return false;}
            if(!TechCatalog.Has(Game.State,spec.RequiredTech)){reason=TechCatalog.Get(spec.RequiredTech).Name+" 연구가 필요합니다.";return false;}
            if(Game.State.Coins<spec.CoinCost||Game.Sim.Get(Resource.Timber)<spec.TimberCost||Game.Sim.Get(Resource.Stone)<spec.StoneCost)
            {reason=$"필요: {spec.CoinCost}G · 목재 {spec.TimberCost} · 석재 {spec.StoneCost}";return false;}
            return true;
        }
        public bool CanPlaceAt(int x,int z,out string reason)
        {
            SnapPlacement(ref x,ref z);
            if(!CanAfford(SelectedTool,out reason))return false;
            return Sim.CanPlace(SelectedTool,x,z,Direction,out reason);
        }
        public void PlaceAt(int x,int z)
        {
            Bind();
            if(SelectedTool!=FactoryKind.None)SnapPlacement(ref x,ref z);
            if(RemovalMode)
            {
                var old=Sim.GetAt(x,z);var spec=old==null?null:FactoryCatalog.Get(old.Kind);
                if(Sim.Remove(x,z,out var reason))
                {
                    if(spec!=null){Game.State.Coins+=spec.CoinCost*.35f;Game.State.Stock[1]+=spec.TimberCost*.35f;Game.State.Stock[2]+=spec.StoneCost*.35f;}
                    Recover();SelectedEntity=null;WorldChanged();SetNotice("설비를 철거하고 물자와 일부 건설비를 회수했습니다.");
                    Game.Feel?.Play(FeelCue.FactoryRemove,View.WorldPosition(x,z));
                }else {SetNotice(reason);Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(x,z));}
                return;
            }
            if(SelectedTool==FactoryKind.None){SelectAt(x,z);return;}
            if(!CanPlaceAt(x,z,out var message)){SetNotice(message);Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(x,z));return;}
            var price=FactoryCatalog.Get(SelectedTool);
            if(Sim.TryPlace(SelectedTool,x,z,Direction,out message))
            {
                Game.State.Coins-=price.CoinCost;Game.State.Stock[1]-=price.TimberCost;Game.State.Stock[2]-=price.StoneCost;
                SyncCoins();SelectedEntity=Sim.GetAt(x,z);WorldChanged();SetNotice(price.Name+" 건설 완료");
                Game.Feel?.Play(FeelCue.FactoryBuild,View.WorldPosition(x,z),SelectedEntity!=null?View.ModelFor(SelectedEntity.Id):null);
            }else {SetNotice(message);Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(x,z));}
        }
        public bool SelectAt(int x,int z)
        {
            SelectedEntity=Sim.GetAt(x,z);
            if(SelectedEntity==null)return false;
            Game.ClearCitySelection();Changed?.Invoke();Game.NotifyWorldSelection();
            Game.Feel?.Play(FeelCue.Select,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z),View.ModelFor(SelectedEntity.Id));return true;
        }
        public void ChooseRecipe(FactoryRecipe recipe)
        {
            if(SelectedEntity==null)return;
            bool valid=SelectedEntity.Kind==FactoryKind.Furnace?recipe==FactoryRecipe.IronPlate:SelectedEntity.Kind==FactoryKind.Assembler&&(recipe==FactoryRecipe.Tools||recipe==FactoryRecipe.Flour||recipe==FactoryRecipe.Bread);
            bool changed=valid&&SelectedEntity.Recipe!=recipe;
            if(changed){for(int i=1;i<9;i++){State.Recovered[i]+=SelectedEntity.Input[i]+SelectedEntity.Output[i];SelectedEntity.Input[i]=SelectedEntity.Output[i]=0;}SelectedEntity.Progress=0;Recover();}
            bool okay=Sim.SetRecipe(SelectedEntity.Id,recipe,out var reason);SetNotice(okay?"제조법을 변경했습니다. 기존 물자는 회수했습니다.":reason);WorldChanged();
            if(changed||!okay)Game.Feel?.Play(okay?FeelCue.FactoryRecipe:FeelCue.Denied,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z),okay?View.ModelFor(SelectedEntity.Id):null);
        }
        public void ChooseFilter(Resource resource){if(SelectedEntity==null)return;bool okay=Sim.SetFilter(SelectedEntity.Id,resource,out var reason);SetNotice(okay?"투입기 물자 필터를 바꿨습니다.":reason);}
        public void Feed(Resource resource,int amount=10)
        {
            if(SelectedEntity==null){SetNotice("보관함 또는 반입 부두를 선택하세요.");return;}
            if(resource==Resource.Coins||(int)resource<1||(int)resource>8||amount<=0||amount>1000)return;
            if(Game.Sim.Get(resource)<amount){SetNotice(Catalog.ResourceName(resource)+" 재고가 부족합니다.");Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z));return;}
            if(Sim.AddInput(SelectedEntity.Id,resource,amount,out var reason)){Game.State.Stock[(int)resource]-=amount;SetNotice(Catalog.ResourceName(resource)+" "+amount+"개 투입");Game.Feel?.Play(FeelCue.FactoryTransfer,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z),View.ModelFor(SelectedEntity.Id));}else{SetNotice(reason);Game.Feel?.Play(FeelCue.Denied,View.WorldPosition(SelectedEntity.X,SelectedEntity.Z));}
        }
        void Recover(){for(int i=1;i<9;i++){Game.State.Stock[i]+=State.Recovered[i];State.Recovered[i]=0;}SyncCoins();}
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
            if(!Physics.Raycast(View.Camera.ScreenPointToRay(point),out var hit,200,1<<8))return false;
            var grid=GridAt(hit.point);PlaceAt(grid.x,grid.y);return true;
        }
        public void HighlightPoint(Vector3 point)
        {
            var grid=GridAt(point);
            int x=grid.x,z=grid.y;if(SelectedTool!=FactoryKind.None)SnapPlacement(ref x,ref z);
            bool valid=SelectedTool==FactoryKind.None||CanPlaceAt(x,z,out _);
            View.Highlight(x,z,valid);
        }
        void SnapPlacement(ref int x,ref int z){var spec=FactoryCatalog.Get(SelectedTool);if(spec!=null&&spec.Width==2&&spec.Height==2){x=Mathf.FloorToInt(x/2f)*2;z=Mathf.FloorToInt(z/2f)*2;}}
        void Update()
        {
            if(Game==null)return;Bind();
            if(!Game.HelpOpen&&!Game.ResearchOpen&&!Game.ModalOpen)
            {
                elapsed+=Time.unscaledDeltaTime*Game.GameSpeed;
                while(elapsed>=.1f)
                {
                    elapsed-=.1f;Configure();int tools=State.Produced[(int)Resource.Tools],total=0;
                    for(int i=1;i<9;i++)total+=State.Produced[i];
                    Sim.Tick(.1f);
                    int nextTotal=0;
                    for(int i=1;i<9;i++){nextTotal+=State.Produced[i];Game.State.Stock[i]+=Sim.TakeExports((Resource)i);}
                    Game.State.TotalToolsProduced+=Math.Max(0,State.Produced[(int)Resource.Tools]-tools);
                    Game.State.TotalProduced+=Math.Max(0,nextTotal-total);
                }
            }
            uiClock+=Time.unscaledDeltaTime;
            if(uiClock>=.25f){uiClock=0;Changed?.Invoke();}
        }
    }
}

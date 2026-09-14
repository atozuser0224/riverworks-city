using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Riverworks
{
    public static class SaveStore
    {
        private const int ExpectedSize = 21;
        private const int MaximumSaveBytes = 4 * 1024 * 1024;

        public static bool TrySave(string path, GameState state, out string error)
        {
            error = "";
            string temporary = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("저장 경로가 비어 있습니다.", nameof(path));
                Validate(state);
                path = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory)) throw new ArgumentException("저장 경로가 올바르지 않습니다.", nameof(path));
                Directory.CreateDirectory(directory);

                string json = JsonUtility.ToJson(state, true);
                if (string.IsNullOrEmpty(json)) throw new InvalidDataException("도시 데이터를 직렬화할 수 없습니다.");
                if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumSaveBytes) throw new InvalidDataException("저장 데이터가 너무 큽니다.");
                if (File.Exists(path) && File.ReadAllText(path) == json) return true;

                // A unique sibling file makes concurrent/retried saves independent while keeping
                // the final rename on the same volume. WriteThrough reduces the chance that a
                // reported-success save exists only in an operating-system buffer.
                temporary = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                temporary = null;
                return true;
            }
            catch(Exception ex) { error=ex.Message; return false; }
            finally
            {
                if (!string.IsNullOrEmpty(temporary))
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch { /* A failed cleanup must not hide the original save error. */ }
                }
            }
        }
        public static bool TryLoad(string path, out GameState state, out string error)
        {
            state=null; error="";
            try
            {
                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("저장 경로가 비어 있습니다.", nameof(path));
                path = Path.GetFullPath(path);
                if (!File.Exists(path)) throw new IOException("저장된 도시가 없습니다.");
                long length = new FileInfo(path).Length;
                if (length <= 0 || length > MaximumSaveBytes) throw new IOException("저장 파일 크기가 올바르지 않습니다.");
                var candidate = JsonUtility.FromJson<GameState>(File.ReadAllText(path));
                if(candidate!=null && candidate.Version>=1&&candidate.Version<=4)
                {
                    ValidateLegacyData(candidate);
                    NormalizeAbsentTutorial(candidate);
                    NormalizeAbsentLegacyFactories(candidate);
                    IndustryMigration.UpgradeLegacy(candidate);
                    ValidateVersion5(candidate);
                    ExpansionMigration.Upgrade(candidate);
                }
                else if(candidate!=null&&candidate.Version==5)
                {
                    NormalizeAbsentTutorial(candidate);
                    ValidateVersion5(candidate);
                    NormalizeAbsentLegacyFactories(candidate);
                    ExpansionMigration.Upgrade(candidate);
                }
                else NormalizeAbsentTutorial(candidate);
                Validate(candidate);
                state=candidate; return true;
            }
            catch(Exception ex) { error=ex.Message; return false; }
        }
        public static void Validate(GameState state)
        {
            ValidateState(state,6,false);
        }

        static void ValidateVersion5(GameState state)
        {
            ValidateState(state,5,true);
        }

        static void ValidateState(GameState state,int expectedVersion,bool frozenVersion5)
        {
            if(state==null || state.Version!=expectedVersion || state.Size!=ExpectedSize) throw new InvalidDataException("지원하지 않는 도시 형식입니다.");
            int resourceCount = ResourceCatalog.Count;
            if(!FiniteNonnegative(state.DayProgressSeconds)||state.DayProgressSeconds>=GameController.SecondsPerDay)throw new InvalidDataException("도시 시간 진행값이 올바르지 않습니다.");
            if(state.Cells==null || state.Cells.Count!=ExpectedSize*ExpectedSize || state.Stock==null || state.Stock.Count!=resourceCount) throw new InvalidDataException("도시 데이터가 완전하지 않습니다.");
            // Tutorial validation is read-only and runs before validators that may normalize
            // other optional runtime buffers, so a bad tutorial cannot partially mutate a save.
            if(!TutorialCatalog.TryValidate(state,out var tutorialError))throw new InvalidDataException(tutorialError);
            if(!FiniteNonnegative(state.Coins) || state.Population<0 || state.Day<0 || state.Happiness<0 || state.Happiness>100 ||
               !FiniteNonnegative(state.TotalProduced) || !FiniteNonnegative(state.TotalToolsProduced) ||
               !FiniteNonnegative(state.LastBreadDemand) || !FiniteNonnegative(state.LastToolsDemand) || !FiniteNonnegative(state.LastToolsDelivered) ||
               state.LastToolsDelivered > state.LastToolsDemand + .001f)
                throw new InvalidDataException("잘못된 도시 수치입니다.");
            foreach(var amount in state.Stock) if(!Finite(amount) || amount<0) throw new InvalidDataException("잘못된 물자 수치입니다.");
            if (Math.Abs(state.Stock[(int)Resource.Coins] - state.Coins) > .01f) throw new InvalidDataException("코인 데이터가 일치하지 않습니다.");
            if (state.Milestone < 0 || state.Milestone > 5 || state.Won != (state.Milestone == 5)) throw new InvalidDataException("목표 진행 데이터가 올바르지 않습니다.");
            ValidateTechnology(state);
            if(state.Factory==null) throw new InvalidDataException("공장 저장 데이터가 없습니다.");
            if(frozenVersion5)FactoryStateValidation.ValidateVersion2(state.Factory);else FactoryStateValidation.Validate(state.Factory);
            if(state.Factory.Width!=42||state.Factory.Height!=42)throw new InvalidDataException("공장 설비는 도시 공유 부지에 있어야 합니다.");
            foreach(var entity in state.Factory.Entities)
            {
                var spec=entity==null?null:FactoryCatalog.Get(entity.Kind);
                if(spec==null)throw new InvalidDataException("알 수 없는 공장 설비입니다.");
                if(!TechCatalog.Has(state,spec.RequiredTech))throw new InvalidDataException("연구하지 않은 공장 설비입니다.");
                var recipe=FactoryCatalog.GetRecipe(entity.Recipe);
                if(recipe!=null&&!TechCatalog.Has(state,recipe.RequiredTech))throw new InvalidDataException("연구하지 않은 제조법입니다.");
                if(entity.ClockPercent>100&&!TechCatalog.Has(state,TechId.AdvancedManufacturing))throw new InvalidDataException("고급 제조 연구 없이 과클럭한 설비입니다.");
                if(entity.ControllerInstalled&&!TechCatalog.Has(state,TechId.IndustrialControl))throw new InvalidDataException("산업 제어 연구 없이 설치된 자동화 제어기입니다.");
            }
            if(state.OwnedRegions==null || !state.OwnedRegions.Contains(4)) throw new InvalidDataException("시작 구역이 없습니다.");
            var regions=new HashSet<int>();
            foreach(int id in state.OwnedRegions) if(id<0 || id>8 || !regions.Add(id)) throw new InvalidDataException("잘못된 구역입니다.");
            ValidateRegionConnectivity(regions);
            int townHalls=0;
            for(int i=0;i<state.Cells.Count;i++)
            {
                var cell=state.Cells[i];
                if(cell==null || cell.X!=i%ExpectedSize || cell.Z!=i/ExpectedSize || !Enum.IsDefined(typeof(BuildingKind),cell.Building) || !Enum.IsDefined(typeof(TerrainKind),cell.Terrain) || cell.Level<0 || cell.Level>3 || !FiniteNonnegative(cell.Progress) || (cell.Status != null && cell.Status.Length > 512)) throw new InvalidDataException("잘못된 타일입니다.");
                if(cell.Building!=BuildingKind.None && (cell.Level<1 || (cell.Terrain==TerrainKind.Water && cell.Building!=BuildingKind.Road) || !regions.Contains((cell.Z/7)*3+cell.X/7))) throw new InvalidDataException("건물 배치가 올바르지 않습니다.");
                if(cell.Building==BuildingKind.None && (cell.Level!=0 || cell.Progress!=0)) throw new InvalidDataException("빈 타일 데이터가 올바르지 않습니다.");
                if((cell.Building==BuildingKind.TownHall || cell.Building==BuildingKind.Road) && cell.Level!=1) throw new InvalidDataException("건물 레벨이 올바르지 않습니다.");
                if(cell.Building!=BuildingKind.None && !TechCatalog.Has(state,TechCatalog.RequiredTechnology(cell.Building))) throw new InvalidDataException("연구하지 않은 시설이 있습니다.");
                int upgradeCap=TechCatalog.Has(state,TechId.UrbanPlanning)?3:TechCatalog.Has(state,TechId.Stonecraft)?2:1;
                if(cell.Level>upgradeCap) throw new InvalidDataException("기술 한도를 넘은 건물 레벨입니다.");
                if(cell.Building==BuildingKind.TownHall) { townHalls++; if(cell.X!=10 || cell.Z!=10) throw new InvalidDataException("시청 위치가 올바르지 않습니다."); }
                ValidateCityBuffer(cell.LogisticsInput);ValidateCityBuffer(cell.LogisticsOutput);
            }
            if(townHalls!=1) throw new InvalidDataException("시청이 없습니다.");
            var environment=new CityLogistics(state);
            foreach(var entity in state.Factory.Entities)
            {
                if(entity.Floor==0&&!environment.CanPlace(entity.Kind,entity.X,entity.Z,entity.Direction,out var placement))throw new InvalidDataException(placement);
                var recipe=entity.Kind==FactoryKind.Drill&&entity.Recipe==FactoryRecipe.None?FactoryCatalog.GetRecipe(FactoryRecipe.IronMining):FactoryCatalog.GetRecipe(entity.Recipe);
                if(recipe!=null&&recipe.IsExtraction)
                {
                    bool source=recipe.SourceResource==Resource.Water?environment.HasWater(entity.X,entity.Z):environment.HasDeposit(recipe.SourceResource,entity.X,entity.Z);
                    if(!source)throw new InvalidDataException("추출 설비가 알맞은 자원 위에 있지 않습니다.");
                }
            }
            if(state.ArchivedFactory!=null&&!IsAbsentLegacyFactoryPlaceholder(state.ArchivedFactory))
            {
                if(frozenVersion5)FactoryStateValidation.ValidateVersion2(state.ArchivedFactory);else FactoryStateValidation.Validate(state.ArchivedFactory);
                ValidateFactoryTechnology(state,state.ArchivedFactory);
            }
            if(frozenVersion5)
            {
                if(state.CityProjects!=null&&state.CityProjects.Count!=0)throw new InvalidDataException("Version 5 city contains expansion projects.");
            }
            else
            {
                FactoryLayers.ValidateCity(state);
                CityProjects.Validate(state);
                foreach(var project in state.CityProjects)
                {
                    var projectSpec=project==null?null:CityProjects.Get(project.Kind);
                    if(projectSpec==null||!TechCatalog.Has(state,projectSpec.RequiredTech))throw new InvalidDataException("연구하지 않은 도시 프로젝트입니다.");
                }
                ValidateExpansionTechnology(state,state.Factory);
                if(state.ArchivedFactory!=null&&!IsAbsentLegacyFactoryPlaceholder(state.ArchivedFactory))ValidateExpansionTechnology(state,state.ArchivedFactory);
            }
        }

        static void ValidateExpansionTechnology(GameState game,FactoryState factory)
        {
            if(factory.Platforms.Any(platform=>platform.Floor==1)&&!TechCatalog.Has(game,TechId.MassProduction))throw new InvalidDataException("대량 생산 연구 없이 2층 플랫폼이 있습니다.");
            if(factory.Platforms.Any(platform=>platform.Floor==2)&&!TechCatalog.Has(game,TechId.AdvancedManufacturing))throw new InvalidDataException("고급 제조 연구 없이 3층 플랫폼이 있습니다.");
            if((factory.AutomationRules.Count>0||factory.Entities.Any(entity=>entity.ControllerInstalled))&&!TechCatalog.Has(game,TechId.IndustrialControl))throw new InvalidDataException("산업 제어 연구 없이 자동화 상태가 있습니다.");
        }

        static void ValidateFactoryTechnology(GameState game,FactoryState factory)
        {
            foreach(var entity in factory.Entities)
            {
                var spec=entity==null?null:FactoryCatalog.Get(entity.Kind);
                if(spec==null||!TechCatalog.Has(game,spec.RequiredTech))throw new InvalidDataException("연구하지 않은 보관 공장 설비입니다.");
                var recipe=FactoryCatalog.GetRecipe(entity.Recipe);
                if(recipe!=null&&!TechCatalog.Has(game,recipe.RequiredTech))throw new InvalidDataException("연구하지 않은 보관 공장 제조법입니다.");
                if(entity.ClockPercent>100&&!TechCatalog.Has(game,TechId.AdvancedManufacturing))throw new InvalidDataException("고급 제조 연구 없이 과클럭한 보관 설비입니다.");
                if(entity.ControllerInstalled&&!TechCatalog.Has(game,TechId.IndustrialControl))throw new InvalidDataException("산업 제어 연구 없이 설치된 보관 공장 제어기입니다.");
            }
        }

        static void NormalizeAbsentTutorial(GameState state)
        {
            // Unity's inline-class serializer can represent null as an all-default object.
            // Interpret only that exact empty shape as the legacy opt-in state on disk reads.
            // Validate/TrySave still reject malformed non-null progress supplied by callers.
            var progress=state?.Tutorial;
            if(progress!=null&&!progress.Enabled&&!progress.Completed&&!progress.Skipped&&!progress.Collapsed&&
               progress.Step==TutorialStepId.Welcome&&progress.RoadBaseline==0&&progress.HouseBaseline==0&&
               progress.LumberyardBaseline==0&&progress.StudyHouseBaseline==0&&progress.StartDay==0&&
               !progress.TownHallIntroPlayed&&!progress.GuidanceEnabled&&progress.GuidanceSeenMask==0)
                state.Tutorial=null;
        }
        static void ValidateCityBuffer(List<float> values)
        {
            if(values==null||values.Count!=ResourceCatalog.Count||values[0]!=0)throw new InvalidDataException("도시 물류 버퍼가 올바르지 않습니다.");
            float total=0;foreach(float value in values){if(!FiniteNonnegative(value))throw new InvalidDataException("도시 물류 수량이 올바르지 않습니다.");total+=value;}
            if(total>CityLogistics.BufferCapacity+.001f)throw new InvalidDataException("도시 물류 버퍼 용량 초과입니다.");
        }
        static void ValidateLegacyData(GameState state)
        {
            if(state.Size!=21||state.Cells==null||state.Cells.Count!=441||state.Stock==null||state.Stock.Count!=ResourceCatalog.LegacyCount||!FiniteNonnegative(state.Coins)||state.Population<0||state.Day<0)throw new InvalidDataException("이전 도시 데이터가 올바르지 않습니다.");
            if(state.CityProjects!=null&&state.CityProjects.Count!=0)throw new InvalidDataException("이전 도시에 확장 프로젝트가 포함되어 있습니다.");
            foreach(float value in state.Stock)if(!FiniteNonnegative(value))throw new InvalidDataException("이전 도시 재고가 올바르지 않습니다.");
            if(Math.Abs(state.Stock[0]-state.Coins)>.01f)throw new InvalidDataException("이전 도시 코인 값이 일치하지 않습니다.");
            if(state.OwnedRegions==null||!state.OwnedRegions.Contains(4))throw new InvalidDataException("이전 도시 영토가 없습니다.");
            var owned=new HashSet<int>();foreach(int id in state.OwnedRegions)if(id<0||id>8||!owned.Add(id))throw new InvalidDataException("이전 도시 영토가 올바르지 않습니다.");ValidateRegionConnectivity(owned);
            for(int i=0;i<441;i++)
            {
                var c=state.Cells[i];if(c==null||c.X!=i%21||c.Z!=i/21||!Enum.IsDefined(typeof(BuildingKind),c.Building)||!Enum.IsDefined(typeof(TerrainKind),c.Terrain)||c.Level<0||c.Level>3||!FiniteNonnegative(c.Progress))throw new InvalidDataException("이전 도시 타일이 올바르지 않습니다.");
                ValidateLegacyCityBuffer(c.LogisticsInput);ValidateLegacyCityBuffer(c.LogisticsOutput);
            }
            if(state.Version>=2)
            {
                if(state.Technologies!=null&&state.Technologies.Any(id=>(int)id>(int)TechId.Automation) || (int)state.ActiveResearch>(int)TechId.Automation)
                    throw new InvalidDataException("이전 도시에 새 산업 기술 ID가 포함되어 있습니다.");
                ValidateTechnology(state);
            }
            if(state.Version>=3)
            {
                if(state.Factory==null)throw new InvalidDataException("이전 공장 데이터가 없습니다.");
                FactoryStateValidation.ValidateLegacy(state.Factory);
                if(state.ArchivedFactory!=null&&!IsAbsentLegacyFactoryPlaceholder(state.ArchivedFactory))FactoryStateValidation.ValidateLegacy(state.ArchivedFactory);
            }
        }

        static void NormalizeAbsentLegacyFactories(GameState state)
        {
            // Unity's inline serializer writes a null class field back as a constructed empty
            // object. Recognize only the two exact empty shapes it can produce. This happens
            // after the legacy state has been checked read-only, so malformed real archives are
            // never repaired or discarded before validation.
            if(state!=null&&IsAbsentLegacyFactoryPlaceholder(state.ArchivedFactory))state.ArchivedFactory=null;
        }

        static bool IsAbsentLegacyFactoryPlaceholder(FactoryState factory)
        {
            if(factory==null)return true;
            bool noEntities=factory.Entities==null||factory.Entities.Count==0;
            bool serializerDefaults=factory.Version==0&&factory.Width==0&&factory.Height==0&&factory.NextEntityId==0&&factory.PowerBudget==0&&
                (factory.Produced==null||factory.Produced.Count==0)&&(factory.Exported==null||factory.Exported.Count==0)&&(factory.Recovered==null||factory.Recovered.Count==0)&&
                (factory.Platforms==null||factory.Platforms.Count==0)&&(factory.AutomationRules==null||factory.AutomationRules.Count==0)&&factory.NextAutomationRuleId==0;
            bool oldInitializedDefaults=factory.Version==2&&factory.Width==24&&factory.Height==16&&factory.NextEntityId==1&&factory.PowerBudget==20&&
                IsZeroInventory(factory.Produced,ResourceCatalog.Count)&&IsZeroInventory(factory.Exported,ResourceCatalog.Count)&&IsZeroInventory(factory.Recovered,ResourceCatalog.Count)&&
                (factory.Platforms==null||factory.Platforms.Count==0)&&(factory.AutomationRules==null||factory.AutomationRules.Count==0)&&(factory.NextAutomationRuleId==0||factory.NextAutomationRuleId==1);
            bool expansionInitializedDefaults=factory.Version==3&&factory.Width==24&&factory.Height==16&&factory.NextEntityId==1&&factory.PowerBudget==20&&
                IsZeroInventory(factory.Produced,ResourceCatalog.Count)&&IsZeroInventory(factory.Exported,ResourceCatalog.Count)&&IsZeroInventory(factory.Recovered,ResourceCatalog.Count)&&
                factory.Platforms!=null&&factory.Platforms.Count==0&&factory.AutomationRules!=null&&factory.AutomationRules.Count==0&&factory.NextAutomationRuleId==1;
            return noEntities&&factory.ElapsedSeconds==0&&(serializerDefaults||oldInitializedDefaults||expansionInitializedDefaults);
        }

        static bool IsZeroInventory(List<int> values,int width)
        {
            if(values==null||values.Count!=width)return false;
            foreach(int value in values)if(value!=0)return false;
            return true;
        }
        static void ValidateLegacyCityBuffer(List<float> values)
        {
            if(values==null||values.Count!=ResourceCatalog.LegacyCount||values[0]!=0)throw new InvalidDataException("이전 도시 물류 버퍼 너비가 올바르지 않습니다.");
            float total=0;foreach(float value in values){if(!FiniteNonnegative(value))throw new InvalidDataException("이전 도시 물류 버퍼가 올바르지 않습니다.");total+=value;}
            if(total>CityLogistics.BufferCapacity+.001f)throw new InvalidDataException("이전 도시 물류 버퍼 용량을 초과했습니다.");
        }
        static void ValidateTechnology(GameState state)
        {
            int catalogCount=0;
            foreach(var spec in TechCatalog.All)if(spec!=null&&spec.Id!=TechId.None)catalogCount++;
            if(!Enum.IsDefined(typeof(Era),state.Era) || !FiniteNonnegative(state.ResearchPoints) || !FiniteNonnegative(state.LastGrainConsumed) || state.Technologies==null || state.Technologies.Count>catalogCount) throw new InvalidDataException("연구 데이터가 올바르지 않습니다.");
            var completed=new HashSet<TechId>();
            foreach(var id in state.Technologies)
            {
                var spec=TechCatalog.Get(id);
                if(id==TechId.None || spec==null || !completed.Add(id)) throw new InvalidDataException("중복되거나 잘못된 기술입니다.");
            }
            foreach(var id in completed) foreach(var prerequisite in TechCatalog.Get(id).Prerequisites)
                if(!completed.Contains(prerequisite)) throw new InvalidDataException("선행 연구가 빠져 있습니다.");
            Era expected=completed.Contains(TechId.SteamPower)?Era.Industrial:completed.Contains(TechId.Guilds)?Era.Renaissance:Era.Medieval;
            if(state.Era!=expected) throw new InvalidDataException("시대와 연구 상태가 일치하지 않습니다.");
            if(state.ActiveResearch==TechId.None)
            {
                if(state.ResearchDaysRemaining!=0) throw new InvalidDataException("진행 중인 연구가 없습니다.");
            }
            else
            {
                var active=TechCatalog.Get(state.ActiveResearch);
                if(active==null || completed.Contains(state.ActiveResearch) || state.ResearchDaysRemaining<=0 || state.ResearchDaysRemaining>active.DurationDays) throw new InvalidDataException("연구 진행 시간이 올바르지 않습니다.");
                foreach(var prerequisite in active.Prerequisites) if(!completed.Contains(prerequisite)) throw new InvalidDataException("연구 선행 조건이 충족되지 않았습니다.");
            }
            if(state.Won && !completed.Contains(TechId.UrbanPlanning)) throw new InvalidDataException("완료되지 않은 시대 발전입니다.");
        }
        static void ValidateRegionConnectivity(HashSet<int> regions)
        {
            var reached = new HashSet<int> { 4 };
            var queue = new Queue<int>();
            queue.Enqueue(4);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int x = current % 3, z = current / 3;
                foreach (int candidate in regions)
                {
                    if (!reached.Contains(candidate) && Math.Abs(candidate % 3 - x) + Math.Abs(candidate / 3 - z) == 1)
                    { reached.Add(candidate); queue.Enqueue(candidate); }
                }
            }
            if (reached.Count != regions.Count) throw new InvalidDataException("소유 구역이 서로 연결되어 있지 않습니다.");
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool FiniteNonnegative(float value) => Finite(value) && value >= 0;
    }
}

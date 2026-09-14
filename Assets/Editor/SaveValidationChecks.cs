using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Riverworks.Editor
{
    public static class SaveValidationChecks
    {
        public static void Run()
        {
            var results=new List<string>();
            string folder=Path.GetFullPath("Artifacts/SaveValidation");Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"shared-"+Guid.NewGuid().ToString("N")+".json");
            var state=GameState.CreateNew();var sim=new Simulation(state);state.DayProgressSeconds=2.375f;
            Check(state.Version==6&&state.Stock.Count==ResourceCatalog.Count&&state.Factory.Width==42&&state.Factory.Height==42,"new city owns one shared factory grid with the current resource width");
            Check(sim.Build(BuildingKind.House,8,9,out _),"city construction");
            Check(sim.StartResearch(TechId.Stonecraft,out _),"research starts");while(state.ActiveResearch!=TechId.None)sim.Tick();
            Check(sim.Upgrade(8,9,out _),"researched upgrade");sim.BuyRegion(1,out _);
            RoundTrip(state,"city, research and partial day");
            string previous=File.ReadAllText(path);sim.Tick();Check(SaveStore.TrySave(path,state,out _),"atomic update");
            Check(File.ReadAllText(path+".bak")==previous,"backup preserves previous bytes");
            string current=File.ReadAllText(path);Check(SaveStore.TrySave(path,state,out _)&&SaveStore.TrySave(path,state,out _),"unchanged save succeeds");
            Check(File.ReadAllText(path)==current&&File.ReadAllText(path+".bak")==previous,"duplicate saves do not rotate backup");
            Reject(s=>s.Stock[5]=float.NaN,"nonfinite stock");Reject(s=>s.Coins+=1,"coin mirror mismatch");
            Reject(s=>s.DayProgressSeconds=5,"invalid partial day");Reject(s=>s.OwnedRegions.Add(4),"duplicate region");
            Reject(s=>s.Cells[0].X=1,"misindexed cell");Reject(s=>s.Technologies.Add(TechId.SteamPower),"missing research prerequisite");
            Reject(s=>s.Factory=null,"missing shared grid");Reject(s=>s.Factory.Width=24,"separate interior geometry rejected");
            Reject(s=>s.Cells[0].LogisticsInput[1]=float.PositiveInfinity,"nonfinite city logistics input");
            Reject(s=>s.Cells[0].LogisticsOutput[1]=81,"city output buffer capacity");
            string valid=File.ReadAllText(path);var invalid=Copy(state);invalid.Stock[1]=-1;
            Check(!SaveStore.TrySave(path,invalid,out _),"invalid writes fail");
            Check(File.ReadAllText(path)==valid,"invalid write preserves canonical bytes");
            File.WriteAllText(path,"{broken");Check(!SaveStore.TryLoad(path,out _,out _),"broken JSON rejected");
            Check(SaveStore.TryLoad(path+".bak",out _,out _),"backup remains readable");

            var active=GameState.CreateNew();new Simulation(active).StartResearch(TechId.CropRotation,out _);RoundTrip(active,"active research");
            var integrated=SharedCityScenario.Create();var city=new Simulation(integrated);
            var machines=new FactorySimulation(integrated.Factory,new CityLogistics(integrated));
            for(int i=0;i<300;i++){if(i%50==0)city.Tick();machines.InvalidateEnvironment();machines.Tick(.1f);}
            Check(integrated.Factory.Entities.Count>0,"machines exist on city");
            Check(integrated.Factory.Exported[(int)Resource.Flour]>0,"city farm-to-warehouse physical flow before save");
            Check(integrated.Cells.Any(c=>c.LogisticsInput.Sum()>0||c.LogisticsOutput.Sum()>0),"city logistics buffers hold real production");
            RoundTrip(integrated,"shared terrain, machines, city buffers and moving cargo");
            var loaded=lastLoaded;float exportedBefore=loaded.Factory.Exported[(int)Resource.Flour];
            var cityAfter=new Simulation(loaded);var resumed=new FactorySimulation(loaded.Factory,new CityLogistics(loaded));
            for(int i=0;i<300;i++){if(i%50==0)cityAfter.Tick();resumed.InvalidateEnvironment();resumed.Tick(.1f);}
            Check(loaded.Factory.Exported[(int)Resource.Flour]>exportedBefore,"shared-city production resumes after load");
            var collision=Copy(integrated);var machine=collision.Factory.Entities.First(e=>e.Kind==FactoryKind.Assembler);
            collision.Cells[(machine.Z/2)*21+machine.X/2].Building=BuildingKind.House;collision.Cells[(machine.Z/2)*21+machine.X/2].Level=1;
            Check(!SaveStore.TrySave(path,collision,out _),"city and machine collision rejected on save");

            var legacy=LegacyGame(3);legacy.Factory=LegacyFactory(FactoryState.CreateExample());
            legacy.Technologies=TechCatalog.All.Where(t=>(int)t.Id<=(int)TechId.Automation).Select(t=>t.Id).ToList();legacy.Era=Era.Industrial;
            float money=legacy.Coins,wood=legacy.Stock[1];int entities=legacy.Factory.Entities.Count;
            int constructionCoins=legacy.Factory.Entities.Sum(e=>FactoryCatalog.Get(e.Kind).CoinCost);
            int constructionWood=legacy.Factory.Entities.Sum(e=>FactoryCatalog.Get(e.Kind).TimberCost);
            int heldWood=legacy.Factory.Entities.Sum(e=>e.Input[1]+e.Output[1]+(e.CargoResource==Resource.Timber?1:0))+legacy.Factory.Recovered[1];
            string legacyText=JsonUtility.ToJson(legacy);File.WriteAllText(path,legacyText);
            Check(SaveStore.TryLoad(path,out var migrated,out var migrationError),"v3 migration: "+migrationError);
            Check(migrated.Version==6&&migrated.Stock.Count==ResourceCatalog.Count&&migrated.Factory.Version==3&&migrated.Factory.Entities.Count==0&&migrated.Factory.Width==42,"old interior replaced by shared city grid and expanded to current resources");
            Check(migrated.ArchivedFactory!=null&&migrated.ArchivedFactory.Entities.Count==entities,"old design retained as archive");
            Check(Math.Abs(migrated.Coins-money-constructionCoins)<.01f&&Math.Abs(migrated.Stock[1]-wood-constructionWood-heldWood)<.01f,"old construction and contents refunded exactly");
            Check(File.ReadAllText(path)==legacyText,"migration leaves original disk bytes unchanged");
            RoundTrip(migrated,"migrated archive");Check(lastLoaded.Coins==migrated.Coins,"migration refunds cannot repeat");
            foreach(int version in new[]{1,2})
            {
                var old=LegacyGame(version);old.Factory=null;if(version==1)old.Technologies=null;
                File.WriteAllText(path,JsonUtility.ToJson(old));
                Check(SaveStore.TryLoad(path,out var upgraded,out _)&&upgraded.Version==6&&upgraded.Stock.Count==ResourceCatalog.Count&&upgraded.Factory.Version==3&&upgraded.Factory.Width==42,"legacy v"+version+" migrates into same city");
            }

            // A real v4-shaped JSON fixture: every resource array is exactly nine entries before
            // loading. It exercises active and archived factories instead of relabeling a v5 DTO.
            var versionFour=LegacyGame(4);
            versionFour.Technologies=TechCatalog.All.Where(t=>(int)t.Id<=(int)TechId.Automation).Select(t=>t.Id).ToList();
            versionFour.Era=Era.Industrial;
            ClearLot(versionFour,7,7);ClearLot(versionFour,8,7);ClearLot(versionFour,9,7);
            Cell oldMill=versionFour.Cells[7*versionFour.Size+9];oldMill.Building=BuildingKind.Mill;oldMill.Level=1;
            oldMill.LogisticsInput[(int)Resource.Grain]=1.25f;oldMill.LogisticsOutput[(int)Resource.Flour]=2.75f;
            var oldActive=FactoryState.CreateCityGrid();oldActive.Entities.Clear();
            oldActive.Entities.Add(new FactoryEntity{Id=1,Kind=FactoryKind.Belt,X=14,Z=14,CargoResource=Resource.Tools,CargoProgress=.625f});
            oldActive.Entities.Add(new FactoryEntity{Id=2,Kind=FactoryKind.Inserter,X=15,Z=14,Filter=Resource.Bread});
            var oldFurnace=new FactoryEntity{Id=3,Kind=FactoryKind.Furnace,Recipe=FactoryRecipe.IronPlate,X=16,Z=14,Progress=1.375f};
            oldFurnace.Input[(int)Resource.Ore]=2;oldFurnace.Output[(int)Resource.Steel]=1;oldActive.Entities.Add(oldFurnace);
            oldActive.NextEntityId=4;oldActive.Produced[(int)Resource.Steel]=13;oldActive.Exported[(int)Resource.Tools]=7;oldActive.Recovered[(int)Resource.Bread]=5;oldActive.ElapsedSeconds=93.125f;
            versionFour.Factory=LegacyFactory(oldActive);
            var oldArchive=FactoryState.CreateEmpty();oldArchive.Entities.Clear();
            var archivedFurnace=new FactoryEntity{Id=1,Kind=FactoryKind.Furnace,Recipe=FactoryRecipe.IronPlate,X=2,Z=2,Progress=2.25f};
            archivedFurnace.Input[(int)Resource.Ore]=4;archivedFurnace.Output[(int)Resource.Steel]=2;oldArchive.Entities.Add(archivedFurnace);
            oldArchive.NextEntityId=2;oldArchive.Produced[(int)Resource.Ore]=19;oldArchive.Exported[(int)Resource.Steel]=3;oldArchive.Recovered[(int)Resource.Tools]=2;oldArchive.ElapsedSeconds=41.5f;
            versionFour.ArchivedFactory=LegacyFactory(oldArchive);
            string versionFourObject=JsonUtility.ToJson(versionFour);
            Check(versionFour.Stock.Count==ResourceCatalog.LegacyCount&&versionFour.Cells.All(c=>c.LogisticsInput.Count==ResourceCatalog.LegacyCount&&c.LogisticsOutput.Count==ResourceCatalog.LegacyCount)&&
                  versionFour.Factory.Entities.All(e=>e.Input.Count==ResourceCatalog.LegacyCount&&e.Output.Count==ResourceCatalog.LegacyCount)&&
                  versionFour.ArchivedFactory.Entities.All(e=>e.Input.Count==ResourceCatalog.LegacyCount&&e.Output.Count==ResourceCatalog.LegacyCount),"v4 fixture is truly exact-width legacy data");
            File.WriteAllText(path,versionFourObject);string versionFourBytes=File.ReadAllText(path);
            Check(SaveStore.TryLoad(path,out var upgradedFour,out var versionFourError),"actual v4 JSON migration: "+versionFourError);
            Check(File.ReadAllText(path)==versionFourBytes&&JsonUtility.ToJson(versionFour)==versionFourObject,"v4 load validates before mutation and leaves source bytes and fixture unchanged");
            Check(upgradedFour.Version==6&&upgradedFour.Stock.Count==ResourceCatalog.Count&&upgradedFour.Factory.Version==3&&upgradedFour.ArchivedFactory.Version==3,"v4 active and archived factories upgrade through frozen v5 to current schema");
            Check(upgradedFour.Technologies.Count==18&&upgradedFour.Technologies.All(t=>(int)t<=(int)TechId.Automation),"v4 migration preserves the original 18 technologies without granting new industry research");
            Check(upgradedFour.Cells[7*upgradedFour.Size+9].Building==BuildingKind.Mill&&Math.Abs(upgradedFour.Cells[7*upgradedFour.Size+9].LogisticsInput[(int)Resource.Grain]-1.25f)<.0001f&&
                  Math.Abs(upgradedFour.Cells[7*upgradedFour.Size+9].LogisticsOutput[(int)Resource.Flour]-2.75f)<.0001f&&
                  upgradedFour.Factory.Entities.First(e=>e.Kind==FactoryKind.Belt).CargoResource==Resource.Tools&&
                  Math.Abs(upgradedFour.Factory.Entities.First(e=>e.Kind==FactoryKind.Belt).CargoProgress-.625f)<.0001f&&
                  upgradedFour.Factory.Entities.First(e=>e.Kind==FactoryKind.Inserter).Filter==Resource.Bread&&
                  upgradedFour.Factory.Entities.First(e=>e.Kind==FactoryKind.Furnace).Input[(int)Resource.Ore]==2&&
                  upgradedFour.Factory.Entities.First(e=>e.Kind==FactoryKind.Furnace).Output[(int)Resource.Steel]==1&&
                  Math.Abs(upgradedFour.Factory.Entities.First(e=>e.Kind==FactoryKind.Furnace).Progress-1.375f)<.0001f,"v4 fractional buffers, cargo, filter and active producer state are preserved");
            Check(upgradedFour.Factory.Produced[(int)Resource.Steel]==13&&upgradedFour.Factory.Exported[(int)Resource.Tools]==7&&upgradedFour.Factory.Recovered[(int)Resource.Bread]==5&&
                  upgradedFour.ArchivedFactory.Produced[(int)Resource.Ore]==19&&upgradedFour.ArchivedFactory.Exported[(int)Resource.Steel]==3&&upgradedFour.ArchivedFactory.Recovered[(int)Resource.Tools]==2&&
                  upgradedFour.ArchivedFactory.Entities[0].Input[(int)Resource.Ore]==4&&upgradedFour.ArchivedFactory.Entities[0].Output[(int)Resource.Steel]==2&&
                  Math.Abs(upgradedFour.ArchivedFactory.Entities[0].Progress-2.25f)<.0001f,"v4 aggregate counters and archived inventory/progress are preserved");
            Check(upgradedFour.Stock.Skip(ResourceCatalog.LegacyCount).All(v=>v==0)&&upgradedFour.Factory.Produced.Skip(ResourceCatalog.LegacyCount).All(v=>v==0)&&
                  upgradedFour.ArchivedFactory.Recovered.Skip(ResourceCatalog.LegacyCount).All(v=>v==0),"v4 migration appends zeroes for all new resources");

            void RejectVersionFourJson(Action<GameState> mutate,string label)
            {
                var malformed=Copy(versionFour);mutate(malformed);string fixtureBefore=JsonUtility.ToJson(malformed);File.WriteAllText(path,fixtureBefore);string bytesBefore=File.ReadAllText(path);
                Check(!SaveStore.TryLoad(path,out _,out _)&&File.ReadAllText(path)==bytesBefore&&JsonUtility.ToJson(malformed)==fixtureBefore,label);
            }
            RejectVersionFourJson(s=>s.Factory.Entities[0].Input.RemoveAt(0),"v4 nested eight-entry array rejects without mutation");
            RejectVersionFourJson(s=>s.ArchivedFactory.Entities[0].Output.Add(0),"v4 nested ten-entry array rejects without mutation");
            RejectVersionFourJson(s=>s.Factory.Entities[2].Input[(int)Resource.Ore]=-1,"v4 negative nested inventory rejects without mutation");
            RejectVersionFourJson(s=>s.Factory.Entities[0].CargoProgress=float.NaN,"v4 nonfinite progress rejects without mutation");
            RejectVersionFourJson(s=>s.Factory.Entities[0].CargoResource=(Resource)ResourceCatalog.LegacyCount,"v4 new resource id rejects as unknown legacy cargo");
            RejectVersionFourJson(s=>s.Factory.Entities[1].Filter=(Resource)ResourceCatalog.LegacyCount,"v4 new resource id rejects as unknown legacy filter");

            var placeholderThree=LegacyGame(3);placeholderThree.Factory=LegacyFactory(FactoryState.CreateEmpty());placeholderThree.ArchivedFactory=null;
            var corruptThreeArchive=Copy(placeholderThree);
            Check(corruptThreeArchive.ArchivedFactory!=null,"Unity JSON materializes the null v3 archive placeholder");
            corruptThreeArchive.ArchivedFactory.Produced[(int)Resource.Timber]=1;
            string corruptThreeJson=JsonUtility.ToJson(corruptThreeArchive);File.WriteAllText(path,corruptThreeJson);
            Check(!SaveStore.TryLoad(path,out _,out _)&&File.ReadAllText(path)==corruptThreeJson,"v3 archive placeholder with a produced value is rejected");
            var placeholderFour=LegacyGame(4);placeholderFour.Factory=LegacyFactory(FactoryState.CreateCityGrid());placeholderFour.ArchivedFactory=null;
            var corruptFourArchive=Copy(placeholderFour);
            Check(corruptFourArchive.ArchivedFactory!=null,"Unity JSON materializes the null v4 archive placeholder");
            corruptFourArchive.ArchivedFactory.Width=25;
            string corruptFourJson=JsonUtility.ToJson(corruptFourArchive);File.WriteAllText(path,corruptFourJson);
            Check(!SaveStore.TryLoad(path,out _,out _)&&File.ReadAllText(path)==corruptFourJson,"v4 archive placeholder with changed geometry is rejected");

            var highIds=GameState.CreateNew();highIds.Technologies=TechCatalog.All.Select(t=>t.Id).ToList();highIds.Era=Era.Industrial;
            ClearLot(highIds,7,7);ClearLot(highIds,8,7);ClearLot(highIds,7,8);
            Cell highWarehouse=highIds.Cells[8*highIds.Size+7];highWarehouse.Building=BuildingKind.Warehouse;highWarehouse.Level=1;highWarehouse.Connected=true;
            highWarehouse.LogisticsInput[(int)Resource.AluminumCasing]=1.5f;
            highIds.Stock[(int)Resource.Fuel]=2.5f;
            highIds.Factory=FactoryState.CreateCityGrid();
            var highStorage=new FactoryEntity{Id=1,Kind=FactoryKind.Storage,X=14,Z=14};highStorage.Input[(int)Resource.AluminumCasing]=4;
            var highCargo=new FactoryEntity{Id=2,Kind=FactoryKind.Belt,X=15,Z=14,CargoResource=Resource.AluminumCasing,CargoProgress=.45f};
            var highTank=new FactoryEntity{Id=3,Kind=FactoryKind.FluidTank,X=16,Z=14,Filter=Resource.Fuel};highTank.Input[(int)Resource.Fuel]=21;
            highIds.Factory.Entities.Add(highStorage);highIds.Factory.Entities.Add(highCargo);highIds.Factory.Entities.Add(highTank);highIds.Factory.NextEntityId=4;
            highIds.Factory.Produced[(int)Resource.AluminumCasing]=12;highIds.Factory.Recovered[(int)Resource.Fuel]=3;
            FreezeVersion5(highIds);string highIdsObject=JsonUtility.ToJson(highIds);string highIdsSource=StripExpansionFields(highIdsObject);File.WriteAllText(path,highIdsSource);
            Check(!highIdsSource.Contains("\"Floor\"")&&!highIdsSource.Contains("\"Platforms\"")&&!highIdsSource.Contains("\"AutomationRules\"")&&!highIdsSource.Contains("\"NextAutomationRuleId\"")&&!highIdsSource.Contains("\"CityProjects\""),"actual v5 JSON omits every v0.9 field");
            Check(SaveStore.TryLoad(path,out lastLoaded,out var highIdsError)&&lastLoaded.Version==6&&File.ReadAllText(path)==highIdsSource&&JsonUtility.ToJson(highIds)==highIdsObject,"actual v5 highest-id JSON upgrades without mutating source: "+highIdsError);
            Check(lastLoaded.Factory.Entities.First(e=>e.Kind==FactoryKind.Storage).Input[(int)Resource.AluminumCasing]==4&&
                  lastLoaded.Factory.Entities.First(e=>e.Kind==FactoryKind.Belt).CargoResource==Resource.AluminumCasing&&
                  Math.Abs(lastLoaded.Factory.Entities.First(e=>e.Kind==FactoryKind.Belt).CargoProgress-.45f)<.0001f&&
                  lastLoaded.Factory.Entities.First(e=>e.Kind==FactoryKind.FluidTank).Input[(int)Resource.Fuel]==21&&
                  lastLoaded.Factory.Recovered[(int)Resource.Fuel]==3&&Math.Abs(lastLoaded.Stock[(int)Resource.Fuel]-2.5f)<.0001f&&
                  Math.Abs(lastLoaded.Cells[8*lastLoaded.Size+7].LogisticsInput[(int)Resource.AluminumCasing]-1.5f)<.0001f,"v5 high-id values are restored exactly");
            void RejectVersionFiveJson(Action<GameState> mutate,string label)
            {
                var malformed=Copy(highIds);mutate(malformed);string malformedJson=JsonUtility.ToJson(malformed);File.WriteAllText(path,malformedJson);
                Check(!SaveStore.TryLoad(path,out _,out _)&&File.ReadAllText(path)==malformedJson,label);
            }
            RejectVersionFiveJson(s=>s.Factory.Entities[0].Input.RemoveAt(s.Factory.Entities[0].Input.Count-1),"v5 37-entry nested array is rejected without changing disk bytes");
            RejectVersionFiveJson(s=>s.Factory.Entities[0].Input.Add(0),"v5 39-entry nested array is rejected without changing disk bytes");
            RejectVersionFiveJson(s=>s.Factory.Entities[0].Floor=1,"v5 nonzero floor field is rejected");
            RejectVersionFiveJson(s=>s.Factory.Entities[0].LinkId=2,"v5 link field is rejected");
            RejectVersionFiveJson(s=>s.Factory.AutomationRules=new List<AutomationRule>{new AutomationRule{Id=1,SourceEntityId=0,TargetEntityId=1,Resource=Resource.Tools,Threshold=1}},"v5 automation collection is rejected");
            RejectVersionFiveJson(s=>s.CityProjects=new List<CityProjectState>{new CityProjectState{Kind=CityProjectKind.ResearchCampus,X=7,Z=7}},"v5 city project collection is rejected");

            var lockedClock=GameState.CreateNew();
            lockedClock.Technologies=TechCatalog.All.Where(t=>(int)t.Id<=(int)TechId.Automation).Select(t=>t.Id).ToList();lockedClock.Era=Era.Industrial;
            ClearLot(lockedClock,7,7);lockedClock.Factory=FactoryState.CreateCityGrid();
            lockedClock.Factory.Entities.Add(new FactoryEntity{Id=1,Kind=FactoryKind.Furnace,Recipe=FactoryRecipe.IronPlate,X=14,Z=14,ClockPercent=150});lockedClock.Factory.NextEntityId=2;
            FreezeVersion5(lockedClock);
            string lockedClockJson=JsonUtility.ToJson(lockedClock);File.WriteAllText(path,lockedClockJson);
            Check(!SaveStore.TryLoad(path,out _,out _)&&File.ReadAllText(path)==lockedClockJson,"v5 overclock without Advanced Manufacturing is rejected from actual JSON");
            lockedClock.Factory.Entities[0].ClockPercent=100;
            string baseClockJson=JsonUtility.ToJson(lockedClock);File.WriteAllText(path,baseClockJson);
            Check(SaveStore.TryLoad(path,out var baseClockLoaded,out var baseClockError)&&baseClockLoaded.Factory.Entities[0].ClockPercent==100,"same v5 factory at base clock remains valid: "+baseClockError);

            var expansion=GameState.CreateNew();expansion.Technologies=TechCatalog.All.Select(t=>t.Id).ToList();expansion.Era=Era.Industrial;
            ClearLot(expansion,7,7);ClearLot(expansion,7,9);ClearLot(expansion,8,9);ClearLot(expansion,7,10);ClearLot(expansion,8,10);
            expansion.Factory=FactoryState.CreateCityGrid();
            expansion.Factory.Platforms.Add(new FactoryPlatform{X=14,Z=14,Floor=1});
            expansion.Factory.Platforms.Add(new FactoryPlatform{X=14,Z=14,Floor=2});
            var liftSend=new FactoryEntity{Id=1,Kind=FactoryKind.ItemLift,X=14,Z=14,Floor=0,LinkId=2,IsLinkSender=true,ControllerInstalled=true,CargoResource=Resource.AluminumCasing,CargoProgress=.4f,ClockPercent=100,AutomationBlocked=true};
            var liftReceive=new FactoryEntity{Id=2,Kind=FactoryKind.ItemLift,X=14,Z=14,Floor=1,LinkId=1,IsLinkSender=false,ClockPercent=100};
            var riserSend=new FactoryEntity{Id=3,Kind=FactoryKind.FluidRiser,X=15,Z=14,Floor=1,LinkId=4,IsLinkSender=true,Filter=Resource.Fuel,ClockPercent=100};riserSend.Input[(int)Resource.Fuel]=12;
            var riserReceive=new FactoryEntity{Id=4,Kind=FactoryKind.FluidRiser,X=15,Z=14,Floor=2,LinkId=3,IsLinkSender=false,Filter=Resource.Fuel,ClockPercent=100};
            expansion.Factory.Entities.Add(liftSend);expansion.Factory.Entities.Add(liftReceive);expansion.Factory.Entities.Add(riserSend);expansion.Factory.Entities.Add(riserReceive);expansion.Factory.NextEntityId=5;
            expansion.Factory.AutomationRules.Add(new AutomationRule{Id=1,SourceEntityId=0,TargetEntityId=1,Resource=Resource.AluminumCasing,Comparison=AutomationComparison.AtLeast,Action=AutomationAction.AllowWhenTrue,Threshold=4,Enabled=true});
            expansion.Factory.NextAutomationRuleId=2;
            expansion.CityProjects.Add(new CityProjectState{Kind=CityProjectKind.ResearchCampus,X=7,Z=9,Stage=0,DaysRemaining=0,StartedDay=expansion.Day,CompletedDay=-1});
            RoundTrip(expansion,"v6 floors, reciprocal item/fluid links, automation and city project");
            Check(lastLoaded.Factory.Platforms.Count==2&&lastLoaded.Factory.Entities.Count==4&&lastLoaded.Factory.AutomationRules.Count==1&&lastLoaded.CityProjects.Count==1&&
                  lastLoaded.Factory.Entities.First(e=>e.Id==1).CargoResource==Resource.AluminumCasing&&Math.Abs(lastLoaded.Factory.Entities.First(e=>e.Id==1).CargoProgress-.4f)<.0001f&&
                  lastLoaded.Factory.Entities.First(e=>e.Id==3).Input[(int)Resource.Fuel]==12&&!lastLoaded.Factory.Entities.Any(e=>e.AutomationBlocked),
                  "v6 restores structural, cargo, fluid, rule and project state while transient automation flags do not persist");

            var guided=GameState.CreateNew();
            Check(guided.Tutorial!=null&&guided.Tutorial.Enabled&&guided.Tutorial.GuidanceEnabled,"fresh city enables beginner and feature guidance");
            guided.Tutorial.Step=TutorialStepId.BuildHouse;guided.Tutorial.Collapsed=true;
            guided.Tutorial.TownHallIntroPlayed=true;guided.Tutorial.GuidanceSeenMask=1;
            RoundTrip(guided,"tutorial step, intro and read guidance");
            Check(lastLoaded.Tutorial.Step==TutorialStepId.BuildHouse&&lastLoaded.Tutorial.TownHallIntroPlayed&&lastLoaded.Tutorial.GuidanceSeenMask==1,"tutorial state is restored exactly");
            guided.Tutorial.Skip();RoundTrip(guided,"skipped tutorial");
            guided.Tutorial=null;RoundTrip(guided,"legacy tutorial null");
            Check(lastLoaded.Tutorial==null,"legacy tutorial stays opt-in after round-trip");
            string noTutorialField=System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(guided),",\"Tutorial\":(?:null|\\{[^{}]*\\})","");
            Check(!noTutorialField.Contains("\"Tutorial\""),"legacy fixture truly omits the tutorial field");
            File.WriteAllText(path,noTutorialField);
            Check(SaveStore.TryLoad(path,out var oldWithoutTutorial,out _)&&oldWithoutTutorial.Tutorial==null,"save without tutorial field remains quiet");
            Reject(s=>s.Tutorial.Step=(TutorialStepId)999,"unknown tutorial step rejected");
            Reject(s=>s.Tutorial.GuidanceSeenMask=1<<29,"unknown guidance bits rejected");
            var invalidGuide=Copy(state);invalidGuide.Tutorial.RoadBaseline=-1;invalidGuide.Cells[0].LogisticsInput=null;
            string invalidBefore=JsonUtility.ToJson(invalidGuide);
            Check(!SaveStore.TrySave(path,invalidGuide,out _)&&JsonUtility.ToJson(invalidGuide)==invalidBefore,"invalid tutorial validation leaves optional buffers unchanged");
            File.WriteAllLines("Artifacts/save-validation-results.txt",results);
            Debug.Log("RIVERWORKS_SAVE_VALIDATION_PASS "+results.Count);

            void Check(bool okay,string message){if(!okay)throw new Exception("Save validation failed: "+message);results.Add("PASS "+message);}
            GameState Copy(GameState value)=>JsonUtility.FromJson<GameState>(JsonUtility.ToJson(value));
            void Reject(Action<GameState> mutate,string message){var bad=Copy(state);mutate(bad);Check(!SaveStore.TrySave(path,bad,out _),message);}
            void RoundTrip(GameState value,string label)
            {
                string expected=JsonUtility.ToJson(value);
                Check(SaveStore.TrySave(path,value,out var error),"save "+label+": "+error);
                Check(SaveStore.TryLoad(path,out lastLoaded,out error),"load "+label+": "+error);
                Check(JsonUtility.ToJson(lastLoaded)==expected,"exact round-trip "+label);
            }
        }
        static GameState lastLoaded;

        static void ClearLot(GameState state,int x,int z)
        {
            Cell cell=state.Cells[z*state.Size+x];cell.Terrain=TerrainKind.Grass;cell.Building=BuildingKind.None;cell.Level=0;cell.Progress=0;cell.Status="";
        }

        static GameState LegacyGame(int version)
        {
            var state=GameState.CreateNew();state.Version=version;
            state.Stock.RemoveRange(ResourceCatalog.LegacyCount,state.Stock.Count-ResourceCatalog.LegacyCount);
            foreach(var cell in state.Cells)
            {
                cell.LogisticsInput.RemoveRange(ResourceCatalog.LegacyCount,cell.LogisticsInput.Count-ResourceCatalog.LegacyCount);
                cell.LogisticsOutput.RemoveRange(ResourceCatalog.LegacyCount,cell.LogisticsOutput.Count-ResourceCatalog.LegacyCount);
            }
            state.Factory=LegacyFactory(state.Factory);
            state.ArchivedFactory=state.ArchivedFactory==null?null:LegacyFactory(state.ArchivedFactory);
            return state;
        }

        static FactoryState LegacyFactory(FactoryState state)
        {
            state.Version=1;
            state.Produced.RemoveRange(ResourceCatalog.LegacyCount,state.Produced.Count-ResourceCatalog.LegacyCount);
            state.Exported.RemoveRange(ResourceCatalog.LegacyCount,state.Exported.Count-ResourceCatalog.LegacyCount);
            state.Recovered.RemoveRange(ResourceCatalog.LegacyCount,state.Recovered.Count-ResourceCatalog.LegacyCount);
            foreach(var entity in state.Entities)
            {
                entity.Input.RemoveRange(ResourceCatalog.LegacyCount,entity.Input.Count-ResourceCatalog.LegacyCount);
                entity.Output.RemoveRange(ResourceCatalog.LegacyCount,entity.Output.Count-ResourceCatalog.LegacyCount);
                entity.Paused=false;entity.ClockPercent=100;entity.FluidProgress=0;entity.FluidCursor=0;
            }
            return state;
        }

        static void FreezeVersion5(GameState state)
        {
            state.Version=5;state.CityProjects=null;
            FreezeFactoryVersion2(state.Factory);
            if(state.ArchivedFactory!=null)FreezeFactoryVersion2(state.ArchivedFactory);
        }

        static void FreezeFactoryVersion2(FactoryState state)
        {
            state.Version=2;state.Platforms=null;state.AutomationRules=null;state.NextAutomationRuleId=0;
            foreach(var entity in state.Entities)
            {
                entity.Floor=0;entity.LinkId=0;entity.IsLinkSender=false;entity.ControllerInstalled=false;entity.AutomationBlocked=false;
            }
        }

        static string StripExpansionFields(string json)
        {
            string result=System.Text.RegularExpressions.Regex.Replace(json,",\"(?:Floor|LinkId)\":0","");
            result=System.Text.RegularExpressions.Regex.Replace(result,",\"(?:IsLinkSender|ControllerInstalled)\":false","");
            result=System.Text.RegularExpressions.Regex.Replace(result,",\"(?:Platforms|AutomationRules|CityProjects)\":(?:null|\\[\\])","");
            result=System.Text.RegularExpressions.Regex.Replace(result,",\"NextAutomationRuleId\":[01]","");
            return result;
        }
    }
}

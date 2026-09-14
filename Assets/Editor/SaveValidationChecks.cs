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
            Check(state.Version==4&&state.Factory.Width==42&&state.Factory.Height==42,"new city owns one shared factory grid");
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

            var legacy=GameState.CreateNew();legacy.Version=3;legacy.Factory=FactoryState.CreateExample();
            legacy.Technologies=TechCatalog.All.Select(t=>t.Id).ToList();legacy.Era=Era.Industrial;
            float money=legacy.Coins,wood=legacy.Stock[1];int entities=legacy.Factory.Entities.Count;
            int constructionCoins=legacy.Factory.Entities.Sum(e=>FactoryCatalog.Get(e.Kind).CoinCost);
            int constructionWood=legacy.Factory.Entities.Sum(e=>FactoryCatalog.Get(e.Kind).TimberCost);
            int heldWood=legacy.Factory.Entities.Sum(e=>e.Input[1]+e.Output[1]+(e.CargoResource==Resource.Timber?1:0))+legacy.Factory.Recovered[1];
            string legacyText=JsonUtility.ToJson(legacy);File.WriteAllText(path,legacyText);
            Check(SaveStore.TryLoad(path,out var migrated,out var migrationError),"v3 migration: "+migrationError);
            Check(migrated.Version==4&&migrated.Factory.Entities.Count==0&&migrated.Factory.Width==42,"old interior replaced by shared city grid");
            Check(migrated.ArchivedFactory!=null&&migrated.ArchivedFactory.Entities.Count==entities,"old design retained as archive");
            Check(Math.Abs(migrated.Coins-money-constructionCoins)<.01f&&Math.Abs(migrated.Stock[1]-wood-constructionWood-heldWood)<.01f,"old construction and contents refunded exactly");
            Check(File.ReadAllText(path)==legacyText,"migration leaves original disk bytes unchanged");
            RoundTrip(migrated,"migrated archive");Check(lastLoaded.Coins==migrated.Coins,"migration refunds cannot repeat");
            foreach(int version in new[]{1,2})
            {
                var old=GameState.CreateNew();old.Version=version;old.Factory=null;if(version==1)old.Technologies=null;
                File.WriteAllText(path,JsonUtility.ToJson(old));
                Check(SaveStore.TryLoad(path,out var upgraded,out _)&&upgraded.Version==4&&upgraded.Factory.Width==42,"legacy v"+version+" migrates into same city");
            }
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
    }
}

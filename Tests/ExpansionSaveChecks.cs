using System;
using System.Collections.Generic;
using System.IO;
using Riverworks;

public static class ExpansionSaveChecks
{
    static int passed;

    public static int Run()
    {
        passed=0;
        FrozenVersionFiveUpgradesWithoutLosingState();
        VersionTwoRejectsExpansionFields();
        CurrentFloorsAndLinksValidateStrictly();
        AutomationAndProjectsRejectMalformedState();
        return passed;
    }

    static void FrozenVersionFiveUpgradesWithoutLosingState()
    {
        GameState game=GameState.CreateNew();game.Version=5;game.CityProjects=null;
        game.Factory=FrozenFactory(42,42);game.ArchivedFactory=FrozenFactory(24,16);
        var storage=new FactoryEntity{Id=1,Kind=FactoryKind.Storage,X=2,Z=2,Paused=true,CargoResource=Resource.Coins,CargoProgress=0,ClockPercent=100};
        storage.Input[(int)Resource.AluminumCasing]=7;storage.FluidProgress=.375f;storage.FluidCursor=2;
        game.Factory.Entities.Add(storage);game.Factory.NextEntityId=2;game.Factory.Produced[(int)Resource.ControlUnit]=5;game.Factory.Recovered[(int)Resource.Fuel]=9;game.Factory.ElapsedSeconds=81.25f;
        var archived=new FactoryEntity{Id=1,Kind=FactoryKind.Belt,X=1,Z=1,CargoResource=Resource.Tools,CargoProgress=.625f,ClockPercent=100};
        game.ArchivedFactory.Entities.Add(archived);game.ArchivedFactory.NextEntityId=2;game.ArchivedFactory.Exported[(int)Resource.Tools]=4;

        ExpansionMigration.Upgrade(game);
        True(game.Version==6&&game.Factory.Version==3&&game.ArchivedFactory.Version==3,"v5 game and both v2 factories upgrade to current versions");
        True(storage.Paused&&!storage.AutomationBlocked&&storage.Floor==0&&storage.LinkId==0&&!storage.IsLinkSender&&!storage.ControllerInstalled,"manual pause survives while new entity fields receive safe defaults");
        True(storage.Input[(int)Resource.AluminumCasing]==7&&Math.Abs(storage.FluidProgress-.375f)<.0001f&&storage.FluidCursor==2&&
             game.Factory.Produced[(int)Resource.ControlUnit]==5&&game.Factory.Recovered[(int)Resource.Fuel]==9&&Math.Abs(game.Factory.ElapsedSeconds-81.25f)<.0001f&&
             archived.CargoResource==Resource.Tools&&Math.Abs(archived.CargoProgress-.625f)<.0001f&&game.ArchivedFactory.Exported[(int)Resource.Tools]==4,
            "v5 inventories, aggregate counts, fluid credit, cargo and elapsed time are preserved");
        True(game.CityProjects!=null&&game.CityProjects.Count==0&&game.Factory.Platforms.Count==0&&game.Factory.AutomationRules.Count==0&&game.Factory.NextAutomationRuleId==1,
            "migration grants no platforms, projects, rules or controllers");
    }

    static void VersionTwoRejectsExpansionFields()
    {
        FactoryState state=FrozenFactory(42,42);
        var entity=new FactoryEntity{Id=1,Kind=FactoryKind.Belt,X=1,Z=1,ClockPercent=100};state.Entities.Add(entity);state.NextEntityId=2;
        FactoryStateValidation.ValidateVersion2(state);
        entity.Floor=1;True(Throws(()=>FactoryStateValidation.ValidateVersion2(state)),"frozen v2 rejects a nonzero floor");entity.Floor=0;
        entity.LinkId=2;True(Throws(()=>FactoryStateValidation.ValidateVersion2(state)),"frozen v2 rejects link metadata");entity.LinkId=0;
        entity.ControllerInstalled=true;True(Throws(()=>FactoryStateValidation.ValidateVersion2(state)),"frozen v2 rejects controller hardware");entity.ControllerInstalled=false;
        state.Platforms=new List<FactoryPlatform>{new FactoryPlatform{X=2,Z=2,Floor=1}};
        True(Throws(()=>FactoryStateValidation.ValidateVersion2(state)),"frozen v2 rejects platform collections");
    }

    static void CurrentFloorsAndLinksValidateStrictly()
    {
        FactoryState state=FactoryState.CreateCityGrid();
        state.Platforms.Add(new FactoryPlatform{X=2,Z=2,Floor=1});
        var sender=new FactoryEntity{Id=1,Kind=FactoryKind.ItemLift,X=2,Z=2,Floor=0,LinkId=2,IsLinkSender=true,ClockPercent=100};
        var receiver=new FactoryEntity{Id=2,Kind=FactoryKind.ItemLift,X=2,Z=2,Floor=1,LinkId=1,IsLinkSender=false,ClockPercent=100};
        state.Entities.Add(sender);state.Entities.Add(receiver);state.NextEntityId=3;
        FactoryStateValidation.Validate(state);
        True(true,"reciprocal endpoints at identical coordinates on adjacent floors validate");
        receiver.Floor=3;True(Throws(()=>FactoryStateValidation.Validate(state)),"entity floors above the maximum reject");receiver.Floor=1;
        receiver.LinkId=2;True(Throws(()=>FactoryStateValidation.Validate(state)),"nonreciprocal vertical links reject");receiver.LinkId=1;
        receiver.IsLinkSender=true;True(Throws(()=>FactoryStateValidation.Validate(state)),"vertical link requires opposite sender roles");receiver.IsLinkSender=false;
        sender.LinkId=0;True(Throws(()=>FactoryStateValidation.Validate(state)),"unpaired vertical endpoint rejects");
    }

    static void AutomationAndProjectsRejectMalformedState()
    {
        FactoryState factory=FactoryState.CreateCityGrid();
        var storage=new FactoryEntity{Id=1,Kind=FactoryKind.Storage,X=2,Z=2,ClockPercent=100,ControllerInstalled=true};factory.Entities.Add(storage);factory.NextEntityId=2;
        factory.AutomationRules.Add(new AutomationRule{Id=1,SourceEntityId=0,TargetEntityId=1,Resource=Resource.Tools,Comparison=AutomationComparison.AtLeast,Action=AutomationAction.AllowWhenTrue,Threshold=4,Enabled=true});factory.NextAutomationRuleId=2;
        FactoryStateValidation.Validate(factory);True(true,"valid controller rule survives strict factory validation");
        factory.AutomationRules[0].Resource=Resource.Coins;True(Throws(()=>FactoryStateValidation.Validate(factory)),"automation rule cannot persist a Coins measurement");

        GameState game=GameState.CreateNew();
        foreach(var technology in TechCatalog.All)game.Technologies.Add(technology.Id);
        game.Era=Era.Industrial;
        game.CityProjects.Add(new CityProjectState{Kind=CityProjectKind.ResearchCampus,X=7,Z=7,Stage=0,DaysRemaining=0,StartedDay=0,CompletedDay=-1});
        CityProjects.Validate(game);True(true,"bounded owned project state validates");
        game.CityProjects[0].Delivered.RemoveAt(game.CityProjects[0].Delivered.Count-1);
        True(Throws(()=>CityProjects.Validate(game)),"project delivery arrays must retain all 38 resources");
    }

    static FactoryState FrozenFactory(int width,int height)=>new FactoryState
    {
        Version=2,Width=width,Height=height,NextEntityId=1,Entities=new List<FactoryEntity>(),Produced=FactoryState.NewInventory(),Exported=FactoryState.NewInventory(),Recovered=FactoryState.NewInventory(),
        PowerBudget=20,ElapsedSeconds=0,Platforms=null,AutomationRules=null,NextAutomationRuleId=0
    };
    static bool Throws(Action action){try{action();return false;}catch(Exception e)when(e is InvalidDataException||e is ArgumentException){return true;}}
    static void True(bool value,string name){if(!value)throw new Exception("expansion save check failed: "+name);passed++;Console.WriteLine("ok "+name);}
}

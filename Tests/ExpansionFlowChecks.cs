using System;
using System.Linq;
using Riverworks;

public static class ExpansionFlowChecks
{
    public static int Run()
    {
        var game=ExpansionScenario.Create();
        var city=new Simulation(game);
        var factory=new FactorySimulation(game.Factory,new CityLogistics(game));
        factory.ConfigureTechnology(game);
        double foodBefore=FoodEquivalent(game),waterBefore=Quantity(game,Resource.Water);
        for(int tick=0;tick<1200;tick++)
        {
            if(tick%50==0)city.Tick();
            game.Factory.PowerBudget=Math.Max(0,city.PowerCapacity-city.PowerUsed);
            factory.Tick(.1f);
            foreach(var resource in ResourceCatalog.SolidResources)game.Stock[(int)resource.Id]+=factory.TakeExports(resource.Id);
        }
        var bread=ExpansionScenario.FindBreadMachine(game);
        var tank=ExpansionScenario.FindTopTank(game);
        Require(game.Factory.Exported[(int)Resource.Bread]>0,"grain travels up twice, is milled and baked, then comes down to the ground export");
        Require(game.Factory.Produced[(int)Resource.Flour]>0&&game.Factory.Produced[(int)Resource.Bread]>0,"three-floor food line performs both real recipe transformations");
        Require(bread.AutomationBlocked&&!bread.Paused,"actual exported city stock closes the top-floor bread rule");
        Require(tank.Input[(int)Resource.Water]>=8,"water reaches the third floor through both actual powered risers");
        Require(game.Factory.Entities.Single(e=>e.Kind==FactoryKind.FluidTank&&e.Floor==0).AutomationBlocked,"upper tank water closes the ground tank rule");
        Require(Math.Abs(FoodEquivalent(game)-foodBefore)<.0001,"food mass equivalents are conserved across all three floors and city exports");
        Require(Math.Abs(Quantity(game,Resource.Water)-waterBefore)<.0001,"all prepared water is conserved through fluid links and tanks");
        FactoryStateValidation.Validate(game.Factory);CityProjects.Validate(game);
        Require(game.CityProjects.Count==0,"the integrated fixture leaves real project placement to the player flow");
        return 8;
    }

    static double FoodEquivalent(GameState game)=>Quantity(game,Resource.Grain)+Quantity(game,Resource.Flour)+Quantity(game,Resource.Bread)*2d/3d;
    static double Quantity(GameState game,Resource resource)
    {
        double sum=game.Stock[(int)resource];
        foreach(var e in game.Factory.Entities)sum+=e.Input[(int)resource]+e.Output[(int)resource]+(e.CargoResource==resource?1:0);
        foreach(var c in game.Cells)sum+=c.LogisticsInput[(int)resource]+c.LogisticsOutput[(int)resource];
        return sum+game.Factory.Recovered[(int)resource];
    }
    static void Require(bool okay,string text){if(!okay)throw new InvalidOperationException("Expansion flow failed: "+text);Console.WriteLine("ok "+text);}
}

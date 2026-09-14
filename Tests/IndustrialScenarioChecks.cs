using System;
using Riverworks;

public static class IndustrialScenarioChecks
{
    public static int Run()
    {
        int passed = 0;
        foreach (IndustryScenarioKind kind in Enum.GetValues(typeof(IndustryScenarioKind)))
        {
            GameState state = IndustrialScenario.Create(kind);
            True(state.Version == 6, $"{kind} uses current game schema", ref passed);
            True(state.Factory != null && state.Factory.Version == 3, $"{kind} uses current factory schema", ref passed);
            FactoryStateValidation.Validate(state.Factory);
            FactoryLayers.ValidateCity(state);
            CityProjects.Validate(state);
            True(MainMachineExists(state, kind), $"{kind} retains its representative main recipe", ref passed);
        }
        Console.WriteLine($"industrial scenario {passed} checks passed");
        return passed;
    }

    static bool MainMachineExists(GameState state, IndustryScenarioKind kind)
    {
        FactoryRecipe recipe = kind == IndustryScenarioKind.OilChemistry
            ? FactoryRecipe.PlasticPolymerization
            : kind == IndustryScenarioKind.AluminumRecycling
                ? FactoryRecipe.AluminumSmelting
                : FactoryRecipe.ControlUnitAssembly;
        return IndustrialScenario.FindMachine(state, recipe) != null;
    }

    static void True(bool value, string name, ref int passed)
    {
        if (!value) throw new Exception("industrial scenario check failed: " + name);
        passed++;
        Console.WriteLine("ok " + name);
    }
}

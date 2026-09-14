using System;
using System.Collections.Generic;
using System.Linq;
using Riverworks;

public static class IndustryChainChecks
{
    sealed class CraftEvent
    {
        public FactoryRecipe Recipe;
        public Dictionary<Resource, int> Inputs = new Dictionary<Resource, int>();
        public Dictionary<Resource, int> Outputs = new Dictionary<Resource, int>();
    }

    static readonly Resource[] PrimarySources =
    {
        Resource.Ore, Resource.CopperOre, Resource.Coal, Resource.Stone,
        Resource.Timber, Resource.Bauxite, Resource.Water, Resource.CrudeOil
    };

    static int passed;
    static Dictionary<Resource, int> ledger;
    static Dictionary<Resource, int> initial;
    static Dictionary<Resource, int> debits;
    static Dictionary<Resource, int> credits;
    static List<CraftEvent> events;
    static GameState allTechnology;

    public static int Run()
    {
        passed = 0;
        CanonicalResolverIsFiniteAndRejectsRecycleCycles();
        BlockedMultiOutputBatchIsAtomic();
        ProduceControlUnitFromPrimarySources();
        return passed;
    }

    static void CanonicalResolverIsFiniteAndRejectsRecycleCycles()
    {
        var canonical = Traverse(Resource.ControlUnit, CanonicalRecipe, ResourceCatalog.Count);
        True(canonical.Count >= 18, "canonical ControlUnit graph contains a substantial production chain");
        True(CanonicalRecipe(Resource.Steel) == FactoryRecipe.AlloySteel, "steel uses the canonical alloy route");
        True(CanonicalRecipe(Resource.Silica) == FactoryRecipe.SilicaCrushing, "silica uses crushing rather than depending on a byproduct");
        True(CanonicalRecipe(Resource.PetroleumGas) == FactoryRecipe.OilRefining, "petroleum gas begins with oil refining");
        True(CanonicalRecipe(Resource.Plastic) == FactoryRecipe.PlasticPolymerization &&
             CanonicalRecipe(Resource.Rubber) == FactoryRecipe.RubberPolymerization,
             "canonical polymers do not enter the recycling loop");

        RecipeSpec plasticRecycle = FactoryCatalog.GetRecipe(FactoryRecipe.PlasticRecycling);
        RecipeSpec rubberRecycle = FactoryCatalog.GetRecipe(FactoryRecipe.RubberRecycling);
        True(plasticRecycle.Inputs.Any(x => x.Resource == Resource.Fuel && x.Amount > 0) &&
             rubberRecycle.Inputs.Any(x => x.Resource == Resource.Fuel && x.Amount > 0),
             "both alternate recycling routes retain their fuel cost");

        bool cycleRejected = false;
        try
        {
            Traverse(Resource.Plastic, resource => resource == Resource.Plastic
                ? FactoryRecipe.PlasticRecycling
                : resource == Resource.Rubber
                    ? FactoryRecipe.RubberRecycling
                    : CanonicalRecipe(resource), ResourceCatalog.Count);
        }
        catch (InvalidOperationException)
        {
            cycleRejected = true;
        }
        True(cycleRejected, "bounded resolver rejects the alternate plastic-rubber recycle cycle");
    }

    static void BlockedMultiOutputBatchIsAtomic()
    {
        FactorySimulation sim = CreatePoweredMachine(FactoryRecipe.OilRefining, out FactoryEntity refinery);
        RecipeSpec recipe = FactoryCatalog.GetRecipe(FactoryRecipe.OilRefining);
        foreach (RecipeAmount input in recipe.Inputs) refinery.Input[(int)input.Resource] = input.Amount;
        refinery.Output[(int)Resource.HeavyOil] = FactoryCatalog.Get(refinery.Kind).OutputCapacity - 4;
        int[] before = refinery.Input.ToArray();
        sim.Tick(recipe.Duration + .01f);

        True(refinery.Powered, "blocked refinery is connected to real inlet power");
        True(before.SequenceEqual(refinery.Input), "blocked multi-output batch consumes no input");
        True(refinery.Output[(int)Resource.PetroleumGas] == 0 &&
             sim.State.Produced.All(value => value == 0),
             "blocked multi-output batch produces no partial output");
    }

    static void ProduceControlUnitFromPrimarySources()
    {
        ledger = Enum.GetValues<Resource>().ToDictionary(resource => resource, _ => 0);
        foreach (Resource resource in PrimarySources) ledger[resource] = 1000;
        initial = ledger.ToDictionary(pair => pair.Key, pair => pair.Value);
        debits = Enum.GetValues<Resource>().ToDictionary(resource => resource, _ => 0);
        credits = Enum.GetValues<Resource>().ToDictionary(resource => resource, _ => 0);
        events = new List<CraftEvent>();
        allTechnology = GameState.CreateNew();
        allTechnology.Technologies = Enum.GetValues<TechId>().Where(id => id != TechId.None).ToList();

        True(Enum.GetValues<Resource>().Where(resource => !PrimarySources.Contains(resource))
            .All(resource => initial[resource] == 0),
            "test routing ledger starts with primary raw resources only");

        Ensure(Resource.ControlUnit, 1, new HashSet<Resource>(),
            Enum.GetValues<Resource>().ToDictionary(resource => resource, _ => 0));

        True(ledger[Resource.ControlUnit] >= 1, "real factory batches produce at least one ControlUnit");
        True(events.Select(e => e.Recipe).Distinct().Count() >= 18,
            "ControlUnit production executes at least eighteen distinct recipes");

        FactoryRecipe[] requiredBranches =
        {
            FactoryRecipe.OilRefining, FactoryRecipe.PlasticPolymerization,
            FactoryRecipe.RubberPolymerization, FactoryRecipe.SulfurRecovery,
            FactoryRecipe.SulfuricAcid, FactoryRecipe.CircuitPrinting,
            FactoryRecipe.AdvancedCircuitAssembly, FactoryRecipe.ComputerAssembly,
            FactoryRecipe.AluminaRefining, FactoryRecipe.ScrapRefining,
            FactoryRecipe.AluminumSmelting, FactoryRecipe.CasingPressing,
            FactoryRecipe.BatteryAssembly, FactoryRecipe.ControlUnitAssembly
        };
        HashSet<FactoryRecipe> crafted = events.Select(e => e.Recipe).ToHashSet();
        True(requiredBranches.All(crafted.Contains),
            "oil chemistry, electronics, aluminum, battery, and final-control branches all execute");
        True(events.Any(e => e.Outputs.Count > 1 && e.Recipe == FactoryRecipe.OilRefining) &&
             events.Any(e => e.Outputs.Count > 1 && e.Recipe == FactoryRecipe.BatteryAssembly),
             "test ledger routes every output of multi-output recipes, including byproducts");
        True(ledger.Values.All(value => value >= 0), "test routing ledger never becomes negative");
        True(PrimarySources.All(resource => ledger[resource] <= initial[resource]),
            "production never creates primary source material");
        True(new[] { Resource.HeavyOil, Resource.PetroleumGas, Resource.Silica,
                Resource.AluminumScrap, Resource.SulfuricAcid }
            .All(resource => credits[resource] > debits[resource] && ledger[resource] > 0),
            "surplus multi-output byproducts remain explicitly represented in the final ledger");

        foreach (Resource resource in Enum.GetValues<Resource>())
        {
            Equal(debits[resource], events.Sum(e => e.Inputs.TryGetValue(resource, out int value) ? value : 0),
                $"event input vectors reconcile for {resource}");
            Equal(credits[resource], events.Sum(e => e.Outputs.TryGetValue(resource, out int value) ? value : 0),
                $"event output vectors reconcile for {resource}");
            Equal(initial[resource] + credits[resource] - debits[resource], ledger[resource],
                $"ledger conservation for {resource}, including unused byproducts");
        }
    }

    static void Ensure(Resource resource, int amount, HashSet<Resource> visiting,
        Dictionary<Resource, int> reserved)
    {
        if (Available(resource, reserved) >= amount) return;
        if (!visiting.Add(resource))
            throw new InvalidOperationException("Canonical recipe cycle at " + resource);

        FactoryRecipe recipeId = CanonicalRecipe(resource);
        RecipeSpec recipe = FactoryCatalog.GetRecipe(recipeId)
            ?? throw new InvalidOperationException("No canonical recipe for " + resource);
        RecipeAmount product = recipe.Outputs.FirstOrDefault(x => x.Resource == resource);
        if (product.Amount <= 0)
            throw new InvalidOperationException(recipeId + " does not produce " + resource);

        int missing = amount - Available(resource, reserved);
        int batches = (missing + product.Amount - 1) / product.Amount;
        for (int batch = 0; batch < batches; batch++)
        {
            // Reserve each parent ingredient as soon as it is available. A later
            // dependency may use the same resource, but must craft its own supply.
            foreach (RecipeAmount input in recipe.Inputs)
            {
                Ensure(input.Resource, input.Amount, visiting, reserved);
                reserved[input.Resource] += input.Amount;
            }
            foreach (RecipeAmount input in recipe.Inputs)
                reserved[input.Resource] -= input.Amount;
            ExecuteBatch(recipe, reserved);
        }
        visiting.Remove(resource);
    }

    static int Available(Resource resource, Dictionary<Resource, int> reserved) =>
        ledger[resource] - reserved[resource];

    static void ExecuteBatch(RecipeSpec recipe, Dictionary<Resource, int> reserved)
    {
        FactorySimulation sim = CreatePoweredMachine(recipe.Id, out FactoryEntity machine);
        var craft = new CraftEvent { Recipe = recipe.Id };

        foreach (RecipeAmount input in recipe.Inputs)
        {
            True(Available(input.Resource, reserved) >= input.Amount,
                $"{recipe.Id} input is available before routing without consuming an ancestor reservation");
            ledger[input.Resource] -= input.Amount;
            debits[input.Resource] += input.Amount;
            machine.Input[(int)input.Resource] = input.Amount;
            craft.Inputs[input.Resource] = input.Amount;
        }

        int[] inputsBefore = machine.Input.ToArray();
        sim.Tick(recipe.Duration + .01f);
        True(machine.Powered, $"{recipe.Id} runs on the inlet-backed power graph");
        foreach (RecipeAmount input in recipe.Inputs)
            Equal(inputsBefore[(int)input.Resource] - input.Amount,
                machine.Input[(int)input.Resource], $"{recipe.Id} consumes one exact input batch");

        foreach (RecipeAmount output in recipe.Outputs)
        {
            Equal(output.Amount, machine.Output[(int)output.Resource],
                $"{recipe.Id} emits its catalog output through the real engine");
            Equal(output.Amount, sim.State.Produced[(int)output.Resource],
                $"{recipe.Id} records its actual produced amount");
            ledger[output.Resource] += output.Amount;
            credits[output.Resource] += output.Amount;
            craft.Outputs[output.Resource] = output.Amount;
        }
        True(recipe.Outputs.Sum(x => x.Amount) == machine.Output.Sum(),
            $"{recipe.Id} has no untracked engine output");
        events.Add(craft);
    }

    static FactorySimulation CreatePoweredMachine(FactoryRecipe recipeId, out FactoryEntity machine)
    {
        RecipeSpec recipe = FactoryCatalog.GetRecipe(recipeId)
            ?? throw new InvalidOperationException("Unknown recipe " + recipeId);
        var state = FactoryState.CreateEmpty();
        state.PowerBudget = 1000;
        var sim = new FactorySimulation(state);
        sim.ConfigureTechnology(allTechnology ?? FullyResearchedState());
        Place(sim, FactoryKind.PowerInlet, 1, 1);
        machine = Place(sim, recipe.Machines[0], 4, 1);
        True(sim.SetRecipe(machine.Id, recipeId, out string reason),
            $"select {recipeId} on {machine.Kind}: {reason}");
        sim.Recalculate();
        return sim;
    }

    static GameState FullyResearchedState()
    {
        var state = GameState.CreateNew();
        state.Technologies = Enum.GetValues<TechId>().Where(id => id != TechId.None).ToList();
        return state;
    }

    static FactoryEntity Place(FactorySimulation sim, FactoryKind kind, int x, int z)
    {
        True(sim.TryPlace(kind, x, z, 0, out string reason), $"place {kind}: {reason}");
        return sim.GetAt(x, z);
    }

    static HashSet<FactoryRecipe> Traverse(Resource target,
        Func<Resource, FactoryRecipe> resolver, int maximumNodes)
    {
        var recipes = new HashSet<FactoryRecipe>();
        var visiting = new HashSet<Resource>();
        var visited = new HashSet<Resource>();

        void Visit(Resource resource)
        {
            if (PrimarySources.Contains(resource) || visited.Contains(resource)) return;
            if (!visiting.Add(resource)) throw new InvalidOperationException("Recipe cycle at " + resource);
            if (visited.Count + visiting.Count > maximumNodes)
                throw new InvalidOperationException("Recipe traversal exceeded its resource bound");
            FactoryRecipe id = resolver(resource);
            RecipeSpec recipe = FactoryCatalog.GetRecipe(id)
                ?? throw new InvalidOperationException("No recipe for " + resource);
            if (!recipe.Outputs.Any(x => x.Resource == resource))
                throw new InvalidOperationException(id + " does not produce " + resource);
            recipes.Add(id);
            foreach (RecipeAmount input in recipe.Inputs) Visit(input.Resource);
            visiting.Remove(resource);
            visited.Add(resource);
        }

        Visit(target);
        return recipes;
    }

    static FactoryRecipe CanonicalRecipe(Resource resource)
    {
        return resource switch
        {
            Resource.Steel => FactoryRecipe.AlloySteel,
            Resource.Copper => FactoryRecipe.CopperSmelting,
            Resource.Gear => FactoryRecipe.GearCutting,
            Resource.Wire => FactoryRecipe.WireDrawing,
            Resource.SteelBeam => FactoryRecipe.BeamRolling,
            Resource.SteelPipe => FactoryRecipe.PipeRolling,
            Resource.Silica => FactoryRecipe.SilicaCrushing,
            Resource.ModularFrame => FactoryRecipe.FrameAssembly,
            Resource.Motor => FactoryRecipe.MotorAssembly,
            Resource.Circuit => FactoryRecipe.CircuitPrinting,
            Resource.AdvancedCircuit => FactoryRecipe.AdvancedCircuitAssembly,
            Resource.Computer => FactoryRecipe.ComputerAssembly,
            Resource.ControlUnit => FactoryRecipe.ControlUnitAssembly,
            Resource.HeavyOil => FactoryRecipe.OilRefining,
            Resource.PetroleumGas => FactoryRecipe.OilRefining,
            Resource.Plastic => FactoryRecipe.PlasticPolymerization,
            Resource.Rubber => FactoryRecipe.RubberPolymerization,
            Resource.Sulfur => FactoryRecipe.SulfurRecovery,
            Resource.SulfuricAcid => FactoryRecipe.SulfuricAcid,
            Resource.Fuel => FactoryRecipe.FuelRefining,
            Resource.AluminaSolution => FactoryRecipe.AluminaRefining,
            Resource.AluminumScrap => FactoryRecipe.ScrapRefining,
            Resource.Aluminum => FactoryRecipe.AluminumSmelting,
            Resource.AluminumCasing => FactoryRecipe.CasingPressing,
            Resource.Battery => FactoryRecipe.BatteryAssembly,
            _ => throw new InvalidOperationException("No canonical conversion for " + resource)
        };
    }

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("industry chain check failed: " + name);
        passed++;
    }

    static void Equal(int expected, int actual, string name) =>
        True(expected == actual, $"{name} (expected {expected}, actual {actual})");
}

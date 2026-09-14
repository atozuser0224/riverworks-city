using System;

namespace Riverworks
{
    [Serializable]
    public readonly struct RecipeAmount
    {
        public readonly Resource Resource;
        public readonly int Amount;
        public RecipeAmount(Resource resource, int amount) { Resource = resource; Amount = amount; }
    }

    [Serializable]
    public sealed class RecipeSpec
    {
        public FactoryRecipe Id;
        public string Name = "", Description = "";
        public FactoryKind[] Machines = Array.Empty<FactoryKind>();
        public TechId RequiredTech;
        public float Duration;
        public RecipeAmount[] Inputs = Array.Empty<RecipeAmount>();
        public RecipeAmount[] Outputs = Array.Empty<RecipeAmount>();
        public bool IsExtraction;
        public Resource SourceResource = Resource.Coins;
    }
}

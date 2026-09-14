using System;
using System.Collections.Generic;

namespace Riverworks
{
    public readonly struct FluidPort
    {
        public readonly int X;
        public readonly int Z;
        public readonly int Floor;
        public readonly Resource Resource;
        public readonly bool IsInput;

        public FluidPort(int x, int z, Resource resource, bool isInput)
            : this(x, z, 0, resource, isInput) { }

        public FluidPort(int x, int z, int floor, Resource resource, bool isInput)
        {
            X = x;
            Z = z;
            Floor = floor;
            Resource = resource;
            IsInput = isInput;
        }
    }

    public static class FluidPorts
    {
        static readonly FluidPort[] Empty = Array.Empty<FluidPort>();

        public static IReadOnlyList<FluidPort> For(FactoryEntity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (!FactoryCatalog.IsProduction(entity.Kind)) return Empty;

            RecipeSpec recipe = FactoryCatalog.GetRecipe(EffectiveRecipe(entity));
            if (recipe == null) return Empty;

            var result = new List<FluidPort>(4);
            int inputSlot = 0;
            foreach (RecipeAmount amount in recipe.Inputs)
                if (ResourceCatalog.IsFluid(amount.Resource) && inputSlot < 2)
                    result.Add(Create(entity, inputSlot++, amount.Resource, true));

            int outputSlot = 0;
            foreach (RecipeAmount amount in recipe.Outputs)
                if (ResourceCatalog.IsFluid(amount.Resource) && outputSlot < 2)
                    result.Add(Create(entity, outputSlot++, amount.Resource, false));
            return result;
        }

        static FactoryRecipe EffectiveRecipe(FactoryEntity entity)
        {
            if (entity.Recipe != FactoryRecipe.None) return entity.Recipe;
            if (entity.Kind == FactoryKind.Drill) return FactoryRecipe.IronMining;
            if (entity.Kind == FactoryKind.WaterPump) return FactoryRecipe.WaterExtraction;
            if (entity.Kind == FactoryKind.OilPump) return FactoryRecipe.OilExtraction;
            return FactoryRecipe.None;
        }

        static FluidPort Create(FactoryEntity entity, int slot, Resource resource, bool input)
        {
            FactorySpec spec = FactoryCatalog.Get(entity.Kind);
            int width = spec == null ? 2 : spec.Width;
            int height = spec == null ? 2 : spec.Height;
            int direction = entity.Direction & 3;
            int x;
            int z;

            if (direction == 0)
            {
                x = input ? entity.X - 1 : entity.X + width;
                z = entity.Z + Math.Min(slot, height - 1);
            }
            else if (direction == 2)
            {
                x = input ? entity.X + width : entity.X - 1;
                z = entity.Z + (height - 1 - Math.Min(slot, height - 1));
            }
            else if (direction == 1)
            {
                x = entity.X + (width - 1 - Math.Min(slot, width - 1));
                z = input ? entity.Z - 1 : entity.Z + height;
            }
            else
            {
                x = entity.X + Math.Min(slot, width - 1);
                z = input ? entity.Z + height : entity.Z - 1;
            }
            return new FluidPort(x, z, entity.Floor, resource, input);
        }
    }
}

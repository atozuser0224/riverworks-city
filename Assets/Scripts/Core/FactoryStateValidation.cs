using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Riverworks
{
    /// <summary>Read-only schema validation and the one-way v1 factory inventory upgrade.</summary>
    public static class FactoryStateValidation
    {
        const int MaximumDimension = 256;
        const int LegacyLastKind = (int)FactoryKind.Splitter;
        const int LegacyLastRecipe = (int)FactoryRecipe.Bread;
        const int LegacyLastResource = ResourceCatalog.LegacyCount - 1;

        public static void Validate(FactoryState state)
        {
            ValidateCurrent(state, 3, false);
            if (state.Platforms == null || state.AutomationRules == null || state.NextAutomationRuleId <= 0)
                throw new InvalidDataException("Expansion factory collections are incomplete.");
            FactoryLayers.Validate(state);
            FactoryAutomation.Validate(state);
            ValidateLinks(state);
        }

        public static void ValidateVersion2(FactoryState state)
        {
            ValidateCurrent(state, 2, true);
            if ((state.Platforms != null && state.Platforms.Count != 0) ||
                (state.AutomationRules != null && state.AutomationRules.Count != 0) ||
                (state.NextAutomationRuleId != 0 && state.NextAutomationRuleId != 1))
                throw new InvalidDataException("Version 2 factory contains expansion collections.");
        }

        static void ValidateCurrent(FactoryState state, int version, bool frozenVersion2)
        {
            ValidateHeader(state, version);
            ValidateInventory(state.Produced, ResourceCatalog.Count, "production totals");
            ValidateInventory(state.Exported, ResourceCatalog.Count, "export totals");
            ValidateInventory(state.Recovered, ResourceCatalog.Count, "recovery totals");
            for (int i = 1; i < ResourceCatalog.Count; i++)
                if (state.Exported[i] != 0 && !ResourceCatalog.IsTransportable((Resource)i))
                    throw new InvalidDataException("Export totals contain a fluid.");
            ValidateEntities(state, false, frozenVersion2);
        }

        public static void ValidateLegacy(FactoryState state)
        {
            ValidateHeader(state, 1);
            if ((state.Platforms != null && state.Platforms.Count != 0) ||
                (state.AutomationRules != null && state.AutomationRules.Count != 0) ||
                (state.NextAutomationRuleId != 0 && state.NextAutomationRuleId != 1))
                throw new InvalidDataException("Legacy factory contains expansion collections.");
            ValidateInventory(state.Produced, ResourceCatalog.LegacyCount, "legacy production totals");
            ValidateInventory(state.Exported, ResourceCatalog.LegacyCount, "legacy export totals");
            ValidateInventory(state.Recovered, ResourceCatalog.LegacyCount, "legacy recovery totals");
            ValidateEntities(state, true, false);
        }

        public static FactoryState UpgradeLegacy(FactoryState state)
        {
            ValidateLegacy(state);
            Expand(state.Produced);
            Expand(state.Exported);
            Expand(state.Recovered);
            foreach (FactoryEntity entity in state.Entities)
            {
                Expand(entity.Input);
                Expand(entity.Output);
                entity.Paused = false;
                entity.ClockPercent = 100;
                entity.FluidProgress = 0;
                entity.FluidCursor = 0;
                entity.Floor = 0;
                entity.LinkId = 0;
                entity.IsLinkSender = false;
                entity.ControllerInstalled = false;
                entity.AutomationBlocked = false;
            }
            state.Platforms = null;
            state.AutomationRules = null;
            state.NextAutomationRuleId = 0;
            state.Version = 2;
            return state;
        }

        public static FactoryState UpgradeVersion2(FactoryState state)
        {
            ValidateVersion2(state);
            foreach (FactoryEntity entity in state.Entities)
            {
                entity.Floor = 0;
                entity.LinkId = 0;
                entity.IsLinkSender = false;
                entity.ControllerInstalled = false;
                entity.AutomationBlocked = false;
            }
            state.Platforms = new List<FactoryPlatform>();
            state.AutomationRules = new List<AutomationRule>();
            state.NextAutomationRuleId = 1;
            state.Version = 3;
            return state;
        }

        static void ValidateHeader(FactoryState state, int version)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Version != version || state.Width <= 0 || state.Height <= 0 || state.Width > MaximumDimension || state.Height > MaximumDimension)
                throw new InvalidDataException("Unsupported factory save format.");
            if (state.Entities == null || state.Produced == null || state.Exported == null || state.Recovered == null)
                throw new InvalidDataException("Factory save data is incomplete.");
            if (state.PowerBudget < 0 || state.PowerBudget > 1000000 || !Finite(state.ElapsedSeconds) || state.ElapsedSeconds < 0)
                throw new InvalidDataException("Factory power or elapsed time is invalid.");
        }

        static void ValidateEntities(FactoryState state, bool legacy, bool frozenVersion2)
        {
            var ids = new HashSet<int>();
            var occupied = new HashSet<int>();
            foreach (FactoryEntity entity in state.Entities)
            {
                if (entity == null) throw new InvalidDataException("Factory entity is missing.");
                int kindId = (int)entity.Kind;
                int recipeId = (int)entity.Recipe;
                if (legacy && (kindId <= 0 || kindId > LegacyLastKind || recipeId < 0 || recipeId > LegacyLastRecipe))
                    throw new InvalidDataException("Legacy factory contains a newer kind or recipe.");
                if (frozenVersion2 && (kindId <= 0 || kindId > (int)FactoryKind.Manufacturer))
                    throw new InvalidDataException("Version 2 factory contains an expansion kind.");
                FactorySpec spec = FactoryCatalog.Get(entity.Kind);
                if (spec == null || entity.Id <= 0 || !ids.Add(entity.Id) || entity.Direction < 0 || entity.Direction > 3 ||
                    entity.X < 0 || entity.Z < 0 || entity.X + spec.Width > state.Width || entity.Z + spec.Height > state.Height)
                    throw new InvalidDataException("Factory entity data is invalid.");

                int width = legacy ? ResourceCatalog.LegacyCount : ResourceCatalog.Count;
                ValidateInventory(entity.Input, width, "entity input");
                ValidateInventory(entity.Output, width, "entity output");
                if (!Finite(entity.CargoProgress) || entity.CargoProgress < 0 || entity.CargoProgress > 1 || !Finite(entity.Progress) || entity.Progress < 0 ||
                    !Finite(entity.FluidProgress) || entity.FluidProgress < 0 || entity.FluidProgress > 1 || entity.FluidCursor < 0 || entity.FluidCursor > 3)
                    throw new InvalidDataException("Factory progress is invalid.");

                if (legacy)
                {
                    // JsonUtility may materialize a field that did not exist on disk as either
                    // its CLR initializer (100) or the serializer default (0). Both mean the
                    // legacy 100% clock; the upgrade writes the canonical value explicitly.
                    if (entity.Paused || (entity.ClockPercent != 0 && entity.ClockPercent != 100) || entity.FluidProgress != 0 || entity.FluidCursor != 0)
                        throw new InvalidDataException("Legacy factory contains newer runtime flags.");
                    ValidateLegacyEntity(entity);
                }
                else ValidateCurrentEntity(entity, spec);

                if (legacy || frozenVersion2)
                {
                    if (entity.Floor != 0 || entity.LinkId != 0 || entity.IsLinkSender || entity.ControllerInstalled || entity.AutomationBlocked)
                        throw new InvalidDataException("Older factory contains expansion entity fields.");
                }
                else ValidateExpansionEntity(entity);

                for (int z = entity.Z; z < entity.Z + spec.Height; z++)
                for (int x = entity.X; x < entity.X + spec.Width; x++)
                    if (!occupied.Add(entity.Floor * state.Width * state.Height + z * state.Width + x)) throw new InvalidDataException("Factory entities overlap.");
            }
            if (state.NextEntityId <= 0 || ids.Any(id => id >= state.NextEntityId))
                throw new InvalidDataException("Next factory entity id is invalid.");
        }

        static void ValidateExpansionEntity(FactoryEntity entity)
        {
            if (entity.Floor < 0 || entity.Floor > FactoryLayers.MaxFloor)
                throw new InvalidDataException("Factory entity floor is invalid.");
            bool link = entity.Kind == FactoryKind.ItemLift || entity.Kind == FactoryKind.FluidRiser;
            if (!link && (entity.LinkId != 0 || entity.IsLinkSender))
                throw new InvalidDataException("Ordinary factory entity contains vertical-link fields.");
            if (link && entity.LinkId <= 0)
                throw new InvalidDataException("Vertical endpoint is missing its reciprocal link.");
            if (link && !entity.IsLinkSender && entity.ControllerInstalled)
                throw new InvalidDataException("Vertical receiver cannot own an automation controller.");
            if ((entity.Kind == FactoryKind.Drill || entity.Kind == FactoryKind.WaterPump || entity.Kind == FactoryKind.OilPump ||
                 entity.Kind == FactoryKind.ImportDock || entity.Kind == FactoryKind.ExportDock || entity.Kind == FactoryKind.PowerInlet) && entity.Floor != 0)
                throw new InvalidDataException("Ground-only factory equipment is on an upper floor.");
        }

        static void ValidateLinks(FactoryState state)
        {
            var byId = state.Entities.ToDictionary(entity => entity.Id);
            foreach (FactoryEntity entity in state.Entities)
            {
                if (entity.Kind != FactoryKind.ItemLift && entity.Kind != FactoryKind.FluidRiser) continue;
                if (!byId.TryGetValue(entity.LinkId, out FactoryEntity other) || other == entity || other.Kind != entity.Kind ||
                    other.LinkId != entity.Id || other.X != entity.X || other.Z != entity.Z ||
                    Math.Abs(other.Floor - entity.Floor) != 1 || other.IsLinkSender == entity.IsLinkSender)
                    throw new InvalidDataException("Vertical factory endpoints are not reciprocal.");
            }
        }

        static void ValidateLegacyEntity(FactoryEntity entity)
        {
            if ((int)entity.Filter < 0 || (int)entity.Filter > LegacyLastResource ||
                (entity.Kind != FactoryKind.Inserter && entity.Filter != Resource.Coins))
                throw new InvalidDataException("Legacy filter is invalid.");
            bool cargo = (int)entity.CargoResource > 0 && (int)entity.CargoResource <= LegacyLastResource;
            if ((entity.CargoResource == Resource.Coins && entity.CargoProgress != 0) ||
                (!cargo && entity.CargoResource != Resource.Coins) ||
                (cargo && !IsItemCarrier(entity.Kind)))
                throw new InvalidDataException("Legacy cargo is invalid.");
            bool recipeOkay = entity.Kind == FactoryKind.Furnace ? entity.Recipe == FactoryRecipe.IronPlate :
                entity.Kind == FactoryKind.Assembler ? entity.Recipe == FactoryRecipe.None || entity.Recipe == FactoryRecipe.Tools || entity.Recipe == FactoryRecipe.Flour || entity.Recipe == FactoryRecipe.Bread :
                entity.Recipe == FactoryRecipe.None;
            if (!recipeOkay) throw new InvalidDataException("Legacy machine recipe is invalid.");
            float duration = entity.Kind == FactoryKind.Drill ? 2f : FactoryCatalog.RecipeDuration(entity.Recipe);
            if (entity.Progress > duration) throw new InvalidDataException("Legacy machine progress exceeds its cycle.");
            ValidateLegacyBuffers(entity);
        }

        static void ValidateCurrentEntity(FactoryEntity entity, FactorySpec spec)
        {
            if (!ResourceCatalog.IsValid(entity.Filter) ||
                ((entity.Kind == FactoryKind.Inserter || entity.Kind == FactoryKind.ItemLift) && entity.Filter != Resource.Coins && !ResourceCatalog.IsTransportable(entity.Filter)) ||
                (IsFluidContainer(entity.Kind) && entity.Filter != Resource.Coins && !ResourceCatalog.IsFluid(entity.Filter)) ||
                (entity.Kind != FactoryKind.Inserter && entity.Kind != FactoryKind.ItemLift && !IsFluidContainer(entity.Kind) && entity.Filter != Resource.Coins))
                throw new InvalidDataException("Factory filter is invalid.");
            bool cargo = ResourceCatalog.IsTransportable(entity.CargoResource);
            if ((entity.CargoResource == Resource.Coins && entity.CargoProgress != 0) ||
                (!cargo && entity.CargoResource != Resource.Coins) || (cargo && !IsItemCarrier(entity.Kind)))
                throw new InvalidDataException("Factory cargo is invalid.");
            bool recipeCompatible = FactoryCatalog.IsProduction(entity.Kind)
                ? FactoryCatalog.IsRecipeCompatible(entity.Kind, entity.Recipe)
                : entity.Recipe == FactoryRecipe.None;
            if (!recipeCompatible) throw new InvalidDataException("Machine recipe is incompatible.");
            if ((entity.Kind == FactoryKind.WaterPump || entity.Kind == FactoryKind.OilPump) && entity.Recipe == FactoryRecipe.None)
                throw new InvalidDataException("Extraction pump recipe is missing.");
            if (!ValidClock(entity.ClockPercent) || (!FactoryCatalog.IsClockable(entity.Kind) && entity.ClockPercent != 100))
                throw new InvalidDataException("Machine clock is invalid.");

            RecipeSpec recipe = EffectiveRecipe(entity);
            float duration = recipe?.Duration ?? 0;
            if (entity.Progress > duration + .0001f) throw new InvalidDataException("Machine progress exceeds its cycle.");
            int inputTotal = Total(entity.Input), outputTotal = Total(entity.Output);
            if (inputTotal > spec.InputCapacity || outputTotal > spec.OutputCapacity)
                throw new InvalidDataException("Factory buffer capacity is exceeded.");
            ValidateCurrentBuffers(entity, recipe);
        }

        static RecipeSpec EffectiveRecipe(FactoryEntity entity)
        {
            if (entity.Kind == FactoryKind.Drill && entity.Recipe == FactoryRecipe.None)
                return FactoryCatalog.GetRecipe(FactoryRecipe.IronMining);
            return FactoryCatalog.GetRecipe(entity.Recipe);
        }

        static void ValidateCurrentBuffers(FactoryEntity entity, RecipeSpec recipe)
        {
            if (FactoryCatalog.IsProduction(entity.Kind))
            {
                var inputs = new HashSet<Resource>((recipe?.Inputs ?? Array.Empty<RecipeAmount>()).Select(x => x.Resource));
                var outputs = new HashSet<Resource>((recipe?.Outputs ?? Array.Empty<RecipeAmount>()).Select(x => x.Resource));
                for (int i = 1; i < ResourceCatalog.Count; i++)
                {
                    if (entity.Input[i] != 0 && !inputs.Contains((Resource)i)) throw new InvalidDataException("Machine input does not belong to its recipe.");
                    if (entity.Output[i] != 0 && !outputs.Contains((Resource)i)) throw new InvalidDataException("Machine output does not belong to its recipe.");
                }
                return;
            }
            if (entity.Kind == FactoryKind.Storage || entity.Kind == FactoryKind.ImportDock || entity.Kind == FactoryKind.ExportDock)
            {
                for (int i = 1; i < ResourceCatalog.Count; i++)
                    if ((entity.Input[i] != 0 || entity.Output[i] != 0) && !ResourceCatalog.IsTransportable((Resource)i))
                        throw new InvalidDataException("Item storage contains a fluid.");
                if (entity.Output.Any(value => value != 0)) throw new InvalidDataException("Storage output must be empty.");
                return;
            }
            if (IsFluidContainer(entity.Kind))
            {
                int fluids = 0;
                for (int i = 1; i < ResourceCatalog.Count; i++) if (entity.Input[i] != 0)
                {
                    if (!ResourceCatalog.IsFluid((Resource)i) || ++fluids > 1) throw new InvalidDataException("Fluid container is mixed.");
                }
                if (entity.Output.Any(value => value != 0)) throw new InvalidDataException("Fluid transport output must be empty.");
                return;
            }
            if (entity.Input.Any(value => value != 0) || entity.Output.Any(value => value != 0))
                throw new InvalidDataException("Passive factory equipment contains inventory.");
        }

        static void ValidateLegacyBuffers(FactoryEntity entity)
        {
            int input = Total(entity.Input), output = Total(entity.Output);
            bool storage = entity.Kind == FactoryKind.Storage || entity.Kind == FactoryKind.ImportDock || entity.Kind == FactoryKind.ExportDock;
            bool machine = entity.Kind == FactoryKind.Drill || entity.Kind == FactoryKind.Furnace || entity.Kind == FactoryKind.Assembler;
            if (storage && (input > 80 || output != 0)) throw new InvalidDataException("Legacy storage capacity is invalid.");
            if (machine && (input > 24 || output > 24)) throw new InvalidDataException("Legacy machine capacity is invalid.");
            if (!storage && !machine && (input != 0 || output != 0)) throw new InvalidDataException("Legacy passive equipment contains inventory.");
            for (int i = 1; i < ResourceCatalog.LegacyCount; i++)
            {
                if (entity.Kind == FactoryKind.Drill && (entity.Input[i] != 0 || (i != (int)Resource.Ore && entity.Output[i] != 0))) throw new InvalidDataException("Legacy drill inventory is invalid.");
                if (entity.Kind == FactoryKind.Furnace && ((i != (int)Resource.Ore && entity.Input[i] != 0) || (i != (int)Resource.Steel && entity.Output[i] != 0))) throw new InvalidDataException("Legacy furnace inventory is invalid.");
            }
            if (entity.Kind == FactoryKind.Assembler)
            {
                RecipeSpec recipe = FactoryCatalog.GetRecipe(entity.Recipe);
                var inputs = new HashSet<Resource>((recipe?.Inputs ?? Array.Empty<RecipeAmount>()).Select(value => value.Resource));
                var outputs = new HashSet<Resource>((recipe?.Outputs ?? Array.Empty<RecipeAmount>()).Select(value => value.Resource));
                for (int i = 1; i < ResourceCatalog.LegacyCount; i++)
                {
                    if (entity.Input[i] != 0 && !inputs.Contains((Resource)i)) throw new InvalidDataException("Legacy assembler input is invalid.");
                    if (entity.Output[i] != 0 && !outputs.Contains((Resource)i)) throw new InvalidDataException("Legacy assembler output is invalid.");
                }
            }
        }

        static void ValidateInventory(List<int> values, int width, string label)
        {
            if (values == null || values.Count != width || values[0] != 0 || values.Any(value => value < 0))
                throw new InvalidDataException("Invalid " + label + ".");
        }

        static void Expand(List<int> values)
        {
            while (values.Count < ResourceCatalog.Count) values.Add(0);
        }

        static bool IsItemCarrier(FactoryKind kind) => kind == FactoryKind.Belt || kind == FactoryKind.Splitter || kind == FactoryKind.Inserter || kind == FactoryKind.ItemLift;
        static bool IsFluidContainer(FactoryKind kind) => kind == FactoryKind.Pipe || kind == FactoryKind.PipeJunction || kind == FactoryKind.FluidTank || kind == FactoryKind.FluidRiser;
        static bool ValidClock(int value) => value == 50 || value == 100 || value == 150 || value == 200;
        static int Total(List<int> values) { int total = 0; for (int i = 1; i < values.Count; i++) checked { total += values[i]; } return total; }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

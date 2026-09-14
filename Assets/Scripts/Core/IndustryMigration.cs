using System;
using System.Collections.Generic;

namespace Riverworks
{
    public static class IndustryMigration
    {
        public static void UpgradeLegacy(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Version < 1 || state.Version > 4) throw new ArgumentException("Only legacy game states can be upgraded.", nameof(state));
            TechCatalog.MigrateLegacy(state);
            Expand(state.Stock);
            if (state.Cells != null) foreach (Cell cell in state.Cells)
            {
                if (cell == null) continue;
                Expand(cell.LogisticsInput);
                Expand(cell.LogisticsOutput);
            }
            state.Factory = UpgradeFactory(state.Factory);
            state.ArchivedFactory = UpgradeFactory(state.ArchivedFactory);
            state.Version = 5;
        }

        static FactoryState UpgradeFactory(FactoryState factory)
        {
            if (factory == null) return null;
            if (factory.Version == 1) return FactoryStateValidation.UpgradeLegacy(factory);
            // TechCatalog/CityLogistics construct the new default FactoryState while performing
            // the historical v1-v4 city migration. With the v0.9 DTO that constructor is v3;
            // accept only its exact empty expansion shape and restore the required frozen v2
            // intermediate before the separate v5->v6 migration runs.
            if (factory.Version == 3)
            {
                if (factory.Entities == null || factory.Entities.Count != 0 ||
                    (factory.Platforms != null && factory.Platforms.Count != 0) ||
                    (factory.AutomationRules != null && factory.AutomationRules.Count != 0) ||
                    factory.NextAutomationRuleId != 1)
                    throw new ArgumentException("Legacy migration produced a nonempty expansion factory.", nameof(factory));
                factory.Platforms = null;
                factory.AutomationRules = null;
                factory.NextAutomationRuleId = 0;
                factory.Version = 2;
            }
            FactoryStateValidation.ValidateVersion2(factory);
            return factory;
        }

        static void Expand(List<float> values)
        {
            if (values == null) return;
            while (values.Count < ResourceCatalog.Count) values.Add(0);
        }
    }
}

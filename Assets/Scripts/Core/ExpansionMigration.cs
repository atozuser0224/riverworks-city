using System;
using System.Collections.Generic;

namespace Riverworks
{
    /// <summary>One-way upgrade from the frozen v0.8 save schema to v0.9.</summary>
    public static class ExpansionMigration
    {
        public static void Upgrade(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Version != 5) throw new ArgumentException("Only a validated version 5 game can be expanded.", nameof(state));
            if (state.CityProjects != null && state.CityProjects.Count != 0)
                throw new ArgumentException("Version 5 game contains expansion projects.", nameof(state));

            // Validate both graphs before touching either one. A malformed archive must not
            // leave the active factory half-upgraded.
            if (state.Factory == null) throw new ArgumentException("Version 5 game has no active factory.", nameof(state));
            FactoryStateValidation.ValidateVersion2(state.Factory);
            if (state.ArchivedFactory != null) FactoryStateValidation.ValidateVersion2(state.ArchivedFactory);

            state.Factory = UpgradeFactory(state.Factory);
            state.ArchivedFactory = UpgradeFactory(state.ArchivedFactory);
            state.CityProjects = new List<CityProjectState>();
            state.Version = 6;
        }

        static FactoryState UpgradeFactory(FactoryState factory)
        {
            if (factory == null) return null;
            return FactoryStateValidation.UpgradeVersion2(factory);
        }
    }
}

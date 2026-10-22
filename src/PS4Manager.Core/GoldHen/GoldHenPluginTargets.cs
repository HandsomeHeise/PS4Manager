using System;
using System.Collections.Generic;
using System.Linq;

namespace PS4Manager.Core.GoldHen
{
    public static class GoldHenPluginTargets
    {
        public static IReadOnlyList<GoldHenPluginTarget> FromDiscovery(GoldHenDiscovery discovery)
        {
            if (discovery == null) return new GoldHenPluginTarget[0];

            var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in discovery.Plugins)
                if (!string.IsNullOrEmpty(plugin.TitleId))
                    ids.Add(plugin.TitleId);

            AddLocation(ids, discovery.Location(GoldHenSection.Cheats));
            AddLocation(ids, discovery.Location(GoldHenSection.Patches));

            var targets = new List<GoldHenPluginTarget> { new GoldHenPluginTarget(null) };
            targets.AddRange(ids.Select(id => new GoldHenPluginTarget(id)));
            return targets;
        }

        private static void AddLocation(SortedSet<string> ids, GoldHenLocation location)
        {
            if (location == null) return;
            foreach (var entry in location.Entries)
            {
                var titleId = GoldHenInventory.TitleIdOf(entry.Path);
                if (!string.IsNullOrEmpty(titleId)) ids.Add(titleId);
            }
        }
    }
}

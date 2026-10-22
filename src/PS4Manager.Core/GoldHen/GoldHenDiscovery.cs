using System.Collections.Generic;
using System.Linq;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>Was auf dieser Konsole gefunden wurde — nichts davon ist geraten.</summary>
    public sealed class GoldHenDiscovery
    {
        public GoldHenDiscovery(string root, bool rootExists, IReadOnlyList<GoldHenLocation> locations,
            IReadOnlyList<string> configurationFiles, string configurationPath, IReadOnlyList<PluginEntry> plugins)
        {
            Root = root;
            RootExists = rootExists;
            Locations = locations;
            ConfigurationFiles = configurationFiles;
            ConfigurationPath = configurationPath;
            Plugins = plugins;
        }

        public string Root { get; }
        public bool RootExists { get; }
        public IReadOnlyList<GoldHenLocation> Locations { get; }
        public IReadOnlyList<string> ConfigurationFiles { get; }
        public string ConfigurationPath { get; }
        public IReadOnlyList<PluginEntry> Plugins { get; }

        public GoldHenLocation Location(GoldHenSection section)
        {
            return Locations.First(l => l.Section == section);
        }
    }
}

using System;
using System.Linq;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenPluginTargetTests
    {
        [Fact]
        public void Targets_include_default_existing_ini_sections_and_game_files()
        {
            var discovery = new GoldHenDiscovery("/data/GoldHEN", true, new[]
            {
                Location(GoldHenSection.Plugins),
                Location(GoldHenSection.Cheats, "/data/GoldHEN/cheats/CUSA00411.json"),
                Location(GoldHenSection.Patches, "/data/GoldHEN/patches/PPSA00001.xml"),
                Location(GoldHenSection.Configuration)
            }, new[] { "/data/GoldHEN/plugins.ini" }, "/data/GoldHEN/plugins.ini", new[]
            {
                new PluginEntry("global.prx", "/data/GoldHEN/plugins/global.prx", GoldHenActivation.Enabled, true, 2),
                new PluginEntry("scoped.prx", "/data/GoldHEN/plugins/scoped.prx", GoldHenActivation.Disabled, true, 4, "CUSA00000")
            });

            var targets = GoldHenPluginTargets.FromDiscovery(discovery).ToList();

            Assert.Equal(new[] { null, "CUSA00000", "CUSA00411", "PPSA00001" }, targets.Select(t => t.TitleId));
            Assert.Equal("default", targets[0].DisplayName);
        }

        [Fact]
        public void Targets_are_empty_without_discovery()
        {
            Assert.Empty(GoldHenPluginTargets.FromDiscovery(null));
        }

        private static GoldHenLocation Location(GoldHenSection section, params string[] paths)
        {
            return new GoldHenLocation(section, "/data/GoldHEN/" + section.ToString().ToLowerInvariant(),
                paths.Length > 0, paths.Select(p => new RemoteItem(p, false, 1, DateTime.MinValue)).ToList());
        }
    }
}

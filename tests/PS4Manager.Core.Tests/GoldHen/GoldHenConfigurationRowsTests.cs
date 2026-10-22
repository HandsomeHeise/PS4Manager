using System;
using System.Linq;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenConfigurationRowsTests
    {
        [Fact]
        public void Parsed_plugin_rows_still_point_at_the_configuration_file()
        {
            var item = new RemoteItem("/data/GoldHEN/plugins.ini", false, 100, DateTime.MinValue);
            var rows = GoldHenConfigurationRows.Build(new[] { item }, item.Path, new[]
            {
                new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx",
                    GoldHenActivation.Enabled, true, 1, "CUSA00411")
            }).ToList();

            Assert.Equal(2, rows.Count);
            Assert.Same(item, rows[0].Item);
            Assert.Same(item, rows[1].Item);
            Assert.True(rows[1].IsPluginLine);
        }
    }
}

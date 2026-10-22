using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenRequestGateTests
    {
        [Fact]
        public void A_new_refresh_invalidates_the_previous_session_request()
        {
            var gate = new GoldHenRequestGate();

            var first = gate.BeginRefresh();
            var second = gate.BeginRefresh();

            Assert.False(gate.IsCurrentRefresh(first));
            Assert.True(gate.IsCurrentRefresh(second));
        }

        [Fact]
        public void A_new_section_request_invalidates_rows_for_the_previous_section()
        {
            var gate = new GoldHenRequestGate();

            var plugins = gate.BeginContent(GoldHenSection.Plugins);
            var cheats = gate.BeginContent(GoldHenSection.Cheats);

            Assert.False(gate.IsCurrentContent(plugins, GoldHenSection.Plugins));
            Assert.True(gate.IsCurrentContent(cheats, GoldHenSection.Cheats));
            Assert.False(gate.IsCurrentContent(cheats, GoldHenSection.Plugins));
        }
    }
}

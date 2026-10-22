using PS4Manager.UI;
using System;
using System.Linq;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Session;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    [Collection(UiControlCollection.Name)]
    public sealed class GoldHenFormTests
    {
        [Fact]
        public void Toolbar_controls_are_docked_instead_of_overlapping_at_the_origin()
        {
            using (var form = new GoldHenForm(new ManagerSessionController(new UnusedFactory())))
            {
                var toolbar = form.Controls.OfType<UiPanel>().Single(p => p.Dock == DockStyle.Top);
                var buttons = toolbar.Controls.OfType<UiButton>().ToList();

                Assert.Equal(DockStyle.Fill, toolbar.Controls.OfType<UiLabel>().Single().Dock);
                Assert.Equal(DockStyle.Left, buttons.Single(b => b.Text == "Aktualisieren").Dock);
                Assert.All(buttons.Where(b => b.Text != "Aktualisieren"), b => Assert.Equal(DockStyle.Right, b.Dock));
            }
        }

        private sealed class UnusedFactory : IManagerSessionFactory
        {
            public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode)
            {
                throw new InvalidOperationException("The layout test does not connect.");
            }
        }
    }
}

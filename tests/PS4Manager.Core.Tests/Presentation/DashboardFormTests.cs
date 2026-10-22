using PS4Manager.UI;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Session;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class DashboardFormTests
    {
        [Fact]
        public void Frame4_tool_is_available_without_an_ftp_session()
        {
            using (var form = new DashboardForm(new ManagerSettings()))
            {
                form.CreateControl();

                var button = FindRibbonItem(form, "Frame4 Debugger");

                Assert.True(button.Enabled);
            }
        }

        [Theory]
        [InlineData("health", "PS4 Health")]
        [InlineData("prx", "PRX Manager")]
        [InlineData("backup", "Backup Timeline")]
        [InlineData("packages", "PKG Manager")]
        [InlineData("luma", "Luma Studio — PS4")]
        [InlineData("mods", "Mod Library")]
        [InlineData("games", "Games")]
        public void New_manager_tools_open_real_windows_instead_of_placeholders(string key, string windowTitle)
        {
            using (var form = new DashboardForm(new ManagerSettings()))
            {
                form.CreateControl();

                OpenToolForTests(form, key);

                Assert.Contains(form.OwnedForms.Cast<Form>(), f => f.Text == windowTitle);
                Assert.DoesNotContain(form.OwnedForms.Cast<Form>().SelectMany(FindLabels),
                    l => l.Text == "This module is not built yet.");
            }
        }

        [Fact]
        public void Frame4_tool_opens_the_debugger_window_instead_of_the_placeholder()
        {
            using (var form = new DashboardForm(new ManagerSettings()))
            {
                form.CreateControl();

                OpenToolForTests(form, "frame4");

                var tool = Assert.Single(form.OwnedForms);
                Assert.Equal("Frame4 Debugger", tool.Text);
                Assert.DoesNotContain(FindLabels(tool), l => l.Text == "This module is not built yet.");
            }
        }

        [Fact]
        public void Dashboard_does_not_duplicate_tool_buttons_below_the_ribbon()
        {
            using (var form = new DashboardForm(new ManagerSettings()))
            {
                form.Show();

                Assert.Empty(ToolButtons(form));
            }
        }

        [Fact]
        public void Dashboard_uses_a_ribbon_for_primary_commands()
        {
            using (var form = new DashboardForm(new ManagerSettings()))
            {
                form.Show();

                var ribbon = Assert.Single(FindControls<AppRibbon>(form));

                Assert.Equal(new[] { "Start", "PS4 / Modding", "Games", "Tools", "View" }, ribbon.Tabs.Select(t => t.Text));
                Assert.Contains(ribbon.Tabs[0].Groups, g => g.Title == "Console");
                Assert.Contains(ribbon.Tabs.SelectMany(t => t.Groups), g => g.Title == "Tools");
                Assert.Contains(ribbon.Tabs[0].Groups, g => g.Title == "Updates");
                Assert.Equal(ToolNames().OrderBy(t => t), RibbonToolNames(ribbon).OrderBy(t => t));
            }
        }

        [Fact]
        public void Package_manager_toolbar_buttons_are_compact()
        {
            using (var form = new PackageManagerForm(new ManagerSessionController(new NoopSessionFactory())))
            {
                form.Show();

                var buttons = FindButtons(form).ToList();

                Assert.Contains(buttons, b => b.Text == "Upload PKG");
                Assert.All(buttons, button => Assert.InRange(button.Height, 1, 38));
            }
        }

        [Fact]
        public void Luma_manager_toolbar_buttons_are_large_enough_to_click()
        {
            using (var form = new LumaManagerForm(new ManagerSettings(), () => { }))
            {
                form.Show();

                var commandButtons = FindButtons(form)
                    .Where(b => b.Text == "Connect" ||
                                b.Text == "Disconnect" ||
                                b.Text == "Hello" ||
                                b.Text == "Reload Scripts" ||
                                b.Text == "Resources")
                    .ToList();

                Assert.Equal(5, commandButtons.Count);
                Assert.All(commandButtons, button => Assert.InRange(button.Width, 88, 240));
                Assert.All(commandButtons, button => Assert.InRange(button.Height, 30, 42));
            }
        }

        private static System.Collections.Generic.IEnumerable<UiButton> FindButtons(Control root)
        {
            foreach (Control child in root.Controls)
            {
                var button = child as UiButton;
                if (button != null) yield return button;
                foreach (var nested in FindButtons(child)) yield return nested;
            }
        }

        private static System.Collections.Generic.IEnumerable<T> FindControls<T>(Control root)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var match = child as T;
                if (match != null) yield return match;
                foreach (var nested in FindControls<T>(child)) yield return nested;
            }
        }

        private static System.Collections.Generic.IEnumerable<UiButton> ToolButtons(Control root)
        {
            return FindButtons(root)
                .Where(b => b.Text == "File Manager" ||
                            b.Text == "Screenshots & Videos" ||
                            b.Text == "GoldHEN Manager" ||
                            b.Text == "Frame4 Debugger" ||
                            b.Text == "PS4 Health" ||
                            b.Text == "PRX Manager" ||
                            b.Text == "Backup Timeline" ||
                            b.Text == "PKG Manager" ||
                            b.Text == "Luma Studio");
        }

        private static RibbonItem FindRibbonItem(DashboardForm form, string text)
        {
            var ribbon = Assert.Single(FindControls<AppRibbon>(form));
            return ribbon.Tabs.SelectMany(t => t.Groups)
                .SelectMany(g => g.Items)
                .Single(i => i.Text == text);
        }

        private static System.Collections.Generic.IEnumerable<string> RibbonToolNames(AppRibbon ribbon)
        {
            var names = ToolNames();
            return ribbon.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Items).Select(i => i.Text).Where(names.Contains);
        }

        private static string[] ToolNames()
        {
            return new[]
            {
                "File Manager",
                "Screenshots & Videos",
                "GoldHEN Manager",
                "Frame4 Debugger",
                "PS4 Health",
                "PRX Manager",
                "Backup Timeline",
                "PKG Manager",
                "Luma Studio",
                "Mod Library", "Games"
            };
        }

        private static void OpenToolForTests(DashboardForm form, string key)
        {
            typeof(DashboardForm)
                .GetMethod("OpenTool", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { key });
        }

        private static System.Collections.Generic.IEnumerable<UiLabel> FindLabels(Control root)
        {
            foreach (Control child in root.Controls)
            {
                var label = child as UiLabel;
                if (label != null) yield return label;
                foreach (var nested in FindLabels(child)) yield return nested;
            }
        }

        private sealed class NoopSessionFactory : IManagerSessionFactory
        {
            public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode)
            {
                throw new System.NotSupportedException();
            }
        }
    }
}

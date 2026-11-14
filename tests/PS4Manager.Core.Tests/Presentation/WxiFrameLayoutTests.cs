using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.UI;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class WxiFrameLayoutTests
    {
        [Theory]
        [InlineData(10f, AppTheme.Light)]
        [InlineData(10f, AppTheme.Dark)]
        [InlineData(14f, AppTheme.Light)]
        [InlineData(14f, AppTheme.Dark)]
        public void Prx_actions_paths_and_modules_stay_visible_through_resize(float points, string theme)
        {
            StaTest.Run(() => {
                var previousTheme = AppTheme.Current;
                try {
                    AppTheme.Current = theme;
                    using (var form = new PrxManagerForm(new ManagerSettings { Ip = "" }, null))
                    using (var font = new Font("Segoe UI", points)) {
                        SetFont(form, font);
                        form.Show();
                        Application.DoEvents();
                        var normalSize = form.Size;
                        foreach (var size in new[] { normalSize, form.MinimumSize, normalSize }) {
                            form.Size = size;
                            Application.DoEvents();
                            AssertPrxLayout(form);
                        }
                    }
                } finally {
                    AppTheme.Current = previousTheme;
                }
            });
        }

        [Fact]
        public void Prx_file_actions_wrap_at_narrow_width_and_rejoin_when_widened()
        {
            StaTest.Run(() => {
                using (var form = new PrxManagerForm(new ManagerSettings { Ip = "" }, null))
                using (var font = new Font("Segoe UI", 20f)) {
                    SetFont(form, font);
                    form.Show();
                    var upload = Field<UiButton>(form, "_upload");
                    var load = Field<UiButton>(form, "_load");
                    form.Size = new Size(640, 480);
                    Application.DoEvents();
                    Assert.True(load.Top > upload.Top, "File actions should wrap instead of running beyond the pane.");
                    AssertPrxLayout(form);
                    form.Width = 1000;
                    Application.DoEvents();
                    Assert.Equal(upload.Top, load.Top);
                    AssertPrxLayout(form);
                }
            });
        }

        private static void AssertPrxLayout(PrxManagerForm form)
        {
            var toolbar = Field<Control>(form, "_toolbar");
            var refresh = Field<UiButton>(form, "_refresh");
            Assert.True(toolbar.Height < refresh.GetPreferredSize(Size.Empty).Height + 32,
                "PRX toolbar must follow the action height instead of the default 100px panel height.");

            foreach (var name in new[] { "_refresh", "_list", "_browse", "_upload", "_deployLoad", "_load", "_unload" }) {
                var button = Field<UiButton>(form, name);
                AssertFits(button);
                AssertFullyVisible(button, button.Text);
            }

            foreach (var name in new[] { "_remotePath", "_localPrx" }) {
                var path = Field<UiTextBox>(form, name);
                AssertSpansCell(path);
                AssertFullyVisible(path, path.PlaceholderText);
            }

            var modules = Field<AppGrid>(form, "_modules");
            Assert.True(modules.ClientSize.Height >= DpiScale.Scale(48, form.DeviceDpi),
                "The module grid must have room for its header and a row.");
            AssertFullyVisible(modules, "Module grid");
            foreach (var label in Descendants(form).OfType<UiLabel>().Where(l => l.AutoSize && l.Parent is TableLayoutPanel)) AssertLabelFitsCell(label);
        }

        private static void AssertFullyVisible(Control control, string name)
        {
            Assert.True(control.Visible, name + " is hidden.");
            var bounds = control.Parent.RectangleToScreen(control.Bounds);
            for (var parent = control.Parent; parent != null; parent = parent.Parent) {
                var viewport = parent.RectangleToScreen(parent.ClientRectangle);
                Assert.True(viewport.Contains(bounds),
                    name + " is clipped by " + parent.GetType().Name + ": " + bounds + " exceeds " + viewport);
            }
        }

        [Theory]
        [InlineData(10f, false)]
        [InlineData(14f, true)]
        public void Frame4_tabs_keep_actions_uncropped_and_inputs_stretched(float points, bool minimum)
        {
            StaTest.Run(() => {
                using (var form = new Frame4Form(new ManagerSettings { Ip = "", ExpertMode = true }))
                using (var font = new Font("Segoe UI", points)) {
                    SetFont(form, font);
                    form.Show();
                    if (minimum) form.Size = form.MinimumSize;
                    var tabs = Field<UiTabs>(form, "_tabs");
                    for (var i = 0; i < tabs.TabPages.Count; i++) {
                        tabs.SelectedTabPageIndex = i;
                        Application.DoEvents();
                        foreach (var button in Descendants(tabs.TabPages[i]).OfType<UiButton>()) AssertFits(button);
                        foreach (var label in Descendants(tabs.TabPages[i]).OfType<UiLabel>().Where(l => l.AutoSize && l.Parent is TableLayoutPanel)) AssertLabelFitsCell(label);
                        foreach (var field in Descendants(tabs.TabPages[i]).OfType<UiTextBox>().Where(f => !f.Multiline && f.Parent is TableLayoutPanel))
                            AssertSpansCell(field);
                    }
                }
            });
        }

        private static void SetFont(Form form, Font font)
        {
            form.Font = font;
            foreach (var control in Descendants(form)) {
                var property = control.GetType().GetProperties().FirstOrDefault(p => p.Name == "Appearance" && typeof(DevExpress.Utils.AppearanceObject).IsAssignableFrom(p.PropertyType));
                var appearance = property?.GetValue(control) as DevExpress.Utils.AppearanceObject;
                if (appearance == null) continue;
                appearance.Font = font;
                appearance.Options.UseFont = true;
            }
        }

        private static void AssertFits(UiButton button)
        {
            var preferred = button.GetPreferredSize(Size.Empty);
            Assert.True(button.Width >= preferred.Width && button.Height >= preferred.Height,
                button.Text + " is cropped: " + button.Size + ", needs " + preferred);
            Assert.True(button.Parent.DisplayRectangle.Contains(button.Bounds), button.Text + " extends outside its action row.");
        }

        private static void AssertLabelFitsCell(UiLabel label)
        {
            var table = (TableLayoutPanel)label.Parent;
            var column = table.GetPositionFromControl(label).Column;
            var width = table.GetColumnWidths().Skip(column).Take(table.GetColumnSpan(label)).Sum();
            Assert.True(label.Width + label.Margin.Horizontal <= width, label.Text + " overlaps the next table column: " + label.Width + " in " + width + "; min=" + label.MinimumSize + "; preferred=" + label.GetPreferredSize(Size.Empty) + "; best=" + label.CalcBestSize());
        }

        private static void AssertSpansCell(UiTextBox field)
        {
            var table = (TableLayoutPanel)field.Parent;
            var position = table.GetPositionFromControl(field);
            var widths = table.GetColumnWidths();
            var available = widths.Skip(position.Column).Take(table.GetColumnSpan(field)).Sum() - field.Margin.Horizontal;
            Assert.True(field.Width >= available - 2, "Input width " + field.Width + " does not fill its " + available + "px table cell.");
            Assert.True(field.Height >= field.GetPreferredSize(Size.Empty).Height, "Input is vertically cropped.");
        }

        private static T Field<T>(object instance, string name) where T : Control
            => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls) {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}

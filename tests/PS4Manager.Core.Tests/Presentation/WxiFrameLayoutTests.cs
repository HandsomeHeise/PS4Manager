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
        [InlineData(10f, false)]
        [InlineData(14f, true)]
        public void Prx_toolbar_and_actions_fit_their_text_and_path_uses_available_width(float points, bool minimum)
        {
            StaTest.Run(() => {
                using (var form = new PrxManagerForm(new ManagerSettings { Ip = "" }, null))
                using (var font = new Font("Segoe UI", points)) {
                    SetFont(form, font);
                    form.Show();
                    if (minimum) form.Size = form.MinimumSize;
                    Application.DoEvents();
                    var toolbar = Field<Control>(form, "_toolbar");
                    var refresh = Field<UiButton>(form, "_refresh");
                    Assert.True(toolbar.Height < refresh.GetPreferredSize(Size.Empty).Height + 32,
                        "PRX toolbar must follow the action height instead of the default 100px panel height.");
                    foreach (var name in new[] { "_list", "_browse", "_upload", "_deployLoad", "_load", "_unload" })
                        AssertFits(Field<UiButton>(form, name));
                    AssertSpansCell(Field<UiTextBox>(form, "_remotePath"));
                    AssertSpansCell(Field<UiTextBox>(form, "_localPrx"));
                    foreach (var label in Descendants(form).OfType<UiLabel>().Where(l => l.AutoSize && l.Parent is TableLayoutPanel)) AssertLabelFitsCell(label);
                }
            });
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

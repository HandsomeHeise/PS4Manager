using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.Utils;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.UI;
using PS4Manager.App;
using PS4Manager.Core.Config;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class WxiSharedLayoutTests
    {
        [Theory] [InlineData(ContentAlignment.TopRight, VertAlignment.Top)] [InlineData(ContentAlignment.BottomLeft, VertAlignment.Bottom)]
        public void Labels_honor_vertical_alignment_and_report_the_requested_value(ContentAlignment alignment, VertAlignment vertical)
        {
            StaTest.Run(() => { using (var label = new UiLabel { AutoSize = false, TextAlignment = alignment }) {
                Assert.Equal(alignment, label.TextAlignment); Assert.Equal(vertical, label.Appearance.TextOptions.VAlignment);
            } });
        }
        [Fact] public void Form_table_labels_and_fields_align_and_use_the_available_row_width()
        {
            StaTest.Run(() => {
                using (var form = new AppWindow { ClientSize = new Size(700, 180) })
                using (var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }) {
                    table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                    var label = new UiLabel { Text = "Script directory" }; var field = new UiTextBox { Text = "/data/gtalua/scripts" };
                    table.Controls.Add(label, 0, 0); table.Controls.Add(field, 1, 0); form.Controls.Add(table); form.Show(); Application.DoEvents();
                    Assert.InRange(Math.Abs(label.Top + label.Height / 2 - field.Top - field.Height / 2), 0, 2);
                    Assert.True(field.Width > 500, "Inputs should fill their available table column rather than keeping a 100px default.");
                    Assert.False(field.Multiline); field.Multiline = true; Assert.True(field.Multiline); Assert.Contains("gtalua", field.Text);
                }
            });
        }
        [Fact] public void Large_button_text_has_enough_preferred_height_and_width()
        {
            StaTest.Run(() => { using (var button = new UiButton { Text = "Upload selected scripts" }) using (var font = new Font("Segoe UI", 22)) {
                button.Font = font; var text = TextRenderer.MeasureText(button.Text, button.Font);
                Assert.True(button.GetPreferredSize(Size.Empty).Height >= text.Height + 10);
                Assert.True(button.GetPreferredSize(Size.Empty).Width >= text.Width + 16);
            } });
        }
        [Theory] [InlineData(9)] [InlineData(13.5f)]
        public void Auto_sized_checkboxes_fit_the_complete_caption(float fontSize)
        {
            StaTest.Run(() => {
                using (var form = new AppWindow()) using (var font = new Font("Segoe UI", fontSize)) {
                    var check = new UiCheckBox { Text = "Only installed games", AutoSize = true, Font = font };
                    form.Controls.Add(check); form.Show(); Application.DoEvents();
                    Assert.True(check.Width >= TextRenderer.MeasureText(check.Text, check.Font).Width + 24, "The checkbox caption must not use CheckEdit's default 75px width.");
                    Assert.True(check.Height >= check.GetPreferredSize(Size.Empty).Height);
                }
            });
        }
        [Fact] public void Text_editor_footer_contains_actions_and_separates_status()
        {
            StaTest.Run(() => {
                using (var form = new FileManager.App.Dialogs.IniEditorDialog("plugins.ini", "", text => System.Threading.Tasks.Task.FromResult(true))) {
                    form.Show(); Application.DoEvents();
                    var footer = form.Controls.OfType<UiPanel>().Single(p => p.Dock == DockStyle.Bottom);
                    foreach (var button in footer.Controls.OfType<UiButton>()) Assert.True(footer.DisplayRectangle.Contains(button.Bounds), "Editor footer crops its actions.");
                    form.Size = form.MinimumSize; Application.DoEvents();
                    var status = footer.Controls.OfType<UiLabel>().Single();
                    status.Text = "Error: a long save failure must not cover the actions or grow over the editor.";
                    Application.DoEvents();
                    Assert.True(footer.DisplayRectangle.Contains(status.Bounds));
                    foreach (var button in footer.Controls.OfType<UiButton>()) Assert.False(status.Bounds.IntersectsWith(button.Bounds));
                }
            });
        }
        [Fact] public void Legacy_absolute_table_rows_expand_to_hold_native_editor_and_button_text()
        {
            StaTest.Run(() => {
                using (var form = new AppWindow { ClientSize = new Size(600, 240) })
                using (var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 }) {
                    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                    var action = new UiButton { AutoSize = true, Text = "Upload selected" }; var field = new UiTextBox { Text = "main.lua" };
                    table.Controls.Add(action, 0, 0); table.Controls.Add(field, 1, 0); form.Controls.Add(table); form.Show(); Application.DoEvents();
                    Assert.True(action.Height >= action.GetPreferredSize(Size.Empty).Height);
                    Assert.True(field.Height >= field.GetPreferredSize(Size.Empty).Height);
                }
            });
        }
        [Theory] [InlineData("health")] [InlineData("backup")] [InlineData("media")]
        public void Utility_toolbars_align_actions_and_status_without_giant_or_cropped_buttons(string key)
        {
            StaTest.Run(() => {
                using (var dashboard = new DashboardForm(new ManagerSettings()))
                using (var form = (Form)typeof(DashboardForm).GetMethod("BuildTool", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dashboard, new object[] { key })) {
                    form.Show(); Application.DoEvents();
                    var toolbar = form.Controls.OfType<UiPanel>().Single(p => p.Dock == DockStyle.Top);
                    foreach (var button in toolbar.Controls.OfType<UiButton>()) {
                        Assert.True(button.Height >= button.GetPreferredSize(Size.Empty).Height);
                        Assert.True(toolbar.DisplayRectangle.Contains(button.Bounds), "Button is outside the toolbar content area.");
                        Assert.True(toolbar.Height <= button.GetPreferredSize(Size.Empty).Height + 24, "Toolbar should not use the default 100px panel height.");
                    }
                }
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraTab;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Luma.Studio;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.UI;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class LumaStudioDesignTests
    {
        [Theory]
        [InlineData(1040, 680)]
        [InlineData(1380, 850)]
        [InlineData(1920, 1000)]
        public void Reference_layout_keeps_code_wide_and_tools_below_without_hiding_actions(int width, int height)
        {
            StaTest.Run(() => {
                using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                    form.ClientSize = new Size(width, height); form.Show(); Application.DoEvents();
                    Assert.True(form.ActiveEditor.Width > form.ClientSize.Width * .70, "API help must not consume a permanent right column: editor=" + form.ActiveEditor.Width + ", client=" + form.ClientSize.Width);
                    var tools = All(form).OfType<XtraTabControl>().Single(t => t.Name == "studio.tools");
                    Assert.Equal(new[] { "OUTPUT", "PROBLEMS", "LIVE DIAGNOSTICS", "SEARCH", "NAVIGATION", "API", "PROJECT SYNC" }, tools.TabPages.Select(p => p.Text));
                    Assert.Equal("OUTPUT", tools.SelectedTabPage.Text);
                    Assert.True(tools.PointToScreen(Point.Empty).Y >= form.ActiveEditor.PointToScreen(Point.Empty).Y + form.ActiveEditor.Height);
                    Assert.Contains("Execute", form.CommandNames); Assert.Contains("Upload", form.CommandNames); Assert.Contains("Reload", form.CommandNames);
                    form.DiscardAllForTest();
                }
            });
        }

        [Fact]
        public void Dark_palette_styles_the_real_Lua_editor_without_changing_the_global_theme()
        {
            StaTest.Run(() => {
                string original = AppTheme.Current;
                try {
                    AppTheme.Current = AppTheme.Dark;
                    using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                        form.Show(); Application.DoEvents();
                        Assert.Equal(AppTheme.Dark, AppTheme.Current);
                        Assert.Equal(AppTheme.Surface, form.ActiveEditor.Styles[ScintillaNET.Style.Default].BackColor);
                        Assert.Equal(Color.FromArgb(199, 146, 234), form.ActiveEditor.Styles[ScintillaNET.Style.Lua.Word].ForeColor);
                        Assert.Equal(Color.FromArgb(247, 198, 108), form.ActiveEditor.Styles[ScintillaNET.Style.Lua.Number].ForeColor);
                        form.DiscardAllForTest();
                    }
                } finally { AppTheme.Current = original; }
            });
        }

        [Fact]
        public void Footer_tracks_caret_and_navigation_switches_real_document_buffers()
        {
            StaTest.Run(() => {
                using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                    form.Show();
                    form.ActiveEditor.Text = "local value = 1\nreturn value";
                    form.ActiveEditor.GotoPosition(form.ActiveEditor.Lines[1].Position + 3); Application.DoEvents();
                    var position = All(form).Single(c => c.AccessibleName == "Editor position");
                    Assert.Contains("Ln 2, Col 4", position.Text);
                    Assert.Contains("UTF-8", position.Text);
                    var original = form.ActiveDocument;
                    form.AddDocument(new StudioDocument("other.lua", "return 2"));
                    var navigation = All(form).OfType<DevExpress.XtraEditors.ListBoxControl>().Single(c => c.Name == "studio.navigation");
                    Assert.Equal(2, navigation.ItemCount);
                    navigation.SelectedIndex = 0;
                    typeof(Control).GetMethod("OnDoubleClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(navigation, new object[] { EventArgs.Empty });
                    Assert.Equal(original.Id, form.ActiveDocument.Id);
                    Assert.Equal("local value = 1\nreturn value", form.ActiveEditor.Text);
                    form.DiscardAllForTest();
                }
            });
        }
        private static IEnumerable<Control> All(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; }
        }
        [Fact]
        public void Command_buttons_leave_room_for_larger_accessibility_fonts()
        {
            StaTest.Run(() => {
                using (var form = new LumaStudioForm(new ManagerSettings(), () => { }))
                using (var large = new Font("Segoe UI", 24F)) {
                    var save = All(form).OfType<UiButton>().Single(b => b.Text == "Save");
                    var previous = save.Font;
                    try {
                        save.Font = large;
                        Assert.True(save.GetPreferredSize(Size.Empty).Height >= TextRenderer.MeasureText(save.Text, large).Height + 8,
                            "Auto-sized commands must not clip scaled or larger text.");
                    } finally { save.Font = previous; }
                    form.DiscardAllForTest();
                }
            });
        }
    }
}

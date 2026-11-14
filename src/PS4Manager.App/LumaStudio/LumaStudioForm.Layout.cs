using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraTab;
using PS4Manager.UI;
using PS4Manager.Core.Luma.Studio;

namespace PS4Manager.App
{
    internal static class StudioColors
    {
        internal static Color Editor => AppTheme.Surface;
        internal static Color Text => AppTheme.Foreground;
        internal static Color Muted => AppTheme.IsLight ? Color.FromArgb(75, 89, 108) : Color.FromArgb(137, 154, 177);
        internal static Color Accent => AppTheme.IsLight ? AppTheme.Accent : Color.FromArgb(91, 173, 255);
        internal static Color Keyword => AppTheme.IsLight ? Color.FromArgb(117, 39, 152) : Color.FromArgb(199, 146, 234);
        internal static Color Number => AppTheme.IsLight ? Color.FromArgb(136, 70, 0) : Color.FromArgb(247, 198, 108);
        internal static Color Error => AppTheme.IsLight ? Color.FromArgb(169, 36, 35) : Color.FromArgb(244, 135, 113);
        internal static Color Success => AppTheme.IsLight ? Color.FromArgb(26, 103, 72) : Color.FromArgb(126, 218, 173);
    }

    internal sealed partial class LumaStudioForm
    {
        private readonly XtraTabControl tools = new XtraTabControl { Dock = DockStyle.Fill, Name = "studio.tools" };
        private readonly XtraTabPage searchPage = new XtraTabPage { Text = "SEARCH" };
        private readonly XtraTabPage apiPage = new XtraTabPage { Text = "API" };
        private readonly MemoEdit liveDetails = new MemoEdit { Dock = DockStyle.Fill };
        private readonly ListBoxControl navigation = new ListBoxControl { Dock = DockStyle.Fill, Name = "studio.navigation" };
        private readonly UiLabel projectTitle = new UiLabel { Text = "NO FOLDER OPEN", Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(10, 0, 0, 0) };
        private readonly UiLabel positionLabel = new UiLabel { Dock = DockStyle.Fill, AutoSize = false, AccessibleName = "Editor position", TextAlignment = ContentAlignment.MiddleRight };
        private readonly UiLabel liveLabel = new UiLabel { Dock = DockStyle.Fill, AutoSize = false, AccessibleName = "Live diagnostics", TextAlignment = ContentAlignment.MiddleRight };
        private UiSplit workspaceSplit, outputSplit;
        private readonly Bitmap documentGlyph = CreateDocumentGlyph();
        private sealed class StudioButton : UiButton
        {
            public override Size GetPreferredSize(Size proposedSize)
            {
                var measured = TextRenderer.MeasureText(Text, Font);
                return new Size(Math.Max(DpiScale.Scale(55, DeviceDpi), measured.Width + DpiScale.Scale(18, DeviceDpi)),
                    Math.Max(DpiScale.Scale(30, DeviceDpi), measured.Height + DpiScale.Scale(10, DeviceDpi)));
            }
        }
        private static Bitmap CreateDocumentGlyph()
        {
            var image = new Bitmap(18, 30);
            using (var graphics = Graphics.FromImage(image))
            using (var font = new Font("Consolas", 10F))
            using (var brush = new SolidBrush(AppTheme.Accent)) {
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("{}", font, brush, 0, 6);
            }
            return image;
        }

        private Control BuildReferenceWorkspace()
        {
            workspaceSplit = new UiSplit { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterPosition = 240, Panel1MinSize = 160, Panel2MinSize = 500, Padding = new Padding(0) };
            outputSplit = new UiSplit { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterPosition = 230, Panel1MinSize = 200, Panel2MinSize = 140 };
            var sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
            project.OptionsView.ShowColumns = false; project.OptionsView.ShowIndicator = false;
            project.OptionsView.ShowHorzLines = false; project.OptionsView.ShowVertLines = false;
            project.OptionsBehavior.Editable = false; project.Columns.AddVisible("File");
            project.BorderStyle = BorderStyles.Default;
            project.DoubleClick += (s, e) => { var path = project.FocusedNode?.Tag as string; if (path != null) OpenPath(path); };
            var treeFrame = new Panel { Dock = DockStyle.Fill };
            treeFrame.Controls.Add(project);
            var explorerHint = new UiLabel { Text = "SOURCE EXPLORER\r\nDouble-click a Lua script to open it", Dock = DockStyle.Bottom, AutoSize = false, Height = 62, Padding = new Padding(12, 6, 0, 0) };
            sidebar.Controls.Add(treeFrame); sidebar.Controls.Add(explorerHint); sidebar.Controls.Add(projectTitle);
            workspaceSplit.Panel1.Controls.Add(sidebar); workspaceSplit.Panel2.Controls.Add(outputSplit);
            outputSplit.Panel1.Controls.Add(documents); outputSplit.Panel2.Controls.Add(tools);
            tools.TabPages.Add(new XtraTabPage { Text = "OUTPUT", Controls = { Inset(console) } });
            tools.TabPages.Add(new XtraTabPage { Text = "PROBLEMS", Controls = { Inset(diagnostics) } });
            tools.TabPages.Add(new XtraTabPage { Text = "LIVE DIAGNOSTICS", Controls = { Inset(liveDetails) } });
            tools.TabPages.Add(searchPage);
            tools.TabPages.Add(new XtraTabPage { Text = "NAVIGATION", Controls = { Inset(navigation) } });
            tools.TabPages.Add(apiPage); apiPage.Controls.Add(Inset(help));
            var searchBar = Bar(); searchBar.Dock = DockStyle.Top; searchBar.Height = 42; searchBar.Controls.Add(find);
            find.Width = 340; find.Properties.NullValuePrompt = "Find in current Lua document";
            AddButton(searchBar, "Next", FindNext); searchPage.Controls.Add(searchBar);
            searchPage.Controls.Add(new UiLabel { Text = "Search wraps around the current document. Ctrl+F opens this panel.", Dock = DockStyle.Bottom, Height = 38, AutoSize = false, Padding = new Padding(12) });
            navigation.DoubleClick += (s, e) => {
                var item = navigation.SelectedItem as NavigationItem;
                Buffer buffer;
                if (item != null && buffers.TryGetValue(item.Id, out buffer)) { documents.SelectedTabPage = buffer.Page; buffer.Editor.Focus(); }
            };
            foreach (var memo in new[] { help, console, liveDetails }) { memo.Properties.ReadOnly = true; memo.Properties.UseAdvancedMode = DefaultBoolean.True; memo.BorderStyle = BorderStyles.NoBorder; }
            help.Properties.WordWrap = true; console.Properties.WordWrap = false;
            diagnostics.BorderStyle = navigation.BorderStyle = BorderStyles.NoBorder;
            logLines.Enqueue("Luma Studio · Lua 5.4");
            logLines.Enqueue("Open a Lua project or choose a template, then press F5 to check syntax locally.");
            logLines.Enqueue("Ctrl+Space: completion    Ctrl+F: find    Ctrl+Shift+O: open folder");
            console.Text = string.Join("\r\n", logLines);
            ConfigureTabs(documents); ConfigureTabs(tools);
            return workspaceSplit;
        }
        private Control BuildReferenceFooter()
        {
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 28, ColumnCount = 3, RowCount = 1, Padding = new Padding(12, 0, 12, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            footer.Controls.Add(status, 0, 0); footer.Controls.Add(positionLabel, 1, 0); footer.Controls.Add(liveLabel, 2, 0);
            return footer;
        }
        private static Control Inset(Control child)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            panel.Controls.Add(child); return panel;
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            workspaceSplit.SplitterPosition = Math.Max(180, Math.Min(330, (int)(ClientSize.Width * .175)));
            outputSplit.SplitterPosition = Math.Max(170, Math.Min(280, (int)(ClientSize.Height * .28)));
            RefreshEditorChrome();
        }
        private void ApplyStudioTheme()
        {
            // Only the plain WinForms containers need explicit colors.
            // DevExpress controls inherit the application's WXI palette.
            foreach (var panel in Descendants(this).OfType<Panel>()) panel.BackColor = AppTheme.Background;
            foreach (var buffer in buffers.Values) StyleEditor(buffer.Editor);
            UpdateLiveStatus();
        }
        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        private static void ConfigureTabs(XtraTabControl tabs)
        {
            tabs.HeaderAutoFill = DefaultBoolean.False;
            tabs.ShowHeaderFocus = DefaultBoolean.False;
            tabs.HeaderButtons = TabButtons.Prev | TabButtons.Next;
            tabs.HeaderButtonsShowMode = TabButtonShowMode.WhenNeeded;
        }
        private sealed class NavigationItem
        {
            internal Guid Id;
            internal string Title;
            public override string ToString() => Title;
        }
        private void RefreshEditorChrome()
        {
            if (ActiveBuffer != null) ActiveBuffer.Breadcrumb.Text = ActiveDocument.FilePath ?? ActiveDocument.Title + " · unsaved";
            navigation.Items.BeginUpdate();
            try {
                navigation.Items.Clear();
                foreach (var buffer in buffers.Values) {
                    navigation.Items.Add(new NavigationItem { Id = buffer.Document.Id, Title = buffer.Document.Title + (buffer.Document.IsDirty ? " *" : "") });
                    if (buffer == ActiveBuffer) navigation.SelectedIndex = navigation.Items.Count - 1;
                }
            } finally { navigation.Items.EndUpdate(); }
            UpdateCaretStatus(); UpdateLiveStatus();
        }
        private void UpdateCaretStatus()
        {
            var editor = ActiveEditor;
            positionLabel.Text = editor == null ? "UTF-8  |  Lua 5.4" : "Ln " + (editor.LineFromPosition(editor.CurrentPosition) + 1) + ", Col " + (editor.GetColumn(editor.CurrentPosition) + 1) + "  |  UTF-8  |  Lua 5.4";
        }
        private void UpdateLiveStatus()
        {
            var result = ActiveBuffer?.Check;
            int errors = result?.Diagnostics.Count(d => d.Severity == LuaDiagnosticSeverity.Error) ?? 0, warnings = result?.Diagnostics.Count(d => d.Severity == LuaDiagnosticSeverity.Warning) ?? 0;
            liveLabel.Text = ActiveBuffer == null ? "Live: no document" : result == null ? "Live: not checked" : "Live: " + errors + " errors, " + warnings + " hints";
            liveLabel.Appearance.ForeColor = result == null ? StudioColors.Muted : errors > 0 ? StudioColors.Error : warnings > 0 ? StudioColors.Number : StudioColors.Success;
            liveLabel.Appearance.Options.UseForeColor = true;
            liveDetails.Text = ActiveDocument == null ? "Open a Lua file to begin." :
                ActiveDocument.Title + "\r\n\r\n" + liveLabel.Text + "\r\nLua 5.4.7 · compile-only checks\r\n\r\n" +
                "Native catalog/name/argument-count hints are warnings only, not full Lua type/scope analysis. Checks never execute code or verify runtime availability.\r\n\r\n" +
                (CurrentDiagnostics.Count == 0 ? "No current diagnostic messages." : string.Join("\r\n", CurrentDiagnostics.Select(d => "Line " + d.Line + ": " + d.Message)));
        }
        private void FocusSearch() { tools.SelectedTabPage = searchPage; find.Focus(); find.SelectAll(); }
    }
}

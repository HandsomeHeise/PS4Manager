using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraTab;
using PS4Manager.Core.Luma.Studio;
using PS4Manager.UI;
using ScintillaNET;

namespace PS4Manager.App
{
    internal sealed partial class LumaStudioForm
    {
        internal void AddDocument(StudioDocument document)
        {
            var editor = new Scintilla { Dock = DockStyle.Fill, WrapMode = WrapMode.None, TabWidth = 4, IndentWidth = 4, UseTabs = false };
            editor.LexerName = "lua";
            editor.SetKeywords(0, "and break do else elseif end false for function goto if in local nil not or repeat return then true until while");
            editor.SetProperty("fold", "1"); editor.SetProperty("fold.compact", "0");
            editor.Margins[0].Type = MarginType.Number; editor.Margins[0].Width = 56;
            editor.Margins[1].Type = MarginType.Symbol; editor.Margins[1].Mask = Marker.MaskFolders;
            editor.Margins[1].Width = 18; editor.Margins[1].Sensitive = true;
            editor.AutomaticFold = AutomaticFold.Show | AutomaticFold.Click | AutomaticFold.Change;
            for (int i = Marker.FolderEnd; i <= Marker.FolderOpen; i++) { editor.Markers[i].SetBackColor(Color.FromArgb(100, 110, 130)); editor.Markers[i].SetForeColor(AppTheme.Background); }
            editor.Markers[Marker.Folder].Symbol = MarkerSymbol.BoxPlus;
            editor.Markers[Marker.FolderOpen].Symbol = MarkerSymbol.BoxMinus;
            editor.WordChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.:";
            editor.AutoCIgnoreCase = true; editor.AutoCMaxHeight = 12;
            editor.Text = document.Source;
            var buffer = new Buffer { Document = document, Editor = editor, Page = new XtraTabPage { Text = document.Title, Tooltip = document.FilePath ?? "Unsaved buffer" } };
            buffer.Page.Tag = buffer; buffer.Page.Controls.Add(editor); buffers.Add(document.Id, buffer);
            StyleEditor(editor);
            editor.TextChanged += (s, e) => {
                document.Source = editor.Text; buffer.Check = null; buffer.CheckedVersion = -1;
                buffer.Page.Text = document.Title + (document.IsDirty ? " *" : "");
                if (buffer == ActiveBuffer) { ShowDiagnostics(); checkTimer.Stop(); checkTimer.Start(); }
            };
            editor.CharAdded += (s, e) => {
                if (e.Char == '\n') {
                    int line = editor.LineFromPosition(editor.CurrentPosition);
                    if (line > 0) { editor.Lines[line].Indentation = editor.Lines[line - 1].Indentation; editor.GotoPosition(editor.Lines[line].EndPosition); }
                } else if (e.Char == '(') ShowCallTip(editor);
                else if (char.IsLetter((char)e.Char) || e.Char == '_' || e.Char == '.') Complete(editor, false);
                else if (e.Char == ')') editor.CallTipCancel();
            };
            editor.KeyDown += (s, e) => {
                if (e.Control && e.KeyCode == Keys.Space) { Complete(editor, true); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.F1) { ShowSymbolHelp(editor); e.SuppressKeyPress = true; }
            };
            editor.UpdateUI += (s, e) => { if (buffer == ActiveBuffer && !busy) ShowSymbolHelp(editor, false); };
            documents.TabPages.Add(buffer.Page); documents.SelectedTabPage = buffer.Page;
            buffer.Page.Text = document.Title + (document.IsDirty ? " *" : "");
            editor.Focus(); UpdateCommands();
        }
        private static void StyleEditor(Scintilla editor)
        {
            editor.Styles[Style.Default].Font = "Consolas"; editor.Styles[Style.Default].Size = 11;
            editor.Styles[Style.Default].BackColor = AppTheme.Background; editor.Styles[Style.Default].ForeColor = AppTheme.Foreground;
            editor.StyleClearAll();
            editor.Styles[Style.LineNumber].ForeColor = Color.FromArgb(130, 143, 160);
            editor.Styles[Style.LineNumber].BackColor = AppTheme.Background;
            editor.Styles[Style.Lua.Word].ForeColor = AppTheme.IsLight ? Color.RoyalBlue : Color.FromArgb(99, 182, 255);
            foreach (int id in new[] { Style.Lua.String, Style.Lua.Character, Style.Lua.LiteralString }) editor.Styles[id].ForeColor = AppTheme.IsLight ? Color.Brown : Color.FromArgb(224, 184, 139);
            foreach (int id in new[] { Style.Lua.Comment, Style.Lua.CommentLine, Style.Lua.CommentDoc }) editor.Styles[id].ForeColor = Color.FromArgb(103, 151, 112);
            editor.Styles[Style.Lua.Number].ForeColor = Color.FromArgb(167, 139, 250);
            editor.CaretForeColor = AppTheme.Foreground;
            editor.SelectionBackColor = Color.FromArgb(55, 84, 123);
            editor.SetFoldMarginColor(true, AppTheme.Background); editor.SetFoldMarginHighlightColor(true, AppTheme.Background);
        }
        private void OnThemeChanged(object sender, EventArgs e) { foreach (var buffer in buffers.Values) StyleEditor(buffer.Editor); }
        private void RefreshProfile()
        {
            templates.Properties.Items.Clear(); templates.Properties.Items.AddRange(StudioTemplates.ForProfile(Profile).Cast<object>().ToArray()); templates.SelectedIndex = 0;
            help.Text = "LUMA STUDIO\r\n\r\n" + (Profile == "gta5" ? "GTA V · CUSA00411 · 1.57" : "Red Dead Redemption 2 · CUSA03041 · 1.32") +
                "\r\n\r\nLua 5.4 · compile-only checks\r\n\r\nCtrl+Space: completion\r\nF1: API help\r\nF5: syntax check\r\nCtrl+F: find\r\nCtrl+W: close tab\r\n\r\nDefinitions are from Luma's runtime and native API. Syntax checks do not verify native argument types or availability in the running game.\r\n\r\nExecute runs this buffer once. Upload copies a Lua file only. Reload restarts the game's scripts separately.";
        }
        private void Complete(Scintilla editor, bool explicitRequest)
        {
            int end = editor.CurrentPosition, start = editor.WordStartPosition(end, true);
            string prefix = editor.GetTextRange(start, end - start);
            if (!explicitRequest && prefix.Length < 2) return;
            var matches = ApiCatalog.Complete(prefix);
            if (matches.Count > 0) editor.AutoCShow(end - start, string.Join(" ", matches.Select(m => m.Name)));
        }
        private void ShowCallTip(Scintilla editor)
        {
            int end = editor.CurrentPosition - 1, start = editor.WordStartPosition(end, true);
            var symbol = ApiCatalog.Find(editor.GetTextRange(start, end - start));
            if (symbol != null) { editor.CallTipShow(start, symbol.Signature); help.Text = symbol.Signature + "\r\n\r\n" + symbol.Documentation; }
        }
        private void ShowSymbolHelp(Scintilla editor, bool reportMissing = true)
        {
            int start = editor.WordStartPosition(editor.CurrentPosition, true), end = editor.WordEndPosition(editor.CurrentPosition, true);
            var symbol = ApiCatalog.Find(editor.GetTextRange(start, end - start));
            if (symbol != null) help.Text = symbol.Signature + "\r\n\r\n" + symbol.Documentation;
            else if (reportMissing) status.Text = "No API documentation for the symbol at the caret.";
        }
        internal async Task CheckDocumentAsync()
        {
            var buffer = ActiveBuffer;
            if (buffer == null || closing) return;
            if (checking) { checkTimer.Start(); return; }
            string source = buffer.Document.Source, name = buffer.Document.FilePath ?? buffer.Document.Title;
            long version = buffer.Document.Version; checking = true;
            try {
                var result = await Task.Run(() => checker.Check(source, name));
                if (closing || !buffers.ContainsKey(buffer.Document.Id) || version != buffer.Document.Version) return;
                buffer.Check = result; buffer.CheckedVersion = version;
                if (ActiveBuffer == buffer) { ShowDiagnostics(); status.Text = result.Success ? "Lua 5.4 · No syntax errors. This does not validate runtime behavior." : result.Available ? "Syntax errors found · double-click a diagnostic." : "Lua checker unavailable · Execute and Upload are blocked."; }
            } catch (Exception ex) { if (!closing) status.Text = "Check failed: " + ex.Message; }
            finally { checking = false; }
        }
        private void ShowDiagnostics()
        {
            diagnostics.Items.Clear();
            foreach (var diagnostic in CurrentDiagnostics) diagnostics.Items.Add(diagnostic.FilePath + ":" + diagnostic.Line + "  " + diagnostic.Message);
        }
        internal void NavigateDiagnostic(int index)
        {
            if (index < 0 || index >= CurrentDiagnostics.Count || ActiveBuffer.CheckedVersion != ActiveDocument.Version) return;
            int line = Math.Min(ActiveEditor.Lines.Count - 1, CurrentDiagnostics[index].Line - 1);
            ActiveEditor.GotoPosition(ActiveEditor.Lines[line].Position); ActiveEditor.ScrollCaret(); ActiveEditor.Focus();
        }
        private void FindNext()
        {
            var editor = ActiveEditor; if (editor == null || find.Text.Length == 0) return;
            editor.TargetStart = editor.SelectionEnd; editor.TargetEnd = editor.TextLength; editor.SearchFlags = SearchFlags.None;
            if (editor.SearchInTarget(find.Text) < 0) { editor.TargetStart = 0; editor.TargetEnd = editor.TextLength; }
            if (editor.SearchInTarget(find.Text) >= 0) { editor.SetSelection(editor.TargetEnd, editor.TargetStart); editor.ScrollCaret(); editor.Focus(); }
            else status.Text = "Text not found.";
        }
    }
}

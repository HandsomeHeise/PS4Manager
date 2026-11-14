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
            var editor = new Scintilla { Dock = DockStyle.Fill, WrapMode = WrapMode.None, TabWidth = 4, IndentWidth = 4, UseTabs = false, BorderStyle = ScintillaNET.BorderStyle.None, ExtraAscent = 1, ExtraDescent = 1 };
            editor.LexerName = "lua";
            editor.SetKeywords(0, "and break do else elseif end false for function goto if in local nil not or repeat return then true until while");
            editor.SetProperty("fold", "1"); editor.SetProperty("fold.compact", "0");
            editor.Margins[0].Type = MarginType.Number; editor.Margins[0].Width = 48;
            editor.Margins[1].Type = MarginType.Symbol; editor.Margins[1].Mask = Marker.MaskFolders;
            editor.Margins[1].Width = 18; editor.Margins[1].Sensitive = true;
            editor.AutomaticFold = AutomaticFold.Show | AutomaticFold.Click | AutomaticFold.Change;
            editor.Markers[Marker.Folder].Symbol = MarkerSymbol.BoxPlus;
            editor.Markers[Marker.FolderOpen].Symbol = MarkerSymbol.BoxMinus;
            editor.WordChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.:";
            editor.AutoCIgnoreCase = true; editor.AutoCMaxHeight = 12;
            editor.AutoCSelectionChange += (s, e) => {
                var symbol = ApiCatalog.Find(e.Text);
                if (symbol != null) help.Text = symbol.Signature + "\r\n\r\n" + symbol.Documentation;
            };
            editor.Text = document.Source;
            var buffer = new Buffer { Document = document, Editor = editor, Page = new XtraTabPage { Text = document.Title, Tooltip = document.FilePath ?? "Unsaved buffer" } };
            buffer.Page.ImageOptions.Image = documentGlyph;
            buffer.Breadcrumb = new UiLabel { Text = document.FilePath ?? document.Title + " · unsaved", Dock = DockStyle.Top, AutoSize = false, Height = 36, Padding = new Padding(20, 0, 0, 0) };
            buffer.Page.Tag = buffer; buffer.Page.Controls.Add(editor); buffer.Page.Controls.Add(buffer.Breadcrumb); buffers.Add(document.Id, buffer);
            StyleEditor(editor);
            editor.TextChanged += (s, e) => {
                document.Source = editor.Text; buffer.Check = null; buffer.CheckedVersion = -1;
                ClearDiagnosticIndicators(editor);
                InvalidateProjectPreview();
                buffer.Page.Text = document.Title + (document.IsDirty ? " *" : "");
                if (buffer == ActiveBuffer) { ShowDiagnostics(); RefreshEditorChrome(); checkTimer.Stop(); checkTimer.Start(); }
            };
            editor.CharAdded += (s, e) => {
                if (e.Char == '\n') {
                    int line = editor.LineFromPosition(editor.CurrentPosition);
                    if (line > 0) { editor.Lines[line].Indentation = editor.Lines[line - 1].Indentation; editor.GotoPosition(editor.Lines[line].EndPosition); }
                } else if (e.Char == '(') ShowCallTip(editor);
                else if (char.IsLetter((char)e.Char) || e.Char == '_' || e.Char == '.') Complete(editor, false);
                else if (e.Char == ')' || e.Char == ',') ShowCallTip(editor);
            };
            editor.KeyDown += (s, e) => {
                if (e.Control && e.KeyCode == Keys.Space) { Complete(editor, true); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.F1) { ShowSymbolHelp(editor); e.SuppressKeyPress = true; }
            };
            editor.UpdateUI += (s, e) => { if (buffer == ActiveBuffer) { UpdateCaretStatus(); if (!busy) { ShowSymbolHelp(editor, false); UpdateSignature(buffer); } } };
            documents.TabPages.Add(buffer.Page); documents.SelectedTabPage = buffer.Page;
            buffer.Page.Text = document.Title + (document.IsDirty ? " *" : "");
            editor.Focus(); RefreshEditorChrome(); UpdateCommands();
        }
        private static void StyleEditor(Scintilla editor)
        {
            editor.Styles[Style.Default].Font = "Consolas"; editor.Styles[Style.Default].Size = 12;
            editor.Styles[Style.Default].BackColor = StudioColors.Editor; editor.Styles[Style.Default].ForeColor = StudioColors.Text;
            editor.StyleClearAll();
            editor.Styles[Style.LineNumber].ForeColor = StudioColors.Muted;
            editor.Styles[Style.LineNumber].BackColor = StudioColors.Editor;
            editor.Styles[Style.Lua.Word].ForeColor = StudioColors.Keyword; editor.Styles[Style.Lua.Word].Bold = true;
            editor.Styles[Style.Lua.Word2].ForeColor = StudioColors.Accent;
            foreach (int id in new[] { Style.Lua.String, Style.Lua.Character, Style.Lua.LiteralString }) editor.Styles[id].ForeColor = AppTheme.IsLight ? Color.FromArgb(32, 103, 50) : Color.FromArgb(195, 218, 157);
            foreach (int id in new[] { Style.Lua.Comment, Style.Lua.CommentLine, Style.Lua.CommentDoc }) editor.Styles[id].ForeColor = AppTheme.IsLight ? Color.FromArgb(75, 101, 83) : Color.FromArgb(113, 159, 122);
            editor.Styles[Style.Lua.Number].ForeColor = StudioColors.Number;
            editor.CaretForeColor = StudioColors.Text;
            editor.SelectionBackColor = AppTheme.IsLight ? Color.FromArgb(222, 222, 222) : Color.FromArgb(66, 66, 66);
            editor.SetFoldMarginColor(true, StudioColors.Editor); editor.SetFoldMarginHighlightColor(true, StudioColors.Editor);
            for (int i = Marker.FolderEnd; i <= Marker.FolderOpen; i++) { editor.Markers[i].SetBackColor(StudioColors.Muted); editor.Markers[i].SetForeColor(StudioColors.Editor); }
            ConfigureDiagnosticIndicators(editor);
            StyleFunctionNames(editor);
        }
        private static void StyleFunctionNames(Scintilla editor)
        {
            var functions = System.Text.RegularExpressions.Regex.Matches(editor.Text, @"(?m)^\s*(?:local\s+)?function\s+([A-Za-z_][\w.:]*)")
                .Cast<System.Text.RegularExpressions.Match>().Take(256).Select(m => m.Groups[1].Value.Split('.', ':').Last());
            editor.SetKeywords(1, "print log pairs ipairs type tostring tonumber require assert error pcall " + string.Join(" ", functions));
        }
        private void OnThemeChanged(object sender, EventArgs e) => ApplyStudioTheme();
        private void RefreshProfile()
        {
            foreach (var buffer in buffers.Values) { buffer.Check = null; buffer.CheckedVersion = -1; buffer.AssistanceProfile = null; ClearDiagnosticIndicators(buffer.Editor); buffer.Editor.AutoCCancel(); buffer.Editor.CallTipCancel(); }
            templates.Properties.Items.Clear(); templates.Properties.Items.AddRange(StudioTemplates.ForProfile(Profile).Cast<object>().ToArray()); templates.SelectedIndex = 0;
            help.Text = "LUMA STUDIO\r\n\r\n" + (Profile == "gta5" ? "GTA V · CUSA00411 · 1.57" : "Red Dead Redemption 2 · CUSA03041 · 1.32") +
                "\r\n\r\nLua 5.4 · compile-only checks\r\n\r\nCtrl+Space: completion\r\nF1: API help\r\nF5: syntax check\r\nCtrl+F: find\r\nCtrl+W: close tab\r\n\r\nDefinitions are from Luma's runtime and native API. Syntax checks do not verify native argument types or availability in the running game.\r\n\r\nExecute runs this buffer once. Upload copies a Lua file only. Reload restarts the game's scripts separately.";
            if (ActiveBuffer != null) { ShowDiagnostics(); checkTimer.Stop(); checkTimer.Start(); }
        }
        private void Complete(Scintilla editor, bool explicitRequest)
        {
            int end = editor.CurrentPosition;
            var completion = LuaLanguageService.Complete(editor.Text, end, ApiCatalog);
            if (!explicitRequest && end - completion.Start < 2) return;
            if (completion.Items.Count > 0) editor.AutoCShow(end - completion.Start, string.Join(" ", completion.Items.Select(m => m.Name)));
            else editor.AutoCCancel();
        }
        private void ShowCallTip(Scintilla editor)
        {
            var tip = LuaLanguageService.SignatureAt(editor.Text, editor.CurrentPosition, ApiCatalog);
            if (tip == null) { editor.CallTipCancel(); return; }
            editor.CallTipShow(tip.Start, tip.Symbol.Signature);
            string signature = tip.Symbol.Signature; int first = signature.IndexOf('(') + 1;
            var parameters = signature.Substring(first).TrimEnd(')').Split(',');
            int parameter = Math.Min(tip.Parameter, parameters.Length - 1);
            int highlight = first + parameters.Take(parameter).Sum(p => p.Length + 1);
            editor.CallTipSetHlt(highlight, highlight + parameters[parameter].Length);
            help.Text = signature + "\r\n\r\n" + tip.Symbol.Documentation;
        }
        private void ShowSymbolHelp(Scintilla editor, bool reportMissing = true)
        {
            int start = editor.WordStartPosition(editor.CurrentPosition, true), end = editor.WordEndPosition(editor.CurrentPosition, true);
            var symbol = ApiCatalog.Find(editor.GetTextRange(start, end - start));
            if (symbol != null) { help.Text = symbol.Signature + "\r\n\r\n" + symbol.Documentation; if (reportMissing) tools.SelectedTabPage = apiPage; }
            else if (reportMissing) status.Text = "No API documentation for the symbol at the caret.";
        }
        internal async Task CheckDocumentAsync()
        {
            var buffer = ActiveBuffer;
            if (buffer == null || closing) return;
            if (checking) { checkTimer.Start(); return; }
            string source = buffer.Document.Source, name = buffer.Document.FilePath ?? buffer.Document.Title;
            string profile = Profile; var catalog = ApiCatalog;
            long version = buffer.Document.Version; checking = true;
            try {
                var result = await Task.Run(() => {
                    var syntax = checker.Check(source, name);
                    return syntax.Success ? new LuaCheckResult(syntax.Available, syntax.Diagnostics.Concat(LuaLanguageService.Analyze(source, name, catalog))) : syntax;
                });
                if (closing || !buffers.ContainsKey(buffer.Document.Id) || version != buffer.Document.Version || profile != Profile) return;
                buffer.Check = result; buffer.CheckedVersion = version;
                StyleFunctionNames(buffer.Editor);
                if (ActiveBuffer == buffer) { ShowDiagnostics(); status.Text = result.Success ? result.Diagnostics.Count == 0 ? "Lua 5.4 · No syntax errors. Runtime behavior is not verified." : "Syntax valid · " + result.Diagnostics.Count + " native hint(s). Warnings do not block execution/upload." : result.Available ? "Syntax errors found · double-click a diagnostic." : "Lua checker unavailable · Execute and Upload are blocked."; }
            } catch (Exception ex) { if (!closing) status.Text = "Check failed: " + ex.Message; }
            finally { checking = false; }
        }
        private void ShowDiagnostics()
        {
            diagnostics.Items.Clear();
            foreach (var diagnostic in CurrentDiagnostics) diagnostics.Items.Add(diagnostic.Severity + "  " + diagnostic.FilePath + ":" + diagnostic.Line + "  " + diagnostic.Message);
            if (ActiveBuffer != null) PaintDiagnosticIndicators(ActiveBuffer);
            UpdateLiveStatus();
        }
        internal void NavigateDiagnostic(int index)
        {
            if (index < 0 || index >= CurrentDiagnostics.Count || ActiveBuffer.CheckedVersion != ActiveDocument.Version) return;
            int line = Math.Min(ActiveEditor.Lines.Count - 1, CurrentDiagnostics[index].Line - 1);
            ActiveEditor.GotoPosition(CurrentDiagnostics[index].Start >= 0 ? Math.Min(ActiveEditor.TextLength, CurrentDiagnostics[index].Start) : ActiveEditor.Lines[line].Position); ActiveEditor.ScrollCaret(); ActiveEditor.Focus();
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

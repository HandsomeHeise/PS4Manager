using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList.Nodes;
using PS4Manager.Core.Luma.Studio;
using PS4Manager.UI;

namespace PS4Manager.App
{
    internal sealed partial class LumaStudioForm
    {
        private void OpenFiles()
        {
            using (var dialog = new OpenFileDialog { Filter = "Lua source (*.lua)|*.lua", Multiselect = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) foreach (string path in dialog.FileNames) OpenPath(path);
        }
        private void OpenPath(string path)
        {
            try {
                path = Path.GetFullPath(path);
                var existing = buffers.Values.FirstOrDefault(b => string.Equals(b.Document.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (existing != null) documents.SelectedTabPage = existing.Page;
                else AddDocument(StudioDocument.Load(path));
            } catch (Exception ex) { status.Text = "Open failed: " + ex.Message; }
        }
        private void OpenProject()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Open a local Lua project folder" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try {
                    projectRoot = Path.GetFullPath(dialog.SelectedPath);
                    var files = StudioProject.Files(projectRoot); project.BeginUpdate();
                    try {
                        project.Nodes.Clear();
                        var root = project.AppendNode(new object[] { Path.GetFileName(projectRoot) }, (TreeListNode)null);
                        var folders = new Dictionary<string, TreeListNode>(StringComparer.OrdinalIgnoreCase) { [projectRoot] = root };
                        foreach (string path in files) {
                            var stack = new Stack<string>(); string directory = Path.GetDirectoryName(path);
                            while (!folders.ContainsKey(directory)) { stack.Push(directory); directory = Path.GetDirectoryName(directory); }
                            while (stack.Count > 0) { string folder = stack.Pop(); folders[folder] = project.AppendNode(new object[] { Path.GetFileName(folder) }, folders[Path.GetDirectoryName(folder)]); }
                            project.AppendNode(new object[] { Path.GetFileName(path) }, folders[Path.GetDirectoryName(path)]).Tag = path;
                        }
                        root.Expanded = true;
                    } finally { project.EndUpdate(); }
                    status.Text = files.Length + " Lua files · double-click to open. Scan capped at 1,000 files / 5,000 entries / 12 levels; links/build folders excluded.";
                } catch (Exception ex) { status.Text = "Project failed: " + ex.Message; }
            }
        }
        private bool SaveDocument(Buffer buffer, bool saveAs)
        {
            if (buffer == null) return false;
            string path = buffer.Document.FilePath;
            bool approved = false;
            if (saveAs || path == null) {
                using (var dialog = new SaveFileDialog { Filter = "Lua source (*.lua)|*.lua", DefaultExt = "lua", AddExtension = true, FileName = buffer.Document.Title, OverwritePrompt = true }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    path = Path.GetFullPath(dialog.FileName);
                    var other = buffers.Values.FirstOrDefault(b => b != buffer && string.Equals(b.Document.FilePath, path, StringComparison.OrdinalIgnoreCase));
                    if (other != null) { status.Text = "This file is open in another tab. Choose a different filename."; return false; }
                    approved = File.Exists(path);
                }
            }
            try {
                try { buffer.Document.Save(path, approved); }
                catch (StudioFileConflictException) {
                    if (XtraMessageBox.Show(this, "This file changed or was deleted outside Luma Studio.\r\n\r\nReplace it with this editor buffer?\r\n" + path,
                        "External file change", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
                    buffer.Document.Save(path, true);
                }
                buffer.Page.Text = buffer.Document.Title; buffer.Page.Tooltip = buffer.Document.FilePath;
                status.Text = "Saved " + path; return true;
            } catch (Exception ex) { status.Text = "Save failed: " + ex.Message; return false; }
        }
        internal Func<StudioDocument, DialogResult> CloseDecision { get; set; }
        private DialogResult ConfirmClose(StudioDocument document) => CloseDecision != null ? CloseDecision(document) : XtraMessageBox.Show(this,
            "Save changes to " + document.Title + " before closing?", "Unsaved Lua source", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        internal bool TryCloseDocument(Guid id, Func<StudioDocument, DialogResult> confirm)
        {
            Buffer buffer; if (!buffers.TryGetValue(id, out buffer)) return true;
            if (buffer.Document.IsDirty) {
                var decision = confirm(buffer.Document);
                if (decision == DialogResult.Cancel || decision == DialogResult.Yes && !SaveDocument(buffer, false)) return false;
            }
            buffers.Remove(id); documents.TabPages.Remove(buffer.Page); buffer.Page.Dispose(); ShowDiagnostics(); UpdateCommands(); return true;
        }
        internal bool ApproveClosing()
        {
            if (!discardAll) {
                foreach (var buffer in buffers.Values.ToArray()) {
                    if (!buffer.Document.IsDirty) continue;
                    var decision = ConfirmClose(buffer.Document);
                    if (decision == DialogResult.Cancel || decision == DialogResult.Yes && !SaveDocument(buffer, false)) return false;
                }
            }
            return true;
        }
        internal void CommitCloseApproval() { discardAll = true; Enabled = false; }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ApproveClosing()) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { StopStudio(); base.OnFormClosed(e); }
        private void StopStudio()
        {
            if (closing) return;
            closing = true; checkTimer.Stop(); pollTimer.Stop(); lifetime.Cancel();
            connection.LogReceived -= OnStudioLog; connection.Dispose(); AppTheme.Changed -= OnThemeChanged;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { StopStudio(); checkTimer.Dispose(); pollTimer.Dispose(); lifetime.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

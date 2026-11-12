using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraTab;
using PS4Manager.Core.Luma.Studio;
using PS4Manager.UI;

namespace PS4Manager.App
{
    internal sealed partial class LumaStudioForm
    {
        private StudioWorkspace currentProject;
        private StudioProjectPreview projectPreview;
        private readonly ComboBoxEdit recentProjects = new ComboBoxEdit { Width = 260 };
        private readonly CheckedListBoxControl syncFiles = new CheckedListBoxControl { Dock = DockStyle.Fill, CheckOnClick = true, Name = "studio.syncFiles" };
        private readonly XtraTabPage syncPage = new XtraTabPage { Text = "PROJECT SYNC" };
        private readonly UiLabel syncSummary = new UiLabel { Dock = DockStyle.Top, AutoSize = false, Height = 48, Padding = new Padding(10, 4, 10, 0), Text = "Open a project and connect to Luma, then Compare project. Nothing uploads automatically." };
        internal StudioWorkspace CurrentProject => currentProject;

        private Control BuildProjectBar()
        {
            var bar = Bar();
            AddButton(bar, "New project", NewProject); AddButton(bar, "Save project", SaveProject);
            AddButton(bar, "Refresh project", () => TryProjectAction(() => { if (currentProject != null) LoadProject(currentProject.Root); }));
            recentProjects.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
            recentProjects.Properties.NullText = "Recent projects"; bar.Controls.Add(recentProjects);
            AddButton(bar, "Open recent", () => { if (recentProjects.SelectedItem is string path) TryProjectAction(() => LoadProject(path)); });
            AddButton(bar, "Compare project", async () => await CompareProjectAsync());
            var actions = Bar(); actions.Dock = DockStyle.Bottom; actions.Height = 38;
            AddButton(actions, "Upload selected", async () => await UploadSelectedAsync()); commands["Upload selected"].Name = "studio.uploadSelected";
            AddButton(actions, "Select changes", () => SetSyncSelection(true)); AddButton(actions, "Clear selection", () => SetSyncSelection(false));
            syncPage.Controls.Add(Inset(syncFiles)); syncPage.Controls.Add(syncSummary); syncPage.Controls.Add(actions); tools.TabPages.Add(syncPage);
            syncFiles.ItemCheck += (s, e) => UpdateProjectCommands();
            recentProjects.LookAndFeel.UseDefaultLookAndFeel = false; recentProjects.LookAndFeel.SetSkinStyle(AppTheme.SkinName);
            SetColors(recentProjects.Properties.Appearance, StudioColors.Editor, StudioColors.Text);
            syncFiles.LookAndFeel.UseDefaultLookAndFeel = false; syncFiles.LookAndFeel.SetSkinStyle(AppTheme.SkinName);
            SetColors(syncFiles.Appearance, StudioColors.Editor, StudioColors.Text);
            RefreshRecentProjects(); return bar;
        }
        private void NewProject()
        {
            if (busy) return;
            using (var folder = new FolderBrowserDialog { Description = "Choose a parent folder for the new Luma project" }) {
                if (folder.ShowDialog(this) != DialogResult.OK) return;
                string name = XtraInputBox.Show(this, "Project name (uses the currently selected game)", "New Luma project", "My Luma scripts");
                if (string.IsNullOrWhiteSpace(name)) return;
                TryProjectAction(() => { var created = StudioWorkspace.Create(folder.SelectedPath, name, Profile); LoadProject(created.Root); OpenPath(Path.Combine(created.Root, "main.lua")); });
            }
        }
        private void SaveProject() => TryProjectAction(() => {
            if (currentProject == null || busy) return;
            currentProject.Save(); status.Text = "Project metadata saved · save scripts separately with Ctrl+S.";
        });
        private void TryProjectAction(Action action) { try { action(); } catch (Exception ex) { status.Text = "Project failed: " + ex.Message; } }
        internal void LoadProject(string root)
        {
            if (busy) return;
            var opened = StudioWorkspace.Open(root, Profile);
            if (opened.Profile != Profile) { DisconnectStudio(); game.SelectedIndex = opened.Profile == "rdr2" ? 1 : 0; }
            currentProject = opened; projectRoot = opened.Root; InvalidateProjectPreview(); PopulateProjectTree();
            projectTitle.Text = opened.Name.ToUpperInvariant(); Text = "Luma Studio — " + opened.Name;
            settings.Luma.RecentProjects = new[] { opened.Root }.Concat(settings.Luma.RecentProjects ?? new System.Collections.Generic.List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            RefreshRecentProjects(); saveSettings(); UpdateCommands();
        }
        private void RefreshRecentProjects()
        {
            recentProjects.Properties.Items.Clear();
            recentProjects.Properties.Items.AddRange((settings.Luma.RecentProjects ?? new System.Collections.Generic.List<string>()).Take(10).Cast<object>().ToArray());
            if (recentProjects.Properties.Items.Count > 0) recentProjects.SelectedIndex = 0;
        }
        private bool HasDirtyProjectBuffers => currentProject != null && buffers.Values.Any(b => b.Document.IsDirty && b.Document.FilePath != null &&
            b.Document.FilePath.StartsWith(currentProject.Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        private void InvalidateProjectPreview()
        {
            projectPreview = null; syncFiles.Items.Clear();
            syncSummary.Text = "Compare saved project scripts with PS4. Remote-only files appear only in folders also present locally; no remote folder discovery or deletion.";
            if (commands.ContainsKey("Upload selected")) UpdateProjectCommands();
        }
        private void UpdateProjectCommands()
        {
            if (!commands.ContainsKey("Compare project")) return;
            foreach (string name in new[] { "New project", "Open project", "Open recent" }) commands[name].Enabled = !busy;
            foreach (string name in new[] { "Save project", "Refresh project" }) commands[name].Enabled = currentProject != null && !busy;
            commands["Compare project"].Enabled = currentProject != null && connection.Connected && !busy;
            commands["Upload selected"].Enabled = projectPreview != null && projectPreview.Generation == connection.Generation && connection.Connected && !busy && !HasDirtyProjectBuffers;
            commands["Select changes"].Enabled = commands["Clear selection"].Enabled = projectPreview != null && !busy;
            syncFiles.Enabled = recentProjects.Enabled = !busy;
        }
        private void SetSyncSelection(bool selected)
        {
            for (int i = 0; i < syncFiles.Items.Count; i++)
                syncFiles.SetItemChecked(i, selected && ((StudioSyncEntry)syncFiles.Items[i].Value).CanUpload);
        }
        internal async Task CompareProjectAsync()
        {
            if (busy || currentProject == null || !connection.Connected) return;
            if (HasDirtyProjectBuffers) { status.Text = "Save modified project scripts first (Ctrl+S), then compare again."; return; }
            InvalidateProjectPreview(); tools.SelectedTabPage = syncPage;
            var projectAtStart = currentProject;
            await RunRemoteAsync("Compare project", async ct => {
                var preview = await StudioProjectSync.CompareAsync(connection, projectAtStart, ct);
                if (closing || currentProject != projectAtStart || HasDirtyProjectBuffers || preview.Generation != connection.Generation) return "Project changed; compare again.";
                projectPreview = preview;
                foreach (var entry in preview.Entries) syncFiles.Items.Add(new CheckedListBoxItem(entry, entry.CanUpload ? CheckState.Checked : CheckState.Unchecked, entry.CanUpload));
                syncSummary.Text = preview.Entries.Count(e => e.CanUpload) + " new/changed · " + preview.Entries.Count(e => e.Status == StudioSyncStatus.Unchanged) +
                    " identical · " + preview.Entries.Count(e => e.Status == StudioSyncStatus.RemoteOnly) + " remote-only (locally represented folders only). Select files below; Upload does not execute or reload.";
                return "Compared " + preview.Entries.Count + " files. Select only the files you want to upload.";
            });
        }
        private async Task UploadSelectedAsync()
        {
            if (busy || projectPreview == null || HasDirtyProjectBuffers) return;
            var preview = projectPreview;
            string[] selected = syncFiles.Items.Cast<CheckedListBoxItem>().Where(i => i.CheckState == CheckState.Checked && ((StudioSyncEntry)i.Value).CanUpload)
                .Select(i => ((StudioSyncEntry)i.Value).Path).ToArray();
            if (selected.Length == 0) { status.Text = "Select at least one new or changed file."; return; }
            if (XtraMessageBox.Show(this, preview.ConfirmationText + "\r\n\r\n" + string.Join("\r\n", selected.Select(p => "scripts/" + p)),
                "Confirm selected project upload", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int uploaded = 0;
            await RunRemoteAsync("Upload project", async ct => {
                try {
                    await StudioProjectSync.UploadAsync(connection, preview, selected, path => {
                        // Core resumes off the UI thread; marshal one exact acknowledgement per completed PUT.
                        Interlocked.Increment(ref uploaded);
                        if (!closing && IsHandleCreated) BeginInvoke(new Action(() => { if (!closing) AppendConsole("Uploaded scripts/" + path); }));
                    }, ct);
                    return uploaded + " selected files uploaded only. Reload separately when ready.";
                } finally {
                    if (!closing) { InvalidateProjectPreview(); AppendConsole("Project upload: " + uploaded + "/" + selected.Length + " files acknowledged. Compare again before another upload. No automatic rollback or reload."); }
                }
            });
        }
    }
}

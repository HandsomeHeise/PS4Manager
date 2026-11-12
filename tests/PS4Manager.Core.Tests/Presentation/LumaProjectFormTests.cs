using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.XtraEditors;
using DevExpress.XtraTab;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Luma.Studio;
using PS4Manager.Core.Luma;
using PS4Manager.Core.Tests.Luma.Studio;
using PS4Manager.Core.Tests.TestSupport;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class LumaProjectFormTests
    {
        [Fact] public void Open_project_applies_profile_preserves_dirty_buffers_and_remembers_recent_paths()
        {
            StaTest.Run(() => {
                string parent = Path.Combine(Path.GetTempPath(), "studio-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(parent);
                try {
                    var project = StudioWorkspace.Create(parent, "RDR scripts", "rdr2"); var settings = new ManagerSettings(); int saved = 0;
                    using (var form = new LumaStudioForm(settings, () => saved++)) {
                        form.Show(); form.ActiveEditor.Text = "-- unsaved"; var id = form.ActiveDocument.Id;
                        form.LoadProject(project.Root);
                        Assert.Same(LuaApiCatalog.ForProfile("rdr2"), form.ApiCatalog); Assert.Equal(id, form.ActiveDocument.Id); Assert.Equal("-- unsaved", form.ActiveDocument.Source);
                        Assert.Equal(project.Root, form.CurrentProject.Root); Assert.Single(settings.Luma.RecentProjects); Assert.True(saved > 0);
                        Assert.Contains("Compare project", form.CommandNames); Assert.Contains("New project", form.CommandNames);
                        Assert.False(form.Controls.Find("studio.uploadSelected", true).Single().Enabled);
                        Assert.Contains(form.Controls.OfType<Control>().SelectMany(Descendants).OfType<XtraTabPage>(), p => p.Text == "PROJECT SYNC");
                        form.DiscardAllForTest();
                    }
                } finally { Directory.Delete(parent, true); }
            });
        }
        [Fact] public void Recent_projects_are_deduplicated_and_capped_without_automatic_connections()
        {
            StaTest.Run(() => {
                string parent = Path.Combine(Path.GetTempPath(), "studio-recent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(parent);
                try {
                    var settings = new ManagerSettings();
                    using (var form = new LumaStudioForm(settings, () => { })) {
                        for (int i = 0; i < 12; i++) { string root = Path.Combine(parent, "p" + i); Directory.CreateDirectory(root); form.LoadProject(root); }
                        form.LoadProject(Path.Combine(parent, "p9"));
                        Assert.Equal(10, settings.Luma.RecentProjects.Count); Assert.Equal(Path.Combine(parent, "p9"), settings.Luma.RecentProjects[0]);
                        Assert.False(form.StudioConnected); form.DiscardAllForTest();
                    }
                } finally { Directory.Delete(parent, true); }
            });
        }
        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
        { yield return control; foreach (Control child in control.Controls) foreach (var descendant in Descendants(child)) yield return descendant; }

        [Fact] public void Comparison_shows_saved_changes_and_dirty_buffers_block_remote_reads_and_invalidate_preview()
        {
            StaTest.Run(() => {
                string parent = Path.Combine(Path.GetTempPath(), "studio-compare-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(parent);
                try {
                    var wire = new StudioProjectSyncTests.ProjectTransport();
                    using (var session = new StudioConnection(() => new LumaClient(wire)))
                    using (var form = new LumaStudioForm(new ManagerSettings(), () => { }, session, new LuaSyntaxChecker())) {
                        form.Show(); var workspace = StudioWorkspace.Create(parent, "Test", "gta5"); form.LoadProject(workspace.Root);
                        form.AddDocument(StudioDocument.Load(Path.Combine(workspace.Root, "main.lua")));
                        Pump(session.ConnectAsync(new LumaEndpoint("offline.test", 9615), "gta5", "", CancellationToken.None));
                        form.ActiveEditor.Text += "-- unsaved"; int before = wire.Operations.Count;
                        Pump(form.CompareProjectAsync()); Assert.Equal(before, wire.Operations.Count);
                        form.ActiveDocument.Save(); Pump(form.CompareProjectAsync());
                        var files = Descendants(form).OfType<CheckedListBoxControl>().Single(c => c.Name == "studio.syncFiles");
                        Assert.Single(files.Items); Assert.True(files.GetItemChecked(0));
                        Assert.True(form.Controls.Find("studio.uploadSelected", true).Single().Enabled);
                        string capture = Environment.GetEnvironmentVariable("LUMA_PROJECT_CAPTURE");
                        if (!string.IsNullOrEmpty(capture)) using (var image = new System.Drawing.Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height)); image.Save(capture);
                        }
                        form.ActiveEditor.Text += "\n-- changed again";
                        Assert.Empty(files.Items); Assert.False(form.Controls.Find("studio.uploadSelected", true).Single().Enabled);
                        form.DiscardAllForTest();
                    }
                } finally { Directory.Delete(parent, true); }
            });
        }
        private static void Pump(Task task)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 8000) { Application.DoEvents(); Thread.Sleep(2); }
            Assert.True(task.IsCompleted); task.GetAwaiter().GetResult();
        }
        [Theory] [InlineData(false)] [InlineData(true)]
        public void Refresh_handles_missing_root_or_invalid_metadata_without_losing_dirty_buffers(bool invalidManifest)
        {
            StaTest.Run(() => {
                string root = Path.Combine(Path.GetTempPath(), "studio-refresh-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                try {
                    using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                        form.Show(); form.LoadProject(root); form.ActiveEditor.Text = "-- preserved";
                        if (invalidManifest) File.WriteAllText(Path.Combine(root, "luma-project.json"), "not JSON"); else Directory.Delete(root);
                        Descendants(form).OfType<SimpleButton>().Single(b => b.Text == "Refresh project").PerformClick();
                        Assert.Equal("-- preserved", form.ActiveDocument.Source); Assert.True(form.ActiveDocument.IsDirty);
                        Assert.Contains(Descendants(form), c => c.Text.StartsWith("Project failed:")); form.DiscardAllForTest();
                    }
                } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            });
        }
        [Fact] public void CtrlS_during_remote_operation_does_not_change_disk_or_mark_buffer_saved()
        {
            StaTest.Run(() => {
                string root = Path.Combine(Path.GetTempPath(), "studio-save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                try {
                    string path = Path.Combine(root, "a.lua"); File.WriteAllText(path, "return 1");
                    using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                        form.Show(); form.AddDocument(StudioDocument.Load(path)); form.ActiveEditor.Text = "return 2";
                        typeof(LumaStudioForm).GetField("busy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(form, true);
                        var message = Message.Create(form.Handle, 0, IntPtr.Zero, IntPtr.Zero);
                        typeof(LumaStudioForm).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .Invoke(form, new object[] { message, Keys.Control | Keys.S });
                        Assert.Equal("return 1", File.ReadAllText(path)); Assert.True(form.ActiveDocument.IsDirty);
                        form.DiscardAllForTest();
                    }
                } finally { Directory.Delete(root, true); }
            });
        }
    }
}

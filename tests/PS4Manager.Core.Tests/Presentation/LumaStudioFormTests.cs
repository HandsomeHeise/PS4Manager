using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Luma.Studio;
using PS4Manager.Core.Tests.TestSupport;
using PS4Manager.UI;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class LumaStudioFormTests
    {
        [Fact]
        public void Studio_opens_offline_with_separate_actions_and_real_editor()
        {
            StaTest.Run(() => {
            AppTheme.Current = AppTheme.Dark;
            using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                form.Show();
                Assert.True(form.ActiveEditor.Width > 600, "Editor should receive most of the horizontal space.");
                Assert.False(form.StudioConnected);
                Assert.Equal("lua", form.ActiveEditor.LexerName);
                Assert.Equal(AppTheme.Background, form.ActiveEditor.Styles[ScintillaNET.Style.LineNumber].BackColor);
                Assert.Contains("Check", form.CommandNames);
                Assert.Contains("Execute", form.CommandNames);
                Assert.Contains("Upload", form.CommandNames);
                Assert.Contains("Reload", form.CommandNames);
                Assert.NotNull(form.ApiCatalog.Find("GET_PLAYER_PED"));
                var capture = Environment.GetEnvironmentVariable("LUMA_STUDIO_CAPTURE");
                if (!string.IsNullOrEmpty(capture)) {
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) {
                        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(capture);
                    }
                }
                form.DiscardAllForTest();
            }
            });
        }

        [Fact]
        public void Documents_preserve_independent_buffers_and_cancelled_close()
        {
            StaTest.Run(() => {
            using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                var first = form.ActiveDocument;
                form.ActiveEditor.Text = "return 1";
                form.AddDocument(new StudioDocument("second.lua", "return 2"));
                Assert.Equal("return 2", form.ActiveEditor.Text);
                Assert.False(form.TryCloseDocument(first.Id, _ => DialogResult.Cancel));
                Assert.Equal(2, form.DocumentCount);
                Assert.True(form.TryCloseDocument(first.Id, _ => DialogResult.No));
                Assert.Equal("return 2", form.ActiveEditor.Text);
                form.DiscardAllForTest();
            }
            });
        }

        [Fact]
        public void Diagnostics_belong_to_the_checked_buffer_and_are_navigable()
        {
            StaTest.Run(() => {
            using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                form.Show();
                form.ActiveEditor.Text = "local x = 1\nlocal =";
                Pump(form.CheckDocumentAsync());
                Assert.Single(form.CurrentDiagnostics);
                Assert.Equal(2, form.CurrentDiagnostics[0].Line);
                form.NavigateDiagnostic(0);
                Assert.Equal(1, form.ActiveEditor.LineFromPosition(form.ActiveEditor.CurrentPosition));
                form.DiscardAllForTest();
            }
            });
        }

        [Fact]
        public void Late_check_cannot_replace_newer_document_diagnostics()
        {
            StaTest.Run(() => {
            var checker = new BlockingChecker();
            using (var form = new LumaStudioForm(new ManagerSettings(), () => { }, new StudioConnection(), checker)) {
                form.Show();
                var check = form.CheckDocumentAsync();
                Pump(checker.Entered.Task);
                form.ActiveEditor.Text = "return 42";
                checker.Release.Set();
                Pump(check);
                Assert.Empty(form.CurrentDiagnostics);
                form.DiscardAllForTest();
            }
            });
        }
        private static void Pump(Task task)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 8000) { Application.DoEvents(); Thread.Sleep(2); }
            Assert.True(task.IsCompleted, "UI operation timed out"); task.GetAwaiter().GetResult();
        }
        [Fact]
        public void Dashboard_exit_honors_Studio_unsaved_document_veto_before_shutdown()
        {
            StaTest.Run(() => {
                using (var dashboard = new DashboardForm(new ManagerSettings())) {
                    dashboard.Show();
                    typeof(DashboardForm).GetMethod("OpenTool", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(dashboard, new object[] { "luma" });
                    var studio = Assert.Single(dashboard.OwnedForms.OfType<LumaStudioForm>());
                    studio.CloseDecision = _ => DialogResult.Cancel;
                    var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
                    typeof(DashboardForm).GetMethod("OnFormClosing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(dashboard, new object[] { args });
                    Assert.False((bool)typeof(DashboardForm).GetField("_closing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(dashboard));
                    Assert.True(args.Cancel);
                    studio.DiscardAllForTest();
                }
            });
        }
        private sealed class BlockingChecker : ILuaSyntaxChecker
        {
            public TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>();
            public ManualResetEventSlim Release = new ManualResetEventSlim();
            public LuaCheckResult Check(string source, string fileName) {
                Entered.TrySetResult(true); Release.Wait();
                return new LuaCheckResult(true, new[] { new LuaDiagnostic(fileName, 1, "old error") });
            }
        }
    }
}

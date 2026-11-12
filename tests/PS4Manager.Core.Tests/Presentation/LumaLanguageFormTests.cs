using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Luma.Studio;
using PS4Manager.Core.Tests.TestSupport;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class LumaLanguageFormTests
    {
        [Fact] public void Real_editor_offers_completion_and_nested_signature_but_not_in_strings()
        {
            StaTest.Run(() => {
                using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                    form.Show(); var editor = form.ActiveEditor;
                    editor.Text = "GET_PLAYER_P"; editor.GotoPosition(editor.TextLength);
                    Invoke(form, "Complete", editor, true); Assert.True(editor.AutoCActive); editor.AutoCCancel();
                    editor.Text = "local s = 'GET_PLAYER_P"; editor.GotoPosition(editor.TextLength);
                    Invoke(form, "Complete", editor, true); Assert.False(editor.AutoCActive);
                    editor.Text = "SET_ENTITY_HEALTH(GET_PLAYER_PED(-1), 100"; editor.GotoPosition(editor.TextLength);
                    Invoke(form, "ShowCallTip", editor); Assert.True(editor.CallTipActive); form.DiscardAllForTest();
                }
            });
        }
        [Fact] public void Native_warnings_are_nonblocking_squiggles_with_correct_utf8_offsets_and_clear_on_edit()
        {
            StaTest.Run(() => {
                using (var form = new LumaStudioForm(new ManagerSettings(), () => { })) {
                    form.Show(); form.ActiveEditor.Text = "-- Grüße 🔥\nMISSING_NATIVE()";
                    Pump(form.CheckDocumentAsync());
                    var warning = Assert.Single(form.CurrentDiagnostics); Assert.Equal(LuaDiagnosticSeverity.Warning, warning.Severity);
                    int start = System.Text.Encoding.UTF8.GetByteCount("-- Grüße 🔥\n");
                    Assert.NotEqual(IntPtr.Zero, form.ActiveEditor.DirectMessage(2507, (IntPtr)9, (IntPtr)start));
                    Assert.Equal(IntPtr.Zero, form.ActiveEditor.DirectMessage(2507, (IntPtr)9, (IntPtr)(start - 1)));
                    string capture = Environment.GetEnvironmentVariable("LUMA_LANGUAGE_CAPTURE");
                    if (!string.IsNullOrEmpty(capture)) {
                        Descendants(form).OfType<DevExpress.XtraTab.XtraTabControl>().Single(c => c.Name == "studio.tools").SelectedTabPageIndex = 1;
                        using (var image = new System.Drawing.Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height)); image.Save(capture);
                        }
                    }
                    form.ActiveEditor.Text = "return 1";
                    Assert.Empty(form.CurrentDiagnostics); Assert.Equal(0, form.ActiveEditor.Indicators[9].ValueAt(0)); form.DiscardAllForTest();
                }
            });
        }
        private static void Invoke(object instance, string method, params object[] arguments) => instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, arguments);
        [Fact] public void Switching_profile_discards_a_pending_check_and_rechecks_with_the_new_catalog()
        {
            StaTest.Run(() => {
                var checker = new BlockingChecker();
                using (var form = new LumaStudioForm(new ManagerSettings(), () => { }, new StudioConnection(), checker)) {
                    form.Show(); form.ActiveEditor.Text = "SET_ENTITY_HEALTH(1, 100)";
                    var pending = form.CheckDocumentAsync(); Pump(checker.Entered.Task);
                    var game = Descendants(form).OfType<DevExpress.XtraEditors.ComboBoxEdit>().Single(c => c.Properties.Items.Contains("GTA V · Luma"));
                    game.SelectedIndex = 1; checker.Release.Set(); Pump(pending);
                    Assert.Empty(form.CurrentDiagnostics);
                    Pump(form.CheckDocumentAsync()); Assert.Contains(form.CurrentDiagnostics, d => d.Message.Contains("not in this game's"));
                    form.DiscardAllForTest();
                }
            });
        }
        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
        { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
        private sealed class BlockingChecker : ILuaSyntaxChecker
        {
            public readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>();
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            public LuaCheckResult Check(string source, string fileName) { Entered.TrySetResult(true); Release.Wait(); return new LuaCheckResult(true, Array.Empty<LuaDiagnostic>()); }
        }
        private static void Pump(Task task)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 8000) { Application.DoEvents(); Thread.Sleep(2); }
            Assert.True(task.IsCompleted); task.GetAwaiter().GetResult();
        }
    }
}

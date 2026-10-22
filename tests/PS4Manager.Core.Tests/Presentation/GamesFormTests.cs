using PS4Manager.UI;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System;
using PS4Manager.Core.Session;
using PS4Manager.Core.Tests.Games;
using PS4Manager.App;
using PS4Manager.Core.Config;
using PS4Manager.Core.Games;
using PS4Manager.Core.Tests.TestSupport;
using Xunit;

namespace PS4Manager.Core.Tests.Presentation
{
    [Collection(UiControlCollection.Name)]
    public sealed class GamesFormTests
    {
        [Fact]
        public void Background_poll_preserves_button_state_focus_and_reason_but_failure_disables_actions()
        {
            StaTest.Run(() => {
                var sessions = new ManagerSessionController(new SessionFactory()); var f = Bo3Fixture.Create();
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (var form = new GamesForm(sessions, new ManagerSettings { Frame4 = { Enabled = true } }, null, f.Transport, f.Trainer, () => true)) {
                    try {
                        form.Show(); sessions.ConnectAsync(new ManagerEndpoint("console", 2121), false, CancellationToken.None).GetAwaiter().GetResult();
                        PumpUntil(() => !(bool)Field(form, "refreshing"));
                        ((System.Windows.Forms.Timer)Field(form, "timer")).Stop();
                        var give = All(form).OfType<UiButton>().Single(b => Equals(b.Tag, TrainerAction.GiveRk5));
                        var reason = All(form).Single(c => c.AccessibleName == "Trainer availability");
                        Assert.True(give.Enabled); give.Focus(); string before = reason.Text; int changes = 0;
                        give.EnabledChanged += (s, e) => changes++;
                        f.Transport.DelayRead = address => release.Task;
                        var refresh = typeof(GamesForm).GetMethod("RefreshAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                        var poll = (Task)refresh.Invoke(form, new object[] { false });
                        All(form).OfType<UiTextBox>().Single().Text = "CUSA02624";
                        Application.DoEvents();
                        Assert.True(give.Enabled); Assert.True(give.Focused); Assert.Equal(before, reason.Text);
                        release.SetResult(true); PumpUntil(() => poll.IsCompleted); poll.GetAwaiter().GetResult();
                        Assert.Equal(0, changes); Assert.True(give.Enabled); Assert.True(give.Focused);
                        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var controller = (GamesController)Field(form, "controller");
                        var action = controller.RequestAsync(TrainerAction.GiveRk5, (r, ct) => confirmation.Task, CancellationToken.None);
                        Application.DoEvents(); Assert.False(give.Enabled);
                        confirmation.SetResult(false); PumpUntil(() => action.IsCompleted && give.Enabled);
                        Assert.Equal(ActionOutcome.Cancelled, action.GetAwaiter().GetResult().Outcome);
                        f.PutHealth(0);
                        var failed = (Task)refresh.Invoke(form, new object[] { false }); PumpUntil(() => failed.IsCompleted);
                        Assert.False(give.Enabled); Assert.Contains("No active player", reason.Text);
                        Assert.Empty(f.Transport.Writes); Assert.Empty(f.Transport.Calls);
                    } finally { release.TrySetResult(true); sessions.DisconnectAsync().GetAwaiter().GetResult(); }
                }
            });
        }
        private static object Field(GamesForm form, string name) => typeof(GamesForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        private static void PumpUntil(Func<bool> ready)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && timeout.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
            Assert.True(ready());
        }
        [Fact]
        public void Reconnect_during_held_refresh_eventually_discovers_new_session()
        {
            StaTest.Run(() => {
                var sessions = new ManagerSessionController(new SessionFactory()); var trainer = new HeldTrainer();
                using (var form = new GamesForm(sessions, new ManagerSettings { Frame4 = { Enabled = true } }, null, new FakeTrainerTransport(), trainer, () => true)) {
                    form.Show(); sessions.ConnectAsync(new ManagerEndpoint("first", 2121), false, CancellationToken.None).GetAwaiter().GetResult();
                    PumpUntil(() => trainer.Probes == 1);
                    sessions.ConnectAsync(new ManagerEndpoint("second", 2121), false, CancellationToken.None).GetAwaiter().GetResult();
                    trainer.Held.SetResult(new TrainerStatus(null, false, "old", GodmodeState.Unknown, null, null));
                    var controller = (GamesController)Field(form, "controller");
                    PumpUntil(() => controller.Installed.Count == 1 && trainer.Probes > 1);
                    sessions.DisconnectAsync().GetAwaiter().GetResult();
                }
            });
        }
        [Fact]
        public void Disabled_frame4_detach_clears_installation_evidence()
        {
            StaTest.Run(() => {
                using (var form = new GamesForm(null, new ManagerSettings(), null)) {
                    typeof(GamesForm).GetField("disabledInstalled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form,
                        new[] { new InstalledGame("CUSA02624", "BO3", "old-version", InstalledState.Installed, null) });
                    typeof(GamesForm).GetMethod("Detach", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                    Assert.Empty((IReadOnlyList<InstalledGame>)Field(form, "disabledInstalled"));
                }
            });
        }
        [Fact]
        public void Abandoned_dashboard_exit_does_not_reuse_games_close_approval()
        {
            StaTest.Run(() => {
                int prompts = 0;
                using (var form = new GamesForm(null, new ManagerSettings(), null, new FakeTrainerTransport(), null, () => ++prompts == 1)) {
                    typeof(GamesForm).GetField("everMayBeEnabled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, true);
                    Assert.True(form.ApproveClosing()); // Other dashboard warnings then cancel the exit.
                    Assert.False(form.ApproveClosing()); Assert.Equal(2, prompts);
                }
            });
        }
        private sealed class SessionFactory : IManagerSessionFactory
        {
            public ManagerSession Create(ManagerEndpoint endpoint, bool expertMode) =>
                (ManagerSession)typeof(PS4Manager.Core.Tests.Session.ManagerSessionTests).GetMethod("CreateSession", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { null, null });
        }
        private sealed class HeldTrainer : IGameTrainer
        {
            internal int Probes;
            internal readonly TaskCompletionSource<TrainerStatus> Held = new TaskCompletionSource<TrainerStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<TrainerStatus> ProbeAsync(TrainerTarget target, CancellationToken ct) => ++Probes == 1 ? Held.Task :
                Task.FromResult(new TrainerStatus(null, false, "test", GodmodeState.Unknown, null, null));
            public Task<TrainerResult> ExecuteAsync(TrainerTarget target, TrainerIdentity expected, TrainerAction action, CancellationToken ct, string expectedContext = null) => throw new InvalidOperationException("No test mutation allowed");
        }
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void Games_can_render_for_visual_acceptance(bool light)
        {
            StaTest.Run(() => {
                var previous = AppTheme.Current;
                try {
                    AppTheme.Current = light ? AppTheme.Light : AppTheme.Dark;
                    using (var form = new GamesForm(null, new ManagerSettings(), null)) {
                        form.Show(); form.PerformLayout(); Application.DoEvents();
                        var folder = System.Environment.GetEnvironmentVariable("PS4_GAMES_VISUAL_OUTPUT");
                        if (string.IsNullOrEmpty(folder)) return;
                        foreach (bool small in new[] { false, true }) {
                            if (small) form.Size = form.MinimumSize;
                            form.PerformLayout(); Application.DoEvents();
                            using (var bitmap = new Bitmap(form.Width, form.Height)) {
                                form.DrawToBitmap(bitmap, form.ClientRectangle);
                                bitmap.Save(System.IO.Path.Combine(folder, "games-" + (light ? "light" : "dark") + (small ? "-small" : "-large") + ".png"));
                            }
                        }
                    }
                } finally { AppTheme.Current = previous; }
            });
        }
        [Theory]
        [InlineData(false, 1f)] [InlineData(true, 1f)]
        [InlineData(false, 1.5f)] [InlineData(true, 1.5f)]
        [InlineData(false, 2f)] [InlineData(true, 2f)]
        public void Small_window_and_long_reasons_remain_scrollable_in_both_skins(bool light, float scale)
        {
            StaTest.Run(() => {
                var previous = AppTheme.Current;
                try {
                    AppTheme.Current = light ? AppTheme.Light : AppTheme.Dark;
                    using (var form = new GamesForm(null, new ManagerSettings(), null)) {
                        form.Show(); form.Scale(new SizeF(scale, scale)); form.Size = form.MinimumSize;
                        var reason = All(form).Single(c => c.AccessibleName == "Trainer availability");
                        reason.Text = string.Join(" ", Enumerable.Repeat("Long diagnostic reason", 100)); form.PerformLayout();
                        Assert.True(reason.Height > 100);
                        Assert.True(((ScrollableControl)reason.Parent).AutoScroll);
                        AppTheme.Current = light ? AppTheme.Dark : AppTheme.Light;
                        Assert.True(All(form).OfType<UiButton>().All(b => !string.IsNullOrEmpty(b.AccessibleName)));
                    }
                } finally { AppTheme.Current = previous; }
            });
        }
        [Fact]
        public void Unknown_after_active_godmode_warns_and_close_never_writes()
        {
            StaTest.Run(() => {
                var f = Bo3Fixture.Create(); f.PutFlags(5); int prompts = 0;
                using (var form = new GamesForm(null, new ManagerSettings { Frame4 = { Enabled = true } }, null, f.Transport, f.Trainer, () => ++prompts > 1)) {
                    form.Show();
                    var controller = (GamesController)typeof(GamesForm).GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    controller.Attach(f.Target, null); controller.RefreshAsync(true, CancellationToken.None).GetAwaiter().GetResult();
                    Application.DoEvents(); Assert.True(form.RequiresCloseWarning);
                    controller.Detach(); Application.DoEvents(); Assert.True(form.RequiresCloseWarning);
                    form.Close(); Assert.False(form.IsDisposed);
                    form.Close(); Assert.True(form.IsDisposed); Assert.Empty(f.Transport.Writes); Assert.Empty(f.Transport.Calls);
                }
            });
        }
        [Fact]
        public void Icon_decoder_rejects_oversized_dimensions_before_decoding()
        {
            var bytes = new byte[24]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
            System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12); bytes[16] = 127; bytes[23] = 1;
            Assert.Throws<System.IO.InvalidDataException>(() => GamesForm.DecodeIcon(bytes));
        }
        internal static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (var child in All(c)) yield return child; }
        }
        [Fact]
        public void Disconnected_catalog_has_game_experimental_warning_and_disabled_actions()
        {
            StaTest.Run(() => {
                using (var form = new GamesForm(null, new ManagerSettings(), null)) {
                    form.Show();
                    Assert.Contains(All(form), c => c.Text.Contains("Black Ops III"));
                    Assert.Contains(All(form), c => c.Text.Contains("Experimental"));
                    Assert.Contains(All(form), c => c.Text.Contains("Connect a console"));
                    var actions = All(form).OfType<UiButton>().Where(b => b.Tag is TrainerAction).ToArray();
                    Assert.Equal(4, actions.Length); Assert.All(actions, b => Assert.False(b.Enabled));
                    Assert.False(form.RequiresCloseWarning);
                    All(form).OfType<UiTextBox>().Single().Text = "not a game";
                    Assert.Contains(All(form), c => c.Visible && c.Text.Contains("No matching"));
                    form.Close();
                }
            });
        }
        [Theory]
        [InlineData(Keys.Enter)] [InlineData(Keys.Escape)] [InlineData(Keys.None)]
        public void Action_dialog_defaults_to_cancel(Keys key)
        {
            StaTest.Run(() => {
                using (var dialog = GamesDialogs.CreateAction(new ConfirmationRequest(null, TrainerAction.EnableGodmode, true, "Paused?"))) {
                    dialog.Show(); Assert.Equal("Cancel", dialog.ActiveControl.Text);
                    if (key == Keys.None) dialog.Close();
                    else dialog.GetType().GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[] { key });
                    Assert.Equal(0, dialog.Choice);
                }
            });
        }
        [Fact]
        public void Close_warning_defaults_to_cancel_and_explains_no_cleanup()
        {
            StaTest.Run(() => {
                using (var dialog = GamesDialogs.CreateCloseWarning()) {
                    dialog.Show(); Assert.Equal("Cancel", dialog.ActiveControl.Text);
                    Assert.Contains(All(dialog), c => c.Text.Contains("No changes will be undone"));
                    dialog.Close(); Assert.Equal(0, dialog.Choice);
                }
            });
        }
        [Fact]
        public void Dashboard_owns_exactly_one_games_window()
        {
            StaTest.Run(() => {
                using (var dashboard = new DashboardForm(new ManagerSettings())) {
                    dashboard.Show();
                    var open = typeof(DashboardForm).GetMethod("OpenTool", BindingFlags.Instance | BindingFlags.NonPublic);
                    open.Invoke(dashboard, new object[] { "games" }); open.Invoke(dashboard, new object[] { "games" });
                    Assert.Single(dashboard.OwnedForms.OfType<GamesForm>());
                }
            });
        }
    }
}

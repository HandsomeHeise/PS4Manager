using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.Games;
using Xunit;

namespace PS4Manager.Core.Tests.Games
{
    public sealed class GamesControllerTests
    {
        [Fact]
        public async Task Unchanged_background_poll_does_not_publish_busy_or_status_churn()
        {
            var f = new FakeTrainer(); using (var c = await Ready(f)) {
                int notifications = 0; c.Changed += (s, e) => notifications++;
                await c.RefreshAsync(false, CancellationToken.None);
                Assert.Equal(0, notifications);
                f.Godmode = GodmodeState.OnExternal;
                await c.RefreshAsync(false, CancellationToken.None);
                Assert.Equal(1, notifications); Assert.Equal(GodmodeState.OnExternal, c.Status.Godmode);
            }
        }
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public async Task One_click_waits_for_poll_and_reconnect_cancels_it(bool reconnect)
        {
            var f = new FakeTrainer(); using (var c = await Ready(f)) {
                var held = new TaskCompletionSource<TrainerStatus>(TaskCreationOptions.RunContinuationsAsynchronously); f.NextProbe = held;
                var poll = c.RefreshAsync(false, CancellationToken.None);
                int confirmations = 0;
                var action = c.RequestAsync(TrainerAction.GiveRk5, (r, ct) => { confirmations++; return Task.FromResult(true); }, CancellationToken.None);
                Assert.False(action.IsCompleted); Assert.Equal(0, f.Executions);
                Assert.Equal(ActionOutcome.Blocked, (await c.RequestAsync(TrainerAction.GiveRk5, (r, ct) => Task.FromResult(true), CancellationToken.None)).Outcome);
                if (reconnect) c.Attach(Target(2), new Installed());
                held.SetResult(new TrainerStatus(Identity(1), true, "Ready", GodmodeState.Off, "Player", "Bloodhound", "player"));
                await poll; var result = await action;
                Assert.Equal(reconnect ? ActionOutcome.Cancelled : ActionOutcome.Success, result.Outcome);
                Assert.Equal(reconnect ? 0 : 1, f.Executions); Assert.Equal(reconnect ? 0 : 1, confirmations);
                Assert.Equal(1, result.Identity.Generation);
            }
        }
        private static TrainerTarget Target(long generation) => new TrainerTarget(generation, new Frame4Endpoint("console"), () => true, CancellationToken.None);
        private static TrainerIdentity Identity(long generation) => new TrainerIdentity(generation, "console", 42, "CUSA02624", 0x400000, "md5-bf1ffcd3");
        private static async Task<GamesController> Ready(FakeTrainer trainer, Action<TrainerResult> completed = null)
        {
            var c = new GamesController(new GameCatalog(), trainer, completed); c.Attach(Target(1), new Installed());
            await c.RefreshAsync(true, CancellationToken.None); return c;
        }
        [Fact]
        public async Task Late_install_discovery_cannot_populate_a_new_session()
        {
            var f = new FakeTrainer(); var source = new Installed { Pending = new TaskCompletionSource<IReadOnlyList<InstalledGame>>() };
            using (var c = new GamesController(new GameCatalog(), f, null)) {
                c.Attach(Target(1), source); var refresh = c.RefreshAsync(true, CancellationToken.None);
                c.Attach(Target(2), new Installed());
                source.Pending.SetResult(new[] { new InstalledGame("CUSA02624", "Old", "1.0", InstalledState.Installed, null) });
                await refresh; Assert.Empty(c.Installed); Assert.False(c.Status.Ready);
            }
        }
        [Fact]
        public async Task Cancellation_after_dispatch_reports_unknown_once()
        {
            var f = new FakeTrainer { Pending = new TaskCompletionSource<TrainerResult>() }; var results = new List<TrainerResult>();
            using (var c = await Ready(f, results.Add)) {
                var request = c.RequestAsync(TrainerAction.RefillAmmo, (r, ct) => Task.FromResult(true), CancellationToken.None);
                f.Pending.SetCanceled();
                Assert.Equal(ActionOutcome.Unknown, (await request).Outcome); Assert.Single(results); Assert.Equal(1, f.Executions);
            }
        }
        [Fact]
        public async Task Cancellation_during_confirmation_has_no_execution_or_toast()
        {
            var f = new FakeTrainer(); var results = new List<TrainerResult>();
            using (var c = await Ready(f, results.Add)) using (var cancel = new CancellationTokenSource()) {
                var r = await c.RequestAsync(TrainerAction.EnableGodmode, (q, ct) => { cancel.Cancel(); return Task.FromResult(true); }, cancel.Token);
                Assert.Equal(ActionOutcome.Cancelled, r.Outcome); Assert.Equal(0, f.Executions); Assert.Empty(results);
            }
        }
        [Fact]
        public async Task Stale_confirmation_does_not_execute_on_new_session()
        {
            var f = new FakeTrainer(); using (var c = await Ready(f)) {
                var confirmation = new TaskCompletionSource<bool>();
                var request = c.RequestAsync(TrainerAction.GiveRk5, (r, ct) => confirmation.Task, CancellationToken.None);
                c.Attach(Target(2), new Installed()); confirmation.SetResult(true);
                await request; Assert.Equal(0, f.Executions); Assert.False(c.Status.Ready);
                await c.RefreshAsync(true, CancellationToken.None); Assert.Equal(2, c.Status.Identity.Generation);
            }
        }
        [Fact]
        public async Task Duplicate_actions_and_polls_do_not_overlap_confirmation()
        {
            var f = new FakeTrainer(); using (var c = await Ready(f)) {
                var confirmation = new TaskCompletionSource<bool>();
                var first = c.RequestAsync(TrainerAction.EnableGodmode, (r, ct) => confirmation.Task, CancellationToken.None);
                int probes = f.Probes;
                Assert.Equal(ActionOutcome.Blocked, (await c.RequestAsync(TrainerAction.GiveRk5, (r, ct) => Task.FromResult(true), CancellationToken.None)).Outcome);
                await c.RefreshAsync(false, CancellationToken.None); Assert.Equal(probes, f.Probes);
                confirmation.SetResult(false); Assert.Equal(ActionOutcome.Cancelled, (await first).Outcome); Assert.Equal(0, f.Executions);
            }
        }
        [Fact]
        public async Task Player_change_with_same_pid_invalidates_confirmation()
        {
            var f = new FakeTrainer(); using (var c = await Ready(f)) {
                var r = await c.RequestAsync(TrainerAction.GiveRk5, (q, ct) => { f.Context = "next-player"; return Task.FromResult(true); }, CancellationToken.None);
                Assert.Equal(ActionOutcome.Blocked, r.Outcome); Assert.Equal(0, f.Executions);
            }
        }
        [Fact]
        public async Task Late_result_keeps_original_identity_and_does_not_replace_new_status()
        {
            var f = new FakeTrainer { Pending = new TaskCompletionSource<TrainerResult>() }; var results = new List<TrainerResult>();
            using (var c = await Ready(f, results.Add)) {
                var request = c.RequestAsync(TrainerAction.GiveRk5, (r, ct) => Task.FromResult(true), CancellationToken.None);
                c.Attach(Target(2), new Installed());
                f.Pending.SetResult(new TrainerResult(Identity(1), TrainerAction.GiveRk5, ActionOutcome.Partial, "Ammo failed"));
                await request; Assert.Single(results); Assert.Equal(1, results[0].Identity.Generation); Assert.False(c.Status.Ready);
            }
        }
        [Fact]
        public async Task Unknown_outcome_requires_manual_reprobe_without_retry()
        {
            var f = new FakeTrainer { Outcome = ActionOutcome.Unknown }; using (var c = await Ready(f)) {
                await c.RequestAsync(TrainerAction.RefillAmmo, (r, ct) => Task.FromResult(true), CancellationToken.None);
                Assert.False(c.Status.Ready);
                Assert.Equal(ActionOutcome.Blocked, (await c.RequestAsync(TrainerAction.RefillAmmo, (r, ct) => Task.FromResult(true), CancellationToken.None)).Outcome);
                await c.RefreshAsync(false, CancellationToken.None); Assert.False(c.Status.Ready);
                await c.RefreshAsync(true, CancellationToken.None); Assert.True(c.Status.Ready); Assert.Equal(1, f.Executions);
            }
        }
        [Fact]
        public async Task Disconnect_and_dispose_never_disable_external_godmode()
        {
            var f = new FakeTrainer { Godmode = GodmodeState.OnExternal }; var c = await Ready(f);
            c.Detach(); Assert.Equal(GodmodeState.Unknown, c.Status.Godmode); Assert.Contains("may still", c.Status.Reason);
            c.Dispose(); Assert.Equal(0, f.Executions);
        }
        [Fact]
        public async Task Consent_is_session_bound_and_pause_is_always_required()
        {
            var f = new FakeTrainer(); using (var c = await Ready(f)) {
                var requests = new List<ConfirmationRequest>();
                Func<ConfirmationRequest, CancellationToken, Task<bool>> confirm = (r, ct) => { requests.Add(r); return Task.FromResult(true); };
                await c.RequestAsync(TrainerAction.EnableGodmode, confirm, CancellationToken.None);
                await c.RequestAsync(TrainerAction.DisableGodmode, confirm, CancellationToken.None);
                Assert.True(requests[0].NeedsOfflineConsent); Assert.False(requests[1].NeedsOfflineConsent);
                Assert.All(requests, r => Assert.Contains("paused", r.Message));
                c.Attach(Target(2), new Installed()); await c.RefreshAsync(true, CancellationToken.None);
                await c.RequestAsync(TrainerAction.EnableGodmode, confirm, CancellationToken.None); Assert.True(requests[2].NeedsOfflineConsent);
            }
        }
        private sealed class Installed : IInstalledGames
        {
            internal TaskCompletionSource<IReadOnlyList<InstalledGame>> Pending;
            public Task<IReadOnlyList<InstalledGame>> DiscoverAsync(CancellationToken ct) => Pending?.Task ?? Task.FromResult<IReadOnlyList<InstalledGame>>(new InstalledGame[0]);
        }
        private sealed class FakeTrainer : IGameTrainer
        {
            internal int Probes, Executions;
            internal string Context = "player";
            internal GodmodeState Godmode = GodmodeState.Off;
            internal ActionOutcome Outcome = ActionOutcome.Success;
            internal TaskCompletionSource<TrainerResult> Pending;
            internal TaskCompletionSource<TrainerStatus> NextProbe;
            public Task<TrainerStatus> ProbeAsync(TrainerTarget target, CancellationToken ct)
            { Probes++; ct.ThrowIfCancellationRequested(); var held = NextProbe; NextProbe = null; return held?.Task ?? Task.FromResult(new TrainerStatus(Identity(target.Generation), true, "Ready", Godmode, "Player", "Bloodhound", Context)); }
            public Task<TrainerResult> ExecuteAsync(TrainerTarget target, TrainerIdentity expected, TrainerAction action, CancellationToken ct, string expectedContext = null)
            { Assert.Equal(Context, expectedContext); Executions++; return Pending?.Task ?? Task.FromResult(new TrainerResult(expected, action, Outcome, "Result")); }
        }
    }
}

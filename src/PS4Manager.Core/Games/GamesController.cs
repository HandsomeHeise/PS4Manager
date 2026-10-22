using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Games
{
    public sealed class GamesController : IDisposable
    {
        private readonly object sync = new object();
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly GameCatalog catalog;
        private readonly IGameTrainer trainer;
        private readonly Action<TrainerResult> completed;
        private TrainerTarget target;
        private IInstalledGames installedService;
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private TrainerIdentity consent;
        private string consentContext;
        private bool disposed, busy, actionPending, needsReprobe;
        private TrainerStatus status = Unavailable("Connect a console to use trainers.");
        private IReadOnlyList<InstalledGame> installed = new InstalledGame[0];
        private string selected;
        public GamesController(GameCatalog catalog, IGameTrainer trainer, Action<TrainerResult> completed)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.trainer = trainer ?? throw new ArgumentNullException(nameof(trainer)); this.completed = completed;
            selected = catalog.Entries.FirstOrDefault()?.Id;
        }
        public TrainerStatus Status { get { lock (sync) return status; } }
        public IReadOnlyList<InstalledGame> Installed { get { lock (sync) return installed; } }
        public string SelectedGameId { get { lock (sync) return selected; } }
        public bool Busy { get { lock (sync) return busy; } }
        public bool ActionPending { get { lock (sync) return actionPending; } }
        public event EventHandler Changed;
        private static TrainerStatus Unavailable(string reason) => new TrainerStatus(null, false, reason, GodmodeState.Unknown, null, null);
        private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

        public void Attach(TrainerTarget newTarget, IInstalledGames newInstalled)
        {
            if (newTarget == null) throw new ArgumentNullException(nameof(newTarget));
            CancellationTokenSource old;
            lock (sync) {
                if (disposed) return;
                old = lifetime; lifetime = new CancellationTokenSource(); target = newTarget; installedService = newInstalled;
                consent = null; consentContext = null; needsReprobe = false; installed = new InstalledGame[0]; status = Unavailable("Refresh to detect the running game.");
            }
            old.Cancel(); old.Dispose(); Notify();
        }
        public void Detach()
        {
            CancellationTokenSource old;
            lock (sync) {
                old = lifetime; lifetime = new CancellationTokenSource(); target = null; installedService = null;
                consent = null; consentContext = null; needsReprobe = false; installed = new InstalledGame[0];
                status = Unavailable("Console disconnected. Godmode state is unknown and may still be enabled.");
            }
            old.Cancel(); old.Dispose(); Notify();
        }
        public void Select(string gameId)
        {
            lock (sync) {
                if (!catalog.Entries.Any(g => g.Id == gameId)) throw new ArgumentException("Unsupported game.", nameof(gameId));
                if (busy) return; selected = gameId;
            }
            Notify();
        }
        private bool Current(TrainerTarget captured)
        { lock (sync) return !disposed && ReferenceEquals(target, captured) && captured != null && captured.IsCurrent() && !captured.SessionToken.IsCancellationRequested; }
        private void Publish(TrainerTarget captured, TrainerStatus value)
        {
            lock (sync) {
                if (!Current(captured)) return;
                bool unchanged = Equals(status.Identity, value.Identity) && status.Ready == value.Ready &&
                    status.Reason == value.Reason && status.Godmode == value.Godmode && status.PlayerName == value.PlayerName &&
                    status.WeaponName == value.WeaponName && status.ContextKey == value.ContextKey;
                status = value;
                if (!value.Ready || !Equals(consent, value.Identity) || consentContext != value.ContextKey) { consent = null; consentContext = null; }
                if (unchanged) return;
            }
            Notify();
        }
        private void SetBusy(bool value, bool notify = true) { lock (sync) busy = value; if (notify) Notify(); }

        public async Task<bool> RefreshAsync(bool includeInstalled, CancellationToken ct)
        {
            if (!await gate.WaitAsync(0, ct).ConfigureAwait(false)) return false;
            TrainerTarget captured = null;
            try {
                IInstalledGames service; CancellationToken token;
                lock (sync) {
                    if (disposed || target == null || actionPending || (needsReprobe && !includeInstalled)) return false;
                    captured = target; service = installedService; token = lifetime.Token;
                }
                SetBusy(true, includeInstalled);
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token, captured.SessionToken)) {
                    if (includeInstalled && service != null) {
                        var games = await service.DiscoverAsync(linked.Token).ConfigureAwait(false);
                        lock (sync) if (Current(captured)) installed = Array.AsReadOnly(games.ToArray());
                    }
                    var result = await trainer.ProbeAsync(captured, linked.Token).ConfigureAwait(false);
                    lock (sync) if (Current(captured) && result.Ready) needsReprobe = false;
                    Publish(captured, result);
                }
            } catch (OperationCanceledException) { }
            catch (Exception ex) { Publish(captured, Unavailable("Frame4 is not available: " + ex.Message)); }
            finally { SetBusy(false, includeInstalled); gate.Release(); }
            return true;
        }

        public async Task<TrainerResult> RequestAsync(TrainerAction action, Func<ConfirmationRequest, CancellationToken, Task<bool>> confirm, CancellationToken ct)
        {
            if (confirm == null) throw new ArgumentNullException(nameof(confirm));
            TrainerIdentity identity; TrainerTarget captured; CancellationToken token; string clickedContext;
            lock (sync) {
                identity = status.Identity;
                if (actionPending) return new TrainerResult(identity, action, ActionOutcome.Blocked, "Another operation is in progress.");
                if (disposed || target == null || !status.Ready || needsReprobe)
                    return new TrainerResult(identity, action, ActionOutcome.Blocked, status.Reason);
                captured = target; token = lifetime.Token; clickedContext = status.ContextKey; actionPending = true;
            }
            bool gateHeld = false, executed = false; TrainerResult result = null;
            try {
                Notify();
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token, captured.SessionToken)) {
                    // Reserve one user request while a read finishes; never queue duplicate clicks.
                    await gate.WaitAsync(linked.Token).ConfigureAwait(false); gateHeld = true;
                    linked.Token.ThrowIfCancellationRequested();
                    if (!Current(captured)) return new TrainerResult(identity, action, ActionOutcome.Blocked, "The console session changed.");
                    SetBusy(true);
                    var before = await trainer.ProbeAsync(captured, linked.Token).ConfigureAwait(false);
                    if (!Current(captured) || !before.Ready || identity == null)
                        return new TrainerResult(identity, action, ActionOutcome.Blocked, before.Reason);
                    Publish(captured, before);
                    if (!identity.Equals(before.Identity) || clickedContext != before.ContextKey)
                        return new TrainerResult(identity, action, ActionOutcome.Blocked, "The player or game session changed. Refresh and confirm again.");
                    bool needsConsent;
                    lock (sync) needsConsent = !Equals(consent, identity) || consentContext != before.ContextKey;
                    string message = ActionName(action) + " on " + identity.Host + " (" + identity.TitleId + ").\r\n\r\n" +
                        "Confirm that this is a local offline Zombies round and you are paused now. Do not resume until the result appears. " +
                        "Concurrent external trainers are not supported. Pause does not suspend every game thread.\r\n\r\n" +
                        (action == TrainerAction.DisableGodmode ? "This disables the existing protection, including protection enabled elsewhere." : "This experimental action is limited to the tested BO3 build.");
                    if (!await confirm(new ConfirmationRequest(identity, action, needsConsent, message), linked.Token).ConfigureAwait(false))
                        return new TrainerResult(identity, action, ActionOutcome.Cancelled, "Cancelled.");
                    linked.Token.ThrowIfCancellationRequested();
                    if (!Current(captured)) return new TrainerResult(identity, action, ActionOutcome.Blocked, "The console session changed.");
                    var fresh = await trainer.ProbeAsync(captured, linked.Token).ConfigureAwait(false);
                    if (!Current(captured) || !fresh.Ready || !identity.Equals(fresh.Identity) || before.ContextKey != fresh.ContextKey) {
                        lock (sync) { consent = null; consentContext = null; }
                        return new TrainerResult(identity, action, ActionOutcome.Blocked, "The player or game session changed. Refresh and confirm again.");
                    }
                    lock (sync) {
                        if (!Current(captured)) return new TrainerResult(identity, action, ActionOutcome.Blocked, "The console session changed.");
                        consent = identity; consentContext = fresh.ContextKey;
                    }
                    executed = true;
                    result = await trainer.ExecuteAsync(captured, identity, action, linked.Token, fresh.ContextKey).ConfigureAwait(false);
                    if (Current(captured)) {
                        if (result.Outcome == ActionOutcome.Unknown || result.Outcome == ActionOutcome.Partial) {
                            lock (sync) needsReprobe = true;
                            Publish(captured, new TrainerStatus(identity, false, result.Message + " Use Refresh to reconcile.", GodmodeState.Unknown, fresh.PlayerName, fresh.WeaponName));
                        } else Publish(captured, await trainer.ProbeAsync(captured, linked.Token).ConfigureAwait(false));
                    }
                    return result;
                }
            } catch (Exception ex) {
                result = new TrainerResult(identity, action, executed ? ActionOutcome.Unknown : ex is OperationCanceledException ? ActionOutcome.Cancelled : ActionOutcome.Blocked,
                    executed ? "Outcome unknown; the action may have completed. Refresh before continuing. " + ex.Message : ex.Message);
                if (executed && Current(captured)) { lock (sync) needsReprobe = true; Publish(captured, Unavailable(result.Message)); }
                return result;
            } finally {
                if (gateHeld) { SetBusy(false, false); gate.Release(); }
                lock (sync) actionPending = false;
                Notify();
                if (executed && result != null) completed?.Invoke(result);
            }
        }
        public static string ActionName(TrainerAction action)
        {
            switch (action) {
                case TrainerAction.EnableGodmode: return "Enable Godmode";
                case TrainerAction.DisableGodmode: return "Disable Godmode";
                case TrainerAction.RefillAmmo: return "Refill ammo";
                case TrainerAction.GiveRk5: return "Give RK5";
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
        public void Dispose()
        {
            lock (sync) { if (disposed) return; disposed = true; }
            Detach();
            lifetime.Dispose();
            // In-flight tasks still release the gate. Never free a remote RPC stub or clear Godmode here.
        }
    }
}

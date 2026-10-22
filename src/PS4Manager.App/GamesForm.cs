using PS4Manager.UI;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.App.Notifications;
using FileManager.Core.Browsing;
using PS4Manager.Core.Config;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.Games;
using PS4Manager.Core.Games.Bo3;
using PS4Manager.Core.Mods;
using PS4Manager.Core.Session;
using Ps4.Core.Titles;

namespace PS4Manager.App
{
    internal sealed partial class GamesForm : AppWindow
    {
        private static long nextGeneration;
        private readonly GameCatalog catalog = new GameCatalog();
        private readonly ManagerSessionController sessions;
        private readonly ManagerSettings settings;
        private readonly ToolUiDispatcher ui;
        private readonly ToolSessionBinding binding;
        private readonly GamesController controller;
        private readonly ITrainerTransport transport;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2000 };
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private ManagerSession session;
        private long generation;
        private bool closing, closeApproved, everMayBeEnabled, refreshing, manualRefreshing, fullRefreshPending;
        private readonly Func<bool> confirmClose;

        public GamesForm(ManagerSessionController sessions, ManagerSettings settings, DesktopNotificationService notifications)
            : this(sessions, settings, notifications, new Frame4TrainerTransport(new Frame4Client(), new Frame4PacketClient())) { }

        internal GamesForm(ManagerSessionController sessions, ManagerSettings settings, DesktopNotificationService notifications,
            ITrainerTransport transport, IGameTrainer trainer = null, Func<bool> confirmClose = null)
        {
            this.sessions = sessions; this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.transport = transport; this.confirmClose = confirmClose;
            ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            // Capture the dashboard-owned service, not this window, in late-result callbacks.
            controller = new GamesController(catalog, trainer ?? new Bo3Trainer(transport), result => ShowResult(notifications, result));
            BuildUi(); controller.Changed += ControllerChanged; Render();
            timer.Tick += async (s, e) => { if (Visible && !controller.Busy && !refreshing) await RefreshAsync(false); };
            VisibleChanged += (s, e) => timer.Enabled = Visible && !closing;
            if (sessions != null) binding = new ToolSessionBinding(sessions,
                s => Dispatch(() => { if (ReferenceEquals(sessions.Current, s)) Attach(s); }),
                s => Dispatch(() => { if (ReferenceEquals(session, s)) Detach(); }));
        }

        private void Dispatch(Action action)
        {
            try { ui.Dispatch(() => { if (!closing && !IsDisposed) action(); }); }
            catch (ObjectDisposedException) { } catch (InvalidOperationException) { }
        }
        private void ControllerChanged(object sender, EventArgs e) => Dispatch(Render);
        private void Attach(ManagerSession value)
        {
            Detach(); session = value; generation = Interlocked.Increment(ref nextGeneration);
            long captured = generation;
            var endpoint = new Frame4Endpoint(value.Endpoint.Host, settings.Frame4.HttpPort,
                Frame4Endpoint.DefaultSocketPort, settings.Frame4.UartPort);
            controller.Attach(new TrainerTarget(captured, endpoint,
                () => !closing && Interlocked.Read(ref generation) == captured && ReferenceEquals(sessions.Current, value), value.CancellationToken),
                new InstalledGamesAdapter(new ModGameDiscovery(value.Explorer.Ftp, value.Gates)));
            var _ = RefreshAsync(true);
        }
        private void Detach()
        {
            Interlocked.Exchange(ref generation, Interlocked.Increment(ref nextGeneration));
            session = null; lifetime.Cancel(); lifetime.Dispose(); lifetime = new CancellationTokenSource();
            fullRefreshPending = false; disabledInstalled = new InstalledGame[0];
            transport.Invalidate(); controller.Detach();
            var old = icon.Image; icon.Image = null; old?.Dispose();
            Render();
        }

        private async Task RefreshAsync(bool manual)
        {
            if (manual) fullRefreshPending = true;
            if (closing || session == null || refreshing || controller.Busy) return;
            manual |= fullRefreshPending;
            refreshing = true; manualRefreshing = manual; if (manual) Render();
            var captured = session; long capturedGeneration = generation;
            try {
                bool performed = false;
                if (settings.Frame4.Enabled) performed = await controller.RefreshAsync(manual, lifetime.Token);
                else if (manual) {
                    // FTP discovery remains useful even when the debugger is disabled.
                    var found = await new InstalledGamesAdapter(new ModGameDiscovery(captured.Explorer.Ftp, captured.Gates)).DiscoverAsync(lifetime.Token);
                    if (!closing && generation == capturedGeneration && ReferenceEquals(session, captured)) { disabledInstalled = found; performed = true; }
                }
                if (performed && manual && generation == capturedGeneration) fullRefreshPending = false;
                if (manual && !closing && generation == capturedGeneration && icon.Image == null)
                    await LoadIconAsync(captured, capturedGeneration, lifetime.Token);
            } catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closing && generation == capturedGeneration) footer.Text = "Refresh failed: " + ex.Message; }
            finally { refreshing = false; manualRefreshing = false; if (!closing && manual) Render(); }
        }

        private async Task ActAsync(TrainerAction action)
        {
            if (closing || !settings.Frame4.Enabled) return;
            // Close/disconnect may make a dispatched action unknowable. Do not silently assume Off.
            if (action == TrainerAction.EnableGodmode) everMayBeEnabled = true;
            try {
                var result = await controller.RequestAsync(action, ConfirmAsync, lifetime.Token);
                if (!closing && result.Identity != null && result.Identity.Generation == generation)
                    footer.Text = result.Outcome + ": " + result.Message;
            } catch (OperationCanceledException) { }
            finally { if (!closing) Render(); }
        }
        private Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (closing || ct.IsCancellationRequested) { completion.SetResult(false); return completion.Task; }
            ui.Dispatch(() => {
                if (closing || IsDisposed || ct.IsCancellationRequested) { completion.TrySetResult(false); return; }
                try {
                    using (var dialog = GamesDialogs.CreateAction(request))
                    using (ct.Register(() => Dispatch(() => { if (!dialog.IsDisposed) dialog.Close(); }))) {
                        dialog.ShowDialog(this); completion.TrySetResult(dialog.Choice == 1 && !ct.IsCancellationRequested);
                    }
                } catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        }
        internal bool RequiresCloseWarning => controller.ActionPending || everMayBeEnabled ||
            controller.Status.Godmode == GodmodeState.OnOwned || controller.Status.Godmode == GodmodeState.OnExternal;
        internal bool ApproveClosing()
        {
            if (closeApproved || !RequiresCloseWarning) return true;
            if (confirmClose != null) return confirmClose();
            using (var dialog = GamesDialogs.CreateCloseWarning()) {
                dialog.ShowDialog(this); return dialog.Choice == 1;
            }
        }
        internal void CommitCloseApproval() { closeApproved = true; }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!e.Cancel && !ApproveClosing()) e.Cancel = true;
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !closing) {
                closing = true; timer.Stop(); timer.Dispose(); binding?.Dispose();
                Interlocked.Exchange(ref generation, Interlocked.Increment(ref nextGeneration));
                lifetime.Cancel(); lifetime.Dispose(); transport.Invalidate();
                controller.Changed -= ControllerChanged; controller.Dispose();
                var old = icon.Image; icon.Image = null; old?.Dispose();
            }
            base.Dispose(disposing);
        }
        private static void ShowResult(DesktopNotificationService notifications, TrainerResult result)
        {
            if (notifications == null) return;
            var id = result.Identity;
            string details = (id == null ? "Unknown session" : id.Host + " / " + id.TitleId + " / PID " + id.Pid + " / session " + id.Generation) +
                "\r\n" + result.Outcome + ": " + result.Message;
            var kind = result.Outcome == ActionOutcome.Success ? MessageKind.Success :
                result.Outcome == ActionOutcome.NoChange ? MessageKind.Information : MessageKind.Warning;
            notifications.Show(new DesktopNotification(GamesController.ActionName(result.Action),
                (id?.Host ?? "Console") + " — " + result.Outcome + ": " + result.Message, kind, details,
                () => MessageDialog.ShowMessage(null, "Trainer result", details, kind)));
        }

        private async Task LoadIconAsync(ManagerSession source, long stamp, CancellationToken ct)
        {
            foreach (string path in new[] { TitlePaths.IconPath("CUSA02624"), "/system_data/priv/appmeta/external/CUSA02624/icon0.png" }) {
                Image decoded = null;
                try {
                    var bytes = await FtpGates.RunAsync(source.Gates.Browse, () => Task.Run(() => {
                        using (var input = source.Explorer.Ftp.OpenRead(path, 0))
                        using (ct.Register(input.Dispose))
                        using (var output = new MemoryStream()) {
                            var buffer = new byte[8192]; int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) != 0) {
                                ct.ThrowIfCancellationRequested();
                                if (output.Length + count > 1024 * 1024) throw new InvalidDataException("Icon is too large.");
                                output.Write(buffer, 0, count);
                            }
                            return output.ToArray();
                        }
                    }, ct), ct);
                    decoded = DecodeIcon(bytes);
                    if (closing || generation != stamp || ct.IsCancellationRequested) return;
                    icon.Image = decoded; decoded = null; return;
                } catch (OperationCanceledException) { return; }
                catch (Exception) { /* Optional metadata: the local name tile remains available. */ }
                finally { decoded?.Dispose(); }
            }
        }
        internal static Image DecodeIcon(byte[] bytes)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (bytes == null || bytes.Length < 24 || bytes.Length > 1024 * 1024 || !bytes.Take(8).SequenceEqual(signature) ||
                bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R') throw new InvalidDataException("Invalid PNG icon.");
            Func<int, uint> dimension = p => (uint)bytes[p] << 24 | (uint)bytes[p + 1] << 16 | (uint)bytes[p + 2] << 8 | bytes[p + 3];
            uint w = dimension(16), h = dimension(20);
            if (w == 0 || h == 0 || w > 2048 || h > 2048) throw new InvalidDataException("Invalid icon dimensions.");
            using (var stream = new MemoryStream(bytes)) using (var image = Image.FromStream(stream, false, true)) return new Bitmap(image);
        }
    }
}

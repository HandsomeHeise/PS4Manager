using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Config;
using PS4Manager.Core.Frame4;
using PS4Manager.Core.GoldHen;
using PS4Manager.Core.Session;

namespace PS4Manager.App
{
    internal sealed class Ps4HealthForm : AppWindow
    {
        private readonly ManagerSessionController _sessions;
        private readonly ManagerSettings _settings;
        private readonly Frame4PacketClient _frame4;
        private readonly ToolUiDispatcher _ui;
        private readonly ToolSessionBinding _binding;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly UiPanel _toolbar = new UiToolbar { Dock = DockStyle.Top };
        private readonly UiButton _refresh = new UiButton { Text = "Refresh", Dock = DockStyle.Left, AutoSize = true };
        private readonly UiLabel _status = new UiLabel { Dock = DockStyle.Fill, AutoSize = false, Text = "Not connected." };
        private readonly UiTextBox _details = new UiTextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true };
        private ManagerSession _session;
        private CancellationTokenSource _refreshCts;

        public Ps4HealthForm(ManagerSessionController sessions, ManagerSettings settings)
            : this(sessions, settings, new Frame4PacketClient())
        {
        }

        internal Ps4HealthForm(ManagerSessionController sessions, ManagerSettings settings, Frame4PacketClient frame4)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _frame4 = frame4 ?? throw new ArgumentNullException(nameof(frame4));
            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            Text = "PS4 Health";
            ClientSize = new System.Drawing.Size(720, 460);
            MinimumSize = new System.Drawing.Size(520, 340);
            StartPosition = FormStartPosition.CenterParent;

            _status.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
            _toolbar.Controls.Add(_status);
            _toolbar.Controls.Add(_refresh);
            Controls.Add(_details);
            Controls.Add(_toolbar);

            _refresh.Click += async (s, e) => await RefreshAsync();
            _binding = new ToolSessionBinding(_sessions, AttachSession, DetachSession);
            UpdateDisconnected();
        }

        private void AttachSession(ManagerSession session)
        {
            try
            {
                _ui.Dispatch(() =>
                {
                    if (IsDisposed) return;
                    _session = session;
                    _refresh.Enabled = true;
                    _status.Text = "Connected to " + session.Endpoint.Host + ".";
                    var _ = RefreshAsync();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void DetachSession(ManagerSession session)
        {
            try
            {
                _ui.Dispatch(() =>
                {
                    if (IsDisposed || !ReferenceEquals(_session, session)) return;
                    if (_refreshCts != null) _refreshCts.Cancel();
                    _session = null;
                    UpdateDisconnected();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void UpdateDisconnected()
        {
            _refresh.Enabled = true;
            _status.Text = "No FTP session.";
            _details.Text = "Connect the dashboard for FTP and GoldHEN checks. Frame4 can be checked from the saved IP.";
        }

        private async Task RefreshAsync()
        {
            var session = _session;
            if (_refreshCts != null) _refreshCts.Cancel();
            var cts = session == null
                ? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, session.CancellationToken);
            _refreshCts = cts;
            _refresh.Enabled = false;
            _status.Text = "Running health checks...";
            try
            {
                var lines = new List<string>();
                lines.Add("Endpoint: " + EndpointText(session));
                if (session == null)
                {
                    lines.Add("FTP: not connected");
                    lines.Add("GoldHEN: skipped without FTP");
                    lines.Add("Transfers: no FTP session");
                }
                else
                {
                    lines.Add("FTP: checking...");
                    await session.Explorer.CheckAsync(cts.Token).ConfigureAwait(false);
                    lines[lines.Count - 1] = "FTP: OK";

                    var discovery = await new GoldHenDiscoveryService(session.Explorer.Cache,
                        session.Explorer.Ftp, session.Explorer.Gates).DiscoverAsync(cts.Token).ConfigureAwait(false);
                    AddGoldHen(lines, discovery);
                    AddTransfers(lines, session);
                }
                await AddFrame4Async(lines, cts.Token).ConfigureAwait(false);
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _details.Text = string.Join(Environment.NewLine, lines);
                    _status.Text = "Health checks finished.";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _ui.Dispatch(() =>
                {
                    if (!IsCurrent(cts)) return;
                    _status.Text = "Health check failed: " + Ui.Describe(ex);
                });
            }
            finally
            {
                _ui.Dispatch(() =>
                {
                    if (IsCurrent(cts)) _refresh.Enabled = _session != null;
                });
            }
        }

        private async Task AddFrame4Async(List<string> lines, CancellationToken token)
        {
            lines.Add("");
            if (string.IsNullOrWhiteSpace(_settings.Ip))
            {
                lines.Add("Frame4: skipped, no IP configured");
                return;
            }

            try
            {
                var endpoint = new Frame4Endpoint(_settings.Ip, _settings.Frame4.HttpPort, _settings.Frame4.UartPort);
                var version = await _frame4.GetVersionAsync(endpoint, token).ConfigureAwait(false);
                var info = await _frame4.GetConsoleInfoAsync(endpoint, token).ConfigureAwait(false);
                lines.Add("Frame4: OK (" + version + ")");
                lines.Add("Firmware: " + Hex(info.UpdateVersion) + " · SDK " + Hex(info.SdkVersion));
                lines.Add("Model: " + info.HardwareModel + " · CPUs: " + info.CpuCount);
                lines.Add("Kernel: " + info.KernelVersion);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lines.Add("Frame4: " + Ui.Describe(ex));
            }
        }

        private bool IsCurrent(CancellationTokenSource cts)
        {
            return !IsDisposed && ReferenceEquals(_refreshCts, cts) && !cts.IsCancellationRequested;
        }

        private static void AddGoldHen(List<string> lines, GoldHenDiscovery discovery)
        {
            lines.Add("");
            lines.Add("GoldHEN root: " + (discovery.RootExists ? "found" : "missing"));
            lines.Add("Configuration: " + (discovery.ConfigurationPath ?? "missing"));
            foreach (var location in discovery.Locations.Where(l => l.Section != GoldHenSection.Configuration))
                lines.Add(SectionName(location.Section) + ": " +
                          (location.Exists ? location.Entries.Count + " file(s)" : "missing"));
            var active = discovery.Plugins.Count(p => p.Activation == GoldHenActivation.Enabled);
            var inactive = discovery.Plugins.Count(p => p.Activation == GoldHenActivation.Disabled);
            var unknown = discovery.Plugins.Count(p => p.Activation == GoldHenActivation.Unknown);
            lines.Add("Plugins: " + active + " active, " + inactive + " inactive, " + unknown + " unknown");
        }

        private static void AddTransfers(List<string> lines, ManagerSession session)
        {
            var jobs = session.Queue.Jobs;
            lines.Add("");
            lines.Add("Transfers: " + TransferText.Summary(jobs));
            lines.Add("Open edit sessions: " + session.Explorer.Editing.PendingSessions.Count);
        }

        private string EndpointText(ManagerSession session)
        {
            if (session != null) return session.Endpoint.Host + ":" + session.Endpoint.Port;
            return string.IsNullOrWhiteSpace(_settings.Ip)
                ? "not configured"
                : _settings.Ip + ":" + _settings.Port;
        }

        private static string Hex(uint value)
        {
            return "0x" + value.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string SectionName(GoldHenSection section)
        {
            switch (section)
            {
                case GoldHenSection.Plugins: return "Plugin folder";
                case GoldHenSection.Cheats: return "Cheat folder";
                case GoldHenSection.Patches: return "Patch folder";
                default: return "Configuration";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_refreshCts != null) _refreshCts.Cancel();
                _lifetime.Cancel();
                if (_binding != null) _binding.Dispose();
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

using PS4Manager.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.Core.Config;
using FileManager.Core.Editing;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Session;
using Ps4.Core.Ftp;
using TreeNode = PS4Manager.UI.NavigationNode;

namespace FileManager.App
{
    public sealed partial class FileManagerForm
    {
        /// <summary>Nach „Verbinden" — die Sitzung ist geprüft und aktiv.</summary>
        private event Action<ExplorerSession> SessionStarted;

        partial void WireConnection()
        {
            // Gemeinsame Sitzung: Verbinden und Trennen führt nur das Dashboard;
            // dieses Fenster bittet lediglich darum (Knopf „Connect" bzw.
            // „Select console …").
            _connectItem.Click += (s, e) => RaiseConnectionRequested();
            _targetItem.Click += (s, e) => RaiseConnectionRequested();
        }

        private void RaiseConnectionRequested()
        {
            var requested = ConnectionRequested;
            if (requested != null) requested(this, EventArgs.Empty);
        }

        /// <summary>
        /// Was ein Trennen oder Beenden kostet — das Dashboard bündelt das vor
        /// dem Schließen aller Fenster zu einer Warnung (ILeaveWarningSource).
        /// </summary>
        public IReadOnlyList<string> CollectLeaveWarnings()
        {
            var warnings = new List<string>();
            var collect = CollectingCloseWarnings;
            if (collect != null) collect(warnings);
            return warnings;
        }

        /// <summary>Schnellzugriff (Spec „PS4-Wissen"): nur Vorhandenes, dazu die angehefteten Ordner.</summary>
        private async Task LoadQuickAccessAsync(ManagerSession managerSession, CancellationToken token)
        {
            var session = managerSession.Explorer;
            try
            {
                var entries = await session.QuickAccess.BuildAsync(_settings.PinnedFolders, token);
                if (!IsCurrentSession(managerSession, token)) return;
                _quickRoot.Children.ReplaceWith(entries.Select(e => FolderNode(e.Label, e.Path)).ToList());
                _quickRoot.HasChildrenHint = _quickRoot.Children.Count > 0;
                _quickRoot.Expand();
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(managerSession, token) && !(ex is OperationCanceledException))
                    SetStatus("Quick access: " + Ui.Describe(ex));
            }
        }

        private void AttachSession(ManagerSession managerSession)
        {
            _requestedManagerSession = managerSession;
            RunSessionChangeOnUi(ApplyRequestedSessionOnUi);
        }

        private void AttachSessionOnUi(ManagerSession managerSession)
        {
            if (IsDisposed || managerSession.CancellationToken.IsCancellationRequested) return;
            _managerSession = managerSession;
            _session = managerSession.Explorer;
            _managerSettings.Ip = managerSession.Endpoint.Host;
            _managerSettings.Port = managerSession.Endpoint.Port;
            _managerSettings.ExpertMode = _session.ExpertMode;
            _expertItem.Checked = _session.ExpertMode;
            _consoleRoot.HasChildrenHint = true;
            Text = "PS4 File Manager — " + managerSession.Endpoint.Host + ":" + managerSession.Endpoint.Port;
            var started = SessionStarted;
            if (started != null) started(_session);
            UpdateTargetText();
            UpdateCommands();
            var _ = InitializeAttachedSessionAsync(managerSession);
        }

        private async Task InitializeAttachedSessionAsync(ManagerSession managerSession)
        {
            var token = managerSession.CancellationToken;
            await LoadQuickAccessAsync(managerSession, token);
            if (!IsCurrentSession(managerSession, token)) return;
            await NavigateToAsync(string.IsNullOrEmpty(_settings.LastPath) ? "/data" : _settings.LastPath);
            if (!IsCurrentSession(managerSession, token)) return;
            if (_currentPath == null) await NavigateToAsync("/data");
        }

        private void DetachSession(ManagerSession managerSession)
        {
            if (ReferenceEquals(_requestedManagerSession, managerSession)) _requestedManagerSession = null;
            RunSessionChangeOnUi(ApplyRequestedSessionOnUi);
        }

        private void ApplyRequestedSessionOnUi()
        {
            if (IsDisposed) return;
            var requested = _requestedManagerSession;
            if (ReferenceEquals(_managerSession, requested)) return;

            var attached = _managerSession;
            if (attached != null) DetachSessionOnUi(attached);
            if (requested != null && ReferenceEquals(_requestedManagerSession, requested))
                AttachSessionOnUi(requested);
        }

        private void DetachSessionOnUi(ManagerSession managerSession)
        {
            if (!ReferenceEquals(_managerSession, managerSession)) return;
            var session = managerSession.Explorer;
            var ending = SessionEnding;
            if (ending != null) ending(session);
            _managerSession = null;
            _session = null;
            _currentPath = null;
            ClearView();
            ClearTitleIcons();
            Text = "PS4 File Manager";
            UpdateTargetText();
            UpdateCommands();
            UpdateStatus();
        }

        private bool IsCurrentSession(ManagerSession managerSession, CancellationToken token)
        {
            return !token.IsCancellationRequested && ReferenceEquals(managerSession, _managerSession);
        }

        private void RunSessionChangeOnUi(Action action)
        {
            if (IsDisposed) return;
            try
            {
                _uiDispatcher.Dispatch(() =>
                {
                    if (!IsDisposed) action();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}

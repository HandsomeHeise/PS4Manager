using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.App.Views;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace FileManager.App
{
    public sealed partial class FileManagerForm
    {
        private readonly TransferPanel _transfers = new TransferPanel();
        private readonly HashSet<TransferJob> _finishedSeen = new HashSet<TransferJob>();

        /// <summary>Einmal je fertigem Auftrag, auf dem UI-Thread, nach der Cache-Invalidierung.</summary>
        private event Action<TransferJob> JobFinished;

        partial void AddTransferPanel()
        {
            Controls.Add(_transfers);
        }

        partial void WireTransfers()
        {
            SessionStarted += session =>
            {
                session.Queue.JobChanged += OnJobChanged;
                ShowJobs();
            };
            SessionEnding += session =>
            {
                session.Queue.JobChanged -= OnJobChanged;
                _finishedSeen.Clear();
                _transfers.ShowJobs(new TransferJob[0]);
                UpdateTransferButtons();
            };
            // Aufträge abbrechen, auf Ruhe warten und die Warteschlange melden
            // macht das Dashboard: Die Warteschlange ist global und kann auch aus
            // der Galerie gefüllt sein.

            _transfers.CancelJob.Click += async (s, e) =>
            {
                var managerSession = _managerSession;
                if (managerSession == null) return;
                var token = managerSession.CancellationToken;
                var job = SelectedOrRunningJob();
                if (job == null) return;
                await managerSession.Explorer.Queue.CancelAsync(job);
                if (!IsCurrentSession(managerSession, token)) return;
                UpdateTransferButtons();
            };
            _transfers.ResumeJob.Click += (s, e) =>
            {
                var job = _transfers.SelectedJob;
                if (job != null && _session != null && job.CanResume) _session.Queue.Resume(job);
            };
            _transfers.ClearList.Click += (s, e) =>
            {
                if (_session == null) return;
                _session.Queue.ClearFinished();
                ShowJobs();
            };
            _transfers.ShowFailures.Click += (s, e) => ShowFailuresOf(_transfers.SelectedJob);
            _transfers.Grid.Selection.Changed += (s, e) => UpdateTransferButtons();
            UpdateTransferButtons();
        }

        /// <summary>Arbeits-Thread der Warteschlange → UI-Thread (Übergabe Task 15).</summary>
        private void OnJobChanged(object sender, TransferJob job)
        {
            if (IsDisposed || !IsHandleCreated) return;
            var queue = (TransferQueue)sender;
            var managerSession = _managerSession;
            if (managerSession == null || managerSession.Explorer.Queue != queue) return;
            var token = managerSession.CancellationToken;
            if (token.IsCancellationRequested) return;
            try
            {
                BeginInvoke((Action)(() => OnJobChangedOnUi(managerSession, token, queue, job)));
            }
            catch (InvalidOperationException)
            {
                // Fenster schließt gerade — nichts mehr anzuzeigen.
            }
        }

        private void OnJobChangedOnUi(PS4Manager.Core.Session.ManagerSession managerSession,
            System.Threading.CancellationToken token, TransferQueue queue, TransferJob job)
        {
            if (!IsCurrentSession(managerSession, token)) return;
            var session = managerSession.Explorer;
            if (session.Queue != queue) return;

            if (!_transfers.Expanded && job.State == JobState.Waiting) _transfers.Expanded = true;
            ShowJobs();

            if (!job.IsFinished || !_finishedSeen.Add(job)) return;

            var folders = session.ApplyFinished(job);
            ReloadTreeFolders(folders);
            if (_currentPath != null && folders.Any(f => RemotePath.Canonical(f) == RemotePath.Canonical(_currentPath)))
            {
                var _ = RefreshAsync(false);
            }
            if (job.State == JobState.CompletedWithErrors) SetStatus(job.Title + ": " + job.Summary);

            var finished = JobFinished;
            if (finished != null) finished(job);
        }

        private void ShowJobs()
        {
            _transfers.ShowJobs(_session != null ? _session.Queue.Jobs : (IReadOnlyList<TransferJob>)new TransferJob[0]);
            UpdateTransferButtons();
        }

        /// <summary>
        /// Die Übertragungsleiste aufklappen — Ziel des Dashboard-Knopfs „Transfers".
        /// Es gibt nur eine Warteschlange, also zeigt auch nur dieses Fenster sie.
        /// </summary>
        public void ShowTransfers()
        {
            _transfers.Expanded = true;
            _transfers.Toggle.Focus();
        }

        private TransferJob SelectedOrRunningJob()
        {
            var selected = _transfers.SelectedJob;
            if (selected != null && !selected.IsFinished) return selected;
            return _session == null ? null : _session.Queue.Jobs.FirstOrDefault(j => j.State == JobState.Running);
        }

        private void UpdateTransferButtons()
        {
            var job = _transfers.SelectedJob;
            _transfers.CancelJob.Enabled = SelectedOrRunningJob() != null;
            _transfers.ResumeJob.Enabled = job != null && job.CanResume;
            _transfers.ShowFailures.Enabled = job != null && job.Failures.Count > 0;
        }

        /// <summary>Bilanz mit Grund je Datei (Spec „Permanenter Fehler … Bilanz").</summary>
        private void ShowFailuresOf(TransferJob job)
        {
            if (job == null || job.Failures.Count == 0) return;
            var lines = job.Failures.Take(20)
                .Select(f => RemotePath.Name(f.RemotePath ?? f.LocalPath ?? "") + " — " + f.Error)
                .ToList();
            if (job.Failures.Count > 20) lines.Add("… and " + (job.Failures.Count - 20) + " more");
            MessageDialog.Info(this, job.Title, job.Summary + "\n\n" + string.Join("\n", lines));
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileManager.App.Dialogs;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.Session;

namespace FileManager.App
{
    public sealed partial class FileManagerForm
    {
        private const int ErrorNoAssociation = 1155;

        private sealed class PendingOpen
        {
            public RemoteItem Item;
            public string LocalPath;
            public bool IntegratedEditor;
        }

        private readonly Dictionary<TransferJob, PendingOpen> _openJobs = new Dictionary<TransferJob, PendingOpen>();
        private readonly Queue<EditSession> _changed = new Queue<EditSession>();
        private readonly System.Windows.Forms.Timer _editTimer = new System.Windows.Forms.Timer { Interval = 250 };
        private bool _askingWriteBack;

        partial void WireEditing()
        {
            FileActivated += OpenFile;
            JobFinished += OnEditJobFinished;
            SessionStarted += session => session.Editing.ChangeDetected += OnChangeDetected;
            SessionEnding += session =>
            {
                session.Editing.ChangeDetected -= OnChangeDetected;
                _openJobs.Clear();
                _changed.Clear();
            };
            CollectingCloseWarnings += warnings =>
            {
                if (_session == null) return;
                int pending = _session.Editing.PendingSessions.Count;
                if (pending == 1) warnings.Add("1 changed file has not been written back to the PS4 yet.");
                else if (pending > 1) warnings.Add(pending + " changed files have not been written back to the PS4 yet.");
            };

            _editTimer.Tick += (s, e) =>
            {
                var managerSession = _managerSession;
                if (managerSession != null && !managerSession.CancellationToken.IsCancellationRequested)
                    managerSession.Explorer.Editing.Tick();
            };
            _editTimer.Start();
            FormClosed += (s, e) => _editTimer.Dispose();
        }

        /// <summary>Doppelklick/Enter auf eine Datei: laden, mit dem Windows-Programm öffnen, beobachten.</summary>
        private void OpenFile(FolderRow row)
        {
            var managerSession = _managerSession;
            if (managerSession == null || managerSession.CancellationToken.IsCancellationRequested
                || row.IsFolder || row.IsLink) return;
            var session = managerSession.Explorer;
            if (EditSessionManager.NeedsSizeConfirmation(row.Entry.Size)
                && !MessageDialog.Ask(this, "Open",
                    "“" + row.DisplayName + "” is " + SizeFormat.Bytes(row.Entry.Size) + " in size.\nThe file must be downloaded completely before it can be opened.",
                    "Open", "Cancel"))
            {
                return;
            }
            if (!IsCurrentSession(managerSession, managerSession.CancellationToken)) return;

            var local = session.Editing.PrepareLocalPath(row.Item.Path);
            var job = session.Operations.PlanOpenForEdit(row.Item, local);
            _openJobs[job] = new PendingOpen
            {
                Item = row.Item,
                LocalPath = local,
                IntegratedEditor = IniFilePolicy.IsEditable(row.Item.Path)
            };
            session.Queue.Enqueue(job);
            SetStatus(job.Title + " — downloading …");
        }

        private async void OnEditJobFinished(TransferJob job)
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            var session = managerSession.Explorer;

            PendingOpen open;
            if (_openJobs.TryGetValue(job, out open))
            {
                _openJobs.Remove(job);
                if (job.State != JobState.Completed)
                {
                    SetStatus(job.Title + ": " + job.Summary);
                    return;
                }
                try
                {
                    var openedEdit = await session.Editing.RegisterAsync(open.Item.Path, open.LocalPath,
                        open.Item.Size, open.Item.Modified, token);
                    if (!IsCurrentSession(managerSession, token)) return;
                    if (open.IntegratedEditor) OpenIniEditor(managerSession, openedEdit);
                    else ShellOpen(open.LocalPath);
                }
                catch (OperationCanceledException)
                {
                    if (IsCurrentSession(managerSession, token)) SetStatus("Opening the file was cancelled.");
                }
                catch (Exception ex)
                {
                    if (IsCurrentSession(managerSession, token)) SetStatus("Error: " + Ui.Describe(ex));
                }
                return;
            }
        }

        private void OpenIniEditor(ManagerSession managerSession, EditSession edit)
        {
            try
            {
                if (!IsCurrentSession(managerSession, managerSession.CancellationToken)) return;
                var session = managerSession.Explorer;
                var document = TextFileContent.Load(edit.LocalPath);
                using (var dialog = new IniEditorDialog(RemotePath.Name(edit.RemotePath), document.Text,
                    value => SaveIniAsync(managerSession, edit, document, value)))
                {
                    dialog.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(managerSession, managerSession.CancellationToken)) ShowError(ex);
            }
        }

        private async Task<bool> SaveIniAsync(ManagerSession managerSession, EditSession edit,
            TextFileContent document, string text)
        {
            var token = managerSession.CancellationToken;
            if (!IsCurrentSession(managerSession, token)) return false;
            var session = managerSession.Explorer;
            document.Save(edit.LocalPath, text);
            session.Editing.MarkLocalSavePending(edit);

            bool remoteChanged = await session.Editing.RemoteChangedAsync(edit, token).ConfigureAwait(true);
            if (!IsCurrentSession(managerSession, token)) return false;
            if (remoteChanged)
            {
                var overwrite = MessageDialog.Ask(this, "Write back",
                    "“" + RemotePath.Name(edit.RemotePath) + "” has changed on the PS4 in the meantime.\nOverwrite it with the PC version anyway?",
                    "Overwrite", "Cancel");
                if (!IsCurrentSession(managerSession, token) || !overwrite) return false;
                session.Editing.AcceptObservedRemoteVersion(edit);
            }

            var job = await session.Operations.PlanWriteBackAsync(edit.LocalPath, edit.RemotePath, token)
                .ConfigureAwait(true);
            if (!IsCurrentSession(managerSession, token) || job == null) return false;
            session.Editing.ProtectWriteBack(job, edit);
            var completion = session.Editing.TrackWriteBackAsync(job, edit);
            session.Queue.Enqueue(job);
            var state = await completion.ConfigureAwait(true);
            if (!IsCurrentSession(managerSession, token)) return false;
            if (state != JobState.Completed)
            {
                SetStatus(job.Title + ": " + job.Summary);
                return false;
            }
            SetStatus(RemotePath.Name(edit.RemotePath) + " was written back to the PS4.");
            return true;
        }

        /// <summary>Windows-Standardprogramm; ohne Zuordnung der „Öffnen mit"-Dialog (Spec).</summary>
        private void ShellOpen(string localPath)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true, Verb = "open" })) { }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNoAssociation)
            {
                using (Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL \"" + localPath + "\"")) { }
            }
            catch (Exception ex)
            {
                SetStatus("Open failed: " + Ui.Describe(ex));
            }
        }

        /// <summary>
        /// Kommt aus Tick, also auf dem UI-Thread. In eine Warteschlange statt
        /// sofort fragen: melden mehrere Dateien im selben Takt, darf keine
        /// Rückfrage verloren gehen, während eine andere offen ist.
        /// </summary>
        private void OnChangeDetected(object sender, EditSession edit)
        {
            var managerSession = _managerSession;
            if (managerSession == null || managerSession.CancellationToken.IsCancellationRequested
                || !ReferenceEquals(sender, managerSession.Explorer.Editing)) return;
            if (!_changed.Contains(edit)) _changed.Enqueue(edit);
            if (!_askingWriteBack)
            {
                var _ = AskQueuedWriteBacksAsync();
            }
        }

        private async Task AskQueuedWriteBacksAsync()
        {
            _askingWriteBack = true;
            try
            {
                while (_changed.Count > 0) await AskWriteBackAsync(_changed.Dequeue());
            }
            finally
            {
                _askingWriteBack = false;
            }
        }

        private async Task AskWriteBackAsync(EditSession edit)
        {
            var managerSession = _managerSession;
            if (managerSession == null) return;
            var token = managerSession.CancellationToken;
            var session = managerSession.Explorer;
            var name = RemotePath.Name(edit.RemotePath);
            try
            {
                var writeBack = MessageDialog.Ask(this, "Write back",
                    "“" + name + "” was changed.\nWrite it back to the PS4?", "Write back", "No");
                if (!IsCurrentSession(managerSession, token)) return;
                if (!writeBack)
                {
                    session.Editing.Discard(edit);
                    return;
                }

                bool remoteChanged = await session.Editing.RemoteChangedAsync(edit, token);
                if (!IsCurrentSession(managerSession, token)) return;
                if (remoteChanged)
                {
                    var overwrite = MessageDialog.Ask(this, "Write back",
                        "“" + name + "” has changed on the PS4 in the meantime.\nOverwrite it with the PC version anyway?", "Overwrite", "Cancel");
                    if (!IsCurrentSession(managerSession, token)) return;
                    if (!overwrite)
                    {
                        session.Editing.Discard(edit);
                        return;
                    }
                    session.Editing.AcceptObservedRemoteVersion(edit);
                }

                var job = await session.Operations.PlanWriteBackAsync(edit.LocalPath, edit.RemotePath, token);
                if (!IsCurrentSession(managerSession, token)) return;
                if (job == null)
                {
                    session.Editing.Discard(edit);              // Warnzone abgelehnt
                    return;
                }
                session.Editing.ProtectWriteBack(job, edit);
                var completion = session.Editing.TrackWriteBackAsync(job, edit);
                session.Queue.Enqueue(job);
                var _ = ObserveWriteBackAsync(managerSession, edit, job, completion);
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(managerSession, token)) ShowError(ex);
            }
        }

        private async Task ObserveWriteBackAsync(ManagerSession managerSession, EditSession edit,
            TransferJob job, Task<JobState> completion)
        {
            var token = managerSession.CancellationToken;
            try
            {
                var state = await completion.ConfigureAwait(true);
                if (!IsCurrentSession(managerSession, token)) return;
                if (state == JobState.Completed)
                    SetStatus(RemotePath.Name(edit.RemotePath) + " was written back to the PS4.");
                else
                    SetStatus(job.Title + ": " + job.Summary);
            }
            catch (Exception ex)
            {
                if (IsCurrentSession(managerSession, token)) ShowError(ex);
            }
        }
    }
}

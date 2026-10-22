using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using PS4Manager.Core.Luma;
using PS4Manager.Core.Luma.Studio;

namespace PS4Manager.App
{
    internal sealed partial class LumaStudioForm
    {
        private int remoteRevision;
        private void LoadConnectionSettings()
        {
            loading = true;
            try {
                game.SelectedIndex = settings.Luma.Game == "rdr2" ? 1 : 0;
                host.Text = settings.Ip ?? ""; port.Text = settings.Luma.Port.ToString(CultureInfo.InvariantCulture); token.Text = settings.Luma.Token ?? "";
            } finally { loading = false; }
        }
        private async Task ConnectStudioAsync()
        {
            try {
                int number;
                if (!int.TryParse(port.Text, out number)) throw new ArgumentException("Port must be a number.");
                var endpoint = new LumaEndpoint(host.Text, number);
                settings.Luma.Game = Profile; settings.Luma.Port = number; settings.Luma.Token = token.Text; settings.Ip = endpoint.Host; saveSettings();
                string profile = Profile, auth = token.Text;
                await RunRemoteAsync("Connect", ct => connection.ConnectAsync(endpoint, profile, auth, ct));
                if (connection.Connected && !closing) pollTimer.Start();
            } catch (Exception ex) { if (!closing) status.Text = "Connect failed: " + ex.Message; }
        }
        private void DisconnectStudio()
        {
            ++remoteRevision; pollTimer.Stop(); connection.Disconnect(); busy = false;
            execution = null; logLines.Clear(); console.Text = "";
            status.Text = "Disconnected · no scripts were changed."; UpdateCommands();
        }
        private async Task ExecuteDocumentAsync()
        {
            var buffer = ActiveBuffer; if (buffer == null || busy) return;
            execution = new StudioExecution(buffer.Document, connection.Generation);
            string source = buffer.Document.Source;
            string chunkName = execution.ChunkName;
            await RunRemoteAsync("Execute", ct => connection.ExecuteAsync(source, chunkName, ct));
        }
        private async Task UploadDocumentAsync()
        {
            var buffer = ActiveBuffer; if (buffer == null || busy || !connection.Connected) return;
            string fileName = Path.GetFileName(buffer.Document.FilePath ?? buffer.Document.Title);
            // Filename is validated by the core. An unsaved default buffer still has an explicit target.
            string source = buffer.Document.Source, profile = connection.Profile;
            long generation = connection.Generation;
            string remote = (profile == "gta5" ? "/data/gtalua/" : "/data/rdr2lua/") + "scripts/" + fileName;
            if (XtraMessageBox.Show(this,
                "Upload this buffer to " + host.Text + ":" + port.Text + "?\r\n\r\n" + (profile == "gta5" ? "GTA V" : "Red Dead Redemption 2") + "\r\n" + remote +
                "\r\n\r\nAn existing file at this path will be REPLACED.\r\nScripts will NOT be executed or reloaded. Use Reload separately when ready.",
                "Confirm Lua upload / replacement", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await RunRemoteAsync("Upload", ct => connection.UploadAsync(fileName, source, generation, ct));
        }
        private async Task RunRemoteAsync(string operation, Func<CancellationToken, Task<string>> action, bool silent = false)
        {
            if (busy || closing) return;
            int revision = ++remoteRevision; busy = true; UpdateCommands();
            if (!silent) status.Text = operation + "…";
            try {
                string result = await action(lifetime.Token);
                if (closing || revision != remoteRevision) return;
                if (!silent) { AppendConsole(operation + ": " + result); status.Text = operation == "Upload" ? "Uploaded only · click Reload separately to load the file." : operation + " completed."; }
            } catch (OperationCanceledException) { if (!closing && revision == remoteRevision) status.Text = "Luma operation cancelled; reconnect if necessary."; }
            catch (Exception ex) {
                if (!closing && revision == remoteRevision) { AppendConsole(operation + " failed: " + ex.Message); status.Text = operation + " failed: " + ex.Message; AddRuntimeDiagnostic(ex.Message); }
            } finally {
                if (!closing && revision == remoteRevision) { busy = false; if (!connection.Connected) pollTimer.Stop(); UpdateCommands(); }
            }
        }
        private void OnStudioLog(object sender, StudioLogEventArgs e)
        {
            if (closing || !IsHandleCreated || e.Generation != connection.Generation) return;
            try { BeginInvoke(new Action(() => {
                if (closing || e.Generation != connection.Generation) return;
                AppendConsole(e.Text); AddRuntimeDiagnostic(e.Text);
            })); } catch (InvalidOperationException) { }
        }
        private void AppendConsole(string text)
        {
            text = text ?? "";
            if (text.Length > 65536) text = text.Substring(0, 65536) + "\n[message truncated]";
            foreach (string line in text.Replace("\r", "").Split('\n')) {
                logLines.Enqueue(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + (line.Length > 4096 ? line.Substring(0, 4096) + "…" : line));
                while (logLines.Count > 1500) logLines.Dequeue();
            }
            console.Text = string.Join("\r\n", logLines); console.SelectionStart = console.Text.Length; console.ScrollToCaret();
        }
        private void AddRuntimeDiagnostic(string message)
        {
            if (execution == null) return;
            Buffer buffer;
            if (!buffers.TryGetValue(execution.DocumentId, out buffer)) return;
            var diagnostic = execution.Diagnostic(message, buffer.Document, connection.Generation);
            if (diagnostic == null) return;
            buffer.Check = new LuaCheckResult(true, new[] { diagnostic });
            buffer.CheckedVersion = buffer.Document.Version;
            if (buffer == ActiveBuffer) ShowDiagnostics();
        }
    }
}

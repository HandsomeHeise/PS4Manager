using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using FileManager.Core.Zones;

namespace FileManager.App.Dialogs
{
    /// <summary>
    /// Die Rückfragen des Kerns als Dialoge (Spec „Dialoge"). Der Kern ruft sie
    /// aus Hintergrund-Threads — Ui.OnUiAsync bringt sie auf den UI-Thread.
    /// Standardknopf ist jeweils „Abbrechen": Enter soll nichts zerstören.
    ///
    /// Öffentlich, damit das Dashboard (PS4Manager.App) die Rückfragen der
    /// gemeinsamen Sitzung mit denselben Dialogen bedient.
    /// </summary>
    public sealed class DialogPrompts : IUserPrompts
    {
        private const int MaxListed = 5;
        private readonly Form _owner;

        public DialogPrompts(Form owner)
        {
            _owner = owner;
        }

        public Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct)
        {
            var text = "“" + action + "” affects a protected area of the PS4 (warning zone):\n"
                       + Listing(paths)
                       + "\nErrors here can damage games or save data.";
            return Ui.OnUiAsync(_owner, () => Confirm("Warning zone", text, "Continue"));
        }

        public Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct)
        {
            var parts = new List<string> { Count(summary.FileCount, "file", "files") };
            if (summary.FolderCount > 0) parts.Add(Count(summary.FolderCount, "folder", "folders"));
            if (summary.TotalBytes > 0) parts.Add(SizeFormat.Bytes(summary.TotalBytes));

            var text = "Permanently delete: " + string.Join(", ", parts) + "\n"
                       + Listing(summary.TopLevelNames)
                       + "\nThe PS4 has no recycle bin.";
            if (summary.Zone == Zone.Warn)
                text += "\n\nWarning: This is in a protected area (warning zone) —\nerrors here can damage games or save data.";
            return Ui.OnUiAsync(_owner, () => Confirm("Permanently delete", text, "Delete"));
        }

        private bool Confirm(string title, string text, string yes)
        {
            using (var dialog = new MessageDialog(title, text, new[] { yes, "Cancel" }, 1, 1, null))
            {
                dialog.ShowDialog(_owner);
                return dialog.Choice == 0;
            }
        }

        private static string Listing(IReadOnlyList<string> names)
        {
            var lines = names.Take(MaxListed).Select(n => "   " + n).ToList();
            if (names.Count > MaxListed) lines.Add("   … and " + (names.Count - MaxListed) + " more");
            return string.Join("\n", lines) + "\n";
        }

        private static string Count(int n, string one, string many)
        {
            return n == 1 ? "1 " + one : n + " " + many;
        }
    }
}

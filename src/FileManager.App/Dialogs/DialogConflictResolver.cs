using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;

namespace FileManager.App.Dialogs
{
    /// <summary>
    /// Konfliktdialog (Spec „Konflikte"): Ersetzen, Überspringen, Beide behalten,
    /// dazu „für alle weiteren". Die Warteschlange wartet auf die Antwort; der
    /// Dialog läuft auf dem UI-Thread (nie umgekehrt warten).
    ///
    /// Öffentlich, damit das Dashboard (PS4Manager.App) die gemeinsame
    /// Warteschlange mit demselben Dialog bedient.
    /// </summary>
    public sealed class DialogConflictResolver : IConflictResolver
    {
        private readonly Form _owner;

        public DialogConflictResolver(Form owner)
        {
            _owner = owner;
        }

        public Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct)
        {
            return Ui.OnUiAsync(_owner, () => Ask(conflict));
        }

        private ConflictDecision Ask(ConflictInfo conflict)
        {
            var name = conflict.TargetIsRemote ? RemotePath.Name(conflict.TargetPath) : Path.GetFileName(conflict.TargetPath);
            var where = conflict.TargetIsRemote ? "on the PS4" : "on the PC";
            var text = "“" + name + "” already exists " + where + ".\n"
                       + "Existing: " + SizeFormat.Bytes(conflict.ExistingSize) + " · New: " + SizeFormat.Bytes(conflict.NewSize) + "\n"
                       + "\n“Keep both” creates the new file as “" + conflict.KeepBothName + "”.";
            using (var dialog = new MessageDialog("File already exists", text,
                new[] { "Replace", "Skip", "Keep both" }, 1, 1,
                "Apply to all remaining conflicts in this job"))
            {
                dialog.ShowDialog(_owner);
                var choice = dialog.Choice == 0 ? ConflictChoice.Replace
                           : dialog.Choice == 2 ? ConflictChoice.KeepBoth
                           : ConflictChoice.Skip;
                return new ConflictDecision(choice, dialog.Checked);
            }
        }
    }
}

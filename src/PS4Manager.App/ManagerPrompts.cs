using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.Core.Transfers;

namespace PS4Manager.App
{
    /// <summary>
    /// Die Rückfragen der gemeinsamen Sitzung (Zonenwarnung, Löschbilanz). Sie
    /// gehören dem Dashboard — ein Satz für alle Werkzeuge —, die Dialoge selbst
    /// sind die bewährten des File Managers.
    ///
    /// Das Besitzerfenster wird pro Frage neu bestimmt: Ein Dialog über dem
    /// Dashboard wäre hinter einem gerade aktiven Werkzeugfenster versteckt.
    /// </summary>
    internal sealed class ManagerPrompts : IUserPrompts
    {
        private readonly Func<Form> _owner;

        public ManagerPrompts(Func<Form> owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public Task<bool> ConfirmWarnZoneAsync(string action, IReadOnlyList<string> paths, CancellationToken ct)
        {
            return new DialogPrompts(_owner()).ConfirmWarnZoneAsync(action, paths, ct);
        }

        public Task<bool> ConfirmDeleteAsync(DeleteSummary summary, CancellationToken ct)
        {
            return new DialogPrompts(_owner()).ConfirmDeleteAsync(summary, ct);
        }
    }

    /// <summary>
    /// Der Konfliktdialog der gemeinsamen Warteschlange — Besitzerfenster wie
    /// bei <see cref="ManagerPrompts"/>.
    /// </summary>
    internal sealed class ManagerConflictResolver : IConflictResolver
    {
        private readonly Func<Form> _owner;

        public ManagerConflictResolver(Func<Form> owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public Task<ConflictDecision> ResolveAsync(ConflictInfo conflict, CancellationToken ct)
        {
            return new DialogConflictResolver(_owner()).ResolveAsync(conflict, ct);
        }
    }
}

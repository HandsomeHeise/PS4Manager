using System.Collections.Generic;

namespace PS4Manager.Core.Presentation
{
    /// <summary>
    /// Ein Werkzeug meldet, was das Verlassen der gemeinsamen Sitzung kostet
    /// (ungesicherte Änderungen, offene Rückfragen). Das Dashboard bündelt die
    /// Meldungen aller offenen Fenster zu EINER Warnung (Spec „Dashboard").
    ///
    /// Die globale Warteschlange gehört bewusst nicht hierher: Übertragungen
    /// sind fensterunabhängig, ihre Warnung erhebt das Dashboard selbst.
    /// </summary>
    public interface ILeaveWarningSource
    {
        IReadOnlyList<string> CollectLeaveWarnings();
    }
}

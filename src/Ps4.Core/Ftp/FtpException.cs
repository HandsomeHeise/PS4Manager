using System;

namespace Ps4.Core.Ftp
{
    public enum FtpErrorKind
    {
        /// <summary>Verbindung/Zeit — ein erneuter Versuch kann gelingen.</summary>
        Transient,
        /// <summary>Der Server hat die Aktion abgelehnt — ein erneuter Versuch ändert nichts.</summary>
        Permanent
    }

    /// <summary>
    /// Das Einzige, was oberhalb von FtpClient an Netz-/Serverfehlern ankommt.
    /// Die Nachricht ist deutscher Klartext für den Anwender; die ursprüngliche
    /// Ausnahme bleibt als InnerException erhalten (die Gallery zeigt weiterhin
    /// die innerste Ursache).
    /// </summary>
    public sealed class FtpException : Exception
    {
        public FtpException(string message, FtpErrorKind kind, int statusCode)
            : this(message, kind, statusCode, null)
        {
        }

        public FtpException(string message, FtpErrorKind kind, int statusCode, Exception inner)
            : base(message, inner)
        {
            Kind = kind;
            StatusCode = statusCode;
        }

        public FtpErrorKind Kind { get; }

        /// <summary>FTP-Antwortcode; 0, wenn gar keine FTP-Antwort kam (Transportfehler).</summary>
        public int StatusCode { get; }

        public bool IsTransient { get { return Kind == FtpErrorKind.Transient; } }
    }
}

using System;
using System.IO;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Ein Lese-/Schreibfehler auf der LOKALEN Datei während eines Transfers
    /// (Zieldatei gesperrt, Platte voll) — bleibt dadurch für
    /// FtpErrors.Translate von einem Verbindungsabbruch zur PS4
    /// unterscheidbar (sonst würde eine gewöhnliche IOException als
    /// FtpException(Transient) missverstanden: „Verbindung unterbrochen —
    /// später fortsetzen" statt „diese Datei ist fehlgeschlagen").
    /// </summary>
    internal sealed class LocalFileException : IOException
    {
        public LocalFileException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}

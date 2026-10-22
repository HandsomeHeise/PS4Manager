using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Naht zwischen Logik und Draht — im Test ein Dictionary bzw. ein
    /// In-Memory-Dateisystem. Verträge für alle Methoden:
    /// Netz-/Serverfehler kommen als FtpException (Transient/Permanent),
    /// ein Abbruch durch den Aufrufer als OperationCanceledException, lokale
    /// Dateifehler unverändert. progress meldet immer die in DIESEM Aufruf
    /// übertragenen Bytes ab 0.
    /// </summary>
    public interface IFtpClient
    {
        Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct);
        Task<byte[]> DownloadAsync(string path, CancellationToken ct);

        /// <summary>
        /// Lädt streamend in eine Datei — für Clips, die als byte[] den Speicher
        /// sprengen würden. progress meldet die bisher übertragenen Bytes (die
        /// Gesamtgröße kennt der Aufrufer aus dem Listing).
        /// </summary>
        Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct);

        /// <summary>
        /// Wie oben, aber ab offset (REST): die lokale Datei muss dann genau
        /// offset Bytes lang sein und wird fortgeschrieben.
        /// </summary>
        Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct);

        /// <summary>Synchron: für den Shell-Stream von Explorer, nie auf dem UI-Thread.</summary>
        Stream OpenRead(string path, long offset);

        Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct);

        /// <summary>APPE: hängt die lokale Datei ab localOffset an die entfernte an (Fortsetzen).</summary>
        Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct);

        Task<long> GetFileSizeAsync(string path, CancellationToken ct);
        Task MakeDirectoryAsync(string path, CancellationToken ct);

        /// <summary>Nur leere Ordner — rekursives Löschen ist Sache des Aufrufers.</summary>
        Task RemoveDirectoryAsync(string path, CancellationToken ct);

        /// <summary>RNFR/RNTO mit absolutem Zielpfad (an der Konsole verifiziert, Task 2).</summary>
        Task RenameAsync(string fromPath, string toPath, CancellationToken ct);

        Task DeleteAsync(string path, CancellationToken ct);
    }
}

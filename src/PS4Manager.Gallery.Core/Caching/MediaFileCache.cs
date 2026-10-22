using System;
using System.IO;
using System.Linq;

namespace PS4Manager.Gallery.Core.Caching
{
    /// <summary>
    /// Datei-Cache für Clips: root\TitleID\Dateiname. Anders als der
    /// ThumbnailCache gibt er PFADE heraus statt Bytes — der Player öffnet eine
    /// Datei — und anders als dieser hat er eine Obergrenze: 19 s Clip sind
    /// 15 MB, eine Stunde Aufnahme wären knapp 3 GB. Unbegrenztes Wachstum ist
    /// hier ein reales Plattenproblem, kein theoretisches.
    ///
    /// Geladen wird immer in eine .part-Datei und erst nach vollständigem
    /// Transfer umbenannt: ohne das gälte eine abgebrochene halbe Datei als
    /// Treffer und der Clip bliebe dauerhaft unabspielbar. Sessiongebundene
    /// Aufrufer verwenden eindeutige Stage-Dateien, damit alter und neuer
    /// Download derselben Datei nicht kollidieren.
    /// </summary>
    public sealed class MediaFileCache
    {
        private const string PartSuffix = ".part";

        // Ab wann ein .part als tot gilt statt als laufender Transfer, siehe
        // Trim(). Der Transfer selbst bricht schon nach 30 s Stillstand ab —
        // eine Stunde ist zwei Größenordnungen Sicherheitsabstand, kein
        // Zufallswert.
        private static readonly TimeSpan StalePartAge = TimeSpan.FromHours(1);

        private readonly string _root;
        private readonly long _budgetBytes;

        public MediaFileCache(string rootDirectory, long budgetBytes)
        {
            if (string.IsNullOrEmpty(rootDirectory))
                throw new ArgumentException("Cache-Wurzel fehlt.", nameof(rootDirectory));
            if (budgetBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(budgetBytes));

            _root = rootDirectory;
            _budgetBytes = budgetBytes;
        }

        public string PathFor(string titleId, string fileName)
        {
            return Path.Combine(_root, titleId, fileName);
        }

        /// <summary>
        /// Pfad der fertigen Datei oder null. Ein Treffer wird berührt, damit
        /// der Verfall ihn als "zuletzt gebraucht" sieht: NTFS führt die
        /// Zugriffszeit standardmäßig NICHT selbst nach.
        ///
        /// Jeder Fehler hier ist ein Treffer-Fehlschlag: eine Lesesperre ist
        /// genau wie eine fehlende Datei ein Grund, das Ergebnis zu ignorieren
        /// und der Anrufer lädt die Datei erneut herunter. Eine Cache-Abfrage,
        /// die wirft, darf nicht vorkommen.
        /// </summary>
        public string TryGetPath(string titleId, string fileName)
        {
            try
            {
                var path = PathFor(titleId, fileName);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0) return null;

                try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
                catch (Exception) { /* Berühren ist Kür, kein Muss */ }

                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string BeginWrite(string titleId, string fileName)
        {
            var path = PathFor(titleId, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var part = path + PartSuffix;
            // Alte .part löschen: ein Fehler hier ist egal, der Transfer
            // überschreibt sie oder wird ignoriert. Unten liefern ist sicher.
            try { if (File.Exists(part)) File.Delete(part); }
            catch (Exception) { /* dann überschreibt der Transfer sie eben */ }

            return part;
        }

        /// <summary>
        /// Liefert eine eindeutige .part-Datei für genau einen Download. Eine
        /// alte Sitzung kann dadurch weder die Stage-Datei ihres Ersatzes
        /// überschreiben noch bei der eigenen Bereinigung löschen.
        /// </summary>
        public string BeginStagedWrite(string titleId, string fileName)
        {
            var path = PathFor(titleId, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path + "." + Guid.NewGuid().ToString("N") + PartSuffix;
        }

        public void Commit(string titleId, string fileName)
        {
            var path = Publish(titleId, fileName);
            Maintain(path);
        }

        /// <summary>
        /// Macht genau eine fertig geschriebene .part-Datei sichtbar. Die
        /// rekursive Budgetpflege ist bewusst getrennt, damit Aufrufer die
        /// kurze Sichtbarkeitsumschaltung atomar schützen können, ohne den
        /// Sessionwechsel während einer vollständigen Cache-Suche zu sperren.
        /// </summary>
        public string Publish(string titleId, string fileName)
        {
            var path = PathFor(titleId, fileName);
            return Publish(titleId, fileName, path + PartSuffix);
        }

        public string Publish(string titleId, string fileName, string stagedPath)
        {
            var path = PathFor(titleId, fileName);
            ValidateStagedPath(path, stagedPath);

            if (File.Exists(path)) File.Delete(path);
            File.Move(stagedPath, path);
            return path;
        }

        public void Discard(string stagedPath)
        {
            if (string.IsNullOrEmpty(stagedPath)) return;
            if (!stagedPath.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Stage-Datei muss auf .part enden.", nameof(stagedPath));
            try { if (File.Exists(stagedPath)) File.Delete(stagedPath); }
            catch (Exception) { /* spätere Trim-Runde räumt den Rest auf */ }
        }

        public void Maintain(string keepPath)
        {
            if (string.IsNullOrEmpty(keepPath)) throw new ArgumentException("Cache-Pfad fehlt.", nameof(keepPath));
            Trim(keepPath);
        }

        public void Remove(string titleId, string fileName)
        {
            try
            {
                var path = PathFor(titleId, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // Ein nicht löschbarer Cache-Eintrag ist keine Betriebsstörung.
                // Eine Lesesperre bedeutet, der Clip wird gerade gebraucht —
                // beim nächsten Mal vielleicht.
            }
        }

        /// <summary>
        /// Wirft die am längsten ungenutzten Dateien weg, bis das Budget passt,
        /// und räumt daneben tote .part-Reste weg. Die gerade geschriebene
        /// Datei bleibt immer — sie wird ja jetzt gebraucht, selbst wenn sie
        /// allein das Budget sprengt. .part-Dateien gehören laufenden
        /// Transfers und zählen darum nie ins Budget und sind nie
        /// Verfall-Kandidaten — ein lebender Transfer darf nie mitgezählt
        /// oder verdrängt werden.
        ///
        /// Aber ein .part kann auch tot sein: Beenden der App oder ein
        /// Refresh mitten im Clip-Download lässt ihn liegen, und BeginWrite
        /// läuft für genau diesen Clip nie wieder, wenn er später auf der
        /// Konsole verschwindet — sonst bliebe die Datei für immer liegen,
        /// unsichtbar fürs Budget. Ein laufender Transfer schreibt
        /// ununterbrochen; ist der letzte Schreibzugriff eine Stunde her, ist
        /// er tot — der Idle-Timeout des Transfers selbst liegt bei 30 s,
        /// eine Stunde ist also zwei Größenordnungen Sicherheitsabstand, kein
        /// Zufallswert.
        ///
        /// Cache-Verwaltung darf eine vollendete Übertragung nicht nachträglich
        /// fehlschlagen lassen: ein Fehler hier ist schweigend. Ebenso bei der
        /// Abfrage eines alten Eintrags — eine Lesesperre ist wie ein fehlender
        /// Clip ein Grund zu versuchen, nachzuladen.
        /// </summary>
        private void Trim(string keepPath)
        {
            try
            {
                if (!Directory.Exists(_root)) return;

                var all = new DirectoryInfo(_root).GetFiles("*", SearchOption.AllDirectories);

                var staleCutoff = DateTime.UtcNow - StalePartAge;
                foreach (var part in all.Where(f => f.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase)))
                {
                    if (part.LastWriteTimeUtc >= staleCutoff) continue;   // könnte noch laufen
                    try { part.Delete(); }
                    catch (Exception) { /* in Benutzung — beim nächsten Mal */ }
                }

                var files = all
                    .Where(f => !f.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                long total = files.Sum(f => f.Length);
                if (total <= _budgetBytes) return;

                foreach (var file in files.OrderBy(f => f.LastAccessTimeUtc))
                {
                    if (total <= _budgetBytes) break;
                    if (string.Equals(file.FullName, keepPath, StringComparison.OrdinalIgnoreCase)) continue;

                    long size = file.Length;
                    try
                    {
                        file.Delete();
                        total -= size;
                    }
                    catch (Exception) { /* in Benutzung — beim nächsten Mal */ }
                }
            }
            catch (Exception)
            {
                // Aufräumen ist Nebensache; ein Fehler hier darf den Download
                // nicht nachträglich scheitern lassen.
            }
        }

        private static void ValidateStagedPath(string targetPath, string stagedPath)
        {
            if (string.IsNullOrEmpty(stagedPath))
                throw new ArgumentException("Stage-Datei fehlt.", nameof(stagedPath));

            var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            var stagedFullPath = Path.GetFullPath(stagedPath);
            if (!string.Equals(Path.GetDirectoryName(stagedFullPath), targetDirectory,
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(stagedFullPath).StartsWith(
                    Path.GetFileName(targetPath) + ".", StringComparison.OrdinalIgnoreCase) ||
                !stagedFullPath.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Stage-Datei gehört nicht zu diesem Cache-Eintrag.", nameof(stagedPath));
        }
    }
}

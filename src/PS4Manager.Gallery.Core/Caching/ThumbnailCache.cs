using System;
using System.IO;

namespace PS4Manager.Gallery.Core.Caching
{
    /// <summary>
    /// Disk-Cache: root\TitleID\Dateiname. Bewusst dumm — kein Verfall, keine
    /// Größengrenze. Thumbnails sind ~24 KB, ein voller Cache ist billiger als
    /// jede Verwaltung (YAGNI; die Spec sieht keinen Verfall vor).
    /// </summary>
    public sealed class ThumbnailCache
    {
        private readonly string _root;

        public ThumbnailCache(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory))
                throw new ArgumentException("Cache-Wurzel fehlt.", nameof(rootDirectory));
            _root = rootDirectory;
        }

        private string PathFor(string titleId, string fileName)
        {
            return Path.Combine(_root, titleId, fileName);
        }

        public byte[] TryGet(string titleId, string fileName)
        {
            try
            {
                var path = PathFor(titleId, fileName);
                if (!File.Exists(path)) return null;
                var data = File.ReadAllBytes(path);
                return data.Length == 0 ? null : data;
            }
            catch (IOException)
            {
                return null;     // parallel beschrieben o. ä. — dann eben Fehltreffer
            }
        }

        public void Put(string titleId, string fileName, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            var staged = Stage(titleId, fileName, data);
            try { Publish(titleId, fileName, staged); }
            finally { Discard(staged); }
        }

        public string Stage(string titleId, string fileName, byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Cache-Daten fehlen.", nameof(data));
            var path = PathFor(titleId, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var staged = path + "." + Guid.NewGuid().ToString("N") + ".part";
            File.WriteAllBytes(staged, data);
            return staged;
        }

        public void Publish(string titleId, string fileName, string stagedPath)
        {
            var path = PathFor(titleId, fileName);
            ValidateStagedPath(path, stagedPath);
            if (File.Exists(path)) File.Delete(path);
            File.Move(stagedPath, path);
        }

        public void Discard(string stagedPath)
        {
            if (string.IsNullOrEmpty(stagedPath)) return;
            if (!stagedPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Stage-Datei muss auf .part enden.", nameof(stagedPath));
            try { if (File.Exists(stagedPath)) File.Delete(stagedPath); }
            catch (Exception) { }
        }

        public void Remove(string titleId, string fileName)
        {
            try
            {
                var path = PathFor(titleId, fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Ein nicht löschbarer Cache-Eintrag ist keine Betriebsstörung.
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
                !stagedFullPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Stage-Datei gehört nicht zu diesem Cache-Eintrag.", nameof(stagedPath));
        }
    }
}

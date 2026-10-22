using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Sichert eine Konsolendatei LOKAL, bevor sie geändert wird. Schlägt das
    /// fehl, fliegt die Ausnahme bis zum Aufrufer — die Änderung unterbleibt
    /// dann (Spec: keine Änderung ohne vollständige Sicherung). Die Konsole
    /// bekommt dabei nichts zu sehen.
    /// </summary>
    public sealed class GoldHenBackupWriter
    {
        private readonly string _root;
        private readonly IClock _clock;

        public GoldHenBackupWriter(string root, IClock clock)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("A backup root is required.", nameof(root));
            _root = root;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public GoldHenBackup Write(string remotePath, byte[] content, string operation)
        {
            if (string.IsNullOrEmpty(remotePath)) throw new ArgumentException("A remote path is required.", nameof(remotePath));
            if (content == null) throw new ArgumentNullException(nameof(content));

            var stamp = _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var directory = Path.Combine(_root, stamp);
            for (int suffix = 2; Directory.Exists(directory); suffix++)
                directory = Path.Combine(_root, stamp + "-" + suffix);

            Directory.CreateDirectory(directory);
            var payloadDirectory = Path.Combine(directory, "files");
            Directory.CreateDirectory(payloadDirectory);
            var payload = Path.Combine(payloadDirectory, RemotePath.Name(remotePath));
            File.WriteAllBytes(payload, content);

            var hash = Hash(content);
            var manifest = Path.Combine(directory, "manifest.json");
            File.WriteAllText(manifest, Manifest(remotePath, operation, content.Length, hash, _clock.UtcNow),
                new UTF8Encoding(false));
            return new GoldHenBackup(directory, payload, manifest, hash);
        }

        private static string Hash(byte[] content)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
        }

        private static string Manifest(string source, string operation, int size, string hash, DateTime createdUtc)
        {
            var builder = new StringBuilder();
            builder.Append("{\"version\":1");
            builder.Append(",\"operation\":\"").Append(Escape(operation)).Append('"');
            builder.Append(",\"source\":\"").Append(Escape(source)).Append('"');
            builder.Append(",\"createdUtc\":\"").Append(createdUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append('"');
            builder.Append(",\"size\":").Append(size.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"sha256\":\"").Append(hash).Append('"');
            builder.Append('}');
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}

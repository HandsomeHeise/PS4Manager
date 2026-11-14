using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Gallery.Core.Clips
{
    public static class ClipExport
    {
        public static string FindEncoder()
        {
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var directory = entry.Trim().Trim('"');
                if (!IsLocalAbsolutePath(directory)) continue;
                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(directory, "ffmpeg.exe"));
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }
            return null;
        }

        public static async Task EncodeAsync(string ffmpeg, string source, ClipRange range, string output, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (range == null) throw new ArgumentNullException(nameof(range));
            ffmpeg = LocalPath(ffmpeg);
            source = LocalPath(source);
            output = Mp4Path(output);
            if (!File.Exists(ffmpeg)) throw new FileNotFoundException("FFmpeg wurde nicht gefunden.", ffmpeg);
            RequireNewFile(output);
            string stage = StagePath(output);
            try
            {
                // Cache eviction cannot unlink the input while FFmpeg is using it.
                using (var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var process = new Process { StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    WorkingDirectory = Path.GetDirectoryName(ffmpeg),
                    Arguments = "-hide_banner -nostdin -loglevel error -xerror -n -abort_on empty_output_stream" +
                        " -protocol_whitelist file -ss " + Seconds(range.Start) + " -accurate_seek -i " + Quote(source) +
                        " -t " + Seconds(range.Duration) + " -map 0:V:0 -map 0:a:0? -map_metadata -1 -map_chapters -1" +
                        " -c:v libx264 -preset fast -crf 20 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart -f mp4 " + Quote(stage),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                } })
                {
                    token.ThrowIfCancellationRequested();
                    process.Start();
                    using (token.Register(() => Stop(process)))
                    {
                        var diagnostics = ReadDiagnosticsAsync(process.StandardError);
                        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                        var error = await diagnostics.ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (process.ExitCode != 0)
                            throw new IOException("FFmpeg konnte den Ausschnitt nicht erstellen. " + error.Trim());
                    }
                }
                if (!File.Exists(stage) || new FileInfo(stage).Length == 0)
                    throw new IOException("FFmpeg hat keine MP4-Datei erzeugt.");
                token.ThrowIfCancellationRequested();
                File.Move(stage, output);
            }
            finally { DeleteTemporaryFile(stage); }
        }

        public static async Task SaveAsync(string rendered, string destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            rendered = Mp4Path(rendered);
            destination = Mp4Path(destination);
            RequireNewFile(destination);
            string stage = StagePath(destination);
            try
            {
                using (var source = new FileStream(rendered, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                using (var target = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    if (source.Length == 0) throw new IOException("Die Vorschau ist leer.");
                    await source.CopyToAsync(target, 81920, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                File.Move(stage, destination); // Atomic publication; never replaces a file.
            }
            finally { DeleteTemporaryFile(stage); }
        }

        private static async Task<string> ReadDiagnosticsAsync(StreamReader reader)
        {
            var result = new StringBuilder();
            var buffer = new char[512];
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                result.Append(buffer, 0, Math.Min(read, 3000 - result.Length));
            return result.ToString();
        }

        private static void Stop(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }

        private static string Seconds(TimeSpan value) => (value.Ticks / (decimal)TimeSpan.TicksPerSecond).ToString("0.0000000", CultureInfo.InvariantCulture);
        private static string Quote(string value) => "\"" + Regex.Replace(value, "(\\\\*)(\"|$)", "$1$1$2") + "\"";
        private static string StagePath(string destination) => Path.Combine(Path.GetDirectoryName(destination), ".ps4-clip-" + Guid.NewGuid().ToString("N") + ".part");

        private static bool IsLocalAbsolutePath(string path) => !string.IsNullOrEmpty(path) && Regex.IsMatch(path, @"^[a-zA-Z]:[\\/]");

        private static string LocalPath(string path)
        {
            if (!IsLocalAbsolutePath(path)) throw new ArgumentException("Ein absoluter lokaler Dateipfad ist erforderlich.", nameof(path));
            return Path.GetFullPath(path);
        }

        private static string Mp4Path(string path)
        {
            path = LocalPath(path);
            if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Der Dateiname muss auf .mp4 enden.", nameof(path));
            return path;
        }

        private static void RequireNewFile(string path)
        {
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Die Zieldatei existiert bereits. Wähle einen neuen Dateinamen.");
        }

        private static void DeleteTemporaryFile(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

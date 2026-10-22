using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ps4.Core.Ftp
{
    /// <summary>
    /// Dünner Draht über FtpWebRequest (auf net48 der richtige Weg; die
    /// Deprecation gilt .NET 6+). GoldHEN: anonym, passiv, binär.
    /// Bewusst OHNE eigene Wiederholungslogik — Fehler gehen als FtpException
    /// (transient/permanent, deutscher Klartext) nach oben, die App entscheidet.
    ///
    /// Wichtig: FtpWebRequest.Timeout wirkt nur auf synchrone GetResponse()-Aufrufe;
    /// GetResponseAsync() ignoriert es. Darum: verlinkte CTS mit CancelAfter() für
    /// die Frist; request.Abort() auf dem Timeout-Token registrieren.
    /// </summary>
    public sealed class FtpClient : IFtpClient, IStrictFtpListingClient
    {
        /// <summary>Listen ist billig: 8 s. Transfers: 30 s Untätigkeit (Spec).</summary>
        private const int ListTimeoutMs = 8000;
        private const int TransferTimeoutMs = 30000;

        /// <summary>
        /// Die Kodierung, in der FtpWebRequest Pfade auf den Draht schreibt:
        /// Es fragt „OPTS utf8 on", GoldHEN antwortet 504, danach nimmt .NET
        /// Encoding.Default (auf deutschem Windows Windows-1252; Befund Task 2).
        /// GoldHEN speichert genau diese Bytes. LIST muss darum mit DERSELBEN
        /// Kodierung gelesen werden — sonst trifft ein gelisteter Name mit
        /// Umlaut beim nächsten RETR/DELE die Datei nicht mehr. Namen, die ein
        /// anderer Client als UTF-8 abgelegt hat, erscheinen so als Mojibake,
        /// bleiben aber byte-genau erreichbar.
        /// </summary>
        public static readonly Encoding PathEncoding = Encoding.Default;

        private readonly string _host;
        private readonly int _port;

        public FtpClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        internal static Uri BuildUri(string host, int port, string path)
        {
            EnsureNoControlCharacters(path, nameof(path));
            var builder = new StringBuilder("ftp://").Append(host).Append(':').Append(port);
            foreach (var segment in path.Split('/'))
            {
                if (segment.Length == 0) continue;
                builder.Append('/').Append(Uri.EscapeDataString(segment));
            }
            return new Uri(builder.ToString());
        }

        /// <summary>
        /// FtpWebRequest schneidet Kommandoparameter nur an "\r\n" ab: ein
        /// einzelnes LF, CR oder NUL geht — nach dem Uri-Unescaping in
        /// BuildUri — unverändert auf den Draht. RenameTo übernimmt den
        /// Zielpfad sogar roh, ganz ohne Uri-Escaping. Ein Name wie
        /// „x\nDELE /system/y" könnte so ein zweites FTP-Kommando
        /// einschleusen, das der Zonenschutz nie zu sehen bekam. Darum hier
        /// Schluss, bevor überhaupt etwas gesendet wird.
        /// </summary>
        private static void EnsureNoControlCharacters(string value, string paramName)
        {
            foreach (var c in value)
                if (c < ' ' || c == '\x7F')
                    throw new ArgumentException("The path cannot contain control characters.", paramName);
        }

        private FtpWebRequest Create(string path, string method, int timeoutMs)
        {
            var request = (FtpWebRequest)WebRequest.Create(BuildUri(_host, _port, path));
            request.Method = method;
            request.Credentials = new NetworkCredential("anonymous", "anonymous");
            request.UsePassive = true;
            request.UseBinary = true;
            request.KeepAlive = true;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            return request;
        }

        public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
        {
            return ListCoreAsync(path, false, ct);
        }

        public Task<IReadOnlyList<FtpEntry>> ListStrictAsync(string path, CancellationToken ct)
        {
            return ListCoreAsync(path, true, ct);
        }

        private Task<IReadOnlyList<FtpEntry>> ListCoreAsync(string path, bool strict, CancellationToken ct)
        {
            return Guard(async () =>
            {
                var request = Create(path, WebRequestMethods.Ftp.ListDirectoryDetails, ListTimeoutMs);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(ListTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(response.GetResponseStream(), PathEncoding))
                    {
                        var listing = await reader.ReadToEndAsync().ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        return strict ? UnixListingParser.ParseStrict(listing) : UnixListingParser.Parse(listing);
                    }
                }
            }, ct);
        }

        public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
        {
            return Guard(async () =>
            {
                var request = Create(path, WebRequestMethods.Ftp.DownloadFile, TransferTimeoutMs);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TransferTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                    using (var stream = response.GetResponseStream())
                    using (var buffer = new MemoryStream())
                    {
                        await stream.CopyToAsync(buffer, 81920, timeout.Token).ConfigureAwait(false);
                        return buffer.ToArray();
                    }
                }
            }, ct);
        }

        public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct)
        {
            return DownloadToFileAsync(path, localPath, 0, progress, ct);
        }

        public async Task DownloadToFileAsync(string path, string localPath, long offset,
            IProgress<long> progress, CancellationToken ct)
        {
            await DownloadToFileAsync(path, localPath, offset, progress, false, ct).ConfigureAwait(false);
        }

        public async Task DownloadToFileAsync(string path, string localPath, long offset,
            IProgress<long> progress, bool keepPartialOnFailure, CancellationToken ct)
        {
            if (offset > 0)
            {
                var existing = new FileInfo(localPath);
                if (!existing.Exists || existing.Length != offset)
                    throw new InvalidOperationException(
                        "The local partial file does not match offset " + offset + ": " + localPath);
            }

            // VOR Guard öffnen: eine gesperrte Zieldatei oder eine volle Platte
            // sind lokale Fehler, kein „Verbindungsabbruch" (Review Task 6,
            // Finding A) — genau wie UploadCoreAsync die Quelle zuerst öffnet.
            bool deleteOnFailure = false;
            try
            {
                using (var file = new FileStream(localPath, offset > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, FtpTransfer.BufferSize, true))
                using (var local = new LocalFileStream(file))
                {
                    try
                    {
                        await Guard(async () =>
                        {
                            var request = Create(path, WebRequestMethods.Ftp.DownloadFile, TransferTimeoutMs);
                            request.ContentOffset = offset;
                            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                            {
                                // Anders als DownloadAsync ist das hier KEINE Gesamtfrist: jeder
                                // gelesene Block schiebt sie neu an (siehe FtpTransfer).
                                timeout.CancelAfter(TransferTimeoutMs);
                                using (timeout.Token.Register(request.Abort))
                                using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                                using (var stream = response.GetResponseStream())
                                {
                                    await FtpTransfer.CopyAsync(stream, local, progress,
                                        () => timeout.CancelAfter(TransferTimeoutMs), timeout.Token).ConfigureAwait(false);
                                }
                            }
                        }, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Bei offset 0 ist die Datei gerade erst mit FileMode.Create
                        // angelegt worden — ganz unser eigenes Werk. Ein 0-Byte-Rest an
                        // der Stelle, die der Anwender zum Speichern gewählt hat, wäre
                        // irreführend (Gallery-Regression seit Task 6, Finding zur
                        // Schlussdurchsicht). Bei offset > 0 ist es die fortsetzbare
                        // Teildatei eines Resumes — die bleibt liegen.
                        var ftp = ex as FtpException;
                        deleteOnFailure = offset == 0 &&
                            (!keepPartialOnFailure || ftp == null || !ftp.IsTransient);
                        throw;
                    }
                }
            }
            finally
            {
                // Erst NACH dem using-Block löschen: der FileStream hält die Datei mit
                // FileShare.None exklusiv offen, solange er lebt.
                if (deleteOnFailure)
                {
                    try { File.Delete(localPath); }
                    catch (Exception) { /* best effort — der eigentliche Fehler zählt */ }
                }
            }
        }

        public Stream OpenRead(string path, long offset)
        {
            var request = Create(path, WebRequestMethods.Ftp.DownloadFile, TransferTimeoutMs);
            request.ContentOffset = offset;
            try
            {
                // Synchron: hier greift request.Timeout/ReadWriteTimeout von selbst.
                var response = request.GetResponse();
                return new ResponseStream(request, response);
            }
            catch (Exception ex)
            {
                var translated = FtpErrors.Translate(ex, CancellationToken.None);
                if (ReferenceEquals(translated, ex)) throw;
                throw translated;
            }
        }

        public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct)
        {
            return UploadCoreAsync(localPath, 0, remotePath, WebRequestMethods.Ftp.UploadFile, progress, ct);
        }

        public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath,
            IProgress<long> progress, CancellationToken ct)
        {
            return UploadCoreAsync(localPath, localOffset, remotePath, WebRequestMethods.Ftp.AppendFile, progress, ct);
        }

        private async Task UploadCoreAsync(string localPath, long localOffset, string remotePath, string method,
            IProgress<long> progress, CancellationToken ct)
        {
            // Die lokale Datei VOR dem Netz öffnen: fehlt sie oder ist sie
            // gesperrt, bleibt das ein lokaler Fehler — kein „Verbindungsabbruch".
            using (var file = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                FtpTransfer.BufferSize, true))
            using (var local = new LocalFileStream(file))
            {
                // Ein Versatz jenseits des Dateiendes würde sonst stillschweigend
                // ein leeres APPE schicken und die Datei als fertig melden, obwohl
                // sie unvollständig blieb (Review Task 6, Finding C).
                if (localOffset < 0 || localOffset > file.Length)
                    throw new InvalidOperationException(
                        "The offset " + localOffset + " is outside the local file (" +
                        file.Length + " Bytes): " + localPath);

                file.Seek(localOffset, SeekOrigin.Begin);
                await Guard(async () =>
                {
                    var request = Create(remotePath, method, TransferTimeoutMs);
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(TransferTimeoutMs);
                        using (timeout.Token.Register(request.Abort))
                        {
                            using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                            {
                                await FtpTransfer.CopyAsync(local, stream, progress,
                                    () => timeout.CancelAfter(TransferTimeoutMs), timeout.Token).ConfigureAwait(false);
                            }
                            using (await request.GetResponseAsync().ConfigureAwait(false))
                            {
                            }
                        }
                    }
                }, ct).ConfigureAwait(false);
            }
        }

        public Task<long> GetFileSizeAsync(string path, CancellationToken ct)
        {
            return Guard(async () =>
            {
                var request = Create(path, WebRequestMethods.Ftp.GetFileSize, ListTimeoutMs);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(ListTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                    {
                        return response.ContentLength;
                    }
                }
            }, ct);
        }

        public Task MakeDirectoryAsync(string path, CancellationToken ct)
        {
            return SimpleAsync(path, WebRequestMethods.Ftp.MakeDirectory, null, ct);
        }

        public Task RemoveDirectoryAsync(string path, CancellationToken ct)
        {
            return SimpleAsync(path, WebRequestMethods.Ftp.RemoveDirectory, null, ct);
        }

        public Task RenameAsync(string fromPath, string toPath, CancellationToken ct)
        {
            // RenameTo unten übernimmt toPath roh (kein Uri-Escaping wie bei
            // BuildUri) — die Steuerzeichenprüfung muss also hier explizit
            // stehen, bevor überhaupt ein RNFR den Draht erreicht.
            EnsureNoControlCharacters(toPath, nameof(toPath));
            // Absoluter Zielpfad: FtpWebRequest schickt ihn unverändert als RNTO
            // (sonst hängte es ihn an das Quellverzeichnis). An der Konsole
            // geprüft in Task 2 (Nr. 08/10).
            return SimpleAsync(fromPath, WebRequestMethods.Ftp.Rename, r => r.RenameTo = toPath, ct);
        }

        public Task DeleteAsync(string path, CancellationToken ct)
        {
            return SimpleAsync(path, WebRequestMethods.Ftp.DeleteFile, null, ct);
        }

        private Task SimpleAsync(string path, string method, Action<FtpWebRequest> configure, CancellationToken ct)
        {
            return Guard(async () =>
            {
                var request = Create(path, method, TransferTimeoutMs);
                if (configure != null) configure(request);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TransferTimeoutMs);
                    using (timeout.Token.Register(request.Abort))
                    using (await request.GetResponseAsync().ConfigureAwait(false))
                    {
                    }
                }
            }, ct);
        }

        private static async Task<T> Guard<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var translated = FtpErrors.Translate(ex, ct);
                if (ReferenceEquals(translated, ex)) throw;
                throw translated;
            }
        }

        private static Task Guard(Func<Task> operation, CancellationToken ct)
        {
            return Guard(async () =>
            {
                await operation().ConfigureAwait(false);
                return 0;
            }, ct);
        }

        /// <summary>Der Antwortstrom plus die Antwort selbst — Dispose schließt beides.</summary>
        private sealed class ResponseStream : Stream
        {
            private readonly FtpWebRequest _request;
            private readonly WebResponse _response;
            private readonly Stream _inner;
            private bool _eofReached;

            public ResponseStream(FtpWebRequest request, WebResponse response)
            {
                _request = request;
                _response = response;
                _inner = response.GetResponseStream();
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                try
                {
                    int read = _inner.Read(buffer, offset, count);
                    if (read == 0) _eofReached = true;
                    return read;
                }
                catch (Exception ex)
                {
                    var translated = FtpErrors.Translate(ex, CancellationToken.None);
                    if (ReferenceEquals(translated, ex)) throw;
                    throw translated;
                }
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (!_eofReached)
                    {
                        // Ein nicht zu Ende gelesener Transfer: request.Abort() statt
                        // den synchronen Close() warten zu lassen, bis der Server
                        // 226/426 schickt (bis zu 30 s) — sonst hängt z. B. ein früh
                        // geschlossener Explorer-Stream unnötig lange (Task 17;
                        // Review Task 6, Finding B).
                        try { _request.Abort(); } catch (Exception) { }
                    }
                    _inner.Dispose();
                    _response.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Wickelt eine lokale FileStream: Lese-/Schreibfehler (Zieldatei
        /// gesperrt, Platte voll) werden als LocalFileException erkennbar,
        /// statt als gewöhnliche IOException in der FTP-Fehlerbehandlung als
        /// Verbindungsabbruch misszudeuten (Review Task 6, Finding A).
        /// </summary>
        private sealed class LocalFileStream : Stream
        {
            private readonly FileStream _inner;

            public LocalFileStream(FileStream inner)
            {
                _inner = inner;
            }

            public override bool CanRead { get { return _inner.CanRead; } }
            public override bool CanSeek { get { return _inner.CanSeek; } }
            public override bool CanWrite { get { return _inner.CanWrite; } }
            public override long Length { get { return _inner.Length; } }
            public override long Position
            {
                get { return _inner.Position; }
                set { _inner.Position = value; }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                try { return _inner.Read(buffer, offset, count); }
                catch (IOException ex) { throw Wrap(ex); }
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                return ReadAsyncCore(buffer, offset, count, ct);
            }

            private async Task<int> ReadAsyncCore(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                try { return await _inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false); }
                catch (IOException ex) { throw Wrap(ex); }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                try { _inner.Write(buffer, offset, count); }
                catch (IOException ex) { throw Wrap(ex); }
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                return WriteAsyncCore(buffer, offset, count, ct);
            }

            private async Task WriteAsyncCore(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                try { await _inner.WriteAsync(buffer, offset, count, ct).ConfigureAwait(false); }
                catch (IOException ex) { throw Wrap(ex); }
            }

            public override void Flush()
            {
                try { _inner.Flush(); }
                catch (IOException ex) { throw Wrap(ex); }
            }

            public override long Seek(long offset, SeekOrigin origin) { return _inner.Seek(offset, origin); }
            public override void SetLength(long value) { _inner.SetLength(value); }

            private static LocalFileException Wrap(IOException ex)
            {
                return new LocalFileException("Error accessing the local file: " + ex.Message, ex);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}

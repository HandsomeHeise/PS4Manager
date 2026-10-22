using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Ps4.Core.Tests
{
    /// <summary>
    /// Minimaler FTP-Server auf 127.0.0.1 mit Dateisystem im Speicher, für die
    /// Tests des echten FtpClient (FtpWebRequest). Verhält sich, soweit an der
    /// Konsole erhoben (Spec „Verifizierte Fakten" und Befunde Task 2), wie
    /// GoldHEN v2.2: USER genügt (230), PASS ist überflüssig (202), kein
    /// UTF-8-Modus (OPTS → 504), LIST mit „.“/„..“ und Datum ohne Uhrzeit, RMD
    /// nur auf leere Ordner, RNTO überschreibt eine existierende Zieldatei.
    ///
    /// Bytetransparent wie GoldHEN: Steuerzeilen und Listings laufen über
    /// Latin-1 (jedes Byte = ein Zeichen). Ein Name ist hier also genau die
    /// Bytefolge, die FtpWebRequest geschickt hat.
    ///
    /// Jede Steuerzeile landet in den Commands — so prüfen die Tests, was
    /// FtpWebRequest wirklich auf den Draht schreibt.
    /// </summary>
    internal sealed class LoopbackFtpServer : IDisposable
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private readonly TcpListener _listener;
        private readonly object _lock = new object();
        private readonly HashSet<string> _dirs = new HashSet<string>(StringComparer.Ordinal) { "/" };
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly List<string> _commands = new List<string>();
        private volatile bool _stopped;

        public LoopbackFtpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(AcceptLoopAsync);
        }

        public string Host { get { return "127.0.0.1"; } }
        public int Port { get; }
        public int CutDataAfterBytes { get; set; }

        public void AddDirectory(string path)
        {
            lock (_lock) AddDirectoryUnlocked(Normalize(path));
        }

        public void AddFile(string path, byte[] data)
        {
            lock (_lock)
            {
                var p = Normalize(path);
                AddDirectoryUnlocked(Parent(p));
                _files[p] = data;
            }
        }

        /// <summary>Eine Datei, deren Name als rohe Bytes vorliegt — z. B. UTF-8 von einem anderen FTP-Client.</summary>
        public void AddFileWithRawName(string directory, byte[] nameBytes, byte[] data)
        {
            AddFile(Normalize(directory) + "/" + Latin1.GetString(nameBytes), data);
        }

        public byte[] GetFile(string path)
        {
            lock (_lock)
            {
                byte[] data;
                return _files.TryGetValue(Normalize(path), out data) ? data : null;
            }
        }

        public bool DirectoryExists(string path)
        {
            lock (_lock) return _dirs.Contains(Normalize(path));
        }

        public string[] CommandsSnapshot()
        {
            lock (_lock) return _commands.ToArray();
        }

        public void Dispose()
        {
            _stopped = true;
            _listener.Stop();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stopped)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) { return; }                       // Listener gestoppt
                var ignored = Task.Run(() => SessionAsync(client));
            }
        }

        private async Task SessionAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Latin1);
                var control = new StreamWriter(stream, Latin1) { NewLine = "\r\n", AutoFlush = true };
                string cwd = "/";
                string renameFrom = null;
                long restOffset = 0;
                TcpListener pasv = null;

                try
                {
                    await control.WriteLineAsync("220 Loopback FTP (GoldHEN-artig)").ConfigureAwait(false);
                    while (!_stopped)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) return;
                        lock (_lock) _commands.Add(line);

                        int space = line.IndexOf(' ');
                        var verb = (space < 0 ? line : line.Substring(0, space)).ToUpperInvariant();
                        var arg = space < 0 ? "" : line.Substring(space + 1);
                        var target = Resolve(cwd, arg);

                        switch (verb)
                        {
                            case "USER": await control.WriteLineAsync("230 User logged in, proceed.").ConfigureAwait(false); break;
                            case "PASS": await control.WriteLineAsync("202 Command not implemented, superfluous at this site.").ConfigureAwait(false); break;
                            case "OPTS": await control.WriteLineAsync("504 Command not implemented for that parameter.").ConfigureAwait(false); break;
                            case "TYPE":
                            case "NOOP": await control.WriteLineAsync("200 Command okay.").ConfigureAwait(false); break;
                            case "PWD": await control.WriteLineAsync("257 \"" + cwd + "\" is the current directory.").ConfigureAwait(false); break;
                            case "CWD":
                                if (DirectoryExists(target)) { cwd = target; await control.WriteLineAsync("250 Requested file action okay, completed.").ConfigureAwait(false); }
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            case "PASV":
                                if (pasv != null) pasv.Stop();
                                pasv = new TcpListener(IPAddress.Loopback, 0);
                                pasv.Start();
                                int p = ((IPEndPoint)pasv.LocalEndpoint).Port;
                                await control.WriteLineAsync("227 Entering Passive Mode (127,0,0,1," + (p / 256) + "," + (p % 256) + ").").ConfigureAwait(false);
                                break;
                            case "REST":
                                restOffset = long.Parse(arg, CultureInfo.InvariantCulture);
                                await control.WriteLineAsync("350 Requested file action pending further information.").ConfigureAwait(false);
                                break;
                            case "LIST":
                            {
                                var path = arg.Length == 0 ? cwd : target;
                                var listing = BuildListing(path);
                                if (listing == null) { await Fail(control).ConfigureAwait(false); break; }
                                await SendAsync(control, pasv, Latin1.GetBytes(listing), 0, false).ConfigureAwait(false);
                                pasv = null;
                                break;
                            }
                            case "RETR":
                            {
                                var data = GetFile(target);
                                if (data == null) { await Fail(control).ConfigureAwait(false); break; }
                                await SendAsync(control, pasv, data, restOffset, true).ConfigureAwait(false);
                                restOffset = 0;
                                pasv = null;
                                break;
                            }
                            case "STOR":
                            case "APPE":
                            {
                                bool parentOk;
                                lock (_lock) parentOk = _dirs.Contains(Parent(target)) && !_dirs.Contains(target);
                                if (!parentOk) { await Fail(control).ConfigureAwait(false); break; }
                                await ReceiveAsync(control, pasv, target, verb == "APPE").ConfigureAwait(false);
                                pasv = null;
                                break;
                            }
                            case "SIZE":
                            {
                                var data = GetFile(target);
                                if (data == null) await Fail(control).ConfigureAwait(false);
                                else await control.WriteLineAsync("213 " + data.Length).ConfigureAwait(false);
                                break;
                            }
                            case "MDTM":
                                if (GetFile(target) == null) await Fail(control).ConfigureAwait(false);
                                else await control.WriteLineAsync("213 20260911000000").ConfigureAwait(false);
                                break;
                            case "MKD":
                            {
                                bool ok;
                                lock (_lock)
                                {
                                    ok = _dirs.Contains(Parent(target)) && !_dirs.Contains(target) && !_files.ContainsKey(target);
                                    if (ok) _dirs.Add(target);
                                }
                                if (ok) await control.WriteLineAsync("257 \"" + target + "\" created.").ConfigureAwait(false);
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "RMD":
                            {
                                bool ok;
                                lock (_lock)
                                {
                                    ok = target != "/" && _dirs.Contains(target) && !HasChildrenUnlocked(target);
                                    if (ok) _dirs.Remove(target);
                                }
                                if (ok) await control.WriteLineAsync("250 Requested file action okay, completed.").ConfigureAwait(false);
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "DELE":
                            {
                                bool ok;
                                lock (_lock) ok = _files.Remove(target);
                                if (ok) await control.WriteLineAsync("250 Requested file action okay, completed.").ConfigureAwait(false);
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "RNFR":
                            {
                                bool exists;
                                lock (_lock) exists = _files.ContainsKey(target) || (_dirs.Contains(target) && target != "/");
                                if (exists) { renameFrom = target; await control.WriteLineAsync("350 Requested file action pending further information.").ConfigureAwait(false); }
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "RNTO":
                            {
                                bool ok = renameFrom != null && MoveUnlockedSafe(renameFrom, target);
                                renameFrom = null;
                                if (ok) await control.WriteLineAsync("250 Requested file action okay, completed.").ConfigureAwait(false);
                                else await Fail(control).ConfigureAwait(false);
                                break;
                            }
                            case "QUIT":
                                await control.WriteLineAsync("221 Service closing control connection.").ConfigureAwait(false);
                                return;
                            default:
                                await control.WriteLineAsync("502 Command not implemented.").ConfigureAwait(false);
                                break;
                        }
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
                finally
                {
                    if (pasv != null) pasv.Stop();
                }
            }
        }

        private static Task Fail(StreamWriter control)
        {
            return control.WriteLineAsync("550 Requested action not taken. File unavailable.");
        }

        private async Task SendAsync(StreamWriter control, TcpListener pasv, byte[] data, long offset, bool mayCut)
        {
            if (pasv == null) { await control.WriteLineAsync("425 Use PASV first.").ConfigureAwait(false); return; }
            await control.WriteLineAsync("150 File status okay; about to open data connection.").ConfigureAwait(false);
            bool cut = false;
            using (var dataClient = await pasv.AcceptTcpClientAsync().ConfigureAwait(false))
            {
                pasv.Stop();
                var s = dataClient.GetStream();
                int start = (int)Math.Min(offset, data.Length);
                int count = data.Length - start;
                int limit = CutDataAfterBytes;
                if (mayCut && limit > 0 && limit < count)
                {
                    await s.WriteAsync(data, start, limit).ConfigureAwait(false);
                    dataClient.Client.LingerState = new LingerOption(true, 0);  // hart kappen (RST)
                    cut = true;
                }
                else
                {
                    await s.WriteAsync(data, start, count).ConfigureAwait(false);
                }
            }
            await control.WriteLineAsync(cut ? "426 Connection closed; transfer aborted." : "226 Closing data connection.").ConfigureAwait(false);
        }

        private async Task ReceiveAsync(StreamWriter control, TcpListener pasv, string target, bool append)
        {
            if (pasv == null) { await control.WriteLineAsync("425 Use PASV first.").ConfigureAwait(false); return; }
            await control.WriteLineAsync("150 File status okay; about to open data connection.").ConfigureAwait(false);
            bool cut = false;
            byte[] received;
            using (var dataClient = await pasv.AcceptTcpClientAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                pasv.Stop();
                var s = dataClient.GetStream();
                var chunk = new byte[8192];
                int limit = CutDataAfterBytes;
                while (true)
                {
                    int read = await s.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false);
                    if (read == 0) break;
                    buffer.Write(chunk, 0, read);
                    if (limit > 0 && buffer.Length >= limit)
                    {
                        buffer.SetLength(limit);
                        dataClient.Client.LingerState = new LingerOption(true, 0);
                        cut = true;
                        break;
                    }
                }
                received = buffer.ToArray();
            }

            lock (_lock)
            {
                byte[] existing;
                _files[target] = append && _files.TryGetValue(target, out existing)
                    ? existing.Concat(received).ToArray()
                    : received;
            }
            await control.WriteLineAsync(cut ? "426 Connection closed; transfer aborted." : "226 Closing data connection.").ConfigureAwait(false);
        }

        private string BuildListing(string path)
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                if (_files.ContainsKey(path))
                {
                    sb.Append(FileLine(Name(path), _files[path].Length));
                    return sb.ToString();
                }
                if (!_dirs.Contains(path)) return null;

                sb.Append("drwxrwxrwx 1 0 0 512 Sep 11 2026 .\r\n");
                sb.Append("drwxrwxrwx 1 0 0 512 Sep 11 2026 ..\r\n");
                foreach (var d in _dirs.Where(d => d != "/" && Parent(d) == path).OrderBy(d => d, StringComparer.Ordinal))
                    sb.Append("drwxrwxrwx 1 0 0 512 Sep 11 2026 ").Append(Name(d)).Append("\r\n");
                foreach (var f in _files.Keys.Where(f => Parent(f) == path).OrderBy(f => f, StringComparer.Ordinal))
                    sb.Append(FileLine(Name(f), _files[f].Length));
                return sb.ToString();
            }
        }

        private static string FileLine(string name, long size)
        {
            return "-rwxrwxrwx 1 0 0 " + size + " Sep 11 2026 " + name + "\r\n";
        }

        private bool MoveUnlockedSafe(string from, string to)
        {
            lock (_lock)
            {
                if (!_dirs.Contains(Parent(to))) return false;
                if (_dirs.Contains(to)) return false;
                if (_files.ContainsKey(to) && !_files.ContainsKey(from)) return false;
                if (_files.ContainsKey(from))
                {
                    // GoldHEN überschreibt eine existierende Zieldatei kommentarlos (Task 2 Nr. 13).
                    _files[to] = _files[from];
                    _files.Remove(from);
                    return true;
                }
                if (!_dirs.Contains(from)) return false;
                foreach (var d in _dirs.Where(d => d == from || d.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                {
                    _dirs.Remove(d);
                    _dirs.Add(to + d.Substring(from.Length));
                }
                foreach (var f in _files.Keys.Where(f => f.StartsWith(from + "/", StringComparison.Ordinal)).ToList())
                {
                    _files[to + f.Substring(from.Length)] = _files[f];
                    _files.Remove(f);
                }
                return true;
            }
        }

        private bool HasChildrenUnlocked(string dir)
        {
            return _dirs.Any(d => d != dir && Parent(d) == dir) || _files.Keys.Any(f => Parent(f) == dir);
        }

        private void AddDirectoryUnlocked(string path)
        {
            if (path == "/") return;
            AddDirectoryUnlocked(Parent(path));
            _dirs.Add(path);
        }

        private static string Resolve(string cwd, string arg)
        {
            if (string.IsNullOrEmpty(arg)) return cwd;
            return Normalize(arg.StartsWith("/", StringComparison.Ordinal) ? arg : cwd + "/" + arg);
        }

        internal static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var segment in (path ?? "").Split('/'))
            {
                if (segment.Length == 0 || segment == ".") continue;
                if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(segment);
            }
            return "/" + string.Join("/", parts);
        }

        private static string Parent(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? "/" : path.Substring(0, slash);
        }

        private static string Name(string path)
        {
            return path.Substring(path.LastIndexOf('/') + 1);
        }
    }
}

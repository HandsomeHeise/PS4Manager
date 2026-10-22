using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Ps4.Core.Ftp;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// IStream über eine Datei auf der Konsole — Explorer liest daraus beim
    /// Kopieren. Die FTP-Verbindung wird erst beim ersten Read geöffnet und
    /// schließt sich SELBST, sobald bis zum Dateiende gelesen wurde (auch bei
    /// exakt Size gelesenen Bytes, ohne dass ein weiterer Read 0 zurückgibt)
    /// oder bei einem Fehler; von außen zusätzlich über CloseIfOpen (siehe
    /// VirtualFileDataObject.CloseOpenStreams), falls Explorer abbricht, ohne
    /// bis ans Ende zu lesen. Ein COM-Release ist aus .NET nicht beobachtbar,
    /// darauf zu warten hieße, Verbindungen bis zur Garbage Collection
    /// festzuhalten (Grund, warum die Shell kein Semaphor hat, siehe
    /// FtpGates).
    /// </summary>
    internal sealed class FtpComStream : IStream
    {
        private const int StgEAccessDenied = unchecked((int)0x80030005);
        private const int StgEInvalidFunction = unchecked((int)0x80030001);
        private const int ENotImpl = unchecked((int)0x80004001);

        private readonly IFtpClient _ftp;
        private readonly string _remotePath;
        private readonly long _size;
        private readonly string _name;
        private Stream _inner;
        private long _position;
        private bool _atEnd;

        public FtpComStream(IFtpClient ftp, string remotePath, long size, string name)
        {
            _ftp = ftp;
            _remotePath = remotePath;
            _size = size;
            _name = name;
        }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int total = ReadCore(pv, cb);
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, total);
        }

        private int ReadCore(byte[] buffer, int count)
        {
            if (_atEnd) return 0;
            int total = 0;
            try
            {
                if (_inner == null) _inner = _ftp.OpenRead(_remotePath, _position);
                while (total < count)
                {
                    int n = _inner.Read(buffer, total, count - total);
                    if (n == 0)
                    {
                        CloseInner();
                        _atEnd = true;
                        break;
                    }
                    total += n;
                }
            }
            catch (Exception)
            {
                CloseInner();
                throw;
            }
            _position += total;
            if (!_atEnd && _position >= _size)
            {
                // Exakt bis Size gelesen: kein weiterer Read mit n==0 zu erwarten, der die
                // Verbindung sonst freigäbe — hier selbst schließen (Befund Review Runde 1).
                CloseInner();
                _atEnd = true;
            }
            return total;
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long target;
            switch (dwOrigin)
            {
                case 0: target = dlibMove; break;                 // STREAM_SEEK_SET
                case 1: target = _position + dlibMove; break;     // STREAM_SEEK_CUR
                case 2: target = _size + dlibMove; break;         // STREAM_SEEK_END
            default: throw new COMException("Unknown seek origin.", StgEInvalidFunction);
            }
            if (target < 0) throw new COMException("Cannot seek before the beginning of the file.", StgEInvalidFunction);
            if (target != _position)
            {
                CloseInner();                                     // beim nächsten Read ab target neu öffnen (REST)
                _position = target;
                _atEnd = target >= _size;
            }
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, target);
        }

        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG
            {
                type = 2,                                         // STGTY_STREAM
                cbSize = _size,
                grfMode = 0,                                      // STGM_READ
                pwcsName = (grfStatFlag & 1) != 0 ? null : _name  // STATFLAG_NONAME
            };
        }

        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
        {
            // cb ist ein ULARGE_INTEGER (vorzeichenlos) — als long marshaliert kommt
            // ULLONG_MAX ("bis zum Ende kopieren") als -1 an. Vorzeichenlos behandeln,
            // sonst kopiert die Schleife gar nichts (Befund Review Runde 1).
            ulong remaining = unchecked((ulong)cb);
            var buffer = new byte[81920];
            ulong done = 0;
            while (remaining > 0)
            {
                int chunk = (int)Math.Min((ulong)buffer.Length, remaining);
                int n = ReadCore(buffer, chunk);
                if (n == 0) break;
                pstm.Write(buffer, n, IntPtr.Zero);
                done += (ulong)n;
                remaining -= (ulong)n;
            }
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt64(pcbRead, unchecked((long)done));
            if (pcbWritten != IntPtr.Zero) Marshal.WriteInt64(pcbWritten, unchecked((long)done));
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten) { throw new COMException("Read-only.", StgEAccessDenied); }
        public void SetSize(long libNewSize) { throw new COMException("Read-only.", StgEAccessDenied); }
        public void Commit(int grfCommitFlags) { }
        public void Revert() { }
        public void LockRegion(long libOffset, long cb, int dwLockType) { throw new COMException("", ENotImpl); }
        public void UnlockRegion(long libOffset, long cb, int dwLockType) { throw new COMException("", ENotImpl); }
        public void Clone(out IStream ppstm) { throw new COMException("", ENotImpl); }

        /// <summary>Schließt eine noch offene Verbindung von außen (siehe VirtualFileDataObject.CloseOpenStreams) — unabhängig vom Lesefortschritt.</summary>
        internal void CloseIfOpen()
        {
            CloseInner();
        }

        private void CloseInner()
        {
            if (_inner == null) return;
            try { _inner.Dispose(); } catch (Exception) { }
            _inner = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// Dateien der Konsole als „virtuelle Dateien" für Explorer (Spec
    /// „Virtuelle Dateien"): FileGroupDescriptorW beschreibt sie, FileContents
    /// liefert je Datei einen IStream direkt vom FTP-Server. Ordner werden erst
    /// ermittelt, wenn Explorer die Beschreibung abruft — also beim Loslassen,
    /// nicht beim Ziehbeginn.
    ///
    /// Fadenmodell: Standard-OLE-Marshaling führt Aufrufe auf dem erzeugenden STA aus.
    /// ShellPublisher stellt dafür einen eigenen Thread bereit, den FTP blockieren darf.
    /// Die Files-Eigenschaft schützt vor erneutem Eintritt während COM Nachrichten pumpt.
    /// </summary>
    public sealed class VirtualFileDataObject : StandardOleMarshalObject, IDataObject, IDataObjectAsyncCapability
    {
        private const int DvEFormatEtc = unchecked((int)0x80040064);
        private const int DvELIndex = unchecked((int)0x80040068);
        private const int OleEAdviseNotSupported = unchecked((int)0x80040003);
        private const int ENotImpl = unchecked((int)0x80004001);
        private const int DataSSameFormatEtc = 0x00040130;
        private const int DropEffectCopy = 1;
        private const int RpcECallRejected = unchecked((int)0x80010001);
        private const uint GmemMoveable = 0x0002;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        private readonly IFtpClient _ftp;
        private readonly IReadOnlyList<RemoteItem> _items;
        private readonly object _lock = new object();
        private readonly object _streamsLock = new object();
        private readonly List<FtpComStream> _openStreams = new List<FtpComStream>();
        private List<VirtualFile> _files;
        private bool _expanding;
        private bool _async = true;
        private bool _inOperation;

        public VirtualFileDataObject(IFtpClient ftp, IReadOnlyList<RemoteItem> items)
        {
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (items == null) throw new ArgumentNullException(nameof(items));
            _ftp = ftp;
            _items = items.Where(i => !i.IsLink).ToList();
        }

        /// <summary>HRESULT von Explorer, wenn ein asynchrones Kopieren endet (0 = Erfolg).</summary>
        public event EventHandler<int> OperationEnded;

        private IReadOnlyList<VirtualFile> Files
        {
            get
            {
                lock (_lock)
                {
                    if (_files != null) return _files;
                    // Reentrancy: ein blockierender COM-Aufruf pumpt auf einem STA-Thread
                    // eingehende Nachrichten — Explorer kann auf demselben Thread erneut
                    // hierher zurückrufen, während Expand() noch läuft. lock ist für
                    // denselben Thread rekursiv (kein Deadlock), darum die eigene Wache.
                    if (_expanding)
                        throw new COMException("Folder enumeration is already in progress — please try again.", RpcECallRejected);
                    _expanding = true;
                    try
                    {
                        var expanded = Expand();
                        _files = expanded;
                        return expanded;
                    }
                    finally
                    {
                        _expanding = false;
                    }
                }
            }
        }

        private List<VirtualFile> Expand()
        {
            var result = new List<VirtualFile>();
            foreach (var item in _items)
            {
                var name = LocalName.Sanitize(item.Name);
                result.Add(new VirtualFile(name, item.IsDirectory, item.Size, item.Modified, item.Path));
                if (item.IsDirectory) ExpandDirectory(item.Path, name, result);
            }
            return result;
        }

        private void ExpandDirectory(string remoteDir, string relative, List<VirtualFile> result)
        {
            // Läuft auf dem Thread, der dieses Objekt veröffentlicht hat (STA-Marshaling,
            // NICHT auf einem Hintergrund-Thread von Explorer — siehe Klassenkommentar) —
            // synchron blockiert also genau diesen Thread.
            var entries = _ftp.ListAsync(remoteDir, CancellationToken.None).GetAwaiter().GetResult();
            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (entry.Kind == FtpEntryKind.Link) continue;           // keine Schleifen
                var path = RemotePath.Combine(remoteDir, entry.Name);
                var name = relative + "\\" + LocalName.Sanitize(entry.Name);
                result.Add(new VirtualFile(name, entry.IsDirectory, entry.Size, entry.Modified, path));
                if (entry.IsDirectory) ExpandDirectory(path, name, result);
            }
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            medium = new STGMEDIUM();

            if (format.cfFormat == ShellFormats.FileGroupDescriptorW && (format.tymed & TYMED.TYMED_HGLOBAL) != 0)
            {
                medium.tymed = TYMED.TYMED_HGLOBAL;
                medium.unionmember = ToHGlobal(FileDescriptorWriter.Write(Files));
                return;
            }

            if (format.cfFormat == ShellFormats.FileContents && (format.tymed & TYMED.TYMED_ISTREAM) != 0)
            {
                var files = Files;
                if (format.lindex < 0 || format.lindex >= files.Count || files[format.lindex].IsDirectory)
                    throw new COMException("No file content exists at this index.", DvELIndex);
                var file = files[format.lindex];
                var stream = new FtpComStream(_ftp, file.RemotePath, file.Size, Path.GetFileName(file.RelativeName));
                lock (_streamsLock) _openStreams.Add(stream);
                medium.tymed = TYMED.TYMED_ISTREAM;
                medium.unionmember = Marshal.GetComInterfaceForObject(stream, typeof(IStream));
                return;
            }

            if (format.cfFormat == ShellFormats.PreferredDropEffect && (format.tymed & TYMED.TYMED_HGLOBAL) != 0)
            {
                medium.tymed = TYMED.TYMED_HGLOBAL;
                medium.unionmember = ToHGlobal(BitConverter.GetBytes(DropEffectCopy));
                return;
            }

            throw new COMException("Format is not available.", DvEFormatEtc);
        }

        public int QueryGetData(ref FORMATETC format)
        {
            if (format.cfFormat == ShellFormats.FileGroupDescriptorW && (format.tymed & TYMED.TYMED_HGLOBAL) != 0) return 0;
            if (format.cfFormat == ShellFormats.FileContents && (format.tymed & TYMED.TYMED_ISTREAM) != 0) return 0;
            if (format.cfFormat == ShellFormats.PreferredDropEffect && (format.tymed & TYMED.TYMED_HGLOBAL) != 0) return 0;
            return DvEFormatEtc;
        }

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            if (direction != DATADIR.DATADIR_GET) throw new COMException("", ENotImpl);
            return new FormatEnumerator(new[]
            {
                Describe(ShellFormats.FileGroupDescriptorW, TYMED.TYMED_HGLOBAL),
                Describe(ShellFormats.FileContents, TYMED.TYMED_ISTREAM),
                Describe(ShellFormats.PreferredDropEffect, TYMED.TYMED_HGLOBAL)
            });
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
        {
            throw new COMException("", ENotImpl);
        }

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            formatOut.ptd = IntPtr.Zero;
            return DataSSameFormatEtc;
        }

        /// <summary>Explorer meldet „Performed DropEffect"/„Paste Succeeded" — angenommen, freigegeben, ignoriert.</summary>
        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
        {
            if (release) ShellInterop.ReleaseStgMedium(ref medium);
        }

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            connection = 0;
            return OleEAdviseNotSupported;
        }

        public void DUnadvise(int connection)
        {
            throw new COMException("", OleEAdviseNotSupported);
        }

        public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
        {
            enumAdvise = null;
            return OleEAdviseNotSupported;
        }

        public void SetAsyncMode(bool fDoOpAsync) { _async = fDoOpAsync; }
        public void GetAsyncMode(out bool pfIsOpAsync) { pfIsOpAsync = _async; }
        public void StartOperation(IntPtr pbcReserved) { _inOperation = true; }
        public void InOperation(out bool pfInAsyncOp) { pfInAsyncOp = _inOperation; }

        public void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects)
        {
            _inOperation = false;
            CloseOpenStreams();
            var handler = OperationEnded;
            if (handler != null) handler(this, hResult);
        }

        /// <summary>
        /// Schließt die FTP-Verbindung jedes bisher ausgegebenen FileContents-Streams,
        /// der Explorer nicht bis ans Ende gelesen hat — ohne auf dessen COM-Freigabe zu
        /// warten (sonst bliebe die Verbindung bis zur Garbage Collection belegt, siehe
        /// FtpComStream). Im Async-Modus ruft EndOperation dies bereits selbst auf. Für den
        /// synchronen Fall (kein Async-Handshake) ruft die App dies auf, sobald
        /// ShellInterop.StartDrag zurückkehrt, und wenn der Zwischenablage-Inhalt durch
        /// einen neuen ersetzt wird (ShellPublisher erledigt beides).
        /// </summary>
        public void CloseOpenStreams()
        {
            FtpComStream[] streams;
            lock (_streamsLock) streams = _openStreams.ToArray();
            foreach (var stream in streams) stream.CloseIfOpen();
        }

        private static FORMATETC Describe(short cf, TYMED tymed)
        {
            return new FORMATETC { cfFormat = cf, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = tymed, ptd = IntPtr.Zero };
        }

        /// <summary>
        /// GMEM_MOVEABLE statt Marshal.AllocHGlobal: STGMEDIUM.unionmember für
        /// TYMED_HGLOBAL muss ein echtes HGLOBAL sein (der Empfänger — Explorer oder
        /// ShellInterop.ReleaseStgMedium — ruft GlobalLock/GlobalFree darauf auf, nicht
        /// LocalFree, das hinter Marshal.FreeHGlobal steckt).
        /// </summary>
        private static IntPtr ToHGlobal(byte[] data)
        {
            var handle = GlobalAlloc(GmemMoveable, (UIntPtr)data.Length);
            if (handle == IntPtr.Zero) throw new OutOfMemoryException();
            var ptr = GlobalLock(handle);
            try
            {
                Marshal.Copy(data, 0, ptr, data.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }
            return handle;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Shell;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    public class VirtualFileDataObjectTests
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        private sealed class Descriptor
        {
            public uint Flags;
            public uint Attributes;
            public long Size;
            public long WriteTime;
            public string Name;
        }

        /// <summary>Testdouble: verpackt jeden von OpenRead gelieferten Stream, um sein Dispose zu protokollieren.</summary>
        private sealed class DisposeTrackingFtpClient : IFtpClient
        {
            private readonly IFtpClient _inner;
            public readonly List<DisposeTrackingStream> Streams = new List<DisposeTrackingStream>();

            public DisposeTrackingFtpClient(IFtpClient inner) { _inner = inner; }

            public Stream OpenRead(string path, long offset)
            {
                var stream = new DisposeTrackingStream(_inner.OpenRead(path, offset));
                Streams.Add(stream);
                return stream;
            }

            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct) { return _inner.ListAsync(path, ct); }
            public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { return _inner.DownloadAsync(path, ct); }
            public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { return _inner.DownloadToFileAsync(path, localPath, progress, ct); }
            public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { return _inner.DownloadToFileAsync(path, localPath, offset, progress, ct); }
            public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct) { return _inner.UploadFromFileAsync(localPath, remotePath, progress, ct); }
            public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct) { return _inner.AppendFromFileAsync(localPath, localOffset, remotePath, progress, ct); }
            public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { return _inner.GetFileSizeAsync(path, ct); }
            public Task MakeDirectoryAsync(string path, CancellationToken ct) { return _inner.MakeDirectoryAsync(path, ct); }
            public Task RemoveDirectoryAsync(string path, CancellationToken ct) { return _inner.RemoveDirectoryAsync(path, ct); }
            public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { return _inner.RenameAsync(fromPath, toPath, ct); }
            public Task DeleteAsync(string path, CancellationToken ct) { return _inner.DeleteAsync(path, ct); }
        }

        private sealed class DisposeTrackingStream : Stream
        {
            private readonly Stream _inner;
            public bool Disposed { get; private set; }

            public DisposeTrackingStream(Stream inner) { _inner = inner; }

            public override bool CanRead { get { return _inner.CanRead; } }
            public override bool CanSeek { get { return _inner.CanSeek; } }
            public override bool CanWrite { get { return _inner.CanWrite; } }
            public override long Length { get { return _inner.Length; } }
            public override long Position { get { return _inner.Position; } set { _inner.Position = value; } }
            public override void Flush() { _inner.Flush(); }
            public override int Read(byte[] buffer, int offset, int count) { return _inner.Read(buffer, offset, count); }
            public override long Seek(long offset, SeekOrigin origin) { return _inner.Seek(offset, origin); }
            public override void SetLength(long value) { _inner.SetLength(value); }
            public override void Write(byte[] buffer, int offset, int count) { _inner.Write(buffer, offset, count); }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>Einfacher, verwalteter IStream — Ziel für CopyTo, wie es ein echter Explorer-Stream wäre.</summary>
        private sealed class MemoryComStream : IStream
        {
            private readonly MemoryStream _inner = new MemoryStream();

            public byte[] ToArray() { return _inner.ToArray(); }

            public void Write(byte[] pv, int cb, IntPtr pcbWritten)
            {
                _inner.Write(pv, 0, cb);
                if (pcbWritten != IntPtr.Zero) Marshal.WriteInt32(pcbWritten, cb);
            }

            public void Read(byte[] pv, int cb, IntPtr pcbRead) { throw new NotImplementedException(); }
            public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition) { throw new NotImplementedException(); }
            public void SetSize(long libNewSize) { throw new NotImplementedException(); }
            public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) { throw new NotImplementedException(); }
            public void Commit(int grfCommitFlags) { }
            public void Revert() { throw new NotImplementedException(); }
            public void LockRegion(long libOffset, long cb, int dwLockType) { throw new NotImplementedException(); }
            public void UnlockRegion(long libOffset, long cb, int dwLockType) { throw new NotImplementedException(); }
            public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag) { throw new NotImplementedException(); }
            public void Clone(out IStream ppstm) { throw new NotImplementedException(); }
        }

        private static FORMATETC Format(short cf, TYMED tymed, int lindex = -1)
        {
            return new FORMATETC { cfFormat = cf, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = lindex, tymed = tymed, ptd = IntPtr.Zero };
        }

        /// <summary>Holt FileGroupDescriptorW, wie Explorer es täte, und zerlegt die Bytes. Liest/befreit das HGLOBAL korrekt per GlobalLock/ReleaseStgMedium.</summary>
        private static List<Descriptor> ReadDescriptors(IDataObject data)
        {
            var format = Format(ShellFormats.FileGroupDescriptorW, TYMED.TYMED_HGLOBAL);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            try
            {
                Assert.Equal(TYMED.TYMED_HGLOBAL, medium.tymed);
                var ptr = GlobalLock(medium.unionmember);
                try
                {
                    int count = Marshal.ReadInt32(ptr);
                    var bytes = new byte[4 + count * FileDescriptorWriter.DescriptorSize];
                    Marshal.Copy(ptr, bytes, 0, bytes.Length);
                    var result = new List<Descriptor>();
                    for (int i = 0; i < count; i++)
                    {
                        int o = 4 + i * FileDescriptorWriter.DescriptorSize;
                        var nameBytes = new byte[520];
                        Array.Copy(bytes, o + 72, nameBytes, 0, 520);
                        var name = Encoding.Unicode.GetString(nameBytes);
                        result.Add(new Descriptor
                        {
                            Flags = BitConverter.ToUInt32(bytes, o),
                            Attributes = BitConverter.ToUInt32(bytes, o + 36),
                            WriteTime = BitConverter.ToUInt32(bytes, o + 56) | ((long)BitConverter.ToUInt32(bytes, o + 60) << 32),
                            Size = ((long)BitConverter.ToUInt32(bytes, o + 64) << 32) | BitConverter.ToUInt32(bytes, o + 68),
                            Name = name.Substring(0, name.IndexOf('\0'))
                        });
                    }
                    return result;
                }
                finally
                {
                    GlobalUnlock(medium.unionmember);
                }
            }
            finally
            {
                ShellInterop.ReleaseStgMedium(ref medium);
            }
        }

        private static byte[] ReadContents(IDataObject data, int index)
        {
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, index);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            Assert.Equal(TYMED.TYMED_ISTREAM, medium.tymed);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            Marshal.Release(medium.unionmember);
            return ReadAll(stream);
        }

        private static byte[] ReadAll(IStream stream)
        {
            var result = new List<byte>();
            var buffer = new byte[70000];
            var readPtr = Marshal.AllocCoTaskMem(4);
            try
            {
                while (true)
                {
                    stream.Read(buffer, buffer.Length, readPtr);
                    int n = Marshal.ReadInt32(readPtr);
                    if (n == 0) break;
                    result.AddRange(buffer.Take(n));
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(readPtr);
            }
            return result.ToArray();
        }

        private static byte[] Pattern(int length)
        {
            return Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        }

        private static InMemoryFtpClient Ps4Console()
        {
            return new InMemoryFtpClient()
                .AddFile("/data/a.txt", "abc")
                .AddFile("/data/mods/x.prx", Pattern(150000))
                .AddFile("/data/mods/sub/y.ini", "y");
        }

        private static RemoteItem File(string path, long size) { return new RemoteItem(path, false, size, new DateTime(2026, 9, 11)); }
        private static RemoteItem Dir(string path) { return new RemoteItem(path, true, 0, new DateTime(2026, 9, 11)); }

        [Fact]
        public void The_descriptor_lists_files_and_folders_with_relative_names()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3), Dir("/data/mods") });

            var d = ReadDescriptors(data);

            // Ordinal sortiert: "sub" < "x.prx".
            Assert.Equal(new[] { "a.txt", "mods", "mods\\sub", "mods\\sub\\y.ini", "mods\\x.prx" }, d.Select(x => x.Name).ToArray());
            Assert.Equal(0x10u, d[1].Attributes & 0x10u);                // Ordner
            Assert.Equal(0u, d[0].Attributes & 0x10u);
            Assert.Equal(150000, d[4].Size);
            Assert.NotEqual(0u, d[0].Flags & 0x4000u);                  // FD_PROGRESSUI
            Assert.NotEqual(0u, d[0].Flags & 0x40u);                    // FD_FILESIZE
            Assert.Equal(new DateTime(2026, 9, 11).ToFileTime(), d[0].WriteTime);
        }

        [Fact]
        public void Sizes_above_4_gb_are_split_into_high_and_low()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/pkg/riesig.pkg", 18143969280L) });
            Assert.Equal(18143969280L, ReadDescriptors(data).Single().Size);
        }

        [Fact]
        public void Folders_are_expanded_only_when_explorer_asks()
        {
            var ftp = Ps4Console();
            var data = new VirtualFileDataObject(ftp, new[] { Dir("/data/mods") });
            Assert.DoesNotContain(ftp.CallsSnapshot(), c => c.StartsWith("LIST"));

            ReadDescriptors(data);

            Assert.Contains("LIST /data/mods", ftp.CallsSnapshot());
        }

        [Fact]
        public void File_contents_stream_straight_from_the_console()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3), Dir("/data/mods") });
            ReadDescriptors(data);

            Assert.Equal(Pattern(150000), ReadContents(data, 4));
            Assert.Equal("y", Encoding.UTF8.GetString(ReadContents(data, 3)));
            Assert.Equal("abc", Encoding.UTF8.GetString(ReadContents(data, 0)));
        }

        [Fact]
        public void The_stream_reports_its_size_and_can_start_over()
        {
            var ftp = Ps4Console();
            var data = new VirtualFileDataObject(ftp, new[] { File("/data/mods/x.prx", 150000) });
            ReadDescriptors(data);
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, 0);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            Marshal.Release(medium.unionmember);

            System.Runtime.InteropServices.ComTypes.STATSTG stat;
            stream.Stat(out stat, 1);
            Assert.Equal(150000, stat.cbSize);

            var first = ReadAll(stream);
            stream.Seek(0, 0, IntPtr.Zero);
            var second = ReadAll(stream);

            Assert.Equal(first, second);
            Assert.Equal(2, ftp.CallsSnapshot().Count(c => c == "RETR /data/mods/x.prx@0"));
        }

        [Fact]
        public void Contents_of_a_folder_entry_are_refused()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { Dir("/data/mods") });
            ReadDescriptors(data);
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, 0);
            STGMEDIUM medium;
            var ex = Assert.Throws<COMException>(() => data.GetData(ref format, out medium));
            Assert.Equal(unchecked((int)0x80040068), ex.ErrorCode);     // DV_E_LINDEX
        }

        [Fact]
        public void Only_the_three_formats_are_offered()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3) });

            var text = Format(1, TYMED.TYMED_HGLOBAL);                   // CF_TEXT
            Assert.Equal(unchecked((int)0x80040064), data.QueryGetData(ref text));   // DV_E_FORMATETC
            var descriptor = Format(ShellFormats.FileGroupDescriptorW, TYMED.TYMED_HGLOBAL);
            Assert.Equal(0, data.QueryGetData(ref descriptor));

            var formats = new FORMATETC[5];
            var fetched = new int[1];
            data.EnumFormatEtc(DATADIR.DATADIR_GET).Next(5, formats, fetched);
            Assert.Equal(3, fetched[0]);
            Assert.Equal(new[] { ShellFormats.FileGroupDescriptorW, ShellFormats.FileContents, ShellFormats.PreferredDropEffect },
                formats.Take(3).Select(f => f.cfFormat).ToArray());
        }

        [Fact]
        public void The_preferred_drop_effect_is_copy()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3) });
            var format = Format(ShellFormats.PreferredDropEffect, TYMED.TYMED_HGLOBAL);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            try
            {
                var ptr = GlobalLock(medium.unionmember);
                try
                {
                    Assert.Equal(1, Marshal.ReadInt32(ptr));  // DROPEFFECT_COPY
                }
                finally
                {
                    GlobalUnlock(medium.unionmember);
                }
            }
            finally
            {
                ShellInterop.ReleaseStgMedium(ref medium);
            }
        }

        [Fact]
        public void Async_mode_is_offered_and_the_end_is_reported()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/a.txt", 3) });
            IDataObjectAsyncCapability async = data;
            int? ended = null;
            data.OperationEnded += (s, hr) => ended = hr;

            bool mode;
            async.GetAsyncMode(out mode);
            Assert.True(mode);
            async.StartOperation(IntPtr.Zero);
            bool inOperation;
            async.InOperation(out inOperation);
            Assert.True(inOperation);
            async.EndOperation(0, IntPtr.Zero, 1);

            Assert.Equal(0, ended);
        }

        [Fact]
        public void Names_windows_cannot_store_are_made_safe()
        {
            var ftp = new InMemoryFtpClient().AddFile("/data/a:b?.txt", "x");
            var data = new VirtualFileDataObject(ftp, new[] { File("/data/a:b?.txt", 1) });
            Assert.Equal("a_b_.txt", ReadDescriptors(data).Single().Name);
            Assert.Equal("x", Encoding.UTF8.GetString(ReadContents(data, 0)));
        }

        [Fact]
        public void A_stream_read_to_its_exact_size_releases_its_connection()
        {
            var tracking = new DisposeTrackingFtpClient(Ps4Console());
            var data = new VirtualFileDataObject(tracking, new[] { File("/data/a.txt", 3) });
            ReadDescriptors(data);
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, 0);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            Marshal.Release(medium.unionmember);

            // Genau Size Bytes in einem Rutsch — kein weiterer Read mit n==0, der die
            // Verbindung über den alten Pfad freigäbe (Sinn dieses Tests: nur der neue
            // "exakt bis Size gelesen"-Zweig in ReadCore darf hier schließen).
            var readPtr = Marshal.AllocCoTaskMem(4);
            try
            {
                var buffer = new byte[3];
                stream.Read(buffer, 3, readPtr);
                Assert.Equal(3, Marshal.ReadInt32(readPtr));
                Assert.Equal("abc", Encoding.UTF8.GetString(buffer));
            }
            finally
            {
                Marshal.FreeCoTaskMem(readPtr);
            }

            Assert.Single(tracking.Streams);
            Assert.True(tracking.Streams[0].Disposed);
        }

        [Fact]
        public void EndOperation_closes_streams_explorer_abandoned()
        {
            var tracking = new DisposeTrackingFtpClient(Ps4Console());
            var data = new VirtualFileDataObject(tracking, new[] { File("/data/mods/x.prx", 150000) });
            ReadDescriptors(data);
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, 0);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            Marshal.Release(medium.unionmember);

            var readPtr = Marshal.AllocCoTaskMem(4);
            try
            {
                var buffer = new byte[100];
                stream.Read(buffer, buffer.Length, readPtr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(readPtr);
            }
            Assert.False(tracking.Streams[0].Disposed);   // noch weit vor Size — Explorer bricht hier ab

            IDataObjectAsyncCapability async = data;
            async.EndOperation(0, IntPtr.Zero, 1);

            Assert.True(tracking.Streams[0].Disposed);
        }

        [Fact]
        public void CopyTo_treats_cb_as_unsigned_and_copies_the_whole_file()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/mods/x.prx", 150000) });
            ReadDescriptors(data);
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, 0);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            Marshal.Release(medium.unionmember);

            var target = new MemoryComStream();
            stream.CopyTo(target, -1, IntPtr.Zero, IntPtr.Zero);   // ULLONG_MAX, vorzeichenbehaftet als -1 marshaliert

            Assert.Equal(Pattern(150000), target.ToArray());
        }

        [Fact]
        public void Seeking_before_the_start_is_refused()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { File("/data/mods/x.prx", 150000) });
            ReadDescriptors(data);
            var format = Format(ShellFormats.FileContents, TYMED.TYMED_ISTREAM, 0);
            STGMEDIUM medium;
            data.GetData(ref format, out medium);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            Marshal.Release(medium.unionmember);

            var ex = Assert.Throws<COMException>(() => stream.Seek(-1, 0, IntPtr.Zero));   // STREAM_SEEK_SET
            Assert.Equal(unchecked((int)0x80030001), ex.ErrorCode);     // STG_E_INVALIDFUNCTION
        }
        [Fact]
        public void A_date_before_1602_is_left_out()
        {
            var data = new VirtualFileDataObject(Ps4Console(), new[] { new RemoteItem("/data/a.txt", false, 3, new DateTime(1601, 1, 1)) });

            var d = ReadDescriptors(data).Single();

            Assert.Equal(0u, d.Flags & 0x20u);                          // kein FD_WRITESTIME
            Assert.Equal(0, d.WriteTime);
        }

        [Fact]
        public void Links_are_not_offered_to_explorer()
        {
            var link = new RemoteItem("/data/verweis", FtpEntryKind.Link, 7, new DateTime(2026, 9, 11), "/system");
            var data = new VirtualFileDataObject(Ps4Console(), new[] { link, File("/data/a.txt", 3) });

            Assert.Equal(new[] { "a.txt" }, ReadDescriptors(data).Select(x => x.Name).ToArray());
        }

        [Fact]
        public void The_data_object_uses_standard_ole_marshaling()
        {
            Assert.IsAssignableFrom<StandardOleMarshalObject>(new VirtualFileDataObject(Ps4Console(), new RemoteItem[0]));
        }
       }
}

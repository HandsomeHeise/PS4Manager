using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Shell;
using FileManager.Core.Tests.Support;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;
using Xunit;

namespace FileManager.Core.Tests
{
    /// <summary>
    /// Headless: kein Ziehen (braucht die Maus), keine Zwischenablage (gehört
    /// dem Anwender). Geprüft wird das Fadenmodell — Aufrufe über den Proxy
    /// landen auf dem Publisher-Thread.
    /// </summary>
    public sealed class ShellPublisherTests : IDisposable
    {
        private readonly ShellPublisher _publisher = new ShellPublisher();

        public void Dispose()
        {
            _publisher.Dispose();
        }

        /// <summary>Protokolliert, auf welchem Thread gelistet wird.</summary>
        private sealed class ThreadRecordingFtpClient : IFtpClient
        {
            private readonly IFtpClient _inner;
            public readonly List<int> ListThreads = new List<int>();

            public ThreadRecordingFtpClient(IFtpClient inner) { _inner = inner; }

            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                lock (ListThreads) ListThreads.Add(Thread.CurrentThread.ManagedThreadId);
                return _inner.ListAsync(path, ct);
            }

            public Task<byte[]> DownloadAsync(string path, CancellationToken ct) { return _inner.DownloadAsync(path, ct); }
            public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { return _inner.DownloadToFileAsync(path, localPath, progress, ct); }
            public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { return _inner.DownloadToFileAsync(path, localPath, offset, progress, ct); }
            public Stream OpenRead(string path, long offset) { return _inner.OpenRead(path, offset); }
            public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct) { return _inner.UploadFromFileAsync(localPath, remotePath, progress, ct); }
            public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct) { return _inner.AppendFromFileAsync(localPath, localOffset, remotePath, progress, ct); }
            public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { return _inner.GetFileSizeAsync(path, ct); }
            public Task MakeDirectoryAsync(string path, CancellationToken ct) { return _inner.MakeDirectoryAsync(path, ct); }
            public Task RemoveDirectoryAsync(string path, CancellationToken ct) { return _inner.RemoveDirectoryAsync(path, ct); }
            public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { return _inner.RenameAsync(fromPath, toPath, ct); }
            public Task DeleteAsync(string path, CancellationToken ct) { return _inner.DeleteAsync(path, ct); }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDataFunction(IntPtr self, ref FORMATETC format, out STGMEDIUM medium);

        /// <summary>IDataObject::GetData über die VTable des rohen Proxys — ohne RCW, die ihn auspacken würde.</summary>
        private static int CallGetData(IntPtr dataObject, ref FORMATETC format, out STGMEDIUM medium)
        {
            var vtable = Marshal.ReadIntPtr(dataObject);
            var slot = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);          // nach QueryInterface, AddRef, Release
            var getData = (GetDataFunction)Marshal.GetDelegateForFunctionPointer(slot, typeof(GetDataFunction));
            return getData(dataObject, ref format, out medium);
        }

        [Fact]
        public void Work_runs_on_the_publishers_sta_thread()
        {
            var info = _publisher.Invoke(() => Tuple.Create(Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.GetApartmentState()));

            Assert.Equal(_publisher.ManagedThreadId, info.Item1);
            Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, info.Item1);
            Assert.Equal(ApartmentState.STA, info.Item2);
        }

        [Fact]
        public async Task Async_work_runs_there_too()
        {
            var id = await _publisher.InvokeAsync(() => Thread.CurrentThread.ManagedThreadId);

            Assert.Equal(_publisher.ManagedThreadId, id);
        }

        [Fact]
        public void An_exception_reaches_the_caller()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => _publisher.Invoke<int>(() => { throw new InvalidOperationException("kaputt"); }));

            Assert.Equal("kaputt", ex.Message);
        }

        [Fact]
        public void After_dispose_the_thread_ends_and_no_work_is_taken()
        {
            var publisher = new ShellPublisher();
            publisher.Dispose();

            Assert.False(publisher.IsAliveForTests);
            Assert.Throws<ObjectDisposedException>(() => publisher.Invoke(() => 1));
        }

        [Fact]
        public void A_drag_proxy_calls_back_on_the_publisher_thread()
        {
            var ftp = new ThreadRecordingFtpClient(new InMemoryFtpClient().AddFile("/data/mods/a.txt", "a"));
            var data = _publisher.Create(ftp, new[] { new RemoteItem("/data/mods", true, 0, DateTime.MinValue) });
            int hr = -1;
            int callerThread = 0;
            Exception error = null;

            var caller = new Thread(() =>
            {
                try
                {
                    callerThread = Thread.CurrentThread.ManagedThreadId;
                    var proxy = _publisher.MarshalToCaller(data);
                    try
                    {
                        var format = new FORMATETC
                        {
                            cfFormat = ShellFormats.FileGroupDescriptorW,
                            dwAspect = DVASPECT.DVASPECT_CONTENT,
                            lindex = -1,
                            tymed = TYMED.TYMED_HGLOBAL,
                            ptd = IntPtr.Zero
                        };
                        STGMEDIUM medium;
                        hr = CallGetData(proxy, ref format, out medium);
                        if (hr >= 0) ShellInterop.ReleaseStgMedium(ref medium);
                    }
                    finally
                    {
                        Marshal.Release(proxy);
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            caller.SetApartmentState(ApartmentState.STA);
            caller.Start();
            caller.Join();

            Assert.Null(error);
            Assert.Equal(0, hr);
            Assert.NotEqual(callerThread, _publisher.ManagedThreadId);
            Assert.Equal(new[] { _publisher.ManagedThreadId }, ftp.ListThreads.Distinct().ToArray());
        }
    }
}

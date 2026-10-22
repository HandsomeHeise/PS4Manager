using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// Eigener STA-Thread mit Nachrichtenschleife, der die virtuellen Dateien
    /// veröffentlicht (Plan-1-Übergabe): Explorers Rückrufe — die rekursive
    /// Ordnerermittlung in GetData und jedes IStream::Read — laufen auf dem
    /// Thread, dem das Datenobjekt gehört. Das ist dieser, nicht der UI-Thread;
    /// FTP darf ihn blockieren.
    ///
    /// Die Zwischenablage setzt dieser Thread selbst. Fürs Ziehen bekommt der
    /// UI-Thread (der die Maus hält) einen rohen Proxy und ruft DoDragDrop damit;
    /// COM leitet Explorers Aufrufe am UI-Thread vorbei hierher.
    /// </summary>
    public sealed class ShellPublisher : IDisposable
    {
        private const uint WmApp = 0x8000;
        private const uint WmQuit = 0x0012;
        private const uint PmNoRemove = 0x0000;
        private static readonly Guid IidIDataObject = new Guid("0000010e-0000-0000-C000-000000000046");

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public IntPtr Hwnd;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref Msg msg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Msg msg);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint elapseMs, IntPtr timerProc);

        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr reserved);

        [DllImport("ole32.dll")]
        private static extern void OleUninitialize();

        [DllImport("ole32.dll")]
        private static extern int OleSetClipboard(IDataObject data);

        [DllImport("ole32.dll")]
        private static extern int OleIsCurrentClipboard(IDataObject data);

        [DllImport("ole32.dll")]
        private static extern int CoMarshalInterThreadInterfaceInStream([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] object unknown, out IntPtr stream);

        [DllImport("ole32.dll")]
        private static extern int CoGetInterfaceAndReleaseStream(IntPtr stream, [In] ref Guid riid, out IntPtr result);

        private readonly Thread _thread;
        private readonly ConcurrentQueue<Action> _work = new ConcurrentQueue<Action>();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private uint _nativeThreadId;
        private Exception _startError;
        private volatile bool _disposed;
        private VirtualFileDataObject _clipboard;          // nur auf dem Publisher-Thread benutzt

        public ShellPublisher()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Shell-Publisher" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();
            if (_startError != null) throw new InvalidOperationException("The shell thread could not be started.", _startError);
        }

        public int ManagedThreadId { get { return _thread.ManagedThreadId; } }

        internal bool IsAliveForTests { get { return _thread.IsAlive; } }

        private void Run()
        {
            int hr = OleInitialize(IntPtr.Zero);
            if (hr < 0)
            {
                _startError = Marshal.GetExceptionForHR(hr);
                _ready.Set();
                return;
            }
            try
            {
                Msg msg;
                PeekMessage(out msg, IntPtr.Zero, 0, 0, PmNoRemove);    // Nachrichtenschlange anlegen, bevor jemand postet
                _nativeThreadId = GetCurrentThreadId();
                SetTimer(IntPtr.Zero, UIntPtr.Zero, 250, IntPtr.Zero);   // Herzschlag: eine verschluckte WM_APP wird spätestens beim nächsten WM_TIMER abgearbeitet
                _ready.Set();

                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.Hwnd == IntPtr.Zero && msg.Message == WmApp)
                    {
                        Drain();
                        continue;
                    }
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                    // Falls eine WM_APP-Nachricht in einer fremden Modalschleife unterging.
                    Drain();
                }
                Drain();
            }
            finally
            {
                OleUninitialize();
            }
        }

        private void Drain()
        {
            Action action;
            while (_work.TryDequeue(out action)) action();
        }

        private void Post(Action action)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShellPublisher));
            _work.Enqueue(action);
            if (!PostThreadMessage(_nativeThreadId, WmApp, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("The shell thread is no longer accepting requests.");
        }

        public T Invoke<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (Thread.CurrentThread == _thread) return func();

            T result = default(T);
            ExceptionDispatchInfo error = null;
            using (var done = new ManualResetEventSlim(false))
            {
                Post(() =>
                {
                    try { result = func(); }
                    catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
                    finally { done.Set(); }
                });
                done.Wait();
            }
            if (error != null) error.Throw();
            return result;
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        /// <summary>Baut das Datenobjekt auf diesem Thread — es gehört dann ihm.</summary>
        public VirtualFileDataObject Create(IFtpClient ftp, IReadOnlyList<RemoteItem> items)
        {
            return Invoke(() => new VirtualFileDataObject(ftp, items));
        }

        public Task SetClipboardAsync(VirtualFileDataObject data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return InvokeAsync(() =>
            {
                // Der bisherige Inhalt wird ersetzt: seine nie zu Ende gelesenen
                // Streams freigeben (Plan-1-Übergabe).
                if (_clipboard != null && !ReferenceEquals(_clipboard, data)) _clipboard.CloseOpenStreams();
                int hr = OleSetClipboard(data);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                _clipboard = data;
                return true;
            });
        }

        /// <summary>Gehört die Zwischenablage noch diesem Inhalt? Ein fremdes Kopieren ersetzt ihn still.</summary>
        public Task<bool> OwnsClipboardAsync(VirtualFileDataObject data)
        {
            return InvokeAsync(() =>
            {
                bool owns = data != null && OleIsCurrentClipboard(data) == 0;       // S_OK = ja, S_FALSE = nein
                if (!owns && _clipboard != null && ReferenceEquals(_clipboard, data))
                {
                    _clipboard.CloseOpenStreams();
                    _clipboard = null;
                }
                return owns;
            });
        }

        /// <summary>
        /// Zieht data per OLE. Aufzurufen auf dem UI-Thread (STA), der die Maus
        /// hält — er bekommt einen Proxy, Explorers Rückrufe gehen an diesen
        /// Thread. Lief danach kein asynchroner Kopiervorgang an (Ziel ohne
        /// IDataObjectAsyncCapability), gibt dieser Thread die offenen Streams
        /// frei (Plan-1-Übergabe).
        /// </summary>
        public int DoDragDrop(VirtualFileDataObject data, int allowedEffects)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("DoDragDrop requires an STA thread.");

            var proxy = MarshalToCaller(data);
            try
            {
                return ShellInterop.StartDrag(proxy, allowedEffects);
            }
            finally
            {
                Marshal.Release(proxy);
                Invoke(() =>
                {
                    bool inOperation;
                    data.InOperation(out inOperation);
                    if (!inOperation) data.CloseOpenStreams();
                    return true;
                });
            }
        }

        /// <summary>Roher IDataObject-Proxy im Apartment des Aufrufers; der Aufrufer gibt ihn mit Marshal.Release frei.</summary>
        internal IntPtr MarshalToCaller(VirtualFileDataObject data)
        {
            var stream = Invoke(() =>
            {
                var iid = IidIDataObject;
                IntPtr s;
                int hr = CoMarshalInterThreadInterfaceInStream(ref iid, data, out s);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                return s;
            });

            var iidCaller = IidIDataObject;
            IntPtr proxy;
            int hrCaller = CoGetInterfaceAndReleaseStream(stream, ref iidCaller, out proxy);
            if (hrCaller < 0) Marshal.ThrowExceptionForHR(hrCaller);
            return proxy;
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                Invoke(() =>
                {
                    // Beim Beenden die Zwischenablage freigeben, falls sie noch uns
                    // gehört — ohne OleFlushClipboard, das alles herunterlüde (Spec).
                    if (_clipboard != null)
                    {
                        if (OleIsCurrentClipboard(_clipboard) == 0) OleSetClipboard(null);
                        _clipboard.CloseOpenStreams();
                        _clipboard = null;
                    }
                    return true;
                });
            }
            finally
            {
                _disposed = true;
                PostThreadMessage(_nativeThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
                _thread.Join(TimeSpan.FromSeconds(5));
                _ready.Dispose();
            }
        }
    }
}

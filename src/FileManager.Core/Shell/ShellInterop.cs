using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// OLE direkt statt über WinForms: WinForms wickelt Datenobjekte in ein
    /// eigenes DataObject, das IDataObjectAsyncCapability verschluckt — dann
    /// kopierte Explorer synchron und DoDragDrop hielte unseren UI-Thread für
    /// die ganze Kopie fest. Die App verwendet ShellPublisher: dessen STA setzt die
    /// Zwischenablage, der UI-Thread zieht mit einem Proxy auf dessen Datenobjekt.
    /// Explorers Rückrufe laufen auf dem Publisher-Thread.
    ///
    /// OleFlushClipboard wird bewusst NIE gerufen: das erzwänge beim Beenden
    /// das Herunterladen aller Inhalte. Nur per Fahrprobe prüfbar.
    /// </summary>
    public static class ShellInterop
    {
        public const int DropEffectCopy = 1;
        public const int DropEffectMove = 2;
        [DllImport("ole32.dll", EntryPoint = "DoDragDrop")]
        private static extern int DoDragDropRaw(IntPtr pDataObj, IDropSource pDropSource, int dwOkEffects, out int pdwEffect);
   
        /// <summary>DoDragDrop mit einem rohen (Proxy-)Zeiger — siehe ShellPublisher.DoDragDrop.</summary>
        internal static int StartDrag(IntPtr dataObject, int allowedEffects)
        {
            int effect;
            int hr = DoDragDropRaw(dataObject, new DropSource(), allowedEffects, out effect);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            return effect;
        }
   

        [DllImport("ole32.dll")]
        private static extern int DoDragDrop(IDataObject pDataObj, IDropSource pDropSource, int dwOkEffects, out int pdwEffect);

        [DllImport("ole32.dll")]
        private static extern int OleSetClipboard(IDataObject pDataObj);

        [DllImport("ole32.dll")]
        internal static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

        /// <summary>
        /// Zieht `data` per DoDragDrop (blockiert den aufrufenden — STA — Thread, bis
        /// Explorer fertig ist oder abbricht). Kehrt DoDragDrop zurück, ohne dass ein
        /// Async-Handshake stattfand (Ziel unterstützt kein IDataObjectAsyncCapability),
        /// ruft die App anschließend data.CloseOpenStreams() auf, um von Explorer nicht
        /// bis ans Ende gelesene Streams freizugeben — im Async-Fall übernimmt das
        /// bereits EndOperation.
        /// </summary>
        public static int StartDrag(VirtualFileDataObject data)
        {
            int effect;
            int hr = DoDragDrop(data, new DropSource(), DropEffectCopy, out effect);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            return effect;
        }

        /// <summary>
        /// Veröffentlicht `data` als Zwischenablage-Inhalt. Die App ruft
        /// data.CloseOpenStreams() auf, sobald dieser Inhalt durch einen neuen ersetzt
        /// wird (erneutes SetClipboard, fremdes OleSetClipboard) — bis dahin bleiben
        /// angebotene, aber nie zu Ende gelesene Streams offen.
        /// </summary>
        public static void SetClipboard(VirtualFileDataObject data)
        {
            int hr = OleSetClipboard(data);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
    }

    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, int grfKeyState);

        [PreserveSig]
        int GiveFeedback(int dwEffect);
    }

    internal sealed class DropSource : IDropSource
    {
        private const int SOk = 0;
        private const int DragDropSDrop = 0x00040100;
        private const int DragDropSCancel = 0x00040101;
        private const int DragDropSUseDefaultCursors = 0x00040102;
        private const int MkLButton = 0x1;
        private const int MkRButton = 0x2;

        public int QueryContinueDrag(bool fEscapePressed, int grfKeyState)
        {
            if (fEscapePressed) return DragDropSCancel;
            if ((grfKeyState & (MkLButton | MkRButton)) == 0) return DragDropSDrop;
            return SOk;
        }

        public int GiveFeedback(int dwEffect)
        {
            return DragDropSUseDefaultCursors;
        }
    }
}

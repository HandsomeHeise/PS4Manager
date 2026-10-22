using System;
using System.Runtime.InteropServices;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// Shell-Schnittstelle (früher IAsyncOperation): bietet ein Datenobjekt
    /// sie an, kopiert Explorer in einem eigenen Hintergrund-Thread und
    /// DoDragDrop kehrt sofort zurück. Reihenfolge der Methoden = VTable.
    /// </summary>
    [ComImport]
    [Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDataObjectAsyncCapability
    {
        void SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool fDoOpAsync);
        void GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool pfIsOpAsync);
        void StartOperation(IntPtr pbcReserved);
        void InOperation([MarshalAs(UnmanagedType.Bool)] out bool pfInAsyncOp);
        void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects);
    }
}

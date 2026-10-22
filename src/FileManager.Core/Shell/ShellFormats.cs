using System.Runtime.InteropServices;

namespace FileManager.Core.Shell
{
    /// <summary>Die registrierten Zwischenablage-Formate der Shell (IDs ab 0xC000 — als short negativ, das ist korrekt).</summary>
    internal static class ShellFormats
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterClipboardFormat(string format);

        public static readonly short FileGroupDescriptorW = Register("FileGroupDescriptorW");
        public static readonly short FileContents = Register("FileContents");
        public static readonly short PreferredDropEffect = Register("Preferred DropEffect");

        private static short Register(string name)
        {
            return unchecked((short)RegisterClipboardFormat(name));
        }
    }
}

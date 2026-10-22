using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FileManager.Core.Presentation;

namespace FileManager.App.Shell
{
    /// <summary>
    /// Symbole wie im Explorer und sprachunabhängige englische Typnamen:
    /// SHGetFileInfo mit SHGFI_USEFILEATTRIBUTES — ohne Download, nur über die
    /// Endung, je Endung einmal geholt. Die Bilder gehören dieser Klasse.
    /// Nur auf dem UI-Thread benutzen.
    /// </summary>
    internal sealed class ShellFileInfo : IDisposable
    {
        private const uint FileAttributeNormal = 0x80;
        private const uint FileAttributeDirectory = 0x10;
        private const uint ShgfiIcon = 0x100;
        private const uint ShgfiLargeIcon = 0x0;
        private const uint ShgfiSmallIcon = 0x1;
        private const uint ShgfiUseFileAttributes = 0x10;
        private const string FolderKey = "<ordner>";
        private const string NoExtensionKey = "<datei>";
        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileInfo
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);

        private readonly Dictionary<string, Image> _small = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Image> _large = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public Image SmallIcon(string name, bool isFolder)
        {
            return IconFor(_small, Key(name, isFolder), isFolder, ShgfiSmallIcon);
        }

        public Image LargeIcon(string name, bool isFolder)
        {
            return IconFor(_large, Key(name, isFolder), isFolder, ShgfiLargeIcon);
        }

        /// <summary>Das Verknüpfungssymbol (wie eine .lnk-Datei).</summary>
        public Image LinkIcon()
        {
            return IconFor(_small, ".lnk", false, ShgfiSmallIcon);
        }

        public string TypeName(string name, bool isFolder)
        {
            return FileTypeText.For(name, isFolder);
        }

        private static Image IconFor(Dictionary<string, Image> cache, string key, bool isFolder, uint size)
        {
            Image image;
            if (cache.TryGetValue(key, out image)) return image;

            var info = new ShFileInfo();
            SHGetFileInfo(Probe(key, isFolder), isFolder ? FileAttributeDirectory : FileAttributeNormal,
                ref info, (uint)Marshal.SizeOf(typeof(ShFileInfo)), ShgfiIcon | size | ShgfiUseFileAttributes);
            if (info.hIcon != IntPtr.Zero)
            {
                using (var icon = Icon.FromHandle(info.hIcon)) image = icon.ToBitmap();
                DestroyIcon(info.hIcon);
            }
            cache[key] = image;                               // auch null merken — kein zweiter Versuch
            return image;
        }

        /// <summary>
        /// Ordner teilen sich ein Symbol, Dateien eines je Endung. Von Hand statt
        /// Path.GetExtension: PS4-Namen dürfen Zeichen enthalten, an denen die
        /// Pfadfunktionen von Windows werfen.
        /// </summary>
        private static string Key(string name, bool isFolder)
        {
            if (isFolder) return FolderKey;
            var n = name ?? "";
            int dot = n.LastIndexOf('.');
            if (dot <= 0 || dot == n.Length - 1) return NoExtensionKey;
            var extension = n.Substring(dot);
            return extension.IndexOfAny(InvalidNameChars) >= 0 ? NoExtensionKey : extension;
        }

        private static string Probe(string key, bool isFolder)
        {
            if (isFolder) return "folder";
            return key == NoExtensionKey ? "file" : "file" + key;
        }

        public void Dispose()
        {
            foreach (var image in _small.Values.Concat(_large.Values))
            {
                if (image != null) image.Dispose();
            }
            _small.Clear();
            _large.Clear();
        }
    }
}

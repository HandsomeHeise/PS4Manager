using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileManager.Core.Shell
{
    /// <summary>
    /// FILEGROUPDESCRIPTORW von Hand gebaut statt per Struct-Marshaling: UINT
    /// cItems, dann je Eintrag 592 Byte FILEDESCRIPTORW —
    /// dwFlags@0, clsid@4, sizel@20, pointl@28, dwFileAttributes@36,
    /// ftCreationTime@40, ftLastAccessTime@48, ftLastWriteTime@56,
    /// nFileSizeHigh@64, nFileSizeLow@68, cFileName[260]@72.
    /// </summary>
    internal static class FileDescriptorWriter
    {
        public const int DescriptorSize = 592;

        private const uint FdAttributes = 0x00000004;
        private const uint FdWritesTime = 0x00000020;
        private const uint FdFileSize = 0x00000040;
        private const uint FdProgressUi = 0x00004000;
        private const uint FdUnicode = 0x80000000;
        private const uint FileAttributeDirectory = 0x10;
        private const uint FileAttributeNormal = 0x80;
        private const int MaxNameChars = 259;

        public static byte[] Write(IReadOnlyList<VirtualFile> files)
        {
            var buffer = new byte[4 + files.Count * DescriptorSize];
            Put(buffer, 0, (uint)files.Count);

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                int o = 4 + i * DescriptorSize;

                bool hasTime = file.Modified.Year > 1601;
                uint flags = FdAttributes | FdProgressUi | FdUnicode;
                if (!file.IsDirectory) flags |= FdFileSize;
                if (hasTime) flags |= FdWritesTime;
                Put(buffer, o, flags);
                Put(buffer, o + 36, file.IsDirectory ? FileAttributeDirectory : FileAttributeNormal);

                if (hasTime)
                {
                    long fileTime = file.Modified.ToFileTime();
                    Put(buffer, o + 56, (uint)(fileTime & 0xFFFFFFFF));
                    Put(buffer, o + 60, (uint)(fileTime >> 32));
                }

                if (!file.IsDirectory)
                {
                    Put(buffer, o + 64, (uint)(file.Size >> 32));
                    Put(buffer, o + 68, (uint)(file.Size & 0xFFFFFFFF));
                }

                if (file.RelativeName.Length > MaxNameChars)
                    throw new PathTooLongException("Path is too long for Explorer: " + file.RelativeName);
                Encoding.Unicode.GetBytes(file.RelativeName).CopyTo(buffer, o + 72);   // Rest bleibt 0 = NUL-Ende
            }
            return buffer;
        }

        private static void Put(byte[] buffer, int offset, uint value)
        {
            BitConverter.GetBytes(value).CopyTo(buffer, offset);
        }
    }
}

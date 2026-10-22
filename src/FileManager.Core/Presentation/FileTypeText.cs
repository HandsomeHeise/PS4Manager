using System;

namespace FileManager.Core.Presentation
{
    public static class FileTypeText
    {
        public static string For(string name, bool isFolder)
        {
            if (isFolder) return "File folder";
            var value = name ?? "";
            int dot = value.LastIndexOf('.');
            if (dot <= 0 || dot == value.Length - 1) return "File";
            return value.Substring(dot + 1).ToUpperInvariant() + " file";
        }
    }
}

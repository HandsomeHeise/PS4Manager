using System;
using System.IO;

namespace FileManager.Core.Editing
{
    /// <summary>Legt fest, welche Dateien im File Manager direkt bearbeitet werden.</summary>
    public static class IniFilePolicy
    {
        public static bool IsEditable(string remotePath)
        {
            if (string.IsNullOrEmpty(remotePath)) return false;
            return string.Equals(Path.GetExtension(remotePath), ".ini", StringComparison.OrdinalIgnoreCase);
        }
    }
}

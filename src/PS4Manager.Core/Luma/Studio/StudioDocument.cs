using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PS4Manager.Core.Luma.Studio
{
    public sealed class StudioFileConflictException : IOException { public StudioFileConflictException() : base("The file changed on disk. Save As or explicitly approve overwriting it.") { } }
    public sealed class StudioDocument
    {
        private string source, savedSource, diskHash;
        private bool bom;
        public StudioDocument(string title, string source = "") { Title = title; this.source = source ?? ""; savedSource = ""; }
        public Guid Id { get; } = Guid.NewGuid();
        public string FilePath { get; private set; }
        public string Title { get; private set; }
        public long Version { get; private set; }
        public string Source { get => source; set { if (source == value) return; source = value ?? ""; Version++; } }
        public bool IsDirty => source != savedSource || FilePath == null;
        public static StudioDocument Load(string path) {
            path = Path.GetFullPath(path); var info = new FileInfo(path); if (info.Length > LuaSyntaxChecker.MaxSourceBytes + 3) throw new IOException("File exceeds the 1 MiB editor limit.");
            var bytes = File.ReadAllBytes(path); bool bom = bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 });
            string text = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            if (text.IndexOf('\0') >= 0) throw new IOException("Binary files cannot be opened as Lua source.");
            return new StudioDocument(Path.GetFileName(path), text) { FilePath = path, savedSource = text, diskHash = Hash(bytes), bom = bom };
        }
        public void Save(string path = null, bool overwrite = false) {
            path = Path.GetFullPath(path ?? FilePath ?? throw new InvalidOperationException("Choose a file name first."));
            bool same = string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase);
            if (File.Exists(path) && !overwrite && (!same || Hash(File.ReadAllBytes(path)) != diskHash)) throw new StudioFileConflictException();
            if (same && !File.Exists(path) && !overwrite) throw new StudioFileConflictException();
            byte[] bytes = new UTF8Encoding(bom).GetPreamble().Concat(new UTF8Encoding(false, true).GetBytes(source)).ToArray();
            if (bytes.Length > LuaSyntaxChecker.MaxSourceBytes + 3) throw new IOException("Script exceeds the 1 MiB limit.");
            string temporary = path + ".luma-studio-" + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                FilePath = path; Title = Path.GetFileName(path); diskHash = Hash(bytes); savedSource = source;
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes)); }
    }
    public static class StudioProject
    {
        public static string[] Files(string root, int entryBudget = 5000) {
            if (entryBudget < 1 || entryBudget > 5000) throw new ArgumentOutOfRangeException(nameof(entryBudget));
            var files = new System.Collections.Generic.List<string>(); Walk(Path.GetFullPath(root), 0, files, ref entryBudget); return files.ToArray();
        }
        private static void Walk(string directory, int depth, System.Collections.Generic.List<string> files, ref int remaining) {
            if (depth > 12 || files.Count >= 1000 || remaining <= 0) return;
            try {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                foreach (var file in Directory.EnumerateFiles(directory, "*.lua").Take(Math.Min(remaining, 1000 - files.Count)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
                    remaining--;
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) files.Add(file);
                }
                foreach (var child in Directory.EnumerateDirectories(directory).Take(remaining).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
                    if (remaining <= 0 || files.Count >= 1000) return;
                    remaining--;
                    string name = Path.GetFileName(child); if (name.StartsWith(".") || new[] { "bin", "obj", "node_modules" }.Contains(name, StringComparer.OrdinalIgnoreCase) || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    Walk(child, depth + 1, files, ref remaining);
                }
            } catch (UnauthorizedAccessException) { } catch (IOException) { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PS4Manager.Core.Luma.Studio
{
    public static class StudioScriptPath
    {
        public static string Validate(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 200 || path.Split('/').Length > 13 ||
                !Regex.IsMatch(path, @"\A(?:[A-Za-z0-9][A-Za-z0-9_.-]*/)*[A-Za-z0-9][A-Za-z0-9_.-]*\.lua\z", RegexOptions.CultureInvariant) ||
                path.Split('/').Any(p => p.EndsWith(".") || IsDevice(p)))
                throw new ArgumentException("Use ASCII Lua paths below scripts/, without traversal, device names or trailing dots (max 200 characters / 12 folders).", nameof(path));
            return path;
        }
        internal static bool IsDevice(string name) => Regex.IsMatch(name.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public sealed class StudioWorkspace
    {
        private const string ManifestName = "luma-project.json";
        [DataContract] private sealed class Metadata
        {
            [DataMember(Name = "schema")] public int Schema;
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "profile")] public string Profile;
        }
        private string manifestHash;
        private StudioWorkspace(string root, string name, string profile, string hash) { Root = root; Name = name; Profile = profile; manifestHash = hash; }
        public string Root { get; }
        public string Name { get; }
        public string Profile { get; }
        public static StudioWorkspace Create(string parent, string name, string profile)
        {
            ValidateName(name); LuaApiCatalog.ForProfile(profile);
            string root = Path.Combine(Path.GetFullPath(parent), name);
            if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Project destination already exists. Open it instead.");
            Directory.CreateDirectory(root);
            var result = new StudioWorkspace(root, name, profile, null);
            result.Save();
            using (var file = new FileStream(Path.Combine(root, "main.lua"), FileMode.CreateNew)) {
                byte[] source = Encoding.UTF8.GetBytes("-- " + name.Replace("\r", "").Replace("\n", "") + "\nprint('Hello from Luma')\n");
                file.Write(source, 0, source.Length);
            }
            return result;
        }
        public static StudioWorkspace Open(string root, string fallbackProfile)
        {
            root = Path.GetFullPath(root); EnsureSafe(root, root);
            string path = Path.Combine(root, ManifestName);
            if (!File.Exists(path)) { LuaApiCatalog.ForProfile(fallbackProfile); return new StudioWorkspace(root, new DirectoryInfo(root).Name, fallbackProfile, null); }
            EnsureSafe(root, path);
            byte[] bytes = ReadBounded(path, 8192);
            Metadata metadata;
            using (var stream = new MemoryStream(bytes)) metadata = (Metadata)new DataContractJsonSerializer(typeof(Metadata)).ReadObject(stream);
            if (metadata == null || metadata.Schema != 1) throw new IOException("Unsupported Luma project manifest version.");
            ValidateName(metadata.Name); LuaApiCatalog.ForProfile(metadata.Profile);
            return new StudioWorkspace(root, metadata.Name, metadata.Profile, Hash(bytes));
        }
        public void Save()
        {
            string path = Path.Combine(Root, ManifestName); EnsureSafe(Root, path);
            if (File.Exists(path) ? manifestHash == null || Hash(ReadBounded(path, 8192)) != manifestHash : manifestHash != null)
                throw new StudioFileConflictException();
            byte[] bytes;
            using (var stream = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(Metadata)).WriteObject(stream, new Metadata { Schema = 1, Name = Name, Profile = Profile }); bytes = stream.ToArray();
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                manifestHash = Hash(bytes);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public string[] ScriptPaths()
        {
            var files = new List<string>(); int remaining = 5000;
            Walk(Root, 0, files, ref remaining);
            return files.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }
        private void Walk(string directory, int depth, List<string> files, ref int remaining)
        {
            EnsureSafe(Root, directory);
            if (depth > 12) throw new IOException("Project exceeds 12 directory levels.");
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory)) {
                if (--remaining < 0) throw new IOException("Project exceeds the 5,000-entry scan limit.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Project contains a filesystem link: " + entry);
                string name = Path.GetFileName(entry);
                if ((attributes & FileAttributes.Directory) != 0) {
                    if (name.StartsWith(".") || new[] { "bin", "obj", "node_modules" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    Walk(entry, depth + 1, files, ref remaining);
                } else if (name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) {
                    string relative = entry.Substring(Root.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                    StudioScriptPath.Validate(relative); files.Add(relative);
                    if (files.Count > 200) throw new IOException("Project exceeds the 200-script sync limit.");
                }
            }
        }
        internal byte[] ReadScript(string relative)
        {
            StudioScriptPath.Validate(relative); string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            EnsureSafe(Root, path); return ReadBounded(path, LuaSyntaxChecker.MaxSourceBytes);
        }
        internal static byte[] ReadBounded(string path, int limit)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (stream.Length > limit) throw new IOException("File exceeds its size limit: " + Path.GetFileName(path));
                byte[] bytes = new byte[(int)stream.Length]; int offset = 0;
                while (offset < bytes.Length) { int count = stream.Read(bytes, offset, bytes.Length - offset); if (count == 0) throw new IOException("Incomplete file read."); offset += count; }
                return bytes;
            }
        }
        internal static void EnsureSafe(string root, string path)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); path = Path.GetFullPath(path);
            if (path != root && !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path escapes the project.");
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)) {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Filesystem links are not allowed in project paths.");
            }
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        }
        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.EndsWith(" ") || name == "." || name == ".." || StudioScriptPath.IsDevice(name))
                throw new ArgumentException("Choose a valid project name (1–80 characters).", nameof(name));
        }
        internal static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes)); }
    }
}

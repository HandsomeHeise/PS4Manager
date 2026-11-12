using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS4Manager.Core.Luma.Studio
{
    public enum StudioSyncStatus { New, Changed, Unchanged, RemoteOnly }
    public sealed class StudioSyncEntry
    {
        internal StudioSyncEntry(string path, byte[] local, string remoteHash)
        { Path = path; Bytes = local; LocalHash = local == null ? null : StudioWorkspace.Hash(local); RemoteHash = remoteHash;
            Status = local == null ? StudioSyncStatus.RemoteOnly : remoteHash == null ? StudioSyncStatus.New : LocalHash == remoteHash ? StudioSyncStatus.Unchanged : StudioSyncStatus.Changed; }
        public string Path { get; }
        public StudioSyncStatus Status { get; }
        public bool CanUpload => Status == StudioSyncStatus.New || Status == StudioSyncStatus.Changed;
        internal byte[] Bytes { get; }
        internal string LocalHash { get; }
        internal string RemoteHash { get; }
        public override string ToString() => Status + "   scripts/" + Path;
    }
    public sealed class StudioProjectPreview
    {
        internal StudioProjectPreview(StudioWorkspace project, long generation, StudioConnection connection, IEnumerable<StudioSyncEntry> entries)
        {
            Project = project; Generation = generation; Connection = connection; Entries = Array.AsReadOnly(entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray());
            ConfirmationText = connection.PrepareUpload("project.lua").ConfirmationText.Split(new[] { "\r\n\r\n" }, StringSplitOptions.None)[0] +
                "\r\n\r\nProject: " + project.Name + "\r\nLocal root: " + project.Root +
                "\r\nTargets are relative to the connected plugin sandbox; game identity is not verified. Confirm the correct game is running." +
                "\r\nSelected existing files will be REPLACED. No deletes, execution or reload.\r\nRemote checks and writes are not atomic: close other editing clients.\r\nContinue with the selected files?";
        }
        internal StudioWorkspace Project { get; }
        internal StudioConnection Connection { get; }
        public long Generation { get; }
        public IReadOnlyList<StudioSyncEntry> Entries { get; }
        public string ConfirmationText { get; }
    }
    public static class StudioProjectSync
    {
        public static async Task<StudioProjectPreview> CompareAsync(StudioConnection connection, StudioWorkspace project, CancellationToken ct)
        {
            if (connection == null || project == null) throw new ArgumentNullException();
            if (!connection.Connected || connection.Profile != project.Profile) throw new InvalidOperationException("Connect to the project's game profile first.");
            long generation = connection.Generation;
            string[] paths = project.ScriptPaths();
            var local = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (string path in paths) {
                ct.ThrowIfCancellationRequested(); byte[] bytes = project.ReadScript(path); Decode(bytes);
                if (local.ContainsKey(path)) throw new IOException("Case-colliding deployment paths: " + path);
                local.Add(path, bytes); total += bytes.Length;
                if (total > 16 * 1024 * 1024) throw new IOException("Project exceeds the 16 MiB sync limit.");
            }
            var entries = new List<StudioSyncEntry>();
            var directories = new HashSet<string>(paths.Select(DirectoryPart), StringComparer.Ordinal) { "" };
            foreach (string directory in directories.OrderBy(p => p, StringComparer.Ordinal)) {
                var listing = await connection.ListProjectAsync(directory, generation, ct).ConfigureAwait(false);
                foreach (string path in paths.Where(p => DirectoryPart(p) == directory)) {
                    string name = FilePart(path);
                    if (listing.Any(n => n != name && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) throw new IOException("Remote filename case differs: " + path);
                    string remoteHash = listing.Contains(name, StringComparer.Ordinal) ? StudioWorkspace.Hash(await connection.ReadProjectAsync(path, generation, ct).ConfigureAwait(false)) : null;
                    entries.Add(new StudioSyncEntry(path, local[path], remoteHash));
                }
                foreach (string name in listing.Where(n => n.EndsWith(".lua", StringComparison.Ordinal))) {
                    string path = directory.Length == 0 ? name : directory + "/" + name;
                    StudioScriptPath.Validate(path);
                    if (!local.ContainsKey(path)) entries.Add(new StudioSyncEntry(path, null, null));
                    if (entries.Count > 1200) throw new IOException("Remote preview exceeds 1,200 rows.");
                }
            }
            if (connection.Generation != generation) throw new OperationCanceledException("Luma session changed.");
            return new StudioProjectPreview(project, generation, connection, entries);
        }
        public static async Task UploadAsync(StudioConnection connection, StudioProjectPreview preview, IEnumerable<string> selectedPaths, Action<string> completed, CancellationToken ct)
        {
            if (preview == null || connection == null || selectedPaths == null) throw new ArgumentNullException();
            if (preview.Connection != connection || preview.Generation != connection.Generation || !connection.Connected || preview.Project.Profile != connection.Profile)
                throw new OperationCanceledException("Session changed; compare the project again.");
            var selected = selectedPaths.ToArray();
            if (selected.Length == 0 || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length) throw new ArgumentException("Select one or more distinct changed files.");
            var entries = selected.Select(p => preview.Entries.SingleOrDefault(e => e.Path == p) ?? throw new ArgumentException("File is not in this preview.")).ToArray();
            var checker = new LuaSyntaxChecker();
            foreach (var entry in entries) {
                ct.ThrowIfCancellationRequested();
                if (!entry.CanUpload) throw new InvalidOperationException("Only new or changed local scripts can be uploaded.");
                if (StudioWorkspace.Hash(preview.Project.ReadScript(entry.Path)) != entry.LocalHash) throw new StudioFileConflictException();
                var check = checker.Check(Decode(entry.Bytes), entry.Path);
                if (!check.Success) throw new LumaException(entry.Path + ": " + check.Diagnostics[0].Message);
            }
            foreach (var entry in entries) {
                ct.ThrowIfCancellationRequested();
                string[] listing = await connection.ListProjectAsync(DirectoryPart(entry.Path), preview.Generation, ct).ConfigureAwait(false);
                string name = FilePart(entry.Path);
                if (listing.Any(n => n != name && string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) throw new IOException("Remote filename case changed: " + entry.Path);
                string remoteHash = listing.Contains(name, StringComparer.Ordinal) ? StudioWorkspace.Hash(await connection.ReadProjectAsync(entry.Path, preview.Generation, ct).ConfigureAwait(false)) : null;
                if (remoteHash != entry.RemoteHash) throw new IOException("Remote file changed since comparison: " + entry.Path + ". Compare again.");
                if (StudioWorkspace.Hash(preview.Project.ReadScript(entry.Path)) != entry.LocalHash) throw new StudioFileConflictException();
                await connection.PutProjectAsync(entry.Path, entry.Bytes, preview.Generation, ct).ConfigureAwait(false);
                completed?.Invoke(entry.Path);
            }
        }
        private static string DirectoryPart(string path) { int slash = path.LastIndexOf('/'); return slash < 0 ? "" : path.Substring(0, slash); }
        private static string FilePart(string path) => path.Substring(path.LastIndexOf('/') + 1);
        private static string Decode(byte[] bytes)
        {
            string source = new UTF8Encoding(false, true).GetString(bytes);
            if (source.IndexOf('\0') >= 0 || source.StartsWith("\u001b")) throw new IOException("Only UTF-8 Lua source can be synchronized.");
            return source.TrimStart('\ufeff');
        }
    }
}

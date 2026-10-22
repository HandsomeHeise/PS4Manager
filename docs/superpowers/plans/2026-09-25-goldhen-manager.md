# GoldHEN Manager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the GoldHEN Manager as the third tool window of `PS4Manager.exe`: discover the console's GoldHEN folders and configuration, show plugins/cheats/patches, toggle plugin activation by editing the discovered configuration, and manage plugin and cheat files through the shared queue — every change preceded by a completed local safety backup.

**Architecture:** Headless logic (discovery, line-based configuration editing, safety backups, inventory grouping) lives in `PS4Manager.Core/GoldHen/` and is unit-tested with focused in-memory fakes, mirroring `QuickAccessBuilder`'s "a missing path is not an error" handling and its `FtpGates.RunAsync` reads. The window lives in `PS4Manager.App` and consumes the published `ManagerSession` exactly like the other two tool windows: it plans every write with the existing `FileOperations` and executes it on the shared `TransferQueue`.

**Tech Stack:** C# 7.3, .NET Framework 4.8, WinForms, UIFramework controls, xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-25-goldhen-manager-design.md`

**Branch:** work on a branch `goldhen-manager` created from `main`; after the human's probe passes, merge it locally into `main`. No push.

## Global Constraints

- Target `net48` with `LangVersion` `7.3`; German comments in the app layer.
- Exactly one `FtpClient`, guard, gate set and queue per session. The module never constructs an FTP client and never writes outside `FileOperations` + `TransferQueue`.
- Reads use the existing seams: `DirectoryCache.ListAsync` under `FtpGates.Browse` for listings, `FtpGates.RunAsync(gates.Browse, () => ftp.DownloadAsync(...), ct)` for file contents.
- A missing root, folder or candidate is `null`/`Exists = false`, never an exception — copy `QuickAccessBuilder.TryListAsync`'s filter: rethrow `OperationCanceledException` and transient `FtpException`, swallow everything else.
- Codecs: `TextFileContent` keeps encoding and BOM; the toggle must leave every untouched byte alone, including line terminators.
- Safety backups are local (`%LocalAppData%\PS4Manager\backups\goldhen`), run **before** any change, and abort the change when they fail.
- Headless code is tested; the app layer has no test project, so the window's verification is build + greps + the human probe.
- No UIFramework change, no new project.

---

### Task 1: Discovery and plugin state

**Files:**
- Create: `src/PS4Manager.Core/GoldHen/GoldHenSection.cs`
- Create: `src/PS4Manager.Core/GoldHen/GoldHenActivation.cs`
- Create: `src/PS4Manager.Core/GoldHen/GoldHenLocation.cs`
- Create: `src/PS4Manager.Core/GoldHen/PluginEntry.cs`
- Create: `src/PS4Manager.Core/GoldHen/GoldHenDiscovery.cs`
- Create: `src/PS4Manager.Core/GoldHen/IniPluginEditor.cs`
- Create: `src/PS4Manager.Core/GoldHen/GoldHenDiscoveryService.cs`
- Modify: `src/FileManager.Core/Editing/TextFileContent.cs` (add `FromBytes`)
- Test: `tests/PS4Manager.Core.Tests/GoldHen/IniPluginEditorTests.cs`
- Test: `tests/PS4Manager.Core.Tests/GoldHen/GoldHenDiscoveryServiceTests.cs`

**Interfaces:**
- Consumes: `FileManager.Core.Browsing.DirectoryCache`, `FileManager.Core.Browsing.FtpGates`, `Ps4.Core.Ftp.IFtpClient`, `FileManager.Core.Editing.TextFileContent`.
- Produces:
  - `GoldHenDiscoveryService(DirectoryCache listings, IFtpClient ftp, FtpGates gates)`
  - `Task<GoldHenDiscovery> GoldHenDiscoveryService.DiscoverAsync(CancellationToken ct)`
  - `Task<IReadOnlyList<PluginEntry>> GoldHenDiscoveryService.ReadPluginsAsync(string configurationPath, IReadOnlyList<RemoteItem> pluginFiles, CancellationToken ct)`
  - `IReadOnlyList<PluginEntry> IniPluginEditor.Parse(string text, IReadOnlyList<string> pluginFileNames)`
  - `TextFileContent.FromBytes(byte[] bytes)`

- [ ] **Step 1: Write the failing parser tests**

Create `tests/PS4Manager.Core.Tests/GoldHen/IniPluginEditorTests.cs`:

```csharp
using System.Linq;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class IniPluginEditorTests
    {
        private static readonly string[] Files = { "hen.prx", "debug.prx", "tool.prx" };

        [Fact]
        public void A_line_naming_a_plugin_file_is_enabled()
        {
            var entries = IniPluginEditor.Parse("hen.prx\n", Files);

            var entry = Assert.Single(entries);
            Assert.Equal("hen.prx", entry.Name);
            Assert.Equal(GoldHenActivation.Enabled, entry.Activation);
            Assert.True(entry.FilePresent);
            Assert.Equal(0, entry.LineIndex);
        }

        [Fact]
        public void A_commented_line_naming_a_plugin_file_is_disabled()
        {
            var entries = IniPluginEditor.Parse("; hen.prx\n# debug.prx\n", Files);

            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Equal(GoldHenActivation.Disabled, e.Activation));
            Assert.Equal(new[] { "hen.prx", "debug.prx" }, entries.Select(e => e.Name));
        }

        [Fact]
        public void Comments_blank_lines_and_unknown_names_are_not_entries()
        {
            var text = "; a comment\n\n[misc]\nother.prx\n/tmp/hen.prx\nhen.prx\n";

            var entries = IniPluginEditor.Parse(text, Files);

            var entry = Assert.Single(entries);
            Assert.Equal("hen.prx", entry.Name);
            Assert.Equal(5, entry.LineIndex);
        }

        [Fact]
        public void The_file_name_comparison_ignores_case()
        {
            var entries = IniPluginEditor.Parse("HEN.PRX\n", Files);

            Assert.Equal(GoldHenActivation.Enabled, Assert.Single(entries).Activation);
        }

        [Fact]
        public void An_empty_text_yields_no_entries()
        {
            Assert.Empty(IniPluginEditor.Parse("", Files));
            Assert.Empty(IniPluginEditor.Parse(null, Files));
        }
    }
}
```

- [ ] **Step 2: Run the parser tests to verify they fail**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~IniPluginEditorTests`
Expected: FAIL — `IniPluginEditor` does not exist.

- [ ] **Step 3: Write the failing discovery tests**

Create `tests/PS4Manager.Core.Tests/GoldHen/GoldHenDiscoveryServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using PS4Manager.Core.GoldHen;
using Ps4.Core.Ftp;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenDiscoveryServiceTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task A_console_without_goldhen_reports_no_root_and_no_error()
        {
            var console = new FakeConsole();
            console.AddDir("/");
            console.AddDir("/data");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.False(discovery.RootExists);
            Assert.Empty(discovery.Plugins);
            Assert.Null(discovery.ConfigurationPath);
            Assert.All(discovery.Locations, l => Assert.False(l.Exists));
        }

        [Fact]
        public async Task A_partial_install_reports_only_what_is_there()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddDir("/data/GoldHEN/plugins");
            console.AddFile("/data/GoldHEN/plugins/hen.prx", "plugin");
            console.AddFile("/data/GoldHEN/plugins.ini", "; hen.prx\n");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.True(discovery.RootExists);
            Assert.True(discovery.Location(GoldHenSection.Plugins).Exists);
            Assert.False(discovery.Location(GoldHenSection.Cheats).Exists);
            Assert.False(discovery.Location(GoldHenSection.Patches).Exists);
        }

        [Fact]
        public async Task The_first_existing_configuration_candidate_is_the_activation_source()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddFile("/data/GoldHEN/config.ini", "");
            console.AddFile("/data/GoldHEN/plugins.txt", "");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.Equal("/data/GoldHEN/config.ini", discovery.ConfigurationPath);
            Assert.Equal(new[] { "/data/GoldHEN/config.ini", "/data/GoldHEN/plugins.txt" },
                discovery.ConfigurationFiles);
        }

        [Fact]
        public async Task Plugins_the_configuration_does_not_mention_are_disabled_without_a_line()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddDir("/data/GoldHEN/plugins");
            console.AddFile("/data/GoldHEN/plugins/hen.prx", "a");
            console.AddFile("/data/GoldHEN/plugins/tool.prx", "b");
            console.AddFile("/data/GoldHEN/plugins.ini", "hen.prx\n");

            var discovery = await Service(console).DiscoverAsync(None);

            var hen = discovery.Plugins.Single(p => p.Name == "hen.prx");
            var tool = discovery.Plugins.Single(p => p.Name == "tool.prx");
            Assert.Equal(GoldHenActivation.Enabled, hen.Activation);
            Assert.Equal(GoldHenActivation.Disabled, tool.Activation);
            Assert.Equal(-1, tool.LineIndex);
        }

        [Fact]
        public async Task Without_a_configuration_every_plugin_stays_unknown()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddDir("/data/GoldHEN/plugins");
            console.AddFile("/data/GoldHEN/plugins/hen.prx", "a");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.Equal(GoldHenActivation.Unknown, Assert.Single(discovery.Plugins).Activation);
        }

        [Fact]
        public async Task Paths_are_written_with_forward_slashes_and_the_root_prefix()
        {
            var console = new FakeConsole();
            console.AddDir("/data/GoldHEN");
            console.AddDir("/data/GoldHEN/plugins");
            console.AddFile("/data/GoldHEN/plugins/hen.prx", "a");

            var discovery = await Service(console).DiscoverAsync(None);

            Assert.Equal("/data/GoldHEN/plugins/hen.prx", Assert.Single(discovery.Plugins).Path);
        }

        private static GoldHenDiscoveryService Service(FakeConsole console)
        {
            var gates = new FtpGates();
            return new GoldHenDiscoveryService(new DirectoryCache(console, gates), console, gates);
        }

        /// <summary>Konsole im Speicher: Ordner, Dateien, sonst 550.</summary>
        private sealed class FakeConsole : IFtpClient
        {
            private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            private readonly HashSet<string> _dirs = new HashSet<string>(StringComparer.Ordinal) { "/" };

            public void AddDir(string path) { _dirs.Add(path); }

            public void AddFile(string path, string text)
            {
                var dir = path.Substring(0, path.LastIndexOf('/'));
                if (dir.Length == 0) dir = "/";
                _dirs.Add(dir);
                _files[path] = new UTF8Encoding(false).GetBytes(text);
            }

            public Task<IReadOnlyList<FtpEntry>> ListAsync(string path, CancellationToken ct)
            {
                if (!_dirs.Contains(path)) throw NotFound();
                var prefix = path == "/" ? "/" : path + "/";
                var entries = new List<FtpEntry>();
                foreach (var dir in _dirs.Where(d => d.StartsWith(prefix, StringComparison.Ordinal) && d.Length > prefix.Length)
                                          .Select(d => d.Substring(prefix.Length)).Where(n => n.IndexOf('/') < 0))
                    entries.Add(new FtpEntry(dir, true, 0));
                foreach (var file in _files.Keys.Where(f => f.StartsWith(prefix, StringComparison.Ordinal)
                                                            && f.Substring(prefix.Length).IndexOf('/') < 0))
                    entries.Add(new FtpEntry(file.Substring(prefix.Length), false, _files[file].Length));
                return Task.FromResult<IReadOnlyList<FtpEntry>>(entries);
            }

            public Task<byte[]> DownloadAsync(string path, CancellationToken ct)
            {
                byte[] bytes;
                if (!_files.TryGetValue(path, out bytes)) throw NotFound();
                return Task.FromResult(bytes);
            }

            /// <summary>Wie die vorhandenen Tests ein fehlendes Ziel melden (InMemoryFtpClient): permanent 550.</summary>
            private static FtpException NotFound()
            {
                return new FtpException("Nicht vorhanden oder kein Zugriff", FtpErrorKind.Permanent, 550);
            }

            public Task DownloadToFileAsync(string path, string localPath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DownloadToFileAsync(string path, string localPath, long offset, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Stream OpenRead(string path, long offset) { throw new NotSupportedException(); }
            public Task UploadFromFileAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task AppendFromFileAsync(string localPath, long localOffset, string remotePath, IProgress<long> progress, CancellationToken ct) { throw new NotSupportedException(); }
            public Task<long> GetFileSizeAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task MakeDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task RemoveDirectoryAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
            public Task RenameAsync(string fromPath, string toPath, CancellationToken ct) { throw new NotSupportedException(); }
            public Task DeleteAsync(string path, CancellationToken ct) { throw new NotSupportedException(); }
        }
    }
}
```

**Note on the fake:** `FtpEntry` has a three-argument constructor `(name, isDirectory, size)`;
size and modified default to 0 / `DateTime.MinValue`, which is all the discovery needs.

- [ ] **Step 4: Run the discovery tests to verify they fail**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~GoldHenDiscoveryServiceTests`
Expected: FAIL — the service does not exist.

- [ ] **Step 5: Implement the models**

`src/PS4Manager.Core/GoldHen/GoldHenSection.cs`:

```csharp
namespace PS4Manager.Core.GoldHen
{
    /// <summary>Die Abschnitte des Fensters; jeder wird einzeln geprüft.</summary>
    public enum GoldHenSection { Plugins, Cheats, Patches, Configuration }
}
```

`src/PS4Manager.Core/GoldHen/GoldHenActivation.cs`:

```csharp
namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Unknown heißt: keine erkannte Konfiguration. Es wird nie ein Zustand
    /// erfunden, den die Konsole nicht hergibt.
    /// </summary>
    public enum GoldHenActivation { Unknown, Enabled, Disabled }
}
```

`src/PS4Manager.Core/GoldHen/GoldHenLocation.cs`:

```csharp
using System.Collections.Generic;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>Ein geprüfter Ort auf der Konsole. Fehlt er, ist Exists false — kein Fehler.</summary>
    public sealed class GoldHenLocation
    {
        public GoldHenLocation(GoldHenSection section, string path, bool exists, IReadOnlyList<RemoteItem> entries)
        {
            Section = section;
            Path = path;
            Exists = exists;
            Entries = entries ?? new RemoteItem[0];
        }

        public GoldHenSection Section { get; }
        public string Path { get; }
        public bool Exists { get; }
        public IReadOnlyList<RemoteItem> Entries { get; }
    }
}
```

`src/PS4Manager.Core/GoldHen/PluginEntry.cs`:

```csharp
namespace PS4Manager.Core.GoldHen
{
    /// <summary>Ein Plugin und was die Konfiguration über es sagt.</summary>
    public sealed class PluginEntry
    {
        public PluginEntry(string name, string path, GoldHenActivation activation, bool filePresent, int lineIndex)
        {
            Name = name;
            Path = path;
            Activation = activation;
            FilePresent = filePresent;
            LineIndex = lineIndex;
        }

        public string Name { get; }
        public string Path { get; }
        public GoldHenActivation Activation { get; }
        public bool FilePresent { get; }
        public int LineIndex { get; }
    }
}
```

`src/PS4Manager.Core/GoldHen/GoldHenDiscovery.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>Was auf dieser Konsole gefunden wurde — nichts davon ist geraten.</summary>
    public sealed class GoldHenDiscovery
    {
        public GoldHenDiscovery(string root, bool rootExists, IReadOnlyList<GoldHenLocation> locations,
            IReadOnlyList<string> configurationFiles, string configurationPath, IReadOnlyList<PluginEntry> plugins)
        {
            Root = root;
            RootExists = rootExists;
            Locations = locations;
            ConfigurationFiles = configurationFiles;
            ConfigurationPath = configurationPath;
            Plugins = plugins;
        }

        public string Root { get; }
        public bool RootExists { get; }
        public IReadOnlyList<GoldHenLocation> Locations { get; }
        public IReadOnlyList<string> ConfigurationFiles { get; }
        public string ConfigurationPath { get; }
        public IReadOnlyList<PluginEntry> Plugins { get; }

        public GoldHenLocation Location(GoldHenSection section)
        {
            return Locations.First(l => l.Section == section);
        }
    }
}
```

- [ ] **Step 6: Implement the parser and `TextFileContent.FromBytes`**

`src/PS4Manager.Core/GoldHen/IniPluginEditor.cs` (parse only in this task; the toggle follows in Task 2):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Liest die Plugin-Zeilen einer GoldHEN-Konfiguration und schaltet sie um.
    /// Bewusst zeilenweise und ohne Formatannahme: Eine Zeile, die auf eine
    /// vorhandene Plugin-Datei verweist, ist ein Eintrag; ein Kommentarzeichen
    /// davor heißt „aus". Alles, was der Parser nicht erklärt, bleibt liegen.
    /// </summary>
    public static class IniPluginEditor
    {
        private const string CommentMarkers = ";#";

        public static IReadOnlyList<PluginEntry> Parse(string text, IReadOnlyList<string> pluginFileNames)
        {
            var entries = new List<PluginEntry>();
            if (string.IsNullOrEmpty(text)) return entries;

            var names = new HashSet<string>(pluginFileNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var lines = Split(text);
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Content.Trim();
                if (trimmed.Length == 0) continue;

                bool commented = CommentMarkers.IndexOf(trimmed[0]) >= 0;
                var candidate = commented ? trimmed.Substring(1).Trim() : trimmed;
                if (candidate.Length == 0) continue;
                if (candidate.IndexOf('/') >= 0 || candidate.IndexOf('\\') >= 0) continue;
                if (!names.Contains(candidate)) continue;

                entries.Add(new PluginEntry(candidate, "/data/GoldHEN/plugins/" + candidate,
                    commented ? GoldHenActivation.Disabled : GoldHenActivation.Enabled, true, i));
            }
            return entries;
        }

        /// <summary>Zerlegt den Text in Zeilen samt ihrer Zeilenenden — die bleiben so erhalten, wie sie waren.</summary>
        internal static List<TextLine> Split(string text)
        {
            var lines = new List<TextLine>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                int contentEnd = i > start && text[i - 1] == '\r' ? i - 1 : i;
                lines.Add(new TextLine(text.Substring(start, contentEnd - start),
                    text.Substring(contentEnd, i + 1 - contentEnd)));
                start = i + 1;
            }
            if (start < text.Length || lines.Count == 0) lines.Add(new TextLine(text.Substring(start), ""));
            return lines;
        }

        internal static string Join(IEnumerable<TextLine> lines)
        {
            return string.Concat(lines.Select(l => l.Content + l.Terminator));
        }

        internal sealed class TextLine
        {
            public TextLine(string content, string terminator) { Content = content; Terminator = terminator; }
            public string Content { get; }
            public string Terminator { get; }
        }
    }
}
```

In `src/FileManager.Core/Editing/TextFileContent.cs`, replace `Load` with a call to the new factory so both share one decoding path:

```csharp
        public static TextFileContent Load(string path)
        {
            return FromBytes(File.ReadAllBytes(path));
        }

        /// <summary>Wie <see cref="Load"/>, aber aus Bytes im Speicher — für gelesene Konsolendateien.</summary>
        public static TextFileContent FromBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            Encoding encoding;
            int preambleLength;
            Detect(bytes, out encoding, out preambleLength);
            var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            return new TextFileContent(text, encoding, bytes.Take(preambleLength).ToArray());
        }
```

- [ ] **Step 7: Implement the discovery service**

`src/PS4Manager.Core/GoldHen/GoldHenDiscoveryService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Ftp;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Prüft die GoldHEN-Orte EINZELN und meldet, was da ist. Fehlt etwas, ist
    /// es „nicht vorhanden" — nie ein Fehler und nie ein erfundener Zustand
    /// (Muster von QuickAccessBuilder).
    /// </summary>
    public sealed class GoldHenDiscoveryService
    {
        public const string Root = "/data/GoldHEN";
        public static readonly string PluginsPath = Root + "/plugins";
        public static readonly string CheatsPath = Root + "/cheats";
        public static readonly string PatchesPath = Root + "/patches";
        public static readonly string[] ConfigurationCandidates =
        {
            Root + "/plugins.ini", Root + "/config.ini", Root + "/plugins.txt"
        };

        private readonly DirectoryCache _listings;
        private readonly IFtpClient _ftp;
        private readonly FtpGates _gates;

        public GoldHenDiscoveryService(DirectoryCache listings, IFtpClient ftp, FtpGates gates)
        {
            if (listings == null) throw new ArgumentNullException(nameof(listings));
            if (ftp == null) throw new ArgumentNullException(nameof(ftp));
            if (gates == null) throw new ArgumentNullException(nameof(gates));
            _listings = listings;
            _ftp = ftp;
            _gates = gates;
        }

        public async Task<GoldHenDiscovery> DiscoverAsync(CancellationToken ct)
        {
            var root = await TryListAsync(Root, ct).ConfigureAwait(false);
            var plugins = await ProbeAsync(GoldHenSection.Plugins, PluginsPath,
                root != null && HasDir(root, "plugins"), ct).ConfigureAwait(false);
            var cheats = await ProbeAsync(GoldHenSection.Cheats, CheatsPath,
                root != null && HasDir(root, "cheats"), ct).ConfigureAwait(false);
            var patches = await ProbeAsync(GoldHenSection.Patches, PatchesPath,
                root != null && HasDir(root, "patches"), ct).ConfigureAwait(false);

            var configurationFiles = new List<string>();
            if (root != null)
            {
                foreach (var candidate in ConfigurationCandidates)
                {
                    var name = RemotePath.Name(candidate);
                    if (root.Any(e => !e.IsDirectory && e.Name == name)) configurationFiles.Add(candidate);
                }
            }
            var configurationPath = configurationFiles.Count > 0 ? configurationFiles[0] : null;
            var configuration = new GoldHenLocation(GoldHenSection.Configuration, Root, root != null,
                root == null ? new RemoteItem[0] : ConfigurationItems(root, configurationFiles));

            var pluginEntries = configurationPath == null
                ? UnknownPlugins(plugins.Entries)
                : await ReadPluginsAsync(configurationPath, plugins.Entries, ct).ConfigureAwait(false);

            return new GoldHenDiscovery(Root, root != null,
                new[] { plugins, cheats, patches, configuration },
                configurationFiles, configurationPath, pluginEntries);
        }

        /// <summary>Liest die Konfiguration und verschmilzt sie mit den vorhandenen Plugin-Dateien.</summary>
        public async Task<IReadOnlyList<PluginEntry>> ReadPluginsAsync(string configurationPath,
            IReadOnlyList<RemoteItem> pluginFiles, CancellationToken ct)
        {
            var files = pluginFiles ?? new RemoteItem[0];
            var names = files.Select(f => RemotePath.Name(f.Path)).ToList();
            TextFileContent document;
            try
            {
                var bytes = await FtpGates.RunAsync(_gates.Browse,
                    () => _ftp.DownloadAsync(configurationPath, ct), ct).ConfigureAwait(false);
                document = TextFileContent.FromBytes(bytes);
            }
            catch
            {
                // Keine lesbare Konfiguration ist kein Fehler, sondern „unbekannt".
                return UnknownPlugins(files);
            }

            var parsed = IniPluginEditor.Parse(document.Text, names);
            var entries = new List<PluginEntry>();
            foreach (var file in files)
            {
                var name = RemotePath.Name(file.Path);
                var match = parsed.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                entries.Add(match ?? new PluginEntry(name, file.Path, GoldHenActivation.Disabled, true, -1));
            }
            return entries;
        }

        private static IReadOnlyList<PluginEntry> UnknownPlugins(IReadOnlyList<RemoteItem> pluginFiles)
        {
            return (pluginFiles ?? new RemoteItem[0])
                .Select(f => new PluginEntry(RemotePath.Name(f.Path), f.Path, GoldHenActivation.Unknown, true, -1))
                .ToList();
        }

        private static IReadOnlyList<RemoteItem> ConfigurationItems(IReadOnlyList<FtpEntry> root,
            IReadOnlyList<string> configurationFiles)
        {
            var items = new List<RemoteItem>();
            foreach (var path in configurationFiles)
            {
                var name = RemotePath.Name(path);
                var entry = root.First(e => !e.IsDirectory && e.Name == name);
                items.Add(new RemoteItem(path, false, entry.Size, entry.Modified));
            }
            return items;
        }

        private async Task<GoldHenLocation> ProbeAsync(GoldHenSection section, string path, bool exists, CancellationToken ct)
        {
            var entries = new List<RemoteItem>();
            if (exists)
            {
                var listing = await TryListAsync(path, ct).ConfigureAwait(false) ?? new FtpEntry[0];
                foreach (var entry in listing.Where(e => !e.IsDirectory))
                    entries.Add(new RemoteItem(path + "/" + entry.Name, false, entry.Size, entry.Modified));
            }
            return new GoldHenLocation(section, path, exists, entries);
        }

        /// <summary>Fehlt ein Ort, ist das null; ein Verbindungsabbruch geht durch.</summary>
        private async Task<IReadOnlyList<FtpEntry>> TryListAsync(string path, CancellationToken ct)
        {
            try
            {
                return await _listings.ListAsync(path, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (FtpException ex) when (ex.IsTransient) { throw; }
            catch (Exception) { return null; }
        }

        private static bool HasDir(IReadOnlyList<FtpEntry> entries, string name)
        {
            return entries.Any(e => e.IsDirectory && e.Name == name);
        }
    }
}
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~GoldHen`
Expected: PASS.

- [ ] **Step 9: Run the full suite and commit**

```bash
dotnet test ScreenshotGallery.sln -c Debug
git add src/PS4Manager.Core/GoldHen src/FileManager.Core/Editing/TextFileContent.cs tests/PS4Manager.Core.Tests/GoldHen
git commit -m "Discover the GoldHEN folders and plugin state"
```

---

### Task 2: Toggling a plugin in the configuration

**Files:**
- Modify: `src/PS4Manager.Core/GoldHen/IniPluginEditor.cs`
- Test: `tests/PS4Manager.Core.Tests/GoldHen/IniPluginEditorTests.cs`

**Interfaces:**
- Consumes: `IniPluginEditor.Parse`, `IniPluginEditor.Split`/`Join`/`TextLine` from Task 1.
- Produces: `string IniPluginEditor.SetEnabled(string text, PluginEntry entry, bool enabled)` — text in, text out.

- [ ] **Step 1: Write the failing toggle tests**

Append to `tests/PS4Manager.Core.Tests/GoldHen/IniPluginEditorTests.cs`:

```csharp
        [Fact]
        public void Disabling_comments_exactly_the_adressed_line()
        {
            var text = "hen.prx\ntool.prx\n";
            var entry = IniPluginEditor.Parse(text, Files).Single(e => e.Name == "tool.prx");

            var changed = IniPluginEditor.SetEnabled(text, entry, false);

            Assert.Equal("hen.prx\n; tool.prx\n", changed);
        }

        [Fact]
        public void Enabling_removes_the_marker_and_keeps_the_rest_of_the_line()
        {
            var text = "#  tool.prx   \n";
            var entry = IniPluginEditor.Parse(text, Files).Single();

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("tool.prx   \n", changed);
        }

        [Fact]
        public void Windows_line_endings_survive_the_toggle()
        {
            var text = "; hen.prx\r\ntool.prx\r\n";
            var entry = IniPluginEditor.Parse(text, Files).Single(e => e.Name == "hen.prx");

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("hen.prx\r\ntool.prx\r\n", changed);
        }

        [Fact]
        public void An_unmentioned_plugin_is_appended_with_the_files_line_ending()
        {
            var text = "; hen.prx\r\n";
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx", GoldHenActivation.Disabled, true, -1);

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("; hen.prx\r\ntool.prx\r\n", changed);
        }

        [Fact]
        public void Appending_to_a_file_without_a_trailing_newline_adds_one()
        {
            var text = "; hen.prx";
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx", GoldHenActivation.Disabled, true, -1);

            var changed = IniPluginEditor.SetEnabled(text, entry, true);

            Assert.Equal("; hen.prx\ntool.prx", changed);
        }

        [Fact]
        public void Turning_a_plugin_off_that_has_no_line_changes_nothing()
        {
            var text = "hen.prx\n";
            var entry = new PluginEntry("tool.prx", "/data/GoldHEN/plugins/tool.prx", GoldHenActivation.Disabled, true, -1);

            Assert.Equal(text, IniPluginEditor.SetEnabled(text, entry, false));
        }

        [Fact]
        public void A_document_survives_a_round_trip_through_the_text_codec()
        {
            var original = "; hen.prx\r\n; debug.prx\r\n";
            var entries = IniPluginEditor.Parse(original, Files);
            var enabled = IniPluginEditor.SetEnabled(original, entries.First(e => e.Name == "hen.prx"), true);
            var document = TextFileContent.FromBytes(new UTF8Encoding(true).GetPreamble()
                .Concat(new UTF8Encoding(false).GetBytes(enabled)).ToArray());

            Assert.Equal(enabled, document.Text);
        }
```

(This file needs `using System.Linq;`, `using System.Text;` and `using FileManager.Core.Editing;`.)

- [ ] **Step 2: Run the toggle tests to verify they fail**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~IniPluginEditorTests`
Expected: FAIL — `SetEnabled` does not exist.

- [ ] **Step 3: Implement `SetEnabled`**

Add to `IniPluginEditor`:

```csharp
        /// <summary>
        /// Schaltet GENAU eine Zeile um (oder hängt eine an) — alles andere wird
        /// unverändert zurückgegeben, Zeilenenden eingeschlossen.
        /// </summary>
        public static string SetEnabled(string text, PluginEntry entry, bool enabled)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var source = text ?? "";
            var lines = Split(source);

            if (entry.LineIndex >= 0 && entry.LineIndex < lines.Count)
            {
                var line = lines[entry.LineIndex];
                var content = line.Content;
                var body = content.TrimStart();
                bool commented = body.Length > 0 && CommentMarkers.IndexOf(body[0]) >= 0;

                if (enabled && commented)
                    content = content.Substring(0, content.Length - body.Length) + body.Substring(1).TrimStart();
                else if (!enabled && !commented)
                    content = content.Substring(0, content.Length - body.Length) + "; " + body;
                else
                    return source;                      // schon im gewünschten Zustand

                lines[entry.LineIndex] = new TextLine(content, line.Terminator);
                return Join(lines);
            }

            if (!enabled) return source;                // ohne Zeile ist „aus" schon erfüllt

            var terminator = DominantTerminator(source);
            if (source.Length == 0) return entry.Name;
            if (lines.Count > 0 && lines[lines.Count - 1].Terminator.Length == 0)
            {
                var last = lines[lines.Count - 1];
                lines[lines.Count - 1] = new TextLine(last.Content, terminator);
                return Join(lines) + entry.Name;         // Datei endete ohne Zeilenende: es bleibt so
            }
            return source + entry.Name + terminator;
        }

        private static string DominantTerminator(string text)
        {
            return text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~IniPluginEditorTests`
Expected: PASS (all seven tests).

- [ ] **Step 5: Run the full suite and commit**

```bash
dotnet test ScreenshotGallery.sln -c Debug
git add src/PS4Manager.Core/GoldHen/IniPluginEditor.cs tests/PS4Manager.Core.Tests/GoldHen/IniPluginEditorTests.cs
git commit -m "Toggle plugins by editing one configuration line"
```

---

### Task 3: Local safety backup before every change

**Files:**
- Create: `src/PS4Manager.Core/GoldHen/GoldHenBackup.cs`
- Create: `src/PS4Manager.Core/GoldHen/GoldHenBackupWriter.cs`
- Modify: `src/PS4Manager.App/AppPaths.cs` (add `GoldHenBackupRoot`)
- Test: `tests/PS4Manager.Core.Tests/GoldHen/GoldHenBackupWriterTests.cs`

**Interfaces:**
- Consumes: `FileManager.Core.Transfers.IClock`.
- Produces:
  - `GoldHenBackupWriter(string root, IClock clock)`
  - `GoldHenBackup GoldHenBackupWriter.Write(string remotePath, byte[] content, string operation)`
  - `GoldHenBackup { string Directory; string PayloadPath; string ManifestPath; string Sha256; }`
  - `AppPaths.GoldHenBackupRoot` = `%LocalAppData%\PS4Manager\backups\goldhen`

- [ ] **Step 1: Write the failing backup tests**

Create `tests/PS4Manager.Core.Tests/GoldHen/GoldHenBackupWriterTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenBackupWriterTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "goldhen-backup-" + Guid.NewGuid().ToString("N"));
        private readonly FixedClock _clock = new FixedClock(new DateTime(2026, 9, 25, 18, 30, 12, DateTimeKind.Utc));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Fact]
        public void A_backup_stores_the_payload_and_a_manifest_with_the_hash()
        {
            var bytes = Encoding.UTF8.GetBytes("; hen.prx\n");

            var backup = new GoldHenBackupWriter(_root, _clock).Write("/data/GoldHEN/plugins.ini", bytes, "toggle-plugin");

            Assert.True(File.Exists(backup.PayloadPath));
            Assert.Equal(bytes, File.ReadAllBytes(backup.PayloadPath));
            Assert.Equal(BitConverter.ToString(SHA256.Create().ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), backup.Sha256);

            var manifest = File.ReadAllText(backup.ManifestPath);
            Assert.Contains("\"version\":1", manifest);
            Assert.Contains("\"operation\":\"toggle-plugin\"", manifest);
            Assert.Contains("\"source\":\"/data/GoldHEN/plugins.ini\"", manifest);
            Assert.Contains("\"size\":" + bytes.Length, manifest);
            Assert.Contains("2026-09-25T18:30:12Z", manifest);
        }

        [Fact]
        public void The_payload_keeps_the_remote_file_name()
        {
            var backup = new GoldHenBackupWriter(_root, _clock)
                .Write("/data/GoldHEN/cheats/CUSA00411.json", new byte[] { 1 }, "replace");

            Assert.Equal("CUSA00411.json", Path.GetFileName(backup.PayloadPath));
        }

        [Fact]
        public void A_second_backup_in_the_same_second_gets_its_own_directory()
        {
            var writer = new GoldHenBackupWriter(_root, _clock);

            var first = writer.Write("/data/GoldHEN/plugins.ini", new byte[] { 1 }, "toggle-plugin");
            var second = writer.Write("/data/GoldHEN/plugins.ini", new byte[] { 2 }, "toggle-plugin");

            Assert.NotEqual(first.Directory, second.Directory);
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(first.PayloadPath));
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(second.PayloadPath));
        }

        [Fact]
        public void A_backup_root_that_cannot_be_written_throws()
        {
            var blocked = Path.Combine(_root, "blocked");
            File.WriteAllText(blocked, "eine Datei, kein Ordner");

            Assert.ThrowsAny<Exception>(() =>
                new GoldHenBackupWriter(blocked, _clock).Write("/data/GoldHEN/plugins.ini", new byte[] { 1 }, "toggle-plugin"));
        }

        private sealed class FixedClock : IClock
        {
            private readonly DateTime _now;
            public FixedClock(DateTime now) { _now = now; }
            public DateTime UtcNow { get { return _now; } }
        }
    }
}
```

**Note:** `IClock`'s member is `UtcNow` (verified in `src/FileManager.Core/Transfers/IClock.cs`).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~GoldHenBackupWriterTests`
Expected: FAIL — the writer does not exist.

- [ ] **Step 3: Implement the writer**

`src/PS4Manager.Core/GoldHen/GoldHenBackup.cs`:

```csharp
namespace PS4Manager.Core.GoldHen
{
    /// <summary>Was eine Sicherung angelegt hat — Pfade für die Anzeige, Hash für die Prüfung.</summary>
    public sealed class GoldHenBackup
    {
        public GoldHenBackup(string directory, string payloadPath, string manifestPath, string sha256)
        {
            Directory = directory;
            PayloadPath = payloadPath;
            ManifestPath = manifestPath;
            Sha256 = sha256;
        }

        public string Directory { get; }
        public string PayloadPath { get; }
        public string ManifestPath { get; }
        public string Sha256 { get; }
    }
}
```

`src/PS4Manager.Core/GoldHen/GoldHenBackupWriter.cs`:

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Sichert eine Konsolendatei LOKAL, bevor sie geändert wird. Schlägt das
    /// fehl, fliegt die Ausnahme bis zum Aufrufer — die Änderung unterbleibt
    /// dann (Spec: keine Änderung ohne vollständige Sicherung).
    /// </summary>
    public sealed class GoldHenBackupWriter
    {
        private readonly string _root;
        private readonly IClock _clock;

        public GoldHenBackupWriter(string root, IClock clock)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("A backup root is required.", nameof(root));
            _root = root;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public GoldHenBackup Write(string remotePath, byte[] content, string operation)
        {
            if (string.IsNullOrEmpty(remotePath)) throw new ArgumentException("A remote path is required.", nameof(remotePath));
            if (content == null) throw new ArgumentNullException(nameof(content));

            var stamp = _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var directory = Path.Combine(_root, stamp);
            for (int suffix = 2; Directory.Exists(directory); suffix++)
                directory = Path.Combine(_root, stamp + "-" + suffix);

            Directory.CreateDirectory(directory);
            var name = RemotePath.Name(remotePath);
            var payload = Path.Combine(directory, name);
            File.WriteAllBytes(payload, content);

            var hash = Hash(content);
            var manifest = Path.Combine(directory, "manifest.json");
            File.WriteAllText(manifest, Manifest(remotePath, operation, content.Length, hash, _clock.UtcNow), new UTF8Encoding(false));
            return new GoldHenBackup(directory, payload, manifest, hash);
        }

        private static string Hash(byte[] content)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
        }

        private static string Manifest(string source, string operation, int size, string hash, DateTime createdUtc)
        {
            var builder = new StringBuilder();
            builder.Append("{\"version\":1");
            builder.Append(",\"operation\":\"").Append(Escape(operation)).Append('"');
            builder.Append(",\"source\":\"").Append(Escape(source)).Append('"');
            builder.Append(",\"createdUtc\":\"").Append(createdUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append('"');
            builder.Append(",\"size\":").Append(size.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"sha256\":\"").Append(hash).Append('"');
            builder.Append('}');
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
```

In `src/PS4Manager.App/AppPaths.cs` add:

```csharp
        /// <summary>Sicherungen vor Änderungen (GoldHEN); die Konsole bleibt unberührt.</summary>
        public static readonly string GoldHenBackupRoot = Path.Combine(Root, "backups", "goldhen");
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~GoldHenBackupWriterTests`
Expected: PASS.

- [ ] **Step 5: Run the full suite and commit**

```bash
dotnet test ScreenshotGallery.sln -c Debug
git add src/PS4Manager.Core/GoldHen/GoldHenBackup.cs src/PS4Manager.Core/GoldHen/GoldHenBackupWriter.cs src/PS4Manager.App/AppPaths.cs tests/PS4Manager.Core.Tests/GoldHen/GoldHenBackupWriterTests.cs
git commit -m "Back up GoldHEN files locally before any change"
```

---

### Task 4: Cheats and patches grouped by title

**Files:**
- Create: `src/PS4Manager.Core/GoldHen/GoldHenTitleGroup.cs`
- Create: `src/PS4Manager.Core/GoldHen/GoldHenInventory.cs`
- Test: `tests/PS4Manager.Core.Tests/GoldHen/GoldHenInventoryTests.cs`

**Interfaces:**
- Consumes: `RemoteItem`, `Ps4.Core.Titles.TitleId.IsMatch`.
- Produces: `IReadOnlyList<GoldHenTitleGroup> GoldHenInventory.GroupAsync(IReadOnlyList<RemoteItem> files, Func<string, CancellationToken, Task<string>> resolveGameName, CancellationToken ct)`, `string GoldHenInventory.TitleIdOf(string path)` and `GoldHenTitleGroup { string TitleId; string Label; IReadOnlyList<RemoteItem> Files; }`.

- [ ] **Step 1: Write the failing inventory tests**

Create `tests/PS4Manager.Core.Tests/GoldHen/GoldHenInventoryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using Xunit;

namespace PS4Manager.Core.Tests.GoldHen
{
    public sealed class GoldHenInventoryTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task Files_are_grouped_by_their_title_id()
        {
            var files = new[] { Item("/c/CUSA00411.json"), Item("/c/CUSA00411_2.json"), Item("/c/PPSA00001.json") };

            var groups = await GoldHenInventory.GroupAsync(files, Name, None);

            Assert.Equal(2, groups.Count);
            Assert.Equal(new[] { "CUSA00411", "PPSA00001" }, groups.Select(g => g.TitleId));
            Assert.Equal(2, groups.First().Files.Count);
        }

        [Fact]
        public async Task The_label_is_the_resolved_game_name_and_falls_back_to_the_id()
        {
            var files = new[] { Item("/c/CUSA00411.json"), Item("/c/PPSA00001.json") };

            var groups = await GoldHenInventory.GroupAsync(files, Name, None);

            Assert.Equal("Grand Theft Auto V", groups.Single(g => g.TitleId == "CUSA00411").Label);
            Assert.Equal("PPSA00001", groups.Single(g => g.TitleId == "PPSA00001").Label);
        }

        [Fact]
        public async Task Files_without_a_title_id_land_in_one_group_without_an_id()
        {
            var files = new[] { Item("/c/readme.txt"), Item("/c/cheats/notes.json") };

            var groups = await GoldHenInventory.GroupAsync(files, Name, None);

            var group = Assert.Single(groups);
            Assert.Null(group.TitleId);
            Assert.Equal("Andere", group.Label);
            Assert.Equal(2, group.Files.Count);
        }

        [Fact]
        public async Task A_failing_name_lookup_does_not_lose_files()
        {
            var files = new[] { Item("/c/CUSA00411.json") };

            var groups = await GoldHenInventory.GroupAsync(files,
                (id, ct) => throw new InvalidOperationException("offline"), None);

            Assert.Equal("CUSA00411", Assert.Single(groups).Label);
        }

        private static Task<string> Name(string titleId, CancellationToken ct)
        {
            return Task.FromResult(titleId == "CUSA00411" ? "Grand Theft Auto V" : null);
        }

        private static RemoteItem Item(string path)
        {
            return new RemoteItem(path, false, 10, DateTime.MinValue);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~GoldHenInventoryTests`
Expected: FAIL — `GoldHenInventory` does not exist.

- [ ] **Step 3: Implement the grouping**

`src/PS4Manager.Core/GoldHen/GoldHenTitleGroup.cs`:

```csharp
using System.Collections.Generic;
using FileManager.Core.Transfers;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>Cheats oder Patches eines Spiels; ohne erkennbare Titel-ID ist TitleId null.</summary>
    public sealed class GoldHenTitleGroup
    {
        public GoldHenTitleGroup(string titleId, string label, IReadOnlyList<RemoteItem> files)
        {
            TitleId = titleId;
            Label = label;
            Files = files;
        }

        public string TitleId { get; }
        public string Label { get; }
        public IReadOnlyList<RemoteItem> Files { get; }
    }
}
```

`src/PS4Manager.Core/GoldHen/GoldHenInventory.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileManager.Core.Paths;
using FileManager.Core.Transfers;
using Ps4.Core.Titles;

namespace PS4Manager.Core.GoldHen
{
    /// <summary>
    /// Gruppiert Cheat- und Patch-Dateien nach der Titel-ID in ihrem Namen
    /// („CUSA00411.json" → CUSA00411) und holt den Spielnamen über den
    /// vorhandenen Titel-Katalog. Ohne Namen bleibt die ID stehen.
    /// </summary>
    public static class GoldHenInventory
    {
        public const string OtherLabel = "Andere";

        public static async Task<IReadOnlyList<GoldHenTitleGroup>> GroupAsync(IReadOnlyList<RemoteItem> files,
            Func<string, CancellationToken, Task<string>> resolveGameName, CancellationToken ct)
        {
            var byTitle = new Dictionary<string, List<RemoteItem>>(StringComparer.Ordinal);
            var others = new List<RemoteItem>();

            foreach (var file in files ?? (IReadOnlyList<RemoteItem>)new RemoteItem[0])
            {
                var titleId = TitleIdOf(file.Path);
                if (titleId == null) others.Add(file);
                else
                {
                    List<RemoteItem> list;
                    if (!byTitle.TryGetValue(titleId, out list)) byTitle[titleId] = list = new List<RemoteItem>();
                    list.Add(file);
                }
            }

            var groups = new List<GoldHenTitleGroup>();
            foreach (var pair in byTitle.OrderBy(p => p.Key, StringComparer.Ordinal))
                groups.Add(new GoldHenTitleGroup(pair.Key, await LabelAsync(pair.Key, resolveGameName, ct).ConfigureAwait(false), pair.Value));
            if (others.Count > 0) groups.Add(new GoldHenTitleGroup(null, OtherLabel, others));
            return groups;
        }

        public static string TitleIdOf(string path)
        {
            var name = RemotePath.Name(path ?? "");
            var dot = name.LastIndexOf('.');
            var stem = dot > 0 ? name.Substring(0, dot) : name;
            return TitleId.IsMatch(stem) ? stem : null;
        }

        private static async Task<string> LabelAsync(string titleId,
            Func<string, CancellationToken, Task<string>> resolveGameName, CancellationToken ct)
        {
            if (resolveGameName == null) return titleId;
            try
            {
                var name = await resolveGameName(titleId, ct).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(name) ? titleId : name;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return titleId; }
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~GoldHenInventoryTests`
Expected: PASS.

- [ ] **Step 5: Run the full suite and commit**

```bash
dotnet test ScreenshotGallery.sln -c Debug
git add src/PS4Manager.Core/GoldHen tests/PS4Manager.Core.Tests/GoldHen
git commit -m "Group GoldHEN cheats and patches by title"
```

---

### Task 5: The GoldHEN window and its dashboard tile

**Files:**
- Create: `src/PS4Manager.App/GoldHenForm.cs`
- Modify: `src/PS4Manager.App/DashboardForm.cs` (tool key, tile, `BuildTool`, `PlannedModules`)
- Modify: `src/FileManager.App/Dialogs/IniEditorDialog.cs` (`internal sealed class` → `public sealed class`)
- Modify: `src/FileManager.Core/Editing/TextFileContent.cs` (add `ToBytes(string text)`, the counterpart of `FromBytes`)
- Modify: `tests/FileManager.Core.Tests/TextFileContentTests.cs` (round-trip test for `ToBytes`)

**Interfaces:**
- Consumes: everything from Tasks 1–4; `ManagerSessionController`, `ManagerSession`, `ToolSessionBinding`, `ToolUiDispatcher`, `FileOperations`, `TransferQueue`, `EditSessionManager`, `TextFileContent`, `Dialogs.IniEditorDialog`, `MessageDialog`, `Ui.Describe`.
- Produces: `GoldHenForm(ManagerSessionController sessions)`, registered by the dashboard under the key `goldhen`; the `goldhen` entry leaves `DashboardForm.PlannedModules`.

- [ ] **Step 1: Make the integrated editor reusable**

In `src/FileManager.App/Dialogs/IniEditorDialog.cs` change the declaration to public and note why:

```csharp
    /// <summary>
    /// Der eingebaute Editor für Textdateien (Spec „integrierter Editor").
    /// Öffentlich, damit auch der GoldHEN-Manager Konfigurationen damit öffnet;
    /// das Speichern bleibt beim Aufrufer (Rückruf), also beim jeweiligen
    /// Werkzeug und damit bei der gemeinsamen Warteschlange.
    /// </summary>
    public sealed class IniEditorDialog : AppForm
```

- [ ] **Step 2: Write the window**

Create `src/PS4Manager.App/GoldHenForm.cs`. It follows `MediaGalleryForm`'s shape (tool window, `ToolSessionBinding`, `ToolUiDispatcher`, status line sized by content, transfers observed through the shared queue):

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileManager.App.Dialogs;
using FileManager.Core.Browsing;
using FileManager.Core.Editing;
using FileManager.Core.Paths;
using FileManager.Core.Presentation;
using FileManager.Core.Transfers;
using PS4Manager.Core.GoldHen;
using PS4Manager.Core.Presentation;
using PS4Manager.Core.Session;
using UIFramework.Controls;
using UIFramework.Core.Controls;
using UIFramework.Grid;

namespace PS4Manager.App
{
    /// <summary>
    /// Der GoldHEN-Manager: Er zeigt, was auf der Konsole liegt, schaltet Plugins
    /// über die gefundene Konfiguration und verwaltet Plugin-, Cheat- und
    /// Patch-Dateien. Geschrieben wird ausschließlich über die gemeinsame
    /// Warteschlange, und jeder Änderung geht eine lokale Sicherung voraus.
    /// </summary>
    internal sealed class GoldHenForm : SkinnedForm
    {
        private readonly ManagerSessionController _sessions;
        private readonly ToolUiDispatcher _ui;
        private readonly ToolSessionBinding _binding;

        private readonly SkinPanel _toolbar = new SkinPanel { Dock = DockStyle.Top };
        private readonly SkinLabel _status = new SkinLabel { Text = "Nicht verbunden." };
        private readonly SkinButton _refresh = new SkinButton { Text = "Aktualisieren", Enabled = false };
        private readonly SkinButton _upload = new SkinButton { Text = "Hochladen …", Enabled = false };
        private readonly SkinButton _replace = new SkinButton { Text = "Ersetzen …", Enabled = false };
        private readonly SkinButton _download = new SkinButton { Text = "Herunterladen …", Enabled = false };
        private readonly SkinButton _delete = new SkinButton { Text = "Löschen", Enabled = false };
        private readonly SkinButton _edit = new SkinButton { Text = "Im Editor öffnen", Enabled = false };
        private readonly SkinButton _toggle = new SkinButton { Text = "Aktivieren", Enabled = false };
        private readonly SkinSplitContainer _split = new SkinSplitContainer
        {
            Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 200, Panel2MinSize = 400
        };
        private readonly GridControl _sections = new GridControl { Dock = DockStyle.Fill };
        private readonly GridControl _content = new GridControl { Dock = DockStyle.Fill };

        private ManagerSession _session;
        private GoldHenDiscovery _discovery;
        private bool _busy;

        public GoldHenForm(ManagerSessionController sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _ui = new ToolUiDispatcher(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            InitializeUi();
            _binding = new ToolSessionBinding(_sessions, AttachSession, DetachSession);
        }

        // Ab hier die Methoden in dieser Reihenfolge; die Namen sind bindend, weil
        // Task 6 und der Review sich darauf beziehen:
        //   InitializeUi, AttachSession, DetachSession, RefreshAsync, FillSections,
        //   FillContent, ToggleAsync, WriteTextAsync, EditAsync (Editor),
        //   UploadAsync, ReplaceAsync, DownloadAsync, DeleteAsync, OnQueueChanged,
        //   SetStatus, IsCurrent, OnFormClosed, Dispose
    }
}
```

The window implements, in this order:

1. `InitializeUi()` — build the toolbar (Aktualisieren, the five file actions, the toggle, the status label), the two grids, the split container, then size the toolbar like the File Manager's status bar:

```csharp
            int chrome = _toolbar.Height - _toolbar.DisplayRectangle.Height;
            _toolbar.Height = _refresh.GetPreferredSize(Size.Empty).Height + chrome;
```

2. `AttachSession(ManagerSession session)` / `DetachSession(ManagerSession session)` — store/clear `_session`, enable or disable the buttons and refresh, and start one discovery pass; both dispatch onto the UI thread through `_ui` and re-check `IsDisposed` (the pattern of `MediaGalleryForm.AttachSessionOnUi`).
3. `RefreshAsync()` — `_discovery = await new GoldHenDiscoveryService(_session.Explorer.Cache, _session.Explorer.Ftp, _session.Explorer.Gates).DiscoverAsync(token)`, then fill the sections grid (`Plugins (12)`, `Cheats — nicht vorhanden`, …) and re-fill the content grid for the selected section. Guard every continuation with the captured session and token (`IsCurrentSession`-style check).
4. Section selection → `FillContent()` with the matching grid columns: plugins `Name | Datei | Zustand | Pfad`; cheats/patches `Spiel | Titel-ID | Datei | Größe | Geändert`; configuration `Datei | Größe | Geändert`.
5. `OnToggle()` — the guarded change:

```csharp
        private async Task ToggleAsync(PluginEntry entry)
        {
            var session = _session;
            var token = session.CancellationToken;
            var configuration = _discovery.ConfigurationPath;
            var enabled = entry.Activation != GoldHenActivation.Enabled;

            var bytes = await FtpGates.RunAsync(session.Explorer.Gates.Browse,
                () => session.Explorer.Ftp.DownloadAsync(configuration, token), token).ConfigureAwait(true);
            if (!IsCurrent(session, token)) return;

            var document = TextFileContent.FromBytes(bytes);
            var text = IniPluginEditor.SetEnabled(document.Text, entry, enabled);
            await WriteTextAsync(configuration, document, text,
                enabled ? "toggle-plugin-on" : "toggle-plugin-off", token);
        }
```

6. `WriteTextAsync(...)` — **the backup-first rule**, shared by the toggle and the editor save:

```csharp
        private async Task WriteTextAsync(string remotePath, TextFileContent document, string text,
            string operation, CancellationToken token)
        {
            var session = _session;
            try
            {
                new GoldHenBackupWriter(AppPaths.GoldHenBackupRoot, SystemClock.Instance)
                    .Write(remotePath, document.ToBytes(text), operation);
            }
            catch (Exception ex)
            {
                SetStatus("Sicherung fehlgeschlagen, Änderung unterblieben: " + Ui.Describe(ex));
                return;
            }

            // Ab hier der Weg des File Managers (MainForm.Editing.cs:145-180), nur
            // ohne Editor-Dialog: Text in die Arbeitskopie schreiben, Änderung am
            // Ziel erkennen, zurückschreiben lassen, über die Warteschlange senden.
            var localPath = session.Explorer.Editing.PrepareLocalPath(remotePath);
            document.Save(localPath, text);
            var edit = await session.Explorer.Editing.RegisterAsync(remotePath, localPath,
                document.ToBytes(text).Length, DateTime.MinValue, token).ConfigureAwait(true);
            if (!IsCurrent(session, token)) return;
            session.Explorer.Editing.MarkLocalSavePending(edit);
            if (await session.Explorer.Editing.RemoteChangedAsync(edit, token).ConfigureAwait(true))
            {
                if (!IsCurrent(session, token)) return;
                if (!MessageDialog.Ask(this, "Zurückschreiben",
                        "„" + RemotePath.Name(remotePath) + "“ hat sich auf der PS4 inzwischen geändert.\nTrotzdem mit der PC-Fassung überschreiben?",
                        "Überschreiben", "Abbrechen"))
                    return;
                session.Explorer.Editing.AcceptObservedRemoteVersion(edit);
            }

            var job = await session.Explorer.Operations.PlanWriteBackAsync(localPath, remotePath, token).ConfigureAwait(true);
            if (!IsCurrent(session, token) || job == null) return;
            session.Explorer.Editing.ProtectWriteBack(job, edit);
            var completion = session.Explorer.Editing.TrackWriteBackAsync(job, edit);
            session.Explorer.Queue.Enqueue(job);
            var state = await completion.ConfigureAwait(true);
            if (!IsCurrent(session, token)) return;
            SetStatus(state == JobState.Completed
                ? RemotePath.Name(remotePath) + " wurde auf die PS4 geschrieben."
                : job.Title + ": " + job.Summary);
        }
```

`TextFileContent` needs `ToBytes(string text)` for this (the counterpart of `Save`): add it in this task next to `FromBytes` and cover it in the existing `TextFileContentTests` in `tests/FileManager.Core.Tests/TextFileContentTests.cs` with a round-trip test (encoding + BOM preserved).

The write-back itself is the File Manager's order (see `MainForm.Editing.cs:145-180`): `TextFileContent.Save(localPath, text)` → `session.Explorer.Editing.MarkLocalSavePending(edit)` → `RemoteChangedAsync` → `AcceptObservedRemoteVersion` after asking through `MessageDialog` → `session.Explorer.Operations.PlanWriteBackAsync(localPath, remotePath, token)` → `ProtectWriteBack` + `TrackWriteBackAsync` → `Queue.Enqueue` → await the completion. The local path comes from `session.Explorer.Editing.PrepareLocalPath(remotePath)` and the `EditSession` from `session.Explorer.Editing.RegisterAsync(remotePath, localPath, size, modified, token)` where the size comes from the downloaded content.

7. `OnEdit()` — the same order, but through `IniEditorDialog`: download via `Operations.PlanOpenForEdit` + `Queue.Enqueue` + `await Queue.WhenIdle()` + check `job.State`, register the edit session, `TextFileContent.Load(localPath)`, `IniEditorDialog(RemotePath.Name(remotePath), document.Text, value => SaveEditorAsync(remotePath, document, value, token))`, where the save callback runs `WriteTextAsync` and returns `true` on success.
8. `OnUpload()` / `OnReplace()` / `OnDownload()` / `OnDelete()` — `OpenFileDialog`/`FolderBrowserDialog` exactly as in `MainForm.Operations.cs:162-200`, then the matching planner and `Queue.Enqueue`:
   - Upload: `session.Explorer.Operations.PlanUploadAsync(localPaths, <section folder>, token)`.
   - Replace: back up the selected entry's current bytes first (download + `GoldHenBackupWriter`), then copy the chosen local file into the work root under `RemotePath.Name(entry.Path)` and plan `PlanUploadAsync(new[] { stagedLocalPath }, RemotePath.Parent(entry.Path), token)`. The upload carries the stored name, so it replaces that entry — the queue's conflict prompt is the confirmation, and the earlier backup is what makes it safe.
   - Download: `PlanDownloadAsync(selected, targetFolder, token)`.
   - Delete: the bytes of each selected entry are downloaded and backed up first (the spec accepts the download cost), then `PlanDeleteAsync(selected, token)`; the delete confirmation comes from the prompt the planner already asks.
   Every planner returns a `TransferJob` or `null` (nothing to do) — enqueue only a non-null job, exactly like `MainForm.Operations.cs`'s `RunPlanAsync`.

```csharp
        private async Task DeleteAsync(IReadOnlyList<RemoteItem> selection, CancellationToken token)
        {
            var session = _session;
            foreach (var item in selection)
            {
                var bytes = await FtpGates.RunAsync(session.Explorer.Gates.Browse,
                    () => session.Explorer.Ftp.DownloadAsync(item.Path, token), token).ConfigureAwait(true);
                if (!IsCurrent(session, token)) return;
                new GoldHenBackupWriter(AppPaths.GoldHenBackupRoot, SystemClock.Instance)
                    .Write(item.Path, bytes, "delete");     // wirft → Abbruch (im Aufrufer abgefangen)
            }
            var job = await session.Explorer.Operations.PlanDeleteAsync(selection, token);
            if (job != null) session.Explorer.Queue.Enqueue(job);
        }
```

9. Transfers are observed like the dashboard does: subscribe `Queue.JobChanged` on attach, marshal through `_ui`, and show `TransferText.Summary(queue.Jobs)` in the status label; the dashboard's summary therefore already covers them.
10. `OnFormClosed` disposes `_binding` and the item menu, mirroring `MediaGalleryForm.OnFormClosed` (it must **not** touch the session).

- [ ] **Step 3: Wire the tile in the dashboard**

In `src/PS4Manager.App/DashboardForm.cs`:

1. Add the key constant next to the others: `private const string GoldHenKey = "goldhen";` (it exists — keep it) and give the tile `new ToolDefinition(GoldHenKey, "GoldHEN Manager", "Plugins and cheats", true)` — the tool needs a connection, so its `NeedsConnection` changes from `false` to `true`.
2. Delete the `GoldHenKey` entry from `PlannedModules` (Backup and Diagnostics keep theirs).
3. In `BuildTool` add a case before the planned ones:

```csharp
                case GoldHenKey:
                    return new GoldHenForm(_sessions);
```

4. Update the README sentence that lists the not-yet-built tiles (Task 6).

- [ ] **Step 4: Build and verify the module has one write path**

```bash
dotnet build ScreenshotGallery.sln -c Release
grep -rn "new FtpClient\|new ExplorerSession\|new TransferQueue\|new ZoneGuardFtpClient" src/PS4Manager.App src/PS4Manager.Core
grep -rn "GoldHenBackupRoot\|Queue.Enqueue" src/PS4Manager.App/GoldHenForm.cs
```

Expected: build 0 warnings / 0 errors; the first grep prints nothing in the two projects; the second shows every mutation path passing the backup writer or enqueuing a planned job.

- [ ] **Step 5: Run the suite and smoke the app**

```bash
dotnet test ScreenshotGallery.sln -c Debug
```

Then start `src/PS4Manager.App/bin/Release/net48/PS4Manager.exe` hidden, wait four seconds, assert it has not exited and reports `Responding`, and stop exactly that process.

- [ ] **Step 6: Commit**

```bash
git add src/PS4Manager.App src/FileManager.App/Dialogs/IniEditorDialog.cs
git commit -m "Add the GoldHEN Manager window"
```

---

### Task 6: Documentation, verification, review and probe handover

**Files:**
- Modify: `README.md`
- Modify: `docs/ps4-manager-migration.md`
- Modify: whichever file a confirmed review finding names.

**Interfaces:**
- Consumes: everything above.
- Produces: the evidence the human needs for the probe.

- [ ] **Step 1: Documentation**

`README.md`:
- In the dashboard bullet list, drop *GoldHEN Manager* from the "Noch nicht gebaut" sentence (Backup and Diagnostics stay).
- Add a `### GoldHEN Manager` subsection under `## Werkzeuge` describing: what it discovers, the line-based toggle with the honest "no recognized configuration" fallback, cheats/patches grouped by title, the file actions, and that every change is preceded by a local backup under `%LocalAppData%\PS4Manager\backups\goldhen`.

`docs/ps4-manager-migration.md`:
- Replace the sentence that still lists GoldHEN management as not built, and add the backup path to the data-paths list.

- [ ] **Step 2: Complete verification**

```bash
dotnet test ScreenshotGallery.sln -c Debug
dotnet test ScreenshotGallery.sln -c Release
dotnet build ScreenshotGallery.sln -c Release --no-restore
git diff --check
```

Expected: all tests pass (543 + the new GoldHEN tests), 0 warnings and 0 errors, no whitespace errors. Then the hidden process smoke of `PS4Manager.exe` as in Task 5.

- [ ] **Step 3: Fresh read-only review**

Dispatch a reviewer (read-only; explicitly forbid GUI automation, input simulation, screenshots and starting applications) over the whole GoldHEN diff with the spec at hand. Focus: a second write path (any write that does not go through `FileOperations` + `TransferQueue`), a mutation without a completed backup, the toggle's byte preservation, discovery swallowing real errors as "not present", and stale callbacks after session replacement. Fix every confirmed Critical, Important or Minor finding and repeat the affected verification.

- [ ] **Step 4: Hand the probe to the human**

Report as a checklist: open the GoldHEN tile with the console connected; see which sections exist; toggle a plugin and see the backup appear under `%LocalAppData%\PS4Manager\backups\goldhen`; open a configuration in the editor and save it; upload, download and delete one cheat file and watch each in the dashboard's transfer summary; disconnect and reconnect while the window is open. The visible clicks stay with the human.

- [ ] **Step 5: Commit**

```bash
git add README.md docs/ps4-manager-migration.md
git commit -m "Document the GoldHEN Manager"
```

---

## After this plan

Remaining from the spec's implementation sequence: Backup & Restore, text comparison, gallery search/filters/favourites, diagnostics, and the final data migration. The GoldHEN safety backups already write the manifest shape that the Backup module should adopt.

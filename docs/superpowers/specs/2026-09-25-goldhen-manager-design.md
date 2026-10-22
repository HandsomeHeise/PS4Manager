# GoldHEN Manager — Design

## Objective

Add the GoldHEN Manager as a third tool window of PS4 Manager: discover the GoldHEN
folders and configuration on the console, show plugins, cheats and patches, toggle
plugin activation by editing the discovered configuration, and let the user upload,
replace, download, delete and back up plugin and cheat files. Every remote write goes
through the zone guard and the shared transfer queue; every change is preceded by a
completed local safety backup.

Explicitly **not** in this module: installing PKGs, starting payloads, and assuming that
every GoldHEN version uses the same directory layout.

## Constraints

- C# 7.3 / `net48`; German comments where the app layer already speaks German.
- One `FtpClient`, one guard, one gate set and one `TransferQueue` per session — the
  window consumes the published `ManagerSession` and never constructs its own. There is
  **no second write path**: uploads, replacements, write-backs and deletes are planned by
  the existing `FileOperations` and executed by the shared queue.
- Headless logic lives in `PS4Manager.Core/GoldHen/` and is tested in
  `PS4Manager.Core.Tests/GoldHen/`; the window lives in `PS4Manager.App/GoldHenForm.cs`.
- The window is a tool window like the other two: one instance (dashboard registry key
  `goldhen`), attaches/detaches through `ToolSessionBinding`, owns no settings, and is
  closed by the dashboard's shutdown path.
- Reuse instead of new invention: `TextFileContent` (encoding and BOM), `EditSessionManager`
  (working copies, write-back protection, remote-change detection), `FileOperations`
  (`PlanUpload`/`PlanDownload`/`PlanDeleteAsync`/`PlanOpenForEdit`/`PlanWriteBackAsync`),
  `TransferQueue`, `TitleCatalog` (game names), `FolderRow`/`TitleId` parsing, and the
  dashboard-owned `IUserPrompts`/`IConflictResolver`.
- `FileManager.App.Dialogs.IniEditorDialog` becomes `public` (the same visibility change
  the other dialogs already got) and is reused as the integrated editor. Local file and
  folder pickers are plain `OpenFileDialog`/`FolderBrowserDialog`, as in the File Manager.
- Safety backups are written **locally** under `%LocalAppData%\PS4Manager\backups\goldhen`;
  nothing extra is written to the console. The core receives that root as a parameter
  (the app supplies it from `AppPaths`).
- No UIFramework change. No new project.

## Discovery

`GoldHenDiscoveryService.DiscoverAsync(...)` runs on the shared browse gate and reports
what is there, one candidate at a time:

- Root: `/data/GoldHEN`.
- Sections, each probed separately: `plugins/`, `cheats/`, `patches/`.
- Configuration candidates, in this order: `/data/GoldHEN/plugins.ini`, `config.ini`,
  `plugins.txt`. **Every** candidate that exists is listed in the Configuration section;
  the first one that exists is the *activation source*.
- A missing root, a missing folder or a missing candidate is reported as unavailable —
  never as an error, and never as an invented state.

```csharp
public enum GoldHenSection { Plugins, Cheats, Patches, Configuration }
public enum GoldHenActivation { Unknown, Enabled, Disabled }

public sealed class GoldHenLocation
{
    public GoldHenSection Section { get; }
    public string Path { get; }
    public bool Exists { get; }
    public IReadOnlyList<RemoteItem> Entries { get; }
}

public sealed class PluginEntry
{
    public string Name { get; }          // file name as written in the config
    public string Path { get; }          // /data/GoldHEN/plugins/<Name>
    public GoldHenActivation Activation { get; }
    public bool FilePresent { get; }
    public int LineIndex { get; }        // -1 when the config does not mention it
}

public sealed class GoldHenDiscovery
{
    public string Root { get; }
    public bool RootExists { get; }
    public IReadOnlyList<GoldHenLocation> Locations { get; }   // plugins, cheats, patches, configuration
    public IReadOnlyList<string> ConfigurationFiles { get; }   // candidates that exist
    public string ConfigurationPath { get; }                   // activation source, null when none
    public IReadOnlyList<PluginEntry> Plugins { get; }
}
```

## Activation: the honest rule

The module does not guess a GoldHEN version's configuration format. The rule is
line-based and reversible:

- **Parsing.** A *candidate line* is a line whose content, after stripping surrounding
  whitespace and at most one leading comment marker (`;` or `#`), names a file that
  exists in the plugins folder (case-insensitive comparison of the file name). A candidate
  line without a comment marker is `Enabled`, with one it is `Disabled`. Plugin files the
  configuration does not mention get `Activation = Disabled`, `LineIndex = -1`.
- **No configuration found** → every plugin keeps `Activation = Unknown`; the window shows
  the files, disables the toggle and says "no recognized configuration — edit it as text".
- **Toggling.** `IniPluginEditor.SetEnabled(IReadOnlyList<string> lines, PluginEntry entry, bool enabled)`
  is a pure function returning the new lines: it changes exactly one line (adding or
  removing the marker) or appends one line when the config did not mention the plugin.
  Every other line is returned byte-identical. Everything the parser cannot explain stays
  untouched and remains editable as text.

## Inventory (cheats and patches)

Files are grouped by the title ID in their name: the file name without its extension is
tested with the existing `Ps4.Core.Titles.TitleId.IsMatch` (`CUSA00411.json` → `CUSA00411`),
and the game name comes from `TitleCatalog` (fallback: the ID itself). Files without a title
ID are grouped under the folder name. Each group carries its files with size and modified
time so the window can show them in a grid.

```csharp
public sealed class GoldHenTitleGroup
{
    public string TitleId { get; }        // null for "other"
    public string Label { get; }          // resolved game name, the ID, or "Other"
    public IReadOnlyList<RemoteItem> Files { get; }
}
```

## Safety backups

```csharp
public sealed class GoldHenBackupWriter
{
    public GoldHenBackupWriter(string root, IClock clock);
    public GoldHenBackup Write(string remotePath, byte[] content, string operation);
}
```

`Write` creates `<root>\<yyyyMMdd-HHmmss>\` (with `-2`, `-3` … when that second already
exists), stores the bytes under their file name, and writes `manifest.json`:

```json
{
  "version": 1,
  "operation": "toggle-plugin",
  "source": "/data/GoldHEN/plugins.ini",
  "createdUtc": "2026-09-25T18:30:12Z",
  "size": 1234,
  "sha256": "…"
}
```

**Rule: the backup runs first, and the change is aborted when it fails.** Every mutation
— toggling, replacing, deleting, writing an edited configuration back — calls the writer
before it enqueues anything; a failing backup aborts the operation with a visible status
message. Deleting therefore downloads the file first, so the backup holds its bytes; the
cost of that download is accepted.

## Window

`GoldHenForm : SkinnedForm`, constructor `GoldHenForm(ManagerSessionController sessions)`,
registered by the dashboard under the key `goldhen` — the tile now opens this window instead
of the phase placeholder (the `goldhen` entry leaves `DashboardForm`'s `PlannedModules`;
Backup and Diagnostics keep theirs).

- **Top:** toolbar (`SkinPanel`, height from `GetPreferredSize` as with the File Manager's
  status bar) with `Aktualisieren` and the status label.
- **Left:** a `GridControl` listing the four sections with their state: `Plugins (12)`,
  `Cheats (34)`, `Patches — not present`, `Configuration (1)`. Docked in a
  `SkinSplitContainer` like the File Manager's tree.
- **Right:** the selected section's grid.
  - *Plugins:* name, file present, activation, path.
  - *Cheats* / *Patches:* game name, title ID, file, size, modified.
  - *Configuration:* the discovered configuration files (and, for the activation source, the
    parsed plugin lines).
- **Actions** (enabled per selection): `Hochladen…`, `Ersetzen…`, `Herunterladen…`,
  `Löschen`, `Im Editor öffnen`, and for plugins `Aktivieren`/`Deaktivieren`. The toggle is a
  toolbar button whose caption follows the selected entry's state — no checkbox column, so
  the grid stays with the behaviour the framework already proves.
  - *Hochladen…* copies one or more chosen local files into the **currently selected section's
    folder**; an existing name asks through the conflict dialog as always.
  - *Ersetzen…* copies exactly one chosen local file onto the **selected entry's path**; the
    target is explicit, so the conflict dialog is accepted as "replace" for that step.
  - *Herunterladen…* copies the selected entries into a chosen local folder.
  - *Löschen* removes the selected entries; the confirmation comes from the dashboard's prompts.
- **Bottom:** status line, sized to its content.
- **Editor flow** (the File Manager's order, with the backup inserted first):
  `Editing.PrepareLocalPath` → `Operations.PlanOpenForEdit` → `Queue.Enqueue` →
  `await Queue.WhenIdle()` → check `job.State` → `Editing.RegisterAsync` →
  `TextFileContent.Load` → `IniEditorDialog`. On save: **back up** →
  `TextFileContent.Save` → `Editing.MarkLocalSavePending` → `Editing.RemoteChangedAsync`
  (ask before overwriting; `AcceptObservedRemoteVersion`) → `Operations.PlanWriteBackAsync` →
  `Editing.ProtectWriteBack` + `TrackWriteBackAsync` → `Queue.Enqueue` → await the completion.
- **Toggle flow:** download the configuration (same open sequence) → `IniPluginEditor.SetEnabled`
  → **back up** → write the text back through the same write-back path, without the editor dialog.
- **Upload/Replace/Download:** `OpenFileDialog`/`FolderBrowserDialog` (as in the File Manager),
  then the matching `FileOperations` planning method and the queue. Conflicts and delete
  confirmations come from the dashboard's prompts, exactly as in the other windows.

## Error handling

- `/data/GoldHEN` missing → the window says so; every section reads "not present"; nothing throws.
- No recognizable configuration → plugins listed, `Activation = Unknown`, toggle disabled with
  the hint to use the editor.
- Backup fails → the change does not happen; the status line names the operation and the path.
- Interrupted connection → an interrupted queue job as everywhere else; uploads keep using
  `.part` files, so a half-written configuration never replaces the original.
- Zone policy: everything under `/data/GoldHEN` is in the free zone; nothing bypasses the guard.

## Tests (`PS4Manager.Core.Tests/GoldHen/`)

1. **Discovery:** root present/absent, mixed sections (one folder missing) → sections report
   `Exists` correctly and nothing throws; every existing configuration candidate is listed and
   the first is the activation source.
2. **Parser:** entries with and without a marker; comments and blank lines are not entries; a
   line naming a file that does not exist is not an entry; a plugin file without a line gets
   `Disabled`/`LineIndex = -1`.
3. **Toggle:** enabling adds the marker removal on exactly the adressed line; disabling adds a
   marker; the appended-line case for an unmentioned plugin; **every other line byte-identical**
   (encoding and BOM preserved through `TextFileContent`).
4. **Backup:** timestamped directory + manifest with SHA-256; a second backup in the same second
   gets a `-2` suffix; a failing writer (unwritable root) makes the operation abort — the test
   asserts the abort happens *before* any queue step exists.
5. **Inventory:** grouping by title ID with and without a resolved game name; files without a
   title ID land in "Other".
6. **One write path:** the planned operations are ordinary `TransferJob`s that end up in the
   shared queue; no new FTP construction anywhere in the module (asserted by the reviewer's grep
   step, not by a unit test).

## Verification

- `dotnet test ScreenshotGallery.sln -c Debug` and `-c Release` — all green (543 + the new tests).
- `dotnet build ScreenshotGallery.sln -c Release` — 0 warnings, 0 errors; `git diff --check` clean.
- Hidden process smoke of `PS4Manager.exe`.
- **Human probe with the console:** open the GoldHEN tile, see the discovered sections,
  toggle a plugin (and see the local backup appear under
  `%LocalAppData%\PS4Manager\backups\goldhen`), open a configuration in the editor and save it,
  upload/download/delete one cheat file and watch it in the dashboard's transfer summary.

## Out of scope

- PKG installation, payload execution, per-version layout assumptions.
- The Backup & Restore module (the safety backups here are deliberately simpler: one file plus
  a manifest; the Backup module's spec can adopt the format later).
- Gallery search/filters/favourites, diagnostics.

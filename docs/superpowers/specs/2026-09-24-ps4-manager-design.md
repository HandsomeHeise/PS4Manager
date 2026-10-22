# PS4 Manager: Unified Application Design

## Objective

Combine the existing PS4 File Manager and Screenshot Gallery into one Windows application named **PS4 Manager**. The application starts with a dashboard and opens each major tool in its own WinForms window. Every tool shares one PS4 connection, one protected transfer queue, one connection lifecycle, and one set of settings.

The first unified release also adds:

- GoldHEN plugin, cheat, and configuration management;
- backup and restore profiles;
- local-versus-PS4 text comparison;
- gallery search, filters, and favorites;
- connection and FTP diagnostics.

PKG installation, automatic cloud backup, and automatic three-way merging without a known common baseline are outside this release.

## User Experience

### Dashboard

`PS4Manager.exe` opens a dashboard with tiles for:

1. File Manager
2. Screenshots & Videos
3. GoldHEN Manager
4. Backup & Restore
5. Diagnostics

The dashboard owns the connection controls. It shows the selected PS4 endpoint, connection state, active and queued transfers, and the latest actionable error. It also offers shortcuts for common actions such as opening the gallery or starting a configured backup.

Each tile opens a modeless tool window in the same process. Only one instance of each tool window exists at a time; selecting an already open tool activates its existing window. Tool windows may remain open while disconnected and update when a new session is established.

Closing the dashboard closes the application. If transfers, unsaved editor changes, or pending write-backs exist, the normal consolidated leave warning appears first. Closing an individual tool window does not disconnect the console or stop unrelated transfers.

### Shared connection

The dashboard connects once. All tools consume the current `ManagerSession` and never construct their own `FtpClient`. Disconnecting cancels session-scoped reads, resolves pending editor waits, stops or preserves transfers according to the existing queue rules, and changes all open tools to a disconnected state.

Reconnect creates a fresh session and publishes it to all open tools. No object tied to the previous FTP client may be reused after that transition.

## Project Structure

The existing core libraries retain their focused responsibilities:

- `Ps4.Core`: FTP protocol, PS4 title metadata, and console-level primitives.
- `FileManager.Core`: browsing, path handling, zones, transfers, file editing, and Explorer integration.
- `ScreenshotGallery.Core`: media discovery, media models, thumbnails, and media cache behavior.

Two unified projects are added:

- `PS4Manager.Core`: the shared `ManagerSession`, connection lifecycle, dashboard state, backup manifests and profiles, GoldHEN discovery models, diagnostics results, and orchestration across the existing cores.
- `PS4Manager.App`: the only final executable, containing the dashboard and all tool windows.

During migration, the two old app projects remain buildable. They are removed from the solution only after their behavior is available through `PS4Manager.App` and the replacement has passed the full regression suite.

## ManagerSession

`ManagerSession` is the composition root for a connected console. It owns:

- the underlying `IFtpClient`;
- one `ZoneGuardFtpClient` and `ZonePolicy`;
- shared browse and transfer gates;
- directory and title caches;
- one `TransferQueue`;
- file operations and edit-session tracking;
- a media repository using the guarded FTP client;
- cancellation for all session-scoped reads;
- session health and diagnostic observations.

The existing `ExplorerSession` responsibilities move behind, or are composed by, `ManagerSession`; there must not be two independent guards, queues, or gate sets. Read-only media loading uses the shared browse budget. Gallery downloads, deletes, backup operations, restores, and GoldHEN writes use the common operation planning and transfer queue.

Session events are limited to lifecycle and state notifications: connecting, connected, disconnecting, disconnected, transfer-state changes, and actionable errors. UI windows marshal those notifications to the UI thread.

## Tool Windows

### File Manager

The current File Manager behavior remains available: tree and address navigation, details and tile views, drag-and-drop, clipboard integration, uploads, downloads, renames, deletes, quick access, protected zones, resumable transfers, the integrated INI editor, and external-editor write-back.

The window receives a shared session instead of creating or disposing a connection. Its transfer panel represents the global queue, including work started by other tools.

### Screenshots & Videos

The existing combined screenshot and video gallery, preview, video player, cache, download, clipboard, and deletion behavior is retained. The unified version adds:

- case-insensitive search over game name, title ID, and file name;
- filters for game, date range, screenshot/video type, and favorites;
- persistent favorites keyed by media kind, title ID, hash directory, and file name;
- clear empty and error states that distinguish “no matching media” from a failed PS4 read.

Media writes and downloads are represented as normal transfer jobs. Thumbnail reads may remain lightweight browse operations but must obey the shared connection budget and session cancellation.

### GoldHEN Manager

The GoldHEN Manager discovers supported plugin, cheat, and configuration locations through a dedicated discovery service. Missing optional folders appear as unavailable sections rather than connection failures.

The initial feature set includes:

- list installed plugins and their enabled state when represented in the detected GoldHEN configuration;
- enable or disable a plugin by editing the relevant configuration through the text-editing and write-back pipeline;
- open supported configuration files in the integrated editor;
- group cheats by title ID and resolved game name;
- upload, replace, download, delete, and back up plugin and cheat files;
- create an automatic backup before changing plugin activation or overwriting a discovered GoldHEN configuration.

All paths pass through the zone policy. The module does not install PKGs, start payloads, or assume that every GoldHEN version uses the same optional directory layout.

### Backup & Restore

A backup profile contains a name, one or more remote roots or files, a local destination, and inclusion or exclusion rules. Built-in templates cover saves, GoldHEN configuration, plugins, and cheats; users may also create custom profiles.

Before execution, the application lists the files that will be copied, skipped, overwritten, or unavailable. A backup is stored in a timestamped directory containing:

- a versioned JSON manifest;
- the configured console profile name and endpoint;
- source remote paths;
- relative payload paths;
- size, modified time when available, and SHA-256 for every file;
- completion state and per-file errors.

An incomplete backup remains visibly marked incomplete and is never presented as a successful restore point. Restore validates the manifest and local hashes before enqueueing writes. It shows a complete preview, applies normal zone warnings and conflicts, and never deletes extra PS4 files unless a future explicit mirror mode is designed separately.

Automatic safety backups made by GoldHEN or text-write operations use the same format and are labeled with their initiating operation.

### Text comparison

When both the local working copy and PS4 file changed, a comparison dialog displays the decoded texts side by side with changed lines highlighted. It preserves the encoding and byte-order mark information already tracked by `TextFileContent`.

The dialog offers:

- **Use local**: retain the local text and proceed through protected write-back;
- **Use PS4**: replace the working copy after an explicit unsaved-change warning;
- **Edit result**: open an editable result initialized from either side;
- **Merge** only when a stored common baseline exists, using a three-way comparison and requiring manual resolution of overlapping changes.

Binary files and text that cannot be represented safely in the original encoding do not enter the line-diff flow. They retain the existing replace/cancel conflict choices.

### Diagnostics

Diagnostics run as individually selectable checks and produce a copyable report containing no credentials or file contents. Checks include:

- TCP/FTP connection and login;
- server feature observations needed by the application;
- root and known PS4 directory reachability;
- media, GoldHEN, save, and USB path availability;
- listing and download timing;
- optional write, rename, resume, and delete checks using a unique temporary path under `/data`;
- an optional bounded transfer-speed test.

The write test is off by default, clearly labeled, passes through the zone guard and shared gates, and cleans up its temporary file in a `finally` path. Cleanup failure is reported with the exact remote path.

## Settings and Data Migration

Unified settings live under `%LocalAppData%\PS4Manager\`. They include console profiles, window state, theme, favorites, backup profiles, and module preferences. Cache data and backup data are separate from settings.

On first start, when unified settings do not yet exist:

1. Import the File Manager endpoint and UI preferences when present.
2. Otherwise import the Screenshot Gallery endpoint.
3. Import compatible gallery cache metadata and retain the legacy cache as a read-through source so large clips do not need to be downloaded again.
4. Promote legacy cache entries into the new cache lazily when they are used.
5. Leave all legacy data intact; migration is repeat-safe and records completion only after the new settings are written atomically.

Backups default to a user-selected location outside the cache. Cache cleanup must never traverse or delete a backup destination.

## Safety and Error Handling

- Every remote write uses the guarded client and the shared transfer queue.
- Restore and GoldHEN operations do not bypass protected-zone prompts.
- Target identity is checked immediately before destructive replacement.
- Automatic safety backups must complete successfully before the guarded change proceeds.
- A failure in one tool does not close the process or invalidate unrelated completed work.
- Transient transport failures leave resumable jobs interrupted; permanent per-file failures allow the remaining job steps to continue.
- Disconnect and application shutdown use the established consolidated warning for transfers and pending edits.
- Error messages identify the action and affected path while avoiding secrets and raw file contents.

## Implementation Sequence

1. Add `PS4Manager.Core`, `PS4Manager.App`, dashboard, unified settings, and the shared session lifecycle.
2. Host the existing File Manager and Gallery as session-consuming tool windows with feature parity.
3. Route gallery transfers and deletes through the common queue and remove all secondary FTP clients.
4. Implement backup profiles, manifests, preview, backup execution, validation, and restore.
5. Implement the text-comparison and conflict-resolution flow.
6. Implement GoldHEN discovery and management on top of backup and text services.
7. Add gallery search, filters, and favorites.
8. Add diagnostics and sanitized report export.
9. Migrate settings and cache behavior, update documentation and icons, then remove the old executable projects from the solution.

Each stage must leave the solution buildable and its existing tests passing. Old executables are not removed early merely to enforce the target shape.

## Verification and Acceptance

Automated coverage includes:

- shared-session creation, cancellation, reconnect, and disposal;
- enforcement of one FTP client, one guard, one gate set, and one transfer queue per session;
- tool-window activation and session replacement behavior;
- backup manifest round trips, incomplete backups, hash validation, previews, and protected restores;
- two-way and three-way text comparisons, encoding preservation, and conflict choices;
- GoldHEN discovery with present, absent, and variant optional directories;
- gallery search, filters, favorites, and unified transfer jobs;
- diagnostic read checks, opt-in write checks, cleanup failures, and report redaction;
- settings migration and repeat safety;
- disconnect and shutdown while transfers, reads, or editor write-backs are active.

Acceptance requires:

- one final `PS4Manager.exe` and no separate shipping File Manager or Gallery executable;
- feature parity with both current applications;
- no tool-owned FTP connection or unguarded remote write;
- one visible global transfer queue;
- successful Debug and Release test suites;
- a Release build with no warnings or errors;
- startup smoke tests for the dashboard and every tool window;
- a manual real-console pass for connection, browsing, gallery playback, a resumable transfer, a safety backup and restore, a GoldHEN configuration change, and diagnostics cleanup.

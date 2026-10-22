# Single Application Cleanup — Design

## Objective

After this change `PS4Manager.exe` is the only startable application in the
repository. The File Manager and the Screenshots & Videos gallery stay as
in-process tool windows; their former executable projects become plain
libraries, and the standalone-only code paths disappear.

Why now: the repository is meant to be published. A release folder of
`PS4Manager.exe` currently carries two further executables
(`PS4FileManager.exe`, `PS4ScreenshotGallery.exe`) that exist only because the
tool UIs live in WinExe projects, and both windows still carry a second code
path for standalone operation that nothing uses any more. Removing both keeps
every later plan (GoldHEN Manager first) building on one entry point.

## Constraints

- **No behavioural change** for the unified shell: dashboard, one shared
  session, one transfer queue, tool windows, consolidated leave warnings and the
  first-run migration behave exactly as probed on 2026-09-25.
- `net48`, C# 7.3, German comments in the app layer, existing house patterns.
- **The legacy settings import keeps working.** `LegacySettingsImporter` reads
  `%LocalAppData%\PS4FileManager\settings.json` and
  `%LocalAppData%\ScreenshotGallery\settings.json` through the path constants in
  `src/PS4Manager.App/AppPaths.cs`; both files stay untouched.
- UIFramework is not modified (published at `1330a53`).
- Project names, folders, assembly names and namespaces stay as they are
  (renaming to `*.Ui` was explicitly declined).

## Target Structure

```
src/PS4Manager.App        PS4Manager.exe              dashboard, tool windows, dialogs, prompts
src/PS4Manager.Core       PS4Manager.Core.dll        shared session, settings, migration, media planning
src/FileManager.Core      FileManager.Core.dll       file manager core
src/ScreenshotGallery.Core ScreenshotGallery.Core.dll gallery core
src/Ps4.Core              Ps4.Core.dll               FTP and title metadata
src/FileManager.App       PS4FileManager.dll         File Manager window classes only (Library)
src/ScreenshotGallery.App PS4ScreenshotGallery.dll   Gallery window classes only (Library)
```

The two former app projects keep their names and folders but switch to
`OutputType=Library`, so their assemblies appear as `PS4FileManager.dll` and
`PS4ScreenshotGallery.dll` next to `PS4Manager.exe`. `Program.cs`,
`app.manifest`, the application icon and the icon content item are removed from
both; the icon under `assets/` stays (it is the dashboard's icon until a unified
one is supplied).

## What is removed

### `src/FileManager.App`

| Removed | Reason |
|---|---|
| `Program.cs`, `app.manifest`, `icon_file_manager.ico` (+ project settings) | no standalone entry point |
| `AppPaths.cs` | paths of the standalone settings/work root; the dashboard owns `%LocalAppData%\PS4Manager` |
| `MainForm.cs`: parameterless constructor, `_legacySettings`, `_ownedSessions`, `FromLegacySettings`, `TrySaveSettings`, `_sessionEndForCloseStarted`, `_closingAfterSessionEnd`, `OnFormClosing`, `EndSessionAndCloseAsync`, `EndOwnedSessionAsync` | the dashboard owns session lifetime, leave check and settings persistence |
| `MainForm.Connection.cs`: `_connectCts`, `ConnectAsync`, `DisconnectAsync`, `EditConnection`, `ConfirmLeave`, `LegacySessionFactory` | connection is requested, never owned; the ribbon's “Connect”/“Select console …” only raise `ConnectionRequested` |
| `MainForm.Transfers.cs`: the `SessionEndingAsync` handler and the transfer leave-warning collector | cancelling jobs belongs to the dashboard; the queue warning is global |
| `MainForm.cs`: the `SessionEndingAsync` event | unused after the above (would otherwise warn CS0067) |

Kept: attach/detach against the shared session, `CollectLeaveWarnings`, the
transfer panel, browsing, titles, operations, menus, clipboard, editing, the
dialogs and helpers the dashboard reuses (`AppForm`, `Ui`, `MessageDialog`,
`DialogPrompts`, `DialogConflictResolver`), and `OnFormClosed` (it stores column
widths/tree width into the shared settings object and disposes the binding).

### `src/ScreenshotGallery.App`

| Removed | Reason |
|---|---|
| `Program.cs` | no standalone entry point (the project has neither manifest nor icon) |
| `MainForm.cs`: parameterless constructor, `LegacySessionFactory`, `GalleryPrompts`, `ReplaceConflictResolver`, `ConnectStandaloneAsync`, `_ownedSessions`, `LegacyDataRoot`, `SettingsPath`, the IP/port/connect toolbar branch and the `standalone` parameter of `InitializeUi` | connection, prompts and conflicts come from the shared session; only the dashboard touches settings |
| the `_settings` field, its assignments and the unified constructor's `ManagerSettings` parameter (`MediaGalleryForm(ManagerSessionController)`) | after the standalone path is gone the gallery reads no settings at all; the parameter would be pure ceremony. The File Manager keeps its settings parameter because it really owns view mode, column widths, pinned folders and last path. `DashboardForm.CreateTool` follows the new signature. |
| `_ip`, `_port`, `_connect` and the toolbar branch that only fed them | dead state (the WPF player stays untouched) |

Kept: gallery, preview window, video player, thumbnail/video caches under
`%LocalAppData%\PS4Manager\cache\gallery`, media transfer planning through the
shared queue, and the session-replacement guards.

### Repository and documentation

- `.gitignore`: drop the `docs/superpowers/` entry; **all** plans and specs
  become tracked (`git add docs/superpowers`).
- `src/ScreenshotGallery.Core/Config/AppSettings.cs`: default `Ip` becomes `""`
  (only the migration import reads this type afterwards; `FileManagerSettings`
  and `ManagerSettings` already default to empty).
- `README.md`: one application plus libraries, drop the “Einzel-App” sections,
  list the real output folder, name the legacy paths as migration sources only.
- `docs/ps4-manager-migration.md`: wording for “the single apps no longer exist,
  their files remain import sources”.

## Data Flow (unchanged, restated)

Dashboard → `ManagerSessionFactory` → one `FtpClient` → `ExplorerSession`
(guard, gates, queue, editing) → `ManagerSession` → published by
`ManagerSessionController` → both tool windows attach through
`ToolSessionBinding`. Prompts and conflict dialogs are created by the dashboard
(`ManagerPrompts`) and resolved against the active window. Nothing in this
design changes that graph; it only removes the parallel standalone graph.

## Error Handling

Unchanged: a failed connect keeps the previous session and shows the error in
the dashboard's state line; a failed first-run import falls back to defaults and
stays repeatable as documented; closing the dashboard cancels running jobs and
waits for the queue before disposing the session.

## Verification

1. `dotnet test ScreenshotGallery.sln -c Debug` and `-c Release` — 543/543.
2. `dotnet build ScreenshotGallery.sln -c Release` — 0 warnings, 0 errors.
3. `git diff --check` — clean.
4. **Folder check:** after a clean Release build, the folder
   `src/PS4Manager.App/bin/Release/net48/` contains `PS4Manager.exe` plus
   assemblies only — in particular **no** `PS4FileManager.exe` and **no**
   `PS4ScreenshotGallery.exe`.
5. Hidden process smoke of `PS4Manager.exe` (starts, stays responsive, exact
   process stopped).
6. `grep` for stale references: `PS4FileManager.exe`, `PS4ScreenshotGallery.exe`,
   “Einzel-App”, `standalone`, `_ownedSessions` in `src/` and `README.md`/`docs/`.
7. **Human probe:** start `PS4Manager.exe`, connect, open both tool windows,
   one transfer, close — plus the status bar fix from `8aab862` (bar no longer
   clipped).

## Out of Scope

- Renaming projects or assemblies to `*.Ui` (declined).
- The GoldHEN Manager, Backup & Restore, text comparison, gallery search and
  diagnostics (each gets its own spec and plan; GoldHEN Manager is next).
- Creating a remote for this repository, a licence decision and pushing it — the
  UIFramework push was done separately on 2026-09-25.
- A unified application icon.

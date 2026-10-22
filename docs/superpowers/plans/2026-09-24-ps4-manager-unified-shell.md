# PS4 Manager Unified Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Ausführung (2026-09-25):** Tasks 1–5 hat ein Codex-SDD-Lauf im Worktree erledigt (Branch `codex/ps4-manager-unified`, `a8ffeb3..f1a8ce5`, Ledger `.superpowers/sdd/2026-09-24-ps4-manager-unified-shell/progress.md`); Task 6 und 7 hat Claude Code im Hauptordner nachgeholt (`file-manager-app`, `superpowers:executing-plans`, ohne Subagenten). Alle Schritte unten sind abgehakt. Die sichtbare Fahrprobe am echten Desktop macht der Mensch; die drei Befunde des Phasen-Reviews sind im Ledger und im Doku-Commit festgehalten.

**Goal:** Deliver a working `PS4Manager.exe` with a dashboard, one shared PS4 session, one global transfer queue, and the existing File Manager and Screenshot/Video Gallery opened as tool windows.

**Architecture:** Add `PS4Manager.Core` as the composition and lifecycle layer over the existing core libraries, then add `PS4Manager.App` as the sole new entry point. Refactor both existing forms to consume a published `ManagerSession`; keep their standalone launchers buildable until final cleanup. Route gallery file transfers through the existing protected `FileOperations` and `TransferQueue` so the unified shell has no second write path.

**Tech Stack:** C# 7.3, .NET Framework 4.8, WinForms, UIFramework sibling projects, `DataContractJsonSerializer`, xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-24-ps4-manager-design.md`

## Global Constraints

- Target `net48` with `LangVersion` `7.3`; do not introduce syntax from newer C# versions.
- Keep `Ps4.Core`, `FileManager.Core`, and `ScreenshotGallery.Core` focused and independently testable.
- Exactly one `IFtpClient`, `ZoneGuardFtpClient`, `FtpGates`, and `TransferQueue` may exist in a connected `ManagerSession`.
- Every remote write must pass through the zone guard and common transfer queue.
- Existing standalone executables stay buildable until the unified executable reaches feature parity.
- Preserve current editor, resume, conflict, cache, preview, video-player, drag/drop, and clipboard behavior.
- Do not add PKG installation, cloud services, or automatic merging in this plan.
- Use `apply_patch` for repository edits and keep unrelated working-tree changes intact.

## Review Focus

- A connection attempt completing after a newer attempt or disconnect must be disposed and never published; Task 3 adds the stale-attempt test.
- A tool window open during disconnect/reconnect must detach the old session before accepting the new one; Tasks 4 and 5 add session-replacement tests.
- Repeated dashboard clicks must activate one existing window instead of creating duplicates; Task 6 adds the registry test.
- Gallery deletion includes media, optional `.dat`, and thumbnail paths but must not turn missing companions into a failed primary deletion; Task 5 adds the queue-plan test.
- Corrupt unified settings or an interrupted first-run migration must fall back safely and remain repeatable; Task 1 adds corrupt-file and repeat-migration tests.

---

### Task 1: Unified settings and repeat-safe import

**Files:**
- Create: `src/PS4Manager.Core/PS4Manager.Core.csproj`
- Create: `src/PS4Manager.Core/Config/ManagerSettings.cs`
- Create: `src/PS4Manager.Core/Config/ManagerSettingsStore.cs`
- Create: `src/PS4Manager.Core/Config/LegacySettingsImporter.cs`
- Create: `tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj`
- Create: `tests/PS4Manager.Core.Tests/Config/ManagerSettingsStoreTests.cs`
- Create: `tests/PS4Manager.Core.Tests/Config/LegacySettingsImporterTests.cs`
- Modify: `ScreenshotGallery.sln`

**Interfaces:**
- Produces: `ManagerSettingsStore.Load(string)`, `ManagerSettingsStore.Save(string, ManagerSettings)`, and `LegacySettingsImporter.ImportIfNeeded(string unifiedPath, string fileManagerPath, string galleryPath)`.
- Produces: `ManagerSettings.Ip`, `Port`, `ExpertMode`, `Theme`, `MigrationCompleted`, and nested file-manager view settings needed by Task 4.

- [x] **Step 1: Add the core and test projects to the solution**

Create SDK-style projects targeting inherited `net48`. `PS4Manager.Core` references all three existing core projects. Its test project references `PS4Manager.Core` and uses the same xUnit package versions as `FileManager.Core.Tests`.

Run:

```powershell
dotnet sln ScreenshotGallery.sln add src/PS4Manager.Core/PS4Manager.Core.csproj
dotnet sln ScreenshotGallery.sln add tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj
```

Expected: both projects appear under the existing `src` and `tests` solution folders after solution nesting is adjusted.

- [x] **Step 2: Write failing settings-store tests**

Add tests equivalent to:

```csharp
[Fact]
public void Corrupt_settings_fall_back_to_repaired_defaults()
{
    var path = _temp.File("settings.json", "not json");
    var settings = ManagerSettingsStore.Load(path);
    Assert.Equal(2121, settings.Port);
    Assert.NotNull(settings.FileManager.ColumnWidths);
}

[Fact]
public void Save_replaces_the_file_atomically_and_round_trips()
{
    var path = _temp.PathOf("settings.json");
    ManagerSettingsStore.Save(path, new ManagerSettings { Ip = "192.168.0.20", Port = 2121 });
    Assert.Equal("192.168.0.20", ManagerSettingsStore.Load(path).Ip);
    Assert.False(File.Exists(path + ".tmp"));
}
```

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~ManagerSettingsStoreTests`

Expected: FAIL because the settings types do not exist.

- [x] **Step 3: Implement the settings model and atomic store**

Use `[DataContract]` and `[DataMember]`, initialize defaults in both the constructor and an `[OnDeserializing]` callback, and repair null collections and invalid ports after deserialization. Save through `settings.json.tmp`, then `File.Replace` or `File.Move`, matching the proven File Manager store behavior.

- [x] **Step 4: Write failing migration tests**

Cover these exact decisions:

```csharp
[Fact]
public void File_manager_endpoint_wins_when_both_legacy_settings_exist()
{
    FileManagerSettingsStore.Save(_fileManagerPath,
        new FileManagerSettings { Ip = "10.0.0.10", Port = 2121 });
    SettingsStore.Save(_galleryPath, new AppSettings { Ip = "10.0.0.20", Port = 2122 });
    var imported = LegacySettingsImporter.ImportIfNeeded(_unifiedPath, _fileManagerPath, _galleryPath);
    Assert.Equal("10.0.0.10", imported.Ip);
    Assert.Equal(2121, imported.Port);
}

[Fact]
public void Gallery_endpoint_is_used_when_file_manager_settings_are_absent()
{
    SettingsStore.Save(_galleryPath, new AppSettings { Ip = "10.0.0.20", Port = 2122 });
    var imported = LegacySettingsImporter.ImportIfNeeded(_unifiedPath, _fileManagerPath, _galleryPath);
    Assert.Equal("10.0.0.20", imported.Ip);
    Assert.Equal(2122, imported.Port);
}

[Fact]
public void Repeating_a_completed_import_does_not_overwrite_unified_changes()
{
    FileManagerSettingsStore.Save(_fileManagerPath,
        new FileManagerSettings { Ip = "10.0.0.10", Port = 2121 });
    var first = LegacySettingsImporter.ImportIfNeeded(_unifiedPath, _fileManagerPath, _galleryPath);
    first.Ip = "10.0.0.99";
    ManagerSettingsStore.Save(_unifiedPath, first);
    var repeated = LegacySettingsImporter.ImportIfNeeded(_unifiedPath, _fileManagerPath, _galleryPath);
    Assert.Equal("10.0.0.99", repeated.Ip);
}

[Fact]
public void A_failed_write_does_not_mark_migration_complete()
{
    Directory.CreateDirectory(_unifiedPath); // a directory cannot be replaced by the JSON file
    Assert.ThrowsAny<Exception>(() => LegacySettingsImporter.ImportIfNeeded(
        _unifiedPath, _fileManagerPath, _galleryPath));
    Directory.Delete(_unifiedPath);
    var retry = LegacySettingsImporter.ImportIfNeeded(_unifiedPath, _fileManagerPath, _galleryPath);
    Assert.True(retry.MigrationCompleted);
}
```

Use real temporary JSON files written by the existing settings stores instead of hand-authored JSON.

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~LegacySettingsImporterTests`

Expected: FAIL because `LegacySettingsImporter` does not exist.

- [x] **Step 5: Implement deterministic legacy import**

Implement the precedence from the spec: load existing unified settings if present; otherwise prefer File Manager IP/port and UI settings, fall back to Gallery IP/port, set `MigrationCompleted` only in the object written successfully by `ManagerSettingsStore.Save`, and leave both legacy files unchanged.

- [x] **Step 6: Run tests and commit**

Run:

```powershell
dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj
dotnet test ScreenshotGallery.sln -c Debug
```

Expected: all tests pass.

Commit:

```powershell
git add ScreenshotGallery.sln src/PS4Manager.Core tests/PS4Manager.Core.Tests
git commit -m "Add unified PS4 Manager settings"
```

### Task 2: Shared ManagerSession composition

**Files:**
- Create: `src/PS4Manager.Core/Session/ManagerEndpoint.cs`
- Create: `src/PS4Manager.Core/Session/ManagerSession.cs`
- Create: `src/PS4Manager.Core/Session/IManagerSessionFactory.cs`
- Modify: `src/FileManager.Core/Presentation/ExplorerSession.cs`
- Test: `tests/PS4Manager.Core.Tests/Session/ManagerSessionTests.cs`

**Interfaces:**
- Consumes: `ExplorerSession`, `MediaRepository`, and Task 1 settings.
- Produces: `ManagerSession.Endpoint`, `Explorer`, `Media`, `CancellationToken`, `Cancel()`, and `Dispose()`.
- Produces: `IManagerSessionFactory.Create(ManagerEndpoint endpoint, bool expertMode)`.

- [x] **Step 1: Write failing composition tests**

Add tests using the existing in-memory FTP fake, promoted to a test-support project reference or copied as a focused fake:

```csharp
[Fact]
public void Explorer_and_media_share_the_same_guarded_client()
{
    using (var session = _factory.Create(Endpoint, false))
    {
        Assert.Same(session.Explorer.Ftp, session.MediaClient);
        Assert.Same(session.Explorer.Gates, session.Gates);
        Assert.Same(session.Explorer.Queue, session.Queue);
    }
}

[Fact]
public void Cancel_cancels_the_session_token_once_and_dispose_is_idempotent()
{
    var session = _factory.Create(Endpoint, false);
    session.Cancel();
    Assert.True(session.CancellationToken.IsCancellationRequested);
    session.Dispose();
    session.Dispose();
    Assert.True(session.IsDisposed);
}
```

`MediaClient` is an internal test-visible property exposing the exact `IFtpClient` passed to `MediaRepository`; it must reference `Explorer.Ftp`. `IsDisposed` is an internal test-visible state flag set during the first disposal.

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~ManagerSessionTests`

Expected: FAIL because `ManagerSession` does not exist.

- [x] **Step 2: Add a constructor seam to ExplorerSession**

Keep the public constructor behavior, and factor object creation so `ManagerSession` can verify and reuse the same graph without duplicating guards:

```csharp
public sealed class ExplorerSession : IDisposable
{
    public ZoneGuardFtpClient Ftp { get; }
    public FtpGates Gates { get; }
    public TransferQueue Queue { get; }
    // Existing services remain properties of this graph.
}
```

Do not add an overload that accepts a second queue or gates object.

- [x] **Step 3: Implement ManagerEndpoint and ManagerSession**

Validate host text and ports `1..65535`. `ManagerSession` constructs `MediaRepository` with `Explorer.Ftp`, exposes `Explorer.Queue` as `Queue`, owns one `CancellationTokenSource`, calls `Cancel()` before disposing `Explorer`, and tolerates repeated disposal.

- [x] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~ManagerSessionTests
dotnet test tests/FileManager.Core.Tests/FileManager.Core.Tests.csproj
dotnet test tests/ScreenshotGallery.Core.Tests/ScreenshotGallery.Core.Tests.csproj
```

Expected: all tests pass.

Commit:

```powershell
git add src/PS4Manager.Core/Session src/FileManager.Core/Presentation/ExplorerSession.cs tests/PS4Manager.Core.Tests/Session
git commit -m "Compose one shared PS4 manager session"
```

### Task 3: Race-safe connection controller

**Files:**
- Create: `src/PS4Manager.Core/Session/ConnectionState.cs`
- Create: `src/PS4Manager.Core/Session/ManagerSessionChangedEventArgs.cs`
- Create: `src/PS4Manager.Core/Session/ManagerSessionController.cs`
- Test: `tests/PS4Manager.Core.Tests/Session/ManagerSessionControllerTests.cs`

**Interfaces:**
- Consumes: `IManagerSessionFactory.Create(...)` and `ManagerSession.Explorer.CheckAsync(...)`.
- Produces: `Current`, `State`, `ConnectAsync(ManagerEndpoint, bool, CancellationToken)`, `DisconnectAsync()`, and `SessionChanged`.

- [x] **Step 1: Write lifecycle and stale-attempt tests**

Use controllable factories and sessions:

```csharp
[Fact]
public async Task A_stale_connection_completion_is_disposed_and_not_published()
{
    var first = _factory.BlockNextCheck();
    var firstConnect = _controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
    await _controller.ConnectAsync(SecondEndpoint, false, CancellationToken.None);
    first.Release();
    await firstConnect;
    Assert.Equal(SecondEndpoint, _controller.Current.Endpoint);
    Assert.True(first.Session.IsDisposed);
}

[Fact]
public async Task Disconnect_publishes_null_after_cancelling_the_old_session()
{
    await _controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
    var old = _controller.Current;
    await _controller.DisconnectAsync();
    Assert.Null(_controller.Current);
    Assert.True(old.CancellationToken.IsCancellationRequested);
    Assert.Equal(ConnectionState.Disconnected, _controller.State);
}

[Fact]
public async Task Failed_connect_keeps_the_previous_connected_session()
{
    await _controller.ConnectAsync(FirstEndpoint, false, CancellationToken.None);
    var old = _controller.Current;
    _factory.FailNextCheck(new IOException("offline"));
    await Assert.ThrowsAsync<IOException>(() =>
        _controller.ConnectAsync(SecondEndpoint, false, CancellationToken.None));
    Assert.Same(old, _controller.Current);
}
```

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~ManagerSessionControllerTests`

Expected: FAIL because the controller does not exist.

- [x] **Step 2: Implement serialized publication with attempt generations**

Increment a generation for every connect or disconnect. Build and check a candidate session without publishing it. Under the controller lock, publish only when its generation is still current. Dispose stale or failed candidates. Keep the previous healthy session until a replacement passes `CheckAsync`; then publish the new session and dispose the old one.

- [x] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~ManagerSessionControllerTests
dotnet test ScreenshotGallery.sln -c Debug
```

Expected: all tests pass.

Commit:

```powershell
git add src/PS4Manager.Core/Session tests/PS4Manager.Core.Tests/Session
git commit -m "Add shared connection lifecycle"
```

### Task 4: Convert the File Manager into a session-consuming tool window

**Files:**
- Modify: `src/FileManager.App/AppForm.cs`
- Modify: `src/FileManager.App/MainForm.cs`
- Modify: `src/FileManager.App/MainForm.Connection.cs`
- Modify: `src/FileManager.App/MainForm.Transfers.cs`
- Modify: `src/FileManager.App/MainForm.Editing.cs`
- Modify: `src/FileManager.App/Program.cs`
- Modify: `src/FileManager.App/FileManager.App.csproj`
- Create: `tests/PS4Manager.Core.Tests/Tools/ToolSessionBindingTests.cs`

**Interfaces:**
- Consumes: `ManagerSessionController.Current` and `SessionChanged` from Task 3.
- Produces: public `FileManagerForm(ManagerSessionController sessions, ManagerSettings settings)` and internal standalone bootstrap preserving `PS4FileManager.exe`.

- [x] **Step 1: Add a headless binding test before changing the form**

Extract a small `ToolSessionBinding` in `PS4Manager.Core` rather than unit-testing WinForms controls:

```csharp
[Fact]
public void Reconnect_detaches_old_before_attaching_new()
{
    var calls = new List<string>();
    using (var binding = new ToolSessionBinding(_controller,
        s => calls.Add("attach:" + s.Endpoint.Host),
        s => calls.Add("detach:" + s.Endpoint.Host)))
    {
        _controller.PublishForTest(_first);
        _controller.PublishForTest(_second);
    }
    Assert.Equal(new[] { "attach:first", "detach:first", "attach:second", "detach:second" }, calls);
}
```

Expose no production `PublishForTest`; drive the controller through its fake factory and `ConnectAsync` in the actual test.

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~ToolSessionBindingTests`

Expected: FAIL because the binding does not exist.

- [x] **Step 2: Implement ToolSessionBinding**

Create `src/PS4Manager.Core/Session/ToolSessionBinding.cs`. On construction, subscribe to `SessionChanged` and attach the current session if present. For replacement, call detach for the old session before attach for the new session. On disposal, unsubscribe and detach once.

- [x] **Step 3: Rename and expose the File Manager form**

Rename the partial class from `MainForm` to `FileManagerForm` across all `MainForm*.cs` declarations while retaining the file names for a reviewable diff. Make `AppForm` and `FileManagerForm` public where required by `PS4Manager.App`.

Add the unified constructor:

```csharp
public FileManagerForm(ManagerSessionController sessions, ManagerSettings settings)
{
    InitializeUi(settings.FileManager);
    _sessionBinding = new ToolSessionBinding(sessions, AttachSession, DetachSession);
}
```

Factor the existing constructor body into `InitializeUi`. The standalone parameterless constructor creates a private controller through a legacy bootstrap and retains the existing connection ribbon behavior.

- [x] **Step 4: Move connection ownership out of unified mode**

In unified mode, `Connect` and `Select console` raise `ConnectionRequested` for the dashboard and never construct `FtpClient`. `AttachSession` wires browsing, title, transfer, and editing events against `session.Explorer`. `DetachSession` executes the existing session-ending checks and UI cleanup without disposing the shared `ManagerSession`.

Guard every asynchronous continuation with both the captured session and its cancellation token so results from a detached session cannot update the form.

- [x] **Step 5: Verify standalone and unified compilation**

Update `Program.cs` to instantiate `FileManagerForm`. Add the `PS4Manager.Core` project reference.

Run:

```powershell
dotnet test ScreenshotGallery.sln -c Debug
dotnet build src/FileManager.App/FileManager.App.csproj -c Release
```

Expected: all tests pass and `PS4FileManager.exe` still builds.

- [x] **Step 6: Commit**

```powershell
git add src/FileManager.App src/PS4Manager.Core/Session/ToolSessionBinding.cs tests/PS4Manager.Core.Tests/Tools
git commit -m "Host the file manager on a shared session"
```

### Task 5: Convert the Gallery and route its operations through the global queue

**Files:**
- Modify: `src/ScreenshotGallery.App/MainForm.cs`
- Modify: `src/ScreenshotGallery.App/Program.cs`
- Modify: `src/ScreenshotGallery.App/ScreenshotGallery.App.csproj`
- Modify: `src/ScreenshotGallery.Core/Ps4/MediaRepository.cs`
- Create: `src/PS4Manager.Core/Media/MediaTransferPlanner.cs`
- Test: `tests/PS4Manager.Core.Tests/Media/MediaTransferPlannerTests.cs`
- Test: `tests/PS4Manager.Core.Tests/Tools/MediaToolSessionTests.cs`

**Interfaces:**
- Consumes: `ManagerSession.Media`, `Explorer.Operations`, `Explorer.Queue`, `ToolSessionBinding`, and unified cache paths.
- Produces: public `MediaGalleryForm(ManagerSessionController sessions, ManagerSettings settings)`.
- Produces: `MediaTransferPlanner.PlanDownload(...)` and `PlanDeleteAsync(...)` returning normal `TransferJob` instances.

- [x] **Step 1: Write failing media transfer-plan tests**

Map a `MediaItem` to its remote paths through `Ps4Paths` and assert:

```csharp
[Fact]
public async Task Delete_plans_primary_media_dat_and_thumbnail_in_safe_order()
{
    var job = await _planner.PlanDeleteAsync(new[] { Video }, CancellationToken.None);
    Assert.Equal(new[]
    {
        Ps4Paths.MediaPath(Video),
        Ps4Paths.DatPath(Video),
        Ps4Paths.ThumbnailPath(Video)
    }, job.Steps.Select(s => s.RemotePath));
}

[Fact]
public void Downloads_are_transfer_jobs_visible_in_the_shared_queue()
{
    var job = _planner.PlanDownload(new[] { Photo }, _temp.Path);
    _session.Queue.Enqueue(job);
    Assert.Contains(job, _session.Queue.Jobs);
    Assert.Equal(StepKind.DownloadFile, Assert.Single(job.Steps).Kind);
}
```

Represent optional companion deletion with a `MissingIsSuccess` flag on `TransferStep`; add a queue regression test proving FTP 550 is ignored only for those companion steps, never for the primary media file.

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~MediaTransferPlannerTests`

Expected: FAIL because the planner and optional-delete behavior do not exist.

- [x] **Step 2: Implement media transfer planning and optional companion deletion**

Use `FileOperations.PlanDownloadAsync` for downloads after converting media to `RemoteItem`. For deletion, run the same zone approval path, then create primary, `.dat`, and thumbnail delete steps. Extend `TransferQueue` so permanent not-found errors become `Skipped` only when `MissingIsSuccess` is true.

- [x] **Step 3: Refactor the Gallery form around ToolSessionBinding**

Rename `MainForm` to public `MediaGalleryForm`. Its unified constructor receives the controller and settings, removes the IP/port controls, and uses `session.Media` plus `session.CancellationToken`. On detach, cancel thumbnail and media loads, clear repository references, and retain only cache-backed UI state that is safe across sessions.

Downloads and deletes enqueue the planner's jobs and observe their terminal states. Preview and video cache behavior remain unchanged. The standalone constructor retains the old toolbar and creates a private legacy session controller.

- [x] **Step 4: Add session replacement tests**

Test the headless gallery presenter or binding adapter: a load from session A completing after session B attaches must be ignored; detaching cancels queued thumbnail requests; attaching B performs one fresh repository load.

- [x] **Step 5: Run all affected tests and commit**

Run:

```powershell
dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj
dotnet test tests/ScreenshotGallery.Core.Tests/ScreenshotGallery.Core.Tests.csproj
dotnet test tests/FileManager.Core.Tests/FileManager.Core.Tests.csproj
dotnet build src/ScreenshotGallery.App/ScreenshotGallery.App.csproj -c Release
```

Expected: all tests pass and the old gallery executable still builds.

Commit:

```powershell
git add src/ScreenshotGallery.App src/ScreenshotGallery.Core src/FileManager.Core src/PS4Manager.Core/Media tests
git commit -m "Share gallery transfers and connection"
```

### Task 6: Dashboard, tool-window registry, and unified executable

**Files:**
- Create: `src/PS4Manager.App/PS4Manager.App.csproj`
- Create: `src/PS4Manager.App/Program.cs`
- Create: `src/PS4Manager.App/AppPaths.cs`
- Create: `src/PS4Manager.App/ManagerSessionFactory.cs`
- Create: `src/PS4Manager.App/DashboardForm.cs`
- Create: `src/PS4Manager.App/ToolWindowRegistry.cs`
- Create: `src/PS4Manager.App/Dialogs/ConnectionDialog.cs`
- Create: `src/PS4Manager.App/app.manifest`
- Create: `tests/PS4Manager.Core.Tests/Presentation/ToolWindowRegistryModelTests.cs`
- Modify: `ScreenshotGallery.sln`

**Interfaces:**
- Consumes: Tasks 1-5 and the existing UIFramework controls.
- Produces: `PS4Manager.exe`, dashboard connection controls, five tool tiles, shared transfer summary, and single-instance tool activation.

- [x] **Step 1: Write the registry model test**

Keep native windows out of unit tests by extracting a generic registry model:

```csharp
[Fact]
public void Reopening_a_tool_activates_the_existing_instance()
{
    var first = _registry.Open("files", _factory);
    var second = _registry.Open("files", _factory);
    Assert.Same(first, second);
    Assert.Equal(1, _factory.CreateCount);
    Assert.Equal(1, first.ActivateCount);
}

[Fact]
public void Closing_a_tool_allows_a_new_instance()
{
    var first = _registry.Open("files", _factory);
    first.RaiseClosed();
    var second = _registry.Open("files", _factory);
    Assert.NotSame(first, second);
    Assert.Equal(2, _factory.CreateCount);
}
```

Run: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~ToolWindowRegistryModelTests`

Expected: FAIL because the registry model does not exist.

- [x] **Step 2: Implement the dashboard layout**

Use UIFramework skin controls and the existing dark default. The top connection card shows profile/IP/port, `Connect` or `Disconnect`, and state text. The center uses five keyboard-accessible tiles. File Manager and Media tiles are enabled when connected; GoldHEN, Backup, and Diagnostics open clear phase-status forms until their dedicated implementation plans replace those bodies. The bottom transfer summary subscribes to the shared queue and shows active title, progress, queued count, and a button that activates the File Manager transfer panel.

- [x] **Step 3: Implement connection and session factory wiring**

`ManagerSessionFactory` creates the one real `FtpClient`, File Manager prompt adapters owned by the dashboard, file watcher, clock, `ExplorerSession`, and `ManagerSession`. The dashboard saves the selected endpoint only after a successful connection and uses `ManagerSessionController` for connect, replace, and disconnect.

- [x] **Step 4: Implement ToolWindowRegistry**

Map stable keys to form factories. On open, activate and restore an existing non-disposed form; otherwise create it, subscribe once to `FormClosed`, and remove that exact instance when it closes. Dashboard shutdown closes all registered forms after the consolidated leave check succeeds.

- [x] **Step 5: Add the executable to the solution and perform smoke tests**

The project references `PS4Manager.Core`, both existing app projects during migration, and required UIFramework projects. Configure assembly name `PS4Manager`, manifest, and the established icon until a unified icon is supplied.

Run:

```powershell
dotnet sln ScreenshotGallery.sln add src/PS4Manager.App/PS4Manager.App.csproj
dotnet build ScreenshotGallery.sln -c Release
```

Start `src/PS4Manager.App/bin/Release/net48/PS4Manager.exe`, verify it remains responsive, open File Manager and Gallery once each, click each tile again to verify activation rather than duplication, close both, and exit the dashboard.

- [x] **Step 6: Commit**

```powershell
git add ScreenshotGallery.sln src/PS4Manager.App tests/PS4Manager.Core.Tests/Presentation
git commit -m "Add unified PS4 Manager dashboard"
```

### Task 7: Documentation, regression verification, and phase review

**Files:**
- Modify: `README.md`
- Create: `docs/ps4-manager-migration.md`
- Modify: `.gitignore` only if new generated output appears during verification.

**Interfaces:**
- Consumes: complete unified-shell behavior from Tasks 1-6.
- Produces: user-facing build, launch, migration, and current-module documentation.

- [x] **Step 1: Update documentation**

Make `PS4Manager.exe` the recommended launch target. Document dashboard connection ownership, the File Manager and Gallery windows, legacy settings import, data paths, shared transfer behavior, and that the three remaining dashboard modules are delivered by their named follow-on plans rather than claiming they already work.

- [x] **Step 2: Run complete verification**

Run:

```powershell
dotnet test ScreenshotGallery.sln -c Debug
dotnet test ScreenshotGallery.sln -c Release
dotnet build ScreenshotGallery.sln -c Release --no-restore
git diff --check
```

Expected: every test passes; Release build reports zero warnings and zero errors; diff check reports no whitespace errors.

- [x] **Step 3: Run process smoke tests**

Start each of these hidden, wait four seconds, assert `HasExited == false` and `Responding == true`, then stop the exact process:

```text
src/PS4Manager.App/bin/Release/net48/PS4Manager.exe
src/FileManager.App/bin/Release/net48/PS4FileManager.exe
src/ScreenshotGallery.App/bin/Release/net48/PS4ScreenshotGallery.exe
```

Then run the visible unified-window smoke sequence from Task 6.

- [x] **Step 4: Request read-only review and address findings**

Ask a fresh reviewer to inspect the entire phase for duplicated FTP construction, session-lifecycle races, bypasses of the global queue, stale UI callbacks, and migration data loss. Fix every confirmed Critical, Important, or Minor finding and repeat the affected verification.

- [x] **Step 5: Commit documentation and final phase fixes**

```powershell
git add README.md docs/ps4-manager-migration.md
git commit -m "Document the unified PS4 Manager shell"
```

After this plan is complete, author separate implementation plans in this order: Backup & Restore; Text Comparison; GoldHEN Manager; Gallery Search/Filters/Favorites; Diagnostics; final data migration and removal of legacy executables. Each plan must leave `PS4Manager.exe` usable and pass the complete solution verification before the next begins.

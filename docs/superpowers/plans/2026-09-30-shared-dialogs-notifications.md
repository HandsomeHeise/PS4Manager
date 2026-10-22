# Shared Dialogs and Desktop Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the remaining Mod Library Windows message boxes with skinned dialogs and provide reusable, non-activating desktop notifications for shared transfer results.

**Architecture:** Extend the existing shared `FileManager.App` UI assembly, without changing the separate UIFramework repository. A pure notification model controls capacity, expiry and placement; one dashboard-owned presenter manages windows. A session-bound adapter snapshots transfer events and coordinates modal ownership before enqueueing jobs.

**Tech Stack:** C# 7.3, .NET Framework 4.8, WinForms, existing UIFramework controls and skins, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-09-30-shared-dialogs-notifications-design.md` — approved by the user after commit `ee73f1f`.

## Global Constraints

- Existing worktree: `C:\Users\BBC\.codex\worktrees\mod-library-server\ScreenshotGallery`; use explicit working directories. Check status and applicable AGENTS before execution; preserve unrelated edits.
- Do not modify `C:\Users\BBC\.codex\worktrees\UIFramework`. Reuse its controls and skin appearances.
- Dialogues retain existing public calls and choice indices; safe activation default remains Later. Closing is never permission to write.
- "Richtwert: 360 logische Pixel Breite, maximal 220 Höhe, 12 Pixel Außenabstand."
- "Höchstens drei sichtbare Karten"; pending capacity 50; overflow becomes one dismissible summary with a count and transfer access.
- "Information und Erfolg verschwinden nach sechs Sekunden." Hover or keyboard focus pauses elapsed visible time; warnings/errors persist until dismissed.
- No native Windows notifications, notification-center integration, sounds, animation, background service or new tray behavior. Manager must be running.
- Keep existing English Mod Library copy; use structured outcomes, never parse English messages for severity.
- No console writes, server deployment or changes to FTP safety behavior during this work. Use fakes for tests.
- UI failures must not fail or alter a transfer. Dispose windows, timers and subscriptions on shutdown.

## Review Focus

1. Synchronous or extremely fast job completion before the enqueue method returns: register modal ownership before enqueue (Task 5 tests).
2. Display removal, negative monitor coordinates and taskbars on other edges: keep cards inside the current working area without stealing focus (Tasks 2–3 tests).
3. Repeated terminal events, rapid resume and delayed UI dispatch: each attempt generates one truthful result, never a replay from another session (Task 4 tests).
4. Long unbroken paths, many dialog lines and 200% DPI: actions remain reachable and full details are recoverable (Tasks 1 and 3 tests).
5. Shutdown or session change while Details is queued, including a throwing callback: no stale console action, leaked window or transfer failure (Tasks 3–5 tests).

## File Map

All paths below are relative to the worktree above. New files belong to the existing SDK-style projects unless stated otherwise.

| Area | Files | Responsibility |
| --- | --- | --- |
| Shared dialog | `src/FileManager.App/Dialogs/MessageDialog.cs`, new `MessageKind.cs` | Compatible skinned dialogs and semantic severity |
| Notification data/state | new `src/FileManager.App/Notifications/DesktopNotification.cs`, `NotificationQueue.cs`, `NotificationLayout.cs` | Immutable display data, bounded queue, expiry and geometry |
| Desktop presenter | new `src/FileManager.App/Notifications/NotificationWindow.cs`, `DesktopNotificationService.cs` | Controls, UI-thread dispatch, windows and lifetime |
| Transfer bridge | new `src/PS4Manager.App/Notifications/TransferNotificationSnapshot.cs`, `TransferNotificationTracker.cs`, `TransferNotificationCoordinator.cs` | Snapshot, attempt deduplication, session identity, modal ownership |
| Composition | `src/PS4Manager.App/DashboardForm.cs` | Own service/coordinator, attach queue, Details navigation |
| Mod outcomes | new `src/PS4Manager.Core/Mods/ModActivationOutcome.cs`; modify `ModActivationPrompt.cs`, `ModLibraryController.cs` | Typed activation result, pre-enqueue registration seam |
| Mod UI | `src/PS4Manager.App/ModLibraryForm.cs`, `ModConsoleProfileDialog.cs` | Replace MessageBox and coordinate completion presentation |
| Tests | new `tests/PS4Manager.Core.Tests/Notifications/*.cs`, `Presentation/MessageDialogTests.cs`, `TestSupport/StaTest.cs`; existing `Mods/ModActivationPromptTests.cs`, `Mods/ModLibraryFormTests.cs`, `Mods/ModLibraryControllerTests.cs` | Unit, fake-session and UI checks |
| Delivery | `README.md`, new `scripts/package-client.ps1` | Behavior documentation and verified runtime ZIP |

Do not reorganize unrelated dashboard or transfer code. Keep the existing specialized activation prompt unless a specific layout test exposes a problem.

### Task 1: Compatible skinned message dialogs

**Interfaces**

- Add public `MessageKind { Information, Success, Warning, Error }` in `FileManager.App.Dialogs`.
- Keep the existing constructor and `Choose`, `Info`, `Ask` unchanged.
- Add constructor overload `(string title, string message, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex, string checkText, MessageKind kind, string details)`.
- Add `ShowMessage(IWin32Window owner, string title, string message, MessageKind kind, string details = null)` returning void.
- Add test helper `StaTest.Run(Action action)`: new STA thread, capture exception with `ExceptionDispatchInfo`, join, rethrow on caller. UI tests use `UiControlCollection` and restore SkinManager in finally.

- [ ] Write failing tests in `Presentation/MessageDialogTests.cs` for default and cancel indices, Escape, Enter on focused button, window close, checkbox behavior, and invalid index input. Representative assertion:

```csharp
[Fact]
public void Closing_confirmation_keeps_cancel_choice()
{
    StaTest.Run(() => {
        using (var dialog = new MessageDialog("Confirm", "Continue?",
            new[] { "Continue", "Cancel" }, 1, 1, null)) {
            dialog.Show();
            dialog.Close();
            Assert.Equal(1, dialog.Choice);
        }
    });
}
```

- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter FullyQualifiedName~MessageDialogTests`; distinguish baseline passing tests from a new failing long-text/overload case.
- [ ] Implement semantic title/icon text using existing skin foreground and controls; no hard-coded palette. Replace unconstrained per-line sizing with a bounded wrapping body and scrollable read-only detail text. Preserve buttons and checkbox outside the scrolling region. Use monitor WorkingArea to constrain the form; validate indices before constructing controls.

```csharp
if (defaultIndex < 0 || defaultIndex >= buttons.Count)
    throw new ArgumentOutOfRangeException(nameof(defaultIndex));
if (cancelIndex < 0 || cancelIndex >= buttons.Count)
    throw new ArgumentOutOfRangeException(nameof(cancelIndex));
// Existing constructor delegates to the new overload with Information/null.
```

- [ ] Add long-path (4096 characters), multiline, Light/Dark and 100/150/200% tests; assert every button is within the client area and full content remains accessible in a read-only control. Exercise Enter/Escape through the existing reflection pattern in ModLibraryFormTests.
- [ ] Run focused tests and build `src/FileManager.App/FileManager.App.csproj -c Release`. Commit only this task's files with message `Add bounded skinned message dialogs`.

### Task 2: Notification state and screen geometry

**Interfaces**

- `DesktopNotification(string title, string message, MessageKind kind, string details = null, Action openDetails = null)`; immutable properties plus generated `Guid Id`.
- `NotificationQueue(Action openOverflowDetails)` with `Add(DesktopNotification)`, `Dismiss(Guid)`, `Advance(TimeSpan elapsed, ISet<Guid> paused)`, `SetVisibleCapacity(int)`, `Clear()`; `Visible` and `Pending` are read-only snapshots, `OverflowCount` is an integer.
- `NotificationLayout.Arrange(Rectangle workingArea, int dpi, IReadOnlyList<Size> preferredSizes)` returns `IReadOnlyList<Rectangle>` for the cards that fit, at most three.
- Model confined to UI thread. Geometry has no dependency on Screen or window handles. Pending limit excludes a single overflow-summary record. Summary is persistent Warning, count updated in place, oldest pending entries served first; promote the summary once existing pending entries are served. Closing summary resets its count.

- [ ] Create `NotificationQueueTests.cs` and `NotificationLayoutTests.cs`; write red tests:

```csharp
[Fact]
public void Error_does_not_expire()
{
    var queue = new NotificationQueue(() => { });
    queue.SetVisibleCapacity(3);
    var notice = new DesktopNotification("Upload failed", "Open Details", MessageKind.Error);
    queue.Add(notice);
    queue.Advance(TimeSpan.FromHours(1), new HashSet<Guid>());
    Assert.Equal(notice.Id, Assert.Single(queue.Visible).Id);
}

[Fact]
public void Negative_monitor_origin_is_respected()
{
    var area = new Rectangle(-1920, 40, 1920, 1040);
    var boxes = NotificationLayout.Arrange(area, 192,
        new[] { new Size(720, 440), new Size(720, 440), new Size(720, 440) });
    Assert.InRange(boxes.Count, 1, 3);
    Assert.All(boxes, box => Assert.True(area.Contains(box)));
}
```

- [ ] Run test project with filter `FullyQualifiedName~NotificationQueueTests|FullyQualifiedName~NotificationLayoutTests`; verify failure due to missing behavior.
- [ ] Implement promotion, paused visible elapsed time and expiry at six seconds. Pending entries accrue no time. On capacity reduction, move displaced cards to the front of pending preserving their remaining lifetime; apply the same cap and overflow aggregation. Clamp width/height to available area. If no card fits, keep it pending rather than creating an offscreen form.

```csharp
bool expires = notice.Kind == MessageKind.Information || notice.Kind == MessageKind.Success;
// Each visible entry tracks its own elapsed time; do not use wall-clock deadlines.
if (expires && !paused.Contains(notice.Id)) entryElapsed += elapsed;
```

- [ ] Add boundary tests at 5.999/6 seconds, hover pause, zero/one/three capacity, 1000 notifications, FIFO promotion, persistent overflow summary, empty area, 200% scale and taskbar offsets. Confirm pending memory remains bounded and no rectangle intersects its neighbor.
- [ ] Run focused tests green; commit `Add bounded desktop notification state and layout`.

### Task 3: Non-activating desktop presenter

**Interfaces**

- `NotificationWindow(DesktopNotification notice)` extends `AppForm`, exposes `bool InteractionActive`, `event EventHandler Dismissed`, `event EventHandler DetailsRequested`.
- `DesktopNotificationService(Form dashboard, Action openOverflowDetails) : IDisposable`; public `void Show(DesktopNotification notice)` and `void Clear()`. Calls are marshalled onto dashboard UI thread; after disposal they are ignored. Clear closes cards and resets pending/overflow state, without invoking any Details action.
- Private presenter owns NotificationQueue, a WinForms timer and window dictionary by Id. Use Stopwatch deltas for time, not DateTime. Clock/monitor inputs exposed internally for deterministic tests; use a narrowly scoped InternalsVisibleTo for the existing test assembly if required.

- [ ] Write failing `NotificationWindowTests.cs` and `DesktopNotificationServiceTests.cs` for no taskbar presence, dismiss action, throwing Details callback, late Show after disposal and no timer/window leaks. Reuse `StaTest.Run`.
- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter "FullyQualifiedName~NotificationWindowTests|FullyQualifiedName~DesktopNotificationServiceTests"` and confirm expected failure.
- [ ] Construct card from SkinPanel, SkinLabel and SkinButton. Keep title and accessible status label visible; clamp preview text, with full details in shared MessageDialog. The close button has accessible name `Close notification`; Details is explicit. Use these WinForms overrides:

```csharp
protected override bool ShowWithoutActivation => true;
protected override CreateParams CreateParams {
    get {
        var parameters = base.CreateParams;
        parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
        return parameters;
    }
}
```

- [ ] Set borderless, ShowInTaskbar=false and topmost; show unowned so minimizing the dashboard does not hide the cards. Reposition without activation (do not call Activate or Focus). Do not set permanent WS_EX_NOACTIVATE: explicit interaction must allow keyboard use. Hover is cursor-inside-card (including children); keyboard pause uses ContainsFocus.
- [ ] Service remembers dashboard's last non-minimized monitor. Recompute on dashboard move/resize, display/work-area change and card DPI/skin changes. Use current working area; resolve removed monitor to primary. Pair every event registration with Dispose removal. Never use a notification click to reconnect a console.
- [ ] Catch disposed-handle races at dispatch and callback exceptions at the UI boundary. A failed Details callback opens a safe shared error dialog if possible; failed display leaves existing transfer/status information unchanged. No exceptions propagate to JobChanged.
- [ ] Test long text, Light/Dark skin switch, 100/150/200% scale, minimized dashboard, monitor removal using injected rectangles, shutdown with a posted callback, and throwing actions. Verify foreground HWND remains unchanged when showing and reflowing cards in a controlled desktop test; report if the environment cannot execute that test.
- [ ] Run focused tests and Release build; commit `Present skinned desktop notifications without activation`.

### Task 4: Session-bound transfer results

**Interfaces**

- `TransferNotificationSnapshot.Capture(TransferJob job)` copies State, Title, Summary, InterruptReason and failure messages immediately on event receipt; never retain mutable Steps for later rendering.
- `TransferNotificationTracker.Observe(TransferJob job, TransferNotificationSnapshot snapshot)` returns bool: true only for first reportable terminal state in a new attempt. `Seed(IEnumerable<TransferJob> jobs)` marks existing terminal states seen. Waiting/Running resets terminal marker only after a terminal state; repeated Running events do not reset it.
- `TransferNotificationCoordinator(DesktopNotificationService service, Action<Action> dispatch, Action openTransfers) : IDisposable` with `Attach(TransferQueue queue)`, `Claim(TransferJob job, bool successOnly)` and `ShowForQueue(TransferQueue expectedQueue, DesktopNotification notice)`. ShowForQueue rejects a stale queue and dispatches with the same generation guard as job results. Claims use weak keys and are registered before enqueue. `successOnly=true` suppresses only Completed without failures; false suppresses all outcomes for that job.
- Coordinator keeps a monotonically increasing attachment generation. Every dispatched callback validates generation; Details callbacks for older sessions show copied text only.

- [ ] Write failing `TransferNotificationTrackerTests.cs` and `TransferNotificationCoordinatorTests.cs`. Test terminal sequence independent of UI dispatch:

```csharp
// Test helper sets internal State using reflection, as existing ModLibraryFormTests do.
var job = new TransferJob(TransferKind.Upload, "Upload", new TransferStep[0]);
var tracker = new TransferNotificationTracker();
typeof(TransferJob).GetProperty("State").SetValue(job, JobState.Completed);
Assert.True(tracker.Observe(job, TransferNotificationSnapshot.Capture(job)));
Assert.False(tracker.Observe(job, TransferNotificationSnapshot.Capture(job)));
```

- [ ] Run filter `FullyQualifiedName~TransferNotification`; verify red cases for deduplication and stale session dispatch.
- [ ] Implement snapshot-to-notice mapping: Completed without failures -> Success; CompletedWithErrors or copied failures -> Error; Interrupted -> Warning with reason; Cancelled/Waiting/Running -> no notification. All result notices have Details access. A terminal Completed with failures must never be labeled Success.

```csharp
int capturedGeneration = generation;
dispatch(() => {
    if (disposed || capturedGeneration != generation) return;
    service.Show(notice);
});
```

- [ ] Marshal snapshots, not live job reads. Synchronize tracker/claim access because JobChanged is a worker event. Bound retention using weak job keys; seed from Queue.Jobs on Attach, clear per-attachment tracker on detach. On an actual queue change, Clear the service on the UI thread before displaying new-session results, so overflow cannot mix sessions. Already dispatched Details callbacks still validate generation and fall back to copied text. Guard the whole event callback so presentation cannot affect queue progress.
- [ ] Add tests for Interrupted -> Waiting -> Running -> Completed, immediate completion before UI dispatch, mutable job changes after snapshot, historical queue results, duplicate terminal events, failed individual steps, cancellation, old-session Details and disposal.
- [ ] Integrate dashboard ownership: instantiate after a valid UI dispatcher is available, attach in AttachQueue, dispose before dashboard disposal. Pass a guarded ShowTransfers delegate for current session only. Keep existing summary logic. Test two fake job kinds representing file transfers and a GoldHEN action through the same coordinator, without console I/O.
- [ ] Run focused tests and existing DashboardFormTests; commit `Notify shared transfer results with session-safe deduplication`.

### Task 5: Mod dialogs and duplicate-free outcomes

**Interfaces**

- Add `ModActivationOutcomeKind { Deferred, Enabled, ActivationFailed }` and immutable `ModActivationOutcome(ModActivationOutcomeKind kind, string message)` with properties `Kind`, `Message`.
- Add `ModActivationPrompt.OfferResultAsync` with the same arguments as OfferAsync, returning `Task<ModActivationOutcome>`; null means no eligible/current result. Keep OfferAsync as an async compatibility wrapper returning `result?.Message` so existing callers/tests keep working.
- Add optional final `Action<TransferJob> beforeEnqueue = null` to `ConfirmAndEnqueue`, `Recover`, `ActivatePlugins`; call before queue.Enqueue. The callback is registration only, never UI/network/await. Catch its exception so a notification failure cannot block the existing transfer. Do not change transaction confirmation or locking semantics.
- Extend ModLibraryForm constructor with optional `TransferNotificationCoordinator notifications = null`; dashboard supplies the shared instance.

- [ ] Add typed-result tests to ModActivationPromptTests (deferred, enabled, failure after successful install, failed install, stale profile/session). Keep all existing tests. Representative new expectation uses the same existing fake ModScenario arrange:

```csharp
var result = await ModActivationPrompt.OfferResultAsync(job, "install", () => true,
    () => receipt, r => Task.FromResult(true),
    id => Task.FromException(new IOException("Backup unavailable")));
Assert.Equal(ModActivationOutcomeKind.ActivationFailed, result.Kind);
Assert.Contains("Installation succeeded", result.Message);
```

- [ ] Run filter `FullyQualifiedName~ModActivationPromptTests`; verify the typed API fails before implementation, then implement while preserving original copy and guards.
- [ ] Add controller tests proving beforeEnqueue registration happens before the first Queue.JobChanged event, including an immediate fake transaction, and that a throwing registration does not alter the job outcome. The callback cannot auto-confirm install or activation.
- [ ] Register successful PRX install/resume ownership before enqueue using verified plan targets/receipt plugin paths. Only claim success for those jobs; failed installs still notify. Claim all outcomes of optional activation because OfferResultAsync handles the result. Non-PRX installs, rollback and uninstall continue through the general notification path.

```csharp
Action<TransferJob> claimSuccess = job => notifications?.Claim(job, true);
Action<TransferJob> claimActivation = job => notifications?.Claim(job, false);
// Pass claimSuccess only for PRX install/resume; claimActivation for activation.
```

- [ ] Replace FinishInstallation's MessageBox with `MessageDialog.ShowMessage`, mapping Enabled -> Success, Deferred -> Information, ActivationFailed -> Warning. Keep status.Text updated. Replace the console-profile validation MessageBox and close-confirmation MessageBox with shared dialogs; close-confirmation defaults to Cancel. Do not replace activation selection with a notification.
- [ ] Route ModLibraryForm Run/RunSync exceptions that are not already represented by a queued job through `notifications.ShowForQueue(capturedSession.Queue, notice)`, retaining the status line. Cancellation remains silent. Avoid sending a second error for the same queued failure; scope this explicitly by whether the operation created/awaited a job. Capture session at operation start, not in the catch block.
- [ ] Test instant completion, one modal outcome/no duplicate notice, failed PRX install still notifying, scripts-only install notifying, session change during prompt, disposal before completion, warning severity without message parsing, and unchanged Later/Enter/Escape behavior.
- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter "FullyQualifiedName~Mods|FullyQualifiedName~Notifications|FullyQualifiedName~MessageDialogTests"`; commit `Use shared dialogs and coordinate mod completion notifications`.

### Task 6: Verification, documentation and runtime ZIP

**Files:** README.md; create scripts/package-client.ps1; use ignored `.superpowers/artifacts/shared-notifications-2026-09-30/` for screenshots, logs and ZIP. Preserve all earlier ZIPs.

- [ ] Add packaging script with mandatory output path. It enumerates Release/net48 files with extensions `.exe`, `.dll`, `.config`, rejects an existing output path, creates ZIP via ZipArchive and verifies every entry's size and SHA256 against its build source. Fail if PS4Manager.exe is missing; do not include PDBs, test files, credentials or user settings. Use explicit paths and no deletion.

```powershell
param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Output already exists.' }
# Derive build root from $PSScriptRoot, not the caller's current directory.
# Use FileMode.CreateNew and ZipArchiveMode.Create, then reopen in Read mode.
```

- [ ] Negative-check packaging against a missing build source and an existing output filename; positive-check entry hashes and ensure output closes on failure. Do not overwrite a user's installed executable.
- [ ] Run complete verification from the worktree:

```powershell
dotnet test PS4Manager.sln -c Release
dotnet build src/PS4Manager.App/PS4Manager.App.csproj -c Release
git diff --check
rg -n 'MessageBox\.Show' src/PS4Manager.App/ModLibraryForm.cs src/PS4Manager.App/ModConsoleProfileDialog.cs
```

Expected: all tests pass, app builds successfully, diff check is clean, rg finds no remaining MessageBox call in those two files (exit 1 is expected).

- [ ] Render and inspect dialog/card screenshots in Light/Dark at 100/150/200%; include long path, error details and stacked cards. Check actual desktop focus, minimized main form and taskbar placement with a controlled offline harness. Use computer-use skill if interacting with native UI is available; otherwise state limitations and do not claim unrun manual tests.
- [ ] Request one independent whole-change review according to requesting-code-review, focusing on five Review Focus cases and existing safety semantics. Fix reproduced findings with regression tests, then rerun affected tests plus full suite before release.
- [ ] Document custom-window limitations, expiry, persistent errors and Details behavior in README. Do not imply Windows notification-center or Do Not Disturb support.
- [ ] Build ZIP with `scripts/package-client.ps1 -OutputPath 'C:\Users\BBC\.codex\worktrees\mod-library-server\ScreenshotGallery\.superpowers\artifacts\shared-notifications-2026-09-30\PS4Manager-SharedNotifications.zip'`; create the artifact directory if absent, and select a new numbered filename if the ZIP already exists. Report the actual artifact path, SHA256, test counts and remaining manual limitations. ZIP verification is against this final build, after any review fixes.
- [ ] Commit documentation/packaging and final fixes after verification. Do not push, publish a server release, deploy to console or archive the worktree as part of this task.

## Plan Self-Review

- Spec sections map to Tasks 1 (dialogs), 2–3 (desktop behavior), 4–5 (events/ownership), 6 (verification/delivery).
- All five Review Focus conditions have explicit tests in their owning tasks.
- Core activation compatibility is preserved through OfferAsync; existing dialog API remains callable. No UI dependency is added to core assemblies.
- The notification registry is registered before enqueue, avoiding the fast-job race. Old-session queued display is dropped; old displayed Details uses captured text only.
- No product code has been changed while writing this plan. Execute only after the user reviews the plan and selects an execution method.

## Execution result — 2026-09-30

All six tasks implemented locally. Full solution: 927 passing tests (428 manager,
361 file manager, 96 PS4 core, 42 gallery). Release client build: zero warnings and
zero errors. Packaging negative/positive checks passed; the runtime ZIP contains
exactly 14 allowlisted files, individually verified by size and SHA256.

Independent whole-change review found two Important issues, no Critical or Minor:

- A resume during result capture could combine state from attempt 1 with identity
  from attempt 2 and swallow its eventual completion. Deterministic real-queue
  regression failed before the fix and passes afterward. Attempt publication now
  follows resetting the terminal state; capture rejects an attempt changed mid-copy.
- Current-session Details navigated only to mutable transfers. Two regressions
  covering 25 long-path failures after ClearFinished/resume failed before the fix
  and now pass. Details always exposes the saved result; optional Open Transfers
  rechecks the session after the dialog closes.

Light/dark renderings at simulated 100/150/200% inspected, including long paths,
error details and three-card stacks. Automated native-window checks cover focus
preservation, explicit keyboard focus, minimized host, disposal and late dispatch.
Physical mixed-DPI changes, monitor unplugging and alternate taskbar edges were
not live-tested. Render scaling is not a claim of real device-DPI verification.

Local runtime artifact:
`.superpowers/artifacts/shared-notifications-2026-09-30/PS4Manager-SharedNotifications.zip`

SHA256: `30A7FBC5C97DECBE50EB8204CC090B47896E66EB939A3893731D2BC1F867F693`.
No credentials/settings included. No server or PS4 changes, push, publication or
worktree removal. Earlier ZIPs preserved.

Execution decisions and tradeoffs:

1. Monotonic attempt metadata replaces timing inference; cost: one added core field.
2. Internal delegate constructor enables headless coordinator tests; cost: a small
   internal testing surface.
3. Explicit runtime allowlist avoids shipping settings/test DLLs; cost: maintain
   the list when adding runtime dependencies.
4. Failed Details callbacks produce a persistent error card instead of another
   modal; cost: one extra Details click to inspect that error.
5. Physical multi-monitor/DPI checks remain manual; cost: hardware-specific layout
   behavior remains unverified despite geometry and native-window coverage.
6. The reviewer deliberately did not rerun tests concurrently; the implementer's
   observed full-suite and release results are the gate. Cost: no independently
   repeated test run.

Deferred minor findings: none. Execution logs and ledger are retained in an
evidence ZIP beside the runtime artifact. Scratch cleanup was blocked by tool
policy; the ignored scratch directory is retained too. No deletion occurred.

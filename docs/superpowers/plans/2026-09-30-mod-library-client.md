# Mod Library Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let PS4 Manager discover exactly compatible published mods and safely install, update, uninstall, and recover their files on a connected console.

**Architecture:** Add a testable Mods subsystem to PS4Manager.Core and a skinned single-instance WinForms tool. Keep HTTP trust validation separate from console discovery and journaled FTP transactions. Execute each installation as one shared-queue job, with durable recovery state and no silent plugin activation.

**Tech Stack:** Existing .NET Framework 4.8, C# 7.3, WinForms/UIFramework, xUnit, HttpClient, RSA/SHA-256, existing FTP and Frame4 clients. No new runtime dependencies without a documented necessity and review.

**Spec:** `docs/superpowers/specs/2026-09-30-mod-library-client-design.md`; frozen API: `server/API.md`.

## Global Constraints

- Generic files packages copy only declared files.
- No server changes, release publication, RDR2 compatibility edits, community uploads, dependency resolution, automatic updates, silent plugin activation, or PRX hot-loading.
- Existing console files must never be silently replaced.
- No private keys enter the app.
- Size agreement alone is not cryptographic proof of remote content.
- Never recursively delete a game data root.
- Public-server acceptance is read-only against the existing Luma release; do not create or publish production fixtures.
- Hardware acceptance is separately authorized and uses a disposable non-game target.
- Retain net48/C# 7.3 and existing English UI copy conventions. Do not use records, nullable-reference syntax, or APIs available only in modern .NET.
- Work in the already attached isolated worktree. Preserve ignored operator files and the open server PR; do not push this client phase into that PR without direction.

## Review Focus

1. A console reconnects at the same address but is not the same device: receipts must not authorize writes without explicit local profile confirmation (Tasks 5, 9).
2. FTP returns a repeatable, incorrect blob or 550 means denied rather than absent: no fabricated backup or assumed absence; preserve originals and stop on inconsistencies (Tasks 6, 7).
3. A remote rename succeeded but its response was lost: recovery inspects content/state and never repeats destructive operations blindly (Task 7).
4. An ordinary file-manager job is submitted while installation is backing up: shared queue ordering and transfer-gate ownership prevent interleaving/deadlock (Tasks 6, 10).
5. Window closure, cancellation, or late HTTP completion after switching games: no stale card may authorize a write or erase recovery state (Tasks 8, 9).

## File and interface map

New core files live under `src/PS4Manager.Core/Mods/`:

- `ModContracts.cs`, `ModCompatibility.cs`: immutable validated public models and canonical pairs.
- `ModJson.cs`: bounded strict JSON reading, duplicate-member rejection, model conversion.
- `ModCatalogClient.cs`, `ModCatalogCache.cs`: HTTPS API, bounded downloads and descriptive cache.
- `ModSignatureVerifier.cs`, `ModTrustKeys.cs`: independently reconstructed signed envelope and pinned public keys.
- `ModManifest.cs`, `ModPackageValidator.cs`, `ModZipDirectory.cs`: manifest/ZIP validation and controlled extraction.
- `ModGameDiscovery.cs`, `ModGame.cs`: installed metadata and optional running-game annotation.
- `ModReceipt.cs`, `ModReceiptStore.cs`, `ModConsoleProfile.cs`: versioned local journals, backups and console association.
- `ModInstallPlanner.cs`, `ModTransaction.cs`, `ModRecovery.cs`, `ModRemoteSnapshot.cs`: preflight, staged mutations, recovery and ownership.
- `ModLibraryController.cs`, `ModLibraryState.cs`, `ModLumaReload.cs`: UI-independent state, actions and optional reload.

New app files: `src/PS4Manager.App/ModLibraryForm.cs`, `ModInstallReviewDialog.cs`, `ModConsoleProfileDialog.cs`.
Modify `DashboardForm.cs`, `AppPaths.cs`, `ManagerSettings.cs`, and the existing shared transfer types only at the boundaries identified below.
Tests mirror the new core files under `tests/PS4Manager.Core.Tests/Mods/`; queue tests remain in `tests/FileManager.Core.Tests/`.

## Execution conventions

Each task runs RED first, then GREEN, then its regression set and a scoped commit.
Use `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~<class>` for core focused tests.
Compiler failure is acceptable for the first RED when the production type does not exist; thereafter assert behavioral failures.
The code blocks below define the contract and representative assertions; the explicitly listed negative cases are mandatory, not optional examples.
Use temp directories per test, bounded fake streams, deterministic clocks, and no real console in automated tests.

### Task 1: Strict contract parsing and compatibility

**Files:** Create `ModContracts.cs`, `ModCompatibility.cs`, `ModJson.cs`; test `Mods/ModContractTests.cs`.

**Interfaces:** `ModCompatibility.NormalizeTitleId(string)`, `NormalizeVersion(string)`, and `Key(string,string)` return strings or throw `FormatException`.
`ModJson.ReadCatalog(string)`, `ReadMod(string)`, and `ReadRelease(string)` return `ModCatalogPage`, `ModDetail`, and `ModRelease`.
Models mirror all fields in `server/API.md` and `server/src/catalog/catalog-service.ts`; use `Guid` IDs, `long` sizes, UTC DateTimeOffset timestamps, and explicit compatibility lists. `ModRelease` includes State, Manifest, Changelog and SignedEnvelope; `ModCatalogItem` contains summary fields. No unchecked mutable DTO reaches an installer.

- [ ] Add failing normalization and hostile-JSON tests:

```csharp
[Theory]
[InlineData("01.57", "1.57")]
[InlineData("1.5", "1.05")]
public void Version_matches_server(string input, string expected)
{ Assert.Equal(expected, ModCompatibility.NormalizeVersion(input)); }

[Theory]
[InlineData("1.57\n")]
[InlineData("1.57.0")]
[InlineData("1.57 or newer")]
public void Ambiguous_versions_fail(string input)
{ Assert.Throws<FormatException>(() => ModCompatibility.NormalizeVersion(input)); }
```

- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj --filter FullyQualifiedName~ModContractTests`; observe RED.
- [ ] Implement title regex `^CUSA[0-9]{5}$`, version two numeric components of 1–3 digits, control-character rejection before trim, ordinal comparisons. Build bounded JSON token scanning with duplicate-name detection and strict UTF-8 before DataContract conversion; cap nesting at 32. Reject missing required fields, invalid v4 UUIDs, oversized/negative sizes, invalid hash/base64, duplicate normalized compatibility and contradictory manifest type. Allow only documented release states. Plain description strings are never executed or interpreted as instructions.
- [ ] Add fixtures copied from public server projection tests, plus duplicate JSON properties, escaped duplicate names, depth overflow, malformed Unicode, ID/type errors, unknown release state and normalized duplicate pairs. Run GREEN and existing update/config tests.
- [ ] Commit `feat: add strict mod catalog contracts` with only this task's files.

### Task 2: HTTPS catalog, media and download boundaries

**Files:** Create `ModCatalogClient.cs`, `ModCatalogCache.cs`; test `Mods/ModCatalogClientTests.cs`, `Mods/ModCatalogCacheTests.cs`.

**Interfaces:** Inject `HttpClient` plus validated origin and cache root into `ModCatalogClient`; production handler disables automatic redirects. Expose:

```csharp
Task<ModCatalogPage> CatalogAsync(string titleId, string version, int page, CancellationToken ct);
Task<ModDetail> DetailAsync(Guid modId, CancellationToken ct);
Task<ModRelease> ReleaseAsync(Guid releaseId, CancellationToken ct);
Task DownloadAsync(ModRelease release, string newFile, IProgress<long> progress, CancellationToken ct);
Task<byte[]> MediaAsync(Uri url, CancellationToken ct);
```

- [ ] With a fake HttpMessageHandler, assert only HTTPS same-origin API paths are requested, pageSize=24, response bodies over 4 MiB JSON/50 MiB media are rejected, and a cross-origin 302 is rejected without a second request. Assert failed artifact download leaves no accepted cache entry. Add cancellation midstream and misleading Content-Length tests; limits apply to bytes actually read.
- [ ] Run the two test classes and observe RED.
- [ ] Implement ResponseHeadersRead, bounded copy loops, 30-second metadata and 10-minute artifact timeout linked to caller cancellation. Download to a unique temporary file, reject more/fewer bytes than signed PackageSize and enforce 2 GiB compressed limit. Build download URLs from release IDs; reject off-origin detail/cover URLs. Media accepts documented PNG/JPEG/MP4 content types, with image dimensions/pixel limits in the UI decoder. No automatic retry for mutations (there are no HTTP mutations here).

```csharp
var target = new Uri(origin, "api/v1/releases/" + release.ReleaseId.ToString("D") + "/download");
if (target.Scheme != Uri.UriSchemeHttps || target.Authority != origin.Authority)
    throw new InvalidDataException("Untrusted download origin.");
```

- [ ] Implement cache records keyed by origin + normalized compatibility + page and carrying ETag/fetched UTC. Cache only parsed valid responses; atomically replace records, quarantine corrupt records, handle 304 only with valid cached content. Offline records are descriptive and never make CanInstall true. Bound catalog page navigation instead of fetching unbounded pages eagerly.
- [ ] Run GREEN, test empty pages, 404/withdrawal, 429/Retry-After presentation, 304 without cache, stale cache, cache separation across origins and overlapping requests.
- [ ] Commit `feat: add bounded mod catalog transport and cache`.

### Task 3: Pinned mod signatures and parity fixtures

**Files:** Create `ModSignatureVerifier.cs`, `ModTrustKeys.cs`; test `Mods/ModSignatureVerifierTests.cs`; add `Mods/Fixtures/mod-release.json`, `mod-public-key.xml`, `mod-package.zip` to tests with CopyToOutputDirectory metadata.

**Interfaces:** `ModSignatureVerifier.Verify(ModRelease release, Stream zip)` throws `InvalidDataException`/`CryptographicException` on failure. Constructor accepts `IReadOnlyDictionary<string,string>` of key ID to public RSA XML. `ModTrustKeys.Production` exposes only trusted public keys.

- [ ] Create a fixed dummy ZIP and signed fixture using the server's canonical signer with a test-only key; store only its public key, signature and innocuous bytes in the fixtures. Test acceptance and independent changes to every signed field, wrong key ID, duplicate pairs, truncated stream and forged SignedEnvelope.
- [ ] Run `ModSignatureVerifierTests` and observe RED.
- [ ] Reconstruct this exact UTF-8 payload with final LF, ordinal-sorted normalized compatibility, invariant decimal size, lowercase hash, lowercase canonical UUIDs and no BOM:

```text
PS4MANAGER-MOD-RELEASE-V1
releaseId=<uuid>
modId=<uuid>
version=<release version>
packageType=<files or luma-script>
size=<decimal bytes>
sha256=<lowercase hex>
compatibility=<sorted CUSAxxxxx@version pairs, comma-separated>
```

- [ ] Verify RSA-SHA256 PKCS#1 v1.5 with net48-compatible RSA, stream size/SHA-256 independently, and ensure signedEnvelope (if provided) equals the reconstruction. Obtain only the production PUBLIC key through the established authenticated operator connection, verify its DER-SPKI SHA-256 key ID matches the published release (currently `503d131c6d8d979e`), and embed it. Do not log or copy the private key. If the authoritative key cannot be obtained, report a trust blocker, never fall back to response-supplied keys.
- [ ] Run GREEN plus a read-only verification of the currently published Luma ZIP using the pinned key. Keep production IDs out of unit-test assumptions.
- [ ] Commit `feat: verify mod releases against pinned signing keys`.

### Task 4: Safe manifests, ZIP validation and staging

**Files:** Create `ModManifest.cs`, `ModPackageValidator.cs`, `ModZipDirectory.cs`; tests `Mods/ModManifestTests.cs`, `Mods/ModPackageValidatorTests.cs`.

**Interfaces:** `ModManifest.Parse(string)` returns format/type/profile and ordered source/target operations. `ModPackageValidator.ValidateAndExtract(string zipPath, string newStagingRoot, ModRelease release, CancellationToken ct)` returns `ValidatedModPackage` with Manifest and payload path/size/hash records. The staging root must not already exist and its parent must be controlled, non-reparse local storage.

- [ ] Add red tests rejecting `payload/../x`, `C:/x`, backslashes, ADS colons, trailing dots/spaces, NFC violations, COM¹/LPT² names, component UTF-8 length >255, path >1024, duplicate/case-folded source or target, target-prefix conflicts and undeclared payload files. Include strict extra/duplicate manifest properties and foreign Luma roots.
- [ ] Add ZIP fixture tests for symlinks/special files, encrypted flags, forged central/local header disagreement, CRC mismatch, unsupported methods, oversized entries/counts, path escape, reparse-point parent and truncated ZIP. Run RED.
- [ ] Parse central-directory flags/attributes explicitly (ZipArchive alone does not expose all required safety checks); reject multi-disk and unsupported ZIP features. Support stored/deflate and required ZIP64 sizes with checked arithmetic. Validate local-header correspondence, streamed CRC32, observed sizes, and portable normalized names before accepting extraction.
- [ ] Enforce 2 GiB archive, 4 GiB total expanded, 10,000 entries, 1 MiB manifest and compression ratio from the server admin limits (currently 100). Cap full local paths conservatively for the existing net48 app and reject names needing unsupported Windows path handling. Never use ExtractToDirectory on unchecked entries.

```csharp
string full = Path.GetFullPath(Path.Combine(stagingRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
string prefix = Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Package path escapes staging.");
```

- [ ] Match manifest type/profile against release, enforce ZonePolicy.Classify (not expert-mode Effective) and strict Luma roots `/data/gtalua/scripts/` or `/data/rdr2lua/scripts/`. Return validated targets and their zone requirements without writing to PS4. On failure remove only this new controlled local staging directory after validating its resolved boundary.
- [ ] Run GREEN plus all server-derived manifest fixture cases; commit `feat: validate mod archives and installation targets`.

### Task 5: Game discovery, receipt storage and console profiles

**Files:** Create `ModGame.cs`, `ModGameDiscovery.cs`, `ModConsoleProfile.cs`, `ModReceipt.cs`, `ModReceiptStore.cs`; tests `Mods/ModGameDiscoveryTests.cs`, `Mods/ModReceiptStoreTests.cs`.

**Interfaces:** `ModGameDiscovery.DiscoverAsync(CancellationToken)` returns `IReadOnlyList<ModGame>`; `RefreshAsync(string titleId,CancellationToken)` returns an uncached ModGame. Each game has TitleId, Name, Version (nullable value), InvalidReason and RunningState (Running/NotRunning/Unknown). `ModReceiptStore.Load(Guid profileId)`, `Save(ModReceipt)`, `LoadTransaction(Guid profileId,Guid transactionId)` keep versioned records under a controlled root. `ModConsoleProfile` has random local ID/name and explicit per-session confirmation, no server-visible identity.

- [ ] Test installed directories, duplicate IDs, unknown APP_VER, malformed SFO, TITLE_ID mismatch, changed version at preflight and unavailable Frame4. Require valid installed-title evidence (SFO plus installed title directory) rather than treating every stale appmeta directory as installed. Test external-storage titles separately.
- [ ] Test atomic receipt replacement, corrupt/truncated JSON, unknown schema, forged outside-root backup paths, console-profile separation and unconfirmed same-IP reconnect. Run RED.
- [ ] Read metadata using existing TitlePaths/SfoReader and FTP browse gates; strict structural validation for installation metadata must not change tolerant gallery name fallback. APP_VER is distinct from VERSION. Cross-check title ID and version sources; ambiguous/inaccessible active update metadata disables installs instead of guessing. Use Frame4ProcessInfo.TitleId for annotation, never process name alone.
- [ ] Define journal states `Prepared`, `BackingUp`, `Staging`, `Replacing`, `Incomplete`, `Installed`, `RollingBack`, `Uninstalling`, `RolledBack`, `Uninstalled`. Record transaction ID, profile ID, signed release metadata, original and predecessor backup references, target existence/hash/size, installed hash/size, unique remote stage/original names, per-step intent/completion, verification level and timestamps. Validate all loaded paths/IDs before operations.
- [ ] Save via a flushed new sibling file then atomic replace; retain previous valid journal until replacement succeeds. Backups are immutable content-addressed local files; rehash before recovery. Validate profile confirmation anew for every session.
- [ ] Run GREEN; commit `feat: discover compatible console games and persist mod receipts`.

### Task 6: Shared-queue transaction boundary and readback safeguards

**Files:** Modify `src/FileManager.Core/Transfers/TransferQueue.cs`, `TransferJob.cs`; create `IQueuedTransaction.cs`, `TransferTransactionContext.cs` in that directory. Create Mods `ModRemoteSnapshot.cs`. Test `tests/FileManager.Core.Tests/TransferTransactionTests.cs`, `Mods/ModRemoteSnapshotTests.cs`.

**Interfaces:** Add a transaction-job constructor/factory without altering existing step jobs. Transaction body implements:

```csharp
public interface IQueuedTransaction
{
    Task ExecuteAsync(TransferTransactionContext context, CancellationToken ct);
}
```

`TransferTransactionContext` exposes guarded FTP, the exclusively owned transfer slot and progress reporting; it cannot enqueue-and-await nested jobs. All transaction FTP calls execute within the queue worker's existing exclusive job and gate. Existing ordinary upload/download semantics stay unchanged.

- [ ] Use TaskCompletionSource barriers: submit ordinary A, transaction B, ordinary C; hold B during backup; assert operation trace cannot contain C until B ends. Assert cancellation releases the gate, subscriber exceptions do not wedge the queue, and disconnect marks transaction interrupted without deleting journal-owned staging. Run RED.
- [ ] Add transaction-job dispatch to RunJobAsync, acquire Transfer once for the body, link queue/user/session cancellation and dispose safely after callbacks. Expose completion Task for this job, not global WhenIdle. Mark transaction failures as interrupted/recoverable and delegate recovery to the journal engine instead of generic append-resume/discard-partial logic. Existing Resume must reject transaction jobs without explicit engine reconciliation.
- [ ] Implement ModRemoteSnapshot via listing + SIZE + two streamed reads/hashes when a file exists. A bare 550 is ambiguous; only a successful parent listing without the exact target establishes absence. Reject directory/symlink entries, changed size/hash, unavailable SIZE and inconsistent readback. Keep local backups plus on-console renamed originals for replacement recovery. Repeated matching reads are evidence of consistency, not a claim of absolute authenticity.
- [ ] Test zero-length files, permission-denied 550, vanished parent, equal-size differing reads, truncated RETR and stable-but-wrong candidate compared with known package hash. Run GREEN plus complete FileManager.Core.Tests; commit `feat: serialize journaled mod transactions in shared transfer queue`.

### Task 7: Install, update, uninstall and crash recovery

**Files:** Create `ModInstallPlanner.cs`, `ModTransaction.cs`, `ModRecovery.cs`; tests `Mods/ModTransactionTests.cs`, `Mods/ModRecoveryTests.cs`; add test-only `Mods/Support/ModScenario.cs` using in-memory FTP and disposable receipt storage.

**Interfaces:** `ModInstallPlanner.PrepareAsync(ModRelease, ModGame, Guid profileId, CancellationToken)` returns `ModInstallPlan` containing validated package, expected prior targets, required confirmations and owned predecessor receipt. `ModTransaction` implements IQueuedTransaction and accepts an immutable plan plus receipt store. `ModRecovery.CreateResume`, `CreateRollback`, `CreateUninstall` each return an IQueuedTransaction only after receipt/path/profile validation. Reject unknown or incompatible state transitions.

- [ ] Add this representative failing scenario with a test helper that creates an unsigned local scenario only AFTER trust validation has been represented by a fake validated-package boundary; production cannot bypass verification:

```csharp
[Fact]
public async Task Backup_failure_performs_no_remote_writes()
{
    using (var scenario = ModScenario.ExistingTarget("original"))
    {
        scenario.FailBackupRead = true;
        await Assert.ThrowsAsync<IOException>(() => scenario.InstallAsync("replacement"));
        Assert.Empty(scenario.RemoteMutations);
        Assert.Equal("original", scenario.ReadTarget());
    }
}
```

- [ ] Add scenarios for all backup-before-write ordering, other-mod conflict, target appearing after planning, insufficient local space, lost stage upload, lost rename response, cancellation after each journal transition, reconnect/restart, modified remote target and tampered backup. Run RED.
- [ ] Preflight re-fetches published release and console version, validates profile/approvals and all target ownership. Detect same-target and ancestor/descendant conflicts across installed/incomplete receipts. Persist journal first; snapshot all targets before writes. Recheck current target identities immediately before replacement. Stage all payload files to unique same-directory names and verify downloaded stage hash against local known payload.
- [ ] Persist rename intent; for existing targets rename to unique remote-original name, then rename verified stage to target. Never use Delete(target) before replacement. After each network result persist completion; recover ambiguous responses by comparing target/stage/original against expected hashes. Do not remove a remote-original until local and remote recovery state is sound. Parent-directory creation is tracked and empty-directory cleanup is optional, never recursive.
- [ ] Finish with an atomic Installed receipt. Update keeps pre-mod originals and immediate predecessor separately; removed paths are restored through the same journal. Rollback restores predecessor, uninstall restores pre-mod originals. Every deletion needs proven transaction ownership and current hash match. Changed user files stop automatic recovery. If fresh metadata now withdraws a release, refuse resume of new installation and offer rollback; offline rollback/uninstall remain available.
- [ ] After restart, enumerate unfinished journals and inspect remote state on explicit recovery action. Do not auto-write on app startup. Do not apply new-install cancellation cleanup to saved recovery files. Retain completed backups until separate cleanup confirmation.
- [ ] Run GREEN across all mutation boundaries plus Install→Update→Rollback→Uninstall byte-exact restoration; commit `feat: add recoverable mod installation lifecycle`.

### Task 8: Library controller and optional Luma reload

**Files:** Create `ModLibraryController.cs`, `ModLibraryState.cs`, `ModLumaReload.cs`; tests `Mods/ModLibraryControllerTests.cs`, `Mods/ModLumaReloadTests.cs`.

**Interfaces:** Controller offers RefreshGamesAsync, SelectGameAsync, SelectModAsync, PrepareInstallAsync, ConfirmAndEnqueue, Resume, Rollback and Uninstall, and publishes immutable state snapshots. Inject catalog, discovery, verifier/package validator, planner/store and queue boundaries. A monotonic request generation binds results and confirmations to the originating session/game/release.

- [ ] Add RED tests for late catalog completion after game switch, disconnect during preflight, stale cached release, empty list, withdrawn installed release, changed profile, repeated button clicks and recovery without HTTPS. No stale completion may set CanInstall true.
- [ ] Implement cancellation-linked generation checks before every state update and immediately before enqueue. State includes descriptive offline timestamp, busy action, install prohibition reason, local receipt state and queue job ID. Disable install until exact current compatibility, authenticated package verification and required confirmations pass.
- [ ] Luma reload is explicit and enabled only after successful luma-script deployment for the matching selected game/profile. Use existing ConnectAsync, HelloAsync(token), ReloadAsync; never log the token. Coordinate with an already-open Luma tool or report its active connection rather than competing silently for the single-client port. Failure sets a separate reload warning without altering Installed receipt.
- [ ] Test files/PRX packages never call reload, failed deployment never calls reload, token not in errors, reload failure retains receipt and retry is independent. Run GREEN; commit `feat: orchestrate mod library state and script reload`.

### Task 9: Dashboard integration and usable skinned UI

**Files:** Create `ModLibraryForm.cs`, `ModInstallReviewDialog.cs`, `ModConsoleProfileDialog.cs`; modify DashboardForm.cs, AppPaths.cs, ManagerSettings.cs. Tests `Mods/ModLibraryFormTests.cs`, existing Presentation/DashboardFormTests.cs and Config/ManagerSettingsStoreTests.cs.

**Interfaces:** New dashboard tool key `mods`, title `Mod Library`; `AppPaths.ModRoot` points below existing Root. Persist HTTPS catalog origin and selected local profile without exposing a user-editable trusted-key field. Wire one controller per window and session binding; disposing the view cancels browsing but does not erase queued transaction recovery state.

- [ ] Add RED headless/STA tests following the existing UI test collection for single-instance registration, settings migration defaults, session detach, disabled install states and controls at 100/150/200% DPI. Keep old settings backwards-compatible.
- [ ] Build a minimum 960×640 resizable form with a game selector/running-game badge, paginated cover cards, details and installed/recovery tab. Use existing skinned controls and flexible layout panels. Give status text and target paths enough width; no fixed-width truncation of critical install information. Detail text is plain text. Bound decoded image pixel count and dispose superseded bitmaps.
- [ ] Confirmation dialog lists each target and action, replacements, backup location, zone warnings and PRX restart requirement. Explicitly state that PRX installation does not activate plugins or include undeclared Lua files. Link to the existing GoldHEN Manager without editing plugins.ini. Profile dialog requires confirmation on each connection before receipt-based writes; cancellation is read-only.
- [ ] Use the shared transfer display for HTTPS/download and journaled FTP phases. Show recovery buttons only for applicable states, confirmations bound to the exact plan, withdrawal warnings, unavailable versions and separate Luma reload outcomes. Add a leave warning for unsaved UI decisions/in-progress preflight while preserving background job observability.
- [ ] Run GREEN plus UI rendering inspection at supported scales; test keyboard tab order, long titles/paths, absent/broken covers, long descriptions, and switching consoles during an active request. Commit `feat: integrate mod library dashboard tool`.

### Task 10: End-to-end acceptance and handoff

**Files:** Create `tests/PS4Manager.Core.Tests/Mods/ModLibraryEndToEndTests.cs`; update README.md; create `docs/superpowers/plans/2026-09-30-mod-library-client-completion.md`.

- [ ] Build a local HTTP fake and loopback/in-memory FTP scenario that uses real parser, verifier, archive validator, journal store, queue and controller. Test signed compatible install, update, rollback, uninstall, conflict, bad hash/key/archive, title-version change, cancellation and durable recovery. Assert original bytes restored, no forbidden mutations, and no false Installed state.
- [ ] Run focused tests to expose missing integration first; correct production integration only through failing tests. Simulate a foreign ordinary transfer submitted during the backup phase and prove ordering from operation logs.
- [ ] Run `dotnet test PS4Manager.sln --configuration Release` and `dotnet build src/PS4Manager.App/PS4Manager.App.csproj --configuration Release`; fix regressions before reporting success. Run `git diff --check`.
- [ ] Read-only live acceptance: browse published Luma by CUSA00411@1.57; display its cover/description; verify downloaded signature/hash/manifest against the pinned key. Do not alter server releases or the user's PS4. Report separately what cannot be tested until a disposable hardware target is authorized.
- [ ] Document activation boundary, offline limitations, recovery UI, backup retention, profile confirmation and PRX restart. Record exact commands/results, screenshots, remaining hardware gate and current branch/worktree in the completion note.
- [ ] Obtain final independent review through the chosen execution workflow, address actionable findings with tests, then commit verified changes. Preserve server PR boundaries; do not push/merge/release without requested integration authority.

## Self-review and execution decision

Coverage: contract/trust Tasks 1–4; discovery/privacy/profile binding Task 5; safety and persistence Tasks 5–7; offline/UI/Luma Tasks 8–9; integration/security/hardware distinction Task 10. All five Review Focus items have named test cases above. No server or PRX Manager implementation is included.

Recommended execution: Native in this existing session, with a final independent review. The core tasks share tightly coupled trust/journal/queue interfaces; retaining implementation context reduces handoff overhead. Subagent-driven execution remains available if the user prefers per-task independent reviews.

The user explicitly authorized autonomous implementation on 2026-09-30. Proceed natively with TDD and a final independent review, without intermediate approval gates. PRX Manager diagnosis remains deferred.

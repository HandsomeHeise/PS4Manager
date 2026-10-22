# Games and Trainers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship an expandable Games window with a local BO3 offline-Zombies trainer, explicit confirmations, and a checked client ZIP.

**Architecture:** A bundled catalog and typed BO3 provider sit behind a testable controller. A thin Frame4 adapter owns transport and process-bound RPC installation; the existing session, installed-game discovery, dashboard, dialogs, and notifications are reused. No remotely executable trainers or server changes.

**Tech Stack:** Windows, .NET Framework 4.8 (`net48`), C# 7.3, WinForms/UIFramework, xUnit, existing Frame4 HTTP/TCP and FTP clients. No new package dependency.

**Spec:** `docs/superpowers/specs/2026-09-30-games-trainers-design.md` (approved in chat).

## Global Constraints

The following exact requirements are quoted from the approved specification:

- “Die Bedienoberfläche bleibt entsprechend den bestehenden Tools auf Englisch.”
- “CUSA02624 und der im Offset-Katalog als md5-bf1ffcd3 geführte eboot-Build sind der Ausgangspunkt.”
- “Dieser Forschungs-Buildschlüssel ist keine behauptete APP_VER.”
- “Das Öffnen, Auswählen, Suchen und Refresh lesen nur.”
- “Doppelklicks werden nicht eingereiht.”
- “Bei sichtbarem Fenster wird höchstens alle zwei Sekunden eine kleine Zustandsprobe ausgeführt, ohne vollständige Speicherabbilder oder erneute FTP-Katalogabfrage.”
- “Alle drei Aktionen verlangen unmittelbar vorher eine ausdrückliche Bestätigung, dass die lokale Runde pausiert ist.”
- “Beim Schließen wird Godmode nicht automatisch entfernt.”
- “Keine automatische Wiederholung, insbesondere nicht für Vergabe und Ammo-Addition.”
- “Der aktuelle Konsolenzustand wird beim Spezifizieren/Builden nicht verändert.”
- “Keine Accounts oder neue Server-API für Trainer im ersten Stand.”

Also preserve the spec's exclusions: no unlimited-ammo loop, arbitrary weapons, gobblegums, points, online cheats, hooks, auto-activation, VM-wrapper calls, or unrelated Mod Library refactoring. All runtime ownership, consent, pointers and stubs are memory-only. Existing user changes and offset-catalog edits must be preserved.

## Review Focus

1. A reconnect or reused PID arrives while a confirmation/read is pending: invalidate the old operation; never target the new console or overwrite its state (Tasks 2, 6).
2. FTP cannot enumerate one install root, or external SFO metadata is missing: retain Unknown rather than reporting Not installed or hiding the entire catalog (Task 1).
3. Corrupt remote pointers, oversized counts, unterminated names or short reads: stop with a reason using bounded reads, not speculative writes or a full memory scan (Task 3).
4. RPC return values contain unrelated upper bits: decode AL/EAX correctly and retain all pointer bits (Tasks 2, 5).
5. A grant changes inventory before a lost response/cancellation, or ammo initialization fails: report Unknown/Partial, never repeat or restore a stale inventory snapshot (Tasks 5, 6).

---

## Execution workspace and evidence

Reuse the attached clean worktree `C:\Users\BBC\.codex\worktrees\mod-library-server\ScreenshotGallery`; do not switch the user's primary checkout. Baseline specification commit: `c583b26`. All commands and repository-relative paths below are relative to that worktree. Inspect status before execution and preserve intervening edits. Read the approved spec and applicable skills before implementing.

Read-only research source: `E:\Projects\PS4\ps4-offsets\data\CUSA02624\md5-bf1ffcd3`. Private evidence: `.superpowers/artifacts/bo3-trainer-2026-09-30/`. Ship only the necessary profile constants/signatures and evidence summaries; never private transcripts, console addresses, credentials or heap snapshots. Do not commit/push the separate offset repository. Use the ps4-offsets skill when working with the profile; obtain any missing callable evidence rather than guessing.

## File and responsibility map

| Files | Responsibility |
| --- | --- |
| `src/PS4Manager.Core/Games/GameCatalog.cs`, `GameModels.cs` | Immutable catalog, UI-independent states, identity and action/result contracts |
| `src/PS4Manager.Core/Games/InstalledGamesAdapter.cs` | Convert existing discovery evidence into Installed/NotInstalled/Unknown |
| `src/PS4Manager.Core/Mods/ModGameDiscovery.cs`, `ModGameDiscoveryReport.cs` | Add nonbreaking enumeration-completeness report; preserve current public behavior |
| `src/PS4Manager.Core/Games/ITrainerTransport.cs`, `Frame4TrainerTransport.cs`, `RpcReturn.cs` | Bounded Frame4 adapter, lazy process-bound stub, return-width decoding |
| `src/PS4Manager.Core/Frame4/Frame4PacketClient.cs` | Missing wire-level RPC install command |
| `src/PS4Manager.Core/Games/Bo3/Bo3Profile.cs` | Auditable build constants/signatures and supported plain-pistol guards |
| `src/PS4Manager.Core/Games/Bo3/Bo3Trainer.cs`, `Bo3Trainer.Actions.cs`, `Bo3Probe.cs` | Read-only probe, context revalidation, narrowly scoped actions |
| `src/PS4Manager.Core/Games/GamesController.cs` | Catalog selection, session generation, consent, single-flight operations and late results |
| `src/PS4Manager.App/GamesForm.cs`, `GamesForm.Layout.cs`, `GamesDialogs.cs` | Skinned display, timer/lifecycle and cancel-default confirmations |
| `src/PS4Manager.App/DashboardForm.cs` | Games entry, singleton factory, shared notification service |
| `tests/PS4Manager.Core.Tests/Games/*.cs` | Synthetic transport, provider and controller tests |
| `tests/PS4Manager.Core.Tests/Presentation/GamesFormTests.cs`, existing `DashboardFormTests.cs` | STA integration and display tests |
| `docs/testing/games-trainers.md` | Reproducible acceptance evidence and outstanding checks |

Keep preferences in memory for v1: no ManagerSettings schema change. New profiles are compiled into the existing assemblies; packaging allowlist need not grow.

## Shared contracts

Define these in Task 1 using constructors, get-only properties, copied arrays/lists and `IEquatable<TrainerIdentity>`; no records, nullable-reference syntax, `init`, or mutable public collections. The following is the cross-task contract, not a requirement to add unnecessary public classes beyond these units.

```csharp
public enum InstalledState { Unknown, Installed, NotInstalled }
public enum TrainerAction { EnableGodmode, DisableGodmode, RefillAmmo, GiveRk5 }
public enum ActionOutcome { Success, NoChange, Blocked, Cancelled, Partial, Unknown }
public enum GodmodeState { Unknown, Off, OnExternal, OnOwned }

// TrainerIdentity(long generation, string host, int pid, string titleId,
//                 ulong moduleBase, string profileId)
// Properties: Generation, Host, Pid, TitleId, ModuleBase, ProfileId.
// Normalize Host/TitleId before equality; compare all six fields.
// GameEntry(string id, string name, string titleId, string profileId,
//           string mode, bool experimental)
// Properties: Id, Name, TitleId, ProfileId, Mode, Experimental.
// InstalledGame(string titleId, string name, string version,
//               InstalledState state, string reason)
// Properties: TitleId, Name, Version, State, Reason.
// TrainerStatus(TrainerIdentity identity, bool ready, string reason,
//               GodmodeState godmode, string playerName, string weaponName)
// Properties: Identity (may be null), Ready, Reason, Godmode, PlayerName, WeaponName.
// TrainerResult(TrainerIdentity identity, TrainerAction action,
//               ActionOutcome outcome, string message)
// Properties: Identity, Action, Outcome, Message.
// TrainerTarget(long generation, Frame4Endpoint endpoint,
//               Func<bool> isCurrent, CancellationToken sessionToken)
// Properties: Generation, Endpoint, IsCurrent, SessionToken.
// ConfirmationRequest(TrainerIdentity identity, TrainerAction action,
//                     bool needsOfflineConsent, string message)
// Properties: Identity, Action, NeedsOfflineConsent, Message.

public interface IGameTrainer
{
    Task<TrainerStatus> ProbeAsync(TrainerTarget target, CancellationToken ct);
    Task<TrainerResult> ExecuteAsync(TrainerTarget target, TrainerIdentity expected,
        TrainerAction action, CancellationToken ct);
}

public interface IInstalledGames
{
    Task<IReadOnlyList<InstalledGame>> DiscoverAsync(CancellationToken ct);
}
```

`IGameTrainer.ExecuteAsync` is called only after controller consent. It still revalidates independently before every remote mutation; UI enablement is not an authorization boundary. `TrainerTarget.IsCurrent()` and its cancellation token bind every request to the session captured when the operation began, including between RPC calls.

## Task 1: Bundled catalog and honest installed-game discovery

**Files:** Create the three Core/Games catalog/model/adapter files above and `src/PS4Manager.Core/Mods/ModGameDiscoveryReport.cs`; modify `src/PS4Manager.Core/Mods/ModGameDiscovery.cs`; create `tests/PS4Manager.Core.Tests/Games/GameCatalogTests.cs`, `InstalledGamesAdapterTests.cs`.

**Interfaces:**

- Consumes: `ModGameDiscovery.DiscoverAsync(CancellationToken)` and existing SFO parsing/FTP gates.
- Produces: shared contracts above; `GameCatalog.Entries : IReadOnlyList<GameEntry>`; `GameCatalog.Filter(string search, bool onlyInstalled, IReadOnlyList<InstalledGame> installed) : IReadOnlyList<GameEntry>`.
- Produces: `ModGameDiscovery.DiscoverReportAsync(CancellationToken) : Task<ModGameDiscoveryReport>` with `Games : IReadOnlyList<ModGame>` and `EnumerationComplete : bool`; `InstalledGamesAdapter(ModGameDiscovery discovery)` implements `IInstalledGames`.

- [ ] Write catalog tests, beginning with this concrete case:

```csharp
[Fact]
public void Offline_catalog_is_searchable_without_fake_games()
{
    var catalog = new GameCatalog();
    var games = catalog.Filter("black ops", false, new InstalledGame[0]);
    Assert.Single(games);
    Assert.Equal("CUSA02624", games[0].TitleId);
    Assert.Equal("md5-bf1ffcd3", games[0].ProfileId);
    Assert.True(games[0].Experimental);
    Assert.Empty(catalog.Filter("", true, new InstalledGame[0]));
}
```

- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter FullyQualifiedName~GameCatalogTests`; expect missing Games contracts initially, then a catalog assertion failure until implemented.
- [ ] Implement immutable contracts and one BO3 entry. Filter name/Title ID case-insensitively; Only installed includes only positively installed entries. Show “Installation status unavailable” when this filter cannot be populated, with a way to turn it off. Do not conflate empty search matches with disconnected state.
- [ ] Extend discovery with a completeness report without changing the old return type. Both install roots successfully enumerated means absent IDs may be NotInstalled. Any inaccessible root makes absent IDs Unknown; a discovered directory proves Installed even if its SFO cannot be parsed. A 550 on an external root alone cannot prove an unmounted device absent. Existing SFO external fallback remains intact.
- [ ] Test these report-to-state rules as data:

```csharp
[Theory]
[InlineData(true, false, InstalledState.NotInstalled)]
[InlineData(false, false, InstalledState.Unknown)]
[InlineData(false, true, InstalledState.Installed)]
public void Missing_metadata_does_not_invent_absence(
    bool complete, bool found, InstalledState expected)
{
    var entries = found ? new[] { new ModGame { TitleId = "CUSA02624" } }
                        : new ModGame[0];
    var report = new ModGameDiscoveryReport(entries, complete);
    Assert.Equal(expected, InstalledGamesAdapter.Map("CUSA02624", report).State);
}
```

  Add `InstalledGamesAdapter.Map(string titleId, ModGameDiscoveryReport report) : InstalledGame` as the tested conversion seam. FTP fixture cases cover both roots, external fallback, 550, transient failure and cancellation. Preserve errors as Unknown/reason at the adapter boundary, while cancellation propagates.
- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter "FullyQualifiedName~GameCatalogTests|FullyQualifiedName~InstalledGamesAdapterTests|FullyQualifiedName~ModGameDiscovery"`; expect all pass.
- [ ] Commit only these paths: `git add src/PS4Manager.Core/Games src/PS4Manager.Core/Mods/ModGameDiscovery.cs src/PS4Manager.Core/Mods/ModGameDiscoveryReport.cs tests/PS4Manager.Core.Tests/Games`; `git commit -m "feat: add local trainer catalog and discovery states"`.

## Task 2: Process-bound transport and wire-tested RPC installation

**Files:** Modify `src/PS4Manager.Core/Frame4/Frame4PacketClient.cs`, `tests/PS4Manager.Core.Tests/Frame4/Frame4PacketClientTests.cs`; create `src/PS4Manager.Core/Games/ITrainerTransport.cs`, `Frame4TrainerTransport.cs`, `RpcReturn.cs`, `tests/PS4Manager.Core.Tests/Games/TrainerTransportTests.cs`, `FakeTrainerTransport.cs`.

**Interfaces:**

- Consumes: `TrainerTarget`, `TrainerIdentity`; existing `Frame4Client` and `Frame4PacketClient`, `Frame4RpcCall`/`Frame4RpcResult`.
- Produces: `Frame4PacketClient.InstallRpcAsync(Frame4Endpoint endpoint, int pid, CancellationToken ct) : Task<ulong>`.
- Produces the transport below; `Frame4TrainerTransport(Frame4Client http, Frame4PacketClient packets)` implements it. `RpcReturn.AsBool(ulong) : bool`, `AsInt32(ulong) : int` are pure conversions; u64 results stay `ulong`.

```csharp
public interface ITrainerTransport
{
    Task<IReadOnlyList<Frame4Process>> GetProcessesAsync(TrainerTarget target, CancellationToken ct);
    Task<Frame4ProcessInfo> GetProcessInfoAsync(TrainerTarget target, int pid, CancellationToken ct);
    Task<IReadOnlyList<Frame4PrxModule>> GetModulesAsync(TrainerTarget target, int pid, CancellationToken ct);
    Task<byte[]> ReadAsync(TrainerTarget target, int pid, ulong address, uint length, CancellationToken ct);
    Task WriteAsync(TrainerTarget target, TrainerIdentity identity, ulong address, byte[] bytes, CancellationToken ct);
    Task<ulong> CallAsync(TrainerTarget target, TrainerIdentity identity, ulong function,
        ulong[] arguments, CancellationToken ct);
    void Invalidate();
}
```

- [ ] Add install packet test in the existing test class using its `FakePacketTransport` and byte helpers. Response: status `0x80000000`, stub `0x12345000UL`. Expected request bytes:

```csharp
Assert.Equal(new byte[] {
    0xCC,0xBB,0xAA,0xFF, 0x05,0x00,0xAA,0xBD,
    0x04,0x00,0x00,0x00, 0x2A,0x00,0x00,0x00
}, transport.Written.ToArray()); // PID 42
Assert.Equal(0x12345000UL, stub);
```

- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter FullyQualifiedName~Frame4PacketClientTests`; expect missing install method.
- [ ] Implement command `0xBDAA0005`, payload i32 PID, success status then u64 stub. Reuse existing private send/read/status methods. Reject zero PID/stub, bad status, truncated response, cancelled connection; do not mask network faults. Add packet tests for each failure and existing `CallRpcAsync` response PID consistency.
- [ ] Implement bounded transport (per read maximum 4096 bytes, exact response length, checked address arithmetic), at most six integer RPC arguments, and guard target/session immediately before dispatch. Bind endpoint host to `TrainerTarget`, never global debugger configuration. Before installing/reusing stub, verify current PID/Title ID/module through small existing queries. Cache exactly one known stub for an unchanged identity; discard client cache on Invalidate, never free remote stub. Install timeout itself poisons that process RPC session: do not automatically install a second stub; require game restart if no reliable reconciliation is possible.
- [ ] Add width tests and cache tests:

```csharp
[Fact]
public void Rpc_widths_ignore_unspecified_upper_bits()
{
    Assert.False(RpcReturn.AsBool(0x1234567800000000UL));
    Assert.True(RpcReturn.AsBool(0x1234567800000001UL));
    Assert.Equal(1, RpcReturn.AsInt32(0xFEDCBA9800000001UL));
    Assert.Equal(-1, RpcReturn.AsInt32(0x11111111FFFFFFFFUL));
}
// Implementations: (value & 0xffUL) != 0; unchecked((int)(uint)value).
```

  Cache test asserts two calls/one install, new generation invalidates reuse, read-only probe produces zero install/write/call, delayed response for old target never dispatches to a changed host. A cancellation after send is conservatively outcome-unknown, not a safe retry.
- [ ] Implement `FakeTrainerTransport`: in-memory address-to-byte blocks, recorded Reads/Writes/Calls, fake process/module metadata, `TaskCompletionSource` delay points and injectable read/call errors. No network fallback. Provide exact-length enforcement and synthetic addresses above 4 GiB; do not copy real user pointers.
- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter "FullyQualifiedName~Frame4PacketClientTests|FullyQualifiedName~TrainerTransportTests"`; expect all pass. Commit the named files with `feat: add guarded trainer transport and lazy Frame4 RPC`.

## Task 3: Read-only BO3 profile and bounded live context probe

**Files:** Create `src/PS4Manager.Core/Games/Bo3/Bo3Profile.cs`, `Bo3Trainer.cs`, `Bo3Probe.cs`, `tests/PS4Manager.Core.Tests/Games/Bo3ProbeTests.cs`, `Bo3Fixture.cs`.

**Interfaces:**

- Consumes: `ITrainerTransport`, `TrainerTarget`, `TrainerIdentity`.
- Produces: `Bo3Trainer(ITrainerTransport transport) : IGameTrainer`; `ProbeAsync(TrainerTarget, CancellationToken) : Task<TrainerStatus>`.
- Produces internal `Bo3Probe` with Identity, Entity, Client, Flags, Health, CurrentWeapon, Inventory, Clip, Reserve; Entity/Client/CurrentWeapon are `ulong`, Flags `uint`, Health/Clip/Reserve `int`, Inventory a copied `ulong[15]`.
- Produces internal `ReadProbeAsync(TrainerTarget target, CancellationToken ct) : Task<Bo3Probe>` and `RevalidateAsync(TrainerTarget target, Bo3Probe expected, CancellationToken ct) : Task<Bo3Probe>` for action code. Revalidation compares identity plus entity/client/current weapon and inventory relevant to the requested operation; it never returns stale cached pointers.
- Test fixture produces `Bo3Fixture.Create()`, properties `Transport`, `Target`, `Trainer`, `Entity`, `Client`; `PutFlags(uint)`, `PutHealth(int)`, `PutHeldWeapon(string internalName)`, `PutInventory(params string[] internalNames)`, `SetTitleId(string)`, `CorruptSignature(string symbol)`. All use synthetic memory matching documented layout; absent/unsupported names are explicit fixture errors.

- [ ] Write a failing offline probe test:

```csharp
[Fact]
public async Task Probe_never_installs_or_calls_rpc()
{
    var f = Bo3Fixture.Create();
    var status = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
    Assert.True(status.Ready, status.Reason);
    Assert.Equal("CUSA02624", status.Identity.TitleId);
    Assert.Empty(f.Transport.Writes);
    Assert.Empty(f.Transport.Calls);
}
```

- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter FullyQualifiedName~Bo3ProbeTests`; expect missing BO3 implementation.
- [ ] Populate the profile by copying reviewed catalog RVAs and masked AOBs with symbol/evidence comments. Match masks at exact module-relative locations, including AOB offset where present; do not perform a broad live scan. Profile compatibility is code/data-based; APP_VER remains metadata text. Every RPC helper used later needs its own profile signature. No callable may be created from a script wrapper address.

  Required anchors/functions (verify against individual YAML files):

  | Symbol | RVA | Use |
  | --- | --- | --- |
  | g_entities | `0x66EAFE0` | Entity array; stride `0x4F0` |
  | Weapon definition table | `0xE978840` | Loaded pointer table; count source `0xE978834` |
  | BO3_GivePlayerWeaponInternal | `0xAFDEE0` | Narrow plain-pistol grant |
  | BO3_StartingAmmoDeficit | `0x986750` | Start-ammo quantity |
  | BO3_AddWeaponAmmo | `0x9E9460` | Engine ammo addition |
  | BO3_GetWeaponClipCapacity | `0x11921F0` | Clip limit |
  | BO3_GetWeaponReserveCapacity | `0x1192070` | Inventory-aware reserve limit |
  | BO3_GetWeaponAmmoHeadroom | `0x11924E0` | Addition bound |
  | Trainer_GetWeaponClipAmmo | `0x1192E00` | Current clip |
  | Trainer_GetWeaponReserveAmmo | `0x1192F90` | Current reserve |
  | Trainer_TransferReserveToClip | `0x9E93C0` | Fill empty clip from existing reserve |
  | BO3_GetWeaponAlternate | `0x11AF400` | Reject alternate branch |
  | BO3_GetWeaponDualWieldPartner | `0x11AF510` | Reject dual branch |
  | BO3_IsWeaponDualWield | `0x11ABCB0` | AL return only |

- [ ] Implement the read-only probe from process info plus `eboot.bin` module, then small signature/data reads. Entity+`0x250` supplies client; entity+`0x2C8` health; client flags+`0x20`, current packed weapon+`0x2C0`, health mirror+`0x368`, inventory+`0x378` with 15 slots/48-byte stride, reserves+`0x648`, clips+`0x684`, player name+`0x16C44`. `checked(moduleBase + rva)` must relocate once, not add IDA base twice.
- [ ] Keep this task independently compilable by implementing `ExecuteAsync` as a fail-closed result until Tasks 4–5 replace its branches:

```csharp
return Task.FromResult(new TrainerResult(expected, action,
    ActionOutcome.Blocked, "This action is not available."));
```
- [ ] Resolve loaded names `pistol_revolver38_zm` and `pistol_burst_zm`: table count max-index+1 limited to 512; packed low 9 bits select definition; header name pointer+0, definition pointer+`0x18`. Require nonzero aligned readable pointers, strings NUL-terminated within 128 bytes, no overflow or truncated reads, matching health fields and a live player. Read bounded ranges instead of obtaining the enormous full process map. Ambiguous player/mode/definition produces a reason and Ready=false; offline mode additionally needs explicit consent in Task 6.
- [ ] Add fixture cases for wrong Title ID, wrong signature, relocated base, missing/dead player, zero/misaligned client, count=513, unterminated name, short read, changed inventory between reads and pointer above 4 GiB. Confirm every rejection performs no writes/calls. Full table enumeration is only on initial/action probe or invalidated context; two-second status probes reuse resolved definitions only while their pointer/name/count identity still matches.
- [ ] Run BO3 probe tests, then all Games tests: `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter FullyQualifiedName~Games`; expect pass. Commit named files with `feat: add read-only BO3 trainer compatibility probe`.

## Task 4: Explicit Godmode with readback and non-destructive lifetime

**Files:** Create `src/PS4Manager.Core/Games/Bo3/Bo3Trainer.Actions.cs`, `tests/PS4Manager.Core.Tests/Games/Bo3GodmodeTests.cs`; update `Bo3Trainer.cs`.

**Interfaces:**

- Consumes: `ReadProbeAsync`, `RevalidateAsync`, `ITrainerTransport.WriteAsync`, immutable action/result/status contracts.
- Produces: `Bo3Trainer.ExecuteAsync(TrainerTarget target, TrainerIdentity expected, TrainerAction action, CancellationToken ct)` Godmode branches. Other action values return Blocked until Task 5, never dummy success.

- [ ] Write bit-preservation theory (fixture from Task 3):

```csharp
[Theory]
[InlineData(4U, 5U)]
[InlineData(0xA0U, 0xA1U)]
public async Task Enable_changes_only_bit_zero(uint before, uint after)
{
    var f = Bo3Fixture.Create();
    f.PutFlags(before);
    var status = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
    var result = await f.Trainer.ExecuteAsync(f.Target, status.Identity,
        TrainerAction.EnableGodmode, CancellationToken.None);
    Assert.Equal(ActionOutcome.Success, result.Outcome);
    Assert.Equal(after, f.Transport.ReadUInt32(f.Client + 0x20));
    Assert.Single(f.Transport.Writes);
    Assert.Empty(f.Transport.Calls);
}
```

  Add `FakeTrainerTransport.ReadUInt32(ulong address) : uint` for fixture readback.
- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter FullyQualifiedName~Bo3GodmodeTests`; expect Blocked/missing action failure.
- [ ] Implement fresh probe, exact expected identity check, revalidation, then fresh flags read and one write:

```csharp
uint next = action == TrainerAction.EnableGodmode
    ? flags | 1U : flags & ~1U;
// next == flags => NoChange; otherwise guarded WriteAsync and exact readback.
```

  Record ownership only after successful readback in this provider/session/player. Already enabled means OnExternal/NoChange, not newly owned. Explicit confirmed disable may clear bit zero regardless of original owner, with dialog copy stating it will disable existing protection; never do so on disposal. If dispatch/readback fails, report Unknown and retain “may still be enabled.” No snapshot rollback.
- [ ] Add tests for disable-preserves-bits; already-on zero writes; re-open reports OnExternal; dead player/context change after first read blocks; failed readback unknown; session invalidation/disposal causes zero writes; no auto-reapply after round/health/client change. Same PID or same pointer alone never establishes ownership. If a restart reuses all observable fields, do not claim reliable round detection: consent/action revalidation and no recurring writes remain required.
- [ ] Run Godmode tests and Games suite; expect pass. Commit named files with `feat: add explicit BO3 Godmode with verified readback`.

## Task 5: One-shot ammunition and bounded RK5 grant

**Files:** Modify `Bo3Trainer.Actions.cs`, `Bo3Profile.cs`; create `tests/PS4Manager.Core.Tests/Games/Bo3WeaponTests.cs`.

**Interfaces:**

- Consumes: `IGameTrainer.ExecuteAsync`, `ITrainerTransport.CallAsync`, `RpcReturn`, `Bo3Probe` and profile signatures from Tasks 2–4.
- Produces: RefillAmmo and GiveRk5 branches returning Success, NoChange, Blocked, Partial or Unknown with original identity; no retry API or automatic rollback.
- Fixture adds `QueueRpcResult(string symbol, ulong result)`, `FailRpcAfterDispatch(string symbol)`, `ApplyGrantOnCall(string internalName)`, `SetAmmo(int clip, int reserve)` to `Bo3Fixture`; call recorder includes Symbol, Arguments, Identity. Symbol comes from profile lookup, never text supplied by the server.

- [ ] Add duplicate/timeout tests first:

```csharp
[Fact]
public async Task Existing_rk5_is_not_granted_again()
{
    var f = Bo3Fixture.Create();
    f.PutInventory("pistol_revolver38_zm", "pistol_burst_zm");
    var status = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
    var result = await f.Trainer.ExecuteAsync(f.Target, status.Identity,
        TrainerAction.GiveRk5, CancellationToken.None);
    Assert.Equal(ActionOutcome.NoChange, result.Outcome);
    Assert.Empty(f.Transport.Calls);
    Assert.Empty(f.Transport.Writes);
}
```

- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter FullyQualifiedName~Bo3WeaponTests`; expect missing weapon behavior.
- [ ] Implement preflight before any RPC installation. Require loaded supported name, consistent inventory/current weapon, non-packed-modified plain pistol, free slot, no existing RK5 and fewer than two ordinary firearms. Reject ambiguous classification instead of guessing that all 15 slots are normal weapons. Validate profile-backed definition branch guards to exclude offhand, alternate, dual-wield, attachment/special-ammo and TLS-reaching paths. The private grant plan and catalog establish the checked RK5 fields; do not broaden support beyond their evidence. Unknown guard values disable the action.
- [ ] Implement grant sequence with fresh revalidation between calls: `GivePlayerWeaponInternal(entity, client, weapon, 0, 0, 0)` -> decode EAX -> verify exactly one new matching slot and all other slots unchanged -> `StartingAmmoDeficit(client, weapon)` -> bounded nonnegative deficit -> `AddWeaponAmmo(entity, weapon, deficit, 1)`. Derive quantities; never embed runtime weapon IDs 88/89 or assume 15/105 universally. Failure before dispatch is Blocked; missing grant response is Unknown; grant verified followed by ammo failure is Partial. Never delete a granted weapon to recover.
- [ ] Before adding the direct reserve-to-clip call, verify its argument/return types against its caller in the provided IDB: its catalog entry documents behavior but does not specify a complete ABI. Use the IDAPython skill for this read-only check; record its verified calling contract with the profile. If ABI or required plain-pistol branch guards cannot be established, stop this action's implementation and report the missing evidence rather than inventing it. This does not require a live-console write or another binary-matching investigation.
- [ ] Implement refill only for held Bloodhound/RK5 after guard checks. Obtain clip/reserve counts and capacity/headroom using signature-verified helpers; reject negative/overflow/inconsistent capacities. Fill clip through the engine transfer path where reserve already exists, re-read, then add only the remaining nonnegative total deficit using `AddWeaponAmmo(entity, weapon, deficit, 1)` bounded by headroom. Re-read counts and inventory after each mutation. Full state is NoChange with no ammo addition/transfer; definition-based plain-pistol bounds may establish no-op before RPC. A paused but not fully suspended game still needs repeated identity checks. Standalone full refill remains experimentally unverified until Task 8 hardware acceptance.
- [ ] Pin irreversible outcomes with this test shape:

```csharp
[Fact]
public async Task Lost_grant_response_does_not_repeat_grant()
{
    var f = Bo3Fixture.Create();
    f.ApplyGrantOnCall("pistol_burst_zm");
    f.FailRpcAfterDispatch("BO3_GivePlayerWeaponInternal");
    var s = await f.Trainer.ProbeAsync(f.Target, CancellationToken.None);
    var r = await f.Trainer.ExecuteAsync(f.Target, s.Identity,
        TrainerAction.GiveRk5, CancellationToken.None);
    Assert.Equal(ActionOutcome.Unknown, r.Outcome);
    Assert.Single(f.Transport.Calls.Where(c => c.Symbol == "BO3_GivePlayerWeaponInternal"));
    Assert.DoesNotContain(f.Transport.Calls, c => c.Symbol == "BO3_AddWeaponAmmo");
}
```

- [ ] Add cases: gun absent; inventory full/ambiguous; two ordinary firearms; changed held weapon; non-pistol held; already-full ammo; exact refill deficit; refill clip with full reserve; readback mismatch; upper-bit garbage in bool/EAX; changed session between grant/ammo; cancellation after send; successful grant plus failed ammo => Partial. Assert zero mutation retries and unchanged unrelated slots. Use deliberately different fixture caps from 15/105 to prove runtime calculation.
- [ ] Run weapon/Games/Frame4 tests; expect pass. Commit named files with `feat: add guarded one-shot BO3 ammo and RK5 actions`.

## Task 6: Controller serialization, confirmation and stale-result isolation

**Files:** Create `src/PS4Manager.Core/Games/GamesController.cs`, `tests/PS4Manager.Core.Tests/Games/GamesControllerTests.cs`.

**Interfaces:**

- Consumes: `GameCatalog`, `IInstalledGames`, `IGameTrainer`, `TrainerTarget`, `ConfirmationRequest`, result/status contracts.
- Produces: `GamesController(GameCatalog catalog, IGameTrainer trainer, Action<TrainerResult> completed)`; `Attach(TrainerTarget target, IInstalledGames installed)`, `Detach()`, `Select(string gameId)`, `RefreshAsync(bool includeInstalled, CancellationToken ct) : Task`, `RequestAsync(TrainerAction action, Func<ConfirmationRequest, CancellationToken, Task<bool>> confirm, CancellationToken ct) : Task<TrainerResult>`, `Dispose()`.
- Readable state: `Status : TrainerStatus`, `Installed : IReadOnlyList<InstalledGame>`, `SelectedGameId : string`, `Busy : bool`, `event EventHandler Changed`. Events/results carry immutable snapshots; form marshals them to UI thread.

- [ ] Build fake trainer tests with held `TaskCompletionSource` confirmation/probe. Confirm this sequence: start request -> change session generation -> accept old dialog -> assert zero ExecuteAsync calls, new Status unchanged. Second request while pending must return Blocked immediately, not queue.
- [ ] Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter FullyQualifiedName~GamesControllerTests`; expect missing controller.
- [ ] Implement one shared operation gate for refresh and action; try-acquire, no queued duplicate actions. Capture target, identity and operation generation before showing dialog. Keep gate while awaiting user decision. First mutation confirmation includes explicit offline-Zombies declaration; always includes pause confirmation and unsupported-external-trainers notice. Consent is bound to exact session/process/profile identity, not global settings. Re-probe after acceptance, compare expected identity, then execute. If relevant player context changes, invalidate consent. Cancel/default close produce no RPC or writes.

```csharp
// Core gate pattern: contention is a result, never a delayed second mutation.
if (!await gate.WaitAsync(0, ct).ConfigureAwait(false))
    return new TrainerResult(capturedIdentity, action, ActionOutcome.Blocked,
        "Another operation is in progress.");
try
{
    var request = new ConfirmationRequest(capturedIdentity, action,
        needsOfflineConsent, confirmationMessage);
    if (!await confirm(request, ct).ConfigureAwait(false))
        return new TrainerResult(capturedIdentity, action,
            ActionOutcome.Cancelled, "Cancelled.");
    var fresh = await trainer.ProbeAsync(capturedTarget, ct).ConfigureAwait(false);
    if (!capturedTarget.IsCurrent() || !fresh.Ready ||
        !capturedIdentity.Equals(fresh.Identity))
        return new TrainerResult(capturedIdentity, action,
            ActionOutcome.Blocked, "The game session changed. Refresh and try again.");
    return await trainer.ExecuteAsync(capturedTarget, capturedIdentity, action, ct)
        .ConfigureAwait(false);
}
finally { gate.Release(); }
```

  Here `capturedTarget`/`capturedIdentity` are the request's immutable target/status identity; `needsOfflineConsent` compares the saved consent identity, and `confirmationMessage` names the selected action, console and required paused offline mode. Apply result/state publication and the unknown-outcome latch around this guarded sequence. The gate is not a cross-process lock or a guarantee that the game is frozen.
- [ ] Invalidate cancellation/generation on detach/dispose. Never disable Godmode or free remote stubs. If there was active/unknown Godmode, preserve an Unknown/may-still-be-enabled display, not Off. Late completions are reported once with original identity to the completion callback but cannot replace new status/consent. An unknown mutation latches writes off until successful fresh read-only reconciliation; successful reprobe does not automatically retry the action. RPC-install uncertainty requires process restart rather than re-install.
- [ ] Add cancellation before confirm/after send, identity equality with reused PID/new module/new generation, external active Godmode, no reenable after reconnect, delayed install discovery after detach, zero polls during pending confirmation/action, and one callback per dispatched action. Poll success emits Changed only for useful state changes, not notifications.
- [ ] Run controller and all Games tests; expect pass. Commit named files with `feat: coordinate trainer consent and session-safe actions`.

## Task 7: Skinned Games window, dashboard and shared notifications

**Files:** Create `src/PS4Manager.App/GamesForm.cs`, `GamesForm.Layout.cs`, `GamesDialogs.cs`, `tests/PS4Manager.Core.Tests/Presentation/GamesFormTests.cs`; modify `DashboardForm.cs` and `tests/PS4Manager.Core.Tests/Presentation/DashboardFormTests.cs`.

**Interfaces:**

- Consumes: GamesController Task 6 APIs, existing `ToolSessionBinding`, `ToolUiDispatcher`, `ToolWindowRegistry`, `DesktopNotificationService.Show(DesktopNotification)` and `MessageDialog` explicit-index constructor.
- Produces: dashboard tool key `games`, `GamesForm(ManagerSessionController sessions, ManagerSettings settings, DesktopNotificationService notifications)` wired to the dashboard's existing `_sessions`, `_settings`, `_notifications`. Do not create a second session manager.
- `GamesDialogs.CreateAction(ConfirmationRequest request) : MessageDialog`; `GamesDialogs.CreateCloseWarning() : MessageDialog` (internal static methods, test via existing visibility/reflection conventions).

- [ ] Add `games` to DashboardFormTests existing tool theory and verify repeated OpenTool calls yield a single owned GamesForm even disconnected. Add STA test that disconnected form displays BO3, Experimental, disabled actions and reason, with no transport mutations. Run `dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --filter "FullyQualifiedName~GamesFormTests|FullyQualifiedName~DashboardFormTests"`; expect missing Games tool failure.
- [ ] Build two-column layout using current SkinPanel/SkinLabel/SkinButton/CheckEdit and established layout/DPI helpers. Left: search, Only installed, game name/icon/Title ID/status. Right: console, running title, SFO version, Frame4/build status; Experimental text; Godmode observed/ownership state plus explicit enable/disable; Refill ammo; Give RK5; persistent disabled reason; status footer. Scrolling prevents clipped actions at minimum size. Use semantic accessible names and keyboard tab order. Do not turn experimental flags into a color-only status or mark Godmode off when unknown.
- [ ] Bind through ToolSessionBinding. Build `Frame4Endpoint` from `session.Endpoint.Host`, `settings.Frame4.HttpPort`, `Frame4Endpoint.DefaultSocketPort` and `settings.Frame4.UartPort` (current settings have no SocketPort field). Session generation is monotonically incremented on attach/detach. InstalledGamesAdapter receives this session's FTP client/gates. Keep catalog usable when Frame4 is disabled/unavailable; action reasons distinguish those states.
- [ ] Add a visible-only WinForms timer with `Interval = 2000`; call `RefreshAsync(false, ct)` and skip while busy. Attach/manual Refresh invokes installed discovery once. Never reload FTP metadata or full weapon table each tick. Dispose timer/cancellation/binding/controller and local images without remote cleanup writes. Load existing app icon through the console metadata path only on attach/selection; cache per title/session, bound image dimensions/bytes and dispose decoded images. Failure uses neutral local name tile, no web fetch.
- [ ] Implement safe dialogs with explicit indices:

```csharp
return new MessageDialog("Confirm trainer action", request.Message,
    new[] { "Cancel", "I am paused — continue" }, 0, 0, null);
// Close warning:
// new MessageDialog("Godmode may remain enabled", explanation,
//     new[] { "Cancel", "Close and leave enabled" }, 0, 0, null);
```

  Do not use `MessageDialog.Ask` (default Yes) or assume generic Choose has safe indices. Close while action pending must warn of uncertain outcome; cancel/leave may close the UI but must not issue cleanup mutation. A fresh provider on reopen reads actual state as externally owned.
- [ ] Route final result to status and one shared toast using `DesktopNotificationService.Show`; include original console/game and partial/unknown wording. Callback only opens details/Games, never repeats action. Polls and rejected duplicate clicks generate no notification spam. Keep late-result details even when form is closed; dashboard owns service. Prevent UI assignment after disposal.
- [ ] Add STA tests for Cancel default/Enter/Escape/close, unknown Godmode close warning, close-leaves-enabled zero writes, filter without connection, long reason text, action disable during confirmation, theme changes and singleton. Do not claim real DPI rendering from these tests alone.
- [ ] Run presentation, Games, session and notification tests, then full test suite; expect pass. Commit named files with `feat: add Games trainer window and dashboard integration`.

## Task 8: Verification, honest acceptance record and checked ZIP

**Files:** Create `docs/testing/games-trainers.md`; use existing `scripts/package-client.ps1` and `scripts/test-package-client.ps1` unchanged unless a failing test proves a scoped defect.

**Interfaces:**

- Consumes: completed Games window, verified transport/provider/controller tests, existing packaging script `-OutputPath` and fixture test `-FixtureRoot`.
- Produces: release ZIP from Release/net48 allowlisted runtime files, SHA-256, validation record listing Passed/Failed/Not run separately for automated, visual and hardware checks. No declaration that mocks prove hardware safety.

- [ ] Run `git diff --check`, `dotnet build PS4Manager.sln -c Release` and `dotnet test PS4Manager.sln -c Release`; capture actual counts/results. All existing Frame4, session, Mod Library, dashboard and notification tests must stay passing. Diagnose failures before claiming completion.
- [ ] Conduct local visual checks with the frontend-design/computer-use skill as applicable: dark/light, 100/150/200% actual DPI, minimum-size scrolling, long status/player strings, keyboard-only action/Cancel, toast focus behavior. Use the existing UI verification harness if available, otherwise record unavailable display modes as Not run. Opening the disconnected window remains read-only. Do not alter the user's OS display settings without permission.
- [ ] Request separate fresh hardware acceptance, naming one action at a time and waiting for “paused.” Checklist: detect CUSA02624/code profile; Godmode enable then explicitly confirmed disable; both supported held-weapon refills with actual capacities; RK5 grant with eligible inventory, switch/fire, duplicate no-op; reconnect and round change without reapply. Never remove protection unannounced. If user is unavailable, deliver build with all pending hardware items marked Not run, especially the previously untested standalone refill path.
- [ ] Verify packaging fixture in a new scoped directory and create ZIP using unique output path:

```powershell
$gamesArtifacts = Join-Path $env:TEMP ('ps4manager-games-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $gamesArtifacts | Out-Null
& .\scripts\test-package-client.ps1 -FixtureRoot (Join-Path $gamesArtifacts 'fixture')
& .\scripts\package-client.ps1 -OutputPath (Join-Path $gamesArtifacts 'PS4Manager-Games.zip')
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $gamesArtifacts 'PS4Manager-Games.zip')
```

  Expect fixture pass, 14 approved runtime entries and verified per-file hashes, then SHA-256. No real settings, credentials, tests, private evidence or research dumps. Never overwrite an existing user release file. Keep output path for final clickable delivery.
- [ ] Record validation evidence and experimental limits in `docs/testing/games-trainers.md`. Review full diff against spec, run final checks after any fixes, then request one independent final review according to chosen execution method. Do not merge/push/deploy without user request. Commit only acceptance documentation and scoped verified fixes with `docs: record Games trainer acceptance and release evidence`.
- [ ] Deliver ZIP link, concise features, test results and outstanding checks. Call the Games feature implemented only once code/build/tests support that statement; call hardware actions verified only for acceptance actually performed on that build.

## Plan self-review and handoff

- Coverage: catalog/discovery Task 1; protocol Tasks 2–3; Godmode Task 4; weapons Task 5; lifetime/consent Task 6; UI/notifications Task 7; verification/delivery Task 8.
- Review Focus 1 is covered by transport generation and held-confirmation tests; 2 by discovery report fixtures; 3 by bounded probe fixtures; 4 by width and action tests; 5 by lost-response/partial/cancellation tests.
- Shared identity/action/state signatures are defined once above. C# version, Frame4 port fields and cancel-default dialog API have been checked against the repository.
- Pending decision: plan approval and execution choice. Native execution is recommended because these eight tasks share tightly coupled transport/profile/controller interfaces; a final independent review remains required. Subagent-driven execution adds a fresh implementer and reviewer for each task before the final review, at greater context cost.
- Product implementation starts only after approval. This document itself does not change the console or deliver the Games feature.

# Mod Library client — acceptance record

Implementation runs in the attached isolated worktree
`C:\Users\BBC\.codex\worktrees\mod-library-server\ScreenshotGallery`, detached HEAD.
Client base: `292342a`. The existing server PR is unchanged; no push, merge,
publication, production migration or console write is part of this delivery.

## Implemented

Strict catalog parsing, bounded same-origin HTTPS/cache, pinned RSA-SHA256 trust,
independent envelope reconstruction, ZIP/manifest/path validation, console game
discovery, explicitly confirmed local console profiles, immutable backup blobs,
durable journals, shared-queue transaction isolation, install/update/rollback/
uninstall, lost-rename response reconciliation, cancellation and offline recovery.
The dashboard exposes the skinned Mod Library, cover cards, plain-text details,
exact target review and installed/recovery view. PRX activation is separate;
Luma script reload is explicit and never changes installation success.

## Verification

- Core lifecycle tests restore original bytes through install, update, rollback
  and uninstall. Tests cover backup failure, wrong trust/hash, ownership conflicts,
  changed target, missing/denied FTP reads, changed console version, withdrawal,
  unknown staging, lost install/restore rename replies and profile reconfirmation.
- End-to-end fake HTTP plus in-memory FTP uses the real parser, RSA verifier,
  archive validator, receipt store, controller and shared queue. A queued ordinary
  write cannot interleave with the transaction. Restarted offline recovery restores
  the original bytes without catalog access.
- UI controls/layout rendered at simulated 100/150/200% scale. Native DPI switching
  on different physical displays is not claimed by this test.
- Read-only live acceptance on 2026-09-30: published Luma release
  `aa6b9003-f0e2-4ee9-a8b5-f48cf3da73c2`, 244626 ZIP bytes, key
  `503d131c6d8d979e`, SHA-256
  `080f9a26be1edcafa29d5b7a3685147a2cef68008e5dc0b1e78b16e9151db566`.
  Catalog, detail, cover, signature, payload hash and manifest passed; sole target
  `/data/GoldHEN/plugins/Luma.prx`. Published compatibility remains CUSA00411@1.57.
  Description mentions RDR2, but that does not authorize an RDR2 install.
- Live acceptance is explicitly opted in with `PS4_MOD_LIVE_READONLY=1` and test
  filter `ModLiveReadOnlyAcceptanceTests`; routine tests make no live requests.

Final verification: `dotnet test PS4Manager.sln --configuration Release
--no-restore --verbosity quiet` passed **830 tests** (42 Gallery, 96 Ps4, 361 FileManager,
331 Manager). `dotnet build src/PS4Manager.App/PS4Manager.App.csproj
--configuration Release --verbosity quiet` succeeded with **0 warnings / 0 errors**.
`git diff --check` passed. One independent read-only Astra review covered
`292342a..2f8ad69`; its one Critical and six Important findings were fixed in one
TDD pass, with no second review. The only Minor finding is deferred below.

## Final review fixes

All seven findings have a regression observed RED before the fix and GREEN
afterwards; the final full suite is 830/830.

1. Remote-original provenance: `Repeatably_wrong_original_RETR_never_becomes_the_restored_original`
   and `Rollback_of_removed_target_preserves_original_for_predecessor_uninstall`.
   Restoration renames the retained console original, never substitutes an
   unproven local readback. Missing originals fail before remote writes.
2. Canonical alias collisions: `Canonical_alias_collisions_are_rejected` and
   `Canonical_aliases_cannot_appear_twice_in_a_receipt` reject duplicate and
   ancestor/descendant targets across `/data` and `/user/data`.
3. Cancellation publication: `Cancellation_at_plan_publication_abandons_decision_and_allows_navigation`
   and `Cancelled_late_catalog_response_cannot_publish_content`.
4. Shared Resume: `Transaction_owns_gate_and_completion_and_failure_does_not_wedge_queue`
   rejects generic transaction resume; UI coverage additionally proves ordinary
   interrupted transfers retain Resume while mod transactions disable it.
5. Validation extraction retention: `Validation_extractions_are_disposable_not_cumulative`.
   Temporary validation roots are shorter than the original accepted root and
   removed in finally, including predecessor validation.
6. Corrupt cache DTO: `Corrupt_inner_catalog_or_null_record_is_a_cache_miss`;
   additional transport coverage proves a fresh online fetch succeeds.
7. Lossy FTP listing: `Safety_listing_rejects_rows_the_browser_would_drop`.
   A dedicated strict interface propagates through the zone guard to all mod
   target/parent checks; FIFO/socket/malformed rows cannot imply absence.
   The ordinary file browser keeps its existing tolerant parsing.

## Rulings made (chronological)

1. Compression ratio 100 rather than planned 200: matches the authoritative
   server; costs stricter archive acceptance.
2. Invalid cache records are ignored in place, not quarantined: no read-time
   renaming of user data; repeated invalid records incur repeated validation.
3. Node-signed parity bytes are base64 in JSON rather than a binary ZIP:
   signature logic is content-agnostic; this fixture alone does not test archives.
4. Canonical active appmeta SFO plus installed-directory evidence defines game
   version, not base-package metadata: inaccessible data disables installs and
   unusual firmware behavior still needs hardware verification.
5. Target parents must already exist: avoids untracked directory creation;
   missing script folders require explicit creation in File Manager.
6. Preserve partial staging/original/retained files: unknown bytes are not
   silently deleted; costs console disk space and sometimes manual inspection.
7. Fault injection covers network mutations and lost replies rather than the
   inside of atomic journal writes: actual disk-full/power-loss timing is unproven.
8. HTTPS preparation appears in library status, FTP transactions in the shared
   queue: no FTP worker is held by an approval dialog; downloads lack a separate
   shared-queue progress entry.
9. Running state is Unknown without a Frame4 title provider: avoids guessing from
   eboot.bin; user must confirm the game is closed, automatic highlighting deferred.
10. Real-console FTP/power-loss/disk-full acceptance remains a separate gate:
    no disposable target was authorized; simulation cannot prove firmware behavior.

## Deferred minors

Screenshot/video detail previews and receipt-derived installation badges on
catalog cards are not implemented. Installation state remains visible in the
separate installed/recovery tab.

## Hardware gate

### Follow-up: optional GoldHEN activation after installation (2026-09-30)

The user confirmed successful Luma installation, then approved an explicit
post-install activation prompt. This is a consent-based extension to the original
manual-activation scope, not automatic plugin loading. Successful install/resume
offers Activate now / Later for installed PRX payloads. The dialog derives its
game from the receipt, defaults keyboard focus to Later, and is suppressed after
session/profile changes. Later performs no configuration write.

Activation is a separate shared-queue transaction: revalidate the signed receipt
and installed PRX bytes, reject configuration ownership conflicts, preserve a
local GoldHEN backup, upload/read back staging, compare the original again, retain
the remote original by rename, then replace/read back plugins.ini. Only the
receipt's title-ID section is enabled. A safe rename-back is attempted if the
replacement fails; unknown bytes are never overwritten for recovery. Interrupted
activation may require manual inspection of the reported recovery paths. It
never changes the installation receipt or invokes Frame4 PRX loading.

Verification: **24 targeted cases**, **861/861 full Release tests**, app build
with zero warnings/errors. Tests cover consent, decline, install/resume-only
prompts, session changes, queue tracking, original/missing configuration,
idempotency, failed backup/replacement, changed plugin/configuration, ownership
conflicts, unrelated legacy-line preservation, an external edit at the original
rename, and dialog default/receipt game at 100/150/200% layout scales. Rendered
dialog captures were inspected at 100% and 200%. No live PS4 writes were made;
real-console activation and interruption behavior remain unverified.

Independent read-only review found the legacy-normalization and post-rename
verification gaps; both were reproduced RED, fixed and rechecked with no remaining
actionable findings. Release delivery: `PS4Manager-GoldHENActivation.zip` in the
existing client artifacts folder; all 14 runtime entries were checked against the
Release output by size and SHA-256. Prior delivery ZIPs and the running client
were left untouched.

### Follow-up: profile-change dropdown crash (2026-09-30)

Resetting the library after console-profile confirmation cleared game items but
left SkinComboBox's selected index pointing at a removed item. The next paint
threw ArgumentOutOfRangeException. Both game and recovery dropdowns now clear
their selection before replacing items. Three UI regression cases (profile
reset, disconnect, empty recovery list) reproduced the exception before the fix
and pass afterwards, including paint and subsequent game reselection. Full
Release suite: **837/837**; app build: zero warnings/errors.

The reported subsequent Luma target conflict was an existing Incomplete journal,
previously stopped by the version check. No journal was edited or removed and
no console write was performed; the user was directed to resume that existing
transaction from Installed & recovery with the same confirmed console profile.

### Follow-up: closed external games (2026-09-30)

User testing exposed a missing storage-layout case: closed external games had
their SFO at `/system_data/priv/appmeta/external/<CUSA>/param.sfo`, while the
ordinary appmeta path returned FTP 550. Discovery now tracks external-directory
evidence and uses that persistent SFO only when the ordinary path is unavailable.
Invalid/mismatching readable metadata does not silently fall through, and an
internal-only installation cannot use stale external metadata. Missing APP_VER
no longer discards an otherwise validated game name.

Two reproductions were observed RED then GREEN; two additional provenance guards
pass. Full Release suite: **834/834**. Read-only live discovery on the user's PS4
returned all **18 CUSA titles with names and versions** with no game mounted in
the sandbox, including Grand Theft Auto V 1.57 and Red Dead Redemption 2 1.32.
No console writes were performed; this validates discovery, not installation.

No real PS4 file was changed. GoldHEN FTP behavior (especially RETR consistency),
power loss/disk-full timing, and disposable-target install/update/recovery still
need a separately authorized hardware acceptance run. The user's working Luma
plugin is not a test target. Frame4 PRX-load diagnosis remains deferred.

## Deliberate limitations

Target parents must already exist. Unknown partial staging is retained and can
require rollback/manual inspection. Original and retained remote files/backups
consume disk until explicit cleanup. HTTP preparation is shown in the library
status, while FTP mutations appear in the shared transfer queue. Running state
is Unknown unless a title provider is configured; every write requires explicit
closed-game confirmation. Cached metadata is read-only for new installation.

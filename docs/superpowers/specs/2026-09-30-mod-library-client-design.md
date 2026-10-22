# Mod Library client — design

## Intent and approved scope

The user wants the published catalog inside PS4 Manager: discover compatible mods,
view covers and descriptions, validate downloads, and install onto the connected
console with backups and recovery. On 2026-09-30 the user approved the integrated
client scope and leaving plugin activation to the existing GoldHEN Manager.

This specifies the client phase of `2026-09-28-mod-library-server-design.md`.
The deployed server and `server/API.md` are the contract; no server migration or
publication change is part of this work. The public Luma 1.0.0 release currently
contains only Luma.prx and advertises CUSA00411@1.57. Its description is not evidence
of RDR2 release compatibility. Existing console files must never be silently replaced.

Success means a user can select an installed game, inspect an exactly compatible
release, install it with verified recovery data, and uninstall it without losing
the prior file. A failed or interrupted operation remains visibly recoverable.

## Boundaries and reuse

- Add `PS4Manager.Core/Mods` for contract DTOs, catalog/download transport, signature
  and archive validation, game discovery, receipts, and transaction orchestration.
- Add a single-instance `ModLibraryForm` using the existing WinForms skin and
  dashboard tool registry; do not introduce a second UI framework.
- Reuse `ManagerSession`, `ToolSessionBinding`, FTP gates, write-zone policy, and
  the shared transfer queue. Connection changes cancel session-bound work.
- Reuse existing SFO parsing infrastructure, extending it to read APP_VER without
  changing existing title-name/icon fallback behavior. Current TitleInfo does not
  expose a game version and cannot alone authorize installation.
- Reuse the existing Luma connection boundary for an explicit post-install script
  reload. Do not use Frame4 PRX loading as part of installation.
- Keep mod trust separate from application-update trust and admin credentials.

## Discovery and UI

The tool contains a console-game selector, compatible-mod cards, a detail panel,
and an installed/recovery view. Cards show cover, title, author, version, and local
installation state. Details show description, changelog, exact compatibility,
package size, and validated target paths. Preview media use bounded downloads;
unsupported video previews may open in the system browser after explicit action.

Enumerate installed titles through existing console metadata paths. Read title ID
and APP_VER from valid console metadata; missing, conflicting, or unreadable version
data disables installation and shows a specific reason. Never substitute a default
version or derive compatibility from a process name. Re-read metadata without the
browsing cache before any new install/update/reinstall.

Frame4 may highlight a running title when reliable title information is available.
Failure to resolve it must not prevent FTP-based browsing. A name such as eboot.bin
alone is insufficient. Known-running targets require the game to be closed before
installing PRX files. When running state cannot be established, require the user to
confirm the target game is closed; never claim the application verified it.

Use the deployed HTTPS origin by default. Catalog queries send only title ID,
version, and pagination. No console address, receipt, or device identity is sent.
The API exposes only exact-compatible catalogs: do not invent an all-releases API.
Offline catalog display is timestamped and read-only for new installations.
Existing receipt recovery remains available with FTP but without the catalog server.

## Download and trust

Build API paths from validated identifiers and the configured HTTPS origin. Do not
follow arbitrary metadata URLs or redirects to a different origin. Reject HTTP,
embedded credentials, excessive responses, timeouts, and malformed DTOs. HTTP and
FTP work report progress through the manager's existing transfer presentation;
extend its job boundary where needed rather than creating an independent write queue.

Pin the production mod public key obtained through the established authenticated
operator channel and verify its key ID. Never accept a key delivered by the catalog
as its own authority. Tests use a separate fixture key. No private keys enter the app.

Before PS4 writes, verify signature over the canonical envelope from `server/API.md`,
including release/mod IDs, version, package type, size, SHA-256, and sorted exact
compatibility. Independently reconstruct the envelope; do not trust signedEnvelope
text as a replacement for checking the displayed fields. Verify downloaded size and
hash and compare archive manifest against release metadata.

Independently validate ZIP entries and manifest version/type/operations before
extraction. Reject traversal, absolute sources, alternate separators, duplicate or
case-colliding names/targets, device names, Unicode/path ambiguities, symlinks,
encrypted entries, undeclared payloads, and configured count/size/ratio excesses.
Use the server's frozen limits/fixtures as the parity baseline, adding Windows full
path constraints. Extract only into a new controlled staging directory.

Reapply client write-zone rules. Reject forbidden targets; collect existing guarded
zone approval before starting a transaction. Luma-script packages additionally must
match the selected game's profile and approved script roots. Package contents are
data, never local executables or commands to run.

## Installation and recovery

Store cache, transaction journals, immutable backup blobs, and receipts under
LocalAppData/PS4Manager/mods, separated by a local console profile. Profile binding
must be checked on reconnect; an address alone is not proof of console identity.
If identity cannot be established, require explicit profile confirmation before
using its receipts for writes. Do not send this local association to the server.

Acquire an exclusive mod-transaction lease coordinated with the shared transfer
queue so conflicting manager writes cannot interleave. Detect target overlap with
other receipts and incomplete transactions using normalized paths. Another owner's
target blocks installation; offer cancel or uninstall-owner, not overwrite-anyway.

1. Fetch fresh published metadata, recheck console compatibility, verify package.
2. Show targets, existing-file replacements, and required approvals.
3. Persist a prepared transaction with planned operations before any remote mutation.
4. Back up every existing target locally and record original size/hash or explicit
   absence. Fail closed on any read/backup inconsistency before the first PS4 write.
   GoldHEN RETR anomalies must not be mistaken for a verified backup: compare fresh
   remote sizes and repeat reads/hashes, rejecting inconsistent or unsupported reads.
   Size agreement alone is not cryptographic proof of remote content.
5. Upload to transaction-unique sibling staging names and verify using the strongest
   supported remote readback; report the actual verification level, never overstate it.
   Persist operation intent before renames and completion immediately after them.
6. Replace targets through staged renames, preserving recoverable originals. A rename
   ambiguity after disconnect is resolved by inspecting remote state, not blind retry.
7. Atomically finalize the receipt only after every declared operation succeeds.

Any cancellation/disconnect/failure after mutation leaves an Incomplete transaction
and retained backups. Do not erase partial state or claim a multi-file install is
atomic. Resume first reconciles recorded and remote state; it never blindly repeats
an overwrite. Rollback restores prior targets and removes only transaction-created
files proven to belong to that transaction. Unknown remote edits block automatic
recovery and require manual resolution, not destructive guesses.

Update/reinstall use the same transaction engine. Preserve the original pre-mod
backup for eventual uninstall and a separate immediate predecessor for rollback.
Removed targets in an update are restored only after ownership/current-content
checks. Uninstall similarly restores original files or removes owned additions;
user-modified files are surfaced as conflicts. Keep backups until successful
uninstall and explicit cleanup consent. Never recursively delete a game data root.

## Plugin and Luma behavior

Generic files packages copy only declared files. They never silently edit plugins.ini,
create unlisted Lua files, or inject/unload modules in a running game. Luma's current
PRX-only package therefore shows an activation/restart notice with a route to the
existing GoldHEN Manager; it is not presented as a complete scripting setup.

Only luma-script packages may offer a post-install reload through the existing Luma
client after successful deployment. Reload failure is separate from file-install
success; retain receipts and offer retry. PRX replacement always requires a game restart.

## Tests and acceptance

Use fake HTTP, FTP, Frame4, clocks, and receipt stores to test deterministic core
behavior. Add protocol parity fixtures for signatures/manifests and hostile ZIPs.
Test exact compatibility, unknown versions, offline/stale data, withdrawals, wrong
keys/hashes, redirect rejection, limits, conflicts, backup failures, queue exclusion,
console switches, interrupted writes/renames, restart recovery, update ownership,
uninstall restoration, changed remote files, and Luma reload failure.

Run the complete .NET test suite and app build. Verify the form's loading, empty,
offline, failure, progress, cancellation, and recovery states with a disposable local
test environment. Public-server acceptance is read-only against the existing Luma
release; do not create or publish production fixtures.

Hardware acceptance is separately authorized and uses a disposable non-game target
to prove install/update/uninstall/rollback and restoration. Do not overwrite the user's
working Luma installation merely to demonstrate the client. Report missing hardware
verification explicitly; unit tests do not prove GoldHEN's real FTP behavior.

## Explicit exclusions

No server changes, release publication, RDR2 compatibility edits, community uploads,
dependency resolution, automatic updates, silent plugin activation, or PRX hot-loading.
The reported Frame4 PRX-load status is a separate diagnostic task, not a reason to
expand this client's install flow.

## Review state

The user approved the scope in chat and subsequently directed implementation of the
library first, deferring PRX Manager diagnosis. The detailed implementation plan is
the next review artifact; no PRX Manager changes are included.

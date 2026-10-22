# Games trainer acceptance — 2026-09-30

Experimental BO3 offline Zombies, CUSA02624 / research profile md5-bf1ffcd3.
The profile key is not an SFO application version. No automatic online/pause detection is claimed.

## Passed

- Release solution build: 0 warnings, 0 errors.
- Final full solution: 1,016 tests (517 manager, 361 file manager, 96 PS4 core, 42 gallery), no failures or skips. Final Release build: 0 warnings/errors.
- Focused Games/dashboard/session/notification run: 184 tests.
- Controller confirmation and session changes; provider execute-entry checks bind the confirmed player/weapon context.
- Godmode bit preservation, ownership, no cleanup or automatic reapply; ammo/RK5 bounds, typed RPC returns, uncertain/partial outcomes without retry.
- STA safe dialog default/Enter/Escape/close; singleton disconnected catalog; unknown-after-active close warning; no close-time mutation; long reasons, both skins, software scaling 1/1.5/2.
- DrawToBitmap inspection of dark/light layouts, normal/minimum window size: legible wrapping and scroll access. This is a test-host rendering, not a full desktop capture.
- Package fixture: missing files rejected, exclusive destination, 14-file allowlist, per-entry SHA-256 verification, no credential/test files, handles closed.
- Final checked `PS4Manager-Games-reviewed.zip`: 14 runtime files, SHA-256 `8562EBB47C3982FBE024EBA1301B06CC24776E4D1039E2B5C780CDB73D03A5B6`. Supersedes the pre-review package.
- Compiled provider live detection: CUSA02624, matching code signatures, Benutzer1, held RK5, Godmode initially Off.
- User separately approved an enable-only Godmode test while paused. Execute returned Success after readback; user then confirmed no damage from zombie hits. Godmode was deliberately left enabled. This exercised the compiled provider through a local harness, not the complete Games UI click path.
- Independent Astra review: no Critical, four Important, one Minor. All Important findings reproduced RED and fixed GREEN, followed by the 1,016-test full suite:
  - Lost process continuity invalidates RPC; every call verifies stub header and immutable code, stopping on missing/unsupported stub.
  - Reconnect retains a pending full refresh after the old operation releases its slot.
  - Frame4-disabled installation evidence clears on detach and is published only to its captured generation.
  - Aborted dashboard exit does not retain Games close approval.

## Not run / pending

- Actual Windows 150% and 200% DPI, keyboard-only end-to-end operation and Games toast focus behavior on the user's desktop. Software scaling tests do not substitute for actual monitor DPI. OS display settings were not changed.
- Remaining hardware acceptance: explicitly confirmed Godmode disable; standalone Bloodhound and RK5 refills; eligible RK5 grant, switch/fire and duplicate no-op; reconnect/round change without reapply. Do not disable protection without a new explicit request and pause confirmation.
- Minor deferred: Only installed hides the Unknown installation reason behind the generic empty-filter message. Clear the filter to inspect Unknown status.

## Stable-button follow-up

- Automatic polls no longer publish busy transitions or unchanged trainer status. Buttons, focus and availability text remain stable, including when a search redraw happens during a poll.
- One user click may wait for an in-flight read; duplicate clicks are rejected. The original console/player context is captured before waiting and revalidated before confirmation and execution. Reconnect cancels the waiting request.
- Actual actions and manual refresh still disable controls; a newly invalid game session also disables trainer actions. Read-only polling alone no longer causes a close warning.
- Regression tests observed failing before the respective fixes, then passing. Full solution: 1,020 passed (521 manager, 361 file manager, 96 PS4 core, 42 gallery), no failures/skips. Release build: 0 warnings/errors.
- Checked `PS4Manager-Games-stable-buttons.zip`: 14 allowlisted runtime files, per-entry length/SHA-256 verified; archive SHA-256 `50B25C0AD875C162BB74EB69CF78ABB5AB774AB68F5769AFEB88BF11F08E826B`.
- No console access or mutation during this follow-up. Hardware and native-DPI checks above remain pending.

No console mutation was performed during implementation or review. The subsequent authorized acceptance changed only the Godmode bit. Earlier research verified the damage-skip bit against zombie hits in two rounds and RK5 grant/start ammo; those experiments do not prove this build's ammunition actions, every damage source, arbitrary weapons or game-thread safety.

## Implementation decisions and costs

1. Ammo headroom is reserve-only on the constrained plain-pistol path; add missing magazine rounds too, based on IDA evidence. If wrong: readback fails Unknown, without retry.
2. Use a deterministic engine simulation rather than separately named queue helpers. Cost: simulated behavior is not hardware proof; wire tests and pending live acceptance remain necessary.
3. Bind confirmation to an opaque player/weapon/inventory ContextKey. Cost: conservative re-confirmation when context changes.
4. Carry that key through provider execute entry as well. Cost: the same conservative extra confirmation.
5. Keep transport lifetime at dashboard scope so uncertain RPC installation survives Games reopen. Cost: small process identity records persist until application exit.
6. Stream icons with byte/dimension bounds and require PNG. Cost: unsupported icons use the name tile.
7. Source review cannot establish hardware/native-DPI/keyboard acceptance; list pending checks separately. Cost: coverage remains limited and is not claimed.
8. Do not rederive all ABI/signatures in the final review; retain catalog/IDA evidence and Experimental labeling. Cost: residual reverse-engineering uncertainty.
9. Check immutable RPC code and header against [Frame4 851ef1c](https://github.com/DeathRGH/frame4/blob/851ef1c6026ccf7fe04c352d58e7f172dcda3e7f/kdebugger/include/rpcasm.h), whose [installation/call implementation](https://github.com/DeathRGH/frame4/blob/851ef1c6026ccf7fe04c352d58e7f172dcda3e7f/kdebugger/source/hooks.c) distinguishes immutable code from mutable registers. Cost: different stub versions fail closed; network checks cannot atomically prevent a simultaneous process restart or prove a worker thread's health.
10. Return whether Refresh acquired its operation slot, preserving pending discovery for the current generation. Cost: a small internal return-contract change.

## Using the build

Extract all ZIP entries together, launch PS4Manager, connect the console and enable Frame4 in Settings. Open Games from the dashboard. Browse/search works disconnected. Refresh reads installed-game metadata and the running profile; it never installs RPC or changes game values.

Pause a local Zombies round before each action and keep it paused until the result. Do not run another trainer concurrently. Godmode remains enabled on close; disable it only through the explicit action. Unknown/Partial outcomes require manual Refresh, never automatic retry. Give RK5 supports one plain Bloodhound/RK5 plus recognized knife/grenade inventory, free space and no existing RK5. Unsupported states are blocked.

The frontend follows the existing UIFramework skin, with separate catalog/status/action areas, wrapped reasons, and scrolling at small sizes. No external cover service or downloaded trainer code is used. Custom base-weapon textures are a separate follow-up; the supplied BO2/Xbox 360 XAsset/DDS example is not included as a BO3 implementation.

# SDD ledger — plan: docs/superpowers/plans/2026-09-11-ps4-file-manager-kern.md

Spec: docs/superpowers/specs/2026-09-11-ps4-file-manager-design.md (reachable)
Branch: file-manager-kern (created by controller from main 43f25ce.. eedab87; Task 1 Step 1 is a no-op)
Baseline: dotnet test ScreenshotGallery.sln -> 61/61 green (2026-09-11)
User consent: Task 2 (console writes under /data/_fm_probe_*) approved by user on 2026-09-11, console is on.
Parallel: Plan 2 (UIFramework) runs concurrently in worktree E:\Projects\UIFramework-explorer (branch explorer-bausteine); this repo builds against untouched E:\Projects\UIFramework master.

## Pre-flight scan

| Pair / Task | Produces vs consumes | Finding |
|---|---|---|
| T1 -> T3,T4,T6,T7 | T1 moves Ftp/* and SfoReader into Ps4.Core (ns Ps4.Core.Ftp / Ps4.Core.Titles); later tasks edit those files | consistent |
| T1 -> T5,T6,T7 | Ps4.Core.Tests project + TestData/param.sfo; later tasks add test files there | consistent |
| T5 -> T6 | LoopbackFtpServer API (Host, Port, AddDirectory, AddFile, GetFile, DirectoryExists, CommandsSnapshot, CutDataAfterBytes) | all members used by T6 exist |
| T6 -> T6 step5, T7, T9 | IFtpClient 12 members; implemented by gallery FakeFtpClient, StubFtpClient, InMemoryFtpClient, ZoneGuardFtpClient | all four implement all 12 |
| T3 -> T9,T10,T15,T17 | FtpEntry(name, kind, size, modified, linkTarget), FtpEntryKind | consistent |
| T4 -> all | FtpException(message, kind, code[, inner]), IsTransient | consistent |
| T9 -> T10 | InMemoryFtpClient.ListAsync replaced by async version with ListGate | T10 gives full replacement text |
| T9 -> T15 | InMemory StoreAsync keeps partial on FtpException OR OperationCanceledException (needed by T15 cancel test) | present in T9 text |
| T13 -> T14,T15,T16,T17 | TransferStep (incl. internal Started, IsDirectory, OverwriteWithoutAsking), TransferJob, LocalName, RemoteItem, UniqueName | consistent |
| T14 -> T15 | FileOperations steps consumed by queue | consistent |
| T15 -> T16 | TestClock (Support) created in T15, used in T16 | order ok |
| T8 -> T11 | test csproj; T11 adds TestData ItemGroup | consistent |
| T10 -> T11,T14,T16 | FtpGates.RunAsync, gates.Browse | consistent |
| T1 self | step 6 expects CS0103 for TitlePaths before step 7 | consistent |
| T2 self | throwaway probe outside repo; STOP table | consistent; gate for T6 (rename/APPE) and T15 (AppendResumesUploads) |
| T3..T17 self | expected test counts checked: T3 14, T4 22 cases, T6 14, T7 14 cases, T10 8, T12 5, T13 22 cases, T16 11, T17 10 | consistent |
| T18 self | needs user Fahrprobe of gallery | will ask user at T18 |

Rulings (pre-flight): none needed.

## Progress
Task 1: dispatched (base eedab87, sonnet implementer)
Ruling: Task 2 runs concurrently with Task 1 — it touches no repo file (throwaway probe in %TEMP%, results into the git-ignored spec), so it cannot conflict — cost if wrong: none beyond a re-run.
Ruling: Task 2 gets no diff review — it produces no code; the controller checks the probe output against the plan's STOP table instead — cost if wrong: a misread probe result would surface in Task 6 loopback/console behaviour.
Task 2: dispatched (sonnet implementer; user consent recorded above)
Task 1: implementer DONE c81ba30 (61/61, Release 0 warnings); review dispatched (sonnet)
Task 1: review Approved (spec ✅). ⚠️ trailer checked by controller: commit carries "Co-Authored-By: Claude Sonnet 5" + Claude-Session line.
Ruling: subagent commits may name the model that actually wrote them in Co-Authored-By (Session line stays mandatory) — honest attribution beats a copied template line — cost if wrong: cosmetic trailer rewording.
Task 1: minor (deferred): dotnet sln add rewrote ScreenshotGallery.sln with BOM + x64/x86 platform matrix for all projects (tool side effect).
Task 1: minor (deferred, reviewer likely wrong): InternalsVisibleTo("Ps4.Core.Tests") flagged unused — FtpClientTests uses internal FtpClient.BuildUri.
Task 1: complete (commits eedab87..c81ba30, review clean)
Task 2: probe DONE. Controller check vs STOP table: 02-07 OK (APPE appends, SIZE, REST ok), 08/10-11 OK (absolute RenameTo works), 13-14 RNTO overwrites existing file silently, 15 RMD non-empty -> 550, 20 username.dat = "Benutzer1"+8 NUL (17 B), 25-26 cleanup ok. STOP at 16-18: LIST returns C4/FC bytes for Ä/ü.
Task 2: controller spike (throwaway, %TEMP%/ftp-enc): net48 FtpWebRequest sends "OPTS utf8 on"; if the server accepts it encodes paths UTF-8, if it answers 504 (GoldHEN does) it encodes paths with Encoding.Default (Windows-1252 here: Ä=C4 ü=FC €=80). So the console stores what FtpWebRequest sent; FtpClient.ListAsync decoding LIST as UTF-8 is the bug.
Ruling: resolve the 16-18 STOP without parking — FtpClient decodes LIST with Encoding.Default (FtpClient.PathEncoding), the same encoding FtpWebRequest uses for commands after GoldHEN's 504, so every listed name round-trips byte-exact; LoopbackFtpServer (T5) becomes byte-transparent (Latin-1 on control+listing) and gains AddFileWithRawName; T6 gets two round-trip tests (umlaut via upload/list/delete; UTF-8-named file from another tool stays reachable). UTF-8 names written by other tools display as mojibake (deferred display fix for Plan 3); uploads of non-ASCII names land as CP1252 bytes (spec limitation note). The user is informed and may overrule — cost if wrong: non-ASCII names garbled on display; access stays byte-exact and is test-covered.
Ruling: fakes mirror observed GoldHEN RNTO (overwrites an existing file): InMemoryFtpClient.RenameOverwrites defaults to true (T9), LoopbackFtpServer RNTO overwrites an existing file target (T5) — cost if wrong: none; the queue already checks conflicts before renaming.
Task 2: complete (no repo diff; spec Befunde written by probe agent + controller ruling note)
Task 3: dispatched (base c81ba30, sonnet)
Task 3: implementer DONE dd06edb (69/69); review dispatched
Task 3: minor (deferred): no test for FtpEntry 5-arg ctor nulling LinkTarget on non-Link kinds.
Task 3: complete (commits c81ba30..dd06edb, review clean)
Task 4: dispatched (base dd06edb, sonnet)
Task 4: implementer DONE be93d66 (91/91); review dispatched
Task 4: minor (deferred): Classify ignores its WebExceptionStatus parameter (plan-mandated).
Task 4: minor (deferred): IsLocal misses DriveNotFoundException -> would read as "Verbindung unterbrochen".
Task 4: minor (deferred): 421/425/426 duplicated in Classify and Describe.
Task 4: minor (deferred): FtpException without [Serializable] ctor.
Task 4: complete (commits dd06edb..be93d66, review clean)
Task 5: dispatched (base be93d66, sonnet)
Task 5: implementer DONE eaa69f0 (94/94); review dispatched
Task 5: minor (deferred): LoopbackFtpServer.Dispose does not tear down live PASV listeners/sessions (port/task leak in odd scenarios; client timeouts prevent hangs).
Task 5: minor (deferred): SessionAsync catches only IO/ObjectDisposed/Socket — e.g. bad REST arg kills the session silently.
Task 5: minor (deferred): STOR/APPE existence check and write are not atomic across sessions.
Task 5: complete (commits be93d66..eaa69f0, review clean)
Task 6: dispatched (base eaa69f0, sonnet)
Task 6: implementer DONE 87ac29c (110/110, Release 0 warnings); review dispatched (opus: changes the gallery's live FTP client)
Task 6: review Needs fixes (opus). Important A (plan-mandated): download destination FileStream + local writes inside Guard -> locked target / disk full reported as transient connection loss.
Ruling: fix A against the brief — spec contract "lokale Dateifehler unverändert" wins; open local stream before Guard + LocalFileException wrapper for local read/write failures — cost if wrong: ~40 lines more in FtpClient.
Ruling: promote reviewer Minor "OpenRead early Dispose waits up to 30 s / no Abort" to Important — Task 17's FtpComStream closes early on seek/cancel — cost if wrong: small extra code.
Ruling: promote reviewer Minor "AppendFromFileAsync accepts offset beyond local EOF -> empty APPE reported as success" to Important — silent incomplete file — cost if wrong: one guard.
Task 6: minor (deferred): translate-or-rethrow duplicated 3x in FtpClient; umlaut test depends on ANSI code page (no guard/comment); no progress-from-0 test for offset transfers, cancel/refused only via ListAsync, no idle-timeout upload test.
Task 6: fix round 1 dispatched (resume implementer; FIX_BASE 87ac29c)
Task 6: fix round 1/5 (3 addressed, 0 open — local IO errors, OpenRead early abort, append offset guard; commits 87ac29c..2d4af17)
Task 6: minor (deferred): FileStream disposed twice (LocalFileStream + outer using) — harmless.
Task 6: complete (commits eaa69f0..2d4af17, review clean)
Task 7: dispatched (base 2d4af17, sonnet)
Task 7: implementer DONE 6c7bc66 (128/128); review dispatched
Task 7: minor (deferred): TitleId regex uses ^/$ -> "CUSA00411\n" matches; use \A/\z.
Task 7: minor (deferred): no test for transient failure on the icon download or for the ftp==null guard.
Task 7: complete (commits 2d4af17..6c7bc66, review clean)
Task 8: dispatched (base 6c7bc66, sonnet)
Task 8: implementer DONE 6028c7c (150/150); review dispatched
Task 8: ⚠️ resolved by controller: all commits main..HEAD carry the Claude-Session trailer (checked via git log); "Up" is not a NavigationHistory API in the plan — callers use RemotePath.Parent (plan Interfaces), so no gap.
Task 8: minor (deferred): IsStrictlyBelow normalizes ancestor twice; no standalone RemotePath.Root test.
Task 8: complete (commits 6c7bc66..6028c7c, review clean)
Task 9: dispatched (base 6028c7c, sonnet)
Task 9: implementer DONE 64ade89 (206/206, 2x CS0649 in test fake); review dispatched (opus: security core)
Task 9: review FAILED to run (API session limit) — re-dispatched
Task 9: review Approved (opus retry).
Ruling: accept the two CS0649 warnings in InMemoryFtpClient (Failure, InterruptAfterBytes) for now — Task 10/15 tests assign them and the warnings vanish; Task 18 verifies a warning-free build — cost if wrong: a leftover warning caught at T18.
Task 9: minor (deferred): no guard-level test that a Warn-zone write passes through ZoneGuardFtpClient; report's test-count narration muddled (runner count 56 correct).
Task 9: complete (commits 6028c7c..64ade89, review clean)
Task 10: dispatched (base 64ade89, sonnet)
Task 10: implementer DONE_WITH_CONCERNS d006491 (214/214) — deviation: ExecuteSynchronously on the Forget continuation (race reproduced), bounded poll in AtMostTwo test; review dispatched (opus: concurrency)
Task 10: review Approved (opus). ExecuteSynchronously deviation confirmed load-bearing for PRODUCTION (async failure path: 54-279/2000 stale faults without it, 0 with it).
Task 10: minor (deferred, FINAL REVIEW PLEASE TRIAGE): ExecuteSynchronously comment claims it only matters for the sync/test path — reword; add async-path regression test (ListGate + missing dir, release, retry must LIST again).
Task 10: minor (deferred): Forget continuation does not observe t.Exception -> UnobservedTaskException when all waiters cancelled.
Task 10: minor (deferred, plan-mandated): FTP call's synchronous start runs under the cache lock.
Task 10: ⚠️ for Plan 3 handover: DirectoryCache keys are Normalize()d, not Canonical() — /user/data/x and /data/x are separate entries; the app must invalidate with the spelling it listed with (RemoteFoldersToRefresh uses step paths). Waiters that joined before an Invalidate still get the old listing; views must re-request after invalidation.
Task 10: complete (commits 64ade89..d006491, review clean)
Task 11: dispatched (base d006491, sonnet)
Task 11: implementer DONE 9a48b1e (229/229); review dispatched
Task 11: review Approved with Important (plan-mandated): QuickAccessBuilder.ReadUserNameAsync downloads outside the FtpGates budget.
Ruling: fix it — the connection budget (spec "Verbindungslimit") must cover every call; constructor gains FtpGates — cost if wrong: one extra ctor parameter for Plan 3.
Task 11: minor (deferred): TitleCatalog benign duplicate load for the same id under concurrency.
Task 11: fix round 1 dispatched (resume implementer; FIX_BASE 9a48b1e; also UserNameReader comment wording)
Task 11: fix round 1/5 (2 addressed, 0 open — gated username download, comment; commits 9a48b1e..7b75f05)
Task 11: complete (commits d006491..7b75f05, review clean)
Task 12: dispatched (base 7b75f05, sonnet)
Task 12: implementer DONE 7ea1a91 (234/234); review dispatched
Task 12: minor (deferred): Save with a bare filename -> Directory.CreateDirectory("") throws.
Task 12: complete (commits 7b75f05..7ea1a91, review clean)
Task 13: dispatched (base 7ea1a91, sonnet)
Task 13: implementer DONE 1e6c50e (256/256); review dispatched
Task 13: minor (deferred): Summary headline ignores failed folder steps in mixed jobs (still in Failures) — Plan 3 UI should show Failures list.
Task 13: complete (commits 7ea1a91..1e6c50e, review clean)
Task 14: dispatched (base 1e6c50e, sonnet)
Task 14: implementer DONE dea6165 (279/279); review dispatched (opus: zone approval path)
Task 14: review Approved (opus). ⚠️ move-target conflicts are Task 15's job (queue) — check at T15 review.
Task 14: minor (deferred, FINAL REVIEW PLEASE TRIAGE — recommend fix): ValidateName accepts control chars (\n) -> console entry that LIST can never show/delete again; reject char.IsControl.
Task 14: minor (deferred, FINAL REVIEW PLEASE TRIAGE — recommend fix): zone check for folder rename/move/delete relies on the implicit "no zone root nested in another" invariant — add a ZonePolicyTests fact asserting it.
Task 14: minor (deferred): delete/rename/new-folder list the server before the Locked check (slow/odd error for Locked selections; refusal names deepest path) — check items first.
Task 14: minor (deferred): upload walker: dropped drive root "C:\" -> "C:" (cwd of drive), reparse-point loops, sync enumeration before first await.
Task 14: minor (deferred): test gaps (nested upload into Warn/Locked, delete Warn+Locked mix / expert mode, Warn prompts for rename/move/new folder/write-back, link rules, Browse gate); empty input returns empty job instead of null (delete asks "0 files"); duplicated Locked-throw logic.
Task 14: complete (commits 1e6c50e..dea6165, review clean)
Task 15: dispatched (base dea6165, sonnet)
Task 15: implementer DONE 8f7ab92 (299/299, 0 warnings); review dispatched (opus: concurrency)
Task 15: review Needs fixes (opus): I1 throwing JobChanged handler kills worker (jobs stranded); I2 cancel of a resumed-but-waiting job skips discard; I3 Started set before server took over target -> resume APPEs onto old file / cancel DELEs original. RNTO overwrite handling confirmed present.
Ruling: promote Minor "RNTO test cannot fail" to Important — it guards the verified console behaviour (silent overwrite) — cost: 2 small tests.
Ruling: promote Minor "cts.Cancel() under _lock" to Important — Plan 3 resolver cancellation marshals to the UI thread; deadlock risk — cost: few lines.
Task 15: minor (deferred): per-file (not per-job) progress throttle + unthrottled per-step Raise; CancelAsync dual contract; discard outside gates; file/folder name clash handling inconsistent (upload onto dir offers Replace; mkdir onto file "merges"); fresh download deletes a pre-existing user .part; JobChanged doc says worker thread only; RateMeter stale rate after >3 s gap; DoneBytes counts skipped as 0 so progress never 100 % with skips (Task 13 code).
Task 15: fix round 1 dispatched (resume implementer; FIX_BASE 8f7ab92)
Task 15: fix round 1/5 (5 addressed, 1 new open — lost-wakeup race introduced by the try/finally resetting _running; commits 8f7ab92..e636650)
Task 15: minor (deferred): cancelled Waiting job's discard DELE now typically overlaps another job's transfer (outside the transfer gate); STOR accepted + drop before first progress -> cancel leaves truncated file (accepted by semantics).
Task 15: fix round 2 dispatched (resume implementer; FIX_BASE e636650; also stop disposing per-job CTS)
Task 15: fix round 2/5 (1 addressed, 0 open — lost wakeup; commits e636650..e3ad502)
Task 15: minor (deferred, FINAL REVIEW PLEASE TRIAGE): stress test Jobs_enqueued_while_the_worker_drains_are_never_stranded cannot hit the race (awaits WhenIdle) and its comment overstates coverage — rewrite to enqueue b right after a finishes without WhenIdle, or drop the claim; stale ObjectDisposedException comment in CancelAsync (catch now dead code).
Task 15: minor (deferred): exception path can strand jobs until next Enqueue/Resume (near-unreachable, accepted); WhenIdle returns the worker task at call time (pre-existing race for callers).
Task 15: complete (commits dea6165..e3ad502, review clean after 2 fix rounds)
Task 16: dispatched (base e3ad502, sonnet)
Task 16: implementer DONE 85dcd3f (316/316); review dispatched
Task 16: review Approved with Important (plan-mandated): CompleteWriteBackAsync/Discard mutate session fields outside _lock.
Ruling: fix it (lock the writes; + workRoot null-guard) — PendingSessions is read at app exit — cost: a few lines.
Task 16: ⚠️ FileSystemChangeSource untested by design — Fahrprobe belongs to Plan 3 (real editor saving via temp+rename).
Task 16: minor (deferred): session stalls forever in Tick if its working copy is deleted after a change.
Task 16: fix round 1 dispatched (resume implementer; FIX_BASE 85dcd3f)
Task 16: fix round 1/5 (2 addressed, 0 open — locked session writes, workRoot guard; commits 85dcd3f..0aa4ae9)
Task 16: complete (commits e3ad502..0aa4ae9, review clean)
Task 17: dispatched (base 0aa4ae9, sonnet; carry Task 6 finding: FtpClient.OpenRead stream now aborts on early Dispose)
Task 17: implementer DONE 43bb464 (326/326, 0 warnings); review dispatched (opus: COM interop)
Task 17: review Needs fixes (opus). COM layout/IIDs/HRESULTs/ownership confirmed. Important: I1 Explorer calls land on our STA UI thread (brief's "agile" rationale wrong cross-process); I2 abandoned/exact-length streams leak an FTP connection; I3 CopyTo treats ULARGE cb as signed (ULLONG_MAX copies nothing).
Ruling: I1 — fix the false documentation + guard Files re-entrancy now; the threading architecture (dedicated STA for clipboard/drag publishing or read-ahead) is a Plan 3 design item to settle with a thread-ID check in the Fahrprobe — cost if wrong: UI stalls during Explorer copies until Plan 3.
Ruling: I2, I3 fix as reported; promote Minors "HGLOBAL via LocalAlloc (off-contract)" and "Seek accepts unknown origin/negative target" to Important — COM contract correctness, cheap — cost: a few P/Invokes.
Task 17: ⚠️ for Plan 3: FILEDESCRIPTOR write time uses ToFileTimeUtc on listing dates — verify against Explorer in the Fahrprobe (console local time vs UTC).
Task 17: minor (deferred): one name >259 chars fails the whole drop (PathTooLong); duplicated DropEffectCopy constant; tests don't exercise real COM marshaling (GetObjectForIUnknown returns the managed object).
Task 17: fix round 1 dispatched (resume implementer; FIX_BASE 43bb464)
Task 17: fix round 1 interrupted by API session limit (partial uncommitted edit) — implementer resumed at 19:52
Task 17: fix round 1/5 (5 addressed, 0 open — docs+reentrancy guard, stream leaks, unsigned CopyTo, GlobalAlloc, Seek validation; commits 43bb464..df1f6be)
Task 17: minor (deferred, Plan 3 Fahrprobe/threading): exact-size close aborts the pooled FTP control connection (reconnect cost); FtpComStream._inner not synchronized — CloseOpenStreams must be called on the publishing thread (document or Interlocked); early close = one reconnect; ToHGlobal no GlobalFree on copy failure; _openStreams never pruned; RPC_E_CALL_REJECTED from method body does not trigger a retry (message text overstates); re-entrancy testable headlessly via a fake ListAsync that calls GetData; grown files now truncated at listed size; plan brief line 16 still carries the wrong "agile" claim — Plan 3 must follow the code comments.
Task 17: complete (commits 0aa4ae9..df1f6be, review clean)
Task 18: dispatched (base df1f6be, sonnet; steps 1,3,4,5 only — step 2 user drive test and step 6 finishing are controller/human)
Task 18: steps 1,3,4,5 DONE a598fdd (Ps4.Core.Tests 85, ScreenshotGallery.Core.Tests 43, FileManager.Core.Tests 202 = 330; Release 0 warnings). Step 2 (user gallery drive test) and step 6 (finishing) pending with the human.
Final whole-branch review dispatched (opus, eedab87..a598fdd)
Final review (opus): Ready to merge WITH FIXES. No Critical. Must-fix: control chars in names/paths (ValidateName + FtpClient wire guard); zone-root non-nesting invariant test; ExecuteSynchronously comment + async regression test. Gate: gallery drive test (human).
Ruling: also fix in the same wave the gallery regression "failed download leaves a 0-byte file in the user's folder" (live app behaviour changed by Task 6 fix A) and the two stale T15 comments (cheap) — cost: small.
Ruling: final-review Important #2 (case-insensitive conflicts on FAT/exFAT USB), #3 (symlink zone bypass / RemoteItem kind), and recommendations (TransferQueue ctor typed as ZoneGuardFtpClient, dedicated STA for shell publishing, OleSetClipboard(null) at exit, upload-to-.part+RNTO for write-back, EditSession baseline via LastWriteTime/Length, Move-Replace without pre-delete) go to the Plan 3 handover — no app exists yet, Core-only merge exposes nobody — cost if wrong: they must be addressed in Plan 3's first tasks.
Final fix wave dispatched (sonnet; FIX_BASE a598fdd)
Final fix wave DONE (bd7eb0d..a651aed; 89+43+206=338 green, Release 0 warnings); scoped re-review dispatched
Final fix wave re-review (sonnet): all 5 findings RESOLVED, no new breakage. Delete-on-failure only ever hits .part scratch paths (all 3 call sites traced). Out-of-scope notes (no action): FreeRootsForTests exposes the static array via IReadOnlyList cast; FtpClient guard deliberately narrower (C0+DEL) than ValidateName (char.IsControl incl. C1).
Final review: CLEAN. Remaining gate: Task 18 step 2 (human gallery drive test), then finishing-a-development-branch (merge decision = user).
Task 18: step 2 gallery drive test PASSED by the user ("gallery ok", build a651aed)
Task 18: complete (a598fdd; final fixes bd7eb0d..a651aed, final review clean)

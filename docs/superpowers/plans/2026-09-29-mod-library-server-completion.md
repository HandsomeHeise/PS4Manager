# Mod-library server completion record

Server plan: `2026-09-28-mod-library-server.md`. All ten implementation tasks
are present. This is the durable replacement for the temporary SDD workspace.
No production deployment, push, merge or desktop mod-client implementation is
included. The app-managed detached worktree is retained.

## Verification and review

- Clean npm install, formatting, lint and TypeScript build passed.
- Final source suite: 185 passed; the one opt-in deployment test runs separately.
- Fresh Docker API and backup images built successfully.
- Explicit Docker deployment test passed in 52.72 seconds: real HTTPS mod and
  update workflows, downloaded signatures/bytes, ordered audit, separate client
  quotas through Nginx despite forged forwarding headers, backup lock contention,
  offsite-copy stand-in and restore into fresh volumes. Test resources removed.
- Full .NET suite: 676 passed. No .NET source changed afterward.
- The compiled C# UpdateVerifier accepted the frozen Node-signed update as Valid.
- Production dependency audit: zero reported vulnerabilities.
- Final Astra medium whole-branch review covered c623e5c..815183b. Both Important
  findings were reproduced with failing tests and fixed: per-client quotas at
  the Nginx boundary and Windows superscript COM/LPT path aliases. Default app
  construction does not trust forwarding headers. Production trusts only one
  private-network hop; API ports must remain unpublished and its network trusted.
- The path component-length finding was promoted from Minor because signing an
  unextractable package has the same installation impact as the reserved aliases.
  ASCII and UTF-8 overflow cases failed before the 255-byte component bound and
  pass afterward, including a positive exact-boundary test. No deferred minors.
- Earlier reviews fixed runtime log sanitization, ZIP file/directory conflicts,
  artifact reader permissions, TOTP replay, safe framework 4xx responses, login
  form POST fallback and publication rate limits. Self-audit fixed a backup vs.
  orphan-cleanup race with a real storage-lock contention regression.

## Decisions and consequences

1. Internal Nginx serving belongs to deployment Task 9, not Task 10 as one plan
   cross-reference said. Reversing this changes sequencing/documentation only.
2. MySQL DDL implicitly commits: migrations are rerunnable, and their marker is
   recorded only after all statements succeed under one advisory lock. Future
   data migrations need their own explicit recovery strategy.
3. Luma roots match the inspected upstream profiles: `/data/gtalua/scripts` and
   `/data/rdr2lua/scripts`. Upstream changes require a coordinated allowlist change.
4. Game-version components stay 0..999, fractional components have at least two
   digits, outer spaces are trimmed and control whitespace rejected. Changing
   normalization requires a coordinated client contract update.
5. ZIP validation uses lazy strict yauzl iteration and CRC-checks every entry.
   This costs linear I/O instead of trusting headers.
6. Atomic hard-link publication prevents overwriting immutable artifacts. It
   requires staging and final storage on the same hard-link-capable filesystem.
7. Storage uses spec prefixes `packages/mods`, `packages/updates`, and separate
   previews. Consumers must use these keys consistently.
8. Withdrawn release details remain accessible for receipts; lists and downloads
   exclude them. Clients must check state before offering installation.
9. App versions are canonical numeric three-part Int32-compatible values;
   previews use a channel rather than a suffix. Semver suffix support would
   require coordinated changes to the existing System.Version client contract.
10. Update URLs use the immutable hash route, with `{sha256}` substitution in
    the offline signer. Changing it requires updating both URL and signing docs.
11. Production composition moved into Task 9 to enable its real readiness gate.
    This changes the commit boundary, not the public interface.
12. Compose checks inspect resolved graphs and real container behavior. Restore
    keeps MySQL running but refuses API/Nginx/backup writers; operators must use
    the documented restore wrapper and temporary writable-artifact override.
13. Cleanup preserves active draft staging and removes only old unreferenced
    uploads. Long-lived drafts therefore retain disk space.
14. Deployment verification runs explicitly after image builds rather than using
    potentially stale images in the default suite. CI must run both gates.

## Limits that remain outside this server plan

Actual VPS certificate renewal, encrypted remote backup retention and secret
ownership require the operator's deployment environment. The local recovery
test uses disposable keys, TLS and a local offsite stand-in. Real PS4 filesystem
behavior and the desktop client's extraction policy belong to the follow-on
client plan. Production backup sizing, duration and throughput require realistic
datasets and load measurements. The reviewer relied on supplied full-gate
evidence rather than rerunning the complete suite itself.

Use `server/OPERATIONS.md` for deployment/recovery and `server/API.md` for the
frozen client contract.

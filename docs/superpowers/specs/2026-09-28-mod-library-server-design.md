# Online Mod Library and Distribution Server — Design

## Objective

Add a curated online mod library to PS4 Manager. A connected client discovers the
running game and the games installed on the console, reads each title ID and game
version, and offers only mod releases explicitly published for that exact combination.
Every release has a preview, an immutable signed package, and a one-click installation
flow with conflict detection, backup, uninstall, and rollback.

The same Linux-VPS backend also becomes the source of PS4 Manager application updates.
Mods and application updates share storage, download, metadata, and integrity
infrastructure, but remain separate package types with separate signing keys and
installers.

Version 1 is operated by one trusted administrator. It does not accept community
accounts or uploads.

## Scope

Version 1 includes:

- a curated online catalog with a protected administrator panel;
- exact compatibility by title ID and game version;
- cover images, screenshot galleries, and optional short MP4 previews;
- immutable mod releases with changelogs, hashes, and signatures;
- file packages and Luma-script packages;
- detection of both the running game and all installed games;
- download, preflight validation, install, update, reinstall, uninstall, and rollback;
- local installation receipts and safety backups on the Windows client;
- catalog caching without telemetry or device identity;
- stable and preview application-update channels using the existing updater; and
- Docker-based deployment on one Linux VPS with MySQL and local file storage.

Explicitly out of scope for version 1:

- public registration, community uploads, ratings, comments, and download counters;
- inferred compatibility such as "1.57 or newer";
- GoldHEN plugin, cheat, and patch package types;
- mod dependencies, load ordering, and automatic conflict resolution;
- silent installation, automatic mod updates, or installation without a connected PS4;
- storing package archives or preview media in MySQL; and
- collecting console identifiers, installation histories, or per-device analytics.

## System architecture

The server is a modular TypeScript monolith behind Nginx. It is one deployable Node.js
application with internal modules for `catalog`, `packages`, `compatibility`, `updates`,
`admin`, `auth`, `signing`, and `audit`. The administrator UI is served by the same
application. This keeps deployment small while retaining boundaries that can be split
later without changing the public API.

```text
PS4 Manager desktop client
          |
        HTTPS
          |
        Nginx  ---------- immutable ZIP/image/video files
          |
   Node.js/TypeScript API + Admin UI
          |
        MySQL
```

Nginx is the only public container. MySQL has no host port and is reachable only on the
private Compose network. Node authorizes requests and returns content-addressed download
URLs; Nginx serves the bytes without routing large files through the Node process.

The backend lives under a new `server/` directory in this repository so the API contract,
desktop client, updater, and deployment files are versioned together. The existing .NET
solution remains independently buildable.

## Persistent storage

MySQL stores metadata only. Package archives and preview media remain on the VPS file
system. Docker volumes map to a host-controlled persistent root equivalent to:

```text
/var/lib/ps4manager/
├── packages/
│   ├── mods/
│   └── updates/
├── previews/
├── staging/
├── backups/
└── mysql/
```

Uploaded files first enter `staging/`. Publication validates the upload, computes its
SHA-256 hash, moves it atomically into a content-addressed final path, and commits the
database publication in the corresponding workflow. A failed publication leaves no
visible release. A cleanup job removes abandoned staging files.

Backups contain a consistent MySQL dump plus the package and preview trees. The backup
job writes locally first and then copies the completed backup to an independently
configured off-VPS destination. A backup on the same VPS alone is not considered a
recoverable backup.

## Catalog model

The conceptual MySQL entities are:

- `AdminUser`: login identity, password hash, TOTP secret, state, and timestamps;
- `Mod`: stable identity, slug, title, author credit, summary, description, category,
  state, and timestamps;
- `ModMedia`: ordered cover, screenshot, or video metadata for one mod;
- `ModRelease`: immutable mod version, package type, changelog, artifact reference,
  signed release envelope, signing key ID, state, and publication time;
- `ReleaseCompatibility`: one exact title-ID/game-version pair for a release;
- `Artifact`: content type, storage key, byte size, SHA-256, and creation time;
- `AppRelease`: application version, channel, notes, artifact, and publication state; and
- `AuditEntry`: administrator, action, subject, timestamp, and non-secret details.

A mod may have many releases. One release may have many exact compatibility rows:

```text
CUSA00411 + 1.57
CUSA00411 + 1.58
CUSA12345 + 1.00
```

Title IDs are normalized to uppercase and validated structurally. Game versions are
stored and compared as normalized strings, not floating-point numbers. The server never
assumes that compatibility with one version implies compatibility with another.

Published releases and artifacts are immutable. Correcting package bytes, manifest
contents, compatibility, or a changelog requires a new release. An unsafe release is
withdrawn rather than deleted so existing installation receipts remain resolvable.

## Package format

A mod archive is a ZIP with `manifest.json` at its root and payload files under
`payload/`. The initial manifest version is `1`:

```json
{
  "formatVersion": 1,
  "packageType": "files",
  "operations": [
    {
      "source": "payload/config.ini",
      "target": "/data/example/config.ini"
    }
  ]
}
```

Version 1 accepts two package types:

- `files` declares explicit source-to-target copy operations; and
- `luma-script` is a restricted file package that declares its Luma profile and explicit
  script targets. The client deploys its files through the shared FTP transfer queue and
  may issue the existing Luma reload command only after successful deployment.

Every source must name a regular file contained under `payload/`. Every target must be a
normalized absolute PS4 path accepted by the existing client write-zone policy. Free-zone
targets keep the one-click flow, guarded-zone targets require the existing explicit safety
confirmation, and forbidden targets are rejected. A `luma-script` target must additionally
belong to a client-approved script root for its declared Luma profile. Empty segments,
`.`/`..`, alternate separators, duplicate targets, case-colliding names,
symbolic links, undeclared payload files, encrypted entries, and archives exceeding the
configured compressed or expanded size limits are rejected both during publication and
again by the client. ZIP validation also applies limits to entry count and compression
ratio.

The server and client parse the same versioned manifest contract from shared fixtures,
but each implements its own validation. Server acceptance is not trusted as a substitute
for client-side validation.

## Signing and trust

Every artifact always has a SHA-256 digest. A digest detects corruption but does not
establish who published a package, so signatures are mandatory.

Two independent trust roots are used:

- The **mod signing key** is available to the backend signing module and signs only mod
  artifacts after all publication checks pass.
- The **application-update signing key** remains offline on the maintainer's development
  machine. The backend accepts an application release only when its pre-existing
  signature verifies; the private update key is never copied to the VPS.

The desktop client pins the corresponding public keys independently. Signed data includes
the artifact hash, artifact size, package kind, release identity, release version, and
compatibility data so none can be substituted without invalidating the signature. Each
signature carries a key ID. Adding or removing a trusted key requires a signed desktop
application release; a catalog response cannot expand its own trust.

The mod key is mounted read-only for the API service and is not present in the Nginx,
MySQL, or backup-runner containers. Backups of private keys are encrypted separately from
ordinary catalog backups.

## Administrator workflow

The administrator signs in with a strong password and TOTP second factor. Authentication
uses a short-lived secure, HTTP-only, SameSite session cookie. State-changing form and API
requests require CSRF protection. Login and publication endpoints are rate-limited.

Publishing a mod release follows one workflow:

1. Create or edit the mod's descriptive metadata.
2. Upload and order a cover, screenshots, and optional short MP4 preview.
3. Enter the mod version, changelog, package type, and exact compatibility pairs.
4. Upload the ZIP to staging.
5. Validate the archive, manifest, targets, type, sizes, and compatibility.
6. Review a generated page showing all public metadata and every target operation.
7. Publish, which creates the immutable artifact, signs it, and exposes the release.

The admin may withdraw and later restore a release but may not mutate a published one.
Every login-sensitive action, publication, withdrawal, restoration, and application-update
change creates an audit entry. Audit details exclude passwords, TOTP secrets, session
tokens, and private signing material.

Application releases follow a separate screen and endpoint. Their archive, manifest, hash,
and offline signature must already agree before upload can be published.

## Public API

The read-only client API is versioned from its first release:

```text
GET /api/v1/catalog?titleId=CUSA00411&gameVersion=1.57
GET /api/v1/mods/{modId}
GET /api/v1/releases/{releaseId}
GET /api/v1/releases/{releaseId}/download
GET /api/v1/updates/latest?channel=stable
```

Catalog and detail responses use ETags. Lists are paginated and have deterministic sort
orders. Public DTOs never expose host file paths, administrator data, or internal database
identifiers that are not intended as stable API IDs. Artifact URLs are HTTPS and identify
immutable content; a reused URL must never return different bytes.

Admin routes live under `/api/admin/` and are unavailable without an authenticated,
second-factor-complete session. Database models are not serialized directly into either
public or administrator responses.

If the server is unavailable, the client may display its most recently validated catalog
cache with a visible offline timestamp. It may not begin a new installation from stale
metadata. A locally installed mod remains uninstallable without the server because the
client retains its installation receipt, backup, and validated package metadata.

## Game discovery and client UI

The Mod Library is a dashboard tool window. It combines two sources:

- Frame4 identifies the currently running game when a recognizable game process exists.
- The existing FTP/title metadata path enumerates installed games and reads title ID,
  display name, icon, and installed game version.

The running game is highlighted at the top. The full installed-game list remains below it.
If Frame4 is unavailable, installed-game browsing still works. If FTP is unavailable, the
window shows cached descriptive catalog data but disables installation and refresh actions.
The game version used for compatibility always comes from console metadata; a process name
alone never authorizes an install.

Selecting a game requests the exact title-ID/game-version catalog. Mod cards show cover,
title, author, category, release version, and installation state. A detail view shows the
description, screenshot gallery, video preview, changelog, package size, compatibility,
and declared target paths.

The primary action is state-dependent: `Install`, `Update`, `Reinstall`, or `Uninstall`.
Incompatible releases are hidden by default. The user may reveal them for information,
but their install controls remain disabled. Downloads and PS4 writes use the existing
shared transfer queue so progress and cancellation behave like the other manager tools.

## Installation transaction

An install is explicit and recoverable:

1. Re-read the console title ID and game version immediately before installation.
2. Download the immutable artifact to the client's update/mod cache.
3. Verify expected size, SHA-256, pinned-key signature, and signed compatibility data.
4. Extract into a new local staging directory using the archive safety limits.
5. Validate the manifest and target allowlist again.
6. Compare declared targets with local installation receipts and show every conflict.
7. Download each existing target from the PS4 into a new local safety-backup directory.
8. Enqueue the declared PS4 writes through the existing guarded transfer queue.
9. Verify each completed remote file against the strongest verification available to the
   current FTP implementation.
10. Atomically publish the local installation receipt only after every operation succeeds.

The receipt records the mod and release IDs, signed metadata, title ID, game version,
package hash, target paths, whether each target previously existed, previous hashes, backup
paths, installation time, and final state. Receipts and backups live below
`%LocalAppData%\PS4Manager\mods` and are not uploaded.

If a target backup fails, no PS4 write starts. If a transfer fails after writes have begun,
the installation is marked `Incomplete`; the UI offers `Resume` and `Roll back`. Rollback
restores overwritten targets and removes targets that did not exist before the install.
Backups are retained until the mod is successfully uninstalled and the user explicitly
allows their cleanup.

Two installed releases that declare the same target are a conflict. Version 1 never
silently stacks them or invents an order. The conflict dialog may cancel the new install
or direct the user to uninstall the existing owner first; it does not offer an unsafe
"overwrite anyway" path.

For a Luma package, the same validation and receipt rules apply to persistent files. A
reload happens only after file deployment succeeds. Failure to connect or reload does not
discard the receipt or backup; it is reported separately and may be retried.

## Application updates

The backend replaces an arbitrary manifest URL with the versioned update endpoint while
preserving the current desktop updater's responsibilities. `AppRelease` supports `stable`
and `preview` channels. The response contains the version, channel, immutable HTTPS URL,
size, SHA-256, offline signature, notes, and publication time.

The existing desktop verifier still rejects insecure URLs, bad hashes, bad signatures,
channel mismatches, downgrades, and non-newer versions. Mod signing keys cannot authorize
an application update. Publishing or withdrawing a mod has no effect on the application
update feed.

## Privacy and logging

The public API requires no account and receives no persistent device identifier. Version 1
does not report installations, uninstalls, running games, installed-game lists, console
addresses, or console serial data to the server. A catalog request necessarily contains a
title ID and game version; the backend uses them to answer the request but does not build a
device profile.

Operational logs contain request time, route, response status, duration, and the minimum
network information required for abuse and fault diagnosis. Logs have a documented short
retention period and never contain authentication secrets, signatures' private material,
package contents, or admin form bodies.

## Docker deployment

`docker compose` manages four services:

- `api`: the Node.js application and admin UI;
- `mysql`: MySQL with a persistent data volume and no published port;
- `nginx`: HTTPS termination, reverse proxy, and static artifact/media delivery; and
- `backup`: scheduled consistent database dumps and file-tree snapshots.

Only ports 80 and 443 are public. Port 80 redirects to HTTPS and supports certificate
renewal. Runtime secrets are injected at deployment and never copied into images or the
repository. Containers run as non-root users where their function permits it; volumes are
writable only by the service that owns them.

Health checks cover Nginx, API/database readiness, and MySQL. Schema migrations are an
explicit deployment step and run once before the new API container becomes healthy.
Application deployment does not mutate package bytes or preview media.

## Error handling

- Unknown title ID or game version: return a valid empty catalog, not compatible guesses.
- Withdrawn release: hide it from new installs, show its warning to clients that already
  installed it, and keep uninstall metadata available.
- Invalid upload: leave it in non-public staging long enough for diagnostics, then remove
  it through cleanup; never create a visible release.
- Disk full or atomic move failure: abort publication before the database exposes it.
- MySQL unavailable: fail readiness and serve no misleading catalog response.
- Signature or hash mismatch: delete the downloaded client artifact and perform no PS4
  writes.
- Title/version changed between browsing and install: stop at preflight and refresh the
  catalog.
- Backup failure: perform no PS4 writes.
- Interrupted transfer: retain staging, backups, and an incomplete receipt for resume or
  rollback.
- Luma reload failure: keep the completed file installation, report reload failure, and
  offer retry.

## Testing and verification

Server unit tests cover manifest parsing, title/version normalization, exact compatibility,
publication state transitions, signing inputs, withdrawal, pagination, and authorization.
Integration tests run against a real disposable MySQL container and exercise migrations,
transactions, admin sessions, public DTOs, artifact publication, ETags, and update channels.

Security fixtures cover ZIP traversal, absolute ZIP names, mixed separators, case
collisions, duplicate targets, symbolic links, encrypted entries, decompression bombs,
oversized archives, undeclared files, wrong hashes, wrong keys, substituted compatibility,
CSRF, expired sessions, missing TOTP, and rate limits.

Desktop tests use fake API, download, FTP, Frame4, and Luma boundaries. They cover running
and installed-game discovery, exact filtering, stale-cache presentation, preflight
re-checks, backup-before-write, target conflicts, interrupted installs, resume, rollback,
uninstall, and withdrawn-release warnings.

The release gate is:

1. Node formatting, static analysis, unit tests, integration tests, and production build.
2. Existing .NET build and complete test suite.
3. Docker Compose startup from empty volumes, migration, health checks, TLS routing, and
   restore from a generated backup.
4. Staging end-to-end probe: create draft, upload previews and a package, publish, discover
   it for the exact console version, install, update, uninstall, and verify restoration of
   the original remote file.
5. Negative console probe: incompatible version, altered ZIP, target conflict, interrupted
   transfer, and rollback.
6. Separate application-update probe using an offline-signed test update and a disposable
   copy of the PS4 Manager release directory.

## Future extensions

The module boundaries and versioned package/API contracts deliberately leave room for
GoldHEN package types, community submissions with moderation, dependency metadata, mirrors
or object storage, and aggregate statistics. None of these changes the version 1 trust
rule: only an explicitly compatible, correctly signed, locally revalidated release may
write to the console.

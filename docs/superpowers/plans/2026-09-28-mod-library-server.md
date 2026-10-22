# Mod Library Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the deployable Linux-VPS backend that publishes a curated, signed mod catalog and the existing PS4 Manager application-update feed.

**Architecture:** Add a `server/` Node.js/TypeScript modular monolith behind Nginx, backed by MySQL 8.4 and local content-addressed storage. Public catalog/update routes, the authenticated admin UI, package validation, signing, audit logging, Docker deployment, and backup/restore are completed here; the .NET Mod Library client is a separate follow-on plan against the frozen `/api/v1` contract.

**Tech Stack:** Node.js 24 LTS, TypeScript, Fastify, MySQL 8.4, `mysql2`, EJS, `yauzl`, Vitest, Testcontainers, Docker Compose, Nginx.

**Spec:** `docs/superpowers/specs/2026-09-28-mod-library-server-design.md`

## Global Constraints

- The server is a modular TypeScript monolith under `server/`; the existing .NET solution must remain independently buildable.
- MySQL contains metadata only; ZIPs, images, and MP4 files stay in local content-addressed storage.
- Version 1 has one trusted administrator, password plus TOTP, and no public accounts or uploads.
- Compatibility is exact normalized `TITLE_ID + gameVersion`; no range or fallback match is allowed.
- Published releases and artifact bytes are immutable; unsafe releases are withdrawn, never rewritten.
- Mod and application-update trust roots are separate. Only the mod private key may exist on the VPS; the update private key stays offline.
- Every public artifact URL is HTTPS and immutable; the same URL must never return different bytes.
- Privacy is the default: the public API collects no persistent device identifier or installation telemetry.
- Only Nginx publishes host ports. MySQL stays on the private Compose network.
- Package and preview limits are configuration with safe production defaults, and validation occurs before publication.

## File Structure

```text
server/
├── package.json                         scripts and pinned npm dependency graph
├── package-lock.json                    reproducible dependency versions
├── tsconfig.json                        NodeNext strict TypeScript build
├── eslint.config.js                     static checks
├── .prettierrc.json                     formatting rules
├── Dockerfile                           non-root production API image
├── compose.yaml                         API, MySQL, Nginx, and backup services
├── .env.example                         non-secret deployment settings
├── migrations/
│   └── 001-initial.sql                  complete version-1 schema
├── nginx/
│   └── default.conf.template            TLS, proxy, and immutable artifact routes
├── backup/
│   ├── Dockerfile                       MySQL client, tar, and rclone
│   ├── backup.sh                        consistent backup and off-VPS copy
│   └── restore.sh                       explicit restore procedure
├── scripts/
│   ├── create-admin.ts                  password/TOTP bootstrap CLI
│   └── sign-update.ts                   offline application-update signing CLI
├── src/
│   ├── main.ts                          process entry point and shutdown
│   ├── app.ts                           Fastify composition root
│   ├── config.ts                        validated environment configuration
│   ├── clock.ts                         injectable UTC clock
│   ├── logging.ts                       redacted structured request logging
│   ├── maintenance.ts                   expired-session, staging, and orphan cleanup
│   ├── db/
│   │   ├── pool.ts                      mysql2 pool factory
│   │   ├── migrate.ts                   ordered migration runner
│   │   └── transaction.ts               typed transaction helper
│   ├── domain/
│   │   ├── compatibility.ts             title/version normalization and exact keys
│   │   ├── manifest.ts                  version-1 package manifest model/parser
│   │   ├── release-envelope.ts           canonical signed release payload
│   │   └── ids.ts                       UUID creation and validation
│   ├── storage/
│   │   ├── artifact-store.ts            staging, hashing, atomic publication
│   │   ├── media-validator.ts            JPEG/PNG/MP4 signature and size checks
│   │   └── zip-validator.ts              streaming safe ZIP inspection
│   ├── catalog/
│   │   ├── catalog-repository.ts         mod/release/media/compatibility SQL
│   │   ├── catalog-service.ts            catalog and detail DTO assembly
│   │   └── catalog-routes.ts             public `/api/v1` catalog routes
│   ├── publishing/
│   │   ├── rsa-signer.ts                 RSA-SHA256 mod signatures
│   │   ├── mod-publisher.ts              draft validation and immutable publish
│   │   └── withdrawal-service.ts         withdraw/restore and audit
│   ├── auth/
│   │   ├── password.ts                   built-in scrypt password format
│   │   ├── totp.ts                       RFC 6238 verification
│   │   ├── secret-box.ts                 AES-256-GCM TOTP-secret protection
│   │   ├── session-repository.ts          hashed admin sessions
│   │   └── auth-plugin.ts                cookies, CSRF, rate limits, guards
│   ├── admin/
│   │   ├── admin-repository.ts            draft CRUD and audit SQL
│   │   ├── admin-routes.ts                login, draft, publish, withdraw, update routes
│   │   └── views/                          EJS login, list, edit, and review pages
│   ├── updates/
│   │   ├── update-manifest.ts             current .NET manifest contract
│   │   ├── update-repository.ts           app-release SQL
│   │   ├── update-service.ts              offline-signature verification/publication
│   │   └── update-routes.ts               `/api/v1/updates/latest`
│   └── shared/
│       ├── audit-repository.ts             append-only audit writes
│       ├── http-errors.ts                  stable HTTP error mapping
│       └── etag.ts                         deterministic strong ETags
└── test/
    ├── fixtures/                           valid and hostile ZIP/media fixtures
    ├── helpers/                            Fastify, temp storage, key, and MySQL helpers
    ├── unit/                               pure domain/storage/auth tests
    └── integration/                        MySQL and complete HTTP flows
```

## Review Focus

- Archive entry names that are harmless on Linux but collide on Windows or PS4 must be rejected by `zip-validator.test.ts` before any final artifact exists.
- A release request whose compatibility order differs must produce the same signed payload; `release-envelope.test.ts` pins sorting and newline encoding.
- A database failure after the artifact rename must leave no visible release and must be recoverable by orphan cleanup; `mod-publisher.integration.test.ts` injects that failure.
- Expired/replayed admin cookies or a valid cookie with the wrong CSRF token must never mutate state; `auth.integration.test.ts` exercises both conditions.
- Update publication with a valid signature over a different URL, notes field, channel, or hash must fail; `updates.integration.test.ts` mutates each signed field.

---

### Task 1: Server workspace and validated process boundary

**Files:**
- Create: `server/package.json`
- Create: `server/package-lock.json`
- Create: `server/tsconfig.json`
- Create: `server/eslint.config.js`
- Create: `server/.prettierrc.json`
- Create: `server/src/config.ts`
- Create: `server/src/clock.ts`
- Create: `server/src/logging.ts`
- Create: `server/src/app.ts`
- Create: `server/src/main.ts`
- Create: `server/test/unit/config.test.ts`
- Create: `server/test/unit/logging.test.ts`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `loadConfig(env: NodeJS.ProcessEnv): AppConfig`
- Produces: `buildApp(deps: AppDependencies): Promise<FastifyInstance>`
- Produces: `Clock` with `now(): Date` and `SystemClock`

- [ ] **Step 1: Initialize the Node workspace and install the fixed tool set**

Run from the repository root:

```powershell
New-Item -ItemType Directory -Path server | Out-Null
Set-Location server
npm init -y
npm install fastify @fastify/cookie @fastify/formbody @fastify/multipart @fastify/rate-limit @fastify/view ejs mysql2 yauzl
npm install --save-dev typescript tsx vitest @types/node @types/ejs @types/yauzl eslint @eslint/js typescript-eslint prettier testcontainers
```

Edit `package.json` to set `private: true`, `type: "module"`, engine `node: ">=24 <25"`, and these scripts:

```json
{
  "scripts": {
    "build": "tsc -p tsconfig.json",
    "dev": "tsx watch src/main.ts",
    "start": "node dist/main.js",
    "lint": "eslint .",
    "format:check": "prettier --check .",
    "format": "prettier --write .",
    "test": "vitest run",
    "test:unit": "vitest run test/unit",
    "test:integration": "vitest run test/integration",
    "admin:create": "tsx scripts/create-admin.ts",
    "update:sign": "tsx scripts/sign-update.ts"
  }
}
```

- [ ] **Step 2: Write failing configuration tests**

```ts
import { describe, expect, it } from "vitest";
import { loadConfig } from "../../src/config.js";

const valid = {
  NODE_ENV: "test",
  HOST: "127.0.0.1",
  PORT: "3100",
  PUBLIC_BASE_URL: "https://mods.example.test",
  MYSQL_HOST: "mysql",
  MYSQL_PORT: "3306",
  MYSQL_DATABASE: "ps4manager",
  MYSQL_USER: "ps4manager",
  MYSQL_PASSWORD_FILE: "/run/secrets/mysql_password",
  STORAGE_ROOT: "/var/lib/ps4manager",
  SESSION_SECRET_FILE: "/run/secrets/session_secret",
  TOTP_ENCRYPTION_KEY_FILE: "/run/secrets/totp_key",
  MOD_PRIVATE_KEY_FILE: "/run/secrets/mod_private_key",
  UPDATE_PUBLIC_KEY_FILE: "/run/secrets/update_public_key"
};

describe("loadConfig", () => {
  it("accepts an HTTPS production origin", () => {
    expect(loadConfig(valid).publicBaseUrl.href).toBe("https://mods.example.test/");
  });

  it("rejects a non-HTTPS production origin", () => {
    expect(() => loadConfig({ ...valid, NODE_ENV: "production", PUBLIC_BASE_URL: "http://mods.test" }))
      .toThrow("PUBLIC_BASE_URL must use HTTPS");
  });

  it("rejects an invalid port instead of silently defaulting", () => {
    expect(() => loadConfig({ ...valid, PORT: "70000" })).toThrow("PORT");
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `npm test -- test/unit/config.test.ts`

Expected: FAIL because `src/config.ts` does not exist.

- [ ] **Step 4: Implement the configuration, clock, app shell, and shutdown**

`AppConfig` must contain `environment`, `host`, `port`, `publicBaseUrl`, MySQL connection fields, `storageRoot`, file paths for all four secrets, `sessionTtlMinutes` (default `30`), `maxPackageBytes` (default `2147483648`), `maxExpandedBytes` (default `4294967296`), `maxZipEntries` (default `10000`), `maxPreviewBytes` (default `52428800`), and `logRetentionDays` (default `14`). Numeric parsing rejects NaN, zero, negatives, and values above each field's documented maximum.

`buildApp` initially registers only `GET /health/live -> {"status":"ok"}` and leaves database readiness for Task 2. `logging.ts` configures Fastify/Pino to omit request bodies and query strings and redact `authorization`, `cookie`, `set-cookie`, passwords, TOTP values, CSRF values, and every configured secret-file value. Its unit test feeds each sensitive field and asserts none appears in serialized output. `main.ts` loads configuration, builds the app, listens, and closes Fastify on `SIGINT` or `SIGTERM` without swallowing startup errors.

Add to `.gitignore`:

```text
server/node_modules/
server/dist/
server/.env
server/coverage/
server/.test-storage/
```

- [ ] **Step 5: Verify the workspace**

Run:

```powershell
npm run format
npm run lint
npm run build
npm test -- test/unit/config.test.ts test/unit/logging.test.ts
```

Expected: all four commands exit `0`; configuration and redaction tests pass.

- [ ] **Step 6: Commit**

```powershell
git add .gitignore server
git commit -m "Add mod library server workspace"
```

### Task 2: MySQL schema, migrations, and transaction boundary

**Files:**
- Create: `server/migrations/001-initial.sql`
- Create: `server/src/db/pool.ts`
- Create: `server/src/db/migrate.ts`
- Create: `server/src/db/transaction.ts`
- Create: `server/test/helpers/mysql.ts`
- Create: `server/test/integration/migrations.integration.test.ts`
- Modify: `server/src/app.ts`

**Interfaces:**
- Consumes: `AppConfig.database`
- Produces: `createPool(config: DatabaseConfig): Pool`
- Produces: `runMigrations(pool: Pool, directory: string): Promise<void>`
- Produces: `withTransaction<T>(pool: Pool, work: (connection: PoolConnection) => Promise<T>): Promise<T>`

- [ ] **Step 1: Write the failing migration integration test**

The helper starts `mysql:8.4`, creates a pool, and disposes both in `afterAll`.

```ts
it("creates the complete version-one schema exactly once", async () => {
  await runMigrations(pool, migrationsDirectory);
  await runMigrations(pool, migrationsDirectory);

  const [rows] = await pool.query<{ TABLE_NAME: string }[]>(
    "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()"
  );
  expect(rows.map(row => row.TABLE_NAME).sort()).toEqual([
    "admin_sessions", "admin_users", "app_releases", "artifacts", "audit_entries",
    "mod_media", "mod_releases", "mods", "release_compatibility", "schema_migrations"
  ]);
});

it("rolls back all statements when work throws", async () => {
  await expect(withTransaction(pool, async connection => {
    await connection.execute("INSERT INTO mods (id, slug, title, author, summary, description, category, state) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
      ["11111111-1111-4111-8111-111111111111", "rollback", "Rollback", "A", "S", "D", "misc", "draft"]);
    throw new Error("inject rollback");
  })).rejects.toThrow("inject rollback");
  const [rows] = await pool.query("SELECT id FROM mods WHERE slug = 'rollback'");
  expect(rows).toHaveLength(0);
});
```

- [ ] **Step 2: Run the integration test to verify it fails**

Run: `npm test -- test/integration/migrations.integration.test.ts`

Expected: FAIL because the migration runner and schema do not exist.

- [ ] **Step 3: Create the initial schema**

`001-initial.sql` creates the ten tables listed by the test using `utf8mb4_0900_ai_ci`, InnoDB, UTC timestamp columns, and `CHAR(36)` UUID primary keys. Required constraints:

- unique `mods.slug`;
- unique `artifacts.sha256` plus `storage_key`;
- unique `(mod_id, version)` on `mod_releases`;
- unique `(release_id, title_id, game_version)` on `release_compatibility`;
- unique `(channel, version)` on `app_releases`;
- unique `admin_users.username`;
- primary `admin_sessions.token_hash`, expiry index, and foreign key to admin user;
- ordered `mod_media.position` unique per mod;
- foreign keys use `RESTRICT` for immutable published data and `CASCADE` only for draft-owned child rows.

Store `totp_secret_encrypted` as `VARBINARY(512)`, password hashes as `VARCHAR(255)`, hashes as `CHAR(64)`, signatures and signed envelopes as `TEXT`, and audit details as `JSON`.

- [ ] **Step 4: Implement ordered migration and transaction helpers**

`runMigrations` creates `schema_migrations(name VARCHAR(255) PRIMARY KEY, applied_utc DATETIME(6))`, takes MySQL advisory lock `GET_LOCK('ps4manager-migrations', 30)`, applies lexically sorted `*.sql` files not already recorded, records each file only after success, and always releases the advisory lock. `withTransaction` begins, commits on success, rolls back on failure, and always releases the connection.

Add `/health/ready`: run `SELECT 1`; return `200 {"status":"ready"}` on success and `503 {"status":"unavailable"}` on failure.

- [ ] **Step 5: Run migration and workspace checks**

Run:

```powershell
npm test -- test/integration/migrations.integration.test.ts
npm run lint
npm run build
```

Expected: migration tests pass; lint and build exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add server
git commit -m "Add mod library MySQL schema"
```

### Task 3: Compatibility, manifest, and hostile ZIP validation

**Files:**
- Create: `server/src/domain/compatibility.ts`
- Create: `server/src/domain/manifest.ts`
- Create: `server/src/storage/zip-validator.ts`
- Create: `server/test/unit/compatibility.test.ts`
- Create: `server/test/unit/manifest.test.ts`
- Create: `server/test/unit/zip-validator.test.ts`
- Create: `server/test/fixtures/build-zips.ts`

**Interfaces:**
- Produces: `normalizeTitleId(value: string): string`
- Produces: `normalizeGameVersion(value: string): string`
- Produces: `compatibilityKey(titleId: string, gameVersion: string): string`
- Produces: `parseManifest(json: string): PackageManifest`
- Produces: `validatePackageZip(path: string, limits: ZipLimits): Promise<ValidatedPackage>`

- [ ] **Step 1: Write failing normalization and manifest tests**

```ts
it.each([["cusa00411", "CUSA00411"], [" CUSA00411 ", "CUSA00411"]])
  ("normalizes a title id", (input, expected) => expect(normalizeTitleId(input)).toBe(expected));

it.each(["CUSA0411", "CUSA0041X", "CUSA00411\n", ""])
  ("rejects malformed title id %j", input => expect(() => normalizeTitleId(input)).toThrow());

it.each([["01.57", "1.57"], ["1.0", "1.00"], [" 1.29 ", "1.29"]])
  ("normalizes a game version", (input, expected) => expect(normalizeGameVersion(input)).toBe(expected));

it("parses an exact files manifest", () => {
  const result = parseManifest(JSON.stringify({
    formatVersion: 1,
    packageType: "files",
    operations: [{ source: "payload/config.ini", target: "/data/example/config.ini" }]
  }));
  expect(result.operations[0].target).toBe("/data/example/config.ini");
});
```

Normalization preserves two decimal digits for the fractional game version, rejects signs, exponents, embedded whitespace, more than two components, and values outside `0..999` per component. Thus `1.0` becomes `1.00`, while `01.57` becomes `1.57`.

- [ ] **Step 2: Write failing hostile archive tests**

Generate fixtures in the test temp directory and assert rejection of:

```ts
const rejected = [
  "../escape.lua", "/absolute.lua", "payload\\windows.lua", "payload/./dot.lua",
  "payload/a/../escape.lua", "payload/Foo.lua|payload/foo.lua", "payload/link.lua",
  "payload/secret.lua|encrypted", "payload/undeclared.lua", "payload/bomb.bin|ratio"
];
```

Also assert rejection for duplicate manifest targets, more than `maxEntries`, total expanded bytes above `maxExpandedBytes`, compressed bytes above `maxPackageBytes`, and missing `manifest.json`. A valid fixture must return the parsed manifest plus regular payload entries without extracting them.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `npm test -- test/unit/compatibility.test.ts test/unit/manifest.test.ts test/unit/zip-validator.test.ts`

Expected: FAIL because the domain and ZIP validators do not exist.

- [ ] **Step 4: Implement strict parsing and lazy ZIP inspection**

Use `yauzl.open(path, { lazyEntries: true, autoClose: true })`. Reject an entry before opening its stream when its raw name is absolute, contains backslashes, contains an empty/`.`/`..` segment, has a trailing slash but is declared as a file, is encrypted, is a Unix symbolic link, or case-folds to a prior name. Accumulate entry count, compressed size, uncompressed size, and compression ratio with checked arithmetic.

Read `manifest.json` with a one-megabyte hard cap. Require exactly `formatVersion`, `packageType`, `operations`, plus `lumaProfile` only for `luma-script`. Each operation has exactly `source` and `target`; sources must be unique regular files under `payload/`, targets must be canonical absolute forward-slash paths, and every payload file must be declared once. Allowed Luma profiles in version 1 are `gta5` and `rdr2`.

- [ ] **Step 5: Run focused and full server tests**

Run:

```powershell
npm test -- test/unit/compatibility.test.ts test/unit/manifest.test.ts test/unit/zip-validator.test.ts
npm test
npm run build
```

Expected: all commands exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add server
git commit -m "Validate mod package archives"
```

### Task 4: Content-addressed storage, media validation, and release signatures

**Files:**
- Create: `server/src/domain/ids.ts`
- Create: `server/src/domain/release-envelope.ts`
- Create: `server/src/storage/artifact-store.ts`
- Create: `server/src/storage/media-validator.ts`
- Create: `server/src/publishing/rsa-signer.ts`
- Create: `server/test/helpers/keys.ts`
- Create: `server/test/unit/release-envelope.test.ts`
- Create: `server/test/unit/artifact-store.test.ts`
- Create: `server/test/unit/media-validator.test.ts`

**Interfaces:**
- Produces: `canonicalReleasePayload(input: ReleaseEnvelopeInput): Buffer`
- Produces: `RsaSigner.sign(payload: Buffer): Promise<{ keyId: string; signatureBase64: string }>`
- Produces: `ArtifactStore.stage(stream: Readable, originalName: string, maxBytes: number): Promise<StagedArtifact>`
- Produces: `ArtifactStore.publish(staged: StagedArtifact, kind: "mods" | "updates" | "previews"): Promise<StoredArtifact>`
- Produces: `validatePreview(path: string, declaredType: string, maxBytes: number): Promise<PreviewMetadata>`

- [ ] **Step 1: Write failing canonical-signature tests**

```ts
it("sorts compatibility before signing", () => {
  const first = canonicalReleasePayload({
    releaseId: "11111111-1111-4111-8111-111111111111",
    modId: "22222222-2222-4222-8222-222222222222",
    version: "2.0.0", packageType: "files", size: 5,
    sha256: "a".repeat(64),
    compatibility: [
      { titleId: "CUSA00411", gameVersion: "1.58" },
      { titleId: "CUSA00411", gameVersion: "1.57" }
    ]
  });
  const second = canonicalReleasePayload({
    releaseId: "11111111-1111-4111-8111-111111111111",
    modId: "22222222-2222-4222-8222-222222222222",
    version: "2.0.0", packageType: "files", size: 5,
    sha256: "a".repeat(64),
    compatibility: [
      { titleId: "CUSA00411", gameVersion: "1.57" },
      { titleId: "CUSA00411", gameVersion: "1.58" }
    ]
  });
  expect(first.equals(second)).toBe(true);
  expect(first.toString("utf8").endsWith("\n")).toBe(true);
});
```

The exact payload is UTF-8 without BOM:

```text
PS4MANAGER-MOD-RELEASE-V1
releaseId=11111111-1111-4111-8111-111111111111
modId=22222222-2222-4222-8222-222222222222
version=2.0.0
packageType=files
size=5
sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
compatibility=CUSA00411@1.57,CUSA00411@1.58
```

- [ ] **Step 2: Write failing storage and media tests**

Assert that staging streams to disk while hashing, refuses a stream after `maxBytes`, uses `<kind>/<sha256[0..2]>/<sha256>.<extension>` for publication, returns the existing identical artifact for repeated bytes, and never overwrites different bytes. Media tests accept JPEG, PNG, and ISO-BMFF MP4 signatures only when the declared MIME agrees; extension alone never authorizes media.

- [ ] **Step 3: Run tests to verify they fail**

Run: `npm test -- test/unit/release-envelope.test.ts test/unit/artifact-store.test.ts test/unit/media-validator.test.ts`

Expected: FAIL because storage, media, and signing modules do not exist.

- [ ] **Step 4: Implement storage and RSA-SHA256 signing**

`ArtifactStore.stage` writes a random file under `<root>/staging`, updates SHA-256 incrementally, fsyncs, and deletes the partial file on error. `publish` performs a same-volume atomic rename; `EEXIST` is accepted only after re-hashing the existing final file.

`RsaSigner` loads a PEM RSA private key, derives `keyId` as the first 16 lowercase hex characters of SHA-256 over the DER SPKI public key, and signs with RSA PKCS#1 v1.5 plus SHA-256 so the .NET Framework client can verify with `RSACryptoServiceProvider.VerifyData`.

- [ ] **Step 5: Run tests and TypeScript checks**

Run:

```powershell
npm test -- test/unit/release-envelope.test.ts test/unit/artifact-store.test.ts test/unit/media-validator.test.ts
npm run lint
npm run build
```

Expected: all commands exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add server
git commit -m "Add signed artifact storage"
```

### Task 5: Catalog repository and public read API

**Files:**
- Create: `server/src/catalog/catalog-repository.ts`
- Create: `server/src/catalog/catalog-service.ts`
- Create: `server/src/catalog/catalog-routes.ts`
- Create: `server/src/shared/etag.ts`
- Create: `server/src/shared/http-errors.ts`
- Create: `server/test/integration/catalog.integration.test.ts`
- Modify: `server/src/app.ts`

**Interfaces:**
- Produces: `CatalogRepository.listCompatible(titleId: string, gameVersion: string, page: PageRequest): Promise<CatalogPage>`
- Produces: `CatalogRepository.getMod(modId: string): Promise<ModDetail | null>`
- Produces: `registerCatalogRoutes(app: FastifyInstance, service: CatalogService): Promise<void>`

- [ ] **Step 1: Write failing public API integration tests**

Seed one published `CUSA00411@1.57` release, one `CUSA00411@1.58` release, one draft, and one withdrawn release. Verify:

```ts
const response = await app.inject({
  method: "GET",
  url: "/api/v1/catalog?titleId=cusa00411&gameVersion=01.57&page=1&pageSize=24"
});
expect(response.statusCode).toBe(200);
expect(response.json().items.map((item: { releaseId: string }) => item.releaseId))
  .toEqual([published157Id]);
expect(response.headers.etag).toMatch(/^"[a-f0-9]{64}"$/);
```

Repeat with `If-None-Match` and expect `304` with an empty body. Verify an unknown exact pair returns `200` with `items: []`, malformed title/version returns `400`, page size above `100` returns `400`, mod and release detail exclude drafts/withdrawn releases, and no response contains `storage_key`, admin fields, or host paths.

- [ ] **Step 2: Run the integration test to verify it fails**

Run: `npm test -- test/integration/catalog.integration.test.ts`

Expected: FAIL because catalog modules and routes do not exist.

- [ ] **Step 3: Implement deterministic catalog DTOs and queries**

Catalog items expose exactly: `modId`, `slug`, `title`, `author`, `summary`, `category`, `coverUrl`, `releaseId`, `releaseVersion`, `packageType`, `packageSize`, `sha256`, `signature`, `signingKeyId`, `compatibility`, `publishedUtc`, and `detailUrl`. Sort by normalized title then stable mod ID. `GET /api/v1/mods/:modId` additionally exposes description, ordered media URLs, and published releases. `GET /api/v1/releases/:releaseId` exposes changelog, declared manifest operations, signed envelope, and every exact compatibility row.

Artifact download route `GET /api/v1/releases/:releaseId/download` resolves only a published, non-withdrawn release and responds with `X-Accel-Redirect: /_artifacts/<storage-key>`, immutable cache headers, content type, content length, and quoted SHA-256 ETag. Nginx's `/_artifacts/` location is internal in Task 10.

- [ ] **Step 4: Implement strong deterministic ETags**

Serialize public DTOs with properties assembled in fixed order and compute lowercase SHA-256 hex over UTF-8 JSON. `If-None-Match` compares the quoted token exactly. Do not include request time or host-local paths.

- [ ] **Step 5: Run catalog and full server tests**

Run:

```powershell
npm test -- test/integration/catalog.integration.test.ts
npm test
npm run build
```

Expected: all commands exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add server
git commit -m "Add public mod catalog API"
```

### Task 6: Administrator password, TOTP, sessions, CSRF, and bootstrap

**Files:**
- Create: `server/src/auth/password.ts`
- Create: `server/src/auth/totp.ts`
- Create: `server/src/auth/secret-box.ts`
- Create: `server/src/auth/session-repository.ts`
- Create: `server/src/auth/auth-plugin.ts`
- Create: `server/scripts/create-admin.ts`
- Create: `server/test/unit/password.test.ts`
- Create: `server/test/unit/totp.test.ts`
- Create: `server/test/unit/secret-box.test.ts`
- Create: `server/test/integration/auth.integration.test.ts`
- Modify: `server/src/app.ts`

**Interfaces:**
- Produces: `hashPassword(password: string): Promise<string>` and `verifyPassword(password: string, encoded: string): Promise<boolean>`
- Produces: `verifyTotp(secret: Buffer, token: string, now: Date, window: number): boolean`
- Produces: `SecretBox.seal/open`
- Produces: `request.requireAdmin(): Promise<AdminIdentity>` and `request.verifyCsrf(): void`

- [ ] **Step 1: Write failing auth primitive tests**

```ts
it("does not accept the same password against a different salt", async () => {
  const first = await hashPassword("correct horse battery staple");
  const second = await hashPassword("correct horse battery staple");
  expect(first).not.toBe(second);
  expect(await verifyPassword("correct horse battery staple", first)).toBe(true);
  expect(await verifyPassword("wrong", first)).toBe(false);
});

it("accepts only the configured TOTP window", () => {
  const now = new Date("2026-09-28T12:00:00Z");
  const token = totpForTestSecret(testSecret, now);
  expect(verifyTotp(testSecret, token, now, 1)).toBe(true);
  expect(verifyTotp(testSecret, token, new Date(now.getTime() + 90_000), 1)).toBe(false);
});
```

Password encoding is `scrypt$N$r$p$saltBase64$hashBase64` using Node's built-in `scrypt`; verification uses `timingSafeEqual`. `SecretBox` uses AES-256-GCM with a random 12-byte nonce and authenticated ciphertext.

- [ ] **Step 2: Write failing HTTP auth tests**

Test bad password, bad TOTP, rate limiting, secure cookie attributes, expired session, replay after logout, missing CSRF, wrong CSRF, and valid CSRF. The mutation test endpoint exists only in the test app and increments a counter after both guards succeed.

- [ ] **Step 3: Run auth tests to verify they fail**

Run: `npm test -- test/unit/password.test.ts test/unit/totp.test.ts test/unit/secret-box.test.ts test/integration/auth.integration.test.ts`

Expected: FAIL because auth modules do not exist.

- [ ] **Step 4: Implement authentication and bootstrap CLI**

Sessions use 32 random bytes sent as the `ps4manager_admin` cookie; only SHA-256 of the token is stored. Cookies are `Secure`, `HttpOnly`, `SameSite=Strict`, path `/`, and expire with the database row. Login rotates any existing session. Logout deletes it. CSRF tokens are separate 32-byte random values stored with the session and compared in constant time from `_csrf` form field or `x-csrf-token` header.

`create-admin.ts` reads username and password from an interactive TTY, generates a 20-byte TOTP secret, encrypts it, writes the user, and prints one `otpauth://totp/PS4Manager:<username>` URI exactly once. It refuses redirected stdin and refuses to overwrite an existing username.

- [ ] **Step 5: Run auth and full checks**

Run:

```powershell
npm test -- test/unit/password.test.ts test/unit/totp.test.ts test/unit/secret-box.test.ts test/integration/auth.integration.test.ts
npm test
npm run lint
npm run build
```

Expected: all commands exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add server
git commit -m "Secure mod library administration"
```

### Task 7: Drafts, previews, immutable mod publication, and audit

**Files:**
- Create: `server/src/admin/admin-repository.ts`
- Create: `server/src/admin/admin-routes.ts`
- Create: `server/src/admin/views/layout.ejs`
- Create: `server/src/admin/views/login.ejs`
- Create: `server/src/admin/views/mod-list.ejs`
- Create: `server/src/admin/views/mod-edit.ejs`
- Create: `server/src/admin/views/release-edit.ejs`
- Create: `server/src/admin/views/release-review.ejs`
- Create: `server/src/shared/audit-repository.ts`
- Create: `server/src/publishing/mod-publisher.ts`
- Create: `server/src/publishing/withdrawal-service.ts`
- Create: `server/test/integration/admin.integration.test.ts`
- Create: `server/test/integration/mod-publisher.integration.test.ts`
- Modify: `server/src/app.ts`

**Interfaces:**
- Consumes: auth guards, ZIP validator, media validator, artifact store, RSA signer, transaction helper
- Produces: `ModPublisher.publish(request: PublishModRequest, actor: AdminIdentity): Promise<PublishedRelease>`
- Produces: `WithdrawalService.withdraw/restore(releaseId, actor, reason)`

- [ ] **Step 1: Write failing administrator workflow tests**

Exercise through `app.inject` with an authenticated session:

```ts
it("keeps an uploaded release private until explicit publication", async () => {
  const draft = await createDraftThroughAdmin(app, session, validDraftFields);
  await uploadPackageThroughAdmin(app, session, draft.releaseId, validPackagePath);
  expect((await app.inject({ method: "GET", url: catalogUrl })).json().items).toHaveLength(0);

  const review = await app.inject({ method: "GET", url: `/admin/releases/${draft.releaseId}/review`, cookies: session.cookies });
  expect(review.body).toContain("/data/example/config.ini");

  await publishThroughAdmin(app, session, draft.releaseId);
  expect((await app.inject({ method: "GET", url: catalogUrl })).json().items).toHaveLength(1);
});
```

Also verify media ordering, MIME-signature mismatch rejection, exact compatibility normalization, audit rows for create/upload/publish/withdraw/restore, immutable published fields, and forbidden restoration when artifact bytes are missing.

- [ ] **Step 2: Write the injected database-failure test**

Provide a transaction hook that throws after `ArtifactStore.publish` but before commit. Assert the catalog stays empty, the final content-addressed file is an unreferenced orphan, `cleanupOrphans()` removes it after the configured grace period, and staging contains no partial file.

- [ ] **Step 3: Run admin tests to verify they fail**

Run: `npm test -- test/integration/admin.integration.test.ts test/integration/mod-publisher.integration.test.ts`

Expected: FAIL because admin routes and publisher do not exist.

- [ ] **Step 4: Implement draft and publication services**

Draft routes permit editing descriptive metadata, ordered media, release version, changelog, package type, and exact compatibility. Uploads stream directly to staging and enforce limits without buffering whole archives. Review reparses the staged archive and renders every operation. Publish performs this exact order: lock draft row; validate state; validate compatibility; validate ZIP; ensure selected type matches manifest; publish artifact bytes; construct and sign canonical envelope; insert artifact/release compatibility; mark release published; append audit; commit.

If commit fails, the file remains unreachable and cleanup handles it. Never delete a content-addressed file synchronously on transaction failure because another release may already reference the same hash.

Withdrawal and restore update only release state and append audit. Restore rechecks artifact existence and signature. Published metadata forms return `409` rather than mutating an immutable row.

- [ ] **Step 5: Run admin, catalog, and full checks**

Run:

```powershell
npm test -- test/integration/admin.integration.test.ts test/integration/mod-publisher.integration.test.ts test/integration/catalog.integration.test.ts
npm test
npm run lint
npm run build
```

Expected: all commands exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add server
git commit -m "Add curated mod publishing workflow"
```

### Task 8: Offline-signed application update publication and feed

**Files:**
- Create: `server/src/updates/update-manifest.ts`
- Create: `server/src/updates/update-repository.ts`
- Create: `server/src/updates/update-service.ts`
- Create: `server/src/updates/update-routes.ts`
- Create: `server/scripts/sign-update.ts`
- Create: `server/src/admin/views/update-list.ejs`
- Create: `server/src/admin/views/update-upload.ejs`
- Create: `server/test/unit/update-manifest.test.ts`
- Create: `server/test/integration/updates.integration.test.ts`
- Modify: `server/src/admin/admin-routes.ts`
- Modify: `server/src/app.ts`

**Interfaces:**
- Produces: `signedUpdatePayload(manifest: UpdateManifest): Buffer` matching `.NET UpdateManifest.SignedPayload()` byte-for-byte
- Produces: `UpdateService.publish(stagedZip, manifest, actor): Promise<AppRelease>`
- Produces: `GET /api/v1/updates/latest?channel=stable|preview`

- [ ] **Step 1: Write failing signed-payload parity tests**

```ts
it("matches the existing .NET five-line payload", () => {
  expect(signedUpdatePayload({
    version: "1.2.0", channel: "stable",
    url: "https://mods.example.test/artifacts/updates/package.zip",
    sha256: "a".repeat(64), notes: "Changed things", signature: "ignored"
  }).toString("utf8")).toBe(
    "1.2.0\nstable\nhttps://mods.example.test/artifacts/updates/package.zip\n" + "a".repeat(64) + "\nChanged things");
});
```

Use a fixed RSA fixture pair to verify Node accepts a signature that the existing C# verifier accepts. Add the same JSON/signature as a fixture consumed later by the client plan.

- [ ] **Step 2: Write failing update publication tests**

Publish a valid offline-signed update, request stable and preview channels, and assert only the newest matching semantic version appears. Mutate URL, notes, channel, hash, and ZIP bytes one at a time and assert each request returns `400` without a visible release. Assert same `(channel, version)` cannot be replaced and withdrawal reveals the preceding published version.

- [ ] **Step 3: Run update tests to verify they fail**

Run: `npm test -- test/unit/update-manifest.test.ts test/integration/updates.integration.test.ts`

Expected: FAIL because update modules do not exist.

- [ ] **Step 4: Implement offline signing CLI and server verification**

`sign-update.ts` runs only on the maintainer machine. Arguments are `--version`, `--channel`, `--url`, `--notes-file`, `--package`, `--private-key`, and `--out`. It hashes the package, constructs the current five-line payload without a trailing newline, signs RSA-SHA256 PKCS#1 v1.5, and writes the exact JSON contract consumed by the existing .NET updater.

The server has only the PEM update public key. Admin upload stages the ZIP and manifest together, requires HTTPS, validates channel and semantic version, verifies ZIP hash and signature, checks that manifest URL equals the final immutable public URL, then publishes the bytes and database row in the same orphan-safe pattern as mod publication. The public JSON preserves the existing six manifest fields and additionally returns `size` and `publishedUtc`; the current .NET deserializer safely ignores those additions until the client plan exposes them.

- [ ] **Step 5: Run update, full Node, and existing .NET update tests**

Run from `server/`:

```powershell
npm test -- test/unit/update-manifest.test.ts test/integration/updates.integration.test.ts
npm test
npm run build
```

Run from the repository root:

```powershell
dotnet test tests/PS4Manager.Core.Tests/PS4Manager.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Updates
```

Expected: all commands exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add server
git commit -m "Serve offline-signed application updates"
```

### Task 9: Docker Compose, Nginx, TLS handoff, and backup/restore

**Files:**
- Create: `server/Dockerfile`
- Create: `server/compose.yaml`
- Create: `server/.dockerignore`
- Create: `server/.env.example`
- Create: `server/nginx/default.conf.template`
- Create: `server/backup/Dockerfile`
- Create: `server/backup/backup.sh`
- Create: `server/backup/restore.sh`
- Create: `server/OPERATIONS.md`
- Create: `server/test/unit/backup-contract.test.ts`

**Interfaces:**
- Consumes: built `server/dist`, migrations, storage root, Docker secrets
- Produces: four-service production deployment and dated restorable backup archives

- [ ] **Step 1: Write the failing deployment contract test**

The test parses `compose.yaml` as text and verifies the operational invariants without requiring a live VPS:

```ts
it("exposes only nginx and keeps persistent state mounted", () => {
  const compose = readFileSync(new URL("../../compose.yaml", import.meta.url), "utf8");
  expect(compose.match(/ports:/g)).toHaveLength(1);
  expect(compose).toContain("127.0.0.1:8080:80");
  expect(compose).toContain("443:443");
  expect(compose).toContain("mysql_data:/var/lib/mysql");
  expect(compose).toContain("artifact_data:/var/lib/ps4manager");
  expect(compose).not.toContain("3306:3306");
});
```

Also assert the Nginx template marks `/_artifacts/` as `internal`, sets immutable caching, limits request bodies, and proxies `/api/` plus `/admin/` only to `api`.

- [ ] **Step 2: Run the deployment test to verify it fails**

Run: `npm test -- test/unit/backup-contract.test.ts`

Expected: FAIL because deployment files do not exist.

- [ ] **Step 3: Implement the production images and Compose graph**

Use `node:24-bookworm-slim` for build and runtime stages and `mysql:8.4` for the database. The API image copies only production dependencies, `dist`, views, and migrations; runs as an unprivileged user; has a read-only root filesystem and writable `/tmp` plus artifact volume. Compose defines `api`, `mysql`, `nginx`, and `backup`, health-dependent startup, restart policies, private `backend` network, persistent named volumes, and Docker secrets for database password, session secret, TOTP encryption key, mod private key, and update public key.

Map HTTP to `127.0.0.1:8080` for initial certificate issuance and HTTPS to public `443`. `OPERATIONS.md` gives exact one-shot Certbot webroot commands and then enables the port-80 redirect. Certificate files are bind-mounted read-only into Nginx.

- [ ] **Step 4: Implement backup and restore scripts**

`backup.sh` uses `mysqlsh` or `mysqldump --single-transaction --routines --events`, writes to a timestamped temporary directory, snapshots packages/previews plus a SHA-256 inventory, creates the final archive by atomic rename, verifies the archive, then runs `rclone copy` to the configured off-VPS destination. It retains local completed backups for the configured days and never deletes the only completed backup.

`restore.sh` requires the stack to be stopped, verifies the inventory, restores into empty MySQL/artifact volumes, runs migrations, and prints the health-check commands. It refuses a non-empty destination unless invoked with exact flag `--replace-empty-volumes` after verifying those volumes contain no catalog rows or artifact files.

- [ ] **Step 5: Validate Compose and image builds**

Run:

```powershell
npm test -- test/unit/backup-contract.test.ts
docker compose -f compose.yaml config
docker compose -f compose.yaml build
```

Expected: test passes; Compose config and all images build successfully.

- [ ] **Step 6: Start clean containers and verify readiness**

Use a dedicated Compose project name and test-only secrets:

```powershell
docker compose -p ps4manager-mods-test -f compose.yaml up -d
docker compose -p ps4manager-mods-test -f compose.yaml ps
curl.exe --fail http://127.0.0.1:8080/health/live
curl.exe --fail http://127.0.0.1:8080/health/ready
docker compose -p ps4manager-mods-test -f compose.yaml down -v
```

Expected: every service is healthy, both endpoints return success, and the final command removes only the dedicated test project's volumes.

- [ ] **Step 7: Commit**

```powershell
git add server
git commit -m "Deploy mod library server with Docker"
```

### Task 10: Full release verification and server handoff

**Files:**
- Create: `server/test/integration/end-to-end.integration.test.ts`
- Create: `server/test/fixtures/dotnet-update-manifest.json`
- Create: `server/src/maintenance.ts`
- Create: `server/test/integration/maintenance.integration.test.ts`
- Modify: `README.md`
- Modify: `server/OPERATIONS.md`

**Interfaces:**
- Consumes: all server tasks
- Produces: a verified server phase and frozen `/api/v1` contract for the .NET client plan

- [ ] **Step 1: Write the end-to-end integration test**

The test starts disposable MySQL and temp storage, creates an admin through the service boundary, logs in with password/TOTP, creates a mod draft, uploads cover/package, reviews, publishes, reads exact catalog/detail, downloads and verifies bytes/signature, withdraws, confirms catalog removal, restores, and confirms the same artifact URL returns the same bytes.

In the same test process, upload the fixed offline-signed application update fixture and verify stable/preview selection. Assert audit actions in order:

```ts
expect(audit.map(entry => entry.action)).toEqual([
  "admin.login", "mod.create", "media.upload", "release.package.upload",
  "release.publish", "release.withdraw", "release.restore", "update.publish"
]);
```

- [ ] **Step 2: Run the end-to-end test to verify it fails before fixture wiring**

Run: `npm test -- test/integration/end-to-end.integration.test.ts`

Expected: FAIL because the cross-module fixture and final wiring are not complete.

- [ ] **Step 3: Finish composition and documentation**

Wire repositories/services/routes in `buildApp` through explicit `AppDependencies`; production dependencies come from config, while tests inject pool, clock, storage, and key fixtures. Add graceful pool close to app shutdown. Document local development, migration, admin bootstrap, key generation, update signing, publish/withdraw, backup, restore, key rotation, and disaster recovery with exact commands.

`maintenance.ts` runs under a MySQL advisory lock so only one API replica performs cleanup. On startup and then every six hours it deletes expired admin sessions, staging files older than 24 hours, and final content-addressed files that have no artifact row and are older than 24 hours. `maintenance.integration.test.ts` uses an injected clock to prove fresh files and referenced artifacts survive while expired rows and old orphans are removed. Shutdown cancels the timer and awaits any active cleanup pass.

Configure Docker logging with bounded rotation and document the default 14-day operational-log policy plus the VPS log-rotation setting that enforces it. The documentation states exactly which request fields are logged and that query strings, bodies, cookies, authentication headers, TOTP values, and CSRF tokens are excluded.

Update the root README with the server purpose, `server/OPERATIONS.md` link, and the statement that the .NET client integration is delivered by the follow-on plan rather than implied complete here.

- [ ] **Step 4: Run the complete server gate**

From `server/`:

```powershell
npm ci
npm run format:check
npm run lint
npm run build
npm test
npm audit --omit=dev
docker compose -f compose.yaml config
docker compose -f compose.yaml build
```

Expected: every command exits `0`; Vitest reports zero failed tests; npm audit reports no production vulnerability requiring action.

- [ ] **Step 5: Run the existing repository gate**

From the repository root:

```powershell
dotnet test PS4Manager.sln -c Release --no-restore
git diff --check
git status --short
```

Expected: all .NET tests pass; `git diff --check` is empty; status contains only the intended README, operations, fixture, and end-to-end-test changes.

- [ ] **Step 6: Commit the verified server phase**

```powershell
git add README.md server
git commit -m "Complete mod library server MVP"
```

- [ ] **Step 7: Record the next plan boundary**

The next implementation plan must consume the frozen DTOs and fixtures from this phase and cover:

```text
PS4Manager.Core/Mods: API client, signature verification, safe extraction, receipts,
conflict detection, backup-before-write, install/resume/rollback/uninstall, installed-title
and running-game discovery; PS4Manager.App/ModLibraryForm; dashboard/settings integration;
and the console end-to-end probe.
```

Do not add those client files in this server plan's branch review; they receive their own
TDD tasks and review boundary.

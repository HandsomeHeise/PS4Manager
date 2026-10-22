# Public API v1

All public URLs use the configured HTTPS origin. JSON is UTF-8. No public writes,
client accounts or telemetry endpoints exist. Errors have `{ "error": "code" }`.
Private storage paths, staging locations and administrator identities are never
part of public DTOs.

| Endpoint                                                                    | Contract                                                                                                                 |
| --------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `GET /api/v1/catalog?titleId=CUSA00411&gameVersion=1.57&page=1&pageSize=24` | Exact compatibility only; `{page,pageSize,items}`. Page size 1–100. Unknown supported-format pairs return an empty list. |
| `GET /api/v1/mods/:id`                                                      | Published mod details and published releases.                                                                            |
| `GET /api/v1/releases/:id`                                                  | Published or withdrawn release metadata; draft IDs return 404.                                                           |
| `GET /api/v1/releases/:id/download`                                         | Published ZIP bytes; withdrawn/draft IDs return 404.                                                                     |
| `GET /api/v1/media/:id`                                                     | Authorized public PNG/JPEG/MP4 bytes.                                                                                    |
| `GET /api/v1/updates/latest?channel=stable`                                 | Newest published numeric version in `stable` or `preview`; no matching release returns 404.                              |
| `GET /api/v1/updates/packages/:sha256.zip`                                  | Immutable update ZIP referenced by at least one published app release.                                                   |

Catalog items contain `modId`, `slug`, `title`, `author`, `summary`, `category`,
`coverUrl`, `releaseId`, `releaseVersion`, `packageType`, `packageSize`, `sha256`,
`signature`, `signingKeyId`, `compatibility`, `publishedUtc`, `detailUrl`.
Compatibility entries are `{titleId,gameVersion}`. IDs are UUIDv4. A CUSA ID is
uppercase with five digits; numeric game-version components are normalized, not
interpreted as compatibility ranges.

The mod/detail and release/detail projection implementations are frozen in
`src/catalog/catalog-service.ts` and exercised by catalog integration fixtures.
Clients must check release `state`: withdrawn metadata remains accessible for
installed-package receipts, but must not be offered for new installations.

JSON responses use stable ETags and `Cache-Control: no-cache`; a matching
`If-None-Match` returns 304. Artifact responses use SHA-256 ETags and immutable
caching. Nginx serves their bytes through an internal redirect; running the API
without Nginx does not provide a standalone file server. No cache can revoke
copies already downloaded or cached before withdrawal.

## Mod signatures

Package paths use NFC Unicode and portable components of at most 255 UTF-8
bytes. Windows device names, including COM/LPT superscript ¹/²/³ aliases, are
forbidden in every source, archive and destination component. The client must
also check its complete local extraction path and platform-specific limits.

RSA-SHA256, PKCS#1 v1.5, base64 signature. Key IDs are the first 16 lowercase hex
characters of SHA-256 of the public key's DER SPKI. Verify against a pinned trusted
key, never a key supplied alongside an untrusted response. The UTF-8 payload is:

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

There is a final LF after the compatibility line. Hash and size must also match
the downloaded ZIP. Validate `manifest.json`, payload paths and PS4 destination
policy independently on the client before any extraction or installation.

## Existing updater compatibility

The update feed contains the existing six fields `version`, `channel`, `url`,
`sha256`, `notes`, `signature`, plus `size` and `publishedUtc`. The signed payload
is UTF-8 `version + LF + channel + LF + url + LF + sha256 + LF + notes`, with no
extra final LF. Notes are preserved exactly, including their own line breaks.
App-update signing keys are separate from mod keys. Only canonical three-part
numeric versions are published; preview is a separate channel.

`test/fixtures/dotnet-update-manifest.json` and `update-verification.json` are
fixed, non-production parity fixtures. Both Node and the existing compiled C#
`UpdateVerifier` have accepted their signature. The fixture archive is dummy test
data, not a runnable application update.

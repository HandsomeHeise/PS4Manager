# PS4 Manager mod-library server

The server is separate from the desktop application. The follow-on client plan
adds mod discovery, verified installation and receipts to .NET; those features
are not implied by this deployment.

## Local checks

Use Node 24 and Docker Desktop with Linux containers. On Windows, add Docker
Desktop's `resources/bin` directory (including its credential helper) to PATH.

```sh
npm ci
npm run format:check
npm run lint
npm run build
npm test
npm audit --omit=dev
docker compose config
docker compose build
npm run test:deployment
```

The last command creates **two uniquely named disposable Compose projects**,
temporary keys and certificates, checks HTTPS publication/downloads, copies a
backup to a local offsite stand-in, restores into fresh volumes, and removes only
its own containers/volumes. It never contacts a production backup destination.
Do not use these test keys or self-signed certificates in production.

## Provision a Linux VPS

Requirements: Docker Engine with Compose v2 supporting `!override`, a DNS name
pointing to the VPS, inbound TCP 80/443, outbound registry and backup access, and
enough disk space for packages, staging, a complete backup and its verification.
The API and MySQL have no published ports. Only Nginx serves downloads; direct
`/_artifacts/` requests are forbidden. Containers use an internal database network.

The production API trusts exactly one immediate proxy hop from private IPv4 or
IPv6 ULA space. Nginx overwrites the forwarded client address; login quotas are
per external client, not per Nginx container. **Never publish the API port or
attach untrusted containers to its network:** a direct private-network caller
could otherwise forge that header. Public-address peers and earlier forwarded
hops are not trusted. If adding a CDN/proxy ahead of Nginx, explicitly configure
its trusted real-IP boundary before relying on individual client quotas.

From `server/`, copy `.env.example` to `.env`, set the real HTTPS origin and DNS
name, and configure an **encrypted off-VPS rclone remote**. Create its
`secrets/rclone.conf` using `rclone config`; never commit it.

```sh
umask 077
mkdir -p secrets tls acme certificates
openssl rand -hex 32 > secrets/mysql_password
openssl rand -hex 32 > secrets/mysql_root_password
openssl rand 32 > secrets/session_secret
openssl rand 32 > secrets/totp_key
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out secrets/mod_private_key.pem
```

Generate the **separate update key on the maintainer's offline machine**:

```sh
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out update_private_key.pem
openssl pkey -in update_private_key.pem -pubout -out update_public_key.pem
```

Copy only `update_public_key.pem` to the VPS's `secrets/`. The API's UID is 1000.
On Linux, make its mounted secret files readable by that UID, without granting
world access (for example ownership 1000:1000 and mode 0600). MySQL and backup
receive only their necessary secrets; Nginx never receives signing/session keys.
Keep the mod private key, session secret and TOTP encryption key in a separate
encrypted recovery store. Database/artifact backups intentionally exclude them.

## Obtain TLS certificates before starting the full stack

Replace `mods.example.com` and the email below. The temporary webroot-only
Nginx serves public port 80; it needs neither API nor an existing certificate.

```sh
docker run -d --name ps4manager-acme-bootstrap -p 80:80 \
  -v "$PWD/acme:/var/www/acme:ro" \
  -v "$PWD/nginx/acme.conf:/etc/nginx/conf.d/default.conf:ro" nginx:1.28-alpine
docker run --rm -v "$PWD/acme:/var/www/acme" \
  -v "$PWD/certificates:/etc/letsencrypt" certbot/certbot certonly \
  --webroot -w /var/www/acme -d mods.example.com --email admin@example.com --agree-tos
docker stop ps4manager-acme-bootstrap
docker rm ps4manager-acme-bootstrap
install -m 0644 certificates/live/mods.example.com/fullchain.pem tls/fullchain.pem
install -m 0600 certificates/live/mods.example.com/privkey.pem tls/privkey.pem
```

The main Compose graph binds HTTP to localhost:8080 and HTTPS to public 443.
Forward the host's public port 80 to localhost:8080 using the existing host proxy
or an explicitly configured firewall rule. This enables ACME renewals and the
HTTP-to-HTTPS redirect; do not expose the API or MySQL instead. For a VPS without
another host proxy, use a Compose override replacing Nginx's ports with
`["80:80", "443:443"]`. Confirm that port 80 is free first.

Schedule the same webroot Certbot renewal daily. After a successful renewal,
copy the renewed certificate/key into `tls/` as above and run
`docker compose exec nginx nginx -s reload`. Keep the whole Certbot certificate
directory so renewal configuration and symlink targets remain available.

## Logs and startup

The base configuration bounds Docker logs to five 10 MB files per service; this
is a size limit, **not a 14-day age guarantee**. On the production Linux VPS use
the journald override and a retention policy. On a dedicated VPS, create
`/etc/systemd/journald.conf.d/ps4manager-retention.conf`:

```ini
[Journal]
SystemMaxUse=500M
MaxRetentionSec=14day
```

Restart `systemd-journald` after reviewing this host-wide policy. On a shared
host, obtain an equivalent dedicated log destination instead of changing other
applications' retention. Start production with the override consistently:

```sh
docker compose -f compose.yaml -f compose.journald.yaml config
docker compose -f compose.yaml -f compose.journald.yaml up -d --build
docker compose ps
curl --fail http://127.0.0.1:8080/health/ready
curl --fail https://mods.example.com/health/ready
docker compose exec api node dist/admin-main.js
```

Bootstrap is interactive only. Enter a username and a strong password, then save
the one-time TOTP enrollment URI in an authenticator. No default account exists.
Open `/admin/login` over HTTPS. Used TOTP steps cannot be replayed. Session
cookies are Secure/HttpOnly/SameSite=Strict; mutations also require CSRF.

API logs contain sanitized request ID, method/path, status and timing; free-form
messages and sensitive fields are redacted. Nginx access logs contain time,
method, path, status and byte count, not IP/user agent. Query strings, request
bodies, cookies, authorization, TOTP and CSRF values are not access-log fields.
Do not enable debug/request-body logging. Startup errors intentionally omit
secret contents and paths; check secret permissions/config and service health.
Nginx runtime error logging is disabled because its free-form errors can include
unsanitized request URLs. Use `nginx -t`, health checks and sanitized access logs
for diagnosis. The public contract and signing formats are in [API.md](API.md).

## Migrations and publication

Startup applies migrations under a MySQL advisory lock. To migrate explicitly:

Startup and every six hours, maintenance removes expired sessions and unreferenced
staging/final files older than 24 hours. Draft-attached uploads and referenced
artifacts are preserved. A shared database lock excludes publication while
garbage collection runs; shutdown cancels its timer and waits for an active pass.

```sh
docker compose run --rm --no-deps api node dist/migrate-main.js
```

Create a mod draft in `/admin/mods`, enter exact title/version compatibility,
upload PNG/JPEG/MP4 previews and the ZIP, then review every target operation.
Only the explicit publish action signs and exposes a release. Published metadata
and bytes cannot be edited: create a new release. Withdrawal hides a release
from catalog/downloads; restoration rechecks the existing bytes and signature.

Application updates use `/admin/updates`. On the maintainer machine:

```sh
npm run update:sign -- --version 1.2.0 --channel stable \
  --url 'https://mods.example.com/api/v1/updates/packages/{sha256}.zip' \
  --notes-file release-notes.txt --package PS4Manager.zip \
  --private-key /secure/offline/update_private_key.pem --out signed-update.json
```

Use canonical three-part numeric versions. `preview` is a channel, not a version
suffix; the existing .NET `System.Version` parser cannot consume prerelease
suffixes. Upload the exact ZIP and signed JSON together. The tool refuses to
overwrite its output. The server rejects changed notes/URL/version/channel/hash
or bytes and never holds the private update key. Withdrawing an update exposes
the preceding published version in that channel.

## Backup and disaster recovery

The backup service runs once after startup, then daily. It dumps MySQL using a
consistent transaction, copies immutable packages/previews (not staging), writes
SHA-256 inventory entries, creates/verifies an archive and atomically names it,
then copies it with rclone. Retention runs only after a successful offsite copy
and always preserves the newest local archive. Health requires a successful
offsite copy within 36 hours. Monitor Docker health and remote storage capacity.
The dump/copy/inventory phase holds the shared storage lock, excluding publication
and cleanup. Compression and offsite transfer release that lock. A large snapshot
can temporarily make publication time out; retry after the backup phase finishes.

```sh
docker compose exec backup /app/backup/backup.sh
docker compose exec backup cat /backups/latest.ok
```

Restore onto a **fresh installation/project**, using the original recovery keys
and new environment configuration. Bring up MySQL only. Copy/download one archive
into that project's `/backups` volume, preserving its `ps4manager-*.tar.gz` name.
The wrapper refuses running API/Nginx/backup writers. It rejects links/path
traversal and checksum mismatches before database mutation, then requires empty
database/artifact destinations, restores and runs migrations.

```sh
docker compose stop api nginx backup
docker compose up -d mysql
sh backup/restore.sh ps4manager-2026-09-29T01-00-00-000Z.tar.gz
docker compose -f compose.yaml -f compose.journald.yaml up -d
curl --fail http://127.0.0.1:8080/health/ready
```

`--replace-empty-volumes` only permits already-created empty tables/directories;
it **never** permits overwriting rows or artifact files. No force-overwrite mode
exists. If a restore is interrupted after SQL import, preserve its diagnostics
and retry into another fresh destination. Never delete a production volume to
make a restore command pass. Validate known catalog entries, download hashes,
admin login and the update feed before switching DNS/traffic.

Rotate session secrets to invalidate all sessions. TOTP-key rotation requires
re-encrypting every stored admin secret or re-enrolling accounts; do not merely
replace the key. The current mod verifier uses one active key, so retain the
original key for restoring older releases; coordinated key-ring/client support
is a follow-on requirement. Coordinate update public-key rotation with deployed
clients before publishing with a new offline private key.

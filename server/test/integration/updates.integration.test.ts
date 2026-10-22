import { beforeAll, afterAll, expect, it } from "vitest";
import { randomUUID, createHash, sign, constants } from "node:crypto";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import type { FastifyInstance } from "fastify";
import { startMysql } from "../helpers/mysql.js";
import { testConfig } from "../helpers/config.js";
import { testKeys } from "../helpers/keys.js";
import { buildZip } from "../fixtures/build-zips.js";
import { buildApp } from "../../src/app.js";
import { runMigrations } from "../../src/db/migrate.js";
import { SecretBox } from "../../src/auth/secret-box.js";
import { tokenHash } from "../../src/auth/session-repository.js";
import { ArtifactStore } from "../../src/storage/artifact-store.js";
import { RsaSigner } from "../../src/publishing/rsa-signer.js";
let db: Awaited<ReturnType<typeof startMysql>>, root: string, app: FastifyInstance;
let cookies: Record<string, string>,
  address = 10;
const keys = testKeys(),
  token = "b".repeat(64),
  csrf = "c".repeat(64),
  actorId = randomUUID();
beforeAll(async () => {
  db = await startMysql();
  await runMigrations(db.pool, fileURLToPath(new URL("../../migrations", import.meta.url)));
  root = await mkdtemp(join(tmpdir(), "ps4manager-updates-"));
  const now = new Date();
  await db.pool.execute(
    "INSERT INTO admin_users (id,username,password_hash,totp_secret_encrypted) VALUES (?,'updates','unused',?)",
    [actorId, Buffer.alloc(32)]
  );
  await db.pool.execute(
    "INSERT INTO admin_sessions (token_hash,admin_user_id,csrf_token,created_utc,expires_utc) VALUES (?,?,?,?,?)",
    [tokenHash(token), actorId, csrf, now, new Date(now.getTime() + 3600000)]
  );
  app = await buildApp({
    config: testConfig(),
    clock: { now: () => now },
    pool: db.pool,
    auth: { secretBox: new SecretBox(Buffer.alloc(32)), sessionSecret: Buffer.alloc(32, 2) },
    publishing: { store: new ArtifactStore(root), signer: new RsaSigner(testKeys().privateKey) },
    updates: { publicKey: keys.publicKey }
  });
  cookies = { ps4manager_admin: app.signCookie(token) };
}, 240000);
afterAll(async () => {
  if (app) await app.close();
  if (db) await db.stop();
  if (root) await rm(root, { recursive: true, force: true });
}, 60000);
function fixture(version: string, channel = "stable") {
  const bytes = buildZip([{ name: "PS4Manager.exe", content: `${version}:${channel}` }]);
  const sha256 = createHash("sha256").update(bytes).digest("hex");
  const manifest = {
    version,
    channel,
    url: `https://mods.example.test/api/v1/updates/packages/${sha256}.zip`,
    sha256,
    notes: "Offline signiert\nÄnderungen",
    signature: ""
  };
  manifest.signature = sign(
    "RSA-SHA256",
    Buffer.from([version, channel, manifest.url, sha256, manifest.notes].join("\n")),
    { key: keys.privateKey, padding: constants.RSA_PKCS1_PADDING }
  ).toString("base64");
  return { bytes, manifest };
}
function publish(item: ReturnType<typeof fixture>) {
  const parts = [
    Buffer.from(
      '--update\r\nContent-Disposition: form-data; name="manifest"; filename="manifest.json"\r\nContent-Type: application/json\r\n\r\n' +
        JSON.stringify(item.manifest) +
        "\r\n"
    ),
    Buffer.from(
      '--update\r\nContent-Disposition: form-data; name="package"; filename="package.zip"\r\nContent-Type: application/zip\r\n\r\n'
    ),
    item.bytes,
    Buffer.from("\r\n--update--\r\n")
  ];
  return app.inject({
    method: "POST",
    url: "/api/admin/updates",
    cookies,
    remoteAddress: `127.0.0.${address++}`,
    headers: { "x-csrf-token": csrf, "content-type": "multipart/form-data; boundary=update" },
    payload: Buffer.concat(parts)
  });
}
it("selects the newest numeric version in each channel and falls back after withdrawal", async () => {
  for (const item of [fixture("1.9.0"), fixture("1.10.0"), fixture("2.0.0", "preview")])
    expect((await publish(item)).statusCode).toBe(201);
  const latest = await app.inject("/api/v1/updates/latest?channel=stable");
  expect(latest.statusCode).toBe(200);
  expect(latest.json().version).toBe("1.10.0");
  expect((await app.inject("/api/v1/updates/latest?channel=preview")).json().version).toBe("2.0.0");
  expect(
    (
      await app.inject({
        url: "/api/v1/updates/latest?channel=stable",
        headers: { "if-none-match": latest.headers.etag! }
      })
    ).statusCode
  ).toBe(304);
  const again = await publish(fixture("1.10.0"));
  expect(again.statusCode).toBe(409);
  const listing = await app.inject({ url: "/api/admin/updates", cookies });
  const id = listing.json().items.find((r: { version: string }) => r.version === "1.10.0").id;
  expect(
    (
      await app.inject({
        method: "POST",
        url: `/api/admin/updates/${id}/withdraw`,
        cookies,
        headers: { "x-csrf-token": csrf },
        payload: { reason: "Test rollback" }
      })
    ).statusCode
  ).toBe(200);
  expect((await app.inject("/api/v1/updates/latest?channel=stable")).json().version).toBe("1.9.0");
  expect((await app.inject(new URL(latest.json().url).pathname)).statusCode).toBe(404);
});
it.each(["url", "notes", "channel", "sha256", "version", "bytes"])(
  "rejects tampered %s without publishing",
  async (field) => {
    const item = fixture("3.0.0");
    if (field === "bytes") item.bytes = Buffer.concat([item.bytes, Buffer.from("changed")]);
    else if (field === "url") item.manifest.url = "https://other.example/package.zip";
    else if (field === "notes") item.manifest.notes = "changed";
    else if (field === "channel") item.manifest.channel = "preview";
    else if (field === "version") item.manifest.version = "3.0.1";
    else item.manifest.sha256 = "0".repeat(64);
    expect((await publish(item)).statusCode).toBe(400);
    expect((await app.inject("/api/v1/updates/latest?channel=stable")).json().version).toBe(
      "1.9.0"
    );
  }
);

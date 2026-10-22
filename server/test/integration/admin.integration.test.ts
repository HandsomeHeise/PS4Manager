import { beforeAll, afterAll, expect, it } from "vitest";
import { randomUUID } from "node:crypto";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import type { FastifyInstance } from "fastify";
import type { RowDataPacket } from "mysql2/promise";
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
let cookies: Record<string, string>;
const csrf = "c".repeat(64),
  token = "a".repeat(64),
  actorId = randomUUID();
const fields = {
  slug: "test-mod",
  title: "<script>bad()</script>",
  author: "Curator",
  summary: "Example",
  description: "A test mod",
  category: "tools"
};
const release = {
  version: "1.0",
  packageType: "files",
  changelog: "Initial",
  compatibility: [{ titleId: "cusa00411", gameVersion: "01.057" }]
};
const catalogUrl = "/api/v1/catalog?titleId=CUSA00411&gameVersion=1.57";
beforeAll(async () => {
  db = await startMysql();
  await runMigrations(db.pool, fileURLToPath(new URL("../../migrations", import.meta.url)));
  root = await mkdtemp(join(tmpdir(), "ps4manager-admin-"));
  const now = new Date();
  await db.pool.execute(
    "INSERT INTO admin_users (id,username,password_hash,totp_secret_encrypted) VALUES (?,'curator','unused',?)",
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
    publishing: { store: new ArtifactStore(root), signer: new RsaSigner(testKeys().privateKey) }
  });
  cookies = { ps4manager_admin: app.signCookie(token) };
}, 240000);
afterAll(async () => {
  if (app) await app.close();
  if (db) await db.stop();
  if (root) await rm(root, { recursive: true, force: true });
}, 60000);
async function mutate(url: string, payload: object, method: "POST" | "PUT" = "POST") {
  return app.inject({ method, url, cookies, headers: { "x-csrf-token": csrf }, payload });
}
async function upload(url: string, bytes: Buffer, name: string, type: string) {
  return app.inject({
    method: "POST",
    url,
    cookies,
    headers: { "x-csrf-token": csrf, "content-type": "multipart/form-data; boundary=fixture" },
    payload: Buffer.concat([
      Buffer.from(
        `--fixture\r\nContent-Disposition: form-data; name="file"; filename="${name}"\r\nContent-Type: ${type}\r\n\r\n`
      ),
      bytes,
      Buffer.from("\r\n--fixture--\r\n")
    ])
  });
}
it("guards administrator pages and every mutating endpoint", async () => {
  expect((await app.inject("/admin/mods")).statusCode).toBe(401);
  expect(
    (await app.inject({ url: "/api/admin/mods", method: "POST", cookies, payload: fields }))
      .statusCode
  ).toBe(403);
  const loginPage = await app.inject("/admin/login");
  expect(loginPage.statusCode).toBe(200);
  expect(loginPage.body).toMatch(/<form[^>]*method="post"[^>]*>/u);
});
it("keeps uploads private until reviewed and published, then forbids editing", async () => {
  const created = await mutate("/api/admin/mods", fields);
  expect(created.statusCode).toBe(201);
  const modId = created.json().id;
  const draft = await mutate(`/api/admin/mods/${modId}/releases`, release);
  expect(draft.statusCode).toBe(201);
  const releaseId = draft.json().id;
  for (const url of ["/admin/mods", `/admin/mods/${modId}`, `/admin/releases/${releaseId}`]) {
    const page = await app.inject({ url, cookies });
    expect(page.statusCode).toBe(200);
    expect(page.body).not.toContain("<script>bad()</script>");
    expect(page.headers["content-security-policy"]).toContain("frame-ancestors 'none'");
  }
  const zip = buildZip([
    {
      name: "manifest.json",
      content: JSON.stringify({
        formatVersion: 1,
        packageType: "files",
        operations: [{ source: "payload/config.ini", target: "/data/example/config.ini" }]
      })
    },
    { name: "payload/config.ini", content: "config" }
  ]);
  expect(
    (
      await upload(
        `/api/admin/releases/${releaseId}/package`,
        zip,
        "package.zip",
        "application/zip"
      )
    ).statusCode
  ).toBe(200);
  expect((await app.inject(catalogUrl)).json().items).toHaveLength(0);
  const review = await app.inject({ url: `/admin/releases/${releaseId}/review`, cookies });
  expect(review.statusCode).toBe(200);
  expect(review.body).toContain("/data/example/config.ini");
  expect(review.body).toContain("CUSA00411");
  expect(review.body).not.toContain("<script>bad()</script>");
  const badPreview = await upload(
    `/api/admin/mods/${modId}/media?kind=cover&position=0`,
    Buffer.from("not a png"),
    "cover.png",
    "image/png"
  );
  expect(badPreview.statusCode).toBe(400);
  for (const [position, kind] of [
    [1, "screenshot"],
    [0, "cover"]
  ] as const) {
    expect(
      (
        await upload(
          `/api/admin/mods/${modId}/media?kind=${kind}&position=${position}`,
          Buffer.concat([Buffer.from("89504e470d0a1a0a", "hex"), Buffer.alloc(32, position)]),
          "cover.png",
          "image/png"
        )
      ).statusCode
    ).toBe(201);
  }
  const [mediaBefore] = await db.pool.query<RowDataPacket[]>(
    "SELECT id FROM mod_media WHERE mod_id=? ORDER BY position",
    [modId]
  );
  const order = mediaBefore.map((m) => m.id).reverse();
  expect((await mutate(`/api/admin/mods/${modId}/media`, { order }, "PUT")).statusCode).toBe(200);
  expect(
    (await mutate(`/api/admin/mods/${modId}/media`, { order: [order[0]] }, "PUT")).statusCode
  ).toBe(400);
  expect((await mutate(`/api/admin/releases/${releaseId}/publish`, {})).statusCode).toBe(200);
  expect((await app.inject(catalogUrl)).json().items).toHaveLength(1);
  const [media] = await db.pool.query<RowDataPacket[]>(
    "SELECT kind FROM mod_media WHERE mod_id=? ORDER BY position",
    [modId]
  );
  expect(media.map((m) => m.kind)).toEqual(["screenshot", "cover"]);
  expect(
    (await mutate(`/api/admin/releases/${releaseId}`, { ...release, version: "2.0" }, "PUT"))
      .statusCode
  ).toBe(409);
  expect(
    (
      await upload(
        `/api/admin/releases/${releaseId}/package`,
        zip,
        "package.zip",
        "application/zip"
      )
    ).statusCode
  ).toBe(409);
  expect(
    (await mutate(`/api/admin/releases/${releaseId}/withdraw`, { reason: "Review compatibility" }))
      .statusCode
  ).toBe(200);
  expect((await app.inject(catalogUrl)).json().items).toHaveLength(0);
  expect((await mutate(`/api/admin/releases/${releaseId}/restore`, {})).statusCode).toBe(200);
  const [audit] = await db.pool.query<RowDataPacket[]>(
    "SELECT action FROM audit_entries WHERE admin_user_id=?",
    [actorId]
  );
  expect(audit.map((a) => a.action)).toEqual(
    expect.arrayContaining([
      "mod.create",
      "release.create",
      "release.upload",
      "media.upload",
      "release.publish",
      "release.withdraw",
      "release.restore"
    ])
  );
});
it("rate limits costly publication attempts", async () => {
  let status = 0;
  for (let i = 0; i < 7; i++) {
    const response = await app.inject({
      method: "POST",
      url: `/api/admin/releases/${randomUUID()}/publish`,
      remoteAddress: "127.0.0.42",
      cookies,
      headers: { "x-csrf-token": csrf },
      payload: {}
    });
    status = response.statusCode;
  }
  expect(status).toBe(429);
});

import { beforeAll, afterAll, expect, it } from "vitest";
import { randomUUID } from "node:crypto";
import { fileURLToPath } from "node:url";
import type { FastifyInstance } from "fastify";
import { startMysql } from "../helpers/mysql.js";
import { testConfig } from "../helpers/config.js";
import { runMigrations } from "../../src/db/migrate.js";
import { buildApp } from "../../src/app.js";
import { SystemClock } from "../../src/clock.js";
let db: Awaited<ReturnType<typeof startMysql>>, app: FastifyInstance;
const modId = randomUUID(),
  releaseIds = Array.from({ length: 4 }, () => randomUUID());
beforeAll(async () => {
  db = await startMysql();
  await runMigrations(db.pool, fileURLToPath(new URL("../../migrations", import.meta.url)));
  await db.pool.execute(
    "INSERT INTO mods (id,slug,title,author,summary,description,category,state) VALUES (?, 'example','Example','Author','Summary','Description','misc','published')",
    [modId]
  );
  for (let i = 0; i < 4; i++) {
    const artifactId = randomUUID(),
      sha256 = String(i).repeat(64);
    await db.pool.execute(
      "INSERT INTO artifacts (id,sha256,storage_key,content_type,size_bytes) VALUES (?,?,?,'application/zip',123)",
      [artifactId, sha256, `packages/mods/${sha256.slice(0, 2)}/${sha256}.zip`]
    );
    await db.pool.execute(
      "INSERT INTO mod_releases (id,mod_id,version,package_type,changelog,artifact_id,manifest,signed_envelope,signature,signing_key_id,state,published_utc,staged_path) VALUES (?,?,?,'files','Changes',?,?,'envelope','signature','0123456789abcdef',?,'2026-09-28 12:00:00','/private/staging')",
      [
        releaseIds[i],
        modId,
        `1.${i}.0`,
        artifactId,
        JSON.stringify({
          formatVersion: 1,
          packageType: "files",
          operations: [{ source: "payload/a", target: "/data/a" }]
        }),
        ["published", "published", "draft", "withdrawn"][i]
      ]
    );
    await db.pool.execute(
      "INSERT INTO release_compatibility (id,release_id,title_id,game_version) VALUES (?,?,'CUSA00411',?)",
      [randomUUID(), releaseIds[i], i === 1 ? "1.58" : "1.57"]
    );
  }
  app = await buildApp({ config: testConfig(), clock: new SystemClock(), pool: db.pool });
}, 240_000);
afterAll(async () => {
  if (app) await app.close();
  if (db) await db.stop();
}, 60_000);
it("filters by exact normalized compatibility, omits private fields and validates ETags", async () => {
  const url = "/api/v1/catalog?titleId=cusa00411&gameVersion=01.57&page=1&pageSize=24";
  const response = await app.inject(url);
  expect(response.statusCode).toBe(200);
  expect(response.json().items.map((x: { releaseId: string }) => x.releaseId)).toEqual([
    releaseIds[0]
  ]);
  expect(Object.keys(response.json().items[0]).sort()).toEqual(
    [
      "modId",
      "slug",
      "title",
      "author",
      "summary",
      "category",
      "coverUrl",
      "releaseId",
      "releaseVersion",
      "packageType",
      "packageSize",
      "sha256",
      "signature",
      "signingKeyId",
      "compatibility",
      "publishedUtc",
      "detailUrl"
    ].sort()
  );
  expect(response.headers.etag).toMatch(/^"[a-f0-9]{64}"$/);
  for (const secret of ["storage_key", "/private", "admin_user", "staged_path"])
    expect(response.body).not.toContain(secret);
  const unchanged = await app.inject({ url, headers: { "if-none-match": response.headers.etag! } });
  expect(unchanged.statusCode).toBe(304);
  expect(unchanged.body).toBe("");
  expect(
    (await app.inject("/api/v1/catalog?titleId=CUSA99999&gameVersion=1.00")).json().items
  ).toEqual([]);
});
it.each([
  "titleId=bad&gameVersion=1.00",
  "titleId=CUSA00411&gameVersion=bad",
  "titleId=CUSA00411&gameVersion=1.57&pageSize=101",
  "titleId=CUSA00411&gameVersion=1.57&page=0",
  "titleId=CUSA00411&gameVersion=1.57&page=1.2"
])("rejects invalid query %s", async (query) => {
  expect((await app.inject("/api/v1/catalog?" + query)).statusCode).toBe(400);
});
it("exposes published mod details and preserves withdrawn receipt metadata without downloads", async () => {
  const mod = await app.inject(`/api/v1/mods/${modId}`);
  expect(mod.statusCode).toBe(200);
  expect(
    mod
      .json()
      .releases.map((x: { releaseId: string }) => x.releaseId)
      .sort()
  ).toEqual(releaseIds.slice(0, 2).sort());
  expect((await app.inject(`/api/v1/releases/${releaseIds[2]}`)).statusCode).toBe(404);
  const withdrawn = await app.inject(`/api/v1/releases/${releaseIds[3]}`);
  expect(withdrawn.statusCode).toBe(200);
  expect(withdrawn.json().state).toBe("withdrawn");
  expect((await app.inject(`/api/v1/releases/${releaseIds[3]}/download`)).statusCode).toBe(404);
});
it("authorizes an internal immutable download only for a published release", async () => {
  const response = await app.inject(`/api/v1/releases/${releaseIds[0]}/download`);
  expect(response.statusCode).toBe(200);
  expect(response.headers["x-accel-redirect"]).toBe(
    `/_artifacts/packages/mods/00/${"0".repeat(64)}.zip`
  );
  expect(response.headers["content-type"]).toBe("application/zip");
  expect(response.headers["content-length"]).toBe("123");
  expect(response.headers.etag).toBe(`"${"0".repeat(64)}"`);
  expect(response.headers["cache-control"]).toContain("immutable");
});
it("serves previews referenced by a published mod and rejects private previews", async () => {
  const artifactId = randomUUID(),
    mediaId = randomUUID(),
    hash = "f".repeat(64);
  await db.pool.execute(
    "INSERT INTO artifacts (id,sha256,storage_key,content_type,size_bytes) VALUES (?,?,?,'image/png',12)",
    [artifactId, hash, `previews/ff/${hash}.png`]
  );
  await db.pool.execute(
    "INSERT INTO mod_media (id,mod_id,artifact_id,kind,position) VALUES (?,?,?,'cover',0)",
    [mediaId, modId, artifactId]
  );
  const response = await app.inject(`/api/v1/media/${mediaId}`);
  expect(response.statusCode).toBe(200);
  expect(response.headers["x-accel-redirect"]).toBe(`/_artifacts/previews/ff/${hash}.png`);
  await db.pool.execute("UPDATE mods SET state='draft' WHERE id=?", [modId]);
  try {
    expect((await app.inject(`/api/v1/media/${mediaId}`)).statusCode).toBe(404);
  } finally {
    await db.pool.execute("UPDATE mods SET state='published' WHERE id=?", [modId]);
  }
});

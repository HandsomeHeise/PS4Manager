import { beforeAll, afterAll, it, expect } from "vitest";
import { randomUUID } from "node:crypto";
import { mkdtemp, rm, readdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { Readable } from "node:stream";
import type { RowDataPacket } from "mysql2/promise";
import { startMysql } from "../helpers/mysql.js";
import { runMigrations } from "../../src/db/migrate.js";
import { ArtifactStore } from "../../src/storage/artifact-store.js";
import { runMaintenance } from "../../src/maintenance.js";
let db: Awaited<ReturnType<typeof startMysql>>, root: string;
beforeAll(async () => {
  db = await startMysql();
  await runMigrations(db.pool, fileURLToPath(new URL("../../migrations", import.meta.url)));
  root = await mkdtemp(join(tmpdir(), "ps4manager-maintenance-"));
}, 240000);
afterAll(async () => {
  if (db) await db.stop();
  if (root) await rm(root, { recursive: true, force: true });
}, 60000);
it("retains fresh files and referenced artifacts while deleting expired sessions and old orphans", async () => {
  const store = new ArtifactStore(root),
    now = new Date(),
    actor = randomUUID();
  const stage = () => store.stage(Readable.from(randomUUID()), "package.zip", 1000);
  const oldStage = await stage(),
    orphan = await store.publish(await stage(), "mods"),
    referenced = await store.publish(await stage(), "mods");
  await db.pool.execute(
    "INSERT INTO artifacts (id,sha256,storage_key,content_type,size_bytes) VALUES (?,?,?,'application/zip',?)",
    [randomUUID(), referenced.sha256, referenced.storageKey, referenced.size]
  );
  await db.pool.execute(
    "INSERT INTO admin_users (id,username,password_hash,totp_secret_encrypted) VALUES (?,'cleanup','unused',?)",
    [actor, Buffer.alloc(32)]
  );
  for (const [hash, expiry] of [
    ["a".repeat(64), new Date(now.getTime() - 1000)],
    ["b".repeat(64), new Date(now.getTime() + 7 * 86400000)]
  ] as const)
    await db.pool.execute(
      "INSERT INTO admin_sessions (token_hash,admin_user_id,csrf_token,expires_utc) VALUES (?,?,?,?)",
      [hash, actor, "c".repeat(64), expiry]
    );
  await runMaintenance(db.pool, store, { now: () => now });
  await expect(store.inspect(orphan.storageKey)).resolves.toBeDefined();
  expect(await readdir(join(root, "staging"))).toContain(oldStage.path.split(/[\\/]/u).pop());
  const future = new Date(now.getTime() + 2 * 86400000);
  await Promise.all([
    runMaintenance(db.pool, store, { now: () => future }),
    runMaintenance(db.pool, store, { now: () => future })
  ]);
  expect(await readdir(join(root, "staging"))).toEqual([]);
  await expect(store.inspect(orphan.storageKey)).rejects.toThrow();
  await expect(store.inspect(referenced.storageKey)).resolves.toBeDefined();
  const [sessions] = await db.pool.query<RowDataPacket[]>("SELECT token_hash FROM admin_sessions");
  expect(sessions.map((s) => s.token_hash)).toEqual(["b".repeat(64)]);
});

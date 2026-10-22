import { beforeAll, afterAll, expect, it } from "vitest";
import { randomUUID, verify, constants } from "node:crypto";
import { mkdtemp, rm, readdir, unlink } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { Readable } from "node:stream";
import type { RowDataPacket } from "mysql2/promise";
import { startMysql } from "../helpers/mysql.js";
import { testKeys } from "../helpers/keys.js";
import { buildZip } from "../fixtures/build-zips.js";
import { runMigrations } from "../../src/db/migrate.js";
import { ArtifactStore } from "../../src/storage/artifact-store.js";
import { RsaSigner } from "../../src/publishing/rsa-signer.js";
import { ModPublisher } from "../../src/publishing/mod-publisher.js";
import { WithdrawalService } from "../../src/publishing/withdrawal-service.js";
import { cleanupOrphans } from "../../src/storage/cleanup.js";
let db: Awaited<ReturnType<typeof startMysql>>, root: string, store: ArtifactStore;
const keys = testKeys(),
  signer = new RsaSigner(keys.privateKey);
const actor = { id: randomUUID(), username: "publisher" };
const now = new Date("2026-09-29T12:00:00Z"),
  clock = { now: () => now };
const limits = {
  maxPackageBytes: 2_000_000,
  maxExpandedBytes: 4_000_000,
  maxEntries: 100,
  maxCompressionRatio: 100
};
const bytes = (text: string) =>
  buildZip([
    {
      name: "manifest.json",
      content: JSON.stringify({
        formatVersion: 1,
        packageType: "files",
        operations: [{ source: "payload/config.ini", target: "/data/example/config.ini" }]
      })
    },
    { name: "payload/config.ini", content: text }
  ]);
beforeAll(async () => {
  db = await startMysql();
  await runMigrations(db.pool, fileURLToPath(new URL("../../migrations", import.meta.url)));
  root = await mkdtemp(join(tmpdir(), "ps4manager-publish-"));
  store = new ArtifactStore(root);
  await db.pool.execute(
    "INSERT INTO admin_users (id,username,password_hash,totp_secret_encrypted) VALUES (?,?,'unused',?)",
    [actor.id, actor.username, Buffer.alloc(32)]
  );
}, 240_000);
afterAll(async () => {
  if (db) await db.stop();
  if (root) await rm(root, { recursive: true, force: true });
}, 60_000);
async function draft(text: string, packageType = "files") {
  const modId = randomUUID(),
    releaseId = randomUUID();
  const staged = await store.stage(
    Readable.from(bytes(text)),
    "package.zip",
    limits.maxPackageBytes
  );
  await db.pool.execute(
    "INSERT INTO mods (id,slug,title,author,summary,description,category) VALUES (?,?,'Title','Author','Summary','Description','misc')",
    [modId, modId]
  );
  await db.pool.execute(
    "INSERT INTO mod_releases (id,mod_id,version,package_type,changelog,staged_path) VALUES (?,?,'1.0',?,'Initial',?)",
    [releaseId, modId, packageType, staged.path]
  );
  await db.pool.execute(
    "INSERT INTO release_compatibility (id,release_id,title_id,game_version) VALUES (?,?,'CUSA00411','1.57')",
    [randomUUID(), releaseId]
  );
  return { modId, releaseId, staged };
}
function publisher(afterArtifactPublish?: () => Promise<void>) {
  return new ModPublisher({
    pool: db.pool,
    store,
    signer,
    clock,
    limits,
    ...(afterArtifactPublish ? { afterArtifactPublish } : {})
  });
}
it("publishes once, signs exact bytes and compatibility, and records audit atomically", async () => {
  const item = await draft("valid");
  const published = await publisher().publish({ releaseId: item.releaseId }, actor);
  expect(published.releaseId).toBe(item.releaseId);
  const [rows] = await db.pool.query<RowDataPacket[]>(
    "SELECT r.*,a.storage_key,a.sha256,a.size_bytes,m.state AS mod_state FROM mod_releases r JOIN artifacts a ON a.id=r.artifact_id JOIN mods m ON m.id=r.mod_id WHERE r.id=?",
    [item.releaseId]
  );
  expect(rows[0].state).toBe("published");
  expect(rows[0].mod_state).toBe("published");
  expect(rows[0].staged_path).toBeNull();
  expect(rows[0].signed_envelope).toContain("compatibility=CUSA00411@1.57\n");
  expect(
    verify(
      "RSA-SHA256",
      Buffer.from(rows[0].signed_envelope),
      { key: keys.publicKey, padding: constants.RSA_PKCS1_PADDING },
      Buffer.from(rows[0].signature, "base64")
    )
  ).toBe(true);
  await expect(publisher().publish({ releaseId: item.releaseId }, actor)).rejects.toMatchObject({
    statusCode: 409
  });
  const [audit] = await db.pool.query<RowDataPacket[]>(
    "SELECT action FROM audit_entries WHERE subject=?",
    [item.releaseId]
  );
  expect(audit.map((r) => r.action)).toEqual(["release.publish"]);
});
it("rejects mismatched package types before making bytes public", async () => {
  const item = await draft("wrong-type", "luma-script");
  await expect(publisher().publish({ releaseId: item.releaseId }, actor)).rejects.toMatchObject({
    statusCode: 400
  });
});
it("leaves a failed-commit artifact unreachable and cleans only old unreferenced files", async () => {
  const item = await draft("orphan");
  await expect(
    publisher(async () => {
      throw new Error("injected failure");
    }).publish({ releaseId: item.releaseId }, actor)
  ).rejects.toThrow("injected failure");
  const [rows] = await db.pool.query<RowDataPacket[]>(
    "SELECT state,artifact_id FROM mod_releases WHERE id=?",
    [item.releaseId]
  );
  expect(rows[0]).toMatchObject({ state: "draft", artifact_id: null });
  const key = `packages/mods/${item.staged.sha256.slice(0, 2)}/${item.staged.sha256}.zip`;
  expect(await store.inspect(key)).toMatchObject({ sha256: item.staged.sha256 });
  expect(await readdir(join(root, "staging"))).not.toContain(
    item.staged.path.split(/[\\/]/u).pop()
  );
  expect(await cleanupOrphans(db.pool, store, new Date(), 86_400_000)).not.toContain(key);
  expect(
    await cleanupOrphans(db.pool, store, new Date(Date.now() + 2 * 86_400_000), 86_400_000)
  ).toContain(key);
  const [referenced] = await db.pool.query<RowDataPacket[]>("SELECT storage_key FROM artifacts");
  for (const row of referenced) await expect(store.inspect(row.storage_key)).resolves.toBeDefined();
});
it("withdraws and restores without resigning, and rejects missing or altered artifacts", async () => {
  const item = await draft("withdraw");
  await publisher().publish({ releaseId: item.releaseId }, actor);
  const withdrawal = new WithdrawalService({ pool: db.pool, store, signer, clock });
  await withdrawal.withdraw(item.releaseId, actor, "Compatibility issue");
  await withdrawal.restore(item.releaseId, actor);
  const [audit] = await db.pool.query<RowDataPacket[]>(
    "SELECT action FROM audit_entries WHERE subject=? ORDER BY created_utc,id",
    [item.releaseId]
  );
  expect(audit.map((r) => r.action).sort()).toEqual([
    "release.publish",
    "release.restore",
    "release.withdraw"
  ]);
  await withdrawal.withdraw(item.releaseId, actor, "Missing file");
  const [original] = await db.pool.query<RowDataPacket[]>(
    "SELECT signature FROM mod_releases WHERE id=?",
    [item.releaseId]
  );
  await db.pool.execute("UPDATE mod_releases SET signature=? WHERE id=?", [
    Buffer.alloc(256).toString("base64"),
    item.releaseId
  ]);
  await expect(withdrawal.restore(item.releaseId, actor)).rejects.toMatchObject({
    statusCode: 409
  });
  await db.pool.execute("UPDATE mod_releases SET signature=? WHERE id=?", [
    original[0].signature,
    item.releaseId
  ]);
  await unlink(
    join(root, `packages/mods/${item.staged.sha256.slice(0, 2)}/${item.staged.sha256}.zip`)
  );
  await expect(withdrawal.restore(item.releaseId, actor)).rejects.toMatchObject({
    statusCode: 409
  });
});
it("serializes more simultaneous publishers than pool connections without starving the lock owner", async () => {
  const item = await draft("concurrent");
  const results = await Promise.allSettled(
    Array.from({ length: 12 }, () => publisher().publish({ releaseId: item.releaseId }, actor))
  );
  expect(results.filter((r) => r.status === "fulfilled")).toHaveLength(1);
  for (const result of results)
    if (result.status === "rejected") expect(result.reason).toMatchObject({ statusCode: 409 });
});
